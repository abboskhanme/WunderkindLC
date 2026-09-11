using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM / LIDLAR testlari: lid KARTASI (<see cref="LeadNotifier"/>) va sinov darsi eslatmasi
/// (<see cref="TrialReminderService"/>). Rasmiy manba: <c>.claude/rules/crm-leads.md</c> va
/// <c>.claude/rules/messaging.md</c>.
///
/// <para><see cref="LeadNotifier"/> ning matn tuzish va oluvchi tanlash mantig'i <c>private static</c>
/// (tashqi yuborish Telegram tokeniga bog'liq — testda tarmoq YO'Q), shuning uchun refleksiya orqali
/// bevosita chaqiriladi: aynan shu ikki qism qoidaning o'zi.</para>
/// </summary>
public class LeadsTests
{
    // ===================== Refleksiya yordamchilari =====================

    private static bool ShouldNotify(AppUser u) =>
        Reflect.StaticCall<bool>(typeof(LeadNotifier), "ShouldNotify", u);

    /// <summary>
    /// ⚠️ IMZO O'ZGARDI (2026-08-22): oxirgidan oldingi parametr <c>bool isNewLead</c> emas, tayyor
    /// <c>string header</c>. Sarlavha endi chaqiruvchining bir martalik bayrog'idan EMAS, saqlangan
    /// holatdan hisoblanadi — <see cref="HeaderOf"/>. Sabab: taklif havolasi bilan topshirilgan
    /// testda <c>RepeatCount</c> oshirilmaydi, ya'ni eski kod mavjud lid kartasini «🆕 Yangi lid!»
    /// deb qayta yozar, keyingi sinxronizatsiya esa BOSHQA sarlavha chizib, matn (demak xesh)
    /// har safar o'zgarib turardi.
    /// </summary>
    private static string BuildText(
        Lead lead, LevelTestSubmission? sub = null, string? testTitle = null,
        string header = "🆕 Yangi lid!", string? createdBy = null) =>
        Reflect.StaticCall<string>(typeof(LeadNotifier), "BuildText", lead, sub, testTitle, header, createdBy);

    /// <summary>Karta SARLAVHASI — sof funksiya, faqat saqlangan ma'lumotdan.</summary>
    private static string HeaderOf(Lead lead, LevelTestSubmission? sub = null) =>
        Reflect.StaticCall<string>(typeof(LeadNotifier), "HeaderOf", lead, sub);

    private static string Trim(string text) =>
        Reflect.StaticCall<string>(typeof(LeadNotifier), "Trim", text);

    private static string Sha256Hex(string text) =>
        Reflect.StaticCall<string>(typeof(LeadNotifier), "Sha256Hex", text);

    /// <summary><c>LeadNotifier.CardParts</c> — private nested record (bazadan yig'ilgan ma'lumot).</summary>
    private static readonly Type CardPartsType =
        typeof(LeadNotifier).GetNestedType("CardParts", BindingFlags.NonPublic)
        ?? throw new MissingMemberException(nameof(LeadNotifier), "CardParts");

    /// <summary>
    /// Bitta chat uchun karta matnini quradi (<c>LeadNotifier.Render</c>) va (matn, xesh) juftini
    /// qaytaradi.
    ///
    /// <para>⚠️ <paramref name="includeNotes"/> — modulning YANGI qoidasi: SHAXSIY chatga to'liq
    /// izohlar, GURUHGA esa faqat sanoq («💬 N ta izoh»). Menejer izohi ichki, filtrsiz matn —
    /// u har bir guruhga tarqalmasligi kerak.</para>
    /// </summary>
    private static (string Text, string Hash) RenderCard(
        Lead lead, bool includeNotes = true, LevelTestSubmission? sub = null, string? testTitle = null,
        string? createdBy = null, string? stageTitle = null, TrialLesson? trial = null,
        string? trialGroupName = null, IReadOnlyList<LeadEvent>? notes = null, int? noteCount = null)
    {
        var list = notes ?? new List<LeadEvent>();
        var parts = Activator.CreateInstance(
            CardPartsType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object?[]
            {
                lead, sub, testTitle, createdBy, stageTitle, trial, trialGroupName,
                list, noteCount ?? list.Count,
            },
            culture: null)!;

        var card = Reflect.StaticCall<object>(typeof(LeadNotifier), "Render", parts, includeNotes);
        var t = card.GetType();
        return ((string)t.GetProperty("Text")!.GetValue(card)!,
                (string)t.GetProperty("Hash")!.GetValue(card)!);
    }

    private static Lead NewLead() => new()
    {
        FullName = "Yangi Lid",
        Phone = "901234567",
        Source = "Instagram",
        InterestSubject = "IELTS",
        CreatedAt = AppClock.Iso(),
    };

    // ===================== 1) Oluvchi: FAQAT superadmin =====================

