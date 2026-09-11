using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KONTENT REJALASHTIRISH → <b>AI BILAN CAPTION YOZISH</b> (§5.10).
///
/// <para>Foydalanuvchi MAVZU yozadi ("ingliz tili yozgi kurs, chegirma"), servis esa markazning
/// BILIM BAZASI (<c>IgKnowledge</c>) asosida post matnini va hashtaglarni qaytaradi.</para>
///
/// <para><b>Model chaqiruvi loyihadagi YAGONA nuqtadan</b> — <see cref="GeminiService"/>
/// (yangi provayder = yangi kalit = yangi billing; bu TAQIQLANGAN). Bilim bazasi ham yangidan
/// o'qilmaydi: <see cref="InstagramAgentService.LoadKnowledgeAsync"/> ishlatiladi, ya'ni AI
/// agenti va caption generatori AYNAN bir xil ma'lumotni ko'radi.</para>
///
/// <para><b>🔴 ASOSIY QOIDA — NATIJA CHEGARALARGA SOLISHTIRILADI.</b> AI qaytargan matn
/// to'g'ridan-to'g'ri foydalanuvchiga berilmaydi: u
/// <see cref="InstagramPublishContract.ValidateCaption"/> chegaralariga (2200 belgi, 30
/// hashtag, 20 mention) moslanadi (<see cref="Finalize"/>). Aks holda foydalanuvchi AI matnini
/// maydonga qo'yib, saqlashda «Matn juda uzun» (Meta kodi <c>2207010</c>) xatosini olardi —
/// ya'ni yordamchi tugma muammo yasab bergan bo'lardi.</para>
///
/// <para><b>⚠️ MAXFIYLIK:</b> promptga faqat markaz nomi, bilim bazasi va foydalanuvchi yozgan
/// MAVZU ketadi. CRM ma'lumotlari — o'quvchilar, telefonlar, to'lovlar — qo'shilmaydi
/// (<c>InstagramAgentService</c> va <c>.claude/rules/ai-analysis.md</c> bilan bir xil chegara).</para>
///
/// <para><b>⚠️ AUDITGA YOZILMAYDI:</b> matn yaratish hech qanday ma'lumotni o'zgartirmaydi
/// (<c>audit.md</c> §3.5 dagi "AI tahlili" istisnosi bilan bir xil).</para>
/// </summary>
public static class InstagramCaptionService
{
    /* ═════════════════════════ Konstantalar ═════════════════════════ */

    /// <summary>Mavzu matni promptga shuncha belgidan ko'p ketmaydi (uzun matn faqat pul sarflaydi).</summary>
    public const int MaxTopicLength = 400;

    /// <summary>
    /// AI'dan SO'RALADIGAN hashtag soni.
    /// <para>⚠️ Chegara (<see cref="IgPublishConst.MaxHashtags"/> = 30) dan ATAYIN ancha past:
    /// 30 ta hashtag Instagram'da "spam" ko'rinadi, qolaversa model ortiqcha yozib yuborsa ham
    /// <see cref="Finalize"/> qirqishi kerak bo'lmaydi.</para>
    /// </summary>
    public const int WantedHashtags = 12;

    /// <summary>
    /// AI'dan so'raladigan matn uzunligi (belgi).
    /// <para>⚠️ Chegara 2200, biz esa 1400 so'raymiz — zaxira ATAYIN: model uzunlikni aniq
    /// hisoblay olmaydi va biroz oshirib yuborishi odatiy hol. Zaxira bo'lmasa har uchinchi
    /// natija qirqilardi.</para>
    /// </summary>
    public const int TargetCaptionLength = 1400;

    /// <summary>Bitta hashtag shuncha belgidan oshmaydi (Instagram uzun tegni qabul qilmaydi).</summary>
    public const int MaxHashtagLength = 100;

    /* ═════════════════════════ Uslub (tone) ═════════════════════════ */

    public const string ToneFriendly = "friendly";
    public const string ToneExpert = "expert";
    public const string ToneEnergetic = "energetic";
    public const string ToneSales = "sales";

    /// <summary>Ruxsat etilgan uslublar. ⚠️ Frontenddagi <c>IG_CAPTION_TONES</c> kalitlari
    /// AYNAN shular bo'lishi shart (yorliqlar frontendda, qoida shu yerda).</summary>
    public static readonly string[] Tones = { ToneFriendly, ToneExpert, ToneEnergetic, ToneSales };

    public const string DefaultTone = ToneFriendly;

