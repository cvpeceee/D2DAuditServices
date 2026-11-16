using D2DAudit.Infrastructure.Integrations;

namespace D2DAudit.Tests;

public sealed class CloudInventoryTests
{
    [Fact]
    public void Azure_payloads_keep_public_storage_and_wide_open_rules()
    {
        var storage = CloudInventory.AzureStorage("""
            {"value":[{"name":"az-warehouse","properties":{"allowBlobPublicAccess":false,"encryption":{"services":{"blob":{"enabled":true}}}}},{"name":"az-public","properties":{"allowBlobPublicAccess":true}}]}
            """);
        Assert.Equal("false", storage[0].Attributes["public"]);
        Assert.Equal("true", storage[1].Attributes["public"]);

        var network = CloudInventory.AzureNetwork("""
            {"value":[{"name":"staging-nsg","properties":{"securityRules":[{"properties":{"access":"Allow","direction":"Inbound","sourceAddressPrefix":"0.0.0.0/0","destinationPortRange":"3389"}}]}}]}
            """);
        Assert.Equal("true", Assert.Single(network).Attributes["open"]);

        var users = CloudInventory.EntraUsers("""
            {"value":[{"id":"1","userPrincipalName":"priya@harborline.example","isMfaRegistered":true}]}
            """);
        Assert.Equal("true", Assert.Single(users).Attributes["mfa"]);
    }

    [Fact]
    public void Gcp_payloads_mark_public_buckets_and_public_iam()
    {
        var buckets = CloudInventory.GcpBuckets("""
            {"items":[{"name":"gcp-warehouse","iamConfiguration":{"publicAccessPrevention":"enforced"}},{"name":"gcp-open","iamConfiguration":{"publicAccessPrevention":"inherited"}}]}
            """);
        Assert.Equal("false", buckets[0].Attributes["public"]);
        Assert.Equal("true", buckets[1].Attributes["public"]);

        var iam = CloudInventory.GcpIam("""
            {"bindings":[{"role":"roles/viewer","members":["allUsers"]}]}
            """);
        Assert.Equal("true", Assert.Single(iam).Attributes["public"]);
        Assert.Equal("true", Assert.Single(CloudInventory.GcpKms("""{"keyRings":[{"name":"rings/1"}]}""")).Attributes["enabled"]);
        Assert.Equal("false", Assert.Single(CloudInventory.GcpAudit("""{"sinks":[]}""")).Attributes["enabled"]);
    }
}
