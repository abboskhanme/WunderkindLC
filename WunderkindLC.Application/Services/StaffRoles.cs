using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// XODIM ROLLARI (Boshqaruv → Rollar). Rol = <see cref="StaffRoleTemplate"/>: nom + ruxsatlar ro'yxati.
///
/// <para><b>JONLI BOG'LANISH:</b> xodim (<see cref="AppUser.RoleTemplateId"/>) ruxsatlarini roldan oladi.
/// Rolning ruxsatlari o'zgarsa, shu roldagi HAMMA xodimning <see cref="AppUser.Permissions"/> i qayta
/// yoziladi (<see cref="SyncMembersAsync"/>). Ruxsat tekshiruvi (token, <c>PermissionRules</c>) avvalgidek
/// faqat <see cref="AppUser.Permissions"/> ga qaraydi — ya'ni avtorizatsiya qatlami O'ZGARMADI.</para>
///
/// <para>Rolsiz (<c>RoleTemplateId = null</c>) xodim — "individual ruxsatlar": eski xodimlar shunday
/// qoladi (backfill YO'Q), ruxsatlari taxmin bilan o'zgartirilmaydi.</para>
/// </summary>
public static class StaffRoles
{
    /// <summary>Rol nomi uzunligi chegarasi.</summary>
    public const int MaxNameLength = 80;

    /// <summary>
    /// O'qituvchi — TIZIM roli (jadvalda emas). Uning ruxsatlari o'qituvchi portalining o'z kalitlari
    /// (<see cref="TeacherPermissions"/>), admin bo'limlari emas — Rollar'da tahrirlanmaydi.
    /// </summary>
    public const string TeacherRoleId = "teacher";

    /// <summary>Ruxsat kalitlarini tozalaydi: bo'sh/dublikat tashlanadi, bo'shliqlar kesiladi, tartiblanadi.</summary>
    public static List<string> Normalize(IEnumerable<string?>? perms) =>
        (perms ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Nomdan barqaror kod ("Kassir 2" → "kassir_2"); bo'sh bo'lsa tasodifiy.</summary>
    public static string CodeFrom(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray();
        var code = new string(chars).Trim('_');
        while (code.Contains("__")) code = code.Replace("__", "_");
        return code.Length == 0 ? "role_" + Guid.NewGuid().ToString("N")[..8] : code;
    }

    /// <summary>
    /// Rolning ruxsatlarini shu roldagi BARCHA xodimga yozadi. Qaytaradi: nechta xodim yangilandi.
    /// ⚠️ Faqat <c>role = staff</c>: superadmin/admin qilingan akkaunt rol bog'lanishini saqlasa ham
    /// (orqaga tushirilganda qaytsin), unga ruxsat yozish ma'nosiz — ular hamma narsani ko'radi.
    /// </summary>
    public static async Task<int> SyncMembersAsync(IAppDbContext db, StaffRoleTemplate role)
    {
        var members = await db.Users
            .Where(u => u.RoleTemplateId == role.Id && u.Role == Roles.Staff)
            .ToListAsync();
        foreach (var u in members)
            u.Permissions = role.DefaultPermissions.ToList();
        return members.Count;
    }

    /// <summary>Xodimni rolga biriktiradi (ruxsatlari darhol roldan). <paramref name="role"/> = null — individual.</summary>
    public static void Assign(AppUser user, StaffRoleTemplate? role)
    {
        user.RoleTemplateId = role?.Id;
        if (role is not null) user.Permissions = role.DefaultPermissions.ToList();
    }

    /// <summary>Rolda nechta XODIM bor — faqat <c>role = staff</c> (superadmin/admin qilinganlar
    /// ruxsatni roldan olmaydi; sanoqqa kirsa rolni o'chirib bo'lmay qolardi, sonlar ham aldardi).</summary>
    public static Task<int> MemberCountAsync(IAppDbContext db, string roleId) =>
        db.Users.CountAsync(u => u.RoleTemplateId == roleId && u.Role == Roles.Staff);
}