    [Fact]
    public void Shaxsiy_xabarnoma_faqat_superadminga_boradi()
    {
        // messaging.md: "SHAXSIY xabar FAQAT SUPERADMIN(lar)ga + bot qo'shilgan faol GURUH(lar)ga".
        Assert.True(ShouldNotify(new AppUser { Role = Roles.SuperAdmin }));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Student)]
    [InlineData(Roles.PlatformOwner)]
    [InlineData("")]
    public void Boshqa_rollarga_shaxsiy_xabarnoma_bormaydi(string role)
    {
        Assert.False(ShouldNotify(new AppUser { Role = role }));
    }

    // ===================== 2) SARLAVHA (HeaderOf) — sof funksiya =====================
    //
    // 🔴 Sarlavha chaqiruvchining `isNewLead` bayrog'idan OLINMAYDI: u bir martalik hodisani
    // bildiradi, karta esa JONLI hujjat — keyingi har sinxronizatsiyada qayta chiziladi. Ilgari
    // yuborishda «🆕 Yangi lid!», sinxronizatsiyada esa boshqa sarlavha chiqar, matn (demak xesh)
    // sababsiz o'zgarib, har safar bekorga tahrir so'rovi ketardi.

    [Fact]
    public void Sarlavha_yangi_lidda_YANGI_deb_chiqadi()
    {
        Assert.Equal("🆕 Yangi lid!", HeaderOf(NewLead()));
    }

    [Fact]
    public void Sarlavha_takroriy_murojaatda_MAVJUD_lid_yangilandi()
    {
        var lead = NewLead();
        lead.RepeatCount = 1;
        Assert.Equal("🔁 Mavjud lid yangilandi", HeaderOf(lead));
    }

    [Fact]
    public void Sarlavha_lid_dan_KEYIN_kelgan_test_natijasini_ajratadi()
    {
        // Test lidning O'ZIDAN KEYIN yaratilgan ⇒ lid allaqachon bor edi.
        var lead = NewLead();
        lead.CreatedAt = "2026-08-01T10:00:00";
        var sub = new LevelTestSubmission { CreatedAt = "2026-08-05T09:00:00" };
        Assert.Equal("🔁 Mavjud lid — yangi test natijasi", HeaderOf(lead, sub));
    }

    [Fact]
    public void Sarlavha_lid_TESTDAN_tugilgan_bolsa_YANGI_lid_boladi()
    {
        // 🔴 CHEGARA HOLATI: lid daraja testidan tug'ilganda ikkalasining vaqti AYNAN bir xil
        // (bitta `now` dan yoziladi). Qat'iy ">" solishtiruv shu holatni "mavjud lid" deb
        // ko'rsatib qo'ymasligi kerak.
        var lead = NewLead();
        lead.CreatedAt = "2026-08-05T09:00:00";
        var sub = new LevelTestSubmission { CreatedAt = "2026-08-05T09:00:00" };
        Assert.Equal("🆕 Yangi lid!", HeaderOf(lead, sub));
    }

    [Fact]
    public void Sarlavha_ESKI_test_natijasi_lidni_mavjud_deb_belgilamaydi()
    {
        // Test lidgacha topshirilgan (import qilingan ma'lumot) — bu "yangi natija keldi" emas.
        var lead = NewLead();
        lead.CreatedAt = "2026-08-05T09:00:00";
        var sub = new LevelTestSubmission { CreatedAt = "2026-08-01T10:00:00" };
        Assert.Equal("🆕 Yangi lid!", HeaderOf(lead, sub));
    }

    [Fact]
    public void Sarlavha_test_natijasi_RepeatCount_dan_USTUN()
    {
        // Ikkalasi ham bo'lsa menejer uchun MUHIMROQ xabar — yangi test natijasi.
        var lead = NewLead();
        lead.CreatedAt = "2026-08-01T10:00:00";
        lead.RepeatCount = 3;
        var sub = new LevelTestSubmission { CreatedAt = "2026-08-05T09:00:00" };
        Assert.Equal("🔁 Mavjud lid — yangi test natijasi", HeaderOf(lead, sub));
    }

    [Fact]
    public void Sarlavha_RepeatCount_oshirilmagan_test_natijasini_ham_ushlaydi()
    {
        // 🔴 TOPILMA B4: taklif havolasi bilan topshirilgan testda `LevelTestService`
        // `RepeatCount` ni OSHIRMAYDI — ya'ni faqat `RepeatCount` ga tayangan eski qoida
        // mavjud lidning kartasini «🆕 Yangi lid!» deb qayta yozardi.
        var lead = NewLead();
        lead.CreatedAt = "2026-08-01T10:00:00";
        lead.RepeatCount = 0;
        var sub = new LevelTestSubmission { CreatedAt = "2026-08-01T10:00:01" };
        Assert.Equal("🔁 Mavjud lid — yangi test natijasi", HeaderOf(lead, sub));
    }

    // ===================== 3) Xabar matni (BuildText) =====================

    [Fact]
    public void Matn_sarlavhani_chaqiruvchidan_AYNAN_oladi()
    {
        // BuildText sarlavhani O'ZI hisoblamaydi — berilganini yozadi (qoida `HeaderOf` da).
        Assert.StartsWith("🔁 Mavjud lid — yangi test natijasi",
            BuildText(NewLead(), header: "🔁 Mavjud lid — yangi test natijasi"));
        Assert.StartsWith("🆕 Yangi lid!", BuildText(NewLead()));
    }

    [Fact]
    public void Matn_lidning_asosiy_maydonlarini_chiqaradi()
    {
        var text = BuildText(NewLead());
        Assert.Contains("👤 Yangi Lid", text);
        Assert.Contains("📞 901234567", text);
        Assert.Contains("🔖 Manba: Instagram", text);
        Assert.Contains("📚 Qiziqish: IELTS", text);
    }

    [Fact]
    public void Matn_bosh_maydonlar_uchun_qator_chiqarmaydi()
    {
        var text = BuildText(new Lead { FullName = "Faqat Ism" });
        Assert.Contains("👤 Faqat Ism", text);
        Assert.DoesNotContain("📞", text);
        Assert.DoesNotContain("🔖", text);
        Assert.DoesNotContain("📚", text);
    }

    [Fact]
    public void Matn_kim_kiritganini_eng_tagida_korsatadi()
    {
        var text = BuildText(NewLead(), createdBy: "Sayt");
        Assert.EndsWith("🧑‍💼 Kiritdi: Sayt", text);
    }

    [Fact]
    public void Matn_createdBy_bosh_bolsa_kiritdi_qatori_yoq()
    {
        Assert.DoesNotContain("Kiritdi:", BuildText(NewLead(), createdBy: "   "));
    }

    [Fact]
    public void Matn_izohni_faqat_test_natijasi_bolmaganda_qoshadi()
    {
        var lead = NewLead();
        lead.Note = "Ertaga qo'ng'iroq qilish kerak";

        Assert.Contains("📝 Ertaga qo'ng'iroq qilish kerak", BuildText(lead));

        // Test natijasi bo'lsa — izoh o'rniga natija bloki chiqadi (HOZIRGI xulq).
        var sub = new LevelTestSubmission { Score = 5, Total = 10, Percent = 50 };
        Assert.DoesNotContain("Ertaga qo'ng'iroq", BuildText(lead, sub));
    }

    [Fact]
    public void Matn_test_natijasini_ball_foiz_daraja_bilan_korsatadi()
    {
        var sub = new LevelTestSubmission { Score = 17, Total = 20, Percent = 85, Level = "B2", Age = 15 };
        var text = BuildText(NewLead(), sub, testTitle: "Ingliz tili — daraja testi");

        Assert.Contains("📊 Daraja testi natijasi", text);
        Assert.Contains("📝 Test: Ingliz tili — daraja testi", text);
        Assert.Contains("✅ Ball: 17/20 (85%)", text);
        Assert.Contains("🎯 Daraja: B2", text);
        Assert.Contains("🎂 Yoshi: 15", text);
    }

    [Theory]
    [InlineData(100, "A'lo", "🟢")]
    [InlineData(80, "A'lo", "🟢")]
    [InlineData(79, "Yaxshi", "🟢")]
    [InlineData(60, "Yaxshi", "🟢")]
    [InlineData(59, "O'rta", "🟡")]
    [InlineData(40, "O'rta", "🟡")]
    [InlineData(39, "Past", "🔴")]
    [InlineData(0, "Past", "🔴")]
    public void Matn_foizga_qarab_sifat_bahosini_beradi(int percent, string label, string icon)
    {
        var sub = new LevelTestSubmission { Score = percent, Total = 100, Percent = percent };
        var text = BuildText(NewLead(), sub);
        Assert.Contains($"{icon} Baho: {label}", text);
    }

    [Fact]
    public void Matn_savolsiz_testda_ball_ornida_izoh_korsatadi()
    {
        var sub = new LevelTestSubmission { Score = 0, Total = 0, Percent = 0 };
        var text = BuildText(NewLead(), sub);
        Assert.Contains("ℹ️ Test savolsiz (faqat so'rovnoma).", text);
        Assert.DoesNotContain("✅ Ball:", text);
    }

    [Fact]
    public void Matn_yosh_korsatilmagan_bolsa_qator_chiqmaydi()
    {
        var sub = new LevelTestSubmission { Score = 1, Total = 2, Percent = 50, Age = 0 };
        Assert.DoesNotContain("🎂", BuildText(NewLead(), sub));
    }

    [Fact]
    public void Matn_sorovnoma_javoblarini_royxat_qilib_beradi()
    {
        var survey = JsonSerializer.Serialize(new List<SurveyAnswerDto>
        {
            new("Qayerdan eshitdingiz?", new List<string> { "Instagram", "Do'stlardan" }),
            new("Qaysi vaqt qulay?", new List<string>()),
        });
        var sub = new LevelTestSubmission { Score = 1, Total = 2, Percent = 50, SurveyJson = survey };

        var text = BuildText(NewLead(), sub);

        Assert.Contains("🗒 So'rovnoma:", text);
        Assert.Contains("• Qayerdan eshitdingiz?: Instagram, Do'stlardan", text);
        Assert.Contains("• Qaysi vaqt qulay?: —", text);   // javob tanlanmagan
    }

    [Fact]
    public void Matn_buzuq_sorovnoma_JSONida_yiqilmaydi()
    {
        var sub = new LevelTestSubmission { Score = 1, Total = 2, Percent = 50, SurveyJson = "{buzuq" };
        var text = BuildText(NewLead(), sub);
        Assert.DoesNotContain("🗒 So'rovnoma:", text);
    }

    [Fact]
    public void Matn_sorovnomasiz_testda_sorovnoma_bolimi_yoq()
    {
        var sub = new LevelTestSubmission { Score = 1, Total = 2, Percent = 50, SurveyJson = "" };
        Assert.DoesNotContain("🗒", BuildText(NewLead(), sub));
    }

    [Fact]
    public void Matn_sorovnomada_javoblar_maydoni_yoq_bolsa_ham_ishlaydi()
    {
        // Ilgari "Answers" maydoni bo'lmasa `a.Answers.Count` NullReferenceException berardi,
        // `NotifyNewLeadAsync` tashqi catch'i uni yutar va BUTUN xabarnoma yo'qolardi.
        var sub = new LevelTestSubmission
        {
            Score = 1, Total = 2, Percent = 50,
            SurveyJson = "[{\"Question\":\"Savol\"}]",
        };
        Assert.Contains("• Savol: —", BuildText(NewLead(), sub));
    }

    [Fact]
    public void Matn_sorovnomada_null_element_bolsa_ham_yiqilmaydi()
    {
        // Buzuq JSON ("[null]") butun matnni yiqitmasin — element JIM tashlanadi.
        var sub = new LevelTestSubmission
        {
            Score = 1, Total = 2, Percent = 50,
            SurveyJson = "[null,{\"Question\":\"Savol\",\"Answers\":[\"Ha\"]}]",
        };
        Assert.Contains("• Savol: Ha", BuildText(NewLead(), sub));
    }

    // ===================== 4) NotifyNewLeadAsync — himoya xulqi =====================

    private static TelegramService Telegram() =>
        new(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);

    [Fact]
    public async Task Xabarnoma_bot_sozlanmagan_bolsa_jim_otadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = NewLead();
        ctx.Leads.Add(lead);
        ctx.Users.Add(new AppUser { FullName = "Bosh admin", Email = "super", Role = Roles.SuperAdmin });
        ctx.TelegramGroups.Add(new TelegramGroup { ChatId = GroupChat, IsActive = true });
        ctx.SaveChanges();

        // Token .env'da yo'q ⇒ IsConfigured=false ⇒ hech qanday so'rov ketmaydi (tarmoq bloklangan
        // fabrika bilan — agar chiqmoqchi bo'lsa test yiqilardi).
        await LeadNotifier.NotifyNewLeadAsync(ctx, Telegram(), lead);
        Assert.Empty(ctx.LeadTelegramMessages);
    }

    [Fact]
    public async Task Xabarnoma_hech_qachon_lid_yaratishni_buzmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // Ataylab nomukammal ma'lumot: lid bo'sh, ro'yxatlar bo'sh — istisno chiqmasligi kerak.
        await LeadNotifier.NotifyNewLeadAsync(ctx, Telegram(), new Lead());
    }

    [Fact]
    public void Faol_telegram_guruhlari_va_superadmin_royxatlari_ajratilgan()
    {
        // Oluvchilar to'plami: (a) UserId biriktirilgan shaxsiy registratsiyalar, (b) IsActive guruhlar.
        // Bot chiqarilgan guruh (IsActive=false) ro'yxatga TUSHMASLIGI kerak.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.TelegramGroups.AddRange(
            new TelegramGroup { ChatId = -1, IsActive = true },
            new TelegramGroup { ChatId = -2, IsActive = false });
        ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { UserId = "u-1", ChatId = 11 },
            new TelegramRegistration { UserId = "", ChatId = 12 },      // xodim emas — o'quvchi yozuvi
            new TelegramRegistration { UserId = null, ChatId = 13 });
        ctx.SaveChanges();

        Assert.Equal(new long[] { -1 }, ctx.TelegramGroups.Where(g => g.IsActive).Select(g => g.ChatId).ToArray());
        Assert.Equal(new long[] { 11 },
            ctx.TelegramRegistrations.Where(r => r.UserId != null && r.UserId != "").Select(r => r.ChatId).ToArray());
    }

    // ===================== 5) Sinov darsi eslatmasi =====================

    private static TrialReminderService Trial(IAppDbContext ctx, MessagingStack stack) =>
        new(new SingleServiceProvider(ctx), stack.Auto, NullLogger<TrialReminderService>.Instance);

    private static AutoMessageRule TrialRule(
        string template = "Salom {fish}! Ertaga {dars_sana} kuni {dars_vaqti}da sinov darsingiz bor.") => new()
    {
        Trigger = AutoMessageTriggers.TrialReminder,
        Enabled = true,
        SendSms = true,
        SmsProvider = "local",
        Template = template,
    };

    /// <summary>Local SMS yoqilgan markaz (Eskiz .env'siz — testda faqat "local" yo'li ishlaydi).</summary>
    private static void EnableLocalSms(IAppDbContext ctx) =>
        ctx.CenterMeta.Add(new CenterMeta { Name = "Wunderkind", LocalSmsEnabled = true });

    private static (Lead lead, TrialLesson trial) AddTrial(
        IAppDbContext ctx, DateOnly day, string result = "pending", string time = "15:30")
    {
        var lead = NewLead();
        var trial = new TrialLesson
        {
            LeadId = lead.Id,
            ScheduledAt = $"{day:yyyy-MM-dd}T{time}",
            Result = result,
        };
        ctx.Leads.Add(lead);
        ctx.TrialLessons.Add(trial);
        return (lead, trial);
    }

    [Fact]
    public void Sinov_eslatmasi_ertangi_darsga_yuboriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        EnableLocalSms(ctx);
        AddTrial(ctx, today.AddDays(1));
        ctx.AutoMessageRules.Add(TrialRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        var log = Assert.Single(ctx.SmsLogs);
        Assert.Contains("Salom Yangi Lid!", log.Message);
        Assert.Contains($"{today.AddDays(1):dd.MM.yyyy}", log.Message);
        Assert.Contains("15:30", log.Message);
    }

    [Fact]
    public void Sinov_eslatmasi_bugungi_darsga_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        EnableLocalSms(ctx);
        AddTrial(ctx, today);
        ctx.AutoMessageRules.Add(TrialRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.SmsLogs);
    }

    [Fact]
    public void Sinov_eslatmasi_indingi_darsga_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        EnableLocalSms(ctx);
        AddTrial(ctx, today.AddDays(2));
        ctx.AutoMessageRules.Add(TrialRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.SmsLogs);
    }

    [Theory]
    [InlineData("stayed")]
    [InlineData("left")]
    public void Sinov_eslatmasi_natijasi_belgilangan_darsga_yuborilmaydi(string result)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        EnableLocalSms(ctx);
        AddTrial(ctx, today.AddDays(1), result);
        ctx.AutoMessageRules.Add(TrialRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.SmsLogs);
    }

    [Fact]
    public void Sinov_eslatmasi_oquvchiga_aylantirilgan_lidga_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        EnableLocalSms(ctx);
        var (lead, _) = AddTrial(ctx, today.AddDays(1));
        lead.ConvertedStudentId = "s-1";
        ctx.AutoMessageRules.Add(TrialRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.SmsLogs);
    }

    [Fact]
    public void Sinov_eslatmasi_qoida_yoq_bolsa_ishlamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        EnableLocalSms(ctx);
        AddTrial(ctx, today.AddDays(1));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.SmsLogs);
    }

    [Fact]
    public void Sinov_eslatmasi_lid_topilmasa_yiqilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        EnableLocalSms(ctx);
        ctx.TrialLessons.Add(new TrialLesson
        {
            LeadId = "yoq-lid",
            ScheduledAt = $"{today.AddDays(1):yyyy-MM-dd}T10:00",
            Result = "pending",
        });
        ctx.AutoMessageRules.Add(TrialRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.SmsLogs);
    }

    [Fact]
    public void Sinov_eslatmasi_guruh_kunlarini_ham_tokenga_qoyadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        EnableLocalSms(ctx);
        var group = new Group { Name = "IELTS-1", Days = new List<int> { 0, 2, 4 } };
        var (_, trial) = AddTrial(ctx, today.AddDays(1));
        trial.GroupId = group.Id;
        ctx.Classes.Add(group);
        ctx.AutoMessageRules.Add(TrialRule("Kunlar: {dars_kunlari}"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        var log = Assert.Single(ctx.SmsLogs);
        Assert.StartsWith("Kunlar: ", log.Message);
        Assert.Contains("Du", log.Message);
    }

    [Fact]
    public void Sinov_eslatmasi_local_SMS_ochirilgan_bolsa_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        ctx.CenterMeta.Add(new CenterMeta { LocalSmsEnabled = false });
        AddTrial(ctx, today.AddDays(1));
        ctx.AutoMessageRules.Add(TrialRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Trial(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Empty(ctx.SmsLogs);
    }

    // ===================== 6) LID KARTASI (guruhdagi xabar TAHRIRLANADI) =====================
    //
    // Rasmiy manba: `.claude/rules/messaging.md` → "LID KARTASI". Guruhdagi lid xabari — KARTA:
    // lid o'zgarganda yangi xabar yuborilmaydi, o'sha xabar `editMessageText` bilan JOYIDA
    // yangilanadi (`LeadNotifier.SyncCardAsync`).
    //
    // ⚠️ Bu bo'limdagi testlarda Telegram TOKEN sozlangan bo'lishi SHART: aks holda
    // `TelegramService.IsConfigured == false` bo'lib, har bir funksiya BIRINCHI qatoridayoq
    // qaytar edi va test hech nimani tekshirmasdi ("yashil, lekin bo'sh" test). Token
    // `TelegramTokenScope` orqali FAQAT joriy test oqimida beriladi (fayl oxiridagi izoh).
    //
    // ⚠️ VA: karta yozuvi FAOL chat bilan KESISHISHI kerak (topilma B5). Yozuv bor degani
    // "yuborish kerak" degani emas — guruh o'chirilgan bo'lsa (`IsActive=false`) yoki chat
    // umuman ro'yxatlarda bo'lmasa qator SINXRONLANMAYDI. Shu sababdan `SeedLead` har karta
    // uchun FAOL `TelegramGroup` qatorini ham qo'shadi.

    /// <summary>Guruh chati (manfiy id) — kartada IZOH MATNI ko'rsatilmaydi, faqat sanoq.</summary>
    private const long GroupChat = -100123;

    /// <summary>Superadminning SHAXSIY chati — kartada to'liq izohlar.</summary>
    private const long PersonalChat = 5001;

    /// <summary>Tarmoqqa chiqmaydigan, lekin har so'rovni YOZIB BORADIGAN Telegram xizmati.</summary>
    private static (TelegramService Telegram, FakeTelegramHandler Http) FakeTelegram(
        Func<string, (HttpStatusCode Code, string Body)>? responder = null)
    {
        var handler = new FakeTelegramHandler(responder);
        return (new TelegramService(new SingleHandlerHttpClientFactory(handler),
            NullLogger<TelegramService>.Instance), handler);
    }

    /// <summary>Lidning mavjud kartasi (Telegram xabariga bog'lovchi yozuv).</summary>
    private static LeadTelegramMessage Card(
        string leadId, long chatId = GroupChat, long messageId = 55,
        string hash = "eski-xesh", bool dead = false) => new()
    {
        LeadId = leadId, ChatId = chatId, MessageId = messageId,
        TextHash = hash, IsDead = dead, CreatedAt = AppClock.Iso(), UpdatedAt = AppClock.Iso(),
    };

    /// <summary>Bot qo'shilgan guruh (takror ChatId qo'shilmaydi — indeks UNIKAL).</summary>
    private static void AddGroup(AppDbContext ctx, long chatId, bool active = true)
    {
        if (ctx.TelegramGroups.Local.Any(g => g.ChatId == chatId)
            || ctx.TelegramGroups.Any(g => g.ChatId == chatId)) return;
        ctx.TelegramGroups.Add(new TelegramGroup { ChatId = chatId, IsActive = active });
    }

    /// <summary>Superadminning SHAXSIY chati: foydalanuvchi + Telegram registratsiyasi.</summary>
    private static AppUser AddPersonalChat(AppDbContext ctx, long chatId, string role = Roles.SuperAdmin)
    {
        var user = new AppUser { FullName = "Bosh admin", Email = $"super{chatId}", Role = role };
        ctx.Users.Add(user);
        ctx.TelegramRegistrations.Add(new TelegramRegistration { UserId = user.Id, ChatId = chatId });
        return user;
    }

    /// <summary>
    /// Bazaga saqlangan lid + (ixtiyoriy) kartalari.
    /// <para><paramref name="registerChats"/> = true bo'lsa har kartaning chati FAOL guruh sifatida
    /// ro'yxatga olinadi — aks holda `SyncCardAsync` kesishmani topmay, qatorni umuman
    /// sinxronlamaydi (topilma B5).</para>
    /// </summary>
    private static Lead SeedLead(AppDbContext ctx, params LeadTelegramMessage[] cards) =>
        SeedLead(ctx, true, cards);

    private static Lead SeedLead(AppDbContext ctx, bool registerChats, params LeadTelegramMessage[] cards)
    {
        var lead = NewLead();
        ctx.Leads.Add(lead);
        foreach (var c in cards)
        {
            c.LeadId = lead.Id;
            ctx.LeadTelegramMessages.Add(c);
            if (registerChats) AddGroup(ctx, c.ChatId);
        }
        ctx.SaveChanges();
        return lead;
    }

    /// <summary>
    /// YETIM karta yozuvi — lidi bazada YO'Q qator.
    ///
    /// <para>⚠️ FK CASCADE (migratsiya <c>AddLeadTelegramMessageFk</c>) joriy qilingandan keyin
    /// bunday qatorni oddiy yo'l bilan YARATIB BO'LMAYDI: lid o'chsa bola qator ham o'chadi.
    /// Ya'ni yetim faqat MIGRATSIYAGACHA qolgan ma'lumotda uchraydi — uni modellashtirish uchun
    /// SQLite FK tekshiruvi vaqtincha o'chiriladi.</para>
    /// </summary>
    private static void SeedOrphanCard(AppDbContext ctx, string leadId, long chatId, long messageId = 55)
    {
        ctx.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        try
        {
            ctx.LeadTelegramMessages.Add(Card(leadId, chatId, messageId));
            ctx.SaveChanges();
        }
        finally
        {
            ctx.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        }
    }

    // ---- 6.1 ENG MUHIM HIMOYA: kartasi yo'q lidga karta YARATILMAYDI ----

    [Fact]
    public async Task Kartasi_yoq_lidga_yangi_karta_YARATILMAYDI()
    {
        // 🔴 Busiz deploydan ertasiga menejer kanbanda 200 ta eski lidni surganda guruhga
        // 200 ta yangi karta yog'ilardi. `SyncCardAsync` faqat MAVJUD kartani yangilaydi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx);                       // kartasi YO'Q (eski lid)
        AddGroup(ctx, GroupChat);
        ctx.LeadEvents.Add(new LeadEvent { LeadId = lead.Id, Type = "stage", Text = "Ko'chirildi" });
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();     // bot SOZLANGAN — bahona qolmasin
        var (tg, http) = FakeTelegram();

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);

        Assert.Empty(http.Calls);                       // HECH QANDAY so'rov (sendMessage ham) yo'q
        Assert.Empty(ctx.LeadTelegramMessages);         // yolg'on bog'lovchi yozuv ham paydo bo'lmadi
    }

    [Fact]
    public async Task Kartalari_hammasi_olik_bolsa_ham_yangisi_yaratilmaydi()
    {
        // `IsDead` yozuv "karta bor edi, endi yo'q" degani — bu ham YANGI karta yuborishga
        // asos bo'lmaydi (aks holda o'chirilgan xabar har o'zgarishda qaytib tug'ilardi).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card("", dead: true));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);

        Assert.Empty(http.Calls);
        Assert.True(ctx.LeadTelegramMessages.Single().IsDead);   // holati ham o'zgarmadi
    }

    // ---- 6.2 FAOL CHATLAR bilan kesishuv (topilma B5) ----

    [Fact]
    public async Task Faolsizlantirilgan_guruh_SINXRONLANMAYDI_va_olik_deb_belgilanmaydi()
    {
        // 🔴 B5 ning MAG'ZI: admin guruhni o'chirgan (bot chiqarilgan) bo'lsa, yangi lidlar u
        // yerga bormaydi — eski kartalar ham bormasligi kerak.
        // ⚠️ VA yozuvga `IsDead` QO'YILMAYDI: guruh qayta yoqilishi mumkin, o'shanda karta o'z
        // joyida yangilanishda davom etsin (o'lik deb belgilansa — abadiy muzlardi).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, registerChats: false, Card(""));
        AddGroup(ctx, GroupChat, active: false);
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);

        Assert.Empty(http.Calls);
        var row = ctx.LeadTelegramMessages.Single();
        Assert.False(row.IsDead);
        Assert.Equal("eski-xesh", row.TextHash);
    }

    [Fact]
    public async Task Guruh_qayta_yoqilsa_karta_yana_sinxronlanadi()
    {
        // Yuqoridagi testning davomi: `IsDead` qo'yilmagani uchun karta "tiriladi".
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, registerChats: false, Card(""));
        var group = new TelegramGroup { ChatId = GroupChat, IsActive = false };
        ctx.TelegramGroups.Add(group);
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);
        Assert.Empty(http.Calls);

        group.IsActive = true;
        ctx.SaveChanges();

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);
        Assert.Single(http.Calls);
        Assert.Equal("editMessageText", http.Calls[0].Method);
    }

    [Fact]
    public async Task Notanish_chatdagi_karta_sinxronlanmaydi()
    {
        // Chat na guruhlar ro'yxatida, na registratsiyalarda — kesishma yo'q, so'rov ham yo'q.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, registerChats: false, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);

        Assert.Empty(http.Calls);
        Assert.False(ctx.LeadTelegramMessages.Single().IsDead);
    }

    [Fact]
    public async Task Superadmin_registratsiyasidagi_shaxsiy_chat_sinxronlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, registerChats: false, Card("", chatId: PersonalChat));
        AddPersonalChat(ctx, PersonalChat);
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);

        var call = Assert.Single(http.Calls);
        Assert.Equal("editMessageText", call.Method);
        Assert.Equal(PersonalChat, call.ChatId);
    }

    // ---- 6.3 GURUH matni ≠ SHAXSIY matn (topilma B6) ----

    [Fact]
    public void Guruh_kartasida_izoh_MATNI_yoq_faqat_SANOQ()
    {
        // 🔴 Menejer izohi — ichki, filtrsiz matn (mijoz haqidagi mulohaza, to'lov qobiliyati).
        // U har bir guruhga tarqalmasligi kerak; to'liq matn faqat superadminning SHAXSIY
        // chatidagi kartada qoladi.
        var notes = new List<LeadEvent>
        {
            new() { Type = "note", Text = "To'lovga qurbi yetmasligi mumkin", ActorName = "Aziz" },
            new() { Type = "call", Text = "Onasi bilan gaplashildi" },
        };

        var (groupText, groupHash) = RenderCard(NewLead(), includeNotes: false, notes: notes, noteCount: 7);
        var (personalText, personalHash) = RenderCard(NewLead(), includeNotes: true, notes: notes, noteCount: 7);

        // Guruh: faqat sanoq.
        Assert.Contains("💬 7 ta izoh", groupText);
        Assert.DoesNotContain("💬 Oxirgi izohlar:", groupText);
        Assert.DoesNotContain("To'lovga qurbi", groupText);
        Assert.DoesNotContain("Onasi bilan gaplashildi", groupText);

        // Shaxsiy chat: to'liq matn.
        Assert.Contains("💬 Oxirgi izohlar:", personalText);
        Assert.Contains("• To'lovga qurbi yetmasligi mumkin — Aziz", personalText);
        Assert.Contains("• Onasi bilan gaplashildi", personalText);
        Assert.DoesNotContain("💬 7 ta izoh", personalText);

        // ⚠️ IKKI MATN = IKKI XESH: aks holda guruh va shaxsiy chat bir-birining xeshini
        // "eskirgan" deb ko'rib, bekorga qayta yozib turardi.
        Assert.NotEqual(groupHash, personalHash);
    }

    [Fact]
    public void Izohi_yoq_lidda_ikkala_variant_ham_izoh_qatorisiz()
    {
        var (groupText, _) = RenderCard(NewLead(), includeNotes: false);
        var (personalText, _) = RenderCard(NewLead(), includeNotes: true);
        Assert.DoesNotContain("💬", groupText);
        Assert.DoesNotContain("💬", personalText);
    }

    [Fact]
    public async Task Yangi_lid_guruhga_va_shaxsiy_chatga_TURLI_matn_yuboradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx);
        AddGroup(ctx, GroupChat);
        AddPersonalChat(ctx, PersonalChat);
        ctx.LeadEvents.AddRange(
            new LeadEvent { LeadId = lead.Id, Type = "note", Text = "Ichki mulohaza", CreatedAt = "2026-08-20T10:00:00" },
            new LeadEvent { LeadId = lead.Id, Type = "call", Text = "Ikkinchi izoh", CreatedAt = "2026-08-21T10:00:00" });
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead);

        Assert.Equal(2, http.Calls.Count);
        var groupCall = http.Calls.Single(c => c.ChatId == GroupChat);
        var personalCall = http.Calls.Single(c => c.ChatId == PersonalChat);

        Assert.DoesNotContain("Ichki mulohaza", groupCall.Text);
        Assert.Contains("💬 2 ta izoh", groupCall.Text);
        Assert.Contains("Ichki mulohaza", personalCall.Text);

        // Har qator O'Z matnining xeshi bilan saqlanadi — ikkalasi HAR XIL.
        var hashes = ctx.LeadTelegramMessages.Select(m => m.TextHash).ToList();
        Assert.Equal(2, hashes.Count);
        Assert.Equal(2, hashes.Distinct().Count());
    }

    // ---- 6.4 TextHash: bir xil matnga so'rov umuman yuborilmaydi ----

    [Fact]
    public async Task Karta_matni_ozgarmasa_tahrir_sorovi_umuman_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);   // xesh eski ⇒ TAHRIR ketadi
        Assert.Single(http.Calls);
        Assert.Equal("editMessageText", http.Calls[0].Method);

        // ⚠️ Ilgari bu yerda "daqiqa almashsa qadamni qaytar" sikli turardi: matndagi
        // «🕒 Yangilandi: HH:mm» xeshga ham kirar va u HAR DAQIQA o'zgarardi. Topilma B1 dan
        // keyin xesh stampsiz hisoblanadi — sikl KERAK EMAS, xulq soatga bog'liq emas.
        http.Clear();
        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);   // xesh AYNI ⇒ so'rov YO'Q

        Assert.Empty(http.Calls);
        Assert.NotEqual("eski-xesh", ctx.LeadTelegramMessages.Single().TextHash);
    }

    [Fact]
    public void Karta_xeshi_Yangilandi_qatoriga_BOGLIQ_EMAS()
    {
        // 🔴 TOPILMA B1. Xeshning butun maqsadi — "hech narsa o'zgarmagan bo'lsa so'rov
        // yubormaslik". Vaqt qatori xeshga kirsa u HAR DAQIQA o'zgarar va qisqa yo'l amalda
        // ishlamasdi: menejer kanbanda 20 lidni sursa 20 × (guruhlar + superadminlar) tahrir
        // ketib, Telegram guruh chegarasi (~20 xabar/daqiqa) urilardi.
        var (text, hash) = RenderCard(NewLead());

        var idx = text.LastIndexOf("\n🕒 Yangilandi:", StringComparison.Ordinal);
        Assert.True(idx > 0, "«🕒 Yangilandi» qatori matnda bo'lishi kerak (karta TIRIK ekani ko'rinsin)");

        // Xesh — AYNAN stampsiz matndan; stamp esa faqat MATNGA qo'shiladi.
        Assert.Equal(Sha256Hex(text[..idx]), hash);
        Assert.NotEqual(Sha256Hex(text), hash);
    }

    [Fact]
    public void Bir_xil_holat_bir_xil_xesh_beradi()
    {
        var (_, a) = RenderCard(NewLead());
        var (_, b) = RenderCard(NewLead());
        Assert.Equal(a, b);
    }

    // ---- 6.5 Xatolar tasnifi: Gone / RateLimited / soxta muvaffaqiyat ----

    [Fact]
    public async Task Xabar_topilmasa_karta_olik_deb_belgilanadi_va_qayta_urinilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram(
            FakeTelegramHandler.Error(HttpStatusCode.BadRequest, "Bad Request: message to edit not found"));

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);
        Assert.Single(http.Calls);
        Assert.True(ctx.LeadTelegramMessages.Single().IsDead);

        // Ikkinchi marta — o'lik yozuv umuman OLINMAYDI (tezlik chegarasi bekorga yemasin).
        http.Clear();
        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);
        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task Tezlik_chegarasida_xesh_saqlanmaydi_va_keyingi_ozgarishda_qayta_urinamiz()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram(
            FakeTelegramHandler.Error((HttpStatusCode)429, "Too Many Requests: retry after 3"));

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);

        var row = ctx.LeadTelegramMessages.Single();
        Assert.False(row.IsDead);                 // 429 — xabar joyida, faqat "keyinroq"
        Assert.Equal("eski-xesh", row.TextHash);  // xesh SAQLANMADI ⇒ keyingi safar yana urinamiz

        http.Clear();
        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);
        Assert.Single(http.Calls);
    }

    [Theory]
    [InlineData("<html>502 Bad Gateway</html>")]   // JSON emas — oraliqdagi proxy/CDN xato sahifasi
    [InlineData("{\"result\":true}")]              // JSON, lekin `ok` maydoni YO'Q
    [InlineData("{\"ok\":\"true\"}")]              // `ok` bor, lekin BOOL emas (satr)
    [InlineData("")]                               // tana BO'SH
    public async Task HTTP_200_bolsa_ham_ok_tasdiqlanmasa_natija_Failed(string body)
    {
        // 🔴 TOPILMA B10: ilgari muvaffaqiyat uchun HTTP 200 yetarli edi. Oraliqdagi proxy 200
        // bilan HTML xato sahifasi qaytarsa natija yolg'on "Ok" bo'lar, chaqiruvchi `TextHash`
        // ni saqlab qo'yar va keyingi safar so'rov UMUMAN yuborilmasdi — karta guruhda ABADIY
        // eski holatda qolardi (logda ham hech narsa yo'q).
        using var token = TelegramTokenScope.Use();
        var (tg, _) = FakeTelegram(_ => (HttpStatusCode.OK, body));

        var res = await tg.EditMessageTextOutcomeAsync(GroupChat, 55, "matn");

        Assert.Equal(TgEditResult.Failed, res.Result);
    }

    [Fact]
    public async Task Yolgon_muvaffaqiyatda_xesh_saqlanmaydi_karta_muzlab_qolmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram(_ => (HttpStatusCode.OK, "<html>502 Bad Gateway</html>"));

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);
        Assert.Equal("eski-xesh", ctx.LeadTelegramMessages.Single().TextHash);

        http.Clear();
        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);
        Assert.Single(http.Calls);                 // qayta urinamiz — jimgina "muvaffaqiyat" emas
    }

    [Fact]
    public async Task Tahrir_javobidan_retry_after_va_migrate_to_chat_id_oqiladi()
    {
        // Ikkala qiymat ham javobning `parameters` bo'limida keladi — parse qilinmasa yo'qolardi
        // ("429 dan keyin qancha kutay?" va "yangi chat id qaysi?" savollari javobsiz qolardi).
        using var token = TelegramTokenScope.Use();
        var (tg, _) = FakeTelegram(FakeTelegramHandler.ErrorWithParameters(
            (HttpStatusCode)429, "Too Many Requests: retry after 7",
            new { retry_after = 7, migrate_to_chat_id = -1009999L }));

        var res = await tg.EditMessageTextOutcomeAsync(GroupChat, 55, "matn");

        Assert.Equal(TgEditResult.RateLimited, res.Result);
        Assert.Equal(7, res.RetryAfterSeconds);
        Assert.Equal(-1009999, res.MigrateToChatId);
    }

    [Fact]
    public async Task Parametrsiz_javobda_retry_after_nol_migrate_null()
    {
        using var token = TelegramTokenScope.Use();
        var (tg, _) = FakeTelegram(FakeTelegramHandler.Error((HttpStatusCode)429, "Too Many Requests"));

        var res = await tg.EditMessageTextOutcomeAsync(GroupChat, 55, "matn");

        Assert.Equal(TgEditResult.RateLimited, res.Result);
        Assert.Equal(0, res.RetryAfterSeconds);
        Assert.Null(res.MigrateToChatId);
    }

    [Fact]
    public async Task Guruh_supergruppaga_aylansa_chat_id_YANGILANADI_va_karta_olik_bolmaydi()
    {
        // Eski chat id ABADIY o'zgargan — u bilan qayta urinish hech qachon ishlamaydi. Lekin
        // karta YANGI chatda TIRIK, shuning uchun `IsDead` QO'YILMAYDI, faqat manzil yangilanadi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, _) = FakeTelegram(FakeTelegramHandler.ErrorWithParameters(
            HttpStatusCode.BadRequest, "Bad Request: group chat was upgraded to a supergroup chat",
            new { migrate_to_chat_id = -1009999L }));

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);

        var row = ctx.LeadTelegramMessages.Single();
        Assert.Equal(-1009999, row.ChatId);
        Assert.False(row.IsDead);
        Assert.Equal("eski-xesh", row.TextHash);   // matn yetmadi — keyingi o'zgarishda yuboriladi
    }

    // ---- 6.6 Bot sozlanmagan / hech qachon buzmaydi ----

    [Fact]
    public async Task Karta_boti_sozlanmagan_bolsa_jim_otadi_va_YOZUV_QOLADI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        var (tg, http) = FakeTelegram();   // TelegramTokenScope ATAYIN ishlatilmadi ⇒ token yo'q

        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);
        Assert.Empty(http.Calls);
        Assert.Equal("eski-xesh", ctx.LeadTelegramMessages.Single().TextHash);

        // ⚠️ XULQ O'ZGARDI (topilma B11): ilgari `MarkDeletedAsync` tokensiz ham yozuvlarni
        // TOZALAB yuborardi. Natijada guruhda o'chirilgan lidning ismi va TELEFONI bilan karta
        // abadiy tirik qolar, uni yangilaydigan yagona ip esa uzilib ketardi. Endi bot
        // sozlanmagan bo'lsa yozuv QOLADI — keyingi o'chirishda "yetim" sifatida qayta uriniladi.
        await LeadNotifier.MarkDeletedAsync(ctx, tg, lead.Id, lead.FullName);
        Assert.Empty(http.Calls);
        Assert.Single(ctx.LeadTelegramMessages);
    }

    [Fact]
    public async Task Karta_hech_qachon_lid_amalini_buzmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        using var token = TelegramTokenScope.Use();

        // (a) Telegram 500 qaytaradi, (b) lid yo'q, (c) id bo'sh, (d) yetim karta (lid o'chgan).
        var (tg, _) = FakeTelegram(FakeTelegramHandler.Error(HttpStatusCode.InternalServerError, "boom"));
        await LeadNotifier.SyncCardAsync(ctx, tg, "yoq-lid");
        await LeadNotifier.SyncCardAsync(ctx, tg, "");
        await LeadNotifier.MarkDeletedAsync(ctx, tg, "yoq-lid", "Kimdir");
        await LeadNotifier.MarkDeletedAsync(ctx, tg, "", "");

        // ⚠️ FK CASCADE joriy qilingandan keyin yetim qatorni oddiy `Add` bilan yozib bo'lmaydi
        // (SQLite «FOREIGN KEY constraint failed» beradi) — `SeedOrphanCard` FK'ni vaqtincha
        // o'chirib qo'yadi. Amalda bunday qator faqat migratsiyagacha qolgan ma'lumotda uchraydi.
        SeedOrphanCard(ctx, "yetim-lid", -100888);
        await LeadNotifier.SyncCardAsync(ctx, tg, "yetim-lid");

        // (e) TARMOQ umuman yo'q (istisno tashlaydigan HttpClient) — bu ham yutilishi kerak.
        var lead = SeedLead(ctx, Card(""));
        var offline = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        await LeadNotifier.SyncCardAsync(ctx, offline, lead.Id);
        await LeadNotifier.MarkDeletedAsync(ctx, offline, lead.Id, lead.FullName);

        // Kontekst hamon ishlaydi — chaqiruvchining o'z amali buzilmasin.
        ctx.Leads.Add(new Lead { FullName = "Keyingi lid" });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Bekor_qilingan_sorov_muvaffaqiyat_kabi_otmaydi()
    {
        // ⚠️ `OperationCanceledException` ALOHIDA ushlanib QAYTA ULOQTIRILADI: bekor qilingan
        // so'rov "hammasi joyida" bo'lib ko'rinmasin — chaqiruvchi ham to'xtasin.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, _) = FakeTelegram();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LeadNotifier.SyncCardAsync(ctx, tg, lead.Id, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead, ct: cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LeadNotifier.MarkDeletedAsync(ctx, tg, lead.Id, lead.FullName, cts.Token));
    }

    // ---- 6.7 O'chirish (MarkDeletedAsync) ----

    [Fact]
    public async Task Lid_ochirilganda_karta_matni_almashadi_va_yozuvlar_tozalanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx,
            Card("", chatId: GroupChat, messageId: 55),
            Card("", chatId: -100777, messageId: 66, dead: true));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.MarkDeletedAsync(ctx, tg, lead.Id, lead.FullName);

        // Faqat TIRIK karta tahrirlanadi; xabar O'CHIRILMAYDI (deleteMessage 48 soatdan keyin ishlamaydi).
        var call = Assert.Single(http.Calls);
        Assert.Equal("editMessageText", call.Method);
        Assert.Equal(55, call.MessageId);
        Assert.Contains("🗑 Lid o'chirildi", call.Text);
        Assert.Contains("👤 Yangi Lid", call.Text);

        Assert.Empty(ctx.LeadTelegramMessages);   // yetim qatorlar to'planib qolmaydi
    }

    [Theory]
    [InlineData(429, "Too Many Requests: retry after 3")]
    [InlineData(500, "Internal Server Error")]
    public async Task Ochirishda_tahrir_TASDIQLANMASA_yozuv_QOLADI(int status, string description)
    {
        // 🔴 TOPILMA B11: ilgari natija e'tiborsiz qoldirilardi — 429 yoki tarmoq xatosida
        // guruhda o'chirilgan lidning ismi va TELEFONI bilan karta abadiy tirik qolar, uni
        // yangilaydigan yozuv esa endi yo'q edi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram(FakeTelegramHandler.Error((HttpStatusCode)status, description));

        await LeadNotifier.MarkDeletedAsync(ctx, tg, lead.Id, lead.FullName);

        Assert.Single(http.Calls);
        Assert.Single(ctx.LeadTelegramMessages);   // qator QOLDI — keyin "yetim" bo'lib qayta uriniladi
    }

    [Fact]
    public async Task Ochirishda_xabar_YOQ_bolsa_yozuv_baribir_ochiriladi()
    {
        // `Gone` — xabar allaqachon yo'q, ya'ni ish BAJARILGAN: bog'lovchi qator ham kerak emas.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, _) = FakeTelegram(
            FakeTelegramHandler.Error(HttpStatusCode.BadRequest, "Bad Request: message to edit not found"));

        await LeadNotifier.MarkDeletedAsync(ctx, tg, lead.Id, lead.FullName);

        Assert.Empty(ctx.LeadTelegramMessages);
    }

    [Fact]
    public async Task Yetim_kartalar_keyingi_ochirishda_TOZALANADI()
    {
        // Alohida fon xizmati qurilmagan: tizim o'zini o'zi tozalaydi — har o'chirishda
        // yetimlarning bir qismiga qayta uriniladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedOrphanCard(ctx, "yoq-lid-1", -100555, messageId: 91);
        var lead = SeedLead(ctx, Card(""));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.MarkDeletedAsync(ctx, tg, lead.Id, lead.FullName);

        Assert.Equal(2, http.Calls.Count);
        Assert.Empty(ctx.LeadTelegramMessages);

        // ⚠️ Yetim qatorda lid ismini BILMAYMIZ (lid o'chib ketgan) — matn ISMSIZ chiqadi.
        // Bu maxfiylik uchun ham yaxshi: guruhga ortiqcha ma'lumot tushmaydi.
        var orphanCall = http.Calls.Single(c => c.ChatId == -100555);
        Assert.Contains("🗑 Lid o'chirildi", orphanCall.Text);
        Assert.DoesNotContain("👤", orphanCall.Text);
    }

    [Fact]
    public async Task Yetim_kartalar_bir_ochirishda_kopi_bilan_20_ta_tozalanadi()
    {
        // Chegara BOR, chunki bu ish foydalanuvchi so'rovi ICHIDA bajariladi — o'chirish
        // tugmasi bir necha soniya osilib qolmasin (`LeadNotifier.MaxOrphanRetry` = 20).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        for (var i = 0; i < 25; i++) SeedOrphanCard(ctx, $"yoq-lid-{i}", -100600 - i);

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.MarkDeletedAsync(ctx, tg, "boshqa-yoq-lid", "");
        Assert.Equal(20, http.Calls.Count);
        Assert.Equal(5, ctx.LeadTelegramMessages.Count());

        // Keyingi o'chirishda qolgani ham tozalanadi.
        http.Clear();
        await LeadNotifier.MarkDeletedAsync(ctx, tg, "boshqa-yoq-lid", "");
        Assert.Equal(5, http.Calls.Count);
        Assert.Empty(ctx.LeadTelegramMessages);
    }

    [Fact]
    public void Lid_ochirilganda_karta_yozuvlari_CASCADE_bilan_ochadi()
    {
        // Migratsiya `AddLeadTelegramMessageFk`: tozalash faqat `MarkDeletedAsync` ga tayanardi,
        // u esa xatoni JIM yutadi — SaveChanges yiqilsa yetim qatorlar ABADIY qolardi. Yetim
        // qator zararsiz emas: (LeadId, ChatId) unikal, ya'ni o'sha lid id qayta ishlatilganda
        // (import/tiklash) yangi karta insert'i 23505 bilan yiqilardi. Sxema o'zi kafolatlasin.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card("", chatId: GroupChat), Card("", chatId: -100777));
        Assert.Equal(2, ctx.LeadTelegramMessages.Count());

        // ⚠️ BOSHQA kontekst: bola qatorlar kuzatilmagan bo'lsin — o'chirishni EF emas,
        // BAZANING O'ZI (ON DELETE CASCADE) bajarayotgani tekshirilsin.
        using var other = db.NewContext();
        other.Leads.Remove(other.Leads.Single(l => l.Id == lead.Id));
        other.SaveChanges();

        Assert.Empty(other.LeadTelegramMessages.ToList());
    }

    // ---- 6.8 Yangi lid / takroriy murojaat ----

    [Fact]
    public async Task Yangi_lid_yuborilganda_message_id_saqlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx);
        AddGroup(ctx, GroupChat);
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead, createdBy: "Sayt");

        var call = Assert.Single(http.Calls);
        Assert.Equal("sendMessage", call.Method);

        // ⚠️ Aynan SHU yozuv kartani keyin tahrirlash imkonini beradi — busiz karta rejimi yo'q.
        var row = ctx.LeadTelegramMessages.Single();
        Assert.Equal(GroupChat, row.ChatId);
        Assert.Equal(FakeTelegramHandler.NewMessageId, row.MessageId);
        Assert.NotEmpty(row.TextHash);
    }

    [Fact]
    public async Task Xabar_yuborilmasa_yolgon_yozuv_qoldirilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx);
        AddGroup(ctx, GroupChat);
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram(
            FakeTelegramHandler.Error(HttpStatusCode.BadRequest, "Bad Request: chat not found"));

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead);

        Assert.Single(http.Calls);
        Assert.Empty(ctx.LeadTelegramMessages);   // `message_id` yo'q ⇒ bog'lovchi yozuv ham yo'q
    }

    [Fact]
    public async Task Takroriy_murojaatda_karta_tahrirlanadi_va_ustiga_signal_yuboriladi()
    {
        // ⚠️ Telegram TAHRIRNI bildirishnoma qilmaydi — tashqaridan kelgan ish (takroriy murojaat,
        // test natijasi) sezilmay qolardi. Shuning uchun: tahrir + kartaga JAVOB qilib signal.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));
        lead.RepeatCount = 2;
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead, isNewLead: false);

        Assert.Equal(2, http.Calls.Count);
        Assert.Equal("editMessageText", http.Calls[0].Method);   // KARTA yangilandi (yangi karta emas)
        Assert.Equal(55, http.Calls[0].MessageId);

        var signal = http.Calls[1];
        Assert.Equal("sendMessage", signal.Method);
        Assert.Equal(55, signal.ReplyTo);                        // kartaga JAVOB — bosilsa kartaga sakraydi
        // ⚠️ Xom `reply_to_message_id` EMAS: javob berilayotgan xabar o'chirilgan bo'lsa Telegram
        // BUTUN so'rovni rad etar va signal umuman yetmasdi.
        Assert.True(signal.AllowWithoutReply);
        Assert.Contains("🔁 Takroriy murojaat (×2)", signal.Text);
        Assert.Single(ctx.LeadTelegramMessages);                 // signal id'si SAQLANMAYDI
    }

    [Fact]
    public async Task Tezlik_chegarasida_SIGNAL_ham_yuborilmaydi()
    {
        // 429 dan keyin AYNI chatga navbatdagi so'rov chegarani YANADA chuqurlashtirardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));
        lead.RepeatCount = 2;
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram(
            FakeTelegramHandler.Error((HttpStatusCode)429, "Too Many Requests: retry after 3"));

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead, isNewLead: false);

        var call = Assert.Single(http.Calls);
        Assert.Equal("editMessageText", call.Method);   // sendMessage (signal) YO'Q
    }

    [Fact]
    public async Task Tahrir_tarmoq_xatosi_bilan_yiqilsa_ham_signal_yuboriladi()
    {
        // Hodisa menejerdan yashirin qolmasin: tahrir yiqilsa ham signal ketadi (429 dan farqli).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card(""));
        lead.RepeatCount = 1;
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram(
            FakeTelegramHandler.Error(HttpStatusCode.InternalServerError, "boom"));

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead, isNewLead: false);

        Assert.Equal(2, http.Calls.Count);
        Assert.Equal("sendMessage", http.Calls[1].Method);
    }

    // ---- 6.9 K1: har chat uchun DARHOL saqlash + unikal indeksdan tiklanish ----

    [Fact]
    public async Task Mavjud_karta_qatori_borida_ikkinchisi_QOSHILMAYDI_yangilanadi()
    {
        // 🔴 `(LeadId, ChatId)` UNIKAL. `NotifyNewLeadAsync` yangi lid deb chaqirilganda (masalan
        // ommaviy forma + daraja testi deyarli bir vaqtda) ikkinchi `Add` unikal indeksni buzar
        // va butun so'rov 500 bo'lardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card("", messageId: 11, hash: "eski-xesh"));

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead);   // isNewLead: true

        Assert.Equal("sendMessage", Assert.Single(http.Calls).Method);
        var row = Assert.Single(ctx.LeadTelegramMessages);      // DUBL YO'Q — UPDATE bo'ldi
        Assert.Equal(FakeTelegramHandler.NewMessageId, row.MessageId);
        Assert.NotEqual("eski-xesh", row.TextHash);
        Assert.False(row.IsDead);
    }

    [Fact]
    public async Task Olik_qatorga_yangi_karta_yuborilsa_qator_TIRILADI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, Card("", dead: true));

        using var token = TelegramTokenScope.Use();
        var (tg, _) = FakeTelegram();

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead, isNewLead: false);

        var row = Assert.Single(ctx.LeadTelegramMessages);
        Assert.False(row.IsDead);
        Assert.Equal(FakeTelegramHandler.NewMessageId, row.MessageId);
    }

    [Fact]
    public async Task Poygada_unikal_indeks_buzilsa_qator_QAYTA_YOZILADI_va_kontekst_zaharlanmaydi()
    {
        // 🔴 K1 — ilgari UMUMAN qamralmagan joy. Ikki hodisa deyarli bir vaqtda kelsa (ommaviy
        // forma + daraja testi) ikkinchi `Add` `DbUpdateException` bilan yiqiladi. EF esa yiqilgan
        // entity'ni `Added` holatida ChangeTracker'da QOLDIRADI — tashqi `catch` uni yutsa ham,
        // AYNI DbContext keyin ishlatilganda (chaqiruvchining o'z `SaveChangesAsync`i) o'sha buzuq
        // INSERT qayta uriniladi va bu safar hech kim yutmaydi: OCHIQ LID FORMASIGA 500 ketardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx);                 // kartasi YO'Q — `Add` yo'li tanlanadi
        AddGroup(ctx, GroupChat);
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        // ⚠️ POYGA: xabar Telegramga ketayotgan payt PARALLEL so'rov o'sha (LeadId, ChatId)
        // qatorini allaqachon yozib qo'yadi. Ya'ni bizning `Add` unikal indeksni buzadi.
        var raced = false;
        var (tg, _) = FakeTelegram(method =>
        {
            if (method == "sendMessage" && !raced)
            {
                raced = true;
                using var rival = db.NewContext();
                rival.LeadTelegramMessages.Add(new LeadTelegramMessage
                {
                    LeadId = lead.Id, ChatId = GroupChat, MessageId = 4242,
                    TextHash = "raqib-xesh", CreatedAt = AppClock.Iso(), UpdatedAt = AppClock.Iso(),
                });
                rival.SaveChanges();
            }
            return FakeTelegramHandler.Success(method);
        });

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead);

        // ⚠️ Poyga HAQIQATDA yuz berganiga ishonch: aks holda test "bo'sh yashil" bo'lardi.
        Assert.True(raced, "raqib qator yozilmadi — poyga modellashtirilmagan");

        // Dubl YO'Q, va qator BIZNING yangi xabarimiz bilan qayta yozilgan: aks holda guruhda
        // YETIM karta qolardi (bazadagi yozuv boshqa xabarga ishora qilar, bizniki hech qachon
        // tahrirlanmasdi).
        var rows = ctx.LeadTelegramMessages.AsNoTracking().ToList();
        Assert.Single(rows);
        Assert.Equal(FakeTelegramHandler.NewMessageId, rows[0].MessageId);
        Assert.NotEqual("raqib-xesh", rows[0].TextHash);

        // 🔴 K1 ning IKKINCHI (eng xavfli) qismi: yiqilgan `SaveChanges` dan keyin ChangeTracker
        // ZAHARLANMAYDI — chaqiruvchining keyingi saqlashi muvaffaqiyatli o'tishi SHART.
        ctx.Leads.Add(new Lead { FullName = "Keyingi lid", CreatedAt = AppClock.Iso() });
        await ctx.SaveChangesAsync();
        Assert.Equal(2, ctx.Leads.Count());
    }

    // ---- 6.10 "Kiritdi" qatori — DOIM `created` hodisasidan ----

    [Fact]
    public async Task Kiritdi_qatori_DOIM_created_hodisasidan_olinadi()
    {
        // ⚠️ Chaqiruvchi boyroq matn beradi ("Forma: Matematika kursi"), lekin keyingi tahrirlarda
        // uni qayta hisoblab bo'lmaydi — natijada karta BIRINCHI tahrirdanoq kambag'allashib,
        // bekorga bitta tahrir so'rovi ketardi. Bitta manba = barqaror matn = barqaror xesh.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx);
        AddGroup(ctx, GroupChat);
        ctx.LeadEvents.Add(new LeadEvent
        {
            LeadId = lead.Id, Type = "created", ActorName = "Aziz Menejer",
            CreatedAt = "2026-08-01T10:00:00",
        });
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead, createdBy: "Forma: Matematika kursi");

        var text = Assert.Single(http.Calls).Text;
        Assert.Contains("🧑‍💼 Kiritdi: Aziz Menejer", text);
        Assert.DoesNotContain("Forma: Matematika kursi", text);
    }

    [Fact]
    public async Task Created_hodisasi_yoq_bolsa_chaqiruvchi_bergan_nom_ZAXIRA_boladi()
    {
        // Barcha mavjud yaratish oqimlari `created` hodisasini yozadi, lekin eski/g'ayrioddiy
        // lidda qator butunlay yo'qolib ketmasin.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx);
        AddGroup(ctx, GroupChat);
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();

        await LeadNotifier.NotifyNewLeadAsync(ctx, tg, lead, createdBy: "Sayt");

        Assert.Contains("🧑‍💼 Kiritdi: Sayt", Assert.Single(http.Calls).Text);
    }

    // ---- 6.11 ClassifyEditError — sof funksiya, tarmoqsiz ----

    [Theory]
    // «matn aynan eski» — xato emas, ish bajarilgan
    [InlineData(400, "Bad Request: message is not modified: specified new message content...", TgEditResult.NotModified)]
    [InlineData(400, "Message Is Not Modified", TgEditResult.NotModified)]              // registr farq qiladi
    // xabar/chat yo'q yoki bot u yerda emas — QAYTA URINILMAYDI
    [InlineData(400, "Bad Request: message to edit not found", TgEditResult.Gone)]
    [InlineData(400, "Bad Request: MESSAGE_ID_INVALID", TgEditResult.Gone)]
    [InlineData(400, "Bad Request: message_id_invalid", TgEditResult.Gone)]             // registr farq qiladi
    [InlineData(400, "Bad Request: chat not found", TgEditResult.Gone)]
    [InlineData(400, "Bad Request: message can't be edited", TgEditResult.Gone)]
    [InlineData(400, "Bad Request: chat_id is empty", TgEditResult.Gone)]
    [InlineData(403, "Forbidden: bot was kicked from the supergroup chat", TgEditResult.Gone)]
    [InlineData(403, "Forbidden: bot is not a member of the supergroup chat", TgEditResult.Gone)]
    // guruh SUPERGRUPPAGA aylandi — eski chat id abadiy o'zgargan (yangisi `parameters` da keladi)
    [InlineData(400, "Bad Request: group chat was upgraded to a supergroup chat", TgEditResult.Gone)]
    // ⚠️ SHAXSIY chat holatlari: karta adminning shaxsiy chatiga ham yuboriladi. Ilgari ular
    // `Failed` ga tushib, `IsDead` qo'yilmasdi — lidning HAR o'zgarishida bekorga so'rov ketaverardi.
    [InlineData(403, "Forbidden: bot was blocked by the user", TgEditResult.Gone)]
    [InlineData(403, "Forbidden: user is deactivated", TgEditResult.Gone)]
    // tezlik chegarasi
    [InlineData(429, "Too Many Requests: retry after 5", TgEditResult.RateLimited)]
    [InlineData(200, "TOO MANY REQUESTS", TgEditResult.RateLimited)]                    // faqat matndan ham
    [InlineData(429, null, TgEditResult.RateLimited)]                                   // faqat koddan ham
    // noma'lum sabab
    [InlineData(400, "Bad Request: kutilmagan yangi sabab", TgEditResult.Failed)]
    [InlineData(500, "", TgEditResult.Failed)]
    [InlineData(500, null, TgEditResult.Failed)]                                        // description YO'Q
    public void Tahrir_xatosi_togri_tasniflanadi(int status, string? description, TgEditResult expected)
    {
        Assert.Equal(expected, TelegramService.ClassifyEditError(status, description));
    }

    [Fact]
    public void Tahrir_xatosida_matn_ozgarmagani_429_dan_USTUN()
    {
        // Tartib muhim: 429 bilan birga kelgan "not modified" ham MUVAFFAQIYAT deb qaraladi,
        // aks holda xesh saqlanmay, har safar qayta urinilardi.
        Assert.Equal(TgEditResult.NotModified,
            TelegramService.ClassifyEditError(429, "Bad Request: message is not modified"));
    }

    // ---- 6.12 Karta MATNI (Render) ----

    [Fact]
    public void Karta_matnida_lidning_JORIY_holati_boradi()
    {
        var lead = NewLead();
        lead.RepeatCount = 3;
        lead.LastRepeatAt = "2026-08-20T09:15:00";
        lead.ConvertedStudentId = "s-1";
        var trial = new TrialLesson { ScheduledAt = "2026-08-23T15:30", Result = "stayed" };
        var notes = new List<LeadEvent>
        {
            new() { Type = "call", Text = "Ota-onasi bilan gaplashildi", ActorName = "Aziz" },
            new() { Type = "note", Text = "Dushanbaga keladi" },
        };

        var (text, _) = RenderCard(lead, stageTitle: "Aloqada", trial: trial,
            trialGroupName: "IELTS-1", notes: notes);

        Assert.Contains("📍 Bosqich: Aloqada", text);
        Assert.Contains("🎓 Sinov darsi: 2026-08-23 15:30 · IELTS-1 — qoldi", text);
        Assert.Contains("🔁 Takroriy murojaat: ×3 (2026-08-20 09:15)", text);
        Assert.Contains("💬 Oxirgi izohlar:", text);
        Assert.Contains("• Ota-onasi bilan gaplashildi — Aziz", text);
        Assert.Contains("• Dushanbaga keladi", text);
        Assert.Contains("✅ O'quvchi bo'ldi", text);
        Assert.Contains("🕒 Yangilandi:", text);   // karta TIRIK ekani ko'rinsin
    }

    [Fact]
    public void Karta_matnida_bosh_maydonlar_qator_chiqarmaydi()
    {
        var (text, _) = RenderCard(NewLead());

        Assert.StartsWith("🆕 Yangi lid!", text);  // RepeatCount = 0 ⇒ "yangi" sarlavhasi
        Assert.Contains("— — —", text);            // holat bloki ajratkichi
        Assert.DoesNotContain("📍 Bosqich:", text);
        Assert.DoesNotContain("🎓 Sinov darsi:", text);
        Assert.DoesNotContain("🔁 Takroriy murojaat:", text);
        Assert.DoesNotContain("💬 Oxirgi izohlar:", text);
        Assert.DoesNotContain("✅ O'quvchi bo'ldi", text);
    }

    [Fact]
    public void Karta_sarlavhasi_takroriy_lidda_ozgaradi()
    {
        var lead = NewLead();
        lead.RepeatCount = 1;
        var (text, _) = RenderCard(lead);
        Assert.StartsWith("🔁 Mavjud lid yangilandi", text);
    }

    [Fact]
    public void Karta_uzun_izohni_qirqadi()
    {
        // Karta — TARIX emas, joriy holat: bitta uzun izoh butun kartani egallab olmasin.
        var notes = new List<LeadEvent> { new() { Type = "note", Text = new string('a', 400) } };
        var (text, _) = RenderCard(NewLead(), notes: notes);

        Assert.Contains("…", text);
        Assert.DoesNotContain(new string('a', 200), text);
    }

    [Fact]
    public void Karta_izohdagi_qator_kochirish_belgilarini_boshliqqa_aylantiradi()
    {
        // Windows'dan kiritilgan matn (`\r\n`) kartaning qator tuzilishini buzmasin.
        var notes = new List<LeadEvent> { new() { Type = "note", Text = "Birinchi\r\nIkkinchi" } };
        var (text, _) = RenderCard(NewLead(), notes: notes);

        Assert.Contains("• Birinchi  Ikkinchi", text);
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public async Task Kartada_faqat_OXIRGI_ikkita_izoh_boradi()
    {
        // ⚠️ SHAXSIY chat: guruhda izoh MATNI umuman chiqmaydi (faqat sanoq).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SeedLead(ctx, registerChats: false, Card("", chatId: PersonalChat));
        AddPersonalChat(ctx, PersonalChat);
        for (var i = 1; i <= 4; i++)
            ctx.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id, Type = "note", Text = $"izoh-{i}",
                CreatedAt = $"2026-08-2{i}T10:00:00",
            });
        ctx.SaveChanges();

        using var token = TelegramTokenScope.Use();
        var (tg, http) = FakeTelegram();
        await LeadNotifier.SyncCardAsync(ctx, tg, lead.Id);

        var text = Assert.Single(http.Calls).Text;
        Assert.Contains("• izoh-4", text);
        Assert.Contains("• izoh-3", text);
        Assert.DoesNotContain("• izoh-2", text);
        Assert.DoesNotContain("• izoh-1", text);
    }

    // ---- 6.13 4000 belgi chegarasi — qirqish TEPADAN (topilma B2) ----

    [Fact]
    public void Karta_matni_4000_belgidan_oshmaydi()
    {
        // Ilgari uzun so'rovnomali xabar Telegram chegarasidan oshib 400 olardi, xato esa jim
        // yutilib XABAR UMUMAN YO'QOLARDI.
        var lead = NewLead();
        lead.Note = new string('a', 9000);

        var (text, _) = RenderCard(lead);

        Assert.True(text.Length <= 4000, $"karta matni {text.Length} belgi");
    }

    [Fact]
    public void Qirqish_TEPADAN_ketadi_HOLAT_BLOKI_saqlanadi()
    {
        // 🔴 TOPILMA B2 NING MAG'ZI. Ilgari butun satr OXIRIDAN kesilardi: uzun so'rovnomali
        // lidda aynan holat bloki (bosqich, sinov darsi, izohlar, "O'quvchi bo'ldi", vaqt)
        // qirqilib ketar, matn holatga qarab o'zgarmay qolar va xesh barqarorlashib karta
        // ABADIY MUZLARDI.
        var lead = NewLead();
        lead.Note = new string('a', 9000);
        lead.RepeatCount = 3;
        lead.LastRepeatAt = "2026-08-20T09:15:00";
        lead.ConvertedStudentId = "s-1";
        var trial = new TrialLesson { ScheduledAt = "2026-08-23T15:30", Result = "stayed" };
        var notes = new List<LeadEvent> { new() { Type = "note", Text = "Muhim izoh" } };

        var (text, _) = RenderCard(lead, stageTitle: "Aloqada", trial: trial,
            trialGroupName: "IELTS-1", notes: notes);

        Assert.True(text.Length <= 4000, $"karta matni {text.Length} belgi");

        // Holat bloki TO'LIQ joyida.
        Assert.Contains("📍 Bosqich: Aloqada", text);
        Assert.Contains("🎓 Sinov darsi: 2026-08-23 15:30 · IELTS-1 — qoldi", text);
        Assert.Contains("🔁 Takroriy murojaat: ×3 (2026-08-20 09:15)", text);
        Assert.Contains("• Muhim izoh", text);
        Assert.Contains("✅ O'quvchi bo'ldi", text);
        Assert.Contains("🕒 Yangilandi:", text);
        // Sarlavha ham qoladi (qirqish O'RTADAN): RepeatCount=3 ⇒ HeaderOf "mavjud lid" deydi.
        Assert.StartsWith("🔁 Mavjud lid yangilandi", text);

        // Qirqish belgisi holat blokidan OLDIN — ya'ni kesilgani kam qimmatli tepa qism.
        var cut = text.IndexOf('…');
        Assert.True(cut > 0, "uzun matn qirqilishi kerak");
        Assert.True(cut < text.IndexOf("— — —", StringComparison.Ordinal),
            "qirqish HOLAT blokidan oldin bo'lishi shart");
    }

    [Fact]
    public void Qirqilgan_karta_ham_holatga_qarab_ozgaradi_demak_muzlamaydi()
    {
        // B2 ning natijasi: bosqich o'zgarsa uzun kartaning ham XESHI o'zgaradi.
        var lead = NewLead();
        lead.Note = new string('a', 9000);

        var (_, before) = RenderCard(lead, stageTitle: "Aloqada");
        var (_, after) = RenderCard(lead, stageTitle: "Sinov darsiga yozildi");

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Qirqishda_emoji_ortasidan_kesilmaydi()
    {
        // 3998 belgi + emoji (IKKI `char` — surrogat juftlik): oddiy qirqishda 3999-belgi
        // juftlikning YARMI bo'lib qolar va Telegram'da buzuq belgi chiqardi.
        var text = new string('a', 3998) + "😀" + new string('b', 100);

        var cut = Trim(text);

        Assert.True(cut.Length <= 4000);
        Assert.EndsWith("…", cut);
        // Yolg'iz surrogat UTF-8'ga o'girilganda U+FFFD ga aylanadi — shu bilan tekshiramiz.
        var roundTrip = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(cut));
        Assert.DoesNotContain("�", roundTrip);
    }

    [Fact]
    public void Chegaradan_qisqa_matn_qirqilmaydi()
    {
        var text = "🆕 Yangi lid!\n👤 Yangi Lid";
        Assert.Equal(text, Trim(text));
    }
}

