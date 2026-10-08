using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'qituvchi portalining bosh sahifasi, dars jadvali va "O'quvchilarim" ro'yxati
/// (<c>GET api/teacher/overview | schedule | students</c>).
///
/// <para><b>Hech narsa YOZMAYDI</b> — faqat mavjud qoidalarni bir joyga yig'adi:
/// <list type="bullet">
/// <item>guruh ko'rinishi — <see cref="TeacherGroupAccess"/> (arxiv/blok guruh yo'q);</item>
/// <item>dars sanalari — <see cref="JournalService.EffectiveLessonDatesInMonth"/> (ko'chirishlar bilan);</item>
/// <item>o'rinbosar kunlari — <see cref="SubstituteTeacherService.EffectiveDates"/> / <c>CoversDate</c>;</item>
/// <item>to'lov darvozasi — <see cref="JournalPolicy.PaymentGate"/> (jurnalda ko'rinmagan o'quvchi
///   bu yerda ham ko'rinmaydi va sanoqqa kirmaydi);</item>
/// <item>davomat — <see cref="StudentJournalBuilder.BuildCells"/> (o'quvchi portali bilan bir xil).</item>
/// </list></para>
/// </summary>
public static class TeacherHomeService
{
    /// <summary>Jadval so'rovining eng katta oralig'i (kun) — oylik ko'rinish + zaxira.</summary>
    public const int MaxScheduleDays = 62;

    /// <summary>Bosh sahifadagi guruh kartasida "keyingi dars" qidiriladigan oraliq (kun).</summary>
    private const int NextLessonLookahead = 21;

    /* =========================================================================================
     *  SOF qism — dars rejasi (DB'siz, testlanadi)
     * ========================================================================================= */

    /// <summary>Rejalashtirish uchun bitta guruh: egasimi (<paramref name="Owned"/>), ko'chirishlari va
    /// shu guruhga tegishli FAOL o'rinbosar tayinlovlari.</summary>
    public sealed record PlanSource(
        Group Group, bool Owned,
        IReadOnlyList<LessonReschedule> Moves,
        IReadOnlyList<SubstituteTeacherAssignment> Assignments);

    /// <summary>Rejadagi bitta dars. <c>SubstituteTeacherId</c> — egasining darsini o'tadigan
    /// boshqa o'qituvchi (bo'lmasa bo'sh).</summary>
    public sealed record PlannedLesson(
        string GroupId, string Date, string StartTime, string EndTime,
        bool IsSubstitute, string SubstituteTeacherId, bool Moved);

