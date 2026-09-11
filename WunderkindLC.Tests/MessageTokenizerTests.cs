using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;
using DomainGroup = WunderkindLC.Domain.Group;
using DomainLead = WunderkindLC.Domain.Lead;
using DomainStudent = WunderkindLC.Domain.Student;
using DomainTeacher = WunderkindLC.Domain.Teacher;

namespace WunderkindLC.Tests;

/// <summary>
/// <see cref="MessageTokenizer"/> — SMS / Telegram e'lon / Push matnidagi <c>{token}</c>larni
/// real ma'lumotga almashtiradi. BITTA joy — barcha kanal va auditoriyalar shunga tayanadi,
/// shuning uchun xatosi darrov MINGLAB xabarga tarqaladi (noto'g'ri summa, ochiq qolgan
/// <c>{token}</c>, buzilgan matn).
/// </summary>
public class MessageTokenizerTests
{
    /// <summary>Sinov o'quvchisi — barcha maydonlar to'ldirilgan.</summary>
    private static DomainStudent Oquvchi() => new()
    {
        FullName = "Valiyev Ali Botirovich",
        LastName = "Valiyev",
        FirstName = "Ali",
        MiddleName = "Botirovich",
        ClassName = "Ingliz-A1",
        BirthDate = "2012-05-14",
        Address = "Qo'qon sh., Turkiston 12",
        Phone = "+998-90-111-22-33",
        FatherFullName = "Valiyev Botir",
        FatherPhone = "+998-90-222-33-44",
        MotherFullName = "Valiyeva Zulfiya",
        MotherPhone = "+998-90-333-44-55",
        Balance = -150000m,
    };

    /* =========================================================================================
     *  Money / MoneyPlain / MonthNameUz
     * ========================================================================================= */

    [Theory]
    [InlineData(0, "0 so'm")]
    [InlineData(1500000, "1 500 000 so'm")]
    [InlineData(400000, "400 000 so'm")]
    [InlineData(-50000, "-50 000 so'm")]
    public void Money_ProbelBilanAjratiladi(int qiymat, string kutilgan)
    {
        Assert.Equal(kutilgan, MessageTokenizer.Money(qiymat));
    }

    [Theory]
    [InlineData(400000, "400000")]
    [InlineData(0, "0")]
    public void MoneyPlain_ProbelsizVaSomsiz(int qiymat, string kutilgan)
    {
        // SMS uchun: "so'm" andozaning o'zida yoziladi (Eskiz moderatsiyasi uchun qulay).
        Assert.Equal(kutilgan, MessageTokenizer.MoneyPlain(qiymat));
    }

    [Theory]
    [InlineData(1, "yanvar")]
    [InlineData(7, "iyul")]
    [InlineData(12, "dekabr")]
    [InlineData(0, "")]
    [InlineData(13, "")]
    [InlineData(-3, "")]
    public void MonthNameUz_DiapazondanTashqari_BoshSatr(int oy, string kutilgan)
    {
        Assert.Equal(kutilgan, MessageTokenizer.MonthNameUz(oy));
    }

    /* =========================================================================================
     *  Student — ma'lum tokenlar almashadi
     * ========================================================================================= */

    [Fact]
    public void Student_AsosiyTokenlar_Almashadi()
    {
        var text = "{fish} / {ism} / {familiya} / {sharif} / {guruh} / {sinf} / {tugilgan} / {manzil}";

        var r = MessageTokenizer.Student(text, Oquvchi(), null, null, "Wunderkind");

        Assert.Equal(
            "Valiyev Ali Botirovich / Ali / Valiyev / Botirovich / Ingliz-A1 / Ingliz-A1 / "
            + "2012-05-14 / Qo'qon sh., Turkiston 12", r);
    }

    [Fact]
    public void Student_QarzdorlikVaBalans()
    {
        // Balans manfiy = qarzdor; {qarzdorlik} MUSBAT summa bo'lishi kerak (SMS'da "-150 000
        // so'm qarzingiz bor" deb chiqmasin).
        var r = MessageTokenizer.Student("{qarzdorlik} | {balans}", Oquvchi(), null, null, null);

        Assert.Equal("150 000 so'm | -150 000 so'm", r);
    }

