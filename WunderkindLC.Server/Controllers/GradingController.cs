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

    /// <para>Mezon o'chganda uning belgilaridan hisoblangan jurnal baholari ham QAYTA SINXRONLANADI
    /// (<see cref="GradingService.ResyncAsync"/>; qo'lda qo'yilgan baho tegilmaydi) — aks holda jurnalda
    /// endi mavjud bo'lmagan checklar soni osilib qolardi. <c>ExecuteDeleteAsync</c> ChangeTracker'dan
    /// o'tmaydi, shuning uchun kesh (reyting/ball) QO'LDA yangilanadi.</para>
    [HttpDelete("criteria/{id}")]
    public async Task<ActionResult> DeleteCriterion(string id)
    {
        var c = await db.GradingCriteria.FindAsync(id);
        if (c is null) return NotFound();
        var affected = await AffectedCellsAsync(db.CriterionGrades.Where(g => g.CriterionId == id));
        await db.GroupGradingCriteria.Where(g => g.CriterionId == id).ExecuteDeleteAsync();
        await db.CriterionGrades.Where(g => g.CriterionId == id).ExecuteDeleteAsync();
        db.GradingCriteria.Remove(c);
        await GradingService.ResyncAsync(db, affected);
        await db.SaveChangesAsync();
        dataCache.Bump(new[] { nameof(CriterionGrade), nameof(GroupGradingCriterion), nameof(JournalEntry) });
        return NoContent();
    }

    /// <summary>Belgilangan (Done) mezon kataklari → (guruh, o'quvchi, sana) — qayta sinxron uchun.</summary>
    private static async Task<List<(string GroupId, string StudentId, string Date)>> AffectedCellsAsync(
        IQueryable<CriterionGrade> q) =>
        (await q.Where(g => g.Done).Select(g => new { g.GroupId, g.StudentId, g.Date }).Distinct().ToListAsync())
            .Select(x => (x.GroupId, x.StudentId, x.Date)).ToList();

    // ---------- Guruhga biriktirish ----------

    /// <summary>Guruhga biriktirilgan mezon id'lari (tartibda).</summary>
    [HttpGet("group/{groupId}/criteria")]
    public async Task<ActionResult<IEnumerable<string>>> GroupCriteria(string groupId) =>
        await db.GroupGradingCriteria.Where(g => g.GroupId == groupId)
            .OrderBy(g => g.Order).Select(g => g.CriterionId).ToListAsync();

    /// <summary>Guruhga biriktirilgan mezonlarni to'liq almashtirish.
    /// <para>Ajratilgan mezonlar jurnal bahosidan ham chiqadi: ularning belgilari bor kataklar QAYTA
    /// SINXRONLANADI (faqat sinxronning o'z bahosi; qo'lda qo'yilgan baho tegilmaydi).</para></summary>
    [HttpPut("group/{groupId}/criteria")]
    public async Task<ActionResult> SetGroupCriteria(string groupId, GroupCriteriaInput input)
    {
        var next = (input.CriterionIds ?? new()).Distinct().ToList();
        var removed = (await db.GroupGradingCriteria.Where(g => g.GroupId == groupId)
                .Select(g => g.CriterionId).ToListAsync())
            .Where(cid => !next.Contains(cid)).ToList();
        List<(string GroupId, string StudentId, string Date)> affected = removed.Count == 0 ? new()
            : await AffectedCellsAsync(db.CriterionGrades.Where(g => g.GroupId == groupId && removed.Contains(g.CriterionId)));
        await db.GroupGradingCriteria.Where(g => g.GroupId == groupId).ExecuteDeleteAsync();
        var order = 0;
        foreach (var cid in next)
            db.GroupGradingCriteria.Add(new GroupGradingCriterion { GroupId = groupId, CriterionId = cid, Order = order++ });
        await db.SaveChangesAsync();
        if (affected.Count > 0)
        {
            await GradingService.ResyncAsync(db, affected);
            await db.SaveChangesAsync();
        }
        dataCache.Bump(new[] { nameof(GroupGradingCriterion) });
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

    /// <summary>Bitta katakni belgilash. Jurnal qoidalari (kelajak sana, dars kuni, jurnal siyosati —
    /// adminga "Adminlarga ham qo'llash" bo'yicha) — <see cref="GradingService.UpsertGradeAsync"/>.</summary>
    [HttpPost("grade")]
    public async Task<ActionResult> Grade(SetCriterionGradeRequest req)
    {
        try
        {
            await GradingService.UpsertGradeAsync(db, req, isAdmin: true);
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
        try
        {
            await GradingService.BulkGradeAsync(db, group, req.CriterionId, req.Date, req.Done, isAdmin: true);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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

    // Belgilash va jurnal bahosi sinxroni — GradingService (Application) da: o'qituvchi ilovasi ham
    // shuni chaqiradi va mantiq testlanadi (test loyihasi Server'ga bog'lanmagan).

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
