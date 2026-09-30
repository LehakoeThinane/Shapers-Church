using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Privacy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBreaches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "breaches",
                schema: "privacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    data_involved = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    people_affected = table.Column<int>(type: "integer", nullable: true),
                    special_information = table.Column<bool>(type: "boolean", nullable: false),
                    containment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    regulator_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    people_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_breaches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_breaches_status_discovered_at",
                schema: "privacy",
                table: "breaches",
                columns: new[] { "status", "discovered_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "breaches",
                schema: "privacy");
        }
    }
}
