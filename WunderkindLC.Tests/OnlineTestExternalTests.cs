using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// ONLAYN TEST — TEST KODI va MARKAZDAN TASHQARI ishtirokchilar.
///
/// <para>Talab: markazda o'qimaydigan odam ham testni ishlay olsin — botda «Testni ishlash» →
/// «Test kodi bilan kirish» → KOD → F.I.Sh → test. Uning natijasi <see cref="ExternalTestScore"/> ga
/// yoziladi va natijalar ro'yxatida "Markazdan tashqari" bo'limida ko'rinadi. Test yaratishda
/// "guruhga ham e'lon qilinsinmi yoki faqat kod bilanmi" tanlanadi (<see cref="TestResult.GroupOpen"/>).</para>
/// </summary>
public class OnlineTestExternalTests
{
    // ==================== yordamchilar ====================

    private static readonly string Today = AppClock.Today.ToString("yyyy-MM-dd");

    private static Group AddGroup(AppDbContext ctx, string name = "A guruh")
    {
        var g = new Group { Name = name, MonthlyFee = 500_000m, Days = new List<int> { 0, 2, 4 } };
        ctx.Classes.Add(g);
        return g;
    }

    private static Student AddStudent(AppDbContext ctx, Group? g, string name = "Ali Valiyev")
    {
        var s = new Student { FullName = name, EnrollmentDate = Today };
        ctx.Students.Add(s);
        if (g is not null)
            ctx.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
                JoinedAt = Today, ActivatedAt = Today, RecordedAt = Today,
            });
        return s;
    }

    /// <summary>Onlayn sozlamalar — vaqt oynasi BUGUN va OCHIQ (00:00–23:59).</summary>
    private static OnlineTestDto OnlineDto(
        string key = "ABCD", string code = "", bool groupOpen = true) =>
        new("online", "/uploads/test.pdf", "test.pdf", key.Length, 4, key,
            $"{Today}T00:00", $"{Today}T23:59", code, groupOpen);

    private static async Task<TestResult> CreateOnlineAsync(
        AppDbContext ctx, Group g, string key = "ABCD", string code = "", bool groupOpen = true,
        string name = "Unit 1")
    {
        var (dto, err) = await TestResultService.CreateAsync(
            ctx, g.Id, name, Today, key.Length, "Test admin", OnlineDto(key, code, groupOpen));
        Assert.Null(err);
        Assert.NotNull(dto);
        return await ctx.TestResults.FirstAsync(t => t.Id == dto!.Id);
    }

    // ==================== TEST KODI ====================

    [Fact]
    public void NormalizeCode_probel_tire_kichik_harfni_tozalaydi()
    {
        Assert.Equal("K7M4QP", TestResultService.NormalizeCode(" k7m-4qp "));
        Assert.Equal("K7M4QP", TestResultService.NormalizeCode("K7M 4QP"));
        // DIQQAT: harflar TASHLANMAYDI — kodning o'zi harfdan boshlanishi mumkin, shuning uchun
        // "kod:" kabi prefikslar olib tashlanmaydi (foydalanuvchi faqat kodni yuborishi kerak).
        Assert.Equal("KODK7M4QP", TestResultService.NormalizeCode("kod: K7M4QP"));
        Assert.Equal("", TestResultService.NormalizeCode(null));
        Assert.Equal("", TestResultService.NormalizeCode("   "));
    }

    [Fact]
    public async Task Onlayn_test_yaratilganda_KOD_avtomatik_beriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        await ctx.SaveChangesAsync();

        var t = await CreateOnlineAsync(ctx, g);

        Assert.Equal(6, t.Code.Length);
        Assert.Equal(t.Code.ToUpperInvariant(), t.Code);
        // Adashtiradigan belgilar (0, O, 1, I, L) kod alifbosida YO'Q.
        Assert.DoesNotContain(t.Code, c => c is '0' or 'O' or '1' or 'I' or 'L');
        Assert.True(t.GroupOpen);   // standart — guruhga ham e'lon qilinadi
    }

    [Fact]
    public async Task Kod_QOLDA_kiritilsa_normallashtiriladi_va_takrorlanmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        await ctx.SaveChangesAsync();

        var t1 = await CreateOnlineAsync(ctx, g, code: "my-kod 1", name: "Birinchi");
        Assert.Equal("MYKOD1", t1.Code);

        // AYNAN shu kod bilan ikkinchi test — rad etiladi.
        var (dto, err) = await TestResultService.CreateAsync(
            ctx, g.Id, "Ikkinchi", Today, 4, "Test admin", OnlineDto(code: "MYKOD1"));
        Assert.Null(dto);
        Assert.Contains("MYKOD1", err);
    }

    [Fact]
    public async Task Oflaynga_ogirilsa_kod_bosatiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        await ctx.SaveChangesAsync();
        var t = await CreateOnlineAsync(ctx, g, code: "ABC123");

        var (ok, err) = await TestResultService.UpdateAsync(
            ctx, t.Id, "Unit 1", Today, 100,
            new OnlineTestDto("offline", "", "", 0, 4, "", "", ""));
        Assert.True(ok);
        Assert.Null(err);

        var after = await ctx.TestResults.AsNoTracking().FirstAsync(x => x.Id == t.Id);
        Assert.Equal("", after.Code);   // kod boshqa testga bo'shatiladi
    }

    [Fact]
    public async Task FindByCode_kichik_harf_va_tire_bilan_ham_topadi_oflaynni_topmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        await ctx.SaveChangesAsync();
        var t = await CreateOnlineAsync(ctx, g, code: "K7M4QP");

        Assert.Equal(t.Id, (await TestResultService.FindByCodeAsync(ctx, "k7m-4qp"))?.Id);
        Assert.Null(await TestResultService.FindByCodeAsync(ctx, "YOQKOD"));
        Assert.Null(await TestResultService.FindByCodeAsync(ctx, "abc"));   // 4 belgidan kam

        // Oflayn test kod bo'yicha topilmaydi.
        var off = new TestResult { GroupId = g.Id, Name = "Oflayn", Date = Today, MaxScore = 100, Code = "OFF123" };
        ctx.TestResults.Add(off);
        await ctx.SaveChangesAsync();
        Assert.Null(await TestResultService.FindByCodeAsync(ctx, "OFF123"));
    }

    // ==================== "FAQAT KOD" (GroupOpen=false) ====================

    [Fact]
    public async Task GroupOpen_false_bolsa_test_oquvchi_royxatida_KORINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx, g);
        await ctx.SaveChangesAsync();

        var ochiq = await CreateOnlineAsync(ctx, g, groupOpen: true, name: "Guruhga ochiq");
        var kodli = await CreateOnlineAsync(ctx, g, groupOpen: false, name: "Faqat kod bilan");

        var list = await OnlineTestService.ListForStudentAsync(ctx, s.Id);

        var id = Assert.Single(list).Id;
        Assert.Equal(ochiq.Id, id);
        // "Faqat kod" test ilovada ochilmaydi ham.
        Assert.Null(await OnlineTestService.DetailAsync(ctx, s.Id, kodli.Id));
        var (result, err) = await OnlineTestService.SubmitAsync(ctx, s.Id, kodli.Id, "ABCD");
        Assert.Null(result);
        Assert.Contains("KODI", err);
    }

    // ==================== NATIJALAR: markazdagilar / markazdan tashqari ====================

    [Fact]
    public async Task Detail_markazdagilar_va_markazdan_tashqarini_AJRATIB_qaytaradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var azo = AddStudent(ctx, g, "Azo O'quvchi");
        var boshqa = AddStudent(ctx, AddGroup(ctx, "B guruh"), "Boshqa Guruhdan");
        await ctx.SaveChangesAsync();
        var t = await CreateOnlineAsync(ctx, g);

        // A'zo 3 ball, boshqa guruh o'quvchisi (kod bilan qo'shilgan) 4 ball.
        ctx.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = azo.Id, Score = 3, Source = "bot" });
        ctx.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = boshqa.Id, Score = 4, Source = "bot" });
        // Markazdan tashqari ikki ishtirokchi.
        ctx.ExternalTestScores.Add(new ExternalTestScore
        {
            TestResultId = t.Id, ChatId = 111, FullName = "Tashqi Bir", Score = 2,
            Answers = "AB--", SubmittedAt = $"{Today}T10:00:00",
        });
        ctx.ExternalTestScores.Add(new ExternalTestScore
        {
            TestResultId = t.Id, ChatId = 222, FullName = "Tashqi Ikki", Score = 4,
            Answers = "ABCD", SubmittedAt = $"{Today}T10:05:00", Phone = "998901112233",
        });
        await ctx.SaveChangesAsync();

        var detail = await TestResultService.DetailAsync(ctx, t.Id);
        Assert.NotNull(detail);

        // MARKAZDAGILAR — a'zo + kod bilan qo'shilgan markaz o'quvchisi (u Member=false).
        Assert.Equal(2, detail!.Rows.Count);
        var guest = detail.Rows.Single(r => r.StudentId == boshqa.Id);
        Assert.False(guest.Member);
        Assert.Equal(1, guest.Rank);                       // 4 ball — birinchi
        Assert.True(detail.Rows.Single(r => r.StudentId == azo.Id).Member);

        // MARKAZDAN TASHQARI — alohida ro'yxat, o'z ichida saralangan.
        Assert.NotNull(detail.ExternalRows);
        Assert.Equal(2, detail.ExternalRows!.Count);
        Assert.Equal("Tashqi Ikki", detail.ExternalRows[0].FullName);
        Assert.Equal(1, detail.ExternalRows[0].Rank);
        Assert.Equal("998901112233", detail.ExternalRows[0].Phone);
        Assert.Equal(2, detail.ExternalRows[1].Rank);

        // Ro'yxatdagi sanoqlar.
        var row = Assert.Single(await TestResultService.ListForGroupAsync(ctx, g.Id));
        Assert.Equal(2, row.SubmittedCount);
        Assert.Equal(2, row.ExternalCount);
    }

    [Fact]
    public async Task Faqat_kod_testida_qatnashmagan_azolar_royxatda_KORINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var qatnashgan = AddStudent(ctx, g, "Qatnashgan");
        AddStudent(ctx, g, "Qatnashmagan");
        await ctx.SaveChangesAsync();
        var t = await CreateOnlineAsync(ctx, g, groupOpen: false);

        ctx.TestScores.Add(new TestScore
        {
            TestResultId = t.Id, StudentId = qatnashgan.Id, Score = 4, Source = "bot",
        });
        await ctx.SaveChangesAsync();

        var detail = await TestResultService.DetailAsync(ctx, t.Id);
        // Testga e'lon qilinmagan a'zo "topshirmagan" bo'lib turmaydi.
        var row = Assert.Single(detail!.Rows);
        Assert.Equal(qatnashgan.Id, row.StudentId);

        // Guruhga OCHIQ testda esa hamma a'zo ko'rinadi (eski xatti-harakat).
        var ochiq = await CreateOnlineAsync(ctx, g, groupOpen: true, name: "Ochiq");
        var d2 = await TestResultService.DetailAsync(ctx, ochiq.Id);
        Assert.Equal(2, d2!.Rows.Count);
    }

    [Fact]
    public async Task Test_ochirilsa_markazdan_tashqari_natijalar_ham_ochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        await ctx.SaveChangesAsync();
        var t = await CreateOnlineAsync(ctx, g);
        ctx.ExternalTestScores.Add(new ExternalTestScore
        {
            TestResultId = t.Id, ChatId = 111, FullName = "Tashqi", Score = 2, Answers = "AB--",
        });
        await ctx.SaveChangesAsync();

        Assert.True(await TestResultService.DeleteAsync(ctx, t.Id));
        Assert.Empty(ctx.ExternalTestScores.ToList());
    }

    // ==================== BOT OQIMI: kod → F.I.Sh → javob ====================

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
    }

    private static OnlineTestBotService NewBot() => new(
        new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance),
        new TestHostEnvironment(),
        NullLogger<OnlineTestBotService>.Instance);

    [Fact]
    public async Task Bot_markazdan_tashqari_ishtirokchi_KOD_va_FISh_bilan_testni_ishlaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        await ctx.SaveChangesAsync();
        var t = await CreateOnlineAsync(ctx, g, key: "ABCD", code: "K7M4QP");

        const long chat = 555_000L;
        ctx.BotUsers.Add(new BotUser { ChatId = chat, Name = "Mehmon", Phone = "998900001122" });
        await ctx.SaveChangesAsync();

        var bot = NewBot();

        // 1) KOD — chat markaz o'quvchisiga bog'lanmagan → F.I.Sh so'raladi.
        await bot.HandleCodeAsync(ctx, chat, "k7m-4qp", CancellationToken.None);
        var session = await ctx.TestBotSessions.FirstAsync(s => s.ChatId == chat);
        Assert.Equal(t.Id, session.TestResultId);
        Assert.Equal("", session.StudentId);          // markazdan tashqari
        Assert.Equal("name", session.Stage);

        // 2) F.I.Sh
        var handled = await bot.HandleTextAsync(ctx, chat, "Aliyev Vali", CancellationToken.None);
        Assert.True(handled);
        session = await ctx.TestBotSessions.FirstAsync(s => s.ChatId == chat);
        Assert.Equal("", session.Stage);
        Assert.Equal("Aliyev Vali", session.ExternalName);

        // 3) Javoblar (3 tasi to'g'ri: ABC + noto'g'ri D o'rniga A)
        session.Answers = "ABCA";
        await ctx.SaveChangesAsync();
        await bot.SubmitAsync(ctx, chat, CancellationToken.None);

        var ext = Assert.Single(ctx.ExternalTestScores.ToList());
        Assert.Equal(t.Id, ext.TestResultId);
        Assert.Equal(chat, ext.ChatId);
        Assert.Equal("Aliyev Vali", ext.FullName);
        Assert.Equal("998900001122", ext.Phone);      // botga ulashgan raqam
        Assert.Equal(3m, ext.Score);
        Assert.Equal("ABCA", ext.Answers);
        Assert.Empty(ctx.TestScores.ToList());        // Student jadvaliga TEGMAYDI
        Assert.Empty(ctx.TestBotSessions.ToList());   // sessiya yopildi
    }

    [Fact]
    public async Task Bot_markazdan_tashqari_ishtirokchi_IKKI_MARTA_topshira_olmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        await ctx.SaveChangesAsync();
        var t = await CreateOnlineAsync(ctx, g, key: "ABCD", code: "K7M4QP");

        const long chat = 555_001L;
        ctx.BotUsers.Add(new BotUser { ChatId = chat, Name = "Mehmon" });
        ctx.ExternalTestScores.Add(new ExternalTestScore
        {
            TestResultId = t.Id, ChatId = chat, FullName = "Aliyev Vali", Score = 4,
            Answers = "ABCD", SubmittedAt = $"{Today}T09:00:00",
        });
        await ctx.SaveChangesAsync();

        await NewBot().HandleCodeAsync(ctx, chat, "K7M4QP", CancellationToken.None);

        // Sessiya OCHILMAYDI — allaqachon topshirgan (natijasi ko'rsatiladi).
        Assert.Empty(ctx.TestBotSessions.ToList());
        Assert.Single(ctx.ExternalTestScores.ToList());
    }

    [Fact]
    public async Task Bot_markaz_oquvchisi_kod_bilan_kirsa_bali_TestScore_ga_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var boshqaGuruh = AddGroup(ctx, "B guruh");
        var s = AddStudent(ctx, boshqaGuruh, "Ali Valiyev");
        await ctx.SaveChangesAsync();
        var t = await CreateOnlineAsync(ctx, g, key: "ABCD", code: "K7M4QP");

        const long chat = 555_002L;
        ctx.BotUsers.Add(new BotUser { ChatId = chat, Name = "Ali" });
        ctx.TelegramRegistrations.Add(new TelegramRegistration { ChatId = chat, StudentId = s.Id });
        await ctx.SaveChangesAsync();

        var bot = NewBot();
        // Bitta bog'langan o'quvchi — F.I.Sh so'ralmaydi, darhol test boshlanadi.
        await bot.HandleCodeAsync(ctx, chat, "K7M4QP", CancellationToken.None);
        var session = await ctx.TestBotSessions.FirstAsync(x => x.ChatId == chat);
        Assert.Equal(s.Id, session.StudentId);
        Assert.Equal("", session.Stage);

        session.Answers = "ABCD";
        await ctx.SaveChangesAsync();
        await bot.SubmitAsync(ctx, chat, CancellationToken.None);

        var score = Assert.Single(ctx.TestScores.ToList());
        Assert.Equal(s.Id, score.StudentId);
        Assert.Equal(4m, score.Score);
        Assert.Empty(ctx.ExternalTestScores.ToList());
    }
}
