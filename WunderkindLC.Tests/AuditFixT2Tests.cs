using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Audit (2026-10-07) T2 — o'quvchi, arxiv, tiklash:
/// C1 (arxiv = ketdi; eski arxivdan tiklashda orqaga hisob yo'q) · MEM-5 (butunlay o'chirilgan o'quvchi
/// balansi) · ADM-2 (arxiv suratida parol yo'q) · CRM-4 (lid tarixi bilan tiklash) · MSG-1 (Telegram
/// ulanishini uzish) · CRM (o'chirilgan o'quvchining lidi qayta ochilmaydi) · MEM (guruh sig'imi).
/// </summary>
public class AuditFixT2Tests
{
    private const decimal Fee = 600_000m;

    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");
    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    /// <summary>Har kuni dars — "dars soni" = kunlar soni (qisman oy oson tekshiriladi).</summary>
    private static (Student S, Group G) Seed(AppDbContext ctx, decimal balance = 0m)
    {
        var g = new Group { Name = "A guruh", MonthlyFee = Fee, Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 } };
        var s = new Student
        {
            FullName = "Test O'quvchi", EnrollmentDate = $"{M(-8)}-01", Balance = balance, ClassName = g.Name,
        };
        ctx.Classes.Add(g);
        ctx.Students.Add(s);
        return (s, g);
    }

    private static StudentGroup Member(AppDbContext ctx, Student s, Group g, string status,
        string joinedAt, string activatedAt = "", string frozenAt = "")
    {
        var m = new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = status, IsActive = true,
            JoinedAt = joinedAt, ActivatedAt = activatedAt, FrozenAt = frozenAt, RecordedAt = joinedAt,
        };
        ctx.StudentGroups.Add(m);
        return m;
    }

    private static void Charge(AppDbContext ctx, Student s, Group g, string month, decimal amount) =>
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = month, Amount = amount, Date = $"{month}-01" });

    private static AuditService Audit(AppDbContext ctx) => new(ctx, new NoHttpContext());

    /* ============================ C1 — arxiv = ketdi ============================ */

    [Fact]
    public async Task Arxivlash_azoliklarni_GURUHDAN_CHIQARISH_bilan_yopadi__sabab_va_audit_bilan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx, balance: -2 * Fee);
        var reason = new ActionReason { Category = "archive_student", Label = "Ko'chib ketdi" };
        ctx.ActionReasons.Add(reason);
        var m = Member(ctx, s, g, "active", $"{M(-4)}-01", activatedAt: $"{M(-4)}-01");
        Charge(ctx, s, g, M(-2), Fee);
        Charge(ctx, s, g, M(-1), Fee);   // arxiv sanasidan keyingi oy (oldindan yozilgan)
        await ctx.SaveChangesAsync();

        var leftAt = $"{M(-2)}-10";
        var res = await StudentArchiving.LeaveAllGroupsAsync(ctx, Audit(ctx), s, leftAt,
            reason.Id, reason.Label, "o'quvchi arxivlandi");
        s.IsArchived = true;
        s.ArchivedAt = leftAt;
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Closed);
        Assert.Equal(Fee, res.Restored);   // keyingi oy hisobi bekor qilindi
        var saved = await ctx.StudentGroups.AsNoTracking().SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Equal(leftAt, saved.LeftAt);
        Assert.Equal("", (await ctx.Students.AsNoTracking().SingleAsync()).ClassName);   // yorliq bo'shadi

        // Arxiv oyi faqat arxiv sanasigacha (10 kun) — RemoveMember bilan bir xil.
        var row = Assert.Single(await ctx.MonthlyCharges.AsNoTracking().ToListAsync());
        Assert.Equal(M(-2), row.Month);
        var daysIn = DateTime.DaysInMonth(AppClock.Today.AddMonths(-2).Year, AppClock.Today.AddMonths(-2).Month);
        Assert.Equal(decimal.Round(Fee * 10 / daysIn, 2), row.Amount);

        // Sabab — «Ketish sabablari» manbai (arxiv sababi = ketish sababi), sanasi AYNAN chiqish sanasi.
        var log = Assert.Single(await ctx.MembershipReasonLogs.AsNoTracking().ToListAsync());
        Assert.Equal(MembershipReasons.ActionRemove, log.Action);
        Assert.Equal(leftAt, log.Date);
        Assert.Equal(reason.Id, log.ReasonId);
        var resolved = MembershipReasons.Resolve(leftAt, "", new[] { log },
            Array.Empty<MembershipReasons.AuditRow>(), new MembershipReasons.ArchiveInfo(false, null, null));
        Assert.Equal("Ko'chib ketdi", resolved.Label);   // tiklangandan keyin ham (IsArchived=false)

        // A'zolik audit qatori — KPI/AI audit matnidan o'qiydigan naqsh bilan.
        var audit = Assert.Single(await ctx.AuditLogs.AsNoTracking().Where(a => a.EntityType == "Membership").ToListAsync());
        Assert.Equal($"{g.Id}:{s.Id}", audit.EntityId);
        Assert.StartsWith("Guruhdan chiqarildi", audit.Summary);
        Assert.Equal("Ko'chib ketdi", MembershipReasons.ReasonFromAuditSummary(audit.Summary));
    }

    [Fact]
    public async Task Arxivdan_TIKLANGACH_arxivda_otgan_oylar_HISOBLANMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx);
        Member(ctx, s, g, "active", $"{M(-5)}-01", activatedAt: $"{M(-5)}-01");
        await ctx.SaveChangesAsync();

        // May oyida arxivlandi (yangi qoida bilan) ...
        await StudentArchiving.LeaveAllGroupsAsync(ctx, Audit(ctx), s, $"{M(-3)}-05", null, "", "o'quvchi arxivlandi");
        s.IsArchived = true;
        s.ArchivedAt = $"{M(-3)}-05";
        await ctx.SaveChangesAsync();
        var before = await ctx.MonthlyCharges.CountAsync();

        // ... oktyabrda qaytarildi (a'zoliklar tiklanmaydi): fon xizmati arxiv oylarini yozmaydi.
        s.IsArchived = false;
        s.ArchivedAt = null;
        await ctx.SaveChangesAsync();
        foreach (var month in new[] { M(-2), M(-1), M(0) })
        {
            var (count, _, _) = await TuitionService.AccrueMonth(ctx, month);
            Assert.Equal(0, count);
        }
        Assert.Equal(before, await ctx.MonthlyCharges.CountAsync());
    }

    [Fact]
    public async Task ESKI_arxiv__tiklashda_tirik_azolik_ARXIV_SANASIDA_yopiladi__orqaga_hisob_YOQ()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx, balance: -4 * Fee);
        // Tuzatishdan OLDIN arxivlangan: a'zolik hamon TIRIK (active).
        var m = Member(ctx, s, g, "active", $"{M(-6)}-01", activatedAt: $"{M(-6)}-01");
        foreach (var d in new[] { -6, -5, -4, -3 }) Charge(ctx, s, g, M(d), Fee);
        s.IsArchived = true;
        s.ArchivedAt = $"{M(-3)}-15";
        s.ArchiveReason = "Qimmat";
        await ctx.SaveChangesAsync();
        // Tuzatishsiz: arxiv oylari "hisoblanishi kerak" bo'lib turardi.
        Assert.True(TuitionService.AccruableMonth(m, M(-2)));

        var res = await StudentArchiving.CloseLegacyMembershipsAsync(ctx, Audit(ctx), s);
        s.IsArchived = false;
        s.ArchivedAt = null;
        s.ArchiveReason = null;
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Closed);
        var saved = await ctx.StudentGroups.AsNoTracking().SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Equal($"{M(-3)}-15", saved.LeftAt);
        // Arxiv oyi arxiv sanasigacha qisqardi (15 kun) — to'liq oylik emas.
        Assert.True((await ctx.MonthlyCharges.AsNoTracking().SingleAsync(c => c.Month == M(-3))).Amount < Fee);

        foreach (var month in new[] { M(-2), M(-1), M(0) })
            Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, month)).Count);
        Assert.Equal(4, await ctx.MonthlyCharges.CountAsync());

        // Sabab — arxiv sababi (tiklangandan keyin ham hisobotda yo'qolmaydi).
        Assert.Equal("Qimmat", (await ctx.MembershipReasonLogs.AsNoTracking().SingleAsync()).ReasonLabel);
    }

    [Fact]
    public async Task ESKI_arxiv__arxivdan_KEYIN_muzlatilgan_azolikdagi_hisob_bekor_qilinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx, balance: -Fee / 2);
        // O'quvchi arxivda turganda guruh yopildi — a'zolik o'shanda muzlatilib qisman oylik yozilgan.
        Member(ctx, s, g, "frozen", $"{M(-6)}-01", activatedAt: $"{M(-6)}-01", frozenAt: $"{M(-1)}-14");
        Charge(ctx, s, g, M(-1), Fee / 2);
        s.IsArchived = true;
        s.ArchivedAt = $"{M(-3)}-15";
        await ctx.SaveChangesAsync();

        var res = await StudentArchiving.CloseLegacyMembershipsAsync(ctx, Audit(ctx), s);
        await ctx.SaveChangesAsync();

        Assert.Equal(Fee / 2, res.Restored);
        Assert.Empty(await ctx.MonthlyCharges.ToListAsync());
        Assert.Equal(0m, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);
    }

    [Fact]
    public async Task Guruhsiz_ESKI_oquvchi__yorliq_bo_shatiladi__tiklangach_yorliq_narxi_bilan_HISOBLANMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, _) = Seed(ctx);   // a'zolik YO'Q, faqat ClassName yorlig'i (eski guruhsiz billing)
        s.IsArchived = true;
        s.ArchivedAt = $"{M(-4)}-01";
        await ctx.SaveChangesAsync();

        await StudentArchiving.CloseLegacyMembershipsAsync(ctx, Audit(ctx), s);
        s.IsArchived = false;
        await ctx.SaveChangesAsync();

        Assert.Equal("", (await ctx.Students.AsNoTracking().SingleAsync()).ClassName);
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(-1))).Count);
    }

    [Theory]
    [InlineData("2026-05-10", "2026-03-01", "2026-05-10")]   // oddiy holat
    [InlineData("2026-05-10", "2026-06-01", "2026-06-01")]   // a'zolik arxivdan KEYIN qo'shilgan — teskari oraliq yo'q
    [InlineData("2026-05-10", "", "2026-05-10")]             // qo'shilgan sana noma'lum
    [InlineData("2026-10-07", "2026-11-01", "2026-10-07")]   // kelajakdagi qo'shilish — chiqish kelajakka surilmaydi
    public void Chiqish_sanasi_qoshilgan_kundan_OLDIN_bolmaydi(string leftAt, string joinedAt, string expected) =>
        Assert.Equal(expected, StudentArchiving.LeaveDateFor(new StudentGroup { JoinedAt = joinedAt }, leftAt, today: "2026-10-07"));

    /* ============================ MEM-5 — butunlay o'chirilgan o'quvchi ============================ */

    [Theory]
    [InlineData(-600_000, 0, 0)]               // o'chgan hisoblar qarzi — tushuntirib bo'lmaydi → 0
    [InlineData(0, 900_000, 0)]                // to'lovlar qoldi, hisoblar o'chdi → "tekin avans" YO'Q
    [InlineData(100_000, 1_000_000, 100_000)]  // haqiqiy avans saqlanadi
    [InlineData(-300_000, 600_000, 0)]
    [InlineData(50_000, -20_000, -20_000)]     // qarzni qolgan qatorlar tushuntirsa — qoladi
    public void Tiklangan_balans__soxta_qarz_ham_soxta_avans_ham_YOQ(int snapshot, int rows, int expected) =>
        Assert.Equal((decimal)expected, StudentArchiving.RestoredBalance(snapshot, rows));

    [Fact]
    public async Task Qatorlardan_balans__tolov_minus_vozvrat_minus_qolgan_hisob__bekor_qilingan_hisobga_olinmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx);
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { StudentId = s.Id, Direction = "income", Category = "tuition", Amount = 900_000m, Date = Today },
            new FinanceTransaction { StudentId = s.Id, Direction = "income", Category = "tuition", Amount = 77_000m, Date = Today, IsVoided = true },
            new FinanceTransaction { StudentId = s.Id, Direction = "expense", Category = "refund", Amount = 100_000m, Date = Today },
            new FinanceTransaction { StudentId = s.Id, Direction = "income", Category = "book", Amount = 50_000m, Date = Today });
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 300_000m, Discount = 50_000m, Date = Today });
        await ctx.SaveChangesAsync();

        Assert.Equal(550_000m, await StudentArchiving.BalanceFromRowsAsync(ctx, s.Id));
    }

    /* ============================ ADM-2 — arxiv suratida maxfiy maydon yo'q ============================ */

    [Fact]
    public async Task Xodim_surati_PAROLSIZ_saqlanadi__tiklash_uchun_oqiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var user = new AppUser
        {
            FullName = "Kassir", Email = "kassir", Role = Roles.Staff, PasswordHash = "HASH-123",
            InitialPassword = "ochiq-parol", RoleTemplateId = "rol-1", Permissions = new() { "finance" },
        };

        ArchiveService.Snapshot(ctx, "staff", user.Id, user.FullName, user.Email, user, null, "Admin");
        await ctx.SaveChangesAsync();

        var json = (await ctx.ArchivedRecords.SingleAsync()).Json;
        Assert.DoesNotContain("HASH-123", json);
        Assert.DoesNotContain("ochiq-parol", json);
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("InitialPassword", json);
        var back = JsonSerializer.Deserialize<AppUser>(json)!;
        Assert.Equal(user.Id, back.Id);
        Assert.Equal("kassir", back.Email);
        Assert.Equal("", back.PasswordHash);
    }

    [Fact]
    public async Task ESKI_suratlar_tozalanadi__qayta_chaqirish_hech_narsa_qilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var user = new AppUser { FullName = "X", Email = "x", PasswordHash = "H", InitialPassword = "P" };
        ctx.ArchivedRecords.Add(new ArchivedRecord { Type = "staff", EntityId = user.Id, Json = JsonSerializer.Serialize(user) });
        ctx.ArchivedRecords.Add(new ArchivedRecord { Type = "lead", EntityId = "l1", Json = "{\"Note\":\"Token haqida izoh\"}" });
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ArchiveService.ScrubStoredSecretsAsync(ctx));
        Assert.Equal(0, await ArchiveService.ScrubStoredSecretsAsync(ctx));
        var rows = await ctx.ArchivedRecords.AsNoTracking().ToListAsync();
        Assert.DoesNotContain("InitialPassword", rows.Single(r => r.Type == "staff").Json);
        Assert.Equal("{\"Note\":\"Token haqida izoh\"}", rows.Single(r => r.Type == "lead").Json);   // qiymat emas, KALIT
    }

    [Theory]
    [InlineData("PasswordHash", true)]
    [InlineData("InitialPassword", true)]
    [InlineData("FcmToken", true)]
    [InlineData("TokenHash", true)]
    [InlineData("AppSecret", true)]
    [InlineData("Phone", false)]
    [InlineData("TokenExpiresAt", false)]
    [InlineData("FullName", false)]
    public void Maxfiy_maydon_nomi(string name, bool secret) => Assert.Equal(secret, ArchiveService.IsSecretField(name));

    /* ============================ CRM-4 — lid tarixi bilan tiklash ============================ */

    [Fact]
    public async Task Lid_tiklanganda_HODISALAR_va_SINOV_darslari_qaytadi__yordamchi_yozuv_ochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = new Lead { FullName = "Lid", Phone = "+998-90-123-45-67", CreatedAt = AppClock.Iso() };
        var events = new List<LeadEvent>
        {
            new() { LeadId = lead.Id, Type = "created", Text = "Yaratildi", ActorUserId = "u1", CreatedAt = AppClock.Iso() },
            new() { LeadId = lead.Id, Type = "stage", Text = "Bosqich", CreatedAt = AppClock.Iso() },
        };
        var trials = new List<TrialLesson> { new() { LeadId = lead.Id, GroupId = "g1", ScheduledAt = $"{Today}T10:00" } };
        // T8 shartnomasi: `LeadsController.Delete` AYNAN shunday yozadi.
        ArchiveService.Snapshot(ctx, ArchiveService.TypeLeadHistory, lead.Id, lead.FullName, "",
            new { Events = events, Trials = trials }, null, "Admin");
        ArchiveService.Snapshot(ctx, "lead", lead.Id, lead.FullName, lead.Phone, lead, null, "Admin");
        await ctx.SaveChangesAsync();
        Assert.Contains(ArchiveService.TypeLeadHistory, ArchiveService.HiddenTypes);

        ctx.Leads.Add(lead);
        var (ev, tr) = await ArchiveService.RestoreLeadHistoryAsync(ctx, lead.Id);
        await ctx.SaveChangesAsync();

        Assert.Equal((2, 1), (ev, tr));
        Assert.Equal(2, await ctx.LeadEvents.CountAsync(e => e.LeadId == lead.Id));
        Assert.Equal("g1", (await ctx.TrialLessons.SingleAsync()).GroupId);
        Assert.DoesNotContain(await ctx.ArchivedRecords.ToListAsync(), r => r.Type == ArchiveService.TypeLeadHistory);
        // Kanal — `created` hodisasidagi ActorUserId bo'yicha "qo'lda" (ilgari "Boshqa" ga o'tib ketardi).
        Assert.Equal(LeadOrigins.Manual, LeadOrigins.Classify(lead.Id, manualLeadIds: (await ctx.LeadEvents
            .Where(e => e.Type == "created" && e.ActorUserId != null).Select(e => e.LeadId).ToListAsync()).ToHashSet()));
    }

    [Fact]
    public async Task Lid_tiklash__shu_raqamli_BOSHQA_lid_bolsa_nomi_qaytadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Leads.Add(new Lead { FullName = "Yangi lid", Phone = "998901234567", CreatedAt = AppClock.Iso() });
        await ctx.SaveChangesAsync();

        var restored = new Lead { FullName = "Eski lid", Phone = "+998 90 123 45 67" };
        Assert.Equal("Yangi lid", await ArchiveService.ConflictingLeadNameAsync(ctx, restored));
        Assert.Null(await ArchiveService.ConflictingLeadNameAsync(ctx, new Lead { Phone = "+998 91 000 00 00" }));
        Assert.Null(await ArchiveService.ConflictingLeadNameAsync(ctx, new Lead { Phone = "123" }));
    }

    /* ============================ MSG-1 — Telegram ulanishini uzish ============================ */

    [Fact]
    public async Task Raqam_ozgarsa_ESKI_raqamli_Telegram_ulanishi_uziladi__moslari_qoladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = new Student { FullName = "Ali", Phone = "+998-90-111-11-11", ParentPhone = "+998-90-222-22-22", FatherPhone = "+998-90-222-22-22" };
        ctx.Students.Add(s);
        var parentOld = new TelegramRegistration { StudentId = s.Id, ChatId = 1, Phone = "998902222222", ParentName = "Ota" };
        var own = new TelegramRegistration { StudentId = s.Id, ChatId = 2, Phone = "998901111111" };
        var unknown = new TelegramRegistration { StudentId = s.Id, ChatId = 3, Phone = "" };
        ctx.TelegramRegistrations.AddRange(parentOld, own, unknown);
        await ctx.SaveChangesAsync();

        var before = TelegramLinks.PhonesSignature(s);
        s.ParentPhone = s.FatherPhone = "+998-90-333-33-33";   // ota-ona raqami tuzatildi
        Assert.True(TelegramLinks.PhonesChanged(before, s));
        var removed = await TelegramLinks.PruneStaleAsync(ctx, Audit(ctx), s, "raqam o'zgardi");
        await ctx.SaveChangesAsync();

        Assert.Equal(parentOld.Id, Assert.Single(removed).Id);
        var left = await ctx.TelegramRegistrations.AsNoTracking().Select(r => r.Id).ToListAsync();
        Assert.Equal(new[] { own.Id, unknown.Id }.OrderBy(x => x), left.OrderBy(x => x));   // raqamsiz — taxmin bilan uzilmaydi
        var audit = Assert.Single(await ctx.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(AuditService.EntityStudent, audit.EntityType);
        Assert.Contains("Telegram ulanishi uzildi", audit.Summary);

        var links = await TelegramLinks.ListAsync(ctx, s);
        Assert.Equal(2, links.Count);
        Assert.False(links.Single(l => l.Id == unknown.Id).PhoneMatches);
        Assert.True(links.Single(l => l.Id == own.Id).PhoneMatches);
    }

    [Fact]
    public void Faqat_FORMAT_ozgarsa_raqam_ozgargan_hisoblanmaydi()
    {
        var s = new Student { Phone = "+998-90-111-11-11" };
        var before = TelegramLinks.PhonesSignature(s);
        s.Phone = "901111111";
        Assert.False(TelegramLinks.PhonesChanged(before, s));
    }

    /* ============================ CRM — o'chirilgan o'quvchining lidi ============================ */

    [Fact]
    public async Task Oquvchi_BUTUNLAY_ochirilsa_lidi_QAYTA_OCHILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = new Student { FullName = "Dublikat" };
        var lead = new Lead { FullName = "Dublikat", ConvertedStudentId = s.Id, Stage = "st1", CreatedAt = AppClock.Iso() };
        ctx.Students.Add(s);
        ctx.Leads.Add(lead);
        await ctx.SaveChangesAsync();

        await StudentPurge.PurgeAsync(ctx, s.Id, actorName: "Admin");
        await ctx.SaveChangesAsync();

        var l = await ctx.Leads.AsNoTracking().SingleAsync();
        Assert.Null(l.ConvertedStudentId);
        Assert.True(LeadLoss.IsLost(l));                       // "ochiq lid" = aylantirilmagan VA yo'qotilmagan
        Assert.Equal(StudentPurge.LeadLostReason, l.LostReason);
        Assert.Equal("st1", l.Stage);                          // bosqich tegilmaydi
        Assert.Equal(0, await ctx.Leads.CountAsync(x => x.ConvertedStudentId == null && x.LostAt == null));
        Assert.Contains(await ctx.LeadEvents.AsNoTracking().ToListAsync(), e => e.Type == LeadLoss.EventLost);
    }

    /* ============================ MEM — guruh sig'imi (yaratish/import) ============================ */

    [Fact]
    public async Task Sigim__faqat_tirik_active_trial_sanaladi__muzlatilgan_va_ARXIVDAGI_sanalmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "G", Capacity = 2 };
        var a = new Student { FullName = "A" };
        var b = new Student { FullName = "B", IsArchived = true };
        var c = new Student { FullName = "C" };
        var d = new Student { FullName = "D" };
        ctx.Classes.Add(g);
        ctx.Students.AddRange(a, b, c, d);
        Member(ctx, a, g, "active", Today);
        Member(ctx, b, g, "trial", Today);    // eski arxiv — a'zolik tirik qolgan
        Member(ctx, c, g, "frozen", Today);
        var left = Member(ctx, d, g, "trial", Today);
        left.IsActive = false;
        await ctx.SaveChangesAsync();

        var used = await GroupSeats.OccupiedAsync(ctx, g.Id);
        Assert.Equal(1, used);
        Assert.True(GroupSeats.HasRoom(g, used));
        Assert.False(GroupSeats.HasRoom(g, 2));
        Assert.True(GroupSeats.HasRoom(new Group { Capacity = 0 }, 999));   // 0 = cheklanmagan
        Assert.False(GroupSeats.TakesSeat("frozen"));
        Assert.True(GroupSeats.TakesSeat("trial"));
    }
}
