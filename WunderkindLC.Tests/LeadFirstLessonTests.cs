using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «BIRINCHI DARSGA YOZILGANLAR» (<see cref="LeadFirstLesson"/>) — tanlov qoidasi, filtrlar va
/// endpoint darvozasi. Eng muhimi: ta'rif bosh sahifadagi "Birinchi darsga keladiganlar"
/// kartochkasi (<see cref="DashboardSummary.FirstLessonLeads"/>) bilan BITTA — kartochkadagi son
/// ro'yxatdagi qizil BO'LMAGAN qatorlar soniga teng bo'lishi SHART.
/// </summary>
public class LeadFirstLessonTests
{
    private const string Today = "2026-09-18"; // Juma

    private static LeadFirstLesson.TrialRow T(string lead, string at, string result = "pending", string group = "g1", string? id = null) =>
        new(id ?? $"{lead}-{at}-{result}", lead, group, result, at);

    // ===================== IsPast / Pick / Display =====================

    [Fact]
    public void IsPast_bugungi_sinov_otgan_EMAS_kechagisi_va_buzugi_otgan()
    {
        Assert.False(LeadFirstLesson.IsPast("2026-09-18T09:00", Today));
        Assert.False(LeadFirstLesson.IsPast("2026-09-20T15:30", Today));
        Assert.True(LeadFirstLesson.IsPast("2026-09-17T18:00", Today));
        Assert.True(LeadFirstLesson.IsPast("", Today));
        Assert.True(LeadFirstLesson.IsPast("2026-09", Today));
    }

    [Fact]
    public void Pick_keladiganlarning_ENG_YAQINI_otganlardan_ustun()
    {
        var pick = LeadFirstLesson.Pick(new[]
        {
            T("a", "2026-09-10T10:00"),
            T("a", "2026-09-25T10:00"),
            T("a", "2026-09-19T15:00"),
        }, Today);
        Assert.Equal("2026-09-19T15:00", pick!.Value.ScheduledAt);
    }

    [Fact]
    public void Pick_keladigani_yoq_bolsa_otganlarning_ENG_SONGGISI()
    {
        var pick = LeadFirstLesson.Pick(new[]
        {
            T("a", "2026-09-01T10:00"),
            T("a", "2026-09-12T10:00"),
        }, Today);
        Assert.Equal("2026-09-12T10:00", pick!.Value.ScheduledAt);
    }

    [Fact]
    public void Pick_natijasi_belgilangan_sinovlar_hisobga_OLINMAYDI()
    {
        Assert.Null(LeadFirstLesson.Pick(new[]
        {
            T("a", "2026-09-20T10:00", "came"),
            T("a", "2026-09-21T10:00", "no_show"),
            T("a", "2026-09-22T10:00", "stayed"),
            T("a", "2026-09-23T10:00", "left"),
        }, Today));
    }

    [Fact]
    public void Display_pending_yoq_bolsa_eng_songgi_sinovni_oladi_natijasidan_qati_nazar()
    {
        var shown = LeadFirstLesson.Display(new[]
        {
            T("a", "2026-09-01T10:00", "came", "old"),
            T("a", "2026-09-05T10:00", "stayed", "new"),
        }, Today);
        Assert.Equal("new", shown!.Value.GroupId);

        // Pending bor bo'lsa — aynan u (birinchi dars) ko'rsatiladi.
        var withPending = LeadFirstLesson.Display(new[]
        {
            T("a", "2026-09-30T10:00", "came", "late"),
            T("a", "2026-09-20T10:00", "pending", "trial"),
        }, Today);
        Assert.Equal("trial", withPending!.Value.GroupId);
    }

    // ===================== Select — kim ro'yxatga tushadi =====================

    [Fact]
    public void Select_aylantirilgan_va_pending_sinovsiz_lidlar_TUSHMAYDI()
    {
        var leads = new[]
        {
            new DashboardSummary.LeadRow("open", false),
            new DashboardSummary.LeadRow("conv", true),
            new DashboardSummary.LeadRow("done", false),
            new DashboardSummary.LeadRow("none", false),
        };
        var trials = new[]
        {
            T("open", "2026-09-20T10:00"),
            T("conv", "2026-09-20T10:00"),
            T("done", "2026-09-20T10:00", "came"),
            T("ghost", "2026-09-20T10:00"), // o'chirilgan lid — ro'yxatda yo'q
        };

        var picked = LeadFirstLesson.Select(leads, trials, Today);

        Assert.Equal(new[] { "open" }, picked.Keys.ToArray());
    }

