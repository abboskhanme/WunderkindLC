using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'qituvchiga "davomat kiriting" eslatmasi (fon xizmati). Qoida <see cref="WunderkindLC.Domain.AutoMessageRule.SendScope"/>
/// bo'yicha 3 rejimda ishlaydi:
/// <list type="bullet">
/// <item>"lesson_start" (default) — har faol guruh uchun, dars boshlanish vaqtidan (<see cref="Group.StartTime"/>)
/// qoida bo'yicha belgilangan daqiqa (odatda 5) o'tgach, agar shu kunga hali davomat kiritilmagan bo'lsa
/// (<see cref="LessonNote.Conducted"/>), guruh o'qituvchisiga yuboriladi.</item>
/// <item>"not_filled" — kunlik <see cref="WunderkindLC.Domain.AutoMessageRule.ScheduleTime"/>da: bugun darsi bo'lib (boshlangan)
/// davomatini HALI kiritmagan o'qituvchilarga, har to'ldirilmagan guruh uchun alohida.</item>
/// <item>"all" — kunlik <see cref="WunderkindLC.Domain.AutoMessageRule.ScheduleTime"/>da: BARCHA faol o'qituvchilarga
/// (davomatni to'ldirganlarga ham).</item>
/// </list>
/// Kanal: kabinet bildirishnomasi (har doim) + Telegram (<see cref="AutoMessageChannels"/>). Andoza/vaqt "Sozlamalar → Eslatmalar"da boshqariladi;
/// bir nechta qoida bo'lishi mumkin. Dars vaqti aniq daqiqa talab qilgani uchun sikl HAR 1 DAQIQADA uyg'onadi.
///
/// <para><b>Kunning darslari</b> — o'qituvchi jadvali va «Darslar monitoringi» bilan BITTA manba
/// (<see cref="AttendanceWatch.LessonsOn"/> → <see cref="TeacherHomeService.PlanLessons"/>): KO'CHIRILGAN darslar
/// (asl kunida eslatma yo'q, yangi kunida — yangi VAQTIDA), guruh umri (<c>StartDate</c>/<c>EndDate</c>/arxiv),
/// arxivlangan/BLOKLANGAN guruh yo'q (<see cref="TeacherGroupAccess"/>). Eslatma shu kuni darsni
/// O'TADIGAN odamga ketadi: o'rinbosar biriktirilgan bo'lsa — O'RINBOSARGA (asosiy o'qituvchi kasal/ta'tilda).
/// ⚠️ Ilgari faqat hafta kuniga qaralar va har doim <c>Group.TeacherId</c> ga yuborilardi.</para>
///
/// <para><b>Bir marta</b> — belgi BAZADA (<see cref="LessonReminderMark"/>, kalit
/// <c>(qoida, guruh|daily, sana)</c> unikal), yuborishdan OLDIN yoziladi. Ilgari belgi xotirada edi va
/// deploy/qayta yuklash 10 daqiqalik oyna ichida tushsa eslatma YANA ketardi ("all" rejimida — hammaga).</para>
/// </summary>
public class LessonAttendanceReminderService(
    IServiceProvider services,
    TelegramService telegram,
    ILogger<LessonAttendanceReminderService> logger) : BackgroundService
{
    /// <summary>Belgilar shuncha kundan keyin o'chiriladi (faqat takrordan himoya uchun kerak).</summary>
    private const int MarkKeepDays = 7;
    private DateOnly _purgedDate = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Davomat eslatmasi siklida xatolik");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var today = AppClock.Today;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        if (_purgedDate != today)
        {
            _purgedDate = today;
            var old = today.AddDays(-MarkKeepDays).ToString("yyyy-MM-dd");
            db.LessonReminderMarks.RemoveRange(db.LessonReminderMarks.Where(m => string.Compare(m.Date, old) < 0));
            await db.SaveChangesAsync(ct);
        }

        var rules = await db.AutoMessageRules
            .Where(r => r.Enabled && r.Trigger == AutoMessageTriggers.LessonAttendance)
            .ToListAsync(ct);
        if (rules.Count == 0) return;

        var lessons = await LessonsTodayAsync(db, today, ct);

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var todayStr = today.ToString("yyyy-MM-dd");
        var now = AppClock.Now;

        foreach (var rule in rules)
        {
            if (rule.SendScope is ReminderSendScopes.All or ReminderSendScopes.NotFilled)
            {
                // Kunlik rejim: belgilangan vaqtda bir marta.
                if (!TimeSpan.TryParse(rule.ScheduleTime, out var at)) continue;
                var passed = now.TimeOfDay - at;
                // 10 daqiqalik oyna — servis biroz kechiksa ham eslatma o'tkazib yuborilmasin.
                if (passed < TimeSpan.Zero || passed > TimeSpan.FromMinutes(10)) continue;
                if (!await TryMarkAsync(db, $"{rule.Id}:daily:{todayStr}", todayStr, ct)) continue;

                try
                {
                    await SendDailyAsync(db, rule, lessons, todayStr, now, meta?.Name ?? "", ct);
                    // Yetkazish yozuvlari (kabinet tarixi) SHU YERDA saqlanadi — keyingi belgi
                    // ularning xatosi bilan yiqilmasin (TryMarkAsync izohi).
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Davomat eslatmasi (kunlik): qoida {Id} uchun xatolik", rule.Id);
                }
                continue;
            }

            // "lesson_start" (default): har dars boshlangach +N daqiqada (N manfiy — darsdan OLDIN).
            // Vaqt — darsning O'Z vaqti (ko'chirilgan darsda yangi vaqt).
            foreach (var l in lessons)
            {
                if (!TimeSpan.TryParse(l.StartTime, out var start)) continue;
                var target = start.Add(TimeSpan.FromMinutes(rule.OffsetMinutes));
                var elapsed = now.TimeOfDay - target;
                // 10 daqiqalik oyna — servis biroz kechiksa ham eslatma o'tkazib yuborilmasin.
                if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMinutes(10)) continue;

                // Davomat allaqachon olingan bo'lsa — belgi ham qo'yilmaydi (jim o'tkaziladi).
                if (await ConductedAsync(db, l.Group, todayStr, ct)) continue;
                if (!await TryMarkAsync(db, $"{rule.Id}:{l.Group.Id}:{todayStr}", todayStr, ct)) continue;

                try
                {
                    await SendAsync(db, rule, l, todayStr, meta?.Name ?? "", ct);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Davomat eslatmasi: guruh {Id} uchun xatolik", l.Group.Id);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bugungi darslar — <see cref="AttendanceWatch.LessonsOn"/> (o'qituvchi jadvali va «Darslar
    /// monitoringi» bilan bitta qoida). Faqat o'qituvchiga KO'RINADIGAN guruhlar
    /// (<see cref="TeacherGroupAccess.VisibleQuery"/>: arxiv/blok yo'q) — bloklangan guruhda o'qituvchi
    /// jurnalni ocha olmaydi, demak eslatma ma'nosiz.
    /// </summary>
    internal static async Task<List<AttendanceWatch.DayLesson>> LessonsTodayAsync(
        IAppDbContext db, DateOnly day, CancellationToken ct = default)
    {
        var dayStr = day.ToString("yyyy-MM-dd");
        var groups = await db.Classes.AsNoTracking().Where(TeacherGroupAccess.VisibleQuery).ToListAsync(ct);
        if (groups.Count == 0) return [];
        var ids = groups.Select(g => g.Id).ToList();
        var moves = (await db.LessonReschedules.AsNoTracking()
                .Where(r => ids.Contains(r.ClassId) && (r.FromDate == dayStr || r.ToDate == dayStr))
                .ToListAsync(ct))
            .ToLookup(r => r.ClassId, StringComparer.Ordinal);
        var assignments = (await db.SubstituteTeacherAssignments.AsNoTracking()
                .Where(a => a.IsActive && ids.Contains(a.GroupId)
                            && string.Compare(a.Date, dayStr) <= 0
                            && string.Compare(a.EndDate ?? a.Date, dayStr) >= 0)
                .ToListAsync(ct))
            .ToLookup(a => a.GroupId, StringComparer.Ordinal);
        return AttendanceWatch.LessonsOn(groups, moves, assignments, day);
    }

    /// <summary>Darsni shu kuni O'TADIGAN o'qituvchi: o'rinbosar biriktirilgan bo'lsa — o'rinbosar, aks holda guruh egasi.</summary>
    internal static string RecipientTeacherId(AttendanceWatch.DayLesson l) =>
        string.IsNullOrEmpty(l.SubstituteTeacherId) ? l.Group.TeacherId ?? "" : l.SubstituteTeacherId;

    /// <summary>
    /// Eslatma belgisini bazaga yozadi. false — belgi allaqachon bor (bugun shu eslatma ketgan,
    /// masalan server qayta yuklanishidan oldin) yoki parallel nusxa ulgurib qo'ygan (unikal indeks).
    /// <para>⚠️ <c>false</c> FAQAT haqiqiy poygada (kalit bazada BOR). Ilgari har qanday saqlash xatosi
    /// "boshqa nusxa ulgurdi" deb o'qilardi: oldingi eslatmaning saqlanmagan buzuq qatori (SMS jurnali,
    /// ilova tarixi) belgini yiqitar va shu daqiqadagi QOLGAN hamma eslatma jimgina o'tkazib yuborilardi.
    /// Endi qoldiq avval hal qilinadi (<see cref="JobRunMarker.SettleLeftoversAsync"/>), boshqa xato esa
    /// tashqariga chiqadi — siklda loglanadi va belgi yo'qligi uchun keyingi daqiqada qayta uriniladi.</para>
    /// </summary>
    internal static async Task<bool> TryMarkAsync(IAppDbContext db, string key, string date, CancellationToken ct)
    {
        await JobRunMarker.SettleLeftoversAsync(db, ct);
        if (await db.LessonReminderMarks.AsNoTracking().AnyAsync(m => m.Key == key, ct)) return false;
        var mark = new LessonReminderMark { Key = key, Date = date };
        db.LessonReminderMarks.Add(mark);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Belgini kontekstdan olib tashlaymiz (`Added` qator keyingi saqlashni ham yiqitardi).
            db.LessonReminderMarks.Entry(mark).State = EntityState.Detached;
            // Unikal kalit — boshqa nusxa ulgurdi: haqiqiy poyga.
            if (await db.LessonReminderMarks.AsNoTracking().AnyAsync(m => m.Key == key, ct)) return false;
            throw;
        }
    }

    /// <summary>Shu kuni davomat allaqachon kiritilganmi (dars "o'tildi").</summary>
    private static Task<bool> ConductedAsync(IAppDbContext db, Group g, string todayStr, CancellationToken ct) =>
        db.LessonNotes.AnyAsync(n =>
            n.ClassId == g.Id && n.SubjectId == g.CourseId && n.Quarter == 1 &&
            n.Date == todayStr && n.Conducted, ct);

    /// <summary>
    /// Kunlik rejim: "all" — bugun darsi bor-yo'qligidan va to'ldirganidan qat'i nazar BARCHA faol
    /// o'qituvchilarga bittadan; "not_filled" — bugun darsi bo'lib (boshlangan) davomati hali
    /// kiritilmagan har bir guruh uchun o'z o'qituvchisiga.
    /// </summary>
    private async Task SendDailyAsync(
        IAppDbContext db, AutoMessageRule rule, List<AttendanceWatch.DayLesson> todayLessons, string todayStr, DateTime now,
        string centerName, CancellationToken ct)
    {
        if (rule.SendScope == ReminderSendScopes.All)
        {
            var teachers = await db.Teachers.Where(t => !t.IsArchived).ToListAsync(ct);
            var template = string.IsNullOrWhiteSpace(rule.Template)
                ? "Assalomu alaykum, {fish}! Iltimos, bugungi darslaringiz davomatini jurnalga kiritishni unutmang."
                : rule.Template;
            var title = string.IsNullOrWhiteSpace(rule.Name) ? "Davomat eslatmasi" : rule.Name;
            foreach (var teacher in teachers)
            {
                var body = MessageTokenizer.Teacher(template, teacher, centerName);
                await DeliverAsync(db, rule, teacher, title, body, ct);
            }
            return;
        }

        // "not_filled": faqat boshlangan (yoki vaqti kiritilmagan) darslar — hali bo'lmagan darsni so'ramaymiz.
        foreach (var l in todayLessons)
        {
            if (TimeSpan.TryParse(l.StartTime, out var start) && start > now.TimeOfDay) continue;
            await SendAsync(db, rule, l, todayStr, centerName, ct);
        }
    }

    private async Task SendAsync(
        IAppDbContext db, AutoMessageRule rule, AttendanceWatch.DayLesson lesson, string todayStr, string centerName,
        CancellationToken ct)
    {
        var g = lesson.Group;
        // Davomat allaqachon kiritilgan bo'lsa — jim o'tkaziladi.
        if (await ConductedAsync(db, g, todayStr, ct)) return;

        // Shu kuni darsni O'TADIGAN odam (o'rinbosar bo'lsa — u).
        var teacherId = RecipientTeacherId(lesson);
        if (string.IsNullOrEmpty(teacherId)) return;
        var teacher = await db.Teachers.FirstOrDefaultAsync(t => t.Id == teacherId && !t.IsArchived, ct);
        if (teacher is null) return;

        var courseName = string.IsNullOrEmpty(g.CourseId) ? "" : (await db.Subjects.FindAsync(new object[] { g.CourseId }, ct))?.Name ?? "";
        var template = string.IsNullOrWhiteSpace(rule.Template)
            ? rule.OffsetMinutes < 0 && rule.SendScope != ReminderSendScopes.NotFilled
                // Darsdan OLDINGI eslatma — dars hali boshlanmagan.
                ? "Assalomu alaykum, {fish}! {guruh} guruhida ({kurs}) dars {dars_vaqti} da boshlanadi. Davomat olishni unutmang."
                : "Assalomu alaykum, {fish}! {guruh} guruhida ({kurs}) dars boshlandi ({dars_vaqti}). Iltimos, davomatni jurnalga kiriting."
            : rule.Template;
        var extra = new Dictionary<string, string> { ["{kurs}"] = courseName };
        // Ko'chirilgan darsning vaqti boshqacha bo'lishi mumkin — {dars_vaqti} darsning O'Z vaqtidan.
        if (lesson.Moved && lesson.StartTime.Length > 0)
            template = MessageTokenizer.ApplyExtra(template, new Dictionary<string, string>
            {
                ["{dars_vaqti}"] = lesson.EndTime.Length > 0 ? $"{lesson.StartTime}-{lesson.EndTime}" : lesson.StartTime,
            });
        var body = MessageTokenizer.Teacher(template, teacher, centerName, extra: extra, group: g);
        var title = string.IsNullOrWhiteSpace(rule.Name) ? "Davomat eslatmasi" : rule.Name;

        await DeliverAsync(db, rule, teacher, title, body, ct);
    }

    /// <summary>Bitta o'qituvchiga yetkazish: kabinet bildirishnomasi (HAR DOIM, akkaunti bo'lsa) + Telegram
    /// (<see cref="AutoMessageChannels.Telegram"/>).</summary>
    private async Task DeliverAsync(
        IAppDbContext db, AutoMessageRule rule, Teacher teacher, string title, string body, CancellationToken ct)
    {
        if (teacher.UserId is not null)
            NotificationStore.Add(db, teacher.UserId, title, body, "lesson_attendance");

        if (AutoMessageChannels.Telegram(rule) && telegram.IsConfigured)
        {
            var chatIds = await db.TelegramRegistrations
                .Where(r => r.TeacherId == teacher.Id)
                .Select(r => r.ChatId).Distinct().ToListAsync(ct);
            foreach (var chatId in chatIds)
                await telegram.SendMessageAsync(chatId, $"🔔 {title}\n\n{body}", ct: ct);
        }
    }
}
