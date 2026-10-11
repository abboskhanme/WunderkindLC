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
///   <item><see cref="RestrictedColumns"/> — those entities get a COLUMN-level grant instead of the
///     whole table, so a secret column cannot be read even by a hand-written query;</item>
///   <item><see cref="Forbidden"/> — never granted, whatever a tool declares (a test fails first).</item>
/// </list>
/// Table and column names are resolved from the EF model at startup (Infrastructure
/// <c>McpReadOnlyRole</c>), so renames follow automatically.
/// </summary>
public static class McpReadOnlyGrants
{
    /// <summary>Property names that may be read; everything else on the entity is closed.</summary>
    public static readonly IReadOnlyDictionary<Type, string[]> RestrictedColumns = new Dictionary<Type, string[]>
    {
        // No Email (= login), PasswordHash, InitialPassword, Permissions, lockout counters.
        [typeof(AppUser)] =
        [
            nameof(AppUser.Id), nameof(AppUser.FullName), nameof(AppUser.Role), nameof(AppUser.Phone),
            nameof(AppUser.Position), nameof(AppUser.RoleTemplateId), nameof(AppUser.AvatarUrl),
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

    /// <summary>Sessions, one-time codes, biometrics, device and integration secrets, the MCP tables themselves.</summary>
    public static readonly Type[] Forbidden =
    [
        typeof(RefreshToken), typeof(LoginOtpCode), typeof(TrustedDevice), typeof(FaceChallenge),
        typeof(LoginFaceCheck), typeof(StudentFaceProfile), typeof(DeviceToken), typeof(Camera),
        typeof(CtiAgent), typeof(IgAccount), typeof(IgOAuthState), typeof(IgAdAccount), typeof(BotUser),
        typeof(CareerBotUser), typeof(TestBotSession), typeof(BookBotSession), typeof(ArchivedRecord),
        typeof(McpClient), typeof(McpGrant), typeof(McpAuthCode), typeof(McpToken), typeof(McpAuditEntry),
    ];

    /// <summary>Entities granted as whole tables.</summary>
    public static IReadOnlyCollection<Type> Tables =>
        McpToolRegistry.ReadEntities.Where(t => !RestrictedColumns.ContainsKey(t)).ToArray();

    /// <summary>Entities granted column by column (only those a tool actually reads).</summary>
    public static IReadOnlyDictionary<Type, string[]> Columns =>
        RestrictedColumns.Where(kv => McpToolRegistry.ReadEntities.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
}
