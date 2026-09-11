using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// "BOG'LANISH KERAK" hisobotining YAGONA hisob-kitobi (<see cref="ContactReport"/>) va uning
/// AI tahliliga uzatiladigan qismi (<see cref="ContactAiAnalysisService"/>).
///
/// <para>Tekshiruv AYNAN raqamlar ustida: shu sonlar hisobot sahifasida ham, kunlik jurnalda ham,
/// AI promptida ham ishlatiladi — noto'g'ri bo'lsa AI ishonch bilan YOLG'ON xulosa yozadi.</para>
///
/// <para>⚠️ Testlarda <c>AppSecrets.Init</c> chaqirilmaydi, ya'ni Gemini kaliti BO'SH — tashqi
/// tarmoq so'rovi hech qanday holatda ketmaydi.</para>
/// </summary>
public class ContactReportTests
{
    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    /// <summary>Talab (case) — sabab yorlig'i bilan.</summary>
    private static ContactRequest Request(string studentName, string reason, string createdAt,
        string status = ContactStatuses.New, string due = "") => new()
    {
        StudentId = Guid.NewGuid().ToString(),
        StudentName = studentName,
        ReasonLabel = reason,
        Status = status,
        DueDate = due,
        CreatedAt = createdAt,
    };

    /// <summary>Bitta hodisa (urinish/izoh/ochilish).</summary>
    private static ContactAttempt Attempt(
        ContactRequest req, string date, string time, string type = ContactAttemptTypes.Contact,
        string result = "answered", string response = "", string next = ContactStatuses.Done,
        string actor = "Operator") => new()
    {
        RequestId = req.Id,
        StudentId = req.StudentId,
        Type = type,
        Result = type == ContactAttemptTypes.Contact ? result : "",
        Response = response,
        NextStatus = next,
        ActorId = actor,
        ActorName = actor,
        CreatedAt = $"{date}T{time}:00",
        Date = date,
    };

    // ===================== Hisobot raqamlari =====================

    [Fact]
    public async Task Build_KOTARMAGAN_qongiroq_URINISH_lekin_BOGLANILDI_emas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var req = Request("Ali", "To'lov", "2026-08-10T09:00:00");
        ctx.ContactRequests.Add(req);
        ctx.ContactAttempts.AddRange(
            Attempt(req, "2026-08-10", "09:10", result: "answered", next: ContactStatuses.Done),
            Attempt(req, "2026-08-10", "09:20", result: "no_answer", next: ContactStatuses.Callback),
            Attempt(req, "2026-08-10", "09:30", result: "busy", next: ContactStatuses.Callback));
        await ctx.SaveChangesAsync();

        var m = await ContactReport.BuildAsync(ctx, "2026-08-10", "2026-08-10", Today);

