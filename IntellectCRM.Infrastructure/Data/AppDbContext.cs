using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;

namespace IntellectCRM.Infrastructure.Data;

/// <summary>
/// Markaz ma'lumotlar bazasi (bitta o'quv markazi — multi-tenant emas).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    // Maktab ma'lumotlari
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherAttendance> TeacherAttendances => Set<TeacherAttendance>();
    public DbSet<TurnstileEvent> TurnstileEvents => Set<TurnstileEvent>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Group> Classes => Set<Group>();
    public DbSet<GroupTeacherAssignment> GroupTeacherAssignments => Set<GroupTeacherAssignment>();
    public DbSet<SubstituteTeacherAssignment> SubstituteTeacherAssignments => Set<SubstituteTeacherAssignment>();
    public DbSet<RetentionBonusAward> RetentionBonusAwards => Set<RetentionBonusAward>();
    public DbSet<RetentionBonusShare> RetentionBonusShares => Set<RetentionBonusShare>();
    public DbSet<RetentionBonusTrack> RetentionBonusTracks => Set<RetentionBonusTrack>();
    public DbSet<StudentGroup> StudentGroups => Set<StudentGroup>();
    public DbSet<StudentNote> StudentNotes => Set<StudentNote>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadStage> LeadStages => Set<LeadStage>();
    public DbSet<LeadEvent> LeadEvents => Set<LeadEvent>();
    public DbSet<LeadTelegramMessage> LeadTelegramMessages => Set<LeadTelegramMessage>();
    public DbSet<TrialLesson> TrialLessons => Set<TrialLesson>();
    public DbSet<TestResult> TestResults => Set<TestResult>();
    public DbSet<TestScore> TestScores => Set<TestScore>();
    public DbSet<ExternalTestScore> ExternalTestScores => Set<ExternalTestScore>();
    public DbSet<TestCertificateTemplate> TestCertificateTemplates => Set<TestCertificateTemplate>();
    public DbSet<TestCertificate> TestCertificates => Set<TestCertificate>();
    public DbSet<TestBotSession> TestBotSessions => Set<TestBotSession>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<LessonNote> LessonNotes => Set<LessonNote>();
    public DbSet<LessonReschedule> LessonReschedules => Set<LessonReschedule>();
    public DbSet<AbsenceReason> AbsenceReasons => Set<AbsenceReason>();
    public DbSet<GradingCriterion> GradingCriteria => Set<GradingCriterion>();
    public DbSet<GroupGradingCriterion> GroupGradingCriteria => Set<GroupGradingCriterion>();
    public DbSet<CriterionGrade> CriterionGrades => Set<CriterionGrade>();
    public DbSet<StudentBallAdjustment> StudentBallAdjustments => Set<StudentBallAdjustment>();
    public DbSet<FinanceTransaction> FinanceTransactions => Set<FinanceTransaction>();
    public DbSet<MonthlyCharge> MonthlyCharges => Set<MonthlyCharge>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CenterMeta> CenterMeta => Set<CenterMeta>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();
    public DbSet<PushMessage> PushMessages => Set<PushMessage>();
    public DbSet<TelegramRegistration> TelegramRegistrations => Set<TelegramRegistration>();
    public DbSet<LoginOtpCode> LoginOtpCodes => Set<LoginOtpCode>();
    public DbSet<BotUser> BotUsers => Set<BotUser>();
    public DbSet<TelegramGroup> TelegramGroups => Set<TelegramGroup>();
    public DbSet<StaffTask> StaffTasks => Set<StaffTask>();
    public DbSet<StaffTaskLog> StaffTaskLogs => Set<StaffTaskLog>();
    public DbSet<BotSupportMessage> BotSupportMessages => Set<BotSupportMessage>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<TeacherReview> TeacherReviews => Set<TeacherReview>();

    // LMS (Ta'lim)

    // O'quv dasturi (standalone) + Kurs↔Dastur ko'p-ko'pga bog'lanishi
    public DbSet<Curriculum> Curricula => Set<Curriculum>();
    public DbSet<SubjectCurriculum> SubjectCurricula => Set<SubjectCurriculum>();

    // Dastur sillabusi (Modul → Mavzu → Dars → Topshiriq) + o'quvchi progressi
    public DbSet<CourseModule> CourseModules => Set<CourseModule>();
    public DbSet<CourseTopic> CourseTopics => Set<CourseTopic>();
    public DbSet<CourseLesson> CourseLessons => Set<CourseLesson>();
    public DbSet<CourseItem> CourseItems => Set<CourseItem>();
    public DbSet<CourseQuestion> CourseQuestions => Set<CourseQuestion>();
    public DbSet<CourseProgress> CourseProgresses => Set<CourseProgress>();
    /// <summary>O'quvchi topshiriqni ishlagan urinishlari (natija + javoblar tarixi).</summary>
    public DbSet<CourseItemAttempt> CourseItemAttempts => Set<CourseItemAttempt>();
    public DbSet<GroupCurriculumLog> GroupCurriculumLogs => Set<GroupCurriculumLog>();

    // Amal sabablari (muzlatish/o'chirish/sinovga qaytarish/lid/guruh)
    public DbSet<ActionReason> ActionReasons => Set<ActionReason>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();

    // Arxiv — o'chirilgan entity'larning JSON suratlari (ko'rish/tiklash uchun)
    public DbSet<ArchivedRecord> ArchivedRecords => Set<ArchivedRecord>();

    // Daraja testi (placement test → lid)
    public DbSet<LevelTest> LevelTests => Set<LevelTest>();
    public DbSet<LevelTestQuestion> LevelTestQuestions => Set<LevelTestQuestion>();
    public DbSet<LevelTestBand> LevelTestBands => Set<LevelTestBand>();
    public DbSet<LevelTestSubmission> LevelTestSubmissions => Set<LevelTestSubmission>();
    public DbSet<LevelTestInvite> LevelTestInvites => Set<LevelTestInvite>();

    // Lid formalari (kanal → ommaviy forma → lid)
    public DbSet<LeadForm> LeadForms => Set<LeadForm>();
    public DbSet<LeadFormField> LeadFormFields => Set<LeadFormField>();
    public DbSet<LeadFormSubmission> LeadFormSubmissions => Set<LeadFormSubmission>();

    // Support o'qituvchi bo'sh vaqt slotlari + bron
    public DbSet<SupportSlot> SupportSlots => Set<SupportSlot>();

    // Sertifikatlar
    public DbSet<CertificateTemplate> CertificateTemplates => Set<CertificateTemplate>();
    public DbSet<StudentCertificate> StudentCertificates => Set<StudentCertificate>();
    public DbSet<CertificateVerification> CertificateVerifications => Set<CertificateVerification>();

    // O'quvchi AI tahlili (Gemini)
    public DbSet<StudentAiAnalysis> StudentAiAnalyses => Set<StudentAiAnalysis>();

    // O'qituvchi AI tahlili (Gemini)
    public DbSet<TeacherAiAnalysis> TeacherAiAnalyses => Set<TeacherAiAnalysis>();

    // Landing CMS
    public DbSet<LandingTeacher> LandingTeachers => Set<LandingTeacher>();
    public DbSet<LandingCertificate> LandingCertificates => Set<LandingCertificate>();
    public DbSet<LandingTestimonial> LandingTestimonials => Set<LandingTestimonial>();
    public DbSet<LandingFaq> LandingFaqs => Set<LandingFaq>();

    // Guruh AI tahlili (Gemini)
    public DbSet<GroupAiAnalysis> GroupAiAnalyses => Set<GroupAiAnalysis>();

    // Voronka AI tahlili (lid formalari va daraja testlari — bitta jadval, `Kind` bilan ajratiladi)
    public DbSet<FunnelAiAnalysis> FunnelAiAnalyses => Set<FunnelAiAnalysis>();

    // Markaz kunlik AI tahlili (Gemini)
    public DbSet<CenterAiAnalysis> CenterAiAnalyses => Set<CenterAiAnalysis>();

    // Xodim roli shablonlari
    public DbSet<StaffRoleTemplate> StaffRoleTemplates => Set<StaffRoleTemplate>();

    // O'quv xonalari
    public DbSet<Room> Rooms => Set<Room>();

    // Eskiz.uz SMS
    public DbSet<SmsBatch> SmsBatches => Set<SmsBatch>();
    public DbSet<SmsLog> SmsLogs => Set<SmsLog>();
    public DbSet<SmsTemplate> SmsTemplates => Set<SmsTemplate>();

    // Avto-xabarlar (yagona model: SMS+Push+Telegram)
    public DbSet<AutoMessageRule> AutoMessageRules => Set<AutoMessageRule>();

    // Call Center — qo'ng'iroqlar jurnali
    public DbSet<Call> Calls => Set<Call>();

    // CTI (Local Call) — Android agent-ilovalar (xodim telefonlari) bilan lokal call-center
    public DbSet<CtiAgent> CtiAgents => Set<CtiAgent>();
    public DbSet<CtiCallRecord> CtiCallRecords => Set<CtiCallRecord>();
    public DbSet<CtiCallEvent> CtiCallEvents => Set<CtiCallEvent>();
    public DbSet<CtiCommandLog> CtiCommandLogs => Set<CtiCommandLog>();

    // Tuman + maktab
    public DbSet<District> Districts => Set<District>();
    public DbSet<School> Schools => Set<School>();

    // AI tekshiruv (Speaking/Writing) + o'quvchi ruxsati
    public DbSet<AiCheck> AiChecks => Set<AiCheck>();
    public DbSet<StudentAiAccess> StudentAiAccesses => Set<StudentAiAccess>();

    // Kitoblar sotuvi (ombor + botdan tushgan buyurtmalar)
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookStockMove> BookStockMoves => Set<BookStockMove>();
    public DbSet<BookOrder> BookOrders => Set<BookOrder>();
    public DbSet<BookBotSession> BookBotSessions => Set<BookBotSession>();

    // Karyera (Intellect Career) — vakansiyalar + nomzod arizalari (alohida bot + Mini App)
    public DbSet<CareerAbout> CareerAbout => Set<CareerAbout>();
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<JobApplicationEvent> JobApplicationEvents => Set<JobApplicationEvent>();
    public DbSet<CareerBotUser> CareerBotUsers => Set<CareerBotUser>();

    /* ---------- Bog'lanish kerak (follow-up navbati) ---------- */
    public DbSet<ContactStage> ContactStages => Set<ContactStage>();
    public DbSet<ContactRequest> ContactRequests => Set<ContactRequest>();
    public DbSet<ContactAttempt> ContactAttempts => Set<ContactAttempt>();
    public DbSet<ContactAiAnalysis> ContactAiAnalyses => Set<ContactAiAnalysis>();

    /* ---------- Yuz bilan kirish (o'quvchi mobil ilovasi) ---------- */
    public DbSet<StudentFaceProfile> StudentFaceProfiles => Set<StudentFaceProfile>();
    public DbSet<LoginFaceCheck> LoginFaceChecks => Set<LoginFaceCheck>();
    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();
    public DbSet<FaceChallenge> FaceChallenges => Set<FaceChallenge>();

    /* ---------- Marketing: Instagram AI agenti ---------- */
    public DbSet<IgAccount> IgAccounts => Set<IgAccount>();
    public DbSet<IgWebhookEvent> IgWebhookEvents => Set<IgWebhookEvent>();
    public DbSet<IgConversation> IgConversations => Set<IgConversation>();
    public DbSet<IgMessage> IgMessages => Set<IgMessage>();
    public DbSet<IgAutoRule> IgAutoRules => Set<IgAutoRule>();
    public DbSet<IgIceBreaker> IgIceBreakers => Set<IgIceBreaker>();
    public DbSet<IgKnowledge> IgKnowledges => Set<IgKnowledge>();
    public DbSet<IgOAuthState> IgOAuthStates => Set<IgOAuthState>();

    /* ---------- Marketing: reklama lidlari (Meta Lead Ads) ---------- */
    public DbSet<IgAdPage> IgAdPages => Set<IgAdPage>();
    public DbSet<IgAdLead> IgAdLeads => Set<IgAdLead>();

    /* ---------- Marketing: reklama statistikasi (Meta Ads Insights) ---------- */
    public DbSet<IgAdAccount> IgAdAccounts => Set<IgAdAccount>();
    public DbSet<IgAdEntity> IgAdEntities => Set<IgAdEntity>();
    public DbSet<IgAdInsight> IgAdInsights => Set<IgAdInsight>();

    /* ---------- Marketing: kontent rejalashtirish va CAPI ---------- */
    public DbSet<IgScheduledPost> IgScheduledPosts => Set<IgScheduledPost>();
    public DbSet<IgCapiEvent> IgCapiEvents => Set<IgCapiEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // SQL Server: indeksda qatnashadigan string ustunlar default `nvarchar(max)` bo'lib
        // indekslanmaydi — ularga aniq maksimal uzunlik beramiz (nvarchar(N)). Qolgan matn
        // maydonlari nvarchar(max) bo'lib qoladi (kesilmaydi). Unicode (o'zbek/kirill) — nvarchar
        // tabiatan qo'llab-quvvatlaydi, alohida charset sozlash shart emas.
        b.Entity<AppUser>().Property(u => u.Email).HasMaxLength(256);
        foreach (var (type, prop) in new (Type, string)[]
        {
            (typeof(JournalEntry), "ClassId"), (typeof(JournalEntry), "SubjectId"),
            (typeof(LessonNote), "ClassId"), (typeof(LessonNote), "SubjectId"),
            (typeof(StudentGroup), "StudentId"), (typeof(StudentGroup), "GroupId"),
            (typeof(LeadEvent), "LeadId"), (typeof(TrialLesson), "LeadId"),
            (typeof(FinanceTransaction), "Date"),
            (typeof(MonthlyCharge), "StudentId"), (typeof(MonthlyCharge), "Month"), (typeof(MonthlyCharge), "GroupId"),
            (typeof(AuditLog), "EntityType"), (typeof(AuditLog), "EntityId"), (typeof(AuditLog), "Timestamp"),
            (typeof(AuditLog), "StudentId"), (typeof(AuditLog), "TeacherId"),
            (typeof(ChatMessage), "ClassName"),
            (typeof(Broadcast), "ClassName"),
            (typeof(TelegramRegistration), "StudentId"),
            (typeof(DeviceToken), "Token"), (typeof(DeviceToken), "UserId"),
            (typeof(ContractTemplate), "Target"),
            (typeof(Contract), "Target"), (typeof(Contract), "RecipientKey"),
            (typeof(Feedback), "Status"),
        })
            b.Entity(type).Property(prop).HasMaxLength(200);

        // Login (Email) unikal — DB darajasidagi unique indeks TOCTOU poyga holatida ham dublikatni
        // bloklaydi (parallel ro'yxatdan o'tish login'ni buzmasin).
        b.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();

        // REFRESH TOKEN — hash bo'yicha topiladi (unikal: dedup + tez qidiruv), foydalanuvchi va
        // family bo'yicha bekor qilinadi. Indekslanadigan matn ustunlariga uzunlik beriladi
        // (loyihadagi umumiy qoida — SQL Server nvarchar(max) ni indekslay olmaydi).
        b.Entity<RefreshToken>().Property(r => r.TokenHash).HasMaxLength(128);
        b.Entity<RefreshToken>().Property(r => r.UserId).HasMaxLength(200);
        b.Entity<RefreshToken>().Property(r => r.ReplacedByHash).HasMaxLength(128);
        b.Entity<RefreshToken>().Property(r => r.CreatedByIp).HasMaxLength(64);
        b.Entity<RefreshToken>().Property(r => r.UserAgent).HasMaxLength(256);
        b.Entity<RefreshToken>().HasIndex(r => r.TokenHash).IsUnique();
        b.Entity<RefreshToken>().HasIndex(r => r.UserId);
        b.Entity<RefreshToken>().HasIndex(r => r.FamilyId);

        // Pul maydonlari uchun aniqlik (SQL Server decimal(18,2))
        b.Entity<Student>().Property(s => s.Balance).HasPrecision(18, 2);
        b.Entity<Student>().Property(s => s.DiscountAmount).HasPrecision(18, 2);
        b.Entity<Group>().Property(c => c.MonthlyFee).HasPrecision(18, 2);
        b.Entity<Group>().Property(c => c.TeacherSalaryPercent).HasPrecision(18, 2);
        b.Entity<Group>().Property(c => c.TeacherSalaryFixed).HasPrecision(18, 2);
        b.Entity<Subject>().Property(s => s.Price).HasPrecision(18, 2);
        b.Entity<FinanceTransaction>().Property(t => t.Amount).HasPrecision(18, 2);
        b.Entity<MonthlyCharge>().Property(c => c.Amount).HasPrecision(18, 2);
        b.Entity<MonthlyCharge>().Property(c => c.Discount).HasPrecision(18, 2);
        b.Entity<Teacher>().Property(t => t.Salary).HasPrecision(18, 2);
        b.Entity<Teacher>().Property(t => t.BonusPct).HasPrecision(18, 2);
        b.Entity<Teacher>().Property(t => t.SalaryPercent).HasPrecision(18, 2);
        b.Entity<CenterMeta>().Property(m => m.SalaryRate1).HasPrecision(18, 2);
        b.Entity<CenterMeta>().Property(m => m.SalaryRate2).HasPrecision(18, 2);
        b.Entity<CenterMeta>().Property(m => m.SalaryRateMutaxasis).HasPrecision(18, 2);
        b.Entity<CenterMeta>().Property(m => m.SalaryRateOliy).HasPrecision(18, 2);

        // Tez-tez ishlatiladigan filtrlar uchun indekslar
        b.Entity<JournalEntry>().HasIndex(e => new { e.ClassId, e.SubjectId, e.Quarter });
        b.Entity<LessonNote>().HasIndex(e => new { e.ClassId, e.SubjectId, e.Quarter });
        b.Entity<LessonReschedule>().Property(e => e.ClassId).HasMaxLength(200);
        b.Entity<LessonReschedule>().HasIndex(e => e.ClassId);
        b.Entity<Group>().HasIndex(c => c.TeacherId);
        b.Entity<StudentGroup>().HasIndex(sg => new { sg.StudentId, sg.GroupId }).IsUnique();
        b.Entity<StudentGroup>().HasIndex(sg => sg.GroupId);
        b.Entity<StudentGroup>().HasIndex(sg => new { sg.StudentId, sg.IsActive });

        // ---------- PERFORMANS INDEKSLARI (auth + qidiruv) ----------
        // Student/Teacher.UserId — HAR bir API so'rovida `OnTokenValidated` shu ustun bo'yicha
        // profilni qidiradi (token → o'quvchi/o'qituvchi bog'lash). Indekssiz bu har so'rovda
        // seq-scan edi. Indeksda qatnashgani uchun uzunlik ham beriladi (faylning boshidagi
        // umumiy qoida).
        b.Entity<Student>().Property(s => s.UserId).HasMaxLength(200);
        b.Entity<Teacher>().Property(t => t.UserId).HasMaxLength(200);
        b.Entity<Student>().HasIndex(s => s.UserId);
        b.Entity<Teacher>().HasIndex(t => t.UserId);
        // Bildirishnomalar ro'yxati doim "foydalanuvchi + eng yangisi tepada" kesimida o'qiladi.
        b.Entity<UserNotification>().Property(n => n.UserId).HasMaxLength(200);
        b.Entity<UserNotification>().HasIndex(n => new { n.UserId, n.CreatedAt });
        // ⚠️ E'lon (broadcast) statistikasi `PushMessageId` bo'yicha o'qiladi — bu ustun
        // yuqoridagi (UserId, CreatedAt) indeksiga KIRMAYDI. Prod o'lchovi: shu sabab
        // `UserNotifications` dan 161 mln qator sequential scan bilan o'qilgan edi.
        b.Entity<UserNotification>().HasIndex(n => n.PushMessageId);
        // O'quvchilar ro'yxati deyarli har so'rovda arxivlanganlarni chiqarib tashlaydi.
        b.Entity<Student>().HasIndex(s => s.IsArchived);
        // ⚠️ Quyidagilar prod statistikasi (pg_stat_user_tables.seq_tup_read) asosida qo'shildi:
        // o'quvchi profili va moliya sahifalari shu ustunlar bo'yicha filtrlaydi, lekin
        // munosabatlar haqiqiy FK emas (oddiy `string` ustun) — EF avtomatik indeks yaratmaydi.
        b.Entity<JournalEntry>().HasIndex(e => e.StudentId);   // 21 mln qator seq scan
        b.Entity<JournalEntry>().HasIndex(e => e.Date);
        b.Entity<FinanceTransaction>().HasIndex(t => t.StudentId);  // 8.9 mln qator seq scan
        b.Entity<FinanceTransaction>().HasIndex(t => t.TeacherId);  // maosh tarixi
        // ⚠️ `TurnstileEvents` da UMUMAN indeks yo'q edi. O'quvchi profilidagi turniket tarixi
        // aynan (qurilma ID + sana) kesimida o'qiladi (`DeviceUserId == ... && EventAt LIKE 'oy%'`),
        // hodisalar esa har sinxronda o'sib boradi — indekssiz bu butun jadval bo'ylab skan edi.
        b.Entity<TurnstileEvent>().HasIndex(e => new { e.DeviceUserId, e.EventAt });
        // pg_trgm + GIN trigram indekslar — qidiruv endpointining ILIKE '%...%' so'rovlari
        // indeksdan foydalanishi uchun (oddiy b-tree o'rtadan boshlangan LIKE'ni qo'llamaydi).
        // ⚠️ Bular Npgsql'ga xos annotatsiyalar — SQLite (testlar) ularni e'tiborsiz qoldiradi:
        // indekslar oddiy b-tree bo'lib yaratiladi, extension esa umuman yozilmaydi.
        b.HasPostgresExtension("pg_trgm");
        b.Entity<Student>().HasIndex(s => s.FullName).HasMethod("gin").HasOperators("gin_trgm_ops");
        b.Entity<Student>().HasIndex(s => s.Phone).HasMethod("gin").HasOperators("gin_trgm_ops");
        b.Entity<Student>().HasIndex(s => s.ParentPhone).HasMethod("gin").HasOperators("gin_trgm_ops");
        b.Entity<Student>().HasIndex(s => s.FatherPhone).HasMethod("gin").HasOperators("gin_trgm_ops");
        b.Entity<Student>().HasIndex(s => s.MotherPhone).HasMethod("gin").HasOperators("gin_trgm_ops");

        b.Entity<LeadEvent>().HasIndex(e => e.LeadId);
        // Lid kartasi HAR CHAT uchun bitta: unikal indeks bir guruhda ikkinchi karta paydo
        // bo'lishiga yo'l qo'ymaydi (aks holda ikkalasi ham yangilanib, guruhda dubl ko'rinardi).
        // LeadId indeksda qatnashgani uchun uzunligi cheklanadi (SQL Server nvarchar(max) ni
        // indekslay olmaydi — yuqoridagi ro'yxat bilan bir xil qoida).
        b.Entity<LeadTelegramMessage>().Property(m => m.LeadId).HasMaxLength(200);
        b.Entity<LeadTelegramMessage>().HasIndex(m => new { m.LeadId, m.ChatId }).IsUnique();
        // Lid o'chirilsa uning karta yozuvlari ham BAZA darajasida o'chadi (cascade).
        // NEGA: tozalash ilgari butunlay `LeadNotifier.MarkDeletedAsync` ga tayanardi, u esa
        // xatoni JIM yutadi — SaveChanges yiqilsa yetim qatorlar ABADIY qolardi. Yetim qator
        // zararsiz emas: (LeadId, ChatId) unikal bo'lgani uchun o'sha lid id qayta ishlatilganda
        // (import/tiklash) yangi karta insert'i 23505 bilan yiqilardi. Sxema o'zi kafolatlasin.
        // ⚠️ Navigatsiya xossasi ATAYIN yo'q (`HasOne<Lead>()` navigatsiyasiz shakli) — u DTO
        // seriyalash va `Include` xulqiga ta'sir qilishi mumkin edi.
        b.Entity<LeadTelegramMessage>()
            .HasOne<Lead>()
            .WithMany()
            .HasForeignKey(m => m.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
        // Lid TELEFON KALITI (oxirgi 9 raqam) — "shu odamning lidi bormi" savolini SQL tomonda
        // hal qiladi (`LeadIntake.FindByPhoneAsync`). Qiymatni SaveChanges o'zi yozadi.
        b.Entity<Lead>().Property(l => l.PhoneKey).HasMaxLength(16);
        b.Entity<Lead>().HasIndex(l => l.PhoneKey);
        b.Entity<StudentNote>().HasIndex(n => n.StudentId);
        b.Entity<TrialLesson>().HasIndex(t => t.LeadId);
        b.Entity<FinanceTransaction>().HasIndex(t => t.Date);
        // Per-guruh billing: har (o'quvchi, guruh, oy) uchun bitta hisob.
        b.Entity<MonthlyCharge>().HasIndex(c => new { c.StudentId, c.GroupId, c.Month }).IsUnique();
        b.Entity<GroupCurriculumLog>().HasIndex(g => new { g.GroupId, g.ItemId });
        b.Entity<CourseQuestion>().HasIndex(q => q.ItemId);
        b.Entity<GroupGradingCriterion>().HasIndex(g => g.GroupId);
        b.Entity<GroupGradingCriterion>().HasIndex(g => new { g.GroupId, g.CriterionId }).IsUnique();
        b.Entity<CriterionGrade>().HasIndex(g => new { g.GroupId, g.StudentId, g.CriterionId, g.Date }).IsUnique();
        b.Entity<CriterionGrade>().HasIndex(g => new { g.GroupId, g.Date });
        // Ballni qo'lda tuzatish: hisob HAR DOIM (o'quvchi, guruh) kesimida o'qiladi.
        b.Entity<StudentBallAdjustment>().Property(a => a.StudentId).HasMaxLength(200);
        b.Entity<StudentBallAdjustment>().Property(a => a.GroupId).HasMaxLength(200);
        b.Entity<StudentBallAdjustment>().HasIndex(a => new { a.StudentId, a.GroupId });

        b.Entity<AuditLog>().HasIndex(a => new { a.EntityType, a.EntityId });
        // Bog'lanish kerak: navbat "holat + muddat" bo'yicha o'qiladi, hisobot esa KUN bo'yicha
        // guruhlanadi. Indeksdagi matn ustunlariga uzunlik beriladi (loyihadagi umumiy qoida).
        b.Entity<ContactRequest>().Property(c => c.Status).HasMaxLength(200);
        b.Entity<ContactRequest>().Property(c => c.DueDate).HasMaxLength(200);
        b.Entity<ContactRequest>().Property(c => c.StudentId).HasMaxLength(200);
        b.Entity<ContactRequest>().HasIndex(c => new { c.Status, c.DueDate });
        b.Entity<ContactRequest>().HasIndex(c => c.StudentId);
        b.Entity<ContactRequest>().Property(c => c.StageId).HasMaxLength(200);
        b.Entity<ContactStage>().Property(x => x.BaseStatus).HasMaxLength(200);
        b.Entity<ContactStage>().Property(x => x.Color).HasMaxLength(50);
        b.Entity<ContactAttempt>().Property(a => a.RequestId).HasMaxLength(200);
        b.Entity<ContactAttempt>().Property(a => a.Date).HasMaxLength(200);
        b.Entity<ContactAttempt>().HasIndex(a => a.RequestId);
        b.Entity<ContactAttempt>().HasIndex(a => a.Date);
        // AI tahlil DAVR bo'yicha saqlanadi — "shu davr uchun bugun tahlil bo'lganmi" savoli
        // uchun kalit (FromDate, ToDate, Date).
        b.Entity<ContactAiAnalysis>().Property(a => a.FromDate).HasMaxLength(10);
        b.Entity<ContactAiAnalysis>().Property(a => a.ToDate).HasMaxLength(10);
        b.Entity<ContactAiAnalysis>().Property(a => a.Date).HasMaxLength(10);
        b.Entity<ContactAiAnalysis>().HasIndex(a => new { a.FromDate, a.ToDate, a.Date });
        // YUZ BILAN KIRISH: etalon o'quvchi bo'yicha BITTA (unikal), urinishlar o'quvchi+vaqt
        // bo'yicha o'qiladi (soatlik chegara va admin ro'yxati), qurilma esa (foydalanuvchi,
        // qurilma) bo'yicha yagona bo'lishi SHART — aks holda bir telefon uchun bir nechta
        // "ishonchli" qator paydo bo'lib, bekor qilish ishonchsiz bo'lib qolardi.
        b.Entity<StudentFaceProfile>().Property(p => p.StudentId).HasMaxLength(200);
        b.Entity<StudentFaceProfile>().Property(p => p.ModelVersion).HasMaxLength(100);
        b.Entity<StudentFaceProfile>().HasIndex(p => p.StudentId).IsUnique();
        b.Entity<LoginFaceCheck>().Property(c => c.StudentId).HasMaxLength(200);
        b.Entity<LoginFaceCheck>().Property(c => c.UserId).HasMaxLength(200);
        b.Entity<LoginFaceCheck>().Property(c => c.CreatedAt).HasMaxLength(32);
        b.Entity<LoginFaceCheck>().Property(c => c.Status).HasMaxLength(32);
        b.Entity<LoginFaceCheck>().Property(c => c.DeviceId).HasMaxLength(200);
        b.Entity<LoginFaceCheck>().HasIndex(c => new { c.StudentId, c.CreatedAt });
        b.Entity<LoginFaceCheck>().HasIndex(c => c.Status);
        b.Entity<TrustedDevice>().Property(d => d.UserId).HasMaxLength(200);
        b.Entity<TrustedDevice>().Property(d => d.DeviceId).HasMaxLength(200);
        b.Entity<TrustedDevice>().HasIndex(d => new { d.UserId, d.DeviceId }).IsUnique();
        // Bir martalik tiriklik chaqiruvi (nonce). Nonce UNIKAL — takrorlanish "ikkinchi urinishda
        // eskisini ishlatish" yo'lini ochib qo'yardi.
        b.Entity<FaceChallenge>().Property(c => c.UserId).HasMaxLength(200);
        b.Entity<FaceChallenge>().Property(c => c.StudentId).HasMaxLength(200);
        b.Entity<FaceChallenge>().Property(c => c.Nonce).HasMaxLength(128);
        b.Entity<FaceChallenge>().Property(c => c.CreatedAt).HasMaxLength(32);
        b.Entity<FaceChallenge>().Property(c => c.ExpiresAt).HasMaxLength(32);
        b.Entity<FaceChallenge>().HasIndex(c => c.Nonce).IsUnique();
        b.Entity<FaceChallenge>().HasIndex(c => new { c.UserId, c.CreatedAt });
        // ⚠️ `UsedAt` — KONKURENTLIK TOKENI (`Book.Stock` bilan bir xil naqsh). Nonce'ni
        // "ishlatilgan" deb belgilash bilan saqlash oralig'ida `await` bor, ya'ni AYNI nonce bilan
        // yuborilgan ikki parallel `verify` ikkalasi ham o'tib ketardi. Endi EF
        // `UPDATE … WHERE Id=@id AND UsedAt IS NULL` yozadi va ikkinchisi
        // `DbUpdateConcurrencyException` oladi (`FaceLoginService.TrySaveAsync` ushlaydi).
        // Bu FAQAT model metadatasi — ustun/indeks o'zgarmaydi, MIGRATSIYA KERAK EMAS.
        b.Entity<FaceChallenge>().Property(c => c.UsedAt).IsConcurrencyToken();

        // INSTAGRAM AI AGENTI.
        // 1) `EventKey` — UNIKAL: Meta javob kechikkanda AYNI hodisani qayta yuboradi va unikal
        //    indekssiz mijoz bitta savoliga bir necha marta javob olardi. Dedupni kodda emas,
        //    BAZADA kafolatlaymiz (ikki nusxa parallel qabul qilsa ham).
        // 2) `Status` — fon xizmati har siklda faqat `pending` qatorlarni tanlaydi.
        // 3) `IgConversation.IgUserId` — har kiruvchi hodisada suhbat shu bo'yicha topiladi.
        // 4) `IgMessage.ConversationId` — suhbat lentasi va AI kontekst tarixi shu bo'yicha o'qiladi.
        // 5) `IgMessage.IgMessageId` va 6) `IgMessage.CommentId` — DEDUP so'rovi
        //    (`InstagramPipeline.AlreadyHandledAsync`) HAR kiruvchi hodisada shu ikki ustundan biri
        //    bo'yicha `Any(...)` qiladi. Indekssiz bu butun `IgMessages` bo'ylab seq-scan edi:
        //    jadval yozishmalar bilan o'sgani sayin har webhook sekinlashardi. Unikal EMAS —
        //    ikkala ustun ham bo'sh ("") bo'lishi mumkin (masalan chiquvchi xabar yoki izohsiz DM),
        //    unikal indeks esa shunday qatorlarni ikkinchisidan boshlab rad etardi.
        b.Entity<IgWebhookEvent>().Property(e => e.EventKey).HasMaxLength(200);
        b.Entity<IgWebhookEvent>().Property(e => e.Status).HasMaxLength(32);
        b.Entity<IgWebhookEvent>().HasIndex(e => e.EventKey).IsUnique();
        b.Entity<IgWebhookEvent>().HasIndex(e => e.Status);
        b.Entity<IgConversation>().Property(c => c.IgUserId).HasMaxLength(200);
        b.Entity<IgConversation>().HasIndex(c => c.IgUserId);
        b.Entity<IgMessage>().Property(m => m.ConversationId).HasMaxLength(200);
        b.Entity<IgMessage>().HasIndex(m => m.ConversationId);
        b.Entity<IgMessage>().Property(m => m.IgMessageId).HasMaxLength(200);
        b.Entity<IgMessage>().HasIndex(m => m.IgMessageId);
        b.Entity<IgMessage>().Property(m => m.CommentId).HasMaxLength(200);
        b.Entity<IgMessage>().HasIndex(m => m.CommentId);
        // FAQ tugmalari (ice breakers): uzunliklar Meta chegaralari bilan bir xil (savol 80,
        // javob 1000). Indeks ATAYIN yo'q — jadvalda ko'pi bilan 4 qator bo'ladi.
        b.Entity<IgIceBreaker>().Property(x => x.Question).HasMaxLength(80);
        b.Entity<IgIceBreaker>().Property(x => x.Answer).HasMaxLength(1000);

        // REKLAMA LIDLARI (Meta Lead Ads).
        // 1) `LeadgenId` — UNIKAL: Meta yetkazishni "at-least-once" kafolatlaydi va bir lidni
        //    qayta yuborishi mumkin. Navbat yozuvlari 30 kunda tozalanadi, ya'ni dedupning
        //    UZOQ MUDDATLI qavati aynan shu indeks — usiz eski hodisa qayta kelsa CRM'da
        //    ikkinchi lid ochilardi.
        // 2) `LeadId` — kanal tasnifi (`LeadOrigins`) va lid kartochkasidagi "qaysi reklamadan"
        //    ma'lumoti shu bo'yicha izlanadi.
        // 3) `CreatedTime` — hisobot va ro'yxat AYNAN shu ustun bo'yicha tartiblanadi.
        b.Entity<IgAdLead>().Property(l => l.LeadgenId).HasMaxLength(200);
        b.Entity<IgAdLead>().HasIndex(l => l.LeadgenId).IsUnique();
        b.Entity<IgAdLead>().Property(l => l.LeadId).HasMaxLength(200);
        b.Entity<IgAdLead>().HasIndex(l => l.LeadId);
        b.Entity<IgAdLead>().HasIndex(l => l.CreatedTime);
        b.Entity<IgAdPage>().Property(p => p.PageId).HasMaxLength(200);
        b.Entity<IgAdPage>().HasIndex(p => p.PageId);

        // BILIM BAZASI VEKTORLARI (RAG) va JAVOB SIFATI JURNALI.
        // ⚠️ `EmbeddingJson` ga UZUNLIK QO'YILMAYDI — 768 o'lchamli vektor ≈ 9–10 KB.
        // ⚠️ `IgMessage.CreatedAt` indeksi: `GET /quality` ham, mavjud `GET /analytics` ham
        //    sana ORALIG'I bo'yicha skan qiladi. Jadval yozishmalar bilan o'sadi, indekssiz
        //    har hisobot butun jadval bo'ylab seq-scan bo'lardi.
        b.Entity<IgKnowledge>().Property(k => k.EmbeddingModel).HasMaxLength(100);
        b.Entity<IgKnowledge>().Property(k => k.EmbeddedAt).HasMaxLength(40);
        b.Entity<IgKnowledge>().Property(k => k.EmbeddedHash).HasMaxLength(64);
        b.Entity<IgMessage>().Property(m => m.AiSuggestedText).HasMaxLength(1000);
        b.Entity<IgMessage>().Property(m => m.AiSuggestedIntent).HasMaxLength(40);
        b.Entity<IgMessage>().HasIndex(m => m.CreatedAt);

        // REKLAMA STATISTIKASI (Meta Ads Insights).
        // 1) `IgAdAccount.AdAccountId` — UNIKAL: akkaunt ikki marta ulanmasin. Admin "act_" ni
        //    yozmay qo'yishi juda ehtimolli, shuning uchun qiymat saqlashdan OLDIN prefiksli
        //    ko'rinishga keltiriladi — aks holda bitta akkaunt ikki xil satr bo'lib, indeks
        //    to'qnashuvni umuman ko'rmasdi.
        // 2) `IgAdEntity.ExternalId` — UNIKAL: iyerarxiya HAR sinxronizatsiyada qayta o'qiladi va
        //    upsert qilinadi. Usiz har kuni yangi nusxalar qo'shilib, hisobotdagi JOIN bitta
        //    e'lonni bir necha marta ko'rsatardi. Uch daraja bitta jadvalda tursa ham to'qnashuv
        //    yo'q — Meta id'lari tizim bo'ylab yagona.
        // 3) `(AdAccountId, Level)` — "shu akkauntning kampaniyalari" ro'yxati (ekranning asosiy
        //    so'rovi) shu ikkalasi bo'yicha filtrlanadi.
        // 4) `IgAdInsight` UNIKAL `(Level, ExternalId, StatDate, Platform)` — ENG MUHIM indeks.
        //    Meta oxirgi kunlarning raqamlarini keyin ham tuzatadi (atributsiya kechikadi),
        //    ya'ni o'sha kunlar QAYTA yuklanadi. Unikal kalitsiz har sinxronizatsiya sarfni
        //    ikkilantirib yuborardi va buni hisobotdan sezish deyarli imkonsiz bo'lardi.
        //    `Platform` kalitga ATAYIN kiradi: kesimli (`instagram`/`facebook`) va kesimsiz
        //    (`all`) qatorlar BIRGA saqlanadi.
        // 5) `StatDate` — barcha hisobotlar sana ORALIG'I bo'yicha o'qiladi (oylik/haftalik).
        b.Entity<IgAdAccount>().Property(a => a.AdAccountId).HasMaxLength(200);
        b.Entity<IgAdAccount>().HasIndex(a => a.AdAccountId).IsUnique();
        b.Entity<IgAdEntity>().Property(e => e.ExternalId).HasMaxLength(200);
        b.Entity<IgAdEntity>().HasIndex(e => e.ExternalId).IsUnique();
        b.Entity<IgAdEntity>().Property(e => e.AdAccountId).HasMaxLength(200);
        b.Entity<IgAdEntity>().Property(e => e.Level).HasMaxLength(32);
        b.Entity<IgAdEntity>().HasIndex(e => new { e.AdAccountId, e.Level });
        b.Entity<IgAdInsight>().Property(i => i.ExternalId).HasMaxLength(200);
        b.Entity<IgAdInsight>().Property(i => i.Level).HasMaxLength(32);
        b.Entity<IgAdInsight>().Property(i => i.StatDate).HasMaxLength(32);
        b.Entity<IgAdInsight>().Property(i => i.Platform).HasMaxLength(32);
        b.Entity<IgAdInsight>()
         .HasIndex(i => new { i.Level, i.ExternalId, i.StatDate, i.Platform }).IsUnique();
        b.Entity<IgAdInsight>().HasIndex(i => i.StatDate);

        // KONTENT REJALASHTIRISH.
        // 1) `Status` — worker har siklda faqat `scheduled` (va yarim qolgan `processing`)
        //    qatorlarni tanlaydi; chop etilganlar jadvalda abadiy to'planib boradi, ya'ni
        //    indekssiz skan jadval o'sgani sayin sekinlashardi (`IgWebhookEvent.Status` bilan
        //    bir xil sabab).
        // 2) `ScheduledAt` — "vaqti kelganlar" AYNAN shu ustun bo'yicha saralanib olinadi va
        //    kalendar ko'rinishi ham shu bo'yicha o'qiladi.
        b.Entity<IgScheduledPost>().Property(p => p.Status).HasMaxLength(32);
        b.Entity<IgScheduledPost>().HasIndex(p => p.Status);
        // ⚠️ Indekslanadigan ISO sana ustuniga uzunlik SHART: usiz SQL Server'da u
        // `nvarchar(max)` bo'lib qoladi va indeks umuman yaratilmaydi (faylning boshidagi izoh).
        b.Entity<IgScheduledPost>().Property(p => p.ScheduledAt).HasMaxLength(32);
        b.Entity<IgScheduledPost>().HasIndex(p => p.ScheduledAt);

        // CAPI NAVBATI.
        // 1) `Status` — yuborish sikli faqat `pending` qatorlarni oladi.
        // 2) `EventId` — UNIKAL: dedup kaliti deterministik (`{leadgenId}_{unix}`) va bir hodisa
        //    ikki marta navbatga tushmasligi kerak. Meta tomonidagi dedup oynasi atigi 48 soat,
        //    ya'ni unga tayanib bo'lmaydi — kechikkan qayta urinish konversiyani IKKILANTIRIB,
        //    reklama optimizatsiyasini buzardi.
        // 3) `LeadId` — "bu lid bo'yicha qaysi bosqich allaqachon yuborilgan?" tekshiruvi kunlik
        //    skanda HAR lid uchun qilinadi (indekssiz N ta seq-scan).
        b.Entity<IgCapiEvent>().Property(e => e.Status).HasMaxLength(32);
        b.Entity<IgCapiEvent>().HasIndex(e => e.Status);
        b.Entity<IgCapiEvent>().Property(e => e.EventId).HasMaxLength(200);
        b.Entity<IgCapiEvent>().HasIndex(e => e.EventId).IsUnique();
        b.Entity<IgCapiEvent>().Property(e => e.LeadId).HasMaxLength(200);
        b.Entity<IgCapiEvent>().HasIndex(e => e.LeadId);

        b.Entity<StudentAiAnalysis>().HasIndex(a => new { a.StudentId, a.Date });
        b.Entity<TeacherAiAnalysis>().HasIndex(a => new { a.TeacherId, a.Date });
        b.Entity<GroupAiAnalysis>().HasIndex(a => new { a.GroupId, a.Date });
        // Voronka AI tahlili — "kuniga bir marta" tekshiruvi (Kind, Date) bo'yicha izlanadi.
        b.Entity<FunnelAiAnalysis>().Property(a => a.Kind).HasMaxLength(32);
        b.Entity<FunnelAiAnalysis>().HasIndex(a => new { a.Kind, a.Date });
        b.Entity<CenterAiAnalysis>().HasIndex(a => a.Date);
        b.Entity<SmsLog>().HasIndex(s => s.RequestId);
        b.Entity<SmsLog>().HasIndex(s => s.BatchId);
        // Call Center: o'quvchi tarixi, raqam bo'yicha moslash, "eng oxirgisi tepada" ro'yxat.
        b.Entity<Call>().HasIndex(c => c.StudentId);
        b.Entity<Call>().HasIndex(c => c.PhoneNumber);
        b.Entity<Call>().HasIndex(c => c.StartedAt);
        b.Entity<Call>().HasIndex(c => c.AsteriskUniqueId);
        b.Entity<Call>().HasIndex(c => c.ProviderDbId);

        // CTI (Local Call): agent logini unikal, tarix filtri (agent/raqam/vaqt), hodisa kaskad.
        b.Entity<CtiAgent>().Property(a => a.Login).HasMaxLength(100);
        b.Entity<CtiAgent>().HasIndex(a => a.Login).IsUnique();
        b.Entity<CtiAgent>().Property(a => a.StaffUserId).HasMaxLength(200);
        b.Entity<CtiAgent>().HasIndex(a => a.StaffUserId);
        b.Entity<CtiCallRecord>().Property(c => c.AgentId).HasMaxLength(200);
        b.Entity<CtiCallRecord>().Property(c => c.RemoteNumber).HasMaxLength(50);
        b.Entity<CtiCallRecord>().HasIndex(c => c.AgentId);
        b.Entity<CtiCallRecord>().HasIndex(c => c.RemoteNumber);
        b.Entity<CtiCallRecord>().HasIndex(c => c.StartedAt);
        b.Entity<CtiCallEvent>().Property(e => e.CallId).HasMaxLength(200);
        b.Entity<CtiCallEvent>().HasIndex(e => e.CallId);
        b.Entity<CtiCallEvent>()
            .HasOne<CtiCallRecord>().WithMany()
            .HasForeignKey(e => e.CallId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<CtiCommandLog>().HasIndex(c => c.AgentId);
        b.Entity<AuditLog>().HasIndex(a => a.Timestamp);
        b.Entity<AuditLog>().HasIndex(a => a.StudentId);
        b.Entity<AuditLog>().HasIndex(a => a.TeacherId);

        // Xabarlar (chat/e'lon/telegram)
        b.Entity<ChatMessage>().HasIndex(m => new { m.ClassName, m.CreatedAt });
        b.Entity<Broadcast>().HasIndex(x => new { x.ClassName, x.CreatedAt });
        b.Entity<TelegramRegistration>().HasIndex(r => new { r.StudentId, r.ChatId }).IsUnique();
        b.Entity<TelegramRegistration>().HasIndex(r => r.ChatId);
        // Kod bo'yicha qidiruv (login) va cooldown/eskilarini bekor qilish (chat/user) tez ishlashi uchun.
        b.Entity<LoginOtpCode>().HasIndex(c => c.CodeHash).IsUnique();
        b.Entity<LoginOtpCode>().HasIndex(c => c.ChatId);
        b.Entity<LoginOtpCode>().HasIndex(c => new { c.UserId, c.Used });
        b.Entity<BotUser>().HasIndex(u => u.ChatId).IsUnique();
        b.Entity<TelegramGroup>().HasIndex(g => g.ChatId).IsUnique();
        b.Entity<BotSupportMessage>().HasIndex(m => m.ChatId);

        // Foydalanuvchi sozlamalari va qurilma tokenlari
        b.Entity<UserSettings>().HasKey(s => s.UserId);
        b.Entity<DeviceToken>().HasIndex(d => d.Token).IsUnique();
        b.Entity<DeviceToken>().HasIndex(d => d.UserId);

        // Shartnomalar
        b.Entity<ContractTemplate>().HasIndex(t => t.Target);
        b.Entity<Contract>().HasIndex(c => new { c.Target, c.RecipientKey });

        // Boshqaruv: filiallar va taklif/shikoyatlar
        b.Entity<Feedback>().HasIndex(f => new { f.Status, f.CreatedAt });

        // O'QUVCHINING O'QITUVCHI HAQIDAGI FIKRI — ikki tomondan ham o'qiladi: o'quvchi
        // profilida (StudentId bo'yicha) va o'qituvchi AI tahlilida (TeacherId bo'yicha).
        // O'quvchi/o'qituvchi/guruh o'chirilsa fikrlar ham keraksiz — FK CASCADE.
        b.Entity<TeacherReview>().Property(r => r.StudentId).HasMaxLength(200);
        b.Entity<TeacherReview>().Property(r => r.TeacherId).HasMaxLength(200);
        b.Entity<TeacherReview>().Property(r => r.GroupId).HasMaxLength(200);
        b.Entity<TeacherReview>().HasIndex(r => new { r.StudentId, r.CreatedAt });
        b.Entity<TeacherReview>().HasIndex(r => new { r.TeacherId, r.CreatedAt });
        b.Entity<TeacherReview>()
            .HasOne<Student>().WithMany().HasForeignKey(r => r.StudentId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<TeacherReview>()
            .HasOne<Teacher>().WithMany().HasForeignKey(r => r.TeacherId)
            .OnDelete(DeleteBehavior.Cascade);

        // O'quv dasturi — indekslarda qatnashadigan string ustunlarga aniq uzunlik beriladi.
        foreach (var (type, prop) in new (Type, string)[]
        {
            (typeof(CourseModule), "CurriculumId"),
            (typeof(CourseTopic), "ModuleId"),
            (typeof(CourseLesson), "TopicId"),
            (typeof(CourseItem), "LessonId"),
            (typeof(CourseProgress), "StudentId"), (typeof(CourseProgress), "ItemId"),
            (typeof(CourseItemAttempt), "StudentId"), (typeof(CourseItemAttempt), "ItemId"),
            (typeof(CourseItemAttempt), "CurriculumId"), (typeof(CourseItemAttempt), "Section"),
            (typeof(SubjectCurriculum), "SubjectId"), (typeof(SubjectCurriculum), "CurriculumId"),
        })
            b.Entity(type).Property(prop).HasMaxLength(200);
        b.Entity<CourseModule>().HasIndex(m => new { m.CurriculumId, m.Order });
        b.Entity<CourseTopic>().HasIndex(t => new { t.ModuleId, t.Order });
        b.Entity<CourseLesson>().HasIndex(s => new { s.TopicId, s.Order });
        b.Entity<CourseItem>().HasIndex(i => new { i.LessonId, i.Order });
        b.Entity<CourseProgress>().HasIndex(p => new { p.StudentId, p.ItemId }).IsUnique();
        // Urinishlar TARIX — unique EMAS (har ishlash yangi qator). Profilda o'quvchi bo'yicha
        // eng yangisidan tartiblab o'qiladi; ikkinchi indeks topshiriq kesimidagi hisobot uchun.
        b.Entity<CourseItemAttempt>().HasIndex(a => new { a.StudentId, a.FinishedAt });
        b.Entity<CourseItemAttempt>().HasIndex(a => new { a.ItemId, a.StudentId, a.Section });
        // Kurs↔Dastur: bitta kursga bitta dastur faqat bir marta biriktiriladi.
        b.Entity<SubjectCurriculum>().HasIndex(sc => new { sc.SubjectId, sc.CurriculumId }).IsUnique();
        b.Entity<SubjectCurriculum>().HasIndex(sc => sc.CurriculumId);

        b.Entity<ActionReason>().HasIndex(r => new { r.Category, r.Order });

        // Tuman + maktab (maktab tuman ichida tartiblanadi/qidiriladi).
        b.Entity<District>().HasIndex(d => d.Order);
        b.Entity<School>().Property(s => s.DistrictId).HasMaxLength(200);
        b.Entity<School>().HasIndex(s => new { s.DistrictId, s.Order });

        // AI tekshiruv — o'quvchi + sana bo'yicha (kunlik limit/tarix), ruxsat o'quvchi bo'yicha.
        b.Entity<AiCheck>().Property(a => a.StudentId).HasMaxLength(200);
        b.Entity<AiCheck>().HasIndex(a => new { a.StudentId, a.Date });
        b.Entity<StudentAiAccess>().Property(a => a.StudentId).HasMaxLength(200);
        b.Entity<StudentAiAccess>().HasIndex(a => a.StudentId).IsUnique();

        b.Entity<ArchivedRecord>().HasIndex(r => new { r.Type, r.DeletedAt });

        // ---------- Guruhning o'qituvchi tarixi ----------
        // Asosiy so'rov: "shu guruhni FALON sanada kim o'qitgan" → (GroupId, FromDate).
        // Ikkinchisi: o'qituvchi profili uchun "u qaysi guruhlarni o'qitgan".
        b.Entity<GroupTeacherAssignment>().Property(x => x.GroupId).HasMaxLength(200);
        b.Entity<GroupTeacherAssignment>().Property(x => x.TeacherId).HasMaxLength(200);
        b.Entity<GroupTeacherAssignment>().HasIndex(x => new { x.GroupId, x.FromDate });
        b.Entity<GroupTeacherAssignment>().HasIndex(x => x.TeacherId);

        // ---------- O'quvchini ushlab turish bonusi ----------
        b.Entity<RetentionBonusAward>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Entity<RetentionBonusShare>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<RetentionBonusShare>().Property(x => x.Months).HasPrecision(18, 4);
        b.Entity<RetentionBonusAward>().Property(x => x.StudentId).HasMaxLength(200);
        b.Entity<RetentionBonusAward>().Property(x => x.CourseId).HasMaxLength(200);
        b.Entity<RetentionBonusShare>().Property(x => x.AwardId).HasMaxLength(200);
        b.Entity<RetentionBonusShare>().Property(x => x.TeacherId).HasMaxLength(200);
        // Bitta sikl uchun ikkinchi marta bonus berilmasin (ikki admin bir vaqtda bosgan holat ham).
        // Sikl HAR FAN uchun alohida yuritilgani sababli kalitga CourseId ham kiradi.
        b.Entity<RetentionBonusAward>().HasIndex(x => new { x.StudentId, x.CourseId, x.CycleNo }).IsUnique();
        b.Entity<RetentionBonusShare>().HasIndex(x => x.AwardId);
        // O'qituvchi profilidagi "Bonus" tabi shu indeks bo'yicha o'qiydi.
        b.Entity<RetentionBonusShare>().HasIndex(x => x.TeacherId);
        // Sanoq boshlanish oyi — har (o'quvchi, fan) uchun BITTA qator.
        b.Entity<RetentionBonusTrack>().Property(x => x.StudentId).HasMaxLength(200);
        b.Entity<RetentionBonusTrack>().Property(x => x.CourseId).HasMaxLength(200);
        b.Entity<RetentionBonusTrack>().HasIndex(x => new { x.StudentId, x.CourseId }).IsUnique();

        // ---------- Kitoblar sotuvi ----------
        b.Entity<Book>().Property(x => x.Price).HasPrecision(18, 2);
        // QOLDIQ POYGASI (race) himoyasi — `Stock` konkurentlik TOKENI.
        // Muammo: sotuv "qoldiqni o'qi → yetadimi deb tekshir → yangisini yoz" ketma-ketligi bilan
        // ishlaydi. Qoldiq 1 bo'lganda ikki kassir bir vaqtda "Kitob sotish" bossa, ikkalasi ham
        // Stock=1 ni o'qib, ikkalasi ham tekshiruvdan o'tib, 2 dona sotilardi (ombor 0 emas, -1 ga
        // tushishi kerak edi, lekin tarixda ikkala qatorda ham StockAfter=0 turardi).
        // Token bilan EF `UPDATE Books SET Stock=@yangi WHERE Id=@id AND Stock=@asl` yozadi: oraga
        // boshqa amal tushgan bo'lsa 0 qator yangilanadi va `DbUpdateConcurrencyException` chiqadi
        // (uni `BookSalesService.ApproveAsync` tushunarli xatoga o'giradi).
        // Bu FAQAT model metadatasi — ustun/indeks o'zgarmaydi, ya'ni MIGRATSIYA KERAK EMAS.
        b.Entity<Book>().Property(x => x.Stock).IsConcurrencyToken();
        b.Entity<BookOrder>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Entity<BookOrder>().Property(x => x.Total).HasPrecision(18, 2);
        // QAYTARISH: mijozga qaytarilgan pul (to'lanmagan nasiyada 0 — u yerda qarz kamayadi).
        b.Entity<BookOrder>().Property(x => x.RefundedAmount).HasPrecision(18, 2);
        foreach (var (type, prop) in new (Type, string)[]
        {
            (typeof(BookStockMove), "BookId"), (typeof(BookStockMove), "Reason"),
            (typeof(BookOrder), "BookId"), (typeof(BookOrder), "Status"),
            (typeof(BookOrder), "PaymentMethod"), (typeof(BookOrder), "SettledMethod"),
        })
            b.Entity(type).Property(prop).HasMaxLength(200);
        // Ombor harakati: kitob bo'yicha tarix + "kirim tarixi" sanaga qarab o'qiladi.
        b.Entity<BookStockMove>().HasIndex(m => new { m.BookId, m.CreatedAt });
        b.Entity<BookStockMove>().HasIndex(m => new { m.Reason, m.CreatedAt });
        // Buyurtmalar: kutilayotganlar ro'yxati (status) va sotuv hisobotlari (sana/kitob).
        b.Entity<BookOrder>().HasIndex(o => new { o.Status, o.CreatedAt });
        b.Entity<BookOrder>().HasIndex(o => o.CreatedAt);
        b.Entity<BookOrder>().HasIndex(o => new { o.BookId, o.Status });
        b.Entity<BookOrder>().HasIndex(o => o.ChatId);
        // NASIYA navbati: "to'lanmagan qarzlar" ro'yxati (PaymentMethod="credit" AND PaidAt IS NULL)
        // va "davr ichida nasiyadan yig'ilgan pul" (PaidAt bo'yicha) shu indeksdan o'qiydi.
        b.Entity<BookOrder>().HasIndex(o => new { o.PaymentMethod, o.PaidAt });
        // QAYTARISH: "davr ichida qancha pul qaytarildi" hisoboti ReturnedAt bo'yicha o'qiydi
        // (qaytarilganlar butun jadvalning kichik qismi — indeks bo'lmasa to'liq skan bo'lardi).
        b.Entity<BookOrder>().HasIndex(o => o.ReturnedAt);
        // Bot savdo sessiyasi — bitta chatda bitta faol sessiya.
        b.Entity<BookBotSession>().HasIndex(s => s.ChatId).IsUnique();

        // ---------- Karyera (vakansiyalar + arizalar) ----------
        b.Entity<Vacancy>().Property(x => x.SalaryFrom).HasPrecision(18, 2);
        b.Entity<Vacancy>().Property(x => x.SalaryTo).HasPrecision(18, 2);
        foreach (var (type, prop) in new (Type, string)[]
        {
            (typeof(Vacancy), "Status"), (typeof(Vacancy), "CreatedAt"),
            (typeof(JobApplication), "VacancyId"), (typeof(JobApplication), "Status"),
            (typeof(JobApplication), "CreatedAt"),
            (typeof(JobApplicationEvent), "ApplicationId"), (typeof(JobApplicationEvent), "CreatedAt"),
        })
            b.Entity(type).Property(prop).HasMaxLength(200);
        // Ilovada faqat faol vakansiyalar tartib bo'yicha o'qiladi.
        b.Entity<Vacancy>().HasIndex(v => new { v.Status, v.Order });
        // Admin ro'yxati bosqich/sana bo'yicha; "Arizalarim" esa chat bo'yicha.
        b.Entity<JobApplication>().HasIndex(a => new { a.Status, a.CreatedAt });
        b.Entity<JobApplication>().HasIndex(a => new { a.ChatId, a.CreatedAt });
        b.Entity<JobApplication>().HasIndex(a => new { a.VacancyId, a.Status });
        b.Entity<JobApplicationEvent>().HasIndex(e => new { e.ApplicationId, e.CreatedAt });
        b.Entity<CareerBotUser>().HasIndex(u => u.ChatId).IsUnique();

        // Daraja testi — Slug ommaviy URL kaliti (noyob, indekslanishi uchun uzunlik beriladi).
        b.Entity<LevelTest>().Property(t => t.Slug).HasMaxLength(64);
        b.Entity<LevelTest>().HasIndex(t => t.Slug).IsUnique();
        b.Entity<LevelTestQuestion>().HasIndex(q => new { q.TestId, q.Order });
        b.Entity<LevelTestBand>().HasIndex(x => new { x.TestId, x.Order });
        b.Entity<LevelTestSubmission>().HasIndex(s => new { s.TestId, s.CreatedAt });
        b.Entity<LevelTestInvite>().HasIndex(i => i.Token).IsUnique();
        b.Entity<LevelTestInvite>().HasIndex(i => new { i.TestId, i.CreatedAt });
        b.Entity<LevelTestInvite>().HasIndex(i => i.LeadId);

        // Lid formalari — Slug ommaviy URL kaliti (daraja testidagi bilan bir xil konvensiya).
        b.Entity<LeadForm>().Property(f => f.Slug).HasMaxLength(64);
        b.Entity<LeadForm>().HasIndex(f => f.Slug).IsUnique();
        b.Entity<LeadFormField>().Property(f => f.FormId).HasMaxLength(200);
        b.Entity<LeadFormField>().HasIndex(f => new { f.FormId, f.Order });
        b.Entity<LeadFormSubmission>().Property(s => s.FormId).HasMaxLength(200);
        b.Entity<LeadFormSubmission>().HasIndex(s => new { s.FormId, s.CreatedAt });
        b.Entity<LeadFormSubmission>().Property(s => s.LeadId).HasMaxLength(200);
        b.Entity<LeadFormSubmission>().HasIndex(s => s.LeadId);

        // Sertifikatlar
        b.Entity<CertificateTemplate>().Property(t => t.CourseId).HasMaxLength(200);
        b.Entity<CertificateTemplate>().HasIndex(t => t.CourseId);

        // StudentCertificate
        b.Entity<StudentCertificate>().Property(c => c.StudentId).HasMaxLength(200);
        b.Entity<StudentCertificate>().Property(c => c.CourseId).HasMaxLength(200);
        b.Entity<StudentCertificate>()
            .HasOne<Student>().WithMany()
            .HasForeignKey(c => c.StudentId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<StudentCertificate>()
            .HasOne<Subject>().WithMany()
            .HasForeignKey(c => c.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<StudentCertificate>().HasIndex(c => new { c.StudentId, c.CourseId });
        b.Entity<StudentCertificate>().HasIndex(c => c.Status);
        b.Entity<StudentCertificate>().HasIndex(c => new { c.StudentId, c.CourseId, c.IssuedAt }).IsUnique();

        // CertificateVerification → StudentCertificate (FK, CASCADE)
        b.Entity<CertificateVerification>().Property(v => v.StudentCertificateId).HasMaxLength(200);
        b.Entity<CertificateVerification>()
            .HasOne<StudentCertificate>().WithMany()
            .HasForeignKey(v => v.StudentCertificateId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<CertificateVerification>().HasIndex(v => v.StudentCertificateId);

        // O'quv xonalari — tez-tez ishlatiladigan filtrlar
        b.Entity<Room>().HasIndex(r => r.IsActive);
        b.Entity<Room>().HasIndex(r => new { r.Name, r.IsActive });

        // Test natijalari — guruh bo'yicha ro'yxat, har (test, o'quvchi) uchun bitta ball,
        // test o'chirilsa ballari ham kaskad o'chadi.
        b.Entity<TestResult>().Property(t => t.MaxScore).HasPrecision(18, 2);
        b.Entity<TestResult>().Property(t => t.GroupId).HasMaxLength(200);
        b.Entity<TestResult>().HasIndex(t => t.GroupId);
        // TEST KODI — bot bo'yicha qidiriladi (markazdan tashqari ishtirokchi kodni yuboradi).
        // Uniklik DB indeksi bilan EMAS, `TestResultService` da tekshiriladi: kod faqat ONLAYN testda
        // bo'ladi, oflayn testlarda bo'sh — bo'sh qiymatlar ustidan unikal indeks provayderga bog'liq
        // (filtrli indeks) bo'lib qolardi.
        b.Entity<TestResult>().Property(t => t.Code).HasMaxLength(32);
        b.Entity<TestResult>().HasIndex(t => t.Code);
        b.Entity<TestScore>().Property(t => t.Score).HasPrecision(18, 2);
        b.Entity<TestScore>().Property(t => t.TestResultId).HasMaxLength(200);
        b.Entity<TestScore>().Property(t => t.StudentId).HasMaxLength(200);
        b.Entity<TestScore>().HasIndex(t => new { t.TestResultId, t.StudentId }).IsUnique();
        b.Entity<TestScore>().HasIndex(t => t.StudentId);
        b.Entity<TestScore>()
            .HasOne<TestResult>().WithMany()
            .HasForeignKey(t => t.TestResultId)
            .OnDelete(DeleteBehavior.Cascade);
        // MARKAZDAN TASHQARI ishtirokchi natijasi — bir chat bitta testni bir marta ishlaydi;
        // test o'chirilsa natijalari ham kaskad o'chadi (TestScore bilan bir xil qoida).
        b.Entity<ExternalTestScore>().Property(t => t.Score).HasPrecision(18, 2);
        b.Entity<ExternalTestScore>().Property(t => t.TestResultId).HasMaxLength(200);
        b.Entity<ExternalTestScore>().HasIndex(t => new { t.TestResultId, t.ChatId }).IsUnique();
        b.Entity<ExternalTestScore>()
            .HasOne<TestResult>().WithMany()
            .HasForeignKey(t => t.TestResultId)
            .OnDelete(DeleteBehavior.Cascade);
        // TEST SERTIFIKATI — bir test bo'yicha o'quvchiga BITTA sertifikat; test o'chirilsa
        // sertifikat yozuvlari ham kaskad o'chadi (fayllar `/uploads` da qoladi — ular zaxirada).
        b.Entity<TestCertificate>().Property(c => c.Score).HasPrecision(18, 2);
        b.Entity<TestCertificate>().Property(c => c.MaxScore).HasPrecision(18, 2);
        b.Entity<TestCertificate>().Property(c => c.TestResultId).HasMaxLength(200);
        b.Entity<TestCertificate>().Property(c => c.StudentId).HasMaxLength(200);
        b.Entity<TestCertificate>().HasIndex(c => new { c.TestResultId, c.StudentId }).IsUnique();
        b.Entity<TestCertificate>().HasIndex(c => c.StudentId);
        b.Entity<TestCertificate>()
            .HasOne<TestResult>().WithMany()
            .HasForeignKey(c => c.TestResultId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<TestCertificateTemplate>().HasIndex(t => t.IsActive);

        // Onlayn test — botdagi ishlash sessiyasi (bitta chatda bitta faol sessiya).
        b.Entity<TestBotSession>().Property(s => s.TestResultId).HasMaxLength(200);
        b.Entity<TestBotSession>().Property(s => s.StudentId).HasMaxLength(200);
        b.Entity<TestBotSession>().HasIndex(s => s.ChatId).IsUnique();
        b.Entity<TestBotSession>()
            .HasOne<TestResult>().WithMany()
            .HasForeignKey(s => s.TestResultId)
            .OnDelete(DeleteBehavior.Cascade);

        // Group.RoomId → Room (SET NULL on delete)
        b.Entity<Group>().HasIndex(c => c.RoomId);
        b.Entity<Group>()
            .HasOne<Room>().WithMany()
            .HasForeignKey(c => c.RoomId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    // ==================== Saqlashdan oldingi normalizatsiya ====================

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SyncLeadPhoneKeys();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SyncLeadPhoneKeys();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// <see cref="Lead.PhoneKey"/> ni <see cref="Lead.Phone"/> dan hisoblab qo'yadi (qo'shilgan va
    /// o'zgargan lidlar uchun).
    ///
    /// <para>NEGA shu yerda: lid to'rt joyda yaratiladi (lid formasi, daraja testi, CRM formasi,
    /// landing) va telefon tahrirlanadi ham. Har birida qo'lda yozilsa — beshinchi joy qo'shilganda
    /// unutiladi va o'sha lid telefon bo'yicha QIDIRUVDAN tushib qolardi (dublikat lid ochilar,
    /// modulning butun ma'nosi buzilardi). Bitta darvoza — unutish imkonsiz.</para>
    /// </summary>
    private void SyncLeadPhoneKeys()
    {
        foreach (var entry in ChangeTracker.Entries<Lead>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var key = PhoneUtil.Key(entry.Entity.Phone);
            if (entry.Entity.PhoneKey != key) entry.Entity.PhoneKey = key;
        }
    }
}
