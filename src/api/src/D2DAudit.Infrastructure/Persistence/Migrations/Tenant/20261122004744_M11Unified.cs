using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class M11Unified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AcceptanceExpiresOn",
                table: "risks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedBy",
                table: "risks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "AcceptedOn",
                table: "risks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualRateOfOccurrence",
                table: "risks",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewCadence",
                table: "risks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Quarterly");

            migrationBuilder.AddColumn<decimal>(
                name: "SingleLossExpectancy",
                table: "risks",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "academy_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Audience = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UpdatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_academy_assignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "academy_progress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PersonEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SectionsCompleted = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastScore = table.Column<int>(type: "integer", nullable: false),
                    BestScore = table.Column<int>(type: "integer", nullable: false),
                    PassedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CertificateNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_academy_progress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "control_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_comments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "control_profiles",
                columns: table => new
                {
                    ControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ImplementationGuidance = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EvidenceRequirement = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequiredEvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    RequiresPassingTest = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_profiles", x => x.ControlCode);
                });

            migrationBuilder.CreateTable(
                name: "custom_test_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ResourceKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FilterJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    AssertionJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ControlCodes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Remediation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_test_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OperatingSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AgentKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AgentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PersonEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSignedTimestamp = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_enrollment_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Hint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_enrollment_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_posture_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Serial = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AgentKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AgentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Passing = table.Column<int>(type: "integer", nullable: false),
                    Failing = table.Column<int>(type: "integer", nullable: false),
                    ChecksJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_posture_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employee_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Departments = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RoleKeywords = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PolicyCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TrainingCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evidence_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Cadence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "integration_sync_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResourceCount = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_sync_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_checklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PersonEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TriggeredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AccessDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessRevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lifecycle_checklists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OffboardingAccessSlaHours = table.Column<int>(type: "integer", nullable: false),
                    OffboardingCompleteSlaHours = table.Column<int>(type: "integer", nullable: false),
                    OnboardingDays = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lifecycle_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_template_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SlaHours = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    GroupCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RevokesAccess = table.Column<bool>(type: "boolean", nullable: false),
                    PerAccount = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lifecycle_template_steps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InApp = table.Column<bool>(type: "boolean", nullable: false),
                    EmailDigest = table.Column<bool>(type: "boolean", nullable: false),
                    Slack = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "policy_attestation_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RenewOn = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_attestation_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "posture_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    ControlsPassing = table.Column<int>(type: "integer", nullable: false),
                    ControlsTotal = table.Column<int>(type: "integer", nullable: false),
                    FailingTests = table.Column<int>(type: "integer", nullable: false),
                    VulnCritical = table.Column<int>(type: "integer", nullable: false),
                    VulnHigh = table.Column<int>(type: "integer", nullable: false),
                    VulnMedium = table.Column<int>(type: "integer", nullable: false),
                    VulnLow = table.Column<int>(type: "integer", nullable: false),
                    OpenTasks = table.Column<int>(type: "integer", nullable: false),
                    OverdueItems = table.Column<int>(type: "integer", nullable: false),
                    FrameworksJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    FailingTestIdsJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_posture_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "report_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Format = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Recipients = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    NextRunOn = table.Column<DateOnly>(type: "date", nullable: false),
                    LastSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDeliveryId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_schedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "test_suppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Until = table.Column<DateOnly>(type: "date", nullable: true),
                    ByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_suppressions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Href = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TemplateCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeliveryReceipt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    Rating = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vendor_assessments_vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendor_discovery_suggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SuggestedCriticality = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DataCategories = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_discovery_suggestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ContractRenewalOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DataCategories = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Subprocessor = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewCadenceMonths = table.Column<int>(type: "integer", nullable: false),
                    LastReviewedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vendor_profiles_vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendor_review_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReportPeriod = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Opinion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Exceptions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Cuecs = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SummaryText = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    SummaryMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SummarizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SignedOffBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SignedOffAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SignOffNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_review_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vendor_review_documents_vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "control_comment_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReplacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_comment_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_control_comment_revisions_control_comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "control_comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidence_document_links",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_document_links", x => new { x.DocumentId, x.ControlCode });
                    table.ForeignKey(
                        name: "FK_evidence_document_links_evidence_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "evidence_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidence_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssigneeEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evidence_requests_evidence_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "evidence_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RevokesAccess = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Done = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Result = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lifecycle_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lifecycle_steps_lifecycle_checklists_ChecklistId",
                        column: x => x.ChecklistId,
                        principalTable: "lifecycle_checklists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "policy_attestations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PersonEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AttestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_attestations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_policy_attestations_policy_attestation_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "policy_attestation_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendor_assessment_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Answer = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_assessment_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vendor_assessment_answers_vendor_assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "vendor_assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_academy_assignments_ModuleKey_Audience",
                table: "academy_assignments",
                columns: new[] { "ModuleKey", "Audience" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_academy_progress_ModuleKey_PersonEmail",
                table: "academy_progress",
                columns: new[] { "ModuleKey", "PersonEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_control_comment_revisions_CommentId",
                table: "control_comment_revisions",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_control_comments_ControlCode",
                table: "control_comments",
                column: "ControlCode");

            migrationBuilder.CreateIndex(
                name: "IX_custom_test_definitions_Key",
                table: "custom_test_definitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_credentials_PersonEmail",
                table: "device_credentials",
                column: "PersonEmail");

            migrationBuilder.CreateIndex(
                name: "IX_device_credentials_Serial",
                table: "device_credentials",
                column: "Serial");

            migrationBuilder.CreateIndex(
                name: "IX_device_enrollment_tokens_PersonEmail",
                table: "device_enrollment_tokens",
                column: "PersonEmail");

            migrationBuilder.CreateIndex(
                name: "IX_device_enrollment_tokens_TokenHash",
                table: "device_enrollment_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_posture_snapshots_DeviceId",
                table: "device_posture_snapshots",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_device_posture_snapshots_Serial_At",
                table: "device_posture_snapshots",
                columns: new[] { "Serial", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_groups_Code",
                table: "employee_groups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_document_links_ControlCode",
                table: "evidence_document_links",
                column: "ControlCode");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_documents_Code",
                table: "evidence_documents",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_requests_AssigneeEmail",
                table: "evidence_requests",
                column: "AssigneeEmail");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_requests_DocumentId",
                table: "evidence_requests",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_sync_runs_Provider_StartedAt",
                table: "integration_sync_runs",
                columns: new[] { "Provider", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_checklists_PersonId_Kind",
                table: "lifecycle_checklists",
                columns: new[] { "PersonId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_steps_ChecklistId",
                table: "lifecycle_steps",
                column: "ChecklistId");

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_template_steps_Kind_Code_GroupCode",
                table: "lifecycle_template_steps",
                columns: new[] { "Kind", "Code", "GroupCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_preferences_Email_Kind",
                table: "notification_preferences",
                columns: new[] { "Email", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_receipts_NotificationId_Email",
                table: "notification_receipts",
                columns: new[] { "NotificationId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_policy_attestation_campaigns_PolicyCode",
                table: "policy_attestation_campaigns",
                column: "PolicyCode");

            migrationBuilder.CreateIndex(
                name: "IX_policy_attestations_CampaignId_PersonEmail",
                table: "policy_attestations",
                columns: new[] { "CampaignId", "PersonEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_posture_snapshots_Day",
                table: "posture_snapshots",
                column: "Day",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_schedules_ReportKey",
                table: "report_schedules",
                column: "ReportKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_test_suppressions_TestKey",
                table: "test_suppressions",
                column: "TestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_DedupeKey",
                table: "user_notifications",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_RecipientEmail_At",
                table: "user_notifications",
                columns: new[] { "RecipientEmail", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_vendor_assessment_answers_AssessmentId_QuestionCode",
                table: "vendor_assessment_answers",
                columns: new[] { "AssessmentId", "QuestionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_assessments_TokenHash",
                table: "vendor_assessments",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_assessments_VendorId",
                table: "vendor_assessments",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_vendor_discovery_suggestions_Name",
                table: "vendor_discovery_suggestions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_profiles_VendorId",
                table: "vendor_profiles",
                column: "VendorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_review_documents_VendorId",
                table: "vendor_review_documents",
                column: "VendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "academy_assignments");

            migrationBuilder.DropTable(
                name: "academy_progress");

            migrationBuilder.DropTable(
                name: "control_comment_revisions");

            migrationBuilder.DropTable(
                name: "control_profiles");

            migrationBuilder.DropTable(
                name: "custom_test_definitions");

            migrationBuilder.DropTable(
                name: "device_credentials");

            migrationBuilder.DropTable(
                name: "device_enrollment_tokens");

            migrationBuilder.DropTable(
                name: "device_posture_snapshots");

            migrationBuilder.DropTable(
                name: "employee_groups");

            migrationBuilder.DropTable(
                name: "evidence_document_links");

            migrationBuilder.DropTable(
                name: "evidence_requests");

            migrationBuilder.DropTable(
                name: "integration_sync_runs");

            migrationBuilder.DropTable(
                name: "lifecycle_settings");

            migrationBuilder.DropTable(
                name: "lifecycle_steps");

            migrationBuilder.DropTable(
                name: "lifecycle_template_steps");

            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "notification_receipts");

            migrationBuilder.DropTable(
                name: "policy_attestations");

            migrationBuilder.DropTable(
                name: "posture_snapshots");

            migrationBuilder.DropTable(
                name: "report_schedules");

            migrationBuilder.DropTable(
                name: "test_suppressions");

            migrationBuilder.DropTable(
                name: "user_notifications");

            migrationBuilder.DropTable(
                name: "vendor_assessment_answers");

            migrationBuilder.DropTable(
                name: "vendor_discovery_suggestions");

            migrationBuilder.DropTable(
                name: "vendor_profiles");

            migrationBuilder.DropTable(
                name: "vendor_review_documents");

            migrationBuilder.DropTable(
                name: "control_comments");

            migrationBuilder.DropTable(
                name: "evidence_documents");

            migrationBuilder.DropTable(
                name: "lifecycle_checklists");

            migrationBuilder.DropTable(
                name: "policy_attestation_campaigns");

            migrationBuilder.DropTable(
                name: "vendor_assessments");

            migrationBuilder.DropColumn(
                name: "AcceptanceExpiresOn",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "AcceptedBy",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "AcceptedOn",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "AnnualRateOfOccurrence",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "ReviewCadence",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "SingleLossExpectancy",
                table: "risks");
        }
    }
}
