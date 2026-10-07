---
description: CRM — lidlar, lid manbasi ma'lumotnomasi, lidda tashqi maktab, sinov darsi va konversiya.
paths:
  - "WunderkindLC.Server/Controllers/Lead*.cs"
  - "WunderkindLC.Application/Services/LeadNotifier.cs"
  - "WunderkindLC.Application/Services/TrialReminderService.cs"
  - "WunderkindLC.Application/Services/LeadLoss*.cs"
  - "WunderkindLC.Client/src/pages/admin/leads/**"
  - "WunderkindLC.Client/src/pages/admin/marketing/**"
---

# CRM / lidlar qoidalari

- **CRM:** `Lead`(Source/InterestSubject/CreatedAt/ConvertedStudentId), `LeadEvent`(tarix),
  `TrialLesson`(sinov). Endpointlar `LeadsController`da: events, trials, `/{id}/convert`, `/stats`.

- **QO'LDA lid kiritishda qaysi maydon MAJBURIY ekanini markaz o'zi belgilaydi** —
  «Lid kiritish formasi» (`/admin/forms/lid-kiritish`, entity `LeadEntryField`,
  `Lead.AnswersJson`). Tekshiruv AYNAN `POST/PUT /api/admin/leads` da; ommaviy forma, daraja
  testi, landing, Instagram va Meta leadgen unga BO'YSUNMAYDI. `Lead` ga yangi maydon
  qo'shsangiz — uni `LeadEntryRules.Standard` katalogiga ham qo'shish kerakmi degan savolga
  javob bering. Batafsil: `.claude/rules/lead-entry-form.md`.

- **`Lead.PhoneKey`** (migratsiya `AddLeadPhoneKeyAndRepeat`) — telefonning oxirgi 9 raqami,
  INDEKSLANGAN. "Shu telefon bilan lid bormi?" (`LeadIntake.FindByPhoneAsync` — ommaviy forma va
  daraja testi har murojaatda so'raydi) endi bitta SQL so'rovi; ilgari butun `Leads` jadvali
  xotiraga o'qilardi. ⚠️ **Qo'lda to'ldirilmaydi** — `AppDbContext.SaveChanges` (`SyncLeadPhoneKeys`)
  uni `Phone` dan o'zi yozadi, ya'ni lid yaratadigan/telefonini o'zgartiradigan YANGI joy
  qo'shilganda ham unutilmaydi (unutilsa — o'sha lidga dublikat ochilardi).

