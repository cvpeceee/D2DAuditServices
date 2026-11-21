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
        new("endpoint-edr", "EDR agent healthy on every endpoint", "MAL-01", ["soc2 CC6.8", "iso27001 A.8.7"], "Reinstall or repair the sensor on degraded endpoints and confirm prevention is on."),
        new("endpoint-disk-encryption", "Disk encryption on managed devices", "EP-03", ["soc2 CC6.7", "iso27001 A.8.1"], "Enforce FileVault or BitLocker with an MDM profile and escrow the recovery key."),
        new("vulnerability-sla", "Vulnerabilities fixed within SLA", "VM-02", ["soc2 CC7.1", "iso27001 A.8.8"], "Patch or accept the risk for open findings past their severity SLA (critical 15, high 30, medium 90, low 180 days)."),
        new("hris-leavers-deprovisioned", "HRIS leavers have no active accounts", "HR-08", ["soc2 CC6.2", "soc2 CC6.3", "iso27001 A.6.5"], "Suspend every account that belongs to a terminated employee."),
        new("alerts-triaged", "Security alerts are triaged", "LG-08", ["soc2 CC7.3", "iso27001 A.5.25"], "Acknowledge or resolve open alerts and assign an owner."),
        new("monitors-active", "Monitoring alerts are active", "LG-08", ["soc2 CC7.2", "iso27001 A.8.16"], "Unmute monitors or document why they are muted."),
        new("log-retention", "Audit logs retained for one year", "LG-03", ["soc2 CC7.2", "iso27001 A.8.15"], "Raise retention on audit indexes to at least 365 days or archive to cold storage."),
        new("training-completed", "Security training completed", "HR-06", ["soc2 CC1.4", "soc2 CC2.2", "iso27001 A.6.3"], "Send reminders and escalate overdue learners to their managers."),
        new("background-screened", "Background checks cleared", "HR-01", ["soc2 CC1.4", "iso27001 A.6.1"], "Finish or adjudicate the pending screenings before granting production access."),
        new("database-hardening", "Managed databases encrypted and private", "CR-02", ["soc2 CC6.1", "soc2 CC6.7", "iso27001 A.8.24"], "Turn on encryption at rest and remove 0.0.0.0/0 from database access lists."),
        new("tls-enforced", "TLS enforced on public domains", "CR-05", ["soc2 CC6.7", "iso27001 A.8.24"], "Set SSL mode to Full (strict), turn on Always Use HTTPS and minimum TLS 1.2."),
        new("password-manager-mfa", "Password manager members use MFA", "AC-07", ["soc2 CC6.1", "iso27001 A.5.17"], "Require two-factor in the password manager policy for every member."),
        new("server-backups", "Server backups enabled", "BC-01", ["soc2 A1.2", "iso27001 A.8.13"], "Turn on automated backups for every production server."),
        new("network-exposure", "Firewalls block unrestricted inbound", "CFG-01", ["soc2 CC6.6", "iso27001 A.8.20"], "Restrict administrative ports to known address ranges."),
        new("change-approved", "Changes approved before release", "CM-11", ["soc2 CC8.1", "iso27001 A.8.32"], "Record an approver on every change before it is released."),
        new("oncall-coverage", "On-call schedules are staffed", "IR-02", ["soc2 CC7.4", "iso27001 A.5.24"], "Fill the gaps in on-call rotations so every hour has a responder."),
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
        "endpoint-edr" => AllOf(resources, "edrAgent", "healthy", "true", "No EDR agents were collected.", "These endpoints have a degraded or missing sensor"),
        "endpoint-disk-encryption" => AllOf(resources, "device", "encrypted", "true", "No managed devices were collected.", "Disk encryption is off on"),
        "vulnerability-sla" => Matching(resources, "vulnerability", static item => Flag(item, "status") is "open" or "", static item => Flag(item, "withinSla") == "true", "No open vulnerabilities were collected.", "Open findings are past their SLA"),
        "hris-leavers-deprovisioned" => HrisLeavers(resources),
        "alerts-triaged" => AllOf(resources, "alert", "triaged", "true", "No alerts were collected.", "These alerts are not triaged"),
        "monitors-active" => AllOf(resources, "monitor", "muted", "false", "No monitors were collected.", "These monitors are muted"),
        "log-retention" => Matching(resources, "logIndex", static item => Flag(item, "audit") == "true", static item => int.TryParse(Flag(item, "retentionDays"), out var days) && days >= 365, "No audit log indexes were collected.", "Audit indexes keep logs for under 365 days"),
        "training-completed" => Matching(resources, "training", static item => Flag(item, "status") != "terminated", static item => Flag(item, "completed") == "true", "No training assignments were collected.", "These people have not completed training"),
        "background-screened" => AllOf(resources, "backgroundCheck", "cleared", "true", "No background checks were collected.", "These screenings are not cleared"),
        "database-hardening" => Matching(resources, "database", static _ => true, static item => Flag(item, "encrypted") == "true" && Flag(item, "public") == "false", "No managed databases were collected.", "These databases are unencrypted or public"),
        "tls-enforced" => AllOf(resources, "domain", "tls", "true", "No domains were collected.", "TLS is not enforced on"),
        "password-manager-mfa" => Matching(resources, "vaultMember", static item => Flag(item, "status") != "suspended", static item => Flag(item, "mfa") == "true", "No password manager members were collected.", "These members have no two-factor"),
        "server-backups" => AllOf(resources, "server", "backups", "true", "No servers were collected.", "Backups are off on"),
        "network-exposure" => AllOf(resources, "firewall", "open", "false", "No firewalls were collected.", "These firewalls allow unrestricted inbound"),
        "change-approved" => AllOf(resources, "change", "approved", "true", "No changes were collected.", "These changes have no approver"),
        "oncall-coverage" => AllOf(resources, "schedule", "covered", "true", "No on-call schedules were collected.", "These schedules have gaps"),
        _ => throw new InvalidOperationException($"Unknown automated check {key}."),
    };

    /// <summary>The resource kind each connector-fed check reads, used to build parquet evidence.</summary>
    public static string? KindFor(string key) => key switch
    {
        "endpoint-edr" => "edrAgent",
        "endpoint-disk-encryption" => "device",
        "vulnerability-sla" => "vulnerability",
        "hris-leavers-deprovisioned" => "employee",
        "alerts-triaged" => "alert",
        "monitors-active" => "monitor",
        "log-retention" => "logIndex",
        "training-completed" => "training",
        "background-screened" => "backgroundCheck",
        "database-hardening" => "database",
        "tls-enforced" => "domain",
        "password-manager-mfa" => "vaultMember",
        "server-backups" => "server",
        "network-exposure" => "firewall",
        "change-approved" => "change",
        "oncall-coverage" => "schedule",
        _ => null,
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
        "hris-leavers-deprovisioned" => resources.Where(item => item.Kind is "employee" or "user").ToArray(),
        _ when KindFor(key) is { } kind => resources.Where(item => item.Kind == kind).ToArray(),
        _ => resources.Where(item => item.Kind == "user").ToArray(),
    };

    private static CheckResult Matching(
        IReadOnlyList<NormalizedResource> resources,
        string kind,
        Func<NormalizedResource, bool> include,
        Func<NormalizedResource, bool> passes,
        string empty,
        string failure)
    {
        var rows = resources.Where(item => item.Kind == kind && include(item)).ToArray();
        if (rows.Length == 0)
        {
            return new CheckResult(TestOutcome.NotRun, empty);
        }

        var bad = rows.Where(item => !passes(item)).Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
        return bad.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{rows.Length} {kind} resources match.")
            : new CheckResult(TestOutcome.Fail, $"{failure}: {string.Join(", ", bad)}.");
    }

    private static CheckResult HrisLeavers(IReadOnlyList<NormalizedResource> resources)
    {
        var terminated = resources
            .Where(item => item.Kind == "employee" && Flag(item, "status") == "terminated")
            .Select(Email)
            .Where(email => email.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!resources.Any(item => item.Kind == "employee"))
        {
            return new CheckResult(TestOutcome.NotRun, "No HRIS employees were collected.");
        }

        if (terminated.Count == 0)
        {
            return new CheckResult(TestOutcome.Pass, "The HRIS has no terminated employees.");
        }

        var active = resources
            .Where(item => item.Kind is "user" or "vaultMember"
                && Flag(item, "status") != "suspended"
                && Flag(item, "offboarded") != "true"
                && terminated.Contains(Email(item)))
            .Select(item => item.Name + " (" + item.Provider + ")")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return active.Length == 0
            ? new CheckResult(TestOutcome.Pass, $"{terminated.Count} terminated employees have no active accounts.")
            : new CheckResult(TestOutcome.Fail, $"Terminated employees still have active accounts: {string.Join(", ", active)}.");
    }

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
