using D2DAudit.Api.Security;
using D2DAudit.Application;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Api.Controllers;

public sealed record PasswordLoginBody(string Email, string Password);

public sealed record MfaBody(string PendingAuthenticationToken, string Code, string? ChallengeId);

public sealed record AuthorizeBody(string Provider, string RedirectUri, string State, string? Email);

public sealed record CallbackBody(string Code, string RedirectUri);

public sealed record ForgotBody(string Email);

public sealed record AuthBody(
    bool Ok,
    string? ErrorCode,
    UserSummary? User,
    IReadOnlyList<TenantMembershipSummary>? Tenants,
    bool MfaRequired,
    string? PendingAuthenticationToken,
    string? ChallengeId,
    IReadOnlyList<MfaFactor>? Factors);

[ApiController]
[EnableRateLimiting("auth")]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth, IHostEnvironment environment) : ControllerBase
{
    [HttpGet("mode")]
    [AllowAnonymous]
    public ActionResult<AuthModeResponse> Mode() => auth.Mode();

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthBody>> Login([FromBody] PasswordLoginBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password) || body.Password.Length > 200)
        {
            return Unauthorized(Fail("invalid_credentials"));
        }

        var session = await auth.PasswordLoginAsync(body.Email, body.Password, Ip(), UserAgent(), cancellationToken);
        return ToAction(session);
    }

    [HttpPost("mfa")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthBody>> Mfa([FromBody] MfaBody body, CancellationToken cancellationToken)
    {
        var session = await auth.CompleteMfaAsync(body.PendingAuthenticationToken, body.Code, body.ChallengeId, Ip(), UserAgent(), cancellationToken);
        return ToAction(session);
    }

    [HttpPost("authorize")]
    [AllowAnonymous]
    public async Task<IActionResult> Authorize([FromBody] AuthorizeBody body, CancellationToken cancellationToken)
    {
        var result = await auth.StartExternalAsync(
            new ExternalLoginStart(body.Provider, body.RedirectUri, body.State, body.Email, null),
            cancellationToken);
        return result.Ok ? Ok(new { url = result.Url }) : BadRequest(new { code = result.ErrorCode });
    }

    [HttpPost("callback")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthBody>> Callback([FromBody] CallbackBody body, CancellationToken cancellationToken)
    {
        var session = await auth.CompleteCodeAsync(body.Code, body.RedirectUri, Ip(), UserAgent(), cancellationToken);
        return ToAction(session);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthBody>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(SessionCookies.Refresh, out var refresh) || string.IsNullOrWhiteSpace(refresh))
        {
            return Unauthorized(Fail("invalid_refresh"));
        }

        var session = await auth.RefreshAsync(refresh, Ip(), UserAgent(), cancellationToken);
        return ToAction(session);
    }

    [HttpPost("forgot")]
    [AllowAnonymous]
    public async Task<IActionResult> Forgot([FromBody] ForgotBody body, CancellationToken cancellationToken)
    {
        var result = await auth.RequestPasswordResetAsync(body.Email, cancellationToken);
        return Ok(new { result });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public IActionResult Logout()
    {
        SessionCookies.Clear(HttpContext, SecureCookies());
        return NoContent();
    }

    [HttpGet("/api/v1/me")]
    [Authorize]
    public async Task<ActionResult<AuthBody>> Me(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst("sub")?.Value;
        if (subject is null)
        {
            return Unauthorized();
        }

        var session = await auth.CurrentAsync(subject, cancellationToken);
        return session is null ? Unauthorized() : Ok(ToBody(session));
    }

    private ActionResult<AuthBody> ToAction(AuthSession session)
    {
        if (session.MfaRequired)
        {
            return Ok(ToBody(session));
        }

        if (!session.Ok || session.AccessToken is null || session.ExpiresAt is null || session.User is null)
        {
            return Unauthorized(Fail(session.ErrorCode ?? "invalid_credentials"));
        }

        SessionCookies.Write(HttpContext, SecureCookies(), session.AccessToken, session.ExpiresAt.Value, session.RefreshToken);
        return Ok(ToBody(session));
    }

    private static AuthBody ToBody(AuthSession session) => new(
        session.Ok,
        session.ErrorCode,
        session.User,
        session.Tenants,
        session.MfaRequired,
        session.PendingAuthenticationToken,
        session.ChallengeId,
        session.Factors);

    private static AuthBody Fail(string code) => new(false, code, null, null, false, null, null, null);

    private bool SecureCookies() => !environment.IsDevelopment() && !environment.IsEnvironment("Testing");

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent() => Request.Headers.UserAgent.ToString();
}

[ApiController]
[Authorize]
[TenantScoped]
[Route("api/v1")]
public sealed class WorkspaceController(IWorkspaceQuery query) : ControllerBase
{
    [HttpGet("workspace")]
    public Task<WorkspaceResponse> Workspace(CancellationToken cancellationToken) => query.GetAsync(cancellationToken);

    [HttpGet("dashboard")]
    public Task<DashboardResponse> Dashboard(CancellationToken cancellationToken) => query.DashboardAsync(cancellationToken);

    [HttpGet("frameworks")]
    public Task<IReadOnlyList<FrameworkListItem>> Frameworks(CancellationToken cancellationToken) => query.FrameworksAsync(cancellationToken);

    [HttpGet("frameworks/{key}")]
    public async Task<ActionResult<FrameworkDetail>> Framework(string key, CancellationToken cancellationToken)
    {
        var detail = await query.FrameworkAsync(key, cancellationToken);
        return detail is null ? NotFound(new { code = "not_found" }) : Ok(detail);
    }

    [HttpGet("controls")]
    public Task<IReadOnlyList<ControlListItem>> Controls(
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] string? owner,
        CancellationToken cancellationToken) =>
        query.ControlsAsync(status, q, owner, cancellationToken);

    [HttpGet("controls/{code}")]
    public async Task<ActionResult<ControlDetail>> Control(string code, CancellationToken cancellationToken)
    {
        var detail = await query.ControlAsync(code, cancellationToken);
        return detail is null ? NotFound(new { code = "not_found" }) : Ok(detail);
    }

    [HttpPut("controls/{code}/owner")]
    [Authorize(Policy = "TenantWrite")]
    public async Task<ActionResult<ControlDetail>> AssignOwner(string code, [FromBody] AssignOwnerRequest body, CancellationToken cancellationToken)
    {
        var detail = await query.AssignOwnerAsync(code, body, cancellationToken);
        return detail is null ? NotFound(new { code = "not_found" }) : Ok(detail);
    }

    [HttpGet("jobs")]
    public Task<IReadOnlyList<JobDto>> Jobs(CancellationToken cancellationToken) => query.JobsAsync(cancellationToken);

    [HttpGet("activity")]
    [Authorize(Policy = "TenantAdmin")]
    public Task<IReadOnlyList<ActivityDto>> Activity(CancellationToken cancellationToken) => query.ActivityAsync(cancellationToken);
}

[ApiController]
public sealed class HealthController(ControlPlaneDbContext db) : ControllerBase
{
    [HttpGet("/health")]
    [AllowAnonymous]
    public IActionResult Live() => Ok(new { status = "ok" });

    [HttpGet("/health/ready")]
    [AllowAnonymous]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var ready = await db.Database.CanConnectAsync(cancellationToken);
        return ready ? Ok(new { status = "ready" }) : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
