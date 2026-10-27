using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.HrTasks;

/// <summary>
/// LC: sinov darsi topshiriqlari (`hr-tasks.md` §8) va topshiriq ostidagi «✅ Qabul qildim» (§5).
/// Egasining qarorlari (2026-10-27): mas'ulga (bo'lmasa sinovga yozganga, u ham bo'lmasa direktorga), 09:00 da,
/// BITTA ro'yxat; «bog'laning» ro'yxati hammasi hal bo'lganda O'ZI yopiladi.
/// </summary>
public class HrTrialTasksTests
{
    // ---------------------------------------------------------------- sof qoidalar

    [Fact]
    public void Kimga__masul_keyin_sinovga_yozgan_keyin_direktorlar()
    {
        Func<string, string?> hr = u => u switch { "u-masul" => "e-masul", "u-yozgan" => "e-yozgan", _ => null };
        string[] directors = ["e-dir1", "e-dir2"];
        Assert.Equal(["e-masul"], HrTrialRules.Recipients("u-masul", "u-yozgan", hr, directors));
        // mas'ul HR xodimi emas (masalan superadmin) — sinovga yozganga
        Assert.Equal(["e-yozgan"], HrTrialRules.Recipients("u-superadmin", "u-yozgan", hr, directors));
        Assert.Equal(["e-yozgan"], HrTrialRules.Recipients(null, "u-yozgan", hr, directors));
        // ikkalasi ham yo'q — direktorlarga
        Assert.Equal(directors, HrTrialRules.Recipients(null, null, hr, directors));
        Assert.Empty(HrTrialRules.Recipients(null, null, hr, []));
    }

    [Fact]
    public void Royxatga_tushish__ertangi_faqat_belgilanmagan_kechagi_hammasi_lid_ochiq_bolsa()
    {
        Assert.True(HrTrialRules.Eligible(HrTrialTaskKinds.Before, "pending", leadClosed: false));
        Assert.False(HrTrialRules.Eligible(HrTrialTaskKinds.Before, "came", leadClosed: false));
        Assert.True(HrTrialRules.Eligible(HrTrialTaskKinds.After, "no_show", leadClosed: false));
        Assert.True(HrTrialRules.Eligible(HrTrialTaskKinds.After, "came", leadClosed: false));
        Assert.False(HrTrialRules.Eligible(HrTrialTaskKinds.After, "pending", leadClosed: true));
        Assert.True(HrTrialRules.LeadClosed("st-1", null, null));
        Assert.True(HrTrialRules.LeadClosed(null, null, "2026-10-27T10:00"));
        Assert.False(HrTrialRules.LeadClosed(null, "", " "));
    }

    [Fact]
    public void Hal_boldi__qayta_yozish_rad_etish_aylantirish()
    {
        var day = new DateOnly(2031, 3, 10);
        var pending = new HrTrialRules.TrialState("pending", day);
        // kechagi ro'yxat: lid ochiq va qayta yozilmagan — hal EMAS
        Assert.False(HrTrialRules.Resolved(HrTrialTaskKinds.After, day, false, false, pending, [day]));
        // keyingi sanaga qayta sinovga yozildi — hal
        Assert.True(HrTrialRules.Resolved(HrTrialTaskKinds.After, day, false, false, pending, [day, day.AddDays(3)]));
        // ESKIROQ sinov qayta yozish emas
        Assert.False(HrTrialRules.Resolved(HrTrialTaskKinds.After, day, false, false, pending, [day.AddDays(-5), day]));
        // rad etildi / aylantirildi / o'chirildi — hal
        Assert.True(HrTrialRules.Resolved(HrTrialTaskKinds.After, day, false, true, pending, [day]));
        Assert.True(HrTrialRules.Resolved(HrTrialTaskKinds.After, day, true, false, null, []));

        // ertangi ro'yxat: natija belgilansa yoki boshqa kunga ko'chirilsa — hal
        Assert.False(HrTrialRules.Resolved(HrTrialTaskKinds.Before, day, false, false, pending, [day]));
        Assert.True(HrTrialRules.Resolved(HrTrialTaskKinds.Before, day, false, false, new("came", day), [day]));
        Assert.True(HrTrialRules.Resolved(HrTrialTaskKinds.Before, day, false, false, new("pending", day.AddDays(2)), []));
    }

