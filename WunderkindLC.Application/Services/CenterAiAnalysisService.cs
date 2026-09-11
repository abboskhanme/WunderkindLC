using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Markaz (butun o'quv markazi) kunlik AI tahlili (Gemini). Bir kun oldingi va joriy oy
/// ma'lumotlari asosida: tushum prognozi, o'quvchilar baholari dinamikasi, yangi lidlar,
/// ketgan o'quvchilar sabablari va umumiy tavsiyalar. KUNIGA BIR MARTA (ertalab fon xizmati
/// orqali yoki admin qo'lda). Raqamlar DETERMINISTIK hisoblanadi (AI emas); AI faqat narrativ
/// (o'zbek tilida) yozadi. Natija <see cref="CenterAiAnalysis"/> sifatida saqlanadi.
/// </summary>
public static class CenterAiAnalysisService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>To'liq saqlanadigan natija (AI narrativ + deterministik raqamlar).</summary>
    private record Stored(CenterAiNarrativeDto Ai, CenterRevenueDto Revenue, CenterMetricsDto Metrics);

    /// <summary>Bugungi (yoki eng so'nggi) saqlangan tahlilni qaytaradi (null — yo'q bo'lsa).</summary>
    public static async Task<CenterAiRecordDto?> GetLatestAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var rec = await db.CenterAiAnalyses.FirstOrDefaultAsync(a => a.Date == today, ct)
                  ?? await db.CenterAiAnalyses.OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
                      .FirstOrDefaultAsync(ct);
        return rec is null ? null : ToRecordDto(rec);
    }

    /// <summary>Tahlillar tarixi (eng yangisi birinchi).</summary>
    public static async Task<List<CenterAiHistoryItemDto>> HistoryAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.CenterAiAnalyses
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(a => new CenterAiHistoryItemDto(a.Id, a.Date, a.CreatedAt, a.Health, a.Summary)).ToList();
    }

    /// <summary>Markaz AI tahlilini yaratadi va saqlaydi. KUNIGA BIR MARTA: bugungi yozuv bo'lsa,
    /// <paramref name="force"/>=false bo'lganda Gemini chaqirilmaydi (mavjud yozuv qaytadi).
    /// force=true (superadmin) bo'lsa bugungi yozuv qayta yaratiladi.</summary>
    public static async Task<CenterAiResponseDto> GenerateAsync(
        IAppDbContext db, IConfiguration? config, bool force = false, CancellationToken ct = default)
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var existing = await db.CenterAiAnalyses.FirstOrDefaultAsync(a => a.Date == today, ct);
        if (existing is not null && !force)
            return new CenterAiResponseDto(true, true, ToRecordDto(existing), null);

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var model = GeminiService.ResolveModel(config);
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return new CenterAiResponseDto(false, false, null,
                "Gemini API kaliti sozlanmagan. Sozlamalar → AI Tahlil (Gemini) bo'limidan kalit kiriting.");

        var (revenue, metrics, snapshotJson) = await BuildSnapshotAsync(db, ct);

        var prompt =
            "Sen o'quv markazi rahbari uchun tajribali biznes-tahlilchisan. Quyida markazning BUGUNGI " +
            "holati raqamlar bilan JSON ko'rinishida: moliya (shu oy kutilayotgan hisob, yig'ilgan tushum, " +
            "jami qarzdorlik, kechagi tushum, oy oxiri prognozi, oxirgi 14 kunlik tushum), o'quvchilar " +
            "(aktiv soni, shu oy va kecha yangi lidlar, konversiya, ketganlar soni), baholar dinamikasi " +
            "(shu oy va o'tgan oy o'rtacha bahosi), lidlar manbasi, ketish sabablari.\n\n" +
            "Vazifa: bir kun oldingi va shu oygacha ma'lumotga tayanib CHUQUR tahlil qil va FAQAT O'ZBEK " +
            "TILIDA (lotin alifbosi) natijani QUYIDAGI JSON sxemasida QAYTAR (boshqa hech narsa yozma, faqat JSON):\n" +
            "{\n" +
            "  \"umumiy\": \"2-4 jumla — markazning umumiy holati\",\n" +
            "  \"tushumTahlili\": \"tushum/qarzdorlik holati va oy oxiri prognozi haqida tahlil\",\n" +
            "  \"baholarTahlili\": \"o'quvchilar baholari dinamikasi (shu oy vs o'tgan oy) haqida tahlil\",\n" +
            "  \"lidlar\": \"yangi lidlar, manbalari va konversiya haqida tahlil\",\n" +
            "  \"ketganlar\": \"ketgan o'quvchilar va sabablari, ushlab qolish bo'yicha tahlil\",\n" +
            "  \"xavflar\": [\"e'tibor talab qiladigan xavf/muammo\", ...],\n" +
            "  \"tavsiyalar\": [\"rahbarga aniq amaliy tavsiya\", ...],\n" +
            "  \"salomatlik\": 0-100,\n" +
            "  \"trend\": \"yaxshilanmoqda\" yoki \"barqaror\" yoki \"yomonlashmoqda\"\n" +
            "}\n\n" +
            "Qoidalar: \"salomatlik\" — markazning umumiy salomatligi (moliya+o'quvchi oqimi+baholar) 0..100 " +
            "butun son. Faqat berilgan raqamlarga tayan, to'qib chiqarma. Har matn maydoni qisqa va aniq, " +
            "rahbarga foydali. Summalarni so'mda ko'rsat.\n\n" +
            "Markaz ma'lumotlari (JSON):\n" + snapshotJson;

        var (ok, text, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, prompt, jsonMode: true);
        if (!ok) return new CenterAiResponseDto(false, false, null, err);

        var ai = ParseNarrative(text);
        if (ai is null)
            return new CenterAiResponseDto(false, false, null,
                "AI javobini o'qib bo'lmadi (format xato). Qaytadan urinib ko'ring.");

        var stored = new Stored(ai, revenue, metrics);
        var resultJson = JsonSerializer.Serialize(stored, JsonOpts);

        if (existing is not null)
        {
            existing.CreatedAt = AppClock.Iso();
            existing.Model = model;
            existing.Summary = Trim(ai.Umumiy, 600);
            existing.Health = Math.Clamp(ai.Salomatlik, 0, 100);
            existing.ResultJson = resultJson;
        }
        else
        {
            existing = new CenterAiAnalysis
            {
                Date = today,
                CreatedAt = AppClock.Iso(),
                Model = model,
                Summary = Trim(ai.Umumiy, 600),
                Health = Math.Clamp(ai.Salomatlik, 0, 100),
                ResultJson = resultJson,
            };
            db.CenterAiAnalyses.Add(existing);
        }
        await db.SaveChangesAsync(ct);
        return new CenterAiResponseDto(true, false, ToRecordDto(existing), null);
    }

    /// <summary>Markazning deterministik ko'rsatkichlarini hisoblaydi (moliya + ko'rsatkichlar) va
    /// AI promptiga beriladigan JSON snapshotni qaytaradi.</summary>
    private static async Task<(CenterRevenueDto Revenue, CenterMetricsDto Metrics, string SnapshotJson)>
        BuildSnapshotAsync(IAppDbContext db, CancellationToken ct)
    {
        var now = AppClock.Now;
        var today = AppClock.Today;
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd");
        var cur = now.ToString("yyyy-MM");
        var prev = now.AddMonths(-1).ToString("yyyy-MM");
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var dayOfMonth = now.Day;
        var from14 = today.AddDays(-13).ToString("yyyy-MM-dd");

        // ----- Moliya -----
        var charges = await db.MonthlyCharges.Where(c => c.Month == cur).ToListAsync(ct);
        decimal expected = charges.Sum(c => Math.Max(0m, c.Amount - c.Discount));

        var income = await db.FinanceTransactions
            .Where(t => t.Direction == "income" && string.Compare(t.Date, from14) >= 0)
            .Select(t => new { t.Date, t.Amount }).ToListAsync(ct);
        var incomeMonth = await db.FinanceTransactions
            .Where(t => t.Direction == "income" && t.Date.StartsWith(cur))
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        decimal yesterdayIncome = income.Where(t => t.Date == yesterday).Sum(t => t.Amount);

        decimal outstanding = await db.Students.Where(s => !s.IsArchived && s.Balance < 0)
            .SumAsync(s => (decimal?)(-s.Balance), ct) ?? 0m;

        decimal predicted = dayOfMonth > 0
            ? Math.Round(incomeMonth / dayOfMonth * daysInMonth, 0)
            : incomeMonth;

        var incomeByDay = new List<CenterPointDto>();
        for (var i = 0; i < 14; i++)
        {
            var d = today.AddDays(-13 + i).ToString("yyyy-MM-dd");
            incomeByDay.Add(new CenterPointDto(d.Substring(5), (double)income.Where(t => t.Date == d).Sum(t => t.Amount)));
        }

        var revenue = new CenterRevenueDto(expected, incomeMonth, outstanding, yesterdayIncome, predicted);

        // ----- O'quvchilar / a'zoliklar -----
        var archivedIds = (await db.Students.Where(s => s.IsArchived).Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        var memberships = await db.StudentGroups
            .Select(sg => new { sg.StudentId, sg.Status, sg.IsActive, sg.LeftAt }).ToListAsync(ct);
        int activeStudents = memberships
            .Where(m => m.IsActive && m.Status == "active" && !archivedIds.Contains(m.StudentId))
            .Select(m => m.StudentId).Distinct().Count();
        int departed = memberships.Count(m => !m.IsActive && !string.IsNullOrEmpty(m.LeftAt) && m.LeftAt!.StartsWith(cur));

        // ----- Lidlar -----
        var leads = await db.Leads.Select(l => new { l.Source, l.CreatedAt }).ToListAsync(ct);
        int newLeadsMonth = leads.Count(l => (l.CreatedAt ?? "").StartsWith(cur));
        int newLeadsYesterday = leads.Count(l => (l.CreatedAt ?? "").StartsWith(yesterday));
        var leadsBySource = leads.Where(l => (l.CreatedAt ?? "").StartsWith(cur))
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Source) ? "Boshqa" : l.Source)
            .Select(g => new CenterPointDto(g.Key, g.Count()))
            .OrderByDescending(p => p.Value).ToList();
        int converted = await db.LeadEvents.CountAsync(e => e.Type == "convert" && e.CreatedAt.StartsWith(cur), ct);

        // ----- Ketish sabablari (arxiv yozuvlari) -----
        var archivedThisMonth = await db.ArchivedRecords
            .Where(a => a.Type == "student" && a.DeletedAt.StartsWith(cur))
            .Select(a => a.Reason).ToListAsync(ct);
        var departureReasons = archivedThisMonth
            .GroupBy(r => string.IsNullOrWhiteSpace(r) ? "Sabab ko'rsatilmagan" : r!)
            .Select(g => new CenterPointDto(g.Key, g.Count()))
            .OrderByDescending(p => p.Value).ToList();

        // ----- Baholar dinamikasi -----
        var gradeRows = await db.JournalEntries
            .Where(e => e.Grade != null && (e.Date.StartsWith(cur) || e.Date.StartsWith(prev)))
            .Select(e => new { e.Date, e.Grade }).ToListAsync(ct);
        double Avg(string m)
        {
            var g = gradeRows.Where(r => r.Date.StartsWith(m)).Select(r => (double)r.Grade!.Value).ToList();
            return g.Count > 0 ? Math.Round(g.Average(), 2) : 0;
        }
        double avgCur = Avg(cur), avgPrev = Avg(prev);

        var metrics = new CenterMetricsDto(
            activeStudents, newLeadsMonth, newLeadsYesterday, converted, departed,
            avgCur, avgPrev, leadsBySource, departureReasons, incomeByDay);

        // ----- Prompt uchun snapshot -----
        var snapshot = new
        {
            sana = today.ToString("yyyy-MM-dd"),
            oy = cur,
            oyKuni = dayOfMonth,
            oydaKunlar = daysInMonth,
            moliya = new
            {
                kutilayotganHisob = expected,
                yigilganTushum = incomeMonth,
                qarzdorlik = outstanding,
                kechagiTushum = yesterdayIncome,
                oyOxiriPrognoz = predicted,
                oxirgi14Kun = incomeByDay,
            },
            oquvchilar = new
            {
                aktiv = activeStudents,
                yangiLidlarOy = newLeadsMonth,
                yangiLidlarKecha = newLeadsYesterday,
                konversiya = converted,
                ketganlar = departed,
            },
            baholar = new { shuOy = avgCur, otganOy = avgPrev },
            lidlarManbasi = leadsBySource,
            ketishSabablari = departureReasons,
        };
        var snapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        });

        return (revenue, metrics, snapshotJson);
    }

    private static CenterAiRecordDto? ToRecordDto(CenterAiAnalysis a)
    {
        var s = ParseStored(a.ResultJson);
        if (s is null) return null;
        return new CenterAiRecordDto(a.Id, a.Date, a.CreatedAt, a.Model, a.Health, s.Ai, s.Revenue, s.Metrics);
    }

    private static Stored? ParseStored(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Stored>(json, JsonOpts);
            if (s is null) return null;
            return new Stored(SanitizeNarrative(s.Ai), s.Revenue, s.Metrics);
        }
        catch { return null; }
    }

    /// <summary>Gemini JSON javobini narrativga aylantiradi (fence tozalash + null to'ldirish).</summary>
    private static CenterAiNarrativeDto? ParseNarrative(string text)
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
            var r = JsonSerializer.Deserialize<CenterAiNarrativeDto>(t, JsonOpts);
            return r is null ? null : SanitizeNarrative(r);
        }
        catch { return null; }
    }

    private static CenterAiNarrativeDto SanitizeNarrative(CenterAiNarrativeDto r) => new(
        r.Umumiy ?? "", r.TushumTahlili ?? "", r.BaholarTahlili ?? "",
        r.Lidlar ?? "", r.Ketganlar ?? "",
        r.Xavflar ?? new List<string>(), r.Tavsiyalar ?? new List<string>(),
        Math.Clamp(r.Salomatlik, 0, 100), r.Trend ?? "");

    private static string Trim(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..max];
    }
}
