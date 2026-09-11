using System.Buffers.Binary;
using System.Security.Cryptography;

namespace WunderkindLC.Application.Services;

/// <summary>
/// YUZ VEKTORLARI SEYFI — biometrik vektorlarni bazaga SHIFRLANGAN holda yozadi (AES-256-GCM).
///
/// <para><b>Nega kerak?</b> Yuz vektori — biometrik ma'lumot, ustiga voyaga yetmaganlarniki
/// (<c>FACE-DETEKT-PLAN.md</c> §5). Loyihada baza dump'i (<c>pg_dump</c>) va JSON nusxasi
/// Telegram'ga yuboriladi, ya'ni ochiq saqlangan vektor o'sha kanal orqali chiqib ketardi.
/// Kalitlar aynan shu sababdan bazadan olib tashlangan edi (migratsiya <c>RemoveSecretsFromDb</c>) —
/// vektorlar ham xuddi shu siyosatga bo'ysunadi.</para>
///
/// <para><b>Kalit — FAQAT <c>.env</c>:</b> <c>FACE_VECTOR_KEY</c> (base64, 32 bayt) →
/// <see cref="AppSecrets.FaceVectorKey"/>. Bazada saqlanmaydi va UI'dan kiritilmaydi. Demak
/// baza dump'i O'ZI YETMAYDI: vektorni ochish uchun serverdagi <c>.env</c> ham kerak.</para>
///
/// <para>⚠️ <b>Kalit sozlanmagan bo'lsa modul YOQILMAYDI</b> (<see cref="Configured"/> = false →
/// <c>FaceLoginService.SettingsAsync</c> "o'chiq" qaytaradi va startupda ogohlantirish logi
/// yoziladi). Jimgina shifrlanmagan saqlashga TUSHIB QOLISH mumkin emas — aks holda "modul
/// ishlayapti" deb turib, biometrika ochiq yotardi.</para>
///
/// <para>⚠️ <b>Kalit o'zgarsa/yo'qolsa</b> eski etalonlar OCHILMAYDI. <see cref="Unprotect"/>
/// bunday holatda ISTISNO TASHLAMAYDI, <c>null</c> qaytaradi — chaqiruvchi buni "etalon yo'q"
/// deb qabul qiladi va o'quvchi keyingi kirishda profil rasmi orqali QAYTA ro'yxatdan o'tadi.
/// Ya'ni kalit yo'qolishi — noqulaylik, ma'lumot yo'qolishi emas (`DEPLOY.md` §2.1).</para>
///
/// <para><b>Blob formati</b> (versiya bayti kelajakda algoritm almashtirishga imkon beradi):</para>
/// <code>
/// [1 bayt versiya = 1][12 bayt nonce][16 bayt GCM teg][shifrlangan float32 LE vektor]
/// </code>
///
/// <para>Sinf INSTANCE (statik emas) va kalit konstruktorda beriladi — testlar global holatga
/// tegmasdan o'z kalitini bera oladi (<c>FaceSecurityTests</c>).</para>
/// </summary>
public sealed class FaceVault
{
    /// <summary>AES-256 — kalit AYNAN 32 bayt bo'lishi shart.</summary>
    public const int KeyBytes = 32;

    /// <summary>Joriy blob formati versiyasi.</summary>
    public const byte FormatVersion = 1;

    private const int NonceBytes = 12;   // AES-GCM standart nonce
    private const int TagBytes = 16;     // AES-GCM standart teg
    private const int HeaderBytes = 1 + NonceBytes + TagBytes;

    private readonly byte[]? _key;

    /// <summary>Kalitni base64 satridan oladi. Bo'sh/buzuq/noto'g'ri uzunlikdagi qiymat —
    /// ISTISNO EMAS: sinf shunchaki "sozlanmagan" bo'lib qoladi (modul o'chadi).</summary>
    public FaceVault(string? base64Key)
    {
        _key = TryDecodeKey(base64Key);
    }

    /// <summary><c>.env</c> dagi <c>FACE_VECTOR_KEY</c> bilan (DI shu orqali yaratadi).</summary>
    public static FaceVault FromSecrets() => new(AppSecrets.FaceVectorKey);

