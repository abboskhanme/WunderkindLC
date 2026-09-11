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
    /// "so'm"ni andoza ichida yoziladi (masalan "{summa} so'm") — Eskiz moderatsiyasi uchun ham qulay.</summary>
    public static string MoneyPlain(decimal v) =>
        v.ToString("0", CultureInfo.InvariantCulture);

    private static string Rep(string input, string token, string? value) =>
        Regex.Replace(input, Regex.Escape(token),
            (value ?? "").Replace("$", "$$"), RegexOptions.IgnoreCase);

    /// <summary>Hodisaga xos qo'shimcha tokenlarni matnga BIRINCHI bo'lib qo'llaydi (masalan {summa},
    /// {sabab} — yoki {guruh}/{sana}/{oy} kabi standart tokenlarni USTIDAN yozish uchun). Dispatcher shuni
    /// entity tokenizeridan (Student/Teacher/Lead) OLDIN chaqiradi: shu bilan qo'shimcha qiymat standart
    /// qiymatdan ustun turadi (birinchi almashtirilgan token keyingi passda qayta almashmaydi).</summary>
    public static string ApplyExtra(string text, IReadOnlyDictionary<string, string>? extra)
    {
        if (extra is null) return text;
        var r = text;
        foreach (var (k, v) in extra)
            r = Rep(r, k, v);
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
    /// group=null bo'lsa (o'qituvchi/lid yoki guruh topilmasa) — tokenlar bo'sh qoladi.
    /// </summary>
    private static string Schedule(string text, DomainGroup? g)
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
        return Schedule(text, sana, vaqt, kunlar);
    }

    /// <summary>Dars jadvali tokenlarini aniq qiymatlar bilan almashtiradi (sinov darsi — TrialLesson.ScheduledAt uchun).</summary>
    private static string Schedule(string text, string sana, string vaqt, string kunlar)
    {
        var r = Rep(text, "{dars_sana}", sana);
        r = Rep(r, "{dars_vaqti}", vaqt);
        r = Rep(r, "{dars_kunlari}", kunlar);
        return r;
    }

    /// <summary>Sinov darsi vaqti ("yyyy-MM-ddTHH:mm") → ({dars_sana}="DD.MM.YYYY", {dars_vaqti}="HH:mm").</summary>
    private static (string sana, string vaqt) ParseTrial(string? iso)
    {
        if (iso is not { Length: >= 10 }) return ("", "");
        var sana = $"{iso[8..10]}.{iso[5..7]}.{iso[..4]}";
        var vaqt = iso.Length >= 16 ? iso[11..16] : "";
        return (sana, vaqt);
    }

    /// <summary>Guruh dars kunlari (Days, 0=Du..6=Yak) → "Du, Chor, Jum".</summary>
    private static string FormatDays(List<int> days)
    {
        if (days is null || days.Count == 0) return "";
        return string.Join(", ", days.Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d)
            .Select(d => UzWeekdaysShort[d]));
    }

    /// <summary>Barcha kanal/auditoriyalar uchun umumiy tokenlar: markaz nomi, joriy sana + ad-hoc tokenlar.</summary>
    private static string Common(string text, string? centerName, IReadOnlyDictionary<string, string>? extra)
    {
        var now = AppClock.Now;
        var r = Rep(text, "{markaz}", centerName);
        r = Rep(r, "{sana}", now.ToString("dd.MM.yyyy"));
        r = Rep(r, "{oy}", now.Month is >= 1 and <= 12 ? UzMonths[now.Month - 1] : "");
        r = Rep(r, "{yil}", now.Year.ToString());
        // Hodisaga xos qo'shimcha tokenlar (masalan {summa} — to'lov, {natija} — test).
        if (extra is not null)
            foreach (var (k, v) in extra)
                r = Rep(r, k, v);
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
        IReadOnlyDictionary<string, string>? extra = null, DomainGroup? group = null, string? teacherName = null)
    {
        var debt = s.Balance < 0 ? -s.Balance : 0m;
        var parent = string.IsNullOrWhiteSpace(parentName) ? "Ota-ona" : parentName!;
        var r = text;
        r = Rep(r, "{fish}", s.FullName);
        r = Rep(r, "{ism}", s.FirstName);
        r = Rep(r, "{familiya}", s.LastName);
        r = Rep(r, "{sharif}", s.MiddleName);
        r = Rep(r, "{sinf}", s.ClassName);
        r = Rep(r, "{guruh}", s.ClassName);
        r = Rep(r, "{qarzdorlik}", Money(debt));
        r = Rep(r, "{balans}", Money(s.Balance));
        r = Rep(r, "{ota-ona}", parent);
        r = Rep(r, "{ota_ona}", parent);
        r = Rep(r, "{telefon}", contactPhone);
        r = Rep(r, "{ota}", s.FatherFullName);
        r = Rep(r, "{ota_telefon}", s.FatherPhone);
        r = Rep(r, "{ona}", s.MotherFullName);
        r = Rep(r, "{ona_telefon}", s.MotherPhone);
        r = Rep(r, "{oquvchi_telefon}", s.Phone);
        r = Rep(r, "{manzil}", s.Address);
        r = Rep(r, "{tugilgan}", s.BirthDate);
        r = Rep(r, "{oqituvchi}", teacherName); // guruh o'qituvchisi F.I.Sh (berilmasa bo'sh)
        r = Schedule(r, group); // guruh dars jadvali (sana/vaqt/kunlar)
        return Common(r, centerName, extra);
    }

    /// <summary>O'qituvchi xabari — {fish}/{telefon}/{manzil}/{tugilgan}; o'quvchi tokenlari bo'sh.
    /// <paramref name="group"/> berilsa — {guruh} shu guruh nomidan, dars jadvali tokenlari
    /// ({dars_sana}/{dars_vaqti}/{dars_kunlari}) shu guruhdan to'ladi (masalan davomat eslatmasi uchun).
    /// <paramref name="extra"/> — hodisaga xos qo'shimcha tokenlar (masalan {kurs}).</summary>
    public static string Teacher(string text, Teacher t, string? centerName,
        IReadOnlyDictionary<string, string>? extra = null, DomainGroup? group = null)
    {
        var r = text;
        r = Rep(r, "{fish}", t.FullName);
        r = Rep(r, "{telefon}", t.Phone);
        r = Rep(r, "{oquvchi_telefon}", t.Phone);
        r = Rep(r, "{manzil}", t.Address);
        r = Rep(r, "{tugilgan}", t.BirthDate);
        r = Rep(r, "{guruh}", group?.Name ?? "");
        r = Rep(r, "{oqituvchi}", t.FullName); // o'qituvchiga yozilgan xabarda — o'zining F.I.Sh
        foreach (var tok in new[]
                 {
                     "{ism}", "{familiya}", "{sharif}", "{sinf}", "{qarzdorlik}", "{balans}",
                     "{ota-ona}", "{ota_ona}", "{ota}", "{ota_telefon}", "{ona}", "{ona_telefon}",
                 })
            r = Rep(r, tok, "");
        r = Schedule(r, group); // guruh berilsa dars jadvali tokenlari shundan, aks holda bo'sh
        return Common(r, centerName, extra);
    }

    /// <summary>Lid xabari — lid ma'lumotlari; o'quvchi-spetsifik tokenlar bo'sh.
    /// trialAt berilsa (sinov darsi vaqti "yyyy-MM-ddTHH:mm") — {dars_sana}/{dars_vaqti} to'ladi,
    /// {dars_kunlari} esa group berilса guruh kunlaridan keladi. teacherName — {oqituvchi} (sinov darsi
    /// guruhining o'qituvchisi).</summary>
    public static string Lead(string text, Lead l, string? contactPhone, string? centerName,
        IReadOnlyDictionary<string, string>? extra = null, DomainGroup? group = null, string? trialAt = null,
        string? teacherName = null)
    {
        var r = text;
        r = Rep(r, "{fish}", l.FullName);
        r = Rep(r, "{telefon}", contactPhone);
        r = Rep(r, "{oquvchi_telefon}", l.Phone);
        r = Rep(r, "{ota}", l.FatherFullName);
        r = Rep(r, "{ota_telefon}", l.FatherPhone);
        r = Rep(r, "{ona}", l.MotherFullName);
        r = Rep(r, "{ona_telefon}", l.MotherPhone);
        r = Rep(r, "{fan}", l.InterestSubject);
        r = Rep(r, "{tugilgan}", l.BirthDate);
        r = Rep(r, "{oqituvchi}", teacherName); // sinov darsi guruhining o'qituvchisi (berilmasa bo'sh)
        foreach (var tok in new[]
                 {
                     "{ism}", "{familiya}", "{sharif}", "{sinf}", "{guruh}", "{qarzdorlik}", "{balans}",
                     "{ota-ona}", "{ota_ona}", "{manzil}",
                 })
            r = Rep(r, tok, "");
        // Sinov darsi jadvali: sana/vaqt sinov darsidan (trialAt), kunlar guruhdan.
        // trialAt yo'q bo'lsa — guruh jadvaliga (yoki bo'shga) tushadi.
        var (tsana, tvaqt) = ParseTrial(trialAt);
        r = tsana.Length > 0
            ? Schedule(r, tsana, tvaqt, group is null ? "" : FormatDays(group.Days))
            : Schedule(r, group);
        return Common(r, centerName, extra);
    }
}
