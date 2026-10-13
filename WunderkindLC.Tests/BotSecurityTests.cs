using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Botdan FAQAT login/parol bilan Panel'ga kirib ulangan chat foydalanadi (2026-10-13, egasining qarori). Ilgari bot
/// telefon raqami (kontakt) bilan ro'yxatdan o'tkazib, kirish ma'lumotlarini yuborardi — bu yo'l OLIB TASHLANDI: raqam
/// hech qachon kirish bermaydi, ulanmagan chatga kitob, adminga murojaat, kod va boshqa tugmalar yopiq.
/// </summary>
public class BotSecurityTests
{
    [Theory]
    [InlineData(BotAccess.Action.Start, true)]
    [InlineData(BotAccess.Action.LinkFromPanel, true)]
    [InlineData(BotAccess.Action.Message, false)]
    [InlineData(BotAccess.Action.Callback, false)]
    public void Ulanmagan_chat_faqat_start_va_Panel_orqali_ulanish(BotAccess.Action action, bool allowed) =>
        Assert.Equal(allowed, BotAccess.Allowed(linked: false, action, callbackData: "kitob:1"));

    [Theory]
    [InlineData("check_sub_start", true)]
    [InlineData("check_sub", true)]
    [InlineData("support", false)]
    [InlineData("check_sub_menu", false)]
    [InlineData("mack:x", false)]
    [InlineData(null, false)]
    public void Ulanmagan_chatda_faqat_start_obunasini_tekshirish_tugmasi(string? data, bool allowed) =>
        Assert.Equal(allowed, BotAccess.Allowed(linked: false, BotAccess.Action.Callback, data));

    [Theory]
    [InlineData(BotAccess.Action.Message)]
    [InlineData(BotAccess.Action.Callback)]
    public void Ulangan_chat_hamma_narsadan_foydalanadi(BotAccess.Action action) =>
        Assert.True(BotAccess.Allowed(linked: true, action, "support"));

    [Fact]
    public void Telefon_orqali_royxatdan_otish_YOQ_va_darvoza_kitob_supportdan_OLDIN()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Application", "Services", "TelegramBotService.cs"));

        // No phone → account matching and no credentials sent from the bot anymore.
        Assert.DoesNotContain("MatchStudentsAsync", src);
        Assert.DoesNotContain("request_contact", src);
        Assert.DoesNotContain("InitialPassword", src);

        // The gate runs before every linked-only feature of a message.
        var gate = src.IndexOf("BotAccess.Allowed(await IsLinkedChatAsync(chatId, ct), BotAccess.Action.Message)", StringComparison.Ordinal);
        Assert.True(gate > 0);
        foreach (var feature in new[] { "HandleFileMessageAsync(chatId, msg, ct)", "HandleSupportCommandAsync(chatId, ct);",
                     "HandleOtpRequestAsync(chatId, ct);", "bookShop.ShowCatalogAsync(db, chatId, ct)", "msg.TryGetProperty(\"contact\"" })
            Assert.True(src.IndexOf(feature, StringComparison.Ordinal) > gate, feature);
        // …and before every callback except «Qabul qildim» (the ack service checks the chat itself).
        var cbGate = src.IndexOf("BotAccess.Allowed(await IsLinkedChatAsync(chatId, ct), BotAccess.Action.Callback, data)", StringComparison.Ordinal);
        Assert.True(cbGate > 0 && src.IndexOf("bookShop.OpenBookAsync", StringComparison.Ordinal) > cbGate);
    }
}
