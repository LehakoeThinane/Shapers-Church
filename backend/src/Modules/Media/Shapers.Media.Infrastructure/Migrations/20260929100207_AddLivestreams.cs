using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLivestreams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "livestreams",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    video_provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    video_external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    give_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_cue_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sermon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_livestreams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "livestream_cues",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    shown_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    livestream_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_livestream_cues", x => x.id);
                    table.ForeignKey(
                        name: "fk_livestream_cues_livestreams_livestream_id",
                        column: x => x.livestream_id,
                        principalSchema: "media",
                        principalTable: "livestreams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_livestream_cues_livestream_id",
                schema: "media",
                table: "livestream_cues",
                column: "livestream_id");

            migrationBuilder.CreateIndex(
                name: "ix_livestreams_status_scheduled_start",
                schema: "media",
                table: "livestreams",
                columns: new[] { "status", "scheduled_start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "livestream_cues",
                schema: "media");

            migrationBuilder.DropTable(
                name: "livestreams",
                schema: "media");
        }
    }
}
