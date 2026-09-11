using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// XODIM RUXSATLARI — qoidaning o'zi (<see cref="PermissionRules"/>).
///
/// <para>Bu xavfsizlikning tayanch nuqtasi: shu yerda xato bo'lsa, tor ruxsatli xodim boshqa
/// bo'limning ma'lumotini (jumladan <c>/uploads</c> dagi hujjat manzillarini) ochib olishi mumkin.
/// Mantiq <c>AdminPermAttribute</c> dan ajratib olingan — test loyihasi Server qatlamiga
/// bog'lanmagani uchun.</para>
/// </summary>
public class PermissionRulesTests
{
    // ---------------------------------------------------------------- HasSection

    [Fact]
    public void HasSection_TOLIQruxsat_Beradi()
    {
        Assert.True(PermissionRules.HasSection(["students"], "students"));
    }

    [Fact]
    public void HasSection_BITTAamalRuxsati_HamYETADI()
    {
        // "Faqat qo'shish" ruxsati bor xodim ham bo'lim ma'lumotini o'qiy oladi.
        Assert.True(PermissionRules.HasSection(["students:create"], "students"));
        Assert.True(PermissionRules.HasSection(["students:delete"], "students"));
    }

    [Fact]
    public void HasSection_BoshqaBOLIM_BERMAYDI()
    {
        Assert.False(PermissionRules.HasSection(["finance", "books:create"], "students"));
    }

    [Fact]
    public void HasSection_NOMIOXSHASHbolim_ADASHTIRMAYDI()
    {
        // Eng nozik joy: oddiy "boshlanadi" solishtiruvi bo'lsa, "students-arxiv" ruxsati
        // "students" ni ochib yuborardi. Faqat tenglik yoki "students:" prefiksi hisoblanadi.
        Assert.False(PermissionRules.HasSection(["students-arxiv"], "students"));
        Assert.False(PermissionRules.HasSection(["students2:create"], "students"));
        Assert.False(PermissionRules.HasSection(["studentsx"], "students"));
    }

    [Fact]
    public void HasSection_BOSHrouyxatVaNULL_BERMAYDI()
    {
        Assert.False(PermissionRules.HasSection([], "students"));
        Assert.False(PermissionRules.HasSection(null, "students"));
        Assert.False(PermissionRules.HasSection(["students"], ""));
    }

    // ---------------------------------------------------------------- HasFullSection

    [Fact]
    public void HasFullSection_FAQATyalangBolimniQabulQiladi()
    {
        Assert.True(PermissionRules.HasFullSection(["students"], "students"));
        // Bitta amal ruxsati TO'LIQ emas — nozik amallar (parol eksporti) uchun muhim.
        Assert.False(PermissionRules.HasFullSection(["students:create"], "students"));
    }

    // ---------------------------------------------------------------- Yozish

    [Theory]
    [InlineData("POST", "create")]
    [InlineData("post", "create")]
    [InlineData("DELETE", "delete")]
    [InlineData("PUT", "edit")]
    [InlineData("PATCH", "edit")]
    public void ActionFor_HTTPamaliniRuxsatHarakatigaOgiradi(string method, string kutilgan)
    {
        Assert.Equal(kutilgan, PermissionRules.ActionFor(method));
    }

    [Fact]
    public void CanWrite_ANIQamalRuxsati_FAQATOZamaliniOchadi()
    {
        string[] faqatQoshish = ["students:create"];

        Assert.True(PermissionRules.CanWrite(faqatQoshish, "students", "POST"));
        // Qo'shish ruxsati o'chirishga yaramaydi.
        Assert.False(PermissionRules.CanWrite(faqatQoshish, "students", "DELETE"));
        Assert.False(PermissionRules.CanWrite(faqatQoshish, "students", "PUT"));
    }

    [Fact]
    public void CanWrite_TOLIQruxsat_BARCHAamalniOchadi()
    {
        string[] toliq = ["students"];

        Assert.True(PermissionRules.CanWrite(toliq, "students", "POST"));
        Assert.True(PermissionRules.CanWrite(toliq, "students", "PUT"));
        Assert.True(PermissionRules.CanWrite(toliq, "students", "DELETE"));
    }

    [Fact]
    public void CanWrite_RuxsatYOQ_YOZDIRMAYDI()
    {
        Assert.False(PermissionRules.CanWrite(["finance"], "students", "POST"));
        Assert.False(PermissionRules.CanWrite(null, "students", "POST"));
    }
}

