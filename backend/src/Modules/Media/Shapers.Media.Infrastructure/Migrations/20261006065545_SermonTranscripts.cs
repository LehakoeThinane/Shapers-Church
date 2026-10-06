using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SermonTranscripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "transcript",
                schema: "media",
                table: "sermons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transcript_error",
                schema: "media",
                table: "sermons",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transcript_source",
                schema: "media",
                table: "sermons",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transcript_status",
                schema: "media",
                table: "sermons",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transcript_updated_at",
                schema: "media",
                table: "sermons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sermons_transcript_status",
                schema: "media",
                table: "sermons",
                column: "transcript_status",
                filter: "transcript_status IN ('Queued', 'Working')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sermons_transcript_status",
                schema: "media",
                table: "sermons");

            migrationBuilder.DropColumn(
                name: "transcript",
                schema: "media",
                table: "sermons");

            migrationBuilder.DropColumn(
                name: "transcript_error",
                schema: "media",
                table: "sermons");

            migrationBuilder.DropColumn(
                name: "transcript_source",
                schema: "media",
                table: "sermons");

            migrationBuilder.DropColumn(
                name: "transcript_status",
                schema: "media",
                table: "sermons");

            migrationBuilder.DropColumn(
                name: "transcript_updated_at",
                schema: "media",
                table: "sermons");
        }
    }
}
