using D2DAudit.Domain;

namespace D2DAudit.Application;

public sealed record ComputerItem(
    Guid Id,
    string Serial,
    string OperatingSystem,
    string OwnerName,
    string OwnerEmail,
    bool Encrypted,
    bool ScreenLock,
    bool Antivirus,
    bool PasswordManager,
    bool AutoUpdate,
    DateTimeOffset LastCheckIn,
    string Source,
    string? ScreenshotName,
    Guid? EvidenceId);

public sealed record SaveComputerRequest(
    Guid? Id,
    string Serial,
    string OperatingSystem,
    string OwnerName,
    string OwnerEmail,
    bool Encrypted,
    bool ScreenLock,
    bool Antivirus,
    bool PasswordManager,
    bool AutoUpdate,
    string Source);

public sealed record ScreenshotRequest(string FileName, string ContentBase64);

public sealed record VulnerabilityItem(
    Guid Id,
    string Cve,
    string Title,
    string Severity,
    string AssetName,
    string Source,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    DateOnly SlaDue,
    bool FixAvailable,
    bool Ignored,
    string IgnoreReason,
    bool Overdue);

public sealed record VulnerabilityAssetGroup(string AssetName, int Open, int Critical, int High, IReadOnlyList<VulnerabilityItem> Items);

public sealed record SlaHistoryItem(string Cve, string AssetName, string EventKind, DateOnly DueOn, string Detail, DateTimeOffset At);

public sealed record VulnerabilitySlaDto(int CriticalDays, int HighDays, int MediumDays, int LowDays);

public sealed record VulnerabilityBoard(
    VulnerabilitySlaDto Sla,
    IReadOnlyList<VulnerabilityItem> Findings,
    IReadOnlyList<VulnerabilityAssetGroup> Assets,
    IReadOnlyList<VulnerabilityItem> Ignored,
    IReadOnlyList<SlaHistoryItem> History);

public sealed record IgnoreVulnerabilityRequest(string Reason);

public sealed record TrustDocumentDto(Guid Id, string Title, string Summary, string Access, string FileName, DateOnly UpdatedOn);

public sealed record TrustRequestDto(
    Guid Id,
    Guid? DocumentId,
    string DocumentTitle,
    string RequesterName,
    string RequesterEmail,
    string Company,
    string Purpose,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);

public sealed record TrustFrameworkDto(string Key, string Name, int Controls, int Passing);

public sealed record TrustPublicPage(
    string Slug,
    string TenantName,
    string Headline,
    string Summary,
    string ContactEmail,
    IReadOnlyList<TrustFrameworkDto> Frameworks,
    IReadOnlyList<TrustDocumentDto> Documents);

public sealed record TrustAdminPage(
    string Slug,
    bool Published,
    string Headline,
    string Summary,
    string ContactEmail,
    IReadOnlyList<TrustDocumentDto> Documents,
    IReadOnlyList<TrustRequestDto> Requests);

public sealed record SaveTrustRequest(bool Published, string Headline, string Summary, string ContactEmail);

public sealed record SaveTrustDocumentRequest(Guid? Id, string Title, string Summary, string Access, string FileName);

public sealed record TrustAccessRequestBody(Guid? DocumentId, string Name, string Email, string Company, string Purpose);

public sealed record DecideTrustRequest(string Status);

public sealed record AutomationRuleDto(Guid Id, string Name, string Trigger, string Action, string Target, bool Enabled);

public sealed record SaveAutomationRuleRequest(Guid? Id, string Name, string Trigger, string Action, string Target, bool Enabled);

public sealed record AutomationDeliveryDto(Guid Id, string Trigger, string Action, string Target, string Summary, string ExternalId, DateTimeOffset At);

public sealed record CreateControlRequest(
    string Code,
    string Title,
    string Description,
    string Frequency,
    string OwnerName,
    string OwnerEmail,
    string Framework,
    string Requirement,
    string Coverage);

public sealed record CreatedControl(string Code, string Title, bool Custom);

public sealed record ImportControlsRequest(string Csv);

public sealed record ControlImportRow(
    string Code,
    string Title,
    string Description,
    string Frequency,
    string OwnerName,
    string OwnerEmail,
    string Framework,
    string Requirement,
    string Coverage);

public interface IIssueTracker
{
    Task<string> CreateIssueAsync(string target, string summary, string detail, CancellationToken cancellationToken);
}

public interface INotifier
{
    Task<string> SendAsync(string channel, string target, string summary, string detail, CancellationToken cancellationToken);
}