    [Fact]
    public void Telefon_va_vaqt_formati()
    {
        Assert.Equal("+998 90 123 45 67", HrTrialRules.Phone("998901234567"));
        Assert.Equal("+998 90 123 45 67", HrTrialRules.Phone("90 123 45 67"));
        Assert.Equal("+7 999 1234567", HrTrialRules.Phone("+7 999 1234567"));   // xorijiy — tegilmaydi
        Assert.Equal("11.03 15:00", HrTrialRules.When("2031-03-11T15:00"));
        Assert.Equal(new DateOnly(2031, 3, 11), HrTrialRules.DayOf("2031-03-11T15:00"));
        Assert.Null(HrTrialRules.DayOf("buzuq"));
    }

    [Fact]
    public void Muddat__shu_kuni_20_00_kech_bolsa_ikki_soat()
    {
        Assert.Equal(new DateTime(2031, 3, 10, 20, 0, 0), HrTrialRules.DueLocal(new DateTime(2031, 3, 10, 9, 5, 0)));
        Assert.Equal(new DateTime(2031, 3, 10, 21, 0, 0), HrTrialRules.DueLocal(new DateTime(2031, 3, 10, 19, 0, 0)));
    }

    [Fact]
    public void Tavsif_uzun_royxatda_ham_chegaraga_sigadi_va_qolganini_aytadi()
    {
        var items = Enumerable.Range(1, 80).Select(i => new HrTrialRules.Item(
            $"t{i}", $"O'quvchi {i} Familiyasi", "998901234567", "998911234567", "998921234567", "2031-03-11T15:00",
            "IELTS Foundation 1", "Aziz Karimov", "Ingliz tili", "Beginner", "Mohibuta", "no_show")).ToList();
        var text = HrTrialRules.Description(HrTrialTaskKinds.After, items);
        Assert.True(text.Length <= HrLimits.DescriptionMax);
        Assert.Contains("…va yana", text);
        Assert.Contains("❌ Kelmadi", text);
        Assert.Contains("ota: +998 91 123 45 67", text);
    }

    // ---------------------------------------------------------------- servis: ro'yxatlar

    private static readonly DateOnly Today = new(2031, 3, 10);

    private static async Task<(Lead Lead, TrialLesson Trial)> LeadWithTrialAsync(
        HrTasksKit kit, string? assigneeUserId, string scheduledAt, string result = "pending", string name = "Ali Valiyev")
    {
        var teacher = new Teacher { FullName = "Aziz Karimov" };
        var course = new Subject { Name = "Ingliz tili" };
        var group = new Group { Name = "IELTS-" + Guid.NewGuid().ToString("N")[..4], TeacherId = teacher.Id, CourseId = course.Id };
        var lead = new Lead
        {
            FullName = name, Phone = "998901234567", FatherPhone = "998911112233", Level = "Beginner",
            AssigneeUserId = assigneeUserId, CreatedAt = "2031-03-01T10:00:00",
        };
        var trial = new TrialLesson { LeadId = lead.Id, GroupId = group.Id, ScheduledAt = scheduledAt, Result = result, CreatedAt = "2031-03-05T10:00:00" };
        kit.Db.AddRange(teacher, course, group, lead, trial);
        await kit.Db.SaveChangesAsync();
        return (lead, trial);
    }

    private static async Task<List<HrTask>> TasksOfAsync(HrTasksKit kit, string employeeId)
    {
        await using var db = kit.NewContext();
        return await db.HrTasks.AsNoTracking().Where(t => t.EmployeeId == employeeId).OrderBy(t => t.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task Ertangi_royxat__09_00_dan_keyin_masulga_bitta_topshiriq_botda_hamma_malumot_bilan()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(telegram: true, name: "Mohibuta");
        await LeadWithTrialAsync(kit, admin.User.Id, "2031-03-11T15:00");

        // 09:00 dan OLDIN — hech narsa
        Assert.Equal(0, await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 8, 55)));
        Assert.Empty(await TasksOfAsync(kit, admin.Employee!.Id));

