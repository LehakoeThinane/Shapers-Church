using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shapers.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowAuditRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Entries still can't be edited or truncated. Deleting is now allowed only for rows older than five
            // years, so the retention job can remove them and nothing else can.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_entries_append_only ON platform.audit_entries;

                CREATE TRIGGER audit_entries_append_only
                BEFORE UPDATE OR TRUNCATE ON platform.audit_entries
                FOR EACH STATEMENT EXECUTE FUNCTION platform.audit_entries_append_only();

                CREATE FUNCTION platform.audit_entries_retention_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.occurred_at >= now() - interval '5 years' THEN
                        RAISE EXCEPTION 'platform.audit_entries: only entries older than five years may be deleted';
                    END IF;
                    RETURN OLD;
                END;
                $$;

                CREATE TRIGGER audit_entries_retention_only
                BEFORE DELETE ON platform.audit_entries
                FOR EACH ROW EXECUTE FUNCTION platform.audit_entries_retention_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_entries_retention_only ON platform.audit_entries;
                DROP FUNCTION IF EXISTS platform.audit_entries_retention_only();
                DROP TRIGGER IF EXISTS audit_entries_append_only ON platform.audit_entries;

                CREATE TRIGGER audit_entries_append_only
                BEFORE UPDATE OR DELETE OR TRUNCATE ON platform.audit_entries
                FOR EACH STATEMENT EXECUTE FUNCTION platform.audit_entries_append_only();
                """);
        }
    }
}
