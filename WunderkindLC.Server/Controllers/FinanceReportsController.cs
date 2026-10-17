using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MOLIYA HISOBOTLARI (egasining TZ'si, 2026-10-17, <c>docs/SPEC-backlog-2026-10-17.md</c> F1):
/// «Qarzdorlik tahlili», «Guruhlar va tariflar», «Hisob-fakturalar». Faqat O'QIYDI.
///
/// <para>RUXSAT — har sahifaning O'Z kaliti (<c>permissions.md</c> §10) va <c>ReadRequiresPerm = true</c>:
/// javobda pul summalari va ota-ona telefonlari bor, GET'ni odatdagidek har qanday xodimga ochib bo'lmaydi.
/// Sinf darajasidagi atribut — xavfsiz standart (yangi metod o'z darvozasisiz qolsa ham yopiq bo'lsin).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.debts", ReadRequiresPerm = true)]
[Route("api/admin/reports")]
public class FinanceReportsController(AppDbContext db) : ControllerBase
{
    /// <summary>Qarzdorlik: oylar × (o'quvchi, guruh) matritsasi, qarz yoshi, jamlanma.</summary>
    /// <param name="minAge">Faqat qarz yoshi shu kundan katta qatorlar (0 — hammasi; 7 / 30 / 60).</param>
    [HttpGet("debts")]
    [AdminPerm("finance.debts", ReadRequiresPerm = true)]
    public async Task<ActionResult<DebtReportDto>> Debts(
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] bool includeArchived = false,
        [FromQuery] string? groupId = null, [FromQuery] string? teacherId = null,
        [FromQuery] int minAge = 0, CancellationToken ct = default) =>
        await DebtReport.BuildAsync(db,
            new DebtReport.Query(from, to, includeArchived, groupId, teacherId, Math.Max(0, minAge)),
            AppClock.Today, ct);

    /// <summary>Guruhlar va tariflar: bitta oy, guruh va o'qituvchi kesimida.</summary>
    [HttpGet("tariffs")]
    [AdminPerm("finance.tariffs", ReadRequiresPerm = true)]
    public async Task<ActionResult<TariffReportDto>> Tariffs(
        [FromQuery] string? month = null, [FromQuery] string? teacherId = null, CancellationToken ct = default) =>
        await TariffReport.BuildAsync(db, month, teacherId, AppClock.Today, ct);

    /// <summary>Hisob-fakturalar ro'yxati (server tomonida filtr va sahifalash).</summary>
    /// <param name="status"><c>paid</c> · <c>partial</c> · <c>unpaid</c> (bo'sh — hammasi).</param>
    [HttpGet("invoices")]
    [AdminPerm("finance.invoices", ReadRequiresPerm = true)]
    public async Task<ActionResult<InvoiceListDto>> Invoices(
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] string? status = null, [FromQuery] bool overdue = false,
        [FromQuery] string? q = null, [FromQuery] string? groupId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = InvoiceReport.DefaultPageSize,
        CancellationToken ct = default) =>
        await InvoiceReport.BuildAsync(db,
            new InvoiceReport.Query(from, to, status, overdue, q, groupId, page, pageSize),
            AppClock.Today, ct);
}
