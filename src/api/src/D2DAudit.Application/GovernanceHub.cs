using D2DAudit.Domain;

namespace D2DAudit.Application;

public sealed record LegalEntityDto(
    Guid Id,
    string Code,
    string Name,
    string Jurisdiction,
    string Region,
    int Headcount,
    int ControlsPassing,
    int ControlsTotal,
    int ReadinessPercent,
    string PrimaryFramework,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record EntityRollupDto(
    EntityRollupSummary Summary,
    IReadOnlyList<LegalEntityDto> Entities);

public sealed record SaveEntityRequest(
    Guid? Id,
    string Code,
    string Name,
    string Jurisdiction,
    string Region,
    int Headcount,
    int ControlsPassing,
    int ControlsTotal,
    string PrimaryFramework,
    string Status);

public sealed record PrivacyAssessmentDto(
    Guid Id,
    string Code,
    string Kind,
    string ActivityName,
    string DataSubjects,
    string LawfulBasis,
    string TransferMechanism,
    string RiskLevel,
    string Status,
    string OwnerEmail,
    DateOnly ReviewDueOn,
    DateTimeOffset UpdatedAt);

public sealed record SavePrivacyAssessmentRequest(
    Guid? Id,
    string Code,
    string Kind,
    string ActivityName,
    string DataSubjects,
    string LawfulBasis,
    string TransferMechanism,
    string RiskLevel,
    string Status,
    string OwnerEmail,
    DateOnly ReviewDueOn);

public sealed record AiModelDto(
    Guid Id,
    string Code,
    string ModelName,
    string Provider,
    string EuAiActRiskTier,
    string TrainingDataProvenance,
    string Guardrails,
    string RedTeamStatus,
    string LinkedControlCode,
    string OwnerEmail,
    DateTimeOffset EvaluatedAt);

public sealed record SaveAiModelRequest(
    Guid? Id,
    string Code,
    string ModelName,
    string Provider,
    string EuAiActRiskTier,
    string TrainingDataProvenance,
    string Guardrails,
    string RedTeamStatus,
    string LinkedControlCode,
    string OwnerEmail);

public sealed record PhishingCampaignDto(
    Guid Id,
    string Code,
    string Name,
    string Source,
    int SentCount,
    int ClickedCount,
    int ReportedCount,
    double ClickRatePercent,
    double ReportRatePercent,
    string RiskBand,
    string RemedialTrainingStatus,
    DateTimeOffset RanAt);

public sealed record CreatePhishingCampaignRequest(
    string Name,
    string Source,
    int SentCount,
    int ClickedCount,
    int ReportedCount,
    string? RemedialTrainingStatus);

public sealed record AttackSurfaceAssetDto(
    Guid Id,
    string Hostname,
    string IpAddress,
    string Service,
    int Port,
    int TlsDaysRemaining,
    bool ShadowIt,
    string Severity,
    string Status,
    DateTimeOffset LastSeenAt);

public sealed record SaveAttackSurfaceAssetRequest(
    Guid? Id,
    string Hostname,
    string IpAddress,
    string Service,
    int Port,
    int TlsDaysRemaining,
    bool ShadowIt,
    string Severity,
    string Status);

public sealed record AuditorFirmDto(
    Guid Id,
    string Code,
    string FirmName,
    string Accreditation,
    string Specialties,
    string Regions,
    string LeadAuditorEmail,
    bool Invited,
    DateTimeOffset? InvitedAt);

public sealed record RemediationPrDto(
    Guid Id,
    string ControlCode,
    string TargetResource,
    string Repository,
    string BranchName,
    string Title,
    string ArtifactType,
    string UnifiedDiff,
    int PrNumber,
    string PrUrl,
    string Mode,
    string Status,
    Guid? EvidenceId,
    string EvidenceSha256,
    DateTimeOffset CreatedAt);

public sealed record CreateRemediationPrRequest(
    string ControlCode,
    string TargetResource,
    string Repository,
    string ArtifactType,
    string? CustomFix);

public sealed record SyncIntegrationResultDto(
    string Provider,
    string Mode,
    int UpdatedRecords,
    string Summary,
    DateTimeOffset SyncedAt);

public sealed record ScimDeprovisionRequest(
    string Email,
    string? Reason,
    bool RevokeDownstreamAccounts);

public sealed record ScimDeprovisionResultDto(
    string Email,
    string PersonName,
    string State,
    bool LeaverAccessFlagged,
    int RemainingActiveAccounts,
    string OffboardedCheckOutcome,
    string OffboardedCheckDetail,
    string BackgroundCheckOutcome,
    DateTimeOffset ProcessedAt);

public sealed record GovernanceHubOverviewDto(
    EntityRollupDto Entities,
    IReadOnlyList<PrivacyAssessmentDto> PrivacyAssessments,
    IReadOnlyList<AiModelDto> AiModels,
    IReadOnlyList<PhishingCampaignDto> PhishingCampaigns,
    IReadOnlyList<AttackSurfaceAssetDto> AttackSurface,
    IReadOnlyList<AuditorFirmDto> AuditorNetwork,
    IReadOnlyList<RemediationPrDto> RemediationPrs,
    TimeMachineComparison TimeMachine);

public interface IGovernanceHub
{
    Task<GovernanceHubOverviewDto> OverviewAsync(CancellationToken cancellationToken);

    Task<EntityRollupDto> ListEntitiesAsync(CancellationToken cancellationToken);

    Task<Result<LegalEntityDto>> SaveEntityAsync(SaveEntityRequest request, CancellationToken cancellationToken);

    Task<Result<SyncIntegrationResultDto>> SyncCheckrAsync(CancellationToken cancellationToken);

    Task<Result<SyncIntegrationResultDto>> SyncKnowBe4Async(CancellationToken cancellationToken);

    Task<Result<ScimDeprovisionResultDto>> DeprovisionScimUserAsync(ScimDeprovisionRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrivacyAssessmentDto>> ListPrivacyAssessmentsAsync(CancellationToken cancellationToken);

    Task<Result<PrivacyAssessmentDto>> SavePrivacyAssessmentAsync(SavePrivacyAssessmentRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiModelDto>> ListAiModelsAsync(CancellationToken cancellationToken);

    Task<Result<AiModelDto>> SaveAiModelAsync(SaveAiModelRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PhishingCampaignDto>> ListPhishingCampaignsAsync(CancellationToken cancellationToken);

    Task<Result<PhishingCampaignDto>> CreatePhishingCampaignAsync(CreatePhishingCampaignRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AttackSurfaceAssetDto>> ListAttackSurfaceAsync(CancellationToken cancellationToken);

    Task<Result<AttackSurfaceAssetDto>> SaveAttackSurfaceAssetAsync(SaveAttackSurfaceAssetRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditorFirmDto>> ListAuditorNetworkAsync(CancellationToken cancellationToken);

    Task<Result<AuditorFirmDto>> InviteAuditorFirmAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RemediationPrDto>> ListRemediationPrsAsync(CancellationToken cancellationToken);

    Task<Result<RemediationPrDto>> CreateRemediationPrAsync(CreateRemediationPrRequest request, CancellationToken cancellationToken);

    Task<Result<TimeMachineComparison>> TimeMachineAsync(DateOnly? asOf, DateOnly? compareTo, CancellationToken cancellationToken);
}
