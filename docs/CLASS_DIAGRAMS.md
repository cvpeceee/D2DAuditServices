# Class Diagrams

Mermaid class diagrams of the D2D Audit Services domain model, application boundaries, and infrastructure adapters. Updated alongside every milestone.

## 1. Control-Plane & Multi-Tenancy Model (`D2DAudit.Domain`)

```mermaid
classDiagram
    class Tenant {
        +Guid Id
        +string Slug
        +string Name
        +string Region
        +TenantStatus Status
        +string? DatabaseName
        +int SchemaVersion
        +int RetentionYears
        +bool IsExample
        +SetRetentionYears(int years) RuleResult
    }
    class UserAccount {
        +Guid Id
        +string Subject
        +string Email
        +string DisplayName
        +string? PasswordHash
    }
    class Membership {
        +Guid Id
        +Guid UserId
        +Guid TenantId
        +TenantRole Role
        +bool Active
    }
    class AuditorGrant {
        +Guid Id
        +Guid UserId
        +Guid TenantId
        +string AuditName
        +string FirmName
        +DateOnly PeriodStart
        +DateOnly PeriodEnd
        +DateOnly AccessExpiresOn
        +IsActiveOn(DateOnly today) bool
    }
    class AuditorGrantFramework {
        +Guid GrantId
        +string FrameworkKey
    }
    class TenantDomain {
        +Guid Id
        +Guid TenantId
        +string Domain
        +string? WorkOsOrganizationId
    }
    class ProviderClaim {
        +Guid Id
        +string Provider
        +string ExternalAccountId
        +Guid TenantId
        +DateTimeOffset ClaimedAt
    }
    class JobRecord {
        +Guid Id
        +Guid? TenantId
        +string Kind
        +JobStatus Status
        +int Step
        +int TotalSteps
        +string CurrentStep
        +int? Percent
        +string? Error
    }
    class ActivityEvent {
        +Guid Id
        +DateTimeOffset At
        +Guid? ActorUserId
        +Guid? TenantId
        +string Action
        +string Result
        +string? Detail
        +string? IpAddress
    }

    Tenant "1" --> "*" Membership
    UserAccount "1" --> "*" Membership
    Tenant "1" --> "*" TenantDomain
    Tenant "1" --> "*" ProviderClaim
    AuditorGrant "1" --> "*" AuditorGrantFramework
```

## 2. Core Compliance, Evidence Chain & Audit Model (`D2DAudit.Domain`)

```mermaid
classDiagram
    class FrameworkRecord {
        +Guid Id
        +string Key
        +string Name
        +string Version
        +string Description
    }
    class Requirement {
        +Guid Id
        +Guid FrameworkId
        +Guid? ParentId
        +string Code
        +string Title
        +string Summary
        +int SortOrder
    }
    class Control {
        +Guid Id
        +string Code
        +string Title
        +string Description
        +string? OwnerName
        +string? OwnerEmail
        +ControlStatus Status
        +string Frequency
        +bool Custom
    }
    class ControlRequirementMap {
        +Guid ControlId
        +Guid RequirementId
        +MappingCoverage Coverage
    }
    class ControlTest {
        +Guid Id
        +Guid ControlId
        +string Name
        +TestKind Kind
        +TestOutcome Outcome
        +string? TestKey
        +string? Remediation
    }
    class EvidenceItem {
        +Guid Id
        +Guid ControlId
        +string Title
        +string Source
        +DateTimeOffset? CollectedAt
        +string? Sha256
        +EvidenceIntegrity Integrity
        +string? ObjectKey
        +string? PreviousHead
        +string? ChainHead
        +DateTimeOffset? LockedUntil
    }
    class ChainEntry {
        +Guid Id
        +long Sequence
        +Guid EvidenceId
        +string ObjectKey
        +string ContentHash
        +string? PreviousHead
        +string Head
        +string Canonical
    }
    class AuditEngagement {
        +Guid Id
        +string Name
        +string FirmName
        +string FrameworkKey
        +DateOnly PeriodStart
        +DateOnly PeriodEnd
        +DateOnly FieldworkOn
    }

    FrameworkRecord "1" --> "*" Requirement
    Requirement "0..1" --> "*" Requirement : Parent
    Control "1" --> "*" ControlRequirementMap
    Requirement "1" --> "*" ControlRequirementMap
    Control "1" --> "*" ControlTest
    Control "1" --> "*" EvidenceItem
    EvidenceItem "1" --> "0..1" ChainEntry
```

## 3. Policies, Governance, Operations & Security Rules (`D2DAudit.Domain`)

