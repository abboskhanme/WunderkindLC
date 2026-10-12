using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// STUDENT RATING — tests + journal ball (docs/SPEC-crm-upgrade.md P4.3, rules <c>.claude/rules/rating.md</c>).
/// Page key <c>classes.rating</c> («Reyting»). Every read is permission-gated (<c>ReadRequiresPerm</c>): the
/// answers list students with their scores, i.e. not for every staff member by default.
///
/// <para>All numbers come from ONE cached computation per period (<see cref="RatingEngine.Cached"/>), sliced
/// per endpoint — the centre table, a group's table and a student's block never disagree.</para>
///
/// <para>No debtor lock: the balance is never read (owner decision, 2026-10-11).</para>
///
/// <para>⚠️ CLASS-LEVEL FLOOR (M3, 2026-10-12): <c>[AdminPerm("classes.rating", ReadRequiresPerm = true)]</c> on the
/// class — an action added later WITHOUT its own gate is still closed to staff without the page key (default
/// <c>AdminPerm</c> would leave its GET open to every staff member). A method-level <c>[AdminPerm]</c> still OVERRIDES
/// it (<c>AdminPermAttribute.IsOverriddenAtMethod</c>), so the group/student endpoints keep their wider key sets.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("classes.rating", ReadRequiresPerm = true)]
[Route("api/admin/rating")]
public partial class RatingController(AppDbContext db, DataCache cache, AuditService audit) : ControllerBase
{
    /// <summary>Uzbek labels of the settings in the change history (values are written — they change scores).</summary>
    private static readonly Dictionary<string, string> SettingsLabels = new()
    {
        [nameof(CenterMeta.RatingTestWeight)] = "Testlar ulushi, %",
        [nameof(CenterMeta.RatingTopBottomCount)] = "TOP/BOTTOM soni",
        [nameof(CenterMeta.RatingTopBottomScope)] = "TOP/BOTTOM qamrovi",
    };

    public const string WeightMessage = "Testlar ulushi 0 dan 100 gacha butun son bo'lsin";
    public const string CountMessage = "TOP/BOTTOM soni 1 dan 10 gacha bo'lsin";
    public const string ScopeMessage = "Qamrov «group» yoki «centre» bo'lsin";

    /// <summary>Centre table: ranked students of the period, name search, paged (default 50, max 200).</summary>
    [HttpGet("centre")]
    [AdminPerm("classes.rating", ReadRequiresPerm = true)]
    public async Task<ActionResult<RatingCentreDto>> Centre(
        [FromQuery] string? period, [FromQuery] string? key, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var (p, error) = RatingPeriods.Resolve(period, key, AppClock.Today);
        if (p is null) return BadRequest(new { message = error });
        var r = await RatingEngine.Cached(cache)(p);
        return RatingEngine.Centre(r, q, page, pageSize);
    }

    /// <summary>One group's table (group page «Reyting» → «Umumiy reyting» and the report's «Guruhlar» tab).</summary>
    [HttpGet("groups/{groupId}")]
    [AdminPerm("classes.rating", "classes.list", ReadRequiresPerm = true)]
    public async Task<ActionResult<RatingGroupDto>> GroupRating(string groupId, [FromQuery] string? period, [FromQuery] string? key)
    {
        var (p, error) = RatingPeriods.Resolve(period, key, AppClock.Today);
        if (p is null) return BadRequest(new { message = error });
        var r = await RatingEngine.Cached(cache)(p);
        var info = r.GroupInfo.GetValueOrDefault(groupId);
        if (info is null) return NotFound(new { message = "Guruh topilmadi" });

        // Current members (active/trial, non-archived students) — listed even without data in the period.
        var members = await (from sg in db.StudentGroups.AsNoTracking()
                             join s in db.Students.AsNoTracking() on sg.StudentId equals s.Id
                             where sg.GroupId == groupId && sg.IsActive && sg.Status != "frozen" && !s.IsArchived
                             select new { s.Id, s.FullName })
            .ToListAsync();
        var byId = members.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().FullName, StringComparer.Ordinal);
        return RatingEngine.GroupTable(r, info, byId);
    }

    /// <summary>The student profile «Reyting» tab block.</summary>
    [HttpGet("students/{studentId}")]
    [AdminPerm("students.list", "classes.rating", ReadRequiresPerm = true)]
    public async Task<ActionResult<StudentRatingBlockDto>> StudentBlock(
        string studentId, [FromQuery] string? period, [FromQuery] string? key)
    {
        var today = AppClock.Today;
        var (p, error) = RatingPeriods.Resolve(period, key, today);
        if (p is null) return BadRequest(new { message = error });
        var block = await RatingEngine.StudentBlockAsync(db, RatingEngine.Cached(cache), studentId, p, today);
        return block is null ? NotFound(new { message = "O'quvchi topilmadi" }) : block;
    }

    /// <summary>Rating settings (Sozlamalar → O'quv → «Reyting»).</summary>
    [HttpGet("settings")]
    [AdminPerm("classes.rating", ReadRequiresPerm = true)]
    public async Task<ActionResult<RatingSettingsDto>> GetSettings()
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync();
        return meta is null
            ? new RatingSettingsDto(RatingFormula.DefaultTestWeight, 5, "group")
            : new RatingSettingsDto(meta.RatingTestWeight, meta.RatingTopBottomCount, meta.RatingTopBottomScope);
    }

    /// <summary>Saves the settings — audited with values («Testlar ulushi, %: 60 → 70»): the weight changes
    /// every student's score.</summary>
    [HttpPut("settings")]
    [AdminPerm("classes.rating", ReadRequiresPerm = true)]
    public async Task<ActionResult<RatingSettingsDto>> SaveSettings(RatingSettingsDto req)
    {
        var scope = (req.TopBottomScope ?? "").Trim().ToLowerInvariant();
        if (req.TestWeight is < 0 or > 100) return BadRequest(new { message = WeightMessage });
        if (req.TopBottomCount is < 1 or > 10) return BadRequest(new { message = CountMessage });
        if (scope is not ("group" or "centre")) return BadRequest(new { message = ScopeMessage });

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (meta is null) { meta = new CenterMeta(); db.CenterMeta.Add(meta); }
        meta.RatingTestWeight = req.TestWeight;
        meta.RatingTopBottomCount = req.TopBottomCount;
        meta.RatingTopBottomScope = scope;
        SettingsAudit.Record<CenterMeta>(audit, db.ChangeTracker, "Reyting sozlamalari", withValues: true, labels: SettingsLabels);
        await db.SaveChangesAsync();
        return new RatingSettingsDto(meta.RatingTestWeight, meta.RatingTopBottomCount, meta.RatingTopBottomScope);
    }
}
