using D2DAudit.Domain;

namespace D2DAudit.Application;

public sealed record CreateCustomFrameworkRequest(
    string Key,
    string Name,
    string Version,
    string Description,
    string RequirementsCsv);

public sealed record CustomFrameworkCreatedDto(string Key, string Name, int RequirementCount);

public sealed record AdoptGapsRequest(IReadOnlyList<string>? RequirementCodes);

public sealed record TrustDownloadFile(byte[] Body, string ContentType, string FileName, string Sha256);

public sealed record AssistantAskRequest(string Question);

public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    string Scopes,
    string CreatedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt = null);

public sealed record CreateApiKeyRequest(string Name, string Scopes);

public sealed record CreatedApiKeyDto(ApiKeyDto Key, string PlaintextKey);

public sealed record WebhookDto(
    Guid Id,
    string Name,
    string Url,
    string Events,
    bool Enabled,
    DateTimeOffset CreatedAt);

public sealed record CreateWebhookRequest(string Name, string Url, string Events);

public sealed record CreatedWebhookDto(WebhookDto Webhook, string SigningSecret);

public sealed record WebhookTestResultDto(
    bool Delivered,
    int StatusCode,
    string EventName,
    string SignaturePreview,
    string Detail);

public sealed record ApiKeyIdentity(
    Guid KeyId,
    string KeyName,
    string CreatedByEmail,
    string Scopes,
    Guid TenantId,
    string TenantName,
    string Region,
    bool IsExample,
    string DatabaseName);

public interface IApiKeyAuthenticator
{
    Task<ApiKeyIdentity?> ValidateAsync(string token, CancellationToken cancellationToken);
}

public interface IWebhookDispatcher
{
    Task<IReadOnlyList<string>> DispatchAsync(
        string databaseName,
        string eventName,
        string summary,
        string detail,
        CancellationToken cancellationToken);

    Task<WebhookTestResultDto> SendSingleAsync(
        WebhookSubscription webhook,
        string eventName,
        string summary,
        string detail,
        CancellationToken cancellationToken);
}

public interface IAssistantProvider
{
    Task<AssistantReply> AnswerAsync(
        string question,
        IReadOnlyList<AssistantFact> facts,
        string scopeLabel,
        CancellationToken cancellationToken);
}

public interface IEnterprisePortal
{
    Task<Result<FrameworkSimulationResult>> SimulateFrameworkAsync(string key, CancellationToken cancellationToken);

    Task<Result<int>> AdoptFrameworkGapsAsync(string key, AdoptGapsRequest request, CancellationToken cancellationToken);

    Task<Result<CustomFrameworkCreatedDto>> CreateCustomFrameworkAsync(CreateCustomFrameworkRequest request, CancellationToken cancellationToken);

    Task<TrustReceiptDocument?> TrustReceiptAsync(string slug, CancellationToken cancellationToken);

    Task<Result<TrustDownloadFile>> DownloadTrustDocumentAsync(string slug, Guid documentId, string? accessToken, CancellationToken cancellationToken);

    Task<Result<AssistantReply>> AskTrustPublicAsync(string slug, AssistantAskRequest request, CancellationToken cancellationToken);

    Task<Result<AssistantReply>> AskWorkspaceAsync(AssistantAskRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiKeyDto>> ListApiKeysAsync(CancellationToken cancellationToken);

    Task<Result<CreatedApiKeyDto>> CreateApiKeyAsync(CreateApiKeyRequest request, CancellationToken cancellationToken);

    Task<Result<bool>> RevokeApiKeyAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookDto>> ListWebhooksAsync(CancellationToken cancellationToken);

    Task<Result<CreatedWebhookDto>> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken);

    Task<Result<WebhookTestResultDto>> TestWebhookAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<bool>> DeleteWebhookAsync(Guid id, CancellationToken cancellationToken);
}
