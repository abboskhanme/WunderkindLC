using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Baholash MEZONLARI (kriteriyalar): mezon CRUD + guruhga biriktirish + guruh ichida
/// o'quvchilarni mezonlar bo'yicha baholash. Har guruhga boshqa-boshqa mezonlar biriktiriladi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule.grading")]
[Route("api/admin/grading")]
public class GradingController(AppDbContext db, DataCache dataCache) : ControllerBase
{
    // ---------- Mezonlar (pul) ----------

    [HttpGet("criteria")]
    public async Task<ActionResult<IEnumerable<GradingCriterionDto>>> Criteria() =>
        await db.GradingCriteria.OrderBy(c => c.Order).ThenBy(c => c.CreatedAt)
            .Select(c => new GradingCriterionDto(
                c.Id, c.Name, c.Description, c.MaxScore, c.Order,
                c.TeacherId,
                db.Teachers.Where(t => t.Id == c.TeacherId).Select(t => t.FullName).FirstOrDefault()))
            .ToListAsync();

    [HttpPost("criteria")]
    public async Task<ActionResult<GradingCriterionDto>> CreateCriterion(CriterionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { message = "Nom bo'sh" });
        var teacherId = string.IsNullOrWhiteSpace(input.TeacherId) ? null : input.TeacherId;
        var maxOrder = await db.GradingCriteria.Select(c => (int?)c.Order).MaxAsync() ?? -1;
        var c = new GradingCriterion
        {
            Name = input.Name.Trim(),
            Description = (input.Description ?? "").Trim(),
            MaxScore = input.MaxScore is > 0 and <= 1000 ? input.MaxScore : 5,
            Order = maxOrder + 1,
            TeacherId = teacherId,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.GradingCriteria.Add(c);
        await db.SaveChangesAsync();
        var teacherName = teacherId is null ? null
            : await db.Teachers.Where(t => t.Id == teacherId).Select(t => t.FullName).FirstOrDefaultAsync();
        return new GradingCriterionDto(c.Id, c.Name, c.Description, c.MaxScore, c.Order, c.TeacherId, teacherName);
    }

