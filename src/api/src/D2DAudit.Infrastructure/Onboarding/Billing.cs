using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Onboarding;

public sealed class BillingOptions
{
    public string? SecretKey { get; init; }

    public string? WebhookSecret { get; init; }

    public IReadOnlyDictionary<string, string> PriceIds { get; init; } = new Dictionary<string, string>();

    public bool StripeConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    public static BillingOptions From(IConfiguration configuration)
    {
        static string? Read(IConfiguration configuration, string key) =>
            new[] { configuration[key], Environment.GetEnvironmentVariable(key) }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var prices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (plan, key) in new[] { (PlanCatalog.Starter, "STRIPE_PRICE_STARTER"), (PlanCatalog.Growth, "STRIPE_PRICE_GROWTH"), (PlanCatalog.Enterprise, "STRIPE_PRICE_ENTERPRISE") })
        {
            var value = Read(configuration, key);
            if (value is not null)
            {
                prices[plan] = value;
            }
        }

        return new BillingOptions
        {
            SecretKey = Read(configuration, "STRIPE_SECRET_KEY"),
            WebhookSecret = Read(configuration, "STRIPE_WEBHOOK_SECRET"),
            PriceIds = prices,
        };
    }
}

/// <summary>Default billing provider: plan changes apply immediately and invoices are generated locally.</summary>
public sealed class MockBillingProvider : IBillingProvider
{
    public string Name => "mock";

    public bool Configured => false;

    public Task<Result<BillingRedirect>> CreateCheckoutAsync(BillingCheckout checkout, CancellationToken cancellationToken) =>
        Task.FromResult(Result<BillingRedirect>.Ok(new BillingRedirect(true, null)));

    public Task<Result<BillingPortalResult>> CreatePortalAsync(string? customerId, string returnUrl, CancellationToken cancellationToken) =>
        Task.FromResult(Result<BillingPortalResult>.Ok(new BillingPortalResult(returnUrl + (returnUrl.Contains('?') ? "&" : "?") + "portal=mock")));

    public Task<IReadOnlyList<InvoiceDto>> InvoicesAsync(Guid tenantId, string plan, string? customerId, DateOnly today, CancellationToken cancellationToken)
    {
        var price = PlanCatalog.For(plan).MonthlyPriceUsd ?? 4500;
        var first = new DateOnly(today.Year, today.Month, 1);
        var prefix = tenantId.ToString("N")[..6].ToUpperInvariant();
        IReadOnlyList<InvoiceDto> invoices = Enumerable.Range(0, 3)
            .Select(offset =>
            {
                var issued = first.AddMonths(-offset);
                return new InvoiceDto(
                    $"mock_{prefix}_{issued:yyyyMM}",
                    $"MOCK-{prefix}-{issued:yyyyMM}",
                    price,
                    offset == 0 ? "open" : "paid",
                    issued,
                    null);
            })
            .ToArray();
        return Task.FromResult(invoices);
    }

    public BillingWebhookEvent? ParseWebhook(string payload, string? signatureHeader, DateTimeOffset now) => null;
}

/// <summary>Stripe adapter. Selected when STRIPE_SECRET_KEY is configured.</summary>
public sealed class StripeBillingProvider(HttpClient http, BillingOptions options, ILogger<StripeBillingProvider> logger) : IBillingProvider
{
    private const string Api = "https://api.stripe.com/v1/";

    public string Name => "stripe";

    public bool Configured => options.StripeConfigured;

    public async Task<Result<BillingRedirect>> CreateCheckoutAsync(BillingCheckout checkout, CancellationToken cancellationToken)
    {
        if (!options.PriceIds.TryGetValue(checkout.Plan, out var price))
        {
            return Result<BillingRedirect>.Fail("contact_sales");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "subscription"),
            new("line_items[0][price]", price),
            new("line_items[0][quantity]", "1"),
            new("success_url", checkout.SuccessUrl),
            new("cancel_url", checkout.CancelUrl),
            new("client_reference_id", checkout.TenantId.ToString()),
            new("metadata[tenant_id]", checkout.TenantId.ToString()),
            new("metadata[plan]", checkout.Plan),
            new("subscription_data[metadata][tenant_id]", checkout.TenantId.ToString()),
            new("subscription_data[metadata][plan]", checkout.Plan),
        };
        if (!string.IsNullOrWhiteSpace(checkout.CustomerId))
        {
            form.Add(new("customer", checkout.CustomerId));
        }
        else
        {
            form.Add(new("customer_email", checkout.Email));
        }

