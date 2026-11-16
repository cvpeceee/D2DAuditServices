using Amazon;
using Amazon.Inspector2;
using Amazon.Inspector2.Model;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using D2DAudit.Application;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Operations;

public sealed class AwsInspectorFeed(ILogger<AwsInspectorFeed> logger)
{
    public async Task<IReadOnlyList<ImportedFinding>?> ListAsync(string? roleArn, string? externalId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleArn) || string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        try
        {
            using var sts = new AmazonSecurityTokenServiceClient(new AmazonSecurityTokenServiceConfig { RegionEndpoint = RegionEndpoint.USEast1 });
            var assumed = await sts.AssumeRoleAsync(
                new AssumeRoleRequest
                {
                    RoleArn = roleArn,
                    RoleSessionName = "d2d-inspector",
                    ExternalId = externalId,
                    DurationSeconds = 900,
                },
                cancellationToken);
            var credentials = new SessionAWSCredentials(assumed.Credentials.AccessKeyId, assumed.Credentials.SecretAccessKey, assumed.Credentials.SessionToken);
            using var client = new AmazonInspector2Client(credentials, RegionEndpoint.USEast1);
            var response = await client.ListFindingsAsync(
                new ListFindingsRequest
                {
                    MaxResults = 50,
                    FilterCriteria = new FilterCriteria
                    {
                        FindingStatus =
                        [
                            new StringFilter { Comparison = Amazon.Inspector2.StringComparison.EQUALS, Value = "ACTIVE" },
                        ],
                    },
                },
                cancellationToken);
            return response.Findings.Select(Map).Where(item => item is not null).Cast<ImportedFinding>().ToArray();
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException)
        {
            logger.LogWarning(exception, "Inspector findings were not listed. {ExceptionType}", exception.GetType().Name);
            return null;
        }
    }

    private static ImportedFinding? Map(Finding finding)
    {
        var cve = finding.PackageVulnerabilityDetails?.VulnerabilityId ?? "";
        if (cve.Length == 0)
        {
            cve = finding.FindingArn ?? "";
        }

        var title = finding.Title ?? cve;
        var asset = finding.Resources?.FirstOrDefault()?.Id ?? "aws";
        if (cve.Length == 0 || title.Length == 0)
        {
            return null;
        }

        return new ImportedFinding(
            "AWS Inspector",
            Trim(cve, 40),
            Trim(title, 200),
            Severity(finding.Severity),
            Trim(asset, 200),
            finding.FixAvailable == FixAvailable.YES);
    }

    private static string Severity(Severity? severity) => severity?.Value switch
    {
        "CRITICAL" => "Critical",
        "HIGH" => "High",
        "LOW" => "Low",
        "INFORMATIONAL" => "Low",
        _ => "Medium",
    };

    private static string Trim(string value, int max)
    {
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}
