namespace D2DAudit.Domain;

public sealed record DiffLine(string Kind, string Text, int? OldNumber, int? NewNumber);

public static class LineDiff
{
    public const int MaxLines = 1000;

    /// <summary>Line diff built on a longest-common-subsequence table. Kinds: "same", "added", "removed".</summary>
    public static IReadOnlyList<DiffLine> Compute(string? before, string? after)
    {
        var left = Lines(before);
        var right = Lines(after);
        var table = new int[left.Length + 1, right.Length + 1];
        for (var i = left.Length - 1; i >= 0; i--)
        {
            for (var j = right.Length - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        int a = 0, b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (string.Equals(left[a], right[b], StringComparison.Ordinal))
            {
                result.Add(new DiffLine("same", left[a], a + 1, b + 1));
                a++;
                b++;
            }
            else if (table[a + 1, b] >= table[a, b + 1])
            {
                result.Add(new DiffLine("removed", left[a], a + 1, null));
                a++;
            }
            else
            {
                result.Add(new DiffLine("added", right[b], null, b + 1));
                b++;
            }
        }

        for (; a < left.Length; a++)
        {
            result.Add(new DiffLine("removed", left[a], a + 1, null));
        }

        for (; b < right.Length; b++)
        {
            result.Add(new DiffLine("added", right[b], null, b + 1));
        }

        return result;
    }

    private static string[] Lines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return lines.Length > MaxLines ? lines[..MaxLines] : lines;
    }
}

public sealed class PolicyAttestationCampaign
{
    public Guid Id { get; set; }

    public string PolicyCode { get; set; } = "";

    public int VersionNumber { get; set; }

    public string Group { get; set; } = AttestationRules.Everyone;

    public DateOnly StartedOn { get; set; }

    public DateOnly DueOn { get; set; }

    public DateOnly RenewOn { get; set; }

    public string State { get; set; } = AttestationRules.Active;

    public string CreatedByName { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public List<PolicyAttestation> Attestations { get; set; } = [];
}

public sealed class PolicyAttestation
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public PolicyAttestationCampaign? Campaign { get; set; }

    public string PersonName { get; set; } = "";

    public string PersonEmail { get; set; } = "";

    public DateTimeOffset? AttestedAt { get; set; }
}

public static class AttestationRules
{
    public const string Everyone = "Everyone";
    public const string Active = "Active";
    public const string Renewed = "Renewed";
    public const int RenewalWarningDays = 30;

    public static DateOnly RenewOn(DateOnly startedOn) => startedOn.AddYears(1);

    public static string Status(string state, DateOnly renewOn, DateOnly today)
    {
        if (state == Renewed)
        {
            return "renewed";
        }

        if (renewOn < today)
        {
            return "overdue";
        }

        return renewOn <= today.AddDays(RenewalWarningDays) ? "renewalDue" : "current";
    }

    public static bool InGroup(string group, string department) =>
        group == Everyone || string.Equals(group, department, StringComparison.OrdinalIgnoreCase);

    public static RuleResult Validate(string? group, DateOnly dueOn, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(group) || group.Trim().Length > 100)
        {
            return RuleResult.Fail("invalid_group");
        }

        return dueOn < today ? RuleResult.Fail("invalid_due") : RuleResult.Ok();
    }
}

