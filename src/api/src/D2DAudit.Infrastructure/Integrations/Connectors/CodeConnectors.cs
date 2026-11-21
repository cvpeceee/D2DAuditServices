using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2DAudit.Domain;
using static D2DAudit.Infrastructure.Integrations.Connectors.ConnectorCall;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>GitLab REST v4: group projects, default-branch protection and merge request approval rules.</summary>
public sealed class GitLabConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    public override string Key => "gitlab";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var origin = call.BaseUrl("baseUrl", "gitlab.com");
        var auth = Headers(("PRIVATE-TOKEN", call.Require("accessToken")));
        var group = Escape(call.Require("groupId"));
        var resources = new List<NormalizedResource>();
        var page = "1";
        for (var count = 0; count < MaxPages && !string.IsNullOrEmpty(page); count++)
        {
            var (projects, headers) = await call.GetPageAsync<GitLabProject[]>(
                $"{origin}/api/v4/groups/{group}/projects?include_subgroups=true&archived=false&per_page=100&page={Escape(page)}",
                auth,
                cancellationToken);
            foreach (var project in projects)
            {
                var id = project.Id.ToString(CultureInfo.InvariantCulture);
                var branch = project.DefaultBranch ?? "main";
                var isProtected = await call.ExistsAsync($"{origin}/api/v4/projects/{id}/protected_branches/{Escape(branch)}", auth, cancellationToken);
                var approvals = await call.GetAsync<GitLabApprovals>($"{origin}/api/v4/projects/{id}/approvals", auth, cancellationToken);
                resources.Add(call.Resource(
                    "repository",
                    project.Path,
                    project.PathWithNamespace ?? project.Name,
                    ("protected", isProtected),
                    ("defaultBranch", branch),
                    ("requiredApprovals", approvals.ApprovalsBeforeMerge),
                    ("visibility", project.Visibility)));
            }

            page = Header(headers, "X-Next-Page");
        }

        return resources;
    }
}

/// <summary>Bitbucket Cloud 2.0: workspace repositories and branch restrictions.</summary>
public sealed class BitbucketConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://api.bitbucket.org/2.0";

    public override string Key => "bitbucket";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Basic(call.Require("username"), call.Require("appPassword"));
        var workspace = Escape(call.Require("workspace"));
        var resources = new List<NormalizedResource>();
        string? next = $"{Api}/repositories/{workspace}?pagelen=100";
        for (var count = 0; count < MaxPages && next is not null; count++)
        {
            var page = await call.GetAsync<BitbucketPage<BitbucketRepository>>(next, auth, cancellationToken);
            foreach (var repository in page.Values)
            {
                var branch = repository.Mainbranch?.Name ?? "main";
                var restrictions = await call.GetAsync<BitbucketPage<BitbucketRestriction>>(
                    $"{Api}/repositories/{workspace}/{Escape(repository.Slug)}/branch-restrictions?pagelen=100",
                    auth,
                    cancellationToken);
                var applicable = restrictions.Values
                    .Where(rule => rule.Pattern is null || rule.Pattern == branch || rule.Pattern == "*")
                    .ToArray();
                var approvals = applicable
                    .Where(rule => rule.Kind == "require_approvals_to_merge")
                    .Select(rule => rule.Value ?? 0)
                    .DefaultIfEmpty(0)
                    .Max();
                resources.Add(call.Resource(
                    "repository",
                    repository.Slug,
                    repository.FullName ?? repository.Slug,
                    ("protected", applicable.Any(rule => rule.Kind is "push" or "require_approvals_to_merge" or "force")),
                    ("defaultBranch", branch),
                    ("requiredApprovals", approvals),
                    ("visibility", repository.IsPrivate ? "private" : "public")));
            }

            next = Within(page.Next, Api);
        }

        return resources;
    }
}

