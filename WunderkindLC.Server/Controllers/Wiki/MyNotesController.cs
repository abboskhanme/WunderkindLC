using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MENING ESLATMALARIM — xodimning SHAXSIY eslatmalari (insaytlar, kunlik ish, rejalar). Egasi id'si TOKENDAN
/// olinadi: bu yerda faqat O'Z eslatmalari ko'rinadi va yoziladi, begonasi — 404 (mavjudligi ham oshkor bo'lmaydi).
///
/// <para>Kim ishlata oladi: HAR BIR xodim — staff, admin, superadmin, platforma egasi va O'QITUVCHI, RUXSATSIZ
/// (egasining talabi, 2026-10-27: "barchaga avtomatik ko'rinsin" — yangi akkaunt rolga qo'shilmasdan yozadi).
/// O'quvchi/ota-ona — yo'q (403).</para>
///
/// <para>⚠️ QULF: rahbariyat xodimning eslatmasini qulflashi mumkin (<c>WikiTeamController</c>) — shunda EGASINING O'ZI
/// ham uni (va ichidagilarni) faqat o'qiydi: saqlash, ichiga qo'shish, ko'chirish, o'chirish — 403.</para>
///
/// <para>⚠️ Shaxsiy eslatma "O'zgarishlar tarixi"ga YOZILMAYDI: tarixni `audit` ruxsatli har kim ko'radi, sarlavhalar
/// esa shaxsiy.</para>
/// Qoidalar: <c>.claude/rules/wiki.md</c> §9.
/// </summary>
[ApiController]
[Authorize]
[NoStore]
[Route("api/me/notes")]
public class MyNotesController(AppDbContext db) : ControllerBase
{
    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "";

    /// <summary>Faqat O'Z eslatmalari.</summary>
    private IQueryable<WikiPage> Mine => db.WikiPages.Where(p => p.OwnerUserId == UserId && p.OwnerUserId != "");

    private bool Allowed =>
        UserId.Length > 0 &&
        (User.IsInRole(Roles.Staff) || User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin)
         || User.IsInRole(Roles.PlatformOwner) || User.IsInRole(Roles.Teacher));

    private ObjectResult NoAccess() =>
        StatusCode(403, new { message = "Shaxsiy eslatmalar faqat markaz xodimlari uchun" });

    private ObjectResult Locked() => StatusCode(403, new { message = WikiRules.NoteLockedMessage });

    private static object NotFoundBody => new { message = "Eslatma topilmadi" };

    private static IEnumerable<(string Id, string ParentId, bool Locked)> Shape(IEnumerable<WikiPage> pages) =>
        pages.Select(x => (x.Id, x.ParentId, x.Locked));

    [HttpGet]
    public async Task<ActionResult<WikiNotesTreeDto>> Tree()
    {
        if (!Allowed) return NoAccess();
        var nodes = await Mine.AsNoTracking().OrderBy(p => p.Order).ThenBy(p => p.CreatedAt)
            .Select(p => new WikiTreeNodeDto(p.Id, p.ParentId, p.Title, p.Icon, p.Order, p.Locked, p.IsSection)).ToListAsync();
        return new WikiNotesTreeDto(UserId, Actor, nodes);
    }

    [HttpGet("pages/{id}")]
    public async Task<ActionResult<WikiPageDto>> Get(string id)
    {
        if (!Allowed) return NoAccess();
        var all = await Mine.AsNoTracking().ToListAsync();
        var page = all.FirstOrDefault(p => p.Id == id);
        return page is null ? NotFound(NotFoundBody) : WikiPageOps.ToDto(page, WikiRules.IsLocked(Shape(all), id));
    }

    [HttpPost("pages")]
    public async Task<ActionResult<WikiPageDto>> Create(WikiPageCreatePayload p)
    {
        if (!Allowed) return NoAccess();
        var pages = await Mine.ToListAsync();
        if (WikiRules.IsLocked(Shape(pages), p.ParentId ?? "")) return Locked();
        if (WikiPageOps.Create(pages, UserId, p, Actor, out var page) is { } e) return StatusCode(e.Status, e.Body);
        db.WikiPages.Add(page!);
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page!);
    }

    [HttpPut("pages/{id}")]
    public async Task<ActionResult<WikiPageDto>> Save(string id, WikiPageSavePayload p)
    {
        if (!Allowed) return NoAccess();
        var all = await Mine.ToListAsync();
        var page = all.FirstOrDefault(x => x.Id == id);
        if (page is null) return NotFound(NotFoundBody);
        if (WikiRules.IsLocked(Shape(all), id)) return Locked();
        if (WikiPageOps.ApplySave(page, p, Actor) is { } e) return StatusCode(e.Status, e.Body);
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page);
    }

    [HttpPut("pages/{id}/move")]
    public async Task<ActionResult<WikiPageDto>> Move(string id, WikiPageMovePayload p)
    {
        if (!Allowed) return NoAccess();
        var pool = await Mine.ToListAsync();
        var page = pool.FirstOrDefault(x => x.Id == id);
        if (page is null) return NotFound(NotFoundBody);
        var parentId = p.ParentId ?? "";
        // Qulflangan joydan olib chiqish ham, qulflangan joyga kiritish ham — yo'q.
        if (WikiRules.IsLocked(Shape(pool), id) || WikiRules.IsLocked(Shape(pool), parentId)) return Locked();
        if (WikiPageOps.ApplyMove(pool, page, parentId, p.Index) is { } e) return StatusCode(e.Status, e.Body);
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page);
    }

    [HttpDelete("pages/{id}")]
    public async Task<ActionResult<object>> Delete(string id)
    {
        if (!Allowed) return NoAccess();
        var all = await Mine.ToListAsync();
        if (all.All(x => x.Id != id)) return NotFound(NotFoundBody);
        var subtree = WikiRules.SubtreeIds(all.Select(x => (x.Id, x.ParentId)), id);
        // Qulflangan eslatma (yoki ichidagi qulflangan biri) otasi bilan birga yo'qolmasin.
        if (WikiRules.IsLocked(Shape(all), id) || all.Any(x => subtree.Contains(x.Id) && x.Locked)) return Locked();
        db.WikiPages.RemoveRange(all.Where(x => subtree.Contains(x.Id)));
        await db.SaveChangesAsync();
        return new { deleted = subtree.Count };
    }

    /// <summary>O'z eslatmalari ichidan qidiruv (ko'pi bilan 50 ta).</summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<WikiSearchHitDto>>> Search([FromQuery] string? q)
    {
        if (!Allowed) return NoAccess();
        var term = (q ?? "").Trim();
        if (term.Length < 2) return new List<WikiSearchHitDto>();
        var lower = term.ToLower();
        var hits = await Mine.AsNoTracking()
            .Where(p => !p.IsSection && (p.Title.ToLower().Contains(lower) || p.ContentText.ToLower().Contains(lower)))
            .OrderByDescending(p => p.UpdatedAt).Take(50)
            .Select(p => new { p.Id, p.Title, p.Icon, p.ContentText }).ToListAsync();
        return hits.Select(h => new WikiSearchHitDto(h.Id, h.Title, h.Icon,
            WikiRules.Snippet(h.ContentText, term))).ToList();
    }
}
