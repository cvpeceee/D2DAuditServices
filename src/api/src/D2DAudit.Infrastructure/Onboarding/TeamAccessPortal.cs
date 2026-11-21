using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Onboarding;

public sealed class TeamAccessPortal(
    TenantContext tenant,
    ControlPlaneDbContext control,
    OnboardingIdentity identity,
    IFeatureGate gate,
    INotifier notifier,
    IActivityWriter activity,
    AppLinks links,
    TimeProvider time,
    ILogger<TeamAccessPortal> logger) : ITeamAccess
{
    public async Task<IReadOnlyList<InviteDto>> InvitesAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var rows = await control.MemberInvites.AsNoTracking().Where(item => item.TenantId == tenant.TenantId).ToListAsync(cancellationToken);
        return rows.OrderByDescending(item => item.CreatedAt).Select(item => ToDto(item, now)).ToArray();
    }

    public async Task<Result<CreatedInviteDto>> CreateInviteAsync(CreateInviteRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<CreatedInviteDto>.Fail("forbidden");
        }

        var email = SignupRules.NormalizeEmail(request.Email);
        if (!Enum.TryParse<TenantRole>(request.Role, out var role) || !Enum.IsDefined(role) || email.Length is < 3 or > 320 || !email.Contains('@') || SignupRules.DomainOf(email).Length == 0)
        {
            return Result<CreatedInviteDto>.Fail("invalid_member");
        }

        var existing = await control.Memberships.AnyAsync(
            item => item.TenantId == tenant.TenantId && item.Active && item.User != null && EF.Functions.ILike(item.User.Email, email),
            cancellationToken);
        if (existing)
        {
            return Result<CreatedInviteDto>.Fail("already_member");
        }

        var now = time.GetUtcNow();
        var open = await control.MemberInvites
            .Where(item => item.TenantId == tenant.TenantId && item.Email == email && item.AcceptedAt == null && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var previous in open)
        {
            previous.RevokedAt = now;
        }

        var seats = await gate.CheckAsync(tenant.TenantId, PlanFeatures.Seats, cancellationToken);
        if (!seats.Allowed && role != TenantRole.Auditor)
        {
            await activity.WriteAsync(tenant.UserId, tenant.TenantId, "plan.limit_exceeded", "warning", $"seats {seats.Used}/{seats.Limit}", null, cancellationToken);
        }

        var token = OneTimeTokens.Create();
        var invite = new MemberInvite
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Email = email,
            Role = role,
            TokenHash = OneTimeTokens.Hash(token),
            InvitedByUserId = tenant.UserId,
            InvitedByName = tenant.DisplayName,
            CreatedAt = now,
            ExpiresAt = now.Add(OneTimeTokens.InviteLifetime),
        };
        control.MemberInvites.Add(invite);
        await control.SaveChangesAsync(cancellationToken);

        var url = links.Link("/invite/accept", token);
        var delivery = await notifier.SendAsync(
            "email",
            email,
            $"{tenant.DisplayName} invited you to {tenant.TenantName} on D2D Audit",
            $"You were invited as {role}. Accept within 7 days: {url}",
            cancellationToken);
        string? devUrl = null;
        if (links.ShowDevLinks && AppLinks.IsMockDelivery(delivery))
        {
            devUrl = url;
            logger.LogInformation("Mock email: invite link for {Email} is {Url}", email, url);
        }

        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "member.invite_sent", "ok", role.ToString(), null, cancellationToken);
        return Result<CreatedInviteDto>.Ok(new CreatedInviteDto(ToDto(invite, now), devUrl, seats));
    }

    public async Task<Result<InviteDto>> RevokeInviteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<InviteDto>.Fail("forbidden");
        }

        var invite = await control.MemberInvites.FirstOrDefaultAsync(item => item.Id == id && item.TenantId == tenant.TenantId, cancellationToken);
        if (invite is null)
        {
            return Result<InviteDto>.Fail("not_found");
        }

        var now = time.GetUtcNow();
        if (invite.State(now) == "Pending")
        {
            invite.RevokedAt = now;
            await control.SaveChangesAsync(cancellationToken);
            await activity.WriteAsync(tenant.UserId, tenant.TenantId, "member.invite_revoked", "ok", invite.Role.ToString(), null, cancellationToken);
        }

        return Result<InviteDto>.Ok(ToDto(invite, now));
    }

    public async Task<SecuritySettingsDto> SecurityAsync(CancellationToken cancellationToken)
    {
        var settings = await control.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(item => item.TenantId == tenant.TenantId, cancellationToken)
            ?? new TenantSecuritySettings { TenantId = tenant.TenantId };
        return await ToDtoAsync(settings, cancellationToken);
    }

    public async Task<Result<SecuritySettingsDto>> SaveSecurityAsync(SaveSecuritySettingsRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<SecuritySettingsDto>.Fail("forbidden");
        }

        var settings = await LoadAsync(cancellationToken);
        settings.SsoEnforced = request.SsoEnforced;
        settings.UpdatedAt = time.GetUtcNow();
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.sso_enforced", "ok", request.SsoEnforced ? "on" : "off", null, cancellationToken);
        return Result<SecuritySettingsDto>.Ok(await ToDtoAsync(settings, cancellationToken));
    }

    public async Task<ScimTokenDto> RotateScimTokenAsync(CancellationToken cancellationToken)
    {
        var settings = await LoadAsync(cancellationToken);
        var token = "scim_" + OneTimeTokens.Create();
        var now = time.GetUtcNow();
        settings.ScimTokenHash = OneTimeTokens.Hash(token);
        settings.ScimTokenPrefix = token[..12];
        settings.ScimTokenCreatedAt = now;
        settings.UpdatedAt = now;
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "security.scim_token_rotated", "ok", settings.ScimTokenPrefix, null, cancellationToken);
        return new ScimTokenDto(token, settings.ScimTokenPrefix, now);
    }

    private async Task<TenantSecuritySettings> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await control.SecuritySettings.FirstOrDefaultAsync(item => item.TenantId == tenant.TenantId, cancellationToken);
        if (settings is null)
        {
            settings = new TenantSecuritySettings { TenantId = tenant.TenantId, UpdatedAt = time.GetUtcNow() };
            control.SecuritySettings.Add(settings);
        }

        return settings;
    }

    private async Task<SecuritySettingsDto> ToDtoAsync(TenantSecuritySettings settings, CancellationToken cancellationToken)
    {
        var domain = await control.TenantDomains.AsNoTracking().Where(item => item.TenantId == tenant.TenantId).OrderBy(item => item.Domain).FirstOrDefaultAsync(cancellationToken);
        return new SecuritySettingsDto(
            settings.SsoEnforced,
            identity.Mode == IdentityMode.WorkOs ? "workos" : "mock",
            domain?.Domain ?? SignupRules.DomainOf(tenant.Email),
            !string.IsNullOrWhiteSpace(domain?.WorkOsOrganizationId),
            settings.ScimTokenPrefix,
            settings.ScimTokenCreatedAt,
            links.BaseUrl + "/api/v1/scim/v2");
    }

    internal static InviteDto ToDto(MemberInvite invite, DateTimeOffset now) =>
        new(invite.Id, invite.Email, invite.Role.ToString(), invite.State(now), invite.InvitedByName, invite.CreatedAt, invite.ExpiresAt);
}

