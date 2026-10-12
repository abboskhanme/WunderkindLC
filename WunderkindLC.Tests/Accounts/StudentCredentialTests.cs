using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Accounts;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using Xunit;
using Group = WunderkindLC.Domain.Group;

namespace WunderkindLC.Tests.Accounts;

/*
 * CRM upgrade Phase 6, unit U6a — permanently visible student passwords (docs/SPEC-crm-upgrade.md §7 P6.1/P6.4,
 * .claude/rules/auth-sessions.md §6).
 */

internal static class VaultKeys
{
    public static readonly string A = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    public static readonly string B = Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray());
    public static CredentialVault On() => new(A);
    public static CredentialVault Off() => new(null);
}

internal sealed class NoHttp : IHttpContextAccessor
{
    public HttpContext? HttpContext { get => null; set { } }
}

/* =============================================================================================== */

public class CredentialVaultTests
{
    [Theory]
    [InlineData("abc12345")]
    [InlineData("")]
    [InlineData("O'zbekcha parol — ўзбек 🔑")]
    public void Round_trip(string plain)
    {
        var vault = VaultKeys.On();
        var blob = vault.Protect(plain);
        Assert.Equal(CredentialVault.FormatVersion, blob[0]);
        Assert.Equal(plain, vault.Unprotect(blob));
        // The blob never contains the plain text.
        if (plain.Length > 0)
            Assert.DoesNotContain(plain, Encoding.UTF8.GetString(blob));
    }

    [Fact]
    public void Same_password_twice_gives_different_blobs__random_nonce()
    {
        var vault = VaultKeys.On();
        Assert.NotEqual(vault.Protect("parol123"), vault.Protect("parol123"));
    }

