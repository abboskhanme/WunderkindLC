using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// RAHBARIYAT ANALITIKASI (SPEC-backlog-2026-10-17 F5 D1, TZ 3.4) — `/admin/analitika`, yuqori paneldagi «Analitika».
/// ⚠️ FAQAT SUPERADMIN (HARD rol — markazning butun pul manzarasi: tushum, kassa qoldiqlari, qarz). Ruxsat kaliti YO'Q
/// (`AdminPerm` admin'ni ham o'tkazardi). Hisob — <see cref="ExecutiveDashboard"/> (faqat o'qiydi), 5 daqiqa kesh.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.SuperAdmin)]
[Route("api/admin/analytics")]
public class ExecutiveAnalyticsController(DataCache cache) : ControllerBase
{
    private static readonly string[] Deps =
    [
        nameof(FinanceTransaction), nameof(Cashbox), nameof(CashboxTransfer), nameof(Student), nameof(StudentGroup),
        nameof(Group), nameof(Lead),
    ];

    [HttpGet("executive")]
    public async Task<ActionResult<ExecutiveDashboardDto>> Executive([FromQuery] string? month)
    {
        var cur = AppClock.Today.ToString("yyyy-MM");
        var m = PaymentFields.IsMonth(month) && string.CompareOrdinal(month, cur) <= 0 ? month! : cur;
        return await cache.GetOrCreateAsync($"analytics:executive:{m}", Deps, TimeSpan.FromMinutes(5),
            db => ExecutiveDashboard.BuildAsync(db, m));
    }
}
