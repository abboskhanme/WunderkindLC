using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>
/// The database the MCP (AI client) tools read through — docs/SPEC-mcp.md.
///
/// <para>
/// It hands out ordinary <see cref="AppDbContext"/> instances (so every existing query
/// service can take them as <c>IAppDbContext</c>), but built from different options with
/// independent locks:
/// </para>
/// <list type="number">
///   <item>the connection logs in as the SELECT-only role (<see cref="McpReadOnlyRole"/>), whose
///     sessions are <c>default_transaction_read_only = on</c>;</item>
///   <item>a save-changes interceptor throws before anything reaches the database;</item>
///   <item>change tracking is off, so nothing is even staged;</item>
///   <item>a command interceptor lets only <c>SELECT</c> / <c>WITH</c> statements through;</item>
///   <item>the EF model is reduced to the role's grant (<see cref="McpReadOnlyModel"/>): columns the role may
///     not read do not exist for these contexts, so no query — not even a full-entity load in a shared
///     service — selects them.</item>
/// </list>
/// <para>
/// Deliberately NOT a <c>DbContext</c> subclass: a second context type in the assembly makes
/// every <c>dotnet ef migrations …</c> command fail with "More than one DbContext was found".
/// </para>
/// </summary>
public sealed class ReadOnlyDatabase(DbContextOptions<AppDbContext> options)
{
    /// <summary>A new context on the read-only connection. The caller disposes it.</summary>
    public AppDbContext CreateContext() => new(options);

    /// <summary>Production: PostgreSQL through the read-only role's connection string.</summary>
    public static ReadOnlyDatabase ForNpgsql(string readOnlyConnectionString) =>
        new(Decorate(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(readOnlyConnectionString,
                npg => npg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))));

    /// <summary>
    /// The locks that do not depend on the provider (2–5 above). Tests build the options on
    /// SQLite and pass them through here, so tool tests run under the same refusals and the same model.
    /// </summary>
    public static DbContextOptions<AppDbContext> Decorate(
        DbContextOptionsBuilder<AppDbContext> builder, params IInterceptor[] extra) =>
        builder
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(RefuseWrites.Instance, SelectOnly.Instance)
            .AddInterceptors(extra)
            .ReplaceService<IModelCustomizer, McpReadOnlyModel.Customizer>()
            .ReplaceService<IModelCacheKeyFactory, McpReadOnlyModel.CacheKeyFactory>()
            // Removing mapped properties is the point here, not a mistake worth a log line per start.
            .ConfigureWarnings(w => w.Ignore(CoreEventId.MappedPropertyIgnoredWarning))
            .Options;

    /// <summary>Any SaveChanges on a read-only context fails loudly.</summary>
    public sealed class RefuseWrites : SaveChangesInterceptor
    {
        public static readonly RefuseWrites Instance = new();

        public const string Message = "Read-only database (MCP tools): a write was attempted and refused.";

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result) =>
            throw new InvalidOperationException(Message);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Message);
    }

    /// <summary>Only SELECT / WITH … SELECT reach the connection. Anything else (raw SQL,
    /// a service's ExecuteUpdate/ExecuteDelete) is refused before it is sent.</summary>
    public sealed class SelectOnly : DbCommandInterceptor
    {
        public static readonly SelectOnly Instance = new();

        public const string Message = "Read-only database (MCP tools): only SELECT statements are allowed.";

        /// <summary>
        /// Built-in functions with side effects that a SELECT can still call (locks, notifications, session
        /// settings, sleeps, large objects, sequences, file access, backend control). EF never emits them;
        /// matched on the command text, which never contains parameter VALUES.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex SideEffectFunctions = new(
            @"\b(pg_advisory\w*|pg_try_advisory\w*|pg_notify|set_config|pg_sleep\w*|lo_\w+|nextval|setval|"
            + @"pg_terminate_backend|pg_cancel_backend|pg_reload_conf|pg_read_\w+|pg_ls_\w+|pg_stat_file|"
            + @"dblink\w*|txid_current|pg_current_xact_id|pg_switch_wal|pg_logical_emit_message)\s*\(",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public static bool IsSelect(string sql)
        {
            if (SideEffectFunctions.IsMatch(sql)) return false;
            var s = sql.AsSpan().TrimStart();
            // Skip leading SQL comments EF may emit (query tags).
            while (true)
            {
                if (s.StartsWith("--"))
                {
                    var nl = s.IndexOf('\n');
                    s = nl < 0 ? ReadOnlySpan<char>.Empty : s[(nl + 1)..].TrimStart();
                }
                else if (s.StartsWith("/*"))
                {
                    var end = s.IndexOf("*/");
                    s = end < 0 ? ReadOnlySpan<char>.Empty : s[(end + 2)..].TrimStart();
                }
                else break;
            }
            if (s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("(SELECT", StringComparison.OrdinalIgnoreCase)) return !HasSecondStatement(s);
            if (!s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)) return false;
            // A data-modifying CTE (WITH x AS (DELETE …)) is still a write.
            var text = s.ToString().ToUpperInvariant();
            return !(text.Contains("INSERT ") || text.Contains("UPDATE ") || text.Contains("DELETE ")
                     || text.Contains("MERGE ") || text.Contains("TRUNCATE ")) && !HasSecondStatement(s);
        }

        /// <summary>
        /// "SELECT 1; DELETE …" in one command is not a SELECT. EF never batches reads this way, so a
        /// statement separator followed by anything but whitespace is refused outright.
        /// </summary>
        private static bool HasSecondStatement(ReadOnlySpan<char> s)
        {
            var i = s.IndexOf(';');
            return i >= 0 && !s[(i + 1)..].Trim().IsEmpty;
        }

        private static void Check(DbCommand command)
        {
            if (!IsSelect(command.CommandText)) throw new InvalidOperationException(Message);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Check(command); return result; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        { Check(command); return result; }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
            throw new InvalidOperationException(Message);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Message);
    }
}
