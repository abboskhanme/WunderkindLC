using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Infrastructure.Auth;
using IntellectCRM.Infrastructure.Data;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;
using IntellectCRM.Application.Services;

namespace IntellectCRM.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("teachers.list")]
[Route("api/admin/teachers")]
public class TeachersController(AppDbContext db, AuditService audit, IConfiguration config) : ControllerBase
{
    private const int MinPasswordLength = 8;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";

    /// <summary>
    /// Maoshning o'qiladigan ifodasi — tarix yozuvida "3 000 000 so'm" yoki "yig'ilganning 40%i"
    /// bo'lib chiqadi. Ikki rejim (<c>fixed</c>/<c>percent</c>) bitta joyda yoziladi, aks holda
    /// rejim almashganda "0 → 40" kabi tushunarsiz qator paydo bo'lardi.
    /// </summary>
    private static string SalaryText(Teacher t) =>
        t.SalaryMode == "percent"
            ? $"foizli — yig'ilganning {t.SalaryPercent}%i" + (t.BonusPct > 0 ? $" (+{t.BonusPct}% ustama)" : "")
            : $"qat'iy {AuditService.Money(t.Salary)} so'm" + (t.BonusPct > 0 ? $" (+{t.BonusPct}% ustama)" : "");

    /// <summary>
    /// O'QITUVCHI RASMINI (profil surati) o'rnatish yoki o'chirish.
    ///
    /// <para>O'quvchidagi <c>PUT students/{id}/photo</c> bilan AYNAN bir xil yo'l va shu sababdan:
    /// rasm o'qituvchi sahifasidagi DUMALOQ avatarni bosib (kameradan yoki fayldan) yuklanadi,
    /// u yerda esa to'liq <c>TeacherPayload</c> yo'q — to'liq PUT yuborilsa maosh, toifa, fanlar,
    /// ruxsatlar tasodifan bo'shab qolardi. Shu sabab faqat bitta maydonga tegadigan yengil yo'l.</para>
    ///
    /// <para>Tekshiruvlar ham o'quvchinikidek: faqat serverning O'Z yuklamasi (<c>/uploads/</c>) va
    /// faqat rasm kengaytmasi (<c>UploadGuard.PhotoExtensions</c>) — tashqi havola yoki `.pdf`
    /// skan avatar bo'lib qo'yilmasin.</para>
    /// </summary>
    [HttpPut("{id}/photo")]
    public async Task<IActionResult> SetPhoto(string id, TeacherPhotoRequest req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var url = (req.PhotoUrl ?? "").Trim();
        if (url.Length > 0 && !url.StartsWith("/uploads/", StringComparison.Ordinal))
            return BadRequest(new { message = "Rasm manzili noto'g'ri" });
        if (url.Length > 0 && !Application.Services.UploadGuard.IsPhotoUrl(url))
            return BadRequest(new { message = "Surat faqat JPG yoki PNG bo'lishi kerak" });

        teacher.PhotoUrl = url.Length == 0 ? null : url;
        audit.Record(AuditService.EntityTeacherSalary, id, "update",
            url.Length == 0 ? $"O'qituvchi rasmi o'chirildi: {teacher.FullName}"
                            : $"O'qituvchi rasmi yangilandi: {teacher.FullName}",
            teacherId: id);
        await db.SaveChangesAsync();
        return Ok(new { photoUrl = teacher.PhotoUrl });
    }

    /// <summary>
    /// Faol (arxivlanmagan) o'qituvchilar. <paramref name="includeArchived"/>=true bo'lsa hammasi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Teacher>>> GetAll([FromQuery] bool includeArchived = false)
    {
        var q = db.Teachers.AsQueryable();
        if (!includeArchived) q = q.Where(t => !t.IsArchived);
        return await q.OrderBy(t => t.FullName).ToListAsync();
    }

