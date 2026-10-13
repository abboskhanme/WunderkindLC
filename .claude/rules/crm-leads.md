---
description: CRM — lidlar, lid manbasi ma'lumotnomasi, lidda tashqi maktab, sinov darsi va konversiya.
paths:
  - "WunderkindLC.Server/Controllers/Lead*.cs"
  - "WunderkindLC.Application/Services/LeadNotifier.cs"
  - "WunderkindLC.Application/Services/TrialReminderService.cs"
  - "WunderkindLC.Application/Services/LeadLoss*.cs"
  - "WunderkindLC.Application/Services/LeadConversion.cs"
  - "WunderkindLC.Application/Services/LeadRemoval.cs"
  - "WunderkindLC.Application/Services/LeadTrialRules.cs"
  - "WunderkindLC.Application/Services/LeadIntake.cs"
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
  ⚠️ **«Buyurtmalar» bilan KESISHMAYDI (2026-10-09, mijoz: "Birinchi darsga o'tkazilganda oldingi bo'limdan
  o'chmayapti").** Sinovga yozilgan lid «Buyurtmalar ro'yxati» JADVALIDA standart ko'rinmaydi (status `lead` =
  aylantirilmagan VA `firstLessonAt` bo'sh — `leadMatchesStatus`, `leadLabels.ts`), bosh sahifadagi «Buyurtmalar»
  ham uni sanamaydi (`DashboardSummary.OpenLeads(leads, trials, today)` — a'zolik `LeadFirstLesson.Select` dan).
  O'quvchiga aylantirilgani ham standart jadvalda yo'q. «Status» filtri: Buyurtma · Birinchi darsga yozilgan ·
  O'quvchiga aylantirilgan · Hammasi; jadval ostida yashirilganlar SONI va havola. Kanban — butun voronka,
  standart "hammasi". Natija belgilangan sinov (keldi/kelmadi) lidni «Birinchi dars»dan chiqaradi — aylantirilmagan
  bo'lsa u yana buyurtmalarda.

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
  o'zgartirilsa `SubjectsController.Update` eski nomli lidlarni ham ko'chiradi (`LeadSource` kabi) —
  o'sha tranzaksiyada. ⚠️ Kurs nomlari NOYOB (registr/apostrof farqisiz) va eski nom boshqa kursda
  ham bo'lsa lidlar ko'chirilmaydi (`.claude/rules/admin-core.md` §2).
  **CRM statistikasi** (`/stats` → `CrmStatsDto.ByInterest`, `CrmInterestStatDto`): fan bo'yicha lid
  soni + aylantirilgan + konversiya %; normalizatsiya — kurs id → nomi, registr farqisiz kurs nomiga
  moslash, bo'sh = "Ko'rsatilmagan". UI: `CrmStatsPage` — gorizontal bar (top 10) + to'liq jadval.

- **Tuman/maktab — OLIB TASHLANGAN (2026-09-29, mijoz: "umuman qolmasin").** Lid va o'quvchi
  formalarida, filtrlarda, konversiyada, «Lid kiritish formasi» katalogida va Sozlamalarda YO'Q;
  `DistrictsController` va `settings.districts` kaliti o'chirilgan. `Lead`/`Student` dagi
  `DistrictId`/`SchoolId` ustunlari va `Districts`/`Schools` jadvallari bazada faqat ma'lumot
  saqlanib turishi uchun qolgan — yangi kod ularni ISHLATMASIN. Lidlar ichidagi qidiruv:
  ism/telefon/ota-ona/manba (telefon raqamlar bo'yicha).

## «AKTIV O'QUVCHI QILISH» — bitta oynada (2026-10-13)

Owner: "birinchi darsga yozilganni 1-2 ta knopka bilan aktiv o'quvchi qilish". «Birinchi darsga yozilganlar» qatoridagi
**«Aktiv o'quvchi qilish»** (va lid sahifasidagi «O'quvchiga aylantirish») — `LeadConvertPanel`: sinov darsidan guruh,
o'qituvchi va sana (birinchi dars kuni; kelajakda bo'lsa — bugun) oldindan to'ladi; **«Darhol aktivlashtirish»**
(`classes.list:create` bo'lsa, standart yoqiq) belgilansa aylantirishdan KEYIN odatdagi `POST classes/{id}/members/{sid}/activate`
chaqiriladi (qisman oy, catch-up, audit — yagona yo'l). Oyna birinchi oy QANDAY hisoblanishini oldindan ko'rsatadi:
`GET classes/{id}/activation-quote?date=` → `TuitionService.ActivationQuoteAsync` (aktivlashtirish yozadigan summaning
O'ZI — `ActivationGrossAsync` bilan bitta formula; matn `lib/activationQuote.ts`). To'lov — alohida (owner qarori).
Aylantirish o'tib aktivlashtirish yiqilsa — sabab va «Qayta aktivlashtirish».

## O'QUVCHIGA AYLANTIRISH — `LeadConversion` (2026-10-07, audit C5)

Ilgari `POST {id}/convert` o'quvchi yaratishning "yupqa" IKKINCHI yo'li edi: lidning o'z telefoni
ko'chmas (formadan/Instagram/Meta/testdan kelgan o'quvchi TELEFONSIZ — SMS, qo'ng'iroq moslash,
ota-ona kabineti ishlamas), ota/ona maydonlari, login akkaunt, `CreatedAt`, audit va
`StudentAdded` avto-xabari yo'q edi; guruh to'lsa ham `ClassName` o'sha guruhga yozilar, klient esa
"Aylantirilgan" deb jimgina ko'rsatardi. Endi butun mantiq `LeadConversion` da va qoidalar AYNAN
ikki manbadan ko'chirilgan (ularning fayllari tegilmagan — o'zgarsa bu yerni ham yangilang):

| Qism | Manba qoida |
|---|---|
| O'quvchi yozuvi | `StudentsController.AddStudent`: telefon/ota/ona → `PhoneUtil.Normalize`; ASOSIY ota-ona (`ParentFullName/ParentPhone`) = ota, bo'lmasa ona; `CreatedAt`; "student" akkaunt (`AccountFactory`); `Student` auditi "O'quvchi qo'shildi" |
| A'zolik | `ClassesController.AddMember`: sig'im = o'rin band qilganlar (`OccupiesSeatExpr`, ⚠️ arxivdagi o'quvchi SANALMAYDI); yangi a'zolik `trial`, `RecordedAt` = bugun; avval chiqqan bo'lsa davr tarixga (`ClosePeriod`); `ClassName` FAQAT a'zolik yaratilganda; `Membership` auditi `"{groupId}:{studentId}"` |
| Avto-xabar | `StudentAdded` — faqat a'zolik haqiqatan yaratilganda (guruh to'lgan bo'lsa yo'q) |
| Lid | `ConvertedStudentId`, KPI `ClosedByUserId/ClosedAt`, mas'ul bo'sh bo'lsa — yopgan xodim; `convert` hodisasi; `Lead` auditi |
| Sinov darsi | sanasi o'tgan/BUGUNGI `pending` sinov → `stayed` + `AttendedAt` = sinov kuni (KPI "sinovga keldi" o'sha oyga); KELAJAKDAGI tegilmaydi |

