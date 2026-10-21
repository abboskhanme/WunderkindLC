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
     *  TOPSHIRIQLAR — 2026-10-21 dan QO'LDA (manba — Kanban «Topshiriqlar» — olib tashlandi)
     * ========================================================================================= */

    [Fact]
    public async Task Topshiriq_bandlari_AVTO_tekshirilmaydi_qolda_belgilanadi()
    {
        using var db = TestDb.Sqlite();
        var keys = new[] { "auto:tasks.overdue_zero", "auto:tasks.due_done", "auto:leads.without_task_zero" };
        var map = await ChecklistAutoCheck.RunAsync(db.Context, User, Day, keys);
        // Qo'llanmagan kalit javobga tushmaydi — band oddiy qo'lda belgilanadigan bandga aylanadi
        // (yolg'on "o'tdi"/"yiqildi" bonusga ta'sir qilmasin).
        Assert.Empty(map);
        foreach (var k in keys) Assert.DoesNotContain(k, ChecklistAutoCheck.Supported);
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
