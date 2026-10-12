using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-08 relizdan oldingi XAVFSIZLIK tekshiruvi tuzatishlari:
/// <list type="number">
///   <item>onlayn test boti orqali serverning ISTALGAN faylini o'qish (<c>TestResult.PdfUrl</c>) — 2026-10 dan
///     bot oqimi butunlay olib tashlangan;</item>
///   <item>arxivdan qaytarishda o'zi tanlagan parol (parol darvozasisiz);</item>
///   <item>klientdan kelgan manzil bo'yicha fayl O'CHIRISH (CV, guruh o'chirish);</item>
///   <item>lidni ISTALGAN o'quvchiga bog'lash va ruxsatsiz guruhga qo'shish;</item>
///   <item>OTP/yuz bilan kirishda ochiq <c>InitialPassword</c> qolib ketishi;</item>
///   <item>kameralar: har xodimga ochiq o'qish va RTSP login/paroli javobda;</item>
///   <item>arxivdan tiklashda <c>ex.Message</c> klientga;</item>
///   <item>NVR ISAPI login/parolining Basic bilan ochiq yuborilishi.</item>
/// </list>
/// Test loyihasi Server'ga bog'lanmagan — controller oqimi MANBA MATNI bo'yicha qulflanadi,
/// mantiqning o'zi esa Application yordamchilarida (sof/DB testlar).
/// </summary>
public class SecurityFixTests
{
    private const string GuidHex = "0123456789abcdef0123456789abcdef";
    private const string ServerPdf = "/uploads/" + GuidHex + ".pdf";
    private const string OtherPdf = "/uploads/fedcba9876543210fedcba9876543210.pdf";

    private static string D(int days) => AppClock.Today.AddDays(days).ToString("yyyy-MM-dd");

    /* =========================================================================================
     *  1) FAYL MANZILI QOIDALARI — UploadUrlRules
     * ========================================================================================= */

