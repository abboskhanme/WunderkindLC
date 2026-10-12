using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using DomainGroup = WunderkindLC.Domain.Group; // System.Text.RegularExpressions.Group bilan ziddiyatni oldini olish

namespace WunderkindLC.Application.Services;

/// <summary>
/// SMS / Telegram e'lon / Push matnidagi {o'rinbosar}larni real ma'lumotga almashtiradi.
/// BITTA joy — barcha kanal va auditoriyalar (o'quvchi, ota-ona, o'qituvchi, lid) shu yerga tayanadi,
/// shuning uchun tokenlar har joyda bir xil ishlaydi.
///
/// Tokenlar braces bilan ajratilgani uchun ({ota} ⊄ {ota-ona}) almashtirish tartibi muhim emas.
/// </summary>
public static class MessageTokenizer
{
    private static readonly string[] UzMonths =
    {
        "yanvar", "fevral", "mart", "aprel", "may", "iyun",
        "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr",
    };

    // Hafta kunlari (0=Dushanba ... 6=Yakshanba) — guruh Days bilan mos. SMS uchun qisqa.
    private static readonly string[] UzWeekdaysShort =
    {
        "Du", "Se", "Chor", "Pay", "Jum", "Shan", "Yak",
    };

    /// <summary>So'm formatlash: 1 500 000 so'm (probel bilan ajratilgan).</summary>
    public static string Money(decimal v)
    {
        var nfi = new NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };
        return v.ToString("#,0", nfi) + " so'm";
    }

    /// <summary>Faqat raqam — probelsiz, "so'm"siz (SMS {summa} uchun): 400000.
    /// "so'm"ni andoza ichida yoziladi (masalan "{summa} so'm").</summary>
    public static string MoneyPlain(decimal v) =>
        v.ToString("0", CultureInfo.InvariantCulture);

    /// <param name="html">The target text is Telegram HTML (CRM upgrade P2.3): the substituted VALUE is
    /// HTML-escaped (<see cref="Messaging.TelegramHtml.Escape"/>) so data can never inject markup.</param>
    private static string Rep(string input, string token, string? value, bool html = false) =>
        Regex.Replace(input, Regex.Escape(token),
            (html ? Messaging.TelegramHtml.Escape(value) : value ?? "").Replace("$", "$$"), RegexOptions.IgnoreCase);

    /// <summary>Hodisaga xos qo'shimcha tokenlarni matnga BIRINCHI bo'lib qo'llaydi (masalan {summa},
    /// {sabab} — yoki {guruh}/{sana}/{oy} kabi standart tokenlarni USTIDAN yozish uchun). Dispatcher shuni
    /// entity tokenizeridan (Student/Teacher/Lead) OLDIN chaqiradi: shu bilan qo'shimcha qiymat standart
    /// qiymatdan ustun turadi (birinchi almashtirilgan token keyingi passda qayta almashmaydi).</summary>
    public static string ApplyExtra(string text, IReadOnlyDictionary<string, string>? extra, bool html = false)
    {
        if (extra is null) return text;
        var r = text;
        foreach (var (k, v) in extra)
            r = Rep(r, k, v, html);
        return r;
    }

    /// <summary>Oy raqami (1-12) → o'zbekcha nom (masalan 7 → "iyul"). Diapazondan tashqari bo'lsa "".</summary>
    public static string MonthNameUz(int month) =>
        month is >= 1 and <= 12 ? UzMonths[month - 1] : "";

    /// <summary>
    /// Guruh dars jadvali tokenlari (SMS andozalarida muhim):
    ///   {dars_sana}    — guruh boshlanish sanasi "DD.MM.YYYY" (masalan 30.06.2026);
    ///   {dars_vaqti}   — dars vaqti "HH:mm" yoki "HH:mm–HH:mm" (masalan 11:20 yoki 11:20–12:50);
    ///   {dars_kunlari} — hafta kunlari (masalan "Du, Chor, Jum").
    /// group=null bo'lsa (o'qituvchi yoki guruh topilmasa) — tokenlar bo'sh qoladi.
    /// </summary>
    private static string Schedule(string text, DomainGroup? g, bool html = false)
    {
        var sana = "";
        // StartDate "YYYY-MM-DD" → "DD.MM.YYYY".
        if (g?.StartDate is { Length: >= 10 } sd)
            sana = $"{sd[8..10]}.{sd[5..7]}.{sd[..4]}";
        var vaqt = "";
        // Oddiy defis (–/em-dash emas) — GSM-7 kodlash saqlanadi (SMS uzunligi/narxi oshmaydi).
        if (g is not null && !string.IsNullOrWhiteSpace(g.StartTime))
            vaqt = string.IsNullOrWhiteSpace(g.EndTime) ? g.StartTime : $"{g.StartTime}-{g.EndTime}";
        var kunlar = g is null ? "" : FormatDays(g.Days);
        return Schedule(text, sana, vaqt, kunlar, html);
    }

    /// <summary>Dars jadvali tokenlarini aniq qiymatlar bilan almashtiradi.</summary>
    private static string Schedule(string text, string sana, string vaqt, string kunlar, bool html = false)
    {
        var r = Rep(text, "{dars_sana}", sana, html);
        r = Rep(r, "{dars_vaqti}", vaqt, html);
        r = Rep(r, "{dars_kunlari}", kunlar, html);
        return r;
    }

    /// <summary>Guruh dars kunlari (Days, 0=Du..6=Yak) → "Du, Chor, Jum".</summary>
    private static string FormatDays(List<int> days)
    {
        if (days is null || days.Count == 0) return "";
        return string.Join(", ", days.Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d)
            .Select(d => UzWeekdaysShort[d]));
    }

    /// <summary>Barcha kanal/auditoriyalar uchun umumiy tokenlar: markaz nomi, joriy sana + ad-hoc tokenlar.</summary>
    private static string Common(string text, string? centerName, IReadOnlyDictionary<string, string>? extra, bool html = false)
    {
        var now = AppClock.Now;
        var r = Rep(text, "{markaz}", centerName, html);
        r = Rep(r, "{sana}", now.ToString("dd.MM.yyyy"), html);
        r = Rep(r, "{oy}", now.Month is >= 1 and <= 12 ? UzMonths[now.Month - 1] : "", html);
        r = Rep(r, "{yil}", now.Year.ToString(), html);
        // Hodisaga xos qo'shimcha tokenlar (masalan {summa} — to'lov, {natija} — test).
        if (extra is not null)
            foreach (var (k, v) in extra)
                r = Rep(r, k, v, html);
        return r;
    }

    /// <summary>
    /// Guruh o'qituvchisining F.I.Sh — <c>{oqituvchi}</c> tokeni uchun. <paramref name="group"/> berilmasa
    /// (yoki unda o'qituvchi bo'lmasa) va <paramref name="student"/> berilgan bo'lsa — o'quvchining ASOSIY
    /// guruhi (<see cref="Student.ClassName"/>) bo'yicha topiladi. Hech nima topilmasa "" qaytadi
    /// (token bo'sh qoladi, matnda "{oqituvchi}" ko'rinib qolmaydi).
    /// <para>DIQQAT: bu DB so'rovi — ro'yxat (bulk) yuborishda HAR o'quvchi uchun chaqirmang;
    /// o'qituvchi nomlarini bir marta lug'atga yig'ing (<see cref="TeacherNamesByIdAsync"/>).</para>
    /// </summary>
    public static async Task<string> GroupTeacherNameAsync(
        IAppDbContext db, DomainGroup? group, Student? student = null, CancellationToken ct = default)
    {
        var g = group;
        if ((g is null || string.IsNullOrEmpty(g.TeacherId)) && !string.IsNullOrWhiteSpace(student?.ClassName))
            g = await db.Classes.FirstOrDefaultAsync(c => c.Name == student!.ClassName, ct);
        if (g is null || string.IsNullOrEmpty(g.TeacherId)) return "";
        return await db.Teachers.Where(t => t.Id == g.TeacherId)
            .Select(t => t.FullName).FirstOrDefaultAsync(ct) ?? "";
    }

    /// <summary>O'qituvchi id → F.I.Sh lug'ati (ommaviy yuborishda {oqituvchi} tokenini N+1 so'rovsiz
    /// to'ldirish uchun). <see cref="TeacherNameOf"/> bilan birga ishlatiladi.</summary>
    public static async Task<Dictionary<string, string>> TeacherNamesByIdAsync(
        IAppDbContext db, CancellationToken ct = default) =>
        await db.Teachers.ToDictionaryAsync(t => t.Id, t => t.FullName, ct);

    /// <summary>Guruhning o'qituvchisi F.I.Sh — oldindan yuklangan lug'atdan (yo'q bo'lsa "").</summary>
    public static string TeacherNameOf(DomainGroup? group, IReadOnlyDictionary<string, string>? namesById) =>
        group is null || string.IsNullOrEmpty(group.TeacherId) || namesById is null
            ? ""
            : namesById.GetValueOrDefault(group.TeacherId, "");

    /// <summary>O'quvchi/ota-ona xabari — barcha o'quvchi tokenlari. group berilsa — dars jadvali tokenlari
    /// ({dars_sana}/{dars_vaqti}/{dars_kunlari}) to'ladi. teacherName berilsa — {oqituvchi} (guruh
    /// o'qituvchisining F.I.Sh); berilmasa token bo'sh qoladi.</summary>
    public static string Student(string text, Student s, string? parentName, string? contactPhone, string? centerName,
        IReadOnlyDictionary<string, string>? extra = null, DomainGroup? group = null, string? teacherName = null,
        bool html = false)
    {
        var debt = s.Balance < 0 ? -s.Balance : 0m;
        var parent = string.IsNullOrWhiteSpace(parentName) ? "Ota-ona" : parentName!;
        var r = text;
        r = Rep(r, "{fish}", s.FullName, html);
        r = Rep(r, "{ism}", s.FirstName, html);
        r = Rep(r, "{familiya}", s.LastName, html);
        r = Rep(r, "{sharif}", s.MiddleName, html);
        r = Rep(r, "{sinf}", s.ClassName, html);
        r = Rep(r, "{guruh}", s.ClassName, html);
        r = Rep(r, "{qarzdorlik}", Money(debt), html);
        r = Rep(r, "{balans}", Money(s.Balance), html);
        r = Rep(r, "{ota-ona}", parent, html);
        r = Rep(r, "{ota_ona}", parent, html);
        r = Rep(r, "{telefon}", contactPhone, html);
        r = Rep(r, "{ota}", s.FatherFullName, html);
        r = Rep(r, "{ota_telefon}", s.FatherPhone, html);
        r = Rep(r, "{ona}", s.MotherFullName, html);
        r = Rep(r, "{ona_telefon}", s.MotherPhone, html);
        r = Rep(r, "{oquvchi_telefon}", s.Phone, html);
        r = Rep(r, "{manzil}", s.Address, html);
        r = Rep(r, "{tugilgan}", s.BirthDate, html);
        r = Rep(r, "{oqituvchi}", teacherName, html); // guruh o'qituvchisi F.I.Sh (berilmasa bo'sh)
        r = Schedule(r, group, html); // guruh dars jadvali (sana/vaqt/kunlar)
        return Common(r, centerName, extra, html);
    }

    /// <summary>O'qituvchi xabari — {fish}/{telefon}/{manzil}/{tugilgan}; o'quvchi tokenlari bo'sh.
    /// <paramref name="group"/> berilsa — {guruh} shu guruh nomidan, dars jadvali tokenlari
    /// ({dars_sana}/{dars_vaqti}/{dars_kunlari}) shu guruhdan to'ladi (masalan davomat eslatmasi uchun).
    /// <paramref name="extra"/> — hodisaga xos qo'shimcha tokenlar (masalan {kurs}).</summary>
    public static string Teacher(string text, Teacher t, string? centerName,
        IReadOnlyDictionary<string, string>? extra = null, DomainGroup? group = null, bool html = false)
    {
        var r = text;
        r = Rep(r, "{fish}", t.FullName, html);
        r = Rep(r, "{telefon}", t.Phone, html);
        r = Rep(r, "{oquvchi_telefon}", t.Phone, html);
        r = Rep(r, "{manzil}", t.Address, html);
        r = Rep(r, "{tugilgan}", t.BirthDate, html);
        r = Rep(r, "{guruh}", group?.Name ?? "", html);
        r = Rep(r, "{oqituvchi}", t.FullName, html); // o'qituvchiga yozilgan xabarda — o'zining F.I.Sh
        foreach (var tok in new[]
                 {
                     "{ism}", "{familiya}", "{sharif}", "{sinf}", "{qarzdorlik}", "{balans}",
                     "{ota-ona}", "{ota_ona}", "{ota}", "{ota_telefon}", "{ona}", "{ona_telefon}",
                 })
            r = Rep(r, tok, "", html);
        r = Schedule(r, group, html); // guruh berilsa dars jadvali tokenlari shundan, aks holda bo'sh
        return Common(r, centerName, extra, html);
    }

    // (Lead(...) — lid xabari renderi — 2026-10 da lidga SMS bilan birga olib tashlandi.)
}
