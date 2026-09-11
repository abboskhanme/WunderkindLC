using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Global o'quvchi qidiruvi (<c>GET /api/admin/students/search</c>) qoidalari —
/// <see cref="StudentSearch"/> sof funksiyalari va SQLite ustidagi provayder-neytral tarmoq
/// (<c>WhereNameFallback</c>/<c>WherePhone</c>). Npgsql'dagi ILIKE tarmog'i testda ishlamaydi
/// (SQLite) — lekin naqsh yasovchi <c>LikePattern</c> shu yerda qoplangan.
/// </summary>
public class StudentSearchTests
{
    /* ---------- Words ---------- */

    [Fact]
    public void Words_KichikHarf_ApostrofBirxil_MaxWordsgacha()
    {
        Assert.Equal(new[] { "to'lqin", "aliyev" }, StudentSearch.Words("  Toʻlqin   ALIYEV "));
        // 6 ta so'z — faqat birinchi 5 tasi olinadi (har so'z alohida WHERE sharti).
        Assert.Equal(StudentSearch.MaxWords, StudentSearch.Words("a b c d e f").Length);
        Assert.Empty(StudentSearch.Words("   "));
    }

    [Theory]
    [InlineData("To’lqin")]
    [InlineData("To`lqin")]
    [InlineData("Toʼlqin")]
    public void Words_BarchaApostrofVariantlari_BittaKorinishga(string input)
    {
        Assert.Equal(new[] { "to'lqin" }, StudentSearch.Words(input));
    }

    /* ---------- Digits ---------- */

    [Fact]
    public void Digits_FaqatRaqamlarQoladi()
    {
        Assert.Equal("998901234567", StudentSearch.Digits("+998 (90) 123-45-67"));
        Assert.Equal("", StudentSearch.Digits("Aliyev"));
    }

    /* ---------- LikePattern ---------- */

    [Fact]
    public void LikePattern_MaxsusBelgilarEkranlanadi_ApostrofJoker()
    {
        // % _ \ — LIKE maxsus belgilari, \ bilan ekranlanadi (SQL'da ESCAPE '\').
        Assert.Equal(@"%50\%%", StudentSearch.LikePattern("50%"));
        Assert.Equal(@"%a\_b%", StudentSearch.LikePattern("a_b"));
        // Apostrof -> _ (bitta-belgi joker): bazada ' yoki ʻ turganidan qat'i nazar topiladi.
        Assert.Equal("%to_lqin%", StudentSearch.LikePattern("to'lqin"));
    }

    /* ---------- MemberState ---------- */

    [Fact]
    public void MemberState_Ustunlik_ActiveTrialFrozen()
    {
        Assert.Equal("active", StudentSearch.MemberState(new[] { "frozen", "active", "trial" }));
        Assert.Equal("trial", StudentSearch.MemberState(new[] { "frozen", "trial" }));
        Assert.Equal("frozen", StudentSearch.MemberState(new[] { "frozen" }));
        Assert.Equal("", StudentSearch.MemberState(Array.Empty<string?>()));
    }

    /// <summary>
    /// «AKTIV MUZLATISH» (yangi o'quv yiliga o'tish) — a'zolik <c>Status</c>i baribir "frozen",
    /// belgi ALOHIDA bayroqda keladi. Ustunlikda u oddiy "frozen" dan YUQORI: ikkala xil
    /// muzlatishi bor o'quvchi ro'yxatda aynan "aktiv muzlatilgan" bo'lib ko'rinishi kerak
    /// (savol — "yangi yilga nechta o'quvchi bilan o'tyapmiz").
    /// </summary>
    [Fact]
    public void MemberState_YearFrozen_FrozenDAN_YUQORI_ammo_TrialDAN_PAST()
    {
        Assert.Equal("yearFrozen", StudentSearch.MemberState(new[] { "frozen" }, yearFreeze: true));
        // Faol yoki sinovdagi a'zoligi bor o'quvchi — muzlatilgan deb ko'rsatilmaydi: u haqiqatan
        // qatnayapti, "aktiv muzlatish" esa faqat qatnamayotganini ajratish uchun.
        Assert.Equal("active", StudentSearch.MemberState(new[] { "frozen", "active" }, yearFreeze: true));
        Assert.Equal("trial", StudentSearch.MemberState(new[] { "frozen", "trial" }, yearFreeze: true));
    }

