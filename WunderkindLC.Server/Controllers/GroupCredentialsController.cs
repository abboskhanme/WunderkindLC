using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Accounts;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Group page → «O'quvchilar» → «Login/parollar» (CRM Phase 6, docs/SPEC-crm-upgrade.md §7 P6.1): every live
/// member's login and CURRENT password, the per-group Excel export and «Paroli noma'lumlarga yangi parol».
///
/// <para><b>Gate</b> — the single password gate of <c>.claude/rules/auth-sessions.md</c> §3:
/// <c>[AdminPerm("students.list", ReadRequiresPerm = true)]</c> AND
/// <c>AdminPermAttribute.HasFullAccess(User, "students.list")</c> as the FIRST step of every action (admin/superadmin
/// always; staff only with the full «O'quvchilar» key). Teachers never reach it (SPEC §10 #5). Every response that
/// carries passwords is <c>Cache-Control: no-store</c>; passwords never go to logs or the audit.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.list", ReadRequiresPerm = true)]
[Route("api/admin/classes/{groupId}/credentials")]
public class GroupCredentialsController(AppDbContext db, CredentialVault vault, AuditService audit) : ControllerBase
{
    /// <summary>403 text (the UI shows it).</summary>
    public const string ForbiddenMessage = "Login/parolni ko'rish uchun «O'quvchilar» bo'limiga to'liq ruxsat kerak";

    private bool MaySee => AdminPermAttribute.HasFullAccess(User, "students.list");

    private ObjectResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, new { message = ForbiddenMessage });

    private void NoStore() => Response.Headers.CacheControl = "no-store";

    /// <summary>Live, non-archived members with login + password. Read only — never creates anything.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(string groupId, CancellationToken ct)
    {
        if (!MaySee) return Forbidden();
        var view = await GroupCredentials.LoadAsync(db, vault, groupId, ct);
        if (view is null) return NotFound(new { message = "Guruh topilmadi" });
        NoStore();
        return Ok(Shape(view));
    }

    public sealed record EnsureRequest(bool OnlyUnknown = true);

    /// <summary>
    /// Creates missing accounts and regenerates ONLY unknown passwords (old ones stop working; their sessions end).
    /// Returns the GET shape + <c>created</c>, <c>regenerated</c>.
    /// </summary>
    [HttpPost("ensure")]
    public async Task<IActionResult> Ensure(string groupId, EnsureRequest req, CancellationToken ct)
    {
        if (!MaySee) return Forbidden();
        // Only the «unknown» mode exists: regenerating KNOWN passwords of a whole group in one click is not offered.
        if (!req.OnlyUnknown)
            return BadRequest(new { message = "Faqat paroli noma'lum o'quvchilar uchun yangi parol yaratiladi" });

        var result = await GroupCredentials.EnsureAsync(db, vault, audit, groupId, ct);
        if (result is null) return NotFound(new { message = "Guruh topilmadi" });
        var view = await GroupCredentials.LoadAsync(db, vault, groupId, ct);
        NoStore();
        return Ok(new
        {
            view!.VaultConfigured,
            view.GroupName,
            Rows = view.Rows.Select(RowDto),
            result.Created,
            result.Regenerated,
        });
    }

    /// <summary>Excel «№, F.I.SH., Login, Parol, Telegram, Holat»; audited WITHOUT any password.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(string groupId, CancellationToken ct)
    {
        if (!MaySee) return Forbidden();
        var view = await GroupCredentials.LoadAsync(db, vault, groupId, ct);
        if (view is null) return NotFound(new { message = "Guruh topilmadi" });

        var bytes = ExcelExport.Build("Login-parollar", GroupCredentialsSheet.Headers, GroupCredentialsSheet.Rows(view.Rows));
        audit.Record(AuditService.EntityGroup, view.GroupId, "create",
            $"Login/parollar eksport qilindi: {view.GroupName} ({view.Rows.Count} ta o'quvchi)");
        await db.SaveChangesAsync(ct);

        NoStore();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            GroupCredentialsSheet.FileName(view.GroupName, AppClock.Now));
    }

    private static object Shape(GroupCredentials.View v) => new
    {
        v.VaultConfigured,
        v.GroupName,
        Rows = v.Rows.Select(RowDto),
    };

    private static object RowDto(GroupCredentialRow r) => new
    {
        r.StudentId,
        r.FullName,
        r.Status,
        r.YearFreeze,
        r.HasAccount,
        r.Login,
        Password = r.Known ? r.Password : null,
        r.Known,
        r.Source,
        r.TelegramChats,
    };
}
