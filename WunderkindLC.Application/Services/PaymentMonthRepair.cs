using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// NOTO'G'RI OYGA YOZILGAN TO'LOVLARNI TUZATISH (2026-10-23, bir martalik vosita — Moliya → «To'lov oyini tuzatish»).
///
/// <para><b>Muammo:</b> <see cref="PaymentMonthSplit"/> gacha to'lov oynasi eng eski qarzdor oyni oldindan tanlardi va
/// kassir 225 000 kiritib oyni qoldirsa, butun to'lov o'sha (masalan sentabr — 20 000 qoldiqli) oyga teglanardi.
/// Prod'da oktabrda ~100 ta to'lov shunday sentabrga yozilgan: per-guruh hisobda oktabr "to'lanmagan", o'qituvchi
/// maoshida «Hozir chiqadigan» oktabr pulini ko'rmaydi — holbuki o'quvchi balansi 0.</para>
///
/// <para><b>Qoida</b> (<see cref="Plan"/>, sof, <c>PaymentMonthRepairTests</c>): har (o'quvchi, guruh) bo'yicha,
/// oyma-oy: SOF hisob (summa − chegirma) va shu oyga TEGLANGAN pul (to'lov − vozvrat). Ortiqcha to'langan oyning
/// ORTIG'I keyingi (joriy oygacha) KAM to'langan oylarga, eng eskisidan boshlab ko'chiriladi. To'lov ortiqchaga
/// to'liq sig'sa — faqat <c>Month</c> tegi almashadi; sig'masa — bo'linadi (oila: <see cref="PaymentSplits"/>).
/// Orqaga, kelajakka va <see cref="FloorMonth"/> dan eski oylarga hech narsa ko'chmaydi. Vozvrati bor to'lov
/// ko'chirilmaydi (vozvrat bog'lanishi buzilmasin) — nomzodda izoh bilan aytiladi.</para>
///
/// <para>⚠️ Pul, balans, kassa va hisoblar O'ZGARMAYDI — faqat to'lov "qaysi oy uchun" tegi. Avto-xabar yo'q.
/// Avval KO'RIB chiqiladi (<see cref="FindAsync"/> hech narsa yozmaydi), yozish — tanlanganlar uchun
/// <see cref="ApplyOneAsync"/>, har o'quvchi o'z QULFI ostida, reja qulf ichida QAYTA quriladi.</para>
/// </summary>
public static class PaymentMonthRepair
{
    /// <summary>Rejaga kiradigan bitta to'lov qatori.</summary>
    public sealed record Payment(string Id, string Month, decimal Amount, string Date, DateTime CreatedAt, bool HasRefund);

    /// <summary>Bitta ko'chirish: <paramref name="Amount"/> so'm <paramref name="TxId"/> dan <paramref name="ToMonth"/> ga.</summary>
    public sealed record Move(string TxId, string FromMonth, string ToMonth, decimal Amount);

    /// <summary>Reja: ko'chirishlar + ko'chirib bo'lmagan ortiqcha (vozvrati bor to'lov) haqida izohlar.</summary>
    public sealed record RepairPlan(List<Move> Moves, List<string> Notes);

    public sealed record MoveView(string FromMonth, string ToMonth, decimal Amount);

    public sealed record Candidate(
        string Key, string StudentId, string StudentName, string GroupId, string GroupName, string TeacherName,
        List<MoveView> Moves, decimal Total, List<string> Notes);

    /// <summary>Tanlov kaliti — (o'quvchi, guruh).</summary>
    public static string Key(string studentId, string groupId) => $"{studentId}|{groupId}";

    /// <summary>
    /// Eng eski tegiladigan oy. Maosh foizi HISOBLANGAN bazada bo'lgan oylarda (<c>CenterMeta.SalaryChargedBaseFrom</c>
    /// dan) tegni ko'chirish maoshning o'zini o'zgartirmaydi — faqat «Hozir chiqadigan» ko'rsatkichini; undan oldingi
    /// (yig'ilgan pul bazali, maoshi allaqachon berilgan) oylar tegilmaydi. Shu bilan birga ko'pi bilan oxirgi 3 oy.
    /// Sozlama bo'sh bo'lsa — faqat o'tgan va joriy oy.
    /// </summary>
    public static string FloorMonth(string currentMonth, string? chargedBaseFrom)
    {
        var cur = DateOnly.ParseExact(currentMonth + "-01", "yyyy-MM-dd");
        if (!PaymentFields.IsMonth(chargedBaseFrom)) return cur.AddMonths(-1).ToString("yyyy-MM");
        var window = cur.AddMonths(-2).ToString("yyyy-MM");
        return string.CompareOrdinal(chargedBaseFrom, window) > 0 ? chargedBaseFrom! : window;
    }

