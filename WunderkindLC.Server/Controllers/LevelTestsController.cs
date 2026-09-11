using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>Daraja testi — admin CRUD + ommaviy URL + natijalar (topshiruvchilar lid bo'lib tushadi).</summary>
[ApiController]
[Authorize]
[AdminPerm("schedule.levelTests")]
[Route("api/admin/level-tests")]
public class LevelTestsController(AppDbContext db, DataCache dataCache, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LevelTestListDto>>> GetAll()
    {
        var tests = await db.LevelTests.AsNoTracking().ToListAsync();
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
        // Savol/topshiruv sonlari DB tomonda agregatsiya qilinadi (butun jadval yuklanmaydi).
        var qCounts = (await db.LevelTestQuestions.GroupBy(q => q.TestId)
                .Select(g => new { g.Key, C = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.C);
        var sCounts = (await db.LevelTestSubmissions.GroupBy(s => s.TestId)
                .Select(g => new { g.Key, C = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.C);
        return tests
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new LevelTestListDto(
                t.Id, t.Title, t.CourseId, subjects.GetValueOrDefault(t.CourseId, ""), t.Slug,
                t.IsActive, t.CreatedAt,
                qCounts.GetValueOrDefault(t.Id, 0), sCounts.GetValueOrDefault(t.Id, 0)))
            .ToList();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LevelTestDetailDto>> Get(string id)
    {
        var test = await db.LevelTests.FindAsync(id);
        if (test is null) return NotFound();
        return await LevelTestService.BuildDetailAsync(db, test);
    }

    [HttpPost]
    public async Task<ActionResult<LevelTestDetailDto>> Create(LevelTestPayload p)
    {
        var test = new LevelTest
        {
            Title = (p.Title ?? "").Trim(),
            CourseId = p.CourseId ?? "",
            Intro = p.Intro ?? "",
            IsActive = p.IsActive,
            Slug = await LevelTestService.GenerateSlugAsync(db, p.Title ?? ""),
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.LevelTests.Add(test);
        WriteQuestions(test.Id, p.Questions);
        WriteBands(test.Id, p.Bands);
        await db.SaveChangesAsync();
        return await LevelTestService.BuildDetailAsync(db, test);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<LevelTestDetailDto>> Update(string id, LevelTestPayload p)
    {
        var test = await db.LevelTests.FindAsync(id);
        if (test is null) return NotFound();
        test.Title = (p.Title ?? "").Trim();
        test.CourseId = p.CourseId ?? "";
        test.Intro = p.Intro ?? "";
        test.IsActive = p.IsActive;

        // Savol va diapazonlar — to'liq almashtiriladi (oddiy va ishonchli).
        db.LevelTestQuestions.RemoveRange(db.LevelTestQuestions.Where(q => q.TestId == id));
        db.LevelTestBands.RemoveRange(db.LevelTestBands.Where(b => b.TestId == id));
        WriteQuestions(id, p.Questions);
        WriteBands(id, p.Bands);
        await db.SaveChangesAsync();
        return await LevelTestService.BuildDetailAsync(db, test);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var test = await db.LevelTests.FindAsync(id);
        if (test is null) return NotFound();
        db.LevelTestQuestions.RemoveRange(db.LevelTestQuestions.Where(q => q.TestId == id));
        db.LevelTestBands.RemoveRange(db.LevelTestBands.Where(b => b.TestId == id));
        db.LevelTestSubmissions.RemoveRange(db.LevelTestSubmissions.Where(s => s.TestId == id));
        db.LevelTestInvites.RemoveRange(db.LevelTestInvites.Where(i => i.TestId == id));
        db.LevelTests.Remove(test);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bu testga yuborilgan bir martalik havolalar (invite) — lid + SMS holati + ishlangani.
    /// (Lidlarning ismi/telefoni qaytadi — o'qish darvozalangan.)</summary>
    [HttpGet("{id}/invites")]
    [AdminPerm("schedule.levelTests", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<LevelTestInviteDto>>> Invites(string id)
    {
        var invites = await db.LevelTestInvites.AsNoTracking().Where(i => i.TestId == id)
            .OrderByDescending(i => i.CreatedAt).ToListAsync();
        var leadIds = invites.Select(i => i.LeadId).Distinct().ToList();
        var leads = (await db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id)).ToListAsync())
            .ToDictionary(l => l.Id, l => l);
        return invites.Select(i =>
        {
            leads.TryGetValue(i.LeadId, out var l);
            return new LevelTestInviteDto(
                i.Id, i.TestId, i.LeadId, l?.FullName ?? "(o'chirilgan lid)", l?.Phone ?? "",
                i.SmsStatus, i.CreatedAt, !string.IsNullOrEmpty(i.UsedAt), i.UsedAt, i.Percent, i.Level);
        }).ToList();
    }

    /// <summary>
    /// BARCHA daraja testlari bo'yicha UMUMIY statistika — "Formalar → Test statistikasi" sahifasi
    /// (testga KIRMASDAN, hammasini bir ekranda solishtirib ko'rish uchun).
    ///
    /// <para>Hisob butun topshiruvlar to'plami ustida boradi, shuning uchun natija
    /// <see cref="DataCache"/> da: bog'liq jadvallardan biri o'zgarsa kesh AVTO-eskiradi, TTL
    /// faqat zaxira (lid formalari statistikasi bilan bir xil yondashuv).</para>
    ///
    /// <para>⚠️ <c>ReadRequiresPerm</c> — javobda abituriyentlarning TELEFONLARI va endi
    /// TO'LOV summalari bor. Odatda <see cref="AdminPermAttribute"/> GET'ni har qanday xodimga
    /// ochadi (bo'limlararo o'qish uchun), bu yerda esa bunga hojat yo'q: sahifani faqat
    /// <c>schedule</c> ruxsati bor xodim ochadi. Sinf darajasida qo'yilmadi — <c>GET /</c> (testlar
    /// ro'yxati) lidlar bo'limidagi "test yuborish" oynasiga kerak (`LeadDetailModal`), uni yopish
    /// `leads` ruxsatli xodimning ishini buzardi.</para>
    /// </summary>
    [HttpGet("overall-stats")]
    [AdminPerm("schedule.levelTests", ReadRequiresPerm = true)]
    public async Task<ActionResult<LevelTestOverallStatsDto>> OverallStats() =>
        await dataCache.GetOrCreateAsync(
            "level-tests:overall-stats",
            new[]
            {
                nameof(LevelTest), nameof(LevelTestSubmission), nameof(LevelTestInvite),
                nameof(Lead), nameof(LeadStage), nameof(StudentGroup), nameof(FinanceTransaction),
                // Guruh nomi va o'qituvchi F.I.Sh ham javobda qaytadi (`LeadOutcome`) — ular
                // o'zgarganda kesh eskirmasa, sahifada eski nom TTL tugagunicha turib qolardi.
                nameof(Group), nameof(Teacher),
            },
            TimeSpan.FromMinutes(10),
            LevelTestService.BuildOverallStatsAsync);

    /// <summary>Natijalar — testni topshirganlar (har biri CRM'da lid; ism/telefon qaytadi).</summary>
    [HttpGet("{id}/submissions")]
    [AdminPerm("schedule.levelTests", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<LevelTestSubmissionDto>>> Submissions(string id)
    {
        var subs = await db.LevelTestSubmissions.AsNoTracking().Where(s => s.TestId == id)
            .OrderByDescending(s => s.CreatedAt).ToListAsync();
        return subs.Select(s => new LevelTestSubmissionDto(
            s.Id, s.FullName, s.Phone, s.Age, s.Score, s.Total, s.Percent, s.Level, s.CreatedAt, s.LeadId,
            ParseSurvey(s.SurveyJson))).ToList();
    }

    /// <summary>SurveyJson → DTO ro'yxati (buzilgan bo'lsa bo'sh).</summary>
    private static List<SurveyAnswerDto> ParseSurvey(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<SurveyAnswerDto>>(json) ?? new(); }
        catch { return new(); }
    }

    /// <summary>BITTA test STATISTIKASI — topshiruvchilardan nechtasi AKTIV o'quvchi bo'ldi,
    /// nechtasi PUL to'ladi, qaysi guruh(lar)ga qo'shilgani va o'qituvchisi (FISH).
    /// (Telefon + to'lov qaytgani uchun o'qish ham darvozalangan — `overall-stats` dagi izohga qarang.)</summary>
    [HttpGet("{id}/stats")]
    [AdminPerm("schedule.levelTests", ReadRequiresPerm = true)]
    public async Task<ActionResult<LevelTestStatsDto>> Stats(string id)
    {
        var subs = await db.LevelTestSubmissions.AsNoTracking().Where(s => s.TestId == id)
            .OrderByDescending(s => s.CreatedAt).ToListAsync();
        var rows = await LevelTestService.BuildStatRowsAsync(db, subs);
        // ⚠️ Sonlar TAKRORSIZ lid bo'yicha: bir odam testni ikki marta topshirsa "aktiv" ham,
        // summa ham ikki marta sanalardi — va o'sha test UMUMIY statistika sahifasida boshqacha
        // raqam ko'rsatardi (u har doim takrorsiz sanaydi). Qatorlar ro'yxatida esa ikkala
        // topshiriq ham ko'rinaveradi. Qoida yagona joyda: `LevelTestService.DistinctByLead`.
        var byLead = LevelTestService.DistinctByLead(rows);
        return new LevelTestStatsDto(
            rows.Count, byLead.Count(r => r.Active), rows,
            byLead.Count(r => r.Paid), byLead.Sum(r => Math.Max(0m, r.PaidTotal)),
            byLead.Count);
    }

    // ==================== AI tahlil (voronka) ====================

    /// <summary>
    /// Daraja testlari voronkasining saqlangan AI tahlillari (eng yangisi birinchi).
    ///
    /// <para>⚠️ <c>ReadRequiresPerm</c> — <c>overall-stats</c> bilan bir xil sabab: saqlangan
    /// tahlilning ichida O'SHA statistika (voronka raqamlari, tushum) turadi, ya'ni o'qishni
    /// har qanday xodimga ochib bo'lmaydi.</para>
    ///
    /// <para>⚠️ Marshrut <c>ai-analyses</c> — statik segment <c>{id}</c> dan USTUN (ASP.NET Core
    /// marshrut ustuvorligi), ya'ni <c>GET {id}</c> uni id deb qabul qilmaydi. Bu yerda
    /// <c>overall-stats</c> allaqachon shu tarzda ishlab turibdi.</para>
    /// </summary>
    [HttpGet("ai-analyses")]
    [AdminPerm("schedule.levelTests", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<FunnelAiRecordDto>>> AiAnalyses(CancellationToken ct) =>
        await FunnelAiAnalysisService.HistoryAsync(db, FunnelAiAnalysisService.KindLevelTests, ct);

    /// <summary>
    /// Daraja testlari voronkasini Gemini orqali TANQIDIY tahlil qiladi (kuniga bir marta —
    /// bugungi yozuv bo'lsa Gemini chaqirilmaydi, mavjudi qaytadi).
    ///
    /// <para>⚠️ Auditga YOZILMAYDI: tahlil hech qanday ma'lumotni o'zgartirmaydi
    /// (`.claude/rules/audit.md` — AI tahlil qamrovda ATAYIN yo'q).</para>
    /// </summary>
    [HttpPost("ai-analysis")]
    public async Task<ActionResult<FunnelAiResponseDto>> AiAnalysis(CancellationToken ct) =>
        await FunnelAiAnalysisService.GenerateAsync(db, config, FunnelAiAnalysisService.KindLevelTests, ct);

    private void WriteQuestions(string testId, List<LevelTestQuestionInput>? questions)
    {
        if (questions is null) return;
        var order = 0;
        foreach (var q in questions)
        {
            if (string.IsNullOrWhiteSpace(q.Text)) continue;
            var opts = (q.Options ?? new()).Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).ToList();
            if (opts.Count < 2) continue; // kamida 2 variant
            var kind = q.Kind == "survey" ? "survey" : "question";
            var correct = q.CorrectIndex >= 0 && q.CorrectIndex < opts.Count ? q.CorrectIndex : 0;
            db.LevelTestQuestions.Add(new LevelTestQuestion
            {
                TestId = testId, Text = q.Text.Trim(), Options = opts, CorrectIndex = correct,
                Kind = kind, Multiple = kind == "survey" && q.Multiple, Order = order++,
            });
        }
    }

    private void WriteBands(string testId, List<LevelTestBandInput>? bands)
    {
        if (bands is null) return;
        var order = 0;
        foreach (var b in bands.OrderBy(x => x.MinPercent))
        {
            if (string.IsNullOrWhiteSpace(b.Label)) continue;
            var min = Math.Clamp(b.MinPercent, 0, 100);
            db.LevelTestBands.Add(new LevelTestBand
            {
                TestId = testId, Label = b.Label.Trim(), MinPercent = min, Order = order++,
            });
        }
    }
}
