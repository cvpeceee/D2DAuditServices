using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace D2DAudit.Api.Security;

/// <summary>Tunable request-rate limits. Bound from the <c>RateLimiting</c> configuration section.</summary>
public sealed class RateLimitSettings
{
    public const string Section = "RateLimiting";

    /// <summary>Per-user (or per-IP for anonymous callers) token bucket applied to every API request.</summary>
    public int UserTokensPerMinute { get; set; } = 600;

    public int UserBurst { get; set; } = 120;

    /// <summary>Per-tenant bucket for API-key traffic (shared by every key of a tenant).</summary>
    public int TenantTokensPerMinute { get; set; } = 3000;

    /// <summary>Maximum in-flight requests per user/IP.</summary>
    public int UserConcurrency { get; set; } = 32;

    public int AuthPerMinute { get; set; } = 20;

    public int PublicTrustPerMinute { get; set; } = 10;

    public int PublicAssistantPerMinute { get; set; } = 5;

    public int AssistantPerMinute { get; set; } = 10;

    public int ExportPerMinute { get; set; } = 6;

    public int SignupPerTenMinutes { get; set; } = 10;

    public static RateLimitSettings Relaxed() => new()
    {
        UserTokensPerMinute = 100_000,
        UserBurst = 100_000,
        TenantTokensPerMinute = 100_000,
        UserConcurrency = 10_000,
        AuthPerMinute = 1_000,
        PublicTrustPerMinute = 1_000,
        PublicAssistantPerMinute = 1_000,
        AssistantPerMinute = 1_000,
        ExportPerMinute = 1_000,
        SignupPerTenMinutes = 1_000,
    };
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string PublicTrust = "public_trust";
    public const string PublicAssistant = "public_assistant";
    public const string Assistant = "assistant";
    public const string Export = "export";
    public const string Signup = "signup";
}

public static class EdgeProtection
{
    public const string ExportTimeoutPolicy = "export";

