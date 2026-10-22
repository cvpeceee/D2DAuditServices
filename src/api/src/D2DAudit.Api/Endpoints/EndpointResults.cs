using D2DAudit.Application;
using D2DAudit.Api.Security;

namespace D2DAudit.Api.Endpoints;

internal static class EndpointResults
{
    public static IResult FromResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        return result.Error switch
        {
            "not_found" => Results.NotFound(new { code = "not_found" }),
            "forbidden" => Results.Json(new { code = "forbidden" }, statusCode: StatusCodes.Status403Forbidden),
            _ => Results.BadRequest(new { code = result.Error }),
        };
    }

    public static IResult Session(AuthSession session, HttpContext http, IHostEnvironment environment)
    {
        if (session.MfaRequired)
        {
            return Results.Ok(Body(session));
        }

        if (!session.Ok || session.AccessToken is null || session.ExpiresAt is null || session.User is null)
        {
            return Results.Json(Failed(session.ErrorCode ?? "invalid_credentials"), statusCode: StatusCodes.Status401Unauthorized);
        }

        var secure = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        SessionCookies.Write(http, secure, session.AccessToken, session.ExpiresAt.Value, session.RefreshToken);
        return Results.Ok(Body(session));
    }

    public static AuthBody Body(AuthSession session) => new(
        session.Ok,
        session.ErrorCode,
        session.User,
        session.Tenants,
        session.MfaRequired,
        session.PendingAuthenticationToken,
        session.ChallengeId,
        session.Factors);

    public static AuthBody Failed(string code) => new(false, code, null, null, false, null, null, null);
}

internal sealed record PasswordLoginBody(string Email, string Password);

internal sealed record MfaBody(string PendingAuthenticationToken, string Code, string? ChallengeId);

internal sealed record AuthorizeBody(string Provider, string RedirectUri, string State, string? Email);

internal sealed record CallbackBody(string Code, string RedirectUri);

internal sealed record ForgotBody(string Email);

internal sealed record AuthBody(
    bool Ok,
    string? ErrorCode,
    UserSummary? User,
    IReadOnlyList<TenantMembershipSummary>? Tenants,
    bool MfaRequired,
    string? PendingAuthenticationToken,
    string? ChallengeId,
    IReadOnlyList<MfaFactor>? Factors);
