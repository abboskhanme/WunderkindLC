using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// "BOG'LANISH KERAK" moduli qoidalari (<see cref="ContactService"/>) — bosqichlar, natijalar,
/// muddat va o'tish cheklovi. Butun navbat va hisobot shu kalitlarga tayanadi.
/// </summary>
public class ContactServiceTests
{
    // ==================== Bosqichlar ====================

    [Theory]
    [InlineData(ContactStatuses.New, true)]
    [InlineData(ContactStatuses.Callback, true)]
    [InlineData(ContactStatuses.Done, false)]
    [InlineData(ContactStatuses.Failed, false)]
    public void Ochiq_bosqichlar_faqat_new_va_callback(string status, bool expected)
        => Assert.Equal(expected, ContactService.IsOpen(status));

    [Fact]
    public void Notanish_bosqich_ochiq_hisoblanmaydi()
    {
        // Navbat "ochiq" ro'yxatiga kirmaydi — buzuq yozuv navbatni to'ldirib qo'ymasin.
        Assert.False(ContactService.IsOpen("allaqachon-yoq"));
        Assert.False(ContactService.IsOpen(null));
        Assert.False(ContactService.IsValidStatus("allaqachon-yoq"));
    }

    [Fact]
    public void Bosqich_yorligi_notanish_kalitda_kalitni_ozini_qaytaradi()
    {
        Assert.Equal("Hal bo'ldi", ContactService.StatusLabel(ContactStatuses.Done));
        // Yorliq topilmasa BO'SH emas, kalit qaytadi — tarixda "nima bo'lgani" ko'rinib tursin.
        Assert.Equal("yangi-bosqich", ContactService.StatusLabel("yangi-bosqich"));
    }

    // ==================== O'tish qoidasi ====================

    [Theory]
    [InlineData(ContactStatuses.Callback, true)]
    [InlineData(ContactStatuses.Done, true)]
    [InlineData(ContactStatuses.Failed, true)]
    // "new" ATAYIN taqiqlangan: bog'langandan keyin boshiga qaytish navbatni cheksiz aylantirardi
    // va hisobotda hech qanday bosqich ko'rinmasdi.
    [InlineData(ContactStatuses.New, false)]
    [InlineData("boshqa", false)]
    [InlineData(null, false)]
    public void Keyingi_bosqich_faqat_uchtadan_bolishi_mumkin(string? next, bool expected)
        => Assert.Equal(expected, ContactService.CanTransitionTo(next));

    // ==================== Natijalar ====================

    [Fact]
    public void BOGLANILDI_faqat_gaplashilgan_natijalarda_sanaladi()
    {
        // Kunlik hisobotdagi "nechta odam bilan bog'lanildi" AYNAN shu bo'yicha — ko'tarmagan
        // qo'ng'iroq urinish bo'ladi, lekin "bog'lanildi" EMAS.
        Assert.True(ContactService.Reached("answered"));
        Assert.True(ContactService.Reached("other"));
        Assert.False(ContactService.Reached("no_answer"));
        Assert.False(ContactService.Reached("busy"));
        Assert.False(ContactService.Reached("wrong_number"));
        // Noma'lum/bo'sh natija ham "bog'lanildi" deb sanalmaydi (hisobot shishib ketmasin).
        Assert.False(ContactService.Reached("allaqachon-yoq"));
        Assert.False(ContactService.Reached(null));
    }

    [Fact]
    public void Natija_kaliti_tekshiriladi()
    {
        Assert.True(ContactService.IsValidResult("no_answer"));
        Assert.False(ContactService.IsValidResult("koturmadi"));
        Assert.False(ContactService.IsValidResult(null));
    }

    // ==================== Muddat ====================

    [Theory]
    // Faqat "qayta qo'ng'iroq" bosqichida va sana BUGUNDAN OLDIN bo'lsa.
    [InlineData(ContactStatuses.Callback, "2026-08-04", "2026-08-05", true)]
    [InlineData(ContactStatuses.Callback, "2026-08-05", "2026-08-05", false)]   // bugun — hali o'tmagan
    [InlineData(ContactStatuses.Callback, "2026-08-06", "2026-08-05", false)]
    [InlineData(ContactStatuses.Callback, "", "2026-08-05", false)]             // sana yo'q
    [InlineData(ContactStatuses.New, "2026-08-01", "2026-08-05", false)]        // boshqa bosqich
    [InlineData(ContactStatuses.Done, "2026-08-01", "2026-08-05", false)]
    public void Muddati_otgan_faqat_qayta_qongiroqda_hisoblanadi(
        string status, string due, string today, bool expected)
        => Assert.Equal(expected, ContactService.IsOverdue(status, due, today));

