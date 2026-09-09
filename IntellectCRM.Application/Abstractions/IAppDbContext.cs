using Microsoft.EntityFrameworkCore;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Abstractions;

/// <summary>
/// Ma'lumotlar bazasi konteksti abstraksiyasi. Application qatlamidagi xizmatlar
/// (Services) konkret <c>AppDbContext</c> (Infrastructure) o'rniga shu interfeysga
/// bog'lanadi — bu bog'liqlik yo'nalishini ichkariga (Domain/Application tomon)
/// saqlaydi. Infrastructure'dagi <c>AppDbContext</c> shu interfeysni implement qiladi,
/// DI esa <c>IAppDbContext</c> ni o'sha scoped <c>AppDbContext</c> ga ulaydi.
/// </summary>
public interface IAppDbContext
{
    DbSet<AppUser> Users { get; }
    DbSet<Student> Students { get; }
    DbSet<Teacher> Teachers { get; }
    DbSet<TeacherAttendance> TeacherAttendances { get; }
    DbSet<TurnstileEvent> TurnstileEvents { get; }
    DbSet<Camera> Cameras { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Group> Classes { get; }
    DbSet<GroupTeacherAssignment> GroupTeacherAssignments { get; }
    DbSet<SubstituteTeacherAssignment> SubstituteTeacherAssignments { get; }
    DbSet<RetentionBonusAward> RetentionBonusAwards { get; }
    DbSet<RetentionBonusShare> RetentionBonusShares { get; }
    DbSet<RetentionBonusTrack> RetentionBonusTracks { get; }
    DbSet<StudentGroup> StudentGroups { get; }
    DbSet<StudentNote> StudentNotes { get; }
    DbSet<StudentDiscount> StudentDiscounts { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadStage> LeadStages { get; }
    DbSet<LeadEvent> LeadEvents { get; }
    /// <summary>Lid haqida Telegramga yuborilgan xabarlar (lid × chat) — kartani TAHRIRLASH uchun.</summary>
    DbSet<LeadTelegramMessage> LeadTelegramMessages { get; }
    DbSet<TrialLesson> TrialLessons { get; }
    DbSet<TestResult> TestResults { get; }
    DbSet<TestScore> TestScores { get; }
    /// <summary>Markazdan tashqari (test kodi bilan kirgan) ishtirokchilar natijalari.</summary>
    DbSet<ExternalTestScore> ExternalTestScores { get; }
    DbSet<TestBotSession> TestBotSessions { get; }
    /// <summary>Test sertifikati uchun Word andozalari.</summary>
    DbSet<TestCertificateTemplate> TestCertificateTemplates { get; }
    /// <summary>Test natijasi bo'yicha berilgan sertifikatlar.</summary>
    DbSet<TestCertificate> TestCertificates { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<LessonNote> LessonNotes { get; }
    DbSet<LessonReschedule> LessonReschedules { get; }
    DbSet<AbsenceReason> AbsenceReasons { get; }
    DbSet<GradingCriterion> GradingCriteria { get; }
    DbSet<GroupGradingCriterion> GroupGradingCriteria { get; }
    DbSet<CriterionGrade> CriterionGrades { get; }
    /// <summary>Ballni qo'lda tuzatish yozuvlari (per-guruh, faqat admin/superadmin).</summary>
    DbSet<StudentBallAdjustment> StudentBallAdjustments { get; }
    DbSet<FinanceTransaction> FinanceTransactions { get; }
    DbSet<MonthlyCharge> MonthlyCharges { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<CenterMeta> CenterMeta { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<Broadcast> Broadcasts { get; }
    DbSet<PushMessage> PushMessages { get; }
    DbSet<TelegramRegistration> TelegramRegistrations { get; }
    DbSet<LoginOtpCode> LoginOtpCodes { get; }
    DbSet<BotUser> BotUsers { get; }
    DbSet<TelegramGroup> TelegramGroups { get; }

    // Topshiriqlar moduli (Kanban)
    DbSet<WorkTaskBoard> WorkTaskBoards { get; }
    DbSet<WorkTaskColumn> WorkTaskColumns { get; }
    DbSet<WorkTask> WorkTasks { get; }
    DbSet<WorkTaskItem> WorkTaskItems { get; }
    DbSet<WorkTaskComment> WorkTaskComments { get; }
    DbSet<WorkTaskEvent> WorkTaskEvents { get; }
    DbSet<BotSupportMessage> BotSupportMessages { get; }
    DbSet<UserSettings> UserSettings { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<UserNotification> UserNotifications { get; }
    DbSet<ContractTemplate> ContractTemplates { get; }
    DbSet<Contract> Contracts { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Feedback> Feedbacks { get; }
    /// <summary>O'quvchining o'qituvchi haqidagi fikrlari (admin yozadi, AI tahlil manbai).</summary>
    DbSet<TeacherReview> TeacherReviews { get; }

    // Landing CMS
    DbSet<LandingTeacher> LandingTeachers { get; }
    DbSet<LandingCertificate> LandingCertificates { get; }
    DbSet<LandingTestimonial> LandingTestimonials { get; }
    DbSet<LandingFaq> LandingFaqs { get; }

    // Tuman + maktab (o'quvchi formasi uchun, sozlamalardan boshqariladi)
    DbSet<District> Districts { get; }
    DbSet<School> Schools { get; }

    // AI tekshiruv (Speaking/Writing) + o'quvchi ruxsati
    DbSet<AiCheck> AiChecks { get; }
    DbSet<StudentAiAccess> StudentAiAccesses { get; }

    // Kitoblar sotuvi — ombor, harakatlar tarixi, botdan tushgan buyurtmalar, bot savdo sessiyasi
    DbSet<Book> Books { get; }
    DbSet<BookStockMove> BookStockMoves { get; }
    DbSet<BookOrder> BookOrders { get; }
    DbSet<BookBotSession> BookBotSessions { get; }

    // Karyera (Intellect Career) — vakansiyalar + nomzod arizalari
    DbSet<CareerAbout> CareerAbout { get; }
    DbSet<Vacancy> Vacancies { get; }
    DbSet<JobApplication> JobApplications { get; }
    DbSet<JobApplicationEvent> JobApplicationEvents { get; }
    DbSet<CareerBotUser> CareerBotUsers { get; }

    // LMS (Ta'lim)

    // O'quv dasturi (standalone, Kurs/Subject'dan mustaqil) + Kurs↔Dastur ko'p-ko'pga bog'lanishi
    DbSet<Curriculum> Curricula { get; }
    DbSet<SubjectCurriculum> SubjectCurricula { get; }

    // Dastur sillabusi (Modul → Mavzu → Dars → Topshiriq) + o'quvchi progressi
    DbSet<CourseModule> CourseModules { get; }
    DbSet<CourseTopic> CourseTopics { get; }
    DbSet<CourseLesson> CourseLessons { get; }
    DbSet<CourseItem> CourseItems { get; }
    DbSet<CourseQuestion> CourseQuestions { get; }
    DbSet<CourseProgress> CourseProgresses { get; }
    DbSet<GroupCurriculumLog> GroupCurriculumLogs { get; }

    // Amal sabablari (muzlatish/o'chirish/sinovga qaytarish/lid/guruh)
    DbSet<ActionReason> ActionReasons { get; }
    DbSet<LeadSource> LeadSources { get; }

    // Arxiv — o'chirilgan entity'larning JSON suratlari (ko'rish/tiklash uchun)
    DbSet<ArchivedRecord> ArchivedRecords { get; }

    // Daraja testi (placement test → lid)
    DbSet<LevelTest> LevelTests { get; }
    DbSet<LevelTestQuestion> LevelTestQuestions { get; }
    DbSet<LevelTestBand> LevelTestBands { get; }
    DbSet<LevelTestSubmission> LevelTestSubmissions { get; }
    DbSet<LevelTestInvite> LevelTestInvites { get; }

    // Lid formalari (kanal → ommaviy forma → lid)
    DbSet<LeadForm> LeadForms { get; }
    DbSet<LeadFormField> LeadFormFields { get; }
    DbSet<LeadEntryField> LeadEntryFields { get; }
    DbSet<LeadFormSubmission> LeadFormSubmissions { get; }

    // Support o'qituvchi bo'sh vaqt slotlari + bron
    DbSet<SupportSlot> SupportSlots { get; }

    // Sertifikatlar
    DbSet<CertificateTemplate> CertificateTemplates { get; }
    DbSet<StudentCertificate> StudentCertificates { get; }
    DbSet<CertificateVerification> CertificateVerifications { get; }

    // O'quvchi AI tahlili (Gemini)
    DbSet<StudentAiAnalysis> StudentAiAnalyses { get; }

    // O'qituvchi AI tahlili (Gemini)
    DbSet<TeacherAiAnalysis> TeacherAiAnalyses { get; }

    // Guruh AI tahlili (Gemini)
    DbSet<GroupAiAnalysis> GroupAiAnalyses { get; }
    // Voronka AI tahlili — lid formalari va daraja testlari (bitta jadval, `Kind` bilan)
    DbSet<FunnelAiAnalysis> FunnelAiAnalyses { get; }

    // Markaz (butun o'quv markazi) kunlik AI tahlili (Gemini)
    DbSet<CenterAiAnalysis> CenterAiAnalyses { get; }

    // O'quv xonalari
    DbSet<Room> Rooms { get; }

    // Eskiz.uz SMS — yuborish partiyalari, raqam bo'yicha jurnal, andozalar
    DbSet<SmsBatch> SmsBatches { get; }
    DbSet<SmsLog> SmsLogs { get; }
    DbSet<SmsTemplate> SmsTemplates { get; }

    // Avto-xabarlar (yagona model: SMS+Push+Telegram) — Xabarlar → Avto xabarlar
    DbSet<AutoMessageRule> AutoMessageRules { get; }

    // Call Center — qo'ng'iroqlar jurnali
    DbSet<Call> Calls { get; }

    // CTI (Local Call) — Android agent-ilovalar bilan lokal call-center
    DbSet<CtiAgent> CtiAgents { get; }
    DbSet<CtiCallRecord> CtiCallRecords { get; }
    DbSet<CtiCallEvent> CtiCallEvents { get; }
    DbSet<CtiCommandLog> CtiCommandLogs { get; }

    // Bog'lanish kerak (follow-up navbati)
    DbSet<ContactStage> ContactStages { get; }
    DbSet<ContactRequest> ContactRequests { get; }
    DbSet<ContactAttempt> ContactAttempts { get; }
    // "Bog'lanish kerak" hisobotining AI tahlili (davr bo'yicha)
    DbSet<ContactAiAnalysis> ContactAiAnalyses { get; }

    // Yuz bilan kirish (o'quvchi mobil ilovasi) — etalon vektor, urinishlar, ishonchli qurilmalar
    DbSet<StudentFaceProfile> StudentFaceProfiles { get; }
    DbSet<LoginFaceCheck> LoginFaceChecks { get; }
    DbSet<TrustedDevice> TrustedDevices { get; }
    DbSet<FaceChallenge> FaceChallenges { get; }

    // Marketing: Instagram AI agenti — ulangan akkaunt, webhook navbati, suhbat/xabarlar,
    // kalit so'z qoidalari, bilim bazasi va OAuth `state` (bir martalik).
    DbSet<IgAccount> IgAccounts { get; }
    DbSet<IgWebhookEvent> IgWebhookEvents { get; }
    DbSet<IgConversation> IgConversations { get; }
    DbSet<IgMessage> IgMessages { get; }
    DbSet<IgAutoRule> IgAutoRules { get; }
    DbSet<IgIceBreaker> IgIceBreakers { get; }
    DbSet<IgKnowledge> IgKnowledges { get; }
    DbSet<IgOAuthState> IgOAuthStates { get; }

    // Marketing: REKLAMA LIDLARI (Meta Lead Ads) — lid olinadigan Facebook Page (token bilan)
    // va reklama formasidan kelgan lidlar.
    DbSet<IgAdPage> IgAdPages { get; }
    DbSet<IgAdLead> IgAdLeads { get; }

    // Marketing: REKLAMA STATISTIKASI (Meta Ads Insights) — ulangan reklama akkaunti (ads_read
    // tokeni bilan), kampaniya/adset/e'lon iyerarxiyasi va kunlik faktlar.
    DbSet<IgAdAccount> IgAdAccounts { get; }
    DbSet<IgAdEntity> IgAdEntities { get; }
    DbSet<IgAdInsight> IgAdInsights { get; }

    // Marketing: kontent rejalashtirish navbati va CAPI (lid sifatini Meta'ga qaytarish) navbati.
    DbSet<IgScheduledPost> IgScheduledPosts { get; }
    DbSet<IgCapiEvent> IgCapiEvents { get; }

    // KPI (xodimlar samaradorligi)
    DbSet<KpiProfile> KpiProfiles { get; }
    DbSet<KpiProfileSalary> KpiProfileSalaries { get; }
    DbSet<KpiRuleSet> KpiRuleSets { get; }
    DbSet<KpiTicket> KpiTickets { get; }
    DbSet<ChecklistTemplate> ChecklistTemplates { get; }
    DbSet<ChecklistTemplateItem> ChecklistTemplateItems { get; }
    DbSet<ChecklistEntry> ChecklistEntries { get; }
    DbSet<KpiMonthSnapshot> KpiMonthSnapshots { get; }
    DbSet<KpiMonthResult> KpiMonthResults { get; }
    DbSet<StudentExtension> StudentExtensions { get; }

    int SaveChanges();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
