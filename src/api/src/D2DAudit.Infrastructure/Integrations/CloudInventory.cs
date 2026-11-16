using System.Text.Json;
using D2DAudit.Domain;

namespace D2DAudit.Infrastructure.Integrations;

public static class CloudInventory
{
    public static IReadOnlyList<NormalizedResource> AzureStorage(string json)
    {
        var rows = new List<NormalizedResource>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("value", out var value))
        {
            return rows;
        }

        foreach (var item in value.EnumerateArray())
        {
            var name = Text(item, "name");
            var properties = item.TryGetProperty("properties", out var property) ? property : default;
            var publicAccess = properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("allowBlobPublicAccess", out var allow)
                && allow.ValueKind == JsonValueKind.True;
            var encrypted = properties.ValueKind != JsonValueKind.Object
                || !properties.TryGetProperty("encryption", out _)
                || BlobEncrypted(properties);
            rows.Add(new NormalizedResource("azure", "bucket", name, name, Flags(("public", publicAccess), ("encrypted", encrypted))));
        }

        return rows;
    }

    public static IReadOnlyList<NormalizedResource> AzureVaults(string json)
    {
        var rows = new List<NormalizedResource>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("value", out var value))
        {
            return rows;
        }

        foreach (var item in value.EnumerateArray())
        {
            var name = Text(item, "name");
            var enabled = true;
            if (item.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("enableSoftDelete", out var soft)
                && soft.ValueKind == JsonValueKind.False)
            {
                enabled = false;
            }

            rows.Add(new NormalizedResource("azure", "keyVault", name, name, Flags(("enabled", enabled))));
        }

        return rows;
    }

    public static IReadOnlyList<NormalizedResource> AzureActivity(string json)
    {
        using var document = JsonDocument.Parse(json);
        var count = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;
        return [new NormalizedResource("azure", "trail", "activity-log", "activity-log", Flags(("enabled", count > 0)))];
    }

    public static IReadOnlyList<NormalizedResource> AzureNetwork(string json)
    {
        var rows = new List<NormalizedResource>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("value", out var value))
        {
            return rows;
        }

        foreach (var item in value.EnumerateArray())
        {
            var name = Text(item, "name");
            var open = false;
            if (item.TryGetProperty("properties", out var properties) && properties.TryGetProperty("securityRules", out var rules))
            {
                foreach (var rule in rules.EnumerateArray())
                {
                    if (!rule.TryGetProperty("properties", out var ruleProperties))
                    {
                        continue;
                    }

                    var access = Text(ruleProperties, "access");
                    var direction = Text(ruleProperties, "direction");
                    var source = Text(ruleProperties, "sourceAddressPrefix");
                    var ports = Text(ruleProperties, "destinationPortRange");
                    var wide = source is "*" or "0.0.0.0/0" or "Internet";
                    var sensitive = ports is "22" or "3389" or "*";
                    if (access == "Allow" && direction == "Inbound" && wide && sensitive)
                    {
                        open = true;
                    }
                }
            }

            rows.Add(new NormalizedResource("azure", "network", name, name, Flags(("open", open))));
        }

        return rows;
    }

    public static IReadOnlyList<NormalizedResource> EntraUsers(string json)
    {
        var rows = new List<NormalizedResource>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("value", out var value))
        {
            return rows;
        }

        foreach (var item in value.EnumerateArray())
        {
            var id = Text(item, "id");
            var name = Text(item, "userPrincipalName");
            if (name.Length == 0)
            {
                name = Text(item, "userDisplayName");
            }

            var mfa = item.TryGetProperty("isMfaRegistered", out var registered) && registered.ValueKind == JsonValueKind.True;
            rows.Add(new NormalizedResource("azure", "user", id, name, new Dictionary<string, string>
            {
                ["email"] = name,
                ["mfa"] = mfa ? "true" : "false",
                ["offboarded"] = "false",
                ["status"] = "active",
            }));
        }

        return rows;
    }

    public static IReadOnlyList<NormalizedResource> GcpBuckets(string json)
    {
        var rows = new List<NormalizedResource>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items))
        {
            return rows;
        }

        foreach (var item in items.EnumerateArray())
        {
            var name = Text(item, "name");
            var blocked = item.TryGetProperty("iamConfiguration", out var iam)
                && iam.TryGetProperty("publicAccessPrevention", out var prevention)
                && prevention.GetString() == "enforced";
            rows.Add(new NormalizedResource("gcp", "bucket", name, name, Flags(("public", !blocked), ("encrypted", true))));
        }

        return rows;
    }

    public static IReadOnlyList<NormalizedResource> GcpKms(string json)
    {
        using var document = JsonDocument.Parse(json);
        var count = document.RootElement.TryGetProperty("keyRings", out var rings) && rings.ValueKind == JsonValueKind.Array
            ? rings.GetArrayLength()
            : 0;
        return [new NormalizedResource("gcp", "kms", "key-rings", "key-rings", Flags(("enabled", count > 0)))];
    }

    public static IReadOnlyList<NormalizedResource> GcpAudit(string json)
    {
        using var document = JsonDocument.Parse(json);
        var count = document.RootElement.TryGetProperty("sinks", out var sinks) && sinks.ValueKind == JsonValueKind.Array
            ? sinks.GetArrayLength()
            : 0;
        return [new NormalizedResource("gcp", "trail", "audit-log", "audit-log", Flags(("enabled", count > 0)))];
    }

    public static IReadOnlyList<NormalizedResource> GcpIam(string json)
    {
        using var document = JsonDocument.Parse(json);
        var open = false;
        if (document.RootElement.TryGetProperty("bindings", out var bindings))
        {
            foreach (var binding in bindings.EnumerateArray())
            {
                if (!binding.TryGetProperty("members", out var members))
                {
                    continue;
                }

                foreach (var member in members.EnumerateArray())
                {
                    var value = member.GetString() ?? "";
                    if (value is "allUsers" or "allAuthenticatedUsers")
                    {
                        open = true;
                    }
                }
            }
        }

        return [new NormalizedResource("gcp", "iam", "project-iam", "project-iam", Flags(("public", open)))];
    }

    private static bool BlobEncrypted(JsonElement properties)
    {
        if (!properties.TryGetProperty("encryption", out var encryption))
        {
            return true;
        }

        if (!encryption.TryGetProperty("services", out var services) || !services.TryGetProperty("blob", out var blob))
        {
            return true;
        }

        return !blob.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False;
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static Dictionary<string, string> Flags(params (string Name, bool Value)[] pairs)
    {
        var values = new Dictionary<string, string>();
        foreach (var (name, value) in pairs)
        {
            values[name] = value ? "true" : "false";
        }

        return values;
    }
}