    /// <summary>
    /// Bitta (o'quvchi, guruh) uchun reja — SOF funksiya.
    /// </summary>
    /// <param name="netCharge">Oy → sof hisob (summa − chegirma, ≥ 0).</param>
    /// <param name="payments">Shu guruhga teglangan o'quv to'lovlari (bekor qilinmaganlar).</param>
    /// <param name="refunds">Oy → shu guruh/oyga teglangan vozvratlar yig'indisi.</param>
    public static RepairPlan Plan(
        IReadOnlyDictionary<string, decimal> netCharge, IReadOnlyList<Payment> payments,
        IReadOnlyDictionary<string, decimal> refunds, string floorMonth, string currentMonth)
    {
        bool InWindow(string m) => string.CompareOrdinal(m, floorMonth) >= 0 && string.CompareOrdinal(m, currentMonth) <= 0;
        var months = netCharge.Keys.Concat(payments.Select(p => p.Month)).Concat(refunds.Keys)
            .Where(InWindow).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToList();

        var paid = months.ToDictionary(m => m,
            m => payments.Where(p => p.Month == m).Sum(p => p.Amount) - refunds.GetValueOrDefault(m, 0m));
        // Kam to'langan oylar: qancha yetmaydi (to'lov manfiy bo'lsa — butun hisob).
        var need = months.ToDictionary(m => m, m => Math.Max(0m, netCharge.GetValueOrDefault(m, 0m) - Math.Max(0m, paid[m])));

        var moves = new List<Move>();
        var notes = new List<string>();
        foreach (var from in months)
        {
            var surplus = paid[from] - netCharge.GetValueOrDefault(from, 0m);
            if (surplus <= 0m) continue;
            if (!months.Any(d => string.CompareOrdinal(d, from) > 0 && need[d] > 0m)) continue;

            // Ortiqcha — eng OXIRGI to'lovdan boshlab (oxirgi to'lov yangi oy uchun qilingan bo'lish ehtimoli yuqori;
            // `TuitionService.CarryGroupAdvanceAsync` bilan bir xil naqsh).
            var movable = payments.Where(p => p.Month == from && !p.HasRefund)
                .OrderByDescending(p => p.Date, StringComparer.Ordinal).ThenByDescending(p => p.CreatedAt)
                .Select(p => (p.Id, Left: p.Amount)).ToList();
            var available = Math.Min(surplus, movable.Sum(p => p.Left));
            if (available < surplus)
                notes.Add($"{from}: {AuditService.Money(surplus - available)} so'm ortiqcha vozvrati bor to'lovda — qo'lda tuzatiladi");

            var i = 0;
            foreach (var to in months.Where(d => string.CompareOrdinal(d, from) > 0))
            {
                while (need[to] > 0m && available > 0m && i < movable.Count)
                {
                    var take = Math.Min(Math.Min(need[to], available), movable[i].Left);
                    if (take > 0m)
                    {
                        moves.Add(new Move(movable[i].Id, from, to, take));
                        need[to] -= take;
                        available -= take;
                        movable[i] = (movable[i].Id, movable[i].Left - take);
                    }
                    if (movable[i].Left <= 0m) i++;
                }
                if (available <= 0m || i >= movable.Count) break;
            }
        }
        return new RepairPlan(moves, notes);
    }

    private sealed record PairData(Dictionary<string, decimal> Net, List<Payment> Payments, Dictionary<string, decimal> Refunds);

