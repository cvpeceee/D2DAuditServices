using System.Security.Claims;
using System.Text.Encodings.Web;
using D2DAudit.Application;
using D2DAudit.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace D2DAudit.Api.Security;

public static class SessionCookies
{
    public const string Access = "d2d_session";

    public const string Refresh = "d2d_refresh";

    public static void Write(HttpContext http, bool alwaysSecure, string accessToken, DateTimeOffset expires, string? refreshToken)
    {
        var secure = alwaysSecure || http.Request.IsHttps;
#pragma warning disable S2092 // Production always passes alwaysSecure. Local HTTP cannot set Secure or the browser drops the cookie.
        http.Response.Cookies.Append(Access, accessToken, AccessOptions(secure, expires));
        if (!string.IsNullOrEmpty(refreshToken))
        {
            http.Response.Cookies.Append(Refresh, refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Strict,
                Path = "/api/v1/auth",
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                IsEssential = true,
            });
        }
#pragma warning restore S2092
    }

    public static void Clear(HttpContext http, bool alwaysSecure)
    {
        var secure = alwaysSecure || http.Request.IsHttps;
#pragma warning disable S2092 // Same Secure rule as Write: false only for local HTTP.
        http.Response.Cookies.Delete(Access, new CookieOptions { Path = "/", Secure = secure, HttpOnly = true, SameSite = SameSiteMode.Lax });
        http.Response.Cookies.Delete(Refresh, new CookieOptions { Path = "/api/v1/auth", Secure = secure, HttpOnly = true, SameSite = SameSiteMode.Strict });
#pragma warning restore S2092
    }

#pragma warning disable S2092 // Secure is true outside local HTTP. See Write.
    private static CookieOptions AccessOptions(bool secure, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
#pragma warning restore S2092
}

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IIdentityProvider identity) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "d2d";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadBearer() ?? ReadCookie();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var identityResult = await identity.ValidateAccessTokenAsync(token, Context.RequestAborted);
        if (identityResult is null)
        {
            return AuthenticateResult.Fail("invalid token");
        }

        var claims = new List<Claim> { new("sub", identityResult.Subject) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private string? ReadBearer()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : null;
    }

    private string? ReadCookie() =>
        Request.Cookies.TryGetValue(SessionCookies.Access, out var token) ? token : null;
}

public sealed class CsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method)
            || context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        if (context.Request.Headers["X-D2D-Request"] != "1")
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new { title = "Missing request header.", status = 400, code = "csrf" },
                context.RequestAborted);
            return;
        }

        await next(context);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TenantScopedAttribute : Attribute;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        TenantContext tenant,
        IControlPlaneDirectory directory,
        IActivityWriter activity,
        TimeProvider time)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<TenantScopedAttribute>() is null)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var subject = context.User.FindFirst("sub")?.Value;
        var user = subject is null ? null : await directory.FindUserBySubjectAsync(subject, context.RequestAborted);
        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var memberships = (await directory.MembershipsAsync(user.Id, context.RequestAborted))
            .Where(membership => membership.Status == TenantStatus.Active)
            .ToList();
        var grants = await directory.ActiveGrantsAsync(user.Id, today, context.RequestAborted);

        bool GrantOk(DirectoryMembership membership, out DirectoryGrant? grant)
        {
            grant = null;
            if (membership.Role != TenantRole.Auditor)
            {
                return true;
            }

            grant = grants.FirstOrDefault(item => item.TenantId == membership.TenantId);
            return grant is not null;
        }

        var usable = memberships.Where(membership => GrantOk(membership, out _)).ToList();
        DirectoryMembership? selected = null;
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            if (!Guid.TryParse(header.ToString(), out var tenantId))
            {
                await WriteProblem(context, StatusCodes.Status403Forbidden, "tenant_forbidden");
                return;
            }

            selected = usable.FirstOrDefault(membership => membership.TenantId == tenantId);
            if (selected is null)
            {
                var ip = context.Connection.RemoteIpAddress?.ToString();
                await activity.WriteAsync(user.Id, memberships.Any(item => item.TenantId == tenantId) ? tenantId : null, "tenant_access_denied", "denied", null, ip, context.RequestAborted);
                await WriteProblem(context, StatusCodes.Status403Forbidden, "tenant_forbidden");
                return;
            }
        }
        else if (usable.Count == 1)
        {
            selected = usable[0];
        }
        else
        {
            await WriteProblem(context, StatusCodes.Status400BadRequest, usable.Count == 0 ? "tenant_forbidden" : "tenant_required");
            return;
        }

        GrantOk(selected, out var activeGrant);
        var databaseName = selected.DatabaseName;
        if (databaseName is null || !DatabaseNames.IsSafe(databaseName))
        {
            await WriteProblem(context, StatusCodes.Status503ServiceUnavailable, "tenant_unavailable");
            return;
        }

        tenant.Resolve(
            selected.TenantId,
            selected.TenantName,
            selected.Region,
            selected.IsExample,
            user.Id,
            user.Email,
            user.DisplayName,
            selected.Role,
            activeGrant?.FrameworkKeys ?? [],
            databaseName);
        await next(context);
    }

    private static Task WriteProblem(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { title = code, status, code }, context.RequestAborted);
    }
}

public sealed class TenantWriteRequirement : IAuthorizationRequirement;

public sealed class TenantAdminRequirement : IAuthorizationRequirement;

public sealed class AuditActRequirement : IAuthorizationRequirement;

public sealed class TenantWriteHandler(TenantContext tenant, IActivityWriter activity, IHttpContextAccessor http) : AuthorizationHandler<TenantWriteRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantWriteRequirement requirement)
    {
        if (tenant.IsResolved && tenant.CanWrite)
        {
            context.Succeed(requirement);
            return;
        }

        if (tenant.IsResolved)
        {
            await activity.WriteAsync(
                tenant.UserId,
                tenant.TenantId,
                "write_denied",
                "denied",
                tenant.Role.ToString(),
                http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                http.HttpContext?.RequestAborted ?? CancellationToken.None);
        }
    }
}

public sealed class TenantAdminHandler(TenantContext tenant, IActivityWriter activity, IHttpContextAccessor http) : AuthorizationHandler<TenantAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantAdminRequirement requirement)
    {
        if (tenant.IsResolved && tenant.IsAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        if (tenant.IsResolved)
        {
            await activity.WriteAsync(
                tenant.UserId,
                tenant.TenantId,
                "write_denied",
                "denied",
                tenant.Role.ToString(),
                http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                http.HttpContext?.RequestAborted ?? CancellationToken.None);
        }
    }
}

public sealed class AuditActHandler(TenantContext tenant, IActivityWriter activity, IHttpContextAccessor http) : AuthorizationHandler<AuditActRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AuditActRequirement requirement)
    {
        if (tenant.IsResolved && tenant.Role is TenantRole.Admin or TenantRole.ControlOwner or TenantRole.Auditor)
        {
            context.Succeed(requirement);
            return;
        }

        if (tenant.IsResolved)
        {
            await activity.WriteAsync(
                tenant.UserId,
                tenant.TenantId,
                "audit_denied",
                "denied",
                tenant.Role.ToString(),
                http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                http.HttpContext?.RequestAborted ?? CancellationToken.None);
        }
    }
}