    // ==================== Muddat guruhlari ("bugun kimga qo'ng'iroq kerak?") ====================

    private const string T = "2026-08-05";   // "bugun"

    [Theory]
    // Sanasiz "Bog'lanish kerak" — hoziroq navbatda turibdi.
    [InlineData(ContactStatuses.New, "", ContactService.Due.NoDate)]
    [InlineData(ContactStatuses.New, "2026-09-01", ContactService.Due.NoDate)]   // sana e'tiborsiz
    [InlineData(ContactStatuses.Callback, "2026-08-04", ContactService.Due.Overdue)]
    [InlineData(ContactStatuses.Callback, "2026-01-01", ContactService.Due.Overdue)]
    [InlineData(ContactStatuses.Callback, "2026-08-05", ContactService.Due.Today)]
    [InlineData(ContactStatuses.Callback, "2026-08-06", ContactService.Due.Tomorrow)]
    [InlineData(ContactStatuses.Callback, "2026-08-07", ContactService.Due.Week)]
    [InlineData(ContactStatuses.Callback, "2026-08-12", ContactService.Due.Week)]    // +7 — hali hafta
    [InlineData(ContactStatuses.Callback, "2026-08-13", ContactService.Due.Later)]   // +8 — keyinroq
    // Sanasiz "qayta qo'ng'iroq" bo'lmasligi kerak, lekin bo'lsa YO'QOLMASIN.
    [InlineData(ContactStatuses.Callback, "", ContactService.Due.NoDate)]
    public void Muddat_guruhi_togri_aniqlanadi(string status, string due, string expected)
        => Assert.Equal(expected, ContactService.BucketOf(status, due, T));

    [Theory]
    // Yakunlangan talab navbatda umuman yo'q.
    [InlineData(ContactStatuses.Done)]
    [InlineData(ContactStatuses.Failed)]
    public void Yakunlangan_talab_hech_qaysi_muddat_guruhiga_tushmaydi(string status)
        => Assert.Equal("", ContactService.BucketOf(status, "2026-08-05", T));

    [Fact]
    public void BUGUN_QILISH_KERAK_kechikkanlarni_va_sanasizlarni_ham_qamraydi()
    {
        // Aks holda operator "bugun 5 ta" deb ko'rib, kechagi 12 tasini ko'rmay qolardi.
        Assert.True(ContactService.IsTodo(ContactService.Due.Overdue));
        Assert.True(ContactService.IsTodo(ContactService.Due.Today));
        Assert.True(ContactService.IsTodo(ContactService.Due.NoDate));
        Assert.False(ContactService.IsTodo(ContactService.Due.Tomorrow));
        Assert.False(ContactService.IsTodo(ContactService.Due.Week));
        Assert.False(ContactService.IsTodo(ContactService.Due.Later));
        Assert.False(ContactService.IsTodo(""));      // yakunlangan
    }

    [Fact]
    public void Buzuq_sana_KEYINROQ_ga_tushadi_va_xato_bermaydi()
    {
        // Yo'qolib ketmaydi: navbatda "keyinroq" bo'lib ko'rinadi va admin tuzatishi mumkin.
        Assert.Equal(ContactService.Due.Later,
            ContactService.BucketOf(ContactStatuses.Callback, "2026-13-99", T));
    }

    [Fact]
    public void IsKnownDue_faqat_royxatdagini_tan_oladi()
    {
        Assert.True(ContactService.IsKnownDue(ContactService.Due.Todo));
        Assert.True(ContactService.IsKnownDue(ContactService.Due.NoDate));
        Assert.False(ContactService.IsKnownDue("bugun"));
        Assert.False(ContactService.IsKnownDue(null));
    }

    // ==================== Javoblar tahlili (TopWords) ====================

