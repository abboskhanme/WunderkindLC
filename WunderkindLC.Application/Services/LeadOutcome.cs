using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LID NATIJASI — "lid → BOSQICH → o'quvchi → TO'LOV → faol a'zolik" zanjiri, bir marta va
/// TO'PLAMLI o'qiladi.
///
/// <para>Bu zanjir ikkita bo'limda bir xil savolga javob beradi: <b>"kelgan odam haqiqatan
/// o'quvchi bo'ldimi va PUL to'ladimi?"</b> — daraja testi statistikasida
/// (<see cref="LevelTestService"/>) va lid formalari statistikasida (<see cref="LeadFormService"/>).
/// Shuning uchun mantiq bitta joyda: aks holda "aktiv" yoki "to'ladi" so'zi ikki bo'limda ikki xil
/// ma'no anglata boshlardi.</para>
///
/// <para>⚠️ "Aktiv" = <c>StudentGroup.IsActive &amp;&amp; Status == "active"</c> — ya'ni sinovdagi
/// va muzlatilgan a'zolik aktiv SANALMAYDI (a'zolik holatlari: <c>.claude/rules/billing.md</c>).</para>
/// </summary>
public sealed class LeadOutcome
{
    /// <summary>Lid id → u aylantirilgan o'quvchi id'si (faqat aylantirilganlar).</summary>
    public required IReadOnlyDictionary<string, string> LeadToStudent { get; init; }
    /// <summary>Hali MAVJUD (CRM'dan o'chirilmagan) lidlar.</summary>
    public required IReadOnlySet<string> ExistingLeadIds { get; init; }
    /// <summary>Hozir FAOL a'zoligi bor o'quvchilar.</summary>
    public required IReadOnlySet<string> ActiveStudentIds { get; init; }
    /// <summary>O'quvchi id → faol guruh(lar) nomi va o'qituvchi(lar) F.I.Sh (vergul bilan).</summary>
    public required IReadOnlyDictionary<string, (string Groups, string Teachers)> ByStudent { get; init; }
    /// <summary>
    /// Lid id → lidning HOZIRGI kanban bosqichi (<see cref="LeadStage"/> sarlavhasi + rangi).
    /// Bosqich tanlanmagan yoki lid o'chirilgan bo'lsa — ro'yxatda YO'Q.
    /// </summary>
    public required IReadOnlyDictionary<string, (string Title, string Color)> StageByLead { get; init; }
    /// <summary>
    /// O'quvchi id → to'lov: <c>Net</c> = tushgan o'quv to'lovlari MINUS vozvratlar,
    /// <c>FirstPaidAt</c> = BIRINCHI to'lov sanasi ("yyyy-MM-dd").
    /// </summary>
    public required IReadOnlyDictionary<string, (decimal Net, string FirstPaidAt)> PayByStudent { get; init; }

    /// <summary>Berilgan lidlar bo'yicha butun zanjirni bir necha to'plamli so'rovda yig'adi.</summary>
    public static async Task<LeadOutcome> BuildAsync(IAppDbContext db, IEnumerable<string> leadIds)
    {
        var ids = leadIds.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        if (ids.Count == 0)
            return new LeadOutcome
            {
                LeadToStudent = new Dictionary<string, string>(),
                ExistingLeadIds = new HashSet<string>(),
                ActiveStudentIds = new HashSet<string>(),
                ByStudent = new Dictionary<string, (string, string)>(),
                StageByLead = new Dictionary<string, (string, string)>(),
                PayByStudent = new Dictionary<string, (decimal, string)>(),
            };

        // Lid → o'quvchi (ConvertedStudentId) + hozirgi BOSQICH. Ikkalasi bitta so'rovda
        // o'qiladi — `Lead.Stage` (kanban ustuni id'si) shu yerda keladi.
        var leadRows = await db.Leads.Where(l => ids.Contains(l.Id))
            .Select(l => new { l.Id, l.ConvertedStudentId, l.Stage })
            .ToListAsync();
        var leadToStudent = leadRows.Where(l => l.ConvertedStudentId != null)
            .ToDictionary(l => l.Id, l => l.ConvertedStudentId!);
        var studentIds = leadToStudent.Values.Distinct().ToList();

        // Hali MAVJUD lidlar — "o'chirilgan" bayrog'i uchun.
        var existingLeadIds = leadRows.Select(l => l.Id).ToHashSet();

        // Bosqich id → sarlavha/rang. Ustun o'chirib yuborilgan bo'lsa lid bosqichsiz ko'rinadi
        // (kanbanda ham shunday) — sun'iy "Noma'lum bosqich" YOZILMAYDI.
        var stageIds = leadRows.Select(l => l.Stage).Where(s => !string.IsNullOrEmpty(s))
            .Distinct().ToList();
        var stageById = stageIds.Count == 0
            ? new Dictionary<string, (string Title, string Color)>()
            : (await db.LeadStages.Where(s => stageIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.Title, s.Color }).ToListAsync())
                .ToDictionary(s => s.Id, s => (s.Title, s.Color));
        var stageByLead = new Dictionary<string, (string Title, string Color)>();
        foreach (var l in leadRows)
            if (!string.IsNullOrEmpty(l.Stage) && stageById.TryGetValue(l.Stage, out var st))
                stageByLead[l.Id] = st;