    /// <summary>Noma'lum/bo'sh uslub → <see cref="DefaultTone"/> (so'rov xato kalit tufayli
    /// yiqilmasin — foydalanuvchi baribir matnni ko'radi va o'zi tuzatadi).</summary>
    public static string NormalizeTone(string? v)
    {
        var s = (v ?? "").Trim().ToLowerInvariant();
        foreach (var t in Tones) if (t == s) return t;
        return DefaultTone;
    }

    /* ═════════════════════════ Asosiy oqim ═════════════════════════ */

    /// <summary>
    /// Mavzudan post matni yasaydi.
    /// </summary>
    /// <returns><c>Ok=false</c> bo'lsa <c>Error</c> — foydalanuvchiga KO'RSATILADIGAN o'zbekcha sabab.</returns>
    public static async Task<(bool Ok, string Caption, List<string> Hashtags, string Error)> GenerateAsync(
        IAppDbContext db, IConfiguration config,
        string? postType, string? topic, string? language, string? tone,
        CancellationToken ct = default)
    {
        var subject = (topic ?? "").Trim();
        if (subject.Length == 0)
            return (false, "", [], "Mavzu yozilmagan — AI nima haqida yozishini bilmaydi.");

        // ⚠️ Kalit tekshiruvi TARMOQQA CHIQISHDAN OLDIN: sozlanmagan kalit bilan so'rov yuborish
        // 90 soniya kutib, tushunarsiz xato qaytarardi.
        if (!AppSecrets.GeminiConfigured)
            return (false, "", [], "Gemini API kaliti sozlanmagan (.env: GEMINI_API_KEY) — AI matn yoza olmaydi.");

        var type = InstagramPublishContract.NormalizePostType(postType);
        var lang = InstagramContract.NormalizeLanguage(language);
        var style = NormalizeTone(tone);

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var knowledge = await InstagramAgentService.LoadKnowledgeAsync(db, ct);

        // Model AI agenti bilan bir xil manbadan: avval markaz sozlamasi, keyin env.
        var model = (meta?.InstagramAiModel ?? "").Trim();
        if (model.Length == 0) model = GeminiService.ResolveModel(config);

        var prompt = BuildPrompt(
            knowledge, meta?.Name ?? "", type,
            InstagramContract.Trim(subject, MaxTopicLength), lang, style);

        var (ok, text, err) = await GeminiService.GenerateAsync(
            AppSecrets.GeminiApiKey, model, prompt, jsonMode: true);
        if (!ok) return (false, "", [], err ?? "AI xatosi.");

        var draft = ParseDraft(text);
        if (draft is null)
            return (false, "", [], "AI javobini o'qib bo'lmadi (format xato) — qaytadan urinib ko'ring.");

        return Finalize(draft.Caption, draft.Hashtags);
    }

    /* ═════════════════════════ PROMPT (sof funksiya) ═════════════════════════ */

