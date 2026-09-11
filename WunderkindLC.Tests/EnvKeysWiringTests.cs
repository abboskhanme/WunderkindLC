using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// `.env` KALITI KONTEYNERGA YETIB BORADIMI — drift qulfi.
///
/// <para><b>Real hodisa (2026-08-14):</b> Instagram moduli qo'shildi, `AppSecrets.EnvKeys` ga
/// `INSTAGRAM_APP_SECRET` va `INSTAGRAM_VERIFY_TOKEN` yozildi, `.env.example` ga ham tushdi —
/// lekin `docker-compose.yml` dagi `app` servisiga uzatilmadi. Prod'da `app` servisida
/// <c>env_file</c> YO'Q (faqat aniq `environment:` ro'yxati), ya'ni `.env` dagi qator compose
/// o'zgaruvchisi sifatida O'QILADI, lekin konteynerga UZATILMAYDI.</para>
///
/// <para>Natija foydalanuvchi uchun ko'rinmas edi: admin `.env` ga to'g'ri qiymatni yozadi,
/// `docker compose up -d` qiladi, lekin `AppSecrets.InstagramVerifyToken` baribir bo'sh qoladi
/// va webhook FAIL-CLOSED bo'lib Meta tasdig'ini 403 bilan rad etadi. Xato sozlamada emas,
/// compose faylida edi — buni topish uchun uch qatlamni (env → compose → AppSecrets) qo'lda
/// solishtirish kerak bo'ldi.</para>
///
/// <para>Shu sabab qoida test bilan qulflanadi: <b>`EnvKeys` ga qo'shilgan HAR bir kalit
/// `docker-compose.yml` da ham, `.env.example` da ham bo'lishi SHART.</b></para>
/// </summary>
public class EnvKeysWiringTests
{
    /// <summary>Repo ildizi — test bin papkasidan yuqoriga chiqib topiladi.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            dir = dir.Parent;

        Assert.True(dir is not null, "docker-compose.yml topilmadi (repo ildizi aniqlanmadi).");
        return dir!.FullName;
    }

    /// <summary>`AppSecrets.EnvKeys` dagi barcha konstanta qiymatlari.</summary>
    public static TheoryData<string> AllKeys()
    {
        var data = new TheoryData<string>();
        foreach (var f in typeof(AppSecrets.EnvKeys).GetFields())
            if (f.IsLiteral && f.GetRawConstantValue() is string v)
                data.Add(v);
        return data;
    }

    /// <summary>
    /// Kalit prod compose'ida `app` servisiga uzatiladimi. Qidiruv `${KALIT` bo'yicha —
    /// `"${KALIT:-}"` ham, `"${KALIT}"` ham mos keladi.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Prod_compose_kalitni_uzatadi(string key)
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), "docker-compose.yml"));

        Assert.True(compose.Contains("${" + key, StringComparison.Ordinal),
            $"`{key}` docker-compose.yml da YO'Q — .env ga yozilgan qiymat konteynerga yetib "
            + "bormaydi va modul jimgina sozlanmagan bo'lib qoladi. `app` servisining "
            + "`environment:` ro'yxatiga qo'shing (masalan `Bolim__Kalit: \"${" + key + ":-}\"`).");
    }

    /// <summary>
    /// Kalit `.env.example` da hujjatlangan bo'lishi shart — admin qaysi qatorni qo'shishini
    /// shu fayldan ko'radi (Sozlamalar sahifasi ham `EnvKeys` nomlarini ko'rsatadi).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Env_example_kalitni_hujjatlaydi(string key)
    {
        var example = File.ReadAllText(Path.Combine(RepoRoot(), ".env.example"));

        Assert.True(example.Contains(key + "=", StringComparison.Ordinal),
            $"`{key}` .env.example da YO'Q — admin bu kalit borligini bilmaydi.");
    }
}

