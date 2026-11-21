using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class M11DatabaseHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_evidence_items_controls_ControlId",
                table: "evidence_items");

            migrationBuilder.Sql("ALTER TABLE resource_snapshots ALTER COLUMN \"AttributesJson\" TYPE jsonb USING \"AttributesJson\"::jsonb;");

            migrationBuilder.CreateIndex(
                name: "IX_vulnerabilities_Source",
                table: "vulnerabilities",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_sla_events_At",
                table: "sla_events",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_resource_snapshots_Kind",
                table: "resource_snapshots",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_At",
                table: "notifications",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_items_CollectedAt",
                table: "evidence_items",
                column: "CollectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_changes_OpenedAt",
                table: "changes",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_automation_deliveries_At",
                table: "automation_deliveries",
                column: "At");

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_items_controls_ControlId",
                table: "evidence_items",
                column: "ControlId",
                principalTable: "controls",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_people_email_lower ON people (lower(\"Email\"));");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_policy_acknowledgements_email_lower ON policy_acknowledgements (lower(\"PersonEmail\"));");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_training_completions_module_email_lower ON training_completions (\"ModuleId\", lower(\"PersonEmail\"));");

            migrationBuilder.Sql(HardeningSql.AppendOnlyFunction);

            migrationBuilder.Sql(HardeningSql.AppendOnly("evidence_chain"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(HardeningSql.DropAppendOnly("evidence_chain"));

            migrationBuilder.Sql(HardeningSql.DropAppendOnlyFunction);

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_people_email_lower; DROP INDEX IF EXISTS ix_policy_acknowledgements_email_lower; DROP INDEX IF EXISTS ix_training_completions_module_email_lower;");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_items_controls_ControlId",
                table: "evidence_items");

            migrationBuilder.DropIndex(
                name: "IX_vulnerabilities_Source",
                table: "vulnerabilities");

            migrationBuilder.DropIndex(
                name: "IX_sla_events_At",
                table: "sla_events");

            migrationBuilder.DropIndex(
                name: "IX_resource_snapshots_Kind",
                table: "resource_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_notifications_At",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_evidence_items_CollectedAt",
                table: "evidence_items");

            migrationBuilder.DropIndex(
                name: "IX_changes_OpenedAt",
                table: "changes");

            migrationBuilder.DropIndex(
                name: "IX_automation_deliveries_At",
                table: "automation_deliveries");

            migrationBuilder.Sql("ALTER TABLE resource_snapshots ALTER COLUMN \"AttributesJson\" TYPE character varying(4000) USING \"AttributesJson\"::text;");

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_items_controls_ControlId",
                table: "evidence_items",
                column: "ControlId",
                principalTable: "controls",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
