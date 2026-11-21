using System.Diagnostics;
using System.Runtime.InteropServices;

namespace D2DAudit.Agent;

internal interface ICommandRunner
{
    (int ExitCode, string Output) Run(string fileName, params string[] arguments);
}

internal sealed class ProcessRunner : ICommandRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public (int ExitCode, string Output) Run(string fileName, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return (-1, "");
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                return (-1, "");
            }

            return (process.ExitCode, stdout.Result + stderr.Result);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, "");
        }
    }
}

internal sealed class Collector(ICommandRunner runner, TimeProvider time)
{
    public const string Kind = "dotnet";

    internal const string WindowsProbe = """
        function Section($name, [scriptblock]$probe) { Write-Output "##$name"; try { & $probe } catch { } }
        Section serial { (Get-CimInstance -ClassName Win32_BIOS -ErrorAction Stop).SerialNumber }
        Section bitlockerAdmin { (Get-CimInstance -Namespace root/cimv2/Security/MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume -Filter "DriveLetter='C:'" -ErrorAction Stop).ProtectionStatus }
        Section bitlockerShell { (New-Object -ComObject Shell.Application).NameSpace('C:').Self.ExtendedProperty('System.Volume.BitLockerProtection') }
        Section antivirus { Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop | ForEach-Object { "$($_.displayName)|$($_.productState)" } }
        Section defender { $s = Get-MpComputerStatus -ErrorAction Stop; "AMServiceEnabled=$($s.AMServiceEnabled)"; "RealTimeProtectionEnabled=$($s.RealTimeProtectionEnabled)" }
        Section installed {
          $keys = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
          Get-ItemProperty $keys -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName } | ForEach-Object { $_.DisplayName }
          Get-AppxPackage -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '1Password|Bitwarden|KeePass|LastPass|Dashlane|Keeper' } | ForEach-Object { $_.Name }
        }
        Section lastPatch { $h = Get-HotFix -ErrorAction Stop | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn -Descending | Select-Object -First 1; if ($h) { $h.InstalledOn.ToString('o') } }
        Section firewall { Get-NetFirewallProfile -ErrorAction Stop | ForEach-Object { "$($_.Name)=$($_.Enabled)" } }
        """;

    private string? windowsSerial;

    public static string AgentVersion => typeof(Collector).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public static string Platform =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    public AgentPayload Collect()
    {
        var checks = Platform switch
        {
            "windows" => Windows(),
            "macos" => Mac(),
            _ => Linux(),
        };
        return new AgentPayload(1, AgentVersion, Kind, Serial(), Environment.MachineName, RuntimeInformation.OSDescription.Trim(), Platform, time.GetUtcNow(), checks);
    }

    public string Serial()
    {
        string? serial = null;
        if (OperatingSystem.IsWindows())
        {
            serial = Parsers.CleanSerial(windowsSerial ?? PowerShell("(Get-CimInstance -ClassName Win32_BIOS).SerialNumber"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            serial = Parsers.CleanSerial(Parsers.MacSerial(runner.Run("ioreg", "-rd1", "-c", "IOPlatformExpertDevice").Output));
        }
        else
        {
            serial = Parsers.CleanSerial(ReadFile("/sys/class/dmi/id/product_serial")) ?? Parsers.CleanSerial(ReadFile("/etc/machine-id"));
        }

        return serial ?? Environment.MachineName;
    }

    private List<PostureCheck> Windows()
    {
        // One PowerShell process for every probe: starting powershell.exe is the slowest part of collection.
        var probe = Parsers.Sections(PowerShell(WindowsProbe));
        var encryption = Parsers.BitLockerAdmin(probe.GetValueOrDefault("bitlockerAdmin", ""))
            ?? Parsers.BitLockerShell(probe.GetValueOrDefault("bitlockerShell", ""));

        var policy = Parsers.ParseRegQuery(runner.Run("reg", "query", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "/v", "InactivityTimeoutSecs").Output);
        var desktop = Parsers.ParseRegQuery(runner.Run("reg", "query", @"HKCU\Control Panel\Desktop").Output);
        var desktopPolicy = Parsers.ParseRegQuery(runner.Run("reg", "query", @"HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop").Output);
        foreach (var (key, value) in desktopPolicy)
        {
            desktop[key] = value;
        }

        var antivirusOutput = probe.GetValueOrDefault("antivirus", "");
        var antivirus = string.IsNullOrWhiteSpace(antivirusOutput)
            ? Parsers.DefenderStatus(probe.GetValueOrDefault("defender", ""))
            : Parsers.SecurityCenterAntivirus(antivirusOutput);

        var installed = Parsers.Lines(probe.GetValueOrDefault("installed", "")).ToList();
        installed.AddRange(KnownPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1Password"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Bitwarden"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KeePass Password Safe 2"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KeePassXC")));

        var updatePolicy = Parsers.ParseRegQuery(runner.Run("reg", "query", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "/v", "NoAutoUpdate").Output);
        var firewallOutput = probe.GetValueOrDefault("firewall", "");
        if (string.IsNullOrWhiteSpace(firewallOutput))
        {
            firewallOutput = runner.Run("netsh", "advfirewall", "show", "allprofiles", "state").Output;
        }

        windowsSerial = probe.GetValueOrDefault("serial");
        return
        [
            encryption,
            Parsers.WindowsScreenLock(policy, desktop),
            antivirus,
            Parsers.PasswordManager(installed),
            Parsers.WindowsUpdates(probe.GetValueOrDefault("lastPatch", ""), updatePolicy, time.GetUtcNow()),
            Parsers.WindowsFirewall(firewallOutput),
        ];
    }

