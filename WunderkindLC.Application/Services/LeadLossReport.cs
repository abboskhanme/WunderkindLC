using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «YO'QOTILGAN LIDLAR» hisoboti — sof funksiya (<see cref="Build"/>, <c>LeadLossTests</c>) +
/// yuklovchi (<see cref="BuildAsync"/>). Qoidalar: `.claude/rules/crm-leads.md` «YO'QOTILGAN LIDLAR».
///
/// <para><b>DAVR — lid KELGAN sana bo'yicha</b> (<c>Lead.CreatedAt</c>), CRM analitikasidagi
/// (<see cref="LeadAnalytics.InRange"/>) bilan AYNAN bir xil. Savol: «shu davrda kelgan lidlarning
/// qanchasini yo'qotdik va nega». Shu sababli har bir foiz BITTA to'plamdan olinadi va 100% dan
/// oshmaydi. "Yo'qotilgan sana bo'yicha" hisoblansa sanoq bir to'plamdan, maxraj boshqasidan bo'lib,
/// "120% yo'qotish" kabi ma'nosiz raqamlar chiqardi.</para>
///
/// <para><b>Holat — JORIY:</b> tiklangan lid yo'qotilgan sanalmaydi.</para>
/// </summary>
public static class LeadLossReport
{
    /// <summary>Hisob uchun kerakli lid maydonlari.</summary>
    /// <param name="Source">Manba (NOM yoki eski yozuvlarda id — <c>sourceNames</c> bilan nomga aylanadi).</param>
    public readonly record struct Row(
        string Id, string FullName, string CreatedAt, string Source, string? AssigneeUserId, bool Converted,
        string? LostAt, string? LostReason, string? LostComment, string? LostByName);

    /// <summary>Sababi yozilmagan yo'qotilgan lid (eski/qo'lda tuzatilgan yozuv) — jimgina yo'qolmasin.</summary>
    public const string NoReasonLabel = "— sababsiz —";

    /// <summary>Mas'ul xodimi yo'q lid.</summary>
    public const string UnassignedLabel = "Biriktirilmagan";

    /// <summary>Mas'ul id'si bor, lekin xodim topilmadi (o'chirilgan akkaunt).</summary>
    public const string UnknownUserLabel = "Noma'lum xodim";

    /// <summary>Oylik oqimda ko'pi bilan nechta oy (eng so'nggilari) ko'rsatiladi.</summary>
    public const int MaxMonths = 36;

    /// <summary>Ro'yxatdagi eng ko'p qator (eng so'nggi yo'qotilganlar).</summary>
    public const int RecentLimit = 100;

    public static bool IsLost(Row r) => !string.IsNullOrWhiteSpace(r.LostAt);

    /// <summary>Butun hisobot. <paramref name="userNames"/> — AppUser.Id → ism, <paramref name="sourceNames"/> — LeadSource.Id → nom.</summary>
    public static LeadLossReportDto Build(
        IEnumerable<Row> all, string? from, string? to,
        IReadOnlyDictionary<string, string>? userNames = null,
        IReadOnlyDictionary<string, string>? sourceNames = null)
    {
        var leads = (all ?? []).Where(l => LeadAnalytics.InRange(l.CreatedAt, from, to)).ToList();
        var lost = leads.Where(IsLost).ToList();
        var total = leads.Count;

        string SourceLabel(string? raw)
        {
            var v = (raw ?? "").Trim();
            if (v.Length == 0) return LeadAnalytics.UnknownSourceLabel;
            return sourceNames is not null && sourceNames.TryGetValue(v, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n.Trim() : v;
        }

        string ManagerLabel(string key) =>
            key.Length == 0 ? UnassignedLabel
            : userNames is not null && userNames.TryGetValue(key, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n : UnknownUserLabel;

        // SABABLAR — snapshot MATNI bo'yicha (katalogdagi sabab o'chirilgan/qayta nomlangan bo'lsa
        // ham eski lidlar o'z sababi bilan qoladi). Registr/bo'shliq farqi bitta sabab sanaladi.
        var byReason = lost
            .GroupBy(l => ReasonKey(l.LostReason), StringComparer.Ordinal)
            .Select(g =>
            {
                var label = g.Select(l => (l.LostReason ?? "").Trim()).Where(s => s.Length > 0)
                    .GroupBy(s => s).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => x.Key).FirstOrDefault() ?? NoReasonLabel;
                return new LeadLossReasonDto(label, g.Count(), Percent(g.Count(), lost.Count));
            })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label, StringComparer.Ordinal)
            .ToList();

        var bySource = Groups(leads, l => SourceLabel(l.Source), k => k);
        var byManager = Groups(leads, l => (l.AssigneeUserId ?? "").Trim(), ManagerLabel);

        var recent = lost
            .OrderByDescending(l => l.LostAt, StringComparer.Ordinal).ThenBy(l => l.Id, StringComparer.Ordinal)
            .Take(RecentLimit)
            .Select(l => new LeadLossItemDto(
                l.Id, l.FullName ?? "", l.CreatedAt ?? "", l.LostAt ?? "",
                string.IsNullOrWhiteSpace(l.LostReason) ? NoReasonLabel : l.LostReason!.Trim(),
                l.LostComment ?? "", SourceLabel(l.Source),
                ManagerLabel((l.AssigneeUserId ?? "").Trim()), l.LostByName ?? ""))
            .ToList();

        return new LeadLossReportDto(
            From: from ?? "", To: to ?? "",
            Total: total, Lost: lost.Count, LostRate: Percent(lost.Count, total),
            Converted: leads.Count(l => l.Converted),
            ByReason: byReason,
            BySource: bySource,
            ByManager: byManager,
            Monthly: Monthly(leads, from, to),
            Recent: recent,
            RecentTruncated: Math.Max(0, lost.Count - recent.Count));
    }

