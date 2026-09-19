using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Xodimlar — o'qituvchi BO'LMAGAN ishchilar (kassir, administrator, ...). Har biriga admin
/// paneliga kiruvchi tizim akkaunti (role="staff") generatsiya qilinadi. Qaysi bo'limlarni
/// ko'rishi <see cref="AppUser.Permissions"/> bilan boshqariladi — uni superadmin/admin yoki
/// "Xodimlar" bo'limiga TO'LIQ ruxsati bor xodim o'zgartiradi ("Xodimlar va rollar" bo'limi /
/// <see cref="SetPermissions"/>). Bu bo'limga to'liq ruxsat berilgan xodim amalda superadmin
/// bilan bir xil darajada boshqa xodimlarni boshqara oladi — ATAYLAB shunday (ikkinchi
/// "superadmin darajali" boshqaruvchi kerak bo'lganda shu orqali beriladi).
///
/// <para><b>IKKINCHI SUPERADMIN</b> (<see cref="SetRole"/>): ruxsat matritsasi bermaydigan
/// imtiyozlar ham bor (moliya tahriri, o'chirish, AI, CTI'da hammani ko'rish — qarang
/// <see cref="AdminPermAttribute.IsSuperAdminOrGranted"/>). Shu sabab admin/xodim akkauntini
/// TO'LIQ superadminga aylantirish mumkin: <c>PUT {id}/role</c>. Ro'yxat (<see cref="GetAll"/>)
/// shu sababdan faqat <c>staff</c> emas, <b>panel akkauntlarining hammasini</b> qaytaradi —
/// ko'tarilgan odam ro'yxatdan yo'qolib qolsa, uni orqaga qaytarib bo'lmasdi.</para>
///
/// <para><b>Ism/parol/o'chirish amallari ATAYIN faqat <c>staff</c> uchun</b> qoladi: aks holda
/// "Xodimlar"ga to'liq ruxsati bor oddiy xodim SUPERADMIN parolini qayta yaratib, uning
/// akkauntiga kirib olardi (huquq oshirish). Rolni esa faqat superadminning O'ZI o'zgartira oladi.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("staff")]
[Route("api/admin/staff")]
public class StaffController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const int MinPasswordLength = 8;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";

    /// <summary>Admin paneliga kiradigan akkaunt rollari — ro'yxat va rol almashtirish shular ustida ishlaydi.
    /// O'qituvchi/o'quvchi/ota-ona bu yerga umuman tushmaydi.</summary>
    private static readonly string[] PanelRoles = [Roles.Staff, Roles.Admin, Roles.SuperAdmin];

    /// <summary>Rolni faqat TIZIM EGASI o'zgartiradi (platforma egasi ham). Oddiy <c>admin</c> ham,
    /// "Xodimlar"ga to'liq ruxsatli xodim ham o'zini/boshqasini superadmin qila olmaydi.</summary>
    private bool CanManageRoles =>
        User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.PlatformOwner);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StaffDto>>> GetAll()
    {
        // Odatdagidek GET xodimga ochiq (AdminPerm qoidasi). Lekin admin/superadmin akkauntining
        // LOGINI oddiy xodimga ko'rsatilmaydi — parol tiklash u yerdan baribir yopiq, login esa
        // brute-force uchun kerakli yarim ma'lumot. Superadmin/admin uchun to'liq ko'rinadi.
        var seesLogins = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin)
                         || User.IsInRole(Roles.PlatformOwner);
        return (await db.Users.AsNoTracking().Where(u => PanelRoles.Contains(u.Role))
                .OrderBy(u => u.FullName).ToListAsync())
            // Superadminlar tepada — "kim egasi" savoli ro'yxatning boshida javob topsin.
            .OrderBy(u => u.Role == Roles.SuperAdmin ? 0 : u.Role == Roles.Admin ? 1 : 2)
            .ThenBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(u => seesLogins || u.Role == Roles.Staff ? ToDto(u) : ToDto(u) with { Login = "" })
            .ToList();
    }

    /// <summary>Barcha xodim roli shablonlari — yangi xodim qo'shishda tanlash uchun.</summary>
    [HttpGet("role-templates")]
    public async Task<ActionResult<IEnumerable<StaffRoleTemplateDto>>> GetRoleTemplates() =>
        (await db.StaffRoleTemplates.ToListAsync())
            .Select(t => new StaffRoleTemplateDto(t.Id, t.Code, t.Name, t.Description, t.DefaultPermissions))
            .ToList();

    [HttpPost]
    public async Task<ActionResult<StaffDto>> Create(CreateStaffWithTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FullName)) return BadRequest(new { message = "F.I.SH kerak" });
        var user = AccountFactory.CreateAccountFor(db, Roles.Staff, req.FullName.Trim());
        user.Position = (req.Position ?? "").Trim();
        user.Phone = PhoneUtil.Normalize(req.Phone ?? "");

        // Ruxsatlar (rollar) — faqat "Xodimlar" bo'limiga TO'LIQ ruxsati bor kishi belgilay oladi
        // (SetPermissions kabi, HasFullAccess). Faqat QISMAN (masalan faqat "view") ruxsati bilan
        // kirgan xodim boshqa xodim yaratib, unga o'zboshimcha (finance/settings/staff) ruxsat
        // berib darajasini oshira olmasligi uchun. To'liq ruxsati bo'lmasa — ruxsatsiz yaratiladi
        // (keyin "Rollar" orqali beriladi).
        var permissions = new List<string>();
        if (AdminPermAttribute.HasFullAccess(User, "staff"))
        {
            // Role template tanlansa — default ruxsatlari qo'shiladi
            if (!string.IsNullOrWhiteSpace(req.TemplateCode))
            {
                var template = await db.StaffRoleTemplates
                    .FirstOrDefaultAsync(t => t.Code == req.TemplateCode.Trim());
                if (template is not null)
                {
                    permissions.AddRange(template.DefaultPermissions);
                }
            }
            // Qo'shimcha ruxsatlari qo'shiladi
            if (req.ExtraPermissions?.Count > 0)
            {
                foreach (var perm in req.ExtraPermissions)
                    if (!string.IsNullOrWhiteSpace(perm) && !permissions.Contains(perm))
                        permissions.Add(perm);
            }
        }
        user.Permissions = permissions;

        if (!string.IsNullOrWhiteSpace(req.NewPassword))
        {
            if (req.NewPassword.Trim().Length < MinPasswordLength)
                return BadRequest(new { message = WeakPasswordMessage });
            user.SetInitialPassword(req.NewPassword.Trim());
        }
        audit.Record("Staff", user.Id, "create",
            $"Xodim qo'shildi: {user.FullName}" +
            (user.Position.Length > 0 ? $" ({user.Position})" : "") +
            (permissions.Count > 0 ? $" — ruxsatlar: {string.Join(", ", permissions)}" : " — ruxsatsiz"));
        await db.SaveChangesAsync();
        return ToDto(user);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<StaffDto>> Update(string id, CreateStaffWithTemplateRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        if (string.IsNullOrWhiteSpace(req.FullName)) return BadRequest(new { message = "F.I.SH kerak" });
        user.FullName = req.FullName.Trim();
        user.Position = (req.Position ?? "").Trim();
        user.Phone = PhoneUtil.Normalize(req.Phone ?? "");
        if (!string.IsNullOrWhiteSpace(req.NewPassword))
        {
            if (req.NewPassword.Trim().Length < MinPasswordLength)
                return BadRequest(new { message = WeakPasswordMessage });
            user.SetInitialPassword(req.NewPassword.Trim());
        }
        audit.Record("Staff", user.Id, "update",
            $"Xodim tahrirlandi: {user.FullName}" +
            (user.Position.Length > 0 ? $" ({user.Position})" : "") +
            (!string.IsNullOrWhiteSpace(req.NewPassword) ? " — PAROL o'zgartirildi" : ""));
        await db.SaveChangesAsync();
        return ToDto(user);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? reasonId = null)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        var reason = string.IsNullOrWhiteSpace(reasonId) ? "" : (await db.ActionReasons.Where(r => r.Id == reasonId).Select(r => r.Label).FirstOrDefaultAsync() ?? "");
        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
        ArchiveService.Snapshot(db, "staff", user.Id, user.FullName, user.Email ?? "", user, reason.Length > 0 ? reason : null, actor);
        audit.Record("Staff", user.Id, "delete",
            $"Xodim o'chirildi: {user.FullName}" + (reason.Length > 0 ? $" — sabab: {reason}" : ""));
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Xodim akkaunti logini. Parol xavfsizlik uchun saqlanmaydi — bo'sh qaytadi
    /// (ko'rsatish kerak bo'lsa <see cref="ResetPassword"/> orqali yangisini yarating).
    /// <para>GET odatda xodim uchun ochiq bo'lsa-da (bo'limlararo o'qish uchun), bu endpoint
    /// AKKAUNT MA'LUMOTINI (login va dastlabki parol) qaytargani uchun MAXSUS tekshiriladi —
    /// faqat superadmin/admin yoki "Xodimlar" bo'limiga TO'LIQ ruxsati bor xodim. Aks holda
    /// bo'lim ruxsati yo'q istalgan xodim boshqalarning parolini o'qib olardi.</para>
    /// <para>⚠️ Xodim SUPERADMIN (yoki admin) qilingan bo'lsa ham login/parol ko'rinishi kerak —
    /// aks holda rol berilishi bilan akkaunt boshqarib bo'lmas holga tushardi. Lekin bunday
    /// akkauntni FAQAT superadminning O'ZI ocha oladi (<see cref="CanManageRoles"/>): "Xodimlar"
    /// bo'limiga to'liq ruxsatli oddiy xodim superadminning parolini o'qib, uning nomidan kirib
    /// olardi — huquq oshirish.</para></summary>
    [HttpGet("{id}/credentials")]
    public async Task<ActionResult<CredentialsDto>> Credentials(string id)
    {
        if (!AdminPermAttribute.HasFullAccess(User, "staff")) return Forbid();
        var user = await db.Users.FindAsync(id);
        if (user is null || !PanelRoles.Contains(user.Role)) return NotFound();
        if (user.Role != Roles.Staff && !CanManageRoles) return Forbid();
        return new CredentialsDto(user.Email, user.InitialPassword ?? "", user.Role);
    }

    /// <summary>Xodimga yangi tasodifiy parol generatsiya qiladi va uni BIR MARTA qaytaradi
    /// (DB'da faqat hash saqlanadi).</summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<CredentialsDto>> ResetPassword(string id)
    {
        var user = await db.Users.FindAsync(id);
        // Superadmin/admin qilingan akkauntning ham parolini tiklash mumkin, lekin faqat
        // superadmin (sabab — `Credentials` izohi: huquq oshirish yo'li ochilmasin).
        if (user is null || !PanelRoles.Contains(user.Role)) return NotFound();
        if (user.Role != Roles.Staff && !CanManageRoles) return Forbid();
        var pwd = AccountFactory.GeneratePassword();
        // Parolning O'ZI hech qachon tarixga yozilmaydi — faqat "almashtirildi" faktI.
        audit.Record("Staff", user.Id, "update", $"Xodim paroli qayta yaratildi: {user.FullName}");
        user.SetInitialPassword(pwd);
        await db.SaveChangesAsync();
        return new CredentialsDto(user.Email, pwd, user.Role);
    }

    /// <summary>Xodimning admin bo'lim ruxsatlari (Rollar) — "Xodimlar" ruxsati (tahrir amali)
    /// kerak; superadmin/admin har doim, xodim faqat shu ruxsat berilgan bo'lsa.</summary>
    [HttpPut("{id}/permissions")]
    public async Task<ActionResult<StaffDto>> SetPermissions(string id, SetStaffPermissionsRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        var oldPerms = user.Permissions.ToList();
        // null/bo'sh/dublikat kalitlarni tozalaymiz — aks holda token validation'da
        // null claim qiymati 500 (ArgumentNullException) keltirib chiqarishi mumkin.
        user.Permissions = (req.Permissions ?? new())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct()
            .ToList();
        // NIMA qo'shilgani/olib tashlangani ko'rinsin — "ruxsat kim tomonidan kengaytirilgan"
        // savoli tarixdan javob topsin (ro'yxatning o'zi uzun bo'lishi mumkin).
        var added = user.Permissions.Except(oldPerms).Order().ToList();
        var removed = oldPerms.Except(user.Permissions).Order().ToList();
        audit.Record("Staff", user.Id, "update",
            $"Xodim ruxsatlari o'zgartirildi: {user.FullName}" +
            (added.Count > 0 ? $" — qo'shildi: {string.Join(", ", added)}" : "") +
            (removed.Count > 0 ? $" — olib tashlandi: {string.Join(", ", removed)}" : "") +
            (added.Count == 0 && removed.Count == 0 ? " — o'zgarish yo'q" : ""),
            before: new { Permissions = oldPerms }, after: new { user.Permissions });
        await db.SaveChangesAsync();
        return ToDto(user);
    }

    /// <summary>
    /// PANEL AKKAUNTINING ROLI — "ikkinchi superadmin" tayinlash (yoki qaytarish).
    ///
    /// <para>Nega kerak: ruxsat matritsasi (<see cref="SetPermissions"/>) bo'limlarni ochadi, lekin
    /// superadminning BA'ZI imtiyozlari ruxsat kaliti bilan berilmaydi — masalan to'lovni tahrirlash/
    /// vozvrat, hisoblangan oylikni qo'lda tuzatish, markaz AI tahlili, Local Call'da hamma
    /// operatorni ko'rish. Markazda ikkinchi to'liq huquqli boshqaruvchi kerak bo'lsa, uning
    /// akkaunti shu endpoint bilan <c>superadmin</c> qilinadi.</para>
    ///
    /// <para>Cheklovlar (har biri ATAYIN):</para>
    /// <list type="bullet">
    ///   <item>faqat superadmin (yoki platforma egasi) chaqira oladi — <see cref="CanManageRoles"/>;</item>
    ///   <item><b>o'z rolini o'zgartirib bo'lmaydi</b> — tasodifan o'zini tushirib, tizimni
    ///     boshqaruvsiz qoldirmasin;</item>
    ///   <item><b>oxirgi superadminni tushirib bo'lmaydi</b> — markaz egasiz qolmasin;</item>
    ///   <item>o'qituvchi/o'quvchi akkaunti bu yerdan rol ololmaydi (faqat panel rollari).</item>
    /// </list>
    ///
    /// <para>Rol DB'da o'zgaradi va HAR so'rovda tokenga qayta o'qiladi (<c>Program.cs</c>
    /// <c>OnTokenValidated</c>) — ya'ni <b>qayta login shart emas</b>, xuddi ruxsatlar kabi.</para>
    /// </summary>
    [HttpPut("{id}/role")]
    public async Task<ActionResult<StaffDto>> SetRole(string id, SetStaffRoleRequest req)
    {
        if (!CanManageRoles) return Forbid();

        var role = (req.Role ?? "").Trim().ToLowerInvariant();
        if (!PanelRoles.Contains(role))
            return BadRequest(new { message = "Rol noto'g'ri (superadmin | admin | staff)" });

        var user = await db.Users.FindAsync(id);
        if (user is null || !PanelRoles.Contains(user.Role)) return NotFound();

        var meId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(meId) && meId == user.Id)
            return BadRequest(new { message = "O'z rolingizni o'zgartira olmaysiz" });

        if (user.Role == role) return ToDto(user);

        if (user.Role == Roles.SuperAdmin)
        {
            var superCount = await db.Users.CountAsync(u => u.Role == Roles.SuperAdmin);
            if (superCount <= 1)
                return BadRequest(new { message = "Oxirgi superadminni tushirib bo'lmaydi" });
        }

        var oldRole = user.Role;
        user.Role = role;
        audit.Record("Staff", user.Id, "update",
            $"Akkaunt roli o'zgartirildi: {user.FullName} — {RoleLabel(oldRole)} → {RoleLabel(role)}"
            + (role == Roles.SuperAdmin ? " (to'liq huquq: bo'lim ruxsatlari endi tekshirilmaydi)" : ""),
            before: new { Role = oldRole }, after: new { user.Role });
        await db.SaveChangesAsync();
        return ToDto(user);
    }

    private static string RoleLabel(string role) => role switch
    {
        Roles.SuperAdmin => "Superadmin",
        Roles.Admin => "Admin",
        _ => "Xodim",
    };

    private static StaffDto ToDto(AppUser u) =>
        new(u.Id, u.FullName, u.Position, u.Email, u.Permissions, u.Phone, u.Role);
}
