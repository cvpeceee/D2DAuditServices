using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace D2DAudit.Tests;

public sealed class SchemaParityTests
{
    [Fact]
    public void ControlPlane_and_Tenant_migrations_have_no_pending_model_changes()
    {
        using var control = new ControlPlaneDesignTimeFactory().CreateDbContext([]);
        Assert.False(control.Database.HasPendingModelChanges(), "ControlPlaneDbContext has pending model changes without a migration.");

        using var tenant = new TenantDesignTimeFactory().CreateDbContext([]);
        Assert.False(tenant.Database.HasPendingModelChanges(), "TenantDbContext has pending model changes without a migration.");

        var controlMigrations = control.GetService<IMigrationsAssembly>().Migrations;
        var tenantMigrations = tenant.GetService<IMigrationsAssembly>().Migrations;
        Assert.NotEmpty(controlMigrations);
        Assert.NotEmpty(tenantMigrations);
    }

    [Fact]
    public void Auditor_export_package_keys_are_isolated_from_admin_packages()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var auditorId = Guid.NewGuid();
        var adminKey = AuditPortal.PackageKey(tenantId, jobId, null);
        var auditorKey = AuditPortal.PackageKey(tenantId, jobId, auditorId);

        Assert.NotEqual(adminKey, auditorKey);
        Assert.Contains("/exports/auditor-", auditorKey, StringComparison.Ordinal);
        Assert.DoesNotContain("/exports/auditor-", adminKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GroundedQuestionnaireAssistant_uses_llm_provider_when_active_and_preserves_verified_citation()
    {
        var knowledge = new (string Citation, string Text)[]
        {
            ("Control CM-11 Change management", "Every production change requires peer review approval before deployment."),
        };

        var llmAssistant = new GroundedQuestionnaireAssistant(new StubAssistantProvider("llm", "All production changes require peer review approval prior to deployment."));
        var llmResult = await llmAssistant.SuggestAsync("Are production changes reviewed before deploy?", knowledge, "Harborline", CancellationToken.None);

        Assert.Contains("peer review approval", llmResult.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Control CM-11", llmResult.Answer, StringComparison.Ordinal);
        Assert.Contains("Control CM-11", llmResult.Citation, StringComparison.Ordinal);

        var fallbackAssistant = new GroundedQuestionnaireAssistant(new StubAssistantProvider("grounded-retrieval", "ignored"));
        var fallbackResult = await fallbackAssistant.SuggestAsync("Are production changes reviewed before deploy?", knowledge, "Harborline", CancellationToken.None);

        Assert.StartsWith("From Control CM-11", fallbackResult.Answer, StringComparison.Ordinal);
        Assert.Contains("Control CM-11", fallbackResult.Citation, StringComparison.Ordinal);
    }

    private sealed class StubAssistantProvider(string mode, string answer) : IAssistantProvider
    {
        public Task<AssistantReply> AnswerAsync(string question, IReadOnlyList<AssistantFact> facts, string scopeLabel, CancellationToken cancellationToken) =>
            Task.FromResult(new AssistantReply(
                answer,
                mode,
                facts.Take(1).Select(f => new AssistantCitation(f.Kind, f.Code, f.Title, f.Href)).ToArray(),
                []));
    }
}
