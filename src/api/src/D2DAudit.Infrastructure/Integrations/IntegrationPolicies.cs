using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace D2DAudit.Infrastructure.Integrations;

public static partial class IntegrationPolicies
{
    public static bool IsAwsRoleArn(string value) => AwsRoleArn().IsMatch(value);

    public static string AwsTrustPolicy(string principalArn, string externalId) =>
        $$"""
        {
          "Version": "2012-10-17",
          "Statement": [
            {
              "Effect": "Allow",
              "Principal": { "AWS": "{{principalArn}}" },
              "Action": "sts:AssumeRole",
              "Condition": { "StringEquals": { "sts:ExternalId": "{{externalId}}" } }
            }
          ]
        }
        """;

    public const string AwsPermissions =
        """
        {
          "Version": "2012-10-17",
          "Statement": [
            {
              "Effect": "Allow",
              "Action": [
                "iam:GetAccountSummary",
                "s3:ListAllMyBuckets",
                "s3:GetBucketPublicAccessBlock",
                "s3:GetEncryptionConfiguration",
                "s3:GetBucketLocation",
                "cloudtrail:DescribeTrails",
                "cloudtrail:GetTrailStatus",
                "sts:GetCallerIdentity"
              ],
              "Resource": "*"
            }
          ]
        }
        """;

    public static string CollectorArn(IConfiguration configuration)
    {
        var configured = configuration["AWS_COLLECTOR_ROLE_ARN"];
        return string.IsNullOrWhiteSpace(configured)
            ? "arn:aws:iam::000000000000:role/d2d-collector"
            : configured.Trim();
    }

    public static string? AuthorizationUrl(IConfiguration configuration, string provider, string redirectUri, string state)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return null;
        }

        return provider switch
        {
            "google" => GoogleUrl(configuration, redirectUri, state),
            "microsoft" => MicrosoftUrl(configuration, redirectUri, state),
            "github" => GitHubUrl(configuration, state),
            _ => null,
        };
    }

    public static IReadOnlyList<string> ExampleProviders(string fixture) =>
        fixture == "northwind"
            ? ["aws", "google", "microsoft", "github"]
            : ["aws", "azure", "gcp", "google", "microsoft", "github"];

    public static string DisplayName(string provider) => provider switch
    {
        "aws" => "Amazon Web Services",
        "azure" => "Microsoft Azure",
        "gcp" => "Google Cloud",
        "google" => "Google Workspace",
        "microsoft" => "Microsoft 365",
        "github" => "GitHub",
        _ => provider,
    };

    private static string? GoogleUrl(IConfiguration configuration, string redirectUri, string state)
    {
        var endpoint = configuration["Integrations:GoogleAuthorizationEndpoint"];
        var clientId = configuration["GOOGLE_CLIENT_ID"];
        var scope = configuration["Integrations:GoogleDirectoryScope"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        return WithQuery(endpoint, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["scope"] = scope,
            ["state"] = state,
        });
    }

    private static string? MicrosoftUrl(IConfiguration configuration, string redirectUri, string state)
    {
        var endpoint = configuration["Integrations:MicrosoftAuthorizationEndpoint"];
        var clientId = configuration["MS_CLIENT_ID"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        return WithQuery(endpoint, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
        });
    }

    private static string? GitHubUrl(IConfiguration configuration, string state)
    {
        var endpoint = configuration["Integrations:GitHubInstallEndpoint"];
        var slug = configuration["GITHUB_APP_SLUG"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return WithQuery(endpoint.TrimEnd('/') + "/" + slug + "/installations/new", new Dictionary<string, string>
        {
            ["state"] = state,
        });
    }

    private static string WithQuery(string endpoint, IReadOnlyDictionary<string, string> query)
    {
        var builder = new UriBuilder(endpoint);
        builder.Query = string.Join(
            "&",
            query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return builder.Uri.ToString();
    }

    [GeneratedRegex("^arn:aws:iam::\\d{12}:role/[\\w+=,.@/-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AwsRoleArn();
}
