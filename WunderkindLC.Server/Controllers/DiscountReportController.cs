using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «CHEGIRMALAR HISOBOTI» — markaz bo'yicha: chegirma qanchaga tushyapti, kimga, qaysi
/// o'qituvchi/guruhda va NEGA berilgan.
///
/// <para>⚠️ <b>DAVR SUMMALARI <see cref="MonthlyCharge"/> DAN</b>, registrdan EMAS. Sabab:
/// chegirmalarning HAQIQATAN qo'llangan tarixi oylik hisoblarda turibdi (registr esa faqat
/// "hozir nima amalda" ni biladi — o'tgan oyda boshqacha bo'lgan bo'lishi mumkin). Registr
/// "hozir kimda qanday chegirma bor" qismini beradi: <c>byStudent.activeCount/activeLabel</c>,
/// <c>byReason</c>, <c>active</c>.</para>
///
/// <para>⚠️ Chegirma endi HAR FAN uchun alohida bo'lishi mumkin, shuning uchun o'quvchi
/// kesimida "foiz/summa" o'rniga <b>SONI</b> va qisqa <b>YORLIG'I</b> qaytadi
/// (<c>DiscountRules.ActiveLabel</c> — «Matematika 20%, Ingliz tili 50 000 so'm»).</para>
///
/// <para>RUXSAT — <c>finance.main</c> va <c>ReadRequiresPerm = true</c>: javobda pul summalari
/// bor, GET'ni odatdagidek har qanday xodimga ochib bo'lmaydi.</para>
///
/// <para>Batafsil: <c>.claude/rules/discounts.md</c>.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.discounts", ReadRequiresPerm = true)]
[Route("api/admin/reports/discounts")]
public class DiscountReportController(AppDbContext db) : ControllerBase
{
    /// <summary>Guruhsiz (<c>MonthlyCharge.GroupId == null</c>) hisoblar — ALOHIDA qatorda,
    /// jimgina yo'qolmasin.</summary>
    private const string NoGroupLabel = "Guruhsiz";

    /// <summary>O'qituvchisi biriktirilmagan guruhlar — ALOHIDA qatorda.</summary>
    private const string NoTeacherLabel = "Biriktirilmagan";

    /// <summary>
    /// Hisobot. <paramref name="from"/>/<paramref name="to"/> — "yyyy-MM" (inklyuziv). Bo'sh
    /// bo'lsa: <c>to</c> = joriy oy, <c>from</c> = undan 11 oy oldin (oxirgi 12 oy).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DiscountReportDto>> Get(
        [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        var current = TuitionService.CurrentMonth();
        var toMonth = IsMonth(to) ? to! : current;
        var fromMonth = IsMonth(from) ? from! : AppClock.Today.AddMonths(-11).ToString("yyyy-MM");
        if (string.CompareOrdinal(fromMonth, toMonth) > 0) fromMonth = toMonth;

        // ---------- (1) PUL: davrdagi barcha oylik hisoblar ----------
        // Chegirmalilar EMAS, HAMMASI: "chegirma ulushi" ko'rsatkichi maxrajsiz ma'nosiz bo'lardi.
        var charges = await db.MonthlyCharges.AsNoTracking()
            .Where(c => string.Compare(c.Month, fromMonth) >= 0 && string.Compare(c.Month, toMonth) <= 0)
            .Select(c => new { c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount })
            .ToListAsync();

        // ---------- (2) Nomlar: guruh → o'qituvchi/kurs (N+1 emas — ommaviy) ----------
        var groups = await db.Classes.AsNoTracking()
            .Select(g => new { g.Id, g.Name, g.TeacherId, g.CourseId })
            .ToListAsync();
        var teacherNames = await db.Teachers.AsNoTracking()
            .Select(t => new { t.Id, t.FullName }).ToListAsync();
        var courseNames = await db.Subjects.AsNoTracking()
            .Select(s => new { s.Id, s.Name }).ToListAsync();
        var teacherById = teacherNames.ToDictionary(t => t.Id, t => t.FullName);
        var courseById = courseNames.ToDictionary(s => s.Id, s => s.Name);
        var groupById = groups.ToDictionary(g => g.Id, g => new GroupInfo(
            g.Name,
            string.IsNullOrEmpty(g.TeacherId) ? "" : g.TeacherId,
            string.IsNullOrEmpty(g.TeacherId) || !teacherById.TryGetValue(g.TeacherId, out var tn) ? NoTeacherLabel : tn,
            string.IsNullOrEmpty(g.CourseId) || !courseById.TryGetValue(g.CourseId, out var cn) ? "" : cn));

        var studentNames = await db.Students.AsNoTracking()
            .Select(s => new { s.Id, s.FullName, s.IsArchived }).ToListAsync();
        var studentById = studentNames.ToDictionary(s => s.Id, s => s.FullName);
        var totalStudents = studentNames.Count(s => !s.IsArchived);

        // ---------- (3) REGISTR: hozir amaldagi chegirmalar ----------
        var activeRows = await db.StudentDiscounts.AsNoTracking()
            .Where(d => d.Status == StudentDiscount.StatusActive)
            .ToListAsync();
        // Arxivlangan o'quvchining chegirmasi "hozir amalda" deb sanalmaydi — u o'qimayapti.
        var archivedIds = studentNames.Where(s => s.IsArchived).Select(s => s.Id).ToHashSet();
        activeRows = activeRows.Where(d => !archivedIds.Contains(d.StudentId)).ToList();
        // ⚠️ O'quvchida bir NECHTA amaldagi chegirma bo'lishi mumkin (har fanga alohida) —
        // shuning uchun bu yerda "eng yangisi" EMAS, HAMMASI yig'iladi.
        var activeByStudent = activeRows
            .GroupBy(d => d.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal).ToList());

