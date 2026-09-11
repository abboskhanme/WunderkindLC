using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace WunderkindLC.Application.Services;

/// <summary>
/// TEST SERTIFIKATI — Word andozasini o'quvchi natijasi bilan to'ldirib PDF ga o'giradi.
///
/// <para>Oqim: <c>TestResult.CertificateEnabled</c> yoqilgan testda natijalar saqlanganda
/// (o'qituvchi/admin "Saqlash va sertifikat yaratish" bosadi) ball kiritilgan HAR bir o'quvchiga
/// bitta sertifikat yaratiladi. Andoza — <see cref="TestCertificateTemplate"/> (.docx), tokenlar
/// <see cref="DocxTemplate"/> sintaksisida (<c>@fish</c>, <c>@ball</c>, ...).</para>
///
/// <para><b>Idempotent:</b> kalit (test, o'quvchi) — qayta yaratilsa mavjud yozuv YANGILANADI va
/// eski fayllar o'chiriladi. Ya'ni "Saqlash"ni bir necha marta bosish nusxa yaratmaydi.</para>
///
/// <para><b>PDF bo'lmasa ham ishlaydi:</b> LibreOffice topilmasa <c>Status="docx"</c> bilan faqat
/// Word fayl saqlanadi — foydalanuvchi uni qo'lda PDF qiladi. Sertifikat yaratish TO'XTAMAYDI.</para>
/// </summary>
public class TestCertificateService(IHostEnvironment env, DocxToPdfConverter pdf)
{
    /// <summary>Faqat Word andozalari (boshqa format render qilinmaydi).</summary>
    public const string TemplateExtension = ".docx";

    public const string StatusReady = "ready";
    public const string StatusDocxOnly = "docx";

    /// <summary>
    /// ANDOZADA ISHLATILADIGAN O'ZGARUVCHILAR — <b>yagona manba</b>. Backend shu ro'yxat bo'yicha
    /// qiymat qo'yadi, admin paneli esa AYNAN shu ro'yxatni ko'rsatadi (ikkovi ajralib ketmasin).
    /// </summary>
    public static readonly IReadOnlyList<CertificateTokenDto> Tokens =
    [
        new("@fish", "O'quvchining F.I.Sh", "Valiyev Ali"),
        new("@guruh", "Guruh nomi", "Ingliz tili A1-2"),
        new("@kurs", "Kurs (fan) nomi", "Ingliz tili"),
        new("@oqituvchi", "Guruh o'qituvchisi", "Karimova Nodira"),
        new("@test", "Test nomi", "Unit 3 test"),
        new("@ball", "O'quvchi olgan ball", "85"),
        new("@maksball", "Maksimal ball", "100"),
        new("@foiz", "Foiz (%)", "85"),
        new("@orin", "Guruhdagi o'rni", "2"),
        new("@sana", "Test o'tkazilgan sana", "04.08.2026"),
        new("@bugun", "Sertifikat berilgan sana", "04.08.2026"),
        new("@raqam", "Sertifikat raqami", "SRT-2026-0042"),
        // Matn emas — shu joyga o'quvchining SURATI qo'yiladi (185×260 px).
        new("@rasm", "O'quvchining surati (shu joyga rasm qo'yiladi)",
            $"{DocxTemplate.PhotoWidthPx}×{DocxTemplate.PhotoHeightPx} px"),
    ];

    /// <summary>
    /// O'QUVCHI SURATI — matn tokeni EMAS. Sabab: <c>@rasm</c> deb yozilsa rasmning o'lchami va
    /// joylashuvini KOD taxmin qilishi kerak bo'lardi. Buning o'rniga shablon muallifi Word'ning
    /// o'zida rasm qo'yadi va uni xohlagancha sozlaydi — biz faqat MAZMUNINI almashtiramiz.
    /// </summary>
    public static readonly CertificatePhotoHelpDto PhotoHelp = new(
        "O'quvchining surati — ikki usul",
        [
            $"ODDIY YO'L: shablonda kerakli joyga {DocxTemplate.PhotoToken} deb yozing — surat "
            + $"{DocxTemplate.PhotoWidthPx}×{DocxTemplate.PhotoHeightPx} px o'lchamda qo'yiladi.",
            "O'Z O'LCHAMINGIZ KERAK BO'LSA: Word'da istalgan rasm qo'ying (Qo'yish → Rasm) — u "
            + "faqat O'RIN, mazmuni almashtiriladi.",
            "O'lchamini, ramkasini va joyini xohlagancha sozlang — sertifikatda AYNAN shunday chiqadi.",
            "Rasmni o'ng tugma bilan bosing → «Alt matn» (Edit Alt Text) → «rasm» deb yozing. "
            + "Shablonda boshqa rasm bo'lmasa (logotip ham), alt matn shart emas.",
        ],
        "Surat CHO'ZILMAYDI: katakni to'ldiradi, ortiqchasi markazdan qirqiladi (yuz o'rtada qoladi). "
        + $"O'quvchida surat bo'lmasa {DocxTemplate.PhotoToken} belgisi olib tashlanadi, "
        + "Word'dagi rasm o'rni esa o'z holicha qoladi.");

