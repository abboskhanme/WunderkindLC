using IntellectCRM.Application.Services.Kpi;
using IntellectCRM.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// OY BOSHI SURATI (<see cref="KpiSnapshotService.TakeAsync"/>).
///
/// <para>Bu yerda ikki KAFOLAT qulflanadi: (1) qayta chaqirish unikal indeksga urilmaydi
/// (1-kuni server restart bo'lsa <c>23505</c> bilan yiqilmasin), (2) O'TGAN oyning surati
/// HECH QACHON qayta yozilmaydi — modulning butun ma'nosi shu.</para>
/// </summary>
public class KpiSnapshotServiceTests
{
    private static string CurrentMonth => AppClock.Today.ToString("yyyy-MM");
    private static string PastMonth => AppClock.Today.AddMonths(-2).ToString("yyyy-MM");

    private static Student AddStudent(TestDb db, bool archived = false, bool active = true)
    {
        var s = new Student { FullName = "O'quvchi", IsArchived = archived };
        db.Context.Students.Add(s);
        db.Context.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id,
            GroupId = "g1",
            Status = active ? "active" : "trial",
            IsActive = true,
        });
        return s;
    }

    [Fact]
    public async Task Bosh_bazada_MARKAZ_qatori_yoziladi()
    {
        using var db = TestDb.Sqlite();

        var rows = await KpiSnapshotService.TakeAsync(db.Context, CurrentMonth);

        Assert.Equal(1, rows);
        var snap = await db.Context.KpiMonthSnapshots.SingleAsync();
        Assert.Equal("", snap.UserId);                 // markaz darajasi (spetsifikatsiya §17.1)
        Assert.Equal(KpiConst.Retention, snap.RoleCode);
        Assert.Contains("activeCount", snap.Json);
        Assert.Contains("debtTotal", snap.Json);
    }

    [Fact]
    public async Task Faol_oquvchilar_KpiActiveStudentRule_boyicha_sanaladi()
    {
        using var db = TestDb.Sqlite();
        AddStudent(db);                                   // faol
        AddStudent(db, active: false);                    // sinovda — FAOL EMAS
        AddStudent(db, archived: true);                   // arxivda — FAOL EMAS
        await db.Context.SaveChangesAsync();

        await KpiSnapshotService.TakeAsync(db.Context, CurrentMonth);

        var snap = await db.Context.KpiMonthSnapshots.SingleAsync(s => s.UserId == "");
        Assert.Contains("\"activeCount\":1", snap.Json);
    }

    [Fact]
    public async Task Ikki_marta_chaqirilsa_QATOR_TAKRORLANMAYDI()
    {
        using var db = TestDb.Sqlite();

        await KpiSnapshotService.TakeAsync(db.Context, CurrentMonth);
        var second = await KpiSnapshotService.TakeAsync(db.Context, CurrentMonth);

        Assert.Equal(1, second);   // yangilandi (bugun olingan), lekin qator BITTA
        Assert.Single(await db.Context.KpiMonthSnapshots.ToListAsync());
    }

    [Fact]
    public async Task OTGAN_oyning_surati_QAYTA_YOZILMAYDI()
    {
        using var db = TestDb.Sqlite();
        db.Context.KpiMonthSnapshots.Add(new KpiMonthSnapshot
        {
            Month = PastMonth,
            RoleCode = KpiConst.Retention,
            UserId = "",
            Json = "{\"activeCount\":320}",
        });
        await db.Context.SaveChangesAsync();

        var rows = await KpiSnapshotService.TakeAsync(db.Context, PastMonth);

        Assert.Equal(0, rows);
        var snap = await db.Context.KpiMonthSnapshots.SingleAsync();
        Assert.Equal("{\"activeCount\":320}", snap.Json);
    }

    [Fact]
    public async Task Joriy_oy_surati_KECHA_olingan_bolsa_tegilmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.KpiMonthSnapshots.Add(new KpiMonthSnapshot
        {
            Month = CurrentMonth,
            RoleCode = KpiConst.Retention,
            UserId = "",
            Json = "{\"activeCount\":99}",
            TakenAt = AppClock.Now.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss"),
        });
        await db.Context.SaveChangesAsync();

        var rows = await KpiSnapshotService.TakeAsync(db.Context, CurrentMonth);

        Assert.Equal(0, rows);
        Assert.Equal("{\"activeCount\":99}",
            (await db.Context.KpiMonthSnapshots.SingleAsync()).Json);
    }

    [Fact]
    public async Task Har_FAOL_profil_uchun_alohida_qator()
    {
        using var db = TestDb.Sqlite();
        db.Context.KpiProfiles.Add(new KpiProfile
        { UserId = "u1", RoleCode = KpiConst.Retention, StartMonth = CurrentMonth });
        db.Context.KpiProfiles.Add(new KpiProfile
        { UserId = "u2", RoleCode = KpiConst.Intake, StartMonth = CurrentMonth });
        db.Context.KpiProfiles.Add(new KpiProfile
        { UserId = "u3", RoleCode = KpiConst.Intake, StartMonth = CurrentMonth, IsActive = false });
        await db.Context.SaveChangesAsync();

        var rows = await KpiSnapshotService.TakeAsync(db.Context, CurrentMonth);

        Assert.Equal(3, rows);   // markaz + u1 + u2
        var all = await db.Context.KpiMonthSnapshots.ToListAsync();
        Assert.Equal(3, all.Count);
        Assert.DoesNotContain(all, s => s.UserId == "u3");

        // Chiquvchida markaz raqamlari bor, kiruvchida — oy boshida tiklab bo'lmaydigan raqam yo'q.
        Assert.Contains("activeCount", all.Single(s => s.UserId == "u1").Json);
        Assert.Equal("{}", all.Single(s => s.UserId == "u2").Json);
    }

    [Fact]
    public async Task Buzuq_oy_qiymatida_hech_narsa_yozilmaydi()
    {
        using var db = TestDb.Sqlite();

        Assert.Equal(0, await KpiSnapshotService.TakeAsync(db.Context, ""));
        Assert.Equal(0, await KpiSnapshotService.TakeAsync(db.Context, "2026-13"));
        Assert.Empty(await db.Context.KpiMonthSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Qarz_qoldigi_hisoblanadi_tolangani_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var s = AddStudent(db);
        await db.Context.SaveChangesAsync();

        var month = AppClock.Today.AddMonths(-1).ToString("yyyy-MM");
        db.Context.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id,
            Month = month,
            Amount = 500_000m,
            Discount = 100_000m,
            Date = $"{month}-01",
        });
        db.Context.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = s.Id,
            Direction = "income",
            Category = "tuition",
            Amount = 150_000m,
            Date = $"{month}-05",
        });
        await db.Context.SaveChangesAsync();

        await KpiSnapshotService.TakeAsync(db.Context, CurrentMonth);

        // 500 000 − 100 000 chegirma − 150 000 to'lov = 250 000
        var snap = await db.Context.KpiMonthSnapshots.SingleAsync(x => x.UserId == "");
        Assert.Contains("\"debtTotal\":250000", snap.Json);
    }
}
