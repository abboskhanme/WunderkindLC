using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «CLICK-TO-DIRECT» REKLAMASIDAN kelgan lidlar — <b>ANIQ</b> atributsiya.
///
/// <para><b>Nima:</b> "Xabar yuborish" maqsadidagi reklamani bosib Direct'ga yozgan odamning
/// birinchi xabarida Meta <c>referral.ad_id</c> ni O'ZI beradi (taxmin EMAS). Pipeline uni
/// kiruvchi DM qatoriga (<c>IgMessage.AdId</c>) yozadi; suhbat keyin lidga bog'lanadi
/// (<c>IgConversation.LeadId</c>).</para>
///
/// <para><b>🔴 Muammo shu yordamchi tuzatadigan:</b> bu aniq bog'lanish hech qaysi hisobotga
/// yetib bormasdi — ROI (<see cref="MetaAdsRoi"/>) faqat <c>IgAdLeads</c> (forma lidlari) ni,
/// kanal tahlili (<see cref="LeadCrmOverview"/>) esa bunday lidlarni "Instagram (izoh/DM)" deb
/// sanardi. Natijada Click-to-Direct kampaniyasi "pul sarflandi, lid yo'q" bo'lib ko'rinardi.</para>
///
/// <para>⚠️ <b>FAQAT DM</b> (<c>Channel == dm</c>): izohdagi <c>AdId</c> — media↔creative
/// solishtiruvidan TIKLANGAN TAXMIN (<see cref="IgAdAttribution"/>) va hisobotda "aniq" son
/// sifatida ishlatilmaydi (qoidalar §20.1). DM'da esa <c>AdId</c> faqat <c>referral</c> dan
/// to'ldiriladi.</para>
///
/// <para>Bitta (suhbat, lid) — bitta qator: o'sha lidga tegishli ENG BIRINCHI reklama xabari
/// (first-touch).</para>
///
/// <para>🔴 <b>Lid — xabar KELGAN paytdagi lid, suhbatning HOZIRGI lidi emas</b> (audit R2-2).
/// Aylantirilgan lid qayta ishlatilmaydi: qaytgan o'quvchi yozsa suhbat YANGI lidga ko'chadi. Ilgari
/// atributsiya joriy <c>conv.LeadId</c> bo'yicha olinardi va reklama pul to'lagan eski liddan yangi,
/// to'lamagan lidga ko'chib ketardi — ROI'da reklama tushumi yo'qolardi. Endi xabar egasi
/// <see cref="IgLeadLinks.OwnerAt"/> bilan tarix (<c>IgConversation.PastLeadIds</c>) bo'yicha
/// aniqlanadi. Tarix bo'sh bo'lsa natija AYNAN eskisi.</para>
///
/// <para>⚠️ Bazada YO'Q (o'chirilgan) lidga qator chiqmaydi — ROI'dagi "CRM lidlari" soni faqat
/// mavjud lidlarni sanasin.</para>
/// </summary>
public static class IgDmAdLeads
{
    /// <summary>Bitta click-to-DM lidi: CRM lid · e'lon · kampaniya (bo'sh bo'lishi mumkin) ·
    /// reklama xabari vaqti (ISO).</summary>
    public sealed record Row(string LeadId, string AdId, string CampaignId, string CreatedAt);

    /// <summary>Atributsiya uchun bitta kiruvchi reklama DM xabari (suhbat bog'lanishi bilan).</summary>
    public sealed record AdMessage(
        string ConversationId, string? CurrentLeadId, IReadOnlyCollection<string>? PastLeadIds,
        string AdId, string CampaignId, string CreatedAt);

    /// <summary>
    /// SOF qism: har xabar egasini (<see cref="IgLeadLinks.OwnerAt"/>) aniqlaydi va (suhbat, lid)
    /// juftligiga ENG BIRINCHI xabarni qoldiradi. Egasi yo'q xabar (joriy lid bo'sh va tarixda
    /// ham tushmadi) tashlanadi.
    /// </summary>
    public static List<Row> Attribute(IEnumerable<AdMessage> messages) =>
        messages
            .Select(m => (Msg: m, Owner: IgLeadLinks.OwnerAt(m.CurrentLeadId, m.PastLeadIds, m.CreatedAt)))
            .Where(x => x.Owner.Length > 0)
            .GroupBy(x => (x.Msg.ConversationId, x.Owner))
            .Select(g => g.OrderBy(x => x.Msg.CreatedAt, StringComparer.Ordinal).First())
            .Select(x => new Row(x.Owner, x.Msg.AdId, x.Msg.CampaignId, x.Msg.CreatedAt))
            .ToList();

    /// <summary>Barcha click-to-DM lidlari (har (suhbat, lid) uchun bitta, eng birinchi reklama xabari).</summary>
    public static async Task<List<Row>> LoadAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var raw = await (
                from m in db.IgMessages.AsNoTracking()
                join c in db.IgConversations.AsNoTracking() on m.ConversationId equals c.Id
                where m.Direction == IgConst.DirIn
                      && m.Channel == IgConst.ChannelDm
                      && m.AdId != ""
                      && ((c.LeadId != null && c.LeadId != "") || c.PastLeadIds.Count > 0)
                select new { ConvId = c.Id, c.LeadId, c.PastLeadIds, m.AdId, m.AdCampaignId, m.CreatedAt })
            .ToListAsync(ct);

        var rows = Attribute(raw.Select(x => new AdMessage(
            x.ConvId, x.LeadId, x.PastLeadIds, x.AdId ?? "", x.AdCampaignId ?? "", x.CreatedAt ?? "")));
        if (rows.Count == 0) return rows;

        var owners = rows.Select(r => r.LeadId).Distinct(StringComparer.Ordinal).ToList();
        var existing = (await db.Leads.AsNoTracking()
                .Where(l => owners.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        return rows.Where(r => existing.Contains(r.LeadId)).ToList();
    }

    /// <summary>
    /// REKLAMA kanaliga tegishli BARCHA lid id'lari: Lead Ads formasi (<c>IgAdLead.LeadId</c>)
    /// + click-to-DM (<see cref="LoadAsync"/>).
    ///
    /// <para>⚠️ Kanal tahlili (<c>LeadOrigins.Classify</c> ning <c>adsLeadIds</c> parametri) shu
    /// to'plamni oladi — ikki sahifa (CRM statistikasi va «Butun CRM manzarasi») bir xil son
    /// ko'rsatishi uchun ikkalasi ham SHU funksiyani chaqirishi kerak.</para>
    /// </summary>
    public static async Task<HashSet<string>> AdsLeadIdsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var ids = (await db.IgAdLeads.AsNoTracking()
                .Where(x => x.LeadId != "").Select(x => x.LeadId).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var r in await LoadAsync(db, ct))
            if (r.LeadId.Length > 0) ids.Add(r.LeadId);
        return ids;
    }
}
