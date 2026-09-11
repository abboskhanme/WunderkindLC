using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// OMMAVIY (autentifikatsiyasiz) LID FORMASI: mijoz ijtimoiy tarmoqdagi havola orqali
/// <c>/forma/{slug}</c> ga kiradi, ariza qoldiradi — CRM'da lid bo'lib tushadi
/// (manba = formaning manbasi, ya'ni qaysi tarmoqdan kelgani ko'rinib turadi).
///
/// <para>Daraja testining ommaviy endpointi (<see cref="PublicTestController"/>) bilan bir xil
/// himoya: anonim, lekin <c>public-lead</c> rate-limit ostida va kirish uzunliklari cheklangan
/// (tekshiruvlar <see cref="LeadFormService.SubmitAsync"/> ichida — bot orqali kelsa ham bir xil).</para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/form")]
public class PublicLeadFormController(
    AppDbContext db, TelegramService telegram, AutoMessageService autoMsg, DataCache dataCache)
    : ControllerBase
{
    /// <summary>Slug bo'yicha faol formani oladi. Topilmasa 404.</summary>
    [HttpGet("{slug}")]
    public async Task<ActionResult<PublicLeadFormDto>> Get(string slug)
    {
        var dto = await LeadFormService.GetPublicAsync(db, slug);
        if (dto is null) return NotFound(new { message = "Forma topilmadi yoki faol emas" });

        // Ochilishlar sanog'i — konversiya foizi ("ochgan / ariza qoldirgan") uchun. Yagona
        // UPDATE bilan oshiriladi: entity yuklab-yozish ikki foydalanuvchi bir vaqtda kirganda
        // sanoqni yo'qotardi.
        await db.LeadForms.Where(f => f.Slug == slug)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Views, f => f.Views + 1));
        // ⚠️ `ExecuteUpdate` SaveChanges'dan o'tmaydi, ya'ni `CacheInvalidationInterceptor` buni
        // SEZMAYDI. Statistika keshi (`leadforms:stats`) `LeadForm` versiyasiga bog'langani uchun
        // versiyani shu yerda O'ZIMIZ oshiramiz — aks holda "Ochilgan" soni TTL tugagunicha
        // qotib qolardi (admin havolani ochib ko'rib, sanoq o'zgarmaganini ko'rardi).
        dataCache.Bump(new[] { nameof(LeadForm) });
        return dto;
    }

    /// <summary>Arizani qabul qiladi — lid yaratiladi yoki telefon bo'yicha mavjudiga biriktiriladi.</summary>
    [HttpPost("{slug}/submit")]
    [EnableRateLimiting("public-lead")]
    public async Task<ActionResult<LeadFormSubmitResultDto>> Submit(string slug, LeadFormSubmitRequest req)
    {
        var (result, error) = await LeadFormService.SubmitAsync(db, slug, req, telegram, autoMsg);
        if (error is not null) return BadRequest(new { message = error });
        if (result is null) return NotFound(new { message = "Forma topilmadi yoki faol emas" });
        return result;
    }
}
