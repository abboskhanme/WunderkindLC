---
description: Xabar tizimi — yagona AVTO-XABAR (AutoMessageRule + 13 trigger), Local SMS provider, Telegram bot (majburiy obuna, guruhga lid yuborish), xabarlar frontendi.
paths:
  - "WunderkindLC.Application/Services/AutoMessage*.cs"
  - "WunderkindLC.Application/Services/Message*.cs"
  - "WunderkindLC.Application/Services/Telegram*.cs"
  - "WunderkindLC.Application/Services/Eskiz*.cs"
  - "WunderkindLC.Application/Services/Cti*.cs"
  - "WunderkindLC.Application/Services/Fcm*.cs"
  - "WunderkindLC.Application/Services/*Reminder*.cs"
  - "WunderkindLC.Application/Services/BirthdaySmsService.cs"
  - "WunderkindLC.Application/Services/LeadNotifier.cs"
  - "WunderkindLC.Application/Services/NotificationStore.cs"
  - "WunderkindLC.Server/Controllers/MessagesController.cs"
  - "WunderkindLC.Server/Controllers/AutoMessagesController.cs"
  - "WunderkindLC.Server/Controllers/NotificationsController.cs"
  - "WunderkindLC.Server/Controllers/SmsCallbackController.cs"
  - "WunderkindLC.Client/src/pages/admin/messages/**"
  - "WunderkindLC.Client/src/components/messaging/**"
---

# Xabar tizimi qoidalari

