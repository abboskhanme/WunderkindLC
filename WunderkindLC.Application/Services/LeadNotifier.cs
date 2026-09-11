using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Lid haqidagi Telegram xabari — <b>KARTA</b>.
///
/// <para>Oluvchilar: SHAXSIY xabar FAQAT SUPERADMIN(lar)ga (<see cref="ShouldNotify"/>) + bot
/// qo'shilgan faol GURUH(lar)ga. Bot sozlanmagan / oluvchi yo'q bo'lsa — jim o'tadi.</para>
///
/// <para><b>NEGA KARTA:</b> ilgari lidning har o'zgarishi (bosqich, izoh, sinov darsi, konversiya)
/// guruhga umuman yetib bormasdi — faqat lid TUG'ILGANDAGI xabar turardi va u tezda eskirardi.
/// Endi yuborilgan xabarning <c>message_id</c>'si <see cref="LeadTelegramMessage"/> da saqlanadi va
/// har o'zgarishda o'sha xabar <b>joyida tahrirlanadi</b> (<see cref="SyncCardAsync"/>) — guruh
/// "o'zgardi / yana o'zgardi" xabarlari bilan to'lib ketmaydi.</para>
///
/// <para>⚠️ <b>TAHRIR = JIM YOZUV.</b> Telegram tahrirlangan xabarni bildirishnoma bilan
/// ko'rsatmaydi: chat ro'yxat tepasiga chiqmaydi, telefon jiringlamaydi. Bosqich/izoh/dars
/// o'zgarishi uchun bu TO'G'RI (o'zgarishni qilgan odam CRM'ning o'zida turibdi), lekin
/// TASHQARIDAN kelgan yangi ish (takroriy murojaat, daraja testi natijasi) sezilmay qolardi —
/// shuning uchun u holatda karta tahrirlanadi VA kartaga javob qilib bitta qatorli
/// SIGNAL yuboriladi (<see cref="SignalText"/>).</para>
///
/// <para>⚠️ <b>GURUHGA IZOH MATNI CHIQMAYDI.</b> Menejer izohi — ichki, filtrsiz matn (mijoz
/// haqidagi mulohaza, to'lov qobiliyati va h.k.). Guruh kartasida faqat SANOQ ko'rinadi
/// («💬 3 ta izoh»), to'liq matn esa superadminning SHAXSIY chatidagi kartada qoladi
/// (<see cref="Render"/> — <c>includeNotes</c>).</para>
/// </summary>
public static class LeadNotifier
{
    /// <summary>
    /// Xabar matnining chegarasi. Telegram <c>sendMessage</c>/<c>editMessageText</c> uchun 4096
    /// belgi beradi; biz 4000 da qirqamiz (zaxira bilan).
    ///
    /// <para>⚠️ Bu HOZIR HAM mavjud bo'lgan yashirin nosozlikni yopadi: uzun so'rovnomali daraja
    /// testi matni 4096 dan oshib ketsa Telegram 400 qaytarardi, xato esa tashqi <c>catch</c> da
    /// jim yutilib, XABARNOMA UMUMAN YO'QOLARDI (karta rejimida esa karta hech qachon
    /// yangilanmasdi).</para>
    /// </summary>
    private const int MaxTextLength = 4000;

    /// <summary>Kartada ko'rsatiladigan oxirgi izohlar soni (karta — TARIX emas, JORIY holat).</summary>
    private const int MaxNoteLines = 2;

    /// <summary>Bitta izohning kartadagi eng katta uzunligi (uzuni "…" bilan qirqiladi).</summary>
    private const int MaxNoteLength = 120;

    /// <summary>
    /// Bitta o'chirishda YETIM kartalardan ko'pi bilan nechtasiga qayta urinamiz
    /// (<see cref="MarkDeletedAsync"/>). Chegara bor, chunki bu ish foydalanuvchi so'rovi ICHIDA
    /// bajariladi — o'chirish tugmasi bir necha soniya osilib qolmasin.
    /// </summary>
    private const int MaxOrphanRetry = 20;

