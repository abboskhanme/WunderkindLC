using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «SENTABR EDUTIZIMDA YOPILGAN» belgisi (2026-10-26): import a'zoliklarining aktivlashtirish oyiga 0 so'mlik QULFLI
/// hisob qatori (`edutizim-sep-*`). U to'lov oynasida oyni "to'langan" qiladi, lekin ushlab turish bonusida sanoqqa
/// KIRMAYDI — belgidan oldingi xulq (qator yo'q = neytral oy) o'zgarmasin.
/// </summary>
public class SeptemberMarkerTests
{
    private static string M(int offset) => AppClock.Today.AddMonths(offset).ToString("yyyy-MM");

    [Fact]
    public async Task Bonus__0_somlik_belgi_qatori_SANOQQA_KIRMAYDI_va_siklni_uzmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = new Subject { Name = "Ingliz tili" };
        ctx.Subjects.Add(course);
        var g = new Group { Name = "Kids", MonthlyFee = 225_000m, CourseId = course.Id, Days = new List<int> { 0, 2, 4 } };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Import O'quvchi", EnrollmentDate = "2025-01-01", RetentionBonus = true, RetentionBonusStartMonth = M(-1) };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = "2025-01-01", ActivatedAt = $"{M(-1)}-01", RecordedAt = $"{M(-1)}-18",
        });
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            Id = "edutizim-sep-test", StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = 0m, Discount = 0m,
            Date = $"{M(-1)}-01", Locked = true,
        });
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 225_000m, Date = $"{M(0)}-01" });
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(0)}-05", Direction = "income", Category = "tuition", Amount = 225_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var row = (await RetentionBonusService.BuildReportAsync(ctx, s.Id)).Rows.Single();

        Assert.Equal(RetentionBonusService.StateNoCharge, row.Months.Single(m => m.Month == M(-1)).State);
        Assert.Equal(RetentionBonusService.StatePaid, row.Months.Single(m => m.Month == M(0)).State);
        Assert.Equal(1, row.Counted);
    }
}