    /// <summary>
    /// O'qituvchining <paramref name="from"/>..<paramref name="to"/> (ikkalasi ham kiradi) oralig'idagi darslari.
    /// <para><b>O'z guruhi</b> — guruh kunlari (+ ko'chirishlar), guruh umri ichida. Shu kuni boshqa
    /// odam o'rinbosar bo'lsa dars BARIBIR ro'yxatda (o'qituvchi "bugun darsim yo'q, nega?" deb
    /// qolmasin), lekin <c>SubstituteTeacherId</c> to'ldiriladi.</para>
    /// <para><b>O'rinbosarlik</b> — faqat tayinlovning O'Z kunlari (<c>EffectiveDates</c>).</para>
    /// <para>⚠️ Dars kunlari belgilanmagan guruh jadvalga TUSHMAYDI. Maosh hisobida bunday guruh
    /// "oyning har kuni" deb olinadi (<see cref="SubstituteTeacherService.LessonDatesInMonth"/>),
    /// lekin jadvalda bu 30 ta soxta darsni chizardi.</para>
    /// </summary>
    public static List<PlannedLesson> PlanLessons(
        string teacherId, IEnumerable<PlanSource> sources, DateOnly from, DateOnly to)
    {
        var result = new List<PlannedLesson>();
        if (to < from || string.IsNullOrEmpty(teacherId)) return result;
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");
        bool InRange(string d) => string.CompareOrdinal(d, fromStr) >= 0 && string.CompareOrdinal(d, toStr) <= 0;

        foreach (var src in sources)
        {
            var g = src.Group;
            var moves = src.Moves.Select(m => new JournalService.LessonMove(m.FromDate, m.ToDate)).ToList();
            // Bir kunga bir necha ko'chirish tushsa — eng oxirgisi (zanjir baribir bitta yangi kunga yig'iladi).
            var moveByTo = src.Moves
                .Where(m => m.ToDate.Length >= 10)
                .GroupBy(m => m.ToDate, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.OrderBy(m => m.CreatedAt).Last(), StringComparer.Ordinal);

            IEnumerable<string> dates = src.Owned
                ? GroupLessonDates(g, moves, from, to)
                : src.Assignments
                    .Where(a => a.IsActive && a.SubstituteTeacherId == teacherId)
                    .SelectMany(a => SubstituteTeacherService.EffectiveDates(a, g, moves))
                    .Where(InRange)
                    .Distinct(StringComparer.Ordinal);

            foreach (var d in dates)
            {
                moveByTo.TryGetValue(d, out var mv);
                var (start, end) = LessonTime(g.StartTime, g.EndTime, mv?.Time);
                var substitute = src.Owned
                    ? src.Assignments.FirstOrDefault(a =>
                        a.IsActive && a.SubstituteTeacherId != teacherId
                        && SubstituteTeacherService.CoversDate(a, d))?.SubstituteTeacherId ?? ""
                    : "";
                result.Add(new PlannedLesson(g.Id, d, start, end, !src.Owned, substitute, mv is not null));
            }
        }

        return result
            .OrderBy(l => l.Date, StringComparer.Ordinal)
            .ThenBy(l => ScheduleRules.ParseTime(l.StartTime) ?? int.MaxValue)
            .ToList();
    }

    /// <summary>Guruhning oraliqdagi dars sanalari — kunlar + ko'chirishlar, guruh umri ichida
    /// (boshlanishidan oldin va yopilganidan keyin dars yo'q).</summary>
    public static List<string> GroupLessonDates(
        Group g, IReadOnlyList<JournalService.LessonMove> moves, DateOnly from, DateOnly to)
    {
        var result = new List<string>();
        if (to < from || g.Days is not { Count: > 0 }) return result;
        var lo = from.ToString("yyyy-MM-dd");
        var hi = to.ToString("yyyy-MM-dd");
        var start = g.StartDate is { Length: >= 10 } ? g.StartDate[..10] : null;
        var end = SalaryLedger.LessonEnd(g.EndDate, g.ArchivedAt);
        if (start is not null && string.CompareOrdinal(start, lo) > 0) lo = start;
        if (end is not null && string.CompareOrdinal(end, hi) < 0) hi = end;
        if (string.CompareOrdinal(lo, hi) > 0) return result;

        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
            foreach (var d in JournalService.EffectiveLessonDatesInMonth(g.Days, m.ToString("yyyy-MM"), moves))
                if (string.CompareOrdinal(d, lo) >= 0 && string.CompareOrdinal(d, hi) <= 0)
                    result.Add(d);
        return result;
    }

    /// <summary>
    /// Dars vaqti. Ko'chirilgan darsda yangi boshlanish vaqti berilgan bo'lsa (<paramref name="movedStart"/>)
    /// dars shu vaqtdan boshlanadi va ASL davomiyligini saqlaydi (14:00–15:30 → 16:00 bo'lsa 16:00–17:30).
    /// Buzuq vaqt jimgina bo'sh qaytadi — jadval buzilmasin.
    /// </summary>
    public static (string Start, string End) LessonTime(string? start, string? end, string? movedStart)
    {
        var s = ScheduleRules.ParseTime(start);
        var e = ScheduleRules.ParseTime(end);
        var moved = ScheduleRules.ParseTime(movedStart);
        if (moved is null)
            return (s is null ? "" : ScheduleRules.FormatTime(s.Value), e is null ? "" : ScheduleRules.FormatTime(e.Value));

        var duration = s is not null && e is not null && e > s ? e.Value - s.Value : (int?)null;
        var newEnd = duration is null ? "" : ScheduleRules.FormatTime(Math.Min(moved.Value + duration.Value, 24 * 60 - 1));
        return (ScheduleRules.FormatTime(moved.Value), newEnd);
    }

