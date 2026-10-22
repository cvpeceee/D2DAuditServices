using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/policies", (IPolicyPortal portal, CancellationToken cancellationToken) => portal.ListAsync(cancellationToken))
            .Produces<IReadOnlyList<PolicyListItem>>();
        group.MapPost("/policies/library", Install).RequireAuthorization("TenantWrite").Produces<int>();
        group.MapPost("/policies/reminders", Remind).RequireAuthorization("TenantWrite").Produces<int>();
        group.MapGet("/policies/{code}", Get).Produces<PolicyDetail>();
        group.MapPost("/policies/{code}/draft", Draft).RequireAuthorization("TenantWrite").Produces<PolicyDetail>();
        group.MapPost("/policies/{code}/submit", Submit).RequireAuthorization("TenantWrite").Produces<PolicyDetail>();
        group.MapPost("/policies/{code}/approve", Approve).RequireAuthorization("TenantAdmin").Produces<PolicyDetail>();
        group.MapPost("/policies/{code}/acknowledgements", Assign).RequireAuthorization("TenantWrite").Produces<PolicyDetail>();
        group.MapPost("/policies/{code}/acknowledge", Acknowledge).Produces<PolicyDetail>();
        return app;
    }

    private static async Task<IResult> Install(IPolicyPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.InstallLibraryAsync(cancellationToken));

    private static async Task<IResult> Remind(IPolicyPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.RemindAsync(cancellationToken));

    private static async Task<IResult> Get(string code, IPolicyPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.GetAsync(code, cancellationToken));

    private static async Task<IResult> Draft(string code, IPolicyPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.DraftAsync(code, cancellationToken));

    private static async Task<IResult> Submit(string code, IPolicyPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.SubmitAsync(code, cancellationToken));

    private static async Task<IResult> Approve(string code, IPolicyPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.ApproveAsync(code, cancellationToken));

    private static async Task<IResult> Assign(string code, AssignAcknowledgementRequest body, IPolicyPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.AssignAcknowledgementAsync(code, body, cancellationToken));

    private static async Task<IResult> Acknowledge(string code, IPolicyPortal portal, CancellationToken cancellationToken) =>
        EndpointResults.FromResult(await portal.AcknowledgeAsync(code, cancellationToken));
}
