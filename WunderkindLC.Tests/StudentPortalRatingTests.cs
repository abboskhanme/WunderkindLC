using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QUVCHI ILOVASI — «Progress → Guruh» reytingi (<c>GET /api/student/rating</c> yadrosi:
/// <see cref="RatingService.PortalAsync"/>).
///
/// <para><b>REGRESSIYA:</b> guruh ro'yxati ilgari <c>Student.ClassName</c> MATN yorlig'i tengligi
/// bilan qurilardi. M2M a'zolikka o'tilgandan keyin bu yorliq ko'p o'quvchida BO'SH yoki ESKIRGAN:
/// bo'sh bo'lsa ro'yxat umuman bo'sh chiqar ("Reyting yo'q"), eskirgan bo'lsa butunlay boshqa
/// guruh odamlari ko'rinardi. Endi manba — FAOL a'zolik (<see cref="StudentGroup"/>).</para>
/// </summary>
public class StudentPortalRatingTests
{
    /* =========================================================================================
     *  Yordamchilar
     * ========================================================================================= */

    private static Group AddGroup(TestDb db, string name)
    {
        var subject = new Subject { Name = $"{name} kursi" };
        db.Context.Subjects.Add(subject);
        var teacher = new Teacher { FullName = $"{name} o'qituvchisi" };
        db.Context.Teachers.Add(teacher);
        var g = new Group { Name = name, CourseId = subject.Id, TeacherId = teacher.Id };
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return g;
    }

    private static Student AddStudent(TestDb db, string fullName, string className = "")
    {
        var s = new Student { FullName = fullName, ClassName = className };
        db.Context.Students.Add(s);
        db.Context.SaveChanges();
        return s;
    }

    private static void AddMember(TestDb db, Student s, Group g, bool active = true)
    {
        db.Context.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id,
            GroupId = g.Id,
            IsActive = active,
            Status = "active",
            JoinedAt = "2026-01-01",
            ActivatedAt = "2026-01-01",
            RecordedAt = "2026-01-01",
            LeftAt = active ? null : "2026-02-01",
        });
        db.Context.SaveChanges();
    }

    /// <summary>Ball beradi: bitta jurnal bahosi (Ball = Σ baholar).</summary>
    private static void AddBall(TestDb db, Student s, Group g, int grade)
    {
        db.Context.JournalEntries.Add(new JournalEntry
        {
            ClassId = g.Id,
            SubjectId = g.CourseId,
            StudentId = s.Id,
            Quarter = 1,
            Date = "2026-03-02",
            Period = 1,
            Grade = grade,
        });
        db.Context.SaveChanges();
    }

    /// <summary>O'quvchi + ball + guruh a'zoligi — bitta qadamda.</summary>
    private static Student AddMemberWithBall(TestDb db, Group g, string name, int ball, string className = "")
    {
        var s = AddStudent(db, name, className);
        AddMember(db, s, g);
        AddBall(db, s, g, ball);
        return s;
    }

    private static async Task<PortalRatingDto> PortalAsync(TestDb db, Student s)
    {
        var school = await RatingService.SchoolAsync(db.Context);
        return await RatingService.PortalAsync(db.Context, s, school);
    }

    /* =========================================================================================
     *  1. ASOSIY REGRESSIYA — ClassName BO'SH bo'lsa ham guruh reytingi keladi
     * ========================================================================================= */

    [Fact]
    public async Task ClassName_bosh_bolsa_ham_guruh_reytingi_keladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "Ingliz A1");
        var me = AddMemberWithBall(db, g, "Ali Valiyev", 5);          // ClassName = "" (yorliq yo'q)
        AddMemberWithBall(db, g, "Vali Aliyev", 9);

        var dto = await PortalAsync(db, me);

        Assert.Single(dto.Groups);
        Assert.Equal(g.Id, dto.Groups[0].GroupId);
        Assert.Equal("Ingliz A1", dto.Groups[0].GroupName);
        Assert.Equal(2, dto.Groups[0].Size);
        Assert.Contains(dto.Groups[0].Rows, r => r.StudentId == me.Id);
        Assert.Equal(2, dto.Groups[0].MeRank);                        // balli pastroq — ikkinchi
    }

    /* =========================================================================================
     *  2. KO'P GURUH — har biri alohida
     * ========================================================================================= */

    [Fact]
    public async Task Kop_guruhli_oquvchi_har_ikkala_guruhini_koradi()
    {
        using var db = TestDb.Sqlite();
        var a = AddGroup(db, "Ingliz A1");
        var b = AddGroup(db, "Matematika");

        var me = AddStudent(db, "Ali Valiyev");
        AddMember(db, me, a);
        AddMember(db, me, b);
        AddBall(db, me, a, 5);

        AddMemberWithBall(db, a, "A guruh o'quvchisi", 9);
        AddMemberWithBall(db, b, "B guruh o'quvchisi", 3);

        var dto = await PortalAsync(db, me);

        Assert.Equal(2, dto.Groups.Count);
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), dto.Groups.Select(x => x.GroupId).OrderBy(x => x));
        // Har guruhda faqat O'SHA guruh a'zolari
        var ga = dto.Groups.First(x => x.GroupId == a.Id);
        var gb = dto.Groups.First(x => x.GroupId == b.Id);
        Assert.Equal(2, ga.Size);
        Assert.Equal(2, gb.Size);
        Assert.All(dto.Groups, x => Assert.Contains(x.Rows, r => r.StudentId == me.Id));
        Assert.DoesNotContain(ga.Rows, r => r.FullName == "B guruh o'quvchisi");
        Assert.DoesNotContain(gb.Rows, r => r.FullName == "A guruh o'quvchisi");
    }

    /* =========================================================================================
     *  3. O'RIN GURUH ICHIDA QAYTA RAQAMLANADI (podium 1/2/3 shunga tayanadi)
     * ========================================================================================= */

    [Fact]
    public async Task Guruh_ichidagi_rank_qayta_raqamlanadi()
    {
        using var db = TestDb.Sqlite();
        var mine = AddGroup(db, "Ingliz A1");
        var other = AddGroup(db, "Matematika");

        // Markaz tartibi (ball kamayishi): T1(10), T2(8), me(5), T3(4), S3(3)
        AddMemberWithBall(db, other, "T1", 10);
        AddMemberWithBall(db, other, "T2", 8);
        var me = AddMemberWithBall(db, mine, "Ali Valiyev", 5);
        AddMemberWithBall(db, other, "T3", 4);
        AddMemberWithBall(db, mine, "S3", 3);

        var dto = await PortalAsync(db, me);

        var rows = dto.Groups.Single().Rows;
        Assert.Equal(new[] { 1, 2 }, rows.Select(r => r.Rank));       // 3 va 5 EMAS
        Assert.Equal(me.Id, rows[0].StudentId);
        Assert.Equal(1, dto.Groups[0].MeRank);
        Assert.Equal(3, dto.MeSchoolRank);                            // markaz o'rni o'zgarmaydi
        Assert.Equal(5, dto.SchoolSize);
    }

    /* =========================================================================================
     *  4. ESKIRGAN ClassName — a'zolik USTUN keladi
     * ========================================================================================= */

    [Fact]
    public async Task Eskirgan_ClassName_bolsa_azolik_ustun_keladi()
    {
        using var db = TestDb.Sqlite();
        var eski = AddGroup(db, "Eski guruh");
        var yangi = AddGroup(db, "Yangi guruh");

        // Yorliq "Eski guruh"da qolib ketgan, a'zolik esa "Yangi guruh"da.
        var me = AddStudent(db, "Ali Valiyev", "Eski guruh");
        AddMember(db, me, yangi);
        AddBall(db, me, yangi, 5);

        AddMemberWithBall(db, eski, "Eski guruh o'quvchisi", 9, className: "Eski guruh");
        AddMemberWithBall(db, yangi, "Yangi guruh o'quvchisi", 3);

        var dto = await PortalAsync(db, me);

        var only = Assert.Single(dto.Groups);
        Assert.Equal(yangi.Id, only.GroupId);
        Assert.Contains(only.Rows, r => r.StudentId == me.Id);        // o'zi ro'yxatda BOR
        Assert.DoesNotContain(only.Rows, r => r.FullName == "Eski guruh o'quvchisi");
    }

    /* =========================================================================================
     *  5. FALLBACK — faol a'zolik yo'q, faqat eski ClassName yorlig'i
     * ========================================================================================= */

    [Fact]
    public async Task Faol_azoligi_yoq_oquvchida_ClassName_fallback_ishlaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "Ingliz A1");

        var me = AddStudent(db, "Ali Valiyev", "Ingliz A1");          // a'zolik YO'Q, faqat yorliq
        AddBall(db, me, g, 5);
        AddMemberWithBall(db, g, "Vali Aliyev", 9, className: "Ingliz A1");

        var dto = await PortalAsync(db, me);

        var only = Assert.Single(dto.Groups);
        Assert.Equal("", only.GroupId);                               // "soxta" guruh — yorliq bo'yicha
        Assert.Equal("Ingliz A1", only.GroupName);
        Assert.Equal(2, only.Size);
        Assert.Equal(2, only.MeRank);
        Assert.NotEmpty(dto.ClassRows);
    }

    /// <summary>Chiqib ketgan (IsActive=false) a'zolik "faol guruh" hisoblanmaydi — fallbackka tushadi.</summary>
    [Fact]
    public async Task Chiqib_ketgan_azolik_faol_guruh_hisoblanmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "Ingliz A1");

        var me = AddStudent(db, "Ali Valiyev", "Ingliz A1");
        AddMember(db, me, g, active: false);
        AddBall(db, me, g, 5);
        AddMemberWithBall(db, g, "Vali Aliyev", 9, className: "Ingliz A1");

        var dto = await PortalAsync(db, me);

        var only = Assert.Single(dto.Groups);
        Assert.Equal("", only.GroupId);
        Assert.Contains(only.Rows, r => r.StudentId == me.Id);
    }

    /* =========================================================================================
     *  6. ORQAGA MOSLIK — ClassRows (eski nom) baribir to'ladi
     * ========================================================================================= */

    [Fact]
    public async Task ClassRows_orqaga_moslikda_toladi()
    {
        using var db = TestDb.Sqlite();
        var a = AddGroup(db, "Ingliz A1");
        var b = AddGroup(db, "Matematika");

        var me = AddStudent(db, "Ali Valiyev");                       // ClassName BO'SH
        AddMember(db, me, a);
        AddMember(db, me, b);
        AddBall(db, me, a, 5);
        AddMemberWithBall(db, a, "Vali Aliyev", 9);
        AddMemberWithBall(db, b, "Hasan Hasanov", 3);

        var dto = await PortalAsync(db, me);

        Assert.NotEmpty(dto.ClassRows);                               // ilgari BO'SH edi — bug shu
        Assert.Equal(dto.Groups[0].Rows, dto.ClassRows);              // birinchi guruh qatorlari
        Assert.Equal(me.Id, dto.MeStudentId);
        Assert.NotEmpty(dto.SchoolRows);
    }

    /// <summary>Markaz ro'yxati (TOP 15) va o'z markaz o'rni guruh o'zgarishidan qat'i nazar ishlaydi.</summary>
    [Fact]
    public async Task Markaz_top15_va_oz_orni_saqlanadi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "Ingliz A1");
        for (var i = 0; i < 20; i++) AddMemberWithBall(db, g, $"O'quvchi {i}", 100 - i);
        var me = AddMemberWithBall(db, g, "Ali Valiyev", 1);          // eng oxirgi

        var dto = await PortalAsync(db, me);

        Assert.Equal(15, dto.SchoolRows.Count);
        Assert.Equal(21, dto.SchoolSize);
        Assert.Equal(21, dto.MeSchoolRank);
        Assert.Equal(21, dto.Groups.Single().Size);
        Assert.Equal(21, dto.Groups[0].MeRank);
    }
}
