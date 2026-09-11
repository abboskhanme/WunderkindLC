using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// REKLAMA LIDI (Meta Lead Ads) hodisasini to'liq qayta ishlaydi:
/// <c>webhook → Graph'dan ma'lumot → IgAdLead → CRM Lead → Telegram signali</c>.
///
/// <para><b>Nega alohida xizmat?</b> <see cref="InstagramPipeline"/> izoh/DM oqimi bilan
/// allaqachon to'la va reklama lidining oqimi u bilan HECH NARSA bo'lishmaydi (AI ham, suhbat
/// ham, 24 soatlik oyna ham yo'q). Pipeline faqat hodisani shu yerga uzatadi.</para>
///
/// <para><b>Darvoza:</b> <c>CenterMeta.InstagramLeadAdsEnabled == false</c> bo'lsa Graph API'ga
/// so'rov UMUMAN ketmaydi (modul o'chiq — tashqariga hech narsa chiqmaydi qoidasi). Hodisa
/// navbatda `skipped` bo'lib qoladi va sababi yozib qo'yiladi.</para>
///
/// <para>DI: <c>builder.Services.AddScoped&lt;MetaLeadgenService&gt;();</c></para>
/// </summary>
public sealed class MetaLeadgenService(
    IAppDbContext db,
    MetaAdsApi api,
    TelegramService telegram,
    ILogger<MetaLeadgenService> logger)
{
    /// <summary>Bitta webhook yozuvidagi BARCHA reklama lidlarini qayta ishlaydi.</summary>
    /// <returns>Qayta ishlashda chiqqan muammolar (bo'sh = hammasi joyida).</returns>
    public async Task<IReadOnlyList<string>> HandleAsync(
        IReadOnlyList<IgLeadgenEvent> events, CenterMeta? meta, CancellationToken ct)
    {
        var problems = new List<string>();
        if (events.Count == 0) return problems;

        if (meta is null || !meta.InstagramLeadAdsEnabled)
        {
            problems.Add("Reklama lidlari moduli o'chirilgan — hodisa qayta ishlanmadi.");
            return problems;
        }

        var page = await db.IgAdPages
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.ConnectedAt)
            .FirstOrDefaultAsync(ct);

        foreach (var ev in events)
        {
            try { await HandleOneAsync(ev, meta, page, ct); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reklama lidini qayta ishlashda xatolik ({Id})", ev.LeadgenId);
                problems.Add(ex.Message);
            }
        }

        return problems;
    }

    private async Task HandleOneAsync(IgLeadgenEvent ev, CenterMeta meta, IgAdPage? page, CancellationToken ct)
    {
        // ── 1) DEDUP (navbat kalitidan MUSTAQIL) ──
        // Navbat yozuvlari 30 kunda tozalanadi, bu tekshiruv esa abadiy: Meta eski hodisani
        // qayta yuborsa ham ikkinchi lid ochilmaydi.
        if (await db.IgAdLeads.AnyAsync(l => l.LeadgenId == ev.LeadgenId, ct))
        {
            logger.LogInformation("Reklama lidi allaqachon qabul qilingan — o'tkazib yuborildi ({Id})", ev.LeadgenId);
            return;
        }

        var row = new IgAdLead
        {
            LeadgenId = ev.LeadgenId,
            PageId = ev.PageId,
            FormId = ev.FormId,
            AdId = ev.AdId,
            AdsetId = ev.AdgroupId,
            CreatedTime = ev.CreatedTimeIso.Length > 0 ? ev.CreatedTimeIso : AppClock.Iso(),
            ReceivedAt = AppClock.Iso(),
        };

        var token = page?.AccessToken ?? "";
        if (token.Length == 0)
        {
            // ⚠️ Yozuv BARIBIR saqlanadi: keyin token kiritilganda admin "qayta olish"
            // tugmasi bilan ma'lumotni to'ldira oladi. Jimgina yo'qolgan lid — eng yomon holat.
            row.Error = "Page Access Token yo'q — Marketing → Sozlamalar bo'limida ulang.";
            db.IgAdLeads.Add(row);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Reklama lidi keldi, lekin Page Access Token sozlanmagan ({Id})", ev.LeadgenId);
            return;
        }

        // ── 2) MA'LUMOTNI OLISH ──
        // Webhook payloadida ism ham, telefon ham YO'Q — faqat shu so'rov beradi.
        var (ok, data, err) = await api.FetchLeadAsync(ev.LeadgenId, token, ct);
        if (!ok || data is null)
        {
            row.Error = err;
            db.IgAdLeads.Add(row);
            if (page is not null) page.LastError = err;
            await db.SaveChangesAsync(ct);
            return;
        }

        row.FullName = data.FullName;
        row.Phone = PhoneUtil.Normalize(data.Phone);
        row.Email = data.Email;
        row.RawFieldsJson = data.FieldsJson;
        row.AdName = data.AdName;
        row.CampaignId = data.CampaignId;
        row.CampaignName = data.CampaignName;
        row.Platform = data.Platform;
        if (data.FormId.Length > 0) row.FormId = data.FormId;
        if (data.AdId.Length > 0) row.AdId = data.AdId;
        if (data.AdsetId.Length > 0) row.AdsetId = data.AdsetId;
        if (data.CreatedTimeIso.Length > 0) row.CreatedTime = data.CreatedTimeIso;
        row.FormName = await FormNameAsync(row.FormId, token, ct);

        // ── 3) CRM LIDI ──
        try
        {
            var (leadId, isNew) = await MetaLeadBridge.UpsertAsync(db, row, meta.InstagramAdsLeadSource, ct);
            row.LeadId = leadId;
            row.IsNewLead = isNew;
        }
        catch (Exception ex)
        {
            // Lid yaratilmasa ham reklama lidi yozuvi QOLADI — ro'yxatda xato bilan ko'rinadi.
            logger.LogError(ex, "Reklama lididan CRM lidi yaratilmadi ({Id})", ev.LeadgenId);
            row.Error = InstagramContract.Trim("Lid yaratilmadi: " + ex.Message, 400);
        }

        db.IgAdLeads.Add(row);
        if (page is not null)
        {
            page.LastLeadAt = row.ReceivedAt;
            page.LastError = "";
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Reklama lidi qabul qilindi ({Id}) — lid: {Lead}", ev.LeadgenId, row.LeadId.Length > 0 ? "ha" : "yo'q");

        // ── 4) TELEGRAM SIGNALI ──
        // Xatosi JIM yutiladi (`LeadNotifier` siyosati) — xabarnoma lidni buzmasin.
        if (row.LeadId.Length > 0 && meta.InstagramNotifyTelegram)
        {
            var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == row.LeadId, ct);
            if (lead is not null)
                await LeadNotifier.NotifyNewLeadAsync(
                    db, telegram, lead, isNewLead: row.IsNewLead,
                    createdBy: MetaLeadBridge.ActorName, ct: ct);
        }
    }

    /// <summary>
    /// Forma NOMI. Avval O'SHA formaning oldingi lididan olinadi (kesh) va faqat topilmasa
    /// Graph so'raladi — aks holda har lid uchun ortiqcha so'rov ketardi va bir necha lid
    /// birdaniga kelganda rate-limitga urilardik.
    /// </summary>
    private async Task<string> FormNameAsync(string formId, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formId)) return "";

        var known = await db.IgAdLeads
            .Where(l => l.FormId == formId && l.FormName != "")
            .Select(l => l.FormName)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(known)) return known;

        return await api.FetchFormNameAsync(formId, token, ct);
    }
}