    /// <summary>
    /// To'liq prompt: persona + qoidalar + bilim bazasi + mavzu + JSON sxemasi. SOF funksiya
    /// (testlanadi).
    /// <para>Tuzilma <see cref="InstagramAgentService.BuildSystemPrompt"/> naqshida — bir xil
    /// uslub, bir xil "hech narsa o'ylab topma" qoidasi.</para>
    /// </summary>
    public static string BuildPrompt(
        string knowledge, string centerName, string postType, string topic, string language, string tone)
    {
        var center = string.IsNullOrWhiteSpace(centerName) ? "o'quv markazi" : centerName.Trim();
        var kb = (knowledge ?? "").Trim();
        var type = InstagramPublishContract.NormalizePostType(postType);

        var sb = new StringBuilder();
        sb.Append($"Sen — «{center}» o'quv markazining Instagram uchun MATN YOZUVCHISISAN (SMM copywriter). ")
          .Append("Vazifang: berilgan mavzu bo'yicha post matnini (caption) va hashtaglarni yozish.\n\n");

        sb.Append("QOIDALAR:\n");
        sb.Append("1. TIL VA YOZUV: matnni AYNAN shu tilda yoz — ").Append(LanguageName(language)).Append(".\n");
        sb.Append("2. USLUB: ").Append(ToneInstruction(tone)).Append('\n');
        sb.Append("3. POST TURI: ").Append(PostTypeHint(type)).Append('\n');
        sb.Append("4. NARX, JADVAL, MUDDAT VA SHARTLARNI O'YLAB TOPMA. Faqat quyidagi BILIM BAZASIdagi ")
          .Append("ma'lumotdan foydalan. Bilim bazasida yo'q raqamni matnga YOZMA — uning o'rniga ")
          .Append("«batafsil ma'lumot uchun yozing» de.\n");
        sb.Append("5. MATN UZUNLIGI: ko'pi bilan ").Append(TargetCaptionLength)
          .Append(" belgi. Qisqa xatboshilar, o'qish oson bo'lsin.\n");
        sb.Append("6. HASHTAGLARNI MATN ICHIGA YOZMA — ular ALOHIDA `hashtags` massivida qaytadi. ")
          .Append("Ko'pi bilan ").Append(WantedHashtags).Append(" ta, har biri `#` bilan boshlanadi, ")
          .Append("ichida bo'sh joy bo'lmaydi.\n");
        // ⚠️ Mention TAQIQ: begona akkauntni teglash markaz nomidan spam bo'lib ketadi, qolaversa
        // Instagram'da mention chegarasi bor (20) va uni AI bilmasdan oshirib yuborardi.
        sb.Append("7. `@mention` (birovning akkauntini teglash) YOZMA — hech qanday username qo'shma.\n");
        sb.Append("8. Emoji 2–5 tadan ko'p bo'lmasin; CAPS LOCK bilan baqirma.\n");
        sb.Append("9. Oxirida BITTA aniq harakatga chaqiruv (CTA) bo'lsin: yozing / qo'ng'iroq qiling / ")
          .Append("profildagi havolaga o'ting.\n");
        sb.Append("10. Va'da berma («albatta o'rganasiz», «100% natija») — bu ishonchni yo'qotadi.\n\n");

        sb.Append("BILIM BAZASI:\n");
        sb.Append(kb.Length > 0
            ? kb
            : "(Bilim bazasi hali to'ldirilmagan — narx, jadval yoki chegirma haqida hech narsa o'ylab topma.)");
        sb.Append("\n\n");

        sb.Append("MAVZU (foydalanuvchi yozgani):\n").Append(topic.Trim()).Append("\n\n");

        sb.Append("FAQAT quyidagi JSON'ni qaytar (boshqa hech narsa yozma, izoh ham qo'shma):\n");
        sb.Append("{\n");
        sb.Append("  \"caption\": \"post matni — HASHTAGSIZ\",\n");
        sb.Append("  \"hashtags\": [\"#misol\", \"#ikkinchi\"]\n");
        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>Til kalitining odam o'qiydigan nomi (promptga tushadi).</summary>
    public static string LanguageName(string? language) => InstagramContract.NormalizeLanguage(language) switch
    {
        "uz-Cyrl" => "ўзбек тилида, КИРИЛЛ алифбосида",
        "ru" => "rus tilida",
        "en" => "ingliz tilida",
        _ => "o'zbek tilida, LOTIN alifbosida",
    };

    /// <summary>Uslub ko'rsatmasi (promptga tushadi).</summary>
    public static string ToneInstruction(string? tone) => NormalizeTone(tone) switch
    {
        ToneExpert => "ishonchli va mutaxassislarcha — dalil va aniq faktlarga tayan, ortiqcha hayajon yo'q.",
        ToneEnergetic => "jonli va energiyali — qisqa gaplar, harakatga undash, lekin baqirmasdan.",
        ToneSales => "sotuvga yo'naltirilgan — foydani aniq ko'rsat, cheklovni (joy soni, muddat) esla, "
                     + "lekin BILIM BAZASIDA yo'q cheklovni O'YLAB TOPMA.",
        _ => "samimiy va iliq — ota-ona bilan gaplashayotgandek, oddiy so'zlar bilan.",
    };

    /// <summary>Post turiga qarab matn shakli (birinchi gap, uzunlik, ohang).</summary>
    public static string PostTypeHint(string? postType) => InstagramPublishContract.NormalizePostType(postType) switch
    {
        IgPublishConst.TypeReels =>
            "Reels — birinchi gap 3 soniyada ushlab qoladigan «ilmoq» (hook) bo'lsin, matn qisqa.",
        IgPublishConst.TypeVideo =>
            "Video — birinchi gap videoning mazmunini ochsin, matn o'rtacha uzunlikda.",
        IgPublishConst.TypeStory =>
            "Story — juda qisqa (1–2 gap): Story matni ekranda ko'rinmaydi, u faqat ichki eslatma.",
        IgPublishConst.TypeCarousel =>
            "Karusel — matn barcha slaydlarni bir joyda umumlashtirsin (slaydlarga alohida matn yozilmaydi).",
        _ => "Lentaga rasm — birinchi gap diqqatni tortsin, keyin 2–4 qisqa xatboshi.",
    };

    /* ═════════════════════════ JAVOBNI O'QISH (sof) ═════════════════════════ */

    /// <summary>AI qaytargan xom JSON — tozalanmagan holida.</summary>
    public sealed record IgCaptionDraft(string Caption, List<string> Hashtags);

    /// <summary>
    /// Gemini javobini o'qiydi. SOF funksiya (testlanadi).
    /// <para>Tozalash <see cref="InstagramAgentService.ParseOutput"/> bilan AYNAN bir xil:
    /// ```json fence olib tashlanadi, so'ng birinchi <c>{</c> dan oxirgi <c>}</c> gacha
    /// kesiladi (model ba'zan matn qo'shib yuboradi).</para>
    /// <para>Format buzuq bo'lsa <c>null</c> — chaqiruvchi foydalanuvchiga xato ko'rsatadi
    /// ("yarim matn"ni maydonga qo'yib qo'yish yomonroq bo'lardi).</para>
    /// </summary>
    public static IgCaptionDraft? ParseDraft(string? raw)
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

        RawCaption? r;
        try { r = JsonSerializer.Deserialize<RawCaption>(t, JsonOpts); }
        catch (JsonException) { return null; }
        if (r is null) return null;

        return new IgCaptionDraft(r.Caption ?? "", r.Hashtags ?? []);
    }

