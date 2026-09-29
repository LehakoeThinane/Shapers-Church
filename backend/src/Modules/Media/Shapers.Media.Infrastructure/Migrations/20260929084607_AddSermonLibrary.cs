using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace Shapers.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSermonLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.CreateTable(
                name: "assets",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "media",
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
                name: "playback_positions",
                schema: "media",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sermon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_seconds = table.Column<int>(type: "integer", nullable: false),
                    completed = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playback_positions", x => new { x.person_id, x.sermon_id });
                });

            migrationBuilder.CreateTable(
                name: "series",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    artwork_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: true),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sermons",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    preached_on = table.Column<DateOnly>(type: "date", nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    publish_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    topics = table.Column<List<string>>(type: "text[]", nullable: false),
                    video_provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    video_external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    audio_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audio_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    notes_pdf_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    import_source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    search_text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('english', coalesce(search_text, ''))", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sermons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "speakers",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    photo_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_speakers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sermon_scripture",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    book_number = table.Column<int>(type: "integer", nullable: false),
                    chapter_from = table.Column<int>(type: "integer", nullable: false),
                    verse_from = table.Column<int>(type: "integer", nullable: true),
                    chapter_to = table.Column<int>(type: "integer", nullable: false),
                    verse_to = table.Column<int>(type: "integer", nullable: true),
                    sermon_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sermon_scripture", x => x.id);
                    table.ForeignKey(
                        name: "fk_sermon_scripture_sermons_sermon_id",
                        column: x => x.sermon_id,
                        principalSchema: "media",
                        principalTable: "sermons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sermon_speakers",
                schema: "media",
                columns: table => new
                {
                    speaker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sermon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sermon_speakers", x => new { x.sermon_id, x.speaker_id });
                    table.ForeignKey(
                        name: "fk_sermon_speakers_sermons_sermon_id",
                        column: x => x.sermon_id,
                        principalSchema: "media",
                        principalTable: "sermons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assets_storage_key",
                schema: "media",
                table: "assets",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "media",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_playback_positions_updated_at",
                schema: "media",
                table: "playback_positions",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_series_slug",
                schema: "media",
                table: "series",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sermon_scripture_book_number",
                schema: "media",
                table: "sermon_scripture",
                column: "book_number");

            migrationBuilder.CreateIndex(
                name: "ix_sermon_scripture_sermon_id",
                schema: "media",
                table: "sermon_scripture",
                column: "sermon_id");

            migrationBuilder.CreateIndex(
                name: "ix_sermon_speakers_speaker_id",
                schema: "media",
                table: "sermon_speakers",
                column: "speaker_id");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_import_source",
                schema: "media",
                table: "sermons",
                column: "import_source",
                unique: true,
                filter: "import_source IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_scope",
                schema: "media",
                table: "sermons",
                column: "scope")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_sermons_search_vector",
                schema: "media",
                table: "sermons",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_series_id",
                schema: "media",
                table: "sermons",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_slug",
                schema: "media",
                table: "sermons",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sermons_status_preached_on",
                schema: "media",
                table: "sermons",
                columns: new[] { "status", "preached_on" });

            migrationBuilder.CreateIndex(
                name: "ix_sermons_topics",
                schema: "media",
                table: "sermons",
                column: "topics")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_speakers_name",
                schema: "media",
                table: "speakers",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assets",
                schema: "media");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "media");

            migrationBuilder.DropTable(
                name: "playback_positions",
                schema: "media");

            migrationBuilder.DropTable(
                name: "series",
                schema: "media");

            migrationBuilder.DropTable(
                name: "sermon_scripture",
                schema: "media");

            migrationBuilder.DropTable(
                name: "sermon_speakers",
                schema: "media");

            migrationBuilder.DropTable(
                name: "speakers",
                schema: "media");

            migrationBuilder.DropTable(
                name: "sermons",
                schema: "media");
        }
    }
}
