using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.ControlPlane
{
    /// <inheritdoc />
    public partial class M11DatabaseHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_jobs_TenantId",
                table: "jobs");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_TenantId_UpdatedAt",
                table: "jobs",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_auditor_grants_TenantId",
                table: "auditor_grants",
                column: "TenantId");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_users_email_lower ON users (lower(\"Email\"));");

            migrationBuilder.Sql(HardeningSql.AppendOnlyFunction);

            migrationBuilder.Sql(HardeningSql.AppendOnly("activity_events"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(HardeningSql.DropAppendOnly("activity_events"));

            migrationBuilder.Sql(HardeningSql.DropAppendOnlyFunction);

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_users_email_lower;");

            migrationBuilder.DropIndex(
                name: "IX_jobs_TenantId_UpdatedAt",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_auditor_grants_TenantId",
                table: "auditor_grants");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_TenantId",
                table: "jobs",
                column: "TenantId");
        }
    }
}
