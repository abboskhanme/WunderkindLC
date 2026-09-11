using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Markaz (butun o'quv markazi) kunlik AI tahlili — bosh sahifadagi "AI Tahlil" bo'limi.
/// Tahlil har kuni ertalab fon xizmati (<see cref="CenterAiSchedulerService"/>) orqali avtomatik
/// yaratiladi; qo'lda ham yangilash mumkin (POST run). Kuniga bir marta (force=true superadmin).
///
/// KIRISH: DEFAULT faqat SUPERADMIN. Xodim (staff) — faqat "Xodimlar va rollar"da "ai" bo'limi
/// berilgan bo'lsa (GET → ko'rish, POST run → "ai" to'liq yoki "ai:create"). Oddiy admin roli
/// KO'RMAYDI (foydalanuvchi talabi). AdminPermAttribute ishlatilmaydi — u staff uchun GET'ni
/// har doim ochiq qoldiradi, bu bo'lim esa GET'da ham yopiq bo'lishi kerak.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/ai-analysis")]
public class AiAnalysisController(AppDbContext db, IConfiguration config) : ControllerBase
{
    /// <summary>Superadmin — har doim; xodim — "ai" ruxsati bo'yicha; boshqalar (jumladan admin) — yo'q.
    /// <paramref name="action"/>: "view" (biror ai:* tokeni ham yetadi) yoki "create".</summary>
    private bool HasAiAccess(string action)
    {
        if (User.IsInRole(Domain.Roles.SuperAdmin)) return true;
        if (!User.IsInRole(Domain.Roles.Staff)) return false;
        var perms = User.Claims.Where(c => c.Type == AdminPermAttribute.ClaimType).Select(c => c.Value).ToList();
        if (perms.Contains("ai") || perms.Contains("ai:" + action)) return true;
        // Ko'rish: bo'limda istalgan amal ruxsati bo'lsa ham ochiq (frontend `can` bilan izchil).
        return action == "view" && perms.Any(p => p.StartsWith("ai:"));
    }

    /// <summary>Bugungi (yoki eng so'nggi) markaz AI tahlili. Yo'q bo'lsa null.</summary>
    [HttpGet("center")]
    public async Task<ActionResult<CenterAiRecordDto?>> GetCenter(CancellationToken ct)
    {
        if (!HasAiAccess("view")) return Forbid();
        return await CenterAiAnalysisService.GetLatestAsync(db, ct);
    }

    /// <summary>Markaz AI tahlillari tarixi (eng yangisi birinchi).</summary>
    [HttpGet("center/history")]
    public async Task<ActionResult<IEnumerable<CenterAiHistoryItemDto>>> History(CancellationToken ct)
    {
        if (!HasAiAccess("view")) return Forbid();
        return await CenterAiAnalysisService.HistoryAsync(db, ct);
    }

    /// <summary>Qo'lda markaz AI tahlilini yaratish. Bugun allaqachon qilingan bo'lsa mavjudi qaytadi
    /// (AlreadyToday=true). <paramref name="force"/>=true (faqat superadmin) bo'lsa qayta yaratiladi.</summary>
    [HttpPost("center/run")]
    public async Task<ActionResult<CenterAiResponseDto>> Run([FromQuery] bool force = false, CancellationToken ct = default)
    {
        if (!HasAiAccess("create")) return Forbid();
        var canForce = force && User.IsInRole(Domain.Roles.SuperAdmin);
        return await CenterAiAnalysisService.GenerateAsync(db, config, canForce, ct);
    }
}