    [Fact]
    public void Kartochka_soni_royxatdagi_QIZIL_BOLMAGAN_qatorlarga_TENG()
    {
        var leads = new[]
        {
            new DashboardSummary.LeadRow("a", false), // bugun
            new DashboardSummary.LeadRow("b", false), // o'tib ketgan
            new DashboardSummary.LeadRow("c", false), // eskisi o'tgan, yangisi keladi
            new DashboardSummary.LeadRow("d", true),  // aylantirilgan
            new DashboardSummary.LeadRow("e", false), // ikki keladigan sinov — bitta
            new DashboardSummary.LeadRow("f", false), // buzuq sana
        };
        var trials = new[]
        {
            T("a", "2026-09-18T15:00"),
            T("b", "2026-09-10T15:00"),
            T("c", "2026-09-01T15:00"), T("c", "2026-09-22T15:00"),
            T("d", "2026-09-22T15:00"),
            T("e", "2026-09-19T15:00"), T("e", "2026-09-26T15:00"),
            T("f", ""),
        };

        var picked = LeadFirstLesson.Select(leads, trials, Today);
        var upcoming = picked.Values.Count(t => !LeadFirstLesson.IsPast(t.ScheduledAt, Today));

        var card = DashboardSummary.FirstLessonLeads(
            leads, trials.Select(t => new DashboardSummary.TrialRow(t.LeadId, t.Result, t.ScheduledAt)), Today);

        Assert.Equal(card, upcoming);
        Assert.Equal(3, upcoming);          // a, c, e
        Assert.Equal(5, picked.Count);      // + o'tib ketgan b va buzuq f (qizil qatorlar)
    }

    // ===================== Filtrlar =====================

    private static LeadFirstLesson.Row R(
        string id, string at, string name = "Ali Valiyev", string course = "Ingliz tili", string level = "Beginner",
        string teacher = "t1", string assignee = "u1", int[]? days = null, string[]? phones = null, string search = "") =>
        new(id, name, phones ?? new[] { "+998901234567" }, search, at,
            LeadFirstLesson.IsPast(at, Today), course, level, teacher, assignee, days ?? new[] { 0, 2, 4 });

    private static string[] Ids(IEnumerable<LeadFirstLesson.Row> rows) => rows.Select(r => r.LeadId).ToArray();

    [Fact]
    public void Apply_filtrsiz_hammasi_birinchi_dars_sanasi_boyicha_OSIB_boradi()
    {
        var rows = new[] { R("late", "2026-09-25T10:00"), R("past", "2026-09-10T10:00"), R("mid", "2026-09-19T10:00") };
        Assert.Equal(new[] { "past", "mid", "late" }, Ids(LeadFirstLesson.Apply(rows, new())));
    }

    [Fact]
    public void Apply_sana_va_oraliq_chegaralari_KIRADI()
    {
        var rows = new[] { R("a", "2026-09-17T10:00"), R("b", "2026-09-18T10:00"), R("c", "2026-09-20T23:00") };
        Assert.Equal(new[] { "b" }, Ids(LeadFirstLesson.Apply(rows, new(Date: "2026-09-18"))));
        Assert.Equal(new[] { "b", "c" }, Ids(LeadFirstLesson.Apply(rows, new(From: "2026-09-18", To: "2026-09-20"))));
    }

    [Fact]
    public void Apply_kurs_va_daraja_registr_farqisiz()
    {
        var rows = new[] { R("a", "2026-09-19T10:00", course: "Ingliz tili", level: "Beginner"),
                           R("b", "2026-09-19T10:00", course: "Matematika", level: "Intermediate") };
        Assert.Equal(new[] { "a" }, Ids(LeadFirstLesson.Apply(rows, new(Course: "ingliz TILI"))));
        Assert.Equal(new[] { "b" }, Ids(LeadFirstLesson.Apply(rows, new(Level: "intermediate"))));
    }

    [Fact]
    public void Apply_kun_birinchi_dars_sanasining_hafta_kuni()
    {
        // 2026-09-18 — Juma (4), 2026-09-21 — Dushanba (0).
        var rows = new[] { R("fri", "2026-09-18T10:00"), R("mon", "2026-09-21T10:00") };
        Assert.Equal(new[] { "mon" }, Ids(LeadFirstLesson.Apply(rows, new(Weekday: 0))));
        Assert.Equal(new[] { "fri" }, Ids(LeadFirstLesson.Apply(rows, new(Weekday: 4))));
        Assert.Equal(4, LeadFirstLesson.WeekdayOf("2026-09-18"));
        Assert.Equal(-1, LeadFirstLesson.WeekdayOf("2026-13-99"));
    }

