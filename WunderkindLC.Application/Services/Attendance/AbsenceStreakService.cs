using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KETMA-KET KELMASLIK → «BOG'LANISH KERAK» (SPEC P3.5). Startupdan 2 daqiqa keyin bir marta, so'ng har 30 daqiqada
/// (08:00–22:00): <see cref="AbsenceStreakLoader"/> seriyalaridan sozlangan (n, «Sozlamalar → Servis») va 5 darajali (<see cref="AbsenceStreaks.ContactLevels"/>) <see cref="AbsenceStreakAlert"/>
/// qatorlari (unikal kalit — qayta yugurish takror bermaydi). YANGI qator paydo bo'lganda o'quvchiga (yugurish
/// boshiga ko'pi bilan BITTA amal, faqat oxirgi yo'qlik ≤ 14 kun oldin bo'lsa):
/// <list type="bullet">
///   <item>ochiq talab yo'q → yangi talab («Ketma-ket darsga kelmadi», <see cref="ContactQueueService.Add"/>);</item>
///   <item>ochiq talab bor → unga IZOH (<c>note</c> — hisobotdagi urinish/bog'lanildi sonlarini o'zgartirmaydi).</item>
/// </list>
/// Hech qachon ikkinchi ochiq talab ochilmaydi (<c>contacts.md</c> §4).
/// </summary>
public sealed class AbsenceStreakService(IServiceProvider services, ILogger<AbsenceStreakService> logger) : BackgroundService
{
    public const string ReasonLabel = "Ketma-ket darsga kelmadi";
    public const string ActorName = "Tizim";

