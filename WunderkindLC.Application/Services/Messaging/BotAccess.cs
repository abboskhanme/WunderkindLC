namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// Who may use the bot (2026-10-13, owner: "login paroli bo'lmagan odam umuman foydalana olmasligi kerak").
///
/// <para>A private chat is LINKED when it has a <c>TelegramRegistration</c> — created only after a login/password (or
/// one-time code) sign-in inside the «📱 Panel» Mini App (<see cref="TelegramMiniAppLink"/>). An UNLINKED chat may only
/// start the bot (greeting + the Panel button) and link itself from the Panel (<c>web_app_data</c>, the
/// subscription re-check of /start). Books, support, one-time codes and every other button need a linked account;
/// anything else gets <see cref="TelegramBotService.GateText"/>. A phone number never grants access.</para>
/// </summary>
public static class BotAccess
{
    public enum Action
    {
        /// <summary>/start — the greeting with the Panel button.</summary>
        Start,
        /// <summary><c>web_app_data</c> from the Panel (<see cref="TelegramLinkTokens"/>) — how an unlinked chat links.</summary>
        LinkFromPanel,
        /// <summary>Any other message: text, button, contact, photo, document.</summary>
        Message,
        /// <summary>An inline-button press (<c>callback_query</c>).</summary>
        Callback,
    }

    /// <summary>The /start subscription re-check («✅ Tekshirish»); the old phone-flow value behaves the same.</summary>
    public static readonly IReadOnlySet<string> OpenCallbacks = new HashSet<string>(StringComparer.Ordinal)
    {
        "check_sub_start", "check_sub",
    };

    public static bool Allowed(bool linked, Action action, string? callbackData = null) =>
        linked
        || action is Action.Start or Action.LinkFromPanel
        || (action == Action.Callback && callbackData is not null && OpenCallbacks.Contains(callbackData));
}