    /// <summary>Oynadagi ma'lumot (o'quvchi, guruh) bo'yicha — <paramref name="studentId"/> berilsa faqat o'sha o'quvchi.</summary>
    private static async Task<(Dictionary<(string S, string G), PairData> Pairs, string Floor, string Current)> LoadAsync(
        IAppDbContext db, string? studentId, CancellationToken ct)
    {
        var current = AppClock.Today.ToString("yyyy-MM");
        var chargedFrom = await db.CenterMeta.AsNoTracking().Select(m => m.SalaryChargedBaseFrom).FirstOrDefaultAsync(ct);
        var floor = FloorMonth(current, chargedFrom);
        var months = TuitionService.MonthRange(floor, current).ToList();

        var txQ = db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId != null && t.GroupId != null && t.Month != null && months.Contains(t.Month)
                        && ((t.Direction == "income" && t.Category == "tuition")
                            || (t.Direction == "expense" && t.Category == "refund")));
        if (studentId is not null) txQ = txQ.Where(t => t.StudentId == studentId);
        var txs = await txQ
            .Select(t => new { t.Id, t.StudentId, t.GroupId, t.Month, t.Amount, t.Date, t.CreatedAt, t.Direction })
            .ToListAsync(ct);
        var incomeIds = txs.Where(t => t.Direction == "income").Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        // Vozvrati bor to'lovlar (vozvrat qaysi oyga yozilganidan qat'i nazar).
        var refunded = (await db.FinanceTransactions.AsNoTracking()
                .Where(r => r.Direction == "expense" && r.Category == "refund" && r.RefundOfId != null)
                .Select(r => r.RefundOfId!).ToListAsync(ct))
            .Where(incomeIds.Contains).ToHashSet(StringComparer.Ordinal);

        var studentIds = txs.Where(t => t.Direction == "income").Select(t => t.StudentId!).ToHashSet(StringComparer.Ordinal);
        var chargeQ = db.MonthlyCharges.AsNoTracking()
            .Where(c => c.GroupId != null && months.Contains(c.Month));
        if (studentId is not null) chargeQ = chargeQ.Where(c => c.StudentId == studentId);
        var charges = (await chargeQ.Select(c => new { c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount }).ToListAsync(ct))
            .Where(c => studentIds.Contains(c.StudentId)).ToList();