- **Lid o'zgarishi TELEGRAM KARTASIDA aks etadi:** guruhdagi lid xabari — karta, u yangi xabar
  bilan emas, `editMessageText` bilan JOYIDA yangilanadi (`LeadNotifier.SyncCardAsync`, har
  `SaveChanges` dan KEYIN). ⚠️ Kartasi YO'Q eski lidga karta YARATILMAYDI. Yangi lid endpointi
  qo'shsangiz — sinxronizatsiya chaqiruvini ham qo'shing.
  ⚠️ **GURUHGA IZOH MATNI CHIQMAYDI** — u yerda faqat sanoq («💬 3 ta izoh»), to'liq matn esa
  superadminning shaxsiy chatidagi kartada (menejer izohi ichki ma'lumot).
  ⚠️ Karta faqat **FAOL** chatga boradi: yozuvlar `TelegramGroups.IsActive` va
  `TelegramRegistrations` bilan kesishtiriladi; o'chirilgan guruh kartani olmaydi, lekin qator
  `IsDead` deb belgilanmaydi (guruh qayta yoqilishi mumkin).
  Batafsil: `.claude/rules/messaging.md` → «LID KARTASI».

- **`Lead.RepeatCount` / `LastRepeatAt`** — TAKRORIY murojaat (odam formani/daraja testini yana
  to'ldirdi). Bosqich ATAYIN o'zgarmaydi, belgisi kanban kartasida «Takroriy ×N» chipi va lid
  oynasida alohida qator bo'lib chiqadi. Batafsil: `.claude/rules/lead-forms.md` §4.

- **«Birinchi darsga yozilganlar»** (`/admin/leads/birinchi-dars`, `GET /api/admin/leads/first-lesson`,
  ruxsat `leads.list`): ochiq lid + natijasi `pending` sinov darsi — bosh sahifadagi "Birinchi darsga
  keladiganlar" bilan BITTA ta'rif (`LeadFirstLesson` ↔ `DashboardSummary.FirstLessonLeads`;
  `LeadFirstLessonTests` "kartochka soni == qizil bo'lmagan qatorlar" ni qulflaydi). Sanasi o'tgan,
  belgilanmagan sinov ro'yxatda QOLADI (qizil `#FFCACA`). Ikkinchi ta'rif YARATMANG.

- **Lid bosilsa — lid SAHIFASI** `/admin/leads/:id` (edutizim `/orders/info/:id`); eski `?lead=<id>`
  havolalari shu sahifaga yo'naltiriladi. `/admin/leads` standart ko'rinishi — jadval, `?view=kanban` — taxta.

- **Lid manbasi ma'lumotnoma** (migratsiya `AddLeadSources`): `LeadSource`(Id,Name,Order) entity +
  `LeadSourcesController` (`api/admin/lead-sources`, AdminPerm "settings" — GET barcha xodimga ochiq).
  Boshqariladi: "O'quv bo'limi → Sabablar" sahifasi (`ReasonsPage` uchinchi karta). `Lead.Source` — manba
  NOMI (matn); manba nomi o'zgartirilsa server eski lidlarni ham ko'chiradi. Lid formasi select'i
  serverdan (`config/constants.ts` `leadSourceOptions` faqat FALLBACK), Lidlar sahifasida "Manba" filtri
  ("Noma'lum" = bo'sh source). Migratsiya mavjud `Leads.Source` qiymatlarini + 6 standart manbani seed
  qiladi.

- **Qiziqqan fani = KURS** (`Lead.InterestSubject` kurs NOMINI saqlaydi — `LevelTestService` bilan bir
  xil konvensiya): lid formasidagi maydon endi erkin matn emas, `GET /api/admin/leads/courses`
  (Subject nomlari; kurslar `schedule` ruxsatida bo'lgani uchun leads ruxsati ostida alohida endpoint)
  dan keladigan SELECT. Ro'yxatda yo'q eski/landing qiymati variant sifatida saqlanadi. Kurs nomi
  o'zgartirilsa `SubjectsController.Update` eski nomli lidlarni ham ko'chiradi (`LeadSource` kabi).
  **CRM statistikasi** (`/stats` → `CrmStatsDto.ByInterest`, `CrmInterestStatDto`): fan bo'yicha lid
  soni + aylantirilgan + konversiya %; normalizatsiya — kurs id → nomi, registr farqisiz kurs nomiga
  moslash, bo'sh = "Ko'rsatilmagan". UI: `CrmStatsPage` — gorizontal bar (top 10) + to'liq jadval.

- **Tuman/maktab — OLIB TASHLANGAN (2026-09-29, mijoz: "umuman qolmasin").** Lid va o'quvchi
  formalarida, filtrlarda, konversiyada, «Lid kiritish formasi» katalogida va Sozlamalarda YO'Q;
  `DistrictsController` va `settings.districts` kaliti o'chirilgan. `Lead`/`Student` dagi
  `DistrictId`/`SchoolId` ustunlari va `Districts`/`Schools` jadvallari bazada faqat ma'lumot
  saqlanib turishi uchun qolgan — yangi kod ularni ISHLATMASIN. Lidlar ichidagi qidiruv:
  ism/telefon/ota-ona/manba (telefon raqamlar bo'yicha).

## SOTUV BO'LIMI ANALITIKASI (2026-08-19)

Savol: **«kim qaysi bosqichgacha nechta lidni olib bordi va qanday sotmoqda»**. Hisob-kitob
`LeadAnalytics` da (sof funksiyalar, `LeadSalesAnalyticsTests`), endpoint
`GET /api/admin/leads/analytics?from&to`, UI — `CrmStatsPage`.

### 1. Menejer qatori (`LeadManagerRowDto`)

| Maydon | Ma'nosi |
|---|---|
| `Moves` | bosqich ko'chirishlar soni (faollik) |
| `Leads` | nechta HAR XIL lid bilan ishlagani (kiritgan yoki ko'chirgan) |
| `Created` | shundan nechtasini O'ZI kiritgan |
| `Won` | o'quvchiga aylantirgan |
| `Paid` / `Revenue` | shulardan nechtasi PUL to'lagan va qancha (sof: to'lov − vozvrat) |
| `Stages[]` | bosqich matritsasi — shu bosqichga olib kelgan takrorsiz lidlar |

⚠️ **PUL BIR MARTA SANALADI:** `Won`/`Paid`/`Revenue` faqat lidni **AYLANTIRGAN** menejerga
yoziladi. Aks holda bir lidning tushumi bir necha menejerga qo'shilib, jadvaldagi summa
markazning haqiqiy tushumidan oshib ketardi. «Kim yordam berdi» savoliga bosqich matritsasi
javob beradi.

⚠️ **BOSQICH MATRITSASI — VORONKA EMAS:** qatordagi sonlar o'ngga qarab kamayib borishi SHART
emas (menejer lidni o'rtadagi bosqichdan olib, keyingisiga surgan bo'lishi mumkin) va ustun
yig'indisi voronkadagi son bilan mos kelmasligi mumkin (bir lidni ikki xodim surgan bo'lsa,
har biri o'zi ko'chirgani uchun sanaladi). Shu sabab jadvalda "jami" qatori ATAYIN yo'q.

⚠️ Matritsaga `created` hodisasi ham kiradi — lidni birinchi ustunga QO'YISH ham uni o'sha
bosqichga olib kelish demak. `Moves` ga esa kirmaydi (uning ta'rifi — faqat ko'chirishlar).

⚠️ Butun kesim `LeadEvent.ActorUserId` ga tayanadi — u 2026-08 gacha yozilmagan, ya'ni jadval
faqat shundan keyingi ishni ko'rsatadi. `ActorUserId` bo'sh yozuvlar "Noma'lum" qatoriga
YIG'ILMAYDI (tizim yozgan hodisalarda menejer umuman yo'q).

### 2. Lid KANALI — `LeadOrigins`

`form` · `test` · `instagram` · `manual` · `other`. Tasnif **BIRINCHI TEGINISH** bo'yicha:
xodim lidni o'zi kiritgan bo'lsa (`created` hodisasida `ActorUserId` bor) — u keyin forma
to'ldirgani (takroriy murojaat) kanalni O'ZGARTIRMAYDI.

⚠️ Eski (2026-08 gacha) qo'lda kiritilgan lidlarda `ActorUserId` yo'q — ular `other` ga tushadi,
shuning uchun yorlig'i ochiq: «Boshqa (sayt, eski yozuvlar)».

Kesimning o'zi — `LeadAnalytics.BuildOrigins`, UI — `components/leads/OriginTable.tsx`
(CRM statistikasi ham, "Formalar"/"Daraja testi" sahifalari ham AYNAN shuni chizadi).

### 3. «Butun CRM manzarasi» — `LeadCrmOverview`

"Formalar" bo'limidagi ikkala statistika ham (lid formalari va daraja testi) FAQAT o'z kanalini
sanaydi. Markazda esa qo'lda kiritilgan lidlar ham bor — shu kontekstsiz sahifadagi "jami" raqami
«markazning hamma lidi» deb o'qilib, noto'g'ri xulosaga olib kelardi.

Shuning uchun ikkala sahifada ham bir xil blok bor: **jami lid → aylandi → to'ladi**, kanallar
kesimi va **barcha lidlar qaysi bosqichda**. Server tomonda hisob BITTA (`LeadCrmOverview`),
UI'da ham BITTA komponent (`components/leads/CrmOverviewCard.tsx`) — ya'ni "qo'lda kiritilgan"
yoki "to'ladi" so'zi ikki sahifada ikki xil hisoblanib qolmaydi. Blok DAVRGA BOG'LIQ EMAS
(joriy holat) va bu sarlavha ostida yozib qo'yilgan.

Sahifadagi bosqich jadvallari shu sababdan qayta nomlandi: «Formadan kelgan lidlar qaysi
bosqichda» / «Test topshirganlar qaysi bosqichda» — ular BUTUN CRM emas, faqat o'z kanalini
ko'rsatadi.

### 4. "To'ladi" — yagona ta'rif

Uchala sahifada ham `LeadOutcome` zanjiri: lid → o'quvchi → `FinanceTransaction` (kirim/tuition
MINUS chiqim/refund). Ya'ni **"o'quvchi bo'ldi" hali pul degani emas** — sotuv konversiyasi
(`payRate`) aynan pul to'laganlar ulushi. Kitob sotuvi bunga kirmaydi (`.claude/rules/books.md`).

## YO'QOTILGAN LIDLAR (2026-10)

Yig'ilish talabi: «yo'qotilgan mijozlar uchun alohida ustun va sabablarni belgilash (vaqt to'g'ri
kelmadi, narx qimmatlik qildi, boshqa kursga ketdi)». Kod: `LeadLoss` (belgilash/tiklash — YAGONA
joy), `LeadLossReport` (hisobot, sof), `LeadLossController` (HTTP). Testlar: `LeadLossTests`,
klientda `pages/admin/leads/__tests__/leadLoss.test.tsx`.

### 1. ENG MUHIM — bu BOSQICH EMAS

`Lead.LostAt` (+ `LostReasonId` · `LostReason` · `LostComment` · `LostByUserId` · `LostByName`).
`LostAt == null` — lid yo'qotilmagan (mavjud qatorlar shunday, backfill YO'Q).

