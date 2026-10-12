using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-12 — O'qituvchilar hisobotining yuqori kartochkalari har O'QUVCHINI bir marta sanaydi. Ilgari qatorlar
/// qo'shilardi: «Faol o'quvchilar» = aktivlashgan A'ZOLIKLAR oqimi (ketgan/muzlatilganlar ham) va ikki guruhdagi
/// o'quvchi ikki marta — prod'da 730, haqiqatda 681 aktiv + 20 sinov.
/// </summary>
public class TeacherReportTotalsTests
{
    [Fact]
    public async Task Kartochkalar_takrorsiz_hozirgi_aktiv_va_sinov_guruh_almashtirgan_ketgan_emas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t1 = new Teacher { FullName = "Birinchi" };
        var t2 = new Teacher { FullName = "Ikkinchi" };
        ctx.Teachers.AddRange(t1, t2);
        var g1 = new Group { Name = "G1", TeacherId = t1.Id };
        var g2 = new Group { Name = "G2", TeacherId = t1.Id };
        var g3 = new Group { Name = "G3", TeacherId = t2.Id };
        ctx.Classes.AddRange(g1, g2, g3);

        Student S(string name, bool archived = false)
        {
            var s = new Student { FullName = name, IsArchived = archived };
            ctx.Students.Add(s);
            return s;
        }
        void M(Student s, Group g, string status, bool live = true, string? leftAt = null) =>
            ctx.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id, GroupId = g.Id, Status = status, IsActive = live, LeftAt = leftAt,
                JoinedAt = "2026-09-01", ActivatedAt = status == "trial" ? "" : "2026-09-02",
            });

        var a = S("A"); M(a, g1, "active"); M(a, g3, "active");           // two teachers — once
        var b = S("B"); M(b, g1, "active"); M(b, g2, "trial");            // active wins over trial
        var c = S("C"); M(c, g2, "trial");                                // trial only
        var d = S("D"); M(d, g1, "active", live: false, leftAt: "2026-09-20"); M(d, g3, "active");  // switched — not left
        var e = S("E"); M(e, g2, "active", live: false, leftAt: "2026-09-25");                     // really left
        var f = S("F", archived: true); M(f, g1, "active");               // archived — not studying
        var g = S("G"); M(g, g3, "frozen");                               // frozen — neither, not left
        ctx.SaveChanges();

        var report = await TeacherActivityReport.BuildOverviewAsync(ctx);

        var totals = Assert.IsType<WunderkindLC.Application.Dtos.TeacherReportTotalsDto>(report.Totals);
        Assert.Equal(3, totals.ActiveNow);   // A, B, D
        Assert.Equal(1, totals.TrialNow);    // C
        Assert.Equal(1, totals.Left);        // E
        Assert.Equal(7, totals.Came);        // every student once
        // The per-teacher rows still count memberships per teacher — their sum is NOT the number of students.
        Assert.Equal(4, report.Rows.Sum(r => r.Remaining));
    }
}
