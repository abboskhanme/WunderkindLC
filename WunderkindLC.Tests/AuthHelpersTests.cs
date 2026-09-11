using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// AUTH yordamchilari: parol hash'i (<see cref="PasswordHasher"/>), akkaunt yaratish
/// (<see cref="AccountFactory"/>, <see cref="AccountExtensions"/>) va bot orqali beriladigan
/// bir martalik kirish kodi (<see cref="LoginOtpService"/>).
/// </summary>
public class AuthHelpersTests
{
    // ===================== 1) Parol hash'i =====================

    [Fact]
    public void Hash_uch_qismli_formatda_va_iteratsiyalar_soni_bilan()
    {
        var hash = PasswordHasher.Hash("parol123");
        var parts = hash.Split('.');

        Assert.Equal(3, parts.Length);
        Assert.Equal("100000", parts[0]);
        Assert.Equal(16, Convert.FromBase64String(parts[1]).Length);   // salt
        Assert.Equal(32, Convert.FromBase64String(parts[2]).Length);   // kalit
    }

    [Fact]
    public void Hash_bir_xil_parolda_ham_har_safar_boshqacha_boladi()
    {
        // Har hash'da tasodifiy salt — bir xil parolli ikki foydalanuvchi bazada bir xil ko'rinmaydi.
        var a = PasswordHasher.Hash("bir xil parol");
        var b = PasswordHasher.Hash("bir xil parol");

        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("bir xil parol", a));
        Assert.True(PasswordHasher.Verify("bir xil parol", b));
    }

    [Fact]
    public void Verify_togri_parolda_true_notogrida_false()
    {
        var hash = PasswordHasher.Hash("Parol123!");

        Assert.True(PasswordHasher.Verify("Parol123!", hash));
        Assert.False(PasswordHasher.Verify("parol123!", hash));   // registr muhim
        Assert.False(PasswordHasher.Verify("Parol123", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Verify_uzun_va_unicode_parollarni_qollaydi()
    {
        var pwd = "Oʻzbekiston-2026 ✅ " + new string('x', 200);
        Assert.True(PasswordHasher.Verify(pwd, PasswordHasher.Hash(pwd)));
    }

    [Fact]
    public void Verify_bosh_parolni_ham_togri_tekshiradi()
    {
        var hash = PasswordHasher.Hash("");
        Assert.True(PasswordHasher.Verify("", hash));
        Assert.False(PasswordHasher.Verify("x", hash));
    }

    [Theory]
    [InlineData("")]                       // BlockLogin — login bloklangan akkaunt
    [InlineData("faqat-matn")]
    [InlineData("100000.faqatikkiqism")]
    [InlineData("abc.YWJj.YWJj")]          // iteratsiyalar soni raqam emas
    public void Verify_buzuq_hashda_false_qaytaradi(string stored)
    {
        Assert.False(PasswordHasher.Verify("parol", stored));
    }

    [Fact]
    public void Verify_hash_qismi_ozgartirilgan_bolsa_false()
    {
        var parts = PasswordHasher.Hash("parol").Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{Convert.ToBase64String(new byte[32])}";
        Assert.False(PasswordHasher.Verify("parol", tampered));
    }

    [Fact]
    public void Verify_base64_bolmagan_saltda_istisno_tashlaydi()
    {
        // HOZIRGI XULQ (xatoni qayd etuvchi yashil test): buzuq (base64 bo'lmagan) hash
        // FormatException beradi — login endpointida bu 500 xatoga aylanadi.
        Assert.Throws<FormatException>(() => PasswordHasher.Verify("parol", "100000.@@@.###"));
    }

    [Fact(Skip = "XATO (PasswordHasher.cs:28-29): Verify buzuq saqlangan hash'da " +
                 "`Convert.FromBase64String` orqali FormatException tashlaydi (null saqlanganda esa " +
                 "NullReferenceException) — noto'g'ri parol o'rniga 500 Internal Server Error. " +
                 "Tuzatish: `Convert.TryFromBase64String` bilan tekshirib, muvaffaqiyatsiz bo'lsa false qaytarish.")]
    public void Verify_base64_bolmagan_saltda_false_qaytarishi_kerak()
    {
        Assert.False(PasswordHasher.Verify("parol", "100000.@@@.###"));
    }

    // ===================== 2) Akkaunt (login/parol) yaratish =====================

    [Fact]
    public void GeneratePassword_standart_uzunligi_8_va_chalkash_belgilarsiz()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        for (var i = 0; i < 20; i++)
        {
            var pwd = AccountFactory.GeneratePassword();
            Assert.Equal(8, pwd.Length);
            Assert.All(pwd, ch => Assert.Contains(ch, alphabet));
        }
        // Chalkashadigan belgilar (0/o, 1/l/i) alifboda YO'Q.
        Assert.DoesNotContain('0', alphabet);
        Assert.DoesNotContain('1', alphabet);
        Assert.DoesNotContain('l', alphabet);
    }

    [Fact]
    public void GeneratePassword_uzunligi_sozlanadi_va_takrorlanmaydi()
    {
        Assert.Equal(16, AccountFactory.GeneratePassword(16).Length);
        Assert.Equal(4, AccountFactory.GeneratePassword(4).Length);
        Assert.NotEqual(AccountFactory.GeneratePassword(16), AccountFactory.GeneratePassword(16));
    }

    [Fact]
    public void GenerateUsername_FISHdan_familiya_va_ismni_qoshib_tuzadi()
    {
        using var db = TestDb.Sqlite();
        Assert.Equal("voxidjonovabduxalil",
            AccountFactory.GenerateUsername(db.Context, "Voxidjonov Abduxalil"));
    }

    [Fact]
    public void GenerateUsername_faqat_birinchi_ikki_sozni_oladi()
    {
        using var db = TestDb.Sqlite();
        Assert.Equal("aliyevvali",
            AccountFactory.GenerateUsername(db.Context, "Aliyev Vali Salimovich"));
    }

    [Fact]
    public void GenerateUsername_apostrof_va_belgilarni_tashlab_yuboradi()
    {
        using var db = TestDb.Sqlite();
        Assert.Equal("gulomovotkir",
            AccountFactory.GenerateUsername(db.Context, "G'ulomov O'tkir"));
    }

    [Fact]
    public void GenerateUsername_kirill_FISHni_lotinga_ogiradi()
    {
        using var db = TestDb.Sqlite();
        Assert.Equal("karimovshuxrat",
            AccountFactory.GenerateUsername(db.Context, "Каримов Шухрат"));
    }

    [Fact]
    public void GenerateUsername_bosh_FISHda_user_beradi()
    {
        using var db = TestDb.Sqlite();
        Assert.Equal("user", AccountFactory.GenerateUsername(db.Context, "   "));
        Assert.Equal("user", AccountFactory.GenerateUsername(db.Context, "!!! ???"));
    }

    [Fact]
    public void GenerateUsername_band_login_uchun_raqam_qoshadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Users.Add(new AppUser { FullName = "Aliyev Vali", Email = "aliyevvali", Role = Roles.Staff });
        ctx.SaveChanges();

        Assert.Equal("aliyevvali2", AccountFactory.GenerateUsername(ctx, "Aliyev Vali"));

        ctx.Users.Add(new AppUser { FullName = "Aliyev Vali", Email = "aliyevvali2", Role = Roles.Staff });
        ctx.SaveChanges();

        Assert.Equal("aliyevvali3", AccountFactory.GenerateUsername(ctx, "Aliyev Vali"));
    }

    [Fact]
    public void GenerateUsername_registr_farqini_ham_band_deb_hisoblaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Users.Add(new AppUser { FullName = "Aliyev Vali", Email = "ALIYEVVALI", Role = Roles.Staff });
        ctx.SaveChanges();

        Assert.Equal("aliyevvali2", AccountFactory.GenerateUsername(ctx, "Aliyev Vali"));
    }

    [Fact]
    public void GenerateUsername_hali_saqlanmagan_akkauntni_ham_band_deb_biladi()
    {
        // Bir SaveChanges ichida bir nechta o'quvchi qo'shilsa loginlar to'qnashmasligi kerak.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var first = AccountFactory.CreateAccountFor(ctx, Roles.Student, "Aliyev Vali");
        var second = AccountFactory.CreateAccountFor(ctx, Roles.Student, "Aliyev Vali");

        Assert.Equal("aliyevvali", first.Email);
        Assert.Equal("aliyevvali2", second.Email);
        ctx.SaveChanges();
        Assert.Equal(2, ctx.Users.Count());
    }

    [Fact]
    public void CreateAccountFor_parolni_hashlaydi_va_ochiq_nusxasini_qaytaradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var user = AccountFactory.CreateAccountFor(ctx, Roles.Teacher, "Karimov Karim", out var plain);

        Assert.Equal(Roles.Teacher, user.Role);
        Assert.Equal("Karimov Karim", user.FullName);
        Assert.Equal(8, plain.Length);
        Assert.Equal(plain, user.InitialPassword);          // superadmin ko'rishi uchun
        Assert.NotEqual(plain, user.PasswordHash);          // ochiq parol hash o'rniga yozilmaydi
        Assert.True(PasswordHasher.Verify(plain, user.PasswordHash));
    }

    [Fact]
    public void SetInitialPassword_hash_va_ochiq_parolni_saqlaydi()
    {
        var user = new AppUser { FullName = "Xodim", Email = "xodim", Role = Roles.Staff };
        user.SetInitialPassword("yangi123");

        Assert.Equal("yangi123", user.InitialPassword);
        Assert.True(PasswordHasher.Verify("yangi123", user.PasswordHash));
    }

    [Fact]
    public void SetOwnPassword_ochiq_parolni_tozalaydi()
    {
        var user = new AppUser { FullName = "Xodim", Email = "xodim", Role = Roles.Staff };
        user.SetInitialPassword("boshlangich");
        user.SetOwnPassword("mening-parolim");

        Assert.Null(user.InitialPassword);
        Assert.True(PasswordHasher.Verify("mening-parolim", user.PasswordHash));
        Assert.False(PasswordHasher.Verify("boshlangich", user.PasswordHash));
    }

    [Fact]
    public void BlockLogin_parolni_bosatadi_va_hech_qaysi_parol_otmaydi()
    {
        var user = new AppUser { FullName = "Xodim", Email = "xodim", Role = Roles.Staff };
        user.SetInitialPassword("parol");
        user.BlockLogin();

        Assert.Equal("", user.PasswordHash);
        Assert.Null(user.InitialPassword);
        Assert.False(PasswordHasher.Verify("parol", user.PasswordHash));
        Assert.False(PasswordHasher.Verify("", user.PasswordHash));
    }

    // ===================== 3) Bir martalik kirish kodi (OTP) =====================

    private const long ChatId = 555_000_111;

    [Fact]
    public void Kod_muddati_va_sorash_oraligi_qoidaga_mos()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), LoginOtpService.CodeTtl);
        Assert.Equal(TimeSpan.FromMinutes(5), LoginOtpService.RequestCooldown);
    }

    [Fact]
    public async Task Kod_8_belgi_va_chalkash_belgilarsiz_alifbodan()
    {
        const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        for (var i = 0; i < 10; i++)
        {
            var code = await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);
            Assert.Equal(8, code.Length);
            Assert.All(code, ch => Assert.Contains(ch, alphabet));
        }
        Assert.DoesNotContain('0', alphabet);
        Assert.DoesNotContain('O', alphabet);
        Assert.DoesNotContain('1', alphabet);
        Assert.DoesNotContain('I', alphabet);
        Assert.DoesNotContain('L', alphabet);
    }

    [Fact]
    public async Task Kod_bazada_ochiq_matnda_saqlanmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var code = await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);

        var row = Assert.Single(ctx.LoginOtpCodes);
        Assert.NotEqual(code, row.CodeHash);
        Assert.Equal(64, row.CodeHash.Length);              // SHA-256 hex
        Assert.Equal("u-1", row.UserId);
        Assert.Equal(ChatId, row.ChatId);
        Assert.False(row.Used);
        Assert.Null(row.ConsumedAt);
        Assert.Equal(60, (row.ExpiresAt - row.CreatedAt).TotalSeconds, 1);
    }

    [Fact]
    public async Task Yangi_kod_sorash_eski_kodni_bekor_qiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var first = await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);
        var second = await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);

        Assert.Null(await LoginOtpService.VerifyAndConsumeAsync(ctx, first, default));
        Assert.Equal("u-1", await LoginOtpService.VerifyAndConsumeAsync(ctx, second, default));
    }

    [Fact]
    public async Task Yangi_kod_boshqa_foydalanuvchining_kodini_bekor_qilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var mine = await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);
        await LoginOtpService.IssueAsync(ctx, "u-2", ChatId, default);

        Assert.Equal("u-1", await LoginOtpService.VerifyAndConsumeAsync(ctx, mine, default));
    }

    [Fact]
    public async Task Kod_bir_marta_ishlatiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var code = await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);

        Assert.Equal("u-1", await LoginOtpService.VerifyAndConsumeAsync(ctx, code, default));
        Assert.Null(await LoginOtpService.VerifyAndConsumeAsync(ctx, code, default));

        var row = Assert.Single(ctx.LoginOtpCodes);
        Assert.True(row.Used);
        Assert.NotNull(row.ConsumedAt);
    }

    [Fact]
    public async Task Muddati_otgan_kod_ishlamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var code = await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);

        var row = ctx.LoginOtpCodes.Single();
        row.ExpiresAt = AppClock.Now.AddSeconds(-1);
        ctx.SaveChanges();

        Assert.Null(await LoginOtpService.VerifyAndConsumeAsync(ctx, code, default));
        Assert.False(ctx.LoginOtpCodes.Single().Used);   // muddati o'tgan kod "ishlatilgan" deb belgilanmaydi
    }

    [Fact]
    public async Task Notogri_kod_null_qaytaradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);

        Assert.Null(await LoginOtpService.VerifyAndConsumeAsync(ctx, "ZZZZZZZZ", default));
        Assert.Null(await LoginOtpService.VerifyAndConsumeAsync(ctx, "", default));
        Assert.Null(await LoginOtpService.VerifyAndConsumeAsync(ctx, "   ", default));
    }

    [Fact]
    public async Task Kod_kichik_harf_va_boshliqlar_bilan_kiritilsa_ham_qabul_qilinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var code = await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);

        Assert.Equal("u-1", await LoginOtpService.VerifyAndConsumeAsync(ctx, $"  {code.ToLowerInvariant()} ", default));
    }

    [Fact]
    public async Task Oxirgi_sorov_vaqti_chat_boyicha_qaytariladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        Assert.Null(await LoginOtpService.LastRequestAtAsync(ctx, ChatId, default));

        await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);
        var at = await LoginOtpService.LastRequestAtAsync(ctx, ChatId, default);

        Assert.NotNull(at);
        Assert.InRange((AppClock.Now - at!.Value).TotalMinutes, -1, 1);
        // Boshqa chatda so'rov bo'lmagan.
        Assert.Null(await LoginOtpService.LastRequestAtAsync(ctx, ChatId + 1, default));
    }

    [Fact]
    public async Task Oxirgi_sorov_vaqti_eng_yangi_yozuvni_beradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        await LoginOtpService.IssueAsync(ctx, "u-1", ChatId, default);
        var old = ctx.LoginOtpCodes.Single();
        old.CreatedAt = AppClock.Now.AddMinutes(-30);
        ctx.SaveChanges();

        await LoginOtpService.IssueAsync(ctx, "u-2", ChatId, default);

        var at = await LoginOtpService.LastRequestAtAsync(ctx, ChatId, default);
        Assert.InRange((AppClock.Now - at!.Value).TotalMinutes, -1, 1);
    }
}
