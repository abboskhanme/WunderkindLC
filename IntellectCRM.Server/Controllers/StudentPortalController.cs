using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Infrastructure.Data;
using IntellectCRM.Infrastructure.Auth;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;
using IntellectCRM.Application.Services;
using System.Security.Claims;
using System.Text.Json;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// O'quvchi (oila) ilovasi API'si — `/api/student/*`. Asosiy foydalanuvchi `student` roli (o'z
/// ma'lumotini o'zi ko'radi), lekin `admin` roli ham `?studentId=...` so'rovi orqali istalgan
/// o'quvchining ma'lumotini ko'ra oladi (admin'ga alohida ko'rinish endpointlari qilmaslik uchun).
/// Mutatsiyalar (xabar yuborish, vazifa topshirish, fayl yuklash, shaxsiy sozlamani saqlash)
/// — faqat `student` rolida: admin boshqa odam nomidan amal qila olmaydi.
/// </summary>
[ApiController]
[Authorize(Roles = "student,parent,admin")]
[Route("api/student")]
public class StudentPortalController(
    AppDbContext db, ChatService chat, IWebHostEnvironment env, ReferenceCache refCache,
    TelegramService telegram, IConfiguration config, DataCache dataCache,
    ContractService contracts) : ControllerBase
{

    /// <summary>
    /// Maqsadli o'quvchini topadi.
    /// • student → o'z akkauntidan (UserId bo'yicha)
    /// • parent  → logini (email) telefon raqami sifatida Student.ParentPhone bilan taqqoslanadi
    /// • admin   → ?studentId=... query param orqali istalgan o'quvchi
    /// </summary>
    private async Task<Student?> TargetAsync(string? studentId)
    {
        if (User.IsInRole("admin"))
        {
            if (string.IsNullOrWhiteSpace(studentId)) return null;
            return await db.Students.FindAsync(studentId);
        }
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return null;

        if (User.IsInRole("parent"))
        {
            var user = await db.Users.FindAsync(uid);
            if (user is null) return null;
            // Telefonlar bazaga HAR DOIM PhoneUtil.Normalize orqali yoziladi (StudentsController
            // Create/Update va CSV import — barcha yozuv yo'llari kanonik "+998-XX-XXX-XX-XX" formatda
            // saqlaydi; Normalize idempotent). Shu sababli kiruvchi login-telefonni bir marta Normalize
            // qilib DB TOMONDA taqqoslash mumkin — butun jadvalni xotiraga yuklamaymiz.
            var norm = PhoneUtil.Normalize(user.Email);
            if (string.IsNullOrEmpty(norm)) return null;
            // Ota ham, ona ham kira oladi — ASOSIY/ota/ona telefonidan birortasi mos kelsa yetarli.
            var children = await db.Students.AsNoTracking()
                .Where(s => !s.IsArchived
                            && (s.ParentPhone == norm || s.FatherPhone == norm || s.MotherPhone == norm))
                .ToListAsync();
            // studentId berilsa — SHU farzand (FAQAT o'ziniki bo'lsa; egalik tekshiruvi). Aks holda birinchi farzand.
            return string.IsNullOrWhiteSpace(studentId)
                ? children.FirstOrDefault()
                : children.FirstOrDefault(s => s.Id == studentId);
        }

        return await db.Students.FirstOrDefaultAsync(s => s.UserId == uid);
    }

    /// <summary>
    /// Mutatsiya (yozish) amallari uchun — FAQAT student rolida; admin impersonate qila olmaydi.
    /// </summary>
    private async Task<Student?> MeAsync()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return uid is null ? null : await db.Students.FirstOrDefaultAsync(s => s.UserId == uid);
    }

    /// <summary>
    /// O'quvchining ASOSIY guruhi — bitta guruh ko'rsatilishi SHART bo'lgan joylar uchun
    /// (bugungi darslar, davomat, o'quv dasturi, chat kanali).
    ///
    /// <para>⚠️ Tanlov A'ZOLIK bo'yicha: faol → sinov → muzlatilgan, keyin eng yangi qo'shilgani
    /// (<see cref="MembershipLifecycle.PrimaryMembership"/>). Ilgari bu yerda
    /// <c>Classes.FirstOrDefault(c =&gt; c.Name == s.ClassName)</c> turardi, ya'ni HAR DOIM
    /// BIRINCHI qo'shilgan guruh: o'quvchi eski guruhida muzlatilib yangisida o'qiyotgan bo'lsa,
    /// ilovada ESKI guruhning darslari/davomati ko'rinardi (yoki guruh arxivlangan bo'lsa —
    /// bo'sh ekran). <c>ClassName</c> endi faqat a'zolik UMUMAN bo'lmaganda zaxira.</para>
    /// </summary>
    private Task<Group?> PrimaryGroupAsync(Student s) => StudentMembershipView.PrimaryGroupAsync(db, s);

    /// <summary>O'quvchining ASOSIY guruh id'si (<see cref="PrimaryGroupAsync"/>).</summary>
    private async Task<string?> ClassIdOf(Student s) => (await PrimaryGroupAsync(s))?.Id;

    /// <summary>
    /// BILDIRISHNOMA IDENTIFIKATORI — ota-ona ham, o'quvchi ham BITTA ilovadan foydalanadi,
    /// shuning uchun push va bildirishnoma tarixi HAR DOIM o'quvchining akkauntiga bog'lanadi.
    ///
    /// <para>student → o'z `UserId`si; parent → FARZANDINING `Student.UserId`si.
    /// Shu sabab: (1) ota-ona telefonida ro'yxatdan o'tgan qurilma o'quvchiga yuborilgan pushni
    /// oladi; (2) ota-ona ilovada bildirishnomalar tarixini KO'RADI (ilgari bo'sh chiqardi —
    /// hamma narsa `Student.UserId` ga yozilardi, ota-onaning esa alohida akkaunti bor).</para>
    ///
    /// <para>Ota-onada bir nechta farzand bo'lsa — birinchisi (butun portal shu konvensiyada:
    /// <see cref="TargetAsync"/> ham `children.FirstOrDefault()` qaytaradi).</para>
    /// </summary>
    private async Task<string?> NotificationUserIdAsync()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return null;
        if (!User.IsInRole("parent")) return uid;
        var child = await TargetAsync(null);
        return child?.UserId ?? uid;
    }

    /// <summary>Admin uchun: studentId berilmagan bo'lsa 400 javobi.</summary>
    private ActionResult NeedStudentId() =>
        BadRequest(new { message = "Admin chaqiruvi uchun ?studentId=... kerak" });

    /// <summary>Yuklangan faylni `uploads/` ga saqlab, `/uploads/...` manzilini qaytaradi.</summary>
    private async Task<string> SaveUploadAsync(IFormFile file)
    {
        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = Application.Services.UploadGuard.SafeName(file);
        await using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored));
        await file.CopyToAsync(fs);
        return $"/uploads/{stored}";
    }

    /// <summary>
    /// Ilova (ota-ona/o'quvchi) uchun Telegram bot holati va shu o'quvchi botda ro'yxatdan o'tganmi.
    /// Ilova birinchi kirilganda — agar <c>configured=true</c> va <c>registered=false</c> bo'lsa —
    /// foydalanuvchini botga (<c>deepLink</c>) yo'naltirib, e'lon olish uchun ro'yxatdan o'tishni taklif qiladi.
    /// </summary>
    [HttpGet("telegram")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult<object>> TelegramInfo([FromQuery] string? studentId)
    {
        var s = User.IsInRole("parent") ? await TargetAsync(null) : await MeAsync();
        if (s is null) return NotFound();
        var registered = await db.TelegramRegistrations.AnyAsync(r => r.StudentId == s.Id);
        var username = telegram.BotUsername ?? "";
        return Ok(new
        {
            configured = telegram.IsConfigured,
            botUsername = username,
            botName = telegram.BotName ?? "",
            deepLink = string.IsNullOrEmpty(username) ? "" : $"https://t.me/{username}",
            registered,
        });
    }

    /// <summary>
    /// Ota-ona (o'quvchi akkaunti orqali) taklif yoki shikoyat yuboradi. Faqat student rolida.
    /// Admin "Taklif va shikoyatlar" bo'limida ko'radi.
    /// </summary>
    [HttpPost("feedback")]
    [Authorize(Roles = "student,parent")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> SubmitFeedback(
        [FromForm] string type, [FromForm] string text, IFormFile? image)
    {
        // Student o'zi, parent farzandi nomidan yuboradi.
        var s = User.IsInRole("parent") ? await TargetAsync(null) : await MeAsync();
        if (s is null) return Unauthorized();
        var body = (text ?? "").Trim();
        if (body.Length == 0) return BadRequest(new { message = "Matn bo'sh" });
        if (image is not null && Application.Services.UploadGuard.Validate(image) is { } imgError)
            return BadRequest(new { message = imgError });

        var feedbackType = type == "complaint" ? "complaint" : "suggestion";
        var senderName = s.ParentFullName ?? "";
        db.Feedbacks.Add(new Feedback
        {
            StudentId = s.Id,
            ParentName = senderName,
            SenderRole = "parent",
            SenderName = senderName,
            Type = feedbackType,
            Text = body,
            ImageUrl = image is { Length: > 0 } ? await SaveUploadAsync(image) : null,
            CreatedAt = AppClock.Now,
            Status = "new",
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<StudentProfileDto>> Profile([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        // Guruh nomi — ASOSIY (tirik) a'zolikdan; `ClassName` faqat zaxira (qarang: PrimaryGroupAsync).
        var primary = await PrimaryGroupAsync(s);
        return new StudentProfileDto(
            s.Id, s.FullName, primary?.Name ?? s.ClassName, s.BirthDate, s.Gender,
            s.ParentFullName, s.ParentPhone, s.EnrollmentDate,
            s.BirthCertificateUrl, s.ParentPassportUrl);
    }

    /// <summary>O'quvchining barcha test natijalari (O'quv bo'limi → Testlar natijalari'dan; o'qituvchi
    /// kiritadi) — sana desc, har testda o'quvchining o'rni (Rank) va jami baholanganlar (Total).
    /// student — o'ziniki; parent — farzandiniki; admin — <c>?studentId=...</c>.</summary>
    [HttpGet("test-results")]
    public async Task<ActionResult<List<StudentGroupTestDto>>> TestResults([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await TestResultService.StudentResultsAsync(db, s.Id);
    }

    // ---------- ONLAYN TEST (ilovada ishlanadi — Telegram bot bilan bir xil mantiq) ----------

    /// <summary>O'quvchining faol guruhlaridagi ONLAYN testlar: hozirgi/kelgusi va oxirgi 7 kunda
    /// tugaganlar. Har qatorda savollar PDF'i, vaqt oynasi va holat (ochiq/topshirilgan/…) keladi.
    /// student — o'ziniki; parent — farzandiniki; admin — <c>?studentId=...</c>.</summary>
    [HttpGet("online-tests")]
    public async Task<ActionResult<List<StudentOnlineTestDto>>> OnlineTests([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await OnlineTestService.ListForStudentAsync(db, s.Id);
    }

    /// <summary>Bitta onlayn test tafsiloti (o'z javoblari, o'rni; javob kaliti FAQAT vaqt tugagach).</summary>
    [HttpGet("online-tests/{id}")]
    public async Task<ActionResult<StudentOnlineTestDetailDto>> OnlineTest(string id, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var dto = await OnlineTestService.DetailAsync(db, s.Id, id);
        return dto is null ? NotFound(new { message = "Test topilmadi" }) : dto;
    }

    /// <summary>Javoblarni yuborish ("ABCDA…"). Avtomatik tekshiriladi va ball
    /// <see cref="TestScore"/>ga yoziladi — bot orqali topshirish bilan bir xil natija.
    /// Bir marta topshiriladi; admin boshqa o'quvchi nomidan yubora olmaydi.</summary>
    [HttpPost("online-tests/{id}/submit")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult<StudentOnlineTestDetailDto>> SubmitOnlineTest(
        string id, OnlineTestSubmitRequest req)
    {
        var s = await TargetAsync(null);
        if (s is null) return NotFound();
        var (result, error) = await OnlineTestService.SubmitAsync(db, s.Id, id, req.Answers ?? "");
        if (error is not null) return BadRequest(new { message = error });
        return result is null ? NotFound(new { message = "Test topilmadi" }) : result;
    }

    /// <summary>
    /// O'quvchining TO'LIQ "shaxsiy daftari" — admin ko'radigan detal sahifasi bilan AYNAN bir xil
    /// (<see cref="StudentProfileBuilder"/>): profil + shaxsiy ma'lumot (manzil, chegirma, guruh,
    /// hujjatlar, balans), fan×chorak baholar va o'rtacha, davomat (qoldirgan/kech + sabablar),
    /// OYLIK BAHOLASH (turlar×oy), uy vazifa/xulq jamlamasi va oylik trend — bularning bari
    /// bitta javobda.
    /// student — o'ziniki; parent — farzandiniki; admin — <c>?studentId=...</c>.
    /// </summary>
    [HttpGet("notebook")]
    public async Task<ActionResult<StudentNotebookDto>> Notebook([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await StudentProfileBuilder.BuildAsync(db, s);
    }

    /// <summary>Markaz meta'si (chorak/dars vaqtlari/sabablar + joriy chorak/hafta) —
    /// hammaga bir xil, `studentId` shart emas.</summary>
    [HttpGet("meta")]
    public async Task<ActionResult<PortalMetaDto>> Meta() => await refCache.MetaAsync();

    /// <summary>Joriy markaz nomi — ilova brendingi/sarlavhasi uchun.</summary>
    [HttpGet("school")]
    public async Task<ActionResult<SchoolNameDto>> School()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        return new SchoolNameDto(m?.Name ?? "", m?.TelegramChannel ?? "", m?.LogoUrl ?? "");
    }

    /// <summary>Ilova bildirishnomalari tarixi (yuborilgan push'lar) — o'qilmaganlar soni bilan.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<NotificationsResponseDto>> Notifications()
    {
        var uid = await NotificationUserIdAsync();
        if (uid is null) return Unauthorized();
        var items = await db.UserNotifications.Where(n => n.UserId == uid)
            .OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync();
        var unread = items.Count(n => n.ReadAt == null);
        return new NotificationsResponseDto(unread, items.Select(n =>
            new UserNotificationDto(n.Id, n.Title, n.Body, n.Type, n.CreatedAt.ToString("o"),
                n.ReadAt != null, n.ConfirmedAt != null)).ToList());
    }

    /// <summary>Barcha bildirishnomalarni o'qilgan deb belgilaydi.</summary>
    [HttpPost("notifications/read")]
    public async Task<IActionResult> MarkNotificationsRead()
    {
        var uid = await NotificationUserIdAsync();
        if (uid is null) return Unauthorized();
        var unread = await db.UserNotifications.Where(n => n.UserId == uid && n.ReadAt == null).ToListAsync();
        foreach (var n in unread) n.ReadAt = AppClock.Now;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bitta bildirishnomani TASDIQLAYDI (o'qib tasdiqladim) — admin shu holatni ko'radi.</summary>
    [HttpPost("notifications/{id}/confirm")]
    public async Task<IActionResult> ConfirmNotification(string id)
    {
        var uid = await NotificationUserIdAsync();
        if (uid is null) return Unauthorized();
        var n = await db.UserNotifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        if (n is null) return NotFound();
        if (n.ConfirmedAt is null)
        {
            n.ConfirmedAt = AppClock.Now;
            n.ReadAt ??= AppClock.Now;
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    // ---------- Shartnoma (o'z hujjatlari) ----------

    /// <summary>O'quvchi (oila) shartnomalari — admin yashirganlari ko'rinmaydi, eng yangisi birinchi.</summary>
    [HttpGet("contracts")]
    public async Task<ActionResult<IEnumerable<ContractDocDto>>> Contracts([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var items = await db.Contracts.AsNoTracking()
            .Where(c => c.Target == "parent" && c.RecipientKey == s.Id && c.Visible && c.PdfUrl != "")
            .OrderByDescending(c => c.Number).ToListAsync();
        return items.Select(ContractService.ToDoc).ToList();
    }

    /// <summary>Shartnoma PDF nusxasi (imzolangani bo'lsa — o'sha). Faqat shu o'quvchiniki.</summary>
    [HttpGet("contracts/{id}/pdf")]
    public async Task<IActionResult> ContractPdf(string id, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var c = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == id && x.Target == "parent" && x.RecipientKey == s.Id && x.Visible);
        if (c is null) return NotFound();
        var path = contracts.ResolveUpload(c.PdfUrl);
        if (path is null) return NotFound(new { message = "Shartnoma fayli topilmadi" });
        return PhysicalFile(path, "application/pdf", $"shartnoma-{c.Number}.pdf");
    }

    // ---------- Baholar va davomat (o'ziniki) ----------

    [HttpGet("grades")]
    public async Task<ActionResult<StudentReportDto>> Grades([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await StudentReportBuilder.BuildAsync(db, s);
    }

    /// <summary>
    /// O'quvchi reytingi (admin "Reyting"i bilan bir xil hisob — yig'ilgan ball bo'yicha):
    /// <b>o'z guruhlarini to'liq</b> (har biri alohida, `Groups`), <b>markaz bo'yicha esa faqat TOP 15</b>.
    /// O'z qatori `MeStudentId` bilan, markaz o'rni (top 15 dan tashqarida bo'lsa ham) `MeSchoolRank` bilan beriladi.
    /// Parent farzandi nomidan; admin uchun `?studentId=` shart.
    ///
    /// <para>Guruh ro'yxati M2M A'ZOLIK bo'yicha quriladi (<see cref="RatingService.PortalAsync"/>) —
    /// ilgari `ClassName` matn yorlig'i tengligi ishlatilar va yorliq bo'sh bo'lgan o'quvchida
    /// guruh reytingi BO'SH chiqardi.</para>
    /// </summary>
    [HttpGet("rating")]
    public async Task<ActionResult<PortalRatingDto>> Rating([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        // Butun markaz reytingi admin endpointi bilan BIR xil kesh kalitidan ("rating:school") o'qiladi —
        // bog'liq jadval (jumladan StudentGroup) o'zgarsa interceptor uni avtomatik yangilaydi.
        var school = await dataCache.GetOrCreateAsync(
            "rating:school",
            new[]
            {
                nameof(JournalEntry), nameof(LessonNote), nameof(Student),
                nameof(StudentGroup), nameof(Group), nameof(AbsenceReason), nameof(CriterionGrade),
                // Qo'lda tuzatish ballni darhol o'zgartiradi (admin endpointi bilan BIR XIL ro'yxat).
                nameof(StudentBallAdjustment),
            },
            TimeSpan.FromMinutes(15),
            db2 => RatingService.SchoolAsync(db2));

        return await RatingService.PortalAsync(db, s, school);
    }

    /// <summary>
    /// «UMUMIY STATISTIKA» — sana ORALIG'I bo'yicha jurnal (hafta/oy): har darsga olingan baho,
    /// davomat, uyga vazifa va xulq + jamlanma va fanlar kesimi.
    ///
    /// <para>Nega alohida endpoint: <c>grades</c> faqat fan×chorak O'RTACHASINI, <c>attendance</c>
    /// esa faqat davomatsizlik yozuvlarini beradi — ikkalasidan ham "shu haftada har darsda nima
    /// bo'ldi" degan savolga javob chiqmaydi.</para>
    ///
    /// <para><c>from</c>/<c>to</c> berilmasa — joriy oy. <c>groupId</c> berilmasa — o'quvchining
    /// BARCHA guruhlari birlashtiriladi (har dars o'z guruhi/fani bilan). Hisob mantig'i admin
    /// jurnal modali bilan YAGONA (<see cref="StudentJournalBuilder"/>): guruhga qo'shilishidan
    /// oldingi / chiqib ketganidan keyingi darslar chiqmaydi, orqaga sanalgan a'zolikdagi
    /// "noma'lum" darslar esa davomat foizini buzmaydi.</para>
    /// </summary>
    [HttpGet("journal")]
    public async Task<ActionResult<StudentPeriodJournalDto>> Journal(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? groupId, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        var (f, t, error) = StudentJournalBuilder.NormalizeRange(from, to);
        if (error is not null) return BadRequest(new { message = error });

        var dto = await StudentJournalBuilder.PeriodAsync(db, s.Id, f, t, groupId);
        return dto is null ? NotFound() : dto;
    }

    /// <summary>
    /// O'quvchi davomati — chorak bo'yicha umumlashtirilgan ko'rsatkichlar + kunlik
    /// davomatsizlik/kech qolish yozuvlari ro'yxati.
    /// </summary>
    [HttpGet("attendance")]
    public async Task<ActionResult<StudentAttendanceFullDto>> Attendance(
        [FromQuery] int? quarter, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var cls = await PrimaryGroupAsync(s);
        if (cls is null) return new StudentAttendanceFullDto(
            new StudentAttendanceDto(new(), new(), new(), new(), new()),
            new List<StudentAbsenceRowDto>());

        var report = await StudentReportBuilder.BuildAsync(db, s);

        var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
        var reasonRows = await db.AbsenceReasons.ToListAsync();
        var reasons = reasonRows.ToDictionary(r => r.Id);

        var rowsQuery = db.JournalEntries
            .Where(e => e.ClassId == cls.Id && e.StudentId == s.Id && e.ReasonId != null);
        if (quarter.HasValue) rowsQuery = rowsQuery.Where(e => e.Quarter == quarter.Value);

        var rows = (await rowsQuery.ToListAsync())
            .OrderByDescending(e => e.Date, StringComparer.Ordinal).ThenByDescending(e => e.Period)
            .Select(e =>
            {
                reasons.TryGetValue(e.ReasonId!, out var r);
                var name = r?.Name ?? "";
                return new StudentAbsenceRowDto(
                    e.Date, e.Period, e.Quarter,
                    e.SubjectId, subjects.GetValueOrDefault(e.SubjectId, ""),
                    e.ReasonId!, name, r?.IsLate ?? false,
                    name.ToLowerInvariant().Contains("kasal"));
            })
            .ToList();

        return new StudentAttendanceFullDto(report.Attendance, rows);
    }

    /// <summary>
    /// Bosh sahifa uchun YAGONA chaqiruv — profil + meta + bugungi darslar + bugungi baholar +
    /// balans. Bir o'rinda hammasi (Flutter Dashboard ekraniga mos).
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<StudentDashboardDto>> Dashboard([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        // Bugungi darslar/baholar va oylik — ASOSIY GURUH bo'yicha (qarang: PrimaryGroupAsync).
        var cls = await PrimaryGroupAsync(s);
        var profile = new StudentProfileDto(
            s.Id, s.FullName, cls?.Name ?? s.ClassName, s.BirthDate, s.Gender,
            s.ParentFullName, s.ParentPhone, s.EnrollmentDate,
            s.BirthCertificateUrl, s.ParentPassportUrl);
        var meta = await refCache.MetaAsync();

        // Bugungi darslar — jadval tizimi olib tashlangan; bugun yozilgan darslar (LessonNote) bo'yicha.
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var todayLessons = new List<StudentLessonDto>();
        var todayGrades = new List<HomeworkItemDto>();
        if (cls is not null)
        {
            var subjects = await db.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);
            var teacherNames = await db.Teachers.ToDictionaryAsync(x => x.Id, x => x.FullName);
            var teacherByGroupSubject = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(cls.CourseId) && !string.IsNullOrEmpty(cls.TeacherId))
                teacherByGroupSubject[cls.CourseId] = cls.TeacherId;

            var todayNotes = await db.LessonNotes
                .Where(n => n.ClassId == cls.Id && n.Date == today)
                .ToListAsync();

            todayLessons = todayNotes
                .OrderBy(n => n.Period)
                .Select(n =>
                {
                    var teacherId = teacherByGroupSubject.GetValueOrDefault(n.SubjectId, "");
                    return new StudentLessonDto(
                        0, n.Period, null, null,
                        n.SubjectId, subjects.GetValueOrDefault(n.SubjectId, ""),
                        teacherId, teacherNames.GetValueOrDefault(teacherId, ""));
                })
                .ToList();

            // Bugungi baholar — shu o'quvchining bugungi jurnal yozuvlari (Grade != null).
            var entries = await db.JournalEntries
                .Where(e => e.ClassId == cls.Id && e.StudentId == s.Id && e.Date == today && e.Grade != null)
                .ToListAsync();
            var notes = await db.LessonNotes
                .Where(n => n.ClassId == cls.Id && n.Date == today)
                .ToListAsync();
            var noteMap = notes.ToDictionary(n => (n.Date, n.Period, n.SubjectId));
            var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id);

            todayGrades = entries
                .OrderBy(e => e.Period)
                .Select(e =>
                {
                    noteMap.TryGetValue((e.Date, e.Period, e.SubjectId), out var n);
                    AbsenceReason? r = null;
                    if (e.ReasonId is not null) reasons.TryGetValue(e.ReasonId, out r);
                    return new HomeworkItemDto(
                        e.Date, e.Period, e.SubjectId, subjects.GetValueOrDefault(e.SubjectId, ""),
                        n?.Topic ?? "", n?.Homework, n?.Conducted ?? true,
                        e.Grade, e.ReasonId, r?.Name, r?.IsLate ?? false);
                })
                .ToList();
        }

        var monthlyFee = cls?.MonthlyFee ?? 0m;

        return new StudentDashboardDto(
            profile, meta, todayLessons, todayGrades, s.Balance, monthlyFee);
    }

    /// <summary>
    /// O'quvchining GURUHLARI — faol ham, tugagan/chiqilgan ham. Ilova bosh sahifasi va profili
    /// shu yagona manbadan foydalanadi.
    ///
    /// <para>CRM'dagi <c>ClassesController.StudentGroups</c> bilan BIR XIL qamrov: a'zolik
    /// yozuvlari FILTRLANMAYDI (arxivlangan guruh ham, chiqib ketilgan a'zolik ham qaytadi) —
    /// aks holda guruh ilovada jimgina yo'qolib, "guruhi yo'q" bo'lib ko'rinardi. Ko'rsatish
    /// qoidasini <c>State</c> hal qiladi.</para>
    /// </summary>
    [HttpGet("groups")]
    public async Task<ActionResult<List<StudentGroupItemDto>>> Groups([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await StudentGroupsAsync(s);
    }

    /// <summary>Ko'rsatiladigan yagona holat — qoida ikki tilda takrorlanmasin.</summary>
    private static string GroupState(StudentGroup m, bool groupArchived)
    {
        // Guruhning o'zi yopilgan yoki a'zolik tugagan/chiqilgan — "tugagan".
        if (groupArchived) return "finished";
        if (!string.IsNullOrWhiteSpace(m.LeftAt) || m.Status == "completed") return "finished";
        // DIQQAT: muzlatilgan a'zolik ikki xil yoziladi — ClassesController IsActive'ni
        // tegmaydi, StudentsController esa false qo'yadi. Shu sabab avval Status tekshiriladi.
        if (m.Status == "frozen") return "frozen";
        if (!m.IsActive) return "finished";
        return m.Status == "trial" ? "trial" : "active";
    }

    /// <summary>Ro'yxat tartibi: faol → sinov → muzlatilgan → tugagan, so'ng nom bo'yicha.</summary>
    private static int StateRank(string state) => state switch
    {
        "active" => 0, "trial" => 1, "frozen" => 2, _ => 3,
    };

    private async Task<List<StudentGroupItemDto>> StudentGroupsAsync(Student s)
    {
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.StudentId == s.Id)
            .ToListAsync();

        var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var groups = groupIds.Count == 0
            ? new List<Group>()
            : await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync();

        // ORQAGA MOSLIK: a'zolik yozuvi UMUMAN bo'lmasa — `Student.ClassName` bo'yicha guruh.
        // StudentReportBuilder/StudentProfileBuilder ham AYNAN shunday qiladi, ya'ni baho va
        // davomat shu guruhdan hisoblanadi — ilova ham SHU guruhni ko'rsatishi kerak, aks holda
        // o'quvchi "guruhim yo'q" degan bo'sh ekranni ko'radi (eski bazalarda uchraydi).
        if (memberships.Count == 0 && !string.IsNullOrWhiteSpace(s.ClassName))
        {
            var byName = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Name == s.ClassName);
            if (byName is not null)
            {
                groups.Add(byName);
                memberships.Add(new StudentGroup
                {
                    StudentId = s.Id, GroupId = byName.Id, IsActive = true, Status = "active",
                });
            }
        }
        if (groups.Count == 0) return new List<StudentGroupItemDto>();

        var byId = groups.ToDictionary(g => g.Id);
        var courseIds = groups.Select(g => g.CourseId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var teacherIds = groups.Select(g => g.TeacherId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var courses = await db.Subjects.AsNoTracking()
            .Where(x => courseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var teachers = await db.Teachers.AsNoTracking()
            .Where(x => teacherIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName);

        return memberships
            .Where(m => byId.ContainsKey(m.GroupId))
            // Bir guruhda bir nechta a'zolik tarixi bo'lsa (chiqib, qaytib qo'shilgan) — eng
            // "tirik"ini olamiz, aks holda bitta guruh ikki marta chiqardi.
            .GroupBy(m => m.GroupId)
            .Select(g => g.OrderBy(m => StateRank(GroupState(m, byId[m.GroupId].IsArchived))).First())
            .Select(m =>
            {
                var c = byId[m.GroupId];
                return new StudentGroupItemDto(
                    c.Id, c.Name,
                    courses.GetValueOrDefault(c.CourseId ?? "", ""),
                    teachers.GetValueOrDefault(c.TeacherId ?? "", ""),
                    c.Days ?? new List<int>(), c.StartTime ?? "", c.EndTime ?? "", c.Room ?? "",
                    GroupState(m, c.IsArchived), m.Status ?? "", m.IsActive, c.IsArchived,
                    m.JoinedAt ?? "", m.LeftAt ?? "");
            })
            .OrderBy(r => StateRank(r.State))
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    // ---------- Foydalanuvchi sozlamalari (til, tema, bildirishnoma) ----------

    /// <summary>Foydalanuvchi sozlamasini qaytaradi. Student — o'ziniki; admin — `?studentId` o'quvchining
    /// foydalanuvchisi (o'quvchiga akkaunt biriktirilmagan bo'lsa default qaytadi).</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings([FromQuery] string? studentId)
    {
        string? targetUid;
        if (User.IsInRole("admin"))
        {
            if (string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
            var st = await db.Students.FindAsync(studentId);
            if (st is null) return NotFound();
            targetUid = st.UserId;
        }
        else
        {
            targetUid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
        if (string.IsNullOrWhiteSpace(targetUid))
            return new UserSettingsDto("uz", "system", true);
        var us = await db.UserSettings.FindAsync(targetUid);
        return us is null
            ? new UserSettingsDto("uz", "system", true)
            : new UserSettingsDto(us.Language, us.Theme, us.NotificationsEnabled);
    }

    /// <summary>Sozlamani yangilash (qator yo'q bo'lsa yaratiladi). Faqat student rolida — o'ziniki.</summary>
    [HttpPut("settings")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult<UserSettingsDto>> SaveSettings(SaveUserSettingsRequest req)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();
        var st = await db.UserSettings.FindAsync(uid);
        if (st is null)
        {
            st = new UserSettings { UserId = uid };
            db.UserSettings.Add(st);
        }
        if (!string.IsNullOrWhiteSpace(req.Language)) st.Language = req.Language!.Trim();
        if (!string.IsNullOrWhiteSpace(req.Theme)) st.Theme = req.Theme!.Trim();
        if (req.NotificationsEnabled.HasValue) st.NotificationsEnabled = req.NotificationsEnabled.Value;
        st.UpdatedAt = AppClock.Now;
        await db.SaveChangesAsync();
        return new UserSettingsDto(st.Language, st.Theme, st.NotificationsEnabled);
    }

    /// <summary>
    /// O'quvchi/ota-ona ilova ichida o'z parolini almashtiradi. Joriy parol bilan tasdiqlanadi,
    /// yangi parol kamida 8 belgi. Faqat o'zinikiga (admin bu yerda impersonate qila olmaydi).
    /// Login (email) o'zgarmagani uchun joriy token amal qilaveradi — qayta kirish shart emas.
    /// </summary>
    [HttpPut("password")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();
        var user = await db.Users.FindAsync(uid);
        if (user is null) return Unauthorized();

        if (!PasswordHasher.Verify(req.CurrentPassword ?? "", user.PasswordHash))
            return BadRequest(new { message = "Joriy parol noto'g'ri" });

        var newPwd = (req.NewPassword ?? "").Trim();
        if (newPwd.Length < 8)
            return BadRequest(new { message = "Yangi parol kamida 8 belgidan iborat bo'lsin" });

        user.SetOwnPassword(newPwd);
        await db.SaveChangesAsync();
        return Ok(new { message = "Parol almashtirildi" });
    }

    // ---------- Joylashuv (GPS) ----------

    /// <summary>
    /// Uy joylashuvini yangilash — mobil ilova GPS dan keladi (latitude/longitude, ixtiyoriy address).
    /// Ilova foydalanuvchisi (o'quvchi/ota-ona — bitta akkaunt) o'z joylashuvini kiritadi (admin impersonate emas).
    /// Saqlangan joylashuv admin "Ilova → Joylashuv" xaritasida (Leaflet) ko'rinadi.
    /// </summary>
    [HttpPut("location")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult> UpdateLocation(UpdateLocationRequest req)
    {
        var s = await TargetAsync(null);   // akkauntga bog'langan o'quvchi
        if (s is null) return NotFound();
        if (req.Latitude is < -90 or > 90 || req.Longitude is < -180 or > 180)
            return BadRequest(new { message = "Koordinatalar noto'g'ri" });
        s.Latitude = req.Latitude;
        s.Longitude = req.Longitude;
        s.LocationAddress = (req.Address ?? "").Trim();
        s.LocationUpdatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Joriy saqlangan joylashuvni o'qish (ilova xaritada ko'rsatishi uchun). Hali yo'q bo'lsa null'lar.</summary>
    [HttpGet("location")]
    public async Task<ActionResult<StudentLocationDto>> GetLocation([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return new StudentLocationDto(s.Latitude, s.Longitude, s.LocationAddress, s.LocationUpdatedAt);
    }

    // ---------- Push bildirishnoma (qurilma tokeni) ----------

    /// <summary>Push (FCM/APNs/Web) qurilma tokenini ro'yxatdan o'tkazadi (yangi bo'lsa qo'shadi,
    /// mavjud bo'lsa LastSeenAt yangilanadi). Token boshqa foydalanuvchiga bog'langan bo'lsa
    /// joriy foydalanuvchiga ko'chiriladi (qurilma boshqa akkauntga kirgan deb hisoblanadi).
    /// OTA-ONA ham SHU ilovadan kiradi, shuning uchun uning qurilmasi ham qabul qilinadi —
    /// token FARZANDINING akkauntiga bog'lanadi (<see cref="NotificationUserIdAsync"/>), aks holda
    /// ota-ona telefoniga push hech qachon yetib bormasdi.</summary>
    [HttpPost("notifications/register")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult> RegisterDevice(RegisterDeviceRequest req)
    {
        var uid = await NotificationUserIdAsync();
        if (uid is null) return Unauthorized();
        var token = req.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token)) return BadRequest(new { message = "Token bo'sh" });
        var platform = string.IsNullOrWhiteSpace(req.Platform) ? "android" : req.Platform!.Trim().ToLowerInvariant();
        var deviceName = (req.DeviceName ?? "").Trim();
        var appId = (req.AppId ?? "").Trim();

        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
        if (existing is null)
        {
            db.DeviceTokens.Add(new DeviceToken
            {
                UserId = uid,
                Token = token,
                Platform = platform,
                DeviceName = deviceName,
                AppId = appId,
            });
        }
        else
        {
            existing.UserId = uid;
            existing.Platform = platform;
            if (deviceName.Length > 0) existing.DeviceName = deviceName;
            if (appId.Length > 0) existing.AppId = appId;
            existing.LastSeenAt = AppClock.Now;
        }

        // BITTA AKKAUNT — BITTA QURILMA: shu akkauntning boshqa (eski) qurilma tokenlari
        // o'chiriladi, ya'ni push FAQAT ENG OXIRGI kirilgan qurilmaga boradi.
        // Sabab: o'quvchi akkauntidan O'QUVCHI ham, OTA-ONA ham foydalanadi (bitta ilova) va
        // eski/ikkinchi telefon qolib ketsa bir xil xabar bir necha qurilmaga ketardi.
        var stale = await db.DeviceTokens
            .Where(d => d.UserId == uid && d.Token != token)
            .ToListAsync();
        if (stale.Count > 0) db.DeviceTokens.RemoveRange(stale);

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Qurilma tokenini o'chiradi (logout/disable). Topilmasa ham 200 qaytaradi.</summary>
    [HttpDelete("notifications/register")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult> UnregisterDevice([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest(new { message = "Token bo'sh" });
        var uid = await NotificationUserIdAsync();
        var d = await db.DeviceTokens.FirstOrDefaultAsync(x => x.Token == token && x.UserId == uid);
        if (d is not null)
        {
            db.DeviceTokens.Remove(d);
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    // ---------- To'lovlar (o'ziniki) ----------

    [HttpGet("finance")]
    public async Task<ActionResult<StudentLedgerDto>> Finance([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await StudentLedger.BuildAsync(db, s);
    }

    // ---------- Guruh chati (o'z guruhi) ----------

    [HttpGet("chat")]
    public async Task<ActionResult<IEnumerable<ChatMessageDto>>> Chat(
        [FromQuery] string? since, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        // Chat kanali — ASOSIY guruh (qarang: PrimaryGroupAsync). Ilgari `ClassName` ishlatilardi,
        // ya'ni o'quvchi ESKI (muzlatilgan) guruhining chatini ochardi.
        var channel = (await PrimaryGroupAsync(s))?.Name ?? s.ClassName;
        if (string.IsNullOrEmpty(channel)) return new List<ChatMessageDto>();
        return await chat.GetMessagesAsync(channel, ChatService.ParseSince(since));
    }

    /// <summary>Chatga xabar yuborish — faqat student rolida (admin o'zining /api/admin/messages
    /// orqali yozadi; bu yerda admin impersonate qila olmaydi).</summary>
    [HttpPost("chat")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult<ChatMessageDto>> SendChat(SendChatRequest req)
    {
        var s = await MeAsync();
        if (s is null) return NotFound();
        var channel = (await PrimaryGroupAsync(s))?.Name ?? s.ClassName;
        if (string.IsNullOrEmpty(channel)) return BadRequest(new { message = "Guruh biriktirilmagan" });
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var dto = await chat.PostAsync(channel, uid, req.Text);
        return dto is null ? BadRequest(new { message = "Xabar bo'sh" }) : dto;
    }

    // ==================== AI tekshiruv (Speaking / Writing) ====================

    /// <summary>O'quvchining bugungi AI tekshiruv holati (kalitlar tayyorligi + limit/premium/blok).</summary>
    [HttpGet("ai-check/status")]
    public async Task<ActionResult<AiCheckStatusDto>> AiCheckStatus([FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        var geminiReady = GeminiService.IsConfigured(AppSecrets.GeminiApiKey);
        var azureReady = AzureSpeechService.IsConfigured(AppSecrets.AzureSpeechKey, AppSecrets.AzureSpeechRegion);
        var (premium, blocked, limit, used) = await AiAccessAsync(s.Id, meta);
        var remaining = premium ? 999 : Math.Max(0, limit - used);
        // Enabled=false bo'lsa ilova bo'limni yopiq ko'rsatadi (tarixni o'qish baribir ishlaydi).
        return new AiCheckStatusDto(geminiReady, azureReady, premium, blocked, limit, used, remaining,
            meta?.AiCheckEnabled ?? false);
    }

    /// <summary>O'quvchi AI tekshiruv tarixi (eng yangi birinchi).</summary>
    [HttpGet("ai-check/history")]
    public async Task<ActionResult<IEnumerable<AiCheckListItemDto>>> AiCheckHistory([FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        return await db.AiChecks.Where(a => a.StudentId == s.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AiCheckListItemDto(a.Id, a.Type, a.Prompt, a.Score, a.Date, a.CreatedAt, a.AudioUrl != ""))
            .ToListAsync();
    }

    /// <summary>Bitta AI tekshiruv yozuvi (to'liq — matn/ovoz/tahlil).</summary>
    [HttpGet("ai-check/history/{id}")]
    public async Task<ActionResult<AiCheckDto>> AiCheckItem(string id, [FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var a = await db.AiChecks.FirstOrDefaultAsync(x => x.Id == id && x.StudentId == s.Id);
        return a is null ? NotFound() : ToAiCheckDto(a);
    }

    /// <summary>Writing (yozma) — o'quvchi matn yozadi, Gemini tahlil qiladi va saqlaydi.</summary>
    [HttpPost("ai-check/writing")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult<AiCheckDto>> AiCheckWriting(AiCheckWritingRequest req)
    {
        var s = await MeAsync();
        if (s is null) return NotFound();
        var text = (req.Text ?? "").Trim();
        if (text.Length < 10) return BadRequest(new { message = "Matn juda qisqa (kamida 10 belgi)." });
        if (text.Length > 8000) text = text[..8000];

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (GuardEnabled(meta) is { } offError) return offError;
        if (await GuardLimitAsync(s.Id, meta) is { } limitError) return limitError;
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return BadRequest(new { message = "AI tekshiruv hali sozlanmagan (admin Gemini kalitini kiritishi kerak)." });

        var taskType = (req.TaskType ?? "").Trim();
        if (taskType != "ielts_task1" && taskType != "ielts_task2") taskType = "";

        var model = GeminiService.ResolveModel(config);
        var prompt = AiCheckService.WritingPrompt(req.Prompt, text, taskType);
        var (ok, raw, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, prompt, jsonMode: true);
        if (!ok) return BadRequest(new { message = err ?? "AI tahlil qilolmadi." });
        var analysis = AiCheckService.Parse(raw);
        if (analysis is null) return BadRequest(new { message = "AI javobini o'qib bo'lmadi. Qaytadan urinib ko'ring." });

        var rec = new AiCheck
        {
            StudentId = s.Id,
            Type = "writing",
            TaskType = taskType,
            Prompt = (req.Prompt ?? "").Trim(),
            InputText = text,
            // IELTS bo'lsa umumiy band (0-9), aks holda 0-100 ball.
            Score = taskType.Length > 0 && analysis.Ielts is not null ? analysis.Ielts.Overall : analysis.Overall,
            AnalysisJson = JsonSerializer.Serialize(analysis),
            Model = model,
            Date = AppClock.Today.ToString("yyyy-MM-dd"),
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.AiChecks.Add(rec);
        await db.SaveChangesAsync();
        return ToAiCheckDto(rec);
    }

    /// <summary>
    /// Speaking (nutq) — ovoz yuboriladi. Oqim: Azure talaffuzni baholaydi (Pronunciation Assessment —
    /// nutqni matnga o'giradi + HAR SO'Z talaffuz aniqligi + umumiy ravonlik/ohang) → Gemini har so'z
    /// bo'yicha talaffuz maslahati beradi. Natijada: so'zlar yashil (yaxshi)/qizil (xato) + so'z soni. Ovoz saqlanadi.
    /// </summary>
    [HttpPost("ai-check/speaking")]
    [Authorize(Roles = "student")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<AiCheckDto>> AiCheckSpeaking(
        IFormFile audio, [FromForm] string? prompt, [FromForm] string? referenceText)
    {
        var s = await MeAsync();
        if (s is null) return NotFound();
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (GuardEnabled(meta) is { } offError) return offError;
        if (await GuardLimitAsync(s.Id, meta) is { } limitError) return limitError;
        var key = AppSecrets.AzureSpeechKey;
        var region = AppSecrets.AzureSpeechRegion ?? "";
        if (!AzureSpeechService.IsConfigured(key, region))
            return BadRequest(new { message = "Speaking baholash hali sozlanmagan (admin Azure kalitini kiritishi kerak)." });
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return BadRequest(new { message = "AI tahlil hali sozlanmagan (admin Gemini kalitini kiritishi kerak)." });
        if (audio is null || audio.Length == 0) return BadRequest(new { message = "Audio bo'sh" });
        if (audio.Length > 8_000_000) return BadRequest(new { message = "Audio juda katta (8 MB dan oshmasin)." });

        using var ms = new MemoryStream();
        await audio.CopyToAsync(ms);
        var bytes = ms.ToArray();
        if (!AzureSpeechService.LooksLikeWav(bytes))
            return BadRequest(new { message = "Audio formati noto'g'ri (WAV kutilgan)." });

        // 1-qadam: Azure talaffuzni baholaydi (matn + har so'z aniqligi + ravonlik/ohang).
        // Reference matn berilsa — aniq per-so'z baholash (scripted). Bo'lmasa erkin nutq:
        // talaffuz ballari kelmasligi mumkin — bu holda faqat matn + Gemini tahlili bo'ladi.
        var azure = await AzureSpeechService.AssessAsync(bytes, referenceText ?? "", key, region);
        if (azure.Error is not null) return BadRequest(new { message = azure.Error });
        // Talaffuz ballari haqiqatan keldimi? (erkin rejimda kelmasligi mumkin)
        var hasPron = azure.Words.Count > 0 || azure.PronScore > 0 || azure.Accuracy > 0;

        // Ovozni saqlaymiz (qayta eshitish uchun).
        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = $"aicheck-{Guid.NewGuid():N}.wav";
        await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, stored), bytes);

        // 2-qadam: Gemini — Azure talaffuz natijasini (har so'z aniqligi bilan) tahlil qiladi va maslahat beradi.
        var model = GeminiService.ResolveModel(config);
        var aiPrompt = AiCheckService.SpeakingPrompt(prompt, azure.RecognizedText, azure);
        var (ok, raw, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, aiPrompt, jsonMode: true);
        var analysis = ok ? AiCheckService.Parse(raw) : null;
        // Gemini tahlili bo'lmasa ham Azure talaffuz natijasi (ballar + so'zlar) bilan yozuvni saqlaymiz.

        var rec = new AiCheck
        {
            StudentId = s.Id,
            Type = "speaking",
            Prompt = (prompt ?? "").Trim(),
            InputText = (referenceText ?? "").Trim(),
            RecognizedText = azure.RecognizedText,
            AudioUrl = $"/uploads/{stored}",
            Score = analysis?.Overall ?? azure.PronScore,
            // Talaffuz ballari kelgan bo'lsa saqlaymiz (per-so'z yashil/qizil); aks holda bo'sh.
            AzureJson = hasPron ? JsonSerializer.Serialize(azure) : "",
            AnalysisJson = analysis is null ? "" : JsonSerializer.Serialize(analysis),
            Model = model,
            Date = AppClock.Today.ToString("yyyy-MM-dd"),
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.AiChecks.Add(rec);
        await db.SaveChangesAsync();
        return ToAiCheckDto(rec);
    }

    /// <summary>O'quvchi ruxsati: (premium, blocked, effektiv limit, bugun ishlatilgan).</summary>
    private async Task<(bool Premium, bool Blocked, int Limit, int Used)> AiAccessAsync(string studentId, CenterMeta? meta)
    {
        var access = await db.StudentAiAccesses.FirstOrDefaultAsync(a => a.StudentId == studentId);
        var defaultLimit = meta?.AiCheckDailyLimit > 0 ? meta.AiCheckDailyLimit : 3;
        var limit = access is { DailyLimit: > 0 } ? access.DailyLimit : defaultLimit;
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var used = await db.AiChecks.CountAsync(a => a.StudentId == studentId && a.Date == today);
        return (access?.IsPremium ?? false, access?.IsBlocked ?? false, limit, used);
    }

    /// <summary>Bo'lim markaz tomonidan ilovada ochilganmi (<c>CenterMeta.AiCheckEnabled</c>).
    /// Yopiq bo'lsa YANGI tekshiruv bajarilmaydi (o'qish — status/tarix — ruxsat).
    /// Xabar matni yagona joyda: <see cref="AiCheckController.DisabledMessage"/>.</summary>
    private ActionResult? GuardEnabled(CenterMeta? meta) =>
        (meta?.AiCheckEnabled ?? false) ? null : BadRequest(new { message = AiCheckController.DisabledMessage });

    /// <summary>Limit/blok tekshiruvi — buzilsa xato javobi, aks holda null (davom etadi).</summary>
    private async Task<ActionResult?> GuardLimitAsync(string studentId, CenterMeta? meta)
    {
        var (premium, blocked, limit, used) = await AiAccessAsync(studentId, meta);
        if (blocked) return StatusCode(403, new { message = "AI tekshiruv sizga cheklangan. Adminga murojaat qiling." });
        if (premium) return null;
        if (used >= limit)
            return StatusCode(429, new { message = $"Kunlik limit tugadi ({used}/{limit}). Premium uchun adminga murojaat qiling." });
        return null;
    }

    private static AiCheckDto ToAiCheckDto(AiCheck a)
    {
        var analysis = AiCheckService.ParseStored(a.AnalysisJson);
        AiCheckSpeechDto? speech = null;
        if (a.Type == "speaking" && !string.IsNullOrWhiteSpace(a.AzureJson))
        {
            try
            {
                var r = JsonSerializer.Deserialize<SpeakingResultDto>(a.AzureJson);
                if (r is not null)
                    speech = new AiCheckSpeechDto(r.RecognizedText, r.PronScore, r.Accuracy,
                        r.Fluency, r.Completeness, r.Prosody, r.Words ?? new());
            }
            catch { /* azure json buzuq — speech null */ }
        }
        return new AiCheckDto(a.Id, a.Type, a.Prompt, a.InputText, a.RecognizedText, a.AudioUrl,
            a.Score, a.Date, a.CreatedAt, analysis, speech, a.TaskType);
    }

    /* ─── Fan progresi (dars o'tilishiga qarab — LMS'siz) ──────
       Progress = o'tilgan darslar / chorakdagi reja darslar. O'qituvchi jurnalda
       "dars o'tildi" deb belgilashidan kelib chiqadi. */

    /// <summary>Barcha fanlar bo'yicha umumiy + har bir fan progresi (joriy/berilgan chorak).</summary>
    [HttpGet("subjects-progress")]
    public async Task<ActionResult<StudentSubjectsProgressDto>> SubjectsProgress(
        [FromQuery] int? quarter, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var q = quarter ?? 1;
        var cls = await PrimaryGroupAsync(s);
        if (cls is null) return new StudentSubjectsProgressDto(q, 0, 0, 0, new());
        return await SubjectProgressService.ForStudentAsync(db, cls.Id, q);
    }

    /// <summary>Bitta fanga kirilganda — darslar ro'yxati (yashil = o'tilgan, qizil = hali yo'q).</summary>
    [HttpGet("subjects-progress/{subjectId}")]
    public async Task<ActionResult<SubjectProgressDetailDto>> SubjectProgressDetail(
        string subjectId, [FromQuery] int? quarter, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var cls = await PrimaryGroupAsync(s);
        if (cls is null) return NotFound();
        var dto = await SubjectProgressService.ForStudentSubjectAsync(
            db, cls.Id, quarter ?? 1, subjectId);
        return dto is null ? NotFound() : dto;
    }

    /// <summary>O'quvchining O'QUV DASTURI — har bir faol guruh kursi bo'yicha o'tilgan/qolgan bandlar +
    /// foiz + tugash prognozi (Duolingo uslubidagi yo'l-xarita uchun). Guruh kursida dastur bo'lmasa chiqmaydi.</summary>
    [HttpGet("curriculum")]
    public async Task<ActionResult<IEnumerable<GroupCurriculumDto>>> Curriculum([FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        var groupIds = await db.StudentGroups
            .Where(sg => sg.StudentId == s.Id && sg.IsActive)
            .Select(sg => sg.GroupId).Distinct().ToListAsync();
        var groups = await db.Classes
            .Where(c => groupIds.Contains(c.Id) && c.CourseId != "").ToListAsync();

        var result = new List<GroupCurriculumDto>();
        foreach (var g in groups)
        {
            var dto = await CurriculumForecast.BuildGroupAsync(db, g);
            if (dto.TotalItems > 0) result.Add(dto); // faqat dasturi bor kurslar
        }
        return result.OrderByDescending(r => r.TotalItems).ToList();
    }

    /// <summary>Bitta DARS kontentini o'qish (Duolingo node bosilganda ochiladi): video/matn/audio/lug'at/test.
    /// O'quvchi faqat o'z faol guruh(lar)i kursidagi darsni ko'ra oladi.</summary>
    [HttpGet("curriculum/item/{id}")]
    public async Task<ActionResult<CourseItemDetailDto>> CurriculumItem(string id, [FromQuery] string? studentId)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        var i = await db.CourseItems.FindAsync(id);
        if (i is null) return NotFound();

        // O'quvchining faol guruhlari + ularning kurslariga biriktirilgan dasturlar (M2M).
        var myGroups = await (from sg in db.StudentGroups
                              join c in db.Classes on sg.GroupId equals c.Id
                              where sg.StudentId == s.Id && sg.IsActive
                              select new { sg.GroupId, c.CourseId }).ToListAsync();
        var myCourseIds = myGroups.Select(g => g.CourseId).Distinct().ToList();
        var myCurriculumIds = await db.SubjectCurricula
            .Where(sc => myCourseIds.Contains(sc.SubjectId)).Select(sc => sc.CurriculumId).ToListAsync();
        // Faqat o'quvchining kurslariga biriktirilgan dasturlardagi darsni ko'rsatamiz.
        if (!myCurriculumIds.Contains(i.CurriculumId)) return NotFound();

        // NAZORAT: dars FAQAT o'qituvchi shu guruhda "o'tildi" qilgach (GroupCurriculumLog) ochiladi.
        var myGroupIds = myGroups.Select(g => g.GroupId).ToList();
        var covered = await db.GroupCurriculumLogs
            .AnyAsync(g => myGroupIds.Contains(g.GroupId) && g.ItemId == id && !g.IsRevision);
        if (!covered)
            return StatusCode(403, new { message = "Bu dars hali ochilmagan — o'qituvchi o'tgach ochiladi", locked = true });

        var qs = await db.CourseQuestions.Where(q => q.ItemId == id).OrderBy(q => q.Order)
            .Select(q => new CourseQuestionDto(q.Id, q.Text, q.Options, q.CorrectIndex)).ToListAsync();
        var vocab = string.IsNullOrWhiteSpace(i.VocabJson)
            ? new List<VocabEntryDto>()
            : (TryDeserialize(i.VocabJson) ?? new());
        return new CourseItemDetailDto(
            i.Id, i.LessonId, i.Text, i.Note, i.Order, i.Type,
            i.VideoUrl, i.AudioUrl, i.TextContent, i.PdfUrl, i.PdfName,
            i.Meta, vocab, qs, i.ExerciseKind, i.ExerciseJson);
    }

    private static List<VocabEntryDto>? TryDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<List<VocabEntryDto>>(json); }
        catch { return null; }
    }

    /// <summary>O'quvchining BAHOLASH statistikasi (har faol guruh bo'yicha): mezonlarda
    /// OYLIK xulosa (nechta darsda bajardi / jami) + HAR DARSLIK belgilar.</summary>
    [HttpGet("grading")]
    public async Task<ActionResult<IEnumerable<StudentGradingGroupDto>>> Grading(
        [FromQuery] string? studentId, [FromQuery] string? month)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        var groups = await (from sg in db.StudentGroups
                            join c in db.Classes on sg.GroupId equals c.Id
                            where sg.StudentId == s.Id && sg.IsActive
                            select c).ToListAsync();

        var result = new List<StudentGradingGroupDto>();
        var cur = TuitionService.CurrentMonth();
        foreach (var g in groups)
        {
            var assigns = await db.GroupGradingCriteria.Where(x => x.GroupId == g.Id)
                .OrderBy(x => x.Order).ToListAsync();
            if (assigns.Count == 0) continue;
            var critIds = assigns.Select(a => a.CriterionId).ToList();
            var critDict = await db.GradingCriteria.Where(c => critIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            var orderedCrits = assigns.Where(a => critDict.ContainsKey(a.CriterionId))
                .Select(a => critDict[a.CriterionId]).ToList();
            if (orderedCrits.Count == 0) continue;

            var startMonth = !string.IsNullOrEmpty(g.StartDate) && g.StartDate.Length >= 7 ? g.StartDate[..7] : cur;
            if (string.CompareOrdinal(startMonth, cur) > 0) startMonth = cur;
            var months = TuitionService.MonthRange(startMonth, cur).ToList();
            if (months.Count == 0) months.Add(cur);
            var resolved = !string.IsNullOrEmpty(month) && months.Contains(month) ? month! : months[^1];
            var gMoves = (await db.LessonReschedules.Where(r => r.ClassId == g.Id).ToListAsync())
                .Select(m => new JournalService.LessonMove(m.FromDate, m.ToDate)).ToList();
            var dates = JournalService.EffectiveLessonDatesInMonth(g.Days, resolved, gMoves);
            var dateSet = dates.ToHashSet();

            var marks = await db.CriterionGrades
                .Where(x => x.GroupId == g.Id && x.StudentId == s.Id && x.Done && x.Date.StartsWith(resolved))
                .Select(x => new { x.CriterionId, x.Date }).ToListAsync();

            var byCriterion = marks.GroupBy(m => m.CriterionId)
                .ToDictionary(gr => gr.Key, gr => gr.Select(x => x.Date).ToHashSet());
            var criteria = orderedCrits.Select(c => new StudentGradingCriterionDto(
                c.Id, c.Name,
                byCriterion.TryGetValue(c.Id, out var ds) ? ds.Count(d => dateSet.Contains(d)) : 0,
                dates.Count)).ToList();

            var byDate = marks.GroupBy(m => m.Date)
                .ToDictionary(gr => gr.Key, gr => gr.Select(x => x.CriterionId).ToList());
            var lessons = dates.Select(d => new StudentGradingDateDto(
                d, byDate.TryGetValue(d, out var cids) ? cids : new List<string>())).ToList();

            // Yig'ilgan ball: shu oyda (dars sanalari bo'yicha) va shu guruhda BARCHA vaqt bo'yicha.
            var monthBall = criteria.Sum(c => c.Done);
            var totalBall = await db.CriterionGrades.CountAsync(x =>
                x.GroupId == g.Id && x.StudentId == s.Id && x.Done && critIds.Contains(x.CriterionId));

            result.Add(new StudentGradingGroupDto(
                g.Id, g.Name, months, resolved, dates, criteria, lessons, monthBall, totalBall));
        }
        return result;
    }

    // ---------- Support (yordam darslari — bo'sh vaqtga bron) ----------

    /// <summary>Support ekrani: bo'sh slotli support o'qituvchilar + o'quvchining o'z bronlari.</summary>
    [HttpGet("support")]
    public async Task<ActionResult<StudentSupportDto>> Support([FromQuery] string? studentId = null)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var supports = await db.Teachers.Where(t => t.IsSupport && !t.IsArchived)
            .OrderBy(t => t.FullName).ToListAsync();
        var supIds = supports.Select(t => t.Id).ToList();

        // Bo'sh + bugundan keyingi slotlar (sana ISO bo'lgani uchun matn taqqoslash to'g'ri).
        var openSlots = (await db.SupportSlots
                .Where(x => supIds.Contains(x.TeacherId) && x.Status == "open").ToListAsync())
            .Where(x => string.CompareOrdinal(x.Date, today) >= 0)
            .OrderBy(x => x.Date).ThenBy(x => x.StartTime).ToList();
        var openByTeacher = openSlots.GroupBy(x => x.TeacherId).ToDictionary(g => g.Key, g => g.ToList());

        // Har support o'qituvchi uchun dars beradigan kurslar nomini bitta so'rovda olamiz (N+1 dan qochish).
        var teacherCourseNames = await (
            from c in db.Classes
            join sub in db.Subjects on c.CourseId equals sub.Id
            where supIds.Contains(c.TeacherId)
            select new { c.TeacherId, sub.Name }
        ).ToListAsync();
        var subjectByTeacher = teacherCourseNames
            .GroupBy(x => x.TeacherId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(x => x.Name).Distinct()));

        var supDtos = supports
            .Select(t => new StudentSupportTeacherDto(t.Id, t.FullName, t.PhotoUrl,
                subjectByTeacher.GetValueOrDefault(t.Id, ""),
                (openByTeacher.GetValueOrDefault(t.Id) ?? new())
                    .Select(x => new StudentSupportSlotDto(x.Id, x.Date, x.StartTime, x.EndTime)).ToList()))
            .Where(d => d.OpenSlots.Count > 0)
            .ToList();

        // Mening bronlarim (barcha holatlar — o'tilganlari mavzu/izoh bilan).
        var mine = await db.SupportSlots.Where(x => x.StudentId == s.Id)
            .OrderByDescending(x => x.Date).ThenBy(x => x.StartTime).ToListAsync();
        var tNames = (await db.Teachers.Where(t => mine.Select(m => m.TeacherId).Contains(t.Id)).ToListAsync())
            .ToDictionary(t => t.Id, t => t.FullName);
        var myBookings = mine.Select(x => new StudentSupportBookingDto(
            x.Id, x.TeacherId, tNames.GetValueOrDefault(x.TeacherId, ""), x.Date, x.StartTime, x.EndTime,
            x.Status, x.Topic, x.Notes)).ToList();

        return new StudentSupportDto(supDtos, myBookings);
    }

    /// <summary>Bo'sh slotni bron qilish.</summary>
    [HttpPost("support/slots/{id}/book")]
    public async Task<IActionResult> BookSupport(string id, [FromQuery] string? studentId = null)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var bookedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        // ATOMIK bron: faqat hali "open" va egasi yo'q slot yangilanadi. Ikki o'quvchi bir vaqtda bron
        // qilsa — faqat bittasi 1 qator yangilaydi, ikkinchisi 0 qator oladi → "band qilingan" (race-safe).
        var affected = await db.SupportSlots
            .Where(x => x.Id == id && x.Status == "open" && x.StudentId == null)
            .ExecuteUpdateAsync(up => up
                .SetProperty(x => x.StudentId, s.Id)
                .SetProperty(x => x.Status, "booked")
                .SetProperty(x => x.BookedAt, bookedAt));
        if (affected == 0)
            return await db.SupportSlots.AnyAsync(x => x.Id == id)
                ? BadRequest(new { message = "Bu vaqt allaqachon band qilingan" })
                : NotFound();
        return NoContent();
    }

    /// <summary>O'z bronini bekor qilish (o'tilgan darsdan tashqari) — slot yana bo'sh bo'ladi.</summary>
    [HttpPost("support/slots/{id}/cancel")]
    public async Task<IActionResult> CancelSupport(string id, [FromQuery] string? studentId = null)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();
        var slot = await db.SupportSlots.FindAsync(id);
        if (slot is null || slot.StudentId != s.Id) return NotFound();
        if (slot.Status == "done") return BadRequest(new { message = "O'tilgan darsni bekor qilib bo'lmaydi" });
        slot.StudentId = null;
        slot.Status = "open";
        slot.BookedAt = null;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- O'quv dasturi (curriculum) progress ----------

    /// <summary>O'quvchining shu kursda o'tilgan (Done=true) bandlar id'larini qaytaradi.</summary>
    [HttpGet("curriculum/{courseId}/progress")]
    public async Task<ActionResult<string[]>> GetCourseProgress(string courseId, [FromQuery] string? studentId)
    {
        // DIQQAT: progress/urinishlar HAR DOIM Student.Id bilan yoziladi (AppUser.Id bilan EMAS) —
        // admin tomoni (CurriculumController) ham shu kalit bo'yicha o'qiydi.
        var s = await TargetAsync(studentId);
        if (s is null) return Unauthorized();

        // O'quvchi shu kursda faol guruhda bo'lganini tekshirish
        var hasAccess = await db.StudentGroups
            .AnyAsync(sg => sg.StudentId == s.Id
                         && sg.IsActive
                         && sg.Status != "frozen"
                         && db.Classes.Any(c => c.Id == sg.GroupId && c.CourseId == courseId));

        if (!hasAccess) return Forbid();

        // Progress kontentga (dastur) tegishli — kursga biriktirilgan dasturlar orqali resolve qilinadi.
        var curriculumIds = await db.SubjectCurricula
            .Where(sc => sc.SubjectId == courseId).Select(sc => sc.CurriculumId).ToListAsync();
        var done = await db.CourseProgresses
            .Where(p => p.StudentId == s.Id && p.Done)
            .Join(db.CourseItems.Where(i => curriculumIds.Contains(i.CurriculumId)),
                p => p.ItemId, i => i.Id, (p, i) => p.ItemId)
            .ToListAsync();

        return done.ToArray();
    }

    /// <summary>O'quvchi shu topshiriqni ochish huquqiga egami — dastur uning faol guruhi kursiga
    /// biriktirilganmi. Ha bo'lsa (guruh id, dastur bandi) qaytaradi.</summary>
    private async Task<string?> GroupForItemAsync(string studentId, string curriculumId) =>
        await (from sg in db.StudentGroups
               join c in db.Classes on sg.GroupId equals c.Id
               where sg.StudentId == studentId && sg.IsActive
                     && db.SubjectCurricula.Any(sc => sc.SubjectId == c.CourseId && sc.CurriculumId == curriculumId)
               select sg.GroupId).FirstOrDefaultAsync();

    /// <summary>Dars progresini yangilash (o'tildi/o'tilmadi) — upsert. Faqat student rolida.</summary>
    [HttpPost("curriculum/progress")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult> SetCourseProgress([FromBody] SetCourseProgressRequest req)
    {
        var me = await MeAsync();
        if (me is null) return Unauthorized();

        // ItemId → CurriculumId topish
        var item = await db.CourseItems.FirstOrDefaultAsync(i => i.Id == req.ItemId);
        if (item is null) return NotFound(new { message = "Dars topilmadi" });

        // Access control — o'quvchi shu topshiriq dasturi biriktirilgan biror kursda faol guruhda
        // bo'lishi kerak (dastur bir nechta kursga biriktirilgan bo'lishi mumkin — biri yetarli).
        var groupId = await GroupForItemAsync(me.Id, item.CurriculumId);
        if (groupId is null) return Forbid();

        await UpsertProgressAsync(me.Id, item, req.Done);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>CourseProgress upsert — haqiqiy kalit (StudentId, ItemId); CurriculumId faqat
    /// filtrlash uchun denormalized. SaveChanges CHAQIRUVCHIDA qilinadi.</summary>
    private async Task UpsertProgressAsync(string studentId, CourseItem item, bool done)
    {
        var existing = await db.CourseProgresses
            .FirstOrDefaultAsync(p => p.StudentId == studentId && p.ItemId == item.Id);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        if (existing is not null)
        {
            existing.Done = done;
            existing.UpdatedAt = now;
        }
        else
        {
            db.CourseProgresses.Add(new CourseProgress
            {
                StudentId = studentId,
                ItemId = item.Id,
                CurriculumId = item.CurriculumId,
                Done = done,
                UpdatedAt = now,
            });
        }
    }

    // ---------- Topshiriq URINISHLARI (o'quvchi natijasi saqlanadigan joy) ----------

    /// <summary>
    /// O'quvchi topshiriqni (mashq / test / ko'rish) yakunlaganda chaqiriladi — natija
    /// <see cref="CourseItemAttempt"/> ga TARIX sifatida yoziladi (har chaqiruv yangi urinish)
    /// va <see cref="CourseProgress"/> avtomatik "bajarildi" bo'ladi.
    /// Faqat student rolida — admin boshqa nomdan urinish yoza olmaydi.
    /// </summary>
    [HttpPost("curriculum/attempt")]
    [Authorize(Roles = "student")]
    public async Task<ActionResult<SaveAttemptResponse>> SaveAttempt([FromBody] SaveAttemptRequest req)
    {
        var me = await MeAsync();
        if (me is null) return Unauthorized();

        var item = await db.CourseItems.FirstOrDefaultAsync(i => i.Id == req.ItemId);
        if (item is null) return NotFound(new { message = "Topshiriq topilmadi" });

        var groupId = await GroupForItemAsync(me.Id, item.CurriculumId);
        if (groupId is null) return Forbid();

        var section = req.Section is "exercise" or "test" or "view" ? req.Section : "exercise";


        // Nechanchi urinish — shu (o'quvchi, topshiriq, bo'lim) bo'yicha.
        var attemptNo = await db.CourseItemAttempts
            .CountAsync(a => a.StudentId == me.Id && a.ItemId == item.Id && a.Section == section) + 1;

        var total = Math.Max(0, req.Total);
        var correct = Math.Clamp(req.Correct, 0, total);
        var finishedAt = AppClock.Now;
        var duration = Math.Clamp(req.DurationSec, 0, 24 * 60 * 60);

        // Javoblar tafsiloti — kiruvchi ma'lumot qirqiladi va QAYTA seriyalanadi (xom JSON'ga
        // ishonmaymiz: uzunlik cheklovi + faqat kutilgan maydonlar bazaga tushadi).
        var answers = (req.Answers ?? new()).Take(500).Select((a, i) => new AttemptAnswerDto(
            a.Index > 0 ? a.Index : i,
            Trim(a.Prompt, 500),
            Trim(a.Answer, 500),
            Trim(a.Expected, 500),
            a.Ok,
            Math.Clamp(a.Sec, 0, 60 * 60))).ToList();

        db.CourseItemAttempts.Add(new CourseItemAttempt
        {
            StudentId = me.Id,
            ItemId = item.Id,
            CurriculumId = item.CurriculumId,
            LessonId = item.LessonId,
            GroupId = groupId,
            Section = section,
            ExerciseKind = section == "exercise" ? Trim(req.ExerciseKind, 60) : "",
            AttemptNo = attemptNo,
            Correct = correct,
            Total = total,
            ScorePct = total > 0 ? (int)Math.Round(correct * 100.0 / total) : 0,
            DurationSec = duration,
            AnswersJson = answers.Count > 0 ? JsonSerializer.Serialize(answers) : "",
            StartedAt = finishedAt.AddSeconds(-duration),
            FinishedAt = finishedAt,
        });

        // Ishlagan bo'lsa — band avtomatik "bajarildi" (ilgari faqat admin qo'lda belgilardi).
        await UpsertProgressAsync(me.Id, item, true);
        await db.SaveChangesAsync();

        return new SaveAttemptResponse(attemptNo);
    }

    /// <summary>O'quvchining shu topshiriq bo'yicha o'z urinishlari (eng yangisidan) — ilovada
    /// "oldingi natijalarim" ko'rinishi uchun.</summary>
    [HttpGet("curriculum/item/{itemId}/attempts")]
    public async Task<ActionResult<IEnumerable<MyAttemptDto>>> MyAttempts(string itemId, [FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return Unauthorized();

        return await db.CourseItemAttempts.AsNoTracking()
            .Where(a => a.StudentId == s.Id && a.ItemId == itemId)
            .OrderByDescending(a => a.FinishedAt)
            .Take(50)
            .Select(a => new MyAttemptDto(
                a.Id, a.Section, a.ExerciseKind, a.AttemptNo,
                a.Correct, a.Total, a.ScorePct, a.DurationSec, a.FinishedAt))
            .ToListAsync();
    }

    private static string Trim(string? v, int max)
    {
        var t = (v ?? "").Trim();
        return t.Length <= max ? t : t[..max];
    }

    public class SetCourseProgressRequest
    {
        public string ItemId { get; set; } = "";
        public bool Done { get; set; }
    }
}
