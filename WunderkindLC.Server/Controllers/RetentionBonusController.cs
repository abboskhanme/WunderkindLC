using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'QUVCHINI USHLAB TURISH BONUSI — "O'quvchilar → Bonus hisoboti" bo'limining API'si.
///
/// <para>Butun mantiq <see cref="RetentionBonusService"/> da; bu controller faqat HTTP qobig'i
/// (yuklash, tekshirish natijasini tarjima qilish, audit).</para>
///
/// <para>Ruxsat kaliti <c>finance</c>: jadvalni KO'RISH staff uchun ochiq (<c>AdminPerm</c>
/// qoidasi — GET har doim ochiq), bonus BERISH/bekor qilish uchun esa moliya ruxsati kerak.
/// Frontend'da sahifa <c>students</c> menyusida turadi, «Bonus berish» tugmasi
/// <c>can('finance','edit')</c> bilan ko'rinadi.</para>
/// </summary>
[ApiController]
[Authorize]
// Bonus — MOLIYA bo'limining "Bonus hisoboti" sahifasi (menyuda o'quvchilar bilan tursa ham).
// ⚠️ ATAYIN `students.*` EMAS: bu pul beradigan amal, o'quvchilar bo'limi ruxsati bilan
// ochilib ketmasin. `finance` (butun bo'lim) merosi bilan bu yerga baribir yetadi.
[AdminPerm("finance.bonus")]
[Route("api/admin/retention-bonus")]
public class RetentionBonusController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";

    /// <summary>Bonus hisoboti jadvali: ptichkali o'quvchilar, oylik holatlar, progress va tarix.</summary>
    [HttpGet]
    public async Task<ActionResult<RetentionReportDto>> Get(CancellationToken ct) =>
        await RetentionBonusService.BuildReportAsync(db, null, ct);

    /// <summary>Tayyor (sanoq to'lgan) bonuslar soni — menyudagi belgi uchun (yengil so'rov).</summary>
    [HttpGet("ready-count")]
    public async Task<ActionResult<int>> ReadyCount(CancellationToken ct) =>
        (await RetentionBonusService.BuildReportAsync(db, null, ct)).ReadyCount;

    /// <summary>
    /// Bonus berish. Holat serverda QAYTA tekshiriladi (jadval eskirgan yoki ikki admin bir vaqtda
    /// bosgan bo'lishi mumkin) va taqsimot yig'indisi jami summaga teng bo'lishi shart.
    ///
    /// <para><b>BIR SO'ROVDAN ORTIG'I O'TMAYDI.</b> Tekshiruv (<c>BuildReportAsync</c> — sikl
    /// tayyormi, o'qituvchi bloklanganmi) bilan yozuv ORASIDA boshqa so'rov kirib ulgursa, ikkalasi
    /// ham "tayyor" holatni ko'rib IKKITA bonus yozib qo'yardi — «bonusni ikki marta hisoblab
    /// qo'ymoqda» shikoyatining aynan shu yo'li bor edi (ikki marta bosish, ikkinchi oyna yoki ikki
    /// admin). Ayniqsa bir o'quvchining IKKI FANI bir o'qituvchiga tegishli bo'lsa: unikal indeks
    /// <c>(StudentId, CourseId, CycleNo)</c> ni ushlamaydi (fan boshqa) va «bir o'qituvchi — bir
    /// o'quvchi — bitta bonus» qoidasi buzilardi.
    /// Yechim: shu O'QUVCHI bo'yicha bonus berish PostgreSQL advisory lock bilan
    /// ketma-ketlashtiriladi — ikkinchi so'rov birinchisini kutadi va allaqachon YOZILGAN holatni
    /// ko'rib odatdagi tushunarli xato bilan rad etiladi.</para>
    /// </summary>
    [HttpPost("awards")]
    public async Task<IActionResult> Give(GiveRetentionBonusRequest req, CancellationToken ct)
    {
        // Qulf kaliti — o'quvchi (sikl ham, o'qituvchi bloki ham AYNAN o'quvchi kesimida tekshiriladi).
        var lockKey = $"retention-bonus:{req.StudentId}";
        // Sessiya darajasidagi qulf bitta ulanishda ushlanadi — shuning uchun ulanish qo'lda ochiladi
        // (aks holda EF har buyruq uchun poolda boshqa ulanish olishi mumkin edi).
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock(hashtext({lockKey}))", ct);

            var (award, error) = await RetentionBonusService.GiveAsync(db, req, Actor, ct);
            if (error is not null || award is null) return BadRequest(new { message = error });

            audit.Record(AuditService.EntityStudent, award.StudentId, "create",
                $"Ushlab turish bonusi berildi: {AuditService.Money(award.TotalAmount)} so'm " +
                $"({award.StudentName} · {award.CourseName}, {award.PeriodFrom}…{award.PeriodTo}, {award.CycleNo}-sikl)",
                after: new { award.TotalAmount, award.CourseId, award.CourseName, award.PeriodFrom, award.PeriodTo, award.CycleNo, req.Shares },
                studentId: award.StudentId);

            await db.SaveChangesAsync(ct);
            return Ok(new { id = award.Id });
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Oxirgi to'siq: unikal indeks (StudentId, CourseId, CycleNo). Qulf bilan bu yerga
            // yetib kelinmaydi, lekin kelib qolsa — foydalanuvchi 500 emas, tushunarli xabar ko'rsin.
            // (Faqat UNIKAL buzilishi ushlanadi — boshqa saqlash xatolari yashirilmaydi.)
            return Conflict(new { message = "Bu sikl uchun bonus allaqachon berilgan — sahifani yangilang" });
        }
        finally
        {
            try
            {
                // `unlock_all`, aniq `pg_advisory_unlock` EMAS. Sabab: EF'da EnableRetryOnFailure
                // yoqilgan (Program.cs) — tranzient xatoda `pg_advisory_lock` QAYTA bajarilishi
                // mumkin. Advisory qulf re-entrant: ikki marta olingan bo'lsa bir marta bo'shatish
                // YETMAYDI va qulf ushlab turgan ulanish poolga qaytardi — o'sha o'quvchi bo'yicha
                // keyingi barcha so'rovlar abadiy kutib qolardi. `unlock_all` sanoqdan qat'i nazar
                // hammasini bo'shatadi; loyihada boshqa advisory qulf ishlatilmagani uchun xavfsiz.
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock_all()", CancellationToken.None);
            }
            catch (Exception)
            {
                // Ulanish uzilgan bo'lsa qulf o'z-o'zidan bo'shaydi (sessiya tugadi) — asl xatoni
                // yashirib yubormaslik uchun bu yerdagi xato yutiladi.
            }
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Bonusni bekor qilish (xato kiritilgan bo'lsa). Yozuv o'chirilmaydi — tarixda "cancelled"
    /// bo'lib qoladi. Sanoq QAYTARILMAYDI va bekor qilingan bonus o'qituvchi(lar)ni shu o'quvchi
    /// orqali yangi bonusdan bloklab turaveradi (markaz egasining talabi).
    /// </summary>
    [HttpPost("awards/{id}/cancel")]
    public async Task<IActionResult> Cancel(string id, CancelRetentionBonusRequest? req, CancellationToken ct)
    {
        var award = await db.RetentionBonusAwards.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        var error = await RetentionBonusService.CancelAsync(db, id, req?.Reason, ct);
        if (error is not null) return BadRequest(new { message = error });

        if (award is not null)
            audit.Record(AuditService.EntityStudent, award.StudentId, "update",
                $"Ushlab turish bonusi BEKOR qilindi: {AuditService.Money(award.TotalAmount)} so'm " +
                $"({award.StudentName} · {award.CourseName}){(string.IsNullOrWhiteSpace(req?.Reason) ? "" : $" — {req!.Reason}")}",
                before: new { award.Status, award.TotalAmount },
                studentId: award.StudentId);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Uzilgan siklni yangi oydan qayta boshlash — FAQAT ko'rsatilgan fan uchun
    /// (avvalgi sikl tarixda qoladi, boshqa fanlar tegilmaydi).</summary>
    [HttpPost("students/{id}/restart")]
    public async Task<IActionResult> Restart(string id, RestartRetentionRequest req, CancellationToken ct)
    {
        var error = await RetentionBonusService.RestartAsync(db, id, req.CourseId, req.StartMonth, Actor, ct);
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(AuditService.EntityStudent, id, "update",
            $"Ushlab turish bonusi sanog'i qayta boshlandi ({req.StartMonth})",
            after: new { req.CourseId, req.StartMonth }, studentId: id);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Bitta O'QUVCHINING bonus holati — o'quvchi profilidagi «Bonus» bo'limi uchun: har fan
    /// bo'yicha sanoq, oylik kataklar va berilgan bonuslar tarixi.
    ///
    /// <para>FAQAT admin/superadmin. Odatdagi <see cref="AdminPermAttribute"/> qoidasidan
    /// (xodimga GET har doim ochiq) ATAYIN qattiqroq: bonus — o'qituvchi haqiga taalluqli
    /// moliyaviy ma'lumot va uni har bir xodim ko'rishi shart emas. Xodim ruxsati bilan ham
    /// ochilmaydi — markaz egasining talabi.</para>
    /// </summary>
    [HttpGet("student/{id}")]
    public async Task<ActionResult<RetentionReportDto>> ForStudent(string id, CancellationToken ct)
    {
        if (!User.IsInRole(Roles.Admin) && !User.IsInRole(Roles.SuperAdmin)) return Forbid();
        return await RetentionBonusService.BuildReportAsync(db, id, ct);
    }

    /// <summary>Bitta o'qituvchining bonuslari — profil "Bonus" tabi va o'qituvchi ilovasi uchun.</summary>
    [HttpGet("teacher/{id}")]
    public async Task<ActionResult<TeacherRetentionSummaryDto>> ForTeacher(string id, CancellationToken ct) =>
        await RetentionBonusService.ForTeacherAsync(db, id, ct);

    /// <summary>Sozlamalar (<c>CenterMeta</c>): necha oy talab qilinadi, ruxsat etilgan tanaffus, standart summa.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<RetentionSettingsDto>> GetSettings(CancellationToken ct) =>
        RetentionBonusService.Settings(await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct));

    [HttpPut("settings")]
    public async Task<ActionResult<RetentionSettingsDto>> SaveSettings(RetentionSettingsDto req, CancellationToken ct)
    {
        if (req.MonthsRequired is < 1 or > 36)
            return BadRequest(new { message = "Talab qilinadigan oylar soni 1-36 oralig'ida bo'lishi kerak" });
        if (req.MaxGapMonths is < 0 or > 12)
            return BadRequest(new { message = "Ruxsat etilgan tanaffus 0-12 oy oralig'ida bo'lishi kerak" });
        if (req.DefaultAmount < 0m)
            return BadRequest(new { message = "Standart summa manfiy bo'lishi mumkin emas" });

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null) { meta = new CenterMeta(); db.CenterMeta.Add(meta); }

        meta.RetentionMonthsRequired = req.MonthsRequired;
        meta.RetentionMaxGapMonths = req.MaxGapMonths;
        meta.RetentionDefaultAmount = req.DefaultAmount;
        await db.SaveChangesAsync(ct);

        return RetentionBonusService.Settings(meta);
    }

    /// <summary>Hisobotni Excel'ga yuklash (oylik kataklar matn belgisi bilan).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var report = await RetentionBonusService.BuildReportAsync(db, null, ct);

        var headers = new List<string>
        {
            "F.I.Sh", "Fan", "Guruh", "O'qituvchi", "Dars kunlari", "Boshlanish oyi", "Sikl",
            "Sanoq", "Holat", "Izoh", "Oylar",
        };
        var rows = report.Rows.Select(r => (IReadOnlyList<string>)new List<string>
        {
            r.FullName,
            r.CourseName,
            string.Join(", ", r.Groups.Select(g => g.Name)),
            string.Join(", ", r.Teachers.Select(t => t.Name)),
            r.Days,
            r.StartMonth,
            r.CycleNo.ToString(),
            $"{r.Counted}/{r.Required}",
            StatusLabel(r.Status),
            r.StatusNote,
            string.Join(" ", r.Months.Select(m => $"{m.Month}:{StateLabel(m.State)}")),
        }).ToList();

        return File(ExcelExport.Build("Bonus hisoboti", headers, rows), XlsxMime,
            $"bonus_hisobot_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    private static string StatusLabel(string status) => status switch
    {
        RetentionBonusService.RowReady => "Tayyor",
        RetentionBonusService.RowBroken => "Uzildi",
        RetentionBonusService.RowNotStarted => "Boshlanmagan",
        RetentionBonusService.RowBlocked => "Bloklangan",
        _ => "Yo'lda",
    };

    private static string StateLabel(string state) => state switch
    {
        RetentionBonusService.StatePaid => "to'liq",
        RetentionBonusService.StateDebt => "qarz",
        RetentionBonusService.StateNoCharge => "hisob yo'q",
        RetentionBonusService.StateFrozen => "muzlatilgan",
        _ => "a'zolik yo'q",
    };
}
