using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp;

/// <summary>
/// Superadmin alerts about AI connections (docs/SPEC-mcp.md §6): a NEW connection was authorised, or one
/// connection read unusually much in 24 h. Delivered to every superadmin's personal Telegram chat through the
/// existing staff bot link (<see cref="TelegramRegistration.UserId"/> — the same channel new-lead cards use).
///
/// <para>The text never carries tokens, client ids, grant ids or chat ids — only who, which app, where the key
/// went, when, and the volume numbers. No <c>parse_mode</c>: the client name is self-declared, untrusted text.</para>
/// </summary>
public static class McpAlertText
{
    private static string RoleLabel(string role) => role switch
    {
        Roles.SuperAdmin => "superadmin",
        Roles.Admin => "administrator",
        Roles.Staff => "xodim",
        _ => role,
    };

    private static string When(DateTime utc) => AppClock.ToLocal(utc).ToString("dd.MM.yyyy HH:mm");

    /// <summary>"A new AI connection was created" — sent right after the code exchange.</summary>
    public static string NewConnection(string userName, string role, string clientName, string destination, DateTime atUtc) =>
        string.Join("\n",
            "🔌 Yangi AI ulanish (faqat o'qish)",
            "",
            $"👤 Kim: {McpText.Sanitize(userName, 120)} ({RoleLabel(role)})",
            $"🤖 Ilova: «{McpText.Sanitize(clientName, McpLimits.MaxClientName)}» (nomi tekshirilmagan)",
            $"📍 Kalit yuborilgan joy: {McpText.Sanitize(destination, 120)}",
            $"🕒 Vaqt: {When(atUtc)}",
            "",
            "Agar bu ulanishni tanimasangiz: Sozlamalar → Umumiy → «AI ulanishlar» → «Bekor qilish».");

    /// <summary>"One connection read a lot in the last 24 h" — sent by the volume watch, at most once a day per connection.</summary>
    public static string Volume(string userName, string clientName, int calls, long rows) =>
        string.Join("\n",
            "⚠️ AI ulanish: 24 soatda odatdan ko'p so'rov",
            "",
            $"👤 Kim: {McpText.Sanitize(userName, 120)}",
            $"🤖 Ilova: «{McpText.Sanitize(clientName, McpLimits.MaxClientName)}»",
            $"📊 So'rovlar: {calls} ta (chegara {McpLimits.VolumeCallsPerDay}) · o'qilgan qatorlar: {rows} ta (chegara {McpLimits.VolumeRowsPerDay})",
            "",
            "Ulanish ishlayveradi. Tekshiring: Sozlamalar → Umumiy → «AI ulanishlar» → «So'rovlar jurnali»; "
            + "kerak bo'lsa «Bekor qilish».");
}

/// <summary>Sends <see cref="McpAlertText"/> messages to superadmins. Never throws (except cancellation).</summary>
public static class McpAlertSender
{
    /// <summary>Personal chats of CURRENT superadmins that linked the staff bot.</summary>
    public static async Task<List<long>> SuperAdminChatsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var superIds = await db.Users.AsNoTracking().Where(u => u.Role == Roles.SuperAdmin)
            .Select(u => u.Id).ToListAsync(ct);
        if (superIds.Count == 0) return [];
        return await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.UserId != null && superIds.Contains(r.UserId))
            .Select(r => r.ChatId).Distinct().ToListAsync(ct);
    }

    /// <summary>
    /// Deliver <paramref name="text"/> to every superadmin chat. When the bot is not configured or no superadmin
    /// linked it, a WARNING line with the same text goes to the log instead — the alert is never silently lost.
    /// </summary>
    /// <returns>Number of chats the message reached.</returns>
    public static async Task<int> SendToSuperAdminsAsync(
        IAppDbContext db, TelegramService telegram, string text, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            if (!telegram.IsConfigured)
            {
                logger.LogWarning("MCP alert (Telegram bot not configured — log only): {Alert}", text);
                return 0;
            }
            var chats = await SuperAdminChatsAsync(db, ct);
            var sent = 0;
            foreach (var chat in chats)
                if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
            if (sent == 0)
                logger.LogWarning("MCP alert not delivered (no superadmin linked to the Telegram bot, or sending failed): {Alert}", text);
            return sent;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP alert failed: {Alert}", text);
            return 0;
        }
    }
}
