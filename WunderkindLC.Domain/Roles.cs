namespace WunderkindLC.Domain;

/// <summary>
/// Tizim rollari (AppUser.Role qiymatlari). Yangi rol qo'shilsa shu joyga qo'shiladi.
///
/// <para>
/// <see cref="SuperAdmin"/> — tizim egasi. Admin'ga teng huquqlar ortida QO'SHIMCHA imtiyozlar:
/// guruhlash kabi o'quv yili boshida bloklanadigan amallarni istalgan vaqtda o'zgartira oladi
/// (kelajakda boshqa "lock"lar ham shu rolga override bo'ladi).
/// </para>
/// <para>
/// <see cref="Admin"/> — oddiy administrator. Hamma admin endpoint'lardan foydalanadi, lekin
/// muzlatilgan ma'lumotlarni (masalan, o'quv yili boshlangach guruhlarni) o'zgartira olmaydi.
/// </para>
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "superadmin";
    public const string Admin = "admin";
    public const string Teacher = "teacher";
    public const string Student = "student";
    // ESLATMA: "support" alohida rol YO'Q — support o'qituvchi role="teacher" + Teacher.IsSupport bayrog'i
    // bilan ifodalanadi; "Support" sahifasi teacher portalida IsSupport bo'yicha ko'rinadi.
    /// <summary>O'qituvchi bo'lmagan xodim (kassir, administrator, ...). Admin paneliga
    /// kiradi, lekin faqat <see cref="AppUser.Permissions"/> dagi bo'limlarni ko'radi.</summary>
    public const string Staff = "staff";

    /// <summary>Platforma egasi — tizim boshlig'i (yagona). Barcha modullarga to'liq kirish,
    /// foydalanuvchilarni o'chirish, ma'lumotlarni tozalash, tizim sozlamalari. Bitta markaz uchun
    /// (multi-tenant/Control Plane YO'Q).</summary>
    public const string PlatformOwner = "platformowner";

    /// <summary>CTI (Local Call) Android agent-ilovasi — xodim telefonidagi qo'ng'iroq
    /// yozuvchi ilova. FAQAT mobil API'ga (<c>api/mobile</c>) va WebSocket'ga kiradi; admin
    /// paneliga kira olmaydi. Token <see cref="AppUser"/>siz — CtiAgent bo'yicha beriladi.</summary>
    public const string CtiAgent = "ctiagent";

    /// <summary>
    /// Admin endpoint'larida ishlatish uchun: ikkala rol ham ruxsat etiladi.
    /// <c>[Authorize(Roles = Roles.AdminOrSuper)]</c> ko'rinishida foydalaniladi.
    /// </summary>
    public const string AdminOrSuper = Admin + "," + SuperAdmin;
}
