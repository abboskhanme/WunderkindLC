using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using System.Text.Json;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// AI tekshiruv (Speaking/Writing) — ADMIN boshqaruvi (Ilova → AI check).
/// Kim necha marta foydalanayotgani, kunlik limit/premium/blok, va o'quvchi tarixini ko'rish.
/// </summary>
[ApiController]
[Authorize]
// O'QISH ham darvozalanadi (ReadRequiresPerm): `GET item/{id}` javobida o'quvchining `AudioUrl` —
// `/uploads/...` OVOZ YOZUVI manzili qaytadi (ustiga yozgan matni va tahlili). `/uploads`
// autentifikatsiyasiz berilgani uchun manzilni olgan xodim yozuvni abadiy tinglay oladi. Shu sabab
// GET uchun ham `app` bo'limi ruxsati (biror amali) talab qilinadi — bu "Ilova" navigatsiyasidagi
// talab bilan bir xil (nav: perm 'app').
[AdminPerm("app.aiCheck", ReadRequiresPerm = true)]
[Route("api/admin/ai-check")]
public class AiCheckController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// BARCHA (arxivlanmagan) o'quvchilar ro'yxati — FISH + guruh + kim necha marta (speaking/writing)
    /// ishlatgani, bugungi foydalanish va limit/premium/blok holati. Foydalanmaganlar ham chiqadi
    /// (admin ularga ham limit/premium/blok belgilashi uchun). Qidiruv frontendda (FISH bo'yicha).
    /// </summary>
    [HttpGet("overview")]
    public async Task<ActionResult<IEnumerable<AiCheckOverviewRowDto>>> Overview()
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        var defaultLimit = meta?.AiCheckDailyLimit > 0 ? meta.AiCheckDailyLimit : 3;
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync();
        var checksByStudent = (await db.AiChecks.ToListAsync())
            .GroupBy(c => c.StudentId).ToDictionary(g => g.Key, g => g.ToList());
        var access = (await db.StudentAiAccesses.ToListAsync()).ToDictionary(a => a.StudentId);

        var rows = students.Select(st =>
        {
            checksByStudent.TryGetValue(st.Id, out var checks);
            access.TryGetValue(st.Id, out var ac);
            var speaking = checks?.Count(c => c.Type == "speaking") ?? 0;
            var writing = checks?.Count(c => c.Type == "writing") ?? 0;
            var total = checks?.Count ?? 0;
            var todayUsed = checks?.Count(c => c.Date == today) ?? 0;
            var limit = ac is { DailyLimit: > 0 } ? ac.DailyLimit : defaultLimit;
            return new AiCheckOverviewRowDto(
                st.Id, st.FullName, st.ClassName, speaking, writing, total, todayUsed, limit,
                ac?.IsPremium ?? false, ac?.IsBlocked ?? false);
        });

        // Faollar (ko'p ishlatganlar) yuqorida, qolganlar alifbo bo'yicha.
        return rows.OrderByDescending(r => r.Total).ThenBy(r => r.FullName).ToList();
    }

    /// <summary>Bo'lim ilovada yopiq bo'lganda o'quvchiga ko'rsatiladigan YAGONA xabar matni.
    /// Boshqa joyda xom satr yozilmasin — shu konstantadan foydalaning.</summary>
    public const string DisabledMessage = "AI tekshiruv bo'limi hali markaz tomonidan ochilmagan";

    /// <summary>Global standart kunlik limit + bo'lim ilovada ochilganmi.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<AiCheckSettingsDto>> GetSettings()
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        return new AiCheckSettingsDto(
            meta?.AiCheckDailyLimit > 0 ? meta.AiCheckDailyLimit : 3,
            meta?.AiCheckEnabled ?? false);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(SaveAiCheckSettingsRequest req)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (meta is null) { meta = new CenterMeta(); db.CenterMeta.Add(meta); }
        meta.AiCheckDailyLimit = Math.Clamp(req.DailyLimit, 0, 1000);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bo'limni o'quvchi ilovasida OCHISH/YOPISH. ATAYLAB alohida endpoint —
    /// `PUT settings` (kunlik limit) bayroqqa TEGMAYDI, aks holda eski klient limitni
    /// saqlaganda bayroqni beixtiyor almashtirib yuborardi.</summary>
    [HttpPut("enabled")]
    public async Task<IActionResult> SetEnabled(SetAiCheckEnabledRequest req)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (meta is null) { meta = new CenterMeta(); db.CenterMeta.Add(meta); }
        meta.AiCheckEnabled = req.Enabled;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>O'quvchiga limit/premium/blok belgilash (upsert).</summary>
    [HttpPut("access/{studentId}")]
    public async Task<IActionResult> SaveAccess(string studentId, SaveAiAccessRequest req)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId)) return NotFound();
        var access = await db.StudentAiAccesses.FirstOrDefaultAsync(a => a.StudentId == studentId);
        if (access is null)
        {
            access = new StudentAiAccess { StudentId = studentId };
            db.StudentAiAccesses.Add(access);
        }
        access.DailyLimit = Math.Clamp(req.DailyLimit, 0, 1000);
        access.IsPremium = req.IsPremium;
        access.IsBlocked = req.IsBlocked;
        access.UpdatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>O'quvchining AI tekshiruv tarixi (admin ko'rinishi — o'quvchidagidek).</summary>
    [HttpGet("history/{studentId}")]
    public async Task<ActionResult<IEnumerable<AiCheckListItemDto>>> History(string studentId) =>
        await db.AiChecks.Where(a => a.StudentId == studentId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AiCheckListItemDto(a.Id, a.Type, a.Prompt, a.Score, a.Date, a.CreatedAt, a.AudioUrl != ""))
            .ToListAsync();

    /// <summary>Bitta yozuv (to'liq — matn/ovoz/tahlil).</summary>
    [HttpGet("item/{id}")]
    public async Task<ActionResult<AiCheckDto>> Item(string id)
    {
        var a = await db.AiChecks.FindAsync(id);
        return a is null ? NotFound() : ToDto(a);
    }

    private static AiCheckDto ToDto(AiCheck a)
    {
        var analysis = AiCheckService.ParseStored(a.AnalysisJson);
        AiCheckSpeechDto? speech = null;
        if (a.Type == "speaking" && !string.IsNullOrWhiteSpace(a.AzureJson))
        {
            try
            {
                var r = JsonSerializer.Deserialize<SpeakingResultDto>(a.AzureJson);
                if (r is not null)
                    speech = new AiCheckSpeechDto(r.RecognizedText, r.PronScore, r.Accuracy,
                        r.Fluency, r.Completeness, r.Prosody, r.Words ?? new());
            }
            catch { /* buzuq json — speech null */ }
        }
        return new AiCheckDto(a.Id, a.Type, a.Prompt, a.InputText, a.RecognizedText, a.AudioUrl,
            a.Score, a.Date, a.CreatedAt, analysis, speech, a.TaskType);
    }
}
