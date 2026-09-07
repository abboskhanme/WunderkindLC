using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// TURNIKET (<see cref="TurnstileService"/>) va CTI/Local Call (<see cref="CtiConnectionManager"/>,
/// <see cref="CtiSmsService"/>) testlari.
///
/// <para>Diqqat: barcha sanalar NISBIY quriladi (<c>AppClock.Today.AddDays(-1)</c> va h.k.) —
/// <c>AppClock</c> statik va inject qilinmaydi, mutlaq sana yozilsa test kelasi oyda yiqilardi.</para>
/// </summary>
public class TurnstileCtiTests
{
    // =============================================================================================
    //  Yordamchilar
    // =============================================================================================

    private static string Iso(DateOnly d, string hhmm) => $"{d:yyyy-MM-dd}T{hhmm}:00";

    private static DateOnly Kecha => AppClock.Today.AddDays(-1);
    private static DateOnly Bugun => AppClock.Today;

    private static void SeedMeta(TestDb db, string workStart = "08:30", int grace = 10, bool enabled = true)
    {
        db.Context.CenterMeta.Add(new CenterMeta
        {
            WorkStartTime = workStart, LateGraceMinutes = grace, TurnstileEnabled = enabled,
        });
        db.Context.SaveChanges();
    }

    private static Teacher AddTeacher(TestDb db, string device, string name = "Dilnoza Karimova")
    {
        var t = new Teacher { FullName = name, DeviceUserId = device };
        db.Context.Teachers.Add(t);
        db.Context.SaveChanges();
        return t;
    }

