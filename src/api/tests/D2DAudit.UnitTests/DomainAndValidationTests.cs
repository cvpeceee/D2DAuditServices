using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class DomainAndValidationTests
{
    [Fact]
    public void Database_names_reject_anything_that_is_not_a_safe_identifier()
    {
        var name = DatabaseNames.ForTenant(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));
        Assert.Equal("t_0123456789abcdef0123456789abcdef", name);
        Assert.True(DatabaseNames.IsSafe(name));
        Assert.False(DatabaseNames.IsSafe("t_ok;drop database postgres"));
        Assert.False(DatabaseNames.IsSafe("T_UPPER"));
    }

    [Fact]
    public void Retention_can_only_grow()
    {
        var tenant = new Tenant { RetentionYears = 3 };
        Assert.True(tenant.SetRetentionYears(6).Succeeded);
        Assert.Equal(6, tenant.RetentionYears);
        Assert.Equal("cannot_shrink", tenant.SetRetentionYears(4).Error);
        Assert.Equal("below_minimum", tenant.SetRetentionYears(2).Error);
        Assert.Equal(6, tenant.RetentionYears);
    }

    [Theory]
    [InlineData("Production", null, null)]
    [InlineData("Staging", "", "")]
    [InlineData("Development", "sk_test", null)]
    public void Local_identity_is_rejected_when_the_process_would_be_unsafe(string environment, string? apiKey, string? clientId)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => IdentityModeResolver.Resolve(environment, apiKey, clientId));
        Assert.Contains("WORKOS", exception.Message);
    }

    [Fact]
    public void WorkOS_is_selected_only_when_both_credentials_exist()
    {
        Assert.Equal(IdentityMode.WorkOs, IdentityModeResolver.Resolve("Production", "sk_test", "client_test"));
        Assert.Equal(IdentityMode.Local, IdentityModeResolver.Resolve("Development", null, null));
        Assert.Equal(IdentityMode.Local, IdentityModeResolver.Resolve("Testing", null, null));
    }

    [Fact]
    public void Tenant_requests_reject_a_bad_slug_or_region()
    {
        var validator = new CreateTenantValidator();
        Assert.False(validator.Validate(new CreateTenantRequest("Acme", "Acme", "US", false)).IsValid);
        Assert.False(validator.Validate(new CreateTenantRequest("acme", "Acme", "APAC", false)).IsValid);
        Assert.True(validator.Validate(new CreateTenantRequest("acme", "Acme", "EU", false)).IsValid);
    }

    [Fact]
    public void Owner_email_must_be_empty_or_an_address()
    {
        var validator = new AssignOwnerValidator();
        Assert.True(validator.Validate(new AssignOwnerRequest("Priya", null)).IsValid);
        Assert.True(validator.Validate(new AssignOwnerRequest("Priya", "priya@acme.example")).IsValid);
        Assert.False(validator.Validate(new AssignOwnerRequest("Priya", "not-an-email")).IsValid);
        Assert.False(validator.Validate(new AssignOwnerRequest(new string('a', 201), null)).IsValid);
    }
}