```mermaid
classDiagram
    class PolicyDocument {
        +Guid Id
        +string Code
        +string Title
        +string OwnerName
    }
    class PolicyVersion {
        +Guid Id
        +int Number
        +string Body
        +PolicyState State
        +DateTimeOffset? ApprovedAt
        +string? ApprovedByName
    }
    class PolicyAcknowledgement {
        +Guid Id
        +string PersonName
        +string PersonEmail
        +DateOnly DueOn
        +DateTimeOffset? AcknowledgedAt
        +DateTimeOffset? RemindedAt
    }
    class RiskItem {
        +Guid Id
        +string Code
        +string Title
        +int Likelihood
        +int Impact
        +RiskTreatment Treatment
        +bool Approved
    }
    class VendorRecord {
        +Guid Id
        +string Name
        +VendorCriticality Criticality
        +VendorReviewState ReviewState
        +string Decision
    }
    class ComputerRecord {
        +Guid Id
        +string Serial
        +string OperatingSystem
        +bool Encrypted
        +bool ScreenLock
        +bool Antivirus
        +bool PasswordManager
        +bool AutoUpdate
        +string Source
    }
    class VulnerabilityRecord {
        +Guid Id
        +string Cve
        +string Severity
        +string AssetName
        +string Source
        +DateOnly SlaDue
        +bool FixAvailable
        +bool Ignored
    }
    class TrustProfile {
        +Guid Id
        +bool Published
        +string Headline
        +string Summary
        +string ContactEmail
    }
    class SecurityRules {
        <<static>>
        +SafeUrl.IsHttpOrHttps(string?) bool
        +SafeUrl.IsPublicHttpOrHttps(string?) bool
        +SafeUrl.IsPublicIpAddress(IPAddress) bool
        +SafeUrl.IsSameOrigin(string?, string?) bool
        +CsvSafety.EscapeCell(string?) string
        +TaskRecurrence.NextDue(DateOnly, string?) DateOnly?
        +RedirectPolicy.IsAllowed(string?, IReadOnlyCollection) bool
    }

    PolicyDocument "1" --> "*" PolicyVersion
    PolicyVersion "1" --> "*" PolicyAcknowledgement
```

## 4. Application Boundaries & Infrastructure Adapters

```mermaid
classDiagram
    class IIdentityProvider {
        <<interface>>
        +PasswordLoginAsync()
        +CompleteMfaAsync()
        +GetAuthorizationUrlAsync()
        +ValidateAccessTokenAsync()
    }
    class ICloudProvider {
        <<interface>>
        +string ProviderKey
        +ConnectAsync()
        +CollectAsync()
    }
    class IWorkspaceDirectory {
        <<interface>>
        +string ProviderKey
        +ConnectAsync()
        +CollectAsync()
    }
    class ISourceControlProvider {
        <<interface>>
        +string ProviderKey
        +ConnectAsync()
        +CollectAsync()
    }
    class IStorageProvider {
        <<interface>>
        +PutAsync()
        +GetAsync()
    }
    class IIssueTracker {
        <<interface>>
        +CreateIssueAsync()
    }
    class INotifier {
        <<interface>>
        +SendAsync()
    }
    class IAutomationDispatcher {
        <<interface>>
        +DispatchAsync()
        +DispatchForTenantAsync()
    }
    class IApiKeyAuthenticator {
        <<interface>>
        +ValidateAsync(token, cancellationToken) ApiKeyIdentity?
    }
    class IWebhookDispatcher {
        <<interface>>
        +DispatchAsync(databaseName, eventName, summary, detail, cancellationToken)
        +SendSingleAsync(webhook, eventName, summary, detail, cancellationToken) WebhookTestResultDto
    }

    IIdentityProvider <|.. WorkOsIdentityProvider
    IIdentityProvider <|.. LocalIdentityProvider
    ICloudProvider <|.. AwsCloudProvider
    ICloudProvider <|.. AzureCloudProvider
    ICloudProvider <|.. GcpCloudProvider
    IWorkspaceDirectory <|.. GoogleWorkspaceDirectory
    IWorkspaceDirectory <|.. Microsoft365Directory
    ISourceControlProvider <|.. GitHubSourceControl
    IStorageProvider <|.. S3StorageProvider
    IStorageProvider <|.. LocalStorageProvider
    IIssueTracker <|.. ConfigurableIssueTracker
    INotifier <|.. ConfigurableNotifier
    IAutomationDispatcher <|.. AutomationDispatcher
    IApiKeyAuthenticator <|.. ApiKeyAuthenticator
    IWebhookDispatcher <|.. WebhookDispatcher
```

## 5. Cloud Security Intelligence & Blast-Radius Graph (`D2DAudit.Domain` — M8)

