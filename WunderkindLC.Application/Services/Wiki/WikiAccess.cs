using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Bilimlar bazasidagi so'rov egasi.
/// <para><see cref="IsLeader"/> — RAHBARIYAT: admin, superadmin, platforma egasi yoki HR'da «direktor»
/// (`HrEmployee.HrRole = director`, «Topshiriqlar → Xodimlar» da belgilanadi). Rahbariyat HAMMA xodimning
/// eslatmalarini o'qiydi, ularga sahifa/bo'lim qo'shadi, qulf qo'yadi/oladi va qulflangan eslatmani tahrirlaydi.</para>
/// <para><see cref="TeamNoteOwners"/> — rahbariyat bo'lmagan menejer qaysi xodimlarning eslatmalarini o'qiydi
/// (USER id'lar). Rahbariyat uchun ahamiyatsiz (hammasi), oddiy xodim uchun BO'SH.</para>
/// </summary>
public sealed record WikiViewer(string UserId, string Name, bool IsLeader, IReadOnlySet<string> TeamNoteOwners)
{
    /// <summary>Shu odamning shaxsiy eslatmalarini o'qiy oladimi.</summary>
    public bool CanSeeNotesOf(string ownerUserId) =>
        ownerUserId == UserId || IsLeader || TeamNoteOwners.Contains(ownerUserId);
}

/// <summary>So'rov egasini aniqlash (IO). Qoidalarning sof qismi — <see cref="WikiRules"/>.</summary>
public static class WikiAccess
{
    public static bool IsLeaderRole(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin) || user.IsInRole(Roles.PlatformOwner);

    public static async Task<WikiViewer> ResolveAsync(IAppDbContext db, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
        if (IsLeaderRole(user)) return new WikiViewer(userId, name, true, new HashSet<string>());
        if (userId.Length == 0) return new WikiViewer(userId, name, false, new HashSet<string>());

        var self = await db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.IsActive, ct);
        if (self is null) return new WikiViewer(userId, name, false, new HashSet<string>());
        if (self.HrRole == HrRoles.Director) return new WikiViewer(userId, name, true, new HashSet<string>());
        if (self.HrRole != HrRoles.Manager) return new WikiViewer(userId, name, false, new HashSet<string>());

        var settings = await SettingsAsync(db, ct);
        var edges = (await db.HrEmployeeManagers.AsNoTracking().Select(m => new { m.EmployeeId, m.ManagerId }).ToListAsync(ct))
            .Select(m => (m.EmployeeId, m.ManagerId));
        var employees = (await db.HrEmployees.AsNoTracking()
                .Select(e => new { e.Id, e.UserId, e.PositionId, e.IsActive }).ToListAsync(ct))
            .Select(e => (e.Id, e.UserId, e.PositionId, e.IsActive));
        var team = WikiRules.TeamNoteOwners(self.Id, edges, employees,
            settings.ManagersSeeTeamNotes, settings.ManagerVisiblePositionIds);
        return new WikiViewer(userId, name, false, team);
    }

    /// <summary>Sozlama qatori; bo'lmasa — standart qiymatlar (saqlanmaydi).</summary>
    public static async Task<WikiSettings> SettingsAsync(IAppDbContext db, CancellationToken ct = default) =>
        await db.WikiSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == WikiSettings.SingletonId, ct)
        ?? new WikiSettings();
}
