using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «GURUHLAR VA TARIFLAR» (TZ 1.2) — bitta OY uchun har guruhda: qaysi narxda nechta o'quvchi o'qiydi
/// («13 ta × 1 590 000, 3 ta × 1 490 000»), rejadagi (hisoblangan) tushum, chegirma, yig'ilgan pul, qolgan
/// qarz va yig'ilish foizi; o'qituvchilar kesimida jamlanma va to'lov intizomi sust guruhlar.
///
/// <para><b>Tarif</b> — o'quvchining shu oydagi TO'LIQ narxi (<see cref="PricingBook"/>: sinf oralig'i yoki guruh
/// narxi), chegirma alohida ustunda. Qisman oy (aktivlashtirish / muzlatish / chiqish shu oyda) o'z tarifi
/// ostida «qisman» bo'lib sanaladi. Hisob qo'lda boshqa summaga tuzatilgan bo'lsa (shaxsiy narx) — o'sha
/// summa alohida tarif bo'lib chiqadi.</para>
///
/// <para><b>O'qituvchi</b> — shu OYNING egasi, o'qituvchi tarixi bo'yicha (<see cref="GroupOwnership.OwnerAtMonth"/>,
/// maosh bilan bir xil qoida — <c>billing.md</c> «O'TGAN OY KIMNIKI»).</para>
/// </summary>
public static class TariffReport
{
    /// <summary>Shu foizdan past yig'ilish — «to'lov intizomi sust» (Moliya → guruhlar ko'rinishidagi qizil bilan bir xil).</summary>
    public const decimal WeakPct = 60m;

    private const string NoTeacher = "Biriktirilmagan";

    public static async Task<TariffReportDto> BuildAsync(
        IAppDbContext db, string? month, string? teacherId, DateOnly today, CancellationToken ct = default)
    {
        var m = PaymentFields.IsMonth(month) ? month! : today.ToString("yyyy-MM");
        var cells = await DebtLedger.LoadAsync(db, m, m, ct);

        var studentIds = cells.Select(c => c.StudentId).Distinct().ToList();
        // Narx uchun faqat sinf maydonlari kerak (PricingBook.Resolve) — butun jadval yuklanmaydi.
        var students = (await db.Students.AsNoTracking()
                .Where(s => studentIds.Contains(s.Id))
                .Select(s => new Student { Id = s.Id, SchoolGrade = s.SchoolGrade, SchoolGradeYear = s.SchoolGradeYear })
                .ToListAsync(ct))
            .ToDictionary(s => s.Id, StringComparer.Ordinal);
        var groupIds = cells.Select(c => c.GroupId).Where(g => g != DebtLedger.NoGroup).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync(ct);
        var groupById = groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var memberships = (await db.StudentGroups.AsNoTracking()
                .Where(sg => groupIds.Contains(sg.GroupId)).ToListAsync(ct))
            .GroupBy(sg => (sg.StudentId, sg.GroupId))
            .ToDictionary(g => g.Key, g => g.ToList());
        var courseNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var pricing = await PricingBook.LoadAsync(db, ct);
        var owners = await GroupOwnership.LoadAsync(db, groups, ct);

        var rows = new List<TariffGroupDto>();
        foreach (var gcells in cells.GroupBy(c => c.GroupId))
        {
            var gid = gcells.Key;
            groupById.TryGetValue(gid, out var group);
            var owner = group is null ? "" : owners.OwnerAtMonth(gid, m) ?? group.TeacherId;
            if (!string.IsNullOrEmpty(teacherId) && owner != teacherId) continue;

            var charged = gcells.Where(c => c.HasCharge).ToList();
            // Tariflar taqsimoti: (narx) → (to'liq, qisman).
            var prices = new Dictionary<decimal, (int Full, int Partial)>();
            foreach (var c in charged)
            {
                var price = c.Gross;
                var partial = false;
                if (group is not null && students.TryGetValue(c.StudentId, out var st))
                {
                    var tariff = pricing.Resolve(group, st, m).Monthly;
                    if (c.Gross == tariff) price = tariff;
                    else if (c.Gross < tariff && IsPartialMonth(memberships.GetValueOrDefault((c.StudentId, gid)), m))
                    {
                        price = tariff;
                        partial = true;
                    }
                }
                var cur = prices.GetValueOrDefault(price);
                prices[price] = partial ? (cur.Full, cur.Partial + 1) : (cur.Full + 1, cur.Partial);
            }

            var totals = DebtLedger.Totals(gcells);
            rows.Add(new TariffGroupDto(
                gid,
                group?.Name ?? "Guruhsiz",
                group is null ? "" : courseNames.GetValueOrDefault(group.CourseId, ""),
                owner,
                string.IsNullOrEmpty(owner) ? NoTeacher : teacherNames.GetValueOrDefault(owner, NoTeacher),
                group?.IsArchived ?? false,
                charged.Select(c => c.StudentId).Distinct().Count(),
                prices.OrderByDescending(p => p.Value.Full + p.Value.Partial).ThenByDescending(p => p.Key)
                    .Select(p => new TariffPriceDto(p.Key, p.Value.Full + p.Value.Partial, p.Value.Partial)).ToList(),
                // «Yig'ilgan» — shu oy hisobini yopgan pul (sofdan oshmaydi): Reja − Chegirma − Yig'ilgan = Qarz.
                totals.Charged, totals.Discount, totals.Net, totals.Paid, totals.Debt,
                totals.CollectionPct, totals.Debtors,
                totals.Net > 0 && totals.CollectionPct < WeakPct));
        }

        rows = rows.OrderBy(r => r.TeacherName, StringComparer.CurrentCulture)
            .ThenBy(r => r.GroupName, StringComparer.CurrentCulture).ToList();

        var teachers = rows.GroupBy(r => r.TeacherId)
            .Select(g =>
            {
                var net = g.Sum(r => r.Net);
                var debt = g.Sum(r => r.Debt);
                return new TariffTeacherDto(
                    g.Key, g.First().TeacherName, g.Count(), g.Sum(r => r.Students),
                    g.Sum(r => r.Planned), g.Sum(r => r.Discount), net, g.Sum(r => r.Collected), debt,
                    DebtLedger.Pct(net - debt, net), g.Count(r => r.Weak));
            })
            .OrderBy(t => t.CollectionPct).ThenByDescending(t => t.Debt)
            .ToList();

        var inScope = cells.Where(c => rows.Any(r => r.GroupId == c.GroupId)).ToList();
        return new TariffReportDto(m, WeakPct, DebtLedger.Totals(inScope), rows, teachers);
    }

    /// <summary>Shu oy (o'quvchi, guruh) uchun QISMAN oymi — a'zolik shu oyda aktivlashgan, muzlagan yoki tugagan.</summary>
    public static bool IsPartialMonth(IReadOnlyList<StudentGroup>? memberships, string month)
    {
        if (memberships is null) return false;
        static bool In(string? date, string m) => date is { Length: >= 7 } && date[..7] == m;
        return memberships.Any(sg => In(sg.ActivatedAt, month) || In(sg.FrozenAt, month)
                                     || (!sg.IsActive && In(sg.LeftAt, month)));
    }
}