// =================================================================================================
// LID KARTASI testlari uchun yordamchilar
// =================================================================================================

/// <summary>Telegram Bot API'ga ketgan bitta so'rov (JSON tanasidan o'qilgan holda).</summary>
/// <param name="Method">API metodi: <c>sendMessage</c> | <c>editMessageText</c>.</param>
/// <param name="ReplyTo">Javob berilayotgan xabar id'si (<c>reply_parameters.message_id</c>).</param>
/// <param name="AllowWithoutReply">Javob xabari o'chirilgan bo'lsa ham yuborilsinmi.</param>
internal sealed record TgCall(
    string Method, long ChatId, long MessageId, long? ReplyTo, bool AllowWithoutReply, string Text);

/// <summary>
/// Telegram javobini SOXTALASHTIRADIGAN handler: haqiqiy tarmoqqa chiqmaydi, lekin
/// <see cref="NoNetworkHttpClientFactory"/> dan farqli o'laroq so'rovni YOZIB BORADI.
///
/// <para>⚠️ Nega kerak: <c>TelegramService</c> har xatoni O'ZI yutadi (log + <c>false</c>), ya'ni
/// "tarmoqqa chiqsa test yiqiladi" usuli KARTA testlarida ishlamaydi — ortiqcha so'rov jimgina
/// yutilib, test baribir yashil qolardi. Shuning uchun so'rovlar SANALADI.</para>
/// </summary>
internal sealed class FakeTelegramHandler(
    Func<string, (HttpStatusCode Code, string Body)>? responder = null) : HttpMessageHandler
{
    /// <summary>Soxta Telegram bergan <c>message_id</c> (yangi xabar uchun).</summary>
    public const long NewMessageId = 777;

    private readonly List<TgCall> _calls = new();

    public IReadOnlyList<TgCall> Calls { get { lock (_calls) return _calls.ToList(); } }

    public void Clear() { lock (_calls) _calls.Clear(); }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var method = request.RequestUri!.Segments[^1];
        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(ct);
        using (var doc = JsonDocument.Parse(body))
        {
            var root = doc.RootElement;
            long Num(string name) => root.TryGetProperty(name, out var v) ? v.GetInt64() : 0;

            // ⚠️ Javob XOM `reply_to_message_id` bilan emas, `reply_parameters` obyekti bilan
            // yuboriladi: javob berilayotgan xabar o'chirilgan bo'lsa Telegram xom shaklda
            // BUTUN so'rovni rad etardi va signal umuman yetmasdi.
            long? replyTo = null;
            var allowWithoutReply = false;
            if (root.TryGetProperty("reply_parameters", out var rp) && rp.ValueKind == JsonValueKind.Object)
            {
                if (rp.TryGetProperty("message_id", out var rid)) replyTo = rid.GetInt64();
                allowWithoutReply = rp.TryGetProperty("allow_sending_without_reply", out var aw)
                                    && aw.ValueKind == JsonValueKind.True;
            }
            else if (root.TryGetProperty("reply_to_message_id", out var legacy))
            {
                replyTo = legacy.GetInt64();
            }

            var call = new TgCall(method, Num("chat_id"), Num("message_id"), replyTo, allowWithoutReply,
                root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "");
            lock (_calls) _calls.Add(call);
        }

        var (code, responseBody) = (responder ?? Success)(method);
        return new HttpResponseMessage(code)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Standart javob: hammasi muvaffaqiyatli (yangi xabar — <see cref="NewMessageId"/>).</summary>
    public static (HttpStatusCode, string) Success(string method) => method == "sendMessage"
        ? (HttpStatusCode.OK, $"{{\"ok\":true,\"result\":{{\"message_id\":{NewMessageId}}}}}")
        : (HttpStatusCode.OK, "{\"ok\":true}");

    /// <summary>Telegram xato javobi: <c>{"ok":false,"description":"..."}</c>.</summary>
    public static Func<string, (HttpStatusCode, string)> Error(HttpStatusCode code, string description) =>
        _ => (code, JsonSerializer.Serialize(new { ok = false, description }));

    /// <summary>Telegram xato javobi + <c>parameters</c> (<c>retry_after</c>, <c>migrate_to_chat_id</c>).</summary>
    public static Func<string, (HttpStatusCode, string)> ErrorWithParameters(
        HttpStatusCode code, string description, object parameters) =>
        _ => (code, JsonSerializer.Serialize(new { ok = false, description, parameters }));
}

