using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>Apex domendagi statik landing sahifasi ("Bepul darsga yozilish" formasi) uchun ommaviy
/// (autentifikatsiyasiz) lid qabul qiluvchi endpoint. Lid obyekti qaytarilmaydi (faqat {ok:true}).
///
/// <para>QOIDA MANBASI — <c>.claude/rules/lead-forms.md</c> §4 ("BIR TELEFON = BITTA LID") va §6.5.
/// Bu yerdagi mantiq <see cref="LeadFormService.SubmitAsync"/> va <c>LevelTestService</c> bilan
/// AYNAN bir xil bo'lishi shart — aks holda bir odam saytdan, formadan va daraja testidan kelganda
/// CRM uchta xil natija ko'rsatardi:</para>
/// <list type="bullet">
///   <item>telefon bo'yicha mavjud lid izlanadi (<see cref="LeadIntake.FindByPhoneAsync"/>) —
///     dublikat lid OCHILMAYDI, takroriy murojaat <c>RepeatCount</c> bilan belgilanadi;</item>
///   <item>lid BOSQICHI takroriy arizada o'zgarmaydi (first-touch), MANBA ham o'zgarmaydi;</item>
///   <item>yangi lid birinchi bosqichga tushadi (<see cref="LeadIntake.FirstStageIdAsync"/>) —
///     bosqich yo'q bo'lsa lid BOSQICHSIZ qoladi, sun'iy ustun YARATILMAYDI;</item>
///   <item>avto-xabar (<c>lead_new</c>) FAQAT yangi lidga, Telegram xabarnomasi esa ikkalasida ham.</item>
/// </list>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/landing-lead")]
public class PublicLandingController(AppDbContext db, TelegramService telegram, AutoMessageService autoMsg) : ControllerBase
{
    public record LandingLeadRequest(string FullName, string Phone, string? Subject, string? Note);

    /// <summary>Landing sahifasida fan tanlanmagan bo'lsa YANGI lidga yoziladigan qiymat
    /// (sahifaning asosiy taklifi). Takroriy murojaatda BU QIYMAT ISHLATILMAYDI — §4 first-touch.</summary>
    private const string DefaultSubject = "General English";

    /// <summary>Lid manbasi — <c>LeadSource</c> katalogidagi NOM bilan bir xil bo'lishi kerak
    /// (`.claude/rules/crm-leads.md`), aks holda "Manba" filtri bir xil kanalni ikki qator qilib
    /// ko'rsatardi. Katalogda topilmasa shu matn yoziladi.</summary>
    private const string SourceName = "Sayt";

