namespace WunderkindLC.Application.Services;

/// <summary>
/// <c>.env</c> faylini konfiguratsiyaga yuklaydi — ilova konteynersiz ishga tushirilganda
/// (mahalliy <c>dotnet run</c>, bare-metal xizmat) ham kalitlar <b>.env</b> dan o'qilsin.
///
/// <para>Prod (docker compose) da <c>.env</c> qiymatlari <c>Telegram__BotToken</c> ko'rinishida
/// muhit o'zgaruvchisi sifatida uzatiladi — bu yerda hech narsa qilinmaydi (konteyner ichida
/// <c>.env</c> fayli yo'q). <see cref="AppSecrets"/> ikkala nomni ham (bo'lim:kalit VA xom
/// <c>TELEGRAM_BOT_TOKEN</c>) o'qiganligi uchun ikkala rejim ham bir xil ishlaydi.</para>
///
/// <para>HAQIQIY muhit o'zgaruvchisi HAR DOIM ustun: <c>.env</c> faqat o'rnatilmagan kalitlarni
/// to'ldiradi (deploy vaqtida berilgan qiymatni fayl bosib ketmasin).</para>
///
/// <para><b><c>DOTENV_DISABLED=true</c> — faylni umuman o'qimaydi.</b> Bu LOKAL DOCKER (hot reload)
/// uchun hayotiy muhim: u yerda repo butunlay konteynerga ulanadi, ya'ni <c>/src/.env</c> — PROD
/// kalitlari bilan — konteyner ichida ko'rinib qoladi. Compose'da <c>Telegram__BotToken: ""</c> deb
/// qo'yish YETARLI EMAS: <see cref="AppSecrets"/> bo'sh qiymatdan keyin xom nomga
/// (<c>TELEGRAM_BOT_TOKEN</c>) tushadi va uni aynan shu fayldan oladi — natijada lokal bot JONLI
/// bot tokeni bilan <c>getUpdates</c> qilib, foydalanuvchi xabarlarini serverdan o'g'irlab qo'yadi
/// (Telegram har yangilanishni faqat bir marta yetkazadi).</para>
/// </summary>
public static class DotEnvFile
{
    /// <summary>Faylni o'qishni butunlay o'chiradigan muhit o'zgaruvchisi.</summary>
    public const string DisableEnvVar = "DOTENV_DISABLED";

    /// <summary>
    /// <paramref name="startDir"/> dan boshlab yuqoriga (<paramref name="maxUp"/> pog'onagacha)
    /// <c>.env</c> qidiradi va topilganini o'qiydi. Qaytadi: konfiguratsiyaga qo'shiladigan
    /// kalit→qiymat (allaqachon muhitda bor kalitlar CHIQARIB tashlanadi). Fayl yo'q — bo'sh ro'yxat.
    /// <c>DOTENV_DISABLED=true</c> bo'lsa — hech narsa o'qilmaydi.
    /// </summary>
    public static Dictionary<string, string?> Load(string startDir, int maxUp = 3)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var disabled = Environment.GetEnvironmentVariable(DisableEnvVar);
        if (!string.IsNullOrWhiteSpace(disabled) &&
            (disabled.Equals("true", StringComparison.OrdinalIgnoreCase) || disabled == "1"))
            return result;

        var path = Find(startDir, maxUp);
        if (path is null) return result;

        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return result; }   // o'qib bo'lmasa — sozlamasiz davom etamiz

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase)) line = line[7..].Trim();

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Length == 0) continue;

            // Qiymat qo'shtirnoq ichida bo'lishi mumkin ("...JSON...") — tirnoqlarni olib tashlaymiz.
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            // Haqiqiy muhit o'zgaruvchisi ustun (deploy qiymatini fayl bosib ketmasin).
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))) continue;

            result[key] = value;
        }
        return result;
    }

    /// <summary>Joriy va yuqoridagi kataloglarda <c>.env</c> ni qidiradi (repo ildizida yotgan bo'lishi mumkin).</summary>
    private static string? Find(string startDir, int maxUp)
    {
        var dir = startDir;
        for (var i = 0; i <= maxUp && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, ".env");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
