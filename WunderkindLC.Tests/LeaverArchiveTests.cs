using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Mijoz shikoyati (2026-10-23): "guruhdan chiqarilganlar arxivga tushmayapti (58 ta chiqardim — arxiv 0)" va
/// "«Aktiv o'quvchidan ketganlar» ichida yozgan izohlarimiz, sabablari ko'rinmayapti".
/// <see cref="LeaveNotes.PickNote"/> · <see cref="LeaveDetails"/> · <see cref="LeaverArchive"/> ·
/// <see cref="StudentArchiving.LeaveAllGroupsAsync"/> (chiqarish + arxivlash bitta so'rovda).
/// </summary>
public class LeaverArchiveTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static AuditService Audit(AppDbContext ctx) => new(ctx, new NoHttpContext());

    private static StudentGroup Membership(string studentId, string groupId, string status, bool isActive,
        string? leftAt = null, string recordedAt = "2026-09-01") => new()
    {
        StudentId = studentId, GroupId = groupId, Status = status, IsActive = isActive,
        JoinedAt = "2026-09-01", RecordedAt = recordedAt, LeftAt = leftAt,
    };

    /* ============================ izoh ============================ */

    [Fact]
    public void PickNote__shu_guruh_va_sana_izohi_prefiksiz__eng_kechkisi()
    {
        var notes = new[]
        {
            new LeaveNotes.NoteRow(LeaveNotes.GroupLeaveText("A guruh", "2026-10-15", "Eski izoh"), "2026-10-15T10:00:00"),
            new LeaveNotes.NoteRow(LeaveNotes.GroupLeaveText("A guruh", "2026-10-15", "Narx qimmat, ota-onasi bilan gaplashildi"), "2026-10-15T11:00:00"),
            new LeaveNotes.NoteRow(LeaveNotes.GroupLeaveText("B guruh", "2026-10-15", "Boshqa guruh"), "2026-10-15T12:00:00"),
            new LeaveNotes.NoteRow(LeaveNotes.GroupLeaveText("A guruh", "2026-09-01", "Boshqa chiqish"), "2026-09-01T12:00:00"),
            new LeaveNotes.NoteRow("Oddiy izoh", "2026-10-16T09:00:00"),
        };
        Assert.Equal("Narx qimmat, ota-onasi bilan gaplashildi", LeaveNotes.PickNote(notes, "A guruh", "2026-10-15", null));
        Assert.Equal("", LeaveNotes.PickNote(notes, "C guruh", "2026-10-15", null));
    }

    [Fact]
    public void PickNote__chiqish_kuni_arxivlangan_bolsa__arxiv_izohi_zaxira()
    {
        var notes = new[]
        {
            new LeaveNotes.NoteRow(LeaveNotes.ArchiveText("Toshkentga ko'chib ketdi"), "2026-10-20T14:00:00"),
        };
        Assert.Equal("Toshkentga ko'chib ketdi", LeaveNotes.PickNote(notes, "A guruh", "2026-10-20", "2026-10-20"));
        // Boshqa kuni arxivlangan — bu chiqishning izohi emas.
        Assert.Equal("", LeaveNotes.PickNote(notes, "A guruh", "2026-10-18", "2026-10-20"));
        Assert.Equal("", LeaveNotes.PickNote(notes, "A guruh", "2026-10-20", null));
    }

    [Fact]
    public void Prefiks_matni_ozgarmadi()
    {
        Assert.Equal("Guruhdan chiqarildi («A guruh», 15.10.2026): x", LeaveNotes.GroupLeaveText("A guruh", "2026-10-15", "x"));
        Assert.Equal("Arxivga ko'chirildi: x", LeaveNotes.ArchiveText("x"));
    }

    /* ============================ nomzod ============================ */

    [Fact]
    public void LastExit__guruhsiz_qolgan_chiqarilgan_oquvchi_nomzod()
    {
        var last = LeaverArchive.LastExit(false, new[]
        {
            Membership("s", "g1", "active", false, "2026-09-10"),
            Membership("s", "g2", "trial", false, "2026-10-05"),
        });
        Assert.NotNull(last);
        Assert.Equal("g2", last!.GroupId);
    }

    [Fact]
    public void LastExit__tirik_azoligi_bor__arxivda__sertifikat_bilan_tugatgan__azoligi_yoq__NOMZOD_EMAS()
    {
        // Boshqa guruhda hali o'qiyapti (muzlatilgan ham tirik).
        Assert.Null(LeaverArchive.LastExit(false, new[]
        {
            Membership("s", "g1", "active", false, "2026-09-10"),
            Membership("s", "g2", "frozen", true),
        }));
        Assert.Null(LeaverArchive.LastExit(true, new[] { Membership("s", "g1", "active", false, "2026-09-10") }));
        // Oxirgisi sertifikat bilan tugatilgan — ketgan emas (keyingi darajaga yozilishi mumkin).
        Assert.Null(LeaverArchive.LastExit(false, new[]
        {
            Membership("s", "g1", "active", false, "2026-09-10"),
            Membership("s", "g2", "completed", false, "2026-10-01"),
        }));
        Assert.Null(LeaverArchive.LastExit(false, Array.Empty<StudentGroup>()));
    }

    /* ============================ sabab + izoh (bazadan) ============================ */

    [Fact]
    public async Task LeaveDetails__chiqarish_sababi_va_izohi_topiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "A guruh", MonthlyFee = 100_000m, Days = new List<int> { 0 } };
        var s = new Student { FullName = "Ketgan O'quvchi" };
        ctx.Classes.Add(g);
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(Membership(s.Id, g.Id, "active", false, "2026-10-15"));
        MembershipReasons.Record(ctx, s.Id, g.Id, MembershipReasons.ActionRemove, "2026-10-15", null, "Narx qimmat");
        LeaveNotes.Add(ctx, s.Id, LeaveNotes.GroupLeaveText(g.Name, "2026-10-15", "Ota-onasi bilan gaplashildi"), "u1", "Operator");
        await ctx.SaveChangesAsync();

        var exit = new LeaveDetails.Exit(s.Id, g.Id, "2026-10-15");
        var info = (await LeaveDetails.LoadAsync(ctx, new[] { exit }))[exit];
        Assert.Equal("Narx qimmat", info.Reason);
        Assert.Equal("Ota-onasi bilan gaplashildi", info.Note);

        var rows = await LeaverArchive.LoadAsync(ctx);
        var row = Assert.Single(rows).Row;
        Assert.Equal("A guruh", row.GroupName);
        Assert.Equal("Narx qimmat", row.Reason);
        Assert.Equal("Ota-onasi bilan gaplashildi", row.Note);
    }

    /* ============================ chiqarish + arxivlash bitta so'rovda ============================ */

    /// <summary>
    /// ⚠️ RemoveMember «arxivga ham o'tkazish» bilan: a'zolik XOTIRADA yopilgan (hali saqlanmagan), keyin
    /// arxivlash <see cref="StudentArchiving.LeaveAllGroupsAsync"/> ni chaqiradi. SQL uni hali tirik deb
    /// qaytaradi — ikkinchi marta "chiqarilmasligi" (hisob, sabab yozuvi va audit takrorlanmasligi) SHART.
    /// </summary>
    [Fact]
    public async Task LeaveAllGroups__shu_sorovda_yopilgan_azolikni_QAYTA_chiqarmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "A guruh", MonthlyFee = 100_000m, Days = new List<int> { 0 } };
        var s = new Student { FullName = "Ketgan O'quvchi", ClassName = "A guruh" };
        ctx.Classes.Add(g);
        ctx.Students.Add(s);
        var m = Membership(s.Id, g.Id, "trial", true);
        ctx.StudentGroups.Add(m);
        await ctx.SaveChangesAsync();

        // RemoveMember (sinov) — xotirada yopiladi.
        m.IsActive = false;
        m.LeftAt = "2026-10-15";

        var res = await StudentArchiving.LeaveAllGroupsAsync(ctx, Audit(ctx), s, "2026-10-23", null, "Ko'chib ketdi", "o'quvchi arxivlandi");
        await ctx.SaveChangesAsync();

        Assert.Equal(0, res.Closed);
        var saved = await ctx.StudentGroups.AsNoTracking().SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Equal("2026-10-15", saved.LeftAt);   // chiqish sanasi arxiv sanasi bilan USTIDAN yozilmadi
        Assert.Equal(0, await ctx.MembershipReasonLogs.CountAsync());
        Assert.Equal(0, await ctx.AuditLogs.CountAsync(a => a.EntityType == "Membership"));
    }

    /* ============================ ruxsat (manba) ============================ */

    [Fact]
    public void RemoveMember__arxiv_ruxsati_HECH_NARSA_ozgarmasdan_OLDIN_tekshiriladi()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", "ClassesController.cs"));
        var i = src.IndexOf("public async Task<IActionResult> RemoveMember(", StringComparison.Ordinal);
        Assert.True(i >= 0);
        var gate = src.IndexOf("archiveIfLast && !MayArchiveStudents", i, StringComparison.Ordinal);
        var firstQuery = src.IndexOf("await db.StudentGroups", i, StringComparison.Ordinal);
        Assert.True(gate > i && gate < firstQuery, "Arxiv ruxsati a'zolik o'zgarishidan OLDIN tekshirilishi kerak");
        // «Arxivga ko'chirish» bilan bir xil yadro — nusxa yo'q.
        Assert.Contains("StudentArchiveFlow.ArchiveAsync(", src[i..]);
        var students = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", "StudentsController.cs"));
        Assert.Equal(2, CountOf(students, "StudentArchiveFlow.ArchiveAsync("));
    }

    private static int CountOf(string s, string needle)
    {
        var n = 0;
        for (var i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

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
}
