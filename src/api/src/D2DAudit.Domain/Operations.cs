namespace D2DAudit.Domain;

public sealed class ComputerRecord
{
    public Guid Id { get; set; }

    public string Serial { get; set; } = "";

    public string OperatingSystem { get; set; } = "";

    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public bool Encrypted { get; set; }

    public bool ScreenLock { get; set; }

    public bool Antivirus { get; set; }

    public bool PasswordManager { get; set; }

    public bool AutoUpdate { get; set; }

    public DateTimeOffset LastCheckIn { get; set; }

    public string Source { get; set; } = "Manual";

    public string? ScreenshotName { get; set; }

    public Guid? EvidenceId { get; set; }
}

public sealed class VulnerabilityRecord
{
    public Guid Id { get; set; }

    public string Cve { get; set; } = "";

    public string Title { get; set; } = "";

    public string Severity { get; set; } = "Medium";

    public string AssetName { get; set; } = "";

    public string Source { get; set; } = "";

    public DateTimeOffset FirstSeen { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public DateOnly SlaDue { get; set; }

    public bool FixAvailable { get; set; }

    public bool Ignored { get; set; }

    public string IgnoreReason { get; set; } = "";

    public DateTimeOffset? IgnoredAt { get; set; }
}

public sealed class VulnerabilityPolicy
{
    public Guid Id { get; set; }

    public int CriticalDays { get; set; } = 7;

    public int HighDays { get; set; } = 30;

    public int MediumDays { get; set; } = 60;

    public int LowDays { get; set; } = 90;
}

public sealed class SlaEvent
{
    public Guid Id { get; set; }

    public string Cve { get; set; } = "";

    public string AssetName { get; set; } = "";

    public string EventKind { get; set; } = "";

    public DateOnly DueOn { get; set; }

    public string Detail { get; set; } = "";

    public DateTimeOffset At { get; set; }
}

public sealed class TrustProfile
{
    public Guid Id { get; set; }

    public bool Published { get; set; }

    public string Headline { get; set; } = "";

    public string Summary { get; set; } = "";

    public string ContactEmail { get; set; } = "";
}

public sealed class TrustDocument
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string Summary { get; set; } = "";

    public string Access { get; set; } = "Public";

    public string FileName { get; set; } = "";

    public DateOnly UpdatedOn { get; set; }
}

public sealed class TrustAccessRequest
{
    public Guid Id { get; set; }

    public Guid? DocumentId { get; set; }

    public string RequesterName { get; set; } = "";

    public string RequesterEmail { get; set; } = "";

    public string Company { get; set; } = "";

    public string Purpose { get; set; } = "";

    public string Status { get; set; } = "Pending";

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class AutomationRule
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Trigger { get; set; } = "";

    public string Action { get; set; } = "";

    public string Target { get; set; } = "";

    public bool Enabled { get; set; } = true;
}

public sealed class AutomationDelivery
{
    public Guid Id { get; set; }

    public Guid? RuleId { get; set; }

    public string Trigger { get; set; } = "";

    public string Action { get; set; } = "";

    public string Target { get; set; } = "";

    public string Summary { get; set; } = "";

    public string ExternalId { get; set; } = "";

    public DateTimeOffset At { get; set; }
}
