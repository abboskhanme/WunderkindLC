using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ModelContextProtocol;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp;

/// <summary>Thrown when the signed-in user lacks the area permission. Audited as <c>denied</c>.</summary>
public sealed class McpAccessDeniedException(string message) : McpException(message);

/// <summary>Per-call state shared between a tool and the audit filter.</summary>
public sealed class McpCallState
{
    public bool Denied { get; set; }
    public int? RowCount { get; set; }
}

/// <summary>
/// The only user columns MCP tools may read (see <see cref="McpReadOnlyGrants"/>). A class with init
/// properties (not a positional record) on purpose: EF can compose a <c>Where</c> over a member-init
/// projection, so <c>t.Users.Where(u => ids.Contains(u.Id))</c> filters in SQL.
/// </summary>
public sealed class McpUserRow
{
    public string Id { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Role { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Position { get; init; } = "";
}

/// <summary>
/// Everything an MCP tool may touch: the signed-in user, the READ-ONLY database context,
/// permission checks, caps and the output serializer. Tools get nothing else — in
/// particular never the request's read/write context from DI: <see cref="Db"/> is opened by
/// the host on the read-only connection (SELECT-only role, no tracking, writes refused).
/// A reflection test enforces that tool classes depend on this class only.
/// </summary>
public sealed class McpToolContext(
    ClaimsPrincipal user, Func<IAppDbContext> openReadOnly, IMemoryCache cache, McpCallState state)
    : IAsyncDisposable, IDisposable
{
    /// <summary>Claim type of staff permissions — the same one the web panel uses.</summary>
    public const string PermClaimType = "perm";

    /// <summary>Largest page any list tool returns.</summary>
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 50;

    /// <summary>Longest date range a range tool accepts.</summary>
    public const int MaxRangeDays = 366;

    private IAppDbContext? _db;

    /// <summary>Read-only context (SELECT-only role, no tracking, SaveChanges refused). Opened lazily.</summary>
    public IAppDbContext Db => _db ??= openReadOnly();

    public void Dispose() => (_db as IDisposable)?.Dispose();

    public ValueTask DisposeAsync() =>
        _db is IAsyncDisposable d ? d.DisposeAsync() : ValueTask.CompletedTask;

    public ClaimsPrincipal User => user;

    public McpCallState State => state;

    public bool IsSuperAdmin => user.IsInRole(Roles.SuperAdmin);

    public bool IsFullAccess => user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin);

    private IEnumerable<string> Perms => user.Claims.Where(c => c.Type == PermClaimType).Select(c => c.Value);

    /// <summary>
    /// The web panel's read rule for one permission key (<see cref="PermissionRules.HasSection"/>):
    /// admin / superadmin — always; staff — the key, its section, or any action on it.
    /// </summary>
    public bool CanRead(string key) =>
        IsFullAccess || (user.IsInRole(Roles.Staff) && PermissionRules.HasSection(Perms, key));

    /// <summary>May the user read the area (any one of its keys; superadmin-only areas by role)?</summary>
    public bool CanReadArea(McpArea area) =>
        area.SuperAdminOnly ? IsSuperAdmin : IsFullAccess || area.Keys.Length == 0 || area.Keys.Any(CanRead);

    /// <summary>Money (balances, payments, salaries): the "Moliya" page permission.</summary>
    public bool CanReadFinance => CanReadArea(McpAreas.Finance);

    /// <summary>Refuse unless the user may read the area.</summary>
    public void Require(McpArea area)
    {
        if (CanReadArea(area)) return;
        state.Denied = true;
        throw new McpAccessDeniedException(area.SuperAdminOnly
            ? $"Ruxsat yo'q: «{area.Title}» faqat superadmin uchun."
            : $"Ruxsat yo'q: bu ma'lumot «{area.Title}» bo'limiga tegishli, sizning rolingizda u ochilmagan.");
    }

    /// <summary>
    /// Users as the read-only role may see them: no login, no password hash, no permissions.
    /// Tools must go through this projection — a full <see cref="AppUser"/> SELECT is refused
    /// by the database (column-level grant).
    /// </summary>
    public IQueryable<McpUserRow> Users =>
        Db.Users.Select(u => new McpUserRow
        {
            Id = u.Id, FullName = u.FullName, Role = u.Role, Phone = u.Phone, Position = u.Position,
        });

    /// <summary>
    /// Current names of the given users (Id → FullName), one SQL query filtered by id. Use this instead of
    /// loading <see cref="Users"/> whole: <c>Users</c> also holds every student and parent account.
    /// </summary>
    public async Task<Dictionary<string, string>> UserNamesAsync(IEnumerable<string?> ids, CancellationToken ct = default)
    {
        var list = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = await Users.Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.FullName ?? "", StringComparer.Ordinal);
    }

