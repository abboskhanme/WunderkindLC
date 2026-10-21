using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// XODIM AKKAUNTI O'CHIRILGANDA unga bog'langan JONLI havolalarni bo'shatish (StaffController.Delete).
/// SaveChanges QILMAYDI — o'chirish bilan BIR tranzaksiyada saqlanadi (testlangan: <c>AuditFixT10Tests</c>).
///
/// <para>Ilgari akkaunt o'char, havolalar esa qolardi: KPI profili faol qolib "arvoh" qator bo'lib
/// lidlar hisobini olardi; ochiq lidlar va kassa "yo'q odam"ga biriktirilgan bo'lib qolardi;
/// Telegram bog'lanishi orqali o'chirilgan odamga xabarlar ketaverardi. (Local Call agent-ilovasi
/// 2026-10 da olib tashlandi — agent bo'shatish ham yo'q; Kanban «Topshiriqlar» 2026-10-21 da olib
/// tashlandi — topshiriq bo'shatish ham yo'q.)</para>
///
/// <para>⚠️ FAQAT JONLI havolalar bo'shatiladi — TARIX tegilmaydi: yopilgan/yo'qotilgan/aylantirilgan
/// lidlarning <c>AssigneeUserId</c> i (yo'qotish hisoboti va KPI shundan), <c>ClosedByUserId</c>,
/// tasdiqlangan KPI natijalari o'z joyida qoladi.</para>
/// </summary>
public static class StaffRelease
{
    /// <summary>Nima bo'shatildi — audit matni uchun sanoqlar.</summary>
    public sealed record Result(int KpiProfiles, int OpenLeads, int Cashboxes, int TelegramLinks)
    {
        public bool Any => KpiProfiles + OpenLeads + Cashboxes + TelegramLinks > 0;

        /// <summary>"KPI profili nofaol qilindi, 3 ta ochiq lid, 1 ta kassa ..." — bo'sh bo'lsa "".</summary>
        public string Summary()
        {
            var parts = new List<string>();
            if (KpiProfiles > 0) parts.Add("KPI profili nofaol qilindi");
            if (OpenLeads > 0) parts.Add($"{OpenLeads} ta ochiq lid biriktirilmagan holatga o'tdi");
            if (Cashboxes > 0) parts.Add($"{Cashboxes} ta kassa mas'ulsiz qoldi");
            if (TelegramLinks > 0) parts.Add($"{TelegramLinks} ta Telegram bog'lanishi uzildi");
            return string.Join(", ", parts);
        }
    }

    public static async Task<Result> ReleaseAsync(IAppDbContext db, string userId, CancellationToken ct = default)
    {
        var profiles = await db.KpiProfiles.Where(p => p.UserId == userId && p.IsActive).ToListAsync(ct);
        foreach (var p in profiles) p.IsActive = false;

        // Ochiq lid = aylantirilmagan, shartnoma yopilmagan va yo'qotilmagan (ChecklistAutoCheck bilan bir xil).
        var leads = await db.Leads
            .Where(l => l.AssigneeUserId == userId && l.ConvertedStudentId == null && l.ClosedAt == null && l.LostAt == null)
            .ToListAsync(ct);
        foreach (var l in leads) l.AssigneeUserId = null;

        // Kassa: mas'ul ham, uning ismi (snapshot) ham tozalanadi — kartada "mas'ul: X" turib qolsa,
        // admin pul hamon o'sha odamga yo'naltirilyapti deb o'ylardi.
        var cashboxes = await db.Cashboxes.Where(b => b.ResponsibleUserId == userId).ToListAsync(ct);
        foreach (var b in cashboxes) { b.ResponsibleUserId = null; b.ResponsibleName = null; }

        // Xodim bog'lanishi o'chiriladi. Qator ota-ona/o'qituvchi bog'lanishi ham bo'lsa (StudentId/TeacherId
        // to'ldirilgan) — faqat xodim qismi uziladi, ota-ona xabarlari yo'qolmasin.
        var links = await db.TelegramRegistrations.Where(r => r.UserId == userId).ToListAsync(ct);
        foreach (var r in links)
        {
            if (string.IsNullOrEmpty(r.StudentId) && string.IsNullOrEmpty(r.TeacherId)) db.TelegramRegistrations.Remove(r);
            else r.UserId = null;
        }

        return new Result(profiles.Count, leads.Count, cashboxes.Count, links.Count);
    }
}
