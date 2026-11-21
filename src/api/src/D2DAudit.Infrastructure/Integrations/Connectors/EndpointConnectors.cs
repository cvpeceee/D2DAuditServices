using System.Text.Json;
using System.Text.Json.Serialization;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>CrowdStrike Falcon: OAuth2 token, devices-scroll query and device entity details.</summary>
public sealed class CrowdStrikeConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "crowdstrike";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("baseUrl", "https://api.crowdstrike.com");
        var token = await call.PostFormAsync<OAuthToken>(
            origin + "/oauth2/token",
            new Dictionary<string, string> { ["client_id"] = call.Require("clientId"), ["client_secret"] = call.Require("clientSecret") },
            static _ => { },
            cancellationToken);
        var auth = Bearer(token.AccessToken);
        var resources = new List<NormalizedResource>();
        string? offset = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var url = origin + "/devices/queries/devices-scroll/v1?limit=500" + (offset is null ? "" : "&offset=" + Escape(offset));
            var ids = await call.GetAsync<FalconIds>(url, auth, cancellationToken);
            if (ids.Resources.Length == 0)
            {
                break;
            }

            var details = await call.PostJsonAsync<FalconDevices>(origin + "/devices/entities/devices/v2", new { ids = ids.Resources }, auth, cancellationToken);
            resources.AddRange(details.Resources.Select(device =>
            {
                var seen = call.DaysSince(device.LastSeen);
                return call.Resource(
                    "edrAgent",
                    device.SerialNumber ?? device.DeviceId,
                    device.Hostname ?? device.DeviceId,
                    ("platform", device.PlatformName),
                    ("healthy", device.ReducedFunctionalityMode != "yes" && seen <= 7),
                    ("prevention", device.Status ?? "normal"),
                    ("lastSeenDaysAgo", seen),
                    ("sensorVersion", device.AgentVersion));
            }));
            offset = ids.Meta?.Pagination?.Offset;
            if (string.IsNullOrEmpty(offset))
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>SentinelOne Management API v2.1: /agents with cursor paging and unresolved /threats.</summary>
public sealed class SentinelOneConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "sentinelone";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("consoleUrl");
        var auth = Scheme("ApiToken", call.Require("apiToken"));
        var resources = new List<NormalizedResource>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var url = origin + "/web/api/v2.1/agents?limit=200" + (cursor is null ? "" : "&cursor=" + Escape(cursor));
            var agents = await call.GetAsync<SentinelPage<SentinelAgent>>(url, auth, cancellationToken);
            resources.AddRange(agents.Data.Select(agent =>
            {
                var seen = call.DaysSince(agent.LastActiveDate);
                return call.Resource(
                    "edrAgent",
                    agent.SerialNumber ?? agent.Id,
                    agent.ComputerName ?? agent.Id,
                    ("owner", agent.LastLoggedInUserName),
                    ("platform", agent.OsName),
                    ("healthy", agent.IsActive && !agent.Infected),
                    ("lastSeenDaysAgo", seen),
                    ("sensorVersion", agent.AgentVersion));
            }));
            cursor = agents.Pagination?.NextCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        var threats = await call.GetAsync<SentinelPage<SentinelThreat>>(origin + "/web/api/v2.1/threats?limit=100&resolved=false", auth, cancellationToken);
        resources.AddRange(threats.Data.Select(threat => call.Resource(
            "alert",
            threat.Id,
            threat.ThreatInfo?.ThreatName ?? threat.Id,
            ("severity", threat.ThreatInfo?.ConfidenceLevel),
            ("triaged", !string.Equals(threat.ThreatInfo?.IncidentStatus, "unresolved", StringComparison.OrdinalIgnoreCase)),
            ("status", threat.ThreatInfo?.IncidentStatus))));
        return resources;
    }
}

/// <summary>Microsoft Defender for Endpoint: app token, /api/machines and /api/alerts.</summary>
public sealed class DefenderConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.securitycenter.microsoft.com";
    private const string Login = "https://login.microsoftonline.com/";

    public override string Key => "defender";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var token = await call.PostFormAsync<OAuthToken>(
            Login + Escape(call.Require("tenantId")) + "/oauth2/v2.0/token",
            new Dictionary<string, string>
            {
                ["client_id"] = call.Require("clientId"),
                ["client_secret"] = call.Require("clientSecret"),
                ["grant_type"] = "client_credentials",
                ["scope"] = Api + "/.default",
            },
            static _ => { },
            cancellationToken);
        var auth = Bearer(token.AccessToken);
        var resources = new List<NormalizedResource>();
        var next = Api + "/api/machines";
        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            var machines = await call.GetAsync<ODataPage<DefenderMachine>>(next, auth, cancellationToken);
            resources.AddRange(machines.Value.Select(machine =>
            {
                var seen = call.DaysSince(machine.LastSeen);
                return call.Resource(
                    "edrAgent",
                    machine.Id,
                    machine.ComputerDnsName ?? machine.Id,
                    ("platform", machine.OsPlatform),
                    ("healthy", machine.HealthStatus == "Active"),
                    ("exposure", machine.ExposureLevel),
                    ("lastSeenDaysAgo", seen),
                    ("sensorVersion", machine.AgentVersion));
            }));
            next = Within(machines.NextLink, Api);
        }

        var alerts = await call.GetAsync<ODataPage<DefenderAlert>>(Api + "/api/alerts?$top=200&$filter=" + Escape("status ne 'Resolved'"), auth, cancellationToken);
        resources.AddRange(alerts.Value.Select(alert => call.Resource(
            "alert",
            alert.Id,
            alert.Title ?? alert.Id,
            ("severity", alert.Severity),
            ("triaged", alert.Status != "New" || !string.IsNullOrEmpty(alert.AssignedTo)),
            ("status", alert.Status))));
        return resources;
    }
}

