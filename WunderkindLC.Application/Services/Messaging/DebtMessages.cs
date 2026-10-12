using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>One debt reminder: WHICH course (group), how much and the ready system text (plain).</summary>
/// <param name="Group">Token context — {guruh}, {oqituvchi}, {dars_*} come from this group (null on the legacy path).</param>
/// <param name="Label">Course name — in the text and the {kurs} token.</param>
/// <param name="Amount">Debt of THIS course (not the total) — the {qarzdorlik} token.</param>
public sealed record DebtMessage(Group? Group, string Label, decimal Amount, string Title, string Body);

/// <summary>
/// DEBT REMINDER TEXT — one message PER COURSE (moved out of <c>PaymentReminderService</c> unchanged, CRM upgrade P2.5,
/// so the manual «Qarzdorlar» campaign and the automatic reminder build the SAME messages).
///
/// <para>⚠️ The student gets one message per course (owner's explicit requirement: N courses = N messages). Debt per
/// course via <see cref="StudentGroupLedger"/> up to the CURRENT month; finished memberships count too (an old group's
/// remaining debt comes with that course's name); trial memberships bill nothing. Legacy students without any
/// membership get ONE message from <see cref="Student.Balance"/>; a debtor whose debt belongs to no group gets one
/// message without a course name.</para>
/// </summary>
public static class DebtMessages
{
    public static async Task<List<DebtMessage>> BuildForStudentAsync(
        IAppDbContext db, Student s, IReadOnlyDictionary<string, Group> groupByName, CancellationToken ct = default)
    {
        var items = new List<DebtMessage>();

        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(m => m.StudentId == s.Id)
            .ToListAsync(ct);

        if (memberships.Count == 0)
        {
            var total = s.Balance < 0 ? -s.Balance : 0m;
            if (total <= 0) return items;
            var label = s.ClassName ?? "";
            items.Add(Compose(s, groupByName.GetValueOrDefault(label), label, total));
            return items;
        }

        var billable = memberships.Where(m => m.Status != "trial").ToList();
        var groupIds = billable.Select(m => m.GroupId).Distinct().ToList();
        var groups = (await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync(ct))
            .ToDictionary(g => g.Id);

        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);

        foreach (var group in billable.GroupBy(m => m.GroupId))
        {
            var m = MembershipLifecycle.PrimaryMembership(group)
                    ?? group.OrderByDescending(x => x.LeftAt ?? "", StringComparer.Ordinal)
                        .ThenByDescending(x => x.JoinedAt ?? "", StringComparer.Ordinal)
                        .First();
            if (!groups.TryGetValue(m.GroupId, out var g)) continue;
            var ledger = await StudentGroupLedger.BuildAsync(db, s, g, m, book);
            // ⚠️ Only up to the current month (the ledger shows 3 future months for advance payments).
            var current = TuitionService.CurrentMonth();
            var owed = ledger.Months.Where(x => string.CompareOrdinal(x.Month, current) <= 0).Sum(x => x.Remaining);
            if (owed <= 0) continue;
            items.Add(Compose(s, g, ledger.CourseName, owed));
        }

        if (items.Count == 0 && s.Balance < 0)
            items.Add(Compose(s, null, "", -s.Balance));

        return items;
    }

    /// <summary>System text of one course (deliberately short — one message per course; no "Jami" line).</summary>
    public static DebtMessage Compose(Student s, Group? group, string label, decimal amount)
    {
        var head = string.IsNullOrWhiteSpace(label)
            ? $"{s.FullName} bo'yicha qarzdorlik: {Money(amount)}."
            : $"{s.FullName} — {label} bo'yicha qarzdorlik: {Money(amount)}.";
        return new DebtMessage(group, label, amount, "To'lov eslatmasi", $"{head}\nIltimos, to'lovni amalga oshiring.");
    }

    /// <summary>So'm: 1 700 000 so'm.</summary>
    public static string Money(decimal v)
    {
        var nfi = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };
        return v.ToString("#,0", nfi) + " so'm";
    }

    /// <summary>Token values of one course message: {qarzdorlik} (this course), {kurs}, {guruh} (this group).</summary>
    public static Dictionary<string, string> Extra(DebtMessage item) => new()
    {
        ["{qarzdorlik}"] = Money(item.Amount),
        ["{kurs}"] = item.Label,
        ["{guruh}"] = item.Group?.Name ?? item.Label,
    };

    /// <summary>Students' groups by name (legacy fallback context) — loaded once per run.</summary>
    public static async Task<Dictionary<string, Group>> GroupsByNameAsync(IAppDbContext db, CancellationToken ct = default) =>
        (await db.Classes.AsNoTracking().ToListAsync(ct)).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());
}
