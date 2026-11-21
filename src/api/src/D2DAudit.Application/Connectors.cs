using System.Text.Json;
using D2DAudit.Domain;

namespace D2DAudit.Application;

public sealed record ConnectorField(string Key, string Label, bool Secret, bool Required, string Placeholder);

public sealed record ConnectorDescriptor(
    string Key,
    string Name,
    string Category,
    string AuthType,
    string Description,
    IReadOnlyList<ConnectorField> Fields,
    IReadOnlyList<string> Resources,
    IReadOnlyList<string> Tests,
    IReadOnlyList<string> Controls,
    string DocsUrl,
    bool Builtin,
    string? ManagedAt,
    IReadOnlyList<string> EnvKeys,
    bool Harborline);

public sealed record ConnectorCheck(string Key, string Name, string ControlCode, IReadOnlyList<string> Requirements, string Remediation);

/// <summary>A pluggable integration. Mock and live implementations share this contract.</summary>
public interface IConnector
{
    string Key { get; }

    Task<Result<string>> TestAsync(IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<NormalizedResource>>> CollectAsync(IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken);
}

public interface IConnectorRegistry
{
    IReadOnlyList<ConnectorDescriptor> All { get; }

    ConnectorDescriptor? Find(string key);

    IConnector Mock(string key);

    IConnector? Live(string key);
}

public static class ConnectorModes
{
    public const string Mock = "mock";
    public const string Live = "live";
}

public static class ConnectorCatalog
{
    private static readonly Lazy<Document> Loaded = new(Load);

    public static IReadOnlyList<string> Categories => Loaded.Value.CategoryList;

    public static IReadOnlyList<ConnectorCheck> Checks => Loaded.Value.CheckList;

    public static IReadOnlyList<ConnectorDescriptor> Connectors => Loaded.Value.Descriptors;

    public static ConnectorDescriptor? Find(string? key) =>
        key is null ? null : Loaded.Value.Descriptors.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    public static IReadOnlyList<NormalizedResource> Fixtures(string key) =>
        Loaded.Value.FixtureMap.TryGetValue(key, out var rows) ? rows : [];

    private static Document Load()
    {
        var assembly = typeof(ConnectorCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream("connectors.json")
            ?? throw new InvalidOperationException("Missing connector catalog.");
        var raw = JsonSerializer.Deserialize<ConnectorFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Connector catalog is empty.");
        var descriptors = raw.Connectors.Select(item => new ConnectorDescriptor(
            item.Key,
            item.Name,
            item.Category,
            item.AuthType,
            item.Description,
            item.Fields.Select(field => new ConnectorField(field.Key, field.Label, field.Secret, field.Required, field.Placeholder ?? "")).ToArray(),
            item.Resources,
            item.Tests,
            item.Controls,
            item.DocsUrl,
            item.Builtin,
            item.ManagedAt,
            item.EnvKeys,
            item.Harborline)).ToArray();
        var fixtures = raw.Connectors.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<NormalizedResource>)item.Fixtures
                .Select(row => new NormalizedResource(row.Provider, row.Kind, row.ExternalId, row.Name, row.Attributes))
                .ToArray(),
            StringComparer.Ordinal);
        var checks = raw.Checks.Select(item => new ConnectorCheck(item.Key, item.Name, item.ControlCode, item.Requirements, item.Remediation)).ToArray();
        return new Document(raw.Categories, checks, descriptors, fixtures);
    }

    private sealed record Document(
        IReadOnlyList<string> CategoryList,
        IReadOnlyList<ConnectorCheck> CheckList,
        IReadOnlyList<ConnectorDescriptor> Descriptors,
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedResource>> FixtureMap);
}

internal sealed record ConnectorFile(string[] Categories, ConnectorFileCheck[] Checks, ConnectorFileEntry[] Connectors);

internal sealed record ConnectorFileCheck(string Key, string Name, string ControlCode, string[] Requirements, string Remediation);

internal sealed record ConnectorFileEntry(
    string Key,
    string Name,
    string Category,
    string AuthType,
    string Description,
    ConnectorFileField[] Fields,
    string[] Resources,
    string[] Tests,
    string[] Controls,
    string DocsUrl,
    bool Builtin,
    string? ManagedAt,
    string[] EnvKeys,
    bool Harborline,
    ConnectorFileResource[] Fixtures);

internal sealed record ConnectorFileField(string Key, string Label, bool Secret, bool Required, string? Placeholder);

internal sealed record ConnectorFileResource(string Provider, string Kind, string ExternalId, string Name, Dictionary<string, string> Attributes);
/// <summary>Returns the deterministic fixture resources shipped in connectors.json.</summary>
public sealed class MockConnector(ConnectorDescriptor descriptor) : IConnector
{
    public string Key => descriptor.Key;

    public Task<Result<string>> TestAsync(IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken) =>
        Task.FromResult(Result<string>.Ok($"{descriptor.Name} mock connector is reachable ({ConnectorCatalog.Fixtures(descriptor.Key).Count} fixture resources)."));