    /// <summary>
    /// Bitta a'zolikning oylik davomati — <see cref="StudentJournalBuilder"/> qoidasida:
    /// "jonli" dars = o'tilgan, bloklanmagan (a'zolikdan tashqari emas), noma'lum emas.
    /// Foiz = (jonli − kelmagan) / jonli; kech kelgan KELGAN hisoblanadi (guruh "Davomat" tabi bilan bir xil).
    /// </summary>
    public static (int Held, int Attended, int Absent, int Late, int? Pct) Attendance(
        IEnumerable<StudentJournalBuilder.Cell> cells)
    {
        var live = cells.Where(c => !c.Blocked && c.Conducted && !c.Unknown).ToList();
        var absent = live.Count(c => c.ReasonName is not null && !c.IsLate);
        var late = live.Count(c => c.ReasonName is not null && c.IsLate);
        var attended = live.Count(c => c.Present || c.Grade.HasValue || c.IsLate);
        int? pct = live.Count == 0 ? null : (int)Math.Round((live.Count - absent) * 100.0 / live.Count);
        return (live.Count, attended, absent, late, pct);
    }

    /* =========================================================================================
     *  DB qism
     * ========================================================================================= */

    /// <summary>O'qituvchi ko'radigan guruhlar: o'ziniki + o'rinbosarlik (ko'rish oynasida).
    /// <c>GET api/teacher/classes</c> bilan AYNAN bir xil to'plam — kursi yo'q guruh kirmaydi.</summary>
    private static async Task<(List<Group> Groups, HashSet<string> Owned)> VisibleGroupsAsync(
        IAppDbContext db, string teacherId)
    {
        var subIds = await SubstituteTeacherService.SubstituteGroupIdsAsync(db, teacherId);
        var groups = await db.Classes.AsNoTracking()
            .Where(c => c.TeacherId == teacherId || subIds.Contains(c.Id))
            .Where(TeacherGroupAccess.VisibleQuery)
            .ToListAsync();
        groups = groups.Where(g => !string.IsNullOrEmpty(g.CourseId)).ToList();
        var owned = groups.Where(g => g.TeacherId == teacherId).Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return (groups, owned);
    }

    /// <summary>Guruh a'zolari — faqat o'qituvchi jurnalida KO'RINADIGANLAR (to'lov darvozasi
    /// yashirganlar tashlanadi) + shu guruh bo'yicha balans.</summary>
    private sealed record Member(StudentGroup Membership, Student Student, GroupBalanceService.GroupBalanceInfo Balance);

