using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("students.list")]
[Route("api/admin/students")]
public class StudentsController(
    AppDbContext db, AuditService audit, IConfiguration config, AutoMessageService autoMsg,
    DataCache dataCache) : ControllerBase
{
    private const int MinPasswordLength = 8;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";

    /// <summary>Amalni bajargan xodim — SNAPSHOT (chegirma registri va audit uchun).</summary>
    private string Actor => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
    private string? ActorId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Faol (arxivlanmagan) o'quvchilar ro'yxati. <paramref name="includeArchived"/>=true bo'lsa
    /// arxivlangan o'quvchilar ham qaytadi.
    /// </summary>
    /// <summary>
    /// HUJJAT MANZILLARINI TOZALAYDI — <c>students</c> bo'limi ruxsati YO'Q xodim uchun.
    ///
    /// <para>Xodim (staff) uchun GET so'rovlari ataylab ochiq: moliya/qabul sahifasi o'quvchilar
    /// ro'yxatini o'qiy olishi kerak (<see cref="AdminPermAttribute"/> izohiga qarang). Lekin bu
    /// ro'yxat XOM <see cref="Student"/> obyektini qaytaradi — ichida o'quvchining SURATI
    /// (<c>BirthCertificateUrl</c> — nomi eski) va ota-onaning passport skani
    /// (<c>ParentPassportUrl</c>) manzillari bor.</para>
    ///
    /// <para>Bu manzillar <c>/uploads</c> ga ishora qiladi, u esa autentifikatsiyasiz beriladi —
    /// ya'ni bir marta olingan manzil <b>abadiy</b>, hatto xodim ishdan bo'shatilgandan keyin ham
    /// ishlayveradi. Shuning uchun o'quvchilar bo'limiga kira olmaydigan xodimga bu manzillar
    /// berilmaydi; ism/telefon/balans kabi bo'limlararo ishlash uchun kerak bo'lgan maydonlar
    /// avvalgidek qoladi. UI tomonda surat o'rniga bosh harflar ko'rinadi — ish buzilmaydi.</para>
    /// </summary>
    private void RedactDocs(IEnumerable<Student> students)
    {
        if (AdminPermAttribute.HasSectionAccess(User, "students.list")) return;
        foreach (var s in students)
        {
            s.BirthCertificateUrl = null;
            s.ParentPassportUrl = null;
        }
    }

    /// <param name="state">Ixtiyoriy HOLAT filtri (<see cref="StudentListView.MatchesState"/>):
    /// <c>trial</c> — "Yangi o'quvchilar" (tirik sinov a'zoligi bor), <c>active</c> — "Aktiv
    /// o'quvchilar". Bo'sh — hammasi (eski xatti-harakat). Noma'lum qiymat — 400.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Student>>> GetAll(
        [FromQuery] bool includeArchived = false, [FromQuery] string? state = null)
    {
        if (!StudentListView.IsKnownState(state))
            return BadRequest(new { message = $"Noma'lum holat: {state}" });
        // AsNoTracking: quyida obyektlar KO'RSATISH uchun o'zgartiriladi (guruh nomlari,
        // hujjat manzillarini tozalash) — ular tasodifan bazaga yozilib qolmasin.
        var q = db.Students.AsNoTracking().AsQueryable();
        if (!includeArchived) q = q.Where(s => !s.IsArchived);
        var students = await q.OrderBy(s => s.FullName).ToListAsync();
        RedactDocs(students);

        // A'ZOLIK KESIMI (guruhlar, holat, "aktiv") — YAGONA manbadan
        // (<see cref="StudentMembershipView.EnrichAsync"/>). Ilgari jamlama AYNAN shu yerda,
        // faqat RO'YXAT uchun hisoblanardi: bitta o'quvchi qaytaradigan yo'llar (profil, arxiv,
        // yangi yaratilgan) uni to'ldirmasdi va klientda "Aktiv emas" + eski (ClassName) guruh
        // ko'rinardi.
        await StudentMembershipView.EnrichAsync(db, students);
        // HOLAT FILTRI — jamlama to'ldirilgandan KEYIN (qoida bitta joyda: `StudentListView`).
        if (!string.IsNullOrEmpty(state))
            students = students.Where(s => StudentListView.MatchesState(s, state)).ToList();
        var ids = students.Select(s => s.Id).ToList();
        // Ro'yxat ustunlari: manba, moderator, to'lov sanasi, ilova, shartnoma.
        await StudentListView.EnrichExtrasAsync(db, students);

        // AMALDAGI CHEGIRMALAR SONI — bitta ommaviy so'rov (`MemberState` naqshi bilan bir xil).
        // Chegirma endi HAR FAN uchun alohida bo'lgani sababli ro'yxatda "nechta" ko'rsatiladi;
        // summa esa bu yerda hisoblanmaydi (u oylik hisobda).
        var discountCounts = (await db.StudentDiscounts.AsNoTracking()
                .Where(d => d.Status == StudentDiscount.StatusActive && ids.Contains(d.StudentId))
                .Select(d => d.StudentId)
                .ToListAsync())
            .GroupBy(x => x)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var s in students)
        {
            s.DiscountCount = discountCounts.GetValueOrDefault(s.Id, 0);
        }
        return students;
    }

    /// <summary>Qidiruv uchun YENGIL ichki proyeksiya — SELECT'ga faqat shu ustunlar tushadi
    /// (hujjat manzillari umuman o'qilmaydi, ya'ni <see cref="RedactDocs"/> ham kerak emas).</summary>
    private sealed record SearchRow(
        string Id, string FullName, string Phone, string ParentPhone,
        string FatherPhone, string MotherPhone, bool IsArchived);

    /// <summary>
    /// GLOBAL QIDIRUV (topbar / Ctrl+K): FISH (o'quvchi + ota-ona ismlari, so'z TARTIBI muhim
    /// emas) YOKI telefon (o'z/ota/ona/asosiy) bo'yicha. Filtrlash va <c>Take</c> SQL'da —
    /// ilgari frontend BUTUN ro'yxatni tortib brauzerda filtrlar edi.
    ///
    /// <para>Npgsql'da <c>EF.Functions.ILike</c> (pg_trgm GIN indekslariga mos), SQLite
    /// testlarida provayderga bog'liq bo'lmagan <see cref="StudentSearch.WhereNameFallback"/>
    /// tarmog'i (<c>ILike</c> SQLite'da ishlamaydi — <c>AuditController</c> dagi bilan bir xil
    /// sabab). Qoidalarning o'zi <see cref="StudentSearch"/> da (sof, testlangan).</para>
    ///
    /// <para>Ruxsat — sinf darajasidagi <c>[AdminPerm("students.list")]</c>
    /// (<see cref="GetAll"/> bilan bir xil).</para>
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<StudentSearchResultDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] int limit = StudentSearch.DefaultLimit,
        [FromQuery] bool includeArchived = true)
    {
        limit = StudentSearch.ClampLimit(limit);
        var term = (q ?? string.Empty).Trim();
        if (term.Length == 0) return new List<StudentSearchResultDto>();

        IQueryable<Student> Base()
        {
            var s = db.Students.AsNoTracking().AsQueryable();
            return includeArchived ? s : s.Where(x => !x.IsArchived);
        }

        static IQueryable<SearchRow> Project(IQueryable<Student> src) => src.Select(s =>
            new SearchRow(s.Id, s.FullName, s.Phone, s.ParentPhone,
                s.FatherPhone, s.MotherPhone, s.IsArchived));

        var words = StudentSearch.Words(term);
        var digits = StudentSearch.Digits(term);

        // ISM bo'yicha: har so'z topilishi SHART (AND), qaysi ism ustunida ekani muhim emas.
        var nameRows = new List<SearchRow>();
        if (words.Length > 0)
        {
            var nq = Base();
            if (db.Database.IsNpgsql())
            {
                foreach (var w in words)
                {
                    var p = StudentSearch.LikePattern(w);
                    nq = nq.Where(s =>
                        EF.Functions.ILike(s.FullName, p, "\\")
                        || EF.Functions.ILike(s.ParentFullName, p, "\\")
                        || EF.Functions.ILike(s.FatherFullName, p, "\\")
                        || EF.Functions.ILike(s.MotherFullName, p, "\\"));
                }
            }
            else
            {
                nq = StudentSearch.WhereNameFallback(nq, words);
            }
            // ⚠️ OrderBy/Take PROYEKSIYADAN OLDIN: `SearchRow` pozitsion record bo'lgani uchun
            // EF uni KONSTRUKTOR orqali quradi, konstruktor-proyeksiya a'zosiga esa keyingi
            // operatorda (`Project(...).OrderBy(r => r.FullName)`) murojaat qilib BO'LMAYDI —
            // butun so'rov "could not be translated" bilan yiqilib, qidiruv HAR DOIM 500 qaytarardi
            // (`StudentSearchTests.Endpoint_Oqimi...` shu holatni qulflaydi).
            nameRows = await Project(nq.OrderBy(s => s.FullName).Take(limit)).ToListAsync();
        }

        // TELEFON bo'yicha — ALOHIDA yengil so'rov (ism AND-zanjiri bilan OR qilish LINQ'da
        // qo'pol expression-tree talab qilardi; ikkita kichik SQL o'rniga bitta murakkabi shart emas).
        var phoneRows = new List<SearchRow>();
        if (digits.Length >= StudentSearch.MinPhoneDigits)
            phoneRows = await Project(StudentSearch.WherePhone(Base(), digits)
                .OrderBy(s => s.FullName).Take(limit)).ToListAsync();

        // Birlashtirish + tartib: ismi so'rovning BIRINCHI so'zi bilan BOSHLANGANLAR tepada
        // (odam odatda shuni qidiradi) — frontenddagi eski tartib bilan bir xil.
        var first = words.FirstOrDefault() ?? string.Empty;
        var merged = nameRows.Concat(phoneRows)
            .GroupBy(r => r.Id).Select(g => g.First())
            .OrderBy(r => first.Length > 0 && StudentSearch.Normalize(r.FullName).StartsWith(first) ? 0 : 1)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
        if (merged.Count == 0) return new List<StudentSearchResultDto>();

        // Guruh nomlari — TOPILGAN idlar bo'yicha alohida yengil so'rov (butun jadval emas).
        // MUZLATILGANLAR ham kiradi — dropdown "qayerda va qanday holatda"ni ko'rsatadi
        // (GetAll'ning GroupStates xatti-harakati bilan bir xil).
        var ids = merged.Select(r => r.Id).ToList();
        var memberships = await (from sg in db.StudentGroups
                                 join c in db.Classes on sg.GroupId equals c.Id
                                 where sg.IsActive && ids.Contains(sg.StudentId)
                                 select new { sg.StudentId, c.Name, sg.Status, sg.YearFreeze })
            .ToListAsync();
        // «Aktiv muzlatish» — a'zolik `Status`ida EMAS, alohida bayroqda (qarang: GetAll).
        // Guruh chiplari (`StudentSearchGroupDto`) o'zgarmaydi: dropdownda "qayerda va qanday
        // holatda" yetarli, ajratish esa o'quvchi darajasidagi `memberState` belgisida ko'rinadi.
        var yearFrozenIds = memberships.Where(m => m.Status == "frozen" && m.YearFreeze)
            .Select(m => m.StudentId).ToHashSet();
        var groupsBy = memberships.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new StudentSearchGroupDto(x.Name, x.Status ?? ""))
                .ToList());

        return merged.Select(r =>
        {
            var groups = groupsBy.GetValueOrDefault(r.Id) ?? new List<StudentSearchGroupDto>();
            var parentPhone = !string.IsNullOrEmpty(r.ParentPhone) ? r.ParentPhone
                : !string.IsNullOrEmpty(r.FatherPhone) ? r.FatherPhone : r.MotherPhone;
            return new StudentSearchResultDto(
                r.Id, r.FullName, r.Phone, parentPhone, r.IsArchived,
                StudentSearch.MemberState(groups.Select(x => (string?)x.Status), yearFrozenIds.Contains(r.Id)),
                groups);
        }).ToList();
    }

    /// <summary>O'quvchi shaxsiy daftari — bitta o'quvchi haqida barcha ma'lumot (profil, o'zlashtirish, davomat, oylik baholash, uy vazifa/xulq).</summary>
    [HttpGet("{id}/profile")]
    public async Task<ActionResult<StudentNotebookDto>> GetProfile(string id)
    {
        // AsNoTracking — quyida hujjat manzillari tozalanishi mumkin (qarang: RedactDocs).
        var st = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (st is null) return NotFound();
        // Tozalash MANBADA: `st` dan keyin butun daftar yig'iladi, ya'ni surat va passport
        // manzillari javobga umuman tushmaydi.
        RedactDocs([st]);
        return await StudentProfileBuilder.BuildAsync(db, st);
    }

    private static readonly System.Text.Json.JsonSerializerOptions AiJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>O'quvchining saqlangan AI tahlillari tarixi (eng yangisi birinchi). O'quvchi
    /// sahifasidagi "AI Tahlil" bo'limi shu yozuvlarni ko'rsatadi.</summary>
    [HttpGet("{id}/ai-analyses")]
    public async Task<ActionResult<IEnumerable<StudentAiAnalysisRecordDto>>> AiAnalyses(string id)
    {
        var rows = await db.StudentAiAnalyses
            .Where(a => a.StudentId == id)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .ToListAsync();
        return rows.Select(ToAiRecordDto).Where(r => r is not null).Select(r => r!).ToList();
    }

    /// <summary>O'quvchining BARCHA ma'lumotlarini Google Gemini orqali tahlil qiladi (strukturali:
    /// matn + diagramma sonlari). KUNIGA BIR MARTA: shu kun yozuvi bo'lsa Gemini chaqirilmaydi,
    /// mavjud yozuv qaytadi (AlreadyToday=true). Oldingi tahlil bo'lsa, yangi tahlil unga nisbatan
    /// o'zgarishlarni ham aytadi. API kaliti Sozlamalar → AI Tahlil (Gemini)da.</summary>
    [HttpPost("{id}/ai-analysis")]
    public async Task<ActionResult<StudentAiAnalysisResponseDto>> AiAnalysis(string id)
    {
        var st = await db.Students.FirstOrDefaultAsync(s => s.Id == id);
        if (st is null) return NotFound();

        var today = AppClock.Today.ToString("yyyy-MM-dd");

        // Kuniga bir marta: bugungi yozuv bo'lsa — uni qaytaramiz (Gemini chaqirilmaydi,
        // kalit tekshiruvidan oldin — keshlangan tahlilni ko'rsatish kalitga bog'liq emas).
        var todays = await db.StudentAiAnalyses
            .FirstOrDefaultAsync(a => a.StudentId == id && a.Date == today);
        if (todays is not null)
            return new StudentAiAnalysisResponseDto(true, true, ToAiRecordDto(todays), null);

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        var model = GeminiService.ResolveModel(config);
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return new StudentAiAnalysisResponseDto(false, false, null,
                "Gemini API kaliti sozlanmagan. Sozlamalar → AI Tahlil (Gemini) bo'limidan kalit kiriting.");

        // Oldingi (eng yangi) tahlil — yangi tahlil unga nisbatan o'zgarishlarni aytadi.
        var prev = await db.StudentAiAnalyses
            .Where(a => a.StudentId == id)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        var profile = await StudentProfileBuilder.BuildAsync(db, st);
        var json = System.Text.Json.JsonSerializer.Serialize(profile, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        });

        var prevContext = prev is null
            ? "Bu o'quvchining BIRINCHI tahlili — oldingi tahlil yo'q. \"ozgarishlar\" maydonini bo'sh (\"\") qoldir."
            : $"Oldingi tahlil ({prev.Date}) xulosasi: \"{prev.Summary}\". Oldingi umumiy ball: {prev.OverallScore}. " +
              "\"ozgarishlar\" maydonida ANA SHU oldingi tahlilga nisbatan nima o'zgarganini (yaxshilangan/yomonlashgan " +
              "joylar, ball farqi) aniq yoz.";

        var prompt =
            "Sen o'quv markazi uchun tajribali pedagog-tahlilchisan. Quyida bitta o'quvchining to'liq " +
            "ma'lumotlari JSON ko'rinishida: shaxsiy ma'lumotlar, fanlar bo'yicha oylik baholar, davomat " +
            "(qoldirgan/kasal/kech), oylik baholash (feedback), uy vazifa va xulq dinamikasi, " +
            "to'lov balansi.\n\n" +
            "Vazifa: shu ma'lumotni CHUQUR tahlil qilib, FAQAT O'ZBEK TILIDA (lotin alifbosi) natijani QUYIDAGI " +
            "JSON sxemasida QAYTAR (boshqa hech narsa yozma, faqat JSON):\n" +
            "{\n" +
            "  \"umumiy\": \"2-4 jumla — o'quvchining hozirgi umumiy holati\",\n" +
            "  \"kuchli\": [\"kuchli tomon\", ...],\n" +
            "  \"zaif\": [\"zaif tomon / e'tibor kerak\", ...],\n" +
            "  \"dinamika\": \"o'qishdagi dinamika — yaxshilanmoqda/barqaror/yomonlashmoqda, sabablari bilan\",\n" +
            "  \"ozgarishlar\": \"oldingi tahlilga nisbatan o'zgarishlar (yo'q bo'lsa bo'sh)\",\n" +
            "  \"tavsiyalar\": [\"o'qituvchi/ota-onaga aniq amaliy tavsiya\", ...],\n" +
            "  \"baholar\": { \"akademik\": 0-100, \"davomat\": 0-100, \"intizom\": 0-100, \"uyVazifa\": 0-100, \"faollik\": 0-100, \"umumiy\": 0-100 },\n" +
            "  \"trend\": \"yaxshilanmoqda\" yoki \"barqaror\" yoki \"yomonlashmoqda\"\n" +
            "}\n\n" +
            "Qoidalar: \"baholar\" — 0..100 butun sonlar (real ma'lumotga asoslangan baho; ma'lumot yo'q soha uchun " +
            "ehtiyotkor o'rta baho). \"umumiy\" — boshqa sohalarning umumlashmasi. Faqat berilgan ma'lumotga tayan, " +
            "to'qib chiqarma. Har matn maydoni qisqa va aniq. " + prevContext + "\n\n" +
            "O'quvchi ma'lumotlari (JSON):\n" + json;

        var (ok, text, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, prompt, jsonMode: true);
        if (!ok)
            return new StudentAiAnalysisResponseDto(false, false, null, err);

        var result = ParseAiResult(text);
        if (result is null)
            return new StudentAiAnalysisResponseDto(false, false, null,
                "AI javobini o'qib bo'lmadi (format xato). Qaytadan urinib ko'ring.");

        var rec = new StudentAiAnalysis
        {
            StudentId = st.Id,
            Date = today,
            CreatedAt = AppClock.Iso(),
            Model = model,
            Summary = Trim(result.Umumiy, 600),
            OverallScore = Math.Clamp(result.Baholar.Umumiy, 0, 100),
            ResultJson = System.Text.Json.JsonSerializer.Serialize(result, AiJsonOpts),
        };
        db.StudentAiAnalyses.Add(rec);
        await db.SaveChangesAsync();

        return new StudentAiAnalysisResponseDto(true, false, ToAiRecordDto(rec), null);
    }

    private static string Trim(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>Saqlangan ResultJson'ni typed record'ga aylantiradi (yozuv → DTO).</summary>
    private static StudentAiAnalysisRecordDto? ToAiRecordDto(StudentAiAnalysis a)
    {
        var result = ParseStored(a.ResultJson);
        if (result is null) return null;
        return new StudentAiAnalysisRecordDto(a.Id, a.Date, a.CreatedAt, a.Model, a.OverallScore, result);
    }

    private static StudentAiAnalysisResultDto? ParseStored(string json)
    {
        try
        {
            var r = System.Text.Json.JsonSerializer.Deserialize<StudentAiAnalysisResultDto>(json, AiJsonOpts);
            return r is null ? null : Sanitize(r);
        }
        catch { return null; }
    }

    /// <summary>Gemini JSON javobini typed natijaga aylantiradi (kod-fence tozalanadi, null'lar to'ldiriladi).</summary>
    private static StudentAiAnalysisResultDto? ParseAiResult(string text)
    {
        var t = (text ?? "").Trim();
        // ```json ... ``` fence bo'lsa tozalaymiz.
        if (t.StartsWith("```"))
        {
            var nl = t.IndexOf('\n');
            if (nl >= 0) t = t[(nl + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
            t = t.Trim();
        }
        // Birinchi { dan oxirgi } gacha (oldi/orqa shovqinni kesish).
        var open = t.IndexOf('{');
        var close = t.LastIndexOf('}');
        if (open >= 0 && close > open) t = t[open..(close + 1)];
        try
        {
            var r = System.Text.Json.JsonSerializer.Deserialize<StudentAiAnalysisResultDto>(t, AiJsonOpts);
            return r is null ? null : Sanitize(r);
        }
        catch { return null; }
    }

    private static StudentAiAnalysisResultDto Sanitize(StudentAiAnalysisResultDto r)
    {
        static int C(int v) => Math.Clamp(v, 0, 100);
        var b = r.Baholar ?? new AiRatingsDto(0, 0, 0, 0, 0, 0);
        return new StudentAiAnalysisResultDto(
            r.Umumiy ?? "",
            r.Kuchli ?? new List<string>(),
            r.Zaif ?? new List<string>(),
            r.Dinamika ?? "",
            r.Ozgarishlar ?? "",
            r.Tavsiyalar ?? new List<string>(),
            new AiRatingsDto(C(b.Akademik), C(b.Davomat), C(b.Intizom), C(b.UyVazifa), C(b.Faollik), C(b.Umumiy)),
            r.Trend ?? "");
    }

    /// <summary>O'quvchiga support o'qituvchilar bergan feedback — o'tilgan (done) support darslari
    /// (sana, support o'qituvchi ismi, mavzu, izoh), eng yangi birinchi. Support guruhga bog'lanmaydi,
    /// shuning uchun feedback shu yerda alohida ko'rsatiladi.</summary>
    [HttpGet("{id}/support-feedback")]
    public async Task<ActionResult<IEnumerable<StudentSupportFeedbackDto>>> SupportFeedback(string id)
    {
        var slots = await db.SupportSlots
            .Where(s => s.StudentId == id && s.Status == "done")
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.StartTime)
            .ToListAsync();
        if (slots.Count == 0) return new List<StudentSupportFeedbackDto>();
        var tIds = slots.Select(s => s.TeacherId).Distinct().ToList();
        var tNames = (await db.Teachers.Where(t => tIds.Contains(t.Id)).ToListAsync())
            .ToDictionary(t => t.Id, t => t.FullName);
        return slots.Select(s => new StudentSupportFeedbackDto(
            s.Date, s.StartTime, s.EndTime, tNames.GetValueOrDefault(s.TeacherId, ""),
            s.Topic, s.Notes)).ToList();
    }

    /// <summary>Faqat arxivlangan o'quvchilar ro'yxati.</summary>
    [HttpGet("archived")]
    public async Task<ActionResult<IEnumerable<Student>>> GetArchived()
    {
        var students = await db.Students.AsNoTracking().Where(s => s.IsArchived)
            .OrderByDescending(s => s.ArchivedAt).ThenBy(s => s.FullName).ToListAsync();
        RedactDocs(students);
        // A'ZOLIK KESIMI — ro'yxat bilan BIR XIL (arxiv tabi ham o'sha ustunlarni chizadi).
        // Ilgari to'ldirilmasdi: arxivdagi HAR o'quvchi "Aktiv emas" bo'lib va guruh sifatida
        // eski `ClassName` yorlig'i bilan ko'rinardi.
        await StudentMembershipView.EnrichAsync(db, students);
        // "Arxiv o'quvchilar" ro'yxati ustunlari (moderator va h.k.) — faol ro'yxat bilan bir xil manba.
        await StudentListView.EnrichExtrasAsync(db, students);
        return students;
    }

    /// <summary>
    /// BOSH SAHIFA KARTOCHKASI RO'YXATI — "Yangi o'quvchidan ketganlar" (<c>leftNew</c>), "Aktiv
    /// o'quvchidan ketganlar" (<c>leftActive</c>), "Birinchi to'lovni qilganlar" (<c>firstPayment</c>).
    /// Tanlov kartochka sonini hisoblaydigan AYNAN o'sha funksiyadan (<see cref="DashboardSummary.MovementAsync"/>),
    /// ya'ni qatorlar soni kartochkadagi son bilan teng.
    /// <para>⚠️ ARXIVLANGANLAR HAM qaytadi: ketgan o'quvchi ko'pincha keyin arxivlanadi, kartochka esa
    /// uni baribir sanaydi (<c>knownIds</c> — barcha o'quvchilar).</para>
    /// <para><paramref name="month"/> — "yyyy-MM", bo'sh = joriy oy. Ruxsat — sinf darajasidagi
    /// <c>students.list</c> (ro'yxat bilan bir xil, hujjat manzillari <see cref="RedactDocs"/> bilan).</para>
    /// </summary>
    [HttpGet("month-movement")]
    public async Task<ActionResult<IEnumerable<Student>>> MonthMovement(
        [FromQuery] string? kind, [FromQuery] string? month = null)
    {
        if (kind is null || !DashboardSummary.MovementKinds.Contains(kind))
            return BadRequest(new { message = $"Noma'lum ro'yxat: {kind}" });
        var m = string.IsNullOrEmpty(month) ? AppClock.Today.ToString("yyyy-MM") : month;
        if (m.Length != 7 || !DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return BadRequest(new { message = "Oy noto'g'ri (kutilgan ko'rinish: yyyy-MM)" });

        var rows = await DashboardSummary.MovementAsync(db, kind, m);
        var ids = rows.Keys.ToList();
        // AsNoTracking — pastda ko'rsatish uchun o'zgartiriladi (RedactDocs, guruh nomlari).
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync();
        RedactDocs(students);
        await StudentMembershipView.EnrichAsync(db, students);
        await StudentListView.EnrichExtrasAsync(db, students);

        var groupIds = rows.Values.Select(r => r.GroupId).Where(g => g.Length > 0).Distinct().ToList();
        var groupNames = await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name);
        foreach (var s in students)
        {
            var r = rows[s.Id];
            s.MovementDate = r.Date;
            s.MovementGroup = groupNames.GetValueOrDefault(r.GroupId, "");
        }
        return students
            .OrderByDescending(s => s.MovementDate, StringComparer.Ordinal)
            .ThenBy(s => s.FullName)
            .ToList();
    }

    /// <summary>
    /// "JORIY OYDA OBUNASI TUGAYDIGANLAR" — puli joriy oy darslarini qoplamaydigan o'quvchilar
    /// (<see cref="SubscriptionRisk"/>: mavjud hisob qatorlari + accrual qoidasi, qayta hisob yo'q).
    /// Ruxsat — sinf darajasidagi <c>students.list</c> darvozasi (o'quvchilar ro'yxati bilan
    /// bir xil; javobda fayl manzili yo'q). Metodga alohida kalit QO'YILMAYDI — u sinfdagisini
    /// bekor qilardi (permissions.md §4).
    /// </summary>
    [HttpGet("subscription-risk")]
    public async Task<ActionResult<SubscriptionRiskReportDto>> SubscriptionRiskReport() =>
        await SubscriptionRisk.BuildAsync(db, TuitionService.CurrentMonth());

    /// <summary>Bitta o'quvchi (profil sahifasidan tahrirlash formasi uchun to'liq obyekt).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Student>> GetOne(string id)
    {
        // AsNoTracking — pastda hujjat manzillari tozalanishi mumkin (qarang: RedactDocs).
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        RedactDocs([s]);
        // A'ZOLIK KESIMI (guruhlar, holat, "aktiv") — RO'YXAT bilan AYNAN bir xil manbadan.
        // ⚠️ Ilgari bu YO'Q edi: javobda `Active=false`, `MemberState=""`, `Groups=[]` kelardi va
        // profil/tahrir/SMS/kassa oynalari o'quvchini "Aktiv emas" deb, guruh sifatida esa eski
        // `ClassName` (BIRINCHI qo'shilgan guruh) yorlig'ini ko'rsatardi — eski guruhida
        // muzlatilib yangisida o'qiyotgan o'quvchi aynan shunday "yo'qolib" ketardi.
        await StudentMembershipView.EnrichAsync(db, [s]);
        // AMALDAGI CHEGIRMALAR SONI — `MemberState` naqshi (bazada YO'Q, faqat javobda).
        // Profil sahifasida "chegirma bormi va nechta fanda" savoliga javob beradi; batafsili
        // «Chegirma» tabida (`GET {id}/discounts`).
        s.DiscountCount = await db.StudentDiscounts.AsNoTracking()
            .CountAsync(d => d.StudentId == id && d.Status == StudentDiscount.StatusActive);
        return s;
    }

    /// <summary>
    /// Kiritilgan raqamlar (o'quvchi o'zi / ota / ona / ota-ona) allaqachon biror o'quvchida (ARXIVDAGILAR
    /// ham) ishlatilganmi — tekshiradi. Raqamlar <see cref="PhoneUtil.Key"/> bo'yicha (oxirgi 9 raqam)
    /// solishtiriladi. <paramref name="req"/>.ExcludeId — tahrirdagi o'quvchining o'zi (uni hisobga olmaymiz).
    /// Mos kelgan har bir o'quvchi uchun bitta qator qaytadi (qaysi raqam va qaysi rol mos kelgani bilan).
    /// </summary>
    [HttpPost("check-phones")]
    public async Task<ActionResult<IEnumerable<PhoneMatchDto>>> CheckPhones(CheckPhonesRequest req)
    {
        // Kiritilgan nomzod raqamlar (yorliq + xom qiymat + kalit). Bo'sh/juda qisqalar tashlanadi.
        var candidates = new[]
            {
                ("O'quvchi", req.Phone),
                ("Ota", req.FatherPhone),
                ("Ona", req.MotherPhone),
                ("Ota-ona", req.ParentPhone),
            }
            .Where(c => !string.IsNullOrWhiteSpace(c.Item2))
            .Select(c => (Label: c.Item1, Raw: c.Item2!.Trim(), Key: PhoneUtil.Key(c.Item2)))
            .Where(c => c.Key.Length >= 7)
            .ToList();
        if (candidates.Count == 0) return new List<PhoneMatchDto>();
        var candidateKeys = candidates.Select(c => c.Key).ToHashSet();

        // Arxivdagilar ham — barcha o'quvchilar.
        // ⚠️ Faqat KERAKLI ustunlar va `AsNoTracking()`: bu endpoint forma to'ldirilayotganda
        // (har maydon `blur`ida) chaqiriladi, ilgari esa har safar to'liq `Student` entity'lari
        // (passport, manzil, chegirma va h.k.) change-tracker bilan yuklanardi.
        // `PhoneUtil.Key` normalizatsiyasi SQL'ga tarjima bo'lmaydi, shuning uchun solishtirish
        // C# da qoladi — lekin tortiladigan ma'lumot bir necha barobar kamaydi.
        var students = await db.Students.AsNoTracking()
            .Select(s => new
            {
                s.Id, s.FullName, s.ClassName, s.IsArchived,
                s.Phone, s.FatherPhone, s.MotherPhone, s.ParentPhone,
            })
            .ToListAsync();
        var matches = new List<PhoneMatchDto>();
        foreach (var s in students)
        {
            if (!string.IsNullOrEmpty(req.ExcludeId) && s.Id == req.ExcludeId) continue;
            var existing = new[]
            {
                ("O'quvchi", s.Phone),
                ("Ota", s.FatherPhone),
                ("Ona", s.MotherPhone),
                ("Ota-ona", s.ParentPhone),
            };
            foreach (var ex in existing)
            {
                if (string.IsNullOrWhiteSpace(ex.Item2)) continue;
                var key = PhoneUtil.Key(ex.Item2);
                if (key.Length < 7 || !candidateKeys.Contains(key)) continue;
                var cand = candidates.First(c => c.Key == key);
                matches.Add(new PhoneMatchDto(cand.Raw, s.Id, s.FullName, s.ClassName, s.IsArchived, ex.Item1));
                break; // bitta o'quvchi — birinchi mos kelgan raqam yetarli
            }
        }
        return matches;
    }

    /// <summary>ASOSIY ota-ona kontaktini tanlash: ota (bo'lmasa ona); ikkalasi ham bo'sh bo'lsa
    /// orqaga moslik uchun <paramref name="fallback"/> (eski ParentFullName/ParentPhone).</summary>
    private static string DerivePrimary(string? father, string? mother, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(father)) return father.Trim();
        if (!string.IsNullOrWhiteSpace(mother)) return mother.Trim();
        return (fallback ?? "").Trim();
    }

    /// <summary>"YYYY-MM" oyini normallashtiradi. Klient <c>&lt;input type="month"&gt;</c> aynan shu
    /// formatda yuboradi, lekin to'liq sana ("2026-08-01") kelib qolsa ham oy qismi olinadi;
    /// noto'g'ri qiymat bo'sh satrga tushadi ("hali boshlanmagan").</summary>
    private static string NormalizeMonth(string? month)
    {
        var m = (month ?? "").Trim();
        if (m.Length < 7) return "";
        m = m[..7];
        return DateOnly.TryParse($"{m}-01", out _) ? m : "";
    }

    private const string SchoolGradeInvalid = "Sinf 1 dan 11 gacha yoki «Maktabni bitirgan» bo'lishi kerak";

    /// <summary><see cref="StudentPayload.SchoolGrade"/> qabul qilinadimi: <c>null</c> (tegilmaydi),
    /// <c>0</c> (tozalash) yoki <see cref="SchoolGrades.IsValid"/>.</summary>
    private static bool SchoolGradeOk(int? grade) => grade is null or 0 || SchoolGrades.IsValid(grade.Value);

    /// <summary>
    /// Maktab sinfini yozadi. Qiymat JORIY o'quv yiliga bog'lab saqlanadi — keyingi yillarda
    /// <see cref="SchoolGrades.Current"/> uni o'zi oshiradi. Tahrir formasi joriy sinfni qaytarib
    /// yuborgani uchun qayta bog'lash natijani O'ZGARTIRMAYDI (g + (Y − y0) = joriy).
    /// </summary>
    private static void ApplySchoolGrade(Student s, int? grade)
    {
        if (grade is null || !SchoolGradeOk(grade)) return;
        if (grade == 0)
        {
            s.SchoolGrade = null;
            s.SchoolGradeYear = null;
            return;
        }
        s.SchoolGrade = grade;
        s.SchoolGradeYear = SchoolGrades.AcademicYear(AppClock.Today);
    }

    /// <summary>"Familiya Ism Sharifi" — parts'ni birlashtirish (bo'sh qismlar tashlanadi).</summary>
    private static string JoinName(string? last, string? first, string? middle) =>
        string.Join(' ', new[] { last, first, middle }
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrEmpty(x)));

    [HttpPost]
    public async Task<ActionResult<Student>> Create(StudentPayload p)
    {
        if (!SchoolGradeOk(p.SchoolGrade)) return BadRequest(new { message = SchoolGradeInvalid });
        // ⚠️ Nom bo'yicha qidiruvda ARXIVLANMAGAN guruh ustun: «Tugatish (sertifikat bilan)» yangi guruhga
        // eski nomni beradi, ya'ni bir xil nomli arxiv guruh ham bo'ladi — o'quvchi o'shanga tushib qolardi.
        var cls = await db.Classes.Where(c => c.Name == p.ClassName).OrderBy(c => c.IsArchived).FirstOrDefaultAsync();
        // ARXIVDAGI (yopilgan/tugatilgan) guruhga yangi o'quvchi QO'SHILMAYDI — `AddMember` bilan bir xil
        // qoida: u yerda dars yo'q, a'zolik esa keyin oylik hisobga tushib yopiq guruhdan qarz yozilardi
        // (audit R1-1). Nom noyob bo'lgani uchun eski nom endi faqat arxiv guruhniki bo'lishi mumkin.
        if (cls is { IsArchived: true })
            return BadRequest(new { message = ArchivedGroupMessage(cls) + " — boshqa guruh tanlang yoki guruhsiz saqlang" });
        // SIG'IM — guruh sahifasidagi «Guruhga qo'shish» (AddMember) bilan BIR XIL qoida: to'lgan
        // guruhga yangi (sinov) a'zo qo'shilmaydi. Rad etiladi (400) — forma ochiq qoladi va admin
        // boshqa guruh tanlaydi yoki guruhsiz saqlaydi; jimgina "guruhsiz yaratildi" bo'lib qolmasin.
        if (cls is not null && cls.Capacity > 0 && !GroupSeats.HasRoom(cls, await GroupSeats.OccupiedAsync(db, cls.Id)))
            return BadRequest(new { message = $"{GroupSeats.FullMessage(cls)} — boshqa guruh tanlang yoki guruhsiz saqlang" });
        var student = AddStudent(p, cls);
        // CHEGIRMA REGISTRI: o'quvchi chegirma bilan YARATILSA registrda qator ochiladi —
        // «BARCHA GURUHLAR» qamrovida (`groupId: null`). ⚠️ Bu ATAYIN qoldirilgan: import va eski
        // API mijozlari o'quvchini chegirma bilan yaratadi va ular buzilmasligi kerak. TAHRIRLASH
        // yo'lida (`PUT`) esa chegirma endi umuman qabul qilinmaydi — u profildagi «Chegirma»
        // bo'limidan boshqariladi (`.claude/rules/discounts.md` §6).
        //
        // ⚠️ `ApplyAsync` ko'zguni (Student.Discount*) O'ZI qayta yozadi — `AddStudent` qo'ygan
        // qiymatlar bilan bir xil chiqadi, ya'ni natija o'zgarmaydi.
        if (student.DiscountPct > 0 || student.DiscountAmount > 0)
            await StudentDiscountService.ApplyAsync(
                db, student,
                new DiscountSpec(student.DiscountPct, student.DiscountAmount,
                    student.DiscountStartMonth, student.DiscountEndMonth, student.DiscountNote,
                    student.DiscountGroupId),
                Actor, ActorId);
        await db.SaveChangesAsync();

        // Avto xabar — o'quvchi guruhga qo'shilganda ota-onaga ("O'quvchi guruhga qo'shilganda" hodisasi).
        if (cls is not null)
            await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.StudentAdded, student,
                new Dictionary<string, string> { ["{guruh}"] = cls.Name }, group: cls);
        // A'ZOLIK KESIMI — javob RO'YXATdagi qator bilan bir xil bo'lsin: klient yangi o'quvchini
        // ro'yxatga qayta so'rovsiz qo'shadi va usiz qator "Aktiv emas" + guruhsiz ko'rinardi.
        await StudentMembershipView.EnrichAsync(db, [student]);
        return student;
    }

    /// <summary>
    /// <see cref="StudentPayload"/>'dan Student yaratib (tizim akkaunti + oylik hisoblar + audit bilan)
    /// db kontekstiga qo'shadi. SaveChanges QILMAYDI — chaqiruvchi (bitta yaratish yoki ommaviy import)
    /// hammasini qo'shib bo'lgach bir marta saqlaydi. <paramref name="cls"/> — oldindan topilgan guruh
    /// (narx/hisob uchun; null bo'lsa oylik hisob yozilmaydi).
    /// `status` — a'zolik holati (trial/active/frozen); bo'sh bo'lsa default "trial".
    /// </summary>
    private Student AddStudent(StudentPayload p, Group? cls, string status = "")
    {
        // Noto'g'ri/qisqa sana (masalan "2024", "abc") saqlanmasin — oylik billing batch'ini crash qilmasligi
        // uchun bo'sh/whitespace bilan bir xil muomala: bugungi sanaga tushamiz.
        var enrollment = string.IsNullOrWhiteSpace(p.EnrollmentDate)
                || p.EnrollmentDate.Length != 10 || !DateOnly.TryParse(p.EnrollmentDate, out _)
            ? AppClock.Today.ToString("yyyy-MM-dd")
            : p.EnrollmentDate;

        // FISH parts berilsa ulardan FullName yig'iladi. Aks holda eski yagona FullName ishlatiladi.
        var lastName = (p.LastName ?? "").Trim();
        var firstName = (p.FirstName ?? "").Trim();
        var middleName = (p.MiddleName ?? "").Trim();
        var fullName = (lastName + firstName + middleName) == ""
            ? (p.FullName ?? "").Trim()
            : JoinName(lastName, firstName, middleName);

        var parentLast = (p.ParentLastName ?? "").Trim();
        var parentFirst = (p.ParentFirstName ?? "").Trim();
        var parentMiddle = (p.ParentMiddleName ?? "").Trim();

        // Lid formasiga mos: o'quvchi telefoni + ota/ona (FISH+telefon).
        var fatherFull = (p.FatherFullName ?? "").Trim();
        var fatherPhone = PhoneUtil.Normalize(p.FatherPhone ?? "");
        var motherFull = (p.MotherFullName ?? "").Trim();
        var motherPhone = PhoneUtil.Normalize(p.MotherPhone ?? "");

        // ASOSIY ota-ona kontakti — ota (bo'lmasa ona) dan; ikkalasi bo'sh bo'lsa eski parts/payload.
        var parentFull = DerivePrimary(fatherFull, motherFull,
            (parentLast + parentFirst + parentMiddle) == ""
                ? (p.ParentFullName ?? "").Trim()
                : JoinName(parentLast, parentFirst, parentMiddle));
        var parentPhone = DerivePrimary(fatherPhone, motherPhone, PhoneUtil.Normalize(p.ParentPhone ?? ""));

        var student = new Student
        {
            FullName = fullName,
            LastName = lastName,
            FirstName = firstName,
            MiddleName = middleName,
            BirthDate = p.BirthDate,
            Address = p.Address,
            Gender = StudentGender.Normalize(p.Gender), // ixtiyoriy — bo'sh qoladi
            Phone = PhoneUtil.Normalize(p.Phone ?? ""),
            BirthCertificateUrl = string.IsNullOrWhiteSpace(p.BirthCertificateUrl) ? null : p.BirthCertificateUrl,
            ParentFullName = parentFull,
            ParentLastName = parentLast,
            ParentFirstName = parentFirst,
            ParentMiddleName = parentMiddle,
            ParentPhone = parentPhone,
            FatherFullName = fatherFull,
            FatherPhone = fatherPhone,
            MotherFullName = motherFull,
            MotherPhone = motherPhone,
            ParentPassportUrl = string.IsNullOrWhiteSpace(p.ParentPassportUrl) ? null : p.ParentPassportUrl,
            ClassName = p.ClassName,
            EnrollmentDate = enrollment,
            CreatedAt = AppClock.Iso(),
            Balance = 0,
            DiscountPct = Math.Clamp(p.DiscountPct ?? 0, 0, 100),
            DiscountAmount = Math.Max(0m, p.DiscountAmount ?? 0m),
            DiscountNote = (p.DiscountNote ?? "").Trim(),
            DiscountStartMonth = (p.DiscountStartMonth ?? "").Trim(),
            DiscountEndMonth = (p.DiscountEndMonth ?? "").Trim(),
            DiscountGroupId = string.IsNullOrWhiteSpace(p.DiscountGroupId) ? null : p.DiscountGroupId.Trim(),
            // Ushlab turish bonusi — ESKI (legacy) maydonlar. Endi bonusga kirish HAR FAN uchun
            // alohida, a'zolikni AKTIVLASHTIRISH oynasidagi ptichka orqali belgilanadi
            // (RetentionBonusTrack). O'quvchi formasi bu maydonlarni endi YUBORMAYDI; shu sabab
            // yangi o'quvchida ular bo'sh qoladi. Eski yozuvlar uchun o'qilishda davom etadi.
            RetentionBonus = p.RetentionBonus ?? false,
            RetentionBonusStartMonth = NormalizeMonth(p.RetentionBonusStartMonth),
        };
        ApplySchoolGrade(student, p.SchoolGrade);
        db.Students.Add(student);

        // O'quvchiga "student" rolli tizim akkaunti generatsiya qilib biriktiramiz.
        var account = AccountFactory.CreateAccountFor(db, "student", student.FullName);
        student.UserId = account.Id;

        // Guruh tanlangan bo'lsa — M2M a'zolik yaratamiz. Status: trial/active/frozen (default: trial)
        // Oylik to'lov DARROV yozilmaydi — a'zolik AKTIVLASHTIRILGANDA boshlanadi
        // (qisman birinchi oy + keyingi to'liq oylar; muzlatilsa to'xtaydi).
        if (cls is not null)
        {
            AddMembershipRow(student.Id, cls, status, enrollment);
            // A'zolik audit qatori — guruh sahifasining «Tarix» tabida ham ko'rinsin (AddMember naqshi,
            // `EntityId = "{groupId}:{studentId}"`). Ilgari bu yo'l a'zolikni izsiz yaratardi.
            audit.Record("Membership", $"{cls.Id}:{student.Id}", "create",
                $"Guruhga qo'shildi (o'quvchi yaratildi): {student.FullName} → {cls.Name} ({NormalizeStatus(status)}, {enrollment})",
                studentId: student.Id);
        }

        // Chegirma berilgan bo'lsa — audit yozuvi.
        if (student.DiscountPct > 0 || student.DiscountAmount > 0)
            audit.Record(AuditService.EntityStudentDiscount, student.Id, "create",
                DiscountSummary("O'quvchi yaratildi", student.FullName, 0, 0, student.DiscountPct, student.DiscountAmount, student.DiscountNote),
                after: DiscountSnapshot(student), studentId: student.Id);

        // O'quvchi yaratildi — "kim yaratdi" audit izi (profil snapshot bilan).
        audit.Record(AuditService.EntityStudent, student.Id, "create",
            $"O'quvchi qo'shildi ({student.FullName})",
            after: AuditService.StudentProfileSnapshot(student), studentId: student.Id);

        return student;
    }

    /// <summary>Chegirma o'zgarishi audit izohini tuzadi.</summary>
    private static string DiscountSummary(
        string action, string studentName,
        int oldPct, decimal oldAmount, int newPct, decimal newAmount, string note)
    {
        var changed = (oldPct != newPct ? $"{oldPct}% → {newPct}%" : $"{newPct}%")
                    + ", "
                    + (oldAmount != newAmount
                        ? $"{AuditService.Money(oldAmount)} → {AuditService.Money(newAmount)} so'm"
                        : $"{AuditService.Money(newAmount)} so'm");
        var n = string.IsNullOrWhiteSpace(note) ? "" : $" — \"{note}\"";
        return $"{action}: chegirma {changed}{n} ({studentName})";
    }

    /// <summary>Chegirma snapshot'i (audit Before/After uchun).</summary>
    private static object DiscountSnapshot(Student s) => new
    {
        s.DiscountPct,
        s.DiscountAmount,
        s.DiscountNote,
    };

    /// <summary>
    /// O'quvchini tahrirlash.
    ///
    /// <para>⚠️ <b>CHEGIRMA BU YERDAN YOZILMAYDI.</b> Tanadagi <c>Discount*</c> maydonlari va
    /// <paramref name="applyDiscount"/> parametri E'TIBORGA OLINMAYDI — ikkalasi ham API shakli
    /// buzilmasin deb qoldirilgan (mobil ilova/import mijozlari). Chegirma endi HAR FAN uchun
    /// alohida va faqat profildagi «Chegirma» bo'limidan boshqariladi
    /// (<see cref="StudentDiscountsController"/>, <c>.claude/rules/discounts.md</c> §6).</para>
    /// </summary>
    /// <param name="applyDiscount">⚠️ ISHLATILMAYDI (orqaga moslik uchun qabul qilinadi).</param>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, StudentPayload p, [FromQuery] bool applyDiscount = false)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        if (!SchoolGradeOk(p.SchoolGrade)) return BadRequest(new { message = SchoolGradeInvalid });

        var beforeProfile = AuditService.StudentProfileSnapshot(student);
        // Telegram ulanishlari — raqamlar o'zgarsa eskirganlari uziladi (pastda, saqlashdan oldin).
        var phonesBefore = TelegramLinks.PhonesSignature(student);

        var oldClassName = student.ClassName;

        // O'quvchi FISH — parts berilsa ulardan FullName yig'iladi.
        if (p.LastName is not null || p.FirstName is not null || p.MiddleName is not null)
        {
            student.LastName = (p.LastName ?? "").Trim();
            student.FirstName = (p.FirstName ?? "").Trim();
            student.MiddleName = (p.MiddleName ?? "").Trim();
            student.FullName = JoinName(student.LastName, student.FirstName, student.MiddleName);
        }
        else
        {
            student.FullName = p.FullName;
        }
        student.BirthDate = p.BirthDate;
        student.Address = p.Address;
        student.Gender = StudentGender.Normalize(p.Gender);
        student.Phone = PhoneUtil.Normalize(p.Phone ?? "");
        if (p.BirthCertificateUrl is not null)
            student.BirthCertificateUrl = string.IsNullOrWhiteSpace(p.BirthCertificateUrl) ? null : p.BirthCertificateUrl;
        // Eski FISH parts berilsa ham yangilanadi (orqaga moslik); lekin ASOSIY kontakt ota/onadan keladi.
        if (p.ParentLastName is not null || p.ParentFirstName is not null || p.ParentMiddleName is not null)
        {
            student.ParentLastName = (p.ParentLastName ?? "").Trim();
            student.ParentFirstName = (p.ParentFirstName ?? "").Trim();
            student.ParentMiddleName = (p.ParentMiddleName ?? "").Trim();
        }
        // Lid formasiga mos: ota/ona (FISH+telefon) — va ulardan ASOSIY ota-ona kontaktini chiqaramiz.
        student.FatherFullName = (p.FatherFullName ?? "").Trim();
        student.FatherPhone = PhoneUtil.Normalize(p.FatherPhone ?? "");
        student.MotherFullName = (p.MotherFullName ?? "").Trim();
        student.MotherPhone = PhoneUtil.Normalize(p.MotherPhone ?? "");
        student.ParentFullName = DerivePrimary(student.FatherFullName, student.MotherFullName, (p.ParentFullName ?? "").Trim());
        student.ParentPhone = DerivePrimary(student.FatherPhone, student.MotherPhone, PhoneUtil.Normalize(p.ParentPhone ?? ""));
        if (p.ParentPassportUrl is not null)
            student.ParentPassportUrl = string.IsNullOrWhiteSpace(p.ParentPassportUrl) ? null : p.ParentPassportUrl;
        // «ASOSIY GURUH» YORLIG'I (`ClassName`) — o'quvchida TIRIK a'zolik bo'lsa BU YO'LDAN
        // O'ZGARTIRILMAYDI.
        //
        // ⚠️ Sabab: bu endpoint a'zolikni KO'CHIRA olmaydi (pastda a'zolik faqat "umuman a'zoligi
        // yo'q" holatda yaratiladi) — ya'ni yorliqni yozish "guruhi o'zgardi" degan YOLG'ON holat
        // yasardi: o'quvchi guruhda qolaverar, lekin yorliqqa qaraydigan joylar (eski hisobotlar,
        // ilova, chat, guruh arxivlash) uni boshqa guruhda ko'rsatardi. Guruh a'zoligi
        // `ClassesController` orqali boshqariladi (qo'shish/aktivlashtirish/muzlatish/ko'chirish),
        // o'quvchi formasida esa bunday o'quvchiga guruh tanlagichi umuman ko'rsatilmaydi.
        var hasLiveMembership = await db.StudentGroups
            .AnyAsync(sg => sg.StudentId == student.Id && sg.IsActive);
        // ⚠️ ARXIVDAGI o'quvchiga bu yo'ldan guruh BERILMAYDI (arxiv = ketdi, `StudentArchiving`): aks holda
        // pastda arxivdagi o'quvchiga a'zolik (yoki guruhsiz oylik hisob) ochilib qolardi. Guruhga qaytarish —
        // arxivdan tiklash yoki «Guruhga qo'shish» (AddMember u o'quvchini arxivdan o'zi chiqaradi).
        // ARXIVDAGI guruh tanlansa — rad etiladi (Create bilan bir xil, audit R1-1): a'zolik baribir ochilmasdi,
        // lekin yorliq yopiq guruhga yozilib, pastdagi guruhsiz hisob bloki undan oylik yozardi.
        if (!hasLiveMembership && !student.IsArchived && !string.IsNullOrWhiteSpace(p.ClassName)
            && !string.Equals(oldClassName, p.ClassName, StringComparison.Ordinal))
        {
            var target = await db.Classes.AsNoTracking().Where(c => c.Name == p.ClassName)
                .OrderBy(c => c.IsArchived).FirstOrDefaultAsync();
            if (target is { IsArchived: true })
                return BadRequest(new { message = ArchivedGroupMessage(target) + " — boshqa guruh tanlang" });
        }
        if (!hasLiveMembership && !student.IsArchived) student.ClassName = p.ClassName;
        var (oldGrade, oldGradeYear) = (student.SchoolGrade, student.SchoolGradeYear);
        ApplySchoolGrade(student, p.SchoolGrade);
        // Sinf o'zgarsa — kurs SINF NARXI bo'lgan guruhlarda joriy oylik DARHOL moslanadi.
        await GradeRepricing.ApplyAsync(db, audit, student, oldGrade, oldGradeYear);
        if (!string.IsNullOrWhiteSpace(p.EnrollmentDate))
        {
            // Noto'g'ri sana saqlanib, keyin oylik billing batch'ini crash qilmasligi uchun aniq xato qaytaramiz.
            if (p.EnrollmentDate.Length != 10 || !DateOnly.TryParse(p.EnrollmentDate, out _))
                return BadRequest(new { message = "Ro'yxatga olingan sana noto'g'ri formatda (kutilgan: YYYY-MM-DD)" });
            student.EnrollmentDate = p.EnrollmentDate;
        }

        // ⚠️ CHEGIRMA BU YERDA YOZILMAYDI. `StudentPayload` dagi `Discount*` maydonlari
        // (`DiscountPct`, `DiscountAmount`, `DiscountNote`, `DiscountStartMonth`,
        // `DiscountEndMonth`, `DiscountGroupId`) va `?applyDiscount=` parametri bu yo'lda
        // E'TIBORGA OLINMAYDI — DTO'dan olib tashlanmagan, chunki API shakli buzilmasligi kerak.
        //
        // Sabab: chegirma endi HAR FAN uchun alohida bo'lishi mumkin, ya'ni bitta "foiz + summa"
        // juftligi butun holatni ifodalay olmaydi. Eski forma o'sha juftlikni yozsa, u
        // o'quvchining BOSHQA fanlaridagi chegirmalarini jimgina o'chirib yuborardi.
        // Chegirma FAQAT profildagi «Chegirma» bo'limidan boshqariladi
        // (`StudentDiscountsController`, `.claude/rules/discounts.md` §6).
        //
        // `Student.Discount*` ko'zgusi esa faqat registrdan yangilanadi
        // (`StudentDiscountService.RefreshMirrorAsync`).

        // Ushlab turish bonusi — ESKI maydonlar (yuqoridagi izohga qarang). O'quvchi formasi
        // ularni endi yubormaydi, ya'ni bu yerda null keladi va MAVJUD qiymat TEGILMAYDI —
        // oddiy tahrirlash eski bonus sozlamasini yo'qotmaydi.
        if (p.RetentionBonus.HasValue) student.RetentionBonus = p.RetentionBonus.Value;
        if (p.RetentionBonusStartMonth is not null)
            student.RetentionBonusStartMonth = NormalizeMonth(p.RetentionBonusStartMonth);
        // Ptichka o'chirilsa sanoq oyi ham tozalanadi — qayta yoqilganda eski oy "tirilib"
        // kutilmagan sikl ochib yubormasin.
        if (!student.RetentionBonus) student.RetentionBonusStartMonth = "";

        // Akkaunt nomini sinxronlaymiz va (ixtiyoriy) yangi parol o'rnatamiz.
        var user = student.UserId is null ? null : await db.Users.FindAsync(student.UserId);
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            var pwd = p.NewPassword.Trim();
            if (pwd.Length < MinPasswordLength) return BadRequest(new { message = WeakPasswordMessage });
            // Parol o'rnatish = akkauntga kirish — `ResetPassword`/`Credentials` bilan BIR XIL darvoza (AUTH-4).
            if (!AdminPermAttribute.HasFullAccess(User, "students.list"))
                return StatusCode(StatusCodes.Status403Forbidden, new { message = PasswordForbiddenMessage });
            // Akkaunt yo'q bo'lsa — yaratib biriktiramiz.
            user ??= AccountFactory.CreateAccountFor(db, "student", student.FullName);
            student.UserId = user.Id;
            user.SetInitialPassword(pwd);
            await RefreshTokenService.RevokeAllForUserAsync(db, user.Id); // eski sessiyalar tugaydi (AUTH-1)
        }
        if (user is not null) user.FullName = student.FullName;

        // Guruh o'zgardimi? (Chegirma bu yo'lda umuman o'zgarmaydi — yuqoridagi izohga qarang.)
        var classChanged = !string.Equals(oldClassName, student.ClassName, StringComparison.Ordinal);

        // Joriy guruh narxiga ko'ra hisoblarni TO'G'RILAYMIZ/TO'LDIRAMIZ (ClassName MATNI o'zgarmagan
        // bo'lsa ham — masalan o'quvchi guruh hali yaratilmagan paytda qo'shilib, keyin guruh yaratilgan):
        //  • yetishmagan oylar (kelgan oyidan joriy oygacha) — yangi narxda yaratiladi, balans kamayadi;
        //  • mavjud JORIY oy — guruh o'zgarsa yangi narxga moslanadi;
        //  • o'tgan oylardagi mavjud hisoblar — tarixiy, tegilmaydi.
        var applied = false;
        // M2M a'zolik bo'lsa billing AccrueMonth/aktivlashtirish (a'zolik narxi) orqali yuritiladi —
        // ClassName narxi bo'yicha qayta hisoblamaymiz (aks holda ikki manba bir-biriga zid summa yozadi).
        var hasMembership = await db.StudentGroups.AnyAsync(sg => sg.StudentId == student.Id);
        var cls = await db.Classes.Where(c => c.Name == student.ClassName).OrderBy(c => c.IsArchived).FirstOrDefaultAsync();
        // Agar className YANGI qiymatga o'zgardi va o'quvchining TIRIK a'zoligi yo'q bo'lsa — bu admin
        // o'quvchini guruhga qo'shmoqchi (StudentFormModal "Guruhga biriktirish" dropdown). Bunday
        // holatda M2M a'zolik (Status="trial") yaratamiz va billing QILINMAYDI — to'lov faqat
        // aktivlashtirish (ActivateMember) paytida boshlanishi kerak.
        // ⚠️ Ilgari faqat "umuman a'zoligi yo'q" o'quvchida ishlardi: guruhdan CHIQQAN o'quvchiga forma
        // tanlovni ko'rsatar, lekin a'zolik yaratilmas — faqat `ClassName` yorlig'i yozilib, o'quvchi
        // guruhda bordek ko'rinardi. Endi o'sha (o'quvchi, guruh) qatori bo'lsa `AddMember` dagidek tiklanadi.
        if (classChanged && !hasLiveMembership && cls is not null && !cls.IsArchived)
        {
            var today = AppClock.Today.ToString("yyyy-MM-dd");
            var joinedAt = student.EnrollmentDate.Length >= 10 ? student.EnrollmentDate : today;
            var old = await db.StudentGroups.FirstOrDefaultAsync(sg => sg.StudentId == student.Id && sg.GroupId == cls.Id);
            if (old is not null)
            {
                // Oldingi faol davr tarixga — sanalar tozalanishidan OLDIN (`membership-periods.md` §3).
                MembershipLifecycle.ClosePeriod(old, today);
                old.IsActive = true;
                old.LeftAt = null;
                old.JoinedAt = today;
                old.Status = "trial";
                old.ActivatedAt = string.Empty;
                old.FrozenAt = string.Empty;
                old.YearFreeze = false;
                old.RecordedAt = today;
            }
            else
                db.StudentGroups.Add(new StudentGroup
                {
                    StudentId = student.Id,
                    GroupId = cls.Id,
                    JoinedAt = joinedAt,
                    IsActive = true,
                    Status = "trial",
                    // Jurnaldagi avto-"keldi" qoidasi shu sanadan boshlanadi (JoinedAt orqaga
                    // sanalgan bo'lishi mumkin — eski darslar bo'sh qolishi kerak).
                    RecordedAt = today,
                });
            audit.Record("Membership", $"{cls.Id}:{student.Id}", "create",
                $"Guruhga qo'shildi (o'quvchi formasidan): {student.FullName} → {cls.Name} (sinov)"
                    + (old is not null ? " — qayta qo'shildi" : ""),
                studentId: student.Id);
            hasMembership = true; // billing blokini o'tkazib yuborish uchun
        }
        if (!hasMembership && cls is not null && cls.MonthlyFee > 0)
        {
            var current = TuitionService.CurrentMonth();
            var startMonth = string.IsNullOrEmpty(student.EnrollmentDate) || student.EnrollmentDate.Length < 7
                ? current
                : student.EnrollmentDate[..7];

            // Guruhsiz o'quvchi — faqat GroupId=null (ClassName-asosli) hisoblar bilan ishlaymiz.
            var existing = await db.MonthlyCharges
                .Where(c => c.StudentId == student.Id && c.GroupId == null)
                .ToDictionaryAsync(c => c.Month, c => c);

            // Chegirma REGISTRDAN — halqadan OLDIN bir marta (`.claude/rules/discounts.md`).
            var discountBook = await DiscountBook.LoadForStudentAsync(db, student.Id);

            foreach (var month in TuitionService.MonthRange(startMonth, current))
            {
                // Chegirma faqat amal qilish davrida qo'llanadi — har oy alohida. Guruhsiz
                // (GroupId=null) hisobga faqat «barcha guruhlar» chegirmasi tushadi.
                var monthDiscount = discountBook.DiscountFor(student.Id, cls.MonthlyFee, month, null);
                var monthEffective = cls.MonthlyFee - monthDiscount;
                if (existing.TryGetValue(month, out var charge))
                {
                    if (charge.Locked) continue; // qo'lda tahrirlangan — tegmaymiz.
                    // Faqat JORIY oyni va faqat GURUH o'zgarsa qayta hisoblaymiz (o'tgan oylar tarixiy).
                    var recompute = month == current && classChanged;
                    if (recompute && (charge.Amount != cls.MonthlyFee || charge.Discount != monthDiscount))
                    {
                        var delta = monthEffective - (charge.Amount - charge.Discount);
                        charge.Amount = cls.MonthlyFee;
                        charge.Discount = monthDiscount;
                        student.Balance -= delta;   // delta > 0 → ko'proq to'lash → balans kamayadi
                        applied = true;
                    }
                }
                else
                {
                    // Hisob yo'q edi — yangi guruh narxida yaratamiz (guruhsiz qo'shilgan o'quvchi holati).
                    db.MonthlyCharges.Add(new MonthlyCharge
                    {
                        StudentId = student.Id,
                        GroupId = null, // guruhsiz (ClassName-asosli) hisob
                        Month = month,
                        Amount = cls.MonthlyFee,
                        Discount = monthDiscount,
                        Date = $"{month}-01",
                    });
                    student.Balance -= monthEffective;
                    applied = true;
                }
            }
        }

        // ⚠️ Chegirmaning joriy oyga qayta qo'llanishi (`?applyDiscount=`) va registrni
        // sinxronlash (`SyncFromStudentAsync`) BU YERDAN OLIB TASHLANDI: chegirma bu yo'ldan
        // umuman o'zgarmaydi. Ikkalasi ham endi `StudentDiscountsController` da —
        // `ReapplyCurrentMonthAsync` va `RefreshMirrorAsync`.

        // Audit — guruh o'zgarishi (chegirmaniki registr yo'lida yoziladi).
        if (classChanged)
        {
            var summary = $"O'quvchi yangilandi (guruh: {oldClassName} → {student.ClassName})"
                + (applied ? " — joriy oy hisobi yangi summaga to'g'rilandi" : " — keyingi oydan amal qiladi");
            audit.Record(AuditService.EntityStudentDiscount, student.Id, "update", $"{summary} ({student.FullName})",
                before: new { Class = oldClassName },
                after: new { Class = student.ClassName },
                studentId: student.Id);
        }

        // Profil maydonlari o'zgarganda — "kim o'zgartirdi" audit izi (guruh/chegirma moliyaviy audit'dan alohida).
        var afterProfile = AuditService.StudentProfileSnapshot(student);
        if (System.Text.Json.JsonSerializer.Serialize(beforeProfile) != System.Text.Json.JsonSerializer.Serialize(afterProfile))
            audit.Record(AuditService.EntityStudent, student.Id, "update",
                $"O'quvchi ma'lumoti tahrirlandi ({student.FullName})",
                before: beforeProfile, after: afterProfile, studentId: student.Id);

        // TELEGRAM: ota-ona (yoki o'quvchi) raqami tuzatilgan bo'lsa ESKI raqam bilan ulangan chat
        // endi qarz/davomat xabarlarini olmasin va kirish kodini so'ray olmasin (`TelegramLinks`).
        if (TelegramLinks.PhonesChanged(phonesBefore, student))
            await TelegramLinks.PruneStaleAsync(db, audit, student, "raqam o'zgardi");

        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- IZOHLAR (o'quvchi profilidagi erkin eslatmalar) ----------

    /// <summary>
    /// "IZOHLARGA JAVOBLAR" — izoh YOZILGAN o'quvchilar ro'yxati (kimga, nechta, oxirgisi qachon
    /// va nima deb yozilgan). O'quvchilar bo'limidagi alohida sahifa shu endpointdan to'ladi.
    ///
    /// <para>⚠️ <c>ReadRequiresPerm = true</c> — METOD darajasida, sinf darajasidagi
    /// <c>[AdminPerm("students.list")]</c> ustiga. Sabab: bitta o'quvchining izohlari uning profilida
    /// ko'rinadi, bu yerda esa BUTUN markazning izohlari BIR ro'yxatda — ichida ota-ona bilan
    /// suhbat, to'lov kelishuvi, sog'liq kabi shaxsiy eslatmalar bo'ladi. Bunday jamlanma
    /// "bo'limlararo o'qish" uchun kerak emas (uploads-security.md dagi bir xil mantiq).</para>
    /// </summary>
    /// <param name="q">O'quvchi ismi YOKI izoh matni ichidan qidiruv.</param>
    /// <param name="from">Izoh yozilgan sana "yyyy-MM-dd" dan.</param>
    /// <param name="to">Izoh yozilgan sana "yyyy-MM-dd" gacha (kun oxirigacha — server o'zi cho'zadi).</param>
    [HttpGet("notes/overview")]
    [AdminPerm("students.list", "students.notes", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<StudentNoteOverviewDto>>> NotesOverview(
        [FromQuery] string? q, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int limit = StudentNoteService.DefaultLimit, CancellationToken ct = default) =>
        await StudentNoteService.OverviewAsync(db, q, from, to, limit, ct);

    /// <summary>
    /// Oylik kalendar kataklaridagi sonlar: shu oyning qaysi kunida nechta izoh yozilgan.
    /// Tanlangan davrga BOG'LIQ EMAS — bitta kun tanlanganda ham oy to'liq ko'rinib tursin.
    /// </summary>
    [HttpGet("notes/days")]
    [AdminPerm("students.list", "students.notes", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<StudentNoteDayDto>>> NoteDays(
        [FromQuery] string? month, CancellationToken ct = default) =>
        await StudentNoteService.DaysAsync(db, month, ct);

    /// <summary>O'quvchining izohlari — yangisi tepada. CanDelete/CanEdit: o'z izohi yoki superadmin.</summary>
    [HttpGet("{id}/notes")]
    public async Task<ActionResult<IEnumerable<StudentNoteDto>>> Notes(string id)
    {
        var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        var isSuper = User.IsInRole(Roles.SuperAdmin);
        return await db.StudentNotes.AsNoTracking()
            .Where(n => n.StudentId == id)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new StudentNoteDto(
                n.Id, n.Text, n.AuthorName, n.CreatedAt,
                isSuper || (n.AuthorId != "" && n.AuthorId == uid),
                isSuper || (n.AuthorId != "" && n.AuthorId == uid),
                n.EditedAt))
            .ToListAsync();
    }

    /// <summary>O'quvchiga yangi izoh qo'shish (yozgan xodim va vaqti bilan saqlanadi).</summary>
    [HttpPost("{id}/notes")]
    // Izoh IKKI sahifadan yoziladi: o'quvchi PROFILI va "Izohlarga javoblar" ro'yxati —
    // shuning uchun ikkala sahifa kaliti ham qabul qilinadi (bo'lim ruxsati esa ikkalasini
    // ham qamrab oladi, ya'ni eski xodimlar uchun hech narsa o'zgarmaydi).
    [AdminPerm("students.list", "students.notes")]
    public async Task<ActionResult<StudentNoteDto>> AddNote(string id, AddStudentNoteRequest req)
    {
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "Izoh bo'sh bo'lmasin" });
        if (await db.Students.FindAsync(id) is null) return NotFound();

        var note = new StudentNote
        {
            StudentId = id,
            Text = text,
            AuthorName = User.Identity?.Name
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin",
            AuthorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.StudentNotes.Add(note);
        await db.SaveChangesAsync();
        return new StudentNoteDto(note.Id, note.Text, note.AuthorName, note.CreatedAt, true, true, null);
    }

    /// <summary>Izoh MATNINI tahrirlash — faqat muallifi yoki superadmin (o'chirish bilan bir xil qoida).
    /// Muallif va yozilgan vaqt O'ZGARMAYDI; <c>EditedAt</c> yoziladi va ro'yxatda "(tahrirlangan)"
    /// bo'lib ko'rinadi (izoh — tarix, jimgina qayta yozilmasin).</summary>
    [HttpPut("notes/{noteId}")]
    // Izoh IKKI sahifadan yoziladi: o'quvchi PROFILI va "Izohlarga javoblar" ro'yxati —
    // shuning uchun ikkala sahifa kaliti ham qabul qilinadi (bo'lim ruxsati esa ikkalasini
    // ham qamrab oladi, ya'ni eski xodimlar uchun hech narsa o'zgarmaydi).
    [AdminPerm("students.list", "students.notes")]
    public async Task<ActionResult<StudentNoteDto>> EditNote(string noteId, EditStudentNoteRequest req)
    {
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "Izoh bo'sh bo'lmasin" });

        var note = await db.StudentNotes.FindAsync(noteId);
        if (note is null) return NotFound();
        var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!User.IsInRole(Roles.SuperAdmin) && (note.AuthorId.Length == 0 || note.AuthorId != uid))
            return Forbid();

        // Matn o'zgarmagan bo'lsa "(tahrirlangan)" belgisini qo'ymaymiz (bekorga tegmasin).
        if (note.Text != text)
        {
            note.Text = text;
            note.EditedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            await db.SaveChangesAsync();
        }
        return new StudentNoteDto(note.Id, note.Text, note.AuthorName, note.CreatedAt, true, true, note.EditedAt);
    }

    /// <summary>Izohni o'chirish — faqat muallifi yoki superadmin (tarix o'zgarmasligi uchun).</summary>
    [HttpDelete("notes/{noteId}")]
    // Izoh IKKI sahifadan yoziladi: o'quvchi PROFILI va "Izohlarga javoblar" ro'yxati —
    // shuning uchun ikkala sahifa kaliti ham qabul qilinadi (bo'lim ruxsati esa ikkalasini
    // ham qamrab oladi, ya'ni eski xodimlar uchun hech narsa o'zgarmaydi).
    [AdminPerm("students.list", "students.notes")]
    public async Task<IActionResult> DeleteNote(string noteId)
    {
        var note = await db.StudentNotes.FindAsync(noteId);
        if (note is null) return NotFound();
        var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!User.IsInRole(Roles.SuperAdmin) && (note.AuthorId.Length == 0 || note.AuthorId != uid))
            return Forbid();
        db.StudentNotes.Remove(note);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? reasonId = null)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        var reason = string.IsNullOrWhiteSpace(reasonId) ? "" : (await db.ActionReasons.Where(r => r.Id == reasonId).Select(r => r.Label).FirstOrDefaultAsync() ?? "");
        // Bog'liq qatorlar orphan qolmasligi uchun ularni ham olib tashlaymiz: oylik hisob, guruh a'zoliklari,
        // jurnal yozuvlari. (Moliya yozuvlari audit uchun saqlanadi.)
        db.MonthlyCharges.RemoveRange(db.MonthlyCharges.Where(c => c.StudentId == id));
        db.StudentGroups.RemoveRange(db.StudentGroups.Where(sg => sg.StudentId == id));
        db.StudentNotes.RemoveRange(db.StudentNotes.Where(n => n.StudentId == id));
        db.JournalEntries.RemoveRange(db.JournalEntries.Where(e => e.StudentId == id));
        // Biriktirilgan tizim akkaunti + qurilma tokenlarini ham o'chiramiz.
        if (student.UserId is not null)
        {
            db.DeviceTokens.RemoveRange(db.DeviceTokens.Where(d => d.UserId == student.UserId));
            var user = await db.Users.FindAsync(student.UserId);
            if (user is not null) db.Users.Remove(user);
        }
        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
        ArchiveService.Snapshot(db, "student", student.Id, student.FullName,
            student.Phone ?? student.ClassName ?? "", student, reason.Length > 0 ? reason : null, actor);
        // "Kim, qachon o'chirdi" — ilgari bu amal tarixda IZ QOLDIRMASDI. `studentId` ATAYIN beriladi:
        // o'quvchi «Arxiv (barcha turdagi)» dan tiklansa, bu qator uning tarixida ko'rinadi.
        audit.Record(AuditService.EntityStudent, student.Id, "delete",
            $"O'quvchi o'chirildi (o'chirilganlar arxiviga ko'chirildi): {student.FullName}"
                + (reason.Length > 0 ? $" — sabab: {reason}" : ""),
            before: AuditService.StudentProfileSnapshot(student), studentId: student.Id);
        db.Students.Remove(student);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* ---------- Arxiv ---------- */

    /// <summary>
    /// O'QUVCHI RASMINI (profil surati) o'rnatish yoki o'chirish.
    ///
    /// <para>Nega alohida endpoint: rasm o'quvchi sahifasidagi DUMALOQ avatarni bosib ham
    /// (kameradan yoki fayldan) yuklanadi. U yerda to'liq <see cref="StudentPayload"/> yo'q —
    /// to'liq <c>PUT</c> yuborilsa boshqa maydonlar (chegirma, ota-ona, manzil...) tasodifan
    /// bo'shab qolishi mumkin edi. Shu sabab faqat bitta maydonga tegadigan yengil yo'l.</para>
    ///
    /// <para>DIQQAT: rasm ANIQ shu ustunda — <see cref="Student.BirthCertificateUrl"/>. Nomi
    /// eski (ilgari tug'ilganlik guvohnomasi edi), lekin butun tizim uni RASM deb ishlatadi:
    /// admin formasidagi yorlig'i "O'quvchi rasmi", o'quvchi ilovasiga esa
    /// <c>StudentProfileDto.PhotoUrl</c> bo'lib chiqadi. Ikkinchi ustun ochilmadi — aks holda
    /// ikkita "rasm" manbai paydo bo'lib, qaysi biri ko'rsatilishi chalkashardi.</para>
    /// </summary>
    [HttpPut("{id}/photo")]
    public async Task<IActionResult> SetPhoto(string id, StudentPhotoRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        var url = (req.PhotoUrl ?? "").Trim();
        // Faqat serverning o'z yuklamalari qabul qilinadi (tashqi havola qo'yib bo'lmasin).
        if (url.Length > 0 && !url.StartsWith("/uploads/", StringComparison.Ordinal))
            return BadRequest(new { message = "Rasm manzili noto'g'ri" });
        // Kengaytma ham tekshiriladi: ilgari `/uploads/` bilan boshlangan HAR QANDAY fayl (masalan
        // `.pdf` skan) avatar bo'lib qo'yilaverardi. Ro'yxat sabablari — `UploadGuard.PhotoExtensions`.
        if (url.Length > 0 && !Application.Services.UploadGuard.IsPhotoUrl(url))
            return BadRequest(new { message = "Surat faqat JPG yoki PNG bo'lishi kerak (sertifikatda ham shu turlar chiqadi)" });

        student.BirthCertificateUrl = url.Length == 0 ? null : url;
        audit.Record(AuditService.EntityStudent, id, "update",
            url.Length == 0 ? "O'quvchi rasmi o'chirildi" : "O'quvchi rasmi yangilandi",
            studentId: id);
        await db.SaveChangesAsync();
        return Ok(new { photoUrl = student.BirthCertificateUrl });
    }

    /// <summary>
    /// O'quvchini arxivga ko'chirish = O'QUVCHI KETDI: <c>IsArchived=true</c>, sana va sabab yoziladi,
    /// akkaunt login bloklanadi (PasswordHash bo'shaltiriladi) va HAR tirik a'zolik «Guruhdan chiqarish»
    /// bilan aynan bir xil yopiladi (<see cref="StudentArchiving.LeaveAllGroupsAsync"/>: hisob, sabab
    /// yozuvi, a'zolik audit qatori, «asosiy guruh» yorlig'i). Tarixiy ma'lumotlar saqlanadi.
    /// ReasonId berilsa, ActionReason.Label yoziladi; aks holda Reason suz berilsa yoziladi.
    ///
    /// <para>⚠️ Ilgari a'zoliklar TIRIK qolardi: arxivdagi o'quvchi jurnalda, guruh SMS'larida va o'rin
    /// sanog'ida turar, arxivdan qaytarilganda esa arxivda o'tgan har oy birdaniga hisoblanib, har oy
    /// uchun SMS ketardi (audit C1).</para>
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id, ArchiveStudentRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        if (student.IsArchived)
            return BadRequest(new { message = "O'quvchi allaqachon arxivda" });

        // Bir o'quvchining pul amallari bir navbatda (RemoveMember bilan bir xil qulf) — balans qulf
        // ICHIDA qayta o'qiladi (parallel to'lov ta'siri eski qiymat ustidan yozilmasin).
        using var gate = await PaymentIdempotency.LockAsync("student:" + id);
        await StudentBalanceSync.ReloadAsync(db, student);

        var today = AppClock.Today.ToString("yyyy-MM-dd");

        // Sabab: ReasonId bo'lsa undan Label, aks holda Reason suz
        string? reasonId = null;
        if (!string.IsNullOrWhiteSpace(req.ReasonId))
        {
            var reason = await db.ActionReasons.FindAsync(req.ReasonId);
            reasonId = reason?.Id;
            student.ArchiveReason = reason?.Label ?? (req.Reason ?? "").Trim();
        }
        else
        {
            student.ArchiveReason = (req.Reason ?? "").Trim();
        }

        // ARXIV = KETDI: a'zoliklar arxiv sanasida yopiladi (o'quvchi qulfi ostida — pul o'zgaradi).
        var leave = await StudentArchiving.LeaveAllGroupsAsync(db, audit, student, today,
            reasonId, student.ArchiveReason, "o'quvchi arxivlandi");

        student.IsArchived = true;
        student.ArchivedAt = today;

        // Login bloklash — PasswordHash bo'shaltiriladi (login imkonsiz bo'ladi).
        if (student.UserId is not null)
        {
            var user = await db.Users.FindAsync(student.UserId);
            if (user is not null)
            {
                user.BlockLogin();
                // Ochiq sessiyalar ham yopiladi: aks holda tiklangandan keyin arxivdan OLDINGI
                // refresh tokenlar yana ishlab ketardi (`auth-sessions.md`).
                await RefreshTokenService.RevokeAllForUserAsync(db, user.Id);
            }
        }

        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"O'quvchi arxivga ko'chirildi ({student.FullName})"
                + (leave.Closed > 0 ? $" — {leave.Closed} ta guruhdan chiqarildi: {string.Join(", ", leave.GroupNames)}" : "")
                + (string.IsNullOrWhiteSpace(student.ArchiveReason) ? "" : $": \"{student.ArchiveReason}\""),
            studentId: student.Id);

        await db.SaveChangesAsync();
        return Ok(new { closedMemberships = leave.Closed, restored = leave.Restored, groups = leave.GroupNames });
    }

    /// <summary>
    /// Arxivdan qaytarish: <c>IsArchived=false</c>, arxiv maydonlari tozalanadi. Ixtiyoriy
    /// <c>NewPassword</c> berilsa akkauntga yangi parol o'rnatiladi (aks holda parol bloklangicha qoladi).
    ///
    /// <para>⚠️ A'ZOLIKLAR TIKLANMAYDI — o'quvchi GURUHSIZ qaytadi (arxivlashda u guruhlardan chiqqan),
    /// admin kerakli guruhga qo'shadi; javobdagi <c>message</c> buni aytadi. ESKI arxivdagi o'quvchida
    /// (bu tuzatishdan oldin arxivlangan) a'zoliklar hamon tirik — ular avval ARXIV SANASIDA yopiladi
    /// (<see cref="StudentArchiving.CloseLegacyMembershipsAsync"/>), aks holda arxivda o'tgan har oy
    /// qaytarilishi bilanoq hisoblanib, ota-onaga har oy uchun SMS ketardi.</para>
    /// </summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id, RestoreStudentRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        if (!student.IsArchived)
            return BadRequest(new { message = "O'quvchi arxivda emas" });

        var newPwd = (req?.NewPassword ?? "").Trim();
        // Parol o'rnatish = akkauntga kirish — `ResetPassword`/`Credentials` bilan BIR XIL darvoza
        // (auth-sessions.md §3). HECH NARSA o'zgartirilishidan OLDIN. ⚠️ Ilgari faqat sinf darajasidagi
        // `students.list:create` (POST) yetardi: parolni KO'RA olmaydigan xodim arxivdagi o'quvchini
        // O'ZI tanlagan parol bilan qaytarib, uning nomidan kirardi. Parolsiz qaytarish — avvalgidek.
        if (newPwd.Length > 0 && !AdminPermAttribute.HasFullAccess(User, "students.list"))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = PasswordForbiddenMessage });
        if (newPwd.Length > 0 && newPwd.Length < MinPasswordLength)
            return BadRequest(new { message = WeakPasswordMessage });

        using var gate = await PaymentIdempotency.LockAsync("student:" + id);
        await StudentBalanceSync.ReloadAsync(db, student);

        // ESKI ARXIV: tirik a'zoliklar arxiv sanasida yopiladi (yangi arxivda ular yo'q — hech narsa
        // qilinmaydi). `IsArchived` hali true — yopish uni bilishi shart emas, lekin sana undan olinadi.
        var legacy = await StudentArchiving.CloseLegacyMembershipsAsync(db, audit, student);

        student.IsArchived = false;
        student.ArchivedAt = null;
        student.ArchiveReason = null;
        student.ArchivedWithClass = false;

        if (!string.IsNullOrEmpty(newPwd) && student.UserId is not null)
        {
            var user = await db.Users.FindAsync(student.UserId);
            if (user is null)
            {
                user = AccountFactory.CreateAccountFor(db, "student", student.FullName);
                student.UserId = user.Id;
            }
            user.SetInitialPassword(newPwd);
        }
        // Arxivdan OLDINGI sessiyalar (30 kunlik sirpanuvchi refresh) qayta TIRILMASIN (R2-6) — yangi parol
        // berilsa ham, berilmasa ham (eski arxivlashda sessiyalar bekor qilinmagan bo'lishi mumkin).
        if (student.UserId is not null) await RefreshTokenService.RevokeAllForUserAsync(db, student.UserId);

        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"O'quvchi arxivdan qaytarildi ({student.FullName}) — guruhsiz"
                + (legacy.Closed > 0 ? $"; eski arxivdagi {legacy.Closed} ta a'zolik arxiv sanasida yopildi" : ""),
            studentId: student.Id);

        await db.SaveChangesAsync();
        return Ok(new { message = StudentArchiving.RestoredWithoutGroupsMessage, closedLegacy = legacy.Closed });
    }

    // ---------- TELEGRAM ULANISHLARI (ota-ona boti) — `TelegramLinks` ----------

    /// <summary>
    /// O'quvchiga bog'langan Telegram chatlari (bot xabarlari va kirish kodi shu chatlarga ketadi).
    /// <c>phoneMatches=false</c> — ulanish raqami o'quvchining joriy raqamlaridan hech biriga mos emas
    /// (eskirgan, uzish tavsiya etiladi). <c>ReadRequiresPerm</c> — ota-onaning Telegram ismi va raqami
    /// faqat o'quvchilar bo'limi ruxsati bor xodimga.
    /// </summary>
    [HttpGet("{id}/telegram-links")]
    [AdminPerm("students.list", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<TelegramLinks.LinkDto>>> TelegramLinksList(string id)
    {
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
        return await TelegramLinks.ListAsync(db, s);
    }

    /// <summary>
    /// Telegram ulanishini QO'LDA uzish (masalan telefon begona odamga o'tgan). Shu chat endi bu o'quvchi
    /// bo'yicha xabar olmaydi va kirish kodini so'ray olmaydi; ota-ona qayta ulanishi uchun botga to'g'ri
    /// raqamni yuboradi. Ruxsat — sinf darajasidagi <c>students.list</c> (DELETE → o'chirish amali).
    /// </summary>
    [HttpDelete("{id}/telegram-links/{regId}")]
    public async Task<IActionResult> UnlinkTelegram(string id, string regId)
    {
        var s = await db.Students.FindAsync(id);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var reg = await db.TelegramRegistrations.FirstOrDefaultAsync(r => r.Id == regId && r.StudentId == id);
        if (reg is null) return NotFound(new { message = "Ulanish topilmadi — allaqachon uzilgan bo'lishi mumkin" });
        db.TelegramRegistrations.Remove(reg);
        audit.Record(AuditService.EntityStudent, id, "update",
            $"Telegram ulanishi qo'lda uzildi: {TelegramLinks.Describe(reg)} ({s.FullName})",
            studentId: id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// O'quvchi login'ini vaqtincha cheklaydi/qayta ochadi (arxivlashdan farqli — o'quvchi faol
    /// ro'yxatda qoladi, faqat tizimga kira olmaydi). Akkauntga (UserId) bog'liq emas — Student.LoginBlocked
    /// maydoni orqali ishlaydi, shuning uchun akkaunt bo'lmasa ham amal qiladi.
    /// </summary>
    [HttpPut("{id}/login-block")]
    public async Task<IActionResult> SetLoginBlock(string id, StudentLoginBlockRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        if (student.LoginBlocked != req.Blocked)
        {
            student.LoginBlocked = req.Blocked;
            audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
                (req.Blocked ? "O'quvchi login'i cheklandi" : "O'quvchi login'i qayta ochildi")
                    + $" ({student.FullName})",
                studentId: student.Id);
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>
    /// Bir nechta o'quvchi login'ini birdaniga cheklaydi/qayta ochadi (jadvalda ko'p tanlash).
    /// Faqat holati o'zgargan o'quvchilar uchun audit yozuvi qo'shiladi, bitta SaveChangesAsync.
    /// </summary>
    [HttpPut("login-block-bulk")]
    public async Task<IActionResult> SetLoginBlockBulk(StudentLoginBlockBulkRequest req)
    {
        if (req.StudentIds is null || req.StudentIds.Count == 0)
            return BadRequest(new { message = "O'quvchilar tanlanmagan" });

        var students = await db.Students.Where(s => req.StudentIds.Contains(s.Id)).ToListAsync();

        var changed = 0;
        foreach (var student in students)
        {
            if (student.LoginBlocked != req.Blocked)
            {
                student.LoginBlocked = req.Blocked;
                audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
                    (req.Blocked ? "O'quvchi login'i cheklandi" : "O'quvchi login'i qayta ochildi")
                        + $" ({student.FullName})",
                    studentId: student.Id);
                changed++;
            }
        }

        if (changed > 0) await db.SaveChangesAsync();

        return Ok(new { changed });
    }

    /// <summary>O'quvchining tizim akkaunti (login/parol). Akkaunt yo'q bo'lsa — yaratib biriktiradi.
    /// <para>GET odatda xodim uchun ochiq bo'lsa-da (bo'limlararo o'qish uchun), bu endpoint
    /// LOGIN va DASTLABKI PAROLNI qaytargani (va akkaunt yaratib DB'ni o'zgartirgani) uchun
    /// MAXSUS tekshiriladi — faqat superadmin/admin yoki "O'quvchilar" bo'limiga TO'LIQ
    /// ruxsati bor xodim. Aks holda bo'lim ruxsati yo'q xodim ham parolni o'qiy olardi.</para></summary>
    [HttpGet("{id}/credentials")]
    public async Task<ActionResult<CredentialsDto>> Credentials(string id)
    {
        if (!AdminPermAttribute.HasFullAccess(User, "students.list")) return Forbid();
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        var user = student.UserId is null ? null : await db.Users.FindAsync(student.UserId);
        if (user is null)
        {
            user = AccountFactory.CreateAccountFor(db, "student", student.FullName);
            student.UserId = user.Id;
            await db.SaveChangesAsync();
        }

        // Foydalanuvchi hali kirmagan bo'lsa dastlabki parol ko'rsatiladi; kirgach bo'sh (faqat reset-password).
        return new CredentialsDto(user.Email, user.InitialPassword ?? "", user.Role);
    }

    /// <summary>Parolni o'rnatish/qayta yaratish huquqi yo'q xodimga (403) ko'rsatiladigan sabab.</summary>
    private const string PasswordForbiddenMessage =
        "Parol o'rnatish uchun «O'quvchilar» bo'limiga to'liq ruxsat kerak";

    /// <summary>O'quvchiga yangi tasodifiy parol generatsiya qiladi va qaytaradi.
    /// <para>⚠️ Darvoza <see cref="Credentials"/> bilan BIR XIL (<c>HasFullAccess</c>): yangi parol
    /// QAYTARILADI, ya'ni bu o'quvchi akkauntiga kirish. Ilgari faqat sinf darajasidagi
    /// <c>students.list:create</c> yetardi — parolni KO'RISH huquqi yo'q xodim uni qayta yaratib
    /// olardi (xodimlardagi `StaffController.ResetPassword` bilan bir xil sabab).</para>
    /// <para>Yangi parol <c>InitialPassword</c> ga ham yoziladi (<c>SetInitialPassword</c>) — aks holda
    /// «Login/parol», Excel eksport va botdagi login xabari ESKI (yaroqsiz) parolni ko'rsatardi.
    /// Eski sessiyalar tugatiladi (yo'qolgan telefon / parolni bilib olgan odam chiqib ketadi).</para></summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<CredentialsDto>> ResetPassword(string id)
    {
        if (!AdminPermAttribute.HasFullAccess(User, "students.list"))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = PasswordForbiddenMessage });
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        var user = student.UserId is null ? null : await db.Users.FindAsync(student.UserId);
        if (user is null)
        {
            user = AccountFactory.CreateAccountFor(db, "student", student.FullName);
            student.UserId = user.Id;
        }
        var pwd = AccountFactory.GeneratePassword();
        // Parolning O'ZI hech qachon tarixga yozilmaydi — faqat "almashtirildi" fakti
        // (o'qituvchi/xodimdagi qoida bilan bir xil). Tur — login amallari bilan bir joyda.
        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"O'quvchi paroli qayta yaratildi ({student.FullName})", studentId: student.Id);
        user.SetInitialPassword(pwd);
        await RefreshTokenService.RevokeAllForUserAsync(db, user.Id);
        await db.SaveChangesAsync();
        return new CredentialsDto(user.Email, pwd, user.Role);
    }

    /// <summary>
    /// Barcha (faol) o'quvchilarni login/parol bilan Excel (.xlsx) ga eksport qiladi.
    /// Parol FAQAT foydalanuvchi hali kirmagan bo'lsa ko'rinadi (kirgach bo'sh). GET odatda xodim uchun
    /// ham ochiq bo'lsa-da, bu amal ommaviy parol dumpi bo'lgani uchun MAXSUS tekshiriladi — faqat
    /// superadmin/admin yoki "O'quvchilar" bo'limiga TO'LIQ (barcha 4 amal) ruxsati bor xodim.
    /// Ustunlar: F.I.SH., Guruh, Ota-ona, Telefon, Login, Parol.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        if (!AdminPermAttribute.HasFullAccess(User, "students.list")) return Forbid();
        var students = await db.Students.Where(s => !s.IsArchived)
            .OrderBy(s => s.ClassName).ThenBy(s => s.FullName).ToListAsync();
        var userIds = students.Where(s => s.UserId != null).Select(s => s.UserId!).ToList();
        var byId = (await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id);

        var headers = new[] { "F.I.SH.", "Guruh", "Ota-ona", "Telefon", "Login", "Parol" };
        var rows = students.Select(s =>
        {
            byId.TryGetValue(s.UserId ?? "", out var u);
            return (IReadOnlyList<string>)new[]
            {
                s.FullName, s.ClassName, s.ParentFullName, s.ParentPhone,
                u?.Email ?? "", u?.InitialPassword ?? "",
            };
        });

        var bytes = ExcelExport.Build("O'quvchilar", headers, rows);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"oquvchilar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// TANLANGAN o'quvchilarning to'liq ma'lumotlarini Excel (.xlsx) ga eksport qiladi
    /// (profil + faol guruhlar + balans + login). Parol ustuni faqat superadmin uchun
    /// (va foydalanuvchi hali kirmagan bo'lsagina ko'rinadi).
    /// </summary>
    [HttpPost("export-selected")]
    public async Task<IActionResult> ExportSelected(StudentExportSelectedRequest req)
    {
        if (req.StudentIds is null || req.StudentIds.Count == 0)
            return BadRequest(new { message = "O'quvchilar tanlanmagan" });

        var students = await db.Students
            .Where(s => req.StudentIds.Contains(s.Id))
            .OrderBy(s => s.FullName).ToListAsync();
        if (students.Count == 0) return BadRequest(new { message = "O'quvchilar topilmadi" });

        var ids = students.Select(s => s.Id).ToList();
        var memberships = await (from sg in db.StudentGroups
                                 join c in db.Classes on sg.GroupId equals c.Id
                                 where sg.IsActive && ids.Contains(sg.StudentId)
                                 select new { sg.StudentId, c.Name, sg.Status }).ToListAsync();
        var groupsByStudent = memberships
            .GroupBy(m => m.StudentId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Status switch
                    {
                        "trial" => $"{x.Name} (sinov)",
                        "frozen" => $"{x.Name} (muzlatilgan)",
                        _ => x.Name,
                    })));

        var userIds = students.Where(s => s.UserId != null).Select(s => s.UserId!).ToList();
        var usersById = (await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id);
        var isSuper = User.IsInRole(Roles.SuperAdmin);

        var headers = new List<string>
        {
            "F.I.SH.", "Tug'ilgan sana", "Jinsi", "Telefon", "Manzil", "Guruhlar",
            "Ota F.I.SH.", "Ota tel", "Ona F.I.SH.", "Ona tel", "Balans", "Login",
        };
        if (isSuper) headers.Add("Parol");

        var rows = students.Select(s =>
        {
            usersById.TryGetValue(s.UserId ?? "", out var u);
            var row = new List<string>
            {
                s.FullName,
                s.BirthDate,
                StudentGender.ExportLabel(s.Gender),
                s.Phone,
                s.Address,
                groupsByStudent.GetValueOrDefault(s.Id, ""),
                s.FatherFullName,
                s.FatherPhone,
                s.MotherFullName,
                s.MotherPhone,
                s.Balance.ToString("0.##", CultureInfo.InvariantCulture),
                u?.Email ?? "",
            };
            if (isSuper) row.Add(u?.InitialPassword ?? "");
            return (IReadOnlyList<string>)row;
        });

        var bytes = ExcelExport.Build("O'quvchilar", headers, rows);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"oquvchilar_tanlangan_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /* ---------- Excel'dan ommaviy import ---------- */

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Import shabloni ustunlari (1-varaq). Import ustunlarni SARLAVHA bo'yicha topadi
    // (`StudentImportPlan.MapColumns`) — sarlavha matni `HeaderField` taniydigan so'z bilan boshlansin.
    // ⚠️ «Jinsi» ustuni 2026-10-09 da OLIB TASHLANDI (jins ixtiyoriy) — eski shablon bilan to'ldirilgan
    // fayllardagi «Jinsi» va «Sinf» baribir o'qiladi (sarlavha bo'yicha).
    private static readonly string[] ImportHeaders =
    {
        "F.I.SH (o'quvchi)*", "Telefon", "Ota F.I.SH", "Ota tel", "Ona F.I.SH", "Ona tel",
        "Guruh", "Holat", "Tug'ilgan sana (YYYY-MM-DD)", "Sinf (1-11 yoki bitirgan)",
    };

    /// <summary>Importda o'qiladigan ustunlar soni — eski (11 ustunli) va qo'shimcha ustunli fayllar ham sig'sin.</summary>
    private const int ImportReadColumns = 30;

    /// <summary>
    /// Guruh a'zoligini yaratadi — yangi o'quvchi (<see cref="AddStudent"/>) va Excel importida
    /// MAVJUD o'quvchini guruhga qo'shish uchun BITTA qoida (holat, sanalar, orqaga sanani qirqish).
    /// SaveChanges QILMAYDI. «active» a'zolikning qo'shilgan oyi hisobi — <see cref="ChargeJoinMonthAsync"/>
    /// (chaqiruvchi, async).
    /// </summary>
    private StudentGroup AddMembershipRow(string studentId, Group cls, string status, string enrollment)
    {
        var memberStatus = NormalizeStatus(status);
        var row = new StudentGroup
        {
            StudentId = studentId,
            GroupId = cls.Id,
            JoinedAt = enrollment,
            // ⚠️ Muzlatilgan ham TIRIK a'zolik (`FreezeCoreAsync` dagidek `IsActive=true`). Ilgari `false`
            // yozilardi: chiqish sanasisiz "chiqqan" qator bo'lib, «Aktiv emas» da chiqar va aktivlashtirib
            // bo'lmasdi (server faqat faol a'zolikni aktivlashtiradi).
            IsActive = true,
            Status = memberStatus,
            // ⚠️ HOLAT SANASIZ QOLMASIN. Ilgari bu yerda `ActivatedAt`/`FrozenAt` UMUMAN
            // yozilmasdi va qator ZID chiqardi: "active" (ya'ni pullik), lekin qachondan
            // ekani noma'lum. `TuitionService.AccruableMonth` haqiqiy sana talab qilgani
            // uchun bunday a'zolikka oylik HECH QACHON hisoblanmasdi, teglanmagan to'lov
            // taqsimoti (maosh, per-guruh balans, kurs moliyasi) esa uni "pullik" deb
            // olardi — bir guruhning puli ikkinchisining o'qituvchisiga bo'linib ketardi.
            // Endi sana = qabul sanasi: "active" tanlansa hisob shu sanadan yuritiladi
            // (qabul oyining O'ZI bu yerda hisoblanmaydi — qisman oylik «Aktivlashtirish» oynasida,
            // yoki shu oyga TO'LOV kelganda `TuitionService.EnsureChargeAsync` orqali yoziladi;
            // aks holda birinchi oy bepul qolardi), "frozen" tanlansa esa a'zolik boshidanoq to'xtatilgan.
            // ⚠️ 2026-10-09 dan Excel importi qabul oyining O'ZINI ham darhol hisoblaydi
            // (`ChargeJoinMonthAsync`) — mijoz: "aktiv qilib qo'shilganda balans 0 bo'lib ochilyapti".
            //
            // ⚠️ ORQAGA SANALGAN QABUL SANASI QIRQILADI (`MembershipLifecycle.ActivationStartForCreate`). Sabab:
            // `AccrueDue` har 12 soatda butun tarixni skanerlaydi, ya'ni eski `enrollment`
            // bilan OMMAVIY IMPORT qilinsa bir necha oylik qarz JIMGINA yozilib, ota-onalarga
            // avto-SMS to'lqini ketardi (`.claude/rules/membership-periods.md` §6 aynan shu
            // xavfdan qo'riqlaydi). Bu yo'l qisman oylik ham YOZMAYDI, ya'ni orqaga sanashning
            // to'g'ri natijasi baribir chiqmasdi. Haqiqatan o'tmishdan aktivlashtirish kerak
            // bo'lsa — «Aktivlashtirish» oynasi: u qisman oylikni yozadi va bo'shliqlarni
            // ONGLI ravishda (`AccrueCatchUpAsync`) to'ldiradi.
            ActivatedAt = memberStatus == "active"
                ? MembershipLifecycle.ActivationStartForCreate(enrollment, AppClock.Today)
                : string.Empty,
            FrozenAt = memberStatus == "frozen" ? enrollment : string.Empty,
            // RecordedAt — HAQIQIY bugungi sana (enrollment ORQAGA sanalgan bo'lishi mumkin).
            // Jurnalda undan OLDINGI, allaqachon davomati olingan darslar avto-"keldi" ✓
            // bo'lib to'lib qolmasin (ClassesController.AddMember bilan bir xil qoida).
            RecordedAt = AppClock.Today.ToString("yyyy-MM-dd"),
        };
        db.StudentGroups.Add(row);
        return row;
    }

    /// <summary>
    /// «active» bilan qo'shilgan a'zolikning QO'SHILGAN OYINI darhol hisoblaydi — «Aktivlashtirish» oynasidagi
    /// AYNAN o'sha qisman hisob (<see cref="TuitionService.ChargeActivationProrateAsync"/>: sanadan oy oxirigacha
    /// qolgan darslar, chegirma bilan; oyning birinchi darsidan — to'liq oylik). Sinov/muzlatilgan — hech narsa.
    /// <para>⚠️ Ilgari bu oy ATAYIN yozilmasdi (ommaviy importda yolg'on qarz/SMS to'lqini xavfi) va o'quvchi
    /// balansi 0 bo'lib qolardi — o'qiyotgan oyi tekin edi (mijoz shikoyati, 2026-10-09). Xavf endi yo'q:
    /// <c>ActivatedAt</c> joriy oy boshidan orqaga o'tmaydi (<see cref="MembershipLifecycle.ActivationStartForCreate"/>),
    /// ya'ni faqat BITTA — haqiqatan o'qilayotgan — oy yoziladi; avto-xabar yuborilmaydi.</para>
    /// SaveChanges QILMAYDI. Qaytaradi: yozilgan effektiv summa.
    /// </summary>
    private async Task<decimal> ChargeJoinMonthAsync(Student s, Group cls, StudentGroup sg, PricingBook pricing)
    {
        if (sg.Status != "active" || sg.ActivatedAt.Length < 10) return 0m;
        var before = s.Balance;
        await TuitionService.ChargeActivationProrateAsync(db, s, cls, sg.ActivatedAt[..10], pricing: pricing);
        var charged = before - s.Balance;
        if (charged > 0)
            audit.Record("Membership", $"{cls.Id}:{s.Id}", "update",
                $"Qo'shilgan oy hisoblandi ({sg.ActivatedAt[..10]} sanadan qolgan darslar): "
                + $"{AuditService.Money(charged)} so'm — {s.FullName}, {cls.Name}",
                studentId: s.Id);
        return charged;
    }

    /// <summary>Arxivdagi guruhga a'zo qo'shib bo'lmasligi sababi (yaratish 400, import ogohlantirishi).</summary>
    private static string ArchivedGroupMessage(Group cls) => $"«{cls.Name}» guruhi arxivda (yopilgan)";

    // Guruh bo'yicha status mapping (guruh berilgan bo'lsa status to'ldirish uchun).
    private static string NormalizeGroupStatus(Group? cls, string statusFromRow)
    {
        if (cls is null) return ""; // guruh yo'q — status ko'p muhim emas
        var normalized = NormalizeStatus(statusFromRow);
        return string.IsNullOrEmpty(normalized) ? "trial" : normalized;
    }

    /// <summary>
    /// O'quvchilarni ommaviy kiritish uchun bo'sh Excel shabloni (.xlsx). 1-varaq "O'quvchilar" —
    /// to'ldiriladigan sarlavhalar; 2-varaq "Yo'riqnoma" — maydonlar izohi va MAVJUD guruhlar ro'yxati.
    /// Import faqat 1-varaqni o'qiydi, shu sababli yo'riqnoma import'ga ta'sir qilmaydi.
    /// </summary>
    [HttpGet("import-template")]
    public async Task<IActionResult> ImportTemplate()
    {
        // Faqat ARXIVLANMAGAN guruhlar — arxivdagi (yopilgan) guruhga import o'quvchi qo'shmaydi.
        var groupNames = await db.Classes.Where(c => !c.IsArchived).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        var statuses = new[] { "trial", "active", "frozen" };

        var info = new List<IReadOnlyList<string>>
        {
            new[] { "F.I.SH (o'quvchi)*", "Majburiy. Familiya va ism, masalan: Aliyev Vali (otasining ismi yozilmaydi)" },
            new[] { "Telefon", "ixtiyoriy. O'quvchining o'z raqami" },
            new[] { "Ota F.I.SH", "ixtiyoriy. Masalan: Aliyev Ali" },
            new[] { "Ota tel", "ixtiyoriy. Masalan: +998901234567" },
            new[] { "Ona F.I.SH", "ixtiyoriy. Masalan: Aliyeva Nodira" },
            new[] { "Ona tel", "ixtiyoriy. Masalan: +998901234568" },
            new[] { "Guruh", "ixtiyoriy. Bo'sh bo'lsa, o'quvchi faqat yaratiladi; pastdagi ro'yxatdan aniq nom" },
            new[] { "Holat", "ixtiyoriy: trial, active yoki frozen (default: trial). Guruh bo'lsa qo'llaniladi" },
            new[] { "Tug'ilgan sana", "YYYY-MM-DD, masalan 2015-03-21 (ixtiyoriy)" },
            new[] { "Sinf", "ixtiyoriy. Maktabdagi sinf: 1 dan 11 gacha raqam (masalan 7) yoki «bitirgan». Har yili 1-sentabrda o'zi oshadi" },
            new[] { "", "" },
            new[] { "Takroriy o'quvchi", "Bazada F.I.SH va telefoni (o'quvchi/ota/ona) mos o'quvchi bo'lsa, yangisi yaratilmaydi — faqat ko'rsatilgan guruhga qo'shiladi. Bir o'quvchini bir nechta guruhga qo'shish uchun uni har guruh bilan alohida qatorda yozing" },
            new[] { "", "" },
            new[] { "Mavjud guruhlar:", groupNames.Count == 0 ? "(guruh yaratilmagan)" : "" },
        };
        info.AddRange(groupNames.Select(c => (IReadOnlyList<string>)new[] { c, "" }));
        info.Add(new[] { "", "" });
        info.Add(new[] { "Holat qiymatları:", "" });
        info.AddRange(statuses.Select(s => (IReadOnlyList<string>)new[] { s, "" }));

        var bytes = ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec("O'quvchilar", ImportHeaders, Array.Empty<IReadOnlyList<string>>()),
            new ExcelExport.SheetSpec("Yo'riqnoma", new[] { "Maydon", "Izoh" }, info),
        });
        return File(bytes, XlsxMime, "oquvchilar_shablon.xlsx");
    }

    /// <summary>
    /// To'ldirilgan Excel (.xlsx) shablonidan o'quvchilarni ommaviy yaratadi. Har qator alohida
    /// tekshiriladi: F.I.SH va Guruh majburiy, Guruh mavjud bo'lishi shart. To'g'ri qatorlar yaratiladi
    /// (akkaunt + oylik hisob bilan), xato qatorlar raqami/sababi bilan qaytariladi (qisman import).
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<StudentImportResultDto>> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmagan" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat .xlsx (Excel) fayl qabul qilinadi" });

        List<string[]> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            rows = ExcelImport.ReadRows(stream, ImportReadColumns);
        }
        catch
        {
            return BadRequest(new { message = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring" });
        }

        // Guruhlar oldindan yuklab olinadi (har qatorda DB so'rovi bo'lmasligi uchun).
        var classByName = (await db.Classes.ToListAsync())
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            // Bir xil nomli guruhlardan ARXIVLANMAGANI (Create bilan bir xil qoida).
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.IsArchived).First(), StringComparer.OrdinalIgnoreCase);

        // DUBLIKATDAN HIMOYA (`StudentImportPlan`): bazadagi o'quvchilar va ularning a'zoliklari —
        // qator bilan ism + telefon mos kelsa yangisi yaratilmaydi, faqat guruhga qo'shiladi.
        var existing = (await db.Students.AsNoTracking()
                .Select(x => new { x.Id, x.FullName, x.IsArchived, x.SchoolGrade, x.SchoolGradeYear,
                                   x.Phone, x.FatherPhone, x.MotherPhone, x.ParentPhone })
                .ToListAsync())
            .Select(x => new StudentImportPlan.Existing(x.Id, x.FullName, x.IsArchived,
                SchoolGrades.Current(x.SchoolGrade, x.SchoolGradeYear, AppClock.Today),
                x.Phone, x.FatherPhone, x.MotherPhone, x.ParentPhone))
            .ToList();
        var groupNameById = classByName.Values.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        var memberships = (await db.StudentGroups.AsNoTracking()
                .Select(m => new { m.StudentId, m.GroupId, m.IsActive }).ToListAsync())
            .Where(m => groupNameById.ContainsKey(m.GroupId))
            .Select(m => (m.StudentId, groupNameById[m.GroupId], m.IsActive))
            .ToList();

        // 0-qator — sarlavha; ma'lumot 1-indeksdan boshlanadi (Excel'dagi 2-qator).
        // Ustunlar SARLAVHA bo'yicha topiladi (`MapColumns`): yangi shablon (Jinsi'siz) ham, eski (Jinsi va
        // Sinf bilan) ham to'g'ri o'qiladi; sarlavha tanilmasa — eski tartib.
        var columns = StudentImportPlan.MapColumns(rows.Count > 0 ? rows[0] : Array.Empty<string>());
        var planRows = rows.Skip(1).Select((r, i) => StudentImportPlan.ReadRow(i + 2, r, columns)).ToList();
        var steps = StudentImportPlan.Build(planRows, existing, memberships, classByName.Keys);

        var errors = new List<StudentImportRowErrorDto>();
        var warnings = new List<StudentImportRowErrorDto>();
        int created = 0, skipped = 0, existingCount = 0, addedToGroups = 0;
        var joinCharged = 0m;
        var joinChargedStudents = new HashSet<string>(StringComparer.Ordinal);
        var joinCharges = new List<(string StudentId, Group Group, int ExcelRow)>();
        // Kurslarning sinf narxlari — qo'shilgan oy hisobi uchun, kerak bo'lganda BIR marta (N+1 bo'lmasin).
        PricingBook? pricing = null;
        var createdByIndex = new Dictionary<int, Student>();

        // SIG'IM (`GroupSeats`, AddMember bilan bir xil qoida) — import ham guruhni to'ldirib yubormasin.
        // Band o'rinlar guruh bo'yicha BIR MARTA o'qiladi va shu importda qo'shilganlar bilan oshiriladi
        // (hammasi bitta SaveChanges bilan saqlanadi — bazadan qayta sanab bo'lmaydi).
        var seatsUsed = new Dictionary<string, int>(StringComparer.Ordinal);
        async Task<bool> TakeSeatAsync(Group g, string memberStatus)
        {
            if (g.Capacity <= 0 || !GroupSeats.TakesSeat(memberStatus)) return true;
            if (!seatsUsed.TryGetValue(g.Id, out var used)) used = await GroupSeats.OccupiedAsync(db, g.Id);
            if (!GroupSeats.HasRoom(g, used)) { seatsUsed[g.Id] = used; return false; }
            seatsUsed[g.Id] = used + 1;
            return true;
        }

        foreach (var step in steps)
        {
            var r = step.Source;
            var cls = r.Group.Trim().Length > 0 ? classByName.GetValueOrDefault(r.Group.Trim()) : null;
            switch (step.Kind)
            {
                case StudentImportPlan.Kind.Error:
                    errors.Add(new StudentImportRowErrorDto(step.ExcelRow, step.Message ?? "Xato"));
                    break;

                case StudentImportPlan.Kind.Create:
                {
                    // Holat — faqat guruh bo'lsa qo'llaniladi
                    var status = cls is not null ? NormalizeGroupStatus(cls, r.Status.Trim()) : "";
                    // Guruh ARXIVDA (yopilgan) bo'lsa — o'quvchi baribir yaratiladi, lekin GURUHSIZ (audit R1-1):
                    // yopiq guruhdagi a'zolik oylik hisobga tushib qarz yozardi. `AddMember` bilan bir xil qoida.
                    if (cls is { IsArchived: true })
                    {
                        warnings.Add(new StudentImportRowErrorDto(step.ExcelRow,
                            $"{ArchivedGroupMessage(cls)} — o'quvchi guruhsiz yaratildi"));
                        cls = null;
                        status = "";
                    }
                    // Guruh TO'LGAN bo'lsa o'quvchi baribir yaratiladi (qator yo'qolmasin), lekin GURUHSIZ —
                    // natija oynasida ogohlantirish bilan (admin boshqa guruhga qo'shadi).
                    if (cls is not null && !await TakeSeatAsync(cls, status))
                    {
                        warnings.Add(new StudentImportRowErrorDto(step.ExcelRow,
                            $"{GroupSeats.FullMessage(cls)}: \"{cls.Name}\" — o'quvchi guruhsiz yaratildi"));
                        cls = null;
                        status = "";
                    }
                    var payload = new StudentPayload(
                        FullName: StudentImportPlan.CleanName(r.FullName),
                        BirthDate: NormalizeDate(r.BirthDate),
                        Address: "",
                        Gender: StudentGender.Normalize(r.Gender),
                        ParentFullName: null,
                        ParentPhone: null,
                        ClassName: cls?.Name ?? "", // guruh bo'lsa guruh nomi, aks holda bo'sh
                        EnrollmentDate: null,
                        Phone: PhoneUtil.Normalize(r.Phone),
                        FatherFullName: r.FatherName.Trim().Length > 0 ? r.FatherName.Trim() : null,
                        FatherPhone: r.FatherPhone.Trim().Length > 0 ? PhoneUtil.Normalize(r.FatherPhone) : null,
                        MotherFullName: r.MotherName.Trim().Length > 0 ? r.MotherName.Trim() : null,
                        MotherPhone: r.MotherPhone.Trim().Length > 0 ? PhoneUtil.Normalize(r.MotherPhone) : null,
                        SchoolGrade: step.Grade);
                    var newStudent = AddStudent(payload, cls, status);
                    createdByIndex[step.NewIndex!.Value] = newStudent;
                    created++;
                    // YANGI o'quvchi hali bazada yo'q — parallel pul amali unga tegolmaydi, hisob shu yerda.
                    if (cls is not null
                        && db.StudentGroups.Local.FirstOrDefault(x => x.StudentId == newStudent.Id && x.GroupId == cls.Id)
                            is { Status: "active" } newSg)
                        AddJoinCharge(newStudent.Id, await ChargeJoinMonthAsync(newStudent, cls, newSg, pricing ??= await PricingBook.LoadAsync(db)));
                    if (step.Warning is not null) warnings.Add(new StudentImportRowErrorDto(step.ExcelRow, step.Warning));
                    break;
                }

                case StudentImportPlan.Kind.Join:
                case StudentImportPlan.Kind.Skip when step.ExistingId is not null || step.NewIndex is not null:
                {
                    // MAVJUD o'quvchi (bazada yoki shu faylda yuqoriroqda yaratilgan).
                    var student = step.ExistingId is not null
                        ? await db.Students.FindAsync(step.ExistingId)
                        : createdByIndex.GetValueOrDefault(step.NewIndex!.Value);
                    if (student is null) { skipped++; break; }
                    existingCount++;

                    if (step.FillGrade && student.SchoolGrade is null)
                    {
                        var before = AuditService.StudentProfileSnapshot(student);
                        var (oldGrade, oldGradeYear) = (student.SchoolGrade, student.SchoolGradeYear);
                        ApplySchoolGrade(student, step.Grade);
                        audit.Record(AuditService.EntityStudent, student.Id, "update",
                            $"O'quvchi ma'lumoti tahrirlandi — Excel importi, sinf qo'shildi ({student.FullName})",
                            before: before, after: AuditService.StudentProfileSnapshot(student), studentId: student.Id);
                        await GradeRepricing.ApplyAsync(db, audit, student, oldGrade, oldGradeYear);
                    }

                    // Guruhga qo'shilmadi (arxiv/sig'im) — «… guruhiga qo'shildi» degan reja matni yozilmasin.
                    var joinRefused = false;
                    if (step.Kind == StudentImportPlan.Kind.Join && cls is { IsArchived: true })
                    {
                        // Arxivdagi (yopilgan) guruhga qo'shilmaydi — yaratishdagi bilan bir xil (audit R1-1).
                        warnings.Add(new StudentImportRowErrorDto(step.ExcelRow,
                            $"{ArchivedGroupMessage(cls)} — {student.FullName} guruhga qo'shilmadi"));
                        joinRefused = true;
                    }
                    else if (step.Kind == StudentImportPlan.Kind.Join && cls is not null)
                    {
                        var joinStatus = NormalizeGroupStatus(cls, r.Status.Trim());
                        if (!await TakeSeatAsync(cls, joinStatus))
                        {
                            warnings.Add(new StudentImportRowErrorDto(step.ExcelRow,
                                $"{GroupSeats.FullMessage(cls)}: \"{cls.Name}\" — {student.FullName} guruhga qo'shilmadi"));
                            joinRefused = true;
                        }
                        else
                        {
                            var joinSg = AddMembershipRow(student.Id, cls, joinStatus, AppClock.Today.ToString("yyyy-MM-dd"));
                            audit.Record("Membership", $"{cls.Id}:{student.Id}", "create",
                                $"Guruhga qo'shildi (Excel importi): {student.FullName} → {cls.Name}",
                                studentId: student.Id);
                            addedToGroups++;
                            // MAVJUD o'quvchi — qo'shilgan oy hisobi SAQLASHDAN KEYIN, o'quvchi qulfi ostida (pastda).
                            if (joinSg.Status == "active") joinCharges.Add((student.Id, cls, step.ExcelRow));
                        }
                    }
                    warnings.Add(new StudentImportRowErrorDto(step.ExcelRow, joinRefused
                        ? $"\"{student.FullName}\" allaqachon bor — yangi o'quvchi yaratilmadi"
                        : step.Message ?? ""));
                    break;
                }

                default: // bo'sh qator
                    skipped++;
                    break;
            }
        }

        if (created > 0 || existingCount > 0) await db.SaveChangesAsync();

        // MAVJUD o'quvchini «active» bilan guruhga qo'shishning qo'shilgan oy hisobi — har biri o'quvchi QULFI ostida,
        // balans bazadan QAYTA o'qilib, ALOHIDA saqlanadi. ⚠️ Halqa ichida yozilsa: (1) import davomida kelgan to'lov
        // yoki fon accrual'i oxiridagi bitta SaveChanges bilan eski balans ustidan yozilib YO'QOLARDI; (2) bir o'quvchi
        // ikki guruhga qo'shilsa, eski guruhsiz (aggregate) qator saqlanmay turib ikki marta balansga qaytarilardi.
        // Bittasining xatosi qolganlarini to'xtatmaydi va importni (allaqachon saqlangan) 500 ga aylantirmaydi —
        // sabab natija oynasida ogohlantirish bo'lib chiqadi (hisobni keyin «Qo'shilgan oy hisobini tuzatish» yozadi).
        foreach (var (sid, jcls, excelRow) in joinCharges)
        {
            using var gate = await PaymentIdempotency.LockAsync("student:" + sid);
            try
            {
                var st = await db.Students.FindAsync(sid);
                var jsg = await db.StudentGroups.FirstOrDefaultAsync(x => x.StudentId == sid && x.GroupId == jcls.Id && x.IsActive);
                if (st is null || jsg is null) continue;
                await StudentBalanceSync.ReloadAsync(db, st);
                AddJoinCharge(sid, await ChargeJoinMonthAsync(st, jcls, jsg, pricing ??= await PricingBook.LoadAsync(db)));
                await db.SaveChangesAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                db.ChangeTracker.Clear();
                warnings.Add(new StudentImportRowErrorDto(excelRow,
                    $"«{jcls.Name}» guruhiga qo'shildi, lekin qo'shilgan oy hisoblanmadi — Moliya → «Qo'shilgan oy hisobini tuzatish»"));
            }
        }

        return new StudentImportResultDto(created, errors.Count, skipped, errors,
            Existing: existingCount, AddedToGroups: addedToGroups, Warnings: warnings,
            JoinMonthCharged: joinCharged, JoinMonthChargedCount: joinChargedStudents.Count);

        void AddJoinCharge(string studentId, decimal amount)
        {
            if (amount <= 0) return;
            joinCharged += amount;
            joinChargedStudents.Add(studentId); // natija oynasi O'QUVCHILAR sonini aytadi (a'zoliklar emas)
        }
    }

    private static string NormalizeStatus(string raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        // active yoki frozen bo'lsa shuning o'zi, aks holda default "trial"
        return v is "active" or "frozen" ? v : "trial";
    }

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy",
    };

    /// <summary>Sanani "YYYY-MM-DD" ga keltiradi. Excel matn sanasini ham, raqamli (OADate) sanasini ham qabul qiladi.</summary>
    private static string NormalizeDate(string raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return "";
        if (DateTime.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd");
        if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var oa) && oa is > 1 and < 600000)
        {
            try { return DateTime.FromOADate(oa).ToString("yyyy-MM-dd"); } catch { /* e'tiborsiz */ }
        }
        return v; // ixtiyoriy maydon — noma'lum format bo'lsa, kiritilganicha qoladi
    }

    private static int? ParseIntOrNull(string raw)
    {
        var v = (raw ?? "").Replace("%", "").Trim();
        return int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static decimal? ParseDecimalOrNull(string raw)
    {
        var v = (raw ?? "").Replace(" ", "").Replace(",", "").Trim();
        return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }


    // ---------- «Chegirma» — kelmagan darslar (to'lov oynasida, `LessonDiscountHttp`) ----------

    [HttpGet("{id}/lesson-discounts")]
    public Task<ActionResult<List<LessonDiscountDto>>> LessonDiscounts(string id) =>
        LessonDiscountHttp.ListAsync(db, id);

    [HttpGet("{id}/lesson-discounts/quote")]
    public Task<ActionResult<LessonDiscountQuoteDto>> LessonDiscountQuote(
        string id, [FromQuery] string? groupId, [FromQuery] string? month, [FromQuery] int lessons) =>
        LessonDiscountHttp.QuoteAsync(db, id, groupId, month, lessons);

    [HttpPost("{id}/lesson-discounts")]
    public Task<ActionResult<LessonDiscountDto>> ApplyLessonDiscount(string id, LessonDiscountRequest req) =>
        LessonDiscountHttp.ApplyAsync(db, audit, User, id, req);

    [HttpPost("{id}/lesson-discounts/{discountId}/cancel")]
    public Task<ActionResult<LessonDiscountDto>> CancelLessonDiscount(string id, string discountId) =>
        LessonDiscountHttp.CancelAsync(db, audit, User, id, discountId);

    /// <summary>Bitta guruh bo'yicha o'quvchining oylik hisobi (to'lov oynasi uchun) — aggregate emas, faqat
    /// shu guruh narxi + shu guruhga teglangan to'lovlar. O'quvchi bir nechta guruhda o'qisa shu kerak.</summary>
    [HttpGet("{id}/group-ledger")]
    public async Task<ActionResult<GroupLedgerDto>> GroupLedger(string id, [FromQuery] string groupId)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        var group = await db.Classes.FindAsync(groupId);
        if (group is null) return NotFound();
        var membership = await db.StudentGroups
            .FirstOrDefaultAsync(sg => sg.StudentId == id && sg.GroupId == groupId);
        if (membership is null)
            return new GroupLedgerDto(group.Id, group.Name, group.Name, new List<GroupMonthDto>());
        return await StudentGroupLedger.BuildAsync(db, student, group, membership);
    }

    /// <summary>O'quvchiga to'lov kiritish — balansga qo'shiladi va moliyaga kirim sifatida yoziladi.
    /// <paramref name="req"/> ichida Month ("YYYY-MM") berilsa, to'lov shu oy uchun hisoblanadi.
    /// Mantiqning O'ZI <see cref="PaymentIntake"/> da — Kassa bo'limi ham aynan shu yo'ldan yozadi.</summary>
    [HttpPost("{id}/payments")]
    public async Task<IActionResult> AddPayment(string id, PaymentRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        var res = await PaymentIntake.AddAsync(db, audit, autoMsg, student, req,
            User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        return PaymentIntakeHttp.ToActionResult(this, res);
    }

    /// <summary>O'quvchi to'lov tarixi: oylar bo'yicha hisoblangan/to'langan holat.</summary>
    [HttpGet("{id}/ledger")]
    public async Task<ActionResult<StudentLedgerDto>> Ledger(string id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        var ledger = await StudentLedger.BuildAsync(db, student);
        // Admin tarixida BEKOR QILINGAN to'lovlar ham ko'rinadi (ustiga chizilgan) — jamilarga kirmaydi.
        var voided = await StudentLedger.VoidedPaymentsAsync(db, student.Id);
        return voided.Count == 0 ? ledger : ledger with
        {
            Payments = ledger.Payments.Concat(voided).OrderByDescending(p => p.Date).ToList(),
        };
    }

    /// <summary>Shu oyning HISOBLANGAN summasini qo'lda tahrirlaydi — «Balansni tuzatish» (profil) va «Tranzaksiyalar
    /// tarixi»dagi qalam. Balans effektiv (summa − chegirma) farqiga moslanadi; o'zgarish auditga yoziladi.
    /// <para>⚠️ <b>FAQAT superadmin</b> (HARD rol, 2026-10-09): UI ham faqat unga ko'rsatadi; ilgari server
    /// `students.list:edit` li har qanday xodimga ochiq edi (o'quvchi to'lovi tahriri bilan bir xil qoida —
    /// `billing.md` «TAHRIR»). Balans o'quvchi QULFI ostida qayta o'qiladi (to'lov bilan bir navbat).</para>
    /// <para><c>Preview</c> — natija (balans oldin/keyin) qaytadi, HECH NARSA saqlanmaydi.</para></summary>
    [HttpPut("{id}/charges/{month}")]
    public async Task<ActionResult<EditChargeResultDto>> EditCharge(string id, string month, EditChargeRequest req, [FromQuery] string? groupId = null)
    {
        if (!User.IsInRole(Roles.SuperAdmin))
            return StatusCode(403, new { message = "Oylik hisobni (balansni) faqat superadmin tuzata oladi" });
        if (req.Amount < 0) return BadRequest(new { message = "Summa manfiy bo'lishi mumkin emas" });
        var note = (req.Note ?? "").Trim();
        if (note.Length > 300) return BadRequest(new { message = "Izoh 300 belgidan oshmasin" });
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        using var gate = await PaymentIdempotency.LockAsync("student:" + id);
        await StudentBalanceSync.ReloadAsync(db, student);
        // Per-guruh: qaysi guruh hisobi tahrirlanmoqda (null = guruhsiz/ClassName hisobi).
        var gid = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        var charge = await db.MonthlyCharges
            .FirstOrDefaultAsync(c => c.StudentId == id && c.GroupId == gid && c.Month == month);
        if (charge is null) return NotFound(new { message = "Bu oy uchun hisob topilmadi" });

        // ⚠️ CHEGIRMA YANGI SUMMADAN QAYTA HISOBLANADI. `MonthlyCharge.Discount` — birinchi
        // hisoblashda ESKI summadan chiqarilgan MUTLAQ so'm. Uni shundayligicha ko'chirsak,
        // FOIZLI chegirma summa tahrirlanganda jimgina boshqa foizga aylanardi:
        // 1 000 000 (30% = 300 000) → 600 000 da chegirma 300 000 bo'lib qolib, aslida 50%
        // bo'lardi. Chegirmaning SPETSIFIKATSIYASI (foiz + summa) registrda — pul manbai
        // aynan shu (`.claude/rules/discounts.md` §2), shuning uchun bazani registrdan olamiz.
        var book = await DiscountBook.LoadForStudentAsync(db, student.Id);
        var rows = book.For(student.Id);
        // Amaldagi qator YO'Q bo'lsa (tarixiy oy yoki bekor qilingan chegirma) — `null` uzatamiz
        // va eski chegirma saqlanadi: qayta hisoblansa u JIMGINA 0 ga tushib, o'quvchiga
        // to'satdan qarz yozilardi.
        decimal? specDiscount = DiscountRules.Resolve(rows, month, gid) is null
            ? null
            : TuitionService.DiscountForMonth(rows, Math.Max(0m, req.Amount), month, gid);
        // Kelmagan darslar chegirmasi (to'lov oynasidagi «Chegirma») registrda YO'Q — u mutlaq so'm
        // bo'lib qatorning `Discount`iga qo'shilgan. Qayta hisobda uni ham qo'shamiz, aks holda u
        // jimgina o'chib ketardi. `null` holatda eski chegirma (u bilan birga) saqlanadi.
        if (specDiscount is not null)
            specDiscount += await LessonDiscountService.ActiveAmountAsync(db, student.Id, gid, month);

        // ⚠️ REJA AVVAL TUZILADI (sof funksiya), qator KEYIN o'zgartiriladi. Ilgari chegirma
        // `charge` ustida DARHOL qirqilar, "eski effektiv" esa ALLAQACHON O'ZGARGAN chegirma bilan
        // hisoblanardi — natijada balansga hech qachon to'lanmagan pul qaytarilardi
        // (misol `TuitionService.PlanChargeEdit` izohida).
        var plan = TuitionService.PlanChargeEdit(charge.Amount, charge.Discount, req.Amount, specDiscount);
        var balanceBefore = student.Balance;
        var result = new EditChargeResultDto(!req.Preview, plan.OldAmount, plan.NewAmount, plan.OldDiscount,
            plan.NewDiscount, balanceBefore, balanceBefore + plan.BalanceDelta);
        if (req.Preview) return result; // hech narsa o'zgartirilmadi

        // Hisob balansni EFFEKTIV miqdorda kamaytirgan edi — farqni balansga qaytaramiz.
        student.Balance += plan.BalanceDelta;
        charge.Amount = plan.NewAmount;
        charge.Discount = plan.NewDiscount;
        charge.Locked = true; // qo'lda tahrirlandi — avtomatik qayta hisob endi bu yozuvni o'zgartirmaydi.

        // Chegirma o'zgargan bo'lsa (qayta hisoblangan yoki qirqilgan) — buni ham AYTAMIZ va
        // SABABINI yozamiz: tarixda faqat "summa o'zgardi" tursa, keyin "chegirmam qayerga
        // ketdi" savoli javobsiz qolardi.
        var discountWhy = plan.DiscountRecomputed ? " (yangi summadan qayta hisoblandi)"
            : plan.DiscountClamped ? " (yangi summaga qirqildi)"
            : "";
        var discountNote = plan.DiscountChanged
            ? $"; chegirma {AuditService.Money(plan.OldDiscount)} → {AuditService.Money(plan.NewDiscount)} so'm{discountWhy}"
            : "";
        var groupName = gid is null ? null : await db.Classes.Where(c => c.Id == gid).Select(c => c.Name).FirstOrDefaultAsync();
        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"Oylik hisob qo'lda tahrirlandi ({month}{(groupName is null ? "" : ", " + groupName)}): "
            + $"{AuditService.Money(plan.OldAmount)} → {AuditService.Money(plan.NewAmount)} so'm{discountNote}; "
            + $"balans {AuditService.Money(result.BalanceBefore)} → {AuditService.Money(result.BalanceAfter)} so'm — {student.FullName}"
            + (note.Length > 0 ? $" — izoh: {note}" : ""),
            studentId: student.Id);

        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// O'quvchining Local Call (CTI) qo'ng'iroqlari tarixi — StartedAt bo'yicha kamayish, max 100 ta.
    /// Faqat metadata (audio playback yo'q — u "calls.local" ruxsati bilan Local Call modulida).
    /// Eski bulutli telefoniya tarixi (`Calls` jadvali) bu ro'yxatga KIRMAYDI — jonli telefoniya
    /// faqat Local Call.
    /// </summary>
    [HttpGet("{id}/calls")]
    public async Task<ActionResult<List<StudentCallDto>>> GetCalls(string id)
    {
        // Local/CTI qo'ng'iroqlari — agent nomlarini oldindan lug'atga yig'ib N+1'dan qochamiz.
        var records = await db.CtiCallRecords.AsNoTracking()
            .Where(c => c.StudentId == id)
            .OrderByDescending(c => c.StartedAt)
            .Take(100)
            .ToListAsync();

        var agentIds = records.Select(c => c.AgentId).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
        var agentNames = await db.CtiAgents.AsNoTracking()
            .Where(a => agentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.DisplayName);

        var calls = records.Select(c => new StudentCallDto(
            Id: c.Id,
            Source: "local",
            // "missed" — javobsiz kiruvchi; qolgani entitydagi incoming/outgoing.
            Direction: c.Direction == "missed" ? "incoming" : c.Direction,
            PhoneNumber: c.RemoteNumber,
            StartedAt: c.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            DurationSec: c.DurationSec,
            Answered: c.Direction != "missed" && (c.AnsweredAt != null || c.DurationSec > 0),
            HasAudio: c.AudioUploaded && c.AudioPath.Length > 0,
            Handler: agentNames.GetValueOrDefault(c.AgentId, "")))
            .ToList();

        return calls;
    }

    /// <summary>
    /// O'quvchiga (yoki ota-onasiga) yuborilgan SMS'lar tarixi — CreatedAt bo'yicha kamayish, max 200 ta.
    /// SmsLog'da StudentId yo'q, shu sabab o'quvchining barcha telefon raqamlari (Phone/ParentPhone/
    /// FatherPhone/MotherPhone) oxirgi 9 raqami bo'yicha moslashtiriladi (turli format: +998/998/local).
    /// </summary>
    [HttpGet("{id}/sms")]
    public async Task<ActionResult<List<StudentSmsDto>>> GetSms(string id)
    {
        var student = await db.Students.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new { s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone })
            .FirstOrDefaultAsync();
        if (student is null) return NotFound();

        var keys = new[] { student.Phone, student.ParentPhone, student.FatherPhone, student.MotherPhone }
            .Select(PhoneUtil.Key)
            .Where(k => k.Length >= 7)
            .Distinct()
            .ToList();
        if (keys.Count == 0) return new List<StudentSmsDto>();

        var logs = await db.SmsLogs.AsNoTracking()
            .Where(l => keys.Any(k => l.PhoneNumber.EndsWith(k)))
            .OrderByDescending(l => l.CreatedAt)
            .Take(200)
            .ToListAsync();

        return logs.Select(l => new StudentSmsDto(
            l.Id, l.PhoneNumber, l.Message, l.Status, l.Provider, l.CreatedAt.ToString("o"))).ToList();
    }
}
