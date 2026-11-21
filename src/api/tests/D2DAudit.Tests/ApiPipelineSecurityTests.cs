using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure;
using D2DAudit.Infrastructure.Audits;
using D2DAudit.Infrastructure.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace D2DAudit.Tests;

public sealed class ApiPipelineSecurityTests : IClassFixture<ApiPipelineSecurityTests.InMemoryPipelineFactory>
{
    private readonly InMemoryPipelineFactory _factory;

    public ApiPipelineSecurityTests(InMemoryPipelineFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Api_key_authenticates_bypasses_csrf_and_enforces_scopes_and_tenant_isolation()
    {
        var client = _factory.CreateClient();

        // Write-scoped API key succeeds on POST without X-D2D-Request CSRF header.
        using var writeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/security/exposure/scan");
        writeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.WriteApiKey);
        var writeResponse = await client.SendAsync(writeRequest);
        Assert.Equal(HttpStatusCode.OK, writeResponse.StatusCode);

        // Read-only API key can read exposure board, but gets 403 on write endpoint.
        using var readGet = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/exposure");
        readGet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.ReadApiKey);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(readGet)).StatusCode);

        using var readPost = new HttpRequestMessage(HttpMethod.Post, "/api/v1/security/exposure/scan");
        readPost.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.ReadApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(readPost)).StatusCode);

        // API key cannot cross into another tenant via X-Tenant-Id header.
        using var crossTenant = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/exposure");
        crossTenant.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.ReadApiKey);
        crossTenant.Headers.Add("X-Tenant-Id", InMemoryPipelineFactory.OtherTenantId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(crossTenant)).StatusCode);

        // Invalid/revoked API key cannot bypass CSRF or authenticate.
        using var revoked = new HttpRequestMessage(HttpMethod.Post, "/api/v1/security/exposure/scan");
        revoked.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "d2d_api_00000000000000000000000000000000_revoked");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(revoked)).StatusCode);
    }

    [Fact]
    public async Task Cookie_session_requires_csrf_header_on_mutations()
    {
        var client = _factory.CreateClient();
        using var noCsrf = new HttpRequestMessage(HttpMethod.Post, "/api/v1/security/exposure/scan");
        noCsrf.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.AdminSessionToken);
        noCsrf.Headers.Add("X-Tenant-Id", InMemoryPipelineFactory.TenantAId.ToString());
        var response = await client.SendAsync(noCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("csrf", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET", "/api/v1/security/exposure")]
    [InlineData("GET", "/api/v1/security/scans")]
    [InlineData("GET", "/api/v1/security/dspm")]
    [InlineData("GET", "/api/v1/security/finops")]
    [InlineData("GET", "/api/v1/security/attack-paths")]
    [InlineData("GET", "/api/v1/trust")]
    [InlineData("GET", "/api/v1/computers")]
    [InlineData("GET", "/api/v1/vulnerabilities")]
    [InlineData("GET", "/api/v1/notifications")]
    [InlineData("GET", "/api/v1/devices")]
    [InlineData("GET", "/api/v1/resources")]
    [InlineData("GET", "/api/v1/frameworks/iso27001/simulator")]
    [InlineData("GET", "/api/v1/governance/overview")]
    [InlineData("GET", "/api/v1/governance/entities")]
    [InlineData("GET", "/api/v1/governance/time-machine")]
    [InlineData("POST", "/api/v1/governance/checkr/sync")]
    [InlineData("POST", "/api/v1/scim/v2/Users/user%40acme.example/deprovision")]
    [InlineData("POST", "/api/v1/incidents")]
    [InlineData("POST", "/api/v1/me/cards")]
    public async Task External_auditor_is_forbidden_from_internal_security_trust_and_employee_endpoints(string method, string path)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.AuditorSessionToken);
        request.Headers.Add("X-D2D-Request", "1");
        request.Headers.Add("X-Tenant-Id", InMemoryPipelineFactory.TenantAId.ToString());
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { title = "x", severity = "Low", description = "y", kind = "Mfa", question = "secrets?" });
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_read_trust_admin_or_test_webhooks_but_admin_can()
    {
        var client = _factory.CreateClient();

        using var employeeTrust = new HttpRequestMessage(HttpMethod.Get, "/api/v1/trust");
        employeeTrust.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.EmployeeSessionToken);
        employeeTrust.Headers.Add("X-Tenant-Id", InMemoryPipelineFactory.TenantAId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(employeeTrust)).StatusCode);

        var webhookId = Guid.NewGuid();
        using var employeeWebhookTest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/developer/webhooks/{webhookId}/test");
        employeeWebhookTest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.EmployeeSessionToken);
        employeeWebhookTest.Headers.Add("X-D2D-Request", "1");
        employeeWebhookTest.Headers.Add("X-Tenant-Id", InMemoryPipelineFactory.TenantAId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(employeeWebhookTest)).StatusCode);

        using var adminWebhookTest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/developer/webhooks/{webhookId}/test");
        adminWebhookTest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InMemoryPipelineFactory.AdminSessionToken);
        adminWebhookTest.Headers.Add("X-D2D-Request", "1");
        adminWebhookTest.Headers.Add("X-Tenant-Id", InMemoryPipelineFactory.TenantAId.ToString());
        var ok = await client.SendAsync(adminWebhookTest);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dto = await ok.Content.ReadFromJsonAsync<WebhookTestResultDto>();
        Assert.NotNull(dto);
        Assert.True(dto.Delivered);
        Assert.Equal("webhook.test", dto.EventName);
    }

    [Fact]
    public async Task Public_trust_download_reads_X_Trust_Token_header_and_returns_nda_required_without_it()
    {
        var client = _factory.CreateClient();
        var docId = Guid.NewGuid();

        var denied = await client.GetAsync($"/api/v1/public/trust/acme/documents/{docId}/download");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        Assert.Contains("nda_required", deniedBody, StringComparison.Ordinal);

        using var approved = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/public/trust/acme/documents/{docId}/download");
        approved.Headers.Add("X-Trust-Token", "approved-nda-token");
        var ok = await client.SendAsync(approved);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(ok.Headers.TryGetValues("X-Document-Sha256", out var sha));
        Assert.Contains("abc123sha", sha);
    }

    [Fact]
    public async Task OpenApi_v1_document_exposes_current_m7_m8_m9_endpoints()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/security/exposure", out _));
        Assert.True(paths.TryGetProperty("/api/v1/security/attack-paths", out _));
        Assert.True(paths.TryGetProperty("/api/v1/security/dspm", out _));
        Assert.True(paths.TryGetProperty("/api/v1/security/finops", out _));
        Assert.True(paths.TryGetProperty("/api/v1/developer/webhooks/{id}/test", out _));
        Assert.True(paths.TryGetProperty("/api/v1/public/trust/{slug}/receipt", out _));
        Assert.True(paths.TryGetProperty("/api/v1/evidence/{id}/download", out _));
        Assert.True(paths.TryGetProperty("/api/v1/governance/overview", out _));
        Assert.True(paths.TryGetProperty("/api/v1/governance/entities", out _));
        Assert.True(paths.TryGetProperty("/api/v1/governance/checkr/sync", out _));
        Assert.True(paths.TryGetProperty("/api/v1/governance/knowbe4/sync", out _));
        Assert.True(paths.TryGetProperty("/api/v1/governance/remediation-prs", out _));
        Assert.True(paths.TryGetProperty("/api/v1/governance/time-machine", out _));
        Assert.True(paths.TryGetProperty("/api/v1/scim/v2/Users/{email}/deprovision", out _));

        if (string.Equals(Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT"), "1", StringComparison.Ordinal))
        {
            var root = FindRepoRoot();
            if (root is not null)
            {
                var target = Path.Combine(root, "src", "web", "openapi", "v1.json");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, json, new UTF8Encoding(false));
            }
        }
    }

    private static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "web")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public sealed class InMemoryPipelineFactory : WebApplicationFactory<Program>
    {
        public static readonly Guid TenantAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public const string AdminSessionToken = "session-admin";
        public const string EmployeeSessionToken = "session-employee";
        public const string AuditorSessionToken = "session-auditor";
        public const string ReadApiKey = "d2d_api_11111111111111111111111111111111_readsecret0000000000000000000000";
        public const string WriteApiKey = "d2d_api_11111111111111111111111111111111_writesecret000000000000000000000";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlane", "Host=127.0.0.1;Database=d2d_unused_stub;Username=stub;Password=stub");
            builder.ConfigureServices(services =>
            {
                var hosted = services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToArray();
                foreach (var descriptor in hosted)
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<IIdentityProvider>();
                services.RemoveAll<IApiKeyAuthenticator>();
                services.RemoveAll<IControlPlaneDirectory>();
                services.RemoveAll<IActivityWriter>();
                services.RemoveAll<ICloudSecurity>();
                services.RemoveAll<IEnterprisePortal>();
                services.RemoveAll<IGovernanceHub>();
                services.RemoveAll<IOperations>();
                services.RemoveAll<IIncidents>();
                services.RemoveAll<IProgram>();
                services.RemoveAll<IClientRead>();

                services.AddSingleton<IIdentityProvider, StubIdentityProvider>();
                services.AddSingleton<IApiKeyAuthenticator, StubApiKeyAuthenticator>();
                services.AddSingleton<IControlPlaneDirectory, StubDirectory>();
                services.AddSingleton<IActivityWriter, StubActivityWriter>();
                services.AddSingleton<ICloudSecurity, StubCloudSecurity>();
                services.AddSingleton<IEnterprisePortal, StubEnterprisePortal>();
                services.AddSingleton<IGovernanceHub, StubGovernanceHub>();
                services.AddSingleton<IOperations, StubOperations>();
                services.AddSingleton<IIncidents, StubIncidents>();
                services.AddSingleton<IProgram, StubProgram>();
                services.AddSingleton<IClientRead, StubClientRead>();
            });
        }
    }

    private sealed class StubIdentityProvider : IIdentityProvider
    {
        public IdentityMode Mode => IdentityMode.Local;

        public Task<AccessTokenIdentity?> ValidateAccessTokenAsync(string token, CancellationToken cancellationToken) => token switch
        {
            InMemoryPipelineFactory.AdminSessionToken => Task.FromResult<AccessTokenIdentity?>(new("sub-admin", DateTimeOffset.UtcNow.AddHours(1))),
            InMemoryPipelineFactory.EmployeeSessionToken => Task.FromResult<AccessTokenIdentity?>(new("sub-employee", DateTimeOffset.UtcNow.AddHours(1))),
            InMemoryPipelineFactory.AuditorSessionToken => Task.FromResult<AccessTokenIdentity?>(new("sub-auditor", DateTimeOffset.UtcNow.AddHours(1))),
            _ => Task.FromResult<AccessTokenIdentity?>(null),
        };

        public Task<IdentityLoginResult> PasswordLoginAsync(string email, string password, string? ip, string? userAgent, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IdentityLoginResult> CompleteMfaAsync(string pendingToken, string code, string? challengeId, string? ip, string? userAgent, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AuthorizationRedirect> GetAuthorizationUrlAsync(ExternalLoginStart start, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IdentityLoginResult> CompleteAuthorizationCodeAsync(string code, string redirectUri, string? ip, string? userAgent, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IdentityLoginResult> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class StubApiKeyAuthenticator : IApiKeyAuthenticator
    {
        public Task<ApiKeyIdentity?> ValidateAsync(string token, CancellationToken cancellationToken) => token switch
        {
            InMemoryPipelineFactory.ReadApiKey => Task.FromResult<ApiKeyIdentity?>(new(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "CI Read Key",
                "admin@acme.example",
                "read:controls,read:evidence",
                InMemoryPipelineFactory.TenantAId,
                "Acme",
                "US",
                false,
                "tenant_acme")),
            InMemoryPipelineFactory.WriteApiKey => Task.FromResult<ApiKeyIdentity?>(new(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "CI Write Key",
                "admin@acme.example",
                "read:controls,write:security",
                InMemoryPipelineFactory.TenantAId,
                "Acme",
                "US",
                false,
                "tenant_acme")),
            _ => Task.FromResult<ApiKeyIdentity?>(null),
        };
    }

    private sealed class StubDirectory : IControlPlaneDirectory
    {
        private static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        private static readonly Guid EmployeeId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        private static readonly Guid AuditorId = Guid.Parse("00000000-0000-0000-0000-000000000003");

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<DirectoryUser?> FindUserBySubjectAsync(string subject, CancellationToken cancellationToken) => subject switch
        {
            "sub-admin" => Task.FromResult<DirectoryUser?>(new(AdminId, "admin@acme.example", "Admin")),
            "sub-employee" => Task.FromResult<DirectoryUser?>(new(EmployeeId, "emp@acme.example", "Employee")),
            "sub-auditor" => Task.FromResult<DirectoryUser?>(new(AuditorId, "auditor@firm.example", "Auditor")),
            _ => Task.FromResult<DirectoryUser?>(null),
        };

        public Task<IReadOnlyList<DirectoryMembership>> MembershipsAsync(Guid userId, CancellationToken cancellationToken)
        {
            var role = TenantRole.Auditor;
            if (userId == AdminId)
            {
                role = TenantRole.Admin;
            }
            else if (userId == EmployeeId)
            {
                role = TenantRole.Employee;
            }

            IReadOnlyList<DirectoryMembership> list =
            [
                new(InMemoryPipelineFactory.TenantAId, "Acme", "US", false, TenantStatus.Active, role, "tenant_acme"),
            ];
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<DirectoryGrant>> ActiveGrantsAsync(Guid userId, DateOnly today, CancellationToken cancellationToken)
        {
            IReadOnlyList<DirectoryGrant> list = userId == AuditorId
                ? [new(InMemoryPipelineFactory.TenantAId, ["soc2"])]
                : [];
            return Task.FromResult(list);
        }
    }

    private sealed class StubActivityWriter : IActivityWriter
    {
        public Task WriteAsync(Guid? actorUserId, Guid? tenantId, string action, string result, string? detail, string? ipAddress, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubCloudSecurity : ICloudSecurity
    {
        private static readonly ExposureBoardDto EmptyExposure = new(0, 0, 0, 0, 0, []);

        public Task<ExposureBoardDto> ExposureBoardAsync(CancellationToken cancellationToken) => Task.FromResult(EmptyExposure);
        public Task<Result<ExposureBoardDto>> ScanExposureAsync(CancellationToken cancellationToken) => Task.FromResult(Result<ExposureBoardDto>.Ok(EmptyExposure));
        public Task<Result<ExposureItemDto>> ResolveExposureAsync(Guid id, ResolveFindingRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SecurityScansBoardDto> ScansBoardAsync(CancellationToken cancellationToken) => Task.FromResult(new SecurityScansBoardDto(0, 0, 0, [], [], []));
        public Task<Result<int>> IngestSarifAsync(IngestSarifRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<int>.Ok(0));
        public Task<Result<ScanFindingDto>> ResolveScanAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DspmBoardDto> DspmBoardAsync(CancellationToken cancellationToken) => Task.FromResult(new DspmBoardDto(0, 0, 0, 0, []));
        public Task<Result<DataStoreDto>> SaveDataStoreAsync(SaveDataStoreRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<DspmBoardDto>> ScanDspmAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<FinOpsBoardDto> FinOpsBoardAsync(CancellationToken cancellationToken) => Task.FromResult(new FinOpsBoardDto(new FinOpsSummary(0, 0, 0, 0, 0, 0), []));
        public Task<Result<CostRecommendationDto>> UpdateCostStatusAsync(Guid id, UpdateCostStatusRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AttackPathsBoardDto> AttackPathsAsync(CancellationToken cancellationToken) => Task.FromResult(new AttackPathsBoardDto(0, 0, 0, []));
    }

    private sealed class StubEnterprisePortal : IEnterprisePortal
    {
        public Task<Result<TrustDownloadFile>> DownloadTrustDocumentAsync(string slug, Guid documentId, string? accessToken, CancellationToken cancellationToken) =>
            string.Equals(accessToken, "approved-nda-token", StringComparison.Ordinal)
                ? Task.FromResult(Result<TrustDownloadFile>.Ok(new TrustDownloadFile(Encoding.UTF8.GetBytes("soc2-pdf"), "soc2.pdf", "application/pdf", "abc123sha")))
                : Task.FromResult(Result<TrustDownloadFile>.Fail("nda_required"));

        public Task<Result<WebhookTestResultDto>> TestWebhookAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Result<WebhookTestResultDto>.Ok(new WebhookTestResultDto(true, 200, "webhook.test", "t=1,v1=abc", "delivered:200")));

        public Task<Result<FrameworkSimulationResult>> SimulateFrameworkAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(Result<FrameworkSimulationResult>.Ok(new FrameworkSimulationResult(key, key, 1, 1, 1, 100, 100, [])));

        public Task<Result<int>> AdoptFrameworkGapsAsync(string key, AdoptGapsRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<int>.Ok(0));
        public Task<Result<CustomFrameworkCreatedDto>> CreateCustomFrameworkAsync(CreateCustomFrameworkRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TrustReceiptDocument?> TrustReceiptAsync(string slug, CancellationToken cancellationToken) => Task.FromResult<TrustReceiptDocument?>(null);
        public Task<Result<AssistantReply>> AskTrustPublicAsync(string slug, AssistantAskRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<AssistantReply>> AskWorkspaceAsync(AssistantAskRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<AssistantReply>.Ok(new AssistantReply("ok", "stub", [], [])));
        public Task<IReadOnlyList<ApiKeyDto>> ListApiKeysAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ApiKeyDto>>([]);
        public Task<Result<CreatedApiKeyDto>> CreateApiKeyAsync(CreateApiKeyRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<bool>> RevokeApiKeyAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Result<bool>.Ok(true));
        public Task<IReadOnlyList<WebhookDto>> ListWebhooksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WebhookDto>>([]);
        public Task<Result<CreatedWebhookDto>> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<bool>> DeleteWebhookAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Result<bool>.Ok(true));
    }

    private sealed class StubOperations : IOperations
    {
        public Task<IReadOnlyList<ComputerItem>> ComputersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ComputerItem>>([]);
        public Task<Result<ComputerItem>> SaveComputerAsync(SaveComputerRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<ComputerItem>> UploadScreenshotAsync(Guid id, ScreenshotRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<VulnerabilityBoard> VulnerabilitiesAsync(CancellationToken cancellationToken) => Task.FromResult(new VulnerabilityBoard(new VulnerabilitySlaDto(7, 30, 60, 90), [], [], [], []));
        public Task<Result<VulnerabilityItem>> IgnoreVulnerabilityAsync(Guid id, IgnoreVulnerabilityRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<VulnerabilitySlaDto>> SaveSlaAsync(VulnerabilitySlaDto request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TrustAdminPage> TrustAdminAsync(CancellationToken cancellationToken) => Task.FromResult(new TrustAdminPage("acme", true, "H", "S", "sec@acme.example", [], []));
        public Task<Result<TrustAdminPage>> SaveTrustAsync(SaveTrustRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<TrustDocumentDto>> SaveTrustDocumentAsync(SaveTrustDocumentRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<TrustRequestDto>> DecideTrustAsync(Guid id, DecideTrustRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TrustPublicPage?> TrustPublicAsync(string slug, CancellationToken cancellationToken) => Task.FromResult<TrustPublicPage?>(null);
        public Task<Result<TrustRequestDto>> RequestTrustAccessAsync(string slug, TrustAccessRequestBody request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<AutomationRuleDto>> RulesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationRuleDto>>([]);
        public Task<IReadOnlyList<AutomationDeliveryDto>> DeliveriesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationDeliveryDto>>([]);
        public Task<Result<AutomationRuleDto>> SaveRuleAsync(SaveAutomationRuleRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<AutomationDeliveryDto>>> RunDueAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<CreatedControl>> CreateControlAsync(CreateControlRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<int>> ImportControlsAsync(ImportControlsRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class StubIncidents : IIncidents
    {
        public Task<IReadOnlyList<IncidentItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IncidentItem>>([]);
        public Task<Result<IncidentItem>> ReportAsync(ReportIncidentRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<IncidentItem>> UpdateAsync(Guid id, UpdateIncidentRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class StubProgram : IProgram
    {
        public Task<IReadOnlyList<MeCard>> CardsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MeCard>>([]);
        public Task<Result<MeCard>> CompleteCardAsync(CompleteCardRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<TrainingModuleItem>> ModulesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrainingModuleItem>>([]);
        public Task<Result<TrainingModuleDetail>> ModuleAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TrainingPersonPage> TrainingPeopleAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<QuizResult>> SubmitQuizAsync(Guid id, QuizRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<QuizResult>> UploadCertificateAsync(Guid id, CertificateRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AssetPage> AssetsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<AssetItem>> SaveAssetAsync(SaveAssetRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<int>> ImportAssetsAsync(AssetImportRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<FileExport>> ExportAssetsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<WorkTaskItem>> TasksAsync(bool mine, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkTaskItem>>([]);
        public Task<Result<WorkTaskItem>> SaveTaskAsync(SaveTaskRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<WorkTaskItem>> FinishTaskAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<MemberItem>> MembersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MemberItem>>([]);
        public Task<Result<MemberItem>> InviteAsync(InviteRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<MemberItem>> SetRoleAsync(Guid id, RoleRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<MemberItem>> DeactivateAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<SsoStatus>> SsoAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SsoStatus>>([]);
        public Task<IReadOnlyList<GrantItem>> GrantsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GrantItem>>([]);
        public Task<Result<GrantItem>> SaveGrantAsync(SaveGrantRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class StubClientRead : IClientRead
    {
        public Task<IReadOnlyList<NotificationDto>> NotificationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationDto>>([]);
        public Task<IReadOnlyList<DeviceDto>> DevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceDto>>([]);
        public Task<IReadOnlyList<ResourceDto>> ResourcesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ResourceDto>>([]);
        public Task<IReadOnlyList<HistoryPoint>> HistoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoryPoint>>([]);
        public Task<int> DeviceCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class StubGovernanceHub : IGovernanceHub
    {
        private static readonly TimeMachineComparison EmptyTm = TimeMachineCalculator.Compare(new DateOnly(2026, 8, 1), new DateOnly(2026, 11, 20), [], [], 0, 0);

        public Task<GovernanceHubOverviewDto> OverviewAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GovernanceHubOverviewDto(new EntityRollupDto(EntityRollupCalculator.Summarize([]), []), [], [], [], [], [], [], EmptyTm));
        public Task<EntityRollupDto> ListEntitiesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new EntityRollupDto(EntityRollupCalculator.Summarize([]), []));
        public Task<Result<LegalEntityDto>> SaveEntityAsync(SaveEntityRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<SyncIntegrationResultDto>> SyncCheckrAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<SyncIntegrationResultDto>.Ok(new SyncIntegrationResultDto("Checkr", "stub", 0, "ok", DateTimeOffset.UtcNow)));
        public Task<Result<SyncIntegrationResultDto>> SyncKnowBe4Async(CancellationToken cancellationToken) =>
            Task.FromResult(Result<SyncIntegrationResultDto>.Ok(new SyncIntegrationResultDto("KnowBe4", "stub", 0, "ok", DateTimeOffset.UtcNow)));
        public Task<Result<ScimDeprovisionResultDto>> DeprovisionScimUserAsync(ScimDeprovisionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ScimDeprovisionResultDto>.Ok(new ScimDeprovisionResultDto(request.Email, "Stub", "Offboarded", false, 0, "Pass", "ok", "Pass", DateTimeOffset.UtcNow)));
        public Task<IReadOnlyList<PrivacyAssessmentDto>> ListPrivacyAssessmentsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PrivacyAssessmentDto>>([]);
        public Task<Result<PrivacyAssessmentDto>> SavePrivacyAssessmentAsync(SavePrivacyAssessmentRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<AiModelDto>> ListAiModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AiModelDto>>([]);
        public Task<Result<AiModelDto>> SaveAiModelAsync(SaveAiModelRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<PhishingCampaignDto>> ListPhishingCampaignsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PhishingCampaignDto>>([]);
        public Task<Result<PhishingCampaignDto>> CreatePhishingCampaignAsync(CreatePhishingCampaignRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<AttackSurfaceAssetDto>> ListAttackSurfaceAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AttackSurfaceAssetDto>>([]);
        public Task<Result<AttackSurfaceAssetDto>> SaveAttackSurfaceAssetAsync(SaveAttackSurfaceAssetRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<AuditorFirmDto>> ListAuditorNetworkAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditorFirmDto>>([]);
        public Task<Result<AuditorFirmDto>> InviteAuditorFirmAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<RemediationPrDto>> ListRemediationPrsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RemediationPrDto>>([]);
        public Task<Result<RemediationPrDto>> CreateRemediationPrAsync(CreateRemediationPrRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<TimeMachineComparison>> TimeMachineAsync(DateOnly? asOf, DateOnly? compareTo, CancellationToken cancellationToken) =>
            Task.FromResult(Result<TimeMachineComparison>.Ok(EmptyTm));
    }
}
