using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QITUVCHI AI tahlili (Gemini) — o'qituvchi profilidagi "AI tahlil" tabi. Bir o'qituvchiga
/// KUNIGA BIR MARTA yaratiladi (mavjud bo'lsa Gemini chaqirilmaydi — saqlangani qaytadi).
///
/// Raqamlar DETERMINISTIK: <see cref="TeacherSnapshotBuilder"/> hisoblaydi (o'quvchi oqimi, ketish
/// sabablari, jurnalni o'z vaqtida to'ldirish, baholar dinamikasi, testlar, davomat).
/// AI faqat NARRATIV yozadi (o'zbek tilida) va 0..100 oralig'ida sohaviy baholar qo'yadi.
/// Oldingi tahlil bo'lsa — yangi tahlil unga nisbatan o'zgarishlarni ham aytadi.
/// </summary>
public static class TeacherAiAnalysisService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Saqlanadigan to'liq natija: AI narrativ + deterministik raqamlar.</summary>
    private record Stored(TeacherAiNarrativeDto Ai, TeacherAiMetricsDto Metrics);

    /// <summary>O'qituvchining saqlangan tahlillari (eng yangisi birinchi).</summary>
    public static async Task<List<TeacherAiRecordDto>> HistoryAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default)
    {
        var rows = await db.TeacherAiAnalyses.AsNoTracking()
            .Where(a => a.TeacherId == teacherId)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToRecordDto).Where(r => r is not null).Select(r => r!).ToList();
    }

    /// <summary>Tahlil yaratadi va saqlaydi. Bugungi yozuv bo'lsa — Gemini chaqirilmaydi
    /// (AlreadyToday=true, mavjud yozuv qaytadi).</summary>
    public static async Task<TeacherAiResponseDto> GenerateAsync(
        IAppDbContext db, IConfiguration? config, Teacher teacher, CancellationToken ct = default)
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        // Kuniga bir marta — kalit tekshiruvidan OLDIN (keshlangan tahlilni ko'rsatish kalitga bog'liq emas).
        var todays = await db.TeacherAiAnalyses
            .FirstOrDefaultAsync(a => a.TeacherId == teacher.Id && a.Date == today, ct);
        if (todays is not null)
            return new TeacherAiResponseDto(true, true, ToRecordDto(todays), null);

        var model = GeminiService.ResolveModel(config);
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return new TeacherAiResponseDto(false, false, null,
                "Gemini API kaliti sozlanmagan. Sozlamalar → AI Tahlil (Gemini) bo'limidan kalit kiriting.");

        var prev = await db.TeacherAiAnalyses.AsNoTracking()
            .Where(a => a.TeacherId == teacher.Id)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var (metrics, snapshotJson) = await TeacherSnapshotBuilder.BuildAsync(db, teacher, ct);

        var prevContext = prev is null
            ? "Bu o'qituvchining BIRINCHI tahlili — oldingi tahlil yo'q. \"ozgarishlar\" maydonini bo'sh (\"\") qoldir."
            : $"Oldingi tahlil ({prev.Date}) xulosasi: \"{prev.Summary}\". Oldingi umumiy ball: {prev.OverallScore}. " +
              "\"ozgarishlar\" maydonida ANA SHU oldingi tahlilga nisbatan nima o'zgarganini (yaxshilangan/" +
              "yomonlashgan joylar, ball farqi) aniq yoz.";

        var prompt =
            "Sen o'quv markazi rahbari uchun tajribali pedagogik auditorsan. Quyida BITTA O'QITUVCHI haqida " +
            "oxirgi 12 oylik to'liq ma'lumot JSON ko'rinishida:\n" +
            "• guruhlari va ulardagi o'quvchilar holati;\n" +
            "• O'QUVCHI OQIMI — oyma-oy qancha o'quvchi kelgan, aktivlashgan, muzlatilgan va ketgan, " +
            "saqlash (retention) va yo'qotish foizi;\n" +
            "• KETISH SABABLARI — o'quvchilar guruhdan chiqarilganda/muzlatilganda ko'rsatilgan sabablar sanog'i;\n" +
            "• JURNAL INTIZOMI — rejadagi va o'tilgan darslar, MUHLATI O'TGANIGA QARAMAY jurnalda " +
            "belgilanmagan darslar (jurnalni o'z vaqtida to'ldirish ko'rsatkichi), mavzu/uy vazifa yozilishi, " +
            "davomat olinishi, qo'yilgan baholar soni;\n" +
            "• RIVOJLANISH — o'rtacha baho dinamikasi, o'quvchilar davomati va yig'gan bali, " +
            "test natijalari;\n" +
            "• o'qituvchining o'z davomati va ota-onalardan kelgan shikoyat/takliflar;\n" +
            "• O'QUVCHILARNING SHU O'QITUVCHI HAQIDAGI FIKRLARI (`oquvchilarFikri.matnlar`) — " +
            "ma'muriyat o'quvchilar bilan suhbatlashib yozib borgan MATNLI mulohazalar. Bu eng qimmatli " +
            "sifat manbai: raqamlar ko'rsatmaydigan narsani (tushuntirish uslubi, munosabat, dars " +
            "qiziqarliligi, adolatlilik) aynan shu yerdan bilib olasan.\n\n" +
            "Vazifa: shu ma'lumotni CHUQUR tahlil qilib, FAQAT O'ZBEK TILIDA (lotin alifbosi) natijani QUYIDAGI " +
            "JSON sxemasida QAYTAR (boshqa hech narsa yozma, faqat JSON):\n" +
            "{\n" +
            "  \"umumiy\": \"2-4 jumla — o'qituvchining hozirgi umumiy holati\",\n" +
            "  \"oquvchiOqimi\": \"qancha o'quvchi kelmoqda/ketmoqda, oqim dinamikasi va saqlash darajasi tahlili\",\n" +
            "  \"ketishSabablari\": \"o'quvchilar nima sababdan ketmoqda, qaysi sabab ustun, buni kamaytirish yo'llari\",\n" +
            "  \"jurnal\": \"jurnalni o'z vaqtida to'ldirish intizomi: belgilanmagan darslar, mavzu/uy vazifa/baho qo'yish\",\n" +
            "  \"rivojlanish\": \"o'quvchilar o'zlashtirishi va davomati dinamikasi — yaxshilanmoqdami yoki pasaymoqda\",\n" +
            "  \"oquvchilarFikri\": \"O'QUVCHILAR FIKRI tahlili: matnlarda TAKRORLANUVCHI naqshlar — nima " +
            "maqtaladi, nimadan shikoyat qilinadi, qaysi guruhda muammo ko'proq; 3-6 jumla. Matn yo'q bo'lsa bo'sh\",\n" +
            "  \"ozgarishlar\": \"oldingi tahlilga nisbatan o'zgarishlar (yo'q bo'lsa bo'sh)\",\n" +
            "  \"kuchli\": [\"kuchli tomon\", ...],\n" +
            "  \"zaif\": [\"zaif tomon / e'tibor kerak\", ...],\n" +
            "  \"xavflar\": [\"rahbar e'tiboriga muhtoj xavf\", ...],\n" +
            "  \"tavsiyalar\": [\"o'qituvchi va rahbarga aniq amaliy tavsiya\", ...],\n" +
            "  \"baholar\": { \"jurnal\": 0-100, \"saqlash\": 0-100, \"baholash\": 0-100, \"rivojlanish\": 0-100, \"faollik\": 0-100, \"umumiy\": 0-100 },\n" +
            "  \"trend\": \"yaxshilanmoqda\" yoki \"barqaror\" yoki \"yomonlashmoqda\"\n" +
            "}\n\n" +
            "Qoidalar: \"baholar\" — 0..100 butun sonlar: jurnal (darslarni o'z vaqtida belgilash va to'ldirish), " +
            "saqlash (o'quvchini ushlab qola olishi), baholash (baho/mavzu/uy vazifa muntazamligi), " +
            "rivojlanish (o'zlashtirish va davomat dinamikasi), faollik (test va umumiy faollik), " +
            "umumiy (boshqalarning umumlashmasi). Ma'lumot yo'q soha uchun ehtiyotkor o'rta baho qo'y. " +
            "FAQAT berilgan raqamlarga tayan, to'qib chiqarma. Har matn maydoni qisqa, aniq va rahbarga foydali. " +
            "MAXFIYLIK: o'quvchilar fikri matnlarini SO'ZMA-SO'Z KO'CHIRMA va o'quvchi ismini yozma — " +
            "faqat umumlashtirilgan naqshni yoz (bu xulosa o'qituvchining o'ziga ham ko'rsatiladi). " +
            "O'quvchilar fikri bo'lsa — uni \"kuchli\"/\"zaif\"/\"tavsiyalar\" ro'yxatlarida ham hisobga ol, " +
            "chunki o'qituvchini rivojlantirish uchun eng aniq signal shu. " +
            prevContext + "\n\n" +
            "O'qituvchi ma'lumotlari (JSON):\n" + snapshotJson;

        var (ok, text, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, prompt, jsonMode: true);
        if (!ok) return new TeacherAiResponseDto(false, false, null, err);

        var ai = ParseNarrative(text);
        if (ai is null)
            return new TeacherAiResponseDto(false, false, null,
                "AI javobini o'qib bo'lmadi (format xato). Qaytadan urinib ko'ring.");

        var rec = new TeacherAiAnalysis
        {
            TeacherId = teacher.Id,
            Date = today,
            CreatedAt = AppClock.Iso(),
            Model = model,
            Summary = Trim(ai.Umumiy, 600),
            OverallScore = Math.Clamp(ai.Baholar.Umumiy, 0, 100),
            ResultJson = JsonSerializer.Serialize(new Stored(ai, metrics), JsonOpts),
        };
        db.TeacherAiAnalyses.Add(rec);
        await db.SaveChangesAsync(ct);

        return new TeacherAiResponseDto(true, false, ToRecordDto(rec), null);
    }

    private static TeacherAiRecordDto? ToRecordDto(TeacherAiAnalysis a)
    {
        var s = ParseStored(a.ResultJson);
        if (s is null) return null;
        return new TeacherAiRecordDto(a.Id, a.Date, a.CreatedAt, a.Model, a.OverallScore, s.Ai, s.Metrics);
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
    private static TeacherAiNarrativeDto? ParseNarrative(string text)
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
            var r = JsonSerializer.Deserialize<TeacherAiNarrativeDto>(t, JsonOpts);
            return r is null ? null : Sanitize(r);
        }
        catch { return null; }
    }

    private static TeacherAiNarrativeDto Sanitize(TeacherAiNarrativeDto r)
    {
        var b = r.Baholar ?? new TeacherAiScoresDto(0, 0, 0, 0, 0, 0);
        return new TeacherAiNarrativeDto(
            r.Umumiy ?? "", r.OquvchiOqimi ?? "", r.KetishSabablari ?? "", r.Jurnal ?? "",
            r.Rivojlanish ?? "", r.Ozgarishlar ?? "",
            r.Kuchli ?? new List<string>(), r.Zaif ?? new List<string>(),
            r.Xavflar ?? new List<string>(), r.Tavsiyalar ?? new List<string>(),
            new TeacherAiScoresDto(
                Math.Clamp(b.Jurnal, 0, 100), Math.Clamp(b.Saqlash, 0, 100), Math.Clamp(b.Baholash, 0, 100),
                Math.Clamp(b.Rivojlanish, 0, 100), Math.Clamp(b.Faollik, 0, 100), Math.Clamp(b.Umumiy, 0, 100)),
            r.Trend ?? "",
            r.OquvchilarFikri ?? "");
    }

    private static string Trim(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..max];
    }
}
