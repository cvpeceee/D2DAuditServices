using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class ContinuityEndpoints
{
    public static IEndpointRouteBuilder MapContinuityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/continuity", (IContinuity portal, CancellationToken cancellationToken) => portal.GetAsync(cancellationToken)).Produces<ContinuityBoard>();
        group.MapPost("/continuity/services", SaveService).RequireAuthorization("TenantWrite").Produces<ImpactItem>();
        group.MapPost("/continuity/plans", SavePlan).RequireAuthorization("TenantWrite").Produces<PlanItem>();
        group.MapPost("/continuity/exercises", SaveExercise).RequireAuthorization("TenantWrite").Produces<ExerciseItem>();
        return app;
    }

    private static async Task<IResult> SaveService(SaveImpactRequest body, IContinuity portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveServiceAsync(body, cancellationToken));

    private static async Task<IResult> SavePlan(SavePlanRequest body, IContinuity portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SavePlanAsync(body, cancellationToken));

    private static async Task<IResult> SaveExercise(SaveExerciseRequest body, IContinuity portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SaveExerciseAsync(body, cancellationToken));
}
