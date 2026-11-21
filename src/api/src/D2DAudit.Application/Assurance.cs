using System.Text.Json;

namespace D2DAudit.Application;

public sealed record CustomTestDto(
    Guid Id,
    string Key,
    string Name,
    string Description,
    string ResourceKind,
    string? Provider,
    JsonElement? Filter,
    JsonElement Assertion,
    string Severity,
    IReadOnlyList<string> ControlCodes,
    string Remediation,
    bool Enabled,
    string Outcome,
    string Detail,
    string CreatedByName,
    DateTimeOffset UpdatedAt);

public sealed record SaveCustomTestRequest(
    string Name,
    string? Description,
    string ResourceKind,
    string? Provider,
    JsonElement? Filter,
    JsonElement Assertion,
    string Severity,
    IReadOnlyList<string> ControlCodes,
    string? Remediation,
    bool Enabled = true);

public sealed record PreviewCustomTestRequest(string ResourceKind, string? Provider, JsonElement? Filter, JsonElement Assertion);

public sealed record PreviewResource(string Provider, string Kind, string Name, string ExternalId);

public sealed record CustomTestPreview(
    string Outcome,
    string Detail,
    int Total,
    IReadOnlyList<PreviewResource> Passing,
    IReadOnlyList<PreviewResource> Failing);

public sealed record ResourceTypeDto(string Provider, string Kind, int Count, IReadOnlyList<string> Fields);

public sealed record TestStateDto(string TestKey, string State, string Reason, DateOnly? Until, string ByName, DateTimeOffset At, bool Active);

public sealed record SetTestStateRequest(string State, string? Reason, DateOnly? Until);

public interface ICustomTestPortal
{
    Task<IReadOnlyList<CustomTestDto>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourceTypeDto>> ResourceTypesAsync(CancellationToken cancellationToken);

    Task<Result<CustomTestPreview>> PreviewAsync(PreviewCustomTestRequest request, CancellationToken cancellationToken);

    Task<Result<CustomTestDto>> CreateAsync(SaveCustomTestRequest request, CancellationToken cancellationToken);

    Task<Result<CustomTestDto>> UpdateAsync(Guid id, SaveCustomTestRequest request, CancellationToken cancellationToken);

    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TestStateDto>> StatesAsync(CancellationToken cancellationToken);

    Task<Result<TestStateDto?>> SetStateAsync(string testKey, SetTestStateRequest request, CancellationToken cancellationToken);
}

public sealed record EvidenceRequestDto(
    Guid Id,
    string DocumentCode,
    string DocumentTitle,
    string AssigneeName,
    string AssigneeEmail,
    DateOnly DueOn,
    string Note,
    string Status,
    bool Overdue,
    string RequestedByName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? FulfilledAt);

public sealed record EvidenceDocumentDto(
    Guid Id,
    string Code,
    string Title,
    string Description,
    string OwnerName,
    string OwnerEmail,
    string Cadence,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string Status,
    Guid? CurrentEvidenceId,
    string? CurrentSha256,
    IReadOnlyList<string> ControlCodes,
    IReadOnlyList<EvidenceRequestDto> Requests);

public sealed record EvidenceLibrarySummary(int Total, int Current, int ExpiringSoon, int Expired, int Missing, int OpenRequests);

public sealed record EvidenceLibraryPage(EvidenceLibrarySummary Summary, IReadOnlyList<EvidenceDocumentDto> Documents, IReadOnlyList<EvidenceRequestDto> MyRequests);

public sealed record CreateEvidenceDocumentRequest(
    string Title,
    string? Description,
    string? OwnerName,
    string? OwnerEmail,
    string Cadence,
    IReadOnlyList<string>? ControlCodes);

public sealed record BulkLinkEvidenceRequest(IReadOnlyList<string> DocumentCodes, IReadOnlyList<string> ControlCodes);

public sealed record CreateEvidenceRequestRequest(string AssigneeName, string AssigneeEmail, DateOnly DueOn, string? Note);