    /// <summary>
    /// Bayroq FAQAT muzlatilgan a'zolik bor bo'lganda ma'noga ega — u muzlatishning TURINI
    /// bildiradi, o'z-o'zidan holat EMAS. Guruhsiz o'quvchi baribir bo'sh yorliq oladi.
    /// </summary>
    [Fact]
    public void MemberState_Muzlatilgan_azolik_YOQ_bolsa_bayroq_HECH_NARSA_ozgartirmaydi()
    {
        Assert.Equal("", StudentSearch.MemberState(Array.Empty<string?>(), yearFreeze: true));
        Assert.Equal("active", StudentSearch.MemberState(new[] { "active" }, yearFreeze: true));
    }

    /// <summary>
    /// ORTGA MOSLIK: bayroqsiz (eski) chaqiruvlar avvalgidek "frozen" olishi SHART — overload
    /// standart qiymat bilan qo'shildi, mavjud chaqiruv joylari o'zgartirilmagan.
    /// </summary>
    [Fact]
    public void MemberState_Bayroqsiz_chaqiruv_avvalgidek_frozen()
    {
        Assert.Equal("frozen", StudentSearch.MemberState(new[] { "frozen" }));
        Assert.Equal("frozen", StudentSearch.MemberState(new[] { "frozen" }, yearFreeze: false));
    }

    /* ---------- ClampLimit ---------- */

    [Fact]
    public void ClampLimit_1dan50gacha()
    {
        Assert.Equal(1, StudentSearch.ClampLimit(0));
        Assert.Equal(12, StudentSearch.ClampLimit(12));
        Assert.Equal(StudentSearch.MaxLimit, StudentSearch.ClampLimit(500));
    }

    /* ---------- SQLite: ism bo'yicha (provayder-neytral tarmoq) ---------- */

    private static Student St(string fullName, string phone = "", string father = "",
        string fatherPhone = "", bool archived = false) => new()
    {
        FullName = fullName,
        Phone = phone,
        FatherFullName = father,
        FatherPhone = fatherPhone,
        IsArchived = archived,
    };

    [Fact]
    public async Task WhereNameFallback_SozTartibiMuhimEmas_ApostrofVariantiTopiladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.Students.AddRange(
            St("Aliyev Toʻlqin Akramovich"),   // bazada ʻ (modifier letter) varianti
            St("Karimov Bekzod"),
            St("Toshmatova Lola"));
        await db.Context.SaveChangesAsync();

        // Odam "ism familiya" deb yozadi — bazada esa "Familiya Ism" turadi.
        var words = StudentSearch.Words("to'lqin aliyev");
        var found = await StudentSearch.WhereNameFallback(db.Context.Students.AsNoTracking(), words)
            .Select(s => s.FullName).ToListAsync();

