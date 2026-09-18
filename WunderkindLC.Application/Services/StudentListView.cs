using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHILAR RO'YXATLARI (edutizim ko'rinishi) — holat filtri va qo'shimcha ustunlar.
///
/// <para><b>Holat filtri</b> (<see cref="MatchesState"/>) — "Yangi o'quvchilar" va "Aktiv
/// o'quvchilar" sahifalari AYNAN shu endpointdan (<c>GET api/admin/students?state=</c>) oladi.
/// Ta'rif bosh sahifa kartochkalari bilan BIR XIL (<see cref="DashboardSummary.Compute"/>):
/// kartochkadagi son bosilganda ochiladigan ro'yxatdagi qatorlar soni bilan teng chiqsin.</para>
/// <list type="bullet">
/// <item><c>trial</c> — kamida bitta TIRIK sinov a'zoligi bor. ⚠️ <c>MemberState == "trial"</c>
/// EMAS: u ustunlik bo'yicha "aktiv" ni oldinga qo'yadi, ya'ni bir kursda o'qib, ikkinchisida
/// sinovda turgan o'quvchi "yangi" ro'yxatdan (va kartochka sonidan farqli) tushib qolardi.</item>
/// <item><c>active</c> — <c>MemberState == "active"</c> (ya'ni kamida bitta tirik AKTIV a'zolik).</item>
/// </list>
///
/// <para><b>Qo'shimcha ustunlar</b> (<see cref="EnrichExtrasAsync"/>) — MANBA, MODERATOR, TO'LOV
/// SANASI, ILOVA, SHARTNOMA. Hammasi mavjud yozuvlardan O'QILADI, yangi ma'lumot saqlanmaydi.</para>
/// </summary>
public static class StudentListView
{
    /// <summary>Qo'llab-quvvatlanadigan <c>state</c> qiymatlari.</summary>
    public static readonly IReadOnlyList<string> States = ["trial", "active"];

    /// <summary>Bo'sh qiymat (filtr yo'q) yoki <see cref="States"/> dan biri.</summary>
    public static bool IsKnownState(string? state) =>
        string.IsNullOrEmpty(state) || States.Contains(state);

    /// <summary>
    /// O'quvchi berilgan holat filtriga mos keladimi. <see cref="StudentMembershipView.EnrichAsync"/>
    /// dan KEYIN chaqiriladi (<see cref="Student.GroupStates"/> / <see cref="Student.MemberState"/>
    /// to'ldirilgan bo'lishi shart). Bo'sh holat — hamma mos.
    /// </summary>
    public static bool MatchesState(Student s, string? state) => state switch
    {
        null or "" => true,
        "trial" => s.GroupStates.Any(g => g.Status == "trial"),
        "active" => s.MemberState == "active",
        _ => false,
    };

    /// <summary>
    /// MODERATOR (mas'ul xodim) — shartnomani YOPGAN xodim (<see cref="Lead.ClosedByUserId"/>),
    /// u bo'lmasa lidni ISHLAGAN operator (<see cref="Lead.AssigneeUserId"/>). Ikkalasi ham
    /// bo'lmasa — bo'sh. Ism topilmasa (akkaunt o'chirilgan) ham bo'sh.
    /// </summary>
    public static string PickModerator(string? closedById, string? assigneeId, IReadOnlyDictionary<string, string> userNames)
    {
        foreach (var id in new[] { closedById, assigneeId })
            if (!string.IsNullOrEmpty(id) && userNames.TryGetValue(id, out var name) && name.Length > 0)
                return name;
        return string.Empty;
    }

    /// <summary>
    /// Ro'yxat ustunlarini to'ldiradi (bazaga yozilmaydi — maydonlar <c>[NotMapped]</c>):
    /// <see cref="Student.LeadSource"/>, <see cref="Student.Moderator"/>,
    /// <see cref="Student.LastPaymentDate"/>, <see cref="Student.AppFirstLoginAt"/>,
    /// <see cref="Student.ContractNumber"/>. Hammasi ommaviy so'rovlar (N+1 yo'q).
    /// </summary>
    public static async Task EnrichExtrasAsync(
        IAppDbContext db, IReadOnlyCollection<Student> students, CancellationToken ct = default)
    {
        if (students.Count == 0) return;
        var ids = students.Select(s => s.Id).ToList();

        // Lid → o'quvchi. Bitta o'quvchiga ikki lid aylantirilgan bo'lsa — ENG YANGISI.
        var leads = (await db.Leads.AsNoTracking()
                .Where(l => l.ConvertedStudentId != null && ids.Contains(l.ConvertedStudentId))
                .Select(l => new { StudentId = l.ConvertedStudentId!, l.Source, l.AssigneeUserId, l.ClosedByUserId, l.CreatedAt })
                .ToListAsync(ct))
            .GroupBy(l => l.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt, StringComparer.Ordinal).First());

        var userIds = leads.Values.SelectMany(l => new[] { l.AssigneeUserId, l.ClosedByUserId })
            .Concat(students.Select(s => s.UserId))
            .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        var users = userIds.Count == 0
            ? []
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.FirstLoginAt }).ToListAsync(ct);
        var userNames = users.ToDictionary(u => u.Id, u => u.FullName ?? "");
        var firstLogins = users.ToDictionary(u => u.Id, u => u.FirstLoginAt ?? "");

        // Oxirgi O'QUV to'lovi — `DashboardSummary` / `LeadOutcome` konvensiyasi: kirim/tuition.
        var lastPaid = (await db.FinanceTransactions.AsNoTracking()
                .Where(t => t.StudentId != null && ids.Contains(t.StudentId)
                            && t.Direction == "income" && t.Category == "tuition")
                .Select(t => new { StudentId = t.StudentId!, t.Date })
                .ToListAsync(ct))
            .GroupBy(t => t.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Date ?? "").Max(StringComparer.Ordinal) ?? "");

        // Shartnoma — ota-onaga yuborilgan (Target="parent", RecipientKey = o'quvchi id'si).
        // FAQAT RAQAM — fayl manzillari (PdfUrl/DocxUrl) bu yerga TUSHMAYDI (uploads-security.md).
        var contracts = (await db.Contracts.AsNoTracking()
                .Where(c => c.Target == "parent" && ids.Contains(c.RecipientKey))
                .Select(c => new { c.RecipientKey, c.Number })
                .ToListAsync(ct))
            .GroupBy(c => c.RecipientKey)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Number));

        foreach (var s in students)
        {
            if (leads.TryGetValue(s.Id, out var lead))
            {
                s.LeadSource = lead.Source ?? "";
                s.Moderator = PickModerator(lead.ClosedByUserId, lead.AssigneeUserId, userNames);
            }
            s.LastPaymentDate = lastPaid.GetValueOrDefault(s.Id, "");
            s.AppFirstLoginAt = s.UserId is not null ? firstLogins.GetValueOrDefault(s.UserId, "") : "";
            s.ContractNumber = contracts.TryGetValue(s.Id, out var n) ? n : null;
        }
    }
}
