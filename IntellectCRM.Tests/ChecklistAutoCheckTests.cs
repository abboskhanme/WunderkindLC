using IntellectCRM.Application.Services.Kpi;
using IntellectCRM.Domain;
using Xunit;

namespace IntellectCRM.Tests;

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

    private static DateTime At(string date, int hour, int minute = 0, int second = 0) =>
        DateTime.Parse($"{date}T00:00:00").AddHours(hour).AddMinutes(minute).AddSeconds(second);

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
            new[] { ChecklistAutoCheck.TasksOverdueZero });
        Assert.Empty(map);
    }

    /* =========================================================================================
     *  TOPSHIRIQLAR
     * ========================================================================================= */

    private static WorkTask Task(string due, DateTime? completed = null, bool archived = false,
        string assignee = User) => new()
    {
        AssigneeId = assignee,
        Title = "Ish",
        DueDate = due,
        CompletedAt = completed,
        IsArchived = archived,
    };

    [Fact]
    public async Task Kechikkan_topshiriq_yoq_bolsa_OTADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.WorkTasks.Add(Task(Prev, completed: At(Prev, 18)));   // kechikkan, LEKIN yopilgan
        db.Context.WorkTasks.Add(Task(Day));                             // bugungi — hali kechikmagan
        db.Context.WorkTasks.Add(Task(null!));                           // muddatsiz
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
    }

    [Fact]
    public async Task Ochiq_kechikkan_topshiriq_bolsa_YIQILADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.WorkTasks.Add(Task(Prev));
        await db.Context.SaveChangesAsync();

        Assert.False(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
    }

    [Fact]
    public async Task Kechikkan_topshiriq_BOSHQA_xodimniki_bolsa_OTADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.WorkTasks.Add(Task(Prev, assignee: "boshqa"));
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
    }

    [Fact]
    public async Task Arxivlangan_kechikkan_topshiriq_HISOBGA_KIRMAYDI()
    {
        using var db = TestDb.Sqlite();
        db.Context.WorkTasks.Add(Task(Prev, archived: true));
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksOverdueZero));
    }

    [Fact]
    public async Task Bugungi_topshiriqlar_yopilgan_bolsa_OTADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.WorkTasks.Add(Task(Day, completed: At(Day, 15)));
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksDueDone));
    }

    [Fact]
    public async Task Bugungi_topshiriq_ochiq_qolsa_YIQILADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.WorkTasks.Add(Task(Day));
        await db.Context.SaveChangesAsync();

        Assert.False(await RunOne(db, ChecklistAutoCheck.TasksDueDone));
    }

    [Fact]
    public async Task Bugunga_topshiriq_umuman_bolmasa_OTADI()
    {
        using var db = TestDb.Sqlite();
        Assert.True(await RunOne(db, ChecklistAutoCheck.TasksDueDone));
    }

    /* =========================================================================================
     *  QO'NG'IROQLAR
     * ========================================================================================= */

    private static Call Missed(string phone, DateTime at) => new()
    {
        PhoneNumber = phone,
        Direction = "inbound",
        Status = "no_answer",
        StartedAt = at,
    };

    private static Call Out(string phone, DateTime at) => new()
    {
        PhoneNumber = phone,
        Direction = "outbound",
        Status = "completed",
        StartedAt = at,
    };

    [Fact]
    public async Task Kechagi_javobsiz_qongiroq_qaytarilgan_bolsa_OTADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.Calls.Add(Missed("+998 90 123 45 67", At(Prev, 22)));
        // Format boshqacha — solishtiruv oxirgi 9 raqam bo'yicha.
        db.Context.Calls.Add(Out("998901234567", At(Day, 9, 30)));
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.CallsMissedReturned));
    }

    [Fact]
    public async Task Qaytarilmagan_javobsiz_qongiroq_bolsa_YIQILADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.Calls.Add(Missed("901234567", At(Prev, 22)));
        db.Context.Calls.Add(Out("909999999", At(Day, 9, 30)));   // boshqa raqam
        await db.Context.SaveChangesAsync();

        Assert.False(await RunOne(db, ChecklistAutoCheck.CallsMissedReturned));
    }

    [Fact]
    public async Task Javobsiz_qongiroqdan_OLDINGI_chiquvchi_hisobga_KIRMAYDI()
    {
        using var db = TestDb.Sqlite();
        db.Context.Calls.Add(Out("901234567", At(Prev, 10)));
        db.Context.Calls.Add(Missed("901234567", At(Prev, 22)));
        await db.Context.SaveChangesAsync();

        Assert.False(await RunOne(db, ChecklistAutoCheck.CallsMissedReturned));
    }

    [Fact]
    public async Task Kecha_javobsiz_qongiroq_bolmasa_OTADI()
    {
        using var db = TestDb.Sqlite();
        Assert.True(await RunOne(db, ChecklistAutoCheck.CallsMissedReturned));
    }

    [Fact]
    public async Task Tez_javob_ulushi_90_foizdan_yuqori_bolsa_OTADI()
    {
        using var db = TestDb.Sqlite();
        for (var i = 0; i < 9; i++)
        {
            var start = At(Day, 10, i);
            db.Context.Calls.Add(new Call
            {
                PhoneNumber = $"90000000{i}",
                Direction = "inbound",
                Status = "completed",
                OperatorUserId = User,
                StartedAt = start,
                AnsweredAt = start.AddSeconds(5),
            });
        }
        // Javobsiz qo'ng'iroq maxrajga KIRMAYDI (boshqa bandning mavzusi).
        db.Context.Calls.Add(Missed("909999999", At(Day, 11)));
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.CallsAnswerSpeed));
    }

    [Fact]
    public async Task Sekin_javoblar_kop_bolsa_YIQILADI()
    {
        using var db = TestDb.Sqlite();
        for (var i = 0; i < 4; i++)
        {
            var start = At(Day, 10, i);
            db.Context.Calls.Add(new Call
            {
                PhoneNumber = $"90000000{i}",
                Direction = "inbound",
                Status = "completed",
                OperatorUserId = i == 0 ? "" : User,   // operatorsiz kiruvchi ham sanaladi
                StartedAt = start,
                AnsweredAt = start.AddSeconds(i == 0 ? 40 : 5),
            });
        }
        await db.Context.SaveChangesAsync();

        Assert.False(await RunOne(db, ChecklistAutoCheck.CallsAnswerSpeed));
    }

    [Fact]
    public async Task Kiruvchi_qongiroq_bolmagan_kun_OTADI()
    {
        using var db = TestDb.Sqlite();
        Assert.True(await RunOne(db, ChecklistAutoCheck.CallsAnswerSpeed));
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

    /* =========================================================================================
     *  LIDLAR
     * ========================================================================================= */

    private static Lead OpenLead(string id) => new()
    {
        Id = id,
        FullName = "Lid",
        AssigneeUserId = User,
        Stage = "new",
    };

    [Fact]
    public async Task Ochiq_lid_bolmasa_OTADI()
    {
        using var db = TestDb.Sqlite();
        Assert.True(await RunOne(db, ChecklistAutoCheck.LeadsWithoutTaskZero));
    }

    [Fact]
    public async Task Hech_bir_topshiriq_lidga_BOGLANMAGAN_bolsa_OTADI_fail_open()
    {
        using var db = TestDb.Sqlite();
        db.Context.Leads.Add(OpenLead("L1"));
        db.Context.WorkTasks.Add(Task(Day));   // bog'lanmagan topshiriq
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.LeadsWithoutTaskZero));
    }

    [Fact]
    public async Task Boglanish_ISHLATILSA_vazifasiz_lid_YIQITADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.Leads.Add(OpenLead("L1"));
        db.Context.Leads.Add(OpenLead("L2"));   // bunga topshiriq yo'q
        var t = Task(Day);
        t.Tags = new List<string> { "L1" };
        db.Context.WorkTasks.Add(t);
        await db.Context.SaveChangesAsync();

        Assert.False(await RunOne(db, ChecklistAutoCheck.LeadsWithoutTaskZero));
    }

    [Fact]
    public async Task Har_ochiq_lidda_topshiriq_bolsa_OTADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.Leads.Add(OpenLead("L1"));
        db.Context.Leads.Add(OpenLead("L2"));
        var t = Task(Day);
        t.Tags = new List<string> { "L1", "L2" };
        db.Context.WorkTasks.Add(t);
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.LeadsWithoutTaskZero));
    }

    [Fact]
    public async Task Yopilgan_lid_vazifa_TALAB_QILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var closed = OpenLead("L1");
        closed.ClosedAt = Prev;
        db.Context.Leads.Add(closed);
        var linked = OpenLead("L2");
        db.Context.Leads.Add(linked);
        var t = Task(Day);
        t.Tags = new List<string> { "L2" };
        db.Context.WorkTasks.Add(t);
        await db.Context.SaveChangesAsync();

        Assert.True(await RunOne(db, ChecklistAutoCheck.LeadsWithoutTaskZero));
    }
}