    [Fact]
    public void Wrong_key_corrupt_short_or_unknown_version__null_never_throws()
    {
        var blob = VaultKeys.On().Protect("parol123");
        Assert.Null(new CredentialVault(VaultKeys.B).Unprotect(blob));

        var tampered = (byte[])blob.Clone();
        tampered[^1] ^= 0xFF;
        Assert.Null(VaultKeys.On().Unprotect(tampered));

        var wrongVersion = (byte[])blob.Clone();
        wrongVersion[0] = 9;
        Assert.Null(VaultKeys.On().Unprotect(wrongVersion));

        Assert.Null(VaultKeys.On().Unprotect(new byte[5]));
        Assert.Null(VaultKeys.On().Unprotect(null));
        Assert.Null(VaultKeys.On().Unprotect(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]   // valid base64, 3 bytes
    public void Missing_or_invalid_key__not_configured_and_nothing_can_be_stored(string? key)
    {
        var vault = new CredentialVault(key);
        Assert.False(vault.Configured);
        Assert.False(CredentialVault.IsValidKey(key));
        Assert.Throws<InvalidOperationException>(() => vault.Protect("parol123"));
        Assert.Null(vault.Unprotect(VaultKeys.On().Protect("parol123")));
    }

    [Fact]
    public void Generated_key_is_valid()
    {
        var key = CredentialVault.GenerateKey();
        Assert.True(CredentialVault.IsValidKey(key));
        Assert.True(new CredentialVault(key).Configured);
    }
}

/* =============================================================================================== */

public class StudentCredentialsRulesTests
{
    private static PendingPasswordChange Set(string p = "parol123", string src = PasswordSources.Reset) =>
        PendingPasswordChange.Set(p, src);

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    [InlineData("parent")]   // SPEC §10 #10 — parents are NOT stored
    public void Non_student_accounts_are_never_stored(string role)
    {
        Assert.Equal(StudentCredentials.VaultAction.None, StudentCredentials.Decide(Set(), role, true));
        Assert.Equal(StudentCredentials.VaultAction.None, StudentCredentials.Decide(PendingPasswordChange.Clear(), role, true));
    }

    [Fact]
    public void Student_set_with_key__upsert__without_key__delete_stale_row()
    {
        Assert.Equal(StudentCredentials.VaultAction.Upsert, StudentCredentials.Decide(Set(), Roles.Student, true));
        Assert.Equal(StudentCredentials.VaultAction.Delete, StudentCredentials.Decide(Set(), Roles.Student, false));
        Assert.Equal(StudentCredentials.VaultAction.Delete, StudentCredentials.Decide(PendingPasswordChange.Clear(), Roles.Student, true));
        Assert.Equal(StudentCredentials.VaultAction.None, StudentCredentials.Decide(null, Roles.Student, true));
    }

    [Fact]
    public void Reveal__vault_row_first__then_initial__else_unknown()
    {
        var vault = VaultKeys.On();
        var user = new AppUser { Role = Roles.Student };
        var row = new StudentCredential { UserId = user.Id, PasswordCipher = vault.Protect("saqlangan1"), Source = PasswordSources.SelfChange, UpdatedAt = new DateTime(2026, 10, 1) };

        var r = StudentCredentials.Reveal(row, user, vault);
        Assert.Equal(("saqlangan1", true, PasswordSources.SelfChange), (r.Password, r.Known, r.Source));
        Assert.Equal(new DateTime(2026, 10, 1), r.UpdatedAt);

        // No row → still-unused initial password.
        user.InitialPassword = "boshlangich";
        r = StudentCredentials.Reveal(null, user, vault);
        Assert.Equal(("boshlangich", true, PasswordSources.Initial), (r.Password, r.Known, r.Source));

        // Neither → unknown (old account whose student already logged in before the vault).
        user.InitialPassword = null;
        r = StudentCredentials.Reveal(null, user, vault);
        Assert.False(r.Known);
        Assert.Null(r.Password);

        // Row from another key (key changed) + no initial → unknown, no exception.
        r = StudentCredentials.Reveal(row, user, new CredentialVault(VaultKeys.B));
        Assert.False(r.Known);

        // No account at all.
        Assert.False(StudentCredentials.Reveal(null, null, vault).Known);
    }

    [Fact]
    public void Reveal__a_stale_row_loses_to_the_current_initial_password()
    {
        // Possible only after a key outage: a reset happened while the vault was off.
        var vault = VaultKeys.On();
        var user = new AppUser { Role = Roles.Student, InitialPassword = "yangisi12" };
        var stale = new StudentCredential { UserId = user.Id, PasswordCipher = vault.Protect("eskisi12"), Source = PasswordSources.Create };
        var r = StudentCredentials.Reveal(stale, user, vault);
        Assert.Equal("yangisi12", r.Password);
        Assert.Equal(PasswordSources.Initial, r.Source);
    }

    [Fact]
    public void Export_cell_and_backfill_rule()
    {
        Assert.Equal("p1", StudentCredentials.ExportCell(new("p1", true, "reset", null), hasAccount: true));
        Assert.Equal("(saqlanmagan)", StudentCredentials.ExportCell(new(null, false, "", null), hasAccount: true));
        Assert.Equal("", StudentCredentials.ExportCell(new(null, false, "", null), hasAccount: false));

        Assert.True(StudentCredentials.NeedsBackfill(Roles.Student, "abc", hasRow: false));
        Assert.False(StudentCredentials.NeedsBackfill(Roles.Student, "abc", hasRow: true));
        Assert.False(StudentCredentials.NeedsBackfill(Roles.Student, null, hasRow: false));
        Assert.False(StudentCredentials.NeedsBackfill(Roles.Teacher, "abc", hasRow: false));
        Assert.False(StudentCredentials.NeedsBackfill("parent", "abc", hasRow: false));
    }

    [Fact]
    public void Pending_change_never_prints_or_serialises_the_password()
    {
        var p = PendingPasswordChange.Set("MaxfiyParol9", PasswordSources.Reset);
        Assert.DoesNotContain("MaxfiyParol9", p.ToString());

        var user = new AppUser { FullName = "Ali", Role = Roles.Student, PendingPassword = p };
        Assert.DoesNotContain("MaxfiyParol9", JsonSerializer.Serialize(user));
        Assert.DoesNotContain("PendingPassword", JsonSerializer.Serialize(user));
    }
}

/* =============================================================================================== */

public class StudentCredentialInterceptorTests
{
    private static TestDb Db(CredentialVault vault) => TestDb.Sqlite(new StudentCredentialInterceptor(vault));

    private static async Task<StudentCredential?> RowAsync(AppDbContext ctx, string userId) =>
        await ctx.StudentCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId);

    [Fact]
    public async Task Create_reset_self_change_and_block__each_captured_in_the_same_SaveChanges()
    {
        var vault = VaultKeys.On();
        using var db = Db(vault);
        var ctx = db.Context;

        // create (AccountFactory → SetInitialPassword(..., "create"))
        var user = AccountFactory.CreateAccountFor(ctx, Roles.Student, "Aliyev Vali", out var first);
        await ctx.SaveChangesAsync();
        var row = await RowAsync(ctx, user.Id);
        Assert.NotNull(row);
        Assert.Equal(PasswordSources.Create, row!.Source);
        Assert.Equal(first, vault.Unprotect(row.PasswordCipher));
        Assert.Null(user.PendingPassword);   // consumed

        // reset
        user.SetInitialPassword("reset1234", PasswordSources.Reset);
        await ctx.SaveChangesAsync();
        row = await RowAsync(ctx, user.Id);
        Assert.Equal(("reset1234", PasswordSources.Reset), (vault.Unprotect(row!.PasswordCipher), row.Source));

        // the student logs in (InitialPassword cleared) — the stored one survives
        user.InitialPassword = null;
        await ctx.SaveChangesAsync();
        Assert.Equal("reset1234", StudentCredentials.Reveal(await RowAsync(ctx, user.Id), user, vault).Password);

        // self change in the cabinet → admins see the NEW password (acceptance criterion 1)
        user.SetOwnPassword("ozimniki99");
        await ctx.SaveChangesAsync();
        row = await RowAsync(ctx, user.Id);
        Assert.Equal(("ozimniki99", PasswordSources.SelfChange), (vault.Unprotect(row!.PasswordCipher), row.Source));
        Assert.Null(user.InitialPassword);
        Assert.True(PasswordHasher.Verify("ozimniki99", user.PasswordHash));

        // blocked login (archive) → row deleted
        user.BlockLogin();
        await ctx.SaveChangesAsync();
        Assert.Null(await RowAsync(ctx, user.Id));
        Assert.Equal(1, await ctx.Users.CountAsync());
    }

    [Fact]
    public void Synchronous_SaveChanges_is_captured_too()
    {
        var vault = VaultKeys.On();
        using var db = Db(vault);
        var user = AccountFactory.CreateAccountFor(db.Context, Roles.Student, "Sync Test", out var pwd);
        db.Context.SaveChanges();
        var row = db.Context.StudentCredentials.AsNoTracking().Single(c => c.UserId == user.Id);
        Assert.Equal(pwd, vault.Unprotect(row.PasswordCipher));
    }

    [Fact]
    public async Task Create_then_admin_set_in_ONE_save__the_last_one_wins()
    {
        var vault = VaultKeys.On();
        using var db = Db(vault);
        var user = AccountFactory.CreateAccountFor(db.Context, Roles.Student, "Ikki Bosqich");
        user.SetInitialPassword("formadagi1");   // admin_set (default)
        await db.Context.SaveChangesAsync();
        var row = await RowAsync(db.Context, user.Id);
        Assert.Equal(("formadagi1", PasswordSources.AdminSet), (vault.Unprotect(row!.PasswordCipher), row.Source));
        Assert.Equal(1, await db.Context.StudentCredentials.CountAsync());
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    [InlineData("parent")]
    public async Task Non_student_accounts_are_ignored(string role)
    {
        using var db = Db(VaultKeys.On());
        var user = AccountFactory.CreateAccountFor(db.Context, role, "Boshqa Rol");
        await db.Context.SaveChangesAsync();
        user.SetOwnPassword("ozgardi123");
        await db.Context.SaveChangesAsync();
        Assert.Equal(0, await db.Context.StudentCredentials.CountAsync());
        Assert.Null(user.PendingPassword);
    }

    [Fact]
    public async Task Key_missing__nothing_stored__and_a_stale_row_is_removed_on_the_next_set()
    {
        // Phase 1: key present, row stored.
        using var db = Db(VaultKeys.On());
        var user = AccountFactory.CreateAccountFor(db.Context, Roles.Student, "Kalit Yoq");
        await db.Context.SaveChangesAsync();
        Assert.Equal(1, await db.Context.StudentCredentials.CountAsync());

        // Phase 2: the same database, the key is gone (another context with a "not configured" vault).
        await using var noKey = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Context.Database.GetDbConnection())
            .AddInterceptors(new StudentCredentialInterceptor(VaultKeys.Off())).Options);
        var u2 = await noKey.Users.FirstAsync(u => u.Id == user.Id);
        u2.SetInitialPassword("kalitsiz12", PasswordSources.Reset);
        await noKey.SaveChangesAsync();

        Assert.Equal(0, await noKey.StudentCredentials.CountAsync());          // never plain text, never stale
        Assert.Equal("kalitsiz12", u2.InitialPassword);                         // still visible until first login

        // A brand-new account without a key → no row either.
        AccountFactory.CreateAccountFor(noKey, Roles.Student, "Yangi Kalitsiz");
        await noKey.SaveChangesAsync();
        Assert.Equal(0, await noKey.StudentCredentials.CountAsync());
    }

