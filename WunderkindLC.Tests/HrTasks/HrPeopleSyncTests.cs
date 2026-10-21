using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.HrTasks;

/// <summary>
/// Every LC employee is an HR employee (LMS client, 2026-09-28, ported 1:1): teachers and staff land in the HR
/// list so they can get tasks and be placed in the subordination tree. Each test owns its SQLite database (the
/// sync reads the WHOLE users table). Ported from WunderkindLMS SchoolLms.Tests/HrTasks/HrPeopleSyncTests.cs —
/// the delete endpoints are checked through <see cref="HrTasksService.ReleaseUnusedEmployeeAsync"/>, the call
/// both delete actions make (wiring: <see cref="HrTasksRbacTests"/>).
/// </summary>
public class HrPeopleSyncTests
{
    [Fact]
    public async Task Faol_oqituvchi_va_xodimlar_royxatga_tushadi_qolganlar_yoq()
    {
        using var kit = new HrTasksKit();
        var db = kit.Db;
        var teacher = User("O'qituvchi Bir", Roles.Teacher);
        var leaver = User("Ketgan O'qituvchi", Roles.Teacher);
        var nurse = User("Hamshira Opa", Roles.Staff, position: "Hamshira", phone: "+998 90 111 22 33");
        var twin = User("Yana Shu Raqam", Roles.Staff, phone: "+998901112233");
        var system = User("Tizim administratori", Roles.SuperAdmin);
        var owner = User("Platforma egasi", Roles.PlatformOwner);
        var pupil = User("O'quvchi Bola", Roles.Student);
        var already = User("Eski Xodim", Roles.Staff);
        var phoneless = User("Telefonsiz Eski", Roles.Staff, position: "Buxgalter", phone: "+998 93 444 55 66");
        db.Users.AddRange(teacher, leaver, nurse, twin, system, owner, pupil, already, phoneless);
        db.Teachers.AddRange(
            new Teacher { FullName = teacher.FullName, UserId = teacher.Id, Phone = "" },
            new Teacher { FullName = leaver.FullName, UserId = leaver.Id, Phone = "+998907770000", IsArchived = true });
        // Imported from the old HR: a short name and its own phone / no phone at all.
        db.HrEmployees.AddRange(
            new HrEmployee { UserId = already.Id, FullName = "Eski", Phone = "+998905550000" },
            new HrEmployee { UserId = phoneless.Id, FullName = phoneless.FullName });
        await db.SaveChangesAsync();

        var sync = await kit.Service().SyncLmsPeopleAsync();

        Assert.Equal(new HrPeopleSyncResult(Added: 3, Updated: 2), sync);
        await using var read = kit.NewContext();
        var rows = await read.HrEmployees.AsNoTracking().ToListAsync();
        var positions = await read.HrPositions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);
        HrEmployee Row(AppUser u) => rows.Single(e => e.UserId == u.Id);

        // A teacher without a phone is still added — the phone is only a label.
        Assert.Equal(HrTasksService.TeacherPositionName, positions[Row(teacher).PositionId!]);
        Assert.Null(Row(teacher).Phone);
        Assert.Equal(HrRoles.Employee, Row(teacher).HrRole);
        Assert.True(Row(teacher).IsActive);
        Assert.Equal(HrBranches.Center, Row(teacher).Branch); // LC: the learning centre

        // Staff keep their "Lavozim"; the second holder of a number gets none, not an error.
        Assert.Equal("Hamshira", positions[Row(nurse).PositionId!]);
        Assert.Equal("+998901112233", Row(nurse).Phone);
        Assert.Null(Row(twin).Phone);
        Assert.Equal(HrTasksService.DefaultPositionName, positions[Row(twin).PositionId!]);

        Assert.DoesNotContain(rows, e => e.UserId == leaver.Id || e.UserId == system.Id || e.UserId == owner.Id || e.UserId == pupil.Id);
        // People are managed in Boshqaruv: the account name wins, an HR phone is kept, a missing one filled.
        Assert.Equal("Eski Xodim", Row(already).FullName);
        Assert.Equal("+998905550000", Row(already).Phone);
        Assert.Equal("+998934445566", Row(phoneless).Phone);
        // Positions are Boshqaruv's too: its "Lavozim" wins, an empty one leaves HR's alone.
        Assert.Equal("Buxgalter", positions[Row(phoneless).PositionId!]);
        Assert.Null(Row(already).PositionId);
        Assert.Empty(await read.HrEmployeeManagers.ToListAsync()); // nobody is placed in the tree

