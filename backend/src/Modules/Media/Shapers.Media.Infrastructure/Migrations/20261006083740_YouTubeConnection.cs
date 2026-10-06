using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class YouTubeConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "youtube_connections",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    protected_refresh_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    connected_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_youtube_connections", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "youtube_connections",
                schema: "media");
        }
    }
}
