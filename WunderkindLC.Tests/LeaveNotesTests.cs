using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'quvchi KETGANDA yoziladigan izoh — <see cref="LeaveNotes"/> («Guruhdan chiqarish» va «Arxivga
/// ko'chirish» oynalaridagi «Izoh» maydoni). Izoh profil izohlariga tushadi, audit matniga EMAS.
/// </summary>
public class LeaveNotesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void Clean__bosh_izoh_YOZILMAYDI_va_xato_emas(string? raw)
    {
        var (text, error) = LeaveNotes.Clean(raw);
        Assert.Null(text);
        Assert.Null(error);
    }

    [Fact]
    public void Clean__chetdagi_bosh_joy_olinadi()
    {
        var (text, error) = LeaveNotes.Clean("  Ota-onasi boshqa markazga o'tkazdi \n");
        Assert.Equal("Ota-onasi boshqa markazga o'tkazdi", text);
        Assert.Null(error);
    }

    [Fact]
    public void Clean__500_belgi_otadi__501_jimgina_QIRQILMAYDI_xato_qaytadi()
    {
        Assert.Equal(500, LeaveNotes.Clean(new string('a', 500)).Text!.Length);

        var (text, error) = LeaveNotes.Clean(new string('a', 501));
        Assert.Null(text);
        Assert.Equal(LeaveNotes.TooLongMessage, error);
    }

    [Fact]
    public void Matnlar__guruh_nomi_va_sana_kun_oy_yil_korinishida()
    {
        Assert.Equal("Guruhdan chiqarildi («A guruh», 15.10.2026): Narx qimmat dedi",
            LeaveNotes.GroupLeaveText("A guruh", "2026-10-15", "Narx qimmat dedi"));
        // Buzuq sana — o'zgarishsiz (matn baribir yoziladi).
        Assert.Equal("Guruhdan chiqarildi («A», ?): x", LeaveNotes.GroupLeaveText("A", "?", "x"));
        Assert.Equal("Arxivga ko'chirildi: Ko'chib ketdi", LeaveNotes.ArchiveText("Ko'chib ketdi"));
    }

    [Fact]
    public async Task Add__izoh_muallif_va_vaqt_bilan_amal_tranzaksiyasida_saqlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = new Student { FullName = "Test O'quvchi" };
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();

        LeaveNotes.Add(ctx, s.Id, "Guruhdan chiqarildi («A», 15.10.2026): izoh", "u1", "Operator");
        // SaveChanges QILMAYDI — chaqiruvchi amal bilan birga saqlaydi.
        Assert.Equal(0, await ctx.StudentNotes.AsNoTracking().CountAsync());

        await ctx.SaveChangesAsync();
        var note = await ctx.StudentNotes.AsNoTracking().SingleAsync();
        Assert.Equal(s.Id, note.StudentId);
        Assert.Equal("u1", note.AuthorId);
        Assert.Equal("Operator", note.AuthorName);
        Assert.Equal(19, note.CreatedAt.Length);
        Assert.Null(note.EditedAt);
    }

    /// <summary>
    /// ⚠️ Izoh <c>Membership</c> audit matniga TUSHMASLIGI shart: KPI, AI tahlil va «Ketish sabablari»
    /// sababni o'sha matnning OXIRIDAN («— sabab: X») o'qiydi — izoh sababga yopishib qolardi.
    /// </summary>
    [Fact]
    public void Chiqarish_va_arxivlash__izoh_audit_matniga_emas_profil_izohlariga_yoziladi()
    {
        var classes = Controller("ClassesController.cs");
        var remove = Body(classes, "public async Task<IActionResult> RemoveMember(", "private async Task ReleasePrimaryGroupNameAsync");
        Assert.Contains("LeaveNotes.Clean(note)", remove);
        Assert.Contains("LeaveNotes.GroupLeaveText(", remove);
        var auditCall = Body(remove, "audit.Record(\"Membership\"", "studentId: studentId);");
        Assert.DoesNotContain("note", auditCall, StringComparison.OrdinalIgnoreCase);

        var students = Controller("StudentsController.cs");
        var archive = Body(students, "public async Task<IActionResult> Archive(string id, ArchiveStudentRequest req)", "await db.SaveChangesAsync();");
        Assert.Contains("LeaveNotes.Clean(req.Note)", archive);
        Assert.Contains("LeaveNotes.ArchiveText(", archive);
    }

    private static string Body(string src, string start, string end)
    {
        var i = src.IndexOf(start, StringComparison.Ordinal);
        Assert.True(i >= 0, $"Topilmadi: {start}");
        var j = src.IndexOf(end, i, StringComparison.Ordinal);
        Assert.True(j > i, $"Topilmadi: {end}");
        return src[i..j];
    }

    private static string Controller(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file));

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
