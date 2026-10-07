using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «KETISH SABABLARI» (Retention / Loss) — o'qituvchilar kesimida kim, qachon va NEGA ketdi.
/// Batafsil: <c>.claude/rules/retention-loss.md</c>.
///
/// <para>Hisob-kitob <see cref="Compute"/> da — sof funksiya (<c>RetentionLossReportTests</c>).
/// <see cref="BuildAsync"/> faqat ma'lumot yuklaydi. Qoidalar QAYTA YOZILMAGAN:</para>
/// <list type="bullet">
/// <item>"ketdi" — <see cref="DashboardSummary.LeftEvents"/> (bosh sahifadagi «Ketgan o'quvchilar»
///   bilan AYNAN bir xil: kurs ichidagi guruh almashtirish va sertifikat bilan tugatish KETISH EMAS);</item>
/// <item>"aktiv edi" (maxraj) — <see cref="MembershipLifecycle.BillableInMonth(StudentGroup,string)"/>
///   (yopilgan davrlar va chiqish chegarasi bilan);</item>
/// <item>o'sha paytdagi o'qituvchi — <see cref="GroupTeacherHistory.TeacherAtMonth"/>, tarix bo'lmasa
///   guruhning hozirgi o'qituvchisi;</item>
/// <item>sabab — <see cref="MembershipReasons.Resolve"/> (tuzilgan yozuv → audit matni → arxiv sababi);</item>
/// <item>"nazoratdan tashqari" — <see cref="ActionReason.OutOfControl"/> (KPI bilan bir xil manba).</item>
/// </list>
/// <para>⚠️ <c>YearFreeze</c> bu yerda TEKSHIRILMAYDI (<c>year-freeze.md</c> §1).</para>
/// </summary>
public static class RetentionLossReport
{
    /// <summary>Standart davr (oy) va eng katta davr.</summary>
    public const int DefaultMonths = 6;
    public const int MaxMonths = 24;

    public const string KindAll = "all";
    public const string KindActive = "active";
    public const string KindTrial = "trial";

    /// <summary>O'qituvchisi yo'q guruh(lar)ning yorlig'i.</summary>
    public const string NoTeacherLabel = "Biriktirilmagan";

    /// <summary>So'rov (normallashtirilgan — <see cref="Normalize"/>).</summary>
    public readonly record struct Query(string From, string To, string? TeacherId, string? CourseId, string Kind);

    public readonly record struct GroupRow(string Id, string Name, string CourseId, string TeacherId);

    public readonly record struct StudentRow(string Id, string FullName, bool IsArchived, string? ArchivedAt, string? ArchiveReason);

    /// <summary>Hisob uchun barcha ma'lumot (bazadan yuklangan yoki testda qo'lda yasalgan).</summary>
    public sealed class Data
    {
        public IReadOnlyList<StudentGroup> Memberships { get; init; } = [];
        public IReadOnlyList<GroupRow> Groups { get; init; } = [];
        public IReadOnlyDictionary<string, List<GroupTeacherAssignment>> TeacherHistory { get; init; } =
            new Dictionary<string, List<GroupTeacherAssignment>>();
        public IReadOnlyList<StudentRow> Students { get; init; } = [];
        /// <summary>O'qituvchi id → ism (arxivdagilar ham — tarix ismsiz qolmasin).</summary>
        public IReadOnlyDictionary<string, string> TeacherNames { get; init; } = new Dictionary<string, string>();
        /// <summary>Filtr ro'yxatiga chiqadigan (arxivlanmagan) o'qituvchilar.</summary>
        public IReadOnlyCollection<string> ActiveTeacherIds { get; init; } = [];
        public IReadOnlyDictionary<string, string> CourseNames { get; init; } = new Dictionary<string, string>();
        public IReadOnlyList<ActionReason> Reasons { get; init; } = [];
        public IReadOnlyList<MembershipReasonLog> ReasonLogs { get; init; } = [];
        /// <summary>"Membership" audit yozuvlari: kalit — <c>EntityId</c> ("{guruh}:{o'quvchi}").</summary>
        public IReadOnlyDictionary<string, List<MembershipReasons.AuditRow>> Audits { get; init; } =
            new Dictionary<string, List<MembershipReasons.AuditRow>>();
    }