    /// <summary>Bazadan yuklab <see cref="Build"/> ni chaqiradi (faqat o'qish).</summary>
    public static async Task<LeadLossReportDto> BuildAsync(
        IAppDbContext db, string? from, string? to, CancellationToken ct = default)
    {
        var rows = (await db.Leads.AsNoTracking()
                .Select(l => new
                {
                    l.Id, l.FullName, l.CreatedAt, l.Source, l.AssigneeUserId, l.ConvertedStudentId,
                    l.LostAt, l.LostReason, l.LostComment, l.LostByName,
                })
                .ToListAsync(ct))
            .Select(l => new Row(
                l.Id, l.FullName ?? "", l.CreatedAt ?? "", l.Source ?? "", l.AssigneeUserId,
                l.ConvertedStudentId != null, l.LostAt, l.LostReason, l.LostComment, l.LostByName))
            .ToList();

        // Xodim ismi JORIY ro'yxatdan (voronka analitikasidagi naqsh — eskirgan snapshot emas).
        var userNames = (await db.Users.AsNoTracking()
                .Select(u => new { u.Id, u.FullName }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => u.FullName ?? "", StringComparer.Ordinal);
        // Eski/import yozuvlarda `Lead.Source` da manba ID'si turishi mumkin (`LeadsController.GetAll` naqshi).
        var sourceNames = (await db.LeadSources.AsNoTracking()
                .Select(s => new { s.Id, s.Name }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => s.Name ?? "", StringComparer.Ordinal);

        return Build(rows, from, to, userNames, sourceNames);
    }

    // ------------------------------------------------------------------ yordamchilar

    private static string ReasonKey(string? reason)
    {
        var v = (reason ?? "").Trim();
        return v.Length == 0 ? "" : string.Join(' ', v.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    /// <summary>Kesim: guruh bo'yicha jami lid, yo'qotilgan va ulush. Yo'qotilgani ko'pi tepada.</summary>
    private static List<LeadLossGroupDto> Groups(
        IEnumerable<Row> leads, Func<Row, string> keyOf, Func<string, string> labelOf) =>
        leads.GroupBy(keyOf, StringComparer.Ordinal)
            .Select(g =>
            {
                var t = g.Count();
                var l = g.Count(IsLost);
                return new LeadLossGroupDto(g.Key, labelOf(g.Key), t, l, Percent(l, t));
            })
            .OrderByDescending(x => x.Lost).ThenByDescending(x => x.Total)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Oylik oqim (lid KELGAN oy bo'yicha). Bo'sh oylar ham NOL bilan chiziladi — aks holda
    /// grafikda oylar "sakrab" o'tib, tushish ko'rinmay qolardi. Davr berilgan bo'lsa uning
    /// chegaralari ham qamraladi; ko'pi bilan <see cref="MaxMonths"/> ta eng so'nggi oy.
    /// </summary>
    public static List<LeadLossMonthDto> Monthly(IReadOnlyCollection<Row> leads, string? from, string? to)
    {
        var byMonth = leads.Where(l => (l.CreatedAt ?? "").Length >= 7 && IsMonth(l.CreatedAt![..7]))
            .GroupBy(l => l.CreatedAt[..7], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Lost: g.Count(IsLost)), StringComparer.Ordinal);

        var bounds = byMonth.Keys.ToList();
        if ((from ?? "").Trim().Length >= 7 && IsMonth(from!.Trim()[..7])) bounds.Add(from.Trim()[..7]);
        if ((to ?? "").Trim().Length >= 7 && IsMonth(to!.Trim()[..7])) bounds.Add(to.Trim()[..7]);
        if (bounds.Count == 0) return [];

        var start = Parse(bounds.Min(StringComparer.Ordinal)!);
        var end = Parse(bounds.Max(StringComparer.Ordinal)!);
        var months = new List<string>();
        for (var m = start; m <= end; m = m.AddMonths(1))
            months.Add(m.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        if (months.Count > MaxMonths) months = months.Skip(months.Count - MaxMonths).ToList();

        return months.Select(m =>
        {
            var (t, l) = byMonth.GetValueOrDefault(m);
            return new LeadLossMonthDto(m, t, l, Percent(l, t));
        }).ToList();
    }

    private static bool IsMonth(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static DateTime Parse(string month) =>
        DateTime.ParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture);

    private static double Percent(int part, int total) =>
        total <= 0 ? 0 : Math.Round(part * 100.0 / total, 1);
}
