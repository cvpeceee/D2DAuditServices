namespace D2DAudit.Domain;

public sealed record NormalizedResource(
    string Provider,
    string Kind,
    string ExternalId,
    string Name,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record CheckSpec(
    string Key,
    string Name,
    string ControlCode,
    IReadOnlyList<string> Requirements,
    string Remediation);

public readonly record struct CheckResult(TestOutcome Outcome, string Detail);

public static class AutomatedChecks
{
    public static readonly IReadOnlyList<CheckSpec> All =
    [
        new(
            "mfa-all-users",
            "MFA enforced for all users",
            "AC-07",
            ["soc2 CC6.1", "iso27001 A.5.17", "iso27001 A.8.5"],
            "Turn on MFA for every active user. Suspend accounts that cannot enroll."),
        new(
            "root-mfa",
            "Root account MFA",
            "AC-07",
            ["soc2 CC6.1", "iso27001 A.5.17"],
            "Enable MFA on the AWS root account and keep the device with the break-glass owners."),
        new(
            "s3-public-access",
            "S3 public access blocked",
            "CFG-01",
            ["soc2 CC6.1", "iso27001 A.8.3"],
            "Turn on Block Public Access for every bucket."),
        new(
            "cloudtrail-enabled",
            "CloudTrail enabled",
            "LG-03",
            ["soc2 CC7.2", "iso27001 A.8.15"],
            "Create a trail that is logging, and keep it from being stopped."),
        new(
            "encryption-at-rest",
            "Encryption at rest",
            "CR-02",
            ["soc2 CC6.7", "iso27001 A.8.24"],
            "Turn on default encryption for every bucket."),
        new(
            "branch-protection",
            "Branch protection on the default branch",
            "CM-11",
            ["soc2 CC8.1", "iso27001 A.8.4", "iso27001 A.8.32"],
            "Require a pull request on the default branch of every repository."),
        new(
            "offboarded-removed",
            "Offboarded users removed",
            "HR-08",
            ["soc2 CC6.2", "soc2 CC6.3", "iso27001 A.6.5"],
            "Remove or suspend the leaver in every connected system."),
        new(
            "azure-storage-public",
            "Azure storage public access blocked",
            "CFG-01",
            ["soc2 CC6.1", "iso27001 A.8.3"],
            "Turn off public blob access on every storage account."),
        new(
            "azure-key-vault",
            "Azure Key Vault is in use",
            "CR-02",
            ["soc2 CC6.7", "iso27001 A.8.24"],
            "Create a Key Vault with soft delete for the subscription."),
        new(
            "azure-activity-log",
            "Azure activity log alerts are on",
            "LG-03",
            ["soc2 CC7.2", "iso27001 A.8.15"],
            "Create an activity log alert so subscription changes are recorded."),
        new(
            "azure-network-rules",
            "Azure network rules are not wide open",
            "CFG-01",
            ["soc2 CC6.6", "iso27001 A.8.20"],
            "Remove inbound rules that allow SSH or RDP from the internet."),
        new(
            "entra-mfa",
            "Entra ID MFA for active users",
            "AC-07",
            ["soc2 CC6.1", "iso27001 A.8.5"],
            "Turn on MFA for every active Entra ID user."),
        new(
            "gcp-bucket-public",
            "GCP bucket public access blocked",
            "CFG-01",
            ["soc2 CC6.1", "iso27001 A.8.3"],
            "Enforce public access prevention on every bucket."),
        new(
            "gcp-kms",
            "GCP KMS keys are present",
            "CR-02",
            ["soc2 CC6.7", "iso27001 A.8.24"],
            "Create a Cloud KMS key ring for customer data."),
        new(
            "gcp-audit-log",
            "GCP audit logs are exported",
            "LG-03",
            ["soc2 CC7.2", "iso27001 A.8.15"],
            "Create a logging sink for admin activity."),
        new(
            "gcp-iam-public",
            "GCP IAM is not public",
            "AC-07",
            ["soc2 CC6.1", "iso27001 A.5.15"],
            "Remove allUsers and allAuthenticatedUsers from the project policy."),
    ];

    public static CheckResult Evaluate(string key, IReadOnlyList<NormalizedResource> resources) => key switch
    {
        "mfa-all-users" => Users(resources, static user => Flag(user, "status") != "suspended" && Flag(user, "offboarded") != "true", "mfa", "true", "No active users were collected.", "These active users have no MFA"),
        "root-mfa" => AllOf(resources, "root", "mfa", "true", "No AWS root account was collected.", "Root MFA is off"),
        "s3-public-access" => AllOf(resources, "bucket", "public", "false", "No buckets were collected.", "Public access is not blocked"),
        "cloudtrail-enabled" => AnyOf(resources, "trail", "enabled", "true", "No CloudTrail trails were collected.", "CloudTrail is logging."),
        "encryption-at-rest" => AllOf(resources, "bucket", "encrypted", "true", "No buckets were collected.", "Encryption at rest is off"),
        "branch-protection" => AllOf(resources, "repository", "protected", "true", "No repositories were collected.", "The default branch is not protected"),
        "offboarded-removed" => Leavers(resources),
        "azure-storage-public" => ProviderAll(resources, "azure", "bucket", "public", "false", "No Azure storage accounts were collected.", "Public blob access is on"),
        "azure-key-vault" => ProviderAny(resources, "azure", "keyVault", "enabled", "true", "No Azure Key Vault was collected.", "A Key Vault is enabled."),
        "azure-activity-log" => ProviderAny(resources, "azure", "trail", "enabled", "true", "No Azure activity log was collected.", "Activity log alerts are on."),
        "azure-network-rules" => ProviderAll(resources, "azure", "network", "open", "false", "No Azure network rules were collected.", "A network rule allows the internet"),
        "entra-mfa" => ProviderUsers(resources, "azure"),
        "gcp-bucket-public" => ProviderAll(resources, "gcp", "bucket", "public", "false", "No GCP buckets were collected.", "Public access prevention is off"),
        "gcp-kms" => ProviderAny(resources, "gcp", "kms", "enabled", "true", "No GCP KMS inventory was collected.", "Cloud KMS key rings are present."),
        "gcp-audit-log" => ProviderAny(resources, "gcp", "trail", "enabled", "true", "No GCP audit log was collected.", "An audit log sink is present."),
        "gcp-iam-public" => ProviderAll(resources, "gcp", "iam", "public", "false", "No GCP IAM policy was collected.", "The project policy is public"),
        _ => throw new InvalidOperationException($"Unknown automated check {key}."),
    };

    public static IReadOnlyList<NormalizedResource> Inputs(string key, IReadOnlyList<NormalizedResource> resources) => key switch
    {
        "root-mfa" => resources.Where(item => item.Kind == "root").ToArray(),
        "s3-public-access" or "encryption-at-rest" => resources.Where(item => item.Kind == "bucket").ToArray(),
        "cloudtrail-enabled" => resources.Where(item => item.Kind == "trail").ToArray(),
        "branch-protection" => resources.Where(item => item.Kind == "repository").ToArray(),
        "azure-storage-public" => Of(resources, "azure", "bucket"),
        "gcp-bucket-public" => Of(resources, "gcp", "bucket"),
        "azure-key-vault" => Of(resources, "azure", "keyVault"),
        "azure-activity-log" => Of(resources, "azure", "trail"),
        "azure-network-rules" => Of(resources, "azure", "network"),
        "entra-mfa" => Of(resources, "azure", "user"),
        "gcp-kms" => Of(resources, "gcp", "kms"),
        "gcp-audit-log" => Of(resources, "gcp", "trail"),
        "gcp-iam-public" => Of(resources, "gcp", "iam"),
        _ => resources.Where(item => item.Kind == "user").ToArray(),
    };

    private static NormalizedResource[] Of(IReadOnlyList<NormalizedResource> resources, string provider, string kind) =>
        resources.Where(item => item.Provider == provider && item.Kind == kind).ToArray();

    private static CheckResult ProviderAll(
        IReadOnlyList<NormalizedResource> resources,
        string provider,
        string kind,
        string attribute,
        string expected,
        string empty,
        string failure)
    {
        var rows = Of(resources, provider, kind);
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.NotRun, empty);
        }

        var bad = rows.Where(item => Flag(item, attribute) != expected).Select(item => item.Name).ToArray();
        return bad.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{rows.Length} {provider} {kind} resources match.")
            : new CheckResult(TestOutcome.Fail, $"{failure}: {string.Join(", ", bad)}.");
    }

    private static CheckResult ProviderAny(
        IReadOnlyList<NormalizedResource> resources,
        string provider,
        string kind,
        string attribute,
        string expected,
        string empty,
        string success)
    {
        var rows = Of(resources, provider, kind);
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.NotRun, empty);
        }

        return rows.Any(item => Flag(item, attribute) == expected)
            ? new CheckResult(TestOutcome.Pass, success)
            : new CheckResult(TestOutcome.Fail, $"No {provider} {kind} has {attribute} {expected}.");
    }

    private static CheckResult ProviderUsers(IReadOnlyList<NormalizedResource> resources, string provider)
    {
        var users = Of(resources, provider, "user").Where(item => Flag(item, "status") != "suspended" && Flag(item, "offboarded") != "true").ToArray();
        if (users.Length == 0)
        {
            return new CheckResult(TestOutcome.NotRun, "No active " + provider + " users were collected.");
        }

        var missing = users.Where(item => Flag(item, "mfa") != "true").Select(item => item.Name).ToArray();
        return missing.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{users.Length} active users match.")
            : new CheckResult(TestOutcome.Fail, $"These active users have no MFA: {string.Join(", ", missing)}.");
    }

    private static CheckResult Users(
        IReadOnlyList<NormalizedResource> resources,
        Func<NormalizedResource, bool> include,
        string attribute,
        string expected,
        string empty,
        string failure)
    {
        var users = resources.Where(item => item.Kind == "user" && include(item)).ToArray();
        if (users.Length == 0)
        {
            return new CheckResult(TestOutcome.NotRun, empty);
        }

        var missing = users.Where(item => Flag(item, attribute) != expected).Select(item => item.Name).ToArray();
        return missing.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{users.Length} active users match.")
            : new CheckResult(TestOutcome.Fail, $"{failure}: {string.Join(", ", missing)}.");
    }

    private static CheckResult AllOf(
        IReadOnlyList<NormalizedResource> resources,
        string kind,
        string attribute,
        string expected,
        string empty,
        string failure)
    {
        var rows = resources.Where(item => item.Kind == kind).ToArray();
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.NotRun, empty);
        }

        var bad = rows.Where(item => Flag(item, attribute) != expected).Select(item => item.Name).ToArray();
        return bad.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{rows.Length} {kind} resources match.")
            : new CheckResult(TestOutcome.Fail, $"{failure}: {string.Join(", ", bad)}.");
    }

    private static CheckResult AnyOf(
        IReadOnlyList<NormalizedResource> resources,
        string kind,
        string attribute,
        string expected,
        string empty,
        string success)
    {
        var rows = resources.Where(item => item.Kind == kind).ToArray();
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.NotRun, empty);
        }

        return rows.Any(item => Flag(item, attribute) == expected)
            ? new CheckResult(TestOutcome.Pass, success)
            : new CheckResult(TestOutcome.Fail, $"No {kind} has {attribute} {expected}.");
    }

    private static CheckResult Leavers(IReadOnlyList<NormalizedResource> resources)
    {
        var users = resources.Where(item => item.Kind == "user").ToArray();
        if (users.Length == 0)
        {
            return new CheckResult(TestOutcome.NotRun, "No users were collected.");
        }

        var leaverEmails = users
            .Where(item => Flag(item, "offboarded") == "true")
            .Select(Email)
            .Where(email => email.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (leaverEmails.Count == 0)
        {
            return new CheckResult(TestOutcome.Pass, "The directory has no leavers.");
        }

        var stillActive = users
            .Where(item => Flag(item, "offboarded") != "true" && Flag(item, "status") != "suspended" && leaverEmails.Contains(Email(item)))
            .Select(item => item.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return stillActive.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{leaverEmails.Count} leavers are suspended or removed.")
            : new CheckResult(TestOutcome.Fail, $"Leavers still have an active account: {string.Join(", ", stillActive)}.");
    }

    private static string Flag(NormalizedResource resource, string name) =>
        resource.Attributes.TryGetValue(name, out var value) ? value : "";

    private static string Email(NormalizedResource resource) => Flag(resource, "email");
}