    /// <summary>Faqat arxivlangan o'qituvchilar.</summary>
    [HttpGet("archived")]
    public async Task<ActionResult<IEnumerable<Teacher>>> GetArchived() =>
        await db.Teachers.Where(t => t.IsArchived)
            .OrderByDescending(t => t.ArchivedAt).ThenBy(t => t.FullName).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<Teacher>> Create(TeacherPayload p)
    {
        var teacher = new Teacher
        {
            FullName = p.FullName,
            BirthDate = p.BirthDate,
            Address = p.Address,
            Gender = p.Gender,
            Phone = PhoneUtil.Normalize(p.Phone ?? ""),
            PhotoUrl = p.PhotoUrl,
            HomeroomClass = p.HomeroomClass,
            SubjectIds = p.SubjectIds ?? new(),
            // Maosh QO'LDA: rejim "fixed" (qat'iy summa) yoki "percent" (guruh to'lovidan foiz).
            SalaryMode = p.SalaryMode == "percent" ? "percent" : "fixed",
            Salary = p.Salary,
            SalaryPercent = p.SalaryPercent,
            Category = p.Category ?? "",
            IsSupport = p.IsSupport,
            SalaryStartDate = p.SalaryStartDate ?? "",
            SalaryStartMonth = !string.IsNullOrEmpty(p.SalaryStartDate) && p.SalaryStartDate.Length >= 7
                ? p.SalaryStartDate[..7]
                : p.SalaryStartMonth ?? "",
            // Yangi o'qituvchiga standart — barcha bo'limlar ochiq (admin keyin cheklashi mumkin).
            Permissions = p.Permissions ?? TeacherPermissions.All.ToList(),
        };
        db.Teachers.Add(teacher);

        // Tizim akkaunti: doimo "teacher" roli. Support o'qituvchi ham teacher portaliga kiradi —
        // "Support" sahifasi profil menyusida IsSupport bayrog'i bo'yicha ko'rinadi (alohida rol kerak emas).
        var account = AccountFactory.CreateAccountFor(db, Roles.Teacher, teacher.FullName);
        teacher.UserId = account.Id;

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "create",
            $"O'qituvchi qo'shildi: {teacher.FullName}" +
            (string.IsNullOrEmpty(teacher.Category) ? "" : $" — toifa: {teacher.Category}") +
            $" — {SalaryText(teacher)}",
            after: AuditService.TeacherSnapshot(teacher), teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return teacher;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Teacher>> Update(string id, TeacherPayload p)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var oldCategory = teacher.Category;
        var oldStart = teacher.SalaryStartMonth;
        // TO'LIQ snapshot: ilgari FAQAT toifa va maosh boshlanish oyi kuzatilardi — ism, telefon,
        // MAOSH summasi/foizi, fanlar va ruxsatlar o'zgarishi tarixda umuman ko'rinmasdi.
        var beforeTeacher = AuditService.TeacherSnapshot(teacher);
        var oldSalaryText = SalaryText(teacher);
        teacher.FullName = p.FullName;
        teacher.BirthDate = p.BirthDate;
        teacher.Address = p.Address;
        teacher.Gender = p.Gender;
        teacher.Phone = PhoneUtil.Normalize(p.Phone ?? "");
        teacher.PhotoUrl = p.PhotoUrl;
        teacher.HomeroomClass = p.HomeroomClass;
        teacher.SubjectIds = p.SubjectIds ?? new();
        // Maosh: rejim + qat'iy summa + foiz (qo'lda kiritiladi).
        teacher.SalaryMode = p.SalaryMode == "percent" ? "percent" : "fixed";
        teacher.Salary = p.Salary;
        teacher.SalaryPercent = p.SalaryPercent;
        // Toifa — berilsa yangilaymiz (oylik avtomatik shu toifa narxidan hisoblanadi).
        if (p.Category is not null) teacher.Category = p.Category;
        teacher.IsSupport = p.IsSupport;
        teacher.SalaryStartDate = p.SalaryStartDate ?? "";
        teacher.SalaryStartMonth = !string.IsNullOrEmpty(p.SalaryStartDate) && p.SalaryStartDate.Length >= 7
            ? p.SalaryStartDate[..7]
            : p.SalaryStartMonth ?? "";
        // Ruxsatlar (bo'limlar) — berilsa yangilaymiz; berilmasa joriyini saqlaymiz.
        if (p.Permissions is not null) teacher.Permissions = p.Permissions;

        // Akkaunt nomini sinxronlaymiz va (ixtiyoriy) yangi parol o'rnatamiz.
        var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            var pwd = p.NewPassword.Trim();
            if (pwd.Length < MinPasswordLength) return BadRequest(new { message = WeakPasswordMessage });
            // Akkaunt yo'q bo'lsa — yaratib biriktiramiz.
            user ??= AccountFactory.CreateAccountFor(db, Roles.Teacher, teacher.FullName);
            teacher.UserId = user.Id;
            user.SetInitialPassword(pwd);
        }
        if (user is not null)
        {
            user.FullName = teacher.FullName;
            // Akkaunt roli doimo "teacher" (eski "support" rolli akkauntni ham shu yerda tuzatadi —
            // support sahifasi teacher portalida IsSupport bo'yicha ko'rinadi).
            user.Role = Roles.Teacher;
        }

