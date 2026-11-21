namespace D2DAudit.Application;

public sealed record DiscoveredVendor(string Name, string Source, string Detail, string SuggestedCriticality, IReadOnlyList<string> DataCategories);

/// <summary>Suggests vendors from connected sources such as SSO app catalogs or expense feeds.</summary>
public interface IVendorDiscoverySource
{
    string Name { get; }

    Task<IReadOnlyList<DiscoveredVendor>> DiscoverAsync(CancellationToken cancellationToken);
}

public sealed record VendorRiskRow(
    Guid Id,
    string Name,
    string Service,
    string Criticality,
    string DataAccess,
    string ReviewState,
    string Decision,
    int InherentScore,
    string Tier,
    string OwnerName,
    string OwnerEmail,
    DateOnly? ContractRenewalOn,
    string RenewalState,
    IReadOnlyList<string> DataCategories,
    bool Subprocessor,
    int ReviewCadenceMonths,
    DateOnly? LastReviewedOn,
    DateOnly NextReviewOn,
    string ReviewStatus,
    string ContactName,
    string ContactEmail,
    string LatestAssessmentStatus,
    int? LatestAssessmentScore);

public sealed record SaveVendorProfileRequest(
    string? OwnerName,
    string? OwnerEmail,
    DateOnly? ContractRenewalOn,
    IReadOnlyList<string>? DataCategories,
    bool Subprocessor,
    int? ReviewCadenceMonths,
    DateOnly? LastReviewedOn,
    string? ContactName,
    string? ContactEmail);

public sealed record VendorDiscoveryDto(
    Guid Id,
    string Name,
    string Source,
    string Detail,
    string SuggestedCriticality,
    IReadOnlyList<string> DataCategories,
    string Status,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? DecidedAt,
    string DecidedBy,
    Guid? VendorId);

public sealed record VendorDiscoveryScanResult(int Found, int Added, IReadOnlyList<string> Sources, IReadOnlyList<VendorDiscoveryDto> Items);

public sealed record QuestionnaireQuestionDto(string Code, string Domain, string Text, int Weight, bool Critical);

public sealed record QuestionnaireTemplateDto(string Code, string Name, string Description, IReadOnlyList<QuestionnaireQuestionDto> Questions);

public sealed record VendorAnswerDto(string QuestionCode, string Answer, string Comment);

public sealed record VendorAssessmentDto(
    Guid Id,
    Guid VendorId,
    string VendorName,
    string TemplateCode,
    string TemplateName,
    string ContactName,
    string ContactEmail,
    string Status,
    DateTimeOffset SentAt,
    string SentBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string ReviewedBy,
    string ReviewNote,
    int? Score,
    string Rating,
    int Answered,
    int Total,
    IReadOnlyList<string> Flagged,
    string DeliveryReceipt,
    IReadOnlyList<VendorAnswerDto> Answers);

public sealed record SendVendorQuestionnaireRequest(string TemplateCode, string ContactName, string ContactEmail, int? ExpiresInDays);

public sealed record SentVendorQuestionnaire(VendorAssessmentDto Assessment, string PortalPath);

public sealed record ReviewVendorAssessmentRequest(string Decision, string? Note);

public sealed record VendorReviewDocumentDto(
    Guid Id,
    Guid VendorId,
    string VendorName,
    Guid? AssessmentId,
    string Kind,
    string FileName,
    long SizeBytes,
    string Sha256,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    string ReportPeriod,
    string Opinion,
    IReadOnlyList<string> Exceptions,
    IReadOnlyList<string> Cuecs,
    string SummaryText,
    string SummaryMode,
    DateTimeOffset? SummarizedAt,
    string SignedOffBy,
    DateTimeOffset? SignedOffAt,
    string SignOffNote);

public sealed record UploadVendorDocumentRequest(string Kind, string FileName, string ContentBase64);

public sealed record SignOffVendorDocumentRequest(string? Note);

public sealed record VendorReminderDto(Guid Id, string Title, string LinkedCode, DateOnly DueOn, string Status);

public sealed record VendorReminderSyncResult(int Created, IReadOnlyList<VendorReminderDto> Reminders);

public sealed record VendorRiskDetailDto(
    VendorRiskRow Vendor,
    IReadOnlyList<VendorAssessmentDto> Assessments,
    IReadOnlyList<VendorReviewDocumentDto> Documents,
    IReadOnlyList<VendorReminderDto> Reminders);

public interface IVendorRisk
{
    Task<IReadOnlyList<VendorRiskRow>> InventoryAsync(CancellationToken cancellationToken);

    Task<Result<VendorRiskDetailDto>> DetailAsync(Guid vendorId, CancellationToken cancellationToken);

    Task<Result<VendorRiskRow>> SaveProfileAsync(Guid vendorId, SaveVendorProfileRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<VendorDiscoveryDto>> DiscoveryAsync(CancellationToken cancellationToken);

    Task<Result<VendorDiscoveryScanResult>> ScanAsync(CancellationToken cancellationToken);

    Task<Result<VendorDiscoveryDto>> DecideAsync(Guid id, bool accept, CancellationToken cancellationToken);

    IReadOnlyList<QuestionnaireTemplateDto> Templates();

    Task<IReadOnlyList<VendorAssessmentDto>> AssessmentsAsync(CancellationToken cancellationToken);

    Task<Result<SentVendorQuestionnaire>> SendAsync(Guid vendorId, SendVendorQuestionnaireRequest request, CancellationToken cancellationToken);

    Task<Result<VendorAssessmentDto>> ReviewAsync(Guid assessmentId, ReviewVendorAssessmentRequest request, CancellationToken cancellationToken);

    Task<Result<VendorAssessmentDto>> RevokeAsync(Guid assessmentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VendorReviewDocumentDto>> DocumentsAsync(CancellationToken cancellationToken);

    Task<Result<VendorReviewDocumentDto>> UploadAsync(Guid vendorId, UploadVendorDocumentRequest request, CancellationToken cancellationToken);

    Task<Result<VendorReviewDocumentDto>> SummarizeAsync(Guid documentId, CancellationToken cancellationToken);

    Task<Result<VendorReviewDocumentDto>> SignOffAsync(Guid documentId, SignOffVendorDocumentRequest request, CancellationToken cancellationToken);

    Task<Result<VendorReminderSyncResult>> SyncRemindersAsync(CancellationToken cancellationToken);
}

public sealed record VendorPortalView(
    string CompanyName,
    string VendorName,
    string ContactName,
    string Status,
    DateTimeOffset ExpiresAt,
    QuestionnaireTemplateDto Template,
    IReadOnlyList<VendorAnswerDto> Answers,
    IReadOnlyList<VendorPortalDocument> Documents);

public sealed record VendorPortalDocument(string Kind, string FileName, DateTimeOffset UploadedAt);

public sealed record VendorPortalAnswersRequest(IReadOnlyList<VendorAnswerDto> Answers, bool Submit);

/// <summary>Unauthenticated, token-scoped access for an invited vendor contact.</summary>
public interface IVendorPortal
{
    Task<Result<VendorPortalView>> GetAsync(string token, CancellationToken cancellationToken);

    Task<Result<VendorPortalView>> SaveAnswersAsync(string token, VendorPortalAnswersRequest request, CancellationToken cancellationToken);

    Task<Result<VendorPortalView>> UploadAsync(string token, UploadVendorDocumentRequest request, CancellationToken cancellationToken);
}
