using System.Text.Json;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QITUVCHI BOTI (<see cref="TeacherBotService"/>, <c>.env: TEACHER_BOT_TOKEN</c>).
/// <para>Qulflanadi (foydalanuvchi talabi): pastda BITTA tugma va u o'qituvchi panelini Mini App
/// sifatida ochadi (asosiy botdagi bilan bir xil «📱 Panel»); chapdagi ko'k menyu tugmasi YO'Q; dev muhiti JONLI tokenni o'g'irlamaydi.</para>
/// </summary>
public class TeacherBotServiceTests
{
    private static JsonElement Json(object o) => JsonDocument.Parse(JsonSerializer.Serialize(o)).RootElement;

    [Fact]
    public void Pastda_BITTA_tugma__u_panelni_Mini_App_sifatida_ochadi()
    {
        var kb = Json(TeacherBotService.Keyboard());
        var rows = kb.GetProperty("keyboard");

        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(1, rows[0].GetArrayLength());
        var btn = rows[0][0];
        Assert.Equal(TelegramBotService.PanelButtonText, btn.GetProperty("text").GetString());
        Assert.Equal(TelegramBotService.PanelUrl, btn.GetProperty("web_app").GetProperty("url").GetString());
        Assert.False(btn.TryGetProperty("request_contact", out _));   // telefon so'ralmaydi
        Assert.True(kb.GetProperty("is_persistent").GetBoolean());    // tugma doim ko'rinadi
    }

    [Fact]
    public void Panel_manzili_https__asosiy_botdagi_bilan_AYNAN_bir_xil()
    {
        Assert.Equal(TelegramBotService.PanelUrl, TeacherBotService.PanelUrl);
        Assert.StartsWith("https://", TeacherBotService.PanelUrl);   // web_app faqat https qabul qiladi
    }

    [Fact]
    public void Chapdagi_kok_menyu_tugmasi_YOQ__standart_holatga_qaytariladi()
    {
        var menu = Json(TeacherTelegramService.MenuButtonPayload).GetProperty("menu_button");

        Assert.Equal("default", menu.GetProperty("type").GetString());
        Assert.False(menu.TryGetProperty("web_app", out _));
    }

    [Theory]
    [InlineData("/start", true)]
    [InlineData("/start abc", true)]
    [InlineData("salom", false)]
    [InlineData("", false)]
    public void Start_xush_kelibsiz__qolgani_yol_yoriq(string text, bool welcome)
    {
        Assert.Equal(welcome ? TeacherBotService.WelcomeText : TeacherBotService.HintText,
            TeacherBotService.ReplyFor(text));
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi topilmadi");
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Dev_muhiti_JONLI_tokenni_ishlatmaydi()
    {
        // Telegram har update'ni BIR marta beradi: lokal bot prod tokeni bilan getUpdates qilsa,
        // o'qituvchilar botidagi xabarlarni "o'g'irlab", jonli botni javobsiz qoldirardi.
        var dev = RepoFile("docker-compose.dev.yml");
        Assert.Contains("TeacherBot__BotToken: \"${DEV_TEACHER_BOT_TOKEN:-}\"", dev);
        Assert.DoesNotContain("${TEACHER_BOT_TOKEN", dev);
    }

    [Fact]
    public void Bot_ilovada_royxatdan_otgan()
    {
        var program = RepoFile("WunderkindLC.Server", "Program.cs");
        Assert.Contains("AddSingleton<TeacherTelegramService>()", program);
        Assert.Contains("AddHostedService<TeacherBotService>()", program);
    }
}
