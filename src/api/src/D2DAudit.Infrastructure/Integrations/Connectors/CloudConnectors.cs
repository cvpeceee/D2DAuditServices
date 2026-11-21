using System.Text.Json;
using System.Text.Json.Serialization;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>Cloudflare API v4: account zones with SSL settings and account members.</summary>
public sealed class CloudflareConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.cloudflare.com/client/v4";

    public override string Key => "cloudflare";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Bearer(call.Require("apiToken"));
        var account = Escape(call.Require("accountId"));
        var resources = new List<NormalizedResource>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var zones = await call.GetAsync<CloudflarePage<CloudflareZone>>($"{Api}/zones?account.id={account}&per_page=50&page={page}", auth, cancellationToken);
            foreach (var zone in zones.Result)
            {
                var id = Escape(zone.Id);
                var ssl = await call.GetAsync<CloudflareSetting>($"{Api}/zones/{id}/settings/ssl", auth, cancellationToken);
                var https = await call.GetAsync<CloudflareSetting>($"{Api}/zones/{id}/settings/always_use_https", auth, cancellationToken);
                var minimum = await call.GetAsync<CloudflareSetting>($"{Api}/zones/{id}/settings/min_tls_version", auth, cancellationToken);
                var mode = Read(ssl.Result.Value) ?? "off";
                var always = Read(https.Result.Value) == "on";
                var tls = Read(minimum.Result.Value) ?? "1.0";
                resources.Add(call.Resource(
                    "domain",
                    zone.Name,
                    zone.Name,
                    ("tls", mode is "full" or "strict" && always && string.CompareOrdinal(tls, "1.2") >= 0),
                    ("sslMode", mode),
                    ("alwaysHttps", always),
                    ("minTls", tls),
                    ("verified", zone.Status == "active")));
            }

            if (page >= (zones.ResultInfo?.TotalPages ?? 1))
            {
                break;
            }
        }

        for (var page = 1; page <= MaxPages; page++)
        {
            var members = await call.GetAsync<CloudflarePage<CloudflareMember>>($"{Api}/accounts/{account}/members?per_page=50&page={page}", auth, cancellationToken);
            resources.AddRange(members.Result.Where(member => member.User?.Email is not null).Select(member => call.Resource(
                "user",
                member.User!.Email!,
                Join(member.User.FirstName, member.User.LastName),
                ("email", member.User.Email),
                ("mfa", member.User.TwoFactorAuthenticationEnabled),
                ("status", member.Status == "rejected" ? "suspended" : "active"))));
            if (page >= (members.ResultInfo?.TotalPages ?? 1))
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>DigitalOcean API v2: droplets (backups) and cloud firewalls (open admin ports).</summary>
public sealed class DigitalOceanConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.digitalocean.com/v2";
    private static readonly string[] AdminPorts = ["22", "3389", "0", "all", "1-65535"];

    public override string Key => "digitalocean";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Bearer(call.Require("apiToken"));
        var resources = new List<NormalizedResource>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var droplets = await call.GetAsync<DropletPage>($"{Api}/droplets?per_page=200&page={page}", auth, cancellationToken);
            resources.AddRange(droplets.Droplets.Select(droplet => call.Resource(
                "server",
                droplet.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                droplet.Name,
                ("backups", droplet.Features?.Contains("backups") == true),
                ("region", droplet.Region?.Slug),
                ("size", droplet.SizeSlug))));
            if (droplets.Links?.Pages?.Next is null)
            {
                break;
            }
        }

        var firewalls = await call.GetAsync<FirewallPage>($"{Api}/firewalls?per_page=200", auth, cancellationToken);
        resources.AddRange(firewalls.Firewalls.Select(firewall =>
        {
            var open = firewall.InboundRules?
                .Where(rule => rule.Sources?.Addresses?.Any(address => address is "0.0.0.0/0" or "::/0") == true)
                .Select(rule => rule.Ports ?? "all")
                .Where(ports => AdminPorts.Contains(ports))
                .ToArray() ?? [];
            return call.Resource(
                "firewall",
                firewall.Id,
                firewall.Name,
                ("open", open.Length > 0),
                ("inbound", string.Join(",", open)),
                ("allowed", open.Length > 0 ? "0.0.0.0/0" : "restricted"));
        }));
        return resources;
    }
}

