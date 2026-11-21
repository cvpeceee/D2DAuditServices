using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using D2DAudit.Application;
using Microsoft.Extensions.Caching.Distributed;

namespace D2DAudit.Infrastructure.Identity;

/// <summary>
/// <see cref="ISessionGuard"/> on <see cref="IDistributedCache"/>. Uses Redis when <c>Redis:ConnectionString</c> is set
/// (required when more than one API instance runs) and an in-process cache otherwise. Keys never contain raw tokens or
/// emails, only SHA-256 digests.
/// </summary>
public sealed class DistributedSessionGuard(IDistributedCache cache, TimeProvider time) : ISessionGuard
{
    private const long ReuseGraceSeconds = 10;

    private static readonly TimeSpan RefreshLedgerTtl = TimeSpan.FromDays(30);

    public async Task<LoginLock> LoginLockAsync(string email, CancellationToken cancellationToken)
    {
        var until = await cache.GetStringAsync(Key("ll", Normalize(email)), cancellationToken);
        return LockFrom(until);
    }

    public async Task<LoginLock> RecordLoginFailureAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Normalize(email);
        var countKey = Key("lf", normalized);
        var current = int.TryParse(await cache.GetStringAsync(countKey, cancellationToken), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        var failures = current + 1;
        await cache.SetStringAsync(
            countKey,
            failures.ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = LoginBackoff.FailureWindow },
            cancellationToken);
        var lockFor = LoginBackoff.LockFor(failures);
        if (lockFor <= TimeSpan.Zero)
        {
            return new LoginLock(false, TimeSpan.Zero);
        }

        var until = time.GetUtcNow().Add(lockFor);
        await cache.SetStringAsync(
            Key("ll", normalized),
            until.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpiration = until },
            cancellationToken);
        return new LoginLock(true, lockFor);
    }

    public async Task ClearLoginFailuresAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Normalize(email);
        await cache.RemoveAsync(Key("lf", normalized), cancellationToken);
        await cache.RemoveAsync(Key("ll", normalized), cancellationToken);
    }

    public Task RevokeAccessTokenAsync(string token, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var ttl = expiresAt - time.GetUtcNow();
        if (ttl <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        return cache.SetStringAsync(
            Key("rv", token),
            "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl + TimeSpan.FromMinutes(2) },
            cancellationToken);
    }

    public async Task<bool> IsRevokedAsync(string token, AccessTokenIdentity identity, CancellationToken cancellationToken)
    {
        if (await cache.GetStringAsync(Key("rv", token), cancellationToken) is not null)
        {
            return true;
        }

        var revokedAt = await cache.GetStringAsync(Key("rs", identity.Subject), cancellationToken);
        if (revokedAt is null || !long.TryParse(revokedAt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        // Tokens without an issue time cannot prove they post-date the revocation.
        return identity.IssuedAt is null || identity.IssuedAt.Value.ToUnixTimeSeconds() <= seconds;
    }

    public Task RegisterRefreshTokenAsync(string refreshToken, string subject, CancellationToken cancellationToken) =>
        cache.SetStringAsync(
            Key("rt", refreshToken),
            "fresh|" + subject,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = RefreshLedgerTtl },
            cancellationToken);

    public async Task<RefreshTokenCheck> ConsumeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var key = Key("rt", refreshToken);
        var entry = await cache.GetStringAsync(key, cancellationToken);
        if (entry is null)
        {
            await cache.SetStringAsync(key, "used||" + time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = RefreshLedgerTtl }, cancellationToken);
            return new RefreshTokenCheck(RefreshTokenState.Unknown, null);
        }

        var parts = entry.Split('|');
        var state = parts[0];
        var subject = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        if (state == "used")
        {
            // Two browser tabs refreshing at the same instant is not theft: allow a short grace window.
            var usedAt = parts.Length > 2 && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var at) ? at : 0;
            return now - usedAt <= ReuseGraceSeconds
                ? new RefreshTokenCheck(RefreshTokenState.Unknown, subject)
                : new RefreshTokenCheck(RefreshTokenState.Reused, subject);
        }

        await cache.SetStringAsync(
            key,
            "used|" + subject + "|" + now.ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = RefreshLedgerTtl },
            cancellationToken);
        return new RefreshTokenCheck(RefreshTokenState.Fresh, subject);
    }

    public Task RevokeSubjectAsync(string subject, CancellationToken cancellationToken) =>
        cache.SetStringAsync(
            Key("rs", subject),
            time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = RefreshLedgerTtl },
            cancellationToken);

    private LoginLock LockFrom(string? until)
    {
        if (until is null || !long.TryParse(until, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return new LoginLock(false, TimeSpan.Zero);
        }

        var remaining = DateTimeOffset.FromUnixTimeSeconds(seconds) - time.GetUtcNow();
        return remaining > TimeSpan.Zero ? new LoginLock(true, remaining) : new LoginLock(false, TimeSpan.Zero);
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string Key(string kind, string value) =>
        "d2d:" + kind + ":" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