    [Fact]
    public void TopWords_eng_kop_uchragan_sozlarni_beradi()
    {
        var words = ContactService.TopWords(new[]
        {
            "To'lov juma kuni qiladi",
            "To'lov kechikdi, dushanbagacha so'radi",
            "Bola kasal, dars qoldirdi",
        });

        // "to'lov" ikki javobda uchradi — birinchi o'rinda.
        Assert.Equal("to'lov", words[0].Word);
        Assert.Equal(2, words[0].Count);
    }

    [Fact]
    public void TopWords_QOSHIMCHALIshakl_ALOHIDAsozDebSanaladi()
    {
        // ATAYLAB shunday: qo'shimchalar KESILMAYDI. O'zak ajratish (stemming) qo'shilsa,
        // ehtiyotsiz ro'yxat bog'liq bo'lmagan so'zlarni ham qo'shib yuborardi, natijada
        // hisobot noto'g'ri ko'rsatardi. Shuning uchun "to'lov" va "to'lovni" — ikki xil so'z.
        // Bu qaror shu test bilan yozib qo'yilgan: kelajakda kimdir buni "xato" deb
        // o'zgartirmasin (yoki o'zgartirsa — ongli ravishda, shu testni yangilab qilsin).
        var words = ContactService.TopWords(["To'lov keldi", "To'lovni kutmoqda"]);

        // Ikkalasi ham BITTA martadan — ya'ni birlashtirilmadi (aks holda "to'lov" 2 bo'lardi).
        Assert.Contains(words, w => w.Word == "to'lov" && w.Count == 1);
        Assert.Contains(words, w => w.Word == "to'lovni" && w.Count == 1);
    }

    [Fact]
    public void TopWords_bitta_javobdagi_takror_BIR_marta_sanaladi()
    {
        // Aks holda bitta uzun izoh butun hisobotni egallab olardi: savol "necha marta yozildi"
        // emas, "NECHTA JAVOBDA uchradi".
        var words = ContactService.TopWords(new[] { "kasal kasal kasal kasal" });
        var w = Assert.Single(words);
        Assert.Equal("kasal", w.Word);
        Assert.Equal(1, w.Count);
    }

    [Fact]
    public void TopWords_manosiz_va_qisqa_sozlarni_tashlaydi()
    {
        var words = ContactService.TopWords(new[] { "u va bilan uchun ham deb bo'ldi to'lov" });
        var w = Assert.Single(words);
        Assert.Equal("to'lov", w.Word);
    }

    [Theory]
    // Apostrof turlari BIR ko'rinishga keltiriladi — aks holda "to'lov" va "toʻlov" ikki xil
    // so'z bo'lib sanalardi (matn turli klaviaturalardan kiritiladi).
    [InlineData("to'lov")]
    [InlineData("toʻlov")]
    [InlineData("to’lov")]
    [InlineData("To`lov")]
    public void TopWords_apostroflarni_bir_xil_koradi(string text)
    {
        var w = Assert.Single(ContactService.TopWords(new[] { text }));
        Assert.Equal("to'lov", w.Word);
    }

    [Fact]
    public void TopWords_tinish_belgilari_va_bosh_matn_muammo_qilmaydi()
    {
        var words = ContactService.TopWords(new[] { "", "   ", "Kasal!!! Dars, qoldirdi." });
        Assert.Contains(words, w => w.Word == "kasal");
        Assert.Contains(words, w => w.Word == "dars");
        // Tinish belgisi so'zga yopishib qolmaydi.
        Assert.DoesNotContain(words, w => w.Word.Contains('!') || w.Word.Contains(','));
    }

    [Fact]
    public void TopWords_chegara_hurmat_qilinadi()
        => Assert.Equal(2, ContactService.TopWords(
            new[] { "birinchi ikkinchi uchinchi to'rtinchi" }, take: 2).Count);

    // ==================== Katalog butunligi ====================

