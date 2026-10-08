using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("teachers.list")]
[Route("api/admin/teachers")]
public class TeachersController(AppDbContext db, AuditService audit, IConfiguration config) : ControllerBase
{
    private const int MinPasswordLength = 8;
    /// <summary><see cref="Teacher.InactivePeriods"/> ro'yxatining chegarasi (eng eskilari tushadi).</summary>
    private const int MaxInactivePeriods = 50;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";

    /// <summary>
    /// Maoshning o'qiladigan ifodasi — tarix yozuvida "3 000 000 so'm" yoki "yig'ilganning 40%i"
    /// bo'lib chiqadi. Ikki rejim (<c>fixed</c>/<c>percent</c>) bitta joyda yoziladi, aks holda
    /// rejim almashganda "0 → 40" kabi tushunarsiz qator paydo bo'lardi.
    /// </summary>
    private static string SalaryText(Teacher t) =>
        t.SalaryMode == "percent"
            ? $"foizli — hisoblangan oylikning {t.SalaryPercent}%i" + (t.BonusPct > 0 ? $" (+{t.BonusPct}% ustama)" : "")
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
        var q = db.Teachers.AsNoTracking().AsQueryable();
        if (!includeArchived) q = q.Where(t => !t.IsArchived);
        return await q.OrderBy(t => t.FullName).ToListAsync();
    }

    /// <summary>
    /// BITTA o'qituvchi id bo'yicha — ro'yxatdagi element bilan AYNAN bir xil shakl (`Teacher`).
    /// <para>⚠️ ATAYIN qo'shildi: o'qituvchi sahifasi bitta o'qituvchini topish uchun BUTUN
    /// ro'yxatni tortardi. Prod o'lchovi: <c>Teachers</c> jadvali 20 000 martadan ko'p sequential
    /// scan qilingan. Arxivlangan o'qituvchi ham qaytadi — uning sahifasi ochilishi kerak.</para>
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Teacher>> GetOne(string id)
    {
        var t = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return t is null ? NotFound() : t;
    }

    /// <summary>Faqat arxivlangan o'qituvchilar.</summary>
    [HttpGet("archived")]
    public async Task<ActionResult<IEnumerable<Teacher>>> GetArchived() =>
        await db.Teachers.Where(t => t.IsArchived)
            .OrderByDescending(t => t.ArchivedAt).ThenBy(t => t.FullName).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<Teacher>> Create(TeacherPayload p)
    {
        if (p.SalaryPercent is < 0 or > 100)
            return BadRequest(new { message = "Maosh foizi 0 dan 100 gacha bo'lishi kerak" });
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
        if (p.SalaryPercent is < 0 or > 100)
            return BadRequest(new { message = "Maosh foizi 0 dan 100 gacha bo'lishi kerak" });
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();

        var oldCategory = teacher.Category;
        var oldStart = teacher.SalaryStartMonth;
        // TO'LIQ snapshot: ilgari FAQAT toifa va maosh boshlanish oyi kuzatilardi — ism, telefon,
        // MAOSH summasi/foizi, fanlar va ruxsatlar o'zgarishi tarixda umuman ko'rinmasdi.
        var beforeTeacher = AuditService.TeacherSnapshot(teacher);
        var oldSalaryText = SalaryText(teacher);
        var oldSalaryMode = teacher.SalaryMode;
        var oldSalaryPercent = teacher.SalaryPercent;
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
            // Parol o'rnatish = akkauntga kirish — `ResetPassword`/`Credentials` bilan BIR XIL darvoza (AUTH-4).
            if (!AdminPermAttribute.HasFullAccess(User, "teachers.list"))
                return StatusCode(StatusCodes.Status403Forbidden, new { message = PasswordForbiddenMessage });
            // Akkaunt yo'q bo'lsa — yaratib biriktiramiz.
            user ??= AccountFactory.CreateAccountFor(db, Roles.Teacher, teacher.FullName);
            teacher.UserId = user.Id;
            user.SetInitialPassword(pwd);
            await RefreshTokenService.RevokeAllForUserAsync(db, user.Id); // eski sessiyalar tugaydi (AUTH-1)
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

        // UMUMIY FOIZ qo'yildi/o'zgardi — o'qituvchining "QAT'IY 0 so'm" guruhlari "Umumiy"ga o'tadi.
        // ⚠️ Bunday guruhlar (masalan edutizimdan ko'chirilganlar) amalda SOZLANMAGAN: ular qolsa,
        // admin umumiy foiz qo'yib ham maosh 0 bo'lib qolardi va sababi ko'rinmasdi. Qat'iy summasi
        // BOR guruh (ataylab qo'yilgan) va o'zining foizi bor guruh TEGILMAYDI.
        if (teacher.SalaryMode == "percent" && teacher.SalaryPercent > 0
            && (oldSalaryMode != "percent" || oldSalaryPercent != teacher.SalaryPercent))
        {
            var zeroFixed = await db.Classes
                .Where(c => c.TeacherId == teacher.Id && c.TeacherSalaryMode == "fixed" && c.TeacherSalaryFixed == 0)
                .ToListAsync();
            foreach (var g in zeroFixed) g.TeacherSalaryMode = "";
            if (zeroFixed.Count > 0)
                audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
                    $"Per-guruh maosh: {string.Join(", ", zeroFixed.Select(g => g.Name))} — qat'iy 0 so'm → umumiy " +
                    $"({teacher.SalaryPercent}%)", teacherId: teacher.Id);
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
        if (await ActiveGroupsGuardAsync(id, "o'chirib") is { } blocked) return blocked;
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
    /// <remarks>
    /// <para>⚠️ <b>ARXIV = ISHDAN KETDI (audit SAL-2)</b> — ilgari faqat bayroq qo'yilardi:</para>
    /// <list type="bullet">
    /// <item>FAOL guruhi bor o'qituvchi arxivlanmaydi (<c>Delete</c> bilan bir xil darvoza) — 400 va
    ///   qaysi guruhlarga boshqa o'qituvchi tayinlash kerakligi ro'yxati. Aks holda guruh "egasiz"
    ///   qolib, jurnal/maosh arxivdagi odamga yozilaverardi;</item>
    /// <item>KELAJAKDAGI o'rinbosarlik tayinlovlari (o'rinbosar ham, asosiy o'qituvchi ham bo'lgan)
    ///   bekor qilinadi — har biriga audit qatori (<see cref="SubstituteTeacherService.CancelFutureForTeacherAsync"/>);</item>
    /// <item>qat'iy oylik arxiv KUNIDA to'xtaydi (<see cref="WunderkindLC.Application.Services.SalaryLedger.MonthFraction"/>, C4);</item>
    /// <item>maosh varaqasida to'lanmagan QOLDIQ bo'lsa — javobda ogohlantirish (<c>unpaidSalary</c>):
    ///   ketgan xodim bilan hisob-kitobni yopish esdan chiqmasin.</item>
    /// </list>
    /// </remarks>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id, ArchiveTeacherRequest req)
    {
        var teacher = await db.Teachers.FindAsync(id);
        if (teacher is null) return NotFound();
        if (teacher.IsArchived) return BadRequest(new { message = "O'qituvchi allaqachon arxivda" });
        if (await ActiveGroupsGuardAsync(id, "arxivlab") is { } blocked) return blocked;

        teacher.IsArchived = true;
        teacher.ArchivedAt = AppClock.Today.ToString("yyyy-MM-dd");
        teacher.ArchiveReason = (req.Reason ?? "").Trim();

        // Login bloklash — PasswordHash bo'shaltiriladi (login imkonsiz bo'ladi).
        if (teacher.UserId is not null)
        {
            var user = await db.Users.FindAsync(teacher.UserId);
            if (user is not null)
            {
                user.BlockLogin();
                // Ochiq sessiyalar ham yopiladi: aks holda tiklangandan keyin arxivdan OLDINGI
                // refresh tokenlar yana ishlab ketardi (`auth-sessions.md`).
                await RefreshTokenService.RevokeAllForUserAsync(db, user.Id);
            }
        }

        var cancelledSubs = await SubstituteTeacherService.CancelFutureForTeacherAsync(
            db, teacher.Id, audit, $"o'qituvchi {teacher.FullName} arxivlandi");

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi arxivga ko'chirildi ({teacher.FullName})"
                + (string.IsNullOrWhiteSpace(teacher.ArchiveReason) ? "" : $": \"{teacher.ArchiveReason}\"")
                + (cancelledSubs > 0 ? $" — {cancelledSubs} ta o'rinbosarlik tayinlovining kelajakdagi kunlari bekor qilindi" : ""),
            teacherId: teacher.Id);

        await db.SaveChangesAsync();

        // To'lanmagan maosh qoldig'i — arxiv sanasigacha hisoblangan (C4: qat'iy oylik shu kunda to'xtagan).
        var ledger = await WunderkindLC.Application.Services.SalaryLedger.BuildAsync(db, teacher, null, null);
        var unpaid = ledger.Remaining > 0.5m ? decimal.Round(ledger.Remaining, 2) : 0m;
        return Ok(new
        {
            ok = true,
            cancelledSubstitutions = cancelledSubs,
            unpaidSalary = unpaid,
            warning = unpaid > 0
                ? $"Diqqat: {teacher.FullName} uchun {AuditService.Money(unpaid)} so'm to'lanmagan maosh qoldig'i bor — " +
                  "hisob-kitobni yopish uchun o'qituvchi sahifasidagi «Maosh» bo'limiga qarang."
                : null,
        });
    }

    /// <summary>
    /// O'qituvchining FAOL (arxivlanmagan) guruhlari bo'lsa — 400 va guruhlar NOMLARI (avval boshqa
    /// o'qituvchi tayinlansin yoki guruh arxivlansin). <c>Delete</c> va <c>Archive</c> uchun BITTA darvoza.
    /// </summary>
    private async Task<IActionResult?> ActiveGroupsGuardAsync(string teacherId, string verb)
    {
        var names = await db.Classes.AsNoTracking()
            .Where(c => c.TeacherId == teacherId && !c.IsArchived)
            .OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        if (names.Count == 0) return null;
        return BadRequest(new
        {
            message = $"Bu o'qituvchi {names.Count} ta faol guruhga biriktirilgan ({string.Join(", ", names)}) — " +
                      $"{verb} bo'lmaydi. Avval guruhga boshqa o'qituvchi tayinlang yoki guruhni arxivlang.",
            groups = names,
        });
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

        var newPwd = (req?.NewPassword ?? "").Trim();
        // Parol o'rnatish = akkauntga kirish — `ResetPassword`/`Credentials` bilan BIR XIL darvoza
        // (auth-sessions.md §3). HECH NARSA o'zgartirilishidan OLDIN. ⚠️ Ilgari faqat sinf darajasidagi
        // `teachers.list:create` (POST) yetardi: parolni KO'RA olmaydigan xodim arxivdagi o'qituvchini
        // O'ZI tanlagan parol bilan qaytarib, uning nomidan kirardi. Parolsiz qaytarish — avvalgidek.
        if (newPwd.Length > 0 && !AdminPermAttribute.HasFullAccess(User, "teachers.list"))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = PasswordForbiddenMessage });
        if (newPwd.Length > 0 && newPwd.Length < MinPasswordLength)
            return BadRequest(new { message = WeakPasswordMessage });

        // ⚠️ Arxivda turgan davr maosh tarixida QOLADI: `ArchivedAt` tozalanadi, shuning uchun u
        // `InactivePeriods` ga yoziladi — aks holda qaytarilgan o'qituvchiga arxivdagi oylar uchun ham
        // qat'iy oylik (va guruh ulushi) qaytadan "hisoblanib" qolardi.
        var gap = WunderkindLC.Application.Services.SalaryLedger.InactivePeriodFor(teacher.ArchivedAt, AppClock.Today);
        if (gap is not null && !teacher.InactivePeriods.Contains(gap))
            teacher.InactivePeriods = teacher.InactivePeriods.Append(gap).TakeLast(MaxInactivePeriods).ToList();

        teacher.IsArchived = false;
        teacher.ArchivedAt = null;
        teacher.ArchiveReason = null;

        if (!string.IsNullOrEmpty(newPwd))
        {
            var user = teacher.UserId is null ? null : await db.Users.FindAsync(teacher.UserId);
            user ??= AccountFactory.CreateAccountFor(db, "teacher", teacher.FullName);
            teacher.UserId = user.Id;
            user.SetInitialPassword(newPwd);
        }
        // Arxivdan OLDINGI sessiyalar (30 kunlik sirpanuvchi refresh) qayta TIRILMASIN (R2-6) — yangi parol
        // berilsa ham, berilmasa ham (eski arxivlashda sessiyalar bekor qilinmagan bo'lishi mumkin).
        if (teacher.UserId is not null) await RefreshTokenService.RevokeAllForUserAsync(db, teacher.UserId);

        audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "update",
            $"O'qituvchi arxivdan qaytarildi ({teacher.FullName})"
                + (gap is null ? "" : $" — arxivda turgan davr ({gap.Replace("|", " — ")}) maosh hisoblanmaydigan davr bo'lib qoldi"),
            teacherId: teacher.Id);

        await db.SaveChangesAsync();
        // ⚠️ Arxivlashda parol O'CHIRILGAN (BlockLogin) — yangi parol berilmagan bo'lsa o'qituvchi hamon
        // tizimga KIRA OLMAYDI. Klient shu bayroq bo'yicha ogohlantiradi va parol yaratishni taklif qiladi.
        var loginUser = teacher.UserId is null ? null : await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == teacher.UserId);
        return Ok(new { ok = true, loginBlocked = loginUser is null || string.IsNullOrEmpty(loginUser.PasswordHash) });
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

    /// <summary>Parolni o'rnatish/qayta yaratish huquqi yo'q xodimga (403) ko'rsatiladigan sabab.</summary>
    private const string PasswordForbiddenMessage =
        "Parol o'rnatish uchun «O'qituvchilar» bo'limiga to'liq ruxsat kerak";

    /// <summary>O'qituvchiga yangi tasodifiy parol generatsiya qiladi va qaytaradi.
    /// <para>⚠️ Darvoza <see cref="Credentials"/> bilan BIR XIL (<c>HasFullAccess</c>): yangi parol
    /// QAYTARILADI, ya'ni bu o'qituvchi akkauntiga kirish. Ilgari <c>teachers.list:create</c> yetardi.</para>
    /// <para>Yangi parol <c>InitialPassword</c> ga ham yoziladi (<c>SetInitialPassword</c>) — «Login/parol»,
    /// Excel eksport va botdagi login xabari yangi parolni ko'rsatsin. Eski sessiyalar tugatiladi.</para></summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<CredentialsDto>> ResetPassword(string id)
    {
        if (!AdminPermAttribute.HasFullAccess(User, "teachers.list"))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = PasswordForbiddenMessage });
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
        user.SetInitialPassword(pwd);
        await RefreshTokenService.RevokeAllForUserAsync(db, user.Id);
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

        // Arxivlangan O'QUVCHI = ketgan (eski yozuvlarda a'zoligi tirik bo'lishi mumkin) — "faol" emas.
        var memberships = await db.StudentGroups
            .Where(sg => groups.Contains(sg.GroupId))
            .Select(sg => new
            {
                sg.Status,
                IsActive = sg.IsActive && !db.Students.Any(st => st.Id == sg.StudentId && st.IsArchived),
                sg.LeftAt,
            })
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

        // Arxivlangan O'QUVCHI = ketgan (eski yozuvlarda a'zoligi tirik bo'lishi mumkin) — "faol" emas.
        var memberships = await db.StudentGroups
            .Where(sg => groupIds.Contains(sg.GroupId))
            .Select(sg => new
            {
                sg.GroupId, sg.Status,
                IsActive = sg.IsActive && !db.Students.Any(st => st.Id == sg.StudentId && st.IsArchived),
                sg.LeftAt,
            })
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
        return await WunderkindLC.Application.Services.SalaryLedger.BuildAsync(
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

        // Foiz 0..100, summa manfiy emas — ilgari 150% ham jimgina saqlanardi (maosh tushumdan oshib ketardi).
        var bad = items.FirstOrDefault(i => i.Mode == "percent" && i.Percent is < 0 or > 100);
        if (bad is not null)
            return BadRequest(new
            {
                message = $"Maosh foizi 0 dan 100 gacha bo'lishi kerak" +
                          (byId.TryGetValue(bad.GroupId, out var bg) ? $" («{bg.Name}»: {bad.Percent}%)" : $" ({bad.Percent}%)"),
            });
        if (items.Any(i => i.Mode == "fixed" && i.Fixed < 0))
            return BadRequest(new { message = "Qat'iy maosh summasi manfiy bo'lishi mumkin emas" });

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
