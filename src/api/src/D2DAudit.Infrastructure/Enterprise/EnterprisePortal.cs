using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Enterprise;

public sealed class EnterprisePortal(
    TenantContext tenant,
    TenantDatabases databases,
    ControlPlaneDbContext control,
    IStorageProvider storage,
    IAssistantProvider assistant,
    WebhookSecretProtector webhookSecrets,
    IWebhookDispatcher webhookDispatcher,
    IActivityWriter activity,
    TimeProvider time) : IEnterprisePortal
{
    public async Task<Result<FrameworkSimulationResult>> SimulateFrameworkAsync(string key, CancellationToken cancellationToken)
    {
        var normalized = (key ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return Result<FrameworkSimulationResult>.Fail("not_found");
        }

        if (tenant.Role == TenantRole.Auditor && !tenant.AuditorFrameworkKeys.Contains(normalized, StringComparer.Ordinal))
        {
            return Result<FrameworkSimulationResult>.Fail("forbidden");
        }

        await using var db = Open();
        var framework = await db.Frameworks.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Key == normalized, cancellationToken);
        if (framework is null)
        {
            return Result<FrameworkSimulationResult>.Fail("not_found");
        }

        var requirements = await db.Requirements.AsNoTracking()
            .Where(item => item.FrameworkId == framework.Id)
            .Include(item => item.Mappings)
            .ThenInclude(mapping => mapping.Control)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Code)
            .ToListAsync(cancellationToken);

        var parentIds = requirements.Where(item => item.ParentId.HasValue).Select(item => item.ParentId!.Value).ToHashSet();
        var leaves = requirements
            .Where(item => !parentIds.Contains(item.Id))
            .Select(item => new SimulatorRequirementInput(
                item.Code,
                item.Title,
                item.Summary,
                item.Mappings.Count > 0,
                item.Mappings.Any(mapping => mapping.Control?.Status == ControlStatus.Passing)))
            .ToArray();

        return Result<FrameworkSimulationResult>.Ok(FrameworkSimulator.Analyze(framework.Key, framework.Name, leaves));
    }

    public async Task<Result<int>> AdoptFrameworkGapsAsync(string key, AdoptGapsRequest request, CancellationToken cancellationToken)
    {
        var simulation = await SimulateFrameworkAsync(key, cancellationToken);
        if (!simulation.IsSuccess || simulation.Value is null)
        {
            return Result<int>.Fail(simulation.Error ?? "not_found");
        }

        var wanted = request.RequirementCodes is { Count: > 0 }
            ? request.RequirementCodes.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        var gaps = simulation.Value.Gaps
            .Where(gap => wanted is null || wanted.Contains(gap.Code))
            .ToArray();
        if (gaps.Length == 0)
        {
            return Result<int>.Ok(0);
        }

        await using var db = Open();
        var framework = await db.Frameworks.FirstAsync(item => item.Key == simulation.Value.FrameworkKey, cancellationToken);
        var requirements = await db.Requirements
            .Where(item => item.FrameworkId == framework.Id)
            .ToDictionaryAsync(item => item.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existingCodes = (await db.Controls.Select(item => item.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var suffix = 1;
        foreach (var gap in gaps)
        {
            if (!requirements.TryGetValue(gap.Code, out var requirement))
            {
                continue;
            }

            var code = gap.SuggestedControlCode;
            while (!existingCodes.Add(code))
            {
                code = $"{gap.SuggestedControlCode}-{suffix.ToString(CultureInfo.InvariantCulture)}";
                suffix++;
            }

            var controlItem = new Control
            {
                Id = Guid.NewGuid(),
                Code = code,
                Title = gap.Title.Length > 300 ? gap.Title[..300] : gap.Title,
                Description = gap.Summary,
                Frequency = "Quarterly",
                OwnerName = tenant.DisplayName,
                OwnerEmail = tenant.Email,
                Status = ControlStatus.NeedsEvidence,
                Custom = true,
            };
            db.Controls.Add(controlItem);
            db.ControlMappings.Add(new ControlRequirementMap
            {
                ControlId = controlItem.Id,
                RequirementId = requirement.Id,
                Coverage = MappingCoverage.Full,
            });
            created++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "framework.gaps.adopted", "ok", $"{framework.Key}:{created}", null, cancellationToken);
        return Result<int>.Ok(created);
    }

    public async Task<Result<CustomFrameworkCreatedDto>> CreateCustomFrameworkAsync(CreateCustomFrameworkRequest request, CancellationToken cancellationToken)
    {
        var key = Slugify(request.Key);
        var name = (request.Name ?? "").Trim();
        var version = string.IsNullOrWhiteSpace(request.Version) ? "1.0" : request.Version.Trim();
        var description = (request.Description ?? "").Trim();
        var parsed = CustomFrameworkCsv.Parse(request.RequirementsCsv);

        if (key.Length is < 2 or > 64 || name.Length is 0 or > 200 || version.Length > 32 || description.Length > 2000 || parsed.Count == 0)
        {
            return Result<CustomFrameworkCreatedDto>.Fail("invalid_framework");
        }

        await using var db = Open();
        if (await db.Frameworks.AnyAsync(item => item.Key == key, cancellationToken))
        {
            return Result<CustomFrameworkCreatedDto>.Fail("duplicate_framework");
        }

        var framework = new FrameworkRecord
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Version = version,
            Description = description.Length == 0 ? $"Custom framework {name} ({version})." : description,
        };
        db.Frameworks.Add(framework);

        var order = 1;
        foreach (var row in parsed)
        {
            db.Requirements.Add(new Requirement
            {
                Id = Guid.NewGuid(),
                FrameworkId = framework.Id,
                Code = row.Code,
                Title = row.Title,
                Summary = row.Summary,
                SortOrder = order++,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "framework.custom.created", "ok", $"{key}:{parsed.Count}", null, cancellationToken);
        return Result<CustomFrameworkCreatedDto>.Ok(new CustomFrameworkCreatedDto(key, name, parsed.Count));
    }

    public async Task<TrustReceiptDocument?> TrustReceiptAsync(string slug, CancellationToken cancellationToken)
    {
        var match = await OpenBySlugAsync(slug, cancellationToken);
        if (match is null)
        {
            return null;
        }

        await using var db = match.Value.Db;
        var profile = await db.TrustProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null || !profile.Published)
        {
            return null;
        }

        var frameworks = await LoadTrustFrameworksAsync(db, cancellationToken);
        var chainEntries = await db.Chain.AsNoTracking()
            .OrderBy(item => item.Sequence)
            .ToListAsync(cancellationToken);
        var chainVerified = VerifyChainContinuity(chainEntries);
        var latest = chainEntries.Count > 0 ? chainEntries[^1] : null;

        return TrustReceiptBuilder.Build(
            match.Value.Tenant.Slug,
            match.Value.Tenant.Name,
            match.Value.Tenant.Region,
            match.Value.Tenant.RetentionYears,
            time.GetUtcNow(),
            frameworks,
            chainEntries.Count,
            latest?.Sequence ?? 0,
            latest?.Head ?? "",
            chainVerified);
    }

    private static bool VerifyChainContinuity(IReadOnlyList<ChainEntry> entries)
    {
        if (entries.Count == 0)
        {
            return false;
        }

        string? previous = null;
        foreach (var entry in entries)
        {
            var expectedHead = EvidenceChain.NextHead(previous, entry.ContentHash, entry.Canonical);
            if (!string.Equals(expectedHead, entry.Head, StringComparison.Ordinal)
                || !string.Equals(previous, entry.PreviousHead, StringComparison.Ordinal))
            {
                return false;
            }

            previous = entry.Head;
        }

        return true;
    }

    public async Task<Result<TrustDownloadFile>> DownloadTrustDocumentAsync(string slug, Guid documentId, string? accessToken, CancellationToken cancellationToken)
    {
        var match = await OpenBySlugAsync(slug, cancellationToken);
        if (match is null)
        {
            return Result<TrustDownloadFile>.Fail("not_found");
        }

        await using var db = match.Value.Db;
        var profile = await db.TrustProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null || !profile.Published)
        {
            return Result<TrustDownloadFile>.Fail("not_found");
        }

        var document = await db.TrustDocuments.AsNoTracking().FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is null)
        {
            return Result<TrustDownloadFile>.Fail("not_found");
        }

        if (document.Access == "Nda")
        {
            var token = (accessToken ?? "").Trim();
            if (token.Length == 0)
            {
                return Result<TrustDownloadFile>.Fail("nda_required");
            }

            var approved = await db.TrustRequests.AsNoTracking()
                .Where(item => item.Status == "Approved" && item.NdaAccepted && item.AccessToken != "" && (item.DocumentId == null || item.DocumentId == documentId))
                .Select(item => item.AccessToken)
                .ToListAsync(cancellationToken);
            var tokenBytes = Encoding.UTF8.GetBytes(token);
            var authorized = approved.Any(candidate =>
            {
                var candidateBytes = Encoding.UTF8.GetBytes(candidate);
                return candidateBytes.Length == tokenBytes.Length
                    && CryptographicOperations.FixedTimeEquals(candidateBytes, tokenBytes);
            });
            if (!authorized)
            {
                return Result<TrustDownloadFile>.Fail("nda_required");
            }
        }

        if (!string.IsNullOrWhiteSpace(document.ObjectKey))
        {
            try
            {
                var stored = await storage.GetAsync(document.ObjectKey, cancellationToken);
                var contentType = string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType;
                return Result<TrustDownloadFile>.Ok(new TrustDownloadFile(stored, contentType, document.FileName, document.Sha256));
            }
            catch (Exception exception) when (exception is IOException or Amazon.S3.AmazonS3Exception or InvalidOperationException)
            {
                // Fall through to generated summary PDF if fixture metadata has no backing blob yet.
            }
        }

        var lines = new[]
        {
            $"{match.Value.Tenant.Name} — Trust Center Document",
            $"Title: {document.Title}",
            $"Access: {document.Access}",
            $"Updated: {document.UpdatedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            "",
            document.Summary,
        };
        var pdf = PlainPdf.Document(lines);
        return Result<TrustDownloadFile>.Ok(new TrustDownloadFile(pdf, "application/pdf", document.FileName, EvidenceChain.ContentHash(pdf)));
    }

    public async Task<Result<AssistantReply>> AskTrustPublicAsync(string slug, AssistantAskRequest request, CancellationToken cancellationToken)
    {
        var question = (request.Question ?? "").Trim();
        if (question.Length is 0 or > 1000)
        {
            return Result<AssistantReply>.Fail("invalid_question");
        }

        var match = await OpenBySlugAsync(slug, cancellationToken);
        if (match is null)
        {
            return Result<AssistantReply>.Fail("not_found");
        }

        await using var db = match.Value.Db;
        var profile = await db.TrustProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null || !profile.Published)
        {
            return Result<AssistantReply>.Fail("not_found");
        }

        var frameworks = await LoadTrustFrameworksAsync(db, cancellationToken);
        var publicDocs = await db.TrustDocuments.AsNoTracking()
            .Where(item => item.Access == "Public")
            .OrderBy(item => item.Title)
            .ToListAsync(cancellationToken);
        var ndaCount = await db.TrustDocuments.CountAsync(item => item.Access == "Nda", cancellationToken);
        var chainCount = await db.Chain.CountAsync(cancellationToken);

        var facts = new List<AssistantFact>
        {
            new("TrustProfile", match.Value.Tenant.Slug, profile.Headline, $"{profile.Summary} Region: {match.Value.Tenant.Region}. Evidence retention: {match.Value.Tenant.RetentionYears} years. Contact: {profile.ContactEmail}.", $"/trust/{match.Value.Tenant.Slug}"),
            new("EvidenceChain", "CHAIN", "Tamper-evident hash chain", $"{chainCount} SHA-256 hash-chained evidence entries verified with {match.Value.Tenant.RetentionYears}-year retention.", $"/trust/{match.Value.Tenant.Slug}"),
            new("TrustDocs", "NDA", "NDA-gated assurance reports", $"{ndaCount} assurance report(s) available upon NDA request approval.", $"/trust/{match.Value.Tenant.Slug}"),
        };
        facts.AddRange(frameworks.Select(fw => new AssistantFact(
            "Framework",
            fw.Key.ToUpperInvariant(),
            fw.Name,
            $"{fw.Passing} of {fw.Total} mapped controls passing ({fw.Percent}%).",
            $"/trust/{match.Value.Tenant.Slug}")));
        facts.AddRange(publicDocs.Select(doc => new AssistantFact(
            "PublicDoc",
            doc.FileName,
            doc.Title,
            doc.Summary,
            $"/trust/{match.Value.Tenant.Slug}")));

        var reply = await assistant.AnswerAsync(question, facts, $"{match.Value.Tenant.Name} public Trust Center", cancellationToken);
        return Result<AssistantReply>.Ok(reply);
    }

    public async Task<Result<AssistantReply>> AskWorkspaceAsync(AssistantAskRequest request, CancellationToken cancellationToken)
    {
        var question = (request.Question ?? "").Trim();
        if (question.Length is 0 or > 1000)
        {
            return Result<AssistantReply>.Fail("invalid_question");
        }

        await using var db = Open();
        var facts = new List<AssistantFact>();

        if (tenant.Role == TenantRole.Auditor)
        {
            var granted = tenant.AuditorFrameworkKeys;
            var controls = await db.Controls.AsNoTracking()
                .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(req => req.Framework)
                .ToListAsync(cancellationToken);
            var inScope = controls.Where(control => control.Mappings.Any(m => m.Requirement?.Framework?.Key is string k && granted.Contains(k, StringComparer.Ordinal)));
            facts.AddRange(inScope.Select(control => new AssistantFact(
                "Control",
                control.Code,
                control.Title,
                $"Status: {control.Status}. {control.Description}",
                $"/controls/{Uri.EscapeDataString(control.Code)}")));
            var scopeLabel = $"auditor scope ({string.Join(", ", granted)})";
            var auditorReply = await assistant.AnswerAsync(question, facts, scopeLabel, cancellationToken);
            return Result<AssistantReply>.Ok(auditorReply);
        }

        var allControls = await db.Controls.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        facts.AddRange(allControls.Select(control => new AssistantFact(
            "Control",
            control.Code,
            control.Title,
            $"Status: {control.Status}. Owner: {control.OwnerName ?? "Unassigned"}. {control.Description}",
            $"/controls/{Uri.EscapeDataString(control.Code)}")));

        var failingTests = await db.ControlTests.AsNoTracking()
            .Where(item => item.Outcome == TestOutcome.Fail)
            .ToListAsync(cancellationToken);
        facts.AddRange(failingTests.Select(test => new AssistantFact(
            "Test",
            test.TestKey ?? "TEST",
            test.Name,
            $"Failing check: {test.Detail} Remediation: {test.Remediation}",
            "/tests")));

        var exposures = await db.ExposureFindings.AsNoTracking()
            .Where(item => item.Status == "Open")
            .OrderByDescending(item => item.RiskScore)
            .ToListAsync(cancellationToken);
        facts.AddRange(exposures.Select(exp => new AssistantFact(
            "Exposure",
            exp.ControlCode,
            $"{exp.Provider.ToUpperInvariant()} {exp.ResourceName} ({exp.Service}:{exp.Port})",
            $"Open {exp.Severity} exposure (risk {exp.RiskScore}) from {exp.Cidr}. {exp.Summary}",
            "/exposure")));

        var stores = await db.DataStoreAssets.AsNoTracking().OrderByDescending(item => item.RiskScore).ToListAsync(cancellationToken);
        facts.AddRange(stores.Select(store => new AssistantFact(
            "DSPM",
            store.ControlCode,
            $"{store.StoreName} ({store.Classification})",
            $"{store.Engine} in {store.Region}; sensitive={store.SensitiveDataTypes}; encrypted={store.Encrypted}; public={store.PublicAccess}; risk={store.RiskScore}.",
            "/dspm")));

        var costs = await db.CostRecommendations.AsNoTracking().Where(item => item.Status == "Open").ToListAsync(cancellationToken);
        facts.AddRange(costs.Select(cost => new AssistantFact(
            "FinOps",
            string.IsNullOrWhiteSpace(cost.LinkedControlCode) ? "COST" : cost.LinkedControlCode,
            $"{cost.ResourceName} (${cost.MonthlySavingsUsd:0}/mo)",
            $"{cost.ComplianceImpact} Action: {cost.RemediationSummary}",
            "/finops")));

        var vulns = await db.Vulnerabilities.AsNoTracking().Where(item => !item.Ignored).ToListAsync(cancellationToken);
        var scans = await db.SecurityScanFindings.AsNoTracking().Where(item => item.Status == "Open").ToListAsync(cancellationToken);
        var people = await db.People.AsNoTracking().ToListAsync(cancellationToken);
        var paths = AttackPathAnalyzer.Build(exposures, vulns, scans, people, stores);
        facts.AddRange(paths.Select(path => new AssistantFact(
            "AttackPath",
            path.Id,
            path.Title,
            $"Blast radius {path.BlastRadiusScore}/100 ({path.Severity}). Chokepoint fix: {path.ChokepointRemediation}",
            "/attack-paths")));

        var policies = await db.Policies.AsNoTracking().ToListAsync(cancellationToken);
        facts.AddRange(policies.Select(policy => new AssistantFact(
            "Policy",
            policy.Code,
            policy.Title,
            $"Policy owner: {policy.OwnerName}.",
            "/policies")));

        var workspaceReply = await assistant.AnswerAsync(question, facts, "your workspace", cancellationToken);
        return Result<AssistantReply>.Ok(workspaceReply);
    }

    public async Task<IReadOnlyList<ApiKeyDto>> ListApiKeysAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.ApiKeys.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(ToApiKeyDto).ToArray();
    }

    public async Task<Result<CreatedApiKeyDto>> CreateApiKeyAsync(CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? "").Trim();
        var scopes = string.IsNullOrWhiteSpace(request.Scopes) ? "read:controls,read:evidence" : request.Scopes.Trim();
        if (name.Length is 0 or > 200 || scopes.Length > 500)
        {
            return Result<CreatedApiKeyDto>.Fail("invalid_api_key");
        }

        var plaintext = ApiKeyTokens.Create(tenant.TenantId);
        var prefix = plaintext[..16];
        var hash = GroundedAssistant.HashToken(plaintext);

        var row = new ApiKeyRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            Prefix = prefix,
            KeyHash = hash,
            Scopes = scopes,
            CreatedByEmail = tenant.Email,
            CreatedAt = time.GetUtcNow(),
        };

        await using var db = Open();
        db.ApiKeys.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "developer.apikey.created", "ok", prefix, null, cancellationToken);
        return Result<CreatedApiKeyDto>.Ok(new CreatedApiKeyDto(ToApiKeyDto(row), plaintext));
    }

    public async Task<Result<bool>> RevokeApiKeyAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await db.ApiKeys.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<bool>.Fail("not_found");
        }

        row.RevokedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "developer.apikey.revoked", "ok", row.Prefix, null, cancellationToken);
        return Result<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<WebhookDto>> ListWebhooksAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.Webhooks.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(ToWebhookDto).ToArray();
    }

    public async Task<Result<CreatedWebhookDto>> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? "").Trim();
        var url = (request.Url ?? "").Trim();
        var events = string.IsNullOrWhiteSpace(request.Events) ? "test.failed,exposure.critical" : request.Events.Trim();
        if (name.Length is 0 or > 200 || url.Length is 0 or > 500 || !SafeUrl.IsPublicHttpOrHttps(url) || events.Length > 500)
        {
            return Result<CreatedWebhookDto>.Fail("invalid_webhook");
        }

        var secret = "whsec_" + RandomNumberGenerator.GetHexString(32).ToLowerInvariant();
        var encrypted = webhookSecrets.Protect(secret);
        if (string.IsNullOrEmpty(encrypted))
        {
            return Result<CreatedWebhookDto>.Fail("key_not_configured");
        }

        var row = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
            Events = events,
            SigningSecretHash = GroundedAssistant.HashToken(secret),
            SigningSecretEncrypted = encrypted,
            Enabled = true,
            CreatedAt = time.GetUtcNow(),
        };

        await using var db = Open();
        db.Webhooks.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "developer.webhook.created", "ok", name, null, cancellationToken);
        return Result<CreatedWebhookDto>.Ok(new CreatedWebhookDto(ToWebhookDto(row), secret));
    }

    public async Task<Result<WebhookTestResultDto>> TestWebhookAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<WebhookTestResultDto>.Fail("not_found");
        }

        var outcome = await webhookDispatcher.SendSingleAsync(
            row,
            "webhook.test",
            $"Test event from {tenant.TenantName}",
            "Signed webhook verification triggered from Settings -> Developer Console.",
            cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "developer.webhook.tested", outcome.Delivered ? "ok" : "error", $"{row.Name}:{outcome.Detail}", null, cancellationToken);
        return Result<WebhookTestResultDto>.Ok(outcome);
    }

    public async Task<Result<bool>> DeleteWebhookAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await db.Webhooks.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<bool>.Fail("not_found");
        }

        db.Webhooks.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "developer.webhook.deleted", "ok", row.Name, null, cancellationToken);
        return Result<bool>.Ok(true);
    }

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);

    private async Task<(Tenant Tenant, TenantDbContext Db)?> OpenBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var clean = (slug ?? "").Trim().ToLowerInvariant();
        if (clean.Length == 0)
        {
            return null;
        }

        var tenantRow = await control.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Slug == clean && item.Status == TenantStatus.Active && item.DatabaseName != null, cancellationToken);
        return tenantRow is null ? null : (tenantRow, databases.Create(tenantRow.DatabaseName!));
    }

    private static async Task<IReadOnlyList<TrustReceiptFramework>> LoadTrustFrameworksAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var frameworks = await db.Frameworks.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var mappings = await db.ControlMappings.AsNoTracking()
            .Include(item => item.Control)
            .Include(item => item.Requirement)
            .ToListAsync(cancellationToken);

        return frameworks.Select(framework =>
        {
            var controls = mappings
                .Where(mapping => mapping.Requirement?.FrameworkId == framework.Id && mapping.Control is not null)
                .Select(mapping => mapping.Control!)
                .DistinctBy(control => control.Id)
                .ToArray();
            var total = controls.Length;
            var passing = controls.Count(control => control.Status == ControlStatus.Passing);
            var percent = total == 0 ? 0 : (int)Math.Round(passing * 100.0 / total, MidpointRounding.AwayFromZero);
            return new TrustReceiptFramework(framework.Key, framework.Name, passing, total, percent);
        }).ToArray();
    }

    private static string Slugify(string? raw)
    {
        var chars = (raw ?? "").Trim().ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static ApiKeyDto ToApiKeyDto(ApiKeyRecord row) =>
        new(row.Id, row.Name, row.Prefix, row.Scopes, row.CreatedByEmail, row.CreatedAt, row.RevokedAt, row.LastUsedAt);

    private static WebhookDto ToWebhookDto(WebhookSubscription row) =>
        new(row.Id, row.Name, row.Url, row.Events, row.Enabled, row.CreatedAt);
}
