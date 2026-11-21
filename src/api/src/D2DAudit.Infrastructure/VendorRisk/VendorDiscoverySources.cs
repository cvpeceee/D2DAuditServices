using System.Net.Http.Headers;
using System.Text.Json;
using D2DAudit.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.VendorRisk;

/// <summary>Fixture vendors seen in an SSO app catalog and a corporate card expense feed.</summary>
public sealed class MockVendorDiscoverySource : IVendorDiscoverySource
{
    public string Name => "mock";

    public static readonly IReadOnlyList<DiscoveredVendor> Fixtures =
    [
        new("Figma", "SSO", "38 people sign in through single sign-on", "Medium", ["CustomerData"]),
        new("Notion", "SSO", "112 people sign in through single sign-on", "High", ["CustomerData", "EmployeeData"]),
        new("Loom", "SSO", "21 people sign in through single sign-on", "Low", ["EmployeeData"]),
        new("Datadog", "Expense", "Monthly card charge of USD 4,850", "High", ["CustomerData", "Credentials"]),
        new("Zapier", "Expense", "Monthly card charge of USD 299", "Medium", ["PersonalData"]),
        new("Calendly", "Expense", "Monthly card charge of USD 96", "Low", ["PersonalData"]),
    ];

    public Task<IReadOnlyList<DiscoveredVendor>> DiscoverAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Fixtures);
}

/// <summary>
/// Reads an expense feed from EXPENSE_FEED_URL (optional bearer EXPENSE_FEED_TOKEN) when configured.
/// The feed returns a JSON array of { "merchant", "amount", "currency" } rows. Falls back to the fixtures.
/// </summary>
public sealed class ConfigurableVendorDiscoverySource(
    HttpClient http,
    IConfiguration configuration,
    ILogger<ConfigurableVendorDiscoverySource> logger) : IVendorDiscoverySource
{
    private readonly MockVendorDiscoverySource mock = new();

    public string Name => Configured ? "expense-feed" : mock.Name;

    private bool Configured => Uri.TryCreate(configuration["EXPENSE_FEED_URL"], UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public async Task<IReadOnlyList<DiscoveredVendor>> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return await mock.DiscoverAsync(cancellationToken);
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, configuration["EXPENSE_FEED_URL"]);
            var token = configuration["EXPENSE_FEED_TOKEN"];
            if (!string.IsNullOrWhiteSpace(token))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await http.SendAsync(message, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Select(Row)
                .Where(row => row is not null)
                .Select(row => row!)
                .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(200)
                .ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Expense feed could not be read for vendor discovery.");
            return [];
        }
    }

    private static DiscoveredVendor? Row(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("merchant", out var merchant)
            || merchant.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var name = merchant.GetString()!.Trim();
        if (name.Length is 0 or > 200)
        {
            return null;
        }

        var amount = item.TryGetProperty("amount", out var value) && value.TryGetDecimal(out var parsed) ? parsed : 0m;
        var currency = item.TryGetProperty("currency", out var cur) && cur.ValueKind == JsonValueKind.String ? cur.GetString() : "USD";
        return new DiscoveredVendor(name, "Expense", $"Card charge of {currency} {amount:0.##}", amount >= 1000 ? "High" : "Medium", []);
    }
}