/// <summary>
/// SAHIFA (page) darajasidagi ruxsatlar — <c>"bolim.sahifa"</c>.
///
/// <para>Bu yerdagi xato ikki xil zarar keltiradi: (1) mavjud xodimning ruxsati YO'QOLADI
/// (bo'lim berilgan, lekin sahifa ochilmaydi), (2) tor ruxsatli xodim BUTUN bo'limga yozadi
/// (masalan turniket operatori o'quvchi yaratadi). Ikkalasi ham tekshiriladi.</para>
/// </summary>
public class PermissionRulesPageTests
{
    // ------------------------------------------------- PASTGA meros (backward-compat)

    [Fact]
    public void BOLIMruxsati_UningHARsahifasiniOchadi()
    {
        // Eng muhim qoida: eski xodimlarda faqat "students" turibdi — hech narsa buzilmasin.
        Assert.True(PermissionRules.HasSection(["students"], "students.turnstile"));
        Assert.True(PermissionRules.CanWrite(["students"], "students.turnstile", "POST"));
        Assert.True(PermissionRules.CanWrite(["students:edit"], "students.turnstile", "PUT"));
        // Amal mos kelmasa — yo'q (bo'lim ruxsati ham amal bo'yicha cheklangan).
        Assert.False(PermissionRules.CanWrite(["students:edit"], "students.turnstile", "DELETE"));
    }

    // ------------------------------------------------- YUQORIGA: o'qish HA, yozish YO'Q

    [Fact]
    public void SAHIFAruxsati_BolimniOQISHgaOchadi()
    {
        // Sahifa o'z ma'lumotini bo'lim controlleridan o'qiydi — GET yopilib qolmasin.
        Assert.True(PermissionRules.HasSection(["students.turnstile"], "students"));
        Assert.True(PermissionRules.HasSection(["students.turnstile:edit"], "students"));
    }

    [Fact]
    public void SAHIFAruxsati_BolimgaYOZDIRMAYDI()
    {
        // Turniket operatori POST /admin/students bilan o'quvchi YARATA OLMAYDI.
        Assert.False(PermissionRules.CanWrite(["students.turnstile"], "students", "POST"));
        Assert.False(PermissionRules.CanWrite(["students.turnstile"], "students.list", "POST"));
        // Qo'shni sahifaga ham o'tmaydi.
        Assert.False(PermissionRules.CanWrite(["students.turnstile"], "students.face", "PUT"));
    }

    [Fact]
    public void SAHIFAruxsati_OZsahifasigaYOZADI()
    {
        Assert.True(PermissionRules.CanWrite(["students.turnstile"], "students.turnstile", "POST"));
        Assert.True(PermissionRules.CanWrite(["students.turnstile:create"], "students.turnstile", "POST"));
        Assert.False(PermissionRules.CanWrite(["students.turnstile:create"], "students.turnstile", "DELETE"));
    }

    // ------------------------------------------------- Adashish bo'lmasin

    [Fact]
    public void BOSHQAbolimSahifasi_ADASHTIRMAYDI()
    {
        Assert.False(PermissionRules.HasSection(["finance.bonus"], "students"));
        Assert.False(PermissionRules.HasSection(["students-arxiv.turnstile"], "students"));
        Assert.False(PermissionRules.CanWrite(["teachers.list"], "students.list", "POST"));
    }

    [Fact]
    public void ParentOf_BolimniAjratadi()
    {
        Assert.Equal("students", PermissionRules.ParentOf("students.turnstile"));
        Assert.Null(PermissionRules.ParentOf("students"));
        Assert.Null(PermissionRules.ParentOf(""));
        Assert.Null(PermissionRules.ParentOf(null));
        // Ikki nuqtali kalit bo'lsa ham BIRINCHI qismi bo'lim (chuqurroq daraja qo'llanmaydi).
        Assert.Equal("settings", PermissionRules.ParentOf("settings.azure-speech"));
    }

    // ------------------------------------------------- Nozik (TO'LIQ) ruxsat

    [Fact]
    public void HasFullSection_BOLIMdanSahifagaMerosBoladi()
    {
        // Parol eksporti kabi nozik amal: yalang bo'lim ham, yalang sahifa ham yetadi.
        Assert.True(PermissionRules.HasFullSection(["students"], "students.list"));
        Assert.True(PermissionRules.HasFullSection(["students.list"], "students.list"));
        Assert.False(PermissionRules.HasFullSection(["students.list:edit"], "students.list"));
        // Sahifa ruxsati BO'LIMning nozik amalini OCHMAYDI.
        Assert.False(PermissionRules.HasFullSection(["students.list"], "students"));
    }
}
