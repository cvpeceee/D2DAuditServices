namespace D2DAudit.Domain;

public sealed class IntegrationConnection
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Status { get; set; } = "pending";

    public bool Example { get; set; }

    public string Fixture { get; set; } = "";

    public string? ExternalId { get; set; }

    public string? RoleArn { get; set; }

    public string? InstallationId { get; set; }

    public string? AccountLabel { get; set; }

    public string? Domain { get; set; }

    public string? Secret { get; set; }

    public string? SetupHint { get; set; }

    public DateTimeOffset? LastCollectedAt { get; set; }

    public string? LastError { get; set; }
}

public sealed class ChainEntry
{
    public Guid Id { get; set; }

    public long Sequence { get; set; }

    public Guid EvidenceId { get; set; }

    public string ObjectKey { get; set; } = "";

    public string ContentHash { get; set; } = "";

    public string? PreviousHead { get; set; }

    public string Head { get; set; } = "";

    public string Canonical { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ResourceSnapshot
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = "";

    public string Kind { get; set; } = "";

    public string ExternalId { get; set; } = "";

    public string Name { get; set; } = "";

    public string AttributesJson { get; set; } = "{}";

    public DateTimeOffset CollectedAt { get; set; }
}
