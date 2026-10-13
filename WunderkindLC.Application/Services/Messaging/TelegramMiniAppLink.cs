using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// Links a Telegram chat to the account its owner just logged in with inside the «📱 Panel» Mini App
/// (2026-10-12, owner: "one bot for everyone, no phone sharing in the bot").
///
/// <para>The bot no longer asks for a phone number. After a password/OTP login the Mini App posts its signed
/// <c>initData</c>; the server checks Telegram's HMAC (<see cref="TelegramInitData"/>) and links that chat to the
/// CALLER's own account(s) only: the students of a student account, the children of a parent account (relation
/// detected from the parent's login phone), the teacher, or the staff user. Proof of identity = the login session
/// + Telegram's signature of the chat id — never a value the client chooses.</para>
///
/// <para>Idempotent: an existing link is refreshed (block mark cleared), never duplicated. Does NOT save.</para>
/// </summary>
public static class TelegramMiniAppLink
{
    /// <summary>Parent accounts log in with a phone (no constant in <see cref="Roles"/>; the student portal uses the
    /// same literal).</summary>
    public const string ParentRole = "parent";

    public sealed record Outcome(
        List<Student> Students, List<TelegramRegistration> NewStudentLinks,
        List<Teacher> Teachers, List<Teacher> NewTeachers,
        AppUser? Staff, bool NewStaffLink)
    {
        public bool AnyNew => NewStudentLinks.Count > 0 || NewTeachers.Count > 0 || NewStaffLink;
    }

    /// <summary>Whose chat this account links: its own students / a parent's children (+ the parent's phone for the
    /// relation) / the teacher / the staff user. Unknown roles (platform owner) link nothing.</summary>
    private sealed record Targets(List<Student> Students, string Phone, List<Teacher> Teachers, AppUser? Staff);

