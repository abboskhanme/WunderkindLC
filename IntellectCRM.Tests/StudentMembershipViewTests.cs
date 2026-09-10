using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// O'QUVCHI HOLATI — JAMLAMA (bir nechta guruh a'zoligi ustidan), NUSXA EMAS.
///
/// <para><b>Muammo (2026-09-09, markaz egasi bildirgan):</b> o'quvchi A guruhida MUZLATILGAN,
/// B guruhida esa o'qiyapti. O'quvchilar bo'limida u "Aktiv emas" deb va BIRINCHI (muzlatilgan)
/// guruhi bilan ko'rinardi; A dagi a'zolik o'chirilishi bilan yana "Aktiv" bo'lardi. Sabab:
/// jamlama faqat RO'YXAT endpointida hisoblanardi — bitta o'quvchi qaytaradigan yo'llar
/// (<c>GET students/{id}</c>, arxiv, yangi yaratilgan) uni to'ldirmasdi va klient zaxira
/// sifatida <c>Student.ClassName</c> ni (birinchi qo'shilgan guruh nomi) chizardi.</para>
///
/// <para>INVARIANT: holat — BARCHA tirik a'zoliklar ustidan
/// <c>active &gt; trial &gt; yearFrozen &gt; frozen &gt; ""</c>; "qaysi guruh" savoliga ham
/// a'zoliklar javob beradi (<c>ClassName</c> faqat a'zolik UMUMAN bo'lmaganda).</para>
/// </summary>
public class StudentMembershipViewTests
{
    // ==================== yordamchilar ====================

    private static Student AddStudent(AppDbContext ctx, string className = "")
    {
        var s = new Student { FullName = "Test O'quvchi", ClassName = className };
        ctx.Students.Add(s);
        return s;
    }

    private static Group AddGroup(AppDbContext ctx, string name, string teacherId = "")
    {
        var g = new Group { Name = name, TeacherId = teacherId };
        ctx.Classes.Add(g);
        return g;
    }