        // ---------- (4) Jamlanma ----------
        var periodCharged = charges.Sum(c => c.Amount);
        var periodDiscount = charges.Sum(c => c.Discount);
        var studentCount = activeByStudent.Count;   // ODAMLAR soni (qatorlar soni — ActiveCount)
        var summary = new DiscountReportSummaryDto(
            ActiveCount: activeRows.Count,
            StudentCount: studentCount,
            TotalStudents: totalStudents,
            StudentSharePct: Share(studentCount, totalStudents),
            PeriodCharged: periodCharged,
            PeriodDiscount: periodDiscount,
            SharePct: Share(periodDiscount, periodCharged),
            CurrentMonthDiscount: charges.Where(c => c.Month == current).Sum(c => c.Discount));

        // ---------- (5) Oy kesimi — davrdagi HAR oy (chegirmasizi ham 0 bilan) ----------
        var byMonthRaw = charges.GroupBy(c => c.Month).ToDictionary(g => g.Key, g => g.ToList());
        var months = TuitionService.MonthRange(fromMonth, toMonth)
            .Select(m => byMonthRaw.TryGetValue(m, out var rows)
                ? new DiscountReportMonthDto(m, rows.Sum(r => r.Amount), rows.Sum(r => r.Discount),
                    rows.Where(r => r.Discount != 0m).Select(r => r.StudentId).Distinct().Count())
                : new DiscountReportMonthDto(m, 0m, 0m, 0))
            .ToList();

        // ---------- (6) O'qituvchi kesimi ----------
        var byTeacher = charges
            .GroupBy(c => c.GroupId is not null && groupById.TryGetValue(c.GroupId, out var g)
                ? (g.TeacherId, g.TeacherName)
                : ("", NoGroupLabel))
            .Select(g => new DiscountReportTeacherDto(
                g.Key.Item1, g.Key.Item2,
                g.Sum(c => c.Amount), g.Sum(c => c.Discount),
                g.Where(c => c.Discount != 0m).Select(c => c.StudentId).Distinct().Count(),
                g.Where(c => c.GroupId is not null).Select(c => c.GroupId!).Distinct().Count()))
            .OrderByDescending(t => t.Discount)
            .ToList();

