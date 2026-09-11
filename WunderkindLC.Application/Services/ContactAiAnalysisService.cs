using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "BOG'LANISH KERAK" hisobotining AI tahlili (Gemini) — yozilgan SABABLAR, "javobi nima dedi"
/// matnlari va qo'ng'iroq natijalari asosida xulosa.
///
/// <para>Loyihaning AI arxitekturasi bilan bir xil (<c>.claude/rules/ai-analysis.md</c>):
/// raqamlar DETERMINISTIK — kod hisoblaydi (<see cref="ContactReport.BuildAsync"/>, ya'ni hisobot
/// sahifasidagi AYNAN o'sha sonlar), AI faqat NARRATIV yozadi va 0..100 baho qo'yadi. Natija
/// <c>ResultJson</c> = <c>{ ai, metrics }</c> bo'lib saqlanadi (eski tahlil ochilganda ham
/// hamma raqam joyida turadi).</para>
///
/// <para><b>DAVR BILAN ISHLAYDI — boshqa tahlillardan asosiy FARQI.</b> Hisobot sahifasida
/// operator kun/oy/oraliq tanlaydi, tahlil esa AYNAN o'sha davr uchun yaratiladi. "Kuniga bir
/// marta" cheklovi ham shu davr bo'yicha: bir kunda har xil davrlarni tahlil qilish mumkin,
/// lekin bitta davrni ikki marta emas (Gemini bekorga chaqirilmasin).</para>
///
/// <para>⚠️ <b>MAXFIYLIK:</b> promptga o'quvchining ISMI ham, TELEFONI ham HECH QACHON
/// yuborilmaydi — savol "kim" emas, "NIMA deyilyapti va natija qanday" (qarang:
/// <see cref="ContactAiSampleDto"/>). Xodim ismi esa qoladi: "kim qanday ishlayapti" tahlilning
/// maqsadli qismi va bu ichki ma'lumot.</para>
/// </summary>
public static class ContactAiAnalysisService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private record Stored(ContactAiNarrativeDto Ai, ContactAiMetricsDto Metrics);

    /// <summary>Tarixda qaytadigan eng ko'p tahlil (ro'yxat cheksiz o'smasin).</summary>
    public const int HistoryLimit = 50;

    // ==================== Tarix ====================

    /// <summary>
    /// Saqlangan tahlillar — eng yangisi birinchi. <paramref name="from"/>/<paramref name="to"/>
    /// berilsa faqat AYNI o'sha davr tahlillari (sahifadagi davr o'zgarganda begona davr tahlili
    /// ko'rinib qolmasin — raqamlar boshqa davrniki bo'lardi).
    /// </summary>
    public static async Task<List<ContactAiRecordDto>> HistoryAsync(
        IAppDbContext db, string? from = null, string? to = null, CancellationToken ct = default)
    {
        var q = db.ContactAiAnalyses.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(from)) q = q.Where(a => a.FromDate == from);
        if (!string.IsNullOrWhiteSpace(to)) q = q.Where(a => a.ToDate == to);

        var rows = await q
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Date)
            .Take(HistoryLimit)
            .ToListAsync(ct);
        return rows.Select(ToRecordDto).Where(r => r is not null).Select(r => r!).ToList();
    }

    // ==================== Tahlil yaratish ====================

    /// <summary>
    /// Tanlangan davr uchun tahlil yaratadi va saqlaydi.
    ///
    /// <para>⚠️ Shu davr uchun BUGUN yaratilgan yozuv bo'lsa Gemini CHAQIRILMAYDI
    /// (<c>AlreadyToday=true</c>, mavjud yozuv qaytadi). Bu tekshiruv API kaliti tekshiruvidan
    /// OLDIN turadi — keshlangan natija kalit olib tashlangan holatda ham ko'rinishi kerak
    /// (boshqa tahlillardagi bilan bir xil tartib).</para>
    /// </summary>
    public static async Task<ContactAiResponseDto> GenerateAsync(
        IAppDbContext db, IConfiguration? config, string? from, string? to,
        CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var fromDate = string.IsNullOrWhiteSpace(from)
            ? today.AddDays(-29).ToString("yyyy-MM-dd") : from!.Trim();
        var toDate = string.IsNullOrWhiteSpace(to) ? today.ToString("yyyy-MM-dd") : to!.Trim();
        if (!DateOnly.TryParse(fromDate, out var f) || !DateOnly.TryParse(toDate, out var t))
            return new ContactAiResponseDto(false, false, null, "Sana noto'g'ri (YYYY-MM-DD)");
        if (t < f) (fromDate, toDate) = (toDate, fromDate);

        var todayKey = today.ToString("yyyy-MM-dd");

        var todays = await db.ContactAiAnalyses
            .FirstOrDefaultAsync(a => a.FromDate == fromDate && a.ToDate == toDate && a.Date == todayKey, ct);
        if (todays is not null)
            return new ContactAiResponseDto(true, true, ToRecordDto(todays), null);

        var metrics = await ContactReport.BuildAsync(
            db, fromDate, toDate, todayKey, ContactReport.DefaultSampleCount, ct);

        // ⚠️ BO'SH DAVR tekshiruvi kalit tekshiruvidan ham OLDIN: bu foydalanuvchining TANLOVI
        // haqidagi xato ("boshqa davrni tanlang"), kalit esa sozlama muammosi. Bo'sh davrdan
        // xulosa chiqmaydi, so'rov puli esa baribir ketardi.
        if (metrics.Attempts == 0 && metrics.Created == 0)
            return new ContactAiResponseDto(false, false, null,
                "Bu davrda bog'lanish amali bo'lmagan — tahlil qilishga ma'lumot yo'q. Boshqa davrni tanlang.");

        var model = GeminiService.ResolveModel(config);
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return new ContactAiResponseDto(false, false, null,
                "Gemini API kaliti sozlanmagan. Sozlamalar → AI Tahlil (Gemini) bo'limidan kalit kiriting.");

        var prev = await db.ContactAiAnalyses.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var prevContext = prev is null
            ? "Bu bo'limning BIRINCHI tahlili — oldingi tahlil yo'q. \"ozgarishlar\" maydonini bo'sh (\"\") qoldir."
            : $"Oldingi tahlil ({prev.FromDate} — {prev.ToDate}, {prev.Date} kuni yaratilgan) xulosasi: \"{prev.Summary}\". "
              + $"Oldingi umumiy ball: {prev.OverallScore}. \"ozgarishlar\" maydonida ANA SHU oldingi tahlilga "
              + "nisbatan nima o'zgarganini aniq yoz (davrlar boshqa bo'lsa buni ham eslat).";

        var metricsJson = JsonSerializer.Serialize(metrics, JsonOpts);

        var prompt =
            "Sen o'quv markazi rahbari uchun talabchan va TANQIDIY sotuv/xizmat auditorisan. " +
            "Quyida markazning \"BOG'LANISH KERAK\" bo'limi (o'quvchi bilan bog'lanish navbati) " +
            $"bo'yicha {fromDate} — {toDate} davridagi ko'rsatkichlar JSON ko'rinishida.\n\n" +
            "Bo'lim qanday ishlaydi: xodim o'quvchini SABAB bilan navbatga qo'yadi (masalan darsga " +
            "kelmayapti, to'lov kechikdi), operator qo'ng'iroq qiladi, NATIJANI belgilaydi " +
            "(gaplashildi / ko'tarmadi / band / raqam ishlamadi) va \"javobi nima dedi\" ni yozadi, " +
            "so'ng keyingi qadamni tanlaydi: hal bo'ldi / qayta qo'ng'iroq (sana bilan) / bog'lanib " +
            "bo'lmadi.\n\n" +
            "Maydonlar: \"created\" — davrda ochilgan talablar; \"attempts\" — bog'lanish urinishlari " +
            "(ko'tarmagani ham); \"reached\" — odam bilan HAQIQATAN gaplashilgani; \"done\"/\"failed\"/" +
            "\"callback\" — urinishdan keyingi qadam; \"openNow\"/\"overdueNow\" — davrga BOG'LIQ EMAS, " +
            "HOZIRGI navbat va muddati o'tgan qayta qo'ng'iroqlar; \"withResponse\" — javob matni " +
            "yozilgan urinishlar; \"daily\" — kunlik oqim; \"byStaff\" — xodimlar kesimi; \"byReason\" — " +
            "sabablar kesimi (talab OCHILGAN sana bo'yicha); \"byResult\" — qo'ng'iroq natijalari; " +
            "\"topWords\" — javoblarda eng ko'p uchragan so'zlar; \"samples\" — javob matnlarining " +
            "O'ZIDAN namunalar (o'quvchi ismi ATAYIN yo'q — maxfiylik).\n\n" +
            "ASOSIY SAVOL: odamlar NIMA deyishyapti (sabablar va javob matnlari nimani ko'rsatadi — " +
            "markazning qaysi muammosi qaytarilyapti), navbat qanday ishlanyapti va nimani tuzatish " +
            "kerak. Javob matnlarini o'qib TAKRORLANUVCHI naqshlarni ajrat (masalan \"to'lov\", \"kasal\", " +
            "\"o'qituvchi\", \"vaqt mos emas\") va har birini SON bilan asosla.\n\n" +
            "Natijani FAQAT O'ZBEK TILIDA (lotin alifbosi), QUYIDAGI JSON sxemasida QAYTAR " +
            "(boshqa hech narsa yozma, faqat JSON):\n" +
            "{\n" +
            "  \"umumiy\": \"2-4 jumla — bo'limning shu davrdagi holati, ochiq va aniq\",\n" +
            "  \"sabablar\": \"qaysi sabablar ustunlik qilmoqda va ular markazning qaysi muammosini ko'rsatadi\",\n" +
            "  \"javoblar\": \"javob matnlaridagi TAKRORLANUVCHI naqshlar — odamlar aynan nima deyishyapti\",\n" +
            "  \"sifat\": \"aloqa sifati: ko'tarmagan/band ulushi, javob yozilmagan urinishlar, yozuvlarning to'liqligi\",\n" +
            "  \"xodimlar\": \"xodimlar kesimi: kim ko'p ishladi, kimning natijasi past — raqam bilan\",\n" +
            "  \"ozgarishlar\": \"oldingi tahlilga nisbatan o'zgarishlar (yo'q bo'lsa bo'sh)\",\n" +
            "  \"kuchli\": [\"kuchli tomon — raqam bilan\", ...],\n" +
            "  \"zaif\": [\"zaif tomon — raqam bilan\", ...],\n" +
            "  \"xavflar\": [\"e'tiborsiz qolsa nima yo'qotiladi\", ...],\n" +
            "  \"tavsiyalar\": [\"rahbar/operatorga aniq, bajariladigan tavsiya\", ...],\n" +
            "  \"baholar\": { \"qamrov\": 0-100, \"aloqa\": 0-100, \"natija\": 0-100, \"sifat\": 0-100, \"umumiy\": 0-100 },\n" +
            "  \"trend\": \"yaxshilanmoqda\" yoki \"barqaror\" yoki \"yomonlashmoqda\"\n" +
            "}\n\n" +
            "Qoidalar: TANQIDIY bo'l — muammoni yumshatma va oqibatini ko'rsat, lekin HAR bir da'voni " +
            "berilgan RAQAM bilan asosla (masalan \"42 urinishdan 12 tasida javob yozilmagan — 29%\"). " +
            "Hech narsani TO'QIB CHIQARMA: berilgan raqamlar va matnlardan tashqarida hech narsa yo'q; " +
            "ma'lumot kam bo'lsa (davr qisqa, urinish oz) buni OCHIQ ayt va xulosani shartli qilib qo'y. " +
            "Javob namunalarini SO'ZMA-SO'Z uzun ko'chirma — umumlashtir. \"baholar\" — 0..100 butun " +
            "sonlar: qamrov (navbat ishlanyaptimi — ochiq va muddati o'tganlarga nisbatan urinishlar), " +
            "aloqa (gaplashilganlar ulushi), natija (bog'lanishlar hal bo'lish bilan tugadimi), " +
            "sifat (\"javobi nima dedi\" to'ldirilyaptimi va mazmunlimi), umumiy (boshqalarning " +
            "umumlashmasi). " + prevContext + "\n\n" +
            "Ko'rsatkichlar (JSON):\n" + metricsJson;

        var (ok, text, err) = await GeminiService.GenerateAsync(
            AppSecrets.GeminiApiKey, model, prompt, jsonMode: true);
        if (!ok) return new ContactAiResponseDto(false, false, null, err);

        var ai = ParseNarrative(text);
        if (ai is null)
            return new ContactAiResponseDto(false, false, null,
                "AI javobini o'qib bo'lmadi (format xato). Qaytadan urinib ko'ring.");

        var rec = new ContactAiAnalysis
        {
            FromDate = fromDate,
            ToDate = toDate,
            Date = todayKey,
            CreatedAt = AppClock.Iso(),
            Model = model,
            Summary = Trim(ai.Umumiy, 600),
            OverallScore = Math.Clamp(ai.Baholar.Umumiy, 0, 100),
            ResultJson = JsonSerializer.Serialize(new Stored(ai, metrics), JsonOpts),
        };
        db.ContactAiAnalyses.Add(rec);
        await db.SaveChangesAsync(ct);

        return new ContactAiResponseDto(true, false, ToRecordDto(rec), null);
    }

    // ==================== Yordamchilar ====================

    private static ContactAiRecordDto? ToRecordDto(ContactAiAnalysis a)
    {
        var s = ParseStored(a.ResultJson);
        if (s is null) return null;
        return new ContactAiRecordDto(
            a.Id, a.FromDate, a.ToDate, a.Date, a.CreatedAt, a.Model, a.OverallScore, s.Ai, s.Metrics);
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
    private static ContactAiNarrativeDto? ParseNarrative(string text)
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
            var r = JsonSerializer.Deserialize<ContactAiNarrativeDto>(t, JsonOpts);
            return r is null ? null : Sanitize(r);
        }
        catch { return null; }
    }

    private static ContactAiNarrativeDto Sanitize(ContactAiNarrativeDto r)
    {
        var b = r.Baholar ?? new ContactAiScoresDto(0, 0, 0, 0, 0);
        return new ContactAiNarrativeDto(
            r.Umumiy ?? "", r.Sabablar ?? "", r.Javoblar ?? "", r.Sifat ?? "", r.Xodimlar ?? "",
            r.Ozgarishlar ?? "",
            r.Kuchli ?? new List<string>(), r.Zaif ?? new List<string>(),
            r.Xavflar ?? new List<string>(), r.Tavsiyalar ?? new List<string>(),
            new ContactAiScoresDto(
                Math.Clamp(b.Qamrov, 0, 100), Math.Clamp(b.Aloqa, 0, 100),
                Math.Clamp(b.Natija, 0, 100), Math.Clamp(b.Sifat, 0, 100),
                Math.Clamp(b.Umumiy, 0, 100)),
            r.Trend ?? "");
    }

    private static string Trim(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..max];
    }
}