        var body = await PostAsync("checkout/sessions", form, cancellationToken);
        var url = body?.TryGetProperty("url", out var value) == true ? value.GetString() : null;
        return url is null ? Result<BillingRedirect>.Fail("billing_unavailable") : Result<BillingRedirect>.Ok(new BillingRedirect(false, url));
    }

    public async Task<Result<BillingPortalResult>> CreatePortalAsync(string? customerId, string returnUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Result<BillingPortalResult>.Fail("no_customer");
        }

        var body = await PostAsync("billing_portal/sessions", [new("customer", customerId), new("return_url", returnUrl)], cancellationToken);
        var url = body?.TryGetProperty("url", out var value) == true ? value.GetString() : null;
        return url is null ? Result<BillingPortalResult>.Fail("billing_unavailable") : Result<BillingPortalResult>.Ok(new BillingPortalResult(url));
    }

    public async Task<IReadOnlyList<InvoiceDto>> InvoicesAsync(Guid tenantId, string plan, string? customerId, DateOnly today, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Api + "invoices?limit=12&customer=" + Uri.EscapeDataString(customerId));
        Authorize(request);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Stripe invoice list returned {Status}.", (int)response.StatusCode);
                return [];
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.GetProperty("data").EnumerateArray().Select(item => new InvoiceDto(
                item.GetProperty("id").GetString() ?? "",
                item.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.String ? number.GetString()! : "",
                item.TryGetProperty("amount_due", out var amount) && amount.TryGetInt64(out var cents) ? cents / 100m : 0m,
                item.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "",
                DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(item.TryGetProperty("created", out var created) ? created.GetInt64() : 0).UtcDateTime),
                item.TryGetProperty("hosted_invoice_url", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null)).ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Stripe invoice list failed.");
            return [];
        }
    }

    public BillingWebhookEvent? ParseWebhook(string payload, string? signatureHeader, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookSecret) || !StripeSignature.Verify(payload, signatureHeader, options.WebhookSecret, now))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var id = root.GetProperty("id").GetString() ?? "";
            var type = root.GetProperty("type").GetString() ?? "";
            var data = root.GetProperty("data").GetProperty("object");
            string? Text(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            var metadata = data.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object ? meta : default;
            var tenantText = metadata.ValueKind == JsonValueKind.Object ? Text(metadata, "tenant_id") : null;
            tenantText ??= Text(data, "client_reference_id");
            var plan = metadata.ValueKind == JsonValueKind.Object ? Text(metadata, "plan") : null;
            var subscription = type.StartsWith("customer.subscription", StringComparison.Ordinal) ? Text(data, "id") : Text(data, "subscription");
            return new BillingWebhookEvent(
                id,
                type,
                Guid.TryParse(tenantText, out var tenantId) ? tenantId : null,
                plan,
                Text(data, "customer"),
                subscription,
                Text(data, "status"));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private void Authorize(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);

    private async Task<JsonElement?> PostAsync(string path, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api + path) { Content = new FormUrlEncodedContent(form) };
        Authorize(request);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Stripe {Path} returned {Status}.", path, (int)response.StatusCode);
                return null;
            }

            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Stripe {Path} failed.", path);
            return null;
        }
    }
}

public sealed class FeatureGateService(ControlPlaneDbContext control, TenantDatabases databases) : IFeatureGate
{
    public async Task<PlanEntitlements> EntitlementsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await control.Subscriptions.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Select(item => item.Plan)
            .FirstOrDefaultAsync(cancellationToken);
        return PlanCatalog.For(plan ?? PlanCatalog.Starter);
    }

    public async Task<GateDecision> CheckAsync(Guid tenantId, string feature, CancellationToken cancellationToken)
    {
        var entitlements = await EntitlementsAsync(tenantId, cancellationToken);
        var used = feature switch
        {
            PlanFeatures.Frameworks => await FrameworksUsedAsync(tenantId, cancellationToken),
            PlanFeatures.Seats => await SeatsUsedAsync(tenantId, cancellationToken),
            PlanFeatures.Integrations => await IntegrationsUsedAsync(tenantId, null, cancellationToken),
            _ => 0,
        };
        return FeatureGate.Check(entitlements, feature, used);
    }

    public async Task<IReadOnlyList<GateDecision>> UsageAsync(Guid tenantId, string? databaseName, CancellationToken cancellationToken)
    {
        var entitlements = await EntitlementsAsync(tenantId, cancellationToken);
        return
        [
            FeatureGate.Check(entitlements, PlanFeatures.Frameworks, await FrameworksUsedAsync(tenantId, cancellationToken)),
            FeatureGate.Check(entitlements, PlanFeatures.Seats, await SeatsUsedAsync(tenantId, cancellationToken)),
            FeatureGate.Check(entitlements, PlanFeatures.Integrations, await IntegrationsUsedAsync(tenantId, databaseName, cancellationToken)),
            FeatureGate.Check(entitlements, PlanFeatures.TrustCenter),
            FeatureGate.Check(entitlements, PlanFeatures.ApiAccess),
        ];
    }

    private async Task<int> FrameworksUsedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var stored = await control.TenantOnboarding.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Select(item => item.Frameworks)
            .FirstOrDefaultAsync(cancellationToken);
        return OnboardingPortal.Split(stored ?? "").Count;
    }

    private async Task<int> SeatsUsedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var members = await control.Memberships.CountAsync(item => item.TenantId == tenantId && item.Active && item.Role != TenantRole.Auditor, cancellationToken);
        var pending = await control.MemberInvites.CountAsync(item => item.TenantId == tenantId && item.AcceptedAt == null && item.RevokedAt == null && item.ExpiresAt > DateTimeOffset.UtcNow && item.Role != TenantRole.Auditor, cancellationToken);
        return members + pending;
    }

    private async Task<int> IntegrationsUsedAsync(Guid tenantId, string? databaseName, CancellationToken cancellationToken)
    {
        databaseName ??= await control.Tenants.AsNoTracking().Where(item => item.Id == tenantId).Select(item => item.DatabaseName).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return 0;
        }

        await using var db = databases.Create(databaseName);
        return await db.Integrations.CountAsync(cancellationToken);
    }
}

