using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// VORONKA AI tahlili (Gemini) — "O'quv bo'limi → Formalar" bo'limidagi IKKITA statistika sahifasi
/// uchun: <b>Lid statistikasi</b> (lid formalari) va <b>Test statistikasi</b> (daraja testlari).
///
/// <para>Ikkalasi bitta servisda, farqi <c>kind</c> da: savol ham, ma'lumot shakli ham bir xil
/// (keldi → ariza → lid → o'quvchi → PUL to'ladi), shuning uchun ikkita ayri servis/jadval
/// yasalmadi. Prompt esa <c>kind</c> ga qarab moslashadi: lid formalarida gap KANALLAR
/// (Instagram/Telegram formasi, manba, <c>?ref=</c> sub-kanal) va reklama byudjeti haqida,
/// daraja testlarida esa TESTLAR va ularga yuborilgan bir martalik havolalar haqida.</para>
///
/// <para>Loyihaning AI arxitekturasi bilan AYNAN bir xil (<c>.claude/rules/ai-analysis.md</c>):
/// raqamlar DETERMINISTIK — kod hisoblaydi (<see cref="BuildMetricsAsync"/>), AI faqat NARRATIV
/// yozadi va 0..100 baho qo'yadi. Natija <c>ResultJson</c> = <c>{ ai, metrics }</c> bo'lib
/// saqlanadi (eski tahlil ochilganda ham diagrammalar ishlaydi) va KUNIGA BIR MARTA yaratiladi.</para>
///
/// <para>⚠️ <b>MAXFIYLIK:</b> promptga FAQAT jamlanma raqamlar ketadi. Ariza qoldirganlarning
/// ismi, TELEFONI va savolnomaga bergan javoblari Gemini'ga HECH QACHON yuborilmaydi — bu tashqi
/// xizmat va bu ma'lumot tahlil uchun umuman kerak emas (savol "qaysi kanal ishlayapti", "kim
/// yozildi" emas). Guruh tahlilida o'quvchilar ismi promptga kiradi, chunki u ICHKI (o'z
/// o'quvchilari) ro'yxati va tavsiya AYNAN shu odamlar haqida bo'ladi — bu yerda esa
/// murojaatchilar hali markazga tegishli emas.</para>
/// </summary>
public static class FunnelAiAnalysisService
{
    /// <summary>Lid formalari voronkasi ("Lid statistikasi" sahifasi).</summary>
    public const string KindLeadForms = "lead-forms";

    /// <summary>Daraja testlari voronkasi ("Test statistikasi" sahifasi).</summary>
    public const string KindLevelTests = "level-tests";

    /// <summary>
    /// Promptga kiradigan kanal/test qatorlari chegarasi. Sabab: prompt shishib ketsa AI eng
    /// muhim raqamlarni "yo'qotadi" (va token narxi oshadi) — 15 ta eng ko'p arizali kanal
    /// voronka tahlili uchun yetarli, quyruqdagilar baribir birma-bir muhokama qilinmaydi.
    /// Jamlanma sonlar (Submissions/Leads/...) BUTUN to'plam bo'yicha — cheklov faqat kesimga.
    /// </summary>
    public const int MaxChannels = 15;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private record Stored(FunnelAiNarrativeDto Ai, FunnelAiMetricsDto Metrics);

    /// <summary>Qo'llab-quvvatlanadigan voronka turlari (klientdan kelgan qiymat shu yerda tekshiriladi).</summary>
    public static bool IsValidKind(string? kind) => kind is KindLeadForms or KindLevelTests;

    // ==================== DETERMINISTIK raqamlar ====================

    /// <summary>
    /// AI'ga beriladigan raqamlarni yig'adi. Ma'lumot MAVJUD yagona manbalardan olinadi —
    /// <see cref="LeadFormService.BuildStatsAsync"/> / <see cref="LevelTestService.BuildOverallStatsAsync"/>
    /// — ya'ni statistika SAHIFASIDA ko'rinadigan sonlar bilan AYNAN bir xil (yangi hisob mantig'i
    /// yaratilmaydi, aks holda "AI boshqa raqam ko'rsatyapti" holati kelib chiqardi).
    ///
    /// <para>Public — testlar (va kelajakdagi "AI'siz snapshot" endpointi) shu funksiyani
    /// to'g'ridan-to'g'ri chaqiradi.</para>
    /// </summary>
    public static async Task<FunnelAiMetricsDto> BuildMetricsAsync(
        IAppDbContext db, string kind, CancellationToken ct = default)
    {
        if (kind == KindLevelTests) return await BuildLevelTestMetricsAsync(db, ct);
        return await BuildLeadFormMetricsAsync(db, ct);
    }

