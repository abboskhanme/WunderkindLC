using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>Reestr qatori. <paramref name="Kind"/>: <c>teacher</c> | <c>staff</c>.
/// <paramref name="Source"/>: teacher — <c>ledger</c>; staff — <c>kpi</c> (tasdiqlangan KPI oyi), <c>kpi-pending</c>
/// (KPI profili bor, oy hali tasdiqlanmagan — oklad ko'rsatiladi), <c>fixed</c> (qat'iy oylik), <c>none</c> (oylik kiritilmagan).</summary>
public record PayrollRowDto(
    string Kind, string Id, string Name, string Role, string Source,
    decimal Accrued, decimal Advance, decimal Final, decimal Other, decimal Paid, decimal Remaining, string Status,
    decimal? FixedAmount, string? FixedFrom, decimal Pending = 0);

public record PayrollDto(
    string Month, decimal Accrued, decimal Paid, decimal Remaining, decimal Advance, decimal Final,
    List<PayrollRowDto> Teachers, List<PayrollRowDto> Staff);

/// <summary>
/// XODIMLAR MAOSHI REESTRI (SPEC-backlog-2026-10-17 F3 K3; egasining qarori: fiks + KPI, avans/yakuniy). Bir OY uchun
/// har o'qituvchi va har xodim: hisoblangan maosh, berilgan avans va yakuniy to'lovlar, qoldiq.
///
/// <para><b>O'qituvchi</b> — <see cref="SalaryLedger"/> (foiz/qat'iy, ushlanmalar, jarimalar — o'zgarishsiz); to'lovlar
/// avvalgidek <c>TeacherId</c> bilan.</para>
/// <para><b>Xodim</b> (rol <c>staff</c> yoki maoshi yozilgan har panel akkaunti): KPI profili bo'lsa — tasdiqlangan
/// <see cref="KpiMonthResult"/> (oklad + bonus − jarima), tasdiqlanmagan oyda — oklad (<c>kpi-pending</c>); aks holda
/// <see cref="StaffSalary"/> (versiyalangan). To'lovlar — <c>FinanceTransaction.StaffUserId</c> + <c>Month</c>.</para>
/// <para>⚠️ Hisob FAQAT o'qiladi — KPI'ning "summani aytadi, pulni to'lamaydi" qoidasi (kpi.md §1) saqlanadi: to'lov
/// odatdagi maosh chiqimi (chegara bo'lsa — tasdiq bilan).</para>
/// </summary>
public static class PayrollRegister
{
    public static string StatusOf(decimal accrued, decimal paid) =>
        accrued <= 0 && paid <= 0 ? "none" : paid >= accrued ? "paid" : paid > 0 ? "partial" : "unpaid";

