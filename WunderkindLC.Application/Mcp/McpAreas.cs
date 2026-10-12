namespace WunderkindLC.Application.Mcp;

/// <summary>
/// What a tool reads, expressed the way the web panel grants it: permission keys from the
/// <c>adminPermissions</c> catalog (WunderkindLC.Client/src/config/constants.ts). Reading any
/// one of the keys is enough. <see cref="SuperAdminOnly"/> areas ignore the keys — they follow
/// the hard role checks of the panel (e.g. the contact-queue report).
/// </summary>
public sealed record McpArea(string Title, string[] Keys, bool SuperAdminOnly = false);

/// <summary>
/// The area of every tool. Keys MUST exist in the catalog (a test checks it) — a key that is
/// renamed there and not here would silently deny staff, never widen access.
///
/// <para>
/// ⚠️ Sensitive areas use PAGE keys (<c>students.list</c>, <c>finance.main</c>), not section keys:
/// a section key is also satisfied by any ONE of its pages (upward read inheritance,
/// .claude/rules/permissions.md §2), so <c>students</c> would open pupil data to a turnstile operator.
/// </para>
/// </summary>
public static partial class McpAreas
{
    /// <summary>Anyone who may connect at all (counts that carry no personal or money data).</summary>
    public static readonly McpArea Any = new("Umumiy ko'rsatkichlar", []);

    public static readonly McpArea Students = new("O'quvchilar ro'yxati va profili", ["students.list"]);
    public static readonly McpArea StudentAttendance = new("O'quvchilar davomati", ["students.attendance"]);
    public static readonly McpArea Contacts = new("Bog'lanish kerak", ["contacts"]);
    public static readonly McpArea ContactReport = new("Bog'lanish hisoboti", [], SuperAdminOnly: true);

    public static readonly McpArea Groups = new("Guruhlar va jurnal", ["classes.list"]);
    public static readonly McpArea Rooms = new("Xonalar", ["classes.rooms", "classes.list"]);
    public static readonly McpArea TestResults = new("Testlar natijalari", ["classes.testResults"]);
    public static readonly McpArea NotAttended = new("Davomat qilinmagan guruhlar", ["classes.notAttended"]);

    // Rating — tests + ball (CRM Phase 4). The panel's gates (RatingController): the report needs `classes.rating`; a
    // group's table is also on the group page (`classes.list`); a student's block is also in the profile (`students.list`).
    public static readonly McpArea Rating = new("Reyting", ["classes.rating"]);
    public static readonly McpArea RatingGroup = new("Guruh reytingi", ["classes.rating", "classes.list"]);
    public static readonly McpArea StudentRating = new("O'quvchi reytingi", ["students.list", "classes.rating"]);

    public static readonly McpArea Courses = new("Kurslar", ["schedule.courses"]);
    public static readonly McpArea Timetable = new("Dars jadvali", ["schedule.timetable"]);
    public static readonly McpArea CourseAnalytics = new("Kurslar analitikasi", ["schedule.analytics"]);

    public static readonly McpArea Teachers = new("O'qituvchilar ro'yxati va profili", ["teachers.list"]);
    public static readonly McpArea TeacherAttendance = new("O'qituvchilar davomati", ["teachers.attendance"]);
    public static readonly McpArea Substitutions = new("O'rinbosarlar", ["teachers.substitutions"]);
    public static readonly McpArea Retention = new("Ketish sabablari", ["teachers.retention"]);
    public static readonly McpArea Staff = new("Xodimlar", ["staff"]);

    public static readonly McpArea Finance = new("Moliya", ["finance.main"]);
    public static readonly McpArea Discounts = new("Chegirmalar hisoboti", ["finance.discounts"]);
    public static readonly McpArea Kassa = new("Kassa", ["kassa", "finance.main"]);

    public static readonly McpArea Leads = new("Lidlar", ["leads.list"]);
    public static readonly McpArea LeadStats = new("CRM statistika", ["leads.stats"]);

    public static readonly McpArea Messages = new("Xabarlar", ["messages.broadcast"]);
    public static readonly McpArea Tasks = new("Topshiriqlar", ["tasks.board", "tasks.dashboard"]);
    public static readonly McpArea Kpi = new("KPI", ["kpi.today", "kpi.month"]);
    public static readonly McpArea Audit = new("O'zgarishlar tarixi", ["audit"]);
    public static readonly McpArea Contracts = new("Shartnomalar", ["contracts"]);
    public static readonly McpArea Books = new("Kitoblar sotuvi", ["books"]);
    public static readonly McpArea Feedback = new("Taklif va shikoyatlar", ["feedback"]);
    public static readonly McpArea Vacancies = new("Vakansiyalar va arizalar", ["vacancies.list"]);
    public static readonly McpArea Onboarding = new("Moslashuv", ["vacancies.onboarding"]);

    /// <summary>Every area — the catalog test enumerates this.</summary>
    public static IEnumerable<McpArea> All =>
        typeof(McpAreas).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(McpArea))
            .Select(f => (McpArea)f.GetValue(null)!);
}
