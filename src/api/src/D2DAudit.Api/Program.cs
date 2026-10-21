using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using D2DAudit.Api.Hubs;
using D2DAudit.Api.Security;
using D2DAudit.Application;
using D2DAudit.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<IJobNotifier, SignalRJobNotifier>();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantWrite", policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantWriteRequirement()));
    options.AddPolicy("TenantAdmin", policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantAdminRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, TenantWriteHandler>();
builder.Services.AddScoped<IAuthorizationHandler, TenantAdminHandler>();
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
});
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddOpenApi();
var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
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
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
    const int status = StatusCodes.Status500InternalServerError;
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(
        new
        {
            title = "Something went wrong.",
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<CsrfMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHub<JobsHub>("/hubs/jobs");
app.MapOpenApi();

await app.RunAsync();

public partial class Program
{
    protected Program()
    {
    }
}
