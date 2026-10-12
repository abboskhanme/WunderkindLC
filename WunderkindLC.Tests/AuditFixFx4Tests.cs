using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;
using Group = WunderkindLC.Domain.Group;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-08 — post-merge sharh tuzatishlari (FX4):
/// <list type="bullet">
///   <item>R2-4 — ommaviy davomat xabarlari BAZADAGI navbat orqali (<see cref="AttendanceNotifyItem"/>,
///     <see cref="AttendanceNotifyWorker"/>): jurnal bilan bitta SaveChanges, restartda yo'qolmaydi, takror yo'q;</item>
///   <item>R2-5 — <see cref="JobRunMarker"/> / <c>TryMarkAsync</c>: kontekstdagi "buzuq" qoldiq band qilishni
///     "allaqachon band" deb o'qitmaydi, haqiqiy poyga — false, boshqa xato — tashqariga;</item>
///   <item>(R2-7 SMS navbati va R3-3 / R3-7 standart Local SMS agenti testlari 2026-10 da SMS / Local SMS
///     bilan birga olib tashlandi);</item>
///   <item>R3-5 — Telegram zaxirasi GZIP, og'ir jadvallar ro'yxati.</item>
/// </list>
/// </summary>
public class AuditFixFx4Tests
{
    private static string Day(int offset) => AppClock.Today.AddDays(offset).ToString("yyyy-MM-dd");

    private static Group AddGroup(TestDb db)
    {
        var subject = new Subject { Name = "Ingliz tili" };
        db.Context.Subjects.Add(subject);
        var g = new Group
        {
            Name = "Ingliz A1", CourseId = subject.Id, StartDate = Day(-90),
            Days = Enumerable.Range(0, 7).ToList(), StartTime = "14:00", EndTime = "15:30", TeacherId = "t-1",
        };
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return g;
    }

