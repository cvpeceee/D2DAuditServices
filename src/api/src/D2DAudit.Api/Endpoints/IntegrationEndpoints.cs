using D2DAudit.Api.Security;
using D2DAudit.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace D2DAudit.Api.Endpoints;

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithMetadata(new TenantScopedAttribute());
        group.MapGet("/integrations", (IIntegrationQuery query, CancellationToken cancellationToken) => query.ListAsync(cancellationToken));
        group.MapPost("/integrations/{provider}/connect", Connect).RequireAuthorization("TenantWrite").Produces<ConnectResponse>();
        group.MapPost("/integrations/example", Example).RequireAuthorization("TenantWrite").Produces<IReadOnlyList<IntegrationDto>>();
        group.MapPost("/integrations/collect", Collect).RequireAuthorization("TenantWrite").Produces<Guid>();
        group.MapGet("/integrations/catalog", (string? category, string? search, IConnectorHub hub, CancellationToken cancellationToken) => hub.CatalogAsync(category, search, cancellationToken));
        group.MapGet("/integrations/catalog/{key}", async (string key, IConnectorHub hub, CancellationToken cancellationToken) => EndpointResults.FromResult(await hub.DetailAsync(key, cancellationToken))).Produces<ConnectorDetailDto>();
        group.MapGet("/integrations/catalog/{key}/history", async (string key, IConnectorHub hub, CancellationToken cancellationToken) => EndpointResults.FromResult(await hub.HistoryAsync(key, cancellationToken))).Produces<IReadOnlyList<ConnectorRunDto>>();
        group.MapPost("/integrations/catalog/{key}/connect", async (string key, ConnectConnectorRequest body, IConnectorHub hub, CancellationToken cancellationToken) => EndpointResults.FromResult(await hub.ConnectAsync(key, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<ConnectorActionResponse>();
        group.MapPost("/integrations/catalog/{key}/test", async (string key, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConnectConnectorRequest? body, IConnectorHub hub, CancellationToken cancellationToken) => EndpointResults.FromResult(await hub.TestAsync(key, body, cancellationToken))).RequireAuthorization("TenantWrite").Produces<ConnectorActionResponse>();
        group.MapPost("/integrations/catalog/{key}/sync", async (string key, IConnectorHub hub, CancellationToken cancellationToken) => EndpointResults.FromResult(await hub.SyncAsync(key, cancellationToken))).RequireAuthorization("TenantWrite").Produces<ConnectorActionResponse>();
        group.MapPost("/integrations/catalog/{key}/disconnect", async (string key, IConnectorHub hub, CancellationToken cancellationToken) => EndpointResults.FromResult(await hub.DisconnectAsync(key, cancellationToken))).RequireAuthorization("TenantWrite").Produces<ConnectorDetailDto>();
        group.MapGet("/tests", (IIntegrationQuery query, CancellationToken cancellationToken) => query.TestsAsync(cancellationToken));
        group.MapGet("/evidence", (IIntegrationQuery query, CancellationToken cancellationToken) => query.EvidenceAsync(cancellationToken));
        group.MapGet("/evidence/{id:guid}/download", Download);
        group.MapPost("/evidence/verify", (IIntegrationQuery query, CancellationToken cancellationToken) => query.VerifyAsync(cancellationToken));
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
