using WunderkindLC.Application.Services.Kpi;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CHEKLISTNI AVTOMATIK BELGILASH (<see cref="ChecklistAutoCheck"/>) — har qo'llangan kalit
/// uchun O'TADIGAN va YIQILADIGAN holat.
///
/// <para>⚠️ Bu tekshiruvlar samaradorlik foiziga, u esa BONUSGA tushadi — ya'ni noto'g'ri
/// <c>false</c> xodimning pulini ushlab qoladi. Shuning uchun "hukm chiqarish uchun ma'lumot
/// yo'q" holati alohida testlar bilan qulflangan: bunday holatda band HAR DOIM o'tadi.</para>
/// </summary>
public class ChecklistAutoCheckTests
{
    private const string User = "u1";
    private const string Day = "2026-05-20";       // chorshanba
    private const string Prev = "2026-05-19";      // seshanba

    private static async Task<bool?> RunOne(TestDb db, string key, string user = User, string date = Day)
    {
        var map = await ChecklistAutoCheck.RunAsync(db.Context, user, date, new[] { key });
        return map.TryGetValue(key, out var v) ? v : null;
    }

    /* =========================================================================================
     *  UMUMIY
     * ========================================================================================= */

    [Fact]
    public async Task Qollanmagan_kalit_JIMGINA_tashlanadi()
    {
        using var db = TestDb.Sqlite();
        var map = await ChecklistAutoCheck.RunAsync(db.Context, User, Day,
            new[] { "auto:kpi.digest_sent", "auto:xato.kalit" });
        Assert.Empty(map);
    }

    [Fact]
    public async Task Buzuq_sana_BOSH_natija_beradi()
    {
        using var db = TestDb.Sqlite();
        var map = await ChecklistAutoCheck.RunAsync(db.Context, User, "2026-13-99",
            new[] { ChecklistAutoCheck.JournalYesterdayFilled });
        Assert.Empty(map);
    }

    /* =========================================================================================
     *  TOPSHIRIQLAR — 2026-10-21 dan HR topshiriqlaridan (Kanban «Topshiriqlar» olib tashlandi)
     * ========================================================================================= */

    private static readonly TimeZoneInfo Tz = HrValues.Zone("Asia/Tashkent");

    /// <summary>Xodim (AppUser + HR qatori) — HR topshiriqlari egasi.</summary>
    private static async Task<HrEmployee> EmployeeAsync(TestDb db, string userId)
    {
        db.Context.Users.Add(new AppUser { Id = userId, FullName = userId, Role = Roles.Staff, Email = userId, PasswordHash = "x" });
        var e = new HrEmployee { UserId = userId, FullName = userId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Context.HrEmployees.Add(e);
        await db.Context.SaveChangesAsync();
        return e;
    }

    /// <summary>Topshiriq: <paramref name="day"/> kuni soat <paramref name="hour"/> da (Toshkent vaqti) muddati.</summary>
    private static async Task HrTaskAsync(TestDb db, HrEmployee e, string day, int hour, string status = HrTaskStatuses.Open)
    {
        var date = DateOnly.Parse(day);
        db.Context.HrTasks.Add(new HrTask
        {
            EmployeeId = e.Id, Title = "T", PeriodStart = date, Status = status,
            DueAt = HrValues.At(date, new TimeOnly(hour, 0), Tz),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task HR_qatori_yoq_xodimda_ikkala_topshiriq_bandi_OTADI()
    {
        using var db = TestDb.Sqlite();
        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksDueDone));
    }

    [Fact]
    public async Task Kechagi_ochiq_topshiriq_KECHIKKAN_bugungisi_esa_yoq()
    {
        using var db = TestDb.Sqlite();
        var e = await EmployeeAsync(db, User);
        await HrTaskAsync(db, e, Prev, 18);

        Assert.False(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksDueDone));
    }

    [Fact]
    public async Task Bugungi_ochiq_topshiriq_KECHIKKAN_EMAS_lekin_bugungisi_BAJARILMAGAN()
    {
        using var db = TestDb.Sqlite();
        var e = await EmployeeAsync(db, User);
        await HrTaskAsync(db, e, Day, 0);        // kun boshining o'zi — "bugun"
        await HrTaskAsync(db, e, Day, 23);

        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
        Assert.False(await RunOne(db, ChecklistAutoCheck.TasksDueDone));
    }

    [Fact]
    public async Task Qaytarilgan_topshiriq_ham_OCHIQ_sanaladi()
    {
        using var db = TestDb.Sqlite();
        var e = await EmployeeAsync(db, User);
        await HrTaskAsync(db, e, Prev, 10, HrTaskStatuses.Returned);
        Assert.False(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
    }

    [Fact]
    public async Task Bajarilgan_bekor_qilingan_va_ertangi_topshiriqlar_HISOBGA_KIRMAYDI()
    {
        using var db = TestDb.Sqlite();
        var e = await EmployeeAsync(db, User);
        await HrTaskAsync(db, e, Prev, 10, HrTaskStatuses.Done);
        await HrTaskAsync(db, e, Prev, 11, HrTaskStatuses.Cancelled);
        await HrTaskAsync(db, e, Prev, 12, HrTaskStatuses.Missed);
        await HrTaskAsync(db, e, Day, 9, HrTaskStatuses.Done);
        await HrTaskAsync(db, e, "2026-05-21", 0);   // ertaga — hali muddati kelmagan

        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksDueDone));
    }