        if (oldCategory != teacher.Category || oldStart != teacher.SalaryStartMonth)
        {
            var parts = new List<string>();
            if (oldCategory != teacher.Category)
                parts.Add($"toifa {(string.IsNullOrEmpty(oldCategory) ? "—" : oldCategory)} → " +
                          $"{(string.IsNullOrEmpty(teacher.Category) ? "—" : teacher.Category)}");
            if (oldStart != teacher.SalaryStartMonth)
                parts.Add($"boshlanish oyi {(string.IsNullOrEmpty(oldStart) ? "—" : oldStart)} → " +
                          $"{(string.IsNullOrEmpty(teacher.SalaryStartMonth) ? "—" : teacher.SalaryStartMonth)}");
            audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
                "Oylik sozlamasi: " + string.Join(", ", parts),
                before: new { Category = oldCategory, SalaryStartMonth = oldStart },
                after: new { teacher.Category, teacher.SalaryStartMonth }, teacherId: teacher.Id);
        }

        // MAOSH alohida qatorda — pulga tegadigan o'zgarish ro'yxatda ko'zga tashlanib tursin
        // (umumiy "tahrirlandi" yozuvi ichida yo'qolib ketmasin).
        var newSalaryText = SalaryText(teacher);
        if (oldSalaryText != newSalaryText)
            audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
                $"Maosh o'zgartirildi ({teacher.FullName}): {oldSalaryText} → {newSalaryText}",
                teacherId: teacher.Id);

        // Qolgan maydonlar (ism, telefon, manzil, fanlar, ruxsatlar, support...) — umumiy iz.
        // Guruh tahriridagi (ClassesController) qoida bilan bir xil: snapshot'lar farq qilsa yoziladi.
        var afterTeacher = AuditService.TeacherSnapshot(teacher);
        if (System.Text.Json.JsonSerializer.Serialize(beforeTeacher)
            != System.Text.Json.JsonSerializer.Serialize(afterTeacher))
            audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
                $"O'qituvchi tahrirlandi: {teacher.FullName}",
                before: beforeTeacher, after: afterTeacher, teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return teacher;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? reasonId = null)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        // O'qituvchi guruhda MAJBURIY — faol guruhga biriktirilgan bo'lsa o'chirib bo'lmaydi (yetim TeacherId oldini olish).
        var owns = await db.Classes.CountAsync(c => c.TeacherId == id && !c.IsArchived);
        if (owns > 0)
            return BadRequest(new { message = $"Bu o'qituvchi {owns} ta faol guruhga biriktirilgan — avval guruhga boshqa o'qituvchi tayinlang yoki guruhni arxivlang." });
        var reason = string.IsNullOrWhiteSpace(reasonId) ? "" : (await db.ActionReasons.Where(r => r.Id == reasonId).Select(r => r.Label).FirstOrDefaultAsync() ?? "");
        // Biriktirilgan tizim akkauntini ham o'chiramiz.
        if (teacher.UserId is not null)
        {
            var user = await db.Users.FindAsync(teacher.UserId);
            if (user is not null) db.Users.Remove(user);
        }
        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
        ArchiveService.Snapshot(db, "teacher", teacher.Id, teacher.FullName, teacher.Phone ?? "", teacher, reason.Length > 0 ? reason : null, actor);
        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "delete",
            $"O'qituvchi o'chirildi: {teacher.FullName}" + (reason.Length > 0 ? $" — sabab: {reason}" : ""),
            before: AuditService.TeacherSnapshot(teacher), teacherId: teacher.Id);
        db.Teachers.Remove(teacher);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* ---------- Arxiv ---------- */

    /// <summary>
    /// O'qituvchini arxivga ko'chirish: <c>IsArchived=true</c>, sana/sabab saqlanadi, akkaunt
    /// login bloklanadi (PasswordHash bo'shaltiriladi). Tarixiy ma'lumotlar (jurnal, hisobot) saqlanadi.
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id, ArchiveTeacherRequest req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        if (teacher.IsArchived) return BadRequest(new { message = "O'qituvchi allaqachon arxivda" });

        teacher.IsArchived = true;
        teacher.ArchivedAt = AppClock.Today.ToString("yyyy-MM-dd");
        teacher.ArchiveReason = (req.Reason ?? "").Trim();

        // Login bloklash — PasswordHash bo'shaltiriladi (login imkonsiz bo'ladi).
        if (teacher.UserId is not null)
        {
            var user = await db.Users.FindAsync(teacher.UserId);
            if (user is not null) user.BlockLogin();
        }

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi arxivga ko'chirildi ({teacher.FullName})"
                + (string.IsNullOrWhiteSpace(teacher.ArchiveReason) ? "" : $": \"{teacher.ArchiveReason}\""),
            teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Arxivdan qaytarish: <c>IsArchived=false</c>, arxiv maydonlari tozalanadi. Ixtiyoriy
    /// <c>NewPassword</c> berilsa akkauntga yangi parol o'rnatiladi (aks holda parol bloklangicha qoladi).
    /// </summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id, RestoreTeacherRequest req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        if (!teacher.IsArchived) return BadRequest(new { message = "O'qituvchi arxivda emas" });

        teacher.IsArchived = false;
        teacher.ArchivedAt = null;
        teacher.ArchiveReason = null;

        var newPwd = (req?.NewPassword ?? "").Trim();
        if (!string.IsNullOrEmpty(newPwd))
        {
            if (newPwd.Length < MinPasswordLength) return BadRequest(new { message = WeakPasswordMessage });
            var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
            user ??= AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
            user.SetInitialPassword(newPwd);
        }

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi arxivdan qaytarildi ({teacher.FullName})", teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// O'QITUVCHINI VAQTINCHA AKTIV EMAS QILISH (ta'til, to'xtatib turish, intizomiy chora):
    /// tizimga kira olmaydi — login rad etiladi va MAVJUD tokeni ham ishlamay qoladi
    /// (<c>Program.cs</c> dagi <c>OnTokenValidated</c> tekshiruvi).
    ///
    /// <para>ARXIVLASH EMAS: parol TEGILMAYDI (arxivda <c>AppUser.BlockLogin</c> uni o'chiradi va
    /// qaytarishda yangi parol kerak bo'ladi), o'qituvchi faol ro'yxatda belgisi bilan qoladi,
    /// guruhlari/maoshi/jurnali va butun tarixi joyida. Qaytarish — bir tugma (<c>unblock</c>).
    /// Guruhni vaqtincha bloklash (<c>PUT /api/admin/classes/{id}/block</c>) bilan bir xil g'oya.</para>
    ///
    /// <para>NEGA PUT: POST → <c>teachers:create</c>, PUT → <c>teachers:edit</c> ruxsatiga
    /// tushadi — bu amal mavjud yozuvni tahrirlash, UI darvozasi ham <c>teachers:edit</c>.</para>
    /// </summary>
    [HttpPut("{id}/block")]
    public async Task<IActionResult> Block(string id, BlockRequest? req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        if (teacher.IsArchived)
            return BadRequest(new { message = "Arxivdagi o'qituvchi — u allaqachon tizimga kira olmaydi" });
        if (teacher.IsBlocked) return BadRequest(new { message = "O'qituvchi allaqachon vaqtincha faol emas" });

        teacher.IsBlocked = true;
        teacher.BlockedAt = AppClock.Today.ToString("yyyy-MM-dd");
        teacher.BlockNote = (req?.Note ?? "").Trim();

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi vaqtincha faolsizlantirildi ({teacher.FullName}) — tizimga kira olmaydi"
                + (teacher.BlockNote.Length > 0 ? $": \"{teacher.BlockNote}\"" : ""),
            teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return Ok(new { ok = true, blockedAt = teacher.BlockedAt });
    }

    /// <summary>O'qituvchini qayta faollashtirish — eski paroli bilan odatdagidek kiraveradi.</summary>
    [HttpPut("{id}/unblock")]
    public async Task<IActionResult> Unblock(string id)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        if (!teacher.IsBlocked) return BadRequest(new { message = "O'qituvchi faol" });

        teacher.IsBlocked = false;
        teacher.BlockedAt = null;
        teacher.BlockNote = string.Empty;

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi qayta faollashtirildi ({teacher.FullName})", teacherId: teacher.Id);

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>O'qituvchining tizim akkaunti (login/parol). Akkaunt yo'q bo'lsa — yaratib biriktiradi.
    /// <para>GET odatda xodim uchun ochiq bo'lsa-da (bo'limlararo o'qish uchun), bu endpoint
    /// LOGIN va DASTLABKI PAROLNI qaytargani (va akkaunt yaratib DB'ni o'zgartirgani) uchun
    /// MAXSUS tekshiriladi — faqat superadmin/admin yoki "O'qituvchilar" bo'limiga TO'LIQ
    /// ruxsati bor xodim. Aks holda bo'lim ruxsati yo'q xodim ham parolni o'qiy olardi.</para></summary>
    [HttpGet("{id}/credentials")]
    public async Task<ActionResult<CredentialsDto>> Credentials(string id)
    {
        if (!AdminPermAttribute.HasFullAccess(User, "teachers.list")) return Forbid();
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
        if (user is null)
        {
            user = AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
            await db.SaveChangesAsync();
        }

        // O'qituvchi hali kirmagan bo'lsa dastlabki parol ko'rinadi; kirgach bo'sh (faqat reset-password).
        return new CredentialsDto(user.Email, user.InitialPassword ?? "", user.Role);
    }

    /// <summary>O'qituvchiga yangi tasodifiy parol generatsiya qiladi va BIR MARTA qaytaradi
    /// (DB'da faqat hash saqlanadi).</summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<CredentialsDto>> ResetPassword(string id)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
        if (user is null)
        {
            user = AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
        }
        var pwd = AccountFactory.GeneratePassword();
        // Parolning O'ZI hech qachon tarixga yozilmaydi — faqat "almashtirildi" fakti
        // (xodimlar bo'limidagi qoida bilan bir xil).
        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi paroli qayta yaratildi: {teacher.FullName}", teacherId: teacher.Id);
        user.PasswordHash = PasswordHasher.Hash(pwd);
        await db.SaveChangesAsync();
        return new CredentialsDto(user.Email, pwd, user.Role);
    }

    /// <summary>
    /// Barcha (faol) o'qituvchilarni login/parol bilan Excel (.xlsx) ga eksport qiladi.
    /// Parol FAQAT o'qituvchi hali kirmagan bo'lsa ko'rinadi (kirgach bo'sh). GET odatda xodim uchun
    /// ham ochiq bo'lsa-da, bu amal ommaviy parol dumpi bo'lgani uchun MAXSUS tekshiriladi — faqat
    /// superadmin/admin yoki "O'qituvchilar" bo'limiga TO'LIQ (barcha 4 amal) ruxsati bor xodim.
    /// Ustunlar: F.I.SH., Telefon, Guruh rahbarligi, Login, Parol.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        if (!AdminPermAttribute.HasFullAccess(User, "teachers.list")) return Forbid();
        var teachers = await db.Teachers.Where(t => !t.IsArchived)
            .OrderBy(t => t.FullName).ToListAsync();
        var userIds = teachers.Where(t => t.UserId != null).Select(t => t.UserId!).ToList();
        var byId = (await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id);

        var headers = new[] { "F.I.SH.", "Telefon", "Guruh rahbarligi", "Login", "Parol" };
        var rows = teachers.Select(t =>
        {
            byId.TryGetValue(t.UserId ?? "", out var u);
            return (IReadOnlyList<string>)new[]
            {
                t.FullName, t.Phone, t.HomeroomClass, u?.Email ?? "", u?.InitialPassword ?? "",
            };
        });

        var bytes = ExcelExport.Build("O'qituvchilar", headers, rows);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"oqituvchilar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Bitta o'qituvchining talaba saqlab qolish statistikasi (lifetime, per-group).
    /// Barcha guruhlar aggregati: retention%, loss%, effectiveness score.
    /// </summary>
    [HttpGet("{id}/performance")]
    public async Task<ActionResult<TeacherPerformanceDto>> GetSinglePerformance(string id)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var groups = await db.Classes
            .Where(c => c.TeacherId == id && !c.IsArchived)
            .Select(c => c.Id)
            .ToListAsync();

        var memberships = await db.StudentGroups
            .Where(sg => groups.Contains(sg.GroupId))
            .Select(sg => new { sg.Status, sg.IsActive, sg.LeftAt })
            .ToListAsync();

        // YAGONA hisoblagich — o'qituvchilar hisoboti (TeacherActivityReport) bilan raqamlar aynan bir xil.
        var tally = MembershipLifecycle.Tally(memberships.Select(s => (s.Status, s.IsActive, s.LeftAt)));

        return new TeacherPerformanceDto(
            teacher.Id, teacher.FullName, teacher.Phone ?? "",
            tally.Came, tally.Active, tally.Frozen, tally.Left,
            tally.Retention, tally.Loss,
            (int)Math.Round(tally.Retention),
            groups.Count
        );
    }

    /// <summary>
    /// Barcha faol o'qituvchilarning talaba saqlab qolish statistikasi (lifetime, per-group).
    /// Qaytadi: retention%, loss%, effectiveness score — saralash retention bo'yicha (kamayish).
    /// </summary>
    [HttpGet("performance")]
    public async Task<ActionResult<List<TeacherPerformanceDto>>> GetPerformance()
    {
        // Faol o'qituvchilar va ularning guruhlari (arxivlanmagan)
        var teachers = await db.Teachers
            .Where(t => !t.IsArchived)
            .OrderBy(t => t.FullName)
            .ToListAsync();

        var teacherIds = teachers.Select(t => t.Id).ToList();

        // Guruhlar (TeacherId in list) + StudentGroup a'zoliklari
        var groups = await db.Classes
            .Where(c => c.TeacherId != null && teacherIds.Contains(c.TeacherId!) && !c.IsArchived)
            .Select(c => new { c.Id, c.TeacherId })
            .ToListAsync();

        var groupIds = groups.Select(g => g.Id).ToList();

        var memberships = await db.StudentGroups
            .Where(sg => groupIds.Contains(sg.GroupId))
            .Select(sg => new { sg.GroupId, sg.Status, sg.IsActive, sg.LeftAt })
            .ToListAsync();

        // groupId → teacherId xaritasi
        var groupTeacher = groups.ToDictionary(g => g.Id, g => g.TeacherId!);

        // Per-teacher aggregat
        var byTeacher = memberships
            .GroupBy(sg => groupTeacher.GetValueOrDefault(sg.GroupId, ""))
            .ToDictionary(g => g.Key, g => g.ToList());

        var groupCount = groups
            .GroupBy(g => g.TeacherId!)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = teachers.Select(t =>
        {
            var slots = byTeacher.GetValueOrDefault(t.Id, new());
            var tally = MembershipLifecycle.Tally(slots.Select(s => (s.Status, s.IsActive, s.LeftAt)));

            return new TeacherPerformanceDto(
                t.Id, t.FullName, t.Phone ?? "",
                tally.Came, tally.Active, tally.Frozen, tally.Left,
                tally.Retention, tally.Loss,
                (int)Math.Round(tally.Retention),
                groupCount.GetValueOrDefault(t.Id, 0)
            );
        })
        .OrderByDescending(x => x.RetentionPercent)
        .ThenBy(x => x.TeacherName)
        .ToList();

        return result;
    }

    /// <summary>
    /// O'QITUVCHI AI TAHLILI — deterministik ko'rsatkichlar (AI'siz ham ko'rinadi): o'quvchi oqimi
    /// (kelgan/ketgan), ketish sabablari, jurnalni o'z vaqtida to'ldirish, baholar dinamikasi,
    /// testlar, davomat. Profildagi "AI tahlil" tabi shu bilan ochiladi.
    /// </summary>
    [HttpGet("{id}/ai-snapshot")]
    public async Task<ActionResult<TeacherAiMetricsDto>> AiSnapshot(string id, CancellationToken ct)
    {
        var t = await db.Teachers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var (metrics, _) = await TeacherSnapshotBuilder.BuildAsync(db, t, ct);
        return metrics;
    }

    /// <summary>O'qituvchining saqlangan AI tahlillari tarixi (eng yangisi birinchi).</summary>
    [HttpGet("{id}/ai-analyses")]
    public async Task<ActionResult<IEnumerable<TeacherAiRecordDto>>> AiAnalyses(string id, CancellationToken ct) =>
        await TeacherAiAnalysisService.HistoryAsync(db, id, ct);

    /// <summary>O'qituvchining BARCHA ma'lumotlarini Gemini orqali tahlil qiladi (kuniga bir marta —
    /// bugungi yozuv bo'lsa Gemini chaqirilmaydi, mavjudi qaytadi). API kaliti: Sozlamalar → AI Tahlil.</summary>
    [HttpPost("{id}/ai-analysis")]
    public async Task<ActionResult<TeacherAiResponseDto>> AiAnalysis(string id, CancellationToken ct)
    {
        var t = await db.Teachers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        return await TeacherAiAnalysisService.GenerateAsync(db, config, t, ct);
    }

    /// <summary>
    /// O'qituvchi o'quvchilarining REYTINGI — faqat shu o'qituvchi guruhlaridagi ball bo'yicha
    /// (ball = jurnal baholari yig'indisi + bajarilgan baholash mezonlari).
    /// <para><c>month</c> ("yyyy-MM") IXTIYORIY: berilsa faqat shu oy kesimi, bo'sh/berilmagan —
    /// AVVALGIDEK Umumiy (barcha vaqt). Javobdagi <c>months</c> — tanlash mumkin bo'lgan oylar.</para>
    /// </summary>
    [HttpGet("{id}/rating")]
    public async Task<ActionResult<TeacherRatingDto>> Rating(string id, [FromQuery] string? month = null)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        return await StudentBallService.TeacherAsync(db, teacher, month);
    }

    /// <summary>
    /// O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha): jami berilgan, qoldiq va
    /// har oyda qancha oylik berilgani. Oylar davr (from..to) bo'yicha, oy = to'lov sanasi oyi.
    /// <para><c>withRevenue: true</c> — har oyda o'qituvchi guruhlarining TUSHUMI ham qaytadi
    /// ("bo'lishi kerak" / "bo'ldi"), qat'iy maoshli o'qituvchida ham: admin kartochkasidagi oy
    /// tafsiloti aynan shuni ko'rsatadi.</para>
    /// </summary>
    [HttpGet("{id}/salary-ledger")]
    public async Task<ActionResult<SalaryLedgerDto>> SalaryLedger(
        string id, [FromQuery] string? from, [FromQuery] string? to)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        return await IntellectCRM.Application.Services.SalaryLedger.BuildAsync(
            db, teacher, from, to, withRevenue: true);
    }

    /// <summary>
    /// O'qituvchi guruhlarining PER-GURUH maosh sozlamasini yangilaydi: har guruhga alohida rejim
    /// ("percent" — shu guruh to'lovidan foiz | "fixed" — qat'iy summa | "" — o'qituvchi umumiy sozlamasi).
    /// Faqat shu o'qituvchiga biriktirilgan guruhlar yangilanadi. O'qituvchi oyligi guruhlar ulushi yig'indisi.
    /// </summary>
    [HttpPut("{id}/group-salaries")]
    public async Task<IActionResult> UpdateGroupSalaries(string id, GroupSalaryUpdateRequest req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var items = req.Items ?? new();
        var ids = items.Select(i => i.GroupId).ToList();
        // FAQAT shu o'qituvchining guruhlari — boshqa o'qituvchi guruhini o'zgartirib bo'lmaydi.
        var groups = await db.Classes
            .Where(c => c.TeacherId == id && ids.Contains(c.Id))
            .ToListAsync();
        var byId = groups.ToDictionary(g => g.Id);

        var changed = new List<string>();
        foreach (var it in items)
        {
            if (!byId.TryGetValue(it.GroupId, out var g)) continue;
            var mode = it.Mode is "percent" or "fixed" ? it.Mode : "";
            var pct = mode == "percent" ? Math.Max(0m, it.Percent) : 0m;
            var fixedAmt = mode == "fixed" ? Math.Max(0m, it.Fixed) : 0m;
            if (g.TeacherSalaryMode == mode && g.TeacherSalaryPercent == pct && g.TeacherSalaryFixed == fixedAmt)
                continue;
            g.TeacherSalaryMode = mode;
            g.TeacherSalaryPercent = pct;
            g.TeacherSalaryFixed = fixedAmt;
            changed.Add(mode == "percent" ? $"{g.Name}: {pct}%"
                : mode == "fixed" ? $"{g.Name}: {AuditService.Money(fixedAmt)} so'm"
                : $"{g.Name}: umumiy");
        }

        if (changed.Count > 0)
        {
            audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
                "Per-guruh maosh: " + string.Join(", ", changed), teacherId: teacher.Id);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}
