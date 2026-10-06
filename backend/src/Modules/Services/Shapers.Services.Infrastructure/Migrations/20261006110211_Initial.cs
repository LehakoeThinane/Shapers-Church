using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Services.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "services");

            migrationBuilder.CreateTable(
                name: "assignments",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    decline_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "blockouts",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blockouts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "members",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    is_leader = table.Column<bool>(type: "boolean", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "services",
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

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    series_title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    livestream_id = table.Column<Guid>(type: "uuid", nullable: true),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    needs = table.Column<string>(type: "jsonb", nullable: false),
                    live_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    live_item_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    live_ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_types",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    needs = table.Column<string>(type: "jsonb", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "songs",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ccli_number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    themes = table.Column<List<string>>(type: "text[]", nullable: false),
                    lyrics = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    reference_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    arrangements = table.Column<string>(type: "jsonb", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_songs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    open_to_minors = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_date_status",
                schema: "services",
                table: "assignments",
                columns: new[] { "date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_person_id_date",
                schema: "services",
                table: "assignments",
                columns: new[] { "person_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_plan_id_position_id_person_id",
                schema: "services",
                table: "assignments",
                columns: new[] { "plan_id", "position_id", "person_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_blockouts_person_id_to",
                schema: "services",
                table: "blockouts",
                columns: new[] { "person_id", "to" });

            migrationBuilder.CreateIndex(
                name: "ix_members_person_id",
                schema: "services",
                table: "members",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_members_team_id_person_id",
                schema: "services",
                table: "members",
                columns: new[] { "team_id", "person_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "services",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plans_date",
                schema: "services",
                table: "plans",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_plans_scope",
                schema: "services",
                table: "plans",
                column: "scope")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_team_id",
                schema: "services",
                table: "positions",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_songs_ccli_number",
                schema: "services",
                table: "songs",
                column: "ccli_number");

            migrationBuilder.CreateIndex(
                name: "ix_songs_title",
                schema: "services",
                table: "songs",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ix_teams_scope",
                schema: "services",
                table: "teams",
                column: "scope")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignments",
                schema: "services");

            migrationBuilder.DropTable(
                name: "blockouts",
                schema: "services");

            migrationBuilder.DropTable(
                name: "members",
                schema: "services");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "services");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "services");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "services");

            migrationBuilder.DropTable(
                name: "service_types",
                schema: "services");

            migrationBuilder.DropTable(
                name: "songs",
                schema: "services");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "services");
        }
    }
}
