namespace WunderkindLC.Application.Services;

/// <summary>
/// QO'LDA JARIMA (o'qituvchiga) — qoidalar (sof, `TeacherFinesTests`). 2026-10-13, owner: "jarima maoshdan ushlansin".
/// Jarima SHU OY maoshidan ayiriladi (`SalaryLedger`, davomat jarimasi bilan bir xil: maosh manfiy bo'lmaydi) va
/// KASSAGA TEGMAYDI. Ilgari Moliya → «Jarima qo'shish» CHIQIM yozardi: kassadan pul chiqqandek ko'rinar, maoshdan esa
/// hech narsa ushlanmasdi — endi `penalty` toifasidagi YANGI chiqim rad etiladi (eski qatorlar tegilmaydi).
/// </summary>
public static class TeacherFines
{
    public const string Active = "active";
    public const string Waived = "waived";
    public const decimal MaxAmount = 100_000_000m;
    public const int MaxReason = 500;

    /// <summary>Yangi jarima tekshiruvi: oy — "yyyy-MM" va JORIY oydan keyin emas (kelajak maoshidan oldindan ushlanmaydi),
    /// summa 0 dan katta, sabab majburiy. <c>null</c> — to'g'ri.</summary>
    public static string? Validate(string? month, decimal amount, string? reason, DateOnly today)
    {
        if (!PaymentFields.IsMonth(month)) return "Oy noto'g'ri (yyyy-MM)";
        if (string.CompareOrdinal(month, today.ToString("yyyy-MM")) > 0) return "Kelajak oyga jarima qo'yilmaydi";
        if (amount <= 0) return "Jarima summasini kiriting";
        if (amount > MaxAmount) return "Summa juda katta";
        var r = (reason ?? "").Trim();
        if (r.Length == 0) return "Jarima sababini yozing";
        if (r.Length > MaxReason) return $"Sabab {MaxReason} belgidan oshmasin";
        return null;
    }
}
