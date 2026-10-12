using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// MULTI-ACCOUNT REGISTRATION (SPEC P2.4 «Registration», <c>TelegramRegistrationTests</c>).
/// <para>A shared contact is matched against ANY of the student's four numbers (<c>Phone</c>, <c>ParentPhone</c>,
/// <c>FatherPhone</c>, <c>MotherPhone</c>; <c>PhoneUtil.Key</c>, ≥ 7 digits) — so father, mother and the student can
/// each connect their own Telegram (2–3 accounts per student). <c>CenterMeta.TelegramPhoneMatchField</c> is ignored
/// from now on. One registration per matched student (siblings share a parent's chat); the relation is detected
/// (<see cref="TelegramRelations.Detect"/>) and never overwrites an admin's edit.</para>
/// </summary>
public static class TelegramRegistrar
{
    /// <summary>Does <paramref name="phoneKey"/> (<see cref="PhoneUtil.Key"/>) equal one of the student's numbers?</summary>
    public static bool Matches(Student s, string phoneKey)
    {
        if (phoneKey.Length < 7) return false;
        foreach (var p in new[] { s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone })
        {
            var k = PhoneUtil.Key(p);
            if (k.Length >= 7 && k == phoneKey) return true;
        }
        return false;
    }

    public static async Task<List<Student>> MatchStudentsAsync(IAppDbContext db, string? phone, CancellationToken ct = default)
    {
        var key = PhoneUtil.Key(phone);
        if (key.Length < 7) return new List<Student>();
        return (await db.Students.ToListAsync(ct)).Where(s => Matches(s, key)).ToList();
    }

    /// <summary>
    /// Creates the missing registrations of <paramref name="chatId"/> for the matched students (relation detected) and
    /// refreshes existing ones: relation set only when empty, block mark cleared (the user is talking to the bot).
    /// Does NOT save. Returns the NEWLY created registrations (the «01» thank-you goes only to those).
    /// </summary>
    public static async Task<List<TelegramRegistration>> UpsertStudentLinksAsync(
        IAppDbContext db, long chatId, string phone, string senderName, IEnumerable<Student> students,
        CancellationToken ct = default)
    {
        var digits = PhoneUtil.DigitsOnly(phone);
        var created = new List<TelegramRegistration>();
        var list = students.ToList();
        var ids = list.Select(s => s.Id).ToList();
        var existing = await db.TelegramRegistrations
            .Where(r => r.ChatId == chatId && ids.Contains(r.StudentId)).ToListAsync(ct);
        foreach (var s in list)
        {
            var reg = existing.FirstOrDefault(r => r.StudentId == s.Id)
                      ?? db.TelegramRegistrations.Local.FirstOrDefault(r => r.StudentId == s.Id && r.ChatId == chatId);
            if (reg is null)
            {
                reg = new TelegramRegistration
                {
                    StudentId = s.Id, ChatId = chatId, ParentName = senderName, Phone = digits, CreatedAt = AppClock.Now,
                    Relation = TelegramRelations.Detect(s, phone),
                };
                db.TelegramRegistrations.Add(reg);
                created.Add(reg);
                continue;
            }
            if (string.IsNullOrEmpty(reg.Relation)) reg.Relation = TelegramRelations.Detect(s, reg.Phone.Length > 0 ? reg.Phone : phone);
            if (reg.BlockedAt is not null) reg.BlockedAt = null;
        }
        return created;
    }

    /// <summary>Startup backfill (SeedMarker <c>tg-relation-backfill-2026-10</c>): relation of every student registration
    /// that has none. Saves. Returns rows updated.</summary>
    public static async Task<int> BackfillRelationsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var regs = await db.TelegramRegistrations.Where(r => r.StudentId != "" && r.Relation == "").ToListAsync(ct);
        if (regs.Count == 0) return 0;
        var ids = regs.Select(r => r.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var n = 0;
        foreach (var r in regs)
        {
            if (!students.TryGetValue(r.StudentId, out var s)) continue;
            r.Relation = TelegramRelations.Detect(s, r.Phone);
            n++;
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }
}