    /* ═════════════════════════ CHEGARALARNI QO'LLASH (sof) ═════════════════════════ */

    /// <summary>
    /// AI natijasini <see cref="InstagramPublishContract.ValidateCaption"/> chegaralariga
    /// SOLISHTIRADI va yakuniy matnni yig'adi. SOF funksiya (testlanadi).
    ///
    /// <para><b>Qaytadigan <c>Caption</c> — TAYYOR matn: hashtaglar allaqachon oxiriga
    /// qo'shilgan.</b> Ya'ni frontend uni maydonga shundoq qo'yadi. <c>Hashtags</c> ro'yxati
    /// esa faqat KO'RSATISH uchun qaytadi (chiplar) — uni matnga QAYTA qo'shish takror
    /// bo'lardi.</para>
    ///
    /// <para>Tartib ATAYIN shunday:
    /// <list type="number">
    /// <item>mention chegarasidan oshgan matn RAD ETILADI — matndan @ olib tashlash ma'noni
    /// buzardi, qolaversa promptda mention TAQIQLANGAN (ya'ni bu holat deyarli bo'lmaydi);</item>
    /// <item>hashtaglar tozalanadi, takrorlari va matnda ALLAQACHON borlari tashlanadi;</item>
    /// <item>uzunlik oshsa AVVAL hashtaglar oxiridan qirqiladi (ular yordamchi), keyingina
    /// matnning o'zi so'z chegarasida kesiladi — matnning o'rtasidan kesish o'qib bo'lmaydigan
    /// natija berardi;</item>
    /// <item>oxirida natija YANA <c>ValidateCaption</c> dan o'tkaziladi — bu qatlam kutilmagan
    /// kamchilikni foydalanuvchiga chiqarib yubormasin.</item>
    /// </list></para>
    /// </summary>
    public static (bool Ok, string Caption, List<string> Hashtags, string Error) Finalize(
        string? body, IEnumerable<string>? hashtags)
    {
        var text = (body ?? "").Trim();

        var mentions = InstagramPublishContract.CountMentions(text);
        if (mentions > IgPublishConst.MaxMentions)
            return (false, "", [],
                $"AI matnida @mention ko'p: {mentions} ta (ruxsat {IgPublishConst.MaxMentions}). Qaytadan urinib ko'ring.");

        var inBody = InstagramPublishContract.CountHashtags(text);
        if (inBody > IgPublishConst.MaxHashtags)
            return (false, "", [],
                $"AI matnida hashtag ko'p: {inBody} ta (ruxsat {IgPublishConst.MaxHashtags}). Qaytadan urinib ko'ring.");

        // Tozalash + takrorsizlik. Matnda allaqachon bor teg qayta qo'shilmaydi.
        var tags = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in hashtags ?? Array.Empty<string>())
        {
            var tag = NormalizeHashtag(raw);
            if (tag.Length == 0) continue;
            if (!seen.Add(tag)) continue;
            if (ContainsTag(text, tag)) continue;
            tags.Add(tag);
        }

        var room = IgPublishConst.MaxHashtags - inBody;
        if (tags.Count > room) tags = tags.Take(Math.Max(0, room)).ToList();