    private static Student AddStudent(TestDb db, Group g, string name, string userId)
    {
        var s = new Student { FullName = name, UserId = userId, ParentPhone = "901112233" };
        db.Context.Students.Add(s);
        db.Context.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active",
            JoinedAt = Day(-60), ActivatedAt = Day(-60), RecordedAt = Day(-60),
        });
        db.Context.SaveChanges();
        return s;
    }

    private static AbsenceReason Reason(TestDb db, string name = "Sababsiz")
    {
        var r = new AbsenceReason { Name = name, Short = "S" };
        db.Context.AbsenceReasons.Add(r);
        db.Context.SaveChanges();
        return r;
    }

    private static void AddRule(TestDb db, string trigger)
    {
        db.Context.AutoMessageRules.Add(new AutoMessageRule
        {
            Trigger = trigger, Enabled = true, Audience = "parents",
            Template = "{fish} — {sana} davomat",
        });
        db.Context.SaveChanges();
    }

    private static AttendanceNotifyWorker Worker(IAppDbContext ctx) =>
        new(new SingleServiceProvider(ctx), new MessagingStack().Auto, NullLogger<AttendanceNotifyWorker>.Instance);

    /// <summary>Admin/o'qituvchi controllerlari bilan AYNI yo'l: navbat `beforeSave` da qo'shiladi.</summary>
    private static Task<JournalService.BulkAttendanceResult> Bulk(
        TestDb db, BulkAttendanceRequest req, IReadOnlySet<string>? wasPresent = null) =>
        JournalService.BulkAttendanceAsync(db.Context, req,
            beforeSave: r => AttendanceNotify.EnqueueBulkAsync(db.Context, req, r, wasPresent ?? new HashSet<string>()));

    /* =========================================================================================
     *  R2-4 — OMMAVIY DAVOMAT XABARLARI NAVBATI
     * ========================================================================================= */

    [Fact]
    public async Task Ommaviy_KELMADI_navbati_jurnal_bilan_BIRGA_saqlanadi_va_ishchi_bir_marta_yuboradi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var vali = AddStudent(db, g, "Vali", "u-vali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);

        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Day(0), 1,
            new List<string> { ali.Id, vali.Id }, sababsiz.Id, Absent: true));

        // Alohida saqlashsiz — navbat jurnal yozuvi bilan bitta SaveChanges da bazaga tushgan.
        await using (var read = db.NewContext())
        {
            var items = await read.AttendanceNotifyItems.ToListAsync();
            Assert.Equal(2, items.Count);
            Assert.All(items, i => Assert.Equal("pending", i.Status));
            Assert.All(items, i => Assert.Equal(AttendanceNotify.KindAbsent, i.Kind));
            Assert.Equal(2, await read.JournalEntries.CountAsync());
        }
        Assert.Empty(db.Context.UserNotifications);   // so'rovning o'zida hech narsa yuborilmadi

        var worker = Worker(db.Context);
        Assert.Equal(2, await worker.ProcessPendingAsync(CancellationToken.None));
        var users = db.Context.UserNotifications.Select(n => n.UserId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "u-ali", "u-vali" }, users);
        Assert.All(db.Context.AttendanceNotifyItems.ToList(), i => Assert.Equal("done", i.Status));

        // Takror ishlov (masalan restart) — hech kimga ikkinchi xabar yo'q.
        Assert.Equal(0, await Worker(db.Context).ProcessPendingAsync(CancellationToken.None));
        Assert.Equal(2, db.Context.UserNotifications.Count());
    }

    [Fact]
    public async Task Ommaviy_davomat_qoida_YOQ_bolsa_navbatga_hech_narsa_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var sababsiz = Reason(db);

        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Day(0), 1,
            new List<string> { ali.Id }, sababsiz.Id, Absent: true));

        Assert.Empty(db.Context.AttendanceNotifyItems);
        Assert.Single(db.Context.JournalEntries);
    }

    [Fact]
    public async Task Ommaviy_davomat_rad_etilsa_navbat_ham_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bulk(db, new BulkAttendanceRequest(
            g.Id, g.CourseId!, Day(2), 1, new List<string> { ali.Id }, null, Absent: true)));   // kelajak sana

        await using var read = db.NewContext();
        Assert.Empty(await read.AttendanceNotifyItems.ToListAsync());
    }

    [Fact]
    public async Task Ommaviy_KELDI_faqat_yangi_kelganlarga_va_arxivlanganga_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var oldin = AddStudent(db, g, "Oldin kelgan", "u-oldin");
        var yangi = AddStudent(db, g, "Yangi", "u-yangi");
        var ketgan = AddStudent(db, g, "Arxivga tushadi", "u-ketgan");
        AddRule(db, AutoMessageTriggers.AttendancePresent);

        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Day(0), 1,
                new List<string> { oldin.Id, yangi.Id, ketgan.Id }, null),
            wasPresent: new HashSet<string> { oldin.Id });

        var queued = db.Context.AttendanceNotifyItems.Select(i => i.StudentId).ToHashSet();
        Assert.Equal(new HashSet<string> { yangi.Id, ketgan.Id }, queued);   // oldin kelganga takror yo'q

        // Yuborishdan oldin arxivlandi — holat YUBORISH paytida qayta tekshiriladi.
        ketgan.IsArchived = true;
        db.Context.SaveChanges();

        await Worker(db.Context).ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(new[] { "u-yangi" }, db.Context.UserNotifications.Select(n => n.UserId).ToArray());
    }

    [Fact]
    public async Task Navbat_RESTARTDAN_keyin_qolganini_yuboradi_uzilganini_QAYTA_yubormaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var vali = AddStudent(db, g, "Vali", "u-vali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Day(0), 1,
            new List<string> { ali.Id, vali.Id }, sababsiz.Id, Absent: true));

        // Oldingi jarayon Ali'ga yuborayotganda (band qilgan, lekin tugatmagan) o'chdi.
        var aliItem = db.Context.AttendanceNotifyItems.Single(i => i.StudentId == ali.Id);
        aliItem.Status = "sending";
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();

        var worker = Worker(db.Context);   // YANGI jarayon
        await worker.RecoverAsync(CancellationToken.None);
        await worker.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(new[] { "u-vali" }, db.Context.UserNotifications.Select(n => n.UserId).ToArray());
        var statuses = db.Context.AttendanceNotifyItems.ToDictionary(i => i.StudentId, i => i.Status);
        Assert.Equal("unknown", statuses[ali.Id]);
        Assert.Equal("done", statuses[vali.Id]);
    }

    [Fact]
    public async Task Navbatdagi_juda_ESKI_xabar_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Day(0), 1,
            new List<string> { ali.Id }, sababsiz.Id, Absent: true));
        var item = db.Context.AttendanceNotifyItems.Single();
        item.CreatedAt = AppClock.Now - AttendanceNotifyWorker.MaxAge - TimeSpan.FromMinutes(5);
        db.Context.SaveChanges();

        await Worker(db.Context).ProcessPendingAsync(CancellationToken.None);

        Assert.Empty(db.Context.UserNotifications);
        Assert.Equal("expired", db.Context.AttendanceNotifyItems.Single().Status);
    }

    /* =========================================================================================
     *  R2-5 — BAND QILISH: qoldiq ≠ "allaqachon band"
     * ========================================================================================= */

    /// <summary>Kontekstga saqlanmaydigan qator qo'yadi (unikal kalit takrori) — dispatcher o'z saqlash
    /// xatosini yutgandan keyin qoladigan holat.</summary>
    private static void PoisonContext(AppDbContext ctx)
    {
        ctx.LessonReminderMarks.Add(new LessonReminderMark { Key = "dup", Date = Day(0) });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        ctx.LessonReminderMarks.Add(new LessonReminderMark { Key = "dup", Date = Day(0) });   // saqlanmaydi
    }

    [Fact]
    public async Task JobRunMarker_kontekstdagi_BUZUQ_qoldiq_bandni_yiqitmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var d = AppClock.Today;
        PoisonContext(ctx);

        // Ilgari: qoldiq tufayli SaveChanges yiqilar va bu "boshqa nusxa band qildi" deb o'qilardi —
        // qolgan HAMMA oluvchi o'tkazib yuborilardi.
        Assert.True(await JobRunMarker.TryClaimAsync(ctx, "payment_debt:s1", d));
        Assert.True(await JobRunMarker.TryClaimAsync(ctx, "payment_debt:s2", d));
        Assert.False(await JobRunMarker.TryClaimAsync(ctx, "payment_debt:s1", d));   // haqiqiy takror — yo'q

        await using var read = db.NewContext();
        Assert.Equal(2, await read.BackgroundJobRuns.CountAsync());
        Assert.Equal(1, await read.LessonReminderMarks.CountAsync());   // buzuq qator saqlanmadi
    }

    [Fact]
    public async Task JobRunMarker_vaqtinchalik_saqlanmagan_qoldiq_YOQOLMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.UserNotifications.Add(new UserNotification { UserId = "u-1", Title = "T", Body = "B" });   // saqlanmagan

        Assert.True(await JobRunMarker.TryClaimAsync(ctx, "birthday:s1", AppClock.Today));

        await using var read = db.NewContext();
        Assert.Equal(1, await read.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task JobRunMarker_haqiqiy_POYGA_false_qaytaradi()
    {
        using var db = TestDb.Sqlite();
        var d = AppClock.Today;
        Assert.True(await JobRunMarker.TryClaimAsync(db.Context, "trial_reminder:t1", d.AddDays(-1)));

        // Ikkinchi nusxa (boshqa kontekst) bugungi kunni band qilib ulgurdi; birinchisida qator eski holatda kuzatuvda.
        await using (var other = db.NewContext())
            Assert.True(await JobRunMarker.TryClaimAsync(other, "trial_reminder:t1", d));

        Assert.False(await JobRunMarker.TryClaimAsync(db.Context, "trial_reminder:t1", d));
    }

    private sealed class FailingJobRunInsert : SaveChangesInterceptor
    {
        public bool Fail = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Fail && eventData.Context!.ChangeTracker.Entries<BackgroundJobRun>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("baza vaqtincha yozmayapti");
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    [Fact]
    public async Task JobRunMarker_POYGA_bolmagan_xato_TASHQARIGA_chiqadi_va_band_qilinmaydi()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        var interceptor = new FailingJobRunInsert();
        await using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options);
        ctx.Database.EnsureCreated();
        var d = AppClock.Today;

        // Ilgari: false ("allaqachon band") — xizmat oluvchini o'tkazib yuborar va kunni "bajarildi" deb belgilardi.
        await Assert.ThrowsAsync<DbUpdateException>(() => JobRunMarker.TryClaimAsync(ctx, "payment_debt", d));
        Assert.Empty(ctx.ChangeTracker.Entries<BackgroundJobRun>());   // keyingi saqlashni yiqitmaydi

        interceptor.Fail = false;
        Assert.True(await JobRunMarker.TryClaimAsync(ctx, "payment_debt", d));   // baza tiklandi — band qilinadi
    }

    [Fact]
    public async Task Davomat_eslatmasi_belgisi_qoldiq_tufayli_otkazib_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.BackgroundJobRuns.Add(new BackgroundJobRun { Key = "p", LastRunDate = Day(0) });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        ctx.BackgroundJobRuns.Add(new BackgroundJobRun { Key = "p", LastRunDate = Day(0) });   // saqlanmaydigan qoldiq

        var key = $"rule-1:g-1:{Day(0)}";
        Assert.True(await LessonAttendanceReminderService.TryMarkAsync(ctx, key, Day(0), CancellationToken.None));
        Assert.False(await LessonAttendanceReminderService.TryMarkAsync(ctx, key, Day(0), CancellationToken.None));

        await using var read = db.NewContext();
        Assert.True(await read.LessonReminderMarks.AnyAsync(m => m.Key == key));
    }

    /* =========================================================================================
     *  R3-5 — Telegram zaxirasi GZIP
     * ========================================================================================= */

    [Fact]
    public async Task Zaxira_GZIP_ochilganda_ayni_JSON_va_texnik_navbat_kiritilmagan()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(new CenterMeta { Name = "Markaz", InstagramCapiToken = "CAPI-SECRET-777" });
        for (var i = 0; i < 50; i++) db.Context.Students.Add(new Student { FullName = $"O'quvchi {i}" });
        await db.Context.SaveChangesAsync();

        var (gz, tables, rows) = await BackupService.BuildGzipAsync(db.Context);
        var (raw, rawTables, rawRows) = await BackupService.BuildJsonAsync(db.Context);

        using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        using var unpacked = new MemoryStream();
        await input.CopyToAsync(unpacked);
        using var doc = JsonDocument.Parse(unpacked.ToArray());
        var root = doc.RootElement;

        Assert.True(gz.Length < raw.Length);                         // siqilgan
        Assert.Equal(rawTables, tables);
        Assert.Equal(rawRows, rows);
        Assert.Equal(50, root.GetProperty("students").GetArrayLength());
        Assert.DoesNotContain("CAPI-SECRET-777", System.Text.Encoding.UTF8.GetString(unpacked.ToArray()));
        Assert.Contains(root.GetProperty("excluded").EnumerateArray(), e => e.GetString() == "attendanceNotifyItems");
    }

    [Fact]
    public async Task Zaxira_ixcham_nusxada_ASOSIY_pul_va_jurnal_qoladi_qongiroqlar_chiqadi()
    {
        Assert.Contains("calls", BackupService.HeavyTables);
        Assert.Contains("ctiCallRecords", BackupService.HeavyTables);
        Assert.DoesNotContain("financeTransactions", BackupService.HeavyTables);
        Assert.DoesNotContain("journalEntries", BackupService.HeavyTables);
        Assert.DoesNotContain("monthlyCharges", BackupService.HeavyTables);

        using var db = TestDb.Sqlite();
        var (gz, _, _) = await BackupService.BuildGzipAsync(db.Context, includeHeavy: false);
        using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        using var doc = await JsonDocument.ParseAsync(input);
        Assert.False(doc.RootElement.TryGetProperty("calls", out _));
        Assert.True(doc.RootElement.TryGetProperty("financeTransactions", out _));
        Assert.True(doc.RootElement.TryGetProperty("journalEntries", out _));
    }
}
