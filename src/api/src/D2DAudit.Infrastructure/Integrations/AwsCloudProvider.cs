using Amazon;
using Amazon.CloudTrail;
using Amazon.CloudTrail.Model;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using D2DAudit.Application;
using D2DAudit.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Integrations;

public sealed class AwsCloudProvider(IConfiguration configuration, ILogger<AwsCloudProvider> logger) : ICloudProvider
{
    public string ProviderKey => "aws";

    public async Task<Result<ProviderConnect>> ConnectAsync(
        ProviderConnection? existing,
        ConnectIntegrationRequest request,
        CancellationToken cancellationToken)
    {
        if (existing?.Example == true)
        {
            return Result<ProviderConnect>.Ok(Describe(existing, request.RoleArn));
        }

        var roleArn = string.IsNullOrWhiteSpace(request.RoleArn) ? existing?.RoleArn : request.RoleArn.Trim();
        if (string.IsNullOrWhiteSpace(roleArn))
        {
            return Result<ProviderConnect>.Fail("role_arn_required");
        }

        if (!IntegrationPolicies.IsAwsRoleArn(roleArn))
        {
            return Result<ProviderConnect>.Fail("invalid_role_arn");
        }

        var externalId = string.IsNullOrWhiteSpace(existing?.ExternalId) ? Guid.NewGuid().ToString("N") : existing.ExternalId;
        var assumed = await TryAssumeAsync(roleArn, externalId, cancellationToken);
        var status = assumed ? "connected" : "pending";
        var hint = assumed
            ? "Read-only role assumed with the external id."
            : "Apply the trust policy, then connect again. No access keys are stored.";
        return Result<ProviderConnect>.Ok(Connected(status, externalId, roleArn, hint));
    }

