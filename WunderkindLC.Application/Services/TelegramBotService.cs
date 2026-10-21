using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

// Test loyihasi ichki konstantalar/sof funksiyalarga (LegacyTestButtonText, IsLegacyOnlineTestCallback) kira olishi uchun.
[assembly: InternalsVisibleTo("WunderkindLC.Tests")]

namespace WunderkindLC.Application.Services;

/// <summary>
/// Telegram botni long polling (getUpdates) orqali yurituvchi fon xizmati.
/// Oqim (2026-10-12 dan — bitta bot hamma uchun, TELEFON SO'RALMAYDI): /start → MAJBURIY kanal obunasi
/// (sozlangan bo'lsa) → «📱 Panel» tugmasi (Mini App). Foydalanuvchi Panel ichida login/parol bilan kirganda
/// chat uning akkauntiga bog'lanadi (<see cref="Messaging.TelegramMiniAppLink"/>, <c>POST api/telegram/link</c>).
/// 🔒 2026-10-13: ulanmagan chat /start va Panel'dan boshqa hech narsadan foydalana olmaydi (<see cref="Messaging.BotAccess"/>);
/// telefon/kontakt orqali ulanish OLIB TASHLANDI — raqam hech qachon kirish bermaydi.
/// Token sozlanmagan bo'lsa xizmat kutadi (sayt baribir ishlaydi).
/// (2026-10: ilova (APK) fayli yuborish va bot orqali ONLAYN TEST olib tashlandi.)
/// Support rejimi: /support → Mode="support" → keyingi matnlar adminga ketadi.
/// </summary>
public class TelegramBotService(
    IServiceProvider sp, TelegramService telegram,
    BookShopBotService bookShop,
    ILogger<TelegramBotService> logger,
    AutoMessageService? autoMsg = null) : BackgroundService
{
    /// <summary>ESKI «📝 Testni ishlash» tugmasi matni — bot orqali onlayn test 2026-10 da olib tashlandi,
    /// lekin foydalanuvchilarga ilgari yuborilgan klaviaturalarda tugma hali turibdi. Bosilsa — tushuntirish
    /// va yangilangan klaviatura (jim "telefon yuboring" javobiga tushmasin).</summary>
    internal const string LegacyTestButtonText = "📝 Testni ishlash";
    /// <summary>ESKI "test kodi kutilyapti" rejimi (<c>BotUser.Mode</c>) — uchrasa tozalanadi.</summary>
    internal const string LegacyTestCodeMode = "testcode";
    private const string LegacyTestRemovedText =
        "ℹ️ Onlayn testlar endi bot orqali ishlanmaydi. Test natijalari kabinetda («📱 Panel») ko'rinadi.";
    /// <summary>Veb (brauzer) versiyasi — ilova fayli bo'lmasa yoki kompyuterdan kirish uchun.</summary>
    private const string WebAppUrl = "https://lc.wunderkindedu.uz/";
    /// <summary>Telefon klaviaturasidagi "adminga murojaat" tugmasi matni (reply keyboard).</summary>
    private const string SupportButtonText = "✍️ Adminga murojaat";
    /// <summary>Ro'yxatdan o'tgan foydalanuvchi klaviaturasidagi "bir martalik kod olish" tugmasi matni.</summary>
    private const string OtpButtonText = "🔑 Yangi kod olish";
    /// <summary>
    /// O'qituvchi portali va o'quvchi/ota-ona kabineti — bot ICHIDA Telegram Mini App sifatida ochiladi (<c>web_app</c> tugma).
    /// <para>Kirish odatdagidek: birinchi ochilishda login/parol (yoki shu botdagi bir martalik kod),
    /// keyin sessiya Telegram WebView'da saqlanadi. Telegram orqali AVTOMATIK kirish ATAYIN qilinmagan
    /// (yangi kirish yo'li bo'lardi — foydalanuvchi tanlovi, 2026-09-27). SPA Mini App uchun tayyor:
    /// SDK <c>index.html</c> da, <c>ready()/expand()</c> — <c>main.tsx</c>, CSP <c>frame-ancestors</c>
    /// Telegram Web'ga ochiq (Program.cs).</para>
    /// <para>⚠️ BITTA tugma — «📱 Panel» (foydalanuvchi talabi, 2026-10-07): o'qituvchi ham, o'quvchi/ota-ona
    /// ham bitta tugmani bosadi. Manzil — sayt ILDIZI: <c>RootRedirect</c> kirgan foydalanuvchini roliga
    /// qarab o'zi yo'naltiradi (o'qituvchi → <c>/teacher</c>, o'quvchi/ota-ona → <c>/student</c>), kirmagan
    /// bo'lsa login sahifasiga, kirgandan keyin yana o'z portaliga. Rol bo'yicha alohida tugma/manzil QO'SHMANG.</para>
    /// </summary>
    public const string PanelButtonText = "📱 Panel";
    public const string PanelUrl = WebAppUrl;

    /// <summary>Obuna tekshiruvi keshi: chatId → shu vaqtgacha qayta so'ramaymiz. Har tugma bosishda
    /// Telegram API'ga murojaat qilmaslik uchun (obuna holati tez-tez o'zgarmaydi).</summary>
    private readonly ConcurrentDictionary<long, DateTime> _subOkUntil = new();
    private static readonly TimeSpan SubCacheTtl = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long offset = 0;
        var announced = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!telegram.IsConfigured)
            {
                announced = false;
                if (!await DelayAsync(10000, stoppingToken)) break;
                continue;
            }
            if (!announced)
            {
                logger.LogInformation("Telegram bot ishga tushdi (long polling).");
                announced = true;
                // Menyu tugmasi → Panel: shu yo'ldan ochilgan Mini App initData oladi va login bilan chat O'ZI ulanadi
                // (pastki klaviatura tugmasi initData bermaydi) — 2026-10-15, ota-onadan tasdiq so'ralmasin.
                await telegram.SetMenuButtonAsync(PanelUrl, "Panel", stoppingToken);
            }

            try
            {
                var updates = await telegram.GetUpdatesAsync(offset, 30, stoppingToken);
                if (updates is null)
                {
                    if (!await DelayAsync(2000, stoppingToken)) break;
                    continue;
                }
                foreach (var upd in updates.Value.EnumerateArray())
                {
                    if (upd.TryGetProperty("update_id", out var idEl))
                        offset = idEl.GetInt64() + 1;
                    await HandleUpdateAsync(upd, stoppingToken);
                }
            }
            // ⚠️ Cancellation comes in TWO kinds (same fix as CareerBotService/TeacherBotService):
            //  1) the app is stopping (stoppingToken) — really end the loop;
            //  2) an HttpClient timeout — also a TaskCanceledException. An unconditional `break` here
            //     killed the main bot silently on one network hiccup (prod 2026-10-14: updates piled up
            //     unanswered until the container was restarted).
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(ex, "Telegram getUpdates uzildi (timeout) — qayta urinamiz");
                if (!await DelayAsync(3000, stoppingToken)) break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram getUpdates xatosi");
                if (!await DelayAsync(3000, stoppingToken)) break;
            }
        }
    }

    /// <summary>Clears the «blocked» mark of a private chat that talks to the bot again (CRM upgrade P2.4).</summary>
    private async Task UnblockAsync(long chatId, CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await TelegramBlocks.UnblockAsync(db, chatId, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Telegram blokini olib tashlashda xato: chat {Chat}", chatId); }
    }

    private static async Task<bool> DelayAsync(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>Foydalanuvchidan kelgan xabarni (matn/kontakt/tugma) umumiy suhbat tarixiga yozadi —
    /// admin panelidagi "Qo'llab-quvvatlash" bo'limi shu orqali BUTUN yozishmani ko'rsatadi, faqat
    /// support-rejimni emas. AdminUnread OSHIRILMAYDI (buni faqat <see cref="HandleSupportMessageAsync"/>
    /// qiladi) — aks holda har /start bosilganda admin panelida yolg'on "yangi murojaat" ko'rinardi.</summary>
    private async Task LogInAsync(long chatId, string text, CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.BotSupportMessages.Add(new BotSupportMessage
            {
                ChatId = chatId, FromUser = true, Text = text, AdminName = "", CreatedAt = AppClock.Iso(),
            });
            var botUser = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
            if (botUser is not null)
            {
                botUser.LastMessageAt = AppClock.Iso();
                botUser.LastText = text.Length > 140 ? text[..140] : text;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bot xabar logi (kiruvchi) yozilmadi: chatId {Id}", chatId);
        }
    }

    /// <summary>telegram.SendMessageAsync + botning avtomatik javobini umumiy suhbat tarixiga yozadi
    /// (AdminName="Bot" — admin qo'lda yozgan javobdan frontendda ajratish uchun). Barcha ichki
    /// SendMessageAsync chaqiruvlari shu orqali o'tadi — shunda "Qo'llab-quvvatlash" panelida
    /// /start'dan boshlab BUTUN suhbat (savol ham, avtomatik javob ham) ko'rinadi.
    /// <para>⚠️ KIRISH MA'LUMOTLARI tarixga YOZILMAYDI (M4, 2026-10-12): login/parol xabari va bir martalik kod o'rniga
    /// tarixda (<c>Text</c> ham, <c>LastText</c> ham) belgi turadi — <see cref="BotHistoryText.ForHistory"/>.
    /// Foydalanuvchiga xabar AYNAN yuboriladi; faqat saqlanadigan nusxa tozalanadi.</para></summary>
    private async Task<bool> SendAsync(
        long chatId, string text, object? keyboard = null, CancellationToken ct = default, string? parseMode = null)
    {
        var ok = await telegram.SendMessageAsync(chatId, text, keyboard, ct, parseMode);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.BotSupportMessages.Add(new BotSupportMessage
            {
                ChatId = chatId, FromUser = false, Text = BotHistoryText.ForHistory(text), AdminName = "Bot",
                CreatedAt = AppClock.Iso(),
            });
            var botUser = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
            if (botUser is not null)
            {
                botUser.LastMessageAt = AppClock.Iso();
                botUser.LastText = BotHistoryText.Preview(text);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bot xabar logi (chiquvchi) yozilmadi: chatId {Id}", chatId);
        }
        return ok;
    }

    private async Task HandleUpdateAsync(JsonElement upd, CancellationToken ct)
    {
        // Inline tugma bosilgani — obunani qayta tekshiramiz yoki support rejimi.
        if (upd.TryGetProperty("callback_query", out var cq))
        {
            await HandleCallbackAsync(cq, ct);
            return;
        }

        // Botning guruh a'zoligi o'zgargani — lid avtomatik yuboriladigan guruhlar ro'yxatini yangilaymiz.
        if (upd.TryGetProperty("my_chat_member", out var mcm))
        {
            await HandleMyChatMemberAsync(mcm, ct);
            return;
        }

        if (!upd.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
        var chatId = chatIdEl.GetInt64();

        // Faqat SHAXSIY chat. Guruhlarda bot hech narsaga javob bermaydi — u yerda faqat lid kartalari (my_chat_member).
        if (!chat.TryGetProperty("type", out var chatTypeEl) || chatTypeEl.GetString() != "private") return;

        // CRM upgrade P2.4: a private chat that writes to the bot is deliverable again (clears «Bloklagan»).
        await UnblockAsync(chatId, ct);

        // «🔔 Bot xabarlarini yoqish» — pastki klaviaturadan ochilgan Panel `sendData` bilan yuborgan bir martalik kod
        // (bunday ochilishda Telegram initData BERMAYDI — TelegramLinkTokens). Kod LOGGA yozilmaydi.
        if (msg.TryGetProperty("web_app_data", out var webAppData))
        {
            var data = webAppData.TryGetProperty("data", out var d) ? d.GetString() : null;
            await LogInAsync(chatId, "[Panel: botga ulash so'rovi]", ct);
            await HandleWebAppLinkAsync(chatId, data, SenderName(msg), ct);
            return;
        }

        var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

        if (text.StartsWith("/start"))
        {
            await LogInAsync(chatId, text, ct);
            if (!await CheckSubscriptionForStartAsync(chatId, ct)) return;
            await SendStartWelcomeAsync(chatId, msg, ct);
            return;
        }

        // 🔒 2026-10-13 (egasi: "login paroli bo'lmagan odam umuman foydalana olmasligi kerak"): botga ULANMAGAN chat
        // boshqa HECH NARSADAN foydalana olmaydi — kitob, adminga murojaat, kod, kontakt, fayl (BotAccess). Javob tarixga
        // yozilmaydi: begona odamning yozishmasi «Support Telegram»ga tushmasin.
        if (!BotAccess.Allowed(await IsLinkedChatAsync(chatId, ct), BotAccess.Action.Message))
        {
            // Ulanmagan chatdagi «📱 Panel» (endi ODDIY matn tugmasi — StartKeyboard) — inline tugma beriladi: undan
            // ochilgan Panel initData oladi va login bilan chat avtomatik ulanadi. Qolgan hammasi — GateText.
            var reply = text == PanelButtonText ? StartWelcomeText : GateText;
            await telegram.SendMessageAsync(chatId, reply, PanelInline, ct);
            return;
        }

        // Telefon raqami endi KERAK EMAS (kirish faqat login/parol bilan) — yuborilgan kontakt hech narsaga bog'lanmaydi.
        if (msg.TryGetProperty("contact", out _))
        {
            await SendAsync(chatId, "ℹ️ Telefon raqami kerak emas — pastdagi tugmalardan foydalaning.",
                await LinkedKeyboardAsync(ct), ct);
            return;
        }

        // Rasm/hujjat kelgani — KITOB buyurtmasidagi to'lov cheki kutilyapti bo'lsa qabul qilamiz.
        if (msg.TryGetProperty("photo", out var photo) || msg.TryGetProperty("document", out _))
        {
            if (await HandleFileMessageAsync(chatId, msg, ct)) return;
        }

        if (text == "/support" || text == SupportButtonText)
        {
            await LogInAsync(chatId, text, ct);
            // ATAYIN obuna talab qilinmaydi: ulangan foydalanuvchi uchun adminga murojaat — "zaxira yo'l" (kanalni topa
            // olmagan bo'lsa ham yozib so'ray olsin). Ulanmagan chatga u ham yopiq (BotAccess).
            await HandleSupportCommandAsync(chatId, ct);
        }
        else if (text == "/kod" || text == OtpButtonText)
        {
            await LogInAsync(chatId, text, ct);
            // MAJBURIY OBUNA: kirish kodi faqat kanalga obuna bo'lganlarga.
            if (!await RequireSubscriptionAsync(chatId, OtpButtonText, ct)) return;
            await HandleOtpRequestAsync(chatId, ct);
        }
        else if (text == "/test" || text == LegacyTestButtonText)
        {
            await LogInAsync(chatId, text, ct);
            await SendLegacyTestNoticeAsync(chatId, ct);
        }
        else if (text == "/kitoblar" || text == BookShopBotService.BooksButtonText)
        {
            await LogInAsync(chatId, text, ct);
            // MAJBURIY OBUNA: kitob buyurtmasi ham kanalga obunani talab qiladi.
            if (!await RequireSubscriptionAsync(chatId, BookShopBotService.BooksButtonText, ct)) return;
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await bookShop.ShowCatalogAsync(db, chatId, ct);
        }
        else
        {
            // Support rejimida bo'lsa — murojaatni adminga yuborish.
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var botUser = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);

            // ESKI onlayn test "kod kutilyapti" rejimi (bot orqali test 2026-10 da olib tashlandi) —
            // rejim tozalanadi va foydalanuvchiga tushuntiriladi.
            if (botUser?.Mode == LegacyTestCodeMode)
            {
                botUser.Mode = "";
                await db.SaveChangesAsync(ct);
                await LogInAsync(chatId, text, ct);
                await SendLegacyTestNoticeAsync(chatId, ct);
                return;
            }

            // KITOB BUYURTMASI: soni qo'lda yozilishi kutilyapti bo'lsa (yoki chek o'rniga matn
            // kelgan bo'lsa) shu servis qabul qiladi. Aks holda pastdagi oddiy oqim davom etadi.
            if (botUser?.Mode != "support" && await bookShop.HandleTextAsync(db, chatId, text, ct))
            {
                await LogInAsync(chatId, text, ct);
                return;
            }

            if (botUser?.Mode == "support")
            {
                // HandleSupportMessageAsync o'zi to'liq loglaydi (AdminUnread bilan) — LogInAsync shart emas.
                await HandleSupportMessageAsync(db, botUser, chatId, text, ct);
            }
            else
            {
                await LogInAsync(chatId, text, ct);
                // Tanilmagan matn — jim qolmaymiz: «📱 Panel» yo'riqnomasi.
                await SendAsync(chatId, StartHintText, RegisteredKeyboard(await BookSalesEnabledAsync(db, ct), panel: true), ct);
            }
        }
    }

    /// <summary>
    /// Rasm yoki hujjat kelgani. Hozircha faqat KITOB buyurtmasidagi to'lov CHEKI uchun ishlatiladi:
    /// chat chek kutayotgan bo'lsa fayl serverga ko'chiriladi va buyurtma yaratiladi (true qaytadi).
    /// Aks holda false — chaqiruvchi oddiy matn oqimini davom ettiradi.
    /// </summary>
    private async Task<bool> HandleFileMessageAsync(long chatId, JsonElement msg, CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            // Support rejimida bo'lsa — chek emas, adminga murojaat; fayllarni bu yerda ushlamaymiz.
            var botUser = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
            if (botUser?.Mode == "support") return false;
            if (!await bookShop.AwaitingReceiptAsync(db, chatId, ct)) return false;

            string? fileId = null;
            string? fileName = null;
            if (msg.TryGetProperty("photo", out var photos) && photos.GetArrayLength() > 0)
            {
                // Eng katta o'lchamdagi variant (oxirgi element) — chek matni o'qilishi uchun.
                var best = photos[photos.GetArrayLength() - 1];
                if (best.TryGetProperty("file_id", out var pid)) fileId = pid.GetString();
            }
            else if (msg.TryGetProperty("document", out var doc))
            {
                if (doc.TryGetProperty("file_id", out var did)) fileId = did.GetString();
                if (doc.TryGetProperty("file_name", out var dn)) fileName = dn.GetString();
            }
            if (string.IsNullOrEmpty(fileId)) return false;

            await LogInAsync(chatId, "[kitob buyurtmasi uchun to'lov cheki yuborildi]", ct);
            await bookShop.HandleReceiptAsync(db, chatId, fileId, fileName, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kitob cheki (fayl) ishlovida xato: chat {Chat}", chatId);
            return false;
        }
    }

    /// <summary>Botning guruh a'zoligi o'zgargani (<c>my_chat_member</c>): guruhga qo'shilsa lid-xabar
    /// ro'yxatiga yozadi (va bir marta tasdiq yuboradi), chiqarilsa IsActive=false. Shaxsiy chatlar e'tiborsiz.</summary>
    private async Task HandleMyChatMemberAsync(JsonElement mcm, CancellationToken ct)
    {
        try
        {
            if (!mcm.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
            var chatType = chat.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
            var chatId = chatIdEl.GetInt64();
            if (chatType == "private")
            {
                // CRM upgrade P2.4: the user blocked (kicked) / unblocked (member) the bot in a PRIVATE chat.
                var privateStatus = mcm.TryGetProperty("new_chat_member", out var pm) && pm.TryGetProperty("status", out var pst)
                    ? pst.GetString() : null;
                var blocked = TelegramBlocks.BlockedByStatus(privateStatus);
                if (blocked is null) return;
                using var pscope = sp.CreateScope();
                var pdb = pscope.ServiceProvider.GetRequiredService<IAppDbContext>();
                if (blocked == true)
                {
                    if (await TelegramBlocks.MarkBlockedAsync(pdb, chatId, AppClock.Now, ct) > 0) await pdb.SaveChangesAsync(ct);
                }
                else await TelegramBlocks.UnblockAsync(pdb, chatId, ct);
                return;
            }
            if (chatType is not ("group" or "supergroup")) return; // faqat guruhlar (kanal emas)
            var title = chat.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" : "";

            var status = mcm.TryGetProperty("new_chat_member", out var ncm) && ncm.TryGetProperty("status", out var stEl)
                ? stEl.GetString() ?? "" : "";
            var joined = status is "member" or "administrator" or "creator";

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var group = await db.TelegramGroups.FirstOrDefaultAsync(g => g.ChatId == chatId, ct);

            if (joined)
            {
                if (group is null)
                {
                    db.TelegramGroups.Add(new TelegramGroup { ChatId = chatId, Title = title, IsActive = true });
                    await db.SaveChangesAsync(ct);
                    await telegram.SendMessageAsync(chatId,
                        "✅ Bot ulandi. Endi yangi lidlar avtomatik shu guruhga yuboriladi.", null, ct);
                }
                else
                {
                    var wasInactive = !group.IsActive;
                    group.IsActive = true;
                    if (title.Length > 0) group.Title = title;
                    await db.SaveChangesAsync(ct);
                    if (wasInactive)
                        await telegram.SendMessageAsync(chatId,
                            "✅ Bot qayta ulandi. Yangi lidlar shu guruhga yuboriladi.", null, ct);
                }
                // DIQQAT: guruh xabarlari shaxsiy suhbat emas — LogInAsync/SendAsync (BotUser/BotSupportMessage)
                // ATAYLAB ishlatilmaydi, chunki ular ChatId=shaxsiy foydalanuvchi deb hisoblaydi.
            }
            else if (group is not null && group.IsActive) // left / kicked / restricted
            {
                group.IsActive = false;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "my_chat_member ishlovida xato");
        }
    }

    private async Task HandleCallbackAsync(JsonElement cq, CancellationToken ct)
    {
        var cbId = cq.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        if (!cq.TryGetProperty("from", out var from) || !from.TryGetProperty("id", out var fromIdEl)) return;
        var chatId = fromIdEl.GetInt64();
        var data = cq.TryGetProperty("data", out var dEl) ? dEl.GetString() ?? "" : "";
        await UnblockAsync(chatId, ct);

        // «✅ Qabul qildim» (CRM upgrade P2.4): the generic empty answer is SKIPPED — the ack service answers
        // with its own toast. Not logged into the support history (it is not a conversation).
        if (data.StartsWith(MessageAckService.CallbackPrefix, StringComparison.Ordinal))
        {
            try
            {
                using var ackScope = sp.CreateScope();
                var ackDb = ackScope.ServiceProvider.GetRequiredService<IAppDbContext>();
                await MessageAckService.HandleAsync(ackDb, telegram, chatId, cbId,
                    data[MessageAckService.CallbackPrefix.Length..], ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "«Qabul qildim» ishlovida xato: chat {Chat}", chatId); }
            return;
        }

        if (cbId.Length > 0) await telegram.AnswerCallbackAsync(cbId, ct: ct);

        // 🔒 Ulanmagan chat — faqat /start obunasini qayta tekshirish (BotAccess); eski xabarlardagi kitob/support
        // tugmalari ham yopiq. Tarixga yozilmaydi.
        if (!BotAccess.Allowed(await IsLinkedChatAsync(chatId, ct), BotAccess.Action.Callback, data))
        {
            await telegram.SendMessageAsync(chatId, GateText, PanelInline, ct);
            return;
        }
        await LogInAsync(chatId, $"[tugma bosildi: {data}]", ct);

        if (data is "check_sub_start" or "check_sub")
        {
            if (!await CheckSubscriptionForStartAsync(chatId, ct)) return;
            await SendStartWelcomeAsync(chatId, cq, ct);
        }
        else if (data == "support")
        {
            await HandleSupportCommandAsync(chatId, ct);
        }
        else if (data == "check_sub_menu")
        {
            // Menyu darvozasidagi "✅ Tekshirish": obunani qayta tekshiramiz.
            _subOkUntil.TryRemove(chatId, out _);
            if (await RequireSubscriptionAsync(chatId, "Davom etish", ct))
            {
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                await SendAsync(chatId,
                    "✅ Rahmat! Endi pastdagi tugmalardan foydalanishingiz mumkin.",
                    RegisteredKeyboard(await BookSalesEnabledAsync(db, ct), panel: true), ct);
            }
        }
        else if (IsLegacyOnlineTestCallback(data))
        {
            // Eski xabarlardagi onlayn test tugmalari (bot orqali test 2026-10 da olib tashlandi).
            await SendLegacyTestNoticeAsync(chatId, ct);
        }
        else if (BookShopBotService.Handles(data))
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            if (data.StartsWith(BookShopBotService.CbBook, StringComparison.Ordinal))
            {
                // MAJBURIY OBUNA: eski xabardagi tugma orqali ham chetlab o'tib bo'lmasin.
                if (!await RequireSubscriptionAsync(chatId, BookShopBotService.BooksButtonText, ct)) return;
                await bookShop.OpenBookAsync(db, chatId, data, ct);
            }
            else if (data.StartsWith(BookShopBotService.CbQty, StringComparison.Ordinal))
                await bookShop.SetQtyAsync(db, chatId, data, ct);
            else if (data == BookShopBotService.CbQtyOther)
                await bookShop.AskQtyAsync(db, chatId, ct);
            else if (data == BookShopBotService.CbPayCash)
                await bookShop.ChooseCashAsync(db, chatId, ct);
            else if (data == BookShopBotService.CbPayCard)
                await bookShop.ChooseCardAsync(db, chatId, ct);
            else if (data == BookShopBotService.CbConfirm)
                await bookShop.ConfirmCashAsync(db, chatId, ct);
            else if (data == BookShopBotService.CbSendReceipt)
                await bookShop.PromptReceiptAsync(db, chatId, ct);
            else if (data == BookShopBotService.CbCancel)
                await bookShop.CancelAsync(db, chatId, ct);
            else if (data == BookShopBotService.CbList)
                await bookShop.ShowCatalogAsync(db, chatId, ct);
        }
        // (Kanban «Topshiriqlar» tugmalari — `wtdone:` / `wtok:` — 2026-10-21 da modul bilan birga olib tashlandi;
        // eski xabarlardagi tugma bosilsa hech narsa qilinmaydi.)
    }

    /// <summary>
    /// MAJBURIY OBUNA DARVOZASI — botning HAR bir "foydali" amali (bir martalik kod, kitob buyurtmasi)
    /// shu yerdan o'tadi. Ilgari tekshiruv FAQAT /start va telefon yuborishda bo'lgani uchun,
    /// ro'yxatdan o'tib olgan foydalanuvchi kanaldan chiqib ketsa ham kod olishda va boshqa
    /// amallarda hech narsa tekshirilmasdi — shu teshik yopildi.
    /// <para>Obuna bo'lmasa: kanal havolasi + "✅ Tekshirish" tugmasi yuboriladi va <c>false</c>
    /// qaytadi (chaqiruvchi to'xtashi kerak). Tekshirib BO'LMASA (kanal sozlanmagan, xususiy kanal
    /// yoki bot kanalda admin emas) — BLOKLAMAYMIZ (fail-open), lekin log'ga ogohlantirish yoziladi;
    /// admin buni "Sozlamalar → Xabar kanallari → Telegram bot" diagnostikasida ko'radi.</para>
    /// <para>Natija 5 daqiqa keshlanadi — har tugma bosishda Telegram API'ga bormaslik uchun.</para>
    /// </summary>
    /// <param name="action">Bloklangan xabarda ko'rsatiladigan amal nomi (masalan "Yangi kod olish").</param>
    private async Task<bool> RequireSubscriptionAsync(long chatId, string action, CancellationToken ct)
    {
        if (_subOkUntil.TryGetValue(chatId, out var until) && until > DateTime.UtcNow) return true;

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var channel = meta?.TelegramChannel ?? "";

        var channelUser = TelegramService.ChannelUsername(channel);
        if (channelUser is null) return true;   // kanal yo'q / xususiy — tekshirib bo'lmaydi

        var status = await telegram.GetChatMemberStatusAsync(channelUser, chatId, ct);
        if (status is "left" or "kicked")
        {
            _subOkUntil.TryRemove(chatId, out _);
            await SendAsync(chatId,
                $"📢 «{action}» uchun avval markaz kanaliga obuna bo'ling.\n"
                + "Obuna bo'lgach «✅ Tekshirish» tugmasini bosing.",
                SubscribeKeyboard(ChannelUrl(channel), "check_sub_menu"), ct);
            return false;
        }
        if (status is null)
        {
            // Bot kanalga ADMIN qilinmagan yoki kanal nomi noto'g'ri — Telegram javob bermaydi.
            logger.LogWarning(
                "Majburiy obuna TEKSHIRILMADI (bot {Ch} kanalida admin bo'lishi kerak) — chat {Chat} o'tkazib yuborildi.",
                channelUser, chatId);
            return true;
        }

        _subOkUntil[chatId] = DateTime.UtcNow.Add(SubCacheTtl);
        return true;
    }

    /// <summary>
    /// /start (yoki "✅ Tekshirish" tugmasi) bosilganda MAJBURIY kanal obunasini tekshiradi (kanal
    /// sozlangan va ommaviy @username bo'lib tekshirish mumkin bo'lsa). Obuna bo'lmasa kanal havolasi
    /// + "✅ Tekshirish" tugmali xabar yuborib false qaytaradi (chaqiruvchi shu yerda to'xtashi kerak);
    /// kanal sozlanmagan/tekshirib bo'lmaydigan (masalan xususiy kanal) bo'lsa — bloklamaymiz, true.
    /// </summary>
    private async Task<bool> CheckSubscriptionForStartAsync(long chatId, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var channel = meta?.TelegramChannel ?? "";

        var channelUser = TelegramService.ChannelUsername(channel);
        if (channelUser is null) return true;

        var status = await telegram.GetChatMemberStatusAsync(channelUser, chatId, ct);
        if (status is "left" or "kicked")
        {
            await SendAsync(chatId,
                "📢 Botdan foydalanish uchun avval markaz kanaliga obuna bo'ling, so'ng \"✅ Tekshirish\" tugmasini bosing.",
                SubscribeKeyboard(ChannelUrl(channel), "check_sub_start"), ct);
            return false;
        }
        // status null (bot kanal admini emas / tekshirib bo'lmadi) → bloklamaymiz (fail-open), ogohlantiramiz.
        if (status is null)
            logger.LogWarning("Telegram obuna tekshirib bo'lmadi (/start, bot kanal admini bo'lishi kerak): {Ch}", channelUser);
        return true;
    }

    /// <summary>/start (obunadan o'tgach) — BotUser yozadi/yangilaydi va telefon so'rovchi xabarni yuboradi.
    /// <paramref name="fromHolder"/> — "from" maydonli JSON element (message YOKI callback_query, ikkalasida
    /// ham shakli bir xil).</summary>
    private async Task SendStartWelcomeAsync(long chatId, JsonElement fromHolder, CancellationToken ct)
    {
        await UpsertBotUserOnStartAsync(chatId, fromHolder, ct);

        // Allaqachon ro'yxatdan o'tgan bo'lsa — telefonni qayta so'ramasdan MENYUNI ko'rsatamiz
        // (shu bilan yangilangan klaviatura eski foydalanuvchilarda ham paydo bo'ladi).
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            if (await db.TelegramRegistrations.AnyAsync(r => r.ChatId == chatId, ct))
            {
                var books = await BookSalesEnabledAsync(db, ct);
                var isTeacher = await IsTeacherChatAsync(db, chatId, ct);
                var isStudent = await IsStudentChatAsync(db, chatId, ct);
                await SendAsync(chatId,
                    "Assalomu alaykum! 👋\n\nPastdagi tugmalardan foydalaning:\n"
                    + (isTeacher || isStudent ? PanelGreetingLine(isTeacher, isStudent) : $"• {PanelButtonText} — tizimga kirish\n")
                    + $"• {OtpButtonText} — tizimga kirish uchun bir martalik kod\n"
                    + (books ? $"• {BookShopBotService.BooksButtonText} — sotuvdagi kitoblarga buyurtma\n" : "")
                    + $"• {SupportButtonText} — administratorga savol",
                    RegisteredKeyboard(books, panel: true), ct);
                return;
            }
        }

        // 2026-10-12 (owner: one bot for everyone, NO phone sharing): the chat is linked when its owner logs in
        // inside «📱 Panel» (TelegramLinkController / TelegramMiniAppLink), not by a phone number.
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await SendAsync(chatId, "Assalomu alaykum! 👋", StartKeyboard(), ct);
            // Xabar ICHIDAGI (inline) tugma: shu yo'l bilan ochilgan Panel Telegram initData oladi va login'dan keyin
            // chat O'ZI ulanadi. Pastki klaviatura tugmasi initData bermaydi (u yerda Panel'dagi «Bot xabarlarini yoqish»).
            await SendAsync(chatId, StartWelcomeText, PanelInline, ct);
        }
    }

    /// <summary>Berilgan o'quvchilardan bot orqali KIRISH MA'LUMOTI olishga haqli bo'lganlarining id'lari.
    /// Aktiv = arxivlanmagan + admin login'ini cheklamagan + kamida bitta guruh a'zoligi Status=="active"
    /// (admin "O'quvchilar" ro'yxatidagi «Aktiv / Aktiv emas» belgisi bilan bir xil mantiq). Ya'ni
    /// arxivdagi, sinov (trial), muzlatilgan (frozen) yoki umuman guruhsiz o'quvchiga login/parol ham,
    /// bir martalik kod ham yuborilmaydi.</summary>
    private static async Task<HashSet<string>> ActiveStudentIdsAsync(
        IAppDbContext db, IReadOnlyCollection<Student> students, CancellationToken ct)
    {
        var ids = students.Where(s => !s.IsArchived && !s.LoginBlocked).Select(s => s.Id).ToList();
        if (ids.Count == 0) return new HashSet<string>();
        var active = await db.StudentGroups
            .Where(sg => ids.Contains(sg.StudentId) && sg.IsActive && sg.Status == "active")
            .Select(sg => sg.StudentId)
            .Distinct()
            .ToListAsync(ct);
        return active.ToHashSet();
    }

    /// <summary>O'qituvchi va o'quvchi/ota-ona login xabari ostidagi tugma — panel shu yerning o'zida (Mini App)
    /// ochiladi; qaysi portal ekanini rol hal qiladi (<see cref="PanelUrl"/>).</summary>
    public static readonly object PanelInline = new
    {
        inline_keyboard = new object[][]
        {
            new object[] { new { text = "📱 Panelni ochish", web_app = new { url = PanelUrl } } },
        },
    };

    /// <summary>/start salomidagi «📱 Panel» satri — tugma bitta, izoh esa chatning roliga qarab.</summary>
    public static string PanelGreetingLine(bool teacher, bool student) => (teacher, student) switch
    {
        (true, true) => $"• {PanelButtonText} — o'qituvchi paneli yoki o'quvchi kabineti (login bo'yicha)\n",
        (true, false) => $"• {PanelButtonText} — guruhlar, davomat, dars jadvali, maosh\n",
        (false, true) => $"• {PanelButtonText} — balans, dars jadvali, test natijalari, reyting\n",
        _ => "",
    };

    /// <summary>/start kelganda BotUser yozuvini yaratadi yoki mavjudini ism/username bilan yangilaydi.</summary>
    private async Task UpsertBotUserOnStartAsync(long chatId, JsonElement msg, CancellationToken ct)
    {
        try
        {
            var name = SenderName(msg);
            var username = "";
            if (msg.TryGetProperty("from", out var from) && from.TryGetProperty("username", out var un))
                username = un.GetString() ?? "";

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var botUser = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
            if (botUser is null)
            {
                db.BotUsers.Add(new BotUser
                {
                    ChatId = chatId,
                    Name = name,
                    Username = username,
                    StartedAt = AppClock.Iso(),
                    Mode = ""
                });
            }
            else
            {
                if (name.Length > 0) botUser.Name = name;
                if (username.Length > 0) botUser.Username = username;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BotUser upsert xatosi (start): chatId {Id}", chatId);
        }
    }

    /// <summary>Panel'dan kelgan <c>web_app_data</c> (<see cref="TelegramLinkTokens"/>): kod egasining akkaunti SHU chatga
    /// ulanadi — <see cref="TelegramMiniAppLink"/> + <see cref="TelegramLinkFlow"/> (initData yo'li bilan AYNAN bir xil).
    /// Mini App yopiladi, shuning uchun chat har doim javob oladi.</summary>
    private async Task HandleWebAppLinkAsync(long chatId, string? data, string senderName, CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var who = scope.ServiceProvider.GetService<TelegramLinkTokens>()?.Redeem(data);
            if (who is null)
            {
                await SendAsync(chatId,
                    "⏳ Ulanish so'rovi eskirgan. Panelni qayta oching va «🔔 Bot xabarlarini yoqish» tugmasini bosing.", ct: ct);
                return;
            }
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
            var o = await TelegramMiniAppLink.LinkAsync(db, who.Value.UserId, who.Value.Role, chatId, senderName, ct);
            await TelegramLinkFlow.CompleteAsync(db, audit, telegram, autoMsg, logger, o, chatId, replyWhenNothingNew: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Panel orqali botga ulashda xato: chat {Chat}", chatId);
            await SendAsync(chatId, "❗️ Ulab bo'lmadi. Birozdan keyin qayta urinib ko'ring.", ct: ct);
        }
    }

    /// <summary>/kod buyrug'i yoki "🔑 Yangi kod olish" tugmasi: shu chatga bog'langan HAR bir AppUser
    /// uchun bir martalik kirish kodi (8 belgi, 60 soniya, bir martalik) yaratadi va yuboradi — parol
    /// o'rniga login sahifasida ishlatiladi. Cooldown: chat bo'yicha 5 daqiqada bir marta (bitta chatda
    /// bir nechta farzand bo'lsa ham — bittasi so'ralganda barchasiga birga yangi kod chiqadi).</summary>
    private async Task HandleOtpRequestAsync(long chatId, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var regs = await db.TelegramRegistrations.Where(r => r.ChatId == chatId).ToListAsync(ct);
        if (regs.Count == 0)
        {
            await SendAsync(chatId,
                "Bir martalik kod faqat botga ulangan akkauntga beriladi. Avval «" + PanelButtonText
                + "» tugmasi orqali login va parolingiz bilan kiring — shundan keyin bot sizni taniydi.",
                PanelInline, ct);
            return;
        }

        var lastAt = await LoginOtpService.LastRequestAtAsync(db, chatId, ct);
        if (lastAt is not null)
        {
            var wait = LoginOtpService.RequestCooldown - (AppClock.Now - lastAt.Value);
            if (wait > TimeSpan.Zero)
            {
                var mins = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
                await SendAsync(chatId, $"⏳ Yangi kodni {mins} daqiqadan so'ng so'rashingiz mumkin.", ct: ct);
                return;
            }
        }

        // Shu chatga bog'langan har bir AppUser (o'quvchi/o'qituvchi/xodim) — bir nechta farzandi bo'lgan
        // ota-onaga har biriga alohida kod ("chatId → user(lar)" mantiq).
        // O'quvchilar bir marta o'qiladi (N+1 emas) va login/parol bilan BIR XIL siyosat qo'llanadi:
        // faqat AKTIV o'quvchi kod oladi (arxiv/sinov/muzlatilgan/login cheklangan — olmaydi).
        var regStudentIds = regs.Where(r => !string.IsNullOrEmpty(r.StudentId))
            .Select(r => r.StudentId).Distinct().ToList();
        var regStudents = regStudentIds.Count == 0 ? new List<Student>()
            : await db.Students.Where(s => regStudentIds.Contains(s.Id)).ToListAsync(ct);
        var activeStudentIds = await ActiveStudentIdsAsync(db, regStudents, ct);

        var users = new List<(string UserId, string Label)>();
        foreach (var r in regs)
        {
            if (!string.IsNullOrEmpty(r.UserId))
            {
                var u = await db.Users.FirstOrDefaultAsync(x => x.Id == r.UserId, ct);
                if (u is not null) users.Add((u.Id, u.FullName));
            }
            else if (!string.IsNullOrEmpty(r.TeacherId))
            {
                var tch = await db.Teachers.FirstOrDefaultAsync(x => x.Id == r.TeacherId, ct);
                if (tch is not null && !string.IsNullOrEmpty(tch.UserId)) users.Add((tch.UserId, tch.FullName));
            }
            else if (!string.IsNullOrEmpty(r.StudentId))
            {
                var st = regStudents.FirstOrDefault(x => x.Id == r.StudentId);
                if (st is not null && !string.IsNullOrEmpty(st.UserId) && activeStudentIds.Contains(st.Id))
                    users.Add((st.UserId, st.FullName));
            }
        }

        var distinct = users.GroupBy(x => x.UserId).Select(g => g.First()).ToList();
        if (distinct.Count == 0)
        {
            await SendAsync(chatId,
                "Bog'langan faol tizim hisobi topilmadi — hisobingiz hali faollashtirilmagan bo'lishi mumkin. "
                + "Markaz ma'muriyatiga murojaat qiling.", ct: ct);
            return;
        }

        foreach (var (userId, label) in distinct)
        {
            var code = await LoginOtpService.IssueAsync(db, userId, chatId, ct);
            await SendAsync(chatId,
                $"🔑 {WebUtility.HtmlEncode(label)} uchun bir martalik kirish kodi:\n<code>{code}</code>\n" +
                "⏱ 1 daqiqa amal qiladi, faqat bir marta ishlatiladi.\n" +
                "Login sahifasida «Kod bilan kirish»ni tanlang va shu kodni kiriting.",
                null, ct, parseMode: "HTML");
        }
    }

    /// <summary>/support buyrug'i yoki "support" callback: BotUser.Mode="support" qilib xabar yuboradi.</summary>
    private async Task HandleSupportCommandAsync(long chatId, CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var botUser = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
            if (botUser is null)
            {
                db.BotUsers.Add(new BotUser
                {
                    ChatId = chatId,
                    StartedAt = AppClock.Iso(),
                    Mode = "support"
                });
            }
            else
            {
                botUser.Mode = "support";
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BotUser support mode xatosi: chatId {Id}", chatId);
        }
        await SendAsync(chatId,
            "✍️ Savol yoki murojaatingizni yozib yuboring — administrator javob beradi.", null, ct);
    }

    /// <summary>Support rejimidagi matn xabarni saqlaydi, foydalanuvchiga tasdiqlaydi va adminga yuboradi.</summary>
    private async Task HandleSupportMessageAsync(
        IAppDbContext db, BotUser botUser, long chatId, string text, CancellationToken ct)
    {
        try
        {
            db.BotSupportMessages.Add(new BotSupportMessage
            {
                ChatId = chatId,
                FromUser = true,
                Text = text,
                AdminName = "",
                CreatedAt = AppClock.Iso()
            });
            botUser.LastMessageAt = AppClock.Iso();
            botUser.LastText = text.Length > 140 ? text[..140] : text;
            botUser.AdminUnread = botUser.AdminUnread + 1;
            await db.SaveChangesAsync(ct);

            await SendAsync(chatId,
                "✅ Murojaatingiz qabul qilindi. Administrator tez orada javob beradi.", null, ct);

            // Adminlarga Telegram xabarnoma.
            // ⚠️ FAQAT admin/superadmin va "messages.support" ruxsati bor xodimga — xabarda ota-onaning
            // ismi, TELEFONI va murojaat matni bor. Ilgari botga ulangan HAR bir xodim (kassir,
            // turniket operatori ham) olardi — panelda shu ruxsatsiz u "Support Telegram"ni ocha
            // olmasa ham. Qoida panel darvozasi bilan bir xil (`PermissionRules.HasSection`).
            var regs = await db.TelegramRegistrations
                .Where(r => r.UserId != null && r.UserId != "").ToListAsync(ct);
            if (regs.Count > 0)
            {
                var userIds = regs.Select(r => r.UserId!).Distinct().ToList();
                var adminUsers = await db.Users
                    .Where(u => userIds.Contains(u.Id) &&
                                (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || u.Role == Roles.Staff))
                    .ToListAsync(ct);
                var adminUserIds = adminUsers
                    .Where(u => SupportService.ReceivesBotSupport(u.Role, u.Permissions))
                    .Select(u => u.Id).ToHashSet();

                var nameTag = botUser.Name.Length > 0 ? botUser.Name : "Noma'lum";
                var phoneTag = botUser.Phone.Length > 0 ? $" ({botUser.Phone})" : "";
                var notifyText = $"🆘 Yangi murojaat — {nameTag}{phoneTag}:\n{text}";

                var sentChats = new HashSet<long>();
                foreach (var r in regs)
                {
                    if (r.UserId is null || !adminUserIds.Contains(r.UserId)) continue;
                    if (!sentChats.Add(r.ChatId)) continue;
                    await telegram.SendMessageAsync(r.ChatId, notifyText, null, ct);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Support message xatosi: chatId {Id}", chatId);
        }
    }

    /// <summary>Backup faylini Telegram chat'ga yuboradi (SendDocument).
    /// <paramref name="chatId"/> — admin chat ID (CenterMeta.TelegramAdminChatId).
    /// <paramref name="fileData"/> — backup fayl baytlari.
    /// <paramref name="fileName"/> — fayl nomi (masalan "backup-2026-06-22.sql.gz").
    /// Xatolik bo'lsa log'ga yozadi, exception otmaydi.
    /// Muvaffaqiyatli yuborilsa true qaytaradi.</summary>
    public async Task<bool> SendBackupFileAsync(long chatId, byte[] fileData, string fileName)
    {
        if (!telegram.IsConfigured)
        {
            logger.LogWarning("Telegram backup yuborib bo'lmadi — bot sozlanmagan.");
            return false;
        }
        if (chatId == 0 || fileData.Length == 0)
        {
            logger.LogWarning("Telegram backup: chatId ({Id}) yoki fayl bo'sh.", chatId);
            return false;
        }
        try
        {
            var caption = $"Backup: {fileName}\nVaqt: {AppClock.Now:yyyy-MM-dd HH:mm} (UTC)";
            var result = await telegram.SendDocumentReturningIdAsync(
                chatId, null, fileData, fileName, "application/gzip", caption, CancellationToken.None);
            if (result is not null)
            {
                logger.LogInformation("Telegram backup muvaffaqiyatli yuborildi: {File} → chatId {Id}", fileName, chatId);
                return true;
            }
            logger.LogWarning("Telegram backup yuborilmadi (null file_id): chatId {Id}", chatId);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram backup yuborishda xato: chatId {Id}, file {File}", chatId, fileName);
            return false;
        }
    }

    private static string ChannelUrl(string ch)
    {
        ch = (ch ?? "").Trim();
        if (ch.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return ch;
        return "https://t.me/" + ch.TrimStart('@');
    }

    /// <summary>Telegram foydalanuvchisining ismi (from.first_name + last_name) — ulanish yozuvidagi <c>ParentName</c>.</summary>
    private static string SenderName(JsonElement msg)
    {
        if (!msg.TryGetProperty("from", out var from)) return "";
        var fn = from.TryGetProperty("first_name", out var f) ? f.GetString() : null;
        var ln = from.TryGetProperty("last_name", out var l) ? l.GetString() : null;
        return string.Join(" ", new[] { fn, ln }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    /// <summary>Botga ULANGAN foydalanuvchi klaviaturasi: bir martalik
    /// kirish kodi → (yoqilgan bo'lsa) KITOB SOTIB OLISH → adminga murojaat.
    /// Ro'yxatdan o'tish tugagach (va ro'yxatdan o'tgan foydalanuvchi /start bosganda) o'rnatiladi.</summary>
    public static object RegisteredKeyboard(bool books = true, bool panel = false)
    {
        var rows = new List<object[]>();
        // O'qituvchi yoki o'quvchi/ota-onaga — BITTA «📱 Panel» tugmasi ENG TEPADA; portalni rol tanlaydi.
        if (panel) rows.Add(new object[] { new { text = PanelButtonText, web_app = new { url = PanelUrl } } });
        rows.Add(new object[] { new { text = OtpButtonText } });
        if (books) rows.Add(new object[] { new { text = BookShopBotService.BooksButtonText } });
        rows.Add(new object[] { new { text = SupportButtonText } });
        return new { keyboard = rows.ToArray(), resize_keyboard = true, one_time_keyboard = false };
    }

    /// <summary>Botga hali ULANMAGAN chat klaviaturasi (2026-10-13, egasining qarori): FAQAT «📱 Panel» — kitob ham,
    /// adminga murojaat ham YO'Q (login/parolsiz odam botdan foydalanmaydi — <see cref="BotAccess"/>). Kirgach chat
    /// akkauntga bog'lanadi (<see cref="TelegramMiniAppLink"/>) va <see cref="RegisteredKeyboard"/> chiqadi.
    /// <para>⚠️ 2026-10-15: tugma ODDIY MATN (web_app EMAS). Klaviaturadagi web_app tugmasidan ochilgan Panel initData
    /// olmasdi — chat ulanmas va ota-onadan «🔔 Bot xabarlarini yoqish» bosish talab qilinardi. Endi bosilsa bot inline
    /// «📱 Panelni ochish» beradi (initData bilan) — login bilan chat AVTOMATIK ulanadi.</para></summary>
    public static object StartKeyboard() => new
    {
        keyboard = new[] { new object[] { new { text = PanelButtonText } } },
        resize_keyboard = true,
        is_persistent = true,
        one_time_keyboard = false,
    };

    /// <summary>Ulanmagan chat /start'dan boshqa narsa yozsa yoki tugma bossa — javob (+ <see cref="PanelInline"/>).</summary>
    public const string GateText =
        "🔒 Bu bot faqat markaz o'quvchilari, ota-onalari va xodimlari uchun.\n\n"
        + "Foydalanish uchun quyidagi «📱 Panelni ochish» tugmasini bosing va markaz bergan login/parol bilan kiring — "
        + "shundan keyin bot sizni taniydi.\n"
        + "Login/parolingiz bo'lmasa, markaz ma'muriyatiga murojaat qiling.";

    /// <summary>Chat biror akkauntga ulanganmi (<see cref="BotAccess"/>).</summary>
    private async Task<bool> IsLinkedChatAsync(long chatId, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.TelegramRegistrations.AnyAsync(r => r.ChatId == chatId, ct);
    }

    private async Task<object> LinkedKeyboardAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return RegisteredKeyboard(await BookSalesEnabledAsync(db, ct), panel: true);
    }

    /// <summary>Ulanmagan chatga /start javobi.</summary>
    public const string StartWelcomeText =
        "Quyidagi «📱 Panelni ochish» tugmasini bosing va markaz bergan login/parolingiz bilan kiring — o'quvchi va ota-ona "
        + "kabineti yoki o'qituvchi paneli shu yerda ochiladi.\n\n"
        + "🔔 Panelga bir marta kirganingizdan keyin markaz xabarlari (davomat, to'lov, e'lonlar) shu chatga keladi.";

    /// <summary>Tanilmagan matnga javob.</summary>
    public const string StartHintText = "Panelni ochish uchun pastdagi «" + PanelButtonText + "» tugmasini bosing 👇";

    /// <summary>Shu chat FAOL (arxivlanmagan) o'qituvchiga bog'langanmi — «📱 Panel» tugmasi shunda
    /// (yoki o'quvchiga bog'langan bo'lsa) ko'rsatiladi. Arxivlangan o'qituvchining eski bog'lanishi tugmani qaytarmaydi.</summary>
    private static async Task<bool> IsTeacherChatAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var ids = await db.TelegramRegistrations
            .Where(r => r.ChatId == chatId && r.TeacherId != null && r.TeacherId != "")
            .Select(r => r.TeacherId!)
            .ToListAsync(ct);
        return ids.Count > 0 && await db.Teachers.AnyAsync(t => ids.Contains(t.Id) && !t.IsArchived, ct);
    }

    /// <summary>Shu chat ARXIVLANMAGAN o'quvchiga (o'zi yoki ota-onasi telefoni bilan) bog'langanmi —
    /// «📱 Panel» tugmasi shunda ham ko'rsatiladi.</summary>
    private static async Task<bool> IsStudentChatAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var ids = await db.TelegramRegistrations
            .Where(r => r.ChatId == chatId && r.StudentId != null && r.StudentId != "")
            .Select(r => r.StudentId)
            .ToListAsync(ct);
        return ids.Count > 0 && await db.Students.AnyAsync(s => ids.Contains(s.Id) && !s.IsArchived, ct);
    }

    /// <summary>Kitoblar sotuvi botda yoqilganmi (Sozlamalar → Kitoblar sotuvi).</summary>
    private static async Task<bool> BookSalesEnabledAsync(IAppDbContext db, CancellationToken ct) =>
        (await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct))?.BookSalesEnabled ?? true;

    /// <summary>ESKI onlayn test inline tugmalarining callback qiymatlari (2026-10 da olib tashlangan oqim):
    /// eski xabarlarda tugma qolgan — bosilsa tushuntirish yuboriladi (sof funksiya).</summary>
    internal static bool IsLegacyOnlineTestCallback(string data) =>
        data.StartsWith("ot:", StringComparison.Ordinal) || data.StartsWith("oa:", StringComparison.Ordinal)
        || data.StartsWith("og:", StringComparison.Ordinal)
        || data is "ocode" or "omb" or "omt" or "ofin" or "ocon" or "oedit" or "ocan" or "olist";

    /// <summary>Eski onlayn test tugmasi/buyrug'i bosilganda — tushuntirish + YANGILANGAN klaviatura
    /// (ro'yxatdan o'tgan bo'lsa menyu, aks holda telefon so'rovi).</summary>
    private async Task SendLegacyTestNoticeAsync(long chatId, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var registered = await db.TelegramRegistrations.AnyAsync(r => r.ChatId == chatId, ct);
        var keyboard = registered
            ? RegisteredKeyboard(await BookSalesEnabledAsync(db, ct), panel: true)
            : StartKeyboard();
        await SendAsync(chatId, LegacyTestRemovedText, keyboard, ct);
    }

    /// <summary>Kanal havolasi + "✅ Tekshirish" inline tugmalari. <paramref name="callbackData"/> —
    /// tugma bosilganda qaysi oqim davom etishini bildiradi ("check_sub" — kontakt oqimi,
    /// "check_sub_start" — /start oqimi).</summary>
    private static object SubscribeKeyboard(string channelUrl, string callbackData = "check_sub") => new
    {
        inline_keyboard = new object[][]
        {
            new object[] { new { text = "📢 Kanalga obuna bo'lish", url = channelUrl } },
            new object[] { new { text = "✅ Tekshirish", callback_data = callbackData } },
        },
    };
}
