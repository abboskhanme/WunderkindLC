using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// YETIM MEDIA FAYLLARINI TOZALASH — <see cref="MarketingMediaCleanup"/>.
///
/// <para>Modul <c>uploads/marketing-public/</c> (OCHIQ papka, <c>uploads-security.md</c> dagi
/// yagona istisno) ichidagi hech qaysi postda ishlatilmayotgan fayllarni yig'ib oladi.</para>
///
/// <para><b>🔴 Eng muhim test — <see cref="Sweep_Keeps_Orphan_But_Fresh_File"/>:</b>
/// foydalanuvchi faylni yuklab, postni hali SAQLAMAGAN bo'lishi mumkin. O'sha payt fayl
/// hech qaysi postda ko'rinmaydi, ya'ni "yetim" bo'lib turadi — yosh sharti bo'lmasa
/// tozalash uni MODAL OCHIQ TURGANDA o'chirib yuborardi.</para>
/// </summary>
public class MarketingMediaCleanupTests : IDisposable
{
    // =============================================================================================
    //  Test muhiti: vaqtinchalik media papkasi
    // =============================================================================================

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "wunderkind-marketing-media-tests", Guid.NewGuid().ToString("N"));

    public MarketingMediaCleanupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* test tozalash xatosi natijaga ta'sir qilmasin */ }
        GC.SuppressFinalize(this);
    }

    private MarketingMediaCleanup Service(TestDb db) => new(db.Context, _dir, NullLogger.Instance);

    /// <summary>Haqiqiy oqim yozadigan nom bilan fayl yaratadi (<c>{Guid:N}.jpg</c>).</summary>
    /// <param name="ageHours">Faylning yoshi (soat) — yosh sharti aynan shu bilan tekshiriladi.</param>
    private string CreateFile(double ageHours = 0, string ext = ".jpg")
    {
        var name = Guid.NewGuid().ToString("N") + ext;
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-ageHours));
        return name;
    }

    /// <summary>Begona (bizning naqshimizga tushmaydigan) nomli fayl.</summary>
    private string CreateForeignFile(string name, double ageHours = 100)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-ageHours));
        return name;
    }

    private bool Exists(string name) => File.Exists(Path.Combine(_dir, name));

    /// <summary>Ochiq media manzili — bazada AYNAN shunday (absolut URL) saqlanadi.</summary>
    private static string Url(string name) =>
        $"https://crm.example.com{MarketingPublicMedia.RequestPath}/{name}";

    private static IgScheduledPost AddPost(
        TestDb db, string mediaJson, string status = IgPublishConst.StScheduled, string optionsJson = "")
    {
        var post = new IgScheduledPost
        {
            PostType = IgPublishConst.TypeImage,
            MediaJson = mediaJson,
            OptionsJson = optionsJson,
            ScheduledAt = "2026-08-20T10:00:00",
            Status = status,
        };
        db.Context.IgScheduledPosts.Add(post);
        db.Context.SaveChanges();
        return post;
    }

    private static string MediaJson(string url, string coverUrl = "") =>
        IgPublishPayload.WriteMedia(new List<IgMediaJson>
        {
            new() { Url = url, Kind = IgPublishConst.KindImage, CoverUrl = coverUrl },
        });

    // =============================================================================================
    //  1) ISHLATILAYOTGAN FAYL — HECH QACHON o'chirilmaydi
    // =============================================================================================

    /// <summary>
    /// Postda ishlatilayotgan fayl BARCHA holatlarda saqlanadi: bekor qilingan va xato bo'lgan
    /// postlar CRM'da ko'rinadi, joylangani esa tarix — fayl o'chsa ekranda sinuq rasm chiqardi.
    /// </summary>
    [Theory]
    [InlineData(IgPublishConst.StScheduled)]
    [InlineData(IgPublishConst.StProcessing)]
    [InlineData(IgPublishConst.StPublished)]
    [InlineData(IgPublishConst.StFailed)]
    [InlineData(IgPublishConst.StCancelled)]
    public async Task Sweep_Keeps_File_Used_By_Post_In_Any_Status(string status)
    {
        using var db = TestDb.Sqlite();
        var name = CreateFile(ageHours: 200);            // eski, lekin ISHLATILMOQDA
        AddPost(db, MediaJson(Url(name)), status);

        var (deleted, kept, error) = await Service(db).SweepAsync(default);

        Assert.Equal("", error);
        Assert.Equal(0, deleted);
        Assert.Equal(1, kept);
        Assert.True(Exists(name));
    }

    // =============================================================================================
    //  2) 🔴 YOSH SHARTI — yetim, lekin YANGI fayl saqlanadi
    // =============================================================================================

    /// <summary>
    /// 🔴 ENG MUHIM TEST: fayl yuklandi, post hali saqlanmadi. Fayl hech qaysi postda yo'q,
    /// ya'ni "yetim" — lekin u foydalanuvchining OCHIQ TURGAN modalidagi media bo'lishi mumkin.
    /// Bir sutkadan yosh fayl HECH QACHON o'chirilmaydi.
    /// </summary>
    [Theory]
    [InlineData(0.0)]    // hozirgina yuklandi
    [InlineData(1.0)]
    [InlineData(23.5)]   // chegaraga yaqin, lekin hali 24 soat emas
    public async Task Sweep_Keeps_Orphan_But_Fresh_File(double ageHours)
    {
        using var db = TestDb.Sqlite();
        var name = CreateFile(ageHours);

        var (deleted, kept, error) = await Service(db).SweepAsync(default);

        Assert.Equal("", error);
        Assert.Equal(0, deleted);
        Assert.Equal(1, kept);
        Assert.True(Exists(name), "Saqlanmagan chernovikning fayli o'chirib yuborilmasligi kerak.");
    }

    // =============================================================================================
    //  3) YETIM VA ESKI — o'chiriladi
    // =============================================================================================

    [Fact]
    public async Task Sweep_Deletes_Old_Orphan_File()
    {
        using var db = TestDb.Sqlite();
        var orphan = CreateFile(ageHours: 48);
        var used = CreateFile(ageHours: 48);
        var fresh = CreateFile(ageHours: 2);
        AddPost(db, MediaJson(Url(used)));

        var (deleted, kept, error) = await Service(db).SweepAsync(default);

        Assert.Equal("", error);
        Assert.Equal(1, deleted);
        Assert.Equal(2, kept);
        Assert.False(Exists(orphan));
        Assert.True(Exists(used));
        Assert.True(Exists(fresh));
    }

    // =============================================================================================
    //  4) `coverUrl` (Reels muqovasi) ham ISHLATILMOQDA hisoblanadi
    // =============================================================================================

    /// <summary>
    /// Muqova <c>url</c> emas, <c>coverUrl</c> maydonida turadi — faqat <c>url</c> ga qaralsa
    /// muqova fayli yetim deb o'chirilib, Reels muqovasiz (yoki xato bilan) joylanardi.
    /// </summary>
    [Fact]
    public async Task Sweep_Keeps_Reels_Cover_Image()
    {
        using var db = TestDb.Sqlite();
        var video = CreateFile(ageHours: 72, ext: ".mp4");
        var cover = CreateFile(ageHours: 72);
        AddPost(db, MediaJson(Url(video), Url(cover)));

        var (deleted, _, error) = await Service(db).SweepAsync(default);

        Assert.Equal("", error);
        Assert.Equal(0, deleted);
        Assert.True(Exists(video));
        Assert.True(Exists(cover));
    }

    /// <summary>Muqova <c>OptionsJson</c> ichida bo'lsa ham hisobga olinadi — sof funksiya
    /// JSON daraxtining HAR QANDAY satr qiymatini ko'radi.</summary>
    [Fact]
    public void UsedNames_Reads_CoverUrl_From_Options_Json()
    {
        var name = Guid.NewGuid().ToString("N") + ".jpg";
        var options = $$"""{"shareToFeed":true,"coverUrl":"{{Url(name)}}"}""";

        var used = MarketingMediaCleanup.UsedNames(Array.Empty<string?>(), new[] { options });

        Assert.Contains(name, used);
    }

    // =============================================================================================
    //  5) BEGONA NOM — tegilmaydi
    // =============================================================================================

    /// <summary>
    /// Naqshga (32 hex + ruxsat etilgan kengaytma) tushmaydigan fayl bizning oqimimiz yozgan
    /// EMAS — kimdir qo'lda qo'ygan bo'lishi mumkin. Yoshi qanday bo'lishidan qat'i nazar
    /// o'chirilmaydi (faqat sanaladi va logga yoziladi).
    /// </summary>
    [Theory]
    [InlineData("readme.txt")]
    [InlineData("reklama.jpg")]                                  // hex emas
    [InlineData("0123456789abcdef0123456789abcde.jpg")]          // 31 belgi — bir belgi kam
    [InlineData("0123456789abcdef0123456789abcdef.png")]         // ruxsat etilmagan kengaytma
    public async Task Sweep_Never_Deletes_Foreign_Named_File(string name)
    {
        using var db = TestDb.Sqlite();
        CreateForeignFile(name);

        var (deleted, kept, error) = await Service(db).SweepAsync(default);

        Assert.Equal("", error);
        Assert.Equal(0, deleted);
        Assert.Equal(1, kept);
        Assert.True(Exists(name));
    }

    // =============================================================================================
    //  6) PAPKA YO'Q — xato emas
    // =============================================================================================

    [Fact]
    public async Task Sweep_Returns_Empty_When_Folder_Missing()
    {
        using var db = TestDb.Sqlite();
        Directory.Delete(_dir, recursive: true);

        var (deleted, kept, error) = await Service(db).SweepAsync(default);

        Assert.Equal(0, deleted);
        Assert.Equal(0, kept);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task Sweep_Returns_Empty_When_Folder_Is_Empty()
    {
        using var db = TestDb.Sqlite();

        var (deleted, kept, error) = await Service(db).SweepAsync(default);

        Assert.Equal(0, deleted);
        Assert.Equal(0, kept);
        Assert.Equal("", error);
    }

    // =============================================================================================
    //  7) DARHOL O'CHIRISH — bir manzil IKKI postda
    // =============================================================================================

    /// <summary>
    /// 🔴 Foydalanuvchi rejani nusxalasa bir manzil ikki postda bo'ladi. Bittasi o'chirilganda
    /// fayl QOLISHI shart — aks holda ishlab turgan post Meta'da <c>2207052</c> bilan yiqilardi.
    /// </summary>
    [Fact]
    public async Task RemoveUnused_Keeps_File_Still_Used_By_Another_Post()
    {
        using var db = TestDb.Sqlite();
        var name = CreateFile(ageHours: 1);
        var first = AddPost(db, MediaJson(Url(name)));
        AddPost(db, MediaJson(Url(name)));                  // AYNAN o'sha fayl ikkinchi postda

        // Controller naqshi: avval yozuv o'chiriladi va SAQLANADI, keyin tozalash chaqiriladi.
        var names = MarketingMediaCleanup.NamesOf(first.MediaJson, first.OptionsJson);
        db.Context.IgScheduledPosts.Remove(first);
        db.Context.SaveChanges();

        var deleted = await Service(db).RemoveUnusedAsync(names, default);

        Assert.Equal(0, deleted);
        Assert.True(Exists(name));
    }

    /// <summary>Oxirgi post ham o'chirilsa fayl DARHOL ketadi — yosh sharti bu yerda YO'Q
    /// (fayl allaqachon saqlangan postniki edi, "saqlanmagan chernovik" emas).</summary>
    [Fact]
    public async Task RemoveUnused_Deletes_File_Of_Deleted_Post()
    {
        using var db = TestDb.Sqlite();
        var name = CreateFile(ageHours: 0);
        var post = AddPost(db, MediaJson(Url(name)));

        var names = MarketingMediaCleanup.NamesOf(post.MediaJson, post.OptionsJson);
        db.Context.IgScheduledPosts.Remove(post);
        db.Context.SaveChanges();

        var deleted = await Service(db).RemoveUnusedAsync(names, default);

        Assert.Equal(1, deleted);
        Assert.False(Exists(name));
    }

    /// <summary>Tahrirlash naqshi: media almashtirildi — ESKISI o'chadi, YANGISI qoladi.</summary>
    [Fact]
    public async Task RemoveUnused_Deletes_Replaced_Media_Only()
    {
        using var db = TestDb.Sqlite();
        var oldName = CreateFile(ageHours: 0);
        var newName = CreateFile(ageHours: 0);
        var post = AddPost(db, MediaJson(Url(oldName)));

        var oldNames = MarketingMediaCleanup.NamesOf(post.MediaJson, post.OptionsJson);
        post.MediaJson = MediaJson(Url(newName));
        db.Context.SaveChanges();

        var dropped = oldNames.Except(MarketingMediaCleanup.NamesOf(post.MediaJson, post.OptionsJson));
        var deleted = await Service(db).RemoveUnusedAsync(dropped, default);

        Assert.Equal(1, deleted);
        Assert.False(Exists(oldName));
        Assert.True(Exists(newName));
    }

    /// <summary>Begona manzil (boshqa papka) berilsa hech narsa qilinmaydi — darvoza
    /// <see cref="MarketingPublicMedia.SafeStoredName"/> da.</summary>
    [Theory]
    [InlineData("/uploads/certificates/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("../0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.jpg")]
    public async Task RemoveUnused_Ignores_Foreign_Paths(string url)
    {
        using var db = TestDb.Sqlite();

        var deleted = await Service(db).RemoveUnusedAsync(new[] { url }, default);

        Assert.Equal(0, deleted);
    }

    // =============================================================================================
    //  8) BUZUQ JSON — fayl SAQLANADI (noto'g'ri o'chirishdan ehtiyot)
    // =============================================================================================

    /// <summary>
    /// <c>MediaJson</c> o'qilmasa "media yo'q" degani EMAS: ishlab turgan postning fayli yetim
    /// deb o'chirilib ketardi. Buzuq satr xom skanerlanadi va nom baribir topiladi.
    /// </summary>
    [Fact]
    public async Task Sweep_Keeps_File_When_MediaJson_Is_Broken()
    {
        using var db = TestDb.Sqlite();
        var name = CreateFile(ageHours: 100);
        AddPost(db, $$"""[{"url":"{{Url(name)}}", """);      // qirqilgan JSON

        var (deleted, _, error) = await Service(db).SweepAsync(default);

        Assert.Equal("", error);
        Assert.Equal(0, deleted);
        Assert.True(Exists(name));
    }

    // =============================================================================================
    //  9) SOF QOIDALAR
    // =============================================================================================

    [Fact]
    public void IsSweepable_Requires_All_Three_Conditions()
    {
        var name = Guid.NewGuid().ToString("N") + ".jpg";
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var empty = MarketingMediaCleanup.UsedNames(null, null);
        var used = MarketingMediaCleanup.UsedNames(new[] { $"""["{Url(name)}"]""" }, null);

        // yetim + eski → o'chiriladi
        Assert.True(MarketingMediaCleanup.IsSweepable(name, now.AddHours(-25), now, empty));
        // yetim, lekin yosh → yo'q
        Assert.False(MarketingMediaCleanup.IsSweepable(name, now.AddHours(-23), now, empty));
        // eski, lekin ishlatilmoqda → yo'q
        Assert.False(MarketingMediaCleanup.IsSweepable(name, now.AddHours(-25), now, used));
        // begona nom → yo'q
        Assert.False(MarketingMediaCleanup.IsSweepable("reklama.jpg", now.AddHours(-99), now, empty));
        // vaqt oldinga ketgan (soat siljishi) → yo'q
        Assert.False(MarketingMediaCleanup.IsSweepable(name, now.AddHours(5), now, empty));
    }

    [Fact]
    public void UsedNames_Handles_Empty_And_Null_Input()
    {
        Assert.Empty(MarketingMediaCleanup.UsedNames(null, null));
        Assert.Empty(MarketingMediaCleanup.UsedNames(new string?[] { null, "", "   " }, null));
        Assert.Empty(MarketingMediaCleanup.NamesOf("[]", "{}"));
    }

    /// <summary>Nom har xil ko'rinishda kelishi mumkin (absolut URL, nisbiy yo'l, yalang nom,
    /// KATTA harf) — to'plamda BITTA bo'lishi kerak.</summary>
    [Fact]
    public void UsedNames_Normalizes_Different_Url_Forms()
    {
        var stem = Guid.NewGuid().ToString("N");
        var json = $$"""
            [{"url":"{{Url(stem + ".jpg")}}"},
             {"url":"{{MarketingPublicMedia.RequestPath}}/{{stem}}.jpg"},
             {"url":"{{stem.ToUpperInvariant()}}.JPG"}]
            """;

        var used = MarketingMediaCleanup.UsedNames(new[] { json }, null);

        Assert.Single(used);
        Assert.Contains(stem + ".jpg", used);
    }
}
