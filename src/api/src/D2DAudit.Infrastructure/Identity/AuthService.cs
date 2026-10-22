using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Identity;

public sealed class AuthService(
    IIdentityProvider identity,
    ControlPlaneDbContext db,
    IActivityWriter activity,
    TimeProvider time,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<AuthSession?> CurrentAsync(string subject, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(account => account.Subject == subject, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var tenants = await MembershipsAsync(user.Id, cancellationToken);
        return new AuthSession(true, null, new UserSummary(user.Id, user.Email, user.DisplayName), tenants, false, null, null, null, null, null, null);
    }

    public AuthModeResponse Mode() => new(
        identity.Mode == IdentityMode.Local ? "local" : "workos",
        identity.Mode == IdentityMode.Local,
        identity.Mode == IdentityMode.Local ? DemoLocalPassword.Emails : []);

    public async Task<AuthSession> PasswordLoginAsync(
        string email,
        string password,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var result = await identity.PasswordLoginAsync(email, password, ip, userAgent, cancellationToken);
        return await FinishAsync(result, ip, Sanitize(email), cancellationToken);
    }

    public async Task<AuthSession> CompleteMfaAsync(
        string pendingToken,
        string code,
        string? challengeId,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var result = await identity.CompleteMfaAsync(pendingToken, code, challengeId, ip, userAgent, cancellationToken);
        return await FinishAsync(result, ip, null, cancellationToken);
    }

    public Task<AuthorizationRedirect> StartExternalAsync(ExternalLoginStart start, CancellationToken cancellationToken) =>
        identity.GetAuthorizationUrlAsync(start, cancellationToken);

    public async Task<AuthSession> CompleteCodeAsync(
        string code,
        string redirectUri,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var result = await identity.CompleteAuthorizationCodeAsync(code, redirectUri, ip, userAgent, cancellationToken);
        return await FinishAsync(result, ip, null, cancellationToken);
    }

    public async Task<AuthSession> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var result = await identity.RefreshAsync(refreshToken, ip, userAgent, cancellationToken);
        if (!result.Succeeded || result.Principal is null || result.AccessToken is null || result.ExpiresAt is null)
        {
            return Failed(result.ErrorCode ?? "invalid_refresh");
        }

        var user = await db.Users.FirstOrDefaultAsync(account => account.Subject == result.Principal.Subject, cancellationToken);
        if (user is null)
        {
            return Failed("invalid_refresh");
        }

        var tenants = await MembershipsAsync(user.Id, cancellationToken);
        return Success(user, tenants, result);
    }

    public async Task<string> RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            var supported = await identity.RequestPasswordResetAsync(email, cancellationToken);
            return supported ? "sent" : "unavailable";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Password reset request failed. {ExceptionType}", exception.GetType().Name);
            return "sent";
        }
    }

    private async Task<AuthSession> FinishAsync(
        IdentityLoginResult result,
        string? ip,
        string? failureDetail,
        CancellationToken cancellationToken)
    {
        if (result.MfaRequired)
        {
            return new AuthSession(
                false,
                null,
                null,
                null,
                true,
                result.PendingAuthenticationToken,
                result.ChallengeId,
                result.Factors,
                null,
                null,
                null);
        }

        if (!result.Succeeded || result.Principal is null || result.AccessToken is null || result.ExpiresAt is null)
        {
            await activity.WriteAsync(null, null, "login_failed", "denied", failureDetail ?? result.ErrorCode, ip, cancellationToken);
            return Failed(result.ErrorCode ?? "invalid_credentials");
        }

        var user = await UpsertAsync(result.Principal, cancellationToken);
        if (user is null)
        {
            await activity.WriteAsync(null, null, "login_failed", "denied", "account_conflict", ip, cancellationToken);
            return Failed("account_conflict");
        }

        var tenants = await MembershipsAsync(user.Id, cancellationToken);
        await activity.WriteAsync(user.Id, null, "login_succeeded", "allowed", null, ip, cancellationToken);
        return Success(user, tenants, result);
    }

    private async Task<UserAccount?> UpsertAsync(IdentityPrincipal principal, CancellationToken cancellationToken)
    {
        var email = principal.Email.Trim().ToLowerInvariant();
        var bySubject = await db.Users.FirstOrDefaultAsync(account => account.Subject == principal.Subject, cancellationToken);
        var byEmail = await db.Users.FirstOrDefaultAsync(account => account.Email == email, cancellationToken);
        if (bySubject is not null && byEmail is not null && bySubject.Id != byEmail.Id)
        {
            return null;
        }

        if (bySubject is null && byEmail is not null)
        {
            if (!byEmail.Subject.StartsWith("local:", StringComparison.Ordinal))
            {
                return null;
            }

            byEmail.Subject = principal.Subject;
            byEmail.DisplayName = principal.DisplayName;
            await db.SaveChangesAsync(cancellationToken);
            return byEmail;
        }

        if (bySubject is not null)
        {
            if (!string.Equals(bySubject.Email, email, StringComparison.Ordinal))
            {
                var taken = await db.Users.AnyAsync(account => account.Email == email && account.Id != bySubject.Id, cancellationToken);
                if (taken)
                {
                    return null;
                }

                bySubject.Email = email;
            }

            bySubject.DisplayName = principal.DisplayName;
            await db.SaveChangesAsync(cancellationToken);
            return bySubject;
        }

        var created = new UserAccount
        {
            Id = Guid.NewGuid(),
            Subject = principal.Subject,
            Email = email,
            DisplayName = principal.DisplayName,
            CreatedAt = time.GetUtcNow(),
        };
        db.Users.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    private async Task<IReadOnlyList<TenantMembershipSummary>> MembershipsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var rows = await db.Memberships
            .Include(membership => membership.Tenant)
            .Where(membership => membership.UserId == userId && membership.Tenant!.Status == TenantStatus.Active)
            .ToListAsync(cancellationToken);
        var grants = await db.AuditorGrants
            .Where(grant => grant.UserId == userId)
            .ToListAsync(cancellationToken);
        var visible = new List<TenantMembershipSummary>();
        foreach (var membership in rows.OrderBy(membership => membership.Tenant!.Name, StringComparer.Ordinal))
        {
            if (membership.Role == TenantRole.Auditor)
            {
                var grant = grants.FirstOrDefault(item => item.TenantId == membership.TenantId && item.IsActiveOn(today));
                if (grant is null)
                {
                    continue;
                }
            }

            visible.Add(new TenantMembershipSummary(
                membership.TenantId,
                membership.Tenant!.Name,
                membership.Tenant.Slug,
                membership.Tenant.Region,
                membership.Role.ToString(),
                membership.Tenant.IsExample));
        }

        return visible;
    }

    private static AuthSession Success(UserAccount user, IReadOnlyList<TenantMembershipSummary> tenants, IdentityLoginResult result) =>
        new(
            true,
            null,
            new UserSummary(user.Id, user.Email, user.DisplayName),
            tenants,
            false,
            null,
            null,
            null,
            result.AccessToken,
            result.RefreshToken,
            result.ExpiresAt);

    private static AuthSession Failed(string code) =>
        new(false, code, null, null, false, null, null, null, null, null, null);

    private static string? Sanitize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > 320 || trimmed.Any(character => char.IsControl(character)))
        {
            return null;
        }

        return trimmed.ToLowerInvariant();
    }
}
