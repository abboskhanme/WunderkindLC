using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Instagram modulining ASOSIY OQIMI: navbatdagi bitta <see cref="IgWebhookEvent"/> ni to'liq
/// qayta ishlaydi (parse → suhbat → xabar → qoida/AI → javob → lid → signal).
///
/// <para><b>HAR BOSQICH ALOHIDA <c>try/catch</c>:</b> yordamchi tizim yiqilsa ham asosiy vazifa
/// (mijozga javob berish va yozib qo'yish) bajariladi. Telegram xatosi esa umuman JIM yutiladi.</para>
///
/// <para><b>Modul o'chiq bo'lsa hech qanday tashqi so'rov ketmaydi:</b> kiruvchi xabar bazaga
/// yoziladi (tarix yo'qolmasin), lekin AI ham, Graph API ham chaqirilmaydi.</para>
///
/// <para><b>Cheksiz halqadan himoya — 4 qavat, biri ham olib tashlanmaydi:</b>
/// (1) o'z izohimiz parserda tashlanadi (UCHALA identifikator: IG id, app-scoped id, username);
/// (2) echo xabar javob berish uchun ISHLATILMAYDI (faqat operator pauzasini yoqadi);
/// (3) bot o'z javobiga javob bermaydi — echo bizning oxirgi chiquvchi xabarimiz bilan
/// solishtiriladi;
/// (4) <b>AVTOMAT O'CHIRGICH</b> — 10 daqiqada bitta post ostida 8, umumiy 30 javob chegarasi
/// (<c>InstagramContract.BurstBlockReason</c>) + kunlik chegara. Kunlik chegara (200) YOLG'IZ
/// yetmaydi: halqa daqiqalar ichida yuzlab javob yozadi va Instagram akkauntni 200 ga
/// yetmasdan spam deb belgilaydi.</para>
///
/// <para>DI: <c>builder.Services.AddSingleton&lt;InstagramPipeline&gt;();</c> — ichkarida
/// har chaqiruvda o'z <c>scope</c>i olinadi.</para>
/// </summary>
public sealed class InstagramPipeline(IServiceProvider services, ILogger<InstagramPipeline> logger)
{
    /// <summary>Bitta navbat yozuvini qayta ishlaydi va uning <c>Status</c>ini yangilaydi.</summary>
    public async Task ProcessAsync(string eventId, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IAppDbContext>();

        var ev = await db.IgWebhookEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || ev.Status != IgConst.EvPending) return;

        ev.Attempts += 1;
        try
        {
            var api = sp.GetRequiredService<InstagramApi>();
            var telegram = sp.GetRequiredService<TelegramService>();
            var config = sp.GetRequiredService<IConfiguration>();

            var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
            var account = await db.IgAccounts
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.ConnectedAt)
                .FirstOrDefaultAsync(ct);

            // ── REKLAMA LIDI (Meta Lead Ads) ──
            // Payload FACEBOOK PAGE obyektidan keladi va izoh/DM bilan hech narsa bo'lishmaydi
            // (AI ham, suhbat ham, 24 soatlik oyna ham yo'q). Shuning uchun u ALOHIDA xizmatga
            // beriladi va oqim shu yerda tugaydi — bitta webhook yozuvida ikkala tur birga
            // kelmaydi (`page` va `instagram` — ayri obyektlar).
            var leadgen = MetaLeadgenParser.Parse(ev.RawJson);
            if (leadgen.Count > 0)
            {
                var leadProblems = await sp.GetRequiredService<MetaLeadgenService>()
                    .HandleAsync(leadgen, meta, ct);

                ev.Status = IgConst.EvDone;
                ev.Error = leadProblems.Count == 0
                    ? ""
                    : InstagramContract.Trim(string.Join(" | ", leadProblems), 500);
                ev.ProcessedAt = AppClock.Iso();
                await db.SaveChangesAsync(ct);
                return;
            }

            var incoming = InstagramEventParser.Parse(ev.RawJson, new InstagramEventParser.IgSelf(
                IgUserId: account?.IgUserId ?? "",
                AppScopedId: account?.AppScopedUserId ?? "",
                Username: account?.Username ?? ""));
            if (incoming.Count == 0)
            {
                // Qo'llab-quvvatlanmaydigan maydon (`mentions`, `live_comments`), reaksiya/o'qildi
                // hodisasi yoki O'ZIMIZNING izohimiz. Jimgina yo'qolmasin — diagnostikada ko'rinadi.
                //
                // ⚠️ Maydon nomi ATAYIN sababga chiqariladi: "hodisa kelyapti, lekin hech narsa
                // bo'lmayapti" holatining eng ko'p uchraydigan sababi — Meta'da keraksiz maydonga
                // obuna bo'lib qolish. Umumiy matn buni ko'rsatmasdi.
                var unsupported = InstagramEventParser.UnsupportedFields(ev.RawJson);
                if (unsupported.Length > 0)
                    logger.LogWarning(
                        "Instagram: qo'llab-quvvatlanmaydigan webhook maydoni — {Fields} (hodisa: {Id})",
                        unsupported, ev.Id);

                ev.Status = IgConst.EvSkipped;
                ev.Error = unsupported.Length > 0
                    ? $"Qo'llab-quvvatlanmaydigan webhook hodisasi: {unsupported}. "
                      + "Modul faqat `comments` va `messages` ni ishlaydi. Agar KUTILGAN hodisa "
                      + "(izoh yoki DM) o'rniga shular kelayotgan bo'lsa — Meta Dashboard'da "
                      + "ILOVA darajasida `messages`/`comments` maydonlari belgilanmagan "
                      + "(akkaunt obunasi yolg'iz YETMAYDI, ikkala qatlam ham kerak)."
                    : "Qayta ishlanadigan hodisa topilmadi (qo'llab-quvvatlanmaydigan tur yoki o'z yozuvimiz).";
                ev.ProcessedAt = AppClock.Iso();
                await db.SaveChangesAsync(ct);
                return;
            }

