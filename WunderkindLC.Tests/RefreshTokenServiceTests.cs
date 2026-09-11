using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// REFRESH TOKEN oqimi (<see cref="RefreshTokenService"/>) — rotatsiya, grace oynasi va reuse
/// hujumida butun naslni (family) bekor qilish. Bular xavfsizlik mantig'i, shuning uchun sof
/// servis darajasida (haqiqiy SQLite bazasi ustida) sinaladi.
/// </summary>
public class RefreshTokenServiceTests
{
    private static JwtOptions Opts() => new()
    {
        Key = new string('k', 48), Issuer = "T", Audience = "T",
        AccessMinutes = 60, RefreshDays = 30,
    };

    private static RefreshTokenService Svc(AppDbContext db) =>
        new(db, new JwtTokenService(Opts()), Opts(), NullLogger<RefreshTokenService>.Instance);

    /// <summary>Test uchun admin foydalanuvchi (ValidUserAsync uni yaroqli deb qaytaradi).</summary>
    private static async Task<AppUser> SeedUserAsync(AppDbContext db)
    {
        var u = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = "a@a.a" };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task Issue_saqlaydi_hash_va_muddat()
    {
        using var t = TestDb.Sqlite();
        var user = await SeedUserAsync(t.Context);
        var svc = Svc(t.Context);

        var res = await svc.IssueAsync(user.Id);

        Assert.False(string.IsNullOrWhiteSpace(res.RawToken));
        var row = await t.Context.RefreshTokens.SingleAsync();
        // Xom token bazaga YOZILMAYDI — faqat hashi.
        Assert.NotEqual(res.RawToken, row.TokenHash);
        Assert.Equal(res.FamilyId, row.FamilyId);
        Assert.True(row.ExpiresAt > DateTime.UtcNow.AddDays(29));
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task Rotate_yangi_juftlik_beradi_eskisini_bekor_qiladi()
    {
        using var t = TestDb.Sqlite();
        var user = await SeedUserAsync(t.Context);
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(user.Id);

        var r = await svc.RotateAsync(issued.RawToken);

        Assert.True(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(r.RefreshToken));
        Assert.NotEqual(issued.RawToken, r.RefreshToken);

        // Eski token bekor, yangisi faol, ikkalasi ham AYNI family.
        var rows = await t.Context.RefreshTokens.OrderBy(x => x.CreatedAt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].RevokedAt);              // eski
        Assert.NotNull(rows[0].ReplacedByHash);
        Assert.Null(rows[1].RevokedAt);                 // yangi
        Assert.All(rows, x => Assert.Equal(issued.FamilyId, x.FamilyId));
    }

    [Fact]
    public async Task Rotate_notogri_token_401()
    {
        using var t = TestDb.Sqlite();
        await SeedUserAsync(t.Context);
        var svc = Svc(t.Context);

        var r = await svc.RotateAsync("yoq-bunday-token");

        Assert.False(r.Ok);
        Assert.Equal("not_found", r.Error);
    }

    [Fact]
    public async Task Rotate_eskirgan_token_401()
    {
        using var t = TestDb.Sqlite();
        var user = await SeedUserAsync(t.Context);
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(user.Id);

        // Muddatini o'tmishga suramiz (bekor qilinmagan, lekin eskirgan).
        var row = await t.Context.RefreshTokens.SingleAsync();
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await t.Context.SaveChangesAsync();

        var r = await svc.RotateAsync(issued.RawToken);

        Assert.False(r.Ok);
        Assert.Equal("expired", r.Error);
    }

    [Fact]
    public async Task Rotate_grace_ichida_qayta_ishlatilsa_family_bekor_QILINMAYDI()
    {
        using var t = TestDb.Sqlite();
        var user = await SeedUserAsync(t.Context);
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(user.Id);

        // Birinchi rotatsiya — issued endi bekor (RevokedAt = hozir, grace ICHIDA).
        var first = await svc.RotateAsync(issued.RawToken);
        Assert.True(first.Ok);

        // O'sha (allaqachon rotatsiya qilingan) tokenni DARHOL qayta ishlatish — ko'p tab/parallel.
        var second = await svc.RotateAsync(issued.RawToken);

        Assert.True(second.Ok);                          // grace: legitim, yangi juftlik beriladi
        // first dan chiqqan token HALI FAOL bo'lishi kerak (family bekor qilinmagan).
        var firstHash = Sha(first.RefreshToken!);
        var firstRow = await t.Context.RefreshTokens.SingleAsync(x => x.TokenHash == firstHash);
        Assert.Null(firstRow.RevokedAt);
    }

    [Fact]
    public async Task Rotate_grace_dan_keyin_qayta_ishlatilsa_REUSE_butun_family_bekor()
    {
        using var t = TestDb.Sqlite();
        var user = await SeedUserAsync(t.Context);
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(user.Id);

        // Birinchi rotatsiya → issued bekor, yangi (active) token chiqdi.
        var first = await svc.RotateAsync(issued.RawToken);
        Assert.True(first.Ok);

        // issued.RevokedAt ni grace'dan OLDINGA suramiz (2 daqiqa oldin) — endi reuse hujumi.
        var issuedHash = Sha(issued.RawToken);
        var issuedRow = await t.Context.RefreshTokens.SingleAsync(x => x.TokenHash == issuedHash);
        issuedRow.RevokedAt = DateTime.UtcNow.AddMinutes(-2);
        await t.Context.SaveChangesAsync();

        var reuse = await svc.RotateAsync(issued.RawToken);

        Assert.False(reuse.Ok);
        Assert.Equal("reuse", reuse.Error);
        // BUTUN family bekor: first dan chiqqan faol token ham endi bekor bo'lishi kerak.
        var all = await t.Context.RefreshTokens
            .Where(x => x.FamilyId == issued.FamilyId).ToListAsync();
        Assert.All(all, x => Assert.NotNull(x.RevokedAt));
    }

    [Fact]
    public async Task Revoke_keyin_grace_dan_tashqarida_refresh_ishlamaydi()
    {
        using var t = TestDb.Sqlite();
        var user = await SeedUserAsync(t.Context);
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(user.Id);

        await svc.RevokeAsync(issued.RawToken);

        // Grace'dan tashqariga suramiz — logout qilingan token qayta refresh qila olmasin.
        var row = await t.Context.RefreshTokens.SingleAsync();
        row.RevokedAt = DateTime.UtcNow.AddMinutes(-2);
        await t.Context.SaveChangesAsync();

        var r = await svc.RotateAsync(issued.RawToken);

        Assert.False(r.Ok);
        Assert.Equal("reuse", r.Error);
    }

    [Fact]
    public async Task Rotate_bloklangan_oquvchi_401()
    {
        using var t = TestDb.Sqlite();
        // O'quvchi roli — LoginBlocked bo'lsa refresh rad etiladi (OnTokenValidated bilan bir xil).
        var u = new AppUser { FullName = "S", Role = Roles.Student, Email = "s@s.s" };
        t.Context.Users.Add(u);
        t.Context.Students.Add(new Student
        { UserId = u.Id, FullName = "S", LoginBlocked = true });
        await t.Context.SaveChangesAsync();
        var svc = Svc(t.Context);
        var issued = await svc.IssueAsync(u.Id);

        var r = await svc.RotateAsync(issued.RawToken);

        Assert.False(r.Ok);
        Assert.Equal("user_invalid", r.Error);
    }

    private static string Sha(string raw) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw)));
}