    // =============================================================================================
    //  ANDOZALAR
    // =============================================================================================

    public async Task<List<TestCertificateTemplateDto>> ListTemplatesAsync(
        IAppDbContext db, bool includeInactive = true, CancellationToken ct = default)
    {
        var q = db.TestCertificateTemplates.AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        var rows = await q.OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<(TestCertificateTemplateDto? Dto, string? Error)> CreateTemplateAsync(
        IAppDbContext db, TestCertificateTemplatePayload payload, string actor, CancellationToken ct = default)
    {
        var name = (payload.Name ?? "").Trim();
        if (name.Length == 0) return (null, "Shablon nomini kiriting");
        var fileUrl = (payload.FileUrl ?? "").Trim();
        if (!fileUrl.EndsWith(TemplateExtension, StringComparison.OrdinalIgnoreCase))
            return (null, "Faqat Word (.docx) fayl yuklanadi");
        var adopted = await AdoptTemplateFileAsync(fileUrl, ct);
        if (adopted is null) return (null, "Yuklangan fayl topilmadi");

        var row = new TestCertificateTemplate
        {
            Name = name,
            FileUrl = adopted,
            FileName = (payload.FileName ?? "").Trim(),
            IsActive = true,
            CreatedBy = actor,
        };
        db.TestCertificateTemplates.Add(row);
        // Birinchi shablon avtomatik STANDART bo'ladi — aks holda test formasida "shablon tanlanmagan"
        // holati paydo bo'lib, sertifikat jimgina yaratilmay qolardi.
        var isFirst = !await db.TestCertificateTemplates.AnyAsync(ct);
        if (payload.IsDefault || isFirst) await MakeDefaultAsync(db, row, ct);
        await db.SaveChangesAsync(ct);
        return (ToDto(row), null);
    }

    public async Task<(TestCertificateTemplateDto? Dto, string? Error)> UpdateTemplateAsync(
        IAppDbContext db, string id, TestCertificateTemplatePayload payload, CancellationToken ct = default)
    {
        var row = await db.TestCertificateTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return (null, "Shablon topilmadi");

        var name = (payload.Name ?? "").Trim();
        if (name.Length == 0) return (null, "Shablon nomini kiriting");
        row.Name = name;
        row.IsActive = payload.IsActive;

        // Fayl almashtirilsa — eskisi o'chiriladi (ombor shishmasin).
        var fileUrl = (payload.FileUrl ?? "").Trim();
        if (fileUrl.Length > 0 && fileUrl != row.FileUrl)
        {
            if (!fileUrl.EndsWith(TemplateExtension, StringComparison.OrdinalIgnoreCase))
                return (null, "Faqat Word (.docx) fayl yuklanadi");
            var adopted = await AdoptTemplateFileAsync(fileUrl, ct);
            if (adopted is null) return (null, "Yuklangan fayl topilmadi");
            DeleteTemplateFile(row.FileUrl);
            row.FileUrl = adopted;
            row.FileName = (payload.FileName ?? "").Trim();
        }

        if (payload.IsDefault) await MakeDefaultAsync(db, row, ct);
        // Nofaol shablon standart bo'lib qolmasin.
        else if (!row.IsActive) row.IsDefault = false;

        await db.SaveChangesAsync(ct);
        return (ToDto(row), null);
    }

    public async Task<string?> DeleteTemplateAsync(IAppDbContext db, string id, CancellationToken ct = default)
    {
        var row = await db.TestCertificateTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return "Shablon topilmadi";
        // Shablondan sertifikat berilgan bo'lsa — o'chirmaymiz (tarix buzilmasin), nofaol qilinadi.
        if (await db.TestCertificates.AnyAsync(c => c.TemplateId == id, ct))
            return "Bu shablon bo'yicha sertifikatlar berilgan — o'chirib bo'lmaydi. Uni \"nofaol\" qiling.";

        DeleteTemplateFile(row.FileUrl);
        db.TestCertificateTemplates.Remove(row);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Shu shablonni standart qiladi, qolganlaridan belgini oladi (bitta standart bo'lsin).</summary>
    private static async Task MakeDefaultAsync(IAppDbContext db, TestCertificateTemplate row, CancellationToken ct)
    {
        var others = await db.TestCertificateTemplates.Where(t => t.IsDefault).ToListAsync(ct);
        foreach (var o in others) o.IsDefault = false;
        row.IsDefault = true;
        row.IsActive = true;
    }

    // =============================================================================================
    //  SERTIFIKAT YARATISH
    // =============================================================================================

    /// <summary>
    /// BO'LAK O'LCHAMI — bitta LibreOffice chaqiruvida nechta sertifikat chizilishi.
    ///
    /// <para>LibreOffice narxi FAYLGA emas, <b>jarayonni ochishga</b>: sovuq start ~2-4 s va
    /// ~150-200 MB, bitta hujjatni chizish esa ~0.5-1 s. Uchta yo'l bor edi:</para>
    /// <list type="bullet">
    ///   <item>hammasi BITTA chaqiruvda — jami eng tez (start bir marta), lekin xotira cho'qqisi
    ///   eng baland va natija faqat oxirida ko'rinadi;</item>
    ///   <item>bittalab — xotira eng past, lekin start har safar to'lanadi (30 kishida ~110 s);</item>
    ///   <item><b>bo'laklab (tanlangan yo'l)</b> — 30 kishida start 6 marta (~40 s), xotira cho'qqisi
    ///   ~2 barobar past va har bo'lakdan keyin yozuvlar bazaga tushgani uchun foydalanuvchi tayyor
    ///   sertifikatlarni DARHOL yuklab ola boshlaydi.</item>
    /// </list>
    /// 1 GB RAM li serverda xotira cho'qqisi jami vaqtdan muhimroq — shu sabab bo'laklab ishlanadi.
    /// </summary>
    public const int ChunkSize = 5;

    /// <summary>
    /// SERTIFIKAT RAQAMI NAVBATI — bir vaqtda faqat BITTA bo'lak raqam oladi va saqlaydi.
    /// Raqam bazadagi eng kattasidan keyingisi bo'lgani uchun, qulfsiz holda ikkita parallel
    /// generatsiya bir xil raqamni ikki xil o'quvchiga berib yuborardi.
    /// </summary>
    private static readonly SemaphoreSlim NumberGate = new(1, 1);

    /// <summary>Bitta o'quvchi haqida sertifikat uchun kerak bo'ladigan ma'lumot.</summary>
    private sealed record StudentInfo(string FullName, string? PhotoUrl);

    /// <summary>
    /// Generatsiya REJASI — tekshiruvlar o'tgan va hamma ma'lumot yig'ilgan holat.
    /// Bu yerda hali BIRORTA Word fayl to'ldirilmaydi (ya'ni arzon): shuning uchun uni so'rov ichida
    /// chaqirib, "nechta sertifikat chiqadi va xato bormi" degan savolga darhol javob berish mumkin.
    /// </summary>
    private sealed record Plan(
        TestResult Test, TestCertificateTemplate Template, byte[] TemplateBytes,
        string GroupName, string CourseName, string TeacherName,
        List<TestScore> Eligible, Dictionary<string, StudentInfo> Students,
        Dictionary<string, int> RankOf, List<TestCertificate> Existing,
        HashSet<string> ScoredIds);

    /// <summary>Tekshiruvlar + ma'lumot yig'ish. Word fayllar TO'LDIRILMAYDI (qarang: <see cref="Plan"/>).</summary>
    /// <param name="needTemplateBytes">Faqat "nechta chiqadi" so'ralganda <c>false</c> — shablon fayli
    /// bir necha MB bo'lishi mumkin, uni bekorga o'qib tashlash shart emas.</param>
    private async Task<(Plan? Plan, string? Error)> PrepareAsync(
        IAppDbContext db, string testId, CancellationToken ct, bool needTemplateBytes = true)
    {
        var test = await db.TestResults.FirstOrDefaultAsync(t => t.Id == testId, ct);
        if (test is null) return (null, "Test topilmadi");
        if (!test.CertificateEnabled)
            return (null, "Bu testda sertifikat berish yoqilmagan (test sozlamasidagi ptichkani belgilang)");

        var template = await ResolveTemplateAsync(db, test.CertificateTemplateId, ct);
        if (template is null)
            return (null, "Sertifikat shabloni topilmadi. \"Testlar natijalari → Sertifikat shablonlari\" bo'limida Word shablon yuklang.");
        var templatePath = ResolveUpload(template.FileUrl);
        if (templatePath is null)
            return (null, $"\"{template.Name}\" shablonining fayli topilmadi (qayta yuklang)");
        var templateBytes = needTemplateBytes ? await File.ReadAllBytesAsync(templatePath, ct) : [];

        var group = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == test.GroupId, ct);
        var courseName = group is null || string.IsNullOrEmpty(group.CourseId)
            ? ""
            : (await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == group.CourseId, ct))?.Name ?? "";
        var teacherName = group is null || string.IsNullOrEmpty(group.TeacherId)
            ? ""
            : (await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == group.TeacherId, ct))?.FullName ?? "";

        // Ball kiritilganlar (oflayn qo'lda ham, onlayn botdan ham — ikkalasi bir jadvalda).
        var scores = await db.TestScores.AsNoTracking()
            .Where(s => s.TestResultId == testId)
            .ToListAsync(ct);
        if (scores.Count == 0) return (null, "Hali birorta ball kiritilmagan");

        var studentIds = scores.Select(s => s.StudentId).Distinct().ToList();
        // Surat ham olinadi: andozada rasm o'rni bo'lsa uning ichiga qo'yiladi.
        // (`BirthCertificateUrl` — nomi eski, aslida o'quvchi rasmi; qarang: Entities.cs)
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, PhotoUrl = s.BirthCertificateUrl })
            .ToDictionaryAsync(s => s.Id, s => new StudentInfo(s.FullName, s.PhotoUrl), ct);

        // O'RIN — test tafsilotidagi bilan bir xil qoida (teng ball = teng o'rin, keyingisi tashlab ketiladi).
        // DIQQAT: o'rin BARCHA ballar bo'yicha sanaladi — o'chirilgan o'quvchi ham qatorda turadi,
        // aks holda sertifikat o'rni test tafsilotidagidan farq qilardi.
        var ordered = scores.OrderByDescending(s => s.Score).ToList();
        var rankOf = new Dictionary<string, int>();
        decimal? prev = null;
        var rank = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (prev is null || ordered[i].Score != prev) rank = i + 1;
            rankOf[ordered[i].StudentId] = rank;
            prev = ordered[i].Score;
        }

        // O'chirilgan (yoki topilmagan) o'quvchiga sertifikat berilmaydi — ism bo'lmasa nima yozamiz.
        var eligible = ordered
            .Where(s => (students.GetValueOrDefault(s.StudentId)?.FullName ?? "").Length > 0)
            .ToList();
        if (eligible.Count == 0) return (null, "Hali birorta ball kiritilmagan");

        var existing = await db.TestCertificates.Where(c => c.TestResultId == testId).ToListAsync(ct);

        return (new Plan(test, template, templateBytes, group?.Name ?? "", courseName, teacherName,
            eligible, students, rankOf, existing,
            ordered.Select(s => s.StudentId).ToHashSet()), null);
    }

    /// <summary>
    /// Nechta sertifikat chiqishini OLDINDAN aytadi (hech narsa yaratmaydi) — fon ishini boshlashdan
    /// oldin tekshirish uchun: xato bo'lsa foydalanuvchi darhol ko'radi, UI esa "0/N" ni biladi.
    /// </summary>
    public async Task<(int Total, string? Error)> ExpectedCountAsync(
        IAppDbContext db, string testId, CancellationToken ct = default)
    {
        var (plan, error) = await PrepareAsync(db, testId, ct, needTemplateBytes: false);
        return plan is null ? (0, error) : (plan.Eligible.Count, null);
    }

    /// <summary>
    /// Test bo'yicha BALL KIRITILGAN barcha o'quvchiga sertifikat yaratadi (mavjudi yangilanadi).
    ///
    /// <para><b>BO'LAKLAB ishlaydi</b> (<see cref="ChunkSize"/>): har bo'lak alohida LibreOffice
    /// chaqiruvida chiziladi va darhol bazaga yoziladi. Ya'ni ish tugashini kutmasdan ham tayyor
    /// sertifikatlar ro'yxatda ko'rinaveradi, xotirada esa bir vaqtda faqat bitta bo'lakning
    /// fayllari turadi.</para>
    /// </summary>
    /// <param name="onProgress">Har bo'lakdan keyin — jami nechtasi tayyor bo'lgani.</param>
    /// <returns>Yaratilgan sertifikatlar; xato bo'lsa <c>Error</c> to'ldiriladi va ro'yxat bo'sh.</returns>
    public async Task<(List<TestCertificateDto> Items, string? Error)> GenerateForTestAsync(
        IAppDbContext db, string testId, string actor, CancellationToken ct = default,
        Action<int>? onProgress = null)
    {
        var (plan, error) = await PrepareAsync(db, testId, ct);
        if (plan is null) return ([], error);

        var result = new List<TestCertificate>();
        var done = 0;

        foreach (var chunk in plan.Eligible.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();

            // RAQAM NAVBATI — butun bo'lak shu qulf ostida bajariladi.
            // Sabab: raqam (`SRT-yyyy-NNNN`) bazadagi ENG KATTA raqamdan keyingisi bo'lib beriladi.
            // Agar ikkita test bir vaqtda yaratilsa, ikkalasi ham bir xil "eng katta"ni ko'rib,
            // BIR XIL raqamli sertifikat chiqarardi. Qulf raqam berish → yozib qo'yish oralig'ini
            // bo'linmas qiladi. Ilova bitta nusxada ishlagani uchun bu yetarli.
            // Og'ir qism (LibreOffice) allaqachon o'z navbati bilan ketardi, shuning uchun bu
            // qulf sezilarli sekinlik qo'shmaydi.
            await NumberGate.WaitAsync(ct);
            try
            {
                result.AddRange(await ProcessChunkAsync(db, plan, chunk, testId, actor, ct));
            }
            finally
            {
                NumberGate.Release();
            }

            done += chunk.Length;
            onProgress?.Invoke(done);
        }

        // ---- ESKIRGANLARINI OLIB TASHLAYMIZ ----
        // Ssenariy: o'qituvchi noto'g'ri o'quvchiga ball qo'ydi → sertifikatlar yaratildi → ballni
        // TOZALADI → qayta yaratdi. Ilgari o'sha noto'g'ri sertifikat ro'yxatda ham, ZIP ichida ham
        // eski ball bilan qolib ketardi va uni o'chirishning yo'li yo'q edi.
        // Faqat BALLI O'CHIRILGANLAR olib tashlanadi (o'quvchi hali ham ro'yxatda bo'lsa —
        // ya'ni "qayta yaratish" amali natijani ballar bilan moslashtiradi).
        var obsolete = plan.Existing.Where(c => !plan.ScoredIds.Contains(c.StudentId)).ToList();
        if (obsolete.Count > 0)
        {
            foreach (var c in obsolete) db.TestCertificates.Remove(c);
            await db.SaveChangesAsync(ct);
            // Fayllar yozuv o'chgandan KEYIN (yuqoridagi bilan bir xil sabab: uzilishda baza
            // mavjud bo'lmagan faylga ishora qilib qolmasin).
            foreach (var c in obsolete) { DeleteCertFile(c.DocxUrl); DeleteCertFile(c.PdfUrl); }
        }

        return (result.Select(ToDto).ToList(), null);
    }

    /// <summary>
    /// BITTA BO'LAK: Word to'ldirish → PDF → fayl → yozuv → saqlash.
    /// <see cref="NumberGate"/> ostida chaqiriladi (raqam berish bo'linmas bo'lishi uchun).
    /// </summary>
    private async Task<List<TestCertificate>> ProcessChunkAsync(
        IAppDbContext db, Plan plan, TestScore[] chunk, string testId, string actor, CancellationToken ct)
    {
        // Seed HAR BO'LAKDA qaytadan o'qiladi: shu qulf ichida bazadagi eng katta raqam
        // haqiqatan ham eng oxirgisi bo'ladi.
        var seq = await NextNumberSeedAsync(db, ct);

        // ---- 1-BOSQICH: SHU BO'LAKNING Word fayllari (xotirada faqat shuncha turadi) ----
        var built = new List<(TestScore Score, string FullName, int Percent, string Number, byte[] Docx)>();
        foreach (var s in chunk)
        {
            var student = plan.Students.GetValueOrDefault(s.StudentId);
            var fullName = student?.FullName ?? "";
            var percent = plan.Test.MaxScore > 0
                ? (int)Math.Round(s.Score / plan.Test.MaxScore * 100m) : 0;
            var prevRow = plan.Existing.FirstOrDefault(c => c.StudentId == s.StudentId);
            var number = prevRow?.Number is { Length: > 0 } n
                ? n : $"SRT-{AppClock.Today:yyyy}-{seq++:D4}";

            var tokens = new Dictionary<string, string>
            {
                ["@fish"] = fullName,
                ["@guruh"] = plan.GroupName,
                ["@kurs"] = plan.CourseName,
                ["@oqituvchi"] = plan.TeacherName,
                ["@test"] = plan.Test.Name,
                ["@ball"] = Num(s.Score),
                ["@maksball"] = Num(plan.Test.MaxScore),
                ["@foiz"] = percent.ToString(),
                ["@orin"] = plan.RankOf.GetValueOrDefault(s.StudentId, 0).ToString(),
                ["@sana"] = FormatDate(plan.Test.Date),
                ["@bugun"] = AppClock.Today.ToString("dd.MM.yyyy"),
                ["@raqam"] = number,
            };

            var docx = DocxTemplate.Fill(plan.TemplateBytes, tokens);
            // O'quvchining surati: andozadagi `@rasm` belgisiga yoki Word'dagi rasm o'rniga.
            // HAR DOIM chaqiriladi — surat bo'lmasa ham, aks holda sertifikatda "@rasm" yozuvi
            // qolib ketardi (`Fill` noma'lum belgini ataylab tegmasdan qoldiradi).
            var photo = ReadPhoto(student?.PhotoUrl);
            docx = DocxTemplate.ApplyPhoto(docx, photo?.Bytes, photo?.Extension);

            built.Add((s, fullName, percent, number, docx));
        }

        // ---- 2-BOSQICH: bo'lak BITTA LibreOffice chaqiruvida PDF ga ----
        // Konvertor yo'q bo'lsa massiv null'lar bilan qaytadi — sertifikatlar .docx bo'lib saqlanadi.
        var pdfs = await pdf.ConvertManyAsync(built.Select(b => b.Docx).ToList(), ct);

        // ---- 3-BOSQICH: yangi fayllarni yozib, yozuvlarni yangilaymiz ----
        var rows = new List<TestCertificate>();
        var stale = new List<string>();     // eski fayllar — SaqlanGANDAN keyin o'chiriladi
        for (var i = 0; i < built.Count; i++)
        {
            var (s, fullName, percent, number, docx) = built[i];
            var pdfBytes = pdfs[i];
            var row = plan.Existing.FirstOrDefault(c => c.StudentId == s.StudentId);

            // Qayta yaratishda eski fayllar keraksiz bo'ladi (ombor shishmasin), lekin ular
            // HOZIR o'chirilmaydi: SaveChanges uzilib qolsa (disk to'ldi, deploy) bazadagi eski
            // manzil mavjud bo'lmagan faylga ishora qilib qolardi — «Yuklab olish» 404 berardi.
            if (row is not null) { stale.Add(row.DocxUrl); stale.Add(row.PdfUrl); }

            var baseName = $"cert-{Guid.NewGuid():N}";
            var docxUrl = await SaveCertFileAsync(baseName + ".docx", docx, ct);
            var pdfUrl = pdfBytes is null ? "" : await SaveCertFileAsync(baseName + ".pdf", pdfBytes, ct);

            if (row is null)
            {
                row = new TestCertificate { TestResultId = testId, StudentId = s.StudentId };
                db.TestCertificates.Add(row);
                plan.Existing.Add(row);
            }
            row.StudentName = fullName;
            row.TemplateId = plan.Template.Id;
            row.TemplateName = plan.Template.Name;
            row.Number = number;
            row.DocxUrl = docxUrl;
            row.PdfUrl = pdfUrl;
            row.Status = pdfBytes is null ? StatusDocxOnly : StatusReady;
            row.Score = s.Score;
            row.MaxScore = plan.Test.MaxScore;
            row.Percent = percent;
            row.IssuedAt = AppClock.Now;
            row.CreatedBy = actor;
            rows.Add(row);
        }

        // Har BO'LAKDAN keyin saqlanadi — UI shu daqiqada tayyor bo'lganlarni ko'ra oladi.
        await db.SaveChangesAsync(ct);
        // Endi yozuvlar yangi fayllarga ishora qiladi — eskilarini xavfsiz o'chirsa bo'ladi.
        foreach (var old in stale) DeleteCertFile(old);
        return rows;
    }

    /// <summary>Test bo'yicha berilgan sertifikatlar (o'rin bo'yicha emas — ball kamayishi bo'yicha).</summary>
    public static async Task<List<TestCertificateDto>> ListForTestAsync(
        IAppDbContext db, string testId, CancellationToken ct = default)
    {
        // Saralash XOTIRADA: SQLite (testlar) `decimal` bo'yicha ORDER BY ni qo'llamaydi,
        // qatorlar soni esa bitta testdagi o'quvchilar soni — kichik.
        var rows = await db.TestCertificates.AsNoTracking()
            .Where(c => c.TestResultId == testId)
            .ToListAsync(ct);
        return rows
            .OrderByDescending(c => c.Score).ThenBy(c => c.StudentName, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto).ToList();
    }

    /// <summary>
    /// Yuklab olish uchun fayl. <paramref name="preferPdf"/> — PDF bo'lsa PDF, aks holda .docx.
    /// Fayl nomi o'quvchi ismi bilan beriladi ("Sertifikat - Valiyev Ali.pdf").
    /// </summary>
    public async Task<(byte[] Bytes, string FileName, string ContentType)?> ReadFileAsync(
        IAppDbContext db, string certificateId, bool preferPdf = true, CancellationToken ct = default)
    {
        var row = await db.TestCertificates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == certificateId, ct);
        if (row is null) return null;

        var usePdf = preferPdf && row.PdfUrl.Length > 0;
        var url = usePdf ? row.PdfUrl : row.DocxUrl;
        var path = ResolveCertFile(url);
        if (path is null) return null;

        var safeName = string.Join("_", (row.StudentName.Length > 0 ? row.StudentName : row.Number)
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return (await File.ReadAllBytesAsync(path, ct),
            $"Sertifikat - {safeName}{(usePdf ? ".pdf" : ".docx")}",
            usePdf ? "application/pdf"
                   : "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    /// <summary>Test bo'yicha barcha sertifikatlar — bitta ZIP (PDF bo'lsa PDF, bo'lmasa .docx).</summary>
    public async Task<(byte[] Bytes, string FileName)?> ZipForTestAsync(
        IAppDbContext db, string testId, CancellationToken ct = default)
    {
        var rows = (await db.TestCertificates.AsNoTracking()
                .Where(c => c.TestResultId == testId).ToListAsync(ct))
            .OrderByDescending(c => c.Score).ToList();   // saralash xotirada (SQLite decimal cheklovi)
        if (rows.Count == 0) return null;

        var testName = (await db.TestResults.AsNoTracking()
            .Where(t => t.Id == testId).Select(t => t.Name).FirstOrDefaultAsync(ct)) ?? "test";

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                var usePdf = r.PdfUrl.Length > 0;
                var path = ResolveCertFile(usePdf ? r.PdfUrl : r.DocxUrl);
                if (path is null) continue;

                var safe = string.Join("_", (r.StudentName.Length > 0 ? r.StudentName : r.Number)
                    .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                var name = $"{safe}{(usePdf ? ".pdf" : ".docx")}";
                // Bir xil ismli ikki o'quvchi bo'lsa ZIP ichida nom to'qnashmasin.
                for (var i = 2; !used.Add(name); i++) name = $"{safe} ({i}){(usePdf ? ".pdf" : ".docx")}";

                var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Fastest);
                await using var es = entry.Open();
                await using var fs = File.OpenRead(path);
                await fs.CopyToAsync(es, ct);
            }
        }
        var zipName = string.Join("_", $"Sertifikatlar - {testName}"
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return (ms.ToArray(), zipName + ".zip");
    }

    // =============================================================================================
    //  Yordamchilar
    // =============================================================================================

    private static async Task<TestCertificateTemplate?> ResolveTemplateAsync(
        IAppDbContext db, string? templateId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            // `IsActive` SHART: admin shablonni "nofaol" qilgach (o'chirib bo'lmaydi — undan
            // sertifikat berilgan bo'lsa tarix saqlanadi) u endi ishlatilmasligi kerak. Aks holda
            // testda saqlanib qolgan eski id orqali nofaol andoza bilan sertifikat chiqaverardi.
            var chosen = await db.TestCertificateTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, ct);
            if (chosen is not null) return chosen;
        }
        // Tanlanmagan yoki o'chirilgan — standart, u ham bo'lmasa birinchi faol shablon.
        return await db.TestCertificateTemplates.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Joriy yildagi keyingi tartib raqami (SRT-yyyy-NNNN).
    ///
    /// <para>Saralash MATN bo'yicha emas, SON bo'yicha: matnda <c>"...-9999" &gt; "...-10000"</c>
    /// bo'lgani uchun bir yilda 10 000 dan oshsa hisob yana 10000 dan boshlanib, raqamlar
    /// takrorlanardi. Shuning uchun prefiksga mos raqamlar o'qilib, sonli maksimum olinadi
    /// (yiliga bir necha ming qator — arzon).</para>
    /// </summary>
    private static async Task<int> NextNumberSeedAsync(IAppDbContext db, CancellationToken ct)
    {
        var prefix = $"SRT-{AppClock.Today:yyyy}-";
        var numbers = await db.TestCertificates.AsNoTracking()
            .Where(c => c.Number.StartsWith(prefix))
            .Select(c => c.Number)
            .ToListAsync(ct);

        var max = 0;
        foreach (var s in numbers)
            if (int.TryParse(s[prefix.Length..], out var n) && n > max) max = n;
        return max + 1;
    }

    /// <summary>"1234.50" emas, "1234.5"/"1234" — sertifikatda ortiqcha nol chiqmasin.</summary>
    private static string Num(decimal v) =>
        v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.##");

    /// <summary>"2026-08-04" → "04.08.2026" (noto'g'ri format bo'lsa o'z holicha).</summary>
    private static string FormatDate(string? iso) =>
        DateTime.TryParse(iso, out var d) ? d.ToString("dd.MM.yyyy") : (iso ?? "");

    private static TestCertificateTemplateDto ToDto(TestCertificateTemplate t) =>
        new(t.Id, t.Name, t.FileUrl, t.FileName, t.IsDefault, t.IsActive,
            t.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"), t.CreatedBy);

    private static TestCertificateDto ToDto(TestCertificate c) =>
        new(c.Id, c.TestResultId, c.StudentId, c.StudentName, c.Number, c.TemplateName,
            c.DocxUrl, c.PdfUrl, c.Status, c.Score, c.MaxScore, c.Percent,
            c.IssuedAt.ToString("yyyy-MM-ddTHH:mm:ss"));

    // ---- Fayl yo'llari -------------------------------------------------------------------------
    // DIQQAT: `/uploads` Program.cs'da ContentRootPath/uploads dan beriladi (docker volume + zaxira).
    // Shuning uchun sertifikatlar ham SHU YERGA yoziladi — WebRootPath (wwwroot) ga EMAS: u Docker'da
    // har deployda qayta yoziladi va zaxiraga kirmaydi (eski HTML sertifikatlardagi xato shu edi).

    private string UploadsDir => Path.Combine(env.ContentRootPath, "uploads");
    private string CertificatesDir => Path.Combine(UploadsDir, "certificates");

    /// <summary>"/uploads/xxx.docx" → diskdagi yo'l. Manzildan FAQAT fayl nomi olinadi
    /// (papkadan chiqib ketish mumkin emas).</summary>
    private string? ResolveUpload(string? fileUrl)
    {
        var name = Path.GetFileName(fileUrl ?? "");
        if (string.IsNullOrEmpty(name)) return null;
        var path = Path.Combine(UploadsDir, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>O'quvchi suratini "/uploads/..." dan o'qiydi. Yo'q bo'lsa null — sertifikat
    /// baribir yaratiladi (rasmsiz), chunki yarim guruhda surat bo'lmasligi odatiy hol.</summary>
    private (byte[] Bytes, string Extension)? ReadPhoto(string? photoUrl)
    {
        var path = ResolveUpload(photoUrl);
        if (path is null) return null;
        try { return (File.ReadAllBytes(path), Path.GetExtension(path)); }
        catch { return null; }
    }

    private string? ResolveCertFile(string? fileUrl)
    {
        var name = Path.GetFileName(fileUrl ?? "");
        if (string.IsNullOrEmpty(name)) return null;
        var path = Path.Combine(CertificatesDir, name);
        return File.Exists(path) ? path : null;
    }

    private async Task<string> SaveCertFileAsync(string fileName, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(CertificatesDir);
        await File.WriteAllBytesAsync(Path.Combine(CertificatesDir, fileName), bytes, ct);
        return "/uploads/certificates/" + fileName;
    }

    private void DeleteCertFile(string? fileUrl)
    {
        var path = ResolveCertFile(fileUrl);
        if (path is null) return;
        try { File.Delete(path); } catch { /* band/yo'q — yozuvni yangilashga to'sqinlik qilmasin */ }
    }

    /// <summary>
    /// Shablon fayli — SHU BO'LIMNING O'ZINIKI. Yuklangan fayl `/uploads` ichida NUSXALANIB,
    /// <see cref="TemplateFilePrefix"/> bilan nomlanadi va shablon shu nusxaga ishora qiladi.
    ///
    /// <para><b>Nega nusxa?</b> `/uploads` — YAGONA tekis papka: shartnoma andozalari, passport
    /// skanlari, topshiriq materiallari — hammasi shu yerda. Ilgari shablon yaratishda istalgan
    /// mavjud `/uploads/....docx` manzilini ko'rsatish mumkin edi, keyin shablon o'chirilganda
    /// esa <b>o'sha begona fayl diskdan o'chib ketardi</b>. Endi shablon faqat O'Z nusxasiga
    /// egalik qiladi va faqat shuni o'chira oladi.</para>
    /// </summary>
    private const string TemplateFilePrefix = "certtpl-";

    /// <summary>Yuklangan faylni shablonning O'Z nusxasiga ko'chiradi. Manba topilmasa <c>null</c>.</summary>
    private async Task<string?> AdoptTemplateFileAsync(string fileUrl, CancellationToken ct)
    {
        var source = ResolveUpload(fileUrl);
        if (source is null) return null;

        var name = $"{TemplateFilePrefix}{Guid.NewGuid():N}{TemplateExtension}";
        Directory.CreateDirectory(UploadsDir);
        // Nusxalash (ko'chirish EMAS): manba boshqa bo'limning fayli bo'lishi mumkin — unga tegmaymiz.
        await File.WriteAllBytesAsync(
            Path.Combine(UploadsDir, name), await File.ReadAllBytesAsync(source, ct), ct);
        return "/uploads/" + name;
    }

    /// <summary>
    /// Shablon faylini o'chiradi — FAQAT u shu bo'limning o'z nusxasi bo'lsa
    /// (<see cref="TemplateFilePrefix"/>). Eski (nusxalash joriy etilishidan oldingi) shablonlar
    /// begona faylga ishora qilayotgan bo'lishi mumkin, shuning uchun ular TEGILMAY qoldiriladi:
    /// ortiqcha fayl qolgani begona hujjatni o'chirib yuborishdan ko'ra xavfsizroq.
    /// </summary>
    private void DeleteTemplateFile(string? fileUrl)
    {
        var name = Path.GetFileName(fileUrl ?? "");
        if (!name.StartsWith(TemplateFilePrefix, StringComparison.Ordinal)) return;
        var path = ResolveUpload(fileUrl);
        if (path is null) return;
        try { File.Delete(path); } catch { /* band/yo'q — yozuvni yangilashga to'sqinlik qilmasin */ }
    }
}
