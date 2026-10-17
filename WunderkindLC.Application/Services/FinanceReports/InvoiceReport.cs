using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «HISOB-FAKTURALAR» (TZ 1.3) — markaz bo'yicha barcha oylik hisoblar ro'yxati: sana, o'quvchi, toifa (kurs),
/// summa, chegirma, to'langan, qoldiq, holat va muddati o'tganlar filtri.
///
/// <para>Faktura = mavjud <see cref="MonthlyCharge"/> qatori (yangi jadval yo'q, <c>docs/ASSUMPTIONS.md</c>).
/// To'lov qaysi kuni kiritilganidan qat'i nazar O'SHA OYNING fakturasiga tushadi — to'lovning
/// <c>Month</c> maydoni bo'yicha (kassa sanasi <c>Date</c> boshqa narsa, <c>billing.md</c>).</para>
/// </summary>
public static class InvoiceReport
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public sealed record Query(
        string? From = null, string? To = null, string? Status = null, bool Overdue = false,
        string? Search = null, string? GroupId = null, int Page = 1, int PageSize = DefaultPageSize);

    public static async Task<InvoiceListDto> BuildAsync(
        IAppDbContext db, Query q, DateOnly today, CancellationToken ct = default)
    {
        var current = today.ToString("yyyy-MM");
        var (from, to) = DebtLedger.ClampRange(q.From, q.To, current, current);
        var dueDay = await DebtLedger.DueDayAsync(db, ct);
        var cells = (await DebtLedger.LoadAsync(db, from, to, ct)).Where(c => c.HasCharge).ToList();
        var ctx = await DebtReport.NamesAsync(db, cells.Select(c => c.StudentId).Distinct().ToList(), ct);

        var all = cells.Select(c =>
        {
            ctx.Students.TryGetValue(c.StudentId, out var st);
            ctx.Groups.TryGetValue(c.GroupId, out var gi);
            var due = DebtLedger.DueDate(c.Month, dueDay);
            var age = DebtLedger.AgeDays(c.Month, dueDay, today);
            var status = c.Status;
            return new InvoiceRowDto(
                c.ChargeId ?? "",
                c.ChargeDate.Length >= 10 ? c.ChargeDate[..10] : c.ChargeDate,
                c.Month,
                due == DateOnly.MaxValue ? "" : due.ToString("yyyy-MM-dd"),
                c.StudentId, st?.Name ?? "—",
                c.GroupId, gi?.Name ?? (c.GroupId == DebtLedger.NoGroup ? "Guruhsiz" : "—"), gi?.Course ?? "",
                c.Gross, c.Discount, c.Net, c.Applied, c.Remaining,
                status, status != "paid" && age > 0 ? age : 0,
                st?.IsArchived ?? false);
        });

        if (!string.IsNullOrEmpty(q.GroupId)) all = all.Where(r => r.GroupId == q.GroupId);
        if (q.Status is "paid" or "partial" or "unpaid") all = all.Where(r => r.Status == q.Status);
        if (q.Overdue) all = all.Where(r => r.OverdueDays > 0);
        var search = (q.Search ?? "").Trim();
        if (search.Length > 0)
            all = all.Where(r => r.StudentName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                                 || r.GroupName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                                 || r.CourseName.Contains(search, StringComparison.CurrentCultureIgnoreCase));

        var filtered = all
            .OrderByDescending(r => r.Month, StringComparer.Ordinal)
            .ThenByDescending(r => r.OverdueDays)
            .ThenBy(r => r.StudentName, StringComparer.CurrentCulture)
            .ToList();

        var filteredKeys = filtered.Select(r => (r.StudentId, r.GroupId, r.Month)).ToHashSet();
        var totals = DebtLedger.Totals(cells.Where(c => filteredKeys.Contains((c.StudentId, c.GroupId, c.Month))));

        var size = Math.Clamp(q.PageSize <= 0 ? DefaultPageSize : q.PageSize, 1, MaxPageSize);
        var pages = Math.Max(1, (filtered.Count + size - 1) / size);
        var page = Math.Clamp(q.Page <= 0 ? 1 : q.Page, 1, pages);

        return new InvoiceListDto(
            from, to, dueDay, filtered.Count, page, size, totals,
            filtered.Count(r => r.Status == "paid"),
            filtered.Count(r => r.Status == "partial"),
            filtered.Count(r => r.Status == "unpaid"),
            filtered.Count(r => r.OverdueDays > 0),
            filtered.Skip((page - 1) * size).Take(size).ToList());
    }
}
