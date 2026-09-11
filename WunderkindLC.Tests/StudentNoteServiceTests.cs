using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// "IZOHLARGA JAVOBLAR" ro'yxati (<see cref="StudentNoteService.OverviewAsync"/>) — o'quvchi
/// profillariga yozilgan izohlar bir joyda: kimga, nechta, oxirgisi qachon va nima deb yozilgan.
/// </summary>
public class StudentNoteServiceTests
{
    private static Student AddStudent(TestDb db, string name, bool archived = false)
    {
        var s = new Student { FullName = name, IsArchived = archived };
        db.Context.Students.Add(s);
        db.Context.SaveChanges();
        return s;
    }

    private static void AddNote(TestDb db, Student s, string text, string createdAt, string author = "Operator")
    {
        db.Context.StudentNotes.Add(new StudentNote
        {
            StudentId = s.Id,
            Text = text,
            AuthorName = author,
            AuthorId = author,
            CreatedAt = createdAt,
        });
        db.Context.SaveChanges();
    }

    [Fact]
    public async Task Overview_OQUVCHI_boyicha_yigiladi_va_OXIRGI_izoh_tepada()
    {
        using var db = TestDb.Sqlite();
        var ali = AddStudent(db, "Ali Valiyev");
        var vali = AddStudent(db, "Vali Aliyev");
        AddNote(db, ali, "Onasi bilan gaplashildi", "2026-08-01T10:00:00", "Dilnoza");
        AddNote(db, ali, "To'lovni 15-kuni qiladi", "2026-08-05T12:00:00", "Sardor");
        AddNote(db, vali, "Kasal bo'lib qoldi", "2026-08-09T09:00:00", "Dilnoza");

        var rows = await StudentNoteService.OverviewAsync(db.Context);

        Assert.Equal(2, rows.Count);
        // Eng yangi izoh yozilgan o'quvchi tepada.
        Assert.Equal("Vali Aliyev", rows[0].FullName);

        var aliRow = rows[1];
        Assert.Equal(2, aliRow.NoteCount);
        Assert.Equal("2026-08-01T10:00:00", aliRow.FirstNoteAt);
        Assert.Equal("2026-08-05T12:00:00", aliRow.LastNoteAt);
        Assert.Equal("To'lovni 15-kuni qiladi", aliRow.LastNoteText);
        Assert.Equal("Sardor", aliRow.LastAuthorName);
        Assert.Equal(new[] { "Dilnoza", "Sardor" }, aliRow.Authors);
    }

    [Fact]
    public async Task Overview_QIDIRUV_ism_boyicha_ham_MATN_boyicha_ham()
    {
        using var db = TestDb.Sqlite();
        var ali = AddStudent(db, "Ali Valiyev");
        var vali = AddStudent(db, "Vali Aliyev");
        AddNote(db, ali, "Onasi bilan gaplashildi", "2026-08-01T10:00:00");
        AddNote(db, vali, "To'lov kechikdi", "2026-08-02T10:00:00");

        var byName = await StudentNoteService.OverviewAsync(db.Context, q: "vali valiyev".Split(' ')[0]);
        Assert.Equal(2, byName.Count);   // "vali" ikkala ismda ham bor

        var byText = await StudentNoteService.OverviewAsync(db.Context, q: "kechikdi");
        var only = Assert.Single(byText);
        Assert.Equal("Vali Aliyev", only.FullName);
    }

    [Fact]
    public async Task Overview_DAVR_filtri_SONNI_ham_OXIRGI_matnni_ham_cheklaydi()
    {
        using var db = TestDb.Sqlite();
        var ali = AddStudent(db, "Ali Valiyev");
        AddNote(db, ali, "Eski izoh", "2026-07-01T10:00:00");
        AddNote(db, ali, "Iyuldagi ikkinchi", "2026-07-20T10:00:00");
        AddNote(db, ali, "Avgustdagi", "2026-08-10T10:00:00");

        var july = await StudentNoteService.OverviewAsync(db.Context, from: "2026-07-01", to: "2026-07-31");

        var row = Assert.Single(july);
        Assert.Equal(2, row.NoteCount);
        // ⚠️ "Oxirgi izoh" — DAVR ichidagisi: son bilan matn bir-biriga mos bo'lsin.
        Assert.Equal("Iyuldagi ikkinchi", row.LastNoteText);
    }

