using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Translations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_posts_slug",
                schema: "content",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_pages_slug",
                schema: "content",
                table: "pages");

            migrationBuilder.AddColumn<string>(
                name: "language",
                schema: "content",
                table: "posts",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "translated_from_version",
                schema: "content",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "translation_checked_at",
                schema: "content",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "translation_checked_by_user_id",
                schema: "content",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "translation_of_id",
                schema: "content",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                schema: "content",
                table: "pages",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "translated_from_version",
                schema: "content",
                table: "pages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "translation_checked_at",
                schema: "content",
                table: "pages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "translation_checked_by_user_id",
                schema: "content",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "translation_of_id",
                schema: "content",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_posts_slug_language",
                schema: "content",
                table: "posts",
                columns: new[] { "slug", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_posts_translation_of_id_language",
                schema: "content",
                table: "posts",
                columns: new[] { "translation_of_id", "language" },
                unique: true,
                filter: "translation_of_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pages_slug_language",
                schema: "content",
                table: "pages",
                columns: new[] { "slug", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pages_translation_of_id_language",
                schema: "content",
                table: "pages",
                columns: new[] { "translation_of_id", "language" },
                unique: true,
                filter: "translation_of_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_posts_slug_language",
                schema: "content",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_translation_of_id_language",
                schema: "content",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_pages_slug_language",
                schema: "content",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "ix_pages_translation_of_id_language",
                schema: "content",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "language",
                schema: "content",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "translated_from_version",
                schema: "content",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "translation_checked_at",
                schema: "content",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "translation_checked_by_user_id",
                schema: "content",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "translation_of_id",
                schema: "content",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "language",
                schema: "content",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "translated_from_version",
                schema: "content",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "translation_checked_at",
                schema: "content",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "translation_checked_by_user_id",
                schema: "content",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "translation_of_id",
                schema: "content",
                table: "pages");

            migrationBuilder.CreateIndex(
                name: "ix_posts_slug",
                schema: "content",
                table: "posts",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pages_slug",
                schema: "content",
                table: "pages",
                column: "slug",
                unique: true);
        }
    }
}
