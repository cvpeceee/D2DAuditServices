using D2DAudit.Domain;

namespace D2DAudit.Application;

public sealed record ExposureItemDto(
    Guid Id,
    string Provider,
    string ResourceName,
    string ResourceKind,
    string Region,
    string Cidr,
    int Port,
    string Protocol,
    string Service,
    string Severity,
    int RiskScore,
    string ControlCode,
    string Summary,
    string RemediationTerraform,
    string RemediationCli,
    string Status,
    string ResolutionNote,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt);

public sealed record ExposureBoardDto(
    int OpenCount,
    int CriticalCount,
    int HighCount,
    int PublicBuckets,
    int OpenPortsCount,
    IReadOnlyList<ExposureItemDto> Findings);

public sealed record ResolveFindingRequest(string Note);

public sealed record ScanFindingDto(
    Guid Id,
    string Kind,
    string Severity,
    string AssetName,
    string Location,
    int LineNumber,
    string RuleId,
    string Title,
    string Detail,
    string Snippet,
    string Source,
    string FixVersion,
    string ControlCode,
    string Status,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt);

public sealed record SecurityScansBoardDto(
    int OpenContainers,
    int OpenLicenses,
    int OpenSecrets,
    IReadOnlyList<ScanFindingDto> Containers,
    IReadOnlyList<ScanFindingDto> Licenses,
    IReadOnlyList<ScanFindingDto> Secrets);

public sealed record IngestSarifRequest(string AssetName, string SarifJson);

public sealed record DataStoreDto(
    Guid Id,
    string StoreName,
    string Provider,
    string Engine,
    string Region,
    string Classification,
    string SensitiveDataTypes,
    long RecordCountEstimate,
    bool Encrypted,
    bool PublicAccess,
    bool RetentionCompliant,
    string ControlCode,
    int RiskScore,
    string RiskLevel,
    string OwnerEmail,
    DateTimeOffset LastScannedAt);

public sealed record DspmBoardDto(
    int TotalStores,
    int RestrictedStores,
    int PublicSensitiveStores,
    int UnencryptedStores,
    IReadOnlyList<DataStoreDto> Stores);

public sealed record SaveDataStoreRequest(
    Guid? Id,
    string StoreName,
    string Provider,
    string Engine,
    string Region,
    string Classification,
    string SensitiveDataTypes,
    long RecordCountEstimate,
    bool Encrypted,
    bool PublicAccess,
    bool RetentionCompliant,
    string ControlCode,
    string OwnerEmail);

public sealed record CostRecommendationDto(
    Guid Id,
    string Provider,
    string ResourceName,
    string ResourceType,
    string Region,
    string Category,
    decimal MonthlySavingsUsd,
    decimal AnnualSavingsUsd,
    string LinkedControlCode,
    string ComplianceImpact,
    string RemediationSummary,
    string Status,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ActionedAt);

public sealed record FinOpsBoardDto(
    FinOpsSummary Summary,
    IReadOnlyList<CostRecommendationDto> Recommendations);

public sealed record UpdateCostStatusRequest(string Status);

public sealed record AttackPathsBoardDto(
    int TotalPaths,
    int CriticalPaths,
    int HighestBlastRadius,
    IReadOnlyList<AttackPath> Paths);

public interface ICloudSecurity
{
    Task<ExposureBoardDto> ExposureBoardAsync(CancellationToken cancellationToken);

    Task<Result<ExposureBoardDto>> ScanExposureAsync(CancellationToken cancellationToken);

    Task<Result<ExposureItemDto>> ResolveExposureAsync(Guid id, ResolveFindingRequest request, CancellationToken cancellationToken);

    Task<SecurityScansBoardDto> ScansBoardAsync(CancellationToken cancellationToken);

    Task<Result<int>> IngestSarifAsync(IngestSarifRequest request, CancellationToken cancellationToken);

    Task<Result<ScanFindingDto>> ResolveScanAsync(Guid id, CancellationToken cancellationToken);

    Task<DspmBoardDto> DspmBoardAsync(CancellationToken cancellationToken);

    Task<Result<DataStoreDto>> SaveDataStoreAsync(SaveDataStoreRequest request, CancellationToken cancellationToken);

    Task<Result<DspmBoardDto>> ScanDspmAsync(CancellationToken cancellationToken);

    Task<FinOpsBoardDto> FinOpsBoardAsync(CancellationToken cancellationToken);

    Task<Result<CostRecommendationDto>> UpdateCostStatusAsync(Guid id, UpdateCostStatusRequest request, CancellationToken cancellationToken);

    Task<AttackPathsBoardDto> AttackPathsAsync(CancellationToken cancellationToken);
}
