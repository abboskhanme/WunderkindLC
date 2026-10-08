namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH HAYOTI (yopish → arxivdan chiqarish) bo'yicha sof qoidalar — testlangan.
/// </summary>
public static class GroupLifecycleRules
{
    /// <summary>
    /// Guruh ARXIVDAN CHIQARILGANDA tugash sanasi (<c>Group.EndDate</c>) nima bo'ladi.
    ///
    /// <para>«Guruhni yopish» va «Tugatish» bo'sh <c>EndDate</c> ni yopish sanasi bilan to'ldiradi.
    /// Guruh qayta ochilsa ham u qolib ketardi: jadval, o'qituvchi ilovasi, «Davomat olinmagan»
    /// monitori va maosh (<see cref="SalaryLedger.LessonEnd"/>) undan keyingi darslarni
    /// "yo'q" deb hisoblar, o'quvchilarga esa oylik yozilaverardi.</para>
    ///
    /// <para>Qoida: arxivlangan kundan KECH bo'lmagan tugash sanasi (yopish qo'ygan yoki allaqachon
    /// o'tib ketgan) TOZALANADI — qayta ochilgan guruh davom etadi. Arxivlangan kundan KEYINGI
    /// (rejalashtirilgan) sana tegilmaydi. Arxiv sanasi noma'lum bo'lsa — tegilmaydi.</para>
    /// </summary>
    /// <returns>Yangi <c>EndDate</c> (<c>null</c> — tugash sanasi yo'q).</returns>
    public static string? EndDateOnUnarchive(string? endDate, string? archivedAt)
    {
        if (string.IsNullOrWhiteSpace(endDate)) return null;
        if (archivedAt is not { Length: >= 10 } || endDate.Length < 10) return endDate;
        return string.CompareOrdinal(endDate[..10], archivedAt[..10]) <= 0 ? null : endDate;
    }
}