public sealed class BillingPortal(
    TenantContext tenant,
    ControlPlaneDbContext control,
    IBillingProvider provider,
    IFeatureGate gate,
    IActivityWriter activity,
    AppLinks links,
    TimeProvider time) : IBilling
{
    public async Task<BillingOverviewDto> OverviewAsync(CancellationToken cancellationToken)
    {
        var subscription = await LoadAsync(cancellationToken);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var invoices = await provider.InvoicesAsync(tenant.TenantId, subscription.Plan, subscription.CustomerId, today, cancellationToken);
        var usage = await gate.UsageAsync(tenant.TenantId, tenant.DatabaseName, cancellationToken);
        var periodEnd = subscription.CurrentPeriodEnd ?? new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
        return new BillingOverviewDto(
            subscription.Plan,
            subscription.Status,
            provider.Name,
            provider.Configured,
            PlanCatalog.For(subscription.Plan),
            usage,
            PlanCatalog.All,
            invoices,
            periodEnd);
    }

    public async Task<Result<ChangePlanResult>> ChangePlanAsync(ChangePlanRequest request, CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<ChangePlanResult>.Fail("forbidden");
        }

        if (!PlanCatalog.IsKnown(request.Plan))
        {
            return Result<ChangePlanResult>.Fail("invalid_plan");
        }

        var subscription = await LoadAsync(cancellationToken);
        if (subscription.Plan == request.Plan)
        {
            return Result<ChangePlanResult>.Ok(new ChangePlanResult(subscription.Plan, subscription.Status, null));
        }

        var redirect = await provider.CreateCheckoutAsync(
            new BillingCheckout(
                tenant.TenantId,
                request.Plan,
                tenant.Email,
                subscription.CustomerId,
                links.BaseUrl + "/settings/billing?checkout=success",
                links.BaseUrl + "/settings/billing?checkout=cancelled"),
            cancellationToken);
        if (!redirect.IsSuccess || redirect.Value is null)
        {
            return Result<ChangePlanResult>.Fail(redirect.Error ?? "billing_unavailable");
        }

        if (redirect.Value.Immediate)
        {
            var previous = subscription.Plan;
            subscription.Plan = request.Plan;
            subscription.Status = "Active";
            subscription.UpdatedAt = time.GetUtcNow();
            await control.SaveChangesAsync(cancellationToken);
            await activity.WriteAsync(tenant.UserId, tenant.TenantId, "billing.plan_changed", "ok", $"{previous}->{request.Plan}", null, cancellationToken);
            return Result<ChangePlanResult>.Ok(new ChangePlanResult(subscription.Plan, subscription.Status, null));
        }

        await activity.WriteAsync(tenant.UserId, tenant.TenantId, "billing.checkout_started", "ok", request.Plan, null, cancellationToken);
        return Result<ChangePlanResult>.Ok(new ChangePlanResult(subscription.Plan, subscription.Status, redirect.Value.Url));
    }

    public async Task<Result<BillingPortalResult>> PortalAsync(CancellationToken cancellationToken)
    {
        if (!tenant.IsAdmin)
        {
            return Result<BillingPortalResult>.Fail("forbidden");
        }

        var subscription = await LoadAsync(cancellationToken);
        return await provider.CreatePortalAsync(subscription.CustomerId, links.BaseUrl + "/settings/billing", cancellationToken);
    }

    private async Task<TenantSubscription> LoadAsync(CancellationToken cancellationToken)
    {
        var row = await control.Subscriptions.FirstOrDefaultAsync(item => item.TenantId == tenant.TenantId, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new TenantSubscription { TenantId = tenant.TenantId, Plan = PlanCatalog.Starter, Status = "Active", Provider = provider.Name, UpdatedAt = time.GetUtcNow() };
        control.Subscriptions.Add(row);
        await control.SaveChangesAsync(cancellationToken);
        return row;
    }
}

