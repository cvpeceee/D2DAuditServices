using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class AgentEndpoints
{
    private const int MaxBodyBytes = 32 * 1024;

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var anonymous = app.MapGroup("/api/v1/agent").AllowAnonymous().RequireRateLimiting("auth");
        anonymous.MapPost("/enroll", Enroll).Produces<AgentEnrollResult>();
        anonymous.MapPost("/check-in", CheckIn).Produces<AgentCheckInResult>();

        var group = app.MapGroup("/api/v1/agent").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapPost("/enrollment-tokens", CreateToken).Produces<EnrollmentTokenCreated>();
        group.MapGet("/enrollment-tokens", Tokens).Produces<IReadOnlyList<EnrollmentTokenItem>>();
        group.MapPost("/enrollment-tokens/{id:guid}/revoke", RevokeToken).Produces<EnrollmentTokenItem>();
        group.MapGet("/devices", Devices).Produces<IReadOnlyList<AgentDeviceItem>>();
        group.MapGet("/devices/{id:guid}/history", History).Produces<IReadOnlyList<PostureHistoryItem>>();
        group.MapPost("/devices/{id:guid}/revoke", RevokeDevice).RequireAuthorization("TenantWrite").Produces<AgentDeviceItem>();
        return app;
    }

    private static async Task<IResult> Enroll(AgentEnrollRequest body, IDeviceAgent agent, CancellationToken cancellationToken) =>
        FromCall(await agent.EnrollAsync(body, cancellationToken));

    private static async Task<IResult> CheckIn(HttpContext http, IDeviceAgent agent, CancellationToken cancellationToken)
    {
        if (http.Request.ContentLength is > MaxBodyBytes)
        {
            return Results.BadRequest(new { code = "payload_too_large" });
        }

        using var reader = new StreamReader(http.Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (body.Length > MaxBodyBytes)
        {
            return Results.BadRequest(new { code = "payload_too_large" });
        }

        var headers = http.Request.Headers;
        return FromCall(await agent.CheckInAsync(
            headers["X-D2D-Device"].FirstOrDefault(),
            headers["X-D2D-Timestamp"].FirstOrDefault(),
            headers["X-D2D-Signature"].FirstOrDefault(),
            body,
            cancellationToken));
    }

    private static async Task<IResult> CreateToken(CreateEnrollmentTokenRequest body, IDeviceAgent agent, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await agent.CreateTokenAsync(body, cancellationToken));

    private static async Task<IResult> Tokens(IDeviceAgent agent, CancellationToken cancellationToken) =>
        Results.Ok(await agent.TokensAsync(cancellationToken));

    private static async Task<IResult> RevokeToken(Guid id, IDeviceAgent agent, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await agent.RevokeTokenAsync(id, cancellationToken));

    private static async Task<IResult> Devices(bool? mine, IDeviceAgent agent, CancellationToken cancellationToken) =>
        Results.Ok(await agent.DevicesAsync(mine ?? false, cancellationToken));

    private static async Task<IResult> History(Guid id, IDeviceAgent agent, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await agent.HistoryAsync(id, cancellationToken));

    private static async Task<IResult> RevokeDevice(Guid id, IDeviceAgent agent, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await agent.RevokeDeviceAsync(id, cancellationToken));

    internal static IResult FromCall<T>(AgentCall<T> call)
    {
        if (call.Auth != AgentAuthFailure.None)
        {
            var code = call.Auth switch
            {
                AgentAuthFailure.Expired => "expired_signature",
                AgentAuthFailure.Replayed => "replayed",
                AgentAuthFailure.Invalid => "invalid_signature",
                _ => "unauthorized",
            };
            return Results.Json(new { code }, statusCode: StatusCodes.Status401Unauthorized);
        }

        return call.Error is null ? Results.Ok(call.Value) : Results.BadRequest(new { code = call.Error });
    }
}
