using Regex = System.Text.RegularExpressions.Regex;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// QABUL CHEKLISTI (<see cref="StudentIntake"/>): har bandning AVTOMATIK aniqlanishi, QO'LDA
/// belgi (qo'yish / izoh / olib tashlash), ro'yxat qamrovi va jamlamasi, N+1 yo'qligi va
/// RUXSAT darvozasi. Qoidalar: <c>.claude/rules/student-intake.md</c>.
/// </summary>
public class StudentIntakeTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private static StudentIntake.Facts NoFacts(bool hasGroup = false) =>
        new(hasGroup, null, null, 0, null, new Dictionary<string, StudentIntakeMark>());

    private static Student Full(string name = "To'liq Ali") => new()
    {
        FullName = name,
        Phone = "+998-90-123-45-67",
        BirthDate = "2014-03-12",
        SchoolGrade = 6,
        SchoolGradeYear = 2026,
        EnrollmentDate = "2026-10-01",
    };

    // =============================================================================================
    //  1. "Ma'lumotlar to'ldirilgan" — majburiy maydonlar
    // =============================================================================================

    [Fact]
    public void Bosh_profilda_TORTALA_maydon_yetishmaydi()
    {
        var missing = StudentIntake.MissingProfileFields(new Student { FullName = "Bo'sh" }, hasGroup: false, Today);
        Assert.Equal(["phone", "birthDate", "schoolGrade", "group"], missing.Select(m => m.Key));
        Assert.All(missing, m => Assert.False(string.IsNullOrWhiteSpace(m.Label)));
    }

    [Fact]
    public void Toliq_profil_va_guruh__hech_narsa_yetishmaydi()
    {
        Assert.Empty(StudentIntake.MissingProfileFields(Full(), hasGroup: true, Today));
    }

    [Theory]
    [InlineData("", "+998-90-111-22-33", "", "")]   // faqat asosiy ota-ona raqami
    [InlineData("", "", "", "998901112233")]        // faqat onasi
    [InlineData("901112233", "", "", "")]           // mamlakat kodisiz 9 raqam
    public void Telefon__oquvchi_YOKI_ota_onadan_biri_yetadi(string phone, string parent, string father, string mother)
    {
        var s = Full();
        s.Phone = phone; s.ParentPhone = parent; s.FatherPhone = father; s.MotherPhone = mother;
        Assert.True(StudentIntake.HasPhone(s));
    }

    [Fact]
    public void Chala_telefon_va_buzuq_sana_TOLDIRILMAGAN_hisoblanadi()
    {
        var s = Full();
        s.Phone = "+998-90-12";        // chala
        s.BirthDate = "2014-13-45";    // buzuq
        var missing = StudentIntake.MissingProfileFields(s, hasGroup: true, Today).Select(m => m.Key).ToList();
        Assert.Contains("phone", missing);
        Assert.Contains("birthDate", missing);
        Assert.DoesNotContain("schoolGrade", missing);
    }

    [Fact]
    public void Maktabni_bitirgan_ham_sinf_kiritilgan_hisoblanadi()
    {
        var s = Full();
        s.SchoolGrade = SchoolGrades.Graduated;
        Assert.DoesNotContain(StudentIntake.MissingProfileFields(s, true, Today), m => m.Key == "schoolGrade");
    }

    // =============================================================================================
    //  2. Sof baholash — tafsilot matnlari va "bajarildi" = avtomatik YOKI qo'lda
    // =============================================================================================

    [Fact]
    public void Dalilsiz_oquvchi__barcha_bandlar_bajarilmagan_va_tartib_katalogdagidek()
    {
        var dto = StudentIntake.Evaluate(new Student { FullName = "X" }, NoFacts(), Today, [], "", null);
        Assert.Equal(StudentIntake.Items.Select(i => i.Key), dto.Items.Select(i => i.Key));
        Assert.All(dto.Items, i => Assert.False(i.Done));
        Assert.Equal(0, dto.Done);
        Assert.Equal(5, dto.Total);
        Assert.False(dto.Complete);
    }

    [Fact]
    public void Qolda_belgi__band_bajarilgan_lekin_AVTOMATIK_emas()
    {
        var marks = new Dictionary<string, StudentIntakeMark>
        {
            ["levelTest"] = new() { Item = "levelTest", Note = "qog'ozda, B1", MarkedByName = "Reception", MarkedAt = "2026-10-05T10:00:00" },
        };
        var f = NoFacts() with { Marks = marks };
        var item = StudentIntake.Evaluate(Full(), f, Today, [], "", null).Items.Single(i => i.Key == "levelTest");
        Assert.True(item.Done);
        Assert.False(item.Auto);
        Assert.NotNull(item.Mark);
        Assert.Equal("qog'ozda, B1", item.Mark!.Note);
        Assert.Equal("Reception", item.Mark.MarkedBy);
    }

    [Fact]
    public void Tafsilotlar__shartnoma_raqami_tolov_sanasi_va_chatlar_soni()
    {
        var f = new StudentIntake.Facts(
            true,
            new StudentIntake.LevelFact("B1", 72, "2026-10-02T09:15:00", true),
            new StudentIntake.ContractFact(12, "2026-10-03"),
            2,
            new StudentIntake.PaymentFact("2026-10-04", 3),
            new Dictionary<string, StudentIntakeMark>());
        var items = StudentIntake.Evaluate(Full(), f, Today, ["Ingliz A1"], "active", "lead-1").Items
            .ToDictionary(i => i.Key);

        Assert.Equal("Daraja: B1 · 72% · 02.10.2026", items["levelTest"].Detail);
        Assert.Equal("№ 12 · 03.10.2026", items["contract"].Detail);
        Assert.Equal("Bot orqali ulangan (2 ta chat)", items["telegram"].Detail);
        Assert.Equal("Birinchi to'lov: 04.10.2026 · jami 3 ta", items["payment"].Detail);
        Assert.Equal("Hammasi to'ldirilgan", items["profile"].Detail);
        Assert.All(items.Values, i => Assert.True(i.Done && i.Auto));
    }

    // =============================================================================================
    //  3. Qamrov (kim "yangi o'quvchi")
    // =============================================================================================

    [Fact]
    public void Qabul_sanasi__ENROLLMENT_birinchi__CreatedAt_faqat_zaxira()
    {
        // Ommaviy import: CreatedAt = bugun, lekin markazga 2024 da kelgan — "yangi" EMAS.
        Assert.Equal("2024-09-01", StudentIntake.SinceOf("2024-09-01", "2026-10-05T12:00:00"));
        Assert.Equal("2026-10-05", StudentIntake.SinceOf("", "2026-10-05T12:00:00"));
        Assert.Equal("", StudentIntake.SinceOf("", ""));
    }

    [Theory]
    [InlineData(false, false, "2026-09-01", "2026-08-07", true)]   // oyna ichida
    [InlineData(false, false, "2026-08-07", "2026-08-07", true)]   // chegara kuni ham kiradi
    [InlineData(false, false, "2026-08-06", "2026-08-07", false)]  // oynadan tashqari
    [InlineData(false, true, "2024-01-01", "2026-08-07", true)]    // SINOVDAGI — oynadan qat'i nazar
    [InlineData(false, true, "", "2026-08-07", true)]
    [InlineData(true, true, "2026-10-01", "2026-08-07", false)]    // arxivdagi — hech qachon
    [InlineData(false, false, "", "2026-08-07", false)]            // sanasiz va sinovda emas
    public void Qamrov_qoidasi(bool archived, bool trial, string since, string from, bool expected)
    {
        Assert.Equal(expected, StudentIntake.InCohort(archived, trial, since, from));
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(1, 7)]
    [InlineData(90, 90)]
    [InlineData(5000, 365)]
    public void Oyna_chegaralanadi(int? days, int expected) =>
        Assert.Equal(expected, StudentIntake.ClampDays(days));

    // =============================================================================================
    //  4. Bazadan AVTOMATIK aniqlash (har band)
    // =============================================================================================

    private sealed record World(Student Done, Student Empty, Group G);

    private static World Seed(AppDbContext ctx)
    {
        var g = new Group { Name = "Ingliz A1", MonthlyFee = 400_000m };
        var done = Full("Hammasi bor");
        var empty = new Student { FullName = "Hech narsa yo'q", EnrollmentDate = "2026-10-02" };
        ctx.AddRange(g, done, empty);

        ctx.StudentGroups.Add(new StudentGroup { StudentId = done.Id, GroupId = g.Id, Status = "active", ActivatedAt = "2026-10-01" });

        // Daraja testi — LID orqali (o'quvchiga aylantirilgan).
        var lead = new Lead { FullName = done.FullName, Phone = done.Phone, ConvertedStudentId = done.Id, CreatedAt = "2026-09-28T10:00:00" };
        ctx.Leads.Add(lead);
        ctx.LevelTestSubmissions.Add(new LevelTestSubmission
        {
            LeadId = lead.Id, FullName = done.FullName, Level = "A2", Percent = 64, CreatedAt = "2026-09-28T10:05:00",
        });

        ctx.Contracts.Add(new Contract
        {
            Target = "parent", RecipientKey = done.Id, Number = 7, SentAt = new DateTime(2026, 10, 1),
            DocxUrl = "/uploads/secret-contract.docx", PdfUrl = "/uploads/secret-contract.pdf",
        });
        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = done.Id, ChatId = 111, Phone = "998901234567" });
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = done.Id, Direction = "income", Category = "tuition", Amount = 400_000m, Date = "2026-10-01",
        });

        // "Bo'sh" o'quvchiga TEGISHLI BO'LMAGAN / hisobga olinmaydigan yozuvlar:
        ctx.Contracts.Add(new Contract { Target = "staff", RecipientKey = empty.Id, Number = 8 });     // xodim shartnomasi
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = empty.Id, Direction = "income", Category = "book", Amount = 50_000m, Date = "2026-10-03",
        });                                                                                             // kitob — o'quv to'lovi emas
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = empty.Id, Direction = "income", Category = "tuition", Amount = 400_000m, Date = "2026-10-03",
            IsVoided = true, VoidedAt = "2026-10-03T12:00:00",
        });                                                                                             // bekor qilingan
        // Guruh imtihoni — daraja testi EMAS.
        var tr = new TestResult { GroupId = g.Id, Name = "Oylik imtihon", Date = "2026-10-04", MaxScore = 100 };
        ctx.TestResults.Add(tr);
        ctx.TestScores.Add(new TestScore { TestResultId = tr.Id, StudentId = empty.Id, Score = 90 });

        ctx.SaveChanges();
        return new World(done, empty, g);
    }

    [Fact]
    public async Task Barcha_bandlar_mavjud_yozuvlardan_AVTOMATIK_aniqlanadi()
    {
        using var db = TestDb.Sqlite();
        var w = Seed(db.Context);

        var dto = (await StudentIntake.ForStudentAsync(db.Context, w.Done.Id, Today))!;
        Assert.True(dto.Complete);
        Assert.Equal(5, dto.Done);
        Assert.All(dto.Items, i => Assert.True(i.Auto, $"{i.Key} avtomatik aniqlanmadi"));
        Assert.Equal(["Ingliz A1"], dto.Groups);
        Assert.Equal("active", dto.MemberState);
        Assert.NotNull(dto.LeadId);

        var items = dto.Items.ToDictionary(i => i.Key);
        Assert.Equal("Daraja: A2 · 64% · 28.09.2026", items["levelTest"].Detail);
        Assert.Equal("№ 7 · 01.10.2026", items["contract"].Detail);
        Assert.Equal("Birinchi to'lov: 01.10.2026", items["payment"].Detail);
    }

    [Fact]
    public async Task Begona_va_hisobga_olinmaydigan_yozuvlar_bandni_BAJARMAYDI()
    {
        using var db = TestDb.Sqlite();
        var w = Seed(db.Context);

        var dto = (await StudentIntake.ForStudentAsync(db.Context, w.Empty.Id, Today))!;
        Assert.Equal(0, dto.Done);
        var items = dto.Items.ToDictionary(i => i.Key);
        Assert.False(items["contract"].Done);   // xodim shartnomasi hisob emas
        Assert.False(items["payment"].Done);    // kitob to'lovi va BEKOR QILINGAN to'lov hisob emas
        Assert.False(items["levelTest"].Done);  // guruh imtihoni daraja testi emas
        Assert.Equal(["phone", "birthDate", "schoolGrade", "group"], items["profile"].Missing.Select(m => m.Key));
    }

    [Fact]
    public async Task Lid_kartasidagi_daraja__testsiz_ham_bajarilgan()
    {
        using var db = TestDb.Sqlite();
        var s = Full();
        db.Context.Students.Add(s);
        db.Context.Leads.Add(new Lead { FullName = s.FullName, ConvertedStudentId = s.Id, Level = "Elementary", CreatedAt = "2026-09-01T09:00:00" });
        await db.Context.SaveChangesAsync();

        var item = (await StudentIntake.ForStudentAsync(db.Context, s.Id, Today))!.Items.Single(i => i.Key == "levelTest");
        Assert.True(item.Done && item.Auto);
        Assert.Equal("Daraja: Elementary (lid kartasida)", item.Detail);
    }

    [Fact]
    public async Task Arxivlangan_guruhdagi_azolik_GURUH_hisoblanmaydi__chiqib_ketgani_ham()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var archived = new Group { Name = "Eski", IsArchived = true };
        var live = new Group { Name = "Yangi" };
        var a = Full("Arxiv guruhda");
        var b = Full("Chiqib ketgan");
        var c = Full("Sinovda");
        ctx.AddRange(archived, live, a, b, c);
        ctx.StudentGroups.Add(new StudentGroup { StudentId = a.Id, GroupId = archived.Id, Status = "active" });
        ctx.StudentGroups.Add(new StudentGroup { StudentId = b.Id, GroupId = live.Id, Status = "active", IsActive = false, LeftAt = "2026-10-02" });
        ctx.StudentGroups.Add(new StudentGroup { StudentId = c.Id, GroupId = live.Id, Status = "trial" });
        await ctx.SaveChangesAsync();

        var rows = (await StudentIntake.BuildAsync(ctx, [a, b, c], Today)).ToDictionary(r => r.FullName);
        Assert.Contains(rows["Arxiv guruhda"].Items[0].Missing, m => m.Key == "group");
        Assert.Contains(rows["Chiqib ketgan"].Items[0].Missing, m => m.Key == "group");
        Assert.True(rows["Sinovda"].Items[0].Done);   // sinov a'zoligi ham guruh
    }

    [Fact]
    public async Task Javobda_fayl_MANZILI_yoq()
    {
        using var db = TestDb.Sqlite();
        var w = Seed(db.Context);
        var json = JsonSerializer.Serialize(await StudentIntake.ForStudentAsync(db.Context, w.Done.Id, Today));
        Assert.DoesNotContain("/uploads", json);
        Assert.DoesNotContain("secret-contract", json);
    }

    // =============================================================================================
    //  5. QO'LDA belgi
    // =============================================================================================

    [Fact]
    public async Task Qolda_belgi__qoyish_izoh_olib_tashlash_va_idempotentlik()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = Full();
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();

        // Qo'yish
        var c1 = await StudentIntake.ApplyMarkAsync(ctx, s.Id, "levelTest", true, "  qog'ozda olindi  ", "u1", "Reception");
        Assert.Equal("create", c1!.Action);
        Assert.Contains("Daraja testi olingan", c1.Summary);
        await ctx.SaveChangesAsync();

        var item = (await StudentIntake.ForStudentAsync(ctx, s.Id, Today))!.Items.Single(i => i.Key == "levelTest");
        Assert.True(item.Done);
        Assert.False(item.Auto);
        Assert.Equal("qog'ozda olindi", item.Mark!.Note);
        Assert.Equal("Reception", item.Mark.MarkedBy);

        // Bir xil izoh bilan qayta — o'zgarish YO'Q (tarixga ortiqcha qator tushmasin).
        Assert.Null(await StudentIntake.ApplyMarkAsync(ctx, s.Id, "levelTest", true, "qog'ozda olindi", "u1", "Reception"));

        // Izohni o'zgartirish — "update", qator bitta qoladi.
        var c2 = await StudentIntake.ApplyMarkAsync(ctx, s.Id, "levelTest", true, "B1 chiqdi", "u2", "Boshqa");
        Assert.Equal("update", c2!.Action);
        await ctx.SaveChangesAsync();
        Assert.Equal(1, await ctx.StudentIntakeMarks.CountAsync(m => m.StudentId == s.Id));

        // Olib tashlash
        var c3 = await StudentIntake.ApplyMarkAsync(ctx, s.Id, "levelTest", false, null, "u1", "Reception");
        Assert.Equal("delete", c3!.Action);
        await ctx.SaveChangesAsync();
        Assert.False((await StudentIntake.ForStudentAsync(ctx, s.Id, Today))!.Items.Single(i => i.Key == "levelTest").Done);

        // Yo'q belgini olib tashlash — jim (idempotent).
        Assert.Null(await StudentIntake.ApplyMarkAsync(ctx, s.Id, "levelTest", false, null, "u1", "Reception"));
    }

    [Fact]
    public async Task Avtomatik_bajarilgan_band_qolda_belgi_olib_tashlansa_ham_bajarilgan_qoladi()
    {
        using var db = TestDb.Sqlite();
        var w = Seed(db.Context);
        await StudentIntake.ApplyMarkAsync(db.Context, w.Done.Id, "contract", true, "qo'lda ham", "u", "R");
        await db.Context.SaveChangesAsync();
        await StudentIntake.ApplyMarkAsync(db.Context, w.Done.Id, "contract", false, null, "u", "R");
        await db.Context.SaveChangesAsync();

        var item = (await StudentIntake.ForStudentAsync(db.Context, w.Done.Id, Today))!.Items.Single(i => i.Key == "contract");
        Assert.True(item.Done && item.Auto);
        Assert.Null(item.Mark);
    }

    [Fact]
    public async Task Nomalum_band_rad_etiladi__izoh_chegarasi()
    {
        using var db = TestDb.Sqlite();
        var s = Full();
        db.Context.Students.Add(s);
        await db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            StudentIntake.ApplyMarkAsync(db.Context, s.Id, "passport", true, null, "u", "R"));
        Assert.False(StudentIntake.IsKnownItem("passport"));
        Assert.False(StudentIntake.IsKnownItem(null));

        await StudentIntake.ApplyMarkAsync(db.Context, s.Id, "telegram", true, new string('x', 2000), "u", "R");
        await db.Context.SaveChangesAsync();
        Assert.Equal(StudentIntake.MaxNoteLength, (await db.Context.StudentIntakeMarks.SingleAsync()).Note.Length);
    }

    [Fact]
    public async Task Oquvchi_BUTUNLAY_ochirilsa_belgilari_ham_ochadi__boshqaniki_qoladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = Full("Dublikat");
        var other = Full("Haqiqiy");
        ctx.AddRange(s, other);
        ctx.StudentIntakeMarks.Add(new StudentIntakeMark { StudentId = s.Id, Item = "contract" });
        ctx.StudentIntakeMarks.Add(new StudentIntakeMark { StudentId = other.Id, Item = "contract" });
        await ctx.SaveChangesAsync();

        var preview = await StudentPurge.PreviewAsync(ctx, s.Id);
        Assert.Equal(1, preview!.Items.Single(i => i.Key == "intake").Count);

        await StudentPurge.PurgeAsync(ctx, s.Id);
        await ctx.SaveChangesAsync();
        Assert.False(await ctx.StudentIntakeMarks.AnyAsync(m => m.StudentId == s.Id));
        Assert.True(await ctx.StudentIntakeMarks.AnyAsync(m => m.StudentId == other.Id));
    }

    [Fact]
    public async Task Bitta_oquvchi_bitta_band__IKKINCHI_qator_baza_darajasida_rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.StudentIntakeMarks.Add(new StudentIntakeMark { StudentId = "s1", Item = "contract" });
        db.Context.StudentIntakeMarks.Add(new StudentIntakeMark { StudentId = "s1", Item = "contract" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    // =============================================================================================
    //  6. RO'YXAT — qamrov, saralash, jamlama
    // =============================================================================================

    [Fact]
    public async Task Royxat__qamrov_saralash_va_band_boyicha_jamlama()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var w = Seed(ctx);                                                  // Done (01.10) + Empty (02.10)

        var g = w.G;
        var oldTrial = new Student { FullName = "Eski sinovdagi", EnrollmentDate = "2025-01-10" };
        var oldActive = Full("Eski aktiv");
        oldActive.EnrollmentDate = "2025-01-10";
        var archived = new Student { FullName = "Arxivdagi", EnrollmentDate = "2026-10-03", IsArchived = true };
        var imported = new Student { FullName = "Import", EnrollmentDate = "2023-05-01", CreatedAt = "2026-10-05T08:00:00" };
        ctx.AddRange(oldTrial, oldActive, archived, imported);
        ctx.StudentGroups.Add(new StudentGroup { StudentId = oldTrial.Id, GroupId = g.Id, Status = "trial" });
        ctx.StudentGroups.Add(new StudentGroup { StudentId = oldActive.Id, GroupId = g.Id, Status = "active" });
        await ctx.SaveChangesAsync();

        var list = await StudentIntake.ListAsync(ctx, 30, Today);

        Assert.Equal(30, list.Days);
        Assert.Equal("2026-09-06", list.From);
        Assert.Equal(
            ["Hech narsa yo'q", "Eski sinovdagi", "Hammasi bor"],
            list.Items.Select(r => r.FullName));                           // to'liq emaslar tepada, keyin eng yangisi
        Assert.Equal(3, list.Total);
        Assert.Equal(2, list.Incomplete);
        Assert.False(list.Truncated);
        Assert.Equal(StudentIntake.Items.Select(i => i.Key), list.PendingByItem.Keys);
        Assert.Equal(2, list.PendingByItem["contract"]);
        Assert.Equal(2, list.PendingByItem["profile"]);
        // Sinovdagi o'quvchining guruhi bor, lekin telefoni/sanasi/sinfi yo'q.
        Assert.Equal(["phone", "birthDate", "schoolGrade"],
            list.Items.Single(r => r.FullName == "Eski sinovdagi").Items[0].Missing.Select(m => m.Key));
    }

    // =============================================================================================
    //  7. N+1 YO'Q — so'rovlar soni o'quvchilar soniga bog'liq emas
    // =============================================================================================

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref Count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Sorovlar_soni_oquvchilar_soniga_BOGLIQ_EMAS()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var counter = new CommandCounter();
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn).AddInterceptors(counter).Options);
        ctx.Database.EnsureCreated();

        var g = new Group { Name = "G" };
        ctx.Add(g);
        var students = Enumerable.Range(0, 30).Select(i => Full($"O'quvchi {i:00}")).ToList();
        ctx.AddRange(students);
        foreach (var s in students)
        {
            ctx.StudentGroups.Add(new StudentGroup { StudentId = s.Id, GroupId = g.Id, Status = "trial" });
            var lead = new Lead { FullName = s.FullName, ConvertedStudentId = s.Id };
            ctx.Leads.Add(lead);
            ctx.LevelTestSubmissions.Add(new LevelTestSubmission { LeadId = lead.Id, Level = "A1" });
            ctx.Contracts.Add(new Contract { Target = "parent", RecipientKey = s.Id, Number = students.IndexOf(s) + 1 });
            ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = students.IndexOf(s) + 1 });
            ctx.FinanceTransactions.Add(new FinanceTransaction { StudentId = s.Id, Direction = "income", Category = "tuition", Amount = 1, Date = "2026-10-01" });
            ctx.StudentIntakeMarks.Add(new StudentIntakeMark { StudentId = s.Id, Item = "telegram" });
        }
        await ctx.SaveChangesAsync();

        counter.Count = 0;
        await StudentIntake.BuildAsync(ctx, students.Take(2).ToList(), Today);
        var few = counter.Count;

        counter.Count = 0;
        var all = await StudentIntake.BuildAsync(ctx, students, Today);
        Assert.Equal(few, counter.Count);
        Assert.All(all, r => Assert.True(r.Complete));
    }

    // =============================================================================================
    //  8. RUXSAT (RBAC) — darvoza va audit bo'limi
    // =============================================================================================

    private static string ControllerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        var path = Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "StudentIntakeController.cs");
        Assert.True(File.Exists(path), $"Controller topilmadi: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Controller__students_list_kaliti__oqish_ham_darvozalangan__yagona_yozish_PUT()
    {
        var src = ControllerSource();
        var header = src[..src.IndexOf("public class StudentIntakeController", StringComparison.Ordinal)];
        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"students.list\", ReadRequiresPerm = true)]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
        // Metod darajasidagi [AdminPerm] sinfdagisini BEKOR qilardi (permissions.md §4).
        Assert.Single(Regex.Matches(src, @"\[AdminPerm\("));
        // Qo'lda belgi FAQAT PUT (= "edit" amali); POST/DELETE yo'q — aks holda "create"/"delete"
        // ruxsatli xodim ham belgilay olardi.
        Assert.Single(Regex.Matches(src, @"\[Http(Post|Put|Patch|Delete)"));
        Assert.Contains("[HttpPut(\"{studentId}/{item}\")]", src);
        // Audit saqlashdan OLDIN yoziladi.
        Assert.True(src.IndexOf("audit.Record(\"StudentIntake\"", StringComparison.Ordinal)
                    < src.IndexOf("SaveChangesAsync", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new[] { "students" }, true, true)]                  // butun bo'lim
    [InlineData(new[] { "students.list" }, true, true)]             // sahifa — barcha amallar
    [InlineData(new[] { "students.list:edit" }, true, true)]        // tahrir
    [InlineData(new[] { "students:edit" }, true, true)]             // bo'lim tahriri — pastga meros
    [InlineData(new[] { "students.list:create" }, true, false)]     // faqat qo'shish — ko'radi, belgilay olmaydi
    [InlineData(new[] { "students.turnstile" }, false, false)]      // boshqa sahifa — ko'rmaydi ham
    [InlineData(new[] { "leads", "finance" }, false, false)]        // boshqa bo'limlar
    public void Xodim_ruxsati__oqish_va_qolda_belgi(string[] perms, bool canRead, bool canMark)
    {
        Assert.Equal(canRead, PermissionRules.HasSection(perms, "students.list"));
        Assert.Equal(canMark, PermissionRules.CanWrite(perms, "students.list", "PUT"));
    }

    [Fact]
    public void Audit_turi_oquvchilar_bolimiga_tushadi() =>
        Assert.Equal("students", AuditSections.SectionOf("StudentIntake"));
}
