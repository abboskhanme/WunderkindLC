using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM upgrade P2.3 — Telegram HTML (<see cref="TelegramHtml"/>), rendering with escaped token values
/// (<see cref="MessageRender"/>), relations (<see cref="TelegramRelations"/>), send-error classification and the
/// auto-message schedule. All pure.
/// </summary>
public class TelegramHtmlTests
{
    private static Student Student(string first = "Ali", string full = "Aliyev Ali") => new()
    {
        FirstName = first, FullName = full, LastName = "Aliyev",
        Phone = "+998-90-111-22-33", ParentPhone = "+998-90-444-55-66",
        FatherPhone = "+998-90-444-55-66", MotherPhone = "+998-91-777-88-99",
    };

    // ---------------- SanitizeTemplate ----------------

    [Fact]
    public void Ruxsat_etilgan_teglar_saqlanadi_aliaslar_normallashtiriladi()
    {
        var (html, errors) = TelegramHtml.SanitizeTemplate("<strong>A</strong> <em>B</em> <ins>C</ins> <del>D</del> <B>E</B>");
        Assert.Empty(errors);
        Assert.Equal("<b>A</b> <i>B</i> <u>C</u> <s>D</s> <b>E</b>", html);
    }

    [Fact]
    public void Script_va_notanish_teglar_matn_sifatida_escape_qilinadi()
    {
        var (html, errors) = TelegramHtml.SanitizeTemplate("<script>alert(1)</script> <span class=\"x\">y</span>");
        Assert.Empty(errors);
        Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt; &lt;span class=\"x\"&gt;y&lt;/span&gt;", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void Ampersand_escape_qilinadi_lekin_tayyor_entity_saqlanadi()
    {
        Assert.Equal("Q&amp;A", TelegramHtml.SanitizeTemplate("Q&A").Html);
        Assert.Equal("a &amp; b", TelegramHtml.SanitizeTemplate("a &amp; b").Html);
        Assert.Equal("&lt;3 &#128512; &#x1F600;", TelegramHtml.SanitizeTemplate("&lt;3 &#128512; &#x1F600;").Html);
    }

    [Fact]
    public void Yolgiz_qavslar_escape_qilinadi()
    {
        var (html, errors) = TelegramHtml.SanitizeTemplate("3 < 5 va 7 > 2");
        Assert.Empty(errors);
        Assert.Equal("3 &lt; 5 va 7 &gt; 2", html);
    }

    [Fact]
    public void Yopilmagan_teg_xato_beradi()
    {
        var (_, errors) = TelegramHtml.SanitizeTemplate("<b>Salom");
        Assert.Equal(TelegramHtml.UnclosedError("b"), Assert.Single(errors));
        Assert.Equal("Formatlash yopilmagan: <b>", errors[0]);
    }

    [Fact]
    public void Notogri_ichma_ich_teg_xato_beradi()
    {
        var (_, errors) = TelegramHtml.SanitizeTemplate("<b><i>x</b></i>");
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.StartsWith("Formatlash yopilmagan"));
    }

    [Fact]
    public void Javascript_havola_xato_http_havola_togri()
    {
        var bad = TelegramHtml.SanitizeTemplate("<a href=\"javascript:alert(1)\">x</a>");
        Assert.Equal("Havola noto'g'ri: faqat http(s) manzil", Assert.Single(bad.Errors));
        Assert.DoesNotContain("<a href", bad.Html);

        var ok = TelegramHtml.SanitizeTemplate("<a href=\"https://wunderkind.uz/a?b=1&c=2\" target=\"_blank\">sayt</a>");
        Assert.Empty(ok.Errors);
        Assert.Equal("<a href=\"https://wunderkind.uz/a?b=1&amp;c=2\">sayt</a>", ok.Html);   // other attributes dropped

        Assert.Empty(TelegramHtml.SanitizeTemplate("<a href=\"tg://resolve?domain=x\">t</a>").Errors);
        Assert.NotEmpty(TelegramHtml.SanitizeTemplate("<a href=\"/relative\">r</a>").Errors);
    }

    [Fact]
    public void ToPlain_teglarni_olib_tashlaydi_va_entitylarni_ochadi()
    {
        Assert.Equal("Salom <Ali> & do'st", TelegramHtml.ToPlain("<b>Salom</b> &lt;Ali&gt; &amp; do'st"));
        Assert.Equal(5, TelegramHtml.VisibleLength("<b>A&amp;B</b>xy"));
    }

