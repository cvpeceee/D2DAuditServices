using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class CustomTestEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static NormalizedResource Bucket(string name, params (string Key, string Value)[] attributes) =>
        new("aws", "bucket", "arn:" + name, name, attributes.ToDictionary(item => item.Key, item => item.Value));

    private static bool Eval(string json, NormalizedResource resource)
    {
        var parsed = ConditionEvaluator.Parse(json, allowEmpty: false);
        Assert.True(parsed.IsValid, parsed.Error);
        return ConditionEvaluator.Matches(parsed.Node!, resource, Now);
    }

    [Theory]
    [InlineData("""{"field":"attributes.versioning","op":"equals","value":"TRUE"}""", true)]
    [InlineData("""{"field":"attributes.versioning","op":"equals","value":true}""", true)]
    [InlineData("""{"field":"attributes.versioning","op":"notEquals","value":"true"}""", false)]
    [InlineData("""{"field":"name","op":"contains","value":"-PROD"}""", true)]
    [InlineData("""{"field":"name","op":"notContains","value":"dev"}""", true)]
    [InlineData("""{"field":"attributes.region","op":"in","value":["eu-west-1","us-east-1"]}""", true)]
    [InlineData("""{"field":"attributes.region","op":"notIn","value":["eu-west-1"]}""", false)]
    [InlineData("""{"field":"attributes.region","op":"exists"}""", true)]
    [InlineData("""{"field":"attributes.missing","op":"notExists"}""", true)]
    [InlineData("""{"field":"attributes.size","op":"gt","value":10}""", true)]
    [InlineData("""{"field":"attributes.size","op":"lt","value":"10"}""", false)]
    [InlineData("""{"field":"attributes.size","op":"gte","value":12.5}""", true)]
    [InlineData("""{"field":"attributes.size","op":"lte","value":12}""", false)]
    [InlineData("""{"field":"attributes.created","op":"before","value":"2026-01-01"}""", true)]
    [InlineData("""{"field":"attributes.created","op":"after","value":"now-365d"}""", true)]
    [InlineData("""{"field":"attributes.created","op":"after","value":"now"}""", false)]
    [InlineData("""{"field":"provider","op":"equals","value":"aws"}""", true)]
    public void Operators_evaluate_against_resource_fields(string json, bool expected)
    {
        var resource = Bucket("logs-prod", ("versioning", "true"), ("region", "eu-west-1"), ("size", "12.5"), ("created", "2025-11-01T00:00:00Z"));
        Assert.Equal(expected, Eval(json, resource));
    }

    [Fact]
    public void Missing_fields_fail_positive_ops_and_pass_negative_ops()
    {
        var resource = Bucket("x");
        Assert.False(Eval("""{"field":"attributes.v","op":"equals","value":"true"}""", resource));
        Assert.True(Eval("""{"field":"attributes.v","op":"notEquals","value":"true"}""", resource));
        Assert.False(Eval("""{"field":"attributes.v","op":"gt","value":1}""", resource));
        Assert.False(Eval("""{"field":"attributes.v","op":"before","value":"now"}""", resource));
    }

    [Fact]
    public void Non_numeric_actual_values_never_compare()
    {
        Assert.False(Eval("""{"field":"name","op":"gt","value":1}""", Bucket("abc")));
    }

    [Fact]
    public void Groups_combine_with_and_or_and_nest()
    {
        var resource = Bucket("logs-prod", ("versioning", "false"), ("encrypted", "true"));
        const string any = """{"any":[{"field":"attributes.versioning","op":"equals","value":"true"},{"field":"attributes.encrypted","op":"equals","value":"true"}]}""";
        const string all = """{"all":[{"field":"attributes.versioning","op":"equals","value":"true"},{"field":"attributes.encrypted","op":"equals","value":"true"}]}""";
        const string nested = """{"all":[{"field":"name","op":"contains","value":"prod"},{"any":[{"field":"attributes.versioning","op":"equals","value":"true"},{"field":"attributes.encrypted","op":"exists"}]}]}""";
        Assert.True(Eval(any, resource));
        Assert.False(Eval(all, resource));
        Assert.True(Eval(nested, resource));
    }

    [Theory]
    [InlineData(null, "condition_required")]
    [InlineData("   ", "condition_required")]
    [InlineData("{not json", "condition_invalid_json")]
    [InlineData("[]", "condition_invalid")]
    [InlineData("\"equals\"", "condition_invalid")]
    [InlineData("""{"all":[]}""", "condition_empty_group")]
    [InlineData("""{"all":{"field":"name"}}""", "condition_empty_group")]
    [InlineData("""{"all":[{"field":"name","op":"exists"}],"any":[{"field":"name","op":"exists"}]}""", "condition_invalid")]
    [InlineData("""{"all":[{"field":"name","op":"exists"}],"extra":1}""", "condition_invalid")]
    [InlineData("""{"field":"name","op":"exists","script":"rm -rf /"}""", "condition_unknown_property")]
    [InlineData("""{"field":"name"}""", "condition_invalid")]
    [InlineData("""{"field":1,"op":"equals","value":"x"}""", "condition_invalid")]
    [InlineData("""{"field":"GetType().Assembly","op":"equals","value":"x"}""", "condition_invalid_field")]
    [InlineData("""{"field":"attributes.a.b","op":"equals","value":"x"}""", "condition_invalid_field")]
    [InlineData("""{"field":"attributes.","op":"equals","value":"x"}""", "condition_invalid_field")]
    [InlineData("""{"field":"Attributes","op":"equals","value":"x"}""", "condition_invalid_field")]
    [InlineData("""{"field":"name","op":"matches","value":".*"}""", "condition_invalid_operator")]
    [InlineData("""{"field":"name","op":"EQUALS","value":"x"}""", "condition_invalid_operator")]
    [InlineData("""{"field":"name","op":"equals"}""", "condition_value_required")]
    [InlineData("""{"field":"name","op":"equals","value":null}""", "condition_value_required")]
    [InlineData("""{"field":"name","op":"equals","value":{"$where":"1"}}""", "condition_invalid_value")]
    [InlineData("""{"field":"name","op":"equals","value":["a"]}""", "condition_invalid_value")]
    [InlineData("""{"field":"name","op":"in","value":"a"}""", "condition_invalid_list")]
    [InlineData("""{"field":"name","op":"in","value":[]}""", "condition_invalid_list")]
    [InlineData("""{"field":"name","op":"in","value":[["a"]]}""", "condition_invalid_value")]
    [InlineData("""{"field":"name","op":"gt","value":"ten"}""", "condition_invalid_number")]
    [InlineData("""{"field":"name","op":"before","value":"someday"}""", "condition_invalid_date")]
    [InlineData("""{"field":"name","op":"before","value":"now-99999d"}""", "condition_invalid_date")]
    public void Invalid_or_malicious_conditions_are_rejected(string? json, string error)
    {
        var parsed = ConditionEvaluator.Parse(json, allowEmpty: false);
        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Node);
        Assert.Equal(error, parsed.Error);
    }

    [Fact]
    public void Empty_filter_is_allowed_when_optional()
    {
        var parsed = ConditionEvaluator.Parse(null, allowEmpty: true);
        Assert.True(parsed.IsValid);
        Assert.Null(parsed.Node);
    }

    [Fact]
    public void Deep_nesting_is_rejected()
    {
        var json = """{"field":"name","op":"exists"}""";
        for (var i = 0; i < ConditionEvaluator.MaxDepth; i++)
        {
            json = "{\"all\":[" + json + "]}";
        }

        Assert.Equal("condition_too_deep", ConditionEvaluator.Parse(json, false).Error);
    }

    [Fact]
    public void Extremely_deep_json_is_rejected_without_crashing()
    {
        var json = string.Concat(Enumerable.Repeat("{\"all\":[", 500)) + string.Concat(Enumerable.Repeat("]}", 500));
        var parsed = ConditionEvaluator.Parse(json, false);
        Assert.False(parsed.IsValid);
    }

    [Fact]
    public void Too_many_nodes_are_rejected()
    {
        var leaves = string.Join(",", Enumerable.Repeat("""{"field":"name","op":"exists"}""", ConditionEvaluator.MaxNodes));
        Assert.Equal("condition_too_many_nodes", ConditionEvaluator.Parse("{\"any\":[" + leaves + "]}", false).Error);
    }

    [Fact]
    public void Oversized_payloads_and_values_are_rejected()
    {
        Assert.Equal("condition_too_large", ConditionEvaluator.Parse(new string(' ', 10) + new string('x', ConditionEvaluator.MaxJsonLength), false).Error);
        var longValue = new string('a', ConditionEvaluator.MaxValueLength + 1);
        Assert.Equal("condition_invalid_value", ConditionEvaluator.Parse($$"""{"field":"name","op":"equals","value":"{{longValue}}"}""", false).Error);
        var list = string.Join(",", Enumerable.Range(0, ConditionEvaluator.MaxListItems + 1).Select(i => $"\"{i}\""));
        Assert.Equal("condition_invalid_list", ConditionEvaluator.Parse($$"""{"field":"name","op":"in","value":[{{list}}]}""", false).Error);
    }

    [Fact]
    public void Relative_dates_resolve_against_now()
    {
        Assert.Equal(Now, ConditionEvaluator.ParseDate("now", Now));
        Assert.Equal(Now.AddDays(-30), ConditionEvaluator.ParseDate("now-30d", Now));
        Assert.Equal(Now.AddDays(7), ConditionEvaluator.ParseDate(" now+7d ", Now));
        Assert.Null(ConditionEvaluator.ParseDate("now-7h", Now));
        Assert.Null(ConditionEvaluator.ParseDate("", Now));
    }

    [Fact]
    public void Resolve_reads_only_the_allow_listed_fields()
    {
        var resource = Bucket("logs-prod", ("versioning", "true"));
        Assert.Equal("logs-prod", ConditionEvaluator.Resolve("name", resource));
        Assert.Equal("bucket", ConditionEvaluator.Resolve("kind", resource));
        Assert.Equal("arn:logs-prod", ConditionEvaluator.Resolve("externalId", resource));
        Assert.Equal("true", ConditionEvaluator.Resolve("attributes.versioning", resource));
        Assert.Null(ConditionEvaluator.Resolve("Attributes", resource));
        Assert.Null(ConditionEvaluator.Resolve("attributes.nope", resource));
    }

    [Fact]
    public void Engine_reports_failing_resources_in_scope()
    {
        var definition = new CustomTestDefinition
        {
            Name = "Production S3 buckets must have versioning",
            ResourceKind = "bucket",
            Provider = "aws",
            FilterJson = """{"field":"name","op":"contains","value":"-prod"}""",
            AssertionJson = """{"field":"attributes.versioning","op":"equals","value":"true"}""",
        };
        var resources = new[]
        {
            Bucket("logs-prod", ("versioning", "true")),
            Bucket("stages-prod", ("versioning", "false")),
            Bucket("scratch-dev", ("versioning", "false")),
            new NormalizedResource("gcp", "bucket", "g", "gcs-prod", new Dictionary<string, string>()),
            new NormalizedResource("aws", "user", "u", "ops-prod", new Dictionary<string, string>()),
        };

        var run = CustomTestEngine.Run(definition, resources, Now);

        Assert.Equal(TestOutcome.Fail, run.Outcome);
        Assert.Equal(["stages-prod"], run.Failing.Select(item => item.Name));
        Assert.Equal(["logs-prod"], run.Passing.Select(item => item.Name));
        Assert.Contains("1 of 2 resources fail: stages-prod", run.Detail);
    }

    [Fact]
    public void Engine_passes_when_every_resource_matches_and_supports_any_kind()
    {
        var assertion = ConditionEvaluator.Parse("""{"field":"name","op":"exists"}""", false).Node!;
        var run = CustomTestEngine.Run("*", null, null, assertion, [Bucket("a"), new NormalizedResource("github", "repo", "r", "web", new Dictionary<string, string>())], Now);
        Assert.Equal(TestOutcome.Pass, run.Outcome);
        Assert.Equal(2, run.Passing.Count);
    }

    [Fact]
    public void Engine_does_not_run_with_no_scope_or_invalid_definition()
    {
        var assertion = ConditionEvaluator.Parse("""{"field":"name","op":"exists"}""", false).Node!;
        Assert.Equal(TestOutcome.NotRun, CustomTestEngine.Run("repo", null, null, assertion, [Bucket("a")], Now).Outcome);
        var broken = new CustomTestDefinition { ResourceKind = "bucket", AssertionJson = "{bad" };
        Assert.Equal(TestOutcome.NotRun, CustomTestEngine.Run(broken, [Bucket("a")], Now).Outcome);
    }

    [Fact]
    public void Engine_truncates_long_failure_lists()
    {
        var assertion = ConditionEvaluator.Parse("""{"field":"attributes.ok","op":"exists"}""", false).Node!;
        var run = CustomTestEngine.Run("bucket", null, null, assertion, Enumerable.Range(1, 7).Select(i => Bucket("b" + i)), Now);
        Assert.EndsWith(", …", run.Detail);
        Assert.DoesNotContain("b6", run.Detail);
    }

    [Theory]
    [InlineData("ok", "bucket", null, "High", "invalid_name")]
    [InlineData("Valid name", "bucket; drop", null, "High", "invalid_resource_kind")]
    [InlineData("Valid name", "bucket", "../aws", "High", "invalid_provider")]
    [InlineData("Valid name", "bucket", "aws", "Severe", "invalid_severity")]
    [InlineData("Valid name", "*", null, "Low", null)]
    public void Definitions_are_validated(string name, string kind, string? provider, string severity, string? error)
    {
        var result = CustomTestEngine.ValidateDefinition(name, kind, provider, severity, ["CR-02"], "Turn it on.");
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Definitions_require_one_to_ten_controls_and_bounded_remediation()
    {
        Assert.Equal("invalid_controls", CustomTestEngine.ValidateDefinition("Valid", "bucket", null, "Low", [], null).Error);
        Assert.Equal("invalid_controls", CustomTestEngine.ValidateDefinition("Valid", "bucket", null, "Low", Enumerable.Repeat("CR-02", 11).ToArray(), null).Error);
        Assert.Equal("invalid_remediation", CustomTestEngine.ValidateDefinition("Valid", "bucket", null, "Low", ["CR-02"], new string('x', 2001)).Error);
    }

    [Fact]
    public void Keys_are_slugged_and_unique()
    {
        Assert.Equal("custom-production-s3-buckets", CustomTestEngine.KeyFor("  Production S3 buckets!! ", []));
        Assert.Equal("custom-test", CustomTestEngine.KeyFor("!!!", []));
        Assert.Equal("custom-a-3", CustomTestEngine.KeyFor("A", ["custom-a", "custom-a-2"]));
        Assert.True(CustomTestEngine.KeyFor(new string('a', 200), []).Length <= 55);
    }

    [Fact]
    public void Describe_lists_fields_per_resource_type()
    {
        var types = CustomTestEngine.Describe([Bucket("a", ("versioning", "true")), Bucket("b", ("region", "x"))]);
        var bucket = Assert.Single(types);
        Assert.Equal(2, bucket.Count);
        Assert.Contains("attributes.region", bucket.Fields);
        Assert.Contains("attributes.versioning", bucket.Fields);
    }

    [Fact]
    public void Snooze_requires_reason_and_bounded_future_expiry()
    {
        var today = new DateOnly(2026, 10, 21);
        Assert.True(TestSuppressionRules.Validate("Disabled", "Not applicable", null, today).Succeeded);
        Assert.Equal("invalid_state", TestSuppressionRules.Validate("Muted", "Reason", null, today).Error);
        Assert.Equal("reason_required", TestSuppressionRules.Validate("Disabled", " ", null, today).Error);
        Assert.Equal("invalid_until", TestSuppressionRules.Validate("Snoozed", "Vendor fix", today, today).Error);
        Assert.Equal("snooze_too_long", TestSuppressionRules.Validate("Snoozed", "Vendor fix", today.AddDays(366), today).Error);
        Assert.True(TestSuppressionRules.Validate("Snoozed", "Vendor fix", today.AddDays(30), today).Succeeded);
    }

    [Fact]
    public void Snoozes_expire_but_disables_do_not()
    {
        var today = new DateOnly(2026, 10, 21);
        Assert.True(TestSuppressionRules.IsActive("Disabled", null, today));
        Assert.True(TestSuppressionRules.IsActive("Snoozed", today.AddDays(1), today));
        Assert.False(TestSuppressionRules.IsActive("Snoozed", today, today));
        Assert.StartsWith("Snoozed until 2026-11-01", TestSuppressionRules.Describe("Snoozed", "Vendor fix", new DateOnly(2026, 11, 1)));
    }
}