    private static DateTime Dt(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    // =============================================================================================
    //  1) TURNIKET — xom hodisalarni yozish (IngestAsync)
    // =============================================================================================

    [Fact]
    public async Task Ingest_BoshRoyxat_NolQaytaradi()
    {
        using var db = TestDb.Sqlite();

        Assert.Equal(0, await new TurnstileService().IngestAsync(db.Context, new List<TurnstileService.RawEvent>()));
    }

    [Fact]
    public async Task Ingest_YangiHodisalar_OqituvchigaBiriktiriladi()
    {
        using var db = TestDb.Sqlite();
        var t = AddTeacher(db, "1001");

        var added = await new TurnstileService().IngestAsync(db.Context, new()
        {
            new("1001", Iso(Kecha, "08:25"), "in", "Asosiy eshik"),
            new("1001", Iso(Kecha, "17:10"), "out", "Asosiy eshik"),
        });

        Assert.Equal(2, added);
        var events = await db.Context.TurnstileEvents.AsNoTracking().OrderBy(e => e.EventAt).ToListAsync();
        Assert.All(events, e => Assert.Equal(t.Id, e.TeacherId));
        Assert.Equal(new[] { "in", "out" }, events.Select(e => e.Direction).ToArray());
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.CreatedAt)));
    }

    [Fact]
    public async Task Ingest_NomalumQurilmaId_OqituvchisizYoziladi()
    {
        using var db = TestDb.Sqlite();
        AddTeacher(db, "1001");

        var added = await new TurnstileService().IngestAsync(db.Context, new()
        {
            new("9999", Iso(Kecha, "09:00"), "in", "Eshik"),
        });

        Assert.Equal(1, added);
        Assert.Equal("", (await db.Context.TurnstileEvents.AsNoTracking().SingleAsync()).TeacherId);
    }

    [Fact]
    public async Task Ingest_TakroriySinxronlash_DUBLIKATYARATMAYDI()
    {
        using var db = TestDb.Sqlite();
        AddTeacher(db, "1001");
        var svc = new TurnstileService();
        var raw = new List<TurnstileService.RawEvent>
        {
            new("1001", Iso(Kecha, "08:25"), "in", "Eshik"),
            new("1001", Iso(Kecha, "17:10"), "out", "Eshik"),
        };

        Assert.Equal(2, await svc.IngestAsync(db.Context, raw));
        Assert.Equal(0, await svc.IngestAsync(db.Context, raw));   // qayta sinxronlash

        Assert.Equal(2, await db.Context.TurnstileEvents.CountAsync());
    }

    [Fact]
    public async Task Ingest_BittaChaqiruvIchidagiDublikatlar_BirMartaYoziladi()
    {
        using var db = TestDb.Sqlite();
        AddTeacher(db, "1001");

        var added = await new TurnstileService().IngestAsync(db.Context, new()
        {
            new("1001", Iso(Kecha, "08:25"), "in", "Eshik"),
            new("1001", Iso(Kecha, "08:25"), "in", "Eshik"),   // qurilma ayni hodisani ikki marta berdi
        });

        Assert.Equal(1, added);
    }

    [Fact]
    public async Task Ingest_BirXilVaqt_BoshqaQurilmaId_IkkalasiHamYoziladi()
    {
        using var db = TestDb.Sqlite();

        var added = await new TurnstileService().IngestAsync(db.Context, new()
        {
            new("1001", Iso(Kecha, "08:25"), "in", "Eshik"),
            new("1002", Iso(Kecha, "08:25"), "in", "Eshik"),
        });

        Assert.Equal(2, added);
    }

    [Fact]
    public async Task Ingest_HOZIRGI_XULQ_BoshVaqtliHodisa_HARSINXRONLASHDATAKRORLANADI()
    {
        // HOZIRGI (noto'g'ri) xulq — pastdagi "_KUTILGAN" test tuzatilganda BU test o'chiriladi.
        using var db = TestDb.Sqlite();
        var svc = new TurnstileService();
        var raw = new List<TurnstileService.RawEvent> { new("1001", "", "in", "Eshik") };

        Assert.Equal(1, await svc.IngestAsync(db.Context, raw));
        Assert.Equal(1, await svc.IngestAsync(db.Context, raw));   // dedupe ishlamadi
        Assert.Equal(1, await svc.IngestAsync(db.Context, raw));

        Assert.Equal(3, await db.Context.TurnstileEvents.CountAsync());
    }

    [Fact(Skip = "XATO (TurnstileService.cs:154-155 IngestAsync): dublikat kalitlari bazadan "
                 + "`Where(e => e.EventAt != \"\")` filtri BILAN o'qiladi, ya'ni VAQTI BO'SH hodisalar "
                 + "dedupe to'plamiga umuman tushmaydi. Natijada qurilma vaqtsiz (yoki o'qib bo'lmaydigan "
                 + "vaqtli) hodisa bersa, u HAR sinxronlashda qayta yoziladi — TurnstileEvents jadvali "
                 + "har 20 soniyada (TurnstileLiveService) o'sib boradi va `EventAt.Length >= 16` filtri "
                 + "sababli bu qatorlar hech qachon ishlatilmaydi (foydasiz shishish). "
                 + "Tuzatish: EventAt bo'sh bo'lgan xom hodisa UMUMAN qabul qilinmasin "
                 + "(`if (string.IsNullOrEmpty(r.EventAt)) continue;`), yoki dedupe filtri olib tashlansin.")]
    public async Task Ingest_BoshVaqtliHodisa_QabulQilinmasligiKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        var svc = new TurnstileService();
        var raw = new List<TurnstileService.RawEvent> { new("1001", "", "in", "Eshik") };

        Assert.Equal(0, await svc.IngestAsync(db.Context, raw));
        Assert.Equal(0, await svc.IngestAsync(db.Context, raw));
        Assert.Equal(0, await db.Context.TurnstileEvents.CountAsync());
    }

    [Fact]
    public async Task Ingest_HOZIRGI_XULQ_QurilmaIdKeyinBiriktirilsa_EskiHodisalarBoglanmaydi()
    {
        // HOZIRGI xulq — pastdagi "_KUTILGAN" test tuzatilganda BU test o'chiriladi.
        using var db = TestDb.Sqlite();
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new() { new("1001", Iso(Kecha, "08:25"), "in", "Eshik") });

        // Admin qurilma ID'sini KEYIN biriktirdi:
        var t = AddTeacher(db, "1001");

        var e = await db.Context.TurnstileEvents.AsNoTracking().SingleAsync();
        Assert.Equal("", e.TeacherId);
        Assert.NotEqual(t.Id, e.TeacherId);
    }

    [Fact(Skip = "XATO (TurnstileService.cs:167 IngestAsync + 197-199 RecomputeAsync): TeacherId xom "
                 + "hodisaga FAQAT yozish paytida biriktiriladi va boshqa hech qayerda qayta "
                 + "hisoblanmaydi; RecomputeAsync esa `e.TeacherId != \"\"` bo'yicha filtrlaydi. "
                 + "Natija: admin o'qituvchiga qurilma ID'sini KEYINROQ biriktirsa, undan oldingi "
                 + "barcha o'tishlar davomatga HECH QACHON tushmaydi (o'qituvchi o'sha kunlarda "
                 + "'kelmadi' bo'lib qoladi) — qayta sinxronlash ham yordam bermaydi, chunki hodisalar "
                 + "dublikat sifatida rad etiladi. "
                 + "Tuzatish: RecomputeAsync hodisalarni TeacherId o'rniga DeviceUserId bo'yicha "
                 + "moslasin (o'quvchilar dashboardida allaqachon shunday), yoki qurilma ID biriktirilganda "
                 + "eski hodisalarning TeacherId'si to'ldirilsin.")]
    public async Task Ingest_QurilmaIdKeyinBiriktirilsa_EskiHodisalarHamHisoblanishiKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new() { new("1001", Iso(Kecha, "08:25"), "in", "Eshik") });

        var t = AddTeacher(db, "1001");
        await svc.RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        var att = await db.Context.TeacherAttendances.AsNoTracking().SingleAsync();
        Assert.Equal(t.Id, att.TeacherId);
        Assert.Equal("08:25", att.CheckIn);
    }

    // =============================================================================================
    //  2) TURNIKET — davomatni qayta hisoblash (RecomputeAsync)
    // =============================================================================================

    [Fact]
    public async Task Recompute_BirinchiVaOxirgiOtish_KelganKetganVaqt()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        var t = AddTeacher(db, "1001");
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new()
        {
            new("1001", Iso(Kecha, "12:00"), "out", "Eshik"),
            new("1001", Iso(Kecha, "08:35"), "in", "Eshik"),
            new("1001", Iso(Kecha, "17:10"), "out", "Eshik"),
        });

        var updated = await svc.RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        Assert.Equal(1, updated);
        var att = await db.Context.TeacherAttendances.AsNoTracking().SingleAsync();
        Assert.Equal(t.Id, att.TeacherId);
        Assert.Equal($"{Kecha:yyyy-MM-dd}", att.Date);
        Assert.Equal("08:35", att.CheckIn);     // eng erta o'tish
        Assert.Equal("17:10", att.CheckOut);    // eng kech o'tish
        Assert.Equal("present", att.Status);
        Assert.Equal("turnstile", att.Source);
    }

    [Fact]
    public async Task Recompute_BittaOtish_KetganVaqtBosh()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        AddTeacher(db, "1001");
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new() { new("1001", Iso(Kecha, "08:00"), "in", "Eshik") });

        await svc.RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        var att = await db.Context.TeacherAttendances.AsNoTracking().SingleAsync();
        Assert.Equal("08:00", att.CheckIn);
        Assert.Equal("", att.CheckOut);
    }

    [Fact]
    public async Task Recompute_GraceDanKopKechiksa_Late()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db, workStart: "08:30", grace: 10);
        AddTeacher(db, "1001");
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new() { new("1001", Iso(Kecha, "08:45"), "in", "Eshik") });

        await svc.RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        Assert.Equal("late", (await db.Context.TeacherAttendances.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Recompute_GraceChegarasidaAynan_Present()
    {
        // 08:40 = 08:30 + 10 daqiqa → "> grace" emas → hali kechikish emas.
        using var db = TestDb.Sqlite();
        SeedMeta(db, workStart: "08:30", grace: 10);
        AddTeacher(db, "1001");
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new() { new("1001", Iso(Kecha, "08:40"), "in", "Eshik") });

        await svc.RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        Assert.Equal("present", (await db.Context.TeacherAttendances.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Recompute_IshBoshlanishVaqtiSozlanmagan_HechQachonLateEmas()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db, workStart: "");
        AddTeacher(db, "1001");
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new() { new("1001", Iso(Kecha, "13:00"), "in", "Eshik") });

        await svc.RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        Assert.Equal("present", (await db.Context.TeacherAttendances.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Recompute_OtganKun_HodisaYoq_Absent()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        AddTeacher(db, "1001");

        await new TurnstileService().RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        var att = await db.Context.TeacherAttendances.AsNoTracking().SingleAsync();
        Assert.Equal("absent", att.Status);
        Assert.Equal("", att.CheckIn);
    }

    [Fact]
    public async Task Recompute_BUGUN_HodisaYoq_YozuvYaratilmaydi()
    {
        // Bugun hali kelmagan bo'lishi mumkin — "kelmadi" deb yozib qo'yish noto'g'ri bo'lardi.
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        AddTeacher(db, "1001");

        var updated = await new TurnstileService().RecomputeAsync(db.Context, Dt(Bugun), Dt(Bugun));

        Assert.Equal(0, updated);
        Assert.Empty(await db.Context.TeacherAttendances.ToListAsync());
    }

    [Fact]
    public async Task Recompute_QOLDA_TuzatilganYozuvgaTEGMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db);
        var t = AddTeacher(db, "1001");
        ctx.TeacherAttendances.Add(new TeacherAttendance
        {
            TeacherId = t.Id, Date = $"{Kecha:yyyy-MM-dd}", Status = "present",
            CheckIn = "09:00", Note = "Admin tuzatdi", Source = "manual",
        });
        await ctx.SaveChangesAsync();
        var svc = new TurnstileService();
        await svc.IngestAsync(ctx, new() { new("1001", Iso(Kecha, "11:30"), "in", "Eshik") });

        var updated = await svc.RecomputeAsync(ctx, Dt(Kecha), Dt(Kecha));

        Assert.Equal(0, updated);
        var att = await ctx.TeacherAttendances.AsNoTracking().SingleAsync();
        Assert.Equal("09:00", att.CheckIn);
        Assert.Equal("manual", att.Source);
        Assert.Equal("Admin tuzatdi", att.Note);
    }

    [Fact]
    public async Task Recompute_AvvalgiTurniketYozuvi_USTIGAYoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db);
        var t = AddTeacher(db, "1001");
        ctx.TeacherAttendances.Add(new TeacherAttendance
        {
            TeacherId = t.Id, Date = $"{Kecha:yyyy-MM-dd}", Status = "absent", Source = "turnstile",
        });
        await ctx.SaveChangesAsync();
        var svc = new TurnstileService();
        await svc.IngestAsync(ctx, new() { new("1001", Iso(Kecha, "08:35"), "in", "Eshik") });

        await svc.RecomputeAsync(ctx, Dt(Kecha), Dt(Kecha));

        var att = await ctx.TeacherAttendances.AsNoTracking().SingleAsync();
        Assert.Equal("present", att.Status);
        Assert.Equal("08:35", att.CheckIn);
        Assert.Equal(1, await ctx.TeacherAttendances.CountAsync());   // yangi qator qo'shilmadi
    }

    [Fact]
    public async Task Recompute_IshgaKirishSanasidanOLDINGIKunlar_OtkazibYuboriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db);
        var t = AddTeacher(db, "1001");
        t.SalaryStartDate = $"{Bugun:yyyy-MM-dd}";   // bugundan ishlay boshladi
        await ctx.SaveChangesAsync();

        var updated = await new TurnstileService().RecomputeAsync(ctx, Dt(Kecha.AddDays(-5)), Dt(Kecha));

        Assert.Equal(0, updated);
        Assert.Empty(await ctx.TeacherAttendances.ToListAsync());
    }

    [Fact]
    public async Task Recompute_QurilmaIdBiriktirilmaganOqituvchi_HisobgaOlinmaydi()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        AddTeacher(db, "");   // qurilma ID yo'q

        var updated = await new TurnstileService().RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        Assert.Equal(0, updated);
        Assert.Empty(await db.Context.TeacherAttendances.ToListAsync());
    }

    [Fact]
    public async Task Recompute_ArxivlanganOqituvchi_HisobgaOlinmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db);
        var t = AddTeacher(db, "1001");
        t.IsArchived = true;
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await new TurnstileService().RecomputeAsync(ctx, Dt(Kecha), Dt(Kecha)));
    }

    [Fact]
    public async Task Recompute_KopKunlikOraliq_HarKunUchunAlohidaYozuv()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        AddTeacher(db, "1001");
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new()
        {
            new("1001", Iso(Kecha.AddDays(-2), "08:35"), "in", "Eshik"),
            new("1001", Iso(Kecha, "08:32"), "in", "Eshik"),
        });

        var updated = await svc.RecomputeAsync(db.Context, Dt(Kecha.AddDays(-2)), Dt(Kecha));

        Assert.Equal(3, updated);   // 3 kun: hodisali 2 kun + hodisasiz 1 kun (absent)
        var rows = await db.Context.TeacherAttendances.AsNoTracking().OrderBy(a => a.Date).ToListAsync();
        Assert.Equal(new[] { "present", "absent", "present" }, rows.Select(r => r.Status).ToArray());
    }

    // =============================================================================================
    //  3) TURNIKET — BITTA o'quvchining tarixi (o'quvchi profilidagi tab)
    // =============================================================================================
    //  ⚠️ Ilgari bu yerda "barcha o'quvchilar bir kunda" dashboardi sinalardi. Sahifa olib
    //  tashlangach testlar AYNAN o'sha holatlarni yangi `BuildStudentHistoryAsync` ustida
    //  tekshiradi: qurilma bo'yicha moslash, vaqtlar, o'tishlar soni.

    private static Student AddStudent(TestDb db, string device, string name = "Ali Valiyev", bool archived = false)
    {
        var s = new Student { FullName = name, ClassName = "A1-1", DeviceUserId = device, IsArchived = archived };
        db.Context.Students.Add(s);
        db.Context.SaveChanges();
        return s;
    }

    [Fact]
    public async Task StudentHistory_BirinchiKirishOxirgiChiqish_VaOtishlarSoni()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db);
        var s = AddStudent(db, "S-1");
        await new TurnstileService().IngestAsync(ctx, new()
        {
            new("S-1", Iso(Kecha, "14:55"), "in", "Eshik"),
            new("S-1", Iso(Kecha, "16:05"), "out", "Eshik"),
            new("S-1", Iso(Kecha, "15:30"), "out", "Eshik"),
        });

        var h = await new TurnstileService()
            .BuildStudentHistoryAsync(ctx, s, $"{Kecha:yyyy-MM-dd}", $"{Kecha:yyyy-MM}");

        Assert.Equal("14:55", h.FirstIn);
        Assert.Equal("16:05", h.LastOut);
        Assert.Equal(3, h.Passes);
        // Hodisalar vaqt bo'yicha O'SISH tartibida (jadval xronologik o'qiladi).
        Assert.Equal(new[] { "14:55", "15:30", "16:05" }, h.Events.Select(e => e.Time).ToArray());
        Assert.Equal(new[] { "in", "out", "out" }, h.Events.Select(e => e.Direction).ToArray());
        Assert.Equal(new[] { $"{Kecha:yyyy-MM-dd}" }, h.ActiveDays.ToArray());
        Assert.True(h.Enabled);
        Assert.Equal("S-1", h.DeviceUserId);
    }

    [Fact]
    public async Task StudentHistory_QurilmaIdSizOquvchi_BoshNatija_XatoEmas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = AddStudent(db, "", "Ali");

        var h = await new TurnstileService()
            .BuildStudentHistoryAsync(ctx, s, $"{Kecha:yyyy-MM-dd}", $"{Kecha:yyyy-MM}");

        Assert.Equal("", h.DeviceUserId);
        Assert.Equal(0, h.Passes);
        Assert.Equal("", h.FirstIn);
        Assert.Equal("", h.LastOut);
        Assert.Empty(h.Events);
        Assert.Empty(h.ActiveDays);
        Assert.False(h.Enabled);   // CenterMeta yo'q → o'chiq deb ko'rsatiladi
    }

    [Fact]
    public async Task StudentHistory_BoshqaKundagiHodisa_KunRoyxatidaYoq_LekinFaolKunlarda_Bor()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = AddStudent(db, "S-1", "Ali");
        var oldingi = Kecha.AddDays(-1);
        await new TurnstileService().IngestAsync(ctx, new()
        {
            new("S-1", Iso(oldingi, "09:00"), "in", "Eshik"),
        });

        // Oy — hodisa bo'lgan kunniki: shunda "boshqa kun" kalendarda BELGILANADI, lekin
        // tanlangan kunning ro'yxatiga TUSHMAYDI.
        var h = await new TurnstileService()
            .BuildStudentHistoryAsync(ctx, s, $"{Kecha:yyyy-MM-dd}", $"{oldingi:yyyy-MM}");

        Assert.Equal(0, h.Passes);
        Assert.Empty(h.Events);
        Assert.Contains($"{oldingi:yyyy-MM-dd}", h.ActiveDays);
    }

    [Fact]
    public async Task StudentHistory_BoshqaQurilmaHodisasi_HisoblanmasligiKerak()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = AddStudent(db, "S-1", "Ali");
        await new TurnstileService().IngestAsync(ctx, new()
        {
            new("S-2", Iso(Kecha, "09:00"), "in", "Eshik"),   // BOSHQA o'quvchining qurilmasi
        });

        var h = await new TurnstileService()
            .BuildStudentHistoryAsync(ctx, s, $"{Kecha:yyyy-MM-dd}", $"{Kecha:yyyy-MM}");

        Assert.Equal(0, h.Passes);
        Assert.Empty(h.Events);
        Assert.Empty(h.ActiveDays);
    }

    [Fact]
    public async Task StudentHistory_ArxivlanganOquvchi_TarixiOchiladi()
    {
        // Ilgari arxivlangan o'quvchi RO'YXATdan chiqarilardi. Endi tarix uning PROFILIDA —
        // profil arxivda ham ochiladi, ya'ni tarix ko'rinishi KERAK (yo'qolib qolmasin).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = AddStudent(db, "S-1", "Arxiv", archived: true);
        await new TurnstileService().IngestAsync(ctx, new()
        {
            new("S-1", Iso(Kecha, "10:00"), "in", "Eshik"),
        });

        var h = await new TurnstileService()
            .BuildStudentHistoryAsync(ctx, s, $"{Kecha:yyyy-MM-dd}", $"{Kecha:yyyy-MM}");

        Assert.Equal(1, h.Passes);
        Assert.Equal("10:00", h.FirstIn);
        Assert.Equal("", h.LastOut);   // chiqish hodisasi yo'q
    }

    // =============================================================================================
    //  4) TURNIKET — o'qituvchilar dashboardi
    // =============================================================================================

    [Fact]
    public async Task Dashboard_OtganKun_YozuvYoq_JonliAbsent()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        AddTeacher(db, "1001");

        var dash = await new TurnstileService().BuildDashboardAsync(db.Context, $"{Kecha:yyyy-MM-dd}");

        Assert.Equal("absent", Assert.Single(dash.Rows).Status);
        Assert.Equal(1, dash.Summary.Absent);
        Assert.Equal(0, dash.Summary.NotArrived);
    }

    [Fact]
    public async Task Dashboard_BUGUN_YozuvYoq_HaliKelmadi()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db);
        AddTeacher(db, "1001");

        var dash = await new TurnstileService().BuildDashboardAsync(db.Context, $"{Bugun:yyyy-MM-dd}");

        Assert.Equal("", Assert.Single(dash.Rows).Status);
        Assert.Equal(1, dash.Summary.NotArrived);
        Assert.Equal(0, dash.Summary.Absent);
    }

    [Fact]
    public async Task Dashboard_Kechikish_DaqiqalarHisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db, workStart: "08:30");
        var t = AddTeacher(db, "1001");
        ctx.TeacherAttendances.Add(new TeacherAttendance
        {
            TeacherId = t.Id, Date = $"{Kecha:yyyy-MM-dd}", Status = "late",
            CheckIn = "08:45", CheckOut = "17:00", Source = "turnstile",
        });
        await ctx.SaveChangesAsync();

        var row = Assert.Single((await new TurnstileService()
            .BuildDashboardAsync(ctx, $"{Kecha:yyyy-MM-dd}")).Rows);

        Assert.Equal("08:30", row.Expected);
        Assert.Equal(15, row.LateMinutes);
        Assert.Equal("08:45", row.CheckIn);
        Assert.Equal("turnstile", row.Source);
    }

    [Fact]
    public async Task Dashboard_ErtaKelgan_KechikishNOLBolishiKerak_KUTILGAN()
    {
        // 08:00 kutilgan 08:30 dan 30 daqiqa ERTA → kechikish manfiy → dashboardda 0 ko'rsatiladi
        // (ilgari TimeOnly ayirmasi yarim tunda aylanib 1410 daqiqa berardi).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db, workStart: "08:30");
        var t = AddTeacher(db, "1001");
        ctx.TeacherAttendances.Add(new TeacherAttendance
        {
            TeacherId = t.Id, Date = $"{Kecha:yyyy-MM-dd}", Status = "late", CheckIn = "08:00",
        });
        await ctx.SaveChangesAsync();

        Assert.Equal(0, Assert.Single((await new TurnstileService()
            .BuildDashboardAsync(ctx, $"{Kecha:yyyy-MM-dd}")).Rows).LateMinutes);
    }

    [Fact]
    public async Task Recompute_ErtaKelganOqituvchi_PresentBolishiKerak_KUTILGAN()
    {
        // 30 daqiqa ERTA kelgan o'qituvchi "present" bo'lishi shart (MinutesBetween manfiy qaytaradi).
        using var db = TestDb.Sqlite();
        SeedMeta(db, workStart: "08:30", grace: 10);
        AddTeacher(db, "1001");
        var svc = new TurnstileService();
        await svc.IngestAsync(db.Context, new() { new("1001", Iso(Kecha, "08:00"), "in", "Eshik") });

        await svc.RecomputeAsync(db.Context, Dt(Kecha), Dt(Kecha));

        var att = await db.Context.TeacherAttendances.AsNoTracking().SingleAsync();
        Assert.Equal("08:00", att.CheckIn);
        Assert.Equal("present", att.Status);
    }

    [Fact]
    public async Task Dashboard_IshgaKirmaganOqituvchi_SanoqqaTushmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db);
        var t = AddTeacher(db, "1001");
        t.SalaryStartDate = $"{Bugun:yyyy-MM-dd}";
        await ctx.SaveChangesAsync();

        var dash = await new TurnstileService().BuildDashboardAsync(ctx, $"{Kecha:yyyy-MM-dd}");

        Assert.Equal("", Assert.Single(dash.Rows).Status);
        Assert.Equal(0, dash.Summary.Absent);
        Assert.Equal(0, dash.Summary.NotArrived);
    }

    [Fact]
    public async Task Dashboard_HOZIRGI_XULQ_BirKunUchunIKKIDAVOMATYOZUVI_YIQITADI()
    {
        // HOZIRGI (noto'g'ri) xulq — pastdagi "_KUTILGAN" test tuzatilganda BU test o'chiriladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db);
        var t = AddTeacher(db, "1001");
        var date = $"{Kecha:yyyy-MM-dd}";
        ctx.TeacherAttendances.Add(new TeacherAttendance { TeacherId = t.Id, Date = date, Status = "present" });
        ctx.TeacherAttendances.Add(new TeacherAttendance { TeacherId = t.Id, Date = date, Status = "absent" });
        await ctx.SaveChangesAsync();   // baza qabul qildi — unikal indeks YO'Q

        await Assert.ThrowsAsync<ArgumentException>(
            () => new TurnstileService().BuildDashboardAsync(ctx, date));
    }

    [Fact(Skip = "XATO (AppDbContext.cs — TeacherAttendance uchun indeks umuman yo'q; "
                 + "TurnstileService.cs:29 va 205-207 `ToDictionary(a => a.TeacherId ...)`): "
                 + "(TeacherId, Date) juftligida UNIKAL indeks yo'q, shuning uchun baza bir kunga ikkita "
                 + "davomat yozuvini qabul qiladi (parallel so'rov, qo'lda kiritish, sinxronlash poygasi). "
                 + "Bunday holatda ToDictionary ArgumentException tashlaydi va davomat dashboardi hamda "
                 + "RecomputeAsync BUTUNLAY ishdan chiqadi (500) — qo'lda bazani tozalamaguncha tuzalmaydi. "
                 + "Tuzatish: `b.Entity<TeacherAttendance>().HasIndex(a => new { a.TeacherId, a.Date })"
                 + ".IsUnique()` migratsiyasi + ToDictionary o'rniga guruhlab birinchisini olish.")]
    public async Task Dashboard_BirKunUchunIkkiYozuv_BOLMASLIGIKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedMeta(db);
        var t = AddTeacher(db, "1001");
        var date = $"{Kecha:yyyy-MM-dd}";
        ctx.TeacherAttendances.Add(new TeacherAttendance { TeacherId = t.Id, Date = date, Status = "present" });
        ctx.TeacherAttendances.Add(new TeacherAttendance { TeacherId = t.Id, Date = date, Status = "absent" });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // =============================================================================================
    //  5) TURNIKET — sinxronlash darvozasi (SyncAsync)
    // =============================================================================================

    [Fact]
    public async Task Sync_CenterMetaYoq_XatoMatniQaytaradi()
    {
        using var db = TestDb.Sqlite();

        var res = await new TurnstileService().SyncAsync(db.Context);

        Assert.False(res.Ok);
        Assert.Contains("yoqilmagan", res.Message);
        Assert.Equal(0, res.EventsFetched);
    }

    [Fact]
    public async Task Sync_Ochirilgan_QurilmagaSorovYubormaydi()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db, enabled: false);
        db.Context.CenterMeta.Single().TurnstileHost = "10.0.0.5";
        await db.Context.SaveChangesAsync();

        var res = await new TurnstileService().SyncAsync(db.Context);

        Assert.False(res.Ok);
        Assert.Contains("yoqilmagan", res.Message);
    }

    [Fact]
    public async Task Sync_HostKiritilmagan_XatoMatniQaytaradi()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db, enabled: true);

        var res = await new TurnstileService().SyncAsync(db.Context);

        Assert.False(res.Ok);
        Assert.Contains("host", res.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sync_ZKTeco_QollabQuvvatlanmaydi_LekinYiqilmaydi()
    {
        using var db = TestDb.Sqlite();
        SeedMeta(db, enabled: true);
        var meta = db.Context.CenterMeta.Single();
        meta.TurnstileHost = "10.0.0.5";
        meta.TurnstileVendor = "ZKTeco";     // katta-kichik harf farq qilmasin
        await db.Context.SaveChangesAsync();

        var res = await new TurnstileService().SyncAsync(db.Context);

        Assert.False(res.Ok);
        Assert.Contains("ZKTeco", res.Message);
        Assert.Equal("", res.LastSync);      // muvaffaqiyatsizlikda LastSync yangilanmaydi
    }

    // =============================================================================================
    //  6) CTI — WebSocket ulanish menejeri
    // =============================================================================================

    /// <summary>Minimal soxta <see cref="WebSocket"/> (tashqi mock kutubxonasisiz).</summary>
    private sealed class FakeSocket(WebSocketState state = WebSocketState.Open, bool throwOnSend = false)
        : WebSocket
    {
        private WebSocketState _state = state;

        public List<string> Sent { get; } = new();
        public bool Aborted { get; private set; }
        public bool Closed { get; private set; }

        public void SetState(WebSocketState s) => _state = s;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() { Aborted = true; _state = WebSocketState.Aborted; }
        public override void Dispose() { }

        public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken ct)
        {
            Closed = true;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) =>
            Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer, CancellationToken ct) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

        public override Task SendAsync(
            ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct)
        {
            if (throwOnSend) throw new WebSocketException("ulanish uzildi");
            Sent.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Conn_NomalumAgent_UlanmaganHisoblanadi()
    {
        var conn = new CtiConnectionManager();

        Assert.False(conn.IsConnected("yoq-agent"));
    }

    [Fact]
    public void Conn_QoshilganAgent_Ulangan()
    {
        var conn = new CtiConnectionManager();
        conn.AddOrReplace("a1", new FakeSocket());

        Assert.True(conn.IsConnected("a1"));
        Assert.False(conn.IsConnected("a2"));
    }

    [Fact]
    public void Conn_SocketYopilgan_UlanmaganHisoblanadi()
    {
        var conn = new CtiConnectionManager();
        var sock = new FakeSocket();
        conn.AddOrReplace("a1", sock);
        sock.SetState(WebSocketState.Closed);

        Assert.False(conn.IsConnected("a1"));
    }

    [Fact]
    public void Conn_YangiUlanish_EskisiniYopadi()
    {
        var conn = new CtiConnectionManager();
        var eski = new FakeSocket();
        var yangi = new FakeSocket();

        conn.AddOrReplace("a1", eski);
        conn.AddOrReplace("a1", yangi);

        Assert.True(eski.Closed);
        Assert.True(conn.IsConnected("a1"));
    }

    [Fact]
    public void Conn_YangiUlanish_YopiqEskisiniAbortQiladi()
    {
        var conn = new CtiConnectionManager();
        var eski = new FakeSocket(WebSocketState.Aborted);
        conn.AddOrReplace("a1", eski);

        conn.AddOrReplace("a1", new FakeSocket());

        Assert.True(eski.Aborted);
    }

    [Fact]
    public void Conn_Remove_FaqatAYNANShuSocketniOlibTashlaydi()
    {
        var conn = new CtiConnectionManager();
        var eski = new FakeSocket();
        var yangi = new FakeSocket();
        conn.AddOrReplace("a1", eski);
        conn.AddOrReplace("a1", yangi);

        conn.Remove("a1", eski);          // almashtirilgan eski ulanish uzildi

        Assert.True(conn.IsConnected("a1"));   // yangi ulanish tirik qoldi

        conn.Remove("a1", yangi);
        Assert.False(conn.IsConnected("a1"));
    }

    [Fact]
    public async Task Conn_Send_NomalumAgent_False()
    {
        var conn = new CtiConnectionManager();

        Assert.False(await conn.SendAsync("yoq", new { action = "dial" }));
    }

    [Fact]
    public async Task Conn_Send_YopiqSocket_False()
    {
        var conn = new CtiConnectionManager();
        var sock = new FakeSocket(WebSocketState.Closed);
        conn.AddOrReplace("a1", sock);

        Assert.False(await conn.SendAsync("a1", new { action = "dial" }));
        Assert.Empty(sock.Sent);
    }

    [Fact]
    public async Task Conn_Send_JsonCamelCaseYuboriladi()
    {
        var conn = new CtiConnectionManager();
        var sock = new FakeSocket();
        conn.AddOrReplace("a1", sock);

        Assert.True(await conn.SendAsync("a1", new { action = "send_sms", to = "+998901234567", commandId = "c1" }));

        var json = Assert.Single(sock.Sent);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("send_sms", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal("+998901234567", doc.RootElement.GetProperty("to").GetString());
        Assert.Equal("c1", doc.RootElement.GetProperty("commandId").GetString());
    }

    [Fact]
    public async Task Conn_Send_XatoBolsa_FalseVaUlanishTozalanadi()
    {
        var conn = new CtiConnectionManager();
        conn.AddOrReplace("a1", new FakeSocket(throwOnSend: true));

        Assert.False(await conn.SendAsync("a1", new { action = "dial" }));
        Assert.False(conn.IsConnected("a1"));   // buzilgan ulanish ro'yxatdan olib tashlandi
    }

    // =============================================================================================
    //  7) CTI — Local SMS (CtiSmsService)
    // =============================================================================================

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static CtiSmsService Sms(CtiConnectionManager conn) =>
        new(conn, new FcmService(new FakeHttpClientFactory(), NullLogger<FcmService>.Instance),
            NullLogger<CtiSmsService>.Instance);

    /// <summary>Oflayn agent (WS yo'q, FCM token yo'q) — yuborish darhol "yetkazilmadi" bo'ladi,
    /// lekin NormalizePhone natijasi CtiCommandLog.Payload orqali ko'rinadi.</summary>
    private static CtiAgent AddAgent(TestDb db, string login = "agent1")
    {
        var a = new CtiAgent { Login = login, DisplayName = "Operator", FcmToken = "" };
        db.Context.CtiAgents.Add(a);
        db.Context.SaveChanges();
        return a;
    }

    private static async Task<string> NormalizedNumberAsync(TestDb db, string raw)
    {
        // Har chaqiriqda tarixni tozalab olamiz — natija AYNAN shu yuborishga tegishli bo'lsin.
        db.Context.CtiCommandLogs.RemoveRange(db.Context.CtiCommandLogs);
        await db.Context.SaveChangesAsync();

        var agent = await db.Context.CtiAgents.FirstAsync();
        await Sms(new CtiConnectionManager())
            .SendSmsAsync(db.Context, agent.Id, raw, "Salom");
        var cmd = await db.Context.CtiCommandLogs.AsNoTracking().SingleAsync();
        return cmd.Payload.Split(':')[0];
    }

    [Fact]
    public async Task Sms_AgentTanlanmagan_XatoVaTarixgaYoziladi()
    {
        using var db = TestDb.Sqlite();

        var res = await Sms(new CtiConnectionManager())
            .SendSmsAsync(db.Context, null, "998901234567", "Salom");

        Assert.False(res.Ok);
        Assert.Contains("Standart Local SMS agent tanlanmagan", res.Error!);
        Assert.Equal("yetkazilmadi", res.Status);
        var log = await db.Context.SmsLogs.AsNoTracking().SingleAsync();
        Assert.Equal("local", log.Provider);
        Assert.Equal("yetkazilmadi", log.Status);
    }

    [Fact]
    public async Task Sms_AgentTopilmadi_Xato()
    {
        using var db = TestDb.Sqlite();

        var res = await Sms(new CtiConnectionManager())
            .SendSmsAsync(db.Context, "yoq-agent", "998901234567", "Salom");

        Assert.False(res.Ok);
        Assert.Contains("agent topilmadi", res.Error!);
    }

    [Fact]
    public async Task Sms_RaqamdaRaqamYoq_Xato()
    {
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);

        var res = await Sms(new CtiConnectionManager())
            .SendSmsAsync(db.Context, agent.Id, "salom-telefon-emas", "Matn");

        Assert.False(res.Ok);
        Assert.Contains("Telefon raqami noto'g'ri", res.Error!);
        Assert.Empty(await db.Context.CtiCommandLogs.ToListAsync());   // buyruq umuman yaratilmadi
    }

    [Fact]
    public async Task Sms_OflaynAgent_YetkazilmadiVaBuyruqFailed()
    {
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);

        var res = await Sms(new CtiConnectionManager())
            .SendSmsAsync(db.Context, agent.Id, "901234567", "Salom");

        Assert.False(res.Ok);
        Assert.Contains("oflayn", res.Error!);
        Assert.Equal("failed", (await db.Context.CtiCommandLogs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("send_sms", (await db.Context.CtiCommandLogs.AsNoTracking().SingleAsync()).Action);
    }

    [Fact]
    public async Task Sms_UlanganAgent_WSOrqaliYuboriladi()
    {
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);
        var conn = new CtiConnectionManager();
        var sock = new FakeSocket();
        conn.AddOrReplace(agent.Id, sock);

        var res = await Sms(conn).SendSmsAsync(db.Context, agent.Id, "901234567", "Salom", "Ali Valiyev");

        Assert.True(res.Ok);
        Assert.Equal("yuborildi", res.Status);
        Assert.Null(res.Error);
        Assert.Equal("sent", (await db.Context.CtiCommandLogs.AsNoTracking().SingleAsync()).Status);

        using var doc = JsonDocument.Parse(Assert.Single(sock.Sent));
        Assert.Equal("send_sms", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal("+998901234567", doc.RootElement.GetProperty("to").GetString());
        Assert.Equal("Salom", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal(res.CommandId, doc.RootElement.GetProperty("commandId").GetString());
    }

    [Fact]
    public async Task Sms_BatchIdBerilmasa_BittalikSmsBatchYaratiladi()
    {
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);
        var conn = new CtiConnectionManager();
        conn.AddOrReplace(agent.Id, new FakeSocket());

        await Sms(conn).SendSmsAsync(db.Context, agent.Id, "901234567", "Salom", "Ali Valiyev");

        var batch = await db.Context.SmsBatches.AsNoTracking().SingleAsync();
        Assert.Equal("local", batch.Provider);
        Assert.Equal("Local Call", batch.SenderName);
        Assert.Equal("Ali Valiyev", batch.Audience);
        Assert.Equal(1, batch.RecipientCount);
        Assert.Equal(1, batch.SentCount);
        Assert.Equal(batch.Id, (await db.Context.SmsLogs.AsNoTracking().SingleAsync()).BatchId);
    }

    [Fact]
    public async Task Sms_BatchIdBerilsa_YangiBatchYaratilmaydi()
    {
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);

        await Sms(new CtiConnectionManager())
            .SendSmsAsync(db.Context, agent.Id, "901234567", "Salom", batchId: "tashqi-partiya");

        Assert.Empty(await db.Context.SmsBatches.ToListAsync());
        Assert.Equal("tashqi-partiya", (await db.Context.SmsLogs.AsNoTracking().SingleAsync()).BatchId);
    }

    [Fact]
    public async Task Sms_OluvchiNomiBoshBolsa_AudienceTelefon()
    {
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);

        await Sms(new CtiConnectionManager()).SendSmsAsync(db.Context, agent.Id, "901234567", "Salom");

        Assert.Equal("901234567", (await db.Context.SmsBatches.AsNoTracking().SingleAsync()).Audience);
    }

    [Fact]
    public async Task Sms_StandartAgent_CenterMetadanOlinadi()
    {
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);
        db.Context.CenterMeta.Add(new CenterMeta { LocalSmsDefaultAgentId = agent.Id });
        await db.Context.SaveChangesAsync();

        var res = await Sms(new CtiConnectionManager())
            .SendSmsAsync(db.Context, agentId: null, "901234567", "Avto xabar");

        Assert.False(res.Ok);                       // agent oflayn — lekin AGENT TOPILDI
        Assert.Contains("oflayn", res.Error!);
        Assert.Equal(agent.Id, (await db.Context.CtiCommandLogs.AsNoTracking().SingleAsync()).AgentId);
    }

    // ---------- Telefon normalizatsiyasi (CtiSmsService.NormalizePhone) ----------

    [Fact]
    public async Task Telefon_XalqaroFormatgaKeltiriladi()
    {
        using var db = TestDb.Sqlite();
        AddAgent(db);

        Assert.Equal("+998901234567", await NormalizedNumberAsync(db, "901234567"));
        Assert.Equal("+998901234567", await NormalizedNumberAsync(db, "+998 90 123 45 67"));
        Assert.Equal("+998901234567", await NormalizedNumberAsync(db, "998901234567"));
        Assert.Equal("+998901234567", await NormalizedNumberAsync(db, "(90) 123-45-67"));
        Assert.Equal("+998901234567", await NormalizedNumberAsync(db, "0901234567"));   // shahar "0" olib tashlanadi
    }

    [Fact]
    public async Task Telefon_PhoneUtilBilanBirXilRaqamniTopadi()
    {
        // CtiSmsService'da MUSTAQIL NormalizePhone bor. Format boshqacha (PhoneUtil chiziqcha
        // qo'yadi, CTI — E.164), lekin O'ZBEK raqamlarida ikkalasi AYNAN bir raqamni ko'rsatishi shart.
        using var db = TestDb.Sqlite();
        AddAgent(db);

        foreach (var raw in new[] { "901234567", "+998 90 123 45 67", "998901234567", "0901234567" })
        {
            var cti = await NormalizedNumberAsync(db, raw);
            Assert.Equal("+998" + PhoneUtil.Key(raw), cti);
        }
    }

    [Fact]
    public void Telefon_PhoneUtilFormatiCTIdanFARQQILADI()
    {
        // Ikki formatni chalkashtirmaslik uchun qulflab qo'yildi: PhoneUtil — ko'rsatish uchun
        // (chiziqchali), CTI — qurilmaga yuborish uchun (E.164). SmsLog.PhoneNumber esa XOM
        // (normalizatsiyasiz) raqamni saqlaydi.
        Assert.Equal("+998-90-123-45-67", PhoneUtil.Normalize("901234567"));
        Assert.Equal("901234567", PhoneUtil.Key("+998 90 123 45 67"));
    }

    [Fact]
    public async Task Sms_HOZIRGI_XULQ_SmsLogXOMRaqamniSaqlaydi()
    {
        // Yuborilgan raqam "+998901234567" bo'lsa ham, tarixda "90 123 45 67" ko'rinishida qoladi —
        // Eskiz yozuvlari bilan solishtirish/qidiruv shu sabab bir xil emas.
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);
        var conn = new CtiConnectionManager();
        conn.AddOrReplace(agent.Id, new FakeSocket());

        await Sms(conn).SendSmsAsync(db.Context, agent.Id, "90 123 45 67", "Salom");

        Assert.Equal("90 123 45 67", (await db.Context.SmsLogs.AsNoTracking().SingleAsync()).PhoneNumber);
        Assert.StartsWith("+998901234567:",
            (await db.Context.CtiCommandLogs.AsNoTracking().SingleAsync()).Payload);
    }

    [Fact]
    public async Task Sms_HOZIRGI_XULQ_JUDAQISQARAQAMHAMQABULQILINADI()
    {
        // HOZIRGI (noto'g'ri) xulq — pastdagi "_KUTILGAN" test tuzatilganda BU test o'chiriladi.
        using var db = TestDb.Sqlite();
        AddAgent(db);

        Assert.Equal("+12345", await NormalizedNumberAsync(db, "12345"));
        Assert.Equal("+7", await NormalizedNumberAsync(db, "7"));
        Assert.Equal("+998", await NormalizedNumberAsync(db, "998"));
    }

    [Fact(Skip = "XATO (CtiSmsService.cs:152-160 NormalizePhone): raqam UZUNLIGI umuman "
                 + "tekshirilmaydi — bitta raqam ham (\"7\" → \"+7\") xalqaro raqam deb qabul qilinadi va "
                 + "SendSmsAsync uni agentga jo'natadi, SmsLog'da esa \"yuborildi\" bo'lib qoladi "
                 + "(mijoz SMS olmagan bo'lsa ham). Loyihada bunday tekshiruv allaqachon bor: "
                 + "PhoneUtil.Validate — kamida 7 raqam. "
                 + "Tuzatish: NormalizePhone o'chirilib PhoneUtil ishlatilsin (Validate bilan darvoza, "
                 + "so'ng E.164 ga o'girish), toki dial va SMS bitta qoidadan yursin.")]
    public async Task Sms_JudaQisqaRaqam_RadEtilishiKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        var agent = AddAgent(db);

        var res = await Sms(new CtiConnectionManager())
            .SendSmsAsync(db.Context, agent.Id, "12345", "Salom");

        Assert.False(res.Ok);
        Assert.Contains("Telefon raqami noto'g'ri", res.Error!);
        Assert.Empty(await db.Context.CtiCommandLogs.ToListAsync());
    }
}
