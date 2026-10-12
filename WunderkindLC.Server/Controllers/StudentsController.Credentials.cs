using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services.Accounts;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Student password vault helpers for <c>Credentials</c>, <c>ResetPassword</c>, <c>Export</c> and
/// <c>ExportSelected</c> (CRM Phase 6, docs/SPEC-crm-upgrade.md §7 P6.1). The gate stays the single
/// <c>AdminPermAttribute.HasFullAccess(User, "students.list")</c> (<c>.claude/rules/auth-sessions.md</c> §3).
/// </summary>
public partial class StudentsController
{
    /// <summary>Resolved per request — the shared primary constructor is not edited (SPEC §0.2 step 3).</summary>
    private CredentialVault Vault => HttpContext.RequestServices.GetRequiredService<CredentialVault>();

    /// <summary>Every response that carries a password: browsers/proxies must not keep it.</summary>
    private void NoStore() => Response.Headers.CacheControl = "no-store";

    private CredentialsDto CredentialsOf(AppUser user, StudentCredentials.Revealed r) =>
        new(user.Email, r.Password ?? "", user.Role, r.Known, r.Source,
            r.UpdatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"), Vault.Configured);

    /// <summary>Reveals many accounts with ONE query for their vault rows.</summary>
    private async Task<Dictionary<string, StudentCredentials.Revealed>> RevealManyAsync(IEnumerable<AppUser> users)
    {
        var list = users.ToList();
        var ids = list.Select(u => u.Id).ToList();
        var rows = ids.Count == 0
            ? new Dictionary<string, StudentCredential>()
            : await db.StudentCredentials.AsNoTracking().Where(c => ids.Contains(c.UserId))
                .ToDictionaryAsync(c => c.UserId);
        var vault = Vault;
        return list.ToDictionary(u => u.Id, u => StudentCredentials.Reveal(rows.GetValueOrDefault(u.Id), u, vault));
    }

    /// <summary>Excel password cell: the password, «(saqlanmagan)» when unknown, empty without an account.</summary>
    private static string PasswordCell(AppUser? user, IReadOnlyDictionary<string, StudentCredentials.Revealed> revealed) =>
        user is null || !revealed.TryGetValue(user.Id, out var r)
            ? ""
            : StudentCredentials.ExportCell(r, hasAccount: true);
}
