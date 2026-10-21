using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");
        auth.MapGet("/mode", (IAuthService service) => Results.Ok(service.Mode())).Produces<AuthModeResponse>();
        auth.MapPost("/login", Login).Produces<AuthBody>();
        auth.MapPost("/mfa", Mfa).Produces<AuthBody>();
        auth.MapPost("/authorize", Authorize);
        auth.MapPost("/callback", Callback).Produces<AuthBody>();
        auth.MapPost("/refresh", Refresh).Produces<AuthBody>();
        auth.MapPost("/forgot", Forgot);
        auth.MapPost("/logout", Logout);
        app.MapGet("/api/v1/me", Me).RequireAuthorization().Produces<AuthBody>();
        return app;
    }

    private static async Task<IResult> Login(
        PasswordLoginBody body,
        IAuthService auth,
        HttpContext http,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password) || body.Password.Length > 200)
        {
            return Results.Json(EndpointResults.Failed("invalid_credentials"), statusCode: StatusCodes.Status401Unauthorized);
        }

        var session = await auth.PasswordLoginAsync(body.Email, body.Password, Ip(http), Agent(http), cancellationToken);
        return EndpointResults.Session(session, http, environment);
    }

    private static async Task<IResult> Mfa(
        MfaBody body,
        IAuthService auth,
        HttpContext http,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var session = await auth.CompleteMfaAsync(body.PendingAuthenticationToken, body.Code, body.ChallengeId, Ip(http), Agent(http), cancellationToken);
        return EndpointResults.Session(session, http, environment);
    }

    private static async Task<IResult> Authorize(AuthorizeBody body, IAuthService auth, CancellationToken cancellationToken)
    {
        var result = await auth.StartExternalAsync(
            new ExternalLoginStart(body.Provider, body.RedirectUri, body.State, body.Email, null),
            cancellationToken);
        return result.Ok ? Results.Ok(new { url = result.Url }) : Results.BadRequest(new { code = result.ErrorCode });
    }

    private static async Task<IResult> Callback(
        CallbackBody body,
        IAuthService auth,
        HttpContext http,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var session = await auth.CompleteCodeAsync(body.Code, body.RedirectUri, Ip(http), Agent(http), cancellationToken);
        return EndpointResults.Session(session, http, environment);
    }

    private static async Task<IResult> Refresh(IAuthService auth, HttpContext http, IHostEnvironment environment, CancellationToken cancellationToken)
    {
        if (!http.Request.Cookies.TryGetValue(SessionCookies.Refresh, out var refresh) || string.IsNullOrWhiteSpace(refresh))
        {
            return Results.Json(EndpointResults.Failed("invalid_refresh"), statusCode: StatusCodes.Status401Unauthorized);
        }

        var session = await auth.RefreshAsync(refresh, Ip(http), Agent(http), cancellationToken);
        return EndpointResults.Session(session, http, environment);
    }

    private static async Task<IResult> Forgot(ForgotBody body, IAuthService auth, CancellationToken cancellationToken)
    {
        var result = await auth.RequestPasswordResetAsync(body.Email, cancellationToken);
        return Results.Ok(new { result });
    }

    private static IResult Logout(HttpContext http, IHostEnvironment environment)
    {
        var secure = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        SessionCookies.Clear(http, secure);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(IAuthService auth, HttpContext http, CancellationToken cancellationToken)
    {
        var subject = http.User.FindFirst("sub")?.Value;
        if (subject is null)
        {
            return Results.Unauthorized();
        }

        var session = await auth.CurrentAsync(subject, cancellationToken);
        return session is null ? Results.Unauthorized() : Results.Ok(EndpointResults.Body(session));
    }

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    private static string Agent(HttpContext http) => http.Request.Headers.UserAgent.ToString();
}