- **Qabul sanasi** bo'sh → bugun; buzuq → **400** (AddStudent importdagi kabi jimgina almashtirmaydi).
  Topilmagan/arxivlangan guruh → 400 (ilgari jimgina guruhsiz o'quvchi yaratilardi).
- **Ruxsat:** `leads.list:create` (sinf) **+ `students.list:create`** (`MayCreateStudents`, 403 sabab
  matni bilan). Klientda «O'quvchiga aylantirish» ikkala ruxsat bo'lsagina ko'rinadi.
- **DUBLIKAT → 409** `{ code: "duplicate_student", candidates }` — `StudentImportPlan` qoidasi:
  ISM (`NameKey`) VA kamida bitta TELEFON mos, faqat ARXIVLANMAGAN o'quvchilar. Klient ikki yo'l
  beradi: **«Mavjud o'quvchiga bog'lash»** (`?linkStudentId=` — yangi o'quvchi/akkaunt YARATILMAYDI,
  lid o'sha o'quvchiga yopiladi, guruh tanlangan bo'lsa unga qo'shiladi — allaqachon a'zo bo'lsa
  tegilmaydi) va **«Baribir yangi o'quvchi yaratish»** (`?force=true`).
- ⚠️ **Bog'lash FAQAT dublikat nomzodiga** (`LeadConversion.CanLinkTo` — 409 dagi bilan AYNAN bir xil
  qoida: arxivlanmagan, ism + kamida bitta telefon mos), aks holda 400 (2026-10-08). Ilgari istalgan
  `linkStudentId` qabul qilinardi: lidni BEGONA o'quvchiga (masalan aka-ukasiga) yopib, KPI
  "shartnoma"ni yozdirish va uni guruhga qo'shish mumkin edi.
  Bir o'quvchiga BIR NECHTA lid bog'lanishi **MUMKIN (ATAYIN)**: sobiq/joriy o'quvchining takroriy
  murojaati YANGI lid bo'lib tushadi (pastda — "Avval o'quvchi bo'lgan") va u aynan shu o'quvchiga
  yopilishi kerak; rad etilsa operator `force=true` bilan dublikat o'quvchi yaratardi.