    /// <summary>
    /// Rate limiting: a chained global limiter (per-user token bucket, per-tenant bucket for API keys, per-user
    /// concurrency) plus tighter named policies for auth, public trust/assistant, workspace assistant, and exports.
    /// Rejections are 429 with a Retry-After header.
    /// </summary>
    public static IServiceCollection AddD2DRateLimiting(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<RateLimitSettings>()
            .Configure(settings =>
            {
                if (environment.IsEnvironment("Testing"))
                {
                    var relaxed = RateLimitSettings.Relaxed();
                    foreach (var property in typeof(RateLimitSettings).GetProperties())
                    {
                        property.SetValue(settings, property.GetValue(relaxed));
                    }
                }

                configuration.GetSection(RateLimitSettings.Section).Bind(settings);
            });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
                    : 60;
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { code = "rate_limited", title = "Too many requests. Slow down and retry later.", retryAfterSeconds = retryAfter },
                    cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    if (IsExempt(context))
                    {
                        return RateLimitPartition.GetNoLimiter("exempt");
                    }

                    var settings = Settings(context);
                    return RateLimitPartition.GetTokenBucketLimiter(CallerKey(context), _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = Math.Max(1, settings.UserBurst),
                        TokensPerPeriod = Math.Max(1, settings.UserTokensPerMinute / 6),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    var tenant = context.User.FindFirstValue("d2d_tenant_id");
                    if (IsExempt(context) || string.IsNullOrEmpty(tenant))
                    {
                        return RateLimitPartition.GetNoLimiter("no-tenant");
                    }

                    var settings = Settings(context);
                    return RateLimitPartition.GetTokenBucketLimiter("t:" + tenant, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = Math.Max(1, settings.TenantTokensPerMinute / 4),
                        TokensPerPeriod = Math.Max(1, settings.TenantTokensPerMinute / 6),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    if (IsExempt(context) || IsHub(context))
                    {
                        return RateLimitPartition.GetNoLimiter("exempt");
                    }

                    var settings = Settings(context);
                    return RateLimitPartition.GetConcurrencyLimiter(CallerKey(context), _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = Math.Max(1, settings.UserConcurrency),
                        QueueLimit = 0,
                    });
                }));

            options.AddPolicy(RateLimitPolicies.Auth, context => Window(IpKey(context), Settings(context).AuthPerMinute));
            options.AddPolicy(RateLimitPolicies.PublicTrust, context => Window(IpKey(context), Settings(context).PublicTrustPerMinute));
            options.AddPolicy(RateLimitPolicies.PublicAssistant, context => Window(IpKey(context), Settings(context).PublicAssistantPerMinute));
            options.AddPolicy(RateLimitPolicies.Assistant, context => Window(CallerKey(context), Settings(context).AssistantPerMinute));
            options.AddPolicy(RateLimitPolicies.Export, context => Window(CallerKey(context), Settings(context).ExportPerMinute));
            options.AddPolicy(RateLimitPolicies.Signup, context => Window(IpKey(context), Settings(context).SignupPerTenMinutes, TimeSpan.FromMinutes(10)));
        });

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(configuration.GetValue("RequestTimeouts:DefaultSeconds", 60)),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
            };
            options.AddPolicy(ExportTimeoutPolicy, TimeSpan.FromSeconds(configuration.GetValue("RequestTimeouts:ExportSeconds", 120)));
        });

        return services;
    }

    /// <summary>
    /// Trust X-Forwarded-* only from the configured reverse proxy networks (VPC CIDRs) and only one hop, so a client
    /// cannot spoof its IP to dodge per-IP limits or poison audit logs.
    /// </summary>
    public static IServiceCollection AddD2DForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var cidr in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr.Trim()));
            }

            foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy.Trim()));
            }
        });
        return services;
    }

    /// <summary>Kestrel connection and request limits that blunt slowloris, header floods, and oversized bodies.</summary>
    public static void ApplyD2DKestrelLimits(this Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions options, IConfiguration configuration)
    {
        var limits = options.Limits;
        limits.MaxRequestBodySize = configuration.GetValue<long?>("Kestrel:Limits:MaxRequestBodySize") ?? 1_048_576;
        limits.MaxRequestHeadersTotalSize = 32 * 1024;
        limits.MaxRequestHeaderCount = 64;
        limits.MaxRequestLineSize = 8 * 1024;
        limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
        limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
        limits.MinRequestBodyDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(240, TimeSpan.FromSeconds(10));
        limits.MinResponseDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(240, TimeSpan.FromSeconds(10));
        limits.Http2.MaxStreamsPerConnection = 100;
        var maxConnections = configuration.GetValue<long?>("Kestrel:Limits:MaxConcurrentConnections");
        if (maxConnections is > 0)
        {
            limits.MaxConcurrentConnections = maxConnections;
            limits.MaxConcurrentUpgradedConnections = maxConnections;
        }
    }

    /// <summary>
    /// Fail fast on configuration that is unsafe outside development: wildcard/plain-http CORS origins, demo seeding,
    /// or the development database password.
    /// </summary>
    public static void AssertProductionSafe(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction() && !environment.IsStaging())
        {
            return;
        }

        var problem = ProductionProblems(configuration).FirstOrDefault();
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }
    }

    public static IEnumerable<string> ProductionProblems(IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        foreach (var origin in origins)
        {
            if (origin.Contains('*', StringComparison.Ordinal)
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || uri.AbsolutePath != "/")
            {
                yield return $"Cors:Origins must contain only explicit https origins in production (rejected '{origin}').";
            }
        }

        if (configuration.GetValue("Seed:Demo", false))
        {
            yield return "Seed:Demo must be false in production.";
        }

        foreach (var name in new[] { "ControlPlane", "Migrations" })
        {
            var connection = configuration.GetConnectionString(name) ?? "";
            if (connection.Contains("d2d_dev_password", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"ConnectionStrings:{name} uses the development password in production.";
            }
        }
    }

    /// <summary>Wildcard AllowedHosts is tolerated (ALB health checks use the target IP as Host) but flagged.</summary>
    public static bool HasWildcardAllowedHosts(IConfiguration configuration)
    {
        var hosts = configuration["AllowedHosts"];
        return string.IsNullOrWhiteSpace(hosts) || hosts.Trim() == "*";
    }

    private static RateLimitPartition<string> Window(string key, int permits, TimeSpan? window = null) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permits),
            Window = window ?? TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    private static RateLimitSettings Settings(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;

    private static string IpKey(HttpContext context) => "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    public static string CallerKey(HttpContext context)
    {
        var subject = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue("sub") : null;
        return string.IsNullOrEmpty(subject) ? IpKey(context) : "u:" + subject;
    }

    private static bool IsExempt(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    private static bool IsHub(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase);
}
