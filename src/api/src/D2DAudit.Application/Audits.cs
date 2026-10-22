namespace D2DAudit.Application;

public sealed record AuditEngagementDto(
    string Name,
    string FirmName,
    string FrameworkKey,
    string Scope,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly FieldworkOn,
    int ControlsPassing,
    int ControlsTotal,
    int RequestsOpen,
    int RequestsTotal,
    int FindingsTotal,
    int Exceptions,
    double? AverageResponseHours);

public sealed record EvidenceChoiceDto(Guid Id, string Title, string ControlCode);

public sealed record SampleResponseDto(string Body, string RespondedByName, DateTimeOffset RespondedAt, string? EvidenceTitle);

public sealed record SampleRequestDto(
    string Code,
    string Title,
    string Population,
    int? SampleSize,
    string Source,
    DateOnly DueOn,
    string Status,
    SampleResponseDto? Response);

public sealed record FindingDto(
    string Code,
    string Title,
    string Severity,
    string Detail,
    string RequirementCode,
    string? ManagementResponse,
    string? ResponseByName,
    IReadOnlyList<FindingCommentDto> Comments);

public sealed record FindingCommentDto(string AuthorName, string Body, DateTimeOffset At);

public sealed record AuditPortalPage(
    AuditEngagementDto? Engagement,
    IReadOnlyList<SampleRequestDto> Requests,
    IReadOnlyList<FindingDto> Findings,
    IReadOnlyList<EvidenceChoiceDto> Evidence);

public sealed record CreateSampleRequest(string Title, string Population, int? SampleSize, string Source, DateOnly DueOn);

public sealed record RespondSampleRequest(string Body, Guid? EvidenceId);

public sealed record CreateFindingRequest(string Title, string Severity, string Detail, string RequirementCode);

public sealed record ManagementResponseRequest(string Response);

public sealed record PackageFile(byte[] Bytes, string FileName);

public interface IAuditPortal
{
    Task<AuditPortalPage> GetAsync(CancellationToken cancellationToken);

    Task<Result<SampleRequestDto>> CreateRequestAsync(CreateSampleRequest request, CancellationToken cancellationToken);

    Task<Result<SampleRequestDto>> RespondAsync(string code, RespondSampleRequest request, CancellationToken cancellationToken);

    Task<Result<SampleRequestDto>> AcceptAsync(string code, CancellationToken cancellationToken);

    Task<Result<FindingDto>> CreateFindingAsync(CreateFindingRequest request, CancellationToken cancellationToken);

    Task<Result<FindingDto>> RespondToFindingAsync(string code, ManagementResponseRequest request, CancellationToken cancellationToken);

    Task<Result<Guid>> StartExportAsync(CancellationToken cancellationToken);

    Task<Result<PackageFile>> OpenPackageAsync(Guid jobId, CancellationToken cancellationToken);
}