    [Fact]
    public void Escape_null_bosh_satr()
    {
        Assert.Equal("", TelegramHtml.Escape(null));
        Assert.Equal("&lt;b&gt; &amp; &quot;", TelegramHtml.Escape("<b> & \""));
    }

    [Fact]
    public void Yetkazish_rejimi_izoh_yoki_alohida()
    {
        Assert.Equal("text", TelegramHtml.DeliveryMode(false, 5000));
        Assert.Equal("caption", TelegramHtml.DeliveryMode(true, 1024));
        Assert.Equal("separate", TelegramHtml.DeliveryMode(true, 1025));
    }

    [Fact]
    public void PlainCut_4000_belgida_qirqadi()
    {
        var cut = TelegramHtml.PlainCut("<b>" + new string('a', 5000) + "</b>", 4000);
        Assert.Equal(4001, TelegramHtml.ToPlain(cut).Length);
        Assert.EndsWith("…", cut);
    }

    // ---------------- MessageRender: values are escaped ----------------

    [Fact]
    public void Oquvchi_ismi_teg_bolsa_ham_MATN_bolib_chiqadi()
    {
        var s = Student(first: "Ali <b>", full: "Ali <b> Aliyev");
        var (html, errors) = MessageRender.RenderStudent("<b>Salom</b> {ism}", s, null, null, null, null, "Markaz");
        Assert.Empty(errors);
        Assert.Equal("<b>Salom</b> Ali &lt;b&gt;", html);
        Assert.Equal("Salom Ali <b>", TelegramHtml.ToPlain(html));
    }

    [Fact]
    public void Qiymatdagi_ampersand_va_qavslar_escape()
    {
        var (html, _) = MessageRender.RenderStudent("{markaz}", Student(), null, null, null, null, "A & B <C>");
        Assert.Equal("A &amp; B &lt;C&gt;", html);
    }

    [Fact]
    public void Qarindosh_tokeni_ulanishdan_toladi()
    {
        var reg = new TelegramRegistration { Relation = TelegramRelations.Mother, ParentName = "Ona", Phone = "998917778899" };
        var (html, _) = MessageRender.RenderStudent("{qarindosh}: {ota-ona}", Student(), reg, null, null, null, "");
        Assert.Equal("Onasi: Ona", html);
    }

    [Fact]
    public void Talaba_tokenlari_aniqlanadi()
    {
        Assert.False(MessageRender.HasStudentTokens("Ertaga dars yo'q. {markaz}"));
        Assert.True(MessageRender.HasStudentTokens("{ism}, ertaga dars yo'q"));
        Assert.True(MessageRender.HasStudentTokens("{GURUH}"));
        Assert.True(MessageRender.HasStudentTokens("{dars_vaqti}"));
        Assert.True(MessageRender.HasStudentTokens("{qarzdorlik}"));
    }

    [Fact]
    public void Notanish_tokenlar_royxati()
    {
        Assert.Equal(new[] { "{ismi}", "{link}" }, MessageRender.UnknownTokens("{ism} {ismi} {link} {ismi}"));
        Assert.Empty(MessageRender.UnknownTokens("{ism} {ota_ona} {qarindosh} {yutuq} {ball}"));
        Assert.Equal("Noma'lum o'zgaruvchi: {ismi}", MessageRender.UnknownTokensError(new[] { "{ismi}" }));
    }

    // ---------------- Relations ----------------

    [Fact]
    public void Bir_xil_raqamda_OTA_oquvchidan_oldin()
    {
        var s = Student();
        s.Phone = s.FatherPhone;
        Assert.Equal(TelegramRelations.Father, TelegramRelations.Detect(s, "998904445566"));
    }

    [Fact]
    public void ParentPhone_onaga_teng_bolsa_ONA()
    {
        var s = Student();
        s.FatherPhone = "";
        s.ParentPhone = s.MotherPhone;
        Assert.Equal(TelegramRelations.Mother, TelegramRelations.Detect(s, "+998 91 777 88 99"));
    }

