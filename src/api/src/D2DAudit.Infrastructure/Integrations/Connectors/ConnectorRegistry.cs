using System.Collections.Concurrent;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>Resolves catalog descriptors to their mock fixtures and live REST adapters.</summary>
public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly Dictionary<string, LiveConnector> live;
    private readonly ConcurrentDictionary<string, MockConnector> mocks = new(StringComparer.Ordinal);

    public ConnectorRegistry(IEnumerable<LiveConnector> adapters)
    {
        live = adapters.ToDictionary(adapter => adapter.Key, StringComparer.Ordinal);
    }

    public IReadOnlyList<ConnectorDescriptor> All => ConnectorCatalog.Connectors;

    public ConnectorDescriptor? Find(string key) => ConnectorCatalog.Find(key);

    public IConnector Mock(string key)
    {
        var descriptor = Find(key) ?? throw new KeyNotFoundException(key);
        return mocks.GetOrAdd(key, _ => new MockConnector(descriptor));
    }

    public IConnector? Live(string key) => live.GetValueOrDefault(key);

    /// <summary>True for catalog connectors that this hub manages (built-in providers keep their own flows).</summary>
    public bool Handles(string provider) => Find(provider) is { Builtin: false };
}

public sealed record ConnectorOutcome(string Mode, Result<IReadOnlyList<NormalizedResource>> Result);

/// <summary>
/// Chooses mock or live per tenant connection, decrypts the stored config with TENANT_DATA_KEY and
/// normalises adapter output so it always fits the resource snapshot columns.
/// </summary>
public sealed class ConnectorExecutor(ConnectorRegistry registry, SecretBox secrets, IConfiguration configuration)
{
    private const int MaxSecretLength = 4000;

    public ConnectorRegistry Registry => registry;

    public bool MocksAllowed => !string.Equals(configuration["Integrations:MockConnectors"], "false", StringComparison.OrdinalIgnoreCase);

