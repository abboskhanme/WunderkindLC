using System.Globalization;

namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// «FAOL O'QUVCHI» ning YAGONA ta'rifi (sof funksiya, testlangan: <c>KpiActiveStudentRuleTests</c>).
///
/// <para>Bu ta'rif chiquvchi adminning butun bonusi ostida turadi: Bonus A oy oxiridagi faol
/// o'quvchilar soniga ko'paytiriladi, ketish foizi esa oy boshidagi songa bo'linadi.</para>
///
/// <para>⚠️ Ta'rif IKKI joyda ishlatiladi — oy BOSHI snapshoti va oy OXIRI hisobi. Ikkalasi
/// alohida yozilsa, sanoq usuli sal farq qilishi bilanoq "ketish foizi" xayoliy bo'lib qolardi
/// (oy boshi bir qoida bilan, oy oxiri boshqa qoida bilan sanalgani uchun). Shuning uchun
/// nusxa ko'chirilmaydi — HAR IKKI joy shu funksiyani chaqiradi.</para>
///
/// <para>⚠️ SOF: entity qabul qilmaydi, TAYYOR qiymatlar oladi. Sabab — bir marta bazadan
/// o'qib, minglab o'quvchi uchun xotirada hisoblash kerak; entity qabul qilsa, har o'quvchi
/// uchun jurnal va hisoblar alohida so'rov bo'lib ketardi.</para>
/// </summary>
public static class KpiActiveStudentRule
{
    /// <summary>Shuncha kundan beri darsga kelmagan o'quvchi FAOL sanalmaydi.</summary>
    public const int DefaultAbsenceDays = 30;

    /// <summary>To'lov muddati shuncha kundan ortiq o'tib ketgan bo'lsa, o'quvchi FAOL sanalmaydi.</summary>
    public const int DefaultDebtDays = 45;

    /// <summary>
    /// Shu o'quvchi <paramref name="asOf"/> sanasida KPI bo'yicha FAOLmi.
    /// </summary>
    /// <param name="hasActiveMembership">Kamida bitta a'zolik <c>Status == "active" &amp;&amp; IsActive</c>.
    /// ⚠️ <c>trial</c> va <c>frozen</c> — FAOL EMAS: sinovdagi hali pul to'lamaydi, muzlatilgani
    /// esa to'xtatgan. Ikkalasini ham sanasak, bonus A haqiqiy baza uchun emas, ro'yxat uzunligi
    /// uchun to'langan bo'lardi.</param>
    /// <param name="lastPresentDate">Oxirgi <c>Present == true</c> jurnal yozuvining sanasi
    /// ("yyyy-MM-dd"). Bo'sh — hali BIRORTA ham dars bo'lmagan.</param>
    /// <param name="oldestUnpaidDueDate">Eng ESKI to'lanmagan hisobning muddati ("yyyy-MM-dd").
    /// Bo'sh — qarz yo'q.</param>
    /// <param name="isArchived">O'quvchi arxivlanganmi.</param>
    /// <param name="asOf">Qaysi sanaga qarab hisoblanmoqda (oy boshi yoki oy oxiri).</param>
    /// <param name="absenceDays">Kelmaslik chegarasi (kun).</param>
    /// <param name="debtDays">Qarz chegarasi (kun).</param>
    /// <remarks>
    /// ⚠️ <b>"Hali dars bo'lmagan → FAOL".</b> Yangi qo'shilgan o'quvchining jurnalda yozuvi
    /// yo'q. Bo'sh sanani "juda eski" deb hisoblasak, yangi kelgan o'quvchilar oy boshidayoq
    /// bazadan tushib qolar va ketish foizi soxta ravishda ko'tarilardi.
    ///
    /// ⚠️ Chegara QAT'IY (<c>&gt;</c>, <c>&gt;=</c> emas): 30 kun kelmagan — allaqachon
    /// kelmagan. Aks holda "30 kun kelmadi, lekin baribir faol" degan bir kunlik oraliq
    /// qolardi va oy boshi/oxiri sanoqlari bir kunga surilib ketardi.
    ///
    /// ⚠️ Sana BUZUQ bo'lsa (qo'lda kiritilgan "2026-13-99") shart TEKSHIRILMAYDI — o'quvchi
    /// faol qoladi. Sabab: buzuq yozuv tufayli tirik o'quvchini bazadan chiqarib yuborish,
    /// uni qoldirishdan ko'ra battar (pul hisobi shu songa bog'liq).
    /// </remarks>
    public static bool IsActive(bool hasActiveMembership, string? lastPresentDate,
                                string? oldestUnpaidDueDate, bool isArchived, DateOnly asOf,
                                int absenceDays = DefaultAbsenceDays, int debtDays = DefaultDebtDays)
    {
        if (!hasActiveMembership) return false;
        if (isArchived) return false;

        if (TryDate(lastPresentDate, out var last) && last <= asOf.AddDays(-absenceDays)) return false;
        if (TryDate(oldestUnpaidDueDate, out var due) && due <= asOf.AddDays(-debtDays)) return false;

        return true;
    }

    /// <summary>ISO "yyyy-MM-dd" sanani o'qiydi; bo'sh yoki buzuq bo'lsa <c>false</c>.</summary>
    private static bool TryDate(string? iso, out DateOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(iso)) return false;
        var s = iso.Length > 10 ? iso[..10] : iso;   // "…T09:00:00" ham keladi
        return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