/// <summary>Azure DevOps Services: Git repositories and branch policies on the default branch.</summary>
public sealed class AzureDevOpsConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://dev.azure.com";
    private const string MinimumReviewers = "fa4e907d-c16b-4a4c-9dfa-4906e5d171dd";

    public override string Key => "azuredevops";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Basic(string.Empty, call.Require("pat"));
        var root = $"{Api}/{Escape(call.Require("organization"))}/{Escape(call.Require("project"))}/_apis";
        var repositories = await call.GetAsync<AdoList<AdoRepository>>($"{root}/git/repositories?api-version=7.1", auth, cancellationToken);
        var policies = await call.GetAsync<AdoList<AdoPolicy>>($"{root}/policy/configurations?api-version=7.1", auth, cancellationToken);
        return repositories.Value
            .Where(repository => repository.IsDisabled != true)
            .Select(repository =>
            {
                var branch = repository.DefaultBranch ?? "refs/heads/main";
                var applicable = policies.Value
                    .Where(policy => policy.IsEnabled && policy.IsBlocking && Applies(policy.Settings, repository.Id, branch))
                    .ToArray();
                var approvals = applicable
                    .Where(policy => string.Equals(policy.Type?.Id, MinimumReviewers, StringComparison.OrdinalIgnoreCase))
                    .Select(policy => policy.Settings.TryGetProperty("minimumApproverCount", out var value) && value.TryGetInt32(out var number) ? number : 0)
                    .DefaultIfEmpty(0)
                    .Max();
                return call.Resource(
                    "repository",
                    repository.Name,
                    repository.Name,
                    ("protected", applicable.Length > 0),
                    ("defaultBranch", branch.Replace("refs/heads/", string.Empty, StringComparison.Ordinal)),
                    ("requiredApprovals", approvals),
                    ("visibility", repository.Project?.Visibility ?? "private"));
            })
            .ToArray();
    }

    private static bool Applies(JsonElement settings, string repositoryId, string branch)
    {
        if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty("scope", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return scopes.EnumerateArray().Any(scope =>
        {
            var repository = Read(scope, "repositoryId");
            var reference = Read(scope, "refName");
            return (repository is null || string.Equals(repository, repositoryId, StringComparison.OrdinalIgnoreCase))
                && (reference is null || reference == branch);
        });
    }
}

/// <summary>CircleCI API v2: organization pipelines as deployment changes (default-branch pipelines count as approved merges).</summary>
public sealed class CircleCiConnector(IHttpClientFactory factory) : LiveConnector(factory)
{
    private const string Api = "https://circleci.com/api/v2";
    private const int Pages = 5;
    private static readonly string[] ReleaseBranches = ["main", "master", "release", "production"];

    public override string Key => "circleci";

    protected override async Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken)
    {
        var auth = Headers(("Circle-Token", call.Require("apiToken")));
        var slug = Escape(call.Require("orgSlug"));
        var resources = new List<NormalizedResource>();
        string? token = null;
        for (var page = 0; page < Pages; page++)
        {
            var url = $"{Api}/pipeline?org-slug={slug}" + (token is null ? string.Empty : "&page-token=" + Escape(token));
            var pipelines = await call.GetAsync<CirclePage>(url, auth, cancellationToken);
            resources.AddRange(pipelines.Items
                .Where(pipeline => pipeline.Vcs?.Branch is not null && ReleaseBranches.Contains(pipeline.Vcs.Branch))
                .Select(pipeline => call.Resource(
                    "change",
                    "PIPE-" + pipeline.Number.ToString(CultureInfo.InvariantCulture),
                    $"Pipeline {pipeline.Number} {pipeline.ProjectSlug} ({pipeline.Vcs!.Branch})",
                    ("approved", pipeline.Trigger?.Type == "webhook"),
                    ("approver", pipeline.Trigger?.Actor?.Login),
                    ("risk", "low"),
                    ("state", pipeline.State))));
            token = pipelines.NextPageToken;
            if (token is null)
            {
                break;
            }
        }

        return resources;
    }
}

internal sealed record GitLabProject(
    long Id,
    string Name,
    string Path,
    [property: JsonPropertyName("path_with_namespace")] string? PathWithNamespace,
    [property: JsonPropertyName("default_branch")] string? DefaultBranch,
    string? Visibility);

internal sealed record GitLabApprovals([property: JsonPropertyName("approvals_before_merge")] int ApprovalsBeforeMerge);

internal sealed record BitbucketPage<T>(T[] Values, string? Next);

internal sealed record BitbucketRepository(
    string Slug,
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("is_private")] bool IsPrivate,
    BitbucketBranch? Mainbranch);

internal sealed record BitbucketBranch(string? Name);

internal sealed record BitbucketRestriction(string Kind, string? Pattern, int? Value);

internal sealed record AdoList<T>(T[] Value);

internal sealed record AdoRepository(string Id, string Name, string? DefaultBranch, bool? IsDisabled, AdoProject? Project);

internal sealed record AdoProject(string? Visibility);

internal sealed record AdoPolicy(bool IsEnabled, bool IsBlocking, AdoPolicyType? Type, JsonElement Settings);

internal sealed record AdoPolicyType(string? Id, string? DisplayName);

internal sealed record CirclePage(CirclePipeline[] Items, [property: JsonPropertyName("next_page_token")] string? NextPageToken);

internal sealed record CirclePipeline(
    string Id,
    int Number,
    string? State,
    [property: JsonPropertyName("project_slug")] string? ProjectSlug,
    CircleTrigger? Trigger,
    CircleVcs? Vcs);

internal sealed record CircleTrigger(string? Type, CircleActor? Actor);

internal sealed record CircleActor(string? Login);

internal sealed record CircleVcs(string? Branch);
