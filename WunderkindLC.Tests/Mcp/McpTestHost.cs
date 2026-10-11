using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP vositalarini sinash uchun muhit: ma'lumot oddiy (yoziladigan) kontekst orqali tayyorlanadi
/// (<see cref="Rw"/>), vosita esa AYNI bazaga <b>faqat-o'qish</b> konteksti orqali ulanadi —
/// prod'dagi bilan bir xil qulflar (<see cref="ReadOnlyDatabase.Decorate"/>): SaveChanges rad
/// etiladi, SELECT bo'lmagan har qanday buyruq rad etiladi. Ya'ni yozishga uringan vosita testda
/// YIQILADI — "faqat o'qish" qoidasi grep bilan emas, bajarish bilan isbotlanadi.
///
/// <para>Har bir SQL buyrug'i yozib olinadi (<see cref="Sql"/>): <see cref="AssertReadsOnlyDeclared"/>
/// vosita o'qigan jadvallar uning <c>Reads</c> ro'yxatida borligini tekshiradi — o'sha ro'yxat
/// prod'da <c>wlc_ro</c> roliga beriladigan ruxsatlar ro'yxati.</para>
/// </summary>
public sealed class McpTestHost : IDisposable
{
    /// <summary>
    /// PostgreSQL REJIMI (ixtiyoriy): <c>MCP_TEST_PG</c> = egasi ulanish satri (<c>Database=postgres</c>) va
    /// <c>MCP_TEST_RO_PASSWORD</c> berilsa, HAR host uchun alohida vaqtinchalik baza yaratiladi, faqat-o'qish roli
    /// (<c>wlc_ro</c>) prod'dagidek ruxsat ro'yxati bilan sozlanadi va vositalar AYNAN shu rol orqali ishlaydi —
    /// SQLite ko'rmaydigan xatolar (so'rov tarjimasi, ustun ruxsati) shu yerda chiqadi. Berilmasa — SQLite.
    /// </summary>
    private static readonly string? PgOwner = Environment.GetEnvironmentVariable("MCP_TEST_PG");
    private static readonly string? PgRoPassword = Environment.GetEnvironmentVariable("MCP_TEST_RO_PASSWORD");
    private static readonly SemaphoreSlim RoleLock = new(1, 1);

