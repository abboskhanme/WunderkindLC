using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Backup — markazning BIZNES ma'lumotlarini JSON ko'rinishida yig'ib, Telegram orqali adminga
/// yuboradi. Ilova ichida ishlaydi (alohida docker konteyner/pg_dump/curl KERAK EMAS) — bot tokeni
/// va admin chat ID DB'da (CenterMeta), yuborish ilovaning ishlaydigan <see cref="TelegramService"/>i
/// orqali. Avtomatik (kunlik, jadval bo'yicha) <see cref="BackupSchedulerService"/> chaqiradi yoki
/// admin "Hozir yuborish" tugmasi.
///
/// <para><b>BU TO'LIQ TIKLASH NUSXASI EMAS</b> — u server pg_dump (.sql.gz, DEPLOY.md §6.3). Bu JSON
/// "ma'lumot nusxasi": biznes jadvallari (o'quvchi, guruh, pul, jurnal, lid, kitob, shartnoma,
/// qo'ng'iroq, bog'lanish, topshiriq, vakansiya, KPI ...). ATAYIN KIRMAYDI (ro'yxati faylning
/// o'zida ham — <c>excluded</c>):</para>
/// <list type="bullet">
///   <item>MAXFIY maydonlar — parol xeshlari, ochiq dastlabki parol, tokenlar, kamera RTSP manzili
///     (<see cref="SecretScrub"/> — nom bo'yicha, HAR jadvalda; arxiv suratlarining ichida ham);</item>
///   <item>texnik jurnallar va keshlar — audit tarixi, SMS log, webhook/CAPI hodisalari, turniket xom
///     hodisalari, CTI hodisa/buyruq loglari, reklama statistikasi keshi;</item>
///   <item>biometrik ma'lumot (yuz etaloni, selfi tekshiruvlari) va sessiya/OTP/qurilma kalitlari.</item>
/// </list>
/// <para>⚠️ Fayl <b>GZIP</b> (<c>.json.gz</c>, 2026-10-08): JSON to'g'ridan-to'g'ri siqilgan oqimga
/// yoziladi (<see cref="BuildGzipAsync"/>) — xotirada xom JSON bufer UMUMAN turmaydi va hajm ~10 marta
/// kichrayadi. Ilgari ~90 jadval bitta xom <c>byte[]</c> ga yig'ilar, 49 MB dan oshsa ikkinchi nusxa
/// birinchisi hali xotirada turganda quriladi — 1 GB serverda cho'qqi xotira ikki baravar edi.
/// Ochish: 7-Zip / WinRAR yoki <c>gunzip</c>.</para>
/// <para>Siqilgan fayl ham Telegram hujjat chegarasidan (~50 MB) oshsa, ikkinchi urinishda OG'IR
/// jadvallar (<see cref="HeavyTables"/>) chiqarib qayta yig'iladi (birinchi bufer OLDIN qo'yib
/// yuboriladi) — butun zaxira yuborilmay qolmasin.</para>
/// </summary>
public static class BackupService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // MAXFIY maydonlar (…Token, …Password, PasswordHash, RTSP manzil) va byte[] ustunlar HAR
        // turdan (anonim proyeksiya ham) olib tashlanadi — yangi jadval qo'shilganda unutilib qolmasin.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SecretScrub.StripSecretProperties } },
    };

    /// <summary>Telegram hujjat chegarasi (~50 MB) ostida qoladigan xavfsiz o'lcham.</summary>
    public const int MaxBytes = 49_000_000;

    /// <summary>Hajmi katta bo'lishi mumkin bo'lgan, lekin asosiy bo'lmagan jadvallar — fayl
    /// <see cref="MaxBytes"/> dan oshsa, ikkinchi urinishda shular chiqariladi.
    /// <para>⚠️ <c>financeTransactions</c>, <c>monthlyCharges</c>, <c>journalEntries</c> ATAYIN bu yerda
    /// YO'Q — ular katta, lekin ASOSIY (pul va davomat): ixcham nusxa aynan ular uchun kerak.
    /// Qo'ng'iroqlar jurnali va lid tarixi esa ikkilamchi.</para></summary>
    public static readonly IReadOnlyList<string> HeavyTables = new[]
    {
        "courseItemAttempts", "leadTelegramMessages", "workTaskEvents", "contactAttempts",
        "chatMessages", "userNotifications", "kpiMonthSnapshots", "levelTestSubmissions",
        "calls", "ctiCallRecords", "leadEvents",
    };

    /// <summary>Zaxiraga ATAYIN kiritilmagan jadvallar (fayl ichida <c>excluded</c> bo'lib yoziladi —
    /// "BARCHA ma'lumot" deb aldamaslik uchun).</summary>
    public static readonly IReadOnlyList<string> ExcludedTables = new[]
    {
        "auditLogs", "smsLogs", "aiChecks", "igMessages", "igWebhookEvents", "igCapiEvents",
        "igAdEntities", "igAdInsights", "igAdPages", "igAdAccounts", "igOAuthStates", "turnstileEvents",
        "ctiCallEvents", "ctiCommandLogs", "refreshTokens", "loginOtpCodes", "trustedDevices",
        "faceChallenges", "loginFaceChecks", "studentFaceProfiles", "bookBotSessions", "testBotSessions",
        "seedMarkers", "cameras", "attendanceNotifyItems",
        // CRM upgrade Phase 2: Telegram outbox / delivery log — log-like and large (180-day retention).
        "messageDeliveries",
        // Read-only MCP: OAuth mijozlari, ulanishlar, kod/token xeshlari va so'rovlar jurnali — texnik.
        "mcpClients", "mcpGrants", "mcpAuthCodes", "mcpTokens", "mcpAudit",
        // O'quvchi parollari seyfi (CRM Phase 6) — shifrlangan bo'lsa ham Telegram'ga ketmaydi (auth-sessions.md §6).
        "studentCredentials",
        // Davomat seriyalari (U3d) — jurnaldan qayta hisoblanadi (hosila ma'lumot).
        "absenceStreakAlerts",
    };

    /// <summary>Biznes jadvallarini JSON (UTF-8 baytlar) ko'rinishida yig'adi (siqilmagan — testlar va
    /// tekshiruv uchun; Telegram'ga <see cref="BuildGzipAsync"/> ketadi).</summary>
    /// <param name="includeHeavy">false — <see cref="HeavyTables"/> chiqariladi (hajm chegarasi uchun).</param>
    public static async Task<(byte[] Json, int TableCount, long Rows)> BuildJsonAsync(
        IAppDbContext db, CancellationToken ct = default, bool includeHeavy = true)
    {
        var (data, tables, rows) = await CollectAsync(db, includeHeavy, ct);
        return (JsonSerializer.SerializeToUtf8Bytes(data, JsonOpts), tables, rows);
    }

    /// <summary>
    /// <see cref="BuildJsonAsync"/> bilan AYNAN bir xil JSON, lekin GZIP bilan siqilgan (<c>.json.gz</c>).
    /// JSON to'g'ridan-to'g'ri siqilgan oqimga yoziladi — xom JSON bufer xotirada umuman turmaydi.
    /// </summary>
    public static async Task<(byte[] Gzip, int TableCount, long Rows)> BuildGzipAsync(
        IAppDbContext db, CancellationToken ct = default, bool includeHeavy = true)
    {
        var (data, tables, rows) = await CollectAsync(db, includeHeavy, ct);
        using var ms = new MemoryStream();
        await using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            await JsonSerializer.SerializeAsync(gz, data, JsonOpts, ct);
        return (ms.ToArray(), tables, rows);
    }

    private static async Task<(Dictionary<string, object> Data, int TableCount, long Rows)> CollectAsync(
        IAppDbContext db, bool includeHeavy, CancellationToken ct)
    {
        var data = new Dictionary<string, object>();
        long rows = 0;
        var tables = 0;
        var skipped = new List<string>();
        void Put<T>(string name, List<T> list)
        {
            data[name] = list;
            rows += list.Count;
            tables++;
        }
        async Task Add<T>(string name, IQueryable<T> q) where T : class
        {
            if (!includeHeavy && HeavyTables.Contains(name)) { skipped.Add(name); return; }
            Put(name, await q.AsNoTracking().ToListAsync(ct));
        }

        data["generatedAt"] = AppClock.Iso();
        // ---- Asosiy biznes ma'lumotlari ----
        // PAROLLAR NUSXAGA TUSHMAYDI: bu JSON Telegram chatiga yuboriladi, shuning uchun
        // AppUser TO'LIQ emas, tanlab olinadi — `PasswordHash` ham, birinchi login'gacha OCHIQ
        // saqlanadigan `InitialPassword` ham chiqarib tashlanadi (ustiga `SecretScrub` ham bor).
        await Add("users", db.Users.Select(u => new
        {
            u.Id, u.FullName, u.Role, u.Email, u.Phone, u.AvatarUrl,
            u.FirstLoginAt, u.LastLoginAt, u.Position, u.Permissions, u.RoleTemplateId,
        }));
        await Add("staffRoleTemplates", db.StaffRoleTemplates);
        await Add("students", db.Students);
        await Add("teachers", db.Teachers);
        await Add("teacherAttendances", db.TeacherAttendances);
        await Add("teacherReviews", db.TeacherReviews);
        await Add("subjects", db.Subjects);
        await Add("subjectPriceBands", db.SubjectPriceBands);
        await Add("classes", db.Classes);
        await Add("studentGroups", db.StudentGroups);
        await Add("groupTeacherAssignments", db.GroupTeacherAssignments);
        await Add("substituteTeacherAssignments", db.SubstituteTeacherAssignments);
        await Add("membershipReasonLogs", db.MembershipReasonLogs);
        await Add("studentNotes", db.StudentNotes);
        await Add("studentIntakeMarks", db.StudentIntakeMarks);
        await Add("rooms", db.Rooms);
        await Add("branches", db.Branches);
        await Add("districts", db.Districts);
        await Add("schools", db.Schools);
        // ---- Lidlar ----
        await Add("leads", db.Leads);
        await Add("leadStages", db.LeadStages);
        await Add("leadEvents", db.LeadEvents);
        await Add("leadSources", db.LeadSources);
        await Add("leadForms", db.LeadForms);
        await Add("leadFormFields", db.LeadFormFields);
        await Add("leadFormSubmissions", db.LeadFormSubmissions);
        await Add("leadEntryFields", db.LeadEntryFields);
        await Add("leadTelegramMessages", db.LeadTelegramMessages);
        await Add("trialLessons", db.TrialLessons);
        // ---- Jurnal / baholash ----
        await Add("journalEntries", db.JournalEntries);
        await Add("lessonNotes", db.LessonNotes);
        await Add("absenceReasons", db.AbsenceReasons);
        await Add("gradingCriteria", db.GradingCriteria);
        await Add("groupGradingCriteria", db.GroupGradingCriteria);
        await Add("criterionGrades", db.CriterionGrades);
        await Add("studentBallAdjustments", db.StudentBallAdjustments);
        await Add("lessonReschedules", db.LessonReschedules);
        // ---- Moliya ----
        await Add("financeTransactions", db.FinanceTransactions);
        await Add("monthlyCharges", db.MonthlyCharges);
        await Add("transactionTypes", db.TransactionTypes);
        await Add("plannedExpenses", db.PlannedExpenses);
        // Kassalar va kassadan kassaga ko'chirishlar — kassa QOLDIG'I shulardan tiklanadi.
        await Add("cashboxes", db.Cashboxes);
        await Add("cashboxTransfers", db.CashboxTransfers);
        // Davomat jarimalari (U3c) — maoshdan ushlanadi, ya'ni PUL.
        await Add("teacherAttendanceFines", db.TeacherAttendanceFines);
        await Add("studentDiscounts", db.StudentDiscounts);
        await Add("lessonDiscounts", db.LessonDiscounts);
        await Add("studentExtensions", db.StudentExtensions);
        await Add("retentionBonusTracks", db.RetentionBonusTracks);
        await Add("retentionBonusAwards", db.RetentionBonusAwards);
        await Add("retentionBonusShares", db.RetentionBonusShares);
        // ---- Aloqa / bildirishnoma ----
        await Add("chatMessages", db.ChatMessages);
        await Add("broadcasts", db.Broadcasts);
        await Add("pushMessages", db.PushMessages);
        await Add("telegramRegistrations", db.TelegramRegistrations);
        await Add("telegramGroups", db.TelegramGroups);
        await Add("userNotifications", db.UserNotifications);
        await Add("deviceTokens", db.DeviceTokens);
        await Add("feedbacks", db.Feedbacks);
        await Add("autoMessageRules", db.AutoMessageRules);
        await Add("smsTemplates", db.SmsTemplates);
        // CRM upgrade Phase 2: attachment METADATA (files stay in uploads/) and student achievements.
        await Add("messageAttachments", db.MessageAttachments);
        await Add("studentAchievements", db.StudentAchievements);
        await Add("smsBatches", db.SmsBatches);
        await Add("botUsers", db.BotUsers);
        await Add("botSupportMessages", db.BotSupportMessages);
        // ---- Bog'lanish kerak (follow-up navbati) ----
        await Add("contactRequests", db.ContactRequests);
        await Add("contactAttempts", db.ContactAttempts);
        await Add("contactStages", db.ContactStages);
        await Add("contactAiAnalyses", db.ContactAiAnalyses);
        // ---- Qo'ng'iroqlar ----
        await Add("calls", db.Calls);
        await Add("ctiAgents", db.CtiAgents);
        await Add("ctiCallRecords", db.CtiCallRecords);
        // ---- O'quv dasturi (curriculum) ----
        await Add("curricula", db.Curricula);
        await Add("subjectCurricula", db.SubjectCurricula);
        await Add("courseModules", db.CourseModules);
        await Add("courseTopics", db.CourseTopics);
        await Add("courseLessons", db.CourseLessons);
        await Add("courseItems", db.CourseItems);
        await Add("courseQuestions", db.CourseQuestions);
        await Add("courseProgresses", db.CourseProgresses);
        await Add("courseItemAttempts", db.CourseItemAttempts);
        await Add("groupCurriculumLogs", db.GroupCurriculumLogs);
        // ---- Testlar / sertifikatlar ----
        await Add("levelTests", db.LevelTests);
        await Add("levelTestQuestions", db.LevelTestQuestions);
        await Add("levelTestBands", db.LevelTestBands);
        await Add("levelTestSubmissions", db.LevelTestSubmissions);
        await Add("levelTestInvites", db.LevelTestInvites);
        await Add("testResults", db.TestResults);
        await Add("testScores", db.TestScores);
        await Add("externalTestScores", db.ExternalTestScores);
        await Add("testCertificateTemplates", db.TestCertificateTemplates);
        await Add("testCertificates", db.TestCertificates);
        await Add("certificateTemplates", db.CertificateTemplates);
        await Add("studentCertificates", db.StudentCertificates);
        await Add("certificateVerifications", db.CertificateVerifications);
        // ---- Kitoblar / shartnomalar ----
        await Add("books", db.Books);
        await Add("bookOrders", db.BookOrders);
        await Add("bookStockMoves", db.BookStockMoves);
        await Add("contractTemplates", db.ContractTemplates);
        await Add("contracts", db.Contracts);
        // ---- Topshiriqlar ----
        await Add("workTaskBoards", db.WorkTaskBoards);
        await Add("workTaskColumns", db.WorkTaskColumns);
        await Add("workTasks", db.WorkTasks);
        await Add("workTaskItems", db.WorkTaskItems);
        await Add("workTaskComments", db.WorkTaskComments);
        await Add("workTaskEvents", db.WorkTaskEvents);
        // ---- Vakansiyalar / moslashuv ----
        await Add("vacancies", db.Vacancies);
        await Add("jobApplications", db.JobApplications);
        await Add("jobApplicationEvents", db.JobApplicationEvents);
        await Add("careerAbout", db.CareerAbout);
        await Add("careerBotUsers", db.CareerBotUsers);
        await Add("onboardingSteps", db.OnboardingSteps);
        await Add("employeeOnboardings", db.EmployeeOnboardings);
        await Add("employeeOnboardingTasks", db.EmployeeOnboardingTasks);
        // ---- KPI ----
        await Add("kpiProfiles", db.KpiProfiles);
        await Add("kpiProfileSalaries", db.KpiProfileSalaries);
        await Add("kpiRuleSets", db.KpiRuleSets);
        await Add("kpiTickets", db.KpiTickets);
        await Add("kpiMonthResults", db.KpiMonthResults);
        await Add("kpiMonthSnapshots", db.KpiMonthSnapshots);
        await Add("checklistTemplates", db.ChecklistTemplates);
        await Add("checklistTemplateItems", db.ChecklistTemplateItems);
        await Add("checklistEntries", db.ChecklistEntries);
        // ---- Instagram (biznes qismi; tokenlar `SecretScrub` bilan tushadi) ----
        await Add("igAccounts", db.IgAccounts);
        await Add("igAutoRules", db.IgAutoRules);
        await Add("igKnowledges", db.IgKnowledges);
        await Add("igIceBreakers", db.IgIceBreakers);
        await Add("igScheduledPosts", db.IgScheduledPosts);
        await Add("igConversations", db.IgConversations);
        await Add("igAdLeads", db.IgAdLeads);
        // ---- Landing (ommaviy sayt) ----
        await Add("landingTeachers", db.LandingTeachers);
        await Add("landingCertificates", db.LandingCertificates);
        await Add("landingTestimonials", db.LandingTestimonials);
        await Add("landingFaqs", db.LandingFaqs);
        // ---- Boshqa ----
        await Add("actionReasons", db.ActionReasons);
        // ARXIV SURATLARI ichida o'chirilgan entity'ning TO'LIQ JSON'i turadi (eski xodim suratlarida
        // PasswordHash / ochiq InitialPassword bo'lishi mumkin) — matnning ICHI ham tozalanadi.
        Put("archivedRecords", (await db.ArchivedRecords.AsNoTracking().ToListAsync(ct))
            .Select(r => new
            {
                r.Id, r.Type, r.EntityId, r.Title, r.Subtitle, r.Reason, r.DeletedAt, r.ActorName,
                Json = SecretScrub.ScrubJson(r.Json),
            }).ToList());
        await Add("supportSlots", db.SupportSlots);
        await Add("studentAiAnalyses", db.StudentAiAnalyses);
        await Add("studentAiAccesses", db.StudentAiAccesses);
        await Add("groupAiAnalyses", db.GroupAiAnalyses);
        await Add("teacherAiAnalyses", db.TeacherAiAnalyses);
        await Add("funnelAiAnalyses", db.FunnelAiAnalyses);
        await Add("centerAiAnalyses", db.CenterAiAnalyses);
        await Add("userSettings", db.UserSettings);
        // CenterMeta — `InstagramCapiToken`, `FcmVapidKey` va boshqa maxfiy maydonlarsiz (SecretScrub).
        await Add("centerMeta", db.CenterMeta);

        data["excluded"] = ExcludedTables.Concat(skipped).ToList();
        return (data, tables, rows);
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
            // GZIP — siqilgan JSON (xom bufer xotirada turmaydi).
            var (file, tables, rows) = await BuildGzipAsync(db, ct);
            // Telegram hujjat chegarasi ~50MB — oshsa OG'IR jadvallarsiz qayta yig'amiz (butun zaxira
            // yuborilmay qolgandan ko'ra asosiy jadvallar ketgani yaxshi; fayl ichida `excluded` da yoziladi).
            var compact = false;
            if (file.Length > MaxBytes)
            {
                // ⚠️ Birinchi bufer OLDIN qo'yib yuboriladi — ikki nusxa bir vaqtda xotirada turmasin.
                file = [];
                (file, tables, rows) = await BuildGzipAsync(db, ct, includeHeavy: false);
                compact = true;
            }
            var ts = AppClock.Now.ToString("yyyyMMdd_HHmm");
            var fileName = $"wunderkindlc_backup_{ts}.json.gz";
            var sizeKb = Math.Round(file.Length / 1024.0, 1);
            var caption = $"✅ WunderkindLC backup (JSON, gzip)\n📅 {AppClock.Now:yyyy-MM-dd HH:mm}\n" +
                          $"🗂 {tables} jadval · {rows} yozuv · {sizeKb} KB\n" +
                          (compact ? "⚠️ Hajm katta — og'ir jadvallarsiz yuborildi.\n" : "") +
                          "ℹ️ Ma'lumot nusxasi (maxfiy maydonlar va texnik jurnallarsiz). To'liq tiklash — server pg_dump.\n" +
                          "📦 Ochish: 7-Zip / WinRAR yoki gunzip.";

            if (file.Length > MaxBytes)
                return (false, $"Backup juda katta ({Math.Round(file.Length / 1_048_576.0, 1)} MB > 50MB, siqilgan) — yuborilmadi.");

            var ok = await telegram.SendDocumentAsync(chatId, file, fileName, caption, ct);
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