    [Fact]
    public void Mos_kelmasa_BOSHQA_qisqa_raqam_hisobga_olinmaydi()
    {
        var s = Student();
        Assert.Equal(TelegramRelations.Other, TelegramRelations.Detect(s, "998935554433"));
        Assert.Equal(TelegramRelations.Other, TelegramRelations.Detect(s, "12345"));
        s.Phone = "123";
        Assert.Equal(TelegramRelations.Other, TelegramRelations.Detect(s, "123"));
        Assert.Equal(TelegramRelations.StudentSelf, TelegramRelations.Detect(Student(), "901112233"));
    }

    [Fact]
    public void Ota_onalik_va_yorliqlar()
    {
        Assert.True(TelegramRelations.IsParent(""));
        Assert.True(TelegramRelations.IsParent(TelegramRelations.Other));
        Assert.False(TelegramRelations.IsParent(TelegramRelations.StudentSelf));
        Assert.Equal("Otasi", TelegramRelations.Label("father"));
        Assert.Equal("Onasi", TelegramRelations.Label("mother"));
        Assert.Equal("Ota-onasi", TelegramRelations.Label("parent"));
        Assert.Equal("O'zi", TelegramRelations.Label("student"));
        Assert.Equal("Boshqa", TelegramRelations.Label("other"));
    }

    // ---------------- Send error classification ----------------

    [Theory]
    [InlineData(403, "Forbidden: bot was blocked by the user", TgSendResult.Blocked)]
    [InlineData(403, "", TgSendResult.Blocked)]
    [InlineData(400, "Bad Request: chat not found", TgSendResult.Blocked)]
    [InlineData(403, "Forbidden: user is deactivated", TgSendResult.Blocked)]
    [InlineData(403, "Forbidden: bot can't initiate conversation with a user", TgSendResult.Blocked)]
    [InlineData(429, "Too Many Requests: retry after 5", TgSendResult.RateLimited)]
    [InlineData(400, "Bad Request: can't parse entities: Unsupported start tag", TgSendResult.BadMarkup)]
    [InlineData(500, "Internal Server Error", TgSendResult.Failed)]
    [InlineData(200, null, TgSendResult.Failed)]
    public void Yuborish_xatosi_tasniflanadi(int status, string? description, TgSendResult expected) =>
        Assert.Equal(expected, TelegramService.ClassifySendError(status, description));

    // ---------------- Schedule ----------------

    [Fact]
    public void Jadval_rejimlari()
    {
        Assert.Equal(AutoMessageSchedule.MonthlyDayTime, AutoMessageSchedule.ModeOf(AutoMessageTriggers.PaymentDebt));
        Assert.Equal(AutoMessageSchedule.MonthlyDayTime, AutoMessageSchedule.ModeOf(AutoMessageTriggers.RatingTop));
        Assert.Equal(AutoMessageSchedule.MonthlyDayTime, AutoMessageSchedule.ModeOf(AutoMessageTriggers.RatingBottom));
        Assert.Equal(AutoMessageSchedule.DailyTime, AutoMessageSchedule.ModeOf(AutoMessageTriggers.Birthday));
        Assert.Equal(AutoMessageSchedule.Custom, AutoMessageSchedule.ModeOf(AutoMessageTriggers.CustomSchedule));
        Assert.Equal(AutoMessageSchedule.None, AutoMessageSchedule.ModeOf(AutoMessageTriggers.AttendanceAbsent));
        Assert.Equal(AutoMessageSchedule.MonthlyDayTime, AutoMessageTriggers.Get(AutoMessageTriggers.PaymentDebt)!.ScheduleMode);
    }

    [Fact]
    public void Kunlik_qoida_soati_otgach()
    {
        var rule = new AutoMessageRule { Trigger = AutoMessageTriggers.Birthday, ScheduleTime = "09:30" };
        Assert.False(AutoMessageSchedule.IsDue(rule, new DateTime(2026, 10, 12, 9, 29, 0)));
        Assert.True(AutoMessageSchedule.IsDue(rule, new DateTime(2026, 10, 12, 9, 30, 0)));
        Assert.True(AutoMessageSchedule.TryParseTime("23:59", out _));
        Assert.False(AutoMessageSchedule.TryParseTime("24:00", out _));
        Assert.False(AutoMessageSchedule.TryParseTime("9", out _));
    }
}
