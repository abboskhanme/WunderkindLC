using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// JURNAL SIYOSATI — kim, qachon va qaysi darsga yoza oladi:
/// <list type="bullet">
///   <item><see cref="JournalPolicy.PaymentGate"/> — to'lov "darvozasi" (qarzdor o'quvchi
///   o'qituvchi jurnalida ko'rinmaydi). SOF funksiya: hech qanday so'rov yo'q.</item>
///   <item><see cref="JournalPolicy.CheckAsync"/> — tahrirlash rejimlari ("today"/"window"),
///   "faqat o'tilgan darsga" sharti va admin istisnosi.</item>
///   <item><see cref="JournalService"/> ning sof yordamchilari — dars sanalari, ko'chirish,
///   a'zolik boshlanish sanasi.</item>
/// </list>
/// <para>DIQQAT: <c>AppClock</c> statik — sanalar HAR DOIM nisbiy quriladi
/// (<c>AppClock.Today.AddDays(-1)</c>), mutlaq yozilmaydi.</para>
/// </summary>
public class JournalPolicyTests
{
    private static string D(int offsetDays) => AppClock.Today.AddDays(offsetDays).ToString("yyyy-MM-dd");

    private static JournalPolicyDto Policy(
        bool prevMonth = false, bool afterDay = false, int cutoff = 10) =>
        new("free", 3, false, false, false, 0, prevMonth, afterDay, cutoff);

    /* =========================================================================================
     *  1) TO'LOV "DARVOZASI" — PaymentGate (sof)
     * ========================================================================================= */

    [Fact]
    public void PaymentGate_OtganOyQarzi_Yashiriladi()
    {
        // Eng eski qarz oyi joriy oydan OLDIN → sabab "prevMonth" (kun raqamiga bog'liq emas).
        var b = new GroupBalanceService.GroupBalanceInfo(-500_000m, 2, "2026-06", true);

        var (hidden, reason) = JournalPolicy.PaymentGate(Policy(prevMonth: true), b, "2026-07", 1);

        Assert.True(hidden);
        Assert.Equal("prevMonth", reason);
    }

    [Fact]
    public void PaymentGate_QarzJORIYoyda_OtganOySozlamasiIshlamaydi()
    {
        // Qarz faqat joriy oyda — "o'tgan oy qarzi" qoidasi tegmaydi.
        var b = new GroupBalanceService.GroupBalanceInfo(-500_000m, 1, "2026-07", true);

        var (hidden, reason) = JournalPolicy.PaymentGate(Policy(prevMonth: true), b, "2026-07", 28);

        Assert.False(hidden);
        Assert.Equal("", reason);
    }

    [Fact]
    public void PaymentGate_JoriyOyQarzi_KunKelmagan_Korinadi()
    {
        // today (5) < cutoff (10) → hali yashirilmaydi (o'quvchiga to'lash uchun vaqt beriladi).
        var b = new GroupBalanceService.GroupBalanceInfo(-300_000m, 1, "2026-07", true);

        var (hidden, reason) = JournalPolicy.PaymentGate(Policy(afterDay: true, cutoff: 10), b, "2026-07", 5);

        Assert.False(hidden);
        Assert.Equal("", reason);
    }

    [Fact]
    public void PaymentGate_JoriyOyQarzi_AYNANcutoffKuni_Yashiriladi()
    {
        // Chegara SHU kunning o'zida ishlaydi (>=), keyingi kundan emas.
        var b = new GroupBalanceService.GroupBalanceInfo(-300_000m, 1, "2026-07", true);

        var (hidden, reason) = JournalPolicy.PaymentGate(Policy(afterDay: true, cutoff: 10), b, "2026-07", 10);

        Assert.True(hidden);
        Assert.Equal("cutoff", reason);
    }

    [Fact]
    public void PaymentGate_JoriyOyQarziYoq_KunOtgandaHamKorinadi()
    {
        var b = new GroupBalanceService.GroupBalanceInfo(0m, 0, "", false);

        var (hidden, reason) = JournalPolicy.PaymentGate(Policy(afterDay: true, cutoff: 10), b, "2026-07", 25);

        Assert.False(hidden);
        Assert.Equal("", reason);
    }

