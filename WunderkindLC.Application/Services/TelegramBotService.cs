using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

// Test loyihasi xavfsizlik seam'lariga (IsOwnContact) kira olishi uchun.
[assembly: InternalsVisibleTo("WunderkindLC.Tests")]

namespace WunderkindLC.Application.Services;

/// <summary>
/// Telegram botni long polling (getUpdates) orqali yurituvchi fon xizmati.
/// Oqim: foydalanuvchi /start bosadi → MAJBURIY kanal obunasi darhol tekshiriladi (sozlangan
/// bo'lsa — bo'lmasa shu yerda to'xtaydi, "✅ Tekshirish" tugmasi bilan) → telefon raqamini
/// ulashadi → raqam o'quvchi (ParentPhone) yoki o'qituvchi (Phone) bilan solishtiriladi
/// (ro'yxatdan o'tgan); obuna YANA tekshiriladi (ehtiyot chorasi — /start bilan kontakt orasida
/// kanaldan chiqib ketgan bo'lishi mumkin) → keyin TelegramRegistration yoziladi va mos ILOVA
/// (APK) fayli yuboriladi. Token sozlanmagan bo'lsa xizmat kutadi (ilova baribir ishlaydi).
/// Support rejimi: /support → Mode="support" → keyingi matnlar adminga ketadi.
/// </summary>
public class TelegramBotService(
    IServiceProvider sp, TelegramService telegram, IHostEnvironment env,
    OnlineTestBotService onlineTest, BookShopBotService bookShop,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    private const string ApkMime = "application/vnd.android.package-archive";
    private const string ApkCaption =
        "📲 Ilovani o'rnatish: faylni yuklab oling, ochib o'rnating (noma'lum manbalardan o'rnatishga ruxsat bering).";
    /// <summary>Veb (brauzer) versiyasi — ilova fayli bo'lmasa yoki kompyuterdan kirish uchun.</summary>
    private const string WebAppUrl = "https://lc.wunderkindedu.uz/";
    /// <summary>Telefon klaviaturasidagi "adminga murojaat" tugmasi matni (reply keyboard).</summary>
    private const string SupportButtonText = "✍️ Adminga murojaat";
    /// <summary>Ro'yxatdan o'tgan foydalanuvchi klaviaturasidagi "bir martalik kod olish" tugmasi matni.</summary>
    private const string OtpButtonText = "🔑 Yangi kod olish";

    /// <summary>Telefon so'rovchi yagona yo'riqnoma. XAVFSIZLIK: raqam FAQAT «Telefon raqamni yuborish»
    /// tugmasi orqali (Telegram kontakti) qabul qilinadi — matn sifatida yozilgan raqam profilga
    /// BOG'LANMAYDI, aks holda begona (hatto admin) raqamini yozib akkauntni egallash mumkin edi.</summary>
    private const string PhonePrompt =
        "📱 Telefon raqamingizni yuboring:\n\n" +
        "👉 Pastdagi «📱 Telefon raqamni yuborish» tugmasini bosing.\n\n" +
        "⚠️ Raqamni matn sifatida yozish ishlamaydi — xavfsizlik uchun raqam faqat shu tugma orqali " +
        "(Telegram akkauntingizdan) qabul qilinadi.\n" +
        "Raqamingiz markaz ma'lumotlari bilan solishtirilib, profilingizga bog'lanadi.\n" +
        "Administratorga murojaat uchun «" + SupportButtonText + "» tugmasini bosing.";

    /// <summary>Foydalanuvchi raqamni matn qilib yozganda yoki BEGONA kontakt yuborganda javob.</summary>
    private const string OwnContactOnlyPrompt =
        "❗️ Raqam qabul qilinmadi.\n\n" +
        "Iltimos, O'ZINGIZNING raqamingizni pastdagi «📱 Telefon raqamni yuborish» tugmasi orqali yuboring.\n" +
        "Qo'lda yozilgan yoki boshqa odamning kontakti qabul qilinmaydi.";

    /// <summary>Obunadan oldin kontakt yuborgan, lekin hali obuna bo'lmaganlar: chatId → telefon.</summary>
    private readonly ConcurrentDictionary<long, string> _pendingPhone = new();

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
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram getUpdates xatosi");
                if (!await DelayAsync(3000, stoppingToken)) break;
            }
        }
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
    /// /start'dan boshlab BUTUN suhbat (savol ham, avtomatik javob ham) ko'rinadi.</summary>
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
                ChatId = chatId, FromUser = false, Text = text, AdminName = "Bot", CreatedAt = AppClock.Iso(),
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

        // Kontakt ulashildi. XAVFSIZLIK: faqat O'ZINING kontakti qabul qilinadi — Telegramda manzillar
        // kitobidan BEGONA odamning kontaktini ham yuborish mumkin, u holda o'sha odamning profili
        // (login/parol, kirish kodi) hujumchining chatiga bog'lanib qolardi.
        if (msg.TryGetProperty("contact", out var contact))
        {
            var phone = contact.TryGetProperty("phone_number", out var p) ? p.GetString() : null;
            if (!IsOwnContact(msg))
            {
                await LogInAsync(chatId, "[begona kontakt yuborildi — rad etildi]", ct);
                logger.LogWarning("Bot: begona kontakt rad etildi, chatId {Id}", chatId);
                await SendAsync(chatId, OwnContactOnlyPrompt, ContactKeyboard, ct);
                return;
            }
            await LogInAsync(chatId, $"[telefon raqami ulashildi: {phone}]", ct);
            await HandleContactAsync(chatId, phone, SenderName(msg), ct);
            return;
        }

        // Rasm/hujjat kelgani — KITOB buyurtmasidagi to'lov cheki kutilyapti bo'lsa qabul qilamiz.
        if (msg.TryGetProperty("photo", out var photo) || msg.TryGetProperty("document", out _))
        {
            if (await HandleFileMessageAsync(chatId, msg, ct)) return;
        }

        var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

        if (text.StartsWith("/start"))
        {
            await LogInAsync(chatId, text, ct);
            if (!await CheckSubscriptionForStartAsync(chatId, ct)) return;
            await SendStartWelcomeAsync(chatId, msg, ct);
        }
        else if (text == "/support" || text == SupportButtonText)
        {
            await LogInAsync(chatId, text, ct);
            // ATAYIN obuna talab qilinmaydi: adminga murojaat — yagona "zaxira yo'l". Kanalni topa
            // olmagan yoki muammosi bor foydalanuvchi hech bo'lmasa yozib so'ray olishi kerak.
            await HandleSupportCommandAsync(chatId, ct);
        }
        else if (text == "/kod" || text == OtpButtonText)
        {
            await LogInAsync(chatId, text, ct);
            // MAJBURIY OBUNA: kirish kodi faqat kanalga obuna bo'lganlarga.
            if (!await RequireSubscriptionAsync(chatId, OtpButtonText, ct)) return;
            await HandleOtpRequestAsync(chatId, ct);
        }
        else if (text == "/test" || text == OnlineTestBotService.TestButtonText)
        {
            await LogInAsync(chatId, text, ct);
            // MAJBURIY OBUNA: onlayn testni ishlash ham obunani talab qiladi.
            if (!await RequireSubscriptionAsync(chatId, OnlineTestBotService.TestButtonText, ct)) return;
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await onlineTest.ShowListAsync(db, chatId, ct);
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

            // ONLAYN TEST — TEST KODI kutilyapti (markazda o'qimaydigan odam ham testni ishlay oladi).
            // Bu tekshiruv sessiya tekshiruvidan OLDIN: kod bosqichida hali sessiya yo'q.
            if (botUser?.Mode == OnlineTestBotService.ModeAwaitingCode)
            {
                await LogInAsync(chatId, "[test kodi yuborildi]", ct);
                await onlineTest.HandleCodeAsync(db, chatId, text, ct);
                return;
            }

            // ONLAYN TEST: matn rejimida javoblar kutilyapti (faol sessiya bor) — avval shuni sinaymiz.
            // Javobga o'xshamasa (ParseAnswers bo'sh qaytarsa) — pastdagi oddiy oqim davom etadi.
            if (botUser?.Mode != "support" && await onlineTest.HasSessionAsync(db, chatId, ct)
                && await onlineTest.HandleTextAsync(db, chatId, text, ct))
            {
                await LogInAsync(chatId, "[test javoblari yuborildi]", ct);
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
                // XAVFSIZLIK: matn sifatida yozilgan raqam profilga BOG'LANMAYDI (istalgan odamning
                // raqamini yozib akkauntni egallash mumkin edi). Raqamga o'xshash matnga — aniq
                // tushuntirish, boshqa matnga — umumiy yo'riqnoma. Har holda jim qolmaymiz.
                await SendAsync(chatId, LooksLikePhone(text) ? OwnContactOnlyPrompt : PhonePrompt,
                    ContactKeyboard, ct);
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
            if (chatType is not ("group" or "supergroup")) return; // faqat guruhlar (shaxsiy/kanal emas)
            var chatId = chatIdEl.GetInt64();
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
        if (cbId.Length > 0) await telegram.AnswerCallbackAsync(cbId, ct: ct);
        await LogInAsync(chatId, $"[tugma bosildi: {data}]", ct);

        if (data == "check_sub")
        {
            if (!_pendingPhone.TryGetValue(chatId, out var phone))
            {
                await SendAsync(chatId,
                    "Telefon raqamingizni qayta yuboring.", ContactKeyboard, ct);
                return;
            }
            await HandleContactAsync(chatId, phone, "", ct);
        }
        else if (data == "check_sub_start")
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
                    RegisteredKeyboard(await BookSalesEnabledAsync(db, ct)), ct);
            }
        }
        else if (OnlineTestBotService.Handles(data))
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            if (data.StartsWith(OnlineTestBotService.CbOpen, StringComparison.Ordinal))
            {
                // MAJBURIY OBUNA: eski xabardagi tugma orqali ham chetlab o'tib bo'lmasin.
                if (!await RequireSubscriptionAsync(chatId, OnlineTestBotService.TestButtonText, ct)) return;
                await onlineTest.OpenTestAsync(db, chatId, data, ct);
            }
            else if (data == OnlineTestBotService.CbCode)
            {
                // MAJBURIY OBUNA: test kodi bilan kirish ham obunani talab qiladi (markazda
                // o'qimaydigan ishtirokchi ham kanalga obuna bo'ladi).
                if (!await RequireSubscriptionAsync(chatId, OnlineTestBotService.TestButtonText, ct)) return;
                await onlineTest.AskCodeAsync(db, chatId, ct);
            }
            else if (data.StartsWith(OnlineTestBotService.CbAnswer, StringComparison.Ordinal))
                await onlineTest.AnswerAsync(db, chatId, data, ct);
            else if (data.StartsWith(OnlineTestBotService.CbGoto, StringComparison.Ordinal))
                await onlineTest.GotoAsync(db, chatId, data, ct);
            else if (data == OnlineTestBotService.CbModeButtons)
                await onlineTest.SetModeAsync(db, chatId, buttons: true, ct);
            else if (data == OnlineTestBotService.CbModeText)
                await onlineTest.SetModeAsync(db, chatId, buttons: false, ct);
            else if (data == OnlineTestBotService.CbFinish)
                await onlineTest.FinishAsync(db, chatId, ct);
            else if (data == OnlineTestBotService.CbConfirm)
                await onlineTest.SubmitAsync(db, chatId, ct);
            else if (data == OnlineTestBotService.CbEdit)
                await onlineTest.EditAsync(db, chatId, ct);
            else if (data == OnlineTestBotService.CbCancel)
                await onlineTest.CancelAsync(db, chatId, ct);
            else if (data == OnlineTestBotService.CbList)
                await onlineTest.ShowListAsync(db, chatId, ct);
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
        else if (data.StartsWith(WorkTaskTelegram.CallbackDone, StringComparison.Ordinal))
        {
            await HandleWorkTaskDoneAsync(chatId, data[WorkTaskTelegram.CallbackDone.Length..], ct);
        }
    }

    /// <summary>
    /// "Topshiriqlar" modulidagi topshiriqni MAS'ULNING O'ZI bot orqali "bajarildi" qiladi:
    /// topshiriq doskaning birinchi YOPILUVCHI ustuniga ko'chadi, tarixga yozuv tushadi va
    /// takroriy bo'lsa keyingi nusxasi tug'iladi (panel bilan AYNAN bir xil —
    /// <see cref="WorkTaskFlow"/>).
    ///
    /// <para>Xavfsizlik: faqat topshiriq MAS'ULI (o'z chatidan) belgilay oladi.</para>
    /// </summary>
    private async Task HandleWorkTaskDoneAsync(long chatId, string taskId, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var t = await db.WorkTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct);
        if (t is null) return;

        var owns = await db.TelegramRegistrations.AnyAsync(
            r => r.ChatId == chatId && r.UserId == t.AssigneeId, ct);
        if (!owns) return;

        var doneCol = await db.WorkTaskColumns
            .Where(c => c.BoardId == t.BoardId && c.IsDone)
            .OrderBy(c => c.Order).FirstOrDefaultAsync(ct);
        if (doneCol is null) return;

        if (t.ColumnId == doneCol.Id)
        {
            await telegram.SendMessageAsync(chatId, $"✅ «{t.Title}» allaqachon bajarilgan.", null, ct);
            return;
        }

        t.ColumnId = doneCol.Id;
        t.CompletedAt = AppClock.Now;
        t.CompletedById = t.AssigneeId;
        t.UpdatedAt = AppClock.Now;
        db.WorkTaskEvents.Add(new WorkTaskEvent
        {
            TaskId = t.Id, ActorId = t.AssigneeId, Kind = "moved",
            Text = "Telegram bot orqali bajarildi deb belgilandi",
        });
        await WorkTaskFlow.SpawnRepeatAsync(db, t, ct);
        await db.SaveChangesAsync(ct);

        await telegram.SendMessageAsync(chatId, $"✅ Bajarildi: «{t.Title}»", null, ct);
    }

    /// <summary>
    /// MAJBURIY OBUNA DARVOZASI — botning HAR bir "foydali" amali (bir martalik kod, onlayn test)
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
        // (shu bilan yangi «Testni ishlash» tugmasi eski foydalanuvchilarda ham paydo bo'ladi).
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            if (await db.TelegramRegistrations.AnyAsync(r => r.ChatId == chatId, ct))
            {
                var books = await BookSalesEnabledAsync(db, ct);
                await SendAsync(chatId,
                    "Assalomu alaykum! 👋\n\nPastdagi tugmalardan foydalaning:\n"
                    + $"• {OtpButtonText} — tizimga kirish uchun bir martalik kod\n"
                    + $"• {OnlineTestBotService.TestButtonText} — sizga ochilgan onlayn testlar\n"
                    + (books ? $"• {BookShopBotService.BooksButtonText} — sotuvdagi kitoblarga buyurtma\n" : "")
                    + $"• {SupportButtonText} — administratorga savol",
                    RegisteredKeyboard(books), ct);
                return;
            }
        }

        await SendAsync(chatId, "Assalomu alaykum! 👋\n\n" + PhonePrompt, ContactKeyboard, ct);
    }

    /// <summary>Telefon kelganda: moslik → MAJBURIY obuna → register + APK.</summary>
    private async Task HandleContactAsync(long chatId, string? phone, string senderName, CancellationToken ct)
    {
        var key = PhoneUtil.Key(phone);
        if (key.Length < 7)
        {
            await SendAsync(chatId, "Telefon raqami noto'g'ri. Qaytadan urinib ko'ring.", ct: ct);
            return;
        }

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        // Qaysi raqam bo'yicha tekshirilsin (Sozlamalar → Telegram bot): "student" — o'quvchining O'ZI
        // raqami (Student.Phone), aks holda (default) ota-ona raqami (Student.ParentPhone).
        var matchStudentOwnPhone = string.Equals(meta?.TelegramPhoneMatchField, "student", StringComparison.OrdinalIgnoreCase);

        var matchedStudents = (await db.Students.ToListAsync(ct))
            .Where(s => PhoneUtil.Key(matchStudentOwnPhone ? s.Phone : s.ParentPhone) == key).ToList();
        var matchedTeachers = (await db.Teachers.Where(t => !t.IsArchived).ToListAsync(ct))
            .Where(t => PhoneUtil.Key(t.Phone) == key).ToList();
        // Admin/xodim — telefon AppUser.Phone bilan moslashsa (yangi lid xabarnomalarini olish uchun).
        var matchedAdmins = (await db.Users
                .Where(u => u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || u.Role == Roles.Staff)
                .ToListAsync(ct))
            .Where(u => PhoneUtil.Key(u.Phone) == key).ToList();

        // Markaz ro'yxatida topilmadi — tizimga KIRISH berilmaydi, lekin raqam saqlanadi va
        // KITOB SOTUVI ochiq qoladi (kitob sotib olish uchun markazda o'qish shart emas).
        if (matchedStudents.Count == 0 && matchedTeachers.Count == 0 && matchedAdmins.Count == 0)
        {
            _pendingPhone.TryRemove(chatId, out _);
            await UpsertBotUserAfterContactAsync(db, chatId, PhoneUtil.DigitsOnly(phone!), new List<string>(), ct);
            var booksOn = meta?.BookSalesEnabled ?? true;
            await SendAsync(chatId,
                $"Bu raqam ({phone}) markaz o'quvchilari ro'yxatida topilmadi — tizimga kirish ma'lumotlari "
                + "berilmaydi. Ma'lumotingiz noto'g'ri bo'lsa, markaz ma'muriyatiga murojaat qiling."
                + $"\n\n📝 <b>Test kodi</b> berilgan bo'lsa — «{OnlineTestBotService.TestButtonText}» tugmasi orqali "
                + "markazda o'qimasangiz ham testni ishlashingiz mumkin."
                + (booksOn ? $"\n📚 Kitob sotib olish ham ochiq — «{BookShopBotService.BooksButtonText}»." : ""),
                GuestKeyboard, ct, "HTML");
            return;
        }

        var channel = meta?.TelegramChannel ?? "";

        // MAJBURIY obuna (ommaviy @kanal sozlangan bo'lsa).
        var channelUser = TelegramService.ChannelUsername(channel);
        if (channelUser is not null)
        {
            var status = await telegram.GetChatMemberStatusAsync(channelUser, chatId, ct);
            if (status is "left" or "kicked")
            {
                _pendingPhone[chatId] = phone!;
                await SendAsync(chatId,
                    "📢 Davom etish uchun avval markaz kanaliga obuna bo'ling, so'ng \"✅ Tekshirish\" tugmasini bosing.",
                    SubscribeKeyboard(ChannelUrl(channel)), ct);
                return;
            }
            // status null (bot kanal admini emas / tekshirib bo'lmadi) → bloklamaymiz (fail-open), ogohlantiramiz.
            if (status is null)
                logger.LogWarning("Telegram obuna tekshirib bo'lmadi (bot kanal admini bo'lishi kerak): {Ch}", channelUser);
        }

        _pendingPhone.TryRemove(chatId, out _);
        await CompleteAsync(db, meta, chatId, phone!, senderName, matchedStudents, matchedTeachers, matchedAdmins, ct);
    }

    /// <summary>Obuna o'tdi: TelegramRegistration yozadi, login/parol yuboradi, BotUser yangilaydi va mos APK(lar)ni yuboradi.
    /// Admin/xodim moslansa — UserId yozuvi (yangi lid xabarnomalari uchun); APK yuborilmaydi.
    /// DIQQAT: ro'yxatdan o'tish (TelegramRegistration — xabar/e'lon olish uchun) HAMMA mos o'quvchida
    /// bajariladi, lekin login/parol va APK faqat AKTIV o'quvchiga yuboriladi
    /// (<see cref="ActiveStudentIdsAsync"/>).</summary>
    private async Task CompleteAsync(
        IAppDbContext db, CenterMeta? meta, long chatId, string phone, string senderName,
        List<Student> students, List<Teacher> teachers, List<AppUser> admins, CancellationToken ct)
    {
        var digits = PhoneUtil.DigitsOnly(phone);
        var linked = new List<string>();

        // O'quvchi(lar)ning HOZIR o'qiyotgan barcha guruhlari (M2M, faol a'zolik — trial/active/frozen)
        // + har guruhning o'qituvchisi F.I.SH. Eski (bitta) Student.ClassName EMAS — u ko'p guruhli
        // o'quvchida yoki guruh almashtirilganda eskirgan bo'lishi mumkin.
        var studentIds = students.Select(s => s.Id).ToList();
        var memberships = studentIds.Count == 0 ? new List<StudentGroup>()
            : await db.StudentGroups.Where(sg => studentIds.Contains(sg.StudentId) && sg.IsActive).ToListAsync(ct);
        var memberGroupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var memberClasses = memberGroupIds.Count == 0 ? new List<Group>()
            : await db.Classes.Where(c => memberGroupIds.Contains(c.Id)).ToListAsync(ct);
        var memberTeacherIds = memberClasses.Select(c => c.TeacherId)
            .Where(tid => !string.IsNullOrEmpty(tid)).Distinct().ToList();
        var memberTeacherNames = memberTeacherIds.Count == 0 ? new Dictionary<string, string>()
            : await db.Teachers.Where(t => memberTeacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var classesByStudent = memberships
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g
                .Select(m => memberClasses.FirstOrDefault(c => c.Id == m.GroupId))
                .Where(c => c is not null).Cast<Group>().ToList());

        foreach (var s in students)
        {
            var exists = await db.TelegramRegistrations.AnyAsync(r => r.StudentId == s.Id && r.ChatId == chatId, ct);
            if (!exists)
                db.TelegramRegistrations.Add(new TelegramRegistration
                {
                    StudentId = s.Id, ChatId = chatId, ParentName = senderName, Phone = digits, CreatedAt = AppClock.Now,
                });

            var studentClasses = classesByStudent.GetValueOrDefault(s.Id, new List<Group>());
            if (studentClasses.Count > 0)
            {
                var groupLabels = studentClasses.Select(c =>
                {
                    var teacherName = !string.IsNullOrEmpty(c.TeacherId) && memberTeacherNames.TryGetValue(c.TeacherId, out var tn)
                        ? tn : "";
                    return teacherName.Length > 0 ? $"{c.Name} (o'qituvchi: {teacherName})" : c.Name;
                });
                linked.Add($"{s.FullName} — {string.Join(", ", groupLabels)}");
            }
            else
            {
                linked.Add($"{s.FullName} ({s.ClassName})");
            }
        }
        foreach (var tch in teachers)
        {
            var exists = await db.TelegramRegistrations.AnyAsync(r => r.TeacherId == tch.Id && r.ChatId == chatId, ct);
            if (!exists)
                db.TelegramRegistrations.Add(new TelegramRegistration
                {
                    TeacherId = tch.Id, StudentId = "", ChatId = chatId, ParentName = senderName,
                    Phone = digits, CreatedAt = AppClock.Now,
                });
            linked.Add($"{tch.FullName} (xodim)");
        }
        foreach (var u in admins)
        {
            var exists = await db.TelegramRegistrations.AnyAsync(r => r.UserId == u.Id && r.ChatId == chatId, ct);
            if (!exists)
                db.TelegramRegistrations.Add(new TelegramRegistration
                {
                    UserId = u.Id, StudentId = "", ChatId = chatId, ParentName = senderName,
                    Phone = digits, CreatedAt = AppClock.Now,
                });
            linked.Add($"{u.FullName} (admin)");
        }
        await db.SaveChangesAsync(ct);

        await SendAsync(chatId,
            "✅ Ro'yxatdan o'tdingiz:\n• " + string.Join("\n• ", linked),
            RegisteredKeyboard(meta?.BookSalesEnabled ?? true), ct);

        // Admin/xodim — yangi lid xabarnomalari haqida ma'lumot.
        if (admins.Count > 0)
            await SendAsync(chatId,
                "🔔 Yangi lid tushganda shu yerga xabar olasiz.", ct: ct);

        // LOGIN/PAROL — FAQAT "aktiv" o'quvchiga (arxivlangan va aktiv bo'lmaganlar chetlab o'tiladi;
        // o'qituvchi/admin uchun bu cheklov yo'q). Matn o'zida veb-versiya havolasini ham o'z ichiga oladi.
        var activeStudentIds = await ActiveStudentIdsAsync(db, students, ct);
        var loginStudents = students.Where(s => activeStudentIds.Contains(s.Id)).ToList();
        var sentLogin = await SendLoginInfoAsync(db, chatId, loginStudents, teachers, admins, ct);

        // Chetlab o'tilganlarga sababini aytamiz — aks holda "hech narsa kelmadi" bo'lib qoladi.
        var skippedNames = students.Where(s => !activeStudentIds.Contains(s.Id)).Select(s => s.FullName).ToList();
        if (skippedNames.Count > 0)
            await SendAsync(chatId,
                "ℹ️ Kirish ma'lumotlari yuborilmadi — hisob hozircha faol emas:\n• "
                + string.Join("\n• ", skippedNames)
                + "\nMarkaz ma'muriyatiga murojaat qiling.", ct: ct);

        // BotUser — telefon va moslik yorlig'ini yangilash/yaratish.
        await UpsertBotUserAfterContactAsync(db, chatId, digits, linked, ct);

        // Mos ILOVA (APK) — FAQAT kirish ma'lumoti berilgan o'quvchi/o'qituvchi uchun (admin web paneldan
        // foydalanadi; faol bo'lmagan o'quvchiga ilova ham yuborilmaydi — login/parolsiz foydasi yo'q).
        // Veb-versiya havolasi FAQAT hech kimga login xabari (u allaqachon havolani o'z ichiga oladi)
        // yuborilmagan bo'lsa qo'shimcha yuboriladi — aks holda bir xil link ikki marta takrorlanib qolardi.
        if (loginStudents.Count > 0 || teachers.Count > 0)
        {
            var sentAny = await SendAppApkAsync(db, meta, chatId, loginStudents.Count > 0, teachers.Count > 0, ct);
            if (!sentAny && !sentLogin)
                await SendAsync(chatId,
                    $"🌐 Tizimga veb-versiya orqali kiring:\n{WebAppUrl}",
                    ct: ct);
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

    /// <summary>Mos AppUser(lar) uchun login va (mavjud bo'lsa) dastlabki parolni yuboradi.
    /// Kamida bitta xabar yuborilsa true (matn o'zida veb-versiya havolasini ham qo'shadi — chaqiruvchi
    /// shu holatda alohida "veb-versiya" xabarini qayta yubormasligi kerak).</summary>
    private async Task<bool> SendLoginInfoAsync(
        IAppDbContext db, long chatId,
        List<Student> students, List<Teacher> teachers, List<AppUser> admins, CancellationToken ct)
    {
        try
        {
            var seen = new HashSet<string>();

            foreach (var s in students)
            {
                if (string.IsNullOrEmpty(s.UserId)) continue;
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == s.UserId, ct);
                if (user is null || !seen.Add(user.Id)) continue;
                await SendAsync(chatId, BuildLoginText(user), null, ct, parseMode: "HTML");
            }

            foreach (var tch in teachers)
            {
                if (string.IsNullOrEmpty(tch.UserId)) continue;
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == tch.UserId, ct);
                if (user is null || !seen.Add(user.Id)) continue;
                await SendAsync(chatId, BuildLoginText(user), null, ct, parseMode: "HTML");
            }

            foreach (var u in admins)
            {
                if (!seen.Add(u.Id)) continue;
                await SendAsync(chatId, BuildLoginText(u), null, ct, parseMode: "HTML");
            }

            return seen.Count > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Login info yuborishda xato: chatId {Id}", chatId);
            return false;
        }
    }

    /// <summary>HTML parse_mode bilan yuboriladi — &lt;code&gt; ichidagi login/parol Telegram mijozida
    /// bosilganda avtomatik nusxa olinadi (tap-to-copy).</summary>
    private static string BuildLoginText(AppUser user)
    {
        var login = WebUtility.HtmlEncode(user.Email);
        if (!string.IsNullOrEmpty(user.InitialPassword))
        {
            var parol = WebUtility.HtmlEncode(user.InitialPassword);
            return $"🔑 Kirish ma'lumotlari:\nLogin: <code>{login}</code>\nParol: <code>{parol}</code>\nTizimga kirish uchun web-versiya\n🌐 {WebAppUrl}";
        }
        return $"🔑 Login: <code>{login}</code>\nParolingizni avval olgansiz.\nTizimga kirish uchun web-versiya\n🌐 {WebAppUrl}\n(Esdan chiqsa — «{SupportButtonText}» tugmasi orqali administratorga murojaat qiling.)";
    }

    /// <summary>Kontakt ulashilgandan keyin BotUser yozuvini yangilaydi yoki yaratadi.</summary>
    private async Task UpsertBotUserAfterContactAsync(
        IAppDbContext db, long chatId, string digits, List<string> linkedLabels, CancellationToken ct)
    {
        try
        {
            var linkedStr = string.Join("; ", linkedLabels);
            var botUser = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
            if (botUser is null)
            {
                db.BotUsers.Add(new BotUser
                {
                    ChatId = chatId,
                    Phone = digits,
                    Linked = linkedStr,
                    StartedAt = AppClock.Iso(),
                    Mode = ""
                });
            }
            else
            {
                botUser.Phone = digits;
                // Moslik topilmasa (mehmon — faqat kitob sotib oluvchi) eski yorliq TOZALANMAYDI.
                if (linkedStr.Length > 0) botUser.Linked = linkedStr;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BotUser upsert xatosi (contact): chatId {Id}", chatId);
        }
    }

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
            await SendAsync(chatId, "Avval ro'yxatdan o'tishingiz kerak — telefon raqamingizni yuboring.", ContactKeyboard, ct);
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
        // ota-onaga har biriga alohida kod (SendLoginInfoAsync bilan bir xil "chatId → user(lar)" mantiq).
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
            var regs = await db.TelegramRegistrations
                .Where(r => r.UserId != null && r.UserId != "").ToListAsync(ct);
            if (regs.Count > 0)
            {
                var userIds = regs.Select(r => r.UserId!).Distinct().ToList();
                var adminUsers = await db.Users
                    .Where(u => userIds.Contains(u.Id) &&
                                (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || u.Role == Roles.Staff))
                    .ToListAsync(ct);
                var adminUserIds = adminUsers.Select(u => u.Id).ToHashSet();

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

    /// <summary>Roli bo'yicha APK(lar)ni yuboradi (keshlangan file_id bo'lsa qayta yuklamasdan).
    /// O'quvchi/o'qituvchi APK'si yo'q bo'lsa ikkinchisiga qaytadi. Kamida bittasi yuborilsa true.</summary>
    private async Task<bool> SendAppApkAsync(
        IAppDbContext db, CenterMeta? meta, long chatId, bool student, bool teacher, CancellationToken ct)
    {
        if (meta is null) return false;

        // Mavjud bo'lsa slot (nom/yo'l/file_id + file_id keshini yozuvchi).
        (string name, string path, string fileId, Action<string> setId)? StudentSlot =
            (meta.StudentApkPath.Length > 0 || meta.StudentApkFileId.Length > 0)
                ? (meta.StudentApkName, meta.StudentApkPath, meta.StudentApkFileId, id => meta.StudentApkFileId = id)
                : null;
        (string name, string path, string fileId, Action<string> setId)? TeacherSlot =
            (meta.TeacherApkPath.Length > 0 || meta.TeacherApkFileId.Length > 0)
                ? (meta.TeacherApkName, meta.TeacherApkPath, meta.TeacherApkFileId, id => meta.TeacherApkFileId = id)
                : null;

        var slots = new List<(string name, string path, string fileId, Action<string> setId)>();
        if (student && (StudentSlot ?? TeacherSlot) is { } s1) slots.Add(s1);
        if (teacher && (TeacherSlot ?? StudentSlot) is { } s2) slots.Add(s2);

        var seen = new HashSet<string>();
        var any = false;
        var changed = false;
        foreach (var slot in slots)
        {
            var dedupeKey = slot.path.Length > 0 ? slot.path : slot.fileId;
            if (!seen.Add(dedupeKey)) continue;

            string? newId;
            if (slot.fileId.Length > 0)
            {
                newId = await telegram.SendDocumentReturningIdAsync(
                    chatId, slot.fileId, null, FileName(slot.name), ApkMime, ApkCaption, ct);
            }
            else
            {
                var abs = Path.Combine(env.ContentRootPath, slot.path);
                if (!File.Exists(abs)) continue;
                var bytes = await File.ReadAllBytesAsync(abs, ct);
                newId = await telegram.SendDocumentReturningIdAsync(
                    chatId, null, bytes, FileName(slot.name), ApkMime, ApkCaption, ct);
            }

            if (newId is not null)
            {
                any = true;
                if (newId.Length > 0 && newId != slot.fileId) { slot.setId(newId); changed = true; }
            }
        }

        if (changed) await db.SaveChangesAsync(ct);
        return any;
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

    private static string FileName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "app.apk"
        : name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ? name : name + ".apk";

    private static string ChannelUrl(string ch)
    {
        ch = (ch ?? "").Trim();
        if (ch.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return ch;
        return "https://t.me/" + ch.TrimStart('@');
    }

    /// <summary>Matn telefon raqamiga o'xshaydimi — foydalanuvchi tugmasiz, raqamni YOZIB yuborgan bo'lsa
    /// uni kontakt ulashgandek qabul qilamiz. Faqat raqam va telefon belgilari (+ - ( ) bo'sh joy) bo'lib,
    /// 7–12 ta raqam bo'lsa "ha" (oddiy matn/murojaatni telefon deb yanglishmaslik uchun).</summary>
    private static bool LooksLikePhone(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        var digits = PhoneUtil.DigitsOnly(t);
        if (digits.Length is < 7 or > 12) return false;
        return t.All(c => char.IsDigit(c) || c is '+' or '-' or '(' or ')' or ' ');
    }

    /// <summary>XAVFSIZLIK SEAM: kelgan <c>contact</c> yuboruvchining O'ZIGA tegishlimi?
    /// Telegram "request_contact" tugmasi orqali yuborilgan kontaktda <c>contact.user_id</c> bo'ladi va u
    /// <c>message.from.id</c> ga TENG bo'ladi. Manzillar kitobidan tanlangan BEGONA kontaktda esa user_id
    /// boshqa bo'ladi (yoki umuman bo'lmaydi — Telegramda ro'yxatdan o'tmagan raqam).
    /// Maydon yo'q bo'lsa yoki turi noto'g'ri bo'lsa — <c>false</c> (istisno tashlamaydi).</summary>
    internal static bool IsOwnContact(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) return false;
        if (!message.TryGetProperty("contact", out var contact) || contact.ValueKind != JsonValueKind.Object) return false;
        if (!message.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Object) return false;
        // DIQQAT: TryGetInt64 raqam bo'lmagan turda ISTISNO tashlaydi — avval ValueKind tekshiriladi.
        if (!contact.TryGetProperty("user_id", out var uidEl) || uidEl.ValueKind != JsonValueKind.Number
            || !uidEl.TryGetInt64(out var userId)) return false;
        if (!from.TryGetProperty("id", out var fromIdEl) || fromIdEl.ValueKind != JsonValueKind.Number
            || !fromIdEl.TryGetInt64(out var fromId)) return false;
        return userId == fromId;
    }

    private static string SenderName(JsonElement msg)
    {
        if (!msg.TryGetProperty("from", out var from)) return "";
        var fn = from.TryGetProperty("first_name", out var f) ? f.GetString() : null;
        var ln = from.TryGetProperty("last_name", out var l) ? l.GetString() : null;
        return string.Join(" ", new[] { fn, ln }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    /// <summary>Telefon so'rovchi klaviatura (request_contact) + "Testni ishlash" + "Adminga murojaat".
    /// <para>«📝 Testni ishlash» ATAYIN bu yerda ham bor: markazda o'qimaydigan odam TEST KODI bilan
    /// testni ishlay olishi kerak — u telefon raqamini ulashishi shart emas.</para></summary>
    private static object ContactKeyboard => new
    {
        keyboard = new object[][]
        {
            new object[] { new { text = "📱 Telefon raqamni yuborish", request_contact = true } },
            new object[] { new { text = OnlineTestBotService.TestButtonText } },
            new object[] { new { text = SupportButtonText } },
        },
        resize_keyboard = true,
        one_time_keyboard = false,
    };

    /// <summary>Ro'yxatdan o'tgan (telefoni tasdiqlangan) foydalanuvchi klaviaturasi: bir martalik
    /// kirish kodi → ONLAYN TESTNI ISHLASH → (yoqilgan bo'lsa) KITOB SOTIB OLISH → adminga murojaat.
    /// Ro'yxatdan o'tish tugagach (va ro'yxatdan o'tgan foydalanuvchi /start bosganda) o'rnatiladi.</summary>
    private static object RegisteredKeyboard(bool books = true)
    {
        var rows = new List<object[]>
        {
            new object[] { new { text = OtpButtonText } },
            new object[] { new { text = OnlineTestBotService.TestButtonText } },
        };
        if (books) rows.Add(new object[] { new { text = BookShopBotService.BooksButtonText } });
        rows.Add(new object[] { new { text = SupportButtonText } });
        return new { keyboard = rows.ToArray(), resize_keyboard = true, one_time_keyboard = false };
    }

    /// <summary>Faqat KITOB sotib olish + adminga murojaat — markaz ro'yxatida topilmagan (o'quvchi/
    /// o'qituvchi emas) mijoz uchun. Kitob sotuvi hamma uchun ochiq: sotib olish uchun markazda
    /// o'qish shart emas, telefon raqamini ulashish kifoya.</summary>
    private static object GuestKeyboard => new
    {
        keyboard = new object[][]
        {
            new object[] { new { text = OnlineTestBotService.TestButtonText } },
            new object[] { new { text = BookShopBotService.BooksButtonText } },
            new object[] { new { text = SupportButtonText } },
        },
        resize_keyboard = true,
        one_time_keyboard = false,
    };

    /// <summary>Kitoblar sotuvi botda yoqilganmi (Sozlamalar → Kitoblar sotuvi).</summary>
    private static async Task<bool> BookSalesEnabledAsync(IAppDbContext db, CancellationToken ct) =>
        (await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct))?.BookSalesEnabled ?? true;

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