    private static async Task<Targets> TargetsAsync(IAppDbContext db, string userId, string role, CancellationToken ct)
    {
        switch (role)
        {
            case Roles.Student:
                return new(await db.Students.Where(s => s.UserId == userId && !s.IsArchived).ToListAsync(ct), "", new(), null);
            case ParentRole:
                // Same rule as StudentPortalController.TargetAsync: the login is the parent's phone.
                var parent = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
                var norm = PhoneUtil.Normalize(parent?.Email);
                if (string.IsNullOrEmpty(norm)) return new(new(), "", new(), null);
                var children = await db.Students
                    .Where(s => !s.IsArchived && (s.ParentPhone == norm || s.FatherPhone == norm || s.MotherPhone == norm))
                    .ToListAsync(ct);
                return new(children, norm, new(), null);
            case Roles.Teacher:
                return new(new(), "", await db.Teachers.Where(t => t.UserId == userId && !t.IsArchived).ToListAsync(ct), null);
            case Roles.Admin:
            case Roles.SuperAdmin:
            case Roles.Staff:
                return new(new(), "", new(), await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct));
            default:
                return new(new(), "", new(), null);
        }
    }

    /// <summary>Does the account already receive bot messages somewhere (a non-blocked link)? Drives the Mini App
    /// «Bot xabarlarini yoqish» banner — the chat itself is unknown there (keyboard launches carry no initData).</summary>
    public static async Task<bool> IsLinkedAsync(IAppDbContext db, string userId, string role, CancellationToken ct = default)
    {
        var t = await TargetsAsync(db, userId, role, ct);
        var sIds = t.Students.Select(s => s.Id).ToList();
        var tIds = t.Teachers.Select(x => x.Id).ToList();
        var uId = t.Staff?.Id;
        if (sIds.Count == 0 && tIds.Count == 0 && uId is null) return true;   // nothing to link — no banner
        return await db.TelegramRegistrations.AnyAsync(r => r.BlockedAt == null
            && ((r.StudentId != "" && sIds.Contains(r.StudentId))
                || (r.TeacherId != null && tIds.Contains(r.TeacherId))
                || (uId != null && r.UserId == uId)), ct);
    }

    public static async Task<Outcome> LinkAsync(
        IAppDbContext db, string userId, string role, long chatId, string senderName, CancellationToken ct = default)
    {
        var (students, phone, teachers, staff) = await TargetsAsync(db, userId, role, ct);

        // Students: the shared registrar (relation = from the parent's phone, else "other" — admin-editable).
        var newStudentLinks = students.Count == 0
            ? new List<TelegramRegistration>()
            : await TelegramRegistrar.UpsertStudentLinksAsync(db, chatId, phone, senderName, students, ct);

        // Teacher and staff links share ONE non-student row per chat: the unique index (StudentId, ChatId) allows a
        // single StudentId = "" row, so a person with a teacher AND a staff account gets both ids on that row.
        var newTeachers = new List<Teacher>();
        foreach (var t in teachers)
            if (await AttachNonStudentAsync(db, chatId, senderName, "", teacherId: t.Id, userId: null, ct)) newTeachers.Add(t);
        var newStaff = staff is not null
            && await AttachNonStudentAsync(db, chatId, senderName, "", teacherId: null, userId: staff.Id, ct);

        return new Outcome(students, newStudentLinks, teachers, newTeachers, staff, newStaff);
    }

    private static async Task<TelegramRegistration?> NonStudentRowAsync(IAppDbContext db, long chatId, CancellationToken ct) =>
        db.TelegramRegistrations.Local.FirstOrDefault(r => r.ChatId == chatId && r.StudentId == "")
        ?? await db.TelegramRegistrations.FirstOrDefaultAsync(r => r.ChatId == chatId && r.StudentId == "", ct);

    /// <summary>
    /// Links a teacher OR a staff account to the chat's single non-student row (unique index (StudentId, ChatId) allows
    /// one StudentId = "" row per chat): reuses it when that slot is free, refreshes it when already linked, and never
    /// throws on the index. Returns true for a NEW link. Does NOT save. Also used by the bot's phone registration.
    /// </summary>
    public static async Task<bool> AttachNonStudentAsync(
        IAppDbContext db, long chatId, string senderName, string phoneDigits, string? teacherId, string? userId,
        CancellationToken ct = default)
    {
        var row = await NonStudentRowAsync(db, chatId, ct);
        if (row is null)
        {
            db.TelegramRegistrations.Add(new TelegramRegistration
            {
                TeacherId = teacherId, UserId = userId, StudentId = "", ChatId = chatId, ParentName = senderName,
                Phone = phoneDigits, CreatedAt = AppClock.Now,
            });
            return true;
        }
        if (teacherId is not null)
        {
            if (row.TeacherId == teacherId) { row.BlockedAt = null; return false; }
            if (!string.IsNullOrEmpty(row.TeacherId)) return false;   // the chat belongs to ANOTHER teacher account
            row.TeacherId = teacherId;
        }
        else
        {
            if (row.UserId == userId) { row.BlockedAt = null; return false; }
            if (!string.IsNullOrEmpty(row.UserId)) return false;      // the chat belongs to ANOTHER staff account
            row.UserId = userId;
        }
        row.BlockedAt = null;
        return true;
    }

    /// <summary>The bot's confirmation after a NEW link: who the chat now receives messages for.</summary>
    public static string ConfirmationText(Outcome o)
    {
        var lines = new List<string>();
        lines.AddRange(o.NewStudentLinks
            .Select(r => o.Students.FirstOrDefault(s => s.Id == r.StudentId))
            .Where(s => s is not null)
            .Select(s => $"• {s!.FullName} (o'quvchi)"));
        lines.AddRange(o.NewTeachers.Select(t => $"• {t.FullName} (o'qituvchi)"));
        if (o.NewStaffLink && o.Staff is not null) lines.Add($"• {o.Staff.FullName} (xodim)");
        return "✅ Telegram bot ulandi:\n" + string.Join("\n", lines)
               + "\n\nEndi markaz xabarlari shu chatga keladi.";
    }
}
