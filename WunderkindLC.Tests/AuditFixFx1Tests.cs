using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Birlashtirishdan keyingi revyu tuzatishlari (FX1) — a'zoliklar, o'quvchilar, guruhlar, o'rinbosarlar:
/// <list type="bullet">
///   <item><b>R1-2</b> — o'rinbosar sanalari: guruh hayoti o'zgarganda XOM saqlangan sanalar qirqiladi;
///     pul uchun faqat guruh umri va ko'chirishlar qo'llanadi (HOZIRGI hafta kunlari emas);</item>
///   <item><b>R1-3</b> — guruh almashganda chegirma IKKIGA bo'linadi (o'tkazish oyida ikkalasi ham amalda);</item>
///   <item><b>R1-4</b> — «Tugatish» tizim sababi muzlatish sababini yashirmaydi (KPI nazoratdan tashqari);</item>
///   <item><b>R1-1, R1-9, R1-10, R1-11, R2-6</b> — controller oqimi (manba matni bo'yicha qulf).</item>
/// </list>
/// </summary>
public class AuditFixFx1Tests
{
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static AuditService Audit(AppDbContext ctx) => new(ctx, new NoHttpContext());

    private static Teacher AddTeacher(AppDbContext ctx, string name)
    {
        var t = new Teacher { FullName = name };
        ctx.Teachers.Add(t);
        return t;
    }

    private static SubstituteTeacherAssignment Assignment(Group g, Teacher orig, Teacher sub, params string[] dates) => new()
    {
        GroupId = g.Id, OriginalTeacherId = orig.Id, SubstituteTeacherId = sub.Id, IsActive = true,
        Date = dates[0], EndDate = dates[^1], SelectedDates = dates.ToList(), CreatedBy = "Admin",
    };

    // =====================================================================================
    //  R1-2 — O'RINBOSAR SANALARI: pul uchun HOZIRGI hafta kunlari bilan kesishtirilmaydi
    // =====================================================================================

    [Fact]
    public void R1_2__guruh_kunlari_ozgarsa_otgan_oy_orinbosar_darslari_YOQOLMAYDI()
    {
        // Sentabrda guruh DUSHANBA o'qigan (7, 14-sentabr — dushanba); oktabrda jadval SESHANBAga o'zgardi.
        var g = new Group { Name = "G", Days = new List<int> { 1 } };
        var a = new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = "o", SubstituteTeacherId = "s", IsActive = true,
            Date = "2026-09-07", EndDate = "2026-09-14",
            SelectedDates = new List<string> { "2026-09-07", "2026-09-14" },
        };

