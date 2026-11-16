using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class Changes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    Repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ApproverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeployRef = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    DeployedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_changes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_changes_Code",
                table: "changes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_changes_Source_ExternalKey",
                table: "changes",
                columns: new[] { "Source", "ExternalKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "changes");
        }
    }
}
