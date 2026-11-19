using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.CloudSecurity;

public sealed class CloudSecurityPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    IAutomationDispatcher automation,
    TimeProvider time) : ICloudSecurity
{
    public async Task<ExposureBoardDto> ExposureBoardAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return ToExposureBoard([]);
        }

        await using var db = Open();
        var rows = await db.ExposureFindings.AsNoTracking()
            .OrderBy(item => item.Status)
            .ThenByDescending(item => item.RiskScore)
            .ThenBy(item => item.ResourceName)
            .ToListAsync(cancellationToken);
        return ToExposureBoard(rows);
    }

    public async Task<Result<ExposureBoardDto>> ScanExposureAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var now = time.GetUtcNow();
        var snapshots = await db.Resources.AsNoTracking().ToListAsync(cancellationToken);
        var normalized = snapshots.Select(ProviderSet.ToResource).ToArray();
        var discovered = ExposureAnalyzer.AnalyzeSnapshots(normalized, now);
        var existing = await db.ExposureFindings.ToListAsync(cancellationToken);
        var addedCritical = 0;

        foreach (var candidate in discovered)
        {
            var match = existing.FirstOrDefault(item =>
                item.Provider == candidate.Provider
                && item.ResourceName == candidate.ResourceName
                && item.Port == candidate.Port
                && item.Status == "Open");
            if (match is not null)
            {
                match.RiskScore = candidate.RiskScore;
                match.Severity = candidate.Severity;
                match.Summary = candidate.Summary;
                match.RemediationTerraform = candidate.RemediationTerraform;
                match.RemediationCli = candidate.RemediationCli;
                continue;
            }

            db.ExposureFindings.Add(candidate);
            existing.Add(candidate);
            if (candidate.Severity == "Critical")
            {
                addedCritical++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.exposure.scanned", "ok", discovered.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), null, cancellationToken);
        if (addedCritical > 0)
        {
            await automation.DispatchAsync(
                "ExposureCritical",
                $"Exposure scan discovered {addedCritical} critical public exposure finding(s).",
                "Review /exposure for open ports, public buckets, and 1-click Terraform remediation.",
                cancellationToken);
            await automation.DispatchAsync(
                "TestFailed",
                $"Exposure scan discovered {addedCritical} critical public exposure finding(s).",
                "Review /exposure for open ports, public buckets, and 1-click Terraform remediation.",
                cancellationToken);
        }

        return Result<ExposureBoardDto>.Ok(ToExposureBoard(existing
            .OrderBy(item => item.Status, StringComparer.Ordinal)
            .ThenByDescending(item => item.RiskScore)
            .ThenBy(item => item.ResourceName, StringComparer.Ordinal)
            .ToArray()));
    }

    public async Task<Result<ExposureItemDto>> ResolveExposureAsync(Guid id, ResolveFindingRequest request, CancellationToken cancellationToken)
    {
        var note = (request.Note ?? "").Trim();
        if (note.Length is 0 or > 500)
        {
            return Result<ExposureItemDto>.Fail("invalid_resolution");
        }

        await using var db = Open();
        var row = await db.ExposureFindings.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<ExposureItemDto>.Fail("not_found");
        }

        row.Status = "Resolved";
        row.ResolutionNote = note;
        row.ResolvedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.exposure.resolved", "ok", row.ResourceName, null, cancellationToken);
        return Result<ExposureItemDto>.Ok(ToExposureDto(row));
    }

    public async Task<SecurityScansBoardDto> ScansBoardAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return new SecurityScansBoardDto(0, 0, 0, [], [], []);
        }

        await using var db = Open();
        var rows = await db.SecurityScanFindings.AsNoTracking()
            .OrderBy(item => item.Status)
            .ThenByDescending(item => item.DetectedAt)
            .ToListAsync(cancellationToken);
        var dtos = rows.Select(ToScanDto).ToArray();
        var containers = dtos.Where(item => item.Kind == "ContainerCve").ToArray();
        var licenses = dtos.Where(item => item.Kind == "License").ToArray();
        var secrets = dtos.Where(item => item.Kind == "Secret").ToArray();
        return new SecurityScansBoardDto(
            containers.Count(item => item.Status == "Open"),
            licenses.Count(item => item.Status == "Open"),
            secrets.Count(item => item.Status == "Open"),
            containers,
            licenses,
            secrets);
    }

    public async Task<Result<int>> IngestSarifAsync(IngestSarifRequest request, CancellationToken cancellationToken)
    {
        var asset = (request.AssetName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(request.SarifJson) || asset.Length > 200)
        {
            return Result<int>.Fail("invalid_sarif");
        }

        IReadOnlyList<SecurityScanFinding> parsed;
        try
        {
            parsed = SarifParser.Parse(request.SarifJson, asset, time.GetUtcNow());
        }
        catch (JsonException)
        {
            return Result<int>.Fail("invalid_sarif");
        }

        if (parsed.Count == 0)
        {
            return Result<int>.Fail("invalid_sarif");
        }

        await using var db = Open();
        db.SecurityScanFindings.AddRange(parsed);
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.sarif.ingested", "ok", parsed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), null, cancellationToken);
        return Result<int>.Ok(parsed.Count);
    }

    public async Task<Result<ScanFindingDto>> ResolveScanAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var row = await db.SecurityScanFindings.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<ScanFindingDto>.Fail("not_found");
        }

        row.Status = "Resolved";
        row.ResolvedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.scan.resolved", "ok", row.RuleId, null, cancellationToken);
        return Result<ScanFindingDto>.Ok(ToScanDto(row));
    }

    public async Task<DspmBoardDto> DspmBoardAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return ToDspmBoard([]);
        }

        await using var db = Open();
        var rows = await db.DataStoreAssets.AsNoTracking()
            .OrderByDescending(item => item.RiskScore)
            .ThenBy(item => item.StoreName)
            .ToListAsync(cancellationToken);
        return ToDspmBoard(rows);
    }

    public async Task<Result<DataStoreDto>> SaveDataStoreAsync(SaveDataStoreRequest request, CancellationToken cancellationToken)
    {
        var name = (request.StoreName ?? "").Trim();
        var provider = (request.Provider ?? "").Trim().ToLowerInvariant();
        var engine = (request.Engine ?? "").Trim();
        var region = (request.Region ?? "").Trim();
        var classification = NormalizeClassification(request.Classification);
        var dataTypes = (request.SensitiveDataTypes ?? "").Trim();
        var controlCode = string.IsNullOrWhiteSpace(request.ControlCode) ? "CR-02" : request.ControlCode.Trim();
        var owner = (request.OwnerEmail ?? "").Trim();

        if (name.Length is 0 or > 200
            || provider.Length is 0 or > 32
            || engine.Length is 0 or > 40
            || region.Length is 0 or > 32
            || dataTypes.Length > 300
            || owner.Length > 320
            || request.RecordCountEstimate < 0)
        {
            return Result<DataStoreDto>.Fail("invalid_datastore");
        }

        await using var db = Open();
        DataStoreAsset row;
        if (request.Id is Guid id)
        {
            var existing = await db.DataStoreAssets.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (existing is null)
            {
                return Result<DataStoreDto>.Fail("not_found");
            }

            row = existing;
        }
        else
        {
            var duplicate = await db.DataStoreAssets.FirstOrDefaultAsync(
                item => item.Provider == provider && item.StoreName == name,
                cancellationToken);
            if (duplicate is not null)
            {
                row = duplicate;
            }
            else
            {
                row = new DataStoreAsset { Id = Guid.NewGuid() };
                db.DataStoreAssets.Add(row);
            }
        }

        row.StoreName = name;
        row.Provider = provider;
        row.Engine = engine;
        row.Region = region;
        row.Classification = classification;
        row.SensitiveDataTypes = dataTypes;
        row.RecordCountEstimate = request.RecordCountEstimate;
        row.Encrypted = request.Encrypted;
        row.PublicAccess = request.PublicAccess;
        row.RetentionCompliant = request.RetentionCompliant;
        row.ControlCode = controlCode;
        row.OwnerEmail = owner;
        row.RiskScore = DspmScorer.Score(classification, request.PublicAccess, request.Encrypted, dataTypes, request.RetentionCompliant);
        row.LastScannedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.dspm.saved", "ok", row.StoreName, null, cancellationToken);
        return Result<DataStoreDto>.Ok(ToDataStoreDto(row));
    }

    public async Task<Result<DspmBoardDto>> ScanDspmAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var now = time.GetUtcNow();
        var snapshots = await db.Resources.AsNoTracking()
            .Where(item => item.Kind == "bucket" || item.Kind == "database")
            .ToListAsync(cancellationToken);
        var existing = await db.DataStoreAssets.ToListAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var resource = ProviderSet.ToResource(snapshot);
            var isPublic = resource.Attributes.TryGetValue("public", out var pub) && pub == "true";
            var isEncrypted = !resource.Attributes.TryGetValue("encrypted", out var enc) || enc != "false";
            var match = existing.FirstOrDefault(item =>
                item.Provider == resource.Provider && item.StoreName == resource.Name);
            if (match is not null)
            {
                match.PublicAccess = isPublic;
                match.Encrypted = isEncrypted;
                match.RiskScore = DspmScorer.Score(match.Classification, match.PublicAccess, match.Encrypted, match.SensitiveDataTypes, match.RetentionCompliant);
                match.LastScannedAt = now;
                continue;
            }

            var classification = isPublic ? "Confidential" : "Internal";
            var dataTypes = snapshot.Kind == "database" ? "PII,Email" : "Operational Logs";
            var created = new DataStoreAsset
            {
                Id = Guid.NewGuid(),
                StoreName = resource.Name,
                Provider = resource.Provider,
                Engine = EngineFor(resource.Provider, snapshot.Kind),
                Region = RegionFor(resource.Provider),
                Classification = classification,
                SensitiveDataTypes = dataTypes,
                RecordCountEstimate = snapshot.Kind == "database" ? 250_000 : 12_000,
                Encrypted = isEncrypted,
                PublicAccess = isPublic,
                RetentionCompliant = true,
                ControlCode = isPublic ? "CFG-01" : "CR-02",
                RiskScore = DspmScorer.Score(classification, isPublic, isEncrypted, dataTypes, true),
                OwnerEmail = "",
                LastScannedAt = now,
            };
            db.DataStoreAssets.Add(created);
            existing.Add(created);
        }

        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.dspm.scanned", "ok", existing.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), null, cancellationToken);
        return Result<DspmBoardDto>.Ok(ToDspmBoard(existing.OrderByDescending(item => item.RiskScore).ThenBy(item => item.StoreName, StringComparer.Ordinal).ToArray()));
    }

    public async Task<FinOpsBoardDto> FinOpsBoardAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return new FinOpsBoardDto(FinOpsAnalyzer.Summarize([]), []);
        }

        await using var db = Open();
        var rows = await db.CostRecommendations.AsNoTracking()
            .OrderBy(item => item.Status)
            .ThenByDescending(item => item.MonthlySavingsUsd)
            .ToListAsync(cancellationToken);
        return new FinOpsBoardDto(FinOpsAnalyzer.Summarize(rows), rows.Select(ToCostDto).ToArray());
    }

    public async Task<Result<CostRecommendationDto>> UpdateCostStatusAsync(Guid id, UpdateCostStatusRequest request, CancellationToken cancellationToken)
    {
        var status = request.Status switch
        {
            "Applied" => "Applied",
            "Dismissed" => "Dismissed",
            "Open" => "Open",
            _ => "",
        };
        if (status.Length == 0)
        {
            return Result<CostRecommendationDto>.Fail("invalid_status");
        }

        await using var db = Open();
        var row = await db.CostRecommendations.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<CostRecommendationDto>.Fail("not_found");
        }

        row.Status = status;
        row.ActionedAt = status == "Open" ? null : time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.finops.updated", "ok", row.ResourceName + ":" + status, null, cancellationToken);
        return Result<CostRecommendationDto>.Ok(ToCostDto(row));
    }

    public async Task<AttackPathsBoardDto> AttackPathsAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsAuditor)
        {
            return new AttackPathsBoardDto(0, 0, 0, []);
        }

        await using var db = Open();
        var exposures = await db.ExposureFindings.AsNoTracking().ToListAsync(cancellationToken);
        var vulns = await db.Vulnerabilities.AsNoTracking().ToListAsync(cancellationToken);
        var scans = await db.SecurityScanFindings.AsNoTracking().ToListAsync(cancellationToken);
        var people = await db.People.AsNoTracking().ToListAsync(cancellationToken);
        var stores = await db.DataStoreAssets.AsNoTracking().ToListAsync(cancellationToken);
        var paths = AttackPathAnalyzer.Build(exposures, vulns, scans, people, stores);
        return new AttackPathsBoardDto(
            paths.Count,
            paths.Count(item => item.Severity == "Critical"),
            paths.Count == 0 ? 0 : paths.Max(item => item.BlastRadiusScore),
            paths);
    }

    private TenantDbContext Open() => databases.Create(tenant.DatabaseName);

    private static ExposureBoardDto ToExposureBoard(IReadOnlyList<ExposureFinding> rows)
    {
        var open = rows.Where(item => item.Status == "Open").ToArray();
        return new ExposureBoardDto(
            open.Length,
            open.Count(item => item.Severity == "Critical"),
            open.Count(item => item.Severity == "High"),
            open.Count(item => item.ResourceKind == "StorageBucket"),
            open.Count(item => item.ResourceKind is "NetworkFirewall" or "ManagedDatabase"),
            rows.Select(ToExposureDto).ToArray());
    }

    private static ExposureItemDto ToExposureDto(ExposureFinding row) => new(
        row.Id,
        row.Provider,
        row.ResourceName,
        row.ResourceKind,
        row.Region,
        row.Cidr,
        row.Port,
        row.Protocol,
        row.Service,
        row.Severity,
        row.RiskScore,
        row.ControlCode,
        row.Summary,
        row.RemediationTerraform,
        row.RemediationCli,
        row.Status,
        row.ResolutionNote,
        row.DetectedAt,
        row.ResolvedAt);

    private static ScanFindingDto ToScanDto(SecurityScanFinding row) => new(
        row.Id,
        row.Kind,
        row.Severity,
        row.AssetName,
        row.Location,
        row.LineNumber,
        row.RuleId,
        row.Title,
        row.Detail,
        row.Snippet,
        row.Source,
        row.FixVersion,
        row.ControlCode,
        row.Status,
        row.DetectedAt,
        row.ResolvedAt);

    private static DspmBoardDto ToDspmBoard(IReadOnlyList<DataStoreAsset> rows) => new(
        rows.Count,
        rows.Count(item => item.Classification == "Restricted"),
        rows.Count(item => item.PublicAccess && item.Classification is "Restricted" or "Confidential"),
        rows.Count(item => !item.Encrypted),
        rows.Select(ToDataStoreDto).ToArray());

    private static DataStoreDto ToDataStoreDto(DataStoreAsset row) => new(
        row.Id,
        row.StoreName,
        row.Provider,
        row.Engine,
        row.Region,
        row.Classification,
        row.SensitiveDataTypes,
        row.RecordCountEstimate,
        row.Encrypted,
        row.PublicAccess,
        row.RetentionCompliant,
        row.ControlCode,
        row.RiskScore,
        DspmScorer.Level(row.RiskScore),
        row.OwnerEmail,
        row.LastScannedAt);

    private static CostRecommendationDto ToCostDto(CostRecommendation row) => new(
        row.Id,
        row.Provider,
        row.ResourceName,
        row.ResourceType,
        row.Region,
        row.Category,
        row.MonthlySavingsUsd,
        row.AnnualSavingsUsd,
        row.LinkedControlCode,
        row.ComplianceImpact,
        row.RemediationSummary,
        row.Status,
        row.DetectedAt,
        row.ActionedAt);

    private static string NormalizeClassification(string? value) => (value ?? "").Trim() switch
    {
        "Restricted" => "Restricted",
        "Confidential" => "Confidential",
        "Internal" => "Internal",
        "Public" => "Public",
        _ => "Confidential",
    };

    private static string EngineFor(string provider, string kind)
    {
        if (kind == "database")
        {
            return provider switch
            {
                "azure" => "Azure PostgreSQL",
                "gcp" => "Cloud SQL PostgreSQL",
                _ => "AWS RDS PostgreSQL",
            };
        }

        return provider switch
        {
            "azure" => "Azure Blob Storage",
            "gcp" => "Google Cloud Storage",
            _ => "AWS S3",
        };
    }

    private static string RegionFor(string provider) => provider switch
    {
        "azure" => "eastus",
        "gcp" => "us-central1",
        _ => "us-east-1",
    };
}