        var pairs = new Dictionary<(string S, string G), PairData>();
        PairData Pair(string s, string g)
        {
            if (!pairs.TryGetValue((s, g), out var p)) pairs[(s, g)] = p = new PairData(new(), new(), new());
            return p;
        }
        foreach (var t in txs)
        {
            var p = Pair(t.StudentId!, t.GroupId!);
            if (t.Direction == "income")
                p.Payments.Add(new Payment(t.Id, t.Month!, t.Amount, t.Date, t.CreatedAt, refunded.Contains(t.Id)));
            else
                p.Refunds[t.Month!] = p.Refunds.GetValueOrDefault(t.Month!, 0m) + t.Amount;
        }
        foreach (var c in charges)
        {
            if (!pairs.TryGetValue((c.StudentId, c.GroupId!), out var p)) continue; // to'lovi yo'q juftlik — ko'chiradigan narsa yo'q
            p.Net[c.Month] = p.Net.GetValueOrDefault(c.Month, 0m) + Math.Max(0m, c.Amount - c.Discount);
        }
        return (pairs, floor, current);
    }

    /// <summary>Noto'g'ri oyga yozilgan to'lovlar — HECH NARSA YOZMAYDI.</summary>
    public static async Task<List<Candidate>> FindAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var (pairs, floor, current) = await LoadAsync(db, null, ct);
        var plans = pairs
            .Select(kv => (kv.Key.S, kv.Key.G, Plan: Plan(kv.Value.Net, kv.Value.Payments, kv.Value.Refunds, floor, current)))
            .Where(x => x.Plan.Moves.Count > 0)
            .ToList();
        if (plans.Count == 0) return [];

        var sIds = plans.Select(p => p.S).Distinct().ToList();
        var gIds = plans.Select(p => p.G).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => sIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, StringComparer.Ordinal, ct);
        var groups = await db.Classes.AsNoTracking().Where(c => gIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.TeacherId }).ToDictionaryAsync(c => c.Id, StringComparer.Ordinal, ct);
        var tIds = groups.Values.Where(g => !string.IsNullOrEmpty(g.TeacherId)).Select(g => g.TeacherId!).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => tIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FullName, StringComparer.Ordinal, ct);

        return plans
            .Select(p =>
            {
                var g = groups.GetValueOrDefault(p.G);
                var moves = p.Plan.Moves
                    .GroupBy(m => (m.FromMonth, m.ToMonth))
                    .Select(x => new MoveView(x.Key.FromMonth, x.Key.ToMonth, x.Sum(m => m.Amount)))
                    .ToList();
                return new Candidate(Key(p.S, p.G), p.S, students.GetValueOrDefault(p.S, "?"), p.G, g?.Name ?? "?",
                    g?.TeacherId is { Length: > 0 } tid ? teachers.GetValueOrDefault(tid, "") : "",
                    moves, moves.Sum(m => m.Amount), p.Plan.Notes);
            })
            .OrderBy(c => c.TeacherName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.StudentName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Bitta (o'quvchi, guruh) juftligining to'lov oylarini tuzatadi. ⚠️ Chaqiruvchi o'quvchi QULFI ichida
    /// (<c>PaymentIdempotency.LockAsync("student:{id}")</c>) va keyin SaveChanges qiladi. Reja bazadan QAYTA quriladi.
    /// Qaytaradi: ko'chirilgan jami summa (0 — tuzatadigan narsa qolmagan).
    /// </summary>
    public static async Task<decimal> ApplyOneAsync(IAppDbContext db, AuditService audit, string studentId,
                                                    string groupId, CancellationToken ct = default)
    {
        var (pairs, floor, current) = await LoadAsync(db, studentId, ct);
        if (!pairs.TryGetValue((studentId, groupId), out var data)) return 0m;
        var plan = Plan(data.Net, data.Payments, data.Refunds, floor, current);
        if (plan.Moves.Count == 0) return 0m;

        var ids = plan.Moves.Select(m => m.TxId).Distinct().ToList();
        var txs = await db.FinanceTransactions.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var group = await db.Classes.AsNoTracking().Where(c => c.Id == groupId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? groupId;
        var student = await db.Students.AsNoTracking().Where(s => s.Id == studentId).Select(s => s.FullName).FirstOrDefaultAsync(ct) ?? studentId;

        var moved = 0m;
        foreach (var byTx in plan.Moves.GroupBy(m => m.TxId))
        {
            if (!txs.TryGetValue(byTx.Key, out var tx)) continue;
            var list = byTx.ToList();
            var mark = $"[oy tuzatildi: {tx.Month} → {string.Join(", ", list.Select(m => m.ToMonth).Distinct())}]";
            var rest = tx.Amount - list.Sum(m => m.Amount);
            // To'lov TO'LIQ ko'chsa — birinchi ko'chirishni qatorning O'ZI oladi (faqat oy tegi almashadi),
            // qolganlari (va qisman holatda hammasi) — oilaga bog'langan yangi qatorlar.
            var asChildren = list;
            if (rest <= 0m)
            {
                tx.Month = list[0].ToMonth;
                tx.Amount = list[0].Amount;
                asChildren = list.Skip(1).ToList();
            }
            else tx.Amount = rest;
            tx.Note = $"{tx.Note} {mark}".Trim();
            foreach (var m in asChildren)
                db.FinanceTransactions.Add(new FinanceTransaction
                {
                    Date = tx.Date,
                    Direction = tx.Direction,
                    Category = tx.Category,
                    TypeId = tx.TypeId,
                    Amount = m.Amount,
                    StudentId = tx.StudentId,
                    GroupId = tx.GroupId,
                    Month = m.ToMonth,
                    Method = tx.Method,
                    Comment = tx.Comment,
                    ReceiptNo = tx.ReceiptNo,
                    PaidTime = tx.PaidTime,
                    CardLast4 = tx.CardLast4,
                    CreatedBy = tx.CreatedBy,
                    CreatedById = tx.CreatedById,
                    CashboxId = tx.CashboxId, // pul o'sha kassada — faqat oy tegi
                    SplitFromId = tx.SplitFromId ?? tx.Id,
                    Note = $"O'quvchi to'lovi ({m.ToMonth}) [{group}] — {student} [{m.FromMonth} oyiga yozilgan to'lovdan ko'chirildi]",
                });
            moved += list.Sum(m => m.Amount);
        }

        var summary = string.Join("; ", plan.Moves
            .GroupBy(m => (m.FromMonth, m.ToMonth))
            .Select(x => $"{AuditService.Money(x.Sum(m => m.Amount))} so'm {x.Key.FromMonth} → {x.Key.ToMonth}"));
        audit.Record(AuditService.EntityFinanceTransaction, plan.Moves[0].TxId, "update",
            $"To'lov oyi tuzatildi ({group}): {summary} — {student}. Pul va balans o'zgarmadi, faqat «qaysi oy uchun» tegi",
            studentId: studentId);
        return moved;
    }
}