    private List<PostureCheck> Mac()
    {
        var processes = Parsers.Lines(runner.Run("ps", "-axco", "comm").Output);
        var applications = Directory.Exists("/Applications") ? Directory.GetDirectories("/Applications", "*.app").Select(Path.GetFileNameWithoutExtension).OfType<string>() : [];
        return
        [
            Parsers.FileVault(runner.Run("fdesetup", "status").Output),
            Parsers.MacScreenLock(
                runner.Run("sysadminctl", "-screenLock", "status").Output,
                runner.Run("defaults", "-currentHost", "read", "com.apple.screensaver", "idleTime").Output,
                runner.Run("pmset", "-g", "custom").Output),
            Parsers.MacAntivirus(
                Directory.Exists("/Library/Apple/System/Library/CoreServices/XProtect.bundle"),
                runner.Run("spctl", "--status").Output,
                processes),
            Parsers.PasswordManager(applications),
            Parsers.MacUpdates(runner.Run("softwareupdate", "-l").Output),
            Parsers.MacFirewall(runner.Run("/usr/libexec/ApplicationFirewall/socketfilterfw", "--getglobalstate").Output),
        ];
    }

    private List<PostureCheck> Linux()
    {
        var source = runner.Run("findmnt", "-no", "SOURCE", "/").Output.Trim();
        var chain = source.Length == 0 ? "" : runner.Run("lsblk", "-rsno", "TYPE", source).Output;
        var processes = Parsers.Lines(runner.Run("ps", "-eo", "comm").Output);
        var managers = new[] { "1password", "bitwarden", "keepassxc", "keepass2", "lpass", "dashlane-cli", "keeper" }
            .Where(name => runner.Run("sh", "-c", "command -v " + name).ExitCode == 0)
            .Concat(KnownPaths("/opt/1Password", "/opt/Bitwarden", "/snap/bitwarden", "/var/lib/flatpak/app/com.bitwarden.desktop", "/var/lib/flatpak/app/org.keepassxc.KeePassXC"))
            .Select(name => name.Replace("keepassxc", "KeePassXC", StringComparison.Ordinal).Replace("lpass", "LastPass", StringComparison.Ordinal));

        var updates = File.Exists("/usr/bin/apt-get")
            ? Parsers.AptUpdates(runner.Run("apt-get", "-s", "-o", "Debug::NoLocking=1", "upgrade").Output)
            : File.Exists("/usr/bin/dnf")
                ? DnfCheck()
                : new PostureCheck("osUpdates", false, "Unsupported package manager");

        return
        [
            Parsers.LinuxEncryption(chain),
            Parsers.LinuxScreenLock(
                runner.Run("gsettings", "get", "org.gnome.desktop.screensaver", "lock-enabled").Output,
                runner.Run("gsettings", "get", "org.gnome.desktop.session", "idle-delay").Output),
            Parsers.LinuxAntivirus(processes),
            Parsers.PasswordManager(managers),
            updates,
            Parsers.LinuxFirewall(
                runner.Run("ufw", "status").Output,
                runner.Run("firewall-cmd", "--state").Output,
                runner.Run("nft", "list", "ruleset").Output),
        ];
    }

    private PostureCheck DnfCheck()
    {
        var (exit, output) = runner.Run("dnf", "check-update", "--security", "-q");
        return Parsers.DnfUpdates(exit, output);
    }

    private string PowerShell(string script)
    {
        var (exit, output) = runner.Run("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script);
        return exit == 0 ? output.Trim() : "";
    }

    private static IEnumerable<string> KnownPaths(params string[] paths) =>
        paths.Where(path => Directory.Exists(path) || File.Exists(path)).Select(path => Path.GetFileName(path.TrimEnd('/', '\\')));

    private static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