    [HttpPost]
    [HttpPost("/api/public/leads")]
    [EnableRateLimiting("public-lead")]
    public async Task<IActionResult> Create([FromBody] LandingLeadRequest p)
    {
        var fullName = (p.FullName ?? "").Trim();
        if (fullName.Length == 0)
            return BadRequest(new { message = "Ism-familiya kiritilishi shart" });
        if (fullName.Length > 100)
            return BadRequest(new { message = "Ism-familiya juda uzun" });

        var (valid, normalizedPhone, phoneError) = PhoneUtil.Validate(p.Phone);
        if (!valid)
            return BadRequest(new { message = phoneError ?? "Telefon raqami noto'g'ri" });

        // ⚠️ Mijoz HAQIQATAN tanlagan fan (bo'sh bo'lishi MUMKIN) va yangi lidga yoziladigan
        // qiymat ATAYIN ajratilgan: ilgari bo'sh tanlov ham "General English" ga aylantirilar va
        // takroriy arizada mavjud lidning fani (masalan "IELTS") jimgina ustidan yozilardi.
        var chosenSubject = (p.Subject ?? "").Trim();
        if (chosenSubject.Length > 100) chosenSubject = chosenSubject[..100];

        var note = (p.Note ?? "").Trim();
        if (note.Length > 500) note = note[..500];

        var now = Now();

        // MAVJUD LIDNI TEKSHIRISH (dublikat yaratmaydi, balki hodisa va izoh qo'shadi)
        var existingLead = await LeadIntake.FindByPhoneAsync(db, normalizedPhone);
        var isNewLead = existingLead is null;
        Lead lead;

        if (existingLead is not null)
        {
            lead = existingLead;
            // TAKRORIY MUROJAAT belgisi — bosqich ATAYIN o'zgarmaydi (menejerning kanbandagi
            // qo'lda qo'ygan holatini tizim buzmasin), manba ham o'zgarmaydi (first-touch).
            lead.RepeatCount++;
            lead.LastRepeatAt = now;
            if (!string.IsNullOrWhiteSpace(note))
                lead.Note = string.IsNullOrWhiteSpace(lead.Note) ? note : $"{lead.Note} | Sayt: {note}";

            // ⚠️ Fan FAQAT lidda umuman yo'q bo'lsa va mijoz HAQIQATAN tanlagan bo'lsa to'ldiriladi
            // (LeadFormService.SubmitAsync bilan bir xil qoida) — birinchi murojaatdagi qiziqish
            // saqlanadi.
            if (string.IsNullOrWhiteSpace(lead.InterestSubject) && chosenSubject.Length > 0)
                lead.InterestSubject = chosenSubject;

            // Ismi umuman yo'q (yoki "Noma'lum...") lid saytdan kelgan ism bilan to'ldiriladi.
            if (string.IsNullOrWhiteSpace(lead.FullName) || lead.FullName.StartsWith("Noma'lum"))
                lead.FullName = fullName;

            var subjectText = chosenSubject.Length > 0 ? chosenSubject : "fan ko'rsatilmagan";
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id,
                Type = "repeat_intake",
                Text = string.IsNullOrWhiteSpace(note)
                    ? $"Saytdan qayta ariza keldi ({subjectText})"
                    : $"Saytdan qayta ariza keldi ({subjectText}) — Izoh: {note}",
                ActorName = "Sayt",
                CreatedAt = now,
                ToStage = lead.Stage,
            });
        }
        else
        {
            // ⚠️ Bosqich YO'Q bo'lsa lid BOSQICHSIZ ("") qoladi — ommaviy, autentifikatsiyasiz
            // endpoint kanban ustunini YARATMAYDI (lead-forms.md §6.5).
            var firstStageId = await LeadIntake.FirstStageIdAsync(db);

            lead = new Lead
            {
                FullName = fullName,
                Phone = normalizedPhone,
                Stage = firstStageId,
                Source = await ResolveSourceNameAsync(),
                InterestSubject = chosenSubject.Length > 0 ? chosenSubject : DefaultSubject,
                Note = note,
                CreatedAt = now,
            };
            db.Leads.Add(lead);
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id,
                Type = "created",
                Text = string.IsNullOrWhiteSpace(note)
                    ? $"Lid yaratildi ({lead.FullName})"
                    : $"Lid yaratildi ({lead.FullName}) — Izoh: {note}",
                ActorName = "Sayt",
                CreatedAt = now,
                ToStage = firstStageId,
            });
        }

        await db.SaveChangesAsync();

        try
        {
            // Telegram xabarnomasi IKKALA holatda ham ketadi (takroriyda sarlavha boshqacha).
            await LeadNotifier.NotifyNewLeadAsync(db, telegram, lead, isNewLead: isNewLead,
                createdBy: string.IsNullOrWhiteSpace(note) ? "Sayt (ochiq forma)" : $"Sayt ({note})");

            // ⚠️ Avto-xabar (tanishuv SMS'i) FAQAT YANGI lidga — takroriy arizada mijozga bir xil
            // SMS qayta ketardi (markazga haqiqiy pul, mijozga spam). lead-forms.md §4.
            if (isNewLead)
                await autoMsg.DispatchLeadAsync(db, AutoMessageTriggers.LeadNew, lead);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PublicLandingController] Bildirishnoma xatosi: {ex.Message}");
        }

        return Ok(new { ok = true, id = lead.Id });
    }

    /// <summary>Manba nomini <c>LeadSource</c> katalogidan oladi (registr farqisiz), topilmasa
    /// <see cref="SourceName"/>. Katalogdagi AYNAN o'sha yozuv ishlatilsa "Manba" filtri va
    /// statistika bitta qatorda yig'iladi.</summary>
    private async Task<string> ResolveSourceNameAsync()
    {
        var key = SourceName.ToLower();
        var fromCatalog = await db.LeadSources
            .Where(s => s.Name.ToLower() == key)
            .Select(s => s.Name)
            .FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(fromCatalog) ? SourceName : fromCatalog;
    }

    private static string Now() => AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
