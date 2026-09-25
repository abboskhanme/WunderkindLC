using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'RINBOSAR O'QITUVCHILAR — <see cref="SubstituteTeacherService"/> (kirish huquqi, dars sonini
/// oyga taqsimlash va pul) uchun regressiya testlari.
///
/// <para>Har bir test AYNAN bitta topilgan xatoni qo'riqlaydi:
/// <list type="bullet">
///   <item><b>F</b> — oy darslari 28-kunda kesilardi (bitta dars narxi sun'iy KATTA chiqardi);</item>
///   <item><b>H</b> — tayinlov faqat BOSHLANGAN oyiga yozilardi (sentabrda ko'rinmasdi);</item>
///   <item><b>I</b> — kirish huquqi ORALIQ bo'yicha berilardi (tanlanmagan kunlarda ham jurnal ochilardi);</item>
///   <item><b>J</b> — bir kunga ikki marta tayinlash mumkin edi (qo'sh to'lov);</item>
///   <item><b>A/B/D</b> — audit yozuvi bazaga umuman tushmasdi, `action` ruxsatsiz qiymat edi,
///         jumlada GUID turardi;</item>
///   <item><b>C</b> — `substitute_teacher` "Boshqa" bo'limiga tushib qolardi;</item>
///   <item><b>E</b> — maosh raqamlarini har qanday xodim GET bilan o'qiy olardi;</item>
///   <item><b>K1</b> — legacy rejimlarda ushlanma HISOBLANAR, lekin maoshdan AYRILMASDI
///         (markaz bekorga to'lardi) — endi uch rejimda ham NOL YIG'INDILI;</item>
///   <item><b>K2</b> — `ResolveOwnedGroup` o'rinbosarlikni bilmasdi (guruh ro'yxatda bor, ichiga
///         kirsa 403);</item>
///   <item><b>K3</b> — kirish huquqi BUGUNGI sana bo'yicha berilardi (o'tgan istalgan kunni
///         o'zgartirish mumkin edi, tayinlov tugagach esa tuzatib bo'lmasdi);</item>
///   <item><b>J10</b> — o'rinbosar o'tgan dars asosiy o'qituvchini IKKI marta jarimalar edi;</item>
///   <item><b>J11</b> — dars kunlari ikki xil ta'riflangan edi (ko'chirishlar bilan / bilmasdan);</item>
///   <item><b>J12</b> — `MySubstitutions` maosh raqamlarini ruxsatsiz berardi;</item>
///   <item><b>J13</b> — server so'rovda kelgan HAMMA narsani yozardi (buzuq sana, begona kun,
///         chegarasiz sanalar, arxiv guruh, ishdan ketgan o'qituvchi);</item>
///   <item><b>J15</b> — audit `EntityId` guruh tabida topilmasdi;</item>
///   <item><b>J16</b> — ro'yxatda chegara yo'q edi.</item>
/// </list></para>
///
/// <para>2026-yil AVGUSTI ataylab tanlangan: 31 kunlik oy, 31-avgust — DUSHANBA, ya'ni "28-kunda
/// kesish" xatosi natijani ANIQ o'zgartiradi.</para>
/// </summary>
public class SubstituteTeacherTests
{
    // =============================================================================================
    //  Yordamchilar
    // =============================================================================================

    /// <summary>Testlardagi "bugun" — sanalar qat'iy yozilgani uchun vaqt o'tishi bilan
    /// (masalan o'tmish oynasi tekshiruvi) testlar sinmasin.</summary>
    private static readonly DateOnly Bugun = new(2026, 8, 20);

    /// <summary>Dushanba/Chorshanba/Juma darsli guruh. 2026-avgustda 13 dars (31-avgust — dushanba).</summary>
    private static Group NewGroup(string name = "IELTS-3", decimal fee = 500_000m) => new()
    {
        Name = name,
        MonthlyFee = fee,
        TeacherSalaryMode = "percent",
        TeacherSalaryPercent = 50m,
        Days = new List<int> { 0, 2, 4 },   // 0 = dushanba
    };

    private static Teacher NewTeacher(string fullName) => new()
    {
        FullName = fullName,
        SalaryMode = "percent",
        SalaryPercent = 50m,
    };

    private static SubstituteTeacherAssignment NewAssignment(
        Group g, Teacher orig, Teacher sub, List<string>? dates = null,
        string? date = null, string? endDate = null) => new()
    {
        GroupId = g.Id,
        OriginalTeacherId = orig.Id,
        SubstituteTeacherId = sub.Id,
        Date = date ?? dates![0],
        EndDate = endDate ?? dates![^1],
        SelectedDates = dates,
        IsActive = true,
        CreatedBy = "Admin",
    };

    /// <summary>HTTP kontekstsiz <see cref="AuditService"/> (testda so'rov yo'q).</summary>
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    /// <summary>Guruhga N ta faol o'quvchi qo'shadi (o'quvchi yozuvlari bilan).</summary>
    private static void AddStudents(Infrastructure.Data.AppDbContext ctx, Group g, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var s = new Student { FullName = $"O'quvchi {g.Name}-{i}" };
            ctx.Students.Add(s);
            ctx.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active",
                ActivatedAt = "2026-07-01", JoinedAt = "2026-07-01", RecordedAt = "2026-07-01",
            });
        }
    }

    /// <summary>Guruhga SHU OY uchun tuition to'lovi yozadi (foizli maosh bazasi).</summary>
    private static void AddCollected(
        Infrastructure.Data.AppDbContext ctx, Group g, string month, decimal amount)
    {
        var s = ctx.StudentGroups.Local.FirstOrDefault(x => x.GroupId == g.Id)?.StudentId
                ?? ctx.StudentGroups.First(x => x.GroupId == g.Id).StudentId;
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Direction = "income", Category = "tuition",
            StudentId = s, GroupId = g.Id,
            Date = $"{month}-10", Month = month, Amount = amount,
        });
    }

    // =============================================================================================
    //  F — OY DARSLARI 28-KUNDA KESILMAYDI
    // =============================================================================================

    [Fact]
    public void Oyning_oxirgi_kunlaridagi_darslar_ham_sanaladi()
    {
        var g = NewGroup();

        // Dush/Chor/Juma: 3,5,7,10,12,14,17,19,21,24,26,28 va + 31 (dushanba) = 13.
        Assert.Equal(13, SubstituteTeacherService.CalculateScheduledLessons(g, "2026-08-01", "2026-08-31"));

        // Eski kod maxrajni "2026-08-28" gacha olardi — 12 chiqardi.
        Assert.Equal(12, SubstituteTeacherService.CalculateScheduledLessons(g, "2026-08-01", "2026-08-28"));

        // Oy maxraji HAQIQIY oxirgi kunga tayanadi.
        Assert.Equal(13, SubstituteTeacherService.ScheduledLessonsInMonth(g, "2026-08"));
        Assert.Equal("2026-08-31", SubstituteTeacherService.MonthEndDate("2026-08"));
        Assert.Equal("2026-02-28", SubstituteTeacherService.MonthEndDate("2026-02"));
        Assert.Equal("2024-02-29", SubstituteTeacherService.MonthEndDate("2024-02"));   // kabisa yili
    }

    // =============================================================================================
    //  J11 — DARS KUNLARI YAGONA MANBADAN: KO'CHIRILGAN DARS MAXRAJDA TO'G'RI SANALADI
    // =============================================================================================

    [Fact]
    public void Kochirilgan_dars_maxrajda_TOGRI_sanaladi()
    {
        var g = NewGroup();

        // 5-avgust (chorshanba) → 8-avgust (shanba, guruh kuni EMAS) ga ko'chirildi.
        var moves = new List<JournalService.LessonMove> { new("2026-08-05", "2026-08-08") };

        var dates = SubstituteTeacherService.LessonDatesInMonth(g, "2026-08", moves);

        // Dars SONI o'zgarmaydi (bittasi ko'chdi), lekin KUNLAR o'zgaradi.
        Assert.Equal(13, dates.Count);
        Assert.DoesNotContain("2026-08-05", dates);
        Assert.Contains("2026-08-08", dates);

        // ⚠️ Ilgari bu fayl hafta kuni mantig'ini QO'LDA takrorlar va ko'chirishni bilmasdi:
        //    5-avgust maxrajda qolib, 8-avgust umuman yo'q edi — SalaryJournalStats esa
        //    ko'chirishga bo'ysunardi. Ya'ni bitta oyning dars soni ikki xil chiqardi.
        Assert.Equal(
            JournalService.EffectiveLessonDatesInMonth(g.Days, "2026-08", moves),
            dates);

        // Kesishuv tekshiruvi va oraliqdan yaratish ham SHU manbadan.
        Assert.Contains("2026-08-08",
            SubstituteTeacherService.ScheduledDatesBetween(g, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), moves));
    }

    [Fact]
    public void Guruh_yopilgandan_KEYINGI_kunlar_dars_hisoblanmaydi()
    {
        // ⚠️ O17: ilgari `new Group { … }` bilan qisman to'ldirilgan nusxa uzatilar va
        //    ArchivedAt/EndDate/StartDate tashlanardi — arxivlangan guruhda ham darslar
        //    sanalib, o'rinbosarga pul to'lanaverardi.
        var g = NewGroup();
        g.ArchivedAt = "2026-08-14";

        var dates = SubstituteTeacherService.LessonDatesInMonth(g, "2026-08");
        Assert.Contains("2026-08-14", dates);
        Assert.DoesNotContain("2026-08-17", dates);
        Assert.Equal(6, dates.Count);      // 3,5,7,10,12,14

        g.ArchivedAt = null;
        g.StartDate = "2026-08-10";
        Assert.Equal(new[] { "2026-08-10", "2026-08-12", "2026-08-14", "2026-08-17", "2026-08-19",
                             "2026-08-21", "2026-08-24", "2026-08-26", "2026-08-28", "2026-08-31" },
            SubstituteTeacherService.LessonDatesInMonth(g, "2026-08").ToArray());
    }

    // =============================================================================================
    //  PUL — YAGONA HISOBLAGICH (sof funksiya)
    // =============================================================================================

    [Fact]
    public void Bitta_dars_narxi_YIGILGAN_puldan_va_oyning_HAQIQIY_darslaridan()
    {
        var g = NewGroup();                       // guruh foizli, 50%
        var orig = NewTeacher("Asosiy");

        // Yig'ilgan 2 000 000 × 50% = 1 000 000 hovuz; 13 darsga bo'linadi.
        var r = SubstituteTeacherService.PerLesson(new SubstituteTeacherService.SalaryContext(
            Group: g, OriginalTeacher: orig, MonthLessons: 13,
            CollectedInMonth: 2_000_000m, LegacyTotalLessons: 0, ActiveStudents: 4));

        Assert.Equal(SubstituteTeacherService.ModeGroupPercent, r.Mode);
        Assert.Equal(1_000_000m, r.GroupPool);
        Assert.Equal(Math.Round(1_000_000m / 13, 2), r.PerLessonFee);
        Assert.Null(r.Warning);

        // 28-kunda kesilganda maxraj 12 bo'lib, narx SUN'IY ravishda kattaroq chiqardi.
        Assert.True(r.PerLessonFee < Math.Round(1_000_000m / 12, 2));
    }

    [Fact]
    public void Pul_yigilmagan_oyda_haq_0_va_OGOHLANTIRISH_beriladi()
    {
        var g = NewGroup();
        var orig = NewTeacher("Asosiy");

        var yoq = SubstituteTeacherService.PerLesson(new SubstituteTeacherService.SalaryContext(
            g, orig, MonthLessons: 13, CollectedInMonth: 0m, LegacyTotalLessons: 0, ActiveStudents: 4));
        Assert.Equal(0m, yoq.PerLessonFee);
        Assert.NotNull(yoq.Warning);

        // Faol o'quvchi umuman yo'q — boshqa (aniqroq) ogohlantirish.
        var bosh = SubstituteTeacherService.PerLesson(new SubstituteTeacherService.SalaryContext(
            g, orig, MonthLessons: 13, CollectedInMonth: 0m, LegacyTotalLessons: 0, ActiveStudents: 0));
        Assert.Contains("faol o'quvchi", bosh.Warning);
    }

    [Fact]
    public void Uch_maosh_rejimi_UCH_XIL_hovuzdan_hisoblanadi()
    {
        var orig = NewTeacher("Asosiy");
        orig.SalaryMode = "fixed";
        orig.Salary = 6_000_000m;
        orig.SalaryPercent = 40m;

        // (1) Guruh QAT'IY: hovuz = guruhning qat'iy summasi (o'quvchi/tushum qatnashmaydi).
        var qatiy = NewGroup();
        qatiy.TeacherSalaryMode = "fixed";
        qatiy.TeacherSalaryFixed = 2_600_000m;
        var r1 = SubstituteTeacherService.PerLesson(new SubstituteTeacherService.SalaryContext(
            qatiy, orig, 13, CollectedInMonth: 9_999_999m, LegacyTotalLessons: 0, ActiveStudents: 4));
        Assert.Equal(SubstituteTeacherService.ModeGroupFixed, r1.Mode);
        Assert.Equal(200_000m, r1.PerLessonFee);

        // (2) LEGACY-QAT'IY: guruh sozlanmagan, o'qituvchi qat'iy oyliqda.
        //     Oylik BARCHA guruhlarga tegishli → narx = oylik ÷ HAMMA darslar.
        var legacy = NewGroup();
        legacy.TeacherSalaryMode = "";
        var r2 = SubstituteTeacherService.PerLesson(new SubstituteTeacherService.SalaryContext(
            legacy, orig, MonthLessons: 13, CollectedInMonth: 0m, LegacyTotalLessons: 30, ActiveStudents: 4));
        Assert.Equal(SubstituteTeacherService.ModeLegacyFixed, r2.Mode);
        Assert.Equal(Math.Round(6_000_000m / 30, 2), r2.PerLessonFee);

        // (3) LEGACY-FOIZ: guruh sozlanmagan, o'qituvchi foizli.
        orig.SalaryMode = "percent";
        var r3 = SubstituteTeacherService.PerLesson(new SubstituteTeacherService.SalaryContext(
            legacy, orig, 13, CollectedInMonth: 2_000_000m, LegacyTotalLessons: 30, ActiveStudents: 4));
        Assert.Equal(SubstituteTeacherService.ModeLegacyPercent, r3.Mode);
        Assert.Equal(Math.Round(2_000_000m * 0.40m / 13, 2), r3.PerLessonFee);
    }

    [Fact]
    public async Task Royxatdagi_narx_guruhning_YIGILGAN_pulidan_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy Karimov");
        var sub = NewTeacher("O'rinbosar Aliyev");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        AddStudents(ctx, g, 3);
        await ctx.SaveChangesAsync();

        AddCollected(ctx, g, "2026-08", 1_300_000m);
        ctx.SubstituteTeacherAssignments.Add(NewAssignment(g, orig, sub, new List<string> { "2026-08-03", "2026-08-05" }));
        await ctx.SaveChangesAsync();

        var list = await SubstituteTeacherService.GetAssignmentsAsync(ctx);
        var dto = Assert.Single(list);

        // Hovuz = 1 300 000 × 50% = 650 000; 13 dars → 50 000 / dars.
        Assert.Equal(50_000m, dto.PerLessonFee);
        Assert.Equal(2, dto.LessonCount);
        Assert.Equal(100_000m, dto.EstimatedSalary);
        // NOL YIG'INDILI: asosiy o'qituvchidan AYNAN o'shancha ushlanadi.
        Assert.Equal(dto.EstimatedSalary, dto.EstimatedDeduction);
        Assert.Equal(3, dto.StudentCount);
    }

    // =============================================================================================
    //  H — TAYINLOV OYLARGA TO'G'RI BO'LINADI
    // =============================================================================================

    [Fact]
    public void Ikki_oyga_chozilgan_tayinlov_har_oyga_ayrim_sanaladi()
    {
        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");

        // (1) Aniq sanalar tanlangan holat.
        var a = NewAssignment(g, orig, sub, new List<string> { "2026-08-28", "2026-08-31", "2026-09-02" });
        Assert.Equal(2, SubstituteTeacherService.LessonsInMonth(a, g, "2026-08"));
        Assert.Equal(1, SubstituteTeacherService.LessonsInMonth(a, g, "2026-09"));
        Assert.Equal(0, SubstituteTeacherService.LessonsInMonth(a, g, "2026-07"));

        // (2) ORALIQ bilan berilgan (eski) yozuv: 24-avgust — 4-sentabr.
        //     Avgust: 24, 26, 28, 31 = 4; sentabr: 2, 4 = 2.
        var b = NewAssignment(g, orig, sub, dates: null, date: "2026-08-24", endDate: "2026-09-04");
        b.SelectedDates = null;
        Assert.Equal(4, SubstituteTeacherService.LessonsInMonth(b, g, "2026-08"));
        Assert.Equal(2, SubstituteTeacherService.LessonsInMonth(b, g, "2026-09"));

        // Eski kod ikkalasini ham BUTUNLAY avgustga yozar, sentabr maoshida hech narsa ko'rinmasdi.
        Assert.Equal(new[] { "2026-08", "2026-09" }, SubstituteTeacherService.MonthsOf(b, g).ToArray());
    }

    // =============================================================================================
    //  I / K3 — KIRISH HUQUQI: FAQAT TANLANGAN KUNLARDA VA FAQAT TUZATISH OYNASIDA
    // =============================================================================================

    [Fact]
    public async Task Tanlanmagan_kunda_orinbosar_guruhga_KIRA_OLMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);

        // Admin FAQAT 5 va 20-avgustni tanlagan (Date=05, EndDate=20).
        ctx.SubstituteTeacherAssignments.Add(
            NewAssignment(g, orig, sub, new List<string> { "2026-08-05", "2026-08-20" }));
        await ctx.SaveChangesAsync();

        Assert.True(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-05", new DateOnly(2026, 8, 5)));
        Assert.True(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-20", new DateOnly(2026, 8, 20)));

        // ORALIQDAGI kun — begona guruh jurnali OCHILMAYDI (eski kodda true edi).
        Assert.False(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-12", new DateOnly(2026, 8, 12)));
        Assert.False(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-21", new DateOnly(2026, 8, 21)));

        // Guruhlar ro'yxati (o'qituvchi ilovasi) va ro'yxat filtri AYNAN shu qoidada.
        Assert.Empty(await SubstituteTeacherService.SubstituteGroupIdsAsync(ctx, sub.Id, "2026-08-12"));
        Assert.Single(await SubstituteTeacherService.SubstituteGroupIdsAsync(ctx, sub.Id, "2026-08-05"));
        Assert.Empty(await SubstituteTeacherService.GetAssignmentsAsync(ctx, date: "2026-08-12"));
        Assert.Single(await SubstituteTeacherService.GetAssignmentsAsync(ctx, date: "2026-08-20"));
    }

    /// <summary>
    /// K3 — YOZISH huquqi AYNAN SANA bo'yicha: tayinlanmagan kunga yozib bo'lmaydi, o'z darsini
    /// esa <see cref="SubstituteTeacherService.EditWindowDays"/> kun ichida tuzatish MUMKIN.
    /// </summary>
    [Fact]
    public async Task Yozish_huquqi_AYNAN_sana_boyicha_va_tuzatish_oynasida()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        ctx.SubstituteTeacherAssignments.Add(
            NewAssignment(g, orig, sub, new List<string> { "2026-08-17" }));   // dushanba
        await ctx.SaveChangesAsync();

        var darsKuni = new DateOnly(2026, 8, 17);

        // O'sha kuni — ochiq.
        Assert.True(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-17", darsKuni));

        // Tuzatish oynasi ichida (3 kun) — hamon ochiq: "kechqurun jurnalni to'ldirdim".
        Assert.True(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-17", darsKuni.AddDays(SubstituteTeacherService.EditWindowDays)));

        // Oyna tugagach — YOPIQ (ilgari bu ham yopiq edi: tayinlov tugashi bilan).
        Assert.False(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-17", darsKuni.AddDays(SubstituteTeacherService.EditWindowDays + 1)));

        // ⚠️ ENG MUHIMI: tayinlangan kuni ham BOSHQA (o'zi o'tmagan) kunga yozib bo'lmaydi.
        //    Ilgari tekshiruvga sana umuman uzatilmasdi va bu MUMKIN edi.
        Assert.False(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-03", darsKuni));
        Assert.False(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-07-06", darsKuni));

        // Kelajakka ham yozib bo'lmaydi (dars hali o'tilmagan).
        Assert.False(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-17", darsKuni.AddDays(-1)));
    }

    /// <summary>
    /// K2 — o'rinbosar guruhni RO'YXATDA ko'radi VA ichiga kira oladi (o'qish), begona o'qituvchi
    /// esa yo'q. Ro'yxat ko'rish oynasi (kelajakda <see cref="SubstituteTeacherService.UpcomingDays"/>)
    /// bilan ishlaydi — o'rinbosar ertangi darsiga tayyorlana olsin.
    /// </summary>
    [Fact]
    public async Task Orinbosar_guruhni_ROYXATDA_koradi_va_ICHIGA_kira_oladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");
        var begona = NewTeacher("Begona");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub, begona);
        ctx.SubstituteTeacherAssignments.Add(
            NewAssignment(g, orig, sub, new List<string> { "2026-08-19" }));
        await ctx.SaveChangesAsync();

        // Dars kunidan 2 kun OLDIN — guruh ro'yxatda ko'rinadi va ichiga kirsa 403 BO'LMAYDI.
        var oldin = new DateOnly(2026, 8, 17);
        Assert.True(await SubstituteTeacherService.CanSubstituteReadAsync(ctx, sub.Id, g.Id, oldin));
        Assert.Contains(g.Id, await SubstituteTeacherService.SubstituteGroupIdsAsync(ctx, sub.Id, today: oldin));

        // Dars kunidan keyin tuzatish oynasi ichida ham ko'rinadi.
        Assert.True(await SubstituteTeacherService.CanSubstituteReadAsync(ctx, sub.Id, g.Id, new DateOnly(2026, 8, 22)));

        // Oyna tugagach — guruh yo'qoladi.
        Assert.False(await SubstituteTeacherService.CanSubstituteReadAsync(ctx, sub.Id, g.Id, new DateOnly(2026, 9, 1)));

        // BEGONA o'qituvchiga hech qachon ochilmaydi.
        Assert.False(await SubstituteTeacherService.CanSubstituteReadAsync(ctx, begona.Id, g.Id, oldin));
        Assert.Empty(await SubstituteTeacherService.SubstituteGroupIdsAsync(ctx, begona.Id, today: oldin));
    }

    [Fact]
    public async Task Bekor_qilingan_tayinlov_huquq_BERMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        var a = NewAssignment(g, orig, sub, new List<string> { "2026-08-05" });
        a.IsActive = false;
        ctx.SubstituteTeacherAssignments.Add(a);
        await ctx.SaveChangesAsync();

        Assert.False(await SubstituteTeacherService.CanSubstituteWriteAsync(
            ctx, sub.Id, g.Id, "2026-08-05", new DateOnly(2026, 8, 5)));
        Assert.False(await SubstituteTeacherService.CanSubstituteReadAsync(
            ctx, sub.Id, g.Id, new DateOnly(2026, 8, 5)));
    }

    // =============================================================================================
    //  J — KESISHUVCHI TAYINLOV RAD ETILADI (qo'sh to'lov)
    // =============================================================================================

    [Fact]
    public async Task Bir_kunga_ikki_marta_orinbosar_biriktirilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var audit = new AuditService(ctx, new NoHttpContext());

        var g = NewGroup();
        var orig = NewTeacher("Asosiy Karimov");
        var sub1 = NewTeacher("Birinchi O'rinbosar");
        var sub2 = NewTeacher("Ikkinchi O'rinbosar");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub1, sub2);
        await ctx.SaveChangesAsync();

        var (ok1, _, _) = await SubstituteTeacherService.CreateAssignmentAsync(ctx,
            new CreateSubstituteAssignmentRequest(g.Id, sub1.Id, new List<string> { "2026-08-17", "2026-08-19" }),
            "Admin", null, audit, Bugun);
        Assert.True(ok1);

        // 19-avgust BAND — butun so'rov rad etiladi va qaysi kun bandligi aytiladi.
        var (ok2, msg2, entity2) = await SubstituteTeacherService.CreateAssignmentAsync(ctx,
            new CreateSubstituteAssignmentRequest(g.Id, sub2.Id, new List<string> { "2026-08-19", "2026-08-21" }),
            "Admin", null, audit, Bugun);
        Assert.False(ok2);
        Assert.Null(entity2);
        Assert.Contains("19-avgust", msg2);
        Assert.Equal(1, await ctx.SubstituteTeacherAssignments.CountAsync());

        // Kesishmaydigan kunlar — muammosiz.
        var (ok3, _, _) = await SubstituteTeacherService.CreateAssignmentAsync(ctx,
            new CreateSubstituteAssignmentRequest(g.Id, sub2.Id, new List<string> { "2026-08-21" }),
            "Admin", null, audit, Bugun);
        Assert.True(ok3);

        // Bekor qilingan tayinlov joyni BAND QILMAYDI.
        var band = await ctx.SubstituteTeacherAssignments.FirstAsync(x => x.SubstituteTeacherId == sub1.Id);
        await SubstituteTeacherService.CancelAssignmentAsync(ctx, band.Id, audit);
        var (ok4, _, _) = await SubstituteTeacherService.CreateAssignmentAsync(ctx,
            new CreateSubstituteAssignmentRequest(g.Id, sub2.Id, new List<string> { "2026-08-19" }),
            "Admin", null, audit, Bugun);
        Assert.True(ok4);
    }

    // =============================================================================================
    //  J13 — SERVER TEKSHIRUVI: buzuq sana / begona kun / chegara / arxiv guruh / ishdan ketgan
    // =============================================================================================

    [Fact]
    public async Task Server_notogri_sorovni_RAD_etadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        await ctx.SaveChangesAsync();

        async Task<string> Rad(CreateSubstituteAssignmentRequest req)
        {
            var (ok, msg, entity) = await SubstituteTeacherService.CreateAssignmentAsync(
                ctx, req, "Admin", null, null, Bugun);
            Assert.False(ok);
            Assert.Null(entity);
            return msg;
        }

        // (1) BUZUQ SANA — ilgari bazaga "2026-13-99" bo'lib yozilardi.
        Assert.Contains("format", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "2026-13-99" })));
        Assert.Contains("format", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "17.08.2026" })));

        // (2) GURUHNING DARS KUNI EMAS (18-avgust — seshanba, guruh Du/Chor/Juma).
        Assert.Contains("dars yo'q", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "2026-08-18" })));

        // (3) JUDA ESKI SANA — maosh varaqasi allaqachon yopilgan.
        Assert.Contains("eski sana", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "2026-06-01" })));

        // (4) CHEGARADAN ORTIQ SANA — ilgari 1000 ta ham yozilaverardi.
        var kop = SubstituteTeacherService
            .ScheduledDatesBetween(g, new DateOnly(2026, 8, 17), new DateOnly(2027, 6, 30));
        Assert.True(kop.Count > SubstituteTeacherService.MaxDates);
        Assert.Contains($"{SubstituteTeacherService.MaxDates}", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, kop)));

        // (5) O'ZIGA O'ZI o'rinbosar.
        Assert.Contains("o'ziga", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, orig.Id, new List<string> { "2026-08-17" })));

        // (6) ISHDAN KETGAN (arxivlangan) o'rinbosar.
        sub.IsArchived = true;
        await ctx.SaveChangesAsync();
        Assert.Contains("arxivlangan", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "2026-08-17" })));
        sub.IsArchived = false;
        sub.IsBlocked = true;
        await ctx.SaveChangesAsync();
        Assert.Contains("faol emas", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "2026-08-17" })));
        sub.IsBlocked = false;

        // (7) ARXIVLANGAN GURUH.
        g.IsArchived = true;
        await ctx.SaveChangesAsync();
        Assert.Contains("arxivlangan", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "2026-08-17" })));

        // (8) VAQTINCHA BLOKLANGAN GURUH.
        g.IsArchived = false;
        g.IsBlocked = true;
        await ctx.SaveChangesAsync();
        Assert.Contains("bloklangan", await Rad(
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "2026-08-17" })));

        // HECH BIRI bazaga yozilmagan.
        Assert.Equal(0, await ctx.SubstituteTeacherAssignments.CountAsync());
    }

    // =============================================================================================
    //  PREVIEW — modal SERVERDAN hisoblangan raqamni ko'rsatadi
    // =============================================================================================

    [Fact]
    public async Task Preview_serverda_hisoblanadi_va_saqlangani_bilan_MOS()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        AddStudents(ctx, g, 4);
        await ctx.SaveChangesAsync();
        AddCollected(ctx, g, "2026-08", 2_600_000m);
        await ctx.SaveChangesAsync();

        var req = new CreateSubstituteAssignmentRequest(
            g.Id, sub.Id, new List<string> { "2026-08-17", "2026-08-19" });

        var (err, preview) = await SubstituteTeacherService.PreviewAsync(ctx, req, Bugun);
        Assert.Null(err);
        Assert.NotNull(preview);

        // Hovuz = 2 600 000 × 50% = 1 300 000; 13 dars → 100 000/dars; 2 dars → 200 000.
        Assert.Equal(13, preview!.MonthLessons);
        Assert.Equal(2, preview.LessonCount);
        Assert.Equal(100_000m, preview.PerLessonFee);
        Assert.Equal(200_000m, preview.EstimatedSalary);
        Assert.Equal(preview.EstimatedSalary, preview.EstimatedDeduction);   // NOL YIG'INDILI
        Assert.Equal(4, preview.StudentCount);
        Assert.Null(preview.Warning);

        // Saqlangandan keyin ro'yxatdagi raqam AYNAN o'sha (modal boshqa son ko'rsatmaydi).
        var (ok, _, _) = await SubstituteTeacherService.CreateAssignmentAsync(ctx, req, "Admin", null, null, Bugun);
        Assert.True(ok);
        var dto = Assert.Single(await SubstituteTeacherService.GetAssignmentsAsync(ctx));
        Assert.Equal(preview.EstimatedSalary, dto.EstimatedSalary);
        Assert.Equal(preview.PerLessonFee, dto.PerLessonFee);

        // Preview ham AYNAN yaratishdagi tekshiruvdan o'tadi: "modal ruxsat berdi, server rad etdi" bo'lmaydi.
        var (err2, _) = await SubstituteTeacherService.PreviewAsync(ctx,
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, new List<string> { "2026-08-18" }), Bugun);
        Assert.Contains("dars yo'q", err2);
    }

    // =============================================================================================
    //  K1 — NOL YIG'INDILILIK: uch maosh rejimi × (o'rinbosar bor / yo'q)
    // =============================================================================================

    /// <summary>
    /// Bitta stsenariy quriladi va MAOSH VARAQASI ikki marta hisoblanadi: o'rinbosarsiz va
    /// o'rinbosar bilan. Tekshiriladigan narsa — <b>asosiydan ayirilgan summa AYNAN o'rinbosarga
    /// qo'shilgan summaga teng</b> (markaz uchun neytral).
    /// </summary>
    private static async Task NolYigindiliTekshir(
        string nom, Action<Group, Teacher> sozla, decimal collected, decimal charged = 0m)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup($"Guruh-{nom}");
        var orig = NewTeacher("Asosiy Karimov");
        var sub = NewTeacher("O'rinbosar Aliyev");
        sozla(g, orig);
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        AddStudents(ctx, g, 4);
        await ctx.SaveChangesAsync();
        if (collected > 0) AddCollected(ctx, g, "2026-08", collected);
        if (charged > 0)
        {
            // HISOBLANGAN baza (CenterMeta.SalaryChargedBaseFrom): pul KELMAGAN, faqat oylik yozilgan.
            ctx.CenterMeta.Add(new CenterMeta { SalaryChargedBaseFrom = "2026-08" });
            var members = ctx.StudentGroups.Local.Where(x => x.GroupId == g.Id).ToList();
            foreach (var m in members)
                ctx.MonthlyCharges.Add(new MonthlyCharge
                {
                    StudentId = m.StudentId, GroupId = g.Id, Month = "2026-08",
                    Amount = charged / members.Count, Date = "2026-08-01",
                });
        }
        await ctx.SaveChangesAsync();

        // ---------- (a) O'RINBOSARSIZ ----------
        var origOldin = await SalaryLedger.BuildAsync(ctx, orig, "2026-08", "2026-08");
        var subOldin = await SalaryLedger.BuildAsync(ctx, sub, "2026-08", "2026-08");
        var oyOldin = Assert.Single(origOldin.Months);
        Assert.Equal(0m, oyOldin.SubstituteDeduction);
        Assert.Equal(0m, Assert.Single(subOldin.Months).Expected);

        // ---------- (b) O'RINBOSAR 2 DARS O'TDI ----------
        ctx.SubstituteTeacherAssignments.Add(
            NewAssignment(g, orig, sub, new List<string> { "2026-08-17", "2026-08-19" }));
        await ctx.SaveChangesAsync();

        var origKeyin = await SalaryLedger.BuildAsync(ctx, orig, "2026-08", "2026-08");
        var subKeyin = await SalaryLedger.BuildAsync(ctx, sub, "2026-08", "2026-08");
        var oyKeyin = Assert.Single(origKeyin.Months);
        var subOy = Assert.Single(subKeyin.Months);

        // 1) Ushlanma HISOBLANDI va NOLDAN katta (aks holda test hech narsani tekshirmaydi).
        Assert.True(oyKeyin.SubstituteDeduction > 0m,
            $"{nom}: ushlanma hisoblanmadi — stsenariy pulsiz qolgan");

        // 2) O'rinbosarga TO'LANDI va AYNAN o'shancha.
        Assert.Equal(oyKeyin.SubstituteDeduction, subOy.SubstituteFee);

        // 3) ⚠️ K1 — ushlanma HAQIQATAN maoshdan AYRILDI (ilgari legacy rejimlarda faqat
        //    ekranga yuborilar, maosh esa o'zgarmasdi: markaz bekorga to'lardi).
        Assert.Equal(oyOldin.Expected - oyKeyin.SubstituteDeduction, oyKeyin.Expected);

        // 4) O'rinbosarning maoshi AYNAN o'shancha OSHDI.
        Assert.Equal(subOy.SubstituteFee, subOy.Expected);

        // 5) NOL YIG'INDILILIK: markaz jami bir tiyin ham ko'p/kam to'lamaydi.
        Assert.Equal(oyOldin.Expected, oyKeyin.Expected + subOy.Expected);
    }

    [Fact]
    public Task Nol_yigindili_PER_GURUH_foizli() =>
        NolYigindiliTekshir("per-foiz",
            (g, t) => { g.TeacherSalaryMode = "percent"; g.TeacherSalaryPercent = 50m; },
            collected: 2_600_000m);

    [Fact]
    public Task Nol_yigindili_PER_GURUH_qatiy() =>
        NolYigindiliTekshir("per-qatiy",
            (g, t) => { g.TeacherSalaryMode = "fixed"; g.TeacherSalaryFixed = 2_600_000m; },
            collected: 0m);

    [Fact]
    public Task Nol_yigindili_LEGACY_foizli() =>
        NolYigindiliTekshir("legacy-foiz",
            (g, t) => { g.TeacherSalaryMode = ""; t.SalaryMode = "percent"; t.SalaryPercent = 50m; },
            collected: 2_600_000m);

    /// <summary>Hisoblangan bazada (2026-09-25 dan) ham o'rinbosar AYNAN asosiydan ayrilgan summani oladi.</summary>
    [Fact]
    public Task Nol_yigindili_PER_GURUH_foizli_HISOBLANGAN_bazada() =>
        NolYigindiliTekshir("per-foiz-hisob",
            (g, t) => { g.TeacherSalaryMode = "percent"; g.TeacherSalaryPercent = 50m; },
            collected: 0m, charged: 2_600_000m);

    [Fact]
    public Task Nol_yigindili_UMUMIY_foiz_HISOBLANGAN_bazada() =>
        NolYigindiliTekshir("umumiy-hisob",
            (g, t) => { g.TeacherSalaryMode = ""; t.SalaryMode = "percent"; t.SalaryPercent = 40m; },
            collected: 0m, charged: 2_600_000m);

    [Fact]
    public Task Nol_yigindili_LEGACY_qatiy() =>
        NolYigindiliTekshir("legacy-qatiy",
            (g, t) => { g.TeacherSalaryMode = ""; t.SalaryMode = "fixed"; t.Salary = 2_600_000m; },
            collected: 0m);

    // =============================================================================================
    //  J10 — O'RINBOSAR O'TGAN DARS ASOSIYDAN JARIMA OLIB KELMAYDI
    // =============================================================================================

    [Fact]
    public async Task Orinbosar_otgan_dars_asosiyni_IKKI_marta_jarimalamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.Add(orig);
        await ctx.SaveChangesAsync();

        var oy = "2026-07";   // butunlay o'tgan oy — hamma darsning muhlati kelgan
        var groups = new[] { new SalaryJournalStats.GroupInfo(g.Id, g.Name, g.Days, null, null) };

        var hammasi = await SalaryJournalStats.BuildAsync(ctx, groups, oy, oy, 0, null);
        var oldin = hammasi[(oy, g.Id)];
        Assert.True(oldin.Planned > 2);
        Assert.Equal(oldin.Planned, oldin.Missed);   // jurnalda hech narsa belgilanmagan

        // O'rinbosar 2 kun dars o'tdi → o'sha kunlar asosiyning REJASIDAN chiqadi.
        var otgan = new Dictionary<string, HashSet<string>>
        {
            [g.Id] = new() { "2026-07-06", "2026-07-08" },
        };
        var keyin = (await SalaryJournalStats.BuildAsync(ctx, groups, oy, oy, 0, null, otgan))[(oy, g.Id)];

        Assert.Equal(oldin.Planned - 2, keyin.Planned);
        Assert.Equal(oldin.Missed - 2, keyin.Missed);
        Assert.DoesNotContain("2026-07-06", keyin.MissedDates);
        Assert.DoesNotContain("2026-07-08", keyin.MissedDates);
    }

    // =============================================================================================
    //  A / B / D / J15 — AUDIT
    // =============================================================================================

    /// <summary>`.claude/rules/audit.md` §1 — boshqa qiymat yozilmaydi.</summary>
    private static readonly string[] RuxsatEtilganAmallar = { "create", "update", "delete", "complete-and-transfer" };

    [Fact]
    public async Task Audit_yozuvi_HAQIQATAN_bazaga_tushadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var audit = new AuditService(ctx, new NoHttpContext());

        var g = NewGroup("IELTS-3");
        var orig = NewTeacher("Asosiy Karimov");
        var sub = NewTeacher("Aliyev Vali");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        await ctx.SaveChangesAsync();

        var (ok, _, entity) = await SubstituteTeacherService.CreateAssignmentAsync(ctx,
            new CreateSubstituteAssignmentRequest(
                g.Id, sub.Id, new List<string> { "2026-08-17", "2026-08-19" }, Reason: "kasallik"),
            "Admin", null, audit, Bugun);
        Assert.True(ok);

        // ⚠️ Ilgari yozuv servisning SaveChanges'idan KEYIN qo'shilar va bazaga UMUMAN tushmasdi.
        //    Yangi kontekst — ya'ni haqiqatan DISKKA (bazaga) yozilganini tekshiramiz.
        using (var ctx2 = db.NewContext())
        {
            var log = await ctx2.AuditLogs.SingleAsync(x => x.EntityType == SubstituteTeacherService.AuditEntityType);

            Assert.Equal("create", log.Action);
            Assert.Contains(log.Action, RuxsatEtilganAmallar);            // "cancel" — YO'Q
            Assert.Equal(sub.Id, log.TeacherId);

            // J15 — `EntityId` GURUH tabida topilishi uchun "{groupId}:{assignmentId}" bo'lishi SHART
            //       (AuditController: EntityId == groupId || EntityId.StartsWith(groupId + ":")).
            Assert.Equal($"{g.Id}:{entity!.Id}", log.EntityId);
            Assert.StartsWith(g.Id + ":", log.EntityId);

            // D — jumlada GUID emas, O'QILADIGAN ma'lumot bo'lishi shart.
            Assert.Contains("IELTS-3", log.Summary);
            Assert.Contains("Aliyev Vali", log.Summary);
            Assert.Contains("17-avgust", log.Summary);
            Assert.Contains("19-avgust", log.Summary);
            Assert.Contains("kasallik", log.Summary);
            Assert.DoesNotContain(g.Id, log.Summary);
            Assert.DoesNotContain(sub.Id, log.Summary);
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(log.Summary, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-"), "Jumlada GUID qolib ketgan");
        }

        // Bekor qilish ham yoziladi — `action` = "delete" ("cancel" ruxsat etilmagan qiymat edi).
        var (okCancel, _) = await SubstituteTeacherService.CancelAssignmentAsync(ctx, entity!.Id, audit);
        Assert.True(okCancel);

        using (var ctx3 = db.NewContext())
        {
            var loglar = await ctx3.AuditLogs
                .Where(x => x.EntityType == SubstituteTeacherService.AuditEntityType).ToListAsync();
            Assert.Equal(2, loglar.Count);

            var cancel = Assert.Single(loglar, x => x.Action == "delete");
            Assert.Contains(cancel.Action, RuxsatEtilganAmallar);
            Assert.StartsWith(g.Id + ":", cancel.EntityId);
            Assert.Contains("IELTS-3", cancel.Summary);
            Assert.Contains("bekor qilindi", cancel.Summary);
            Assert.DoesNotContain(g.Id, cancel.Summary);
        }
    }

    [Fact]
    public async Task Rad_etilgan_tayinlovda_audit_yozuvi_QOLMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var audit = new AuditService(ctx, new NoHttpContext());

        var (ok, _, _) = await SubstituteTeacherService.CreateAssignmentAsync(ctx,
            new CreateSubstituteAssignmentRequest("yoq-guruh", "yoq-oqituvchi", new List<string> { "2026-08-05" }),
            "Admin", null, audit);

        Assert.False(ok);
        Assert.Equal(0, await ctx.AuditLogs.CountAsync());
    }

    // =============================================================================================
    //  C — BO'LIMGA XARITALASH
    // =============================================================================================

    [Fact]
    public void Audit_yozuvi_OQITUVCHILAR_bolimida_korinadi()
    {
        Assert.Equal("teachers", AuditSections.SectionOf(SubstituteTeacherService.AuditEntityType));
        Assert.Contains(SubstituteTeacherService.AuditEntityType, AuditSections.EntityTypesOf("teachers"));
        Assert.NotEqual(AuditSections.Other, AuditSections.SectionOf(SubstituteTeacherService.AuditEntityType));
    }

    // =============================================================================================
    //  ORALIQ BILAN YARATISH — SelectedDates guruhning HAQIQIY dars kunlari bilan to'ladi
    // =============================================================================================

    [Fact]
    public async Task Oraliq_bilan_yaratilganda_haqiqiy_dars_kunlari_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        await ctx.SaveChangesAsync();

        var (ok, _, entity) = await SubstituteTeacherService.CreateAssignmentAsync(ctx,
            new CreateSubstituteAssignmentRequest(g.Id, sub.Id, Dates: null, Date: "2026-08-17", EndDate: "2026-08-24"),
            "Admin", null, null, Bugun);

        Assert.True(ok);
        // 17, 19, 21, 24 — dush/chor/juma. Ilgari faqat [17, 24] yozilar va "2 dars" deb
        // hisoblanardi, kirish huquqi esa BUTUN oraliqqa (18, 20, 22, 23-avgustga ham) berilardi.
        Assert.Equal(new[] { "2026-08-17", "2026-08-19", "2026-08-21", "2026-08-24" }, entity!.SelectedDates!.ToArray());
        Assert.False(SubstituteTeacherService.CoversDate(entity, "2026-08-18"));
        Assert.True(SubstituteTeacherService.CoversDate(entity, "2026-08-21"));
    }

    // =============================================================================================
    //  J16 — RO'YXAT CHEGARALANGAN VA JAMI SON QAYTADI
    // =============================================================================================

    [Fact]
    public async Task Royxat_chegaralanadi_va_JAMI_son_qaytadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var g = NewGroup();
        var orig = NewTeacher("Asosiy");
        var sub = NewTeacher("O'rinbosar");
        g.TeacherId = orig.Id;
        ctx.Classes.Add(g);
        ctx.Teachers.AddRange(orig, sub);
        for (var i = 0; i < 7; i++)
        {
            var a = NewAssignment(g, orig, sub, new List<string> { "2026-08-17" });
            a.CreatedAt = new DateTime(2026, 8, 1).AddMinutes(i);
            ctx.SubstituteTeacherAssignments.Add(a);
        }
        await ctx.SaveChangesAsync();

        var (items, total) = await SubstituteTeacherService.GetAssignmentsPageAsync(ctx, limit: 3);
        Assert.Equal(3, items.Count);
        // ⚠️ Chegara foydalanuvchidan YASHIRILMAYDI: jami son ham qaytadi ("jami 7, bu yerda 3").
        Assert.Equal(7, total);
        Assert.True(SubstituteTeacherService.MaxRows >= 500);
    }

    // =============================================================================================
    //  E / J12 — MAOSH RAQAMLARI: O'QISH ham ruxsat talab qiladi
    // =============================================================================================

    /// <summary>
    /// Javobda <c>EstimatedSalary</c>/<c>PerLessonFee</c> bor, ya'ni GET odatdagidek har qanday
    /// xodimga ochiq qolmasligi kerak (<c>SensitiveReadPermTests</c> bilan bir xil usul: Tests
    /// loyihasi Server'ga referens qilmaydi, shuning uchun manba matni tekshiriladi).
    /// </summary>
    [Fact]
    public void Orinbosarlar_royxatini_oqish_ruxsat_talab_qiladi()
    {
        var manba = File.ReadAllText(Path.Combine(
            RepoRoot(), "WunderkindLC.Server", "Controllers", "SubstituteTeachersController.cs"));

        Assert.Matches(new System.Text.RegularExpressions.Regex(@"\[AdminPerm\(\s*""teachers\.substitutions""[^\]]*\bReadRequiresPerm\s*=\s*true\b[^\]]*\)\]"), manba);

        // Maosh maydonlari haqiqatan javobda bormi (darvoza SABABI yo'qolsa test qizarsin).
        Assert.NotNull(typeof(SubstituteTeacherAssignmentDto).GetProperty("EstimatedSalary"));
        Assert.NotNull(typeof(SubstituteTeacherAssignmentDto).GetProperty("PerLessonFee"));
        Assert.NotNull(typeof(SubstituteTeacherAssignmentDto).GetProperty("EstimatedDeduction"));
    }

    /// <summary>
    /// J12 — o'qituvchi ilovasidagi <c>GET /api/teacher/substitutions</c> pul maydonlarini
    /// <c>TeacherPermissions.Salary</c> bilan darvozalashi SHART. Filtr
    /// <c>SubstituteTeacherId == me || OriginalTeacherId == me</c> bo'lgani uchun javobda BOSHQA
    /// odamning haqi ham bo'lishi mumkin.
    /// </summary>
    [Fact]
    public void MySubstitutions_maosh_raqamlarini_ruxsatsiz_BERMAYDI()
    {
        var manba = File.ReadAllText(Path.Combine(
            RepoRoot(), "WunderkindLC.Server", "Controllers", "TeacherPortalController.cs"));

        var start = manba.IndexOf("public async Task<IActionResult> MySubstitutions()", StringComparison.Ordinal);
        Assert.True(start > 0, "MySubstitutions endpointi topilmadi");
        var body = manba.Substring(start, Math.Min(1800, manba.Length - start));

        Assert.Contains("TeacherPermissions.Salary", body);
        Assert.Contains("EstimatedSalary = 0m", body);
        Assert.Contains("EstimatedDeduction = 0m", body);
        Assert.Contains("PerLessonFee = 0m", body);
    }

    /// <summary>
    /// K2 — <c>ResolveOwnedGroup</c> o'rinbosarlikni BILISHI shart (natijada
    /// <c>IsSubstitute</c> bayrog'i) va jurnalga yozish darvozalari AYNAN SANA bilan
    /// chaqirilishi kerak (K3).
    /// </summary>
    [Fact]
    public void ResolveOwnedGroup_orinbosarlikni_biladi_va_sana_uzatiladi()
    {
        var manba = File.ReadAllText(Path.Combine(
            RepoRoot(), "WunderkindLC.Server", "Controllers", "TeacherPortalController.cs"));

        // Darvoza o'rinbosarlikni biladi.
        Assert.Contains("bool Owns, bool IsSubstitute)> ResolveOwnedGroup", manba);
        Assert.Contains("CanSubstituteReadAsync", manba);

        // K3: yozish darvozalari AYNAN sana bilan.
        Assert.Contains("Authorized(req.ClassId, req.SubjectId, req.Date)", manba);
        Assert.Contains("Authorized(classId, subjectId, date)", manba);
        Assert.Contains("SubstituteWrite(t.Id, req.ClassId, req.Date)", manba);
        Assert.Contains("SubstituteWrite(t.Id, req.GroupId, req.Date)", manba);

        // Sanasiz `Authorized(...)` chaqiruvi QOLMAGAN bo'lishi kerak.
        Assert.DoesNotContain("Authorized(req.ClassId, req.SubjectId))", manba);
        Assert.DoesNotContain("Authorized(classId, subjectId))", manba);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        return dir!.FullName;
    }
}