    [Fact]
    public async Task Deleting_a_student_account_deletes_its_row()
    {
        using var db = Db(VaultKeys.On());
        var user = AccountFactory.CreateAccountFor(db.Context, Roles.Student, "Ochiriladi");
        await db.Context.SaveChangesAsync();
        db.Context.Users.Remove(user);
        await db.Context.SaveChangesAsync();
        Assert.Equal(0, await db.Context.StudentCredentials.CountAsync());
    }

    [Fact]
    public async Task Without_the_interceptor_nothing_is_written__the_vault_is_opt_in_per_context()
    {
        using var db = TestDb.Sqlite();
        AccountFactory.CreateAccountFor(db.Context, Roles.Student, "Interceptorsiz");
        await db.Context.SaveChangesAsync();
        Assert.Equal(0, await db.Context.StudentCredentials.CountAsync());
    }

    [Fact]
    public async Task Student_purge_removes_the_row_with_the_account()
    {
        var vault = VaultKeys.On();
        using var db = Db(vault);
        var ctx = db.Context;
        var user = AccountFactory.CreateAccountFor(ctx, Roles.Student, "Dublikat Ali");
        var s = new Student { FullName = "Dublikat Ali", UserId = user.Id };
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();
        Assert.Equal(1, await ctx.StudentCredentials.CountAsync());

        await StudentPurge.PurgeAsync(ctx, s.Id);
        await ctx.SaveChangesAsync();
        Assert.Equal(0, await ctx.StudentCredentials.CountAsync());
        Assert.False(await ctx.Users.AnyAsync(u => u.Id == user.Id));
    }
}