public sealed class InviteAcceptance(
    ControlPlaneDbContext control,
    OnboardingIdentity identity,
    PasswordHasher<UserAccount> passwords,
    IActivityWriter activity,
    TimeProvider time) : IInviteAcceptance
{
    public async Task<Result<InvitePreviewDto>> PreviewAsync(string token, CancellationToken cancellationToken)
    {
        var invite = await FindAsync(token, cancellationToken);
        if (invite is null)
        {
            return Result<InvitePreviewDto>.Fail("invalid_token");
        }

        var tenantName = await control.Tenants.AsNoTracking().Where(item => item.Id == invite.TenantId).Select(item => item.Name).FirstOrDefaultAsync(cancellationToken) ?? "";
        var hasPassword = await control.Users.AnyAsync(item => EF.Functions.ILike(item.Email, invite.Email) && item.PasswordHash != null, cancellationToken);
        return Result<InvitePreviewDto>.Ok(new InvitePreviewDto(
            tenantName,
            invite.Email,
            invite.Role.ToString(),
            invite.State(time.GetUtcNow()),
            identity.PasswordRequired && !hasPassword,
            invite.ExpiresAt));
    }

    public async Task<Result<AcceptInviteResult>> AcceptAsync(AcceptInviteRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var invite = await FindAsync(request.Token, cancellationToken);
        if (invite is null)
        {
            await activity.WriteAsync(null, null, "member.invite_accept", "denied", "invalid_token", ipAddress, cancellationToken);
            return Result<AcceptInviteResult>.Fail("invalid_token");
        }

        var now = time.GetUtcNow();
        switch (invite.State(now))
        {
            case "Expired":
                return Result<AcceptInviteResult>.Fail("token_expired");
            case "Revoked":
                return Result<AcceptInviteResult>.Fail("invite_revoked");
            case "Accepted":
                return Result<AcceptInviteResult>.Fail("invite_used");
        }

        var user = await control.Users.FirstOrDefaultAsync(item => EF.Functions.ILike(item.Email, invite.Email), cancellationToken);
        var needsPassword = identity.PasswordRequired && (user is null || user.PasswordHash is null);
        if (needsPassword && !SignupRules.StrongPassword(request.Password))
        {
            return Result<AcceptInviteResult>.Fail("weak_password");
        }

        var name = request.DisplayName?.Trim();
        if (user is null)
        {
            user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Subject = "local:" + Guid.NewGuid().ToString("N"),
                Email = invite.Email,
                DisplayName = string.IsNullOrWhiteSpace(name) ? SignupRules.DisplayNameFor(invite.Email) : name[..Math.Min(200, name.Length)],
                CreatedAt = now,
            };
            control.Users.Add(user);
        }

        if (needsPassword)
        {
            user.PasswordHash = passwords.HashPassword(user, request.Password!);
        }

        var membership = await control.Memberships.FirstOrDefaultAsync(item => item.UserId == user.Id && item.TenantId == invite.TenantId, cancellationToken);
        if (membership is null)
        {
            membership = new Membership { Id = Guid.NewGuid(), UserId = user.Id, TenantId = invite.TenantId };
            control.Memberships.Add(membership);
        }

        membership.Role = invite.Role;
        membership.Active = true;
        invite.AcceptedAt = now;
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(user.Id, invite.TenantId, "member.invite_accepted", "ok", invite.Role.ToString(), ipAddress, cancellationToken);
        return Result<AcceptInviteResult>.Ok(new AcceptInviteResult(invite.TenantId, invite.Email, identity.PasswordRequired ? "login" : "sso"));
    }

    private async Task<MemberInvite?> FindAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
        {
            return null;
        }

        var hash = OneTimeTokens.Hash(token.Trim());
        return await control.MemberInvites.FirstOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
    }
}