```mermaid
classDiagram
    class ExposureFinding {
        +Guid Id
        +string Provider
        +string ResourceName
        +string ResourceKind
        +string Region
        +string Cidr
        +int Port
        +string Service
        +string Severity
        +int RiskScore
        +string ControlCode
        +string RemediationTerraform
        +string RemediationCli
        +string Status
    }
    class SecurityScanFinding {
        +Guid Id
        +string Kind
        +string Severity
        +string AssetName
        +string Location
        +int LineNumber
        +string RuleId
        +string Snippet
        +string ControlCode
        +string Status
    }
    class DataStoreAsset {
        +Guid Id
        +string StoreName
        +string Provider
        +string Engine
        +string Region
        +string Classification
        +string SensitiveDataTypes
        +long RecordCountEstimate
        +bool Encrypted
        +bool PublicAccess
        +bool RetentionCompliant
        +int RiskScore
    }
    class CostRecommendation {
        +Guid Id
        +string Provider
        +string ResourceName
        +string Category
        +decimal MonthlySavingsUsd
        +decimal AnnualSavingsUsd
        +string LinkedControlCode
        +string ComplianceImpact
        +string Status
    }
    class AttackPath {
        +string Id
        +string Title
        +int BlastRadiusScore
        +string Severity
        +IReadOnlyList~AttackHop~ Hops
        +IReadOnlyList~string~ ControlCodes
        +string ChokepointRemediation
    }
    class ICloudSecurity {
        <<interface>>
        +ExposureBoardAsync()
        +ScanExposureAsync()
        +ScansBoardAsync()
        +IngestSarifAsync()
        +DspmBoardAsync()
        +FinOpsBoardAsync()
        +AttackPathsAsync()
    }

    ICloudSecurity <|.. CloudSecurityPortal
    ExposureFinding --> AttackPath : Entry Hop
    SecurityScanFinding --> AttackPath : Credential Hop
    DataStoreAsset --> AttackPath : Crown Jewel Hop
```

## 6. Enterprise Compliance, Trust Center v2, Grounded AI & Developer API (`D2DAudit.Domain` — M9)

```mermaid
classDiagram
    class TrustDocument {
        +Guid Id
        +string Title
        +string Access
        +string FileName
        +string ObjectKey
        +string Sha256
        +string ContentType
    }
    class TrustAccessRequest {
        +Guid Id
        +Guid? DocumentId
        +string RequesterEmail
        +string Status
        +bool NdaAccepted
        +DateTimeOffset? NdaAcceptedAt
        +string AccessToken
    }
    class TrustReceiptDocument {
        +string Slug
        +string TenantName
        +string Region
        +int RetentionYears
        +int ChainEntryCount
        +long LatestChainSequence
        +string ChainHeadHash
        +string ChainStatus
        +string CanonicalPayload
        +string ReceiptSha256
    }
    class ApiKeyRecord {
        +Guid Id
        +string Name
        +string Prefix
        +string KeyHash
        +string Scopes
        +string CreatedByEmail
        +DateTimeOffset CreatedAt
        +DateTimeOffset? LastUsedAt
        +DateTimeOffset? RevokedAt
    }
    class WebhookSubscription {
        +Guid Id
        +string Name
        +string Url
        +string Events
        +string SigningSecretHash
        +string SigningSecretEncrypted
        +bool Enabled
        +DateTimeOffset CreatedAt
    }
    class ApiKeyTokens {
        <<static>>
        +Create(Guid tenantId) string
        +IsApiKey(string? token) bool
        +TryParseTenantId(string? token, out Guid tenantId) bool
        +CanWrite(string? scopes) bool
    }
    class WebhookSigning {
        <<static>>
        +Sign(string secret, long unixTimestamp, string payload) string
        +Verify(string secret, long unixTimestamp, string payload, string headerValue) bool
        +EventNameForTrigger(string trigger) string
        +MatchesEvent(string? subscribedEvents, string eventName) bool
    }
    class WebhookSecretProtector {
        +Protect(string plaintext) string?
        +Unprotect(string? stored) string?
    }
    class IAssistantProvider {
        <<interface>>
        +AnswerAsync(question, facts, scopeLabel) AssistantReply
    }
    class IEnterprisePortal {
        <<interface>>
        +SimulateFrameworkAsync()
        +AdoptFrameworkGapsAsync()
        +CreateCustomFrameworkAsync()
        +TrustReceiptAsync()
        +DownloadTrustDocumentAsync()
        +AskTrustPublicAsync()
        +AskWorkspaceAsync()
        +ListApiKeysAsync()
        +CreateApiKeyAsync()
        +RevokeApiKeyAsync()
        +ListWebhooksAsync()
        +CreateWebhookAsync()
        +TestWebhookAsync()
        +DeleteWebhookAsync()
    }

    IAssistantProvider <|.. ConfigurableAssistantProvider
    IEnterprisePortal <|.. EnterprisePortal
    EnterprisePortal --> IAssistantProvider
    EnterprisePortal --> WebhookSecretProtector
    EnterprisePortal --> IWebhookDispatcher
    EnterprisePortal --> TrustReceiptDocument : builds via TrustReceiptBuilder
    EnterprisePortal --> ApiKeyRecord
    EnterprisePortal --> WebhookSubscription
```
