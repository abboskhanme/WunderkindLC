using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LIDNI O'CHIRISH — <c>DELETE api/admin/leads/{id}</c> ning ma'lumot qismi (controller faqat audit,
/// saqlash va Telegram kartasini bajaradi). SaveChanges QILMAYDI.
///
/// <list type="bullet">
///   <item><b>Aylantirilgan lid O'CHIRILMAYDI</b> (CRM-5) — u KPI "shartnoma" (<c>ClosedByUserId</c>),
///     va sotuv tushumi (lid → o'quvchi → to'lov) uchun YAGONA bog'lanish.</item>
///   <item><b>Tarix arxivlanadi</b> (CRM-4): hodisalar va sinov darslari o'chirilishdan OLDIN alohida
///     <see cref="HistoryArchiveType"/> arxiv yozuviga suratga olinadi — JSON xossalari
///     <c>"Events"</c> (<see cref="LeadEvent"/> ro'yxati) va <c>"Trials"</c> (<see cref="TrialLesson"/>
///     ro'yxati). Tiklash — <c>ArchiveController.Restore</c> (lid tiklanganda tarix ham qaytadi).</item>
///   <item><b>Instagram bog'lanishlari tozalanadi</b> (CRM-3): <c>IgConversation.LeadId</c> = null,
///     <c>IgAdLead.LeadId</c> = "" — aks holda Inbox'da «Lidga aylantirish» yashirinar, reklama
///     lidini «Qayta olish» "allaqachon CRM'da" deb rad etar va qayta yaratilgan lid e'lon qilinmasdi.</item>
/// </list>
/// </summary>
public static class LeadRemoval
{
    /// <summary>Hodisalar + sinovlar suratining arxiv turi (ArchiveController bilan shartnoma).</summary>
    public const string HistoryArchiveType = "lead_history";

    /// <summary>Aylantirilgan lidni o'chirishga urinilganda javob.</summary>
    public const string ConvertedLockedMessage =
        "O'quvchiga aylantirilgan lidni o'chirib bo'lmaydi — u KPI (shartnoma) va sotuv hisoboti uchun kerak.";

    /// <summary>
    /// Lidni o'chirishga tayyorlaydi (arxiv suratlari + bog'liq yozuvlar). Rad etilsa — sabab matni
    /// (hech narsa o'zgartirilmaydi), aks holda <c>null</c>.
    /// </summary>
    public static async Task<string?> RemoveAsync(
        IAppDbContext db, Lead lead, string? reason, string actorName, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(lead.ConvertedStudentId)) return ConvertedLockedMessage;

        var events = await db.LeadEvents.Where(e => e.LeadId == lead.Id).ToListAsync(ct);
        var trials = await db.TrialLessons.Where(t => t.LeadId == lead.Id).ToListAsync(ct);
        // ⚠️ Surat O'CHIRISHDAN OLDIN olinadi (bo'sh bo'lsa ham — tiklovchi har doim bir xil shaklni oladi).
        ArchiveService.Snapshot(db, HistoryArchiveType, lead.Id, lead.FullName, "",
            new { Events = events, Trials = trials }, reason, actorName);
        db.LeadEvents.RemoveRange(events);
        db.TrialLessons.RemoveRange(trials);

        foreach (var conv in await db.IgConversations.Where(c => c.LeadId == lead.Id).ToListAsync(ct))
            conv.LeadId = null;
        foreach (var ad in await db.IgAdLeads.Where(a => a.LeadId == lead.Id).ToListAsync(ct))
            ad.LeadId = "";

        db.Leads.Remove(lead);
        ArchiveService.Snapshot(db, "lead", lead.Id, lead.FullName, lead.Phone ?? "", lead, reason, actorName);
        return null;
    }
}
