using D2DAudit.Infrastructure.Seed;

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
        "chandra.vell@" + ClientSeed.Company.Domain,
        "priya.nair@" + ClientSeed.Company.Domain,
        "marcus.lee@" + ClientSeed.Company.Domain,
        "dev.patel@" + ClientSeed.Company.Domain,
        "lena.cho@" + ClientSeed.Company.Domain,
        "auditor@northgate.example",
        "owner@northwind.example",
    ];
}
