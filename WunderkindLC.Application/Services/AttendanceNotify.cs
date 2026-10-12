using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// DAVOMAT AVTO-XABARLARI — "darsga kelmadi" (<c>attendance_absent</c>) va ommaviy "keldi"
/// (<c>attendance_present</c>) qachon va KIMGA yuborilishining yagona qoidasi. Admin jurnali
/// (<c>JournalController</c>) ham, o'qituvchi ilovasi (<c>TeacherPortalController</c>) ham shu
/// yerdan foydalanadi — ikki controllerda ikki xil qoida bo'lmasin.
///
/// <para><b>"Kelmadi" nima</b> (<see cref="IsNewAbsence"/>): yangi sabab qo'yildi VA u "kech"
/// (<see cref="Domain.AbsenceReason.IsLate"/>) EMAS VA oldingi holat "kelmadi" EMAS edi (sabab
/// yo'q yoki "kech"). Ya'ni:</para>
/// <list type="bullet">
/// <item>"Kech" — o'quvchi DARSDA BO'LGAN: ota-onaga "darsga kelmadi" HECH QACHON ketmaydi;</item>
/// <item>sababni almashtirish (Sababsiz → Kasal) — yangi hodisa EMAS, ikkinchi SMS ketmaydi;</item>
/// <item>"kech" → "sababsiz" — haqiqiy o'zgarish (endi kelmadi), xabar ketadi.</item>
/// </list>
///
/// <para><b>Ommaviy yuborish FONDA va BAZADAGI NAVBAT orqali</b> (<see cref="EnqueueBulkAsync"/> →
/// <see cref="AttendanceNotifyWorker"/>): 25 o'quvchiga SMS bittalab ketadi (Local SMS'da har biri
/// orasida kutish), so'rov Cloudflare'ning 100 soniyasidan oshib ketar, o'qituvchi "xato" ko'rib qayta
/// bosar va hamma ota-ona xabarni IKKI marta olardi. Shuning uchun so'rov darhol qaytadi.
/// ⚠️ Ilgari fon ishi xotirada edi (<c>Task.Run</c>) — yuborish paytida deploy bo'lsa qolgan ota-onalar
/// xabarni JIMGINA olmasdi. Endi oluvchilar (<see cref="AttendanceNotifyItem"/>) jurnal yozuvi bilan
/// BITTA SaveChanges da yoziladi va ishchi ilova qayta ishga tushganda ham qolganini yuboradi.</para>
/// </summary>
public static class AttendanceNotify
{
    /// <summary>
    /// Shu yozuv YANGI "kelmadi" hodisasimi (sof funksiya, testlangan).
    /// <paramref name="lateIds"/> — "kech" turidagi sabablar id'lari.
    /// </summary>
    public static bool IsNewAbsence(string? oldReasonId, string? newReasonId, IReadOnlySet<string> lateIds)
    {
        if (string.IsNullOrEmpty(newReasonId) || lateIds.Contains(newReasonId)) return false;
        return string.IsNullOrEmpty(oldReasonId) || lateIds.Contains(oldReasonId);
    }

