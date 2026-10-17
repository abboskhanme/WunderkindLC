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
/// so'rov UMUMAN ketmaydi (modul o'chiq — tashqariga hech narsa chiqmaydi qoidasi). Lekin
/// <see cref="IgAdLead"/> qatori XATO bilan SAQLANADI (<see cref="ModuleOffError"/>) — modul
/// yoqilgach «Qayta olish» uni tiklaydi; pullik lid izsiz yo'qolmaydi.</para>
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
            // 🔴 PULLIK LID YO'QOLMAYDI. Ilgari bu yerda faqat sabab qaytarilardi: AI agenti
            // yoqilgan, reklama lidlari esa o'chiq markazda navbat yozuvi `done` bo'lib yopilar,
            // `IgAdLead` qatori esa UMUMAN yaratilmasdi — ya'ni «Qayta olish» tugmasi ham
            // ko'rinmasdi va to'langan reklamadan kelgan murojaat izsiz yo'qolardi.
            // Endi qator XATO bilan saqlanadi (tashqariga so'rov KETMAYDI — darvoza buzilmaydi):
            // modul yoqilgach admin «Qayta olish» bilan ism/telefonni Meta'dan oladi (~90 kun).
            var saved = await SaveUnprocessedAsync(db, events, ModuleOffError, ct);
            if (saved > 0) await db.SaveChangesAsync(ct);
            problems.Add(saved > 0
                ? $"Reklama lidlari moduli o'chirilgan — {saved} ta lid «Reklama lidlari» ro'yxatiga xato bilan "
                  + "yozildi (modulni yoqib «Qayta olish» bosing)."
                : "Reklama lidlari moduli o'chirilgan — hodisa qayta ishlanmadi.");
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

    /// <summary>Modul o'chiq paytda kelgan reklama lidining xato matni (ro'yxatda ko'rinadi).</summary>
    public const string ModuleOffError =
        "Reklama lidlari moduli o'chiq paytda keldi — ism va telefon olinmagan. "
        + "Modulni yoqib, «Qayta olish» tugmasini bosing.";

    /// <summary>
    /// Qayta ishlanmagan reklama lidlarini <see cref="IgAdLead"/> qatori sifatida XATO bilan
    /// qo'shadi (tashqariga so'rov YO'Q). Allaqachon bor <c>LeadgenId</c> o'tkazib yuboriladi.
    ///
    /// <para><b>SaveChanges QILMAYDI</b> — chaqiruvchi saqlaydi. Ikki joy chaqiradi: modul o'chiq
    /// paytda kelgan hodisa (<see cref="HandleAsync"/>) va navbat tozalanishi
    /// (<c>InstagramWorkerService.CleanupAsync</c> — 30 kun qayta ishlanmay yotgan hodisa
    /// o'chirilishidan OLDIN, pullik lid izsiz yo'qolmasin).</para>
    /// </summary>
    /// <returns>Nechta yangi qator qo'shildi.</returns>
    public static async Task<int> SaveUnprocessedAsync(
        IAppDbContext db, IReadOnlyList<IgLeadgenEvent> events, string error, CancellationToken ct)
    {
        var added = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ev in events)
        {
            if (string.IsNullOrWhiteSpace(ev.LeadgenId) || !seen.Add(ev.LeadgenId)) continue;
            if (await db.IgAdLeads.AnyAsync(l => l.LeadgenId == ev.LeadgenId, ct)) continue;

            db.IgAdLeads.Add(new IgAdLead
            {
                LeadgenId = ev.LeadgenId,
                PageId = ev.PageId,
                FormId = ev.FormId,
                AdId = ev.AdId,
                AdsetId = ev.AdgroupId,
                CreatedTime = ev.CreatedTimeIso.Length > 0 ? ev.CreatedTimeIso : AppClock.Iso(),
                ReceivedAt = AppClock.Iso(),
                Error = error,
            });
            added++;
        }
        return added;
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
        // Lid avto-topshirig'i Telegram bayrog'iga BOG'LIQ EMAS (bayroq o'chiq bo'lsa karta yo'q, topshiriq bor).
        if (row.LeadId.Length > 0 && !meta.InstagramNotifyTelegram)
            await LeadAutoTask.EnsureByIdAsync(db, telegram, row.LeadId, repeat: !row.IsNewLead, ct);
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
