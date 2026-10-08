using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Kpi;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BULUTLI TELEFONIYA (MoiZvonki) OLIB TASHLANDI — KPI endi Local Call (CTI) ma'lumotida ishlaydi.
///
/// <para><b>Nima qulflanadi:</b></para>
/// <list type="number">
///   <item>CTI yo'nalishi → KPI namunasi shartnomasi (<c>incoming/missed → inbound</c>,
///   <c>outgoing → outbound</c>) va «teginish» ta'rifi (javobsiz kiruvchi — teginish EMAS);</item>
///   <item>kunlik «teginish»/«muloqot vaqti» va haftalik sifat namunasi FAQAT xodimning
///   o'z agent(lar)i (<c>CtiAgent.StaffUserId</c>) bo'yicha;</item>
///   <item>«Bugun» sahifasidagi normalar CTI yozuvlaridan hisoblanadi va isbot havolasi Local Call
///   sahifasiga olib boradi (o'chirilgan sahifaga EMAS);</item>
///   <item>olib tashlangan qism qaytib kelmasin: ruxsat kaliti, marshrut, model ustuni, kod.</item>
/// </list>
/// </summary>
public class MoiZvonkiRemovalTests
{
    private const string User = "u1";
    private const string Day = "2026-05-20";

    private static DateTime At(string date, int hour, int minute = 0) =>
        DateTime.Parse($"{date}T00:00:00").AddHours(hour).AddMinutes(minute);

    private static void SeedAgents(TestDb db)
    {
        db.Context.CtiAgents.Add(new CtiAgent { Id = "a-u1", Login = "u1-tel", StaffUserId = User });
        db.Context.CtiAgents.Add(new CtiAgent { Id = "a-u1-2", Login = "u1-tel2", StaffUserId = User });
        db.Context.CtiAgents.Add(new CtiAgent { Id = "a-u2", Login = "u2-tel", StaffUserId = "u2" });
        db.Context.CtiAgents.Add(new CtiAgent { Id = "a-free", Login = "qabulxona", StaffUserId = null });
    }

    private static CtiCallRecord Rec(string agent, string direction, DateTime at, int seconds = 0,
        bool audio = false, string transcript = "", string ai = "") => new()
    {
        AgentId = agent,
        Direction = direction,
        RemoteNumber = "+998901234567",
        StartedAt = at,
        AnsweredAt = direction == "missed" ? null : at.AddSeconds(5),
        DurationSec = seconds,
        AudioUploaded = audio,
        AudioPath = audio ? $"{Guid.NewGuid():N}.m4a" : "",
        Transcript = transcript,
        AiAnalysis = ai,
    };

    /* =========================================================================================
     *  SOF QOIDALAR
     * ========================================================================================= */

    [Theory]
    [InlineData("incoming", "inbound")]
    [InlineData("missed", "inbound")]
    [InlineData("outgoing", "outbound")]
    [InlineData(null, "inbound")]
    public void CTI_yonalishi_KPI_namunasi_shartnomasiga_ogiriladi(string? cti, string expected) =>
        Assert.Equal(expected, KpiCallSource.SampleDirection(cti));

    [Theory]
    [InlineData("incoming", true)]
    [InlineData("outgoing", true)]   // javobsiz chiquvchi ham URINISH
    [InlineData("missed", false)]    // javobsiz kiruvchi — operator hech kimga tegmagan
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Teginish_tarifi(string? direction, bool expected) =>
        Assert.Equal(expected, KpiCallSource.IsTouch(direction));

    /* =========================================================================================
     *  KUNLIK NORMALAR — teginish va muloqot vaqti
     * ========================================================================================= */

