using System.Net.Http.Headers;
using System.Text.Json;
using D2DAudit.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Operations;

public sealed class MdmImport(HttpClient http, IConfiguration configuration, ILogger<MdmImport> logger) : IMdmImport
{
    public async Task<IReadOnlyList<DeviceReport>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = new List<DeviceReport>();
        rows.AddRange(await JamfAsync(cancellationToken));
        rows.AddRange(await IntuneAsync(cancellationToken));
        rows.AddRange(await KandjiAsync(cancellationToken));
        return rows;
    }

    private Task<IReadOnlyList<DeviceReport>> JamfAsync(CancellationToken cancellationToken) =>
        GetAsync(configuration["JAMF_BASE_URL"], configuration["JAMF_TOKEN"], "/api/v1/computers-inventory?page-size=100", "results", cancellationToken);

    private Task<IReadOnlyList<DeviceReport>> IntuneAsync(CancellationToken cancellationToken) =>
        GetAsync(configuration["INTUNE_GRAPH_URL"], configuration["INTUNE_TOKEN"], "/v1.0/deviceManagement/managedDevices", "value", cancellationToken);

    private Task<IReadOnlyList<DeviceReport>> KandjiAsync(CancellationToken cancellationToken) =>
        GetAsync(configuration["KANDJI_BASE_URL"], configuration["KANDJI_TOKEN"], "/api/v1/devices", null, cancellationToken);

    private async Task<IReadOnlyList<DeviceReport>> GetAsync(string? baseUrl, string? token, string path, string? arrayName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("MDM list failed with status {StatusCode}.", (int)response.StatusCode);
            return [];
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        if (arrayName is not null && root.TryGetProperty(arrayName, out var nested))
        {
            root = nested;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<DeviceReport>();
        foreach (var item in root.EnumerateArray())
        {
            var serial = First(item, "serialNumber", "serial_number", "SerialNumber");
            if (serial.Length == 0 && item.TryGetProperty("hardware", out var hardware))
            {
                serial = First(hardware, "serialNumber", "serial_number");
            }

            if (serial.Length == 0)
            {
                continue;
            }

            var owner = First(item, "email", "userPrincipalName", "username");
            var name = First(item, "name", "deviceName", "device_name");
            var system = First(item, "operatingSystem", "platform", "os");
            rows.Add(new DeviceReport(
                serial,
                system.Length == 0 ? "Unknown" : system,
                name,
                owner,
                Flag(item, "isEncrypted", "encrypted", "fileVaultEnabled"),
                Flag(item, "screenLock", "passcodePresent"),
                Flag(item, "antivirus", "antivirusEnabled"),
                Flag(item, "passwordManager"),
                Flag(item, "autoUpdate", "osUpdateEnabled")));
        }

        return rows;
    }

    private static string First(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? "";
                if (text.Length > 0)
                {
                    return text;
                }
            }
        }

        return "";
    }

    private static bool Flag(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.String && value.GetString() == "true")
            {
                return true;
            }
        }

        return false;
    }
}