    [Fact]
    public void Student_QarziYoq_QarzdorlikNol()
    {
        var s = Oquvchi();
        s.Balance = 250000m; // avans

        var r = MessageTokenizer.Student("{qarzdorlik}", s, null, null, null);

        Assert.Equal("0 so'm", r);
    }

    [Fact]
    public void Student_OtaOnaBerilmasa_StandartMatn()
    {
        var r = MessageTokenizer.Student("{ota-ona} | {ota_ona}", Oquvchi(), null, null, null);

        Assert.Equal("Ota-ona | Ota-ona", r);
    }

    [Fact]
    public void Student_OtaOnaTokeni_OtaTokenidanOldinAlmashadi()
    {
        // {ota} tokeni {ota-ona} ning ichida ham uchraydi — noto'g'ri tartibda almashtirilsa
        // "{ota-ona}" → "Valiyev Botir-ona" bo'lib qolardi.
        var r = MessageTokenizer.Student("{ota-ona} va {ota}", Oquvchi(), "Valiyev Botir", null, null);

        Assert.Equal("Valiyev Botir va Valiyev Botir", r);
    }

    [Fact]
    public void Student_NomalumToken_Ozgarishsiz()
    {
        var r = MessageTokenizer.Student("Salom {nomalum_token} va {fish}", Oquvchi(), null, null, null);

        Assert.Equal("Salom {nomalum_token} va Valiyev Ali Botirovich", r);
    }

    [Fact]
    public void Student_TokenRegistrgaBogliqEmas()
    {
        var r = MessageTokenizer.Student("{FISH} — {Ism}", Oquvchi(), null, null, null);

        Assert.Equal("Valiyev Ali Botirovich — Ali", r);
    }

    [Fact]
    public void Student_NullMaydonlar_BoshSatr_IstisnoYoq()
    {
        // contactPhone/centerName/teacherName null — istisno bo'lmasligi va tokenlar
        // matnda OCHIQ qolmasligi kerak.
        var s = new DomainStudent { FullName = "Ali" };

        var r = MessageTokenizer.Student("[{telefon}][{markaz}][{oqituvchi}][{manzil}][{tugilgan}]",
            s, null, null, null);

        Assert.Equal("[][][][][]", r);
        Assert.DoesNotContain("{", r);
    }

    [Fact]
    public void Student_UmumiyTokenlar_SanaOyYil()
    {
        // AppClock statik — kutilgan qiymatni AYNI manbadan NISBIY quramiz.
        var now = AppClock.Now;

        var r = MessageTokenizer.Student("{markaz} {sana} {oy} {yil}", Oquvchi(), null, null, "Wunderkind");

        Assert.Equal($"Wunderkind {now:dd.MM.yyyy} {MessageTokenizer.MonthNameUz(now.Month)} {now.Year}", r);
    }

    [Fact]
    public void Student_ExtraTokenlar_Qollanadi()
    {
        var extra = new Dictionary<string, string> { ["{summa}"] = "400 000", ["{sabab}"] = "oylik" };

        var r = MessageTokenizer.Student("{summa} — {sabab}", Oquvchi(), null, null, null, extra);

        Assert.Equal("400 000 — oylik", r);
    }

    /* =========================================================================================
     *  Regex xavfsizligi — qiymat ichidagi maxsus belgilar
     * ========================================================================================= */

    [Fact]
    public void Rep_QiymatdaDollarBelgisi_RegexBuzilmaydi()
    {
        // $1 — Regex.Replace uchun "birinchi guruh" degani. Kod uni $$ bilan ekranlaydi;
        // aks holda matn yo'qolardi (yoki istisno bo'lardi).
        var s = Oquvchi();
        s.FullName = "Ali $1 va $$ va $& Valiyev";

        var r = MessageTokenizer.Student("F.I.Sh: {fish}", s, null, null, null);

        Assert.Equal("F.I.Sh: Ali $1 va $$ va $& Valiyev", r);
    }

