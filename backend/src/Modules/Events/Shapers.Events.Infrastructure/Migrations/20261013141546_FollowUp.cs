using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Events.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "follow_up_registrants",
                schema: "events",
                table: "events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "mobile_required",
                schema: "events",
                table: "events",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "follow_up_registrants",
                schema: "events",
                table: "events");

            migrationBuilder.DropColumn(
                name: "mobile_required",
                schema: "events",
                table: "events");
        }
    }
}
