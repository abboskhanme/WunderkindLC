using System.Security.Cryptography;
using System.Text;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Webhook HAQIQIYLIGINI tekshirish — <c>X-Hub-Signature-256</c> (HMAC-SHA256) va GET verify.
///
/// <para><b>⚠️ FAIL-CLOSED.</b> NUR loyihasida App Secret bo'sh bo'lsa imzo tekshiruvi
/// O'TKAZIB YUBORILARDI (mahalliy test uchun ataylab qo'yilgan edi) — prodda bu har kim
/// bizning nomimizdan hodisa yubora oladigan OCHIQ endpoint degani. Bu yerda kalit bo'sh bo'lsa
/// so'rov RAD ETILADI: modul sozlanmagan bo'lsa umuman ishlamagani xavfsizroq.</para>
///
/// <para><b>⚠️ XOM BODY.</b> HMAC Meta yuborgan baytlardan hisoblanadi. Body deserializatsiya
/// qilinib, keyin qayta seriyalansa (bo'sh joy va kalitlar tartibi o'zgaradi) imzo HECH QACHON
/// mos kelmaydi — controller body'ni <c>byte[]</c> sifatida o'qib shu funksiyaga beradi.</para>
///
/// <para>Sof funksiyalar — baza/tarmoq yo'q, to'liq testlanadi.</para>
/// </summary>
public static class InstagramSignature
{
    private const string Prefix = "sha256=";

    /// <summary>
    /// Imzoni tekshiradi. <paramref name="appSecret"/> bo'sh bo'lsa — <c>false</c> (fail-closed).
    /// Solishtirish DOIMIY VAQTLI (<see cref="CryptographicOperations.FixedTimeEquals"/>):
    /// oddiy <c>==</c> baytma-bayt to'xtagani uchun imzoni vaqt o'lchab topish mumkin bo'lardi.
    /// </summary>
    public static bool Verify(byte[] rawBody, string? headerValue, string appSecret)
    {
        if (string.IsNullOrWhiteSpace(appSecret)) return false;      // ⚠️ FAIL-CLOSED
        if (rawBody is null) return false;
        var header = (headerValue ?? "").Trim();
        if (header.Length == 0) return false;
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var hex = header[Prefix.Length..].Trim();
        if (hex.Length != 64) return false;

        byte[] given;
        try { given = Convert.FromHexString(hex); }
        catch (FormatException) { return false; }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var expected = hmac.ComputeHash(rawBody);
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <summary>
    /// Imzo NEGA mos kelmadi — qisqa, o'zbekcha sabab (faqat LOG uchun; javobga tushmaydi).
    ///
    /// <para><b>Nega kerak?</b> «imzo mos kelmadi» ning kamida olti xil sababi bor va ular
    /// butunlay BOSHQA ishni talab qiladi: sarlavha umuman yo'q (Meta emas, tashqi so'rov) —
    /// hech narsa qilinmaydi; imzo boshqa kalit bilan qilingan — Meta konsolidagi ilova
    /// noto'g'ri; kalit sozlanmagan — `.env`/compose ishi. Yalang «mos kelmadi» matni bilan
    /// bularni ajratib bo'lmasdi va tekshiruv har safar noldan boshlanardi.</para>
    ///
    /// <para>⚠️ Sabab matniga <b>kalit ham, imzo qiymati ham, body ham</b> qo'shilmaydi:
    /// bular maxfiy yoki tashqaridan boshqariladigan qiymatlar (§7 "log orqali sizib chiqish").
    /// Faqat "qanday nosozlik" nomlanadi.</para>
    ///
    /// <param name="altSecret">Ikkinchi kalit (<c>MetaAppSecret</c>) — <b>faqat SABABNI
    /// aniqlash uchun</b> solishtiriladi, so'rovni QABUL QILISH uchun EMAS. U mos kelsa
    /// demak Page hodisasi Instagram manziliga kelyapti va Meta konsolida yo'naltirish
    /// noto'g'ri (ikkalasi bir xil bo'lsa bu tekshiruv o'tkazib yuboriladi).</param>
    /// </summary>
    public static string DescribeFailure(
        byte[]? rawBody, string? headerValue, string appSecret, string? altSecret = null)
    {
        if (string.IsNullOrWhiteSpace(appSecret))
            return "App Secret sozlanmagan — fail-closed (.env / docker-compose environment)";
        if (rawBody is null) return "body o'qilmadi";

        var header = (headerValue ?? "").Trim();
        if (header.Length == 0)
            return "X-Hub-Signature-256 sarlavhasi YO'Q — so'rov Meta'dan kelmagan "
                   + "(skaner yoki qo'lda yuborilgan so'rov)";
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return "sarlavha `sha256=` bilan boshlanmaydi";

        var hex = header[Prefix.Length..].Trim();
        if (hex.Length != 64)
            return $"imzo uzunligi noto'g'ri (64 hex kutilgan, {hex.Length} keldi)";
        try { _ = Convert.FromHexString(hex); }
        catch (FormatException) { return "imzo hex formatda emas"; }

        // Shu yergacha yetgan bo'lsa struktura TO'G'RI — demak HMAC qiymati boshqa kalitniki.
        if (!string.IsNullOrWhiteSpace(altSecret)
            && !string.Equals(altSecret, appSecret, StringComparison.Ordinal)
            && Verify(rawBody, header, altSecret!))
        {
            return "imzo META (Page) kaliti bilan MOS KELDI — Page hodisasi Instagram manziliga "
                   + "kelyapti; Meta konsolida uni /leadgen manziliga yo'naltiring";
        }

        return "imzo BOSHQA App Secret bilan qilingan — yuboruvchi ilova "
               + "INSTAGRAM_APP_SECRET egasi EMAS (Meta konsolida boshqa ilova obuna bo'lgan)";
    }

    /// <summary>
    /// GET verify (Meta webhook manzilini ro'yxatga olayotganda).
    /// <para><c>hub.mode == "subscribe"</c> VA token mos bo'lsa — <c>challenge</c> qaytariladi
    /// (controller uni <b>text/plain</b> qilib beradi: JSON qo'shtirnog'i bilan yuborilsa Meta
    /// tasdiqlamaydi). Aks holda <c>null</c> → 403.</para>
    /// <para>Token sozlanmagan bo'lsa ham <c>null</c> — bu yerda ham fail-closed.</para>
    /// </summary>
    public static string? VerifyChallenge(string? mode, string? token, string? challenge, string verifyToken)
    {
        if (string.IsNullOrWhiteSpace(verifyToken)) return null;      // ⚠️ FAIL-CLOSED
        if ((mode ?? "").Trim() != "subscribe") return null;
        if (!FixedEquals((token ?? "").Trim(), verifyToken.Trim())) return null;
        var ch = (challenge ?? "").Trim();
        return ch.Length == 0 ? null : ch;
    }

    /// <summary>Ikki satrni doimiy vaqtda solishtiradi (uzunlik farqi darhol false).</summary>
    private static bool FixedEquals(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        if (x.Length != y.Length) return false;
        return CryptographicOperations.FixedTimeEquals(x, y);
    }
}
