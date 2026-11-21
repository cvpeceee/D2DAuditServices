using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>Datadog API v1: monitors (muted state, notification handles) and log index retention.</summary>
public sealed class DatadogConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "datadog";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = Https("api." + (call.Optional("site") ?? "datadoghq.com").Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/'));
        var auth = Headers(("DD-API-KEY", call.Require("apiKey")), ("DD-APPLICATION-KEY", call.Require("appKey")));
        var resources = new List<NormalizedResource>();
        for (var page = 0; page < MaxPages; page++)
        {
            var monitors = await call.GetAsync<DatadogMonitor[]>($"{origin}/api/v1/monitor?page={page}&page_size=100", auth, cancellationToken);
            resources.AddRange(monitors.Select(monitor => call.Resource(
                "monitor",
                "mon-" + monitor.Id.ToString(CultureInfo.InvariantCulture),
                monitor.Name,
                ("muted", monitor.Options?.Silenced is { ValueKind: JsonValueKind.Object } silenced && silenced.EnumerateObject().Any()),
                ("notifies", Handle(monitor.Message)),
                ("type", monitor.Type))));
            if (monitors.Length < 100)
            {
                break;
            }
        }

        var indexes = await call.GetAsync<DatadogIndexes>($"{origin}/api/v1/logs/config/indexes", auth, cancellationToken);
        resources.AddRange(indexes.Indexes.Select(index => call.Resource(
            "logIndex",
            "index-" + index.Name,
            index.Name,
            ("audit", LogRetention.IsAudit(index.Name, index.Filter?.Query)),
            ("retentionDays", index.NumRetentionDays))));
        return resources;
    }

    private static string? Handle(string? message) =>
        message?.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(word => word.StartsWith('@') && word.Length > 1)?
            .TrimStart('@')
            .Split('-')[0];
}

/// <summary>Splunk Enterprise / Cloud REST: index retention (frozenTimePeriodInSecs).</summary>
public sealed class SplunkConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "splunk";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("baseUrl");
        var auth = Bearer(call.Require("token"));
        var indexes = await call.GetAsync<SplunkFeed>($"{origin}/services/data/indexes?output_mode=json&count=0", auth, cancellationToken);
        return indexes.Entry
            .Where(entry => entry.Content?.Disabled != true)
            .Select(entry => call.Resource(
                "logIndex",
                entry.Name,
                entry.Name,
                ("audit", LogRetention.IsAudit(entry.Name, null)),
                ("retentionDays", (entry.Content?.FrozenTimePeriodInSecs ?? 0) / 86400)))
            .ToArray();
    }
}

/// <summary>New Relic NerdGraph: alert condition entities (enabled tag) as monitors.</summary>
public sealed class NewRelicConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Query = """
        query Conditions($cursor: String) {
          actor { entitySearch(query: "domain = 'AIOPS' AND type = 'CONDITION'") {
            results(cursor: $cursor) { nextCursor entities { guid name tags { key values } } }
          } }
        }
        """;

    public override string Key => "newrelic";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var host = string.Equals(call.Optional("region"), "eu", StringComparison.OrdinalIgnoreCase) ? "api.eu.newrelic.com" : "api.newrelic.com";
        var endpoint = Https(host) + "/graphql";
        var auth = Headers(("API-Key", call.Require("apiKey")));
        var resources = new List<NormalizedResource>();
        string? cursor = null;
        for (var count = 0; count < MaxPages; count++)
        {
            var response = await call.PostJsonAsync<NewRelicResponse>(endpoint, new { query = Query, variables = new { cursor } }, auth, cancellationToken);
            var results = response.Data?.Actor?.EntitySearch?.Results ?? throw new ConnectorException("invalid_response");
            resources.AddRange(results.Entities.Select(entity => call.Resource(
                "monitor",
                entity.Guid,
                entity.Name,
                ("muted", Tag(entity, "enabled") == "false"),
                ("notifies", Tag(entity, "policyName")),
                ("type", Tag(entity, "type") ?? "nrql"))));
            cursor = results.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return resources;
    }

    private static string? Tag(NewRelicEntity entity, string key) =>
        entity.Tags?.FirstOrDefault(tag => string.Equals(tag.Key, key, StringComparison.OrdinalIgnoreCase))?.Values?.FirstOrDefault();
}

