using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// REKLAMA LIDINI (Meta Lead Ads) CRM'dagi <see cref="Lead"/> ga ulaydi.
///
/// <para>Qoidalar <see cref="InstagramLeadBridge"/> bilan AYNAN bir xil — ATAYIN: bir odam avval
/// reklama formasini to'ldirib, keyin DM yozishi (yoki aksincha) juda odatiy holat va ikki joyda
/// ikki xil qoida bo'lsa CRM'da bitta odam ikkita kartochka bo'lib qolardi.</para>
///
/// <list type="bullet">
///   <item><b>Dublikat:</b> telefon bo'yicha mavjud lid izlanadi
///     (<see cref="LeadIntake.FindByPhoneAsync"/> — lid formasi va daraja testi bilan yagona qoida);</item>
///   <item><b>FIRST-TOUCH:</b> mavjud lidda <c>Source</c> ham, <c>Stage</c> ham O'ZGARMAYDI —
///     odamni birinchi qaysi kanal olib kelgani va menejer kanbanda qo'lda qo'ygan holati
///     buzilmasin. O'rniga <c>RepeatCount++</c> va <c>LeadEvent</c>;</item>
///   <item><b>Faqat BO'SH maydonlar to'ldiriladi</b> — reklama formasidagi qiymat menejer
///     kiritgan ma'lumot ustiga yozmasin.</item>
/// </list>
///
/// <para>⚠️ <c>SaveChangesAsync</c> chaqirilmaydi — yozuvlar chaqiruvchining (pipeline)
/// tranzaksiyasida saqlanadi. <see cref="Lead.PhoneKey"/> ham QO'LDA yozilmaydi
/// (<c>AppDbContext.SaveChanges</c> o'zi hisoblaydi).</para>
/// </summary>
public static class MetaLeadBridge
{
    /// <summary>Lid hodisalarida ko'rinadigan ijrochi nomi.</summary>
    public const string ActorName = "Instagram reklamasi";

    /// <summary>Manba nomi bo'sh qolsa ishlatiladigan qiymat.</summary>
    public const string DefaultSource = "Instagram reklama";

    /// <summary>
    /// Reklama lididan CRM lidi yaratadi yoki mavjudini yangilaydi.
    /// </summary>
    /// <param name="adLead">Bazaga yozilayotgan reklama lidi (maydonlari to'ldirilgan).</param>
    /// <param name="sourceName">`CenterMeta.InstagramAdsLeadSource` — lid manbasining NOMI.</param>
    /// <returns>Lid id va u YANGI yaratildimi.</returns>
    public static async Task<(string LeadId, bool IsNew)> UpsertAsync(
        IAppDbContext db, IgAdLead adLead, string sourceName, CancellationToken ct = default)
    {
        var now = AppClock.Iso();
        var source = string.IsNullOrWhiteSpace(sourceName) ? DefaultSource : sourceName.Trim();
        var phone = (adLead.Phone ?? "").Trim();
        var name = (adLead.FullName ?? "").Trim();
        var interest = (adLead.FormName ?? "").Trim();
        var note = BuildNote(adLead);

        var lead = await LeadIntake.FindByPhoneAsync(db, phone, ct);
        if (lead is not null)
        {
            if (string.IsNullOrWhiteSpace(lead.Phone) && phone.Length > 0) lead.Phone = phone;
            if (string.IsNullOrWhiteSpace(lead.FullName) && name.Length > 0) lead.FullName = name;
            if (string.IsNullOrWhiteSpace(lead.InterestSubject) && interest.Length > 0)
                lead.InterestSubject = interest;
            lead.Note = ((lead.Note ?? "").TrimEnd() + "\n" + note).Trim();
            lead.RepeatCount += 1;
            lead.LastRepeatAt = now;
            // ⚠️ Source va Stage ATAYIN tegilmaydi (first-touch).

            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id,
                Type = "note",
                ActorName = ActorName,
                CreatedAt = now,
                Text = "Reklama formasini yana to'ldirdi" + AdSuffix(adLead),
            });

            return (lead.Id, false);
        }

        // ⚠️ Telefonsiz lid ham YOZILADI: Meta formasida telefon majburiy bo'lmasligi mumkin
        // (yoki mijoz noto'g'ri kiritgan). Jimgina tashlab yuborilsa markaz pul to'lagan
        // murojaatdan umuman xabar topmasdi.
        var stage = await LeadIntake.FirstStageIdAsync(db, ct);
        var fresh = new Lead
        {
            FullName = name.Length > 0 ? name : "Reklama lidi (ismsiz)",
            Phone = phone,
            Source = source,
            InterestSubject = interest,
            Note = note,
            Stage = stage,
            CreatedAt = now,
        };
        db.Leads.Add(fresh);
        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = fresh.Id,
            Type = "created",
            ActorName = ActorName,
            CreatedAt = now,
            Text = "Reklama formasi orqali keldi" + AdSuffix(adLead),
            ToStage = stage,
        });

        return (fresh.Id, true);
    }

    /// <summary>Lid izohi — menejer "bu odam qayerdan keldi" savoliga kartochkaning O'ZIDAN
    /// javob topsin (reklama bo'limini ochmasdan).</summary>
    private static string BuildNote(IgAdLead l)
    {
        var parts = new List<string> { "Instagram reklamasi (Lead Ads)" };
        if (!string.IsNullOrWhiteSpace(l.FormName)) parts.Add($"Forma: {l.FormName}");
        if (!string.IsNullOrWhiteSpace(l.CampaignName)) parts.Add($"Kampaniya: {l.CampaignName}");
        if (!string.IsNullOrWhiteSpace(l.AdName)) parts.Add($"E'lon: {l.AdName}");
        if (!string.IsNullOrWhiteSpace(l.Email)) parts.Add($"Email: {l.Email}");
        return string.Join(" · ", parts);
    }

    /// <summary>Hodisa matnining qavs ichidagi qismi — nomlar bo'lmasa qavs umuman chiqmaydi.</summary>
    private static string AdSuffix(IgAdLead l)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.FormName)) bits.Add(l.FormName);
        if (!string.IsNullOrWhiteSpace(l.CampaignName)) bits.Add(l.CampaignName);
        return bits.Count == 0 ? "" : $" ({string.Join(" · ", bits)})";
    }
}