⚠️ **`Lead.Stage` TEGILMAYDI.** Sabablar:
- `LeadStage` ustunlarini foydalanuvchi o'zi o'chiradi/qayta nomlaydi — "Yo'qotilgan" ham bosqich
  bo'lsa, uni o'chirib yuborish butun hisobotni jimgina buzardi;
- `Program.cs` dagi "yetim lidlarni tuzatish" mavjud bo'lmagan bosqichdagi lidni har restartda
  birinchi ustunga ko'chiradi — soxta `"lost"` bosqich id'si u yerda yo'qolardi;
- lid QAYSI bosqichda yo'qotilgani saqlanadi (`lost` hodisasining `FromStage` i) va tiklanganda
  o'sha joyga qaytadi.

Kanbandagi **«Yo'qotilgan» ustuni — DOIMIY tizim ustuni** (`LostColumn`, droppable id
`__lost__`): har doim foydalanuvchi ustunlaridan KEYIN, tahrirlanmaydi/o'chirilmaydi/ko'chirilmaydi;
bazada qatori YO'Q. Yo'qotilgan lid o'z bosqichi ustunida CHIZILMAYDI — faqat shu ustunda.

### 2. Holat o'tishlari

| Amal | Endpoint | Qoida |
|---|---|---|
| Yo'qotilgan deb belgilash | `PATCH {id}/lost` `{ reasonId, comment }` | Sabab **MAJBURIY**, faqat `lead_lost` kategoriyasidan; izoh ≤ 1000 belgi (uzunrog'i 400, qirqilmaydi); aylantirilgan va allaqachon yo'qotilgan lid — 400 |
| Tiklash | `PATCH {id}/restore` `{ stage? }` | `stage` berilsa o'sha bosqichga (alohida `stage` hodisasi `FromStage/ToStage` bilan — voronka buzilmasin) |
| Bosqich surish | `PATCH {id}` | yo'qotilgan lidga **400** — avval tiklanadi |
| O'quvchiga aylantirish | `POST {id}/convert` | yo'qotilgan lidga **400** — bitta lid bir vaqtda "yo'qotilgan" va "o'quvchi" bo'lmasin |