        Assert.Equal(new[] { "Aliyev Toʻlqin Akramovich" }, found);
    }

    [Fact]
    public async Task WhereNameFallback_OtaOnaIsmiBoyichaHamTopadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.Students.AddRange(
            St("Aliyev Sardor", father: "Aliyev Botir"),
            St("Karimov Bekzod", father: "Karimov Olim"));
        await db.Context.SaveChangesAsync();

        var found = await StudentSearch
            .WhereNameFallback(db.Context.Students.AsNoTracking(), StudentSearch.Words("botir"))
            .Select(s => s.FullName).ToListAsync();

        Assert.Equal(new[] { "Aliyev Sardor" }, found);
    }

    /* ---------- SQLite: telefon bo'yicha ---------- */

    [Fact]
    public async Task WherePhone_AjratkichlarsizQismiyMoslik_OtaRaqamiHam()
    {
        using var db = TestDb.Sqlite();
        db.Context.Students.AddRange(
            St("Aliyev Sardor", phone: "+998-90-123-45-67"),
            St("Karimov Bekzod", fatherPhone: "+998 (91) 765-43-21"),
            St("Toshmatova Lola", phone: "+998-93-555-55-55"));
        await db.Context.SaveChangesAsync();

        var q = db.Context.Students.AsNoTracking();

        // O'z raqamining o'rtasidan qismiy moslik.
        Assert.Equal(new[] { "Aliyev Sardor" },
            await StudentSearch.WherePhone(q, "9012345").Select(s => s.FullName).ToListAsync());
        // Ota raqami bo'yicha ham topiladi.
        Assert.Equal(new[] { "Karimov Bekzod" },
            await StudentSearch.WherePhone(q, "917654").Select(s => s.FullName).ToListAsync());
    }

    [Fact]
    public void Normalize_TartiblashUchun_PrefiksAniqlanadi()
    {
        // Controller tartibi: birinchi so'z bilan BOSHLANGAN ism tepaga chiqadi.
        Assert.StartsWith("to'lqin", StudentSearch.Normalize("Toʻlqin Aliyev"));
    }

    /* ---------- ENDPOINT OQIMI (SQLite, provayder-neytral tarmoq) ----------
     *
     * `StudentsController.Search` bilan BIR XIL bosqichlar: so'zlar → ism filtri → telefon
     * filtri (alohida) → birlashtirish/takrorsizlash → a'zoliklar join → MemberState.
     * Controller test loyihasidan chaqirib bo'lmaydi (Server reference yo'q) — shu sabab
     * pipeline shu yerda aynan takrorlanadi va DTO'gacha tekshiriladi. */

    private sealed record Row(string Id, string FullName, string Phone, string ParentPhone,
        string FatherPhone, string MotherPhone, bool IsArchived);

    /// <summary>Controller'dagi Search'ning provayder-neytral nusxasi (SQLite tarmog'i).</summary>
    private static async Task<List<(string FullName, string MemberState, string[] Groups, bool IsArchived)>>
        RunSearchAsync(AppDbContext db, string term, int limit = 12)
    {
        static IQueryable<Row> Project(IQueryable<Student> src) => src.Select(s =>
            new Row(s.Id, s.FullName, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone, s.IsArchived));

        var words = StudentSearch.Words(term);
        var digits = StudentSearch.Digits(term);

        // ⚠️ Controller'dagi TUZATILGAN tartib: OrderBy/Take PROYEKSIYADAN OLDIN. `Row`/`SearchRow`
        // pozitsion record — EF uni KONSTRUKTOR orqali quradi va konstruktor-proyeksiya a'zosiga
        // keyingi operatorda murojaat qilib bo'lmaydi (`Project(...).OrderBy(r => r.FullName)`
        // "could not be translated" bilan yiqilardi — qidiruv HAR DOIM 500 edi).
        var nameRows = new List<Row>();
        if (words.Length > 0)
            nameRows = await Project(StudentSearch.WhereNameFallback(db.Students.AsNoTracking(), words)
                .OrderBy(s => s.FullName).Take(limit)).ToListAsync();

        var phoneRows = new List<Row>();
        if (digits.Length >= StudentSearch.MinPhoneDigits)
            phoneRows = await Project(StudentSearch.WherePhone(db.Students.AsNoTracking(), digits)
                .OrderBy(s => s.FullName).Take(limit)).ToListAsync();

        var first = words.FirstOrDefault() ?? string.Empty;
        var merged = nameRows.Concat(phoneRows)
            .GroupBy(r => r.Id).Select(g => g.First())
            .OrderBy(r => first.Length > 0 && StudentSearch.Normalize(r.FullName).StartsWith(first) ? 0 : 1)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        var ids = merged.Select(r => r.Id).ToList();
        var memberships = await (from sg in db.StudentGroups
                                 join c in db.Classes on sg.GroupId equals c.Id
                                 where sg.IsActive && ids.Contains(sg.StudentId)
                                 select new { sg.StudentId, c.Name, sg.Status }).ToListAsync();
        var groupsBy = memberships.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.Name, Status: x.Status ?? "")).ToList());

        return merged.Select(r =>
        {
            var groups = groupsBy.GetValueOrDefault(r.Id) ?? new();
            return (r.FullName,
                StudentSearch.MemberState(groups.Select(x => (string?)x.Status)),
                groups.Select(x => x.Name).ToArray(),
                r.IsArchived);
        }).ToList();
    }

    [Fact]
    public async Task Endpoint_Oqimi_Ism_Telefon_Azolik_Arxiv()
    {
        using var db = TestDb.Sqlite();
        var g1 = new Group { Name = "IELTS-1" };
        var g2 = new Group { Name = "IELTS-2" };
        var tolqin = St("Aliyev Toʻlqin", phone: "+998-90-123-45-67");
        var lola = St("Karimova Lola", fatherPhone: "+998 (91) 765-43-21", archived: true);
        var bekzod = St("Toshmatov Bekzod");
        db.Context.Classes.AddRange(g1, g2);
        db.Context.Students.AddRange(tolqin, lola, bekzod);
        db.Context.StudentGroups.AddRange(
            new StudentGroup { StudentId = tolqin.Id, GroupId = g1.Id, IsActive = true, Status = "active" },
            new StudentGroup { StudentId = tolqin.Id, GroupId = g2.Id, IsActive = true, Status = "frozen" },
            // IsActive=false a'zolik natijaga KIRMAYDI (chiqib ketgan guruh).
            new StudentGroup { StudentId = bekzod.Id, GroupId = g1.Id, IsActive = false, Status = "active" });
        await db.Context.SaveChangesAsync();

        // Ism bo'yicha ("ism familiya" tartibida, apostrof oddiy '): a'zoliklar ham keladi,
        // MemberState — active > frozen ustunligi bilan.
        var byName = await RunSearchAsync(db.Context, "to'lqin aliyev");
        var hit = Assert.Single(byName);
        Assert.Equal("Aliyev Toʻlqin", hit.FullName);
        Assert.Equal("active", hit.MemberState);
        Assert.Equal(2, hit.Groups.Length);

        // Telefon bo'yicha (ajratkichlarsiz qismiy) — ARXIVLANGAN o'quvchi ham topiladi
        // (ota raqami orqali), guruhsiz — MemberState bo'sh.
        var byPhone = await RunSearchAsync(db.Context, "917654");
        var lolaHit = Assert.Single(byPhone);
        Assert.Equal("Karimova Lola", lolaHit.FullName);
        Assert.True(lolaHit.IsArchived);
        Assert.Equal("", lolaHit.MemberState);

        // Aralash so'rov ("ism + raqam"): ism tarmog'i topmaydi (raqam ismda yo'q),
        // telefon tarmog'i topadi — natija YO'QOLMAYDI va takrorlanmaydi.
        var mixed = await RunSearchAsync(db.Context, "aliyev 9012345");
        Assert.Equal("Aliyev Toʻlqin", Assert.Single(mixed).FullName);

        // Chiqib ketgan (IsActive=false) a'zolik guruh ro'yxatida ko'rinmaydi.
        var bek = await RunSearchAsync(db.Context, "bekzod");
        Assert.Empty(Assert.Single(bek).Groups);
    }

    /* ---------- Npgsql tarmog'i: ILIKE tarjimasi (jonli baza KERAK EMAS) ---------- */

    /// <summary>
    /// Prod'dagi (Npgsql) tarmoq testlarda ishlamay qolmasin: controller'dagi AYNAN shu
    /// ifoda (<c>EF.Functions.ILike(..., pattern, "\")</c> + <c>SearchRow</c> proyeksiyasi)
    /// SQL'ga tarjima bo'lishini <c>ToQueryString</c> bilan tekshiramiz — tarjima buzilsa
    /// bu chaqiruv istisno otadi (500 regressiyasi shu yerda ushlanadi).
    /// </summary>
    [Fact]
    public void Npgsql_ILike_VaTelefon_Tarjimasi_SqlGaOtadi()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=_tarjima_test_")
            .Options;
        using var ctx = new AppDbContext(options);

        var words = StudentSearch.Words("to'lqin aliyev");
        IQueryable<Student> nq = ctx.Students.AsNoTracking();
        foreach (var w in words)
        {
            var p = StudentSearch.LikePattern(w);
            nq = nq.Where(s =>
                EF.Functions.ILike(s.FullName, p, "\\")
                || EF.Functions.ILike(s.ParentFullName, p, "\\")
                || EF.Functions.ILike(s.FatherFullName, p, "\\")
                || EF.Functions.ILike(s.MotherFullName, p, "\\"));
        }
        // Controller'dagi TUZATILGAN shakl: OrderBy/Take avval, RECORD proyeksiyasi keyin.
        var nameSql = nq.OrderBy(s => s.FullName).Take(12)
            .Select(s => new Row(s.Id, s.FullName, s.Phone, s.ParentPhone,
                s.FatherPhone, s.MotherPhone, s.IsArchived))
            .ToQueryString();
        Assert.Contains("ILIKE", nameSql);
        Assert.Contains("ESCAPE", nameSql);

        var phoneSql = StudentSearch.WherePhone(ctx.Students.AsNoTracking(), "9012345")
            .OrderBy(s => s.FullName).Take(12)
            .Select(s => new Row(s.Id, s.FullName, s.Phone, s.ParentPhone,
                s.FatherPhone, s.MotherPhone, s.IsArchived))
            .ToQueryString();
        Assert.Contains("replace", phoneSql, StringComparison.OrdinalIgnoreCase);

        // REGRESSIYA QULFI: ESKI tartib (avval pozitsion-record proyeksiya, KEYIN OrderBy) EF'da
        // tarjima bo'lmaydi ("could not be translated") — endpoint shu sabab HAR so'rovda 500
        // qaytargan edi. Kimdir shu naqshga qaytarsa, bu assert darhol qizaradi.
        Assert.Throws<InvalidOperationException>(() => nq
            .Select(s => new Row(s.Id, s.FullName, s.Phone, s.ParentPhone,
                s.FatherPhone, s.MotherPhone, s.IsArchived))
            .OrderBy(r => r.FullName).Take(12).ToQueryString());
    }
}