public sealed record UploadLibraryEvidenceRequest(string FileName, string ContentBase64, DateOnly? ValidFrom);

public interface IEvidenceLibraryPortal
{
    Task<EvidenceLibraryPage> ListAsync(CancellationToken cancellationToken);

    Task<Result<EvidenceDocumentDto>> CreateAsync(CreateEvidenceDocumentRequest request, CancellationToken cancellationToken);

    Task<Result<int>> LinkAsync(BulkLinkEvidenceRequest request, CancellationToken cancellationToken);

    Task<Result<EvidenceDocumentDto>> RequestAsync(string code, CreateEvidenceRequestRequest request, CancellationToken cancellationToken);

    Task<Result<EvidenceDocumentDto>> UploadAsync(string code, UploadLibraryEvidenceRequest request, CancellationToken cancellationToken);

    Task<Result<int>> CreateRenewalTasksAsync(CancellationToken cancellationToken);
}

public sealed record ReadinessCheckDto(string Key, bool Met, string Detail);

public sealed record ControlProfileDto(string ImplementationGuidance, string EvidenceRequirement, int RequiredEvidenceCount, bool RequiresPassingTest);

public sealed record ControlReadinessDto(
    string ControlCode,
    bool Ready,
    int Percent,
    IReadOnlyList<ReadinessCheckDto> Checks,
    ControlProfileDto Profile,
    IReadOnlyList<EvidenceDocumentDto> LibraryEvidence);

public sealed record UpdateControlProfileRequest(string? ImplementationGuidance, string? EvidenceRequirement, int RequiredEvidenceCount, bool RequiresPassingTest);

public sealed record ControlCommentRevisionDto(string Body, DateTimeOffset ReplacedAt);

public sealed record ControlCommentDto(
    Guid Id,
    string AuthorName,
    string AuthorEmail,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    IReadOnlyList<ControlCommentRevisionDto> History);

public sealed record ControlCommentRequest(string Body);

public interface IControlInsightPortal
{
    Task<Result<ControlReadinessDto>> ReadinessAsync(string code, CancellationToken cancellationToken);

    Task<Result<ControlReadinessDto>> UpdateProfileAsync(string code, UpdateControlProfileRequest request, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ControlCommentDto>>> CommentsAsync(string code, CancellationToken cancellationToken);

    Task<Result<ControlCommentDto>> AddCommentAsync(string code, ControlCommentRequest request, CancellationToken cancellationToken);

    Task<Result<ControlCommentDto>> EditCommentAsync(string code, Guid id, ControlCommentRequest request, CancellationToken cancellationToken);
}

public sealed record PolicyDiffLineDto(string Kind, string Text, int? OldNumber, int? NewNumber);

public sealed record PolicyDiffDto(string Code, int From, int To, int Added, int Removed, IReadOnlyList<PolicyDiffLineDto> Lines);

public sealed record AttestationPersonDto(string PersonName, string PersonEmail, DateTimeOffset? AttestedAt);

public sealed record AttestationCampaignDto(
    Guid Id,
    string PolicyCode,
    int VersionNumber,
    string Group,
    DateOnly StartedOn,
    DateOnly DueOn,
    DateOnly RenewOn,
    string Status,
    int Attested,
    int Total,
    IReadOnlyList<AttestationPersonDto> People);

public sealed record StartAttestationRequest(string Group, DateOnly DueOn);

public interface IPolicyInsightPortal
{
    Task<Result<PolicyDiffDto>> DiffAsync(string code, int from, int to, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AttestationCampaignDto>>> AttestationsAsync(string code, CancellationToken cancellationToken);

    Task<Result<AttestationCampaignDto>> StartAttestationAsync(string code, StartAttestationRequest request, CancellationToken cancellationToken);

    Task<Result<AttestationCampaignDto>> RenewAttestationAsync(string code, Guid id, CancellationToken cancellationToken);

    Task<Result<AttestationCampaignDto>> AttestAsync(string code, CancellationToken cancellationToken);
}
