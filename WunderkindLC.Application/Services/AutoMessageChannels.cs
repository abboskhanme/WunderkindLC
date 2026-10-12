using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Delivery channels of an auto-message rule (<see cref="AutoMessageRule"/>) after the 2026-10
/// cleanup: SMS, push and the CTI (Local SMS) channel were removed — Telegram is the ONLY outbound
/// channel (owner decision, <c>docs/ASSUMPTIONS.md</c> 2026-10-11).
///
/// <para><b>Two deliveries per rule:</b></para>
/// <list type="number">
///   <item><b>Cabinet notification</b> (<see cref="NotificationStore"/>, the bell in the student /
///   teacher web cabinet) — ALWAYS written for every enabled rule when the recipient has an
///   account. It used to be written only inside the push branch (<c>SendPush</c>); a superset is
///   kept so that no rule loses its cabinet history.</item>
///   <item><b>Telegram</b> — when <see cref="Telegram"/> is true.</item>
/// </list>
/// </summary>
public static class AutoMessageChannels
{
    /// <summary>
    /// Does the rule deliver over Telegram? A legacy SMS rule (<c>SendSms</c>, saved before
    /// 2026-10) is treated as a Telegram rule: Telegram replaced SMS, so birthday greetings, debt
    /// reminders and the like keep reaching the families. The admin's next save of the rule clears
    /// the legacy flag (<c>AutoMessagesController.Apply</c>), after which only <c>SendTelegram</c>
    /// decides.
    /// </summary>
    public static bool Telegram(AutoMessageRule rule) => rule.SendTelegram || rule.SendSms;
}
