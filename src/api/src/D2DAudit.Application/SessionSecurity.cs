namespace D2DAudit.Application;

/// <summary>Outcome of presenting a refresh token to the rotation ledger.</summary>
public enum RefreshTokenState
{
    /// <summary>Never seen (issued before the ledger existed or after a cache flush). The provider decides.</summary>
    Unknown,

    /// <summary>Issued by us and not yet used. It is now marked used.</summary>
    Fresh,

    /// <summary>Already used once. Presenting it again signals theft, so the whole session family is revoked.</summary>
    Reused,
}

public sealed record RefreshTokenCheck(RefreshTokenState State, string? Subject);

public sealed record LoginLock(bool Locked, TimeSpan RetryAfter);

/// <summary>
/// Server-side session controls that JWTs cannot carry on their own: per-account login backoff,
/// access-token revocation (logout), refresh-token rotation with reuse detection, and "revoke everything
/// issued before now" for a subject.
/// </summary>
public interface ISessionGuard
{
    Task<LoginLock> LoginLockAsync(string email, CancellationToken cancellationToken);

    Task<LoginLock> RecordLoginFailureAsync(string email, CancellationToken cancellationToken);

    Task ClearLoginFailuresAsync(string email, CancellationToken cancellationToken);

    Task RevokeAccessTokenAsync(string token, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<bool> IsRevokedAsync(string token, AccessTokenIdentity identity, CancellationToken cancellationToken);

    Task RegisterRefreshTokenAsync(string refreshToken, string subject, CancellationToken cancellationToken);

    Task<RefreshTokenCheck> ConsumeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeSubjectAsync(string subject, CancellationToken cancellationToken);
}

/// <summary>Exponential backoff for failed password logins, keyed per account (not per IP).</summary>
public static class LoginBackoff
{
    public const int FreeAttempts = 5;

    public static readonly TimeSpan MaxLock = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan FailureWindow = TimeSpan.FromHours(1);

    /// <summary>0 for the first five failures, then 30s, 60s, 2m, 4m ... capped at 30 minutes.</summary>
    public static TimeSpan LockFor(int failures)
    {
        if (failures < FreeAttempts)
        {
            return TimeSpan.Zero;
        }

        var exponent = Math.Min(failures - FreeAttempts, 16);
        var seconds = 30d * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxLock.TotalSeconds));
    }
}
