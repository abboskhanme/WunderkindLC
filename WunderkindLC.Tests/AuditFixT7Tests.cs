using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using Xunit;
using Group = WunderkindLC.Domain.Group;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-07 audit tuzatishlari (T7 — kirish, sessiyalar, ruxsat yordamchisi, portallar).
///
/// <para>Qulflanadi: parol tiklash/almashtirish va qurilma bekor qilinganda SESSIYALAR tugaydi
/// (refresh token, grace oynasi ham); <c>HasFullAccess</c> bo'limdan sahifaga merosi; parol
/// darvozasi = login/parolni ko'rish darvozasi va <c>InitialPassword</c> yangilanadi; turniket
/// qurilma ID yagonaligi; o'qituvchi tuzatgan ism QISMLARGA bo'linadi; push o'chirilgan
/// foydalanuvchiga yuborilmaydi; arxivdagi o'quvchi o'qituvchi bosh sahifasida yo'q.</para>
/// </summary>
public class AuditFixT7Tests
{
    /* =========================================================================================
     *  AUTH-1 — sessiyalarni tugatish (refresh token)
     * ========================================================================================= */

    private static JwtOptions Opts() => new()
    {
        Key = new string('k', 48), Issuer = "T", Audience = "T",
        AccessMinutes = 60, RefreshDays = 30,
    };

    private static RefreshTokenService Svc(AppDbContext db) =>
        new(db, new JwtTokenService(Opts()), Opts(), NullLogger<RefreshTokenService>.Instance);

    private static async Task<AppUser> UserAsync(AppDbContext db, string email = "a@a.a")
    {
        var u = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = email };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task RevokeAll_foydalanuvchining_HAMMA_sessiyasini_bekor_qiladi_boshqasiga_tegmaydi()
    {
        using var t = TestDb.Sqlite();
        var me = await UserAsync(t.Context);
        var other = await UserAsync(t.Context, "b@b.b");
        var svc = Svc(t.Context);
        var phone = await svc.IssueAsync(me.Id);
        var laptop = await svc.IssueAsync(me.Id);
        var stranger = await svc.IssueAsync(other.Id);

        var n = await RefreshTokenService.RevokeAllForUserAsync(t.Context, me.Id);

        Assert.Equal(2, n);
        // SaveChanges QILMAYDI — chaqiruvchining (parol o'zgarishi bilan) tranzaksiyasida saqlanadi.
        Assert.All(await t.Context.RefreshTokens.AsNoTracking().ToListAsync(), r => Assert.Null(r.RevokedAt));
        await t.Context.SaveChangesAsync();

        Assert.False((await svc.RotateAsync(phone.RawToken)).Ok);
        Assert.False((await svc.RotateAsync(laptop.RawToken)).Ok);
        Assert.True((await svc.RotateAsync(stranger.RawToken)).Ok);   // begona foydalanuvchi tegilmagan
    }

    /// <summary>Grace oynasi (60 s) FAQAT rotatsiya poygasi uchun: parol tiklangandan DARHOL keyin
    /// eski telefon refresh qilsa ham yangi sessiya OLMAYDI.</summary>
    [Fact]
    public async Task Parol_tiklangandan_keyin_grace_oynasida_ham_refresh_rad_etiladi()
    {
        using var t = TestDb.Sqlite();
        var me = await UserAsync(t.Context);
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(me.Id);

        await RefreshTokenService.RevokeAllForUserAsync(t.Context, me.Id);   // RevokedAt = hozir (grace ichida)
        await t.Context.SaveChangesAsync();
        var r = await svc.RotateAsync(issued.RawToken);

        Assert.False(r.Ok);
        Assert.Equal("revoked", r.Error);
        Assert.Equal(1, await t.Context.RefreshTokens.CountAsync());   // yangi token yaratilmagan
    }

    [Fact]
    public async Task Logout_dan_keyin_grace_oynasida_ham_refresh_rad_etiladi()
    {
        using var t = TestDb.Sqlite();
        var me = await UserAsync(t.Context);
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(me.Id);

        await svc.RevokeAsync(issued.RawToken);
        var r = await svc.RotateAsync(issued.RawToken);

        Assert.False(r.Ok);
        Assert.Equal("revoked", r.Error);
    }