    [Fact]
    public void Rep_TokenIchidaMaxsusBelgilar_EkranlangaN()
    {
        // Token o'zi regex sifatida talqin qilinmasligi kerak (Regex.Escape) — masalan
        // "{ota-ona}" dagi "-" yoki extra kalitidagi qavslar.
        var extra = new Dictionary<string, string> { ["{narx(2)}"] = "5000", ["[a.b]"] = "X" };

        var r = MessageTokenizer.ApplyExtra("{narx(2)} va [a.b] va [aXb]", extra);

        Assert.Equal("5000 va X va [aXb]", r);
    }

    /* =========================================================================================
     *  ApplyExtra
     * ========================================================================================= */

    [Fact]
    public void ApplyExtra_Null_MatnOzgarmaydi()
    {
        Assert.Equal("Salom {summa}", MessageTokenizer.ApplyExtra("Salom {summa}", null));
    }

    [Fact]
    public void ApplyExtra_BoshLugat_MatnOzgarmaydi()
    {
        var r = MessageTokenizer.ApplyExtra("Salom {summa}", new Dictionary<string, string>());

        Assert.Equal("Salom {summa}", r);
    }

    [Fact]
    public void ApplyExtra_NomalumTokenQoladi()
    {
        var extra = new Dictionary<string, string> { ["{summa}"] = "1000" };

        Assert.Equal("1000 {qolgan}", MessageTokenizer.ApplyExtra("{summa} {qolgan}", extra));
    }

    /* =========================================================================================
     *  Teacher / Lead
     * ========================================================================================= */

    [Fact]
    public void Teacher_OquvchiTokenlari_BoshQoladi()
    {
        var t = new DomainTeacher { FullName = "Karimov Aziz", Phone = "+998-90-777-88-99" };

        var r = MessageTokenizer.Teacher("{fish}|{telefon}|{qarzdorlik}|{balans}|{ota}|{sinf}", t, null);

        Assert.Equal("Karimov Aziz|+998-90-777-88-99||||", r);
    }

    [Fact]
    public void Teacher_Oqituvchi_OziningFishi()
    {
        var t = new DomainTeacher { FullName = "Karimov Aziz" };

        Assert.Equal("Karimov Aziz", MessageTokenizer.Teacher("{oqituvchi}", t, null));
    }

    [Fact]
    public void Lead_LidTokenlari()
    {
        var l = new DomainLead
        {
            FullName = "Sobirov Jasur",
            Phone = "+998-90-444-55-66",
            InterestSubject = "Matematika",
            FatherFullName = "Sobirov Anvar",
        };

        var r = MessageTokenizer.Lead("{fish}|{fan}|{ota}|{oquvchi_telefon}|{guruh}|{balans}", l, null, null);

        Assert.Equal("Sobirov Jasur|Matematika|Sobirov Anvar|+998-90-444-55-66||", r);
    }

    /* =========================================================================================
     *  Dars jadvali tokenlari
     * ========================================================================================= */

    [Fact]
    public void Student_DarsJadvaliTokenlari_GuruhdanToladi()
    {
        var g = new DomainGroup
        {
            Name = "Ingliz-A1",
            StartDate = "2026-06-30",
            StartTime = "11:20",
            EndTime = "12:50",
            Days = new List<int> { 4, 0, 2 }, // tartibsiz — chiqishda saralanishi kerak
        };

        var r = MessageTokenizer.Student("{dars_sana} {dars_vaqti} {dars_kunlari}",
            Oquvchi(), null, null, null, group: g);

        Assert.Equal("30.06.2026 11:20-12:50 Du, Chor, Jum", r);
    }

    [Fact]
    public void Student_GuruhBerilmasa_JadvalTokenlariBosh()
    {
        var r = MessageTokenizer.Student("[{dars_sana}][{dars_vaqti}][{dars_kunlari}]",
            Oquvchi(), null, null, null);

        Assert.Equal("[][][]", r);
    }

    [Fact]
    public void Student_TugashVaqtiYoq_FaqatBoshlanish()
    {
        var g = new DomainGroup { StartTime = "09:00", EndTime = "" };

        var r = MessageTokenizer.Student("{dars_vaqti}", Oquvchi(), null, null, null, group: g);

        Assert.Equal("09:00", r);
    }

