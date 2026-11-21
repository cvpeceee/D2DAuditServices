namespace D2DAudit.Application;

public sealed record AccessRevocationRequest(string PersonEmail, string PersonName, string Provider, string AccountName, string StepCode);

public sealed record AccessRevocationResult(bool Succeeded, string Mode, string Receipt, string Detail);

/// <summary>Removes a leaver's access at an identity provider or connected app.</summary>
public interface IAccessRevoker
{
    Task<AccessRevocationResult> RevokeAsync(AccessRevocationRequest request, CancellationToken cancellationToken);
}

public sealed record LifecycleSettingsDto(int OffboardingAccessSlaHours, int OffboardingCompleteSlaHours, int OnboardingDays);

public sealed record EmployeeGroupDto(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<string> Departments,
    IReadOnlyList<string> RoleKeywords,
    IReadOnlyList<string> PolicyCodes,
    IReadOnlyList<string> TrainingCodes,
    int MemberCount);

public sealed record SaveEmployeeGroupRequest(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string>? Departments,
    IReadOnlyList<string>? RoleKeywords,
    IReadOnlyList<string>? PolicyCodes,
    IReadOnlyList<string>? TrainingCodes);

public sealed record LifecycleTemplateStepDto(
    Guid Id,
    string Kind,
    string Code,
    string Title,
    string Category,
    int SlaHours,
    int Order,
    string GroupCode,
    bool RevokesAccess,
    bool PerAccount);

public sealed record SaveLifecycleTemplateStepRequest(
    Guid? Id,
    string Kind,
    string Code,
    string Title,
    string Category,
    int SlaHours,
    string? GroupCode,
    bool RevokesAccess,
    bool PerAccount);

public sealed record LifecycleStepDto(
    Guid Id,
    string Code,
    string Title,
    string Category,
    string Provider,
    string AccountName,
    bool RevokesAccess,
    DateTimeOffset DueAt,
    bool Done,
    DateTimeOffset? CompletedAt,
    string CompletedBy,
    string Result,
    Guid? EvidenceId,
    string Sla);

public sealed record LifecycleChecklistDto(
    Guid Id,
    Guid PersonId,
    string PersonName,
    string PersonEmail,
    string Kind,
    string Trigger,
    string Reason,
    DateOnly EffectiveOn,
    DateTimeOffset TriggeredAt,
    DateTimeOffset DueAt,
    DateTimeOffset? AccessDueAt,
    DateTimeOffset? AccessRevokedAt,
    DateTimeOffset? CompletedAt,
    string State,
    string Sla,
    string AccessSla,
    int DoneCount,
    int TotalCount,
    IReadOnlyList<string> Groups,
    IReadOnlyList<LifecycleStepDto> Steps);

public sealed record LifecycleTestDto(string Name, string ControlCode, string Outcome, string Detail);

public sealed record LifecycleOverviewDto(
    LifecycleSettingsDto Settings,
    IReadOnlyList<EmployeeGroupDto> Groups,
    IReadOnlyList<LifecycleTemplateStepDto> Templates,
    IReadOnlyList<LifecycleChecklistDto> Checklists,
    IReadOnlyList<LifecycleTestDto> Tests,
    string RevokerMode);

public sealed record PersonLifecycleDto(
    Guid PersonId,
    string Name,
    string Email,
    string State,
    DateOnly? HireOn,
    DateOnly? TerminationOn,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> RequiredPolicies,
    IReadOnlyList<string> RequiredTraining,
    IReadOnlyList<LifecycleChecklistDto> Checklists);

public sealed record StartOffboardingRequest(DateOnly? TerminationOn, string? Reason);

public sealed record StartOnboardingRequest(DateOnly? StartOn);

public sealed record CompleteLifecycleStepRequest(bool Done, string? Note);

public sealed record LifecycleSyncResult(int OnboardingStarted, int OffboardingStarted, IReadOnlyList<LifecycleTestDto> Tests);

public sealed record RevokeAllResult(int Revoked, int Failed, LifecycleChecklistDto Checklist);

public interface ILifecycle
{
    Task<LifecycleOverviewDto> OverviewAsync(CancellationToken cancellationToken);

    Task<Result<LifecycleSettingsDto>> SaveSettingsAsync(LifecycleSettingsDto request, CancellationToken cancellationToken);

    Task<Result<EmployeeGroupDto>> SaveGroupAsync(SaveEmployeeGroupRequest request, CancellationToken cancellationToken);

    Task<Result<LifecycleTemplateStepDto>> SaveTemplateStepAsync(SaveLifecycleTemplateStepRequest request, CancellationToken cancellationToken);

    Task<Result<bool>> DeleteTemplateStepAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<LifecycleSyncResult>> SyncHrisAsync(CancellationToken cancellationToken);

    Task<Result<PersonLifecycleDto>> PersonAsync(Guid personId, CancellationToken cancellationToken);

    Task<Result<LifecycleChecklistDto>> StartOffboardingAsync(Guid personId, StartOffboardingRequest request, CancellationToken cancellationToken);

    Task<Result<LifecycleChecklistDto>> StartOnboardingAsync(Guid personId, StartOnboardingRequest request, CancellationToken cancellationToken);

    Task<Result<LifecycleChecklistDto>> CompleteStepAsync(Guid stepId, CompleteLifecycleStepRequest request, CancellationToken cancellationToken);

    Task<Result<LifecycleChecklistDto>> RevokeStepAsync(Guid stepId, CancellationToken cancellationToken);

    Task<Result<RevokeAllResult>> RevokeAllAsync(Guid checklistId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<LifecycleTestDto>>> RefreshTestsAsync(CancellationToken cancellationToken);

    Task<PersonLifecycleDto?> MineAsync(CancellationToken cancellationToken);

    Task<Result<LifecycleChecklistDto>> CompleteMyStepAsync(Guid stepId, CompleteLifecycleStepRequest request, CancellationToken cancellationToken);
}
