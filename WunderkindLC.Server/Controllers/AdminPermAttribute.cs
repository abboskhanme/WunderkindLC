using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Admin-bo'lim controlleri uchun ruxsat darvozasi (xodim/staff rollari uchun).
/// <list type="bullet">
///   <item><b>admin / superadmin</b> — to'liq kirish (ruxsat tekshirilmaydi).</item>
///   <item><b>staff</b> — <b>O'QISH</b> (GET/HEAD) har doim ochiq: bir bo'lim sahifasi boshqa
///     bo'lim ma'lumotini o'qishi (masalan Moliya → o'quvchilar ro'yxati) buzilmasligi uchun.
///     "Ko'rish" (bo'lim ko'rinishi) FRONTEND'da (nav + RequirePerm) boshqariladi.
///     <b>YOZISH</b> esa AMAL bo'yicha ajratiladi: POST→<c>create</c> (qo'shish), PUT/PATCH→<c>edit</c>
///     (tahrir), DELETE→<c>delete</c> (o'chirish). Ruxsat tokeni ikki xil bo'ladi: yalang
///     <c>"section"</c> = TO'LIQ (barcha amallar, eski/backward-compat) yoki <c>"section:action"</c>
///     = faqat shu amal.</item>
///   <item>Boshqa rollar (teacher/student/parent) — taqiqlanadi.</item>
/// </list>
/// <para><b>SAHIFA (page) kalitlari:</b> darvozaga bo'lim kaliti (<c>"students"</c>) ham, bitta
/// sahifa kaliti (<c>"students.turnstile"</c>) ham qo'yilishi mumkin. Bo'lim ruxsati o'z
/// sahifalarini avtomatik qamrab oladi (PASTGA meros) — ya'ni eski xodim ruxsatlari o'zgarmaydi.
/// Aksincha emas: bitta sahifaga ruxsati bor xodim bo'limning boshqa endpointlariga YOZA olmaydi.
/// Batafsil: <see cref="PermissionRules"/> va <c>.claude/rules/permissions.md</c>.</para>
///
/// Ruxsat claim'lari tokenga yozilmaydi — ular HAR so'rovda DB'dan (Program.cs OnTokenValidated)
/// yuklanadi. Shuning uchun superadmin xodim ruxsatini o'zgartirsa, xodim qayta login qilmasdan
/// darrov yangi ruxsat bilan ishlaydi.
/// </summary>
/// <remarks>Odatda CONTROLLER ga qo'yiladi. Amal (metod) darajasida ham ishlaydi — bir controller
/// ichida ochiq/o'quvchi/admin marshrutlari aralash bo'lganda kerak bo'ladi
/// (<c>CertificatesController</c>).</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AdminPermAttribute(params string[] perms) : Attribute, IAuthorizationFilter
{
    /// <summary>Staff ruxsatlari shu turdagi claim sifatida principal'ga qo'shiladi.</summary>
    public const string ClaimType = "perm";

    /// <summary>
    /// Qabul qilinadigan ruxsat kalitlari — bo'lim (<c>"students"</c>) yoki SAHIFA
    /// (<c>"students.turnstile"</c>). Bir nechtasi berilsa — BIRORTASI yetadi (<c>permAny</c>).
    ///
    /// <para>Bir nechtasi kerak bo'ladigan joy: bitta endpoint ikki sahifadan ishlatiladi
    /// (masalan o'quvchi izohlari — profil sahifasida ham, "Izohlarga javoblar" sahifasida ham).</para>
    /// </summary>
    private readonly string[] _perms = perms.Length > 0 ? perms : [""];

    /// <summary>
    /// <b>O'QISH ham shu bo'lim ruxsatini talab qiladimi.</b> Standart holat <c>false</c> — GET
    /// hamma xodimga ochiq (yuqoridagi izohga qarang: bo'limlararo o'qish buzilmasin).
    ///
    /// <para>Lekin ba'zi bo'limlar javobida <c>/uploads/...</c> HUJJAT manzillari qaytadi
    /// (shartnoma skanlari, nomzod CV'lari, o'quvchining ovoz yozuvlari). <c>/uploads</c> esa
    /// autentifikatsiyasiz beriladi — ya'ni bunday manzilni bir marta olgan odam faylni
    /// <b>abadiy</b>, hatto ishdan bo'shatilgandan keyin ham ola oladi. Shu sabab bunday
    /// bo'limlarda o'qish ham darvozalanadi: xodimda shu bo'lim ruxsati (biror amali) bo'lishi shart.</para>
    ///
    /// <para>Bu bayroq bo'limlararo o'qish HAQIQATAN kerak bo'lgan joylarda (masalan Moliya →
    /// o'quvchilar ro'yxati) <b>YOQILMAYDI</b> — u yerdagi nozik maydonlar javobning o'zida
    /// tozalanadi.</para>
    /// </summary>
    public bool ReadRequiresPerm { get; init; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // METOD darajasidagi atribut SINF darajasidagisini bekor qiladi ("eng yaqini yutadi").
        // ASP.NET Core ikkala filtrni ham ishga tushiradi, ya'ni bu tekshiruvsiz metodga
        // torroq/boshqa kalit qo'yib bo'lmasdi: sinfdagi keng kalit baribir talab qilinardi.
        if (IsOverriddenAtMethod(context)) return;

        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }

        // To'liq huquqli rollar — cheklovsiz.
        if (user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin)) return;

        // Faqat xodim (staff) shu darvozadan o'tishi mumkin; qolganlari — rad.
        if (!user.IsInRole(Roles.Staff)) { context.Result = new ForbidResult(); return; }

        // O'qish odatda ochiq (bo'limlararo bog'liqliklar uchun); "ko'rish" frontend'da boshqariladi.
        // Nozik hujjat qaytaradigan bo'limlarda esa (ReadRequiresPerm) o'qish ham darvozalanadi.
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            if (!ReadRequiresPerm) return;
            if (!_perms.Any(p => HasSectionAccess(user, p))) context.Result = new ForbidResult();
            return;
        }

        // Yozish amali: yalang "section" (TO'LIQ) YOKI aniq "section:amal".
        // Qoidaning O'ZI `PermissionRules` da (Application) — u yerda testlanadi.
        var claims = PermValues(user).ToList();
        if (!_perms.Any(p => PermissionRules.CanWrite(claims, p, method))) context.Result = new ForbidResult();
    }

    /// <summary>
    /// Shu atribut SINF darajasida turibdi-yu, AMAL (metod) ustida ham <see cref="AdminPermAttribute"/>
    /// bormi — bor bo'lsa sinfdagisi o'tkazib yuboriladi (metoddagisi yagona darvoza bo'ladi).
    ///
    /// <para>⚠️ Solishtiruv QIYMAT bo'yicha, nusxa (reference) bo'yicha EMAS: .NET
    /// <c>GetCustomAttributes</c> ning HAR chaqiruvida atributning YANGI nusxasini yasaydi, ya'ni
    /// <c>ReferenceEquals</c> hech qachon mos kelmasdi va metoddagi atribut ham o'zini "bekor
    /// qilingan" deb hisoblab, darvoza BUTUNLAY ochilib ketardi.</para>
    ///
    /// <para>Mantiq: metodda o'z atributi bo'lsa va MENING kalitlarim o'sha metod atributiniki
    /// bilan mos kelmasa-yu, SINF atributiniki bilan mos kelsa — demak men sinfdagi (kengroq)
    /// atributman va o'tkazib yuborilishim kerak.</para>
    /// </summary>
    private bool IsOverriddenAtMethod(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor d) return false;
        var onMethod = d.MethodInfo.GetCustomAttributes(typeof(AdminPermAttribute), inherit: true)
            .Cast<AdminPermAttribute>().ToList();
        if (onMethod.Count == 0) return false;                 // metodda atribut yo'q — o'zim ishlayman
        if (onMethod.Any(SameGate)) return false;              // men aynan o'sha metod atributiman
        return d.ControllerTypeInfo.GetCustomAttributes(typeof(AdminPermAttribute), inherit: true)
            .Cast<AdminPermAttribute>().Any(SameGate);         // men sinfdagiman — chetlab o'taman
    }

    /// <summary>Ikki atribut AYNAN bir xil darvozami (kalitlar + o'qish rejimi).</summary>
    private bool SameGate(AdminPermAttribute other) =>
        other.ReadRequiresPerm == ReadRequiresPerm && other._perms.SequenceEqual(_perms);

    /// <summary>
    /// Shu bo'limda ISHLAYDIMI — ya'ni bo'lim ruxsatining birortasi (yalang <c>section</c> yoki
    /// <c>section:amal</c>) berilganmi. Admin/superadmin — har doim.
    ///
    /// <para><see cref="HasFullAccess"/> dan farqi: bu yerda TO'LIQ ruxsat shart emas — faqat
    /// "qo'shish" ruxsati bor xodim ham bo'lim ma'lumotini o'qiy oladi. Nozik hujjat qaytaradigan
    /// GET'larni darvozalash uchun ishlatiladi (<see cref="ReadRequiresPerm"/>) va javobdagi
    /// maydonlarni tozalash uchun (masalan passport skani manzili).</para>
    /// </summary>
    public static bool HasSectionAccess(ClaimsPrincipal user, string section) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin) ||
        PermissionRules.HasSection(PermValues(user), section);

    /// <summary>Foydalanuvchining <c>perm</c> claim qiymatlari (qoidalar shu ro'yxat ustida ishlaydi).</summary>
    private static IEnumerable<string> PermValues(ClaimsPrincipal user) =>
        user.Claims.Where(c => c.Type == ClaimType).Select(c => c.Value);

    /// <summary>
    /// Bo'lim bo'yicha TO'LIQ (barcha 4 amal) ruxsati bormi — GET bo'lgani uchun odatdagi
    /// <see cref="OnAuthorization"/> yo'li bilan tekshirilmaydigan, lekin nozik (parol eksporti
    /// kabi) amallar uchun ishlatiladi: admin/superadmin — har doim; xodim (staff) — faqat
    /// yalang <c>section</c> (barcha amal) claim'i berilgan bo'lsa.
    /// </summary>
    public static bool HasFullAccess(ClaimsPrincipal user, string section) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin) ||
        user.Claims.Any(c => c.Type == ClaimType && c.Value == section);

    /// <summary>
    /// SUPERADMIN yoki shu bo'lim ruxsati ANIQ berilgan xodim.
    /// <para><see cref="HasFullAccess"/> dan farqi: oddiy <c>admin</c> roli KIRMAYDI. Markaz egasi
    /// o'zida qoldirmoqchi bo'lgan nozik amallar uchun (masalan bonus ptichkasi) — u faqat
    /// superadminda va "Xodimlar va rollar" dan ruxsat berilgan xodimda ochiladi.</para>
    /// </summary>
    public static bool IsSuperAdminOrGranted(ClaimsPrincipal user, string section) =>
        user.IsInRole(Roles.SuperAdmin) ||
        user.Claims.Any(c => c.Type == ClaimType
                             && (c.Value == section
                                 || c.Value.StartsWith(section + ":", StringComparison.Ordinal)));
}
