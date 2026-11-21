using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>Snowflake SQL API v2: SHOW USERS (MFA / SSO) and network policies (open allow lists).</summary>
public sealed partial class SnowflakeConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "snowflake";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var account = call.Require("account");
        if (!AccountPattern().IsMatch(account))
        {
            throw new ConnectorException("unsafe_url");
        }

        var endpoint = Https(account + ".snowflakecomputing.com") + "/api/v2/statements";
        var token = call.Require("token");
        Action<HttpRequestMessage> auth = request =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("X-Snowflake-Authorization-Token-Type", "PROGRAMMATIC_ACCESS_TOKEN");
        };
        var warehouse = call.Optional("warehouse");
        var resources = new List<NormalizedResource>();
        foreach (var row in await RunAsync(call, endpoint, auth, warehouse, "SHOW USERS", cancellationToken))
        {
            var name = Value(row, "name") ?? "user";
            var email = Value(row, "email") ?? Value(row, "login_name") ?? name;
            var mfa = Value(row, "has_mfa") == "true" || Value(row, "ext_authn_duo") == "true" || Value(row, "has_password") == "false";
            resources.Add(call.Resource(
                "user",
                email.ToLowerInvariant(),
                Value(row, "display_name") ?? name,
                ("email", email),
                ("mfa", mfa),
                ("status", Value(row, "disabled") == "true" ? "suspended" : "active"),
                ("offboarded", false),
                ("department", null),
                ("role", Value(row, "default_role"))));
        }

        foreach (var policy in await RunAsync(call, endpoint, auth, warehouse, "SHOW NETWORK POLICIES", cancellationToken))
        {
            var name = Value(policy, "name");
            if (name is null)
            {
                continue;
            }

            var details = await RunAsync(call, endpoint, auth, warehouse, "DESCRIBE NETWORK POLICY \"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"", cancellationToken);
            var allowed = details.FirstOrDefault(row => Value(row, "name") == "ALLOWED_IP_LIST") is { } entry ? Value(entry, "value") ?? string.Empty : string.Empty;
            resources.Add(call.Resource(
                "firewall",
                name.ToLowerInvariant(),
                name,
                ("open", allowed.Length == 0 || allowed.Contains("0.0.0.0/0", StringComparison.Ordinal)),
                ("allowed", allowed)));
        }

        return resources;
    }

    private static async Task<IReadOnlyList<Dictionary<string, string?>>> RunAsync(
        ConnectorCall call,
        string endpoint,
        Action<HttpRequestMessage> auth,
        string? warehouse,
        string statement,
        CancellationToken cancellationToken)
    {
        var result = await call.PostJsonAsync<SnowflakeResult>(endpoint, new { statement, warehouse, timeout = 60 }, auth, cancellationToken);
        var columns = result.ResultSetMetaData?.RowType?.Select(column => column.Name.ToLowerInvariant()).ToArray() ?? [];
        return (result.Data ?? [])
            .Select(row => columns
                .Select((column, index) => (column, value: index < row.Length ? Read(row[index]) : null))
                .ToDictionary(pair => pair.column, pair => pair.value, StringComparer.Ordinal))
            .ToArray();
    }

    private static string? Value(Dictionary<string, string?> row, string column) =>
        row.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex AccountPattern();
}

/// <summary>MongoDB Atlas Admin API v2 (service account): project clusters and IP access list.</summary>
public sealed class MongoAtlasConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://cloud.mongodb.com";
    private const string Version = "application/vnd.atlas.2024-08-05+json";

    public override string Key => "mongodb-atlas";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var token = await call.PostFormAsync<OAuthToken>(
            Api + "/api/oauth/token",
            new Dictionary<string, string> { ["grant_type"] = "client_credentials" },
            Basic(call.Require("clientId"), call.Require("clientSecret")),
            cancellationToken);
        Action<HttpRequestMessage> auth = request =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(Version));
        };
        var group = Escape(call.Require("groupId"));
        var access = await call.GetAsync<AtlasPage<AtlasAccess>>($"{Api}/api/atlas/v2/groups/{group}/accessList?itemsPerPage=500", auth, cancellationToken);
        var isPublic = access.Results.Any(entry => entry.CidrBlock is "0.0.0.0/0" or "::/0");
        var clusters = await call.GetAsync<AtlasPage<AtlasCluster>>($"{Api}/api/atlas/v2/groups/{group}/clusters?itemsPerPage=500", auth, cancellationToken);
        return clusters.Results
            .Select(cluster => call.Resource(
                "database",
                cluster.Name,
                cluster.Name,
                ("encrypted", true),
                ("customerKey", cluster.EncryptionAtRestProvider is not null and not "NONE"),
                ("public", isPublic),
                ("tier", cluster.ReplicationSpecs?
                    .SelectMany(spec => spec.RegionConfigs ?? [])
                    .Select(region => region.ElectableSpecs?.InstanceSize)
                    .FirstOrDefault(size => size is not null)),
                ("version", cluster.MongoDBVersion)))
            .ToArray();
    }
}

/// <summary>Supabase Management API v1: projects, SSL enforcement and database network restrictions.</summary>
public sealed class SupabaseConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.supabase.com/v1";

    public override string Key => "supabase";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Bearer(call.Require("accessToken"));
        var projects = await call.GetAsync<SupabaseProject[]>($"{Api}/projects", auth, cancellationToken);
        var resources = new List<NormalizedResource>();
        foreach (var project in projects)
        {
            var reference = Escape(project.Id);
            var ssl = await call.GetAsync<SupabaseSsl>($"{Api}/projects/{reference}/ssl-enforcement", auth, cancellationToken);
            var network = await call.GetAsync<SupabaseNetwork>($"{Api}/projects/{reference}/network-restrictions", auth, cancellationToken);
            var cidrs = network.Config?.DbAllowedCidrs ?? [];
            resources.Add(call.Resource(
                "database",
                project.Name,
                project.Name,
                ("encrypted", true),
                ("public", cidrs.Length == 0 || cidrs.Contains("0.0.0.0/0")),
                ("sslEnforced", ssl.CurrentConfig?.Database == true)));
        }

        return resources;
    }
}

internal sealed record SnowflakeResult(SnowflakeMeta? ResultSetMetaData, JsonElement[][]? Data);

internal sealed record SnowflakeMeta(SnowflakeColumn[]? RowType);

internal sealed record SnowflakeColumn(string Name);

internal sealed record AtlasPage<T>(T[] Results);

internal sealed record AtlasAccess(string? CidrBlock, string? IpAddress);

internal sealed record AtlasCluster(
    string Name,
    string? EncryptionAtRestProvider,
    [property: JsonPropertyName("mongoDBVersion")] string? MongoDBVersion,
    AtlasReplication[]? ReplicationSpecs);

internal sealed record AtlasReplication(AtlasRegion[]? RegionConfigs);

internal sealed record AtlasRegion(AtlasHardware? ElectableSpecs);

internal sealed record AtlasHardware(string? InstanceSize);

internal sealed record SupabaseProject(string Id, string Name);

internal sealed record SupabaseSsl(SupabaseSslConfig? CurrentConfig);

internal sealed record SupabaseSslConfig(bool Database);

internal sealed record SupabaseNetwork(SupabaseNetworkConfig? Config);

internal sealed record SupabaseNetworkConfig(string[]? DbAllowedCidrs);
