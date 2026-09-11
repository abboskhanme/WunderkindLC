using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QUVCHINING O'QITUVCHI HAQIDAGI FIKRI — yig'ish, o'qish va AI tahlilga uzatish.
///
/// <para>Talab: o'quvchi qaysi guruh(lar)da o'qisa, HAR guruh o'qituvchisi uchun alohida fikr
/// yozib boriladi (2+ guruh → 2+ blok); bu matnlar o'qituvchining AI tahliliga manba bo'ladi.
/// MAXFIYLIK: xom matn o'qituvchiga ko'rsatilmaydi, AI promptida ham o'quvchi ISMI bo'lmaydi.</para>
/// </summary>
public class TeacherReviewTests
{
    private static readonly string Today = AppClock.Today.ToString("yyyy-MM-dd");

    private static Teacher AddTeacher(AppDbContext ctx, string name)
    {
        var t = new Teacher { FullName = name };
        ctx.Teachers.Add(t);
        return t;
    }

    private static Group AddGroup(AppDbContext ctx, Teacher t, string name, string courseId = "")
    {
        var g = new Group { Name = name, TeacherId = t.Id, CourseId = courseId, MonthlyFee = 500_000m };
        ctx.Classes.Add(g);
        return g;
    }

    private static Student AddStudent(AppDbContext ctx, params Group[] groups)
    {
        var s = new Student { FullName = "Ali Valiyev", EnrollmentDate = Today };
        ctx.Students.Add(s);
        foreach (var g in groups)
            ctx.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
                JoinedAt = Today, ActivatedAt = Today, RecordedAt = Today,
            });
        return s;
    }

    // ==================== O'quvchi profili: guruh bo'yicha bloklar ====================

    [Fact]
    public async Task Ikki_guruhda_oqisa_IKKI_blok_chiqadi_har_biri_oz_oqituvchisi_bilan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t1 = AddTeacher(ctx, "Ustoz Bir");
        var t2 = AddTeacher(ctx, "Ustoz Ikki");
        var g1 = AddGroup(ctx, t1, "A guruh");
        var g2 = AddGroup(ctx, t2, "B guruh");
        var s = AddStudent(ctx, g1, g2);
        await ctx.SaveChangesAsync();

        var blocks = await TeacherReviewService.ForStudentAsync(ctx, s.Id);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(new[] { "A guruh", "B guruh" }, blocks.Select(b => b.GroupName).ToArray());
        Assert.Equal(t1.Id, blocks[0].TeacherId);
        Assert.Equal("Ustoz Bir", blocks[0].TeacherName);
        Assert.Equal(t2.Id, blocks[1].TeacherId);
        Assert.All(blocks, b => Assert.Empty(b.Reviews));
    }

    [Fact]
    public async Task Oqituvchisi_yoq_guruh_uchun_blok_chiqmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var withTeacher = AddGroup(ctx, t, "A guruh");
        // O'qituvchisi biriktirilmagan guruh — fikr yozib bo'lmaydi.
        var orphan = new Group { Name = "B guruh", TeacherId = "", MonthlyFee = 400_000m };
        ctx.Classes.Add(orphan);
        var s = AddStudent(ctx, withTeacher, orphan);
        await ctx.SaveChangesAsync();

        var blocks = await TeacherReviewService.ForStudentAsync(ctx, s.Id);
        Assert.Equal("A guruh", Assert.Single(blocks).GroupName);
    }

    [Fact]
    public async Task Chiqarilgan_azolik_ham_korinadi_lekin_FAOLLAR_tepada()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var eski = AddGroup(ctx, t, "A eski");
        var yangi = AddGroup(ctx, t, "B yangi");
        var s = AddStudent(ctx, yangi);
        // Eski guruhdan chiqarilgan — fikr tarixi baribir qimmatli.
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = eski.Id, Status = "completed", IsActive = false,
            JoinedAt = Today, LeftAt = Today, ActivatedAt = Today, RecordedAt = Today,
        });
        await ctx.SaveChangesAsync();

        var blocks = await TeacherReviewService.ForStudentAsync(ctx, s.Id);

        Assert.Equal(2, blocks.Count);
        Assert.True(blocks[0].IsActive);              // faol a'zolik tepada
        Assert.Equal("B yangi", blocks[0].GroupName);
        Assert.False(blocks[1].IsActive);
    }

    // ==================== Yozish ====================

    [Fact]
    public async Task Fikr_yoziladi_va_eng_yangisi_TEPADA_turadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();

        var (first, e1) = await TeacherReviewService.AddAsync(
            ctx, s.Id, t.Id, g.Id, "  Yaxshi tushuntiradi  ", "Admin Bir", "u-1");
        Assert.Null(e1);
        Assert.Equal("Yaxshi tushuntiradi", first!.Text);   // trim qilinadi

        // Ikkinchisi KEYINROQ yozilgan (CreatedAt kattaroq) — ro'yxatda birinchi turishi kerak.
        ctx.TeacherReviews.Add(new TeacherReview
        {
            StudentId = s.Id, TeacherId = t.Id, GroupId = g.Id,
            Text = "Keyinroq yozilgan", CreatedAt = "2999-01-01T10:00:00", CreatedBy = "Admin Ikki",
        });
        await ctx.SaveChangesAsync();

        var block = Assert.Single(await TeacherReviewService.ForStudentAsync(ctx, s.Id));
        Assert.Equal(2, block.Reviews.Count);
        Assert.Equal("Keyinroq yozilgan", block.Reviews[0].Text);
        Assert.Equal("Admin Bir", block.Reviews[1].CreatedBy);
    }

    [Fact]
    public async Task Bosh_matn_va_azolik_yoq_guruh_RAD_etiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var boshqa = AddGroup(ctx, t, "B guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();

        var (d1, e1) = await TeacherReviewService.AddAsync(ctx, s.Id, t.Id, g.Id, "   ", "Admin", null);
        Assert.Null(d1);
        Assert.Contains("matn", e1, StringComparison.OrdinalIgnoreCase);

        // O'quvchi bu guruhda umuman o'qimagan.
        var (d2, e2) = await TeacherReviewService.AddAsync(ctx, s.Id, t.Id, boshqa.Id, "Fikr", "Admin", null);
        Assert.Null(d2);
        Assert.Contains("o'qimagan", e2);

        Assert.Empty(ctx.TeacherReviews.ToList());
    }

    [Fact]
    public async Task Guruh_oqituvchisi_ustun_klientdan_kelgan_teacherId_ga_ishonilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var haqiqiy = AddTeacher(ctx, "Haqiqiy ustoz");
        var begona = AddTeacher(ctx, "Begona ustoz");
        var g = AddGroup(ctx, haqiqiy, "A guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();

        // Boshqa o'qituvchi id'si bilan yozishga urinish — rad etiladi.
        var (dto, err) = await TeacherReviewService.AddAsync(ctx, s.Id, begona.Id, g.Id, "Fikr", "Admin", null);
        Assert.Null(dto);
        Assert.NotNull(err);

        // teacherId umuman berilmasa — guruh o'qituvchisi olinadi.
        var (ok, _) = await TeacherReviewService.AddAsync(ctx, s.Id, "", g.Id, "Fikr", "Admin", null);
        Assert.Equal(haqiqiy.Id, ok!.TeacherId);
    }

    [Fact]
    public async Task Fikr_ochiriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();
        var (dto, _) = await TeacherReviewService.AddAsync(ctx, s.Id, t.Id, g.Id, "Fikr", "Admin", null);

        Assert.True(await TeacherReviewService.DeleteAsync(ctx, dto!.Id));
        Assert.False(await TeacherReviewService.DeleteAsync(ctx, dto.Id));   // ikkinchi marta — yo'q
        Assert.Empty(ctx.TeacherReviews.ToList());
    }

    [Fact]
    public async Task Oquvchi_ochirilsa_fikrlari_ham_ochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();
        await TeacherReviewService.AddAsync(ctx, s.Id, t.Id, g.Id, "Fikr", "Admin", null);

        ctx.Students.Remove(ctx.Students.First(x => x.Id == s.Id));
        await ctx.SaveChangesAsync();

        Assert.Empty(ctx.TeacherReviews.ToList());   // FK CASCADE
    }

    // ==================== O'qituvchi profili: «Fikrlar» bo'limi ====================

    [Fact]
    public async Task Oqituvchi_profilida_BARCHA_fikrlar_yigiladi_eng_yangisi_tepada()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var boshqa = AddTeacher(ctx, "Boshqa ustoz");
        var g1 = AddGroup(ctx, t, "A guruh");
        var g2 = AddGroup(ctx, t, "B guruh");
        var gb = AddGroup(ctx, boshqa, "C guruh");
        var s1 = AddStudent(ctx, g1, g2);
        var s2 = new Student { FullName = "Vali Aliyev", EnrollmentDate = Today };
        ctx.Students.Add(s2);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s2.Id, GroupId = g1.Id, Status = "active", IsActive = true,
            JoinedAt = Today, ActivatedAt = Today, RecordedAt = Today,
        });
        await ctx.SaveChangesAsync();

        ctx.TeacherReviews.AddRange(
            new TeacherReview
            {
                StudentId = s1.Id, TeacherId = t.Id, GroupId = g1.Id,
                Text = "Eskiroq", CreatedAt = "2026-01-01T10:00:00", CreatedBy = "Admin",
            },
            new TeacherReview
            {
                StudentId = s2.Id, TeacherId = t.Id, GroupId = g2.Id,
                Text = "Eng yangi", CreatedAt = "2026-05-01T10:00:00", CreatedBy = "Admin",
            },
            // Boshqa o'qituvchi haqida — bu ro'yxatga TUSHMASLIGI kerak.
            new TeacherReview
            {
                StudentId = s1.Id, TeacherId = boshqa.Id, GroupId = gb.Id,
                Text = "Begona", CreatedAt = "2026-06-01T10:00:00", CreatedBy = "Admin",
            });
        await ctx.SaveChangesAsync();

        var feed = await TeacherReviewService.ForTeacherAsync(ctx, t.Id);

        Assert.Equal(2, feed.Total);
        Assert.Equal("Eng yangi", feed.Items[0].Text);       // eng yangisi TEPADA
        Assert.Equal("Vali Aliyev", feed.Items[0].StudentName);
        Assert.Equal("B guruh", feed.Items[0].GroupName);
        Assert.Equal("Eskiroq", feed.Items[1].Text);
        Assert.DoesNotContain(feed.Items, x => x.Text == "Begona");
    }

    [Fact]
    public async Task Oqituvchi_profilidagi_royxat_CHEKLANADI_lekin_JAMI_soni_togri()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();

        for (var i = 0; i < 10; i++)
            ctx.TeacherReviews.Add(new TeacherReview
            {
                StudentId = s.Id, TeacherId = t.Id, GroupId = g.Id,
                Text = $"Fikr {i}", CreatedAt = $"2026-01-{i + 1:D2}T10:00:00",
            });
        await ctx.SaveChangesAsync();

        var feed = await TeacherReviewService.ForTeacherAsync(ctx, t.Id, max: 4);

        Assert.Equal(10, feed.Total);        // JAMI — hammasi
        Assert.Equal(4, feed.Items.Count);   // ko'rsatiladigani — cheklangan
        Assert.Contains("Fikr 9", feed.Items[0].Text);
    }

    [Fact]
    public async Task Oqituvchi_profilida_fikr_yoq_bolsa_bosh_royxat()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        await ctx.SaveChangesAsync();

        var feed = await TeacherReviewService.ForTeacherAsync(ctx, t.Id);
        Assert.Equal(0, feed.Total);
        Assert.Empty(feed.Items);
    }

    // ==================== AI tahlilga uzatish (maxfiylik) ====================

    [Fact]
    public async Task AI_uchun_matnlar_ISMSIZ_va_sana_guruh_bilan_beriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();
        await TeacherReviewService.AddAsync(ctx, s.Id, t.Id, g.Id, "Juda yaxshi tushuntiradi", "Admin", null);

        var (count, texts) = await TeacherReviewService.TextsForTeacherAsync(ctx, t.Id, "");

        Assert.Equal(1, count);
        var line = Assert.Single(texts);
        Assert.Contains("Juda yaxshi tushuntiradi", line);
        Assert.Contains("A guruh", line);                 // guruh nomi — bor
        Assert.DoesNotContain("Ali Valiyev", line);       // O'QUVCHI ISMI — YO'Q (maxfiylik)
    }

    [Fact]
    public async Task AI_uchun_matnlar_ESKI_yozuvlarni_va_boshqa_oqituvchini_olmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var boshqa = AddTeacher(ctx, "Boshqa ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var gb = AddGroup(ctx, boshqa, "B guruh");
        var s = AddStudent(ctx, g, gb);
        await ctx.SaveChangesAsync();

        ctx.TeacherReviews.AddRange(
            new TeacherReview
            {
                StudentId = s.Id, TeacherId = t.Id, GroupId = g.Id,
                Text = "Yangi fikr", CreatedAt = "2999-01-01T10:00:00",
            },
            new TeacherReview
            {
                StudentId = s.Id, TeacherId = t.Id, GroupId = g.Id,
                Text = "Juda eski fikr", CreatedAt = "2000-01-01T10:00:00",
            },
            new TeacherReview
            {
                StudentId = s.Id, TeacherId = boshqa.Id, GroupId = gb.Id,
                Text = "Boshqa ustoz haqida", CreatedAt = "2999-01-01T11:00:00",
            });
        await ctx.SaveChangesAsync();

        var (count, texts) = await TeacherReviewService.TextsForTeacherAsync(ctx, t.Id, "2020-01-01T00:00:00");

        Assert.Equal(1, count);                                    // eski davrdan tashqaridagi tushmaydi
        Assert.Contains("Yangi fikr", Assert.Single(texts));
        Assert.DoesNotContain(texts, x => x.Contains("Boshqa ustoz haqida"));
    }

    [Fact]
    public async Task AI_uchun_matnlar_soni_CHEKLANADI_prompt_shishib_ketmasin()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();

        for (var i = 0; i < 30; i++)
            ctx.TeacherReviews.Add(new TeacherReview
            {
                StudentId = s.Id, TeacherId = t.Id, GroupId = g.Id,
                Text = $"Fikr {i}", CreatedAt = $"2026-01-{i + 1:D2}T10:00:00",
            });
        await ctx.SaveChangesAsync();

        var (count, texts) = await TeacherReviewService.TextsForTeacherAsync(ctx, t.Id, "", max: 25);

        Assert.Equal(30, count);        // JAMI soni to'g'ri qaytadi
        Assert.Equal(25, texts.Count);  // promptga esa faqat eng yangi 25 tasi
        Assert.Contains("Fikr 29", texts[0]);
    }

    [Fact]
    public async Task Snapshot_metrikasida_fikrlar_SONI_bor_matnlar_esa_YOQ()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, t, "A guruh");
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();
        await TeacherReviewService.AddAsync(ctx, s.Id, t.Id, g.Id, "Maxfiy matn", "Admin", null);

        var (metrics, snapshotJson) = await TeacherSnapshotBuilder.BuildAsync(ctx, t);

        // Metrikada faqat SON (u UI'ga chiqadi).
        Assert.Equal(1, metrics.StudentReviewCount);
        // Matnning O'ZI esa faqat AI promptiga ketadigan snapshotda.
        Assert.Contains("oquvchilarFikri", snapshotJson);
        Assert.Contains("Maxfiy matn", snapshotJson);
        Assert.DoesNotContain("Ali Valiyev", snapshotJson[snapshotJson.IndexOf("oquvchilarFikri", StringComparison.Ordinal)..]);
    }
}
