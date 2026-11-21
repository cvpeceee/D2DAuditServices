using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Operations;

internal static class OperationsExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!db.Computers.Local.Any() && !await db.Computers.AnyAsync(cancellationToken))
        {
            AddComputers(db, now);
        }

        if (!db.VulnerabilityPolicies.Local.Any() && !await db.VulnerabilityPolicies.AnyAsync(cancellationToken))
        {
            db.VulnerabilityPolicies.Add(new VulnerabilityPolicy { Id = Guid.NewGuid(), CriticalDays = 7, HighDays = 30, MediumDays = 60, LowDays = 90 });
        }

        if (!db.Vulnerabilities.Local.Any() && !await db.Vulnerabilities.AnyAsync(cancellationToken))
        {
            AddVulnerabilities(db, now);
        }

        if (!db.TrustProfiles.Local.Any() && !await db.TrustProfiles.AnyAsync(cancellationToken))
        {
            AddTrust(db, today, now);
        }

        if (!db.AutomationRules.Local.Any() && !await db.AutomationRules.AnyAsync(cancellationToken))
        {
            AddAutomation(db, now);
        }

        await AddCustomControlAsync(db, cancellationToken);
        await RefreshBackgroundAsync(db, today, cancellationToken);
    }

    private static void AddComputers(TenantDbContext db, DateTimeOffset now)
    {
        var index = 0;
        foreach (var device in db.Devices.Local.ToList())
        {
            var miss = index % 7;
            db.Computers.Add(Computer(device.Serial, Os(device.Platform), device.OwnerName, device.OwnerEmail, miss != 1, miss != 2, miss != 3, miss != 4, miss != 5, new DateTimeOffset(device.LastSeenOn.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero), "Mdm"));
            index++;
        }

        var domain = ClientSeed.Company.Domain;
        db.Computers.Add(Computer("HL-MAN-1044", "macOS 15.1", "Chandra Vell", "chandra.vell@" + domain, true, true, true, true, true, now.AddHours(-6), "Manual"));
        db.Computers.Add(Computer("HL-MAN-2201", "Windows 11 24H2", "Lena Cho", "lena.cho@" + domain, true, true, false, true, true, now.AddDays(-2), "Manual"));
        db.Computers.Add(Computer("HL-AGT-1001", "macOS 15.1", "Dev Patel", "dev.patel@" + domain, true, false, true, true, true, now.AddHours(-1), "Agent"));
    }

    private static ComputerRecord Computer(string serial, string os, string owner, string email, bool encrypted, bool screen, bool antivirus, bool passwords, bool updates, DateTimeOffset seen, string source) => new()
    {
        Id = Guid.NewGuid(),
        Serial = serial,
        OperatingSystem = os,
        OwnerName = owner,
        OwnerEmail = email,
        Encrypted = encrypted,
        ScreenLock = screen,
        Antivirus = antivirus,
        PasswordManager = passwords,
        AutoUpdate = updates,
        LastCheckIn = seen,
        Source = source,
    };

    private static string Os(string platform) => platform switch
    {
        "Windows" => "Windows 11 24H2",
        "Linux" => "Ubuntu 24.04",
        _ => "macOS 15.1",
    };

    private static void AddVulnerabilities(TenantDbContext db, DateTimeOffset now)
    {
        AddFinding(db, "CVE-2024-3094", "Build tool package accepts a malicious upstream", "Critical", "build-runner", "GitHub Dependabot", now.AddDays(-20), now.AddDays(-1), now.AddDays(-20).AddDays(7), true, false, "");
        AddFinding(db, "CVE-2023-38545", "HTTP client overflow in a warehouse job", "High", "warehouse-api", "AWS Inspector", now.AddDays(-12), now.AddDays(-1), now.AddDays(-12).AddDays(30), true, false, "");
        AddFinding(db, "CVE-2024-21626", "Container runtime escape on a worker image", "Critical", "worker-image", "AWS Inspector", now.AddDays(-3), now.AddHours(-8), now.AddDays(-3).AddDays(7), true, false, "");
        AddFinding(db, "CVE-2024-6387", "Secure shell race in a bastion image", "High", "bastion", "AWS Inspector", now.AddDays(-40), now.AddDays(-2), now.AddDays(-40).AddDays(30), true, false, "");
        AddFinding(db, "CVE-2023-44487", "Request flood in the public gateway", "Medium", "edge-gateway", "AWS Inspector", now.AddDays(-8), now.AddDays(-1), now.AddDays(-8).AddDays(60), false, false, "");
        AddFinding(db, "CVE-2024-3094", "Build tool package accepts a malicious upstream", "Critical", "analytics-ci", "GitHub Dependabot", now.AddDays(-6), now.AddDays(-1), now.AddDays(-6).AddDays(7), true, false, "");
        AddFinding(db, "CVE-2022-41741", "Media module flaw in an internal player", "Low", "support-portal", "GitHub Dependabot", now.AddDays(-15), now.AddDays(-4), now.AddDays(-15).AddDays(90), true, true, "The player is not reachable from the internet and the fix is scheduled.");
        AddFinding(db, "CVE-2024-5535", "OpenSSL handshake accepts an empty extension", "Medium", "edge-gateway", "Pentest", now.AddDays(-8), now.AddDays(-8), now.AddDays(-8).AddDays(60), false, false, "");
        db.PentestReports.Add(new PentestReport
        {
            Id = Guid.NewGuid(),
            Title = "2026 application test",
            Vendor = "Northgate Assurance",
            TestedOn = DateOnly.FromDateTime(now.AddDays(-8).UtcDateTime),
            Summary = "CVE-2024-5535,Medium,edge-gateway,OpenSSL handshake accepts an empty extension",
            FindingCount = 1,
            UploadedAt = now.AddDays(-8),
        });
        db.SlaEvents.Add(Event("CVE-2024-6387", "bastion", "Breached", DateOnly.FromDateTime(now.AddDays(-10).UtcDateTime), "High findings are due 30 days after first seen.", now.AddDays(-9)));
        db.SlaEvents.Add(Event("CVE-2024-3094", "build-runner", "DueChanged", DateOnly.FromDateTime(now.AddDays(-13).UtcDateTime), "Critical window moved from 14 days to 7 days.", now.AddDays(-18)));
        db.SlaEvents.Add(Event("CVE-2022-41741", "support-portal", "Ignored", DateOnly.FromDateTime(now.AddDays(-15).AddDays(90).UtcDateTime), "The player is not reachable from the internet and the fix is scheduled.", now.AddDays(-2)));
    }

    private static void AddFinding(TenantDbContext db, string cve, string title, string severity, string asset, string source, DateTimeOffset first, DateTimeOffset last, DateTimeOffset due, bool fix, bool ignored, string reason)
    {
        db.Vulnerabilities.Add(new VulnerabilityRecord
        {
            Id = Guid.NewGuid(),
            Cve = cve,
            Title = title,
            Severity = severity,
            AssetName = asset,
            Source = source,
            FirstSeen = first,
            LastSeen = last,
            SlaDue = DateOnly.FromDateTime(due.UtcDateTime),
            FixAvailable = fix,
            Ignored = ignored,
            IgnoreReason = reason,
            IgnoredAt = ignored ? last : null,
        });
    }

    private static SlaEvent Event(string cve, string asset, string kind, DateOnly due, string detail, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        Cve = cve,
        AssetName = asset,
        EventKind = kind,
        DueOn = due,
        Detail = detail,
        At = at,
    };

    private static void AddTrust(TenantDbContext db, DateOnly today, DateTimeOffset now)
    {
        var company = ClientSeed.Company;
        db.TrustProfiles.Add(new TrustProfile
        {
            Id = Guid.NewGuid(),
            Published = true,
            Headline = company.Name + " security and compliance",
            Summary = company.Name + " publishes the frameworks in scope, a control summary, and the documents customers can read. Reports that name customers stay behind an NDA.",
            ContactEmail = "security@" + company.Domain,
        });
        var report = Document("SOC 2 report", "Independent examination of the security and availability criteria.", "Nda", "harborline-soc2.pdf", today.AddDays(-20));
        var certificate = Document("ISO 27001 certificate", "Certificate for the information security management system.", "Public", "harborline-iso27001.pdf", today.AddDays(-40));
        var whitepaper = Document("Security whitepaper", "How the platform separates customer warehouses and records access.", "Public", "harborline-security.pdf", today.AddDays(-12));
        var pentest = Document("Penetration test summary", "Summary of the latest independent test and the open findings.", "Nda", "harborline-pentest.pdf", today.AddDays(-8));
        db.TrustDocuments.AddRange(report, certificate, whitepaper, pentest);
        db.TrustRequests.Add(new TrustAccessRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = report.Id,
            RequesterName = "Mina Okonkwo",
            RequesterEmail = "mina.okonkwo@northbridge.example",
            Company = "Northbridge Analytics",
            Purpose = "Security review before a warehouse contract.",
            Status = "Pending",
            RequestedAt = now.AddDays(-2),
        });
        db.TrustRequests.Add(new TrustAccessRequest
        {
            Id = Guid.NewGuid(),
            DocumentId = pentest.Id,
            RequesterName = "Owen Blake",
            RequesterEmail = "owen.blake@fieldnote.example",
            Company = "Fieldnote",
            Purpose = "Annual vendor review.",
            Status = "Approved",
            RequestedAt = now.AddDays(-18),
            DecidedAt = now.AddDays(-16),
        });
    }

    private static TrustDocument Document(string title, string summary, string access, string file, DateOnly updated) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Summary = summary,
        Access = access,
        FileName = file,
        UpdatedOn = updated,
    };

    private static void AddAutomation(TenantDbContext db, DateTimeOffset now)
    {
        var failed = Rule("Open a ticket when a test fails", "TestFailed", "Jira", "SEC");
        var control = Rule("Tell the channel when a control is not ready", "ControlNotReady", "Slack", "#security");
        var risk = Rule("Mail the risk owner when a risk is created", "RiskCreated", "Email", "security@" + ClientSeed.Company.Domain);
        var review = Rule("Post reviews that are coming due", "ReviewDue", "Teams", "Compliance");
        db.AutomationRules.AddRange(failed, control, risk, review);
        db.AutomationDeliveries.Add(new AutomationDelivery
        {
            Id = Guid.NewGuid(),
            RuleId = failed.Id,
            Trigger = "TestFailed",
            Action = "Jira",
            Target = "SEC",
            Summary = "Personnel completed training within 12 months",
            ExternalId = "DEMO-SEC-training",
            At = now.AddDays(-1),
        });
        db.AutomationDeliveries.Add(new AutomationDelivery
        {
            Id = Guid.NewGuid(),
            RuleId = review.Id,
            Trigger = "ReviewDue",
            Action = "Teams",
            Target = "Compliance",
            Summary = "Reviews due",
            ExternalId = "teams:example1",
            At = now.AddHours(-5),
        });
    }

    private static AutomationRule Rule(string name, string trigger, string action, string target) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Trigger = trigger,
        Action = action,
        Target = target,
        Enabled = true,
    };

    private static async Task AddCustomControlAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Controls.AnyAsync(item => item.Code == "HL-01", cancellationToken))
        {
            return;
        }

        var requirement = await db.Requirements.Include(item => item.Framework)
            .FirstOrDefaultAsync(item => item.Framework!.Key == "iso27001" && item.Code == "A.5.10", cancellationToken);
        if (requirement is null)
        {
            return;
        }

        var control = new Control
        {
            Id = Guid.NewGuid(),
            Code = "HL-01",
            Title = "Customer export is approved",
            Description = "A named owner approves each bulk export of customer warehouse data.",
            Frequency = "Continuous",
            OwnerName = "Priya Nair",
            OwnerEmail = "priya.nair@" + ClientSeed.Company.Domain,
            Status = ControlStatus.Passing,
            Custom = true,
        };
        db.Controls.Add(control);
        db.ControlMappings.Add(new ControlRequirementMap
        {
            ControlId = control.Id,
            RequirementId = requirement.Id,
            Coverage = MappingCoverage.Partial,
        });
    }

    private static async Task RefreshBackgroundAsync(TenantDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var people = db.People.Local.Count > 0
            ? db.People.Local.ToArray()
            : await db.People.AsNoTracking().ToArrayAsync(cancellationToken);
        var result = BackgroundRules.Evaluate(
            people.Select(person => (person.State, person.HireOn, person.BackgroundStatus, person.BackgroundExcluded, person.BackgroundExclusionReason, person.Name)),
            today);
        var control = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == "HR-01", cancellationToken);
        if (control is null)
        {
            return;
        }

        var test = control.Tests.FirstOrDefault(item => item.Name == BackgroundRules.TestName);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = control.Id,
                Name = BackgroundRules.TestName,
                Kind = TestKind.Document,
            };
            db.ControlTests.Add(test);
        }

        test.Outcome = result.Outcome;
        test.Detail = result.Detail;
        if (result.Outcome == TestOutcome.Fail)
        {
            control.Status = ControlStatus.Failing;
        }
    }
}
