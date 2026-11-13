using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.People.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consent_requests",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consent_requests_person_id_purpose",
                schema: "people",
                table: "consent_requests",
                columns: new[] { "person_id", "purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consent_requests",
                schema: "people");
        }
    }
}