/* =============================================================================================== */

public class StudentCredentialBackfillTests
{
    [Fact]
    public async Task Copies_unused_initial_passwords_of_students_once()
    {
        var vault = VaultKeys.On();
        using var db = TestDb.Sqlite();   // seed WITHOUT the interceptor (= accounts made before the vault)
        var ctx = db.Context;
        var a = new AppUser { FullName = "A", Email = "a", Role = Roles.Student, InitialPassword = "aaaa1111" };
        var b = new AppUser { FullName = "B", Email = "b", Role = Roles.Student, InitialPassword = null };   // already logged in
        var t = new AppUser { FullName = "T", Email = "t", Role = Roles.Teacher, InitialPassword = "tttt1111" };
        var p = new AppUser { FullName = "P", Email = "p", Role = "parent", InitialPassword = "pppp1111" };
        var c = new AppUser { FullName = "C", Email = "c", Role = Roles.Student, InitialPassword = "cccc1111" };
        ctx.Users.AddRange(a, b, t, p, c);
        ctx.StudentCredentials.Add(new StudentCredential { UserId = c.Id, PasswordCipher = vault.Protect("cccc-own"), Source = PasswordSources.SelfChange });
        await ctx.SaveChangesAsync();

        var (ran, stored) = await StudentCredentialBackfill.RunOnceAsync(ctx, vault);
        Assert.True(ran);
        Assert.Equal(1, stored);
        var rowA = await ctx.StudentCredentials.AsNoTracking().SingleAsync(x => x.UserId == a.Id);
        Assert.Equal(("aaaa1111", PasswordSources.Backfill), (vault.Unprotect(rowA.PasswordCipher), rowA.Source));
        // An existing row is never overwritten; teachers/parents/logged-in students get nothing.
        Assert.Equal("cccc-own", vault.Unprotect((await ctx.StudentCredentials.AsNoTracking().SingleAsync(x => x.UserId == c.Id)).PasswordCipher));
        Assert.Equal(2, await ctx.StudentCredentials.CountAsync());
        Assert.True(await ctx.SeedMarkers.AnyAsync(m => m.Key == StudentCredentialBackfill.MarkerKey));

        // Second start — no-op.
        ctx.Users.Add(new AppUser { FullName = "D", Email = "d", Role = Roles.Student, InitialPassword = "dddd1111" });
        await ctx.SaveChangesAsync();
        Assert.Equal((false, 0), await StudentCredentialBackfill.RunOnceAsync(ctx, vault));
        Assert.Equal(2, await ctx.StudentCredentials.CountAsync());
    }