    /// <param name="createdBy">Lidni KIM kiritgani. ⚠️ Bu qiymat endi faqat ZAXIRA: karta
    /// «Kiritdi» qatorini DOIM <c>created</c> hodisasining <c>ActorName</c>idan oladi — sabab
    /// <see cref="LoadCardPartsAsync"/> izohida.</param>
    /// <param name="isNewLead">
    /// <c>true</c> — lid ENDI tug'ildi (har chatga yangi karta yuboriladi).
    /// <c>false</c> — TAKRORIY murojaat yoki mavjud lidga yangi test natijasi: mavjud karta
    /// TAHRIRLANADI va unga javob qilib qisqa signal yuboriladi (yangi to'liq karta emas).
    /// Kartasi yo'q eski lidda esa odatdagidek to'liq karta yuboriladi.
    /// ⚠️ Bayroq faqat YETKAZISHNI (tahrir+signal / yangi xabar) hal qiladi; kartaning
    /// SARLAVHASIGA ta'sir qilmaydi — u saqlangan holatdan hisoblanadi (<see cref="HeaderOf"/>).
    /// </param>
    /// <param name="logger">Ixtiyoriy — nosozlik sababi logga tushsin ("nega bu lid kartasi
    /// yangilanmayapti?"). ⚠️ Logga telefon/izoh matni YOZILMAYDI, faqat id'lar.</param>
    public static async Task NotifyNewLeadAsync(
        IAppDbContext db, TelegramService telegram, Lead lead,
        LevelTestSubmission? submission = null, string? testTitle = null,
        bool isNewLead = true, string? createdBy = null, CancellationToken ct = default,
        ILogger? logger = null)
    {
        try
        {
            if (!telegram.IsConfigured) return;

            var regs = await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.UserId != null && r.UserId != "").ToListAsync(ct);
            // Bot qo'shilgan (faol) guruhlar — yangi lid avtomatik shu yerga ham yuboriladi.
            var groupChatIds = await db.TelegramGroups.AsNoTracking()
                .Where(g => g.IsActive).Select(g => g.ChatId).ToListAsync(ct);
            if (regs.Count == 0 && groupChatIds.Count == 0) return;

            var userIds = regs.Select(r => r.UserId!).Distinct().ToList();
            var users = (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync(ct))
                .ToDictionary(u => u.Id);

            // Oluvchi chatlar — TAKRORSIZ va eski tartibda (avval shaxsiy, keyin guruhlar).
            var chats = new List<long>();
            var seen = new HashSet<long>();
            foreach (var r in regs)
            {
                if (!users.TryGetValue(r.UserId!, out var u) || !ShouldNotify(u)) continue;
                if (seen.Add(r.ChatId)) chats.Add(r.ChatId);
            }
            var groupSet = new HashSet<long>(groupChatIds);
            foreach (var gid in groupChatIds)
                if (seen.Add(gid)) chats.Add(gid);
            if (chats.Count == 0) return;

            var parts = await LoadCardPartsAsync(db, lead, submission, testTitle, createdBy, ct);
            // ⚠️ IKKI XIL MATN = IKKI XIL XESH: guruhga izohsiz, shaxsiy chatga izohli variant.
            // Xesh har qator uchun O'ZINING matnidan olinadi — aks holda guruh va shaxsiy chat
            // bir-birining xeshini "eskirgan" deb ko'rib, bekorga qayta yozib turardi.
            var withNotes = Render(parts, includeNotes: true);
            var noNotes = Render(parts, includeNotes: false);
            var now = AppClock.Iso();

            // Shu lidning mavjud kartalari (chat bo'yicha). (LeadId, ChatId) UNIKAL — dublikat yo'q.
            var rows = await db.LeadTelegramMessages
                .Where(m => m.LeadId == lead.Id).ToListAsync(ct);
            var byChat = rows.GroupBy(m => m.ChatId).ToDictionary(g => g.Key, g => g.First());

            foreach (var chatId in chats)
            {
                byChat.TryGetValue(chatId, out var row);
                var card = groupSet.Contains(chatId) ? noNotes : withNotes;

                // TAKRORIY MUROJAAT / TEST NATIJASI + shu chatda TIRIK karta bor:
                // yangi to'liq xabar YUBORILMAYDI — karta tahrirlanadi, ustiga qisqa signal ketadi.
                if (!isNewLead && row is { IsDead: false })
                {
                    var rateLimited = false;
                    // Matn o'zgarmagan bo'lsa so'rov ham, YOZUV ham tegilmaydi (bekorga UPDATE ketmasin).
                    if (row.TextHash != card.Hash)
                    {
                        var res = await ApplyEditAsync(telegram, row, card, now, logger, ct);
                        rateLimited = res.Result == TgEditResult.RateLimited;
                        if (RowTouched(res)) await SaveOrUpdateAsync(db, row, logger, ct);
                    }

                    if (!row.IsDead)
                    {
                        // ⚠️ SIGNAL — tahrir jim bo'lgani uchun. Uning id'si SAQLANMAYDI: u bir
                        // martalik bildirishnoma, hech qachon tahrirlanmaydi.
                        // Tahrir tarmoq xatosi bilan yiqilsa ham yuboriladi — hodisa menejerdan
                        // yashirin qolmasin; TEZLIK CHEGARASIDA (429) esa yuborilmaydi: shu
                        // chatga navbatdagi so'rov chegarani yanada chuqurlashtirardi.
                        if (!rateLimited)
                            await telegram.SendMessageAsync(
                                row.ChatId, SignalText(lead, submission), ct: ct, replyToMessageId: row.MessageId);
                        continue;
                    }
                }

                // YANGI KARTA: yuboramiz va message_id'ni SAQLAYMIZ (keyin shu xabar tahrirlanadi).
                var mid = await telegram.SendMessageReturningIdAsync(chatId, card.Text, ct: ct);
                if (mid is null)
                {
                    // Yubora olmadik — yolg'on yozuv qoldirmaymiz.
                    logger?.LogWarning("Lid kartasi yuborilmadi: {LeadId} / {ChatId}", lead.Id, chatId);
                    continue;
                }

                if (row is null)
                {
                    row = new LeadTelegramMessage
                    {
                        LeadId = lead.Id, ChatId = chatId, MessageId = mid.Value,
                        TextHash = card.Hash, CreatedAt = now, UpdatedAt = now,
                    };
                    db.LeadTelegramMessages.Add(row);
                    byChat[chatId] = row;
                }
                else
                {
                    // ⚠️ (LeadId, ChatId) UNIKAL — mavjud yozuv YANGILANADI, yangi qator qo'shilmaydi.
                    row.MessageId = mid.Value;
                    row.TextHash = card.Hash;
                    row.IsDead = false;
                    row.UpdatedAt = now;
                }

                // 🔴 HAR CHAT UCHUN DARHOL SAQLANADI (bitta yakuniy `SaveChanges` EMAS): xabar
                // allaqachon ketgan, uni bog'lovchi yozuv esa saqlanmasa guruhda YETIM karta
                // qolardi — bazada yozuv yo'q ⇒ u hech qachon tahrirlanmasdi.
                await SaveOrUpdateAsync(db, row, logger, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // ⚠️ Bekor qilingan so'rov "muvaffaqiyat" kabi o'tmasin — chaqiruvchi ham to'xtasin.
            throw;
        }
        catch (Exception ex)
        {
            // Xabarnoma lid yaratishni hech qachon buzmasligi kerak — yutamiz, LEKIN jim emas.
            logger?.LogWarning(ex, "Lid kartasi yuborilmadi: {LeadId}", lead.Id);
        }
    }

    /// <summary>
    /// Lid kartasini JORIY holatga keltiradi: mavjud Telegram xabar(lar)ini <b>joyida tahrirlaydi</b>.
    /// Lid o'zgargan HAR joydan (bosqich, izoh, sinov darsi, konversiya, tahrir) SaveChanges'dan
    /// KEYIN chaqiriladi.
    ///
    /// <para>🔴 <b>ENG MUHIM QOIDA — yozuvi yo'q lidga karta YARATILMAYDI.</b> Aks holda deploydan
    /// ertasiga bir menejer kanbanda 200 ta eski lidni surganda guruhga 200 ta yangi karta
    /// yog'ilardi. Karta faqat HODISADAN tug'iladi (yangi lid / takroriy murojaat / test natijasi —
    /// <see cref="NotifyNewLeadAsync"/>), bu funksiya esa faqat MAVJUDINI yangilaydi.</para>
    ///
    /// <para>Hech qachon istisno chiqarmaydi (ichida <c>try/catch</c>) — karta CRM ishini
    /// buza olmaydi. Yagona istisno: so'rov BEKOR qilingani (<c>OperationCanceledException</c>).</para>
    /// </summary>
    public static async Task SyncCardAsync(
        IAppDbContext db, TelegramService telegram, string leadId, CancellationToken ct = default,
        ILogger? logger = null)
    {
        try
        {
            if (!telegram.IsConfigured || string.IsNullOrWhiteSpace(leadId)) return;

            // O'lik (xabar o'chirilgan) yozuvlar olinmaydi — ularga qayta urinilmaydi.
            var rows = await db.LeadTelegramMessages
                .Where(m => m.LeadId == leadId && !m.IsDead).ToListAsync(ct);
            if (rows.Count == 0) return; // 🔴 kartasi yo'q lid — YANGI xabar YUBORILMAYDI (yuqoriga qarang)

            var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leadId, ct);
            if (lead is null) return; // o'chirilgan lid — `MarkDeletedAsync` ishi

            // ⚠️ OLUVCHI HALI HAM O'RINLIMI? Yozuv bor degani "yuborish kerak" degani EMAS:
            // admin guruhni o'chirgan (`IsActive=false`) bo'lsa, yangi lidlar u yerga bormaydi —
            // eski kartalar ham bormasligi kerak. Shuning uchun qatorlar FAOL guruhlar va
            // superadmin registratsiyalari bilan kesishtiriladi.
            // ⚠️ Kesishmagan qatorga `IsDead` QO'YILMAYDI: guruh qayta yoqilishi mumkin, o'shanda
            // karta o'z joyida yangilanishda davom etadi (o'lik deb belgilansa — abadiy muzlardi).
            var chatIds = rows.Select(r => r.ChatId).Distinct().ToList();
            var groups = await db.TelegramGroups.AsNoTracking()
                .Where(g => chatIds.Contains(g.ChatId))
                .Select(g => new { g.ChatId, g.IsActive }).ToListAsync(ct);
            var knownGroups = groups.Select(g => g.ChatId).ToHashSet();
            var allowed = groups.Where(g => g.IsActive).Select(g => g.ChatId).ToHashSet();
            foreach (var id in await db.TelegramRegistrations.AsNoTracking()
                         .Where(r => r.UserId != null && r.UserId != "" && chatIds.Contains(r.ChatId))
                         .Select(r => r.ChatId).ToListAsync(ct))
                allowed.Add(id);

            // Kartada oxirgi daraja testi natijasi ham turadi — u lidning JORIY holatining bir qismi
            // (aks holda birinchi tahrirdanoq test bloki kartadan yo'qolardi).
            var sub = await db.LevelTestSubmissions.AsNoTracking()
                .Where(s => s.LeadId == leadId)
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id)   // bir sekundda ikkita bo'lsa ham tartib QAT'IY
                .FirstOrDefaultAsync(ct);
            var testTitle = sub is null
                ? null
                : await db.LevelTests.AsNoTracking()
                    .Where(t => t.Id == sub.TestId).Select(t => t.Title).FirstOrDefaultAsync(ct);

            var parts = await LoadCardPartsAsync(db, lead, sub, testTitle, createdByFallback: null, ct);
            var withNotes = Render(parts, includeNotes: true);
            var noNotes = Render(parts, includeNotes: false);
            var now = AppClock.Iso();

            foreach (var row in rows)
            {
                if (!allowed.Contains(row.ChatId)) continue;   // o'chirilgan guruh / bekor qilingan registratsiya

                // Guruhga izoh MATNI chiqmaydi (faqat sanoq), shaxsiy chatga — to'liq.
                var card = knownGroups.Contains(row.ChatId) ? noNotes : withNotes;

                // Matn o'zgarmagan bo'lsa Telegram "message is not modified" qaytarardi —
                // so'rovni umuman yubormaymiz (tezlik chegarasi bekorga sarflanmasin) va
                // yozuvga ham TEGMAYMIZ (bekorga UPDATE ketmasin).
                if (row.TextHash == card.Hash) continue;

                var res = await ApplyEditAsync(telegram, row, card, now, logger, ct);
                if (RowTouched(res)) await SaveOrUpdateAsync(db, row, logger, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Karta yangilanmasligi CRM amalini buza olmaydi — yutamiz, lekin sabab logda qoladi.
            logger?.LogWarning(ex, "Lid kartasi yangilanmadi: {LeadId}", leadId);
        }
    }

    /// <summary>
    /// Lid O'CHIRILGANDA kartani "🗑 Lid o'chirildi" holatiga o'tkazadi va bog'lovchi yozuvlarni
    /// tozalaydi. Lid o'chirilgandan KEYIN chaqiriladi (ism chaqiruvchidan uzatiladi).
    ///
    /// <para><b>NEGA XABAR O'CHIRILMAYDI:</b> Telegram <c>deleteMessage</c> 48 soatdan eski xabarga
    /// ishlamaydi — ya'ni eski karta baribir guruhda qolib, mavjud bo'lmagan lidni ko'rsatib
    /// turardi. Shuning uchun MATNI almashtiriladi.</para>
    ///
    /// <para><b>NEGA ALOHIDA FUNKSIYA</b> (<see cref="SyncCardAsync"/> ga "overrideText" emas):
    /// bu yerda ish tahrir bilan tugamaydi — lid yo'q bo'lgani uchun yozuvlar ham O'CHIRILADI,
    /// aks holda jadvalda hech qachon ishlatilmaydigan yetim qatorlar to'planib borardi.</para>
    ///
    /// <para>🔴 <b>YOZUV FAQAT TAHRIR TASDIQLANGANDA o'chiriladi.</b> Ilgari natija e'tiborsiz
    /// qoldirilardi: 429 yoki tarmoq xatosida guruhda o'chirilgan lidning ismi va TELEFONI bilan
    /// karta abadiy tirik qolar, uni yangilaydigan yozuv esa endi yo'q edi. Endi
    /// <c>RateLimited</c>/<c>Failed</c> da qator QOLADI va u <b>yetim</b> bo'lib turadi (lidi yo'q) —
    /// keyingi istalgan lid o'chirilganda shu funksiyaning o'zi unga qayta urinadi (pastdagi
    /// "yetim kartalar" bloki). Ya'ni alohida fon xizmati qurilmadi, tizim o'zini o'zi tozalaydi.</para>
    /// </summary>
    public static async Task MarkDeletedAsync(
        IAppDbContext db, TelegramService telegram, string leadId, string leadName,
        CancellationToken ct = default, ILogger? logger = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(leadId)) return;
            // ⚠️ Bot sozlanmagan bo'lsa kartani almashtira olmaymiz — demak yozuvni ham
            // O'CHIRMAYMIZ. Aks holda guruhda o'chirilgan lidning kartasi (ism + telefon)
            // abadiy tirik qolar, uni yangilaydigan yagona ip esa uzilib ketardi.
            if (!telegram.IsConfigured) return;

            var rows = await db.LeadTelegramMessages.Where(m => m.LeadId == leadId).ToListAsync(ct);

            // YETIM KARTALAR — lidi bazada YO'Q qatorlar: oldingi o'chirishda tahrir yiqilgan
            // (429/tarmoq) yoki bot o'sha payt sozlanmagan bo'lgan. Har o'chirishda ozgina
            // qismiga qayta urinamiz (`MaxOrphanRetry`) — o'chirish tugmasi osilib qolmasin.
            var orphans = await db.LeadTelegramMessages
                .Where(m => m.LeadId != leadId && !db.Leads.Any(l => l.Id == m.LeadId))
                .Take(MaxOrphanRetry).ToListAsync(ct);

            if (rows.Count == 0 && orphans.Count == 0) return;

            // ⚠️ Yetim qatorda lid ismini bilmaymiz (lid o'chib ketgan) — matn ismsiz chiqadi.
            // Bu maxfiylik uchun ham yaxshi: guruhga ortiqcha ma'lumot tushmaydi.
            var mine = DeletedText(leadName);
            var orphanText = DeletedText(null);

            var removed = new List<LeadTelegramMessage>();
            var migrated = false;
            foreach (var (row, text) in rows.Select(r => (Row: r, Text: mine))
                         .Concat(orphans.Select(o => (Row: o, Text: orphanText))))
            {
                if (row.IsDead) { removed.Add(row); continue; }   // xabar allaqachon yo'q — so'rov shart emas

                var res = await telegram.EditMessageTextOutcomeAsync(row.ChatId, row.MessageId, text, ct: ct);
                if (res.Result is not (TgEditResult.Ok or TgEditResult.NotModified)
                    && res.MigrateToChatId is { } newChat && newChat != row.ChatId)
                {
                    // Guruh supergroup'ga aylangan — qator saqlanadi (yangi chat bilan), keyingi
                    // o'chirishdagi "yetim" bosqichida qayta urinamiz.
                    row.ChatId = newChat;
                    migrated = true;
                    continue;
                }
                if (res.Result is TgEditResult.Ok or TgEditResult.NotModified or TgEditResult.Gone)
                {
                    removed.Add(row);
                    continue;
                }
                // RateLimited | Failed — qator QOLADI (yuqoridagi izoh).
                logger?.LogWarning(
                    "O'chirilgan lid kartasi almashtirilmadi ({Result}, {Seconds}s): {LeadId} / {ChatId}",
                    res.Result, res.RetryAfterSeconds, row.LeadId, row.ChatId);
            }

            if (removed.Count == 0 && !migrated) return;
            if (removed.Count > 0) db.LeadTelegramMessages.RemoveRange(removed);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // ⚠️ Yiqilgan entity'lar `Deleted`/`Modified` holatida ChangeTracker'da QOLADI va
                // AYNI kontekstning keyingi `SaveChangesAsync`ini yiqitardi (chaqiruvchining
                // so'rovi 500 bo'lardi). Faqat O'Z qatorlarimizni chiqaramiz — umumiy
                // `ChangeTracker.Clear()` chaqiruvchining saqlanmagan o'zgarishlarini yo'q qilardi.
                foreach (var r in removed) Detach(db, r);
                foreach (var r in rows.Concat(orphans)) Detach(db, r);
                logger?.LogWarning(ex, "O'chirilgan lid kartasi yozuvlari tozalanmadi: {LeadId}", leadId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Jim emas, lekin o'chirish amalini buzmaymiz.
            logger?.LogWarning(ex, "O'chirilgan lid kartasi yangilanmadi: {LeadId}", leadId);
        }
    }

