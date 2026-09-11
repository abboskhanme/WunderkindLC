using WunderkindLC.Application.Services;
using Xunit;
using Slot = WunderkindLC.Application.Services.ScheduleRules.Slot;

namespace WunderkindLC.Tests;

/// <summary>
/// DARS JADVALI qoidalari. Asosiy talab: "xonada 4-soatda dars bor, 5-soat bo'sh,
/// 6-soatda yana dars" — o'sha ORADAGI teshikni topish.
/// </summary>
public class ScheduleRulesTests
{
    private static Slot S(int day, string from, string to) =>
        ScheduleRules.MakeSlot(day, from, to)!.Value;

    // ---------- Vaqtni o'qish ----------

    [Theory]
    [InlineData("09:30", 570)]
    [InlineData("00:00", 0)]
    [InlineData("23:59", 1439)]
    [InlineData("9:05", 545)]
    public void ParseTime_TogriQiymat(string text, int expected) =>
        Assert.Equal(expected, ScheduleRules.ParseTime(text));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0900")]      // ajratkichsiz
    [InlineData("24:00")]     // soat chegaradan tashqarida
    [InlineData("10:75")]     // daqiqa chegaradan tashqarida
    [InlineData("abc")]
    [InlineData(":30")]
    [InlineData("10:")]
    public void ParseTime_BuzuqQiymat_Null(string? text) =>
        Assert.Null(ScheduleRules.ParseTime(text));

    [Fact]
    public void MakeSlot_BuzuqQator_JimTashlanadi()
    {
        // Bazadagi HAQIQIY buzuq qatorlar: tugash boshlanishdan oldin va tugash bo'sh.
        Assert.Null(ScheduleRules.MakeSlot(0, "13:30", "03:00"));
        Assert.Null(ScheduleRules.MakeSlot(0, "21:29", ""));
        // Tugash == boshlanish ham dars emas (nol uzunlik).
        Assert.Null(ScheduleRules.MakeSlot(0, "10:00", "10:00"));
        // Kun raqami chegaradan tashqarida.
        Assert.Null(ScheduleRules.MakeSlot(7, "09:00", "10:00"));
    }

    // ---------- Bo'sh oraliqlar ----------

    [Fact]
    public void FindGaps_IkkiDarsOrasidagiTeshikTopiladi()
    {
        // 09:00–10:30 dars, 12:00–13:30 dars → orada 90 daqiqa bo'sh.
        var gaps = ScheduleRules.FindGaps([S(0, "09:00", "10:30"), S(0, "12:00", "13:30")]);
        var g = Assert.Single(gaps);
        Assert.Equal(0, g.Day);
        Assert.Equal(90, g.Minutes);
        Assert.Equal("10:30", ScheduleRules.FormatTime(g.StartMin));
        Assert.Equal("12:00", ScheduleRules.FormatTime(g.EndMin));
    }

    [Fact]
    public void FindGaps_KunBoshiVaOxiri_TESHIK_EMAS()
    {
        // Bitta dars: undan oldin ham, keyin ham bo'sh — lekin bu "teshik" emas,
        // aks holda har xonada har kuni ikkita soxta yozuv chiqardi.
        Assert.Empty(ScheduleRules.FindGaps([S(0, "09:00", "10:30")]));
    }

    [Fact]
    public void FindGaps_QisqaTanaffus_Sanalmaydi()
    {
        // 10:30–11:00 = 30 daqiqa: standart chegara (60) dan kichik — teshik emas.
        Assert.Empty(ScheduleRules.FindGaps([S(0, "09:00", "10:30"), S(0, "11:00", "12:30")]));
        // Chegara pasaytirilsa — ko'rinadi.
        Assert.Single(ScheduleRules.FindGaps(
            [S(0, "09:00", "10:30"), S(0, "11:00", "12:30")], minMinutes: 30));
    }