/// <summary>Mosyle Business API: POST /v1/login for a bearer token, then POST /v1/devices (operation list).</summary>
public sealed class MosyleConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://businessapi.mosyle.com/v1";

    public override string Key => "mosyle";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var accessToken = call.Require("accessToken");
        var keyHeader = Headers(("accessToken", accessToken));
        var (_, headers) = await call.PostJsonPageAsync<JsonElement>(
            Api + "/login",
            new { email = call.Require("email"), password = call.Require("password") },
            keyHeader,
            cancellationToken);
        var bearer = Header(headers, "Authorization") ?? throw new ConnectorException("auth_failed");
        var auth = (Action<HttpRequestMessage>)(request =>
        {
            keyHeader(request);
            request.Headers.TryAddWithoutValidation("Authorization", bearer);
        });
        var resources = new List<NormalizedResource>();
        foreach (var os in new[] { "mac", "ios" })
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var result = await call.PostJsonAsync<MosyleResult>(
                    Api + "/devices",
                    new { operation = "list", options = new { os, page } },
                    auth,
                    cancellationToken);
                var devices = result.Response?.Devices ?? [];
                resources.AddRange(devices.Select(device => call.Resource(
                    "device",
                    device.SerialNumber ?? device.DeviceUdid,
                    device.DeviceName ?? device.DeviceUdid,
                    ("email", device.UserEmail),
                    ("owner", device.UserName),
                    ("platform", device.Os ?? os),
                    ("encrypted", device.Extra is not null && device.Extra.Any(item =>
                        item.Key.Contains("filevault", StringComparison.OrdinalIgnoreCase) && Read(item.Value) is "true" or "1" or "Enabled")))));
                if (devices.Length < 50)
                {
                    break;
                }
            }
        }

        return resources;
    }
}

internal sealed record FalconIds(string[] Resources, FalconMeta? Meta);

internal sealed record FalconMeta(FalconPagination? Pagination);

internal sealed record FalconPagination(string? Offset);

internal sealed record FalconDevices(FalconDevice[] Resources);

internal sealed record FalconDevice(
    [property: JsonPropertyName("device_id")] string DeviceId,
    string? Hostname,
    [property: JsonPropertyName("platform_name")] string? PlatformName,
    [property: JsonPropertyName("serial_number")] string? SerialNumber,
    [property: JsonPropertyName("last_seen")] DateTimeOffset? LastSeen,
    [property: JsonPropertyName("agent_version")] string? AgentVersion,
    [property: JsonPropertyName("reduced_functionality_mode")] string? ReducedFunctionalityMode,
    string? Status);

internal sealed record SentinelPage<T>(T[] Data, SentinelPagination? Pagination);

internal sealed record SentinelPagination(string? NextCursor);

internal sealed record SentinelAgent(
    string Id,
    string? ComputerName,
    string? OsName,
    string? SerialNumber,
    bool IsActive,
    bool Infected,
    DateTimeOffset? LastActiveDate,
    string? AgentVersion,
    string? LastLoggedInUserName);

internal sealed record SentinelThreat(string Id, SentinelThreatInfo? ThreatInfo);

internal sealed record SentinelThreatInfo(string? ThreatName, string? IncidentStatus, string? ConfidenceLevel);

internal sealed record ODataPage<T>(T[] Value, [property: JsonPropertyName("@odata.nextLink")] string? NextLink);

internal sealed record DefenderMachine(
    string Id,
    string? ComputerDnsName,
    string? OsPlatform,
    string? HealthStatus,
    string? ExposureLevel,
    DateTimeOffset? LastSeen,
    string? AgentVersion);

internal sealed record DefenderAlert(string Id, string? Title, string? Severity, string? Status, string? AssignedTo);

internal sealed record MosyleResult(MosyleResponse? Response);

internal sealed record MosyleResponse(MosyleDevice[]? Devices);

internal sealed record MosyleDevice(
    [property: JsonPropertyName("deviceudid")] string DeviceUdid,
    [property: JsonPropertyName("serial_number")] string? SerialNumber,
    [property: JsonPropertyName("device_name")] string? DeviceName,
    [property: JsonPropertyName("os")] string? Os,
    [property: JsonPropertyName("username")] string? UserName,
    [property: JsonPropertyName("useremail")] string? UserEmail)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
