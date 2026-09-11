using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// Qoidalar va OKLADNING versiyalanishi: "shu OY uchun qaysi versiya amal qiladi"
/// (sof funksiyalar).
///
/// <para>⚠️ Qoida ham, oklad ham USTIDAN YOZILMAYDI — har o'zgarish yangi qator
/// (<c>EffectiveFrom</c>). Sabab: yopilgan oyning summasi qayta ko'rilganda o'sha paytdagi
/// qoidalar bilan chiqishi SHART. Ustidan yozilsa, mart oyida koeffitsient o'zgartirilishi
/// bilan yanvarning tasdiqlangan raqami ham "boshqacha" bo'lib qolardi — ya'ni xodim bilan
/// kelishilgan hisobni keyinchalik isbotlab bo'lmasdi.</para>
///
/// <para>⚠️ O'zgarish KEYINGI oydan kuchga kiradi degan qoida bu yerda EMAS, chaqiruvchida:
/// funksiya faqat "berilgan oyga qaysi versiya tegishli" degan savolga javob beradi.</para>
/// </summary>
public static class KpiVersioning
{
    /// <summary>
    /// <paramref name="month"/> ("yyyy-MM") uchun amal qiladigan qoidalar to'plami:
    /// <c>EffectiveFrom &lt;= month</c> bo'lgan ENG SO'NGGI versiya.
    /// </summary>
    /// <remarks>⚠️ Versiya topilmasa <c>null</c> — hisob JIMGINA standart qiymatlar bilan
    /// ishlamaydi. Klientga <c>RulesMissing</c> bayrog'i qaytadi va foydalanuvchi "qoidalar
    /// seed qilinmagan" deb ko'radi; aks holda hech kim kiritmagan koeffitsientlar bilan
    /// hisoblangan oylik haqiqiy bo'lib ko'rinardi.</remarks>
    public static KpiRuleSet? RuleFor(IEnumerable<KpiRuleSet> all, string roleCode, string month)
    {
        var m = Month(month);
        return all
            .Where(x => x.RoleCode == roleCode && string.CompareOrdinal(Month(x.EffectiveFrom), m) <= 0)
            .OrderByDescending(x => Month(x.EffectiveFrom), StringComparer.Ordinal)
            .ThenByDescending(x => x.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Shu xodimning shu oydagi OKLADI — <c>EffectiveFrom &lt;= month</c> bo'lgan eng so'nggisi.</summary>
    public static KpiProfileSalary? SalaryFor(IEnumerable<KpiProfileSalary> all, string userId, string month)
    {
        var m = Month(month);
        return all
            .Where(x => x.UserId == userId && string.CompareOrdinal(Month(x.EffectiveFrom), m) <= 0)
            .OrderByDescending(x => Month(x.EffectiveFrom), StringComparer.Ordinal)
            .ThenByDescending(x => x.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Sanani OY darajasiga qisqartiradi ("2026-03-15" → "2026-03").
    /// </summary>
    /// <remarks>⚠️ <c>EffectiveFrom</c> ba'zan to'liq sana ("yyyy-MM-dd") bo'lib kiritiladi.
    /// Qisqartirmasdan solishtirsak, "2026-03-15" &gt; "2026-03" chiqib, mart oyida
    /// kiritilgan versiya AYNAN mart uchun ishlamasdi — ya'ni o'zgarish "sabab yo'q joyda"
    /// bir oyga kechikardi.</remarks>
    private static string Month(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length >= 7 ? value[..7] : value;
    }
}
