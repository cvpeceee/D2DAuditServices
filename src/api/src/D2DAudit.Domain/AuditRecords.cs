namespace D2DAudit.Domain;

public enum SampleStatus
{
    Open,
    Submitted,
    Accepted,
}

public enum FindingSeverity
{
    Exception,
    Observation,
}

public sealed class SampleRequest
{
    public Guid Id { get; set; }

    public Guid EngagementId { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Population { get; set; } = "";

    public int? SampleSize { get; set; }

    public string Source { get; set; } = "";

    public DateOnly DueOn { get; set; }

    public SampleStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public SampleResponse? Response { get; set; }
}

public sealed class SampleResponse
{
    public Guid Id { get; set; }

    public Guid RequestId { get; set; }

    public SampleRequest? Request { get; set; }

    public string Body { get; set; } = "";

    public string RespondedByName { get; set; } = "";

    public DateTimeOffset RespondedAt { get; set; }

    public Guid? EvidenceId { get; set; }
}

public sealed class Finding
{
    public Guid Id { get; set; }

    public Guid EngagementId { get; set; }

    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public FindingSeverity Severity { get; set; }

    public string Detail { get; set; } = "";

    public string RequirementCode { get; set; } = "";

    public string? ManagementResponse { get; set; }

    public string? ResponseByName { get; set; }
}