/// <summary>PagerDuty REST v2: schedule coverage for the next 7 days and recent incidents.</summary>
public sealed class PagerDutyConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.pagerduty.com";

    public override string Key => "pagerduty";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Scheme("Token", "token=" + call.Require("apiToken"));
        var resources = new List<NormalizedResource>();
        var since = Escape(call.Now.ToString("O", CultureInfo.InvariantCulture));
        var until = Escape(call.Now.AddDays(7).ToString("O", CultureInfo.InvariantCulture));
        for (var page = 0; page < MaxPages; page++)
        {
            var schedules = await call.GetAsync<PagerDutySchedules>($"{Api}/schedules?limit=100&offset={page * 100}", auth, cancellationToken);
            foreach (var summary in schedules.Schedules)
            {
                var detail = await call.GetAsync<PagerDutyScheduleEnvelope>(
                    $"{Api}/schedules/{Escape(summary.Id)}?since={since}&until={until}",
                    auth,
                    cancellationToken);
                var schedule = detail.Schedule;
                resources.Add(call.Resource(
                    "schedule",
                    summary.Id,
                    summary.Name,
                    ("covered", schedule.FinalSchedule?.RenderedCoveragePercentage >= 100),
                    ("layers", schedule.ScheduleLayers?.Length ?? 0),
                    ("members", schedule.Users?.Length ?? 0)));
            }

            if (!schedules.More)
            {
                break;
            }
        }

        var monthAgo = Escape(call.Now.AddDays(-30).ToString("O", CultureInfo.InvariantCulture));
        for (var page = 0; page < MaxPages; page++)
        {
            var incidents = await call.GetAsync<PagerDutyIncidents>(
                $"{Api}/incidents?since={monthAgo}&limit=100&offset={page * 100}&statuses[]=triggered&statuses[]=acknowledged&statuses[]=resolved",
                auth,
                cancellationToken);
            resources.AddRange(incidents.Incidents.Select(incident => call.Resource(
                "alert",
                incident.Id,
                incident.Title,
                ("severity", incident.Urgency == "high" ? "high" : "low"),
                ("triaged", incident.Status != "triggered"),
                ("status", incident.Status),
                ("ageHours", Math.Max(0, (int)(call.Now - incident.CreatedAt).TotalHours)))));
            if (!incidents.More)
            {
                break;
            }
        }

        return resources;
    }
}

/// <summary>Opsgenie REST v2: schedules with rotations and recent alerts.</summary>
public sealed class OpsgenieConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "opsgenie";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("baseUrl", "api.opsgenie.com");
        var auth = Scheme("GenieKey", call.Require("apiKey"));
        var resources = new List<NormalizedResource>();
        var schedules = await call.GetAsync<OpsgenieList<OpsgenieSchedule>>($"{origin}/v2/schedules?expand=rotation", auth, cancellationToken);
        resources.AddRange(schedules.Data.Select(schedule =>
        {
            var rotations = schedule.Rotations ?? [];
            var members = rotations
                .SelectMany(rotation => rotation.Participants ?? [])
                .Select(participant => participant.Username ?? participant.Name ?? participant.Id)
                .Where(name => name is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return call.Resource(
                "schedule",
                schedule.Name,
                schedule.Name,
                ("covered", schedule.Enabled && rotations.Length > 0 && rotations.All(rotation => rotation.Participants?.Length > 0)),
                ("layers", rotations.Length),
                ("members", members));
        }));

        for (var page = 0; page < MaxPages; page++)
        {
            var alerts = await call.GetAsync<OpsgenieList<OpsgenieAlert>>($"{origin}/v2/alerts?limit=100&offset={page * 100}&sort=createdAt&order=desc", auth, cancellationToken);
            resources.AddRange(alerts.Data.Select(alert => call.Resource(
                "alert",
                "og-" + (alert.TinyId ?? alert.Id),
                alert.Message,
                ("severity", alert.Priority switch { "P1" => "critical", "P2" => "high", "P3" => "medium", _ => "low" }),
                ("triaged", alert.Acknowledged || alert.Status == "closed"),
                ("status", alert.Status),
                ("ageHours", Math.Max(0, (int)(call.Now - alert.CreatedAt).TotalHours)))));
            if (alerts.Data.Length < 100)
            {
                break;
            }
        }

        return resources;
    }
}

