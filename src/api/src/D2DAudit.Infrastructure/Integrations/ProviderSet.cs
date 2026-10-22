using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Storage;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class ProviderSet(
    IEnumerable<ICloudProvider> clouds,
    IEnumerable<IWorkspaceDirectory> directories,
    IEnumerable<ISourceControlProvider> sources,
    SecretBox secrets)
{
    public ProviderConnection ToConnection(IntegrationConnection row)
    {
        string? secret = null;
        if (!string.IsNullOrEmpty(row.Secret))
        {
            if (!secrets.CanProtect)
            {
                throw new InvalidOperationException("TENANT_DATA_KEY is required to read a stored secret.");
            }

            secret = secrets.Decrypt(row.Secret);
        }

        return new ProviderConnection(
            row.Example,
            row.Fixture,
            row.RoleArn,
            row.ExternalId,
            row.InstallationId,
            row.Domain,
            secret);
    }

    public async Task<Result<ProviderConnect>> ConnectAsync(
        string provider,
        ProviderConnection? existing,
        ConnectIntegrationRequest request,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (provider == "aws")
        {
            return await Cloud().ConnectAsync(existing, request, cancellationToken);
        }

        if (provider is "google" or "microsoft")
        {
            return await Directory(provider).ConnectAsync(existing, request, redirectUri, cancellationToken);
        }

        return await Source().ConnectAsync(existing, request, cancellationToken);
    }

    public async Task<Result<ProviderCollect>> CollectAsync(IntegrationConnection row, CancellationToken cancellationToken)
    {
        if (row.Example)
        {
            return Result<ProviderCollect>.Ok(new ProviderCollect(ExampleSnapshots.For(row.Provider, row.Fixture)));
        }

        if (!string.IsNullOrEmpty(row.Secret) && !secrets.CanProtect)
        {
            return Result<ProviderCollect>.Fail("key_not_configured");
        }

        var connection = ToConnection(row);
        if (row.Provider == "aws")
        {
            return await Cloud().CollectAsync(connection, cancellationToken);
        }

        if (row.Provider is "google" or "microsoft")
        {
            return await Directory(row.Provider).CollectAsync(connection, cancellationToken);
        }

        return await Source().CollectAsync(connection, cancellationToken);
    }

    public string? Apply(IntegrationConnection row, ProviderConnect connect)
    {
        if (!string.IsNullOrWhiteSpace(connect.Secret))
        {
            var protectedSecret = secrets.Encrypt(connect.Secret);
            if (protectedSecret is null)
            {
                return "key_not_configured";
            }

            row.Secret = protectedSecret;
        }

        row.Status = connect.Status;
        row.ExternalId = connect.ExternalId ?? row.ExternalId;
        row.RoleArn = connect.RoleArn ?? row.RoleArn;
        row.AccountLabel = connect.AccountLabel ?? row.AccountLabel;
        row.Domain = connect.Domain ?? row.Domain;
        row.InstallationId = connect.InstallationId ?? row.InstallationId;
        row.SetupHint = connect.SetupHint;
        row.LastError = null;
        return null;
    }

    public static NormalizedResource ToResource(ResourceSnapshot snapshot)
    {
        var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.AttributesJson) ?? [];
        return new NormalizedResource(snapshot.Provider, snapshot.Kind, snapshot.ExternalId, snapshot.Name, attributes);
    }

    private ICloudProvider Cloud() =>
        clouds.First(item => item.ProviderKey == "aws");

    private IWorkspaceDirectory Directory(string provider) =>
        directories.First(item => item.ProviderKey == provider);

    private ISourceControlProvider Source() =>
        sources.First(item => item.ProviderKey == "github");
}
