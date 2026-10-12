using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Auth;

/// <summary>
/// The ONLY place that writes an account password. Each method also sets <see cref="AppUser.PendingPassword"/>,
/// which <see cref="StudentCredentialInterceptor"/> turns into the encrypted <see cref="StudentCredential"/> row in
/// the same <c>SaveChanges</c> (student accounts only — docs/SPEC-crm-upgrade.md §7 P6.1). ⚠️ Never write
/// <c>PasswordHash</c> directly: the stored password would silently go stale.
/// </summary>
public static class AccountExtensions
{
    /// <summary>
    /// Admin tomonidan parol o'rnatish/tiklash: hash + dastlabki ochiq parolni saqlaydi
    /// (superadmin ko'rishi/eksport uchun, birinchi login'gacha).
    /// <para><paramref name="source"/> — <see cref="PasswordSources"/>: <c>admin_set</c> (formadagi yangi parol,
    /// arxivdan qaytarishdagi parol), <c>reset</c> («Yangi parol» tugmalari), <c>create</c> (akkaunt yaratish).</para>
    /// </summary>
    public static void SetInitialPassword(this AppUser user, string plain, string source = PasswordSources.AdminSet)
    {
        user.PasswordHash = PasswordHasher.Hash(plain);
        user.InitialPassword = plain;
        user.PendingPassword = PendingPasswordChange.Set(plain, source);
    }

    /// <summary>Foydalanuvchi o'zi o'zgartirgan parol (yoki login qilingach): faqat hash, ochiq parol tozalanadi.
    /// O'quvchi akkauntida yangi parol seyfga yoziladi (<c>self_change</c>) — adminlar uni ko'ra oladi.</summary>
    public static void SetOwnPassword(this AppUser user, string plain)
    {
        user.PasswordHash = PasswordHasher.Hash(plain);
        user.InitialPassword = null;
        user.PendingPassword = PendingPasswordChange.Set(plain, PasswordSources.SelfChange);
    }

    /// <summary>Login bloklash (arxiv): parolni bo'shatadi va ko'rsatiladigan parolni o'chiradi
    /// (seyfdagi parol ham o'chiriladi).</summary>
    public static void BlockLogin(this AppUser user)
    {
        user.PasswordHash = "";
        user.InitialPassword = null;
        user.PendingPassword = PendingPasswordChange.Clear();
    }
}
