using D2DAudit.Api.Security;
using D2DAudit.Application;

namespace D2DAudit.Api.Endpoints;

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization("TenantRead").WithMetadata(new TenantScopedAttribute());
        group.MapGet("/integrations", (IIntegrationQuery query, CancellationToken cancellationToken) => query.ListAsync(cancellationToken)).RequireAuthorization("WorkspaceMember");
        group.MapPost("/integrations/{provider}/connect", Connect).RequireAuthorization("TenantWrite").Produces<ConnectResponse>();
        group.MapPost("/integrations/example", Example).RequireAuthorization("TenantWrite").Produces<IReadOnlyList<IntegrationDto>>();
        group.MapPost("/integrations/collect", Collect).RequireAuthorization("TenantWrite").Produces<Guid>();
        group.MapGet("/tests", (IIntegrationQuery query, CancellationToken cancellationToken) => query.TestsAsync(cancellationToken));
        group.MapGet("/evidence", (IIntegrationQuery query, CancellationToken cancellationToken) => query.EvidenceAsync(cancellationToken)).RequireAuthorization("EvidenceRead");
        group.MapGet("/evidence/{id:guid}/download", Download).RequireAuthorization("EvidenceRead");
        group.MapPost("/evidence/verify", (IIntegrationQuery query, CancellationToken cancellationToken) => query.VerifyAsync(cancellationToken)).RequireAuthorization("EvidenceRead");
        group.MapPost("/evidence", Upload).RequireAuthorization("TenantWrite").Produces<EvidenceListItem>();
        return app;
    }

    private static async Task<IResult> Connect(
        string provider,
        ConnectIntegrationRequest body,
        IIntegrationQuery query,
        CancellationToken cancellationToken)
    {
        var result = await query.ConnectAsync(provider, body, body.RedirectUri ?? "", cancellationToken);
        return EndpointResults.FromResult(result);
    }

    private static async Task<IResult> Example(ExampleFixtureBody body, IIntegrationQuery query, CancellationToken cancellationToken)
    {
        var result = await query.UseExampleFixturesAsync(body.Fixture, cancellationToken);
        return EndpointResults.FromResult(result);
    }

    private static async Task<IResult> Collect(IIntegrationQuery query, CancellationToken cancellationToken)
    {
        var result = await query.CollectAsync(cancellationToken);
        return EndpointResults.FromResult(result);
    }

    private static async Task<IResult> Upload(UploadEvidenceRequest body, IIntegrationQuery query, CancellationToken cancellationToken)
    {
        var result = await query.UploadAsync(body, cancellationToken);
        return EndpointResults.FromResult(result);
    }

    private static async Task<IResult> Download(Guid id, HttpContext http, IIntegrationQuery query, CancellationToken cancellationToken)
    {
        var result = await query.DownloadAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return EndpointResults.FromResult(result);
        }

        if (!string.IsNullOrWhiteSpace(result.Value.Sha256))
        {
            http.Response.Headers["X-Evidence-Sha256"] = result.Value.Sha256;
        }

        return Results.File(result.Value.Body, result.Value.ContentType, result.Value.FileName);
    }
}

internal sealed record ExampleFixtureBody(string Fixture);
