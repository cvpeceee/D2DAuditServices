using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D2DAudit.Domain;

public sealed class CustomTestDefinition
{
    public Guid Id { get; set; }

    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string ResourceKind { get; set; } = "";

    public string? Provider { get; set; }

    public string? FilterJson { get; set; }

    public string AssertionJson { get; set; } = "";

    public string Severity { get; set; } = "Medium";

    public string ControlCodes { get; set; } = "";

    public string Remediation { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string CreatedByName { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TestSuppression
{
    public Guid Id { get; set; }

    public string TestKey { get; set; } = "";

    public string State { get; set; } = TestSuppressionRules.Disabled;

    public string Reason { get; set; } = "";

    public DateOnly? Until { get; set; }

    public string ByName { get; set; } = "";

    public DateTimeOffset At { get; set; }
}

public interface IConditionNode
{
    int Size { get; }
}

public sealed record ConditionGroup(bool All, IReadOnlyList<IConditionNode> Children) : IConditionNode
{
    public int Size => 1 + Children.Sum(child => child.Size);
}

public sealed record ConditionLeaf(string Field, string Op, string? Value, IReadOnlyList<string> Values) : IConditionNode
{
    public int Size => 1;
}

public sealed record ConditionParse(IConditionNode? Node, string? Error)
{
    public bool IsValid => Error is null;
}

public sealed record CustomTestRun(
    TestOutcome Outcome,
    string Detail,
    IReadOnlyList<NormalizedResource> Passing,
    IReadOnlyList<NormalizedResource> Failing);

public sealed record ResourceTypeInfo(string Provider, string Kind, int Count, IReadOnlyList<string> Fields);

/// <summary>
/// Parses and evaluates JSON condition trees for custom tests. The tree is data only:
/// fields come from a fixed allow-list and operators from a closed set, so nothing in a
/// definition can execute code, reflect over types, or reach outside the resource row.
/// </summary>
public static partial class ConditionEvaluator
{
    public const int MaxJsonLength = 8000;
    public const int MaxDepth = 6;
    public const int MaxNodes = 50;
    public const int MaxValueLength = 500;
    public const int MaxListItems = 50;

    public static IReadOnlyList<string> Operators { get; } =
    [
        "equals", "notEquals", "contains", "notContains", "in", "notIn", "exists", "notExists",
        "gt", "lt", "gte", "lte", "before", "after",
    ];

    public static ConditionParse Parse(string? json, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return allowEmpty ? new ConditionParse(null, null) : new ConditionParse(null, "condition_required");
        }

        if (json.Length > MaxJsonLength)
        {
            return new ConditionParse(null, "condition_too_large");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = (MaxDepth * 2) + 4 });
            var nodes = 0;
            var error = (string?)null;
            var node = ParseNode(document.RootElement, 1, ref nodes, ref error);
            return error is null && node is not null ? new ConditionParse(node, null) : new ConditionParse(null, error ?? "condition_invalid");
        }
        catch (JsonException)
        {
            return new ConditionParse(null, "condition_invalid_json");
        }
    }

    public static bool Matches(IConditionNode node, NormalizedResource resource, DateTimeOffset now) => node switch
    {
        ConditionGroup group when group.All => group.Children.All(child => Matches(child, resource, now)),
        ConditionGroup group => group.Children.Any(child => Matches(child, resource, now)),
        ConditionLeaf leaf => Check(leaf, Resolve(leaf.Field, resource), now),
        _ => false,
    };

    public static string? Resolve(string field, NormalizedResource resource)
    {
        if (field.StartsWith("attributes.", StringComparison.Ordinal))
        {
            return resource.Attributes.TryGetValue(field["attributes.".Length..], out var value) ? value : null;
        }

        return field switch
        {
            "name" => resource.Name,
            "provider" => resource.Provider,
            "kind" => resource.Kind,
            "externalId" => resource.ExternalId,
            _ => null,
        };
    }

    public static bool ValidField(string field) => FieldPattern().IsMatch(field);