- ⚠️ **Guruh tanlansa — `AddMember` ruxsati** (`classes.list`, POST → `create`; `MayAddToGroups`),
  ikkala yo'lda ham (yaratish va bog'lash), 403 sabab bilan. Ilgari lid + o'quvchi ruxsati bor xodim
  shu yo'l bilan guruhlar bo'limiga ruxsatsiz a'zo qo'sha olardi. Standart "Administrator" rolida
  `classes` bor — o'zgarish yo'q.
- **Guruh to'lgan** — o'quvchi yaratiladi, javobda `groupFull = true`, klient panelda sariq
  ogohlantirish ko'rsatadi ("guruhga QO'SHILMADI") — oyna yopilmaydi.

## LID YARATISH, O'CHIRISH, SINOV — boshqa qoidalar (2026-10-07)

- **Qo'lda lid — telefon dublikati 409** (`POST /api/admin/leads`, `code: "duplicate_lead"`, mavjud
  lid(lar): id, ism, bosqich, aylantirilgan/yo'qotilgan). Modal "Bu raqamli lid allaqachon bor" deb
  havola va «Baribir yaratish» (`?force=true`, aka-uka bir raqamda) ko'rsatadi. Solishtirish
  `Lead.PhoneKey` (asosiy telefon) bo'yicha — aylantirilgan lid ham ko'rsatiladi (aka-uka holati).
- **Tashqi kanallardagi takroriy murojaat aylantirilgan lidga YOPISHMAYDI** — `FindByPhoneAsync`
  faqat ochiq (aylantirilmagan) lidni qaytaradi; yangi lidga "Avval o'quvchi bo'lgan: <ism>" izohi
  (CRM-1, `lead-forms.md` §4). Instagram'da suhbat yangi lidga ko'chadi, lekin eski lid
  `IgConversation.PastLeadIds` tarixida qoladi — kanal tahlili va reklama ROI'si pul to'lagan ESKI
  lidni yo'qotmaydi, yangi lid esa Telegram guruhiga YANGI lid bo'lib tushadi
  (`marketing-instagram.md` §6.1, §6.4).
