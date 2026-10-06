using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Services.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TeamCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "services",
                table: "teams",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teams_category_id",
                schema: "services",
                table: "teams",
                column: "category_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categories",
                schema: "services");

            migrationBuilder.DropIndex(
                name: "ix_teams_category_id",
                schema: "services",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "services",
                table: "teams");
        }
    }
}
