namespace D2DAudit.Application;

public sealed record CreateEnrollmentTokenRequest(string? PersonEmail);

public sealed record EnrollmentTokenCreated(Guid Id, string Token, string PersonEmail, string PersonName, DateTimeOffset ExpiresAt);

public sealed record EnrollmentTokenItem(Guid Id, string Hint, string PersonEmail, string PersonName, string CreatedByEmail, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string State);

public sealed record AgentEnrollRequest(string Token, string Serial, string? Hostname, string OperatingSystem, string Platform, string? AgentKind, string? AgentVersion);

public sealed record AgentEnrollResult(string DeviceId, string Secret, string CheckInPath, int IntervalHours, string PersonEmail);

public sealed record AgentCheckInResult(string Serial, int Passing, int Failing, DateTimeOffset At, int NextCheckInHours);

public sealed record AgentCheckView(string Key, string Title, string ControlCode, bool Pass, string Detail, IReadOnlyList<string> Remediation);

public sealed record AgentDeviceItem(
    Guid Id,
    string Serial,
    string Hostname,
    string Platform,
    string OperatingSystem,
    string PersonEmail,
    string PersonName,
    string AgentKind,
    string AgentVersion,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastSeenAt,
    bool Stale,
    bool Revoked,
    int Passing,
    int Failing,
    IReadOnlyList<AgentCheckView> Checks);

public sealed record PostureHistoryItem(DateTimeOffset At, string AgentKind, string AgentVersion, int Passing, int Failing, IReadOnlyList<AgentCheckView> Checks);

public enum AgentAuthFailure
{
    None,
    Unauthorized,
    Expired,
    Replayed,
    Invalid,
}

public sealed record AgentCall<T>(T? Value, AgentAuthFailure Auth, string? Error)
{
    public static AgentCall<T> Ok(T value) => new(value, AgentAuthFailure.None, null);

    public static AgentCall<T> Denied(AgentAuthFailure failure) => new(default, failure, null);

    public static AgentCall<T> Fail(string error) => new(default, AgentAuthFailure.None, error);
}

public interface IDeviceAgent
{
    Task<Result<EnrollmentTokenCreated>> CreateTokenAsync(CreateEnrollmentTokenRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<EnrollmentTokenItem>> TokensAsync(CancellationToken cancellationToken);

    Task<Result<EnrollmentTokenItem>> RevokeTokenAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentDeviceItem>> DevicesAsync(bool mine, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<PostureHistoryItem>>> HistoryAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<AgentDeviceItem>> RevokeDeviceAsync(Guid id, CancellationToken cancellationToken);

    Task<AgentCall<AgentEnrollResult>> EnrollAsync(AgentEnrollRequest request, CancellationToken cancellationToken);

    Task<AgentCall<AgentCheckInResult>> CheckInAsync(string? deviceId, string? timestamp, string? signature, string body, CancellationToken cancellationToken);
}

public sealed record AcademyModuleItem(
    string Key,
    string Title,
    string Summary,
    int Minutes,
    bool Required,
    bool FrameworkSpecific,
    IReadOnlyList<string> Audiences,
    int SectionsTotal,
    int SectionsCompleted,
    string Status,
    int BestScore,
    int QuestionCount,
    DateTimeOffset? PassedAt,
    DateOnly? ExpiresOn,
    string? CertificateNumber);

public sealed record AcademyOverview(IReadOnlyList<AcademyModuleItem> Modules, int Required, int Current, int PassMark, int RenewalMonths);

public sealed record AcademySectionDto(string Heading, string Body);

public sealed record AcademyQuestionDto(string Prompt, IReadOnlyList<string> Choices);

public sealed record AcademyModuleDetail(AcademyModuleItem Module, IReadOnlyList<AcademySectionDto> Sections, IReadOnlyList<AcademyQuestionDto> Questions);

public sealed record AcademyProgressRequest(int SectionsCompleted);

public sealed record AcademyQuizRequest(IReadOnlyList<int> Answers);

public sealed record AcademyQuizResult(
    int Correct,
    int Total,
    int PassMark,
    bool Passed,
    IReadOnlyList<int> Wrong,
    IReadOnlyList<string> Explanations,
    DateOnly? ExpiresOn,
    string? CertificateNumber,
    Guid? EvidenceId);

public sealed record AcademyAssignmentItem(string ModuleKey, string Title, bool Available, IReadOnlyList<string> Audiences, int Assigned, int Current);

public sealed record SaveAcademyAssignmentRequest(IReadOnlyList<string> Audiences);

public interface IAcademy
{
    Task<AcademyOverview> OverviewAsync(CancellationToken cancellationToken);

    Task<Result<AcademyModuleDetail>> ModuleAsync(string key, CancellationToken cancellationToken);

    Task<Result<AcademyModuleItem>> ProgressAsync(string key, AcademyProgressRequest request, CancellationToken cancellationToken);

    Task<Result<AcademyQuizResult>> QuizAsync(string key, AcademyQuizRequest request, CancellationToken cancellationToken);

    Task<Result<FileExport>> CertificateAsync(string key, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AcademyAssignmentItem>>> AssignmentsAsync(CancellationToken cancellationToken);

    Task<Result<AcademyAssignmentItem>> SaveAssignmentAsync(string key, SaveAcademyAssignmentRequest request, CancellationToken cancellationToken);
}