        // TO'LOV: o'quv to'lovlari (kirim/tuition) MINUS vozvratlar (chiqim/refund) — moliya
        // bo'limidagi bilan bir xil konvensiya (`GroupBalanceService`).
        //
        // ⚠️ To'lov QAYSI kurs uchun ekani AHAMIYATSIZ: savol "shu lid markazga pul keltirdimi"
        // — odam bitta, keyin qaysi guruhga yozilgani sotuvning natijasini o'zgartirmaydi.
        // ⚠️ Kitob sotuvi bu yerga KIRMAYDI (u `FinanceTransaction`ga umuman yozilmaydi —
        // `.claude/rules/books.md`), ya'ni "to'ladi" faqat O'QISH uchun to'lovni bildiradi.
        var payByStudent = new Dictionary<string, (decimal Net, string FirstPaidAt)>();
        if (studentIds.Count > 0)
        {
            var movements = await db.FinanceTransactions
                .Where(t => t.StudentId != null && studentIds.Contains(t.StudentId)
                            && ((t.Direction == "income" && t.Category == "tuition")
                                || (t.Direction == "expense" && t.Category == "refund")))
                .Select(t => new { StudentId = t.StudentId!, t.Direction, t.Amount, t.Date })
                .ToListAsync();

            foreach (var g in movements.GroupBy(m => m.StudentId))
            {
                var net = g.Sum(m => m.Direction == "expense" ? -m.Amount : m.Amount);
                // Birinchi to'lov sanasi — VOZVRATLAR hisobga olinmaydi (savol "qachon pul keldi").
                var first = g.Where(m => m.Direction == "income")
                    .Select(m => m.Date ?? "")
                    .Where(d => d.Length > 0)
                    .OrderBy(d => d, StringComparer.Ordinal)
                    .FirstOrDefault() ?? "";
                payByStudent[g.Key] = (net, first);
            }
        }

        // FAOL guruh a'zoliklari (Status=="active") — guruh + o'qituvchi (F.I.Sh) uchun.
        var activeMemberships = await db.StudentGroups
            .Where(sg => studentIds.Contains(sg.StudentId) && sg.IsActive && sg.Status == "active")
            .Select(sg => new { sg.StudentId, sg.GroupId }).ToListAsync();
        var active = activeMemberships.Select(m => m.StudentId).ToHashSet();

        var groupIds = activeMemberships.Select(m => m.GroupId).Distinct().ToList();
        var groups = await db.Classes.Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.TeacherId }).ToListAsync();
        var groupById = groups.ToDictionary(g => g.Id, g => g);
        var teacherIds = groups.Select(g => g.TeacherId).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
        var teacherNames = await db.Teachers.Where(t => teacherIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FullName);

        var byStudent = activeMemberships
            .GroupBy(m => m.StudentId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var names = new List<string>();
                    var teachers = new List<string>();
                    foreach (var m in g)
                    {
                        if (!groupById.TryGetValue(m.GroupId, out var grp)) continue;
                        if (!string.IsNullOrEmpty(grp.Name)) names.Add(grp.Name);
                        var tn = teacherNames.GetValueOrDefault(grp.TeacherId ?? "", "");
                        if (!string.IsNullOrEmpty(tn)) teachers.Add(tn);
                    }
                    return (Groups: string.Join(", ", names.Distinct()),
                            Teachers: string.Join(", ", teachers.Distinct()));
                });

        return new LeadOutcome
        {
            LeadToStudent = leadToStudent,
            ExistingLeadIds = existingLeadIds,
            ActiveStudentIds = active,
            ByStudent = byStudent,
            StageByLead = stageByLead,
            PayByStudent = payByStudent,
        };
    }

    /// <summary>Lid aylantirilgan o'quvchi id'si (aylantirilmagan bo'lsa null).</summary>
    public string? StudentOf(string leadId) =>
        LeadToStudent.TryGetValue(leadId ?? "", out var v) ? v : null;

    /// <summary>Lid AKTIV o'quvchiga aylanganmi.</summary>
    public bool IsActive(string leadId)
    {
        var sid = StudentOf(leadId);
        return sid != null && ActiveStudentIds.Contains(sid);
    }

    /// <summary>Lid yaratilgan edi-yu, hozir CRM'da YO'Q (o'chirilgan).
    /// Konvertatsiya holati ta'sir qilmaydi — hali o'quvchiga aylanmagan lid "o'chirilgan" emas.</summary>
    public bool IsDeletedLead(string leadId) =>
        !string.IsNullOrEmpty(leadId) && !ExistingLeadIds.Contains(leadId);

    /// <summary>Lid o'quvchisining faol guruh(lar)i va o'qituvchi(lar)i.</summary>
    public (string Groups, string Teachers) GroupInfo(string leadId)
    {
        var sid = StudentOf(leadId);
        return sid != null && ByStudent.TryGetValue(sid, out var gi) ? gi : ("", "");
    }

    /// <summary>Lidning hozirgi kanban bosqichi (bo'lmasa — bo'sh).</summary>
    public (string Title, string Color) StageOf(string leadId) =>
        StageByLead.TryGetValue(leadId ?? "", out var s) ? s : ("", "");

    /// <summary>Lid keltirgan SOF pul (to'lov − vozvrat). O'quvchiga aylanmagan lid — 0.</summary>
    public decimal PaidTotal(string leadId)
    {
        var sid = StudentOf(leadId);
        return sid != null && PayByStudent.TryGetValue(sid, out var p) ? p.Net : 0m;
    }

    /// <summary>
    /// Lid PUL to'ladimi — sotuvning haqiqiy o'lchovi (faqat "o'quvchi bo'ldi" emas).
    /// Pul qaytarilgan bo'lsa (sof summa ≤ 0) — to'lamagan hisoblanadi.
    /// </summary>
    public bool HasPaid(string leadId) => PaidTotal(leadId) > 0m;

    /// <summary>Birinchi to'lov sanasi ("yyyy-MM-dd"); to'lov bo'lmasa — bo'sh.</summary>
    public string FirstPaidAt(string leadId)
    {
        var sid = StudentOf(leadId);
        return sid != null && PayByStudent.TryGetValue(sid, out var p) ? p.FirstPaidAt : "";
    }
}