            var problems = new List<string>();
            foreach (var inc in incoming)
            {
                try
                {
                    await HandleOneAsync(db, api, telegram, config, meta, account, inc, ev.ReceivedAt, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Instagram hodisasini qayta ishlashda xatolik ({Key})", inc.EventKey);
                    problems.Add(ex.Message);
                }
            }

            ev.Status = IgConst.EvDone;
            ev.Error = problems.Count == 0 ? "" : InstagramContract.Trim(string.Join(" | ", problems), 500);
            ev.ProcessedAt = AppClock.Iso();
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Instagram navbat yozuvi qayta ishlanmadi ({Id})", eventId);
            ev.Status = ev.Attempts >= IgConst.MaxAttempts ? IgConst.EvFailed : IgConst.EvPending;
            ev.Error = InstagramContract.Trim(ex.Message, 500);
            ev.ProcessedAt = AppClock.Iso();
            try { await db.SaveChangesAsync(ct); }
            catch (Exception saveEx) { logger.LogError(saveEx, "Instagram navbat holatini saqlab bo'lmadi"); }
        }
    }

    /* ═════════════════════════ Bitta hodisa ═════════════════════════ */

    private async Task HandleOneAsync(
        IAppDbContext db, InstagramApi api, TelegramService telegram, IConfiguration config,
        CenterMeta? meta, IgAccount? account, IgIncomingEvent inc, string receivedAtIso, CancellationToken ct)
    {
        var now = AppClock.Now;
        var nowIso = AppClock.Iso();

        // ── 0.0) META SIYOSATI OGOHLANTIRISHI (E6.7) ──
        // Dedupdan ham OLDIN: bu suhbat hodisasi emas, butun modulga tegishli signal.
        if (inc.Kind == InstagramEventParser.KindPolicy)
        {
            await HandlePolicyAsync(db, telegram, meta, inc, ct);
            return;
        }

        // ── 0.1) MIJOZ XABARNI O'CHIRDI (E6.4) ──
        // ⚠️ DEDUPDAN OLDIN turishi SHART: o'chirish hodisasi asl xabarning `mid` i bilan keladi
        // va `AlreadyHandledAsync` uni "allaqachon ishlangan" deb tashlab yuborardi — ya'ni matn
        // bazada QOLIB KETARDI (Platform Terms buzilishi).
        if (inc.Kind == InstagramEventParser.KindDeleted || inc.IsDeleted)
        {
            await HandleDeletedAsync(db, inc, ct);
            return;
        }

        // ── 0) HODISA DARAJASIDAGI DEDUP (navbat kalitidan MUSTAQIL) ──
        if (await AlreadyHandledAsync(db, inc, ct))
        {
            logger.LogInformation(
                "Instagram: hodisa allaqachon qayta ishlangan — o'tkazib yuborildi ({Key})", inc.EventKey);
            return;
        }

        var conv = await db.IgConversations.FirstOrDefaultAsync(c => c.IgUserId == inc.SenderId, ct);

        // ── ECHO: javob uchun EMAS, faqat operator pauzasi uchun ──
        if (inc.IsEcho)
        {
            await HandleEchoAsync(db, conv, inc, now, nowIso, ct);
            return;
        }

        if (conv is null)
        {
            conv = new IgConversation
            {
                IgUserId = inc.SenderId,
                Username = inc.Username,
                Status = IgConst.StatusBot,
                CreatedAt = nowIso,
            };
            db.IgConversations.Add(conv);
        }
        else if (conv.Username.Length == 0 && inc.Username.Length > 0)
        {
            conv.Username = inc.Username;   // DM'da username kelmaydi, izohda keladi
        }

        // ── 0.4) USERNAME'NI PROFIL SO'ROVIDAN ANIQLASH (DM uchun) ──
        //
        // ⚠️ Webhook'ning `messaging[]` bo'limida username UMUMAN yo'q — faqat `sender.id`
        // (qoidalar §11 tuzoq 6, TEXNIK.md §3.5). Usiz Inbox'da DM suhbatlari `@1784140…`
        // degan RAQAM bo'lib turardi: operator kim bilan yozishayotganini bilmasdi, Telegram
        // signalida ham raqam ko'rinardi. Izohda username bor, shuning uchun so'rov amalda
        // faqat DM'dan boshlangan suhbatga ketadi.
        //
        // ⚠️ BIR MARTA: natija `IgConversation.Username` da SAQLANADI, ya'ni keyingi xabarlarda
        // shart bajarilmaydi. Yangi ustun (va migratsiya) kerak emas.
        //
        // ⚠️ `InstagramEnabled` darvozasi ostida (§3): modul o'chiq markazda tashqariga
        // HECH QANDAY so'rov ketmasligi kerak.
        //
        // ⚠️ Xato JIM yutiladi: username — qulaylik, xabarning o'zi emas. Profil yopiq bo'lsa
        // yoki so'rov yiqilsa suhbat eski holida (id bilan) davom etadi.
        if (conv.Username.Length == 0
            && meta is not null && meta.InstagramEnabled
            && account is not null && account.AccessToken.Length > 0
            && inc.SenderId.Length > 0)
        {
            try
            {
                var (okProfile, uname, _, profileErr) =
                    await api.GetUserProfileAsync(inc.SenderId, account.AccessToken, ct);
                if (okProfile && uname.Length > 0) conv.Username = uname;
                else if (!okProfile)
                    logger.LogWarning("Instagram: profil so'rovi bajarilmadi ({Id}): {Err}", inc.SenderId, profileErr);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Instagram: profil so'rovida xatolik ({Id})", inc.SenderId);
            }
        }

        var channel = inc.Kind == IgConst.KindComment ? IgConst.ChannelComment : IgConst.ChannelDm;

        // ── 0.45) POSTBACK (FAQ tugmasi) — "xabar matni" tugma SARLAVHASI ──
        // Meta title'ni bo'sh yuborishi ham mumkin: lentada bo'sh qator qolmasin va pastdagi
        // "matnsiz xabar → operator" eskalatsiyasi ishlab ketmasin (tugma bosilgani mazmunan
        // matnli murojaat, rasm/stiker emas).
        var inboundText = inc.Text;
        if (inc.Kind == InstagramEventParser.KindPostback && string.IsNullOrWhiteSpace(inboundText))
            inboundText = "[FAQ tugmasi bosildi]";

        // ── 0.5) REKLAMA ATRIBUTSIYASI (E3) — TAXMINIY, yiqilsa oqim DAVOM ETADI ──
        var ad = await TryAttributeAdAsync(db, inc, ct);
        if (ad.Found && conv.AdId.Length == 0)
        {
            // Suhbat darajasida BIRINCHI teginish saqlanadi (keyingi izohlar boshqa reklama
            // ostida bo'lsa ham manba o'zgarmaydi — `Lead` dagi first-touch qoidasi bilan bir xil).
            conv.AdId = ad.AdId;
            conv.AdCampaignId = ad.CampaignId;
        }

        // ── 0.6) STORY / ULASHILGAN POST KONTEKSTI (E6.1–E6.3) ──
        // Kontekst xabar MATNIGA qo'shiladi: story id/url uchun alohida ustun yo'q (bu bosqichda
        // migratsiya qilinmaydi), AI esa "nimaga javob yozilyapti" ni bilmasa mazmunsiz javob
        // beradi. Konteksti bo'lmagan oddiy xabarda satr BO'SH — mavjud xulq o'zgarmaydi.
        var context = InstagramEventParser.ContextNote(inc);
        var storedText = context.Length == 0
            ? inboundText
            : (inboundText.Length == 0 ? context : context + "\n" + inboundText);

        // ── 1) Kiruvchi xabar HAR DOIM yoziladi (javob berilmasa ham tarix qoladi) ──
        db.IgMessages.Add(new IgMessage
        {
            ConversationId = conv.Id,
            Direction = IgConst.DirIn,
            Channel = channel,
            Text = storedText,
            MediaId = inc.MediaId,
            CommentId = inc.CommentId,
            IgMessageId = inc.IgMessageId,
            AdId = ad.AdId,
            AdCampaignId = ad.CampaignId,
            // ⚠️ `conv.Username` (inc.Username EMAS): DM'da webhook username bermaydi, u
            // yuqorida profil so'rovidan aniqlanadi — aks holda har DM qatori "Mijoz" bo'lardi.
            ActorName = conv.Username.Length > 0 ? "@" + conv.Username : "Mijoz",
            CreatedAt = nowIso,
        });
        // ⚠️ 24 soatlik oyna MIJOZ YOZGAN vaqtdan hisoblanadi, biz qayta ishlagan vaqtdan emas.
        // Navbat uzoq turib qolsa (modul o'chiq bo'lib keyin yoqilsa, yoki Meta 36 soat davomida
        // qayta yuborsa) oyna "ochiq" bo'lib ko'rinardi, biz javob yuborardik va Instagram uni
        // RAD ETARDI — operator esa sababini bilmasdi.
        //
        // ⚠️ Lekin Meta vaqti KO'R-KO'RONA ishonilmaydi: server soati oldinga surilgan yoki
        // payload buzuq bo'lsa bot BUTUNLAY jim bo'lib qolardi (eng yomon nosozlik — sababsiz
        // sukut). Shuning uchun faqat "mantiqiy" oraliqdagi vaqt qabul qilinadi, aks holda
        // joriy vaqt (eski xulq).
        conv.LastInboundAt = SaneInboundAt(inc.SentAtIso, now) ?? nowIso;
        conv.LastMessageText = InstagramContract.Trim(storedText, 300);
        conv.MessageCount += 1;
        conv.Unread = true;

        // ── 2) MATNSIZ xabar (rasm/stiker/ovoz) — jimgina yo'qolmaydi ──
        // (Postback bu yerga tushmaydi: `inboundText` 0.45-qadamda har doim to'ldiriladi.)
        if (string.IsNullOrWhiteSpace(inboundText))
        {
            Escalate(conv, "Matnsiz xabar keldi (rasm/stiker/ovozli xabar) — AI javob bera olmaydi"
                           + (context.Length > 0 ? " " + context : ""));
            // Konteksti bor bo'lsa (story mention, ulashilgan post) operator ro'yxatda AYNAN
            // nima kelganini ko'rsin — "[matnsiz xabar]" dan foydaliroq.
            conv.LastMessageText = context.Length > 0
                ? InstagramContract.Trim(context, 300)
                : "[matnsiz xabar]";
            await db.SaveChangesAsync(ct);
            await NotifyAdminsAsync(db, telegram, meta,
                $"📎 Instagram: mijoz matnsiz xabar yubordi — operator ko'rsin.\n"
                + $"👤 {InstagramContract.ProfileRef(conv.Username)}", ct);
            return;
        }

        // ── 3) DARVOZALAR: shu yerdan keyin tashqi so'rov ketishi mumkin ──
        if (meta is null || !meta.InstagramEnabled) { await db.SaveChangesAsync(ct); return; }

        // ── 3.1) 🔴 TELEFON RAQAMI → LID (AI'ga ham, javob oqimiga ham BOG'LIQ EMAS) ──
        //
        // Mijoz raqamini (yoki «Ali Valiyev 90 123 45 67» kabi ism + raqamini) yozdi — bu markaz
        // uchun TAYYOR lid. Ilgari lid FAQAT §9 da, faqat AI `lead_contact` ni to'ldirgan
        // taqdirda yozilardi. Ya'ni raqam quyidagi hollarda JIMGINA yo'qolardi:
        //
        //   • operator suhbatni o'z qo'liga olgan (`BotMayReply` false) — eng ko'p uchraydigani:
        //     odam aynan operator bilan gaplashib turib raqamini beradi;
        //   • DM yoki izoh avtojavobi sozlamalardan o'chirilgan;
        //   • kalit so'z qoidasi `StopAi` bilan ishlagan — AI umuman chaqirilmaydi;
        //   • AI yiqilgan, kunlik/halqa chegarasi urilgan, token yo'q, 24 soatlik oyna yopiq;
        //   • AI ishladi, lekin `lead_contact` ni to'ldirmadi (model xatosi).
        //
        // Bularning HAMMASI javob berish haqida, LID haqida emas — shuning uchun tekshiruv
        // darvozalardan OLDIN turadi va faqat «modul yoqilganmi» gatega bo'ysunadi.
        //
        // ⚠️ Manba — mijoz YOZGAN matn (`inc.Text`), `storedText` EMAS: story/ulashilgan post
        // konteksti ichida ham raqamlar bor (media id, url) va ular telefon deb olinishi mumkin.
        var inboundPhone = InstagramContract.ExtractPhone(inc.Text);
        var phoneLeadId = "";
        var phoneLeadGuessedName = false;

        // ⚠️ Karta o'zgaruvchilari SHU YERDA e'lon qilinadi (ilgari §9 da edi): telefon yo'li
        // ham, AI yo'li ham bitta kartani boshqaradi — §9.1 ga qarang.
        var cardLeadId = "";
        var cardIsNewLead = false;
        var firstLink = false;

        if (inboundPhone.Length > 0)
        {
            try
            {
                // Ism — AI'siz, ehtiyotkor taxmin (`ExtractLeadName`). Topilmasa lid baribir
                // yoziladi, nomi «username (Instagram)» bo'ladi va AI kelganda aniqlashadi.
                var guessedName = InstagramContract.ExtractLeadName(inc.Text);
                var firstPhoneLink = string.IsNullOrWhiteSpace(conv.LeadId);
                var phoneSource = string.IsNullOrWhiteSpace(meta.InstagramLeadSource) ? "Instagram" : meta.InstagramLeadSource;

                var (pid, pIsNew) = await InstagramLeadBridge.UpsertAsync(
                    db, conv,
                    InstagramContract.PhoneOnlyOutput(inboundPhone, guessedName, conv.Language),
                    phoneSource, ct);

                // Kontakt qoldirgan odam ta'rifga ko'ra qaynoq — Inbox'da ham shunday ko'rinsin.
                conv.LeadScore = Math.Max(conv.LeadScore, IgConst.HotLeadScore);
                phoneLeadId = pid;
                phoneLeadGuessedName = guessedName.Length > 0;

                // 🔴 DARHOL saqlanadi: pastdagi darvozalardan biri `return` qilsa ham lid QOLADI.
                await db.SaveChangesAsync(ct);

                // Telegram kartasi shu yerda yuboriladi — §9.1 ga yetib bormaydigan yo'llar bor.
                // Keyin AI xulosasi qo'shilsa karta JOYIDA tahrirlanadi (`SyncCardAsync`).
                if (firstPhoneLink && meta.InstagramNotifyTelegram)
                {
                    var phoneLead = await db.Leads.FirstOrDefaultAsync(l => l.Id == pid, ct);
                    if (phoneLead is not null)
                        await LeadNotifier.NotifyNewLeadAsync(
                            db, telegram, phoneLead, isNewLead: pIsNew,
                            createdBy: InstagramLeadBridge.ActorName, ct: ct, logger: logger);
                }
                else
                {
                    await LeadNotifier.SyncCardAsync(db, telegram, pid, ct, logger);
                }

                logger.LogInformation(
                    "Instagram: xabarda telefon topildi — lid {State} ({LeadId})",
                    pIsNew ? "yaratildi" : "yangilandi", pid);
            }
            catch (Exception ex)
            {
                // ⚠️ Jim yiqilmaydi: raqam berilgan, ya'ni bu YO'QOTILGAN mijoz bo'lishi mumkin.
                logger.LogError(ex, "Instagram: telefon bo'yicha lid yozib bo'lmadi ({Conv})", conv.Id);
                Escalate(conv, "Mijoz telefon qoldirdi, lekin lid yozilmadi — operator qo'lda kiritsin");
            }
        }
        if (channel == IgConst.ChannelComment && !meta.InstagramAutoReplyComments) { await db.SaveChangesAsync(ct); return; }
        if (channel == IgConst.ChannelDm && !meta.InstagramAutoReplyDm) { await db.SaveChangesAsync(ct); return; }
        if (!InstagramContract.BotMayReply(conv, now)) { await db.SaveChangesAsync(ct); return; }

        if (account is null || string.IsNullOrWhiteSpace(account.AccessToken))
        {
            Escalate(conv, "Instagram akkaunt ulanmagan yoki token yo'q — javob yuborilmadi");
            await db.SaveChangesAsync(ct);
            await NotifyAdminsAsync(db, telegram, meta, "⚠️ Instagram: akkaunt ulanmagan — mijozga javob yuborilmadi.", ct);
            return;
        }

        // ── 4) KUNLIK LIMIT (halqa/hujum himoyasi) ──
        var today = now.ToString("yyyy-MM-dd");
        var limit = Math.Max(1, meta.InstagramDailyReplyLimit);
        var sentToday = await db.IgMessages
            .CountAsync(m => m.Direction == IgConst.DirOut && m.CreatedAt.StartsWith(today), ct);
        if (sentToday >= limit)
        {
            Escalate(conv, $"Kunlik javob chegarasi tugadi ({limit}) — javob yuborilmadi");
            await db.SaveChangesAsync(ct);
            await NotifyAdminsAsync(db, telegram, meta, $"🚦 Instagram: kunlik javob chegarasi ({limit}) tugadi.", ct);
            return;
        }

        // ── 4.5) HALQA AVTOMAT O'CHIRGICHI (qisqa oyna) ──
        //
        // Kunlik chegara (200) — uzoq muddatli to'siq. Cheksiz halqa esa DAQIQALAR ichida yuzlab
        // javob yozadi va Instagram akkauntni 200 ga yetmasdan spam deb belgilaydi. Shuning uchun
        // 10 daqiqalik oynada ikkita qo'shimcha chegara: bitta post ostida 8, umumiy 30.
        var burstSince = now.AddMinutes(-IgConst.BurstWindowMinutes).ToString("yyyy-MM-ddTHH:mm:ss");
        var globalRecent = await db.IgMessages
            .CountAsync(m => m.Direction == IgConst.DirOut && m.CreatedAt.CompareTo(burstSince) >= 0, ct);
        var perPostRecent = inc.MediaId.Length == 0
            ? 0
            : await db.IgMessages.CountAsync(
                m => m.Direction == IgConst.DirOut && m.MediaId == inc.MediaId
                     && m.CreatedAt.CompareTo(burstSince) >= 0, ct);

        var burst = InstagramContract.BurstBlockReason(perPostRecent, globalRecent);
        if (burst.Length > 0)
        {
            Escalate(conv, burst);
            await db.SaveChangesAsync(ct);
            await NotifyAdminsAsync(db, telegram, meta, $"🛑 Instagram: {burst}. Operator tekshirsin.", ct);
            return;
        }

        // ── 4.7) FAQ TUGMASI (ice breaker postback) — qoidadan ham, AI'dan ham OLDIN ──
        //
        // Tugma bosilganda javob SAQLANGAN matndan yuboriladi: AI umuman chaqirilmaydi (tayyor
        // savolga tayyor javob — kalit so'z qoidasi bilan bir xil mulohaza, faqat mijoz
        // yozmasdan bosadi). Payload mos FAQ'ga kelmasa (tugma o'chirilgan/o'chirib yuborilgan
        // yoki begona payload) — hodisa ODDIY DM kabi qoida→AI oqimiga tushadi, jimgina
        // tashlanmaydi: mijoz baribir savol berdi.
        IgIceBreaker? faq = null;
        if (inc.Kind == InstagramEventParser.KindPostback)
        {
            var faqId = InstagramContract.FaqIdFromPayload(inc.PostbackPayload);
            if (faqId.Length > 0)
                faq = await db.IgIceBreakers.FirstOrDefaultAsync(b => b.Id == faqId && b.IsActive, ct);
        }

        // ── 5) KALIT SO'Z QOIDASI (AI'dan oldin: tez, arzon, aniq) ──
        var reply = "";
        var actor = "";
        var isAi = false;
        IgAgentOutput? output = null;
        IgAutoRule? matched = null;

        if (faq is not null)
        {
            faq.TapCount += 1;
            reply = faq.Answer;
            actor = IgConst.ActorFaq;
        }
        else
        {
            var rules = await db.IgAutoRules.Where(r => r.IsActive).OrderBy(r => r.Order).ToListAsync(ct);
            matched = rules.FirstOrDefault(r => InstagramContract.RuleMatches(r, channel, inboundText));
            if (matched is not null)
            {
                matched.MatchCount += 1;
                reply = matched.ReplyText;
                actor = matched.Title.Length > 0 ? $"Qoida: {matched.Title}" : IgConst.ActorRule;
            }
        }

        // ── 6) AI (FAQ ishlamagan bo'lsa VA qoida topilmasa yoki qoida AI'ni to'xtatmasa) ──
        if (faq is null && (matched is null || !matched.StopAi))
        {
            var caption = "";
            if (channel == IgConst.ChannelComment && inc.MediaId.Length > 0)
            {
                try
                {
                    var media = await api.GetMediaAsync(inc.MediaId, account.AccessToken, ct);
                    if (media.Ok) caption = media.Caption;
                }
                catch (Exception ex) { logger.LogWarning(ex, "Instagram post matnini olib bo'lmadi"); }
            }

            var history = await LoadHistoryAsync(db, conv.Id, channel, inc.MediaId, ct);

            // ⚠️ AI'ga KONTEKSTLI matn beriladi (`storedText`): story'ga yozilgan "Salom!" javobi
            // kontekstsiz umuman tushunarsiz bo'lardi. Tarix ham bazadan shu ko'rinishda keladi.
            var (aiOk, aiOut, aiErr) = await InstagramAgentService.AskAsync(
                db, config, channel, conv.Username, caption, storedText, history, ct);

            if (aiOk && aiOut is not null)
            {
                output = aiOut;
                reply = aiOut.Reply;
                isAi = true;
                actor = IgConst.ActorAi;
            }
            else if (matched is null)
            {
                // ⚠️ AI ishlamadi va tayyor qoida ham yo'q — JONLI JAVOB YUBORILMAYDI.
                Escalate(conv, InstagramContract.Trim($"AI javob bera olmadi: {aiErr}", 200));
                await db.SaveChangesAsync(ct);
                await NotifyAdminsAsync(db, telegram, meta,
                    $"🤖 Instagram: AI javob bera olmadi. Sabab: {aiErr}\n"
                    + $"👤 {InstagramContract.ProfileRef(conv.Username)}", ct);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(reply)) { await db.SaveChangesAsync(ct); return; }
        // Belgi bo'yicha MO'LJAL (AI javobi juda uzun bo'lsa qisqartiriladi), so'ng BAYT
        // bo'yicha haqiqiy chegara — Meta aynan baytni sanaydi (`IgConst.MaxReplyBytes`).
        reply = InstagramContract.TrimBytes(
            InstagramContract.Trim(reply, IgConst.MaxReplyLength), IgConst.MaxReplyBytes);

        // ── 7) TABIIY KECHIKISH (bir zumda kelgan javob spamga o'xshaydi) ──
        //
        // ⚠️ Hodisa NAVBATDA kutgan vaqt HISOBGA OLINADI. Ilgari kechikish har hodisaga to'liq
        // qo'shilardi va u ketma-ket siklda bajarilgani uchun izohlar to'lqinida navbat sun'iy
        // ravishda cho'zilib ketardi (10 ta hodisa × 5 soniya = bitta tsiklga 50+ soniya).
        // Endi "javob mijoz yozganidan keyin kamida N soniya o'tib ketsin" degan MAQSAD saqlanadi,
        // lekin kutish allaqachon o'tgan bo'lsa qo'shimcha pauza qilinmaydi.
        var wanted = Math.Clamp(meta.InstagramReplyDelaySeconds, 0, IgConst.MaxReplyDelaySeconds);
        var waited = InstagramContract.TryIso(receivedAtIso, out var received)
            ? (now - received).TotalSeconds
            : 0;
        var delay = (int)Math.Round(Math.Clamp(wanted - Math.Max(0, waited), 0, wanted));
        if (delay > 0)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
            catch (TaskCanceledException) { }
        }

        // ── 8) YUBORISH ──
        var sendError = "";
        var alert = "";
        if (channel == IgConst.ChannelComment)
        {
            var res = await api.ReplyToCommentAsync(inc.CommentId, reply, account.AccessToken, ct);
            sendError = res.Ok ? "" : res.Error;
            AddOutbound(db, conv, IgConst.ChannelComment, reply, actor, isAi, output, inc.CommentId, inc.MediaId, nowIso, sendError);

            // Yopiq javob (private reply) — yoqilgan bo'lsa va shu izohga HALI yuborilmagan bo'lsa.
            if (res.Ok && meta.InstagramPrivateReplyEnabled && inc.CommentId.Length > 0)
            {
                var already = await db.IgMessages.AnyAsync(
                    m => m.CommentId == inc.CommentId && m.Channel == IgConst.ChannelPrivateReply, ct);
                if (!already)
                {
                    var pr = await api.SendPrivateReplyAsync(inc.CommentId, reply, account.AccessToken, ct);
                    AddOutbound(db, conv, IgConst.ChannelPrivateReply, reply, actor, isAi, output,
                        inc.CommentId, inc.MediaId, AppClock.Iso(), pr.Ok ? "" : pr.Error);
                }
            }
        }
        else
        {
            // ⚠️ 24 SOATLIK OYNA — yuborishdan OLDIN (NUR'da bu tekshiruv umuman yo'q edi).
            if (!InstagramContract.DmWindowOpen(conv.LastInboundAt, now))
            {
                Escalate(conv, "24 soatlik javob oynasi yopiq — DM yuborib bo'lmadi, operator boshqa yo'l bilan bog'lansin");
                await db.SaveChangesAsync(ct);
                await NotifyAdminsAsync(db, telegram, meta,
                    $"⏰ Instagram: 24 soatlik oyna yopiq — javob yuborilmadi.\n"
                    + $"👤 {InstagramContract.ProfileRef(conv.Username)}", ct);
                return;
            }

            var res = await api.SendDmAsync(account.IgUserId, conv.IgUserId, reply, account.AccessToken, ct);
            sendError = res.Ok ? "" : res.Error;
            AddOutbound(db, conv, IgConst.ChannelDm, reply, actor, isAi, output, "", "", nowIso, sendError);
        }

        if (sendError.Length > 0)
        {
            Escalate(conv, InstagramContract.Trim($"Javob yuborilmadi: {sendError}", 200));
            alert = $"❌ Instagram: mijozga javob yuborilmadi. {sendError}\n"
                    + $"👤 {InstagramContract.ProfileRef(conv.Username)}";
        }
        else
        {
            conv.LastOutboundAt = nowIso;
            conv.LastMessageText = InstagramContract.Trim(reply, 300);
        }

        // ── 9) LID (faqat qiziqish belgisi bo'lsa — salom-alik CRM'ni ifloslantirmaydi) ──
        // ⚠️ `cardLeadId` / `cardIsNewLead` / `firstLink` §3.1 da e'lon qilingan: telefon yo'li
        // ham, AI yo'li ham AYNI kartani boshqaradi.
        if (output is not null)
        {
            conv.Language = output.Language;
            conv.Intent = output.Intent;
            conv.LeadScore = Math.Max(conv.LeadScore, InstagramContract.ClampScore(output.LeadScore));

            if (phoneLeadId.Length > 0)
            {
                // §3.1 lidni ALLAQACHON yozdi. Qayta `UpsertAsync` qilinmaydi: u `RepeatCount++`
                // qiladi va hodisa yozadi — bitta xabar uchun takror ×2 bo'lib ko'rinardi.
                // O'rniga AI bilgan narsa (ism, qiziqish, xulosa) mavjud lidga QO'SHILADI.
                try
                {
                    await InstagramLeadBridge.EnrichAsync(db, phoneLeadId, output, phoneLeadGuessedName, ct);
                    // Karta §9.1 da JOYIDA yangilanadi (yangi xabar yuborilmaydi — u ketib bo'lgan).
                    cardLeadId = phoneLeadId;
                    firstLink = false;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Instagram: lidni AI xulosasi bilan boyitib bo'lmadi ({Lead})", phoneLeadId);
                }
            }
            else if (InstagramContract.ShouldCreateLead(output))
            {
                try
                {
                    // ⚠️ Suhbat lidga BIRINCHI marta bog'lanyaptimi — Upsert'dan OLDIN o'qiladi
                    // (u `conv.LeadId` ni to'ldirib qo'yadi). Telegram kartasi shu bilan hal
                    // qilinadi, pastdagi §9.1 ga qarang.
                    firstLink = string.IsNullOrWhiteSpace(conv.LeadId);

                    var source = string.IsNullOrWhiteSpace(meta.InstagramLeadSource) ? "Instagram" : meta.InstagramLeadSource;
                    var (leadId, isNew) = await InstagramLeadBridge.UpsertAsync(db, conv, output, source, ct);
                    cardLeadId = leadId;
                    cardIsNewLead = isNew;
                    logger.LogInformation("Instagram lid {State} ({LeadId})", isNew ? "yaratildi" : "yangilandi", leadId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Instagram suhbatidan lid yaratib bo'lmadi ({Conv})", conv.Id);
                    Escalate(conv, "Lid yaratib bo'lmadi — operator qo'lda kiritsin");
                }
            }

            if (output.EscalateToHuman)
                Escalate(conv, "Mijoz operator bilan gaplashmoqchi (yoki AI javobni topa olmadi)");
            else if (InstagramContract.IsHot(output))
                Escalate(conv, $"Qaynoq lid — qiziqish bali {InstagramContract.ClampScore(output.LeadScore)}");
        }

        await db.SaveChangesAsync(ct);

        // ── 9.1) LID KARTASI — Telegram guruhiga YANGI LID sifatida ──
        //
        // 🔴 Ilgari bu yerda FAQAT `SyncCardAsync` turardi, u esa o'z qoidasi bo'yicha kartasi
        // YO'Q lidga hech narsa yubormaydi — ya'ni Instagram'dan kelgan lid guruhga UMUMAN
        // tushmasdi. Qolgan barcha kanallar (lid formasi, daraja testi, reklama lidi, qo'lda
        // kiritish) `NotifyNewLeadAsync` ni chaqiradi; Instagram YAGONA istisno bo'lib qolgan
        // va nosozlik jimgina edi: lid CRM'da bor, guruhda esa yo'q.
        //
        // ⚠️ Karta suhbat lidga BIRINCHI marta bog'langanda yuboriladi (`firstLink`), har
        // xabarda EMAS. Sabab: `ShouldCreateLead` qaynoq suhbatda HAR xabarda rost bo'ladi
        // (telefon berilgan), ya'ni har «rahmat» ga ham guruhga signal ketardi va karta
        // shovqinga aylanardi. Keyingi xabarlar kartani JIMGINA yangilaydi.
        //
        // ⚠️ `isNewLead` — lid YOZUVI yangimi degani (kartaning yetkazilishini hal qiladi):
        // yangi lid → to'liq karta; mavjud lid (masalan formadan kelgan odam endi Instagram'da
        // yozdi) → mavjud karta tahrirlanadi va ustiga bitta qatorli signal ketadi.
        //
        // ⚠️ Chaqiruv SaveChanges'dan KEYIN: karta bazadagi YOZILGAN holatdan quriladi.
        // ⚠️ Xatosi JIM yutiladi (`LeadNotifier` siyosati) — xabarnoma javobni buzmaydi.
        if (cardLeadId.Length > 0)
        {
            if (firstLink && meta.InstagramNotifyTelegram)
            {
                var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == cardLeadId, ct);
                if (lead is not null)
                    await LeadNotifier.NotifyNewLeadAsync(
                        db, telegram, lead, isNewLead: cardIsNewLead,
                        createdBy: InstagramLeadBridge.ActorName, ct: ct, logger: logger);
            }
            else
            {
                await LeadNotifier.SyncCardAsync(db, telegram, cardLeadId, ct, logger);
            }
        }

        // ── 10) TELEGRAM SIGNALI (xatosi JIM yutiladi) ──
        if (alert.Length == 0 && output is not null && (output.EscalateToHuman || InstagramContract.IsHot(output)))
            alert = BuildHotAlert(conv, output);
        if (alert.Length > 0)
            await NotifyAdminsAsync(db, telegram, meta, alert, ct);
    }

    /* ═════════════════════════ AI uchun suhbat tarixi ═════════════════════════ */

    /// <summary>
    /// AI promptiga tushadigan tarix. <b>Kanalga QARAB</b> ikki xil to'planadi.
    ///
    /// <para><b>DM</b> — butun suhbatning oxirgi <see cref="IgConst.DmHistoryLimit"/> xabari
    /// (avvalgidek): shaxsiy yozishma bitta uzluksiz muloqot va oldingi gap keyingisining
    /// ma'nosini belgilaydi.</para>
    ///
    /// <para>🔴 <b>IZOH — FAQAT O'SHA POST OSTIDAGI izohlar.</b> Ilgari izohga ham butun suhbat
    /// tarixi berilardi va bu ikki xil zarar keltirardi:</para>
    /// <list type="number">
    ///   <item><b>Javob izohga qaramay yozilardi.</b> Odam A postiga «Narxi qancha?» deb yozgan,
    ///     bir hafta oldin B postiga boshqa savol bergan yoki DM'da butunlay boshqa mavzuda
    ///     yozishgan bo'lsa — model o'sha eski suhbatni davom ettirib, ostidagi izohga
    ///     tegishsiz javob berardi. Izoh esa DM emas: u <b>yakka savol</b>, konteksti — post
    ///     matni va o'sha post ostidagi yozishma.</item>
    ///   <item><b>SHAXSIY yozishma OMMAGA chiqardi.</b> Javob ochiq izoh sifatida chop etiladi;
    ///     promptdagi DM tarixi (telefon raqami, to'lov haqidagi gap) modelning javobiga
    ///     kirib qolsa uni post ostida hamma o'qirdi. Chegara <b>tuzilma darajasida</b>
    ///     qo'yilgan: DM qatorlari so'rovga UMUMAN olinmaydi.</item>
    /// </list>
    ///
    /// <para>⚠️ Filtr <see cref="IgMessage.MediaId"/> bo'yicha — chiquvchi izoh javoblarida ham
    /// shu ustun to'ldiriladi (<c>AddOutbound</c>), ya'ni "biz nima deb javob berdik" tarixda
    /// qoladi va bir xil savolga ikki xil javob yozilmaydi.</para>
    ///
    /// <para>⚠️ <c>MediaId</c> bo'sh bo'lsa (eski yozuv yoki Meta post id bermagan holat) post
    /// bo'yicha ajratib bo'lmaydi — u holda hech bo'lmaganda <b>KANAL</b> bo'yicha filtrlanadi,
    /// ya'ni DM baribir promptga tushmaydi. Bu ATAYIN: bo'sh natija zarar qilmaydi (izoh
    /// tarixsiz ham to'liq javob beriladi — post matni va bilim bazasi joyida), aralashib
    /// ketgan tarix esa yuqoridagi ikkala zararni ham qaytarardi.</para>
    /// </summary>
    public static async Task<List<IgMessage>> LoadHistoryAsync(
        IAppDbContext db, string convId, string channel, string mediaId, CancellationToken ct)
    {
        var q = db.IgMessages.AsNoTracking().Where(m => m.ConversationId == convId);

        if (channel == IgConst.ChannelComment)
        {
            // Ochiq kanal: DM qatorlari OLINMAYDI (yopiq javob — o'sha izohning davomi, qoladi).
            q = q.Where(m => m.Channel == IgConst.ChannelComment || m.Channel == IgConst.ChannelPrivateReply);
            if (!string.IsNullOrWhiteSpace(mediaId)) q = q.Where(m => m.MediaId == mediaId);

            var thread = await q
                .OrderByDescending(m => m.CreatedAt)
                .Take(IgConst.CommentHistoryLimit)
                .ToListAsync(ct);
            thread.Reverse();
            return thread;
        }

        var history = await q
            .OrderByDescending(m => m.CreatedAt)
            .Take(IgConst.DmHistoryLimit)
            .ToListAsync(ct);
        history.Reverse();
        return history;
    }

    /* ═════════════════════════ Hodisa darajasidagi dedup ═════════════════════════ */

    /// <summary>
    /// Shu AYNAN hodisa (izoh yoki DM) allaqachon qayta ishlanganmi.
    ///
    /// <para><b>Nega navbat kaliti YETMAYDI:</b> <see cref="IgWebhookEvent.EventKey"/> bitta POST
    /// BODY'ga tegishli va u ichidagi hodisa kalitlarini <c>|</c> bilan birlashtiradi. Meta bir
    /// necha hodisani bitta bodyda ham, alohida ham yuborishi mumkin — A va B birga kelib kalit
    /// <c>A|B</c> bo'lsa, keyin faqat A qayta yuborilganda kalit <c>A</c> bo'ladi va unikal indeks
    /// buni TAKROR deb bilmaydi. Natijada A ikkinchi marta qayta ishlanib mijozga IKKI javob
    /// ketardi (Meta muvaffaqiyatsiz yetkazishni 36 soat qayta yuboradi — bu nazariy emas,
    /// kutiladigan holat).</para>
    ///
    /// <para>Shuning uchun haqiqat manbai — <b>yozilgan xabarlar</b>: Meta bergan barqaror
    /// identifikator (<c>mid</c> / <c>comment_id</c>) bilan qator bor bo'lsa, hodisa qayta
    /// ishlangan. Izohda <c>Direction == in</c> sharti SHART: chiquvchi javob qatorida ham
    /// <c>CommentId</c> saqlanadi (biz JAVOB BERGAN izohning id'si), ya'ni filtrsiz o'z javobimiz
    /// kiruvchi izohni "takror" deb ko'rsatib qo'yardi.</para>
    ///
    /// <para>⚠️ FAIL-OPEN (IG-SPEC §5.5): tekshiruvning o'zi yiqilsa hodisa BARIBIR qayta
    /// ishlanadi — bitta buzilgan so'rov butun oqimni to'xtatib qo'ymasin. Halqadan himoyaning
    /// qolgan qavatlari (o'zimizni tanish, kunlik chegara) joyida turadi.</para>
    /// </summary>
    private async Task<bool> AlreadyHandledAsync(IAppDbContext db, IgIncomingEvent inc, CancellationToken ct)
    {
        try
        {
            if (inc.IgMessageId.Length > 0)
                return await db.IgMessages.AnyAsync(m => m.IgMessageId == inc.IgMessageId, ct);

            if (inc.CommentId.Length > 0)
                return await db.IgMessages.AnyAsync(
                    m => m.CommentId == inc.CommentId && m.Direction == IgConst.DirIn, ct);

            // Meta identifikator bermagan holat: kalit hash'dan qurilgan va navbatdagi unikal
            // indeks aynan shu payloadning takrorini allaqachon ushlaydi.
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Instagram: takrorlik tekshiruvi bajarilmadi — hodisa qayta ishlanaveradi");
            return false;
        }
    }

    /* ═════════════════════════ Echo → operator pauzasi ═════════════════════════ */

    /// <summary>
    /// Akkauntimizdan chiqqan xabar webhook'ga qaytdi. Ikki manba bor:
    /// <list type="bullet">
    ///   <item><b>Botning o'z javobi</b> — e'tibor berilmaydi (aks holda bot o'ziga javob yozib
    ///     cheksiz halqaga tushardi);</item>
    ///   <item><b>Operator telefondan qo'lda yozgani</b> — bot o'sha suhbatda vaqtincha jim
    ///     bo'ladi, aks holda mijoz bir vaqtda "ikki odam" bilan gaplashardi.</item>
    /// </list>
    /// Ajratish BAZA orqali: shu matnli chiquvchi xabar oxirgi daqiqalarda yozilgan bo'lsa — bizniki.
    /// (NUR'dagi xotiradagi "barmoq izi" restartda yo'qolardi.)
    /// </summary>
    private async Task HandleEchoAsync(
        IAppDbContext db, IgConversation? conv, IgIncomingEvent inc, DateTime now, string nowIso, CancellationToken ct)
    {
        if (conv is null) return;   // bizdan boshlangan suhbat bo'lishi mumkin emas (mijoz avval yozadi)

        var since = now.AddMinutes(-IgConst.EchoOwnReplyMinutes).ToString("yyyy-MM-ddTHH:mm:ss");
        var ours = await db.IgMessages.AnyAsync(
            m => m.ConversationId == conv.Id
                 && m.Direction == IgConst.DirOut
                 && m.Text == inc.Text
                 && m.CreatedAt.CompareTo(since) >= 0, ct);
        if (ours) return;

        conv.OperatorPausedUntil = now.AddMinutes(IgConst.OperatorPauseMinutes).ToString("yyyy-MM-ddTHH:mm:ss");
        conv.LastOutboundAt = nowIso;
        conv.MessageCount += 1;
        if (!string.IsNullOrWhiteSpace(inc.Text))
            conv.LastMessageText = InstagramContract.Trim(inc.Text, 300);

        var manual = new IgMessage
        {
            ConversationId = conv.Id,
            Direction = IgConst.DirOut,
            Channel = IgConst.ChannelDm,
            Text = inc.Text,
            IgMessageId = inc.IgMessageId,
            ActorName = IgConst.ActorOperatorIg,
            IsAi = false,
            CreatedAt = nowIso,
        };

        // E6.6 — JAVOB SIFATI JURNALI: operator Instagram ilovasidan yozgan javob AI'ning
        // oxirgi taklifi O'RNIGA ketgan bo'lishi mumkin. Farq shu yerda biriktiriladi
        // (`AttachSuggestionAsync` saqlamaydi — quyidagi `SaveChangesAsync` bilan birga ketadi).
        // ⚠️ `Add` dan OLDIN: so'rov bazaga ketadi va hali yozilmagan qatorni ko'rmaydi.
        await IgQualityLog.AttachSuggestionAsync(db, conv.Id, manual, now, ct);

        db.IgMessages.Add(manual);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Instagram: operator qo'lda javob berdi — bot {Min} daqiqaga pauzada (@{User})",
            IgConst.OperatorPauseMinutes, conv.Username);
    }

    /* ═════════════════════════ E6.4 — mijoz xabarni o'chirdi ═════════════════════════ */

    /// <summary>Matni o'chirilgan xabar o'rnida turadigan belgi (ro'yxatda bo'shliq qolmasin).</summary>
    private const string DeletedText = "[o'chirilgan]";

    /// <summary>
    /// Mijoz Instagram'da xabarini o'chirdi (<c>message.is_deleted</c>).
    ///
    /// <para>🔴 <b>Mazmun HAQIQATAN o'chiriladi</b> — faqat UI'dan yashirish YETARLI EMAS
    /// (Meta Platform Terms talabi: foydalanuvchi o'chirgan mazmunni saqlab qololmaymiz).
    /// Yozuvning O'ZI qoladi: suhbat lentasida "shu yerda xabar bor edi" ko'rinib tursin,
    /// aks holda operator uchun tarix uzilib qolardi.</para>
    ///
    /// <para>Yozuv topilmasa jimgina qaytadi: o'chirish hodisasi biz yozib ulgurmagan xabarga
    /// tegishli bo'lishi mumkin (modul o'chiq bo'lgan davr) — bu xato emas.</para>
    /// </summary>
    private async Task HandleDeletedAsync(IAppDbContext db, IgIncomingEvent inc, CancellationToken ct)
    {
        if (inc.IgMessageId.Length == 0) return;

        var rows = await db.IgMessages.Where(m => m.IgMessageId == inc.IgMessageId).ToListAsync(ct);
        if (rows.Count == 0) return;

        var oldTexts = rows.Select(r => r.Text).Where(t => t.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var m in rows) m.Text = DeletedText;

        // Suhbatdagi DENORMALIZATSIYA ham tozalanadi — aks holda o'chirilgan matn inbox
        // ro'yxatida "oxirgi xabar" bo'lib turaverardi.
        var convIds = rows.Select(r => r.ConversationId).Distinct().ToList();
        var convs = await db.IgConversations.Where(c => convIds.Contains(c.Id)).ToListAsync(ct);
        foreach (var c in convs)
            if (oldTexts.Contains(c.LastMessageText)) c.LastMessageText = DeletedText;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Instagram: mijoz xabarni o'chirdi — mazmun tozalandi ({Mid})", inc.IgMessageId);
    }

    /* ═════════════════════════ E6.7 — Meta siyosati ogohlantirishi ═════════════════════════ */

    /// <summary>
    /// <c>messaging_policy_enforcement</c> — Meta cheklov qo'yishidan OLDINGI ogohlantirishi.
    /// Modulning eng yuqori qiymatli signali, shuning uchun ikki narsa DARHOL bajariladi:
    /// <list type="number">
    ///   <item><b>Avtomatika pauza qilinadi</b> — <c>InstagramAutoReplyComments</c> va
    ///     <c>InstagramAutoReplyDm</c> o'chiriladi;</item>
    ///   <item><b>Telegram alert</b> — admin sababni ko'rib, qo'lda qayta yoqadi.</item>
    /// </list>
    ///
    /// <para>⚠️ <c>InstagramEnabled</c> ATAYIN O'CHIRILMAYDI. Sabab ikkita: (1) u MASTER darvoza —
    /// o'chirilsa <see cref="NotifyAdminsAsync"/> ham jim bo'lardi va ogohlantirish hech kimga
    /// yetmasdi; (2) u bilan birga navbat qayta ishlash ham to'xtardi, ya'ni kelayotgan
    /// xabarlar tarixga yozilmay qolardi. Pauza faqat AVTOMATIK JAVOBGA tegadi — operator
    /// qo'lda javob bera oladi.</para>
    ///
    /// <para>⚠️ Qayta yoqish ATAYIN QO'LDA: "N soatdan keyin o'zi yonsin" varianti sababni
    /// tekshirmasdan o'sha xatoni takrorlashga olib kelardi.</para>
    /// </summary>
    private async Task HandlePolicyAsync(
        IAppDbContext db, TelegramService telegram, CenterMeta? meta, IgIncomingEvent inc, CancellationToken ct)
    {
        var action = inc.PolicyAction.Length > 0 ? inc.PolicyAction : "warning";
        var reason = InstagramContract.Trim(inc.PolicyReason, 300);
        logger.LogWarning(
            "[instagram] META SIYOSATI OGOHLANTIRISHI — amal: {Action}, sabab: {Reason}", action, reason);

        var paused = false;
        if (meta is not null && (meta.InstagramAutoReplyComments || meta.InstagramAutoReplyDm))
        {
            meta.InstagramAutoReplyComments = false;
            meta.InstagramAutoReplyDm = false;
            paused = true;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("[instagram] avtomatik javoblar (izoh va DM) siyosat ogohlantirishi tufayli o'chirildi");
        }

        var lines = new List<string>
        {
            "🚨 Instagram: META SIYOSATI OGOHLANTIRISHI",
            $"Amal: {action}",
        };
        if (reason.Length > 0) lines.Add($"Sabab: {reason}");
        lines.Add(paused
            ? "⛔ Avtomatik javoblar (izoh va DM) VAQTINCHA O'CHIRILDI."
            : "ℹ️ Avtomatik javoblar allaqachon o'chiq edi.");
        lines.Add("Sababni tekshirmasdan qayta yoqmang — keyingi qadam akkauntni cheklash bo'lishi mumkin.");

        await NotifyAdminsAsync(db, telegram, meta, string.Join("\n", lines), ct);
    }

    /* ═════════════════════════ E3 — reklama atributsiyasi ═════════════════════════ */

    /// <summary>Reklama iyerarxiyasi bazada BORMI — tekshiruv natijasi shuncha daqiqa keshlanadi.</summary>
    private const int AdsPresenceCacheMinutes = 5;

    /// <summary>Oxirgi tekshiruv vaqti (ISO) va natijasi. <c>InstagramPipeline</c> singleton va
    /// navbat KETMA-KET qayta ishlanadi, shuning uchun qulf kerak emas (eng yomon holatda
    /// tekshiruv bir marta ortiqcha bajariladi).</summary>
    private string _adsCheckedAt = "";
    private bool _adsExist;

    /// <summary>
    /// Izoh QAYSI REKLAMA ostida yozilganini TAXMIN qiladi (E3).
    ///
    /// <para>🔴 <b>TAXMINIY:</b> Instagram Login yo'lidagi <c>comments</c> webhook'ida
    /// <c>ad_id</c> umuman yo'q, shuning uchun bog'lanish <c>media.id</c> ni
    /// <c>IgAdEntity.CreativeStoryId</c> bilan solishtirish orqali TIKLANADI. Boostlangan
    /// organik postda ishlaydi; <b>dark post</b> (chop etilmagan reklama) va <b>dinamik
    /// katalog</b> reklamasida ishlamaydi. Bo'sh natija "organik" degani EMAS — "aniqlanmadi".</para>
    ///
    /// <para>⚠️ Bu QO'SHIMCHA baza so'rovi, ya'ni yordamchi vazifa. Yiqilsa asosiy vazifa
    /// (mijozga javob berish va xabarni yozib qo'yish) BARIBIR bajariladi — modulning
    /// "har bosqich alohida try/catch" qoidasi.</para>
    ///
    /// <para>⚠️ MODUL DARVOZASI: reklama statistikasi ulanmagan markazda <c>IgAdEntities</c>
    /// bo'sh bo'ladi. Har izohda bekorga so'rov ketmasin — mavjudlik tekshiruvi
    /// <see cref="AdsPresenceCacheMinutes"/> daqiqaga keshlanadi.</para>
    /// </summary>
    private async Task<IgAdAttribution.AdMatch> TryAttributeAdAsync(
        IAppDbContext db, IgIncomingEvent inc, CancellationToken ct)
    {
        // ── 0) ANIQ ATRIBUTSIYA — Meta bergan `referral.ad_id` (DM) ──
        //
        // 🔴 Bu TAXMIN EMAS. "Click to Instagram Direct" reklamasidan kelgan DM'da Meta
        // `message.referral.ad_id` ni O'ZI beradi — pastdagi media↔creative solishtiruvi kabi
        // tiklash kerak emas. Ilgari bu maydon parserda umuman o'qilmasdi va TO'LIQ JIMGINA
        // yo'qolardi: reklamadan kelgan suhbat organik bo'lib qolar, ROI hisoboti esa
        // reklamani "pul keltirmagan" deb ko'rsatardi.
        //
        // ⚠️ `referral` faqat suhbatni BOSHLAGAN xabarda keladi — shuning uchun natija
        // suhbat darajasida saqlanadi (chaqiruvchi `conv.AdId` bo'sh bo'lsagina yozadi).
        var exactAdId = (inc.AdId ?? "").Trim();
        if (exactAdId.Length > 0)
        {
            var campaign = await ResolveCampaignAsync(db, exactAdId, ct);
            logger.LogInformation(
                "Instagram: reklama ANIQ aniqlandi (referral → e'lon {Ad}, kampaniya {Campaign})",
                exactAdId, campaign.Length > 0 ? campaign : "—");
            return new IgAdAttribution.AdMatch(exactAdId, campaign);
        }

        var media = (inc.MediaId ?? "").Trim();
        if (media.Length == 0) return IgAdAttribution.AdMatch.None;

        try
        {
            if (!await AdsPresentAsync(db, ct)) return IgAdAttribution.AdMatch.None;

            // ⚠️ SQL faqat NOMZODLARNI toraytiradi, QARORNI sof funksiya qabul qiladi:
            // `EndsWith` da `_` ba'zi provayderlarda LIKE joker belgisi bo'lib qoladi, ya'ni
            // ro'yxatga ortiqcha qator tushishi mumkin — `IgAdAttribution.Matches` har birini
            // qayta tekshiradi (ortiqcha moslik kirib ketmaydi).
            var suffix = "_" + IgAdAttribution.MediaPart(media);
            var raw = await db.IgAdEntities.AsNoTracking()
                .Where(a => a.CreativeStoryId != ""
                            && (a.CreativeStoryId == media || a.CreativeStoryId.EndsWith(suffix)))
                .OrderBy(a => a.ExternalId)
                .Take(IgAdAttribution.MaxCandidates)
                .Select(a => new { a.ExternalId, a.Level, a.ParentId, a.CreativeStoryId })
                .ToListAsync(ct);
            if (raw.Count == 0) return IgAdAttribution.AdMatch.None;

            var candidates = raw
                .Select(a => new IgAdAttribution.AdRow(a.ExternalId, a.Level, a.ParentId, a.CreativeStoryId))
                .ToList();

            var found = IgAdAttribution.FindAd(media, candidates);
            if (found is null) return IgAdAttribution.AdMatch.None;

            // Kampaniya — ota tugun orqali (`ad → adset → campaign`). Ota topilmasa e'lon id'si
            // baribir saqlanadi: yarim ma'lumot hech qanaqasidan yaxshiroq.
            var parents = new List<IgAdAttribution.AdRow>();
            var parentId = (found.Value.ParentId ?? "").Trim();
            if (parentId.Length > 0)
            {
                var p = await db.IgAdEntities.AsNoTracking()
                    .Where(a => a.ExternalId == parentId)
                    .Select(a => new { a.ExternalId, a.Level, a.ParentId, a.CreativeStoryId })
                    .FirstOrDefaultAsync(ct);
                if (p is not null)
                    parents.Add(new IgAdAttribution.AdRow(p.ExternalId, p.Level, p.ParentId, p.CreativeStoryId));
            }

            var match = new IgAdAttribution.AdMatch(
                found.Value.ExternalId, IgAdAttribution.CampaignOf(found.Value, parents));

            logger.LogInformation(
                "Instagram: izoh reklama ostida deb TAXMIN qilindi (media {Media} → e'lon {Ad})",
                media, match.AdId);
            return match;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Instagram: reklama atributsiyasi bajarilmadi ({Media}) — izoh organik deb qoladi", media);
            return IgAdAttribution.AdMatch.None;
        }
    }

    /// <summary>
    /// Berilgan e'lon id'si uchun KAMPANIYA id'sini topadi (`ad → adset → campaign`).
    ///
    /// <para>E'lon bizning sinxronlangan jadvalimizda bo'lmasligi mumkin (reklama statistikasi
    /// moduli o'chiq yoki hali sinxronlanmagan) — u holda BO'SH satr qaytadi va e'lon id'sining
    /// o'zi baribir saqlanadi: yarim ma'lumot hech qanaqasidan yaxshiroq.</para>
    ///
    /// <para>⚠️ Xato YUTILADI: atributsiya — yordamchi ma'lumot, u tufayli mijozning xabari
    /// qayta ishlanmay qolmasligi kerak.</para>
    /// </summary>
    private async Task<string> ResolveCampaignAsync(IAppDbContext db, string adId, CancellationToken ct)
    {
        try
        {
            var node = await db.IgAdEntities.AsNoTracking()
                .Where(a => a.ExternalId == adId)
                .Select(a => new { a.ExternalId, a.Level, a.ParentId, a.CreativeStoryId })
                .FirstOrDefaultAsync(ct);
            if (node is null) return "";

            var self = new IgAdAttribution.AdRow(node.ExternalId, node.Level, node.ParentId, node.CreativeStoryId);

            var parents = new List<IgAdAttribution.AdRow>();
            var parentId = (node.ParentId ?? "").Trim();
            if (parentId.Length > 0)
            {
                var p = await db.IgAdEntities.AsNoTracking()
                    .Where(a => a.ExternalId == parentId)
                    .Select(a => new { a.ExternalId, a.Level, a.ParentId, a.CreativeStoryId })
                    .FirstOrDefaultAsync(ct);
                if (p is not null)
                    parents.Add(new IgAdAttribution.AdRow(p.ExternalId, p.Level, p.ParentId, p.CreativeStoryId));
            }

            return IgAdAttribution.CampaignOf(self, parents);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Instagram: kampaniya id topilmadi ({Ad}) — e'lon id'si o'zi saqlanadi", adId);
            return "";
        }
    }

    /// <summary>Reklama iyerarxiyasi sinxronlanganmi (keshlangan tekshiruv).</summary>
    private async Task<bool> AdsPresentAsync(IAppDbContext db, CancellationToken ct)
    {
        if (_adsCheckedAt.Length > 0
            && InstagramContract.TryIso(_adsCheckedAt, out var checkedAt)
            && (AppClock.Now - checkedAt).TotalMinutes < AdsPresenceCacheMinutes)
            return _adsExist;

        _adsExist = await db.IgAdEntities.AsNoTracking().AnyAsync(a => a.CreativeStoryId != "", ct);
        _adsCheckedAt = AppClock.Iso();
        return _adsExist;
    }

    /* ═════════════════════════ Yordamchilar ═════════════════════════ */

    /// <param name="mediaId">Javob QAYSI POST ostiga yozilgani. ⚠️ Bo'sh qoldirilmaydi:
    /// halqa avtomat o'chirgichi "shu post ostida 10 daqiqada nechta javob" ni AYNAN shu
    /// ustundan sanaydi (`InstagramContract.BurstBlockReason`).</param>
    private static void AddOutbound(
        IAppDbContext db, IgConversation conv, string channel, string text, string actor, bool isAi,
        IgAgentOutput? output, string commentId, string mediaId, string nowIso, string error)
    {
        db.IgMessages.Add(new IgMessage
        {
            ConversationId = conv.Id,
            Direction = IgConst.DirOut,
            Channel = channel,
            Text = text,
            CommentId = commentId,
            MediaId = mediaId,
            ActorName = actor.Length > 0 ? actor : IgConst.ActorAi,
            IsAi = isAi,
            AiIntent = output?.Intent ?? "",
            AiScore = output is null ? 0 : InstagramContract.ClampScore(output.LeadScore),
            Error = error,
            CreatedAt = nowIso,
        });
    }

    /// <summary>
    /// Meta bergan xabar vaqti ISHONCHLIMI (24 soatlik oyna shundan hisoblanadi).
    ///
    /// <para>Qabul qilinadi: kelajakda emas (soat farqiga <see cref="InboundFutureSkewMinutes"/>
    /// daqiqa yon beriladi) va <see cref="InboundMaxAgeDays"/> kundan eski emas. Chegaradan
    /// tashqarisi — buzuq ma'lumot yoki soat nosozligi: bunda <c>null</c> qaytadi va chaqiruvchi
    /// joriy vaqtga qaytadi.</para>
    /// </summary>
    private static string? SaneInboundAt(string sentAtIso, DateTime now)
    {
        if (!InstagramContract.TryIso(sentAtIso, out var sent)) return null;
        if (sent > now.AddMinutes(InboundFutureSkewMinutes)) return null;
        if (sent < now.AddDays(-InboundMaxAgeDays)) return null;
        return sentAtIso;
    }

    /// <summary>Meta vaqti shundan ko'proq KELAJAKDA bo'lsa — ishonmaymiz (server soati farqi).</summary>
    private const int InboundFutureSkewMinutes = 60;
    /// <summary>Meta vaqti shundan eski bo'lsa — buzuq deb hisoblaymiz (24 soatlik oynadan ancha keng).</summary>
    private const int InboundMaxAgeDays = 30;

    /// <summary>Suhbatni "operator kerak" holatiga qo'yadi (sabab bilan) — inbox'da qizil chip.</summary>
    private static void Escalate(IgConversation conv, string reason)
    {
        conv.NeedsOperator = true;
        conv.NeedsOperatorReason = reason;
        conv.Unread = true;
    }

    private static string BuildHotAlert(IgConversation conv, IgAgentOutput o)
    {
        var lines = new List<string>
        {
            "🔥 Instagram: qaynoq lid!",
            // ⚠️ @username EMAS, PROFIL HAVOLASI: Telegram "@nom"ni o'zining mentioni deb
            // chizadi va menejer bosganda Instagram'ga emas, hech qayerga tushardi.
            $"👤 {InstagramContract.ProfileRef(conv.Username)}",
        };
        if (o.LeadName.Length > 0) lines.Add($"🧑 {o.LeadName}");
        if (o.LeadContact.Length > 0) lines.Add($"📞 {o.LeadContact}");
        if (o.LeadProductInterest.Length > 0) lines.Add($"📚 Qiziqish: {o.LeadProductInterest}");
        lines.Add($"⭐ Ball: {InstagramContract.ClampScore(o.LeadScore)}");
        if (o.LeadSummary.Length > 0) lines.Add($"📝 {o.LeadSummary}");
        if (o.EscalateToHuman) lines.Add("⚠️ Operator so'raldi");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Admin/superadminlarga Telegram xabari (mavjud bot orqali — yangi bot ochilmaydi).
    /// ⚠️ Xato JIM yutiladi: xabarnoma asosiy vazifani HECH QACHON buzmaydi
    /// (<c>LeadNotifier</c> / <c>BookSalesService.NotifyAdminsAsync</c> bilan bir xil siyosat).
    /// </summary>
    public static async Task NotifyAdminsAsync(
        IAppDbContext db, TelegramService telegram, CenterMeta? meta, string text, CancellationToken ct)
    {
        try
        {
            // ⚠️ MASTER DARVOZA: modul o'chiq bo'lsa bu yerdan ham tashqariga hech narsa ketmaydi.
            // (Matnsiz xabar signali `InstagramEnabled` tekshiruvidan OLDIN turadi — darvoza shu
            // yerda bo'lmasa o'chirilgan modul baribir Telegram'ga yozib turardi.)
            if (meta is null || !meta.InstagramEnabled || !meta.InstagramNotifyTelegram) return;
            if (!telegram.IsConfigured) return;

            var regs = await db.TelegramRegistrations
                .Where(r => r.UserId != null && r.UserId != "")
                .ToListAsync(ct);
            if (regs.Count == 0) return;

            var userIds = regs.Select(r => r.UserId!).Distinct().ToList();
            var adminIds = (await db.Users
                .Where(u => userIds.Contains(u.Id) && (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin))
                .Select(u => u.Id)
                .ToListAsync(ct)).ToHashSet();
            if (adminIds.Count == 0) return;

            var sent = new HashSet<long>();
            foreach (var r in regs)
            {
                if (r.UserId is null || !adminIds.Contains(r.UserId)) continue;
                if (!sent.Add(r.ChatId)) continue;      // bir chatga bir marta
                await telegram.SendMessageAsync(r.ChatId, text, ct: ct);
            }
        }
        catch { /* Xabarnoma suhbatni buzmasligi kerak. */ }
    }
}