    private static IConditionNode? ParseNode(JsonElement element, int depth, ref int nodes, ref string? error)
    {
        if (++nodes > MaxNodes)
        {
            error = "condition_too_many_nodes";
            return null;
        }

        if (depth > MaxDepth)
        {
            error = "condition_too_deep";
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "condition_invalid";
            return null;
        }

        var hasAll = element.TryGetProperty("all", out var all);
        var hasAny = element.TryGetProperty("any", out var any);
        if (hasAll || hasAny)
        {
            if ((hasAll && hasAny) || CountProperties(element) != 1)
            {
                error = "condition_invalid";
                return null;
            }

            var list = hasAll ? all : any;
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
            {
                error = "condition_empty_group";
                return null;
            }

            var children = new List<IConditionNode>();
            foreach (var child in list.EnumerateArray())
            {
                var parsed = ParseNode(child, depth + 1, ref nodes, ref error);
                if (parsed is null)
                {
                    return null;
                }

                children.Add(parsed);
            }

            return new ConditionGroup(hasAll, children);
        }

        if (element.EnumerateObject().Any(property => property.Name is not ("field" or "op" or "value")))
        {
            error = "condition_unknown_property";
            return null;
        }

        if (!element.TryGetProperty("field", out var fieldElement) || fieldElement.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("op", out var opElement) || opElement.ValueKind != JsonValueKind.String)
        {
            error = "condition_invalid";
            return null;
        }

        var field = fieldElement.GetString() ?? "";
        if (!ValidField(field))
        {
            error = "condition_invalid_field";
            return null;
        }

        var op = opElement.GetString() ?? "";
        if (!Operators.Contains(op, StringComparer.Ordinal))
        {
            error = "condition_invalid_operator";
            return null;
        }

        element.TryGetProperty("value", out var valueElement);
        var hasValue = valueElement.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
        if (op is "exists" or "notExists")
        {
            return new ConditionLeaf(field, op, null, []);
        }

        if (!hasValue)
        {
            error = "condition_value_required";
            return null;
        }

        if (op is "in" or "notIn")
        {
            if (valueElement.ValueKind != JsonValueKind.Array || valueElement.GetArrayLength() is 0 or > MaxListItems)
            {
                error = "condition_invalid_list";
                return null;
            }

            var values = new List<string>();
            foreach (var item in valueElement.EnumerateArray())
            {
                var scalar = Scalar(item);
                if (scalar is null)
                {
                    error = "condition_invalid_value";
                    return null;
                }

                values.Add(scalar);
            }

            return new ConditionLeaf(field, op, null, values);
        }

        var value = Scalar(valueElement);
        if (value is null)
        {
            error = "condition_invalid_value";
            return null;
        }

        if (op is "gt" or "lt" or "gte" or "lte" && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            error = "condition_invalid_number";
            return null;
        }

        if (op is "before" or "after" && ParseDate(value, DateTimeOffset.UnixEpoch) is null)
        {
            error = "condition_invalid_date";
            return null;
        }

        return new ConditionLeaf(field, op, value, []);
    }

    private static int CountProperties(JsonElement element)
    {
        var count = 0;
        foreach (var _ in element.EnumerateObject())
        {
            count++;
        }

        return count;
    }

    private static string? Scalar(JsonElement element)
    {
        var text = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
        return text is null || text.Length > MaxValueLength ? null : text;
    }

    private static bool Check(ConditionLeaf leaf, string? actual, DateTimeOffset now)
    {
        var present = !string.IsNullOrEmpty(actual);
        return leaf.Op switch
        {
            "exists" => present,
            "notExists" => !present,
            "equals" => present && string.Equals(actual, leaf.Value, StringComparison.OrdinalIgnoreCase),
            "notEquals" => !present || !string.Equals(actual, leaf.Value, StringComparison.OrdinalIgnoreCase),
            "contains" => present && actual!.Contains(leaf.Value!, StringComparison.OrdinalIgnoreCase),
            "notContains" => !present || !actual!.Contains(leaf.Value!, StringComparison.OrdinalIgnoreCase),
            "in" => present && leaf.Values.Contains(actual!, StringComparer.OrdinalIgnoreCase),
            "notIn" => !present || !leaf.Values.Contains(actual!, StringComparer.OrdinalIgnoreCase),
            "gt" or "lt" or "gte" or "lte" => Compare(actual, leaf.Value, leaf.Op),
            "before" or "after" => CompareDates(actual, leaf.Value, leaf.Op, now),
            _ => false,
        };
    }

