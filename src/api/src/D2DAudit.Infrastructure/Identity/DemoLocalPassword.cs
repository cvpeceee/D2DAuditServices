namespace D2DAudit.Infrastructure.Identity;

/// <summary>
/// Shared password for the example workspace. It is a local-development convenience, not a production secret.
/// Production refuses to start without WorkOS and never seeds these accounts.
/// </summary>
public static class DemoLocalPassword
{
    public const string Value = "Example-Only-1!";

    public static readonly string[] Emails =
    [
        "chandra@acme.example",
        "priya@acme.example",
        "marcus@acme.example",
        "dev@acme.example",
        "auditor@northgate.example",
        "owner@northwind.example",
    ];
}