    [Fact]
    public async Task Overview_TO_filtri_OSHA_KUNNI_ham_oz_ichiga_oladi()
    {
        using var db = TestDb.Sqlite();
        var ali = AddStudent(db, "Ali Valiyev");
        AddNote(db, ali, "Kun ichida yozilgan", "2026-08-10T15:30:00");

        // Yalang "2026-08-10" bilan solishtirilsa bu izoh TUSHIB QOLARDI (T15:30 > "2026-08-10").
        var rows = await StudentNoteService.OverviewAsync(db.Context, from: "2026-08-10", to: "2026-08-10");

        Assert.Single(rows);
    }

    [Fact]
    public async Task Overview_GURUHLAR_faol_azoliklardan_MUZLATILGANSIZ()
    {
        using var db = TestDb.Sqlite();
        var ali = AddStudent(db, "Ali Valiyev");
        var faol = new Group { Name = "Ingliz A1" };
        var muzlatilgan = new Group { Name = "Matematika B2" };
        db.Context.Classes.AddRange(faol, muzlatilgan);
        db.Context.SaveChanges();
        db.Context.StudentGroups.AddRange(
            new StudentGroup { StudentId = ali.Id, GroupId = faol.Id, IsActive = true, Status = "active" },
            new StudentGroup { StudentId = ali.Id, GroupId = muzlatilgan.Id, IsActive = true, Status = "frozen" });
        db.Context.SaveChanges();
        AddNote(db, ali, "Izoh", "2026-08-10T10:00:00");

        var row = Assert.Single(await StudentNoteService.OverviewAsync(db.Context));

        Assert.Equal(new[] { "Ingliz A1" }, row.Groups);
    }

    [Fact]
    public async Task Days_OY_boyicha_KUNLIK_sanoq()
    {
        using var db = TestDb.Sqlite();
        var ali = AddStudent(db, "Ali Valiyev");
        var vali = AddStudent(db, "Vali Aliyev");
        AddNote(db, ali, "1", "2026-08-10T09:00:00");
        AddNote(db, vali, "2", "2026-08-10T18:45:00");   // o'sha kun, kechqurun
        AddNote(db, ali, "3", "2026-08-21T10:00:00");
        AddNote(db, ali, "boshqa oy", "2026-07-31T23:00:00");

        var days = await StudentNoteService.DaysAsync(db.Context, "2026-08");

        Assert.Equal(2, days.Count);                       // faqat shu oyning ish BOR kunlari
        Assert.Equal("2026-08-10", days[0].Date);          // sana bo'yicha o'sish tartibida
        Assert.Equal(2, days[0].Count);                    // kun oxirigacha hisobga olinadi
        Assert.Equal("2026-08-21", days[1].Date);
        Assert.Equal(1, days[1].Count);
    }

    [Fact]
    public async Task Days_NOTOGRI_oy_berilsa_JORIY_oy_olinadi()
    {
        using var db = TestDb.Sqlite();
        var ali = AddStudent(db, "Ali Valiyev");
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        AddNote(db, ali, "bugungi", $"{today}T09:00:00");

        var days = await StudentNoteService.DaysAsync(db.Context, "buzuq");

        var d = Assert.Single(days);
        Assert.Equal(today, d.Date);
    }

    [Fact]
    public async Task Overview_ARXIVLANGAN_oquvchi_ham_korinadi_lekin_BELGILANADI()
    {
        using var db = TestDb.Sqlite();
        var arxiv = AddStudent(db, "Ketgan Bola", archived: true);
        AddNote(db, arxiv, "Ketishdan oldin aytgani", "2026-08-10T10:00:00");

        var row = Assert.Single(await StudentNoteService.OverviewAsync(db.Context));

        Assert.True(row.IsArchived);
        Assert.Equal("Ketgan Bola", row.FullName);
    }
}
