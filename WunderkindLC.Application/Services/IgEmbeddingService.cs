using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// E6.5 — <b>EMBEDDING</b> (ma'no vektori) xizmati: Gemini <c>embedContent</c> chaqiruvi va
/// bilim bazasi bo'laklarini fonda vektorlashtirish.
///
/// <para><b>Nega alohida fayl:</b> <see cref="GeminiService"/> — matn generatsiyasining yagona
/// nuqtasi va unga tegilmadi. Embedding boshqa endpoint, boshqa javob sxemasi va boshqa
/// xatolik siyosati (yiqilsa modul ISHLAYVERADI) — shuning uchun o'z joyida turadi.</para>
///
/// <para><b>Ikki qism:</b> STATIK chaqiruv (<see cref="EmbedAsync"/>) — savol vektorini olish
/// uchun <see cref="InstagramAgentService"/> dan to'g'ridan-to'g'ri ishlatiladi (DI kerak emas,
/// <see cref="GeminiService"/> bilan bir xil naqsh); INSTANS qismi
/// (<see cref="EmbedPendingAsync"/>) — bazaga yozadi, shuning uchun Scoped servis.</para>
///
/// <para>🔴 <b>MODUL DARVOZASI:</b> <c>CenterMeta.InstagramEnabled == false</c> bo'lsa
/// <see cref="EmbedPendingAsync"/> tashqariga <b>HECH QANDAY so'rov yubormaydi</b>
/// («modul o'chiq — tashqariga hech narsa chiqmaydi» qoidasi, <c>MetaCapiService</c> bilan
/// bir xil).</para>
///
/// <para><b>DI (men qo'shaman):</b> <c>builder.Services.AddScoped&lt;IgEmbeddingService&gt;();</c></para>
/// <para><b>Worker (men ulayman):</b> <c>InstagramWorkerService.TickAsync</c> ichida,
/// <c>InstagramEnabled</c> darvozasi ostida, navbat tsiklidan sekinroq oraliqda:
/// <c>await sp.GetRequiredService&lt;IgEmbeddingService&gt;().EmbedPendingAsync(ct);</c></para>
/// </summary>
public sealed class IgEmbeddingService(
    IAppDbContext db,
    ILogger<IgEmbeddingService> logger)
{
    /* ═════════════════════════ Konstantalar ═════════════════════════ */

    /// <summary>Embedding modeli. ⚠️ <c>GEMINI_MODEL</c> (matn modeli) bilan ARALASHTIRILMAYDI:
    /// generatsiya modeli embedding endpointida ishlamaydi.
    /// <para>Yangi <c>.env</c> kaliti ATAYIN kiritilmadi — u <c>AppSecrets.EnvKeys</c> ga,
    /// <c>docker-compose.yml</c> ga va <c>.env.example</c> ga ham qo'shilishi kerak bo'lardi
    /// (<c>EnvKeysWiringTests</c>), model esa amalda o'zgarmaydi. Model almashsa shu konstanta
    /// yangilanadi va vektorlar o'z-o'zidan qayta hisoblanadi
    /// (<see cref="IgKnowledgeRag.NeedsEmbedding"/>).</para></summary>
    public const string DefaultModel = "gemini-embedding-001";

    /// <summary>
    /// 🔴 <b>2026-08-22: <c>text-embedding-004</c> Google tomonidan OLIB TASHLANDI.</b>
    ///
    /// <para>Prodda aniqlandi — har tsiklda <c>404: "models/text-embedding-004 is not found for
    /// API version v1beta, or is not supported for embedContent"</c> qaytardi. Oqibati JIMGINA
    /// edi: RAG hech qachon ishlamadi (vektor ustuni bo'sh qoldi), modul esa eski yo'lga
    /// qaytib ishlayverdi — ya'ni tashqaridan hech narsa buzilmagandek ko'rinardi, lekin bilim
    /// bazasi o'sganda promptning oxiri kesilib, AI "bilmayman" deya boshlardi.</para>
    ///
    /// <para>Amaldagi modellar (<c>GET /v1beta/models</c> bilan tekshirilgan):
    /// <c>gemini-embedding-001</c> (tanlangan — barqaror), <c>gemini-embedding-2</c>,
    /// <c>gemini-embedding-2-preview</c>. Ikkalasi ham 3072 o'lchamli vektor qaytaradi.</para>
    ///
    /// <para>⚠️ Model nomi o'zgarsa vektorlar O'ZI qayta hisoblanadi
    /// (<see cref="IgKnowledgeRag.NeedsEmbedding"/> <c>EmbeddingModel</c> ustunini solishtiradi) —
    /// qo'lda tozalash SHART EMAS.</para>
    /// </summary>
    private const string RetiredModelNote = "text-embedding-004 (2026-08 da olib tashlangan)";

    /// <summary>Vazifa turi — SAQLANADIGAN hujjat uchun.</summary>
    public const string TaskDocument = "RETRIEVAL_DOCUMENT";
    /// <summary>Vazifa turi — QIDIRUV so'rovi uchun.
    /// <para>⚠️ Ikkalasi HAR XIL bo'lishi shart: Gemini savol va hujjatni bir-biriga yaqinroq
    /// joylashtirish uchun aynan shu belgidan foydalanadi. Ikkovini ham "document" qilib
    /// yuborish o'xshashlikni sezilarli pasaytiradi.</para></summary>
    public const string TaskQuery = "RETRIEVAL_QUERY";

    /// <summary>Bitta tsiklda ko'pi bilan shuncha bo'lak vektorlashtiriladi — fon xizmatining
    /// bitta aylanishi cho'zilib ketmasin (navbat undan keyin turadi).</summary>
    public const int BatchPerTick = 5;

    /// <summary>Embedding'ga yuboriladigan matn chegarasi (belgi). Model chegarasidan ancha
    /// past — uzun bo'lak baribir bitta mavzuni ifodalaydi, dumi ma'noga ta'sir qilmaydi.</summary>
    public const int TextLimit = 8000;

    /// <summary>Gemini embedding endpointi (SDK'siz, REST — <see cref="GeminiService"/> kabi).</summary>
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    /// <summary>⚠️ Statik <c>HttpClient</c> — <see cref="GeminiService"/> dagi bilan bir xil
    /// yondashuv (typed client DI'siz ham chaqirilsin). Timeout qisqa: embedding — jonli
    /// javob yo'lidagi QO'SHIMCHA qadam, u sekinlashsa mijoz javobni kutib qolardi.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /* ═════════════════════════ Gemini chaqiruvi (statik) ═════════════════════════ */

    /// <summary>
    /// Matnni ma'no vektoriga aylantiradi.
    ///
    /// <para><b>Istisno OTMAYDI</b> — loyiha uslubi bo'yicha tuple qaytadi, xato matni
    /// o'zbekcha. Chaqiruvchi uchun <c>Ok=false</c> «zaxira yo'lga o't» degani.</para>
    /// </summary>
    public static async Task<(bool Ok, float[] Vector, string Error)> EmbedAsync(
        string? apiKey, string? model, string? text, string taskType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, Array.Empty<float>(), "Gemini API kaliti sozlanmagan.");

        var body = InstagramContract.Trim(text ?? "", TextLimit);
        if (body.Length == 0) return (false, Array.Empty<float>(), "Matn bo'sh — vektor hisoblanmaydi.");

        var m = string.IsNullOrWhiteSpace(model) ? DefaultModel : model!.Trim();

        try
        {
            var payload = new
            {
                // ⚠️ `model` maydonida "models/" prefiksi TALAB qilinadi (URL'dagi nomdan farqli).
                model = "models/" + m,
                content = new { parts = new[] { new { text = body } } },
                taskType,
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + m + ":embedContent");
            req.Headers.Add("x-goog-api-key", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, Array.Empty<float>(), $"Gemini embedding xatosi ({(int)resp.StatusCode}). {ExtractError(raw)}");

            var vector = ParseValues(raw);
            return vector.Length == 0
                ? (false, Array.Empty<float>(), "Gemini bo'sh vektor qaytardi.")
                : (true, vector, "");
        }
        catch (TaskCanceledException)
        {
            return (false, Array.Empty<float>(), "Embedding vaqti tugadi (timeout).");
        }
        catch (Exception ex)
        {
            return (false, Array.Empty<float>(), $"Embedding xatosi: {ex.Message}");
        }
    }

    /// <summary><c>{"embedding":{"values":[...]}}</c> dan vektorni ajratadi. Format kutilmagan
    /// bo'lsa bo'sh massiv (istisno emas) — javobni o'qiy olmaslik ham «zaxira yo'l» holati.</summary>
    private static float[] ParseValues(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("embedding", out var emb)) return [];
            if (!emb.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array) return [];

            var len = vals.GetArrayLength();
            if (len == 0 || len > IgKnowledgeRag.MaxDims) return [];

            var result = new float[len];
            var i = 0;
            foreach (var v in vals.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d)) return [];
                result[i++] = (float)d;
            }
            return result;
        }
        catch (JsonException) { return []; }
    }

    private static string ExtractError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "Kalit/model to'g'riligini tekshiring.";
        }
        catch (JsonException) { /* javob JSON bo'lmasligi mumkin */ }
        return "Kalit/model to'g'riligini tekshiring.";
    }

    /* ═════════════════════════ Fon vazifasi ═════════════════════════ */

    /// <summary>
    /// Vektori yo'q (yoki eskirgan) bilim bazasi bo'laklaridan <see cref="BatchPerTick"/>
    /// tasini hisoblab bazaga yozadi.
    ///
    /// <para>Chaqiriladigan joy — mavjud <c>InstagramWorkerService</c> (yangi
    /// <c>BackgroundService</c> YARATILMAYDI, §2.3).</para>
    ///
    /// <para><b>Darvozalar (shu tartibda):</b> modul yoqilganmi → Gemini kaliti bormi →
    /// hisoblanmagan bo'lak bormi. Hech biri bajarilmasa tashqariga so'rov ketmaydi.</para>
    ///
    /// <para>⚠️ Birinchi XATODA tsikl TO'XTAYDI: kalit noto'g'ri yoki kvota tugagan bo'lsa
    /// qolgan bo'laklarni urinib ko'rish faqat bekorga so'rov sarflardi. Keyingi tsiklda
    /// qaytadan urinib ko'riladi (bo'lak «hisoblanmagan» bo'lib qolaveradi).</para>
    /// </summary>
    /// <returns>Nechtasi hisoblandi va nechtasida xato bo'ldi.</returns>
    public async Task<(int Done, int Failed)> EmbedPendingAsync(CancellationToken ct = default)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        if (meta is null || !meta.InstagramEnabled) return (0, 0);
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey)) return (0, 0);

        // ⚠️ Kuzatilgan holda (AsNoTracking YO'Q) — qatorlar shu yerda yangilanadi.
        // Faol bo'laklar o'nlab, shuning uchun hammasi o'qiladi: "qaysi biri eskirgan" savoliga
        // javob HASH bo'yicha beriladi va uni SQL'da hisoblab bo'lmaydi.
        var items = await db.IgKnowledges.Where(k => k.IsActive).OrderBy(k => k.Order).ToListAsync(ct);

        var pending = items
            .Where(k => IgKnowledgeRag.NeedsEmbedding(
                k.EmbeddingJson, k.EmbeddedHash, k.EmbeddingModel, k.Title, k.Content, DefaultModel))
            .Take(BatchPerTick)
            .ToList();
        if (pending.Count == 0) return (0, 0);

        var done = 0;
        var failed = 0;
        foreach (var k in pending)
        {
            // Matnsiz bo'lak `NeedsEmbedding` da allaqachon chetlab o'tilgan — bu yerga tushmaydi.
            var text = (k.Title + "\n" + k.Content).Trim();

            var (ok, vector, err) = await EmbedAsync(AppSecrets.GeminiApiKey, DefaultModel, text, TaskDocument, ct);
            if (!ok)
            {
                failed++;

                // ⚠️ MODEL NOMI ESKIRGAN holati ALOHIDA va ERROR darajasida.
                //
                // Sabab: RAG "yumshoq" degradatsiya qiladi — vektor bo'lmasa modul eski yo'l
                // bilan ishlayveradi. Ya'ni bu nosozlik tashqaridan KO'RINMAYDI va Warning
                // darajasidagi qator loglar orasida yo'qolib ketadi. 2026-08-22 da aynan
                // shunday bo'ldi: `text-embedding-004` olib tashlangan, RAG esa hech qachon
                // yoqilmagan. Xabar NIMA QILISH kerakligini ochiq aytadi.
                if (err.Contains("404") || err.Contains("is not found", StringComparison.OrdinalIgnoreCase))
                    logger.LogError(
                        "Instagram bilim bazasi: EMBEDDING MODELI TOPILMADI ({Model}) — RAG ishlamaydi. "
                        + "Google model nomini o'zgartirgan bo'lishi mumkin ({Retired} bilan aynan shunday bo'lgan). "
                        + "Amaldagi ro'yxat: GET https://generativelanguage.googleapis.com/v1beta/models — "
                        + "so'ng IgEmbeddingService.DefaultModel yangilanadi (vektorlar o'zi qayta hisoblanadi). "
                        + "Xato: {Error}",
                        DefaultModel, RetiredModelNote, err);
                else
                    logger.LogWarning("Instagram bilim bazasi: «{Title}» uchun vektor hisoblanmadi — {Error}", k.Title, err);

                break;   // qolganini keyingi tsiklda
            }

            k.EmbeddingJson = IgKnowledgeRag.SerializeVector(vector);
            k.EmbeddingModel = DefaultModel;
            k.EmbeddedHash = IgKnowledgeRag.ContentHash(k.Title, k.Content);
            k.EmbeddedAt = AppClock.Iso();
            done++;
        }

        // Birinchi urinish yiqilgan bo'lsa o'zgargan qator yo'q — bu shunchaki bo'sh amal.
        if (done > 0) await db.SaveChangesAsync(ct);
        if (done > 0) logger.LogInformation("Instagram bilim bazasi: {Done} ta bo'lak vektorlashtirildi", done);
        return (done, failed);
    }
}