    // ===================== Yozuvni saqlash (poyga himoyasi) =====================

    /// <summary>
    /// Bitta karta yozuvini DARHOL saqlaydi va poyga holatini yumshoq hal qiladi.
    ///
    /// <para>🔴 NEGA: <c>(LeadId, ChatId)</c> UNIKAL indeks. Bir lidga ikki hodisa deyarli bir vaqtda
    /// kelsa (masalan ommaviy forma + daraja testi), ikkinchi <c>Add</c> <c>DbUpdateException</c>
    /// bilan yiqiladi. EF esa yiqilgan entity'ni <c>Added</c> holatida ChangeTracker'da QOLDIRADI —
    /// tashqi <c>catch</c> uni yutsa ham, AYNI DbContext keyin qayta ishlatilganda (chaqiruvchining
    /// o'z <c>SaveChangesAsync</c>i) o'sha buzuq INSERT qayta uriniladi va bu safar hech kim
    /// yutmaydi: ommaviy lid formasi foydalanuvchisiga 500 ketardi.</para>
    ///
    /// <para>Shuning uchun: yiqilsa entity DETACH qilinadi (⚠️ umumiy
    /// <c>ChangeTracker.Clear()</c> QILINMAYDI — u chaqiruvchining saqlanmagan o'zgarishlarini ham
    /// o'chirib yuborardi) va qator qayta o'qib YANGILANADI: unikal buzilish "qator allaqachon bor"
    /// degani, ya'ni guruhda yetim karta qolmasligi uchun mavjud qatorga yangi
    /// <c>message_id</c>/xesh yozilishi kerak.</para>
    /// </summary>
    private static async Task SaveOrUpdateAsync(
        IAppDbContext db, LeadTelegramMessage row, ILogger? logger, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return;
        }
        catch (OperationCanceledException) { throw; }
        catch (DbUpdateException ex)
        {
            Detach(db, row);
            logger?.LogWarning(ex, "Lid kartasi yozuvi saqlanmadi (poyga?): {LeadId} / {ChatId}",
                row.LeadId, row.ChatId);
        }