/// <summary>
/// MAXFIY QIYMAT LOGGA TUSHMASIN + META TALAB QILGAN OCHIQ SAHIFALAR — drift qulflari.
///
/// <para><b>Real hodisa (2026-08-19):</b> konteyner loglarida Telegram bot tokeni OCHIQ holda
/// 102 marta yozilgani topildi. Sabab: <c>AddHttpClient</c> .NET'ning standart HTTP loggerini
/// yoqadi va u so'rovning TO'LIQ manzilini <c>Information</c> darajasida yozadi, bizda esa token
/// MANZIL ICHIDA keladi (<c>api.telegram.org/bot&lt;TOKEN&gt;/…</c>, Instagram Graph'da
/// <c>?access_token=…</c>). Instagram moduli ulangach 60 kunlik token ham shu yo'l bilan
/// loglarga tushardi.</para>
///
/// <para>Tuzatish bitta qatorda (<c>System.Net.Http.HttpClient: Warning</c>), lekin uni bexosdan
/// olib tashlash ham bitta qatorda — shuning uchun test bilan qulflanadi.</para>
/// </summary>
public class SecretLeakAndPublicPageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi topilmadi.");
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void HTTP_klient_loglari_OCHIRILGAN(string file)
    {
        var json = Read("WunderkindLC.Server", file);
        Assert.True(
            json.Contains("\"System.Net.Http.HttpClient\": \"Warning\"", StringComparison.Ordinal),
            $"{file} da `System.Net.Http.HttpClient: Warning` yo'q — HTTP klient so'rov MANZILINI "
            + "Information darajasida yozadi, manzil ichida esa Telegram bot tokeni va Instagram "
            + "access_token keladi. Ular konteyner loglariga (va zaxira nusxalarga) ochiq tushadi.");
    }

    [Fact]
    public void LogLevel_ichida_IZOH_kaliti_BOLMASIN()
    {
        // ⚠️ `Logging:LogLevel` ichidagi HAR bir qiymat LogLevel enum sifatida o'qiladi. U yerga
        // izoh kaliti qo'yilsa ilova "Configuration value ... is not supported" bilan yiqiladi
        // (bu tuzatish paytida aynan shunday bo'ldi). Izoh — LogLevel'dan TASHQARIDA.
        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Read("WunderkindLC.Server", file));
            if (!doc.RootElement.TryGetProperty("Logging", out var logging)) continue;
            if (!logging.TryGetProperty("LogLevel", out var levels)) continue;

            foreach (var p in levels.EnumerateObject())
                Assert.True(
                    Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(p.Value.GetString(), out _),
                    $"{file}: `Logging:LogLevel:{p.Name}` qiymati LogLevel emas ('{p.Value}') — "
                    + "ilova startupda yiqiladi. Izohni LogLevel'dan TASHQARIGA chiqaring.");
        }
    }

    [Theory]
    [InlineData("/privacy", "Maxfiylik siyosati (Meta App: Privacy Policy URL)")]
    [InlineData("/data-deletion", "Ma'lumotni o'chirish (Meta App: Data Deletion Instructions URL)")]
    public void META_talab_qilgan_OCHIQ_marshrutlar_bor(string route, string nima)
    {
        var app = Read("WunderkindLC.Client", "src", "App.tsx");
        Assert.True(
            app.Contains($"path=\"{route}\"", StringComparison.Ordinal),
            $"App.tsx da `{route}` marshruti yo'q — {nima}. Bu maydonsiz Meta App sozlamasi "
            + "yakunlanmaydi (`.claude/rules/marketing-instagram.md` §14).");

        // Marshrut LOGIN ORTIDA qolmasligi kerak: `ProtectedRoute` blokidan OLDIN turishi shart.
        var routeAt = app.IndexOf($"path=\"{route}\"", StringComparison.Ordinal);
        var protectedAt = app.IndexOf("<ProtectedRoute", StringComparison.Ordinal);
        Assert.True(protectedAt < 0 || routeAt < protectedAt,
            $"`{route}` himoyalangan marshrutlar ichida qolib ketgan — u ochiq bo'lishi SHART.");
    }
}
