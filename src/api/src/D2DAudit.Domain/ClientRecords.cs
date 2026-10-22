namespace D2DAudit.Domain;

public sealed class CheckWeek
{
    public Guid Id { get; set; }

    public DateOnly WeekOf { get; set; }

    public int Passing { get; set; }

    public int Failing { get; set; }
}

public sealed class WorkspaceNotification
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public string Href { get; set; } = "";

    public DateTimeOffset At { get; set; }
}

public sealed class EnrolledDevice
{
    public Guid Id { get; set; }

    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public string Platform { get; set; } = "";

    public string Serial { get; set; } = "";

    public DateOnly LastSeenOn { get; set; }
}

public sealed class FindingComment
{
    public Guid Id { get; set; }

    public Guid FindingId { get; set; }

    public Finding? Finding { get; set; }

    public string AuthorName { get; set; } = "";

    public string Body { get; set; } = "";

    public DateTimeOffset At { get; set; }
}