- (2026-10: lidga avto-SMS — `lead_new`, `trial_reminder`, `test_link`, `test_result` — SMS kanali bilan
  birga OLIB TASHLANDI; lidning Telegram manzili yo'q. Quyidagi qator TARIX.)
- **`lead_new` avto-SMS** endi Instagram va Meta reklama lidlarida ham ketadi (YANGI + telefonli) —
  `InstagramLeadBridge`/`MetaLeadBridge` ichida (CRM-9). Takroriy murojaatga — yo'q.
- **Aylantirilgan lid O'CHIRILMAYDI** (400): KPI "shartnoma", sotuv tushumi va qabul cheklisti
  («Daraja testi») unga tayanadi. UI'da "Lidni o'chirish" yashirin.
- **O'chirishda** (`LeadRemoval`): hodisalar + sinovlar alohida **`lead_history`** arxiv yozuviga
  (`{ "Events": [...], "Trials": [...] }`) olinadi — "Arxiv"dan tiklanganda tarix ham qaytadi
  (`ArchiveController.Restore`); `IgConversation.LeadId` / `IgAdLead.LeadId` TOZALANADI (aks holda
  Inbox'da «Lidga aylantirish» yashirinar, reklama lidini «Qayta olish» rad etardi).
- **Sinov darsi** faqat OCHIQ lidga: aylantirilgan yoki yo'qotilgan lidga `POST {id}/trials` → 400
  (UI'da tugmalar yashirin).
- **Sinov natijasini TUZATISH mumkin** (`PATCH trials/{id}`, UI'da «Tuzatish…»): kelmagan holatga
  (`no_show`/`pending`) o'tilsa `AttendedAt` TOZALANADI — KPI `trialCame` to'la `AttendedAt` ni
  "keldi" deb sanaydi; "keldi" oilasi ichida (keldi → qoldi) sana saqlanadi
  (`LeadTrialRules.ApplyAttendance`). Tarixga "natijasi tuzatildi: X → Y" yoziladi; bir xil natija
  qayta yuborilsa hech narsa yozilmaydi.
- **Kanban ustunlari va lid manbalari AUDITGA yoziladi** (turlar `LeadStage`, `LeadSource` → "Lidlar"):
  ular voronka, CRM statistikasi va CAPI (bosqich nomi) natijasini o'zgartiradi. Ichida
  (yo'qotilmagan) lid bor ustun serverda ham o'chirilmaydi (400, soni bilan).
- **Landing** (`POST /api/public/landing-lead`) anonim chaqiruvchiga lid ID'sini QAYTARMAYDI — faqat
  `{ ok: true }`.

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

⚠️ **Kirish to'plamlari IKKI joyda bir xil bo'lishi SHART** — `LeadsController.Analytics` ham,
`LeadCrmOverview` ham `adsIds` (`IgAdLead.LeadId`) ni uzatadi va tushumni `Math.Max(0, …)` bilan
qirqadi. Ilgari CRM statistikasi `adsIds` ni uzatmasdi va reklama lidlari u yerda "Boshqa" ga
tushardi — ikki sahifa, ikki raqam (CRM-2).

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
| Sinov darsi eslatma SMS (`TrialReminderService`) | 2026-10 dan xizmatning O'ZI yo'q |
| KPI cheklisti «ochiq lidlarga topshiriq bog'langan» | yo'qotilgan talab qilinmaydi |
| Lidga ommaviy SMS | 2026-10 dan YO'Q (SMS kanali olib tashlandi) |

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
ko'rmaydi), klientda `RequirePerm perm="leads.stats"`, katalogda `config/reports.ts` (`inNav: true`).
Menyuda — ASOSIY **Hisobotlar** guruhida (2026-10-07 dan Future'da EMAS: `mainReportRoutes`).
Yangi ruxsat kaliti YO'Q.

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

## O'CHIRILGAN LIDNI ARXIVDAN TIKLASH (2026-10-07, audit CRM-4)

«Sozlamalar → Arxiv» → «Lidlar» → «Tiklash» (`ArchiveController`). Ilgari faqat YALANG lid qaytardi:
hodisalar va sinov darslari o'chirishda yo'qolgan (tarix bo'sh, kanal `created` hodisasidan aniqlangani
uchun "Boshqa" ga o'tib ketardi — `LeadOrigins`), audit yo'q edi.

- **Shartnoma (T8):** `LeadsController.Delete` lid suratidan tashqari YORDAMCHI yozuv ham yozadi —
  `ArchiveService.Snapshot(Type = "lead_history", EntityId = lead.Id, new { Events, Trials })`.
  Tiklashda (`ArchiveService.RestoreLeadHistoryAsync`) hodisalar va sinov darslari qayta qo'shiladi,
  yordamchi yozuv o'chiriladi. Lid surati butunlay o'chirilsa — tarix ham birga o'chadi.
- `lead_history` arxiv ro'yxatida va sanog'ida KO'RINMAYDI (`ArchiveService.HiddenTypes`), o'zi alohida
  tiklanmaydi (400).
- ⚠️ **Telefon to'qnashuvi — 400 «Bu raqam bilan boshqa lid bor: <ism>»** (`ConflictingLeadNameAsync`,
  `PhoneKey`). O'chirilgandan keyin shu raqam bilan yangi lid ochilgan bo'lsa tiklash uni "ikkinchi
  nusxa" qilmasin: `LeadIntake.FindByPhoneAsync` ENG ESKI lidni qaytaradi, ya'ni tiklangan eski yozuv
  yangisini telefon bo'yicha qidiruvda to'sib qo'yardi.
- Bosqichi o'chirilgan bo'lsa — birinchi ustunga. Tiklash auditga yoziladi (`Lead`).
