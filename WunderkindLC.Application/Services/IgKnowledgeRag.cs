using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Bilim bazasi bo'lagi + uning ma'no vektori (RAG uchun). Bazadan ajratilgan SOF ko'rinish —
/// shu sabab tanlash mantig'i HTTP va DB'siz testlanadi.
/// </summary>
/// <param name="Vector">Bo'sh massiv = vektor hali hisoblanmagan (bo'lak RAG'ga kirmaydi).</param>
public sealed record IgRagChunk(string Id, string Title, string Content, int Order, float[] Vector);

/// <summary>
/// E6.5 — <b>BILIM BAZASI RAG</b>: butun bilim bazasini promptga tiqish o'rniga savolga
/// MA'NOSI yaqin bir necha bo'lakni tanlaydi.
///
/// <para><b>Muammo:</b> ilgari <c>LoadKnowledgeAsync</c> barcha faol bo'laklarni ketma-ket
/// qo'shib, natijani <c>IgConst.KnowledgeLimit</c> (12000 belgi) da KESARDI. Bilim bazasi
/// o'sganda oxirgi bo'laklar promptga umuman tushmasdi va AI "bunday ma'lumot yo'q" deb
/// operatorga o'tkazardi — nosozlik jimgina, faqat "AI bilmayapti" shikoyati orqali ko'rinardi.</para>
///
/// <para><b>Yechim:</b> har bo'lakning Gemini embedding vektori saqlanadi
/// (<c>IgKnowledge.EmbeddingJson</c>), savol ham vektorga aylantiriladi va
/// <see cref="Cosine"/> bo'yicha eng yaqin <see cref="TopN"/> bo'lak tanlanadi.</para>
///
/// <para>🔴 <b>YANGI KUTUBXONA YO'Q</b> (<c>pgvector</c> ham). Vektor JSON matn sifatida
/// saqlanadi, kosinus oddiy C# tsiklida hisoblanadi: bilim bazasi o'nlab bo'lakdan iborat,
/// ya'ni bitta so'rovda bir necha ming ko'paytirish — o'lchanadigan yuk emas.</para>
///
/// <para>🔴 <b>ZAXIRA YO'L MAJBURIY:</b> vektor yo'q, buzuq yoki o'lchamlari mos kelmasa
/// tanlov bo'sh qaytadi va chaqiruvchi ESKI xatti-harakatga (butun bilim bazasi +
/// <c>KnowledgeLimit</c>) qaytadi. RAG yiqilsa modul ishlashda DAVOM ETADI.</para>
/// </summary>
public static class IgKnowledgeRag
{
    /* ═════════════════════════ Sozlamalar ═════════════════════════ */

    /// <summary>Promptga tanlanadigan bo'laklar soni (topshiriqdagi 5–8 oralig'ining o'rtasi).
    /// <para>Kamroq bo'lsa yonma-yon mavzular (masalan "narx" va "chegirma") tushib qolardi,
    /// ko'proq bo'lsa RAG'ning ma'nosi (promptni qisqartirish) yo'qolardi.</para></summary>
    public const int TopN = 6;

    /// <summary>Kosinus shundan past bo'lsa bo'lak "mavzuga aloqasiz" hisoblanadi.
    /// <para>⚠️ Chegara ATAYIN past: RAG'da eng qimmat xato — kerakli bo'lakni TASHLAB YUBORISH.
    /// Ortiqcha bo'lak promptni biroz uzaytiradi, xolos.</para></summary>
    public const double MinScore = 0.20;

    /// <summary>Vektorda ko'pi bilan shuncha o'lcham o'qiladi (buzuq/ulkan JSON xotirani
    /// yeb qo'ymasin). Gemini <c>text-embedding-004</c> — 768.</summary>
    public const int MaxDims = 4096;

    /* ═════════════════════════ Vektor ↔ JSON (sof) ═════════════════════════ */

