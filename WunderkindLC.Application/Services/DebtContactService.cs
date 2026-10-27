using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>QARZDORLIK → «BOG'LANISH KERAK» — sof qoidalar (`contacts.md` §9.5, <c>DebtContactTests</c>).</summary>
public static class DebtContactRules
{
    /// <summary>To'lov muddati standarti (egasining talabi, 2026-10-27: "20-gacha to'lov qilish kerak"); sozlama —
    /// <see cref="CenterMeta.ContactDebtDay"/> («Sozlamalar → Servis»).</summary>
    public const int DefaultDueDay = 20;
    public const int MinDueDay = 1;
    /// <summary>28 — har oyda bor sana (fevral ham).</summary>
    public const int MaxDueDay = 28;

    public static int ClampDay(int day) => day is >= MinDueDay and <= MaxDueDay ? day : DefaultDueDay;

    /// <summary>Talablar shu kundan (muddat KUNI ham) oy oxirigacha ochiladi; muddatdan keyin qarzdor bo'lganlar ham.</summary>
    public static bool IsOpenSeason(DateOnly today, int dueDay) => today.Day >= ClampDay(dueDay);

    public static string MonthKey(DateOnly day) => day.ToString("yyyy-MM");

    /// <summary>Qarz to'landimi: o'quvchi arxivlangan yoki umumiy balansi manfiy emas. (Qisman to'lov — hali qarzdor.)</summary>
    public static bool Settled(bool missing, bool archived, decimal balance) => missing || archived || balance >= 0;

    /// <summary>Talab izohi: «To'lov qarzdorligi: 450 000 so'm — Ingliz tili: 300 000 so'm; Matematika: 150 000 so'm».</summary>
    public static string NoteText(IReadOnlyList<(string Label, decimal Amount)> items)
    {
        var total = items.Sum(i => i.Amount);
        var parts = items.Select(i => string.IsNullOrWhiteSpace(i.Label)
            ? DebtMessages.Money(i.Amount) : $"{i.Label}: {DebtMessages.Money(i.Amount)}");
        return $"To'lov qarzdorligi: {DebtMessages.Money(total)} — " + string.Join("; ", parts);
    }

    public const string PaidText = "To'lov qilindi — qarz yopildi, talab avtomatik yopildi.";
}

/// <summary>
/// QARZDORLIK → «BOG'LANISH KERAK» (egasining talabi, 2026-10-27). Startupdan 3 daqiqa keyin, so'ng har 30 daqiqada
/// (08:00–22:00):
/// <list type="bullet">
///   <item><b>yopish (har kuni):</b> shu xizmat OCHGAN ochiq talab — o'quvchining qarzi to'langan bo'lsa → «Hal bo'ldi»
///   (tizim hodisasi, izoh <see cref="DebtContactRules.PaidText"/>);</item>
///   <item><b>ochish (oyning 20-sanasidan):</b> qarzdor (`Qarzdorlar` tabi bilan BIR XIL ta'rif — arxivsiz, balans manfiy,
///   yopilgan guruhli emas, kurs bo'yicha qarzi bor) va shu oy hali hodisasi yo'q o'quvchi → ochiq talab bo'lmasa yangi
///   talab («To'lov qarzdorligi», summa kurslar bo'yicha), bo'lsa unga izoh (ikkinchi ochiq talab HECH QACHON — §4).</item>
/// </list>
/// Oyiga BITTA hodisa (<see cref="DebtContactAlert"/>, unikal) — restart va qayta yugurish takror bermaydi.
/// </summary>
public sealed class DebtContactService(IServiceProvider services, ILogger<DebtContactService> logger) : BackgroundService
{
    public const string ReasonLabel = "To'lov qarzdorligi";
    public const string ActorName = "Tizim";