    [Fact]
    public void FindGaps_UstmaUstDarslar_BIRLASHTIRILADI()
    {
        // Xona ikki marta band qilingan (to'qnashuv faqat ogohlantirish edi).
        // Birlashtirmasak 10:30→10:00 kabi MANFIY teshik chiqardi.
        var gaps = ScheduleRules.FindGaps(
            [S(0, "09:00", "10:30"), S(0, "10:00", "11:00"), S(0, "12:00", "13:00")]);
        var g = Assert.Single(gaps);
        Assert.Equal("11:00", ScheduleRules.FormatTime(g.StartMin));
        Assert.Equal(60, g.Minutes);
    }

    [Fact]
    public void FindGaps_HarKunALOHIDA()
    {
        // Dushanbadagi dars va seshanbadagi dars orasida "teshik" bo'lmaydi.
        var gaps = ScheduleRules.FindGaps([S(0, "09:00", "10:30"), S(1, "15:00", "16:30")]);
        Assert.Empty(gaps);
    }

    [Fact]
    public void FindGaps_BirKundaIkkitaTeshik()
    {
        var gaps = ScheduleRules.FindGaps(
            [S(2, "08:00", "09:30"), S(2, "11:00", "12:30"), S(2, "15:00", "16:30")]);
        Assert.Equal(2, gaps.Count);
        Assert.Equal(90, gaps[0].Minutes);
        Assert.Equal(150, gaps[1].Minutes);
    }

    // ---------- Bandlik ----------

    [Fact]
    public void IsFree_UstmaUstTushsa_BandDeydi()
    {
        var busy = new[] { S(0, "09:00", "10:30") };
        Assert.False(ScheduleRules.IsFree(busy, 0, 10 * 60, 11 * 60));   // qisman ustma-ust
        Assert.True(ScheduleRules.IsFree(busy, 0, 10 * 60 + 30, 12 * 60)); // tegib turadi, ustma-ust emas
        Assert.True(ScheduleRules.IsFree(busy, 1, 9 * 60, 10 * 60));     // boshqa kun
    }

    [Fact]
    public void TouchesLesson_OldidanYokiKetidan()
    {
        var busy = new[] { S(0, "09:00", "10:30"), S(0, "13:00", "14:00") };
        Assert.True(ScheduleRules.TouchesLesson(busy, 0, 10 * 60 + 30, 12 * 60));  // ketidan
        Assert.True(ScheduleRules.TouchesLesson(busy, 0, 11 * 60, 13 * 60));       // oldidan
        Assert.False(ScheduleRules.TouchesLesson(busy, 0, 11 * 60, 12 * 60));      // uzilgan
    }

    // ---------- Tig'izlik ----------

    [Fact]
    public void BusyHistogram_BirVaqtdaNechtaDars()
    {
        var h = ScheduleRules.BusyHistogram(
            [S(0, "09:00", "10:00"), S(0, "09:30", "10:30"), S(0, "14:00", "15:00")]);
        Assert.Equal(1, h[(0, 9 * 60)]);        // 09:00–09:30 — bitta
        Assert.Equal(2, h[(0, 9 * 60 + 30)]);   // 09:30–10:00 — ikkita
        Assert.Equal(1, h[(0, 10 * 60)]);       // 10:00–10:30 — bitta
        Assert.False(h.ContainsKey((0, 11 * 60)));
    }

    [Fact]
    public void PeakInRange_OraliqdagiEngYuqoriTigizlik()
    {
        var h = ScheduleRules.BusyHistogram(
            [S(0, "09:00", "10:00"), S(0, "09:30", "10:30")]);
        Assert.Equal(2, ScheduleRules.PeakInRange(h, 0, 9 * 60, 11 * 60));
        Assert.Equal(0, ScheduleRules.PeakInRange(h, 1, 9 * 60, 11 * 60));
    }

    // ---------- Chegara ----------

    [Theory]
    [InlineData(null, 60)]
    [InlineData(0, 15)]        // juda kichik → pastki chegara
    [InlineData(10_000, 480)]  // juda katta → yuqori chegara
    [InlineData(45, 45)]
    public void ClampGapMinutes(int? requested, int expected) =>
        Assert.Equal(expected, ScheduleRules.ClampGapMinutes(requested));
}
