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

    public static async Task<Outcome> LinkAsync(
        IAppDbContext db, string userId, string role, long chatId, string senderName, CancellationToken ct = default)
    {
        var students = new List<Student>();
        var teachers = new List<Teacher>();
        AppUser? staff = null;
        var phone = "";

        switch (role)
        {
            case Roles.Student:
                students = await db.Students.Where(s => s.UserId == userId && !s.IsArchived).ToListAsync(ct);
                break;
            case ParentRole:
                // Same rule as StudentPortalController.TargetAsync: the login is the parent's phone.
                var parent = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
                var norm = PhoneUtil.Normalize(parent?.Email);
                if (!string.IsNullOrEmpty(norm))
                {
                    phone = norm;
                    students = await db.Students
                        .Where(s => !s.IsArchived
                                    && (s.ParentPhone == norm || s.FatherPhone == norm || s.MotherPhone == norm))
                        .ToListAsync(ct);
                }
                break;
            case Roles.Teacher:
                teachers = await db.Teachers.Where(t => t.UserId == userId && !t.IsArchived).ToListAsync(ct);
                break;
            case Roles.Admin:
            case Roles.SuperAdmin:
            case Roles.Staff:
                staff = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
                break;
        }

        // Students: the shared registrar (relation = from the parent's phone, else "other" — admin-editable).
        var newStudentLinks = students.Count == 0
            ? new List<TelegramRegistration>()
            : await TelegramRegistrar.UpsertStudentLinksAsync(db, chatId, phone, senderName, students, ct);

        // Teacher and staff links share ONE non-student row per chat: the unique index (StudentId, ChatId) allows a
        // single StudentId = "" row, so a person with a teacher AND a staff account gets both ids on that row.
        var newTeachers = new List<Teacher>();
        foreach (var t in teachers)
        {
            var row = await NonStudentRowAsync(db, chatId, ct);
            if (row is null)
            {
                db.TelegramRegistrations.Add(NewNonStudentRow(chatId, senderName, teacherId: t.Id, userId: null));
                newTeachers.Add(t);
            }
            else if (row.TeacherId == t.Id) row.BlockedAt = null;
            else if (string.IsNullOrEmpty(row.TeacherId))
            {
                row.TeacherId = t.Id;
                row.BlockedAt = null;
                newTeachers.Add(t);
            }
            // else: the chat already belongs to ANOTHER teacher account — left as is (one teacher per chat).
        }

        var newStaff = false;
        if (staff is not null)
        {
            var row = await NonStudentRowAsync(db, chatId, ct);
            if (row is null)
            {
                db.TelegramRegistrations.Add(NewNonStudentRow(chatId, senderName, teacherId: null, userId: staff.Id));
                newStaff = true;
            }
            else if (row.UserId == staff.Id) row.BlockedAt = null;
            else if (string.IsNullOrEmpty(row.UserId))
            {
                row.UserId = staff.Id;
                row.BlockedAt = null;
                newStaff = true;
            }
            // else: the chat already belongs to ANOTHER staff account — left as is.
        }

        return new Outcome(students, newStudentLinks, teachers, newTeachers, staff, newStaff);
    }

    private static async Task<TelegramRegistration?> NonStudentRowAsync(IAppDbContext db, long chatId, CancellationToken ct) =>
        db.TelegramRegistrations.Local.FirstOrDefault(r => r.ChatId == chatId && r.StudentId == "")
        ?? await db.TelegramRegistrations.FirstOrDefaultAsync(r => r.ChatId == chatId && r.StudentId == "", ct);

    private static TelegramRegistration NewNonStudentRow(long chatId, string senderName, string? teacherId, string? userId) => new()
    {
        TeacherId = teacherId, UserId = userId, StudentId = "", ChatId = chatId, ParentName = senderName, Phone = "",
        CreatedAt = AppClock.Now,
    };

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
