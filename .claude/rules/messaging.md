---
description: Xabar tizimi — TELEGRAM OUTBOX (MessageDelivery + ishchi, HTML, biriktirma, «Qabul qildim», bloklaganlar, qarindoshlik), yagona AVTO-XABAR (AutoMessageRule + 15 trigger), kampaniyalar, yutuqlar, Telegram bot, xabarlar frontendi. 2026-10 dan SMS, push, guruh chati va Local Call/SMS YO'Q.
paths:
  - "WunderkindLC.Application/Services/AutoMessage*.cs"
  - "WunderkindLC.Application/Services/Message*.cs"
  - "WunderkindLC.Application/Services/Telegram*.cs"
  - "WunderkindLC.Application/Services/Messaging/**"
  - "WunderkindLC.Server/Controllers/MessagingController.cs"
  - "WunderkindLC.Server/Controllers/StudentAchievementsController.cs"
  - "WunderkindLC.Server/Crm/CrmMessagingSetup.cs"
  - "WunderkindLC.Application/Services/*Reminder*.cs"
  - "WunderkindLC.Application/Services/BirthdayGreetingService.cs"
  - "WunderkindLC.Application/Services/LeadNotifier.cs"
  - "WunderkindLC.Application/Services/NotificationStore.cs"
  - "WunderkindLC.Server/Controllers/MessagesController.cs"
  - "WunderkindLC.Server/Controllers/AutoMessagesController.cs"
  - "WunderkindLC.Server/Controllers/NotificationsController.cs"
  - "WunderkindLC.Client/src/pages/admin/messages/**"
  - "WunderkindLC.Client/src/components/messaging/**"
---

# Xabar tizimi qoidalari

## 0. 2026-10 — KANALLAR QISQARTIRILDI (egasining qarori, `docs/ASSUMPTIONS.md` 2026-10-11)

O'rnatiladigan ilova YO'Q. Backend va klientdan **butunlay olib tashlandi**: SMS kanali (Eskiz va Local
SMS — CTI agent telefoni), push (FCM, Web Push, qurilma tokenlari), guruh CHATI (`ChatHub`/`ChatService`),
ilova (APK) fayllari va Local Call (CTI agent ilovasi, `/ws`). **Telegram — yagona tashqi kanal**,
ichki kanal — kabinet bildirishnomasi (`NotificationStore`, qo'ng'iroqcha).

| Nima edi | Endi |
|---|---|
| Avto-xabar: SMS / Push / Telegram bayroqlari | **Kabinet bildirishnomasi HAR DOIM** + Telegram (`SendTelegram`) — `AutoMessageChannels` |
| Eski SMS qoidasi (`SendSms=true`) | Telegram orqali yetkaziladi (`AutoMessageChannels.Telegram`), qoida keyingi saqlanganda `SendSms` tozalanadi |
| Push bayrog'i (`SendPush`) | Ahamiyatsiz — kabinet yozuvi har qoida uchun yoziladi (ilgari faqat push bilan) |
| Ommaviy SMS / push / Telegram e'lon | Faqat **Telegram e'lon** (`POST messages/broadcast`) — auditoriya: barcha ota-onalar / guruh / o'qituvchilar / tanlab |
| O'quvchi(lar)ga "SMS yuborish" oynasi | «Telegram xabar» oynasi (o'sha filtrlar, `scope: selected`) |
| Lid hodisalari (`lead_new`, `trial_reminder`, `test_link`, `test_result`) | **OLIB TASHLANDI** — faqat lid telefoniga SMS edi, lidning Telegram manzili yo'q |
| Lidga yakka/ommaviy SMS | **OLIB TASHLANDI** |
| Daraja testi havolasini SMS bilan yuborish | Havola YARATILADI va operatorga qaytariladi (nusxalab Telegram/WhatsApp orqali yuboradi), `invite.SmsStatus = "link"` |
| Jurnal baho/davomat push'i | Kabinet bildirishnomasi (push yo'q) |
| Support bron bildirishnomasi | Kabinet bildirishnomasi (push yo'q) |
| Botdagi «📝 Testni ishlash» / `/test` / eski test tugmalari | Tushuntirish matni + yangilangan klaviatura (`TelegramBotService.SendLegacyTestNoticeAsync`, `IsLegacyOnlineTestCallback`) |
| Botda APK yuborish | Faqat veb-versiya havolasi |

⚠️ **BAZA TEGILMAGAN.** Eski jadvallar (`SmsBatches`, `SmsLogs`, `SmsQueueJobs`, `SmsQueueItems`,
`PushMessages`, `DeviceTokens`, `ChatMessages`, `CtiAgents`, `CtiCallRecords`, `CtiCallEvents`,
`CtiCommandLogs`) va ustunlar (`AutoMessageRule.SendSms/SendPush/SmsProvider`, `CenterMeta.EskizFrom`,
`LocalSms*`, `StudentApk*`/`TeacherApk*`, `FcmWebConfigJson`, `FcmVapidKey`,
`UserSettings.NotificationsEnabled`) **QOLDI** — entity'lari `[Obsolete("Legacy (removed 2026-10)…")]`
bilan belgilangan va tasdiqlangan o'chirishgacha turadi. Yangi kod ularga YOZMASIN; faqat tozalash
(`StudentPurge`, `GroupDeletion`) va zaxira (`BackupService`) ularga tegadi.

⚠️ **`UserNotification.PushMessageId` va `NotificationStore` QOLADI** — kabinet qo'ng'iroqchasi shu.

⚠️ `GET api/admin/messages/classes` qoldi (e'lon "Guruh" auditoriyasi uchun guruhlar + Telegram'ga
ulangan ota-onalar soni), lekin chat darvozasidan TASHQARIDA — `messages.broadcast` ruxsati bilan.

## 0.5. TELEGRAM OUTBOX — CRM upgrade Phase 2 (2026-10-12, `docs/SPEC-crm-upgrade.md` §3)

Kod: `Application/Services/Messaging/*`, `MessagingController` (`api/admin/messaging`), `Server/Crm/CrmMessagingSetup.cs`.
Testlar: `TelegramHtmlTests`, `MessageDeliveryWorkerTests`, `MessageAudienceTests`, `MessagingFlowTests`, `MessagingRbacTests`.
Migratsiya (integrator): `CrmMessaging` + `docs/migrations/Phase2.sql`.

### 🔴 ENG MUHIM — ota-ona/o'quvchiga Telegram FAQAT OUTBOX orqali

Har ota-ona/o'quvchiga ketadigan Telegram xabar — `MessageDelivery` QATORI (`TelegramOutbox.EnqueueAsync` /
`Build`, chaqiruvchining `SaveChanges`i ichida) va uni `MessageDeliveryWorker` yuboradi. To'g'ridan-to'g'ri
`telegram.SendMessageAsync` FAQAT: bot interaktiv javoblari (login, kod, support, kitob do'koni), shartnomalar,
lid kartalari va XODIM/O'QITUVCHI operativ xabarlari (davomat eslatmasi, topshiriqlar). Yangi ota-ona xabari
qo'shsangiz — `AutoMessageService.DispatchStudentAsync` yoki `TelegramOutbox` dan boshqa yo'l YO'Q.

Nega: yuborishdan OLDIN aniq sanoq, oluvchi bo'yicha jurnal, «Qabul qildim», bloklaganlarni hisoblash, restartga
chidamlilik va Telegram tezlik chegarasi — hammasi shu jadvaldan.

### Ishchi (`MessageDeliveryWorker`, singleton = hosted service + `IOutboxSignal.Wake()`)

| Holat | Nima bo'ladi |
|---|---|
| startup | har `sending` → `unknown` (QAYTA YUBORILMAYDI), `pending` davom etadi |
| band qilish | `pending → sending` (`Status` — concurrency token) |
| `Ok` | `sent`, `SentAt`, `TelegramMessageId`; birinchi yuklashda `MessageAttachment.TelegramFileId` saqlanadi |
| `Blocked` (403, «bot was blocked», «chat not found»…) | `blocked` + o'sha CHATNING HAMMA ulanishlariga `BlockedAt` |
| 429 | `pending`, `NextAttemptAt = now + retry_after`; butun ishchi pauza (≤ 60 s) |
| «can't parse entities» | BIR marta darhol oddiy matn (`ToPlain`, parse_mode'siz) |
| boshqa xato | 1 va 5 daqiqadan keyin qayta; 3-urinishda `failed` |
| `ExpiresAt` o'tdi (kampaniya 12 soat, avto 6 soat) | `expired` — eskirgan «kelmadi» yuborilmaydi |

- Tezlik: ≤ 20 xabar/soniya, bitta chatga ≥ 1,1 s oraliq ("erta" qator qoldiriladi, band qilinmaydi).
- Uzun matn + fayl (> 1024 belgi): avval fayl IZOHSIZ, keyin matn; tugma va `TelegramMessageId` OXIRGI xabarda.
  ⚠️ `pending` qatorda `TelegramMessageId` bo'lsa — fayl allaqachon ketgan, qayta urinish faqat MATNNI yuboradi.
- Kampaniyada `pending/sending` qolmasa → `Broadcast.Status = done`.
- Kunlik: 180 kundan eski jurnal o'chiriladi (5 000 lik partiyalar); 60 kundan eski, `file_id` si bor va hech qayerda
  (navbat, yoqilgan qoida, YUTUQ) ishlatilmayotgan biriktirmaning LOKAL fayli o'chiriladi (`FileDeletedAt`).
- Bot sozlanmagan bo'lsa ishchi yubormaydi (qatorlar `expired` bo'ladi); KAMPANIYA esa 400 bilan rad etiladi.

### Matn — Telegram HTML (`TelegramHtml`, `MessageRender`)

- Shablonda faqat `b/strong`, `i/em`, `u/ins`, `s/strike/del`, `code`, `pre`, `a href` (http/https/tg).
  Boshqa teg, yolg'iz `<`/`>`/`&` — MATN sifatida escape. Yopilmagan/aralash teg → «Formatlash yopilmagan: <b>»,
  boshqa havola → «Havola noto'g'ri: faqat http(s) manzil».