    /// <summary>Kalit bor va yaroqlimi. False bo'lsa yuz bilan kirish moduli ISHLAMAYDI.</summary>
    public bool Configured => _key is not null;

    /// <summary>Yangi kalit yaratish uchun (adminga `.env` ga qo'yish uchun ko'rsatiladi).</summary>
    public static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

    /// <summary>Kalit satri yaroqlimi (32 bayt base64) — sozlamalar diagnostikasi uchun.</summary>
    public static bool IsValidKey(string? base64Key) => TryDecodeKey(base64Key) is not null;

    private static byte[]? TryDecodeKey(string? base64Key)
    {
        var s = (base64Key ?? "").Trim();
        if (s.Length == 0) return null;
        try
        {
            var raw = Convert.FromBase64String(s);
            return raw.Length == KeyBytes ? raw : null;
        }
        catch (FormatException) { return null; }
    }

    /* =============================================================================================
     *  SHIFRLASH / OCHISH
     * ========================================================================================== */

    /// <summary>
    /// Vektorni shifrlab, bazaga yoziladigan blob qaytaradi.
    /// ⚠️ Kalit sozlanmagan bo'lsa <see cref="InvalidOperationException"/> — bu KOD xatosi:
    /// modul kalitsiz umuman yoqilmasligi kerak, "jimgina ochiq yozish" varianti YO'Q.
    /// </summary>
    public byte[] Protect(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (_key is null)
            throw new InvalidOperationException(
                "FACE_VECTOR_KEY sozlanmagan — yuz vektorini shifrlab bo'lmaydi (modul o'chiq bo'lishi kerak edi).");

        var plain = new byte[vector.Length * 4];
        for (var i = 0; i < vector.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(plain.AsSpan(i * 4, 4), vector[i]);

        var blob = new byte[HeaderBytes + plain.Length];
        blob[0] = FormatVersion;
        var nonce = blob.AsSpan(1, NonceBytes);
        RandomNumberGenerator.Fill(nonce);

        using var gcm = new AesGcm(_key, TagBytes);
        gcm.Encrypt(nonce, plain, blob.AsSpan(1 + NonceBytes + TagBytes, plain.Length),
            blob.AsSpan(1 + NonceBytes, TagBytes));
        return blob;
    }

    /// <summary>
    /// Blobni ochadi. <b>Hech qachon istisno tashlamaydi</b> — buzuq blob, boshqa kalit bilan
    /// shifrlangan blob, eski (shifrlanmagan) qator yoki noma'lum versiya uchun <c>null</c>.
    ///
    /// <para>Chaqiruvchi <c>null</c> ni "etalon yo'q" deb qabul qiladi: o'quvchi keyingi kirishda
    /// profil rasmi orqali qayta ro'yxatdan o'tadi. Istisno tashlansa esa kalit almashtirilgan
    /// serverda BARCHA o'quvchi 500 xato bilan kira olmay qolardi.</para>
    /// </summary>
    public float[]? Unprotect(byte[]? blob)
    {
        if (_key is null || blob is null) return null;
        if (blob.Length <= HeaderBytes) return null;
        if (blob[0] != FormatVersion) return null;

        var cipherLen = blob.Length - HeaderBytes;
        if (cipherLen % 4 != 0) return null;

        var plain = new byte[cipherLen];
        try
        {
            using var gcm = new AesGcm(_key, TagBytes);
            gcm.Decrypt(
                blob.AsSpan(1, NonceBytes),
                blob.AsSpan(1 + NonceBytes + TagBytes, cipherLen),
                blob.AsSpan(1 + NonceBytes, TagBytes),
                plain);
        }
        catch (CryptographicException) { return null; }   // teg mos emas = boshqa kalit yoki buzuq
        catch (ArgumentException) { return null; }        // o'lchamlar mos emas

        var v = new float[cipherLen / 4];
        for (var i = 0; i < v.Length; i++)
            v[i] = BinaryPrimitives.ReadSingleLittleEndian(plain.AsSpan(i * 4, 4));
        return v;
    }
}
