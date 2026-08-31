using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Infrastructure.Data;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;
using IntellectCRM.Application.Services;
using System.Security.Claims;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// O'qituvchi ilovasi uchun API — faqat "teacher" roli (web admin'ga tegishli emas).
/// Har amal tokendagi foydalanuvchidan o'qituvchini aniqlaydi va faqat o'ziga tegishli
/// ma'lumotni ko'rsatadi/o'zgartiradi. Jurnalga yozish faqat o'qituvchining o'zi dars
/// beradigan guruh+fan uchun ruxsat etiladi (boshqasiga 403).
/// </summary>
[ApiController]
[Authorize(Roles = "teacher")]
[Route("api/teacher")]
public class TeacherPortalController(
    AppDbContext db, ChatService chat, IWebHostEnvironment env, ReferenceCache refCache,
    FcmService fcm, AutoMessageService autoMsg, ContractService contracts,
    TestCertificateService testCerts, TestCertificateJobs testCertJobs,
    ContactQueueService queue) : ControllerBase
{
    /// <summary>"Darsga kelmadi" avto-xabari (attendance_absent) — o'quvchi(lar)ga guruh+sabab bilan.
    /// Exception yutiladi (jurnal javobini bloklamaydi).</summary>
    private async Task DispatchAbsencesAsync(string classId, string date, string? reasonId, IEnumerable<string> studentIds)
    {
        if (string.IsNullOrWhiteSpace(reasonId)) return;
        var cls = await db.Classes.FindAsync(classId);
        var groupName = cls?.Name ?? "";
        var reasonName = (await db.AbsenceReasons.FindAsync(reasonId))?.Name ?? "Sababsiz";
        foreach (var sid in studentIds.Distinct())
        {
            var s = await db.Students.FindAsync(sid);
            if (s is not null)
                await autoMsg.DispatchAttendanceAbsentAsync(db, s, groupName, reasonName, date, cls);
        }
    }

    /// <summary>Tokendagi foydalanuvchi id'si bo'yicha joriy o'qituvchini topadi.</summary>
    private async Task<Teacher?> Me()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return uid is null ? null : await db.Teachers.FirstOrDefaultAsync(t => t.UserId == uid);
    }

    /// <summary>O'qituvchi shu guruhda shu kursni (fan) o'qitadimi? Biriktirish to'g'ridan-to'g'ri
    /// guruhda: Group.TeacherId (o'qituvchi) yoki vaqtincha o'rinbosar biriktiruvi.
    /// <para>Arxivlangan/tugatilgan va vaqtincha bloklangan guruh o'qituvchi uchun UMUMAN yo'q —
    /// qoida <see cref="TeacherGroupAccess"/> da (ro'yxat bilan bitta manbadan).</para>
    ///
    /// <para>⚠️ <paramref name="date"/> — AYNAN yozilayotgan/o'qilayotgan SANA. O'rinbosar uchun
    /// darvoza SHU sanaga qarab ochiladi (<c>SubstituteTeacherService.CanSubstituteWriteAsync</c>):
    /// ilgari tekshiruvga sana umuman uzatilmasdi va ichkarida <c>AppClock.Today</c> o'qilardi —
    /// natijada tayinlangan kuni o'rinbosar guruhning O'TGAN ISTALGAN kunidagi baho/davomatini
    /// o'zgartira olardi, tayinlov tugagach esa o'z xatosini tuzata olmasdi.</para>
    ///
    /// <para><paramref name="date"/> berilmasa (guruh darajasidagi amal, masalan "Aloqa" tabi) —
    /// BUGUN olinadi.</para></summary>
    private async Task<bool> Teaches(string teacherId, string classId, string subjectId, string? date = null)
    {
        var g = await db.Classes.FindAsync(classId);
        if (g == null || !TeacherGroupAccess.Visible(g)) return false;

        var isOwner = TeacherGroupAccess.OwnedBy(g, teacherId);
        var isSubstitute = !isOwner && await SubstituteTeacherService.CanSubstituteWriteAsync(
            db, teacherId, classId, date ?? AppClock.Today.ToString("yyyy-MM-dd"));

        return (isOwner || isSubstitute) && (g.CourseId == subjectId || string.IsNullOrEmpty(subjectId));
    }

    // ---------- Profil ----------

    [HttpGet("me")]
    public async Task<ActionResult<TeacherProfileDto>> Profile()
    {
        var t = await Me();
        if (t is null) return NotFound();
        var user = t.UserId is null ? null : await db.Users.FindAsync(t.UserId);
        var names = (await db.Subjects.Where(s => t.SubjectIds.Contains(s.Id)).ToListAsync())
            .Select(s => new SubjectDto(s.Id, s.Name, s.Price))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new TeacherProfileDto(t.Id, t.FullName, user?.Email ?? "", t.HomeroomClass, names, t.Permissions, t.PhotoUrl, t.IsSupport);
    }

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
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();
        var unread = await db.UserNotifications.Where(n => n.UserId == uid && n.ReadAt == null).ToListAsync();
        foreach (var n in unread) n.ReadAt = AppClock.Now;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bitta bildirishnomani TASDIQLAYDI — admin shu holatni ko'radi.</summary>
    [HttpPost("notifications/{id}/confirm")]
    public async Task<IActionResult> ConfirmNotification(string id)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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

    /// <summary>O'qituvchining o'z shartnomalari, eng yangisi birinchi. Faqat superadmin PDF nusxasini
    /// YUKLAGAN va yashirmagan yozuvlar ko'rinadi.</summary>
    [HttpGet("contracts")]
    public async Task<ActionResult<IEnumerable<ContractDocDto>>> Contracts()
    {
        var t = await Me();
        if (t is null) return NotFound();
        var items = await db.Contracts.AsNoTracking()
            .Where(c => c.Target == "staff" && c.RecipientKey == t.Id && c.Visible && c.PdfUrl != "")
            .OrderByDescending(c => c.Number).ToListAsync();
        return items.Select(ContractService.ToDoc).ToList();
    }

    /// <summary>Shartnoma PDF nusxasi. Faqat o'ziniki.</summary>
    [HttpGet("contracts/{id}/pdf")]
    public async Task<IActionResult> ContractPdf(string id)
    {
        var t = await Me();
        if (t is null) return NotFound();
        var c = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == id && x.Target == "staff" && x.RecipientKey == t.Id && x.Visible);
        if (c is null) return NotFound();
        var path = contracts.ResolveUpload(c.PdfUrl);
        if (path is null) return NotFound(new { message = "Shartnoma fayli topilmadi" });
        return PhysicalFile(path, "application/pdf", $"shartnoma-{c.Number}.pdf");
    }

    // ---------- Push qurilma (bildirishnoma) ----------

    /// <summary>Qurilma push tokenini ro'yxatdan o'tkazadi (token, platform, qurilma nomi, app_id).</summary>
    [HttpPost("notifications/register")]
    public async Task<ActionResult> RegisterDevice(RegisterDeviceRequest req)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Qurilma tokenini o'chiradi (logout). Topilmasa ham 200.</summary>
    [HttpDelete("notifications/register")]
    public async Task<ActionResult> UnregisterDevice([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest(new { message = "Token bo'sh" });
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var d = await db.DeviceTokens.FirstOrDefaultAsync(x => x.Token == token && x.UserId == uid);
        if (d is not null) { db.DeviceTokens.Remove(d); await db.SaveChangesAsync(); }
        return Ok(new { ok = true });
    }

    // ---------- Dars beradigan guruhlar ----------

    [HttpGet("classes")]
    public async Task<ActionResult<IEnumerable<TeacherClassDto>>> Classes()
    {
        var t = await Me();
        if (t is null) return NotFound();

        var subjectNames = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);

        // O'rinbosar sifatida BUGUN kirish huquqi bor guruhlar. Qoida YAGONA joyda
        // (SubstituteTeacherService) — ro'yxat va guruhga kirish darvozasi (Teaches) ilgari
        // ikki xil ishlar edi: bu yerda faqat ORALIQ tekshirilib, tanlangan sanalar
        // (SelectedDates) e'tiborga olinmasdi.
        var substituteGroupIds = await SubstituteTeacherService.SubstituteGroupIdsAsync(db, t.Id);

        // FAQAT FAOL GURUHLAR. Arxivlangan/tugatilgan (sertifikat bilan yopilgan) va vaqtincha
        // bloklangan guruhlar o'qituvchida UMUMAN ko'rinmaydi — qoida TeacherGroupAccess da,
        // guruh ichiga kirish darvozasi (ResolveOwnedGroup/Teaches) ham AYNAN shunga tayanadi.
        var classes = await db.Classes.AsNoTracking()
            .Where(c => c.TeacherId == t.Id || substituteGroupIds.Contains(c.Id))
            .Where(TeacherGroupAccess.VisibleQuery)
            .ToListAsync();

        // O'qituvchi qaysi guruhda qaysi kursni o'qitishi to'g'ridan-to'g'ri guruhda: Group.TeacherId + Group.CourseId.
        var result = new List<TeacherClassDto>();
        foreach (var cls in classes)
        {
            var subjects = string.IsNullOrEmpty(cls.CourseId)
                ? new List<SubjectDto>()
                : new List<SubjectDto> { new(cls.CourseId, subjectNames.GetValueOrDefault(cls.CourseId, "")) };
            // Kursi biriktirilmagan guruh o'qituvchi ilovasida ish bermaydi (jurnal kurs bo'yicha
            // ochiladi) — ilgari ham ro'yxatga tushmasdi, shu xatti-harakat saqlanadi.
            if (subjects.Count == 0) continue;
            result.Add(new TeacherClassDto(cls.Id, cls.Name, cls.Grade, subjects));
        }
        return result.OrderBy(c => c.Grade).ThenBy(c => c.ClassName).ToList();
    }

    /// <summary>
    /// O'qituvchining o'rinbosar o'qituvchi tayinlovlari (o'rinbosar yoki almashtirilgan holatda).
    ///
    /// <para>⚠️ <b>PUL MAYDONLARI DARVOZALANGAN</b> (<c>TeacherPermissions.Salary</c>). Filtr
    /// <c>SubstituteTeacherId == me || OriginalTeacherId == me</c> — ya'ni javobda ASOSIY
    /// o'qituvchi ham qatnashadi, va u yerda o'rinbosarga to'lanadigan haq turadi. Ilgari bu
    /// endpointda <c>Salary</c> tekshiruvi UMUMAN yo'q edi (qiyoslang: <c>salary</c> va
    /// <c>retention-bonus</c>) — maoshni ko'rish taqiqlangan o'qituvchi ham, hatto begona
    /// odamning haqini ham ko'raverardi.</para>
    ///
    /// <para>403 QAYTARILMAYDI, javob TOZALANADI (`uploads-security.md` dagi "javobni tozalash"
    /// siyosati): ro'yxatning O'ZI ("qaysi kuni qaysi guruhda dars o'taman") pul ma'lumoti emas
    /// va u modulning ishlashi uchun kerak. Pul maydonlari 0 bo'lib qaytadi.</para>
    ///
    /// <para>Nol yig'indili modelda <c>EstimatedSalary</c> va <c>EstimatedDeduction</c> TENG,
    /// shuning uchun "birini yashirib, ikkinchisini ko'rsatish" ma'nosiz — ikkalasi birga
    /// darvozalanadi.</para>
    /// </summary>
    [HttpGet("substitutions")]
    public async Task<IActionResult> MySubstitutions()
    {
        var t = await Me();
        if (t is null) return NotFound();

        // FAQAT FAOL tayinlovlar: bekor qilingani o'qituvchida "menda dars bor" bo'lib
        // ko'rinib turardi (ro'yxatda holat ustuni ham yo'q).
        var list = await SubstituteTeacherService.GetAssignmentsAsync(db, teacherId: t.Id, isActive: true);

        if (!t.Permissions.Contains(TeacherPermissions.Salary))
            list = list.Select(x => x with
            {
                EstimatedSalary = 0m,
                EstimatedDeduction = 0m,
                PerLessonFee = 0m,
                StudentCount = 0,
            }).ToList();

        return Ok(list);
    }

    // ---------- O'quvchilar reytingi (faqat o'z guruhlari) ----------

    /// <summary>O'qituvchi guruhlaridagi o'quvchilar ball bo'yicha reytingi.
    /// <para><c>month</c> ("yyyy-MM") IXTIYORIY — berilsa shu oy kesimi, bo'sh/berilmagan bo'lsa
    /// AVVALGIDEK Umumiy. Guruhlar baribir <c>Me()</c> orqali FAQAT o'ziniki: oy parametri
    /// ko'rish doirasini kengaytirmaydi.</para></summary>
    [HttpGet("rating")]
    public async Task<ActionResult<TeacherRatingDto>> Rating([FromQuery] string? month = null)
    {
        var t = await Me();
        if (t is null) return NotFound();
        return await StudentBallService.TeacherAsync(db, t, month);
    }

    // ---------- Maosh (faqat o'ziniki) ----------

    [HttpGet("salary")]
    public async Task<ActionResult<SalaryLedgerDto>> Salary([FromQuery] string? from, [FromQuery] string? to)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Salary)) return Forbid();
        return await SalaryLedger.BuildAsync(db, t, from, to);
    }

    /// <summary>
    /// O'quvchini ushlab turish bonuslari (faqat o'ziniki): berilganlar (<c>items</c>) va hali
    /// oylari to'planayotgan sikllar (<c>inProgress</c> — "yangi o'quvchilarim qanday hisoblanyapti").
    /// Maosh jadvaliga QO'SHILMAYDI — alohida bo'lim sifatida ko'rsatiladi (bonus
    /// <c>SalaryLedger</c> ga ulanmagan). Maosh ruxsati bilan bir xil darvoza: bonus ham pul ma'lumoti.
    /// </summary>
    [HttpGet("retention-bonus")]
    public async Task<ActionResult<TeacherRetentionSummaryDto>> RetentionBonus(CancellationToken ct)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Salary)) return Forbid();
        return await RetentionBonusService.ForTeacherAsync(db, t.Id, ct);
    }

    // ---------- Jurnal katak (PUT/DELETE) ----------
    // ZAMONAVIY oylik guruh jurnali (journal/group) shu ikki endpointni quarter/period opaque=1 bilan
    // ishlatadi (bitta katakni belgilash/tozalash). Eski chorak GET endpointlari olib tashlandi.

    [HttpPut("journal")]
    public async Task<IActionResult> SetEntry(SetJournalEntryRequest req)
    {
        // K3: darvozaga AYNAN yozilayotgan SANA uzatiladi — o'rinbosar faqat o'zi o'tgan
        // (va tuzatish oynasidagi) kunga yoza oladi.
        if (!await Authorized(req.ClassId, req.SubjectId, req.Date)) return Forbid();
        // Hali o'tilmagan (sanasi kelmagan) darsga baho/jurnal kiritib bo'lmaydi.
        if (string.CompareOrdinal(req.Date, AppClock.Now.ToString("yyyy-MM-dd")) > 0)
            return BadRequest(new { message = "Dars hali o'tilmagan — kelajakdagi sanaga baho qo'yib bo'lmaydi" });
        // Jurnal siyosati (admin "Guruhlar → Jurnal boshqaruvi"): sana oynasi / faqat o'tilgan dars.
        // skipConducted: bitta katakka baho/davomat kiritish DARSNI O'ZI "o'tildi" qiladi (SetEntryAsync
        // ichida) — bulk-attendance bilan bir xil, shuning uchun "hali o'tilmagan" taqig'i bunga qo'llanmaydi.
        var deny = await JournalPolicy.CheckAsync(db, req.ClassId, req.SubjectId, req.Date, req.Period,
            isAdmin: false, skipConducted: true);
        if (deny is not null) return BadRequest(new { message = deny });
        // To'lov "darvozasi": o'qituvchi ko'rmayotgan (yashirilgan) o'quvchiga yozib bo'lmaydi.
        var hidden = await JournalPolicy.PaymentHiddenStudentsAsync(db, req.ClassId, new[] { req.StudentId });
        if (hidden.Contains(req.StudentId))
            return BadRequest(new { message = JournalPolicy.PaymentHiddenMessage });
        var newAbsence = await JournalService.SetEntryAsync(db, req, fcm, autoMsg);
        if (newAbsence)
            await DispatchAbsencesAsync(req.ClassId, req.Date, req.ReasonId, new[] { req.StudentId });
        return NoContent();
    }

    [HttpDelete("journal")]
    public async Task<IActionResult> ClearEntry(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter,
        [FromQuery] string studentId, [FromQuery] string date, [FromQuery] int period)
    {
        // K3: tozalash ham AYNAN o'sha sana bo'yicha darvozalanadi.
        if (!await Authorized(classId, subjectId, date)) return Forbid();
        // Tozalash ham sana oynasiga bo'ysunadi (yopiq davr yozuvini o'chirib ham bo'lmaydi).
        var deny = await JournalPolicy.CheckAsync(db, classId, subjectId, date, period,
            isAdmin: false, skipConducted: true);
        if (deny is not null) return BadRequest(new { message = deny });
        // To'lov "darvozasi": yashirilgan o'quvchining yozuvini o'qituvchi tozalay ham olmaydi.
        var hidden = await JournalPolicy.PaymentHiddenStudentsAsync(db, classId, new[] { studentId });
        if (hidden.Contains(studentId))
            return BadRequest(new { message = JournalPolicy.PaymentHiddenMessage });
        await JournalService.ClearEntryAsync(db, classId, subjectId, quarter, studentId, date, period);
        return NoContent();
    }

    /// <summary>Joriy o'qituvchida shu bo'limga (perm) ruxsat bormi.</summary>
    private async Task<bool> HasPerm(string perm)
    {
        var t = await Me();
        return t is not null && t.Permissions.Contains(perm);
    }

    /// <summary>Jurnal ruxsati + shu guruh+fanga SHU SANADA dars beradimi.</summary>
    private async Task<bool> Authorized(string classId, string subjectId, string? date = null)
    {
        var t = await Me();
        return t is not null && t.Permissions.Contains(TeacherPermissions.Journal)
            && await Teaches(t.Id, classId, subjectId, date);
    }

    /// <summary>
    /// GURUHGA KIRISHNING YAGONA DARVOZASI. Guruh joriy o'qituvchinikimi (<c>Owns</c>) va/yoki u
    /// shu guruhda O'RINBOSARmi (<c>IsSubstitute</c>). Topilmasa <c>Group == null</c>.
    ///
    /// <para>EGALIK YETARLI EMAS: guruh arxivlangan/tugatilgan yoki admin tomonidan vaqtincha
    /// bloklangan bo'lsa <c>Owns=false</c> qaytadi (403). Aks holda guruh ro'yxatdan yo'qolsa ham,
    /// eski havola/keshlangan id bilan jurnalga yozib ketish mumkin bo'lardi.</para>
    ///
    /// <para>⚠️ <c>IsSubstitute</c> — O'QISH darajasidagi bayroq (<c>CanSubstituteReadAsync</c>:
    /// tayinlov ko'rish oynasiga tushadimi). YOZISH uchun bu YETARLI EMAS — u AYNAN sana bo'yicha
    /// <see cref="SubstituteWrite"/> bilan tekshiriladi. Ilgari bu metod o'rinbosarlikni umuman
    /// bilmasdi: o'rinbosar guruhni ro'yxatda ko'rar, bosar va 403 olardi — modulning asosiy
    /// stsenariysi ishlamasdi.</para>
    ///
    /// <para>Har bir chaqiruvchi o'rinbosarga NIMA ochilishini O'ZI hal qiladi (jadval:
    /// <c>.claude/rules/substitute-teachers.md</c> §3).</para>
    /// </summary>
    private async Task<(Teacher? Me, Group? Group, bool Owns, bool IsSubstitute)> ResolveOwnedGroup(string classId)
    {
        var t = await Me();
        if (t is null) return (null, null, false, false);
        var g = await db.Classes.FindAsync(classId);
        if (g is null) return (t, null, false, false);
        var owns = TeacherGroupAccess.OwnedBy(g, t.Id);
        var sub = !owns && TeacherGroupAccess.Visible(g)
            && await SubstituteTeacherService.CanSubstituteReadAsync(db, t.Id, classId);
        return (t, g, owns, sub);
    }

    /// <summary>O'rinbosar AYNAN shu SANAdagi ishni o'zgartira oladimi (tuzatish oynasi bilan).</summary>
    private Task<bool> SubstituteWrite(string teacherId, string classId, string? date) =>
        SubstituteTeacherService.CanSubstituteWriteAsync(db, teacherId, classId, date);

    // ---------- ZAMONAVIY: Guruh OYLIK jurnali + sillabus o'tilishi (admin bilan bir xil, o'qituvchiga skoplangan) ----------
    // Yangi monthly model: guruh dars kunlari bo'yicha avtomatik ustunlar + sillabus o'tilishi/prognoz.
    // Faqat guruh EGASI (Group.TeacherId == me) kirishi mumkin (aks holda 403/404).

    /// <summary>Guruhning bitta OYLIK jurnali (admin <c>GET /admin/journal/group</c> bilan bir xil), faqat o'z guruhi uchun.</summary>
    [HttpGet("journal/group")]
    public async Task<ActionResult<GroupJournalDto>> JournalGroupMonth(
        [FromQuery] string classId, [FromQuery] string? month)
    {
        var (t, g, owns, isSub) = await ResolveOwnedGroup(classId);
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        if (g is null) return NotFound(new { message = "Guruh topilmadi" });
        // O'RINBOSARGA OCHIQ (o'qish): dars o'tayotgan odam jurnalni ko'rmasa davomat ham qo'ya
        // olmaydi — bu modulning asosiy stsenariysi. Yozish esa AYNAN SANA bo'yicha darvozalanadi.
        if (!owns && !isSub) return Forbid();
        var result = await JournalService.GroupMonthAsync(db, classId, month);
        if (result is null) return NotFound(new { message = "Guruh topilmadi" });
        // TO'LOV "DARVOZASI": to'lov qilmagan o'quvchi o'qituvchi jurnalida UMUMAN ko'rinmasin —
        // qatori ham, uning yozuvlari (baho/davomat) ham javobdan olib tashlanadi. Admin jurnali
        // (JournalController) tegilmaydi — u hammani ko'radi. To'lov kelishi bilan qator o'zi qaytadi.
        var hiddenIds = result.Students.Where(s => s.PaymentHidden).Select(s => s.StudentId).ToHashSet();
        if (hiddenIds.Count > 0)
            result = result with
            {
                Students = result.Students.Where(s => !hiddenIds.Contains(s.StudentId)).ToList(),
                Entries = result.Entries.Where(e => !hiddenIds.Contains(e.StudentId)).ToList(),
            };
        return result;
    }

    /// <summary>Bitta dars (sana) uchun BARCHA o'quvchiga birdan davomat (admin bilan bir xil), faqat o'z guruhi uchun.</summary>
    [HttpPost("journal/bulk-attendance")]
    public async Task<IActionResult> JournalBulkAttendance(BulkAttendanceRequest req)
    {
        var (t, g, owns, _) = await ResolveOwnedGroup(req.ClassId);
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        if (g is null) return NotFound(new { message = "Guruh topilmadi" });
        // O'RINBOSARGA OCHIQ (yozish) — davomat aynan uning ishi. Lekin FAQAT o'zi o'tgan kunga
        // (tuzatish oynasi bilan): darvozaga req.Date uzatiladi, "bugun" emas.
        if (!owns && !await SubstituteWrite(t.Id, req.ClassId, req.Date)) return Forbid();
        // Jurnal siyosati: sana oynasi (skipConducted — davomat darsni o'zi "o'tildi" qiladi).
        var deny = await JournalPolicy.CheckAsync(db, req.ClassId, req.SubjectId, req.Date, req.Period,
            isAdmin: false, skipConducted: true);
        if (deny is not null) return BadRequest(new { message = deny });
        // To'lov "darvozasi": yashirilgan o'quvchilar CHETLAB O'TILADI (butun amal rad etilmaydi —
        // qolganlarga davomat odatdagidek yoziladi). O'qituvchi ularni ro'yxatda ko'rmaydi ham.
        var hidden = await JournalPolicy.PaymentHiddenStudentsAsync(db, req.ClassId, req.StudentIds);
        if (hidden.Count > 0)
        {
            var allowed = req.StudentIds.Where(id => !hidden.Contains(id)).ToList();
            if (allowed.Count == 0) return NoContent();
            req = req with { StudentIds = allowed };
        }
        var absentReasonId = await JournalService.BulkAttendanceAsync(db, req);
        if (absentReasonId is not null)
            await DispatchAbsencesAsync(req.ClassId, req.Date, absentReasonId, req.StudentIds);
        return NoContent();
    }

    /// <summary>Bitta darsni bir martalik boshqa kunga ko'chiradi (admin bilan bir xil), faqat o'z guruhi uchun.</summary>
    [HttpPost("journal/reschedule")]
    public async Task<ActionResult<LessonRescheduleDto>> JournalReschedule(RescheduleLessonRequest req)
    {
        var (t, g, owns, _) = await ResolveOwnedGroup(req.ClassId);
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        if (g is null) return NotFound(new { message = "Guruh topilmadi" });
        // O'RINBOSARGA YOPIQ: dars ko'chirish guruhning JADVALINI o'zgartiradi — u oyning dars
        // soniga (maosh maxraji) va boshqa kunlarga ta'sir qiladi. Bu guruh egasining qarori.
        if (!owns) return Forbid();
        try
        {
            var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            var rec = await JournalService.RescheduleLessonAsync(db, req.ClassId, req.FromDate, req.ToDate, req.Time, actor);
            return new LessonRescheduleDto(rec.Id, rec.FromDate, rec.ToDate, rec.Time);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Ko'chirishni bekor qiladi (admin bilan bir xil) — FAQAT shu ko'chirish o'z guruhiga tegishli bo'lsa.</summary>
    [HttpDelete("journal/reschedule/{id}")]
    public async Task<IActionResult> JournalCancelReschedule(string id)
    {
        var rec = await db.LessonReschedules.FindAsync(id);
        if (rec is null) return NoContent();
        var (t, g, owns, _) = await ResolveOwnedGroup(rec.ClassId);
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        if (g is null) return NotFound(new { message = "Guruh topilmadi" });
        // O'RINBOSARGA YOPIQ — ko'chirishni qo'yish bilan bir xil sabab (yuqoriga qarang).
        if (!owns) return Forbid();
        await JournalService.CancelRescheduleAsync(db, id);
        return NoContent();
    }

    /// <summary>Guruh BAHOLASH grid'i (mezonlar × o'quvchilar) — FAQAT o'z guruhi uchun.
    /// Mezonlarni admin biriktiradi; o'qituvchi o'z guruhi o'quvchilarini shu mezonlar bo'yicha baholaydi.</summary>
    [HttpGet("grading/group/{groupId}/board")]
    public async Task<ActionResult<GradingBoardDto>> GradingBoard(string groupId, [FromQuery] string? month)
    {
        var (t, g, owns, isSub) = await ResolveOwnedGroup(groupId);
        if (t is null) return NotFound();
        if (g is null) return NotFound();
        // O'RINBOSARGA OCHIQ (o'qish): baho qo'yish uchun mezonlar taxtasini ko'rish shart.
        if (!owns && !isSub) return Forbid();
        return await GradingController.BuildBoardAsync(db, g, month);
    }

    /// <summary>O'quvchining bitta mezon bahosini saqlash — FAQAT o'z guruhi uchun.</summary>
    [HttpPost("grading/grade")]
    public async Task<IActionResult> GradingGrade(SetCriterionGradeRequest req)
    {
        var (t, g, owns, _) = await ResolveOwnedGroup(req.GroupId);
        if (t is null) return NotFound();
        if (g is null) return NotFound();
        // O'RINBOSARGA OCHIQ (yozish) — o'zi o'tgan darsning bahosi aynan uning ishi, FAQAT
        // req.Date kuni (tuzatish oynasi bilan).
        if (!owns && !await SubstituteWrite(t.Id, req.GroupId, req.Date)) return Forbid();
        try
        {
            await GradingController.UpsertGradeAsync(db, req);
            await db.SaveChangesAsync();
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Shu sanada bitta mezon bo'yicha BARCHA o'quvchini ommaviy belgilash — FAQAT o'z guruhi.</summary>
    [HttpPost("grading/grade/bulk")]
    public async Task<IActionResult> GradingBulk(BulkCriterionGradeRequest req)
    {
        var (t, g, owns, _) = await ResolveOwnedGroup(req.GroupId);
        if (t is null) return NotFound();
        if (g is null) return NotFound();
        // O'RINBOSARGA OCHIQ (yozish), FAQAT req.Date kuni — yakka baho bilan bir xil qoida.
        if (!owns && !await SubstituteWrite(t.Id, req.GroupId, req.Date)) return Forbid();
        await GradingController.BulkGradeAsync(db, g, req.CriterionId, req.Date, req.Done);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Guruh sillabus o'tilishi + tugash prognozi (admin <c>GET /admin/curriculum/group/{id}</c> bilan bir xil), o'z guruhi uchun.</summary>
    [HttpGet("curriculum/group/{groupId}")]
    public async Task<ActionResult<GroupCurriculumDto>> CurriculumGroup(string groupId)
    {
        var (t, group, owns, isSub) = await ResolveOwnedGroup(groupId);
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Schedule)) return Forbid();
        if (group is null) return NotFound();
        // O'RINBOSARGA OCHIQ (o'qish): "guruh qayerga yetgan, bugun qaysi mavzuni o'tishim kerak"
        // — dars o'tish uchun ZARUR ma'lumot.
        if (!owns && !isSub) return Forbid();

        return await CurriculumForecast.BuildGroupAsync(db, group);
    }

    /// <summary>Bandni o'tilgan/o'tilmagan qilish (admin <c>POST /admin/curriculum/group/{id}/cover</c> bilan bir xil), o'z guruhi uchun.</summary>
    [HttpPost("curriculum/group/{groupId}/cover")]
    public async Task<ActionResult> CurriculumCover(string groupId, CoverRequest req)
    {
        var (t, group, owns, _) = await ResolveOwnedGroup(groupId);
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Schedule)) return Forbid();
        if (group is null) return NotFound();
        // O'RINBOSARGA YOPIQ: o'quv dasturi o'tilishini BELGILASH — kurs prognozini (qachon
        // tugaydi) va guruh rejasini o'zgartiradi, ya'ni bir kunlik o'rinbosarning ishi emas.
        if (!owns) return Forbid();

        if (req.Covered)
        {
            var exists = await db.GroupCurriculumLogs
                .AnyAsync(g => g.GroupId == groupId && g.ItemId == req.ItemId && !g.IsRevision);
            if (!exists)
            {
                db.GroupCurriculumLogs.Add(new GroupCurriculumLog
                {
                    GroupId = groupId,
                    ItemId = req.ItemId,
                    IsRevision = false,
                    Date = AppClock.Today.ToString("yyyy-MM-dd"),
                    CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                });
            }
        }
        else
        {
            await db.GroupCurriculumLogs
                .Where(g => g.GroupId == groupId && g.ItemId == req.ItemId && !g.IsRevision)
                .ExecuteDeleteAsync();
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Takrorlash darsi qo'shish/olib tashlash (admin <c>POST /admin/curriculum/group/{id}/revision</c> bilan bir xil), o'z guruhi uchun.</summary>
    [HttpPost("curriculum/group/{groupId}/revision")]
    public async Task<ActionResult> CurriculumRevision(string groupId, RevisionRequest req)
    {
        var (t, group, owns, _) = await ResolveOwnedGroup(groupId);
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Schedule)) return Forbid();
        if (group is null) return NotFound();
        // O'RINBOSARGA YOPIQ — o'quv dasturini tahrirlash bilan bir xil sabab.
        if (!owns) return Forbid();

        if (req.Delta > 0)
        {
            db.GroupCurriculumLogs.Add(new GroupCurriculumLog
            {
                GroupId = groupId,
                ItemId = "",
                IsRevision = true,
                Date = AppClock.Today.ToString("yyyy-MM-dd"),
                CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            });
        }
        else if (req.Delta < 0)
        {
            var last = await db.GroupCurriculumLogs
                .Where(g => g.GroupId == groupId && g.IsRevision)
                .OrderByDescending(g => g.CreatedAt)
                .FirstOrDefaultAsync();
            if (last != null) db.GroupCurriculumLogs.Remove(last);
        }
        await db.SaveChangesAsync();

        var revisionLessons = await db.GroupCurriculumLogs
            .CountAsync(g => g.GroupId == groupId && g.IsRevision);
        return Ok(new { ok = true, revisionLessons });
    }

    // ---------- Guruh chati (dars beradigan guruhlar + guruh rahbarligi) ----------

    /// <summary>
    /// Har bir kanal uchun oxirgi xabar vaqti (ISO) — frontend o'qilmagan xabarlarni aniqlaydi.
    /// O'qituvchining barcha kanallari (guruhlar + xodimlar) qaytadi. Xabari yo'q kanal uchun null.
    /// </summary>
    [HttpGet("chat/last-messages")]
    public async Task<ActionResult<Dictionary<string, string?>>> ChatLastMessages()
    {
        if (!await HasPerm(TeacherPermissions.Messages)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var channels = await chat.ClassNamesForUserAsync(uid, "teacher");
        var lastByChannel = (await db.ChatMessages
                .Where(m => channels.Contains(m.ClassName))
                .GroupBy(m => m.ClassName)
                .Select(g => new { Name = g.Key, Last = g.Max(x => x.CreatedAt) })
                .ToListAsync())
            .ToDictionary(x => x.Name, x => (string?)x.Last.ToString("o"));
        return channels.ToDictionary(c => c, c => lastByChannel.GetValueOrDefault(c, null));
    }

    [HttpGet("chat/classes")]
    public async Task<ActionResult<IEnumerable<string>>> ChatClasses()
    {
        if (!await HasPerm(TeacherPermissions.Messages)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        return await chat.ClassNamesForUserAsync(uid, "teacher");
    }

    [HttpGet("chat/{className}")]
    public async Task<ActionResult<IEnumerable<ChatMessageDto>>> Chat(string className, [FromQuery] string? since)
    {
        if (!await HasPerm(TeacherPermissions.Messages)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!await chat.CanAccessAsync(uid, "teacher", className)) return Forbid();
        return await chat.GetMessagesAsync(className, ChatService.ParseSince(since));
    }

    [HttpPost("chat/{className}")]
    public async Task<ActionResult<ChatMessageDto>> SendChat(string className, SendChatRequest req)
    {
        if (!await HasPerm(TeacherPermissions.Messages)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!await chat.CanAccessAsync(uid, "teacher", className)) return Forbid();
        var dto = await chat.PostAsync(className, uid, req.Text);
        return dto is null ? BadRequest(new { message = "Xabar bo'sh" }) : dto;
    }

    // ---------- Taklif va shikoyatlar (o'qituvchi → admin) ----------

    /// <summary>
    /// O'qituvchi taklif yoki shikoyat yuboradi (matn + ixtiyoriy rasm). Admin/superadmin
    /// "Taklif va shikoyatlar" bo'limida ko'radi (yuboruvchi = o'qituvchi).
    /// </summary>
    [HttpPost("feedback")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> SubmitFeedback(
        [FromForm] string type, [FromForm] string text, IFormFile? image)
    {
        var t = await Me();
        if (t is null) return NotFound();
        var body = (text ?? "").Trim();
        if (body.Length == 0) return BadRequest(new { message = "Matn bo'sh" });
        if (image is not null && Application.Services.UploadGuard.Validate(image) is { } imgError)
            return BadRequest(new { message = imgError });

        string? imageUrl = null;
        if (image is { Length: > 0 })
        {
            var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
            System.IO.Directory.CreateDirectory(dir);
            var stored = Application.Services.UploadGuard.SafeName(image);
            await using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored));
            await image.CopyToAsync(fs);
            imageUrl = $"/uploads/{stored}";
        }

        db.Feedbacks.Add(new Feedback
        {
            StudentId = "",
            ParentName = "",
            SenderRole = "teacher",
            SenderName = t.FullName,
            TeacherId = t.Id,
            Type = type == "complaint" ? "complaint" : "suggestion",
            Text = body,
            ImageUrl = imageUrl,
            CreatedAt = AppClock.Now,
            Status = "new",
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- LMS (Ta'lim) — FAQAT KO'RISH + progress ----------
    // O'qituvchi LMS kontentini yaratmaydi (uni admin qiladi); faqat o'zi dars beradigan
    // (yoki rahbarlik qiladigan) guruhlarning materialini va o'quvchilar tugatishini ko'radi.

    /// <summary>O'qituvchi dars beradigan/rahbarlik qiladigan guruhlar id'lari (jadval + rahbarlik).</summary>
    private async Task<HashSet<string>> TaughtClassIdsAsync(Teacher t)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var groupIds = await db.Classes.Where(c => c.TeacherId == t.Id)
            .Select(c => c.Id).ToListAsync();
        foreach (var gid in groupIds) ids.Add(gid);
        if (!string.IsNullOrEmpty(t.HomeroomClass))
        {
            var hc = await db.Classes.FirstOrDefaultAsync(c => c.Name == t.HomeroomClass);
            if (hc is not null) ids.Add(hc.Id);
        }
        return ids;
    }

    // ---------- Support (bo'sh vaqt slotlari + bron) ----------

    /// <summary>O'z slotlarim (barcha holatlar): bo'sh / bron qilingan / o'tilgan.
    /// <paramref name="month"/> ("yyyy-MM") berilsa — faqat shu oy (oylar bo'yicha ko'rish/navigatsiya uchun).</summary>
    [HttpGet("support/slots")]
    public async Task<ActionResult<IEnumerable<SupportSlotDto>>> SupportSlots([FromQuery] string? month = null)
    {
        var me = await Me();
        if (me is null) return NotFound();
        var q = db.SupportSlots.Where(s => s.TeacherId == me.Id);
        if (!string.IsNullOrWhiteSpace(month)) q = q.Where(s => s.Date.StartsWith(month));
        var slots = await q
            .OrderByDescending(s => s.Date).ThenBy(s => s.StartTime).ToListAsync();
        var names = await SupportService.StudentNamesAsync(db, slots.Select(s => s.StudentId));
        return slots.Select(s => new SupportSlotDto(
            s.Id, s.TeacherId, s.Date, s.StartTime, s.EndTime, s.Status,
            s.StudentId, s.StudentId != null ? names.GetValueOrDefault(s.StudentId, "") : "",
            s.Topic, s.Notes, s.BookedAt)).ToList();
    }

    /// <summary>Bo'sh vaqt bloki qo'shish. SlotMinutes>0 bo'lsa blok har odamga shuncha daqiqalik bron
    /// slotlarga bo'linadi (1 soat + 30 → 2 slot). RepeatWeeks>0 — shu hafta kuni keyingi N haftaga ham.</summary>
    [HttpPost("support/slots")]
    public async Task<IActionResult> AddSupportSlot(CreateSupportSlotRequest req)
    {
        var me = await Me();
        if (me is null) return NotFound();
        if (!me.IsSupport) return BadRequest(new { message = "Siz support o'qituvchi emassiz" });
        if (string.IsNullOrWhiteSpace(req.Date) || string.IsNullOrWhiteSpace(req.StartTime)
            || string.IsNullOrWhiteSpace(req.EndTime))
            return BadRequest(new { message = "Sana va vaqtni to'liq kiriting" });
        if (!DateTime.TryParse(req.Date, out var baseDate))
            return BadRequest(new { message = "Sana noto'g'ri" });

        // Blokni har odamga ajratilgan davomiylik bo'yicha qism-slotlarga bo'lamiz.
        var subs = SplitInterval(req.StartTime, req.EndTime, req.SlotMinutes);
        if (subs.Count == 0)
            return BadRequest(new { message = "Vaqt oralig'i noto'g'ri (tugash boshlanishdan keyin bo'lsin)" });

        // Qaysi sanalarga qo'shamiz:
        //  RepeatMode=="daily" → Date..EndDate HAR KUNI (oylik rejani oldindan kiritish; max ~3 oy himoya);
        //  aks holda — HAFTALIK (RepeatWeeks: shu hafta kuni keyingi N haftaga). Bo'sh → faqat shu kun.
        var dates = new List<string>();
        if (req.RepeatMode == "daily" && DateTime.TryParse(req.EndDate, out var endDate))
        {
            var d0 = baseDate.Date;
            var d1 = endDate.Date < d0 ? d0 : endDate.Date;
            if ((d1 - d0).TotalDays > 92) d1 = d0.AddDays(92);
            for (var d = d0; d <= d1; d = d.AddDays(1))
                dates.Add(d.ToString("yyyy-MM-dd"));
        }
        else
        {
            var repeat = Math.Clamp(req.RepeatWeeks, 0, 12);
            for (var w = 0; w <= repeat; w++)
                dates.Add(baseDate.AddDays(7 * w).ToString("yyyy-MM-dd"));
        }

        var created = 0;
        foreach (var d in dates)
            foreach (var (st, en) in subs)
            {
                // Dublikat oldini olamiz (shu sana+boshlanish vaqti allaqachon bo'lsa o'tkazib yuboramiz).
                var exists = await db.SupportSlots.AnyAsync(
                    s => s.TeacherId == me.Id && s.Date == d && s.StartTime == st);
                if (exists) continue;
                db.SupportSlots.Add(new SupportSlot
                {
                    TeacherId = me.Id, Date = d, StartTime = st, EndTime = en,
                });
                created++;
            }
        await db.SaveChangesAsync();
        return Ok(new { created });
    }

    /// <summary>"HH:mm" blokni <paramref name="minutes"/> daqiqalik qism-slotlarga bo'ladi.
    /// minutes ≤ 0 yoki blokdan katta → butun blok bitta slot. Faqat to'liq sig'gan qismlar olinadi.</summary>
    private static List<(string Start, string End)> SplitInterval(string start, string end, int minutes)
    {
        var res = new List<(string, string)>();
        if (!TryMinutes(start, out var s) || !TryMinutes(end, out var e) || e <= s) return res;
        if (minutes <= 0 || minutes >= (e - s)) { res.Add((Fmt(s), Fmt(e))); return res; }
        for (var t = s; t + minutes <= e; t += minutes)
            res.Add((Fmt(t), Fmt(t + minutes)));
        return res;
    }

    private static bool TryMinutes(string hhmm, out int total)
    {
        total = 0;
        var parts = (hhmm ?? "").Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m))
            return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        total = h * 60 + m;
        return true;
    }

    private static string Fmt(int total) => $"{total / 60:D2}:{total % 60:D2}";

    /// <summary>Slotni o'chirish (o'tilgan darsdan tashqari). Bron qilingan bo'lsa ham o'chiriladi.</summary>
    [HttpDelete("support/slots/{id}")]
    public async Task<IActionResult> DeleteSupportSlot(string id)
    {
        var me = await Me();
        if (me is null) return NotFound();
        var slot = await db.SupportSlots.FindAsync(id);
        if (slot is null || slot.TeacherId != me.Id) return NotFound();
        if (slot.Status == "done") return BadRequest(new { message = "O'tilgan darsni o'chirib bo'lmaydi" });
        db.SupportSlots.Remove(slot);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bron qilingan darsni YOPISH: mavzu + izoh yozib "o'tildi" qiladi.</summary>
    [HttpPost("support/slots/{id}/complete")]
    public async Task<IActionResult> CompleteSupportSlot(string id, CompleteSupportRequest req)
    {
        var me = await Me();
        if (me is null) return NotFound();
        var slot = await db.SupportSlots.FindAsync(id);
        if (slot is null || slot.TeacherId != me.Id) return NotFound();
        if (slot.StudentId is null) return BadRequest(new { message = "Bu slot bron qilinmagan" });
        slot.Topic = (req.Topic ?? "").Trim();
        slot.Notes = (req.Notes ?? "").Trim();
        slot.Status = "done";
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Test natijalari (o'qituvchi — faqat o'z guruhlari) ----------
    // Mantiq admin bilan umumiy (TestResultService); bu yerda IKKI darvoza tekshiriladi:
    // "journal" ruxsati va guruh EGASI (Group.TeacherId == me) ekani. Test id orqali kelgan
    // amallarda testning guruhi egalik uchun tekshiriladi.

    /// <summary>
    /// Test amali uchun ruxsat: o'qituvchida <b>"journal"</b> ruxsati bor VA guruh EGASI bo'lsa true.
    ///
    /// <para>Ilgari bu yerda faqat egalik tekshirilardi. Ya'ni admin o'qituvchidan "Jurnal" ruxsatini
    /// olib qo'ysa ham, u ilovada tugmalarni ko'rmasa-da, API'ga to'g'ridan-to'g'ri murojaat qilib
    /// ball qo'ya olardi, testni O'CHIRA olardi va sertifikat yaratib yuklab olardi. Endi ruxsat
    /// olingan zahoti bu yo'l ham yopiladi.</para>
    ///
    /// <para>Nega aynan "journal": o'qituvchi ilovasidagi «Testlar» bo'limi shu ruxsat bilan
    /// ochiladi (qarang: <c>test-results/uploads</c> va <c>certificate-templates</c> — ular
    /// allaqachon shuni tekshirardi). Ya'ni bu tekshiruv yangi qoida emas, mavjud qoidani
    /// qolgan endpointlarga ham yoyish.</para>
    /// </summary>
    private async Task<bool> OwnsGroup(string classId)
    {
        if (!await HasPerm(TeacherPermissions.Journal)) return false;
        // ⚠️ O'RINBOSARGA YOPIQ (o'qish ham): testlar bo'limida test YARATISH/O'CHIRISH,
        // ball qo'yish va SERTIFIKAT berish bor — bular kurs davomidagi ish, bir kunlik
        // o'rinbosarnikimas. Shuning uchun bu yerda faqat `owns`.
        var (_, _, owns, _) = await ResolveOwnedGroup(classId);
        return owns;
    }

    /// <summary>O'qituvchining bitta guruhi testlari ro'yxati (?classId=).</summary>
    [HttpGet("test-results")]
    public async Task<ActionResult<List<GroupTestDto>>> TeacherTestList([FromQuery] string classId)
    {
        if (!await OwnsGroup(classId)) return Forbid();
        return await TestResultService.ListForGroupAsync(db, classId);
    }

    /// <summary>Test tafsiloti — o'quvchilar + ballari (ball desc).</summary>
    [HttpGet("test-results/{id}")]
    public async Task<ActionResult<TestResultDetailDto>> TeacherTestDetail(string id)
    {
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();
        var d = await TestResultService.DetailAsync(db, id);
        return d is null ? NotFound() : d;
    }

    /// <summary>Onlayn test savollari (PDF) faylini yuklash — testlar bo'limi uchun (maks ~20MB).
    /// Ruxsat: "journal" (o'qituvchi ilovasidagi «Testlar» bo'limi shu ruxsat bilan ochiladi).</summary>
    [HttpPost("test-results/uploads")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<UploadedFileDto>> TeacherTestUpload(IFormFile file)
    {
        if (!await HasPerm(TeacherPermissions.Journal)) return Forbid();
        if (Application.Services.UploadGuard.Validate(file) is { } error)
            return BadRequest(new { message = error });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = Application.Services.UploadGuard.SafeName(file);
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        return new UploadedFileDto(file.FileName, $"/uploads/{stored}", file.Length, file.ContentType ?? "");
    }

    /// <summary>Yangi test yaratish (o'z guruhiga).</summary>
    [HttpPost("test-results")]
    public async Task<ActionResult<GroupTestDto>> TeacherTestCreate(CreateTestResultRequest req)
    {
        var me = await Me();
        if (me is null) return NotFound();
        if (!await OwnsGroup(req.GroupId)) return Forbid();
        var (dto, err) = await TestResultService.CreateAsync(
            db, req.GroupId, req.Name, req.Date, req.MaxScore, me.FullName, req.Online,
            req.CertificateEnabled, req.CertificateTemplateId);
        return err != null ? BadRequest(new { message = err }) : dto!;
    }

    /// <summary>Testni tahrirlash.</summary>
    [HttpPut("test-results/{id}")]
    public async Task<IActionResult> TeacherTestUpdate(string id, UpdateTestResultRequest req)
    {
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();
        var (ok, err) = await TestResultService.UpdateAsync(
            db, id, req.Name, req.Date, req.MaxScore, req.Online,
            req.CertificateEnabled, req.CertificateTemplateId);
        return ok ? NoContent() : BadRequest(new { message = err });
    }

    /// <summary>Testni o'chirish.</summary>
    [HttpDelete("test-results/{id}")]
    public async Task<IActionResult> TeacherTestDelete(string id)
    {
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();
        return await TestResultService.DeleteAsync(db, id) ? NoContent() : NotFound();
    }

    /// <summary>Bitta o'quvchiga ball qo'yish/tozalash. Qaytadi: qayta saralangan tafsilot.</summary>
    [HttpPut("test-results/{id}/scores")]
    public async Task<ActionResult<TestResultDetailDto>> TeacherTestSetScore(string id, SetTestScoreRequest req)
    {
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();
        var (detail, err) = await TestResultService.SetScoreAsync(db, id, req.StudentId, req.Score);
        return err != null ? BadRequest(new { message = err }) : detail!;
    }

    // ---------- Test sertifikatlari (o'qituvchi — faqat o'z guruhi testlari) ----------
    // Andozalarni FAQAT admin boshqaradi; o'qituvchi ularni tanlaydi va sertifikat yaratadi.

    /// <summary>Tanlash uchun FAOL sertifikat andozalari + PDF konvertori bormi.</summary>
    [HttpGet("test-results/certificate-templates")]
    public async Task<ActionResult<object>> TeacherCertificateTemplates(CancellationToken ct)
    {
        if (!await HasPerm(TeacherPermissions.Journal)) return Forbid();
        var list = await testCerts.ListTemplatesAsync(db, includeInactive: false, ct);
        return Ok(new { templates = list, pdfAvailable = DocxToPdfConverter.IsAvailable });
    }

    /// <summary>Test bo'yicha sertifikat yaratishni BOSHLASH (ball kiritilgan har o'quvchiga).
    /// Ish fonda bajariladi — holat uchun <c>test-results/{id}/certificates/status</c>.</summary>
    [HttpPost("test-results/{id}/certificates")]
    public async Task<ActionResult<TestCertificateJobDto>> TeacherGenerateCertificates(
        string id, CancellationToken ct)
    {
        var me = await Me();
        if (me is null) return NotFound();
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();

        var (_, err) = await testCertJobs.StartAsync(db, testCerts, id, me.FullName, ct);
        if (err != null) return BadRequest(new { message = err });
        // Javobda MAVJUD sertifikatlar ham qaytariladi — aks holda tugma bosilishi bilan ro'yxat
        // birinchi holat javobigacha g'oyib bo'lardi.
        return await testCertJobs.StatusWithItemsAsync(db, id, ct);
    }

    /// <summary>Generatsiya holati + shu daqiqada tayyor sertifikatlar (UI so'rab turadi).</summary>
    [HttpGet("test-results/{id}/certificates/status")]
    public async Task<ActionResult<TestCertificateJobDto>> TeacherCertificatesStatus(
        string id, CancellationToken ct)
    {
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();
        return await testCertJobs.StatusWithItemsAsync(db, id, ct);
    }

    /// <summary>Bitta sertifikatni yuklab olish.</summary>
    [HttpGet("test-results/certificates/{certificateId}/download")]
    public async Task<IActionResult> TeacherDownloadCertificate(
        string certificateId, [FromQuery] string? format = null, CancellationToken ct = default)
    {
        // Egalik: sertifikat qaysi testga tegishli → o'sha testning guruhi meniki bo'lishi shart.
        var testId = await db.TestCertificates.AsNoTracking()
            .Where(c => c.Id == certificateId).Select(c => c.TestResultId).FirstOrDefaultAsync(ct);
        if (testId is null) return NotFound();
        var groupId = await TestResultService.GroupIdOfAsync(db, testId);
        if (groupId is null || !await OwnsGroup(groupId)) return Forbid();

        var file = await testCerts.ReadFileAsync(db, certificateId, preferPdf: format != "docx", ct);
        return file is null ? NotFound() : File(file.Value.Bytes, file.Value.ContentType, file.Value.FileName);
    }

    /// <summary>Test bo'yicha barcha sertifikatlar — bitta ZIP.</summary>
    [HttpGet("test-results/{id}/certificates/download")]
    public async Task<IActionResult> TeacherDownloadAllCertificates(string id, CancellationToken ct)
    {
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();
        var zip = await testCerts.ZipForTestAsync(db, id, ct);
        return zip is null ? NotFound(new { message = "Sertifikat yo'q" }) : File(zip.Value.Bytes, "application/zip", zip.Value.FileName);
    }

    /* =============================================================================================
     *  BOG'LANISH KERAK — o'qituvchi o'z guruhidagi o'quvchini navbatga yuboradi
     * ========================================================================================== */

    /// <summary>
    /// Bog'lanish sabablari (Sozlamalar → Sabablar, kategoriya "contact") — o'qituvchi tanlashi uchun.
    /// Admin endpointi (`/api/admin/action-reasons`) o'qituvchiga yopiq, shuning uchun alohida.
    /// </summary>
    [HttpGet("contact-reasons")]
    public async Task<ActionResult<IEnumerable<ActionReasonDto>>> ContactReasons() =>
        await db.ActionReasons
            .Where(r => r.Category == ContactService.ReasonCategory)
            .OrderBy(r => r.Order)
            .Select(r => new ActionReasonDto(r.Id, r.Category, r.Label, r.Order))
            .ToListAsync();

    /// <summary>
    /// O'z guruhidagi o'quvchi(lar)ni "Bog'lanish kerak" navbatiga yuboradi (jurnaldagi "Aloqa" tabi).
    ///
    /// <para>SANA SO'RALMAYDI — talab darhol navbatga tushadi (bugungi ish). Sabab va izoh
    /// TANLANGANLARNING HAMMASIGA bir xil qo'yiladi.</para>
    ///
    /// <para>XAVFSIZLIK: guruh o'qituvchiniki ekani tekshiriladi VA faqat o'sha guruhning FAOL
    /// a'zolari qabul qilinadi — aks holda o'qituvchi id yozib begona o'quvchini navbatga
    /// qo'sha olardi.</para>
    /// </summary>
    [HttpPost("groups/{classId}/contacts")]
    public async Task<ActionResult<ContactBulkResultDto>> SendToContactQueue(
        string classId, TeacherContactRequest req)
    {
        var me = await Me();
        if (me is null) return Forbid();
        if (!await Teaches(me.Id, classId, "")) return Forbid();

        var ids = (req.StudentIds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { message = "O'quvchi tanlanmagan" });

        // Ruxsat ro'yxati — SHU guruhning faol a'zolari.
        var allowed = (await db.StudentGroups.AsNoTracking()
                .Where(sg => sg.GroupId == classId && sg.IsActive)
                .Select(sg => sg.StudentId).ToListAsync())
            .ToHashSet();

        var r = await queue.AddManyAsync(
            ids, req.ReasonId, req.Note,
            // Sana YO'Q: o'qituvchi "hoziroq bog'laning" deydi, rejalashtirish operatorning ishi.
            due: null,
            actorId: me.UserId ?? "", actorName: me.FullName,
            allowedStudentIds: allowed);

        return new ContactBulkResultDto(r.Created, r.Skipped, r.SkippedNames, r.NotFound);
    }
}
