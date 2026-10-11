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
    /// The SQL the owner runs: role (idempotent), then REVOKE everything and GRANT the allow-list.
    /// Pure (model in, statements out) so a test can assert what is — and is not — granted.
    /// </summary>
    public static IReadOnlyList<string> BuildStatements(IModel model, string database, string password)
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
                + $"NOBYPASSRLS CONNECTION LIMIT 10 PASSWORD '{password}'",
            $"ALTER ROLE {RoleName} SET default_transaction_read_only = on",
            $"ALTER ROLE {RoleName} SET statement_timeout = '30s'",
            // PostgreSQL < 15 (or a dump restored from one) lets PUBLIC create in schema public.
            "REVOKE CREATE ON SCHEMA public FROM PUBLIC",
            $"REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {RoleName}",
            $"REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {RoleName}",
            $"REVOKE ALL ON SCHEMA public FROM {RoleName}",
            $"GRANT CONNECT ON DATABASE {Quote(database)} TO {RoleName}",
            $"GRANT USAGE ON SCHEMA public TO {RoleName}",
        };

        foreach (var type in McpReadOnlyGrants.Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            Guard(type);
            sql.Add($"GRANT SELECT ON {Table(model, type)} TO {RoleName}");
        }

        foreach (var (type, properties) in McpReadOnlyGrants.Columns.OrderBy(kv => kv.Key.Name, StringComparer.Ordinal))
        {
            Guard(type);
            var entity = Entity(model, type);
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var columns = properties.Select(p =>
                Quote(entity.FindProperty(p)?.GetColumnName(store)
                      ?? throw new InvalidOperationException($"{type.Name}.{p} is not a mapped column")));
            sql.Add($"GRANT SELECT ({string.Join(", ", columns)}) ON {Table(model, type)} TO {RoleName}");
        }

        return sql;
    }

    /// <summary>Create/refresh the role and its grants through the OWNER connection.</summary>
    public static void Ensure(string ownerConnectionString, IModel model, string password)
    {
        var database = new NpgsqlConnectionStringBuilder(ownerConnectionString).Database
            ?? throw new InvalidOperationException("ConnectionStrings:Default has no Database");
        using var conn = new NpgsqlConnection(ownerConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        foreach (var statement in BuildStatements(model, database, password))
        {
            using var cmd = new NpgsqlCommand(statement, conn, tx);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// <c>null</c> = the connection is what it claims: logged in as <see cref="RoleName"/>, not a
    /// superuser, member of no other role, session read-only, no CREATE on the schema, not a
    /// single non-SELECT privilege on any table or column, owns nothing, and cannot read the
    /// password hash. Otherwise the reason. Never echoes exception text (it can carry the
    /// connection string).
    /// </summary>
    public static string? SelfTest(string readOnlyConnectionString)
    {
        try
        {
            using var conn = new NpgsqlConnection(readOnlyConnectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("""
                SELECT current_user::text,
                       (SELECT rolsuper FROM pg_roles WHERE rolname = current_user),
                       current_setting('transaction_read_only'),
                       has_schema_privilege(current_user, 'public', 'CREATE'),
                       EXISTS (SELECT 1 FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.member
                                WHERE r.rolname = current_user),
                       (SELECT count(*) FROM information_schema.role_table_grants
                         WHERE grantee = current_user AND privilege_type <> 'SELECT'),
                       (SELECT count(*) FROM information_schema.column_privileges
                         WHERE grantee = current_user AND privilege_type <> 'SELECT'),
                       (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                         WHERE n.nspname = 'public' AND pg_get_userbyid(c.relowner) = current_user),
                       COALESCE(has_column_privilege(current_user, '"Users"', 'PasswordHash', 'SELECT'), false)
                """, conn);
            using var r = cmd.ExecuteReader();
            r.Read();
            var user = r.GetString(0);
            if (user != RoleName) return $"connected as '{user}', expected '{RoleName}'";
            if (r.GetBoolean(1)) return "role is a superuser";
            if (r.GetString(2) != "on") return "session is not read-only";
            if (r.GetBoolean(3)) return "role may CREATE in schema public";
            if (r.GetBoolean(4)) return "role is a member of another role";
            if (r.GetInt64(5) > 0) return "role holds non-SELECT table privileges";
            if (r.GetInt64(6) > 0) return "role holds non-SELECT column privileges";
            if (r.GetInt64(7) > 0) return "role owns objects";
            if (r.GetBoolean(8)) return "role can read Users.PasswordHash";
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