    static McpTestHost() => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);   // Program.cs bilan bir xil

    public static bool IsPostgres => !string.IsNullOrWhiteSpace(PgOwner) && !string.IsNullOrWhiteSpace(PgRoPassword);

    private readonly TestDb? _sqlite;
    private readonly string? _pgDatabase;
    private readonly string? _pgRoConnection;
    private readonly AppDbContext _rw;
    private readonly SqlCapture _capture = new();
    private readonly List<AppDbContext> _opened = [];
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public McpTestHost()
    {
        if (!IsPostgres)
        {
            _sqlite = TestDb.Sqlite();
            _rw = _sqlite.Context;
            return;
        }

        _pgDatabase = "mcp_t_" + Guid.NewGuid().ToString("N");
        using (var admin = new Npgsql.NpgsqlConnection(PgOwner))
        {
            admin.Open();
            using var cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{_pgDatabase}\"", admin);
            cmd.ExecuteNonQuery();
        }
        var ownerConn = new Npgsql.NpgsqlConnectionStringBuilder(PgOwner) { Database = _pgDatabase }.ConnectionString;
        _rw = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ownerConn).Options);
        _rw.Database.EnsureCreated();
        // The role is cluster-wide: concurrent ALTER ROLE from parallel test classes would race.
        RoleLock.Wait();
        try { McpReadOnlyRole.Ensure(ownerConn, _rw.Model, PgRoPassword!); }
        finally { RoleLock.Release(); }
        _pgRoConnection = McpReadOnlyRole.BuildConnectionString(ownerConn, PgRoPassword!);
        // Prod'dagi startup tekshiruvi AYNAN shu: rol o'qishdan boshqa hech narsa qila olmasligi.
        var problem = McpReadOnlyRole.SelfTest(_pgRoConnection, _rw.Model);
        if (problem is not null) throw new InvalidOperationException("wlc_ro self-test failed: " + problem);
    }

    /// <summary>PostgreSQL rejimida faqat-o'qish rolining ulanish satri (SQLite'da <c>null</c>).</summary>
    public string? PgReadOnlyConnection => _pgRoConnection;

    /// <summary>Ma'lumot tayyorlash uchun oddiy kontekst (vositalar buni KO'RMAYDI).</summary>
    public AppDbContext Rw => _rw;

    /// <summary>Vositalar bajargan barcha SQL buyruqlari.</summary>
    public IReadOnlyList<string> Sql => _capture.Commands;

    /// <summary>Oxirgi chaqiruvning holati (rad etildimi, nechta qator).</summary>
    public McpCallState State { get; private set; } = new();

    /// <summary>Faqat-o'qish konteksti (vositalar ishlatadigan AYNAN shu turdagi kontekst).</summary>
    public AppDbContext OpenReadOnly()
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (_pgRoConnection is not null) builder.UseNpgsql(_pgRoConnection);
        else builder.UseSqlite(Rw.Database.GetDbConnection());
        var options = ReadOnlyDatabase.Decorate(builder, _capture);
        var ctx = new AppDbContext(options);
        _opened.Add(ctx);
        return ctx;
    }

    public McpToolContext Context(ClaimsPrincipal user)
    {
        State = new McpCallState();
        return new McpToolContext(user, OpenReadOnly, _cache, State);
    }

    /// <summary>Vosita sinfini shu foydalanuvchi uchun yaratadi (konstruktor: faqat <see cref="McpToolContext"/>).</summary>
    public T Tool<T>(ClaimsPrincipal user) where T : class =>
        (T)Activator.CreateInstance(typeof(T), Context(user))!;

    // ---------------------------------------------------------------- foydalanuvchilar

    public static ClaimsPrincipal SuperAdmin() => User(Roles.SuperAdmin);

    public static ClaimsPrincipal Admin() => User(Roles.Admin);

    /// <summary>Xodim — faqat berilgan ruxsat kalitlari bilan (masalan <c>"students.list"</c>).</summary>
    public static ClaimsPrincipal Staff(params string[] perms) => User(Roles.Staff, perms);

    public static ClaimsPrincipal Teacher() => User(Roles.Teacher);

    private static ClaimsPrincipal User(string role, params string[] perms)
    {
        var identity = new ClaimsIdentity("test", ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "u-" + role));
        identity.AddClaim(new Claim(ClaimTypes.Name, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        foreach (var p in perms) identity.AddClaim(new Claim(McpToolContext.PermClaimType, p));
        return new ClaimsPrincipal(identity);
    }

    // ---------------------------------------------------------------- tekshiruvlar

    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly Regex TableRef = new(@"(?:FROM|JOIN)\s+""([A-Za-z0-9_]+)""", RegexOptions.Compiled);

    /// <summary>Yozib olingan SQL'da uchragan jadval nomlari.</summary>
    public HashSet<string> TablesRead() =>
        Sql.SelectMany(s => TableRef.Matches(s).Select(m => m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Vosita o'qigan HAR jadval uning <c>Reads</c> ro'yxatida bo'lishi shart — aks holda prod'da
    /// <c>wlc_ro</c> roli o'sha jadvalga ruxsatsiz qoladi va vosita "permission denied" bilan yiqiladi.
    /// </summary>
    public void AssertReadsOnlyDeclared(Type toolType)
    {
        var declared = ((Type[])toolType.GetField("Reads", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!)
            .Select(t => Rw.Model.FindEntityType(t)!.GetTableName()!)
            .ToHashSet(StringComparer.Ordinal);
        var extra = TablesRead().Where(t => !declared.Contains(t)).OrderBy(t => t).ToList();
        Assert.True(extra.Count == 0,
            $"{toolType.Name} `Reads` ro'yxatida YO'Q jadvallarni o'qidi: {string.Join(", ", extra)}. "
            + "Entity turini `Reads` ga qo'shing (faqat-o'qish roliga ruxsat shu ro'yxatdan beriladi).");
        AssertColumnGrants();
    }

    /// <summary>
    /// USTUN darajasidagi ruxsat (<see cref="McpReadOnlyRole.ColumnGrants"/>): maxfiy nomli ustuni bor HAR jadval
    /// (<c>Students</c>, <c>Teachers</c>, <c>TestResults</c> …) va ruxsat ro'yxatli <c>Users</c> / <c>CenterMeta</c> /
    /// <c>AuditLog</c> butunlay emas, faqat ruxsat etilgan ustunlari bilan beriladi. SQLite buni tekshirmaydi,
    /// PostgreSQL esa ruxsatsiz ustunni o'qishni <c>permission denied</c> bilan rad etadi — ya'ni xato faqat
    /// prod'da chiqardi. Shu sabab har so'rovdagi <c>"Jadval" AS "a"</c> taxallusi bo'yicha <c>"a"."Ustun"</c>
    /// lar ruxsat ro'yxatida bo'lishi tekshiriladi.
    /// </summary>
    public void AssertColumnGrants()
    {
        foreach (var grant in McpReadOnlyRole.ColumnGrants(Rw.Model))
        {
            var table = grant.Table;
            var allowed = grant.Columns!.ToHashSet(StringComparer.Ordinal);
            var aliasRx = new Regex("\"" + Regex.Escape(table) + "\"\\s+AS\\s+\"([A-Za-z0-9_]+)\"");
            foreach (var sql in Sql)
                foreach (Match a in aliasRx.Matches(sql))
                {
                    var alias = a.Groups[1].Value;
                    foreach (Match c in Regex.Matches(sql, "\"" + Regex.Escape(alias) + "\"\\.\"([A-Za-z0-9_]+)\""))
                        Assert.True(allowed.Contains(c.Groups[1].Value),
                            $"`{table}.{c.Groups[1].Value}` ustuni faqat-o'qish roliga OCHILMAGAN "
                            + "(McpReadOnlyGrants: maxfiy nomli ustun yoki ruxsat ro'yxatida yo'q) — prod'da `permission denied`. "
                            + "Faqat kerakli ustunlarni Select qiling.");
                }
            // Butun qatorni tanlovchi `SELECT *` ham shu jadvalga mumkin emas.
            Assert.DoesNotContain(Sql, q => q.Contains("SELECT *", StringComparison.Ordinal)
                                             && q.Contains("\"" + table + "\"", StringComparison.Ordinal));
        }
    }

    /// <summary>Parol xeshi va boshqa maxfiy ustunlar hech bir so'rovda TANLANMAGAN.</summary>
    public void AssertNoSecretColumns()
    {
        foreach (var column in new[] { "PasswordHash", "InitialPassword", "TokenHash", "CodeHash" })
            Assert.DoesNotContain(Sql, s => s.Contains("\"" + column + "\"", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var c in _opened) c.Dispose();
        _cache.Dispose();
        if (_sqlite is not null) { _sqlite.Dispose(); return; }

        _rw.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
        using var admin = new Npgsql.NpgsqlConnection(PgOwner);
        admin.Open();
        using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_pgDatabase}\" WITH (FORCE)", admin);
        cmd.ExecuteNonQuery();
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Commands.Add(command.CommandText); return result; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        { Commands.Add(command.CommandText); return result; }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