    /// <summary>"Kech" turidagi davomat sabablari id'lari.</summary>
    public static async Task<HashSet<string>> LateReasonIdsAsync(IAppDbContext db, CancellationToken ct = default) =>
        (await db.AbsenceReasons.AsNoTracking().Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// "Darsga kelmadi" xabarini berilgan o'quvchilarga yuboradi (guruh + sabab bilan).
    /// Arxivlangan o'quvchi CHETLAB o'tiladi (eski arxivdagilarda a'zolik hali tirik bo'lishi mumkin).
    /// Chaqiruvchi ro'yxatni ALLAQACHON "yangi kelmadi"larga toraytirgan bo'lishi kerak.
    /// <para>⚠️ CRM U3b dan beri ISHLATILMAYDI: u xabarni DARHOL yuboradi — dars tugashini, menejer nazoratini va
    /// "keldi"ga tuzatishni chetlab o'tardi. Yangi kod <see cref="EnqueueSingleAsync"/> / <see cref="EnqueueBulkAsync"/>.</para>
    /// </summary>
    [Obsolete("CRM U3b: «kelmadi» goes through the queue (EnqueueSingleAsync / EnqueueBulkAsync) — this sends immediately.")]
    public static async Task DispatchAbsencesAsync(
        IAppDbContext db, AutoMessageService autoMsg, string classId, string date, string? reasonId,
        IEnumerable<string> studentIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasonId)) return;
        var ids = studentIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var cls = await db.Classes.FindAsync(new object[] { classId }, ct);
        var groupName = cls?.Name ?? "";
        var reasonName = (await db.AbsenceReasons.FindAsync(new object[] { reasonId }, ct))?.Name ?? "Sababsiz";
        foreach (var sid in ids)
        {
            var s = await db.Students.FindAsync(new object[] { sid }, ct);
            if (s is null || s.IsArchived) continue;
            await autoMsg.DispatchAttendanceAbsentAsync(db, s, groupName, reasonName, date, cls, ct);
        }
    }

    /// <summary>Berilganlardan ARXIVLANMAGAN o'quvchilar (avto-xabar arxivdagiga ketmasin).</summary>
    public static async Task<List<string>> NotArchivedAsync(IAppDbContext db, IReadOnlyCollection<string> studentIds)
    {
        if (studentIds.Count == 0) return [];
        var ids = studentIds.ToList();
        var archived = (await db.Students.AsNoTracking()
                .Where(s => s.IsArchived && ids.Contains(s.Id)).Select(s => s.Id).ToListAsync())
            .ToHashSet();
        return ids.Where(id => !archived.Contains(id)).ToList();
    }

    public const string KindAbsent = "absent";
    public const string KindPresent = "present";

    /* =====================================================================================
     *  CRM U3b (SPEC P3.3) — «kelmadi» navbati: dars TUGAGACH + kechikish, menejer nazorati
     * ===================================================================================== */

    /// <summary>Lesson context of one group's day used to schedule its notices.</summary>
    /// <param name="Start">First lesson start that day ("HH:mm", moved lessons at their new time); "" — unknown.</param>
    /// <param name="End">Its end; "" — unknown.</param>
    /// <param name="TakenAt">First attendance mark of the group's day (<see cref="AttendanceTaking.FirstMarksAsync"/>).</param>
    public sealed record LessonContext(string Start, string End, DateTime? TakenAt, AttendanceSettingsDto Settings);

    /// <summary>
    /// Lesson times of <paramref name="classId"/> on <paramref name="date"/> — <see cref="AttendanceWatch.LessonsOn"/>
    /// (moved lessons at their new day/time, group lifetime), FIRST lesson of the day. No planned lesson that day
    /// (an extra lesson) → the group's regular times. Plus the first attendance mark and the centre's settings.
    /// </summary>
    public static async Task<LessonContext> LessonContextAsync(
        IAppDbContext db, string classId, string date, CancellationToken ct = default)
    {
        var settings = AttendanceSettingsRules.FromMeta(await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct));
        var group = await db.Classes.AsNoTracking().FirstOrDefaultAsync(g => g.Id == classId, ct);
        string start = "", end = "";
        if (group is not null)
        {
            start = group.StartTime ?? "";
            end = group.EndTime ?? "";
            if (DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var day))
            {
                var moves = (await db.LessonReschedules.AsNoTracking()
                        .Where(r => r.ClassId == classId && (r.FromDate == date || r.ToDate == date))
                        .ToListAsync(ct))
                    .ToLookup(r => r.ClassId, StringComparer.Ordinal);
                var none = Array.Empty<SubstituteTeacherAssignment>().ToLookup(a => a.GroupId, StringComparer.Ordinal);
                var first = AttendanceWatch.LessonsOn([group], moves, none, day)
                    .OrderBy(l => ScheduleRules.ParseTime(l.StartTime) ?? int.MaxValue)
                    .FirstOrDefault();
                if (first is not null)
                {
                    start = first.StartTime;
                    end = first.EndTime;
                }
            }
        }
        var marks = await AttendanceTaking.FirstMarksAsync(db, date, [classId], ct);
        return new LessonContext(start, end, marks.TryGetValue(classId, out var t) ? t : null, settings);
    }

    /// <summary>One queue row with its schedule (SendAfter, status) — the ONE place both paths build rows.</summary>
    private static AttendanceNotifyItem BuildItem(
        string kind, string classId, string subjectId, string date, int period, string studentId, string? reasonId,
        LessonContext ctx, DateTime now)
    {
        // The mark being saved right now is the first one when the day had none yet.
        var takenLate = AbsentNoticeRules.TakenLate(date, ctx.Start, ctx.TakenAt ?? now, ctx.Settings.DeadlineMinutes);
        var sendAfter = kind == KindPresent
            ? now
            : AbsentNoticeRules.SendAfter(date, ctx.End, ctx.Start, now, ctx.Settings.AbsentNoticeDelayMinutes);
        return new AttendanceNotifyItem
        {
            Kind = kind, ClassId = classId, SubjectId = subjectId, Date = date, Period = period,
            ReasonId = reasonId, StudentId = studentId,
            Status = AbsentNoticeRules.InitialStatus(kind, ctx.Settings.AbsentNoticeApproval, takenLate),
            SendAfter = sendAfter, LessonStart = ctx.Start, LessonEnd = ctx.End,
            TakenLate = kind == KindAbsent && takenLate,
            CreatedAt = now, UpdatedAt = now,
        };
    }

    /// <summary>Only TODAY's lessons message parents — corrections of past days never do (SPEC P3.3).</summary>
    public static bool IsToday(string date, DateTime now) => date == now.ToString("yyyy-MM-dd");

    /// <summary>
    /// OMMAVIY davomat xabarlarini NAVBATGA yozadi (<see cref="AttendanceNotifyItem"/>, har oluvchi —
    /// bitta qator). <see cref="JournalService.BulkAttendanceAsync"/> ning <c>beforeSave</c> chaqiruvidan
    /// ishlatiladi — ya'ni jurnal yozuvi bilan BITTA SaveChanges da saqlanadi. SaveChanges QILMAYDI.
    /// <list type="bullet">
    /// <item>"kelmadi" — faqat shu so'rovda YANGI kelmaganlarga (<c>NewlyAbsentIds</c>), dars TUGAGACH +
    /// kechikish (<see cref="AbsentNoticeRules.SendAfter"/>); kech olingan davomatda — tasdiq kutadi;</item>
    /// <item>"keldi" — yozilganlardan saqlashdan OLDIN allaqachon «keldi» bo'lmaganlarga, DARHOL
    /// (<paramref name="wasPresent"/>).</item>
    /// </list>
    /// Faqat BUGUNGI dars (o'tgan kunni tuzatish ota-onaga xabar bermaydi). Shu hodisaga yoqilgan qoida
    /// bo'lmasa qator YOZILMAYDI. Arxiv va jurnal holati YUBORISH paytida qayta tekshiriladi.
    /// </summary>
    public static async Task<int> EnqueueBulkAsync(
        IAppDbContext db, BulkAttendanceRequest req, JournalService.BulkAttendanceResult result,
        IReadOnlySet<string> wasPresent, CancellationToken ct = default, DateTime? now = null)
    {
        var at = now ?? AppClock.Now;
        if (!IsToday(req.Date, at)) return 0;
        var absent = result.AbsentReasonId is not null && result.NewlyAbsentIds.Count > 0
            && await RuleEnabledAsync(db, AutoMessageTriggers.AttendanceAbsent, ct);
        var present = !req.Absent && result.WrittenIds.Count > 0
            && await RuleEnabledAsync(db, AutoMessageTriggers.AttendancePresent, ct);
        if (!absent && !present) return 0;

        var ctx = await LessonContextAsync(db, req.ClassId, req.Date, ct);
        var items = new List<AttendanceNotifyItem>();
        if (absent)
            items.AddRange(result.NewlyAbsentIds.Distinct().Select(id =>
                BuildItem(KindAbsent, req.ClassId, req.SubjectId, req.Date, req.Period, id, result.AbsentReasonId, ctx, at)));
        if (present)
            items.AddRange(result.WrittenIds.Where(id => !wasPresent.Contains(id)).Distinct().Select(id =>
                BuildItem(KindPresent, req.ClassId, req.SubjectId, req.Date, req.Period, id, null, ctx, at)));
        if (items.Count > 0) db.AttendanceNotifyItems.AddRange(items);
        return items.Count;
    }

    /// <summary>
    /// BITTA katak (admin jurnali va o'qituvchi ilovasi) — ilgari xabar so'rov ichida darhol ketardi; endi u ham
    /// navbatga tushadi (SPEC P3.3: "ALL attendance messages go through the queue"). <paramref name="newAbsence"/> —
    /// <see cref="JournalService.SetEntryAsync"/> natijasi; <paramref name="wasPresent"/> — saqlashdan OLDIN «keldi»
    /// bo'lganlar. Faqat bugungi dars, faqat yoqilgan qoida, arxivdagiga yo'q. SaveChanges QILMAYDI.
    /// </summary>
    public static async Task<int> EnqueueSingleAsync(
        IAppDbContext db, SetJournalEntryRequest req, bool newAbsence, IReadOnlySet<string> wasPresent,
        CancellationToken ct = default, DateTime? now = null)
    {
        var at = now ?? AppClock.Now;
        if (!IsToday(req.Date, at)) return 0;
        var absent = newAbsence && !string.IsNullOrEmpty(req.ReasonId)
            && await RuleEnabledAsync(db, AutoMessageTriggers.AttendanceAbsent, ct);
        var present = req.Present && req.ReasonId is null && !wasPresent.Contains(req.StudentId)
            && await RuleEnabledAsync(db, AutoMessageTriggers.AttendancePresent, ct);
        if (!absent && !present) return 0;
        if (await db.Students.AsNoTracking().AnyAsync(s => s.Id == req.StudentId && s.IsArchived, ct)) return 0;

        var ctx = await LessonContextAsync(db, req.ClassId, req.Date, ct);
        db.AttendanceNotifyItems.Add(absent
            ? BuildItem(KindAbsent, req.ClassId, req.SubjectId, req.Date, req.Period, req.StudentId, req.ReasonId, ctx, at)
            : BuildItem(KindPresent, req.ClassId, req.SubjectId, req.Date, req.Period, req.StudentId, null, ctx, at));
        return 1;
    }

    private static Task<bool> RuleEnabledAsync(IAppDbContext db, string trigger, CancellationToken ct) =>
        db.AutoMessageRules.AnyAsync(r => r.Enabled && r.Trigger == trigger, ct);

    /// <summary>
    /// OCHIQ xabarlarni JURNAL bilan solishtiradi (har yozuvdan keyin va ishchining har aylanishida): muddati
    /// o'tgani — <c>expired</c>; o'quvchi arxivlangan — <c>cancelled</c> (<c>archived</c>); jurnal endi buni
    /// aytmaydi ("kelmadi" → «keldi»/«kech»/tozalandi, yoki «keldi» → sabab) — <c>cancelled</c> (<c>changed</c>).
    /// Saqlaydi; parallel o'zgarish (menejer, ishchi) bo'lsa — jim, keyingi aylanishda qayta. Qaytaradi: o'zgargan
    /// qatorlar soni.
    /// </summary>
    /// <param name="classId">null — barcha ochiq xabarlar (ishchi); aks holda shu guruh (yozish yo'li).</param>
    public static async Task<int> ReconcileAsync(
        IAppDbContext db, DateTime now, string? classId = null, string? date = null,
        IReadOnlyCollection<string>? studentIds = null, CancellationToken ct = default)
    {
        var open = AbsentNoticeRules.OpenStatuses.ToList();
        var q = db.AttendanceNotifyItems.Where(i => open.Contains(i.Status));
        if (classId is not null) q = q.Where(i => i.ClassId == classId);
        if (date is not null) q = q.Where(i => i.Date == date);
        if (studentIds is { Count: > 0 })
        {
            var only = studentIds.ToList();
            q = q.Where(i => only.Contains(i.StudentId));
        }
        var items = await q.ToListAsync(ct);
        if (items.Count == 0) return 0;

        var sids = items.Select(i => i.StudentId).Distinct().ToList();
        var archived = (await db.Students.AsNoTracking()
                .Where(s => sids.Contains(s.Id) && s.IsArchived).Select(s => s.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var classIds = items.Select(i => i.ClassId).Distinct().ToList();
        var dates = items.Select(i => i.Date).Distinct().ToList();
        var entries = (await db.JournalEntries.AsNoTracking()
                .Where(e => classIds.Contains(e.ClassId) && dates.Contains(e.Date) && sids.Contains(e.StudentId))
                .ToListAsync(ct))
            .ToLookup(e => (e.ClassId, e.SubjectId, e.Date, e.Period, e.StudentId));
        var lateIds = await LateReasonIdsAsync(db, ct);

        var changed = 0;
        foreach (var i in items)
        {
            string? status = null, reason = null;
            if (AbsentNoticeRules.IsExpired(i.Status, i.SendAfter, now)) status = AbsentNoticeRules.Expired;
            else if (archived.Contains(i.StudentId))
                (status, reason) = (AbsentNoticeRules.Cancelled, AbsentNoticeRules.CancelArchived);
            else if (!AbsentNoticeRules.JournalStillValid(
                         i.Kind, entries[(i.ClassId, i.SubjectId, i.Date, i.Period, i.StudentId)], lateIds))
                (status, reason) = (AbsentNoticeRules.Cancelled, AbsentNoticeRules.CancelChanged);
            if (status is null) continue;
            i.Status = status;
            if (reason is not null) i.CancelReason = reason;
            i.UpdatedAt = now;
            changed++;
        }
        if (changed == 0) return 0;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // Menejer yoki ishchi shu orada o'zgartirdi — qatorlarimiz kontekstdan chiqariladi, keyingi aylanishda qayta.
            foreach (var i in items) db.AttendanceNotifyItems.Entry(i).State = EntityState.Detached;
            return 0;
        }
        return changed;
    }

    /// <summary>
    /// Navbatdagi BITTA oluvchiga xabarni yuboradi (<see cref="AttendanceNotifyWorker"/> chaqiradi, holat allaqachon
    /// tekshirilgan). Outbox hodisa kaliti va manbasi — qator id'si (<c>eventKey = sourceId = item.Id</c>): bir xabar
    /// ikki marta navbatga tushmaydi, nazorat sahifasi yetkazish holatini shu id bo'yicha o'qiydi. "Kelmadi" matnidagi
    /// sabab — jurnaldagi JORIY sabab (Sababsiz → Kasal almashgan bo'lsa — Kasal). Arxivdagiga hech narsa ketmaydi.
    /// </summary>
    public static async Task DispatchItemAsync(
        IAppDbContext db, AutoMessageService autoMsg, AttendanceNotifyItem item, CancellationToken ct = default)
    {
        var s = await db.Students.FindAsync(new object[] { item.StudentId }, ct);
        if (s is null || s.IsArchived) return;
        var cls = await db.Classes.FindAsync(new object[] { item.ClassId }, ct);
        var groupName = cls?.Name ?? "";
        if (item.Kind == KindAbsent)
        {
            var current = await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == item.ClassId && e.SubjectId == item.SubjectId && e.Date == item.Date
                            && e.Period == item.Period && e.StudentId == item.StudentId && e.ReasonId != null)
                .Select(e => e.ReasonId)
                .FirstOrDefaultAsync(ct);
            var reasonId = current ?? item.ReasonId;
            var reasonName = reasonId is null ? null
                : (await db.AbsenceReasons.FindAsync(new object[] { reasonId }, ct))?.Name;
            await autoMsg.DispatchAttendanceAbsentAsync(db, s, groupName, reasonName ?? "Sababsiz", item.Date, cls, ct,
                eventKey: item.Id, sourceId: item.Id);
        }
        else if (item.Kind == KindPresent)
        {
            var d = item.Date;
            var sana = d.Length >= 10 ? $"{d[8..10]}.{d[5..7]}.{d[..4]}" : d;
            await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.AttendancePresent, s, new Dictionary<string, string>
            {
                ["{sana}"] = sana,
                ["{guruh}"] = groupName,
            }, group: cls, ct: ct, eventKey: item.Id, sourceId: item.Id);
        }
    }

    /// <summary>Bildirishnoma turi (kabinet qo'ng'iroqchasi) — menejerlarga «tasdiq kutmoqda».</summary>
    public const string AlertNotificationType = "attendance_notice";

    /// <summary>
    /// «TASDIQ KUTMOQDA» — bugungi har dars (guruh, kun) uchun menejerlarga BIR MARTA (<see cref="LessonReminderMark"/>,
    /// kalit <see cref="AbsentNoticeRules.AlertKey"/> — restartda ham takrorlanmaydi): qo'ng'iroqcha + Telegram (xodimga
    /// ishchi xabar — outbox emas, SPEC §0.4). Oluvchilar — <see cref="NoticeRecipientsAsync"/>.
    /// <paramref name="send"/> = null — Telegram sozlanmagan (faqat qo'ng'iroqcha). Qaytaradi: ogohlantirilgan darslar.
    /// </summary>
    public static async Task<int> AlertAwaitingAsync(
        IAppDbContext db, Func<long, string, CancellationToken, Task>? send, string baseUrl, DateTime now,
        CancellationToken ct = default)
    {
        var today = now.ToString("yyyy-MM-dd");
        var waiting = await db.AttendanceNotifyItems.AsNoTracking()
            .Where(i => i.Status == AbsentNoticeRules.Awaiting && i.Date == today)
            .Select(i => new { i.ClassId, i.TakenLate })
            .ToListAsync(ct);
        if (waiting.Count == 0) return 0;

        var alerted = 0;
        List<string>? recipients = null;
        List<long>? chats = null;
        foreach (var g in waiting.GroupBy(i => i.ClassId, StringComparer.Ordinal))
        {
            if (!await LessonAttendanceReminderService.TryMarkAsync(db, AbsentNoticeRules.AlertKey(g.Key, today), today, ct))
                continue;
            recipients ??= await NoticeRecipientsAsync(db, ct);
            var groupName = await db.Classes.AsNoTracking().Where(c => c.Id == g.Key).Select(c => c.Name)
                .FirstOrDefaultAsync(ct) ?? "";
            var marks = await AttendanceTaking.FirstMarksAsync(db, today, [g.Key], ct);
            var text = AbsentNoticeRules.AlertText(groupName, today, g.Count(), g.Any(i => i.TakenLate),
                marks.TryGetValue(g.Key, out var t) ? t : null, baseUrl);
            foreach (var id in recipients) NotificationStore.Add(db, id, "Davomat xabarlari", text, AlertNotificationType);
            await db.SaveChangesAsync(ct);
            alerted++;
            if (send is null || recipients.Count == 0) continue;
            chats ??= await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.UserId != null && recipients.Contains(r.UserId) && r.BlockedAt == null)
                .Select(r => r.ChatId).Distinct().ToListAsync(ct);
            foreach (var chat in chats) await send(chat, text, ct);
        }
        return alerted;
    }

    /// <summary>Menejerlar: superadmin, admin va «Davomat xabarlari nazorati» (<c>students.notices</c>) ruxsatli xodim.</summary>
    public static async Task<List<string>> NoticeRecipientsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.SuperAdmin || u.Role == Roles.Admin || u.Role == Roles.Staff)
            .Select(u => new { u.Id, u.Role, u.Permissions })
            .ToListAsync(ct);
        return users
            .Where(u => u.Role != Roles.Staff || PermissionRules.HasSection(u.Permissions, "students.notices"))
            .Select(u => u.Id)
            .ToList();
    }
}
