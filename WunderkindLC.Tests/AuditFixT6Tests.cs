using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-07 audit tuzatishlari (T6 — xabarlar, eslatma fon ishlari, bog'lanish navbati, support).
/// Rasmiy manba: <c>.claude/rules/messaging.md</c> («KUNLIK FON ISHLARI — BAZADAGI BELGI» va h.k.).
/// </summary>
public class AuditFixT6Tests
{
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

    private static PaymentReminderService Payment(IAppDbContext ctx, MessagingStack stack) =>
        new(new SingleServiceProvider(ctx), stack.Telegram, NullLogger<PaymentReminderService>.Instance);

    private static BirthdayGreetingService Birthday(IAppDbContext ctx, MessagingStack stack) =>
        new(new SingleServiceProvider(ctx), stack.Auto, NullLogger<BirthdayGreetingService>.Instance);

    private static AutoMessageRule DebtRule(string audience = "parents") => new()
    {
        Trigger = AutoMessageTriggers.PaymentDebt, Enabled = true, Template = "",
        Audience = audience,
    };

    /// <summary>Joriy oyning 1-sanasi (toq kun — qarzdorlik eslatmasi kuni).</summary>
    private static DateOnly OddDay() => new(AppClock.Today.Year, AppClock.Today.Month, 1);

    // ===================== C9 — kunlik ish belgisi BAZADA =====================

    [Fact]
    public async Task JobRunMarker_bir_sanada_faqat_BIR_marta_band_qilinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var d = new DateOnly(2026, 10, 7);

        Assert.True(await JobRunMarker.TryClaimAsync(ctx, "job:x", d));
        Assert.False(await JobRunMarker.TryClaimAsync(ctx, "job:x", d));    // takror — yo'q
        Assert.True(await JobRunMarker.IsDoneAsync(ctx, "job:x", d));
        Assert.False(await JobRunMarker.IsDoneAsync(ctx, "job:x", d.AddDays(1)));
        Assert.True(await JobRunMarker.TryClaimAsync(ctx, "job:x", d.AddDays(1)));   // ertasi — yana
        Assert.Single(ctx.BackgroundJobRuns);
    }

    [Fact]
    public async Task JobRunMarker_ikkinchi_kontekst_RESTART_kabi_ham_band_qila_olmaydi()
    {
        // Xotira emas, BAZA: yangi jarayon (yangi kontekst, eski kuzatuv yo'q) ham bugungi belgini ko'radi.
        using var db = TestDb.Sqlite();
        var d = new DateOnly(2026, 10, 7);
        Assert.True(await JobRunMarker.TryClaimAsync(db.Context, "payment_debt:s1", d));

        db.Context.ChangeTracker.Clear();   // "restart" — kuzatuvdagi nusxa yo'q
        Assert.False(await JobRunMarker.TryClaimAsync(db.Context, "payment_debt:s1", d));
    }

    [Fact]
    public async Task JobRunMarker_prune_faqat_prefiksli_ESKI_qatorlarni_ochiradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var d = new DateOnly(2026, 10, 7);
        await JobRunMarker.TryClaimAsync(ctx, "payment_debt", d.AddDays(-5));          // ish belgisi
        await JobRunMarker.TryClaimAsync(ctx, "payment_debt:old", d.AddDays(-5));
        await JobRunMarker.TryClaimAsync(ctx, "payment_debt:new", d);
        await JobRunMarker.TryClaimAsync(ctx, "birthday:old", d.AddDays(-5));

        await JobRunMarker.PruneAsync(ctx, "payment_debt:", d.AddDays(-2));

        var keys = ctx.BackgroundJobRuns.Select(r => r.Key).OrderBy(k => k).ToList();
        Assert.Equal(new[] { "birthday:old", "payment_debt", "payment_debt:new" }, keys);
    }

    [Fact]
    public void Qarzdorlik_eslatmasi_RESTARTDAN_keyin_takror_yuborilmaydi()
    {
        // Hodisa: deploy 11:00 da — xotiradagi "bugun ishladim" belgisi o'chib, HAR qarzdorga eslatma
        // QAYTA ketardi. Endi har o'quvchi bazada band qilinadi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -500000m;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);
        Assert.Single(ctx.UserNotifications);

        // "Restart" — YANGI xizmat nusxasi (xotira toza), o'sha kun.
        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);
        Assert.Single(ctx.UserNotifications);
    }

    [Fact]
    public void Qarzdorlik_TickAsync_ish_tugagach_bazaga_belgi_qoyadi_va_ikkinchi_nusxa_ishlamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -100000m;
        ctx.Students.Add(s);
        var rule = DebtRule();
        ctx.AutoMessageRules.Add(rule);
        ctx.SaveChanges();
        var at10 = OddDay().ToDateTime(new TimeOnly(10, 0));

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "TickAsync", at10, CancellationToken.None);
        // CRM upgrade (P2.5): the "done today" marker is per RULE (each rule has its own day/time).
        Assert.True(JobRunMarker.IsDoneAsync(ctx, JobRunMarker.For(PaymentReminderService.JobKey, rule.Id), OddDay())
            .GetAwaiter().GetResult());

        // Yangi qarzdor qo'shildi — lekin bugungi ish allaqachon BAJARILGAN: restartdagi nusxa qayta boshlamaydi.
        var s2 = NewStudent("Valiyev Vali", "u-2");
        s2.Balance = -100000m;
        ctx.Students.Add(s2);
        ctx.SaveChanges();
        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "TickAsync", at10.AddHours(2), CancellationToken.None);

        Assert.Single(ctx.UserNotifications);
    }

    [Fact]
    public void Qarzdorlik_soat_9_dan_oldin_ishlamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -100000m;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "TickAsync",
            OddDay().ToDateTime(new TimeOnly(8, 30)), CancellationToken.None);

        Assert.Empty(ctx.UserNotifications);
        Assert.Empty(ctx.BackgroundJobRuns);
    }

    [Fact]
    public void Tugilgan_kun_RESTARTDAN_keyin_takror_tabriklamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        var s = NewStudent();
        s.BirthDate = today.AddYears(-12).ToString("yyyy-MM-dd");
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(new AutoMessageRule
        {
            Trigger = AutoMessageTriggers.Birthday, Enabled = true, Template = "Tabrik {ism}",
        });
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Birthday(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);
        Reflect.RunAsyncMethod(Birthday(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        Assert.Single(ctx.UserNotifications);
    }

    // ===================== Lows (d) — oylik erkin eslatma 29–31-sanada =====================

    [Fact]
    public void Oylik_eslatma_31_sanada_qisqa_oyda_OXIRGI_kunda_yuboriladi()
    {
        var rule = new AutoMessageRule { ScheduleType = "monthly", ScheduleDayOfMonth = 31 };
        Assert.True(CustomReminderService.IsScheduledDay(rule, new DateOnly(2026, 2, 28)));   // fevral oxiri
        Assert.False(CustomReminderService.IsScheduledDay(rule, new DateOnly(2026, 2, 27)));
        Assert.True(CustomReminderService.IsScheduledDay(rule, new DateOnly(2026, 4, 30)));   // 30 kunlik oy
        Assert.True(CustomReminderService.IsScheduledDay(rule, new DateOnly(2026, 1, 31)));
        Assert.False(CustomReminderService.IsScheduledDay(rule, new DateOnly(2026, 1, 30)));

        var day30 = new AutoMessageRule { ScheduleType = "monthly", ScheduleDayOfMonth = 30 };
        Assert.True(CustomReminderService.IsScheduledDay(day30, new DateOnly(2026, 1, 30)));
        Assert.False(CustomReminderService.IsScheduledDay(day30, new DateOnly(2026, 1, 31)));  // bir oyda bir marta
        Assert.True(CustomReminderService.IsScheduledDay(day30, new DateOnly(2028, 2, 29)));   // kabisa fevrali

        Assert.True(CustomReminderService.IsScheduledDay(new AutoMessageRule { ScheduleType = "daily" },
            new DateOnly(2026, 2, 28)));
    }

    // ===================== MSG-3 — guruh auditoriyasi A'ZOLIK bo'yicha =====================

    [Fact]
    public async Task Guruh_auditoriyasi_azolikdan_ClassNamedan_EMAS_va_arxivsiz()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var a = new Group { Name = "Ingliz A" };
        var b = new Group { Name = "Matem B" };
        var closed = new Group { Name = "Yopiq", IsArchived = true };

        var twoGroups = NewStudent("Ikki Guruhli", "u-two");
        twoGroups.ClassName = "Ingliz A";            // eskirgan yorliq — faqat birinchi guruh
        var archived = NewStudent("Arxiv", "u-arch");
        archived.IsArchived = true;
        var left = NewStudent("Ketgan", "u-left");
        left.ClassName = "Matem B";                  // yorlig'i qolgan, lekin a'zoligi YOPILGAN
        var legacy = NewStudent("Eski Yozuv", "u-legacy");
        legacy.ClassName = "Matem B";                // a'zoligi UMUMAN yo'q — zaxira yo'l
        var inClosed = NewStudent("Yopiqda", "u-closed");

        ctx.Classes.AddRange(a, b, closed);
        ctx.Students.AddRange(twoGroups, archived, left, legacy, inClosed);
        ctx.StudentGroups.AddRange(
            new StudentGroup { StudentId = twoGroups.Id, GroupId = a.Id, IsActive = true, Status = "active" },
            new StudentGroup { StudentId = twoGroups.Id, GroupId = b.Id, IsActive = true, Status = "frozen" },
            new StudentGroup { StudentId = archived.Id, GroupId = b.Id, IsActive = true, Status = "active" },
            new StudentGroup { StudentId = left.Id, GroupId = b.Id, IsActive = false, Status = "active", LeftAt = "2026-09-01" },
            new StudentGroup { StudentId = inClosed.Id, GroupId = closed.Id, IsActive = true, Status = "frozen" });
        ctx.SaveChanges();

        var audB = await MessagingAudience.GroupAudienceAsync(ctx, "Matem B");
        Assert.Contains(twoGroups.Id, audB);       // ikkinchi guruh — ilgari xabar olmasdi
        Assert.Contains(legacy.Id, audB);          // eski model — zaxira
        Assert.DoesNotContain(archived.Id, audB);  // arxiv — hech qachon
        Assert.DoesNotContain(left.Id, audB);      // chiqib ketgan — yorliq qolgan bo'lsa ham
        Assert.Equal(2, audB.Count);

        // Ro'yxatdagi SANOQ ham AYNAN shu funksiyadan — son = oluvchilar.
        var all = await MessagingAudience.GroupAudienceByNameAsync(ctx);
        Assert.Equal(audB, all["Matem B"]);
        Assert.Equal(new[] { twoGroups.Id }, all["Ingliz A"]);
        Assert.False(all.ContainsKey("Yopiq"));    // yopilgan guruh — ommaviy xabar yo'q
    }

    // (MSG-2 — push "Faqat qarzdorlar" testi 2026-10 da push bilan birga olib tashlandi.)

    // ===================== C1 qismi — arxivdagi o'quvchiga davomat xabari yo'q =====================

    [Fact]
    public async Task Arxivdagi_oquvchiga_davomat_xabari_KETMAYDI_lekin_tolov_kvitansiyasi_ketadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.IsArchived = true;
        ctx.Students.Add(s);
        ctx.AutoMessageRules.AddRange(
            new AutoMessageRule { Trigger = AutoMessageTriggers.AttendanceAbsent, Enabled = true, Template = "Kelmadi {ism}" },
            new AutoMessageRule { Trigger = AutoMessageTriggers.PaymentReceived, Enabled = true, Template = "To'lov {ism}" });
        ctx.SaveChanges();
        var auto = new MessagingStack().Auto;

        await auto.DispatchAttendanceAbsentAsync(ctx, s, "G1", "Sababsiz", "2026-10-07");
        Assert.Empty(ctx.UserNotifications);

        await auto.DispatchStudentAsync(ctx, AutoMessageTriggers.PaymentReceived, s);
        var n = Assert.Single(ctx.UserNotifications);
        Assert.StartsWith("To'lov", n.Body);
    }

    [Fact]
    public void ReachesArchived_faqat_tolov_kvitansiyasi()
    {
        Assert.True(AutoMessageTriggers.ReachesArchived(AutoMessageTriggers.PaymentReceived));
        foreach (var t in new[]
                 {
                     AutoMessageTriggers.AttendanceAbsent, AutoMessageTriggers.AttendancePresent,
                     AutoMessageTriggers.GradeEntered, AutoMessageTriggers.StudentAdded,
                     AutoMessageTriggers.MonthlyCharge, AutoMessageTriggers.Birthday,
                 })
            Assert.False(AutoMessageTriggers.ReachesArchived(t), t);
    }

    // ===================== MSG-8 — eski guruh qarzi o'z nomi bilan =====================

    /// <summary>Kurs + guruh + a'zolik (+ shu oyga hisob).</summary>
    private static Group Subject(Infrastructure.Data.AppDbContext ctx, Student s, string course,
        decimal owed, bool live)
    {
        var month = AppClock.Today.ToString("yyyy-MM");
        var subject = new Subject { Name = course };
        var g = new Group { Name = $"{course} guruhi", CourseId = subject.Id, MonthlyFee = 0m };
        ctx.Subjects.Add(subject);
        ctx.Classes.Add(g);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, IsActive = live, Status = "active",
            JoinedAt = $"{month}-01", ActivatedAt = $"{month}-01",
            LeftAt = live ? null : AppClock.Today.ToString("yyyy-MM-dd"),
        });
        if (owed > 0)
            ctx.MonthlyCharges.Add(new MonthlyCharge
            {
                StudentId = s.Id, GroupId = g.Id, Month = month, Amount = owed, Date = $"{month}-01",
            });
        return g;
    }

    [Fact]
    public void Qarzdorlik_ESKI_guruh_qarzi_ESKI_kurs_nomi_bilan_keladi_yangi_guruh_nomi_bilan_EMAS()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -300000m;
        s.ClassName = "Matematika guruhi";          // eskirgan yorliq yangi guruhni ko'rsatadi
        ctx.Students.Add(s);
        Subject(ctx, s, "Ingliz tili", 300000m, live: false);   // ESKI guruh — chiqib ketgan, qarz qolgan
        Subject(ctx, s, "Matematika", 0m, live: true);          // YANGI guruh — qarz yo'q
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Contains("Ingliz tili", n.Body);
        Assert.Contains("300 000 so'm", n.Body);
        Assert.DoesNotContain("Matematika", n.Body);
    }

    [Fact]
    public void Qarzdorlik_azoligi_BOR_lekin_guruh_qarzi_yoq_bolsa_NOMSIZ_umumiy_eslatma_ketadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -100000m;
        s.ClassName = "Matematika guruhi";
        ctx.Students.Add(s);
        Subject(ctx, s, "Matematika", 0m, live: true);
        ctx.AutoMessageRules.Add(DebtRule());
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        // Guruhga bog'lanmagan (eski GroupId'siz) qarz — eslatma KETADI, lekin eskirgan `ClassName`
        // yorlig'i bilan EMAS: kurs nomisiz umumiy matn.
        var n = Assert.Single(ctx.UserNotifications);
        Assert.Contains("100 000 so'm", n.Body);
        Assert.DoesNotContain("Matematika", n.Body);
    }

    // ===================== MSG-4 — qarzdorlik qoidasining AUDITORIYASI =====================

    [Fact]
    public void Qarzdorlik_oqituvchilar_auditoriyasida_FAN_guruhi_oqituvchisiga_ketadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var teacher = new Teacher { FullName = "Karimov Karim", UserId = "u-teacher" };
        ctx.Teachers.Add(teacher);
        var s = NewStudent();
        s.Balance = -200000m;
        ctx.Students.Add(s);
        var g = Subject(ctx, s, "Ingliz tili", 200000m, live: true);
        g.TeacherId = teacher.Id;
        ctx.AutoMessageRules.Add(DebtRule("teachers"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx, new MessagingStack()), "RunDailyAsync", OddDay(), CancellationToken.None);

        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("u-teacher", n.UserId);     // ota-onaga EMAS
        Assert.Contains("Ingliz tili", n.Body);
    }

    [Fact]
    public void Auditoriya_telefoni_oquvchi_va_ota_ona_uchun_farq_qiladi()
    {
        var s = NewStudent();
        Assert.Equal("907778899", MessagingAudience.StudentPhone(s, "students"));
        Assert.Equal("901112233", MessagingAudience.StudentPhone(s, "parents"));
        s.Phone = "";
        Assert.Equal("901112233", MessagingAudience.StudentPhone(s, "students"));   // zaxira — ota-ona
    }

    // (MSG-6 — ommaviy SMS navbati testlari 2026-10 da SMS kanali bilan birga olib tashlandi.)

    // ===================== MSG-7 — support slotlari =====================

    [Fact]
    public void Support_slot_otgan_vaqtni_aniqlaydi()
    {
        var now = new DateTime(2026, 10, 7, 15, 0, 0);
        Assert.True(SupportService.IsPast(new SupportSlot { Date = "2026-10-06", StartTime = "18:00" }, now));
        Assert.True(SupportService.IsPast(new SupportSlot { Date = "2026-10-07", StartTime = "14:30" }, now));
        Assert.True(SupportService.IsPast(new SupportSlot { Date = "2026-10-07", StartTime = "15:00" }, now));
        Assert.False(SupportService.IsPast(new SupportSlot { Date = "2026-10-07", StartTime = "15:30" }, now));
        Assert.False(SupportService.IsPast(new SupportSlot { Date = "2026-10-08", StartTime = "09:00" }, now));
        Assert.False(SupportService.IsPast(new SupportSlot { Date = "buzuq", StartTime = "09:00" }, now));
    }

    [Fact]
    public async Task Support_bildirishnoma_ilova_tarixiga_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        await SupportService.NotifyAsync(ctx, "u-teacher", "Yangi support bron", "Ali bron qildi");
        var n = Assert.Single(ctx.UserNotifications);
        Assert.Equal("u-teacher", n.UserId);
        Assert.Equal("support", n.Type);

        await SupportService.NotifyAsync(ctx, null, "x", "y");   // akkauntsiz — jim
        Assert.Single(ctx.UserNotifications);
    }

    // (Guruh chati — eski HomeroomClass testi 2026-10 da chat bilan birga olib tashlandi.)

    // ===================== ADM-8 — bot murojaati faqat ruxsatlilarga =====================

    [Fact]
    public void Bot_murojaati_faqat_admin_va_messages_support_ruxsatli_xodimga()
    {
        Assert.True(SupportService.ReceivesBotSupport(Roles.SuperAdmin, null));
        Assert.True(SupportService.ReceivesBotSupport(Roles.Admin, null));
        Assert.True(SupportService.ReceivesBotSupport(Roles.Staff, new[] { "messages.support" }));
        Assert.True(SupportService.ReceivesBotSupport(Roles.Staff, new[] { "messages" }));       // butun bo'lim
        Assert.False(SupportService.ReceivesBotSupport(Roles.Staff, new[] { "finance.cashbox" }));
        Assert.False(SupportService.ReceivesBotSupport(Roles.Staff, new[] { "messages.broadcast" }));
        Assert.False(SupportService.ReceivesBotSupport(Roles.Staff, Array.Empty<string>()));
        Assert.False(SupportService.ReceivesBotSupport(Roles.Teacher, new[] { "messages.support" }));
    }
}
