using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

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
///
/// <para>⚠️ <b>Aylantirilgan lid</b> (o'quvchi bo'lgan) qayta ishlatilmaydi — suhbat unga bog'langan
/// bo'lsa ham YANGI lid ochiladi ("Avval o'quvchi bo'lgan" izohi bilan) — CRM-1,
/// <see cref="LeadIntake.FindByPhoneAsync"/>. Izoh telefon bo'yicha, telefon bo'lmasa — suhbat
/// bog'langan aylantirilgan lid bo'yicha (<see cref="IgLeadLinks.FormerStudentNoteAsync"/>).</para>
///
/// <para>⚠️ <c>conv.LeadId</c> FAQAT <see cref="IgLeadLinks.RelinkAsync"/> orqali almashtiriladi:
/// eski lid tarixga (<see cref="IgConversation.PastLeadIds"/>) yoziladi, aks holda Instagram va
/// reklama atributsiyasi pul to'lagan eski liddan yangi lidga ko'chib ketardi (audit R2-2).</para>
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
        // ⚠️ O'quvchiga AYLANTIRILGAN lid qayta ishlatilmaydi (CRM-1): bu yangi murojaat.
        if (!string.IsNullOrWhiteSpace(conv.LeadId))
            lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == conv.LeadId && l.ConvertedStudentId == null, ct);

        // (2) Telefon bo'yicha mavjud lid (lid formasi/daraja testi bilan yagona qoida).
        lead ??= await LeadIntake.FindByPhoneAsync(db, phone, ct);

        if (lead is not null)
        {
            // Faqat BO'SH maydonlar to'ldiriladi — AI xulosasi menejer kiritgan ma'lumot ustiga yozmasin.
            if (string.IsNullOrWhiteSpace(lead.Phone) && phone.Length > 0) lead.Phone = phone;
            if (string.IsNullOrWhiteSpace(lead.InterestSubject) && output.LeadProductInterest.Length > 0)
                lead.InterestSubject = output.LeadProductInterest;
            // Vaqtinchalik ism ("ali_v (Instagram)", "Instagram mijozi", "Noma'lum…") haqiqiy ism bilan
            // ALMASHTIRILADI (IG-4); menejer yozgan ism tegilmaydi.
            if (LeadIntake.IsPlaceholderName(lead.FullName) && output.LeadName.Length > 0)
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

            await IgLeadLinks.RelinkAsync(db, conv, lead.Id, ct);
            return (lead.Id, false);
        }

        // (3) Yangi lid.
        var stage = await LeadIntake.FirstStageIdAsync(db, ct);
        var name = output.LeadName.Length > 0
            ? output.LeadName
            : (string.IsNullOrWhiteSpace(conv.Username)
                ? LeadIntake.InstagramAnonymousName
                : LeadIntake.InstagramUsernameName(conv.Username));

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
        // Shu telefon bilan avval o'quvchi bo'lgan bo'lsa — kartada ochiq ko'rinsin (CRM-1).
        await LeadIntake.MarkFormerStudentAsync(db, fresh, phone, ct);
        // Telefon yozilmagan bo'lsa ham: suhbat AYLANTIRILGAN lidga bog'langan edi — bu qaytgan
        // o'quvchi. Izoh telefon bo'yicha allaqachon qo'shilgan bo'lsa takrorlanmaydi.
        if (!(fresh.Note ?? "").Contains("Avval o'quvchi bo'lgan", StringComparison.Ordinal))
        {
            var former = await IgLeadLinks.FormerStudentNoteAsync(db, conv.LeadId, ct);
            if (former.Length > 0)
                fresh.Note = string.IsNullOrWhiteSpace(fresh.Note) ? former : $"{fresh.Note!.TrimEnd()}\n{former}";
        }
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

        await IgLeadLinks.RelinkAsync(db, conv, fresh.Id, ct);
        return (fresh.Id, true);
    }

    /// <summary>
    /// AI xulosasi bilan MAVJUD lidni boyitadi — <b>shu so'rovda</b> telefon bo'yicha
    /// (AI'siz) allaqachon yozilgan lid uchun.
    ///
    /// <para>🔴 <b>NEGA <see cref="UpsertAsync"/> EMAS:</b> u <c>RepeatCount++</c> qiladi va
    /// hodisa yozadi. Bitta xabar uchun ikki marta chaqirilsa takror ×2 bo'lib ko'rinar,
    /// tarixda esa bir xil hodisa ikki marta turardi.</para>
    ///
    /// <para>⚠️ To'ldiriladigan maydonlar faqat BO'SH bo'lsa yoziladi (menejer kiritgani
    /// ustiga yozilmaydi) — bitta ISTISNO: <paramref name="allowNameOverwrite"/>. U ism SHU
    /// so'rovda matndan TAXMIN qilib qo'yilgan bo'lsa rost bo'ladi: AI aniqroq biladi, ya'ni
    /// «Ali Valiyev» taxmini AI bergan to'liq ism bilan almashadi.</para>
    ///
    /// <para>⚠️ <c>SaveChangesAsync</c> chaqirilmaydi (chaqiruvchining tranzaksiyasi).</para>
    /// </summary>
    public static async Task EnrichAsync(
        IAppDbContext db, string leadId, IgAgentOutput output, bool allowNameOverwrite,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leadId)) return;
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return;

        var phone = InstagramContract.ExtractPhone(output.LeadContact);
        if (string.IsNullOrWhiteSpace(lead.Phone) && phone.Length > 0) lead.Phone = phone;
        if (string.IsNullOrWhiteSpace(lead.InterestSubject) && output.LeadProductInterest.Length > 0)
            lead.InterestSubject = output.LeadProductInterest;
        if (output.LeadName.Length > 0
            && (LeadIntake.IsPlaceholderName(lead.FullName) || allowNameOverwrite))
            lead.FullName = output.LeadName;

        // AI xulosasi izohga QO'SHILADI — telefon bo'yicha yozilgan qatorda u yo'q edi, karta esa
        // aynan izohni ko'rsatadi ("nega bu odam yozdi?" savoliga javob shu yerda).
        if (output.LeadSummary.Length > 0)
            lead.Note = ((lead.Note ?? "").TrimEnd() + "\n" + InstagramContract.Trim(output.LeadSummary, 300)).Trim();
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
