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
/// XODIMLAR ESLATMALARI — klientda «Eslatmalarim → Xodimlar» papkasi.
/// <list type="bullet">
///   <item><b>O'qish</b>: rahbariyat (admin/superadmin, HR «direktor») — barcha xodimlar; HR menejeri — bo'ysunuvchilari
///     (ichma-ich, sozlamadagi lavozimlar, <see cref="WikiRules.TeamNoteOwners"/>); oddiy xodim — hech kim.</item>
///   <item><b>Yozish — FAQAT RAHBARIYAT</b>: xodimga yangi sahifa qo'shish, uning eslatmasini tahrirlash (klient oldin
///     "o'zgartirasizmi?" deb so'raydi), QULFLASH (shunda egasi ham faqat o'qiydi) va o'chirish. Menejer — faqat o'qiydi.</item>
/// </list>
/// Ko'rinmaydigan xodim/eslatma — 404 (mavjudligi oshkor bo'lmasin). O'z eslatmasi bu yerda emas — <c>MyNotesController</c>.
///
/// <para>Ruxsat: <c>wiki.team</c> (o'qish ham). SOZLAMA (menejer ko'radimi, qaysi lavozimlar) — faqat rahbariyat.
/// ⚠️ Shaxsiy eslatma tarixga YOZILMAYDI (rahbariyatning amallari ham) — sarlavhalar shaxsiy; kim oxirgi
/// o'zgartirgani sahifaning o'zida (`UpdatedBy`).</para>
/// Qoidalar: <c>.claude/rules/wiki.md</c> §9–10.
/// </summary>
[ApiController]
[Authorize]
[NoStore]
[AdminPerm("wiki.team", ReadRequiresPerm = true)]
[Route("api/admin/wiki/team")]
public class WikiTeamController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";
    private WikiViewer? _viewer;
    private async Task<WikiViewer> ViewerAsync() => _viewer ??= await WikiAccess.ResolveAsync(db, User);

    private static object NotFoundBody(string what) => new { message = $"{what} topilmadi" };

    /// <summary>Begona (o'zinikidan boshqa) va ko'rinadigan xodim.</summary>
    private static bool Visible(WikiViewer v, string userId) =>
        WikiRules.IsRealUserId(userId) && userId != v.UserId && v.CanSeeNotesOf(userId);

    private static IEnumerable<(string Id, string ParentId, bool Locked)> Shape(IEnumerable<WikiPage> pages) =>
        pages.Select(x => (x.Id, x.ParentId, x.Locked));

    /// <summary>
    /// Ko'rinadigan xodimlar (o'zisiz): ism, lavozim, HR roli, eslatmalar soni va oxirgi yozgan vaqti.
    /// Eslatmasi yo'q xodim ham ro'yxatda (direktor "kim umuman yozmayapti"ni ham ko'rsin).
    /// </summary>
    [HttpGet("members")]
    public async Task<ActionResult<object>> Members()
    {
        var viewer = await ViewerAsync();
        var employees = await db.HrEmployees.AsNoTracking().Where(e => e.IsActive).ToListAsync();
        var visible = employees.Where(e => Visible(viewer, e.UserId)).ToList();
        var userIds = visible.Select(e => e.UserId).ToList();

        var stats = (await db.WikiPages.AsNoTracking()
                .Where(p => userIds.Contains(p.OwnerUserId))
                .GroupBy(p => p.OwnerUserId)
                // Bo'limlar (papkalar) eslatma sanalmaydi.
                .Select(g => new { UserId = g.Key, Count = g.Count(p => !p.IsSection), Last = g.Max(p => p.UpdatedAt) })
                .ToListAsync())
            .ToDictionary(x => x.UserId);
        var positions = await db.HrPositions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);

        var members = visible
            .Select(e => new WikiTeamMemberDto(
                e.UserId, e.FullName,
                e.PositionId is not null && positions.TryGetValue(e.PositionId, out var pos) ? pos : "",
                e.HrRole,
                stats.GetValueOrDefault(e.UserId)?.Count ?? 0,
                stats.GetValueOrDefault(e.UserId)?.Last ?? ""))
            // Yaqinda yozganlar tepada, keyin ism bo'yicha.
            .OrderByDescending(m => m.LastUpdatedAt).ThenBy(m => m.FullName)
            .ToList();

        return new { isLeader = viewer.IsLeader, members };
    }

    /// <summary>Bitta xodimning eslatmalar daraxti (matnsiz, qulf belgilari bilan).</summary>
    [HttpGet("members/{userId}")]
    public async Task<ActionResult<WikiNotesTreeDto>> MemberTree(string userId)
    {
        var viewer = await ViewerAsync();
        if (!Visible(viewer, userId)) return NotFound(NotFoundBody("Xodim"));
        var name = await MemberNameAsync(userId);
        if (name is null) return NotFound(NotFoundBody("Xodim"));
        var nodes = await db.WikiPages.AsNoTracking().Where(p => p.OwnerUserId == userId)
            .OrderBy(p => p.Order).ThenBy(p => p.CreatedAt)
            .Select(p => new WikiTreeNodeDto(p.Id, p.ParentId, p.Title, p.Icon, p.Order, p.Locked, p.IsSection)).ToListAsync();
        return new WikiNotesTreeDto(userId, name, nodes);
    }

    /// <summary>Bitta eslatma. Klient uni ochiq turganda bir necha soniyada qayta so'raydi — xodim yozayotganini
    /// rahbar "jonli" ko'radi (versiya o'zgarsa matn yangilanadi).</summary>
    [HttpGet("pages/{id}")]
    public async Task<ActionResult<WikiPageDto>> Page(string id)
    {
        var viewer = await ViewerAsync();
        var page = await db.WikiPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId != "");
        if (page is null || !Visible(viewer, page.OwnerUserId)) return NotFound(NotFoundBody("Eslatma"));
        var shape = await OwnerShapeAsync(page.OwnerUserId);
        return WikiPageOps.ToDto(page, WikiRules.IsLocked(shape, page.Id));
    }

    // =============================================================================================
    //  RAHBARIYAT: xodim eslatmalarida yozish
    // =============================================================================================

    /// <summary>Xodimga YANGI sahifa qo'shish (masalan shaxsiy vazifa yoki ko'rsatma). U xodimning o'z daraxtida chiqadi.</summary>
    [HttpPost("members/{userId}/pages")]
    public async Task<ActionResult<WikiPageDto>> CreateForMember(string userId, WikiPageCreatePayload p)
    {
        var viewer = await ViewerAsync();
        if (!viewer.IsLeader) return LeadersOnly();
        if (!Visible(viewer, userId) || await MemberNameAsync(userId) is null) return NotFound(NotFoundBody("Xodim"));
        var pages = await db.WikiPages.Where(x => x.OwnerUserId == userId).ToListAsync();
        if (WikiPageOps.Create(pages, userId, p, Actor, out var page) is { } e) return StatusCode(e.Status, e.Body);
        db.WikiPages.Add(page!);
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page!, WikiRules.IsLocked(Shape(pages), page!.ParentId));
    }

    /// <summary>Xodim eslatmasini TAHRIRLASH (qulflangan bo'lsa ham). Versiya tekshiruvi odatdagidek (409).</summary>
    [HttpPut("pages/{id}")]
    public async Task<ActionResult<WikiPageDto>> SaveMemberPage(string id, WikiPageSavePayload p)
    {
        var viewer = await ViewerAsync();
        if (!viewer.IsLeader) return LeadersOnly();
        var page = await db.WikiPages.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId != "");
        if (page is null || !Visible(viewer, page.OwnerUserId)) return NotFound(NotFoundBody("Eslatma"));
        if (WikiPageOps.ApplySave(page, p, Actor) is { } e) return StatusCode(e.Status, e.Body);
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page, WikiRules.IsLocked(await OwnerShapeAsync(page.OwnerUserId), page.Id));
    }

    /// <summary>QULF: eslatma (va ichidagilar) egasining o'ziga ham faqat o'qish bo'ladi.</summary>
    [HttpPut("pages/{id}/lock")]
    public async Task<ActionResult<WikiPageDto>> LockMemberPage(string id, WikiLockPayload p)
    {
        var viewer = await ViewerAsync();
        if (!viewer.IsLeader) return LeadersOnly();
        var page = await db.WikiPages.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId != "");
        if (page is null || !Visible(viewer, page.OwnerUserId)) return NotFound(NotFoundBody("Eslatma"));
        page.Locked = p.Locked;
        page.LockedBy = p.Locked ? Actor : "";
        page.LockedAt = p.Locked ? AppClock.Iso() : "";
        await db.SaveChangesAsync();
        return WikiPageOps.ToDto(page, WikiRules.IsLocked(await OwnerShapeAsync(page.OwnerUserId), page.Id));
    }

    /// <summary>Xodim eslatmasini (ichidagilari bilan) o'chirish — masalan rahbar xato qo'shgan sahifa.</summary>
    [HttpDelete("pages/{id}")]
    public async Task<ActionResult<object>> DeleteMemberPage(string id)
    {
        var viewer = await ViewerAsync();
        if (!viewer.IsLeader) return LeadersOnly();
        var page = await db.WikiPages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId != "");
        if (page is null || !Visible(viewer, page.OwnerUserId)) return NotFound(NotFoundBody("Eslatma"));
        var all = await db.WikiPages.Where(x => x.OwnerUserId == page.OwnerUserId).ToListAsync();
        var subtree = WikiRules.SubtreeIds(all.Select(x => (x.Id, x.ParentId)), id);
        db.WikiPages.RemoveRange(all.Where(x => subtree.Contains(x.Id)));
        await db.SaveChangesAsync();
        return new { deleted = subtree.Count };
    }

    // =============================================================================================
    //  SOZLAMA — faqat rahbariyat
    // =============================================================================================

    [HttpGet("settings")]
    public async Task<ActionResult<WikiSettingsDto>> GetSettings()
    {
        if (!(await ViewerAsync()).IsLeader) return LeadersOnly();
        var s = await WikiAccess.SettingsAsync(db);
        var positions = await db.HrPositions.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new WikiPositionDto(p.Id, p.Name)).ToListAsync();
        return new WikiSettingsDto(s.ManagersSeeTeamNotes, s.ManagerVisiblePositionIds, positions);
    }

    [HttpPut("settings")]
    public async Task<ActionResult<WikiSettingsDto>> SaveSettings(WikiSettingsPayload p)
    {
        if (!(await ViewerAsync()).IsLeader) return LeadersOnly();
        var known = await db.HrPositions.AsNoTracking().Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name);
        var ids = (p.ManagerVisiblePositionIds ?? []).Where(known.ContainsKey).Distinct().ToList();

        var s = await db.WikiSettings.FirstOrDefaultAsync(x => x.Id == WikiSettings.SingletonId);
        if (s is null) db.WikiSettings.Add(s = new WikiSettings());
        var before = (s.ManagersSeeTeamNotes, Positions: string.Join(", ", s.ManagerVisiblePositionIds.Select(i => known.GetValueOrDefault(i, i))));
        s.ManagersSeeTeamNotes = p.ManagersSeeTeamNotes;
        s.ManagerVisiblePositionIds = ids;
        s.UpdatedAt = AppClock.Iso();
        s.UpdatedBy = Actor;

        var after = string.Join(", ", ids.Select(i => known[i]));
        audit.Record("WikiSettings", WikiSettings.SingletonId, "update",
            "Bilimlar bazasi sozlamasi: menejerlar bo'ysunuvchilar eslatmalarini " +
            (p.ManagersSeeTeamNotes ? "ko'radi" : "KO'RMAYDI") +
            (p.ManagersSeeTeamNotes ? $" (lavozimlar: {(after.Length > 0 ? after : "barchasi")})" : "") +
            $" — avval: {(before.ManagersSeeTeamNotes ? "ko'rardi" : "ko'rmasdi")}" +
            (before.ManagersSeeTeamNotes ? $" ({(before.Positions.Length > 0 ? before.Positions : "barchasi")})" : ""));
        await db.SaveChangesAsync();
        return await GetSettings();
    }

    // =============================================================================================

    private async Task<string?> MemberNameAsync(string userId) =>
        await db.HrEmployees.AsNoTracking().Where(e => e.UserId == userId).Select(e => e.FullName).FirstOrDefaultAsync()
        ?? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync();

    private async Task<List<(string Id, string ParentId, bool Locked)>> OwnerShapeAsync(string ownerUserId) =>
        (await db.WikiPages.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId)
            .Select(x => new { x.Id, x.ParentId, x.Locked }).ToListAsync())
        .Select(x => (x.Id, x.ParentId, x.Locked)).ToList();

    private ObjectResult LeadersOnly() =>
        StatusCode(403, new { message = "Bu amalni faqat rahbariyat (direktor yoki administrator) bajaradi" });
}
