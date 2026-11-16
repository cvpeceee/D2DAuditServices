using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Changes;

public sealed class ChangePortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time,
    GitHubSourceControl github,
    IJiraChangeFeed jira) : IChanges
{
    private static readonly string[] Sources = ["github", "jira", "manual"];

    public async Task<IReadOnlyList<ChangeItem>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var rows = await db.Changes.AsNoTracking().OrderByDescending(item => item.OpenedAt).ToListAsync(cancellationToken);
        return rows.Select(ToItem).ToArray();
    }

    public async Task<Result<ChangeItem>> RecordAsync(RecordChangeRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<ChangeItem>.Fail("forbidden");
        }

        var title = (request.Title ?? "").Trim();
        var source = (request.Source ?? "").Trim().ToLowerInvariant();
        var key = (request.ExternalKey ?? "").Trim();
        var repository = (request.Repository ?? "").Trim();
        if (title.Length is 0 or > 200 || !Sources.Contains(source) || key.Length > 80 || repository.Length > 200)
        {
            return Result<ChangeItem>.Fail("invalid_change");
        }

        await using var db = Open();
        var now = time.GetUtcNow();
        var row = new ChangeRecord
        {
            Id = Guid.NewGuid(),
            Code = await NextCodeAsync(db, cancellationToken),
            Title = title,
            Source = source,
            ExternalKey = key,
            Repository = repository,
            AuthorName = tenant.DisplayName,
            Status = "Open",
            OpenedAt = now,
        };
        db.Changes.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshTestAsync(db, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "change.recorded", "ok", row.Code, null, cancellationToken);
        return Result<ChangeItem>.Ok(ToItem(row));
    }

    public async Task<Result<ChangeItem>> UpdateAsync(Guid id, UpdateChangeRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<ChangeItem>.Fail("forbidden");
        }

        var action = (request.Action ?? "").Trim().ToLowerInvariant();
        var deployRef = (request.DeployRef ?? "").Trim();
        if (action is not ("approve" or "deploy" or "reject") || deployRef.Length > 80)
        {
            return Result<ChangeItem>.Fail("invalid_change");
        }

        if (action == "deploy" && deployRef.Length == 0)
        {
            return Result<ChangeItem>.Fail("invalid_change");
        }

        await using var db = Open();
        var row = await db.Changes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return Result<ChangeItem>.Fail("not_found");
        }

        var now = time.GetUtcNow();
        if (action == "approve" && row.ApprovedAt is null)
        {
            row.ApprovedAt = now;
            row.ApproverName = tenant.DisplayName;
        }

        if (action == "deploy")
        {
            row.DeployRef = deployRef;
            row.DeployedAt ??= now;
        }

        if (action == "reject")
        {
            row.Status = "Rejected";
        }
        else
        {
            row.Status = StatusOf(row);
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshTestAsync(db, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "change.updated", "ok", row.Code, null, cancellationToken);
        return Result<ChangeItem>.Ok(ToItem(row));
    }

    public async Task<Result<ChangeSyncResult>> SyncAsync(CancellationToken cancellationToken)
    {
        if (!tenant.CanWrite)
        {
            return Result<ChangeSyncResult>.Fail("forbidden");
        }

        await using var db = Open();
        var incoming = new List<ExternalChange>();
        var githubRow = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(item => item.Provider == "github", cancellationToken);
        if (githubRow is { Example: false, Status: "connected" })
        {
            var connection = new ProviderConnection(
                githubRow.Example,
                githubRow.Fixture,
                githubRow.RoleArn,
                githubRow.ExternalId,
                githubRow.InstallationId,
                githubRow.Domain,
                null);
            incoming.AddRange(await github.MergedPullRequestsAsync(connection, cancellationToken));
        }

        incoming.AddRange(await jira.ListAsync(cancellationToken));
        var imported = 0;
        foreach (var item in incoming)
        {
            if (await UpsertAsync(db, item, cancellationToken))
            {
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshTestAsync(db, cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "change.synced", "ok", imported.ToString(), null, cancellationToken);
        return Result<ChangeSyncResult>.Ok(new ChangeSyncResult(imported));
    }

    private async Task<bool> UpsertAsync(TenantDbContext db, ExternalChange item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.ExternalKey))
        {
            return false;
        }

        var row = await db.Changes.FirstOrDefaultAsync(
            change => change.Source == item.Source && change.ExternalKey == item.ExternalKey,
            cancellationToken);
        var created = row is null;
        if (row is null)
        {
            row = new ChangeRecord
            {
                Id = Guid.NewGuid(),
                Code = await NextCodeAsync(db, cancellationToken),
                Source = item.Source,
                ExternalKey = item.ExternalKey,
                OpenedAt = item.DeployedAt ?? time.GetUtcNow(),
                AuthorName = item.AuthorName,
            };
            db.Changes.Add(row);
        }

        row.Title = Trim(item.Title, 200);
        row.Repository = Trim(item.Repository, 200);
        if (item.AuthorName.Length > 0)
        {
            row.AuthorName = Trim(item.AuthorName, 200);
        }

        if (item.Approved && row.ApprovedAt is null)
        {
            row.ApprovedAt = item.DeployedAt ?? time.GetUtcNow();
            row.ApproverName = Trim(item.ApproverName, 200);
        }

        if (item.DeployedAt is DateTimeOffset deployed)
        {
            row.DeployedAt = deployed;
            row.DeployRef = Trim(item.DeployRef, 80);
        }

        if (row.Status != "Rejected")
        {
            row.Status = StatusOf(row);
        }

        return created;
    }

    private async Task RefreshTestAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Changes.AsNoTracking().ToListAsync(cancellationToken);
        var result = ChangeRules.Evaluate(rows.Select(item => (item.Code, item.ApprovedAt, item.DeployedAt)));
        var controlItem = await db.Controls.Include(item => item.Tests).FirstOrDefaultAsync(item => item.Code == ChangeRules.ControlCode, cancellationToken);
        if (controlItem is null)
        {
            return;
        }

        var test = controlItem.Tests.FirstOrDefault(item => item.Name == ChangeRules.TestName);
        if (test is null)
        {
            test = new ControlTest
            {
                Id = Guid.NewGuid(),
                ControlId = controlItem.Id,
                Name = ChangeRules.TestName,
                Kind = TestKind.Document,
            };
            db.ControlTests.Add(test);
        }

        test.Outcome = result.Outcome;
        test.Detail = result.Detail;
        if (result.Outcome == TestOutcome.Fail)
        {
            controlItem.Status = ControlStatus.Failing;
        }
        else if (controlItem.Tests.All(item => item.Id == test.Id || item.Outcome != TestOutcome.Fail))
        {
            controlItem.Status = ControlStatus.Passing;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string StatusOf(ChangeRecord row)
    {
        if (row.DeployedAt is not null)
        {
            return "Deployed";
        }

        if (row.ApprovedAt is not null)
        {
            return "Approved";
        }

        return "Open";
    }

    private static string Trim(string value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static async Task<string> NextCodeAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var codes = await db.Changes.Select(item => item.Code).ToListAsync(cancellationToken);
        var local = db.Changes.Local.Select(item => item.Code);
        var number = codes.Concat(local).Select(Parse).DefaultIfEmpty(0).Max() + 1;
        return "CHG-" + number.ToString("00");
    }

    private static int Parse(string code) =>
        code.StartsWith("CHG-", StringComparison.Ordinal) && int.TryParse(code[4..], out var value) ? value : 0;

    private static ChangeItem ToItem(ChangeRecord row) => new(
        row.Id,
        row.Code,
        row.Title,
        row.Source,
        row.ExternalKey,
        row.Repository,
        row.AuthorName,
        row.Status,
        row.ApproverName,
        row.ApprovedAt,
        row.DeployRef,
        row.DeployedAt,
        row.OpenedAt,
        row.ApprovedAt is not null);

    private TenantDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            throw new InvalidOperationException("Tenant database is not resolved.");
        }

        return databases.Create(tenant.DatabaseName);
    }
}