    [Fact]
    public async Task Kunlik_teginish_va_muloqot_FAQAT_xodim_agentlaridan()
    {
        using var db = TestDb.Sqlite();
        SeedAgents(db);
        db.Context.CtiCallRecords.AddRange(
            Rec("a-u1", "outgoing", At(Day, 9), 120),
            Rec("a-u1", "outgoing", At(Day, 9, 30), 0),     // javobsiz urinish — teginish
            Rec("a-u1-2", "incoming", At(Day, 10), 300),    // xodimning IKKINCHI telefoni
            Rec("a-u1", "missed", At(Day, 11)),             // teginish EMAS
            Rec("a-u2", "outgoing", At(Day, 12), 900),      // boshqa xodim
            Rec("a-free", "incoming", At(Day, 13), 600),    // biriktirilmagan telefon
            Rec("a-u1", "outgoing", At("2026-05-19", 23, 59), 999)); // kecha
        await db.Context.SaveChangesAsync();

        var from = DateTime.Parse($"{Day}T00:00:00");
        var (touches, talk) = await KpiCallSource.DailyTotalsAsync(db.Context, User, from, from.AddDays(1));

        Assert.Equal(3, touches);
        Assert.Equal(420, talk);
    }

    [Fact]
    public async Task Agenti_yoq_xodimda_teginish_ham_muloqot_ham_NOL()
    {
        using var db = TestDb.Sqlite();
        SeedAgents(db);
        db.Context.CtiCallRecords.Add(Rec("a-free", "outgoing", At(Day, 9), 120));
        await db.Context.SaveChangesAsync();

        var from = DateTime.Parse($"{Day}T00:00:00");
        Assert.Equal((0, 0), await KpiCallSource.DailyTotalsAsync(db.Context, "u3", from, from.AddDays(1)));
        Assert.Equal((0, 0), await KpiCallSource.DailyTotalsAsync(db.Context, "", from, from.AddDays(1)));
    }

    [Fact]
    public async Task Bugun_sahifasi_normalari_CTI_dan_va_havola_Local_Call_ga()
    {
        using var db = TestDb.Sqlite();
        SeedAgents(db);
        db.Context.CtiCallRecords.AddRange(
            Rec("a-u1", "outgoing", At(Day, 9), 3600),
            Rec("a-u1", "incoming", At(Day, 10), 1800),
            Rec("a-u1", "missed", At(Day, 11)));
        await db.Context.SaveChangesAsync();

        var today = await KpiMetricsService.TodayAsync(db.Context, User, Day);

        var touches = Assert.Single(today.Norms, n => n.Key == "touches");
        var talk = Assert.Single(today.Norms, n => n.Key == "talkTime");
        Assert.Equal(2, touches.Fact);
        Assert.Equal(1.5, talk.Fact);
        Assert.Equal("/admin/calls/local", touches.Link);
        Assert.Equal("/admin/calls/local", talk.Link);
    }

    /* =========================================================================================
     *  HAFTALIK SIFAT NAZORATI NAMUNASI
     * ========================================================================================= */