    [HttpPut("criteria/{id}")]
    public async Task<ActionResult> UpdateCriterion(string id, CriterionInput input)
    {
        var c = await db.GradingCriteria.FindAsync(id);
        if (c is null) return NotFound();
        c.Name = (input.Name ?? "").Trim();
        c.Description = (input.Description ?? "").Trim();
        c.MaxScore = input.MaxScore is > 0 and <= 1000 ? input.MaxScore : c.MaxScore;
        c.TeacherId = string.IsNullOrWhiteSpace(input.TeacherId) ? null : input.TeacherId;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("criteria/{id}")]
    public async Task<ActionResult> DeleteCriterion(string id)
    {
        var c = await db.GradingCriteria.FindAsync(id);
        if (c is null) return NotFound();
        await db.GroupGradingCriteria.Where(g => g.CriterionId == id).ExecuteDeleteAsync();
        await db.CriterionGrades.Where(g => g.CriterionId == id).ExecuteDeleteAsync();
        db.GradingCriteria.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Guruhga biriktirish ----------

    /// <summary>Guruhga biriktirilgan mezon id'lari (tartibda).</summary>
    [HttpGet("group/{groupId}/criteria")]
    public async Task<ActionResult<IEnumerable<string>>> GroupCriteria(string groupId) =>
        await db.GroupGradingCriteria.Where(g => g.GroupId == groupId)
            .OrderBy(g => g.Order).Select(g => g.CriterionId).ToListAsync();

    /// <summary>Guruhga biriktirilgan mezonlarni to'liq almashtirish.</summary>
    [HttpPut("group/{groupId}/criteria")]
    public async Task<ActionResult> SetGroupCriteria(string groupId, GroupCriteriaInput input)
    {
        await db.GroupGradingCriteria.Where(g => g.GroupId == groupId).ExecuteDeleteAsync();
        var order = 0;
        foreach (var cid in (input.CriterionIds ?? new()).Distinct())
            db.GroupGradingCriteria.Add(new GroupGradingCriterion { GroupId = groupId, CriterionId = cid, Order = order++ });
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Baholash grid'i ----------

    [HttpGet("group/{groupId}/board")]
    public async Task<ActionResult<GradingBoardDto>> Board(string groupId, [FromQuery] string? month)
    {
        var group = await db.Classes.FindAsync(groupId);
        if (group is null) return NotFound();
        return await BuildBoardAsync(db, group, month);
    }

    [HttpPost("grade")]
    public async Task<ActionResult> Grade(SetCriterionGradeRequest req)
    {
        try
        {
            await UpsertGradeAsync(db, req);
            await db.SaveChangesAsync();
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Shu sanada bitta mezon bo'yicha BARCHA faol o'quvchini ommaviy belgilash/belgilamaslik.</summary>
    [HttpPost("grade/bulk")]
    public async Task<ActionResult> BulkGrade(BulkCriterionGradeRequest req)
    {
        var group = await db.Classes.FindAsync(req.GroupId);
        if (group is null) return NotFound();
        await BulkGradeAsync(db, group, req.CriterionId, req.Date, req.Done);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>O'quvchining baholash xulosa (oylik o'rtacha + jami) — barcha faol guruhlari bo'yicha.</summary>
    // ⚠️ Bu yerda ilgari `[AllowAnonymous]` turardi va u sinfdagi `[Authorize]` + `[AdminPerm]`
    // ni ham BEKOR qilardi: studentId ni bilgan har kim (login'siz) o'quvchining baholash
    // statistikasini olardi. `/api/admin/...` yo'lida ochiq endpoint bo'lmasligi kerak.
    [HttpGet("student/{studentId}/summary")]
    public async Task<ActionResult<IEnumerable<MonthGradingSummaryDto>>> StudentGradingSummary(string studentId)
    {
        // Qatnashlik tekshirishi: faqat o'quvchining faol guruhlari (admin uchun).
        var groups = await db.StudentGroups
            .Where(sg => sg.StudentId == studentId && sg.IsActive && sg.Status != "frozen")
            .Select(sg => sg.GroupId).ToListAsync();

        if (groups.Count == 0)
            return Ok(new List<MonthGradingSummaryDto>());

        // Barcha oylar: qaysi oyda baholash bor, ularni top'laydi.
        var marks = await db.CriterionGrades
            .Where(g => groups.Contains(g.GroupId) && g.StudentId == studentId && g.Done)
            .Select(g => new { Month = g.Date.Substring(0, 7) })
            .Distinct()
            .GroupBy(x => x.Month)
            .Select(g => new { Month = g.Key, Count = g.Count() })
            .ToListAsync();

        var summary = new List<MonthGradingSummaryDto>();

        foreach (var m in marks.OrderBy(m => m.Month))
        {
            var monthMarks = await db.CriterionGrades
                .Where(g => groups.Contains(g.GroupId) && g.StudentId == studentId && g.Done && g.Date.StartsWith(m.Month))
                .ToListAsync();

            if (monthMarks.Count == 0) continue;

            // O'rtacha: har mezon 1 ball (Done=true) — shuning uchun COUNT/jami mezonlar soni.
            var totalBaholang = monthMarks.Count;  // Har "done" mark = 1 ball
            var uniqueCriteria = monthMarks.Select(m => m.CriterionId).Distinct().Count();

            var avg = uniqueCriteria > 0 ? Math.Round((double)totalBaholang / uniqueCriteria, 2) : 0;

            summary.Add(new MonthGradingSummaryDto(
                m.Month,
                avg,
                totalBaholang,
                uniqueCriteria));
        }

        return Ok(summary);
    }

    // ---------- Umumiy yordamchilar (teacher ham ishlatadi) ----------

    /// <summary>Guruh baholash grid'ini quradi: oy(lar) + dars sanalari + biriktirilgan mezonlar +
    /// faol o'quvchilar + HAR DARSGA "bajardi" belgilari.</summary>
    public static async Task<GradingBoardDto> BuildBoardAsync(AppDbContext db, Group group, string? month)
    {
        // Oylar: guruh boshlanishidan joriy oygacha (jurnaldagi kabi).
        var cur = TuitionService.CurrentMonth();
        var startMonth = !string.IsNullOrEmpty(group.StartDate) && group.StartDate.Length >= 7 ? group.StartDate[..7] : cur;
        if (string.CompareOrdinal(startMonth, cur) > 0) startMonth = cur;
        var months = TuitionService.MonthRange(startMonth, cur).ToList();
        if (months.Count == 0) months.Add(cur);
        var resolved = !string.IsNullOrEmpty(month) && months.Contains(month) ? month! : months[^1];

        var moves = (await db.LessonReschedules.Where(r => r.ClassId == group.Id).ToListAsync())
            .Select(m => new JournalService.LessonMove(m.FromDate, m.ToDate)).ToList();
        var dates = JournalService.EffectiveLessonDatesInMonth(group.Days, resolved, moves);

        var assigns = await db.GroupGradingCriteria.Where(g => g.GroupId == group.Id)
            .OrderBy(g => g.Order).ToListAsync();
        var critIds = assigns.Select(a => a.CriterionId).ToList();
        var critDict = await db.GradingCriteria.Where(c => critIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var criteria = assigns
            .Where(a => critDict.ContainsKey(a.CriterionId))
            .Select(a => new GradingBoardCriterionDto(a.CriterionId, critDict[a.CriterionId].Name, a.Order))
            .ToList();

        // Muzlatilgan (frozen) o'quvchi baholanmaydi — jurnal gridi kabi faqat faol/sinov.
        var memberships = await db.StudentGroups
            .Where(sg => sg.GroupId == group.Id && sg.IsActive && sg.Status != "frozen")
            .ToListAsync();
        var memberIds = memberships.Select(sg => sg.StudentId).ToList();
        // A'zolik boshlanishi (ActivatedAt/JoinedAt) — frontend shu sanadan oldingi kataklarni bloklaydi.
        var startById = memberships.ToDictionary(sg => sg.StudentId, sg => JournalService.MemberStart(sg) ?? "");
        var students = await db.Students.Where(s => memberIds.Contains(s.Id) && !s.IsArchived)
            .OrderBy(s => s.FullName).ToListAsync();

        // "Bajardi" belgilar — faqat shu oydagi sanalar (sparse: faqat Done=true).
        var marks = await db.CriterionGrades
            .Where(g => g.GroupId == group.Id && g.Done && g.Date.StartsWith(resolved))
            .Select(g => new { g.StudentId, g.CriterionId, g.Date }).ToListAsync();
        var byStudent = marks.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => $"{x.CriterionId}|{x.Date}").ToList());

        var rows = students.Select(s => new GradingBoardStudentDto(
            s.Id, s.FullName, byStudent.TryGetValue(s.Id, out var d) ? d : new List<string>(),
            startById.TryGetValue(s.Id, out var ms) ? ms : "")).ToList();

        return new GradingBoardDto(group.Id, group.Name, months, resolved, dates, criteria, rows);
    }

    /// <summary>Bitta katakni belgilash: Done=true → yozuv (bajardi); Done=false → yozuvni o'chiramiz (sparse).
    /// So'ng shu (o'quvchi, sana) uchun jurnal bahosini mezon checklari soniga sinxronlaydi.</summary>
    public static async Task UpsertGradeAsync(AppDbContext db, SetCriterionGradeRequest req)
    {
        // Jurnal bilan bir xil qoida: a'zolik boshlanishidan (aktivlashtirilgan bo'lsa ActivatedAt,
        // aks holda JoinedAt) OLDINGI sanaga mezon belgilab bo'lmaydi (jurnal bahosi ham yaralib qolardi).
        var membership = await db.StudentGroups.FirstOrDefaultAsync(sg =>
            sg.GroupId == req.GroupId && sg.StudentId == req.StudentId && sg.IsActive);
        var memberStart = JournalService.MemberStart(membership);
        if (memberStart is not null && string.CompareOrdinal(req.Date, memberStart) < 0)
            throw new InvalidOperationException(
                "O'quvchi guruhga qo'shilgan/aktivlashtirilgan sanadan oldingi darsga mezon belgilab bo'lmaydi");

        var existing = await db.CriterionGrades.FirstOrDefaultAsync(
            g => g.GroupId == req.GroupId && g.StudentId == req.StudentId
                 && g.CriterionId == req.CriterionId && g.Date == req.Date);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (req.Done)
        {
            if (existing is null)
                db.CriterionGrades.Add(new CriterionGrade
                {
                    GroupId = req.GroupId, StudentId = req.StudentId, CriterionId = req.CriterionId,
                    Date = req.Date, Done = true, UpdatedAt = now,
                });
            else { existing.Done = true; existing.UpdatedAt = now; }
        }
        else if (existing is not null)
        {
            db.CriterionGrades.Remove(existing);
        }

        // Mezon o'zgarishini saqlaymiz (sanoq aniq bo'lishi uchun) → so'ng jurnal bahosini sinxronlaymiz.
        await db.SaveChangesAsync();
        var group = await db.Classes.FindAsync(req.GroupId);
        if (group is not null)
        {
            await SyncJournalGradeAsync(db, group, req.StudentId, req.Date);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Shu sanada bitta mezon bo'yicha guruhning BARCHA faol (frozen emas) o'quvchisini belgilaydi/olib tashlaydi.</summary>
    public static async Task BulkGradeAsync(AppDbContext db, Group group, string criterionId, string date, bool done)
    {
        // A'zoligi shu sanadan KEYIN boshlangan o'quvchilar chetlab o'tiladi (jurnal bulk-attendance bilan
        // bir xil) — yangi qo'shilgan/aktivlashtirilgan o'quvchiga orqadagi darsga mezon belgilanmasin.
        var memberIds = (await db.StudentGroups
                .Where(sg => sg.GroupId == group.Id && sg.IsActive && sg.Status != "frozen")
                .ToListAsync())
            .Where(sg =>
            {
                var start = JournalService.MemberStart(sg);
                return start is null || string.CompareOrdinal(date, start) >= 0;
            })
            .Select(sg => sg.StudentId).ToList();
        var existing = await db.CriterionGrades
            .Where(g => g.GroupId == group.Id && g.CriterionId == criterionId && g.Date == date).ToListAsync();
        var byStudent = existing.ToDictionary(g => g.StudentId);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        foreach (var sid in memberIds)
        {
            byStudent.TryGetValue(sid, out var e);
            if (done)
            {
                if (e is null)
                    db.CriterionGrades.Add(new CriterionGrade
                    {
                        GroupId = group.Id, StudentId = sid, CriterionId = criterionId,
                        Date = date, Done = true, UpdatedAt = now,
                    });
                else { e.Done = true; e.UpdatedAt = now; }
            }
            else if (e is not null)
            {
                db.CriterionGrades.Remove(e);
            }
        }

        // Mezon o'zgarishlarini saqlaymiz → so'ng har bir o'quvchining jurnal bahosini sinxronlaymiz.
        await db.SaveChangesAsync();
        foreach (var sid in memberIds)
            await SyncJournalGradeAsync(db, group, sid, date);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// MEZON CHECKLARI SONI = JURNAL BAHOSI. (guruh, o'quvchi, sana) bo'yicha BELGILANGAN (Done) va guruhga
    /// BIRIKTIRILGAN mezonlar sonini hisoblab <see cref="JournalEntry.Grade"/> ga yozadi (0 bo'lsa bahoni
    /// tozalaydi — sabab/uy vazifa saqlanadi). Kurssiz guruh yoki noto'g'ri sana (StartDate'dan oldin yoki
    /// kelajak) — jurnalga yozilmaydi. Belgilanganda dars "o'tildi" (LessonNote.Conducted) bo'ladi.
    /// </summary>
    private static async Task SyncJournalGradeAsync(AppDbContext db, Group group, string studentId, string date)
    {
        var subjectId = group.CourseId ?? "";
        if (string.IsNullOrEmpty(subjectId)) return; // kurssiz guruh — jurnal yo'q
        const int quarter = 1, period = 1;

        // Noto'g'ri sana (jurnal validatsiyasi bilan izchil): kelajak yoki guruh boshlanishidan oldin — o'tkazib yuboramiz.
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        if (string.Compare(date, today) > 0) return;
        if (!string.IsNullOrEmpty(group.StartDate) && string.Compare(date, group.StartDate) < 0) return;

        // Guruhga biriktirilgan mezonlar (board ko'rsatadigan) bo'yicha belgilangan (Done) sonni hisoblaymiz.
        var assigned = await db.GroupGradingCriteria.Where(g => g.GroupId == group.Id)
            .Select(g => g.CriterionId).ToListAsync();
        var count = assigned.Count == 0 ? 0 : await db.CriterionGrades.CountAsync(g =>
            g.GroupId == group.Id && g.StudentId == studentId && g.Date == date && g.Done
            && assigned.Contains(g.CriterionId));

        var entry = await db.JournalEntries.FirstOrDefaultAsync(e =>
            e.ClassId == group.Id && e.SubjectId == subjectId && e.Quarter == quarter &&
            e.StudentId == studentId && e.Date == date && e.Period == period);

        if (count > 0)
        {
            if (entry is null)
            {
                entry = new JournalEntry
                {
                    ClassId = group.Id, SubjectId = subjectId, Quarter = quarter,
                    StudentId = studentId, Date = date, Period = period,
                };
                db.JournalEntries.Add(entry);
            }
            entry.Grade = count;

            // Darsni "o'tildi" deb belgilash (jurnal ustuni/yashil holat uchun) — SetEntryAsync bilan izchil.
            var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
                n.ClassId == group.Id && n.SubjectId == subjectId &&
                n.Quarter == quarter && n.Date == date && n.Period == period);
            if (note is null)
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = group.Id, SubjectId = subjectId, Quarter = quarter,
                    Date = date, Period = period, Conducted = true,
                });
            else if (!note.Conducted) note.Conducted = true;
        }
        else if (entry is not null)
        {
            entry.Grade = null; // checklar olib tashlandi — bahoni tozalaymiz (boshqa maydonlar saqlanadi)
        }
    }

    /// <summary>Guruh uchun baholash statistikasi: oylik mezon ballari + nechta ba'ho kiritilgan.</summary>
    [HttpGet("group/{groupId}/summary")]
    public async Task<ActionResult<GradingGroupSummaryDto>> GetGroupSummary(string groupId, [FromQuery] string? month)
    {
        var group = await db.Classes.FindAsync(groupId);
        if (group is null) return NotFound();

        var cur = TuitionService.CurrentMonth();
        var resolved = !string.IsNullOrEmpty(month) && month.Length >= 7 ? month : cur;

        // Faol a'zolar (frozen emas)
        var memberIds = await db.StudentGroups
            .Where(sg => sg.GroupId == groupId && sg.IsActive && sg.Status != "frozen")
            .Select(sg => sg.StudentId).ToListAsync();

        // Shu oy baholari (CriterionGrades, Done=true)
        var grades = await db.CriterionGrades
            .Where(g => g.GroupId == groupId && g.Done && g.Date.StartsWith(resolved))
            .ToListAsync();

        var totalGrades = grades.Count;
        var avgScore = 0.0;
        if (totalGrades > 0)
        {
            var scores = grades.GroupBy(g => new { g.StudentId, g.Date })
                .Select(g => g.Count()) // Har dars-o'quvchi bo'yicha mezon soni
                .ToList();
            avgScore = Math.Round(scores.DefaultIfEmpty(0).Average(), 1);
        }

        return new GradingGroupSummaryDto(groupId, group.Name, memberIds.Count, totalGrades, avgScore);
    }

    /// <summary>BARCHA guruhlar uchun baholash statistikasi — BITTA javobda (N+1 o'rniga). Frontend
    /// "Guruhlar" sahifasi har guruh uchun alohida <c>group/{id}/summary</c> yubormasligi uchun. Natija
    /// keshlanadi; CriterionGrade/StudentGroup/Group o'zgarganda avto-yangilanadi.</summary>
    [HttpGet("groups/summary")]
    public async Task<ActionResult<IEnumerable<GradingGroupSummaryDto>>> GetAllGroupsSummary([FromQuery] string? month)
    {
        var cur = TuitionService.CurrentMonth();
        var resolved = !string.IsNullOrEmpty(month) && month.Length >= 7 ? month : cur;
        return await dataCache.GetOrCreateAsync(
            $"grading:groups-summary:{resolved}",
            new[] { nameof(CriterionGrade), nameof(StudentGroup), nameof(Group) },
            TimeSpan.FromMinutes(10),
            db2 => ComputeAllGroupsSummaryAsync(db2, resolved));
    }

    /// <summary>Barcha guruhlar baholash statistikasini SET-BASED hisoblaydi: 3 so'rov (guruhlar, faol
    /// a'zoliklar, shu oy baholari) — so'ng xotirada guruhlab, har guruh uchun <see cref="GetGroupSummary"/>
    /// bilan bir xil natija (ActiveStudents, TotalGrades, AverageScore) chiqaradi.</summary>
    private static async Task<List<GradingGroupSummaryDto>> ComputeAllGroupsSummaryAsync(IAppDbContext db2, string resolved)
    {
        var groups = await db2.Classes.AsNoTracking()
            .Select(g => new { g.Id, g.Name }).ToListAsync();

        // Faol a'zolar soni (frozen emas) — guruh bo'yicha.
        var activeByGroup = (await db2.StudentGroups.AsNoTracking()
                .Where(sg => sg.IsActive && sg.Status != "frozen")
                .Select(sg => sg.GroupId).ToListAsync())
            .GroupBy(gid => gid)
            .ToDictionary(g => g.Key, g => g.Count());

        // Shu oy baholari (Done=true) — guruh bo'yicha.
        var gradesByGroup = (await db2.CriterionGrades.AsNoTracking()
                .Where(g => g.Done && g.Date.StartsWith(resolved))
                .Select(g => new { g.GroupId, g.StudentId, g.Date }).ToListAsync())
            .GroupBy(g => g.GroupId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<GradingGroupSummaryDto>(groups.Count);
        foreach (var g in groups)
        {
            var activeStudents = activeByGroup.TryGetValue(g.Id, out var ac) ? ac : 0;
            var totalGrades = 0;
            var avgScore = 0.0;
            if (gradesByGroup.TryGetValue(g.Id, out var grades) && grades.Count > 0)
            {
                totalGrades = grades.Count;
                // Har (o'quvchi, sana) bo'yicha mezon soni → o'rtacha (GetGroupSummary bilan izchil).
                var scores = grades.GroupBy(x => new { x.StudentId, x.Date }).Select(x => x.Count()).ToList();
                avgScore = Math.Round(scores.DefaultIfEmpty(0).Average(), 1);
            }
            result.Add(new GradingGroupSummaryDto(g.Id, g.Name, activeStudents, totalGrades, avgScore));
        }
        return result;
    }
}