public sealed class BillingWebhooks(
    ControlPlaneDbContext control,
    IBillingProvider provider,
    IActivityWriter activity,
    TimeProvider time,
    ILogger<BillingWebhooks> logger) : IBillingWebhooks
{
    public async Task<Result<string>> HandleAsync(string payload, string? signatureHeader, CancellationToken cancellationToken)
    {
        if (!provider.Configured)
        {
            return Result<string>.Fail("not_found");
        }

        var now = time.GetUtcNow();
        var parsed = provider.ParseWebhook(payload, signatureHeader, now);
        if (parsed is null)
        {
            await activity.WriteAsync(null, null, "billing.webhook", "denied", "invalid_signature", null, cancellationToken);
            return Result<string>.Fail("invalid_signature");
        }

        if (await control.BillingEvents.AnyAsync(item => item.Id == parsed.Id, cancellationToken))
        {
            return Result<string>.Ok("duplicate");
        }

        control.BillingEvents.Add(new BillingEventRecord { Id = parsed.Id[..Math.Min(parsed.Id.Length, 255)], Type = parsed.Type, TenantId = parsed.TenantId, ReceivedAt = now });
        var subscription = await FindAsync(parsed, cancellationToken);
        if (subscription is not null)
        {
            Apply(subscription, parsed, now);
        }
        else
        {
            logger.LogInformation("Stripe event {Type} did not match a tenant subscription.", parsed.Type);
        }

        await control.SaveChangesAsync(cancellationToken);
        await activity.WriteAsync(null, subscription?.TenantId, "billing.webhook", "ok", parsed.Type, null, cancellationToken);
        return Result<string>.Ok(subscription is null ? "ignored" : "applied");
    }

    private async Task<TenantSubscription?> FindAsync(BillingWebhookEvent parsed, CancellationToken cancellationToken)
    {
        if (parsed.TenantId is Guid tenantId)
        {
            var byTenant = await control.Subscriptions.FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);
            if (byTenant is not null)
            {
                return byTenant;
            }

            if (await control.Tenants.AnyAsync(item => item.Id == tenantId, cancellationToken))
            {
                var created = new TenantSubscription { TenantId = tenantId, Plan = PlanCatalog.Starter, Provider = provider.Name };
                control.Subscriptions.Add(created);
                return created;
            }
        }

        if (!string.IsNullOrWhiteSpace(parsed.CustomerId))
        {
            return await control.Subscriptions.FirstOrDefaultAsync(item => item.CustomerId == parsed.CustomerId, cancellationToken);
        }

        return null;
    }

    private static void Apply(TenantSubscription subscription, BillingWebhookEvent parsed, DateTimeOffset now)
    {
        subscription.Provider = "stripe";
        subscription.CustomerId = parsed.CustomerId ?? subscription.CustomerId;
        subscription.SubscriptionId = parsed.SubscriptionId ?? subscription.SubscriptionId;
        switch (parsed.Type)
        {
            case "checkout.session.completed":
                if (PlanCatalog.IsKnown(parsed.Plan))
                {
                    subscription.Plan = parsed.Plan!;
                }

                subscription.Status = "Active";
                break;
            case "customer.subscription.updated":
            case "customer.subscription.created":
                if (PlanCatalog.IsKnown(parsed.Plan))
                {
                    subscription.Plan = parsed.Plan!;
                }

                subscription.Status = Capitalize(parsed.Status) ?? subscription.Status;
                break;
            case "customer.subscription.deleted":
                subscription.Plan = PlanCatalog.Starter;
                subscription.Status = "Canceled";
                break;
            case "invoice.payment_failed":
                subscription.Status = "PastDue";
                break;
        }

        subscription.UpdatedAt = now;
    }

    private static string? Capitalize(string? status) => status switch
    {
        null or "" => null,
        "past_due" => "PastDue",
        _ => char.ToUpper(status[0], CultureInfo.InvariantCulture) + status[1..],
    };
}
