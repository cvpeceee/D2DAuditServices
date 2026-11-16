using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Operations;

public sealed class FakeIssueTracker : IIssueTracker
{
    public Task<string> CreateIssueAsync(string target, string summary, string detail, CancellationToken cancellationToken)
    {
        var project = string.IsNullOrWhiteSpace(target) ? "SEC" : target.Trim();
        return Task.FromResult("DEMO-" + project + "-" + Guid.NewGuid().ToString("N")[..8]);
    }
}

public sealed class FakeNotifier : INotifier
{
    public Task<string> SendAsync(string channel, string target, string summary, string detail, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(channel) ? "notice" : channel.Trim().ToLowerInvariant();
        return Task.FromResult(name + ":" + Guid.NewGuid().ToString("N")[..8]);
    }
}

public sealed class AutomationDispatcher(
    TenantContext tenant,
    TenantDatabases databases,
    IIssueTracker issues,
    INotifier notifier,
    TimeProvider time) : IAutomationDispatcher
{
    public async Task DispatchAsync(string trigger, string summary, string detail, CancellationToken cancellationToken)
    {
        if (tenant.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            return;
        }

        await using var db = databases.Create(tenant.DatabaseName);
        var rules = await db.AutomationRules.Where(rule => rule.Enabled && rule.Trigger == trigger).ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        foreach (var rule in rules)
        {
            var external = rule.Action == "Jira"
                ? await issues.CreateIssueAsync(rule.Target, summary, detail, cancellationToken)
                : await notifier.SendAsync(rule.Action, rule.Target, summary, detail, cancellationToken);
            db.Set<AutomationDelivery>().Add(new AutomationDelivery
            {
                Id = Guid.NewGuid(),
                RuleId = rule.Id,
                Trigger = trigger,
                Action = rule.Action,
                Target = rule.Target,
                Summary = summary,
                ExternalId = external,
                At = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
