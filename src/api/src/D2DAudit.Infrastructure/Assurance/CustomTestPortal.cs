using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Assurance;

public sealed class CustomTestPortal(
    TenantContext tenant,
    TenantDatabases databases,
    IActivityWriter activity,
    TimeProvider time) : ICustomTestPortal
{
    private const int PreviewLimit = 50;

    public async Task<IReadOnlyList<CustomTestDto>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var definitions = await db.CustomTests.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var controls = await AssuranceScope.ControlsAsync(db, cancellationToken);
        var tests = controls.Values.SelectMany(item => item.Tests).Where(item => item.TestKey != null).ToArray();
        return definitions
            .Where(item => !tenant.IsAuditor || CustomTestEngine.SplitCodes(item.ControlCodes).Any(code => controls.TryGetValue(code, out var control) && AssuranceScope.InScope(tenant, control)))
            .Select(item => ToDto(item, tests.FirstOrDefault(test => test.TestKey == item.Key)))
            .ToArray();
    }

    public async Task<IReadOnlyList<ResourceTypeDto>> ResourceTypesAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var resources = await AssuranceCycle.InventoryAsync(db, cancellationToken);
        return CustomTestEngine.Describe(resources)
            .Select(item => new ResourceTypeDto(item.Provider, item.Kind, item.Count, item.Fields))
            .ToArray();
    }

    public async Task<Result<CustomTestPreview>> PreviewAsync(PreviewCustomTestRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ResourceKind))
        {
            return Result<CustomTestPreview>.Fail("invalid_resource_kind");
        }

        var filter = ConditionEvaluator.Parse(Raw(request.Filter), allowEmpty: true);
        var assertion = ConditionEvaluator.Parse(Raw(request.Assertion), allowEmpty: false);
        var error = filter.Error ?? assertion.Error;
        if (error is not null || assertion.Node is null)
        {
            return Result<CustomTestPreview>.Fail(error ?? "condition_required");
        }

        await using var db = Open();
        var resources = await AssuranceCycle.InventoryAsync(db, cancellationToken);
        var run = CustomTestEngine.Run(request.ResourceKind.Trim(), Clean(request.Provider), filter.Node, assertion.Node, resources, time.GetUtcNow());
        return Result<CustomTestPreview>.Ok(new CustomTestPreview(
            Api(run.Outcome),
            run.Detail,
            run.Passing.Count + run.Failing.Count,
            run.Passing.Take(PreviewLimit).Select(ToPreview).ToArray(),
            run.Failing.Take(PreviewLimit).Select(ToPreview).ToArray()));
    }

    public async Task<Result<CustomTestDto>> CreateAsync(SaveCustomTestRequest request, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var definition = new CustomTestDefinition { Id = Guid.NewGuid(), CreatedByName = Actor(), CreatedAt = time.GetUtcNow() };
        var error = await FillAsync(db, definition, request, cancellationToken);
        if (error is not null)
        {
            return Result<CustomTestDto>.Fail(error);
        }

        var keys = await db.CustomTests.Select(item => item.Key).ToListAsync(cancellationToken);
        definition.Key = CustomTestEngine.KeyFor(definition.Name, keys.Concat(AutomatedChecks.All.Select(item => item.Key)));
        db.CustomTests.Add(definition);
        var test = await RefreshAsync(db, definition, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("test.custom.created", definition.Key, cancellationToken);
        return Result<CustomTestDto>.Ok(ToDto(definition, test));
    }

    public async Task<Result<CustomTestDto>> UpdateAsync(Guid id, SaveCustomTestRequest request, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var definition = await db.CustomTests.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (definition is null)
        {
            return Result<CustomTestDto>.Fail("not_found");
        }

        var error = await FillAsync(db, definition, request, cancellationToken);
        if (error is not null)
        {
            return Result<CustomTestDto>.Fail(error);
        }

        var test = await RefreshAsync(db, definition, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("test.custom.updated", definition.Key, cancellationToken);
        return Result<CustomTestDto>.Ok(ToDto(definition, test));
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var definition = await db.CustomTests.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (definition is null)
        {
            return Result<bool>.Fail("not_found");
        }

        var controls = await db.Controls.Include(item => item.Tests).ToListAsync(cancellationToken);
        AssuranceCycle.Remove(db, controls, definition.Key);
        db.CustomTests.Remove(definition);
        var suppression = await db.TestSuppressions.FirstOrDefaultAsync(item => item.TestKey == definition.Key, cancellationToken);
        if (suppression is not null)
        {
            db.TestSuppressions.Remove(suppression);
        }

        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync("test.custom.deleted", definition.Key, cancellationToken);
        return Result<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<TestStateDto>> StatesAsync(CancellationToken cancellationToken)
    {
        await using var db = Open();
        var today = Today();
        var rows = await db.TestSuppressions.AsNoTracking().OrderBy(item => item.TestKey).ToListAsync(cancellationToken);
        return rows.Select(item => ToState(item, today)).ToArray();
    }

    public async Task<Result<TestStateDto?>> SetStateAsync(string testKey, SetTestStateRequest request, CancellationToken cancellationToken)
    {
        await using var db = Open();
        var key = (testKey ?? "").Trim();
        var known = AutomatedChecks.All.Any(item => item.Key == key) || await db.CustomTests.AnyAsync(item => item.Key == key, cancellationToken);
        if (!known)
        {
            return Result<TestStateDto?>.Fail("not_found");
        }

        var today = Today();
        var row = await db.TestSuppressions.FirstOrDefaultAsync(item => item.TestKey == key, cancellationToken);
        if (string.Equals(request.State, "Enabled", StringComparison.Ordinal))
        {
            if (row is not null)
            {
                db.TestSuppressions.Remove(row);
                await db.SaveChangesAsync(cancellationToken);
            }

            await WriteAsync("test.enabled", key, cancellationToken);
            return Result<TestStateDto?>.Ok(null);
        }

        var rule = TestSuppressionRules.Validate(request.State, request.Reason, request.Until, today);
        if (!rule.Succeeded)
        {
            return Result<TestStateDto?>.Fail(rule.Error!);
        }

        if (row is null)
        {
            row = new TestSuppression { Id = Guid.NewGuid(), TestKey = key };
            db.TestSuppressions.Add(row);
        }

        row.State = request.State;
        row.Reason = request.Reason!.Trim();
        row.Until = request.State == TestSuppressionRules.Snoozed ? request.Until : null;
        row.ByName = Actor();
        row.At = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        var action = row.State == TestSuppressionRules.Snoozed ? "test.snoozed" : "test.disabled";
        await WriteAsync(action, $"{key}: {row.Reason}", cancellationToken);
        return Result<TestStateDto?>.Ok(ToState(row, today));
    }

    private async Task<string?> FillAsync(TenantDbContext db, CustomTestDefinition definition, SaveCustomTestRequest request, CancellationToken cancellationToken)
    {
        var codes = (request.ControlCodes ?? []).Select(code => (code ?? "").Trim()).Where(code => code.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var provider = Clean(request.Provider);
        var rule = CustomTestEngine.ValidateDefinition(request.Name, request.ResourceKind, provider, request.Severity, codes, request.Remediation);
        if (!rule.Succeeded)
        {
            return rule.Error;
        }

        if ((request.Description ?? "").Length > 2000)
        {
            return "invalid_description";
        }

        var filterJson = Raw(request.Filter);
        var assertionJson = Raw(request.Assertion);
        var error = ConditionEvaluator.Parse(filterJson, allowEmpty: true).Error ?? ConditionEvaluator.Parse(assertionJson, allowEmpty: false).Error;
        if (error is not null)
        {
            return error;
        }

        var existing = await db.Controls.Where(item => codes.Contains(item.Code)).Select(item => item.Code).ToListAsync(cancellationToken);
        if (existing.Count != codes.Length)
        {
            return "invalid_controls";
        }

        definition.Name = request.Name.Trim();
        definition.Description = (request.Description ?? "").Trim();
        definition.ResourceKind = request.ResourceKind.Trim();
        definition.Provider = provider;
        definition.FilterJson = filterJson;
        definition.AssertionJson = assertionJson!;
        definition.Severity = request.Severity;
        definition.ControlCodes = string.Join(",", codes);
        definition.Remediation = (request.Remediation ?? "").Trim();
        definition.Enabled = request.Enabled;
        definition.UpdatedAt = time.GetUtcNow();
        return null;
    }

    private async Task<ControlTest?> RefreshAsync(TenantDbContext db, CustomTestDefinition definition, CancellationToken cancellationToken)
    {
        var controls = await db.Controls.Include(item => item.Tests).ToListAsync(cancellationToken);
        var resources = await AssuranceCycle.InventoryAsync(db, cancellationToken);
        var suppression = await db.TestSuppressions.AsNoTracking().FirstOrDefaultAsync(item => item.TestKey == definition.Key, cancellationToken);
        var active = suppression is not null && TestSuppressionRules.IsActive(suppression.State, suppression.Until, Today()) ? suppression : null;
        var run = AssuranceCycle.Evaluate(definition, resources, active, time.GetUtcNow());
        var mapped = AssuranceCycle.Apply(db, controls, definition, run.Outcome, run.Detail);
        foreach (var controlItem in controls)
        {
            controlItem.Status = CollectionRunner.Recompute(controlItem);
        }

        await AssuranceCycle.ApplyLibraryAsync(db, controls, Today(), cancellationToken);
        return mapped.SelectMany(item => item.Tests).FirstOrDefault(item => item.TestKey == definition.Key);
    }

    private static CustomTestDto ToDto(CustomTestDefinition item, ControlTest? test) => new(
        item.Id,
        item.Key,
        item.Name,
        item.Description,
        item.ResourceKind,
        item.Provider,
        string.IsNullOrWhiteSpace(item.FilterJson) ? null : JsonSerializer.Deserialize<JsonElement>(item.FilterJson),
        JsonSerializer.Deserialize<JsonElement>(item.AssertionJson),
        item.Severity,
        CustomTestEngine.SplitCodes(item.ControlCodes),
        item.Remediation,
        item.Enabled,
        Api(test?.Outcome ?? TestOutcome.NotRun),
        test?.Detail ?? "",
        item.CreatedByName,
        item.UpdatedAt);

    private static TestStateDto ToState(TestSuppression item, DateOnly today) =>
        new(item.TestKey, item.State, item.Reason, item.Until, item.ByName, item.At, TestSuppressionRules.IsActive(item.State, item.Until, today));

    private static PreviewResource ToPreview(NormalizedResource item) => new(item.Provider, item.Kind, item.Name, item.ExternalId);

    private static string? Raw(JsonElement? element) =>
        element is null || element.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : element.Value.GetRawText();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Api(TestOutcome outcome)
    {
        var name = outcome.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private string Actor() => string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Email : tenant.DisplayName;

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private TenantDbContext Open() => databases.Create(AssuranceScope.Database(tenant));

    private Task WriteAsync(string action, string detail, CancellationToken cancellationToken) =>
        activity.WriteAsync(tenant.UserId, tenant.TenantId, action, "ok", detail, null, cancellationToken);
}
