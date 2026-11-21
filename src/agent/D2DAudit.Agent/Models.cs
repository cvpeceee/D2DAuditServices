using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace D2DAudit.Agent;

internal sealed record PostureCheck(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("pass")] bool Pass,
    [property: JsonPropertyName("detail")] string Detail);

internal sealed record AgentPayload(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("agentVersion")] string AgentVersion,
    [property: JsonPropertyName("agentKind")] string AgentKind,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("operatingSystem")] string OperatingSystem,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("collectedAt")] DateTimeOffset CollectedAt,
    [property: JsonPropertyName("checks")] IReadOnlyList<PostureCheck> Checks);

internal sealed record EnrollRequest(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("operatingSystem")] string OperatingSystem,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("agentKind")] string AgentKind,
    [property: JsonPropertyName("agentVersion")] string AgentVersion);

internal sealed record EnrollResponse(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("secret")] string Secret,
    [property: JsonPropertyName("checkInPath")] string CheckInPath,
    [property: JsonPropertyName("intervalHours")] int IntervalHours,
    [property: JsonPropertyName("personEmail")] string PersonEmail);

internal sealed record AgentConfig(
    [property: JsonPropertyName("server")] string Server,
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("deviceId")] string? DeviceId,
    [property: JsonPropertyName("secret")] string? Secret,
    [property: JsonPropertyName("checkInPath")] string? CheckInPath);

/// <summary>Request signing shared with the server: key = UTF-8 bytes of sha256hex(secret).</summary>
internal static class Signing
{
    public const string Version = "v1=";

    public static string KeyFromSecret(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static string Sign(string secret, long timestamp, string body)
    {
        var key = Encoding.UTF8.GetBytes(KeyFromSecret(secret));
        var message = Encoding.UTF8.GetBytes(timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + body);
        return Version + Convert.ToHexStringLower(HMACSHA256.HashData(key, message));
    }
}