    private static async Task<Dictionary<string, List<Member>>> VisibleMembersAsync(
        IAppDbContext db, IReadOnlyCollection<string> groupIds, bool withBalance)
    {
        var result = groupIds.ToDictionary(id => id, _ => new List<Member>(), StringComparer.Ordinal);
        if (groupIds.Count == 0) return result;

        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => groupIds.Contains(sg.GroupId) && sg.IsActive)
            .ToListAsync();
        var studentIds = memberships.Select(m => m.StudentId).Distinct().ToList();
        // ⚠️ ARXIVDAGI o'quvchi KO'RINMAYDI — eski (2026-10 gacha) arxivlashlar a'zolikni yopmagan,
        // ya'ni `IsActive` qator qolgan bo'lishi mumkin. Busiz arxivdagi bola o'qituvchining
        // ro'yxati/sanoqlarida turib qolardi (arxiv = o'quvchi ketgan).
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && !s.IsArchived)
            .ToDictionaryAsync(s => s.Id);

        var policy = await JournalPolicy.GetAsync(db);
        var gateOn = policy.HideUnpaidPrevMonth || policy.HideUnpaidAfterDay;
        var curMonth = TuitionService.CurrentMonth();
        var day = AppClock.Today.Day;

        foreach (var grp in memberships.GroupBy(m => m.GroupId, StringComparer.Ordinal))
        {
            var ids = grp.Select(m => m.StudentId).Where(students.ContainsKey).ToList();
            // Balans faqat kerak bo'lganda (darvoza yoqilgan yoki ro'yxatda ko'rsatiladi) — u eng qimmat so'rov.
            var balances = withBalance || gateOn
                ? await GroupBalanceService.DetailedForGroupAsync(db, grp.Key, ids)
                : new Dictionary<string, GroupBalanceService.GroupBalanceInfo>();
            foreach (var m in grp)
            {
                if (!students.TryGetValue(m.StudentId, out var st)) continue;
                var bal = balances.GetValueOrDefault(m.StudentId);
                if (gateOn && JournalPolicy.PaymentGate(policy, bal, curMonth, day).Hidden) continue;
                result[grp.Key].Add(new Member(m, st, bal));
            }
        }
        return result;
    }

    /// <summary>Xona nomi: avval <c>RoomId</c> (xonalar katalogi), bo'lmasa eski matnli <c>Room</c>.</summary>
    private static async Task<Dictionary<string, string>> RoomNamesAsync(IAppDbContext db, IEnumerable<Group> groups)
    {
        var ids = groups.Select(g => g.RoomId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<string, string>()
            : await db.Rooms.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);
        return groups.ToDictionary(
            g => g.Id,
            g => !string.IsNullOrEmpty(g.RoomId) && names.TryGetValue(g.RoomId, out var n) && n.Length > 0 ? n : g.Room ?? "",
            StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, string>> CourseNamesAsync(IAppDbContext db, IEnumerable<Group> groups)
    {
        var ids = groups.Select(g => g.CourseId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<string, string>()
            : await db.Subjects.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);
    }

    /// <summary>
    /// Oraliqdagi darslar (jadval sahifasi va bosh sahifadagi "Bugungi darslar").
    /// <para>O'rinbosarlik bu yerda KO'RISH oynasi bilan cheklanmaydi — "keyingi haftada qaysi
    /// guruhga kiraman" jadvalda ko'rinishi kerak. Guruhga KIRISH (jurnal) esa avvalgidek
    /// <c>ResolveOwnedGroup</c> darvozasida, bu yerda hech narsa ochilmaydi.</para>
    /// </summary>
    public static async Task<List<TeacherLessonDto>> ScheduleAsync(
        IAppDbContext db, string teacherId, DateOnly from, DateOnly to)
    {
        if (to < from) return new();
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");

        // O'rinbosar tayinlovlari: men o'rinbosar bo'lganlar + MENING guruhlarimda boshqa odam o'rinbosar bo'lganlar.
        // Qo'pol (superset) filtr bazada — aniq kunlar keyin EffectiveDates/CoversDate bilan.
        var ownedIds = await db.Classes.AsNoTracking()
            .Where(c => c.TeacherId == teacherId)
            .Where(TeacherGroupAccess.VisibleQuery)
            .Select(c => c.Id)
            .ToListAsync();
        var assignments = await db.SubstituteTeacherAssignments.AsNoTracking()
            .Where(a => a.IsActive
                        && string.Compare(a.Date, toStr) <= 0
                        && string.Compare(a.EndDate ?? a.Date, fromStr) >= 0
                        && (a.SubstituteTeacherId == teacherId || ownedIds.Contains(a.GroupId)))
            .ToListAsync();
        // SelectedDates oraliqdan tashqariga chiqqan bo'lishi mumkin — ularni ham qamrab olamiz.
        var subGroupIds = assignments.Where(a => a.SubstituteTeacherId == teacherId).Select(a => a.GroupId).Distinct().ToList();

        var groups = await db.Classes.AsNoTracking()
            .Where(c => ownedIds.Contains(c.Id) || subGroupIds.Contains(c.Id))
            .Where(TeacherGroupAccess.VisibleQuery)
            .ToListAsync();
        groups = groups.Where(g => !string.IsNullOrEmpty(g.CourseId)).ToList();
        if (groups.Count == 0) return new();
        var gids = groups.Select(g => g.Id).ToList();

        var moves = await db.LessonReschedules.AsNoTracking().Where(r => gids.Contains(r.ClassId)).ToListAsync();
        var movesByGroup = moves.ToLookup(m => m.ClassId, StringComparer.Ordinal);
        var assignByGroup = assignments.ToLookup(a => a.GroupId, StringComparer.Ordinal);

        var sources = groups.Select(g => new PlanSource(
            g, g.TeacherId == teacherId, movesByGroup[g.Id].ToList(), assignByGroup[g.Id].ToList()));
        var planned = PlanLessons(teacherId, sources, from, to);
        if (planned.Count == 0) return new();

        var courseNames = await CourseNamesAsync(db, groups);
        var roomNames = await RoomNamesAsync(db, groups);
        var subTeacherIds = planned.Select(p => p.SubstituteTeacherId).Where(x => x.Length > 0).Distinct().ToList();
        var teacherNames = subTeacherIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Teachers.AsNoTracking().Where(t => subTeacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName);

        var members = await VisibleMembersAsync(db, gids, withBalance: false);
        var byId = groups.ToDictionary(g => g.Id, StringComparer.Ordinal);

        // "O'tildi" — shu guruh KURSI bo'yicha (jurnal ham kurs bo'yicha ochiladi).
        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => gids.Contains(n.ClassId) && n.Conducted
                        && n.Date.CompareTo(fromStr) >= 0 && n.Date.CompareTo(toStr) <= 0)
            .Select(n => new { n.ClassId, n.SubjectId, n.Date })
            .ToListAsync();
        var conducted = notes
            .Where(n => byId.TryGetValue(n.ClassId, out var g) && g.CourseId == n.SubjectId)
            .Select(n => n.ClassId + "|" + n.Date)
            .ToHashSet(StringComparer.Ordinal);

        return planned.Select(p =>
        {
            var g = byId[p.GroupId];
            var count = members[p.GroupId].Count(m => m.Membership.Status != "frozen");
            return new TeacherLessonDto(
                g.Id, g.Name, courseNames.GetValueOrDefault(g.CourseId, ""), p.Date,
                p.StartTime, p.EndTime, roomNames.GetValueOrDefault(g.Id, ""), count,
                p.IsSubstitute, p.SubstituteTeacherId.Length > 0 ? teacherNames.GetValueOrDefault(p.SubstituteTeacherId, "O'rinbosar") : "",
                p.Moved, conducted.Contains(g.Id + "|" + p.Date));
        }).ToList();
    }

    /// <summary>Bosh sahifa: guruh kartalari + bugungi darslar + o'quvchilar soni.
    /// <paramref name="canSchedule"/> = false bo'lsa jadvalga oid qismlar (bugungi darslar,
    /// keyingi dars) bo'sh qaytadi — "Dars jadvali" ruxsati berilmagan.</summary>
    public static async Task<TeacherOverviewDto> OverviewAsync(
        IAppDbContext db, string teacherId, DateOnly today, bool canSchedule)
    {
        var (groups, owned) = await VisibleGroupsAsync(db, teacherId);
        var gids = groups.Select(g => g.Id).ToList();
        var members = await VisibleMembersAsync(db, gids, withBalance: false);
        var courseNames = await CourseNamesAsync(db, groups);
        var roomNames = await RoomNamesAsync(db, groups);

        var todayLessons = canSchedule ? await ScheduleAsync(db, teacherId, today, today) : new();

        // Keyingi dars — faqat o'z guruhlari uchun (o'rinbosarlik kunlari jadvalda ko'rinadi).
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        if (canSchedule && owned.Count > 0)
        {
            var ownedIds = owned.ToList();
            var moves = (await db.LessonReschedules.AsNoTracking().Where(r => ownedIds.Contains(r.ClassId)).ToListAsync())
                .ToLookup(m => m.ClassId, StringComparer.Ordinal);
            foreach (var g in groups.Where(g => owned.Contains(g.Id)))
            {
                var mv = moves[g.Id].Select(m => new JournalService.LessonMove(m.FromDate, m.ToDate)).ToList();
                var first = GroupLessonDates(g, mv, today, today.AddDays(NextLessonLookahead)).FirstOrDefault();
                if (first is not null) next[g.Id] = first;
            }
        }

        var cards = groups
            .Select(g =>
            {
                var list = members[g.Id];
                var (start, end) = LessonTime(g.StartTime, g.EndTime, null);
                return new TeacherGroupCardDto(
                    g.Id, g.Name, g.CourseId, courseNames.GetValueOrDefault(g.CourseId, ""),
                    g.Days.OrderBy(d => d).ToList(), start, end, roomNames.GetValueOrDefault(g.Id, ""),
                    g.StartDate ?? "",
                    list.Count(m => m.Membership.Status == "active"),
                    list.Count(m => m.Membership.Status == "trial"),
                    list.Count(m => m.Membership.Status == "frozen"),
                    !owned.Contains(g.Id), next.GetValueOrDefault(g.Id, ""));
            })
            .OrderBy(c => c.IsSubstitute)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // O'quvchilar soni — DISTINCT (bir o'quvchi ikki guruhda bo'lsa bitta), muzlatilganlar kirmaydi.
        var studentCount = groups.Where(g => owned.Contains(g.Id))
            .SelectMany(g => members[g.Id])
            .Where(m => m.Membership.Status != "frozen")
            .Select(m => m.Student.Id)
            .Distinct()
            .Count();

        var debt = await DebtSummaryAsync(db, owned);
        return new TeacherOverviewDto(today.ToString("yyyy-MM-dd"), cards, todayLessons, studentCount, canSchedule,
            debt.DebtorCount, debt.DebtTotal);
    }

    /// <summary>
    /// O'qituvchining O'Z guruhlaridagi qarzdorlar: SHU GURUH balansi manfiy o'quvchilar (distinct) va jami
    /// qarz (har guruh qarzi alohida qo'shiladi). ⚠️ To'lov darvozasi bu yerda QO'LLANMAYDI — u aynan
    /// qarzdorlarni jurnaldan yashiradi, sanoqdan tushirib yuborsak "qarzdor yo'q" chiqardi.
    /// </summary>
    public static async Task<TeacherDebtSummaryDto> DebtSummaryAsync(IAppDbContext db, IReadOnlyCollection<string> ownedGroupIds)
    {
        if (ownedGroupIds.Count == 0) return new TeacherDebtSummaryDto(0, 0m);
        var ids = ownedGroupIds.ToList();
        // Arxivdagi o'quvchi qarzdor sifatida SANALMAYDI (eski arxivlashlarda tirik a'zolik qolgan —
        // `VisibleMembersAsync` dagi izoh); o'qituvchi uni "yopishi" kerak bo'lgan qarz emas.
        var memberships = await (
                from sg in db.StudentGroups.AsNoTracking()
                where ids.Contains(sg.GroupId) && sg.IsActive && sg.Status != "trial"
                join st in db.Students.AsNoTracking() on sg.StudentId equals st.Id
                where !st.IsArchived
                select new { sg.GroupId, sg.StudentId })
            .ToListAsync();
        var debtors = new HashSet<string>(StringComparer.Ordinal);
        var total = 0m;
        foreach (var grp in memberships.GroupBy(m => m.GroupId, StringComparer.Ordinal))
        {
            var balances = await GroupBalanceService.DetailedForGroupAsync(db, grp.Key, grp.Select(m => m.StudentId).ToList());
            foreach (var (sid, info) in balances)
            {
                if (info.Balance >= 0) continue;
                debtors.Add(sid);
                total += -info.Balance;
            }
        }
        return new TeacherDebtSummaryDto(debtors.Count, decimal.Round(total, 0));
    }

    /// <summary>O'qituvchining o'z (o'rinbosarliksiz) guruhlari bo'yicha qarzdorlik.</summary>
    public static async Task<TeacherDebtSummaryDto> DebtSummaryForTeacherAsync(IAppDbContext db, string teacherId)
    {
        var (_, owned) = await VisibleGroupsAsync(db, teacherId);
        return await DebtSummaryAsync(db, owned);
    }

    /// <summary>
    /// "O'quvchilarim": barcha guruhlardagi a'zoliklar + tanlangan oy davomati. Oy berilmasa yoki
    /// noto'g'ri/kelajak bo'lsa — joriy oy.
    /// </summary>
    public static async Task<TeacherStudentsDto> StudentsAsync(IAppDbContext db, string teacherId, string? month)
    {
        var cur = TuitionService.CurrentMonth();
        var resolved = month is { Length: 7 } && DateOnly.TryParse(month + "-01", out _)
            && string.CompareOrdinal(month, cur) <= 0 ? month : cur;

        var (groups, _) = await VisibleGroupsAsync(db, teacherId);
        if (groups.Count == 0) return new TeacherStudentsDto(resolved, new());
        var gids = groups.Select(g => g.Id).ToList();

        var members = await VisibleMembersAsync(db, gids, withBalance: true);
        var courseNames = await CourseNamesAsync(db, groups);
        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id);

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => gids.Contains(e.ClassId) && e.Date.StartsWith(resolved))
            .ToListAsync();
        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => gids.Contains(n.ClassId) && n.Conducted && n.Date.StartsWith(resolved))
            .ToListAsync();
        var moves = (await db.LessonReschedules.AsNoTracking().Where(r => gids.Contains(r.ClassId)).ToListAsync())
            .ToLookup(m => m.ClassId, StringComparer.Ordinal);

        var rows = new List<TeacherStudentRowDto>();
        foreach (var g in groups)
        {
            // Jurnal kurs bo'yicha — guruhda qolgan eski (boshqa kurs) yozuvlar sanalmaydi.
            var groupEntries = entries.Where(e => e.ClassId == g.Id && e.SubjectId == g.CourseId).ToList();
            var lessonByDate = StudentJournalBuilder.AggregateNotes(
                notes.Where(n => n.ClassId == g.Id && n.SubjectId == g.CourseId));
            var dates = JournalService.EffectiveLessonDatesInMonth(
                g.Days, resolved, moves[g.Id].Select(m => new JournalService.LessonMove(m.FromDate, m.ToDate)));

            foreach (var m in members[g.Id])
            {
                var entryByDate = groupEntries
                    .Where(e => e.StudentId == m.Student.Id)
                    .GroupBy(e => e.Date, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
                var cells = StudentJournalBuilder.BuildCells(
                    g, StudentJournalBuilder.BoundsOf(m.Membership), dates, entryByDate, lessonByDate, reasons);
                var (held, attended, absent, late, pct) = Attendance(cells);
                rows.Add(new TeacherStudentRowDto(
                    m.Student.Id, m.Student.FullName, m.Student.BirthCertificateUrl ?? "",
                    g.Id, g.Name, courseNames.GetValueOrDefault(g.CourseId, ""),
                    m.Membership.Status ?? "trial", JournalService.MemberStart(m.Membership) ?? "",
                    m.Balance.DebtMonths,
                    held, attended, absent, late, pct));
            }
        }

        return new TeacherStudentsDto(resolved, rows
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }
}
