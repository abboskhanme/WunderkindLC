using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using WunderkindLC.Application.Mcp;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>
/// The SELECT-only PostgreSQL role the MCP tools connect as (docs/SPEC-mcp.md §3).
///
/// <para>
/// The web app itself connects as the database OWNER, so "the app never writes through MCP"
/// cannot rest on application code alone. At startup — only when <c>Mcp:ReadOnlyPassword</c>
/// is configured — the owner connection (re)creates this role and grants it exactly the
/// allow-list (<see cref="McpReadOnlyGrants"/>); then the read-only connection must pass
/// <see cref="SelfTest"/> or the MCP endpoint stays off (fail-closed).
/// </para>
/// </summary>
public static partial class McpReadOnlyRole
{
    public const string RoleName = "wlc_ro";

    /// <summary>
    /// The password is embedded in <c>ALTER ROLE … PASSWORD '…'</c> (DDL takes no parameters),
    /// so it is restricted to characters that cannot break out of a literal.
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{16,128}$")]
    private static partial Regex PasswordPattern();

    public static bool IsValidPassword(string? password) =>
        password is not null && PasswordPattern().IsMatch(password);

    /// <summary>
    /// The owner connection string re-pointed at the read-only role and hardened: small pool
    /// (fits next to the app's own pool), short timeouts, NO error detail (it can echo row data
    /// into logs), and every session starts read-only even if the role setting were lost.
    /// </summary>
    public static string BuildConnectionString(string ownerConnectionString, string password)
    {
        var b = new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = RoleName,
            Password = password,
            IncludeErrorDetail = false,
            CommandTimeout = 30,
            ApplicationName = "WunderkindLC.Mcp",
        };
        b.Timeout = Math.Min(b.Timeout, 5);
        b.MaxPoolSize = Math.Min(b.MaxPoolSize, 8);
        b.MinPoolSize = 0;
        const string ro = "-c default_transaction_read_only=on";
        if (!(b.Options ?? "").Contains("default_transaction_read_only", StringComparison.OrdinalIgnoreCase))
            b.Options = string.IsNullOrWhiteSpace(b.Options) ? ro : b.Options + " " + ro;
        return b.ConnectionString;
    }

    /// <summary>
    /// The SQL the owner runs: role (idempotent), then REVOKE everything (column privileges go with the
    /// table's) and GRANT the allow-list (<see cref="Grants"/>). Pure (model in, statements out) so a test can
    /// assert what is — and is not — granted. <paramref name="model"/> is the APPLICATION model.
    /// </summary>
    /// <param name="setPassword">false when the role already logs in with this password — the plaintext
    /// then never travels (it would be visible to statement logging / pg_stat_statements).</param>
    public static IReadOnlyList<string> BuildStatements(IModel model, string database, string password, bool setPassword = true)
    {
        if (!IsValidPassword(password))
            throw new ArgumentException("Mcp:ReadOnlyPassword must be 16–128 characters of [A-Za-z0-9_-].");

        var sql = new List<string>
        {
            $"""
            DO $$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{RoleName}') THEN
                CREATE ROLE {RoleName} LOGIN;
              END IF;
            END $$
            """,
            $"ALTER ROLE {RoleName} WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION "
                + "NOBYPASSRLS CONNECTION LIMIT 10",
            $"ALTER ROLE {RoleName} SET default_transaction_read_only = on",
            $"ALTER ROLE {RoleName} SET statement_timeout = '30s'",
            // PostgreSQL < 15 (or a dump restored from one) lets PUBLIC create in schema public.
            "REVOKE CREATE ON SCHEMA public FROM PUBLIC",
            $"REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {RoleName}",
            $"REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {RoleName}",
            $"REVOKE ALL ON SCHEMA public FROM {RoleName}",
            // Temporary tables are a write path PostgreSQL gives every role by default (via PUBLIC). The
            // app connects as the superuser owner, which is unaffected.
            $"REVOKE TEMPORARY ON DATABASE {Quote(database)} FROM PUBLIC",
            $"REVOKE ALL ON DATABASE {Quote(database)} FROM {RoleName}",
            $"GRANT CONNECT ON DATABASE {Quote(database)} TO {RoleName}",
            $"GRANT USAGE ON SCHEMA public TO {RoleName}",
        };
        if (setPassword) sql.Insert(2, $"ALTER ROLE {RoleName} PASSWORD '{password}'");

        foreach (var grant in Grants(model))
        {
            var table = Table(model, grant.Entity);
            sql.Add(grant.Columns is null
                ? $"GRANT SELECT ON {table} TO {RoleName}"
                : $"GRANT SELECT ({string.Join(", ", grant.Columns.Select(Quote))}) ON {table} TO {RoleName}");
        }

        return sql;
    }

    /// <summary>
    /// One table's grant: <see cref="Columns"/> <c>null</c> = the whole table, otherwise exactly these
    /// (store) column names.
    /// </summary>
    public sealed record TableGrant(Type Entity, string Table, IReadOnlyList<string>? Columns);

    /// <summary>
    /// The grants the role gets, from the APPLICATION model (every column the database has): one per entity a
    /// tool reads (<see cref="McpToolRegistry.ReadEntities"/>), column-level when
    /// <see cref="McpReadOnlyGrants.GrantedProperties"/> closes anything. Pure.
    /// </summary>
    public static IReadOnlyList<TableGrant> Grants(IModel model)
    {
        if (model.FindAnnotation(McpReadOnlyModel.Annotation) is not null)
            throw new InvalidOperationException(
                "Grants must be built from the application model, not the reduced MCP read-only model "
                + "(its sensitive columns are already removed, so they would look absent and the whole table would be granted).");

        var list = new List<TableGrant>();
        foreach (var type in McpToolRegistry.ReadEntities.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            Guard(type);
            var entity = Entity(model, type);
            var tableName = entity.GetTableName() ?? throw new InvalidOperationException($"{type.Name} has no table");
            var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
            var properties = McpReadOnlyGrants.GrantedProperties(type, entity.GetProperties().Select(p => p.Name));
            var columns = properties?.Select(p =>
                    entity.FindProperty(p)?.GetColumnName(store)
                    ?? throw new InvalidOperationException($"{type.Name}.{p} is not a mapped column"))
                .ToList();
            list.Add(new TableGrant(type, tableName, columns));
        }
        return list;
    }

    /// <summary>The column-level grants only (tables with at least one closed column) — the test host checks
    /// every captured query against these.</summary>
    public static IReadOnlyList<TableGrant> ColumnGrants(IModel model) =>
        Grants(model).Where(g => g.Columns is not null).ToList();

    /// <summary>Create/refresh the role and its grants through the OWNER connection.</summary>
    public static void Ensure(string ownerConnectionString, IModel model, string password)
    {
        var database = new NpgsqlConnectionStringBuilder(ownerConnectionString).Database
            ?? throw new InvalidOperationException("ConnectionStrings:Default has no Database");
        // Send the password only when the role does not already log in with it.
        var setPassword = !CanLogIn(BuildConnectionString(ownerConnectionString, password));

        using var conn = new NpgsqlConnection(ownerConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        if (setPassword) KeepOutOfLogs(conn, tx);
        foreach (var statement in BuildStatements(model, database, password, setPassword))
        {
            using var cmd = new NpgsqlCommand(statement, conn, tx);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static bool CanLogIn(string readOnlyConnectionString)
    {
        try
        {
            using var c = new NpgsqlConnection(readOnlyConnectionString);
            c.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// For this transaction only: no statement logging and no utility tracking, so the ALTER ROLE … PASSWORD
    /// text cannot land in server logs. These settings need a superuser; without one nothing is changed.
    /// </summary>
    private static void KeepOutOfLogs(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        using var check = new NpgsqlCommand("""
            SELECT (SELECT rolsuper FROM pg_roles WHERE rolname = current_user),
                   EXISTS (SELECT 1 FROM pg_settings WHERE name = 'pg_stat_statements.track_utility')
            """, conn, tx);
        using var r = check.ExecuteReader();
        r.Read();
        var super = r.GetBoolean(0);
        var pgss = r.GetBoolean(1);
        r.Close();
        if (!super) return;
        var sql = "SET LOCAL log_statement = 'none'; SET LOCAL log_min_duration_statement = -1;";
        if (pgss) sql += " SET LOCAL pg_stat_statements.track_utility = off;";
        using var set = new NpgsqlCommand(sql, conn, tx);
        set.ExecuteNonQuery();
    }

    /// <summary>
    /// <c>null</c> = the connection is what it claims: logged in as <see cref="RoleName"/>, not a
    /// superuser, member of no other role, session read-only, no CREATE on the schema, not a
    /// single non-SELECT privilege on any table or column, owns nothing, cannot read the
    /// password hash nor any column with a sensitive name (<see cref="McpReadOnlyGrants.IsSensitiveColumn"/>).
    /// Otherwise the reason. Never echoes exception text (it can carry the
    /// connection string).
    /// </summary>
    public static string? SelfTest(string readOnlyConnectionString, IModel? model = null)
    {
        try
        {
            using var conn = new NpgsqlConnection(readOnlyConnectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("""
                SELECT current_user::text,
                       (SELECT rolsuper FROM pg_roles WHERE rolname = current_user),
                       current_setting('transaction_read_only'),
                       (SELECT count(*) FROM pg_namespace WHERE has_schema_privilege(current_user, oid, 'CREATE')),
                       EXISTS (SELECT 1 FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.member
                                WHERE r.rolname = current_user),
                       -- effective privileges (direct, via PUBLIC, via any role), not only direct grants
                       (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                         WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
                           AND has_table_privilege(current_user, c.oid, 'INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')),
                       (SELECT count(*) FROM information_schema.column_privileges
                         WHERE grantee = current_user AND privilege_type <> 'SELECT'),
                       (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                         WHERE n.nspname = 'public' AND pg_get_userbyid(c.relowner) = current_user),
                       has_database_privilege(current_user, current_database(), 'CREATE'),
                       has_database_privilege(current_user, current_database(), 'TEMPORARY'),
                       COALESCE(has_column_privilege(current_user, '"Users"', 'PasswordHash', 'SELECT'), false)
                         OR COALESCE(has_column_privilege(current_user, '"Users"', 'InitialPassword', 'SELECT'), false)
                         OR COALESCE(has_column_privilege(current_user, '"Users"', 'Email', 'SELECT'), false)
                         OR COALESCE(has_column_privilege(current_user, '"Users"', 'Permissions', 'SELECT'), false)
                """, conn);
            using (var r = cmd.ExecuteReader())
            {
                r.Read();
                var user = r.GetString(0);
                if (user != RoleName) return $"connected as '{user}', expected '{RoleName}'";
                if (r.GetBoolean(1)) return "role is a superuser";
                if (r.GetString(2) != "on") return "session is not read-only";
                if (r.GetInt64(3) > 0) return "role may CREATE in a schema";
                if (r.GetBoolean(4)) return "role is a member of another role";
                if (r.GetInt64(5) > 0) return "role holds a write privilege on a table (directly or via PUBLIC)";
                if (r.GetInt64(6) > 0) return "role holds non-SELECT column privileges";
                if (r.GetInt64(7) > 0) return "role owns objects";
                if (r.GetBoolean(8)) return "role may CREATE in the database";
                if (r.GetBoolean(9)) return "role may create TEMPORARY tables";
                if (r.GetBoolean(10)) return "role can read a secret Users column";
            }

            // Forbidden tables (sessions, codes, biometrics, MCP tokens, the student password vault…) must be unreadable —
            // not even ONE column: has_any_column_privilege is true for a table-level grant AND for a column-level grant of
            // any column (has_table_privilege alone misses `GRANT SELECT ("UserId") ON "StudentCredentials"`).
            if (model is not null)
                foreach (var type in McpReadOnlyGrants.Forbidden)
                {
                    if (model.FindEntityType(type) is null) continue;
                    using var f = new NpgsqlCommand("SELECT has_any_column_privilege(current_user, @t, 'SELECT')", conn);
                    f.Parameters.AddWithValue("t", Table(model, type));
                    if (f.ExecuteScalar() is true) return $"role can read forbidden table {type.Name}";
                }

            // No column with a sensitive NAME is readable — whatever granted it (a table-level grant shows up
            // here per column). The same rule the grants are built from (McpReadOnlyGrants.IsSensitiveColumn).
            using (var cols = new NpgsqlCommand("""
                SELECT table_name::text, column_name::text FROM information_schema.column_privileges
                 WHERE grantee = current_user AND privilege_type = 'SELECT' AND table_schema = 'public'
                """, conn))
            using (var c = cols.ExecuteReader())
                while (c.Read())
                    if (McpReadOnlyGrants.IsSensitiveColumn(c.GetString(1)))
                        return $"role can read the sensitive column {c.GetString(0)}.{c.GetString(1)}";
            return null;
        }
        catch (Exception ex)
        {
            return "cannot connect (" + ex.GetType().Name + ")";
        }
    }

    private static void Guard(Type type)
    {
        if (McpReadOnlyGrants.Forbidden.Contains(type))
            throw new InvalidOperationException(
                $"{type.Name} is on McpReadOnlyGrants.Forbidden and must never be granted to {RoleName}.");
    }

    private static IEntityType Entity(IModel model, Type type) =>
        model.FindEntityType(type) ?? throw new InvalidOperationException($"{type.Name} is not an entity");

    public static string Table(IModel model, Type type)
    {
        var entity = Entity(model, type);
        var schema = entity.GetSchema();
        var table = Quote(entity.GetTableName() ?? throw new InvalidOperationException($"{type.Name} has no table"));
        return schema is null ? "public." + table : Quote(schema) + "." + table;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
