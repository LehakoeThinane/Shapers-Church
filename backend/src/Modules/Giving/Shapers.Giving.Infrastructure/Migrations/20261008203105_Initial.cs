using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Giving.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "giving");

            migrationBuilder.CreateTable(
                name: "funds",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gifts",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    giver_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    giver_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    given_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_checkout_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gifts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "giving",
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
                name: "ix_gifts_person_id",
                schema: "giving",
                table: "gifts",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_gifts_provider_checkout_id",
                schema: "giving",
                table: "gifts",
                column: "provider_checkout_id",
                unique: true,
                filter: "provider_checkout_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gifts_status_given_on",
                schema: "giving",
                table: "gifts",
                columns: new[] { "status", "given_on" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "giving",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "funds",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "gifts",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "giving");
        }
    }
}