    [Fact]
    public async Task Without_a_key__nothing_and_no_marker__retried_next_start()
    {
        using var db = TestDb.Sqlite();
        db.Context.Users.Add(new AppUser { FullName = "A", Email = "a", Role = Roles.Student, InitialPassword = "aaaa1111" });
        await db.Context.SaveChangesAsync();

        Assert.Equal((false, 0), await StudentCredentialBackfill.RunOnceAsync(db.Context, VaultKeys.Off()));
        Assert.Equal(0, await db.Context.StudentCredentials.CountAsync());
        Assert.False(await db.Context.SeedMarkers.AnyAsync());

        Assert.Equal((true, 1), await StudentCredentialBackfill.RunOnceAsync(db.Context, VaultKeys.On()));
    }
}

/* =============================================================================================== */

public class GroupCredentialsTests
{
    private sealed record World(Group G, Student Known, Student Unknown, Student NoAccount, Student Archived, Student Left,
        AppUser KnownUser, AppUser UnknownUser);

    /// <summary>A group with: a member whose password is stored, one whose is not (old account), one without an
    /// account, an archived member and a member who left — plus a member of another group.</summary>
    private static async Task<World> SeedAsync(AppDbContext ctx)
    {
        var g = new Group { Name = "Ingliz tili 5-A" };
        var other = new Group { Name = "Boshqa guruh" };
        var knownUser = AccountFactory.CreateAccountFor(ctx, Roles.Student, "Aliyev Ali");      // stored (create)
        var unknownUser = new AppUser { FullName = "Botirov Bobur", Email = "botirovbobur", Role = Roles.Student, PasswordHash = PasswordHasher.Hash("eski-parol") };
        var known = new Student { FullName = "Aliyev Ali", UserId = knownUser.Id };
        var unknown = new Student { FullName = "Botirov Bobur", UserId = unknownUser.Id };
        var noAccount = new Student { FullName = "Valiyev Vali" };
        var archived = new Student { FullName = "Arxivdagi Odam", IsArchived = true };
        var left = new Student { FullName = "Ketgan Odam" };
        var stranger = new Student { FullName = "Begona Odam" };
        ctx.AddRange(g, other, unknownUser, known, unknown, noAccount, archived, left, stranger);
        ctx.StudentGroups.AddRange(
            new StudentGroup { StudentId = known.Id, GroupId = g.Id, Status = "active" },
            new StudentGroup { StudentId = unknown.Id, GroupId = g.Id, Status = "frozen", YearFreeze = true },
            new StudentGroup { StudentId = noAccount.Id, GroupId = g.Id, Status = "trial" },
            new StudentGroup { StudentId = archived.Id, GroupId = g.Id, Status = "active" },
            new StudentGroup { StudentId = left.Id, GroupId = g.Id, Status = "active", IsActive = false, LeftAt = "2026-09-01" },
            new StudentGroup { StudentId = stranger.Id, GroupId = other.Id, Status = "active" });
        ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = known.Id, ChatId = 1001 },
            new TelegramRegistration { StudentId = known.Id, ChatId = 1002 });
        ctx.RefreshTokens.Add(new RefreshToken { UserId = unknownUser.Id, TokenHash = "h-unknown", ExpiresAt = DateTime.UtcNow.AddDays(10) });
        await ctx.SaveChangesAsync();
        return new World(g, known, unknown, noAccount, archived, left, knownUser, unknownUser);
    }

    [Fact]
    public async Task Load_lists_live_non_archived_members_and_creates_nothing()
    {
        var vault = VaultKeys.On();
        using var db = TestDb.Sqlite(new StudentCredentialInterceptor(vault));
        var w = await SeedAsync(db.Context);
        var usersBefore = await db.Context.Users.CountAsync();

        var view = await GroupCredentials.LoadAsync(db.Context, vault, w.G.Id);

        Assert.NotNull(view);
        Assert.True(view!.VaultConfigured);
        Assert.Equal(new[] { "Aliyev Ali", "Botirov Bobur", "Valiyev Vali" }, view.Rows.Select(r => r.FullName));
        var k = view.Rows[0];
        Assert.True(k.Known);
        Assert.Equal(w.KnownUser.Email, k.Login);
        Assert.Equal(w.KnownUser.InitialPassword, k.Password);
        Assert.Equal(2, k.TelegramChats);
        var u = view.Rows[1];
        Assert.False(u.Known);
        Assert.Null(u.Password);
        Assert.True(u.HasAccount);
        var n = view.Rows[2];
        Assert.False(n.HasAccount);
        Assert.Equal("", n.Login);
        Assert.Equal(usersBefore, await db.Context.Users.CountAsync());
        Assert.Null(await GroupCredentials.LoadAsync(db.Context, vault, "yoq-guruh"));
    }

    [Fact]
    public async Task Ensure_creates_missing_accounts_regenerates_ONLY_unknown_and_ends_their_sessions()
    {
        var vault = VaultKeys.On();
        using var db = TestDb.Sqlite(new StudentCredentialInterceptor(vault));
        var ctx = db.Context;
        var w = await SeedAsync(ctx);
        var knownHash = w.KnownUser.PasswordHash;
        var knownPwd = w.KnownUser.InitialPassword;

        var result = await GroupCredentials.EnsureAsync(ctx, vault, new AuditService(ctx, new NoHttp()), w.G.Id);

        Assert.Equal(new GroupCredentials.EnsureResult(1, 1), result);
        // Known password untouched.
        var knownNow = await ctx.Users.AsNoTracking().SingleAsync(x => x.Id == w.KnownUser.Id);
        Assert.Equal(knownHash, knownNow.PasswordHash);
        // Unknown → new password, stored in the vault with source "reset"; the old one no longer works.
        var unknownNow = await ctx.Users.AsNoTracking().SingleAsync(x => x.Id == w.UnknownUser.Id);
        Assert.False(PasswordHasher.Verify("eski-parol", unknownNow.PasswordHash));
        var row = await ctx.StudentCredentials.AsNoTracking().SingleAsync(x => x.UserId == w.UnknownUser.Id);
        Assert.Equal(PasswordSources.Reset, row.Source);
        Assert.True(PasswordHasher.Verify(vault.Unprotect(row.PasswordCipher)!, unknownNow.PasswordHash));
        // Sessions of the regenerated account ended.
        Assert.NotNull((await ctx.RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == w.UnknownUser.Id)).RevokedAt);
        // Missing account created and linked.
        var noAcc = await ctx.Students.AsNoTracking().SingleAsync(s => s.Id == w.NoAccount.Id);
        Assert.NotNull(noAcc.UserId);
        // Archived / left members untouched.
        Assert.Null((await ctx.Students.AsNoTracking().SingleAsync(s => s.Id == w.Archived.Id)).UserId);
        Assert.Null((await ctx.Students.AsNoTracking().SingleAsync(s => s.Id == w.Left.Id)).UserId);

        // Everybody is known now; the audit never holds a password.
        var view = await GroupCredentials.LoadAsync(ctx, vault, w.G.Id);
        Assert.All(view!.Rows, r => Assert.True(r.Known));
        var audits = await ctx.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Contains(audits, a => a.EntityType == "Group" && a.EntityId == w.G.Id && a.Summary.Contains("1 ta yangi akkaunt, 1 ta yangi parol"));
        Assert.Contains(audits, a => a.EntityType == "StudentDiscount" && a.Summary.StartsWith("O'quvchi paroli qayta yaratildi"));
        foreach (var r in view.Rows)
            Assert.DoesNotContain(audits, a => a.Summary.Contains(r.Password!) || (a.After ?? "").Contains(r.Password!));
        Assert.Equal(knownPwd, view.Rows.Single(r => r.StudentId == w.Known.Id).Password);

        // Second click — nothing left to do, nothing written.
        var auditCount = audits.Count;
        Assert.Equal(new GroupCredentials.EnsureResult(0, 0), await GroupCredentials.EnsureAsync(ctx, vault, new AuditService(ctx, new NoHttp()), w.G.Id));
        Assert.Equal(auditCount, await ctx.AuditLogs.CountAsync());
    }

    [Fact]
    public void Excel_rows_and_file_name()
    {
        var rows = new[]
        {
            new GroupCredentialRow("1", "Aliyev Ali", "active", false, true, "aliyevali", "p1", true, "create", 2),
            new GroupCredentialRow("2", "Botirov Bobur", "frozen", true, true, "botirovbobur", null, false, "", 0),
            new GroupCredentialRow("3", "Valiyev Vali", "trial", false, false, "", null, false, "", 0),
        };
        var sheet = GroupCredentialsSheet.Rows(rows).ToList();
        Assert.Equal(new[] { "№", "F.I.SH.", "Login", "Parol", "Telegram", "Holat" }, GroupCredentialsSheet.Headers);
        Assert.Equal(new[] { "1", "Aliyev Ali", "aliyevali", "p1", "Ulangan (2)", "Aktiv" }, sheet[0]);
        Assert.Equal(new[] { "2", "Botirov Bobur", "botirovbobur", "(saqlanmagan)", "Ulanmagan", "Aktiv muzlatilgan" }, sheet[1]);
        Assert.Equal(new[] { "3", "Valiyev Vali", "(akkaunt yo'q)", "", "Ulanmagan", "Sinov" }, sheet[2]);

        var day = new DateTime(2026, 10, 12);
        Assert.Equal("login_parollar_Ingliz_tili_5-A_2026-10-12.xlsx", GroupCredentialsSheet.FileName("Ingliz tili 5-A", day));
        Assert.Equal("login_parollar_guruh_2026-10-12.xlsx", GroupCredentialsSheet.FileName("  /..\\ ", day));
        Assert.DoesNotContain("/", GroupCredentialsSheet.FileName("a/b\\c:d", day));
    }
}

