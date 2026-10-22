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
        "chandra.vell@harborline.example",
        "priya.nair@harborline.example",
        "marcus.lee@harborline.example",
        "dev.patel@harborline.example",
        "lena.cho@harborline.example",
        "auditor@northgate.example",
        "owner@northwind.example",
    ];
}
