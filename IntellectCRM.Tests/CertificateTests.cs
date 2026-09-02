using System.Text;
using System.Text.RegularExpressions;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// SERTIFIKATLAR — <see cref="CertificateService"/>: hash, sertifikat raqami, andoza
/// o'rinbosarlari, berish (idempotentlik), yuklab olish va TEKSHIRISH (verifikatsiya).
///
/// <para>Verifikatsiya — ommaviy (anonim) yo'l: <c>GET /api/public/certificates/{id}/verify</c>
/// → <c>VerifyCertificateAsync</c>. Ya'ni bu metodga IXTIYORIY (mavjud bo'lmagan) id kelishi
/// mumkin va u yiqilmasligi kerak.</para>
/// </summary>
public class CertificateTests : IDisposable
{
    // =============================================================================================
    //  Test muhiti: vaqtinchalik ContentRoot (sertifikatlar shu yerga yoziladi)
    // =============================================================================================

    /// <summary>Minimal soxta <see cref="IHostEnvironment"/> — tashqi mock kutubxonasisiz.</summary>
    private sealed class FakeEnv(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "IntellectCRM.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "intellect-cert-tests", Guid.NewGuid().ToString("N"));

    public CertificateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* test tozalash xatosi natijaga ta'sir qilmasin */ }
    }

    private string CertsDir => Path.Combine(_root, "uploads", "certificates");

    private CertificateService Service(TestDb db) => new(db.Context, new FakeEnv(_root));

