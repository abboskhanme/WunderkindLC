using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'QUVCHINI ARXIVLASH YADROSI — uchta yo'l AYNAN shuni chaqiradi (nusxa yo'q):
/// «Arxivga ko'chirish» (<c>StudentsController.Archive</c>), «Guruhdan chiqarish» oynasidagi
/// «Boshqa guruhi qolmasa — arxivga ham o'tkazish» (<c>ClassesController.RemoveMember?archiveIfLast=true</c>)
/// va «Guruhsiz qolganlarni arxivlash» (<c>StudentsController.ArchiveLeavers</c>).
///
/// <para>Tirik a'zoliklar arxiv sanasida yopiladi (<see cref="StudentArchiving.LeaveAllGroupsAsync"/>),
/// login bloklanadi va ochiq sessiyalar tugatiladi, audit qatori yoziladi. <c>SaveChanges</c> QILMAYDI;
/// chaqiruvchi o'quvchi qulfini (<c>"student:{id}"</c>) olgan va balansni qulf ichida qayta o'qigan bo'lishi
/// shart (pul o'zgarishi mumkin).</para>
/// </summary>
internal static class StudentArchiveFlow
{
    /// <param name="reasonLabel">Arxiv sababi matni (katalogdan yoki erkin) — <c>Student.ArchiveReason</c>.</param>
    /// <param name="context">Audit matniga qo'shimcha kontekst (masalan "oxirgi guruhidan chiqarilganda");
    /// <c>null</c> — oddiy «Arxivga ko'chirish» matni o'zgarishsiz.</param>
    public static async Task<StudentArchiving.LeaveResult> ArchiveAsync(
        AppDbContext db, AuditService audit, Student student, string today,
        string? reasonId, string reasonLabel, string? context = null)
    {
        student.ArchiveReason = reasonLabel;

        // ARXIV = KETDI: a'zoliklar arxiv sanasida yopiladi (o'quvchi qulfi ostida — pul o'zgaradi).
        var leave = await StudentArchiving.LeaveAllGroupsAsync(db, audit, student, today,
            reasonId, student.ArchiveReason, "o'quvchi arxivlandi");

        student.IsArchived = true;
        student.ArchivedAt = today;

        // Login bloklash — PasswordHash bo'shaltiriladi (login imkonsiz bo'ladi).
        if (student.UserId is not null)
        {
            var user = await db.Users.FindAsync(student.UserId);
            if (user is not null)
            {
                user.BlockLogin();
                // Ochiq sessiyalar ham yopiladi: aks holda tiklangandan keyin arxivdan OLDINGI
                // refresh tokenlar yana ishlab ketardi (`auth-sessions.md`).
                await RefreshTokenService.RevokeAllForUserAsync(db, user.Id);
            }
        }

        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"O'quvchi arxivga ko'chirildi ({student.FullName})"
                + (string.IsNullOrWhiteSpace(context) ? "" : $" — {context}")
                + (leave.Closed > 0 ? $" — {leave.Closed} ta guruhdan chiqarildi: {string.Join(", ", leave.GroupNames)}" : "")
                + (string.IsNullOrWhiteSpace(student.ArchiveReason) ? "" : $": \"{student.ArchiveReason}\""),
            studentId: student.Id);
        return leave;
    }
}
