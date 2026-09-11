using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'ZGARISHLAR TARIXI (audit) — kim, qachon, nimani o'zgartirgani.
///
/// <para><b>RUXSAT.</b> Ilgari bu yerda yalang <c>[Authorize(Roles="admin,superadmin")]</c> turardi,
/// ya'ni XODIM (staff) tarixni umuman ko'ra olmasdi — o'quvchi/guruh sahifasidagi "Tarix" bo'limi
/// unga bo'sh yoki xato bo'lib chiqardi. Endi alohida <c>audit</c> ruxsati bor: admin/superadmin
/// odatdagidek cheklovsiz, xodimga esa "Xodimlar va rollar" dan beriladi.</para>
///
/// <para>⚠️ <c>ReadRequiresPerm = true</c> ATAYIN: <see cref="AdminPermAttribute"/> da GET odatda
/// har qanday xodimga ochiq (bo'limlararo o'qish buzilmasin). Bu yerda bu XAVFLI bo'lardi — audit
/// javobida to'lov summalari, maosh, chegirma va ruxsat o'zgarishlari bor. Shuning uchun o'qish
/// ham <c>audit</c> ruxsatini talab qiladi.</para>
///
/// <para>⚠️ <c>audit</c> = BUTUN tarix (moliya va maosh ham). Bo'limlarga bo'lib berish (masalan
/// "faqat o'z bo'limlaringni ko'r") ATAYIN qilinmagan — bitta tushunarli kalit; kimga berilayotgani
/// shu sababdan o'ylab tanlanishi kerak.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("audit", ReadRequiresPerm = true)]
[Route("api/admin/audit")]
public class AuditController(AppDbContext db) : ControllerBase
{
    /// <summary>Bir so'rovda qaytadigan eng ko'p yozuv (chegara bo'lmasa butun jadval kelardi).</summary>
    private const int MaxLimit = 500;

