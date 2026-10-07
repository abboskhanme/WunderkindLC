using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;
using Group = WunderkindLC.Domain.Group;

namespace WunderkindLC.Tests;

/// <summary>
/// «KETISH SABABLARI» (Retention / Loss) hisoboti — <see cref="RetentionLossReport"/> va sabab
/// manbalari <see cref="MembershipReasons"/>.
///
/// <para>Qulflanadi: (1) "ketdi" ta'rifi bosh sahifa kartochkasi bilan BIR XIL — guruh almashtirish
/// va sertifikat bilan tugatish ketish EMAS; (2) sabablar guruhlanadi, "nazoratdan tashqari"
/// bayrog'i ActionReason'dan; (3) ketish o'sha paytdagi o'qituvchiga yoziladi va foiz uning AKTIV
/// o'quvchilariga nisbatan; (4) sabab tuzilgan yozuvdan, bo'lmasa audit matnidan, bo'lmasa arxivdan;
/// (5) endpoint ruxsat darvozasi (manba matnidan — test loyihasi Server'ga havola qilmaydi).</para>
/// </summary>
public class RetentionLossReportTests
{
    private static readonly RetentionLossReport.Query Sep =
        new("2026-09", "2026-09", null, null, RetentionLossReport.KindAll);

    private static StudentGroup M(
        string student, string group, string status, string joined,
        string activated = "", string? left = null, string frozen = "", List<string>? past = null) => new()
        {
            StudentId = student, GroupId = group, Status = status, JoinedAt = joined,
            ActivatedAt = activated, LeftAt = left, FrozenAt = frozen,
            IsActive = left is null, PastPeriods = past ?? [],
        };

    private static readonly List<RetentionLossReport.GroupRow> Groups =
    [
        new("gA", "Eng A", "eng", "t1"),
        new("gB", "Eng B", "eng", "t1"),
        new("gM", "Math", "math", "t2"),
    ];

    private static RetentionLossReport.Data Data(
        IEnumerable<StudentGroup> ms,
        IEnumerable<MembershipReasonLog>? logs = null,
        IEnumerable<ActionReason>? reasons = null,
        Dictionary<string, List<MembershipReasons.AuditRow>>? audits = null,
        Dictionary<string, List<GroupTeacherAssignment>>? history = null,
        IEnumerable<RetentionLossReport.StudentRow>? students = null)
    {
        var list = ms.ToList();
        return new RetentionLossReport.Data
        {
            Memberships = list,
            Groups = Groups,
            TeacherHistory = history ?? new Dictionary<string, List<GroupTeacherAssignment>>(),
            Students = (students ?? list.Select(m => m.StudentId).Distinct()
                    .Select(id => new RetentionLossReport.StudentRow(id, "O'quvchi " + id, false, null, null)))
                .ToList(),
            TeacherNames = new Dictionary<string, string> { ["t1"] = "Ali", ["t2"] = "Vali", ["t3"] = "Soli" },
            ActiveTeacherIds = ["t1", "t2", "t3"],
            CourseNames = new Dictionary<string, string> { ["eng"] = "Ingliz tili", ["math"] = "Matematika" },
            Reasons = reasons?.ToList() ?? [],
            ReasonLogs = logs?.ToList() ?? [],
            Audits = audits ?? new Dictionary<string, List<MembershipReasons.AuditRow>>(),
        };
    }

    private static MembershipReasonLog Log(string student, string group, string action, string date, string label,
        string? reasonId = null, string createdAt = "2026-09-30T10:00:00") => new()
        {
            StudentId = student, GroupId = group, Action = action, Date = date,
            ReasonId = reasonId, ReasonLabel = label, CreatedAt = createdAt,
        };

    // ============================ "Ketdi" ta'rifi ============================

