using System.Reflection;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp;

/// <summary>
/// Tool types — the ONLY place tools are registered (the server and the tests enumerate this).
///
/// <para>
/// Every tool class declares <c>public static readonly Type[] Reads</c>: the entity types its
/// queries (and the query services it reuses) SELECT from. The union is the allow-list the
/// read-only database role is granted (<see cref="McpReadOnlyGrants"/>) — a table no tool lists
/// stays closed to the AI connection, including every table a future migration adds.
/// </para>
/// </summary>
public static class McpToolRegistry
{
    public static readonly Type[] ToolTypes =
    [
        typeof(Tools.OverviewTools),
        typeof(Tools.StudentTools),
        typeof(Tools.GroupTools),
        typeof(Tools.AttendanceTools),
        typeof(Tools.LeadTools),
        typeof(Tools.FinanceTools),
        typeof(Tools.TeacherTools),
        typeof(Tools.ScheduleTools),
        typeof(Tools.TestTools),
        typeof(Tools.OperationsTools),
        typeof(Tools.AnalyticsTools),
        typeof(Tools.BackOfficeTools),
        // CRM upgrade (2026-10, docs/SPEC-crm-upgrade.md U2m–U6m).
        typeof(Tools.MessagingTools),
    ];

    /// <summary>Entity types read by at least one tool.</summary>
    public static IReadOnlyCollection<Type> ReadEntities { get; } = ToolTypes
        .SelectMany(t => (Type[])(t.GetField("Reads", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException($"{t.Name}: `public static readonly Type[] Reads` is missing")))
        .Distinct()
        .ToArray();
}

/// <summary>
/// What the read-only PostgreSQL role (<c>wlc_ro</c>) may SELECT. Deny-by-default:
/// <list type="bullet">
///   <item>tables — only entities listed by the tools (<see cref="McpToolRegistry.ReadEntities"/>);</item>
///   <item>columns — a column whose NAME looks sensitive (<see cref="IsSensitiveColumn"/>: file links, chat and
///     device ids, secrets, locations, test keys…) is NEVER granted, on any table, including columns a future
///     migration adds: a table with such a column gets a COLUMN-level grant of everything else
///     (<see cref="GrantedProperties"/>), computed from the EF model at startup;</item>
///   <item><see cref="RestrictedColumns"/> — explicit allow-lists that stay authoritative for their entities
///     (only what a tool needs, not "everything but the sensitive");</item>
///   <item><see cref="Forbidden"/> — never granted, whatever a tool declares (a test fails first).</item>
/// </list>
/// Table and column names are resolved from the EF model at startup (Infrastructure
/// <c>McpReadOnlyRole</c>), so renames follow automatically. The MCP read-only DbContext's model is reduced
/// by the same rule (Infrastructure <c>McpReadOnlyModel</c>), so a full-entity load never selects a closed column.
/// </summary>
public static class McpReadOnlyGrants
{
    /// <summary>Property names that may be read; everything else on the entity is closed.</summary>
    public static readonly IReadOnlyDictionary<Type, string[]> RestrictedColumns = new Dictionary<Type, string[]>
    {
        // No Email (= login), PasswordHash, InitialPassword, Permissions, lockout counters, avatar file link.
        [typeof(AppUser)] =
        [
            nameof(AppUser.Id), nameof(AppUser.FullName), nameof(AppUser.Role), nameof(AppUser.Phone),
            nameof(AppUser.Position), nameof(AppUser.RoleTemplateId),
        ],
        // Change history: never the before/after snapshots (they can hold old values of any field) —
        // tools return only the human-readable summary.
        [typeof(AuditLog)] =
        [
            nameof(AuditLog.Id), nameof(AuditLog.EntityType), nameof(AuditLog.EntityId), nameof(AuditLog.Action),
            nameof(AuditLog.Timestamp), nameof(AuditLog.ActorId), nameof(AuditLog.ActorName), nameof(AuditLog.Summary),
            nameof(AuditLog.StudentId), nameof(AuditLog.TeacherId),
        ],
        // Settings row: integration secrets live here too (FCM keys, bot ids, chat ids…). Only the journal
        // policy and the salary base month — what JournalPolicy.GetAsync / SalaryLedger read.
        [typeof(CenterMeta)] =
        [
            nameof(CenterMeta.Id),
            nameof(CenterMeta.JournalEditMode), nameof(CenterMeta.JournalRetroDays), nameof(CenterMeta.JournalConductedOnly),
            nameof(CenterMeta.JournalApplyToAdmins), nameof(CenterMeta.SalaryRequireJournal), nameof(CenterMeta.SalaryGraceDays),
            nameof(CenterMeta.JournalHideUnpaidPrevMonth), nameof(CenterMeta.JournalHideUnpaidAfterDay),
            nameof(CenterMeta.JournalUnpaidCutoffDay), nameof(CenterMeta.SalaryChargedBaseFrom),
        ],
    };