        Assert.Equal(3, m.Attempts);
        // "Bog'lanildi" — faqat odam bilan haqiqatan gaplashilgani (ContactService.Reached).
        Assert.Equal(1, m.Reached);
        Assert.Equal(1, m.Done);
        Assert.Equal(2, m.Callback);
    }

    [Fact]
    public async Task Build_KUNLIK_qatorlar_BOSH_kunlarni_ham_toldiradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var req = Request("Ali", "Kelmayapti", "2026-08-01T09:00:00");
        ctx.ContactRequests.Add(req);
        ctx.ContactAttempts.Add(Attempt(req, "2026-08-03", "10:00"));
        await ctx.SaveChangesAsync();

        var m = await ContactReport.BuildAsync(ctx, "2026-08-01", "2026-08-05", Today);

        Assert.Equal(5, m.Daily.Count);
        Assert.Equal("2026-08-01", m.Daily[0].Date);
        Assert.Equal(0, m.Daily[0].Attempts);
        Assert.Equal(1, m.Daily[2].Attempts);
    }

    [Fact]
    public async Task Build_OpenNow_davrga_BOGLIQ_EMAS()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // Talab BUGUN ochiq, lekin tanlangan davr — o'tgan yil.
        ctx.ContactRequests.Add(Request("Ali", "To'lov", $"{Today}T09:00:00", ContactStatuses.Callback,
            due: "2020-01-01"));
        await ctx.SaveChangesAsync();

        var m = await ContactReport.BuildAsync(ctx, "2025-01-01", "2025-01-31", Today);

        Assert.Equal(0, m.Attempts);          // davrda hech narsa bo'lmagan
        Assert.Equal(1, m.OpenNow);           // ...lekin navbat HOZIR bo'sh emas
        Assert.Equal(1, m.OverdueNow);
    }

    [Fact]
    public async Task Build_NAMUNALAR_faqat_sorralganda_va_JAVOB_yozilganlar()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var req = Request("Ali Valiyev", "To'lov kechikdi", "2026-08-10T09:00:00");
        ctx.ContactRequests.Add(req);
        ctx.ContactAttempts.AddRange(
            Attempt(req, "2026-08-10", "09:10", response: "Onasi oyoq oxirida to'laymiz dedi"),
            Attempt(req, "2026-08-10", "09:20", result: "no_answer", response: ""));
        await ctx.SaveChangesAsync();

        var without = await ContactReport.BuildAsync(ctx, "2026-08-10", "2026-08-10", Today);
        Assert.Empty(without.Samples);        // hisobot sahifasi namunalarni so'ramaydi

        var with = await ContactReport.BuildAsync(ctx, "2026-08-10", "2026-08-10", Today, sampleCount: 10);
        var s = Assert.Single(with.Samples);
        Assert.Equal("To'lov kechikdi", s.ReasonLabel);
        Assert.Contains("to'laymiz", s.Response);
        // ⚠️ MAXFIYLIK: namunada o'quvchi ismi UMUMAN yo'q (DTO'da bunday maydon ham yo'q).
        Assert.DoesNotContain("Ali Valiyev", System.Text.Json.JsonSerializer.Serialize(s));
    }

    // ===================== Kunlik jurnal =====================

    [Fact]
    public async Task Journal_KUNLAR_yangisidan_ICHIDA_ertalabdan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var req = Request("Ali", "Kelmayapti", "2026-08-09T08:00:00");
        ctx.ContactRequests.Add(req);
        ctx.ContactAttempts.AddRange(
            Attempt(req, "2026-08-09", "15:00", response: "kechqurun"),
            Attempt(req, "2026-08-09", "09:00", response: "ertalab"),
            Attempt(req, "2026-08-10", "10:00", response: "ertasiga"));
        await ctx.SaveChangesAsync();

        var days = await ContactReport.JournalAsync(ctx, "2026-08-01", "2026-08-31");

        Assert.Equal(2, days.Count);
        Assert.Equal("2026-08-10", days[0].Date);          // eng yangi kun tepada
        var first = days[1];
        Assert.Equal("2026-08-09", first.Date);
        Assert.Equal("09:00", first.Items[0].Time);        // kun ichida ertalabdan kechgacha
        Assert.Equal("15:00", first.Items[1].Time);
        Assert.Equal("Ali", first.Items[0].StudentName);
        Assert.Equal("Kelmayapti", first.Items[0].ReasonLabel);
    }

    [Fact]
    public async Task Journal_TUR_boyicha_filtr_va_NOMALUM_tur_JIM_tashlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var req = Request("Ali", "To'lov", "2026-08-09T08:00:00");
        ctx.ContactRequests.Add(req);
        ctx.ContactAttempts.AddRange(
            Attempt(req, "2026-08-09", "08:00", type: ContactAttemptTypes.Created, next: ""),
            Attempt(req, "2026-08-09", "09:00", response: "gaplashdik"),
            Attempt(req, "2026-08-09", "10:00", type: ContactAttemptTypes.Note, response: "izoh", next: ""));
        await ctx.SaveChangesAsync();

        var onlyCalls = await ContactReport.JournalAsync(
            ctx, "2026-08-09", "2026-08-09", new[] { ContactAttemptTypes.Contact });
        Assert.Single(onlyCalls[0].Items);
        Assert.Equal("Bog'lanildi", onlyCalls[0].Items[0].TypeLabel);

        // Bo'sh filtr — hammasi.
        var all = await ContactReport.JournalAsync(ctx, "2026-08-09", "2026-08-09");
        Assert.Equal(3, all[0].Items.Count);
        Assert.Equal(1, all[0].Attempts);   // jamlanma faqat "contact" turini sanaydi
        Assert.Equal(1, all[0].Created);
    }

    // ===================== AI tahlili =====================

    [Fact]
    public async Task Ai_SHU_DAVR_uchun_BUGUNGI_tahlil_bolsa_Gemini_CHAQIRILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.ContactAiAnalyses.Add(new ContactAiAnalysis
        {
            FromDate = "2026-08-01",
            ToDate = "2026-08-31",
            Date = Today,
            CreatedAt = AppClock.Iso(),
            Model = "gemini-test",
            Summary = "Navbat yaxshi ishlanmoqda.",
            OverallScore = 72,
            ResultJson = """
                {
                  "ai": {
                    "umumiy": "Navbat yaxshi ishlanmoqda.",
                    "sabablar": "", "javoblar": "", "sifat": "", "xodimlar": "", "ozgarishlar": "",
                    "kuchli": [], "zaif": [], "xavflar": [], "tavsiyalar": [],
                    "baholar": { "qamrov": 70, "aloqa": 60, "natija": 80, "sifat": 50, "umumiy": 72 },
                    "trend": "barqaror"
                  },
                  "metrics": { "from": "2026-08-01", "to": "2026-08-31", "attempts": 10 }
                }
                """,
        });
        await ctx.SaveChangesAsync();

        // Kalit yo'q — lekin "bugun allaqachon qilingan" tekshiruvi kalitdan OLDIN turadi.
        var r = await ContactAiAnalysisService.GenerateAsync(ctx, null, "2026-08-01", "2026-08-31");

        Assert.True(r.Ok);
        Assert.True(r.AlreadyToday);
        Assert.Equal(72, r.Record!.OverallScore);
        Assert.Equal("2026-08-01", r.Record.From);
    }

    [Fact]
    public async Task Ai_BOSHQA_DAVR_alohida_tahlil_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var req = Request("Ali", "To'lov", "2026-07-05T09:00:00");
        ctx.ContactRequests.Add(req);
        ctx.ContactAttempts.Add(Attempt(req, "2026-07-05", "09:10", response: "to'laydi"));
        ctx.ContactAiAnalyses.Add(new ContactAiAnalysis
        {
            FromDate = "2026-08-01", ToDate = "2026-08-31", Date = Today,
            CreatedAt = AppClock.Iso(), Model = "gemini-test", Summary = "…", OverallScore = 50,
            ResultJson = "{}",
        });
        await ctx.SaveChangesAsync();

        // Boshqa davr — bugungi yozuv unga tegishli emas, demak kalit tekshiruviga o'tadi.
        var r = await ContactAiAnalysisService.GenerateAsync(ctx, null, "2026-07-01", "2026-07-31");

        Assert.False(r.Ok);
        Assert.Contains("API kaliti", r.Error);
        // Xato bo'lganda yozuv SAQLANMAYDI (yarim tahlil tarixda qolib ketmasin).
        Assert.Single(ctx.ContactAiAnalyses);
    }

    [Fact]
    public async Task Ai_BOSH_davrda_Gemini_umuman_chaqirilmaydi()
    {
        using var db = TestDb.Sqlite();

        var r = await ContactAiAnalysisService.GenerateAsync(db.Context, null, "2026-01-01", "2026-01-31");

        Assert.False(r.Ok);
        // Kalit xatosi EMAS — ma'lumot yo'qligi (so'rov puli bekorga ketmasin).
        Assert.Contains("ma'lumot yo'q", r.Error);
    }

    [Fact]
    public async Task Ai_TARIX_davr_boyicha_filtrlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ContactAiAnalysis Rec(string from, string to, string createdAt) => new()
        {
            FromDate = from, ToDate = to, Date = Today, CreatedAt = createdAt,
            Model = "gemini-test", Summary = "…", OverallScore = 60,
            ResultJson = """
                {
                  "ai": { "umumiy": "…", "sabablar": "", "javoblar": "", "sifat": "", "xodimlar": "",
                          "ozgarishlar": "", "kuchli": [], "zaif": [], "xavflar": [], "tavsiyalar": [],
                          "baholar": { "qamrov": 1, "aloqa": 1, "natija": 1, "sifat": 1, "umumiy": 60 },
                          "trend": "barqaror" },
                  "metrics": {}
                }
                """,
        };
        ctx.ContactAiAnalyses.AddRange(
            Rec("2026-08-01", "2026-08-31", "2026-08-11T10:00:00"),
            Rec("2026-08-01", "2026-08-31", "2026-08-12T10:00:00"),
            Rec("2026-07-01", "2026-07-31", "2026-08-12T11:00:00"));
        await ctx.SaveChangesAsync();

        var all = await ContactAiAnalysisService.HistoryAsync(ctx);
        Assert.Equal(3, all.Count);
        Assert.Equal("2026-07-01", all[0].From);      // eng yangisi birinchi

        var august = await ContactAiAnalysisService.HistoryAsync(ctx, "2026-08-01", "2026-08-31");
        Assert.Equal(2, august.Count);
        Assert.All(august, r => Assert.Equal("2026-08-31", r.To));
    }
}
