---
description: CRM — lidlar, lid manbasi ma'lumotnomasi, lidda tashqi maktab, sinov darsi va konversiya.
paths:
  - "WunderkindLC.Server/Controllers/Lead*.cs"
  - "WunderkindLC.Application/Services/LeadNotifier.cs"
  - "WunderkindLC.Application/Services/TrialReminderService.cs"
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

- **Lidda tashqi maktab** (migratsiya `AddLeadSchool`): `Lead.DistrictId`/`SchoolId` (o'quvchidagi
  `District`/`School` ma'lumotnomasi) — formada tuman→maktab select'lari, Lidlar sahifasida shu bo'yicha
  filtr + lidlar ichida qidiruv (ism/telefon/ota-ona/manba/maktab; telefon raqamlar bo'yicha).
  Konversiyada (`/convert`) tuman/maktab `Student`ga ko'chadi.

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
