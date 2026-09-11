using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KARYERA BOTI (Wunderkind Career) — markazning ASOSIY botidan MUSTAQIL ikkinchi bot
/// (<c>.env: CAREER_BOT_TOKEN</c>). Butun ish oqimi Mini App'da (<c>/vakansiya</c> — statik
/// HTML/CSS/Bootstrap sahifa), bot esa faqat "eshik" vazifasini bajaradi:
/// <list type="bullet">
///   <item><c>/start</c> — xush kelibsiz xabari + <b>inline tugma</b> (<c>web_app</c>), bosilganda
///     ilova Telegram ichida ochiladi; qo'shimcha ravishda doimiy reply-klaviatura
///     (ilovani ochish / telefonni ulashish).</item>
///   <item>telefon ulashilsa — <see cref="CareerBotUser"/> ga yoziladi va Mini App'dagi ariza
///     formasi uni oldindan to'ldiradi (nomzod qayta yozmasin).</item>
///   <item>ariza bosqichi o'zgarganda nomzodga xabar — <see cref="CareerService.NotifyCandidateAsync"/>.</item>
/// </list>
/// Token bo'sh bo'lsa xizmat jim kutadi (CRM va asosiy bot odatdagidek ishlaydi).
/// </summary>
public class CareerBotService(
    IServiceProvider sp, CareerTelegramService bot, IConfiguration config,
    ILogger<CareerBotService> logger) : BackgroundService
{
    /// <summary>Reply-klaviaturadagi "ilovani ochish" tugmasi (web_app).</summary>
    private const string OpenButtonText = "💼 Vakansiyalar";
    private const string PhoneButtonText = "📱 Telefon raqamni ulashish";

    /// <summary>Mini App manzili. <c>Career:MiniAppUrl</c> (env <c>CAREER_MINIAPP_URL</c>) berilsa —
    /// o'sha; bo'lmasa <c>App:Host</c> dan yasaladi (<c>https://&lt;host&gt;/vakansiya</c>).</summary>
    public string MiniAppUrl
    {
        get
        {
            var explicitUrl = (config["Career:MiniAppUrl"] ?? config["CAREER_MINIAPP_URL"] ?? "").Trim();
            if (explicitUrl.Length > 0) return explicitUrl.TrimEnd('/');
            var host = (config["App:Host"] ?? config["APP_HOST"] ?? "").Trim();
            return host.Length > 0 ? $"https://{host}/vakansiya" : "";
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long offset = 0;
        var announced = false;
        var menuSet = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!bot.IsConfigured)
            {
                announced = false;
                if (!await DelayAsync(15000, stoppingToken)) break;
                continue;
            }
            if (!announced)
            {
                var username = await bot.RefreshUsernameAsync(stoppingToken);
                logger.LogInformation(
                    "Karyera boti ishga tushdi (long polling). @{User}, Mini App: {Url}",
                    username, MiniAppUrl);
                announced = true;
            }
            if (!menuSet && MiniAppUrl.Length > 0)
            {
                await bot.SetMenuButtonAsync(MiniAppUrl, "Vakansiyalar", stoppingToken);
                menuSet = true;
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
            // ⚠️ Bekor qilish IKKI xil bo'ladi va ularni ajratish SHART:
            //  1) ilova to'xtatilyapti (stoppingToken) — siklni chindan ham tugatamiz;
            //  2) HttpClient timeout'i — u ham TaskCanceledException (ya'ni OperationCanceledException)
            //     chiqaradi. Ilgari bu yerda shartsiz `break` turardi: bitta timeout butun karyera
            //     botini o'ldirar va u ilova qayta ishga tushmaguncha tirilmasdi.
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(ex, "Karyera boti: getUpdates uzildi (timeout) — qayta urinamiz");
                if (!await DelayAsync(3000, stoppingToken)) break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Karyera boti: getUpdates xatosi");
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
        // Inline tugma bosilishi — hozircha faqat "soatchani" to'xtatamiz (butun oqim Mini App'da).
        if (upd.TryGetProperty("callback_query", out var cq))
        {
            if (cq.TryGetProperty("id", out var cbId))
                await bot.AnswerCallbackAsync(cbId.GetString() ?? "", null, ct);
            return;
        }

        if (!upd.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
        var chatId = chatIdEl.GetInt64();

        // Guruh/kanal — karyera boti faqat shaxsiy suhbatda ishlaydi.
        var chatType = chat.TryGetProperty("type", out var ctp) ? ctp.GetString() ?? "" : "";
        if (chatType != "private") return;

        await TouchUserAsync(chatId, msg, null, ct);

        // Telefon ulashildi.
        if (msg.TryGetProperty("contact", out var contact))
        {
            var phone = contact.TryGetProperty("phone_number", out var p) ? p.GetString() ?? "" : "";
            await TouchUserAsync(chatId, msg, PhoneUtil.Normalize(phone), ct);
            await bot.SendMessageAsync(chatId,
                "✅ Rahmat! Raqamingiz saqlandi — ariza formasida u avtomatik to'ldiriladi.\n\n"
                + $"Endi «{OpenButtonText}» tugmasi orqali ilovani oching.",
                MainKeyboard(), ct);
            return;
        }

        var text = (msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "").Trim();

        if (text.StartsWith("/start", StringComparison.Ordinal) || text == OpenButtonText)
        {
            await SendWelcomeAsync(chatId, ct);
            return;
        }

        if (text.StartsWith("/help", StringComparison.Ordinal))
        {
            await bot.SendMessageAsync(chatId,
                "ℹ️ Bu bot — markazimizdagi bo'sh ish o'rinlari uchun.\n\n"
                + "• Ilovada «Biz haqimizda», «Vakansiyalar» va «Arizalarim» bo'limlari bor;\n"
                + "• vakansiyani tanlab ariza yuborasiz (F.I.Sh., tajriba, motivatsion xat va CV);\n"
                + "• arizangiz qaysi bosqichda ekanini «Arizalarim»dan kuzatib borasiz.\n\n"
                + $"Ilovani ochish uchun «{OpenButtonText}» tugmasini bosing.",
                MainKeyboard(), ct);
            return;
        }

        // Boshqa har qanday matn — yo'naltiruvchi javob (bot suhbat yuritmaydi).
        await bot.SendMessageAsync(chatId,
            "🙂 Barcha amallar ilova ichida bajariladi.\n\n"
            + $"«{OpenButtonText}» tugmasini bosing — vakansiyalar, ariza yuborish va arizangiz "
            + "bosqichi shu yerda.",
            MainKeyboard(), ct);
    }

    /// <summary>Xush kelibsiz xabari — Mini App'ni ochadigan INLINE tugma bilan.</summary>
    private async Task SendWelcomeAsync(long chatId, CancellationToken ct)
    {
        var url = MiniAppUrl;
        if (url.Length == 0)
        {
            await bot.SendMessageAsync(chatId,
                "⚙️ Ilova manzili hali sozlanmagan. Administratorga murojaat qiling.", null, ct);
            logger.LogWarning("Karyera boti: Mini App manzili yo'q (Career:MiniAppUrl / App:Host bo'sh).");
            return;
        }

        var text =
            "👋 <b>Assalomu alaykum!</b>\n\n"
            + "Bu — markazimizning <b>ishga qabul</b> boti. Bu yerda:\n"
            + "• 🏢 biz haqimizda ma'lumot;\n"
            + "• 💼 faol vakansiyalar;\n"
            + "• 📄 arizangiz qaysi bosqichda ekani.\n\n"
            + "Boshlash uchun quyidagi tugmani bosing 👇";

        var inline = new
        {
            inline_keyboard = new[]
            {
                new[] { new { text = "🚀 Ilovani ochish", web_app = new { url } } },
            },
        };
        await bot.SendMessageAsync(chatId, text, inline, ct, "HTML");
        // Doimiy klaviatura — ilovani keyin ham bir bosishda ochish uchun.
        await bot.SendMessageAsync(chatId,
            "Pastdagi tugmalardan ham foydalanishingiz mumkin 👇", MainKeyboard(), ct);
    }

    /// <summary>Doimiy reply-klaviatura: ilovani ochish (web_app) + telefonni ulashish.</summary>
    private object MainKeyboard()
    {
        var url = MiniAppUrl;
        var rows = new List<object[]>();
        if (url.Length > 0)
            rows.Add([new { text = OpenButtonText, web_app = new { url } }]);
        rows.Add([new { text = PhoneButtonText, request_contact = true }]);
        return new { keyboard = rows.ToArray(), resize_keyboard = true, is_persistent = true };
    }

    /// <summary>Botga murojaat qilgan foydalanuvchini yozib/yangilab boradi (telefon berilsa — saqlaydi).</summary>
    private async Task TouchUserAsync(long chatId, JsonElement msg, string? phone, CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var user = await db.CareerBotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
            var from = msg.TryGetProperty("from", out var f) ? f : default;

            string Get(string prop) =>
                from.ValueKind == JsonValueKind.Object && from.TryGetProperty(prop, out var v)
                    ? v.GetString() ?? "" : "";

            if (user is null)
            {
                user = new CareerBotUser { ChatId = chatId, CreatedAt = AppClock.Iso() };
                db.CareerBotUsers.Add(user);
            }
            user.Username = Get("username");
            user.FirstName = Get("first_name");
            user.LastName = Get("last_name");
            if (!string.IsNullOrWhiteSpace(phone)) user.Phone = phone;
            user.LastSeenAt = AppClock.Iso();
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Karyera boti: foydalanuvchi yozilmadi (chat {Id})", chatId);
        }
    }
}
