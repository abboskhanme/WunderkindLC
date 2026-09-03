using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// MOLIYA / BILLING mantig'ining SOF (bazasiz) testlari — hisob-kitob formulalari, chegirma
/// davri, oy oralig'i, dars sanash, a'zolik holati jamlagichi, to'lov maydonlari
/// normalizatsiyasi va maosh yordamchilari.
///
/// <para>QOIDA: sanalar MUTLAQ yozilmaydi (kelasi oyda test yiqilmasin) — <see cref="AppClock"/>
/// ga nisbatan quriladi. Istisno: <see cref="TuitionService.LessonsInRange"/> kabi soatga umuman
/// bog'liq bo'lmagan sof funksiyalar (u yerda aniq kalendar kunlari tekshiriladi).</para>
///
/// <para>Ba'zi testlar <c>[Fact(Skip=...)]</c> — ular TASDIQLANGAN xatolarni hujjatlashtiradi:
/// KUTILGAN (to'g'ri) xulq yozilgan, lekin production kodi hozircha boshqacha ishlaydi.</para>
/// </summary>
public class FinanceLogicTests
{
    // ==================== yordamchilar ====================

    /// <summary>Joriy oydan <paramref name="delta"/> oy nariga/beriga ("yyyy-MM").</summary>
    private static string MonthOffset(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    private static Student MakeStudent(
        int pct = 0, decimal amount = 0m,
        string start = "", string end = "", string? groupId = null) => new()
    {
        FullName = "Test O'quvchi",
        DiscountPct = pct,
        DiscountAmount = amount,
        DiscountStartMonth = start,
        DiscountEndMonth = end,
        DiscountGroupId = groupId,
    };

    // ==================== TuitionService.ChargeFor ====================

    [Fact]
    public void ChargeFor_AvvalFoiz_KeyinSumma_ayriladi()
    {
        // Tartib MUHIM: 500 000 dan 10% (50 000) olinadi → 450 000, keyin 30 000 ayriladi → 420 000.
        // Teskari tartibda (avval summa) 423 000 chiqardi — narx noto'g'ri bo'lardi.
        Assert.Equal(420_000m, TuitionService.ChargeFor(500_000m, 10, 30_000m));
    }

    [Fact]
    public void ChargeFor_ChegirmaKopBolsa_ManfiyEmas_nol()
    {
        Assert.Equal(0m, TuitionService.ChargeFor(300_000m, 50, 1_000_000m));
    }

    [Fact]
    public void ChargeFor_Foiz_0_100_oraligiga_qisiladi()
    {
        // 150% → 100% ga qisiladi (hammasi chegirma), −20% → 0% (chegirma yo'q).
        Assert.Equal(0m, TuitionService.ChargeFor(400_000m, 150, 0m));
        Assert.Equal(400_000m, TuitionService.ChargeFor(400_000m, -20, 0m));
    }

    [Fact]
    public void ChargeFor_ManfiySumma_chegirmaSifatidaHisoblanmaydi()
    {
        // Manfiy chegirma summasi narxni OSHIRIB yubormasligi kerak (Math.Max(0)).
        Assert.Equal(400_000m, TuitionService.ChargeFor(400_000m, 0, -100_000m));
    }

    [Fact]
    public void ChargeFor_NarxNol_yoki_manfiy_nol()
    {
        Assert.Equal(0m, TuitionService.ChargeFor(0m, 50, 0m));
        Assert.Equal(0m, TuitionService.ChargeFor(-100m, 0, 0m));
    }

    [Fact]
    public void DiscountFor_chegirmaSummasi_narxdanOshmaydi()
    {
        // 100% + qo'shimcha summa bo'lsa ham chegirma narxdan katta chiqmaydi.
        Assert.Equal(500_000m, TuitionService.DiscountFor(500_000m, 100, 999_999m));
        Assert.Equal(50_000m, TuitionService.DiscountFor(500_000m, 10, 0m));
        Assert.Equal(0m, TuitionService.DiscountFor(0m, 50, 10m));
    }

    // ==================== DiscountActiveForMonth ====================

    [Fact]
    public void DiscountActiveForMonth_chegaralar_INKLYUZIV()
    {
        var s = MakeStudent(start: "2026-03", end: "2026-05");
        Assert.True(TuitionService.DiscountActiveForMonth(s, "2026-03"));   // boshlanish oyi kiradi
        Assert.True(TuitionService.DiscountActiveForMonth(s, "2026-04"));
        Assert.True(TuitionService.DiscountActiveForMonth(s, "2026-05"));   // tugash oyi ham kiradi
        Assert.False(TuitionService.DiscountActiveForMonth(s, "2026-02"));
        Assert.False(TuitionService.DiscountActiveForMonth(s, "2026-06"));
    }

    [Fact]
    public void DiscountActiveForMonth_ikkalaChegaraBosh_hardoim_amalda()
    {
        var s = MakeStudent();
        Assert.True(TuitionService.DiscountActiveForMonth(s, "2020-01"));
        Assert.True(TuitionService.DiscountActiveForMonth(s, "2099-12"));
    }

    [Fact]
    public void DiscountActiveForMonth_bittaChegara_birTomonlamaOchiq()
    {
        var faqatBoshi = MakeStudent(start: "2026-03");
        Assert.False(TuitionService.DiscountActiveForMonth(faqatBoshi, "2026-02"));
        Assert.True(TuitionService.DiscountActiveForMonth(faqatBoshi, "2099-12"));

        var faqatOxiri = MakeStudent(end: "2026-05");
        Assert.True(TuitionService.DiscountActiveForMonth(faqatOxiri, "2000-01"));
        Assert.False(TuitionService.DiscountActiveForMonth(faqatOxiri, "2026-06"));
    }

    // ==================== DiscountForMonth ====================

    [Fact]
    public void DiscountForMonth_boshqaGuruhHisobiga_chegirmaBerilmaydi()
    {
        // Chegirma "g1" guruhiga biriktirilgan — "g2" hisobida o'quvchi to'liq to'laydi.
        var s = MakeStudent(pct: 50, groupId: "g1");
        Assert.Equal(200_000m, TuitionService.DiscountForMonth(s, 400_000m, MonthOffset(0), "g1"));
        Assert.Equal(0m, TuitionService.DiscountForMonth(s, 400_000m, MonthOffset(0), "g2"));
        Assert.Equal(0m, TuitionService.DiscountForMonth(s, 400_000m, MonthOffset(0), null));
    }

    [Fact]
    public void DiscountForMonth_guruhgaBiriktirilmagan_chegirma_hammaHisobga()
    {
        var s = MakeStudent(pct: 25);
        Assert.Equal(100_000m, TuitionService.DiscountForMonth(s, 400_000m, MonthOffset(0), "g1"));
        Assert.Equal(100_000m, TuitionService.DiscountForMonth(s, 400_000m, MonthOffset(0), null));
    }

    [Fact]
    public void DiscountForMonth_davrTashqarisida_nol()
    {
        var s = MakeStudent(pct: 50, start: MonthOffset(-1), end: MonthOffset(-1));
        Assert.Equal(200_000m, TuitionService.DiscountForMonth(s, 400_000m, MonthOffset(-1), null));
        Assert.Equal(0m, TuitionService.DiscountForMonth(s, 400_000m, MonthOffset(0), null));
    }

    // ==================== NextMonth / MonthRange ====================

    [Fact]
    public void NextMonth_dekabrdan_yanvarga_otadi()
    {
        Assert.Equal("2027-01", TuitionService.NextMonth("2026-12"));
        Assert.Equal("2026-02", TuitionService.NextMonth("2026-01"));
        Assert.Equal("2026-10", TuitionService.NextMonth("2026-09")); // ikki xonali oy formati saqlanadi
    }

    [Fact]
    public void MonthRange_teskariOraliq_boshRoyxat()
    {
        Assert.Empty(TuitionService.MonthRange("2026-05", "2026-03"));
        Assert.Empty(TuitionService.MonthRange("", "2026-03"));
        Assert.Empty(TuitionService.MonthRange("2026-03", ""));
    }

    [Fact]
    public void MonthRange_yilChegarasidan_otadi_va_inklyuziv()
    {
        Assert.Equal(
            new[] { "2026-11", "2026-12", "2027-01", "2027-02" },
            TuitionService.MonthRange("2026-11", "2027-02").ToArray());
        Assert.Equal(new[] { "2026-04" }, TuitionService.MonthRange("2026-04", "2026-04").ToArray());
    }

    [Fact(Skip = "XATO (TuitionService.cs:136-142): buzuq oy formatida NextMonth/MonthRange istisno beradi")]
    public void MonthRange_buzuqFormatda_istisnoBermasligiKerak()
    {
        // XATO (TuitionService.cs:136-142): NextMonth("2026") → IndexOutOfRange, NextMonth("abcd-ef")
        // → FormatException. MonthRange ichida chaqirilgani uchun butun so'rov 500 bilan yiqiladi
        // (SalaryLedger.cs:38-39 dagi from/to ham foydalanuvchidan keladi va tekshirilmaydi).
        // KUTILGAN: noto'g'ri format bo'sh oraliq beradi (yoki tushunarli validatsiya xatosi).
        Assert.Empty(TuitionService.MonthRange("abcd-ef", "2026-01"));
        Assert.Empty(TuitionService.MonthRange("2026", "2026-01"));
    }

    // ==================== LessonsInRange ====================

    [Fact]
    public void LessonsInRange_kabisaFevral_29kun_hisobgaOlinadi()
    {
        // 2024 — kabisa yil, 1-fevral PAYSHANBA. Payshanbalar: 1, 8, 15, 22, 29 → 5 ta
        // (kabisa kuni 29-fevral ham kiradi). Kabisa bo'lmagan 2025 da esa 4 ta.
        var payshanba = new[] { 3 }; // 0=Dushanba ... 3=Payshanba
        Assert.Equal(5, TuitionService.LessonsInRange(
            payshanba, new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29)));
        Assert.Equal(4, TuitionService.LessonsInRange(
            payshanba, new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28)));
    }

    [Fact]
    public void LessonsInRange_boshKunlar_royxati_nol()
    {
        Assert.Equal(0, TuitionService.LessonsInRange(
            Array.Empty<int>(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void LessonsInRange_teskariOraliq_nol()
    {
        Assert.Equal(0, TuitionService.LessonsInRange(
            new[] { 0, 2, 4 }, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void LessonsInRange_bir_kun_va_hafta_kunlari_togri_sanaladi()
    {
        // 2026-yil 2-mart — DUSHANBA (indeks 0). Bitta kunlik oraliq: dushanba bo'lsa 1, bo'lmasa 0.
        Assert.Equal(1, TuitionService.LessonsInRange(
            new[] { 0 }, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2)));
        Assert.Equal(0, TuitionService.LessonsInRange(
            new[] { 6 }, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2)));
        // Du/Chor/Juma (0,2,4) — bir to'liq hafta ichida 3 ta dars.
        Assert.Equal(3, TuitionService.LessonsInRange(
            new[] { 0, 2, 4 }, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)));
    }

    // ==================== ProratedLessonCharge ====================

    [Fact]
    public void ProratedLessonCharge_toliqOy_toliqNarx()
    {
        // Oyning BIRINCHI darsidan aktivlashtirilgan (qolgan == jami) → to'liq oylik.
        Assert.Equal(600_000m, TuitionService.ProratedLessonCharge(600_000m, 50_000m, 8, 8));
    }

    [Fact]
    public void ProratedLessonCharge_12_dars_chegarasi_toliqNarx()
    {
        // 12+ dars qolgan bo'lsa (jamidan kam bo'lsa ham) — to'liq oylik narx.
        Assert.Equal(600_000m, TuitionService.ProratedLessonCharge(600_000m, 50_000m, 12, 20));
        Assert.Equal(600_000m, TuitionService.ProratedLessonCharge(600_000m, 50_000m, 13, 20));
        // 11 ta — chegaradan past, dars narxi bo'yicha (11 × 50 000 = 550 000).
        Assert.Equal(550_000m, TuitionService.ProratedLessonCharge(600_000m, 50_000m, 11, 20));
    }

    [Fact]
    public void ProratedLessonCharge_LessonPrice_boyicha_yaxlitNarx()
    {
        // Kursda bir dars narxi bor → dars soni × shu narx (oylik ÷ jami emas).
        Assert.Equal(200_000m, TuitionService.ProratedLessonCharge(600_000m, 50_000m, 4, 20));
    }

    [Fact]
    public void ProratedLessonCharge_LessonPrice_yoq_bolsa_eski_proRata()
    {
        // lessonFee = 0 → oylik × dars ÷ jami = 600 000 × 5 ÷ 20 = 150 000.
        Assert.Equal(150_000m, TuitionService.ProratedLessonCharge(600_000m, 0m, 5, 20));
    }

    [Fact]
    public void ProratedLessonCharge_toliqOylikdan_oshmaydi()
    {
        // Dars narxi qimmat: 8 × 100 000 = 800 000 > 600 000 → to'liq oylikka qisiladi.
        Assert.Equal(600_000m, TuitionService.ProratedLessonCharge(600_000m, 100_000m, 8, 20));
    }

    [Fact]
    public void ProratedLessonCharge_nolBoluvchilar_istisnoBermaydi()
    {
        Assert.Equal(0m, TuitionService.ProratedLessonCharge(600_000m, 50_000m, 5, 0));   // oyda dars yo'q
        Assert.Equal(0m, TuitionService.ProratedLessonCharge(600_000m, 50_000m, 0, 20));  // dars qolmagan
        Assert.Equal(0m, TuitionService.ProratedLessonCharge(0m, 50_000m, 5, 20));        // narx yo'q
        Assert.Equal(0m, TuitionService.ProratedLessonCharge(600_000m, 50_000m, -3, 20)); // manfiy
    }

    // ==================== MembershipLifecycle.BillableInMonth ====================

    [Fact]
    public void BillableInMonth_MUZLATISH_oyi_KIRADI()
    {
        // Billing konvensiyasi: muzlatish oyining O'ZI pullik (qisman hisob yoziladi),
        // keyingi oy esa emas.
        Assert.True(MembershipLifecycle.BillableInMonth("frozen", "2026-01-10", "2026-05-14", "2026-05"));
        Assert.False(MembershipLifecycle.BillableInMonth("frozen", "2026-01-10", "2026-05-14", "2026-06"));
    }

    [Fact]
    public void BillableInMonth_AKTIVLASHTIRISH_oyi_kiradi_undanOldingisi_yoq()
    {
        Assert.True(MembershipLifecycle.BillableInMonth("active", "2026-03-15", "", "2026-03"));
        Assert.True(MembershipLifecycle.BillableInMonth("active", "2026-03-15", "", "2026-04"));
        Assert.False(MembershipLifecycle.BillableInMonth("active", "2026-03-15", "", "2026-02"));
    }

    [Fact]
    public void BillableInMonth_SINOV_hech_qachon_pullik_emas()
    {
        // Sinov (trial) a'zolikda muzlatish/aktivlashtirish sanalari bo'lsa ham to'lov hisoblanmaydi.
        Assert.False(MembershipLifecycle.BillableInMonth("trial", "2026-01-01", "", "2026-05"));
        Assert.False(MembershipLifecycle.BillableInMonth("trial", "", "", "2026-05"));
    }

    /// <summary>
    /// «AKTIV MUZLATISH» (yangi o'quv yiliga o'tish) HISOB-KITOBGA TA'SIR QILMAYDI — bu modulning
    /// ENG MUHIM shartnomasi. Belgi <c>StudentGroup.YearFreeze</c> bayrog'ida yashaydi, a'zolik
    /// <c>Status</c>i esa baribir "frozen" bo'lib qoladi; shuning uchun pullik oy qoidasi ikkala
    /// muzlatishda ham AYNAN bir xil natija berishi kerak.
    /// <para>Test AYNAN shu qulfni ushlab turadi: agar kimdir kelajakda <c>BillableInMonth</c> ga
    /// (yoki uni chaqiradigan hisob mantig'iga) yangi bayroqni "hisobga olsin" deb qo'shsa, bu
    /// test qizaradi — chunki o'sha payt muzlatishning IKKI USULI pul jihatidan ayrilib ketardi.</para>
    /// </summary>
    [Fact]
    public void BillableInMonth_AKTIVMUZLATISH_oddiy_muzlatish_bilan_AYNAN_BIR_XIL()
    {
        // Bayroq entity darajasidagi overload orqali ham hech narsani o'zgartirmasligi kerak:
        // yagona farq YearFreeze, qolgan hamma maydon bir xil.
        var oddiy = new StudentGroup
        {
            Status = "frozen", ActivatedAt = "2026-01-10", FrozenAt = "2026-05-14", YearFreeze = false,
        };
        var aktiv = new StudentGroup
        {
            Status = "frozen", ActivatedAt = "2026-01-10", FrozenAt = "2026-05-14", YearFreeze = true,
        };

        // Muzlatish oyi — ikkalasida ham pullik; keyingi oy — ikkalasida ham emas.
        Assert.Equal(MembershipLifecycle.BillableInMonth(oddiy, "2026-05"),
                     MembershipLifecycle.BillableInMonth(aktiv, "2026-05"));
        Assert.Equal(MembershipLifecycle.BillableInMonth(oddiy, "2026-06"),
                     MembershipLifecycle.BillableInMonth(aktiv, "2026-06"));
        Assert.True(MembershipLifecycle.BillableInMonth(aktiv, "2026-05"));
        Assert.False(MembershipLifecycle.BillableInMonth(aktiv, "2026-06"));
    }

    [Fact(Skip = "XATO (MembershipLifecycle.cs:65 ↔ TuitionService.cs:438): bo'sh ActivatedAt da ikki xil ta'rif")]
    public void BillableInMonth_boshActivatedAt_AccrueMonth_bilan_mos_bolishiKerak()
    {
        // XATO: TuitionService.cs:438 `m.ActivatedAt.Length >= 7 && month > ActivatedAt[..7]` talab qiladi —
        // ya'ni ActivatedAt BO'SH bo'lsa a'zolikka oylik HECH QACHON hisoblanmaydi. Ayni paytda
        // MembershipLifecycle.cs:65 `activatedAt.Length < 7` ni "har doim pullik" deb oladi.
        // Natija: teglanmagan to'lov taqsimoti (SalaryLedger/GroupBalanceService) va bonus mantig'i
        // o'sha oyni "pullik" deb hisoblaydi, hisob (AccrueMonth) esa umuman yozmaydi — raqamlar mos kelmaydi.
        // KUTILGAN: ikkala joyda BITTA ta'rif — ActivatedAt bo'sh bo'lsa a'zolik pullik EMAS.
        Assert.False(MembershipLifecycle.BillableInMonth("active", "", "", MonthOffset(0)));
    }

    // ==================== MembershipLifecycle.Tally ====================

    [Fact]
    public void Tally_holatlar_boyicha_togri_sanaydi()
    {
        var t = MembershipLifecycle.Tally(new (string, bool, string?)[]
        {
            ("active", true, null),
            ("active", true, ""),
            ("trial", true, null),
            ("frozen", true, null),
            ("active", false, null),      // guruhdan chiqarilgan → Ketgan
            ("active", true, "2026-04-01"), // LeftAt bor → Ketgan
        });

        Assert.Equal(6, t.Came);
        Assert.Equal(2, t.Active);
        Assert.Equal(1, t.Trial);
        Assert.Equal(1, t.Frozen);
        Assert.Equal(2, t.Left);
        Assert.Equal(4, t.Remaining);      // Came − Left
    }

    [Fact]
    public void Tally_nomalum_holat_faol_deb_olinadi()
    {
        var t = MembershipLifecycle.Tally(new (string, bool, string?)[] { ("nomalum", true, null) });
        Assert.Equal(1, t.Active);
        Assert.Equal(0, t.Trial);
        Assert.Equal(0, t.Frozen);
    }

    [Fact]
    public void Tally_bosh_royxatda_nolga_bolish_yoq()
    {
        var t = MembershipLifecycle.Tally(Array.Empty<(string, bool, string?)>());
        Assert.Equal(0, t.Came);
        Assert.Null(t.ConversionPct);   // Came=0 → foiz aniqlanmagan (0 ga bo'lish yo'q)
        Assert.Equal(0d, t.Retention);
        Assert.Equal(0d, t.Loss);
        Assert.Equal(0, t.Remaining);
    }

    [Fact]
    public void Tally_foizlar_togri_hisoblanadi()
    {
        // 4 kelgan: 1 faol, 1 sinov, 1 muzlatilgan, 1 ketgan.
        var t = MembershipLifecycle.Tally(new (string, bool, string?)[]
        {
            ("active", true, null),
            ("trial", true, null),
            ("frozen", true, null),
            ("active", false, null),
        });
        Assert.Equal(25, t.ConversionPct);          // 1/4
        Assert.Equal(25.0d, t.Retention);
        Assert.Equal(50.0d, t.Loss);                // (muzlatilgan + ketgan) / kelgan
    }

    // ==================== PaymentFields ====================

    [Theory]
    [InlineData("123", "KV123")]
    [InlineData("kv-123", "KV123")]
    [InlineData("KV 000123", "KV000123")]
    [InlineData("  kv 1 2 3  ", "KV123")]
    [InlineData("KV000123", "KV000123")]
    public void NormalizeReceiptNo_yagona_formatga_keltiradi(string raw, string kutilgan)
        => Assert.Equal(kutilgan, PaymentFields.NormalizeReceiptNo(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("KV")]   // faqat seriya, raqamsiz
    [InlineData("kv-")]
    public void NormalizeReceiptNo_boshOyoq_null(string? raw)
        => Assert.Null(PaymentFields.NormalizeReceiptNo(raw));

    [Fact(Skip = "XATO (PaymentFields.cs:23): nozik probel (U+00A0) normallashtirilmaydi")]
    public void NormalizeReceiptNo_nozikProbel_ham_olibTashlanishiKerak()
    {
        // XATO (PaymentFields.cs:23): faqat oddiy probel (" ") va defis olib tashlanadi. Kassir
        // raqamni brauzer/Word'dan nusxalasa ORASIDA nozik probel (U+00A0) kelib qoladi va
        // "KV 123" bo'lib saqlanadi → kvitansiya dublikat nazorati (ReceiptGuard) va qidiruv
        // "KV123" ni topmaydi, ya'ni bitta blank ikki marta ishlatilishi mumkin.
        Assert.Equal("KV123", PaymentFields.NormalizeReceiptNo("KV 123"));
    }

    [Theory]
    [InlineData("1234", "1234")]
    [InlineData("8600 **** 1234", "1234")]
    [InlineData("8600123412341234", "1234")]
    [InlineData("**** **** **** 5678", "5678")]
    public void TryNormalizeCardLast4_faqat_oxirgi_4_raqam_saqlanadi(string raw, string kutilgan)
    {
        Assert.True(PaymentFields.TryNormalizeCardLast4(raw, out var last4));
        Assert.Equal(kutilgan, last4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("karta")]   // raqam umuman yo'q → ixtiyoriy maydon, bo'sh deb olinadi
    public void TryNormalizeCardLast4_boshQiymat_ruxsat(string? raw)
    {
        Assert.True(PaymentFields.TryNormalizeCardLast4(raw, out var last4));
        Assert.Null(last4);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("86-1")]
    public void TryNormalizeCardLast4_4_tadan_kam_raqam_xato(string raw)
    {
        Assert.False(PaymentFields.TryNormalizeCardLast4(raw, out var last4));
        Assert.Null(last4);
    }

    [Theory]
    [InlineData("09:05", "09:05")]
    [InlineData("9:05", "09:05")]
    [InlineData(" 23:59 ", "23:59")]
    [InlineData("00:00", "00:00")]
    public void TryNormalizeTime_HHmm_formatiga_keltiradi(string raw, string kutilgan)
    {
        Assert.True(PaymentFields.TryNormalizeTime(raw, out var time));
        Assert.Equal(kutilgan, time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalizeTime_bosh_vaqt_ixtiyoriy(string? raw)
    {
        Assert.True(PaymentFields.TryNormalizeTime(raw, out var time));
        Assert.Null(time);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("abc")]
    [InlineData("12-30")]
    public void TryNormalizeTime_notogri_format_false(string raw)
    {
        Assert.False(PaymentFields.TryNormalizeTime(raw, out var time));
        Assert.Null(time);
    }

    // ==================== CashierReport.KeyOf ====================

    [Fact]
    public void KeyOf_akkaunt_id_bolsa_oshaId_kalit()
        => Assert.Equal("user-1", CashierReport.KeyOf("user-1", "Ali Valiyev"));

    [Fact]
    public void KeyOf_eski_idsiz_yozuv_ism_boyicha_kalit()
    {
        Assert.Equal("name:Ali Valiyev", CashierReport.KeyOf(null, "Ali Valiyev"));
        Assert.Equal("name:Ali Valiyev", CashierReport.KeyOf("", "Ali Valiyev"));
        Assert.Equal("name:", CashierReport.KeyOf(null, null));
    }

    // ==================== RetentionBonusService.Settings ====================

    [Fact]
    public void RetentionSettings_meta_yoq_bolsa_xavfsiz_standart()
    {
        var s = RetentionBonusService.Settings(null);
        Assert.Equal(6, s.MonthsRequired);   // 0 qolsa har kim darhol "tayyor" bo'lardi
        Assert.Equal(2, s.MaxGapMonths);
        Assert.Equal(0m, s.DefaultAmount);
    }

    [Fact]
    public void RetentionSettings_chegaralar_qisiladi()
    {
        var s = RetentionBonusService.Settings(new CenterMeta
        {
            RetentionMonthsRequired = 100,   // → 36 (yuqori chegara)
            RetentionMaxGapMonths = 50,      // → 12
            RetentionDefaultAmount = -5m,    // → 0
        });
        Assert.Equal(36, s.MonthsRequired);
        Assert.Equal(12, s.MaxGapMonths);
        Assert.Equal(0m, s.DefaultAmount);
    }

    [Fact]
    public void RetentionSettings_sozlanmagan_yoki_manfiy_oy_6_ga_tushadi()
    {
        Assert.Equal(6, RetentionBonusService.Settings(new CenterMeta { RetentionMonthsRequired = 0 }).MonthsRequired);
        Assert.Equal(6, RetentionBonusService.Settings(new CenterMeta { RetentionMonthsRequired = -3 }).MonthsRequired);
        Assert.Equal(0, RetentionBonusService.Settings(new CenterMeta { RetentionMaxGapMonths = -1 }).MaxGapMonths);
    }

    [Fact]
    public void RetentionSettings_togri_qiymatlar_ozgarmaydi()
    {
        var s = RetentionBonusService.Settings(new CenterMeta
        {
            RetentionMonthsRequired = 9,
            RetentionMaxGapMonths = 3,
            RetentionDefaultAmount = 500_000m,
        });
        Assert.Equal(9, s.MonthsRequired);
        Assert.Equal(3, s.MaxGapMonths);
        Assert.Equal(500_000m, s.DefaultAmount);
    }

    // ==================== SalaryJournalStats.Stat ====================

    [Fact]
    public void SalaryStat_Planned_nol_bolsa_Ratio_1_nolga_bolish_yoq()
    {
        // Rejada dars yo'q (muhlati kelmagan / guruh yangi) → ushlanma bo'lmasligi kerak.
        var stat = new SalaryJournalStats.Stat(0, 0, new List<string>());
        Assert.Equal(1m, stat.Ratio);
        Assert.Equal(0, stat.Missed);
    }

    [Fact]
    public void SalaryStat_Ratio_belgilangan_darslar_nisbati()
    {
        var stat = new SalaryJournalStats.Stat(10, 7, new List<string> { "d1", "d2", "d3" });
        Assert.Equal(0.7m, stat.Ratio);
        Assert.Equal(3, stat.Missed);
    }

    [Fact]
    public void SalaryStat_hech_biri_belgilanmagan_Ratio_0()
    {
        var stat = new SalaryJournalStats.Stat(8, 0, new List<string> { "a", "b", "c", "d", "e", "f", "g", "h" });
        Assert.Equal(0m, stat.Ratio);
        Assert.Equal(8, stat.Missed);
    }

    // ==================== TeacherSalaryCalc.StartDateOf ====================

    [Fact]
    public void StartDateOf_yangi_maydon_ustun()
    {
        var t = new Teacher { SalaryStartDate = "2026-03-15", SalaryStartMonth = "2026-01" };
        Assert.Equal("2026-03-15", TeacherSalaryCalc.StartDateOf(t));
    }

    [Fact]
    public void StartDateOf_eski_oy_maydonidan_oyning_1_kuni()
    {
        var t = new Teacher { SalaryStartDate = "", SalaryStartMonth = "2026-01" };
        Assert.Equal("2026-01-01", TeacherSalaryCalc.StartDateOf(t));
    }

    [Fact]
    public void StartDateOf_ikkalasi_bosh_null()
        => Assert.Null(TeacherSalaryCalc.StartDateOf(new Teacher()));

    // ==================== YOPILGAN FAOL DAVRLAR (StudentGroup.PastPeriods) ====================

    private static StudentGroup Sg(string status, string activatedAt, string frozenAt,
                                   params string[] pastPeriods) => new()
    {
        Status = status, ActivatedAt = activatedAt, FrozenAt = frozenAt,
        PastPeriods = pastPeriods.ToList(),
    };

    [Fact]
    public void ClosePeriod_muzlatilgan_azolik_ESKI_davrni_saqlaydi()
    {
        // Muzlatib qayta aktivlashtirish: ActivatedAt ustidan yozilishidan OLDIN davr tarixga o'tadi.
        var m = Sg("frozen", "2026-01-10", "2026-05-15");
        MembershipLifecycle.ClosePeriod(m, "2026-09-01");
        Assert.Equal(new[] { "2026-01-10|2026-05-15" }, m.PastPeriods);
    }

    [Fact]
    public void ClosePeriod_IDEMPOTENT_va_ActivatedAt_bosh_bolsa_hech_narsa_qilmaydi()
    {
        var m = Sg("frozen", "2026-01-10", "2026-05-15");
        MembershipLifecycle.ClosePeriod(m, "2026-09-01");
        MembershipLifecycle.ClosePeriod(m, "2026-09-01");
        Assert.Single(m.PastPeriods); // ikki marta bosilgan tugma tarixni ikkilantirmaydi

        var trial = Sg("trial", "", "");
        MembershipLifecycle.ClosePeriod(trial, "2026-09-01");
        Assert.Empty(trial.PastPeriods); // faol davr umuman boshlanmagan
    }

    [Fact]
    public void ClosePeriod_TESKARI_oraliq_yasamaydi()
    {
        // Orqaga sanalgan muzlatish: tugash boshlanishdan oldin. Davr bir kunlik bo'lib yopiladi,
        // aks holda hech bir oy unga tushmasdi.
        var m = Sg("frozen", "2026-05-10", "2026-03-01");
        MembershipLifecycle.ClosePeriod(m, "2026-09-01");
        Assert.Equal(new[] { "2026-05-10|2026-05-10" }, m.PastPeriods);
    }

    [Fact]
    public void ClosePeriod_FrozenAt_bosh_bolsa_LeftAt_keyin_fallback_ishlatiladi()
    {
        var left = new StudentGroup { Status = "active", ActivatedAt = "2026-01-10", LeftAt = "2026-04-20" };
        MembershipLifecycle.ClosePeriod(left, "2026-09-01");
        Assert.Equal(new[] { "2026-01-10|2026-04-20" }, left.PastPeriods);

        var neither = Sg("active", "2026-01-10", "");
        MembershipLifecycle.ClosePeriod(neither, "2026-09-01");
        Assert.Equal(new[] { "2026-01-10|2026-09-01" }, neither.PastPeriods);
    }

    [Fact]
    public void BillableInMonth_qayta_aktivlashtirilgach_ESKI_oylar_PULLIK_boladi()
    {
        // 2026-01 aktiv → 2026-05 muzlatildi → 2026-09 qayta aktiv.
        var m = Sg("active", "2026-09-01", "", "2026-01-10|2026-05-15");

        Assert.True(MembershipLifecycle.BillableInMonth(m, "2026-01"));  // aktivlashtirish oyi KIRADI
        Assert.True(MembershipLifecycle.BillableInMonth(m, "2026-03"));
        Assert.True(MembershipLifecycle.BillableInMonth(m, "2026-05"));  // muzlatish oyi KIRADI
        Assert.False(MembershipLifecycle.BillableInMonth(m, "2026-06")); // muzlatilgan davr
        Assert.False(MembershipLifecycle.BillableInMonth(m, "2026-08"));
        Assert.True(MembershipLifecycle.BillableInMonth(m, "2026-09"));  // yangi davr
        Assert.False(MembershipLifecycle.BillableInMonth(m, "2025-12")); // davrdan oldin
    }

    [Fact]
    public void BillableInMonth_SINOVGA_qaytarilgan_azolikda_OTMISH_pullik_boligicha_qoladi()
    {
        // "trial" tekshiruvi yopilgan davrlardan KEYIN turadi — o'quvchi bugun sinovga qaytarilgani
        // o'tmishda pul to'lamagan degani emas.
        var m = Sg("trial", "", "", "2026-01-10|2026-04-20");
        Assert.True(MembershipLifecycle.BillableInMonth(m, "2026-02"));
        Assert.False(MembershipLifecycle.BillableInMonth(m, "2026-06"));
    }

    [Fact]
    public void BillableInMonth_PastPeriods_BOSH_bolsa_ESKI_xattiharakat_AYNAN_saqlanadi()
    {
        // Mavjud ma'lumotda ro'yxat bo'sh — ya'ni deploy hech narsani o'zgartirmaydi.
        foreach (var month in new[] { "2025-12", "2026-01", "2026-03", "2026-05", "2026-06" })
            Assert.Equal(
                MembershipLifecycle.BillableInMonth("active", "2026-01-10", "2026-05-15", month),
                MembershipLifecycle.BillableInMonth(Sg("active", "2026-01-10", "2026-05-15"), month));
    }

    [Fact]
    public void TryParsePeriod_BUZUQ_yozuvlar_hisobni_yiqitmaydi()
    {
        Assert.False(MembershipLifecycle.TryParsePeriod(null, out _, out _));
        Assert.False(MembershipLifecycle.TryParsePeriod("", out _, out _));
        Assert.False(MembershipLifecycle.TryParsePeriod("2026-01-10", out _, out _));   // ajratkich yo'q
        Assert.False(MembershipLifecycle.TryParsePeriod("2026|x", out _, out _));       // boshlanish qisqa
        Assert.False(MembershipLifecycle.MonthInPeriod("axlat", "2026-03"));
        Assert.False(MembershipLifecycle.AccruableInPeriod("axlat", "2026-03"));

        // Ikkinchi "|" — tugash qismida qoladi va sana sifatida yaroqsiz bo'ladi (ochiq davr).
        Assert.True(MembershipLifecycle.TryParsePeriod("2026-01-10|a|b", out var f, out var t));
        Assert.Equal("2026-01-10", f);
        Assert.Equal("a|b", t);
    }

    [Fact]
    public void AccruableInPeriod_CHEGARALAR_kirmaydi_MonthInPeriod_dan_FARQI()
    {
        const string p = "2026-01-10|2026-05-15";
        // Aktivlashtirish va muzlatish oylari QISMAN hisob bilan o'z vaqtida yozilgan —
        // accrual ularni qayta yozmasligi kerak (ikki marta hisoblanardi).
        Assert.False(MembershipLifecycle.AccruableInPeriod(p, "2026-01"));
        Assert.True(MembershipLifecycle.AccruableInPeriod(p, "2026-02"));
        Assert.True(MembershipLifecycle.AccruableInPeriod(p, "2026-04"));
        Assert.False(MembershipLifecycle.AccruableInPeriod(p, "2026-05"));
        Assert.False(MembershipLifecycle.AccruableInPeriod(p, "2026-06"));

        // MonthInPeriod esa chegaralarni KIRITADI.
        Assert.True(MembershipLifecycle.MonthInPeriod(p, "2026-01"));
        Assert.True(MembershipLifecycle.MonthInPeriod(p, "2026-05"));

        // Tugashi yo'q davr accrual uchun YOPIQ emas — hech narsa yozilmaydi.
        Assert.False(MembershipLifecycle.AccruableInPeriod("2026-01-10|", "2026-03"));
    }

    [Fact]
    public void FirstActivatedAt_va_ActivatedInMonth_ENG_ERTA_hodisani_topadi()
    {
        var m = Sg("active", "2026-09-01", "", "2026-03-01|2026-06-10", "2026-01-10|2026-02-20");
        Assert.Equal("2026-01-10", MembershipLifecycle.FirstActivatedAt(m));
        Assert.True(MembershipLifecycle.ActivatedInMonth(m, "2026-01"));
        Assert.True(MembershipLifecycle.ActivatedInMonth(m, "2026-03"));
        Assert.True(MembershipLifecycle.ActivatedInMonth(m, "2026-09"));
        Assert.False(MembershipLifecycle.ActivatedInMonth(m, "2026-05"));

        Assert.Equal("", MembershipLifecycle.FirstActivatedAt(Sg("trial", "", "")));
    }

    [Fact]
    public void AccruableMonth_orqaga_sanalgan_MUZLATISHDA_ochirilgan_oylar_TIRILMAYDI()
    {
        // Bugun 2026-09, muzlatish 2026-06 dan (orqaga sanalgan) → PurgeChargesAfterMonthAsync
        // 07/08 hisoblarini o'chirgan. Qayta aktivlashtirilgach ular QAYTA yozilmasligi shart:
        // davr muzlatish sanasida yopilgani uchun 07/08 undan TASHQARIDA qoladi.
        var m = Sg("active", "2026-09-01", "", "2026-01-10|2026-06-05");
        Assert.False(TuitionService.AccruableMonth(m, "2026-07"));
        Assert.False(TuitionService.AccruableMonth(m, "2026-08"));
        Assert.False(TuitionService.AccruableMonth(m, "2026-06")); // muzlatish oyi — qisman, yozilmaydi
        Assert.True(TuitionService.AccruableMonth(m, "2026-03"));  // haqiqiy bo'shliq — yoziladi
        Assert.False(TuitionService.AccruableMonth(m, "2026-01")); // aktivlashtirish oyi — qisman
    }

    [Fact]
    public void AccruableMonth_PastPeriods_BOSH_bolsa_ESKI_shart_AYNAN_saqlanadi()
    {
        var m = Sg("active", "2026-01-10", "2026-05-15");
        foreach (var month in new[] { "2025-12", "2026-01", "2026-02", "2026-05", "2026-06" })
        {
            var eski = m.ActivatedAt.Length >= 7 && string.CompareOrdinal(month, m.ActivatedAt[..7]) > 0
                       && (m.FrozenAt.Length < 7 || string.CompareOrdinal(month, m.FrozenAt[..7]) < 0);
            Assert.Equal(eski, TuitionService.AccruableMonth(m, month));
        }
    }

    [Fact]
    public void ClosePeriod_BUZUQ_sana_yozilmaydi()
    {
        // Aktivlashtirish endpointi sanani validatsiya qilmaydi — bazaga "2026-13-99" tushishi mumkin.
        // Bunday qator oylar oralig'ini hisoblaganda CHEKSIZ siklga olib borardi.
        var buzuq = Sg("frozen", "2026-13-99", "2026-05-15");
        MembershipLifecycle.ClosePeriod(buzuq, "2026-09-01");
        Assert.Empty(buzuq.PastPeriods);

        var buzuqTugash = Sg("frozen", "2026-01-10", "2026-99-99");
        MembershipLifecycle.ClosePeriod(buzuqTugash, "yaroqsiz");
        Assert.Empty(buzuqTugash.PastPeriods);
    }

    [Fact]
    public void MonthRange_BUZUQ_oy_bilan_osilib_qolmaydi()
    {
        // "2026-13" ordinal solishtiruvda hech qachon "2027-01" dan katta bo'lmaydi
        // ("2026-100" < "2027-01") — xavfsizlik chegarasi bo'lmasa sikl abadiy davom etardi.
        var oylar = TuitionService.MonthRange("2026-13", "2027-01").Take(5000).Count();
        Assert.True(oylar <= 1200, $"MonthRange chegarasi ishlamadi: {oylar}");
    }

    [Fact]
    public void TruncatePastPeriodsAfter_orqaga_sanalgan_muzlatish_ESKI_davrni_ham_qirqadi()
    {
        // P1: 2026-01-10..2026-06-15. Keyin 2026-11 da admin ORQAGA sanalgan muzlatish qiladi
        // (2026-03-01) — purge 03 dan keyingi HAMMA hisobni o'chiradi, jumladan P1 ichidagi
        // 04/05/06 ni. Davr qirqilmasa, accrual ularni QAYTA yozib qo'yardi.
        var m = Sg("active", "2026-07-01", "", "2026-01-10|2026-06-15");
        MembershipLifecycle.TruncatePastPeriodsAfter(m, "2026-03-01");

        Assert.Equal(new[] { "2026-01-10|2026-03-01" }, m.PastPeriods);
        Assert.False(TuitionService.AccruableMonth(m, "2026-04"));
        Assert.False(TuitionService.AccruableMonth(m, "2026-05"));
        Assert.True(TuitionService.AccruableMonth(m, "2026-02")); // qirqishdan oldingi oy qoladi
    }

    [Fact]
    public void TruncatePastPeriodsAfter_butunlay_KEYIN_boshlangan_davr_ochiriladi()
    {
        var m = Sg("active", "2026-09-01", "", "2026-01-10|2026-02-20", "2026-05-01|2026-06-10");
        MembershipLifecycle.TruncatePastPeriodsAfter(m, "2026-03-01");
        Assert.Equal(new[] { "2026-01-10|2026-02-20" }, m.PastPeriods); // birinchisi tegilmagan
    }

    [Fact]
    public void TruncatePastPeriodsAfter_BUZUQ_sana_va_bosh_royxat_xavfsiz()
    {
        var m = Sg("active", "2026-09-01", "", "2026-01-10|2026-06-15");
        MembershipLifecycle.TruncatePastPeriodsAfter(m, "yaroqsiz");
        Assert.Equal(new[] { "2026-01-10|2026-06-15" }, m.PastPeriods); // tegilmaydi

        var bosh = Sg("active", "2026-09-01", "");
        MembershipLifecycle.TruncatePastPeriodsAfter(bosh, "2026-03-01");
        Assert.Empty(bosh.PastPeriods);
    }
}