    [Fact]
    public async Task Oz_parolini_almashtirganda_JORIY_sessiya_saqlanadi_qolgani_tugaydi()
    {
        using var t = TestDb.Sqlite();
        var me = await UserAsync(t.Context);
        var svc = Svc(t.Context);
        var current = await svc.IssueAsync(me.Id);
        var lostPhone = await svc.IssueAsync(me.Id);

        await RefreshTokenService.RevokeAllForUserAsync(t.Context, me.Id, keepFamilyId: current.FamilyId);
        await t.Context.SaveChangesAsync();

        Assert.True((await svc.RotateAsync(current.RawToken)).Ok);
        Assert.False((await svc.RotateAsync(lostPhone.RawToken)).Ok);
    }

    [Fact]
    public async Task Joriy_sessiya_cookie_dan_yoki_sfam_claim_dan_aniqlanadi()
    {
        using var t = TestDb.Sqlite();
        var me = await UserAsync(t.Context);
        var other = await UserAsync(t.Context, "b@b.b");
        var svc = Svc(t.Context);
        var mine = await svc.IssueAsync(me.Id);
        var foreign = await svc.IssueAsync(other.Id);
        var claim = Guid.NewGuid();

        // Web: rt cookie (xom token) — o'z tokeni.
        Assert.Equal(mine.FamilyId, await RefreshTokenService.CurrentFamilyAsync(t.Context, me.Id, mine.RawToken, null));
        // Begona foydalanuvchining tokeni — hisobga OLINMAYDI, claim'ga tushadi.
        Assert.Equal(claim, await RefreshTokenService.CurrentFamilyAsync(t.Context, me.Id, foreign.RawToken, claim.ToString()));
        // Mobil: faqat access tokendagi claim.
        Assert.Equal(claim, await RefreshTokenService.CurrentFamilyAsync(t.Context, me.Id, null, claim.ToString()));
        // Hech narsa yo'q — null (ya'ni hamma sessiya bekor qilinadi).
        Assert.Null(await RefreshTokenService.CurrentFamilyAsync(t.Context, me.Id, null, "buzuq"));
    }