    [Fact]
    public void Kurs_ichida_guruh_almashtirish_KETISH_EMAS()
    {
        var r = RetentionLossReport.Compute(Data(
        [
            M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-05"),
            M("s1", "gB", "active", "2026-09-05", activated: "2026-09-05"),
        ]), Sep);

        Assert.Equal(0, r.Totals.Left);
        Assert.Empty(r.Rows);
        Assert.Empty(r.Reasons);
    }

    [Fact]
    public void Sertifikat_bilan_tugatish_KETISH_EMAS()
    {
        var r = RetentionLossReport.Compute(Data(
        [
            M("s1", "gA", "completed", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
        ]), Sep);

        Assert.Equal(0, r.Totals.Left);
        Assert.Empty(r.Rows);
    }

    [Fact]
    public void Hisobot_bosh_sahifa_kartochkasi_bilan_BIR_XIL_sanaydi()
    {
        var ms = new[]
        {
            M("new1", "gA", "trial", "2026-09-01", left: "2026-09-10"),
            M("act1", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-12"),
            M("move", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-05"),
            M("move", "gB", "active", "2026-09-06", activated: "2026-09-06"),
            M("done", "gA", "completed", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
        };
        var r = RetentionLossReport.Compute(Data(ms), Sep);
        var (fromNew, fromActive) = DashboardSummary.LeftInMonth(
            ms, Groups.ToDictionary(g => g.Id, g => g.CourseId), "2026-09");

        Assert.Equal(fromActive, r.Totals.LeftActive);
        Assert.Equal(fromNew, r.Totals.LeftTrial);
        Assert.Equal(2, r.Totals.Left);
    }

    [Fact]
    public void Davrdan_tashqarida_ketgan_kirmaydi()
    {
        var r = RetentionLossReport.Compute(Data(
        [
            M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-08-31"),
            M("s2", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-10-01"),
        ]), Sep);
        Assert.Equal(0, r.Totals.Left);
    }

    // ============================ Sabablar ============================

    [Fact]
    public void Sabablar_guruhlanadi_va_sababsiz_alohida()
    {
        var r = RetentionLossReport.Compute(Data(
            [
                M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
                M("s2", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-12"),
                M("s3", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-15"),
            ],
            logs:
            [
                Log("s1", "gA", MembershipReasons.ActionRemove, "2026-09-10", "Moliyaviy"),
                Log("s2", "gA", MembershipReasons.ActionRemove, "2026-09-12", "moliyaviy"),
            ]), Sep);

        Assert.Equal(2, r.Reasons.Count);
        Assert.Equal(("Moliyaviy", 2), (r.Reasons[0].Label, r.Reasons[0].Count));
        Assert.Equal((MembershipReasons.NoReasonLabel, 1), (r.Reasons[1].Label, r.Reasons[1].Count));
        Assert.Equal(1, r.Totals.NoReason);
    }

    [Fact]
    public void Nazoratdan_tashqari_ActionReason_bayrogidan_id_va_nom_boyicha()
    {
        var moved = new ActionReason { Id = "r-moved", Category = "remove_active", Label = "Ko'chib ketdi", OutOfControl = true };
        var money = new ActionReason { Id = "r-money", Category = "remove_active", Label = "Moliyaviy" };
        var r = RetentionLossReport.Compute(Data(
            [
                M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
                M("s2", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-12"),
                M("s3", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-14"),
            ],
            logs:
            [
                Log("s1", "gA", MembershipReasons.ActionRemove, "2026-09-10", "Ko'chib ketdi", "r-moved"),
                Log("s2", "gA", MembershipReasons.ActionRemove, "2026-09-12", "Moliyaviy", "r-money"),
            ],
            reasons: [moved, money],
            // Eski tarix: id yo'q, faqat audit matnidagi NOM — nomi bo'yicha topiladi.
            audits: new()
            {
                ["gA:s3"] = [new("2026-09-14T11:00:00", "Guruhdan chiqarildi (aktiv, 2026-09-14, guruh: Eng A) — sabab: ko'chib ketdi")],
            }), Sep);

        Assert.Equal(2, r.Totals.OutOfControl);
        Assert.True(r.Rows.Single(x => x.StudentId == "s1").OutOfControl);
        Assert.False(r.Rows.Single(x => x.StudentId == "s2").OutOfControl);
        Assert.Equal("audit", r.Rows.Single(x => x.StudentId == "s3").ReasonSource);
    }

    // ============================ O'qituvchi kesimi ============================

    [Fact]
    public void Oqituvchi_kesimi_soni_va_aktivlariga_nisbatan_foiz()
    {
        var r = RetentionLossReport.Compute(Data(
        [
            // t1 (gA): 4 ta aktiv, 1 tasi ketdi → 25%
            M("a1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
            M("a2", "gA", "active", "2026-05-01", activated: "2026-05-03"),
            M("a3", "gA", "active", "2026-05-01", activated: "2026-05-03"),
            M("a4", "gA", "active", "2026-05-01", activated: "2026-05-03"),
            // t1: sinovdagi ketdi — foizga KIRMAYDI (maxraj aktivlar)
            M("n1", "gA", "trial", "2026-09-01", left: "2026-09-05"),
            // t2 (gM): 2 ta aktiv, 2 tasi ham ketdi → 100%
            M("b1", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
            M("b2", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-20"),
        ]), Sep);

        var t1 = r.Teachers.Single(t => t.TeacherId == "t1");
        Assert.Equal((2, 1, 1, 4), (t1.Left, t1.LeftActive, t1.LeftTrial, t1.ActiveBase));
        Assert.Equal(25.0, t1.LossRate);

        var t2 = r.Teachers.Single(t => t.TeacherId == "t2");
        Assert.Equal((2, 2, 0, 2), (t2.Left, t2.LeftActive, t2.LeftTrial, t2.ActiveBase));
        Assert.Equal(100.0, t2.LossRate);

        Assert.Equal(6, r.Totals.ActiveBase);
        Assert.Equal(50.0, r.Totals.LossRate);
        Assert.Equal(50.0, r.Totals.Retention);
    }

    [Fact]
    public void Ketish_OSHA_PAYTDAGI_oqituvchiga_yoziladi()
    {
        // gA iyulgacha t3, avgustdan t2. Sentyabrdagi ketish — t2 niki: guruh kartasidagi hozirgi
        // o'qituvchi (t1) boshqa bo'lsa ham o'qituvchi TARIXI ustun.
        var history = new Dictionary<string, List<GroupTeacherAssignment>>
        {
            ["gA"] =
            [
                new() { GroupId = "gA", TeacherId = "t3", FromDate = "2026-01-01", ToDate = "2026-07-31" },
                new() { GroupId = "gA", TeacherId = "t2", FromDate = "2026-08-01", ToDate = null },
            ],
        };
        var r = RetentionLossReport.Compute(Data(
            [M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10")],
            history: history), Sep);

        var row = Assert.Single(r.Rows);
        Assert.Equal(("t2", "Vali"), (row.TeacherId, row.TeacherName));
    }

    [Fact]
    public void Muzlatilgan_holda_chiqarilgan_maxrajga_qoshiladi_foiz_100_dan_oshmaydi()
    {
        // Muzlatilgan holda davrdan OLDIN to'xtagan, davrda chiqarilgan: davrda pullik emas, lekin
        // ketgani uchun maxrajga qo'shiladi — foiz 100 dan oshmaydi.
        var r = RetentionLossReport.Compute(Data(
        [
            M("s1", "gA", "frozen", "2026-03-01", activated: "2026-03-02", frozen: "2026-06-01", left: "2026-09-10"),
        ]), Sep);

        Assert.Equal(1, r.Totals.ActiveBase);
        Assert.Equal(100.0, r.Totals.LossRate);
    }

    // ============================ Filtrlar ============================

    [Fact]
    public void Tur_filtri_diagramma_va_jadvalga_tasir_qiladi_jami_sonlarga_emas()
    {
        var ms = new[]
        {
            M("a1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
            M("n1", "gA", "trial", "2026-09-01", left: "2026-09-05"),
        };
        var active = RetentionLossReport.Compute(Data(ms), Sep with { Kind = RetentionLossReport.KindActive });
        Assert.Equal("a1", Assert.Single(active.Rows).StudentId);
        Assert.Equal((1, 1, 1), (active.Totals.Left, active.Totals.LeftActive, active.Totals.LeftTrial));

        var trial = RetentionLossReport.Compute(Data(ms), Sep with { Kind = RetentionLossReport.KindTrial });
        Assert.Equal("n1", Assert.Single(trial.Rows).StudentId);
        Assert.False(trial.Rows[0].FromActive);
    }

    [Fact]
    public void Oqituvchi_va_kurs_filtri()
    {
        var ms = new[]
        {
            M("a1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
            M("b1", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
        };
        var byTeacher = RetentionLossReport.Compute(Data(ms), Sep with { TeacherId = "t2" });
        Assert.Equal("b1", Assert.Single(byTeacher.Rows).StudentId);
        Assert.Equal("t2", Assert.Single(byTeacher.Teachers).TeacherId);

        var byCourse = RetentionLossReport.Compute(Data(ms), Sep with { CourseId = "eng" });
        Assert.Equal("a1", Assert.Single(byCourse.Rows).StudentId);
    }

    [Fact]
    public void Boshqa_kursda_oqiyotgan_bolsa_StillStudying()
    {
        var r = RetentionLossReport.Compute(Data(
        [
            M("s1", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
            M("s1", "gA", "active", "2026-09-11", activated: "2026-09-11"),
            M("s2", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
        ]), Sep);

        Assert.True(r.Rows.Single(x => x.StudentId == "s1").StillStudying);
        Assert.False(r.Rows.Single(x => x.StudentId == "s2").StillStudying);
    }

    [Fact]
    public void Oylik_dinamika_har_oy_uchun_ikkala_tur()
    {
        var r = RetentionLossReport.Compute(Data(
            [
                M("a1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-08-10"),
                M("a2", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
                M("n1", "gA", "trial", "2026-09-01", left: "2026-09-05"),
            ]),
            new RetentionLossReport.Query("2026-08", "2026-09", null, null, RetentionLossReport.KindAll));

        Assert.Equal(new[] { "2026-08", "2026-09" }, r.Months.Select(m => m.Month));
        Assert.Equal((1, 0), (r.Months[0].LeftActive, r.Months[0].LeftTrial));
        Assert.Equal((1, 1), (r.Months[1].LeftActive, r.Months[1].LeftTrial));
    }

    // ============================ So'rov ============================

    [Fact]
    public void Sorov_standart_6_oy_va_xato_format()
    {
        var today = new DateOnly(2026, 10, 6);
        var (q, err) = RetentionLossReport.Normalize(null, null, null, null, null, today);
        Assert.Null(err);
        Assert.Equal(("2026-05", "2026-10", RetentionLossReport.KindAll), (q.From, q.To, q.Kind));

        var (_, bad) = RetentionLossReport.Normalize("2026-13", "2026-10", null, null, null, today);
        Assert.NotNull(bad);

        // Teskari tartib almashtiriladi; 24 oydan uzun davr boshidan qirqiladi.
        var (swapped, _) = RetentionLossReport.Normalize("2026-10", "2026-08", null, null, "ACTIVE", today);
        Assert.Equal(("2026-08", "2026-10", RetentionLossReport.KindActive), (swapped.From, swapped.To, swapped.Kind));
        var (clamped, _) = RetentionLossReport.Normalize("2020-01", "2026-10", null, null, null, today);
        Assert.Equal("2024-11", clamped.From);
    }

    // ============================ Sabab manbalari (MembershipReasons.Resolve) ============================

    private static readonly MembershipReasons.ArchiveInfo NotArchived = new(false, null, null);

    [Fact]
    public void Resolve_chiqish_yozuvi_AYNAN_shu_sanadagi()
    {
        var r = MembershipReasons.Resolve("2026-09-10", "",
            [
                Log("s1", "gA", MembershipReasons.ActionRemove, "2026-03-01", "Eski sabab"),
                Log("s1", "gA", MembershipReasons.ActionRemove, "2026-09-10", "Moliyaviy", "r1"),
            ], [], NotArchived);
        Assert.Equal(("r1", "Moliyaviy", "log"), (r.ReasonId, r.Label, r.Source));
    }

    [Fact]
    public void Resolve_muzlatilib_keyin_SABABSIZ_chiqarilgan_muzlatish_sababini_oladi()
    {
        var r = MembershipReasons.Resolve("2026-09-10", "2026-07-01",
            [Log("s1", "gA", MembershipReasons.ActionFreeze, "2026-07-01", "Sog'lig'i")], [], NotArchived);
        Assert.Equal("Sog'lig'i", r.Label);

        // Muzlatish CHIQISHDAN keyin bo'lsa (buzuq qator) — hisobga olinmaydi.
        var none = MembershipReasons.Resolve("2026-09-10", "2026-09-20",
            [Log("s1", "gA", MembershipReasons.ActionFreeze, "2026-09-20", "Sog'lig'i")], [], NotArchived);
        Assert.Equal("", none.Label);
    }

    [Fact]
    public void Resolve_eski_davrning_sababi_yangi_SABABSIZ_chiqishga_yopishmaydi()
    {
        var audits = new List<MembershipReasons.AuditRow>
        {
            new("2026-02-01T10:00:00", "Guruhdan chiqarildi (aktiv, 2026-02-01, guruh: A) — sabab: Eski sabab"),
            new("2026-09-10T10:00:00", "Guruhdan chiqarildi (aktiv, 2026-09-10, guruh: A)"),
        };
        var r = MembershipReasons.Resolve("2026-09-10", "", [], audits, NotArchived);
        Assert.Equal("", r.Label);
    }

    [Fact]
    public void Resolve_audit_matnidan_va_arxiv_sababidan()
    {
        var fromAudit = MembershipReasons.Resolve("2026-09-10", "",
            [], [new("2026-09-11T09:00:00", "Guruh almashtirildi: A → B (eski guruhdan chiqdi 2026-09-10, aktivlashtirish 2026-09-10) — sabab: Jadval mos emas")],
            NotArchived);
        Assert.Equal(("Jadval mos emas", "audit"), (fromAudit.Label, fromAudit.Source));

        var fromArchive = MembershipReasons.Resolve("2026-09-10", "", [], [],
            new MembershipReasons.ArchiveInfo(true, "2026-09-12", "Boshqa markazga o'tdi"));
        Assert.Equal(("Boshqa markazga o'tdi", "archive"), (fromArchive.Label, fromArchive.Source));

        // Chiqishdan OLDIN arxivlangan (keyin qaytib kelgan) — bu ketishning sababi emas.
        var stale = MembershipReasons.Resolve("2026-09-10", "", [], [],
            new MembershipReasons.ArchiveInfo(true, "2026-01-12", "Eski"));
        Assert.Equal("", stale.Label);
    }

    [Fact]
    public void Record_sababsiz_amalni_YOZMAYDI()
    {
        using var t = TestDb.Sqlite();
        MembershipReasons.Record(t.Context, "s1", "g1", MembershipReasons.ActionRemove, "2026-09-10", null, "");
        MembershipReasons.Record(t.Context, "s1", "g1", MembershipReasons.ActionRemove, "2026-09-10", "r1", "  Moliyaviy ");
        t.Context.SaveChanges();

        var row = Assert.Single(t.Context.MembershipReasonLogs.ToList());
        Assert.Equal(("r1", "Moliyaviy", "2026-09-10"), (row.ReasonId, row.ReasonLabel, row.Date));
    }

    // ============================ Bazadan yuklash (SQLite) ============================

    [Fact]
    public async Task BuildAsync_bazadan_sabab_va_oqituvchi_bilan()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Teachers.Add(new Teacher { Id = "t1", FullName = "Ali" });
        db.Subjects.Add(new Subject { Id = "eng", Name = "Ingliz tili" });
        db.Classes.Add(new Group { Id = "gA", Name = "Eng A", CourseId = "eng", TeacherId = "t1" });
        db.Students.Add(new Student { Id = "s1", FullName = "Aziz" });
        db.Students.Add(new Student { Id = "s2", FullName = "Bobur" });
        db.StudentGroups.Add(M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"));
        db.StudentGroups.Add(M("s2", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-12"));
        db.ActionReasons.Add(new ActionReason { Id = "r1", Category = "remove_active", Label = "Ko'chib ketdi", OutOfControl = true });
        MembershipReasons.Record(db, "s1", "gA", MembershipReasons.ActionRemove, "2026-09-10", "r1", "Ko'chib ketdi");
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Membership", EntityId = "gA:s2", Action = "delete", Timestamp = "2026-09-12T12:00:00",
            Summary = "Guruhdan chiqarildi (aktiv, 2026-09-12, guruh: Eng A) — sabab: Moliyaviy",
        });
        await db.SaveChangesAsync();

        var r = await RetentionLossReport.BuildAsync(db, Sep);

        Assert.Equal(2, r.Totals.Left);
        Assert.Equal(1, r.Totals.OutOfControl);
        var s1 = r.Rows.Single(x => x.StudentId == "s1");
        Assert.Equal(("Ko'chib ketdi", "log", "Ali", "Ingliz tili"), (s1.Reason, s1.ReasonSource, s1.TeacherName, s1.CourseName));
        Assert.Equal(("Moliyaviy", "audit"), (r.Rows.Single(x => x.StudentId == "s2").Reason, r.Rows.Single(x => x.StudentId == "s2").ReasonSource));
        Assert.Contains(r.TeacherOptions, o => o.Id == "t1");
    }

    // ============================ RUXSAT ============================

    private static string ControllerSource(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        return File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", file));
    }

    [Fact]
    public void Endpoint_teachers_retention_ruxsati_bilan_OQISH_ham_darvozalangan()
    {
        var src = ControllerSource("RetentionLossController.cs");
        var header = src[..src.IndexOf("public class RetentionLossController", StringComparison.Ordinal)];

        // Javobda o'quvchi ismlari va sabablar — GET ham ruxsat talab qiladi (ReadRequiresPerm).
        Assert.Matches(new Regex(@"\[AdminPerm\(""teachers\.retention"",\s*ReadRequiresPerm\s*=\s*true\)\]"), header);
        Assert.Contains("[Authorize]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
        // Faqat o'qish: yozuvchi amal yo'q, metod darajasida sinf darvozasini bekor qiluvchi atribut yo'q.
        Assert.DoesNotMatch(new Regex(@"\[Http(Post|Put|Patch|Delete)"), src);
        Assert.Single(Regex.Matches(src, @"\[AdminPerm\("));
    }

    [Fact]
    public void Ruxsat_meros_qoidasi_teachers_retention_uchun()
    {
        // Bo'lim kaliti sahifani OCHADI (pastga meros), qo'shni sahifa kaliti OCHMAYDI.
        Assert.True(PermissionRules.HasSection(["teachers"], "teachers.retention"));
        Assert.True(PermissionRules.HasSection(["teachers.retention"], "teachers.retention"));
        Assert.False(PermissionRules.HasSection(["teachers.list"], "teachers.retention"));
        Assert.False(PermissionRules.HasSection(["students"], "teachers.retention"));
    }
}
