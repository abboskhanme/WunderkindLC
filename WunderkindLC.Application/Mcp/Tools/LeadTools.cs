using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// CRM leads: list, card, board stages and the "CRM statistika" funnel.
/// Areas: <c>leads.list</c> (list, card, stages), <c>leads.stats</c> (funnel); money only with finance access.
///
/// <para>Definitions follow the panel (.claude/rules/crm-leads.md): a lead is OPEN when it is neither
/// converted nor lost (<c>DashboardSummary.LeadRow.IsOpen</c>); "lost" is <c>Lead.LostAt</c>, NOT a stage
/// (<c>LeadLoss.IsLost</c>); a lead whose stage was deleted sits in the FIRST column (kanban <c>stageOf</c>);
/// the next trial lesson is <c>LeadFirstLesson.Pick</c> and only for open leads (<c>LeadsController.GetAll</c>).</para>
/// </summary>
[McpServerToolType]
public sealed class LeadTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Lead), typeof(LeadStage), typeof(LeadSource), typeof(LeadEvent), typeof(TrialLesson),
        typeof(Group), typeof(Teacher), typeof(Subject), typeof(LevelTest), typeof(LevelTestSubmission),
        typeof(AppUser),
        // leads_funnel money (LeadOutcome) — only queried with finance access.
        typeof(FinanceTransaction), typeof(StudentGroup),
    ];

    /// <summary>Most events one lead card returns (newest first).</summary>
    public const int MaxEvents = 100;

    /// <summary>Most level-test submissions one lead card returns (newest first).</summary>
    public const int MaxLevelTests = 20;

    // =====================================================================================
    //  leads_list
    // =====================================================================================

    [McpServerTool(Name = "leads_list", Title = "Lidlar ro'yxati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("CRM lidlari ro'yxati (eng yangisi tepada): ism yoki telefon bo'yicha qidirish; holat (sukut — ochiq: "
        + "o'quvchiga aylantirilmagan va yo'qotilmagan | lost — yo'qotilgan | converted — o'quvchiga aylantirilgan | all), "
        + "bosqich, manba, mas'ul xodim, qiziqqan kurs va yaratilgan sana filtrlari. Har qatorda bosqich, manba, kurs, mas'ul, "
        + "takroriy murojaatlar soni, yo'qotish sababi, aylantirilgan o'quvchi id'si va kutilayotgan sinov darsi chiqadi. "
        + "Sahifalangan (ko'pi bilan 200 qator). List CRM leads with state, stage, source, assignee, course and date filters.")]
    public async Task<string> LeadsList(
        [Description("Ism (lid, ota yoki ona) yoki telefon bo'lagi (kamida 4 raqam)")] string? search = null,
        [Description("open (sukut) | lost | converted | all")] string? state = null,
        [Description("Bosqich id (lead_stages natijasidan)")] string? stageId = null,
        [Description("Manba nomi (lead_stages natijasidan); manbasiz lidlar uchun «unknown»")] string? source = null,
        [Description("Mas'ul xodim id (lid qatoridagi assignee.id); biriktirilmaganlar uchun «none»")] string? assigneeId = null,
        [Description("Qiziqqan kurs nomi yoki id (lead_stages → courses)")] string? course = null,
        [Description("Yaratilgan sana — dan, YYYY-MM-DD")] string? createdFrom = null,
        [Description("Yaratilgan sana — gacha (shu kun ham kiradi), YYYY-MM-DD")] string? createdTo = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Leads);
        var st = (state ?? "open").Trim().ToLowerInvariant();
        if (st is not ("open" or "lost" or "converted" or "all"))
            throw new McpException("«state»: open | lost | converted | all.");
        DateOnly? createdFromDate = string.IsNullOrWhiteSpace(createdFrom) ? null : McpToolContext.Date(createdFrom, default, "createdFrom");
        DateOnly? createdToDate = string.IsNullOrWhiteSpace(createdTo) ? null : McpToolContext.Date(createdTo, default, "createdTo");
        if (createdFromDate is { } f0 && createdToDate is { } t0 && f0 > t0)
            throw new McpException("«createdFrom» sanasi «createdTo» dan keyin bo'lishi mumkin emas.");

        var db = t.Db;
        var stages = await LoadStagesAsync(ct);

        var q = db.Leads.AsQueryable();
        q = st switch
        {
            "open" => q.Where(l => (l.ConvertedStudentId == null || l.ConvertedStudentId == "")
                                   && (l.LostAt == null || l.LostAt == "")),
            "lost" => q.Where(l => l.LostAt != null && l.LostAt != ""),
            "converted" => q.Where(l => l.ConvertedStudentId != null && l.ConvertedStudentId != ""),
            _ => q,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            if (digits.Length >= 4)
            {
                // Phones are stored in mixed formats ("+998 90 111-22-33", "998901112233"): strip the
                // separators in SQL and match the local part (last 9 digits at most).
                var d = digits.Length > 9 ? digits[^9..] : digits;
                q = q.Where(l => l.FullName.ToLower().Contains(term)
                                 || l.Phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Contains(d)
                                 || l.FatherPhone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Contains(d)
                                 || l.MotherPhone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Contains(d));
            }
            else
            {
                q = q.Where(l => l.FullName.ToLower().Contains(term) || l.FatherFullName.ToLower().Contains(term)
                                 || l.MotherFullName.ToLower().Contains(term));
            }
        }

        if (!string.IsNullOrWhiteSpace(stageId))
        {
            var sid = stageId.Trim();
            if (!stages.Any(s => s.Id == sid))
                throw new McpException("«stageId» topilmadi — lead_stages natijasidagi bosqich id'sini bering.");
            if (stages[0].Id == sid)
            {
                // Kanban: a lead whose stage was deleted is drawn in the FIRST column.
                var known = stages.Select(s => s.Id).ToList();
                q = q.Where(l => l.Stage == sid || !known.Contains(l.Stage));
            }
            else q = q.Where(l => l.Stage == sid);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var s = source.Trim();
            if (s.Equals("unknown", StringComparison.OrdinalIgnoreCase) || s.Equals("noma'lum", StringComparison.OrdinalIgnoreCase))
                q = q.Where(l => l.Source.Trim() == "");
            else
            {
                // Lead.Source holds the source NAME; old/imported rows may hold a LeadSource id (GetAll shows its name).
                var catalogue = await db.LeadSources.Select(x => new { x.Id, x.Name }).ToListAsync(ct);
                var lower = s.ToLowerInvariant();
                var ids = catalogue.Where(x => x.Id == s || (x.Name ?? "").Trim().ToLowerInvariant() == lower)
                    .Select(x => x.Id).ToList();
                var names = catalogue.Where(x => x.Id == s).Select(x => (x.Name ?? "").Trim().ToLowerInvariant())
                    .Append(lower).Distinct().ToList();
                q = q.Where(l => ids.Contains(l.Source) || names.Contains(l.Source.Trim().ToLower()));
            }
        }

        if (!string.IsNullOrWhiteSpace(assigneeId))
        {
            var a = assigneeId.Trim();
            q = a.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? q.Where(l => l.AssigneeUserId == null || l.AssigneeUserId == "")
                : q.Where(l => l.AssigneeUserId == a);
        }

        if (!string.IsNullOrWhiteSpace(course))
        {
            // The panel's KURS column (CourseLabel): InterestSubject (course name, or a course id on old rows);
            // empty → the course of the lead's trial-lesson group.
            var c = course.Trim();
            var lower = c.ToLowerInvariant();
            var courseIds = await db.Subjects.Where(x => x.Id == c || x.Name.Trim().ToLower() == lower)
                .Select(x => x.Id).ToListAsync(ct);
            var courseNames = await db.Subjects.Where(x => x.Id == c).Select(x => x.Name.Trim().ToLower()).ToListAsync(ct);
            courseNames.Add(lower);
            var groupIds = courseIds.Count == 0
                ? new List<string>()
                : await db.Classes.Where(g => courseIds.Contains(g.CourseId)).Select(g => g.Id).ToListAsync(ct);
            q = q.Where(l => courseIds.Contains(l.InterestSubject)
                             || courseNames.Contains(l.InterestSubject.Trim().ToLower())
                             || (l.InterestSubject.Trim() == ""
                                 && db.TrialLessons.Any(x => x.LeadId == l.Id && groupIds.Contains(x.GroupId))));
        }

        if (createdFromDate is { } fd)
        {
            var fromIso = McpToolContext.Iso(fd);
            q = q.Where(l => string.Compare(l.CreatedAt, fromIso) >= 0);
        }
        if (createdToDate is { } td)
        {
            var toExcl = McpToolContext.Iso(td.AddDays(1));   // the whole "to" day is included
            q = q.Where(l => string.Compare(l.CreatedAt, toExcl) < 0);
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id).Skip((p - 1) * size).Take(size)
            .Select(l => new LeadCore(
                l.Id, l.FullName, l.Phone, l.Stage, l.Source, l.InterestSubject, l.AssigneeUserId, l.CreatedAt,
                l.RepeatCount, l.LastRepeatAt, l.ConvertedStudentId, l.LostAt, l.LostReason))
            .ToListAsync(ct);

        var ctx = await LoadContextAsync(rows.Select(r => r.Id).ToList(), ct);
        var users = await t.UserNamesAsync(rows.Select(r => r.AssigneeUserId), ct);
        var today = McpToolContext.Iso(McpToolContext.Today);

        var items = rows.Select(r => RowOf(r, stages, ctx, users, today)).ToList();
        return t.Json(new { total, page = p, pageSize = size, state = st, items }, items.Count);
    }

    // =====================================================================================
    //  lead_detail
    // =====================================================================================

    [McpServerTool(Name = "lead_detail", Title = "Lid kartochkasi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta lid haqida to'liq ma'lumot: kontakt (ota-ona telefonlari bilan), bosqich, manba, kurs, mas'ul, daraja, "
        + "yo'qotilgan bo'lsa sabab va izoh, hodisalar tarixi (kim, qachon, nima — eng yangisi tepada, ko'pi bilan 100 ta), "
        + "sinov darslari (sana, guruh, o'qituvchi, natija), daraja testi natijalari va «Lid kiritish formasi» qo'shimcha "
        + "savollariga javoblar. id, telefon yoki ism bo'yicha topiladi — bir nechta mos kelsa nomzodlar ro'yxati qaytadi. "
        + "Full lead card with history, trial lessons and level tests, by id, phone or name.")]
    public async Task<string> LeadDetail(
        [Description("Lid id (leads_list natijasidan)")] string? leadId = null,
        [Description("Yoki telefon raqami (lid, ota yoki ona; kamida 7 raqam)")] string? phone = null,
        [Description("Yoki ism (to'liq yoki bo'lagi)")] string? name = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Leads);
        var db = t.Db;
        Lead? lead;
        if (!string.IsNullOrWhiteSpace(leadId))
        {
            var id = leadId.Trim();
            lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        }
        else if (!string.IsNullOrWhiteSpace(phone) || !string.IsNullOrWhiteSpace(name))
        {
            var q = db.Leads.AsQueryable();
            if (!string.IsNullOrWhiteSpace(phone))
            {
                var digits = new string(phone.Where(char.IsDigit).ToArray());
                if (digits.Length < 7) throw new McpException("Telefon raqamida kamida 7 ta raqam bo'lsin.");
                var d = digits.Length > 9 ? digits[^9..] : digits;
                q = q.Where(l => l.Phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Contains(d)
                                 || l.FatherPhone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Contains(d)
                                 || l.MotherPhone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Contains(d));
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                var term = name.Trim().ToLower();
                q = q.Where(l => l.FullName.ToLower().Contains(term));
            }
            var matches = await q.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id).Take(11)
                .Select(l => new { l.Id, l.FullName, l.Phone, l.CreatedAt, l.ConvertedStudentId, l.LostAt })
                .ToListAsync(ct);
            if (matches.Count > 1)
                return t.Json(new
                {
                    message = "Bir nechta lid mos keldi — leadId bilan qayta so'rang.",
                    candidates = matches.Take(10).Select(m => new
                    {
                        m.Id, m.FullName, m.Phone, m.CreatedAt, state = StateOf(m.ConvertedStudentId, m.LostAt),
                    }),
                }, Math.Min(matches.Count, 10));
            var only = matches.FirstOrDefault();
            lead = only is null ? null : await db.Leads.FirstOrDefaultAsync(l => l.Id == only.Id, ct);
        }
        else throw new McpException("leadId, phone yoki name kerak.");

        if (lead is null) return t.Json(new { message = "Lid topilmadi." }, 0);

        var stages = await LoadStagesAsync(ct);
        var ctx = await LoadContextAsync([lead.Id], ct);
        var today = McpToolContext.Iso(McpToolContext.Today);

        var teacherIds = ctx.Groups.Values.Select(g => g.TeacherId).Where(x => x.Length > 0).Distinct().ToList();
        var teachers = teacherIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : (await db.Teachers.Where(x => teacherIds.Contains(x.Id)).Select(x => new { x.Id, x.FullName }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.FullName ?? "", StringComparer.Ordinal);

        var eventsTotal = await db.LeadEvents.CountAsync(e => e.LeadId == lead.Id, ct);
        var events = await db.LeadEvents.Where(e => e.LeadId == lead.Id)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(MaxEvents)
            .Select(e => new { e.Type, e.Text, actor = e.ActorName, at = e.CreatedAt })
            .ToListAsync(ct);

        var tests = await (from s in db.LevelTestSubmissions
                           where s.LeadId == lead.Id
                           join lt in db.LevelTests on s.TestId equals lt.Id into tj
                           from lt in tj.DefaultIfEmpty()
                           orderby s.CreatedAt descending, s.Id
                           select new
                           {
                               at = s.CreatedAt, test = lt != null ? lt.Title : "",
                               s.Score, s.Total, s.Percent, level = s.Level,
                           })
            .Take(MaxLevelTests).ToListAsync(ct);

        var users = await t.UserNamesAsync([lead.AssigneeUserId, lead.ClosedByUserId], ct);

        var core = new LeadCore(
            lead.Id, lead.FullName, lead.Phone, lead.Stage, lead.Source, lead.InterestSubject, lead.AssigneeUserId,
            lead.CreatedAt, lead.RepeatCount, lead.LastRepeatAt, lead.ConvertedStudentId, lead.LostAt, lead.LostReason);
        var row = RowOf(core, stages, ctx, users, today);

        var trials = ctx.Trials[lead.Id].OrderByDescending(x => x.ScheduledAt, StringComparer.Ordinal).Select(x =>
        {
            var g = ctx.Groups.GetValueOrDefault(x.GroupId);
            return new
            {
                at = x.ScheduledAt, groupId = x.GroupId, groupName = g?.Name,
                teacher = g is null || g.TeacherId.Length == 0 ? null : teachers.GetValueOrDefault(g.TeacherId),
                course = g is null ? null : ctx.CourseNames.GetValueOrDefault(g.CourseId),
                result = x.Result, attended = LeadTrialRules.IsCame(x.Result),
            };
        }).ToList();

        var answers = LeadFormService.ParseAnswers(lead.AnswersJson)
            .Select(a => new { question = a.Question, answers = a.Answers }).ToList();

        // KURS DARAJASI: the value stored on the lead wins, else the latest level-test result (GetAll).
        var level = !string.IsNullOrWhiteSpace(lead.Level)
            ? lead.Level
            : tests.Where(x => !string.IsNullOrWhiteSpace(x.level)).Select(x => x.level).FirstOrDefault();

        return t.Json(new
        {
            lead = row,
            contact = new
            {
                lead.Gender, lead.BirthDate,
                father = new { name = lead.FatherFullName, phone = lead.FatherPhone },
                mother = new { name = lead.MotherFullName, phone = lead.MotherPhone },
            },
            lead.Note,
            level = string.IsNullOrWhiteSpace(level) ? null : level,
            lostDetail = string.IsNullOrWhiteSpace(lead.LostAt) ? null : new
            {
                at = lead.LostAt, reason = lead.LostReason, comment = lead.LostComment, byName = lead.LostByName,
            },
            closed = string.IsNullOrWhiteSpace(lead.ClosedAt) ? null : new
            {
                at = lead.ClosedAt,
                by = string.IsNullOrWhiteSpace(lead.ClosedByUserId) ? null : users.GetValueOrDefault(lead.ClosedByUserId!),
            },
            entryFormAnswers = answers,
            trials,
            levelTests = tests,
            eventsTotal,
            eventsShown = events.Count,
            events,
        }, 1);
    }

    // =====================================================================================
    //  leads_funnel
    // =====================================================================================

    [McpServerTool(Name = "leads_funnel", Title = "CRM statistika (voronka)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("CRM statistikasi tanlangan davrda KELGAN (yaratilgan) lidlar bo'yicha: jami, o'quvchiga aylangan va konversiya %; "
        + "bosqichlar voronkasi kanban tartibida (hozir shu bosqichda nechta, shu bosqichga yetib kelganlar, bosqichda o'rtacha "
        + "soat); manbalar; sinov darsi zanjiri (lid → sinovga yozildi → sinovga keldi → o'quvchi); yo'qotilganlar soni va "
        + "sabablari; kurslar kesimi; menejerlar kesimi. To'lov qilganlar va tushum faqat moliya ruxsati bo'lsa. "
        + "Davr sukut bo'yicha oxirgi 30 kun, ko'pi bilan 366 kun. CRM funnel for leads created in a date range.")]
    public async Task<string> LeadsFunnel(
        [Description("Davr boshi YYYY-MM-DD (sukut — 30 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun; shu kun ham kiradi)")] string? to = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.LeadStats);
        var (f, tt) = McpToolContext.Range(from, to);
        var fromIso = McpToolContext.Iso(f);
        var toIso = McpToolContext.Iso(tt);
        var toExcl = McpToolContext.Iso(tt.AddDays(1));
        var finance = t.CanReadFinance;
        var db = t.Db;

        // Period = the day the lead CAME (LeadAnalytics.InRange); pre-filtered in SQL, then the exact rule in memory.
        var inRange = db.Leads.Where(l => string.Compare(l.CreatedAt, fromIso) >= 0 && string.Compare(l.CreatedAt, toExcl) < 0);
        var leads = (await inRange
                .Select(l => new
                {
                    l.Id, l.Stage, l.Source, l.ConvertedStudentId, l.CreatedAt, l.InterestSubject, l.AssigneeUserId,
                    l.LostAt, l.LostReason,
                })
                .ToListAsync(ct))
            .Where(l => LeadAnalytics.InRange(l.CreatedAt, fromIso, toIso))
            .ToList();
        var ids = leads.Select(l => l.Id).ToList();
        var idSet = ids.ToHashSet(StringComparer.Ordinal);

        // Only stage/convert history matters for the funnel (LeadsController.Analytics), and only of these leads.
        var events = (await (from e in db.LeadEvents
                             join l in inRange on e.LeadId equals l.Id
                             where e.Type == LeadAnalytics.TypeStage || e.Type == LeadAnalytics.TypeCreated
                                   || e.Type == LeadAnalytics.TypeConvert
                             select new { e.LeadId, e.Type, e.FromStage, e.ToStage, e.ActorUserId, e.ActorName, e.CreatedAt })
                .ToListAsync(ct))
            .Where(e => idSet.Contains(e.LeadId))
            .Select(e => new LeadAnalytics.EventRow(
                e.LeadId, e.Type ?? "", e.FromStage ?? "", e.ToStage ?? "", e.ActorUserId, e.ActorName ?? "", e.CreatedAt ?? ""))
            .ToList();

        var stageRows = (await LoadStagesAsync(ct))
            .Select(s => new LeadAnalytics.StageRow(s.Id, s.Title, s.Color, s.Order)).ToList();
        var sourceRows = (await db.LeadSources.Select(s => new { s.Id, s.Name }).ToListAsync(ct))
            .Select(s => new LeadAnalytics.SourceRow(s.Id, s.Name ?? "")).ToList();

        // Money: lead → student → tuition payments minus refunds (LeadOutcome) — only with finance access.
        var outcome = finance && ids.Count > 0 ? await LeadOutcome.BuildAsync(db, ids) : null;

        var users = await t.UserNamesAsync(events.Select(e => e.ActorUserId).Concat(leads.Select(l => l.AssigneeUserId)), ct);

        var analytics = LeadAnalytics.Build(
            leads.Select(l => new LeadAnalytics.LeadRow(
                l.Id, l.Stage ?? "", l.Source ?? "", l.ConvertedStudentId != null, l.CreatedAt ?? "",
                Paid: outcome?.HasPaid(l.Id) ?? false,
                Revenue: outcome is null ? 0m : Math.Max(0m, outcome.PaidTotal(l.Id)))),
            events, stageRows, sourceRows, fromIso, toIso, users);

        // Trial-lesson chain: lead → trial scheduled → came to the trial (LeadTrialRules.IsCame) → student.
        var trialRows = (await (from x in db.TrialLessons
                                join l in inRange on x.LeadId equals l.Id
                                select new { x.LeadId, x.Result })
                .ToListAsync(ct))
            .Where(x => idSet.Contains(x.LeadId)).ToList();
        var trialScheduled = trialRows.Select(x => x.LeadId).Distinct().Count();
        var trialAttended = trialRows.Where(x => LeadTrialRules.IsCame(x.Result)).Select(x => x.LeadId).Distinct().Count();

        // Lost leads: the "Yo'qotilgan lidlar" report (LeadLossReport), same period rule.
        var loss = LeadLossReport.Build(
            leads.Select(l => new LeadLossReport.Row(
                l.Id, "", l.CreatedAt ?? "", l.Source ?? "", l.AssigneeUserId, l.ConvertedStudentId != null,
                l.LostAt, l.LostReason, null, null)),
            fromIso, toIso);

        // Current stage of the period's leads (CRM statistika "Bosqich bo'yicha lidlar"); lost leads stay in their stage.
        var known = stageRows.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var current = leads.GroupBy(l => l.Stage ?? "", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (All: g.Count(), Lost: g.Count(x => !string.IsNullOrWhiteSpace(x.LostAt))),
                StringComparer.Ordinal);
        var titleOf = stageRows.ToDictionary(s => s.Id, s => s.Title, StringComparer.Ordinal);

        var total = analytics.Total;
        var courses = await CourseStatsAsync(leads.Select(l => (l.InterestSubject, l.ConvertedStudentId != null)), ct);

        return t.Json(new
        {
            from = fromIso, to = toIso,
            total,
            converted = analytics.Converted,
            conversionRate = analytics.ConversionRate,
            paid = finance ? analytics.Paid : (int?)null,
            revenue = finance ? analytics.Revenue : (decimal?)null,
            payRate = finance ? analytics.PayRate : (int?)null,
            note = finance ? null : "To'lov qilganlar va tushum ko'rsatilmadi: moliya ruxsati yo'q.",
            conversion = new
            {
                leads = total,
                trialScheduled, trialScheduledPct = Percent(trialScheduled, total),
                trialAttended, trialAttendedPct = Percent(trialAttended, total),
                converted = analytics.Converted, convertedPct = analytics.ConversionRate,
            },
            stages = analytics.Funnel.Select(s => new
            {
                stageId = s.StageId, s.Title, s.Color, s.Order,
                currentCount = current.TryGetValue(s.StageId, out var c) ? c.All : 0,
                currentLost = current.TryGetValue(s.StageId, out var c2) ? c2.Lost : 0,
                reached = s.Reached, reachedPct = s.Pct,
                avgHoursInStage = s.AvgHours, avgHoursSamples = s.Samples,
            }),
            leadsInDeletedStage = leads.Count(l => !known.Contains(l.Stage ?? "")),
            sources = analytics.Sources
                .GroupBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { label = g.First().Label, count = g.Sum(x => x.Count) })
                .OrderByDescending(x => x.count).ThenBy(x => x.label, StringComparer.OrdinalIgnoreCase)
                .Select(x => new { x.label, x.count, pct = Percent(x.count, total) }),
            lost = new
            {
                count = loss.Lost, rate = loss.LostRate,
                byReason = loss.ByReason.Select(r => new { reason = r.Label, r.Count, share = r.Share }),
            },
            courses,
            managers = analytics.Managers.Select(m => new
            {
                userId = m.UserId, m.Name, m.Moves, m.Leads, m.Created, won = m.Won,
                paid = finance ? m.Paid : (int?)null,
                revenue = finance ? m.Revenue : (decimal?)null,
                stages = m.Stages.Where(x => x.Reached > 0)
                    .Select(x => new { stage = titleOf.GetValueOrDefault(x.StageId, x.StageId), x.Reached }),
            }),
            caveat = "Bosqichda o'rtacha vaqt va menejerlar kesimi faqat hodisalarda bajaruvchi/bosqich yozila boshlagan "
                     + "davrdan (2026-08) keyingi ishni qamraydi. Lid kanallari (forma/test/Instagram) bu yerda yo'q.",
        }, total);
    }

    // =====================================================================================
    //  lead_stages
    // =====================================================================================

    [McpServerTool(Name = "lead_stages", Title = "Lid bosqichlari va manbalari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kanban bosqichlari tartibi bilan (id, nomi, rangi, birinchi ustunmi, ustundagi kartalar va ochiq lidlar soni), "
        + "doimiy «Yo'qotilgan» tizim ustuni, lid manbalari ma'lumotnomasi va «Qiziqqan kurs» ro'yxati. leads_list filtrlari "
        + "uchun id va nomlar shu yerdan olinadi. Lead board stages, the sources catalogue and course names.")]
    public async Task<string> LeadStages(CancellationToken ct = default)
    {
        t.Require(McpAreas.Leads);
        var db = t.Db;
        var stages = await LoadStagesAsync(ct);

        // Kanban column = every NOT-lost lead in its stage (converted ones too); lost leads only in the lost column.
        var cards = await db.Leads.Where(l => l.LostAt == null || l.LostAt == "")
            .GroupBy(l => l.Stage).Select(g => new { Stage = g.Key, Count = g.Count() }).ToListAsync(ct);
        var open = await db.Leads.Where(l => (l.LostAt == null || l.LostAt == "")
                                             && (l.ConvertedStudentId == null || l.ConvertedStudentId == ""))
            .GroupBy(l => l.Stage).Select(g => new { Stage = g.Key, Count = g.Count() }).ToListAsync(ct);
        var lost = await db.Leads.CountAsync(l => l.LostAt != null && l.LostAt != "", ct);

        var known = stages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        int CountIn(IEnumerable<(string Stage, int Count)> rows, StageInfo s, bool first) =>
            rows.Where(r => r.Stage == s.Id || (first && !known.Contains(r.Stage))).Sum(r => r.Count);
        var cardRows = cards.Select(x => (x.Stage ?? "", x.Count)).ToList();
        var openRows = open.Select(x => (x.Stage ?? "", x.Count)).ToList();

        var sources = await db.LeadSources.OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Order }).ToListAsync(ct);
        var courses = await db.Subjects.Where(s => s.Name != "").OrderBy(s => s.Name).Select(s => s.Name).ToListAsync(ct);

        return t.Json(new
        {
            stages = stages.Select((s, i) => new
            {
                s.Id, s.Title, s.Color, s.Order,
                isFirst = i == 0,
                cards = CountIn(cardRows, s, i == 0),
                openLeads = CountIn(openRows, s, i == 0),
            }),
            lostColumn = new
            {
                id = "__lost__", title = "Yo'qotilgan", isSystem = true, cards = lost,
                note = "Tizim ustuni: yo'qotilgan lid bosqich emas — u o'z bosqichini saqlaydi va faqat shu ustunda ko'rinadi.",
            },
            note = "O'chirilgan bosqichdagi lidlar birinchi ustunda ko'rsatiladi (kanbandagidek).",
            sources,
            courses,
        }, stages.Count + sources.Count);
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

    private sealed record StageInfo(string Id, string Title, string Color, int Order);

    private sealed record GroupInfo(string Name, string TeacherId, string CourseId);

    private sealed record LeadCore(
        string Id, string FullName, string Phone, string Stage, string Source, string InterestSubject,
        string? AssigneeUserId, string CreatedAt, int RepeatCount, string LastRepeatAt,
        string? ConvertedStudentId, string? LostAt, string? LostReason);

    private sealed record Context(
        ILookup<string, LeadFirstLesson.TrialRow> Trials,
        Dictionary<string, GroupInfo> Groups,
        Dictionary<string, string> CourseNames,
        Dictionary<string, string> SourceNames);

    /// <summary>Board order (LeadStagesController.GetAll: by Order; ties by title for determinism).</summary>
    private async Task<List<StageInfo>> LoadStagesAsync(CancellationToken ct) =>
        await t.Db.LeadStages.OrderBy(s => s.Order).ThenBy(s => s.Title).ThenBy(s => s.Id)
            .Select(s => new StageInfo(s.Id, s.Title, s.Color, s.Order)).ToListAsync(ct);

    /// <summary>Trials of the given leads, their groups, course and source names (LeadsController.LoadTrialContextAsync).</summary>
    private async Task<Context> LoadContextAsync(List<string> leadIds, CancellationToken ct)
    {
        var db = t.Db;
        var trials = leadIds.Count == 0
            ? new List<LeadFirstLesson.TrialRow>()
            : (await db.TrialLessons.Where(x => leadIds.Contains(x.LeadId))
                    .Select(x => new { x.Id, x.LeadId, x.GroupId, x.Result, x.ScheduledAt }).ToListAsync(ct))
                .Select(x => new LeadFirstLesson.TrialRow(x.Id, x.LeadId, x.GroupId ?? "", x.Result ?? "", x.ScheduledAt ?? ""))
                .ToList();
        var groupIds = trials.Select(x => x.GroupId).Where(x => x.Length > 0).Distinct().ToList();
        var groups = groupIds.Count == 0
            ? new Dictionary<string, GroupInfo>(StringComparer.Ordinal)
            : (await db.Classes.Where(g => groupIds.Contains(g.Id))
                    .Select(g => new { g.Id, g.Name, g.TeacherId, g.CourseId }).ToListAsync(ct))
                .ToDictionary(g => g.Id, g => new GroupInfo(g.Name ?? "", g.TeacherId ?? "", g.CourseId ?? ""),
                    StringComparer.Ordinal);
        var courseNames = (await db.Subjects.Select(s => new { s.Id, s.Name }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => s.Name ?? "", StringComparer.Ordinal);
        var sourceNames = (await db.LeadSources.Select(s => new { s.Id, s.Name }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => s.Name ?? "", StringComparer.Ordinal);
        return new Context(trials.ToLookup(x => x.LeadId, StringComparer.Ordinal), groups, courseNames, sourceNames);
    }


    private static object RowOf(
        LeadCore l, List<StageInfo> stages, Context ctx, Dictionary<string, string> users, string today)
    {
        var trials = ctx.Trials[l.Id].ToList();
        var shown = LeadFirstLesson.Display(trials, today);
        var shownGroup = shown is { } s ? ctx.Groups.GetValueOrDefault(s.GroupId) : null;
        var state = StateOf(l.ConvertedStudentId, l.LostAt);
        // The upcoming first (trial) lesson — only for an OPEN lead (GetAll / dashboard card: one definition).
        var first = state == "open" ? LeadFirstLesson.Pick(trials, today) : null;
        var firstGroup = first is { } fl ? ctx.Groups.GetValueOrDefault(fl.GroupId) : null;
        var stage = stages.FirstOrDefault(x => x.Id == l.Stage) ?? stages.FirstOrDefault();
        var src = (l.Source ?? "").Trim();
        var source = src.Length == 0 ? null
            : ctx.SourceNames.TryGetValue(src, out var sn) && !string.IsNullOrWhiteSpace(sn) ? sn : src;
        var course = CourseLabel(l.InterestSubject, shownGroup?.CourseId, ctx.CourseNames);

        return new
        {
            l.Id, l.FullName, l.Phone, state,
            stage = stage is null ? null : new { stage.Id, stage.Title },
            source,
            interestCourse = course.Length == 0 ? null : course,
            assignee = string.IsNullOrWhiteSpace(l.AssigneeUserId) ? null : new
            {
                id = l.AssigneeUserId, name = users.GetValueOrDefault(l.AssigneeUserId!),
            },
            l.CreatedAt,
            l.RepeatCount,
            lastRepeatAt = string.IsNullOrWhiteSpace(l.LastRepeatAt) ? null : l.LastRepeatAt,
            lost = string.IsNullOrWhiteSpace(l.LostAt) ? null : new { at = l.LostAt, reason = l.LostReason },
            convertedStudentId = string.IsNullOrWhiteSpace(l.ConvertedStudentId) ? null : l.ConvertedStudentId,
            nextTrialLesson = first is not { } nt ? null : new
            {
                at = nt.ScheduledAt, groupId = nt.GroupId, groupName = firstGroup?.Name,
                isPast = LeadFirstLesson.IsPast(nt.ScheduledAt, today),
            },
        };
    }

    /// <summary>open | lost | converted — converted wins (a lost lead cannot be converted; the panel refuses it).</summary>
    private static string StateOf(string? convertedStudentId, string? lostAt) =>
        !string.IsNullOrWhiteSpace(convertedStudentId) ? "converted"
        : !string.IsNullOrWhiteSpace(lostAt) ? "lost"
        : "open";

    /// <summary>
    /// The KURS column of the leads table (LeadsController.CourseLabel, reproduced — private there):
    /// InterestSubject (course name; a course id on old rows → its name), empty → the trial group's course.
    /// </summary>
    private static string CourseLabel(string? interest, string? groupCourseId, Dictionary<string, string> courseNames)
    {
        var v = (interest ?? "").Trim();
        if (v.Length > 0)
            return courseNames.TryGetValue(v, out var byId) && !string.IsNullOrWhiteSpace(byId) ? byId.Trim() : v;
        return !string.IsNullOrWhiteSpace(groupCourseId) && courseNames.TryGetValue(groupCourseId, out var n) ? n.Trim() : "";
    }

    /// <summary>
    /// "Qiziqish fanlari bo'yicha lidlar" (LeadsController.Stats → ByInterest, reproduced — the logic lives only in
    /// the controller): course id → name, then a case-insensitive course-name match, empty = "Ko'rsatilmagan".
    /// Here it is limited to the period's leads.
    /// </summary>
    private async Task<List<object>> CourseStatsAsync(
        IEnumerable<(string? Interest, bool Converted)> leads, CancellationToken ct)
    {
        var db = t.Db;
        var courses = await db.Subjects.Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var courseById = courses.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        var courseByName = courses.Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .GroupBy(c => c.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Name.Trim());

        string Label(string? raw)
        {
            var v = (raw ?? "").Trim();
            if (v.Length == 0) return "Ko'rsatilmagan";
            if (courseById.TryGetValue(v, out var byId) && !string.IsNullOrWhiteSpace(byId)) return byId.Trim();
            if (courseByName.TryGetValue(v.ToLowerInvariant(), out var byName)) return byName;
            return v;
        }

        return leads
            .Select(l => new { Label = Label(l.Interest), l.Converted })
            .GroupBy(x => x.Label.ToLowerInvariant())
            .Select(g =>
            {
                var label = g.GroupBy(x => x.Label).OrderByDescending(x => x.Count()).First().Key;
                var count = g.Count();
                var conv = g.Count(x => x.Converted);
                return new
                {
                    course = label, leads = count, converted = conv,
                    conversionRate = count == 0 ? 0 : Math.Round(conv * 100.0 / count, 1),
                };
            })
            .OrderByDescending(x => x.leads).ThenBy(x => x.course, StringComparer.OrdinalIgnoreCase)
            .Cast<object>()
            .ToList();
    }

    /// <summary>Rounded percent 0..100 (LeadAnalytics.Percent).</summary>
    private static int Percent(int part, int total) => total == 0 ? 0 : (int)Math.Round(part * 100.0 / total);
}