    public sealed record RunResult(int Alerts, int Requests, int Notes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
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
                    if (r.Alerts + r.Requests + r.Notes > 0)
                        logger.LogInformation("Ketma-ket kelmaslik: {A} ogohlantirish, {R} talab, {N} izoh",
                            r.Alerts, r.Requests, r.Notes);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ketma-ket kelmaslik siklida xatolik");
                }
            }
            first = false;
            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    public static async Task<RunResult> RunOnceAsync(
        IAppDbContext db, AuditService audit, DateOnly today, CancellationToken ct = default)
    {
        // «Sozlamalar → Servis»: o'chirilgan bo'lsa hech narsa qilinmaydi; chegara — necha dars.
        // Qator yo'q — standart (yoqilgan, 3 dars), «Servis» sahifasi ko'rsatadigan qiymat bilan bir xil.
        var settings = await db.CenterMeta.AsNoTracking()
            .Select(m => new { m.ContactAbsenceAutoEnabled, m.ContactAbsenceLessons, m.AbsenceStreakReasonId }).FirstOrDefaultAsync(ct)
            ?? new { ContactAbsenceAutoEnabled = true, ContactAbsenceLessons = AbsenceStreaks.DefaultContactLessons, AbsenceStreakReasonId = (string?)null };
        if (!settings.ContactAbsenceAutoEnabled) return new RunResult(0, 0, 0);
        var start = AbsenceStreaks.ClampContactLessons(settings.ContactAbsenceLessons);

        var rows = (await AbsenceStreakLoader.ComputeAsync(db, today, ct))
            .Where(r => r.Streak.Count >= start)
            .ToList();
        if (rows.Count == 0) return new RunResult(0, 0, 0);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        // Seriya bo'yicha allaqachon ko'tarilgan ENG YUQORI daraja (eski 3-daraja qatorlari ham) — takror yo'q.
        var raised = (await db.AbsenceStreakAlerts.AsNoTracking()
                .Where(a => studentIds.Contains(a.StudentId))
                .Select(a => new { a.StudentId, a.GroupId, a.StreakStart, a.Level })
                .ToListAsync(ct))
            .GroupBy(a => (a.StudentId, a.GroupId, a.StreakStart))
            .ToDictionary(g => g.Key, g => g.Max(a => a.Level));

        // Yangi qatorlar — o'quvchi bo'yicha.
        var fresh = new Dictionary<string, List<(AbsenceStreakLoader.Row Row, AbsenceStreakAlert Alert)>>(StringComparer.Ordinal);
        var now = AppClock.Now;
        foreach (var r in rows)
        {
            var maxRaised = raised.GetValueOrDefault((r.StudentId, r.GroupId, r.Streak.StartDate));
            foreach (var level in AbsenceStreaks.ContactLevelsToRaise(r.Streak.Count, maxRaised, start))
            {
                var alert = new AbsenceStreakAlert
                {
                    StudentId = r.StudentId, GroupId = r.GroupId, StreakStart = r.Streak.StartDate,
                    Level = level, CreatedAt = now,
                };
                (fresh.TryGetValue(r.StudentId, out var list) ? list : fresh[r.StudentId] = []).Add((r, alert));
            }
        }
        if (fresh.Count == 0) return new RunResult(0, 0, 0);

        var queue = new ContactQueueService(db, audit);
        var (reasonId, _) = await queue.ResolveReasonAsync(settings.AbsenceStreakReasonId);

        int alerts = 0, requests = 0, notes = 0;
        foreach (var (studentId, items) in fresh)
        {
            foreach (var (_, alert) in items) db.AbsenceStreakAlerts.Add(alert);

            // BITTA amal: eng uzun seriya, so'ng eng yangi yo'qlik.
            var pick = items.Select(x => x.Row)
                .OrderByDescending(r => r.Streak.Count).ThenByDescending(r => r.Streak.LastDate, StringComparer.Ordinal)
                .First();
            if (AbsenceStreaks.IsFresh(pick.Streak, today))
            {
                var text = AbsenceStreaks.NoteText(pick.GroupName, pick.Streak);
                var open = await queue.OpenRequestAsync(studentId);
                string? requestId = null;
                if (open is null)
                {
                    var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId && !s.IsArchived, ct);
                    if (student is not null)
                    {
                        requestId = queue.Add(student, reasonId, ReasonLabel, text, "", "", ActorName).Id;
                        requests++;
                    }
                }
                else
                {
                    var stamp = AppClock.Iso();
                    db.ContactAttempts.Add(new ContactAttempt
                    {
                        RequestId = open.Id, StudentId = open.StudentId, Type = ContactAttemptTypes.Note,
                        Response = text, ActorId = "", ActorName = ActorName, CreatedAt = stamp,
                        Date = today.ToString("yyyy-MM-dd"),
                    });
                    open.LastActorName = ActorName;
                    open.LastActionAt = stamp;
                    audit.Record(ContactQueueService.AuditEntity, open.Id, "update",
                        $"Bog'lanish izohi ({open.StudentName}): {text}", studentId: open.StudentId);
                    requestId = open.Id;
                    notes++;
                }
                foreach (var (_, alert) in items) alert.ContactRequestId = requestId;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                alerts += items.Count;
            }
            catch (DbUpdateException)
            {
                // Parallel yugurish ulgurdi (unikal kalit) — bu o'quvchining yozuvlarini tashlab, davom etamiz.
                foreach (var (_, alert) in items) db.AbsenceStreakAlerts.Entry(alert).State = EntityState.Detached;
                foreach (var e in db.ContactRequests.Local.Where(c => c.StudentId == studentId).ToList())
                    if (db.ContactRequests.Entry(e).State == EntityState.Added)
                        db.ContactRequests.Entry(e).State = EntityState.Detached;
                foreach (var e in db.ContactAttempts.Local.Where(c => c.StudentId == studentId).ToList())
                    if (db.ContactAttempts.Entry(e).State == EntityState.Added)
                        db.ContactAttempts.Entry(e).State = EntityState.Detached;
                foreach (var e in db.AuditLogs.Local.Where(a => a.StudentId == studentId).ToList())
                    if (db.AuditLogs.Entry(e).State == EntityState.Added)
                        db.AuditLogs.Entry(e).State = EntityState.Detached;
            }
        }
        return new RunResult(alerts, requests, notes);
    }

    /// <summary>
    /// Startup seed (bir martalik, <see cref="SeedMarker"/> <c>streak-reason-2026-10</c>): «Bog'lanish kerak»
    /// kategoriyasida «Ketma-ket darsga kelmadi» sababi (yo'q bo'lsa) va uning id'si
    /// <see cref="CenterMeta.AbsenceStreakReasonId"/> ga.
    /// </summary>
    public const string SeedKey = "streak-reason-2026-10";

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
        meta.AbsenceStreakReasonId = reason.Id;
        db.SeedMarkers.Add(new SeedMarker { Key = SeedKey, AppliedAt = AppClock.Iso() });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