    public Task<Result<IReadOnlyList<NormalizedResource>>> CollectAsync(IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<NormalizedResource>>.Ok(ConnectorCatalog.Fixtures(descriptor.Key)));
}

public sealed record ConnectorConfigValue(string Key, string? Value, bool Configured);

public static class ConnectorCredentials
{
    public const int MaxValueLength = 1024;

    /// <summary>Keeps only declared fields, trims values and merges blank secrets with the stored ones.</summary>
    public static Dictionary<string, string> Merge(
        ConnectorDescriptor descriptor,
        IReadOnlyDictionary<string, string>? stored,
        IReadOnlyDictionary<string, string>? submitted)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in descriptor.Fields.Select(field => field.Key))
        {
            string? value = null;
            if (submitted is not null && submitted.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                value = raw.Trim();
            }
            else if (stored is not null && stored.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                value = existing;
            }

            if (value is not null)
            {
                merged[key] = value.Length > MaxValueLength ? value[..MaxValueLength] : value;
            }
        }

        return merged;
    }

    public static IReadOnlyList<string> Missing(ConnectorDescriptor descriptor, IReadOnlyDictionary<string, string> config) =>
        descriptor.Fields
            .Where(field => field.Required && (!config.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value)))
            .Select(field => field.Key)
            .ToArray();

    /// <summary>Secret values are never returned; callers only learn whether they are set.</summary>
    public static IReadOnlyList<ConnectorConfigValue> Redact(ConnectorDescriptor descriptor, IReadOnlyDictionary<string, string> config) =>
        descriptor.Fields
            .Select(field =>
            {
                var configured = config.TryGetValue(field.Key, out var value) && !string.IsNullOrWhiteSpace(value);
                return new ConnectorConfigValue(field.Key, field.Secret || !configured ? null : value, configured);
            })
            .ToArray();

    public static string Mode(ConnectorDescriptor descriptor, IReadOnlyDictionary<string, string> config, bool example) =>
        example || descriptor.Fields.Count == 0 || Missing(descriptor, config).Count > 0 ? ConnectorModes.Mock : ConnectorModes.Live;

    /// <summary>Removes any configured secret value from free text such as an upstream error message.</summary>
    public static string Scrub(ConnectorDescriptor descriptor, IReadOnlyDictionary<string, string> config, string text)
    {
        var result = text;
        var secrets = descriptor.Fields
            .Where(item => item.Secret)
            .Select(field => config.TryGetValue(field.Key, out var value) ? value : "")
            .Where(value => value.Length >= 4);
        foreach (var value in secrets)
        {
            result = result.Replace(value, "[redacted]", StringComparison.Ordinal);
        }

        return result;
    }
}

public sealed record ConnectorRunDto(
    Guid Id,
    string Kind,
    string Mode,
    string Status,
    int ResourceCount,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Trigger);

public sealed record ConnectorCheckDto(string Key, string Name, string ControlCode, string Outcome, string Detail);

public sealed record ConnectorSummaryDto(
    string Key,
    string Name,
    string Category,
    string AuthType,
    string Description,
    IReadOnlyList<string> Resources,
    IReadOnlyList<string> Tests,
    IReadOnlyList<string> Controls,
    string DocsUrl,
    bool Builtin,
    string? ManagedAt,
    string Status,
    string? Mode,
    int ResourceCount,
    DateTimeOffset? LastSyncedAt,
    string? LastError);

public sealed record ConnectorDetailDto(
    ConnectorSummaryDto Connector,
    IReadOnlyList<ConnectorField> Fields,
    IReadOnlyList<ConnectorConfigValue> Config,
    IReadOnlyList<string> EnvKeys,
    IReadOnlyList<ConnectorCheckDto> Checks,
    IReadOnlyList<ConnectorRunDto> History);

public sealed record ConnectorCatalogDto(
    IReadOnlyList<string> Categories,
    int Total,
    int Connected,
    IReadOnlyList<ConnectorSummaryDto> Connectors);

public sealed record ConnectConnectorRequest(Dictionary<string, string>? Config);

public sealed record ConnectorActionResponse(ConnectorDetailDto Detail, ConnectorRunDto? Run, Guid? JobId, string? Message);

public interface IConnectorHub
{
    Task<ConnectorCatalogDto> CatalogAsync(string? category, string? search, CancellationToken cancellationToken);

    Task<Result<ConnectorDetailDto>> DetailAsync(string key, CancellationToken cancellationToken);

    Task<Result<ConnectorActionResponse>> ConnectAsync(string key, ConnectConnectorRequest request, CancellationToken cancellationToken);

    Task<Result<ConnectorActionResponse>> TestAsync(string key, ConnectConnectorRequest? request, CancellationToken cancellationToken);

    Task<Result<ConnectorActionResponse>> SyncAsync(string key, CancellationToken cancellationToken);

    Task<Result<ConnectorDetailDto>> DisconnectAsync(string key, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ConnectorRunDto>>> HistoryAsync(string key, CancellationToken cancellationToken);
}