- **GURUH CHATI — KIM O'QIY OLADI (darvoza).** Yagona qoida: `ChatService.CanUseAdminChat(role, perms)`
  (sof funksiya, `ChatAccessTests` bilan qoplangan). Sabab: `AdminPermAttribute` xodim (staff) uchun
  BARCHA GET'larni ruxsat tekshirmasdan o'tkazadi (bo'limlararo o'qish uchun ataylab) — chat esa
  bo'limlararo ma'lumot EMAS, shuning uchun `MessagesController` chat endpointlarida ALOHIDA
  tekshiriladi (`CanUseChat()`): `chat/{className}` GET+POST va `classes` → 403 (`Forbid`),
  `last-messages` → BO'SH ro'yxat (403 emas: uni har sahifada `unread-context` chaqiradi).
  • admin/superadmin — cheklovsiz, barcha guruhlar + `__xodimlar__` (o'zgarmagan);
  • **staff — faqat "messages" bo'lim ruxsati bilan** (yalang `messages` yoki `messages:amal`) —
    frontend'dagi `RequirePerm perm="messages.broadcast"` bilan AYNAN bir xil, shuning uchun UI oqimi
    o'zgarmaydi; ruxsati bo'lgan xodim admin kabi hamma kanalni ko'radi (a'zolik tekshirilmaydi —
    xodim guruhga biriktirilmaydi, `ClassNamesForUserAsync` "staff" uchun bo'sh qaytaradi);
  • o'qituvchi/o'quvchi bu endpointga umuman kirmaydi — ular o'z portalidan A'ZOLIK tekshiruvi
    bilan kiradi (`ChatService.CanAccessAsync` — `TeacherPortalController`/o'quvchi portali).
  ⚠️ Yangi chat endpointi qo'shilsa — shu darvozani ham qo'shish SHART.

- **PUSH VA O'QUVCHI ILOVASI — BITTA AKKAUNT, BITTA QURILMA.** O'quvchi ilovasidan O'QUVCHI ham,
  OTA-ONA ham foydalanadi (ota-ona uchun alohida ilova YO'Q). Shuning uchun:
  • `POST/DELETE /api/student/notifications/register` — `student` VA `parent` rollariga ochiq;
  • ota-onaning qurilma tokeni FARZANDINING `Student.UserId` iga bog'lanadi
    (`StudentPortalController.NotificationUserIdAsync`: parent → `TargetAsync(null)?.UserId`).
    Shu sabab push YUBORISH mantig'i o'zgarmaydi (u har doim `Student.UserId` ga yuboradi) va
    bildirishnoma TARIXI ham ota-onada ko'rinadi (ilgari bo'sh chiqardi — ro'yxat login qilgan
    foydalanuvchi id'si bo'yicha filtrlanardi);
  • **ro'yxatdan o'tkazishda shu akkauntning BOSHQA tokenlari O'CHIRILADI** — push faqat ENG
    OXIRGI kirilgan qurilmaga boradi (aks holda o'quvchi va ota-ona telefoniga bir xil xabar
    ikki marta ketardi);
  • Admin "Tanlab push" ro'yxatida o'quvchi akkaunti **"O'quvchi / ota-ona"** deb ko'rsatiladi
    (ilgari xato "Ota-ona" deb yozilgan edi) — ALOHIDA ota-ona oluvchisi yo'q va kerak emas.
  ⚠️ Ilova tomonda FCM: `ilova/student/lib/services/push.dart` (o'qituvchi ilovasidagi bilan bir
  xil naqsh). Ilgari ilovada FCM kodi UMUMAN yo'q edi — token serverga bormas, push ishlamasdi.

- **KALITLAR .env DA:** Telegram bot tokeni (`TELEGRAM_BOT_TOKEN`), FCM service account
  (`FCM_SERVICE_ACCOUNT_JSON`), Eskiz login/paroli (`ESKIZ_EMAIL`/`ESKIZ_PASSWORD`) — hammasi
  `AppSecrets` orqali .env dan o'qiladi, bazada saqlanmaydi va Sozlamalar sahifasidan
  kiritilmaydi (sahifada faqat holat + kerakli .env qatori — `EnvSecretField`). UI'dan
  saqlanadigan qismlar: bot username/nomi, kanal, telefon moslash, Eskiz "sender" (From),
  Firebase WEB config + VAPID (ommaviy). Eskiz Bearer tokeni endi XOTIRADA keshlanadi
  (`EskizService`), `CenterMeta.EskizToken` ustuni yo'q. Batafsil: CLAUDE.md §4 "KALITLAR".

- **Yagona AVTO-XABAR tizimi:** `AutoMessageRule` entity (migratsiya `AddAutoMessages`) — har qoida:
  Trigger + 3 kanal bayrog'i (SendSms/SendPush/SendTelegram) + Audience + Template (tokenli) + jadval
  maydonlari. **13 trigger** katalogi `AutoMessageTriggers` (Application/Services): payment_received,
  monthly_charge, payment_debt, attendance_absent, birthday, student_added, lead_new, trial_reminder,
  test_link, test_result, lesson_attendance, custom_schedule, **grade_entered** (baho qo'yilganda).
  Har trigger `Category` maydoniga ega ("Lidlar" | "O'quv jarayoni" | "Moliya" | "Boshqa") —
  GET /triggers javobida `category` chiqadi, frontend shu bo'yicha guruhlaydi. Markaziy dispatcher
  `AutoMessageService` (singleton) — DispatchStudent/LeadAsync (SMS=Eskiz+SmsLog,
  Push=FCM+DeviceTokens+NotificationStore, Telegram=TelegramRegistrations). `DispatchTeacherAsync`
  O'CHIRILGAN (chaqiruvchi yo'q edi).
  **`grade_entered`:** `JournalService.SetEntryAsync` ixtiyoriy `AutoMessageService?` parametr oladi —
  baho push'i qoida MAVJUD bo'lsa dispatcher orqali (extra {baho},{sana},{guruh}); qoida yo'q bo'lsa eski
  to'g'ridan-to'g'ri FCM push (default-on). Davomat push'i (type "attendance") o'zgarmagan.
  **Token katalogi:** `MessageTokenCatalog` + `GET /api/admin/auto-messages/tokens` →
  `[{token,label,group}]` (group: student|lead|common|event); frontend token chiplarini shundan oladi
  (lokal `messageTemplates.ts` faqat fallback).
  **`{oqituvchi}` — GURUH O'QITUVCHISI F.I.Sh:** `MessageTokenizer.Student/Lead` ixtiyoriy
  `teacherName` parametridan to'ladi (`Teacher(...)`da — o'qituvchining o'zi). Chaqiruvchi nomni
  `MessageTokenizer.GroupTeacherNameAsync(db, group, student)` (bitta xabar) yoki
  `TeacherNamesByIdAsync` + `TeacherNameOf(group, names)` (ro'yxat — N+1 bo'lmasin) orqali beradi.
  Guruh KONTEKSTI ({guruh}/{oqituvchi}/{dars_*}) — hodisa guruhi: `DispatchStudentAsync(..., group:)`
  ga student_added (guruhga qo'shish + yangi o'quvchi), grade_entered va attendance_absent
  (`DispatchAttendanceAbsentAsync(..., group)`) ham guruhni UZATADI; berilmasa o'quvchining asosiy
  (ClassName) guruhi olinadi. `PaymentReminderService`/`CustomReminderService` ommaviy sikllarda
  guruh+o'qituvchi lug'ati BIR MARTA yuklanadi (ilgari bu ikkisida {dars_*} bo'sh chiqardi).
  ⚠️ `PaymentReminderService` da guruh konteksti endi HAR XABARNING O'Z guruhidan olinadi
  (pastdagi «QARZDORLIK ESLATMASI — HAR FAN ALOHIDA»), `ClassName` yorlig'i esa faqat a'zoligi
  BO'LMAGAN o'quvchining zaxira yo'lida ishlatiladi.
  **TOZALANGAN (migratsiya `MessagingCleanup`):** `ReminderRule` entity+DbSet, `RemindersController`
  (`api/admin/reminders`), `ReminderTriggers`, `SmsTemplate.Trigger/IsAuto` ustunlari, Program.cs bir
  martalik seed bloki — hammasi O'CHIRILGAN (avto-xabar to'liq AutoMessageRule'da). `SmsTemplate` faqat
  qo'lda shablon (`{id,name,text,order}`). 3 reminder HostedService AutoMessageRules'dan o'qiydi.
  API: `api/admin/auto-messages` (+ /triggers + /tokens), AdminPerm("messages.broadcast").

- **GURUHI YOPILGAN/TUGATILGAN O'QUVCHIGA AVTO-XABAR YO'Q** (`MessagingAudience.ClosedGroupStudentIdsAsync`):
  o'quvchining a'zoligi BOR-u, biror ham TIRIK a'zoligi (`StudentGroup.IsActive` + guruh ARXIVLANMAGAN)
  qolmagan bo'lsa — tizim o'zi boshlaydigan xabarlar unga yuborilmaydi: qarzdorlik eslatmasi
  (`PaymentReminderService`), tug'ilgan kun (`BirthdaySmsService`), erkin/jadvalli eslatma
  (`CustomReminderService`), ommaviy SMS/e'lon/push (`MessagesController` — "Tanlangan" rejimidan
  TASHQARI: u yerda admin kimni tanlasa o'shanga ketadi). Muzlatilgan, lekin guruhi FAOL o'quvchi
  xabar olaveradi (ta'til). A'zoligi umuman yo'q (eski ClassName) o'quvchilar tegilmaydi.
  Hodisaga JAVOB bo'lgan xabarlar (masalan "to'lov qabul qilindi") filtrlanmaydi — yopilgan guruh
  qarzini to'lagan ota-ona tasdiq oladi. O'qituvchiga dars eslatmasi allaqachon arxiv guruhlarni
  hisobga olmasdi (`LessonAttendanceReminderService` — `!g.IsArchived`).

- **Lidlarga ommaviy SMS:** `POST /api/admin/messages/sms/lead-bulk` `{leadIds:string[], text}` →
  `{sent,failed,noPhone}` (bitta SmsBatch, har lidga LeadEvent; `SendOneLeadSmsAsync` helper — sms/lead
  bilan umumiy).

- **FRONTEND:** Xabarlar sahifasi 3 tab — **Xabar yuborish (default)** | Avto xabarlar (trigger'lar
  Category bo'yicha guruhlangan) | Tarix (3 kanal + kanal filtri chiplari).
  ⚠️ **Guruh chati bu yerda EMAS:** u alohida «Chats» menyusiga chiqarilgan (menyuda «Xabarlar» dan
  TEPADA) — `/admin/chats` (`pages/admin/chats/GroupChatPage.tsx`) va yonida «Support Telegram»
  (`/admin/support-telegram`). Sidebar'dagi o'qilmagan xabar belgisi ham shu «Chats» guruhida. Yagona
  `MessageEditor` komponenti (`components/messaging/`) — token+shablon chiplar + to'g'ri SMS uzunlik
  hisobi — composer, SmsModal, RuleCard, LeadDetailModal, MessageTemplateLibrary hammasi ishlatadi.
  Kanal tartibi/yorliqlari yagona `config/channels.ts`dan (SMS → Telegram → Push). Lidlar Kanban'da
  "SMS yuborish" (bulk modal). O'quvchi detail sahifasida "SMS yuborish" tugmasi (SmsModal bitta
  o'quvchi rejimi). **`SmsModal` KO'P OLUVCHI rejimi (guruh sahifasi, o'quvchilar ro'yxati,
  davomat):** oluvchi filtri — Holat chiplari (Aktiv/Sinov/Muzlatilgan, `SmsRecipient.status`
  yoki Student'ning `memberState`i) + To'lov (Faqat qarzdorlar / Qarzi yo'q, `SmsRecipient.balance`
  manfiy = qarz) + har bir oluvchini qo'lda belgilash. Qarzdorlar MIJOZ tomonida filtrlanadi
  (`onlyDebtors:false` yuboriladi) — guruhda balans SHU GURUH bo'yicha (`GroupMember.balance`,
  GroupBalanceService), serverdagi `OnlyDebtors` esa umumiy `Student.Balance`ga qaraydi. Sozlamalarda "Xabar kanallari" bitta bo'lim (`/admin/settings/channels`, 3 ichki tab:
  SMS(Eskiz) / Telegram bot / Push(Firebase)); Telegram backup → `/admin/settings/backup`, APK yuklash →
  `/admin/settings/apk`; eski telegram/firebase/eskiz section'lari redirect, "Eslatmalar" section
  o'chirilgan (→ /admin/messages).

- **Local SMS (Eskiz'ga muqobil provider — CTI agent telefonining SIM-kartasidan):** har SMS yuborish
  joyida (Xabar yuborish, Lidga SMS, Lidlar ommaviy SMS, Avto-xabar qoidalari) "Eskiz" bilan bir qatorda
  "Local" tanlanishi mumkin (`SmsProviderPicker`, `components/messaging/`) — faqat
  `CenterMeta.LocalSmsEnabled` yoqilgan bo'lsa ko'rinadi (Sozlamalar → Xabar kanallari → SMS,
  `LocalSmsSettings.tsx`). Standart agent `CenterMeta.LocalSmsDefaultAgentId` — interaktiv yuborishda
  o'zgartirish mumkin, avtomatik/fon xabarlarda (AutoMessageService, 3 ta eslatma HostedService) HAR
  DOIM shu standart ishlatiladi (tanlaydigan admin yo'q). Markaziy gateway: `CtiSmsService` — WS/FCM+poll
  yetkazish (dial bilan bir xil oqim) + `SmsLog`/`SmsBatch` yozish (`Provider="local"`); chaqiruvchi
  batchId bermasa (masalan Local Call sahifasidan ad-hoc yuborish) o'zi bittalik `SmsBatch` yaratadi —
  shu bilan HAR QANDAY Local SMS umumiy "Xabarlar → Tarix"da Eskiz bilan bir joyda, "Manba" belgisi
  bilan ko'rinadi (`HistoryTab.tsx`, filtr chiplari bilan). `CtiConnectionManager` (avval Server/Cti/)
  Application qatlamiga ko'chirilgan — sababi: AutoMessageService kabi Application xizmatlari ham Local
  SMS yubora olishi kerak (Server'dan Application'ga bog'liqlik yo'nalishi noto'g'ri bo'lardi).
  `AutoMessageRule.SmsProvider` — har qoida mustaqil Eskiz/Local tanlaydi (`AutoMessageSmsSender` —
  umumiy yordamchi, provider bo'yicha branch qiladi, `SmsLog`/`SmsBatch` yozadi).

- **OMMAVIY SMS — SO'ROV ICHIDA EMAS, FONDA** (`SmsQueueService`, Application/Services; singleton +
  `AddHostedService`). SMS'lar bittalab ketadi: Eskiz'da har raqamga alohida HTTP so'rov (~0.5–1.5 s),
  Local'da esa yuborishlar orasida `LocalSmsDelaySeconds` kutish (agent oflayn bo'lsa yana ~6 s
  "uyg'otish"). 100 oluvchi = bir necha daqiqa, **Cloudflare Tunnel esa javobni 100 soniya kutadi** va
  uzadi → brauzerda "Yuborishda xatolik", SMS'lar esa aslida ketayotgan bo'ladi.
  • Controller (`MessagesController.StartBatchAsync` — `sms/send`, `sms/lead`, `sms/lead-bulk` uchun
    YAGONA joy) oluvchilar ro'yxatini yig'adi, `SmsBatch` ni DARHOL yozadi (tarixda o'sha zahoti
    ko'rinadi) va navbatga qo'yadi;
  • `InlineLimit` (3) gacha bo'lgan partiya avvalgidek so'rov ichida ketadi — bitta o'quvchi/lidga
    yuborishda admin natijani darhol ko'radi;
  • ⚠️ **HAR SMS'dan keyin saqlanadi** (`SmsLog` + `SmsBatch.SentCount`). Ilgari barcha `SmsLog`
    faqat siklning OXIRIDA saqlanardi: ulanish uzilsa (`HttpContext.RequestAborted`) sikl o'lar,
    **pul ketgan, SMS borgan, tarix esa bo'sh** qolardi — admin xatoni ko'rib qayta yuborardi;
  • holat: `GET sms/{id}/progress` — avval xotiradagi navbatdan, u yerda bo'lmasa (ilova qayta ishga
    tushgan) bazadagi `SmsLog` sonidan tiklanadi. Frontendda `watchSmsProgress` (har 2 s) — modal
    "Yuborilmoqda: 12/300" deb ko'rsatadi va oyna yopilsa ham yuborish davom etadi;
  • lid tarixi (`LeadEvent`) ham navbatda yoziladi — `Job.LeadNote` + `Target.LeadId`.
  Testlar: `WunderkindLC.Tests/SmsQueueTests.cs`.

- **To'lov eslatmasi:** mavjud `MessagesController` broadcast `OnlyDebtors=true` (Telegram +
  `{qarzdorlik}` tokenlari). Avtomatik (hisob yaratilganda) trigger — hali yo'q (kelajak).

- **Lidni Telegram guruhga avto-yuborish:** Bot GURUHGA qo'shilsa (`my_chat_member` yangilanishi —
  `TelegramService.GetUpdatesAsync` allowed_updates'ga qo'shilgan) — `TelegramGroup` entity'ga yoziladi
  (migratsiya `AddTelegramGroups`, ChatId unikal, IsActive), guruhga bir marta tasdiq yuboriladi;
  chiqarilsa IsActive=false. Yangi lid tushganda `LeadNotifier.NotifyNewLeadAsync` adminlarga (private
  reg) VA barcha faol guruhlarga bir xil matnni yuboradi (dedupe: `sentChats`). Handler:
  `TelegramBotService.HandleMyChatMemberAsync`. Guruhda `/start` yo'q — faqat qo'shilish kifoya.
  ⚠️ Yuborilgan xabar — **KARTA**: keyin u yangi xabar bilan emas, TAHRIR bilan yangilanadi
  (quyidagi «LID KARTASI» bo'limi).

- **BOTDA MAJBURIY OBUNA:** yagona darvoza `TelegramBotService.RequireSubscriptionAsync` — `/start`,
  telefon yuborish, `/kod` (kirish kodi) va `/test` (onlayn test) shu yerdan o'tadi (5 daqiqa
  keshlanadi); «Adminga murojaat» ATAYIN ochiq. DIQQAT: Telegram `getChatMember` faqat bot kanalda
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
murojaat, izohlar, «O'quvchi bo'ldi», oxirgi test natijasi) va lid har o'zgarganda o'sha xabar
`editMessageText` bilan **joyida tahrirlanadi** — yangi xabar YUBORILMAYDI.
Ilgari faqat lid TUG'ILGANDAGI xabar turardi va u bir kunda eskirardi; har o'zgarishga yangi xabar
yuborish esa guruhni «o'zgardi / yana o'zgardi» oqimiga aylantirardi.

**Oluvchilar:** faol GURUH(lar) + **FAQAT superadmin**(lar)ning shaxsiy chati
(`LeadNotifier.ShouldNotify` — oddiy admin/xodimga shaxsiy xabar ketmaydi).

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
| `SmsQueueService` | yuborilgan SMS lidga `note` hodisasi bo'lib tushadi. ⚠️ `TelegramService` konstruktorga EMAS, `services.GetService<TelegramService>()` orqali olinadi (singleton; sozlanmagan bo'lsa `null` qaytadi va SMS oqimi o'zgarishsiz davom etadi) |

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

## SMS OQIMI — 2026-09-07 prod hodisalaridan chiqqan QULFLAR

Bir kunda to'rtta ayrim muammo topildi; ularning uchtasi **jimgina** ishlab turardi
(hech qanday xato ko'rinmasdi). Qoida bitta: *"SMS ketmadi" ham, "noto'g'ri ketdi" ham
LOGDA yoki EKRANDA sababi bilan ko'rinishi kerak.*

### 1. NOMLI HttpClient — `AddHttpClient("eskiz")` (100 → 25 sekund)

⚠️ `EskizService` `httpFactory.CreateClient("eskiz")` deb so'raydi, lekin bu nom
`Program.cs` da **ro'yxatdan o'tmagan** edi → default `HttpClient.Timeout` = **100 sekund**.

Hodisa: 16:51–16:56 da Eskiz API'si ~5 daqiqa javob bermay qoldi. Har "SMS yuborish"
bosilishi 100 sekund osildi, operator 7 marta bosdi — admin oynasi 12 daqiqa qotdi.
Eskiz o'zi odatda 0.5–1.5 sekundda javob beradi.

⚠️ **AYNAN SHU XATO `"telegram"` mijozida ham bo'lgan va tuzatilgan edi** — ya'ni naqsh
takrorlandi. **`CreateClient("nom")` yozsangiz, o'sha nomni `Program.cs` da ro'yxatdan
o'tkazing.** Nomlanmagan konfiguratsiya jimgina 100 sekundga tushadi.

### 2. Eskiz XATO javoblari LOGGA yoziladi

Ilgari HTTP xato javoblari (400/401/402) faqat `SmsLogs.Status` ga tushardi — log'da
hech narsa yo'q edi. Endi `TrySendAsync` status + javob tanasini `LogWarning` qiladi
(Eskiz'ning ANIQ sababi o'sha yerda: balans, moderatsiya, tasdiqlanmagan sender).

⚠️ **Telefon RAQAMI logga yozilmaydi** — log'da shaxsiy ma'lumot saqlanmaydi.

⚠️ **TIMEOUT alohida ajratildi.** `TaskCanceledException` ni umumiy "Yuborishda xatolik"
deb ko'rsatish operatorni chalg'itardi: u Eskiz **rad etdi** deb o'ylab matnni/raqamni
qayta-qayta o'zgartirardi. Aslida javob **kelmagan**. Endi matn: *"Eskiz javob bermadi
(vaqt tugadi). Bu rad etish EMAS."* Chaqiruvchi bekor qilgani (`ct`) esa xato emas — u
qayta otiladi.

### 3. `{link}` — QO'LDA yuborib bo'lmaydigan token

`{link}` — daraja testining **bir martalik** havolasi; uni faqat "Daraja testi yuborish"
oqimi (`POST leads/{id}/send-test`) tug'diradi.

⚠️ Hodisa: lid oynasining "tayyor matn" ro'yxatida `test_link` andozasi ham chiqardi.
Operator uni tanlab oddiy "SMS yuborish" bosdi — abonentga
*«Assalomu alaykum sizga {link} testi yuborildi»* KETDI. Hech qanday xato ko'rinmadi:
SMS muvaffaqiyatli yuborilgan edi.

Ikki qatlam:
- **Klient** — `NOT_PICKABLE_TRIGGERS` (`api/services/messages.ts`): `test_link` "tayyor
  matn" ro'yxatiga tushmaydi.
- **Server (haqiqiy darvoza)** — `MessageTokenCatalog.ForbiddenInManual`, `sms/lead` va
  `sms/lead-bulk` da 400. ⚠️ Klient filtri YETARLI EMAS: matnni qo'lda yozish/nusxalash
  mumkin.

⚠️ Ro'yxat (`ManualForbidden`) ATAYIN faqat `{link}`. Qolgan `"event"` tokenlari
bloklanmaydi — masalan `{dars_vaqti}` ni `MessageTokenizer` qo'lda yuborishda ham guruh
jadvalidan to'ldira oladi.

### 4. Jo'natuvchi nomi (sender) — NAMUNA matn sender bo'lib qolmasin

⚠️ Prodda `CenterMeta.EskizFrom` da **"tasdiqlangan_nikname"** — o'rniga ism yozilishi
kerak bo'lgan namuna matn turgan. SMS baribir ketardi (Eskiz `4546` dan yuboradi), lekin
markaz nomi ko'rinmasdi va buni **hech narsa ko'rsatmasdi**: maydon sof erkin matn edi.

- `EskizService.IsPlaceholderSender` — bunday qiymat "kiritilmagan" deb hisoblanadi
  (`SenderOf` → `4546`), saqlashda esa 400 bilan rad etiladi.
  ⚠️ Ro'yxat ATAYIN qisqa va aniq — haqiqiy markaz nomini tasodifan rad etmasin.
- `EskizService.GetNicknamesAsync` (`GET /api/nick/me`) — kabinetda **TASDIQLANGAN**
  nomlar. Sozlamalar sahifasi ularni tugma qilib ko'rsatadi; bittasi ham bo'lmasa
  ogohlantiradi, hozirgi qiymat ro'yxatda bo'lmasa qizil yozadi.
- `4546` — Eskiz'ning umumiy raqami: tasdiq talab qilmaydi, har doim ishlaydi, lekin
  abonent markaz nomini ko'rmaydi.

### 5. LOCAL (CTI agent) SMS — o'lik agentga urinmaymiz

⚠️ Ikkala CTI agent ham **3 haftadan beri** oflayn edi, lekin `SmsProvider='local'`
avto-qoidalar har kuni ishlayverardi. Har urinish: bekor FCM push + **6 sekund** kutish
(12 × 500 ms poll) + "yetkazilmadi". Kuniga 20+ marta, log'da esa **hech narsa**.

`CtiSmsService.StaleAgentAfter = 7 kun`: agent shu muddatdan beri ko'rinmagan bo'lsa FCM
bilan uyg'otishga urinilmaydi — darhol sabab bilan qaytadi va **`LogWarning` yoziladi**.

⚠️ Chegara ATAYIN uzoq: FCM yo'li aynan "ilova fonda" holati uchun. Bir necha soat
ko'rinmagan agent — normal holat, uni o'tkazib yuborsak ISHLAYOTGAN sozlamani buzardik.

⚠️ `LastSeenAt == null` stale HISOBLANMAYDI — bu yangi o'rnatilgan agentning birinchi
uyg'otilishi bo'lishi mumkin. Stale = *"ko'rgan edik, lekin ancha oldin"*.

Testlar: `WunderkindLC.Tests/SmsGuardTests.cs`.

## QARZDORLIK ESLATMASI — HAR FAN UCHUN ALOHIDA XABAR (2026-09-09)

`PaymentReminderService` (trigger `payment_debt`) o'quvchiga BITTA xabar yubormaydi: **har fan
(kurs/guruh) uchun ALOHIDA xabar** tuziladi va u BARCHA kanallarga (ilova tarixi, Telegram, push,
SMS) alohida ketadi. Ikki fanda o'qiydigan o'quvchining ota-onasi ikkita eslatma oladi.

⚠️ **NARX — ONGLI kelishuv:** N fan = N SMS (markaz egasining aniq talabi). Shuning uchun tizim
matni QISQARTIRILGAN («F.I.Sh — Kurs bo'yicha qarzdorlik: summa» + bitta iltimos qatori) va
«Jami» qatori butunlay OLIB TASHLANDI — u fanlarni aralashtirib, xabarni yolg'on qilardi.

⚠️ **Token konteksti PER-GURUH:** `{qarzdorlik}` — SHU fanning qarzi (jami EMAS), `{kurs}`,
`{guruh}`, `{oqituvchi}`, `{dars_*}` — SHU xabarning guruhidan. Ilgari hammasi eskirgan
`Student.ClassName` dan olinardi (u faqat BIRINCHI guruhda yoziladi va yangilanmaydi —
`StudentMembershipView` izohi), ya'ni guruh almashtirgan o'quvchida yolg'on ma'lumot chiqardi.

⚠️ **ZAXIRA YO'L o'zgarmagan:** a'zoligi yo'q (eski `ClassName` modeli) qarzdorga avvalgidek
BITTA xabar — qarz `Student.Balance` dan. U yerda fan kesimi umuman ma'lum emas.

⚠️ Qarzi YO'Q fan uchun xabar TUZILMAYDI; bitta guruhda bir nechta a'zolik qatori bo'lsa ham
xabar BITTA (`MembershipLifecycle.PrimaryMembership`).

⚠️ Yuborish yo'lida SMS **dedupe YO'Q** (`SmsLog` faqat jurnal, `AutoMessageSmsSender` har
chaqiruvda yangi `SmsBatch` yozadi) — bir raqamga ketma-ket ikki SMS yutilmaydi. Matnlar ham fan
nomi bilan har xil. Log qatori qarzdor sonini va XABAR sonini ALOHIDA yozadi.

Testlar: `WunderkindLC.Tests/AutoMessageTests.cs` (§7 «Qarzdorlik eslatmasi»).