- ⚠️ Token QIYMATLARI har doim escape (`MessageTokenizer.*(…, html: true)`): «Ali <b>» ismli o'quvchi Telegram'da
  aynan shu MATN bo'lib chiqadi.
- Kabinet qo'ng'iroqchasiga ODDIY matn (`ToPlain`) yoziladi. 4096 dan uzun avto-xabar — oddiy matn, 4000 da qirqilib «…».
- Eski qoida matnida buzuq teg bo'lsa avto-xabar to'xtamaydi — butun shablon escape qilinib yuboriladi.
- Noma'lum `{token}` → «Noma'lum o'zgaruvchi: {…}» (qoida saqlash, shablon, kampaniya — 400; preview — `errors`).
- Sarlavhalar: avto «🔔 <qoida nomi>», qarz «💰 To'lov eslatmasi», e'lon/arxiv «📢 Markaz e'loni», qarz kampaniyasi — yo'q.

### Kim oladi — qarindoshlik va auditoriya

- **ULANISH — PANEL ORQALI, TELEFON SO'RALMAYDI (2026-10-12, egasining qarori «bitta botda ishlaydi hamma»):** /start
  va har qanday matnga bot «📱 Panel» (Mini App) tugmasini beradi (`TelegramBotService.StartKeyboard` — Panel ENG TEPADA,
  kitob (yoqilgan bo'lsa), adminga murojaat; `request_contact` YO'Q). Foydalanuvchi Panel ichida login/parol (yoki kod)
  bilan kirgach klient `POST api/telegram/link {initData}` yuboradi (`lib/telegramLink.ts`, sessiyada bir marta, xato
  jim — ikkilamchi amal). Server Telegram imzosini ASOSIY bot tokeni bilan tekshiradi (`TelegramInitData`) va chatni
  FAQAT so'rovchining O'Z akkauntiga bog'laydi (`TelegramMiniAppLink`): o'quvchi akkaunti → shu o'quvchi (qarindoshlik
  «Boshqa» — admin o'zgartiradi), ota-ona akkaunti → telefoni mos farzandlar (qarindoshlik telefondan), o'qituvchi →
  `TeacherId`, admin/xodim → `UserId`. ⚠️ Bitta chatda o'qituvchi/xodim uchun BITTA qator (unikal `StudentId+ChatId`,
  `StudentId=""`) — ikkala akkaunt bo'lsa id'lar o'sha qatorga qo'shiladi, BOSHQA o'qituvchi/xodim akkaunti ulanmaydi.
  Yangi ulanishda: audit (`Student`/`TeacherSalary`/`Staff`, chat id'siz), botda «✅ Telegram bot ulandi» + yangilangan
  klaviatura, o'quvchiga «01» (`bot_connected`). Testlar: `TelegramMiniAppLinkTests`, `telegramLink.test.ts`.
  Eski yo'l (kontakt yuborish) hamon qabul qilinadi, lekin bot uni SO'RAMAYDI.
- **Ro'yxatdan o'tish (bot, ESKI yo'l — kontakt kelsa):** kontakt 4 raqamning ISTALGANI bilan solishtiriladi (`Phone`, `ParentPhone`,
  `FatherPhone`, `MotherPhone` — `TelegramRegistrar`); `CenterMeta.TelegramPhoneMatchField` endi O'QILMAYDI.
  Har mos o'quvchiga bitta ulanish, `Relation = TelegramRelations.Detect` (tartib: ota → ona → asosiy ota-ona → o'quvchi,
  aks holda `other`). Mavjud ulanishda qarindoshlik faqat BO'SH bo'lsa yoziladi (admin tahriri bosilmaydi).
  Yangi ulanishga «01» (`bot_connected`) — FAQAT o'sha chatga, bir marta (dedupe), tasdiq/login xabarlaridan keyin.
- **Admin** qarindoshlikni profildan o'zgartiradi (`PUT students/{id}/telegram-links/{regId}`, audit).
- **Bloklash:** yuborishda Blocked yoki `my_chat_member` (shaxsiy chat) `kicked` → `BlockedAt`; foydalanuvchi botga
  yozsa / tugma bossa / `member` bo'lsa — belgi tozalanadi. Bloklagan chat sanoqlardan chiqadi («Bloklagan: N»).
