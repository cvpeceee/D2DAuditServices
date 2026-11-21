using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Assurance;

public sealed record CustomTestCycleResult(CustomTestDefinition Definition, CustomTestRun Run, Control ControlItem, TestOutcome? Previous);

/// <summary>Shared inventory loading and the collection-cycle hooks for custom tests, suppressions, and library evidence.</summary>
public static class AssuranceCycle
{
    public static async Task<IReadOnlyList<NormalizedResource>> InventoryAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        var snapshots = await db.Resources.AsNoTracking().ToListAsync(cancellationToken);
        var computers = await db.Computers.AsNoTracking().ToListAsync(cancellationToken);
        var people = await db.People.AsNoTracking().ToListAsync(cancellationToken);
        var vulnerabilities = await db.Vulnerabilities.AsNoTracking().ToListAsync(cancellationToken);
        return
        [
            .. snapshots.Select(ProviderSet.ToResource),
            .. computers.Select(CustomTestEngine.FromComputer),
            .. people.Select(CustomTestEngine.FromPerson),
            .. vulnerabilities.Select(CustomTestEngine.FromVulnerability),
        ];
    }

    public static async Task<Dictionary<string, TestSuppression>> ActiveSuppressionsAsync(TenantDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await db.TestSuppressions.AsNoTracking().ToListAsync(cancellationToken);
        return rows
            .Where(item => TestSuppressionRules.IsActive(item.State, item.Until, today))
            .ToDictionary(item => item.TestKey, StringComparer.Ordinal);
    }

    public static CheckResult Suppress(CheckResult result, TestSuppression? suppression) =>
        suppression is null ? result : new CheckResult(TestOutcome.NotRun, TestSuppressionRules.Describe(suppression.State, suppression.Reason, suppression.Until));

    /// <summary>Writes one custom test outcome onto every mapped control and removes rows from controls no longer mapped.</summary>
    public static IReadOnlyList<Control> Apply(TenantDbContext db, IReadOnlyList<Control> controls, CustomTestDefinition definition, TestOutcome outcome, string detail)
    {
        var codes = CustomTestEngine.SplitCodes(definition.ControlCodes);
        var mapped = new List<Control>();
        foreach (var controlItem in controls)
        {
            var test = controlItem.Tests.FirstOrDefault(item => item.TestKey == definition.Key);
            if (!codes.Contains(controlItem.Code, StringComparer.Ordinal))
            {
                if (test is not null)
                {
                    controlItem.Tests.Remove(test);
                    db.ControlTests.Remove(test);
                }

                continue;
            }

            if (test is null)
            {
                test = new ControlTest { Id = Guid.NewGuid(), ControlId = controlItem.Id, TestKey = definition.Key };
                controlItem.Tests.Add(test);
                db.ControlTests.Add(test);
            }

            test.Name = definition.Name;
            test.Kind = TestKind.Automated;
            test.Outcome = outcome;
            test.Detail = detail.Length > 2000 ? detail[..2000] : detail;
            test.Remediation = definition.Remediation;
            test.Requirements = "Custom " + definition.Severity;
            mapped.Add(controlItem);
        }

        return mapped;
    }

    public static void Remove(TenantDbContext db, IEnumerable<Control> controls, string key)
    {
        var matches = controls.SelectMany(controlItem => controlItem.Tests.Where(item => item.TestKey == key).Select(test => (Control: controlItem, Test: test))).ToArray();
        foreach (var (controlItem, test) in matches)
        {
            controlItem.Tests.Remove(test);
            db.ControlTests.Remove(test);
        }
    }

    public static CustomTestRun Evaluate(CustomTestDefinition definition, IReadOnlyList<NormalizedResource> resources, TestSuppression? suppression, DateTimeOffset now)
    {
        if (!definition.Enabled)
        {
            return new CustomTestRun(TestOutcome.NotRun, "Disabled.", [], []);
        }

        return suppression is null
            ? CustomTestEngine.Run(definition, resources, now)
            : new CustomTestRun(TestOutcome.NotRun, TestSuppressionRules.Describe(suppression.State, suppression.Reason, suppression.Until), [], []);
    }

    /// <summary>Runs custom tests during collection and stores their inputs on the evidence chain.</summary>
    public static async Task<IReadOnlyList<CustomTestCycleResult>> RunCustomTestsAsync(
        TenantDbContext db,
        EvidenceWriter evidence,
        Guid tenantId,
        IReadOnlyList<Control> controls,
        IReadOnlyList<NormalizedResource> resources,
        IReadOnlyDictionary<string, TestSuppression> suppressions,
        DateTimeOffset now,
        DateTimeOffset lockedUntil,
        ISet<Guid> touched,
        CancellationToken cancellationToken)
    {
        var results = new List<CustomTestCycleResult>();
        var definitions = await db.CustomTests.AsNoTracking().OrderBy(item => item.Key).ToListAsync(cancellationToken);
        foreach (var definition in definitions)
        {
            var previous = controls.SelectMany(item => item.Tests).FirstOrDefault(item => item.TestKey == definition.Key)?.Outcome;
            var run = Evaluate(definition, resources, suppressions.GetValueOrDefault(definition.Key), now);
            var mapped = Apply(db, controls, definition, run.Outcome, run.Detail);
            if (run.Outcome == TestOutcome.NotRun)
            {
                continue;
            }

            var body = await EvidenceWriter.ParquetAsync([.. run.Failing, .. run.Passing], cancellationToken);
            foreach (var controlItem in mapped)
            {
                touched.Add(controlItem.Id);
                results.Add(new CustomTestCycleResult(definition, run, controlItem, previous));
                var evidenceId = Guid.NewGuid();
                var item = new EvidenceItem
                {
                    Id = evidenceId,
                    ControlId = controlItem.Id,
                    Title = definition.Name,
                    Source = "Automated (custom test)",
                    ObjectKey = tenantId.ToString("N") + "/" + evidenceId.ToString("N") + ".parquet",
                    Integrity = EvidenceIntegrity.NotStored,
                };
                db.Evidence.Add(item);
                await evidence.AppendAsync(db, tenantId, item, body, "application/vnd.apache.parquet", lockedUntil, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return results;
    }

    public static async Task<Dictionary<string, List<string>>> LibraryStatusesAsync(TenantDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var documents = await db.EvidenceDocuments.AsNoTracking().Include(item => item.Links).ToListAsync(cancellationToken);
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var status = EvidenceExpiry.Status(document.CurrentEvidenceId is not null, document.ValidUntil, today);
            foreach (var code in document.Links.Select(link => link.ControlCode))
            {
                if (!map.TryGetValue(code, out var list))
                {
                    list = [];
                    map[code] = list;
                }

                list.Add(status);
            }
        }

        return map;
    }

    public static async Task ApplyLibraryAsync(TenantDbContext db, IEnumerable<Control> controls, DateOnly today, CancellationToken cancellationToken)
    {
        var statuses = await LibraryStatusesAsync(db, today, cancellationToken);
        foreach (var controlItem in controls)
        {
            if (statuses.TryGetValue(controlItem.Code, out var list))
            {
                controlItem.Status = ControlReadiness.ApplyLibrary(controlItem.Status, list);
            }
        }
    }
}