    /// <summary>
    /// O'zgarishlar tarixi. Filtrlar: <paramref name="section"/> (bo'lim), entityType+entityId (bitta
    /// yozuv), studentId, teacherId, groupId, action, actor (xodim nomi), q (izoh bo'yicha qidiruv),
    /// davr (from/to). Hammasi ixtiyoriy — vaqt bo'yicha kamayish tartibida.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> Get(
        [FromQuery] string? entityType, [FromQuery] string? entityId,
        [FromQuery] string? studentId, [FromQuery] string? teacherId, [FromQuery] string? groupId,
        [FromQuery] string? action, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? section, [FromQuery] string? actor, [FromQuery] string? q,
        [FromQuery] int? limit, [FromQuery] int? offset)
    {
        var query = ApplyFilters(
            db.AuditLogs.AsNoTracking(),
            entityType, entityId, studentId, teacherId, groupId, action, from, to, section, actor, q);

        query = query.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id);

        if (offset is > 0) query = query.Skip(offset.Value);
        query = query.Take(limit is > 0 ? Math.Min(limit.Value, MaxLimit) : MaxLimit);

        var list = await query.ToListAsync();
        return list.Select(a => new AuditLogDto(
            a.Id, a.EntityType, a.EntityId, a.Action, a.Timestamp,
            a.ActorName, a.Summary, a.Before, a.After,
            a.StudentId, a.TeacherId, AuditSections.SectionOf(a.EntityType))).ToList();
    }

    /// <summary>
    /// BO'LIMLAR RO'YXATI + har birida nechta yozuv borligi (Sozlamalardagi "O'zgarishlar tarixi"
    /// sahifasidagi chiplar shundan). Sanoq AYNAN shu paytdagi filtrlar (davr/qidiruv/xodim/amal)
    /// bo'yicha hisoblanadi — chip ustidagi son ochilganda chiqadigan son bilan bir xil bo'lsin.
    /// Bo'sh bo'limlar ham qaytadi (count=0) — ro'yxat "sakrab" turmasin.
    /// </summary>
    [HttpGet("sections")]
    public async Task<ActionResult<AuditSectionsDto>> Sections(
        [FromQuery] string? action, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? actor, [FromQuery] string? q)
    {
        var query = ApplyFilters(
            db.AuditLogs.AsNoTracking(),
            entityType: null, entityId: null, studentId: null, teacherId: null, groupId: null,
            action, from, to, section: null, actor, q);

        // Bo'lim EMAS, TUR bo'yicha sanaymiz (bo'lim xaritasi C#da, SQLda emas), so'ng jamlaymiz.
        var byType = await query
            .GroupBy(a => a.EntityType)
            .Select(g => new { EntityType = g.Key, Count = g.Count() })
            .ToListAsync();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in byType)
        {
            var key = AuditSections.SectionOf(row.EntityType);
            counts[key] = counts.GetValueOrDefault(key) + row.Count;
        }

        // Xodimlar ro'yxati (filtr uchun) — tarixda haqiqatan uchraganlari.
        var actors = await query
            .Where(a => a.ActorName != null && a.ActorName != "")
            .Select(a => a.ActorName!)
            .Distinct()
            .OrderBy(n => n)
            .Take(200)
            .ToListAsync();

        var sections = AuditSections.All
            .Select(s => new AuditSectionDto(s.Key, s.Label, counts.GetValueOrDefault(s.Key)))
            .ToList();

        return new AuditSectionsDto(sections, counts.Values.Sum(), actors);
    }

    /// <summary>Ikkala endpoint uchun UMUMIY filtr — sanoq va ro'yxat bir xil qoidada bo'lsin.</summary>
    private static IQueryable<Domain.AuditLog> ApplyFilters(
        IQueryable<Domain.AuditLog> q,
        string? entityType, string? entityId, string? studentId, string? teacherId, string? groupId,
        string? action, string? from, string? to, string? section, string? actor, string? search)
    {
        if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId)) q = q.Where(a => a.EntityId == entityId);
        if (!string.IsNullOrEmpty(studentId)) q = q.Where(a => a.StudentId == studentId);
        if (!string.IsNullOrEmpty(teacherId)) q = q.Where(a => a.TeacherId == teacherId);
        // Guruhga oid: guruh yozuvining o'zi (EntityId == groupId) YOKI a'zolik hodisalari
        // (Membership entityId = "{groupId}:{studentId}" — ClassesController.audit.Record shu formatda yozadi).
        if (!string.IsNullOrEmpty(groupId))
            q = q.Where(a => a.EntityId == groupId || a.EntityId.StartsWith(groupId + ":"));
        if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(from)) q = q.Where(a => string.Compare(a.Timestamp, from) >= 0);
        if (!string.IsNullOrEmpty(to)) q = q.Where(a => string.Compare(a.Timestamp, to) <= 0);
        if (!string.IsNullOrEmpty(actor)) q = q.Where(a => a.ActorName == actor);
        // Qidiruv — loyihadagi odat bo'yicha `ToLower().Contains` (provayderga bog'liq emas;
        // Npgsql'ning ILike'i SQLite testlarida ishlamas edi).
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().ToLower();
            q = q.Where(a => a.Summary.ToLower().Contains(needle));
        }

        // BO'LIM filtri. "Boshqa" — xaritada YO'Q turlar (kelajakda qo'shilgan, hali xaritalanmagan
        // yozuvlar shu yerda ko'rinadi va e'tibordan chetda qolmaydi).
        if (AuditSections.IsKnownSection(section))
        {
            if (section == AuditSections.Other)
            {
                var known = AuditSections.KnownEntityTypes;
                q = q.Where(a => !known.Contains(a.EntityType));
            }
            else
            {
                var types = AuditSections.EntityTypesOf(section);
                q = q.Where(a => types.Contains(a.EntityType));
            }
        }

        return q;
    }
}
