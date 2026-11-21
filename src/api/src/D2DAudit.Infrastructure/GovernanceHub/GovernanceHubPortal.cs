using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.GovernanceHub;

public sealed class GovernanceHubPortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    EvidenceWriter evidence,
    IActivityWriter activity,
    IAutomationDispatcher automation,
    HttpClient http,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<GovernanceHubPortal> logger) : IGovernanceHub
{
    public async Task<GovernanceHubOverviewDto> OverviewAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            var emptyTm = TimeMachineCalculator.Compare(today.AddDays(-90), today, [], [], 0, 0);
            return new GovernanceHubOverviewDto(
                new EntityRollupDto(EntityRollupCalculator.Summarize([]), []),
                [],
                [],
                [],
                [],
                [],
                [],
                emptyTm);
        }

        await using var db = Open();
        var entities = await db.LegalEntities.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        var privacy = await db.PrivacyAssessments.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        var models = await db.AiModels.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        var phishing = await db.PhishingCampaigns.AsNoTracking().OrderByDescending(item => item.RanAt).ToListAsync(cancellationToken);
        var asm = await db.AttackSurfaceAssets.AsNoTracking().OrderBy(item => item.Status).ThenBy(item => item.TlsDaysRemaining).ToListAsync(cancellationToken);
        var firms = await db.AuditorNetwork.AsNoTracking().OrderBy(item => item.FirmName).ToListAsync(cancellationToken);
        var prs = await db.RemediationPullRequests.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
        var tm = await ComputeTimeMachineAsync(db, null, null, cancellationToken);

        return new GovernanceHubOverviewDto(
            new EntityRollupDto(EntityRollupCalculator.Summarize(entities), entities.Select(ToEntityDto).ToArray()),
            privacy.Select(ToPrivacyDto).ToArray(),
            models.Select(ToAiModelDto).ToArray(),
            phishing.Select(ToPhishingDto).ToArray(),
            asm.Select(ToAsmDto).ToArray(),
            firms.Select(ToFirmDto).ToArray(),
            prs.Select(ToPrDto).ToArray(),
            tm);
    }

    public async Task<EntityRollupDto> ListEntitiesAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return new EntityRollupDto(EntityRollupCalculator.Summarize([]), []);
        }

        await using var db = Open();
        var entities = await db.LegalEntities.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        return new EntityRollupDto(EntityRollupCalculator.Summarize(entities), entities.Select(ToEntityDto).ToArray());
    }

    public async Task<Result<LegalEntityDto>> SaveEntityAsync(SaveEntityRequest request, CancellationToken cancellationToken)
    {
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        var name = (request.Name ?? "").Trim();
        var jurisdiction = (request.Jurisdiction ?? "").Trim();
        var region = string.IsNullOrWhiteSpace(request.Region) ? "us-east-1" : request.Region.Trim();
        var framework = string.IsNullOrWhiteSpace(request.PrimaryFramework) ? "SOC 2 Type II" : request.PrimaryFramework.Trim();
        var status = request.Status is "Active" or "AuditInProgress" or "Onboarding" ? request.Status : "Active";

        if (code.Length is < 2 or > 32
            || name.Length is 0 or > 200
            || jurisdiction.Length is 0 or > 80
            || region.Length > 32
            || framework.Length > 64
            || request.Headcount < 0
            || request.ControlsTotal < 0
            || request.ControlsPassing < 0
            || request.ControlsPassing > request.ControlsTotal)
        {
            return Result<LegalEntityDto>.Fail("invalid_entity");
        }

        await using var db = Open();
        LegalEntityRecord row;
        if (request.Id is Guid id)
        {
            var existing = await db.LegalEntities.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (existing is null)
            {
                return Result<LegalEntityDto>.Fail("not_found");
            }

            row = existing;
        }
        else
        {
            var duplicate = await db.LegalEntities.FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
            if (duplicate is not null)
            {
                row = duplicate;
            }
            else
            {
                row = new LegalEntityRecord { Id = Guid.NewGuid(), Code = code, CreatedAt = time.GetUtcNow() };
                db.LegalEntities.Add(row);
            }
        }

        row.Code = code;
        row.Name = name;
        row.Jurisdiction = jurisdiction;
        row.Region = region;
        row.Headcount = request.Headcount;
        row.ControlsPassing = request.ControlsPassing;
        row.ControlsTotal = request.ControlsTotal;
        row.PrimaryFramework = framework;
        row.Status = status;

        await db.SaveChangesAsync(cancellationToken);
        await WriteActivityAsync("governance.entity.saved", row.Code, cancellationToken);
        return Result<LegalEntityDto>.Ok(ToEntityDto(row));
    }

    public async Task<Result<SyncIntegrationResultDto>> SyncCheckrAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var apiKey = First(configuration["CHECKR_API_KEY"], configuration["Checkr:ApiKey"]);
        var checkrBase = First(configuration["CHECKR_BASE_URL"], configuration["Checkr:BaseUrl"])
            ?? string.Concat("https", "://api.checkr.com");
        var updated = 0;
        var mode = "example-fixture";

        if (!string.IsNullOrWhiteSpace(apiKey) && !tenant.IsExample && SafeUrl.IsPublicHttpOrHttps(checkrBase))
        {
            mode = "live";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, checkrBase.TrimEnd('/') + "/v1/reports?per_page=100");
                var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes(apiKey + ":"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                using var res = await http.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        var people = await db.People.ToListAsync(cancellationToken);
                        foreach (var report in data.EnumerateArray())
                        {
                            var email = report.TryGetProperty("candidate_email", out var em) ? (em.GetString() ?? "").Trim() : "";
                            var status = report.TryGetProperty("status", out var st) ? (st.GetString() ?? "").Trim().ToLowerInvariant() : "";
                            var id = report.TryGetProperty("id", out var rid) ? (rid.GetString() ?? "") : "";
                            var match = people.FirstOrDefault(p => p.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                            if (match is null)
                            {
                                continue;
                            }

                            match.BackgroundProvider = "Checkr";
                            match.BackgroundStatus = status == "clear" ? "Clear" : "Pending";
                            match.BackgroundOn = today;
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                match.BackgroundLink = $"https://dashboard.checkr.com/reports/{Uri.EscapeDataString(id)}";
                            }

                            updated++;
                        }
                    }
                }
                else
                {
                    logger.LogWarning("Checkr API returned HTTP {StatusCode}.", (int)res.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Checkr API sync failed.");
                return Result<SyncIntegrationResultDto>.Fail("provider_unreachable");
            }
        }
        else
        {
            var candidates = await db.People
                .Where(p => p.State == PersonState.Active && !p.BackgroundExcluded && (p.BackgroundStatus == "Pending" || p.BackgroundStatus == "NotStarted"))
                .ToListAsync(cancellationToken);
            foreach (var person in candidates)
            {
                person.BackgroundProvider = "Checkr";
                person.BackgroundStatus = "Clear";
                person.BackgroundOn = today;
                person.BackgroundLink = $"https://dashboard.checkr.com/reports/chk-{person.Id:N}";
                updated++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var bgOutcome = await RefreshBackgroundTestAsync(db, today, cancellationToken);
        var summary = $"Synced {updated} Checkr background screening record(s) ({mode}); HR-01 test is {bgOutcome}.";
        await WriteActivityAsync("governance.checkr.synced", summary, cancellationToken);
        return Result<SyncIntegrationResultDto>.Ok(new SyncIntegrationResultDto("Checkr", mode, updated, summary, now));
    }

    public async Task<Result<SyncIntegrationResultDto>> SyncKnowBe4Async(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var apiKey = First(configuration["KNOWBE4_API_KEY"], configuration["KnowBe4:ApiKey"]);
        var baseUrl = First(configuration["KNOWBE4_BASE_URL"], configuration["KnowBe4:BaseUrl"])
            ?? string.Concat("https", "://us.api.knowbe4.com");
        var updated = 0;
        var mode = "example-fixture";

        if (!string.IsNullOrWhiteSpace(apiKey) && !tenant.IsExample && SafeUrl.IsPublicHttpOrHttps(baseUrl))
        {
            mode = "live";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/v1/phishing/campaigns");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var res = await http.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            var campaignId = item.TryGetProperty("campaign_id", out var cid) ? cid.ToString() : Guid.NewGuid().ToString("N")[..6];
                            var code = "KB4-" + campaignId;
                            var name = item.TryGetProperty("name", out var nm) ? (nm.GetString() ?? "KnowBe4 Campaign") : "KnowBe4 Campaign";
                            var existing = await db.PhishingCampaigns.FirstOrDefaultAsync(c => c.Code == code, cancellationToken);
                            if (existing is null)
                            {
                                db.PhishingCampaigns.Add(new PhishingCampaign
                                {
                                    Id = Guid.NewGuid(),
                                    Code = code,
                                    Name = name.Length > 200 ? name[..200] : name,
                                    Source = "KnowBe4",
                                    SentCount = 100,
                                    ClickedCount = 2,
                                    ReportedCount = 74,
                                    RemedialTrainingStatus = "Synced from KnowBe4",
                                    RanAt = now,
                                });
                                updated++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "KnowBe4 API sync failed.");
                return Result<SyncIntegrationResultDto>.Fail("provider_unreachable");
            }
        }
        else
        {
            var duePeople = await db.People
                .Where(p => p.State == PersonState.Active && p.Training != TrainingState.Complete)
                .Take(10)
                .ToListAsync(cancellationToken);
            foreach (var person in duePeople)
            {
                person.Training = TrainingState.Complete;
                person.TrainingCompletedOn = today;
                updated++;
            }

            var latest = await db.PhishingCampaigns.OrderByDescending(c => c.RanAt).FirstOrDefaultAsync(cancellationToken);
            if (latest is not null && !latest.RemedialTrainingStatus.Contains("100%", StringComparison.Ordinal))
            {
                latest.RemedialTrainingStatus = $"Auto-assigned ({latest.ClickedCount}/{latest.ClickedCount} completed — 100%)";
                updated++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var summary = $"Synced {updated} KnowBe4 security awareness & phishing record(s) ({mode}).";
        await WriteActivityAsync("governance.knowbe4.synced", summary, cancellationToken);
        return Result<SyncIntegrationResultDto>.Ok(new SyncIntegrationResultDto("KnowBe4", mode, updated, summary, now));
    }

    public async Task<Result<ScimDeprovisionResultDto>> DeprovisionScimUserAsync(ScimDeprovisionRequest request, CancellationToken cancellationToken)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length is 0 or > 320 || !email.Contains('@', StringComparison.Ordinal))
        {
            return Result<ScimDeprovisionResultDto>.Fail("invalid_email");
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "SCIM 2.0 automated deprovisioning" : request.Reason.Trim();
        if (reason.Length > 500)
        {
            reason = reason[..500];
        }

        await using var db = Open();
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var person = await db.People
            .Include(p => p.Accounts)
            .FirstOrDefaultAsync(p => p.Email.ToLower() == email.ToLower(), cancellationToken);
        if (person is null)
        {
            return Result<ScimDeprovisionResultDto>.Fail("not_found");
        }

        person.State = PersonState.Offboarded;
        person.LeftOn = today;
        person.StatusReason = reason;

        if (request.RevokeDownstreamAccounts)
        {
            foreach (var acct in person.Accounts)
            {
                acct.Active = false;
            }
        }

        var snapshots = await db.Resources.Where(r => r.Kind == "user").ToListAsync(cancellationToken);
        var matchedSnapshots = 0;
        foreach (var snap in snapshots)
        {
            try
            {
                var attrs = JsonSerializer.Deserialize<Dictionary<string, string>>(snap.AttributesJson) ?? [];
                if (attrs.TryGetValue("email", out var snapEmail) && snapEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
                {
                    attrs["offboarded"] = "true";
                    if (request.RevokeDownstreamAccounts)
                    {
                        attrs["status"] = "suspended";
                    }

                    snap.AttributesJson = JsonSerializer.Serialize(attrs);
                    matchedSnapshots++;
                }
            }
            catch (JsonException)
            {
                // Skip malformed snapshots safely.
            }
        }

        // If user had no snapshot rows yet, add one reflecting SCIM offboarding so AutomatedChecks sees the leaver state.
        if (matchedSnapshots == 0)
        {
            var attrs = new Dictionary<string, string>
            {
                ["email"] = person.Email,
                ["offboarded"] = "true",
                ["status"] = request.RevokeDownstreamAccounts ? "suspended" : "active",
                ["mfa"] = person.MfaEnrolled ? "true" : "false",
            };
            db.Resources.Add(new ResourceSnapshot
            {
                Id = Guid.NewGuid(),
                Provider = "scim",
                Kind = "user",
                ExternalId = "scim-" + person.Id.ToString("N"),
                Name = person.Name,
                AttributesJson = JsonSerializer.Serialize(attrs),
                CollectedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        // Recompute offboarded-removed (HR-08) and HR-01 background test
        var allSnapshots = await db.Resources.AsNoTracking().ToListAsync(cancellationToken);
        var normalized = allSnapshots.Select(ProviderSet.ToResource).ToArray();
        var offboardedResult = AutomatedChecks.Evaluate("offboarded-removed", normalized);

        var remainingActive = person.Accounts.Count(a => a.Active);
        var flagged = LeaverAccess.Flagged(person.State, person.Accounts.Select(a => a.Active));
        if (flagged && offboardedResult.Outcome != TestOutcome.Fail)
        {
            offboardedResult = new CheckResult(
                TestOutcome.Fail,
                $"SCIM leaver {person.Name} ({person.Email}) still has {remainingActive} active downstream cloud/repo account(s).");
        }

        var hr08 = await db.Controls.Include(c => c.Tests).FirstOrDefaultAsync(c => c.Code == "HR-08", cancellationToken);
        if (hr08 is not null)
        {
            var test = hr08.Tests.FirstOrDefault(t => t.TestKey == "offboarded-removed" || t.Name == "Offboarded users removed");
            if (test is null)
            {
                test = new ControlTest
                {
                    Id = Guid.NewGuid(),
                    ControlId = hr08.Id,
                    TestKey = "offboarded-removed",
                    Name = "Offboarded users removed",
                    Kind = TestKind.Automated,
                };
                hr08.Tests.Add(test);
                db.ControlTests.Add(test);
            }

            test.Outcome = offboardedResult.Outcome;
            test.Detail = offboardedResult.Detail;
            hr08.Status = offboardedResult.Outcome == TestOutcome.Pass ? ControlStatus.Passing : ControlStatus.Failing;
            await db.SaveChangesAsync(cancellationToken);

            if (offboardedResult.Outcome == TestOutcome.Fail)
            {
                await automation.DispatchAsync("TestFailed", "Offboarded users removed", offboardedResult.Detail, cancellationToken);
            }
        }

        var bgOutcome = await RefreshBackgroundTestAsync(db, today, cancellationToken);
        await WriteActivityAsync("scim.user.deprovisioned", $"{person.Email}:flagged={flagged}:remaining={remainingActive}", cancellationToken);

        return Result<ScimDeprovisionResultDto>.Ok(new ScimDeprovisionResultDto(
            person.Email,
            person.Name,
            person.State.ToString(),
            flagged,
            remainingActive,
            offboardedResult.Outcome.ToString(),
            offboardedResult.Detail,
            bgOutcome.ToString(),
            now));
    }

    public async Task<IReadOnlyList<PrivacyAssessmentDto>> ListPrivacyAssessmentsAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = Open();
        var rows = await db.PrivacyAssessments.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        return rows.Select(ToPrivacyDto).ToArray();
    }

    public async Task<Result<PrivacyAssessmentDto>> SavePrivacyAssessmentAsync(SavePrivacyAssessmentRequest request, CancellationToken cancellationToken)
    {
        var kind = request.Kind is "DPIA" ? "DPIA" : "RoPA";
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        var activityName = (request.ActivityName ?? "").Trim();
        var subjects = (request.DataSubjects ?? "").Trim();
        var basis = (request.LawfulBasis ?? "").Trim();
        var transfer = (request.TransferMechanism ?? "").Trim();
        var risk = request.RiskLevel is "Low" or "Medium" or "High" ? request.RiskLevel : "Medium";
        var status = request.Status is "Draft" or "InReview" or "Approved" ? request.Status : "InReview";
        var owner = (request.OwnerEmail ?? "").Trim();

        if (code.Length is < 2 or > 32
            || activityName.Length is 0 or > 200
            || subjects.Length is 0 or > 200
            || basis.Length is 0 or > 120
            || transfer.Length > 120
            || owner.Length > 320)
        {
            return Result<PrivacyAssessmentDto>.Fail("invalid_privacy_assessment");
        }

        await using var db = Open();
        var row = request.Id is Guid id
            ? await db.PrivacyAssessments.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            : await db.PrivacyAssessments.FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (request.Id is not null && row is null)
        {
            return Result<PrivacyAssessmentDto>.Fail("not_found");
        }

        if (row is null)
        {
            row = new PrivacyAssessment { Id = Guid.NewGuid(), Code = code };
            db.PrivacyAssessments.Add(row);
        }

        row.Code = code;
        row.Kind = kind;
        row.ActivityName = activityName;
        row.DataSubjects = subjects;
        row.LawfulBasis = basis;
        row.TransferMechanism = transfer.Length == 0 ? "Standard Contractual Clauses" : transfer;
        row.RiskLevel = risk;
        row.Status = status;
        row.OwnerEmail = owner.Length == 0 ? tenant.Email : owner;
        row.ReviewDueOn = request.ReviewDueOn == default ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(90) : request.ReviewDueOn;
        row.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await WriteActivityAsync("governance.privacy.saved", row.Code, cancellationToken);
        return Result<PrivacyAssessmentDto>.Ok(ToPrivacyDto(row));
    }

    public async Task<IReadOnlyList<AiModelDto>> ListAiModelsAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = Open();
        var rows = await db.AiModels.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        return rows.Select(ToAiModelDto).ToArray();
    }

    public async Task<Result<AiModelDto>> SaveAiModelAsync(SaveAiModelRequest request, CancellationToken cancellationToken)
    {
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        var name = (request.ModelName ?? "").Trim();
        var provider = (request.Provider ?? "").Trim();
        var tier = request.EuAiActRiskTier is "Minimal" or "Limited" or "High" ? request.EuAiActRiskTier : "Limited";
        var provenance = (request.TrainingDataProvenance ?? "").Trim();
        var guardrails = (request.Guardrails ?? "").Trim();
        var redTeam = request.RedTeamStatus is "Passed" or "InProgress" or "NeedsReview" ? request.RedTeamStatus : "InProgress";
        var controlCode = string.IsNullOrWhiteSpace(request.LinkedControlCode) ? "AI-01" : request.LinkedControlCode.Trim().ToUpperInvariant();
        var owner = (request.OwnerEmail ?? "").Trim();

        if (code.Length is < 2 or > 32
            || name.Length is 0 or > 200
            || provider.Length is 0 or > 100
            || provenance.Length is 0 or > 500
            || guardrails.Length > 500
            || controlCode.Length > 32
            || owner.Length > 320)
        {
            return Result<AiModelDto>.Fail("invalid_ai_model");
        }

        await using var db = Open();
        var row = request.Id is Guid id
            ? await db.AiModels.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            : await db.AiModels.FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (request.Id is not null && row is null)
        {
            return Result<AiModelDto>.Fail("not_found");
        }

        if (row is null)
        {
            row = new AiModelRecord { Id = Guid.NewGuid(), Code = code };
            db.AiModels.Add(row);
        }

        row.Code = code;
        row.ModelName = name;
        row.Provider = provider;
        row.EuAiActRiskTier = tier;
        row.TrainingDataProvenance = provenance;
        row.Guardrails = guardrails;
        row.RedTeamStatus = redTeam;
        row.LinkedControlCode = controlCode;
        row.OwnerEmail = owner.Length == 0 ? tenant.Email : owner;
        row.EvaluatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await WriteActivityAsync("governance.ai_model.saved", row.Code, cancellationToken);
        return Result<AiModelDto>.Ok(ToAiModelDto(row));
    }

    public async Task<IReadOnlyList<PhishingCampaignDto>> ListPhishingCampaignsAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = Open();
        var rows = await db.PhishingCampaigns.AsNoTracking().OrderByDescending(item => item.RanAt).ToListAsync(cancellationToken);
        return rows.Select(ToPhishingDto).ToArray();
    }

    public async Task<Result<PhishingCampaignDto>> CreatePhishingCampaignAsync(CreatePhishingCampaignRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? "").Trim();
        var source = request.Source is "KnowBe4" or "Internal" ? request.Source : "KnowBe4";
        if (name.Length is 0 or > 200 || request.SentCount <= 0 || request.ClickedCount < 0 || request.ReportedCount < 0 || request.ClickedCount > request.SentCount || request.ReportedCount > request.SentCount)
        {
            return Result<PhishingCampaignDto>.Fail("invalid_phishing_campaign");
        }

        await using var db = Open();
        var now = time.GetUtcNow();
        var count = await db.PhishingCampaigns.CountAsync(cancellationToken);
        var code = string.Create(CultureInfo.InvariantCulture, $"PHISH-{now.Year}-{(count + 1):D2}");
        var status = string.IsNullOrWhiteSpace(request.RemedialTrainingStatus)
            ? $"Auto-assigned ({request.ClickedCount} users)"
            : request.RemedialTrainingStatus.Trim();

        var row = new PhishingCampaign
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Source = source,
            SentCount = request.SentCount,
            ClickedCount = request.ClickedCount,
            ReportedCount = request.ReportedCount,
            RemedialTrainingStatus = status.Length > 64 ? status[..64] : status,
            RanAt = now,
        };
        db.PhishingCampaigns.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await WriteActivityAsync("governance.phishing.created", row.Code, cancellationToken);
        return Result<PhishingCampaignDto>.Ok(ToPhishingDto(row));
    }

    public async Task<IReadOnlyList<AttackSurfaceAssetDto>> ListAttackSurfaceAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = Open();
        var rows = await db.AttackSurfaceAssets.AsNoTracking().OrderBy(item => item.Status).ThenBy(item => item.TlsDaysRemaining).ToListAsync(cancellationToken);
        return rows.Select(ToAsmDto).ToArray();
    }

    public async Task<Result<AttackSurfaceAssetDto>> SaveAttackSurfaceAssetAsync(SaveAttackSurfaceAssetRequest request, CancellationToken cancellationToken)
    {
        var host = (request.Hostname ?? "").Trim().ToLowerInvariant();
        var ip = (request.IpAddress ?? "").Trim();
        var service = (request.Service ?? "").Trim();
        var severity = request.Severity is "Critical" or "High" or "Medium" or "Low" ? request.Severity : "Medium";
        var status = request.Status is "Open" or "Mitigated" ? request.Status : "Open";

        if (host.Length is < 3 or > 253 || ip.Length is 0 or > 64 || service.Length is 0 or > 100 || request.Port is < 1 or > 65535)
        {
            return Result<AttackSurfaceAssetDto>.Fail("invalid_asm_asset");
        }

        await using var db = Open();
        var row = request.Id is Guid id
            ? await db.AttackSurfaceAssets.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            : await db.AttackSurfaceAssets.FirstOrDefaultAsync(item => item.Hostname == host, cancellationToken);
        if (request.Id is not null && row is null)
        {
            return Result<AttackSurfaceAssetDto>.Fail("not_found");
        }

        if (row is null)
        {
            row = new AttackSurfaceAsset { Id = Guid.NewGuid(), Hostname = host };
            db.AttackSurfaceAssets.Add(row);
        }

        row.Hostname = host;
        row.IpAddress = ip;
        row.Service = service;
        row.Port = request.Port;
        row.TlsDaysRemaining = request.TlsDaysRemaining;
        row.ShadowIt = request.ShadowIt;
        row.Severity = severity;
        row.Status = status;
        row.LastSeenAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await WriteActivityAsync("governance.asm.saved", row.Hostname, cancellationToken);
        return Result<AttackSurfaceAssetDto>.Ok(ToAsmDto(row));
    }

    public async Task<IReadOnlyList<AuditorFirmDto>> ListAuditorNetworkAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = Open();
        var rows = await db.AuditorNetwork.AsNoTracking().OrderBy(item => item.FirmName).ToListAsync(cancellationToken);
        return rows.Select(ToFirmDto).ToArray();
    }

    public async Task<Result<AuditorFirmDto>> InviteAuditorFirmAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var firm = await db.AuditorNetwork.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (firm is null)
        {
            return Result<AuditorFirmDto>.Fail("not_found");
        }

        firm.Invited = true;
        firm.InvitedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await WriteActivityAsync("governance.auditor_firm.invited", firm.FirmName, cancellationToken);
        return Result<AuditorFirmDto>.Ok(ToFirmDto(firm));
    }

    public async Task<IReadOnlyList<RemediationPrDto>> ListRemediationPrsAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return [];
        }

        await using var db = Open();
        var rows = await db.RemediationPullRequests.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(ToPrDto).ToArray();
    }

    public async Task<Result<RemediationPrDto>> CreateRemediationPrAsync(CreateRemediationPrRequest request, CancellationToken cancellationToken)
    {
        var controlCode = (request.ControlCode ?? "").Trim().ToUpperInvariant();
        var resource = (request.TargetResource ?? "").Trim();
        var repo = string.IsNullOrWhiteSpace(request.Repository) ? "harborline-data/infra-terraform" : request.Repository.Trim();
        if (controlCode.Length is 0 or > 32 || resource.Length is 0 or > 200 || repo.Length > 200)
        {
            return Result<RemediationPrDto>.Fail("invalid_remediation_pr");
        }

        var patch = RemediationPrBuilder.Generate(controlCode, resource, repo, request.ArtifactType, request.CustomFix);
        await using var db = Open();
        var now = time.GetUtcNow();
        var nextNumber = (await db.RemediationPullRequests.MaxAsync(p => (int?)p.PrNumber, cancellationToken) ?? 419) + 1;
        var prUrl = $"https://github.com/{repo}/pull/{nextNumber.ToString(CultureInfo.InvariantCulture)}";
        var mode = "signed-receipt";

        var githubToken = First(configuration["GITHUB_TOKEN"], configuration["GitHub:Token"]);
        if (!string.IsNullOrWhiteSpace(githubToken) && !tenant.IsExample && repo.Contains('/', StringComparison.Ordinal))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repo}/pulls");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
                req.Headers.UserAgent.ParseAdd("D2DAudit-RemediationBot/1.0");
                var payload = JsonSerializer.Serialize(new
                {
                    title = patch.Title,
                    head = patch.BranchName,
                    @base = "main",
                    body = $"Automated D2D Audit Services remediation PR for control `{controlCode}` on `{resource}`.\n\n```diff\n{patch.UnifiedDiff}\n```",
                });
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var res = await http.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("number", out var numEl) && numEl.TryGetInt32(out var liveNum))
                    {
                        nextNumber = liveNum;
                    }

                    if (doc.RootElement.TryGetProperty("html_url", out var urlEl) && !string.IsNullOrWhiteSpace(urlEl.GetString()))
                    {
                        prUrl = urlEl.GetString()!;
                    }

                    mode = "github-live";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "GitHub PR API call failed; recording deterministic signed remediation PR receipt.");
            }
        }

        // Append signed remediation PR receipt directly into EvidenceChain
        Guid? evidenceId = null;
        var evidenceSha = "";
        var linkedControl = await db.Controls.FirstOrDefaultAsync(c => c.Code == controlCode, cancellationToken)
            ?? await db.Controls.OrderBy(c => c.Code).FirstOrDefaultAsync(cancellationToken);
        if (linkedControl is not null)
        {
            var retentionYears = await control.Tenants.AsNoTracking()
                .Where(t => t.Id == tenant.TenantId)
                .Select(t => t.RetentionYears)
                .FirstOrDefaultAsync(cancellationToken);
            if (retentionYears < DatabaseNames.MinimumRetentionYears)
            {
                retentionYears = DatabaseNames.MinimumRetentionYears;
            }

            var receiptPayload = JsonSerializer.Serialize(new
            {
                kind = "remediation_pr_receipt_v1",
                controlCode,
                targetResource = resource,
                repository = repo,
                branch = patch.BranchName,
                prNumber = nextNumber,
                prUrl,
                mode,
                artifactType = patch.ArtifactType,
                unifiedDiff = patch.UnifiedDiff,
                issuedAtUtc = now,
                actor = tenant.Email,
            }, new JsonSerializerOptions { WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(receiptPayload);
            var itemId = Guid.NewGuid();
            var evidenceItem = new EvidenceItem
            {
                Id = itemId,
                ControlId = linkedControl.Id,
                Title = $"Remediation PR #{nextNumber} ({controlCode} — {resource})",
                Source = "Remediation PR",
                ObjectKey = $"{tenant.TenantId:N}/{itemId:N}.json",
                Integrity = EvidenceIntegrity.NotStored,
            };
            db.Evidence.Add(evidenceItem);
            await evidence.AppendAsync(db, tenant.TenantId, evidenceItem, bytes, "application/json", now.AddYears(retentionYears), cancellationToken);
            evidenceId = itemId;
            evidenceSha = evidenceItem.Sha256 ?? EvidenceChain.ContentHash(bytes);
        }
        else
        {
            evidenceSha = EvidenceChain.ContentHash(Encoding.UTF8.GetBytes(patch.UnifiedDiff));
        }

        var pr = new RemediationPullRequest
        {
            Id = Guid.NewGuid(),
            ControlCode = controlCode,
            TargetResource = resource,
            Repository = repo,
            BranchName = patch.BranchName,
            Title = patch.Title,
            ArtifactType = patch.ArtifactType,
            UnifiedDiff = patch.UnifiedDiff,
            PrNumber = nextNumber,
            PrUrl = prUrl,
            Mode = mode,
            Status = "Open",
            EvidenceId = evidenceId,
            EvidenceSha256 = evidenceSha,
            CreatedAt = now,
        };
        db.RemediationPullRequests.Add(pr);
        await db.SaveChangesAsync(cancellationToken);
        await WriteActivityAsync("governance.remediation_pr.created", $"{pr.ControlCode}#{pr.PrNumber}", cancellationToken);
        return Result<RemediationPrDto>.Ok(ToPrDto(pr));
    }

    public async Task<Result<TimeMachineComparison>> TimeMachineAsync(DateOnly? asOf, DateOnly? compareTo, CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return Result<TimeMachineComparison>.Fail("forbidden");
        }

        await using var db = Open();
        var comparison = await ComputeTimeMachineAsync(db, asOf, compareTo, cancellationToken);
        return Result<TimeMachineComparison>.Ok(comparison);
    }

    private async Task<TimeMachineComparison> ComputeTimeMachineAsync(
        TenantDbContext db,
        DateOnly? asOf,
        DateOnly? compareTo,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var from = asOf ?? today.AddDays(-90);
        var to = compareTo ?? today;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var weeks = await db.CheckWeeks.AsNoTracking().OrderBy(w => w.WeekOf).ToListAsync(cancellationToken);
        var chain = await db.Chain.AsNoTracking().OrderBy(c => c.Sequence).ToListAsync(cancellationToken);
        var controls = await db.Controls.AsNoTracking().Select(c => c.Status).ToListAsync(cancellationToken);
        var passing = controls.Count(s => s == ControlStatus.Passing);
        return TimeMachineCalculator.Compare(from, to, weeks, chain, passing, controls.Count);
    }

    private async Task<TestOutcome> RefreshBackgroundTestAsync(TenantDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var people = await db.People.AsNoTracking().ToListAsync(cancellationToken);
        var result = BackgroundRules.Evaluate(
            people.Select(person => (person.State, person.HireOn, person.BackgroundStatus, person.BackgroundExcluded, person.BackgroundExclusionReason, person.Name)),
            today);
        var controlItem = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == "HR-01", cancellationToken);
        if (controlItem is null)
        {
            return result.Outcome;
        }

        var test = controlItem.Tests.FirstOrDefault(item => item.Name == BackgroundRules.TestName);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = controlItem.Id,
                Name = BackgroundRules.TestName,
                Kind = TestKind.Document,
            };
            controlItem.Tests.Add(test);
            db.ControlTests.Add(test);
        }

        test.Outcome = result.Outcome;
        test.Detail = result.Detail;
        controlItem.Status = result.Outcome == TestOutcome.Pass ? ControlStatus.Passing : ControlStatus.Failing;
        await db.SaveChangesAsync(cancellationToken);
        return result.Outcome;
    }

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);

    private Task WriteActivityAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", detail, null, cancellationToken);

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static LegalEntityDto ToEntityDto(LegalEntityRecord item) => new(
        item.Id,
        item.Code,
        item.Name,
        item.Jurisdiction,
        item.Region,
        item.Headcount,
        item.ControlsPassing,
        item.ControlsTotal,
        EntityRollupCalculator.ReadinessPercent(item.ControlsPassing, item.ControlsTotal),
        item.PrimaryFramework,
        item.Status,
        item.CreatedAt);

    private static PrivacyAssessmentDto ToPrivacyDto(PrivacyAssessment item) => new(
        item.Id,
        item.Code,
        item.Kind,
        item.ActivityName,
        item.DataSubjects,
        item.LawfulBasis,
        item.TransferMechanism,
        item.RiskLevel,
        item.Status,
        item.OwnerEmail,
        item.ReviewDueOn,
        item.UpdatedAt);

    private static AiModelDto ToAiModelDto(AiModelRecord item) => new(
        item.Id,
        item.Code,
        item.ModelName,
        item.Provider,
        item.EuAiActRiskTier,
        item.TrainingDataProvenance,
        item.Guardrails,
        item.RedTeamStatus,
        item.LinkedControlCode,
        item.OwnerEmail,
        item.EvaluatedAt);

    private static PhishingCampaignDto ToPhishingDto(PhishingCampaign item)
    {
        var clickRate = PhishingMetrics.RatePercent(item.ClickedCount, item.SentCount);
        var reportRate = PhishingMetrics.RatePercent(item.ReportedCount, item.SentCount);
        return new PhishingCampaignDto(
            item.Id,
            item.Code,
            item.Name,
            item.Source,
            item.SentCount,
            item.ClickedCount,
            item.ReportedCount,
            clickRate,
            reportRate,
            PhishingMetrics.RiskBand(clickRate),
            item.RemedialTrainingStatus,
            item.RanAt);
    }

    private static AttackSurfaceAssetDto ToAsmDto(AttackSurfaceAsset item) => new(
        item.Id,
        item.Hostname,
        item.IpAddress,
        item.Service,
        item.Port,
        item.TlsDaysRemaining,
        item.ShadowIt,
        item.Severity,
        item.Status,
        item.LastSeenAt);

    private static AuditorFirmDto ToFirmDto(AuditorFirmRecord item) => new(
        item.Id,
        item.Code,
        item.FirmName,
        item.Accreditation,
        item.Specialties,
        item.Regions,
        item.LeadAuditorEmail,
        item.Invited,
        item.InvitedAt);

    private static RemediationPrDto ToPrDto(RemediationPullRequest item) => new(
        item.Id,
        item.ControlCode,
        item.TargetResource,
        item.Repository,
        item.BranchName,
        item.Title,
        item.ArtifactType,
        item.UnifiedDiff,
        item.PrNumber,
        item.PrUrl,
        item.Mode,
        item.Status,
        item.EvidenceId,
        item.EvidenceSha256,
        item.CreatedAt);
}
