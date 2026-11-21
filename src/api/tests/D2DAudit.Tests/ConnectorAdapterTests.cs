using System.Net;
using System.Text;
using D2DAudit.Application;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Integrations.Connectors;
using D2DAudit.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace D2DAudit.Tests;

public sealed class ConnectorAdapterTests
{
    [Fact]
    public void Every_new_catalog_connector_has_a_registered_live_adapter()
    {
        using var provider = new ServiceCollection().AddConnectorCatalog().BuildServiceProvider();
        var registry = provider.GetRequiredService<ConnectorRegistry>();

        foreach (var descriptor in registry.All.Where(item => !item.Builtin))
        {
            Assert.NotNull(registry.Live(descriptor.Key));
            Assert.True(registry.Handles(descriptor.Key));
        }

        Assert.False(registry.Handles("aws"));
        Assert.Equal(IntegrationPolicies.DisplayName("okta"), registry.Find("okta")!.Name);
    }

    [Fact]
    public async Task Okta_adapter_follows_same_origin_pages_and_maps_mfa()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            "https://acme.okta.com/api/v1/users?limit=200" => Json(
                """[{"id":"u1","status":"ACTIVE","profile":{"email":"ada@acme.test","firstName":"Ada","lastName":"L"}}]""",
                ("Link", "<https://acme.okta.com/api/v1/users?after=u1>; rel=\"next\"")),
            "https://acme.okta.com/api/v1/users?after=u1" => Json(
                """[{"id":"u2","status":"DEPROVISIONED","profile":{"email":"bob@acme.test"}}]""",
                ("Link", "<https://evil.example/api/v1/users?after=u2>; rel=\"next\"")),
            "https://acme.okta.com/api/v1/users/u1/factors" => Json("""[{"status":"ACTIVE"}]"""),
            "https://acme.okta.com/api/v1/users/u2/factors" => Json("[]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var connector = new OktaConnector(new FakeFactory(handler));

        var result = await connector.CollectAsync(new Dictionary<string, string> { ["domain"] = "acme.okta.com", ["apiToken"] = "tok" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("true", result.Value[0].Attributes["mfa"]);
        Assert.Equal("true", result.Value[1].Attributes["offboarded"]);
        Assert.DoesNotContain(handler.Requests, uri => uri.Contains("evil.example", StringComparison.Ordinal));
        Assert.All(handler.Authorization, value => Assert.Equal("SSWS tok", value));
    }

    [Theory]
    [InlineData("http://acme.okta.com")]
    [InlineData("localhost")]
    [InlineData("https://user:pass@acme.okta.com")]
    public async Task Live_adapters_reject_unsafe_base_urls_before_any_request(string domain)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var connector = new OktaConnector(new FakeFactory(handler));

        var result = await connector.CollectAsync(new Dictionary<string, string> { ["domain"] = domain, ["apiToken"] = "tok" }, CancellationToken.None);

        Assert.Equal("unsafe_url", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Executor_encrypts_config_and_scrubs_secrets_from_upstream_errors()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TENANT_DATA_KEY"] = Convert.ToBase64String(new byte[32]) })
            .Build();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var registry = new ConnectorRegistry([new OktaConnector(new FakeFactory(handler))]);
        var executor = new ConnectorExecutor(registry, new SecretBox(configuration), configuration);
        var config = new Dictionary<string, string> { ["domain"] = "acme.okta.com", ["apiToken"] = "super-secret-token" };

        var sealedConfig = executor.Protect(config);
        var row = new D2DAudit.Domain.IntegrationConnection { Provider = "okta", Secret = sealedConfig.Value };
        var read = executor.ReadConfig(row);
        var outcome = await executor.CollectAsync(row, CancellationToken.None);
        var mocked = await executor.CollectAsync(registry.Find("okta")!, new Dictionary<string, string>(), example: false, CancellationToken.None);

        Assert.DoesNotContain("super-secret-token", sealedConfig.Value, StringComparison.Ordinal);
        Assert.Equal("super-secret-token", read.Value!["apiToken"]);
        Assert.Equal(ConnectorModes.Live, outcome.Mode);
        Assert.Equal("http_401", outcome.Result.Error);
        Assert.Equal(ConnectorModes.Mock, mocked.Mode);
        Assert.NotEmpty(mocked.Result.Value!);
    }

    private static HttpResponseMessage Json(string body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<string> Authorization { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Authorization.Add(request.Headers.Authorization?.ToString() ?? "");
            return Task.FromResult(respond(request));
        }
    }
}