    public Result<Dictionary<string, string>> ReadConfig(IntegrationConnection? row)
    {
        if (row is null || string.IsNullOrEmpty(row.Secret))
        {
            return Result<Dictionary<string, string>>.Ok(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        if (!secrets.CanProtect)
        {
            return Result<Dictionary<string, string>>.Fail("key_not_configured");
        }

        try
        {
            var plain = secrets.Decrypt(row.Secret);
            var config = string.IsNullOrEmpty(plain) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(plain);
            return Result<Dictionary<string, string>>.Ok(new Dictionary<string, string>(config ?? [], StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is JsonException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            return Result<Dictionary<string, string>>.Fail("config_unreadable");
        }
    }

    /// <summary>Encrypts the connector config; returns null for an empty config.</summary>
    public Result<string?> Protect(IReadOnlyDictionary<string, string> config)
    {
        if (config.Count == 0)
        {
            return Result<string?>.Ok(null);
        }

        if (!secrets.CanProtect)
        {
            return Result<string?>.Fail("key_not_configured");
        }

        var sealedValue = secrets.Encrypt(JsonSerializer.Serialize(config));
        if (sealedValue is null)
        {
            return Result<string?>.Fail("key_not_configured");
        }

        return sealedValue.Length > MaxSecretLength
            ? Result<string?>.Fail("config_too_large")
            : Result<string?>.Ok(sealedValue);
    }

    public static string Mode(ConnectorDescriptor descriptor, IReadOnlyDictionary<string, string> config, bool example) =>
        ConnectorCredentials.Mode(descriptor, config, example);

    public async Task<ConnectorOutcome> TestAsync(
        ConnectorDescriptor descriptor,
        IReadOnlyDictionary<string, string> config,
        bool example,
        CancellationToken cancellationToken)
    {
        var mode = Mode(descriptor, config, example);
        var connector = Pick(descriptor, mode, example);
        if (connector is null)
        {
            return new ConnectorOutcome(mode, Result<IReadOnlyList<NormalizedResource>>.Fail("missing_fields"));
        }

        var tested = await connector.TestAsync(config, cancellationToken);
        return new ConnectorOutcome(
            mode,
            tested.IsSuccess
                ? Result<IReadOnlyList<NormalizedResource>>.Ok([])
                : Result<IReadOnlyList<NormalizedResource>>.Fail(Clean(descriptor, config, tested.Error)));
    }

    public async Task<ConnectorOutcome> CollectAsync(
        ConnectorDescriptor descriptor,
        IReadOnlyDictionary<string, string> config,
        bool example,
        CancellationToken cancellationToken)
    {
        var mode = Mode(descriptor, config, example);
        var connector = Pick(descriptor, mode, example);
        if (connector is null)
        {
            return new ConnectorOutcome(mode, Result<IReadOnlyList<NormalizedResource>>.Fail("missing_fields"));
        }

        var collected = await connector.CollectAsync(config, cancellationToken);
        if (!collected.IsSuccess || collected.Value is null)
        {
            return new ConnectorOutcome(mode, Result<IReadOnlyList<NormalizedResource>>.Fail(Clean(descriptor, config, collected.Error)));
        }

        return new ConnectorOutcome(mode, Result<IReadOnlyList<NormalizedResource>>.Ok(Normalize(descriptor.Key, collected.Value)));
    }

    public async Task<ConnectorOutcome> CollectAsync(IntegrationConnection row, CancellationToken cancellationToken)
    {
        var descriptor = registry.Find(row.Provider);
        if (descriptor is null)
        {
            return new ConnectorOutcome(ConnectorModes.Mock, Result<IReadOnlyList<NormalizedResource>>.Fail("unknown_provider"));
        }

        var config = ReadConfig(row);
        if (!config.IsSuccess || config.Value is null)
        {
            return new ConnectorOutcome(ConnectorModes.Live, Result<IReadOnlyList<NormalizedResource>>.Fail(config.Error ?? "config_unreadable"));
        }

        return await CollectAsync(descriptor, config.Value, row.Example, cancellationToken);
    }

    /// <summary>Forces every resource onto this provider and trims values to the snapshot column limits.</summary>
    public static IReadOnlyList<NormalizedResource> Normalize(string provider, IReadOnlyList<NormalizedResource> resources) =>
        resources
            .Select(resource =>
            {
                var attributes = resource.Attributes
                    .Take(40)
                    .ToDictionary(pair => Cap(pair.Key, 64), pair => Cap(pair.Value ?? "", 300), StringComparer.Ordinal);
                while (attributes.Count > 0 && JsonSerializer.Serialize(attributes).Length > 4000)
                {
                    attributes.Remove(attributes.Keys.Last());
                }

                var externalId = resource.ExternalId.StartsWith(provider + ":", StringComparison.Ordinal)
                    ? resource.ExternalId
                    : provider + ":" + resource.ExternalId;
                return new NormalizedResource(provider, Cap(resource.Kind, 32), Cap(externalId, 200), Cap(resource.Name, 300), attributes);
            })
            .GroupBy(resource => (resource.Kind, resource.ExternalId))
            .Select(group => group.First())
            .ToArray();

    private IConnector? Pick(ConnectorDescriptor descriptor, string mode, bool example)
    {
        if (mode == ConnectorModes.Live)
        {
            return registry.Live(descriptor.Key) ?? (MocksAllowed ? registry.Mock(descriptor.Key) : null);
        }

        return example || MocksAllowed ? registry.Mock(descriptor.Key) : null;
    }

    private static string Clean(ConnectorDescriptor descriptor, IReadOnlyDictionary<string, string> config, string? error)
    {
        var text = ConnectorCredentials.Scrub(descriptor, config, string.IsNullOrWhiteSpace(error) ? "collect_failed" : error);
        return Cap(text, 300);
    }

    private static string Cap(string value, int length) => value.Length > length ? value[..length] : value;
}