    [Theory]
    [InlineData(ServerPdf, true)]
    [InlineData("/uploads/t.pdf", false)]                                   // server nomi emas
    [InlineData("/uploads/0123456789ABCDEF0123456789ABCDEF.pdf", false)]    // katta harf — SafeName yaratmaydi
    [InlineData("/uploads/" + GuidHex + ".PDF", false)]
    [InlineData("/uploads/" + GuidHex + "xpdf", false)]                     // nuqta ESCAPE qilingan
    [InlineData("/uploads/" + GuidHex + ".pdf\n", false)]                   // `$` emas, `\z`
    [InlineData("/uploads/" + GuidHex + ".docx", false)]
    [InlineData("/uploads/../.env", false)]
    [InlineData("/../.env", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("/uploads/face/" + GuidHex + ".pdf", false)]
    [InlineData("/uploads/certificates/" + GuidHex + ".pdf", false)]
    [InlineData("https://evil.com/" + GuidHex + ".pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsServerPdf_faqat_server_saqlagan_tekis_PDF(string? url, bool expected)
    {
        Assert.Equal(expected, UploadUrlRules.IsServerPdf(url));
    }

    [Fact]
    public void CV_nomi_cv_prefiksli_va_faqat_shu_qabul_qilinadi()
    {
        var name = UploadUrlRules.NewCvFileName();
        Assert.StartsWith("cv-", name);
        Assert.True(UploadUrlRules.IsCareerCv("/uploads/" + name));
        Assert.False(UploadUrlRules.IsCareerCv(ServerPdf));                 // shartnoma / test PDF
        Assert.False(UploadUrlRules.IsCareerCv("/uploads/cv-" + GuidHex + ".pdf/../x.pdf"));
        Assert.False(UploadUrlRules.IsCareerCv("/uploads/face/cv-" + GuidHex + ".pdf"));
        Assert.False(UploadUrlRules.IsCareerCv(null));
    }

    [Theory]
    [InlineData("/uploads/certificates/cert-" + GuidHex + ".docx", true)]
    [InlineData("/uploads/certificates/cert-" + GuidHex + ".pdf", true)]
    [InlineData("/uploads/certificates/a.pdf", false)]
    [InlineData("/uploads/certificates/../cert-" + GuidHex + ".pdf", false)]
    [InlineData("/uploads/face/cert-" + GuidHex + ".pdf", false)]
    [InlineData("/uploads/cert-" + GuidHex + ".pdf", false)]
    [InlineData("", false)]
    public void IsTestCertificateFile_faqat_server_yaratgan_sertifikat(string url, bool expected)
    {
        Assert.Equal(expected, UploadUrlRules.IsTestCertificateFile(url));
    }

    [Fact]
    public void ResolveFlat_faqat_uploads_ning_ozidagi_fayl()
    {
        var root = Path.Combine(Path.GetTempPath(), "wlc-sec-root");
        var uploads = Path.GetFullPath(Path.Combine(root, "uploads"));

        Assert.Equal(Path.Combine(uploads, GuidHex + ".pdf"), UploadUrlRules.ResolveFlat(root, ServerPdf));

        Assert.Null(UploadUrlRules.ResolveFlat(root, "/uploads/../.env"));
        Assert.Null(UploadUrlRules.ResolveFlat(root, "/../.env"));
        Assert.Null(UploadUrlRules.ResolveFlat(root, "uploads/a.pdf"));
        Assert.Null(UploadUrlRules.ResolveFlat(root, "/uploads/"));
        Assert.Null(UploadUrlRules.ResolveFlat(root, "/uploads/.."));
        Assert.Null(UploadUrlRules.ResolveFlat(root, "/uploads/face/a.jpg"));
        Assert.Null(UploadUrlRules.ResolveFlat(root, "/uploads/certificates/a.pdf"));
        Assert.Null(UploadUrlRules.ResolveFlat(root, "/uploads/..\\..\\.env"));
        Assert.Null(UploadUrlRules.ResolveFlat(root, "/uploads/C:secret"));
        Assert.Null(UploadUrlRules.ResolveFlat(root, null));
    }

    /* =========================================================================================
     *  1) ONLAYN TEST PDF — 2026-10 dan test PDF'ini YOZADIGAN va BOTGA YUBORADIGAN yo'l YO'Q
     *     (onlayn test rejimi va bot oqimi olib tashlandi — testlar faqat oflayn). Eski qatorlardagi
     *     PdfUrl faqat guruh o'chirilganda naqsh bo'yicha tozalanadi (pastdagi 3-bo'lim).
     * ========================================================================================= */

    private static async Task<Group> GroupAsync(AppDbContext ctx)
    {
        var g = new Group { Name = "Sec " + Guid.NewGuid().ToString("N")[..6], Days = new List<int> { 0, 1, 2 } };
        ctx.Classes.Add(g);
        await ctx.SaveChangesAsync();
        return g;
    }

    [Fact]
    public void Onlayn_test_boti_va_PDF_yozish_yoli_QAYTIB_KELMAGAN()
    {
        Assert.False(File.Exists(Path.Combine(RepoRoot, "WunderkindLC.Application", "Services", "OnlineTestBotService.cs")));
        Assert.False(File.Exists(Path.Combine(RepoRoot, "WunderkindLC.Application", "Services", "OnlineTestService.cs")));
        var svc = Source("WunderkindLC.Application", "Services", "TestResultService.cs");
        Assert.DoesNotContain(".PdfUrl =", svc);
    }

    /* =========================================================================================
     *  1) BOT — o'qishda ham qamrov (bazada eskidan qolgan yomon qiymat uchun)
     * ========================================================================================= */

    [Theory]
    [InlineData("WunderkindLC.Application", "Services", "BookShopBotService.cs", "UploadUrlRules.ResolveFlat(env.ContentRootPath, book.CoverUrl)", "book.CoverUrl.TrimStart('/')")]
    public void Bot_fayl_yuborishda_yol_uploads_ichida_hal_qilinadi(string p1, string p2, string file, string must, string mustNot)
    {
        var src = Source(p1, p2, file);
        Assert.Contains(must, src);
        Assert.DoesNotContain(mustNot, src);
    }

    /* =========================================================================================
     *  3) GURUH O'CHIRISH — faqat server yaratgan fayllar o'chiriladi
     * ========================================================================================= */

    [Fact]
    public async Task Guruh_ochirishda_begona_manzilli_fayllar_ROYXATGA_TUSHMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = await GroupAsync(ctx);
        var other = await GroupAsync(ctx);

        const string lessonPdf = "/uploads/11111111111111111111111111111111.pdf";
        const string contractPdf = "/uploads/22222222222222222222222222222222.pdf";
        const string certOk = "/uploads/certificates/cert-" + GuidHex + ".pdf";

        var tests = new[]
        {
            new TestResult { GroupId = g.Id, Name = "Yaxshi", Date = D(0), MaxScore = 4, PdfUrl = ServerPdf },
            new TestResult { GroupId = g.Id, Name = "Env", Date = D(0), MaxScore = 4, PdfUrl = "/../.env" },
            new TestResult { GroupId = g.Id, Name = "Selfi", Date = D(0), MaxScore = 4, PdfUrl = "/uploads/face/" + GuidHex + ".jpg" },
            new TestResult { GroupId = g.Id, Name = "Dars", Date = D(0), MaxScore = 4, PdfUrl = lessonPdf },
            new TestResult { GroupId = g.Id, Name = "Shartnoma", Date = D(0), MaxScore = 4, PdfUrl = contractPdf },
            new TestResult { GroupId = g.Id, Name = "Ulashilgan", Date = D(0), MaxScore = 4, PdfUrl = OtherPdf },
        };
        ctx.TestResults.AddRange(tests);
        // Boshqa guruh testi o'sha PDF'ni ishlatadi — tegilmaydi (avvalgi qoida).
        ctx.TestResults.Add(new TestResult { GroupId = other.Id, Name = "Boshqa", Date = D(0), MaxScore = 4, PdfUrl = OtherPdf });
        ctx.CourseItems.Add(new CourseItem { Text = "Dars PDF", Type = "pdf", PdfUrl = lessonPdf });
        ctx.Contracts.Add(new Contract { Number = 1, PdfUrl = contractPdf });
        await ctx.SaveChangesAsync();
        ctx.TestCertificates.Add(new TestCertificate
        {
            TestResultId = tests[0].Id, StudentId = "s1",
            DocxUrl = "/uploads/face/" + GuidHex + ".jpg",    // soxta: sertifikat naqshida emas
            PdfUrl = certOk,
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var group = await ctx.Classes.FirstAsync(x => x.Id == g.Id);
        var res = await GroupDeletion.PurgeLearningDataAsync(ctx, group, "Admin");

        Assert.Equal(new[] { certOk, ServerPdf }.OrderBy(x => x), res.FileUrls.OrderBy(x => x));
    }

    /* =========================================================================================
     *  2) ARXIVDAN QAYTARISH — o'zi tanlagan parol = parol darvozasi (manba matni)
     * ========================================================================================= */

    [Theory]
    [InlineData("StudentsController.cs", "students.list", "PaymentIdempotency.LockAsync(")]
    [InlineData("TeachersController.cs", "teachers.list", "teacher.InactivePeriods = ")]
    public void Arxivdan_qaytarishda_yangi_parol_HasFullAccess_bilan_va_OZGARISHDAN_OLDIN(
        string file, string section, string firstChange)
    {
        var body = Body(Source("WunderkindLC.Server", "Controllers", file),
            "public async Task<IActionResult> Restore(string id, Restore");
        var gate = $"newPwd.Length > 0 && !AdminPermAttribute.HasFullAccess(User, \"{section}\")";
        Assert.Contains(gate, body);
        Assert.Contains("PasswordForbiddenMessage", body);
        Assert.True(body.IndexOf(gate, StringComparison.Ordinal) < body.IndexOf(firstChange, StringComparison.Ordinal),
            "Darvoza har qanday o'zgarishdan OLDIN turishi kerak");
        Assert.True(body.IndexOf(gate, StringComparison.Ordinal) < body.IndexOf("IsArchived = false", StringComparison.Ordinal));
    }

    /* =========================================================================================
     *  3) KARYERA — CV faqat o'z yuklashi, o'chirish faqat naqshga mos fayl (manba matni)
     * ========================================================================================= */

    [Fact]
    public void Karyera_CV_yuklash_ariza_va_ochirish_naqsh_bilan()
    {
        var pub = Source("WunderkindLC.Server", "Controllers", "PublicCareerController.cs");
        Assert.Contains("var stored = UploadUrlRules.NewCvFileName();", pub);
        var apply = Body(pub, "public async Task<ActionResult<PublicApplicationDto>> Apply(");
        Assert.Contains("!UploadUrlRules.IsCareerCv(cvUrl)", apply);
        Assert.Contains("a.CvUrl == cvUrl && a.ChatId != me.ChatId", apply);
        Assert.DoesNotContain("cvUrl.EndsWith(\".pdf\"", apply);

        var admin = Source("WunderkindLC.Server", "Controllers", "CareerController.cs");
        var del = Body(admin, "public async Task<IActionResult> DeleteApplication(");
        Assert.Contains("UploadUrlRules.IsCareerCv(a.CvUrl)", del);
        Assert.Contains("!await db.JobApplications.AnyAsync(x => x.CvUrl == a.CvUrl)", del);
        Assert.Contains("UploadUrlRules.ResolveFlat(env.ContentRootPath, cvUrl)", admin);
    }

    /* =========================================================================================
     *  4) LID → MAVJUD O'QUVCHIGA BOG'LASH
     * ========================================================================================= */

    private static Lead MakeLead(string name, string phone = "", string father = "", string mother = "") =>
        new() { FullName = name, Phone = phone, FatherPhone = father, MotherPhone = mother };

    [Fact]
    public void CanLinkTo_faqat_shu_odamning_ozi()
    {
        var lead = MakeLead("Ali Valiyev", father: "+998901234567");

        Assert.True(LeadConversion.CanLinkTo(lead,
            new Student { FullName = "ali  valiyev", ParentPhone = "901234567" }));
        // Aka-uka: bir xil ota raqami, BOSHQA ism — boshqa odam.
        Assert.False(LeadConversion.CanLinkTo(lead,
            new Student { FullName = "Vali Valiyev", FatherPhone = "+998901234567" }));
        // Ismdosh, telefon mos emas.
        Assert.False(LeadConversion.CanLinkTo(lead,
            new Student { FullName = "Ali Valiyev", Phone = "+998935555555" }));
        // Arxivdagi o'quvchiga bog'lanmaydi.
        Assert.False(LeadConversion.CanLinkTo(lead,
            new Student { FullName = "Ali Valiyev", ParentPhone = "+998901234567", IsArchived = true }));
        // Lidda telefon yo'q — ism bo'yicha birlashtirilmaydi.
        Assert.False(LeadConversion.CanLinkTo(MakeLead("Ali Valiyev"),
            new Student { FullName = "Ali Valiyev", Phone = "+998901234567" }));
    }

    [Fact]
    public void Lid_konversiyasi_bogalash_nomzod_va_guruh_ruxsati_bilan()
    {
        var src = Source("WunderkindLC.Server", "Controllers", "LeadsController.cs");
        var convert = Body(src, "public async Task<IActionResult> Convert(");
        Assert.Contains("!LeadConversion.CanLinkTo(lead, existing)", convert);
        Assert.True(convert.IndexOf("CanLinkTo", StringComparison.Ordinal)
                    < convert.IndexOf("LeadConversion.LinkAsync(", StringComparison.Ordinal));
        // Guruh tanlansa — `AddMember` bilan bir xil ruxsat, ikkala yo'ldan (bog'lash/yaratish) OLDIN.
        Assert.Contains("if (!MayAddToGroups())", convert);
        Assert.True(convert.IndexOf("MayAddToGroups()", StringComparison.Ordinal)
                    < convert.IndexOf("LeadConversion.LinkAsync(", StringComparison.Ordinal));
        Assert.True(convert.IndexOf("MayAddToGroups()", StringComparison.Ordinal)
                    < convert.IndexOf("LeadConversion.ConvertAsync(", StringComparison.Ordinal));
        Assert.Contains("PermissionRules.CanWrite(claims, \"classes.list\", HttpMethods.Post)", src);
    }

    [Theory]
    [InlineData("classes", true)]
    [InlineData("classes.list", true)]
    [InlineData("classes:create", true)]
    [InlineData("classes.list:create", true)]
    [InlineData("classes:edit", false)]
    [InlineData("classes.rooms", false)]
    [InlineData("students.list", false)]
    public void Guruhga_qoshish_ruxsati_AddMember_qoidasi(string claim, bool expected)
    {
        Assert.Equal(expected, PermissionRules.CanWrite(new[] { claim }, "classes.list", "POST"));
    }

    /* =========================================================================================
     *  5) InitialPassword — HAR muvaffaqiyatli kirishda tozalanadi (manba matni)
     * ========================================================================================= */

    [Theory]
    [InlineData("AuthController.cs", "public async Task<ActionResult<LoginResponse>> Login(")]
    [InlineData("AuthController.cs", "public async Task<ActionResult<LoginResponse>> OtpLogin(")]
    [InlineData("StudentFaceController.cs", "private async Task<(string Token, string RefreshToken)> IssueSessionAsync(")]
    public void Har_kirish_yolida_ochiq_parol_tozalanadi(string file, string signature)
    {
        var src = Source("WunderkindLC.Server", "Controllers", file);
        var i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i > 0, $"Topilmadi: {signature}");
        var body = src[i..];
        var token = body.IndexOf("CreateToken(user, rt.FamilyId)", StringComparison.Ordinal);
        Assert.True(token > 0);
        var clear = body.IndexOf("user.InitialPassword = null;", StringComparison.Ordinal);
        Assert.True(clear > 0 && clear < token, "InitialPassword token berilishidan OLDIN tozalanishi kerak");
    }

    /* =========================================================================================
     *  6) KAMERALAR — o'qish ham ruxsat bilan, login/parol javobda YO'Q
     * ========================================================================================= */

    [Theory]
    [InlineData("rtsp://admin:Secr3t@192.168.1.10:554/Streaming/Channels/101", "rtsp://192.168.1.10:554/Streaming/Channels/101")]
    [InlineData("rtsp://admin:P@ss@10.0.0.5/Streaming/Channels/101", "rtsp://10.0.0.5/Streaming/Channels/101")]
    [InlineData("rtsp://192.168.1.10:554/ch1", "rtsp://192.168.1.10:554/ch1")]
    [InlineData("rtsp://user@cam.local", "rtsp://cam.local")]
    [InlineData("", "")]
    [InlineData("buzuq", "buzuq")]
    public void Redact_login_parolni_olib_tashlaydi(string url, string expected)
    {
        Assert.Equal(expected, CameraUrlSecrets.Redact(url));
        Assert.DoesNotContain("Secr3t", CameraUrlSecrets.Redact(url));
    }

    [Fact]
    public void Merge_parol_faqat_yoziladi_host_almashsa_ulanmaydi()
    {
        const string stored = "rtsp://admin:Secr3t@192.168.1.10:554/Streaming/Channels/101";

        // Forma tozalangan manzilni qaytardi — parol joyida.
        Assert.Equal(stored, CameraUrlSecrets.Merge(stored, CameraUrlSecrets.Redact(stored)));
        // Kanal o'zgardi, host o'sha — parol yangi yo'lga qo'shiladi.
        Assert.Equal("rtsp://admin:Secr3t@192.168.1.10:554/Streaming/Channels/102",
            CameraUrlSecrets.Merge(stored, "rtsp://192.168.1.10:554/Streaming/Channels/102"));
        // Bo'sh — eskisi.
        Assert.Equal(stored, CameraUrlSecrets.Merge(stored, "  "));
        // Yangi login/parol kiritildi — u.
        Assert.Equal("rtsp://op:new@192.168.1.10:554/x", CameraUrlSecrets.Merge(stored, "rtsp://op:new@192.168.1.10:554/x"));
        // ⚠️ Host BOSHQA — eski parol YANGI hostga ketmaydi.
        Assert.Equal("rtsp://evil.example:554/x", CameraUrlSecrets.Merge(stored, "rtsp://evil.example:554/x"));
        Assert.Equal("rtsp://192.168.1.10:8554/x", CameraUrlSecrets.Merge(stored, "rtsp://192.168.1.10:8554/x"));
        // Eskisida parol yo'q edi — yangisi o'zi.
        Assert.Equal("rtsp://192.168.1.11/x", CameraUrlSecrets.Merge("rtsp://192.168.1.10/x", "rtsp://192.168.1.11/x"));

        Assert.True(CameraUrlSecrets.HasCredentials(stored));
        Assert.False(CameraUrlSecrets.HasCredentials(CameraUrlSecrets.Redact(stored)));
        Assert.False(CameraUrlSecrets.HasCredentials(""));
    }

    [Fact]
    public void Kameralar_controlleri_oqishda_ham_ruxsat_va_tozalangan_manzil()
    {
        var src = Source("WunderkindLC.Server", "Controllers", "CamerasController.cs");
        Assert.Contains("[AdminPerm(\"cameras\", ReadRequiresPerm = true)]", src);
        Assert.Contains("CameraUrlSecrets.Redact(c.RtspUrl), CameraUrlSecrets.Redact(c.RtspSubUrl)", src);
        Assert.DoesNotContain("new(c.Id, c.Name, c.Location, c.RtspUrl", src);
        var update = Body(src, "public async Task<ActionResult<CameraDto>> Update(");
        Assert.Contains("CameraUrlSecrets.Merge(c.RtspUrl, req.RtspUrl)", update);
        Assert.Contains("CameraUrlSecrets.Merge(c.RtspSubUrl, req.RtspSubUrl)", update);
    }

    /* =========================================================================================
     *  7) ARXIVDAN TIKLASH — ex.Message klientga chiqmaydi
     * ========================================================================================= */

    [Fact]
    public void Arxivdan_tiklash_xato_matnini_klientga_bermaydi()
    {
        var src = Source("WunderkindLC.Server", "Controllers", "ArchiveController.cs");
        var restore = Body(src, "public async Task<IActionResult> Restore(string id)");
        // Izohlar hisobga olinmaydi — faqat KOD qatorlari.
        var code = string.Join("\n", restore.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        Assert.DoesNotContain("ex.Message", code);
        Assert.Contains("logger.LogError(ex,", restore);
    }

    /* =========================================================================================
     *  8) NVR ISAPI — FAQAT Digest (Basic bilan parol ochiq ketmaydi)
     * ========================================================================================= */

    [Fact]
    public void Nvr_ISAPI_login_parol_faqat_Digest_ga_beriladi()
    {
        var baseUri = new Uri("http://192.168.1.20:80");
        var creds = NvrArchiveService.IsapiCredentials(baseUri, "admin", "Secr3t");
        var target = new Uri("http://192.168.1.20:80/ISAPI/ContentMgmt/search");

        Assert.Null(creds.GetCredential(target, "Basic"));
        Assert.Null(creds.GetCredential(target, "NTLM"));
        var digest = creds.GetCredential(target, "Digest");
        Assert.NotNull(digest);
        Assert.Equal("admin", digest!.UserName);
        // Boshqa hostga hech qanday sxema bilan berilmaydi.
        Assert.Null(creds.GetCredential(new Uri("http://evil.example/ISAPI"), "Digest"));

        var src = Source("WunderkindLC.Application", "Services", "NvrArchiveService.cs");
        Assert.Contains("Credentials = IsapiCredentials(baseUri, AppSecrets.NvrUsername, AppSecrets.NvrPassword)", src);
        Assert.DoesNotContain("Credentials = new NetworkCredential(", src);
    }

    /* =========================================================================================
     *  QO'SHIMCHA (2026-10-08) — shartnoma fayli, o'quvchi hujjatlari, NVR manzili
     * ========================================================================================= */

    [Theory]
    [InlineData(ServerPdf, true)]
    [InlineData("/uploads/" + GuidHex + ".jpg", true)]
    [InlineData("/uploads/" + GuidHex + ".docx", true)]
    [InlineData("/uploads/" + GuidHex + ".svg", false)]        // ruxsat etilmagan kengaytma
    [InlineData("/uploads/" + GuidHex + ".JPG", false)]
    [InlineData("/uploads/" + GuidHex, false)]
    [InlineData("/uploads/passport.pdf", false)]
    [InlineData("/uploads/cv-" + GuidHex + ".pdf", false)]
    [InlineData("/uploads/face/" + GuidHex + ".jpg", false)]
    [InlineData("/uploads/../" + GuidHex + ".pdf", false)]
    [InlineData(null, false)]
    public void IsServerUpload_umumiy_yuklash_nomi(string? url, bool expected)
    {
        Assert.Equal(expected, UploadUrlRules.IsServerUpload(url));
    }

    [Fact]
    public void IsServerUpload_kengaytma_bilan()
    {
        Assert.True(UploadUrlRules.IsServerUpload("/uploads/" + GuidHex + ".docx", ".docx"));
        Assert.False(UploadUrlRules.IsServerUpload(ServerPdf, ".docx"));
    }

    [Fact]
    public async Task InUseAsync_boshqa_yozuvlarda_ishlatilgan_manzillarni_topadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        const string contract = "/uploads/11111111111111111111111111111111.pdf";
        const string lesson = "/uploads/22222222222222222222222222222222.pdf";
        const string photo = "/uploads/33333333333333333333333333333333.jpg";
        const string free = "/uploads/44444444444444444444444444444444.pdf";
        var me = new Student { FullName = "Men", ParentPassportUrl = free };
        ctx.Students.Add(me);
        ctx.Contracts.Add(new Contract { Number = 1, PdfUrl = contract });
        ctx.CourseItems.Add(new CourseItem { Text = "Dars", PdfUrl = lesson });
        ctx.Teachers.Add(new Teacher { FullName = "O'qituvchi", PhotoUrl = photo });
        await ctx.SaveChangesAsync();

        var used = await UploadReferences.InUseAsync(ctx, [contract, lesson, photo, free, null, ""], exceptStudentId: me.Id);
        Assert.Equal(new[] { contract, lesson, photo }.OrderBy(x => x), used.OrderBy(x => x));

        // O'quvchining O'Z yozuvi hisobga olinmasa ham, boshqa o'quvchi ishlatsa — band.
        Assert.Contains(free, await UploadReferences.InUseAsync(ctx, [free]));
    }

    [Fact]
    public async Task Oquvchini_ochirishda_hujjat_faqat_server_nomi_va_begona_ishlatmasa_ochiriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        const string contractPdf = "/uploads/55555555555555555555555555555555.pdf";
        const string ownPhoto = "/uploads/66666666666666666666666666666666.jpg";
        ctx.Contracts.Add(new Contract { Number = 9, PdfUrl = contractPdf });

        var a = new Student { FullName = "A", BirthCertificateUrl = ownPhoto, ParentPassportUrl = contractPdf };
        var b = new Student { FullName = "B", BirthCertificateUrl = "/uploads/../.env", ParentPassportUrl = "/uploads/face/x.jpg" };
        ctx.Students.AddRange(a, b);
        await ctx.SaveChangesAsync();

        var ra = await StudentPurge.PurgeAsync(ctx, a.Id);
        Assert.Contains(ownPhoto, ra!.FileUrls);
        Assert.DoesNotContain(contractPdf, ra.FileUrls);        // shartnoma ishlatadi
        await ctx.SaveChangesAsync();

        var rb = await StudentPurge.PurgeAsync(ctx, b.Id);
        Assert.DoesNotContain("/uploads/../.env", rb!.FileUrls);
        Assert.DoesNotContain("/uploads/face/x.jpg", rb.FileUrls);
    }

    [Fact]
    public void Shartnoma_PDF_faqat_server_nomi_va_fayl_saqlangandan_keyin_faqat_oziniki_ochadi()
    {
        var src = Source("WunderkindLC.Server", "Controllers", "ContractsController.cs");
        var upload = Body(src, "public async Task<ActionResult<ContractDocDto>> UploadPdf(");
        Assert.Contains("!UploadUrlRules.IsServerPdf(url)", upload);
        Assert.True(upload.IndexOf("SaveChangesAsync", StringComparison.Ordinal)
                    < upload.IndexOf("DeleteOwnFilesAsync(oldPdf)", StringComparison.Ordinal));

        var del = Body(src, "public async Task<IActionResult> DeleteDoc(");
        Assert.True(del.IndexOf("SaveChangesAsync", StringComparison.Ordinal)
                    < del.IndexOf("DeleteOwnFilesAsync(c.PdfUrl, c.DocxUrl)", StringComparison.Ordinal));
        Assert.DoesNotContain("contracts.DeleteUpload(c.", src);
        Assert.Contains("UploadReferences.InUseAsync(db, candidates)", src);
    }

    [Theory]
    [InlineData("192.168.1.20", 554, 80, "192.168.1.20", 554, 80, false)]
    [InlineData("192.168.1.20", 0, 0, "192.168.1.20", 554, 80, false)]      // eski qator: 0 = standart
    [InlineData("NVR.local", 554, 80, "nvr.local", 554, 80, false)]          // host registrsiz
    [InlineData("192.168.1.20", 554, 80, "evil.example", 554, 80, true)]
    [InlineData("192.168.1.20", 554, 80, "192.168.1.20", 8554, 80, true)]
    [InlineData("192.168.1.20", 554, 80, "192.168.1.20", 554, 8080, true)]
    [InlineData("", 0, 0, "", 554, 80, false)]
    public void Nvr_manzili_ozgardimi(string host, int rtsp, int isapi, string nHost, int nRtsp, int nIsapi, bool expected)
    {
        var m = new CenterMeta { NvrHost = host, NvrRtspPort = rtsp, NvrIsapiPort = isapi };
        Assert.Equal(expected, NvrArchiveService.TargetChanged(m, nHost, nRtsp, nIsapi));
    }

    [Fact]
    public void Nvr_manzili_faqat_superadmin_HARD_rol_va_ozgarishdan_OLDIN()
    {
        var src = Source("WunderkindLC.Server", "Controllers", "SettingsController.cs");
        var body = Body(src, "public async Task<ActionResult<CameraSettingsDto>> SaveCameras(");
        Assert.Contains("!User.IsInRole(Roles.SuperAdmin)", body);
        Assert.Contains("NvrArchiveService.TargetChanged(m, newHost, req.NvrRtspPort, req.NvrIsapiPort)", body);
        Assert.Contains("NvrTargetSuperAdminMessage", body);
        Assert.True(body.IndexOf("TargetChanged", StringComparison.Ordinal)
                    < body.IndexOf("m.CameraEnabled = ", StringComparison.Ordinal));
    }

    /* ---------- manba matni yordamchilari ---------- */

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

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot, .. parts]));

    /// <summary>Metod tanasi — imzodan keyingi <c>[Http</c> atributigacha (yoki fayl oxirigacha).</summary>
    private static string Body(string src, string signature)
    {
        var i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i > 0, $"Topilmadi: {signature}");
        var next = src.IndexOf("[Http", i, StringComparison.Ordinal);
        return next > 0 ? src[i..next] : src[i..];
    }
}
