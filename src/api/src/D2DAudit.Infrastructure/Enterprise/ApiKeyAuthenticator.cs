using System.Security.Cryptography;
using System.Text;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Enterprise;

public sealed class ApiKeyAuthenticator(
    ControlPlaneDbContext control,
    TenantDatabases databases,
    TimeProvider time) : IApiKeyAuthenticator
{
    public async Task<ApiKeyIdentity?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (!ApiKeyTokens.TryParseTenantId(token, out var tenantId))
        {
            return null;
        }

        var tenantRow = await control.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == tenantId && item.Status == TenantStatus.Active && item.DatabaseName != null,
                cancellationToken);
        if (tenantRow is null || string.IsNullOrWhiteSpace(tenantRow.DatabaseName) || !DatabaseNames.IsSafe(tenantRow.DatabaseName))
        {
            return null;
        }

        var hash = GroundedAssistant.HashToken(token.Trim());
        await using var db = databases.Create(tenantRow.DatabaseName);
        var record = await db.ApiKeys.FirstOrDefaultAsync(
            item => item.KeyHash == hash && item.RevokedAt == null,
            cancellationToken);
        if (record is null)
        {
            return null;
        }

        var expected = Encoding.ASCII.GetBytes(record.KeyHash);
        var actual = Encoding.ASCII.GetBytes(hash);
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return null;
        }

        record.LastUsedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new ApiKeyIdentity(
            record.Id,
            record.Name,
            record.CreatedByEmail,
            record.Scopes,
            tenantRow.Id,
            tenantRow.Name,
            tenantRow.Region,
            tenantRow.IsExample,
            tenantRow.DatabaseName);
    }
}
