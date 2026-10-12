using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// The student RATING of a period — tests + journal ball (CRM upgrade Phase 4, U4m — docs/SPEC-crm-upgrade.md P4.3/P4.6,
/// rules .claude/rules/rating.md): the centre table, one group's table and one student's block, exactly as the panel's
/// «Hisobotlar → Reyting», the group page «Umumiy reyting» and the profile «Reyting» tab show them.
///
/// <para>One computation per period (<see cref="RatingEngine.ComputeAsync"/> — SQL aggregates only, the journal is never
/// loaded row by row), sliced per tool with the panel's own functions (<see cref="RatingEngine.Centre"/>,
/// <see cref="RatingEngine.GroupTable"/>, <see cref="RatingEngine.StudentBlockAsync"/>) — the numbers never disagree.
/// The MCP cache keeps a period for 5 minutes (the panel's key, TTL only).</para>
///
/// <para>The monthly TOP/BOTTOM preview (<c>rating_top_bottom</c>, U4d) is <see cref="TopBottomService.BuildAsync"/> over the
/// same cached month — what the monthly notifier sends; N and the scope come from the allow-listed CenterMeta columns.</para>
///
/// <para>The legacy ball-only views stay as they were: <c>rating_group</c> (the group page's «Ball tafsiloti») and
/// <c>rating_center</c> (the old portal centre rating by collected ball).</para>
/// </summary>
[McpServerToolType]
public sealed class RatingTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(TestResult), typeof(TestScore), typeof(Group), typeof(Subject), typeof(Teacher), typeof(StudentGroup),
        typeof(Student), typeof(JournalEntry), typeof(CriterionGrade), typeof(StudentBallAdjustment), typeof(CenterMeta),
        // The block's attendance line — StudentJournalBuilder.PeriodAsync (lesson notes, moved lessons, reasons).
        typeof(LessonNote), typeof(AbsenceReason), typeof(LessonReschedule),
    ];

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private const string FormulaNote =
        "Reyting bali = testlar o'rtacha foizi × w + ball × (1 − w); ball — guruhdagi ENG YAXSHI balga nisbatan (0–100), "
        + "w — sozlamadagi testlar ulushi (testWeight, standart 60%). Faqat test bo'lsa — test foizi, faqat ball bo'lsa — "
        + "ball. Markaz bali — o'quvchining guruh ballari o'rtachasi. O'rin musobaqa usulida (1, 2, 2, 4). Arxivdagi "
        + "o'quvchi kirmaydi, qarzdorlik reytingga ta'sir qilmaydi.";

    private RatingPeriod ResolvePeriod(string? period, string? key)
    {
        var (p, error) = RatingPeriods.Resolve(period, key, McpToolContext.Today);
        return p ?? throw new McpException(
            (error ?? RatingPeriods.InvalidMessage) + ": period — month | quarter | year; key — month/quarter uchun YYYY-MM "
            + "(quarter — oxirgi oy), year uchun o'quv yili boshlanish yili YYYY (kelajakdagi davr bo'lmaydi).");
    }

    /// <summary>The panel's period computation, cached per period for this process (key = the panel's key).</summary>
    private RatingLoader Loader(CancellationToken ct) =>
        p => t.CachedAsync(RatingEngine.CacheKey(p), CacheTtl, () => RatingEngine.ComputeAsync(t.Db, p, ct));

    // =====================================================================================
    //  rating_report — RatingController.Centre
    // =====================================================================================

    [McpServerTool(Name = "rating_report", Title = "Reyting (testlar + ball) — markaz",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("«Hisobotlar → Reyting» (Markaz) bilan bir xil: tanlangan davr bo'yicha o'quvchilar reytingi — TESTLAR va "
        + "jurnal BALLI birga (standart 60% testlar + 40% ball, ball guruhning eng yaxshisiga nisbatan). Har qatorda: markazdagi "
        + "o'rin, o'quvchi, reyting bali, testlar o'rtacha foizi va soni, jami ball, har guruhdagi o'rni va bali. Davr: month — "
        + "oy (key YYYY-MM, sukut joriy oy), quarter — sirpanuvchi 3 oy (key — oxirgi oy YYYY-MM), year — o'quv yili "
        + "sentabr–avgust (key — boshlanish yili YYYY). Ism bo'yicha qidiruv. Sahifalangan (TOP N — sahifa hajmi). Bitta guruh — "
        + "rating_group_report, bitta o'quvchi — student_rating. "
        + "Centre student rating of a period (tests + journal ball), as the panel's rating report.")]
    public async Task<string> RatingReport(
        [Description("Davr turi: month (sukut) | quarter | year")] string? period = null,
        [Description("Davr kaliti: month/quarter — YYYY-MM, year — YYYY (sukut — joriy)")] string? key = null,
        [Description("O'quvchi ismi bo'lagi")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi = TOP N (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Rating);
        var p = ResolvePeriod(period, key);
        var r = await Loader(ct)(p);
        var dto = RatingEngine.Centre(r, search, McpToolContext.Page(page), McpToolContext.PageSize(pageSize));
        var items = dto.Items.Select(i => new
        {
            i.Rank, i.StudentId, i.FullName, i.Score,
            testAvg = i.TestAvg, testCount = i.TestCount, ball = i.Ball,
            groups = i.Groups.Select(g => new { g.Id, g.Name, g.Rank, g.Score }),
        }).ToList();

        return t.Json(new
        {
            period = dto.Period,
            testWeight = dto.TestWeight,
            rankedStudents = r.CentreTotal,
            note = FormulaNote + " Natija 5 daqiqagacha eskirgan bo'lishi mumkin.",
            total = dto.Total, page = dto.Page, pageSize = dto.PageSize,
            items,
        }, items.Count);
    }

    // =====================================================================================
    //  rating_group_report — RatingController.GroupRating
    // =====================================================================================

    [McpServerTool(Name = "rating_group_report", Title = "Reyting (testlar + ball) — guruh",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta guruhning tanlangan davrdagi reytingi — guruh sahifasidagi «Umumiy reyting (testlar + ball)» va "
        + "«Hisobotlar → Reyting → Guruhlar» bilan bir xil: guruhdagi o'rin, o'quvchi, reyting bali, testlar o'rtacha foizi va "
        + "soni, ball va uning guruhning eng yaxshisiga nisbati (ballNorm). Avval davrda testi yoki bali bor o'quvchilar "
        + "(o'rinlilar, keyin o'rinsizlar), keyin ma'lumoti yo'q hozirgi faol/sinov a'zolar («—»). Davr: month | quarter | "
        + "year (rating_report dagi kabi). Yig'ilgan ball tafsiloti (mezonlar, davomat) — rating_group. "
        + "One group's rating of a period (tests + journal ball).")]
    public async Task<string> RatingGroupReport(
        [Description("Guruh id (groups_list natijasidan)")] string groupId,
        [Description("Davr turi: month (sukut) | quarter | year")] string? period = null,
        [Description("Davr kaliti: month/quarter — YYYY-MM, year — YYYY (sukut — joriy)")] string? key = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.RatingGroup);
        if (string.IsNullOrWhiteSpace(groupId)) throw new McpException("groupId kerak.");
        var gid = groupId.Trim();
        var p = ResolvePeriod(period, key);
        var r = await Loader(ct)(p);
        var info = r.GroupInfo.GetValueOrDefault(gid);
        if (info is null) return t.Json(new { message = "Guruh topilmadi." }, 0);

        // Current members (active/trial, non-archived students) — listed even without data (RatingController).
        var db = t.Db;
        var members = await (from sg in db.StudentGroups
                             join s in db.Students on sg.StudentId equals s.Id
                             where sg.GroupId == gid && sg.IsActive && sg.Status != "frozen" && !s.IsArchived
                             select new { s.Id, s.FullName })
            .ToListAsync(ct);
        var byId = members.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().FullName, StringComparer.Ordinal);
        var dto = RatingEngine.GroupTable(r, info, byId);

        return t.Json(new
        {
            period = dto.Period,
            testWeight = dto.TestWeight,
            group = new { dto.Group.Id, dto.Group.Name, course = dto.Group.CourseName, teacher = dto.Group.TeacherName, info.IsArchived },
            ranked = dto.Ranked,
            note = FormulaNote + " «ballNorm» — ball ÷ guruhdagi eng yaxshi ball × 100.",
            total = dto.Items.Count,
            items = dto.Items,
        }, dto.Items.Count);
    }

    // =====================================================================================
    //  student_rating — RatingController.StudentBlock
    // =====================================================================================

    [McpServerTool(Name = "student_rating", Title = "O'quvchi reytingi (davr bo'yicha)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta o'quvchining reyting bloki — profil «Reyting» tabi bilan bir xil: markazdagi o'rni (jami nechtadan, "
        + "oldingi davrdagi o'rni bilan), har guruhdagi o'rni, komponentlar (testlar o'rtacha foizi, testlar soni, jami ball, "
        + "testlar ulushi), oxirgi 12 oy tarixi (markazdagi o'rin va bal), davrdagi testlari (yechgan/savollar, foiz, testdagi "
        + "o'rni / qatnashganlar) va davrdagi davomat (o'tilgan, kelgan, foiz — yagona davomat ta'rifi). Davr: month | quarter | "
        + "year. One student's rating block for a period.")]
    public async Task<string> StudentRating(
        [Description("O'quvchi id (students_search natijasidan)")] string studentId,
        [Description("Davr turi: month (sukut) | quarter | year")] string? period = null,
        [Description("Davr kaliti: month/quarter — YYYY-MM, year — YYYY (sukut — joriy)")] string? key = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.StudentRating);
        if (string.IsNullOrWhiteSpace(studentId)) throw new McpException("studentId kerak.");
        var p = ResolvePeriod(period, key);
        var block = await RatingEngine.StudentBlockAsync(t.Db, Loader(ct), studentId.Trim(), p, McpToolContext.Today, ct);
        if (block is null) return t.Json(new { message = "O'quvchi topilmadi." }, 0);
        var name = await t.Db.Students.Where(s => s.Id == block.StudentId)
            .Select(s => new { s.FullName, s.IsArchived }).FirstOrDefaultAsync(ct);

        return t.Json(new
        {
            student = new { id = block.StudentId, fullName = name?.FullName ?? "", isArchived = name?.IsArchived ?? false },
            block.Period, block.Centre, block.Groups, block.Components, block.History, block.Tests, block.Attendance,
            note = FormulaNote + " «centre.prevRank» — oldingi davrdagi o'rin (oy → o'tgan oy, 3 oy → undan oldingi 3 oy, "
                + "o'quv yili → o'tgan o'quv yili); testdagi o'rin — o'sha testda ball olgan hamma orasida.",
        }, 1);
    }

    // =====================================================================================
    //  rating_top_bottom — RatingController.TopBottom (TopBottomService.BuildAsync, U4d)
    // =====================================================================================

    [McpServerTool(Name = "rating_top_bottom", Title = "Oylik TOP / BOTTOM (testlar bo'yicha)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("«Hisobotlar → Reyting → TOP/BOTTOM» oldindan ko'rish bilan bir xil — oylik avto-xabar AYNAN shu ro'yxatni "
        + "yuboradi: tanlangan oyda (sukut — O'TGAN oy, ya'ni navbatdagi yuborish ishlatadigan oy) testlar o'rtacha FOIZI (T) "
        + "eng yuqori (TOP) va eng past (BOTTOM) o'quvchilar. Ro'yxat hajmi k = min(N, ⌊n/2⌋): N — sozlama (1..10), n — "
        + "o'sha oyda testi bor o'quvchilar; T si TOP dagi kimdir bilan teng o'quvchi BOTTOM ga tushmaydi; oyda testi yo'qlar "
        + "qaralmaydi. Qamrov: group — har guruh alohida (kamida 2 ta testi bor o'quvchi bo'lsa), centre — bitta markaz "
        + "ro'yxati (o'quvchi T = guruhlaridagi T lar o'rtachasi); sukut — sozlamadagi. Har qatorda: o'quvchi, T va o'rin "
        + "(teng T — bir xil o'rin). Faqat KO'RISH — hech narsa yubormaydi (yuborish qoidalari — auto_message_rules, "
        + "rating_top / rating_bottom). Sahifalangan (guruhlar). Monthly TOP/BOTTOM students by test average (preview).")]
    public async Task<string> RatingTopBottom(
        [Description("Oy YYYY-MM (sukut — o'tgan oy; kelajakdagi oy bo'lmaydi)")] string? month = null,
        [Description("Qamrov: group (har guruh) | centre (markaz) — sukut: sozlamadagi")] string? scope = null,
        [Description("Faqat shu guruh ro'yxati (group qamrovida; groups_list natijasidan id)")] string? groupId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi — guruhlar (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Rating);
        var sc = string.IsNullOrWhiteSpace(scope) ? null : scope.Trim().ToLowerInvariant();
        if (sc is not null && sc is not (TopBottomRules.ScopeGroup or TopBottomRules.ScopeCentre))
            throw new McpException("«scope»: group | centre.");
        var gid = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();

        var (dto, _, error) = await TopBottomService.BuildAsync(t.Db, Loader(ct), month, sc, McpToolContext.Today, ct);
        if (dto is null)
            throw new McpException((error ?? RatingPeriods.InvalidMessage) + ": month — YYYY-MM, kelajakdagi oy bo'lmaydi.");

        var groups = dto.Groups.Where(g => gid is null || g.GroupId == gid).ToList();
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = groups.Skip((p - 1) * size).Take(size).Select(g => new
        {
            group = new { id = g.GroupId, name = g.GroupName },
            scored = g.Scored,
            top = g.Top.Select(r => new { r.StudentId, r.FullName, testAvg = r.TestAvg, r.Rank }),
            bottom = g.Bottom.Select(r => new { r.StudentId, r.FullName, testAvg = r.TestAvg, r.Rank }),
        }).ToList();

        return t.Json(new
        {
            month = dto.Month, monthLabel = dto.MonthLabel, scope = dto.Scope, count = dto.Count,
            summary = new
            {
                lists = groups.Count,
                topStudents = groups.Sum(g => g.Top.Count),
                bottomStudents = groups.Sum(g => g.Bottom.Count),
            },
            note = "T — oydagi testlar o'rtacha foizi (har test teng vaznda), o'rin — shu ro'yxat (guruh yoki markaz) ichida, "
                + "«scored» — o'sha oyda testi bor o'quvchilar (xabardagi «o'rin / n» ning n i). BOTTOM eng past T dan boshlab. "
                + "Markaz qamrovida bitta ro'yxat: group.id = \"\", nomi «Markaz». Natija 5 daqiqagacha eskirgan bo'lishi mumkin.",
            total = groups.Count, page = p, pageSize = size,
            items,
        }, items.Sum(i => i.top.Count() + i.bottom.Count()));
    }
}
