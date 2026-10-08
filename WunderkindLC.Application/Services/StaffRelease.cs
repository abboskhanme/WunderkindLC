using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// XODIM AKKAUNTI O'CHIRILGANDA unga bog'langan JONLI havolalarni bo'shatish (StaffController.Delete).
/// SaveChanges QILMAYDI — o'chirish bilan BIR tranzaksiyada saqlanadi (testlangan: <c>AuditFixT10Tests</c>).
///
/// <para>Ilgari akkaunt o'char, havolalar esa qolardi: KPI profili faol qolib "arvoh" qator bo'lib
/// lidlar hisobini olardi; ochiq lidlar, kassa, topshiriqlar "yo'q odam"ga biriktirilgan bo'lib qolardi;
/// Telegram bog'lanishi orqali o'chirilgan odamga topshiriq eslatmasi ketaverardi va u "Bajardim"
/// bosa olardi; CTI agenti (telefon) ishlayverardi.</para>
///
/// <para>⚠️ Agent STANDART Local SMS agenti bo'lsa — barcha avtomatik Local SMS to'xtaydi. Bu jimgina
/// bo'lmaydi: <see cref="Result.DefaultSmsAgentName"/> audit matniga va o'chirish javobiga tushadi.</para>
///
/// <para>⚠️ FAQAT JONLI havolalar bo'shatiladi — TARIX tegilmaydi: yopilgan/yo'qotilgan/aylantirilgan
/// lidlarning <c>AssigneeUserId</c> i (yo'qotish hisoboti va KPI shundan), bajarilgan topshiriqlar,
/// <c>ClosedByUserId</c>, tasdiqlangan KPI natijalari o'z joyida qoladi.</para>
/// </summary>
public static class StaffRelease
{
    /// <summary>Nima bo'shatildi — audit matni uchun sanoqlar.</summary>
    public sealed record Result(int KpiProfiles, int OpenLeads, int Cashboxes, int OpenTasks, int CtiAgents, int TelegramLinks)
    {
        public bool Any => KpiProfiles + OpenLeads + Cashboxes + OpenTasks + CtiAgents + TelegramLinks > 0;

        /// <summary>O'chirilgan agentlardan biri STANDART Local SMS agenti edi (Local SMS yoqiq) — uning
        /// nomi; aks holda <c>null</c>. Endi barcha avtomatik Local SMS to'xtaydi — audit matnida va
        /// javobda OCHIQ aytiladi (<see cref="LocalSmsDefaultAgent"/>).</summary>
        public string? DefaultSmsAgentName { get; init; }

        /// <summary>"KPI profili nofaol qilindi, 3 ta ochiq lid, 1 ta kassa ..." — bo'sh bo'lsa "".</summary>
        public string Summary()
        {
            var parts = new List<string>();
            if (KpiProfiles > 0) parts.Add("KPI profili nofaol qilindi");
            if (OpenLeads > 0) parts.Add($"{OpenLeads} ta ochiq lid biriktirilmagan holatga o'tdi");
            if (Cashboxes > 0) parts.Add($"{Cashboxes} ta kassa mas'ulsiz qoldi");
            if (OpenTasks > 0) parts.Add($"{OpenTasks} ta ochiq topshiriq mas'ulsiz qoldi");
            if (CtiAgents > 0) parts.Add($"{CtiAgents} ta CTI agent (telefon) o'chirildi");
            if (TelegramLinks > 0) parts.Add($"{TelegramLinks} ta Telegram bog'lanishi uzildi");
            if (DefaultSmsAgentName is not null)
                parts.Add($"⚠️ \"{DefaultSmsAgentName}\" standart Local SMS agenti edi — avtomatik Local SMS'lar to'xtadi");
            return string.Join(", ", parts);
        }
    }

    /// <param name="actorId">Amalni bajargan foydalanuvchi — topshiriq tarixiga yoziladi.</param>
    /// <param name="userName">O'chirilayotgan xodim ismi — topshiriq tarixi matni uchun.</param>
    public static async Task<Result> ReleaseAsync(
        IAppDbContext db, string userId, string userName, string actorId, CancellationToken ct = default)
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

        var tasks = await db.WorkTasks.Where(t => t.AssigneeId == userId && t.CompletedAt == null).ToListAsync(ct);
        foreach (var t in tasks)
        {
            t.AssigneeId = string.Empty;
            t.UpdatedAt = AppClock.Now;
            db.WorkTaskEvents.Add(new WorkTaskEvent
            {
                TaskId = t.Id, ActorId = actorId, Kind = "assigned",
                Text = $"Mas'ul olib tashlandi: {userName} (xodim akkaunti o'chirildi)",
            });
        }

        // CTI agent — `CtiController.UpdateAgent` dagi o'chirish bilan BIR XIL: nofaol, onlayn emas, FCM
        // tokeni tozalanadi (o'chirilgan telefonni push bilan uyg'otishga urinilmasin). Jonli WebSocket'ni
        // chaqiruvchi SAQLAGANDAN KEYIN uzadi (`CtiConnectionManager.DisconnectAsync`, StaffController.Delete).
        var agents = await db.CtiAgents.Where(a => a.StaffUserId == userId).ToListAsync(ct);
        var defaultAgentId = await LocalSmsDefaultAgent.DefaultAmongAsync(
            db, agents.Where(a => a.IsActive).Select(a => a.Id).ToList(), ct);
        var defaultAgent = agents.FirstOrDefault(a => a.Id == defaultAgentId);
        foreach (var a in agents)
        {
            a.IsActive = false;
            a.StaffUserId = null;
            a.IsOnline = false;
            a.FcmToken = "";
        }

        // Xodim bog'lanishi o'chiriladi. Qator ota-ona/o'qituvchi bog'lanishi ham bo'lsa (StudentId/TeacherId
        // to'ldirilgan) — faqat xodim qismi uziladi, ota-ona xabarlari yo'qolmasin.
        var links = await db.TelegramRegistrations.Where(r => r.UserId == userId).ToListAsync(ct);
        foreach (var r in links)
        {
            if (string.IsNullOrEmpty(r.StudentId) && string.IsNullOrEmpty(r.TeacherId)) db.TelegramRegistrations.Remove(r);
            else r.UserId = null;
        }

        return new Result(profiles.Count, leads.Count, cashboxes.Count, tasks.Count, agents.Count, links.Count)
        {
            DefaultSmsAgentName = defaultAgent is null ? null : LocalSmsDefaultAgent.NameOf(defaultAgent),
        };
    }
}