    [Fact]
    public void PaymentGate_SozlamalarOchiq_HECHKIMyashirilmaydi()
    {
        // Ikkala sozlama ham o'chiq — eski xatti-harakat: qarzdor bo'lsa ham jurnalda ko'rinadi.
        var b = new GroupBalanceService.GroupBalanceInfo(-900_000m, 3, "2026-01", true);

        var (hidden, reason) = JournalPolicy.PaymentGate(Policy(), b, "2026-07", 28);

        Assert.False(hidden);
        Assert.Equal("", reason);
    }

    [Fact]
    public void PaymentGate_OtganOyQarziUSTUN_SababPrevMonth()
    {
        // Ikkala qoida ham mos kelsa — birinchi (og'irroq) sabab qaytadi.
        var b = new GroupBalanceService.GroupBalanceInfo(-900_000m, 2, "2026-06", true);

        var (hidden, reason) = JournalPolicy.PaymentGate(
            Policy(prevMonth: true, afterDay: true, cutoff: 10), b, "2026-07", 20);

        Assert.True(hidden);
        Assert.Equal("prevMonth", reason);
    }

    /* =========================================================================================
     *  2) SIYOSAT SOZLAMALARI — GetAsync / SaveAsync
     * ========================================================================================= */

    [Fact]
    public async Task GetAsync_CenterMetaYoq_XavfsizDefault_Erkin()
    {
        using var db = TestDb.Sqlite();

        var p = await JournalPolicy.GetAsync(db.Context);

        Assert.Equal(JournalPolicy.ModeFree, p.EditMode);
        Assert.False(p.ConductedOnly);
        Assert.False(p.ApplyToAdmins);
        Assert.Equal(10, p.UnpaidCutoffDay);
    }

    [Fact]
    public async Task GetAsync_BuzuqCutoffKun_Standart10gaTushadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(new CenterMeta { JournalUnpaidCutoffDay = 0 });
        await db.Context.SaveChangesAsync();

        var p = await JournalPolicy.GetAsync(db.Context);

