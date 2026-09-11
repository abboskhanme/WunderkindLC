using System.Text.Json;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Telegram botning XAVFSIZLIK tekshiruvlari.
///
/// Fon: bot telefon raqamini qabul qilib, uni markaz profiliga (o'quvchi/o'qituvchi/admin)
/// bog'laydi va o'sha chatga login/parol hamda bir martalik kirish kodini yuboradi. Shuning uchun
/// raqam FAQAT yuboruvchining O'ZIGA tegishli bo'lishi shart: Telegramda manzillar kitobidan
/// BEGONA odamning (hatto superadminning) kontaktini yuborish mumkin — bu to'liq akkaunt egallashga
/// olib kelardi. <see cref="TelegramBotService.IsOwnContact"/> shu darvozani yopadi.
/// </summary>
public class BotSecurityTests
{
    /// <summary>JSON matnidan <see cref="JsonElement"/> yasaydi (Telegram "message" obyekti).</summary>
    private static JsonElement Msg(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void IsOwnContact_OzKontakti_True()
    {
        // "📱 Telefon raqamni yuborish" tugmasi orqali yuborilgan kontakt: user_id == from.id.
        var msg = Msg("""
        {
          "from": { "id": 123456789, "first_name": "Ali" },
          "contact": { "phone_number": "998901234567", "user_id": 123456789 }
        }
        """);

        Assert.True(TelegramBotService.IsOwnContact(msg));
    }

    [Fact]
    public void IsOwnContact_BegonaKontakt_False()
    {
        // Hujum ssenariysi: hujumchi manzillar kitobidan BOSHQA odamning (masalan admin) kontaktini
        // yuboradi — user_id from.id ga teng emas.
        var msg = Msg("""
        {
          "from": { "id": 111, "first_name": "Hujumchi" },
          "contact": { "phone_number": "998901112233", "user_id": 999 }
        }
        """);

        Assert.False(TelegramBotService.IsOwnContact(msg));
    }

    [Fact]
    public void IsOwnContact_UserIdYoq_False()
    {
        // Telegramda ro'yxatdan o'tmagan raqam qo'lda kontakt sifatida yuborilsa user_id UMUMAN bo'lmaydi.
        // Egasini aniqlash imkoni yo'q — rad etamiz.
        var msg = Msg("""
        {
          "from": { "id": 111, "first_name": "Ali" },
          "contact": { "phone_number": "998901112233", "first_name": "Vali" }
        }
        """);

        Assert.False(TelegramBotService.IsOwnContact(msg));
    }

    [Fact]
    public void IsOwnContact_KontaktYoq_False()
    {
        var msg = Msg("""{ "from": { "id": 111 }, "text": "901234567" }""");

        Assert.False(TelegramBotService.IsOwnContact(msg));
    }

    [Fact]
    public void IsOwnContact_FromYoq_False()
    {
        // "from" bo'lmasa (masalan kanal nomidan yuborilgan xabar) — kimligini bilib bo'lmaydi.
        var msg = Msg("""{ "contact": { "phone_number": "998901112233", "user_id": 111 } }""");

        Assert.False(TelegramBotService.IsOwnContact(msg));
    }

    [Fact]
    public void IsOwnContact_UserIdSatrBolsa_FalseVaIstisnoYoq()
    {
        // Noto'g'ri tur (raqam o'rniga satr) — false qaytishi va ISTISNO TASHLAMASLIGI kerak,
        // aks holda bitta buzuq xabar bot siklini yiqitardi.
        var msg = Msg("""
        {
          "from": { "id": 111 },
          "contact": { "phone_number": "998901112233", "user_id": "111" }
        }
        """);

        var ex = Record.Exception(() => Assert.False(TelegramBotService.IsOwnContact(msg)));
        Assert.Null(ex);
    }

    [Fact]
    public void IsOwnContact_KontaktObyektEmas_False()
    {
        // Himoya: kutilmagan tuzilma (contact — null yoki massiv) ham xotirjam rad etilsin.
        Assert.False(TelegramBotService.IsOwnContact(Msg("""{ "from": { "id": 1 }, "contact": null }""")));
        Assert.False(TelegramBotService.IsOwnContact(Msg("""{ "from": { "id": 1 }, "contact": [] }""")));
    }
}
