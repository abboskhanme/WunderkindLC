using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-07 audit tuzatishlari (T8 — CRM lidlari, lid → o'quvchi konversiyasi, lid kanallari).
/// Rasmiy manba: <c>.claude/rules/crm-leads.md</c>, <c>lead-forms.md</c>, <c>audit.md</c>.
///
/// <para>Qamrov: C5 (konversiya = to'liq o'quvchi yaratish: telefonlar, akkaunt, a'zolik, audit,
/// guruh to'lgan, dublikat, mavjudga bog'lash, sinov darsi) · CRM-1 (aylantirilgan lidga takroriy
/// murojaat yopishmaydi; CAPI) · CRM-3/4/5 (o'chirish) · CRM-9 (reklama lidi yangi/takroriy; lid SMS'i
/// 2026-10 da olib tashlandi) · IG-4 (vaqtinchalik ismlar) · sinov natijasini tuzatish.</para>
/// </summary>
public class AuditFixT8Tests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static AuditService Audit(AppDbContext ctx) => new(ctx, new NoHttpContext());
    private static readonly LeadConversion.Actor Manager = new("Menejer Ali", "u-1");
    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    private static Func<string, string> Accounts(AppDbContext ctx) =>
        name => AccountFactory.CreateAccountFor(ctx, "student", name).Id;

    private static Lead NewLead(string name = "Aliyev Ali", string phone = "901234567") => new()
    {
        FullName = name,
        Phone = phone,
        FatherFullName = "Aliyev Vali",
        FatherPhone = "+998 91 111 22 33",
        MotherFullName = "Aliyeva Gulnora",
        MotherPhone = "93 444 55 66",
        BirthDate = "2014-05-01",
        Gender = "male",
        CreatedAt = "2026-09-01T10:00:00",
    };

    // ============================ C5 — konversiya ============================

    [Fact]
    public async Task Konversiya_telefonlarni_akkauntni_azolikni_va_auditni_yozadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var lead = NewLead();
        var group = new Group { Name = "IELTS-1", Capacity = 10 };
        db.Leads.Add(lead);
        db.Classes.Add(group);
        await db.SaveChangesAsync();

        var r = await LeadConversion.ConvertAsync(db, Audit(db), lead, group, "2026-10-01", Accounts(db), Manager);
        await db.SaveChangesAsync();

        var s = await db.Students.SingleAsync();
        // Lidning O'Z telefoni ko'chadi (ilgari tushib qolardi) — normalizatsiya bilan.
        Assert.Equal("+998-90-123-45-67", s.Phone);
        Assert.Equal("Aliyev Vali", s.FatherFullName);
        Assert.Equal("+998-91-111-22-33", s.FatherPhone);
        Assert.Equal("Aliyeva Gulnora", s.MotherFullName);
        Assert.Equal("+998-93-444-55-66", s.MotherPhone);
        // ASOSIY ota-ona — ota (AddStudent qoidasi).
        Assert.Equal("Aliyev Vali", s.ParentFullName);
        Assert.Equal("+998-91-111-22-33", s.ParentPhone);
        Assert.Equal("2026-10-01", s.EnrollmentDate);
        Assert.False(string.IsNullOrWhiteSpace(s.CreatedAt));
        Assert.Equal("IELTS-1", s.ClassName);
        // Login akkaunt yaratilgan.
        var user = await db.Users.SingleAsync(u => u.Id == s.UserId);
        Assert.Equal("student", user.Role);

        // A'zolik — AddMember bilan bir xil: SINOV, RecordedAt = bugun.
        var sg = await db.StudentGroups.SingleAsync();
        Assert.Equal("trial", sg.Status);
        Assert.True(sg.IsActive);
        Assert.Equal("2026-10-01", sg.JoinedAt);
        Assert.Equal(Today, sg.RecordedAt);
        Assert.Equal(LeadConversion.MembershipOutcome.Added, r.Membership);
        Assert.True(r.MembershipAdded);

        // Audit: o'quvchi yaratildi + a'zolik ("{groupId}:{studentId}") + lid yopildi.
        var logs = await db.AuditLogs.ToListAsync();
        Assert.Contains(logs, a => a.EntityType == AuditService.EntityStudent && a.Action == "create" && a.StudentId == s.Id);
        Assert.Contains(logs, a => a.EntityType == "Membership" && a.EntityId == $"{group.Id}:{s.Id}");
        Assert.Contains(logs, a => a.EntityType == "Lead" && a.EntityId == lead.Id);
        Assert.All(logs, a => Assert.NotEqual(AuditSections.Other, AuditSections.SectionOf(a.EntityType)));

        // Lid yopildi (KPI "shartnoma").
        var l = await db.Leads.SingleAsync();
        Assert.Equal(s.Id, l.ConvertedStudentId);
        Assert.Equal(Today, l.ClosedAt);
        Assert.Equal("u-1", l.ClosedByUserId);
        Assert.Contains(await db.LeadEvents.ToListAsync(), e => e.Type == "convert" && e.ActorUserId == "u-1");
    }

    [Fact]
    public async Task Guruh_tolgan_bolsa_azolik_ham_ClassName_ham_yozilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var group = new Group { Name = "Kids-A", Capacity = 1 };
        var other = new Student { FullName = "Band o'rin" };
        db.Classes.Add(group);
        db.Students.Add(other);
        db.StudentGroups.Add(new StudentGroup { StudentId = other.Id, GroupId = group.Id, IsActive = true, Status = "active" });
        var lead = NewLead();
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var r = await LeadConversion.ConvertAsync(db, Audit(db), lead, group, Today, Accounts(db), Manager);
        await db.SaveChangesAsync();

        Assert.Equal(LeadConversion.MembershipOutcome.GroupFull, r.Membership);
        Assert.False(r.MembershipAdded);
        var s = await db.Students.SingleAsync(x => x.Id == r.Student.Id);
        // Ilgari ClassName baribir guruh nomiga yozilardi — o'quvchi guruhda bo'lmagan holda.
        Assert.Equal("", s.ClassName);
        Assert.Single(db.StudentGroups); // faqat eski a'zo
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.EntityType == "Membership");
        Assert.Contains("guruh to'lgan", (await db.LeadEvents.SingleAsync(e => e.Type == "convert")).Text);
    }

    [Fact]
    public async Task Arxivdagi_oquvchining_tirik_azoligi_orin_egallamaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var group = new Group { Name = "Kids-B", Capacity = 1 };
        var archived = new Student { FullName = "Ketgan", IsArchived = true };
        db.Classes.Add(group);
        db.Students.Add(archived);
        db.StudentGroups.Add(new StudentGroup { StudentId = archived.Id, GroupId = group.Id, IsActive = true, Status = "active" });
        await db.SaveChangesAsync();

        Assert.Equal(0, await LeadConversion.SeatsTakenAsync(db, group.Id));
    }

    [Fact]
    public async Task Otgan_kutilayotgan_sinov_qoldi_bolib_yopiladi_kelajakdagisi_tegilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var lead = NewLead();
        db.Leads.Add(lead);
        var past = AppClock.Today.AddDays(-3).ToString("yyyy-MM-dd");
        var future = AppClock.Today.AddDays(5).ToString("yyyy-MM-dd");
        db.TrialLessons.Add(new TrialLesson { LeadId = lead.Id, ScheduledAt = past + "T15:00", Result = "pending" });
        db.TrialLessons.Add(new TrialLesson { LeadId = lead.Id, ScheduledAt = future + "T15:00", Result = "pending" });
        await db.SaveChangesAsync();

        await LeadConversion.ConvertAsync(db, Audit(db), lead, null, Today, Accounts(db), Manager);
        await db.SaveChangesAsync();

        var trials = await db.TrialLessons.OrderBy(x => x.ScheduledAt).ToListAsync();
        Assert.Equal("stayed", trials[0].Result);
        Assert.Equal(past, trials[0].AttendedAt);   // KPI "sinovga keldi" — sinov kuni oyiga
        Assert.Equal("pending", trials[1].Result);
        Assert.Null(trials[1].AttendedAt);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("2026-10-01", true)]
    [InlineData("2026-13-40", false)]
    [InlineData("01.10.2026", false)]
    [InlineData("2026", false)]
    public void Qabul_sanasi_tekshiriladi(string raw, bool ok)
    {
        Assert.Equal(ok, LeadConversion.TryEnrollmentDate(raw, new DateOnly(2026, 10, 7), out var date));
        if (raw == "") Assert.Equal("2026-10-07", date);
    }

    [Fact]
    public async Task Dublikat_ism_va_telefon_mos_kelsa_topiladi_arxivdagi_va_faqat_telefon_mos_emas()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        // Ism (apostrof/registr farqi bilan) + ota telefoni mos — o'sha odam.
        var same = new Student { FullName = "aliyev  ali", FatherPhone = "+998-91-111-22-33", ClassName = "IELTS-1" };
        // Aka-uka: telefon mos, ism boshqa — dublikat EMAS.
        var sibling = new Student { FullName = "Aliyev Hasan", ParentPhone = "911112233" };
        // Arxivdagi o'quvchi — 409 ga kirmaydi.
        var archived = new Student { FullName = "Aliyev Ali", Phone = "901234567", IsArchived = true };
        db.Students.AddRange(same, sibling, archived);
        var lead = NewLead();
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var dups = await LeadConversion.FindDuplicateStudentsAsync(db, lead);
        var d = Assert.Single(dups);
        Assert.Equal(same.Id, d.Id);
        Assert.Equal("IELTS-1", d.ClassName);
    }

    [Fact]
    public async Task Telefonsiz_lid_uchun_dublikat_izlanmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Students.Add(new Student { FullName = "Aliyev Ali" });
        var lead = new Lead { FullName = "Aliyev Ali" };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        Assert.Empty(await LeadConversion.FindDuplicateStudentsAsync(db, lead));
    }

    [Fact]
    public async Task Mavjud_oquvchiga_boglash_yangi_oquvchi_yaratmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var existing = new Student { FullName = "Aliyev Ali", Phone = "+998-90-123-45-67", ClassName = "" };
        var group = new Group { Name = "IELTS-2" };
        db.Students.Add(existing);
        db.Classes.Add(group);
        var lead = NewLead();
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var r = await LeadConversion.LinkAsync(db, Audit(db), lead, existing, group, Today, Manager);
        await db.SaveChangesAsync();

        Assert.False(r.Created);
        Assert.Single(db.Students);
        Assert.Empty(db.Users); // akkaunt YARATILMAYDI
        Assert.Equal(existing.Id, (await db.Leads.SingleAsync()).ConvertedStudentId);
        Assert.Equal(LeadConversion.MembershipOutcome.Added, r.Membership);
        Assert.Equal("IELTS-2", (await db.Students.SingleAsync()).ClassName);

        // Ikkinchi marta shu guruhga — allaqachon a'zo, hech narsa qo'shilmaydi.
        var lead2 = NewLead();
        db.Leads.Add(lead2);
        await db.SaveChangesAsync();
        var r2 = await LeadConversion.LinkAsync(db, Audit(db), lead2, existing, group, Today, Manager);
        Assert.Equal(LeadConversion.MembershipOutcome.AlreadyMember, r2.Membership);
    }

    [Fact]
    public async Task Avval_chiqqan_guruhga_qayta_qoshishda_eski_davr_tarixga_kochadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var existing = new Student { FullName = "Aliyev Ali", Phone = "901234567" };
        var group = new Group { Name = "IELTS-3" };
        db.Students.Add(existing);
        db.Classes.Add(group);
        db.StudentGroups.Add(new StudentGroup
        {
            StudentId = existing.Id, GroupId = group.Id, IsActive = false, Status = "active",
            ActivatedAt = "2026-03-01", LeftAt = "2026-05-15",
        });
        var lead = NewLead();
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var r = await LeadConversion.LinkAsync(db, Audit(db), lead, existing, group, Today, Manager);
        await db.SaveChangesAsync();

        Assert.Equal(LeadConversion.MembershipOutcome.Readded, r.Membership);
        var sg = await db.StudentGroups.SingleAsync();
        Assert.True(sg.IsActive);
        Assert.Equal("trial", sg.Status);
        Assert.Null(sg.LeftAt);
        Assert.Equal("", sg.ActivatedAt);
        Assert.Contains("2026-03-01|2026-05-15", sg.PastPeriods);
    }

    // ============================ CRM-1 — takroriy murojaat ============================

    private static async Task<Lead> ConvertedLeadAsync(AppDbContext db, string studentName = "Aliyev Ali")
    {
        var student = new Student { FullName = studentName };
        db.Students.Add(student);
        var lead = new Lead
        {
            FullName = "Aliyev Ali", Phone = "+998901234567", ConvertedStudentId = student.Id,
            CreatedAt = "2026-01-10T10:00:00", ClosedAt = "2026-01-20",
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead;
    }

    [Fact]
    public async Task FindByPhone_aylantirilgan_lidni_qaytarmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await ConvertedLeadAsync(db);

        Assert.Null(await LeadIntake.FindByPhoneAsync(db, "90 123 45 67"));
        Assert.Equal("Avval o'quvchi bo'lgan: Aliyev Ali", await LeadIntake.FormerStudentNoteAsync(db, "901234567"));
    }

    [Fact]
    public async Task Aylantirilgan_lid_raqamidan_forma_kelsa_YANGI_lid_ochiladi_izoh_bilan()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var old = await ConvertedLeadAsync(db, studentName: "Aliyev Ali (o'quvchi)");
        db.LeadForms.Add(new LeadForm { Title = "Instagram", Slug = "ig", Source = "Instagram", IsActive = true });
        await db.SaveChangesAsync();

        var (result, error) = await LeadFormService.SubmitAsync(db, "ig",
            new LeadFormSubmitRequest("Aliyev Hasan", "+998901234567", null, 0, null, null, null));

        Assert.Null(error);
        Assert.NotNull(result);
        var fresh = await db.Leads.SingleAsync(l => l.Id != old.Id);
        Assert.Equal("Aliyev Hasan", fresh.FullName);
        Assert.Contains("Avval o'quvchi bo'lgan: Aliyev Ali (o'quvchi)", fresh.Note);
        Assert.True((await db.LeadFormSubmissions.SingleAsync()).IsNewLead);
        // Eski (yopilgan) lid tegilmaydi.
        Assert.Equal(0, (await db.Leads.SingleAsync(l => l.Id == old.Id)).RepeatCount);
    }

    [Fact]
    public async Task Instagram_suhbati_aylantirilgan_lidga_bogliq_bolsa_yangi_lid_ochiladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var old = await ConvertedLeadAsync(db);
        var conv = new IgConversation { IgUserId = "1", Username = "ali_v", Status = IgConst.StatusBot, LeadId = old.Id };
        db.IgConversations.Add(conv);
        await db.SaveChangesAsync();

        var (leadId, isNew) = await InstagramLeadBridge.UpsertAsync(db, conv,
            new IgAgentOutput("", "uz-Latn", "buying_intent", 80, true, false, false, "", "901234567", "IELTS", "Yana yozildi"),
            "Instagram");
        await db.SaveChangesAsync();

        Assert.True(isNew);
        Assert.NotEqual(old.Id, leadId);
        Assert.Equal(leadId, conv.LeadId);
    }

    [Theory]
    [InlineData("2026-01-20", null, "2026-03-01T10:00:00", null, true)]   // reklamadan OLDIN aylantirilgan
    [InlineData("2026-03-05", null, "2026-03-01T10:00:00", null, false)]  // reklamadan keyin — haqiqiy konversiya
    [InlineData("2026-03-01", null, "2026-03-01T18:00:00", null, false)]  // o'sha kuni — konversiya
    [InlineData(null, "2026-01-20T12:00:00", "2026-03-01T10:00:00", null, true)] // ClosedAt yo'q — convert hodisasi
    [InlineData(null, null, "2026-03-01T10:00:00", null, false)]          // sana noma'lum — eski xatti-harakat
    [InlineData("2026-01-20", null, "", "2026-03-02T09:00:00", true)]     // CreatedTime bo'sh — ReceivedAt
    public void CAPI_reklama_lididan_oldin_aylantirilgan_lid_konversiya_emas(
        string? closedAt, string? convertEvent, string adCreated, string? adReceived, bool expected) =>
        Assert.Equal(expected, MetaCapiService.ConvertedBeforeAd(closedAt, convertEvent, adCreated, adReceived));

    // ============================ CRM-3/4/5 — o'chirish ============================

    [Fact]
    public async Task Ochirish_IG_boglanishlarini_tozalaydi_va_tarixni_lead_history_ga_oladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var lead = NewLead();
        db.Leads.Add(lead);
        db.LeadEvents.Add(new LeadEvent { LeadId = lead.Id, Type = "created", Text = "Lid yaratildi", CreatedAt = "2026-09-01T10:00:00" });
        db.TrialLessons.Add(new TrialLesson { LeadId = lead.Id, GroupId = "g-1", ScheduledAt = "2026-09-05T15:00", Result = "came", AttendedAt = "2026-09-05" });
        var conv = new IgConversation { IgUserId = "2", Username = "ali", Status = IgConst.StatusBot, LeadId = lead.Id };
        var ad = new IgAdLead { LeadgenId = "777", LeadId = lead.Id };
        db.IgConversations.Add(conv);
        db.IgAdLeads.Add(ad);
        await db.SaveChangesAsync();

        var refused = await LeadRemoval.RemoveAsync(db, lead, "Dublikat", "Admin");
        await db.SaveChangesAsync();

        Assert.Null(refused);
        Assert.Empty(db.Leads);
        Assert.Empty(db.LeadEvents);
        Assert.Empty(db.TrialLessons);
        Assert.Null((await db.IgConversations.SingleAsync()).LeadId);
        Assert.Equal("", (await db.IgAdLeads.SingleAsync()).LeadId);

        var history = await db.ArchivedRecords.SingleAsync(r => r.Type == LeadRemoval.HistoryArchiveType);
        Assert.Equal(lead.Id, history.EntityId);
        using var json = JsonDocument.Parse(history.Json);
        Assert.Equal(1, json.RootElement.GetProperty("Events").GetArrayLength());
        Assert.Equal("came", json.RootElement.GetProperty("Trials")[0].GetProperty("Result").GetString());
        Assert.Single(db.ArchivedRecords, r => r.Type == "lead");
    }

    [Fact]
    public async Task Aylantirilgan_lid_ochirilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var lead = await ConvertedLeadAsync(db);

        var refused = await LeadRemoval.RemoveAsync(db, lead, null, "Admin");
        await db.SaveChangesAsync();

        Assert.Equal(LeadRemoval.ConvertedLockedMessage, refused);
        Assert.Single(db.Leads);
        Assert.Empty(db.ArchivedRecords);
    }

    // ============================ IG-4 — vaqtinchalik ismlar ============================

    [Theory]
    [InlineData("", true)]
    [InlineData("Noma'lum (daraja testi)", true)]
    [InlineData("Nomaʻlum", true)]
    [InlineData("Instagram mijozi", true)]
    [InlineData("ali_v (Instagram)", true)]
    [InlineData("Reklama lidi (ismsiz)", true)]
    [InlineData("Aliyev Ali", false)]
    [InlineData("Instagram bo'yicha menejer", false)]
    public void Vaqtinchalik_ism_taniladi(string name, bool expected) =>
        Assert.Equal(expected, LeadIntake.IsPlaceholderName(name));

    [Fact]
    public async Task Instagram_placeholder_ismi_daraja_testidagi_haqiqiy_ism_bilan_almashadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Leads.Add(new Lead { FullName = "ali_v (Instagram)", Phone = "901234567", CreatedAt = "2026-09-01T10:00:00" });
        db.LevelTests.Add(new LevelTest { Title = "Ingliz tili", Slug = "eng", IsActive = true });
        await db.SaveChangesAsync();

        await LevelTestService.SubmitAsync(db, "eng",
            new TestSubmitRequest("Aliyev Ali", "+998901234567", 12, new Dictionary<string, int>()));

        Assert.Equal("Aliyev Ali", (await db.Leads.SingleAsync()).FullName);
    }

    [Fact]
    public async Task Reklama_lidi_placeholder_ismni_almashtiradi_lekin_menejer_ismini_emas()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Leads.Add(new Lead { Id = "a", FullName = "Instagram mijozi", Phone = "901234567", CreatedAt = "2026-09-01T10:00:00" });
        db.Leads.Add(new Lead { Id = "b", FullName = "Menejer yozgan", Phone = "907654321", CreatedAt = "2026-09-01T10:00:00" });
        await db.SaveChangesAsync();

        await MetaLeadBridge.UpsertAsync(db, new IgAdLead { FullName = "Aliyev Ali", Phone = "+998901234567" }, "");
        await MetaLeadBridge.UpsertAsync(db, new IgAdLead { FullName = "Boshqa ism", Phone = "+998907654321" }, "");
        await db.SaveChangesAsync();

        Assert.Equal("Aliyev Ali", (await db.Leads.SingleAsync(l => l.Id == "a")).FullName);
        Assert.Equal("Menejer yozgan", (await db.Leads.SingleAsync(l => l.Id == "b")).FullName);
    }

    // ============================ CRM-9 — reklama lidi: yangi / takroriy ============================
    // (Lid avto-SMS — lead_new, test_result — 2026-10 da SMS kanali bilan birga olib tashlandi; lidning
    //  Telegram manzili yo'q. Qolgan qoida: takroriy murojaat yangi lid emas.)

    [Fact]
    public async Task Yangi_reklama_lidi_YANGI_takroriy_murojaat_YANGI_EMAS()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;

        var (_, isNew) = await MetaLeadBridge.UpsertAsync(
            db, new IgAdLead { FullName = "Aliyev Ali", Phone = "+998901234567" }, "");
        await db.SaveChangesAsync();
        Assert.True(isNew);

        var (_, isNew2) = await MetaLeadBridge.UpsertAsync(
            db, new IgAdLead { FullName = "Aliyev Ali", Phone = "+998901234567" }, "");
        await db.SaveChangesAsync();
        Assert.False(isNew2);
        Assert.Single(db.Leads);
        Assert.Empty(db.SmsLogs);   // SMS kanali yo'q — eski jadvalga hech narsa yozilmaydi
    }

    // ============================ Sinov natijasini tuzatish ============================

    [Fact]
    public void Sinov_natijasi_tuzatilganda_kelgan_sana_KPI_bilan_birga_ozgaradi()
    {
        var today = new DateOnly(2026, 10, 7);
        var trial = new TrialLesson { Result = "pending" };

        LeadTrialRules.ApplyAttendance(trial, "came", today);
        Assert.Equal("2026-10-07", trial.AttendedAt);

        // "keldi" oilasi ichida (keldi → qoldi) tashrif kuni saqlanadi.
        LeadTrialRules.ApplyAttendance(trial, "stayed", new DateOnly(2026, 10, 9));
        Assert.Equal("2026-10-07", trial.AttendedAt);

        // Xato "Keldi" → "Kelmadi": sana TOZALANADI (aks holda KPI uni baribir sanardi).
        LeadTrialRules.ApplyAttendance(trial, "no_show", today);
        Assert.Null(trial.AttendedAt);
    }

    // ============================ Manba matni bo'yicha qulflar ============================

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string Controller(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file));

    [Fact]
    public void CRM_statistikasi_reklama_lidlarini_ham_tasniflaydi_va_tushumni_qirqadi()
    {
        var src = Controller("LeadsController.cs");
        var analytics = src[src.IndexOf("[HttpGet(\"analytics\")]", StringComparison.Ordinal)..];
        analytics = analytics[..analytics.IndexOf("LeadAnalytics.Build(", StringComparison.Ordinal)];
        // Reklama to'plami `LeadCrmOverview` bilan AYNAN bir manbadan (Lead Ads + click-to-DM).
        Assert.Contains("IgDmAdLeads.AdsLeadIdsAsync(db)", analytics);
        Assert.Contains("LeadOrigins.Classify(l.Id, manualIds, formIds, testIds, igIds, adsIds)", analytics);
        Assert.Contains("Math.Max(0m, outcome.PaidTotal(l.Id))", analytics);
    }

    [Fact]
    public void Konversiya_oquvchi_yaratish_ruxsatini_va_dublikatni_tekshiradi()
    {
        var src = Controller("LeadsController.cs");
        var convert = src[src.IndexOf("[HttpPost(\"{id}/convert\")]", StringComparison.Ordinal)..];
        convert = convert[..convert.IndexOf("private bool MayCreateStudents", StringComparison.Ordinal)];
        Assert.Contains("MayCreateStudents()", convert);
        Assert.Contains("FindDuplicateStudentsAsync", convert);
        Assert.Contains("Conflict(", convert);
        Assert.Contains("AutoMessageTriggers.StudentAdded", convert);
        Assert.True(PermissionRules.CanWrite(new[] { "students.list:create" }, "students.list", "POST"));
        Assert.False(PermissionRules.CanWrite(new[] { "leads.list" }, "students.list", "POST"));
    }

    [Fact]
    public void Landing_anonim_chaqiruvchiga_lid_id_sini_qaytarmaydi()
    {
        var src = Controller("PublicLandingController.cs");
        Assert.DoesNotContain("id = lead.Id", src);
        Assert.Contains("return Ok(new { ok = true });", src);
    }

    [Fact]
    public void Yangi_audit_turlari_Lidlar_bolimiga_tushadi()
    {
        Assert.Equal("leads", AuditSections.SectionOf("LevelTest"));
        Assert.Equal("leads", AuditSections.SectionOf("LeadStage"));
        Assert.Equal("leads", AuditSections.SectionOf("LeadSource"));
    }
}
