using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Application.Services;
using IntellectCRM.Application.Services.Kpi;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// KPI (xodimlar samaradorligi) — «Boshqaruv → KPI» bo'limining API'si.
///
/// <para>Butun HISOB mantiqи <see cref="KpiCalculator"/> va <see cref="KpiMetricsService"/> da;
/// bu controller — HTTP qobig'i: kim nimani so'rayapti, ruxsati bormi, natijani DTO'ga
/// aylantirish va YOZUV (tiket, cheklist belgisi, snapshot, oylik natija) + audit.</para>
///
/// <para><b>RUXSAT NAQSHI</b> (<c>.claude/rules/permissions.md</c> §4.1, <c>SettingsController</c>
/// bilan bir xil): SINF darajasi = O'QISH, METOD darajasi = SAHIFA bo'yicha YOZISH.</para>
///
/// <para>⚠️ <c>ReadRequiresPerm = true</c> — javobda xodimning OKLADI va oylik summasi bor.
/// <c>AdminPerm</c> da GET odatda har qanday xodimga ochiq (bo'limlararo o'qish uchun); bu yerda
/// esa har kim hammaning maoshini ko'rib qolardi.</para>
///
/// <para>⚠️ <b>Xodim O'Z KPI'sini ko'radi</b>: <c>userId</c> bo'sh bo'lsa joriy foydalanuvchi
/// olinadi. BOSHQA xodimning raqamini so'rash uchun <c>kpi</c> bo'lim ruxsati kerak va
/// yetmasa <b>403 sabab bilan</b> qaytadi — jim bo'sh natija EMAS (bo'sh javob "raqamlaringiz
/// nol" bo'lib ko'rinardi).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("kpi", ReadRequiresPerm = true)]
[Route("api/admin/kpi")]
public class KpiController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /* ---------- Audit turlari (`AuditSections.ByEntityType` da ro'yxatdan o'tgan) ---------- */
    private const string EntityProfile = "KpiProfile";
    private const string EntityRules = "KpiRuleSet";
    private const string EntityTicket = "KpiTicket";
    private const string EntityResult = "KpiMonthResult";
    private const string EntityChecklistItem = "ChecklistTemplateItem";

    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";

    private string CurrentUserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";

    /// <summary>
    /// Kimning raqamlari so'ralmoqda: bo'sh <paramref name="requested"/> — JORIY foydalanuvchi.
    /// </summary>
    /// <remarks>⚠️ Boshqa xodimning raqamini ko'rish uchun bo'lim ruxsati SHART va tekshiruv
    /// ANIQ (jim bo'sh ro'yxat emas): oylik summasi shaxsiy ma'lumot, "hech narsa chiqmadi"
    /// esa nosozlik bo'lib ko'rinib, xodim uni qayta-qayta so'rayverardi.</remarks>
    private bool ResolveUser(string? requested, out string userId, out string? error)
    {
        userId = string.IsNullOrWhiteSpace(requested) ? CurrentUserId : requested.Trim();
        error = null;
        if (string.Equals(userId, CurrentUserId, StringComparison.Ordinal)) return true;
        if (AdminPermAttribute.HasSectionAccess(User, "kpi")) return true;
        error = "Boshqa xodimning KPI raqamlarini ko'rish uchun «KPI» bo'limi ruxsati kerak.";
        return false;
    }

    /// <summary>Oy ("yyyy-MM") — bo'sh bo'lsa joriy oy.</summary>
    private static string MonthOr(string? month) =>
        string.IsNullOrWhiteSpace(month) ? AppClock.Today.ToString("yyyy-MM") : month.Trim()[..Math.Min(7, month.Trim().Length)];

    /// <summary>Sana ("yyyy-MM-dd") — bo'sh bo'lsa bugun.</summary>
    private static string DateOr(string? date) =>
        string.IsNullOrWhiteSpace(date) ? AppClock.Today.ToString("yyyy-MM-dd") : date.Trim();

    /* =========================================================================================
     *  PROFILLAR va OKLAD
     * ====================================================================================== */

    /// <summary>KPI profillari: rol, kafolat va OKLAD TARIXI (har xodim uchun).</summary>
    [HttpGet("profiles")]
    public async Task<ActionResult<List<KpiProfileDto>>> Profiles(CancellationToken ct)
    {
        var profiles = await db.KpiProfiles.AsNoTracking().ToListAsync(ct);
        var userIds = profiles.Select(p => p.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Position })
            .ToDictionaryAsync(u => u.Id, ct);
        var salaries = await db.KpiProfileSalaries.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId)).ToListAsync(ct);

        var month = AppClock.Today.ToString("yyyy-MM");
        return profiles
            .OrderBy(p => p.RoleCode, StringComparer.Ordinal)
            .ThenBy(p => users.GetValueOrDefault(p.UserId)?.FullName ?? "", StringComparer.CurrentCulture)
            .Select(p => new KpiProfileDto(
                p.Id, p.UserId,
                users.GetValueOrDefault(p.UserId)?.FullName ?? "(o'chirilgan xodim)",
                users.GetValueOrDefault(p.UserId)?.Position ?? "",
                p.RoleCode, KpiRuleSeed.RoleLabel(p.RoleCode),
                p.StartMonth, p.GuaranteeUntilMonth, p.IsActive, p.Note,
                KpiVersioning.SalaryFor(salaries, p.UserId, month)?.BaseSalary ?? 0m,
                salaries.Where(s => s.UserId == p.UserId)
                    .OrderByDescending(s => s.EffectiveFrom, StringComparer.Ordinal)
                    .Select(s => new KpiSalaryDto(s.Id, s.EffectiveFrom, s.BaseSalary, s.Note, s.CreatedBy, s.CreatedAt))
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// PROFIL BERISH MUMKIN BO'LGAN xodimlar — «xodim qo'shish» formasining ro'yxati.
    /// </summary>
    /// <remarks>⚠️ Busiz forma xodimning GUID'ini qo'lda so'rashga majbur bo'lardi. Profili bor
    /// xodim ham ro'yxatda qoladi (roli bilan) — formada uni tanlab TAHRIRLASH mumkin bo'lsin;
    /// profili yo'qlarda <c>RoleCode</c> BO'SH.</remarks>
    [HttpGet("profiles/candidates")]
    public async Task<ActionResult<List<KpiStaffDto>>> ProfileCandidates(CancellationToken ct)
    {
        string[] roles = [Roles.Admin, Roles.SuperAdmin, Roles.Staff];
        var users = await db.Users.AsNoTracking()
            .Where(u => roles.Contains(u.Role))
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync(ct);
        var profiles = await db.KpiProfiles.AsNoTracking()
            .Select(p => new { p.UserId, p.RoleCode })
            .ToDictionaryAsync(p => p.UserId, p => p.RoleCode, ct);

        return users
            .OrderBy(u => u.FullName, StringComparer.CurrentCulture)
            .Select(u =>
            {
                var role = profiles.GetValueOrDefault(u.Id, "");
                return new KpiStaffDto(u.Id, u.FullName, role,
                    string.IsNullOrEmpty(role) ? "" : KpiRuleSeed.RoleLabel(role));
            })
            .ToList();
    }

    /// <summary>Profil yaratish/tahrirlash (+ ixtiyoriy BIRINCHI oklad versiyasi).</summary>
    [HttpPost("profiles")]
    [AdminPerm("kpi.rules")]
    public async Task<ActionResult<KpiProfileDto>> SaveProfile(SaveKpiProfileRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.UserId)) return BadRequest(new { message = "Xodim tanlanmagan" });
        if (!KpiRuleSeed.Roles.Contains(req.RoleCode))
            return BadRequest(new { message = $"Noma'lum rol kodi: {req.RoleCode}" });

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.UserId, ct);
        if (user is null) return BadRequest(new { message = "Xodim topilmadi" });

        // ⚠️ Bir xodimda BITTA profil: ikkinchisi qo'shilsa oylik ikki marta hisoblanardi
        // («Yopish» jadvalida ikkita qator). Indeks unikal EMAS, shuning uchun tekshiruv shu yerda.
        var existing = req.Id is { Length: > 0 }
            ? await db.KpiProfiles.FirstOrDefaultAsync(p => p.Id == req.Id, ct)
            : await db.KpiProfiles.FirstOrDefaultAsync(p => p.UserId == req.UserId, ct);

        var isNew = existing is null;
        var profile = existing ?? new KpiProfile { UserId = req.UserId };
        var before = isNew ? null : new { profile.RoleCode, profile.StartMonth, profile.GuaranteeUntilMonth, profile.IsActive };

        profile.UserId = req.UserId;
        profile.RoleCode = req.RoleCode;
        profile.StartMonth = MonthOr(req.StartMonth);
        profile.GuaranteeUntilMonth = string.IsNullOrWhiteSpace(req.GuaranteeUntilMonth)
            ? null : req.GuaranteeUntilMonth.Trim();
        profile.IsActive = req.IsActive;
        profile.Note = req.Note;
        if (isNew) db.KpiProfiles.Add(profile);

        if (req.BaseSalary is decimal salary && salary > 0)
        {
            var from = MonthOr(req.SalaryEffectiveFrom ?? profile.StartMonth);
            // Bir oy uchun IKKI oklad versiyasi bo'lmasin — bo'lsa qiymat yangilanadi
            // (`KpiVersioning.SalaryFor` ikkalasidan bittasini tanlab, ikkinchisini jimgina
            // e'tiborsiz qoldirardi va "oklad o'zgarmayapti" bo'lib ko'rinardi).
            var row = await db.KpiProfileSalaries
                .FirstOrDefaultAsync(s => s.UserId == req.UserId && s.EffectiveFrom == from, ct);
            if (row is null)
                db.KpiProfileSalaries.Add(new KpiProfileSalary
                {
                    UserId = req.UserId, EffectiveFrom = from, BaseSalary = salary,
                    Note = "Profil bilan birga kiritildi", CreatedBy = Actor,
                });
            else { row.BaseSalary = salary; row.CreatedBy = Actor; }
        }

        audit.Record(EntityProfile, profile.Id, isNew ? "create" : "update",
            $"KPI profili {(isNew ? "yaratildi" : "tahrirlandi")}: {user.FullName} — " +
            $"{KpiRuleSeed.RoleLabel(profile.RoleCode)}" +
            (profile.GuaranteeUntilMonth is { Length: > 0 } g ? $", kafolat {g} gacha" : ""),
            before, new { profile.RoleCode, profile.StartMonth, profile.GuaranteeUntilMonth, profile.IsActive });
        await db.SaveChangesAsync(ct);

        var all = await Profiles(ct);
        return all.Value?.FirstOrDefault(p => p.Id == profile.Id) is { } dto
            ? dto
            : BadRequest(new { message = "Profil saqlandi, lekin qaytarib bo'lmadi" });
    }

    /// <summary>Profilni o'chirish.</summary>
    /// <remarks>⚠️ Oklad TARIXI va tasdiqlangan oylar TEGILMAYDI: ular tarix va ularga tayangan
    /// hisobotlar o'chirilgan profil tufayli buzilmasligi kerak. Xodimni hisobdan chiqarish
    /// uchun odatda <c>IsActive = false</c> yetadi.</remarks>
    [HttpDelete("profiles/{id}")]
    [AdminPerm("kpi.rules")]
    public async Task<IActionResult> DeleteProfile(string id, CancellationToken ct)
    {
        var p = await db.KpiProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        var name = await db.Users.AsNoTracking().Where(u => u.Id == p.UserId)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? p.UserId;

        db.KpiProfiles.Remove(p);
        audit.Record(EntityProfile, id, "delete",
            $"KPI profili o'chirildi: {name} — {KpiRuleSeed.RoleLabel(p.RoleCode)}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Yangi OKLAD versiyasi (eskisi tarixda qoladi).</summary>
    [HttpPost("profiles/{userId}/salary")]
    [AdminPerm("kpi.rules")]
    public async Task<ActionResult<KpiSalaryDto>> SaveSalary(string userId, SaveKpiSalaryRequest req, CancellationToken ct)
    {
        if (req.BaseSalary < 0) return BadRequest(new { message = "Oklad manfiy bo'la olmaydi" });
        var from = MonthOr(req.EffectiveFrom);
        if (IsPastMonth(from))
            return BadRequest(new { message = "O'tgan oyni tanlab bo'lmaydi — yopilgan oylar qayta yozilmaydi." });

        // Bir (xodim, oy) uchun bitta versiya — takror kiritilsa ustidan yoziladi.
        var row = await db.KpiProfileSalaries
            .FirstOrDefaultAsync(s => s.UserId == userId && s.EffectiveFrom == from, ct);
        var isNew = row is null;
        row ??= new KpiProfileSalary { UserId = userId, EffectiveFrom = from };
        var before = isNew ? null : new { row.BaseSalary };
        row.BaseSalary = req.BaseSalary;
        row.Note = req.Note;
        row.CreatedBy = Actor;
        if (isNew) db.KpiProfileSalaries.Add(row);

        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? userId;
        audit.Record(EntityProfile, row.Id, isNew ? "create" : "update",
            $"KPI oklad versiyasi: {name} — {from} dan {AuditService.Money(req.BaseSalary)} so'm",
            before, new { row.BaseSalary });
        await db.SaveChangesAsync(ct);

        return new KpiSalaryDto(row.Id, row.EffectiveFrom, row.BaseSalary, row.Note, row.CreatedBy, row.CreatedAt);
    }

    /* =========================================================================================
     *  QOIDALAR
     * ====================================================================================== */

    /// <summary>Shu OYDA amal qiladigan qoidalar to'plami.</summary>
    /// <remarks>⚠️ Versiya topilmasa 404 emas, Excel'dagi SEED qiymatlari <c>Id = ""</c> bilan
    /// qaytadi: «Qoidalar» sahifasi ochilishi va foydalanuvchi birinchi versiyani SAQLAY olishi
    /// kerak. 404 bersak sahifa xato bilan qorayib, seed qilishning yo'li ko'rinmasdi.</remarks>
    [HttpGet("rules")]
    public async Task<ActionResult<KpiRuleSetDto>> Rules(
        [FromQuery] string role, [FromQuery] string? month, CancellationToken ct)
    {
        if (!KpiRuleSeed.Roles.Contains(role)) return BadRequest(new { message = $"Noma'lum rol: {role}" });
        var m = MonthOr(month);
        var all = await db.KpiRuleSets.AsNoTracking().Where(r => r.RoleCode == role).ToListAsync(ct);
        var found = KpiVersioning.RuleFor(all, role, m);
        if (found is null)
            return new KpiRuleSetDto("", role, KpiRuleSeed.RoleLabel(role), m,
                "Hali seed qilinmagan — bu Excel'dagi standart qiymatlar.", null, AppClock.Iso(),
                KpiRuleSeed.For(role));

        return ToDto(found);
    }

    /// <summary>Rol qoidalarining BARCHA versiyalari (yangisidan eskisiga).</summary>
    [HttpGet("rules/history")]
    public async Task<ActionResult<List<KpiRuleSetDto>>> RuleHistory([FromQuery] string role, CancellationToken ct)
    {
        var rows = await db.KpiRuleSets.AsNoTracking()
            .Where(r => r.RoleCode == role).ToListAsync(ct);
        return rows
            .OrderByDescending(r => r.EffectiveFrom, StringComparer.Ordinal)
            .ThenByDescending(r => r.CreatedAt, StringComparer.Ordinal)
            .Select(ToDto).ToList();
    }

    /// <summary>Qoidalarning YANGI versiyasi.</summary>
    /// <remarks>⚠️ Mavjud versiya TAHRIRLANMAYDI — har saqlash yangi qator. Ustidan yozilsa,
    /// martda koeffitsient o'zgartirilishi bilan yanvarning TASDIQLANGAN summasi ham
    /// "boshqacha" bo'lib qolardi.</remarks>
    [HttpPost("rules")]
    [AdminPerm("kpi.rules")]
    public async Task<ActionResult<KpiRuleSetDto>> SaveRules(SaveKpiRuleSetRequest req, CancellationToken ct)
    {
        if (!KpiRuleSeed.Roles.Contains(req.RoleCode))
            return BadRequest(new { message = $"Noma'lum rol: {req.RoleCode}" });
        var from = MonthOr(req.EffectiveFrom);
        if (IsPastMonth(from))
            return BadRequest(new { message = "O'tgan oyni tanlab bo'lmaydi — yopilgan oylar qayta yozilmaydi." });

        // Bir (rol, oy) uchun bitta versiya: bir kunda ikki marta tahrirlansa YANGI qator emas,
        // o'sha oyning versiyasi yangilanadi — aks holda tarixda bir xil oyli o'nlab qator
        // to'planib, qaysi biri amalda ekani ko'rinmay qolardi.
        var row = await db.KpiRuleSets
            .FirstOrDefaultAsync(r => r.RoleCode == req.RoleCode && r.EffectiveFrom == from, ct);
        var isNew = row is null;
        row ??= new KpiRuleSet { RoleCode = req.RoleCode, EffectiveFrom = from };
        row.Json = KpiMetricsService.SerializeRules(req.Rules);
        row.Note = req.Note;
        row.CreatedBy = Actor;
        if (isNew) db.KpiRuleSets.Add(row);

        audit.Record(EntityRules, row.Id, isNew ? "create" : "update",
            $"KPI qoidalari {(isNew ? "yaratildi" : "yangilandi")}: " +
            $"{KpiRuleSeed.RoleLabel(req.RoleCode)} — {from} dan kuchga kiradi",
            null, new { req.RoleCode, EffectiveFrom = from, req.Note });
        await db.SaveChangesAsync(ct);

        return ToDto(row);
    }

    /// <summary>
    /// Excel konstantalarini va cheklist shablonlarini SEED qilish — idempotent.
    /// </summary>
    [HttpPost("rules/seed")]
    [AdminPerm("kpi.rules")]
    public async Task<ActionResult<object>> SeedRules(CancellationToken ct)
    {
        var (rules, templates, items) = await KpiSeedService.EnsureAsync(db, Actor, ct);
        if (rules + templates + items > 0)
            audit.Record(EntityRules, "seed", "create",
                $"KPI seed: {rules} qoidalar to'plami, {templates} cheklist shabloni, {items} band qo'shildi");
        await db.SaveChangesAsync(ct);
        return Ok(new { rules, templates, items });
    }

    private static KpiRuleSetDto ToDto(KpiRuleSet r) => new(
        r.Id, r.RoleCode, KpiRuleSeed.RoleLabel(r.RoleCode), r.EffectiveFrom, r.Note,
        r.CreatedBy, r.CreatedAt,
        KpiMetricsService.DeserializeRules(r.Json) ?? KpiRuleSeed.For(r.RoleCode));

    /// <summary>Berilgan oy joriy oydan OLDINGImi (o'tgan oyga versiya kiritish taqiqlangan).</summary>
    private static bool IsPastMonth(string month) =>
        string.CompareOrdinal(month, AppClock.Today.ToString("yyyy-MM")) < 0;

    /* =========================================================================================
     *  OY (kalkulyator)
     * ====================================================================================== */

    /// <summary>Bitta xodimning oylik hisobi (kirish raqamlari + koeffitsientlar + summa).</summary>
    [HttpGet("month")]
    [AdminPerm("kpi.month", ReadRequiresPerm = true)]
    public async Task<ActionResult<KpiMonthDto>> Month(
        [FromQuery] string? userId, [FromQuery] string? month, CancellationToken ct)
    {
        if (!ResolveUser(userId, out var uid, out var err)) return StatusCode(403, new { message = err });
        return await KpiMetricsService.MonthAsync(db, uid, MonthOr(month), ct);
    }

    /// <summary>Barcha KPI xodimlari bo'yicha oylik jadval.</summary>
    [HttpGet("month/all")]
    [AdminPerm("kpi.month", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<KpiMonthDto>>> MonthAll([FromQuery] string? month, CancellationToken ct) =>
        await KpiMetricsService.MonthAllAsync(db, MonthOr(month), ct);

    /* =========================================================================================
     *  BUGUN
     * ====================================================================================== */

    /// <summary>Kunlik norma, cheklist, signallar va erta ogohlantirish ro'yxatlari.</summary>
    [HttpGet("today")]
    [AdminPerm("kpi.today", ReadRequiresPerm = true)]
    public async Task<ActionResult<KpiTodayDto>> Today(
        [FromQuery] string? userId, [FromQuery] string? date, CancellationToken ct)
    {
        if (!ResolveUser(userId, out var uid, out var err)) return StatusCode(403, new { message = err });
        var today = await KpiMetricsService.TodayAsync(db, uid, DateOr(date), ct);

        // ⚠️ OYLIK PROGNOZI — bu sahifadagi YAGONA pul raqami, ya'ni `kpi.today` ruxsati
        // (cheklist to'ldirish uchun beriladi) orqali BEGONA xodimning maoshi ko'rinib
        // ketardi. Kalkulyator sahifasining kaliti (`kpi.month`) bo'lmagan odam faqat O'Z
        // prognozini ko'radi; qolgan hammasi (norma, cheklist, signal) o'z joyida qoladi —
        // rahbar cheklistni baribir tekshira oladi.
        if (!string.Equals(uid, CurrentUserId, StringComparison.Ordinal) &&
            !AdminPermAttribute.HasSectionAccess(User, "kpi.month"))
            today = today with { Forecast = null };

        return today;
    }

    /// <summary>Cheklist bandini belgilash (done | failed | na).</summary>
    /// <remarks>⚠️ UPSERT: <c>(UserId, Date, ItemId)</c> unikal indeks bilan qulflangan — ikkinchi
    /// marta <c>Add</c> qilinsa Postgres <c>23505</c> bilan yiqilardi (tez-tez bosishda yoki
    /// avtomatik tekshiruv qo'lda belgilash bilan to'qnashganda).</remarks>
    [HttpPost("today/check")]
    [AdminPerm("kpi.today")]
    public async Task<IActionResult> Check(SaveChecklistCheckRequest req, CancellationToken ct)
    {
        string[] states = [KpiConst.CheckDone, KpiConst.CheckFailed, KpiConst.CheckNa];
        if (!states.Contains(req.State)) return BadRequest(new { message = $"Noma'lum holat: {req.State}" });
        if (!ResolveUser(req.UserId, out var uid, out var err)) return StatusCode(403, new { message = err });

        var itemExists = await db.ChecklistTemplateItems.AsNoTracking().AnyAsync(i => i.Id == req.ItemId, ct);
        if (!itemExists) return BadRequest(new { message = "Cheklist bandi topilmadi" });

        var date = DateOr(req.Date);
        var row = await db.ChecklistEntries
            .FirstOrDefaultAsync(e => e.UserId == uid && e.Date == date && e.ItemId == req.ItemId, ct);
        if (row is null)
        {
            row = new ChecklistEntry { UserId = uid, Date = date, ItemId = req.ItemId };
            db.ChecklistEntries.Add(row);
        }
        row.State = req.State;
        row.Note = req.Note;
        row.Source = KpiConst.SourceManual;
        row.UpdatedAt = AppClock.Iso();
        row.UpdatedBy = Actor;

        // ⚠️ Cheklist belgisi AUDITGA yozilmaydi: kuniga 30+ belgi × har xodim — tarix
        // ro'yxati boshqa hamma narsani ko'mib tashlardi. "Kim belgiladi" yozuvning O'ZIDA
        // (`UpdatedBy`/`UpdatedAt`) saqlanadi.
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* =========================================================================================
     *  TIKETLAR
     * ====================================================================================== */

    /// <summary>Tiketlar ro'yxati (davr, xodim va holat bo'yicha filtr).</summary>
    [HttpGet("tickets")]
    [AdminPerm("kpi.tickets", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<KpiTicketDto>>> Tickets(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? userId,
        [FromQuery] string? status, CancellationToken ct)
    {
        var q = db.KpiTickets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(from)) q = q.Where(t => string.Compare(t.Date, from) >= 0);
        if (!string.IsNullOrWhiteSpace(to)) q = q.Where(t => string.Compare(t.Date, to) <= 0);
        if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(t => t.UserId == userId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);

        var rows = await q.ToListAsync(ct);
        return rows
            .OrderByDescending(t => t.Date, StringComparer.Ordinal)
            .ThenByDescending(t => t.IssuedAt, StringComparer.Ordinal)
            .Select(ToDto).ToList();
    }

    /// <summary>Tiket qo'yish.</summary>
    [HttpPost("tickets")]
    [AdminPerm("kpi.tickets")]
    public async Task<ActionResult<KpiTicketDto>> CreateTicket(SaveKpiTicketRequest req, CancellationToken ct)
    {
        var error = ValidateTicket(req);
        if (error is not null) return BadRequest(new { message = error });

        var name = await db.Users.AsNoTracking().Where(u => u.Id == req.UserId)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct);
        if (name is null) return BadRequest(new { message = "Xodim topilmadi" });

        var t = new KpiTicket
        {
            UserId = req.UserId, UserName = name, Date = DateOr(req.Date),
            ReasonCode = req.ReasonCode, CriterionNo = req.CriterionNo,
            CallId = req.CallId, Note = req.Note,
            Status = string.IsNullOrWhiteSpace(req.Status) ? KpiConst.TicketProposed : req.Status,
            IssuedBy = Actor,
        };
        db.KpiTickets.Add(t);

        audit.Record(EntityTicket, t.Id, "create",
            $"KPI tiketi qo'yildi: {name} — {KpiTicketCatalog.ReasonLabel(t.ReasonCode)} " +
            $"({t.Date}, holat: {t.Status})");
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    /// <summary>Tiket holati/izohi (tasdiqlash, e'tiroz, bekor qilish).</summary>
    /// <remarks>⚠️ Tiket O'CHIRILMAYDI, holati o'zgaradi: <c>cancelled</c> ham TARIX — bir marta
    /// qo'yilgan va keyin olib tashlangan jarima haqidagi savol javobsiz qolmasin.</remarks>
    [HttpPut("tickets/{id}")]
    [AdminPerm("kpi.tickets")]
    public async Task<ActionResult<KpiTicketDto>> UpdateTicket(string id, SaveKpiTicketRequest req, CancellationToken ct)
    {
        var t = await db.KpiTickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var error = ValidateTicket(req);
        if (error is not null) return BadRequest(new { message = error });

        var before = new { t.Status, t.ReasonCode, t.Date, t.Note };
        var statusChanged = !string.Equals(t.Status, req.Status, StringComparison.Ordinal);

        t.Date = DateOr(req.Date);
        t.ReasonCode = req.ReasonCode;
        t.CriterionNo = req.CriterionNo;
        t.CallId = req.CallId;
        t.Note = req.Note;
        t.DisputeNote = req.DisputeNote;
        if (!string.IsNullOrWhiteSpace(req.Status)) t.Status = req.Status;
        if (statusChanged) { t.ResolvedBy = Actor; t.ResolvedAt = AppClock.Iso(); }

        audit.Record(EntityTicket, t.Id, "update",
            $"KPI tiketi yangilandi: {t.UserName} — {KpiTicketCatalog.ReasonLabel(t.ReasonCode)} " +
            $"(holat: {before.Status} → {t.Status})",
            before, new { t.Status, t.ReasonCode, t.Date, t.Note });
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    /// <summary>Tiketni butunlay o'chirish (xato kiritilgan yozuv uchun).</summary>
    [HttpDelete("tickets/{id}")]
    [AdminPerm("kpi.tickets")]
    public async Task<IActionResult> DeleteTicket(string id, CancellationToken ct)
    {
        var t = await db.KpiTickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        db.KpiTickets.Remove(t);
        audit.Record(EntityTicket, id, "delete",
            $"KPI tiketi o'chirildi: {t.UserName} — {KpiTicketCatalog.ReasonLabel(t.ReasonCode)} ({t.Date})");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sabablar katalogi + 13 audit mezoni + KPI profili bor xodimlar.</summary>
    [HttpGet("tickets/reasons")]
    public async Task<ActionResult<KpiTicketMetaDto>> TicketMeta(CancellationToken ct)
    {
        var profiles = await db.KpiProfiles.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        var ids = profiles.Select(p => p.UserId).ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return new KpiTicketMetaDto(
            KpiTicketCatalog.Reasons
                .Select(r => new KpiTicketReasonDto(r.Code, r.Label, r.RoleCode, r.CriterionNo)).ToList(),
            KpiTicketCatalog.Criteria.Select(c => new KpiCriterionDto(c.No, c.Label)).ToList(),
            profiles
                .Select(p => new KpiStaffDto(p.UserId, names.GetValueOrDefault(p.UserId, "(o'chirilgan xodim)"),
                    p.RoleCode, KpiRuleSeed.RoleLabel(p.RoleCode)))
                .OrderBy(s => s.UserName, StringComparer.CurrentCulture)
                .ToList());
    }

    /// <summary>Haftalik sifat nazorati uchun kamida <c>count</c> ta TASODIFIY qo'ng'iroq.</summary>
    /// <remarks>
    /// ⚠️ Filtr ATAYIN qattiq: oxirgi 7 kun, YOZUVI BOR va 60 soniyadan uzun. Qisqa/yozuvsiz
    /// qo'ng'iroqni tinglab bo'lmaydi — namunaga tushsa rahbar uni o'tkazib yuborar va nazorat
    /// aslida bo'lmasdi.
    ///
    /// ⚠️ Yozuv FAYLI manzili qaytmaydi (faqat <c>HasRecording</c> bayrog'i): fayl
    /// avtorizatsiyalangan qo'ng'iroqlar bo'limidan tinglanadi
    /// (<c>.claude/rules/uploads-security.md</c> printsipi).
    /// </remarks>
    [HttpGet("tickets/calls")]
    [AdminPerm("kpi.tickets", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<KpiCallSampleDto>>> CallSamples(
        [FromQuery] string? userId, [FromQuery] int count = 5, CancellationToken ct = default)
    {
        if (!ResolveUser(userId, out var uid, out var err)) return StatusCode(403, new { message = err });
        var take = Math.Clamp(count, 1, 25);
        var since = AppClock.Now.AddDays(-7);

        var rows = await db.Calls.AsNoTracking()
            .Where(c => c.OperatorUserId == uid && c.StartedAt >= since
                        && c.DurationSeconds > 60 && c.RecordingFile != "")
            .Select(c => new
            {
                c.Id, c.PhoneNumber, c.Direction, c.StartedAt, c.DurationSeconds,
                c.RecordingFile, c.Transcript, c.AiAnalysis,
            })
            .ToListAsync(ct);

        // TASODIFIY tanlov XOTIRADA: `ORDER BY random()` indeksdan foydalana olmaydi va bu
        // yerda ro'yxat baribir kichik (bir xodimning haftalik qo'ng'iroqlari).
        var rnd = Random.Shared;
        return rows
            .OrderBy(_ => rnd.Next())
            .Take(take)
            .Select(c => new KpiCallSampleDto(
                c.Id, c.PhoneNumber, c.Direction,
                c.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss"), c.DurationSeconds,
                HasRecording: true,
                string.IsNullOrWhiteSpace(c.Transcript) ? null : c.Transcript,
                string.IsNullOrWhiteSpace(c.AiAnalysis) ? null : c.AiAnalysis))
            .ToList();
    }

    private static string? ValidateTicket(SaveKpiTicketRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.UserId)) return "Xodim tanlanmagan";
        if (KpiTicketCatalog.Reasons.All(r => r.Code != req.ReasonCode))
            return $"Noma'lum sabab kaliti: {req.ReasonCode}";
        string[] statuses =
        [
            KpiConst.TicketProposed, KpiConst.TicketConfirmed,
            KpiConst.TicketDisputed, KpiConst.TicketCancelled,
        ];
        if (!string.IsNullOrWhiteSpace(req.Status) && !statuses.Contains(req.Status))
            return $"Noma'lum holat: {req.Status}";
        if (req.CriterionNo is int no && KpiTicketCatalog.Criteria.All(c => c.No != no))
            return $"Noma'lum mezon raqami: {no}";
        return null;
    }

    private static KpiTicketDto ToDto(KpiTicket t) => new(
        t.Id, t.UserId, t.UserName, t.Date, t.ReasonCode, KpiTicketCatalog.ReasonLabel(t.ReasonCode),
        t.CriterionNo, KpiTicketCatalog.CriterionLabel(t.CriterionNo), t.CallId, t.Note, t.Status,
        t.IssuedBy, t.IssuedAt, t.DisputeNote, t.ResolvedBy, t.ResolvedAt);

    /* =========================================================================================
     *  YOPISH
     * ====================================================================================== */

    /// <summary>Oy natijalari (qoralama + tasdiqlangan) va birlik iqtisodiyoti.</summary>
    [HttpGet("close")]
    [AdminPerm("kpi.close", ReadRequiresPerm = true)]
    public async Task<ActionResult<KpiCloseDto>> Close([FromQuery] string? month, CancellationToken ct)
    {
        var m = MonthOr(month);
        var rows = await KpiMetricsService.MonthAllAsync(db, m, ct);
        var unit = await KpiMetricsService.UnitEconomicsAsync(db, m, ct);
        return new KpiCloseDto(m, rows, unit);
    }

    /// <summary>Uchta sog'lomlik indikatori (alohida yengil so'rov).</summary>
    [HttpGet("close/unit-economics")]
    [AdminPerm("kpi.close", ReadRequiresPerm = true)]
    public async Task<ActionResult<KpiUnitEconomicsDto>> UnitEconomics([FromQuery] string? month, CancellationToken ct) =>
        await KpiMetricsService.UnitEconomicsAsync(db, MonthOr(month), ct);

    /// <summary>
    /// Oy natijasini MUZLATISH: qayta hisoblanadi va kirish raqamlari, koeffitsientlar hamda
    /// qaysi qoida/oklad versiyasi ishlatilgani bilan birga saqlanadi.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>SHIFTDAN OSHGAN natija oddiy tasdiq bilan yopilmaydi</b> — so'rovda
    /// <c>approveOverCap=true</c> bo'lishi SHART. Sabab: summa avtomatik KESILMAYDI
    /// (<c>KpiCalculator</c>), ya'ni chegara faqat "rahbar alohida qaror qabul qilsin" degani.
    /// Ikkinchi tasdiq faqat klientda bo'lsa, boshqa mijoz (yoki eski versiya) undan chetlab
    /// o'tib, chegara umuman ishlamay qolardi.
    ///
    /// ⚠️ UPSERT: <c>(UserId, Month)</c> unikal indeks (spetsifikatsiya §17.4).
    /// </remarks>
    [HttpPost("close/{userId}")]
    [AdminPerm("kpi.close")]
    public async Task<ActionResult<KpiMonthDto>> Confirm(
        string userId, [FromQuery] string? month, [FromQuery] bool approveOverCap = false,
        CancellationToken ct = default)
    {
        var m = MonthOr(month);

        // Avval MAVJUD yozuvni tozalaymiz: tasdiqlangan qator turgan bo'lsa `MonthAsync`
        // muzlatilgan qiymatni qaytarardi va "qayta tasdiqlash" hech narsani yangilamasdi.
        var row = await db.KpiMonthResults.FirstOrDefaultAsync(r => r.UserId == userId && r.Month == m, ct);
        if (row is { Status: KpiConst.MonthConfirmed })
            return BadRequest(new { message = "Bu oy allaqachon tasdiqlangan. Avval «Qayta ochish» qiling." });

        var dto = await KpiMetricsService.MonthAsync(db, userId, m, ct);
        if (dto.RulesMissing)
            return BadRequest(new { message = "Qoidalar to'plami yo'q — oyni tasdiqlab bo'lmaydi. Avval seed qiling." });
        if (dto.CapExceeded && !approveOverCap)
            return BadRequest(new
            {
                message = $"Hisoblangan oylik yuqori chegaradan oshdi ({AuditService.Money(dto.Salary)} so'm" +
                          (dto.Cap is decimal cap ? $", chegara {AuditService.Money(cap)} so'm" : "") +
                          "). Summa KESILMAYDI — tasdiqlash uchun alohida ruxsat bering " +
                          "(approveOverCap).",
                capExceeded = true,
            });

        var salaryVersion = await db.KpiProfileSalaries.AsNoTracking()
            .Where(s => s.UserId == userId).ToListAsync(ct);

        var isNew = row is null;
        row ??= new KpiMonthResult { UserId = userId, Month = m };
        row.UserName = dto.UserName;
        row.RoleCode = dto.RoleCode;
        row.InputsJson = KpiMetricsService.SerializeInputs(dto.Inputs);
        row.CoefsJson = KpiMetricsService.SerializeCoefs(dto);
        row.BaseSalary = dto.BaseSalary;
        row.BonusTotal = dto.BonusTotal;
        row.FineTotal = dto.FineTotal;
        row.Salary = dto.Salary;
        row.GuaranteeApplied = dto.GuaranteeApplied;
        row.CapExceeded = dto.CapExceeded;
        row.RuleSetId = dto.RuleSetId;
        row.SalaryVersionId = KpiVersioning.SalaryFor(salaryVersion, userId, m)?.Id;
        row.Status = KpiConst.MonthConfirmed;
        row.Note = dto.Warning;
        row.ConfirmedBy = Actor;
        row.ConfirmedAt = AppClock.Iso();
        if (isNew) db.KpiMonthResults.Add(row);

        audit.Record(EntityResult, row.Id, isNew ? "create" : "update",
            $"KPI oyi TASDIQLANDI: {dto.UserName} — {m}, {AuditService.Money(dto.Salary)} so'm" +
            (dto.GuaranteeApplied ? " (kafolat qo'llandi)" : "") +
            (dto.CapExceeded ? " ⚠️ chegaradan oshgan summa alohida ruxsat bilan tasdiqlandi" : ""),
            null, new { dto.BaseSalary, dto.BonusTotal, dto.FineTotal, dto.Salary, dto.RuleSetId });
        await db.SaveChangesAsync(ct);

        return await KpiMetricsService.MonthAsync(db, userId, m, ct);
    }

    /// <summary>Tasdiqlangan oyni QAYTA OCHISH (qoralamaga qaytarish).</summary>
    /// <remarks>⚠️ Qator O'CHIRILMAYDI, holati <c>draft</c> ga qaytadi: kim tasdiqlagani va
    /// qanday raqamlar bilan tasdiqlangani tarixda qolsin.</remarks>
    [HttpPost("close/{userId}/reopen")]
    [AdminPerm("kpi.close")]
    public async Task<ActionResult<KpiMonthDto>> Reopen(string userId, [FromQuery] string? month, CancellationToken ct)
    {
        var m = MonthOr(month);
        var row = await db.KpiMonthResults.FirstOrDefaultAsync(r => r.UserId == userId && r.Month == m, ct);
        if (row is null) return NotFound(new { message = "Bu oy uchun tasdiqlangan natija yo'q" });

        row.Status = KpiConst.MonthDraft;
        audit.Record(EntityResult, row.Id, "update",
            $"KPI oyi QAYTA OCHILDI: {row.UserName} — {m} (avval {AuditService.Money(row.Salary)} so'm bilan " +
            $"tasdiqlangan edi, tasdiqlagan: {row.ConfirmedBy})");
        await db.SaveChangesAsync(ct);

        return await KpiMetricsService.MonthAsync(db, userId, m, ct);
    }

    /// <summary>Oy natijalarini Excel'ga yuklab olish.</summary>
    [HttpGet("close/export")]
    [AdminPerm("kpi.close", ReadRequiresPerm = true)]
    public async Task<IActionResult> Export([FromQuery] string? month, CancellationToken ct)
    {
        var m = MonthOr(month);
        var rows = await KpiMetricsService.MonthAllAsync(db, m, ct);

        string[] headers =
        [
            "Xodim", "Rol", "Oy", "Holat", "Oklad", "Bonus", "Jarima", "Hisoblangan", "JAMI",
            "Kafolat", "Chegaradan oshdi", "Tasdiqladi", "Tasdiqlangan vaqt", "Izoh",
        ];

        var body = rows.Select(r => (IReadOnlyList<string>)new List<string>
        {
            r.UserName,
            r.RoleLabel,
            r.Month,
            r.Status == KpiConst.MonthConfirmed ? "Tasdiqlangan" : "Qoralama",
            Num(r.BaseSalary), Num(r.BonusTotal), Num(r.FineTotal), Num(r.Computed), Num(r.Salary),
            r.GuaranteeApplied ? "ha" : "",
            r.CapExceeded ? "ha" : "",
            r.ConfirmedBy ?? "", r.ConfirmedAt ?? "", r.Warning ?? "",
        }).ToList();

        // Ikkinchi varaq — KIRISH RAQAMLARI: "jami" ustuni bahsni hal qilmaydi, "qayerdan"
        // ustuni hal qiladi (Excel'ni ochgan odam manbani ham ko'rsin).
        string[] inputHeaders = ["Xodim", "Ko'rsatkich", "Qiymat", "Qayerdan", "Taxminiy"];
        var inputRows = rows.SelectMany(r => r.Inputs.Select(i => (IReadOnlyList<string>)new List<string>
        {
            r.UserName, i.Label,
            i.Value.ToString("0.####", CultureInfo.InvariantCulture),
            i.Source, i.Estimated ? "ha" : "",
        })).ToList();

        var bytes = ExcelExport.Build(
        [
            new ExcelExport.SheetSpec($"KPI {m}", headers, body),
            new ExcelExport.SheetSpec("Kirish raqamlari", inputHeaders, inputRows),
        ]);
        return File(bytes, XlsxMime, $"kpi_{m}.xlsx");

        static string Num(decimal v) => v.ToString("0", CultureInfo.InvariantCulture);
    }

    /* =========================================================================================
     *  CHEKLIST SHABLONLARI
     * ====================================================================================== */

    /// <summary>Cheklist shablonlari (rol bo'yicha) — bandlari bilan.</summary>
    [HttpGet("checklist/templates")]
    public async Task<ActionResult<List<KpiChecklistTemplateDto>>> Templates(
        [FromQuery] string? role, CancellationToken ct)
    {
        var q = db.ChecklistTemplates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role)) q = q.Where(t => t.RoleCode == role);
        var templates = await q.ToListAsync(ct);
        if (templates.Count == 0) return new List<KpiChecklistTemplateDto>();

        var ids = templates.Select(t => t.Id).ToList();
        var items = await db.ChecklistTemplateItems.AsNoTracking()
            .Where(i => ids.Contains(i.TemplateId))
            .OrderBy(i => i.Order).ThenBy(i => i.No)
            .ToListAsync(ct);

        return templates
            .OrderBy(t => t.RoleCode, StringComparer.Ordinal)
            .Select(t => new KpiChecklistTemplateDto(
                t.Id, t.RoleCode, KpiRuleSeed.RoleLabel(t.RoleCode), t.Name, t.IsActive,
                items.Where(i => i.TemplateId == t.Id).Select(ToDto).ToList()))
            .ToList();
    }

    /// <summary>Ikkala Excel cheklistini seed qilish — idempotent (<c>rules/seed</c> bilan bir xil yo'l).</summary>
    [HttpPost("checklist/templates/seed")]
    [AdminPerm("kpi.rules")]
    public async Task<ActionResult<object>> SeedTemplates(CancellationToken ct)
    {
        var (rules, templates, items) = await KpiSeedService.EnsureAsync(db, Actor, ct);
        if (rules + templates + items > 0)
            audit.Record(EntityRules, "seed", "create",
                $"KPI cheklist seed: {templates} shablon, {items} band qo'shildi");
        await db.SaveChangesAsync(ct);
        return Ok(new { templates, items });
    }

    /// <summary>Shablon bandini tahrirlash — QISMAN (berilmagan maydon tegilmaydi).</summary>
    /// <remarks>⚠️ Band O'CHIRILMAYDI va yangisi shu yerdan qo'shilmaydi: kunlik belgilar
    /// (<c>ChecklistEntry</c>) band Id'siga bog'langan, ya'ni o'chirilgan band o'tgan kunlarning
    /// samaradorlik foizini jimgina o'zgartirib yuborardi. Ro'yxatni kengaytirish — seed orqali.</remarks>
    [HttpPut("checklist/items/{id}")]
    [AdminPerm("kpi.rules")]
    public async Task<ActionResult<KpiChecklistItemDefDto>> UpdateItem(
        string id, UpdateChecklistItemRequest req, CancellationToken ct)
    {
        var item = await db.ChecklistTemplateItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        if (req.CriterionNo is int no && no != 0 && KpiTicketCatalog.Criteria.All(c => c.No != no))
            return BadRequest(new { message = $"Noma'lum mezon raqami: {no}" });

        var before = new { item.No, item.TimeBlock, item.Text, item.Norm, item.KpiTag, item.CriterionNo, item.Order };

        if (req.No is int n) item.No = n;
        if (req.TimeBlock is not null) item.TimeBlock = req.TimeBlock;
        if (req.Text is not null) item.Text = req.Text;
        if (req.Norm is not null) item.Norm = req.Norm;
        if (req.KpiTag is not null) item.KpiTag = req.KpiTag.Length == 0 ? null : req.KpiTag;
        if (req.CriterionNo is int c2) item.CriterionNo = c2 == 0 ? null : c2;
        if (req.AutoCheckKey is not null) item.AutoCheckKey = req.AutoCheckKey.Length == 0 ? null : req.AutoCheckKey;
        if (req.Order is int o) item.Order = o;

        audit.Record(EntityChecklistItem, item.Id, "update",
            $"Cheklist bandi tahrirlandi: №{item.No} — {item.Text}",
            before, new { item.No, item.TimeBlock, item.Text, item.Norm, item.KpiTag, item.CriterionNo, item.Order });
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    private static KpiChecklistItemDefDto ToDto(ChecklistTemplateItem i) => new(
        i.Id, i.No, i.TimeBlock, i.Text, i.Norm, i.KpiTag, i.CriterionNo, i.AutoCheckKey, i.Order);

    /* =========================================================================================
     *  SNAPSHOTLAR
     * ====================================================================================== */

    /// <summary>Oy boshi snapshotlari.</summary>
    [HttpGet("snapshots")]
    public async Task<ActionResult<List<KpiSnapshotDto>>> Snapshots([FromQuery] string? month, CancellationToken ct)
    {
        var m = MonthOr(month);
        var rows = await db.KpiMonthSnapshots.AsNoTracking().Where(s => s.Month == m).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Oy boshi snapshotini QO'LDA olish (fon xizmati o'tkazib yuborgan yoki oy o'rtasida
    /// modul yoqilgan holat uchun).
    /// </summary>
    /// <remarks>
    /// ⚠️ Yozish mantig'i <see cref="KpiSnapshotService.TakeAsync"/> da — fon xizmati bilan
    /// AYNAN bir xil yo'l. Controller o'z upsert'ini yozsa, ikkalasi vaqt o'tishi bilan
    /// ayrilib ketar va "qo'lda olingan surat" bilan "avtomatik surat" boshqa-boshqa raqam
    /// berardi (ya'ni ketish foizi qaysi yo'l bilan olinganiga bog'liq bo'lib qolardi).
    ///
    /// ⚠️ O'TGAN oyning surati QAYTA YOZILMAYDI (xizmatning o'zi rad etadi): modulning butun
    /// ma'nosi o'sha paytdagi holatni muzlatib qo'yishda. Bunday holatda 0 qator yoziladi va
    /// mavjud suratlar o'zgarishsiz qaytadi.
    /// </remarks>
    [HttpPost("snapshots")]
    [AdminPerm("kpi.rules")]
    public async Task<ActionResult<List<KpiSnapshotDto>>> TakeSnapshot(
        [FromQuery] string? month, CancellationToken ct)
    {
        var m = MonthOr(month);
        var written = await KpiSnapshotService.TakeAsync(db, m, ct);

        if (written > 0)
        {
            audit.Record(EntityRules, $"snapshot:{m}", "create",
                $"KPI oy boshi surati QO'LDA olindi: {m} — {written} qator yozildi");
            await db.SaveChangesAsync(ct);
        }

        var rows = await db.KpiMonthSnapshots.AsNoTracking().Where(s => s.Month == m).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>⚠️ <c>UserId</c>: entity'da bo'sh satr = MARKAZ darajasi, DTO'da esa <c>null</c>.</summary>
    private static KpiSnapshotDto ToDto(KpiMonthSnapshot s) => new(
        s.Id, s.Month, s.RoleCode,
        string.IsNullOrEmpty(s.UserId) ? null : s.UserId,
        s.TakenAt,
        ParseValues(s.Json));

    private static Dictionary<string, double> ParseValues(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, double>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                   ?? new Dictionary<string, double>();
        }
        catch (System.Text.Json.JsonException)
        {
            // Buzuq JSON butun ro'yxatni yiqitmasin — qator bo'sh qiymatlar bilan ko'rinadi.
            return new Dictionary<string, double>();
        }
    }
}
