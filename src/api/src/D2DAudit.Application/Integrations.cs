using D2DAudit.Domain;

namespace D2DAudit.Application;

public sealed record ProviderConnection(
    bool Example,
    string Fixture,
    string? RoleArn,
    string? ExternalId,
    string? InstallationId,
    string? Domain,
    string? Secret);

public sealed record ProviderCollect(IReadOnlyList<NormalizedResource> Resources);

public sealed record ProviderConnect(
    string Status,
    string? ExternalId,
    string? RoleArn,
    string? AccountLabel,
    string? Domain,
    string? InstallationId,
    string? Secret,
    string? AuthorizationUrl,
    string? TrustPolicy,
    string? PermissionPolicy,
    string? SetupHint);

public sealed record ConnectIntegrationRequest(
    string? RoleArn,
    string? Domain,
    string? InstallationId,
    string? Code,
    string? State,
    string? RedirectUri);

public interface ICloudProvider
{
    string ProviderKey { get; }

    Task<Result<ProviderConnect>> ConnectAsync(ProviderConnection? existing, ConnectIntegrationRequest request, CancellationToken cancellationToken);

    Task<Result<ProviderCollect>> CollectAsync(ProviderConnection connection, CancellationToken cancellationToken);
}

public interface IWorkspaceDirectory
{
    string ProviderKey { get; }

    Task<Result<ProviderConnect>> ConnectAsync(ProviderConnection? existing, ConnectIntegrationRequest request, string redirectUri, CancellationToken cancellationToken);

    Task<Result<ProviderCollect>> CollectAsync(ProviderConnection connection, CancellationToken cancellationToken);
}

public interface ISourceControlProvider
{
    string ProviderKey { get; }

    Task<Result<ProviderConnect>> ConnectAsync(ProviderConnection? existing, ConnectIntegrationRequest request, CancellationToken cancellationToken);

    Task<Result<ProviderCollect>> CollectAsync(ProviderConnection connection, CancellationToken cancellationToken);
}

public interface IStorageProvider
{
    Task PutAsync(string key, ReadOnlyMemory<byte> body, string contentType, DateTimeOffset? lockUntil, CancellationToken cancellationToken);

    Task<byte[]> GetAsync(string key, CancellationToken cancellationToken);
}

public sealed record IntegrationDto(
    Guid Id,
    string Provider,
    string DisplayName,
    string Status,
    bool Example,
    string? AccountLabel,
    string? ExternalId,
    string? RoleArn,
    string? Domain,
    string? SetupHint,
    DateTimeOffset? LastCollectedAt);

public sealed record ConnectResponse(
    IntegrationDto Integration,
    string? AuthorizationUrl,
    string? TrustPolicy,
    string? PermissionPolicy);

public sealed record AutomatedTestDto(
    Guid Id,
    string Key,
    string Name,
    string ControlCode,
    string Outcome,
    string? Detail,
    string Remediation,
    IReadOnlyList<string> Requirements);

public sealed record EvidenceListItem(
    Guid Id,
    string Title,
    string ControlCode,
    string Source,
    DateTimeOffset? CollectedAt,
    string Integrity,
    string? Sha256,
    DateTimeOffset? LockedUntil,
    string? ObjectKey);

public sealed record EvidenceSummary(
    int Total,
    int Automated,
    int Manual,
    int Verified,
    int RetentionYears,
    bool ChainIntact);

public sealed record EvidencePage(EvidenceSummary Summary, IReadOnlyList<EvidenceListItem> Items);

public sealed record VerifyReport(bool Intact, int Checked, string? ObjectKey, string? Reason);

public sealed record UploadEvidenceRequest(string ControlCode, string Title, string ContentBase64);

public interface IIntegrationQuery
{
    Task<IReadOnlyList<IntegrationDto>> ListAsync(CancellationToken cancellationToken);

    Task<Result<ConnectResponse>> ConnectAsync(string provider, ConnectIntegrationRequest request, string redirectUri, CancellationToken cancellationToken);

    Task<Result<Guid>> CollectAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<IntegrationDto>>> UseExampleFixturesAsync(string fixture, CancellationToken cancellationToken);

    Task<IReadOnlyList<AutomatedTestDto>> TestsAsync(CancellationToken cancellationToken);

    Task<EvidencePage> EvidenceAsync(CancellationToken cancellationToken);

    Task<VerifyReport> VerifyAsync(CancellationToken cancellationToken);

    Task<Result<EvidenceListItem>> UploadAsync(UploadEvidenceRequest request, CancellationToken cancellationToken);
}

public interface ICollectionSchedule
{
    Task<Result<Guid>> StartAsync(Guid tenantId, CancellationToken cancellationToken);
}
