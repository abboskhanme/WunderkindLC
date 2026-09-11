using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using System.Text.Json;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'quv dasturi (Modul → Mavzu → Dars → Topshiriq) — Kurs (Subject) dan MUSTAQIL, standalone
/// <c>Curriculum</c> sifatida yaratiladi. Bir dastur bir nechta kursga, bir kurs bir nechta dasturga
/// biriktirilishi mumkin (<c>SubjectCurriculum</c> — ko'p-ko'pga, boshqaruvi <c>SubjectsController</c>da).
/// Guruh ko'rinishida (<see cref="CurriculumForecast"/>) guruh kursiga biriktirilgan BARCHA dasturlar
/// bitta ketma-ket ro'yxatga birlashtiriladi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule.curricula")]
[Route("api/admin/curriculum")]
public class CurriculumController(AppDbContext db) : ControllerBase
{
    // ---- O'quv dasturlari ro'yxati (top-level) ----

    [HttpGet]
    public async Task<ActionResult<List<CurriculumSummaryDto>>> ListCurricula()
    {
        var curricula = await db.Curricula.OrderBy(c => c.Order).ToListAsync();
        var curriculumIds = curricula.Select(c => c.Id).ToList();

        var modules = await db.CourseModules.Where(m => curriculumIds.Contains(m.CurriculumId)).ToListAsync();
        var topics = await db.CourseTopics.Where(t => curriculumIds.Contains(t.CurriculumId)).ToListAsync();
        var items = await db.CourseItems.Where(i => curriculumIds.Contains(i.CurriculumId)).ToListAsync();
        var subjectCounts = (await db.SubjectCurricula.Where(sc => curriculumIds.Contains(sc.CurriculumId))
                .Select(sc => sc.CurriculumId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var itemIds = items.Select(i => i.Id).ToList();
        var qCounts = (await db.CourseQuestions.Where(q => itemIds.Contains(q.ItemId))
                .Select(q => q.ItemId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        return curricula.Select(c => new CurriculumSummaryDto(
            c.Id, c.Name, c.Note, c.Order, c.CreatedAt,
            modules.Count(m => m.CurriculumId == c.Id),
            topics.Count(t => t.CurriculumId == c.Id),
            items.Count(i => i.CurriculumId == c.Id),
            items.Where(i => i.CurriculumId == c.Id).Count(i => IsReady(i, qCounts)),
            subjectCounts.GetValueOrDefault(c.Id, 0)
        )).ToList();
    }

    [HttpPost]
    public async Task<ActionResult> CreateCurriculum(CurriculumInput input)
    {
        var maxOrder = await db.Curricula.Select(c => (int?)c.Order).MaxAsync() ?? -1;
        var curriculum = new Curriculum
        {
            Name = input.Name,
            Note = input.Note ?? "",
            Order = maxOrder + 1,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.Curricula.Add(curriculum);
        await db.SaveChangesAsync();
        return Ok(new { id = curriculum.Id });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateCurriculum(string id, CurriculumInput input)
    {
        var curriculum = await db.Curricula.FindAsync(id);
        if (curriculum == null) return NotFound();
        curriculum.Name = input.Name;
        curriculum.Note = input.Note ?? "";
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Dasturni butunlay o'chiradi: barcha modul/mavzu/dars/topshiriqlari, ularga bog'liq
    /// progress/test savollari VA barcha kurslarga biriktirilgan holatlar (SubjectCurriculum) bilan
    /// birga. Bu amalni qaytarib bo'lmaydi.</summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteCurriculum(string id)
    {
        var curriculum = await db.Curricula.FindAsync(id);
        if (curriculum == null) return NotFound();

        var itemIds = await db.CourseItems.Where(i => i.CurriculumId == id).Select(i => i.Id).ToListAsync();
        await db.CourseProgresses.Where(p => itemIds.Contains(p.ItemId)).ExecuteDeleteAsync();
        await db.CourseQuestions.Where(q => itemIds.Contains(q.ItemId)).ExecuteDeleteAsync();
        await db.CourseItems.Where(i => i.CurriculumId == id).ExecuteDeleteAsync();
        await db.CourseLessons.Where(s => s.CurriculumId == id).ExecuteDeleteAsync();
        await db.CourseTopics.Where(t => t.CurriculumId == id).ExecuteDeleteAsync();
        await db.CourseModules.Where(m => m.CurriculumId == id).ExecuteDeleteAsync();
        await db.SubjectCurricula.Where(sc => sc.CurriculumId == id).ExecuteDeleteAsync();
        db.Curricula.Remove(curriculum);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- To'liq sillabusni o'qish ----

    [HttpGet("{curriculumId}")]
    public async Task<ActionResult<CurriculumDto>> Get(string curriculumId)
    {
        var curriculum = await db.Curricula.FindAsync(curriculumId);
        if (curriculum == null) return NotFound();

        var modules = await db.CourseModules
            .Where(m => m.CurriculumId == curriculumId)
            .OrderBy(m => m.Order).ToListAsync();
        var topics = await db.CourseTopics
            .Where(t => t.CurriculumId == curriculumId)
            .OrderBy(t => t.Order).ToListAsync();
        var lessons = await db.CourseLessons
            .Where(s => s.CurriculumId == curriculumId)
            .OrderBy(s => s.Order).ToListAsync();
        var items = await db.CourseItems
            .Where(i => i.CurriculumId == curriculumId)
            .OrderBy(i => i.Order).ToListAsync();

        // Test darslarining savol sonini olamiz (meta + tayyorlik uchun).
        var itemIds = items.Select(i => i.Id).ToList();
        var qCounts = (await db.CourseQuestions.Where(q => itemIds.Contains(q.ItemId))
                .Select(q => q.ItemId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var moduleDtos = BuildModuleDtos(modules, topics, lessons, items, qCounts);
        return new CurriculumDto(curriculumId, curriculum.Name, moduleDtos);
    }

    private static List<CurriculumModuleDto> BuildModuleDtos(
        List<CourseModule> modules, List<CourseTopic> topics, List<CourseLesson> lessons, List<CourseItem> items,
        IReadOnlyDictionary<string, int> qCounts) =>
        modules.Select(m => new CurriculumModuleDto(
            m.Id, m.Name, m.Note, m.Order,
            topics.Where(t => t.ModuleId == m.Id).Select(t => new CurriculumTopicDto(
                t.Id, t.Title, t.Note, t.Order,
                lessons.Where(s => s.TopicId == t.Id).Select(s => new CurriculumLessonDto(
                    s.Id, s.Title, s.Note, s.Order,
                    items.Where(i => i.LessonId == s.Id).Select(i => ToItemDto(i, qCounts)).ToList()
                )).ToList()
            )).ToList()
        )).ToList();

    /// <summary>Topshiriq (CourseItem) → daraxt DTO: tur, meta (test savoli/lug'at so'zi soni),
    /// tayyorlik (o'z turiga mos maydon to'ldirilganmi), yaratilgan sana.</summary>
    private static CurriculumItemDto ToItemDto(CourseItem i, IReadOnlyDictionary<string, int> qCounts) =>
        new(i.Id, i.Text, i.Note, i.Order, i.Type, MetaFor(i, qCounts), IsReady(i, qCounts), i.CreatedAt,
            CountFor(i, qCounts));

    /// <summary>Topshiriq ICHIDAGI elementlar soni (jadvaldagi "Topshiriqlar soni" ustuni):
    /// mashqda gap/savol/juftlik, testda savol, lug'atda so'z soni. Media/matn turlarida 0.</summary>
    private static int CountFor(CourseItem i, IReadOnlyDictionary<string, int> qCounts)
    {
        if (i.Type == "exercise") return ExerciseItemCount(i.ExerciseJson);
        if (i.Type == "vocab") return ParseVocab(i.VocabJson).Count;
        return qCounts.GetValueOrDefault(i.Id, 0);
    }

    /// <summary>Mashq JSON'idagi elementlar soni. Tuzilma: <c>{ kind, &lt;oila&gt;: { items: [...] } }</c>
    /// (moslashtirishda <c>rows</c>, writing/speaking'da bitta <c>topic</c>), shuning uchun turga
    /// bog'lanmasdan umumiy tarzda sanaladi — yangi tur qo'shilganda bu joyni tahrirlash shart emas.</summary>
    private static int ExerciseItemCount(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return 0;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                if (prop.Value.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    return items.GetArrayLength();
                if (prop.Value.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                    return rows.GetArrayLength();
                if (prop.Value.TryGetProperty("topic", out var topic) && topic.ValueKind == JsonValueKind.String)
                    return string.IsNullOrWhiteSpace(topic.GetString()) ? 0 : 1;
            }
            return 0;
        }
        catch { return 0; }
    }

    private static bool IsReady(CourseItem i, IReadOnlyDictionary<string, int> qCounts) =>
        !string.IsNullOrWhiteSpace(i.VideoUrl) || !string.IsNullOrWhiteSpace(i.TextContent)
        || !string.IsNullOrWhiteSpace(i.AudioUrl) || !string.IsNullOrWhiteSpace(i.PdfUrl)
        || ParseVocab(i.VocabJson).Count > 0 || qCounts.GetValueOrDefault(i.Id, 0) > 0
        || ExerciseFilled(i);

    /// <summary>Interaktiv mashq to'ldirilganmi: tur tanlangan VA mazmuni bo'sh emas.</summary>
    private static bool ExerciseFilled(CourseItem i) =>
        !string.IsNullOrWhiteSpace(i.ExerciseKind) && !string.IsNullOrWhiteSpace(i.ExerciseJson);

    private static string MetaFor(CourseItem i, IReadOnlyDictionary<string, int> qCounts)
    {
        if (!string.IsNullOrWhiteSpace(i.Meta)) return i.Meta;
        var qc = qCounts.GetValueOrDefault(i.Id, 0);
        var vocabCount = ParseVocab(i.VocabJson).Count;
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(i.VideoUrl)) sections.Add("Video");
        if (!string.IsNullOrWhiteSpace(i.TextContent)) sections.Add("Matn");
        if (!string.IsNullOrWhiteSpace(i.AudioUrl)) sections.Add("Audio");
        if (!string.IsNullOrWhiteSpace(i.PdfUrl)) sections.Add("PDF");
        if (vocabCount > 0) sections.Add("Lug'at");
        if (qc > 0) sections.Add("Test");
        // Mashq turi tanlangan bo'lsa — hali to'ldirilmagan bo'lsa ham ko'rsatamiz (jadvalda
        // "Gap tuzish · so'z tartibi" deb turadi, tayyorlik esa alohida belgi bilan).
        if (!string.IsNullOrWhiteSpace(i.ExerciseKind)) sections.Add(ExerciseLabel(i.ExerciseKind));
        return string.Join(" · ", sections);
    }

    /// <summary>Mashq turining o'zbekcha nomi (daraxt/jadvaldagi meta yorlig'i uchun). Front-end
    /// katalogi (<c>exercise/catalog.ts</c>) bilan bir xil nomlar.</summary>
    private static string ExerciseLabel(string kind) => kind switch
    {
        "sentence-order" => "Gap tuzish · so'z tartibi",
        "sentence-audio" => "Gap tuzish · audio",
        "sentence-image" => "Gap tuzish · rasm",
        "sentence-choice" => "Gap tuzish · variant",
        "fill-choose" => "Bo'sh joy · variant",
        "fill-write" => "Bo'sh joy · yozish",
        "fill-audio" => "Bo'sh joy · audio",
        "fill-image" => "Bo'sh joy · rasm",
        "fill-media" => "Bo'sh joy · audio + rasm",
        "wordpick-plain" => "So'z tanlash",
        "wordpick-image" => "So'z tanlash · rasm",
        "wordpick-audio" => "So'z tanlash · audio",
        "wordfind-plain" => "So'z topish",
        "wordfind-image" => "So'z topish · rasm",
        "wordfind-audio" => "So'z topish · audio",
        "reading-choice" => "Reading · variant",
        "reading-fill" => "Reading · bo'sh joy",
        "reading-short" => "Reading · qisqa javob",
        "test-image" => "Test · rasmli",
        "test-imageopts" => "Test · rasmli variantlar",
        "test-audio" => "Test · audio",
        "writing" => "Writing",
        "speaking" => "Speaking",
        "matching-plain" => "Moslashtirish",
        "matching-reading" => "Moslashtirish · reading",
        "matching-audio" => "Moslashtirish · audio",
        _ => "Mashq",
    };

    private static readonly string[] AllowedTypes = { "text", "video", "audio", "vocab", "test", "pdf", "exercise" };
    private static string NormalizeType(string? t) => t is not null && AllowedTypes.Contains(t) ? t : "text";
    private static List<VocabEntryDto> ParseVocab(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<VocabEntryDto>>(json) ?? new(); }
        catch { return new(); }
    }

    // ---- Modul (dastur ichidagi 1-bosqich) ----

    [HttpPost("{curriculumId}/modules")]
    public async Task<ActionResult> CreateModule(string curriculumId, ModuleInput input)
    {
        var curriculum = await db.Curricula.FindAsync(curriculumId);
        if (curriculum == null) return NotFound();
        var maxOrder = await db.CourseModules
            .Where(m => m.CurriculumId == curriculumId)
            .Select(m => (int?)m.Order).MaxAsync() ?? -1;
        var module = new CourseModule
        {
            CurriculumId = curriculumId,
            Name = input.Name,
            Note = input.Note ?? "",
            Order = maxOrder + 1,
        };
        db.CourseModules.Add(module);
        await db.SaveChangesAsync();
        return Ok(new { id = module.Id });
    }

    [HttpPut("modules/{id}")]
    public async Task<ActionResult> UpdateModule(string id, ModuleInput input)
    {
        var module = await db.CourseModules.FindAsync(id);
        if (module == null) return NotFound();
        module.Name = input.Name;
        module.Note = input.Note ?? "";
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("modules/{id}")]
    public async Task<ActionResult> DeleteModule(string id)
    {
        var module = await db.CourseModules.FindAsync(id);
        if (module == null) return NotFound();

        var topicIds = await db.CourseTopics
            .Where(t => t.ModuleId == id).Select(t => t.Id).ToListAsync();
        var lessonIds = await db.CourseLessons
            .Where(s => topicIds.Contains(s.TopicId)).Select(s => s.Id).ToListAsync();
        var itemIds = await db.CourseItems
            .Where(i => lessonIds.Contains(i.LessonId)).Select(i => i.Id).ToListAsync();

        await db.CourseProgresses.Where(p => itemIds.Contains(p.ItemId)).ExecuteDeleteAsync();
        await db.CourseQuestions.Where(q => itemIds.Contains(q.ItemId)).ExecuteDeleteAsync();
        await db.CourseItems.Where(i => itemIds.Contains(i.Id)).ExecuteDeleteAsync();
        await db.CourseLessons.Where(s => lessonIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.CourseTopics.Where(t => t.ModuleId == id).ExecuteDeleteAsync();
        db.CourseModules.Remove(module);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Mavzu ----

    [HttpPost("modules/{moduleId}/topics")]
    public async Task<ActionResult> CreateTopic(string moduleId, TopicInput input)
    {
        var module = await db.CourseModules.FindAsync(moduleId);
        if (module == null) return NotFound();
        var maxOrder = await db.CourseTopics
            .Where(t => t.ModuleId == moduleId)
            .Select(t => (int?)t.Order).MaxAsync() ?? -1;
        var topic = new CourseTopic
        {
            CurriculumId = module.CurriculumId,
            ModuleId = moduleId,
            Title = input.Title,
            Note = input.Note ?? "",
            Order = maxOrder + 1,
        };
        db.CourseTopics.Add(topic);
        await db.SaveChangesAsync();
        return Ok(new { id = topic.Id });
    }

    [HttpPut("topics/{id}")]
    public async Task<ActionResult> UpdateTopic(string id, TopicInput input)
    {
        var topic = await db.CourseTopics.FindAsync(id);
        if (topic == null) return NotFound();
        topic.Title = input.Title;
        topic.Note = input.Note ?? "";
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("topics/{id}")]
    public async Task<ActionResult> DeleteTopic(string id)
    {
        var topic = await db.CourseTopics.FindAsync(id);
        if (topic == null) return NotFound();

        var lessonIds = await db.CourseLessons
            .Where(s => s.TopicId == id).Select(s => s.Id).ToListAsync();
        var itemIds = await db.CourseItems
            .Where(i => lessonIds.Contains(i.LessonId)).Select(i => i.Id).ToListAsync();
        await db.CourseProgresses.Where(p => itemIds.Contains(p.ItemId)).ExecuteDeleteAsync();
        await db.CourseQuestions.Where(q => itemIds.Contains(q.ItemId)).ExecuteDeleteAsync();
        await db.CourseItems.Where(i => itemIds.Contains(i.Id)).ExecuteDeleteAsync();
        await db.CourseLessons.Where(s => s.TopicId == id).ExecuteDeleteAsync();
        db.CourseTopics.Remove(topic);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Dars ----

    [HttpPost("topics/{topicId}/lessons")]
    public async Task<ActionResult> CreateLesson(string topicId, LessonInput input)
    {
        var topic = await db.CourseTopics.FindAsync(topicId);
        if (topic == null) return NotFound();
        var maxOrder = await db.CourseLessons
            .Where(s => s.TopicId == topicId)
            .Select(s => (int?)s.Order).MaxAsync() ?? -1;
        var lesson = new CourseLesson
        {
            CurriculumId = topic.CurriculumId,
            TopicId = topicId,
            Title = input.Title,
            Note = input.Note ?? "",
            Order = maxOrder + 1,
        };
        db.CourseLessons.Add(lesson);
        await db.SaveChangesAsync();
        return Ok(new { id = lesson.Id });
    }

    /// <summary>Dars nomi/izohini yangilaydi.</summary>
    [HttpPut("lessons/{id}")]
    public async Task<ActionResult> UpdateLesson(string id, LessonInput input)
    {
        var lesson = await db.CourseLessons.FindAsync(id);
        if (lesson == null) return NotFound();
        lesson.Title = input.Title;
        lesson.Note = input.Note ?? "";
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("lessons/{id}")]
    public async Task<ActionResult> DeleteLesson(string id)
    {
        var lesson = await db.CourseLessons.FindAsync(id);
        if (lesson == null) return NotFound();
        var itemIds = await db.CourseItems
            .Where(i => i.LessonId == id).Select(i => i.Id).ToListAsync();
        await db.CourseProgresses.Where(p => itemIds.Contains(p.ItemId)).ExecuteDeleteAsync();
        await db.CourseQuestions.Where(q => itemIds.Contains(q.ItemId)).ExecuteDeleteAsync();
        await db.CourseItems.Where(i => itemIds.Contains(i.Id)).ExecuteDeleteAsync();
        db.CourseLessons.Remove(lesson);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Topshiriq (o'z turini tanlaydi: text|video|audio|vocab|test|pdf, keyin ham o'zgartirilishi mumkin) ----

    [HttpPost("lessons/{lessonId}/items")]
    public async Task<ActionResult> CreateItem(string lessonId, ItemInput input)
    {
        var lesson = await db.CourseLessons.FindAsync(lessonId);
        if (lesson == null) return NotFound();
        var maxOrder = await db.CourseItems
            .Where(i => i.LessonId == lessonId)
            .Select(i => (int?)i.Order).MaxAsync() ?? -1;
        var item = new CourseItem
        {
            CurriculumId = lesson.CurriculumId,
            LessonId = lessonId,
            Text = input.Text,
            Note = input.Note ?? "",
            Order = maxOrder + 1,
            Type = NormalizeType(input.Type),
            // Mashq turi topshiriq YARATISH ekranida tanlanadi — konstruktor uni darhol ochadi.
            ExerciseKind = (input.ExerciseKind ?? "").Trim(),
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.CourseItems.Add(item);
        await db.SaveChangesAsync();
        return Ok(new { id = item.Id });
    }

    /// <summary>Bir nechta TOPSHIRIQNI BIR ZUMDA yaratadi — barchasi BITTA turda (masalan 10ta video
    /// topshiriq nomi kiritilsa, 10tasi ham "video" turida yaratiladi). Dars ichida bir xil turdagi
    /// ko'p topshiriqni tez kiritish uchun (har biri uchun alohida "+ Topshiriq" bosish shart emas).</summary>
    [HttpPost("lessons/{lessonId}/items/bulk")]
    public async Task<ActionResult<List<CurriculumItemDto>>> CreateItemsBulk(string lessonId, BulkItemInput input)
    {
        var lesson = await db.CourseLessons.FindAsync(lessonId);
        if (lesson == null) return NotFound();

        var texts = (input.Texts ?? new()).Select(t => (t ?? "").Trim()).Where(t => t.Length > 0).ToList();
        if (texts.Count == 0) return BadRequest(new { message = "Kamida bitta topshiriq nomi kerak" });

        var type = NormalizeType(input.Type);
        var maxOrder = await db.CourseItems
            .Where(i => i.LessonId == lessonId)
            .Select(i => (int?)i.Order).MaxAsync() ?? -1;
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        var created = new List<CourseItem>();
        foreach (var text in texts)
        {
            var item = new CourseItem
            {
                CurriculumId = lesson.CurriculumId,
                LessonId = lessonId,
                Text = text,
                Note = "",
                Order = ++maxOrder,
                Type = type,
                CreatedAt = now,
            };
            db.CourseItems.Add(item);
            created.Add(item);
        }
        await db.SaveChangesAsync();

        return created.Select(i => ToItemDto(i, new Dictionary<string, int>())).ToList();
    }

    /// <summary>Topshiriq nomi/izohini yangilaydi. <see cref="ItemInput.Type"/> berilsa — turi ham
    /// shu yerda o'zgartiriladi (masalan Excel'dan "matn" bo'lib kirgan topshiriqni "video"ga
    /// almashtirish uchun).</summary>
    [HttpPut("items/{id}")]
    public async Task<ActionResult> UpdateItem(string id, ItemInput input)
    {
        var item = await db.CourseItems.FindAsync(id);
        if (item == null) return NotFound();
        item.Text = input.Text;
        item.Note = input.Note ?? "";
        if (input.Type != null) item.Type = NormalizeType(input.Type);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("items/{id}")]
    public async Task<ActionResult> DeleteItem(string id)
    {
        var item = await db.CourseItems.FindAsync(id);
        if (item == null) return NotFound();
        await db.CourseProgresses.Where(p => p.ItemId == id).ExecuteDeleteAsync();
        await db.CourseQuestions.Where(q => q.ItemId == id).ExecuteDeleteAsync();
        db.CourseItems.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Topshiriq KONTENTI (video/matn/audio/lug'at/test) ----

    /// <summary>Bitta topshiriqning to'liq kontenti — tahrirlovchi va ko'rish ekranlari uchun.</summary>
    [HttpGet("item/{id}")]
    public async Task<ActionResult<CourseItemDetailDto>> GetItem(string id)
    {
        var i = await db.CourseItems.FindAsync(id);
        if (i == null) return NotFound();
        var qs = await db.CourseQuestions.Where(q => q.ItemId == id).OrderBy(q => q.Order)
            .Select(q => new CourseQuestionDto(q.Id, q.Text, q.Options, q.CorrectIndex)).ToListAsync();
        return new CourseItemDetailDto(
            i.Id, i.LessonId, i.Text, i.Note, i.Order, i.Type,
            i.VideoUrl, i.AudioUrl, i.TextContent, i.PdfUrl, i.PdfName,
            i.Meta, ParseVocab(i.VocabJson), qs, i.ExerciseKind, i.ExerciseJson);
    }

    /// <summary>Topshiriq kontentini saqlash: nom + (video/matn/audio/lug'at) + test savollari
    /// (almashtiriladi). Type BU YERDA O'ZGARTIRILMAYDI — buning uchun <c>PUT items/{id}</c> bor.</summary>
    [HttpPut("items/{id}/content")]
    public async Task<ActionResult> SaveItemContent(string id, SaveItemContentRequest req)
    {
        var item = await db.CourseItems.FindAsync(id);
        if (item == null) return NotFound();

        item.Text = (req.Text ?? "").Trim();
        item.VideoUrl = (req.VideoUrl ?? "").Trim();
        item.AudioUrl = (req.AudioUrl ?? "").Trim();
        item.TextContent = req.TextContent ?? "";
        item.PdfUrl = (req.PdfUrl ?? "").Trim();
        // PDF olib tashlansa nomi ham tozalanadi.
        item.PdfName = item.PdfUrl.Length > 0 ? (req.PdfName ?? "").Trim() : "";

        var vocab = (req.Vocab ?? new()).Where(v => !string.IsNullOrWhiteSpace(v.Term)).ToList();
        item.VocabJson = vocab.Count > 0 ? JsonSerializer.Serialize(vocab) : "";

        // Test savollari — to'liq almashtiriladi.
        await db.CourseQuestions.Where(q => q.ItemId == id).ExecuteDeleteAsync();
        var questions = (req.Questions ?? new()).Where(q => !string.IsNullOrWhiteSpace(q.Text)).ToList();
        var order = 0;
        foreach (var q in questions)
            db.CourseQuestions.Add(new CourseQuestion
            {
                ItemId = id,
                Text = q.Text.Trim(),
                Options = (q.Options ?? new()).Select(o => o ?? "").ToList(),
                CorrectIndex = q.CorrectIndex,
                Order = order++,
            });

        // Interaktiv mashq (topshiriq konstruktori): tur + JSON mazmun. null berilsa — tegilmaydi
        // (boshqa turdagi topshiriqni saqlash mashq mazmunini o'chirib yubormasin).
        if (req.ExerciseKind is not null) item.ExerciseKind = req.ExerciseKind.Trim();
        if (req.ExerciseJson is not null) item.ExerciseJson = req.ExerciseJson;

        // Meta — foydalanuvchi erkin yorlig'i (masalan "12 daq"); bo'sh bo'lsa daraxtda
        // bo'limlar ro'yxati avtomatik ko'rsatiladi (MetaFor).
        item.Meta = (req.Meta ?? "").Trim();

        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Excel import (shablon + fayl) ----

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Shablon ustunlari (1-varaq). Tartibi Excel importi o'qishi bilan AYNAN bir xil bo'lishi shart.
    private static readonly string[] ExcelImportHeaders = { "Modul*", "Mavzu*", "Dars nomi*", "Izoh" };

    /// <summary>
    /// O'quv dasturini ommaviy kiritish uchun Excel shabloni (.xlsx). 1-varaq "Dastur" —
    /// to'ldiriladigan sarlavhalar; 2-varaq "Yo'riqnoma" — maydonlar izohi va namuna.
    /// Import faqat 1-varaqni o'qiydi.
    /// </summary>
    [HttpGet("import-template")]
    public IActionResult ImportTemplate()
    {
        var info = new List<IReadOnlyList<string>>
        {
            new[] { "Modul*", "Dasturning katta bosqichi. Masalan: Beginner, A1, 1-modul" },
            new[] { "Mavzu*", "Modul ichidagi mavzu. Masalan: Present Simple" },
            new[] { "Dars nomi*", "Mavzu ichidagi dars. Masalan: 1-dars. Tanishuv" },
            new[] { "Izoh", "ixtiyoriy — dars izohi" },
            new[] { "", "" },
            new[] { "QOIDA:", "Modul va Mavzu ustunlari bo'sh qoldirilsa — YUQORIDAGI qator qiymati olinadi." },
            new[] { "", "Ya'ni modul/mavzu nomini faqat birinchi darsida yozish kifoya." },
            new[] { "", "" },
            new[] { "DIQQAT:", "Excel faqat Modul→Mavzu→Dars skeletini yaratadi. Har darsning ICHIGA" },
            new[] { "", "topshiriqlar (video/matn/audio/pdf/lug'at/test) import'dan KEYIN, dastur" },
            new[] { "", "sahifasida qo'lda (bir nechtasini birdan) qo'shiladi." },
            new[] { "", "" },
            new[] { "NAMUNA:", "" },
            new[] { "Modul", "Mavzu | Dars nomi" },
            new[] { "Beginner", "Alifbo | 1-dars. Harflar" },
            new[] { "(bo'sh)", "(bo'sh) | 2-dars. Talaffuz" },
            new[] { "(bo'sh)", "Salomlashish | 3-dars. Greetings" },
            new[] { "Elementary", "Present Simple | 4-dars. Fe'llar" },
        };

        var bytes = ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec("Dastur", ExcelImportHeaders, Array.Empty<IReadOnlyList<string>>()),
            new ExcelExport.SheetSpec("Yo'riqnoma", new[] { "Maydon", "Izoh" }, info),
        });
        return File(bytes, XlsxMime, "oquv_dasturi_shablon.xlsx");
    }

    /// <summary>
    /// To'ldirilgan Excel (.xlsx) shablonidan o'quv dasturi SKELETINI (Modul→Mavzu→Dars) yuklaydi.
    /// Har qator = bitta dars; Modul/Mavzu bo'sh bo'lsa yuqoridagi qator qiymati olinadi
    /// (carry-forward). TOPSHIRIQLAR Excel orqali yaratilmaydi — ular import'dan keyin har darsning
    /// ichida qo'lda (bir nechtasini birdan, tezkor panel bilan) qo'shiladi.
    /// <paramref name="replace"/>=true bo'lsa mavjud dastur (progress bilan) O'CHIRILIB, o'rniga yoziladi;
    /// aks holda mavjud modul/mavzularga nomi bo'yicha QO'SHILADI (bir xil nom — takrorlanmaydi).
    /// </summary>
    [HttpPost("{curriculumId}/import-excel")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<CurriculumExcelImportResultDto>> ImportExcel(
        string curriculumId, IFormFile? file, [FromQuery] bool replace = false)
    {
        var curriculum = await db.Curricula.FindAsync(curriculumId);
        if (curriculum == null) return NotFound(new { message = "Dastur topilmadi" });

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmagan" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat .xlsx (Excel) fayl qabul qilinadi" });

        List<string[]> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            rows = ExcelImport.ReadRows(stream, ExcelImportHeaders.Length);
        }
        catch
        {
            return BadRequest(new { message = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring" });
        }

        if (replace)
        {
            // Eski dastur (progress va test savollari bilan) tozalanadi.
            var oldItemIds = await db.CourseItems
                .Where(i => i.CurriculumId == curriculumId).Select(i => i.Id).ToListAsync();
            await db.CourseProgresses.Where(p => oldItemIds.Contains(p.ItemId)).ExecuteDeleteAsync();
            await db.CourseQuestions.Where(q => oldItemIds.Contains(q.ItemId)).ExecuteDeleteAsync();
            await db.CourseItems.Where(i => i.CurriculumId == curriculumId).ExecuteDeleteAsync();
            await db.CourseLessons.Where(s => s.CurriculumId == curriculumId).ExecuteDeleteAsync();
            await db.CourseTopics.Where(t => t.CurriculumId == curriculumId).ExecuteDeleteAsync();
            await db.CourseModules.Where(m => m.CurriculumId == curriculumId).ExecuteDeleteAsync();
        }

        // Mavjud modul/mavzular (append rejimida nomi bo'yicha qayta ishlatiladi).
        var existingModules = replace
            ? new List<CourseModule>()
            : await db.CourseModules.Where(m => m.CurriculumId == curriculumId).ToListAsync();
        var existingTopics = replace
            ? new List<CourseTopic>()
            : await db.CourseTopics.Where(t => t.CurriculumId == curriculumId).ToListAsync();

        var moduleByName = existingModules
            .GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // Mavzu kaliti: moduleId + "|" + nom (kichik harfda)
        var topicByKey = existingTopics
            .GroupBy(t => $"{t.ModuleId}|{t.Title.Trim().ToLowerInvariant()}")
            .ToDictionary(g => g.Key, g => g.First());

        var nextModuleOrder = existingModules.Count > 0 ? existingModules.Max(m => m.Order) + 1 : 0;
        var nextTopicOrder = new Dictionary<string, int>(); // moduleId -> keyingi order
        var nextLessonOrder = new Dictionary<string, int>(); // topicId -> keyingi order
        foreach (var g in existingTopics.GroupBy(t => t.ModuleId))
            nextTopicOrder[g.Key] = g.Max(t => t.Order) + 1;
        if (!replace)
        {
            var existingLessonOrders = await db.CourseLessons
                .Where(s => s.CurriculumId == curriculumId)
                .GroupBy(s => s.TopicId)
                .Select(g => new { TopicId = g.Key, Max = g.Max(s => s.Order) })
                .ToListAsync();
            foreach (var e in existingLessonOrders) nextLessonOrder[e.TopicId] = e.Max + 1;
        }

        var errors = new List<StudentImportRowErrorDto>();
        int newModules = 0, newTopics = 0, newLessons = 0, skipped = 0;

        CourseModule? currentModule = null;
        CourseTopic? currentTopic = null;

        // 0-qator — sarlavha; ma'lumot 1-indeksdan (Excel'dagi 2-qator) boshlanadi.
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var excelRow = i + 1;
            if (r.All(string.IsNullOrWhiteSpace)) { skipped++; continue; }

            var moduleName = r[0].Trim();
            var topicTitle = r[1].Trim();
            var lessonTitle = r[2].Trim();
            var note = r[3].Trim();

            // Modul: nom berilgan bo'lsa topamiz/yaratamiz, bo'sh bo'lsa oldingi qator moduli.
            if (moduleName.Length > 0)
            {
                if (!moduleByName.TryGetValue(moduleName, out var md))
                {
                    md = new CourseModule
                    {
                        CurriculumId = curriculumId,
                        Name = moduleName,
                        Note = "",
                        Order = nextModuleOrder++,
                    };
                    db.CourseModules.Add(md);
                    moduleByName[moduleName] = md;
                    newModules++;
                }
                if (!ReferenceEquals(currentModule, md)) currentTopic = null; // modul almashdi
                currentModule = md;
            }
            if (currentModule == null)
            {
                errors.Add(new StudentImportRowErrorDto(excelRow, "Modul ko'rsatilmagan"));
                continue;
            }

            // Mavzu: xuddi shunday carry-forward.
            if (topicTitle.Length > 0)
            {
                var key = $"{currentModule.Id}|{topicTitle.ToLowerInvariant()}";
                if (!topicByKey.TryGetValue(key, out var tp))
                {
                    var order = nextTopicOrder.GetValueOrDefault(currentModule.Id, 0);
                    nextTopicOrder[currentModule.Id] = order + 1;
                    tp = new CourseTopic
                    {
                        CurriculumId = curriculumId,
                        ModuleId = currentModule.Id,
                        Title = topicTitle,
                        Note = "",
                        Order = order,
                    };
                    db.CourseTopics.Add(tp);
                    topicByKey[key] = tp;
                    newTopics++;
                }
                currentTopic = tp;
            }

            // Dars nomi bo'sh qator — faqat modul/mavzu e'lon qilingan bo'lishi mumkin.
            if (lessonTitle.Length == 0) { skipped++; continue; }

            if (currentTopic == null)
            {
                errors.Add(new StudentImportRowErrorDto(excelRow, "Mavzu ko'rsatilmagan"));
                continue;
            }

            var lessonOrder = nextLessonOrder.GetValueOrDefault(currentTopic.Id, 0);
            nextLessonOrder[currentTopic.Id] = lessonOrder + 1;
            db.CourseLessons.Add(new CourseLesson
            {
                CurriculumId = curriculumId,
                TopicId = currentTopic.Id,
                Title = lessonTitle,
                Note = note,
                Order = lessonOrder,
            });
            newLessons++;
        }

        if (newModules + newTopics + newLessons > 0 || replace)
            await db.SaveChangesAsync();

        return new CurriculumExcelImportResultDto(newModules, newTopics, newLessons, skipped, errors);
    }

    // ---- Modulni boshqa dasturga nusxalash ----

    /// <summary>
    /// Modul bilan barcha mavzu/dars/topshiriqlari (kontentsiz) boshqa O'QUV DASTURIGA nusxalanadi —
    /// mavjud dasturni o'zgartirmasdan uni "shablon" sifatida ishlatish uchun (bitta dasturni bir
    /// nechta kursga biriktirish uchun NUSXALASH SHART EMAS — shuning uchun bor, faqat
    /// SubjectsController orqali biriktiring). ID'lar yangi yaratiladi, kontent bo'sh qoladi.
    /// </summary>
    [HttpPost("modules/{moduleId}/copy-to/{targetCurriculumId}")]
    public async Task<ActionResult> CopyModuleToCurriculum(string moduleId, string targetCurriculumId)
    {
        var sourceModule = await db.CourseModules.FindAsync(moduleId);
        if (sourceModule == null) return NotFound("Modul topilmadi");

        var targetCurriculum = await db.Curricula.FindAsync(targetCurriculumId);
        if (targetCurriculum == null) return NotFound("Maqsadli dastur topilmadi");

        var source = await LoadModuleSourceAsync(moduleId);
        var r = await CopyModuleIntoCurriculumAsync(sourceModule, source, targetCurriculum);

        return Ok(new
        {
            moduleId = r.ModuleId,
            moduleName = sourceModule.Name,
            merged = r.Merged,
            topicCount = r.AddedTopics,
            lessonCount = r.AddedLessons,
            itemCount = r.AddedItems,
        });
    }

    /// <summary>
    /// BIR NECHTA modulni BIR NECHTA o'quv dasturiga birdan nusxalash (har modul × har dastur).
    /// Agar maqsad dasturda shu NOMLI modul allaqachon bo'lsa — BIRLASHTIRILADI (nom bo'yicha
    /// mavzu/dars, matn bo'yicha topshiriq moslashadi; bir xillari takrorlanmaydi, faqat yangilari
    /// qo'shiladi). Har bir (modul, dastur) juftligi uchun alohida natija qaytadi.
    /// </summary>
    [HttpPost("modules/copy-many")]
    public async Task<ActionResult> CopyModulesToCurricula([FromBody] CopyModulesRequest req)
    {
        var moduleIds = (req?.ModuleIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var targetIds = (req?.TargetCurriculumIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (moduleIds.Count == 0) return BadRequest("Kamida bitta modul tanlang");
        if (targetIds.Count == 0) return BadRequest("Kamida bitta maqsadli dastur tanlang");

        var sourceModules = await db.CourseModules.Where(m => moduleIds.Contains(m.Id)).ToListAsync();
        if (sourceModules.Count == 0) return NotFound("Modul topilmadi");

        var targetById = (await db.Curricula.Where(c => targetIds.Contains(c.Id)).ToListAsync())
            .ToDictionary(c => c.Id);

        var results = new List<CopyModuleResultDto>();
        foreach (var sourceModule in sourceModules)
        {
            var source = await LoadModuleSourceAsync(sourceModule.Id);
            foreach (var targetId in targetIds)
            {
                if (!targetById.TryGetValue(targetId, out var tc))
                {
                    results.Add(new CopyModuleResultDto(
                        sourceModule.Id, sourceModule.Name, targetId, null, false, false, "Dastur topilmadi", 0, 0, 0));
                    continue;
                }
                // Modulni o'z dasturiga nusxalab bo'lmaydi — jimgina o'tkazamiz.
                if (tc.Id == sourceModule.CurriculumId) continue;

                var r = await CopyModuleIntoCurriculumAsync(sourceModule, source, tc);
                results.Add(new CopyModuleResultDto(
                    sourceModule.Id, sourceModule.Name, tc.Id, tc.Name, true, r.Merged, null,
                    r.AddedTopics, r.AddedLessons, r.AddedItems));
            }
        }

        return Ok(new
        {
            okCount = results.Count(x => x.Ok),
            failCount = results.Count(x => !x.Ok),
            mergedCount = results.Count(x => x.Ok && x.Merged),
            results,
        });
    }

    // Modul ostidagi barcha mavzu/dars/topshiriqlarni (tartiblangan holda) yuklaydi.
    private async Task<ModuleSource> LoadModuleSourceAsync(string moduleId)
    {
        var topics = await db.CourseTopics
            .Where(t => t.ModuleId == moduleId).OrderBy(t => t.Order).ToListAsync();
        var topicIds = topics.Select(t => t.Id).ToList();
        var lessons = await db.CourseLessons
            .Where(s => topicIds.Contains(s.TopicId)).OrderBy(s => s.Order).ToListAsync();
        var lessonIds = lessons.Select(s => s.Id).ToList();
        var items = await db.CourseItems
            .Where(i => lessonIds.Contains(i.LessonId)).OrderBy(i => i.Order).ToListAsync();
        return new ModuleSource(topics, lessons, items);
    }

    // Bitta modulni (oldindan yuklangan manba bilan) bitta maqsadli dasturga nusxalaydi.
    // Agar shu nomli modul allaqachon bo'lsa — YANGI yaratmasdan mavjudiga BIRLASHTIRADI:
    //   modul: Name bo'yicha, mavzu: Title bo'yicha, dars: Title bo'yicha, topshiriq: Text bo'yicha
    //   moslashadi — bir xillari takrorlanmaydi, faqat yangilari qo'shiladi.
    // Kontent maydonlari (video/audio/matn/lug'at/test) BO'SH qoladi — faqat tuzilma nusxalanadi.
    private async Task<CopyModuleOutcome> CopyModuleIntoCurriculumAsync(
        CourseModule sourceModule, ModuleSource source, Curriculum targetCurriculum)
    {
        // Maqsad dasturda shu nomli modul bormi? Bo'lsa — birlashtiramiz, bo'lmasa — yangi yaratamiz.
        var module = await db.CourseModules
            .FirstOrDefaultAsync(m => m.CurriculumId == targetCurriculum.Id && m.Name == sourceModule.Name);
        var merged = module != null;

        if (module == null)
        {
            var maxOrder = await db.CourseModules
                .Where(m => m.CurriculumId == targetCurriculum.Id)
                .Select(m => (int?)m.Order).MaxAsync() ?? -1;
            module = new CourseModule
            {
                CurriculumId = targetCurriculum.Id,
                Name = sourceModule.Name,
                Note = sourceModule.Note,
                Order = maxOrder + 1,
            };
            db.CourseModules.Add(module);
            await db.SaveChangesAsync();
        }

        // ---- Mavzular: Title bo'yicha moslash ----
        var existingTopics = await db.CourseTopics.Where(t => t.ModuleId == module.Id).ToListAsync();
        var topicByTitle = new Dictionary<string, CourseTopic>();
        foreach (var t in existingTopics) topicByTitle.TryAdd(t.Title ?? "", t);
        var topicMaxOrder = existingTopics.Count == 0 ? -1 : existingTopics.Max(t => t.Order);

        var topicMapping = new Dictionary<string, string>();
        var addedTopics = 0;
        foreach (var st in source.Topics)
        {
            if (topicByTitle.TryGetValue(st.Title ?? "", out var et))
            {
                topicMapping[st.Id] = et.Id;
            }
            else
            {
                var nt = new CourseTopic
                {
                    CurriculumId = targetCurriculum.Id,
                    ModuleId = module.Id,
                    Title = st.Title,
                    Note = st.Note,
                    Order = ++topicMaxOrder,
                };
                db.CourseTopics.Add(nt);
                topicByTitle[st.Title ?? ""] = nt;
                topicMapping[st.Id] = nt.Id;
                addedTopics++;
            }
        }
        await db.SaveChangesAsync();

        // ---- Darslar: (TopicId + Title) bo'yicha moslash ----
        var targetTopicIds = topicMapping.Values.Distinct().ToList();
        var existingLessons = await db.CourseLessons.Where(s => targetTopicIds.Contains(s.TopicId)).ToListAsync();
        var lessonByKey = new Dictionary<(string, string), CourseLesson>();
        var lessonMaxOrder = new Dictionary<string, int>();
        foreach (var s in existingLessons)
        {
            lessonByKey.TryAdd((s.TopicId, s.Title ?? ""), s);
            lessonMaxOrder[s.TopicId] = Math.Max(lessonMaxOrder.GetValueOrDefault(s.TopicId, -1), s.Order);
        }

        var lessonMapping = new Dictionary<string, string>();
        var addedLessons = 0;
        foreach (var sl in source.Lessons)
        {
            if (!topicMapping.TryGetValue(sl.TopicId, out var targetTopicId)) continue;
            var key = (targetTopicId, sl.Title ?? "");
            if (lessonByKey.TryGetValue(key, out var el))
            {
                lessonMapping[sl.Id] = el.Id;
            }
            else
            {
                var nextOrder = lessonMaxOrder.GetValueOrDefault(targetTopicId, -1) + 1;
                lessonMaxOrder[targetTopicId] = nextOrder;
                var nl = new CourseLesson
                {
                    CurriculumId = targetCurriculum.Id,
                    TopicId = targetTopicId,
                    Title = sl.Title,
                    Note = sl.Note,
                    Order = nextOrder,
                };
                db.CourseLessons.Add(nl);
                lessonByKey[key] = nl;
                lessonMapping[sl.Id] = nl.Id;
                addedLessons++;
            }
        }
        await db.SaveChangesAsync();

        // ---- Topshiriqlar: (LessonId + Text) bo'yicha moslash — bir xil matnli takrorlanmaydi ----
        var targetLessonIds = lessonMapping.Values.Distinct().ToList();
        var existingItems = await db.CourseItems.Where(i => targetLessonIds.Contains(i.LessonId)).ToListAsync();
        var itemKeys = new HashSet<(string, string)>();
        var itemMaxOrder = new Dictionary<string, int>();
        foreach (var it in existingItems)
        {
            itemKeys.Add((it.LessonId, it.Text ?? ""));
            itemMaxOrder[it.LessonId] = Math.Max(itemMaxOrder.GetValueOrDefault(it.LessonId, -1), it.Order);
        }

        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var addedItems = 0;
        foreach (var si in source.Items)
        {
            if (!lessonMapping.TryGetValue(si.LessonId, out var targetLessonId)) continue;
            var key = (targetLessonId, si.Text ?? "");
            if (itemKeys.Contains(key)) continue; // shu darsda shu matnli topshiriq bor — o'tkazamiz
            itemKeys.Add(key);
            var nextOrder = itemMaxOrder.GetValueOrDefault(targetLessonId, -1) + 1;
            itemMaxOrder[targetLessonId] = nextOrder;
            db.CourseItems.Add(new CourseItem
            {
                CurriculumId = targetCurriculum.Id,
                LessonId = targetLessonId,
                Text = si.Text,
                Note = si.Note,
                Type = si.Type,
                Order = nextOrder,
                CreatedAt = now,
                // Kontent maydonlari BO'SH qoladi (video, audio, matn, lug'at, test)
            });
            addedItems++;
        }
        await db.SaveChangesAsync();

        return new CopyModuleOutcome(merged, addedTopics, addedLessons, addedItems, module.Id);
    }

    // ---- Progress (kurs/Subject bo'yicha — kirish nazorati uchun; kontent M2M orqali resolve qilinadi) ----

    [HttpGet("subject/{subjectId}/progress/{studentId}")]
    public async Task<ActionResult<string[]>> GetProgress(string subjectId, string studentId)
    {
        var curriculumIds = await db.SubjectCurricula
            .Where(sc => sc.SubjectId == subjectId).Select(sc => sc.CurriculumId).ToListAsync();
        var ids = await db.CourseProgresses
            .Where(p => p.StudentId == studentId && p.Done)
            .Join(db.CourseItems.Where(i => curriculumIds.Contains(i.CurriculumId)),
                p => p.ItemId, i => i.Id, (p, i) => p.ItemId)
            .ToListAsync();
        return ids.ToArray();
    }

    /// <summary>Bitta kursga (Subject) biriktirilgan BARCHA dasturlarni BITTA birlashtirilgan
    /// daraxtga jamlab qaytaradi (StudentDetailPage'dagi "O'quv dasturi" ko'rinishi uchun).</summary>
    [HttpGet("subject/{subjectId}/tree")]
    public async Task<ActionResult<CurriculumDto>> GetSubjectTree(string subjectId)
    {
        var subject = await db.Subjects.FindAsync(subjectId);
        var links = await db.SubjectCurricula
            .Where(sc => sc.SubjectId == subjectId).OrderBy(sc => sc.Order).ToListAsync();
        var curriculumIds = links.Select(l => l.CurriculumId).ToList();
        var attachOrder = links.Select((l, idx) => (l.CurriculumId, idx)).ToDictionary(x => x.CurriculumId, x => x.idx);

        var modules = (await db.CourseModules.Where(m => curriculumIds.Contains(m.CurriculumId)).ToListAsync())
            .OrderBy(m => attachOrder.GetValueOrDefault(m.CurriculumId, 0)).ThenBy(m => m.Order).ToList();
        var topics = await db.CourseTopics.Where(t => curriculumIds.Contains(t.CurriculumId)).OrderBy(t => t.Order).ToListAsync();
        var lessons = await db.CourseLessons.Where(s => curriculumIds.Contains(s.CurriculumId)).OrderBy(s => s.Order).ToListAsync();
        var items = await db.CourseItems.Where(i => curriculumIds.Contains(i.CurriculumId)).OrderBy(i => i.Order).ToListAsync();

        var itemIds = items.Select(i => i.Id).ToList();
        var qCounts = (await db.CourseQuestions.Where(q => itemIds.Contains(q.ItemId))
                .Select(q => q.ItemId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var moduleDtos = BuildModuleDtos(modules, topics, lessons, items, qCounts);
        return new CurriculumDto(subjectId, subject?.Name ?? "", moduleDtos);
    }

    [HttpPost("progress")]
    public async Task<ActionResult> SetProgress(SetProgressRequest req)
    {
        var item = await db.CourseItems.FindAsync(req.ItemId);
        var existing = await db.CourseProgresses
            .FirstOrDefaultAsync(p => p.StudentId == req.StudentId && p.ItemId == req.ItemId);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (existing != null)
        {
            existing.Done = req.Done;
            existing.UpdatedAt = now;
        }
        else
        {
            db.CourseProgresses.Add(new CourseProgress
            {
                StudentId = req.StudentId,
                ItemId = req.ItemId,
                CurriculumId = item?.CurriculumId,
                Done = req.Done,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ---- Guruh sillabus o'tilishi + tugash prognozi ----

    [HttpGet("group/{groupId}")]
    public async Task<ActionResult<GroupCurriculumDto>> GetGroupCurriculum(string groupId)
    {
        var group = await db.Classes.FindAsync(groupId);
        if (group == null) return NotFound();
        return await CurriculumForecast.BuildGroupAsync(db, group);
    }

    [HttpPost("group/{groupId}/cover")]
    public async Task<ActionResult> CoverItem(string groupId, CoverRequest req)
    {
        if (req.Covered)
        {
            var exists = await db.GroupCurriculumLogs
                .AnyAsync(g => g.GroupId == groupId && g.ItemId == req.ItemId && !g.IsRevision);
            if (!exists)
            {
                db.GroupCurriculumLogs.Add(new GroupCurriculumLog
                {
                    GroupId = groupId,
                    ItemId = req.ItemId,
                    IsRevision = false,
                    Date = AppClock.Today.ToString("yyyy-MM-dd"),
                    CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                });
            }
        }
        else
        {
            await db.GroupCurriculumLogs
                .Where(g => g.GroupId == groupId && g.ItemId == req.ItemId && !g.IsRevision)
                .ExecuteDeleteAsync();
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("group/{groupId}/revision")]
    public async Task<ActionResult> Revision(string groupId, RevisionRequest req)
    {
        if (req.Delta > 0)
        {
            db.GroupCurriculumLogs.Add(new GroupCurriculumLog
            {
                GroupId = groupId,
                ItemId = "",
                IsRevision = true,
                Date = AppClock.Today.ToString("yyyy-MM-dd"),
                CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            });
        }
        else if (req.Delta < 0)
        {
            var last = await db.GroupCurriculumLogs
                .Where(g => g.GroupId == groupId && g.IsRevision)
                .OrderByDescending(g => g.CreatedAt)
                .FirstOrDefaultAsync();
            if (last != null) db.GroupCurriculumLogs.Remove(last);
        }
        await db.SaveChangesAsync();

        var revisionLessons = await db.GroupCurriculumLogs
            .CountAsync(g => g.GroupId == groupId && g.IsRevision);
        return Ok(new { ok = true, revisionLessons });
    }

    // ---- O'quvchining o'tilgan sillabus vaqt jadvali (per-student coverage timeline) ----

    [HttpGet("student/{studentId}/coverage-log")]
    public async Task<ActionResult<List<CoverageLogEntryDto>>> GetStudentCoverageLog(string studentId)
    {
        // O'quvchining FAOL guruh a'zoliklari.
        var groupIds = await db.StudentGroups
            .Where(sg => sg.StudentId == studentId && sg.IsActive)
            .Select(sg => sg.GroupId)
            .Distinct()
            .ToListAsync();
        if (groupIds.Count == 0) return new List<CoverageLogEntryDto>();

        var groups = await db.Classes
            .Where(g => groupIds.Contains(g.Id))
            .ToListAsync();

        var courseIds = groups.Select(g => g.CourseId).Where(c => c != "").Distinct().ToList();

        var subjects = await db.Subjects
            .Where(s => courseIds.Contains(s.Id))
            .ToListAsync();
        var courseNameById = subjects.ToDictionary(s => s.Id, s => s.Name);

        // Kurslarga biriktirilgan dasturlar (M2M) — shu dasturlarning band/dars/mavzu/modul
        // lug'atlari (per-log so'rovsiz).
        var curriculumIds = await db.SubjectCurricula
            .Where(sc => courseIds.Contains(sc.SubjectId))
            .Select(sc => sc.CurriculumId).Distinct().ToListAsync();

        var items = await db.CourseItems
            .Where(i => curriculumIds.Contains(i.CurriculumId))
            .ToListAsync();
        var lessons = await db.CourseLessons
            .Where(s => curriculumIds.Contains(s.CurriculumId))
            .ToListAsync();
        var topics = await db.CourseTopics
            .Where(t => curriculumIds.Contains(t.CurriculumId))
            .ToListAsync();
        var modules = await db.CourseModules
            .Where(m => curriculumIds.Contains(m.CurriculumId))
            .ToListAsync();

        var itemById = items.ToDictionary(i => i.Id);
        var lessonById = lessons.ToDictionary(s => s.Id);
        var topicById = topics.ToDictionary(t => t.Id);
        var moduleById = modules.ToDictionary(m => m.Id);

        var logs = await db.GroupCurriculumLogs
            .Where(g => groupIds.Contains(g.GroupId))
            .ToListAsync();

        var entries = new List<(string Date, string CreatedAt, CoverageLogEntryDto Dto)>();

        foreach (var log in logs)
        {
            var group = groups.FirstOrDefault(g => g.Id == log.GroupId);
            if (group == null) continue;
            var groupName = group.Name;
            var courseName = courseNameById.TryGetValue(group.CourseId, out var cn) ? cn : "";

            if (log.IsRevision)
            {
                entries.Add((log.Date, log.CreatedAt, new CoverageLogEntryDto(
                    log.Date, courseName, groupName, "", "", "Takrorlash darsi", true)));
            }
            else
            {
                if (!itemById.TryGetValue(log.ItemId, out var item)) continue; // band o'chirilgan
                var hasLesson = lessonById.TryGetValue(item.LessonId, out var lesson);
                var topicTitle = hasLesson && topicById.TryGetValue(lesson!.TopicId, out var topic) ? topic.Title : "";
                var moduleName = hasLesson && topicById.TryGetValue(lesson!.TopicId, out var tp)
                    && moduleById.TryGetValue(tp.ModuleId, out var module) ? module.Name : "";
                entries.Add((log.Date, log.CreatedAt, new CoverageLogEntryDto(
                    log.Date, courseName, groupName, moduleName, topicTitle, item.Text, false)));
            }
        }

        return entries
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.CreatedAt)
            .Select(e => e.Dto)
            .ToList();
    }

    // Bir nechta modulni bir nechta dasturga nusxalash — so'rov tanasi.
    public record CopyModulesRequest(List<string>? ModuleIds, List<string>? TargetCurriculumIds);

    // ---- O'quvchining topshiriq URINISHLARI (profildagi "Topshiriq natijalari") ----

    /// <summary>
    /// O'quvchi ishlagan topshiriqlar tarixi: har urinish alohida qator, sillabusdagi joyi
    /// (dastur → modul → mavzu → dars → topshiriq) va guruh nomi bilan boyitilgan.
    /// Javob tafsiloti bu yerda YUBORILMAYDI (ro'yxat yengil bo'lishi uchun) — u
    /// <c>attempt/{id}</c> orqali alohida olinadi.
    /// </summary>
    [HttpGet("student/{studentId}/attempts")]
    public async Task<ActionResult<StudentAttemptsDto>> GetStudentAttempts(string studentId, [FromQuery] int limit = 300)
    {
        var attempts = await db.CourseItemAttempts.AsNoTracking()
            .Where(a => a.StudentId == studentId)
            .OrderByDescending(a => a.FinishedAt)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync();

        if (attempts.Count == 0)
            return new StudentAttemptsDto(0, 0, 0, 0, 0, new List<StudentAttemptDto>());

        var rows = await EnrichAttemptsAsync(attempts);

        // Baholanadigan urinishlar — "view" (ko'rildi) bo'limi o'rtachaga kirmaydi.
        var graded = rows.Where(r => r.Total > 0).ToList();
        return new StudentAttemptsDto(
            ItemCount: rows.Select(r => r.ItemId).Distinct().Count(),
            AttemptCount: rows.Count,
            GradedCount: graded.Count,
            AvgScorePct: graded.Count > 0 ? (int)Math.Round(graded.Average(r => r.ScorePct)) : 0,
            TotalMinutes: (int)Math.Round(rows.Sum(r => r.DurationSec) / 60.0),
            Attempts: rows);
    }

    /// <summary>Bitta urinishning TO'LIQ tafsiloti — har savolga o'quvchi nima javob bergani.</summary>
    [HttpGet("attempt/{id}")]
    public async Task<ActionResult<StudentAttemptDetailDto>> GetAttempt(string id)
    {
        var a = await db.CourseItemAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        var rows = await EnrichAttemptsAsync(new List<CourseItemAttempt> { a });
        return new StudentAttemptDetailDto(rows[0], ParseAnswers(a.AnswersJson));
    }

    /// <summary>Urinish qatorlarini sillabus nomlari + guruh nomi bilan to'ldiradi (bitta so'rovda,
    /// per-qator so'rovsiz).</summary>
    private async Task<List<StudentAttemptDto>> EnrichAttemptsAsync(List<CourseItemAttempt> attempts)
    {
        var itemIds = attempts.Select(a => a.ItemId).Distinct().ToList();
        var groupIds = attempts.Select(a => a.GroupId).Where(g => g != "").Distinct().ToList();
        var curriculumIds = attempts.Select(a => a.CurriculumId).Distinct().ToList();

        var items = await db.CourseItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToListAsync();
        var lessonIds = items.Select(i => i.LessonId).Distinct().ToList();
        var lessons = await db.CourseLessons.AsNoTracking().Where(l => lessonIds.Contains(l.Id)).ToListAsync();
        var topicIds = lessons.Select(l => l.TopicId).Distinct().ToList();
        var topics = await db.CourseTopics.AsNoTracking().Where(t => topicIds.Contains(t.Id)).ToListAsync();
        var moduleIds = topics.Select(t => t.ModuleId).Distinct().ToList();
        var modules = await db.CourseModules.AsNoTracking().Where(m => moduleIds.Contains(m.Id)).ToListAsync();
        var curricula = await db.Curricula.AsNoTracking().Where(c => curriculumIds.Contains(c.Id)).ToListAsync();
        var groups = await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync();

        var itemById = items.ToDictionary(i => i.Id);
        var lessonById = lessons.ToDictionary(l => l.Id);
        var topicById = topics.ToDictionary(t => t.Id);
        var moduleById = modules.ToDictionary(m => m.Id);
        var curriculumName = curricula.ToDictionary(c => c.Id, c => c.Name);
        var groupName = groups.ToDictionary(g => g.Id, g => g.Name);

        return attempts.Select(a =>
        {
            itemById.TryGetValue(a.ItemId, out var item);
            CourseLesson? lesson = null;
            CourseTopic? topic = null;
            CourseModule? module = null;
            if (item is not null && lessonById.TryGetValue(item.LessonId, out var l))
            {
                lesson = l;
                if (topicById.TryGetValue(l.TopicId, out var t))
                {
                    topic = t;
                    moduleById.TryGetValue(t.ModuleId, out module);
                }
            }

            return new StudentAttemptDto(
                a.Id, a.ItemId,
                // Topshiriq o'chirilgan bo'lsa ham urinish tarixi qoladi — nomi o'rniga izoh.
                item?.Text ?? "(o'chirilgan topshiriq)",
                item?.Type ?? "",
                lesson?.Title ?? "", topic?.Title ?? "", module?.Name ?? "",
                curriculumName.GetValueOrDefault(a.CurriculumId, ""),
                groupName.GetValueOrDefault(a.GroupId, ""),
                a.Section, a.ExerciseKind, a.AttemptNo,
                a.Correct, a.Total, a.ScorePct, a.DurationSec, a.FinishedAt,
                ParseAnswers(a.AnswersJson).Count);
        }).ToList();
    }

    /// <summary>AnswersJson → javoblar ro'yxati (buzuq JSON bo'lsa bo'sh ro'yxat).</summary>
    private static List<AttemptAnswerDto> ParseAnswers(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<AttemptAnswerDto>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { return new(); }
    }

    // Bir (modul, dastur) juftligi uchun nusxalash natijasi (klientga qaytadi).
    // Merged=true bo'lsa — mavjud modulga birlashtirildi; Added* — QO'SHILGAN (yangi) elementlar soni.
    public record CopyModuleResultDto(
        string ModuleId, string ModuleName, string CurriculumId, string? CurriculumName,
        bool Ok, bool Merged, string? Error, int AddedTopics, int AddedLessons, int AddedItems);

    // Ichki: modul ostidagi oldindan yuklangan manba.
    private record ModuleSource(List<CourseTopic> Topics, List<CourseLesson> Lessons, List<CourseItem> Items);

    // Ichki: bitta nusxalash/birlashtirish amalining natijasi.
    private record CopyModuleOutcome(
        bool Merged, int AddedTopics, int AddedLessons, int AddedItems, string ModuleId);
}
