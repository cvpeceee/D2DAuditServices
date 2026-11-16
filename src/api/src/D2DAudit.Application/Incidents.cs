namespace D2DAudit.Application;

public sealed record IncidentEventDto(string Kind, string Detail, string AuthorName, DateTimeOffset At);

public sealed record IncidentItem(
    Guid Id,
    string Code,
    string Title,
    string Severity,
    string Status,
    string Description,
    string ReporterName,
    string ReporterEmail,
    DateTimeOffset ReportedAt,
    DateOnly SlaDue,
    DateTimeOffset? ResolvedAt,
    string RootCause,
    string ControlCode,
    Guid? EvidenceId,
    bool WithinSla,
    IReadOnlyList<IncidentEventDto> Timeline);

public sealed record ReportIncidentRequest(string Title, string Severity, string Description);

public sealed record UpdateIncidentRequest(string? Status, string? RootCause, string? ControlCode, Guid? EvidenceId, string? Note);

public interface IIncidents
{
    Task<IReadOnlyList<IncidentItem>> ListAsync(CancellationToken cancellationToken);

    Task<Result<IncidentItem>> ReportAsync(ReportIncidentRequest request, CancellationToken cancellationToken);

    Task<Result<IncidentItem>> UpdateAsync(Guid id, UpdateIncidentRequest request, CancellationToken cancellationToken);
}
