using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Accounts;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Infrastructure.Auth;

/// <summary>
/// Group «Login/parollar» (CRM Phase 6, docs/SPEC-crm-upgrade.md §7 P6.1): every LIVE, non-archived member of a
/// group with login + password (<see cref="StudentCredentials.Reveal"/>), and the one-click «generate passwords for
/// the unknown ones». The controller (<c>GroupCredentialsController</c>) only gates and shapes the response.
/// </summary>
public static class GroupCredentials
{
    /// <summary>The window's data. <c>null</c> from <see cref="LoadAsync"/> = group not found.</summary>
    public sealed record View(string GroupId, string GroupName, bool VaultConfigured, IReadOnlyList<GroupCredentialRow> Rows);

    /// <summary>Result of <see cref="EnsureAsync"/>: new accounts and regenerated passwords.</summary>
    public sealed record EnsureResult(int Created, int Regenerated);

    /// <summary>Read only — never creates an account or a password (a member without an account → <c>login: ""</c>).</summary>
    public static async Task<View?> LoadAsync(AppDbContext db, CredentialVault vault, string groupId, CancellationToken ct = default)
    {
        var group = await db.Classes.AsNoTracking().Where(g => g.Id == groupId)
            .Select(g => new { g.Id, g.Name }).FirstOrDefaultAsync(ct);
        if (group is null) return null;

        var members = await (from sg in db.StudentGroups.AsNoTracking()
                             join s in db.Students.AsNoTracking() on sg.StudentId equals s.Id
                             where sg.GroupId == groupId && sg.IsActive && !s.IsArchived
                             select new { s.Id, s.FullName, s.UserId, sg.Status, sg.YearFreeze }).ToListAsync(ct);
        // One row per student even if two live memberships of the same group exist (bad legacy data).
        var distinct = members.GroupBy(m => m.Id).Select(g => g.First())
            .OrderBy(m => m.FullName, StringComparer.OrdinalIgnoreCase).ToList();

        var userIds = distinct.Where(m => m.UserId != null).Select(m => m.UserId!).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<string, AppUser>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var vaultRows = userIds.Count == 0
            ? new Dictionary<string, StudentCredential>()
            : await db.StudentCredentials.AsNoTracking().Where(c => userIds.Contains(c.UserId)).ToDictionaryAsync(c => c.UserId, ct);

        var studentIds = distinct.Select(m => m.Id).ToList();
        var chats = studentIds.Count == 0
            ? new Dictionary<string, int>()
            : (await db.TelegramRegistrations.AsNoTracking()
                    .Where(r => studentIds.Contains(r.StudentId))
                    .Select(r => new { r.StudentId, r.ChatId }).ToListAsync(ct))
                .GroupBy(r => r.StudentId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.ChatId).Distinct().Count());

        var rows = distinct.Select(m =>
        {
            users.TryGetValue(m.UserId ?? "", out var u);
            var r = StudentCredentials.Reveal(u is null ? null : vaultRows.GetValueOrDefault(u.Id), u, vault);
            return new GroupCredentialRow(m.Id, m.FullName, m.Status, m.YearFreeze, u is not null,
                u?.Email ?? "", r.Password, r.Known, r.Source, chats.GetValueOrDefault(m.Id));
        }).ToList();
        return new View(group.Id, group.Name, vault.Configured, rows);
    }

    /// <summary>
    /// «Paroli noma'lumlarga yangi parol yaratish»: creates the missing accounts (source <c>create</c>) and gives a
    /// NEW password ONLY to members whose password is unknown (<c>SetInitialPassword(new, "reset")</c> + every
    /// session ended). Known passwords are never touched. Per-student audit (no password) + one <c>Group</c> row;
    /// one <c>SaveChanges</c>. <c>null</c> = group not found.
    /// </summary>
    public static async Task<EnsureResult?> EnsureAsync(
        AppDbContext db, CredentialVault vault, AuditService audit, string groupId, CancellationToken ct = default)
    {
        var group = await db.Classes.AsNoTracking().Where(g => g.Id == groupId)
            .Select(g => new { g.Id, g.Name }).FirstOrDefaultAsync(ct);
        if (group is null) return null;

        var students = (await (from sg in db.StudentGroups
                               join s in db.Students on sg.StudentId equals s.Id
                               where sg.GroupId == groupId && sg.IsActive && !s.IsArchived
                               select s).ToListAsync(ct))
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase).ToList();

        var userIds = students.Where(s => s.UserId != null).Select(s => s.UserId!).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<string, AppUser>()
            : await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var vaultRows = userIds.Count == 0
            ? new Dictionary<string, StudentCredential>()
            : await db.StudentCredentials.AsNoTracking().Where(c => userIds.Contains(c.UserId)).ToDictionaryAsync(c => c.UserId, ct);

        int created = 0, regenerated = 0;
        foreach (var s in students)
        {
            users.TryGetValue(s.UserId ?? "", out var user);
            if (user is null)
            {
                user = AccountFactory.CreateAccountFor(db, Roles.Student, s.FullName);
                s.UserId = user.Id;
                audit.Record(AuditService.EntityStudentDiscount, s.Id, "create",
                    $"O'quvchi uchun login/parol yaratildi ({s.FullName})", studentId: s.Id);
                created++;
                continue;
            }
            // Only student accounts (a mis-linked staff account is never re-passworded from here).
            if (user.Role != Roles.Student) continue;
            if (StudentCredentials.Reveal(vaultRows.GetValueOrDefault(user.Id), user, vault).Known) continue;

            var pwd = AccountFactory.GeneratePassword();
            // The password itself is never written to the history — only the fact (auth-sessions.md §3).
            audit.Record(AuditService.EntityStudentDiscount, s.Id, "update",
                $"O'quvchi paroli qayta yaratildi ({s.FullName})", studentId: s.Id);
            user.SetInitialPassword(pwd, PasswordSources.Reset);
            await RefreshTokenService.RevokeAllForUserAsync(db, user.Id, ct: ct);
            regenerated++;
        }

        if (created + regenerated > 0)
        {
            audit.Record(AuditService.EntityGroup, group.Id, "update",
                $"Login/parollar yaratildi: {group.Name} — {created} ta yangi akkaunt, {regenerated} ta yangi parol");
            await db.SaveChangesAsync(ct);
        }
        return new EnsureResult(created, regenerated);
    }
}
