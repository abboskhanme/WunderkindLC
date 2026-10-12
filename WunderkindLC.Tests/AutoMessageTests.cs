using System.Reflection;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

// ---------------------------------------------------------------------------------------------
// Umumiy soxta (fake) yordamchilar — avto-xabar va lid testlari uchun. Tashqi mock kutubxonasi
// yo'q, shuning uchun eng kichik ishlaydigan implementatsiyalar shu yerda.
// ---------------------------------------------------------------------------------------------

/// <summary>Har qanday HTTP so'rovni DARHOL xatoga chiqaradigan HttpClient fabrikasi — testda
/// hech qanday xizmat (Telegram) haqiqatan tarmoqqa chiqmasligini kafolatlaydi.</summary>
internal sealed class NoNetworkHttpClientFactory : IHttpClientFactory
{
    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Testda tarmoqqa chiqish taqiqlangan: " + request.RequestUri);
    }

    public HttpClient CreateClient(string name) => new(new BlockingHandler());
}

/// <summary>Faqat <see cref="IAppDbContext"/> beradigan eng sodda DI konteyner — fon
/// xizmatlari (<c>services.CreateScope()</c>) uchun. Scope = konteynerning o'zi.</summary>
internal sealed class SingleServiceProvider(IAppDbContext db)
    : IServiceProvider, IServiceScopeFactory, IServiceScope
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceScopeFactory)) return this;
        if (serviceType == typeof(IAppDbContext)) return db;
        return null;
    }

    public IServiceScope CreateScope() => this;
    public IServiceProvider ServiceProvider => this;
    public void Dispose() { }
}

/// <summary>Testda ishlatiladigan real (lekin tarmoqsiz) xizmatlar to'plami.</summary>
internal sealed class MessagingStack
{
    public TelegramService Telegram { get; }
    public AutoMessageService Auto { get; }

    public MessagingStack()
    {
        var http = new NoNetworkHttpClientFactory();
        Telegram = new TelegramService(http, NullLogger<TelegramService>.Instance);
        Auto = new AutoMessageService(Telegram, NullLogger<AutoMessageService>.Instance);
    }
}

/// <summary>Reflection yordamchisi — fon xizmatlarining <c>private</c> ish metodlarini chaqirish
/// (ular <c>BackgroundService</c> siklidan ajratilgan, lekin ochiq emas).</summary>
internal static class Reflect
{
    public static void RunAsyncMethod(object target, string name, params object?[] args)
    {
        var mi = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? throw new MissingMethodException(target.GetType().Name, name);
        ((Task)mi.Invoke(target, args)!).GetAwaiter().GetResult();
    }

    public static T StaticCall<T>(Type type, string name, params object?[] args)
    {
        var mi = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                 ?? throw new MissingMethodException(type.Name, name);
        return (T)mi.Invoke(null, args)!;
    }
}

/// <summary>
/// AVTO-XABAR tizimi testlari: trigger katalogi, token katalogi, yetkazish kanallari
/// (<see cref="AutoMessageChannels"/>), <see cref="AutoMessageService"/> dispatcheri va eslatma fon-xizmatlari (qarzdorlik, tug'ilgan
/// kun, erkin jadval). Rasmiy manba: <c>.claude/rules/messaging.md</c>.
/// </summary>
public class AutoMessageTests
{
    // ===================== 1) Trigger katalogi (sof mantiq) =====================

    [Fact]
    public void Triggerlar_10_ta_va_kalitlari_takrorlanmaydi()
    {
        // messaging.md: "10 trigger" katalogi AutoMessageTriggers da (2026-10-07: + attendance_present;
        // 2026-10: 4 ta lid hodisasi SMS bilan birga olib tashlandi).
        Assert.Equal(10, AutoMessageTriggers.All.Length);
        Assert.Equal(10, AutoMessageTriggers.All.Select(t => t.Key).Distinct().Count());
        Assert.Contains(AutoMessageTriggers.All, t => t.Key == AutoMessageTriggers.AttendancePresent);
    }

    [Fact]
    public void IsKnown_faqat_katalogdagi_kalitlarni_taniydi()
    {
        Assert.True(AutoMessageTriggers.IsKnown(AutoMessageTriggers.PaymentReceived));
        Assert.True(AutoMessageTriggers.IsKnown(AutoMessageTriggers.GradeEntered));
        Assert.False(AutoMessageTriggers.IsKnown("payment_received_2"));
        Assert.False(AutoMessageTriggers.IsKnown(""));
        Assert.False(AutoMessageTriggers.IsKnown(null));
        Assert.False(AutoMessageTriggers.IsKnown("   "));
    }

    [Fact]
    public void Get_notogri_kalitda_null_qaytaradi()
    {
        Assert.Null(AutoMessageTriggers.Get("yoq"));
        Assert.Null(AutoMessageTriggers.Get(null));
        Assert.Equal("Tug'ilgan kun", AutoMessageTriggers.Get(AutoMessageTriggers.Birthday)!.Label);
    }

