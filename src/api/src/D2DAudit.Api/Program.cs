using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using D2DAudit.Api.Endpoints;
using D2DAudit.Api.Hubs;
using D2DAudit.Api.Security;
using D2DAudit.Application;
using D2DAudit.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Fail at startup, not on the first request, when a service registration is missing.
builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = true);

builder.Services.AddSignalR();
builder.Services.AddSingleton<IJobNotifier, SignalRJobNotifier>();
builder.Services.AddSingleton<INotificationPush, SignalRNotificationPush>();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(TenantPolicies.Register);
builder.Services.AddScoped<IAuthorizationHandler, TenantRoleHandler>();
builder.Services.AddScoped<IAuthorizationHandler, TenantWriteHandler>();
builder.Services.AddScoped<IAuthorizationHandler, TenantAdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AuditActHandler>();
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceMemberHandler>();
EdgeProtection.AssertProductionSafe(builder.Configuration, builder.Environment);
builder.Services.AddD2DForwardedHeaders(builder.Configuration);
builder.Services.AddD2DRateLimiting(builder.Configuration, builder.Environment);
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
<<<<<<< HEAD
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
    });
}

=======
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsEnvironment("Testing") ? 1_000 : 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    options.AddPolicy("public_trust", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsEnvironment("Testing") ? 1_000 : 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    options.AddPolicy("signup", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsEnvironment("Testing") ? 1_000 : 10,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
            }));
});
>>>>>>> m11/onboarding
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.MaxDepth = 32;
});
builder.Services.AddOpenApi();
var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    if (!string.IsNullOrWhiteSpace(otelEndpoint))
    {
        logging.AddOtlpExporter();
    }
});
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("d2d-api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otelEndpoint))
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otelEndpoint))
        {
            metrics.AddOtlpExporter();
        }
    });
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.WebHost.ConfigureKestrel(options => options.ApplyD2DKestrelLimits(builder.Configuration));

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}

if (EdgeProtection.HasWildcardAllowedHosts(app.Configuration) && app.Environment.IsProduction())
{
    app.Logger.LogWarning("AllowedHosts is a wildcard; set it to the API host names behind the load balancer.");
}

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
    var concurrency = error is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException;
    var status = concurrency ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError;
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(
        new
        {
            title = concurrency ? "This record was changed by someone else. Reload and try again." : "Something went wrong.",
            status,
            detail = environment.IsDevelopment() || environment.IsEnvironment("Testing") ? error?.Message : null,
        },
        context.RequestAborted);
}));

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await next();
});
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseRequestTimeouts();
app.UseMiddleware<CsrfMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapWorkspaceEndpoints();
app.MapIntegrationEndpoints();
app.MapPolicyEndpoints();
app.MapAuditEndpoints();
app.MapGovernanceEndpoints();
app.MapProgramEndpoints();
app.MapOperationsEndpoints();
app.MapIncidentEndpoints();
app.MapChangeEndpoints();
app.MapQuestionnaireEndpoints();
app.MapSystemDescriptionEndpoints();
app.MapContinuityEndpoints();
app.MapVulnerabilityIngestEndpoints();
app.MapDeviceFleetEndpoints();
app.MapCloudSecurityEndpoints();
app.MapEnterpriseEndpoints();
app.MapGovernanceHubEndpoints();
app.MapInsightsEndpoints();
app.MapAssuranceEndpoints();
app.MapLifecycleEndpoints();
app.MapVendorRiskEndpoints();
app.MapOnboardingEndpoints();
app.MapAgentEndpoints();
app.MapAcademyEndpoints();
app.MapHub<JobsHub>("/hubs/jobs").DisableRequestTimeout();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
}

await app.RunAsync();

public partial class Program
{
    protected Program()
    {
    }
}