        Assert.Equal(new HrPeopleSyncResult(0, 0), await kit.Service().SyncLmsPeopleAsync()); // idempotent
    }

    [Fact]
    public async Task Tarixsiz_HR_qatori_ochirishga_xalaqit_bermaydi()
    {
        using var kit = new HrTasksKit();
        var (_, userId) = await SeedTeacherWithHrRowAsync(kit);

        // what TeachersController.Delete / StaffController.Delete do before removing the account
        await using var db = kit.NewContext();
        Assert.True(await HrTasksService.ReleaseUnusedEmployeeAsync(db, userId));
        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == userId));
        await db.SaveChangesAsync();

        await using var read = kit.NewContext();
        Assert.False(await read.HrEmployees.AnyAsync(e => e.UserId == userId));
        Assert.False(await read.Users.AnyAsync(u => u.Id == userId));
    }

    [Fact]
    public async Task Topshirigi_yoki_qoidasi_bor_HR_xodimi_ochirilmaydi_409()
    {
        using var kit = new HrTasksKit();
        var (_, withTask) = await SeedTeacherWithHrRowAsync(kit);
        var employee = await kit.Db.HrEmployees.SingleAsync(e => e.UserId == withTask);
        await kit.TaskAsync(employee.Id, DateTimeOffset.UtcNow);

        var (_, withRule) = await SeedTeacherWithHrRowAsync(kit);
        var ruled = await kit.Db.HrEmployees.SingleAsync(e => e.UserId == withRule);
        kit.Db.HrTaskTemplates.Add(new HrTaskTemplate
        {
            Title = "Qoida", TargetType = HrTargetTypes.Employee, TargetEmployeeId = ruled.Id,
            Recurrence = HrRecurrences.Daily, Weekdays = [1],
        });
        await kit.Db.SaveChangesAsync();

        await using var db = kit.NewContext();
        Assert.False(await HrTasksService.ReleaseUnusedEmployeeAsync(db, withTask));
        Assert.False(await HrTasksService.ReleaseUnusedEmployeeAsync(db, withRule));
        Assert.True(await HrTasksService.ReleaseUnusedEmployeeAsync(db, Guid.NewGuid().ToString())); // no HR row — nothing blocks
        // the RESTRICT foreign key is the last line: removing the account anyway fails loudly
        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == withTask));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Telefonsiz_mavjud_foydalanuvchidan_xodim_ochiladi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var teacherUser = User("Telefonsiz O'qituvchi", Roles.Teacher);
        kit.Db.Users.Add(teacherUser);
        await kit.Db.SaveChangesAsync();

        var s = kit.Service();
        var panel = await s.PanelAsync(HrTasksKit.Actor(admin.User));
        var created = await s.CreateEmployeeAsync(panel, Input(userId: teacherUser.Id), NoNewUser);
        Assert.Equal("", created.Phone);
        Assert.Null(created.Credentials);

        // A brand-new user still needs a phone — it is the login.
        var ex = await Assert.ThrowsAsync<HrTasksException>(() => kit.Service().CreateEmployeeAsync(panel, Input(fullName: "Telefonsiz Yangi"), NoNewUser));
        Assert.Equal(422, ex.StatusCode);
    }

    // ---------------------------------------------------------------- yordamchilar

    internal static HrEmployeeCreateInput Input(
        string? fullName = null, string? phone = null, string? userId = null, string? hrRole = null, string? branch = null) =>
        new(fullName, phone, null, null, null, branch, null, userId, hrRole);

    internal static (AppUser User, string Password) NoNewUser(string fullName, string login, string displayPhone) =>
        throw new InvalidOperationException("a user must not be created here");

    private static async Task<(string TeacherId, string UserId)> SeedTeacherWithHrRowAsync(HrTasksKit kit)
    {
        var user = User("O'qituvchi " + Guid.NewGuid().ToString("N")[..4], Roles.Teacher);
        var teacher = new Teacher { FullName = user.FullName, UserId = user.Id };
        kit.Db.Users.Add(user);
        kit.Db.Teachers.Add(teacher);
        kit.Db.HrEmployees.Add(new HrEmployee { UserId = user.Id, FullName = user.FullName });
        await kit.Db.SaveChangesAsync();
        return (teacher.Id, user.Id);
    }

    private static AppUser User(string name, string role, string position = "", string phone = "") => new()
    {
        FullName = name,
        Role = role,
        Email = $"sync-{Guid.NewGuid():N}",
        PasswordHash = "x",
        Position = position,
        Phone = phone,
    };
}