    [Fact]
    public void Lead_SinovDarsiVaqti_DarsSanaVaqtiniBeradi()
    {
        var l = new DomainLead { FullName = "Jasur" };

        var r = MessageTokenizer.Lead("{dars_sana} {dars_vaqti}", l, null, null,
            trialAt: "2026-08-15T14:30");

        Assert.Equal("15.08.2026 14:30", r);
    }

    /* =========================================================================================
     *  TeacherNameOf
     * ========================================================================================= */

    [Fact]
    public void TeacherNameOf_TurliHolatlar()
    {
        var names = new Dictionary<string, string> { ["t1"] = "Karimov Aziz" };

        Assert.Equal("Karimov Aziz", MessageTokenizer.TeacherNameOf(new DomainGroup { TeacherId = "t1" }, names));
        Assert.Equal("", MessageTokenizer.TeacherNameOf(new DomainGroup { TeacherId = "yoq" }, names));
        Assert.Equal("", MessageTokenizer.TeacherNameOf(new DomainGroup { TeacherId = "" }, names));
        Assert.Equal("", MessageTokenizer.TeacherNameOf(null, names));
        Assert.Equal("", MessageTokenizer.TeacherNameOf(new DomainGroup { TeacherId = "t1" }, null));
    }

    /* =========================================================================================
     *  MA'LUM XATOLAR — Skip bilan hujjatlashtirilgan (kutilgan TO'G'RI xulq yozilgan)
     * ========================================================================================= */

    [Fact(Skip = "XATO (MessageTokenizer.cs:43-45, 51-58): extra lug'atida BO'SH kalit bo'lsa "
                 + "Regex.Replace(input, \"\") har pozitsiyaga mos keladi va qiymat HAR BELGI "
                 + "orasiga qo'shilib matnni butunlay buzadi. Tuzatish: Rep() boshida "
                 + "string.IsNullOrEmpty(token) bo'lsa input'ni o'zgarishsiz qaytarish")]
    public void ApplyExtra_BoshKalit_MatnniBuzmasligiKerak()
    {
        // KUTILGAN: bo'sh kalit e'tiborsiz qoldiriladi → matn o'zgarmaydi.
        // HOZIRGI: "Salom" → "XSXaXlXoXmX".
        var extra = new Dictionary<string, string> { [""] = "X" };

        Assert.Equal("Salom", MessageTokenizer.ApplyExtra("Salom", extra));
    }

    [Fact]
    public void ApplyExtra_BoshKalit_HozirgiXulq()
    {
        // Yuqoridagi xato tuzatilgunga qadar HOZIRGI xulqni qayd etamiz (regressiya sezilsin).
        var extra = new Dictionary<string, string> { [""] = "X" };

        Assert.Equal("XSXaXlXoXmX", MessageTokenizer.ApplyExtra("Salom", extra));
    }

    [Fact(Skip = "XATO (MessageTokenizer.cs:51-58 va Student/Teacher/Lead ketma-ket Rep chaqiruvlari): "
                 + "almashtirilgan QIYMAT ichidagi token keyingi passda QAYTA kengayadi. Ma'lumot "
                 + "(masalan o'quvchi F.I.Sh) tokenga o'xshash matn bo'lsa xabar buziladi — "
                 + "hujjatga kirmagan \"token in'ektsiyasi\". Tuzatish: bitta o'tishda "
                 + "MatchEvaluator bilan almashtirish (barcha tokenlarni bitta regexda)")]
    public void Student_QiymatIchidagiToken_QaytaKengaymasligiKerak()
    {
        // KUTILGAN: {fish} → "{qarzdorlik}" matni AYNAN shunday qolishi kerak.
        // HOZIRGI: keyinroq {qarzdorlik} passi uni balansga aylantiradi.
        var s = Oquvchi();
        s.FullName = "{qarzdorlik}";

        Assert.Equal("{qarzdorlik}", MessageTokenizer.Student("{fish}", s, null, null, null));
    }

    [Fact]
    public void Student_QiymatIchidagiToken_HozirgiXulq()
    {
        // Yuqoridagi xatoning HOZIRGI natijasi: F.I.Sh o'rniga qarzdorlik summasi chiqadi.
        var s = Oquvchi();
        s.FullName = "{qarzdorlik}";

        Assert.Equal("150 000 so'm", MessageTokenizer.Student("{fish}", s, null, null, null));
    }
}
