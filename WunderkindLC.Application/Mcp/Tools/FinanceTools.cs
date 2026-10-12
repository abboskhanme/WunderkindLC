using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Money: period summary, transaction journal, debtors, one student's ledger, cash boxes, salary
/// payments and the discount report. Area: finance.main (cash boxes: kassa or finance.main —
/// balances only with finance.main; discount report: finance.discounts).
///
/// <para>Every number reuses the panel's logic: <see cref="StudentLedger"/>, <see cref="StudentGroupLedger"/>
/// (debt reminder), <see cref="CashboxService"/>. Where the panel keeps the logic in a controller
/// (Moliya summary, transaction list, discount report) the same query is reproduced here.
/// Voided transactions are excluded by the global query filter exactly as in the panel; only the
/// journal shows them on request (<c>includeVoided</c>), like Moliya → Amallar.</para>
///
/// <para><see cref="CenterMeta"/> holds integration secrets: MCP reads only its journal-policy and
/// salary-base columns (column-level grant, <see cref="McpReadOnlyGrants"/>) — enough for
/// <see cref="SalaryLedger"/>, so <c>teacher_salaries</c> is the panel's full calculation.</para>
/// </summary>
[McpServerToolType]
public sealed class FinanceTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(FinanceTransaction), typeof(TransactionType), typeof(Cashbox), typeof(CashboxTransfer),
        typeof(Student), typeof(StudentGroup), typeof(Group), typeof(Subject), typeof(SubjectPriceBand),
        typeof(Teacher), typeof(MonthlyCharge), typeof(StudentDiscount),
        // SalaryLedger (teacher_salaries): ownership history, substitutes, journal stats, policy columns.
        typeof(CenterMeta), typeof(GroupTeacherAssignment), typeof(SubstituteTeacherAssignment),
        typeof(LessonNote), typeof(LessonReschedule),
        // SalaryLedger subtracts ACTIVE attendance fines (CRM Phase 3, U3c) — the read-only role needs the table,
        // otherwise teacher_salaries fails with "permission denied". The fine LIST is teacher_attendance_fines.
        typeof(TeacherAttendanceFine),
    ];

    /// <summary>The discount report accepts at most this many months.</summary>
    public const int MaxReportMonths = 24;

    /// <summary>Page cap of tools that build a full ledger per row (debtors, teacher salaries).</summary>
    public const int HeavyPageSize = 50;

    // =====================================================================
    //  finance_summary
    // =====================================================================

    [McpServerTool(Name = "finance_summary", Title = "Moliyaviy xulosa",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Davr bo'yicha moliyaviy xulosa (Moliya → Umumiy bilan bir xil): jami kirim, jami chiqim va sof natija; "
        + "kirim/chiqim toifalari, markazning tranzaksiya turlari, to'lov usullari (naqd/karta/bank) va oylar kesimi; "
        + "o'quvchilarning HOZIRGI jami qarzi va avansi. Bekor qilingan amallar hisobga kirmaydi (alohida soni ko'rsatiladi), "
        + "kassadan kassaga ko'chirish kirim ham, chiqim ham emas. Sukut — joriy oy, ko'pi bilan 366 kun. Moliya ruxsati kerak. "
        + "Income, expense and net for a date range with category, type, payment-method and month breakdowns.")]
    public async Task<string> FinanceSummary(
        [Description("Boshlanish sanasi YYYY-MM-DD (sukut — joriy oyning 1-kuni yoki «to» oyining 1-kuni)")] string? from = null,
        [Description("Tugash sanasi YYYY-MM-DD (sukut — joriy oyning oxirgi kuni; «from» berilsa — bugun)")] string? to = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Finance);
        var (f, e) = PeriodOrMonth(from, to);
        var fromIso = McpToolContext.Iso(f);
        var toIso = McpToolContext.Iso(e);
        var db = t.Db;

        // Same rows as FinanceController.Summary (voided rows are removed by the global filter).
        var rows = await db.FinanceTransactions
            .Where(x => string.Compare(x.Date, fromIso) >= 0 && string.Compare(x.Date, toIso) <= 0)
            .Select(x => new { x.Date, x.Direction, x.Category, x.TypeId, x.Method, x.Amount })
            .ToListAsync(ct);

        var income = rows.Where(r => r.Direction == "income").ToList();
        var expense = rows.Where(r => r.Direction == "expense").ToList();
        var totalIncome = income.Sum(r => r.Amount);
        var totalExpense = expense.Sum(r => r.Amount);
        var tuition = income.Where(r => r.Category == "tuition").Sum(r => r.Amount);

        List<CategoryTotal> ByCategory(IEnumerable<(string Category, decimal Amount)> list, string direction) =>
            list.GroupBy(r => string.IsNullOrEmpty(r.Category) ? "other" : r.Category)
                .Select(g => new CategoryTotal(g.Key, CategoryLabel(g.Key, direction), g.Sum(x => x.Amount), g.Count()))
                .OrderByDescending(c => c.Amount).ThenBy(c => c.Category, StringComparer.Ordinal).ToList();

        var types = await db.TransactionTypes.Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var byType = rows
            .GroupBy(r => (TypeId: r.TypeId ?? "", r.Direction))
            .Select(g => new
            {
                typeId = g.Key.TypeId.Length == 0 ? null : g.Key.TypeId,
                name = g.Key.TypeId.Length == 0 ? "Turi tanlanmagan" : types.GetValueOrDefault(g.Key.TypeId, "(o'chirilgan tur)"),
                direction = g.Key.Direction,
                amount = g.Sum(x => x.Amount),
                count = g.Count(),
            })
            .OrderBy(x => x.direction, StringComparer.Ordinal).ThenByDescending(x => x.amount).ToList();

        var byMethod = rows
            .GroupBy(r => string.IsNullOrEmpty(r.Method) ? "" : r.Method)
            .Select(g => new
            {
                method = g.Key.Length == 0 ? null : g.Key,
                label = MethodLabel(g.Key),
                income = g.Where(x => x.Direction == "income").Sum(x => x.Amount),
                expense = g.Where(x => x.Direction == "expense").Sum(x => x.Amount),
                count = g.Count(),
            })
            .OrderByDescending(x => x.income).ThenBy(x => x.method ?? "", StringComparer.Ordinal).ToList();

        var byMonth = rows
            .GroupBy(r => r.Date.Length >= 7 ? r.Date[..7] : r.Date)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var inc = g.Where(x => x.Direction == "income").Sum(x => x.Amount);
                var exp = g.Where(x => x.Direction == "expense").Sum(x => x.Amount);
                return new { month = g.Key, income = inc, expense = exp, net = inc - exp };
            }).ToList();

        // Voided rows are NOT counted — shown separately so the totals can be reconciled.
        var voided = await db.FinanceTransactions.IgnoreQueryFilters()
            .Where(x => x.IsVoided && string.Compare(x.Date, fromIso) >= 0 && string.Compare(x.Date, toIso) <= 0)
            .Select(x => x.Amount).ToListAsync(ct);

        // Receivables — the panel's Summary: current balances (archived included, archived part separately).
        var balances = await db.Students.Where(s => s.Balance < 0 || s.Balance > 0)
            .Select(s => new { s.Balance, s.IsArchived }).ToListAsync(ct);

        return t.Json(new
        {
            from = fromIso, to = toIso,
            totalIncome, totalExpense, net = totalIncome - totalExpense,
            tuitionIncome = tuition, otherIncome = totalIncome - tuition,
            transactionCount = rows.Count,
            incomeByCategory = ByCategory(income.Select(r => (r.Category, r.Amount)), "income"),
            expenseByCategory = ByCategory(expense.Select(r => (r.Category, r.Amount)), "expense"),
            byType,
            byMethod,
            byMonth,
            voided = new { count = voided.Count, amount = voided.Sum() },
            receivables = new
            {
                studentDebt = balances.Where(b => b.Balance < 0).Sum(b => -b.Balance),
                studentAdvance = balances.Where(b => b.Balance > 0).Sum(b => b.Balance),
                archivedDebt = balances.Where(b => b.IsArchived && b.Balance < 0).Sum(b => -b.Balance),
                archivedAdvance = balances.Where(b => b.IsArchived && b.Balance > 0).Sum(b => b.Balance),
                note = "Joriy holat (davrga bog'liq emas). Arxivdagi o'quvchilar ham kiradi — ularning ulushi alohida.",
            },
        }, rows.Count);
    }

    // =====================================================================
    //  finance_transactions
    // =====================================================================

    [McpServerTool(Name = "finance_transactions", Title = "Moliyaviy amallar jurnali",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kirim/chiqim amallari ro'yxati (Moliya → Amallar / To'lovlar): sana, yo'nalish, toifa va markaz turi, summa, "
        + "to'lov usuli, o'quvchi, o'qituvchi, guruh, kassa, izoh, kvitansiya, kim kiritgani, vozvrat qilingan summa. "
        + "Filtrlar: davr, yo'nalish, toifa, tur, o'quvchi, o'qituvchi, guruh, kassa, to'lov usuli, izohdan qidiruv. "
        + "Eng yangisi tepada, sahifalangan (ko'pi bilan 200). Bekor qilinganlar sukut bo'yicha chiqmaydi "
        + "(includeVoided=true — belgisi bilan chiqadi, jamilarga baribir kirmaydi). Sukut davri — joriy oy. Moliya ruxsati kerak. "
        + "The income/expense journal with filters, newest first, paged.")]
    public async Task<string> FinanceTransactions(
        [Description("Boshlanish sanasi YYYY-MM-DD (sukut — joriy oyning 1-kuni)")] string? from = null,
        [Description("Tugash sanasi YYYY-MM-DD (sukut — joriy oyning oxirgi kuni)")] string? to = null,
        [Description("Yo'nalish: income (kirim) | expense (chiqim)")] string? direction = null,
        [Description("Tizim toifasi kodi: tuition, salary, refund, rent, utilities, internet, book, other ...")] string? category = null,
        [Description("Markaz tranzaksiya turi id (finance_summary → byType)")] string? typeId = null,
        [Description("O'quvchi id")] string? studentId = null,
        [Description("O'qituvchi id")] string? teacherId = null,
        [Description("Guruh id")] string? groupId = null,
        [Description("Kassa id (cashboxes natijasidan)")] string? cashboxId = null,
        [Description("To'lov usuli: cash | card | bank | none (belgilanmagan)")] string? method = null,
        [Description("Izoh, kassir izohi yoki kvitansiya raqami ichidan qidiruv")] string? search = null,
        [Description("Bekor qilinganlar ham chiqsinmi (sukut false)")] bool? includeVoided = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Finance);
        var (f, e) = PeriodOrMonth(from, to);
        var fromIso = McpToolContext.Iso(f);
        var toIso = McpToolContext.Iso(e);
        var db = t.Db;

        var q = includeVoided == true ? db.FinanceTransactions.IgnoreQueryFilters() : db.FinanceTransactions.AsQueryable();
        q = q.Where(x => string.Compare(x.Date, fromIso) >= 0 && string.Compare(x.Date, toIso) <= 0);
        if (!string.IsNullOrWhiteSpace(direction))
        {
            var d = direction.Trim().ToLowerInvariant();
            if (d is not ("income" or "expense")) throw new McpException("«direction»: income | expense.");
            q = q.Where(x => x.Direction == d);
        }
        if (!string.IsNullOrWhiteSpace(category)) { var c = category.Trim(); q = q.Where(x => x.Category == c); }
        if (!string.IsNullOrWhiteSpace(typeId)) { var v = typeId.Trim(); q = q.Where(x => x.TypeId == v); }
        if (!string.IsNullOrWhiteSpace(studentId)) { var v = studentId.Trim(); q = q.Where(x => x.StudentId == v); }
        if (!string.IsNullOrWhiteSpace(teacherId)) { var v = teacherId.Trim(); q = q.Where(x => x.TeacherId == v); }
        if (!string.IsNullOrWhiteSpace(groupId)) { var v = groupId.Trim(); q = q.Where(x => x.GroupId == v); }
        if (!string.IsNullOrWhiteSpace(cashboxId)) { var v = cashboxId.Trim(); q = q.Where(x => x.CashboxId == v); }
        if (!string.IsNullOrWhiteSpace(method))
        {
            var m = method.Trim().ToLowerInvariant();
            q = m switch
            {
                "cash" or "card" or "bank" => q.Where(x => x.Method == m),
                "none" => q.Where(x => x.Method == null || x.Method == ""),
                _ => throw new McpException("«method»: cash | card | bank | none."),
            };
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(x => (x.Note != null && x.Note.ToLower().Contains(term))
                             || (x.Comment != null && x.Comment.ToLower().Contains(term))
                             || (x.ReceiptNo != null && x.ReceiptNo.ToLower().Contains(term)));
        }

        // Totals of the WHOLE filtered set (bounded by the ≤ 366-day range); voided rows never count.
        var sums = await q.Select(x => new { x.Direction, x.Amount, x.IsVoided }).ToListAsync(ct);
        var total = sums.Count;
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(x => new
            {
                x.Id, x.Date, x.Direction, x.Category, x.TypeId, x.Amount, x.Method, x.Month,
                x.StudentId, x.TeacherId, x.GroupId, x.CashboxId, x.Note, x.Comment, x.ReceiptNo, x.PaidTime,
                x.CreatedBy, x.CreatedAt, x.RefundOfId, x.BookOrderId,
                x.IsVoided, x.VoidedAt, x.VoidedBy, x.VoidReason,
            }).ToListAsync(ct);

        var studentIds = rows.Where(r => r.StudentId != null).Select(r => r.StudentId!).Distinct().ToList();
        var teacherIds = rows.Where(r => r.TeacherId != null).Select(r => r.TeacherId!).Distinct().ToList();
        var groupIds = rows.Where(r => r.GroupId != null).Select(r => r.GroupId!).Distinct().ToList();
        var students = await db.Students.Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName }).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var teachers = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var groups = await db.Classes.Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name }).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var cashboxes = await db.Cashboxes.Select(b => new { b.Id, b.Name }).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var types = await db.TransactionTypes.Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        // Refunded part of each tuition payment on the page (the panel's "Qaytarilgan" column).
        var paymentIds = rows.Where(r => r.Direction == "income" && r.Category == "tuition").Select(r => r.Id).ToList();
        var refunded = (await db.FinanceTransactions
                .Where(x => x.Direction == "expense" && x.Category == "refund" && x.RefundOfId != null
                            && paymentIds.Contains(x.RefundOfId))
                .Select(x => new { x.RefundOfId, x.Amount }).ToListAsync(ct))
            .GroupBy(x => x.RefundOfId!).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        static object? Ref(string? id, IReadOnlyDictionary<string, string> names) =>
            id is null ? null : new { id, name = names.GetValueOrDefault(id) };

        var items = rows.Select(r => new
        {
            r.Id, r.Date, r.Direction, r.Category,
            categoryLabel = CategoryLabel(r.Category, r.Direction),
            r.TypeId, typeName = r.TypeId is null ? null : types.GetValueOrDefault(r.TypeId),
            r.Amount, r.Method, methodLabel = MethodLabel(r.Method), r.Month,
            student = Ref(r.StudentId, students),
            teacher = Ref(r.TeacherId, teachers),
            group = Ref(r.GroupId, groups),
            cashbox = Ref(r.CashboxId, cashboxes),
            r.Note, r.Comment, r.ReceiptNo, r.PaidTime, r.CreatedBy,
            createdAt = r.CreatedAt == default ? null : AppClock.ToLocal(r.CreatedAt).ToString("yyyy-MM-ddTHH:mm:ss"),
            refunded = refunded.TryGetValue(r.Id, out var rf) ? rf : (decimal?)null,
            r.RefundOfId,
            fromBookSale = r.BookOrderId != null,
            voided = r.IsVoided,
            voidInfo = r.IsVoided ? new { at = r.VoidedAt, by = r.VoidedBy, reason = r.VoidReason } : null,
        }).ToList();

        var live = sums.Where(s => !s.IsVoided).ToList();
        return t.Json(new
        {
            from = fromIso, to = toIso,
            total, page = p, pageSize = size,
            totals = new
            {
                income = live.Where(s => s.Direction == "income").Sum(s => s.Amount),
                expense = live.Where(s => s.Direction == "expense").Sum(s => s.Amount),
                voidedCount = sums.Count - live.Count,
            },
            items,
        }, items.Count);
    }

    // =====================================================================
    //  finance_debtors
    // =====================================================================

    [McpServerTool(Name = "finance_debtors", Title = "Qarzdorlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Qarzdor o'quvchilar (balansi manfiy — o'quvchilar ro'yxatidagi «Qarzdorlar» va bosh sahifa bilan bir xil ta'rif), "
        + "eng katta qarz tepada. Har qatorda umumiy qarz, telefonlar va FAN (guruh) bo'yicha qarz qatorlari — to'lov eslatmasi "
        + "ishlatadigan per-guruh hisob: qaysi oylar to'lanmagan va qancha. Faqat muddati kelgan oylar (joriy oygacha) sanaladi. "
        + "Filtrlar: guruh, kurs (o'sha guruh/kursda to'lovli a'zoligi bor-bo'lgan qarzdorlar), minimal qarz, arxivdagilar. "
        + "Sahifalangan. Moliya ruxsati kerak. "
        + "Students who owe money with per-group unpaid months, biggest debt first.")]
    public async Task<string> FinanceDebtors(
        [Description("Guruh id")] string? groupId = null,
        [Description("Kurs (fan) id")] string? courseId = null,
        [Description("Minimal qarz summasi (so'm)")] decimal? minDebt = null,
        [Description("Arxivdagi o'quvchilar ham kirsinmi (sukut false)")] bool? includeArchived = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Finance);
        if (minDebt is < 0) throw new McpException("«minDebt» manfiy bo'lishi mumkin emas.");
        var db = t.Db;

        var q = db.Students.Where(s => s.Balance < 0);
        if (includeArchived != true) q = q.Where(s => !s.IsArchived);
        if (minDebt is > 0)
        {
            var limit = -minDebt.Value;
            q = q.Where(s => s.Balance <= limit);
        }
        if (!string.IsNullOrWhiteSpace(groupId))
        {
            var gid = groupId.Trim();
            q = q.Where(s => db.StudentGroups.Any(m => m.StudentId == s.Id && m.GroupId == gid && m.Status != "trial"));
        }
        if (!string.IsNullOrWhiteSpace(courseId))
        {
            var cid = courseId.Trim();
            var courseGroups = db.Classes.Where(g => g.CourseId == cid).Select(g => g.Id);
            q = q.Where(s => db.StudentGroups.Any(m => m.StudentId == s.Id && m.Status != "trial"
                                                       && courseGroups.Contains(m.GroupId)));
        }

        // Debtors are a small subset; ordering by a decimal is not portable in SQL (SQLite), so the
        // light projection is sorted here and only the page is loaded in full.
        var candidates = await q.Select(s => new { s.Id, s.FullName, s.Balance }).ToListAsync(ct);
        var ordered = candidates.OrderBy(c => c.Balance).ThenBy(c => c.FullName, StringComparer.Ordinal)
            .ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var p = McpToolContext.Page(page);
        var size = Math.Min(McpToolContext.PageSize(pageSize), HeavyPageSize);   // a ledger per row
        var pageIds = ordered.Skip((p - 1) * size).Take(size).Select(c => c.Id).ToList();

        var students = (await db.Students.Where(s => pageIds.Contains(s.Id)).ToListAsync(ct)).ToDictionary(s => s.Id);
        var memberships = await db.StudentGroups.Where(m => pageIds.Contains(m.StudentId)).ToListAsync(ct);
        var (groups, teacherNames) = await GroupsAndTeachersAsync(memberships.Select(m => m.GroupId), ct);
        var book = await DiscountBook.LoadAsync(db, pageIds);
        var current = TuitionService.CurrentMonth();

        var items = new List<object>();
        foreach (var id in pageIds)
        {
            if (!students.TryGetValue(id, out var s)) continue;
            var own = memberships.Where(m => m.StudentId == id).ToList();
            var debt = -s.Balance;
            var lines = own.Count == 0
                ? new List<GroupDebt>()
                : (await GroupDebtsAsync(s, own, groups, teacherNames, book, current))
                    .Where(l => l.Debt > 0).OrderByDescending(l => l.Debt).ToList();
            items.Add(new
            {
                studentId = s.Id, s.FullName, s.Phone,
                parent = new { name = s.ParentFullName, phone = s.ParentPhone, fatherPhone = s.FatherPhone, motherPhone = s.MotherPhone },
                s.IsArchived,
                debt,
                groups = lines.Select(l => new
                {
                    l.GroupId, l.GroupName, l.Course, l.Teacher, l.MembershipStatus, l.GroupArchived, l.Debt,
                    unpaidMonths = l.Months.Where(m => m.Remaining > 0),
                }),
                // The reminder's fallback: debt that no group ledger explains (legacy, untagged charges).
                ungroupedDebt = lines.Count == 0 ? debt : (decimal?)null,
            });
        }

        return t.Json(new
        {
            total = ordered.Count, page = p, pageSize = size,
            totalDebt = ordered.Sum(c => -c.Balance),
            note = "Qarz = o'quvchining umumiy balansi (manfiy). Fan qatorlari — per-guruh hisob, faqat joriy oygacha bo'lgan oylar; "
                   + "ularning yig'indisi umumiy qarzdan farq qilishi mumkin (guruhga bog'lanmagan eski hisob/to'lovlar).",
            items,
        }, items.Count);
    }

    // =====================================================================
    //  student_ledger
    // =====================================================================

    [McpServerTool(Name = "student_ledger", Title = "O'quvchi to'lov tarixi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta o'quvchining to'lov tarixi (profil → To'lovlar tarixi bilan bir xil): oyma-oy hisoblangan summa, chegirma, "
        + "to'langan, qoldiq va holat; har oyning guruhlar bo'yicha qatorlari (qo'lda tahrirlangan — locked belgisi); "
        + "fan (guruh) bo'yicha qarz (to'lov oynasidagi hisob); amalga oshirilgan to'lovlar (bekor qilinganlari belgisi bilan), "
        + "vozvratlar va joriy balans. Moliya ruxsati kerak. "
        + "One student's month-by-month charges, payments and balance.")]
    public async Task<string> StudentLedgerTool(
        [Description("O'quvchi id (students_search yoki finance_debtors natijasidan)")] string studentId,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Finance);
        if (string.IsNullOrWhiteSpace(studentId)) throw new McpException("studentId kerak.");
        var db = t.Db;
        var id = studentId.Trim();
        var st = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (st is null) return t.Json(new { message = "O'quvchi topilmadi." }, 0);

        // The panel's endpoint: StudentsController.Ledger = StudentLedger.BuildAsync + voided payments.
        var ledger = await StudentLedger.BuildAsync(db, st);
        var voided = await StudentLedger.VoidedPaymentsAsync(db, st.Id);

        var chargeRows = await db.MonthlyCharges.Where(c => c.StudentId == st.Id)
            .Select(c => new { c.Month, c.GroupId, c.Amount, c.Discount, c.Locked }).ToListAsync(ct);
        var memberships = await db.StudentGroups.Where(m => m.StudentId == st.Id).ToListAsync(ct);
        var (groups, teacherNames) = await GroupsAndTeachersAsync(memberships.Select(m => m.GroupId).Concat(chargeRows.Where(c => c.GroupId != null).Select(c => c.GroupId!)), ct);
        var courseIds = groups.Values.Select(g => g.CourseId).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        var courses = await db.Subjects.Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var months = ledger.Months.Select(m => new
        {
            m.Month, charged = m.Charged, m.Discount, m.Paid, m.Remaining, m.Status,
            groups = chargeRows.Where(c => c.Month == m.Month)
                .OrderBy(c => c.GroupId ?? "", StringComparer.Ordinal)
                .Select(c =>
                {
                    var g = c.GroupId is null ? null : groups.GetValueOrDefault(c.GroupId);
                    return new
                    {
                        groupId = c.GroupId,
                        groupName = g?.Name ?? (c.GroupId is null ? "Guruhsiz (eski hisob)" : "(o'chirilgan guruh)"),
                        course = g is null || string.IsNullOrEmpty(g.CourseId) ? null : courses.GetValueOrDefault(g.CourseId),
                        c.Amount, c.Discount, c.Locked,
                    };
                }),
        }).ToList();

        var book = await DiscountBook.LoadForStudentAsync(db, st.Id);
        var byGroup = memberships.Count == 0
            ? new List<GroupDebt>()
            : await GroupDebtsAsync(st, memberships, groups, teacherNames, book, TuitionService.CurrentMonth());

        var payments = ledger.Payments.Concat(voided).OrderByDescending(x => x.Date, StringComparer.Ordinal)
            .Select(x => new
            {
                x.Date, x.Amount, x.Method, methodLabel = MethodLabel(x.Method), x.Month,
                group = x.GroupName, course = x.CourseName, teacher = x.TeacherName,
                x.Note, x.Comment, x.ReceiptNo, x.PaidTime,
                voided = x.IsVoided,
                voidInfo = x.IsVoided ? new { at = x.VoidedAt, by = x.VoidedBy, reason = x.VoidReason } : null,
            }).ToList();

        var refunds = await db.FinanceTransactions
            .Where(x => x.StudentId == st.Id && x.Direction == "expense" && x.Category == "refund")
            .OrderByDescending(x => x.Date)
            .Select(x => new { x.Id, x.Date, x.Amount, x.Month, x.GroupId, x.RefundOfId, x.Note })
            .ToListAsync(ct);

        return t.Json(new
        {
            student = new { st.Id, st.FullName, st.Phone, parentPhone = st.ParentPhone, st.IsArchived },
            balance = st.Balance,
            debt = st.Balance < 0 ? -st.Balance : 0m,
            advance = st.Balance > 0 ? st.Balance : 0m,
            currentMonthlyFee = ledger.MonthlyFee,
            totals = new { charged = ledger.TotalCharged, discount = ledger.TotalDiscount, paid = ledger.TotalPaid },
            months,
            byGroup = byGroup.Select(l => new
            {
                l.GroupId, l.GroupName, l.Course, l.Teacher, l.MembershipStatus, l.GroupArchived, debt = l.Debt,
                months = l.Months,
            }),
            payments,
            refunds,
            note = "«months» — umumiy (barcha guruhlar) hisob, to'lovlar oylarga taqsimlangan; «byGroup» — har guruh alohida, "
                   + "faqat shu guruhga teglangan to'lovlar va joriy oygacha bo'lgan oylar. Bekor qilingan to'lovlar jamilarga kirmaydi.",
        }, months.Count + payments.Count);
    }

    // =====================================================================
    //  cashboxes
    // =====================================================================

    [McpServerTool(Name = "cashboxes", Title = "Kassalar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kassalar (Moliya → Kassalar): nomi, mas'ul xodim, asosiy/arxiv belgisi; moliya ruxsati bo'lsa — joriy qoldiq "
        + "(butun tarix: kirim − chiqim + kelgan − ketgan ko'chirishlar) va tanlangan davrdagi kirim, chiqim, ko'chirishlar, "
        + "hamda barcha kassalar jami (arxivdagilar ham). Sukut davri — joriy oy. "
        + "Cash boxes with responsible person and current balance.")]
    public async Task<string> Cashboxes(
        [Description("Davr boshi YYYY-MM-DD (davr kirim/chiqimi uchun; sukut — joriy oyning 1-kuni)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — joriy oyning oxirgi kuni)")] string? to = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Kassa);
        var finance = t.CanReadFinance;
        var (f, e) = PeriodOrMonth(from, to);
        var db = t.Db;

        var boxes = await db.Cashboxes.ToListAsync(ct);
        // Balances only with finance.main — the panel's cash box cards are `finance.main` (ReadRequiresPerm).
        // The panel's service scans every cash-box transaction; cached briefly (key does not depend on the
        // caller — the permission check above runs first) so a looping caller does not repeat the scan.
        var balances = finance
            ? await t.CachedAsync($"cashboxes:{McpToolContext.Iso(f)}:{McpToolContext.Iso(e)}", TimeSpan.FromSeconds(60),
                () => CashboxService.BalancesAsync(db, McpToolContext.Iso(f), McpToolContext.Iso(e), ct))
            : new Dictionary<string, CashboxService.Balance>();

        var ordered = CashboxService.Active(boxes)
            .Concat(boxes.Where(b => b.IsArchived).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var items = ordered.Select(b =>
        {
            var bal = balances.GetValueOrDefault(b.Id);
            return new
            {
                b.Id, b.Name, responsibleName = b.ResponsibleName,
                isMain = b.Id == CashboxService.MainId, b.IsArchived, b.CreatedAt,
                balance = finance ? bal?.Total ?? 0m : (decimal?)null,
                periodIncome = finance ? bal?.PeriodIncome ?? 0m : (decimal?)null,
                periodExpense = finance ? bal?.PeriodExpense ?? 0m : (decimal?)null,
                periodTransferIn = finance ? bal?.PeriodTransferIn ?? 0m : (decimal?)null,
                periodTransferOut = finance ? bal?.PeriodTransferOut ?? 0m : (decimal?)null,
            };
        }).ToList();

        return t.Json(new
        {
            from = McpToolContext.Iso(f), to = McpToolContext.Iso(e),
            total = finance ? items.Sum(i => i.balance ?? 0m) : (decimal?)null,
            activeTotal = finance ? items.Where(i => !i.IsArchived).Sum(i => i.balance ?? 0m) : (decimal?)null,
            note = finance ? null : "Qoldiq va kirim/chiqim ko'rsatilmadi: moliya ruxsati yo'q.",
            items,
        }, items.Count);
    }

    // =====================================================================
    //  teacher_salaries
    // =====================================================================

    [McpServerTool(Name = "teacher_salaries", Title = "O'qituvchilar maoshi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bir oy uchun o'qituvchilar maoshi — Moliya → O'qituvchilar bilan BIR XIL hisob (SalaryLedger): "
        + "hisoblangan maosh (qat'iy yoki guruh to'lovlaridan foiz), jurnalda belgilanmagan darslar uchun ushlanma, "
        + "o'rinbosarlik haqi/ushlanmasi, davomat jarimalari (davomat o'z vaqtida olinmagan darslar), berilgan maosh va "
        + "qoldiq; foizli maoshda baza (hisoblangan yoki yig'ilgan pul) va "
        + "«hozir chiqadigan» summa. Maosh qaysi oy uchun berilgani hisoblanadi (iyul maoshi avgustda berilsa ham iyulga). "
        + "Faol o'qituvchilar va shu oy uchun maosh olgan arxivdagilar. Moliya ruxsati kerak. "
        + "Accrued salary, deductions, paid and remaining per teacher for one month, as the panel computes them.")]
    public async Task<string> TeacherSalaries(
        [Description("Oy YYYY-MM (sukut — joriy oy)")] string? month = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Finance);
        var m = McpToolContext.Month(month);
        var db = t.Db;

        // Archived teachers appear only when they were paid for this month (SalaryLedger.PayMonth: `Month`, else the date).
        var monthPayments = await db.FinanceTransactions
            .Where(x => x.Direction == "expense" && x.Category == "salary" && x.TeacherId != null
                        && ((x.Month != null && x.Month.Length >= 7 && x.Month.StartsWith(m))
                            || ((x.Month == null || x.Month.Length < 7) && x.Date.StartsWith(m))))
            .Select(x => new { TeacherId = x.TeacherId!, x.Amount }).ToListAsync(ct);
        var paidIds = monthPayments.Select(x => x.TeacherId).Distinct().ToList();

        var tq = db.Teachers.Where(x => !x.IsArchived || paidIds.Contains(x.Id));
        var total = await tq.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = Math.Min(McpToolContext.PageSize(pageSize), HeavyPageSize);   // a ledger per row
        var teachers = await tq.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var items = new List<object>();
        foreach (var teacher in teachers)
        {
            // The panel's salary report (FinanceController.SalaryReport) calls exactly this, per teacher.
            var ledger = await SalaryLedger.BuildAsync(db, teacher, m, m);
            var row = ledger.Months.FirstOrDefault(x => x.Month == m);
            var payments = ledger.Payments
                .Where(pm => (pm.Month is { Length: >= 7 } pmMonth ? pmMonth[..7] : pm.Date.Length >= 7 ? pm.Date[..7] : "") == m)
                .ToList();
            items.Add(new
            {
                teacherId = teacher.Id, teacher.FullName, teacher.IsArchived,
                salaryMode = ledger.SalaryMode,
                fixedSalary = ledger.SalaryMode == "fixed" ? ledger.Salary : (decimal?)null,
                salaryPercent = ledger.SalaryMode == "percent" ? ledger.SalaryPercent : (decimal?)null,
                accrued = row?.Expected ?? 0m,
                accruedBeforeDeduction = row?.BaseExpected ?? 0m,
                journalDeduction = row?.Deduction ?? 0m,
                lessons = row is null ? null : new { planned = row.PlannedLessons, conducted = row.ConductedLessons, missed = row.MissedLessons },
                substituteFee = row?.SubstituteFee ?? 0m,
                substituteDeduction = row?.SubstituteDeduction ?? 0m,
                // Attendance fines of the month (CRM Phase 3, U3c): ACTIVE fines, capped at what is left after the
                // journal deduction — already subtracted from «accrued». List: teacher_attendance_fines.
                attendanceFines = row?.AttendanceFines ?? 0m,
                attendanceFineCount = row?.AttendanceFineCount ?? 0,
                percentBase = row is null || ledger.SalaryMode != "percent" ? null
                    : new { amount = row.SalaryBase, fromCharged = row.ChargedBase, collected = row.Collected, charged = row.Charged },
                paid = row?.Paid ?? payments.Sum(pm => pm.Amount),
                remaining = row?.Remaining ?? 0m,
                status = row?.Status,
                payments = payments.Select(pm => new { pm.Date, pm.Amount, pm.Method, methodLabel = MethodLabel(pm.Method), pm.Note }),
            });
        }

        return t.Json(new
        {
            month = m, total, page = p, pageSize = size,
            totalPaid = monthPayments.Sum(x => x.Amount),   // all teachers, not just this page
            journalLinked = (await JournalPolicy.GetAsync(db)).SalaryRequireJournal,
            note = "Hisob panel bilan bir xil (SalaryLedger). «accrued» — jurnal ushlanmasi va davomat jarimalaridan keyingi "
                   + "hisoblangan maosh; «attendanceFines» — shu oyning faol davomat jarimalari (ushlanmadan keyingi qoldiqdan "
                   + "oshmaydi, «attendanceFineCount» — jarimali darslar); «remaining» — accrued − paid.",
            items,
        }, items.Count);
    }

    // =====================================================================
    //  discounts_report
    // =====================================================================

    [McpServerTool(Name = "discounts_report", Title = "Chegirmalar hisoboti",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Chegirmalar hisoboti (Hisobotlar → Chegirmalar bilan bir xil): oylar oralig'ida hisoblangan summa va berilgan chegirma "
        + "(oylik hisoblardan — o'sha oyda haqiqatan qo'llangani), chegirma ulushi, oylar, o'qituvchilar, guruhlar va o'quvchilar "
        + "kesimi, hozir amaldagi chegirmalar sabablari bo'yicha. Sukut — oxirgi 12 oy, ko'pi bilan 24 oy. "
        + "O'quvchilar ro'yxati sahifalangan (eng katta chegirma tepada). Chegirmalar hisoboti ruxsati kerak. "
        + "Discount totals per month, teacher, group, student and reason for a month range.")]
    public async Task<string> DiscountsReport(
        [Description("Boshlanish oyi YYYY-MM (sukut — 11 oy oldin)")] string? from = null,
        [Description("Tugash oyi YYYY-MM (sukut — joriy oy)")] string? to = null,
        [Description("O'quvchilar ro'yxati sahifasi (1 dan)")] int? page = null,
        [Description("O'quvchilar sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Discounts);
        var current = TuitionService.CurrentMonth();
        var toMonth = string.IsNullOrWhiteSpace(to) ? current : McpToolContext.Month(to, "to");
        var fromMonth = string.IsNullOrWhiteSpace(from)
            ? McpToolContext.Today.AddMonths(-11).ToString("yyyy-MM")
            : McpToolContext.Month(from, "from");
        if (string.CompareOrdinal(fromMonth, toMonth) > 0) fromMonth = toMonth;   // the panel clamps the same way
        if (TuitionService.MonthRange(fromMonth, toMonth).Count() > MaxReportMonths)
            throw new McpException($"Oylar oralig'i ko'pi bilan {MaxReportMonths} oy bo'lishi mumkin.");
        var db = t.Db;

        // (1) Money: every charge of the period (not only discounted ones — the share needs the denominator).
        var charges = await db.MonthlyCharges
            .Where(c => string.Compare(c.Month, fromMonth) >= 0 && string.Compare(c.Month, toMonth) <= 0)
            .Select(c => new { c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount })
            .ToListAsync(ct);

        // (2) Names: group → teacher / course.
        var groupRows = await db.Classes.Select(g => new { g.Id, g.Name, g.TeacherId, g.CourseId }).ToListAsync(ct);
        var teacherById = await db.Teachers.Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var courseById = await db.Subjects.Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var groupById = groupRows.ToDictionary(g => g.Id, g => new GroupInfo(
            g.Name,
            string.IsNullOrEmpty(g.TeacherId) ? "" : g.TeacherId,
            string.IsNullOrEmpty(g.TeacherId) || !teacherById.TryGetValue(g.TeacherId, out var tn) ? NoTeacherLabel : tn,
            string.IsNullOrEmpty(g.CourseId) || !courseById.TryGetValue(g.CourseId, out var cn) ? "" : cn));

        var totalStudents = await db.Students.CountAsync(s => !s.IsArchived, ct);

        // (3) Registry: discounts in force now; an archived student's discount is not "in force".
        var activeRows = await db.StudentDiscounts.Where(d => d.Status == StudentDiscount.StatusActive).ToListAsync(ct);
        var registryStudentIds = activeRows.Select(d => d.StudentId).Distinct().ToList();
        var archivedIds = (await db.Students.Where(s => s.IsArchived && registryStudentIds.Contains(s.Id))
            .Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        activeRows = activeRows.Where(d => !archivedIds.Contains(d.StudentId)).ToList();
        var activeByStudent = activeRows.GroupBy(d => d.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal).ToList());

        // (4) Summary.
        var periodCharged = charges.Sum(c => c.Amount);
        var periodDiscount = charges.Sum(c => c.Discount);
        var studentCount = activeByStudent.Count;
        var summary = new
        {
            activeCount = activeRows.Count,
            studentCount,
            totalStudents,
            studentSharePct = Share(studentCount, totalStudents),
            periodCharged,
            periodDiscount,
            sharePct = Share(periodDiscount, periodCharged),
            currentMonthDiscount = charges.Where(c => c.Month == current).Sum(c => c.Discount),
        };

        // (5) Months — every month of the period, zero months included.
        var byMonthRaw = charges.GroupBy(c => c.Month).ToDictionary(g => g.Key, g => g.ToList());
        var months = TuitionService.MonthRange(fromMonth, toMonth).Select(m => byMonthRaw.TryGetValue(m, out var rows)
                ? new { month = m, charged = rows.Sum(r => r.Amount), discount = rows.Sum(r => r.Discount),
                        students = rows.Where(r => r.Discount != 0m).Select(r => r.StudentId).Distinct().Count() }
                : new { month = m, charged = 0m, discount = 0m, students = 0 })
            .ToList();

        // (6) Teachers (the teacher of the group the charge was written in).
        var byTeacher = charges
            .GroupBy(c => c.GroupId is not null && groupById.TryGetValue(c.GroupId, out var g)
                ? (g.TeacherId, g.TeacherName) : ("", NoGroupLabel))
            .Select(g => new
            {
                teacherId = g.Key.Item1, teacherName = g.Key.Item2,
                charged = g.Sum(c => c.Amount), discount = g.Sum(c => c.Discount),
                students = g.Where(c => c.Discount != 0m).Select(c => c.StudentId).Distinct().Count(),
                groups = g.Where(c => c.GroupId is not null).Select(c => c.GroupId!).Distinct().Count(),
            })
            .OrderByDescending(x => x.discount).ThenBy(x => x.teacherName, StringComparer.Ordinal).ToList();

        // (7) Groups.
        var byGroup = charges.GroupBy(c => c.GroupId ?? "")
            .Select(g =>
            {
                var info = g.Key.Length > 0 && groupById.TryGetValue(g.Key, out var gi)
                    ? gi
                    : new GroupInfo(g.Key.Length == 0 ? NoGroupLabel : "(o'chirilgan guruh)", "", NoTeacherLabel, "");
                return new
                {
                    groupId = g.Key.Length == 0 ? null : g.Key, groupName = info.Name, teacherName = info.TeacherName,
                    courseName = info.CourseName,
                    charged = g.Sum(c => c.Amount), discount = g.Sum(c => c.Discount),
                    students = g.Where(c => c.Discount != 0m).Select(c => c.StudentId).Distinct().Count(),
                };
            })
            .OrderByDescending(x => x.discount).ThenBy(x => x.groupName, StringComparer.Ordinal).ToList();

        // (8) Students — biggest discount first, paged.
        var studentAgg = charges.Where(c => c.Discount != 0m).GroupBy(c => c.StudentId)
            .Select(g => new { StudentId = g.Key, Rows = g.ToList(), Discount = g.Sum(c => c.Discount) })
            .OrderByDescending(x => x.Discount).ThenBy(x => x.StudentId, StringComparer.Ordinal).ToList();
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var pageAgg = studentAgg.Skip((p - 1) * size).Take(size).ToList();
        var pageStudentIds = pageAgg.Select(x => x.StudentId).ToList();
        var studentNames = await db.Students.Where(s => pageStudentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName }).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var byStudent = pageAgg.Select(x =>
        {
            var infos = x.Rows.Where(c => c.GroupId is not null)
                .Select(c => groupById.GetValueOrDefault(c.GroupId!)).Where(gi => gi is not null).Select(gi => gi!).ToList();
            var reg = activeByStudent.GetValueOrDefault(x.StudentId) ?? new List<StudentDiscount>();
            return new
            {
                studentId = x.StudentId, studentName = studentNames.GetValueOrDefault(x.StudentId, ""),
                charged = x.Rows.Sum(c => c.Amount), discount = x.Discount,
                months = x.Rows.Select(c => c.Month).Distinct().Count(),
                groupNames = infos.Select(i => i.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList(),
                teacherNames = infos.Select(i => i.TeacherName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList(),
                activeCount = reg.Count, activeLabel = DiscountRules.ActiveLabel(reg),
            };
        }).ToList();

        // (9) Reasons — registry rows; the sum is the period discount of those students (each counted once per reason).
        var discountByStudent = charges.GroupBy(c => c.StudentId).ToDictionary(g => g.Key, g => g.Sum(c => c.Discount));
        var byReason = activeRows.GroupBy(d => (d.Reason ?? "").Trim())
            .Select(g => new
            {
                reason = g.Key.Length == 0 ? "— sababsiz —" : g.Key,
                count = g.Count(),
                discount = g.Select(d => d.StudentId).Distinct().Sum(sid => discountByStudent.GetValueOrDefault(sid, 0m)),
            })
            .OrderByDescending(r => r.discount).ThenByDescending(r => r.count).ThenBy(r => r.reason, StringComparer.Ordinal)
            .ToList();

        return t.Json(new
        {
            from = fromMonth, to = toMonth,
            summary, months, byReason, byTeacher, byGroup,
            byStudent = new { total = studentAgg.Count, page = p, pageSize = size, items = byStudent },
        }, byStudent.Count);
    }

    // =====================================================================
    //  helpers
    // =====================================================================

    private const string NoGroupLabel = "Guruhsiz";
    private const string NoTeacherLabel = "Biriktirilmagan";

    private sealed record GroupInfo(string Name, string TeacherId, string TeacherName, string CourseName);

    internal sealed record CategoryTotal(string Category, string Label, decimal Amount, int Count);

    internal sealed record DueMonth(string Month, decimal Fee, decimal Paid, decimal Remaining, string Status);

    internal sealed record GroupDebt(
        string GroupId, string GroupName, string Course, string? Teacher, string MembershipStatus, bool GroupArchived,
        decimal Debt, List<DueMonth> Months);

    private static decimal Share(decimal part, decimal total) =>
        total <= 0m ? 0m : decimal.Round(part * 100m / total, 1);

    /// <summary>
    /// Both dates empty → the current calendar month; only <c>to</c> → from the 1st of its month;
    /// only <c>from</c> → up to today. Always validated through <see cref="McpToolContext.Range"/>.
    /// </summary>
    private static (DateOnly From, DateOnly To) PeriodOrMonth(string? from, string? to)
    {
        var today = McpToolContext.Today;
        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
        {
            var first = new DateOnly(today.Year, today.Month, 1);
            return (first, first.AddMonths(1).AddDays(-1));
        }
        var end = McpToolContext.Date(to, today, "to");
        var start = McpToolContext.Date(from, new DateOnly(end.Year, end.Month, 1), "from");
        return McpToolContext.Range(McpToolContext.Iso(start), McpToolContext.Iso(end));
    }

    /// <summary>Groups (by id) and their teachers' names — small lookups for the ledger lines.</summary>
    private async Task<(Dictionary<string, Group> Groups, Dictionary<string, string> TeacherNames)> GroupsAndTeachersAsync(
        IEnumerable<string> groupIds, CancellationToken ct)
    {
        var db = t.Db;
        var ids = groupIds.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var groups = (await db.Classes.Where(g => ids.Contains(g.Id)).ToListAsync(ct)).ToDictionary(g => g.Id);
        var teacherIds = groups.Values.Select(g => g.TeacherId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var teacherNames = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return (groups, teacherNames);
    }

    /// <summary>
    /// Per-subject debt exactly as the debt reminder builds it (<c>PaymentReminderService.BuildMessagesAsync</c>):
    /// every billable (non-trial) membership — finished ones too — one line per group (the live membership,
    /// otherwise the latest finished one), amounts from <see cref="StudentGroupLedger"/>.
    /// <para>⚠️ Only months up to the CURRENT one are due: the group ledger also lists the next three
    /// months of an active membership as payable in advance (with the full fee as "remaining"); those are
    /// not debt.</para>
    /// </summary>
    private async Task<List<GroupDebt>> GroupDebtsAsync(Student s, IReadOnlyCollection<StudentGroup> memberships,
        IReadOnlyDictionary<string, Group> groups, IReadOnlyDictionary<string, string> teacherNames,
        DiscountBook book, string currentMonth)
    {
        var db = t.Db;
        var result = new List<GroupDebt>();
        foreach (var grp in memberships.Where(m => m.Status != "trial").GroupBy(m => m.GroupId)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var m = MembershipLifecycle.PrimaryMembership(grp)
                    ?? grp.OrderByDescending(x => x.LeftAt ?? "", StringComparer.Ordinal)
                        .ThenByDescending(x => x.JoinedAt ?? "", StringComparer.Ordinal)
                        .First();
            if (!groups.TryGetValue(m.GroupId, out var g)) continue;
            var ledger = await StudentGroupLedger.BuildAsync(db, s, g, m, book);
            var due = ledger.Months.Where(x => string.CompareOrdinal(x.Month, currentMonth) <= 0)
                .Select(x => new DueMonth(x.Month, x.Fee, x.Paid, x.Remaining, x.Status)).ToList();
            result.Add(new GroupDebt(
                g.Id, g.Name, ledger.CourseName,
                string.IsNullOrEmpty(g.TeacherId) ? null : teacherNames.GetValueOrDefault(g.TeacherId),
                m.IsActive ? m.Status : "left", g.IsArchived, due.Sum(x => x.Remaining), due));
        }
        return result;
    }

    private static readonly Dictionary<string, string> IncomeLabels = new(StringComparer.Ordinal)
    {
        ["tuition"] = "O'quvchi to'lovi", ["book"] = "Kitob/qo'llanma sotuvi", ["mock"] = "Mock imtihon to'lovi",
        ["donation"] = "Homiylik", ["rent_in"] = "Ijaradan kirim", ["other"] = "Boshqa kirim",
    };

    private static readonly Dictionary<string, string> ExpenseLabels = new(StringComparer.Ordinal)
    {
        ["salary"] = "Oylik maosh", ["utilities"] = "Kommunal", ["internet"] = "Internet", ["stationery"] = "Kantselyariya",
        ["supplies"] = "Jihoz/materiallar", ["rent"] = "Ijara", ["repair"] = "Ta'mirlash", ["penalty"] = "Jarima",
        ["other"] = "Boshqa chiqim",
    };

    /// <summary>The panel's <c>financeCategoryLabel(category, direction)</c> (Client config/constants.ts).</summary>
    internal static string CategoryLabel(string? category, string? direction)
    {
        var c = string.IsNullOrEmpty(category) ? "other" : category;
        if (c == "refund") return "Vozvrat";
        if (c == "book" && direction == "expense") return "Kitob qaytarish";
        var (first, second) = direction == "expense" ? (ExpenseLabels, IncomeLabels) : (IncomeLabels, ExpenseLabels);
        return first.TryGetValue(c, out var l) ? l : second.TryGetValue(c, out var l2) ? l2 : c;
    }

    /// <summary>The panel's <c>paymentMethodLabel</c>.</summary>
    internal static string MethodLabel(string? method) => method switch
    {
        "cash" => "Naqd",
        "card" => "Karta",
        "bank" => "Bank orqali",
        _ => "Belgilanmagan",
    };
}