/// <summary>Heroku Platform API v3: team apps and team members with two-factor status.</summary>
public sealed class HerokuConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.heroku.com";

    public override string Key => "heroku";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var token = call.Require("apiKey");
        var team = Escape(call.Require("team"));
        var auth = (Action<HttpRequestMessage>)(request =>
        {
            Bearer(token)(request);
            request.Headers.Accept.Clear();
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.heroku+json; version=3");
        });
        var apps = await call.GetAsync<HerokuApp[]>($"{Api}/teams/{team}/apps", auth, cancellationToken);
        var members = await call.GetAsync<HerokuMember[]>($"{Api}/teams/{team}/members", auth, cancellationToken);
        return apps
            .Select(app => call.Resource(
                "app",
                app.Name,
                app.Name,
                ("stack", app.Stack?.Name),
                ("region", app.Region?.Name),
                ("environment", app.Name.Contains("staging", StringComparison.OrdinalIgnoreCase) ? "staging" : "production")))
            .Concat(members.Select(member => call.Resource(
                "user",
                member.Email,
                member.Email,
                ("email", member.Email),
                ("mfa", member.TwoFactorAuthentication),
                ("status", "active"),
                ("role", member.Role))))
            .ToArray();
    }
}

/// <summary>Vercel REST API: /v9/projects and /v5/domains, scoped to a team when teamId is set.</summary>
public sealed class VercelConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.vercel.com";

    public override string Key => "vercel";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Bearer(call.Require("apiToken"));
        var team = call.Optional("teamId") is { } id ? "&teamId=" + Escape(id) : "";
        var resources = new List<NormalizedResource>();
        long? until = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var projects = await call.GetAsync<VercelProjects>($"{Api}/v9/projects?limit=100{team}" + (until is null ? "" : "&until=" + until), auth, cancellationToken);
            resources.AddRange(projects.Projects.Select(project => call.Resource(
                "app",
                project.Name,
                project.Name,
                ("framework", project.Framework),
                ("environment", "production"))));
            until = projects.Pagination?.Next;
            if (until is null)
            {
                break;
            }
        }

        var domains = await call.GetAsync<VercelDomains>($"{Api}/v5/domains?limit=100{team}", auth, cancellationToken);
        resources.AddRange(domains.Domains.Select(domain => call.Resource(
            "domain",
            domain.Name,
            domain.Name,
            ("tls", true),
            ("sslMode", "managed"),
            ("alwaysHttps", true),
            ("minTls", "1.2"),
            ("verified", domain.Verified))));
        return resources;
    }
}

internal sealed record CloudflarePage<T>(T[] Result, [property: JsonPropertyName("result_info")] CloudflareInfo? ResultInfo);

internal sealed record CloudflareInfo([property: JsonPropertyName("total_pages")] int TotalPages);

internal sealed record CloudflareZone(string Id, string Name, string? Status);

internal sealed record CloudflareSetting(CloudflareSettingValue Result);

internal sealed record CloudflareSettingValue(string Id, JsonElement Value);

internal sealed record CloudflareMember(string Id, string? Status, CloudflareUser? User);

internal sealed record CloudflareUser(
    string? Email,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    [property: JsonPropertyName("two_factor_authentication_enabled")] bool TwoFactorAuthenticationEnabled);

internal sealed record DropletPage(Droplet[] Droplets, DoLinks? Links);

internal sealed record Droplet(
    long Id,
    string Name,
    string[]? Features,
    DoRegion? Region,
    [property: JsonPropertyName("size_slug")] string? SizeSlug);

internal sealed record DoRegion(string? Slug);

internal sealed record DoLinks(DoPages? Pages);

internal sealed record DoPages(string? Next);

internal sealed record FirewallPage(DoFirewall[] Firewalls);

internal sealed record DoFirewall(string Id, string Name, [property: JsonPropertyName("inbound_rules")] DoRule[]? InboundRules);

internal sealed record DoRule(string? Protocol, string? Ports, DoSources? Sources);

internal sealed record DoSources(string[]? Addresses);

internal sealed record HerokuApp(string Id, string Name, HerokuNamed? Region, HerokuNamed? Stack);

internal sealed record HerokuNamed(string? Name);

internal sealed record HerokuMember(
    string Email,
    string? Role,
    [property: JsonPropertyName("two_factor_authentication")] bool TwoFactorAuthentication);

internal sealed record VercelProjects(VercelProject[] Projects, VercelPagination? Pagination);

internal sealed record VercelProject(string Id, string Name, string? Framework);

internal sealed record VercelPagination(long? Next);

internal sealed record VercelDomains(VercelDomain[] Domains);

internal sealed record VercelDomain(string Name, bool Verified);