    public async Task<Result<ProviderCollect>> CollectAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Example)
        {
            return Result<ProviderCollect>.Ok(new ProviderCollect(ExampleSnapshots.For(ProviderKey, connection.Fixture)));
        }

        if (string.IsNullOrWhiteSpace(connection.RoleArn) || string.IsNullOrWhiteSpace(connection.ExternalId))
        {
            return Result<ProviderCollect>.Fail("not_connected");
        }

        try
        {
            var session = await AssumeAsync(connection.RoleArn, connection.ExternalId, cancellationToken);
            var resources = new List<NormalizedResource>();
            using var sts = new AmazonSecurityTokenServiceClient(session, RegionEndpoint.USEast1);
            var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), cancellationToken);
            using var iam = new AmazonIdentityManagementServiceClient(session, RegionEndpoint.USEast1);
            var summary = await iam.GetAccountSummaryAsync(new GetAccountSummaryRequest(), cancellationToken);
            summary.SummaryMap.TryGetValue("AccountMFAEnabled", out var mfaFlag);
            resources.Add(new NormalizedResource(
                "aws",
                "root",
                identity.Account,
                "root:" + identity.Account,
                Flag(("mfa", mfaFlag == 1))));

            using var s3 = new AmazonS3Client(session, RegionEndpoint.USEast1);
            var buckets = await s3.ListBucketsAsync(cancellationToken);
            foreach (var name in buckets.Buckets.Select(bucket => bucket.BucketName))
            {
                var blocked = await PublicAccessBlockedAsync(s3, session, name, cancellationToken);
                var encrypted = await EncryptedAsync(s3, session, name, cancellationToken);
                resources.Add(new NormalizedResource("aws", "bucket", name, name, Flag(("public", !blocked), ("encrypted", encrypted))));
            }

            using var trails = new AmazonCloudTrailClient(session, RegionEndpoint.USEast1);
            var described = await trails.DescribeTrailsAsync(new DescribeTrailsRequest { IncludeShadowTrails = false }, cancellationToken);
            foreach (var trail in described.TrailList)
            {
                var status = await trails.GetTrailStatusAsync(new GetTrailStatusRequest { Name = trail.TrailARN }, cancellationToken);
                resources.Add(new NormalizedResource("aws", "trail", trail.Name, trail.Name, Flag(("enabled", status.IsLogging == true))));
            }

            return Result<ProviderCollect>.Ok(new ProviderCollect(resources));
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException)
        {
            logger.LogWarning(exception, "AWS collection failed. {ExceptionType}", exception.GetType().Name);
            return Result<ProviderCollect>.Fail("aws_collect_failed");
        }
    }

    private ProviderConnect Describe(ProviderConnection existing, string? roleArn)
    {
        var arn = string.IsNullOrWhiteSpace(roleArn) ? existing.RoleArn : roleArn.Trim();
        if (!string.IsNullOrWhiteSpace(arn) && !IntegrationPolicies.IsAwsRoleArn(arn))
        {
            arn = existing.RoleArn;
        }

        var externalId = string.IsNullOrWhiteSpace(existing.ExternalId) ? Guid.NewGuid().ToString("N") : existing.ExternalId;
        return Connected("connected", externalId, arn, "Example fixture. The trust policy is real text; collection uses the fixture.");
    }

    private ProviderConnect Connected(string status, string externalId, string? roleArn, string hint) =>
        new(
            status,
            externalId,
            roleArn,
            roleArn is null ? null : "aws:" + roleArn.Split(':').ElementAtOrDefault(4),
            null,
            null,
            null,
            null,
            IntegrationPolicies.AwsTrustPolicy(IntegrationPolicies.CollectorArn(configuration), externalId),
            IntegrationPolicies.AwsPermissions,
            hint);

    private async Task<bool> TryAssumeAsync(string roleArn, string externalId, CancellationToken cancellationToken)
    {
        try
        {
            _ = await AssumeAsync(roleArn, externalId, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException)
        {
            logger.LogWarning(exception, "AssumeRole did not succeed. {ExceptionType}", exception.GetType().Name);
            return false;
        }
    }

    private static async Task<SessionAWSCredentials> AssumeAsync(string roleArn, string externalId, CancellationToken cancellationToken)
    {
        using var sts = new AmazonSecurityTokenServiceClient(new AmazonSecurityTokenServiceConfig { RegionEndpoint = RegionEndpoint.USEast1 });
        var assumed = await sts.AssumeRoleAsync(
            new AssumeRoleRequest
            {
                RoleArn = roleArn,
                RoleSessionName = "d2d-collection",
                ExternalId = externalId,
                DurationSeconds = 900,
            },
            cancellationToken);
        var credentials = assumed.Credentials;
        return new SessionAWSCredentials(credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken);
    }

    private static async Task<bool> PublicAccessBlockedAsync(
        AmazonS3Client fallback,
        SessionAWSCredentials session,
        string bucket,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await ClientForBucketAsync(fallback, session, bucket, cancellationToken);
            var block = await client.GetPublicAccessBlockAsync(new GetPublicAccessBlockRequest { BucketName = bucket }, cancellationToken);
            var config = block.PublicAccessBlockConfiguration;
            return config.BlockPublicAcls == true && config.IgnorePublicAcls == true && config.BlockPublicPolicy == true && config.RestrictPublicBuckets == true;
        }
        catch (AmazonS3Exception)
        {
            return false;
        }
    }

    private static async Task<bool> EncryptedAsync(
        AmazonS3Client fallback,
        SessionAWSCredentials session,
        string bucket,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await ClientForBucketAsync(fallback, session, bucket, cancellationToken);
            var encryption = await client.GetBucketEncryptionAsync(new GetBucketEncryptionRequest { BucketName = bucket }, cancellationToken);
            return encryption.ServerSideEncryptionConfiguration?.ServerSideEncryptionRules?.Count > 0;
        }
        catch (AmazonS3Exception)
        {
            return false;
        }
    }

    private static async Task<AmazonS3Client> ClientForBucketAsync(
        AmazonS3Client fallback,
        SessionAWSCredentials session,
        string bucket,
        CancellationToken cancellationToken)
    {
        try
        {
            var location = await fallback.GetBucketLocationAsync(new GetBucketLocationRequest { BucketName = bucket }, cancellationToken);
            var region = string.IsNullOrWhiteSpace(location.Location?.Value) ? "us-east-1" : location.Location.Value;
            if (region == "us-east-1")
            {
                return new AmazonS3Client(session, RegionEndpoint.USEast1);
            }

            return new AmazonS3Client(session, RegionEndpoint.GetBySystemName(region));
        }
        catch (AmazonS3Exception)
        {
            return new AmazonS3Client(session, RegionEndpoint.USEast1);
        }
    }

    private static Dictionary<string, string> Flag(params (string Name, bool Value)[] flags)
    {
        var values = new Dictionary<string, string>();
        foreach (var (name, value) in flags)
        {
            values[name] = value ? "true" : "false";
        }

        return values;
    }
}
