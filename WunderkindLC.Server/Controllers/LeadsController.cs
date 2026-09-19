using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("leads.list")]
[Route("api/admin/leads")]
// ⚠️ `logger` — lid KARTASI (Telegram) uchun: `LeadNotifier` xatoni yutadi (karta CRM amalini
// buza olmaydi), lekin sababi logda qolishi kerak — aks holda "nega bu lid kartasi
// yangilanmayapti?" savoliga javob topib bo'lmasdi.
public class LeadsController(
    AppDbContext db, AuditService audit, TelegramService telegram, AutoMessageService autoMsg,
    ILogger<LeadsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LeadWithAttendanceDto>>> GetAll()
    {
        var leads = await db.Leads.AsNoTracking().ToListAsync();

        // Ilgari HAR lid uchun GetFirstLessonAttendanceAsync 3 tagacha alohida so'rov qilardi
        // (500 lid = ~1500 so'rov). Endi barcha konvertilgan o'quvchilar uchun davomat holati
        // bir necha TO'PLAMLI so'rovda hisoblanadi (semantikasi AYNAN saqlangan).
        var studentIds = leads
            .Where(l => !string.IsNullOrWhiteSpace(l.ConvertedStudentId))
            .Select(l => l.ConvertedStudentId!)
            .Distinct().ToList();
        var attendanceByStudent = await ComputeFirstLessonAttendanceAsync(studentIds);

        // MAS'UL XODIM ismi — JORIY ro'yxatdan (voronka analitikasidagi bilan bir xil naqsh:
        // hodisadagi/snapshotdagi eskirgan ism emas, bugungi ism ko'rsatiladi). Bitta so'rov,
        // lidlar soniga bog'liq emas.
        var assigneeIds = leads
            .Where(l => !string.IsNullOrWhiteSpace(l.AssigneeUserId))
            .Select(l => l.AssigneeUserId!)
            .Distinct().ToList();
        var assigneeNames = assigneeIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : (await db.Users.AsNoTracking()
                    .Where(u => assigneeIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FullName }).ToListAsync())
                .ToDictionary(u => u.Id, u => u.FullName ?? "", StringComparer.Ordinal);

        // Jadval ustunlari (edutizim "Buyurtmalar ro'yxati"): O'QITUVCHI/GURUH — sinov darsidan,
        // KURS DARAJASI — daraja testidan. Hammasi TO'PLAMLI (lidlar soniga bog'liq bo'lmagan)
        // bir necha so'rovda — kanban/jadval har ochilganda N ta so'rov ketmasin.
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var ctx = await LoadTrialContextAsync(pendingOnly: false);

        // MANBA — eski/import yozuvlarda `Lead.Source` da manba ID'si turishi mumkin (forma esa
        // NOM saqlaydi). Ro'yxatda GUID ko'rinib qolmasin — kurs ustunidagi bilan bir xil qoida.
        var sourceNames = (await db.LeadSources.AsNoTracking()
                .Select(x => new { x.Id, x.Name }).ToListAsync())
            .ToDictionary(x => x.Id, x => x.Name ?? "", StringComparer.Ordinal);

        return leads.Select(lead =>
        {
            var trials = ctx.TrialsByLead[lead.Id].ToList();
            var shown = LeadFirstLesson.Display(trials, today);
            var group = shown is { } s ? ctx.Groups.GetValueOrDefault(s.GroupId) : null;
            // Kutilayotgan birinchi dars — FAQAT ochiq lidda (bosh sahifa kartochkasi bilan bitta ta'rif).
            var first = string.IsNullOrWhiteSpace(lead.ConvertedStudentId) ? LeadFirstLesson.Pick(trials, today) : null;
            return new LeadWithAttendanceDto(
            lead.Id, lead.FullName, lead.Gender, lead.BirthDate, lead.Phone,
            lead.FatherFullName, lead.FatherPhone, lead.MotherFullName,
            lead.MotherPhone, lead.Note, lead.Stage,
            sourceNames.TryGetValue(lead.Source ?? "", out var srcName) && srcName.Length > 0
                ? srcName
                : lead.Source,
            // KURS — «Birinchi darsga yozilganlar» dagi bilan AYNAN bir xil qoida: eski
            // yozuvlarda `InterestSubject` da kurs ID'si turishi mumkin, ro'yxatda GUID
            // ko'rinib qolmasin (bo'sh bo'lsa sinov guruhining kursi olinadi).
            CourseLabel(lead.InterestSubject, group?.CourseId, ctx.CourseNames),
            lead.CreatedAt, lead.ConvertedStudentId,
            string.IsNullOrWhiteSpace(lead.ConvertedStudentId)
                ? "no-lesson"
                : attendanceByStudent.GetValueOrDefault(lead.ConvertedStudentId, "no-lesson"),
            lead.DistrictId, lead.SchoolId,
            lead.RepeatCount, lead.LastRepeatAt,
            lead.AssigneeUserId,
            string.IsNullOrWhiteSpace(lead.AssigneeUserId)
                ? null : assigneeNames.GetValueOrDefault(lead.AssigneeUserId!),
            lead.ClosedByUserId, lead.ClosedAt,
            FirstLessonAt: first?.ScheduledAt,
            GroupId: group is null ? null : shown!.Value.GroupId,
            GroupName: group?.Name,
            TeacherId: string.IsNullOrWhiteSpace(group?.TeacherId) ? null : group!.TeacherId,
            TeacherName: string.IsNullOrWhiteSpace(group?.TeacherId) ? null : ctx.TeacherNames.GetValueOrDefault(group!.TeacherId),
            // KURS DARAJASI — lidda saqlangan qiymat USTUN (ko'chirilgan/qo'lda belgilangan),
            // bo'sh bo'lsa avvalgidek daraja testi natijasi (`Lead.Level` izohiga qarang).
            Level: string.IsNullOrWhiteSpace(lead.Level)
                ? ctx.LevelByLead.GetValueOrDefault(lead.Id)
                : lead.Level);
        }).ToList();
    }

    /// <summary>
    /// «BIRINCHI DARSGA YOZILGANLAR» (edutizim: Lidlar → Birinchi darsga yozilganlar): ochiq lid +
    /// natijasi belgilanmagan sinov darsi. Tanlov va filtrlar — <see cref="LeadFirstLesson"/>
    /// (sof funksiyalar); ta'rif bosh sahifadagi "Birinchi darsga keladiganlar" kartochkasi bilan
    /// BITTA (<see cref="DashboardSummary.FirstLessonLeads"/>), farqi — o'tib ketgan sana ham
    /// ro'yxatda qoladi (<c>IsPast</c>, qizil qator).
    ///
    /// <para>Ruxsat — sinf darajasidagi <c>leads.list</c> (lidlar ro'yxati bilan AYNAN bir xil):
    /// bu o'sha lidlarning boshqa kesimi, yangi ma'lumot ochmaydi.</para>
    /// </summary>
    [HttpGet("first-lesson")]
    public async Task<ActionResult<IEnumerable<FirstLessonLeadDto>>> FirstLesson(
        [FromQuery] string? date = null, [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] string? course = null, [FromQuery] string? level = null,
        [FromQuery] int? weekday = null, [FromQuery] string? parity = null,
        [FromQuery] string? assignee = null, [FromQuery] string? teacher = null,
        [FromQuery] string? color = null, [FromQuery] string? q = null)
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var leads = await db.Leads.AsNoTracking()
            .Where(l => l.ConvertedStudentId == null)
            .Select(l => new
            {
                l.Id, l.FullName, l.Phone, l.FatherPhone, l.MotherPhone, l.FatherFullName,
                l.MotherFullName, l.Note, l.CreatedAt, l.InterestSubject, l.AssigneeUserId, l.Stage,
                l.Level,
            })
            .ToListAsync();
        var ctx = await LoadTrialContextAsync(pendingOnly: true);

        var picked = LeadFirstLesson.Select(
            leads.Select(l => new DashboardSummary.LeadRow(l.Id, false)),
            ctx.TrialsByLead.SelectMany(g => g), today);
        var byId = leads.Where(l => picked.ContainsKey(l.Id)).ToDictionary(l => l.Id, StringComparer.Ordinal);

        var rows = byId.Values.Select(l =>
        {
            var t = picked[l.Id];
            var g = ctx.Groups.GetValueOrDefault(t.GroupId);
            return new LeadFirstLesson.Row(
                l.Id, l.FullName ?? "",
                new[] { l.Phone ?? "", l.FatherPhone ?? "", l.MotherPhone ?? "" },
                $"{l.FatherFullName} {l.MotherFullName} {l.Note} {g?.Name}",
                t.ScheduledAt ?? "", LeadFirstLesson.IsPast(t.ScheduledAt, today),
                CourseLabel(l.InterestSubject, g?.CourseId, ctx.CourseNames),
                string.IsNullOrWhiteSpace(l.Level) ? ctx.LevelByLead.GetValueOrDefault(l.Id, "") : l.Level,
                g?.TeacherId ?? "", l.AssigneeUserId ?? "",
                g?.Days ?? new List<int>());
        });
        var filtered = LeadFirstLesson.Apply(rows, new LeadFirstLesson.Filter(
            date, from, to, course, level, weekday, parity, assignee, teacher, color, q));

        var assigneeIds = filtered.Select(r => r.AssigneeUserId).Where(id => id.Length > 0).Distinct().ToList();
        var assigneeNames = assigneeIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : (await db.Users.AsNoTracking()
                    .Where(u => assigneeIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FullName }).ToListAsync())
                .ToDictionary(u => u.Id, u => u.FullName ?? "", StringComparer.Ordinal);

        return filtered.Select(r =>
        {
            var l = byId[r.LeadId];
            var t = picked[r.LeadId];
            var g = ctx.Groups.GetValueOrDefault(t.GroupId);
            return new FirstLessonLeadDto(
                l.Id, l.FullName ?? "", l.Phone ?? "", l.FatherPhone ?? "", l.MotherPhone ?? "",
                l.CreatedAt, r.FirstLessonAt, r.IsPast, t.Id,
                t.GroupId, g?.Name ?? "", r.TeacherId, ctx.TeacherNames.GetValueOrDefault(r.TeacherId, ""),
                r.Course, r.Level,
                r.AssigneeUserId.Length == 0 ? null : r.AssigneeUserId,
                r.AssigneeUserId.Length == 0 ? null : assigneeNames.GetValueOrDefault(r.AssigneeUserId),
                l.Note, l.Stage ?? "");
        }).ToList();
    }

    /// <summary>Guruh — lidlar jadvali uchun kerakli qismi.</summary>
    private sealed record TrialGroup(string Name, string TeacherId, string CourseId, List<int> Days);

    /// <summary>Sinovlar (lid bo'yicha), guruhlar, o'qituvchi/kurs nomlari va lid darajasi.</summary>
    private sealed record TrialContext(
        ILookup<string, LeadFirstLesson.TrialRow> TrialsByLead,
        Dictionary<string, TrialGroup> Groups,
        Dictionary<string, string> TeacherNames,
        Dictionary<string, string> CourseNames,
        Dictionary<string, string> LevelByLead);

    /// <summary>
    /// Lidlar jadvali va «Birinchi darsga yozilganlar» uchun UMUMIY yuklash — bir necha to'plamli
    /// so'rov. <paramref name="pendingOnly"/> — faqat natijasi belgilanmagan sinovlar.
    /// Arxivlangan guruhlar ham olinadi: sinov o'sha guruhga yozilgan bo'lsa nomi yo'qolmasin.
    /// </summary>
    private async Task<TrialContext> LoadTrialContextAsync(bool pendingOnly)
    {
        var trialQuery = db.TrialLessons.AsNoTracking();
        if (pendingOnly) trialQuery = trialQuery.Where(t => t.Result == DashboardSummary.TrialPending);
        var trials = (await trialQuery
                .Select(t => new { t.Id, t.LeadId, t.GroupId, t.Result, t.ScheduledAt }).ToListAsync())
            .Select(t => new LeadFirstLesson.TrialRow(t.Id, t.LeadId, t.GroupId ?? "", t.Result ?? "", t.ScheduledAt ?? ""))
            .ToLookup(t => t.LeadId, StringComparer.Ordinal);

        var groups = (await db.Classes.AsNoTracking()
                .Select(g => new { g.Id, g.Name, g.TeacherId, g.CourseId, g.Days }).ToListAsync())
            .ToDictionary(g => g.Id,
                g => new TrialGroup(g.Name ?? "", g.TeacherId ?? "", g.CourseId ?? "", g.Days ?? new List<int>()),
                StringComparer.Ordinal);
        var teacherNames = (await db.Teachers.AsNoTracking()
                .Select(t => new { t.Id, t.FullName }).ToListAsync())
            .ToDictionary(t => t.Id, t => t.FullName ?? "", StringComparer.Ordinal);
        var courseNames = (await db.Subjects.AsNoTracking()
                .Select(s => new { s.Id, s.Name }).ToListAsync())
            .ToDictionary(s => s.Id, s => s.Name ?? "", StringComparer.Ordinal);

        // KURS DARAJASI — lidning ENG SO'NGGI daraja testi natijasi (bo'sh daraja hisobga olinmaydi).
        var levelByLead = (await db.LevelTestSubmissions.AsNoTracking()
                .Where(s => s.LeadId != "" && s.Level != "")
                .Select(s => new { s.LeadId, s.Level, s.CreatedAt }).ToListAsync())
            .GroupBy(s => s.LeadId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => g.OrderByDescending(s => s.CreatedAt ?? "", StringComparer.Ordinal).First().Level ?? "",
                StringComparer.Ordinal);

        return new TrialContext(trials, groups, teacherNames, courseNames, levelByLead);
    }

    /// <summary>
    /// KURS ustuni: lidning kursi (<c>InterestSubject</c> — odatda kurs NOMI; eski yozuvlarda kurs
    /// id'si bo'lsa nomiga aylantiriladi, `Stats` dagi bilan bir xil konvensiya), bo'sh bo'lsa —
    /// sinov guruhining kursi.
    /// </summary>
    private static string CourseLabel(string? interest, string? groupCourseId, Dictionary<string, string> courseNames)
    {
        var v = (interest ?? "").Trim();
        if (v.Length > 0)
            return courseNames.TryGetValue(v, out var byId) && !string.IsNullOrWhiteSpace(byId) ? byId.Trim() : v;
        return !string.IsNullOrWhiteSpace(groupCourseId) && courseNames.TryGetValue(groupCourseId, out var n) ? n.Trim() : "";
    }

    /// <summary>
    /// Yangi lid (qo'lda kiritish).
    ///
    /// <para>⚠️ Maydonlar «Lid kiritish formasi» sozlamasi bo'yicha TEKSHIRILADI
    /// (<see cref="LeadEntryFormService.ValidateAsync"/>): markaz majburiy deb belgilagan maydon
    /// bo'sh bo'lsa lid SAQLANMAYDI va 400 qaytadi. Tekshiruv AYNAN shu endpointda — ommaviy
    /// forma, daraja testi, landing, Instagram va Meta leadgen o'z qoidalari bilan ishlaydi
    /// (aks holda sozlama tashqi kanallardan kelayotgan lidlarni jimgina rad etardi).</para>
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Lead>> Create(LeadCreateRequest p)
    {
        var lead = new Lead
        {
            FullName = p.FullName,
            Gender = p.Gender,
            BirthDate = p.BirthDate,
            Phone = PhoneUtil.Normalize(p.Phone ?? ""),
            FatherFullName = p.FatherFullName ?? "",
            FatherPhone = PhoneUtil.Normalize(p.FatherPhone ?? ""),
            MotherFullName = p.MotherFullName ?? "",
            MotherPhone = PhoneUtil.Normalize(p.MotherPhone ?? ""),
            Note = p.Note,
            Stage = p.Stage,
            Source = p.Source ?? "",
            InterestSubject = p.InterestSubject ?? "",
            DistrictId = p.DistrictId ?? "",
            SchoolId = p.SchoolId ?? "",
            CreatedAt = Now(),
            // MAS'UL XODIM — lidni QO'LDA kiritgan odam. ⚠️ Bu FAQAT shu endpointda: ommaviy
            // forma, daraja testi, landing va Instagram/Meta webhook'lari orqali tug'ilgan lid
            // BIRIKTIRILMAGAN (null) qoladi — KPI'da "biriktirilmagan" alohida sanaladi va
            // botning ishini xodimning hisobiga yozib qo'yish o'lchovni yolg'on qilardi.
            AssigneeUserId = UserId(),
        };
        // ⚠️ Tekshiruv `db.Leads.Add` dan OLDIN: rad etilgan lid kontekstda osilib qolmasin.
        // Yaratishda javoblar lug'ati BO'SH bo'lsa ham uzatiladi (null EMAS) — ya'ni majburiy
        // qo'shimcha savol chindan tekshiriladi (tahrirlashdagi "tegilmadi" holati bu yerda yo'q).
        var (entryError, answersJson) = await LeadEntryFormService.ValidateAsync(
            db, lead, p.Answers ?? new Dictionary<string, List<string>>());
        if (entryError is not null) return BadRequest(new { message = entryError });
        lead.AnswersJson = answersJson ?? "";

        db.Leads.Add(lead);
        // Voronka uchun: lid QAYSI bosqichda tug'ilgani ham tarixga tushadi (FromStage yo'q).
        AddEvent(lead.Id, "created", $"Lid yaratildi ({lead.FullName})", toStage: lead.Stage);
        await db.SaveChangesAsync();
        // Botda ro'yxatdan o'tgan admin/xodimlarga yangi lid xabarnomasi (kim kiritgani bilan).
        await LeadNotifier.NotifyNewLeadAsync(db, telegram, lead, createdBy: Actor(), logger: logger);
        // Avto xabar — "Yangi lid kelganda" hodisasiga yoqilgan qoida bo'lsa, lidga avtomatik SMS.
        await autoMsg.DispatchLeadAsync(db, AutoMessageTriggers.LeadNew, lead);
        return lead;
    }

    /// <summary>Lidga BIR MARTALIK daraja-testi havolasini SMS qilib yuboradi (avto-SMS andoza, {link}).
    /// Lid testni o'z ma'lumotini qayta kiritmasdan ishlaydi; natija shu lidga bog'lanadi.</summary>
    [HttpPost("{id}/send-test")]
    public async Task<ActionResult<SendLeadTestResultDto>> SendTest(string id, SendLeadTestRequest req)
    {
        var lead = await db.Leads.FindAsync(id);
        if (lead is null) return NotFound(new { message = "Lid topilmadi" });
        var test = await db.LevelTests.FirstOrDefaultAsync(t => t.Id == req.TestId);
        if (test is null) return NotFound(new { message = "Test topilmadi" });

        var invite = new LevelTestInvite
        {
            TestId = test.Id, LeadId = lead.Id, CreatedAt = Now(),
        };
        db.LevelTestInvites.Add(invite);
        await db.SaveChangesAsync();

        var link = $"{Request.Scheme}://{Request.Host}/test/invite/{invite.Token}";
        var (ok, status, reqId) = await autoMsg.SendLeadTestLinkAsync(db, lead, link);
        invite.SmsStatus = ok ? "sent" : status;
        invite.SmsRequestId = reqId;
        AddEvent(lead.Id, "note", ok ? $"Daraja testi havolasi yuborildi: {test.Title}" : $"Test havolasi SMS xato: {status}");
        await db.SaveChangesAsync();
        await LeadNotifier.SyncCardAsync(db, telegram, lead.Id, logger: logger);

        return new SendLeadTestResultDto(ok, status, link);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, LeadUpdateRequest p)
    {
        var lead = await db.Leads.FindAsync(id);
        if (lead is null) return NotFound();
        lead.FullName = p.FullName;
        lead.Gender = p.Gender;
        lead.BirthDate = p.BirthDate;
        lead.Phone = PhoneUtil.Normalize(p.Phone ?? "");
        lead.FatherFullName = p.FatherFullName ?? "";
        lead.FatherPhone = PhoneUtil.Normalize(p.FatherPhone ?? "");
        lead.MotherFullName = p.MotherFullName ?? "";
        lead.MotherPhone = PhoneUtil.Normalize(p.MotherPhone ?? "");
        lead.Note = p.Note;
        if (p.Source is not null) lead.Source = p.Source;
        if (p.InterestSubject is not null) lead.InterestSubject = p.InterestSubject;
        if (p.DistrictId is not null) lead.DistrictId = p.DistrictId;
        if (p.SchoolId is not null) lead.SchoolId = p.SchoolId;

        // «Lid kiritish formasi» qoidalari tahrirlashda ham amal qiladi — aks holda majburiy
        // maydon bir marta to'ldirilib, keyin tahrirlashda bo'shatib yuborilardi.
        // ⚠️ `p.Answers` null bo'lsa qo'shimcha javoblar TEGILMAYDI (eskirgan yoki boshqa
        // chaqiruvchi lidning javoblarini jimgina o'chirib yubormasin).
        var (entryError, answersJson) = await LeadEntryFormService.ValidateAsync(db, lead, p.Answers);
        if (entryError is not null) return BadRequest(new { message = entryError });
        if (answersJson is not null) lead.AnswersJson = answersJson;

        await db.SaveChangesAsync();
        // Telegramdagi lid kartasi JOYIDA yangilanadi (yangi xabar yuborilmaydi) — kartasi yo'q
        // eski lidga esa hech narsa yuborilmaydi (`SyncCardAsync` izohiga qarang).
        await LeadNotifier.SyncCardAsync(db, telegram, lead.Id, logger: logger);
        return NoContent();
    }

    /// <summary>Lidni boshqa bosqichga (ustunga) ko'chirish.</summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> ChangeStage(string id, LeadStageRequest req)
    {
        var lead = await db.Leads.FindAsync(id);
        if (lead is null) return NotFound();
        var stage = await db.LeadStages.FindAsync(req.Stage);
        // Eski bosqich YANGILASHDAN OLDIN o'qib olinadi — voronkaga "qayerdan qayerga" yoziladi.
        var oldStage = lead.Stage;
        lead.Stage = req.Stage;
        // MAS'UL XODIM — lidni BIRINCHI marta qo'zg'atgan odam. Bot/ommaviy forma yaratgan lid
        // biriktirilmagan (null) keladi; uni kanbanda birinchi bo'lib ko'chirgan xodim — aynan
        // shu lid ustida ishlayotgan odam.
        // ⚠️ MAVJUD mas'ul HECH QACHON ustidan yozilmaydi: aks holda boshqa xodimning bitta
        // sudrab qo'yishi lidni (va u bilan birga KPI raqamini) o'ziga o'tkazib olardi.
        if (string.IsNullOrWhiteSpace(lead.AssigneeUserId)) lead.AssigneeUserId = UserId();
        AddEvent(id, "stage", $"Bosqich: {stage?.Title ?? req.Stage}",
            fromStage: oldStage, toStage: req.Stage);
        await db.SaveChangesAsync();
        // Bosqich — kartadagi eng muhim qator, shuning uchun karta darhol qayta chiziladi.
        await LeadNotifier.SyncCardAsync(db, telegram, lead.Id, logger: logger);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? reasonId = null)
    {
        var lead = await db.Leads.FindAsync(id);
        if (lead is null) return NotFound();
        db.LeadEvents.RemoveRange(db.LeadEvents.Where(e => e.LeadId == id));
        db.TrialLessons.RemoveRange(db.TrialLessons.Where(t => t.LeadId == id));
        db.Leads.Remove(lead);

        var reason = string.IsNullOrWhiteSpace(reasonId) ? ""
            : (await db.ActionReasons.Where(r => r.Id == reasonId).Select(r => r.Label).FirstOrDefaultAsync() ?? "");
        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
        ArchiveService.Snapshot(db, "lead", lead.Id, lead.FullName, lead.Phone ?? "", lead,
            reason.Length > 0 ? reason : null, actor);
        audit.Record("Lead", id, "delete",
            $"Lid o'chirildi ({lead.FullName})" + (reason.Length > 0 ? $" — sabab: {reason}" : ""));
        await db.SaveChangesAsync();
        // ⚠️ Lid o'chgach `SyncCardAsync` uni topa olmaydi (va Telegram `deleteMessage` 48 soatdan
        // keyin ishlamaydi) — shuning uchun karta MATNI "🗑 Lid o'chirildi" ga almashtiriladi va
        // bog'lovchi yozuvlar tozalanadi. Ism o'chirilgan obyektdan (xotirada) o'qiladi.
        await LeadNotifier.MarkDeletedAsync(db, telegram, id, lead.FullName, logger: logger);
        return NoContent();
    }

    // ---------- Hodisalar tarixi ----------

    [HttpGet("{id}/events")]
    public async Task<ActionResult<IEnumerable<LeadEventDto>>> Events(string id) =>
        await db.LeadEvents.Where(e => e.LeadId == id)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new LeadEventDto(e.Id, e.Type, e.Text, e.ActorName, e.CreatedAt))
            .ToListAsync();

    [HttpPost("{id}/events")]
    public async Task<IActionResult> AddEventEndpoint(string id, AddLeadEventRequest req)
    {
        if (await db.Leads.FindAsync(id) is null) return NotFound();
        AddEvent(id, string.IsNullOrWhiteSpace(req.Type) ? "note" : req.Type, req.Text);
        await db.SaveChangesAsync();
        // Yangi izoh kartaning "Oxirgi izohlar" blokida ko'rinadi.
        await LeadNotifier.SyncCardAsync(db, telegram, id, logger: logger);
        return Ok(new { ok = true });
    }

    // ---------- Sinov darslari ----------

    [HttpGet("{id}/trials")]
    public async Task<ActionResult<IEnumerable<TrialLessonDto>>> Trials(string id) =>
        await (from t in db.TrialLessons
               where t.LeadId == id
               join c in db.Classes on t.GroupId equals c.Id into gj
               from c in gj.DefaultIfEmpty()
               orderby t.ScheduledAt descending
               select new TrialLessonDto(t.Id, t.LeadId, t.GroupId, c != null ? c.Name : "", t.ScheduledAt, t.Result, t.CreatedAt, t.AttendedAt))
              .ToListAsync();

    /// <summary>Lid uchun sinov darsi belgilash (guruh + vaqt).</summary>
    [HttpPost("{id}/trials")]
    public async Task<IActionResult> ScheduleTrial(string id, ScheduleTrialRequest req)
    {
        if (await db.Leads.FindAsync(id) is null) return NotFound();
        var group = await db.Classes.FindAsync(req.GroupId);
        var trial = new TrialLesson
        {
            LeadId = id, GroupId = req.GroupId, ScheduledAt = req.ScheduledAt,
            Result = "pending", CreatedAt = Now(),
        };
        db.TrialLessons.Add(trial);
        AddEvent(id, "trial", $"Sinov darsi belgilandi: {group?.Name ?? req.GroupId} — {req.ScheduledAt}");
        await db.SaveChangesAsync();
        // Birinchi (sinov) dars sanasi kartaga chiqadi.
        await LeadNotifier.SyncCardAsync(db, telegram, id, logger: logger);
        return Ok(new { ok = true, trialId = trial.Id });
    }

    /// <summary>Lid sinov darsiga yozilganda chiqadigan chek (TO'LOVSIZ ro'yxat varaqasi):
    /// O'quvchi(lid)/Guruh/O'qituvchi/Sana-vaqt. Jami/to'lov turi bo'lmaydi (Total=null → frontend "Jami"ni
    /// ko'rsatmaydi). Chek sozlamalari (maydon tanlovi, sarlavha, footer) moliya cheki bilan bir xil.</summary>
    [HttpGet("trials/{trialId}/receipt")]
    public async Task<ActionResult<ReceiptDto>> TrialReceipt(string trialId)
    {
        var trial = await db.TrialLessons.FindAsync(trialId);
        if (trial is null) return NotFound();
        var lead = await db.Leads.FindAsync(trial.LeadId);
        var meta = await db.CenterMeta.FirstOrDefaultAsync();

        var groupName = "";
        var teacherName = "";
        if (!string.IsNullOrWhiteSpace(trial.GroupId) && await db.Classes.FindAsync(trial.GroupId) is { } grp)
        {
            groupName = grp.Name;
            if (!string.IsNullOrWhiteSpace(grp.TeacherId))
                teacherName = (await db.Teachers.FindAsync(grp.TeacherId))?.FullName ?? "";
        }
        // Sana/vaqt = sinov darsi vaqti ("yyyy-MM-ddTHH:mm" → "yyyy-MM-dd HH:mm").
        var dt = trial.ScheduledAt.Length >= 16 ? trial.ScheduledAt[..16].Replace('T', ' ') : trial.ScheduledAt;
        var responsible = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";

        return new ReceiptDto(
            FinanceController.ReceiptNumber(trial.Id), dt, lead?.FullName ?? "", teacherName,
            responsible, groupName, "", null, null, // Method/Comment/Total yo'q (to'lovsiz)
            meta?.Name ?? "", meta?.Phone ?? "", meta?.Address ?? "", meta?.LogoUrl ?? "",
            meta?.CheckSettings ?? "", Subtitle: "Sinov darsiga yozildi");
    }

    /// <summary>
    /// Sinov darsi natijasi: <c>came</c> (keldi) | <c>no_show</c> (kelmadi) | <c>stayed</c> (qoldi) |
    /// <c>left</c> (ketdi).
    ///
    /// <para>⚠️ "keldi/kelmadi" va "qoldi/ketdi" — IKKI BOSHQA savol: birinchisi lid sinovga
    /// TASHRIF BUYURDIMI, ikkinchisi tashrifdan keyin markazda QOLDIMI. Ilgari faqat ikkinchisi
    /// bor edi, ya'ni "keldi, lekin ketdi" bilan "umuman kelmadi" bir xil ko'rinardi.</para>
    ///
    /// <para>Eski chaqiruvchilar (faqat "stayed"/"left" yuboradiganlar) avvalgidek ishlaydi.</para>
    /// </summary>
    [HttpPatch("trials/{trialId}")]
    public async Task<IActionResult> SetTrialResult(string trialId, TrialResultRequest req)
    {
        var trial = await db.TrialLessons.FindAsync(trialId);
        if (trial is null) return NotFound();
        var result = (req.Result ?? "").Trim();
        if (!TrialResults.Contains(result))
            return BadRequest(new { message = "Noma'lum natija" });
        trial.Result = result;

        // ⚠️ KPI konversiyasi "sinovga YOZILDI" emas, "sinovga KELDI" bo'yicha o'lchanadi
        // (kiruvchi adminning asosiy ko'rsatkichi — `KPI-SPEC.md` §8, `trialCame`). Shuning
        // uchun KELGANLIK fakti alohida SANA bilan yoziladi: sinov bir oyda belgilanib,
        // keyingi oyda bo'lishi mumkin — natija esa qaysi OYda kelgani bo'yicha sanaladi.
        // "stayed"/"left" ham KELGAN hisoblanadi: ular tashrifdan KEYINGI qaror.
        if ((result is "came" or "stayed" or "left") && string.IsNullOrWhiteSpace(trial.AttendedAt))
            trial.AttendedAt = AppClock.Today.ToString("yyyy-MM-dd");

        AddEvent(trial.LeadId, "trial", $"Sinov darsi natijasi: {TrialResultLabel(result)}");
        await db.SaveChangesAsync();
        await LeadNotifier.SyncCardAsync(db, telegram, trial.LeadId, logger: logger);
        return Ok(new { ok = true });
    }

    /// <summary>Ruxsat etilgan sinov natijalari (`TrialLesson.Result`).</summary>
    private static readonly string[] TrialResults = { "pending", "came", "no_show", "stayed", "left" };

    private static string TrialResultLabel(string result) => result switch
    {
        "came" => "keldi",
        "no_show" => "kelmadi",
        "stayed" => "qoldi",
        "left" => "ketdi",
        _ => "kutilmoqda",
    };

    // ---------- Konversiya (lid -> o'quvchi) ----------

    /// <summary>Lidni o'quvchiga aylantirish: yangi Student yaratiladi, lid yopiladi (ConvertedStudentId).
    /// GroupId berilsa o'quvchi shu guruhga (StudentGroup) qo'shiladi.</summary>
    [HttpPost("{id}/convert")]
    public async Task<IActionResult> Convert(string id, ConvertLeadRequest req)
    {
        var lead = await db.Leads.FindAsync(id);
        if (lead is null) return NotFound();
        if (lead.ConvertedStudentId is not null)
            return BadRequest(new { message = "Lid allaqachon o'quvchiga aylantirilgan" });

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var enrollment = string.IsNullOrWhiteSpace(req.EnrollmentDate) ? today : req.EnrollmentDate!;
        Group? group = string.IsNullOrWhiteSpace(req.GroupId) ? null : await db.Classes.FindAsync(req.GroupId);

        var student = new Student
        {
            FullName = lead.FullName,
            Gender = lead.Gender,
            BirthDate = lead.BirthDate,
            // Student'da bitta ota-ona maydoni bor — otani asosiy qilamiz (bo'lmasa ona).
            ParentFullName = !string.IsNullOrWhiteSpace(lead.FatherFullName) ? lead.FatherFullName : lead.MotherFullName,
            ParentPhone = !string.IsNullOrWhiteSpace(lead.FatherPhone) ? lead.FatherPhone : lead.MotherPhone,
            EnrollmentDate = enrollment,
            ClassName = group?.Name ?? "",
            // Lidda tanlangan tashqi maktab (tuman + maktab) o'quvchiga ko'chadi.
            DistrictId = lead.DistrictId,
            SchoolId = lead.SchoolId,
        };
        db.Students.Add(student);

        // Sig'im tekshiruvi — ClassesController.AddMember bilan bir xil qoida: Capacity>0 va
        // O'RIN BAND QILGANLAR soni (active + trial, MUZLATILGANLAR sanalmaydi —
        // MembershipLifecycle.OccupiesSeat) yetib borgan bo'lsa, guruhga QO'SHILMAYDI
        // (lekin o'quvchi baribir yaratiladi).
        var groupFull = false;
        if (group is not null)
        {
            if (group.Capacity > 0 &&
                await db.StudentGroups.Where(sg => sg.GroupId == group.Id)
                    .CountAsync(MembershipLifecycle.OccupiesSeatExpr) >= group.Capacity)
            {
                groupFull = true;
            }
            else
            {
                db.StudentGroups.Add(new StudentGroup
                {
                    StudentId = student.Id, GroupId = group.Id, JoinedAt = enrollment, IsActive = true,
                    // Jurnaldagi avto-"keldi" qoidasi shu sanadan (enrollment orqaga sanalgan
                    // bo'lishi mumkin — undan oldingi darslar bo'sh qolishi kerak).
                    RecordedAt = AppClock.Today.ToString("yyyy-MM-dd"),
                });
            }
        }

        // Poyga holati (race condition) tekshiruvi: shu lid ustida parallel yuborilgan boshqa
        // /convert so'rovi bizdan oldin saqlagan bo'lishi mumkin — SaveChangesAsync'dan OLDIN qayta
        // (no-tracking) tekshiramiz va shunday bo'lsa bekor qilamiz (ikkilangan Student yaratmaslik uchun).
        var alreadyConverted = await db.Leads.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => l.ConvertedStudentId)
            .FirstOrDefaultAsync();
        if (alreadyConverted is not null)
            return BadRequest(new { message = "Lid allaqachon o'quvchiga aylantirilgan" });

        lead.ConvertedStudentId = student.Id;
        // SHARTNOMA IMZOLANDI. Lidni o'quvchiga aylantirish — kiruvchi admin uchun "shartnoma"
        // hodisasining YAGONA manbai (`KPI-SPEC.md` §8, `contracts`): boshqa hech qayerda
        // "shartnoma tuzildi" fakti yozilmaydi. Sana ATAYIN "yyyy-MM-dd" — KPI oy bo'yicha
        // guruhlaydi, soat/daqiqa kerak emas.
        lead.ClosedByUserId = UserId();
        lead.ClosedAt = today;
        // Mas'ul biriktirilmagan bo'lsa (bot yaratgan va hech kim ko'chirmagan lid) — shartnomani
        // yopgan odam ayni paytda uni ishlagan odam hisoblanadi.
        if (string.IsNullOrWhiteSpace(lead.AssigneeUserId)) lead.AssigneeUserId = UserId();
        AddEvent(id, "convert", $"O'quvchiga aylantirildi ({lead.FullName})" + (group is not null
            ? (groupFull ? $" — guruh to'lgan, qo'shilmadi: {group.Name}" : $" — guruh: {group.Name}")
            : ""));
        await db.SaveChangesAsync();
        // Kartada "✅ O'quvchi bo'ldi" qatori paydo bo'ladi.
        await LeadNotifier.SyncCardAsync(db, telegram, id, logger: logger);
        return Ok(new { studentId = student.Id, groupFull });
    }

    // ---------- CRM statistikasi ----------

    /// <summary>Lid formasi "Qiziqqan fani" ro'yxati uchun KURSLAR (Subject) nomlari. Kurslar bo'limi
    /// "schedule" ruxsatida — CRM xodimida u bo'lmasligi mumkin, shuning uchun "leads" ruxsati ostida
    /// shu yerda ochiladi. Yangi kurs yaratilsa — shu ro'yxatda avtomatik chiqadi.</summary>
    /// <summary>
    /// Lidning «Lid kiritish formasi» qo'shimcha savollariga bergan javoblari.
    ///
    /// <para>Alohida endpoint ATAYIN: javoblar lidlar RO'YXATIGA qo'shilsa har bir kanban
    /// yuklanishida keraksiz matn tashilardi, holbuki ular faqat bitta lid ochilganda kerak.
    /// (`GET /api/admin/lead-forms/submissions` bu yerda YARAMAYDI — u `leads.forms` ruxsati
    /// bilan yopiq va OMMAVIY forma arizalarini qaytaradi.)</para>
    /// </summary>
    [HttpGet("{id}/answers")]
    public async Task<ActionResult<IEnumerable<SurveyAnswerDto>>> Answers(string id)
    {
        var json = await db.Leads.AsNoTracking().Where(l => l.Id == id)
            .Select(l => l.AnswersJson).FirstOrDefaultAsync();
        if (json is null) return NotFound();
        return LeadFormService.ParseAnswers(json);
    }

    [HttpGet("courses")]
    public async Task<ActionResult<IEnumerable<string>>> Courses() =>
        await db.Subjects.AsNoTracking()
            .Where(s => s.Name != "")
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync();

    /// <summary>CRM statistikasi: jami, bosqich/manba/qiziqish fani bo'yicha, konversiya %, oylik dinamika.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<CrmStatsDto>> Stats()
    {
        var leads = await db.Leads.AsNoTracking().ToListAsync();
        var stages = await db.LeadStages.AsNoTracking().ToListAsync();
        var stageTitle = stages.ToDictionary(s => s.Id, s => s.Title);

        var total = leads.Count;
        var converted = leads.Count(l => l.ConvertedStudentId != null);
        var rate = total == 0 ? 0 : Math.Round(converted * 100.0 / total, 1);

        var byStage = leads.GroupBy(l => l.Stage)
            .Select(g => new CrmStatChartItemDto(stageTitle.GetValueOrDefault(g.Key, "—"), g.Count()))
            .ToList();
        var bySource = leads.GroupBy(l => string.IsNullOrWhiteSpace(l.Source) ? "Noma'lum" : l.Source)
            .Select(g => new CrmStatChartItemDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ToList();
        var monthly = leads.Where(l => l.CreatedAt.Length >= 7)
            .GroupBy(l => l.CreatedAt[..7])
            .OrderBy(g => g.Key)
            .Select(g => new CrmMonthlyDto(g.Key, g.Count(), g.Count(l => l.ConvertedStudentId != null)))
            .ToList();

        // QIZIQISH FANLARI (kurslar) bo'yicha. `Lead.InterestSubject` — odatda kurs NOMI (lid formasi,
        // daraja testi), lekin eski/ommaviy (landing) yozuvlarda ixtiyoriy matn yoki kurs id'si ham
        // bo'lishi mumkin. Shuning uchun avval kurs id'si → nomi, so'ng registr farqisiz kurs nomiga
        // moslaymiz; qolgani matn ko'rinishida (registr farqi bo'yicha birlashtirilib) ko'rsatiladi.
        var courses = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync();
        var courseById = courses.ToDictionary(c => c.Id, c => c.Name);
        var courseByName = courses
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .GroupBy(c => c.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Name.Trim());

        string InterestLabel(string? raw)
        {
            var v = (raw ?? "").Trim();
            if (v.Length == 0) return "Ko'rsatilmagan";
            if (courseById.TryGetValue(v, out var byId) && !string.IsNullOrWhiteSpace(byId)) return byId.Trim();
            if (courseByName.TryGetValue(v.ToLowerInvariant(), out var byName)) return byName;
            return v;
        }

        var byInterest = leads
            .Select(l => new { Label = InterestLabel(l.InterestSubject), Converted = l.ConvertedStudentId != null })
            .GroupBy(x => x.Label.ToLowerInvariant())
            .Select(g =>
            {
                // Ko'rsatiladigan nom: guruhdagi eng ko'p uchragan yozilishi (kurs nomi bo'lsa — o'zi).
                var label = g.GroupBy(x => x.Label).OrderByDescending(x => x.Count()).First().Key;
                var count = g.Count();
                var conv = g.Count(x => x.Converted);
                return new CrmInterestStatDto(label, count, conv,
                    count == 0 ? 0 : Math.Round(conv * 100.0 / count, 1));
            })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CrmStatsDto(total, converted, rate, byStage, bySource, monthly, byInterest);
    }

    /// <summary>
    /// VORONKA ANALITIKASI (amoCRM uslubidagi dashboard): bosqichlar bo'yicha yetib kelgan lidlar,
    /// manbalar kesimi va menejerlar kesimi. <c>from</c>/<c>to</c> — "yyyy-MM-dd" (ikkalasi ixtiyoriy,
    /// berilmasa hamma vaqt); filtr <c>Lead.CreatedAt</c> bo'yicha, chegara kunlari QO'SHIB olinadi.
    ///
    /// <para>Butun hisob-kitob <see cref="LeadAnalytics"/> da (sof funksiyalar, testlangan) — bu
    /// yerda faqat ma'lumot yuklanadi.</para>
    ///
    /// <para><b>HALOLLIK:</b> bosqichda o'tirish vaqti (<c>AvgHours</c>) va menejerlar kesimi lid
    /// hodisalaridagi <c>FromStage</c>/<c>ToStage</c>/<c>ActorUserId</c> maydonlariga tayanadi —
    /// ular ESKI yozuvlarda yo'q. Shuning uchun bu ikki bo'lim faqat tarix yozila boshlagandan
    /// keyingi davrni qamraydi; har bosqichda <c>Samples</c> qaytariladi (nechta haqiqiy o'lchov).
    /// Voronkaning o'zi (<c>Reached</c>) esa lidning JORIY bosqichiga ham qaraydi, shuning uchun
    /// eski lidlar bilan ham to'la ishlaydi.</para>
    /// </summary>
    [HttpGet("analytics")]
    public async Task<ActionResult<LeadAnalyticsDto>> Analytics(
        [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        var raw = await db.Leads.AsNoTracking()
            .Select(l => new { l.Id, l.Stage, l.Source, l.ConvertedStudentId, l.CreatedAt })
            .ToListAsync();

        // Davr filtri SHU YERDA ham qo'llanadi (`Build` ichida yana takrorlanadi, natija bir xil):
        // pul va kanal ma'lumoti faqat DAVRDAGI lidlar uchun yuklansin — aks holda hisobot
        // sahifasi butun bazani (barcha to'lovlar, barcha arizalar) ko'tarardi.
        var inRange = raw.Where(l => LeadAnalytics.InRange(l.CreatedAt ?? "", from, to)).ToList();
        var ids = inRange.Select(l => l.Id).ToList();

        // SOTUV: lid → o'quvchi → to'lov (vozvrat ayirilgan). Daraja testi va lid formalari
        // statistikasidagi bilan AYNAN bir xil zanjir — "to'ladi" so'zi bo'limlarda bir xil
        // ma'no anglatsin (`LeadOutcome`).
        var outcome = await LeadOutcome.BuildAsync(db, ids);

        // KANAL (qayerdan keldi). Qoida `LeadOrigins` da — "Formalar statistikasi" ham shundan
        // foydalanadi, ya'ni ikkala sahifadagi "qo'lda kiritilgan" bir xil hisoblanadi.
        var idSet = ids.ToHashSet(StringComparer.Ordinal);
        var manualIds = (await db.LeadEvents.AsNoTracking()
                .Where(e => e.Type == LeadAnalytics.TypeCreated && e.ActorUserId != null && e.ActorUserId != "")
                .Select(e => e.LeadId).Distinct().ToListAsync())
            .Where(idSet.Contains).ToHashSet(StringComparer.Ordinal);
        var formIds = (await db.LeadFormSubmissions.AsNoTracking()
                .Where(x => x.LeadId != "").Select(x => x.LeadId).Distinct().ToListAsync())
            .Where(idSet.Contains).ToHashSet(StringComparer.Ordinal);
        var testIds = (await db.LevelTestSubmissions.AsNoTracking()
                .Where(x => x.LeadId != "").Select(x => x.LeadId).Distinct().ToListAsync())
            .Where(idSet.Contains).ToHashSet(StringComparer.Ordinal);
        var igIds = (await db.IgConversations.AsNoTracking()
                .Where(x => x.LeadId != null).Select(x => x.LeadId!).Distinct().ToListAsync())
            .Where(idSet.Contains).ToHashSet(StringComparer.Ordinal);

        var leads = inRange
            .Select(l => new LeadAnalytics.LeadRow(
                l.Id, l.Stage ?? "", l.Source ?? "", l.ConvertedStudentId != null, l.CreatedAt ?? "",
                Paid: outcome.HasPaid(l.Id),
                Revenue: outcome.PaidTotal(l.Id),
                Origin: LeadOrigins.Classify(l.Id, manualIds, formIds, testIds, igIds)))
            .ToList();

        // Faqat bosqich/konversiyaga oid hodisalar kerak (izoh/qo'ng'iroq/sinov — voronkaga aloqasiz).
        var events = (await db.LeadEvents.AsNoTracking()
                .Where(e => e.Type == LeadAnalytics.TypeStage
                            || e.Type == LeadAnalytics.TypeCreated
                            || e.Type == LeadAnalytics.TypeConvert)
                .Select(e => new
                {
                    e.LeadId, e.Type, e.FromStage, e.ToStage, e.ActorUserId, e.ActorName, e.CreatedAt,
                })
                .ToListAsync())
            .Select(e => new LeadAnalytics.EventRow(
                e.LeadId, e.Type ?? "", e.FromStage ?? "", e.ToStage ?? "",
                e.ActorUserId, e.ActorName ?? "", e.CreatedAt ?? ""))
            .ToList();

        var stages = (await db.LeadStages.AsNoTracking()
                .Select(s => new { s.Id, s.Title, s.Color, s.Order }).ToListAsync())
            .Select(s => new LeadAnalytics.StageRow(s.Id, s.Title ?? "", s.Color ?? "", s.Order))
            .ToList();

        var sources = (await db.LeadSources.AsNoTracking()
                .Select(s => new { s.Id, s.Name }).ToListAsync())
            .Select(s => new LeadAnalytics.SourceRow(s.Id, s.Name ?? ""))
            .ToList();

        // Menejer ismi JORIY ro'yxatdan olinadi (hodisadagi ism eskirgan bo'lishi mumkin).
        var userNames = (await db.Users.AsNoTracking()
                .Select(u => new { u.Id, u.FullName }).ToListAsync())
            .ToDictionary(u => u.Id, u => u.FullName ?? "", StringComparer.Ordinal);

        return LeadAnalytics.Build(leads, events, stages, sources, from, to, userNames);
    }

    // ---------- Yordamchilar ----------

    /// <summary>
    /// Konvertilgan o'quvchilarning birinchi dars davomat holatini TO'PLAMLI hisoblaydi
    /// (avvalgi HAR lid uchun 3 ta so'rov o'rniga jami 4 ta so'rov). Natija: studentId →
    /// "attended" | "absent". Ro'yxatda BO'LMAGAN o'quvchi = "no-lesson" (default).
    /// Semantikasi eski har-lidli mantiq bilan AYNAN bir xil:
    /// 1. O'quvchi mavjud bo'lmasa → "no-lesson"
    /// 2. FAOL (IsActive && Status=="active") guruh(lar)i bo'lmasa → "no-lesson"
    /// 3. Shu guruhlarda o'tilgan (Conducted) birinchi dars (Date, keyin Period) bo'lmasa → "no-lesson"
    /// 4. Shu dars uchun JournalEntry yo'q → "no-lesson"; ReasonId to'la → "absent"; bo'sh → "attended".
    /// </summary>
    private async Task<Dictionary<string, string>> ComputeFirstLessonAttendanceAsync(List<string> studentIds)
    {
        var result = new Dictionary<string, string>();
        if (studentIds.Count == 0) return result;

        // 1) Mavjud o'quvchilar (ConvertedStudentId o'chirilgan o'quvchiga ishora qilishi mumkin).
        var existing = (await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id)).Select(s => s.Id).ToListAsync()).ToHashSet();

        // 2) Har o'quvchining FAOL (active) guruhlari.
        var sgRows = await db.StudentGroups.AsNoTracking()
            .Where(sg => studentIds.Contains(sg.StudentId) && sg.IsActive && sg.Status == "active")
            .Select(sg => new { sg.StudentId, sg.GroupId })
            .ToListAsync();
        var groupsByStudent = sgRows
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.GroupId).ToHashSet());

        // 3) Shu guruhlardagi o'tilgan (Conducted) darslar.
        var allGroupIds = sgRows.Select(x => x.GroupId).Distinct().ToList();
        var notes = allGroupIds.Count == 0
            ? new List<(string ClassId, string SubjectId, string Date, int Period)>()
            : (await db.LessonNotes.AsNoTracking()
                    .Where(n => allGroupIds.Contains(n.ClassId) && n.Conducted)
                    .Select(n => new { n.ClassId, n.SubjectId, n.Date, n.Period })
                    .ToListAsync())
                .Select(n => (n.ClassId, n.SubjectId, n.Date, n.Period)).ToList();
        var notesByGroup = notes.GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 4) Har o'quvchi uchun BIRINCHI dars (Date, keyin Period bo'yicha) — sanalar "yyyy-MM-dd"
        //    (ISO) formatda, shu sabab ordinal solishtirish = xronologik tartib (DB OrderBy bilan bir xil).
        var firstLessons = new Dictionary<string, (string ClassId, string SubjectId, string Date, int Period)>();
        foreach (var sid in existing)
        {
            if (!groupsByStudent.TryGetValue(sid, out var gids) || gids.Count == 0) continue;
            (string ClassId, string SubjectId, string Date, int Period)? first = null;
            foreach (var gid in gids)
            {
                if (!notesByGroup.TryGetValue(gid, out var list)) continue;
                foreach (var n in list)
                {
                    if (first is null
                        || string.CompareOrdinal(n.Date, first.Value.Date) < 0
                        || (n.Date == first.Value.Date && n.Period < first.Value.Period))
                        first = n;
                }
            }
            if (first is not null) firstLessons[sid] = first.Value;
        }
        if (firstLessons.Count == 0) return result;

        // 5) Shu birinchi darslar uchun jurnal yozuvlari (faqat aloqador o'quvchilar bo'yicha).
        var involved = firstLessons.Keys.ToList();
        var journalMap = new Dictionary<(string, string, string, string, int), string?>();
        var jRows = await db.JournalEntries.AsNoTracking()
            .Where(je => involved.Contains(je.StudentId))
            .Select(je => new { je.StudentId, je.ClassId, je.SubjectId, je.Date, je.Period, je.ReasonId })
            .ToListAsync();
        foreach (var je in jRows)
            journalMap[(je.StudentId, je.ClassId, je.SubjectId, je.Date, je.Period)] = je.ReasonId;

        foreach (var (sid, fl) in firstLessons)
        {
            if (journalMap.TryGetValue((sid, fl.ClassId, fl.SubjectId, fl.Date, fl.Period), out var reasonId))
                result[sid] = string.IsNullOrWhiteSpace(reasonId) ? "attended" : "absent";
            // topilmasa result'ga qo'shilmaydi → default "no-lesson".
        }
        return result;
    }

    private static string Now() => AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

    private string Actor() =>
        User.Identity?.Name
        ?? User.FindFirst("name")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
        ?? "Admin";

    /// <summary>Joriy foydalanuvchi id'si (<see cref="AppUser"/>.Id) — menejerlar kesimi uchun.
    /// Claim bo'lmasa null (analitikada bunday yozuvlar hisobga olinmaydi).</summary>
    private string? UserId()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>
    /// Lid tarixiga hodisa yozadi. <paramref name="fromStage"/>/<paramref name="toStage"/> — bosqich
    /// o'zgarishida TO'LDIRILADI: matn ("Bosqich: Yangi") odam uchun, bu ikki maydon esa voronka
    /// analitikasi uchun (<see cref="LeadAnalytics"/>). Chaqiruvchi bermasa — bo'sh qoladi.
    /// </summary>
    private void AddEvent(string leadId, string type, string text,
        string? fromStage = null, string? toStage = null) =>
        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = leadId, Type = type, Text = text, ActorName = Actor(), CreatedAt = Now(),
            FromStage = fromStage ?? "", ToStage = toStage ?? "", ActorUserId = UserId(),
        });
}