    public sealed record RunResult(int Requests, int Notes, int Closed);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (TaskCanceledException) { return; }

        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = AppClock.Now;
            if (first || (now.Hour >= 8 && now.Hour < 22))
            {
                try
                {
                    using var scope = services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                    var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
                    var r = await RunOnceAsync(db, audit, DateOnly.FromDateTime(now), stoppingToken);
                    if (r.Requests + r.Notes + r.Closed > 0)
                        logger.LogInformation("Qarzdorlik → bog'lanish: {R} talab, {N} izoh, {C} yopildi", r.Requests, r.Notes, r.Closed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Qarzdorlik → bog'lanish siklida xatolik");
                }
            }
            first = false;
            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    public static async Task<RunResult> RunOnceAsync(IAppDbContext db, AuditService audit, DateOnly today, CancellationToken ct = default)
    {
        // Yopish sozlamadan qat'i nazar (o'chirilgan bo'lsa ham to'langanlarning talabi navbatda osilib qolmasin).
        var closed = await CloseSettledAsync(db, audit, ct);
        // Qator yo'q — standart (yoqilgan, 20-sana), «Servis» sahifasi ko'rsatadigan qiymat bilan bir xil.
        var settings = await db.CenterMeta.AsNoTracking()
            .Select(m => new { m.ContactDebtAutoEnabled, m.ContactDebtDay, m.DebtContactReasonId }).FirstOrDefaultAsync(ct)
            ?? new { ContactDebtAutoEnabled = true, ContactDebtDay = DebtContactRules.DefaultDueDay, DebtContactReasonId = (string?)null };
        if (!settings.ContactDebtAutoEnabled || !DebtContactRules.IsOpenSeason(today, settings.ContactDebtDay))
            return new RunResult(0, 0, closed);

        var month = DebtContactRules.MonthKey(today);
        var done = (await db.DebtContactAlerts.AsNoTracking().Where(a => a.Month == month).Select(a => a.StudentId).ToListAsync(ct))
            .ToHashSet();
        var candidates = (await db.Students.AsNoTracking().Where(s => !s.IsArchived && s.Balance < 0)
                .OrderBy(s => s.FullName).ToListAsync(ct))
            .Where(s => !done.Contains(s.Id)).ToList();
        if (candidates.Count == 0) return new RunResult(0, 0, closed);

        // «Qarzdorlar» tabi (MessagingReports.DebtorsAsync) bilan bir xil populyatsiya: yopilgan guruhli o'quvchi yo'q,
        // kurs bo'yicha qarz DebtMessages dan.
        var closedGroups = await MessagingAudience.ClosedGroupStudentIdsAsync(db, ct);
        var groupByName = await DebtMessages.GroupsByNameAsync(db, ct);
        var queue = new ContactQueueService(db, audit);
        var (reasonId, reasonLabel) = await queue.ResolveReasonAsync(settings.DebtContactReasonId);
        if (reasonLabel.Length == 0) reasonLabel = ReasonLabel;

        int requests = 0, notes = 0;
        foreach (var student in candidates)
        {
            if (closedGroups.Contains(student.Id)) continue;
            var items = await DebtMessages.BuildForStudentAsync(db, student, groupByName, ct);
            if (items.Count == 0) continue;
            var text = DebtContactRules.NoteText([.. items.Select(i => (i.Label, i.Amount))]);
            var alert = new DebtContactAlert
            {
                StudentId = student.Id, Month = month, Amount = items.Sum(i => i.Amount), CreatedAt = AppClock.Now,
            };

            var open = await queue.OpenRequestAsync(student.Id);
            if (open is null)
            {
                alert.ContactRequestId = queue.Add(student, reasonId, reasonLabel, text, "", "", ActorName).Id;
                alert.OpenedRequest = true;
                requests++;
            }
            else
            {
                AddSystemNote(db, audit, open, text, today);
                alert.ContactRequestId = open.Id;
                notes++;
            }
            db.DebtContactAlerts.Add(alert);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Parallel yugurish ulgurdi (unikal kalit) — shu o'quvchining yozuvlarini tashlab, davom etamiz.
                DetachAdded(db, student.Id);
                if (alert.OpenedRequest) requests--; else notes--;
            }
        }
        return new RunResult(requests, notes, closed);
    }

