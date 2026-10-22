using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class EmployeeProgram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Decision",
                table: "vendors",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<int>(
                name: "ResidualImpact",
                table: "vendors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResidualLikelihood",
                table: "vendors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Approved",
                table: "risks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "risks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ApprovedOn",
                table: "risks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LibraryCode",
                table: "risks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BackgroundOn",
                table: "people",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundStatus",
                table: "people",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AddColumn<string>(
                name: "DeviceStatus",
                table: "people",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Missing");

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnrolled",
                table: "people",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "people",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "TrainingCompletedOn",
                table: "people",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsRemediation",
                table: "access_review_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RemediationNote",
                table: "access_review_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Providers",
                table: "access_campaigns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InScope = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "training_modules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModuleType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_modules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vendor_questions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prompt = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Answer = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vendor_questions_vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LinkedKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LinkedCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Recurrence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "training_completions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_completions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_training_completions_training_modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "training_modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assets_ExternalId",
                table: "assets",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_training_completions_ModuleId_PersonEmail",
                table: "training_completions",
                columns: new[] { "ModuleId", "PersonEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_training_modules_Code",
                table: "training_modules",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_questions_VendorId",
                table: "vendor_questions",
                column: "VendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "training_completions");

            migrationBuilder.DropTable(
                name: "vendor_questions");

            migrationBuilder.DropTable(
                name: "work_tasks");

            migrationBuilder.DropTable(
                name: "training_modules");

            migrationBuilder.DropColumn(
                name: "Decision",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "ResidualImpact",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "ResidualLikelihood",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "Approved",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "ApprovedOn",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "LibraryCode",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "BackgroundOn",
                table: "people");

            migrationBuilder.DropColumn(
                name: "BackgroundStatus",
                table: "people");

            migrationBuilder.DropColumn(
                name: "DeviceStatus",
                table: "people");

            migrationBuilder.DropColumn(
                name: "MfaEnrolled",
                table: "people");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "people");

            migrationBuilder.DropColumn(
                name: "TrainingCompletedOn",
                table: "people");

            migrationBuilder.DropColumn(
                name: "NeedsRemediation",
                table: "access_review_items");

            migrationBuilder.DropColumn(
                name: "RemediationNote",
                table: "access_review_items");

            migrationBuilder.DropColumn(
                name: "Providers",
                table: "access_campaigns");
        }
    }
}
