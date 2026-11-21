using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2DAudit.Domain;

public sealed class DeviceEnrollmentToken
{
    public Guid Id { get; set; }

    public string PersonEmail { get; set; } = "";

    public string PersonName { get; set; } = "";

    public string TokenHash { get; set; } = "";

    public string Hint { get; set; } = "";

    public string CreatedByEmail { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? DeviceId { get; set; }
}

public sealed class DeviceCredential
{
    public Guid Id { get; set; }

    public string Serial { get; set; } = "";

    public string Hostname { get; set; } = "";

    public string Platform { get; set; } = "";

    public string OperatingSystem { get; set; } = "";

    public string AgentKind { get; set; } = "";

    public string AgentVersion { get; set; } = "";

    public string PersonEmail { get; set; } = "";

    public string PersonName { get; set; } = "";

    public string SecretHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public long LastSignedTimestamp { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class DevicePostureSnapshot
{
    public Guid Id { get; set; }

    public Guid? DeviceId { get; set; }

    public string Serial { get; set; } = "";

    public DateTimeOffset At { get; set; }

    public string AgentKind { get; set; } = "";

    public string AgentVersion { get; set; } = "";

    public string Platform { get; set; } = "";

    public int Passing { get; set; }

    public int Failing { get; set; }

    public string ChecksJson { get; set; } = "[]";
}

/// <summary>
/// Enrollment tokens and device ids carry the tenant id so the anonymous agent endpoints can find the
/// tenant database without a session. Only SHA-256 hashes of tokens and secrets are stored.
/// </summary>
public static class DeviceAgentTokens
{
    public const string EnrollmentPrefix = "d2de_";
    public const string DevicePrefix = "d2dd_";
    public static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromHours(72);

    public static string CreateEnrollmentToken(Guid tenantId) =>
        $"{EnrollmentPrefix}{tenantId:N}_{RandomNumberGenerator.GetHexString(40, lowercase: true)}";

    public static string CreateSecret() => RandomNumberGenerator.GetHexString(64, lowercase: true);

    public static string DeviceId(Guid tenantId, Guid credentialId) => $"{DevicePrefix}{tenantId:N}_{credentialId:N}";

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim()))).ToLowerInvariant();

    public static string Hint(string token) => token.Length <= 8 ? token : "…" + token[^6..];

    public static bool TryParseEnrollmentToken(string? token, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        if (!trimmed.StartsWith(EnrollmentPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = trimmed[EnrollmentPrefix.Length..];
        return body.Length >= 32 + 1 + 32 && body[32] == '_' && Guid.TryParseExact(body[..32], "N", out tenantId);
    }

    public static bool TryParseDeviceId(string? deviceId, out Guid tenantId, out Guid credentialId)
    {
        tenantId = Guid.Empty;
        credentialId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        var trimmed = deviceId.Trim();
        if (!trimmed.StartsWith(DevicePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = trimmed[DevicePrefix.Length..];
        return body.Length == 65
            && body[32] == '_'
            && Guid.TryParseExact(body[..32], "N", out tenantId)
            && Guid.TryParseExact(body[33..], "N", out credentialId);
    }

    public static bool IsUsable(DeviceEnrollmentToken token, DateTimeOffset now) =>
        token.RevokedAt is null && token.UsedAt is null && token.ExpiresAt > now;

    public static string State(DeviceEnrollmentToken token, DateTimeOffset now)
    {
        if (token.RevokedAt is not null)
        {
            return "Revoked";
        }

        if (token.UsedAt is not null)
        {
            return "Used";
        }

        return token.ExpiresAt <= now ? "Expired" : "Active";
    }

    public static bool HashEquals(string expectedHex, string actualHex)
    {
        var expected = Encoding.ASCII.GetBytes(expectedHex);
        var actual = Encoding.ASCII.GetBytes(actualHex);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

public enum SignatureCheck
{
    Valid,
    Missing,
    Expired,
    Replayed,
    Invalid,
}

/// <summary>
/// Check-ins are signed with HMAC-SHA256 over "{unixSeconds}.{rawBody}". The HMAC key is the lowercase hex
/// SHA-256 of the device secret, so the server only needs to keep the hash. A timestamp must be inside the
/// replay window and strictly newer than the last accepted timestamp for the device.
/// </summary>
public static class DeviceSignature
{
    public const long WindowSeconds = 300;
    public const string Scheme = "v1=";

    public static string KeyFromSecret(string secret) => DeviceAgentTokens.Hash(secret);

    public static string Sign(string secretHash, long unixTimestamp, string body)
    {
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secretHash),
            Encoding.UTF8.GetBytes(unixTimestamp.ToString(CultureInfo.InvariantCulture) + "." + body));
        return Scheme + Convert.ToHexString(mac).ToLowerInvariant();
    }

    public static SignatureCheck Verify(string secretHash, string? timestampHeader, string body, string? signatureHeader, long nowUnix, long lastAccepted)
    {
        if (string.IsNullOrWhiteSpace(secretHash) || string.IsNullOrWhiteSpace(signatureHeader)
            || !long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
        {
            return SignatureCheck.Missing;
        }

        if (Math.Abs(nowUnix - timestamp) > WindowSeconds)
        {
            return SignatureCheck.Expired;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secretHash, timestamp, body));
        var actual = Encoding.ASCII.GetBytes(signatureHeader.Trim());
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return SignatureCheck.Invalid;
        }

        return timestamp <= lastAccepted ? SignatureCheck.Replayed : SignatureCheck.Valid;
    }
}

public sealed record PostureCheck(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("pass")] bool Pass,
    [property: JsonPropertyName("detail")] string Detail);

public sealed record AgentPayload(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("agentVersion")] string AgentVersion,
    [property: JsonPropertyName("agentKind")] string AgentKind,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("operatingSystem")] string OperatingSystem,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("collectedAt")] DateTimeOffset? CollectedAt,
    [property: JsonPropertyName("checks")] IReadOnlyList<PostureCheck> Checks);

