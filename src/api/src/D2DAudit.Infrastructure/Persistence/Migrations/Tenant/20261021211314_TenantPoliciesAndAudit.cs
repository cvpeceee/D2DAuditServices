using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class TenantPoliciesAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodEnd",
                table: "audit_engagements",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodStart",
                table: "audit_engagements",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "audit_engagements",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequirementCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManagementResponse = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResponseByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_findings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sample_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Population = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sample_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "policy_controls",
                columns: table => new
                {
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_controls", x => new { x.PolicyId, x.ControlCode });
                    table.ForeignKey(
                        name: "FK_policy_controls_policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "policy_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_policy_versions_policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sample_responses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RespondedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sample_responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sample_responses_sample_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "sample_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "policy_acknowledgements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PersonEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemindedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_acknowledgements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_policy_acknowledgements_policy_versions_PolicyVersionId",
                        column: x => x.PolicyVersionId,
                        principalTable: "policy_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_findings_Code",
                table: "findings",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_policies_Code",
                table: "policies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_policy_acknowledgements_PolicyVersionId_PersonEmail",
                table: "policy_acknowledgements",
                columns: new[] { "PolicyVersionId", "PersonEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_policy_versions_PolicyId_Number",
                table: "policy_versions",
                columns: new[] { "PolicyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sample_requests_Code",
                table: "sample_requests",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sample_responses_RequestId",
                table: "sample_responses",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "findings");

            migrationBuilder.DropTable(
                name: "policy_acknowledgements");

            migrationBuilder.DropTable(
                name: "policy_controls");

            migrationBuilder.DropTable(
                name: "sample_responses");

            migrationBuilder.DropTable(
                name: "policy_versions");

            migrationBuilder.DropTable(
                name: "sample_requests");

            migrationBuilder.DropTable(
                name: "policies");

            migrationBuilder.DropColumn(
                name: "PeriodEnd",
                table: "audit_engagements");

            migrationBuilder.DropColumn(
                name: "PeriodStart",
                table: "audit_engagements");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "audit_engagements");
        }
    }
}
