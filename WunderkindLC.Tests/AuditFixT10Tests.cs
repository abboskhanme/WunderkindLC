using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

// Legacy entities of removed modules (2026-10): purge / backup / MCP no-read coverage still seeds them.
#pragma warning disable CS0618

namespace WunderkindLC.Tests;

/// <summary>
/// Audit (2026-10-07) T10 tuzatishlari: xona tahriri faollikni saqlaydi (C10), kursni o'chirish
/// darvozasi (C11), zaxira JSON'da maxfiy qiymatlar yo'q (ADM-2), xodim o'chirilganda havolalar
/// bo'shatiladi (ADM-5), davomat sababi darvozasi, kurs nomi noyobligi va sozlama auditi (ADM-4).
/// </summary>
public class AuditFixT10Tests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    // ===================== C10 — xona tahriri faollikni O'CHIRMAYDI =====================

    [Fact]
    public void Xona_tahriri_isActive_yuborilmasa_null_boladi_va_faollik_saqlanadi()
    {
        // Klient (rooms.ts) ilgari AYNAN shunday payload yuborardi — isActive umuman yo'q.
        const string clientPayload = """{"name":"301","capacity":25,"building":"A-blok","location":""}""";
        var req = JsonSerializer.Deserialize<UpdateRoomRequest>(clientPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Null(req.IsActive);
        Assert.True(RoomRules.ResolveIsActive(current: true, req.IsActive));
        Assert.False(RoomRules.ResolveIsActive(current: false, req.IsActive));
        // Aniq yuborilgan qiymat — hurmat qilinadi.
        Assert.False(RoomRules.ResolveIsActive(current: true, requested: false));
        Assert.True(RoomRules.ResolveIsActive(current: false, requested: true));
    }

    [Fact]
    public async Task Xona_ochirish_darvozasi_faqat_arxivlanmagan_guruhlarni_sanaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.Rooms.AddRange(new Room { Id = "r1", Name = "301" }, new Room { Id = "r2", Name = "302" });
        db.Context.Classes.AddRange(
            new Group { Name = "G1", RoomId = "r1" },
            new Group { Name = "G2", RoomId = "r1", IsArchived = true },
            new Group { Name = "G3", RoomId = "r2", IsArchived = true });
        await db.Context.SaveChangesAsync();

        Assert.Equal(1, await RoomRules.LiveGroupsAsync(db.Context, "r1"));
        Assert.Equal(0, await RoomRules.LiveGroupsAsync(db.Context, "r2"));
    }

    [Fact]
    public async Task Xona_bandligi_arxivlangan_oquvchini_sanamaydi()
    {
        // Eski arxivlanganlarda a'zolik hamon "active" turgan bo'lishi mumkin (arxiv = ketdi).
        using var db = TestDb.Sqlite();
        var room = new Room { Name = "401", Capacity = 10 };
        var g = new Group { Name = "G", RoomId = room.Id, StartTime = "09:00", EndTime = "10:00", Days = new() { 0 } };
        var live = new Student { FullName = "Faol" };
        var archived = new Student { FullName = "Arxivda", IsArchived = true };
        db.Context.Rooms.Add(room);
        db.Context.Classes.Add(g);
        db.Context.Students.AddRange(live, archived);
        db.Context.StudentGroups.AddRange(
            new StudentGroup { StudentId = live.Id, GroupId = g.Id, IsActive = true, Status = "active" },
            new StudentGroup { StudentId = archived.Id, GroupId = g.Id, IsActive = true, Status = "active" });
        await db.Context.SaveChangesAsync();

        var svc = new RoomUtilizationService(db.Context);
        Assert.Equal(1, Assert.Single(await svc.GetRoomUtilizationAsync()).CurrentStudents);
        Assert.Equal(1, (await svc.GetRoomDetailMetricAsync(room.Id))!.ActualStudents);
        Assert.Equal(1, (await svc.GetRoomCapacityAsync(room.Id))!.ActualStudents);
    }

    // ===================== C11 — kursni o'chirish darvozasi =====================

    [Fact]
    public async Task Kurs_ochirish_ARXIVDAGI_guruh_bolsa_ham_rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        var course = new Subject { Name = "Matematika", Price = 300_000 };
        db.Context.Subjects.Add(course);
        db.Context.Classes.Add(new Group { Name = "M-1", CourseId = course.Id, IsArchived = true });
        await db.Context.SaveChangesAsync();

        var refs = await CourseCatalog.CountReferencesAsync(db.Context, course.Id);

        Assert.Equal(0, refs.Groups);
        Assert.Equal(1, refs.ArchivedGroups);
        var msg = refs.BlockMessage();
        Assert.NotNull(msg);
        Assert.Contains("1 ta arxivdagi guruh", msg);
    }

    [Fact]
    public async Task Kurs_ochirish_sertifikat_bonus_test_andoza_uzaytirish_bolsa_rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        var course = new Subject { Name = "Ingliz tili" };
        var student = new Student { FullName = "Ali Valiyev" };
        db.Context.Subjects.Add(course);
        db.Context.Students.Add(student);
        db.Context.StudentCertificates.Add(new StudentCertificate { StudentId = student.Id, CourseId = course.Id });
        db.Context.CertificateTemplates.Add(new CertificateTemplate { Name = "A", CourseId = course.Id });
        db.Context.RetentionBonusTracks.Add(new RetentionBonusTrack { StudentId = student.Id, CourseId = course.Id });
        db.Context.LevelTests.Add(new LevelTest { Title = "Daraja", CourseId = course.Id, Slug = "x1" });
        db.Context.StudentExtensions.Add(new StudentExtension { StudentId = student.Id, CourseId = course.Id });
        await db.Context.SaveChangesAsync();

        var refs = await CourseCatalog.CountReferencesAsync(db.Context, course.Id);

        Assert.Equal(1, refs.Certificates);
        Assert.Equal(1, refs.CertificateTemplates);
        Assert.Equal(1, refs.BonusTracks);
        Assert.Equal(1, refs.LevelTests);
        Assert.Equal(1, refs.Extensions);
        Assert.Equal(5, refs.Total);
        Assert.NotNull(refs.BlockMessage());
    }

    [Fact]
    public async Task Bogliqsiz_kurs_bitta_SaveChanges_da_ochadi_dastur_qoladi_oqituvchidan_olinadi()
    {
        using var db = TestDb.Sqlite();
        var course = new Subject { Name = "Kimyo", Price = 200_000 };
        var other = new Subject { Name = "Fizika" };
        var curriculum = new Curriculum { Name = "Kimyo dasturi" };
        db.Context.Subjects.AddRange(course, other);
        db.Context.Curricula.Add(curriculum);
        db.Context.SubjectCurricula.Add(new SubjectCurriculum { SubjectId = course.Id, CurriculumId = curriculum.Id });
        db.Context.SubjectPriceBands.Add(new SubjectPriceBand { SubjectId = course.Id, GradeFrom = 1, GradeTo = 4, Price = 150_000 });
        db.Context.Teachers.Add(new Teacher { Id = "t1", FullName = "O'qituvchi", SubjectIds = new() { course.Id, other.Id } });
        await db.Context.SaveChangesAsync();

        var refs = await CourseCatalog.CountReferencesAsync(db.Context, course.Id);
        Assert.Null(refs.BlockMessage());

        var (curricula, teachers) = await CourseCatalog.DetachForDeleteAsync(db.Context, course.Id);
        db.Context.Subjects.Remove(await db.Context.Subjects.FindAsync(course.Id) ?? throw new Exception());
        await db.Context.SaveChangesAsync();

        Assert.Equal(1, curricula);
        Assert.Equal(1, teachers);
        await using var read = db.NewContext();
        Assert.False(await read.Subjects.AnyAsync(s => s.Id == course.Id));
        Assert.False(await read.SubjectCurricula.AnyAsync());
        Assert.False(await read.SubjectPriceBands.AnyAsync());
        Assert.True(await read.Curricula.AnyAsync(c => c.Id == curriculum.Id));   // dasturning O'ZI qoladi
        Assert.Equal(new List<string> { other.Id }, (await read.Teachers.FirstAsync()).SubjectIds);
    }

    // ===================== Kurs nomi NOYOBLIGI va lidlarni ko'chirish =====================

    [Theory]
    [InlineData("  Oʻzbek   tili ", "o'zbek tili")]
    [InlineData("MATEMATIKA", "matematika")]
    [InlineData("Ona tili’", "ona tili'")]
    public void Kurs_nomi_kaliti_registr_bosliq_va_apostrofdan_qati_nazar(string a, string b)
        => Assert.Equal(CourseCatalog.NameKey(b), CourseCatalog.NameKey(a));

    [Fact]
    public void Kurs_nomi_saqlashda_ortiqcha_bosliqlar_olinadi()
        => Assert.Equal("Ingliz tili B1", CourseCatalog.CleanName("  Ingliz   tili  B1 "));

    [Fact]
    public async Task Kurs_nomi_band_bolsa_topiladi_ozi_esa_hisobga_olinmaydi()
    {
        using var db = TestDb.Sqlite();
        var math = new Subject { Name = "Matematika" };
        db.Context.Subjects.AddRange(math, new Subject { Name = "Fizika" });
        await db.Context.SaveChangesAsync();

        Assert.Equal("Matematika", await CourseCatalog.NameTakenByAsync(db.Context, " matematika ", null));
        Assert.Null(await CourseCatalog.NameTakenByAsync(db.Context, "Matematika", math.Id));   // o'zi
        Assert.Null(await CourseCatalog.NameTakenByAsync(db.Context, "Kimyo", null));
    }

    [Fact]
    public async Task Lidlar_kursi_qayta_nomlanganda_kochadi_lekin_takror_nomli_kurs_bolsa_kochmaydi()
    {
        using var db = TestDb.Sqlite();
        var a = new Subject { Name = "Ingliz tili" };
        db.Context.Subjects.Add(a);
        db.Context.Leads.AddRange(
            new Lead { FullName = "L1", InterestSubject = "Ingliz tili" },
            new Lead { FullName = "L2", InterestSubject = "Matematika" });
        await db.Context.SaveChangesAsync();

        var moved = await CourseCatalog.RelabelLeadsAsync(db.Context, a.Id, "Ingliz tili", "Ingliz tili (IELTS)");
        await db.Context.SaveChangesAsync();
        Assert.Equal(1, moved);
        Assert.Equal(1, await db.Context.Leads.CountAsync(l => l.InterestSubject == "Ingliz tili (IELTS)"));

        // Qoidadan OLDINGI takror nom: boshqa kurs ham "Matematika" — birini qayta nomlash ikkinchisining
        // lidlarini o'g'irlamasin.
        var m1 = new Subject { Name = "Matematika" };
        db.Context.Subjects.AddRange(m1, new Subject { Name = "Matematika" });
        await db.Context.SaveChangesAsync();
        Assert.Equal(0, await CourseCatalog.RelabelLeadsAsync(db.Context, m1.Id, "Matematika", "Matematika 2"));
    }

    // ===================== ADM-2 — zaxira JSON'da maxfiy qiymatlar YO'Q =====================

    [Fact]
    public void SecretScrub_nomlar_qoidasi()
    {
        Assert.True(SecretScrub.IsSecretName("PasswordHash"));
        Assert.True(SecretScrub.IsSecretName("InitialPassword"));
        Assert.True(SecretScrub.IsSecretName("InstagramCapiToken"));
        Assert.True(SecretScrub.IsSecretName("AccessToken"));
        Assert.True(SecretScrub.IsSecretName("RtspUrl"));
        Assert.False(SecretScrub.IsSecretName("TokenExpiresAt"));   // sana, token emas
        Assert.False(SecretScrub.IsSecretName("FileHash"));         // kontent xeshi
        Assert.False(SecretScrub.IsSecretName("FullName"));
    }

    [Fact]
    public async Task Zaxira_JSON_da_CAPI_token_parol_xeshi_va_ochiq_parol_yoq()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(new CenterMeta { Name = "Markaz", InstagramCapiToken = "CAPI-SECRET-777" });
        var oldStaff = new AppUser
        {
            FullName = "Eski Xodim", Email = "eski", Role = Roles.Staff,
            PasswordHash = "100000.SALT.HASH-XYZ", InitialPassword = "Plain-Pass-1",
        };
        // Eski (tuzatishdan oldingi) arxiv surati — AppUser TO'LIQ seriyalangan.
        db.Context.ArchivedRecords.Add(new ArchivedRecord
        {
            Type = "staff", EntityId = oldStaff.Id, Title = oldStaff.FullName, Json = JsonSerializer.Serialize(oldStaff),
        });
        db.Context.Users.Add(new AppUser { FullName = "Faol", Email = "faol", PasswordHash = "100000.S.LIVE-HASH", InitialPassword = "Live-Pass-2" });
        db.Context.CtiAgents.Add(new CtiAgent { Login = "agent1", PasswordHash = "AGENT-HASH-1", FcmToken = "FCM-TOKEN-1" });
        await db.Context.SaveChangesAsync();

        var (bytes, tables, _) = await BackupService.BuildJsonAsync(db.Context);
        var json = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("CAPI-SECRET-777", json);
        Assert.DoesNotContain("InstagramCapiToken", json);
        Assert.DoesNotContain("HASH-XYZ", json);
        Assert.DoesNotContain("Plain-Pass-1", json);
        Assert.DoesNotContain("LIVE-HASH", json);
        Assert.DoesNotContain("Live-Pass-2", json);
        Assert.DoesNotContain("AGENT-HASH-1", json);
        Assert.DoesNotContain("FCM-TOKEN-1", json);
        // Ma'lumotning o'zi esa bor: arxiv sarlavhasi, markaz nomi, agent logini.
        Assert.Contains("Eski Xodim", json);
        Assert.Contains("Markaz", json);
        Assert.Contains("agent1", json);
    }

    [Fact]
    public async Task Zaxira_JSON_biznes_jadvallarini_oz_ichiga_oladi_va_kiritilmaganlarini_ochiq_aytadi()
    {
        using var db = TestDb.Sqlite();
        var (bytes, tables, _) = await BackupService.BuildJsonAsync(db.Context);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        foreach (var t in new[] { "books", "contracts", "calls", "contactRequests", "jobApplications",
                                  "kpiProfiles", "studentDiscounts", "retentionBonusAwards", "transactionTypes" })
            Assert.True(root.TryGetProperty(t, out _), $"'{t}' zaxirada yo'q");
        Assert.True(root.TryGetProperty("excluded", out var excluded));
        Assert.Contains(excluded.EnumerateArray(), e => e.GetString() == "auditLogs");
        Assert.True(tables > 100);

        // Hajm chegarasi uchun ikkinchi urinish: og'ir jadvallar chiqadi va `excluded` da yoziladi.
        var (compact, _, _) = await BackupService.BuildJsonAsync(db.Context, includeHeavy: false);
        using var compactDoc = JsonDocument.Parse(compact);
        Assert.False(compactDoc.RootElement.TryGetProperty("chatMessages", out _));
        Assert.Contains(compactDoc.RootElement.GetProperty("excluded").EnumerateArray(), e => e.GetString() == "chatMessages");
    }

    [Fact]
    public void Kunlik_zaxira_restartdan_keyin_takrorlanmaydi_lekin_ertalabki_qolda_yuborish_jadvalni_bekor_qilmaydi()
    {
        var today = new DateOnly(2026, 10, 7);
        const int target = 21 * 60;   // 21:00
        Assert.True(BackupSchedulerService.AlreadySentToday(new DateTime(2026, 10, 7, 21, 3, 0), today, target));
        Assert.False(BackupSchedulerService.AlreadySentToday(new DateTime(2026, 10, 7, 9, 0, 0), today, target));
        Assert.False(BackupSchedulerService.AlreadySentToday(new DateTime(2026, 10, 6, 21, 3, 0), today, target));
        Assert.False(BackupSchedulerService.AlreadySentToday(null, today, target));
    }

    // ===================== ADM-5 — xodim o'chirilganda havolalar bo'shatiladi =====================

    [Fact]
    public void Xodim_arxiv_suratida_parol_xeshi_va_ochiq_parol_yoq()
    {
        var user = new AppUser { FullName = "Kassir", Email = "kassir1", PasswordHash = "H-1", InitialPassword = "P-1", Role = Roles.Staff };
        var json = SecretScrub.ToScrubbedNode(user)!.ToJsonString();

        Assert.DoesNotContain("H-1", json);
        Assert.DoesNotContain("P-1", json);
        // Tiklash (ArchiveController) uchun kerakli maydonlar qoladi.
        var restored = JsonSerializer.Deserialize<AppUser>(json)!;
        Assert.Equal(user.Id, restored.Id);
        Assert.Equal("kassir1", restored.Email);
        Assert.Equal("", restored.PasswordHash);   // parolsiz — admin yangisini yaratadi
    }

    [Fact]
    public async Task Xodim_ochirilganda_jonli_havolalar_boshatiladi_tarix_esa_qoladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        const string uid = "staff-1";
        ctx.Users.Add(new AppUser { Id = uid, FullName = "Operator", Email = "op", Role = Roles.Staff });
        ctx.KpiProfiles.Add(new KpiProfile { UserId = uid, RoleCode = "operator", IsActive = true });
        var openLead = new Lead { FullName = "Ochiq", AssigneeUserId = uid };
        var convertedLead = new Lead { FullName = "Aylangan", AssigneeUserId = uid, ConvertedStudentId = "s1" };
        var lostLead = new Lead { FullName = "Yo'qotilgan", AssigneeUserId = uid, LostAt = "2026-09-01" };
        ctx.Leads.AddRange(openLead, convertedLead, lostLead);
        var box = new Cashbox { Name = "Kassa 1", ResponsibleUserId = uid, ResponsibleName = "Operator" };
        ctx.Cashboxes.Add(box);
        var staffLink = new TelegramRegistration { UserId = uid, ChatId = 1 };
        var parentLink = new TelegramRegistration { UserId = uid, StudentId = "s1", ChatId = 2 };
        ctx.TelegramRegistrations.AddRange(staffLink, parentLink);
        await ctx.SaveChangesAsync();

        var r = await StaffRelease.ReleaseAsync(ctx, uid);
        await ctx.SaveChangesAsync();

        Assert.Equal(new StaffRelease.Result(1, 1, 1, 2), r);
        Assert.Contains("1 ta ochiq lid", r.Summary());

        await using var read = db.NewContext();
        Assert.False((await read.KpiProfiles.SingleAsync()).IsActive);
        Assert.Null((await read.Leads.SingleAsync(l => l.Id == openLead.Id)).AssigneeUserId);
        // TARIX tegilmaydi — yo'qotish hisoboti va KPI shundan o'qiydi.
        Assert.Equal(uid, (await read.Leads.SingleAsync(l => l.Id == convertedLead.Id)).AssigneeUserId);
        Assert.Equal(uid, (await read.Leads.SingleAsync(l => l.Id == lostLead.Id)).AssigneeUserId);
        var savedBox = await read.Cashboxes.SingleAsync();
        Assert.Null(savedBox.ResponsibleUserId);
        Assert.Null(savedBox.ResponsibleName);
        // Xodim bog'lanishi o'chdi; ota-ona bog'lanishi qoldi (faqat xodim qismi uzildi).
        Assert.False(await read.TelegramRegistrations.AnyAsync(x => x.Id == staffLink.Id));
        var parent = await read.TelegramRegistrations.SingleAsync(x => x.Id == parentLink.Id);
        Assert.Null(parent.UserId);
    }

    // ===================== Davomat sabablari darvozasi =====================

    private static async Task<TestDb> SeedReasonsAsync()
    {
        var db = TestDb.Sqlite();
        db.Context.AbsenceReasons.AddRange(
            new AbsenceReason { Id = "sick", Name = "Kasal", Short = "K" },
            new AbsenceReason { Id = "unused", Name = "Boshqa", Short = "B" },
            new AbsenceReason { Id = "late", Name = "Kech keldi", Short = "Kch", IsLate = true });
        db.Context.JournalEntries.AddRange(
            new JournalEntry { ClassId = "g", SubjectId = "c", StudentId = "s", Date = "2026-09-01", ReasonId = "sick" },
            new JournalEntry { ClassId = "g", SubjectId = "c", StudentId = "s", Date = "2026-09-02", ReasonId = "late" });
        await db.Context.SaveChangesAsync();
        return db;
    }

    private static List<AbsenceReasonDto> Reasons(params (string Id, string Name, bool Late)[] items) =>
        items.Select(i => new AbsenceReasonDto(i.Id, i.Name, "", i.Late)).ToList();

    [Fact]
    public async Task Jurnalda_ishlatilgan_sababni_ochirish_rad_etiladi()
    {
        using var db = await SeedReasonsAsync();
        var error = await AbsenceReasonRules.ValidateReplaceAsync(db.Context,
            Reasons(("unused", "Boshqa", false), ("late", "Kech keldi", true)));

        Assert.NotNull(error);
        Assert.Contains("«Kasal» jurnalda 1 ta yozuvda", error);
    }

    [Fact]
    public async Task Ishlatilgan_sababning_kech_keldi_belgisini_almashtirish_rad_etiladi()
    {
        using var db = await SeedReasonsAsync();
        var error = await AbsenceReasonRules.ValidateReplaceAsync(db.Context,
            Reasons(("sick", "Kasal", false), ("unused", "Boshqa", false), ("late", "Kech keldi", false)));

        Assert.NotNull(error);
        Assert.Contains("«Kech keldi»", error);
    }

    [Fact]
    public async Task Ishlatilmagan_sababni_ochirish_va_nomini_ozgartirish_mumkin()
    {
        using var db = await SeedReasonsAsync();
        var error = await AbsenceReasonRules.ValidateReplaceAsync(db.Context,
            Reasons(("sick", "Kasallik", false), ("late", "Kechikdi", true), ("new-1", "Yangi", false)));

        Assert.Null(error);
    }

    // ===================== ADM-4 — sozlama auditi (qiymatlar bilan) =====================

    [Fact]
    public async Task Sozlama_auditi_pul_qiymatini_eski_yangi_korinishida_maxfiyni_esa_nomsiz_yozadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(new CenterMeta { RetentionDefaultAmount = 100_000, InstagramCapiToken = "OLD-TOKEN" });
        await db.Context.SaveChangesAsync();

        var meta = await db.Context.CenterMeta.SingleAsync();
        meta.RetentionDefaultAmount = 150_000;
        meta.InstagramCapiToken = "NEW-TOKEN";
        var audit = new AuditService(db.Context, new NoHttpContext());
        SettingsAudit.Record<CenterMeta>(audit, db.Context.ChangeTracker, "Ushlab turish bonusi", withValues: true,
            labels: new Dictionary<string, string> { [nameof(CenterMeta.RetentionDefaultAmount)] = "Standart summa" });
        await db.Context.SaveChangesAsync();

        var log = await db.Context.AuditLogs.SingleAsync();
        Assert.Equal("CenterMeta", log.EntityType);
        Assert.Equal("settings", AuditSections.SectionOf(log.EntityType));
        Assert.Contains("Standart summa: 100 000 → 150 000", log.Summary);
        Assert.Contains("InstagramCapiToken", log.Summary);   // nomi — ha
        Assert.DoesNotContain("TOKEN", (log.Summary.Replace("InstagramCapiToken", "")) + log.Before + log.After);
    }

    [Theory]
    [InlineData("ActionReason", "settings")]
    [InlineData("AbsenceReason", "settings")]
    [InlineData("Branch", "settings")]
    [InlineData("Room", "classes")]
    [InlineData("StudentAiAccess", "students")]
    [InlineData("CertificateTemplate", "settings")]
    public void Yangi_katalog_audit_turlari_bolimga_tushadi(string entityType, string section)
        => Assert.Equal(section, AuditSections.SectionOf(entityType));
}