    /* =========================================================================================
     *  SO'ROV
     * ====================================================================================== */

    /// <summary>
    /// So'rovni tekshiradi va to'ldiradi: oy berilmasa — oxirgi <see cref="DefaultMonths"/> oy
    /// (joriy oy bilan); <c>from &gt; to</c> bo'lsa almashtiriladi; davr <see cref="MaxMonths"/> dan
    /// oshsa boshi qirqiladi. Noto'g'ri oy formati — <c>Error</c> (400 bilan qaytariladi).
    /// </summary>
    public static (Query Query, string? Error) Normalize(
        string? from, string? to, string? teacherId, string? courseId, string? kind, DateOnly today)
    {
        var curMonth = new DateOnly(today.Year, today.Month, 1);
        DateOnly? Parse(string? m) =>
            m is { Length: 7 } && DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", out var d) ? d : null;

        var toD = string.IsNullOrWhiteSpace(to) ? curMonth : Parse(to.Trim());
        var fromD = string.IsNullOrWhiteSpace(from) ? toD?.AddMonths(-(DefaultMonths - 1)) : Parse(from.Trim());
        if (toD is null || fromD is null)
            return (default, "Oy noto'g'ri (yyyy-MM)");
        if (fromD > toD) (fromD, toD) = (toD, fromD);
        if (fromD.Value.AddMonths(MaxMonths - 1) < toD.Value)
            fromD = toD.Value.AddMonths(-(MaxMonths - 1));

        var k = kind?.Trim().ToLowerInvariant() switch
        {
            KindActive => KindActive,
            KindTrial => KindTrial,
            _ => KindAll,
        };
        return (new Query(fromD.Value.ToString("yyyy-MM"), toD.Value.ToString("yyyy-MM"),
            string.IsNullOrWhiteSpace(teacherId) ? null : teacherId.Trim(),
            string.IsNullOrWhiteSpace(courseId) ? null : courseId.Trim(), k), null);
    }

    /// <summary>"yyyy-MM" oylar ro'yxati, ikkala chegara ham kiradi.</summary>
    public static List<string> MonthsBetween(string from, string to)
    {
        var result = new List<string>();
        if (!DateOnly.TryParseExact(from + "-01", "yyyy-MM-dd", out var a)
            || !DateOnly.TryParseExact(to + "-01", "yyyy-MM-dd", out var b)) return result;
        for (var m = a; m <= b && result.Count < MaxMonths * 2; m = m.AddMonths(1))
            result.Add(m.ToString("yyyy-MM"));
        return result;
    }

    /* =========================================================================================
     *  HISOB — sof funksiya
     * ====================================================================================== */

    /// <summary>Bitta ketish — hisobning ichki ko'rinishi.</summary>
    private sealed record Departure(
        DashboardSummary.LeftEvent Event, GroupRow? Group, string TeacherId,
        MembershipReasons.Resolved Reason, bool OutOfControl);