    private static StudentGroup AddMembership(
        AppDbContext ctx, Student s, Group g, string status,
        bool isActive = true, bool yearFreeze = false, string joinedAt = "2026-01-01")
    {
        var m = new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = status,
            IsActive = isActive, YearFreeze = yearFreeze, JoinedAt = joinedAt,
        };
        ctx.StudentGroups.Add(m);
        return m;
    }

    /// <summary>Muammoning AYNAN o'zi: A — muzlatilgan, B — aktiv (ikkalasi ham tirik a'zolik).</summary>
    private static (Student S, Group A, Group B) FrozenInAActiveInB(AppDbContext ctx)
    {
        // ClassName — BIRINCHI qo'shilgan guruh nomi (A), keyin YANGILANMAYDI: aynan shu maydon
        // eski kodda "asosiy guruh" deb ko'rsatilardi.
        var a = AddGroup(ctx, "A guruh", "t-a");
        var b = AddGroup(ctx, "B guruh", "t-b");
        var s = AddStudent(ctx, className: a.Name);
        AddMembership(ctx, s, a, "frozen", joinedAt: "2026-01-01");
        AddMembership(ctx, s, b, "active", joinedAt: "2026-05-01");
        ctx.SaveChanges();
        return (s, a, b);
    }

    // ==================== sof qoida (MembershipLifecycle) ====================

    [Fact]
    public void MemberState_MUZLATILGAN_va_AKTIV_bolsa_AKTIV_chiqadi()
    {
        using var db = TestDb.Sqlite();
        var (s, _, _) = FrozenInAActiveInB(db.Context);
        var memberships = db.Context.StudentGroups.Where(m => m.StudentId == s.Id).ToList();

        Assert.Equal("active", MembershipLifecycle.MemberState(memberships));
        Assert.True(MembershipLifecycle.IsActiveOverall(memberships));
    }

    [Theory]
    // ustunlik: active > trial > yearFrozen > frozen > ""
    [InlineData(new[] { "frozen", "trial" }, "trial")]
    [InlineData(new[] { "frozen", "active", "trial" }, "active")]
    [InlineData(new[] { "frozen", "frozen" }, "frozen")]
    [InlineData(new string[0], "")]
    public void MemberState_USTUNLIK_tartibi(string[] statuses, string expected)
    {
        var rows = statuses.Select(st => new StudentGroup { Status = st, IsActive = true }).ToList();
        Assert.Equal(expected, MembershipLifecycle.MemberState(rows));
    }

    [Fact]
    public void MemberState_TIRIK_bolmagan_azolik_HISOBGA_KIRMAYDI()
    {
        // Guruhdan chiqarilgan (IsActive=false) aktiv a'zolik o'quvchini "aktiv" qilib qo'ymaydi.
        var rows = new List<StudentGroup>
        {
            new() { Status = "active", IsActive = false },
            new() { Status = "frozen", IsActive = true },
        };
        Assert.Equal("frozen", MembershipLifecycle.MemberState(rows));
    }

    [Fact]
    public void MemberState_AKTIV_MUZLATISH_oddiy_muzlatishdan_OLDIN()
    {
        var rows = new List<StudentGroup>
        {
            new() { Status = "frozen", IsActive = true },
            new() { Status = "frozen", IsActive = true, YearFreeze = true },
        };
        Assert.Equal("yearFrozen", MembershipLifecycle.MemberState(rows));
    }

    [Fact]
    public void PrimaryMembership_MUZLATILGANNI_emas_AKTIVNI_tanlaydi()
    {
        using var db = TestDb.Sqlite();
        var (s, _, b) = FrozenInAActiveInB(db.Context);
        var memberships = db.Context.StudentGroups.Where(m => m.StudentId == s.Id).ToList();

        var primary = MembershipLifecycle.PrimaryMembership(memberships);
        Assert.NotNull(primary);
        Assert.Equal(b.Id, primary!.GroupId);
    }

    // ==================== EnrichAsync (ro'yxat/profil/arxiv uchun YAGONA manba) ====================

    [Fact]
    public async Task Enrich_MUZLATILGAN_A_va_AKTIV_B__AKTIV_va_faqat_B_korsatiladi()
    {
        using var db = TestDb.Sqlite();
        var (s, a, b) = FrozenInAActiveInB(db.Context);

        await StudentMembershipView.EnrichAsync(db.Context, new[] { s });

        Assert.True(s.Active);                       // ilgari: false → "Aktiv emas"
        Assert.Equal("active", s.MemberState);
        Assert.Equal(new[] { b.Name }, s.Groups);    // ilgari: bo'sh → klient ClassName (A) ni chizardi
        Assert.Equal(2, s.GroupStates.Count);        // kesimda IKKALASI ham bor (A — muzlatilgan)
        Assert.Equal("frozen", s.GroupStates.Single(g => g.GroupId == a.Id).Status);
        Assert.Equal("t-b", s.GroupStates.Single(g => g.GroupId == b.Id).TeacherId);
    }

    [Fact]
    public async Task Enrich_HAMMA_azoligi_MUZLATILGAN_bolsa_holat_MUZLATILGAN()
    {
        using var db = TestDb.Sqlite();
        var a = AddGroup(db.Context, "A guruh");
        var s = AddStudent(db.Context, className: a.Name);
        AddMembership(db.Context, s, a, "frozen");
        db.Context.SaveChanges();

        await StudentMembershipView.EnrichAsync(db.Context, new[] { s });

        Assert.False(s.Active);
        Assert.Equal("frozen", s.MemberState);
        Assert.Empty(s.Groups); // ro'yxat ustuni muzlatilganlarni O'ZI xira chizadi (eski xatti-harakat)
        Assert.Single(s.GroupStates);
    }

    [Fact]
    public async Task Enrich_AZOLIGI_YOQ_oquvchi__holat_BOSH()
    {
        using var db = TestDb.Sqlite();
        var s = AddStudent(db.Context, className: "Eski guruh");
        db.Context.SaveChanges();

        await StudentMembershipView.EnrichAsync(db.Context, new[] { s });

        Assert.False(s.Active);
        Assert.Equal("", s.MemberState);
        Assert.Empty(s.Groups);
    }

    // ==================== "asosiy guruh" (bitta guruh ko'rsatiladigan joylar) ====================

    [Fact]
    public async Task PrimaryGroup_MUZLATILGAN_guruhni_EMAS_AKTIVNI_qaytaradi()
    {
        using var db = TestDb.Sqlite();
        var (s, _, b) = FrozenInAActiveInB(db.Context);

        var primary = await StudentMembershipView.PrimaryGroupAsync(db.Context, s);

        Assert.NotNull(primary);
        Assert.Equal(b.Id, primary!.Id); // ilgari: ClassName ("A guruh") bo'yicha topilardi
    }

    [Fact]
    public async Task PrimaryGroup_AZOLIK_YOQ_bolsa_ClassName_bilan_topiladi()
    {
        // ORQAGA MOSLIK: juda eski yozuvlarda a'zolik umuman yo'q — u holda ClassName ishlaydi.
        using var db = TestDb.Sqlite();
        var a = AddGroup(db.Context, "A guruh");
        var s = AddStudent(db.Context, className: a.Name);
        db.Context.SaveChanges();

        var primary = await StudentMembershipView.PrimaryGroupAsync(db.Context, s);

        Assert.Equal(a.Id, primary?.Id);
    }

    [Fact]
    public async Task DisplayGroupNames_MUZLATILGANNI_yashiradi_hammasi_muzlagan_bolsa_KORSATADI()
    {
        using var db = TestDb.Sqlite();
        var (s, _, b) = FrozenInAActiveInB(db.Context);
        Assert.Equal(new[] { b.Name }, await StudentMembershipView.DisplayGroupNamesAsync(db.Context, s));

        // B ham muzlatilsa — o'quvchi "guruhsiz" bo'lib qolmasin.
        var mb = db.Context.StudentGroups.Single(m => m.GroupId == b.Id);
        mb.Status = "frozen";
        db.Context.SaveChanges();
        var all = await StudentMembershipView.DisplayGroupNamesAsync(db.Context, s);
        Assert.Equal(new[] { "A guruh", "B guruh" }, all);
    }

    // ==================== hisobot/daftar (bitta "asosiy" guruh tanlaydigan joylar) ====================

    [Fact]
    public async Task StudentReport_GURUH_RAHBARI_muzlatilgan_guruhdan_OLINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var (s, _, b) = FrozenInAActiveInB(db.Context);
        db.Context.Teachers.Add(new Teacher { Id = "t-a", FullName = "Eski o'qituvchi" });
        db.Context.Teachers.Add(new Teacher { Id = "t-b", FullName = "Yangi o'qituvchi" });
        db.Context.SaveChanges();

        var report = await StudentReportBuilder.BuildAsync(db.Context, s);

        // Ilgari: ClassName ("A guruh") mos kelgani tanlanardi → "Eski o'qituvchi".
        Assert.Equal("Yangi o'qituvchi", report.HomeroomTeacher);
        Assert.Equal(b.Name, report.ClassName);
    }

    [Fact]
    public async Task StudentNotebook_GURUH_nomi_TIRIK_azolikdan_olinadi()
    {
        using var db = TestDb.Sqlite();
        var (s, _, b) = FrozenInAActiveInB(db.Context);

        var notebook = await StudentProfileBuilder.BuildAsync(db.Context, s);

        Assert.Equal(b.Name, notebook.ClassName);
    }

    [Fact]
    public void BIR_GURUHDA_IKKINCHI_azolik_qatori_BAZADA_MUMKIN_EMAS()
    {
        // QULF: (StudentId, GroupId) unikal — guruhdan chiqib qayta qo'shilganda MAVJUD qator
        // tiklanadi (`ClassesController.AddMember`), yangi qator YARATILMAYDI. Aynan shu sabab
        // a'zoliklarni guruh bo'yicha lug'atga yig'adigan joylar (`StudentProfileBuilder`,
        // `StudentJournalBuilder`) xavfsiz. Indeks olib tashlansa — bu test qizaradi va o'sha
        // joylarni ustunlik bilan guruhlash kerak bo'ladi.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db.Context, "A guruh");
        var s = AddStudent(db.Context, className: g.Name);
        AddMembership(db.Context, s, g, "frozen", joinedAt: "2026-01-01");
        AddMembership(db.Context, s, g, "active", joinedAt: "2026-05-01");

        Assert.Throws<DbUpdateException>(() => db.Context.SaveChanges());
    }

    // ==================== boshqa yuzalar (regressiya) ====================

    [Fact]
    public async Task Chat_oquvchi_YANGI_guruh_kanalini_ham_koradi()
    {
        // ⚠️ Ilgari `ClassNamesForUserAsync` o'quvchiga FAQAT `ClassName` kanalini berardi:
        // yangi guruhga o'tgan o'quvchi o'sha guruh chatiga UMUMAN kira olmasdi
        // (`CanAccessAsync` shu ro'yxatga qaraydi).
        using var db = TestDb.Sqlite();
        var (s, a, b) = FrozenInAActiveInB(db.Context);
        var user = new AppUser { Role = "student", FullName = s.FullName };
        db.Context.Users.Add(user);
        s.UserId = user.Id;
        db.Context.SaveChanges();

        var chat = new ChatService(db.Context, new FakeChatHub());
        var names = await chat.ClassNamesForUserAsync(user.Id, "student");

        Assert.Equal(new[] { b.Name }, names);
        Assert.True(await chat.CanAccessAsync(user.Id, "student", b.Name));
        // A guruhda MUZLATILGAN — u yerda hozir o'qimayapti, kanali ham ochilmaydi.
        Assert.False(await chat.CanAccessAsync(user.Id, "student", a.Name));
    }

    [Fact]
    public async Task Ledger_JORIY_OYLIK_barcha_AKTIV_guruhlar_yigindisi()
    {
        // ⚠️ Ilgari faqat `ClassName` guruhining narxi ko'rsatilardi: ikki kursda o'qiydigan
        // o'quvchi "oylik 500 000" ko'rardi, jadvalda esa 900 000 hisoblangan bo'lardi.
        using var db = TestDb.Sqlite();
        var a = AddGroup(db.Context, "A guruh");
        var b = AddGroup(db.Context, "B guruh");
        a.MonthlyFee = 500_000m;
        b.MonthlyFee = 400_000m;
        var s = AddStudent(db.Context, className: a.Name);
        AddMembership(db.Context, s, a, "active");
        AddMembership(db.Context, s, b, "active");
        db.Context.SaveChanges();

        var ledger = await StudentLedger.BuildAsync(db.Context, s);

        Assert.Equal(900_000m, ledger.MonthlyFee);
    }

    [Fact]
    public async Task Ledger_MUZLATILGAN_guruh_oylikka_KIRMAYDI()
    {
        using var db = TestDb.Sqlite();
        var (s, a, b) = FrozenInAActiveInB(db.Context);
        db.Context.Classes.Single(c => c.Id == a.Id).MonthlyFee = 500_000m;
        db.Context.Classes.Single(c => c.Id == b.Id).MonthlyFee = 400_000m;
        db.Context.SaveChanges();

        var ledger = await StudentLedger.BuildAsync(db.Context, s);

        Assert.Equal(400_000m, ledger.MonthlyFee);
    }

    [Fact]
    public async Task Reyting_VAKIL_GURUH_aktiv_azolikdan()
    {
        using var db = TestDb.Sqlite();
        var (s, _, b) = FrozenInAActiveInB(db.Context);

        var rows = await RatingService.SchoolAsync(db.Context);

        var row = rows.Single(r => r.Student.Id == s.Id);
        Assert.Equal(b.Name, row.ClassName); // ilgari: ClassName ("A guruh") ustun edi
    }

    [Fact]
    public async Task Jurnal_STANDART_guruh_MUZLATILGANI_emas()
    {
        // ⚠️ Muzlatish `IsActive` ni o'zgartirmaydi, ya'ni eski saralashda ikkala guruh teng
        // chiqib tartib ALIFBOGA tushardi va jurnal "A guruh" bilan ochilardi.
        using var db = TestDb.Sqlite();
        var (s, _, b) = FrozenInAActiveInB(db.Context);

        var journal = await StudentJournalBuilder.PeriodAsync(
            db.Context, s.Id, "2026-01-01", "2026-12-31", null);

        Assert.NotNull(journal);
        Assert.Equal(b.Id, journal!.Groups[0].GroupId);
    }

}

/// <summary>SignalR hub'ining bo'sh o'rnini bosuvchi — <see cref="ChatService"/> a'zolik
/// ro'yxatini hisoblashda hub'ga umuman tegmaydi.</summary>
internal sealed class FakeChatHub : Microsoft.AspNetCore.SignalR.IHubContext<IntellectCRM.Application.Hubs.ChatHub>
{
    public Microsoft.AspNetCore.SignalR.IHubClients Clients => throw new NotSupportedException();
    public Microsoft.AspNetCore.SignalR.IGroupManager Groups => throw new NotSupportedException();
}
