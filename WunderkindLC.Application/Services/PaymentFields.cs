using System.Globalization;

namespace WunderkindLC.Application.Services;

/// <summary>
/// To'lovning qo'shimcha maydonlari — QOG'OZ KVITANSIYA raqami (naqd to'lov) va TO'LOV VAQTI (karta) —
/// uchun yagona normalizatsiya. O'quvchi to'lovi (<c>StudentsController.AddPayment</c>) va moliya yozuvi
/// (<c>FinanceController</c>) bir xil formatda saqlashi shart: aks holda moliyadagi qidiruv
/// ("KV123" ↔ "kv-123") topmay qolardi.
/// </summary>
public static class PaymentFields
{
    /// <summary>Kvitansiya SERIYASI — qog'oz blankada bosilgan ("KV").</summary>
    public const string ReceiptSeries = "KV";

    /// <summary>
    /// Kvitansiya raqamini normallashtiradi: probel/defis olib tashlanadi, katta harfga o'tkaziladi,
    /// "KV" seriyasi bilan boshlanishi ta'minlanadi ("123" → "KV123", "kv-123" → "KV123").
    /// Bo'sh yoki faqat seriya (raqamsiz) bo'lsa — null.
    /// </summary>
    public static string? NormalizeReceiptNo(string? raw)
    {
        var v = (raw ?? "").Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (v.Length == 0) return null;
        if (!v.StartsWith(ReceiptSeries, StringComparison.Ordinal)) v = ReceiptSeries + v;
        return v.Length > ReceiptSeries.Length ? v : null;
    }

    /// <summary>
    /// KARTA raqamining oxirgi 4 raqamini ajratadi ("8600 **** 1234" → "1234").
    /// Bo'sh bo'lsa — true + null (ixtiyoriy). Raqamlari 4 tadan KAM bo'lsa — false (chaqiruvchi
    /// 400 qaytaradi: kassir yarim raqam kiritib qo'ymasin).
    ///
    /// XAVFSIZLIK: to'liq karta raqami HECH QACHON saqlanmaydi — bu yerda faqat oxirgi 4 raqam
    /// olinadi, qolgani tashlanadi (kassir butun raqamni yopishtirsa ham).
    /// </summary>
    public static bool TryNormalizeCardLast4(string? raw, out string? last4)
    {
        last4 = null;
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return true;
        if (digits.Length < 4) return false;
        last4 = digits[^4..];
        return true;
    }

    /// <summary>
    /// To'lov vaqtini ("HH:mm") tekshiradi va normallashtiradi. Bo'sh bo'lsa — true + null
    /// (vaqt ixtiyoriy). Format noto'g'ri bo'lsa — false (chaqiruvchi 400 qaytaradi).
    /// </summary>
    public static bool TryNormalizeTime(string? raw, out string? time)
    {
        time = null;
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return true;
        if (!TimeOnly.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return false;
        time = t.ToString("HH:mm", CultureInfo.InvariantCulture);
        return true;
    }
}