internal static class LogRetention
{
    private static readonly string[] AuditMarkers = ["audit", "security", "wineventlog", "cloudtrail", "auth"];

    public static bool IsAudit(string name, string? query) =>
        AuditMarkers.Any(marker =>
            name.Contains(marker, StringComparison.OrdinalIgnoreCase)
            || (query?.Contains(marker, StringComparison.OrdinalIgnoreCase) ?? false));
}

internal sealed record DatadogMonitor(long Id, string Name, string? Type, string? Message, DatadogOptions? Options);

internal sealed record DatadogOptions(JsonElement? Silenced);

internal sealed record DatadogIndexes(DatadogIndex[] Indexes);

internal sealed record DatadogIndex(
    string Name,
    [property: JsonPropertyName("num_retention_days")] int NumRetentionDays,
    DatadogFilter? Filter);

internal sealed record DatadogFilter(string? Query);

internal sealed record SplunkFeed(SplunkEntry[] Entry);

internal sealed record SplunkEntry(string Name, SplunkContent? Content);

internal sealed record SplunkContent(bool? Disabled, long? FrozenTimePeriodInSecs);

internal sealed record NewRelicResponse(NewRelicData? Data);

internal sealed record NewRelicData(NewRelicActor? Actor);

internal sealed record NewRelicActor(NewRelicSearch? EntitySearch);

internal sealed record NewRelicSearch(NewRelicResults? Results);

internal sealed record NewRelicResults(NewRelicEntity[] Entities, string? NextCursor);

internal sealed record NewRelicEntity(string Guid, string Name, NewRelicTag[]? Tags);

internal sealed record NewRelicTag(string Key, string[]? Values);

internal sealed record PagerDutySchedules(PagerDutyScheduleSummary[] Schedules, bool More);

internal sealed record PagerDutyScheduleSummary(string Id, string Name);

internal sealed record PagerDutyScheduleEnvelope(PagerDutySchedule Schedule);

internal sealed record PagerDutySchedule(
    [property: JsonPropertyName("final_schedule")] PagerDutyFinal? FinalSchedule,
    [property: JsonPropertyName("schedule_layers")] JsonElement[]? ScheduleLayers,
    JsonElement[]? Users);

internal sealed record PagerDutyFinal([property: JsonPropertyName("rendered_coverage_percentage")] double? RenderedCoveragePercentage);

internal sealed record PagerDutyIncidents(PagerDutyIncident[] Incidents, bool More);

internal sealed record PagerDutyIncident(
    string Id,
    string Title,
    string Status,
    string? Urgency,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

internal sealed record OpsgenieList<T>(T[] Data);

internal sealed record OpsgenieSchedule(string Id, string Name, bool Enabled, OpsgenieRotation[]? Rotations);

internal sealed record OpsgenieRotation(OpsgenieParticipant[]? Participants);

internal sealed record OpsgenieParticipant(string? Id, string? Name, string? Username);

internal sealed record OpsgenieAlert(
    string Id,
    string? TinyId,
    string Message,
    string? Priority,
    string? Status,
    bool Acknowledged,
    DateTimeOffset CreatedAt);
