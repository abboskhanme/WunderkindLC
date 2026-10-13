using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// One-time tokens for linking a chat from a Mini App opened by the REPLY-KEYBOARD «📱 Panel» button (2026-10-13).
///
/// <para>Telegram gives such launches NO <c>initData</c> (core.telegram.org/bots/webapps: the launch data "is empty if
/// the Mini App was launched from a keyboard button"), so <see cref="TelegramMiniAppLink"/> cannot learn the chat. Those
/// launches may instead call <c>Telegram.WebApp.sendData</c>: Telegram posts it as a <c>web_app_data</c> service message
/// into the user's own chat with the bot. The logged-in panel asks the server for a token
/// (<c>POST api/telegram/link-token</c>), sends <c>wlclink:{token}</c>, and the bot redeems it for that chat.</para>
///
/// <para>Bound to the account that minted it; 128+ random bits; one-time; 10 minutes. In memory — a restart only
/// means "press the button again".</para>
/// </summary>
public sealed class TelegramLinkTokens(Func<DateTime>? now = null)
{
    public const string DataPrefix = "wlclink:";
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private const int MaxTokens = 10_000;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly ConcurrentDictionary<string, (string UserId, string Role, DateTime ExpiresUtc)> _tokens = new();

    /// <summary>The <c>sendData</c> payload for this account.</summary>
    public string Issue(string userId, string role)
    {
        var now = _now();
        if (_tokens.Count > MaxTokens / 2)
            foreach (var kv in _tokens)
                if (kv.Value.ExpiresUtc <= now) _tokens.TryRemove(kv.Key, out _);
        if (_tokens.Count >= MaxTokens) throw new InvalidOperationException("Juda ko'p so'rov — birozdan keyin urinib ko'ring");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _tokens[token] = (userId, role, now + Ttl);
        return DataPrefix + token;
    }

    /// <summary>The account behind a <c>web_app_data</c> payload, or null (unknown, used or expired).</summary>
    public (string UserId, string Role)? Redeem(string? data)
    {
        if (data is null || !data.StartsWith(DataPrefix, StringComparison.Ordinal)) return null;
        if (!_tokens.TryRemove(data[DataPrefix.Length..], out var v)) return null;
        return v.ExpiresUtc > _now() ? (v.UserId, v.Role) : null;
    }
}
