using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Instagram suhbatini CRM'dagi <see cref="Lead"/> ga ulaydi.
///
/// <para><b>Dublikat ochilmaydi — uch qavat:</b> (1) suhbatda allaqachon lid bo'lsa
/// (<see cref="IgConversation.LeadId"/>) yangisi YARATILMAYDI; (2) telefon topilsa
/// <see cref="LeadIntake.FindByPhoneAsync"/> orqali mavjud lid izlanadi (lid formasi va daraja
/// testi bilan AYNAN bir xil qoida); (3) telefonsiz qaynoq lid ham yoziladi, lekin uning nomi
/// <c>username (Instagram)</c> — keyin telefon kelsa o'sha lid to'ldiriladi.</para>
///
/// <para>⚠️ <b>MATNLARDA <c>@username</c> EMAS, PROFIL HAVOLASI</b>
/// (<see cref="InstagramContract.ProfileRef"/>): lid izohi Telegram guruhidagi lid kartasiga
/// tushadi, u yerda esa <c>@nom</c> Telegram mentioni bo'lib chizilib, bosilganda mavjud
/// bo'lmagan TELEGRAM foydalanuvchisiga olib borardi — menejer mijozga yoza olmasdi. Shu sabab
/// lid ISMIDAGI <c>@</c> ham olib tashlangan (CRM ro'yxatida u shunchaki bezak edi, Telegram
/// kartasida esa yolg'on havola).</para>
///
/// <para><b>FIRST-TOUCH:</b> mavjud lidda <c>Source</c> ham, <c>Stage</c> ham O'ZGARMAYDI —
/// odamni birinchi qaysi kanal olib kelgani va menejerning kanbandagi qo'lda qo'ygan holati
/// buzilmasin. O'rniga <c>RepeatCount++</c> va <c>LeadEvent</c> yoziladi.</para>
///
/// <para>⚠️ <see cref="Lead.PhoneKey"/> QO'LDA YOZILMAYDI — uni <c>AppDbContext.SaveChanges</c>
/// o'zi hisoblaydi.</para>
///
/// <para>⚠️ <c>SaveChangesAsync</c> chaqirilmaydi — yozuvlar chaqiruvchining (pipeline)
/// tranzaksiyasida saqlanadi.</para>
/// </summary>
public static class InstagramLeadBridge
{
    public const string ActorName = "Instagram AI agenti";

    /// <summary>Suhbatdan lid yaratadi yoki mavjudini yangilaydi. Qaytaradi: lid id va yangimi.</summary>
    public static async Task<(string LeadId, bool IsNew)> UpsertAsync(
        IAppDbContext db, IgConversation conv, IgAgentOutput output, string sourceName,
        CancellationToken ct = default)
    {
        var now = AppClock.Iso();
        var source = string.IsNullOrWhiteSpace(sourceName) ? "Instagram" : sourceName.Trim();
        var phone = InstagramContract.ExtractPhone(output.LeadContact);
        var note = BuildNote(conv, output);

        // (1) Suhbat allaqachon lidga bog'langan — yangisi ochilmaydi.
        Lead? lead = null;
        if (!string.IsNullOrWhiteSpace(conv.LeadId))
            lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == conv.LeadId, ct);

        // (2) Telefon bo'yicha mavjud lid (lid formasi/daraja testi bilan yagona qoida).
        lead ??= await LeadIntake.FindByPhoneAsync(db, phone, ct);

        if (lead is not null)
        {
            // Faqat BO'SH maydonlar to'ldiriladi — AI xulosasi menejer kiritgan ma'lumot ustiga yozmasin.
            if (string.IsNullOrWhiteSpace(lead.Phone) && phone.Length > 0) lead.Phone = phone;
            if (string.IsNullOrWhiteSpace(lead.InterestSubject) && output.LeadProductInterest.Length > 0)
                lead.InterestSubject = output.LeadProductInterest;
            if (string.IsNullOrWhiteSpace(lead.FullName) && output.LeadName.Length > 0)
                lead.FullName = output.LeadName;
            lead.Note = ((lead.Note ?? "").TrimEnd() + "\n" + note).Trim();
            lead.RepeatCount += 1;
            lead.LastRepeatAt = now;
            // ⚠️ Source va Stage ATAYIN tegilmaydi (first-touch).

            // ⚠️ Havola QAVS ICHIGA olinmaydi: Telegram xom URL'ni avtomatik havolaga aylantirganda
            // yopuvchi ")" ni ham manzilga qo'shib yuborardi — havola singan bo'lardi.
            var again = new List<string> { "Instagram'da yana yozdi" };
            if (output.LeadSummary.Length > 0) again.Add(InstagramContract.Trim(output.LeadSummary, 200));
            again.Add(InstagramContract.ProfileRef(conv.Username));

            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id,
                Type = "note",
                ActorName = ActorName,
                CreatedAt = now,
                Text = string.Join(" · ", again),
            });

            conv.LeadId = lead.Id;
            return (lead.Id, false);
        }

        // (3) Yangi lid.
        var stage = await LeadIntake.FirstStageIdAsync(db, ct);
        var name = output.LeadName.Length > 0
            ? output.LeadName
            : (string.IsNullOrWhiteSpace(conv.Username) ? "Instagram mijozi" : $"{conv.Username.Trim().TrimStart('@')} (Instagram)");

        var fresh = new Lead
        {
            FullName = name,
            Phone = phone,
            Source = source,
            InterestSubject = output.LeadProductInterest,
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
            // ⚠️ Havola ENG OXIRIDA va qavssiz — Telegram avtomatik havolaga aylantirganda
            // ortidagi belgi manzilga yopishib qolmasin.
            Text = $"Instagram orqali keldi — {InstagramContract.ProfileRef(conv.Username)}",
            ToStage = stage,
        });

        conv.LeadId = fresh.Id;
        return (fresh.Id, true);
    }

    /// <summary>Lid izohi — operator suhbatni ochmasdan turib nima bo'lganini tushunsin.</summary>
    private static string BuildNote(IgConversation conv, IgAgentOutput output)
    {
        // ⚠️ Bu izoh Telegram guruhidagi lid kartasiga («📝 …» qatori) TUSHADI — shuning uchun
        // profil MANZIL bilan yoziladi: menejer kartadan to'g'ridan-to'g'ri Instagram profiliga
        // o'tib yoza oladi.
        var parts = new List<string> { $"Instagram: {InstagramContract.ProfileRef(conv.Username)}" };
        if (output.LeadSummary.Length > 0) parts.Add(output.LeadSummary);
        if (output.LeadContact.Length > 0) parts.Add($"Aloqa: {output.LeadContact}");
        parts.Add($"Qiziqish bali: {InstagramContract.ClampScore(output.LeadScore)}");
        return string.Join(" · ", parts);
    }
}