        LeadTelegramMessage? existing = null;
        try
        {
            existing = await db.LeadTelegramMessages
                .FirstOrDefaultAsync(m => m.LeadId == row.LeadId && m.ChatId == row.ChatId, ct);
            if (existing is null) return;   // yiqilish unikal indeks sababli emas ekan — qo'ymaymiz
            existing.MessageId = row.MessageId;
            existing.TextHash = row.TextHash;
            existing.IsDead = row.IsDead;
            existing.UpdatedAt = row.UpdatedAt;
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (existing is not null) Detach(db, existing);
            logger?.LogWarning(ex, "Lid kartasi yozuvi qayta yozilmadi: {LeadId} / {ChatId}",
                row.LeadId, row.ChatId);
        }
    }

    /// <summary>Bitta entity'ni kuzatuvdan chiqaradi (kontekst "zaharlanib" qolmasin).</summary>
    private static void Detach(IAppDbContext db, LeadTelegramMessage row)
    {
        try { db.LeadTelegramMessages.Entry(row).State = EntityState.Detached; }
        catch { /* allaqachon kuzatilmayapti — muammo emas */ }
    }

    // ===================== Tahrir natijasini yozuvga qo'llash =====================

    /// <summary>
    /// Kartani tahrirlaydi va natijani YOZUVGA qo'llaydi (xesh / o'lik / ko'chgan chat).
    /// Yozuvni SAQLAMAYDI — saqlashni chaqiruvchi <see cref="SaveOrUpdateAsync"/> orqali qiladi.
    /// </summary>
    private static async Task<TgEditOutcome> ApplyEditAsync(
        TelegramService telegram, LeadTelegramMessage row, CardText card, string now,
        ILogger? logger, CancellationToken ct)
    {
        var res = await telegram.EditMessageTextOutcomeAsync(row.ChatId, row.MessageId, card.Text, ct: ct);

        switch (res.Result)
        {
            case TgEditResult.Ok:
            case TgEditResult.NotModified:
                row.TextHash = card.Hash;
                row.UpdatedAt = now;
                break;
            case TgEditResult.Gone:
                // Xabar yo'q — boshqa urinilmaydi (har o'zgarishda bekorga so'rov ketmasin).
                row.IsDead = true;
                row.UpdatedAt = now;
                break;
            case TgEditResult.RateLimited:
                // Hech narsa saqlanmaydi — keyingi o'zgarishda yana urinamiz. Murakkab kutish
                // mexanizmi ATAYIN qurilmadi: karta jonli hujjat, keyingi tahrir baribir keladi.
                logger?.LogWarning("Lid kartasi: Telegram tezlik chegarasi ({Seconds}s): {LeadId} / {ChatId}",
                    res.RetryAfterSeconds, row.LeadId, row.ChatId);
                break;
            default:
                logger?.LogWarning("Lid kartasi tahrirlanmadi: {LeadId} / {ChatId}", row.LeadId, row.ChatId);
                break;
        }

        // Guruh SUPERGROUP'ga aylandi — Telegram xato javobida YANGI chat id beradi. Karta o'sha
        // yerda TIRIK qoladi, shuning uchun `IsDead` QO'YILMAYDI (yuqorida qo'yilgan bo'lsa ham
        // bekor qilinadi), faqat manzil yangilanadi; matn keyingi o'zgarishda yetkaziladi.
        if (res.Result is not (TgEditResult.Ok or TgEditResult.NotModified)
            && res.MigrateToChatId is { } newChat && newChat != row.ChatId)
        {
            row.ChatId = newChat;
            row.IsDead = false;
            row.UpdatedAt = now;
            logger?.LogInformation("Lid kartasi yangi chatga ko'chdi: {LeadId} / {ChatId}", row.LeadId, newChat);
        }
        return res;
    }

    /// <summary>Tahrir natijasi yozuvni o'zgartirdimi (ya'ni saqlash kerakmi).</summary>
    private static bool RowTouched(TgEditOutcome res) =>
        res.MigrateToChatId is not null
        || res.Result is TgEditResult.Ok or TgEditResult.NotModified or TgEditResult.Gone;

    // ===================== Matn =====================

    // Shaxsiy xabar FAQAT superadminga (ilgari admin/xodimga ham ketardi). Guruhga yuborish alohida — saqlanadi.
    private static bool ShouldNotify(AppUser u) => u.Role == Roles.SuperAdmin;

    /// <summary>
    /// Tahrirlangan karta ustiga yuboriladigan BITTA QATORLI signal — tahrir jim bo'lgani uchun
    /// (Telegram tahrirni bildirishnoma qilmaydi). Ataylab qisqa: batafsili kartaning o'zida,
    /// signal esa faqat "qarang" deydi.
    /// </summary>
    private static string SignalText(Lead l, LevelTestSubmission? sub)
    {
        var who = string.Join(" · ", new[] { l.FullName, l.Phone }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var tail = who.Length > 0 ? $" — {who}" : "";
        if (sub is not null) return $"📝 Daraja testi topshirildi{tail}";
        if (l.RepeatCount > 0) return $"🔁 Takroriy murojaat (×{l.RepeatCount}){tail}";
        return $"🔁 Lid yangilandi{tail}";
    }

    /// <summary>Lid o'chirilganda kartaning o'rniga qoladigan matn (ism ixtiyoriy).</summary>
    private static string DeletedText(string? leadName)
    {
        var lines = new List<string> { "🗑 Lid o'chirildi" };
        if (!string.IsNullOrWhiteSpace(leadName)) lines.Add($"👤 {leadName}");
        lines.Add($"🕒 O'chirildi: {AppClock.Now:HH:mm}");
        return Trim(string.Join("\n", lines));
    }

    /// <summary>Karta uchun bazadan yig'ilgan ma'lumot (matn qurishga tayyor, so'rovlarsiz).</summary>
    private sealed record CardParts(
        Lead Lead, LevelTestSubmission? Sub, string? TestTitle, string? CreatedBy,
        string? StageTitle, TrialLesson? Trial, string? TrialGroupName,
        IReadOnlyList<LeadEvent> Notes, int NoteCount);

    /// <summary>Bitta chatga yuboriladigan matn + uning xeshi (ikkisi DOIM birga yuradi).</summary>
    private readonly record struct CardText(string Text, string Hash);

    /// <summary>
    /// Karta uchun kerakli ma'lumotni yuklaydi. Yuborishda ham, keyingi tahrirlarda ham AYNAN shu
    /// funksiya ishlaydi — ya'ni matn DETERMINLASHGAN (bir xil holatdan bir xil matn), aks holda
    /// xesh bekorga farq qilib, har safar ortiqcha tahrir so'rovi ketardi.
    ///
    /// <para>⚠️ Barcha so'rovlar <c>AsNoTracking</c>: bu ma'lumot faqat O'QISH uchun, ChangeTracker'ga
    /// tushishi shart emas (funksiyaning o'zi keyin <c>SaveChanges</c> chaqiradi — begona
    /// entity'lar u yerga aralashmasin).</para>
    /// </summary>
    private static async Task<CardParts> LoadCardPartsAsync(
        IAppDbContext db, Lead lead, LevelTestSubmission? sub, string? testTitle,
        string? createdByFallback, CancellationToken ct)
    {
        var stageTitle = string.IsNullOrWhiteSpace(lead.Stage)
            ? null
            : await db.LeadStages.AsNoTracking()
                .Where(s => s.Id == lead.Stage).Select(s => s.Title).FirstOrDefaultAsync(ct);

        // Sinov darsi — FAQAT oxirgisi: karta tarix emas, joriy holat.
        // ⚠️ `ThenByDescending(Id)` — bir vaqtga belgilangan ikki dars bo'lsa PostgreSQL tartibi
        // kafolatlanmagan bo'lar, matn (demak xesh ham) sababsiz o'zgarib turardi.
        var trial = await db.TrialLessons.AsNoTracking()
            .Where(t => t.LeadId == lead.Id)
            .OrderByDescending(t => t.ScheduledAt)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);
        var trialGroup = trial is null || string.IsNullOrWhiteSpace(trial.GroupId)
            ? null
            : await db.Classes.AsNoTracking()
                .Where(c => c.Id == trial.GroupId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        // ⚠️ IKKITA hodisa so'rovi BITTAGA birlashtirilgan (UNION ALL): oxirgi izohlar VA lidning
        // "created" hodisasi. Ilgari bu ikki alohida so'rov edi.
        // FAQAT izoh/qo'ng'iroq: bosqich, sinov darsi va konversiya kartada ALOHIDA qatorlar bilan
        // ko'rsatiladi — ularni yana izohlar ro'yxatida takrorlash kartani suyultirardi.
        var noteQuery = db.LeadEvents.AsNoTracking()
            .Where(e => e.LeadId == lead.Id && (e.Type == "note" || e.Type == "call"))
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Take(MaxNoteLines);
        var createdQuery = db.LeadEvents.AsNoTracking()
            .Where(e => e.LeadId == lead.Id && e.Type == "created")
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Take(1);
        var events = await noteQuery.Concat(createdQuery).ToListAsync(ct);

        // ⚠️ Xotirada QAYTA tartiblanadi: UNION natijasining tartibi kafolatlanmagan.
        var notes = events
            .Where(e => e.Type is "note" or "call")
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Take(MaxNoteLines).ToList();

        // ⚠️ "Kiritdi" qatori DOIM `created` hodisasidan olinadi — YUBORISHDA ham. Chaqiruvchi
        // boyroq matn beradi ("Forma: Matematika kursi"), lekin keyingi tahrirlarda uni qayta
        // hisoblab bo'lmaydi — natijada karta BIRINCHI tahrirdanoq kambag'allashib, bekorga
        // bitta tahrir so'rovi ketardi. Bitta manba = barqaror matn = barqaror xesh.
        var createdBy = events
            .Where(e => e.Type == "created")
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => e.ActorName).FirstOrDefault();
        // Zaxira: `created` hodisasi umuman yo'q eski/g'ayrioddiy lidda qator butunlay
        // yo'qolib ketmasin (barcha mavjud yaratish oqimlari bu hodisani yozadi).
        if (string.IsNullOrWhiteSpace(createdBy)) createdBy = createdByFallback;

        // Guruh variantida izoh MATNI emas, SANOG'I ko'rsatiladi — shuning uchun to'liq son kerak
        // (yuklangan `notes` ko'pi bilan `MaxNoteLines` ta, u "3 ta izoh" deyishga yaramaydi).
        var noteCount = await db.LeadEvents.AsNoTracking()
            .CountAsync(e => e.LeadId == lead.Id && (e.Type == "note" || e.Type == "call"), ct);

        return new CardParts(lead, sub, testTitle, createdBy, stageTitle, trial, trialGroup, notes, noteCount);
    }

    /// <summary>
    /// KARTA matni = eski xabar matni (<see cref="BuildText"/>, barcha qatorlari saqlangan) +
    /// lidning JORIY holati (bosqich, sinov darsi, takror, izohlar, konversiya, yangilangan vaqt).
    ///
    /// <para>⚠️ <b>XESH "🕒 Yangilandi" QATORISIZ hisoblanadi.</b> Xeshning butun maqsadi —
    /// "hech narsa o'zgarmagan bo'lsa so'rov yubormaslik". Vaqt qatori matnda bo'lgani uchun xesh
    /// HAR DAQIQA o'zgarardi, ya'ni qisqa yo'l amalda ishlamasdi: menejer kanbanda 20 lidni
    /// sursa 20 × (guruhlar + superadminlar) tahrir ketib, Telegram guruh chegarasi
    /// (~20 xabar/daqiqa) urilardi.</para>
    ///
    /// <para>⚠️ <b>QIRQISH HOLAT BLOKIDAN EMAS, TEPADAN.</b> Ilgari butun satr OXIRIDAN kesilardi:
    /// uzun so'rovnomali lidda aynan holat bloki (bosqich, sinov darsi, izohlar, "O'quvchi bo'ldi",
    /// vaqt) qirqilib ketar, matn holatga qarab o'zgarmay qolar va xesh barqarorlashib karta
    /// ABADIY MUZLARDI. Endi holat bloki har doim saqlanadi, kam qimmatli tepa qism
    /// (so'rovnoma / uzun izoh) qisqartiriladi.</para>
    /// </summary>
    private static CardText Render(CardParts p, bool includeNotes)
    {
        var body = BuildText(p.Lead, p.Sub, p.TestTitle, HeaderOf(p.Lead, p.Sub), p.CreatedBy);
        var state = BuildStateText(p, includeNotes);
        // Odam kartaning TIRIK ekanini ko'rsin (oxirgi tahrir vaqti) — lekin XESHGA kirmaydi.
        var stamp = $"🕒 Yangilandi: {AppClock.Now:HH:mm}";

        // Bo'sh qator + ajratkich — eski karta ko'rinishi saqlanadi.
        var tail = state.Length > 0 ? "\n\n" + state : "";
        var budget = MaxTextLength - tail.Length - stamp.Length - 1;   // -1: stamp oldidagi "\n"
        if (budget < 0) budget = 0;
        if (body.Length > budget) body = Cut(body, budget);

        var hashSource = body + tail;
        return new CardText(Trim(hashSource + "\n" + stamp), Sha256Hex(hashSource));
    }

    /// <summary>
    /// Karta sarlavhasi. ⚠️ Chaqiruvchining bir martalik <c>isNewLead</c> bayrog'idan EMAS, faqat
    /// SAQLANGAN ma'lumotdan hisoblanadi — aks holda <see cref="SyncCardAsync"/> keyin boshqa
    /// sarlavha chizib, matn (demak xesh) har sinxronizatsiyada o'zgarardi.
    ///
    /// <para>"Mavjud lid" belgisi: test natijasi lidning O'ZIDAN KEYIN yaratilgan bo'lsa, demak lid
    /// allaqachon bor edi. Bu <c>RepeatCount</c> dan ishonchliroq: taklif havolasi bilan
    /// yuborilgan testda (<c>LevelTestService</c>) <c>RepeatCount</c> OSHIRILMAYDI, ya'ni
    /// mavjud lidning kartasi «🆕 Yangi lid!» deb qayta yozilardi.</para>
    ///
    /// <para>Lid testdan TUG'ILGAN bo'lsa ikkalasining vaqti bir xil (bitta <c>now</c> dan
    /// yoziladi) — sarlavha to'g'ri holda «🆕 Yangi lid!» bo'lib qoladi.</para>
    /// </summary>
    private static string HeaderOf(Lead l, LevelTestSubmission? sub)
    {
        if (sub is not null &&
            string.CompareOrdinal(sub.CreatedAt.Trim(), l.CreatedAt.Trim()) > 0)
            return "🔁 Mavjud lid — yangi test natijasi";
        if (l.RepeatCount > 0) return "🔁 Mavjud lid yangilandi";
        return "🆕 Yangi lid!";
    }

    /// <summary>Kartaning HOLAT bloki ("🕒 Yangilandi" qatorisiz — u xeshdan tashqarida).</summary>
    private static string BuildStateText(CardParts p, bool includeNotes)
    {
        var l = p.Lead;
        var state = new List<string> { "— — —" };
        if (!string.IsNullOrWhiteSpace(p.StageTitle)) state.Add($"📍 Bosqich: {p.StageTitle}");
        if (p.Trial is { } trial)
        {
            var group = string.IsNullOrWhiteSpace(p.TrialGroupName) ? "" : $" · {p.TrialGroupName}";
            state.Add($"🎓 Sinov darsi: {HumanDate(trial.ScheduledAt)}{group} — {TrialLabel(trial.Result)}");
        }
        if (l.RepeatCount > 0)
        {
            var when = string.IsNullOrWhiteSpace(l.LastRepeatAt) ? "" : $" ({HumanDate(l.LastRepeatAt)})";
            state.Add($"🔁 Takroriy murojaat: ×{l.RepeatCount}{when}");
        }
        if (includeNotes && p.Notes.Count > 0)
        {
            state.Add("💬 Oxirgi izohlar:");
            foreach (var n in p.Notes)
                state.Add($"• {Shorten(n.Text, MaxNoteLength)}"
                          + (string.IsNullOrWhiteSpace(n.ActorName) ? "" : $" — {n.ActorName}"));
        }
        else if (!includeNotes && p.NoteCount > 0)
        {
            // ⚠️ GURUHDA faqat SANOQ. Izoh — ichki, filtrsiz matn (mijoz haqidagi mulohaza,
            // to'lov qobiliyati va h.k.); u har bir guruhga tarqalmasligi kerak. To'liq matn
            // superadminning SHAXSIY chatidagi kartada qoladi.
            state.Add($"💬 {p.NoteCount} ta izoh");
        }
        if (!string.IsNullOrWhiteSpace(l.ConvertedStudentId)) state.Add("✅ O'quvchi bo'ldi");
        return string.Join("\n", state);
    }

    private static string BuildText(
        Lead l, LevelTestSubmission? sub, string? testTitle, string header, string? createdBy)
    {
        var lines = new List<string> { header };
        if (!string.IsNullOrWhiteSpace(l.FullName)) lines.Add($"👤 {l.FullName}");
        if (!string.IsNullOrWhiteSpace(l.Phone)) lines.Add($"📞 {l.Phone}");
        if (!string.IsNullOrWhiteSpace(l.Source)) lines.Add($"🔖 Manba: {l.Source}");
        if (!string.IsNullOrWhiteSpace(l.InterestSubject)) lines.Add($"📚 Qiziqish: {l.InterestSubject}");

        if (sub is not null)
        {
            // Daraja testi natijasi — batafsil.
            lines.Add("");
            lines.Add("📊 Daraja testi natijasi");
            if (!string.IsNullOrWhiteSpace(testTitle)) lines.Add($"📝 Test: {testTitle}");
            if (sub.Total > 0)
            {
                lines.Add($"✅ Ball: {sub.Score}/{sub.Total} ({sub.Percent}%)");
                lines.Add($"{PerfIcon(sub.Percent)} Baho: {PerfLabel(sub.Percent)}");
            }
            else
            {
                lines.Add("ℹ️ Test savolsiz (faqat so'rovnoma).");
            }
            if (!string.IsNullOrWhiteSpace(sub.Level)) lines.Add($"🎯 Daraja: {sub.Level}");
            if (sub.Age > 0) lines.Add($"🎂 Yoshi: {sub.Age}");

            var survey = ParseSurvey(sub.SurveyJson);
            if (survey.Count > 0)
            {
                lines.Add("");
                lines.Add("🗒 So'rovnoma:");
                foreach (var a in survey)
                {
                    // ⚠️ `Answers` NULL bo'lishi mumkin: JSON'da "Answers" maydoni umuman bo'lmasa
                    // deserializatsiya uni null qoldiradi. Ilgari `a.Answers.Count` NullReference
                    // tashlar, tashqi catch uni yutar va BUTUN XABARNOMA yo'qolardi (karta rejimida
                    // esa karta hech qachon yangilanmasdi). Endi bo'sh javob "—" bo'lib chiqadi.
                    var answers = a.Answers is { Count: > 0 } ? string.Join(", ", a.Answers) : "—";
                    lines.Add($"• {a.Question}: {answers}");
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(l.Note))
        {
            lines.Add("");
            lines.Add($"📝 {l.Note}");
        }

        // Eng tagida — lidni kim kiritgani.
        if (!string.IsNullOrWhiteSpace(createdBy))
        {
            lines.Add("");
            lines.Add($"🧑‍💼 Kiritdi: {createdBy}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Foizga qarab sifat bahosi (qanday ishladi).</summary>
    private static string PerfLabel(int p) => p >= 80 ? "A'lo" : p >= 60 ? "Yaxshi" : p >= 40 ? "O'rta" : "Past";
    private static string PerfIcon(int p) => p >= 60 ? "🟢" : p >= 40 ? "🟡" : "🔴";

    private static string TrialLabel(string? result) => result switch
    {
        "stayed" => "qoldi",
        "left" => "ketdi",
        _ => "kutilmoqda",
    };

    /// <summary>ISO sana/vaqtni odam o'qiydigan ko'rinishga keltiradi ("2026-08-22T15:00" → "2026-08-22 15:00").</summary>
    private static string HumanDate(string? iso)
    {
        var v = (iso ?? "").Trim();
        if (v.Length == 0) return "—";
        if (v.Length > 16) v = v[..16];
        return v.Replace('T', ' ');
    }

    /// <summary>Uzun matnni qirqadi (izoh kartani bosib ketmasin). Qator ko'chirish belgilari —
    /// `\n` ham, `\r` ham — bo'shliqqa aylanadi (Windows'dan kiritilgan matn qatorni buzmasin).</summary>
    private static string Shorten(string? text, int max)
    {
        var v = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return v.Length <= max ? v : Cut(v, max);
    }

    /// <summary>Butun xabarni Telegram chegarasiga sig'diradi (<see cref="MaxTextLength"/>).</summary>
    private static string Trim(string text) => text.Length <= MaxTextLength ? text : Cut(text, MaxTextLength);

    private static string Cut(string text, int max)
    {
        if (max <= 0) return "";
        if (max == 1) return "…";
        var cut = max - 1;
        // ⚠️ Emoji ikkita `char` (surrogat juftlik) — o'rtasidan kesilsa buzuq belgi chiqardi.
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return string.Concat(text.AsSpan(0, cut), "…");
    }

    /// <summary>Matn xeshi — kartani bekorga tahrirlamaslik uchun (<see cref="LeadTelegramMessage.TextHash"/>).</summary>
    private static string Sha256Hex(string text) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static readonly System.Text.Json.JsonSerializerOptions SurveyOpts = new() { PropertyNameCaseInsensitive = true };

    private static List<SurveyAnswerDto> ParseSurvey(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            // Null elementlar ham tashlanadi ("[null]" kabi buzuq JSON butun matnni yiqitmasin).
            return System.Text.Json.JsonSerializer.Deserialize<List<SurveyAnswerDto>>(json, SurveyOpts)
                       ?.Where(a => a is not null).ToList()
                   ?? new();
        }
        catch { return new(); }
    }
}
