using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>Markaz nomi — brending uchun (barcha kirgan foydalanuvchilarga ochiq).
///
/// <para><b>KESH:</b> Sidebar buni HAR sahifa yuklanishida chaqiradi, javob esa faqat
/// <see cref="CenterMeta"/> (bitta qator) dan tuziladi. Endpoint autentifikatsiyalangan —
/// OutputCache mos emas (default policy Authorization sarlavhali so'rovni keshlamaydi),
/// shuning uchun <see cref="DataCache"/>: admin nom/logotipni o'zgartirsa interceptor
/// CenterMeta versiyasini oshiradi va kesh DARHOL yangilanadi; TTL faqat zaxira.</para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/school")]
public class CenterController(DataCache dataCache) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SchoolNameDto>> Get() =>
        await dataCache.GetOrCreateAsync(
            "school:brand", [nameof(CenterMeta)], TimeSpan.FromMinutes(10), async db =>
            {
                var m = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync();
                return new SchoolNameDto(m?.Name ?? "", "", m?.LogoUrl ?? "");
            });
}