    private static async Task<FunnelAiMetricsDto> BuildLeadFormMetricsAsync(
        IAppDbContext db, CancellationToken ct)
    {
        var s = await LeadFormService.BuildStatsAsync(db);
        // ⚠️ TAKRORSIZ lidlar soni `LeadFormStatsDto` da ALOHIDA maydon sifatida YO'Q va uni
        // javobdan chiqarib olib bo'lmaydi: `ByForm` — FORMALAR kesimi (bir odam ikki formani
        // to'ldirsa ikki qatorda sanaladi), `ByStage` esa bosqichsiz (ustuni o'chirilgan) lidni
        // umuman qoldiradi. Shu sabab maxraj alohida, `BuildStatsAsync` dagi `Funnel` bilan AYNAN
        // bir xil qoidada hisoblanadi (`LeadId` bo'sh qatorlar sanoqqa kirmaydi).
        var (leads, leadsByForm) = await LeadFormService.DistinctLeadCountsAsync(db, ct);

        var channels = s.ByForm
            .OrderByDescending(f => f.Submissions).ThenBy(f => f.Title)
            .Take(MaxChannels)
            .Select(f => new FunnelAiChannelDto(
                f.Title, f.Source, f.Submissions, leadsByForm.GetValueOrDefault(f.FormId, 0),
                f.Converted, f.ActiveStudents, f.Paid, f.Revenue, f.ConvertRate, f.PayRate))
            .ToList();

        return new FunnelAiMetricsDto(
            KindLeadForms, s.Forms, s.ActiveForms, s.Views, s.Submissions, leads,
            s.Converted, s.ActiveStudents, s.Paid, s.Revenue,
            Rate(s.Submissions, s.Views), Rate(s.Converted, leads), Rate(s.Paid, leads),
            channels, s.ByStage, s.Daily);
    }

    private static async Task<FunnelAiMetricsDto> BuildLevelTestMetricsAsync(
        IAppDbContext db, CancellationToken ct)
    {
        _ = ct; // BuildOverallStatsAsync bekor qilish tokenini qabul qilmaydi (kesh orqali chaqiriladi)
        var s = await LevelTestService.BuildOverallStatsAsync(db);

        var channels = s.ByTest
            .OrderByDescending(t => t.Submissions).ThenBy(t => t.Title)
            // Test uchun "manba" tushunchasi yo'q (test kanalga bog'lanmaydi) — maydon bo'sh
            // qoladi, DTO esa ikkala voronka uchun YAGONA (sahifalar bir xil ko'rinishda).
            .Take(MaxChannels)
            .Select(t => new FunnelAiChannelDto(
                t.Title, "", t.Submissions, t.Leads, t.Converted, t.ActiveStudents,
                t.Paid, t.Revenue, t.ConvertRate, t.PayRate))
            .ToList();

        // ⚠️ Bu yerda `Views` = YUBORILGAN bir martalik havolalar (`LevelTestInvite`): daraja
        // testida "ochilish" sanog'i yo'q, eng yaqin ma'nodagi ko'rsatkich shu — "havola
        // yuborilgan N kishidan nechtasi testni topshirdi". Testni ommaviy havola orqali ham
        // topshirish mumkin, ya'ni topshiriq takliflardan KO'P bo'lishi mumkin (foiz 100 dan
        // oshadi) — bu xato emas, promptda ham shunday izohlangan.
        return new FunnelAiMetricsDto(
            KindLevelTests, s.TestCount, s.ActiveTests, s.Invites, s.Submissions, s.Leads,
            s.Converted, s.Active, s.Paid, s.Revenue,
            Rate(s.Submissions, s.Invites), Rate(s.Converted, s.Leads), Rate(s.Paid, s.Leads),
            channels, s.ByStage, s.Daily);
    }

    /// <summary>Foiz (bir kasrgacha). Maxraj 0 bo'lsa — 0 (nolga bo'linish yo'q).</summary>
    private static double Rate(int part, int total) =>
        total > 0 ? Math.Round(part * 100.0 / total, 1) : 0;