    /// <summary>Shu xizmat OCHGAN, hali ochiq talablardan qarzi to'langanlarini yopadi.</summary>
    private static async Task<int> CloseSettledAsync(IAppDbContext db, AuditService audit, CancellationToken ct)
    {
        var pending = await db.DebtContactAlerts
            .Where(a => a.OpenedRequest && a.ResolvedAt == null && a.ContactRequestId != null)
            .ToListAsync(ct);
        if (pending.Count == 0) return 0;

        var requestIds = pending.Select(a => a.ContactRequestId!).ToList();
        var requests = await db.ContactRequests.Where(c => requestIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var studentIds = pending.Select(a => a.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.IsArchived, s.Balance }).ToDictionaryAsync(s => s.Id, ct);

        var queue = new ContactQueueService(db, audit);
        var now = AppClock.Now;
        var closed = 0;
        foreach (var alert in pending)
        {
            if (!requests.TryGetValue(alert.ContactRequestId!, out var request) || !IsOpen(request))
            {
                // Talab o'chirilgan yoki operator o'zi yopgan — kuzatish tugadi (keyingi sikllarda qayta o'qilmasin).
                alert.ResolvedAt = now;
                continue;
            }
            var s = students.GetValueOrDefault(alert.StudentId);
            if (!DebtContactRules.Settled(s is null, s?.IsArchived ?? false, s?.Balance ?? 0)) continue;
            // «Hal bo'ldi» tizim hodisasi bilan — «Hal bo'ldi» sanog'ida «Tizim» (xodimning yutug'i emas).
            queue.CloseBySystem(request, DebtContactRules.PaidText, ActorName);
            alert.ResolvedAt = now;
            closed++;
        }
        await db.SaveChangesAsync(ct);
        return closed;
    }

    private static bool IsOpen(ContactRequest c) => c.Status is ContactStatuses.New or ContactStatuses.Callback;

    private static void AddSystemNote(IAppDbContext db, AuditService audit, ContactRequest open, string text, DateOnly today)
    {
        var stamp = AppClock.Iso();
        db.ContactAttempts.Add(new ContactAttempt
        {
            RequestId = open.Id, StudentId = open.StudentId, Type = ContactAttemptTypes.Note,
            Response = text, ActorId = "", ActorName = ActorName, CreatedAt = stamp, Date = today.ToString("yyyy-MM-dd"),
        });
        open.LastActorName = ActorName;
        open.LastActionAt = stamp;
        audit.Record(ContactQueueService.AuditEntity, open.Id, "update",
            $"Bog'lanish izohi ({open.StudentName}): {text}", studentId: open.StudentId);
    }

    private static void DetachAdded(IAppDbContext db, string studentId)
    {
        foreach (var e in db.DebtContactAlerts.Local.Where(a => a.StudentId == studentId).ToList())
            if (db.DebtContactAlerts.Entry(e).State == EntityState.Added) db.DebtContactAlerts.Entry(e).State = EntityState.Detached;
        foreach (var e in db.ContactRequests.Local.Where(c => c.StudentId == studentId).ToList())
            if (db.ContactRequests.Entry(e).State == EntityState.Added) db.ContactRequests.Entry(e).State = EntityState.Detached;
        foreach (var e in db.ContactAttempts.Local.Where(c => c.StudentId == studentId).ToList())
            if (db.ContactAttempts.Entry(e).State == EntityState.Added) db.ContactAttempts.Entry(e).State = EntityState.Detached;
        foreach (var e in db.AuditLogs.Local.Where(a => a.StudentId == studentId).ToList())
            if (db.AuditLogs.Entry(e).State == EntityState.Added) db.AuditLogs.Entry(e).State = EntityState.Detached;
    }

    /// <summary>
    /// Startup seed (bir martalik, <see cref="SeedMarker"/> <c>debt-contact-reason-2026-10</c>): «Bog'lanish kerak»
    /// kategoriyasida «To'lov qarzdorligi» sababi (yo'q bo'lsa) va uning id'si <see cref="CenterMeta.DebtContactReasonId"/> ga.
    /// </summary>
    public const string SeedKey = "debt-contact-reason-2026-10";

    public static async Task<bool> SeedReasonAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (await db.SeedMarkers.AnyAsync(m => m.Key == SeedKey, ct)) return false;
        var reason = await db.ActionReasons.FirstOrDefaultAsync(
            r => r.Category == ContactService.ReasonCategory && r.Label == ReasonLabel, ct);
        if (reason is null)
        {
            var order = await db.ActionReasons.Where(r => r.Category == ContactService.ReasonCategory)
                .Select(r => (int?)r.Order).MaxAsync(ct) ?? 0;
            reason = new ActionReason { Category = ContactService.ReasonCategory, Label = ReasonLabel, Order = order + 1 };
            db.ActionReasons.Add(reason);
        }
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null)
        {
            meta = new CenterMeta();
            db.CenterMeta.Add(meta);
        }
        meta.DebtContactReasonId = reason.Id;
        db.SeedMarkers.Add(new SeedMarker { Key = SeedKey, AppliedAt = AppClock.Iso() });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
