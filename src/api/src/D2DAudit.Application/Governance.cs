using D2DAudit.Domain;

namespace D2DAudit.Application;

public sealed record RiskListItem(
    string Code,
    string Title,
    string Statement,
    int Likelihood,
    int Impact,
    int Score,
    string Band,
    string OwnerName,
    string OwnerEmail,
    string Treatment,
    string TreatmentPlan,
    DateOnly ReviewOn,
    int ResidualLikelihood,
    int ResidualImpact,
    int ResidualScore,
    string ResidualBand,
    IReadOnlyList<string> ControlCodes);

public sealed record SaveRiskRequest(
    string? Code,
    string Title,
    string Statement,
    int Likelihood,
    int Impact,
    string OwnerName,
    string OwnerEmail,
    string Treatment,
    string TreatmentPlan,
    DateOnly ReviewOn,
    int ResidualLikelihood,
    int ResidualImpact,
    IReadOnlyList<string> ControlCodes);

public sealed record PersonAccountDto(string Provider, string AccountName, bool Active);

public sealed record PersonTaskDto(string Kind, string TaskCode, bool Done);

public sealed record PersonAcknowledgementDto(string PolicyCode, string PolicyTitle, DateOnly DueOn, DateTimeOffset? AcknowledgedAt);

public sealed record PersonListItem(
    Guid Id,
    string Name,
    string Email,
    string Department,
    string Source,
    string State,
    string Training,
    DateOnly? TrainingDueOn,
    bool LeaverStillHasAccess,
    int OpenTasks,
    string Role,
    DateOnly? HireOn,
    DateOnly? LeftOn);

public sealed record PersonDetail(
    Guid Id,
    string Name,
    string Email,
    string Department,
    string Source,
    string State,
    string Training,
    DateOnly? TrainingDueOn,
    bool LeaverStillHasAccess,
    IReadOnlyList<PersonAccountDto> Accounts,
    IReadOnlyList<PersonTaskDto> Tasks,
    IReadOnlyList<PersonAcknowledgementDto> Acknowledgements,
    string Role,
    DateOnly? HireOn,
    DateOnly? LeftOn);

public sealed record SetTaskRequest(string TaskCode, bool Done);

public sealed record SoaRow(string Code, string Title, bool Applicable, string Justification, string Implementation);

public sealed record SaveSoaRequest(bool Applicable, string Justification, string Implementation);

public sealed record VendorDocumentDto(string Title, string Kind, DateOnly? IssuedOn);

public sealed record VendorListItem(
    Guid Id,
    string Name,
    string Service,
    string Criticality,
    string DataAccess,
    string ReviewState,
    DateOnly ReviewDueOn,
    int RiskRating,
    IReadOnlyList<VendorDocumentDto> Documents);

public sealed record SaveVendorRequest(
    Guid? Id,
    string Name,
    string Service,
    string Criticality,
    string DataAccess,
    string ReviewState,
    DateOnly ReviewDueOn,
    int RiskRating,
    IReadOnlyList<VendorDocumentDto> Documents);

public sealed record AccessItemDto(
    Guid Id,
    string PersonName,
    string PersonEmail,
    string Provider,
    string AccountName,
    string Decision);

public sealed record AccessCampaignListItem(Guid Id, string Name, DateOnly DueOn, string State, int Pending, int Total);

public sealed record AccessCampaignDetail(
    Guid Id,
    string Name,
    DateOnly DueOn,
    string State,
    DateTimeOffset? CompletedAt,
    Guid? EvidenceId,
    string? EvidenceSha256,
    IReadOnlyList<AccessItemDto> Items);

public sealed record StartCampaignRequest(string Name, DateOnly DueOn);

public sealed record AccessDecisionRequest(string Decision);

public sealed record FileExport(string FileName, string ContentType, byte[] Body);

public sealed record DueItem(string Kind, string Title, DateOnly DueOn, string Href, bool Overdue);

public interface IGovernance
{
    Task<IReadOnlyList<RiskListItem>> RisksAsync(CancellationToken cancellationToken);

    Task<Result<RiskListItem>> SaveRiskAsync(SaveRiskRequest request, CancellationToken cancellationToken);

    Task<Result<FileExport>> ExportRisksAsync(string format, CancellationToken cancellationToken);

    Task<IReadOnlyList<PersonListItem>> PeopleAsync(CancellationToken cancellationToken);

    Task<Result<PersonDetail>> PersonAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<int>> SyncPeopleAsync(CancellationToken cancellationToken);

    Task<Result<PersonDetail>> SetTaskAsync(Guid id, SetTaskRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SoaRow>> SoaAsync(CancellationToken cancellationToken);

    Task<Result<SoaRow>> SaveSoaAsync(string code, SaveSoaRequest request, CancellationToken cancellationToken);

    Task<Result<FileExport>> ExportSoaAsync(string format, CancellationToken cancellationToken);

    Task<IReadOnlyList<VendorListItem>> VendorsAsync(CancellationToken cancellationToken);

    Task<Result<VendorListItem>> SaveVendorAsync(SaveVendorRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccessCampaignListItem>> CampaignsAsync(CancellationToken cancellationToken);

    Task<Result<AccessCampaignDetail>> CampaignAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<AccessCampaignDetail>> StartCampaignAsync(StartCampaignRequest request, CancellationToken cancellationToken);

    Task<Result<AccessCampaignDetail>> DecideAsync(Guid campaignId, Guid itemId, AccessDecisionRequest request, CancellationToken cancellationToken);

    Task<Result<AccessCampaignDetail>> CompleteCampaignAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DueItem>> DueItemsAsync(CancellationToken cancellationToken);
}
