using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace WunderkindLC.Application.Services;

/// <summary>
/// MAXFIY MAYDONLARNI TASHQARIGA CHIQADIGAN JSON'DAN OLIB TASHLASH — yagona qoida (sof funksiyalar,
/// testlangan: <c>AuditFixT10Tests</c>).
///
/// <para>Ishlatiladigan joylar: kunlik Telegram JSON zaxirasi (<see cref="BackupService"/> — fayl
/// Telegram chatiga ketadi) va o'chirilgan xodim akkauntining arxiv surati (StaffController.Delete).
/// Ilgari zaxiraga <c>CenterMeta.InstagramCapiToken</c> va arxivdagi xodimning <c>PasswordHash</c> /
/// OCHIQ <c>InitialPassword</c> i xom holda tushardi.</para>
///
/// <para>⚠️ Qoida NOM bo'yicha (tur bo'yicha emas) — ATAYIN: yangi entity'ga <c>...Token</c> yoki
/// <c>...Password</c> maydoni qo'shilsa, u zaxiraga o'z-o'zidan TUSHMAYDI (unutib qo'yish xavfi yo'q).
/// Kamchiligi — bir-ikki zararsiz maydon ham tushib qoladi (masalan push qurilma <c>Token</c>i):
/// bu JSON baribir "ma'lumot nusxasi", to'liq tiklash pg_dump orqali (DEPLOY.md §6.3).</para>
/// </summary>
public static class SecretScrub
{
    /// <summary>Aniq nomlar: parol/token XESHLARI (kontent xeshlari — <c>FileHash</c>, <c>TextHash</c> —
    /// emas), kamera RTSP manzili (ichida login/parol bor), VAPID kaliti.</summary>
    private static readonly HashSet<string> ExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "InitialPassword", "TokenHash", "ReplacedByHash", "CodeHash",
        "FcmVapidKey", "RtspUrl", "RtspSubUrl",
    };

    /// <summary>Maxfiy emas, lekin zaxiraga YARAMAYDIGAN og'ir ustunlar (vektor embedding'lar).</summary>
    private static readonly HashSet<string> BulkyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "EmbeddingJson",
    };

    /// <summary>Maydon nomi maxfiymi: aniq ro'yxat + <c>...Token</c> bilan tugaydigan, yoki
    /// <c>Password</c> / <c>Secret</c> / <c>ApiKey</c> / <c>PrivateKey</c> ni o'z ichiga olgan nomlar.
    /// <c>TokenExpiresAt</c> kabi (token EMAS, sana) nomlar o'tadi.</summary>
    public static bool IsSecretName(string? name) =>
        !string.IsNullOrEmpty(name)
        && (ExactNames.Contains(name)
            || name.EndsWith("Token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
            || name.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <see cref="DefaultJsonTypeInfoResolver"/> modifikatori: HAR QANDAY turning (anonim tur ham)
    /// seriyalash shartnomasidan maxfiy maydonlarni, <c>byte[]</c> (biometrik vektor va h.k.) va og'ir
    /// embedding ustunlarini olib tashlaydi. Xotira bo'yicha bepul — entity'lar odatdagidek
    /// seriyalanadi, faqat shu maydonlar yozilmaydi.
    /// </summary>
    public static void StripSecretProperties(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        for (var i = info.Properties.Count - 1; i >= 0; i--)
        {
            var p = info.Properties[i];
            if (IsSecretName(p.Name) || BulkyNames.Contains(p.Name) || p.PropertyType == typeof(byte[]))
                info.Properties.RemoveAt(i);
        }
    }

    /// <summary>JSON daraxtidan (ichma-ich obyekt/massivlar ham) maxfiy nomli maydonlarni olib tashlaydi.</summary>
    public static JsonNode? ScrubNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).Where(IsSecretName).ToList())
                    obj.Remove(key);
                foreach (var kv in obj.ToList())
                    ScrubNode(kv.Value);
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    ScrubNode(item);
                break;
        }
        return node;
    }

    /// <summary>
    /// JSON MATNIDAN (masalan <c>ArchivedRecord.Json</c>) maxfiy maydonlarni olib tashlaydi.
    /// ⚠️ Buzuq JSON xom holda QAYTARILMAYDI — bo'sh satr: ichida nima borligini bilmagan matnni
    /// tashqariga chiqarmaymiz.
    /// </summary>
    public static string ScrubJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            return ScrubNode(JsonNode.Parse(json))?.ToJsonString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>Obyektni JSON daraxtiga aylantirib, maxfiy maydonlarsiz qaytaradi (arxiv surati uchun —
    /// <see cref="ArchiveService.Snapshot"/> daraxtni odatdagidek seriyalaydi).</summary>
    public static JsonNode? ToScrubbedNode(object entity) =>
        ScrubNode(JsonSerializer.SerializeToNode(entity, entity.GetType()));
}