        // ---------- (7) Guruh kesimi ----------
        var byGroup = charges
            .GroupBy(c => c.GroupId ?? "")
            .Select(g =>
            {
                var info = g.Key.Length > 0 && groupById.TryGetValue(g.Key, out var gi)
                    ? gi
                    : new GroupInfo(g.Key.Length == 0 ? NoGroupLabel : "(o'chirilgan guruh)", "", NoTeacherLabel, "");
                return new DiscountReportGroupDto(
                    g.Key, info.Name, info.TeacherName, info.CourseName,
                    g.Sum(c => c.Amount), g.Sum(c => c.Discount),
                    g.Where(c => c.Discount != 0m).Select(c => c.StudentId).Distinct().Count());
            })
            .OrderByDescending(g => g.Discount)
            .ToList();

        // ---------- (8) O'quvchi kesimi — chegirma bo'yicha KAMAYISH tartibida ----------
        var byStudent = charges
            .Where(c => c.Discount != 0m)
            .GroupBy(c => c.StudentId)
            .Select(g =>
            {
                var infos = g.Where(c => c.GroupId is not null)
                    .Select(c => groupById.TryGetValue(c.GroupId!, out var gi) ? gi : null)
                    .Where(gi => gi is not null).Select(gi => gi!).ToList();
                var reg = activeByStudent.GetValueOrDefault(g.Key) ?? new List<StudentDiscount>();
                return new DiscountReportStudentDto(
                    g.Key, studentById.TryGetValue(g.Key, out var name) ? name : "",
                    g.Sum(c => c.Amount), g.Sum(c => c.Discount),
                    g.Select(c => c.Month).Distinct().Count(),
                    infos.Select(i => i.Name).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    infos.Select(i => i.TeacherName).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    reg.Count, DiscountRules.ActiveLabel(reg), reg.Count > 0);
            })
            .OrderByDescending(s => s.Discount)
            .ToList();

        // ---------- (9) Sabab kesimi — REGISTRdagi amaldagi qatorlar bo'yicha ----------
        // Summa esa baribir PULdan: o'sha o'quvchilarning davrdagi chegirmasi.
        // ⚠️ Summa o'quvchi bo'yicha olinadi, sabablar esa endi bir o'quvchida BIR NECHTA bo'lishi
        // mumkin — shuning uchun bitta sabab ichida o'quvchi IKKI marta sanalmasin (`Distinct`),
        // aks holda ikki fanga bir xil sabab bilan chegirma berilgan o'quvchining summasi
        // ikkilanardi. `Count` esa QATORLAR soni (nechta chegirma shu sabab bilan berilgan).
        var discountByStudent = charges.GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Discount));
        var byReason = activeRows
            .GroupBy(d => (d.Reason ?? "").Trim())
            .Select(g => new DiscountReportReasonDto(
                g.Key, g.Count(),
                g.Select(d => d.StudentId).Distinct()
                    .Sum(sid => discountByStudent.TryGetValue(sid, out var v) ? v : 0m)))
            .OrderByDescending(r => r.Discount).ThenByDescending(r => r.Count)
            .ToList();

        var active = activeRows
            .OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal)
            .Select(d => StudentDiscountService.ToDto(d, current))
            .ToList();

        return new DiscountReportDto(fromMonth, toMonth, summary, months, byTeacher, byGroup, byStudent, byReason, active);
    }

    /// <summary>Guruh haqidagi hisobotga kerak bo'ladigan nomlar (bir marta hisoblanadi).</summary>
    private record GroupInfo(string Name, string TeacherId, string TeacherName, string CourseName);

    /// <summary>Ulush (%), maxraj 0 bo'lsa 0 — bo'linish xatosi hisobotni yiqitmasin.</summary>
    private static decimal Share(decimal part, decimal total) =>
        total <= 0m ? 0m : decimal.Round(part * 100m / total, 1);

    private static bool IsMonth(string? v) =>
        !string.IsNullOrWhiteSpace(v) && v.Length == 7 && v[4] == '-' && DateOnly.TryParse($"{v}-01", out _);
}