        // 0 (bo'sh/buzuq) — 1 ga emas, aynan 10 ga tushishi kerak (aks holda hamma qarzdor
        // oyning 1-kunidayoq jurnaldan yo'qolardi).
        Assert.Equal(10, p.UnpaidCutoffDay);
    }

    [Fact]
    public async Task SaveAsync_NotogriQiymatlar_XavfsizChegaragaQisiladi()
    {
        using var db = TestDb.Sqlite();

        var saved = await JournalPolicy.SaveAsync(db.Context,
            new JournalPolicyDto("kambag'al-rejim", 999, true, true, false, 99, false, false, 99));

        Assert.Equal(JournalPolicy.ModeFree, saved.EditMode);   // noma'lum rejim → erkin
        Assert.Equal(90, saved.RetroDays);                      // 1..90
        Assert.Equal(30, saved.SalaryGraceDays);                // 0..30
        Assert.Equal(28, saved.UnpaidCutoffDay);                // 1..28 (fevralda ham bor)
    }

    /* =========================================================================================
     *  3) TAHRIRLASH REJIMLARI — CheckAsync
     * ========================================================================================= */

    private static async Task SetModeAsync(TestDb db, string mode, int retroDays = 3,
        bool conductedOnly = false, bool applyToAdmins = false)
    {
        db.Context.CenterMeta.Add(new CenterMeta
        {
            JournalEditMode = mode,
            JournalRetroDays = retroDays,
            JournalConductedOnly = conductedOnly,
            JournalApplyToAdmins = applyToAdmins,
        });
        await db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task CheckAsync_Erkin_EskiSanagaHamRuxsat()
    {
        using var db = TestDb.Sqlite();

        var err = await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(-60), 1, isAdmin: false);

        Assert.Null(err);
    }

    [Fact]
    public async Task CheckAsync_TodayRejimi_KechagiSanaTaqiqlanadi()
    {
        using var db = TestDb.Sqlite();
        await SetModeAsync(db, JournalPolicy.ModeToday);

        var err = await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(-1), 1, isAdmin: false);

        Assert.NotNull(err);
        Assert.Contains("BUGUNGI", err);
    }

    [Fact]
    public async Task CheckAsync_TodayRejimi_BugungiSanagaRuxsat()
    {
        using var db = TestDb.Sqlite();
        await SetModeAsync(db, JournalPolicy.ModeToday);

        Assert.Null(await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(0), 1, isAdmin: false));
    }

    [Fact]
    public async Task CheckAsync_WindowRejimi_ChegaraICHIDAruxsat_TASHQARIDAtaqiq()
    {
        using var db = TestDb.Sqlite();
        await SetModeAsync(db, JournalPolicy.ModeWindow, retroDays: 3);

        // Chegaraning AYNAN o'zi (bugun - 3) hali ochiq.
        Assert.Null(await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(-3), 1, isAdmin: false));
        // Bir kun oldingisi — yopiq.
        var err = await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(-4), 1, isAdmin: false);
        Assert.NotNull(err);
        Assert.Contains("3 kun", err);
    }

    [Fact]
    public async Task CheckAsync_AdminIstisnosi_ApplyToAdminsOchiqBolsaTaqiqYoq()
    {
        using var db = TestDb.Sqlite();
        await SetModeAsync(db, JournalPolicy.ModeToday, applyToAdmins: false);

        // O'qituvchiga taqiq, adminga — yo'q (default: siyosat faqat o'qituvchiga).
        Assert.NotNull(await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(-10), 1, isAdmin: false));
        Assert.Null(await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(-10), 1, isAdmin: true));
    }

    [Fact]
    public async Task CheckAsync_ApplyToAdminsYoqilgan_AdminGaHamQollanadi()
    {
        using var db = TestDb.Sqlite();
        await SetModeAsync(db, JournalPolicy.ModeToday, applyToAdmins: true);

        Assert.NotNull(await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(-10), 1, isAdmin: true));
    }

    [Fact]
    public async Task CheckAsync_ConductedOnly_OtilmaganDarsgaTaqiq()
    {
        using var db = TestDb.Sqlite();
        await SetModeAsync(db, JournalPolicy.ModeFree, conductedOnly: true);

        var err = await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(0), 1, isAdmin: false);

        Assert.NotNull(err);
        Assert.Contains("o'tildi", err);
    }

    [Fact]
    public async Task CheckAsync_ConductedOnly_OtilganDarsgaRuxsat()
    {
        using var db = TestDb.Sqlite();
        await SetModeAsync(db, JournalPolicy.ModeFree, conductedOnly: true);
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = "g1", SubjectId = "s1", Quarter = 1, Date = D(0), Period = 1, Conducted = true,
        });
        await db.Context.SaveChangesAsync();

        Assert.Null(await JournalPolicy.CheckAsync(db.Context, "g1", "s1", D(0), 1, isAdmin: false));
    }

    [Fact]
    public async Task CheckAsync_ConductedOnly_OmmaviyDavomatCHETLABotadi()
    {
        using var db = TestDb.Sqlite();
        await SetModeAsync(db, JournalPolicy.ModeFree, conductedOnly: true);

        // skipConducted=true — ommaviy davomat darsning O'ZINI "o'tildi" qiladi, shuning uchun
        // "avval davomat qiling" sharti unga qo'llanmaydi (aks holda hech qachon boshlab bo'lmasdi).
        Assert.Null(await JournalPolicy.CheckAsync(
            db.Context, "g1", "s1", D(0), 1, isAdmin: false, skipConducted: true));
    }

    /* =========================================================================================
     *  4) JournalService SOF yordamchilari
     * ========================================================================================= */

    [Fact]
    public void LessonDatesInMonth_KabisaFevral_29iHamKiradi()
    {
        // 2024-02-01 — payshanba (indeks 3: 0=Du..6=Yak). Kabisa yilda 29-fevral ham payshanba.
        var dates = JournalService.LessonDatesInMonth(new[] { 3 }, "2024-02").ToList();

        Assert.Equal(
            new[] { "2024-02-01", "2024-02-08", "2024-02-15", "2024-02-22", "2024-02-29" },
            dates);
    }

    [Fact]
    public void LessonDatesInMonth_ODDIYfevral_29iYoq()
    {
        // 2023 — kabisa emas: 2023-02-02 dan boshlanadigan payshanbalar, oxirgisi 23-fevral.
        var dates = JournalService.LessonDatesInMonth(new[] { 3 }, "2023-02").ToList();

        Assert.Equal(
            new[] { "2023-02-02", "2023-02-09", "2023-02-16", "2023-02-23" },
            dates);
    }

    [Fact]
    public void LessonDatesInMonth_YAKSHANBA_indeks6()
    {
        // DayOfWeek.Sunday=0 ni ((int)d+6)%7 formulasi 6 ga (Yak) o'giradi — chalkashmasin.
        var dates = JournalService.LessonDatesInMonth(new[] { 6 }, "2024-02").ToList();

        Assert.Equal(new[] { "2024-02-04", "2024-02-11", "2024-02-18", "2024-02-25" }, dates);
    }

    [Fact]
    public void LessonDatesInMonth_KunlarYoqYokiOyBuzuq_BoshRoyxat()
    {
        Assert.Empty(JournalService.LessonDatesInMonth(Array.Empty<int>(), "2024-02"));
        Assert.Empty(JournalService.LessonDatesInMonth(new[] { 1 }, "2024"));
    }

    [Fact]
    public void EffectiveLessonDatesInMonth_Kochirish_AslKunYoq_YangiKunBor()
    {
        var moves = new[] { new JournalService.LessonMove("2024-02-08", "2024-02-09") };

        var dates = JournalService.EffectiveLessonDatesInMonth(new[] { 3 }, "2024-02", moves);

        Assert.DoesNotContain("2024-02-08", dates);   // asl kun olib tashlandi
        Assert.Contains("2024-02-09", dates);         // yangi kun qo'shildi (guruh kuni bo'lmasa ham)
        Assert.Equal(dates.OrderBy(d => d, StringComparer.Ordinal).ToList(), dates); // tartiblangan
    }

    [Fact]
    public void EffectiveLessonDatesInMonth_BoshqaOygaKochirish_ShuOydaFAQATolibTashlanadi()
    {
        // Dars keyingi oyga ko'chirilgan: shu oyning ro'yxatidan chiqadi, lekin unga qo'shilmaydi.
        var moves = new[] { new JournalService.LessonMove("2024-02-08", "2024-03-01") };

        var feb = JournalService.EffectiveLessonDatesInMonth(new[] { 3 }, "2024-02", moves);
        var mar = JournalService.EffectiveLessonDatesInMonth(new[] { 3 }, "2024-03", moves);

        Assert.DoesNotContain("2024-02-08", feb);
        Assert.Contains("2024-03-01", mar);
    }

    [Fact]
    public void EffectiveLessonDatesInMonth_KochirishYoq_LessonDatesBilanBirXil()
    {
        var plain = JournalService.LessonDatesInMonth(new[] { 0, 3 }, "2024-02").ToList();

        var eff = JournalService.EffectiveLessonDatesInMonth(
            new[] { 0, 3 }, "2024-02", Array.Empty<JournalService.LessonMove>());

        Assert.Equal(plain, eff);
    }

    [Fact]
    public void MemberStart_ActivatedAtUSTUN()
    {
        var m = new StudentGroup { JoinedAt = "2026-01-05", ActivatedAt = "2026-02-10" };

        Assert.Equal("2026-02-10", JournalService.MemberStart(m));
    }

    [Fact]
    public void MemberStart_ActivatedAtYoq_JoinedAtOlinadi()
    {
        var m = new StudentGroup { JoinedAt = "2026-01-05", ActivatedAt = "" };

        Assert.Equal("2026-01-05", JournalService.MemberStart(m));
    }

    [Fact]
    public void MemberStart_IkkalasiBosh_Null_ChegaraQollanmaydi()
    {
        Assert.Null(JournalService.MemberStart(new StudentGroup { JoinedAt = "", ActivatedAt = "" }));
        Assert.Null(JournalService.MemberStart(null));
    }

    [Fact]
    public void MemberStart_VaqtBilanBirgaKelsa_FaqatSANAqismi()
    {
        var m = new StudentGroup { ActivatedAt = "2026-02-10T14:30:00" };

        Assert.Equal("2026-02-10", JournalService.MemberStart(m));
    }

    [Fact]
    public void MemberStart_KaltaBuzuqSana_ETIBORSIZ()
    {
        // 10 belgidan qisqa qiymat sana emas — chegara qo'llanmaydi (JoinedAt ga tushadi).
        var m = new StudentGroup { ActivatedAt = "2026-02", JoinedAt = "2026-01-05" };

        Assert.Equal("2026-01-05", JournalService.MemberStart(m));
    }
}
