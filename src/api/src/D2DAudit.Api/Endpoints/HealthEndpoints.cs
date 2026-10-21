using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var health = app.MapGroup("/health");
        health.MapGet("", () => Results.Ok(new { status = "ok" }));
        health.MapGet("/ready", Ready);
        return app;
    }

    private static async Task<IResult> Ready(IControlPlaneDirectory directory, CancellationToken cancellationToken)
    {
        var ready = await directory.CanConnectAsync(cancellationToken);
        return ready
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