        if (text.Length == 0 && tags.Count == 0)
            return (false, "", [], "AI bo'sh matn qaytardi — mavzuni aniqroq yozib, qaytadan urinib ko'ring.");

        // Uzunlik: avval hashtaglarni oxiridan kamaytiramiz, keyin matnni kesamiz.
        var tail = Tail(tags);
        while (tags.Count > 0 && text.Length + tail.Length > IgPublishConst.MaxCaptionLength)
        {
            tags.RemoveAt(tags.Count - 1);
            tail = Tail(tags);
        }
        if (text.Length + tail.Length > IgPublishConst.MaxCaptionLength)
            text = TrimToWord(text, IgPublishConst.MaxCaptionLength - tail.Length);

        var final = text + tail;

        // Oxirgi darvoza — saqlashda ishlatiladigan AYNAN o'sha tekshiruv.
        var (ok, error) = InstagramPublishContract.ValidateCaption(final);
        if (!ok) return (false, "", [], $"AI matni chegaraga sig'madi: {error} Qaytadan urinib ko'ring.");

        return (true, final, tags, "");
    }

    /// <summary>
    /// Hashtagni Instagram qabul qiladigan ko'rinishga keltiradi: <c>#</c> + harf/raqam/<c>_</c>.
    /// <para>Bo'sh joy va tinish belgilari OLIB TASHLANADI (Instagram'da hashtag ichida bo'sh
    /// joy bo'lmaydi — "ingliz tili" → <c>#inglizTili</c> emas, <c>#ingliztili</c>: model odatda
    /// tayyor teg beradi, bu faqat xavfsizlik qatlami). Yaroqsiz bo'lsa bo'sh satr.</para>
    /// </summary>
    public static string NormalizeHashtag(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "";

        var sb = new StringBuilder(s.Length + 1);
        var hasWord = false;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                if (sb.Length >= MaxHashtagLength - 1) break;
                sb.Append(c);
                if (char.IsLetterOrDigit(c)) hasWord = true;
            }
            // `#`, bo'sh joy, vergul va boshqa belgilar TASHLANADI.
        }
        // ⚠️ Kamida bitta HARF/RAQAM bo'lishi shart: `#___` Instagram'da teg emas va bizning
        // `CountHashtags` sanog'iga ham kirmasdi (ya'ni chegara hisobi buzilardi).
        return hasWord ? "#" + sb : "";
    }

    /* ═════════════════════════ Yordamchilar ═════════════════════════ */

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Hashtaglar bo'lagi — matndan BO'SH QATOR bilan ajratiladi (Instagram uslubi).</summary>
    private static string Tail(List<string> tags) =>
        tags.Count == 0 ? "" : "\n\n" + string.Join(' ', tags);

    /// <summary>Matnda shu teg ALLAQACHON bormi (katta-kichik harf farqsiz, so'z chegarasi bilan).</summary>
    private static bool ContainsTag(string text, string tag)
    {
        var from = 0;
        while (true)
        {
            var i = text.IndexOf(tag, from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            var end = i + tag.Length;
            // Keyingi belgi harf/raqam bo'lsa — bu boshqa (uzunroq) teg: `#ingliz` ≠ `#inglizcha`.
            if (end >= text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '_')) return true;
            from = i + 1;
        }
    }

    /// <summary>
    /// Matnni <paramref name="max"/> belgigacha SO'Z CHEGARASIDA qisqartiradi va oxiriga «…»
    /// qo'yadi (qirqilgani KO'RINIB tursin — jimgina kesilgan matn foydalanuvchini aldardi).
    /// </summary>
    public static string TrimToWord(string? text, int max)
    {
        var s = (text ?? "").Trim();
        if (max <= 0) return "";
        if (s.Length <= max) return s;
        if (max == 1) return "…";

        var cut = s[..(max - 1)];                     // «…» uchun bitta joy
        var space = cut.LastIndexOfAny([' ', '\n', '\t']);
        // So'z chegarasi juda oldinda bo'lsa (masalan bitta uzun so'z) — o'sha yerdan kesamiz.
        if (space > max / 2) cut = cut[..space];
        return cut.TrimEnd() + "…";
    }

    /// <summary>Gemini JSON'ining xom shakli.</summary>
    private sealed class RawCaption
    {
        [JsonPropertyName("caption")] public string? Caption { get; set; }
        [JsonPropertyName("hashtags")] public List<string>? Hashtags { get; set; }
    }
}
