using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Backup — markazning BARCHA ma'lumotlarini JSON ko'rinishida yig'ib, Telegram orqali adminga
/// yuboradi. Ilova ichida ishlaydi (alohida docker konteyner/pg_dump/curl KERAK EMAS) — bot tokeni
/// va admin chat ID DB'da (CenterMeta), yuborish ilovaning ishlaydigan <see cref="TelegramService"/>i
/// orqali. Avtomatik (kunlik, jadval bo'yicha) <see cref="BackupSchedulerService"/> chaqiradi yoki
/// admin "Hozir yuborish" tugmasi.
/// </summary>
public static class BackupService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Barcha jadvallarni JSON (UTF-8 baytlar) ko'rinishida yig'adi.</summary>
    public static async Task<(byte[] Json, int TableCount, long Rows)> BuildJsonAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object>();
        long rows = 0;
        async Task Add<T>(string name, IQueryable<T> q) where T : class
        {
            var list = await q.AsNoTracking().ToListAsync(ct);
            data[name] = list;
            rows += list.Count;
        }

        data["generatedAt"] = AppClock.Iso();
        // ---- Asosiy biznes ma'lumotlari ----
        // PAROLLAR NUSXAGA TUSHMAYDI: bu JSON Telegram chatiga yuboriladi, shuning uchun
        // AppUser TO'LIQ emas, tanlab olinadi — `PasswordHash` ham, birinchi login'gacha OCHIQ
        // saqlanadigan `InitialPassword` ham chiqarib tashlanadi. To'liq tiklash baribir
        // pg_dump (.sql.gz) orqali qilinadi (DEPLOY.md §6.3), bu JSON esa ma'lumot nusxasi.
        await Add("users", db.Users.Select(u => new
        {
            u.Id, u.FullName, u.Role, u.Email, u.Phone, u.AvatarUrl,
            u.FirstLoginAt, u.LastLoginAt, u.Position, u.Permissions,
        }));
        await Add("students", db.Students);
        await Add("teachers", db.Teachers);
        await Add("teacherAttendances", db.TeacherAttendances);
        await Add("subjects", db.Subjects);
        await Add("classes", db.Classes);
        await Add("studentGroups", db.StudentGroups);
        await Add("leads", db.Leads);
        await Add("leadStages", db.LeadStages);
        await Add("leadEvents", db.LeadEvents);
        await Add("trialLessons", db.TrialLessons);
        // ---- Jurnal / baholash ----
        await Add("journalEntries", db.JournalEntries);
        await Add("lessonNotes", db.LessonNotes);
        await Add("absenceReasons", db.AbsenceReasons);
        await Add("gradingCriteria", db.GradingCriteria);
        await Add("groupGradingCriteria", db.GroupGradingCriteria);
        await Add("criterionGrades", db.CriterionGrades);
        // ---- Moliya ----
        await Add("financeTransactions", db.FinanceTransactions);
        await Add("monthlyCharges", db.MonthlyCharges);
        // ---- Aloqa / bildirishnoma ----
        await Add("chatMessages", db.ChatMessages);
        await Add("broadcasts", db.Broadcasts);
        await Add("pushMessages", db.PushMessages);
        await Add("telegramRegistrations", db.TelegramRegistrations);
        await Add("telegramGroups", db.TelegramGroups);
        await Add("userNotifications", db.UserNotifications);
        await Add("deviceTokens", db.DeviceTokens);
        await Add("feedbacks", db.Feedbacks);
        // ---- O'quv dasturi (curriculum) ----
        await Add("curricula", db.Curricula);
        await Add("subjectCurricula", db.SubjectCurricula);
        await Add("courseModules", db.CourseModules);
        await Add("courseTopics", db.CourseTopics);
        await Add("courseLessons", db.CourseLessons);
        await Add("courseItems", db.CourseItems);
        await Add("courseQuestions", db.CourseQuestions);
        await Add("courseProgresses", db.CourseProgresses);
        await Add("groupCurriculumLogs", db.GroupCurriculumLogs);
        // ---- Boshqa ----
        await Add("actionReasons", db.ActionReasons);
        await Add("archivedRecords", db.ArchivedRecords);
        await Add("levelTests", db.LevelTests);
        await Add("levelTestQuestions", db.LevelTestQuestions);
        await Add("levelTestBands", db.LevelTestBands);
        await Add("levelTestSubmissions", db.LevelTestSubmissions);
        await Add("supportSlots", db.SupportSlots);
        await Add("certificateTemplates", db.CertificateTemplates);
        await Add("studentCertificates", db.StudentCertificates);
        await Add("studentAiAnalyses", db.StudentAiAnalyses);
        await Add("centerAiAnalyses", db.CenterAiAnalyses);
        await Add("rooms", db.Rooms);
        await Add("userSettings", db.UserSettings);
        await Add("centerMeta", db.CenterMeta);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOpts);
        return (bytes, data.Count - 1, rows); // -1: generatedAt jadval emas
    }

    /// <summary>
    /// Backupni yig'ib Telegram orqali adminga yuboradi. Muvaffaqiyat — (true, xabar).
    /// Bot/chat sozlanmagan bo'lsa (false, sabab).
    /// </summary>
    public static async Task<(bool Ok, string Message)> SendAsync(
        IAppDbContext db, TelegramService telegram, ILogger? logger = null, CancellationToken ct = default)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null || !meta.TelegramBackupEnabled)
            return (false, "Telegram backup o'chirilgan (Sozlamalar → Telegram bot → Backup).");
        if (!telegram.IsConfigured)
            return (false, "Telegram bot sozlanmagan (token yo'q).");
        var chatStr = (meta.TelegramAdminChatId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(chatStr) || !long.TryParse(chatStr, out var chatId) || chatId == 0)
            return (false, "Admin chat ID kiritilmagan yoki noto'g'ri.");

        try
        {
            var (json, tables, rows) = await BuildJsonAsync(db, ct);
            var ts = AppClock.Now.ToString("yyyyMMdd_HHmm");
            var fileName = $"wunderkindlc_backup_{ts}.json";
            var sizeKb = Math.Round(json.Length / 1024.0, 1);
            var caption = $"✅ WunderkindLC backup (JSON)\n📅 {AppClock.Now:yyyy-MM-dd HH:mm}\n" +
                          $"🗂 {tables} jadval · {rows} yozuv · {sizeKb} KB";

            // Telegram hujjat chegarasi ~50MB.
            if (json.Length > 49_000_000)
                return (false, $"Backup juda katta ({Math.Round(json.Length / 1_048_576.0, 1)} MB > 50MB) — yuborilmadi.");

            var ok = await telegram.SendDocumentAsync(chatId, json, fileName, caption, ct);
            if (!ok)
                return (false, "Telegram'ga yuborib bo'lmadi (chat ID yoki bot huquqini tekshiring).");

            meta.TelegramBackupLastSentAt = AppClock.Now;
            await db.SaveChangesAsync(ct);
            return (true, $"Backup yuborildi: {tables} jadval, {rows} yozuv, {sizeKb} KB.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Backup yuborishda xatolik");
            return (false, $"Xatolik: {ex.Message}");
        }
    }
}
