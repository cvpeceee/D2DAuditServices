using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Tenancy;

public sealed class ControlPlaneDirectory(ControlPlaneDbContext db) : IControlPlaneDirectory
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
        db.Database.CanConnectAsync(cancellationToken);

    public async Task<DirectoryUser?> FindUserBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(account => account.Subject == subject, cancellationToken);
        return user is null ? null : new DirectoryUser(user.Id, user.Email, user.DisplayName);
    }

    public async Task<IReadOnlyList<DirectoryMembership>> MembershipsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await db.Memberships.AsNoTracking()
            .Include(membership => membership.Tenant)
            .Where(membership => membership.UserId == userId)
            .ToListAsync(cancellationToken);
        return memberships.Select(membership => new DirectoryMembership(
            membership.TenantId,
            membership.Tenant?.Name ?? "",
            membership.Tenant?.Region ?? "",
            membership.Tenant?.IsExample ?? false,
            membership.Tenant?.Status ?? TenantStatus.Provisioning,
            membership.Role,
            membership.Tenant?.DatabaseName)).ToArray();
    }

    public async Task<IReadOnlyList<DirectoryGrant>> ActiveGrantsAsync(Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        var grants = await db.AuditorGrants.AsNoTracking()
            .Include(grant => grant.Frameworks)
            .Where(grant => grant.UserId == userId)
            .ToListAsync(cancellationToken);
        return grants
            .Where(grant => grant.IsActiveOn(today))
            .Select(grant => new DirectoryGrant(grant.TenantId, grant.Frameworks.Select(framework => framework.FrameworkKey).ToArray()))
            .ToArray();
    }
}