        // Surat: o'tilgan darslar to'langan oydan jimgina tushib qolmasin (ilgari 0 chiqardi).
        Assert.Equal(2, SubstituteTeacherService.LessonsInMonth(a, g, "2026-09"));
        Assert.Equal(new[] { "2026-09-07", "2026-09-14" }, SubstituteTeacherService.EffectiveDates(a, g));
        // Maxraj (oyning dars soni) — o'zgarmagan qoida: hozirgi jadval (seshanbalar: 1, 8, 15, 22, 29).
        Assert.Equal(5, SubstituteTeacherService.ScheduledLessonsInMonth(g, "2026-09"));
    }

    [Fact]
    public void R1_2__guruh_umridan_tashqari_sana_tushadi__kochirilgan_darsning_YANGI_sanasi_sanaladi()
    {
        // Guruh 10-sentabrda boshlangan, 21-sentabrda yopilgan.
        var g = new Group { Name = "G", Days = new List<int> { 0 }, StartDate = "2026-09-10", ArchivedAt = "2026-09-21", IsArchived = true };
        var a = new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = "o", SubstituteTeacherId = "s", IsActive = true,
            Date = "2026-09-07", EndDate = "2026-09-28",
            SelectedDates = new List<string> { "2026-09-07", "2026-09-14", "2026-09-21", "2026-09-28" },
        };
        // 7 — boshlanishdan oldin, 28 — yopilgandan keyin; 21 — yopish kuni (oxirgi dars) QOLADI.
        Assert.Equal(new[] { "2026-09-14", "2026-09-21" }, SubstituteTeacherService.EffectiveDates(a, g));

        // 14-sentabr darsi 15-sentabrga ko'chirilgan: eski sana uchun haq YO'Q, yangisi sanaladi.
        var moves = new List<JournalService.LessonMove> { new("2026-09-14", "2026-09-15") };
        Assert.Equal(new[] { "2026-09-15", "2026-09-21" }, SubstituteTeacherService.EffectiveDates(a, g, moves));
        Assert.Equal(2, SubstituteTeacherService.LessonsInMonth(a, g, "2026-09", moves));

        // Kirish huquqi (guruhsiz chaqiruv) — saqlangan sanalar o'zgarmaydi.
        Assert.Equal(4, SubstituteTeacherService.EffectiveDates(a).Count);
    }

    [Fact]
    public async Task R1_2__guruh_yopilganda_KOCHIRILGAN_kundagi_orinbosar_darsi_saqlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var owner = AddTeacher(ctx, "Asosiy");
        var sub = AddTeacher(ctx, "O'rinbosar");
        // Faqat DUSHANBA; 7-sentabr (dushanba) darsi 8-sentabrga (seshanba) ko'chirilgan.
        var g = new Group { Name = "IELTS", TeacherId = owner.Id, Days = new List<int> { 0 } };
        ctx.Classes.Add(g);
        ctx.LessonReschedules.Add(new LessonReschedule { ClassId = g.Id, FromDate = "2026-09-07", ToDate = "2026-09-08" });
        var a = Assignment(g, owner, sub, "2026-09-08", "2026-09-14", "2026-09-21");
        ctx.SubstituteTeacherAssignments.Add(a);
        await ctx.SaveChangesAsync();

        var res = await SubstituteGroupLifecycle.TrimAfterAsync(ctx, Audit(ctx), g, "2026-09-14", "guruh yopildi");
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Trimmed);
        var saved = await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync();
        // Ilgari 8-sentabr (dushanba emas) jadval bo'yicha "dars emas" deb ABADIY o'chirilardi.
        Assert.Equal(new[] { "2026-09-08", "2026-09-14" }, saved.SelectedDates!.ToArray());
        Assert.Equal("2026-09-14", saved.EndDate);
    }

    [Fact]
    public async Task R1_2__oqituvchi_almashganda_jadvali_ozgargan_guruhda_otmish_sanalari_eski_egada_qoladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var oldOwner = AddTeacher(ctx, "Eski");
        var newOwner = AddTeacher(ctx, "Yangi");
        var sub = AddTeacher(ctx, "O'rinbosar");
        // Jadval allaqachon SESHANBAga o'zgargan; 7-sentabr (dushanba) — o'tilgan o'rinbosar darsi.
        var g = new Group { Name = "Math", TeacherId = oldOwner.Id, Days = new List<int> { 1 } };
        ctx.Classes.Add(g);
        var a = Assignment(g, oldOwner, sub, "2026-09-07", "2026-09-15");
        ctx.SubstituteTeacherAssignments.Add(a);
        await ctx.SaveChangesAsync();

        var res = await SubstituteGroupLifecycle.OnTeacherChangedAsync(ctx, Audit(ctx), g, newOwner.Id, "2026-09-10", "Admin");
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Split);
        var rows = await ctx.SubstituteTeacherAssignments.AsNoTracking().ToListAsync();
        var past = rows.Single(r => r.Id == a.Id);
        Assert.Equal(new[] { "2026-09-07" }, past.SelectedDates!.ToArray());
        Assert.Equal(oldOwner.Id, past.OriginalTeacherId);
        var future = rows.Single(r => r.Id != a.Id);
        Assert.Equal(new[] { "2026-09-15" }, future.SelectedDates!.ToArray());
        Assert.Equal(newOwner.Id, future.OriginalTeacherId);
    }

    // =====================================================================================
    //  R1-3 — GURUH ALMASHGANDA CHEGIRMA IKKIGA BO'LINADI
    // =====================================================================================

    private static (Group From, Group To, Student S) DiscountSetup(AppDbContext ctx)
    {
        var from = new Group { Name = "Ingliz A", CourseId = "eng", MonthlyFee = 600_000m };
        var to = new Group { Name = "Ingliz B", CourseId = "eng", MonthlyFee = 600_000m };
        var s = new Student { FullName = "Ali Valiyev" };
        ctx.Classes.AddRange(from, to);
        ctx.Students.Add(s);
        return (from, to, s);
    }

    [Fact]
    public async Task R1_3__otkazish_oyida_chegirma_qayta_hisoblansa_ESKI_guruh_qismi_chegirmasini_YOQOTMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (from, to, s) = DiscountSetup(ctx);
        ctx.StudentDiscounts.Add(new StudentDiscount
        {
            StudentId = s.Id, GroupId = from.Id, GroupName = from.Name, CourseId = "eng", CourseName = "Ingliz tili",
            Pct = 20, CreatedAt = AppClock.Iso(), CreatedBy = "Admin",
        });
        // O'tkazish oyi: eski guruh qisman (300 000, 20% = 60 000) va yangi guruh qisman hisobi.
        var month = TuitionService.CurrentMonth();
        ctx.MonthlyCharges.AddRange(
            new MonthlyCharge { StudentId = s.Id, GroupId = from.Id, Month = month, Amount = 300_000m, Discount = 60_000m, Date = $"{month}-01" },
            new MonthlyCharge { StudentId = s.Id, GroupId = to.Id, Month = month, Amount = 300_000m, Discount = 60_000m, Date = $"{month}-15" });
        s.Balance = -480_000m;
        await ctx.SaveChangesAsync();

        var res = await DiscountTransfer.MoveGroupScopedAsync(ctx, Audit(ctx), s, from, to, $"{month}-15");
        await ctx.SaveChangesAsync();
        Assert.Equal(1, res.Moved);

        // Keyin shu oyda chegirma/sinf o'zgarishi joriy oyni QAYTA hisoblaydi.
        var changed = await StudentDiscountService.ReapplyCurrentMonthAsync(ctx, s);
        await ctx.SaveChangesAsync();

        Assert.False(changed);   // ilgari eski guruh qismi chegirmasiz qolib, −60 000 yolg'on qarz chiqardi
        var charges = await ctx.MonthlyCharges.AsNoTracking().ToListAsync();
        Assert.All(charges, c => Assert.Equal(60_000m, c.Discount));
        Assert.Equal(-480_000m, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);

        var rows = await ctx.StudentDiscounts.AsNoTracking().ToListAsync();
        Assert.Equal(month, rows.Single(r => r.GroupId == from.Id).EndMonth);
        var moved = rows.Single(r => r.GroupId == to.Id);
        Assert.Equal(month, moved.StartMonth);
        Assert.Equal(20, moved.Pct);
        Assert.Equal("Admin", moved.CreatedBy);   // chegirmani BERGAN odam o'zgarmaydi
        Assert.Equal("Ingliz tili", moved.CourseName);

        // Ikkala tomon ham auditda (yakunlash — update, yangi qator — create).
        var logs = await ctx.AuditLogs.AsNoTracking().Where(l => l.EntityType == AuditService.EntityStudentDiscount).ToListAsync();
        Assert.Contains(logs, l => l.Action == "update" && l.Summary.Contains("yakunlandi"));
        Assert.Contains(logs, l => l.Action == "create" && l.Summary.Contains("ko'chirildi"));
    }

    [Fact]
    public async Task R1_3__guruhga_QAYTGAN_oquvchi_chegirmasini_yoqotmaydi__eski_tugagan_qator_yopiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (a, b, s) = DiscountSetup(ctx);
        // O'tgan oyda A→B bo'lingan: A qatori o'tgan oy bilan tugagan, B qatori o'tgan oydan.
        var oldA = new StudentDiscount
        {
            StudentId = s.Id, GroupId = a.Id, GroupName = a.Name, CourseId = "eng", Pct = 20,
            EndMonth = M(-1), CreatedAt = "2026-01-01T10:00:00",
        };
        var bRow = new StudentDiscount
        {
            StudentId = s.Id, GroupId = b.Id, GroupName = b.Name, CourseId = "eng", Pct = 20,
            StartMonth = M(-1), CreatedAt = "2026-01-02T10:00:00",
        };
        ctx.StudentDiscounts.AddRange(oldA, bRow);
        await ctx.SaveChangesAsync();

        // Endi B→A: A qamrovidagi eski qator to'siq EMAS (o'tkazish oyidan keyin kuchda emas).
        var res = await DiscountTransfer.MoveGroupScopedAsync(ctx, Audit(ctx), s, b, a, M(0));
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Moved);
        Assert.Equal(0, res.SkippedConflict);
        var rows = await ctx.StudentDiscounts.AsNoTracking().ToListAsync();
        Assert.Equal(StudentDiscount.StatusReplaced, rows.Single(r => r.Id == oldA.Id).Status);
        Assert.Single(rows, r => r.GroupId == a.Id && r.Status == StudentDiscount.StatusActive);
        var book = await DiscountBook.LoadForStudentAsync(ctx, s.Id);
        Assert.Equal(120_000m, book.DiscountFor(s.Id, 600_000m, M(1), a.Id));
        Assert.Equal(120_000m, book.DiscountFor(s.Id, 600_000m, M(0), b.Id));   // B ning o'tkazish oyi qismi
        Assert.Equal(0m, book.DiscountFor(s.Id, 600_000m, M(1), b.Id));
    }

    [Fact]
    public async Task R1_3__otkazish_oyidan_KEYIN_boshlanadigan_chegirma_bolinmaydi__joyida_kochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (from, to, s) = DiscountSetup(ctx);
        ctx.StudentDiscounts.Add(new StudentDiscount
        {
            StudentId = s.Id, GroupId = from.Id, CourseId = "eng", Pct = 10, StartMonth = M(2), CreatedAt = AppClock.Iso(),
        });
        await ctx.SaveChangesAsync();

        var res = await DiscountTransfer.MoveGroupScopedAsync(ctx, Audit(ctx), s, from, to, M(0));
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Moved);
        var row = await ctx.StudentDiscounts.AsNoTracking().SingleAsync();
        Assert.Equal(to.Id, row.GroupId);
        Assert.Equal(M(2), row.StartMonth);
    }

    [Fact]
    public void R1_3__yangi_guruhdagi_qator_faqat_otkazish_oyidan_KEYIN_ham_kuchda_bolsa_tosiq()
    {
        var m = "2026-10";
        Assert.True(DiscountTransfer.BlocksTarget(new StudentDiscount { EndMonth = "" }, m));
        Assert.True(DiscountTransfer.BlocksTarget(new StudentDiscount { EndMonth = "2026-11" }, m));
        Assert.False(DiscountTransfer.BlocksTarget(new StudentDiscount { EndMonth = "2026-10" }, m));
        Assert.False(DiscountTransfer.BlocksTarget(new StudentDiscount { EndMonth = "2026-09" }, m));
        Assert.False(DiscountTransfer.BlocksTarget(new StudentDiscount { Status = StudentDiscount.StatusCancelled }, m));
    }

    // =====================================================================================
    //  R1-4 — «TUGATISH» TIZIM SABABI MUZLATISH SABABINI YASHIRMAYDI
    // =====================================================================================

    private static MembershipReasonLog Log(string action, string date, string? reasonId, string label, string createdAt) => new()
    {
        StudentId = "s", GroupId = "g", Action = action, Date = date, ReasonId = reasonId, ReasonLabel = label,
        CreatedAt = createdAt,
    };

    private static readonly MembershipReasons.ArchiveInfo NoArchive = new(false, null, null);

    [Fact]
    public void R1_4__muzlatilgan_azo_tugatishda_chiqsa_MUZLATISH_sababi_olinadi()
    {
        var logs = new[]
        {
            Log(MembershipReasons.ActionFreeze, "2026-09-05", "moved-away", "Ko'chib ketdi", "2026-09-05T10:00:00"),
            Log(MembershipReasons.ActionClose, "2026-10-01", null, MembershipReasons.CompletedNotFinishedLabel, "2026-10-01T10:00:00"),
        };
        var r = MembershipReasons.Resolve("2026-10-01", "2026-09-05", logs, Array.Empty<MembershipReasons.AuditRow>(), NoArchive);
        Assert.Equal("Ko'chib ketdi", r.Label);
        Assert.Equal("moved-away", r.ReasonId);   // KPI «nazoratdan tashqari» shu id bo'yicha
        Assert.Equal("log", r.Source);
    }

    [Fact]
    public void R1_4__eski_tarix__muzlatish_sababi_audit_matnidan_olinadi()
    {
        var logs = new[]
        {
            Log(MembershipReasons.ActionClose, "2026-10-01", null, MembershipReasons.CompletedNotFinishedLabel, "2026-10-01T10:00:00"),
        };
        var audits = new[] { new MembershipReasons.AuditRow("2026-08-20T09:00:00", "Muzlatildi: Ali → A guruh (2026-08-20) — sabab: Kasal") };
        var r = MembershipReasons.Resolve("2026-10-01", "2026-08-20", logs, audits, NoArchive);
        Assert.Equal("Kasal", r.Label);
        Assert.Equal("audit", r.Source);
    }

    [Fact]
    public void R1_4__sinovdagi_yoki_sababsiz_muzlatilgan_azoda_TIZIM_sababi_qoladi()
    {
        var close = Log(MembershipReasons.ActionClose, "2026-10-01", null, MembershipReasons.CompletedNotFinishedLabel, "2026-10-01T10:00:00");
        // Sinov — muzlatilmagan.
        var trial = MembershipReasons.Resolve("2026-10-01", "", new[] { close }, Array.Empty<MembershipReasons.AuditRow>(), NoArchive);
        Assert.Equal(MembershipReasons.CompletedNotFinishedLabel, trial.Label);
        // Muzlatilgan, lekin sababsiz (yozuv yo'q) — tizim sababi zaxira.
        var frozen = MembershipReasons.Resolve("2026-10-01", "2026-09-05", new[] { close }, Array.Empty<MembershipReasons.AuditRow>(), NoArchive);
        Assert.Equal(MembershipReasons.CompletedNotFinishedLabel, frozen.Label);
        // Xuddi o'sha kuni muzlatilgan — tizim yozuvining o'zi "muzlatish sababi" bo'lib qaytmaydi, zaxira baribir o'zi.
        var sameDay = MembershipReasons.Resolve("2026-10-01", "2026-10-01", new[] { close }, Array.Empty<MembershipReasons.AuditRow>(), NoArchive);
        Assert.Equal(MembershipReasons.CompletedNotFinishedLabel, sameDay.Label);
    }

    [Fact]
    public void R1_4__admin_tanlagan_chiqish_sababi_avvalgidek_birinchi()
    {
        var logs = new[]
        {
            Log(MembershipReasons.ActionFreeze, "2026-09-05", "sick", "Kasal", "2026-09-05T10:00:00"),
            Log(MembershipReasons.ActionClose, "2026-10-01", "price", "Narx qimmat", "2026-10-01T10:00:00"),
        };
        var r = MembershipReasons.Resolve("2026-10-01", "2026-09-05", logs, Array.Empty<MembershipReasons.AuditRow>(), NoArchive);
        Assert.Equal("Narx qimmat", r.Label);
    }

    // =====================================================================================
    //  Controller oqimi — MANBA MATNI bo'yicha qulf (Server testga bog'lanmagan)
    // =====================================================================================

    private static string Controller(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        return File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", file));
    }

    /// <summary>Metod tanasi (o'z yopuvchi qavsigacha).</summary>
    private static string Body(string src, string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, $"{signature} topilmadi");
        var open = src.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src[start..(i + 1)];
        }
        throw new InvalidOperationException("tana oxiri topilmadi");
    }

    [Fact]
    public void R1_1__oquvchi_yaratish_va_import_ARXIVDAGI_guruhga_azo_qoshmaydi()
    {
        var src = Controller("StudentsController.cs");
        var create = Body(src, "public async Task<ActionResult<Student>> Create(");
        Assert.Contains("cls is { IsArchived: true }", create);
        Assert.True(create.IndexOf("IsArchived: true", StringComparison.Ordinal)
                    < create.IndexOf("AddStudent(p, cls)", StringComparison.Ordinal));

        var import = Body(src, "public async Task<ActionResult<StudentImportResultDto>> Import(");
        // Yaratish ham, mavjud o'quvchini qo'shish ham — arxiv guruhda ogohlantirish bilan guruhsiz.
        Assert.Equal(2, CountOf(import, "cls is { IsArchived: true }"));
        Assert.Contains("ArchivedGroupMessage(cls)", import);

        // Shablon faqat faol guruhlarni ro'yxatlaydi.
        Assert.Contains("db.Classes.Where(c => !c.IsArchived)", Body(src, "public async Task<IActionResult> ImportTemplate("));

        // Tahrirlash — arxiv guruh yorlig'i ham rad etiladi.
        Assert.Contains("ArchivedGroupMessage(target)", Body(src, "public async Task<IActionResult> Update("));
    }

    [Fact]
    public void R1_9__arxivdagi_oquvchini_guruh_almashtirish_bilan_AKTIVLASHTIRIB_bolmaydi()
    {
        var body = Body(Controller("ClassesController.cs"), "public async Task<IActionResult> TransferMember(");
        var block = body.IndexOf("MembershipBulk.StudentArchivedReason", StringComparison.Ordinal);
        Assert.True(block > 0);
        Assert.True(block < body.IndexOf("MembershipBilling.LeaveGroupAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void R1_10__arxivdagi_oqituvchi_faol_guruhga_biriktirilmaydi()
    {
        var src = Controller("ClassesController.cs");
        Assert.Contains("ArchivedTeacherMessage(teacher)", Body(src, "public async Task<ActionResult<Group>> Create("));
        Assert.Contains("newTeacher.IsArchived && !cls.IsArchived", Body(src, "public async Task<ActionResult<Group>> Update("));
        var unarchive = Body(src, "public async Task<IActionResult> Unarchive(");
        Assert.True(unarchive.IndexOf("ArchivedTeacherMessage", StringComparison.Ordinal)
                    < unarchive.IndexOf("cls.IsArchived = false", StringComparison.Ordinal));
    }

    [Fact]
    public void R1_11__AddMember_qulfni_avto_xabardan_OLDIN_bushatadi()
    {
        var body = Body(Controller("ClassesController.cs"), "public async Task<IActionResult> AddMember(");
        var release = body.IndexOf("legacyGate?.Dispose()", StringComparison.Ordinal);
        Assert.True(release > body.IndexOf("await db.SaveChangesAsync()", StringComparison.Ordinal));
        Assert.True(release < body.IndexOf("autoMsg.DispatchStudentAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void R2_6__arxivdan_qaytarishda_eski_sessiyalar_bekor_qilinadi()
    {
        foreach (var file in new[] { "StudentsController.cs", "TeachersController.cs" })
        {
            var body = Body(Controller(file), "public async Task<IActionResult> Restore(");
            var revoke = body.IndexOf("RefreshTokenService.RevokeAllForUserAsync", StringComparison.Ordinal);
            Assert.True(revoke > 0, file);
            Assert.True(revoke < body.IndexOf("await db.SaveChangesAsync()", StringComparison.Ordinal), file);
        }
    }

    private static int CountOf(string s, string sub)
    {
        var n = 0;
        for (var i = s.IndexOf(sub, StringComparison.Ordinal); i >= 0; i = s.IndexOf(sub, i + sub.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
