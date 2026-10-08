using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QUVCHINI BUTUNLAY O'CHIRISH (<see cref="StudentPurge"/>) — dublikat/xato kiritilgan o'quvchi
/// uchun. Qulflanadigan narsalar: o'quvchining O'Z qatorlari o'chadi, BOSHQA o'quvchiniki tegilmaydi,
/// tirik pul amali bo'lsa hech narsa o'zgarmaydi, boshqa bo'limning yozuvlari faqat AJRATILADI.
/// </summary>
public class StudentPurgeTests
{
    private sealed record World(Student Target, Student Other, Group G, AppUser User, Teacher T, Subject C);

    private static World Seed(AppDbContext ctx)
    {
        var t = new Teacher { FullName = "O'qituvchi" };
        var c = new Subject { Name = "Matematika", Price = 500_000m };
        var g = new Group { Name = "5-9 sinf", MonthlyFee = 500_000m, TeacherId = t.Id, CourseId = c.Id };
        var user = new AppUser { FullName = "Dublikat", Role = Roles.Student, Email = "dub@x.uz" };
        var target = new Student
        {
            FullName = "Dublikat Ali", UserId = user.Id,
            BirthCertificateUrl = "/uploads/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg", ParentPassportUrl = "/uploads/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.pdf",
        };
        var other = new Student { FullName = "Haqiqiy Ali" };
        ctx.AddRange(t, c, g, user, target, other);

        foreach (var s in new[] { target, other })
        {
            ctx.StudentGroups.Add(new StudentGroup { StudentId = s.Id, GroupId = g.Id, Status = "active", ActivatedAt = "2026-09-01" });
            ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = "2026-09", Amount = 500_000m });
            ctx.JournalEntries.Add(new JournalEntry { StudentId = s.Id, ClassId = g.Id, Date = "2026-09-02", Present = true });
            ctx.StudentDiscounts.Add(new StudentDiscount { StudentId = s.Id, GroupId = g.Id, Pct = 10, StartMonth = "2026-09" });
            ctx.StudentNotes.Add(new StudentNote { StudentId = s.Id, Text = "izoh" });
            var req = new ContactRequest { StudentId = s.Id, StudentName = s.FullName };
            ctx.ContactRequests.Add(req);
            ctx.ContactAttempts.Add(new ContactAttempt { StudentId = s.Id, RequestId = req.Id, Date = "2026-09-03" });
            ctx.TeacherReviews.Add(new TeacherReview { StudentId = s.Id, TeacherId = t.Id, GroupId = g.Id, Text = "yaxshi" });
            ctx.AuditLogs.Add(new AuditLog { EntityType = "Student", EntityId = s.Id, Action = "create", Summary = "qo'shildi", StudentId = s.Id });
            ctx.AuditLogs.Add(new AuditLog { EntityType = "Membership", EntityId = $"{g.Id}:{s.Id}", Action = "update", Summary = "aktivlashtirildi" });
        }

        var cert = new StudentCertificate { StudentId = target.Id, CourseId = c.Id, FilePath = "/uploads/certificates/ccc.pdf" };
        ctx.StudentCertificates.Add(cert);
        ctx.CertificateVerifications.Add(new CertificateVerification { StudentCertificateId = cert.Id, IsValid = true });
        ctx.DeviceTokens.Add(new DeviceToken { UserId = user.Id, Token = "fcm-1" });
        ctx.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = "h1", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        return new World(target, other, g, user, t, c);
    }

    [Fact]
    public async Task Oquvchi_va_uning_BARCHA_qatorlari_ochadi__boshqa_oquvchiniki_TEGILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        await ctx.SaveChangesAsync();

        var result = await StudentPurge.PurgeAsync(ctx, w.Target.Id);
        await ctx.SaveChangesAsync();

        Assert.NotNull(result);
        var id = w.Target.Id;
        Assert.False(await ctx.Students.AnyAsync(s => s.Id == id));
        Assert.False(await ctx.StudentGroups.AnyAsync(x => x.StudentId == id));
        Assert.False(await ctx.MonthlyCharges.AnyAsync(x => x.StudentId == id));
        Assert.False(await ctx.JournalEntries.AnyAsync(x => x.StudentId == id));
        Assert.False(await ctx.StudentDiscounts.AnyAsync(x => x.StudentId == id));
        Assert.False(await ctx.StudentNotes.AnyAsync(x => x.StudentId == id));
        Assert.False(await ctx.ContactRequests.AnyAsync(x => x.StudentId == id));
        Assert.False(await ctx.ContactAttempts.AnyAsync(x => x.StudentId == id));
        Assert.False(await ctx.TeacherReviews.AnyAsync(x => x.StudentId == id));
        Assert.False(await ctx.StudentCertificates.AnyAsync(x => x.StudentId == id));
        Assert.Equal(0, await ctx.CertificateVerifications.CountAsync());
        // Tarix: o'quvchi yozuvi ham, a'zolik hodisasi ("{guruh}:{o'quvchi}") ham.
        Assert.False(await ctx.AuditLogs.AnyAsync(a => a.StudentId == id || a.EntityId == id || a.EntityId.EndsWith(":" + id)));
        // Arxivga HECH NARSA yozilmaydi.
        Assert.Equal(0, await ctx.ArchivedRecords.CountAsync());

        // Ikkinchi o'quvchi — butunlay joyida.
        var o = w.Other.Id;
        Assert.True(await ctx.Students.AnyAsync(s => s.Id == o));
        Assert.Equal(1, await ctx.StudentGroups.CountAsync(x => x.StudentId == o));
        Assert.Equal(1, await ctx.MonthlyCharges.CountAsync(x => x.StudentId == o));
        Assert.Equal(1, await ctx.JournalEntries.CountAsync(x => x.StudentId == o));
        Assert.Equal(1, await ctx.ContactAttempts.CountAsync(x => x.StudentId == o));
        Assert.Equal(1, await ctx.TeacherReviews.CountAsync(x => x.StudentId == o));
        Assert.Equal(2, await ctx.AuditLogs.CountAsync());
        // Guruh, o'qituvchi, kurs — tegilmaydi.
        Assert.True(await ctx.Classes.AnyAsync(x => x.Id == w.G.Id));
        Assert.True(await ctx.Teachers.AnyAsync(x => x.Id == w.T.Id));
    }

    [Fact]
    public async Task Login_akkaunti_va_uning_tokenlari_ochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        await ctx.SaveChangesAsync();

        await StudentPurge.PurgeAsync(ctx, w.Target.Id);
        await ctx.SaveChangesAsync();

        Assert.False(await ctx.Users.AnyAsync(u => u.Id == w.User.Id));
        Assert.Equal(0, await ctx.DeviceTokens.CountAsync());
        Assert.Equal(0, await ctx.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Oquvchi_EMAS_akkaunt_ochirilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        w.User.Role = Roles.Staff;   // buzuq bog'lanish: o'quvchiga xodim akkaunti ulangan
        await ctx.SaveChangesAsync();

        await StudentPurge.PurgeAsync(ctx, w.Target.Id);
        await ctx.SaveChangesAsync();

        Assert.True(await ctx.Users.AnyAsync(u => u.Id == w.User.Id));
        Assert.Equal(1, await ctx.DeviceTokens.CountAsync());
    }

    [Fact]
    public async Task Boshqa_oquvchi_bilan_UMUMIY_akkaunt_ochirilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        w.Other.UserId = w.User.Id;
        await ctx.SaveChangesAsync();

        await StudentPurge.PurgeAsync(ctx, w.Target.Id);
        await ctx.SaveChangesAsync();

        Assert.True(await ctx.Users.AnyAsync(u => u.Id == w.User.Id));
    }

    [Fact]
    public async Task TIRIK_tolov_bolsa_RAD_etiladi_va_hech_narsa_ozgarmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = "2026-09-05", Direction = "income", Category = "tuition", Amount = 300_000m,
            StudentId = w.Target.Id, GroupId = w.G.Id, Month = "2026-09",
        });
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StudentPurge.PurgeAsync(ctx, w.Target.Id));
        Assert.Contains("300 000", ex.Message.Replace(' ', ' '));

        await ctx.SaveChangesAsync();   // tayyorlangan o'zgarish qolmagan bo'lishi SHART
        Assert.True(await ctx.Students.AnyAsync(s => s.Id == w.Target.Id));
        Assert.Equal(1, await ctx.StudentGroups.CountAsync(x => x.StudentId == w.Target.Id));
        Assert.Equal(1, await ctx.FinanceTransactions.CountAsync());

        var preview = await StudentPurge.PreviewAsync(ctx, w.Target.Id);
        Assert.NotNull(preview);
        Assert.False(preview!.CanPurge);
        Assert.Equal(1, preview.LiveTransactions);
        Assert.Equal(300_000m, preview.LiveTransactionsTotal);
    }

    [Fact]
    public async Task BEKOR_qilingan_tolov_SAQLANADI__faqat_oquvchidan_ajratiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        var voided = new FinanceTransaction
        {
            Date = "2026-09-05", Direction = "income", Category = "tuition", Amount = 300_000m,
            StudentId = w.Target.Id, IsVoided = true, VoidedAt = "2026-09-06T10:00:00", VoidReason = "dublikat",
        };
        ctx.FinanceTransactions.Add(voided);
        await ctx.SaveChangesAsync();

        await StudentPurge.PurgeAsync(ctx, w.Target.Id);
        await ctx.SaveChangesAsync();

        var row = await ctx.FinanceTransactions.IgnoreQueryFilters().SingleAsync(t => t.Id == voided.Id);
        Assert.Null(row.StudentId);
        Assert.True(row.IsVoided);
    }

    [Fact]
    public async Task Boshqa_bolim_yozuvlari_AJRATILADI__bron_qilingan_support_slot_qayta_BOSHAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        var booked = new SupportSlot { TeacherId = w.T.Id, Date = "2026-10-10", Status = "booked", StudentId = w.Target.Id, BookedAt = "2026-09-30T10:00:00" };
        var done = new SupportSlot { TeacherId = w.T.Id, Date = "2026-09-10", Status = "done", StudentId = w.Target.Id, Topic = "Kasrlar" };
        var call = new Call { StudentId = w.Target.Id };
        var lead = new Lead { FullName = "Ali", ConvertedStudentId = w.Target.Id };
        ctx.AddRange(booked, done, call, lead);
        await ctx.SaveChangesAsync();

        await StudentPurge.PurgeAsync(ctx, w.Target.Id);
        await ctx.SaveChangesAsync();

        var b = await ctx.SupportSlots.SingleAsync(x => x.Id == booked.Id);
        Assert.Equal("open", b.Status);
        Assert.Null(b.StudentId);
        Assert.Null(b.BookedAt);
        var d = await ctx.SupportSlots.SingleAsync(x => x.Id == done.Id);
        Assert.Equal("done", d.Status);          // o'tilgan dars — o'qituvchining ish yozuvi qoladi
        Assert.Null(d.StudentId);
        Assert.Null((await ctx.Calls.SingleAsync(x => x.Id == call.Id)).StudentId);
        Assert.Null((await ctx.Leads.SingleAsync(x => x.Id == lead.Id)).ConvertedStudentId);
    }

    [Fact]
    public async Task Fayllar_royxati__boshqa_oquvchida_turgan_rasm_ROYXATGA_KIRMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        w.Other.BirthCertificateUrl = "/uploads/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg";   // bir xil rasm ikkinchi yozuvda ham
        await ctx.SaveChangesAsync();

        var result = await StudentPurge.PurgeAsync(ctx, w.Target.Id);

        Assert.NotNull(result);
        Assert.DoesNotContain("/uploads/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg", result!.FileUrls);
        Assert.Contains("/uploads/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.pdf", result.FileUrls);
        Assert.Contains("/uploads/certificates/ccc.pdf", result.FileUrls);
    }

    [Fact]
    public async Task Topilmagan_oquvchi__null()
    {
        using var db = TestDb.Sqlite();
        Assert.Null(await StudentPurge.PurgeAsync(db.Context, "yoq"));
        Assert.Null(await StudentPurge.PreviewAsync(db.Context, "yoq"));
    }

    [Fact]
    public async Task Preview__faqat_BOR_narsalar_sanaladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);
        await ctx.SaveChangesAsync();

        var p = await StudentPurge.PreviewAsync(ctx, w.Target.Id);

        Assert.NotNull(p);
        Assert.True(p!.CanPurge);
        Assert.Null(p.BlockReason);
        Assert.All(p.Items, i => Assert.True(i.Count > 0));
        Assert.Equal(1, p.Items.Single(i => i.Key == "groups").Count);
        Assert.Equal(2, p.Items.Single(i => i.Key == "history").Count);
        Assert.Equal(1, p.Items.Single(i => i.Key == "account").Count);
        Assert.DoesNotContain(p.Items, i => i.Key == "voided");
    }

    /* ------- RUXSAT — «O'quvchilar ro'yxati» → «O'chirish» (manba matni bo'yicha) -------
     * Test loyihasi Server'ga havola QILMAYDI (HomeEndpointsRbacTests bilan bir xil sabab) — darvoza
     * controller faylidan o'qib tekshiriladi. Kimdir rol tekshiruvini olib tashlasa yoki
     * [AllowAnonymous] qo'shsa, test qizaradi. */

    private static string ServerSource(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        var path = Path.Combine(dir!.FullName, relative);
        Assert.True(File.Exists(path), $"Fayl topilmadi: {path}");
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("public async Task<IActionResult> Preview(")]
    [InlineData("public async Task<IActionResult> Purge(")]
    public void Har_endpoint_BIRINCHI_qadamda_ochirish_ruxsatini_tekshiradi(string signature)
    {
        var src = ServerSource(Path.Combine("WunderkindLC.Server", "Controllers", "StudentPurgeController.cs"));

        // Kalit — «O'quvchilar ro'yxati» ning «O'chirish» amali (Rollar matritsasidan beriladi).
        var gate = src[src.IndexOf("private bool MayPurge =>", StringComparison.Ordinal)..];
        gate = gate[..gate.IndexOf(';')];
        Assert.Contains("PermissionRules.CanWrite(", gate);
        Assert.Contains("\"students.list\", \"DELETE\"", gate);
        Assert.Contains("User.IsInRole(Roles.Staff)", gate);

        var i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i > 0, $"{signature} topilmadi");
        var body = src[src.IndexOf('{', i)..];
        var firstStatement = body[1..].TrimStart().Split('\n')[0].Trim();
        Assert.Equal("if (!MayPurge) return Forbidden();", firstStatement);
    }

    [Fact]
    public void Ruxsat_qoidasi__faqat_OCHIRISH_amali_yetadi()
    {
        // Controller AYNAN shu chaqiriqni ishlatadi — qaysi token o'tishi shu yerda qulflanadi.
        static bool May(params string[] claims) => PermissionRules.CanWrite(claims, "students.list", "DELETE");

        Assert.True(May("students.list:delete"));
        Assert.True(May("students.list"));
        Assert.True(May("students"));
        Assert.True(May("students:delete"));
        Assert.False(May("students.list:edit", "students.list:create"));
        Assert.False(May("students.turnstile"));   // boshqa sahifa — yuqoriga yozish meros EMAS
        Assert.False(May());
    }

    [Fact]
    public void Controller_login_talab_qiladi__AllowAnonymous_YOQ()
    {
        var src = ServerSource(Path.Combine("WunderkindLC.Server", "Controllers", "StudentPurgeController.cs"));
        var header = src[..src.IndexOf("public class StudentPurgeController", StringComparison.Ordinal)];

        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"students.list\")]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
    }

    [Fact]
    public void Klientda_tugma_OCHIRISH_ruxsati_bilan_korinadi()
    {
        var src = ServerSource(Path.Combine(
            "WunderkindLC.Client", "src", "pages", "admin", "students", "StudentDetailPage.tsx"));

        Assert.Contains("const canPurge = can('students.list', 'delete')", src);
    }

    [Theory]
    [InlineData("/uploads/abc.jpg", "uploads/abc.jpg")]
    [InlineData("/uploads/face/abc.jpg", "uploads/face/abc.jpg")]
    [InlineData("/uploads/certificates/abc.pdf", "uploads/certificates/abc.pdf")]
    public void ResolveUploadPath__ruxsat_etilgan_papkalar(string url, string expected)
    {
        var path = StudentPurge.ResolveUploadPath("/app", url);
        Assert.Equal(Path.Combine("/app", expected), path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.uz/a.jpg")]
    [InlineData("/uploads/../appsettings.json")]
    [InlineData("/uploads/face/../../x.jpg")]
    [InlineData("/uploads/marketing-public/a.jpg")]
    [InlineData("/uploads/a\\b.jpg")]
    [InlineData("/uploads/")]
    [InlineData("/uploads/..")]
    public void ResolveUploadPath__begona_yol_RAD_etiladi(string? url)
    {
        Assert.Null(StudentPurge.ResolveUploadPath("/app", url));
    }
}
