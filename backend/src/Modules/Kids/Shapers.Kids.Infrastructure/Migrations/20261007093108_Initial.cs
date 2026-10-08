using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Kids.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kids");

            migrationBuilder.CreateTable(
                name: "care_notes",
                schema: "kids",
                columns: table => new
                {
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allergies = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    medical = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    other = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_notes", x => x.child_id);
                });

            migrationBuilder.CreateTable(
                name: "check_ins",
                schema: "kids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: true),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    pickup_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    checked_in_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    collected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_ins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "classes",
                schema: "kids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    from_age = table.Column<int>(type: "integer", nullable: false),
                    to_age = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "kids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_check_ins_child_id_date",
                schema: "kids",
                table: "check_ins",
                columns: new[] { "child_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_check_ins_date_pickup_code",
                schema: "kids",
                table: "check_ins",
                columns: new[] { "date", "pickup_code" });

            migrationBuilder.CreateIndex(
                name: "ix_check_ins_guardian_id",
                schema: "kids",
                table: "check_ins",
                column: "guardian_id");

            migrationBuilder.CreateIndex(
                name: "ix_check_ins_scope",
                schema: "kids",
                table: "check_ins",
                column: "scope")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "kids",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_notes",
                schema: "kids");

            migrationBuilder.DropTable(
                name: "check_ins",
                schema: "kids");

            migrationBuilder.DropTable(
                name: "classes",
                schema: "kids");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "kids");
        }
    }
}