Har amal: `LeadEvent` (`lost` / `restore` turlari — voronka ularni O'QIMAYDI), audit (`Lead` →
"Lidlar" bo'limi), keyin `LeadNotifier.SyncCardAsync`. Belgilashda mas'ul bo'sh bo'lsa — belgilagan
xodim biriktiriladi (`ChangeStage` qoidasi), bori USTIDAN yozilmaydi.

⚠️ **HOLAT HECH QACHON JIMGINA O'ZGARMAYDI:** karta «Yo'qotilgan» ustuniga tashlansa sabab oynasi
(`LeadLostModal`) ochiladi; undan boshqa ustunga sudralsa — tasdiqlash so'raladi va `restore`
chaqiriladi. Lid sahifasida: "⋮" → «Yo'qotilgan deb belgilash» / qizil blokda «Tiklash»; yo'qotilgan
lidda bosqich tanlovi va «O'quvchiga aylantirish» yopiq.

⚠️ `ReasonPromptModal` ATAYIN ishlatilmadi — u yerda sabab ixtiyoriy.

### 3. Sabab — `ActionReason`, kategoriya `lead_lost`

`ActionReasonsController.Categories` va `ReasonsPage.CATEGORIES` («Lid yo'qotilganda») — IKKALASIDA
ham. Standart to'plam `LeadLoss.DefaultReasons` — `Program.cs` dagi per-kategoriya seed (kategoriya
BO'SH bo'lsa yoziladi). Lidga sabab MATNI SNAPSHOT bo'lib yoziladi (`contacts.md` naqshi): katalogda
qayta nomlansa/o'chirilsa ham eski lid va hisobot o'z sababi bilan qoladi. Filtr va hisobot shu
MATN bo'yicha guruhlaydi (id bo'yicha emas).

### 4. "OCHIQ LID" ta'rifi — yo'qotilgan FAOL ISH EMAS

`DashboardSummary.LeadRow.IsOpen` = aylantirilmagan VA yo'qotilmagan. Shu ta'rifdan:

| Joy | Nima bo'ladi |
|---|---|
| Bosh sahifa «Buyurtmalar» / «Birinchi darsga keladiganlar» | yo'qotilgan SANALMAYDI |
| «Birinchi darsga yozilganlar» (`first-lesson`) | ro'yxatga TUSHMAYDI |
| Lidlar ro'yxatidagi `firstLessonAt` | yo'qotilganda bo'sh |
| Sinov darsi eslatma SMS (`TrialReminderService`) | YUBORILMAYDI (tiklansa yana ishlaydi) |
| KPI cheklisti «ochiq lidlarga topshiriq bog'langan» | yo'qotilgan talab qilinmaydi |
| Ommaviy SMS (tanlovsiz standart auditoriya) | kirmaydi; qo'lda tanlansa — ketadi |

