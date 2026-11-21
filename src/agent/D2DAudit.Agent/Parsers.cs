using System.Globalization;
using System.Text.RegularExpressions;

namespace D2DAudit.Agent;

/// <summary>Pure parsers for command output so every check can be unit tested without the OS.</summary>
internal static partial class Parsers
{
    public const int MaxLockSeconds = 900;
    public const int UpdateWindowDays = 45;

    public static readonly IReadOnlyList<string> PasswordManagers =
        ["1Password", "Bitwarden", "LastPass", "KeePass", "KeePassXC", "Dashlane", "Keeper", "NordPass", "Proton Pass"];

    public static readonly IReadOnlyList<string> UnixAntivirus =
        ["clamd", "freshclam", "falcon-sensor", "falcond", "mdatp", "wdavdaemon", "SentinelAgent", "sentinelone", "sophosav", "savd", "esets", "cbagentd"];

    /// <summary>Parses <c>reg query</c> output into value name → value (REG_DWORD hex is converted to decimal).</summary>
    public static Dictionary<string, string> ParseRegQuery(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(output))
        {
            var match = RegLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups["value"].Value.Trim();
            if (match.Groups["type"].Value is "REG_DWORD" or "REG_QWORD"
                && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
            {
                value = number.ToString(CultureInfo.InvariantCulture);
            }

            values[match.Groups["name"].Value] = value;
        }

        return values;
    }

    /// <summary>Windows: pass when a machine inactivity limit or a secure screen saver locks within 15 minutes.</summary>
    public static PostureCheck WindowsScreenLock(IReadOnlyDictionary<string, string> machinePolicy, IReadOnlyDictionary<string, string> desktop)
    {
        if (Int(machinePolicy, "InactivityTimeoutSecs") is int limit and > 0)
        {
            return limit <= MaxLockSeconds
                ? new("screenLock", true, $"Machine inactivity limit {limit}s")
                : new("screenLock", false, $"Machine inactivity limit {limit}s exceeds {MaxLockSeconds}s");
        }

        var active = Get(desktop, "ScreenSaveActive") == "1";
        var secure = Get(desktop, "ScreenSaverIsSecure") == "1";
        var timeout = Int(desktop, "ScreenSaveTimeOut");
        if (active && secure && timeout is > 0 and <= MaxLockSeconds)
        {
            return new("screenLock", true, $"Secure screen saver after {timeout}s");
        }

        var reason = !active ? "Screen saver is off" : !secure ? "Screen saver does not require sign-in" : $"Screen saver timeout {timeout}s exceeds {MaxLockSeconds}s";
        return new("screenLock", false, reason);
    }

    /// <summary>
    /// Decodes SecurityCenter2 <c>productState</c>: byte 2 (0x10/0x11) = enabled, low byte 0x00 = signatures up to date.
    /// Lines are formatted as <c>name|productState</c>.
    /// </summary>
    public static PostureCheck SecurityCenterAntivirus(string output)
    {
        var products = new List<(string Name, bool Enabled, bool Current)>();
        foreach (var line in Lines(output))
        {
            var parts = line.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var state))
            {
                continue;
            }

