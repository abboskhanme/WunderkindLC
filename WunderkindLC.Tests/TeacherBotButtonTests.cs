using System.Text.Json;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Botdagi «🎓 O'qituvchi paneli» tugmasi (Telegram Mini App, <c>web_app</c>).
/// <para>Qulflanadi: o'qituvchi paneli tugmasi FAQAT o'qituvchi klaviaturasida (o'quvchi/ota-onada esa
/// o'z «O'quvchi kabineti» tugmasi — 2026-10-07 uchrashuv talabi),
/// eng tepada turadi va HTTPS manzilga olib boradi — Telegram <c>web_app</c> uchun faqat https qabul
/// qiladi, http bo'lsa tugma yuborilmaydi va BUTUN klaviatura xabari rad etiladi.</para>
/// </summary>
public class TeacherBotButtonTests
{
    private static JsonElement Rows(object keyboard) =>
        JsonDocument.Parse(JsonSerializer.Serialize(keyboard)).RootElement.GetProperty("keyboard");

    private static IEnumerable<JsonElement> Buttons(JsonElement rows) =>
        rows.EnumerateArray().SelectMany(r => r.EnumerateArray());

    [Fact]
    public void Oqituvchi_klaviaturasida_panel_tugmasi_eng_tepada()
    {
        var rows = Rows(TelegramBotService.RegisteredKeyboard(books: true, teacher: true));
        var first = rows[0][0];
        Assert.Equal(TelegramBotService.TeacherPanelButtonText, first.GetProperty("text").GetString());
        Assert.Equal(TelegramBotService.TeacherPanelUrl, first.GetProperty("web_app").GetProperty("url").GetString());
    }

    [Fact]
    public void Oquvchi_va_ota_ona_klaviaturasida_panel_tugmasi_YOQ()
    {
        var rows = Rows(TelegramBotService.RegisteredKeyboard(books: true, teacher: false));
        Assert.DoesNotContain(Buttons(rows), b => b.TryGetProperty("web_app", out _));
    }

    [Fact]
    public void Oquvchi_ota_ona_klaviaturasida_KABINET_tugmasi__https_va_student_portaliga()
    {
        var rows = Rows(TelegramBotService.RegisteredKeyboard(books: true, teacher: false, student: true));
        var first = rows[0][0];
        Assert.Equal(TelegramBotService.StudentPanelButtonText, first.GetProperty("text").GetString());
        Assert.Equal(TelegramBotService.StudentPanelUrl, first.GetProperty("web_app").GetProperty("url").GetString());
        Assert.StartsWith("https://", TelegramBotService.StudentPanelUrl);
        Assert.EndsWith("/student", TelegramBotService.StudentPanelUrl);
        // O'qituvchi paneli o'quvchiga ko'rinmaydi.
        Assert.DoesNotContain(Buttons(rows), b => b.GetProperty("text").GetString() == TelegramBotService.TeacherPanelButtonText);
    }

    [Fact]
    public void Panel_manzili_https_va_oqituvchi_portaliga_olib_boradi()
    {
        Assert.StartsWith("https://", TelegramBotService.TeacherPanelUrl);
        Assert.EndsWith("/teacher", TelegramBotService.TeacherPanelUrl);

        var inline = JsonDocument.Parse(JsonSerializer.Serialize(TelegramBotService.TeacherPanelInline)).RootElement;
        var btn = inline.GetProperty("inline_keyboard")[0][0];
        Assert.Equal(TelegramBotService.TeacherPanelUrl, btn.GetProperty("web_app").GetProperty("url").GetString());
    }

    [Fact]
    public void Qolgan_tugmalar_joyida_qoladi()
    {
        var texts = Buttons(Rows(TelegramBotService.RegisteredKeyboard(books: true, teacher: true)))
            .Select(b => b.GetProperty("text").GetString()).ToList();
        Assert.Contains(OnlineTestBotService.TestButtonText, texts);
        Assert.Contains(BookShopBotService.BooksButtonText, texts);
        Assert.Equal(5, texts.Count);   // panel · kod · test · kitob · adminga murojaat
    }
}