/// <summary>Berilgan handler ustida HttpClient beradigan fabrika (handler QAYTA ishlatiladi —
/// shuning uchun <c>disposeHandler: false</c>: aks holda birinchi so'rovdan keyin yozuvlar yo'qolardi).</summary>
internal sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// Telegram tokenini FAQAT JORIY TEST OQIMIDA "sozlangan" qilib ko'rsatadi
/// (<c>TelegramService.IsConfigured == true</c>).
///
/// <para><b>NEGA KERAK:</b> token <see cref="AppSecrets"/> dan (statik, global) o'qiladi va
/// <c>TelegramService.IsConfigured</c> <c>virtual</c> EMAS — ya'ni testda uni obyekt darajasida
/// almashtirib bo'lmaydi. Tokensiz esa <c>SyncCardAsync</c>/<c>MarkDeletedAsync</c> birinchi
/// qatoridayoq qaytadi va test HECH NIMANI tekshirmaydi.</para>
///
/// <para>⚠️ <b>PARALLEL TESTLAR BUZILMAYDI:</b> xUnit test klasslarini parallel yuritadi, shuning
/// uchun global <c>AppSecrets</c> ga token YOZIB QO'YIB bo'lmasdi (masalan
/// «Xabarnoma_bot_sozlanmagan_bolsa_jim_otadi» tasodifan qizarardi). Yechim: <c>AppSecrets</c> ga
/// bir marta shunday konfiguratsiya o'rnatiladi-ki, u qiymatni <see cref="AsyncLocal{T}"/> dan
/// oladi — ya'ni tokenni FAQAT shu <c>using</c> bloki ichidagi oqim ko'radi, boshqa hamma joyda
/// (va boshqa hamma kalit uchun) natija baribir BO'SH bo'ladi — Init'gacha bo'lgani bilan bir xil.</para>
/// </summary>
internal sealed class TelegramTokenScope : IDisposable
{
    private static readonly AsyncLocal<string?> Current = new();
    private static readonly AsyncLocalConfig Config = new(() => Current.Value);
    private static int _installed;

    public static TelegramTokenScope Use(string token = "test-bot-token")
    {
        if (Interlocked.Exchange(ref _installed, 1) == 0) AppSecrets.Init(Config);
        Current.Value = token;
        return new TelegramTokenScope();
    }

    public void Dispose() => Current.Value = null;

    /// <summary>Faqat Telegram token kalitiga javob beradigan (va faqat joriy oqimda!)
    /// eng kichik <see cref="IConfiguration"/>.</summary>
    private sealed class AsyncLocalConfig(Func<string?> token) : IConfiguration
    {
        private readonly IConfiguration _empty = new ConfigurationBuilder().Build();

        public string? this[string key]
        {
            get => key is "Telegram:BotToken" or AppSecrets.EnvKeys.TelegramBotToken ? token() : null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => _empty.GetChildren();
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => _empty.GetReloadToken();
        public IConfigurationSection GetSection(string key) => _empty.GetSection(key);
    }
}