    /// <summary>Sessions, one-time codes, biometrics, device and integration secrets, the MCP tables themselves.
    /// Legacy tables of removed modules (DeviceToken, CtiAgent, TestBotSession) stay listed while they exist.</summary>
#pragma warning disable CS0618 // legacy entities (removed 2026-10), tables kept until a confirmed drop
    public static readonly Type[] Forbidden =
    [
        typeof(RefreshToken), typeof(LoginOtpCode), typeof(TrustedDevice), typeof(FaceChallenge),
        typeof(LoginFaceCheck), typeof(StudentFaceProfile), typeof(DeviceToken), typeof(Camera),
        typeof(CtiAgent), typeof(IgAccount), typeof(IgOAuthState), typeof(IgAdAccount), typeof(BotUser),
        typeof(CareerBotUser), typeof(TestBotSession), typeof(BookBotSession), typeof(ArchivedRecord),
        typeof(McpClient), typeof(McpGrant), typeof(McpAuthCode), typeof(McpToken), typeof(McpAuditEntry),
        // Student passwords (credential vault, CRM phase 6) — never readable by the AI read-only role.
        typeof(StudentCredential),
    ];
#pragma warning restore CS0618

    /// <summary>Name endings of sensitive columns: file links (<c>PhotoUrl</c>, <c>CvUrl</c>…) and Telegram file ids.</summary>
    private static readonly string[] SensitiveSuffixes = ["Url", "FileId"];

    /// <summary>Name parts of sensitive columns: chat/device ids, credentials, documents, locations, test keys.</summary>
    private static readonly string[] SensitiveParts =
    [
        "ChatId", "Password", "Token", "Secret", "Hash", "ApiKey", "Passport", "AnswerKey", "DeviceUserId",
        "TgUsername", "Latitude", "Longitude", "LocationAddress", "LocationUpdatedAt",
    ];

    /// <summary>Whole names of sensitive columns (test access codes, submitted answers, logins, permission lists).</summary>
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Answers", "Permissions", "Email", "InitialPassword",
    };

    /// <summary>
    /// The sensitive-column NAME rule (case-insensitive — when in doubt it closes). A column it matches is never
    /// granted to the read-only role, on any table, now or after a future migration. Pure.
    /// </summary>
    public static bool IsSensitiveColumn(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var suffix in SensitiveSuffixes)
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var part in SensitiveParts)
            if (name.Contains(part, StringComparison.OrdinalIgnoreCase)) return true;
        return SensitiveNames.Contains(name);
    }

    /// <summary>
    /// What the role may read of one entity, given its mapped properties (from the EF model):
    /// <c>null</c> = the whole table (no sensitive column); otherwise the property names of a COLUMN-level
    /// grant — the <see cref="RestrictedColumns"/> allow-list for those entities, every non-sensitive column
    /// for the rest. A sensitive name is dropped even from an allow-list. Pure.
    /// </summary>
    public static string[]? GrantedProperties(Type entity, IEnumerable<string> mappedProperties)
    {
        if (RestrictedColumns.TryGetValue(entity, out var allowed))
            return allowed.Where(p => !IsSensitiveColumn(p)).ToArray();
        var mapped = mappedProperties.ToList();
        var open = mapped.Where(p => !IsSensitiveColumn(p)).ToArray();
        return open.Length == mapped.Count ? null : open;
    }
}