⚠️ **«Buyurtmadan ketganlar» (`OrdersLeft`) O'ZGARMADI** — avvalgidek faqat O'CHIRILGAN lidlar:
kartochka ro'yxati arxiv sahifasi, yo'qotilganlar u yerda yo'q (sanoq ro'yxatdan farq qilmasin).
Yo'qotilganlar — «Yo'qotilgan lidlar» hisobotida.

⚠️ Voronka analitikasi (`LeadAnalytics`), `LeadCrmOverview` va `/stats` bosqich kesimi TEGILMAGAN:
yo'qotilgan lid o'z (oxirgi) bosqichida sanaladi — u o'sha bosqichgacha HAQIQATAN yetgan.

⚠️ **TAKRORIY MUROJAAT holatni O'ZGARTIRMAYDI** (birinchi teginish qoidasi, `lead-forms.md` §4):
yo'qotilgan odam formani qayta to'ldirsa ham lid yo'qotilganligicha qoladi. Shuning uchun kartada
`lastRepeatAt > lostAt` bo'lsa «Qaytib keldi» belgisi chiqadi (`cameBackAfterLoss`). Avto-tiklash
QILINMAGAN — kerak bo'lsa lid yaratuvchi 5 joyning hammasiga (`RepeatCount++` qatorlari) qo'shilsin.

### 5. Telegram kartasi

`BuildStateText` → «❌ Yo'qotildi: <sabab>» (guruhga ham). **IZOH faqat superadminning shaxsiy
chatida** — menejer izohi bilan bir xil qoida (`messaging.md` «LID KARTASI»). Yo'qotilmagan lid
kartasining matni/xeshi o'zgarmagan.

### 6. Filtrlar (Lidlar sahifasi, ikkala ko'rinish)

`?lost=hide|only` va `?reason=<sabab matni>` (sabab tanlansa — faqat o'sha sabab bilan
yo'qotilganlar). Standart — hammasi; jadvalda yo'qotilgan qator ism yonida «Yo'qotilgan» chipi bilan.
`hide` da kanbandagi ustun turaveradi (kartaga tashlash mumkin), lekin kartalari chizilmaydi.
Ustunni o'chirishda yo'qotilgan lidlar sanalmaydi (ular u yerda ko'rinmaydi; yetim-tuzatish ularni
birinchi bosqichga o'tkazadi, holati o'zgarmaydi).

### 7. Hisobot — `GET lost-report?from&to` (`/admin/hisobotlar/yoqotilgan-lidlar`)

Ruxsat — `[AdminPerm("leads.stats", ReadRequiresPerm = true)]` (faqat `leads.list` bor operator
ko'rmaydi), klientda `RequirePerm perm="leads.stats"`, katalogda `config/reports.ts` (`inNav: true`,
Future → Hisobotlar). Yangi ruxsat kaliti YO'Q.

⚠️ **DAVR — lid KELGAN sana bo'yicha** (`LeadAnalytics.InRange`, CRM analitikasi bilan bir xil):
«shu davrda kelgan lidlarning qanchasini HOZIR yo'qotilgan». Barcha foizlar bitta to'plamdan —
100% dan oshmaydi. "Yo'qotilgan sana bo'yicha" sanoq va "kelgan sana bo'yicha" maxraj aralashtirilsa
"120% yo'qotish" kabi ma'nosiz raqam chiqardi. Tiklangan lid yo'qotilgan SANALMAYDI.

Tarkibi: KPI (kelgan · yo'qotilgan · ulush · aylangan), sabablar donut'i (eng ko'p 5 tasi + «Boshqa»),
manba va MAS'UL xodim (`AssigneeUserId`, joriy ism; bo'sh — «Biriktirilmagan») kesimi — jami va
yo'qotilgan, oylik oqim (bo'sh oylar NOL bilan, ≤ 36 oy), eng so'nggi 100 ta yo'qotilgan lid
(qirqilgani `RecentTruncated` bilan OCHIQ yoziladi).

Ranglar — `#0284c7` (kelgan) + `#e11d48` (yo'qotilgan), `course-analytics.md` §6 juftligi;
sabablar — `stats/palette.ts`. Foiz alohida o'q bilan CHIZILMAYDI (faqat tooltip/jadvalda).