    [Fact]
    public async Task Access_tokenga_sessiya_nasli_yoziladi_rotatsiyada_ham()
    {
        using var t = TestDb.Sqlite();
        var me = await UserAsync(t.Context);
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(me.Id);

        var direct = new JwtTokenService(Opts()).CreateToken(me, issued.FamilyId);
        var rotated = await svc.RotateAsync(issued.RawToken);

        string? Sfam(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims
            .FirstOrDefault(c => c.Type == JwtTokenService.SessionClaimType)?.Value;
        Assert.Equal(issued.FamilyId.ToString(), Sfam(direct));
        Assert.Equal(issued.FamilyId.ToString(), Sfam(rotated.AccessToken!));
        Assert.Null(Sfam(new JwtTokenService(Opts()).CreateToken(me)));   // eski chaqiruv — claim'siz
    }

    /* ---------- LOW: "oxirgi kirish" refresh'da ham yangilanadi ---------- */

    [Fact]
    public void StampLastSeen_bosh_yoki_eskirgan_bolsa_yozadi_yaqinda_bolsa_yozmaydi()
    {
        var now = new DateTime(2026, 10, 7, 12, 0, 0);
        var u = new AppUser();
        Assert.True(RefreshTokenService.StampLastSeen(u, now));
        Assert.Equal("2026-10-07T12:00:00", u.LastLoginAt);

        Assert.False(RefreshTokenService.StampLastSeen(u, now.AddMinutes(2)));   // 5 daqiqa ichida — yozilmaydi
        Assert.Equal("2026-10-07T12:00:00", u.LastLoginAt);

        Assert.True(RefreshTokenService.StampLastSeen(u, now.AddMinutes(10)));
        Assert.Equal("2026-10-07T12:10:00", u.LastLoginAt);

        u.LastLoginAt = "buzuq";
        Assert.True(RefreshTokenService.StampLastSeen(u, now));
    }

    [Fact]
    public async Task Refresh_LastLoginAt_ni_yangilaydi()
    {
        using var t = TestDb.Sqlite();
        var me = await UserAsync(t.Context);
        me.LastLoginAt = "2026-01-01T08:00:00";
        await t.Context.SaveChangesAsync();
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(me.Id);

        Assert.True((await svc.RotateAsync(issued.RawToken)).Ok);

        var stored = await t.Context.Users.AsNoTracking().SingleAsync(u => u.Id == me.Id);
        Assert.NotEqual("2026-01-01T08:00:00", stored.LastLoginAt);
        Assert.StartsWith(AppClock.Today.ToString("yyyy-MM-dd"), stored.LastLoginAt);
    }

    /* ---------- Har bir parol yo'li sessiyalarni tugatadi (manba matnidan) ---------- */

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

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file));

    /// <summary>Endpoint tanasi — imzodan keyingi <c>[Http</c> atributigacha.</summary>
    private static string Body(string src, string signature)
    {
        var i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i > 0, $"Topilmadi: {signature}");
        var next = src.IndexOf("[Http", i, StringComparison.Ordinal);
        return next > 0 ? src[i..next] : src[i..];
    }

    [Theory]
    [InlineData("StudentsController.cs", "public async Task<ActionResult<CredentialsDto>> ResetPassword(")]
    [InlineData("StudentsController.cs", "public async Task<IActionResult> Update(string id, StudentPayload p")]
    [InlineData("TeachersController.cs", "public async Task<ActionResult<CredentialsDto>> ResetPassword(")]
    [InlineData("TeachersController.cs", "public async Task<ActionResult<Teacher>> Update(string id, TeacherPayload p)")]
    [InlineData("StaffController.cs", "public async Task<ActionResult<CredentialsDto>> ResetPassword(")]
    [InlineData("StaffController.cs", "public async Task<ActionResult<StaffDto>> Update(")]
    [InlineData("AdminFaceController.cs", "public async Task<IActionResult> RevokeDevice(")]
    public void Parol_ornatish_va_qurilma_bekor_qilish_HAMMA_sessiyalarni_tugatadi(string file, string signature)
    {
        Assert.Contains("RefreshTokenService.RevokeAllForUserAsync(db,", Body(Source(file), signature));
    }

    [Theory]
    [InlineData("AuthController.cs", "public async Task<ActionResult<UserDto>> UpdateAccount(")]
    [InlineData("StudentPortalController.cs", "public async Task<ActionResult> ChangePassword(")]
    public void Oz_parolini_almashtirish_BOSHQA_sessiyalarni_tugatadi(string file, string signature)
    {
        var body = Body(Source(file), signature);
        Assert.Contains("EndOtherSessionsAsync(HttpContext, db, user.Id)", body);
        // Sessiyalar parol bilan BITTA saqlashda yoziladi.
        Assert.True(body.IndexOf("EndOtherSessionsAsync", StringComparison.Ordinal)
                    < body.IndexOf("SaveChangesAsync", StringComparison.Ordinal));
    }

    /* =========================================================================================
     *  ADM-1 — HasFullAccess: bo'limdan sahifaga meros
     * ========================================================================================= */

    [Theory]
    [InlineData("students", "students.list", true)]       // yalang bo'lim — sahifani to'liq ochadi (pastga meros)
    [InlineData("students.list", "students.list", true)]
    [InlineData("teachers", "teachers.list", true)]
    [InlineData("staff", "staff", true)]
    [InlineData("students:create", "students.list", false)]   // amal — TO'LIQ emas
    [InlineData("students.list:edit", "students.list", false)]
    [InlineData("students.turnstile", "students.list", false)] // boshqa sahifa
    [InlineData("students.list", "students", false)]           // yuqoriga meros YO'Q
    [InlineData("students-arxiv", "students.list", false)]     // nom bo'yicha adashish yo'q
    public void HasFullSection_pastga_meros_bilan(string claim, string key, bool expected)
    {
        Assert.Equal(expected, PermissionRules.HasFullSection(new[] { claim }, key));
    }

    [Fact]
    public void HasFullAccess_PermissionRules_ga_tayanadi_aynan_tenglikka_emas()
    {
        var src = Source("AdminPermAttribute.cs");
        var i = src.IndexOf("public static bool HasFullAccess(", StringComparison.Ordinal);
        Assert.True(i > 0);
        var body = src[i..src.IndexOf(';', i)];
        Assert.Contains("PermissionRules.HasFullSection(PermValues(user), section)", body);
        Assert.DoesNotContain("c.Value == section", body);
    }

    /* =========================================================================================
     *  AUTH-4/5 — parol darvozasi = ko'rish darvozasi; InitialPassword yangilanadi; audit
     * ========================================================================================= */

    [Theory]
    // Student reset names its source for the password vault (CRM Phase 6: `reset`, auth-sessions.md §6).
    [InlineData("StudentsController.cs", "students.list", "user.SetInitialPassword(pwd, PasswordSources.Reset)")]
    [InlineData("TeachersController.cs", "teachers.list", "user.SetInitialPassword(pwd)")]
    public void ResetPassword_Credentials_bilan_bir_xil_darvoza_va_yangi_InitialPassword(string file, string key, string setCall)
    {
        var body = Body(Source(file), "public async Task<ActionResult<CredentialsDto>> ResetPassword(");
        var gate = body.IndexOf($"HasFullAccess(User, \"{key}\")", StringComparison.Ordinal);
        Assert.True(gate > 0, "ResetPassword HasFullAccess bilan darvozalanmagan");
        Assert.True(gate < body.IndexOf("GeneratePassword", StringComparison.Ordinal));
        // Credentials / eksport / bot login xabari yangi parolni ko'rsatsin.
        Assert.Contains(setCall, body);
        Assert.DoesNotContain("PasswordHash =", body);
        // Parol tiklash tarixda ko'rinadi (parolning O'ZI emas).
        Assert.Contains("audit.Record(", body);
        Assert.DoesNotContain("{pwd}", body);
    }

    [Theory]
    [InlineData("StudentsController.cs", "public async Task<IActionResult> Update(string id, StudentPayload p", "students.list")]
    [InlineData("TeachersController.cs", "public async Task<ActionResult<Teacher>> Update(string id, TeacherPayload p)", "teachers.list")]
    public void Formada_parol_ornatish_ham_toliq_ruxsat_talab_qiladi(string file, string signature, string key)
    {
        var body = Body(Source(file), signature);
        var gate = body.IndexOf($"HasFullAccess(User, \"{key}\")", StringComparison.Ordinal);
        Assert.True(gate > 0);
        Assert.True(gate < body.IndexOf("SetInitialPassword(pwd)", StringComparison.Ordinal));
    }

    [Fact]
    public void SetInitialPassword_hash_va_korinadigan_parolni_birga_yozadi()
    {
        var u = new AppUser { InitialPassword = "eski-parol" };
        u.SetInitialPassword("yangi1234");
        Assert.Equal("yangi1234", u.InitialPassword);
        Assert.True(PasswordHasher.Verify("yangi1234", u.PasswordHash));
        Assert.False(PasswordHasher.Verify("eski-parol", u.PasswordHash));
    }

    /* =========================================================================================
     *  AUTH-7 — turniket qurilma ID yagonaligi
     * ========================================================================================= */

    private static TurnstileService.DeviceOwner Owner(string kind, string id, string name, string device) =>
        new(kind, id, name, device);

    [Fact]
    public void DeviceConflict_oquvchi_va_oqituvchida_bir_xil_ID_rad_etiladi_kimdaligi_aytiladi()
    {
        var msg = TurnstileService.DeviceConflict(new[]
        {
            Owner("teacher", "t1", "Ali Valiyev", "17"),
            Owner("student", "s1", "Aziza Karimova", " 17 "),
        });

        Assert.NotNull(msg);
        Assert.Contains("«17»", msg);
        Assert.Contains("o'qituvchi «Ali Valiyev»", msg);
        Assert.Contains("o'quvchi «Aziza Karimova»", msg);
    }

    [Fact]
    public void DeviceConflict_ikki_oqituvchi_bir_ID_ham_rad_etiladi()
    {
        Assert.NotNull(TurnstileService.DeviceConflict(new[]
        {
            Owner("teacher", "t1", "A", "5"), Owner("teacher", "t2", "B", "5"),
        }));
    }

    [Fact]
    public void DeviceConflict_turli_ID_bosh_ID_va_ozining_ozi_toqnashuv_emas()
    {
        Assert.Null(TurnstileService.DeviceConflict(new[]
        {
            Owner("teacher", "t1", "A", "5"), Owner("student", "s1", "B", "6"),
            Owner("student", "s2", "C", ""), Owner("teacher", "t2", "D", ""),
            // Bir odam ikki marta ro'yxatda (masalan joriy + yangi qiymat) — to'qnashuv EMAS.
            Owner("student", "s3", "E", "9"), Owner("student", "s3", "E", "9"),
        }));
    }

    [Fact]
    public async Task DeviceOwners_arxivdagilarni_hisobga_olmaydi()
    {
        using var t = TestDb.Sqlite();
        t.Context.Students.AddRange(
            new Student { FullName = "Faol", DeviceUserId = "11" },
            new Student { FullName = "Arxivda", DeviceUserId = "12", IsArchived = true });
        t.Context.Teachers.AddRange(
            new Teacher { FullName = "Ustoz", DeviceUserId = "11" },
            new Teacher { FullName = "Ketgan ustoz", DeviceUserId = "12", IsArchived = true });
        await t.Context.SaveChangesAsync();

        var owners = await TurnstileService.DeviceOwnersAsync(t.Context, new[] { "11", "12" });

        Assert.Equal(new[] { "Faol", "Ustoz" }, owners.Select(o => o.Name).OrderBy(x => x));
        var studentsOnly = await TurnstileService.DeviceOwnersAsync(t.Context, new[] { "11" }, teachers: false);
        Assert.Equal("student", Assert.Single(studentsOnly).Kind);
    }

    [Fact]
    public void Qurilma_ID_saqlash_yollari_yagonalikni_tekshiradi()
    {
        Assert.Contains("TurnstileService.DeviceConflict(owners)",
            Body(Source("StudentTurnstileController.cs"), "public async Task<IActionResult> SetDevice("));
        Assert.Contains("TurnstileService.DeviceConflict(owners)",
            Body(Source("SettingsController.cs"), "public async Task<ActionResult<TurnstileSettingsDto>> SaveTurnstile("));
    }

    /* =========================================================================================
     *  AUTH-3 — o'qituvchi tuzatgan ism QISMLARGA bo'linadi (admin tahriri bekor qilmasin)
     * ========================================================================================= */

    [Fact]
    public void Oqituvchi_ismni_tuzatsa_qismlar_ham_yangilanadi()
    {
        var s = new Student
        {
            FullName = "Karimov Ali", LastName = "Karimov", FirstName = "Ali", MiddleName = "",
        };

        TeacherStudentEdit.Apply(s, new TeacherStudentEdit.Input("  Karimova   Aziza  Valiyevna  qizi ", null, null),
            new DateOnly(2026, 10, 7));

        Assert.Equal("Karimova Aziza Valiyevna qizi", s.FullName);
        Assert.Equal(("Karimova", "Aziza", "Valiyevna qizi"), (s.LastName, s.FirstName, s.MiddleName));
        // Admin formasi qismlarni qayta yuboradi va server FullName'ni ulardan YIG'ADI
        // (`StudentsController.JoinName`) — natija o'qituvchi yozgani bilan AYNAN bir xil.
        var rebuilt = string.Join(' ', new[] { s.LastName, s.FirstName, s.MiddleName }.Where(x => x.Length > 0));
        Assert.Equal(s.FullName, rebuilt);
    }

    [Theory]
    [InlineData("Ali", "Ali", "", "")]
    [InlineData("Karimov Ali", "Karimov", "Ali", "")]
    [InlineData("", "", "", "")]
    public void SplitName_klientdagi_splitFullName_bilan_bir_xil(string full, string last, string first, string middle)
    {
        Assert.Equal((last, first, middle), TeacherStudentEdit.SplitName(full));
    }

    // (AUTH-6 — push o'chirilgan foydalanuvchiga push yuborilmaydi — 2026-10 da push bilan birga olib tashlandi.)

    /* =========================================================================================
     *  C1 (qism) — arxivdagi o'quvchi o'qituvchi bosh sahifasida YO'Q
     * ========================================================================================= */

    private static (Teacher T, Group G, Student Live, Student Archived) SeedTeacherGroup(AppDbContext ctx)
    {
        var course = new Subject { Name = "Ingliz tili" };
        ctx.Subjects.Add(course);
        var t = new Teacher { FullName = "Ustoz", Permissions = TeacherPermissions.All.ToList() };
        ctx.Teachers.Add(t);
        var g = new Group
        {
            Name = "A1", CourseId = course.Id, TeacherId = t.Id, MonthlyFee = 600_000m,
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 }, StartTime = "14:00", EndTime = "15:30",
        };
        ctx.Classes.Add(g);
        var live = new Student { FullName = "Aziza" };
        // Eski arxivlash: a'zolik YOPILMAGAN (IsActive) holda qolgan.
        var archived = new Student { FullName = "Arxivdagi Bola", IsArchived = true };
        ctx.Students.AddRange(live, archived);
        foreach (var s in new[] { live, archived })
        {
            ctx.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
                JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
            });
            var month = AppClock.Today.ToString("yyyy-MM");
            ctx.MonthlyCharges.Add(new MonthlyCharge
            {
                StudentId = s.Id, GroupId = g.Id, Month = month, Amount = 600_000m, Date = month + "-01",
            });
        }
        ctx.SaveChanges();
        return (t, g, live, archived);
    }

    [Fact]
    public async Task Oqituvchi_qarzdorlari_arxivdagi_oquvchini_sanamaydi()
    {
        using var db = TestDb.Sqlite();
        var (_, g, _, _) = SeedTeacherGroup(db.Context);

        var debt = await TeacherHomeService.DebtSummaryAsync(db.Context, new[] { g.Id });

        Assert.Equal(1, debt.DebtorCount);
        Assert.Equal(600_000m, debt.DebtTotal);
    }

    [Fact]
    public async Task Oqituvchi_royxati_va_sanoqlarida_arxivdagi_oquvchi_yoq()
    {
        using var db = TestDb.Sqlite();
        var (t, _, live, archived) = SeedTeacherGroup(db.Context);

        var students = await TeacherHomeService.StudentsAsync(db.Context, t.Id, null);
        var overview = await TeacherHomeService.OverviewAsync(db.Context, t.Id, AppClock.Today, canSchedule: true);

        Assert.Contains(students.Rows, r => r.StudentId == live.Id);
        Assert.DoesNotContain(students.Rows, r => r.StudentId == archived.Id);
        Assert.Equal(1, overview.StudentCount);
        Assert.Equal(1, Assert.Single(overview.Groups).Active);
    }

    /* =========================================================================================
     *  LOW — eski HomeroomClass guruhga kirish bermaydi; portal davomati bir xil guruhlar bo'yicha
     * ========================================================================================= */

    [Fact]
    public void TaughtClassIds_HomeroomClass_bo_yicha_kirish_bermaydi()
    {
        var src = Source("TeacherPortalController.cs");
        var i = src.IndexOf("private async Task<HashSet<string>> TaughtClassIdsAsync(", StringComparison.Ordinal);
        Assert.True(i > 0);
        var body = src[i..src.IndexOf("\n    }", i, StringComparison.Ordinal)];
        Assert.DoesNotContain("HomeroomClass", body);
    }

    [Fact]
    public async Task Portal_davomat_guruhlari_barcha_tirik_azoliklar()
    {
        using var t = TestDb.Sqlite();
        var a = new Group { Name = "A" };
        var b = new Group { Name = "B" };
        var left = new Group { Name = "Chiqqan" };
        t.Context.Classes.AddRange(a, b, left);
        var s = new Student { FullName = "Ikki guruhli", ClassName = "A" };
        t.Context.Students.Add(s);
        t.Context.StudentGroups.AddRange(
            new StudentGroup { StudentId = s.Id, GroupId = a.Id, Status = "active", IsActive = true },
            new StudentGroup { StudentId = s.Id, GroupId = b.Id, Status = "frozen", IsActive = true },
            new StudentGroup { StudentId = s.Id, GroupId = left.Id, Status = "active", IsActive = false });
        await t.Context.SaveChangesAsync();

        var groups = await StudentReportBuilder.GroupsAsync(t.Context, s);

        Assert.Equal(new[] { "A", "B" }, groups.Select(g => g.Name).OrderBy(x => x));
        var body = Body(Source("StudentPortalController.cs"), "public async Task<ActionResult<StudentAttendanceFullDto>> Attendance(");
        Assert.Contains("StudentReportBuilder.GroupsAsync(db, s)", body);
        Assert.Contains("groupIds.Contains(e.ClassId)", body);
    }
}
