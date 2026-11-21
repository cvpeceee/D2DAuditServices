using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace D2DAudit.Agent;

internal static class Program
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);
        var command = options.Command;
        if (command is "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var collector = new Collector(new ProcessRunner(), TimeProvider.System);
        try
        {
            return command switch
            {
                "enroll" => await EnrollAsync(options, collector),
                "run" when options.DryRun => DryRun(collector),
                "run" => await RunAsync(options, collector),
                "status" => Status(options),
                _ => Fail("Unknown command '" + command + "'.\n\n" + Usage),
            };
        }
        catch (HttpRequestException exception)
        {
            return Fail("Could not reach the D2D Audit server: " + exception.Message);
        }
    }

    private const string Usage = """
        D2D Audit device agent

        Usage:
          d2d-agent enroll --server https://app.example.com --token d2de_...   Enroll this device (or --from d2d-agent.json)
          d2d-agent run [--dry-run]                                             Collect posture and send a signed check-in
          d2d-agent --dry-run                                                   Print the JSON payload without sending it
          d2d-agent status                                                      Show the enrolled server and device id

        Options:
          --config <path>   Config file (default: per-user D2DAudit/agent.json)
        """;

    private static int DryRun(Collector collector)
    {
        Console.WriteLine(JsonSerializer.Serialize(collector.Collect(), Pretty));
        return 0;
    }

    private static int Status(Options options)
    {
        var config = ConfigStore.Load(options.ConfigPath);
        if (config?.DeviceId is null)
        {
            return Fail("This device is not enrolled. Run: d2d-agent enroll --server <url> --token <token>");
        }

        Console.WriteLine($"Server: {config.Server}\nDevice: {config.DeviceId}\nConfig: {options.ConfigPath}");
        return 0;
    }

    private static async Task<int> EnrollAsync(Options options, Collector collector)
    {
        var server = options.Server;
        var token = options.Token;
        if (options.From is not null)
        {
            var seed = JsonSerializer.Deserialize<AgentConfig>(await File.ReadAllTextAsync(options.From));
            server ??= seed?.Server;
            token ??= seed?.Token;
        }

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(token))
        {
            return Fail("enroll needs --server and --token (or --from d2d-agent.json).");
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback))
        {
            return Fail("The server must be an https:// URL (http is only allowed for localhost).");
        }

        var payload = collector.Collect();
        var request = new EnrollRequest(token.Trim(), payload.Serial, payload.Hostname, payload.OperatingSystem, payload.Platform, Collector.Kind, Collector.AgentVersion);
        using var http = Client(baseUri);
        using var content = Json(JsonSerializer.Serialize(request));
        using var response = await http.PostAsync("/api/v1/agent/enroll", content);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return Fail($"Enrollment failed ({(int)response.StatusCode}): {body}");
        }

        var result = JsonSerializer.Deserialize<EnrollResponse>(body) ?? throw new InvalidOperationException("Empty enrollment response.");
        ConfigStore.Save(options.ConfigPath, new AgentConfig(baseUri.GetLeftPart(UriPartial.Authority), null, result.DeviceId, result.Secret, result.CheckInPath));
        Console.WriteLine($"Enrolled {payload.Serial} for {result.PersonEmail}. Config saved to {options.ConfigPath}.");
        return await RunAsync(options, collector);
    }

    private static async Task<int> RunAsync(Options options, Collector collector)
    {
        var config = ConfigStore.Load(options.ConfigPath);
        if (config?.DeviceId is null || config.Secret is null)
        {
            return Fail("This device is not enrolled. Run: d2d-agent enroll --server <url> --token <token>");
        }

        var body = JsonSerializer.Serialize(collector.Collect());
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var http = Client(new Uri(config.Server));
        using var message = new HttpRequestMessage(HttpMethod.Post, config.CheckInPath ?? "/api/v1/agent/check-in") { Content = Json(body) };
        message.Headers.Add("X-D2D-Device", config.DeviceId);
        message.Headers.Add("X-D2D-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        message.Headers.Add("X-D2D-Signature", Signing.Sign(config.Secret, timestamp, body));
        using var response = await http.SendAsync(message);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return Fail($"Check-in failed ({(int)response.StatusCode}): {text}");
        }

        Console.WriteLine("Check-in accepted: " + text);
        return 0;
    }

    private static HttpClient Client(Uri baseUri)
    {
        var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-D2D-Request", "1");
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("d2d-agent", Collector.AgentVersion));
        return http;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

internal sealed record Options(string Command, bool DryRun, string? Server, string? Token, string? From, string ConfigPath)
{
    public static Options Parse(IReadOnlyList<string> args)
    {
        var command = args.Count > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "run";
        if (args.Count > 0 && args[0] is "--help" or "-h")
        {
            command = "help";
        }

        string? Value(string name)
        {
            for (var index = 0; index < args.Count - 1; index++)
            {
                if (args[index] == name)
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        return new Options(
            command,
            args.Contains("--dry-run"),
            Value("--server"),
            Value("--token"),
            Value("--from"),
            Value("--config") ?? ConfigStore.DefaultPath());
    }
}

internal static class ConfigStore
{
    public static string DefaultPath()
    {
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, OperatingSystem.IsWindows() ? "D2DAudit" : "d2daudit", "agent.json");
    }

    public static AgentConfig? Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path)) : null;

    public static void Save(string path, AgentConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
