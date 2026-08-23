using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Instagram AI sotuv agenti — bilim bazasi + kanal konteksti + suhbat tarixi → Gemini →
/// <see cref="IgAgentOutput"/>.
///
/// <para><b>Model chaqiruvi loyihadagi YAGONA nuqtadan</b> — <see cref="GeminiService"/>
/// (o'z <c>HttpClient</c>i yasalmaydi, kalit <c>.env</c> dan).</para>
///
/// <para><b>⚠️ AI ishlamasa jonli javob YUBORILMAYDI:</b> <c>Ok=false</c> qaytadi va pipeline
/// suhbatni <c>NeedsOperator</c> qilib qo'yadi. "Bir narsa yozib qo'yamiz" varianti YO'Q —
/// noto'g'ri narx yoki bo'sh va'da markazning haqiqiy zarari.</para>
///
/// <para><b>⚠️ MAXFIYLIK:</b> promptga faqat SUHBATNING O'ZI (mijoz yozgan matn, bizning
/// javoblarimiz, post caption'i) va bilim bazasi ketadi. CRM ma'lumotlari — o'quvchilar ro'yxati,
/// telefonlar, to'lovlar — HECH QACHON qo'shilmaydi (`.claude/rules/ai-analysis.md` dagi voronka
/// tahlili chegarasi bilan bir xil mantiq: suhbatdosh hali markazga tegishli emas).</para>
/// </summary>
public static class InstagramAgentService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Javob yaratadi. <paramref name="channel"/> — <c>comment</c> yoki <c>dm</c> (javob uslubi
    /// shunga qarab: izohga 1–2 gap, DM'da batafsil + telefon so'rash).
    /// </summary>
    public static async Task<(bool Ok, IgAgentOutput? Output, string Error)> AskAsync(
        IAppDbContext db, IConfiguration config, string channel, string username,
        string mediaCaption, string message, IReadOnlyList<IgMessage> history,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return (false, null, "Xabar matni bo'sh — AI'ga yuboriladigan narsa yo'q.");

        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return (false, null, "Gemini API kaliti sozlanmagan (.env: GEMINI_API_KEY) — AI javob bera olmaydi.");

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        // ⚠️ Bilim bazasi SAVOLGA QARAB tanlanadi (E6.5 — RAG). Savol berilmasa yoki RAG
        // ishlamasa AYNAN eski xatti-harakat qoladi (butun baza + `KnowledgeLimit`).
        var knowledge = await LoadKnowledgeAsync(db, ct, IgKnowledgeRag.QueryText(message, mediaCaption));

        var model = (meta?.InstagramAiModel ?? "").Trim();
        if (model.Length == 0) model = GeminiService.ResolveModel(config);

        var system = BuildSystemPrompt(knowledge, meta?.Name ?? "", meta?.InstagramGreeting ?? "");
        var context = BuildContext(channel, username, mediaCaption, message, history);

        var (ok, text, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, system + "\n\n" + context, jsonMode: true);
        if (!ok) return (false, null, err ?? "AI xatosi.");

        var parsed = ParseOutput(text);
        if (parsed is null || parsed.Reply.Length == 0)
            return (false, null, "AI javobini o'qib bo'lmadi (format xato) — operator javob bersin.");

        return (true, parsed, "");
    }

    /// <summary>
    /// Bilim bazasini promptga tayyorlaydi (faol bo'laklar, tartib bo'yicha).
    ///
    /// <para><b>E6.5 — RAG:</b> <paramref name="query"/> berilgan va bilim bazasi TAYYOR bo'lsa
    /// (<see cref="IgKnowledgeRag.CanUseRag"/>) savolga ma'nosi eng yaqin bir necha bo'lak
    /// tanlanadi. Aks holda — <b>eski xatti-harakat</b>: barcha faol bo'laklar
    /// <c>IgConst.KnowledgeLimit</c> gacha.</para>
    ///
    /// <para>🔴 <b>ZAXIRA YO'L HAR QANDAY XATODA:</b> kalit yo'q, embedding so'rovi yiqildi,
    /// vektorlar buzuq yoki hech bir bo'lak chegaradan o'tmadi — hamma holatda butun bilim
    /// bazasi qaytadi. RAG modulni HECH QACHON to'xtatib qo'ymaydi.</para>
    ///
    /// <para>⚠️ Tanlov SHU YERDA (DB + HTTP), <see cref="BuildSystemPrompt"/> esa SOF bo'lib
    /// qoladi — u tayyor matnni oladi va testlari o'zgarishsiz ishlaydi.</para>
    /// </summary>
    public static async Task<string> LoadKnowledgeAsync(
        IAppDbContext db, CancellationToken ct = default, string? query = null)
    {
        var items = await db.IgKnowledges.AsNoTracking()
            .Where(k => k.IsActive)
            .OrderBy(k => k.Order)
            .Select(k => new { k.Id, k.Title, k.Content, k.Order, k.EmbeddingJson })
            .ToListAsync(ct);

        var chunks = items
            .Select(k => new IgRagChunk(k.Id, k.Title, k.Content, k.Order, IgKnowledgeRag.ParseVector(k.EmbeddingJson)))
            .ToList();

        var selected = await TrySelectAsync(chunks, query, ct);
        return IgKnowledgeRag.Compose(selected ?? chunks, IgConst.KnowledgeLimit);
    }

    /// <summary>
    /// RAG tanlovi. <c>null</c> qaytishi «zaxira yo'lga o't» degani (savol yo'q, baza tayyor
    /// emas, kalit yo'q, so'rov yiqildi yoki mos bo'lak topilmadi).
    /// </summary>
    private static async Task<IReadOnlyList<IgRagChunk>?> TrySelectAsync(
        IReadOnlyList<IgRagChunk> chunks, string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        if (!IgKnowledgeRag.CanUseRag(chunks)) return null;
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey)) return null;

        var (ok, vector, _) = await IgEmbeddingService.EmbedAsync(
            AppSecrets.GeminiApiKey, IgEmbeddingService.DefaultModel, query,
            IgEmbeddingService.TaskQuery, ct);
        if (!ok) return null;

        var top = IgKnowledgeRag.TopMatches(chunks, vector);
        return top.Count > 0 ? top : null;
    }

    /* ═════════════════════════ PROMPT (sof funksiyalar) ═════════════════════════ */

    /// <summary>
    /// System prompt — persona + QOIDALAR + bilim bazasi. SOF funksiya (testlanadi).
    /// <para>Barqaror qism ataylab ajratilgan: u har chaqiruvda bir xil, o'zgaruvchisi esa
    /// <see cref="BuildContext"/> da.</para>
    /// </summary>
    public static string BuildSystemPrompt(string knowledge, string centerName, string greeting)
    {
        var center = string.IsNullOrWhiteSpace(centerName) ? "o'quv markazi" : centerName.Trim();
        var kb = (knowledge ?? "").Trim();
        var hello = string.IsNullOrWhiteSpace(greeting) ? IgConst.DefaultGreeting : greeting.Trim();

        var sb = new StringBuilder();
        sb.Append($"Sen — «{center}» o'quv markazining Instagram'dagi SOTUV yordamchisisan. ")
          .Append("Vazifang: savolga aniq javob berish, qiziqqan odamni ismi va telefoni bilan ")
          .Append("bog'lanishga olib kelish.\n\n");

        sb.Append("QOIDALAR:\n");
        // 🔴 1-QOIDA ATAYIN BIRINCHI: model suhbatning BOSHIDAGI mavzuga «yopishib» qolardi —
        // odam avval IELTS so'rab, keyin bolalar kursini so'rasa ham javob yana IELTS haqida
        // chiqardi. Sabab: tarix promptning KATTA qismi, oxirgi xabar esa bitta qator; ustiga
        // «qiziqqan odamni bog'lanishga olib kel» vazifasi modelni ANIQLANGAN qiziqishni
        // sotishga undaydi. Shuning uchun "javob NIMAGA yozilishi" ochiq aytiladi.
        sb.Append("1. JAVOB — MIJOZNING OXIRGI XABARIGA. Suhbat tarixi FAQAT kontekst: ")
          .Append("mijoz mavzuni o'zgartirgan bo'lsa YANGI mavzuga javob ber va eskisiga QAYTARMA. ")
          .Append("Oxirgi xabarda so'ralmagan kurs/xizmatni o'zing taklif qilma.\n");
        sb.Append("2. TIL VA YOZUV: mijoz qaysi tilda va qaysi ALIFBODA yozgan bo'lsa — AYNAN o'shanda javob ber. ")
          .Append("Kirill o'zbekcha → kirill; lotin o'zbekcha → lotin; rus tilida → rus tilida; ingliz → ingliz. ")
          .Append("Uslub jonli va samimiy, ortiqcha rasmiylik yo'q («hurmatli mijoz» deb yozma).\n");
        sb.Append("3. NARXNI VA SHARTLARNI O'YLAB TOPMA. Faqat quyidagi BILIM BAZASIdagi ma'lumotdan foydalan. ")
          .Append("Kerakli ma'lumot bilim bazasida BO'LMASA — taxmin qilma, ")
          .Append("`escalate_to_human` ni true qil va «operatorimiz tez orada bog'lanadi» deb yoz.\n");
        sb.Append("4. HAR JAVOBNI BOSHQACHA yoz — bir xil shablon takrorlansa Instagram uni spam deb belgilaydi.\n");
        sb.Append("5. Mijoz «operator», «odam», «menejer» bilan gaplashmoqchi bo'lsa — DARHOL `escalate_to_human=true`. ")
          .Append("Mijozni bot bilan gaplashishga majburlash TAQIQLANGAN.\n");
        sb.Append("6. QISQALIK: ochiq IZOHga 1–2 gap va DM'ga taklif (`move_to_dm=true`); ")
          .Append("shaxsiy xabarda (DM) batafsilroq yoz va TELEFON RAQAM so'ra.\n");
        sb.Append("7. BOT EKANINGNI YASHIRMA. Suhbatning BIRINCHI javobiga shu matnni qo'sh: «").Append(hello).Append("»\n");
        sb.Append("8. LEAD BAHOSI (`lead_score`): 0–30 salom-alik/spam/mavzudan tashqari; ")
          .Append("40–60 qiziqish bor (kurs haqida so'rayapti); ")
          .Append("70–100 xarid niyati (narx so'radi, «yozilaman», «kelaman», kontakt qoldirdi).\n");
        sb.Append("9. Mijoz telefon yoki boshqa aloqa qoldirsa — uni `lead_contact` ga AYNAN yoz. ")
          .Append("Ismini aytsa (masalan «Ali Valiyev 90 123 45 67») — `lead_name` ga FAQAT ismni yoz, ")
          .Append("raqamsiz. Raqam berilgan xabarda `lead_score` 70 dan kam bo'lmasin.\n");
        sb.Append("10. Shikoyat bo'lsa bahslashma: uzr so'ra va operatorga o'tkaz.\n");
        sb.Append("11. Javob 700 belgidan oshmasin, emoji 1–2 tadan ko'p bo'lmasin.\n\n");

        sb.Append("BILIM BAZASI:\n");
        sb.Append(kb.Length > 0
            ? kb
            : "(Bilim bazasi hali to'ldirilmagan — narx yoki jadval so'ralsa hech narsa o'ylab topma, operatorga o'tkaz.)");
        sb.Append("\n\n");

        sb.Append("FAQAT quyidagi JSON'ni qaytar (boshqa hech narsa yozma, izoh ham qo'shma):\n");
        sb.Append("{\n");
        sb.Append("  \"reply\": \"mijozga yuboriladigan matn\",\n");
        sb.Append("  \"language\": \"uz-Cyrl|uz-Latn|ru|en\",\n");
        sb.Append("  \"intent\": \"greeting|price_question|product_question|buying_intent|complaint|spam|other\",\n");
        sb.Append("  \"lead_score\": 0,\n");
        sb.Append("  \"is_hot_lead\": false,\n");
        sb.Append("  \"move_to_dm\": false,\n");
        sb.Append("  \"escalate_to_human\": false,\n");
        sb.Append("  \"lead_name\": \"\",\n");
        sb.Append("  \"lead_contact\": \"\",\n");
        sb.Append("  \"lead_product_interest\": \"\",\n");
        sb.Append("  \"lead_summary\": \"suhbat xulosasi — O'ZBEK TILIDA, 1 gap\"\n");
        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>O'zgaruvchan qism: kanal, mijoz, post matni, tarix va oxirgi xabar. SOF funksiya.</summary>
    public static string BuildContext(
        string channel, string username, string mediaCaption, string message, IReadOnlyList<IgMessage>? history)
    {
        var sb = new StringBuilder();
        sb.Append("[Kontekst]\n");
        sb.Append("Kanal: ").Append(channel == IgConst.ChannelComment ? "ochiq IZOH (post ostida)" : "shaxsiy xabar (DM)").Append('\n');
        if (!string.IsNullOrWhiteSpace(username)) sb.Append("Mijoz username: @").Append(username.Trim()).Append('\n');
        if (!string.IsNullOrWhiteSpace(mediaCaption))
            sb.Append("Post matni: ").Append(InstagramContract.Trim(mediaCaption, IgConst.MediaCaptionLimit)).Append('\n');

        if (history is { Count: > 0 })
        {
            sb.Append("\n[Suhbat tarixi — eskisidan yangisiga]\n");
            var take = history.Count > IgConst.DmHistoryLimit ? history.Count - IgConst.DmHistoryLimit : 0;
            for (var i = take; i < history.Count; i++)
            {
                var m = history[i];
                if (string.IsNullOrWhiteSpace(m.Text)) continue;
                sb.Append(m.Direction == IgConst.DirOut ? "Biz: " : "Mijoz: ")
                  .Append(InstagramContract.Trim(m.Text, 400)).Append('\n');
            }
        }

        // ⚠️ Oxirgi xabar promptning ENG OXIRIDA va ochiq nomlangan: tarix undan o'nlab marta
        // uzun bo'lishi mumkin va model «suhbat mavzusi» ni tarixdan olib, ostidagi savolni
        // yon kontekstga aylantirib qo'yardi (1-qoidaga qarang).
        sb.Append("\n[JAVOB YOZILADIGAN XABAR — mijozning oxirgi xabari]\n").Append(message.Trim());
        return sb.ToString();
    }

    /* ═════════════════════════ JAVOBNI O'QISH (sof) ═════════════════════════ */

    /// <summary>
    /// Gemini javobini <see cref="IgAgentOutput"/> ga aylantiradi. SOF funksiya (testlanadi).
    /// <para>Ikki bosqichli tozalash — loyihadagi <c>FunnelAiAnalysisService.ParseNarrative</c>
    /// bilan bir xil: ```json fence olib tashlanadi, so'ng birinchi <c>{</c> dan oxirgi <c>}</c>
    /// gacha kesiladi (model ba'zan matn qo'shib yuboradi).</para>
    /// <para>Diapazon va enum'lar SHU YERDA to'g'rilanadi (<c>lead_score</c> → 0..100, noma'lum
    /// <c>intent</c> → <c>other</c>) — structured output bu cheklovlarni qo'llab-quvvatlamaydi.</para>
    /// <para>Format buzuq bo'lsa <c>null</c> — chaqiruvchi jonli javob YUBORMAYDI.</para>
    /// </summary>
    public static IgAgentOutput? ParseOutput(string raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0) return null;

        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = t.IndexOf('\n');
            if (nl > 0) t = t[(nl + 1)..];
            var fence = t.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) t = t[..fence];
            t = t.Trim();
        }
        var open = t.IndexOf('{');
        var close = t.LastIndexOf('}');
        if (open < 0 || close <= open) return null;
        t = t[open..(close + 1)];

        RawOutput? r;
        try { r = JsonSerializer.Deserialize<RawOutput>(t, JsonOpts); }
        catch (JsonException) { return null; }
        if (r is null) return null;

        var reply = InstagramContract.Trim(r.Reply ?? "", IgConst.MaxReplyLength);
        return new IgAgentOutput(
            Reply: reply,
            Language: InstagramContract.NormalizeLanguage(r.Language),
            Intent: InstagramContract.NormalizeIntent(r.Intent),
            LeadScore: InstagramContract.ClampScore(r.LeadScore),
            IsHotLead: r.IsHotLead,
            MoveToDm: r.MoveToDm,
            EscalateToHuman: r.EscalateToHuman,
            LeadName: InstagramContract.Trim(r.LeadName ?? "", 100),
            LeadContact: InstagramContract.Trim(r.LeadContact ?? "", 100),
            LeadProductInterest: InstagramContract.Trim(r.LeadProductInterest ?? "", 120),
            LeadSummary: InstagramContract.Trim(r.LeadSummary ?? "", 400));
    }

    /// <summary>Gemini JSON'ining xom shakli (snake_case kalitlar — IG-SPEC §5.1 sxemasi).</summary>
    private sealed class RawOutput
    {
        [JsonPropertyName("reply")] public string? Reply { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("intent")] public string? Intent { get; set; }
        [JsonPropertyName("lead_score")] public int LeadScore { get; set; }
        [JsonPropertyName("is_hot_lead")] public bool IsHotLead { get; set; }
        [JsonPropertyName("move_to_dm")] public bool MoveToDm { get; set; }
        [JsonPropertyName("escalate_to_human")] public bool EscalateToHuman { get; set; }
        [JsonPropertyName("lead_name")] public string? LeadName { get; set; }
        [JsonPropertyName("lead_contact")] public string? LeadContact { get; set; }
        [JsonPropertyName("lead_product_interest")] public string? LeadProductInterest { get; set; }
        [JsonPropertyName("lead_summary")] public string? LeadSummary { get; set; }
    }
}