            var running = (state >> 8) & 0xFF;
            products.Add((parts[0].Trim(), running is 0x10 or 0x11, (state & 0xFF) == 0x00));
        }

        if (products.Count == 0)
        {
            return new("antivirus", false, "No anti-malware product registered with Windows Security Center");
        }

        var good = products.FirstOrDefault(item => item.Enabled && item.Current);
        if (good.Name is not null)
        {
            return new("antivirus", true, $"{good.Name} enabled, signatures current");
        }

        var enabled = products.FirstOrDefault(item => item.Enabled);
        return enabled.Name is not null
            ? new("antivirus", false, $"{enabled.Name} enabled but signatures are out of date")
            : new("antivirus", false, $"{string.Join(", ", products.Select(item => item.Name))} registered but real-time protection is off");
    }

    /// <summary>Parses <c>Get-MpComputerStatus</c> fallback lines formatted as <c>Key=Value</c>.</summary>
    public static PostureCheck DefenderStatus(string output)
    {
        var values = KeyValues(output, '=');
        var realTime = string.Equals(Get(values, "RealTimeProtectionEnabled"), "True", StringComparison.OrdinalIgnoreCase);
        var service = string.Equals(Get(values, "AMServiceEnabled"), "True", StringComparison.OrdinalIgnoreCase);
        return realTime && service
            ? new("antivirus", true, "Microsoft Defender real-time protection on")
            : new("antivirus", false, values.Count == 0 ? "Microsoft Defender status unavailable" : "Microsoft Defender real-time protection off");
    }

    /// <summary>BitLocker via the Shell <c>System.Volume.BitLockerProtection</c> property (works without admin).</summary>
    public static PostureCheck BitLockerShell(string output)
    {
        var text = output.Trim();
        return text switch
        {
            "1" or "3" or "6" => new("diskEncryption", true, text == "3" ? "BitLocker encrypting C:" : "BitLocker protection on for C:"),
            "5" => new("diskEncryption", false, "BitLocker protection suspended for C:"),
            "2" or "4" => new("diskEncryption", false, "BitLocker off for C:"),
            _ => new("diskEncryption", false, "BitLocker status unavailable for C:"),
        };
    }

    /// <summary>BitLocker via WMI <c>Win32_EncryptableVolume.ProtectionStatus</c> (admin) or <c>manage-bde -status</c>.</summary>
    public static PostureCheck? BitLockerAdmin(string output)
    {
        var text = output.Trim();
        if (text == "1" || text.Contains("Protection On", StringComparison.OrdinalIgnoreCase))
        {
            return new("diskEncryption", true, "BitLocker protection on for C:");
        }

        if (text == "0" || text.Contains("Protection Off", StringComparison.OrdinalIgnoreCase))
        {
            return new("diskEncryption", false, "BitLocker protection off for C:");
        }

        return null;
    }

    public static PostureCheck PasswordManager(IEnumerable<string> installed)
    {
        var names = installed.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray();
        foreach (var manager in PasswordManagers.OrderByDescending(item => item.Length))
        {
            var found = names.FirstOrDefault(item => item.Contains(manager, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                return new("passwordManager", true, found);
            }
        }

        return new("passwordManager", false, "No approved password manager (1Password, Bitwarden, LastPass, KeePass) found");
    }

    /// <summary>Windows: last hotfix installed within <see cref="UpdateWindowDays"/> days and automatic updates not disabled.</summary>
    public static PostureCheck WindowsUpdates(string lastPatch, IReadOnlyDictionary<string, string> updatePolicy, DateTimeOffset now)
    {
        if (Get(updatePolicy, "NoAutoUpdate") == "1")
        {
            return new("osUpdates", false, "Automatic updates are disabled by policy");
        }

        if (!DateTimeOffset.TryParse(lastPatch.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var installed))
        {
            return new("osUpdates", false, "No installed update history found");
        }

        var age = (int)Math.Floor((now - installed).TotalDays);
        return age <= UpdateWindowDays
            ? new("osUpdates", true, $"Last update installed {installed:yyyy-MM-dd} ({age} days ago)")
            : new("osUpdates", false, $"Last update installed {installed:yyyy-MM-dd} ({age} days ago, limit {UpdateWindowDays})");
    }

    /// <summary>Parses <c>Get-NetFirewallProfile</c> lines (<c>Domain=True</c>) or <c>netsh advfirewall show allprofiles state</c>.</summary>
    public static PostureCheck WindowsFirewall(string output)
    {
        var profiles = KeyValues(output, '=');
        if (profiles.Count > 0)
        {
            var off = profiles.Where(item => !string.Equals(item.Value, "True", StringComparison.OrdinalIgnoreCase)).Select(item => item.Key).ToArray();
            return off.Length == 0
                ? new("firewall", true, $"Windows Firewall on for {string.Join(", ", profiles.Keys)}")
                : new("firewall", false, $"Windows Firewall off for {string.Join(", ", off)}");
        }

        var states = Lines(output).Where(line => line.TrimStart().StartsWith("State", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (states.Length == 0)
        {
            return new("firewall", false, "Firewall status unavailable");
        }

        return states.All(line => line.Contains("ON", StringComparison.Ordinal))
            ? new("firewall", true, "Windows Firewall on for all profiles")
            : new("firewall", false, "Windows Firewall off for at least one profile");
    }

    public static PostureCheck FileVault(string output) =>
        output.Contains("FileVault is On", StringComparison.OrdinalIgnoreCase)
            ? new("diskEncryption", true, "FileVault is on")
            : new("diskEncryption", false, output.Contains("Encryption in progress", StringComparison.OrdinalIgnoreCase) ? "FileVault encryption in progress" : "FileVault is off");

    /// <summary>macOS: <c>sysadminctl -screenLock status</c> plus screen saver idle time and display sleep minutes.</summary>
    public static PostureCheck MacScreenLock(string lockStatus, string idleTime, string displaySleep)
    {
        if (lockStatus.Contains("is off", StringComparison.OrdinalIgnoreCase))
        {
            return new("screenLock", false, "Require password after screen saver is off");
        }

        var candidates = new List<int>();
        if (int.TryParse(idleTime.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle) && idle > 0)
        {
            candidates.Add(idle);
        }

        var match = DisplaySleep().Match(displaySleep);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
        {
            candidates.Add(minutes * 60);
        }

        if (candidates.Count == 0)
        {
            return new("screenLock", false, "No screen saver or display sleep timeout configured");
        }

        var effective = candidates.Min();
        return effective <= MaxLockSeconds
            ? new("screenLock", true, $"Screen locks after {effective}s")
            : new("screenLock", false, $"Screen locks after {effective}s (limit {MaxLockSeconds}s)");
    }

    public static PostureCheck MacAntivirus(bool xprotectPresent, string gatekeeper, IEnumerable<string> processes)
    {
        var thirdParty = ThirdParty(processes);
        if (thirdParty is not null)
        {
            return new("antivirus", true, thirdParty + " running");
        }

        var gatekeeperOn = gatekeeper.Contains("assessments enabled", StringComparison.OrdinalIgnoreCase);
        return xprotectPresent && gatekeeperOn
            ? new("antivirus", true, "XProtect present and Gatekeeper enabled")
            : new("antivirus", false, !xprotectPresent ? "XProtect not found" : "Gatekeeper is disabled");
    }

    public static PostureCheck MacUpdates(string softwareUpdateList)
    {
        if (softwareUpdateList.Contains("No new software available", StringComparison.OrdinalIgnoreCase))
        {
            return new("osUpdates", true, "No pending macOS updates");
        }

        var pending = Lines(softwareUpdateList).Count(line => line.TrimStart().StartsWith("* Label:", StringComparison.Ordinal) || line.TrimStart().StartsWith("* ", StringComparison.Ordinal));
        return pending > 0
            ? new("osUpdates", false, $"{pending} macOS update(s) pending")
            : new("osUpdates", false, "Software update status unavailable");
    }

    public static PostureCheck MacFirewall(string output) =>
        output.Contains("enabled", StringComparison.OrdinalIgnoreCase) || output.Contains("State = 1", StringComparison.Ordinal) || output.Contains("State = 2", StringComparison.Ordinal)
            ? new("firewall", true, "Application firewall enabled")
            : new("firewall", false, "Application firewall disabled");

    /// <summary>Linux: <c>lsblk -rsno TYPE &lt;root source&gt;</c> lists the device chain under / — a crypt layer means LUKS.</summary>
    public static PostureCheck LinuxEncryption(string lsblkTypes) =>
        Lines(lsblkTypes).Any(line => line.Trim() == "crypt")
            ? new("diskEncryption", true, "Root filesystem is on a LUKS (dm-crypt) volume")
            : new("diskEncryption", false, "Root filesystem is not on an encrypted volume");

    public static PostureCheck LinuxScreenLock(string lockEnabled, string idleDelay)
    {
        if (string.IsNullOrWhiteSpace(lockEnabled))
        {
            return new("screenLock", false, "No desktop screen lock settings found (gsettings unavailable)");
        }

        if (!lockEnabled.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return new("screenLock", false, "Screen lock is disabled");
        }

        var match = Number().Match(idleDelay);
        if (!match.Success || !int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds == 0)
        {
            return new("screenLock", false, "Screen never blanks (idle delay 0)");
        }

        return seconds <= MaxLockSeconds
            ? new("screenLock", true, $"Screen locks after {seconds}s")
            : new("screenLock", false, $"Screen locks after {seconds}s (limit {MaxLockSeconds}s)");
    }

    public static PostureCheck LinuxAntivirus(IEnumerable<string> processes)
    {
        var found = ThirdParty(processes);
        return found is null
            ? new("antivirus", false, "No anti-malware daemon (ClamAV, Defender, CrowdStrike, SentinelOne) running")
            : new("antivirus", true, found + " running");
    }

    /// <summary>Counts security upgrades from <c>apt-get -s upgrade</c> (lines starting with Inst … -security).</summary>
    public static PostureCheck AptUpdates(string simulate)
    {
        var lines = Lines(simulate).Where(line => line.StartsWith("Inst ", StringComparison.Ordinal)).ToArray();
        var security = lines.Count(line => line.Contains("-security", StringComparison.OrdinalIgnoreCase));
        return security == 0
            ? new("osUpdates", true, lines.Length == 0 ? "No pending updates" : $"{lines.Length} non-security update(s) pending")
            : new("osUpdates", false, $"{security} security update(s) pending");
    }

    /// <summary><c>dnf check-update --security -q</c> exits 100 when updates are pending.</summary>
    public static PostureCheck DnfUpdates(int exitCode, string output)
    {
        var count = Lines(output).Count(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("Last metadata", StringComparison.Ordinal));
        return exitCode == 100
            ? new("osUpdates", false, $"{count} security update(s) pending")
            : exitCode == 0 ? new("osUpdates", true, "No pending security updates") : new("osUpdates", false, "Update status unavailable");
    }

    public static PostureCheck LinuxFirewall(string ufw, string firewalld, string nftRules)
    {
        if (ufw.Contains("Status: active", StringComparison.OrdinalIgnoreCase))
        {
            return new("firewall", true, "ufw active");
        }

        if (firewalld.Trim().Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            return new("firewall", true, "firewalld running");
        }

        return Lines(nftRules).Any(line => line.TrimStart().StartsWith("chain input", StringComparison.OrdinalIgnoreCase) || line.Contains("hook input", StringComparison.OrdinalIgnoreCase))
            ? new("firewall", true, "nftables input chain present")
            : new("firewall", false, "No active host firewall (ufw, firewalld, nftables)");
    }

    /// <summary>Splits probe output written as <c>##name</c> headers followed by that probe's lines.</summary>
    public static Dictionary<string, string> Sections(string output)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        var buffer = new List<string>();
        foreach (var line in Lines(output).Append("##"))
        {
            if (!line.StartsWith("##", StringComparison.Ordinal))
            {
                buffer.Add(line);
                continue;
            }

            if (current is not null)
            {
                sections[current] = string.Join('\n', buffer).Trim();
            }

            current = line[2..].Trim();
            buffer.Clear();
        }

        return sections;
    }

    public static string? MacSerial(string ioreg)
    {
        var match = IoregSerial().Match(ioreg);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public static string? CleanSerial(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || text.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Default string", StringComparison.OrdinalIgnoreCase) || text.Equals("System Serial Number", StringComparison.OrdinalIgnoreCase)
            || text.All(ch => ch == '0'))
        {
            return null;
        }

        return text.Length > 80 ? text[..80] : text;
    }

    private static string? ThirdParty(IEnumerable<string> processes)
    {
        var names = processes.Select(item => Path.GetFileName(item.Trim())).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return UnixAntivirus.FirstOrDefault(names.Contains);
    }

    internal static IEnumerable<string> Lines(string text) =>
        (text ?? "").Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0);

    private static Dictionary<string, string> KeyValues(string output, char separator)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(output))
        {
            var index = line.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0)
            {
                values[line[..index].Trim()] = line[(index + 1)..].Trim();
            }
        }

        return values;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static int? Int(IReadOnlyDictionary<string, string> values, string key) =>
        int.TryParse(Get(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    [GeneratedRegex(@"^\s+(?<name>\S+)\s+(?<type>REG_\w+)\s*(?<value>.*)$")]
    private static partial Regex RegLine();

    [GeneratedRegex(@"displaysleep\s+(\d+)")]
    private static partial Regex DisplaySleep();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();

    [GeneratedRegex("\"IOPlatformSerialNumber\"\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex IoregSerial();
}
