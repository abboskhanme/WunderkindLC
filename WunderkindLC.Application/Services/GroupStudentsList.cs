namespace WunderkindLC.Application.Services;

/// <summary>
/// "Guruh → Guruh o'quvchilari" ro'yxati (edutizimdagi <c>/group/group-students</c>): BARCHA
/// guruhlardagi JORIY a'zoliklar — bitta o'quvchi ikki guruhda bo'lsa ikki qator (har a'zolik
/// alohida, chunki savol "kim qaysi guruhda").
///
/// <para>Filtr va sahifalash shu SOF funksiyada (<see cref="Apply"/>) — controller faqat qatorlarni
/// bazadan yig'adi. Testlar: <c>GroupStudentsListTests</c>.</para>
///
/// <para>⚠️ Faqat JORIY (<c>IsActive</c>) a'zoliklar: guruhdan chiqqanlar (<c>completed</c>, chiqarilgan)
/// bu ro'yxatga KIRMAYDI — ularning tarixi guruh sahifasidagi "Arxiv o'quvchilar" da.</para>
///
/// <para>⚠️ "Muzlatilgan" kalitini <c>Status</c> bo'yicha (joriy holat) o'qiymiz — bu tarixiy
/// hisobot emas, "hozir kim qayerda" ro'yxati (<c>course-analytics.md</c> §2 dagi sana
/// qoidasi bu yerda kerak emas). «Aktiv muzlatish» ham oddiy muzlatilgan (<c>year-freeze.md</c> §1).</para>
/// </summary>
public static class GroupStudentsList
{
    public const int DefaultPageSize = 50;
    /// <summary>Bir so'rovda ko'pi bilan — ro'yxat 2–3 ming qator bo'lishi mumkin.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Bitta a'zolik qatori (ro'yxatning o'zi ham, javob elementi ham).</summary>
    public sealed record Row(
        string MembershipId,
        string StudentId,
        string FullName,
        string GroupId,
        string GroupName,
        bool GroupArchived,
        string TeacherId,
        string TeacherName,
        string Status,
        bool YearFreeze,
        string JoinedAt,
        bool IsActive);

    /// <summary>Filtrlar. Bo'sh qiymat = filtr qo'llanmaydi.</summary>
    /// <param name="Frozen">true — FAQAT muzlatilganlar; false — muzlatilganlarsiz (aktiv + sinov).</param>
    /// <param name="GroupState">"active" — arxivlanmagan guruhlar, "archived" — arxivdagilar, bo'sh — hammasi.</param>
    /// <param name="From">Qo'shilgan sana (JoinedAt) — shu kundan (shu kun ham), "yyyy-MM-dd".</param>
    /// <param name="To">Qo'shilgan sana — shu kungacha (shu kun ham).</param>
    public sealed record Query(
        bool Frozen = false,
        string? TeacherId = null,
        string? GroupState = null,
        string? From = null,
        string? To = null,
        string? Search = null,
        int Page = 1,
        int PageSize = DefaultPageSize);

    public sealed record Result(int Total, int Page, int PageSize, List<Row> Items);

    /// <summary>Qator filtrlarga mos keladimi.</summary>
    public static bool Matches(Row r, Query q)
    {
        if (!r.IsActive) return false;

        var frozen = string.Equals(r.Status, "frozen", StringComparison.Ordinal);
        if (q.Frozen != frozen) return false;

        if (!string.IsNullOrWhiteSpace(q.TeacherId) && !string.Equals(r.TeacherId, q.TeacherId, StringComparison.Ordinal))
            return false;

        switch (q.GroupState)
        {
            case "active" when r.GroupArchived: return false;
            case "archived" when !r.GroupArchived: return false;
        }

        // Sana ISO ("yyyy-MM-dd..."): ordinal solishtirish kun tartibi bilan bir xil.
        var joined = DayOf(r.JoinedAt);
        var from = DayOf(q.From);
        var to = DayOf(q.To);
        if (from.Length > 0 && (joined.Length == 0 || string.CompareOrdinal(joined, from) < 0)) return false;
        if (to.Length > 0 && (joined.Length == 0 || string.CompareOrdinal(joined, to) > 0)) return false;

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLowerInvariant();
            if (!r.FullName.ToLowerInvariant().Contains(s)
                && !r.GroupName.ToLowerInvariant().Contains(s)
                && !r.StudentId.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Filtr → saralash (eng yangi qo'shilgan tepada, keyin ism) → sahifa. Sahifa raqami va hajmi
    /// chegaralanadi: noto'g'ri qiymat (0, manfiy, juda katta) so'rovni buzmaydi.
    /// </summary>
    public static Result Apply(IEnumerable<Row> rows, Query q)
    {
        var filtered = rows.Where(r => Matches(r, q))
            .OrderByDescending(r => DayOf(r.JoinedAt), StringComparer.Ordinal)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var size = Math.Clamp(q.PageSize, 1, MaxPageSize);
        var pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)size));
        var page = Math.Clamp(q.Page, 1, pages);
        var items = filtered.Skip((page - 1) * size).Take(size).ToList();
        return new Result(filtered.Count, page, size, items);
    }

    /// <summary>ISO sana/vaqtning KUN qismi ("yyyy-MM-dd"); bo'sh/buzuq — "".</summary>
    private static string DayOf(string? iso) =>
        !string.IsNullOrWhiteSpace(iso) && iso.Length >= 10 ? iso[..10] : "";
}
