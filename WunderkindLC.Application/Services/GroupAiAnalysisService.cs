using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH AI tahlili (Gemini) — guruh sahifasidagi "AI tahlil" tabi. Bir guruhga KUNIGA BIR MARTA
/// yaratiladi (mavjud bo'lsa Gemini chaqirilmaydi — saqlangani qaytadi).
///
/// Raqamlar DETERMINISTIK: <see cref="GroupSnapshotBuilder"/> hisoblaydi (davomat, muzlatish/ketish
/// va sabablari, imtihonlar, to'lovlar, jurnal intizomi, o'zlashtirish, dastur qamrovi). AI faqat
/// NARRATIV yozadi — TANQIDIY ohangda: muammoni yumshatmasdan, aniq raqamga tayanib.
/// </summary>
public static class GroupAiAnalysisService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private record Stored(GroupAiNarrativeDto Ai, GroupAiMetricsDto Metrics);

    /// <summary>Guruhning saqlangan tahlillari (eng yangisi birinchi).</summary>
    public static async Task<List<GroupAiRecordDto>> HistoryAsync(
        IAppDbContext db, string groupId, CancellationToken ct = default)
    {
        var rows = await db.GroupAiAnalyses.AsNoTracking()
            .Where(a => a.GroupId == groupId)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToRecordDto).Where(r => r is not null).Select(r => r!).ToList();
    }

    /// <summary>Tahlil yaratadi va saqlaydi. Bugungi yozuv bo'lsa — Gemini chaqirilmaydi
    /// (AlreadyToday=true, mavjud yozuv qaytadi).</summary>
    public static async Task<GroupAiResponseDto> GenerateAsync(
        IAppDbContext db, IConfiguration? config, Group group, bool includeFinance,
        CancellationToken ct = default)
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var todays = await db.GroupAiAnalyses
            .FirstOrDefaultAsync(a => a.GroupId == group.Id && a.Date == today, ct);
        if (todays is not null)
            return new GroupAiResponseDto(true, true, ToRecordDto(todays), null);

        var model = GeminiService.ResolveModel(config);
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return new GroupAiResponseDto(false, false, null,
                "Gemini API kaliti sozlanmagan. Sozlamalar → AI Tahlil (Gemini) bo'limidan kalit kiriting.");

        var prev = await db.GroupAiAnalyses.AsNoTracking()
            .Where(a => a.GroupId == group.Id)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var (metrics, snapshotJson) = await GroupSnapshotBuilder.BuildAsync(db, group, includeFinance, ct);

        var prevContext = prev is null
            ? "Bu guruhning BIRINCHI tahlili — oldingi tahlil yo'q. \"ozgarishlar\" maydonini bo'sh (\"\") qoldir."
            : $"Oldingi tahlil ({prev.Date}) xulosasi: \"{prev.Summary}\". Oldingi umumiy ball: {prev.OverallScore}. " +
              "\"ozgarishlar\" maydonida ANA SHU oldingi tahlilga nisbatan nima o'zgarganini aniq yoz.";

        var financeRule = includeFinance
            ? "\"tolovlar\" maydonida to'lov intizomini (yig'ilish foizi, qarzdorlik, to'lamaganlar soni) tahlil qil."
            : "To'lov ma'lumoti BERILMAGAN (ruxsat yo'q) — \"tolovlar\" maydonini bo'sh (\"\") qoldir va " +
              "\"baholar.tolov\" uchun 50 qo'y.";

        var prompt =
            "Sen o'quv markazi rahbari uchun talabchan va TANQIDIY pedagogik auditorsan. Quyida BITTA GURUH " +
            "haqida oxirgi 12 oylik to'liq ma'lumot JSON ko'rinishida: guruh pasporti (kurs, o'qituvchi, dars " +
            "kunlari, sig'im, oylik narx), o'quvchi OQIMI (kelgan/aktivlashgan/muzlatilgan/ketgan oyma-oy) va " +
            "KETISH SABABLARI, DAVOMAT (o'rtacha foiz, sababli qoldirish, kech kelish, sabablar taqsimoti), " +
            "JURNAL INTIZOMI (rejadagi va o'tilgan darslar, muhlati o'tganiga qaramay belgilanmagan darslar, " +
            "mavzu/uy vazifa/davomat olinishi), O'ZLASHTIRISH (baholar dinamikasi, ball, uy vazifa va xulq " +
            "belgilari, dastur qamrovi), IMTIHONLAR/testlar natijalari, TO'LOVLAR va har bir o'quvchining kesimi.\n\n" +
            "Vazifa: shu ma'lumotni CHUQUR va TANQIDIY tahlil qilib, FAQAT O'ZBEK TILIDA (lotin alifbosi) " +
            "natijani QUYIDAGI JSON sxemasida QAYTAR (boshqa hech narsa yozma, faqat JSON):\n" +
            "{\n" +
            "  \"umumiy\": \"2-4 jumla — guruhning hozirgi holati, ochiq va aniq\",\n" +
            "  \"davomat\": \"davomat holati: kim/qancha qoldirmoqda, sabablari, xavfli darajadagi o'quvchilar\",\n" +
            "  \"oqim\": \"muzlatish va ketish tahlili: qancha, qachon, nima sababdan; guruh to'lib boryaptimi yoki bo'shab\",\n" +
            "  \"ozlashtirish\": \"baholar va ball dinamikasi, uy vazifa/xulq, dastur bo'yicha qolish/oldinlash\",\n" +
            "  \"imtihonlar\": \"test/imtihon natijalari: o'rtacha daraja, topshirmaganlar, sust natijalar\",\n" +
            "  \"tolovlar\": \"to'lov intizomi: yig'ilish foizi, qarzdorlik va uning oqibatlari\",\n" +
            "  \"jurnal\": \"o'qituvchining jurnal intizomi: belgilanmagan darslar, mavzu/uy vazifa/baho muntazamligi\",\n" +
            "  \"ozgarishlar\": \"oldingi tahlilga nisbatan o'zgarishlar (yo'q bo'lsa bo'sh)\",\n" +
            "  \"kuchli\": [\"guruhning kuchli tomoni\", ...],\n" +
            "  \"zaif\": [\"zaif tomon — aniq raqam bilan\", ...],\n" +
            "  \"xavflar\": [\"guruhga tahdid soladigan xavf (masalan ketish to'lqini, qarzdorlik, past davomat)\", ...],\n" +
            "  \"tavsiyalar\": [\"rahbar/o'qituvchiga aniq, bajariladigan tavsiya\", ...],\n" +
            "  \"baholar\": { \"davomat\": 0-100, \"barqarorlik\": 0-100, \"ozlashtirish\": 0-100, \"tolov\": 0-100, \"jurnal\": 0-100, \"umumiy\": 0-100 },\n" +
            "  \"trend\": \"yaxshilanmoqda\" yoki \"barqaror\" yoki \"yomonlashmoqda\"\n" +
            "}\n\n" +
            "Qoidalar: TANQIDIY bo'l — muammoni yumshatma, kamchilikni ochiq ayt va oqibatini ko'rsat, lekin " +
            "HAR bir da'voni berilgan RAQAM bilan asosla (masalan \"davomat 68% — har 3-dars bo'sh\"). Hech narsani " +
            "to'qib chiqarma; ma'lumot yetarli bo'lmagan joyda buni ochiq yoz. Muammoli o'quvchilarni ISM bilan " +
            "ko'rsat (ro'yxat berilgan). \"baholar\" — 0..100 butun sonlar: davomat, barqarorlik (o'quvchini " +
            "ushlab qolish), ozlashtirish, tolov (to'lov intizomi), jurnal (o'qituvchi intizomi), umumiy " +
            "(boshqalarning umumlashmasi). " + financeRule + " " + prevContext + "\n\n" +
            "Guruh ma'lumotlari (JSON):\n" + snapshotJson;

        var (ok, text, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, prompt, jsonMode: true);
        if (!ok) return new GroupAiResponseDto(false, false, null, err);

        var ai = ParseNarrative(text);
        if (ai is null)
            return new GroupAiResponseDto(false, false, null,
                "AI javobini o'qib bo'lmadi (format xato). Qaytadan urinib ko'ring.");

        var rec = new GroupAiAnalysis
        {
            GroupId = group.Id,
            Date = today,
            CreatedAt = AppClock.Iso(),
            Model = model,
            Summary = Trim(ai.Umumiy, 600),
            OverallScore = Math.Clamp(ai.Baholar.Umumiy, 0, 100),
            ResultJson = JsonSerializer.Serialize(new Stored(ai, metrics), JsonOpts),
        };
        db.GroupAiAnalyses.Add(rec);
        await db.SaveChangesAsync(ct);

        return new GroupAiResponseDto(true, false, ToRecordDto(rec), null);
    }

    private static GroupAiRecordDto? ToRecordDto(GroupAiAnalysis a)
    {
        var s = ParseStored(a.ResultJson);
        if (s is null) return null;
        return new GroupAiRecordDto(a.Id, a.Date, a.CreatedAt, a.Model, a.OverallScore, s.Ai, s.Metrics);
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
    private static GroupAiNarrativeDto? ParseNarrative(string text)
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
            var r = JsonSerializer.Deserialize<GroupAiNarrativeDto>(t, JsonOpts);
            return r is null ? null : Sanitize(r);
        }
        catch { return null; }
    }

    private static GroupAiNarrativeDto Sanitize(GroupAiNarrativeDto r)
    {
        var b = r.Baholar ?? new GroupAiScoresDto(0, 0, 0, 0, 0, 0);
        return new GroupAiNarrativeDto(
            r.Umumiy ?? "", r.Davomat ?? "", r.Oqim ?? "", r.Ozlashtirish ?? "", r.Imtihonlar ?? "",
            r.Tolovlar ?? "", r.Jurnal ?? "", r.Ozgarishlar ?? "",
            r.Kuchli ?? new List<string>(), r.Zaif ?? new List<string>(),
            r.Xavflar ?? new List<string>(), r.Tavsiyalar ?? new List<string>(),
            new GroupAiScoresDto(
                Math.Clamp(b.Davomat, 0, 100), Math.Clamp(b.Barqarorlik, 0, 100),
                Math.Clamp(b.Ozlashtirish, 0, 100), Math.Clamp(b.Tolov, 0, 100),
                Math.Clamp(b.Jurnal, 0, 100), Math.Clamp(b.Umumiy, 0, 100)),
            r.Trend ?? "");
    }

    private static string Trim(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..max];
    }
}