/* =============================================================================================== */

/// <summary>Where the password must NEVER go: JSON backup, MCP, teacher portal, logs/audit (source checks).</summary>
public class StudentCredentialLeakTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo root (WunderkindLC.slnx) not found");
            return dir!.FullName;
        }
    }

    private static string Controller(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file));

    [Fact]
    public async Task Json_backup_has_no_credentials_table_and_no_cipher()
    {
        var vault = VaultKeys.On();
        using var db = TestDb.Sqlite(new StudentCredentialInterceptor(vault));
        var user = AccountFactory.CreateAccountFor(db.Context, Roles.Student, "Zaxira Test", out var pwd);
        await db.Context.SaveChangesAsync();
        var cipher = (await db.Context.StudentCredentials.AsNoTracking().SingleAsync()).PasswordCipher;

        var (bytes, _, _) = await BackupService.BuildJsonAsync(db.Context);
        var json = Encoding.UTF8.GetString(bytes);

        Assert.Contains("studentCredentials", BackupService.ExcludedTables);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("studentCredentials", out _));
        Assert.DoesNotContain(Convert.ToBase64String(cipher), json);
        Assert.DoesNotContain(pwd, json);
        Assert.DoesNotContain(user.PasswordHash, json);
    }

    [Fact]
    public void Mcp_read_only_role_never_gets_the_table_or_the_column()
    {
        Assert.True(McpReadOnlyGrants.IsSensitiveColumn(nameof(StudentCredential.PasswordCipher)));
        Assert.DoesNotContain(typeof(StudentCredential), McpToolRegistry.ReadEntities);
    }

    [Fact]
    public void Teacher_portal_never_reads_the_vault()
    {
        var dir = Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers");
        foreach (var file in Directory.GetFiles(dir, "TeacherPortalController*.cs"))
        {
            var src = File.ReadAllText(file);
            Assert.DoesNotContain("StudentCredential", src);
            Assert.DoesNotContain("CredentialVault", src);
        }
    }

    [Fact]
    public void Group_credentials_controller__gate_first_in_every_action__no_store__audit_without_password()
    {
        var src = Controller("GroupCredentialsController.cs");
        var header = src[..Regex.Match(src, @"public (partial )?class GroupCredentialsController\b").Index];
        Assert.Contains("[AdminPerm(\"students.list\", ReadRequiresPerm = true)]", header);
        Assert.Contains("[Route(\"api/admin/classes/{groupId}/credentials\")]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.Contains("AdminPermAttribute.HasFullAccess(User, \"students.list\")", src);
        Assert.Contains("Login/parolni ko'rish uchun «O'quvchilar» bo'limiga to'liq ruxsat kerak", src);

        foreach (var sig in new[] { "public async Task<IActionResult> Get(", "public async Task<IActionResult> Ensure(", "public async Task<IActionResult> Export(" })
        {
            var i = src.IndexOf(sig, StringComparison.Ordinal);
            Assert.True(i > 0, sig);
            var body = src[i..src.IndexOf("\n    }", i, StringComparison.Ordinal)];
            var gate = body.IndexOf("if (!MaySee) return Forbidden();", StringComparison.Ordinal);
            Assert.True(gate > 0, $"{sig} has no gate");
            Assert.True(gate < body.IndexOf("await ", StringComparison.Ordinal), $"{sig}: the gate must come first");
            Assert.Contains("NoStore();", body);
        }
        // The export audit row names the group and the count — never a password column.
        Assert.Contains("$\"Login/parollar eksport qilindi: {view.GroupName} ({view.Rows.Count} ta o'quvchi)\"", src);
    }

    [Fact]
    public void Student_credentials_and_exports_use_the_vault_the_single_gate_and_no_store()
    {
        var src = Controller("StudentsController.cs");
        string Body(string sig)
        {
            var i = src.IndexOf(sig, StringComparison.Ordinal);
            Assert.True(i > 0, sig);
            return src[i..src.IndexOf("\n    }", i, StringComparison.Ordinal)];
        }

        var creds = Body("public async Task<ActionResult<CredentialsDto>> Credentials(");
        Assert.True(creds.IndexOf("HasFullAccess(User, \"students.list\")", StringComparison.Ordinal) > 0);
        Assert.Contains("StudentCredentials.Reveal(", creds);
        Assert.Contains("NoStore();", creds);

        var export = Body("public async Task<IActionResult> Export()");
        Assert.Contains("RevealManyAsync(", export);
        Assert.DoesNotContain("InitialPassword", export);

        var selected = Body("public async Task<IActionResult> ExportSelected(");
        Assert.DoesNotContain("InitialPassword", selected);
    }

    [Fact]
    public void Bulk_exports_show_passwords_only_to_superadmin_and_audit_without_the_password()
    {
        // M2 (2026-10-12): a centre-wide password dump is SUPERADMIN only (hard role — HasFullAccess/AdminPerm let a
        // plain admin through). Per-student reveal and per-group export stay at HasFullAccess (auth-sessions.md §3).
        var src = Controller("StudentsController.cs");
        var creds = Controller("StudentsController.Credentials.cs");
        Assert.Contains("private bool BulkExportShowsPasswords() => User.IsInRole(Roles.SuperAdmin);", creds);

        string Body(string sig)
        {
            var i = src.IndexOf(sig, StringComparison.Ordinal);
            Assert.True(i > 0, sig);
            return src[i..src.IndexOf("\n    }", i, StringComparison.Ordinal)];
        }
        foreach (var (sig, summary) in new[]
                 {
                     ("public async Task<IActionResult> Export()", "ExportAllAuditSummary("),
                     ("public async Task<IActionResult> ExportSelected(", "ExportSelectedAuditSummary("),
                 })
        {
            var body = Body(sig);
            Assert.Contains("var showPasswords = BulkExportShowsPasswords();", body);
            // The «Parol» column and the reveal exist only under showPasswords.
            Assert.Contains("if (showPasswords) headers.Add(\"Parol\");", body);
            Assert.Contains("if (showPasswords) row.Add(PasswordCell(u, revealed));", body);
            // M1: the export WITH passwords is audited — a fact, never a password value.
            var audit = body.IndexOf("audit.Record(AuditService.EntityStudent, ExportAuditEntityId", StringComparison.Ordinal);
            Assert.True(audit > 0, $"{sig}: no audit row");
            Assert.True(audit > body.IndexOf("if (showPasswords)\n", StringComparison.Ordinal), $"{sig}: audit outside the password branch");
            Assert.Contains(summary, body);
            Assert.DoesNotMatch(new Regex(@"audit\.Record\([^;]*(revealed|Password\b|PasswordCell)"), body);
        }
        // The per-student reveal keeps the single HasFullAccess gate.
        Assert.Contains("HasFullAccess(User, \"students.list\")", Body("public async Task<ActionResult<CredentialsDto>> Credentials("));
        // Audit texts carry only the count.
        Assert.Contains("$\"O'quvchilar login/parollari eksport qilindi: barcha faol o'quvchilar ({students} ta)\"", creds);
        Assert.Contains("$\"Tanlangan o'quvchilar login/parollari eksport qilindi ({students} ta o'quvchi)\"", creds);
    }

    [Fact]
    public void No_direct_password_hash_writes_outside_AccountExtensions()
    {
        // The interceptor sees only what AccountExtensions marks — a direct hash write would bypass the vault.
        var roots = new[] { "WunderkindLC.Server", "WunderkindLC.Application", "WunderkindLC.Infrastructure" };
        var allowed = new[] { "AccountExtensions.cs", "Program.cs" };   // Program.cs: the very first superadmin seed
        foreach (var root in roots)
            foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, root), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (allowed.Contains(Path.GetFileName(file))) continue;
                var src = File.ReadAllText(file);
                Assert.False(Regex.IsMatch(src, @"\b(user|u|account|owner)\.PasswordHash\s*=[^=]"),
                    $"{file}: write the password through AccountExtensions (SetInitialPassword/SetOwnPassword/BlockLogin)");
            }
    }
}
