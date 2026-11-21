namespace D2DAudit.Infrastructure.Persistence.Migrations;

// Raw SQL shared by the M11 hardening migrations (objects EF Core cannot model: triggers, expression indexes).
public static class HardeningSql
{
    // Rejects UPDATE always; DELETE/TRUNCATE only allowed inside a retention purge that sets d2d.allow_purge = 'on'.
    public const string AppendOnlyFunction = """
        CREATE OR REPLACE FUNCTION d2d_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP <> 'UPDATE' AND coalesce(current_setting('d2d.allow_purge', true), '') = 'on' THEN
                RETURN OLD;
            END IF;
            RAISE EXCEPTION 'table % is append-only: % rejected', TG_TABLE_NAME, TG_OP USING ERRCODE = 'P0001';
        END;
        $$;
        """;

    public const string DropAppendOnlyFunction = "DROP FUNCTION IF EXISTS d2d_append_only();";

    public static string AppendOnly(string table) => $"""
        CREATE TRIGGER {table}_append_only BEFORE UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION d2d_append_only();
        CREATE TRIGGER {table}_no_truncate BEFORE TRUNCATE ON {table} FOR EACH STATEMENT EXECUTE FUNCTION d2d_append_only();
        """;

    public static string DropAppendOnly(string table) => $"""
        DROP TRIGGER IF EXISTS {table}_append_only ON {table};
        DROP TRIGGER IF EXISTS {table}_no_truncate ON {table};
        """;
}
