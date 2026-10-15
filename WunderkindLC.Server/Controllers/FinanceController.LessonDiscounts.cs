using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «Chegirma (kelmagan darslar)» — Moliya → «Yangi moliyaviy amal» → «O'quvchi to'lovi» oynasi uchun (2026-10-13). Shu
/// oynada to'lovni `finance.main` bilan kiritadigan xodim chegirmani ham SHU ruxsat bilan qo'llaydi — Kassa
/// (`KassaController`, `kassa`) va o'quvchi profili (`StudentsController`, `students.list`) bilan bir xil naqsh.
/// Mantiq BITTA joyda: <see cref="LessonDiscountHttp"/> → `LessonDiscountService` (`discounts.md` §13).
/// </summary>
public partial class FinanceController
{
    [HttpGet("students/{id}/lesson-discounts")]
    public Task<ActionResult<List<LessonDiscountDto>>> LessonDiscounts(string id) =>
        LessonDiscountHttp.ListAsync(db, id);

    [HttpGet("students/{id}/lesson-discounts/quote")]
    public Task<ActionResult<LessonDiscountQuoteDto>> LessonDiscountQuote(
        string id, [FromQuery] string? groupId, [FromQuery] string? month, [FromQuery] int? attended) =>
        LessonDiscountHttp.QuoteAsync(db, id, groupId, month, attended);

    [HttpPost("students/{id}/lesson-discounts")]
    public Task<ActionResult<LessonDiscountDto>> ApplyLessonDiscount(string id, LessonDiscountRequest req) =>
        LessonDiscountHttp.ApplyAsync(db, audit, User, id, req);

    [HttpPost("students/{id}/lesson-discounts/{discountId}/cancel")]
    public Task<ActionResult<LessonDiscountDto>> CancelLessonDiscount(string id, string discountId) =>
        LessonDiscountHttp.CancelAsync(db, audit, User, id, discountId);
}