    private static bool Compare(string? actual, string? expected, string op)
    {
        if (!decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var left)
            || !decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var right))
        {
            return false;
        }

        return op switch
        {
            "gt" => left > right,
            "lt" => left < right,
            "gte" => left >= right,
            _ => left <= right,
        };
    }

    private static bool CompareDates(string? actual, string? expected, string op, DateTimeOffset now)
    {
        var left = ParseDate(actual, now);
        var right = ParseDate(expected, now);
        if (left is null || right is null)
        {
            return false;
        }

        return op == "before" ? left < right : left > right;
    }

    /// <summary>Accepts ISO dates or relative tokens such as <c>now</c>, <c>now-30d</c>, <c>now+7d</c>.</summary>
    public static DateTimeOffset? ParseDate(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var relative = RelativeDate().Match(value.Trim());
        if (relative.Success)
        {
            if (!relative.Groups[1].Success)
            {
                return now;
            }

            var days = int.Parse(relative.Groups[2].Value, CultureInfo.InvariantCulture);
            return now.AddDays(relative.Groups[1].Value == "-" ? -days : days);
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    [GeneratedRegex("^(name|provider|kind|externalId|attributes\\.[A-Za-z0-9_\\-]{1,64})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex FieldPattern();

    [GeneratedRegex("^now(?:([+-])(\\d{1,4})d)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex RelativeDate();
}

public static partial class CustomTestEngine
{
    public static IReadOnlyList<string> Severities { get; } = ["Low", "Medium", "High", "Critical"];

    public static CustomTestRun Run(
        string resourceKind,
        string? provider,
        IConditionNode? filter,
        IConditionNode assertion,
        IEnumerable<NormalizedResource> resources,
        DateTimeOffset now)
    {
        var scope = resources
            .Where(item => resourceKind == "*" || string.Equals(item.Kind, resourceKind, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrEmpty(provider) || string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Where(item => filter is null || ConditionEvaluator.Matches(filter, item, now))
            .ToArray();
        if (scope.Length == 0)
        {
            return new CustomTestRun(TestOutcome.NotRun, "No matching resources in the inventory.", [], []);
        }

        var passing = new List<NormalizedResource>();
        var failing = new List<NormalizedResource>();
        foreach (var item in scope)
        {
            (ConditionEvaluator.Matches(assertion, item, now) ? passing : failing).Add(item);
        }

        var more = failing.Count > 5 ? ", …" : "";
        var detail = failing.Count == 0
            ? $"{passing.Count} of {scope.Length} resources pass."
            : $"{failing.Count} of {scope.Length} resources fail: {string.Join(", ", failing.Take(5).Select(item => item.Name))}{more}";
        return new CustomTestRun(failing.Count == 0 ? TestOutcome.Pass : TestOutcome.Fail, detail, passing, failing);
    }

    public static CustomTestRun Run(CustomTestDefinition definition, IEnumerable<NormalizedResource> resources, DateTimeOffset now)
    {
        var filter = ConditionEvaluator.Parse(definition.FilterJson, allowEmpty: true);
        var assertion = ConditionEvaluator.Parse(definition.AssertionJson, allowEmpty: false);
        if (!filter.IsValid || !assertion.IsValid || assertion.Node is null)
        {
            return new CustomTestRun(TestOutcome.NotRun, "The test definition is not valid.", [], []);
        }

        return Run(definition.ResourceKind, definition.Provider, filter.Node, assertion.Node, resources, now);
    }

    public static RuleResult ValidateDefinition(
        string? name,
        string? resourceKind,
        string? provider,
        string? severity,
        IReadOnlyList<string>? controlCodes,
        string? remediation)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length is < 3 or > 200)
        {
            return RuleResult.Fail("invalid_name");
        }

        if (string.IsNullOrWhiteSpace(resourceKind) || !KindPattern().IsMatch(resourceKind))
        {
            return RuleResult.Fail("invalid_resource_kind");
        }

        if (!string.IsNullOrEmpty(provider) && !KindPattern().IsMatch(provider))
        {
            return RuleResult.Fail("invalid_provider");
        }

        if (string.IsNullOrWhiteSpace(severity) || !Severities.Contains(severity, StringComparer.Ordinal))
        {
            return RuleResult.Fail("invalid_severity");
        }

        if (controlCodes is null || controlCodes.Count is 0 or > 10 || controlCodes.Any(code => string.IsNullOrWhiteSpace(code) || code.Length > 32))
        {
            return RuleResult.Fail("invalid_controls");
        }

        return (remediation ?? "").Length > 2000 ? RuleResult.Fail("invalid_remediation") : RuleResult.Ok();
    }

    public static string KeyFor(string name, IEnumerable<string> existing)
    {
        var slug = Slug().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 48)
        {
            slug = slug[..48].Trim('-');
        }

        var baseKey = "custom-" + (slug.Length == 0 ? "test" : slug);
        var taken = existing.ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(baseKey))
        {
            return baseKey;
        }

        return Enumerable.Range(2, taken.Count + 1)
            .Select(index => baseKey + "-" + index.ToString(CultureInfo.InvariantCulture))
            .First(candidate => !taken.Contains(candidate));
    }

    public static IReadOnlyList<string> SplitCodes(string codes) =>
        codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IReadOnlyList<ResourceTypeInfo> Describe(IEnumerable<NormalizedResource> resources) =>
        resources
            .GroupBy(item => (item.Provider, item.Kind))
            .Select(group => new ResourceTypeInfo(
                group.Key.Provider,
                group.Key.Kind,
                group.Count(),
                new[] { "name", "provider", "kind", "externalId" }
                    .Concat(group.SelectMany(item => item.Attributes.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(40).Select(key => "attributes." + key))
                    .ToArray()))
            .OrderBy(item => item.Provider, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ToArray();

    public static NormalizedResource FromComputer(ComputerRecord computer) => new(
        "d2d",
        "computer",
        computer.Serial,
        computer.Serial,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operatingSystem"] = computer.OperatingSystem,
            ["ownerEmail"] = computer.OwnerEmail,
            ["encrypted"] = Flag(computer.Encrypted),
            ["screenLock"] = Flag(computer.ScreenLock),
            ["antivirus"] = Flag(computer.Antivirus),
            ["passwordManager"] = Flag(computer.PasswordManager),
            ["autoUpdate"] = Flag(computer.AutoUpdate),
            ["lastCheckIn"] = computer.LastCheckIn.ToString("O", CultureInfo.InvariantCulture),
            ["source"] = computer.Source,
        });

    public static NormalizedResource FromPerson(DirectoryPerson person) => new(
        "d2d",
        "person",
        person.Email,
        person.Name,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["email"] = person.Email,
            ["department"] = person.Department,
            ["role"] = person.Role,
            ["state"] = person.State.ToString(),
            ["training"] = person.Training.ToString(),
            ["mfaEnrolled"] = Flag(person.MfaEnrolled),
            ["background"] = person.BackgroundStatus,
            ["device"] = person.DeviceStatus,
            ["hireOn"] = person.HireOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        });

    public static NormalizedResource FromVulnerability(VulnerabilityRecord vulnerability) => new(
        "d2d",
        "vulnerability",
        vulnerability.Id.ToString("N"),
        string.IsNullOrEmpty(vulnerability.Cve) ? vulnerability.Title : vulnerability.Cve + " · " + vulnerability.AssetName,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cve"] = vulnerability.Cve,
            ["severity"] = vulnerability.Severity,
            ["asset"] = vulnerability.AssetName,
            ["source"] = vulnerability.Source,
            ["slaDue"] = vulnerability.SlaDue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["fixAvailable"] = Flag(vulnerability.FixAvailable),
            ["ignored"] = Flag(vulnerability.Ignored),
            ["firstSeen"] = vulnerability.FirstSeen.ToString("O", CultureInfo.InvariantCulture),
        });

    private static string Flag(bool value) => value ? "true" : "false";

    [GeneratedRegex("^(\\*|[A-Za-z][A-Za-z0-9\\-]{0,40})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex KindPattern();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Slug();
}

public static class TestSuppressionRules
{
    public const string Disabled = "Disabled";
    public const string Snoozed = "Snoozed";
    public const int MaxSnoozeDays = 365;

    public static RuleResult Validate(string? state, string? reason, DateOnly? until, DateOnly today)
    {
        if (state is not (Disabled or Snoozed))
        {
            return RuleResult.Fail("invalid_state");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500)
        {
            return RuleResult.Fail("reason_required");
        }

        if (state == Snoozed)
        {
            if (until is null || until <= today)
            {
                return RuleResult.Fail("invalid_until");
            }

            if (until > today.AddDays(MaxSnoozeDays))
            {
                return RuleResult.Fail("snooze_too_long");
            }
        }

        return RuleResult.Ok();
    }

    public static bool IsActive(string state, DateOnly? until, DateOnly today) =>
        state == Disabled || (state == Snoozed && until is not null && until > today);

    public static string Describe(string state, string reason, DateOnly? until) =>
        state == Snoozed
            ? $"Snoozed until {until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: {reason}"
            : $"Disabled: {reason}";
}