    /// <summary>Haqiqiy andoza yo'liga (wwwroot/templates/certificate-template.html) fayl qo'yadi.</summary>
    private void WriteTemplate(string html)
    {
        var dir = Path.Combine(_root, "templates");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "certificate-template.html"), html, Encoding.UTF8);
    }

    private static (Student Student, Subject Course) Seed(TestDb db, string studentName = "Ali Valiyev")
    {
        var student = new Student { FullName = studentName };
        var course = new Subject { Name = "Ingliz tili A1", Price = 500000 };
        db.Context.Students.Add(student);
        db.Context.Subjects.Add(course);
        db.Context.SaveChanges();
        return (student, course);
    }

    // =============================================================================================
    //  1) SOF MANTIQ — SHA-256
    // =============================================================================================

    [Fact]
    public void ComputeSHA256_MalumVektor_BoshQator()
    {
        // Standart SHA-256("") — hash algoritmi almashtirilsa darhol ushlanadi.
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            CertificateService.ComputeSHA256(Array.Empty<byte>()));
    }

    [Fact]
    public void ComputeSHA256_MalumVektor_abc()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            CertificateService.ComputeSHA256(Encoding.UTF8.GetBytes("abc")));
    }

    [Fact]
    public void ComputeSHA256_KichikHarfliHex_64Belgi()
    {
        var hash = CertificateService.ComputeSHA256(Encoding.UTF8.GetBytes("Sertifikat"));

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeSHA256_BirBaytOzgarsa_HashOzgaradi()
    {
        var a = CertificateService.ComputeSHA256(Encoding.UTF8.GetBytes("Ali Valiyev"));
        var b = CertificateService.ComputeSHA256(Encoding.UTF8.GetBytes("Ali Valiyev "));

        Assert.NotEqual(a, b);
    }

    // =============================================================================================
    //  2) SOF MANTIQ — sertifikat raqami
    // =============================================================================================

    [Fact]
    public void GenerateCertificateNumber_Format_CERT_sana_4Raqam()
    {
        var num = CertificateService.GenerateCertificateNumber();

        Assert.Matches(@"^CERT-\d{4}-\d{2}-\d{2}-\d{4}$", num);
        // Sana qismi — markaz mintaqasidagi BUGUN (mutlaq sana yozilmaydi).
        Assert.StartsWith($"CERT-{AppClock.Today:yyyy-MM-dd}-", num);
    }

    [Fact]
    public void GenerateCertificateNumber_FaylNomiUchunXavfsiz()
    {
        // Raqam to'g'ridan-to'g'ri fayl nomiga ketadi ({num}.html) — yo'l ajratgichlari bo'lmasin.
        var num = CertificateService.GenerateCertificateNumber();

        Assert.DoesNotContain("/", num);
        Assert.DoesNotContain("\\", num);
        Assert.DoesNotContain("..", num);
    }

    [Fact]
    public void GenerateCertificateNumber_KetmaKetChaqiruvlar_NOYOB_KUTILGAN()
    {
        // Raqam ayni paytda FAYL NOMI ham — takrorlansa ikkinchi sertifikat birinchisining
        // faylini qayta yozib, uning hash'ini buzadi. Shu sabab ketma-ket chaqiruvlar noyob.
        var nums = Enumerable.Range(0, 50)
            .Select(_ => CertificateService.GenerateCertificateNumber())
            .ToList();

        Assert.Equal(nums.Count, nums.Distinct().Count());
    }

    // =============================================================================================
    //  3) SOF MANTIQ — HTML ekranlash
    // =============================================================================================

    [Fact]
    public void HtmlEncode_MaxsusBelgilar()
    {
        Assert.Equal("&amp;lt;", CertificateService.HtmlEncode("&lt;"));   // & AVVAL almashadi (ikki marta emas)
        Assert.Equal("&lt;script&gt;", CertificateService.HtmlEncode("<script>"));
        Assert.Equal("a&quot;b", CertificateService.HtmlEncode("a\"b"));
    }

    [Fact]
    public void HtmlEncode_OddiyMatnniOzgartirmaydi()
    {
        Assert.Equal("Ali Valiyev", CertificateService.HtmlEncode("Ali Valiyev"));
        Assert.Equal("", CertificateService.HtmlEncode(""));
    }

    [Fact]
    public void HtmlEncode_ApostrofniHAM_EKRANLAYDI()
    {
        // Ilgari apostrof EKRANLANMAS edi va bu ochiq teshik hisoblanardi: andoza atributlari
        // ikki tirnoqli bo'lgani uchungina xavfsiz edi, bitta tirnoqli atribut (style='...')
        // qo'shilsa darhol XSS'ga aylanardi. Endi encoder QAT'IY (`HtmlEncoder.Default`) —
        // apostrof ham ekranlanadi, ya'ni token qaysi kontekstga tushishidan qat'i nazar xavfsiz.
        Assert.Equal("G&#x27;ulomov", CertificateService.HtmlEncode("G'ulomov"));
    }

    [Fact]
    public void HtmlEncode_NonASCII_ni_ham_ekranlaydi_LEKIN_MATN_YOQOLMAYDI()
    {
        // Qat'iy encoder o'zbek/kirill harflarini ham raqamli havolaga aylantiradi. Bu ATAYIN
        // (kontekstga chidamli bo'lsin), lekin MA'NO yo'qolmasligi shart — brauzer uni asl
        // ko'rinishida chizadi. Shuning uchun aniq kodlarni emas, TESKARI o'girishni qulflaymiz.
        foreach (var asl in new[] { "Gʻulomov", "Тошматов", "Ўқитувчи" })
        {
            var kodlangan = CertificateService.HtmlEncode(asl);
            Assert.DoesNotContain(asl, kodlangan);                       // haqiqatan ekranlangan
            Assert.Equal(asl, System.Net.WebUtility.HtmlDecode(kodlangan)); // ma'no yo'qolmagan
        }
    }

    // =============================================================================================
    //  4) SOF MANTIQ — RenderTemplate (andoza o'rinbosarlari)
    // =============================================================================================

    private const string TokenTemplate =
        "<h1>{{student_name}}</h1><p>{{course_name}}</p><p>{{teacher_name}}</p>"
        + "<span>{{issue_date}}</span><span>{{expires_date}}</span><i>{{certificate_number}}</i>";

    [Fact]
    public void RenderTemplate_AsosiyTokenlarniAlmashtiradi()
    {
        var html = CertificateService.RenderTemplate(
            TokenTemplate, "Ali Valiyev", "Ingliz tili A1",
            new DateTime(2026, 3, 9), new DateTime(2027, 3, 9), "CERT-2026-03-09-0001");

        Assert.Contains("Ali Valiyev", html);
        Assert.Contains("Ingliz tili A1", html);
        Assert.Contains("09.03.2026", html);
        Assert.Contains("09.03.2027", html);
        Assert.Contains("CERT-2026-03-09-0001", html);
        Assert.DoesNotContain("{{student_name}}", html);
        Assert.DoesNotContain("{{expires_date}}", html);
    }

    [Fact]
    public void RenderTemplate_MuddatsizSertifikat()
    {
        var html = CertificateService.RenderTemplate(
            TokenTemplate, "Ali", "Kurs", new DateTime(2026, 1, 1), null, "CERT-1");

        Assert.Contains("Muddatsiz", html);
    }

    [Fact]
    public void RenderTemplate_SanaFormati_ddMMyyyy()
    {
        var html = CertificateService.RenderTemplate(
            TokenTemplate, "Ali", "Kurs", new DateTime(2026, 12, 5), null, "CERT-1");

        Assert.Contains("05.12.2026", html);
    }

    [Fact]
    public void RenderTemplate_IsmniHtmlEkranlaydi()
    {
        var html = CertificateService.RenderTemplate(
            TokenTemplate, "<script>alert(1)</script>", "Kurs", AppClock.Now, null, "CERT-1");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void RenderTemplate_HOZIRGI_XULQ_OqituvchiTokeniQoladi()
    {
        // HOZIRGI xulq: RenderTemplate {{teacher_name}} ni BILMAYDI.
        var html = CertificateService.RenderTemplate(
            TokenTemplate, "Ali", "Kurs", AppClock.Now, null, "CERT-1");

        Assert.Contains("{{teacher_name}}", html);
    }

    [Fact(Skip = "XATO (CertificateService.cs:246-264 RenderTemplate): ommaviy RenderTemplate "
                 + "{{teacher_name}} ni almashtirmaydi, GenerateHtmlCertificate (208-224) esa "
                 + "{{expires_date}} ni almashtirmaydi — bitta andoza uchun IKKI XIL token to'plami. "
                 + "Haqiqiy andoza (Server/wwwroot/templates/certificate-template.html) {{teacher_name}} "
                 + "ishlatadi, ya'ni RenderTemplate orqali render qilinsa sertifikatda literal "
                 + "'{{teacher_name}}' bosilib chiqadi. Tuzatish: bitta ReplaceTokens(...) yordamchisi "
                 + "qilinib, ikkala yo'l ham AYNAN bir xil to'plamni (student/course/teacher/issue/"
                 + "expires/number/verify) almashtirsin.")]
    public void RenderTemplate_BarchaTokenlarniAlmashtirishiKerak_KUTILGAN()
    {
        var html = CertificateService.RenderTemplate(
            TokenTemplate, "Ali", "Kurs", AppClock.Now, null, "CERT-1");

        Assert.DoesNotContain("{{", html);
    }

    // =============================================================================================
    //  5) SERTIFIKAT BERISH
    // =============================================================================================

    [Fact]
    public async Task Generate_FaylVaYozuvYaratadi()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);

        var cert = await Service(db).GenerateCertificateAsync(student.Id, course.Id);

        Assert.Equal(student.Id, cert.StudentId);
        Assert.Equal(course.Id, cert.CourseId);
        Assert.Equal("active", cert.Status);
        Assert.StartsWith("/uploads/certificates/", cert.FilePath);
        Assert.EndsWith(".html", cert.FileName);
        Assert.Equal(AppClock.Today, DateOnly.FromDateTime(cert.IssuedAt));
        Assert.True(File.Exists(Path.Combine(CertsDir, cert.FileName)), "Sertifikat fayli diskda yo'q");
        Assert.Equal(1, await db.Context.StudentCertificates.CountAsync());
    }

    [Fact]
    public async Task Generate_FileHash_DiskdagiFaylgaMosKelishiKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);

        var cert = await Service(db).GenerateCertificateAsync(student.Id, course.Id);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(CertsDir, cert.FileName));

        // Fayl BOM'SIZ yoziladi va hash/FileSize AYNAN o'sha baytlardan olinadi.
        Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Equal(cert.FileHash, CertificateService.ComputeSHA256(bytes));
        Assert.Equal(bytes.LongLength, cert.FileSize);
    }

    [Fact]
    public async Task Verify_ESKI_BOMLI_Fayl_MIGRATSIYASIZ_HamMosKeladi()
    {
        // Eski (tuzatishdan oldingi) sertifikatlar diskda BOM bilan yotibdi, hash esa BOM'siz
        // baytlardan hisoblangan. Tekshirishda BOM olib tashlangani uchun ular ISHLAB ketaveradi —
        // ma'lumot migratsiyasi KERAK EMAS.
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);

        // Eski holatni taqlid qilamiz: o'sha faylni BOM bilan qayta yozamiz (hash o'zgarmaydi).
        var yol = Path.Combine(CertsDir, cert.FileName);
        var html = await File.ReadAllTextAsync(yol);
        await File.WriteAllTextAsync(yol, html, new UTF8Encoding(true));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF },
            (await File.ReadAllBytesAsync(yol)).Take(3).ToArray());

        var v = await svc.VerifyCertificateAsync(cert.Id);

        Assert.True(v.HashMatched);
        Assert.True(v.IsValid);
    }

    [Fact]
    public async Task Generate_ZaxiraAndoza_BarchaTokenlarToldiriladi()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db, "G'ulom Toshmatov");

        var cert = await Service(db).GenerateCertificateAsync(student.Id, course.Id);
        var html = await File.ReadAllTextAsync(Path.Combine(CertsDir, cert.FileName));

        // Ism HTML'ga EKRANLANGAN holda tushadi (apostrof → &#x27;) — brauzer uni "G'ulom
        // Toshmatov" qilib chizadi. Qarang: HtmlEncode_ApostrofniHAM_EKRANLAYDI.
        Assert.Contains("G&#x27;ulom Toshmatov", html);
        Assert.Contains("Ingliz tili A1", html);
        Assert.DoesNotContain("{{", html);   // andozada to'ldirilmagan token qolmasin
    }

    [Fact]
    public async Task Generate_HaqiqiyAndozadan_OqituvchiVaQrniToldiradi()
    {
        using var db = TestDb.Sqlite();
        WriteTemplate("<x>{{student_name}}|{{course_name}}|{{teacher_name}}|{{issue_date}}"
                      + "|{{certificate_number}}|{{verify_url}}|[QR_CODE_IMAGE]</x>");
        var (student, course) = Seed(db);

        var cert = await Service(db).GenerateCertificateAsync(student.Id, course.Id, teacherName: "Dilnoza Karimova");
        var html = await File.ReadAllTextAsync(Path.Combine(CertsDir, cert.FileName));

        Assert.Contains("Dilnoza Karimova", html);
        Assert.Contains($"verify-certificate/{cert.Id}", html);
        Assert.DoesNotContain("[QR_CODE_IMAGE]", html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task Generate_SanaOzbekchaFormatda()
    {
        using var db = TestDb.Sqlite();
        WriteTemplate("<x>{{issue_date}}</x>");
        var (student, course) = Seed(db);

        var cert = await Service(db).GenerateCertificateAsync(student.Id, course.Id);
        var html = await File.ReadAllTextAsync(Path.Combine(CertsDir, cert.FileName));

        // "9 mart 2026-yil" ko'rinishi (oy nomi o'zbekcha, mutlaq sana yozilmaydi).
        Assert.Matches(
            @"^<x>\d{1,2} (yanvar|fevral|mart|aprel|may|iyun|iyul|avgust|sentabr|oktabr|noyabr|dekabr) \d{4}-yil</x>$",
            html);
        Assert.Contains($"{AppClock.Today.Day} ", html);
        Assert.Contains($" {AppClock.Today.Year}-yil", html);
    }

    [Fact]
    public async Task Generate_OqituvchiBerilmasa_GuruhOqituvchisidanOlinadi()
    {
        using var db = TestDb.Sqlite();
        WriteTemplate("<x>{{teacher_name}}</x>");
        var ctx = db.Context;
        var (student, course) = Seed(db);
        var teacher = new Teacher { FullName = "Dilnoza Karimova" };
        ctx.Teachers.Add(teacher);
        ctx.Classes.Add(new IntellectCRM.Domain.Group { Name = "A1-1", CourseId = course.Id, TeacherId = teacher.Id });
        await ctx.SaveChangesAsync();

        var cert = await Service(db).GenerateCertificateAsync(student.Id, course.Id);
        var html = await File.ReadAllTextAsync(Path.Combine(CertsDir, cert.FileName));

        Assert.Contains("Dilnoza Karimova", html);
    }

    [Fact]
    public async Task Generate_OqituvchiTopilmasa_StandartMatn()
    {
        using var db = TestDb.Sqlite();
        WriteTemplate("<x>{{teacher_name}}</x>");
        var (student, course) = Seed(db);

        var cert = await Service(db).GenerateCertificateAsync(student.Id, course.Id);
        var html = await File.ReadAllTextAsync(Path.Combine(CertsDir, cert.FileName));

        Assert.Equal("<x>O&#x27;qituvchi</x>", html);
    }

    [Fact]
    public async Task Generate_ShuKunda_IDEMPOTENT_IkkinchiMartaYangiYozuvYaratmaydi()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);

        var first = await svc.GenerateCertificateAsync(student.Id, course.Id);
        var second = await svc.GenerateCertificateAsync(student.Id, course.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Context.StudentCertificates.CountAsync());
    }

    [Fact]
    public async Task Generate_OquvchiTopilmasa_Xato()
    {
        using var db = TestDb.Sqlite();
        var (_, course) = Seed(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).GenerateCertificateAsync("yoq-oquvchi", course.Id));
        Assert.Contains("O'quvchi topilmadi", ex.Message);
    }

    [Fact]
    public async Task Generate_KursTopilmasa_Xato()
    {
        using var db = TestDb.Sqlite();
        var (student, _) = Seed(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).GenerateCertificateAsync(student.Id, "yoq-kurs"));
        Assert.Contains("Kurs topilmadi", ex.Message);
    }

    [Fact]
    public async Task Generate_MuddatBerilsa_ExpiresAtSaqlanadi()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var muddat = AppClock.Now.AddYears(1);

        var cert = await Service(db).GenerateCertificateAsync(
            student.Id, course.Id, metadataJson: "{\"ball\":90}", expiresAt: muddat);

        Assert.NotNull(cert.ExpiresAt);
        Assert.Equal(muddat.Date, cert.ExpiresAt!.Value.Date);
        Assert.Equal("{\"ball\":90}", cert.Metadata);
    }

    [Fact]
    public async Task Generate_HOZIRGI_XULQ_BekorQilinganSertifikatniQaytaBerish_UNIKALINDEKSGA_URILADI()
    {
        // HOZIRGI (noto'g'ri) xulq — pastdagi Skip test tuzatilganda BU test o'chiriladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (student, course) = Seed(db);
        var svc = Service(db);

        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);
        cert.Status = "revoked";
        cert.RevokedAt = AppClock.Now;
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(
            () => svc.GenerateCertificateAsync(student.Id, course.Id));
    }

    [Fact(Skip = "XATO (CertificateService.cs:36-43 + AppDbContext.cs:449): idempotentlik filtri "
                 + "Status==\"active\" ni talab qiladi, unikal indeks esa (StudentId, CourseId, IssuedAt) "
                 + "— Status'siz. Natija: BEKOR QILINGAN (revoked) sertifikatni O'SHA KUNI qayta berish "
                 + "UNIQUE constraint bilan yiqiladi (DbUpdateException → 500), ustiga HTML fayl allaqachon "
                 + "diskka yozilib bo'lgani uchun yetim fayl ham qoladi. "
                 + "Tuzatish: yo indeksga Status qo'shilsin, yo IssuedAt o'rniga cert Id/raqami bilan "
                 + "unikallik qilinsin, yoki qayta berishda eski yozuv IssuedAt'i bilan to'qnashmasin.")]
    public async Task Generate_BekorQilinganSertifikat_ShuKuniQaytaBerilishiKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (student, course) = Seed(db);
        var svc = Service(db);

        var eski = await svc.GenerateCertificateAsync(student.Id, course.Id);
        eski.Status = "revoked";
        await ctx.SaveChangesAsync();

        var yangi = await svc.GenerateCertificateAsync(student.Id, course.Id);

        Assert.NotEqual(eski.Id, yangi.Id);
        Assert.Equal("active", yangi.Status);
        Assert.Equal(2, await ctx.StudentCertificates.CountAsync());
    }

    // =============================================================================================
    //  6) RO'YXAT VA YUKLAB OLISH
    // =============================================================================================

    [Fact]
    public async Task GetStudentCertificates_FaqatShuOquvchiniki()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (student, course) = Seed(db);
        var boshqa = new Student { FullName = "Boshqa Bola" };
        ctx.Students.Add(boshqa);
        await ctx.SaveChangesAsync();
        var svc = Service(db);
        await svc.GenerateCertificateAsync(student.Id, course.Id);
        await svc.GenerateCertificateAsync(boshqa.Id, course.Id);

        var list = await svc.GetStudentCertificatesAsync(student.Id);

        Assert.Single(list);
        Assert.Equal(student.Id, list[0].StudentId);
    }

    [Fact]
    public async Task GetStudentCertificates_YangiSertifikatBirinchi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (student, course) = Seed(db);
        // Sanalar NISBIY: eski = bugundan 30 kun oldin.
        ctx.StudentCertificates.AddRange(
            new StudentCertificate
            {
                StudentId = student.Id, CourseId = course.Id, FileName = "a.html",
                IssuedAt = AppClock.Now.Date.AddDays(-30),
            },
            new StudentCertificate
            {
                StudentId = student.Id, CourseId = course.Id, FileName = "b.html",
                IssuedAt = AppClock.Now.Date,
            });
        await ctx.SaveChangesAsync();

        var list = await Service(db).GetStudentCertificatesAsync(student.Id);

        Assert.Equal(new[] { "b.html", "a.html" }, list.Select(c => c.FileName).ToArray());
    }

    [Fact]
    public async Task Download_FaylniQaytaradi_VaHisoblagichniOshiradi()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);

        var (bytes, name, contentType) = await svc.DownloadCertificateAsync(student.Id, cert.Id);

        Assert.NotEmpty(bytes);
        Assert.Equal(cert.FileName, name);
        Assert.Equal("text/html", contentType);
        Assert.Equal(1, cert.DownloadCount);
        Assert.NotNull(cert.DownloadedAt);
    }

    [Fact]
    public async Task Download_BirinchiYuklashVaqti_KeyingiYuklashdaOZGARMAYDI()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);

        await svc.DownloadCertificateAsync(student.Id, cert.Id);
        var birinchi = cert.DownloadedAt;
        await svc.DownloadCertificateAsync(student.Id, cert.Id);

        Assert.Equal(2, cert.DownloadCount);
        Assert.Equal(birinchi, cert.DownloadedAt);
    }

    [Fact]
    public async Task Download_BoshqaOquvchi_RuxsatYoq()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (student, course) = Seed(db);
        var boshqa = new Student { FullName = "Begona" };
        ctx.Students.Add(boshqa);
        await ctx.SaveChangesAsync();
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DownloadCertificateAsync(boshqa.Id, cert.Id));
        Assert.Contains("ruxsat yo'q", ex.Message);
    }

    [Fact]
    public async Task Download_FaylOchirilgan_FileNotFound()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);
        File.Delete(Path.Combine(CertsDir, cert.FileName));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => svc.DownloadCertificateAsync(student.Id, cert.Id));
    }

    // =============================================================================================
    //  7) VERIFIKATSIYA
    // =============================================================================================

    [Fact]
    public async Task Verify_TogriSertifikat_HaqiqiyBolishiKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);

        var v = await svc.VerifyCertificateAsync(cert.Id, "1.2.3.4");

        Assert.True(v.HashMatched);
        Assert.True(v.IsValid);
        // Tekshiruv yozuvining O'ZI ham to'g'ri saqlanadi.
        Assert.Equal("1.2.3.4", v.VerifiedFrom);
        Assert.Equal(cert.Id, v.StudentCertificateId);
        Assert.Equal(1, await db.Context.CertificateVerifications.CountAsync());
    }

    [Fact]
    public async Task Verify_FaylOzgartirilgan_HashMosKelmaydi()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);
        await File.AppendAllTextAsync(Path.Combine(CertsDir, cert.FileName), "<!-- soxta -->");

        var v = await svc.VerifyCertificateAsync(cert.Id);

        Assert.False(v.HashMatched);
        Assert.False(v.IsValid);
    }

    [Fact]
    public async Task Verify_FaylUmumanYoq_HaqiqiyEmas()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);
        File.Delete(Path.Combine(CertsDir, cert.FileName));

        var v = await svc.VerifyCertificateAsync(cert.Id);

        Assert.False(v.HashMatched);
        Assert.False(v.IsValid);
    }

    [Fact]
    public async Task Verify_BekorQilingan_HashTogri_LekinHaqiqiyEmas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);
        cert.Status = "revoked";
        cert.RevokedAt = AppClock.Now;
        cert.RevokeReason = "Xato berilgan";
        await ctx.SaveChangesAsync();

        var v = await svc.VerifyCertificateAsync(cert.Id);

        // Status != "active" — hash holatidan QAT'I NAZAR haqiqiy emas.
        Assert.False(v.IsValid);
        Assert.Equal("revoked", (await ctx.StudentCertificates.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Verify_MuddatiOtgan_HaqiqiyEmas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (student, course) = Seed(db);
        var svc = Service(db);
        // Muddat NISBIY: kechagi kun.
        var cert = await svc.GenerateCertificateAsync(
            student.Id, course.Id, expiresAt: AppClock.Now.AddDays(-1));

        var v = await svc.VerifyCertificateAsync(cert.Id);

        Assert.False(v.IsValid);
        Assert.NotNull(cert.ExpiresAt);
        Assert.True(cert.ExpiresAt!.Value < AppClock.Now);
        Assert.Equal(1, await ctx.CertificateVerifications.CountAsync());
    }

    [Fact]
    public async Task Verify_MuddatiHaliTugamagan_Haqiqiy_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(
            student.Id, course.Id, expiresAt: AppClock.Now.AddDays(1));

        Assert.True((await svc.VerifyCertificateAsync(cert.Id)).IsValid);
    }

    [Fact]
    public async Task Verify_HarTekshiruv_TarixgaYoziladi()
    {
        using var db = TestDb.Sqlite();
        var (student, course) = Seed(db);
        var svc = Service(db);
        var cert = await svc.GenerateCertificateAsync(student.Id, course.Id);

        await svc.VerifyCertificateAsync(cert.Id, "1.1.1.1");
        await svc.VerifyCertificateAsync(cert.Id, "2.2.2.2");

        var rows = await db.Context.CertificateVerifications.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1.1.1.1", "2.2.2.2" }, rows.Select(r => r.VerifiedFrom).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Verify_NoMalumId_IsValidFalseQaytarishiKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        Seed(db);

        var v = await Service(db).VerifyCertificateAsync("bunday-sertifikat-yoq", "9.9.9.9");

        Assert.False(v.IsValid);
        Assert.False(v.HashMatched);
        // Mavjud bo'lmagan sertifikat uchun tekshiruv jurnaliga yozilmaydi (majburiy FK bor).
        Assert.Equal(0, await db.Context.CertificateVerifications.CountAsync());
    }

    [Fact]
    public async Task Verify_BoshId_YiqilmasligiKerak()
    {
        using var db = TestDb.Sqlite();
        Seed(db);

        var v = await Service(db).VerifyCertificateAsync("");

        Assert.False(v.IsValid);
        Assert.Equal(0, await db.Context.CertificateVerifications.CountAsync());
    }

    // =============================================================================================
    //  8) SERTIFIKAT ID va TEKSHIRISH URL'i
    // =============================================================================================

    [Fact]
    public async Task Generate_Id_Guid32BelgiVaNoyob()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (student, course) = Seed(db);
        var boshqa = new Student { FullName = "Ikkinchi" };
        ctx.Students.Add(boshqa);
        await ctx.SaveChangesAsync();
        var svc = Service(db);

        var a = await svc.GenerateCertificateAsync(student.Id, course.Id);
        var b = await svc.GenerateCertificateAsync(boshqa.Id, course.Id);

        Assert.Matches("^[0-9a-f]{32}$", a.Id);   // Guid("N") — URL'ga xavfsiz, chiziqchasiz
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Generate_TekshirishUrli_SertifikatIdSiBilan()
    {
        using var db = TestDb.Sqlite();
        WriteTemplate("<x>{{verify_url}}</x>");
        var (student, course) = Seed(db);

        var cert = await Service(db).GenerateCertificateAsync(student.Id, course.Id);
        var html = await File.ReadAllTextAsync(Path.Combine(CertsDir, cert.FileName));

        Assert.Equal($"<x>https://crm.intellectschool.uz/verify-certificate/{cert.Id}</x>", html);
    }

    [Fact]
    public void SertifikatRaqami_RegexAndozasi_FaylNomigaAynanKochadi()
    {
        // GenerateCertificateAsync: safeName = certNumber.Replace("/","-").Replace("\\","-")
        var num = CertificateService.GenerateCertificateNumber();

        Assert.Equal(num, Regex.Replace(num, @"[/\\]", "-"));
    }
}