public static class PolicyTemplateLibrary
{
    public static IReadOnlyList<PolicyTemplate> Extended { get; } =
    [
        new("POL-AUP", "Acceptable use policy",
            "Company devices, accounts, and networks are provided for company work.\nLight personal use is fine when it does not slow work, break the law, or put data at risk.\nDo not install software that has not been approved, and do not turn off security tools.\nReport a lost device or a suspicious message to the security owner straight away.",
            ["GV-01", "EP-08"]),
        new("POL-DCL", "Data classification policy",
            "Every data set has an owner who labels it Public, Internal, Confidential, or Restricted.\nThe label decides where the data may be stored and who may see it.\nRestricted data stays in approved production systems and is never copied to a laptop.\nWhen unsure, treat data as Confidential until the owner says otherwise.",
            ["PP-01", "CR-02"]),
        new("POL-RET", "Data retention policy",
            "Each record type has a written retention period and an owner.\nRecords are deleted or anonymised when the period ends, unless a legal hold applies.\nAudit logs are kept for at least one year.\nThe owner checks once a year that deletion jobs are still running.",
            ["LG-03", "PP-01"]),
        new("POL-ENC", "Encryption policy",
            "Production data is encrypted at rest with keys held in a managed key service.\nData moving between systems uses TLS 1.2 or later.\nKeys are rotated on a schedule and access to them is logged.\nHome-made encryption schemes are not used.",
            ["CR-02", "CR-05"]),
        new("POL-VEN", "Vendor management policy",
            "Before a vendor receives company data, the requester records what data is shared and why.\nVendors with access to Confidential data are reviewed before signing and once a year after.\nThe review looks at their security report, breach history, and contract terms.\nA vendor that is no longer used has its access removed and its data returned or deleted.",
            ["VM-05"]),
        new("POL-RSK", "Risk management policy",
            "The company keeps one risk register with an owner for every risk.\nRisks are scored on likelihood and impact and reviewed at least once a year.\nEach risk is treated, accepted with a reason, transferred, or avoided.\nNew systems and major changes are checked for new risks before launch.",
            ["RM-01"]),
        new("POL-SDL", "Secure development policy",
            "Code reaches production only through the main branch after a second person reviews it.\nAutomated checks for tests and known-vulnerable dependencies run on every change.\nSecrets are kept out of source code.\nDevelopment, test, and production environments stay separate.",
            ["CM-11", "CM-14"]),
        new("POL-VUL", "Vulnerability management policy",
            "Systems are scanned for vulnerabilities on a regular schedule.\nFindings are fixed within a set time by severity: critical first, then high, medium, and low.\nA finding that cannot be fixed in time gets a written exception with an end date.\nThe owner reports open findings to leadership every month.",
            ["VM-02"]),
        new("POL-LOG", "Logging and monitoring policy",
            "Production systems send security-relevant logs to a central place.\nLogs are protected from change and kept for at least one year.\nAlerts for suspicious activity go to a named team that reviews them every working day.\nThe review and any follow-up are recorded.",
            ["LG-03", "LG-08"]),
        new("POL-PHY", "Physical security policy",
            "Offices and server rooms are locked and entry is limited to people who need it.\nEntry to production spaces is logged and the log is kept.\nVisitors sign in and are accompanied.\nEquipment leaving the building is approved and tracked.",
            ["PH-02"]),
        new("POL-HRS", "Human resources security policy",
            "New hires are screened before they receive access, as local law allows.\nEveryone completes security awareness training when they join and once a year.\nWhen someone leaves, their access is removed within one business day.\nRole changes trigger a review of what access the person still needs.",
            ["HR-01", "HR-06", "HR-08"]),
        new("POL-AST", "Asset management policy",
            "Laptops, servers, and cloud accounts are recorded in an inventory with an owner.\nLaptops have disk encryption and a screen lock turned on.\nProduction systems follow a written baseline configuration.\nRetired assets are wiped before disposal and removed from the inventory.",
            ["EP-03", "CFG-01"]),
        new("POL-BKP", "Backup policy",
            "Production data is backed up on a schedule that matches how much loss the business can accept.\nBackups are encrypted and kept in a separate location from the source.\nA restore is tested at least once a quarter and the result is recorded.\nFailed backup jobs alert the owning team.",
            ["BC-01"]),
        new("POL-PWD", "Password and authentication policy",
            "Every workforce account uses a second factor.\nPasswords are long, unique per service, and stored in the company password manager.\nPasswords are never shared or sent in chat or email.\nA password is changed straight away if it may have been exposed.",
            ["AC-07"]),
        new("POL-RMT", "Remote work policy",
            "Remote work uses company-managed devices with encryption and a screen lock.\nPublic Wi-Fi is used only with the company VPN or other approved protection.\nScreens are kept out of view of others when handling Confidential data.\nPrinted Confidential material is shredded, not thrown away.",
            ["EP-03", "EP-08"]),
        new("POL-COC", "Code of conduct",
            "People treat colleagues, customers, and partners with respect.\nConflicts of interest are disclosed to a manager.\nCompany information is used only for company purposes.\nConcerns can be raised without fear of retaliation, and every report is looked into.",
            ["GV-02"]),
        new("POL-AIU", "AI acceptable use policy",
            "Only AI tools approved by the security owner may receive company data.\nConfidential and Restricted data is not pasted into public AI tools.\nA person reviews AI output before it is used in code, decisions, or customer messages.\nNew AI uses that affect customers are risk-assessed before launch.",
            ["GV-01"]),
    ];

    public static IReadOnlyList<PolicyTemplate> All { get; } = [.. PolicyTemplates.Core, .. Extended];
}