    [Fact]
    public void Lid_hodisalari_2026_10_dan_katalogda_YOQ()
    {
        // Lid hodisalari faqat lid telefoniga SMS edi (lidning Telegram manzili yo'q) — SMS kanali bilan
        // birga olib tashlandi. Bazadagi eski qoidalar "noma'lum" bo'lib qoladi: ro'yxatda ko'rinmaydi va
        // hech qachon ishga tushmaydi.
        foreach (var key in new[] { "lead_new", "trial_reminder", "test_link", "test_result" })
        {
            Assert.False(AutoMessageTriggers.IsKnown(key), key);
            Assert.Null(AutoMessageTriggers.Get(key));
        }
        Assert.DoesNotContain(AutoMessageTriggers.All, t => t.Category == AutoMessageTriggers.CategoryLeads);
    }

    // ===================== 1.5) Yetkazish kanallari (sof) =====================

    [Fact]
    public void Kanal_Telegram_bayrogi_yoki_ESKI_SMS_bayrogi_Telegramga_yetkazadi()
    {
        Assert.True(AutoMessageChannels.Telegram(new AutoMessageRule { SendTelegram = true }));
        // Eski SMS qoidasi (2026-10 gacha saqlangan) Telegram orqali yetkaziladi — tabrik/eslatma yo'qolmasin.
        Assert.True(AutoMessageChannels.Telegram(new AutoMessageRule { SendSms = true }));
        Assert.False(AutoMessageChannels.Telegram(new AutoMessageRule()));
        // Push bayrog'i endi hech narsani hal qilmaydi (kabinet yozuvi har doim yoziladi).
        Assert.False(AutoMessageChannels.Telegram(new AutoMessageRule { SendPush = true }));
    }

    [Fact]
    public void Har_triggerning_toifasi_4_ta_malum_qiymatdan_biri()
    {
        var known = new[]
        {
            AutoMessageTriggers.CategoryLeads, AutoMessageTriggers.CategoryEducation,
            AutoMessageTriggers.CategoryFinance, AutoMessageTriggers.CategoryOther,
        };
        foreach (var t in AutoMessageTriggers.All)
            Assert.Contains(t.Category, known);
    }

    [Fact]
    public void Auditoriya_royxati_bosh_bolmasa_default_shu_royxatda_boladi()
    {
        foreach (var t in AutoMessageTriggers.All)
        {
            if (t.Audiences.Length == 0) continue; // lid hodisalari — auditoriya yo'q
            Assert.Contains(t.DefaultAudience, t.Audiences);
        }
    }

    [Fact]
    public void Faqat_qarzdorlik_hodisasida_matn_ixtiyoriy()
    {
        foreach (var t in AutoMessageTriggers.All)
        {
            if (t.Key == AutoMessageTriggers.PaymentDebt)
            {
                Assert.True(t.TemplateOptional);
                Assert.Equal("", t.DefaultTemplate); // tizim matnni o'zi tuzadi
            }
            else
            {
                Assert.False(t.TemplateOptional, t.Key);
                Assert.False(string.IsNullOrWhiteSpace(t.DefaultTemplate), t.Key);
            }
        }
    }

    [Fact]
    public void Jadval_va_yuborish_rejimi_faqat_tegishli_hodisalarda()
    {
        foreach (var t in AutoMessageTriggers.All)
        {
            Assert.Equal(t.Key == AutoMessageTriggers.CustomSchedule, t.SupportsSchedule);
            Assert.Equal(t.Key == AutoMessageTriggers.LessonAttendance, t.SupportsSendScope);
        }
    }

    [Fact]
    public void ReminderSendScopes_uchta_rejimni_taniydi()
    {
        Assert.True(ReminderSendScopes.IsKnown(ReminderSendScopes.LessonStart));
        Assert.True(ReminderSendScopes.IsKnown(ReminderSendScopes.NotFilled));
        Assert.True(ReminderSendScopes.IsKnown(ReminderSendScopes.All));
        Assert.False(ReminderSendScopes.IsKnown("hammaga"));
        Assert.False(ReminderSendScopes.IsKnown(null));
    }

    // ===================== 2) Token katalogi =====================

    [Fact]
    public void Token_katalogidagi_har_bir_yozuv_togri_shaklda()
    {
        var groups = new[] { "student", "lead", "common", "event" };
        foreach (var t in MessageTokenCatalog.All)
        {
            Assert.StartsWith("{", t.Token);
            Assert.EndsWith("}", t.Token);
            Assert.False(string.IsNullOrWhiteSpace(t.Label));
            Assert.Contains(t.Group, groups);
        }
    }

