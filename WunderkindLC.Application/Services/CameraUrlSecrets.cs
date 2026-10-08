namespace WunderkindLC.Application.Services;

/// <summary>
/// Kamera RTSP manzilidagi LOGIN/PAROL — "faqat yoziladigan" (write-only) maydon (sof, testlangan).
///
/// <para><b>Nega:</b> <c>rtsp://login:parol@192.168.1.10:554/...</c> — kameraning (ko'pincha
/// NVR'ning ham) admin paroli. Ilgari <c>GET /api/admin/cameras</c> uni xom qaytarardi, ya'ni
/// ro'yxatni ko'ra oladigan har kim kameraga to'g'ridan-to'g'ri ulana olardi. Endi javobda manzil
/// <see cref="Redact"/> bilan (login/parolsiz) qaytadi, saqlashda esa <see cref="Merge"/> eski
/// login/parolni saqlab qoladi — forma tozalangan manzilni qaytarib yuborsa, parol o'chib ketmaydi.</para>
/// </summary>
public static class CameraUrlSecrets
{
    /// <summary>Manzilni qismlarga ajratadi: sxema ("rtsp://"), userinfo (login:parol, <c>@</c> siz),
    /// avtoritet (host:port) va qolgani (yo'l + so'rov). Sxemasiz qiymat — <c>null</c>.</summary>
    private static (string Scheme, string UserInfo, string Authority, string Tail)? Split(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var s = url.Trim();
        var sep = s.IndexOf("://", StringComparison.Ordinal);
        if (sep <= 0) return null;
        var scheme = s[..(sep + 3)];
        var after = s[(sep + 3)..];
        var slash = after.IndexOf('/');
        var authorityWithUser = slash >= 0 ? after[..slash] : after;
        var rest = slash >= 0 ? after[slash..] : "";
        // Parolda "@" bo'lishi mumkin (kodlanmagan holda ham yoziladi) — ajratkich OXIRGI "@".
        var at = authorityWithUser.LastIndexOf('@');
        return at >= 0
            ? (scheme, authorityWithUser[..at], authorityWithUser[(at + 1)..], rest)
            : (scheme, "", authorityWithUser, rest);
    }

    /// <summary>Manzilda login/parol bormi.</summary>
    public static bool HasCredentials(string? url) => Split(url) is { } p && p.UserInfo.Length > 0;

    /// <summary>Login/parolni olib tashlaydi (javob va UI uchun). Sxemasiz qiymat o'zgarishsiz.</summary>
    public static string Redact(string? url)
    {
        if (Split(url) is not { } p) return (url ?? "").Trim();
        return p.Scheme + p.Authority + p.Tail;
    }

    /// <summary>
    /// Tahrirda saqlanadigan manzil.
    /// <list type="bullet">
    ///   <item>Yangi qiymat bo'sh → eskisi o'zgarishsiz qoladi.</item>
    ///   <item>Yangi qiymatda login/parol BOR → u (admin yangi parol kiritdi).</item>
    ///   <item>Login/parolsiz va HOST:PORT o'sha-o'sha → eski login/parol yangi manzilga QAYTA qo'shiladi
    ///   (yo'l, kanal o'zgargan bo'lishi mumkin).</item>
    ///   <item>Login/parolsiz va host BOSHQA → login/parolsiz saqlanadi. ⚠️ ATAYIN: eski parolni
    ///   yangi hostga ulasak, tahrir huquqi bor odam hostni o'z serveriga almashtirib, shlyuz ulanayotganda
    ///   kameraning parolini qo'lga kiritardi. Host almashsa parol qayta kiritiladi.</item>
    /// </list>
    /// </summary>
    public static string Merge(string? stored, string? incoming)
    {
        var next = (incoming ?? "").Trim();
        var old = (stored ?? "").Trim();
        if (next.Length == 0) return old;
        if (Split(next) is not { } n || n.UserInfo.Length > 0) return next;
        if (Split(old) is not { } o || o.UserInfo.Length == 0) return next;
        return string.Equals(o.Authority, n.Authority, StringComparison.OrdinalIgnoreCase)
            ? n.Scheme + o.UserInfo + "@" + n.Authority + n.Tail
            : next;
    }
}