    public static RetentionLossReportDto Compute(Data data, Query q)
    {
        var months = MonthsBetween(q.From, q.To);
        var monthSet = months.ToHashSet(StringComparer.Ordinal);
        var groups = data.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var courseByGroup = data.Groups.ToDictionary(g => g.Id, g => g.CourseId, StringComparer.Ordinal);
        var students = data.Students.ToDictionary(s => s.Id, StringComparer.Ordinal);
        // Bazada yo'q o'quvchi (yetim a'zolik) sanalmaydi — bosh sahifa kartochkasi bilan bir xil.
        var memberships = data.Memberships.Where(m => students.ContainsKey(m.StudentId)).ToList();

        string TeacherAt(string groupId, string month)
        {
            var hist = data.TeacherHistory.GetValueOrDefault(groupId);
            return GroupTeacherHistory.TeacherAtMonth(hist, month)
                   ?? (groups.TryGetValue(groupId, out var g) ? g.TeacherId : "")
                   ?? "";
        }
        bool CourseOk(string groupId) =>
            q.CourseId is null || (groups.TryGetValue(groupId, out var g) && g.CourseId == q.CourseId);
        bool TeacherOk(string teacherId) => q.TeacherId is null || teacherId == q.TeacherId;

        var logsByPair = data.ReasonLogs
            .GroupBy(l => Pair(l.GroupId, l.StudentId), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var oocById = data.Reasons.ToDictionary(r => r.Id, r => r.OutOfControl, StringComparer.Ordinal);
        var oocLabels = data.Reasons.Where(r => r.OutOfControl)
            .Select(r => r.Label.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ---------- 1. Ketishlar (davr + o'qituvchi + kurs filtri) ----------
        var all = new List<Departure>();
        foreach (var e in DashboardSummary.LeftEvents(memberships, courseByGroup,
                     end => end.Length >= 7 && monthSet.Contains(end[..7])))
        {
            if (!CourseOk(e.GroupId)) continue;
            var teacherId = TeacherAt(e.GroupId, e.Date[..7]);
            if (!TeacherOk(teacherId)) continue;

            var st = students[e.StudentId];
            var reason = MembershipReasons.Resolve(
                e.Date, e.Closing?.FrozenAt,
                logsByPair.GetValueOrDefault(Pair(e.GroupId, e.StudentId)) ?? [],
                data.Audits.GetValueOrDefault(Pair(e.GroupId, e.StudentId)) ?? [],
                new MembershipReasons.ArchiveInfo(st.IsArchived, st.ArchivedAt, st.ArchiveReason));
            var ooc = reason.ReasonId is not null && oocById.TryGetValue(reason.ReasonId, out var flag)
                ? flag
                : reason.Label.Length > 0 && oocLabels.Contains(reason.Label);
            all.Add(new Departure(e, groups.TryGetValue(e.GroupId, out var gr) ? gr : null, teacherId, reason, ooc));
        }

        bool KindOk(Departure d) => q.Kind switch
        {
            KindActive => d.Event.EverActive,
            KindTrial => !d.Event.EverActive,
            _ => true,
        };
        var shown = all.Where(KindOk).ToList();

        // ---------- 2. Maxraj — davr ichida AKTIV (pullik) bo'lgan (o'quvchi, kurs) juftliklari ----------
        var baseByTeacher = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void AddBase(string teacherId, string key)
        {
            if (!baseByTeacher.TryGetValue(teacherId, out var set))
                baseByTeacher[teacherId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(key);
        }
        foreach (var m in memberships)
        {
            if (!CourseOk(m.GroupId)) continue;
            var key = m.StudentId + "|" + DashboardSummary.CourseKey(m.GroupId, courseByGroup);
            foreach (var month in months)
            {
                if (!MembershipLifecycle.BillableInMonth(m, month)) continue;
                var t = TeacherAt(m.GroupId, month);
                if (TeacherOk(t)) AddBase(t, key);
            }
        }
        // Aktivdan ketgan har bir juftlik maxrajda bo'lishi SHART (foiz 100 dan oshmasin): masalan
        // muzlatilgan holda davrdan oldin to'xtagan, davr ichida chiqarilgan o'quvchi.
        foreach (var d in all.Where(d => d.Event.EverActive))
            AddBase(d.TeacherId, d.Event.StudentId + "|" + d.Event.CourseKey);

        var activeBase = baseByTeacher.Values
            .SelectMany(s => s).ToHashSet(StringComparer.Ordinal).Count;
        var leftActive = all.Count(d => d.Event.EverActive);
        var leftTrial = all.Count - leftActive;
        double? lossRate = activeBase == 0 ? null : Pct(leftActive, activeBase);

        var totals = new RetentionLossTotalsDto(
            Left: shown.Count,
            LeftActive: leftActive,
            LeftTrial: leftTrial,
            Students: shown.Select(d => d.Event.StudentId).Distinct(StringComparer.Ordinal).Count(),
            OutOfControl: shown.Count(d => d.OutOfControl),
            NoReason: shown.Count(d => d.Reason.Label.Length == 0),
            ActiveBase: activeBase,
            LossRate: lossRate,
            Retention: lossRate is null ? null : Math.Round(100 - lossRate.Value, 1));

        // ---------- 3. Sabablar ----------
        var reasons = shown
            .GroupBy(d => d.Reason.Label.Length == 0 ? MembershipReasons.NoReasonLabel : d.Reason.Label,
                StringComparer.OrdinalIgnoreCase)
            .Select(g => new RetentionLossReasonDto(g.First().Reason.Label.Length == 0
                    ? MembershipReasons.NoReasonLabel : g.First().Reason.Label,
                g.Count(), g.Any(d => d.OutOfControl)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---------- 4. O'qituvchilar ----------
        string TeacherName(string id) => id.Length == 0
            ? NoTeacherLabel
            : data.TeacherNames.GetValueOrDefault(id) ?? "O'qituvchi o'chirilgan";
        var teacherIds = all.Select(d => d.TeacherId).Concat(baseByTeacher.Keys).Distinct(StringComparer.Ordinal);
        var teachers = teacherIds
            .Select(id =>
            {
                var mine = all.Where(d => d.TeacherId == id).ToList();
                var act = mine.Count(d => d.Event.EverActive);
                var b = baseByTeacher.GetValueOrDefault(id)?.Count ?? 0;
                return new RetentionLossTeacherDto(id, TeacherName(id), mine.Count(KindOk), act,
                    mine.Count - act, b, b == 0 ? null : Pct(act, b));
            })
            .OrderByDescending(t => t.Left).ThenByDescending(t => t.LossRate ?? 0)
            .ThenBy(t => t.TeacherName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---------- 5. Oylik dinamika ----------
        var monthRows = months.Select(m => new RetentionLossMonthDto(m,
            all.Count(d => d.Event.EverActive && d.Event.Date[..7] == m),
            all.Count(d => !d.Event.EverActive && d.Event.Date[..7] == m))).ToList();

        // ---------- 6. Jadval ----------
        var studying = memberships
            .Where(m => m.IsActive && (m.Status is "active" or "trial"))
            .Select(m => m.StudentId).ToHashSet(StringComparer.Ordinal);
        var rows = shown
            .Select(d =>
            {
                var st = students[d.Event.StudentId];
                var courseId = d.Group?.CourseId ?? "";
                return new RetentionLossRowDto(
                    st.Id, st.FullName, d.Event.GroupId, d.Group?.Name ?? "",
                    courseId, data.CourseNames.GetValueOrDefault(courseId, ""),
                    d.TeacherId, TeacherName(d.TeacherId), d.Event.Date, d.Event.EverActive,
                    d.Reason.ReasonId, d.Reason.Label, d.OutOfControl, d.Reason.Source,
                    !st.IsArchived && studying.Contains(st.Id));
            })
            .OrderByDescending(r => r.Date, StringComparer.Ordinal)
            .ThenBy(r => r.StudentName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---------- 7. Filtr variantlari ----------
        var teacherOptions = data.ActiveTeacherIds
            .Concat(all.Select(d => d.TeacherId).Where(id => id.Length > 0))
            .Distinct(StringComparer.Ordinal)
            .Where(id => data.TeacherNames.ContainsKey(id))
            .Select(id => new RetentionLossOptionDto(id, data.TeacherNames[id]))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var courseOptions = data.CourseNames
            .Select(kv => new RetentionLossOptionDto(kv.Key, kv.Value))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RetentionLossReportDto(q.From, q.To, q.Kind, q.TeacherId, q.CourseId,
            totals, reasons, teachers, monthRows, rows, teacherOptions, courseOptions);
    }

    private static string Pair(string groupId, string studentId) => groupId + ":" + studentId;

    private static double Pct(int part, int whole) => Math.Round(part * 100.0 / whole, 1);

    /* =========================================================================================
     *  YUKLASH
     * ====================================================================================== */

    /// <summary>Bazadan yuklab, <see cref="Compute"/> ni chaqiradi. Barcha so'rovlar faqat-o'qish.</summary>
    public static async Task<RetentionLossReportDto> BuildAsync(IAppDbContext db, Query q, CancellationToken ct = default)
    {
        // Arxivlangan guruhlar ham YUKLANADI — ular orqali o'tgan o'quvchilarning ketishi yo'qolmasin
        // (`course-analytics.md` §5).
        var groups = (await db.Classes.AsNoTracking()
                .Select(g => new { g.Id, g.Name, g.CourseId, g.TeacherId }).ToListAsync(ct))
            .Select(g => new GroupRow(g.Id, g.Name ?? "", g.CourseId ?? "", g.TeacherId ?? "")).ToList();
        var memberships = await db.StudentGroups.AsNoTracking().ToListAsync(ct);
        var students = (await db.Students.AsNoTracking()
                .Select(s => new { s.Id, s.FullName, s.IsArchived, s.ArchivedAt, s.ArchiveReason }).ToListAsync(ct))
            .Select(s => new StudentRow(s.Id, s.FullName ?? "", s.IsArchived, s.ArchivedAt, s.ArchiveReason)).ToList();
        var history = await GroupTeacherHistory.LoadAsync(db, groups.Select(g => g.Id), ct);
        var teachers = await db.Teachers.AsNoTracking()
            .Select(t => new { t.Id, t.FullName, t.IsArchived }).ToListAsync(ct);
        var courses = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var reasons = await db.ActionReasons.AsNoTracking().ToListAsync(ct);

        // Sabab manbalari faqat DAVRDA ketgan juftliklar uchun yuklanadi (audit jadvali katta).
        var months = MonthsBetween(q.From, q.To).ToHashSet(StringComparer.Ordinal);
        var known = students.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var events = DashboardSummary.LeftEvents(
                memberships.Where(m => known.Contains(m.StudentId)),
                groups.ToDictionary(g => g.Id, g => g.CourseId, StringComparer.Ordinal),
                end => end.Length >= 7 && months.Contains(end[..7]))
            .ToList();
        var studentIds = events.Select(e => e.StudentId).Distinct().ToList();
        var pairKeys = events.Select(e => Pair(e.GroupId, e.StudentId)).Distinct().ToList();

        var logs = new List<MembershipReasonLog>();
        if (studentIds.Count > 0)
            logs = await db.MembershipReasonLogs.AsNoTracking()
                .Where(l => studentIds.Contains(l.StudentId)).ToListAsync(ct);
        var audits = new Dictionary<string, List<MembershipReasons.AuditRow>>(StringComparer.Ordinal);
        if (pairKeys.Count > 0)
        {
            var raw = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "Membership" && pairKeys.Contains(a.EntityId))
                .Select(a => new { a.EntityId, a.Timestamp, a.Summary })
                .ToListAsync(ct);
            foreach (var g in raw.GroupBy(a => a.EntityId, StringComparer.Ordinal))
                audits[g.Key] = g.Select(a => new MembershipReasons.AuditRow(a.Timestamp ?? "", a.Summary ?? "")).ToList();
        }

        var data = new Data
        {
            Memberships = memberships,
            Groups = groups,
            TeacherHistory = history,
            Students = students,
            TeacherNames = teachers.ToDictionary(t => t.Id, t => t.FullName ?? "", StringComparer.Ordinal),
            ActiveTeacherIds = teachers.Where(t => !t.IsArchived).Select(t => t.Id).ToList(),
            CourseNames = courses.ToDictionary(c => c.Id, c => c.Name ?? "", StringComparer.Ordinal),
            Reasons = reasons,
            ReasonLogs = logs,
            Audits = audits,
        };
        return Compute(data, q);
    }
}
