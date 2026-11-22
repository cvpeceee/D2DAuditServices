using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.ControlPlane
{
    /// <inheritdoc />
    public partial class M11Unified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "member_invites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_invites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "signup_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    EmailDomain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signup_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_onboarding",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanySize = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Industry = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CloudProviders = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataTypes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Frameworks = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ScopedControls = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CompletedSteps = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuditTargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Dismissed = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_onboarding", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "tenant_security_settings",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SsoEnforced = table.Column<bool>(type: "boolean", nullable: false),
                    ScimTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScimTokenPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ScimTokenCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_security_settings", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "tenant_subscriptions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plan = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SubscriptionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrentPeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_subscriptions", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_member_invites_TenantId_Email",
                table: "member_invites",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_member_invites_TokenHash",
                table: "member_invites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signup_requests_Email",
                table: "signup_requests",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_signup_requests_EmailDomain",
                table: "signup_requests",
                column: "EmailDomain");

            migrationBuilder.CreateIndex(
                name: "IX_signup_requests_TokenHash",
                table: "signup_requests",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_subscriptions_CustomerId",
                table: "tenant_subscriptions",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_events");

            migrationBuilder.DropTable(
                name: "member_invites");

            migrationBuilder.DropTable(
                name: "signup_requests");

            migrationBuilder.DropTable(
                name: "tenant_onboarding");

            migrationBuilder.DropTable(
                name: "tenant_security_settings");

            migrationBuilder.DropTable(
                name: "tenant_subscriptions");
        }
    }
}