public sealed record PostureCheckInfo(string Key, string Title, string ControlCode, string ComputerField);

public static class DevicePosture
{
    public const int StaleAfterDays = 7;
    public const string StaleTestName = "Agent devices checked in within 7 days";
    public const string StaleTestKey = "agent.stale";
    public const int MaxBodyBytes = 32 * 1024;

    public static readonly IReadOnlyList<string> AgentKinds = ["dotnet", "powershell", "bash"];
    public static readonly IReadOnlyList<string> Platforms = ["windows", "macos", "linux"];

    public static readonly IReadOnlyList<PostureCheckInfo> Checks =
    [
        new("diskEncryption", "Disk encryption", "EP-03", "Encrypted"),
        new("screenLock", "Screen lock within 15 minutes", "EP-08", "ScreenLock"),
        new("antivirus", "Anti-malware running", "MAL-01", "Antivirus"),
        new("passwordManager", "Password manager installed", "AC-07", "PasswordManager"),
        new("osUpdates", "Operating system updates", "VM-02", "AutoUpdate"),
        new("firewall", "Host firewall enabled", "CFG-01", ""),
    ];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static PostureCheckInfo? Info(string key) =>
        Checks.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    public static string TestKey(string key) => "agent." + key;

    public static string TestName(PostureCheckInfo info) => "Device agent: " + info.Title.ToLowerInvariant() + " on enrolled devices";

    public static bool IsStale(DateTimeOffset? lastCheckIn, DateTimeOffset now) =>
        lastCheckIn is null || now - lastCheckIn.Value > TimeSpan.FromDays(StaleAfterDays);

    public static (AgentPayload? Payload, string? Error) Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
        {
            return (null, "invalid_payload");
        }

        AgentPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AgentPayload>(body, Json);
        }
        catch (JsonException)
        {
            return (null, "invalid_payload");
        }

        if (payload is null)
        {
            return (null, "invalid_payload");
        }

        var error = Validate(payload);
        return error is null ? (Normalize(payload), null) : (null, error);
    }

    public static string? Validate(AgentPayload payload)
    {
        if (payload.SchemaVersion != 1)
        {
            return "unsupported_schema";
        }

        if (string.IsNullOrWhiteSpace(payload.Serial) || payload.Serial.Trim().Length > 80
            || (payload.OperatingSystem ?? "").Trim().Length is 0 or > 80
            || (payload.Hostname ?? "").Length > 120
            || (payload.AgentVersion ?? "").Length > 32)
        {
            return "invalid_payload";
        }

        if (!AgentKinds.Contains(payload.AgentKind ?? "") || !Platforms.Contains(payload.Platform ?? ""))
        {
            return "invalid_payload";
        }

        if (payload.Checks is null || payload.Checks.Count is 0 or > 20
            || payload.Checks.Any(item => Info(item.Key) is null || (item.Detail ?? "").Length > 500)
            || payload.Checks.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != payload.Checks.Count)
        {
            return "invalid_checks";
        }

        return null;
    }

    public static string Serialize(IReadOnlyList<PostureCheck> checks) => JsonSerializer.Serialize(checks);

    private static AgentPayload Normalize(AgentPayload payload) => payload with
    {
        Serial = payload.Serial.Trim(),
        Hostname = (payload.Hostname ?? "").Trim(),
        OperatingSystem = payload.OperatingSystem.Trim(),
        AgentVersion = (payload.AgentVersion ?? "").Trim(),
        Checks = payload.Checks.Select(item => item with { Detail = (item.Detail ?? "").Trim() }).ToArray(),
    };

    public static IReadOnlyList<PostureCheck> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<PostureCheck>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool? Result(IReadOnlyList<PostureCheck> checks, string key) =>
        checks.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))?.Pass;

    public static void Apply(ComputerRecord computer, IReadOnlyList<PostureCheck> checks)
    {
        computer.Encrypted = Result(checks, "diskEncryption") ?? false;
        computer.ScreenLock = Result(checks, "screenLock") ?? false;
        computer.Antivirus = Result(checks, "antivirus") ?? false;
        computer.PasswordManager = Result(checks, "passwordManager") ?? false;
        computer.AutoUpdate = Result(checks, "osUpdates") ?? false;
    }

    public static IReadOnlyList<PostureCheck> FromComputer(ComputerRecord computer) =>
    [
        new("diskEncryption", computer.Encrypted, computer.Encrypted ? "Encrypted" : "Not encrypted"),
        new("screenLock", computer.ScreenLock, computer.ScreenLock ? "Configured" : "Not configured"),
        new("antivirus", computer.Antivirus, computer.Antivirus ? "Running" : "Not detected"),
        new("passwordManager", computer.PasswordManager, computer.PasswordManager ? "Installed" : "Not detected"),
        new("osUpdates", computer.AutoUpdate, computer.AutoUpdate ? "Current" : "Updates pending"),
    ];

    public static (TestOutcome Outcome, string Detail) StaleOutcome(IEnumerable<(string Serial, DateTimeOffset? LastSeen)> devices, DateTimeOffset now)
    {
        var list = devices.ToArray();
        if (list.Length == 0)
        {
            return (TestOutcome.NotRun, "No devices are enrolled with the device agent yet.");
        }

        var stale = list.Where(item => IsStale(item.LastSeen, now)).Select(item => item.Serial).Order(StringComparer.Ordinal).ToArray();
        return stale.Length == 0
            ? (TestOutcome.Pass, $"All {list.Length} enrolled devices checked in within {StaleAfterDays} days.")
            : (TestOutcome.Fail, $"{stale.Length} of {list.Length} enrolled devices have not checked in for more than {StaleAfterDays} days: {string.Join(", ", stale.Take(10))}.");
    }

    public static (TestOutcome Outcome, string Detail) CheckOutcome(string key, IEnumerable<(string Serial, IReadOnlyList<PostureCheck> Checks)> devices)
    {
        var reporting = devices.Where(item => Result(item.Checks, key) is not null).ToArray();
        var info = Info(key);
        var title = info?.Title ?? key;
        if (reporting.Length == 0)
        {
            return (TestOutcome.NotRun, $"No enrolled device has reported {title.ToLowerInvariant()} yet.");
        }

        var failing = reporting.Where(item => Result(item.Checks, key) == false).Select(item => item.Serial).Order(StringComparer.Ordinal).ToArray();
        return failing.Length == 0
            ? (TestOutcome.Pass, $"{title} passes on all {reporting.Length} reporting devices.")
            : (TestOutcome.Fail, $"{title} fails on {failing.Length} of {reporting.Length} devices: {string.Join(", ", failing.Take(10))}.");
    }

    public static IReadOnlyList<string> Remediation(string key, string platform)
    {
        var os = (platform ?? "").ToLowerInvariant();
        return (key, os) switch
        {
            ("diskEncryption", "windows") =>
            [
                "Open Settings > Privacy & security > Device encryption (or Control Panel > BitLocker Drive Encryption).",
                "Turn on BitLocker for the operating system drive and save the recovery key to your company account.",
                "Restart and let encryption finish, then run the agent again.",
            ],
            ("diskEncryption", "macos") =>
            [
                "Open System Settings > Privacy & Security > FileVault.",
                "Click Turn On and store the recovery key with your company (do not keep it only on the laptop).",
                "Keep the laptop on power until encryption completes, then run the agent again.",
            ],
            ("diskEncryption", _) =>
            [
                "Linux disk encryption must be chosen at install time: reinstall with LUKS full-disk encryption enabled.",
                "Back up your home directory first and ask IT for the approved encrypted image.",
                "Confirm with 'lsblk -f' that the root volume shows crypto_LUKS, then run the agent again.",
            ],
            ("screenLock", "windows") =>
            [
                "Open Settings > Accounts > Sign-in options and set 'If you've been away, when should Windows require you to sign in again?' to 'When PC wakes up from sleep'.",
                "Open Settings > Personalization > Lock screen > Screen saver, choose a timeout of 15 minutes or less and tick 'On resume, display logon screen'.",
            ],
            ("screenLock", "macos") =>
            [
                "Open System Settings > Lock Screen and set 'Start Screen Saver when inactive' to 15 minutes or less.",
                "Set 'Require password after screen saver begins or display is turned off' to Immediately.",
            ],
            ("screenLock", _) =>
            [
                "Open Settings > Privacy > Screen Lock (GNOME) and set Blank Screen Delay to 15 minutes or less.",
                "Turn on Automatic Screen Lock with no additional delay.",
            ],
            ("antivirus", "windows") =>
            [
                "Open Windows Security > Virus & threat protection.",
                "Turn on Real-time protection and run Check for updates under Protection updates.",
                "If a third-party product is installed, make sure it is licensed and reports as up to date.",
            ],
            ("antivirus", "macos") =>
            [
                "Keep 'Install Security Responses and system files' on in System Settings > General > Software Update > Automatic updates (XProtect).",
                "Install the company endpoint protection product if IT has assigned one.",
            ],
            ("antivirus", _) =>
            [
                "Install ClamAV ('sudo apt install clamav clamav-daemon') or the company endpoint protection product.",
                "Enable the service: 'sudo systemctl enable --now clamav-freshclam clamav-daemon'.",
            ],
            ("passwordManager", _) =>
            [
                "Install the company-approved password manager (1Password, Bitwarden, LastPass or KeePassXC).",
                "Sign in with your work account and move saved browser passwords into it.",
                "Run the agent again so it can detect the installation.",
            ],
            ("osUpdates", "windows") =>
            [
                "Open Settings > Windows Update and click Check for updates.",
                "Install all pending quality and security updates and restart.",
                "Leave 'Get the latest updates as soon as they're available' on.",
            ],
            ("osUpdates", "macos") =>
            [
                "Open System Settings > General > Software Update and install pending updates.",
                "Turn on Automatic updates, including security responses.",
            ],
            ("osUpdates", _) =>
            [
                "Run 'sudo apt update && sudo apt upgrade' (or the dnf/zypper equivalent) and reboot if a kernel was updated.",
                "Enable unattended security upgrades: 'sudo apt install unattended-upgrades && sudo dpkg-reconfigure unattended-upgrades'.",
            ],
            ("firewall", "windows") =>
            [
                "Open Windows Security > Firewall & network protection.",
                "Turn on Microsoft Defender Firewall for Domain, Private and Public networks.",
            ],
            ("firewall", "macos") =>
            [
                "Open System Settings > Network > Firewall and turn it on.",
            ],
            ("firewall", _) =>
            [
                "Enable the host firewall: 'sudo ufw enable' (or 'sudo systemctl enable --now firewalld').",
            ],
            _ => ["Contact your security team for guidance on this check."],
        };
    }
}
