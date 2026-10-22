using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class TenantIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_control_tests_ControlId",
                table: "control_tests");

            migrationBuilder.AddColumn<string>(
                name: "ChainHead",
                table: "evidence_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "evidence_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObjectKey",
                table: "evidence_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousHead",
                table: "evidence_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remediation",
                table: "control_tests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Requirements",
                table: "control_tests",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestKey",
                table: "control_tests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "evidence_chain",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousHead = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Head = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Canonical = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_chain", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "integration_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Example = table.Column<bool>(type: "boolean", nullable: false),
                    Fixture = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RoleArn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    InstallationId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AccountLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    Secret = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SetupHint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastCollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "resource_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AttributesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_snapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_control_tests_ControlId_TestKey",
                table: "control_tests",
                columns: new[] { "ControlId", "TestKey" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_chain_Sequence",
                table: "evidence_chain",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_connections_Provider",
                table: "integration_connections",
                column: "Provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resource_snapshots_Provider_Kind_ExternalId",
                table: "resource_snapshots",
                columns: new[] { "Provider", "Kind", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence_chain");

            migrationBuilder.DropTable(
                name: "integration_connections");

            migrationBuilder.DropTable(
                name: "resource_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_control_tests_ControlId_TestKey",
                table: "control_tests");

            migrationBuilder.DropColumn(
                name: "ChainHead",
                table: "evidence_items");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "evidence_items");

            migrationBuilder.DropColumn(
                name: "ObjectKey",
                table: "evidence_items");

            migrationBuilder.DropColumn(
                name: "PreviousHead",
                table: "evidence_items");

            migrationBuilder.DropColumn(
                name: "Remediation",
                table: "control_tests");

            migrationBuilder.DropColumn(
                name: "Requirements",
                table: "control_tests");

            migrationBuilder.DropColumn(
                name: "TestKey",
                table: "control_tests");

            migrationBuilder.CreateIndex(
                name: "IX_control_tests_ControlId",
                table: "control_tests",
                column: "ControlId");
        }
    }
}
