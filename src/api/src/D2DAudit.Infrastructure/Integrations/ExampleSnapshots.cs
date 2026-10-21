using D2DAudit.Domain;

namespace D2DAudit.Infrastructure.Integrations;

public static class ExampleSnapshots
{
    public static IReadOnlyList<NormalizedResource> For(string provider, string fixture)
    {
        var all = fixture == "northwind" ? Northwind() : Acme();
        return all.Where(item => item.Provider == provider).ToArray();
    }

    private static IReadOnlyList<NormalizedResource> Acme() =>
    [
        Root("aws", "111122223333", mfa: true),
        Bucket("aws", "logs-prod", publicAccess: false, encrypted: true),
        Bucket("aws", "marketing-site", publicAccess: true, encrypted: false),
        Trail("aws", "management-events", enabled: true),
        User("google", "priya", "Priya N.", "priya@acme.example", mfa: true, offboarded: false, active: true),
        User("google", "sam", "Sam Leaved", "sam.leaved@acme.example", mfa: true, offboarded: true, active: false),
        User("microsoft", "jordan", "Jordan P.", "jordan@acme.example", mfa: false, offboarded: false, active: true),
        User("microsoft", "lena", "Lena K.", "lena@acme.example", mfa: true, offboarded: false, active: true),
        User("github", "casey", "Casey R.", "casey@acme.example", mfa: false, offboarded: false, active: true),
        User("github", "sam-gh", "Sam Leaved", "sam.leaved@acme.example", mfa: true, offboarded: false, active: true),
        Repo("github", "acme-api", protectedBranch: true),
        Repo("github", "acme-web", protectedBranch: true),
        Repo("github", "experiments", protectedBranch: false),
    ];

    private static IReadOnlyList<NormalizedResource> Northwind() =>
    [
        Root("aws", "999988887777", mfa: true),
        Bucket("aws", "northwind-ledger", publicAccess: false, encrypted: true),
        Trail("aws", "northwind-trail", enabled: true),
        User("google", "alex", "Alex Kim", "owner@northwind.example", mfa: true, offboarded: false, active: true),
        User("google", "rita", "Rita Old", "rita.old@northwind.example", mfa: true, offboarded: true, active: false),
        User("microsoft", "alex-ms", "Alex Kim", "owner@northwind.example", mfa: true, offboarded: false, active: true),
        User("microsoft", "rita-ms", "Rita Old", "rita.old@northwind.example", mfa: true, offboarded: true, active: false),
        User("github", "alex-gh", "Alex Kim", "owner@northwind.example", mfa: true, offboarded: false, active: true),
        Repo("github", "northwind-api", protectedBranch: true),
    ];

    private static NormalizedResource Root(string provider, string account, bool mfa) =>
        new(provider, "root", account, "root:" + account, Attr(("mfa", mfa)));

    private static NormalizedResource Bucket(string provider, string name, bool publicAccess, bool encrypted) =>
        new(provider, "bucket", name, name, Attr(("public", publicAccess), ("encrypted", encrypted)));

    private static NormalizedResource Trail(string provider, string name, bool enabled) =>
        new(provider, "trail", name, name, Attr(("enabled", enabled)));

    private static NormalizedResource Repo(string provider, string name, bool protectedBranch) =>
        new(provider, "repository", name, name, Attr(("protected", protectedBranch)));

    private static NormalizedResource User(
        string provider,
        string id,
        string name,
        string email,
        bool mfa,
        bool offboarded,
        bool active) =>
        new(provider, "user", id, name, new Dictionary<string, string>
        {
            ["email"] = email,
            ["mfa"] = mfa ? "true" : "false",
            ["offboarded"] = offboarded ? "true" : "false",
            ["status"] = active ? "active" : "suspended",
        });

    private static Dictionary<string, string> Attr(params (string Name, bool Value)[] flags)
    {
        var values = new Dictionary<string, string>();
        foreach (var (name, value) in flags)
        {
            values[name] = value ? "true" : "false";
        }

        return values;
    }
}
