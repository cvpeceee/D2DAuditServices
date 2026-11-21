using System.Text.RegularExpressions;
using System.Threading.Channels;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Onboarding;

public sealed record OnboardingIdentity(IdentityMode Mode)
{
    public bool PasswordRequired => Mode == IdentityMode.Local;
}

public sealed partial class AppLinks(IConfiguration configuration, IHostEnvironment environment)
{
    public string BaseUrl
    {
        get
        {
            var configured = configuration["APP_BASE_URL"]
                ?? configuration.GetSection("Cors:Origins").Get<string[]>()?.FirstOrDefault()
                ?? "http://localhost:5173";
            return configured.TrimEnd('/');
        }
    }

    public bool ShowDevLinks => environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public string Link(string path, string token) => $"{BaseUrl}{path}?token={Uri.EscapeDataString(token)}";

    /// <summary>The configurable notifier returns "email:xxxxxxxx" when no provider is configured in dev.</summary>
    public static bool IsMockDelivery(string result) => MockResult().IsMatch(result);

    [GeneratedRegex("^email:[0-9a-f]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex MockResult();
}

public sealed class SignupService(
    ControlPlaneDbContext control,
    OnboardingIdentity identity,
    PasswordHasher<UserAccount> passwords,
    INotifier notifier,
    ISignupProvisioningQueue queue,
    IActivityWriter activity,
    AppLinks links,
    TimeProvider time,
    ILogger<SignupService> logger) : ISignupService
{
    private const int MaxPendingPerDomain = 5;

    public SignupOptionsDto Options() => new(
        identity.Mode == IdentityMode.Local ? "local" : "workos",
        identity.PasswordRequired,
        ["US", "EU"],
        SignupRules.MinimumPasswordLength);

    public async Task<Result<SignupAccepted>> StartAsync(SignupRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var errors = SignupRules.Validate(new SignupInput(
            request.CompanyName ?? "",
            request.Email ?? "",
            request.Password,
            request.Region ?? "",
            identity.PasswordRequired));
        if (errors.Count > 0)
        {
            await activity.WriteAsync(null, null, "signup.rejected", "denied", errors[0], ipAddress, cancellationToken);
            return Result<SignupAccepted>.Fail(errors[0]);
        }

        var email = SignupRules.NormalizeEmail(request.Email!);
        var domain = SignupRules.DomainOf(email);
        var duplicate = await DuplicateAsync(email, domain, cancellationToken);
        if (duplicate is not null)
        {
            await activity.WriteAsync(null, null, "signup.rejected", "denied", duplicate, ipAddress, cancellationToken);
            return Result<SignupAccepted>.Fail(duplicate);
        }

        var now = time.GetUtcNow();
        var pending = await control.SignupRequests
            .Where(item => item.EmailDomain == domain && item.Status == SignupStatus.PendingVerification && item.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        if (pending.Count(item => item.Email != email) >= MaxPendingPerDomain)
        {
            return Result<SignupAccepted>.Fail("too_many_signups");
        }

        control.SignupRequests.RemoveRange(pending.Where(item => item.Email == email));
        var token = OneTimeTokens.Create();
        var fullName = request.FullName?.Trim();
        var record = new SignupRequestRecord
        {
            Id = Guid.NewGuid(),
            CompanyName = request.CompanyName!.Trim(),
            Email = email,
            EmailDomain = domain,
            DisplayName = string.IsNullOrWhiteSpace(fullName) ? SignupRules.DisplayNameFor(email) : fullName[..Math.Min(200, fullName.Length)],
            Region = request.Region!.Trim().ToUpperInvariant(),
            TokenHash = OneTimeTokens.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(SignupRules.VerificationLifetime),
            Status = SignupStatus.PendingVerification,
        };
        if (identity.PasswordRequired)
        {
            record.PasswordHash = passwords.HashPassword(new UserAccount { Email = email }, request.Password!);
        }

        control.SignupRequests.Add(record);
        await control.SaveChangesAsync(cancellationToken);

        var url = links.Link("/signup/verify", token);
        var delivery = await notifier.SendAsync(
            "email",
            email,
            "Verify your email to create your D2D Audit workspace",
            $"Open this link within 24 hours to create the {record.CompanyName} workspace: {url}",
            cancellationToken);
        string? devUrl = null;
        if (links.ShowDevLinks && AppLinks.IsMockDelivery(delivery))
        {
            devUrl = url;
            logger.LogInformation("Mock email: signup verification link for {Email} is {Url}", email, url);
        }

        await activity.WriteAsync(null, null, "signup.requested", "ok", domain, ipAddress, cancellationToken);
        return Result<SignupAccepted>.Ok(new SignupAccepted(record.Id, record.Status, email, record.ExpiresAt, devUrl));
    }

    public async Task<Result<SignupStatusDto>> VerifyAsync(VerifySignupRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 200)
        {
            return Result<SignupStatusDto>.Fail("invalid_token");
        }

        var hash = OneTimeTokens.Hash(request.Token.Trim());
        var record = await control.SignupRequests.FirstOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        if (record is null)
        {
            await activity.WriteAsync(null, null, "signup.verify", "denied", "invalid_token", ipAddress, cancellationToken);
            return Result<SignupStatusDto>.Fail("invalid_token");
        }

        if (record.Status != SignupStatus.PendingVerification)
        {
            return Result<SignupStatusDto>.Ok(await ToStatusAsync(record, cancellationToken));
        }

        var now = time.GetUtcNow();
        if (OneTimeTokens.IsExpired(record.ExpiresAt, now))
        {
            return Result<SignupStatusDto>.Fail("token_expired");
        }

        var duplicate = await DuplicateAsync(record.Email, record.EmailDomain, cancellationToken);
        if (duplicate is not null)
        {
            return Result<SignupStatusDto>.Fail(duplicate);
        }

        record.VerifiedAt = now;
        record.Status = SignupStatus.Provisioning;
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(null, null, "signup.verified", "ok", record.EmailDomain, ipAddress, cancellationToken);
        await queue.EnqueueAsync(new SignupProvisionWork(record.Id), cancellationToken);
        return Result<SignupStatusDto>.Ok(await ToStatusAsync(record, cancellationToken));
    }

    public async Task<Result<SignupStatusDto>> StatusAsync(Guid signupId, CancellationToken cancellationToken)
    {
        var record = await control.SignupRequests.AsNoTracking().FirstOrDefaultAsync(item => item.Id == signupId, cancellationToken);
        return record is null
            ? Result<SignupStatusDto>.Fail("not_found")
            : Result<SignupStatusDto>.Ok(await ToStatusAsync(record, cancellationToken));
    }

    private async Task<string?> DuplicateAsync(string email, string domain, CancellationToken cancellationToken)
    {
        if (await control.Users.AnyAsync(item => EF.Functions.ILike(item.Email, email), cancellationToken))
        {
            return "email_taken";
        }

        return await control.TenantDomains.AnyAsync(item => item.Domain == domain, cancellationToken) ? "domain_taken" : null;
    }

    private async Task<SignupStatusDto> ToStatusAsync(SignupRequestRecord record, CancellationToken cancellationToken)
    {
        var percent = 0;
        var step = record.Status;
        if (record.Status == SignupStatus.Provisioning)
        {
            percent = 5;
            step = "Queued";
            if (record.TenantId is Guid tenantId)
            {
                var job = await control.Jobs.AsNoTracking()
                    .Where(item => item.TenantId == tenantId && item.Kind == "tenant_provision")
                    .OrderByDescending(item => item.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                if (job is not null)
                {
                    percent = Math.Clamp(job.Percent ?? 0, 5, 95);
                    step = job.CurrentStep;
                }
            }
        }
        else if (record.Status == SignupStatus.Ready)
        {
            percent = 100;
            step = "Workspace is ready";
        }

        var next = record.Status switch
        {
            SignupStatus.PendingVerification => "verify_email",
            SignupStatus.Provisioning => "wait",
            SignupStatus.Ready => identity.PasswordRequired ? "login" : "sso",
            _ => "contact_support",
        };
        return new SignupStatusDto(record.Id, record.Status, record.Email, record.TenantId, record.CompanyName, percent, step, record.Error, next);
    }
}

public sealed class SignupProvisioningQueue(IServiceScopeFactory scopes, ILogger<SignupProvisioningQueue> logger) : BackgroundService, ISignupProvisioningQueue
{
    private readonly Channel<SignupProvisionWork> _pending = Channel.CreateUnbounded<SignupProvisionWork>();

    public ValueTask EnqueueAsync(SignupProvisionWork work, CancellationToken cancellationToken) =>
        _pending.Writer.WriteAsync(work, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueStuckAsync(stoppingToken);
        await foreach (var work in _pending.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SignupProvisioningRunner>().RunAsync(work, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Signup provisioning {SignupId} failed before the runner finished.", work.SignupId);
            }
        }
    }

    private async Task RequeueStuckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var control = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var stuck = await control.SignupRequests.AsNoTracking()
                .Where(item => item.Status == SignupStatus.Provisioning)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in stuck)
            {
                await _pending.Writer.WriteAsync(new SignupProvisionWork(id), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not resume pending signup provisioning.");
        }
    }
}

public sealed class SignupProvisioningRunner(
    ControlPlaneDbContext control,
    ITenantProvisioner provisioner,
    IActivityWriter activity,
    TimeProvider time,
    ILogger<SignupProvisioningRunner> logger)
{
    public async Task RunAsync(SignupProvisionWork work, CancellationToken cancellationToken)
    {
        var record = await control.SignupRequests.FirstOrDefaultAsync(item => item.Id == work.SignupId, cancellationToken);
        if (record is null || record.Status != SignupStatus.Provisioning)
        {
            return;
        }

        try
        {
            if (await control.Users.AnyAsync(item => EF.Functions.ILike(item.Email, record.Email), cancellationToken))
            {
                await FailAsync(record, "email_taken", cancellationToken);
                return;
            }

            if (await control.TenantDomains.AnyAsync(item => item.Domain == record.EmailDomain, cancellationToken))
            {
                await FailAsync(record, "domain_taken", cancellationToken);
                return;
            }

            var slug = await UniqueSlugAsync(SignupRules.SlugFor(record.CompanyName), cancellationToken);

            // Record the tenant id as soon as the provisioner creates it so status polling can follow the job.
            var created = await provisioner.CreateAsync(new CreateTenantRequest(slug, record.CompanyName, record.Region, false), cancellationToken);
            if (!created.IsSuccess || created.Value is null)
            {
                await FailAsync(record, created.Error ?? "provisioning_failed", cancellationToken);
                return;
            }

            var tenant = created.Value;
            record.TenantId = tenant.Id;
            var now = time.GetUtcNow();
            var user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Subject = "local:" + Guid.NewGuid().ToString("N"),
                Email = record.Email,
                DisplayName = record.DisplayName,
                PasswordHash = record.PasswordHash,
                CreatedAt = now,
            };
            control.Users.Add(user);
            control.Memberships.Add(new Membership { Id = Guid.NewGuid(), UserId = user.Id, TenantId = tenant.Id, Role = TenantRole.Admin, Active = true });
            control.TenantDomains.Add(new TenantDomain { Id = Guid.NewGuid(), TenantId = tenant.Id, Domain = record.EmailDomain });
            control.Subscriptions.Add(new TenantSubscription { TenantId = tenant.Id, Plan = PlanCatalog.Starter, Status = "Trialing", Provider = "mock", UpdatedAt = now });
            control.TenantOnboarding.Add(new TenantOnboarding { TenantId = tenant.Id, UpdatedAt = now });
            control.SecuritySettings.Add(new TenantSecuritySettings { TenantId = tenant.Id, UpdatedAt = now });
            record.UserId = user.Id;
            record.Status = SignupStatus.Ready;
            record.PasswordHash = null;
            record.Error = null;
            await control.SaveChangesAsync(cancellationToken);
            await activity.WriteAsync(user.Id, tenant.Id, "signup.provisioned", "ok", tenant.Slug, null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Signup provisioning failed for {SignupId}.", record.Id);
            var tenantId = record.TenantId;
            control.ChangeTracker.Clear();
            var fresh = await control.SignupRequests.FirstAsync(item => item.Id == work.SignupId, cancellationToken);
            fresh.TenantId = tenantId;
            await FailAsync(fresh, "provisioning_failed", cancellationToken);
        }
    }

    private async Task<string> UniqueSlugAsync(string baseSlug, CancellationToken cancellationToken)
    {
        var slug = baseSlug;
        var suffix = 2;
        while (await control.Tenants.AnyAsync(item => item.Slug == slug, cancellationToken))
        {
            var tail = "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            slug = (baseSlug.Length + tail.Length > 63 ? baseSlug[..(63 - tail.Length)] : baseSlug) + tail;
            suffix++;
        }

        return slug;
    }

    private async Task FailAsync(SignupRequestRecord record, string error, CancellationToken cancellationToken)
    {
        record.Status = SignupStatus.Failed;
        record.Error = error;
        record.PasswordHash = null;
        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(null, record.TenantId, "signup.failed", "error", error, null, cancellationToken);
    }
}
