using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'QITUVCHI — o'z guruhi o'quvchilarini TAHRIRLASH (guruh sahifasi → «O'quvchilar» tabi,
/// asosan Telegram Mini App ichida ishlatiladi).
///
/// <para><b>Faqat uch maydon:</b> ism-familiya, maktab sinfi, o'quvchining telefoni
/// (<see cref="TeacherStudentEdit"/>). Guruhga qo'shish, chiqarish, muzlatish, ko'chirish —
/// o'qituvchi portalida ATAYIN YO'Q (bu admin/xodim ishi, pul hisobiga tegadi).</para>
///
/// <para><b>Darvoza (ikkalasi ham SHART):</b></para>
/// <list type="bullet">
/// <item>o'qituvchida <c>journal</c> bo'limi ruxsati bor (guruh sahifasi shu ruxsat ostida);</item>
/// <item>guruh shu o'qituvchiniki (<see cref="TeacherGroupAccess.OwnedBy"/>, ko'rinadigan guruh).
///   ⚠️ O'RINBOSAR tahrirlay OLMAYDI — u guruhga vaqtincha keladi, o'quvchi ma'lumoti esa doimiy;</item>
/// <item>o'quvchi shu guruhning TIRIK (<c>IsActive</c>) a'zosi va arxivlanmagan — aks holda o'qituvchi
///   id yozib begona o'quvchini tahrirlay olardi.</item>
/// </list>
/// </summary>
public partial class TeacherPortalController
{
    /// <summary>Guruh shu o'qituvchiniki va unga ko'rinadimi (arxiv/blok emas).</summary>
    private async Task<bool> OwnsGroupAsync(string teacherId, string classId)
    {
        var g = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId);
        return g is not null && TeacherGroupAccess.Visible(g) && TeacherGroupAccess.OwnedBy(g, teacherId);
    }

    private static TeacherGroupStudentDto ToGroupStudentDto(Student s, string status) =>
        new(s.Id, s.FullName, s.Phone ?? "", s.SchoolGradeNow, status);

    /// <summary>O'z guruhining faol a'zolari (ism bo'yicha).</summary>
    [HttpGet("groups/{classId}/students")]
    public async Task<ActionResult<List<TeacherGroupStudentDto>>> GroupStudents(string classId)
    {
        var me = await Me();
        if (me is null || !me.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        if (!await OwnsGroupAsync(me.Id, classId)) return Forbid();

        var rows = await (
                from sg in db.StudentGroups.AsNoTracking()
                where sg.GroupId == classId && sg.IsActive
                join s in db.Students.AsNoTracking() on sg.StudentId equals s.Id
                where !s.IsArchived
                select new { Student = s, sg.Status })
            .ToListAsync();

        // `SchoolGradeNow` — hisoblanadigan maydon, shuning uchun xotirada quriladi.
        return rows
            .OrderBy(r => r.Student.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => ToGroupStudentDto(r.Student, r.Status ?? ""))
            .ToList();
    }

    /// <summary>Ism-familiya, sinf va telefonni o'zgartiradi. Javob — yangilangan qator.</summary>
    [HttpPut("groups/{classId}/students/{studentId}")]
    public async Task<ActionResult<TeacherGroupStudentDto>> UpdateGroupStudent(
        string classId, string studentId, TeacherStudentEditRequest req)
    {
        var me = await Me();
        if (me is null || !me.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        if (!await OwnsGroupAsync(me.Id, classId)) return Forbid();

        var membership = await db.StudentGroups.AsNoTracking()
            .FirstOrDefaultAsync(sg => sg.GroupId == classId && sg.StudentId == studentId && sg.IsActive);
        var student = membership is null ? null : await db.Students.FindAsync(studentId);
        if (student is null || student.IsArchived)
            return NotFound(new { message = "O'quvchi bu guruhda topilmadi" });

        var input = new TeacherStudentEdit.Input(req.FullName, req.SchoolGrade, req.Phone);
        var error = TeacherStudentEdit.Validate(input, student.Phone);
        if (error is not null) return BadRequest(new { message = error });

        var before = AuditService.StudentProfileSnapshot(student);
        var (oldGrade, oldGradeYear) = (student.SchoolGrade, student.SchoolGradeYear);
        var phonesBefore = TelegramLinks.PhonesSignature(student);
        TeacherStudentEdit.Apply(student, input, AppClock.Today);
        // Raqam o'zgarsa ESKI raqam bilan ulangan Telegram chat uziladi (admin tahriri bilan bir xil — `TelegramLinks`).
        if (TelegramLinks.PhonesChanged(phonesBefore, student))
            await TelegramLinks.PruneStaleAsync(db, audit, student, "raqam o'zgardi — o'qituvchi tahriri");

        // Login akkauntidagi ism ham yangilanadi (admin tahriri bilan bir xil — `StudentsController.Update`).
        var user = student.UserId is null ? null : await db.Users.FindAsync(student.UserId);
        if (user is not null) user.FullName = student.FullName;

        // "Kim o'zgartirdi" — o'qituvchining o'zi (ActorName tokendan). Hech narsa o'zgarmasa yozilmaydi.
        var after = AuditService.StudentProfileSnapshot(student);
        if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
            audit.Record(AuditService.EntityStudent, student.Id, "update",
                $"O'quvchi ma'lumoti tahrirlandi — o'qituvchi tomonidan ({student.FullName})",
                before: before, after: after, studentId: student.Id);
        // Sinf o'zgarsa — oylik admin tahriridagi kabi DARHOL moslanadi (yagona yo'l: `GradeRepricing`).
        await GradeRepricing.ApplyAsync(db, audit, student, oldGrade, oldGradeYear);

        await db.SaveChangesAsync();
        return ToGroupStudentDto(student, membership!.Status ?? "");
    }
}