    [Fact]
    public void Bosqich_va_natija_kalitlari_takrorlanmaydi()
    {
        Assert.Equal(ContactService.Statuses.Count,
            ContactService.Statuses.Select(s => s.Key).Distinct().Count());
        Assert.Equal(ContactService.Results.Count,
            ContactService.Results.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void Ochiq_bosqichlar_royxati_katalog_bilan_mos()
    {
        // `OpenStatuses` va `Statuses[].IsOpen` — ikkita joy, bir xil haqiqat bo'lishi SHART
        // (biri navbat so'rovida, ikkinchisi UI chiplarida ishlatiladi).
        var fromCatalog = ContactService.Statuses.Where(s => s.IsOpen).Select(s => s.Key).OrderBy(k => k);
        Assert.Equal(fromCatalog, ContactService.OpenStatuses.OrderBy(k => k));
    }

    [Fact]
    public void Entity_default_holati_navbatga_tushadi()
    {
        // Yangi talab hech narsa berilmasa ham NAVBATDA bo'lishi kerak — aks holda yaratilgan
        // talab hech kimga ko'rinmay yo'qolardi.
        Assert.True(ContactService.IsOpen(new ContactRequest().Status));
    }

    // ==================== Kanban ustunlari (ContactStage) ====================

    [Fact]
    public void Har_bazaviy_holat_uchun_AYNAN_bitta_tizim_ustuni_bor()
    {
        // Tizim ustuni — "StageId bo'sh" talabning uyi. Bir holat uchun ikkitasi bo'lsa qaysi
        // biriga tushishi noaniq bo'lardi; biri yetishmasa esa karta taxtadan YO'QOLARDI.
        var keys = ContactService.SystemStages.Select(x => x.Key).OrderBy(k => k).ToList();
        var statuses = ContactService.Statuses.Select(s => s.Key).OrderBy(k => k).ToList();
        Assert.Equal(statuses, keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Tizim_ustunining_Id_si_holat_kaliti_bilan_bir_xil()
    {
        // Bu ATAYIN: `StageId` bo'sh talab o'z holatining ustuniga qo'shimcha izlashsiz tushadi,
        // ya'ni eski qatorlarni to'ldirish (backfill) kerak emas.
        foreach (var def in ContactService.SystemStages)
            Assert.True(ContactService.IsValidStatus(def.Key));
    }

    [Fact]
    public void Tizim_ustunlarining_rangi_katalogdan()
    {
        foreach (var def in ContactService.SystemStages)
            Assert.True(ContactService.IsValidColor(def.Color));
    }

    [Theory]
    [InlineData("slate", "slate")]
    [InlineData("emerald", "emerald")]
    [InlineData("qandaydir-rang", "slate")]
    [InlineData("", "slate")]
    [InlineData(null, "slate")]
    public void Notanish_rang_slate_ga_tushadi(string? given, string expected)
        // Rang tufayli ustun YARATILMAY qolmasin — u ko'rinish, mantiq emas.
        => Assert.Equal(expected, ContactService.SafeColor(given));

    [Theory]
    [InlineData(ContactStatuses.New, true)]
    [InlineData(ContactStatuses.Callback, true)]
    [InlineData(ContactStatuses.Done, true)]
    [InlineData(ContactStatuses.Failed, true)]
    [InlineData("boshqa", false)]
    [InlineData(null, false)]
    public void Ustun_faqat_MAVJUD_bazaviy_holatga_boglanadi(string? baseStatus, bool expected)
        => Assert.Equal(expected, ContactService.CanAnchorTo(baseStatus));

    [Fact]
    public void Ustun_faqat_OZ_bazaviy_holatidagi_talabni_qabul_qiladi()
    {
        // Taxtaning asosiy qoidasi: ustun HOLATNI almashtirmaydi, unga bog'lanadi. Mos kelmasa
        // karta o'z holatiga ZID ustunda ko'rinib, hisobot bilan ziddiyat yuzaga kelardi.
        Assert.True(ContactService.StageMatches(ContactStatuses.Callback, ContactStatuses.Callback));
        Assert.False(ContactService.StageMatches(ContactStatuses.Done, ContactStatuses.Callback));
        Assert.False(ContactService.StageMatches("", ContactStatuses.Callback));
        Assert.False(ContactService.StageMatches(null, null));
    }

    [Fact]
    public void Yangi_ustun_HAM_ochiq_HAM_yakuniy_holatga_boglana_oladi()
    {
        // Foydalanuvchi "Bog'lanildi" kabi ORALIQ ustun ham, "Shartnoma imzolandi" kabi YAKUNIY
        // ustun ham qo'sha olishi kerak — cheklov faqat "mavjud holat bo'lsin".
        Assert.True(ContactService.CanAnchorTo(ContactStatuses.Callback));
        Assert.True(ContactService.CanAnchorTo(ContactStatuses.Done));
    }
}
