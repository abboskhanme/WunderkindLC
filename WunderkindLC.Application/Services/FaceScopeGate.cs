namespace WunderkindLC.Application.Services;

/// <summary>
/// CHEKLANGAN TOKEN DARVOZASI — "yuz tasdiqlanmagan" sessiya nima qila oladi.
///
/// <para>Login yuz tasdig'ini talab qilganda foydalanuvchiga ODATDAGI token BERILMAYDI: qaytadigan
/// JWT'da <c>scope=face</c> claim'i bo'ladi va u FAQAT yuz tasdiqlash oqimiga yetadi. Bu bo'lmasa
/// butun funksiya bezakka aylanardi — ilova selfi ekranini ko'rsatib turarkan, o'sha token bilan
/// jurnal/baho/chat endpointlariga bemalol borib kelaverardi.</para>
///
/// <para>Qoida SOF FUNKSIYA sifatida shu yerda (testlangan: <c>FaceLoginTests</c>), middleware esa
/// (Program.cs) faqat uni chaqiradi — "nimaga ruxsat" ro'yxati bitta joyda tursin.</para>
/// </summary>
public static class FaceScopeGate
{
    /// <summary>JWT claim turi va qiymati.</summary>
    public const string ClaimType = "scope";
    public const string FaceScope = "face";

    /// <summary>Cheklangan tokenning amal qilish muddati (daqiqa) — selfi olish uchun yetarli,
    /// lekin o'g'irlangan token uzoq yashamasin.</summary>
    public const int TokenMinutes = 15;

    /// <summary>Ilova bu javobni ko'rib "selfi ekranini och" deb tushunadi.</summary>
    public const string BlockedMessage = "Yuz tasdiqlanmagan — selfi yuboring";

    /// <summary>
    /// Cheklangan (scope=face) token bilan shu so'rovga ruxsat berilsinmi.
    ///
    /// <para><b>Darvozalanadigan yo'llar:</b> <c>/api/*</c>, <c>/hubs/*</c> (SignalR chat/live) va
    /// <c>/uploads/*</c>. Qolganlari (SPA statikasi, landing) — ma'lumot bermaydi, tegilmaydi.</para>
    ///
    /// <para><c>/hubs</c> ATAYIN ro'yxatda: u <c>/api</c> bilan boshlanmaydi, ya'ni faqat "/api"
    /// tekshirilsa cheklangan token bilan GURUH CHATIGA ulanib bo'lardi.</para>
    /// </summary>
    public static bool IsAllowed(string? path, string? method)
    {
        var p = (path ?? "").TrimEnd('/');
        if (p.Length == 0) return true;

        var gated = Starts(p, "/api") || Starts(p, "/hubs") || Starts(p, "/uploads");
        if (!gated) return true;

        var m = (method ?? "").ToUpperInvariant();

        // Yuz tasdiqlash oqimining O'ZI.
        if (Eq(p, "/api/student/face/status") && m == "GET") return true;
        if (Eq(p, "/api/student/face/photo") && m == "GET") return true;
        // Bir martalik tiriklik chaqiruvi (nonce + harakatlar) — selfi yuborishdan OLDIN
        // chaqiriladi, ya'ni cheklangan token bilan ishlashi SHART.
        if (Eq(p, "/api/student/face/challenge") && m == "POST") return true;
        if (Eq(p, "/api/student/face/verify") && m == "POST") return true;

        // Chiqish — foydalanuvchi selfi bermasdan sessiyani tugata olishi kerak.
        if (Eq(p, "/api/auth/logout") && m == "POST") return true;

        // Qayta login: bu endpointlar `[AllowAnonymous]` — token umuman kerak emas, ya'ni ruxsat
        // berish HECH QANDAY imtiyoz bermaydi. Lekin ilova eski (cheklangan) tokenni sarlavhada
        // qoldirib qayta login qilsa, darvoza uni 401 bilan qaytarib, foydalanuvchini "kira
        // olmaydigan" holatga tushirib qo'yardi.
        if (Eq(p, "/api/auth/login") && m == "POST") return true;
        if (Eq(p, "/api/auth/otp-login") && m == "POST") return true;

        return false;
    }

    private static bool Eq(string path, string value) =>
        string.Equals(path, value, StringComparison.OrdinalIgnoreCase);

    private static bool Starts(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
