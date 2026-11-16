using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D2DAudit.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class Continuity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "continuity_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: ""),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_continuity_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "impact_analyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Criticality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RtoHours = table.Column<int>(type: "integer", nullable: false),
                    RpoHours = table.Column<int>(type: "integer", nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    ControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: ""),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_impact_analyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recovery_exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                    ControlCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: ""),
                    RanAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_exercises", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "continuity_plans");

            migrationBuilder.DropTable(
                name: "impact_analyses");

            migrationBuilder.DropTable(
                name: "recovery_exercises");
        }
    }
}