    [Fact]
    public async Task Boshqa_xodimning_topshiriqlari_TASIR_QILMAYDI()
    {
        using var db = TestDb.Sqlite();
        await EmployeeAsync(db, User);
        var other = await EmployeeAsync(db, "u2");
        await HrTaskAsync(db, other, Prev, 10);
        await HrTaskAsync(db, other, Day, 10);

        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksDueDone));
    }

    [Fact]
    public async Task Vazifasiz_sdelka_bandi_QOLDA_belgilanadi()
    {
        using var db = TestDb.Sqlite();
        var map = await ChecklistAutoCheck.RunAsync(db.Context, User, Day, new[] { "auto:leads.without_task_zero" });
        // Lid ↔ topshiriq bog'lanishi Kanban'da edi; qo'llanmagan kalit javobga tushmaydi (yolg'on hukm bonusga ta'sir qilmasin).
        Assert.Empty(map);
        Assert.DoesNotContain("auto:leads.without_task_zero", ChecklistAutoCheck.Supported);
    }

    /* =========================================================================================
     *  QO'NG'IROQLAR — 2026-10 dan QO'LDA (manba — Local Call agent-ilovasi — olib tashlandi)
     * ========================================================================================= */

    [Fact]
    public async Task Qongiroq_bandlari_AVTO_tekshirilmaydi_qolda_belgilanadi()
    {
        using var db = TestDb.Sqlite();
        var map = await ChecklistAutoCheck.RunAsync(db.Context, User, Day,
            new[] { "auto:calls.missed_returned", "auto:calls.answer_speed" });
        // Qo'llanmagan kalit javobga tushmaydi — band oddiy qo'lda belgilanadigan bandga aylanadi
        // (yolg'on "o'tdi"/"yiqildi" bonusga ta'sir qilmasin).
        Assert.Empty(map);
        Assert.DoesNotContain("auto:calls.missed_returned", ChecklistAutoCheck.Supported);
        Assert.DoesNotContain("auto:calls.answer_speed", ChecklistAutoCheck.Supported);
    }

    /* =========================================================================================
     *  JURNAL
     * ========================================================================================= */

    /// <summary>Kecha (Prev) darsi bo'ladigan guruh yaratadi.</summary>
    private static Group GroupWithLessonYesterday(TestDb db, string name = "A1")
    {
        var yesterday = DateOnly.Parse(Prev);
        var dow = ((int)yesterday.DayOfWeek + 6) % 7;   // 0 = Dushanba
        var g = new Group { Name = name, Days = new List<int> { dow } };
        db.Context.Classes.Add(g);
        return g;
    }

    [Fact]
    public async Task Kechagi_darslar_jurnali_toldirilgan_bolsa_OTADI()
    {
        using var db = TestDb.Sqlite();
        var g = GroupWithLessonYesterday(db);
        await db.Context.SaveChangesAsync();
        db.Context.JournalEntries.Add(new JournalEntry { ClassId = g.Id, StudentId = "s1", Date = Prev, Present = true });
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.JournalYesterdayFilled));
    }

    [Fact]
    public async Task Kechagi_jurnal_toldirilmagan_bolsa_YIQILADI()
    {
        using var db = TestDb.Sqlite();
        GroupWithLessonYesterday(db);
        await db.Context.SaveChangesAsync();

        Assert.False(await RunOne(db, ChecklistAutoCheck.JournalYesterdayFilled));
    }

    [Fact]
    public async Task Arxivlangan_guruh_jurnali_TALAB_QILINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = GroupWithLessonYesterday(db);
        g.IsArchived = true;
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.JournalYesterdayFilled));
    }

    [Fact]
    public async Task Kursi_TUGAGAN_guruh_jurnali_TALAB_QILINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = GroupWithLessonYesterday(db);
        g.EndDate = "2026-01-01";
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.JournalYesterdayFilled));
    }

    [Fact]
    public async Task Kecha_dars_bolmagan_kun_OTADI()
    {
        using var db = TestDb.Sqlite();
        var yesterday = DateOnly.Parse(Prev);
        var otherDow = (((int)yesterday.DayOfWeek + 6) % 7 + 1) % 7;
        db.Context.Classes.Add(new Group { Name = "B1", Days = new List<int> { otherDow } });
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.JournalYesterdayFilled));
    }
}