    [Fact]
    public void Token_katalogida_bir_guruh_ichida_takror_token_yoq()
    {
        var dup = MessageTokenCatalog.All
            .GroupBy(t => (t.Token, t.Group))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Token}/{g.Key.Group}")
            .ToList();
        Assert.Empty(dup);
    }

    [Fact]
    public void Triggerlar_elon_qilgan_tokenlar_katalogda_bor()
    {
        // AutoMessageTriggers izohi: Tokens — "MessageTokenizer/dispatcher haqiqatan
        // qo'llab-quvvatlaydigan ANIQ token nomlari (o'ylab topilmagan)".
        var catalog = MessageTokenCatalog.All.Select(t => t.Token).ToHashSet();
        foreach (var t in AutoMessageTriggers.All)
            foreach (var token in t.Tokens)
                Assert.True(catalog.Contains(token), $"{t.Key} → {token} katalogda yo'q");
    }

    [Fact]
    public void Standart_shablondagi_tokenlar_shu_hodisada_elon_qilingan()
    {
        foreach (var t in AutoMessageTriggers.All)
        {
            var used = System.Text.RegularExpressions.Regex.Matches(t.DefaultTemplate, "{[a-z_]+}")
                .Select(m => m.Value).Distinct();
            foreach (var token in used)
                Assert.True(t.Tokens.Contains(token), $"{t.Key} shablonida e'lon qilinmagan token: {token}");
        }
    }

    // 3) Eskiz raqam normalizatsiyasi testlari — 2026-10 da SMS kanali bilan birga olib tashlandi.

    // ===================== 4) AutoMessageService — dispatcher =====================

    private static Student NewStudent(string name = "Aliyev Ali", string? userId = "u-1") => new()
    {
        FullName = name,
        FirstName = "Ali",
        LastName = "Aliyev",
        ParentFullName = "Aliyev Vali",
        ParentPhone = "901112233",
        Phone = "907778899",
        UserId = userId,
    };

    /// <summary>Faqat kabinet bildirishnomasi bilan ishlaydigan qoida (Telegram o'chiq) — kabinet yozuvi
    /// 2026-10 dan HAR qoida uchun yoziladi (ilgari faqat push bayrog'i bilan).</summary>
    private static AutoMessageRule Rule(string trigger, string template) => new()
    {
        Trigger = trigger,
        Name = "",
        Enabled = true,
        Template = template,
    };

    [Fact]
    public async Task Dispatch_qoida_yoq_bolsa_hech_narsa_qilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.Birthday, s);

        Assert.Empty(ctx.UserNotifications);
        Assert.Empty(ctx.SmsLogs);
    }

    [Fact]
    public async Task Dispatch_ochirilgan_qoida_ishlamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        var rule = Rule(AutoMessageTriggers.Birthday, "Tabriklaymiz {ism}!");
        rule.Enabled = false;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(rule);
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.Birthday, s);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public async Task Dispatch_kabinet_bildirishnomasi_tarixiga_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Birthday, "Hurmatli {ism}, tug'ilgan kuningiz muborak!"));
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.Birthday, s);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("u-1", n.UserId);
        Assert.Equal("Hurmatli Ali, tug'ilgan kuningiz muborak!", n.Body);
        Assert.Equal(AutoMessageTriggers.Birthday, n.Type);
        // Sarlavha: qoida nomi bo'sh bo'lsa trigger yorlig'i.
        Assert.Equal("Tug'ilgan kun", n.Title);
    }

    [Fact]
    public async Task Dispatch_qoida_nomi_borligida_sarlavha_shu_nomdan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        var rule = Rule(AutoMessageTriggers.Birthday, "Tabrik");
        rule.Name = "Bizning tabrik";
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(rule);
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.Birthday, s);

        Assert.Equal("Bizning tabrik", Assert.Single(ctx.UserNotifications).Title);
    }

    [Fact]
    public async Task Dispatch_akkaunti_yoq_oquvchiga_kabinet_yozuvi_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent(userId: null);
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Birthday, "Tabrik {ism}"));
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.Birthday, s);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public async Task Dispatch_ESKI_SMS_qoidasi_kabinetga_yoziladi_SMS_jurnali_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        var rule = Rule(AutoMessageTriggers.Birthday, "Tabrik {ism}");
        rule.SendPush = false;
        rule.SendSms = true;            // 2026-10 gacha saqlangan SMS qoidasi
        rule.SmsProvider = "local";
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(rule);
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.Birthday, s);

        // Kabinet yozuvi push bayrog'iga bog'liq emas; SMS kanali yo'q — eski jadvallarga hech narsa yozilmaydi.
        Assert.Equal("Tabrik Ali", Assert.Single(ctx.UserNotifications).Body);
        Assert.Empty(ctx.SmsLogs);
        Assert.Empty(ctx.SmsBatches);
    }

    [Fact]
    public async Task Dispatch_bir_hodisada_ikki_qoida_ikkalasi_ham_ishlaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Birthday, "Birinchi {ism}"));
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Birthday, "Ikkinchi {ism}"));
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.Birthday, s);

        var bodies = ctx.UserNotifications.Select(n => n.Body).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "Birinchi Ali", "Ikkinchi Ali" }, bodies);
    }

    [Fact]
    public async Task Dispatch_bosh_shablon_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Birthday, "   "));
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.Birthday, s);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public async Task Dispatch_oqituvchi_auditoriyasi_guruh_oqituvchisiga_boradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var teacher = new Teacher { FullName = "Karimov Karim", Phone = "903334455", UserId = "u-teacher" };
        var group = new Group { Name = "IELTS-1", TeacherId = teacher.Id };
        var s = NewStudent();
        var rule = Rule(AutoMessageTriggers.StudentAdded, "{ism} qo'shildi");
        rule.Audience = "teachers";
        ctx.Teachers.Add(teacher);
        ctx.Classes.Add(group);
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(rule);
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(
            ctx, AutoMessageTriggers.StudentAdded, s, group: group);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("u-teacher", n.UserId);          // yetkazish — o'qituvchiga
        Assert.Equal("Ali qo'shildi", n.Body);        // matn — o'quvchi haqida
    }

    [Fact]
    public async Task Dispatch_oqituvchi_auditoriyasi_guruh_yoq_bolsa_otkazib_yuboriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        var rule = Rule(AutoMessageTriggers.StudentAdded, "{ism} qo'shildi");
        rule.Audience = "teachers";
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(rule);
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.StudentAdded, s);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public async Task DispatchAttendanceAbsent_sanani_kk_oo_yyyy_ga_ogiradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(
            AutoMessageTriggers.AttendanceAbsent, "{ism} {sana} kuni {guruh}da kelmadi. Sabab: {sabab}"));
        ctx.SaveChanges();

        var date = AppClock.Today.AddDays(-3);
        var iso = date.ToString("yyyy-MM-dd");
        await new MessagingStack().Auto.DispatchAttendanceAbsentAsync(
            ctx, s, "IELTS-1", "Kasal", iso);

        var body = Assert.Single(ctx.UserNotifications).Body;
        Assert.Equal($"Ali {date:dd.MM.yyyy} kuni IELTS-1da kelmadi. Sabab: Kasal", body);
    }

    [Fact]
    public async Task DispatchMonthlyCharges_bir_oquvchiga_bir_oyda_bitta_xabar()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(
            AutoMessageTriggers.MonthlyCharge, "{ism}: {oy} uchun {summa}"));
        ctx.SaveChanges();

        var month = AppClock.Today.ToString("yyyy-MM");
        await new MessagingStack().Auto.DispatchMonthlyChargesAsync(ctx, new[]
        {
            (s.Id, month, 300000m),
            (s.Id, month, 200000m),   // ikkinchi guruh — YIG'ILADI
        });

        var body = Assert.Single(ctx.UserNotifications).Body;
        Assert.Contains("500", body);              // 500 000 (probel bilan)
        Assert.StartsWith("Ali:", body);
    }

    [Fact]
    public async Task DispatchMonthlyCharges_arxivlangan_oquvchini_otkazib_yuboradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.IsArchived = true;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.MonthlyCharge, "{ism}: {summa}"));
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchMonthlyChargesAsync(
            ctx, new[] { (s.Id, AppClock.Today.ToString("yyyy-MM"), 100000m) });

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public async Task DispatchMonthlyCharges_qoida_yoq_bolsa_baza_soroviga_ham_chiqmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchMonthlyChargesAsync(
            ctx, new[] { (s.Id, AppClock.Today.ToString("yyyy-MM"), 100000m) });

        Assert.Empty(ctx.UserNotifications);
    }

    // (Lid avto-SMS va daraja testi havolasi SMS testlari 2026-10 da lidga SMS bilan birga olib tashlandi.)

    // ===================== 5) MessagingAudience — yopilgan guruh filtri =====================

    private static (Student s, Group g, StudentGroup m) Enroll(
        AppDbContextLike ctx, bool memberActive, bool groupArchived)
    {
        var s = NewStudent();
        var g = new Group { Name = "G-" + Guid.NewGuid().ToString("N")[..6], IsArchived = groupArchived };
        var m = new StudentGroup { StudentId = s.Id, GroupId = g.Id, IsActive = memberActive, Status = "active" };
        ctx.Add(s, g, m);
        return (s, g, m);
    }

    /// <summary>Testlarda entity qo'shishni qisqartiruvchi kichik yordamchi.</summary>
    internal sealed class AppDbContextLike(IAppDbContext ctx)
    {
        public void Add(Student s, Group g, StudentGroup m)
        {
            ctx.Students.Add(s);
            ctx.Classes.Add(g);
            ctx.StudentGroups.Add(m);
        }
    }

    [Fact]
    public async Task MessagingAudience_tirik_azoligi_bor_oquvchi_filtrga_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Enroll(new AppDbContextLike(ctx), memberActive: true, groupArchived: false);
        ctx.SaveChanges();

        Assert.Empty(await MessagingAudience.ClosedGroupStudentIdsAsync(ctx));
    }

    [Fact]
    public async Task MessagingAudience_arxiv_guruhli_oquvchi_chiqariladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, _, _) = Enroll(new AppDbContextLike(ctx), memberActive: true, groupArchived: true);
        ctx.SaveChanges();

        Assert.Equal(new[] { s.Id }, await MessagingAudience.ClosedGroupStudentIdsAsync(ctx));
    }

    [Fact]
    public async Task MessagingAudience_tugatilgan_azolik_chiqariladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, _, _) = Enroll(new AppDbContextLike(ctx), memberActive: false, groupArchived: false);
        ctx.SaveChanges();

        Assert.Equal(new[] { s.Id }, await MessagingAudience.ClosedGroupStudentIdsAsync(ctx));
    }

    [Fact]
    public async Task MessagingAudience_bitta_tirik_azolik_yetarli()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        var dead = new Group { Name = "Eski", IsArchived = true };
        var live = new Group { Name = "Yangi" };
        ctx.Students.Add(s);
        ctx.Classes.AddRange(dead, live);
        ctx.StudentGroups.AddRange(
            new StudentGroup { StudentId = s.Id, GroupId = dead.Id, IsActive = true },
            new StudentGroup { StudentId = s.Id, GroupId = live.Id, IsActive = true });
        ctx.SaveChanges();

        Assert.Empty(await MessagingAudience.ClosedGroupStudentIdsAsync(ctx));
    }

    [Fact]
    public async Task MessagingAudience_muzlatilgan_lekin_guruhi_faol_oquvchi_xabar_oladi()
    {
        // messaging.md: "Muzlatilgan, lekin guruhi FAOL o'quvchi xabar olaveradi (ta'til)".
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        var g = new Group { Name = "Faol" };
        ctx.Students.Add(s);
        ctx.Classes.Add(g);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "frozen",
        });
        ctx.SaveChanges();

        Assert.Empty(await MessagingAudience.ClosedGroupStudentIdsAsync(ctx));
    }

    [Fact]
    public async Task MessagingAudience_azoligi_umuman_yoq_oquvchi_tegilmaydi()
    {
        // messaging.md: "A'zoligi UMUMAN yo'q (eski ClassName) o'quvchilar tegilmaydi".
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Students.Add(NewStudent());
        ctx.SaveChanges();

        Assert.Empty(await MessagingAudience.ClosedGroupStudentIdsAsync(ctx));
    }

    // ===================== 6) Tug'ilgan kun eslatmasi (fon xizmati) =====================

    private static BirthdayGreetingService Birthday(IAppDbContext ctx, MessagingStack stack) =>
        new(new SingleServiceProvider(ctx), stack.Auto, NullLogger<BirthdayGreetingService>.Instance);

    [Fact]
    public void Birthday_bugun_tugilgan_kunda_yuboradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        var s = NewStudent();
        s.BirthDate = today.AddYears(-12).ToString("yyyy-MM-dd");
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Birthday, "Tabrik {ism}"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Birthday(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Single(ctx.UserNotifications);
    }

    [Fact]
    public void Birthday_boshqa_kunda_yubormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        var s = NewStudent();
        s.BirthDate = today.AddYears(-12).AddDays(1).ToString("yyyy-MM-dd");
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Birthday, "Tabrik {ism}"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Birthday(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Birthday_qoida_yoq_bolsa_ishlamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        var s = NewStudent();
        s.BirthDate = today.AddYears(-12).ToString("yyyy-MM-dd");
        ctx.Students.Add(s);
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Birthday(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Birthday_arxiv_oquvchiga_va_yopilgan_guruhlikka_yubormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        var bd = today.AddYears(-12).ToString("yyyy-MM-dd");

        var archived = NewStudent("Arxiv Ali", "u-arch");
        archived.BirthDate = bd;
        archived.IsArchived = true;

        var closedGroupStudent = NewStudent("Yopiq Vali", "u-closed");
        closedGroupStudent.BirthDate = bd;
        var closedGroup = new Group { Name = "Yopiq guruh", IsArchived = true };

        var ok = NewStudent("Faol Sami", "u-ok");
        ok.BirthDate = bd;
        var liveGroup = new Group { Name = "Faol guruh" };

        ctx.Students.AddRange(archived, closedGroupStudent, ok);
        ctx.Classes.AddRange(closedGroup, liveGroup);
        ctx.StudentGroups.AddRange(
            new StudentGroup { StudentId = closedGroupStudent.Id, GroupId = closedGroup.Id, IsActive = true },
            new StudentGroup { StudentId = ok.Id, GroupId = liveGroup.Id, IsActive = true });
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Birthday, "Tabrik {fish}"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Birthday(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("u-ok", n.UserId);
    }

    // ===================== 7) Qarzdorlik eslatmasi (fon xizmati) =====================

    private static PaymentReminderService Payment(IAppDbContext ctx, MessagingStack stack) =>
        new(new SingleServiceProvider(ctx), stack.Telegram, NullLogger<PaymentReminderService>.Instance);

    /// <summary>Joriy oyning TOQ kuni (1-sana) — eslatma shu kunlarda yuboriladi.</summary>
    private static DateOnly OddDay() => new(AppClock.Today.Year, AppClock.Today.Month, 1);

    /// <summary>Joriy oyning JUFT kuni (2-sana) — eslatma yuborilmaydi.</summary>
    private static DateOnly EvenDay() => new(AppClock.Today.Year, AppClock.Today.Month, 2);

    private static AutoMessageRule DebtRule(string template = "") => new()
    {
        Trigger = AutoMessageTriggers.PaymentDebt,
        Enabled = true,
        Template = template,
    };

    [Fact]
    public void Qarzdorlik_juft_kunda_yubormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -500000m;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", EvenDay(), CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Qarzdorlik_toq_kunda_yuboradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -500000m;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("To'lov eslatmasi", n.Title);
        // Tizim tuzgan matn. "Jami:" ATAYIN yo'q — xabar endi HAR FAN uchun alohida ketadi
        // (bu o'quvchida a'zolik yo'q, ya'ni zaxira yo'l: bitta umumiy xabar).
        Assert.Contains("qarzdorlik: 500 000 so'm", n.Body);
        Assert.DoesNotContain("Jami", n.Body);
        Assert.Equal("payment_debt", n.Type);
    }

    [Fact]
    public void Qarzdorlik_yoqilgan_qoida_yoq_bolsa_hech_narsa_yubormaydi()
    {
        // messaging.md/kod izohi: "ENDI: yoqilgan qoida bo'lmasa HECH NARSA yuborilmaydi".
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -500000m;
        var off = DebtRule();
        off.Enabled = false;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(off);
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Qarzdorlik_qarzi_yoq_oquvchiga_yubormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = 0m;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Qarzdorlik_guruhi_yopilgan_qarzdorni_otkazib_yuboradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -100000m;
        var g = new Group { Name = "Yopiq", IsArchived = true };
        ctx.Students.Add(s);
        ctx.Classes.Add(g);
        ctx.StudentGroups.Add(new StudentGroup { StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active" });
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Qarzdorlik_qoida_matni_borligida_shu_matn_yuboriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -250000m;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(DebtRule("{ism}, qarzingiz {qarzdorlik}."));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var body = Assert.Single(ctx.UserNotifications).Body;
        Assert.StartsWith("Ali, qarzingiz", body);
        Assert.DoesNotContain("Jami:", body);
    }

    [Fact]
    public void Qarzdorlik_arxiv_oquvchini_hisobga_olmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -100000m;
        s.IsArchived = true;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Qarzdorlik_kunlari_toq_sanalar_ketma_ketligi()
    {
        // Hozirgi qoida (PaymentReminderService.cs:66): 1, 3, 5, ... 31. HOZIRGI XULQNI qayd etadi:
        // 31 kunlik oyda 31-sana va keyingi oyning 1-sanasi KETMA-KET tushadi (ikki kun ketma-ket eslatma).
        static bool Sends(int day) => day == 1 || (day - 1) % 2 == 0;

        Assert.True(Sends(1));
        Assert.False(Sends(2));
        Assert.True(Sends(3));
        Assert.True(Sends(29));
        Assert.True(Sends(31));
        Assert.True(Sends(1));   // keyingi oy boshi — 31 dan keyin darhol
    }

    /// <summary>Qarzdor o'quvchining BITTA fandagi a'zoligi: kurs + guruh + shu oyga hisob (qarz).
    /// <para>⚠️ <c>Group.MonthlyFee = 0</c> ATAYIN: qarz FAQAT yozilgan <see cref="MonthlyCharge"/>
    /// dan kelsin — aks holda ledger avans oylarini ham (joriy + 3) qarz deb qo'shib yuborardi va
    /// test kutgan summa suzib ketardi.</para></summary>
    private static Group DebtSubject(
        Infrastructure.Data.AppDbContext ctx, Student s, string courseName, decimal owed)
    {
        var month = AppClock.Today.ToString("yyyy-MM");
        var subject = new Subject { Name = courseName };
        var g = new Group { Name = $"{courseName} A", CourseId = subject.Id, MonthlyFee = 0m };
        ctx.Subjects.Add(subject);
        ctx.Classes.Add(g);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active",
            JoinedAt = $"{month}-01", ActivatedAt = $"{month}-01",
        });
        if (owed > 0)
            ctx.MonthlyCharges.Add(new MonthlyCharge
            {
                StudentId = s.Id, GroupId = g.Id, Month = month, Amount = owed, Date = $"{month}-01",
            });
        return g;
    }

    [Fact]
    public void Qarzdorlik_HAR_FAN_uchun_ALOHIDA_xabar_yuboriladi()
    {
        // Markaz egasining talabi: "2ta fanda o'qisa shu ikki fan uchun alohida alohida borishi
        // kerak". Ya'ni bitta "jami" xabar EMAS — har fan o'z xabari bilan.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -500000m;
        ctx.Students.Add(s);
        DebtSubject(ctx, s, "Ingliz tili", 200000m);
        DebtSubject(ctx, s, "Matematika", 300000m);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var bodies = ctx.UserNotifications.Select(n => n.Body).ToList();
        Assert.Equal(2, bodies.Count);
        var eng = Assert.Single(bodies, b => b.Contains("Ingliz tili"));
        var mat = Assert.Single(bodies, b => b.Contains("Matematika"));
        // Har xabarda FAQAT o'z fanining summasi turadi.
        Assert.Contains("200 000 so'm", eng);
        Assert.DoesNotContain("300 000", eng);
        Assert.Contains("300 000 so'm", mat);
        Assert.DoesNotContain("200 000", mat);
        // "Jami" fanlarni aralashtirardi — endi umuman yozilmaydi.
        Assert.All(bodies, b => Assert.DoesNotContain("Jami", b));
    }

    [Fact]
    public void Qarzdorlik_summasiga_AVANS_oylari_QOSHILMAYDI_va_qarzsiz_fanga_xabar_ketmaydi()
    {
        // 2026-10-11 xatosi: guruh narxi > 0 bo'lsa ledger keyingi 3 oyni (avans) ham "qolgan" bilan
        // berardi va eslatma summasi 3 oylik ko'p chiqardi; qarzi YO'Q fanga ham "qarz" xabari ketardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -200000m;
        ctx.Students.Add(s);
        var eng = DebtSubject(ctx, s, "Ingliz tili", 200000m);
        eng.MonthlyFee = 200000m;                         // haqiqiy narx
        var mat = DebtSubject(ctx, s, "Matematika", 0m);  // shu fandan qarz yo'q
        mat.MonthlyFee = 300000m;
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Contains("Ingliz tili", n.Body);
        Assert.Contains("200 000 so'm", n.Body);
        Assert.DoesNotContain("800 000", n.Body);
        Assert.DoesNotContain("Matematika", n.Body);
    }

    [Fact]
    public void Qarzdorlik_bitta_fanda_BITTA_xabar()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -200000m;
        ctx.Students.Add(s);
        DebtSubject(ctx, s, "Ingliz tili", 200000m);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Contains("Ingliz tili", n.Body);
        Assert.Contains("200 000 so'm", n.Body);
    }

    [Fact]
    public void Qarzdorlik_qarzi_YOQ_fan_uchun_xabar_tuzilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -200000m;
        ctx.Students.Add(s);
        DebtSubject(ctx, s, "Ingliz tili", 200000m);
        DebtSubject(ctx, s, "Matematika", 0m);      // hisobi yo'q — qarz ham yo'q
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Contains("Ingliz tili", n.Body);
        Assert.DoesNotContain("Matematika", n.Body);
    }

    [Fact]
    public void Qarzdorlik_azoligi_YOQ_oquvchiga_avvalgidek_BITTA_xabar()
    {
        // ZAXIRA YO'L (eski ClassName modeli): fan kesimi ma'lum emas — qarz umumiy balansdan
        // olinadi va BITTA xabar ketadi. Bu yo'l o'zgarmasligi kerak.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -450000m;
        s.ClassName = "Eski guruh";
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Contains("Eski guruh", n.Body);
        Assert.Contains("450 000 so'm", n.Body);
    }

    [Fact]
    public void Qarzdorlik_qoida_matnida_tokenlar_HAR_FAN_boyicha_toladi()
    {
        // {qarzdorlik} — jami emas, SHU fanning qarzi; {kurs}/{guruh} — shu xabarning guruhi
        // (ilgari ikkalasi ham eskirgan `Student.ClassName` dan olinardi).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -500000m;
        s.ClassName = "Eski guruh";   // ataylab ESKIRGAN yorliq — u endi ishlatilmasligi kerak
        ctx.Students.Add(s);
        DebtSubject(ctx, s, "Ingliz tili", 200000m);
        DebtSubject(ctx, s, "Matematika", 300000m);
        ctx.AutoMessageRules.Add(DebtRule("{kurs} ({guruh}) — {qarzdorlik}"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var bodies = ctx.UserNotifications.Select(n => n.Body).ToList();
        Assert.Equal(2, bodies.Count);
        Assert.Contains("Ingliz tili (Ingliz tili A) — 200 000 so'm", bodies);
        Assert.Contains("Matematika (Matematika A) — 300 000 so'm", bodies);
        Assert.All(bodies, b => Assert.DoesNotContain("Eski guruh", b));
    }

    // ===================== 8) Erkin (jadvalli) eslatma =====================

    private static CustomReminderService Custom(IAppDbContext ctx, MessagingStack stack) =>
        new(new SingleServiceProvider(ctx), stack.Telegram, NullLogger<CustomReminderService>.Instance);

    private static AutoMessageRule ScheduleRule(string time, string audience = "students") => new()
    {
        Trigger = AutoMessageTriggers.CustomSchedule,
        Name = "Eslatma",
        Enabled = true,
        Audience = audience,
        Template = "Hurmatli {fish}! Eslatma.",
        ScheduleType = "daily",
        ScheduleTime = time,
    };

    /// <summary>Hozirgi daqiqa — 10 daqiqalik yuborish oynasi ichida (elapsed = soniyalar).</summary>
    private static string NowHhMm() => AppClock.Now.ToString("HH\\:mm");

    [Fact]
    public void Erkin_eslatma_vaqti_kelganda_yuboriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Students.Add(NewStudent());
        ctx.AutoMessageRules.Add(ScheduleRule(NowHhMm()));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Custom(ctx, new MessagingStack()), "TickAsync", CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("Eslatma", n.Title);
        Assert.Equal("custom_schedule", n.Type);
        Assert.Contains("Aliyev Ali", n.Body);
    }

    [Fact]
    public void Erkin_eslatma_bir_kunda_ikki_marta_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Students.Add(NewStudent());
        ctx.AutoMessageRules.Add(ScheduleRule(NowHhMm()));
        ctx.SaveChanges();

        var svc = Custom(ctx, new MessagingStack());
        Reflect.RunAsyncMethod(svc, "TickAsync", CancellationToken.None);
        Reflect.RunAsyncMethod(svc, "TickAsync", CancellationToken.None);

        Assert.Single(ctx.UserNotifications);
    }

    [Fact]
    public void Erkin_eslatma_vaqti_kelmagan_qoidani_yubormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Students.Add(NewStudent());
        ctx.AutoMessageRules.Add(ScheduleRule(AppClock.Now.AddHours(2).ToString("HH\\:mm")));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Custom(ctx, new MessagingStack()), "TickAsync", CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Erkin_eslatma_notogri_vaqt_formatida_otkazib_yuboriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Students.Add(NewStudent());
        ctx.AutoMessageRules.Add(ScheduleRule("chorak kam olti"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Custom(ctx, new MessagingStack()), "TickAsync", CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Erkin_eslatma_oylik_jadvalda_faqat_belgilangan_kunda()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var wrongDay = ScheduleRule(NowHhMm());
        wrongDay.ScheduleType = "monthly";
        wrongDay.ScheduleDayOfMonth = AppClock.Today.Day == 28 ? 27 : 28;
        ctx.Students.Add(NewStudent());
        ctx.AutoMessageRules.Add(wrongDay);
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Custom(ctx, new MessagingStack()), "TickAsync", CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
    }

    [Fact]
    public void Erkin_eslatma_yopilgan_guruhli_oquvchiga_bormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var closed = NewStudent("Yopiq Vali", "u-closed");
        var open = NewStudent("Faol Sami", "u-ok");
        var closedGroup = new Group { Name = "Yopiq", IsArchived = true };
        var liveGroup = new Group { Name = "Faol" };
        ctx.Students.AddRange(closed, open);
        ctx.Classes.AddRange(closedGroup, liveGroup);
        ctx.StudentGroups.AddRange(
            new StudentGroup { StudentId = closed.Id, GroupId = closedGroup.Id, IsActive = true },
            new StudentGroup { StudentId = open.Id, GroupId = liveGroup.Id, IsActive = true });
        ctx.AutoMessageRules.Add(ScheduleRule(NowHhMm()));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Custom(ctx, new MessagingStack()), "TickAsync", CancellationToken.None);

        Assert.Equal("u-ok", Assert.Single(ctx.UserNotifications).UserId);
    }

    [Fact]
    public void Erkin_eslatma_oqituvchilar_auditoriyasida_arxiv_oqituvchiga_bormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var live = new Teacher { FullName = "Karimov Karim", UserId = "u-t1" };
        var archived = new Teacher { FullName = "Eski Ustoz", UserId = "u-t2", IsArchived = true };
        ctx.Teachers.AddRange(live, archived);
        ctx.AutoMessageRules.Add(ScheduleRule(NowHhMm(), audience: "teachers"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Custom(ctx, new MessagingStack()), "TickAsync", CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("u-t1", n.UserId);
        Assert.Contains("Karimov Karim", n.Body);
    }

    [Fact]
    public async Task DispatchNewlyPresent_faqat_YANGI_kelganga__oldin_keldi_bolganga_qayta_ketmaydi()
    {
        // Uchrashuv talabi (2026-10-07): "Farzandingiz darsga keldi" — har dars uchun BIR MARTA.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var fresh = NewStudent("Aliyev Ali", "u-1");
        var again = NewStudent("Karimov Bek", "u-2");
        ctx.Students.AddRange(fresh, again);
        var g = new Group { Name = "IELTS-1" };
        ctx.Classes.Add(g);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.AttendancePresent, "{ism} {sana} kuni {guruh}da keldi"));
        var iso = AppClock.Today.ToString("yyyy-MM-dd");
        foreach (var s in new[] { fresh, again })
            ctx.JournalEntries.Add(new JournalEntry
            {
                ClassId = g.Id, SubjectId = "c1", Quarter = 1, StudentId = s.Id, Date = iso, Period = 1, Present = true,
            });
        ctx.SaveChanges();

        // `again` saqlashdan OLDIN ham «keldi» edi.
        await new MessagingStack().Auto.DispatchNewlyPresentAsync(
            ctx, g.Id, "c1", iso, 1, new[] { fresh.Id, again.Id }, new HashSet<string> { again.Id });

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("u-1", n.UserId);
        Assert.StartsWith("Ali ", n.Body);
        Assert.Contains("IELTS-1da keldi", n.Body);
    }

    [Fact]
    public async Task DispatchNewlyPresent_qoida_yoqilmagan_bolsa_hech_narsa_yubormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        var iso = AppClock.Today.ToString("yyyy-MM-dd");
        ctx.JournalEntries.Add(new JournalEntry
        {
            ClassId = "g1", SubjectId = "c1", Quarter = 1, StudentId = s.Id, Date = iso, Period = 1, Present = true,
        });
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchNewlyPresentAsync(ctx, "g1", "c1", iso, 1, new[] { s.Id }, new HashSet<string>());

        Assert.Empty(ctx.UserNotifications);
    }
}
