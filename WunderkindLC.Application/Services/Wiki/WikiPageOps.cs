using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>Amal rad etildi — controller uni HTTP javobiga aylantiradi.</summary>
/// <param name="Conflict">409 — boshqa odam shu orada saqlagan (klient avtosaqlashni to'xtatadi).</param>
public sealed record WikiError(int Status, string Message, bool Conflict = false)
{
    public object Body => Conflict ? new { conflict = true, message = Message } : new { message = Message };
    public static WikiError BadRequest(string message) => new(400, message);
}

/// <summary>
/// Eslatma amallari — egasining o'zi (`MyNotesController`) ham, rahbariyat (`WikiTeamController`) ham AYNAN shu
/// joydan foydalanadi: saqlash (versiya, faqat matn), ko'chirish (halqa, tartib). Sof — yuklangan obyektlar ustida
/// ishlaydi, bazaga yozmaydi (buni controller qiladi). Testlar: <c>WikiRulesTests</c>.
/// </summary>
public static class WikiPageOps
{
    public static WikiPageDto ToDto(WikiPage p, bool effectiveLocked = false) => new(
        p.Id, p.OwnerUserId, p.ParentId, p.Title, p.Icon, p.ContentJson, p.Version,
        p.CreatedAt, p.CreatedBy, p.UpdatedAt, p.UpdatedBy, p.Locked, effectiveLocked || p.Locked);

    public static WikiPage New(string ownerUserId, string parentId, string title, string icon, int order, string actor)
    {
        var now = AppClock.Iso();
        return new WikiPage
        {
            OwnerUserId = ownerUserId, ParentId = parentId, Title = title,
            Icon = WikiRules.NormalizeIcon(icon), Order = order,
            CreatedAt = now, CreatedBy = actor, UpdatedAt = now, UpdatedBy = actor,
        };
    }

    /// <summary>Bir ota ichidagi keyingi tartib raqami.</summary>
    public static int NextOrder(IEnumerable<WikiPage> siblings) =>
        siblings.Select(s => s.Order).DefaultIfEmpty(-1).Max() + 1;

    /// <summary>
    /// Yangi sahifa yoki BO'LIM (egasining o'zi ham, rahbariyat ham shu yo'ldan). <paramref name="pool"/> — egasining
    /// BARCHA eslatmalari. Qulf tekshiruvi — chaqiruvchida (rahbariyat qulflangan joyga ham qo'sha oladi).
    /// Yaratilgan sahifa bazaga QO'SHILMAYDI — buni controller qiladi.
    /// </summary>
    public static WikiError? Create(List<WikiPage> pool, string ownerUserId, WikiPageCreatePayload p, string actor, out WikiPage? page)
    {
        page = null;
        var isSection = p.IsSection == true;
        var parentId = p.ParentId ?? "";
        if (WikiRules.SectionPlacementError(isSection, parentId) is { } placeErr) return WikiError.BadRequest(placeErr);
        if (WikiRules.ValidateTitle(p.Title, out var title, isSection ? WikiRules.DefaultSectionTitle : null) is { } err)
            return WikiError.BadRequest(err);
        if (parentId.Length > 0 && pool.All(x => x.Id != parentId)) return WikiError.BadRequest("Ota eslatma topilmadi");
        if (pool.Count >= WikiRules.MaxPagesPerOwner)
            return WikiError.BadRequest($"Ko'pi bilan {WikiRules.MaxPagesPerOwner} ta eslatma bo'ladi");

        page = New(ownerUserId, parentId, title, p.Icon ?? "",
            NextOrder(pool.Where(x => x.ParentId == parentId && x.IsSection == isSection)), actor);
        page.IsSection = isSection;
        return null;
    }

    /// <summary>
    /// Saqlash: versiya tekshiruvi (409), sarlavha va matn (faqat matn) tekshiruvi, keyin qo'llash.
    /// </summary>
    public static WikiError? ApplySave(WikiPage page, WikiPageSavePayload p, string actor)
    {
        if (p.BaseVersion != page.Version)
            return new WikiError(409,
                $"Bu sahifani shu orada {page.UpdatedBy} o'zgartirdi ({page.UpdatedAt.Replace('T', ' ')}). " +
                "Sizning oxirgi o'zgarishingiz saqlanmadi — sahifani yangilab, qaytadan kiriting.", Conflict: true);
        if (WikiRules.ValidateTitle(p.Title, out var title, page.IsSection ? WikiRules.DefaultSectionTitle : null) is { } titleErr)
            return WikiError.BadRequest(titleErr);
        // Bo'limda matn yo'q (u faqat sarlavha) — kelgani e'tiborsiz.
        var json = page.IsSection ? "" : p.ContentJson;
        if (WikiRules.ParseContent(json, out var text) is { } contentErr) return WikiError.BadRequest(contentErr);

        page.Title = title;
        page.Icon = WikiRules.NormalizeIcon(p.Icon);
        page.ContentJson = json ?? "";
        page.ContentText = text;
        page.Version++;
        page.UpdatedAt = AppClock.Iso();
        page.UpdatedBy = actor;
        return null;
    }

    /// <summary>
    /// Ko'chirish (shu EGANING daraxti ichida). <paramref name="pool"/> — egasining BARCHA eslatmalari (kuzatiladigan).
    /// Nishon ota shu egada bo'lishi shart; o'zining yoki ichki sahifasining ostiga — rad. Muvaffaqiyatda yangi ota
    /// ichida 0..n qayta raqamlanadi.
    /// </summary>
    public static WikiError? ApplyMove(List<WikiPage> pool, WikiPage page, string parentId, int index)
    {
        if (WikiRules.SectionPlacementError(page.IsSection, parentId) is { } placeErr) return WikiError.BadRequest(placeErr);
        if (parentId.Length > 0 && pool.FirstOrDefault(x => x.Id == parentId)?.OwnerUserId != page.OwnerUserId)
            return WikiError.BadRequest("Ota eslatma topilmadi");
        if (!WikiRules.CanMoveUnder(pool.Select(x => (x.Id, x.ParentId)), page.Id, parentId))
            return WikiError.BadRequest("Eslatmani o'zining ichiga ko'chirib bo'lmaydi");

        page.ParentId = parentId;
        // Yuqori darajada bo'limlar va «Mening eslatmalarim» sahifalari ALOHIDA tartiblanadi (klient ham ikkalasini
        // alohida ro'yxat qilib ko'rsatadi, `index` shu ro'yxatdagi o'rin).
        var siblings = pool
            .Where(x => x.Id != page.Id && x.OwnerUserId == page.OwnerUserId && x.ParentId == parentId && x.IsSection == page.IsSection)
            .OrderBy(x => x.Order).ThenBy(x => x.CreatedAt).ToList();
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), page);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Order = i;
        return null;
    }
}