    [Fact]
    public async Task Sifat_namunasi_faqat_ozining_yozuvli_uzun_songgi_qongiroqlari()
    {
        using var db = TestDb.Sqlite();
        SeedAgents(db);
        var now = DateTime.Parse($"{Day}T18:00:00");
        var ok1 = Rec("a-u1", "incoming", now.AddDays(-1), 120, audio: true, transcript: "salom", ai: "  ");
        var ok2 = Rec("a-u1-2", "outgoing", now.AddDays(-6), 61, audio: true);
        db.Context.CtiCallRecords.AddRange(
            ok1, ok2,
            Rec("a-u1", "outgoing", now.AddDays(-2), 60, audio: true),    // 60 s — chegarada, KIRMAYDI
            Rec("a-u1", "outgoing", now.AddDays(-2), 300, audio: false),  // yozuvsiz
            Rec("a-u1", "outgoing", now.AddDays(-8), 300, audio: true),   // 7 kundan eski
            Rec("a-u2", "outgoing", now.AddDays(-1), 300, audio: true),   // boshqa xodim
            Rec("a-free", "incoming", now.AddDays(-1), 300, audio: true)); // biriktirilmagan
        // Yuklanmagan (AudioUploaded=false, lekin yo'l bor) — ham KIRMAYDI.
        var half = Rec("a-u1", "outgoing", now.AddDays(-1), 300, audio: true);
        half.AudioUploaded = false;
        db.Context.CtiCallRecords.Add(half);
        await db.Context.SaveChangesAsync();

        var list = await KpiCallSource.SampleCandidatesAsync(db.Context, User, now.AddDays(-7));

        Assert.Equal(new[] { ok1.Id, ok2.Id }.OrderBy(x => x), list.Select(x => x.CallId).OrderBy(x => x));
        var s1 = list.Single(x => x.CallId == ok1.Id);
        Assert.Equal("inbound", s1.Direction);
        Assert.Equal("+998901234567", s1.PhoneNumber);
        Assert.Equal(120, s1.DurationSeconds);
        Assert.True(s1.HasRecording);
        Assert.Equal("salom", s1.Transcript);
        Assert.Null(s1.AiAnalysis);                    // bo'sh/probel — null
        Assert.Equal(ok1.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss"), s1.StartedAt);
        Assert.Equal("outbound", list.Single(x => x.CallId == ok2.Id).Direction);
    }

    /* =========================================================================================
     *  OLIB TASHLANGAN QISM QAYTIB KELMASIN
     * ========================================================================================= */

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    [Fact]
    public void CenterMeta_da_sinxron_kursori_ustuni_YOQ()
    {
        Assert.Null(typeof(CenterMeta).GetProperty("MoiZvonkiSyncCursor"));
    }

    [Fact]
    public void Ruxsat_katalogida_bulut_kaliti_YOQ__Local_Call_kaliti_BOR()
    {
        var constants = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Client", "src", "config", "constants.ts"));
        Assert.DoesNotContain("'calls.cloud'", constants);
        Assert.Contains("'calls.local'", constants);
    }

    [Fact]
    public void KPI_isbot_havolasi_MAVJUD_marshrutga_olib_boradi()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Client", "src", "App.tsx"));
        Assert.Equal("/admin/calls/local", KpiCallSource.Link);
        Assert.Contains("path=\"calls/local\"", app);
        Assert.DoesNotContain("CallCenterPage", app);
    }

    [Fact]
    public void Kodda_bulut_provayderi_izi_QOLMAGAN()
    {
        // Migratsiyalar TARIX (o'zgartirilmaydi) — tekshiruvdan tashqari.
        var roots = new[]
        {
            ("WunderkindLC.Server", "*.cs"), ("WunderkindLC.Application", "*.cs"),
            ("WunderkindLC.Domain", "*.cs"), ("WunderkindLC.Infrastructure", "*.cs"),
            (Path.Combine("WunderkindLC.Client", "src"), "*.ts"),
            (Path.Combine("WunderkindLC.Client", "src"), "*.tsx"),
        };
        var rx = new Regex("moi[_\\- ]?zvonki", RegexOptions.IgnoreCase);
        var hits = new List<string>();
        foreach (var (rel, pattern) in roots)
        {
            var dir = Path.Combine(RepoRoot, rel);
            foreach (var file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
            {
                var p = file.Replace('\\', '/');
                if (p.Contains("/Migrations/") || p.Contains("/bin/") || p.Contains("/obj/")
                    || p.Contains("/node_modules/")) continue;
                if (rx.IsMatch(File.ReadAllText(file))) hits.Add(Path.GetRelativePath(RepoRoot, file));
            }
        }
        Assert.True(hits.Count == 0, "Bulut telefoniya izi qolgan:\n  " + string.Join("\n  ", hits));
    }

    [Fact]
    public void CSRF_istisnolarida_telefoniya_webhook_prefiksi_YOQ()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "CsrfMiddleware.cs"));
        Assert.DoesNotContain("/api/telephony/", src);
    }
}