    // ==================== Tarix ====================

    /// <summary>Shu voronkaning saqlangan tahlillari (eng yangisi birinchi).</summary>
    public static async Task<List<FunnelAiRecordDto>> HistoryAsync(
        IAppDbContext db, string kind, CancellationToken ct = default)
    {
        var rows = await db.FunnelAiAnalyses.AsNoTracking()
            .Where(a => a.Kind == kind)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToRecordDto).Where(r => r is not null).Select(r => r!).ToList();
    }

    // ==================== Tahlil yaratish ====================

    /// <summary>
    /// Tahlil yaratadi va saqlaydi. Bugungi yozuv bo'lsa — Gemini CHAQIRILMAYDI
    /// (<c>AlreadyToday=true</c>, mavjud yozuv qaytadi).
    ///
    /// <para>⚠️ "Bugun yaratilganmi" tekshiruvi API kaliti tekshiruvidan OLDIN turadi: keshlangan
    /// natija kalit olib tashlangan/eskirgan holatda ham ko'rinishi kerak (guruh va o'qituvchi
    /// tahlilidagi bilan bir xil tartib).</para>
    /// </summary>
    public static async Task<FunnelAiResponseDto> GenerateAsync(
        IAppDbContext db, IConfiguration? config, string kind, CancellationToken ct = default)
    {
        if (!IsValidKind(kind))
            return new FunnelAiResponseDto(false, false, null, "Noma'lum tahlil turi");

        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var todays = await db.FunnelAiAnalyses
            .FirstOrDefaultAsync(a => a.Kind == kind && a.Date == today, ct);
        if (todays is not null)
            return new FunnelAiResponseDto(true, true, ToRecordDto(todays), null);

        var model = GeminiService.ResolveModel(config);
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return new FunnelAiResponseDto(false, false, null,
                "Gemini API kaliti sozlanmagan. Sozlamalar → AI Tahlil (Gemini) bo'limidan kalit kiriting.");

        var prev = await db.FunnelAiAnalyses.AsNoTracking()
            .Where(a => a.Kind == kind)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var metrics = await BuildMetricsAsync(db, kind, ct);
        var metricsJson = JsonSerializer.Serialize(metrics, JsonOpts);

        var prevContext = prev is null
            ? "Bu voronkaning BIRINCHI tahlili — oldingi tahlil yo'q. \"ozgarishlar\" maydonini bo'sh (\"\") qoldir."
            : $"Oldingi tahlil ({prev.Date}) xulosasi: \"{prev.Summary}\". Oldingi umumiy ball: {prev.OverallScore}. " +
              "\"ozgarishlar\" maydonida ANA SHU oldingi tahlilga nisbatan nima o'zgarganini aniq yoz.";

        var (intro, channelRule) = kind == KindLevelTests ? LevelTestPrompt() : LeadFormPrompt();

        var prompt =
            "Sen o'quv markazi rahbari uchun talabchan va TANQIDIY marketing/sotuv auditorisan. " +
            intro + "\n\n" +
            "Asosiy savol: QAYSI KANAL (yoki test) HAQIQIY, PUL TO'LAYDIGAN o'quvchi keltiryapti va " +
            "voronka QAYSI bosqichda uzilyapti (ochilish → ariza → lid → o'quvchi → to'lov). " +
            "Ko'p ariza keltirgan, lekin puli yo'q kanal — ZARAR, buni ochiq ayt.\n\n" +
            "Natijani FAQAT O'ZBEK TILIDA (lotin alifbosi), QUYIDAGI JSON sxemasida QAYTAR " +
            "(boshqa hech narsa yozma, faqat JSON):\n" +
            "{\n" +
            "  \"umumiy\": \"2-4 jumla — voronkaning hozirgi holati, ochiq va aniq\",\n" +
            "  \"kanallar\": \"" + channelRule + "\",\n" +
            "  \"voronka\": \"voronka qayerda uzilyapti: ochilish/ariza, ariza/lid, lid/o'quvchi, o'quvchi/to'lov — qaysi o'tishda eng katta yo'qotish bor\",\n" +
            "  \"sifat\": \"kelayotgan lidlarning SIFATI: bosqichlar taqsimoti nimani ko'rsatadi, lidlar qayerda qotib qolgan\",\n" +
            "  \"pul\": \"pul tomoni: to'lov konversiyasi, tushum, bitta to'lovchi o'quvchining o'rtacha qiymati\",\n" +
            "  \"ozgarishlar\": \"oldingi tahlilga nisbatan o'zgarishlar (yo'q bo'lsa bo'sh)\",\n" +
            "  \"kuchli\": [\"kuchli tomon — raqam bilan\", ...],\n" +
            "  \"zaif\": [\"zaif tomon — raqam bilan\", ...],\n" +
            "  \"xavflar\": [\"voronkaga tahdid soladigan xavf\", ...],\n" +
            "  \"tavsiyalar\": [\"rahbar/marketologga aniq, bajariladigan tavsiya\", ...],\n" +
            "  \"baholar\": { \"hajm\": 0-100, \"konversiya\": 0-100, \"sotuv\": 0-100, \"barqarorlik\": 0-100, \"umumiy\": 0-100 },\n" +
            "  \"trend\": \"yaxshilanmoqda\" yoki \"barqaror\" yoki \"yomonlashmoqda\"\n" +
            "}\n\n" +
            "Qoidalar: TANQIDIY bo'l — muammoni yumshatma va oqibatini ko'rsat, lekin HAR bir da'voni " +
            "berilgan RAQAM bilan asosla (masalan \"120 ta arizadan faqat 4 tasi to'ladi — 3,3%\"). " +
            "Hech narsani TO'QIB CHIQARMA: berilgan raqamlardan tashqarida hech narsa yo'q, ma'lumot " +
            "yetarli bo'lmasa (masalan hali ariza kam yoki davr qisqa) buni OCHIQ ayt va xulosani " +
            "shartli qilib qo'y. \"baholar\" — 0..100 butun sonlar: hajm (kelayotgan oqim yetarlimi), " +
            "konversiya (lid → o'quvchi), sotuv (lid → PUL), barqarorlik (kunlik oqim muntazammi yoki " +
            "uzilib-uzilib), umumiy (boshqalarning umumlashmasi). " + prevContext + "\n\n" +
            "Ko'rsatkichlar (JSON):\n" + metricsJson;

        var (ok, text, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, prompt, jsonMode: true);
        if (!ok) return new FunnelAiResponseDto(false, false, null, err);

        var ai = ParseNarrative(text);
        if (ai is null)
            return new FunnelAiResponseDto(false, false, null,
                "AI javobini o'qib bo'lmadi (format xato). Qaytadan urinib ko'ring.");

        var rec = new FunnelAiAnalysis
        {
            Kind = kind,
            Date = today,
            CreatedAt = AppClock.Iso(),
            Model = model,
            Summary = Trim(ai.Umumiy, 600),
            OverallScore = Math.Clamp(ai.Baholar.Umumiy, 0, 100),
            ResultJson = JsonSerializer.Serialize(new Stored(ai, metrics), JsonOpts),
        };
        db.FunnelAiAnalyses.Add(rec);
        await db.SaveChangesAsync(ct);

        return new FunnelAiResponseDto(true, false, ToRecordDto(rec), null);
    }

    /// <summary>Lid formalari uchun promptning turga xos qismi (gap KANALLAR haqida).</summary>
    private static (string Intro, string ChannelRule) LeadFormPrompt() => (
        "Quyida markazning IJTIMOIY TARMOQ LID FORMALARI bo'yicha jamlanma ko'rsatkichlar JSON " +
        "ko'rinishida. Har bir forma — ALOHIDA reklama kanali (Instagram, Telegram, Facebook, " +
        "bannerdagi QR...) o'z havolasi va o'z MANBASI bilan, ya'ni ariza qaysi kanaldan kelgani " +
        "aniq. \"views\" — havola necha marta ochilgani, \"submissions\" — to'ldirilgan ariza, " +
        "\"leads\" — TAKRORSIZ odamlar (bir odam ikki marta to'ldirsa ham bitta), \"converted\" — " +
        "o'quvchiga aylangani, \"activeStudents\" — hozir faol o'qiyotgani, \"paid\"/\"revenue\" — " +
        "haqiqatan PUL to'laganlar va sof tushum. \"channels\" — formalar kesimi (\"source\" — " +
        "kanal manbasi), \"stages\" — shu lidlar hozir kanbanning qaysi ustunida, \"daily\" — " +
        "oxirgi 30 kunlik ariza oqimi.",
        "kanallar (formalar) taqqoslamasi: qaysi kanal PUL to'laydigan o'quvchi keltiryapti, " +
        "qaysi biri faqat bo'sh ariza ishlab chiqaryapti; reklama byudjetini qayerga ko'chirish " +
        "kerakligini raqam bilan ayt");

    /// <summary>Daraja testlari uchun promptning turga xos qismi (gap TESTLAR haqida).</summary>
    private static (string Intro, string ChannelRule) LevelTestPrompt() => (
        "Quyida markazning DARAJA TESTLARI bo'yicha jamlanma ko'rsatkichlar JSON ko'rinishida. " +
        "Daraja testi — abituriyentga yuboriladigan onlayn test: uni topshirgan odam CRM'da LID " +
        "bo'lib tushadi. \"views\" — YUBORILGAN bir martalik havolalar soni (testni ommaviy havola " +
        "orqali ham topshirish mumkin, shuning uchun topshiriqlar sonining havolalardan ko'p " +
        "bo'lishi XATO emas), \"submissions\" — topshirilgan test, \"leads\" — TAKRORSIZ odamlar " +
        "(bir odam testni ikki marta topshirsa ham bitta), \"converted\" — o'quvchiga aylangani, " +
        "\"activeStudents\" — hozir faol o'qiyotgani, \"paid\"/\"revenue\" — haqiqatan PUL " +
        "to'laganlar va sof tushum. \"channels\" — TESTLAR kesimi, \"stages\" — shu lidlar hozir " +
        "kanbanning qaysi ustunida, \"daily\" — oxirgi 30 kunlik topshiriq oqimi.",
        "testlar taqqoslamasi: qaysi test PUL to'laydigan o'quvchi keltiryapti, qaysi biri " +
        "topshirilyapti-yu natija bermayapti; yuborilgan havolalarning qancha qismi ishlatilgani " +
        "nimani ko'rsatadi");

    // ==================== Yordamchilar ====================

    private static FunnelAiRecordDto? ToRecordDto(FunnelAiAnalysis a)
    {
        var s = ParseStored(a.ResultJson);
        if (s is null) return null;
        return new FunnelAiRecordDto(a.Id, a.Kind, a.Date, a.CreatedAt, a.Model, a.OverallScore, s.Ai, s.Metrics);
    }

    private static Stored? ParseStored(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Stored>(json, JsonOpts);
            return s is null ? null : new Stored(Sanitize(s.Ai), s.Metrics);
        }
        catch { return null; }
    }

    /// <summary>Gemini JSON javobini narrativga aylantiradi (kod-fence tozalanadi, null'lar to'ldiriladi).</summary>
    private static FunnelAiNarrativeDto? ParseNarrative(string text)
    {
        var t = (text ?? "").Trim();
        if (t.StartsWith("```"))
        {
            var nl = t.IndexOf('\n');
            if (nl >= 0) t = t[(nl + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
            t = t.Trim();
        }
        var open = t.IndexOf('{');
        var close = t.LastIndexOf('}');
        if (open >= 0 && close > open) t = t[open..(close + 1)];
        try
        {
            var r = JsonSerializer.Deserialize<FunnelAiNarrativeDto>(t, JsonOpts);
            return r is null ? null : Sanitize(r);
        }
        catch { return null; }
    }

    private static FunnelAiNarrativeDto Sanitize(FunnelAiNarrativeDto r)
    {
        var b = r.Baholar ?? new FunnelAiScoresDto(0, 0, 0, 0, 0);
        return new FunnelAiNarrativeDto(
            r.Umumiy ?? "", r.Kanallar ?? "", r.Voronka ?? "", r.Sifat ?? "", r.Pul ?? "",
            r.Ozgarishlar ?? "",
            r.Kuchli ?? new List<string>(), r.Zaif ?? new List<string>(),
            r.Xavflar ?? new List<string>(), r.Tavsiyalar ?? new List<string>(),
            new FunnelAiScoresDto(
                Math.Clamp(b.Hajm, 0, 100), Math.Clamp(b.Konversiya, 0, 100),
                Math.Clamp(b.Sotuv, 0, 100), Math.Clamp(b.Barqarorlik, 0, 100),
                Math.Clamp(b.Umumiy, 0, 100)),
            r.Trend ?? "");
    }

    private static string Trim(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..max];
    }
}
