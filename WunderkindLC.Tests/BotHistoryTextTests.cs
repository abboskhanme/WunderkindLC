using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// M4 (2026-10-12): the bot's login/password message and one-time login codes are NEVER kept in plain text in the
/// conversation history (<c>BotSupportMessages.Text</c>, <c>BotUser.LastText</c>) nor in the JSON backup.
/// </summary>
public class BotHistoryTextTests
{
    private const string LoginWithPassword =
        "🔑 Kirish ma'lumotlari:\nLogin: <code>ali.valiyev</code>\nParol: <code>Qx7-secret</code>\nTizimga kirish uchun web-versiya\n🌐 https://x";
    private const string LoginOnly =
        "🔑 Login: <code>ali.valiyev</code>\nParolingizni avval olgansiz.\nTizimga kirish uchun web-versiya\n🌐 https://x";
    private const string OldLogin = "🔑 Ilovaga kirish:\nLogin: ali\nParol: Qx7-secret";
    private const string Otp =
        "🔑 Ali Valiyev uchun bir martalik kirish kodi:\n<code>AB12CD34</code>\n⏱ 1 daqiqa amal qiladi, faqat bir marta ishlatiladi.";

    [Theory]
    [InlineData(LoginWithPassword, BotHistoryText.LoginPlaceholder)]
    [InlineData(LoginOnly, BotHistoryText.LoginPlaceholder)]
    [InlineData(OldLogin, BotHistoryText.LoginPlaceholder)]
    [InlineData(Otp, BotHistoryText.OtpPlaceholder)]
    public void Credential_messages_become_placeholders(string text, string expected)
    {
        Assert.Equal(expected, BotHistoryText.ForHistory(text));
        Assert.True(BotHistoryText.IsSecret(text));
        // The 140-char preview (LastText) of the cut text is scrubbed too.
        Assert.Equal(expected, BotHistoryText.Preview(text));
        Assert.Equal(expected, BotHistoryText.ForHistory(text[..Math.Min(140, text.Length)]));
    }

    [Theory]
    [InlineData("✍️ Savol yoki murojaatingizni yozib yuboring — administrator javob beradi.")]
    [InlineData("🔑 Yangi kod olish")]                       // the user's button press
    [InlineData("🔑 Instagram: tokenni yangilab bo'lmadi")]   // not a bot-history template
    [InlineData("Login: ali")]
    [InlineData("")]
    public void Ordinary_texts_are_kept_as_is(string text)
    {
        Assert.Equal(text, BotHistoryText.ForHistory(text));
        Assert.False(BotHistoryText.IsSecret(text));
    }

    [Fact]
    public void Null_is_empty() => Assert.Equal("", BotHistoryText.ForHistory(null));

    [Fact]
    public async Task One_time_scrub_replaces_stored_credentials_and_keeps_everything_else()
    {
        using var db = TestDb.Sqlite();
        var c = db.Context;
        c.BotSupportMessages.AddRange(
            new BotSupportMessage { ChatId = 1, FromUser = false, AdminName = "Bot", Text = LoginWithPassword, CreatedAt = "2026-10-01T09:00:00" },
            new BotSupportMessage { ChatId = 1, FromUser = false, AdminName = "Bot", Text = Otp, CreatedAt = "2026-10-01T09:01:00" },
            new BotSupportMessage { ChatId = 1, FromUser = false, AdminName = "Bot", Text = "✅ Murojaatingiz qabul qilindi.", CreatedAt = "2026-10-01T09:02:00" },
            new BotSupportMessage { ChatId = 1, FromUser = true, AdminName = "", Text = "🔑 Yangi kod olish", CreatedAt = "2026-10-01T09:03:00" });
        c.BotUsers.AddRange(
            new BotUser { ChatId = 1, LastText = LoginWithPassword[..Math.Min(140, LoginWithPassword.Length)] },
            new BotUser { ChatId = 2, LastText = "Salom" });
        await c.SaveChangesAsync();

        var n = await BotHistoryText.ScrubStoredAsync(c);
        Assert.Equal(3, n);

        var texts = await c.BotSupportMessages.AsNoTracking().OrderBy(m => m.CreatedAt).Select(m => m.Text).ToListAsync();
        Assert.Equal(
            [BotHistoryText.LoginPlaceholder, BotHistoryText.OtpPlaceholder, "✅ Murojaatingiz qabul qilindi.", "🔑 Yangi kod olish"],
            texts);
        Assert.Equal(BotHistoryText.LoginPlaceholder, (await c.BotUsers.AsNoTracking().SingleAsync(u => u.ChatId == 1)).LastText);
        Assert.Equal("Salom", (await c.BotUsers.AsNoTracking().SingleAsync(u => u.ChatId == 2)).LastText);

        // Guarded by a SeedMarker — the second run does nothing.
        Assert.Null(await BotHistoryText.ScrubStoredAsync(c));
        Assert.True(await c.SeedMarkers.AnyAsync(m => m.Key == BotHistoryText.ScrubMarkerKey));
    }

    [Fact]
    public async Task Json_backup_never_carries_a_bot_credential_message()
    {
        using var db = TestDb.Sqlite();
        db.Context.BotSupportMessages.Add(new BotSupportMessage
        {
            ChatId = 7, FromUser = false, AdminName = "Bot", Text = LoginWithPassword, CreatedAt = "2026-10-01T09:00:00",
        });
        db.Context.BotUsers.Add(new BotUser { ChatId = 7, LastText = Otp });
        await db.Context.SaveChangesAsync();

        var (bytes, _, _) = await BackupService.BuildJsonAsync(db.Context);
        var json = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("Qx7-secret", json);
        Assert.DoesNotContain("AB12CD34", json);
        Assert.Contains("kirish ma'lumotlari yuborildi", json);

        // The live rows are untouched by the backup (AsNoTracking projection).
        Assert.Equal(LoginWithPassword, (await db.Context.BotSupportMessages.AsNoTracking().SingleAsync()).Text);
    }

    [Fact]
    public void Bot_send_path_stores_only_the_scrubbed_copy()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Application", "Services", "TelegramBotService.cs"));
        var i = src.IndexOf("private async Task<bool> SendAsync(", StringComparison.Ordinal);
        Assert.True(i > 0);
        var body = src[i..src.IndexOf("\n    }", i, StringComparison.Ordinal)];
        Assert.Contains("Text = BotHistoryText.ForHistory(text)", body);
        Assert.Contains("botUser.LastText = BotHistoryText.Preview(text);", body);
        // Every bot reply writes the history only through SendAsync (no second, unscrubbed writer of bot replies).
        Assert.Single(Regex.Matches(src, @"FromUser = false"));

        var setup = File.ReadAllText(Path.Combine(dir.FullName, "WunderkindLC.Server", "Crm", "CrmMessagingSetup.cs"));
        Assert.Contains("BotHistoryText.ScrubStoredAsync(db)", setup);
    }
}
