using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.GovernanceHub;

internal static class GovernanceHubExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var domain = ClientSeed.Company.Domain;

        if (!db.LegalEntities.Local.Any() && !await db.LegalEntities.AnyAsync(cancellationToken))
        {
            db.LegalEntities.AddRange(
                new LegalEntityRecord
                {
                    Id = Guid.NewGuid(),
                    Code = "ENT-US",
                    Name = $"{ClientSeed.Company.Name} US Inc.",
                    Jurisdiction = "United States (Delaware)",
                    Region = "us-east-1",
                    Headcount = 640,
                    ControlsPassing = 58,
                    ControlsTotal = 64,
                    PrimaryFramework = "SOC 2 Type II + HIPAA",
                    Status = "AuditInProgress",
                    CreatedAt = now.AddDays(-180),
                },
                new LegalEntityRecord
                {
                    Id = Guid.NewGuid(),
                    Code = "ENT-EU",
                    Name = $"{ClientSeed.Company.Name} EU GmbH (Frankfurt)",
                    Jurisdiction = "Germany / European Union",
                    Region = "eu-central-1",
                    Headcount = 310,
                    ControlsPassing = 49,
                    ControlsTotal = 54,
                    PrimaryFramework = "ISO 27001 + GDPR + EU AI Act",
                    Status = "Active",
                    CreatedAt = now.AddDays(-140),
                },
                new LegalEntityRecord
                {
                    Id = Guid.NewGuid(),
                    Code = "ENT-FED",
                    Name = $"{ClientSeed.Company.Name} Federal & OT Systems",
                    Jurisdiction = "US Federal / Critical Infrastructure",
                    Region = "us-gov-west-1",
                    Headcount = 130,
                    ControlsPassing = 36,
                    ControlsTotal = 46,
                    PrimaryFramework = "NIST CSF 2.0 + IEC 62443",
                    Status = "Onboarding",
                    CreatedAt = now.AddDays(-45),
                });
        }

        if (!db.PrivacyAssessments.Local.Any() && !await db.PrivacyAssessments.AnyAsync(cancellationToken))
        {
            db.PrivacyAssessments.AddRange(
                new PrivacyAssessment
                {
                    Id = Guid.NewGuid(),
                    Code = "ROPA-01",
                    Kind = "RoPA",
                    ActivityName = "Customer Analytics Warehouse Ingestion",
                    DataSubjects = "Enterprise customers, end-user telemetry profiles",
                    LawfulBasis = "Art. 6(1)(b) Contract performance",
                    TransferMechanism = "EU-US Data Privacy Framework + 2021 SCCs",
                    RiskLevel = "Medium",
                    Status = "Approved",
                    OwnerEmail = "lena.cho@" + domain,
                    ReviewDueOn = today.AddDays(90),
                    UpdatedAt = now.AddDays(-14),
                },
                new PrivacyAssessment
                {
                    Id = Guid.NewGuid(),
                    Code = "DPIA-01",
                    Kind = "DPIA",
                    ActivityName = "Automated Anomaly Scoring & AI Compliance Copilot",
                    DataSubjects = "Workspace administrators and workforce directory",
                    LawfulBasis = "Art. 6(1)(f) Legitimate interest (security monitoring)",
                    TransferMechanism = "EU-central-1 sovereign processing (no cross-border egress)",
                    RiskLevel = "High",
                    Status = "InReview",
                    OwnerEmail = "priya.nair@" + domain,
                    ReviewDueOn = today.AddDays(21),
                    UpdatedAt = now.AddDays(-4),
                },
                new PrivacyAssessment
                {
                    Id = Guid.NewGuid(),
                    Code = "ROPA-02",
                    Kind = "RoPA",
                    ActivityName = "Workforce Onboarding & Checkr Background Screening",
                    DataSubjects = "Employees and contractors",
                    LawfulBasis = "Art. 6(1)(c) Legal obligation",
                    TransferMechanism = "Standard Contractual Clauses (Module 2)",
                    RiskLevel = "Low",
                    Status = "Approved",
                    OwnerEmail = "chandra.vell@" + domain,
                    ReviewDueOn = today.AddDays(180),
                    UpdatedAt = now.AddDays(-30),
                });
        }

        if (!db.AiModels.Local.Any() && !await db.AiModels.AnyAsync(cancellationToken))
        {
            db.AiModels.AddRange(
                new AiModelRecord
                {
                    Id = Guid.NewGuid(),
                    Code = "AIM-01",
                    ModelName = "Harborline Grounded Compliance Assistant",
                    Provider = "OpenAI-Compatible Enterprise Gateway",
                    EuAiActRiskTier = "Limited",
                    TrainingDataProvenance = "Zero customer training; RAG strictly scoped to role-permitted tenant metadata",
                    Guardrails = "Citation-required output, auditor framework isolation, PII redaction filter",
                    RedTeamStatus = "Passed",
                    LinkedControlCode = "AI-01",
                    OwnerEmail = "priya.nair@" + domain,
                    EvaluatedAt = now.AddDays(-10),
                },
                new AiModelRecord
                {
                    Id = Guid.NewGuid(),
                    Code = "AIM-02",
                    ModelName = "Warehouse PII Auto-Classifier v2.3",
                    Provider = "Internal PyTorch / ONNX",
                    EuAiActRiskTier = "High",
                    TrainingDataProvenance = "Synthetic PII/PHI corpus (v4.1) audited for demographic balance",
                    Guardrails = "Human-in-the-loop approval before retention purge; confidence threshold >= 0.92",
                    RedTeamStatus = "InProgress",
                    LinkedControlCode = "AI-01",
                    OwnerEmail = "marcus.lee@" + domain,
                    EvaluatedAt = now.AddDays(-3),
                });
        }

        if (!db.PhishingCampaigns.Local.Any() && !await db.PhishingCampaigns.AnyAsync(cancellationToken))
        {
            db.PhishingCampaigns.AddRange(
                new PhishingCampaign
                {
                    Id = Guid.NewGuid(),
                    Code = "PHISH-2026-Q3",
                    Name = "Q3 OAuth Consent & SSO Credential Harvest Drill",
                    Source = "KnowBe4",
                    SentCount = 1080,
                    ClickedCount = 29,
                    ReportedCount = 742,
                    RemedialTrainingStatus = "Auto-assigned (27/29 completed)",
                    RanAt = now.AddDays(-32),
                },
                new PhishingCampaign
                {
                    Id = Guid.NewGuid(),
                    Code = "PHISH-2026-Q4",
                    Name = "Q4 Executive Wire & Vendor Invoice Spoof",
                    Source = "KnowBe4",
                    SentCount = 1080,
                    ClickedCount = 14,
                    ReportedCount = 816,
                    RemedialTrainingStatus = "Auto-assigned (14/14 completed)",
                    RanAt = now.AddDays(-6),
                });
        }

        if (!db.AttackSurfaceAssets.Local.Any() && !await db.AttackSurfaceAssets.AnyAsync(cancellationToken))
        {
            db.AttackSurfaceAssets.AddRange(
                new AttackSurfaceAsset
                {
                    Id = Guid.NewGuid(),
                    Hostname = "api." + domain,
                    IpAddress = string.Join('.', 198, 51, 100, 12),
                    Service = "HTTPS API Gateway (TLS 1.3)",
                    Port = 443,
                    TlsDaysRemaining = 84,
                    ShadowIt = false,
                    Severity = "Low",
                    Status = "Mitigated",
                    LastSeenAt = now.AddHours(-2),
                },
                new AttackSurfaceAsset
                {
                    Id = Guid.NewGuid(),
                    Hostname = "staging-mongo." + domain,
                    IpAddress = string.Join('.', 203, 0, 113, 19),
                    Service = "MongoDB Wire Protocol",
                    Port = 27017,
                    TlsDaysRemaining = 9,
                    ShadowIt = true,
                    Severity = "Critical",
                    Status = "Open",
                    LastSeenAt = now.AddHours(-1),
                },
                new AttackSurfaceAsset
                {
                    Id = Guid.NewGuid(),
                    Hostname = "eu-preview." + domain,
                    IpAddress = string.Join('.', 192, 0, 2, 64),
                    Service = "Legacy Admin Portal (Expiring Cert)",
                    Port = 8443,
                    TlsDaysRemaining = 11,
                    ShadowIt = true,
                    Severity = "High",
                    Status = "Open",
                    LastSeenAt = now.AddHours(-3),
                });
        }

        if (!db.AuditorNetwork.Local.Any() && !await db.AuditorNetwork.AnyAsync(cancellationToken))
        {
            db.AuditorNetwork.AddRange(
                new AuditorFirmRecord
                {
                    Id = Guid.NewGuid(),
                    Code = "AUD-NORTHGATE",
                    FirmName = "Northgate Assurance LLP",
                    Accreditation = "AICPA Peer-Reviewed CPA Firm",
                    Specialties = "SOC 2 Type II, HIPAA, PCI DSS 4.0",
                    Regions = "US, Canada",
                    LeadAuditorEmail = "auditor@northgate.example",
                    Invited = true,
                    InvitedAt = now.AddDays(-40),
                },
                new AuditorFirmRecord
                {
                    Id = Guid.NewGuid(),
                    Code = "AUD-RHEIN",
                    FirmName = "RheinCert GmbH",
                    Accreditation = "DAkkS Accredited ISO Certification Body",
                    Specialties = "ISO 27001, ISO 42001, EU DORA, GDPR",
                    Regions = "EU (Frankfurt), UK",
                    LeadAuditorEmail = "certifications@rheincert.example",
                    Invited = false,
                },
                new AuditorFirmRecord
                {
                    Id = Guid.NewGuid(),
                    Code = "AUD-MERIDIAN",
                    FirmName = "Meridian Cyber Assessors",
                    Accreditation = "PCI QSA & FedRAMP 3PAO",
                    Specialties = "PCI DSS 4.0, NIST CSF 2.0, IEC 62443",
                    Regions = "US, APAC",
                    LeadAuditorEmail = "intake@meridiancyber.example",
                    Invited = false,
                });
        }

        if (!db.RemediationPullRequests.Local.Any() && !await db.RemediationPullRequests.AnyAsync(cancellationToken))
        {
            var patch = RemediationPrBuilder.Generate("CFG-01", "sg-staging-analytics-mongo", "harborline-data/infra-terraform", "Terraform");
            db.RemediationPullRequests.Add(new RemediationPullRequest
            {
                Id = Guid.NewGuid(),
                ControlCode = "CFG-01",
                TargetResource = "sg-staging-analytics-mongo",
                Repository = "harborline-data/infra-terraform",
                BranchName = patch.BranchName,
                Title = patch.Title,
                ArtifactType = patch.ArtifactType,
                UnifiedDiff = patch.UnifiedDiff,
                PrNumber = 419,
                PrUrl = "https://github.com/harborline-data/infra-terraform/pull/419",
                Mode = "signed-receipt",
                Status = "Open",
                EvidenceId = null,
                EvidenceSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                CreatedAt = now.AddDays(-1),
            });
        }
    }
}
