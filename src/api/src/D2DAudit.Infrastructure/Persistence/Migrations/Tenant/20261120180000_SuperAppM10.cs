using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class SuperAppM10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_models",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EuAiActRiskTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TrainingDataProvenance = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Guardrails = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RedTeamStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LinkedControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_models", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "attack_surface_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    TlsDaysRemaining = table.Column<int>(type: "integer", nullable: false),
                    ShadowIt = table.Column<bool>(type: "boolean", nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attack_surface_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "auditor_network",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirmName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Accreditation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Specialties = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Regions = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LeadAuditorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Invited = table.Column<bool>(type: "boolean", nullable: false),
                    InvitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditor_network", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_entities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Jurisdiction = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Headcount = table.Column<int>(type: "integer", nullable: false),
                    ControlsPassing = table.Column<int>(type: "integer", nullable: false),
                    ControlsTotal = table.Column<int>(type: "integer", nullable: false),
                    PrimaryFramework = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_entities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "phishing_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SentCount = table.Column<int>(type: "integer", nullable: false),
                    ClickedCount = table.Column<int>(type: "integer", nullable: false),
                    ReportedCount = table.Column<int>(type: "integer", nullable: false),
                    RemedialTrainingStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RanAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_phishing_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "privacy_assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActivityName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataSubjects = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LawfulBasis = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TransferMechanism = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ReviewDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_privacy_assessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "remediation_pull_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetResource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ArtifactType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UnifiedDiff = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PrNumber = table.Column<int>(type: "integer", nullable: false),
                    PrUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remediation_pull_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_models_Code",
                table: "ai_models",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attack_surface_assets_Hostname",
                table: "attack_surface_assets",
                column: "Hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auditor_network_Code",
                table: "auditor_network",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_entities_Code",
                table: "organization_entities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_phishing_campaigns_Code",
                table: "phishing_campaigns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_privacy_assessments_Code",
                table: "privacy_assessments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_remediation_pull_requests_ControlCode",
                table: "remediation_pull_requests",
                column: "ControlCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_models");

            migrationBuilder.DropTable(
                name: "attack_surface_assets");

            migrationBuilder.DropTable(
                name: "auditor_network");

            migrationBuilder.DropTable(
                name: "organization_entities");

            migrationBuilder.DropTable(
                name: "phishing_campaigns");

            migrationBuilder.DropTable(
                name: "privacy_assessments");

            migrationBuilder.DropTable(
                name: "remediation_pull_requests");
        }
    }
}
