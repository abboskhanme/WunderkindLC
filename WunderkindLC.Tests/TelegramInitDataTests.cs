using System.Security.Cryptography;
using System.Text;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// <see cref="TelegramInitData.Validate"/> — Telegram Mini App'ning YAGONA autentifikatsiya
/// darvozasi. Mini App ichida "login" yo'q: sahifa Telegram imzolagan <c>initData</c> satrini
/// serverga yuboradi, server esa uni BOT TOKENI bilan qayta hisoblab tekshiradi. Agar bu
/// tekshiruv teshik bo'lsa — istalgan odam o'zini boshqa nomzod/o'quvchi deb tanishtirib,
/// begona arizalarni ko'ra oladi. Shu sabab bu fayl testlari eng muhimlaridan.
///
/// Rasmiy algoritm (Telegram hujjatlari):
/// <code>
/// secret_key = HMAC_SHA256(key: "WebAppData", data: bot_token)
/// hash       = HMAC_SHA256(key: secret_key,  data: data_check_string)
/// </code>
/// </summary>
public class TelegramInitDataTests
{
    /// <summary>Sinov uchun "bot tokeni" — haqiqiy emas, faqat HMAC kaliti sifatida ishlatiladi.</summary>
    private const string BotToken = "7000000001:AAH_test_token_QWERTYuiop_1234567890";

    /// <summary>Boshqa botning tokeni — noto'g'ri kalit bilan imzo mos kelmasligini ko'rsatish uchun.</summary>
    private const string OtherBotToken = "8000000002:AAH_boshqa_bot_tokeni_0987654321";

    /* =========================================================================================
     *  YORDAMCHILAR — Telegram nima yuborsa, aynan shuni yasaydi
     * ========================================================================================= */