    /// <summary>Shu oydagi qat'iy oylik — <c>EffectiveFrom &lt;= oy</c> bo'lgan eng so'nggisi.</summary>
    public static StaffSalary? SalaryFor(IEnumerable<StaffSalary> all, string userId, string month) =>
        all.Where(x => x.UserId == userId && string.CompareOrdinal(x.EffectiveFrom, month) <= 0)
            .OrderByDescending(x => x.EffectiveFrom, StringComparer.Ordinal)
            .ThenByDescending(x => x.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();

    public static async Task<PayrollDto> BuildAsync(IAppDbContext db, string month, CancellationToken ct = default)
    {
        // Shu oyning maosh to'lovlari — avans / yakuniy / belgilanmagan.
        var pays = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.Direction == "expense" && t.Category == "salary" && t.Month == month)
            .Select(t => new { t.TeacherId, t.StaffUserId, t.SalaryPayKind, t.Amount })
            .ToListAsync(ct);
        (decimal Adv, decimal Fin, decimal Oth) Split(IEnumerable<(string? Kind, decimal Amount)> xs)
        {
            decimal a = 0, f = 0, o = 0;
            foreach (var x in xs)
                switch (x.Kind) { case "advance": a += x.Amount; break; case "final": f += x.Amount; break; default: o += x.Amount; break; }
            return (a, f, o);
        }

        // TASDIQ KUTAYOTGAN maosh so'rovlari (chegaradan katta to'lov) — qatorda «tasdiq kutmoqda»: aks holda qator
        // «to'lanmagan» ko'rinib, ikkinchi so'rov yaratishga undardi.
        var pending = await db.ExpenseRequests.AsNoTracking()
            .Where(r => r.Status == ExpenseApproval.Pending && r.Category == "salary" && r.Month == month)
            .Select(r => new { r.TeacherId, r.StaffUserId, r.Amount })
            .ToListAsync(ct);

        // ---- o'qituvchilar ----
        var teachers = await db.Teachers.AsNoTracking().OrderBy(t => t.FullName).ToListAsync(ct);
        var teacherRows = new List<PayrollRowDto>();
        var monthStart = month + "-01";
        foreach (var te in teachers)
        {
            // Oy boshidan OLDIN arxivlangan va shu oyga to'lovi yo'q o'qituvchi — varaqasi qurilmaydi (har biri ~10 so'rov).
            if (te.IsArchived && !string.IsNullOrEmpty(te.ArchivedAt) && string.CompareOrdinal(te.ArchivedAt, monthStart) < 0
                && !pays.Any(p => p.TeacherId == te.Id) && !pending.Any(r => r.TeacherId == te.Id))
                continue;
            var ledger = await SalaryLedger.BuildAsync(db, te, monthStart, month + "-28");
            var m = ledger.Months.FirstOrDefault(x => x.Month == month);
            var accrued = m?.Expected ?? 0m;
            var (adv, fin, typeless) = Split(pays.Where(p => p.TeacherId == te.Id).Select(p => (p.SalaryPayKind, p.Amount)));
            // «Berildi» — VARAQADAN (SalaryLedger: oyi yo'q eski to'lov SANASI bo'yicha ham shu oyga tushadi), aks holda
            // reestr profil bilan farq qilib, qayta to'lashga undardi. Avans/yakuniy — turi belgilangan to'lovlar.
            var paid = m?.Paid ?? adv + fin + typeless;
            var oth = Math.Max(0m, paid - adv - fin);
            var pend = pending.Where(r => r.TeacherId == te.Id).Sum(r => r.Amount);
            if (te.IsArchived && accrued <= 0 && paid <= 0 && pend <= 0) continue;
            teacherRows.Add(new PayrollRowDto("teacher", te.Id, te.FullName, "O'qituvchi", "ledger",
                accrued, adv, fin, oth, paid, accrued - paid, StatusOf(accrued, paid), null, null, pend));
        }

        // ---- xodimlar ----
        var salaries = await db.StaffSalaries.AsNoTracking().ToListAsync(ct);
        var profiles = await db.KpiProfiles.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        var kpiSalaries = await db.KpiProfileSalaries.AsNoTracking().ToListAsync(ct);
        var results = await db.KpiMonthResults.AsNoTracking()
            .Where(r => r.Month == month && r.Status == "confirmed").ToListAsync(ct);
        var paidStaffIds = pays.Select(p => p.StaffUserId).OfType<string>().ToHashSet();
        var withSalary = salaries.Select(s => s.UserId).ToHashSet();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin)
            .Select(u => new { u.Id, u.FullName, u.Role, u.Position })
            .ToListAsync(ct);

        var staffRows = new List<PayrollRowDto>();
        foreach (var u in users.OrderBy(u => u.FullName, StringComparer.CurrentCulture))
        {
            var profile = profiles.FirstOrDefault(p => p.UserId == u.Id);
            // Admin/superadmin — faqat maoshi kiritilgan yoki to'lov olgan bo'lsa (markaz egasi ro'yxatni to'ldirmasin).
            if (u.Role != Roles.Staff && profile is null && !withSalary.Contains(u.Id) && !paidStaffIds.Contains(u.Id)
                && !pending.Any(r => r.StaffUserId == u.Id)) continue;

            var fixedRow = SalaryFor(salaries, u.Id, month);
            string source;
            decimal accrued;
            var result = results.FirstOrDefault(r => r.UserId == u.Id);
            if (result is not null) { source = "kpi"; accrued = result.Salary; }
            else if (profile is not null)
            {
                source = "kpi-pending";
                accrued = Kpi.KpiVersioning.SalaryFor(kpiSalaries, u.Id, month)?.BaseSalary ?? 0m;
            }
            else if (fixedRow is not null) { source = "fixed"; accrued = fixedRow.Amount; }
            else { source = "none"; accrued = 0m; }

            var (adv, fin, oth) = Split(pays.Where(p => p.StaffUserId == u.Id).Select(p => (p.SalaryPayKind, p.Amount)));
            var paid = adv + fin + oth;
            staffRows.Add(new PayrollRowDto("staff", u.Id, u.FullName,
                string.IsNullOrWhiteSpace(u.Position) ? RoleLabel(u.Role) : u.Position!, source,
                accrued, adv, fin, oth, paid, accrued - paid, StatusOf(accrued, paid),
                fixedRow?.Amount, fixedRow?.EffectiveFrom, pending.Where(r => r.StaffUserId == u.Id).Sum(r => r.Amount)));
        }

        var all = teacherRows.Concat(staffRows).ToList();
        return new PayrollDto(month,
            all.Sum(r => r.Accrued), all.Sum(r => r.Paid), all.Sum(r => Math.Max(0, r.Remaining)),
            all.Sum(r => r.Advance), all.Sum(r => r.Final), teacherRows, staffRows);
    }

    private static string RoleLabel(string role) => role switch
    {
        Roles.SuperAdmin => "Rahbar",
        Roles.Admin => "Administrator",
        _ => "Xodim",
    };
}
