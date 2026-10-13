using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Events.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotifyEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notify_email",
                schema: "events",
                table: "events",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notify_email",
                schema: "events",
                table: "events");
        }
    }
}
