using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.People.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "people");

            migrationBuilder.CreateTable(
                name: "consent_records",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    granted = table.Column<bool>(type: "boolean", nullable: false),
                    lawful_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    policy_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "duplicate_candidates",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    reasons = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duplicate_candidates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "households",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    primary_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_households", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "membership_statuses",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "people",
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
                name: "person_merges",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    survivor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_merges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "household_members",
                schema: "people",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_household_members", x => new { x.household_id, x.person_id });
                    table.ForeignKey(
                        name: "fk_household_members_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "people",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "persons",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    preferred_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    membership_status_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    merged_into_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_persons", x => x.id);
                    table.ForeignKey(
                        name: "fk_persons_membership_statuses_membership_status_id",
                        column: x => x.membership_status_id,
                        principalSchema: "people",
                        principalTable: "membership_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_status_changes",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_status_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_membership_status_changes_persons_person_id",
                        column: x => x.person_id,
                        principalSchema: "people",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "person_contacts",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_person_contacts_persons_person_id",
                        column: x => x.person_id,
                        principalSchema: "people",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consent_records_person_id_purpose_recorded_at",
                schema: "people",
                table: "consent_records",
                columns: new[] { "person_id", "purpose", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_candidates_person_a_id_person_b_id",
                schema: "people",
                table: "duplicate_candidates",
                columns: new[] { "person_a_id", "person_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_candidates_status",
                schema: "people",
                table: "duplicate_candidates",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_household_members_person_id",
                schema: "people",
                table: "household_members",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_households_scope",
                schema: "people",
                table: "households",
                column: "scope")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_membership_status_changes_person_id",
                schema: "people",
                table: "membership_status_changes",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_statuses_is_default",
                schema: "people",
                table: "membership_statuses",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "people",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_person_contacts_person_id",
                schema: "people",
                table: "person_contacts",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_contacts_value",
                schema: "people",
                table: "person_contacts",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "ix_person_merges_merged_id",
                schema: "people",
                table: "person_merges",
                column: "merged_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_merges_survivor_id",
                schema: "people",
                table: "person_merges",
                column: "survivor_id");

            migrationBuilder.CreateIndex(
                name: "ix_persons_last_name_first_name",
                schema: "people",
                table: "persons",
                columns: new[] { "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_persons_membership_status_id",
                schema: "people",
                table: "persons",
                column: "membership_status_id");

            migrationBuilder.CreateIndex(
                name: "ix_persons_merged_into_id",
                schema: "people",
                table: "persons",
                column: "merged_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_persons_scope",
                schema: "people",
                table: "persons",
                column: "scope")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consent_records",
                schema: "people");

            migrationBuilder.DropTable(
                name: "duplicate_candidates",
                schema: "people");

            migrationBuilder.DropTable(
                name: "household_members",
                schema: "people");

            migrationBuilder.DropTable(
                name: "membership_status_changes",
                schema: "people");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "people");

            migrationBuilder.DropTable(
                name: "person_contacts",
                schema: "people");

            migrationBuilder.DropTable(
                name: "person_merges",
                schema: "people");

            migrationBuilder.DropTable(
                name: "households",
                schema: "people");

            migrationBuilder.DropTable(
                name: "persons",
                schema: "people");

            migrationBuilder.DropTable(
                name: "membership_statuses",
                schema: "people");
        }
    }
}
