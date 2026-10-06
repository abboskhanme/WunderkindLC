using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QITUVCHI BOTI — o'qituvchi paneli (Mini App) uchun markazning ASOSIY botidan MUSTAQIL bot
/// (<c>.env: TEACHER_BOT_TOKEN</c>). Qoidalar: <c>.claude/rules/teacher-portal.md</c> §5.1.
///
/// <para>Botning butun ishi — <b>pastda BITTA tugma</b> (reply-klaviatura, <c>web_app</c>): bosilsa
/// o'qituvchi paneli Telegram ichida ochiladi. Kiritish maydoni CHAPIDAGI ko'k menyu tugmasi
/// ATAYIN YO'Q (foydalanuvchi talabi) — startupda tozalanadi
/// (<see cref="TeacherTelegramService.RemoveMenuButtonAsync"/>).</para>
///
/// <para>⚠️ Kirish ODDIY — panel ichida login/parol. Bot o'qituvchini tanimaydi va telefon
/// so'ramaydi: tugma har kimga ko'rinadi, lekin panelga faqat o'qituvchi akkaunti bilan kiriladi,
/// ya'ni yangi kirish yo'li OCHILMAYDI (asosiy botdagi tugma bilan bir xil qaror).</para>
///
/// Token bo'sh bo'lsa xizmat jim kutadi — asosiy bot va panel odatdagidek ishlaydi.
/// </summary>
public class TeacherBotService(TeacherTelegramService bot, ILogger<TeacherBotService> logger) : BackgroundService
{
    /// <summary>Pastdagi YAGONA tugma matni — asosiy botdagi bilan bir xil (o'qituvchi adashmasin).</summary>
    public const string OpenButtonText = TelegramBotService.PanelButtonText;

    /// <summary>Panel manzili — asosiy botdagi bilan AYNAN bir xil (ikki joyda ayrilib ketmasin).</summary>
    public const string PanelUrl = TelegramBotService.PanelUrl;

    /// <summary>
    /// Pastdagi doimiy klaviatura: BITTA qator, BITTA tugma — Mini App (<c>web_app</c>).
    /// <c>is_persistent</c> — klaviatura yopilib qolmaydi, tugma doim ko'rinadi.
    /// </summary>
    public static object Keyboard() => new
    {
        keyboard = new[] { new object[] { new { text = OpenButtonText, web_app = new { url = PanelUrl } } } },
        resize_keyboard = true,
        is_persistent = true,
    };

    public const string WelcomeText =
        "👋 <b>Assalomu alaykum!</b>\n\n"
        + "Bu — markazimizning <b>o'qituvchilar</b> boti.\n"
        + "Pastdagi «📱 Panel» tugmasini bosing: guruhlar, davomat, dars jadvali va maosh "
        + "shu yerda ochiladi.\n\n"
        + "Kirish — o'qituvchi login va parolingiz bilan.";

    public const string HintText = "Panelni ochish uchun pastdagi «📱 Panel» tugmasini bosing 👇";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long offset = 0;
        var started = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!bot.IsConfigured)
            {
                started = false;
                if (!await DelayAsync(15000, stoppingToken)) break;
                continue;
            }
            if (!started)
            {
                var username = await bot.RefreshUsernameAsync(stoppingToken);
                await bot.RemoveMenuButtonAsync(stoppingToken);
                logger.LogInformation("O'qituvchi boti ishga tushdi (long polling). @{User}, panel: {Url}", username, PanelUrl);
                started = true;
            }

            try
            {
                var updates = await bot.GetUpdatesAsync(offset, 30, stoppingToken);
                if (updates is null)
                {
                    if (!await DelayAsync(3000, stoppingToken)) break;
                    continue;
                }
                foreach (var upd in updates.Value.EnumerateArray())
                {
                    if (upd.TryGetProperty("update_id", out var idEl))
                        offset = idEl.GetInt64() + 1;
                    await HandleUpdateAsync(upd, stoppingToken);
                }
            }
            // Ilova to'xtatilyapti — chiqamiz; HttpClient timeout'i esa botni o'ldirmasin
            // (CareerBotService dagi bilan bir xil sabab).
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("O'qituvchi boti: update'ni qayta ishlashda xato ({Type}) — qayta urinamiz", ex.GetType().Name);
                if (!await DelayAsync(3000, stoppingToken)) break;
            }
        }
    }

    private static async Task<bool> DelayAsync(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private async Task HandleUpdateAsync(JsonElement upd, CancellationToken ct)
    {
        if (upd.TryGetProperty("callback_query", out var cq))
        {
            if (cq.TryGetProperty("id", out var cbId)) await bot.AnswerCallbackAsync(cbId.GetString() ?? "", ct);
            return;
        }

        if (!upd.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
        // Guruh/kanal — bot faqat shaxsiy suhbatda ishlaydi.
        if ((chat.TryGetProperty("type", out var tp) ? tp.GetString() : "") != "private") return;

        var text = (msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "").Trim();
        await bot.SendMessageAsync(chatIdEl.GetInt64(), ReplyFor(text), Keyboard(), ct, "HTML");
    }

    /// <summary>Qaysi matnga qanday javob: <c>/start</c> — xush kelibsiz, qolgani — qisqa yo'l-yo'riq.
    /// Har javob pastdagi tugma bilan keladi (klaviatura yo'qolgan bo'lsa ham qaytib chiqadi).</summary>
    public static string ReplyFor(string text) =>
        text.StartsWith("/start", StringComparison.Ordinal) ? WelcomeText : HintText;
}