public interface IAutomationDispatcher
{
    Task DispatchAsync(string trigger, string summary, string detail, CancellationToken cancellationToken);

    Task DispatchForTenantAsync(Guid tenantId, string databaseName, string trigger, string summary, string detail, CancellationToken cancellationToken);
}

public interface IOperations
{
    Task<IReadOnlyList<ComputerItem>> ComputersAsync(CancellationToken cancellationToken);

    Task<Result<ComputerItem>> SaveComputerAsync(SaveComputerRequest request, CancellationToken cancellationToken);

    Task<Result<ComputerItem>> UploadScreenshotAsync(Guid id, ScreenshotRequest request, CancellationToken cancellationToken);

    Task<VulnerabilityBoard> VulnerabilitiesAsync(CancellationToken cancellationToken);

    Task<Result<VulnerabilityItem>> IgnoreVulnerabilityAsync(Guid id, IgnoreVulnerabilityRequest request, CancellationToken cancellationToken);

    Task<Result<VulnerabilitySlaDto>> SaveSlaAsync(VulnerabilitySlaDto request, CancellationToken cancellationToken);

    Task<TrustAdminPage> TrustAdminAsync(CancellationToken cancellationToken);

    Task<Result<TrustAdminPage>> SaveTrustAsync(SaveTrustRequest request, CancellationToken cancellationToken);

    Task<Result<TrustDocumentDto>> SaveTrustDocumentAsync(SaveTrustDocumentRequest request, CancellationToken cancellationToken);

    Task<Result<TrustRequestDto>> DecideTrustAsync(Guid id, DecideTrustRequest request, CancellationToken cancellationToken);

    Task<TrustPublicPage?> TrustPublicAsync(string slug, CancellationToken cancellationToken);

    Task<Result<TrustRequestDto>> RequestTrustAccessAsync(string slug, TrustAccessRequestBody request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AutomationRuleDto>> RulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AutomationDeliveryDto>> DeliveriesAsync(CancellationToken cancellationToken);

    Task<Result<AutomationRuleDto>> SaveRuleAsync(SaveAutomationRuleRequest request, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AutomationDeliveryDto>>> RunDueAsync(CancellationToken cancellationToken);

    Task<Result<CreatedControl>> CreateControlAsync(CreateControlRequest request, CancellationToken cancellationToken);

    Task<Result<int>> ImportControlsAsync(ImportControlsRequest request, CancellationToken cancellationToken);
}

public static class ControlCsv
{
    public static Result<IReadOnlyList<ControlImportRow>> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Result<IReadOnlyList<ControlImportRow>>.Fail("invalid_import");
        }

        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            return Result<IReadOnlyList<ControlImportRow>>.Fail("invalid_import");
        }

        var header = Split(lines[0]);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            index[header[i]] = i;
        }

        string[] required = ["code", "title", "description", "frequency", "framework", "requirement"];
        if (required.Any(name => !index.ContainsKey(name)))
        {
            return Result<IReadOnlyList<ControlImportRow>>.Fail("invalid_import");
        }

        var rows = new List<ControlImportRow>();
        for (var line = 1; line < lines.Length; line++)
        {
            var cells = Split(lines[line]);
            string Cell(string name) => index.TryGetValue(name, out var at) && at < cells.Count ? cells[at].Trim() : "";
            var coverage = Cell("coverage");
            if (coverage.Length == 0)
            {
                coverage = "full";
            }

            var row = new ControlImportRow(
                Cell("code"),
                Cell("title"),
                Cell("description"),
                Cell("frequency"),
                Cell("ownerName"),
                Cell("ownerEmail"),
                Cell("framework"),
                Cell("requirement"),
                coverage);
            if (row.Code.Length is 0 or > 32 || row.Title.Length is 0 or > 300 || row.Framework.Length == 0 || row.Requirement.Length == 0)
            {
                return Result<IReadOnlyList<ControlImportRow>>.Fail("invalid_import");
            }

            if (coverage is not ("full" or "partial"))
            {
                return Result<IReadOnlyList<ControlImportRow>>.Fail("invalid_import");
            }

            rows.Add(row);
        }

        return rows.Count == 0
            ? Result<IReadOnlyList<ControlImportRow>>.Fail("invalid_import")
            : Result<IReadOnlyList<ControlImportRow>>.Ok(rows);
    }

    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var index = 0;
        while (index < line.Length)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }

            index++;
        }

        cells.Add(current.ToString());
        return cells;
    }
}
