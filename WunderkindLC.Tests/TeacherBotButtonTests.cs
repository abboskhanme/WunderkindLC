using System.Text.Json;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Botdagi YAGONA «📱 Panel» tugmasi (Telegram Mini App, <c>web_app</c>).
/// <para>Qulflanadi (foydalanuvchi talabi, 2026-10-07): o'qituvchi ham, o'quvchi/ota-ona ham BITTA tugmani
/// ko'radi; u eng tepada, sayt ILDIZIGA (rol bo'yicha yo'naltirish — <c>RootRedirect</c>) va HTTPS manzilga
/// olib boradi — Telegram <c>web_app</c> uchun faqat https qabul qiladi, http bo'lsa tugma yuborilmaydi va
/// BUTUN klaviatura xabari rad etiladi.</para>
/// </summary>
public class TeacherBotButtonTests
{
    private static JsonElement Rows(object keyboard) =>
        JsonDocument.Parse(JsonSerializer.Serialize(keyboard)).RootElement.GetProperty("keyboard");

    private static IEnumerable<JsonElement> Buttons(JsonElement rows) =>
        rows.EnumerateArray().SelectMany(r => r.EnumerateArray());

    [Fact]
    public void Panel_tugmasi_eng_tepada_va_BITTA()
    {
        var rows = Rows(TelegramBotService.RegisteredKeyboard(books: true, panel: true));
        var first = rows[0][0];
        Assert.Equal(TelegramBotService.PanelButtonText, first.GetProperty("text").GetString());
        Assert.Equal(TelegramBotService.PanelUrl, first.GetProperty("web_app").GetProperty("url").GetString());
        Assert.Single(Buttons(rows), b => b.TryGetProperty("web_app", out _));
    }

    [Fact]
    public void Ochuvchi_bolmagan_chatda_panel_tugmasi_YOQ()
    {
        var rows = Rows(TelegramBotService.RegisteredKeyboard(books: true, panel: false));
        Assert.DoesNotContain(Buttons(rows), b => b.TryGetProperty("web_app", out _));
    }

    [Fact]
    public void Panel_manzili_https_va_sayt_ILDIZI__portalni_rol_tanlaydi()
    {
        Assert.StartsWith("https://", TelegramBotService.PanelUrl);
        Assert.EndsWith("/", TelegramBotService.PanelUrl);
        Assert.DoesNotContain("/teacher", TelegramBotService.PanelUrl);
        Assert.DoesNotContain("/student", TelegramBotService.PanelUrl);

        var inline = JsonDocument.Parse(JsonSerializer.Serialize(TelegramBotService.PanelInline)).RootElement;
        var btn = inline.GetProperty("inline_keyboard")[0][0];
        Assert.Equal(TelegramBotService.PanelUrl, btn.GetProperty("web_app").GetProperty("url").GetString());
    }

    [Fact]
    public void Salomlashuvda_panel_satri_rolga_qarab__lekin_tugma_bitta()
    {
        Assert.Contains("maosh", TelegramBotService.PanelGreetingLine(teacher: true, student: false));
        Assert.Contains("test natijalari", TelegramBotService.PanelGreetingLine(teacher: false, student: true));
        Assert.Contains(TelegramBotService.PanelButtonText, TelegramBotService.PanelGreetingLine(true, true));
        Assert.Equal("", TelegramBotService.PanelGreetingLine(false, false));
    }

    [Fact]
    public void Qolgan_tugmalar_joyida_qoladi()
    {
        var texts = Buttons(Rows(TelegramBotService.RegisteredKeyboard(books: true, panel: true)))
            .Select(b => b.GetProperty("text").GetString()).ToList();
        Assert.Contains(OnlineTestBotService.TestButtonText, texts);
        Assert.Contains(BookShopBotService.BooksButtonText, texts);
        Assert.Equal(5, texts.Count);   // panel · kod · test · kitob · adminga murojaat
    }
}