- **Auditoriya (`AudienceSpec`, `MessageAudienceResolver`):** `active` (arxivsiz, TIRIK a'zolik, standart holat
  active+trial, muzlatilgan — ixtiyoriy, yopiq guruhlilar yo'q), `archive` (FAQAT arxivdagilar), `selected`,
  `teachers`. «Barcha ota-onalar» = otasi/onasi/ota-onasi/boshqa — o'quvchining o'z chati KIRMAYDI.
  ⚠️ Arxiv va faol ARALASHMAYDI: e'lon arxivga → «Arxivdagilarga alohida «Arxiv» xabaridan yuboring»,
  arxiv xabari faolga → «Arxiv xabarida faqat arxivdagi o'quvchilar».
- Avto-qoida auditoriyasi: `parents` — o'quvchining o'z chatidan boshqa hammasi, `students` — faqat `student` chat.
- Shablonda o'quvchi tokeni bo'lmasa kampaniya BITTA chatga BITTA xabar (aka-uka ota-onasiga ikki marta emas).
- ⚠️ **Sanoq = yaratiladigan qatorlar:** `audience/count` aynan `POST campaigns` ning rejalashtiruvchisini
  (`CampaignService.PlanAsync`) chaqiradi. Ikkinchi sanoq yo'li YOZMANG.

### «✅ Qabul qildim» (`MessageAckService`, callback `mack:{deliveryId}`)

Birinchi bosish (xabar yuborilgan chatning O'ZI) → `AckAt`, tugma olib tashlanadi, «✅ Qabul qilindi. Rahmat!».
Begona chat ham, topilmagan id ham — BIR XIL «Xabar topilmadi yoki eskirgan» (2026-10-12, review L2: boshqacha matn
begona id MAVJUDLIGINI tasdiqlardi; natija `WrongChat`/`NotFound` faqat ichki). "Ko'rdi" alohida holat
EMAS (botga o'qildi belgisi kelmaydi). ⚠️ `mack:` umumiy bo'sh `answerCallbackQuery` dan OLDIN ushlanadi
(`MessagingRbacTests` qulflaydi). Phase 5 `wtok:` qo'shadi — `mack:`/`wtdone:` bilan to'qnashmaydi.

### Biriktirma (`MessageAttachments`, `MessageAttachmentStore`)

Bitta xabarga bitta fayl: rasm jpg/png ≤ 10 MB, video mp4 ≤ 50 MB, hujjat pdf/docx/xlsx/pptx/doc/xls/ppt ≤ 50 MB.
Kengaytma + Content-Type + sehrli baytlar — UCHALASI mos bo'lishi shart. Fayl `uploads/messaging/{guid}{ext}` —
⚠️ STATIK BERILMAYDI (`PrivateFolderFileProvider` ro'yxatida), faqat ishchi va `attachments/{id}/content`
(ruxsat bilan, `no-store`). Yuborish diskdan OQIM bilan (`"telegram-media"` mijozi, 120 s); `byte[]` ga yig'ilmaydi.
⚠️ Papka TUNGI tar zaxirasiga KIRMAYDI (2026-10-12, `docker-compose.yml` → `--exclude='uploads/messaging'`,
`uploads/face` kabi): video 50 MB gacha — arxivni shishiradi, yuborilgan fayl Telegram'da `file_id` bilan qoladi.
Qabul qilingan oqibat: disk yo'qolsa yutuq rasmlari tar'dan tiklanmaydi (`KOCHIRISH.md` — ko'chirishda volume TO'LIQ).

### Bot suhbati tarixida KIRISH MA'LUMOTLARI YO'Q (2026-10-12, review M4)

Botning har javobi tarixga (`BotSupportMessages.Text`, `BotUser.LastText`) yoziladi — «Support Telegram» paneli va
kunlik JSON zaxira (Telegram chatiga ketadi). Login/parol xabari va bir martalik kirish kodi — KIRISH MA'LUMOTI:
tarixga ular o'rniga belgi yoziladi — «[kirish ma'lumotlari yuborildi]» / «[bir martalik kirish kodi yuborildi]»
(`BotHistoryText.ForHistory`, `TelegramBotService.SendAsync` — botning YAGONA tarix yozuvchisi). Foydalanuvchiga xabar
AYNAN boradi. Moslash ATAYIN tor: faqat bot yuboradigan andozalarning qat'iy boshi («🔑 Kirish ma'lumotlari:\n»,
«🔑 Ilovaga kirish:\n», «🔑 Login: », «🔑 … uchun bir martalik kirish kodi:») — oddiy support xabariga tegilmaydi.
Eski qatorlar startupda BIR MARTA tozalanadi (SeedMarker `bot-history-credentials-scrub-2026-10`,
`CrmMessagingSetup`); JSON zaxira ham o'sha qoida bilan yig'iladi (ikkinchi qatlam). Yangi kirish xabari qo'shsangiz —
`BotHistoryText` andozalariga ham qo'shing (`BotHistoryTextTests`).

### Jadvalli avto-xabarlar (`AutoMessageSchedule`)

| Rejim | Triggerlar | Qachon |
|---|---|---|
| `monthly_day_time` | `payment_debt`, `rating_top`, `rating_bottom` | qoidaning O'Z kuni (31 → qisqa oyda oxirgi kun) va soatidan keyin |
| `daily_time` | `birthday` | har kuni qoidaning soatidan keyin |
| `custom` | `custom_schedule` | o'z kunlik/oylik oynasi (`CustomReminderService`) |

⚠️ Qarzdorlik eslatmasining ESKI "1-sana, keyin har toq kun 09:00" jadvali OLIB TASHLANDI — ko'proq kun kerak
bo'lsa ko'proq qoida. Xizmatlar har 10 daqiqada uyg'onadi; belgilar QOIDA bo'yicha: `payment_debt:{ruleId}`,
oluvchi `payment_debt:{ruleId}:{studentId}` (tug'ilgan kunda `birthday:{ruleId}[:{studentId}]`).
Mavjud qarz qoidalari migratsiyada 1-sana 09:00 ga o'tkaziladi.

### Hodisa kaliti (dedupe) — `DispatchStudentAsync(…, eventKey, sourceId, onlyRuleId, attachmentId)`

`eventKey` berilsa `{trigger}:{rule}:{student}:{chat}:{eventKey}` hech qachon ikki marta navbatga tushmaydi
(unikal indeks — oxirgi himoya, `TelegramOutbox.SaveAsync` dublikatni DETACH qiladi). Phase 3 «kelmadi» navbati
`eventKey = sourceId = item.Id` beradi va holatni `TelegramOutbox.SummaryBySourceAsync` dan o'qiydi; Phase 4
(`test_score`, `rating_*`) — `onlyRuleId` va `AutoMessageSchedule.IsDue` bilan. Qaytadi: navbatga qo'yilgan son.

### Yangi triggerlar (15 ta katalog)

`bot_connected` («01», faqat ulangan chatga) · `achievement` (yutuq tabrigi, rasm bilan) · `test_score`,
`rating_top`, `rating_bottom` (yuborish — Phase 4: `test_score` — test sahifasidagi «Ota-onalarga yuborish»
(`TestScoreMessages`, `tests.md` §0.2), `rating_*` — `MonthlyRatingNotifier`, `rating.md` §8). `payment_debt` va `attendance_absent` yangi qoidada
«Qabul qildim» oldindan yoqilgan (`DefaultAck`). Qoida saqlash — `SendTelegram = true` (endi faqat `Enabled` hal
qiladi), audit `AutoMessageRule` («Xabarlar» bo'limi).

### Startup tuzatishlari (bir martalik, `SeedMarker`)

`tg-relation-backfill-2026-10` — mavjud ulanishlarga qarindoshlik; `autorule-telegram-only-2026-10` — tashqi kanali
YO'Q (faqat push) qoidalar o'chiriladi (nomlari logda), SMS qoidalari Telegram orqali qoladi (Phase 1 qarori).

### Yutuqlar (`StudentAchievement`, `AchievementService`)

IELTS/CEFR/sertifikat/olimpiada — profil «Yutuqlar» tabi va kabinet «Yutuqlarim». «Ota-onaga tabrik yuborish» —
`achievement` trigger, kalit `{id}:{n}` (har qayta yuborishda yangi). Arxivdagiga tabrik yo'q. Qoida/chat yo'q
bo'lsa yutuq baribir SAQLANADI va javobda `warning` (400 qaytarilsa foydalanuvchi qayta yuborib dublikat yaratardi).
Rasm — biriktirma (faqat `photo`), hech qachon tozalanmaydi.

### Ro'yxatlarda «Telegram» ustuni (U6b, `TelegramLinkSummary`)

Faol o'quvchilar, o'qituvchining «O'quvchilarim» va guruh «O'quvchilar» tabi: ulangan (BLOKLANMAGAN) chatlar soni +
qarindoshlik belgilari. ⚠️ Faqat SON va qarindoshlik — telefon, ism, chat id YO'Q (o'qituvchiga ham ko'rinadi).

- **GURUH NOMI NOYOB (2026-10-07, `GroupNames`).** Nom BARCHA guruhlar (arxivdagilar ham) orasida
  bo'shliqsiz va registrsiz takrorlanmaydi — yaratish, qayta nomlash va «Tugatish»dagi yangi guruh 400
  "Bu nomli guruh allaqachon bor" oladi. Qayta nomlashda `Student.ClassName` yorlig'i va
  `Teacher.HomeroomClass` yangi nomga ko'chiriladi (`GroupNames.RenameResult(Students, Teachers)`).
  Chat 2026-10 da olib tashlandi — eski `ChatMessages` qatorlari endi ko'chirilmaydi; guruh
  O'CHIRILGANDA esa (nom boshqa guruhda bo'lmasa) tozalash sifatida o'chiriladi (`GroupDeletion`).
  ⚠️ Bazada unikal indeks YO'Q (prod'da eski dublikatlar bo'lishi mumkin) — tekshiruv faqat nom O'ZGARGANDA.

- **KALITLAR .env DA:** Telegram bot tokeni (`TELEGRAM_BOT_TOKEN`) — `AppSecrets` orqali .env dan
  o'qiladi, bazada saqlanmaydi va Sozlamalar sahifasidan kiritilmaydi (sahifada faqat holat + kerakli
  .env qatori — `EnvSecretField`). UI'dan saqlanadiganlar: bot username/nomi, kanal (telefon moslash sozlamasi 2026-10-12 dan O'QILMAYDI — 4 raqamning istalgani, §0.5).
  `FCM_SERVICE_ACCOUNT_JSON` .env da QOLDI — endi faqat yuz bilan kirishdagi Play Integrity
  tekshiruvining ZAXIRA kaliti (`GoogleServiceAccount`, `AppAttestation`). `ESKIZ_*` kalitlari
  `.env.example` va compose'dan olib tashlandi.

- **Yagona AVTO-XABAR tizimi:** `AutoMessageRule` entity (migratsiya `AddAutoMessages`) — har qoida:
  Trigger + Audience + Template (tokenli) + `SendTelegram` + jadval maydonlari. **15 trigger** katalogi (2026-10-12: + bot_connected, achievement, test_score, rating_top, rating_bottom — §0.5)
  `AutoMessageTriggers` (Application/Services): payment_received, monthly_charge, payment_debt,
  attendance_absent, **attendance_present** (2026-10-07: "Farzandingiz darsga keldi" —
  `DispatchNewlyPresentAsync`, har dars uchun BIR MARTA: saqlashdan OLDIN «keldi» bo'lganlarga qayta
  ketmaydi; qoida yoqilmasa hech narsa o'qilmaydi), **grade_entered** (baho qo'yilganda), birthday,
  student_added, lesson_attendance, custom_schedule. Lid hodisalari (lead_new, trial_reminder,
  test_link, test_result) 2026-10 da olib tashlandi — bazadagi eski qoidalari "noma'lum" bo'lib qoladi:
  ro'yxatda (`GET auto-messages`, `templates/all`) ko'rinmaydi va hech qachon ishga tushmaydi.
  `lesson_attendance` «lesson_start» rejimida `OffsetMinutes` MANFIY bo'lishi mumkin (−180..180): darsdan
  OLDIN eslatma; UI'da «Qachon: darsdan oldin / dars boshlangach» + daqiqa.
  Har trigger `Category` maydoniga ega ("O'quv jarayoni" | "Moliya" | "Boshqa") — GET /triggers
  javobida `category` chiqadi, frontend shu bo'yicha guruhlaydi (trigger javobida kanal bayroqlari YO'Q).
  **YETKAZISH (`AutoMessageService`, singleton; `AutoMessageChannels`):** har yoqilgan qoida uchun
  (1) KABINET bildirishnomasi — o'quvchi auditoriyasida `Student.UserId`, o'qituvchi auditoriyasida
  `Teacher.UserId` (akkaunt bo'lmasa yozilmaydi); (2) `AutoMessageChannels.Telegram(rule)` bo'lsa —
  `TelegramRegistrations` chatlari. Uchta eslatma HostedService (`PaymentReminderService`,
  `CustomReminderService`, `LessonAttendanceReminderService`) AYNAN shu modelda ishlaydi.
  **`POST auto-messages/seed-standard`** — standart Telegram qoidalari (to'lov, oylik hisob, qarzdorlik,
  kelmadi, tug'ilgan kun); shu trigger uchun Telegram/SMS qoidasi allaqachon bo'lsa o'tkazib yuboriladi.
  `DispatchTeacherAsync` va `DispatchLeadAsync` YO'Q.
  **`grade_entered`:** `JournalService.SetEntryAsync(db, req, autoMsg, teacher)` — qoida MAVJUD bo'lsa
  yuborish dispatcher orqali (extra {baho},{sana},{guruh}); qoida UMUMAN yo'q bo'lsa standart kabinet
  bildirishnomasi (default-on). Davomatda ham shu naqsh (`journal.md` §C8).
  **Token katalogi:** `MessageTokenCatalog` + `GET /api/admin/auto-messages/tokens` →
  `[{token,label,group,example}]` (group: student|common|event; lid guruhi va `{link}`, `{natija}`, `{daraja}`
  2026-10 da olib tashlandi; 2026-10-12 dan `{ball}`, `{foiz}` QAYTDI va `{qarindosh}`, `{yutuq}`, `{yutuq_natija}`,
  `{test}`, `{turi}`, `{orin}`, `{guruh_orin}`, `{reyting_orin}` qo'shildi; `example` — server preview namunasi);
  frontend token chiplarini shundan oladi.
  **`{oqituvchi}` — GURUH O'QITUVCHISI F.I.Sh:** `MessageTokenizer.Student` ixtiyoriy `teacherName`
  parametridan to'ladi (`Teacher(...)`da — o'qituvchining o'zi). Chaqiruvchi nomni
  `MessageTokenizer.GroupTeacherNameAsync(db, group, student)` (bitta xabar) yoki
  `TeacherNamesByIdAsync` + `TeacherNameOf(group, names)` (ro'yxat — N+1 bo'lmasin) orqali beradi.
  Guruh KONTEKSTI ({guruh}/{oqituvchi}/{dars_*}) — hodisa guruhi: `DispatchStudentAsync(..., group:)`
  ga student_added (guruhga qo'shish + yangi o'quvchi + **lidni o'quvchiga aylantirish**, faqat
  a'zolik haqiqatan yaratilganda), grade_entered va attendance_absent
  (`DispatchAttendanceAbsentAsync(..., group)`) ham guruhni UZATADI; berilmasa o'quvchining asosiy
  (ClassName) guruhi olinadi. `PaymentReminderService`/`CustomReminderService` ommaviy sikllarda
  guruh+o'qituvchi lug'ati BIR MARTA yuklanadi.
  ⚠️ `PaymentReminderService` da guruh konteksti HAR XABARNING O'Z guruhidan olinadi
  (pastdagi «QARZDORLIK ESLATMASI — HAR FAN ALOHIDA»), `ClassName` yorlig'i esa faqat a'zoligi
  BO'LMAGAN o'quvchining zaxira yo'lida ishlatiladi.
  **TOZALANGAN (migratsiya `MessagingCleanup`):** `ReminderRule`, `RemindersController`, `ReminderTriggers`,
  `SmsTemplate.Trigger/IsAuto`. `SmsTemplate` (jadval nomi tarixiy) — faqat qo'lda shablon
  (`{id,name,text,order}`). YOZISH — faqat `messaging/templates` (`MessagingController`: Telegram HTML tekshiruvi,
  noma'lum token, audit); eski `POST/PUT/DELETE messages/templates` 2026-10-12 da OLIB TASHLANDI (chaqiruvchisi yo'q edi),
  `GET messages/templates` va `templates/all` qoldi.
  API: `api/admin/auto-messages` (+ /triggers + /tokens + /seed-standard), AdminPerm("messages.broadcast").

- **GURUHI YOPILGAN/TUGATILGAN O'QUVCHIGA AVTO-XABAR YO'Q** (`MessagingAudience.ClosedGroupStudentIdsAsync`):
  o'quvchining a'zoligi BOR-u, biror ham TIRIK a'zoligi (`StudentGroup.IsActive` + guruh ARXIVLANMAGAN)
  qolmagan bo'lsa — tizim o'zi boshlaydigan xabarlar unga yuborilmaydi: qarzdorlik eslatmasi
  (`PaymentReminderService`), tug'ilgan kun (`BirthdayGreetingService`), erkin/jadvalli eslatma
  (`CustomReminderService`), ommaviy Telegram e'lon (`MessagesController` — "Tanlangan" rejimidan
  TASHQARI: u yerda admin kimni tanlasa o'shanga ketadi). Muzlatilgan, lekin guruhi FAOL o'quvchi
  xabar olaveradi (ta'til). A'zoligi umuman yo'q (eski ClassName) o'quvchilar tegilmaydi.
  Hodisaga JAVOB bo'lgan xabarlar (masalan "to'lov qabul qilindi") filtrlanmaydi — yopilgan guruh
  qarzini to'lagan ota-ona tasdiq oladi. O'qituvchiga dars eslatmasi (`LessonAttendanceReminderService`)
  kunning HAQIQIY darslariga ketadi — `AttendanceWatch.LessonsOn` (ko'chirilgan dars yangi kunida va yangi
  vaqtida, guruh umri, arxiv/BLOKLANGAN guruh yo'q) va shu kuni darsni O'TADIGAN odamga (o'rinbosar bo'lsa —
  o'rinbosarga). "Yuborildi" belgisi BAZADA (`LessonReminderMark`, unikal kalit) — deploy/qayta yuklash
  eslatmani takrorlamaydi (ilgari xotiradagi `HashSet` edi).
- **DAVOMAT XABARLARI (`attendance_absent`) — qachon ketadi:** `.claude/rules/journal.md` §C8 — "kech" hech
  qachon "kelmadi" emas, faqat "kelmadi emas → kelmadi" o'tishida, ommaviy davomatda faqat YANGI kelmaganlarga
  va FONDA — BAZADAGI navbat orqali (`AttendanceNotifyItem` + `AttendanceNotifyWorker`, restartda yo'qolmaydi);
  standart "Davomat" kabinet bildirishnomasi faqat qoida umuman yo'q bo'lsa (bitta hodisa — bitta xabar).
  ⚠️ **2026-10-12 (CRM U3b):** bitta katak ham NAVBAT orqali; «kelmadi» DARS TUGAGACH + `AbsentNoticeDelayMinutes`
  bilan ketadi, kech olingan davomatda menejer tasdig'ini kutadi (`AbsentNoticeApproval`, standart `late_only`),
  menejer Nazorat → «Davomat xabarlari» da to'xtatadi/tasdiqlaydi/bekor qiladi, jurnal «keldi»ga tuzatilsa —
  bekor; «keldi» darhol; faqat BUGUNGI dars. Yuborish — outbox, `eventKey = sourceId = item.Id` (holat sahifada
  `TelegramOutbox.SummaryBySourceAsync` dan). To'liq qoida — `journal.md` «KELMADI XABARLARI NAVBATI».

- **ARXIVDAGI O'QUVCHI (2026-10-07) — `AutoMessageTriggers.ReachesArchived`.** Arxiv = o'quvchi
  markazdan KETDI. `DispatchStudentAsync` arxivdagi o'quvchiga FAQAT `payment_received` (to'lov
  kvitansiyasi — eski qarzini to'lagan ota-ona tasdiq olsin) yuboradi; davomat (kelmadi/keldi), baho,
  guruhga qo'shilish va boshqa hodisalar — YO'Q. Sabab: eski arxivlarda a'zolik tirik qolgan va
  jurnal ularni hali ko'rsatishi mumkin — har darsda ota-onaga "darsga kelmadi" ketardi.

- **GURUH AUDITORIYASI — A'ZOLIK bo'yicha (`MessagingAudience.GroupAudienceByNameAsync`).**
  "Guruh" tanlangan e'lon (Telegram) oluvchilari eskirgan `Student.ClassName` bo'yicha
  EMAS (u faqat BIRINCHI guruhda yoziladi): shu guruhda TIRIK a'zoligi bor (`IsActive` + guruh
  arxivlanmagan, muzlatilgan ham) o'quvchilar + a'zoligi UMUMAN yo'q eski yozuvlar (`ClassName`
  zaxirasi). Arxivdagi o'quvchi hech qachon kirmaydi. ⚠️ Guruhlar ro'yxatidagi SANOQ
  (`GET messages/classes` — o'quvchilar va Telegram chatlari) AYNAN shu funksiyadan — ko'rsatilgan
  son = oluvchilar. Ilgari sanoq a'zolikdan, yuborish `ClassName` dan edi: ikkinchi guruhdagi
  o'quvchi sanalar, lekin xabar olmasdi; guruh nomi o'zgarsa guruhga umuman hech narsa ketmasdi.
  Guruh qamrovida `{guruh}` tokeni ham TANLANGAN guruh nomi bilan to'ladi.

- **FRONTEND (2026-10-12, CRM upgrade P2.8):** Xabarlar sahifasi `?tab=` bilan 5 tab — «Xabar yuborish»
  (`TelegramComposer`: auditoriya Faol/Arxiv/Tanlangan/O'qituvchilar + qarindoshlik chiplari, formatlash, bitta fayl,
  «Qabul qildim», server PREVIEW va sanoq) · «Qarzdorlar» · «Avto xabarlar» · «Shablonlar» · «Tarix» (kampaniya
  hisoboti — har o'quvchi va har chat holati, «Bekor qilish»; avto-xabarlar statistikasi). Preview HTML faqat
  `lib/telegramHtml.ts` whitelist renderi bilan (`dangerouslySetInnerHTML` YO'Q). O'quvchi(lar)ga xabar —
  `TelegramSendModal`. ⚠️ «Telegram xabar» tugmalari (o'quvchi profili, guruh «⋮», «O'quvchilar davomati»,
  o'quvchilar ro'yxatidagi tanlov paneli) FAQAT `can('messages.broadcast', 'create')` bo'lganda ko'rinadi (2026-10-12) —
  yuborish (`POST messaging/campaigns`) shu ruxsatsiz 403 bo'lardi. Quyidagi 3-tabli tavsif — TARIX (2026-10 holati).
- **FRONTEND (2026-10, eski):** Xabarlar sahifasi 3 tab — **Xabar yuborish (default)** | Avto xabarlar
  (trigger'lar Category bo'yicha guruhlangan; har qoidada qat'iy «Kabinet (bildirishnoma) — doim» chipi +
  Telegram kaliti; «Standart qoidalar» → `seed-standard`) | Tarix (faqat Telegram e'lonlari).
  Composer — faqat Telegram: Barcha ota-onalar / Guruh / O'qituvchilar / Tanlab ("Tanlab" ro'yxatida
  Telegram'ga ulanmaganlar belgilanadi — `recipients`, `recipients/teachers`). Yagona `MessageEditor`
  (`components/messaging/`) — token+shablon chiplar (SMS uzunlik hisobi olib tashlandi).
  O'quvchi(lar)ga xabar — `TelegramMessageModal` (guruh sahifasi, o'quvchilar ro'yxati, davomat, profil):
  Holat chiplari + To'lov filtri (qarzdorlar MIJOZ tomonida, guruh balansi bo'yicha — `GroupMember.balance`)
  + qo'lda belgilash; yuborish `sendBroadcast({scope:'selected'})`. Guruh oynasi oluvchilarni
  `GET classes/{id}/sms-recipients` dan oladi (nomi tarixiy). Sozlamalar → «Xabar kanallari» — faqat
  Telegram bot; eski `eskiz`/`firebase`/`local-sms`/`apk` bo'limlari → `/admin/sozlamalar/umumiy` ga
  yo'naltiriladi. Chat menyusi olib tashlandi: `/admin/chats` → `/admin/messages`,
  `/teacher/messages` → `/teacher`, `/student/chat` → `/student`; «Support Telegram» qoldi.

- **To'lov eslatmasi:** «Xabarlar → Qarzdorlar» tabi (`GET messaging/debtors` — avtomatik eslatma bilan bir xil
  populyatsiya, har kurs qarzi, Telegram holati, oxirgi eslatma va «Qabul qildi») → `POST campaigns` `kind=debt`
  (bo'sh matn = tizim matni, HAR FAN alohida). Avtomatik eslatma — `payment_debt` (pastdagi «QARZDORLIK ESLATMASI»).

- **Lidni Telegram guruhga avto-yuborish:** Bot GURUHGA qo'shilsa (`my_chat_member` yangilanishi —
  `TelegramService.GetUpdatesAsync` allowed_updates'ga qo'shilgan) — `TelegramGroup` entity'ga yoziladi
  (migratsiya `AddTelegramGroups`, ChatId unikal, IsActive), guruhga bir marta tasdiq yuboriladi;
  chiqarilsa IsActive=false. Yangi lid tushganda `LeadNotifier.NotifyNewLeadAsync` adminlarga (private
  reg) VA barcha faol guruhlarga bir xil matnni yuboradi (dedupe: `sentChats`). Handler:
  `TelegramBotService.HandleMyChatMemberAsync`. Guruhda `/start` yo'q — faqat qo'shilish kifoya.
  ⚠️ Yuborilgan xabar — **KARTA**: keyin u yangi xabar bilan emas, TAHRIR bilan yangilanadi
  (quyidagi «LID KARTASI» bo'limi).

- **BOTDA MAJBURIY OBUNA:** yagona darvoza `TelegramBotService.RequireSubscriptionAsync` — `/start`,
  telefon yuborish va `/kod` (kirish kodi) shu yerdan o'tadi (5 daqiqa keshlanadi; `/test` 2026-10 dan
  faqat "onlayn test olib tashlandi" tushuntirishini beradi); «Adminga murojaat» ATAYIN ochiq. DIQQAT: Telegram `getChatMember` faqat bot kanalda
  **ADMIN** bo'lsagina ishlaydi; bot admin bo'lmasa yoki kanal xususiy havola (`t.me/+…`) bo'lsa
  tekshirib BO'LMAYDI — tizim fail-open (hammani o'tkazadi), lekin `TelegramService.CheckChannelAsync`
  diagnostikasi Sozlamalar → Telegram bot sahifasida sababni ko'rsatib turadi
  (ok | not-set | no-token | private | not-found | bot-not-admin).

## LID KARTASI — guruhdagi lid xabari TAHRIRLANADI (2026-08-22)

Migratsiyalar: `AddLeadTelegramMessages` (jadval) + `AddLeadTelegramMessageFk` (FK, cascade).
Entity — `LeadTelegramMessage` (LeadId · ChatId · MessageId · TextHash · IsDead), unikal indeks
**(LeadId, ChatId)**. Kod: `LeadNotifier.SyncCardAsync` / `MarkDeletedAsync` / `NotifyNewLeadAsync`,
`TelegramService.EditMessageTextOutcomeAsync` + `ClassifyEditError`.
Testlar: `WunderkindLC.Tests/LeadsTests.cs` (§5).

Lid xabari — **KARTA**: u lidning **JORIY holatini** ko'rsatadi (bosqich, sinov darsi, takroriy
murojaat, izohlar, «❌ Yo'qotildi: sabab» — izohi faqat shaxsiy chatda, `crm-leads.md` «YO'QOTILGAN
LIDLAR» §5, «O'quvchi bo'ldi», oxirgi test natijasi) va lid har o'zgarganda o'sha xabar
`editMessageText` bilan **joyida tahrirlanadi** — yangi xabar YUBORILMAYDI.
Ilgari faqat lid TUG'ILGANDAGI xabar turardi va u bir kunda eskirardi; har o'zgarishga yangi xabar
yuborish esa guruhni «o'zgardi / yana o'zgardi» oqimiga aylantirardi.

**Oluvchilar:** faol GURUH(lar) + **FAQAT superadmin**(lar)ning shaxsiy chati
(`LeadNotifier.ShouldNotify` — oddiy admin/xodimga shaxsiy xabar ketmaydi).
⚠️ `SyncCardAsync` ham shaxsiy chat egasining **JORIY** rolini tekshiradi (2026-10-07): oddiy adminga
tushirilgan yoki o'chirilgan superadminning chatidagi karta (izohlari bilan!) endi yangilanmaydi.
Ilgari "registratsiyasi bor" kifoya edi.

### 🔴 GURUHGA IZOH MATNI CHIQMAYDI — IKKI XIL MATN, IKKI XIL XESH

| Oluvchi | Kartada izohlar | Nega |
|---|---|---|
| **Guruh** | faqat SANOQ: «💬 3 ta izoh» | Menejer izohi — ichki, filtrsiz matn (mijoz haqidagi shaxsiy mulohaza, to'lov qobiliyati va h.k.). U bot qo'shilgan HAR bir guruhga tarqalmasligi kerak |
| **Superadminning shaxsiy chati** | oxirgi **2** izoh to'liq (`MaxNoteLines`, har biri `MaxNoteLength` = 120 belgi) | Qaror qabul qiladigan odam mazmunni ko'rishi kerak |

Matn bitta joyda ikki variantda quriladi: `Render(parts, includeNotes: true/false)`.
⚠️ **Xesh HAR QATOR uchun O'ZINING matnidan olinadi** — aks holda guruh va shaxsiy chat
bir-birining xeshini «eskirgan» deb ko'rib, kartani navbat bilan bekorga qayta yozib turardi.

⚠️ Sanoq (`NoteCount`) alohida `COUNT` so'rovi bilan olinadi: yuklangan izohlar ko'pi bilan 2 ta,
ular «3 ta izoh» deyishga yaramaydi.

### 🔴 Kartasi YO'Q lidga karta YARATILMAYDI

`SyncCardAsync` faqat MAVJUD yozuvni yangilaydi: `LeadTelegramMessages` da qator bo'lmasa (yoki
hammasi `IsDead` bo'lsa) — **hech qanday so'rov ketmaydi**. Ya'ni 2026-08 gacha yaratilgan eski
lidlar guruhga qaytib chiqmaydi.

⚠️ Busiz deploydan ertasiga menejer kanbanda 200 ta eski lidni surganda guruhga **200 ta yangi
karta** yog'ilardi. Karta faqat HODISADAN tug'iladi: *yangi lid · takroriy murojaat · test natijasi*
(`NotifyNewLeadAsync`).

### ⚠️ FAOL CHAT DARVOZASI — «yozuv bor» ≠ «yuborish kerak»

`SyncCardAsync` yozuvlarni oluvchilar bilan KESISHTIRADI: `TelegramGroups` dagi **`IsActive`**
guruhlar + `TelegramRegistrations` dagi chatlar. Kesishmagan qator **o'tkazib yuboriladi**.

Sabab: admin guruhni o'chirgan bo'lsa (bot chiqarilgan → `IsActive=false`) u yerga yangi lidlar
bormaydi — eski kartalar ham bormasligi kerak.

⚠️ **Bunday qatorga `IsDead` QO'YILMAYDI**: guruh qayta yoqilishi mumkin, o'shanda karta o'z
joyida yangilanishda davom etadi. «O'lik» deb belgilansa — karta abadiy muzlab qolardi.

⚠️ Guruh/shaxsiy chat farqi ham SHU so'rovdan chiqadi (`knownGroups`): chat `TelegramGroups` da
bo'lsa — izohsiz variant, aks holda izohli.

### ⚠️ Tahrir — JIM. Shuning uchun ikki xil hodisa AJRATILGAN

Telegram tahrirlangan xabarni bildirishnoma qilmaydi: chat ro'yxat tepasiga chiqmaydi, telefon
jiringlamaydi.

| Hodisa | Nima bo'ladi | Nega |
|---|---|---|
| **Ichki o'zgarish** (bosqich, izoh, birinchi/sinov darsi, konversiya, tahrir) | faqat **jim tahrir** | o'zgarishni qilgan odam CRM'ning O'ZIDA turibdi — unga bildirishnoma kerak emas |
| **Tashqaridan kelgan ish** (yangi lid, takroriy murojaat, daraja testi natijasi) | tahrir **+ kartaga JAVOB (reply) qilib bitta qatorli SIGNAL** | aks holda menejer yangi ishni umuman sezmay qolardi |

Signal ataylab qisqa (`LeadNotifier.SignalText`), batafsili kartaning o'zida. **Signalning
`message_id`'si SAQLANMAYDI** — u bir martalik bildirishnoma, hech qachon tahrirlanmaydi.

⚠️ Tahrir tarmoq xatosi bilan yiqilsa ham signal YUBORILADI (hodisa yashirin qolmasin), lekin
**429 (`RateLimited`) da yuborilmaydi** — o'sha chatga navbatdagi so'rov chegarani yanada
chuqurlashtirardi.

⚠️ Signal `reply_parameters` bilan ketadi (pastda) — javob berilayotgan karta o'chirilgan bo'lsa
ham xabar baribir boradi.

### `TextHash` — bir xil matnga so'rov umuman yuborilmaydi

Yozuvdagi `TextHash` (matnning SHA256'si) joriy matn xeshiga teng bo'lsa `editMessageText`
**chaqirilmaydi** va **yozuvga ham tegilmaydi** (bekorga `UPDATE` ketmasin). Sabab: Telegram
bunday tahrirga `message is not modified` qaytaradi — foyda nol, tezlik chegarasi esa bekorga
yeyiladi (har lidda bir necha chat bor).

⚠️ Karta matni **determinlashgan** bo'lishi SHART (bir xil holatdan bir xil matn) — aks holda xesh
bekorga farq qilib, har safar ortiqcha so'rov ketardi. Shu sabab matn HAR DOIM bitta joydan
yig'iladi: `LoadCardPartsAsync` → `Render` → `BuildText` + `BuildStateText`. Shu sababdan
`TrialLessons` va `LevelTestSubmissions` so'rovlarida `ThenByDescending(Id)` bor: bir vaqtli ikki
yozuvda PostgreSQL tartibi kafolatlanmagan bo'lar, matn (demak xesh) sababsiz o'zgarib turardi.

### ⚠️ XESH «🕒 Yangilandi» QATORISIZ hisoblanadi

Karta oxirida `🕒 Yangilandi: HH:mm` turadi (odam kartaning TIRIK ekanini ko'rsin), lekin u
**xeshga KIRMAYDI** — xesh faqat `body + tail` dan olinadi.

🔴 NEGA: vaqt qatori xeshga kirsa xesh **HAR DAQIQA** o'zgarardi, ya'ni `TextHash` qisqa yo'li
amalda umuman ishlamasdi — menejer kanbanda 20 lidni sursa 20 × (guruhlar + superadminlar) tahrir
ketib, Telegram guruh chegarasi (~20 xabar/daqiqa) urilardi.

### 🔴 QIRQISH — HOLAT BLOKIDAN EMAS, TEPADAN

Matn **4000 belgiga** sig'diriladi (`MaxTextLength`; Telegram chegarasi 4096), lekin qirqish
**tepa qismdan** boradi: holat bloki (bosqich, sinov darsi, izohlar, «O'quvchi bo'ldi», vaqt
qatori) HAR DOIM saqlanadi, kam qimmatli tepa qism (uzun so'rovnoma, izoh) qisqartiriladi
(`Render` da `budget` hisobi).

🔴 NEGA: ilgari butun satr OXIRIDAN kesilardi. Uzun so'rovnomali lidda aynan holat bloki qirqilib
ketar, matn holatga qarab O'ZGARMAY qolar, xesh esa barqarorlashib **karta ABADIY MUZLARDI** —
nosozlik xato bermas, karta shunchaki hech qachon yangilanmasdi.

⚠️ Qirqishda **emoji o'rtasidan kesilmaydi** (`Cut` — surrogat juftlik butun qoladi, aks holda
buzuq belgi chiqardi).

⚠️ Ilgari uzun test natijasi 4096 dan oshib Telegram 400 qaytarar, xato tashqi `catch` da jim
yutilar va **XABAR UMUMAN YO'QOLARDI** — chegara shundan qolgan.

### ⚠️ SARLAVHA — SAQLANGAN ma'lumotdan, `isNewLead` bayrog'idan EMAS

`HeaderOf(lead, sub)`:

| Shart | Sarlavha |
|---|---|
| test natijasi lidning O'ZIDAN KEYIN yaratilgan (`sub.CreatedAt > lead.CreatedAt`) | «🔁 Mavjud lid — yangi test natijasi» |
| `RepeatCount > 0` | «🔁 Mavjud lid yangilandi» |
| qolgan holatda | «🆕 Yangi lid!» |

NEGA chaqiruvchining bir martalik `isNewLead` bayrog'i ISHLATILMAYDI: `SyncCardAsync` keyin
boshqa sarlavha chizsa, matn (demak xesh) **har sinxronizatsiyada** o'zgarardi va karta bekorga
qayta yozilaverardi.

⚠️ `RepeatCount` ham YETARLI EMAS: taklif havolasi bilan yuborilgan testda (`LevelTestService`)
u OSHIRILMAYDI — ya'ni mavjud lidning kartasi «🆕 Yangi lid!» bo'lib qayta yozilardi. Shuning
uchun asosiy mezon — sanalarni solishtirish.

⚠️ `isNewLead` bayrog'i YO'QOLMADI: u faqat YETKAZISHNI hal qiladi (tahrir + signal / yangi
to'liq xabar), sarlavhaga ta'sir qilmaydi.

⚠️ «Kiritdi» qatori ham DOIM `created` hodisasining `ActorName`idan olinadi (chaqiruvchi bergan
boyroq matn faqat ZAXIRA) — aks holda karta BIRINCHI tahrirdanoq kambag'allashib, bekorga bitta
tahrir so'rovi ketardi.

### `TgEditOutcome` — xato tasnifi (`ClassifyEditError`, sof funksiya, registrga bog'liq EMAS)

`EditMessageTextOutcomeAsync` → **`(Result, RetryAfterSeconds, MigrateToChatId)`**: yalang natija
chaqiruvchiga «429 dan keyin qancha kutay?» va «chat supergruppaga aylandi — yangi id qaysi?»
savollariga javob bermasdi (ikkisi ham javobning `parameters` bo'limida keladi).

| Telegram `description` / HTTP | Natija | Qaror |
|---|---|---|
| `message is not modified` | `NotModified` | muvaffaqiyat: xesh SAQLANADI |
| `message to edit not found` · `MESSAGE_ID_INVALID` · `chat not found` · `message can't be edited` · `bot was kicked` · `bot is not a member` · `chat_id is empty` | `Gone` | `IsDead = true` — **boshqa urinilmaydi** |
| `group chat was upgraded to a supergroup chat` | `Gone` + `MigrateToChatId` | qator **TIRIK qoladi**: `ChatId` YANGISIGA almashadi, `IsDead` bekor qilinadi (pastga qarang) |
| `bot was blocked by the user` · `user is deactivated` | `Gone` | SHAXSIY chat holatlari: karta guruhga ham, superadminning shaxsiy chatiga ham ketadi. Ilgari ro'yxatda faqat GURUH holatlari bor edi va bu ikkisi `Failed` ga tushib, lidning HAR o'zgarishida bekorga so'rov ketaverardi |
| HTTP **429** yoki `Too Many Requests` | `RateLimited` | hech narsa saqlanmaydi — keyingi o'zgarishda yana urinamiz |
| **HTTP 200, lekin `ok: true` EMAS** (buzuq JSON, proxy'ning HTML sahifasi) | `Failed` | ilgari yolg'on `Ok` bo'lardi: xesh saqlanar, keyingi safar so'rov umuman yuborilmas va **karta guruhda abadiy eski holatda qolardi** — logda ham hech narsa yo'q edi |
| boshqa hammasi (tarmoq, noma'lum sabab) | `Failed` | keyingi o'zgarishda yana urinamiz |

⚠️ Tartib muhim: `message is not modified` **eng oldin** tekshiriladi (429 bilan birga kelsa ham
muvaffaqiyat). `IsDead` yozuv `SyncCardAsync` so'roviga UMUMAN olinmaydi — yo'q xabarga har
o'zgarishda so'rov yuborish tezlik chegarasini yeb, haqiqiy xabarlarni kechiktirardi.

⚠️ **`migrate_to_chat_id` kelsa qatorning `ChatId` si YANGILANADI** va `IsDead` bekor qilinadi
(`ApplyEditAsync` oxiri): eski chat id ABADIY o'zgargan, u bilan qayta urinish hech qachon
ishlamasdi. Matn keyingi o'zgarishda yetkaziladi.

### Lid O'CHIRILGANDA — `MarkDeletedAsync`

Xabar **o'chirilmaydi**: Telegram `deleteMessage` 48 soatdan eski xabarga ishlamaydi, ya'ni eski
karta baribir guruhda qolib, mavjud bo'lmagan lidni ko'rsatib turardi. Shuning uchun **matni
almashtiriladi** («🗑 Lid o'chirildi» + ism + vaqt).

🔴 **YOZUV FAQAT TAHRIR TASDIQLANGANDA o'chiriladi:**

| Natija | Qator |
|---|---|
| `Ok` · `NotModified` · `Gone` (yoki allaqachon `IsDead`) | O'CHIRILADI |
| `MigrateToChatId` bor | QOLADI — yangi `ChatId` bilan, keyingi urinish uchun |
| `RateLimited` · `Failed` | **QOLADI** |
| Bot sozlanmagan (`!IsConfigured`) | funksiya darhol qaytadi — **hech narsa o'chirilmaydi** |

⚠️ NEGA: aks holda o'chirilgan lidning kartasi guruhda **ismi va TELEFONI bilan abadiy tirik
qolardi**, uni yangilaydigan yagona ip (bazadagi yozuv) esa uzilib ketardi.

⚠️ Qolib ketgan qator **yetim** bo'ladi (lidi yo'q). Alohida fon xizmati QURILMAGAN — tizim
o'zini o'zi tozalaydi: **har `MarkDeletedAsync` chaqiruvi ko'pi bilan 20 ta** (`MaxOrphanRetry`)
yetim qatorga qayta urinadi. Chegara bor, chunki ish foydalanuvchi so'rovi ICHIDA bajariladi —
«O'chirish» tugmasi bir necha soniya osilib qolmasin.

⚠️ Yetim qatorda lid ismi noma'lum (lid o'chgan) — matn ISMSIZ chiqadi. Bu maxfiylik uchun ham
yaxshi: guruhga ortiqcha ma'lumot tushmaydi.

### 🔴 HAR CHAT UCHUN DARHOL SAQLANADI (poyga himoyasi)

`NotifyNewLeadAsync` xabar yuborilgach yozuvni **o'sha zahoti** saqlaydi (`SaveOrUpdateAsync`),
bitta yakuniy `SaveChanges` EMAS. Ikkita nosozlik shundan chiqqan edi:

1. **Guruhda YETIM KARTA.** Xabar ketgan, bog'lovchi yozuv esa saqlanmagan ⇒ bazada qator yo'q,
   ya'ni u karta hech qachon tahrirlanmasdi.
2. **Ochiq lid formasi 500 qaytarardi.** `(LeadId, ChatId)` unikal; bir lidga ikki hodisa deyarli
   bir vaqtda kelsa (ommaviy forma + daraja testi) ikkinchi `Add` `DbUpdateException` bilan
   yiqiladi va EF buzuq entity'ni `ChangeTracker` da **`Added` holatida QOLDIRADI** — tashqi
   `catch` uni yutsa ham, chaqiruvchining O'Z `SaveChangesAsync`i o'sha buzuq INSERT'ni qayta
   urinar va bu safar hech kim yutmasdi.

Yechim: yiqilganda entity **DETACH** qilinadi va qator qayta o'qib **UPDATE** qilinadi (unikal
buzilish «qator allaqachon bor» degani, ya'ni mavjud qatorga yangi `message_id`/xesh yozilishi
kerak).

⚠️ Umumiy `ChangeTracker.Clear()` **QILINMAYDI** — u chaqiruvchining saqlanmagan
o'zgarishlarini ham o'chirib yuborardi. Faqat O'Z qatorlarimiz chiqariladi.

### FK — `LeadId` → `Leads.Id`, `OnDelete: Cascade`

Migratsiya **`AddLeadTelegramMessageFk`**. Lid o'chirilsa karta yozuvlari BAZA darajasida o'chadi.

NEGA: tozalash ilgari butunlay `MarkDeletedAsync` ga tayanardi, u esa xatoni JIM yutadi — yetim
qatorlar abadiy qolardi. Yetim qator zararsiz emas: `(LeadId, ChatId)` unikal bo'lgani uchun
o'sha lid id qayta ishlatilganda (import/tiklash) yangi karta insert'i `23505` bilan yiqilardi.

⚠️ Migratsiya FK'dan **OLDIN** yetim qatorlarni `DELETE` qiladi — jadval FK'siz yaratilgan edi,
ya'ni oradagi vaqtda o'chirilgan lidning kartasi bazada qolgan bo'lishi mumkin va
`ADD CONSTRAINT` prodda **`23503`** (foreign_key_violation) bilan butun deployni to'xtatardi.

⚠️ Navigatsiya xossasi ATAYIN yo'q (`HasOne<Lead>()` navigatsiyasiz shakli) — u DTO seriyalash va
`Include` xulqiga ta'sir qilishi mumkin edi.

### Qolgan qattiq qoidalar

- `parse_mode` **ATAYIN ishlatilmaydi**: foydalanuvchi izohidagi `<` yoki `&` butun xabarni
  yiqitmasin (Telegram HTML'ni qat'iy tekshiradi).
- **`reply_parameters` + `allow_sending_without_reply: true`** (xom `reply_to_message_id` EMAS):
  javob berilayotgan xabar o'chirilgan bo'lsa eski usul BUTUN so'rovni rad etardi va takroriy
  murojaat signali yo'qolardi. Endi xabar eng yomoni javobsiz bo'lib, baribir boradi.
- Sinxronizatsiya **`SaveChangesAsync()` dan KEYIN** chaqiriladi — aks holda karta ESKI ma'lumotni
  chizardi. `NotifyNewLeadAsync`/`SyncCardAsync` o'z yozuvini O'ZI saqlaydi.
- Hech biri **istisno chiqarmaydi** (ichida `try/catch`): karta CRM amalini — lid yaratish,
  bosqich ko'chirish, o'chirish — hech qachon buza olmaydi. **Yagona istisno —
  `OperationCanceledException`: u QAYTA ULOQTIRILADI**, aks holda bekor qilingan so'rov
  «muvaffaqiyat» kabi o'tib ketardi.
- **Xatolar endi JIM emas:** hamma `catch` `ILogger?` ga yozadi (`logger` — ixtiyoriy parametr).
  ⚠️ Logda `LeadId`/`ChatId`/`MessageId` bor (bular markazning O'Z identifikatorlari), **telefon,
  izoh matni, xabar matni va token esa HECH QACHON** yozilmaydi.
- Bot sozlanmagan (`TELEGRAM_BOT_TOKEN` yo'q) bo'lsa hammasi jim o'tadi.
- **`"telegram"` HttpClient** `Program.cs` da **10 sekundlik timeout** bilan ro'yxatga olingan
  (Telegram odatda 1 sekunddan tez javob beradi; uzoq kutish CRM endpointini bloklardi).
  ⚠️ **Long polling qiladigan joylar** (`TelegramService.GetUpdatesAsync`,
  `CareerTelegramService.GetUpdatesAsync`) `PollingClient(timeoutSec)` bilan **o'z nusxasining**
  timeout'ini ko'taradi (`timeoutSec + 15`). `CreateClient` har chaqiruvda yangi `HttpClient`
  qaytargani uchun bu boshqa chaqiruvlarga ta'sir qilmaydi. **Yangi long polling qo'shsangiz shu
  naqshni ishlating** — aks holda so'rov 10 sekundda uzilib, bot yangilanish ololmay qoladi.

### Chaqiruv nuqtalari

**Karta TUG'ILADIGAN joylar** (`NotifyNewLeadAsync`):

| Joy | Hodisa |
|---|---|
| `LeadsController.Create` | qo'lda kiritilgan lid |
| `LeadFormService` | ommaviy lid formasi |
| `PublicLandingController` | landing formasi |
| `LevelTestService` (2 ta) | daraja testi topshirildi (yangi lid + mavjud lidga yangi natija) |
| `MetaLeadgenService` | Meta Lead Ads (reklamadagi forma) |

**`LeadsController`** — kartani yangilaydigan **7 ta** `SyncCardAsync` + `MarkDeletedAsync`:

| Endpoint | Chaqiruv |
|---|---|
| `POST /leads` (`Create`) | `NotifyNewLeadAsync` — karta SHU YERDA tug'iladi |
| `POST /leads/{id}/send-test` (`SendTest`) | `SyncCardAsync` |
| `PUT /leads/{id}` (`Update`) | `SyncCardAsync` |
| `PATCH /leads/{id}` (`ChangeStage`) | `SyncCardAsync` |
| `POST /leads/{id}/events` (`AddEventEndpoint`) | `SyncCardAsync` |
| `POST /leads/{id}/trials` (`ScheduleTrial`) | `SyncCardAsync` |
| `PATCH /leads/trials/{trialId}` (`SetTrialResult`) | `SyncCardAsync` |
| `POST /leads/{id}/convert` (`Convert`) | `SyncCardAsync` |
| `DELETE /leads/{id}` (`Delete`) | **`MarkDeletedAsync`** (lid yo'q — `SyncCardAsync` uni topa olmaydi) |

**Boshqa modullardan** (hammasi `SaveChanges` dan KEYIN):

| Joy | Nega |
|---|---|
| `InstagramPipeline` | takroriy murojaatda `RepeatCount`, izoh va `LeadEvent` o'zgaradi |
| `InstagramController` (suhbatni lidga aylantirish / mavjud lidga bog'lash) | o'sha sabab |

⚠️ **ATAYIN QO'SHILMAGAN:**

| Joy | Nega |
|---|---|
| `LeadSourcesController.Update` (manba nomini almashtirish — barcha eski lidlarni ko'chiradi) | bitta amal yuzlab lidga tegadi; har biriga karta sinxronizatsiyasi Telegram chegarasini urardi |
| `LeadStagesController.Update` (bosqich sarlavhasi — kartadagi «📍 Bosqich» qatori) | o'sha sabab |

Kelishuv: bunday **OMMAVIY** amaldan keyin kartalar biroz eskirib turadi va **keyingi** lid
o'zgarishida o'z-o'zidan tuzaladi (matn o'zgargani uchun xesh mos kelmaydi).

⚠️ **YANGI lid endpointi qo'shsangiz — `SyncCardAsync` chaqiruvini ham qo'shing** (`SaveChanges`
dan KEYIN). Aks holda karta jimgina eskirib qoladi: nosozlik xato bermaydi, shunchaki guruhdagi
xabar haqiqatdan orqada qolib ketadi.

## SMS OQIMI (2026-09-07 qulflari) — 2026-10 da SMS kanali bilan birga OLIB TASHLANDI

Eskiz `HttpClient`, Eskiz xato logi, `{link}` qo'lda yuborish darvozasi (`ForbiddenInManual`) va
jo'natuvchi nomi tekshiruvi, Local SMS o'lik agent qoidasi — hammasi kod bilan birga ketdi (testlari:
`SmsGuardTests`, `SmsQueueTests` o'chirildi). Saqlanib qolgan saboq UMUMIY: **`CreateClient("nom")`
yozsangiz, o'sha nomni `Program.cs` da ro'yxatdan o'tkazing** — nomlanmagan konfiguratsiya jimgina
100 sekundlik timeout'ga tushadi (`"telegram"` mijozida ham aynan shu xato bo'lgan).

## QARZDORLIK ESLATMASI — HAR FAN UCHUN ALOHIDA XABAR (2026-09-09)

⚠️ 2026-10-12 dan matn `DebtMessages.BuildForStudentAsync` da (avtomatik eslatma ham, «Qarzdorlar» kampaniyasi ham
shuni chaqiradi) va jadval QOIDA bo'yicha (§0.5 «Jadvalli avto-xabarlar»).

`PaymentReminderService` (trigger `payment_debt`) o'quvchiga BITTA xabar yubormaydi: **har fan
(kurs/guruh) uchun ALOHIDA xabar** tuziladi va u kabinet tarixiga va Telegram'ga alohida ketadi
(2026-10 gacha push va SMS ham). Ikki fanda o'qiydigan o'quvchining ota-onasi ikkita eslatma oladi.

⚠️ **ONGLI kelishuv:** N fan = N xabar (markaz egasining aniq talabi). Shuning uchun tizim
matni QISQARTIRILGAN («F.I.Sh — Kurs bo'yicha qarzdorlik: summa» + bitta iltimos qatori) va
«Jami» qatori butunlay OLIB TASHLANDI — u fanlarni aralashtirib, xabarni yolg'on qilardi.

⚠️ **Token konteksti PER-GURUH:** `{qarzdorlik}` — SHU fanning qarzi (jami EMAS), `{kurs}`,
`{guruh}`, `{oqituvchi}`, `{dars_*}` — SHU xabarning guruhidan. Ilgari hammasi eskirgan
`Student.ClassName` dan olinardi (u faqat BIRINCHI guruhda yoziladi va yangilanmaydi —
`StudentMembershipView` izohi), ya'ni guruh almashtirgan o'quvchida yolg'on ma'lumot chiqardi.

⚠️ **TUGAGAN a'zoliklar HAM hisoblanadi (2026-10-07):** guruh almashtirgan o'quvchining ESKI
guruhdagi qoldiq qarzi o'sha FAN nomi bilan keladi (`StudentGroupLedger` chiqish oyida to'xtaydi).

⚠️ **ZAXIRA YO'L FAQAT a'zoligi UMUMAN YO'Q o'quvchiga** (eski `ClassName` modeli): BITTA xabar,
qarz `Student.Balance` dan. Ilgari a'zoligi bor-u faol guruhlarida qarzi chiqmagan o'quvchiga ham
ishlardi va eski guruhning qarzini YANGI guruh nomi bilan yozib yuborardi. A'zoligi bor, lekin hech
bir guruh bo'yicha qarzi chiqmagan (manfiy balans guruhga bog'lanmagan eski hisoblardan) qarzdorga
KURS NOMISIZ bitta umumiy eslatma ketadi ("<ism> bo'yicha qarzdorlik: X") — noto'g'ri guruh nomi
yolg'on bo'lardi, jim o'tkazib yuborish esa qarzni unuttirardi.

⚠️ **AUDITORIYA — har qoidaning o'zidan** (ilgari umuman o'qilmasdi, hamma qoida ota-onaga ketardi):
`parents` — ota-ona raqami birinchi; `students` — o'quvchining O'Z raqami birinchi
(`MessagingAudience.StudentPhone`, {telefon} tokeni uchun); `teachers` — SHU FAN guruhining o'qituvchisiga
(kabineti va Telegrami; o'qituvchisiz guruh/zaxira yo'lda o'tkazib yuboriladi). Har qoida o'z kanallari bilan
ALOHIDA yetkaziladi (`DispatchStudentAsync` bilan bir xil model). `custom_schedule` da ham
`students` endi o'quvchi raqamiga ketadi.

⚠️ Qarzi YO'Q fan uchun xabar TUZILMAYDI; bitta guruhda bir nechta a'zolik qatori bo'lsa ham
xabar BITTA (`MembershipLifecycle.PrimaryMembership`, tirigi bo'lmasa — eng oxirgi tugagani).

⚠️ Har fan xabari alohida (matnlar fan nomi bilan har xil). Log qatori qarzdor sonini va XABAR sonini
ALOHIDA yozadi.

Testlar: `WunderkindLC.Tests/AutoMessageTests.cs` (§7 «Qarzdorlik eslatmasi»).

## KUNLIK FON ISHLARI — "BUGUN ISHLADIMI" BAZADA (2026-10-07)

`PaymentReminderService` · `BirthdayGreetingService` · `CustomReminderService` (`TrialReminderService` —
sinov darsi SMS eslatmasi — 2026-10 da olib tashlandi; eski `trial_reminder:*` belgilari zararsiz).
Entity — `BackgroundJobRun` (`Key` PK · `LastRunDate` "yyyy-MM-dd" — concurrency token · `UpdatedAt`),
yagona yozuvchi — `JobRunMarker` (Application/Services).

🔴 **NEGA:** "bugun ishladim" belgisi XOTIRADA edi. Har deploy/restart (09:00 dan keyin) belgini
o'chirar va BUTUN ro'yxat QAYTA yuborilardi — har qarzdorga yana SMS (pul) + Telegram + push,
tug'ilgan kun va sinov darsi SMS'lari ham (o'sha paytdagi kanallar). Erkin eslatmada (`_sent`) oyna ichidagi restart xuddi shunday.

**Ikki daraja:**

| Kalit | Qachon | Nima beradi |
|---|---|---|
| `payment_debt:{ruleId}:{studentId}` · `birthday:{ruleId}:{studentId}` · `custom_schedule:{ruleId}:{s\|t}:{id}` | yuborishdan OLDIN, atomik (`TryClaimAsync`) | ish yarmida yiqilsa — olganlarga TAKROR yo'q, qolganlarga keyingi uyg'onishda yetadi; ikki nusxa poygasida faqat bittasi yuboradi |
| `payment_debt:{ruleId}` · `birthday:{ruleId}` · `custom_schedule:{ruleId}` | qoida TO'LIQ tugagach | keyingi uyg'onish (restart ham) ishni umuman qayta boshlamaydi (2026-10-12 dan QOIDA bo'yicha — har qoidaning o'z kuni/soati bor) |

- ⚠️ Belgi yuborishdan OLDIN qo'yiladi: aynan yuborish paytida yiqilish bo'lsa o'sha BITTA oluvchi
  o'tkazib yuboriladi — takror xabar (ota-ona g'ashi) dan arzon. ATAYIN shunday.
- ⚠️ Ish yarmida xato bo'lsa ish belgisi QO'YILMAYDI — keyingi soatda qayta boshlanadi (oluvchi
  belgilari takrorni ushlaydi). Ya'ni vaqtinchalik xato endi kunni butunlay o'tkazib yubormaydi.
- ⚠️ `JobRunMarker.TryClaimAsync` o'z `SaveChanges`ini chaqiradi (Application qatlamida
  `ExecuteUpdate` yo'q) — kontekstda saqlanmagan boshqa o'zgarish bo'lmasin. Shuning uchun xizmatlar
  HAR OLUVCHIDAN KEYIN saqlaydi (kabinet tarixi yiqilishda yo'qolmaydi ham).
- ⚠️ **"Band" — FAQAT haqiqiy poyga (2026-10-08).** Dispatcher o'z saqlash xatosini yutadi
  (`DispatchStudentAsync`) va kontekstda "buzuq" qatorlar qolardi; band qilishning `SaveChanges`i shu tufayli
  yiqilar va "boshqa nusxa band qildi" deb o'qilardi — qolgan HAMMA oluvchi jimgina o'tkazib yuborilib, kun
  "bajarildi" deb belgilanardi. Endi: band qilishdan OLDIN qoldiq hal qilinadi
  (`JobRunMarker.SettleLeftoversAsync` — yana bir bor saqlab ko'radi, bo'lmasa kuzatuvdan chiqaradi; asl xato
  o'z joyida loglangan), `false` faqat concurrency/PK poygasida (qator bazada SHU sana bilan bor), boshqa
  xato TASHQARIGA chiqadi — xizmat loglaydi va kunlik ish belgisi qo'yilmaydi.
  `LessonAttendanceReminderService.TryMarkAsync` ham xuddi shunday (va har yetkazishdan keyin saqlaydi).
- Oluvchi qatorlari faqat O'Z sanasida ma'noli — ish tugagach 2 kundan eskilari tozalanadi
  (`PruneAsync`), jadval o'smaydi.
- ⚠️ Har jadvalga "yuborildi" ustuni qo'shilmadi ATAYIN: bitta jadval + bitta yordamchi barcha kunlik
  xizmatlarni qoplaydi.
- **Oylik erkin eslatma 29–31-sanaga** qo'yilgan bo'lsa qisqa oyda OYNING OXIRGI KUNI yuboriladi
  (`CustomReminderService.IsScheduledDay`) — ilgari fevral/aprelda umuman o'tkazib yuborilardi.
- `LessonAttendanceReminderService` (o'qituvchiga "davomatni kiriting") o'z belgisini bazada
  saqlaydi — `LessonReminderMark` (dars kaliti bo'yicha UNIQUE), ya'ni restartdan keyin ham
  takrorlanmaydi (`journal.md`).

- ⚠️ **KALIT FORMATI O'ZGARSA — ESKI kalitlar ham "bajarildi" (2026-10-12, review).** CRM upgrade kalitlarni QOIDA
  bo'yicha qildi (`birthday:{ruleId}[:{sid}]`, `payment_debt:{ruleId}[:{sid}]`). Deploy KUNI eski xizmat ertalab
  ishlab bo'lgan bo'lsa yangi kalitlar bo'sh — shuning uchun `BirthdayGreetingService`/`PaymentReminderService` BUGUNGI
  eski kalitlarni ham o'qiydi: butun ish (`birthday` / `payment_debt`) — qoida ishga tushmaydi; oluvchi
  (`birthday:{sid}` / `payment_debt:{sid}`) — o'sha o'quvchi o'tkaziladi (`LegacyJobKey`, `LegacyRecipientKey`,
  `JobRunMarker.DoneKeysAsync`). Kalit formatini yana o'zgartirsangiz — xuddi shunday eski kalitni ham o'qing.

Yangi kunlik fon ishi yozsangiz — "bugun bajarildimi" ni xotirada SAQLAMANG, `JobRunMarker` dan
foydalaning. Testlar: `WunderkindLC.Tests/AuditFixT6Tests.cs`.

## SUPPORT — bot murojaati va support darslari (2026-10-07)

- **Bot "Adminga murojaat" xabarnomasi** (ota-ona ismi, TELEFONI, matni) faqat admin/superadmin va
  `messages.support` ruxsati bor XODIMGA ketadi (`SupportService.ReceivesBotSupport`,
  `PermissionRules.HasSection`). Ilgari botga ulangan HAR xodim (kassir, turniket operatori) olardi.
- **Javob (`POST support/threads/{chatId}/reply`)** avval Telegram'ga yuboriladi, YETIB BORGANDAGINA
  tarixga yoziladi; yetmasa 400 + sabab (bot bloklangan / tarmoq). Ilgari natija e'tiborsiz qolardi —
  javob "yuborildi" bo'lib turar, ota-ona esa hech narsa olmasdi.
- **Support darslari:** bron (o'quvchi) → o'qituvchiga, bronni bekor qilish (o'quvchi) → o'qituvchiga,
  band slotni o'chirish (o'qituvchi) → o'quvchiga BILDIRISHNOMA (`SupportService.NotifyAsync` — kabinet
  tarixi; push 2026-10 da olib tashlandi). Boshlanish vaqti o'tgan slot bron qilinmaydi (400) va ro'yxatda ko'rsatilmaydi
  (`SupportService.IsPast`).

## TELEGRAM ULANISHINI UZISH (2026-10-07, audit MSG-1)

Bot ota-ona ulanishini (`TelegramRegistration`) faqat QO'SHADI (`TelegramBotService.CompleteAsync` —
kontakt raqami o'quvchining raqamiga mos kelsa). Ilgari uni olib tashlashning yo'li yo'q edi: ota-ona
raqami tuzatilsa ham ESKI chat qarz/davomat/baho xabarlarini olaverar va o'quvchi akkaunti uchun
bir martalik kirish kodini so'ray olardi. Qoida — `TelegramLinks` (Application, `AuditFixT2Tests`).

- **Avtomatik:** o'quvchining raqamlari (o'zi, asosiy ota-ona, ota, ona) O'ZGARGANDA — admin/xodim tahriri
  (`StudentsController.Update`) — raqami HECH BIRIGA mos kelmaydigan ulanishlar o'chiriladi. ⚠️ O'qituvchi raqamni
  2026-10-12 dan UMUMAN o'zgartira olmaydi (`teacher-portal.md` §3.5 — raqam botga kirish omili). Solishtiruv `PhoneUtil.Key` (oxirgi 9 raqam):
  faqat FORMAT o'zgarishi ("+998 90…" ↔ "90…") o'zgarish EMAS.
- ⚠️ Raqami NOMA'LUM (bo'sh/qisqa) eski ulanish taxmin bilan UZILMAYDI — profilda "Raqam mos emas"
  bo'lib ko'rinadi, admin qo'lda uzadi.
- ⚠️ Faqat raqam O'ZGARGANDA tozalanadi (har saqlashda emas) — oddiy tahrir kutilmagan uzilish bermasin.
- **Qo'lda:** o'quvchi profili → «Aloqa» → «Telegram ulanishlari» → «Uzish»
  (`GET/DELETE api/admin/students/{id}/telegram-links[/{regId}]`). O'qish — `students.list` bo'limi ruxsati
  (`ReadRequiresPerm`: ota-onaning Telegram ismi va raqami); uzish — `students.list` DELETE amali
  (`AdminPerm` fe'l→amal xaritasi: DELETE = `delete`; klientda `can('students.list','delete')`).
- Har uzilish — `Student` turidagi audit qatori ("Telegram ulanishi uzildi …"), `studentId` bilan.
- Ota-ona qayta ulanishi uchun botga to'g'ri raqamni yuboradi (oddiy ro'yxatdan o'tish).
