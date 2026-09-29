using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.People.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "connect_cards",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    handled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    handler_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connect_cards", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_connect_cards_scope",
                schema: "people",
                table: "connect_cards",
                column: "scope")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_connect_cards_status_submitted_at",
                schema: "people",
                table: "connect_cards",
                columns: new[] { "status", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connect_cards",
                schema: "people");
        }
    }
}