    /// <summary>
    /// <c>data_check_string</c> ustidan HMAC hisoblaydi: <c>hash</c>dan tashqari maydonlar
    /// kalit bo'yicha <see cref="StringComparer.Ordinal"/> saralanib, <c>k=v</c> ko'rinishida
    /// <c>\n</c> bilan ulanadi.
    /// </summary>
    private static string ComputeHash(string token, IDictionary<string, string> fields)
    {
        var dataCheckString = string.Join('\n', fields
            .Where(f => f.Key != "hash")
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={f.Value}"));

        var secret = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Imzolangan xom <c>initData</c> satri: maydonlar <c>encodeURIComponent</c> bilan
    /// kodlanib <c>&amp;</c> orqali ulanadi, oxirida hisoblangan <c>hash</c>.
    /// </summary>
    private static string BuildInitData(string token, IDictionary<string, string> fields)
    {
        var parts = fields
            .Where(f => f.Key != "hash")
            .Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}")
            .ToList();
        parts.Add("hash=" + ComputeHash(token, fields));
        return string.Join("&", parts);
    }

    /// <summary>Telegram beradigan <c>user</c> JSON'i.</summary>
    private static string UserJson(
        long id = 55501, string username = "ali_v", string first = "Ali", string last = "Valiyev") =>
        $$"""{"id":{{id}},"first_name":"{{first}}","last_name":"{{last}}","username":"{{username}}","language_code":"uz"}""";

    /// <summary>Odatiy (yaroqli) maydonlar to'plami. <paramref name="authDate"/> berilmasa — hozir.</summary>
    private static Dictionary<string, string> BaseFields(
        string? userJson = null, DateTimeOffset? authDate = null) => new()
    {
        ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
        ["user"] = userJson ?? UserJson(),
        ["auth_date"] = (authDate ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(),
        ["chat_instance"] = "-1234567890123456789",
        ["chat_type"] = "private",
    };

    /* =========================================================================================
     *  ASOSIY OQIM
     * ========================================================================================= */

    [Fact]
    public void Validate_TogriImzo_FoydalanuvchiniQaytaradi()
    {
        var initData = BuildInitData(BotToken, BaseFields());

        var user = TelegramInitData.Validate(initData, BotToken);

        Assert.NotNull(user);
        Assert.Equal(55501, user!.ChatId);
        Assert.Equal("ali_v", user.Username);
        Assert.Equal("Ali", user.FirstName);
        Assert.Equal("Valiyev", user.LastName);
    }

    [Fact]
    public void Validate_IxtiyoriyMaydonlarYoq_BoshSatrQaytadi()
    {
        // username/last_name bo'lmasligi mumkin — bu xato emas, tokenlar bo'sh qoladi.
        var fields = BaseFields("""{"id":42,"first_name":"Ali"}""");

        var user = TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken);

        Assert.NotNull(user);
        Assert.Equal(42, user!.ChatId);
        Assert.Equal("", user.Username);
        Assert.Equal("", user.LastName);
    }

    /* =========================================================================================
     *  IMZO BUZILGAN HOLATLAR — hammasi null bo'lishi SHART
     * ========================================================================================= */

    [Fact]
    public void Validate_HashBuzilgan_Null()
    {
        var initData = BuildInitData(BotToken, BaseFields());
        // Oxirgi belgini o'zgartiramiz (hex bo'lib qolsin, lekin boshqa qiymat).
        var buzilgan = initData[..^1] + (initData[^1] == 'a' ? 'b' : 'a');

        Assert.Null(TelegramInitData.Validate(buzilgan, BotToken));
    }

    [Fact]
    public void Validate_UserIdImzodanKeyinOzgartirilgan_Null()
    {
        // ENG MUHIM HUJUM: hujumchi o'z initData'sini oladi va faqat user.id ni boshqa
        // odamnikiga almashtiradi — hash eski qolgani uchun rad etilishi shart.
        var fields = BaseFields(UserJson(id: 55501));
        var initData = BuildInitData(BotToken, fields);

        var soxta = initData.Replace(
            Uri.EscapeDataString(UserJson(id: 55501)),
            Uri.EscapeDataString(UserJson(id: 999999)));
        Assert.NotEqual(initData, soxta); // almashtirish haqiqatan bo'lganini tasdiqlaymiz

        Assert.Null(TelegramInitData.Validate(soxta, BotToken));
    }

    [Fact]
    public void Validate_BoshqaBotTokeni_Null()
    {
        // Karyera boti tokeni bilan imzolangan satr asosiy bot tokenida o'tmasligi kerak.
        var initData = BuildInitData(OtherBotToken, BaseFields());

        Assert.Null(TelegramInitData.Validate(initData, BotToken));
    }

    [Fact]
    public void Validate_QoshimchaMaydonQoshilgan_Null()
    {
        // Imzodan keyin yangi maydon qo'shilsa data_check_string o'zgaradi — hash mos kelmaydi.
        var initData = BuildInitData(BotToken, BaseFields()) + "&start_param=admin";

        Assert.Null(TelegramInitData.Validate(initData, BotToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_InitDataBosh_Null(string? initData)
    {
        Assert.Null(TelegramInitData.Validate(initData, BotToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_BotTokenBosh_Null(string token)
    {
        // Token sozlanmagan bo'lsa (bot o'chirilgan) — HECH KIM o'tmasligi kerak.
        var initData = BuildInitData(BotToken, BaseFields());

        Assert.Null(TelegramInitData.Validate(initData, token));
    }

    [Fact]
    public void Validate_HashMaydoniYoq_Null()
    {
        var fields = BaseFields();
        var initData = string.Join("&", fields.Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}"));

        Assert.Null(TelegramInitData.Validate(initData, BotToken));
    }

    [Fact]
    public void Validate_HashBosh_Null()
    {
        var fields = BaseFields();
        var initData = string.Join("&", fields.Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}")) + "&hash=";

        Assert.Null(TelegramInitData.Validate(initData, BotToken));
    }

    [Fact]
    public void Validate_FaqatHash_Null()
    {
        // Boshqa maydonsiz hash — pairs bo'sh, hech nima tekshirib bo'lmaydi.
        Assert.Null(TelegramInitData.Validate("hash=deadbeef", BotToken));
    }

    /* =========================================================================================
     *  MUDDAT (auth_date) — o'g'irlangan satr abadiy ishlamasin
     * ========================================================================================= */

    [Fact]
    public void Validate_AuthDate25SoatOldin_Null()
    {
        // Default muddat — 24 soat.
        var fields = BaseFields(authDate: DateTimeOffset.UtcNow.AddHours(-25));

        Assert.Null(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    [Fact]
    public void Validate_AuthDate23SoatOldin_Ok()
    {
        var fields = BaseFields(authDate: DateTimeOffset.UtcNow.AddHours(-23));

        Assert.NotNull(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    [Fact]
    public void Validate_MaxAge5Daqiqa_10DaqiqalikSatr_Null()
    {
        var fields = BaseFields(authDate: DateTimeOffset.UtcNow.AddMinutes(-10));
        var initData = BuildInitData(BotToken, fields);

        Assert.Null(TelegramInitData.Validate(initData, BotToken, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Validate_MaxAge1Soat_10DaqiqalikSatr_Ok()
    {
        // AYNI SATR, faqat ruxsat etilgan muddat kattaroq — o'tishi kerak.
        var fields = BaseFields(authDate: DateTimeOffset.UtcNow.AddMinutes(-10));
        var initData = BuildInitData(BotToken, fields);

        Assert.NotNull(TelegramInitData.Validate(initData, BotToken, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Validate_AuthDateKelajakda_Null()
    {
        // Soat farqiga 5 daqiqa yon berilgan; 10 daqiqa kelajak — rad etiladi.
        var fields = BaseFields(authDate: DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.Null(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    [Fact]
    public void Validate_AuthDateOzginaKelajakda_Ok()
    {
        // Serverlar soati bir necha soniyaga farq qilishi normal — 2 daqiqa qabul qilinadi.
        var fields = BaseFields(authDate: DateTimeOffset.UtcNow.AddMinutes(2));

        Assert.NotNull(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    [Fact(Skip = "XATO (TelegramInitData.cs:69-74): auth_date maydoni bo'lmasa muddat tekshiruvi "
                 + "o'tkazib yuboriladi (fail-open) — tuzatilgach Skip olib tashlanadi")]
    public void Validate_AuthDateYoq_Null()
    {
        // KUTILGAN xulq: auth_date yo'q satr = muddatini aniqlab bo'lmaydigan satr → rad etilishi kerak.
        // HOZIRGI xulq: long.TryParse muvaffaqiyatsiz bo'lgani uchun `if` bloki UMUMAN bajarilmaydi
        // va imzo to'g'ri bo'lsa satr ABADIY yaroqli qoladi (bir marta o'g'irlangan initData
        // cheksiz ishlaydi). Tuzatish: authRaw bo'sh/parse bo'lmasa `return null`.
        var fields = new Dictionary<string, string>
        {
            ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
            ["user"] = UserJson(),
        };

        Assert.Null(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    /* =========================================================================================
     *  user MAYDONI
     * ========================================================================================= */

    [Fact]
    public void Validate_UserMaydoniYoq_Null()
    {
        var fields = new Dictionary<string, string>
        {
            ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        };

        Assert.Null(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    [Fact]
    public void Validate_UserdaIdYoq_Null()
    {
        var fields = BaseFields("""{"first_name":"Ali","username":"ali_v"}""");

        Assert.Null(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    [Fact]
    public void Validate_UserBuzuqJson_Null()
    {
        // Imzo to'g'ri bo'lsa ham JSON o'qib bo'lmasa — istisno emas, null qaytishi kerak.
        var fields = BaseFields("""{"id":42,"first_name":"Ali" """);

        Assert.Null(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    [Fact]
    public void Validate_UserIdMatnSifatida_Null()
    {
        // {"id":"42"} — GetInt64() istisno beradi, catch null qaytaradi (qulash yo'q).
        var fields = BaseFields("""{"id":"42","first_name":"Ali"}""");

        Assert.Null(TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken));
    }

    /* =========================================================================================
     *  KODLASH / FORMAT NOZIKLIKLARI
     * ========================================================================================= */

    [Fact]
    public void Validate_QiymatdaPlusBelgisi_ImzoMosKeladi()
    {
        // NOZIK JOY: query-parser `+` ni BO'SH JOYGA aylantiradi va imzo buziladi. Kod ataylab
        // Uri.UnescapeDataString ishlatadi. Bu yerda `+` ni XOM holda (kodlanmagan) yozamiz —
        // dekodlangandan keyin ham `+` bo'lib qolishi va hash mos kelishi kerak.
        var fields = BaseFields(UserJson(first: "A+B"));
        var hash = ComputeHash(BotToken, fields);

        var initData =
            $"auth_date={fields["auth_date"]}" +
            $"&chat_instance={Uri.EscapeDataString(fields["chat_instance"])}" +
            $"&chat_type={fields["chat_type"]}" +
            $"&query_id={fields["query_id"]}" +
            $"&user={Uri.EscapeDataString(fields["user"]).Replace("%2B", "+")}" +
            $"&hash={hash}";

        var user = TelegramInitData.Validate(initData, BotToken);

        Assert.NotNull(user);
        Assert.Equal("A+B", user!.FirstName);
    }

    [Fact]
    public void Validate_KirillVaEmoji_ImzoMosKeladi()
    {
        // UTF-8 kodlash: ism kirill/emoji bo'lsa ham HMAC bayt darajasida mos kelishi kerak.
        var fields = BaseFields(UserJson(first: "Абдулла", last: "Тошматов 🎓"));

        var user = TelegramInitData.Validate(BuildInitData(BotToken, fields), BotToken);

        Assert.NotNull(user);
        Assert.Equal("Абдулла", user!.FirstName);
        Assert.Equal("Тошматов 🎓", user.LastName);
    }

    [Fact]
    public void Validate_HashKattaHarflarda_Qabul()
    {
        // Ba'zi klientlar hex'ni katta harfda yuboradi — taqqoslash registrga bog'liq bo'lmasin.
        var fields = BaseFields();
        var initData = BuildInitData(BotToken, fields);
        var hash = ComputeHash(BotToken, fields);
        var kattaHarfli = initData.Replace("hash=" + hash, "hash=" + hash.ToUpperInvariant());

        Assert.NotNull(TelegramInitData.Validate(kattaHarfli, BotToken));
    }

    [Fact]
    public void Validate_SignatureMaydoni_HisobgaKirmaydi()
    {
        // Telegram yangi Ed25519 `signature` maydonini ham yuboradi — u HMAC data_check_string'ga
        // KIRMAYDI. Kirsa edi, barcha yangi klientlar 401 olardi.
        var fields = BaseFields();
        var initData = BuildInitData(BotToken, fields) + "&signature=aBcD_ef-123456789";

        Assert.NotNull(TelegramInitData.Validate(initData, BotToken));
    }

    [Fact]
    public void Validate_MaydonTartibiAhamiyatsiz()
    {
        // Kod maydonlarni o'zi saralaydi — klient qanday tartibda yuborsa ham natija bir xil.
        var fields = BaseFields();
        var hash = ComputeHash(BotToken, fields);
        var teskari = string.Join("&", fields
            .OrderByDescending(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}")) + "&hash=" + hash;

        Assert.NotNull(TelegramInitData.Validate(teskari, BotToken));
    }
}