    /// <summary>
    /// Short server-side cache for centre-wide aggregates that are expensive to compute.
    /// The key must not depend on the user — the permission check runs BEFORE the cached value is used.
    /// </summary>
    public async Task<T> CachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var k = "mcp:" + key;
        if (cache.TryGetValue(k, out T? hit) && hit is not null) return hit;
        // Single flight: concurrent misses of one key wait for ONE computation instead of each running it.
        var gate = CacheGates.GetOrAdd(k, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (cache.TryGetValue(k, out hit) && hit is not null) return hit;
            var value = await factory();
            cache.Set(k, value, ttl);
            return value;
        }
        finally
        {
            gate.Release();
            // Late arrivals find the cached value; dropping the gate keeps the dictionary bounded.
            CacheGates.TryRemove(new KeyValuePair<string, SemaphoreSlim>(k, gate));
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> CacheGates =
        new(StringComparer.Ordinal);

    public static int Page(int? page) => Math.Max(1, page ?? 1);

    public static int PageSize(int? size) => Math.Clamp(size ?? DefaultPageSize, 1, MaxPageSize);

    public static DateOnly Today => AppClock.Today;

    /// <summary>Parse <c>YYYY-MM-DD</c>; null/empty → <paramref name="fallback"/>.</summary>
    public static DateOnly Date(string? value, DateOnly fallback, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", out var d)) return d;
        throw new McpException($"«{name}» sanasi YYYY-MM-DD ko'rinishida bo'lsin (masalan 2026-09-01).");
    }

    /// <summary>Parse <c>YYYY-MM</c>; null/empty → the current month.</summary>
    public static string Month(string? value, string name = "month")
    {
        if (string.IsNullOrWhiteSpace(value)) return Today.ToString("yyyy-MM");
        var v = value.Trim();
        if (v.Length == 7 && DateOnly.TryParseExact(v + "-01", "yyyy-MM-dd", out _)) return v;
        throw new McpException($"«{name}» oyi YYYY-MM ko'rinishida bo'lsin (masalan 2026-09).");
    }

    /// <summary>Validated range, at most <paramref name="maxDays"/> days.</summary>
    public static (DateOnly From, DateOnly To) Range(
        string? from, string? to, int defaultDays = 30, int maxDays = MaxRangeDays)
    {
        var t = Date(to, Today, "to");
        var f = Date(from, t.AddDays(-(defaultDays - 1)), "from");
        if (f > t) throw new McpException("«from» sanasi «to» dan keyin bo'lishi mumkin emas.");
        if (t.DayNumber - f.DayNumber + 1 > maxDays)
            throw new McpException($"Sana oralig'i ko'pi bilan {maxDays} kun bo'lishi mumkin.");
        return (f, t);
    }

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    // ---------------------------------------------------------------------
    //  Output
    // ---------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Uzbek letters readable
        WriteIndented = false,
    };

    /// <summary>
    /// Serialize a tool result. <paramref name="rows"/> goes to the audit row count.
    /// Defence in depth, in ONE place for every tool:
    /// <list type="bullet">
    ///   <item>every property whose NAME looks like a credential (password, hash, token, secret, login, …)
    ///     is removed from the output, however it got there;</item>
    ///   <item>every string VALUE goes through <see cref="McpText.CleanOutput"/>: invisible control / format /
    ///     bidi / zero-width / tag characters are stripped and the length is capped at
    ///     <see cref="McpLimits.MaxTextLength"/> — free text from outside parties (lead notes, feedback,
    ///     job applications, contact responses) is the prompt-injection channel.</item>
    /// </list>
    /// </summary>
    public string Json(object payload, int? rows = null)
    {
        state.RowCount = rows;
        return Serialize(payload);
    }

    public static string Serialize(object payload)
    {
        var node = JsonSerializer.SerializeToNode(payload, JsonOptions);
        node = Scrub(node);
        return node?.ToJsonString(JsonOptions) ?? "null";
    }

    /// <summary>Property names that must never leave the server (case-insensitive).</summary>
    public static bool IsSecretName(string name)
    {
        var n = name.ToLowerInvariant().Replace("_", "").Replace("-", "");
        return n is "pin" or "username" or "otp"
            || n.Contains("login") || n.Contains("email")
            || n.Contains("password") || n.Contains("passwd") || n.Contains("hash") || n.Contains("salt")
            || n.Contains("token") || n.Contains("secret") || n.Contains("apikey") || n.Contains("privatekey")
            || n.Contains("credential") || n.Contains("passport") || n.Contains("pinfl") || n.Contains("jshshir")
            || n.Contains("fcm") || n.Contains("vapid") || n.StartsWith("otp") || n.EndsWith("otp")
            || n.Contains("sessionid") || n.Contains("sessionkey") || n.Contains("sessioncookie")
            || n.Contains("rtsp") || n.Contains("chatid") || n.Contains("certificateurl");
    }

    /// <summary>Removes secret-named properties and cleans string values; returns the (possibly replaced) node.</summary>
    private static JsonNode? Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSecretName(key)) { obj.Remove(key); continue; }
                    var child = obj[key];
                    var cleaned = Scrub(child);
                    if (!ReferenceEquals(cleaned, child)) obj[key] = cleaned;
                }
                return obj;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    var cleaned = Scrub(child);
                    if (!ReferenceEquals(cleaned, child)) arr[i] = cleaned;
                }
                return arr;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                var clean = McpText.CleanOutput(text);
                return string.Equals(clean, text, StringComparison.Ordinal) ? value : JsonValue.Create(clean);
            default:
                return node;
        }
    }
}
