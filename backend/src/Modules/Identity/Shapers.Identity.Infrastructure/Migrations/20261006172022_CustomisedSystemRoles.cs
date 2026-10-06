using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomisedSystemRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_customised",
                schema: "identity",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_customised",
                schema: "identity",
                table: "roles");
        }
    }
}