    /// <summary>Vektorni saqlash uchun JSON massivga aylantiradi. Bo'sh/`null` → bo'sh satr
    /// (ustunda "vektor yo'q" ayni shu bilan ifodalanadi).</summary>
    public static string SerializeVector(IReadOnlyList<float>? vector)
    {
        if (vector is null || vector.Count == 0) return "";

        var sb = new StringBuilder(vector.Count * 8);
        sb.Append('[');
        for (var i = 0; i < vector.Count; i++)
        {
            if (i > 0) sb.Append(',');
            // ⚠️ InvariantCulture: server mintaqasi vergulli o'nlik ishlatsa JSON buzilardi
            // ("0,12" — massivda ikkita son bo'lib o'qilardi).
            sb.Append(vector[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Saqlangan JSON'ni vektorga qaytaradi. <b>Hech qachon istisno otmaydi</b> — buzuq qiymat
    /// bo'sh massiv bo'lib qaytadi va bo'lak shunchaki RAG'dan tushib qoladi (zaxira yo'l).
    /// </summary>
    public static float[] ParseVector(string? json)
    {
        var t = (json ?? "").Trim();
        if (t.Length == 0 || t[0] != '[') return [];

        try
        {
            using var doc = JsonDocument.Parse(t);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var len = doc.RootElement.GetArrayLength();
            if (len == 0 || len > MaxDims) return [];

            var result = new float[len];
            var i = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out var d)) return [];
                if (double.IsNaN(d) || double.IsInfinity(d)) return [];
                result[i++] = (float)d;
            }
            return result;
        }
        catch (JsonException) { return []; }
    }

    /* ═════════════════════════ Kosinus (sof) ═════════════════════════ */

    /// <summary>
    /// Ikki vektor orasidagi kosinus o'xshashligi. Diapazon −1..1, embedding vektorlarida
    /// amalda 0..1.
    ///
    /// <para>⚠️ <b>Yiqilmaydi:</b> bo'sh, `null` yoki TURLI O'LCHAMDAGI vektorlar uchun
    /// <c>0</c> qaytadi (istisno emas). Turli o'lcham — model almashganining belgisi; bunday
    /// bo'lak jimgina chetlab o'tiladi, butun javob esa buzilmaydi.</para>
    /// </summary>
    public static double Cosine(float[]? a, float[]? b)
    {
        if (a is null || b is null || a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            double x = a[i], y = b[i];
            dot += x * y;
            na += x * x;
            nb += y * y;
        }
        if (na <= 0 || nb <= 0) return 0;   // nol vektor — bo'linish xatosi bo'lmasin

        var v = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
        return Math.Clamp(v, -1, 1);
    }

    /* ═════════════════════════ Tanlash (sof) ═════════════════════════ */

    /// <summary>
    /// RAG'ni umuman ishlatish mumkinmi.
    ///
    /// <para>Ikki shart: (1) bo'laklar soni <see cref="TopN"/> dan KO'P — aks holda hammasini
    /// yuborish ham arzon, ham xatosiz; (2) <b>HAR BIR</b> faol bo'lakning vektori bor.</para>
    ///
    /// <para>⚠️ (2) ATAYIN qat'iy: yangi qo'shilgan, hali embedding qilinmagan bo'lak aynan
    /// savolga javob bo'lishi mumkin. Yarim tayyor bazada RAG ishlatilsa u JIMGINA tashlab
    /// ketilardi. Fon xizmati bir necha soniyada yetib olgach RAG o'zi yoqiladi.</para>
    /// </summary>
    public static bool CanUseRag(IReadOnlyList<IgRagChunk>? chunks)
    {
        if (chunks is null || chunks.Count <= TopN) return false;
        foreach (var c in chunks)
            if (c.Vector.Length == 0) return false;
        return true;
    }

    /// <summary>
    /// Savol vektoriga eng yaqin bo'laklar — o'xshashlik bo'yicha KAMAYISH tartibida.
    ///
    /// <para>Teng ballda tartib <c>Order</c> → <c>Id</c> bo'yicha barqarorlashtiriladi:
    /// bir xil kirishda natija doim bir xil bo'lsin (test va diagnostika uchun).</para>
    ///
    /// <para>Hech biri <paramref name="minScore"/> dan o'tmasa <b>bo'sh ro'yxat</b> qaytadi —
    /// chaqiruvchi uchun bu "zaxira yo'lga o't" signali.</para>
    /// </summary>
    public static IReadOnlyList<IgRagChunk> TopMatches(
        IReadOnlyList<IgRagChunk>? chunks, float[]? query, int topN = TopN, double minScore = MinScore)
    {
        if (chunks is null || chunks.Count == 0 || query is null || query.Length == 0) return [];
        if (topN <= 0) return [];

        var scored = new List<(IgRagChunk Chunk, double Score)>(chunks.Count);
        foreach (var c in chunks)
        {
            var s = Cosine(c.Vector, query);
            if (s >= minScore) scored.Add((c, s));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.Order)
            .ThenBy(x => x.Chunk.Id, StringComparer.Ordinal)
            .Take(topN)
            .Select(x => x.Chunk)
            .ToList();
    }

    /// <summary>
    /// Bo'laklarni prompt matniga yig'adi — <b>AYNAN eski format</b>
    /// (<c>## Sarlavha\nMatn\n\n</c>) va AYNAN eski chegara mantiqi, aks holda RAG yoqilgan
    /// markazda promptning ko'rinishi sababsiz o'zgarardi.
    ///
    /// <para>Tartib — <c>Order</c> bo'yicha (bilim bazasidagi "muhimi yuqorida" qoidasi), ball
    /// bo'yicha EMAS: operator bilim bazasini o'sha tartibda ko'radi va promptdagi tartib unga
    /// mos tursin.</para>
    /// </summary>
    public static string Compose(IEnumerable<IgRagChunk>? chunks, int limit)
    {
        var sb = new StringBuilder();
        foreach (var k in (chunks ?? Enumerable.Empty<IgRagChunk>()).OrderBy(c => c.Order).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            if (sb.Length >= limit) break;
            sb.Append("## ").Append(k.Title).Append('\n').Append(k.Content).Append("\n\n");
        }
        return InstagramContract.Trim(sb.ToString(), limit);
    }

    /* ═════════════════════════ Embedding navbati (sof) ═════════════════════════ */

    /// <summary>Bo'lak MATNINING barmoq izi (SHA-256, hex). Sarlavha ham kiradi — u ham
    /// promptga tushadi va ma'noga ta'sir qiladi.</summary>
    public static string ContentHash(string? title, string? content)
    {
        var text = ((title ?? "").Trim() + "\n" + (content ?? "").Trim());
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Bo'lakni (qayta) embedding qilish kerakmi. To'rt sabab: vektor yo'q · buzuq · matn
    /// o'zgargan · model almashgan.
    /// <para>⚠️ Model almashganda qayta hisoblash SHART: har xil modelning vektorlari boshqa
    /// fazoda yotadi va ularni taqqoslash ma'nosiz natija berardi (o'lcham mos kelib qolsa
    /// xato ham chiqmasdi — eng yomon holat).</para>
    /// </summary>
    public static bool NeedsEmbedding(
        string? embeddingJson, string? embeddedHash, string? embeddingModel,
        string? title, string? content, string currentModel)
    {
        // ⚠️ Matni umuman yo'q bo'lak HECH QACHON navbatga tushmaydi: uni embedding qilib
        // bo'lmaydi (Gemini bo'sh matnni rad etadi), navbatda qolsa esa har tsiklda qayta
        // urinilib, boshqa bo'laklarni surib qo'yardi.
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content)) return false;

        if (string.IsNullOrWhiteSpace(embeddingJson)) return true;
        if (ParseVector(embeddingJson).Length == 0) return true;
        if (!string.Equals((embeddingModel ?? "").Trim(), currentModel.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return !string.Equals((embeddedHash ?? "").Trim(), ContentHash(title, content), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Savol matnidan qidiruv so'rovini quradi. Post matni (caption) izohlarda kontekst beradi
    /// ("bu qaysi kurs haqidagi post"), lekin u UZUN bo'lishi mumkin va savolni "bo'g'ib"
    /// qo'yardi — shuning uchun qisqartiriladi va xabardan KEYIN turadi.
    /// </summary>
    public static string QueryText(string? message, string? mediaCaption, int limit = 1200)
    {
        var msg = (message ?? "").Trim();
        var cap = InstagramContract.Trim(mediaCaption ?? "", 200);
        var q = cap.Length > 0 ? msg + "\n" + cap : msg;
        return InstagramContract.Trim(q, limit);
    }
}
