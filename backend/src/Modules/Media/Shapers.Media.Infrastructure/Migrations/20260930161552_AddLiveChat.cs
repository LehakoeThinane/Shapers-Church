using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shapers.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "chat_approval_required",
                schema: "media",
                table: "livestreams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "chat_slow_seconds",
                schema: "media",
                table: "livestreams",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.CreateTable(
                name: "chat_blocked_terms",
                schema: "media",
                columns: table => new
                {
                    term = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_blocked_terms", x => x.term);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    livestream_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    from_team = table.Column<bool>(type: "boolean", nullable: false),
                    text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hold_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    moderated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    moderated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_sanctions",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    livestream_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lifted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    lifted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_sanctions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_reports",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_reports_chat_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "media",
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_livestream_id_sent_at",
                schema: "media",
                table: "chat_messages",
                columns: new[] { "livestream_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_person_id_sent_at",
                schema: "media",
                table: "chat_messages",
                columns: new[] { "person_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_sent_at",
                schema: "media",
                table: "chat_messages",
                column: "sent_at");

            migrationBuilder.CreateIndex(
                name: "ix_chat_reports_message_id_reporter_id",
                schema: "media",
                table: "chat_reports",
                columns: new[] { "message_id", "reporter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_reports_reporter_id",
                schema: "media",
                table: "chat_reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_sanctions_person_id_lifted_at",
                schema: "media",
                table: "chat_sanctions",
                columns: new[] { "person_id", "lifted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_blocked_terms",
                schema: "media");

            migrationBuilder.DropTable(
                name: "chat_reports",
                schema: "media");

            migrationBuilder.DropTable(
                name: "chat_sanctions",
                schema: "media");

            migrationBuilder.DropTable(
                name: "chat_messages",
                schema: "media");

            migrationBuilder.DropColumn(
                name: "chat_approval_required",
                schema: "media",
                table: "livestreams");

            migrationBuilder.DropColumn(
                name: "chat_slow_seconds",
                schema: "media",
                table: "livestreams");
        }
    }
}