        Assert.Equal(1, await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5)));
        var task = Assert.Single(await TasksOfAsync(kit, admin.Employee.Id));
        Assert.Equal("Ertaga (11.03) sinov darsiga keladiganlar — 1 ta", task.Title);
        Assert.Contains("Ali Valiyev", task.Description);
        Assert.Contains("+998 90 123 45 67", task.Description);
        Assert.Contains("ota: +998 91 111 22 33", task.Description);
        Assert.Contains("11.03 15:00", task.Description);
        Assert.Contains("ustoz: Aziz Karimov", task.Description);
        Assert.Contains("Ingliz tili · daraja: Beginner", task.Description);
        Assert.Contains("Mas'ul: Mohibuta", task.Description);
        Assert.False(task.ProofRequired);
        Assert.Equal(HrTasksKit.At(2031, 3, 10, 20, 0), task.DueAt);

        var message = Assert.Single(kit.Sender.To(admin.Chat!.Value));
        Assert.Contains("Ali Valiyev", message);
        Assert.Contains("👤 Berdi: " + HrTrialRules.GiverName, message);
        Assert.Equal((admin.Chat.Value, task.Id), Assert.Single(kit.Sender.TaskButtons));

        // qayta sikl — TAKROR yo'q (har 5 daqiqada yuguradi)
        Assert.Equal(0, await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 10)));
        Assert.Single(await TasksOfAsync(kit, admin.Employee.Id));
        Assert.Single(kit.Sender.To(admin.Chat.Value));
    }

    [Fact]
    public async Task Kech_yozilgan_sinov_osha_royxatga_qoshiladi_va_xabar_boradi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(telegram: true);
        await LeadWithTrialAsync(kit, admin.User.Id, "2031-03-11T15:00");
        await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5));

        var (_, late) = await LeadWithTrialAsync(kit, admin.User.Id, "2031-03-11T17:30", name: "Vali Aliyev");
        Assert.Equal(1, await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 14, 0)));

        var task = Assert.Single(await TasksOfAsync(kit, admin.Employee!.Id));
        Assert.Equal("Ertaga (11.03) sinov darsiga keladiganlar — 2 ta", task.Title);
        Assert.Contains("Vali Aliyev", task.Description);
        await using var db = kit.NewContext();
        Assert.Contains(late.Id, (await db.HrTrialTasks.SingleAsync()).TrialIdList);
        Assert.StartsWith("➕ <b>Ro'yxatga yangi o'quvchi qo'shildi</b>", kit.Sender.To(admin.Chat!.Value).Last());
        Assert.Contains("Vali Aliyev", kit.Sender.To(admin.Chat.Value).Last());
    }

    [Fact]
    public async Task Kechagi_royxat__hammasi_hal_bolganda_ozi_bajariladi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(telegram: true);
        var (lost, _) = await LeadWithTrialAsync(kit, admin.User.Id, "2031-03-09T15:00", result: "no_show");
        var (rebook, _) = await LeadWithTrialAsync(kit, admin.User.Id, "2031-03-09T16:00", result: "came", name: "Vali Aliyev");

        await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5));
        var task = Assert.Single(await TasksOfAsync(kit, admin.Employee!.Id));
        Assert.Equal("Kecha (09.03) sinov darsi bo'lganlar bilan bog'laning — 2 ta", task.Title);
        Assert.Contains("❌ Kelmadi", task.Description);
        Assert.Contains("✅ Sinovga keldi", task.Description);

        // biri rad etildi — hali bitta qoldi
        await using (var db = kit.NewContext())
        {
            (await db.Leads.SingleAsync(l => l.Id == lost.Id)).LostAt = "2031-03-10T11:00";
            await db.SaveChangesAsync();
        }
        await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 11, 5));
        Assert.Equal(HrTaskStatuses.Open, (await TasksOfAsync(kit, admin.Employee.Id)).Single().Status);

        // ikkinchisi KEYINGI sanaga qayta sinovga yozildi — hammasi hal, topshiriq O'ZI bajarildi
        await using (var db = kit.NewContext())
        {
            var oldTrial = await db.TrialLessons.FirstAsync(t => t.LeadId == rebook.Id);
            db.TrialLessons.Add(new TrialLesson { LeadId = rebook.Id, GroupId = oldTrial.GroupId, ScheduledAt = "2031-03-14T16:00", CreatedAt = "2031-03-10T12:00:00" });
            await db.SaveChangesAsync();
        }
        Assert.True(await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 12, 5)) >= 1);
        var done = (await TasksOfAsync(kit, admin.Employee.Id)).Single(t => t.Id == task.Id);
        Assert.Equal(HrTaskStatuses.Done, done.Status);
        Assert.Equal(HrTrialRules.AutoDoneComment, done.EmployeeComment);
    }

    [Fact]
    public async Task Masul_yoq__sinovga_yozgan_xodimga_u_ham_yoq__direktorga()
    {
        using var kit = new HrTasksKit();
        var booker = await kit.PersonAsync();
        var director = await kit.PersonAsync(hrRole: HrRoles.Director);
        var (lead1, _) = await LeadWithTrialAsync(kit, null, "2031-03-11T15:00");
        kit.Db.LeadEvents.Add(new LeadEvent { LeadId = lead1.Id, Type = "trial", Text = "Sinov darsi belgilandi", ActorUserId = booker.User.Id, CreatedAt = "2031-03-05T10:00:00" });
        await kit.Db.SaveChangesAsync();
        await LeadWithTrialAsync(kit, null, "2031-03-11T16:00", name: "Egasiz Lid");

        await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5));
        Assert.Contains("Ali Valiyev", Assert.Single(await TasksOfAsync(kit, booker.Employee!.Id)).Description);
        var forDirector = Assert.Single(await TasksOfAsync(kit, director.Employee!.Id));
        Assert.Contains("Egasiz Lid", forDirector.Description);
        Assert.DoesNotContain("Ali Valiyev", forDirector.Description);
    }

    [Fact]
    public async Task Yopilgan_lid_va_belgilangan_sinov_ertangi_royxatga_tushmaydi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync();
        var (converted, _) = await LeadWithTrialAsync(kit, admin.User.Id, "2031-03-11T15:00");
        converted.ConvertedStudentId = "st-1";
        await kit.Db.SaveChangesAsync();
        await LeadWithTrialAsync(kit, admin.User.Id, "2031-03-11T16:00", result: "came");

        Assert.Equal(0, await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5)));
        Assert.Empty(await TasksOfAsync(kit, admin.Employee!.Id));
    }

    [Fact]
    public async Task Rejalashtiruvchi_siklida_ham_yuguradi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync();
        await LeadWithTrialAsync(kit, admin.User.Id, "2031-03-11T15:00");
        var scheduler = new HrTasksScheduler(kit.Scopes(), Microsoft.Extensions.Logging.Abstractions.NullLogger<HrTasksScheduler>.Instance);
        var result = await scheduler.TickAsync(HrTasksKit.At(2031, 3, 10, 9, 5));
        Assert.Equal(1, result["trial_tasks"]);
    }

    // ---------------------------------------------------------------- «✅ Qabul qildim»

    [Fact]
    public async Task Qabul_qildim__faqat_egasining_chati_qabul_qiladi_tugma_almashadi()
    {
        using var kit = new HrTasksKit();
        var owner = await kit.PersonAsync(telegram: true);
        var stranger = await kit.PersonAsync(telegram: true);
        var task = await kit.TaskAsync(owner.Employee!.Id, HrTasksKit.At(2031, 3, 10, 18, 0));
        var sender = new FakeTelegramSender();
        var now = HrTasksKit.At(2031, 3, 10, 10, 0);

        // begona chat — «topilmadi», qabul QILINMAYDI
        await using (var db = kit.NewContext())
            Assert.Equal(HrAckOutcome.NotFound, await HrTaskAck.HandleAsync(db, sender, stranger.Chat!.Value, "cb1", 77, task.Id, now));
        Assert.Equal(HrTaskAck.ToastNotFound, sender.Answers.Single().Text);
        Assert.Empty(sender.KeyboardsRemoved);

        await using (var db = kit.NewContext())
            Assert.Equal(HrAckOutcome.Accepted, await HrTaskAck.HandleAsync(db, sender, owner.Chat!.Value, "cb2", 88, task.Id, now));
        await using (var db = kit.NewContext())
            Assert.Equal(now, (await db.HrTasks.SingleAsync(t => t.Id == task.Id)).AcceptedAt);
        Assert.Equal((owner.Chat.Value, 88L), Assert.Single(sender.KeyboardsRemoved));
        Assert.Equal(HrTaskAck.ToastOk, sender.Answers.Last().Text);

        // ikkinchi bosish — birinchi vaqt saqlanadi
        await using (var db = kit.NewContext())
            Assert.Equal(HrAckOutcome.AlreadyAccepted,
                await HrTaskAck.HandleAsync(db, sender, owner.Chat.Value, "cb3", 88, task.Id, now.AddHours(1)));
        await using (var db = kit.NewContext())
            Assert.Equal(now, (await db.HrTasks.SingleAsync(t => t.Id == task.Id)).AcceptedAt);
    }

    [Fact]
    public async Task Qabul_qildim__bekor_qilingan_topshiriq_qabul_qilinmaydi()
    {
        using var kit = new HrTasksKit();
        var owner = await kit.PersonAsync(telegram: true);
        var task = await kit.TaskAsync(owner.Employee!.Id, HrTasksKit.At(2031, 3, 10, 18, 0), status: HrTaskStatuses.Cancelled);
        var sender = new FakeTelegramSender();
        await using var db = kit.NewContext();
        Assert.Equal(HrAckOutcome.Closed, await HrTaskAck.HandleAsync(db, sender, owner.Chat!.Value, "cb", 5, task.Id, DateTimeOffset.UtcNow));
        Assert.Null((await db.HrTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).AcceptedAt);
    }

    [Fact]
    public void Tugma_callback_Telegram_chegarasiga_sigadi()
    {
        // callback_data ≤ 64 bayt: prefiks + GUID
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(HrTaskAck.CallbackPrefix + Guid.NewGuid()) <= 64);
    }

    [Fact]
    public async Task Rasm_va_fayl_biriktirmalari_ham_botga_boradi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var e = await kit.PersonAsync(telegram: true);
        var photo = await kit.Service().UploadAttachmentAsync(panel,
            new HrUpload([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3], "rasm.jpg", "image/jpeg"), "file", null);
        var doc = await kit.Service().UploadAttachmentAsync(panel,
            new HrUpload("%PDF-1.4 test"u8.ToArray(), "reja.pdf", "application/pdf"), "file", null);

        await kit.Service().CreateTaskBatchAsync(panel, new HrTaskBatchInput([e.Employee!.Id],
        [
            new HrTaskBatchItemInput("Rasmli", "2031-03-11T10:00:00", "Izoh", null, null, null, photo.Id),
            new HrTaskBatchItemInput("Faylli", "2031-03-11T10:00:00", null, null, null, null, doc.Id),
        ]));
        Assert.Equal(2, kit.Sender.TaskButtons.Count);
        Assert.Equal("rasm.jpg", Assert.Single(kit.Sender.Photos).Name);
        Assert.Equal("reja.pdf", Assert.Single(kit.Sender.Documents).Name);
        Assert.Contains("📎 Biriktirma: 1 ta", kit.Sender.To(e.Chat!.Value)[0]);
    }
}
