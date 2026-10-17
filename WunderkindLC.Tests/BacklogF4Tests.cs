using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>SPEC-backlog-2026-10-17 F4: S1 (xodimlar faolligi) va A2 (reyting «Guruhlar kesimi»).</summary>
public class BacklogF4Tests
{
    // ---------- S1: xodimlar faolligi ----------

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("2026-10-17T09:00:00", null, "2026-10-17T09:00:00")]
    [InlineData(null, "2026-10-16T08:00:00", "2026-10-16T08:00:00")]
    [InlineData("2026-10-01T09:00:00", "2026-10-16T08:00:00", "2026-10-16T08:00:00")]
    public void Oxirgi_korinish_login_va_sessiyaning_kechrogi(string? login, string? refresh, string? expected) =>
        Assert.Equal(expected, StaffActivity.Later(login, refresh));

    [Fact]
    public async Task Faollik__sessiya_yangilanishi_login_dan_kech__amallar_sanaladi__oqituvchi_oz_id_si_bilan()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var staff = new AppUser { Id = "u1", FullName = "Kassir", Email = "k", Role = Roles.Staff, LastLoginAt = "2026-01-01T09:00:00" };
        var teacherUser = new AppUser { Id = "u2", FullName = "O'qituvchi", Email = "o", Role = Roles.Teacher };
        db.Users.AddRange(staff, teacherUser);
        db.Teachers.Add(new Teacher { Id = "t1", FullName = "O'qituvchi", UserId = "u2" });
        db.RefreshTokens.Add(new RefreshToken { UserId = "u1", TokenHash = "h", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        db.AuditLogs.AddRange(
            new AuditLog { EntityType = "Lead", EntityId = "l1", Action = "update", Timestamp = now, ActorId = "u1" },
            new AuditLog { EntityType = "Lead", EntityId = "l1", Action = "update", Timestamp = "2020-01-01T00:00:00", ActorId = "u1" });
        db.LeadEvents.AddRange(
            new LeadEvent { LeadId = "l1", Type = "stage", CreatedAt = now, ActorUserId = "u1" },
            new LeadEvent { LeadId = "l1", Type = "note", CreatedAt = now, ActorUserId = "u1" },   // o'sha lid — bir marta
            new LeadEvent { LeadId = "l2", Type = "note", CreatedAt = now, ActorUserId = "u1" });
        await db.SaveChangesAsync();

        var rows = await StaffActivity.BuildAsync(db, 30);

        var s = Assert.Single(rows, r => r.Kind == "staff");
        Assert.Equal("u1", s.Id);
        Assert.True(string.CompareOrdinal(s.LastSeenAt, s.LastLoginAt) > 0);   // sessiya yangilanishi kechroq
        Assert.Equal((1, 2), (s.AuditActions, s.LeadsHandled));              // eski audit davrdan tashqarida
        var te = Assert.Single(rows, r => r.Kind == "teacher");
        Assert.Equal("t1", te.Id);                                            // ro'yxatdagi o'qituvchi id'si
        Assert.Null(te.LastSeenAt);
    }

    // ---------- A2: reyting «Guruhlar kesimi» ----------

    private static RatingResult Result()
    {
        var groups = new List<GroupScoreRow>
        {
            new("s1", "g1", 80, 1, 10, 100, 88, 1),
            new("s2", "g1", 60, 1, 5, 50, 56, 2),
            new("s3", "g2", null, 0, 0, null, null, null),   // bali yo'q — o'rtachaga kirmaydi
            new("s3", "g3", 90, 1, 8, 100, 94, 1),
        };
        var centre = new List<CentreScoreRow>
        {
            new("s1", 88, 1, 80, 1, 10), new("s2", 56, 3, 60, 1, 5), new("s3", 94, 2, 90, 1, 8),
        };
        var info = new Dictionary<string, RatingGroupInfo>
        {
            ["g1"] = new("g1", "A1", "Ingliz", "Ali", false),
            ["g2"] = new("g2", "B1", "Rus", "Vali", false),
            ["g3"] = new("g3", "C1", "Ingliz", "Soli", false),
        };
        return new RatingResult(
            new RatingPeriod("month", "2026-10", "Oktabr 2026", "2026-10-01", "2026-10-17"), 60, groups, centre,
            new Dictionary<string, string> { ["s1"] = "Ali", ["s2"] = "Bek", ["s3"] = "Sora" }, info);
    }

    [Fact]
    public void Guruh_kesimi__ballilar_ortachasi__balsiz_guruh_tushadi__davomat_guruh_boyicha()
    {
        var attendance = new Dictionary<(string, string), MembershipAttendance.Stat>
        {
            [("s1", "g1")] = new(8, 1, 0, 1),
            [("s2", "g1")] = new(5, 0, 1, 4),
        };
        var (groups, _) = RatingSections.Build(Result(), attendance, new Dictionary<string, int?>());

        Assert.Equal(new[] { "g3", "g1" }, groups.Select(g => g.Key));   // bal bo'yicha kamayish; g2 (balsiz) yo'q
        var g1 = groups.Single(g => g.Key == "g1");
        Assert.Equal((2, 72.0, 70.0, 75.0), (g1.Students, g1.AvgScore!.Value, g1.AvgTest!.Value, g1.AvgBall!.Value));
        Assert.Equal(20, g1.AttendanceMarks);
        Assert.Equal(70, g1.AttendancePct);                               // (9 + 5) / 20
        Assert.Null(groups.Single(g => g.Key == "g3").AttendancePct);
    }

    [Fact]
    public void Sinf_kesimi__oquvchi_bir_marta__sinfsizlar_oxirida()
    {
        var grades = new Dictionary<string, int?> { ["s1"] = 7, ["s2"] = 7, ["s3"] = null };
        var (_, rows) = RatingSections.Build(Result(), new Dictionary<(string, string), MembershipAttendance.Stat>(), grades);

        Assert.Equal(new[] { "7", RatingSections.NoGradeKey }, rows.Select(r => r.Key));
        var seventh = rows[0];
        Assert.Equal(("7-sinf", 2, 72.0), (seventh.Label, seventh.Students, seventh.AvgScore!.Value));
        Assert.Equal(("Sinf ko'rsatilmagan", 1), (rows[1].Label, rows[1].Students));
        Assert.Equal("Maktabni bitirgan", RatingSections.GradeLabel(SchoolGrades.Graduated));
    }
}