    [Fact]
    public void Apply_toq_juft_guruh_jadvali_TOLIQ_tushishi_kerak()
    {
        var rows = new[]
        {
            R("odd", "2026-09-19T10:00", days: new[] { 0, 2, 4 }),
            R("even", "2026-09-19T10:00", days: new[] { 1, 3, 5 }),
            R("mixed", "2026-09-19T10:00", days: new[] { 0, 1 }),
            R("none", "2026-09-19T10:00", days: Array.Empty<int>()),
        };
        Assert.Equal(new[] { "odd" }, Ids(LeadFirstLesson.Apply(rows, new(Parity: "odd"))));
        Assert.Equal(new[] { "even" }, Ids(LeadFirstLesson.Apply(rows, new(Parity: "even"))));
    }

    [Fact]
    public void Apply_moderator_oqituvchi_va_biriktirilmaganlar()
    {
        var rows = new[]
        {
            R("u1", "2026-09-19T10:00", assignee: "u1", teacher: "t1"),
            R("u2", "2026-09-19T11:00", assignee: "u2", teacher: "t2"),
            R("nobody", "2026-09-19T12:00", assignee: "", teacher: "t2"),
        };
        Assert.Equal(new[] { "u2" }, Ids(LeadFirstLesson.Apply(rows, new(Assignee: "u2"))));
        Assert.Equal(new[] { "nobody" }, Ids(LeadFirstLesson.Apply(rows, new(Assignee: LeadFirstLesson.NoAssignee))));
        Assert.Equal(new[] { "u2", "nobody" }, Ids(LeadFirstLesson.Apply(rows, new(Teacher: "t2"))));
    }

    [Fact]
    public void Apply_rang_boyicha_otgan_va_keladigan()
    {
        var rows = new[] { R("past", "2026-09-10T10:00"), R("next", "2026-09-19T10:00") };
        Assert.Equal(new[] { "past" }, Ids(LeadFirstLesson.Apply(rows, new(Color: "past"))));
        Assert.Equal(new[] { "next" }, Ids(LeadFirstLesson.Apply(rows, new(Color: "upcoming"))));
    }

    [Fact]
    public void Apply_qidiruv_ism_matn_va_telefon_RAQAMLARI_boyicha()
    {
        var rows = new[]
        {
            R("ali", "2026-09-19T10:00", name: "Ali Valiyev", phones: new[] { "+998-90-111-22-33" }),
            R("vali", "2026-09-19T11:00", name: "Vali Aliyev", phones: new[] { "", "998935556677" }, search: "Otasi: Karim"),
        };
        Assert.Equal(new[] { "ali" }, Ids(LeadFirstLesson.Apply(rows, new(Q: "ali val"))));
        Assert.Equal(new[] { "vali" }, Ids(LeadFirstLesson.Apply(rows, new(Q: "karim"))));
        Assert.Equal(new[] { "ali" }, Ids(LeadFirstLesson.Apply(rows, new(Q: "90 111 22"))));
        Assert.Equal(new[] { "vali" }, Ids(LeadFirstLesson.Apply(rows, new(Q: "5556677"))));
        // 3 tadan kam raqam — telefon bo'yicha qidirilmaydi (har raqamda "9" bor).
        Assert.Empty(LeadFirstLesson.Apply(rows, new(Q: "99")));
    }

    // ===================== Endpoint darvozasi (RBAC) =====================

    private static string ControllerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        var path = Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "LeadsController.cs");
        Assert.True(File.Exists(path), $"Controller fayli topilmadi: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void FirstLesson_endpointi_lidlar_royxati_bilan_BIR_XIL_ruxsat_ostida()
    {
        var src = ControllerSource();
        var classAt = src.IndexOf("public class LeadsController", StringComparison.Ordinal);
        Assert.True(classAt > 0, "LeadsController topilmadi");
        var header = src[..classAt];

        // Sinf darajasi: login + `leads.list` (lidlar ro'yxati `GET api/admin/leads` bilan bir xil).
        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"leads.list\")]", header);
        Assert.DoesNotContain("AllowAnonymous", src);

        // Metod darajasida BOSHQA kalit qo'yilmagan (u sinfdagisini bekor qilardi — permissions.md §4).
        var at = src.IndexOf("[HttpGet(\"first-lesson\")]", StringComparison.Ordinal);
        Assert.True(at > 0, "GET first-lesson topilmadi");
        var signature = src.IndexOf("FirstLesson(", at, StringComparison.Ordinal);
        Assert.True(signature > at);
        var attrs = src[at..signature];
        Assert.DoesNotMatch(new Regex(@"\[AdminPerm\("), attrs);
        Assert.DoesNotContain("AllowAnonymous", attrs);
    }
}
