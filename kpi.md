# Xodimlar KPI tizimi — TAHLIL VA LOYIHA REJASI

> **Holat: REJA (2026-09-09).** Hali kod yozilmagan. Manba — `kpi/` papkasidagi 5 fayl
> (2 KPI kitobi, 2 kunlik cheklist, 1 qabul skripti) va kod bazasi
> (`Domain/Entities.cs`, `Application/Services/`, `Server/Controllers/`, `RETENTION-BONUS-PLAN.md`).
>
> **Bitta jumlada:** KPI hisoblash uchun kerak raqamlarning katta qismi tizimda allaqachon
> yoziladi — qurish kerak bo'lgan narsa **hisoblash qatlami + cheklist + tiket + oylik yopish**,
> yangi ma'lumot yig'ish emas.
>
> **Naqsh:** `RetentionBonusService` (jonli hisob, hech narsa saqlanmaydi, kechikkan ma'lumot
> kiritilsa katak o'z-o'zidan tuzaladi) va `CenterAiAnalysisService` (raqamlar deterministik,
> AI faqat narrativ). KPI moduli aynan shu ikki naqshda quriladi.

---

## 1. Qisqa xulosa

| | |
|---|---|
| Kerakli kirish raqamlari | **15** — ikkala kalkulyator jami 15 ta oy oxiridagi raqam so'raydi. 5 tasi tizimda tayyor, 7 tasi bitta maydon/hisob bilan chiqadi, 3 tasi (uzaytirish, tiket ×2) uchun yangi yozuv kerak |
| Yangi entity | **10** — `KpiProfile`, `KpiProfileSalary`, `KpiRuleSet`, `KpiTicket`, `ChecklistTemplate`, `ChecklistTemplateItem`, `ChecklistEntry`, `KpiMonthSnapshot`, `KpiMonthResult`, `StudentExtension`. Qolgani mavjud jadvallarga 1–2 maydon |
| Muddat | ~3–4 hafta, 6 bosqich (7-bo'lim) |

**Tavsiya:** alohida **«KPI» bo'limi** (admin menyusida *Xodimlar* yonida), ichida 5 sahifa:
**Bugun** (kunlik norma + cheklist), **Oy** (jonli kalkulyator, har xodim uchun), **Tiketlar**
(sifat nazorati), **Yopish** (oylik natijani tasdiqlash va muzlatish), **Qoidalar** (Excel'dagi
«KPI qoidalari» varag'i — konstantalar va koeffitsient jadvallari, versiyalangan).

Mavjud *Lidlar, Davomat, Moliya, Bog'lanish kerak, Qo'ng'iroqlar, Topshiriqlar, Instagram*
modullari hech qayerga ko'chirilmaydi — KPI bo'limi ulardan faqat **o'qiydi**.

### ⚠️ Excel'dagi ziddiyatlar — kodga o'tkazishdan oldin hal qilinadi

1. **Bonus A / B qiymati.** «KPI Chiquvchi Admin» kitobida *Boshlash* varag'i Bonus A = **6 000**
   so'm / faol o'quvchi, Bonus B = **45 000** / uzaytirish deydi. *KPI qoidalari* varag'idagi
   doimiy qiymatlar esa **1 000** va **35 000**. KALKULYATOR va Ssenariylar 1 000 / 35 000 bilan
   hisoblaydi (`'KPI qoidalari'!$C$10`, `$C$11`). Birlik iqtisodiyoti varag'i ham «Bonus A dagi
   1 000 so'm» deydi.
   Farq: «NORMA» ssenariysi (330 faol, 29 uzaytirgan, 15,5 mln qarz, samaradorlik 0.93)
   1 000 / 35 000 bilan **roppa-rosa 4 500 000** (= kafolat, ya'ni normal oyda bonus yo'q);
   6 000 / 45 000 bilan 6 440 000; «Kuchli oy» (440 faol, 70 mln qarz) 6 000 / 45 000 bilan
   8 751 375 — 7 500 000 chegaradan oshadi.
2. **Kiruvchi kitobi, Ssenariylar xulosasi** «94 shartnoma — 5 208 000, 80 shartnoma — 5 700 000»
   deydi, formulalar esa 4 982 000 va 5 300 000 beradi (matn eski versiyadan). Yo'nalish to'g'ri
   (sifat past → ko'p shartnoma kam pul). Tizimga o'tganda **formulaga** ishonamiz, matnga emas.

---

## 2. Fayllar tahlili — varaqma-varaq

### 2.1 `KPI Kiruvchi Admin.xlsx` — 7 varaq

| Varaq | Nima bor | Tizim uchun |
|---|---|---|
| **Plan matematikasi** | Orqadan hisob: 80/100 shartnoma → 100/125 sinovga kelgan (÷0.80) → 312/390 lid (÷0.32) → kuniga 12/15 lid, 36/45 teginish, 3.8/4.8 sinov, 3.1/3.8 shartnoma (26 ish kuni) | «Bugun» sahifasidagi kunlik norma. 4 faraz (0.80, 0.32, 26 kun, 3 teginish) — sozlanadigan parametr |
| **KPI qoidalari** | Oklad 2 500 000 · baza 35 000/shartnoma · tiket 50 000 · kafolat 4 500 000 (1–2 oy) · kelgan→shartnoma normasi 80% (70–79% → −0.20, <70% → −0.40) · lid kafolati 250 · konversiya jadvali 5 pog'ona (0.5/0.8/1.0/1.2/1.4) · samaradorlik 4 pog'ona (0.75/0.9/1.0/1.1) · 8 tuzatuvchi qoida | To'liq `KpiRuleSet` JSON. Hech narsa hardcode qilinmaydi |
| **KALKULYATOR** | 5 qo'lda raqam (lid, sinovga kelgan, shartnoma, samaradorlik, tiket) → 3 konversiya → koeffitsientlar → oylik. 5-qadam: reja bilan solishtirish, lid kafolati (<250 → reja = lid × 0.32 × 0.80) | «Oy» sahifasi. 5 kirishning **hammasi** tizimdan avtomatik |
| **Ssenariylar** | 6 oy: Yomon (240 lid, 50 sh.) → Kuchli (330 lid, 100 sh.) → «Shartnoma ko'p, sifat past» (94 sh., kelgan→sh. 65%, 3 tiket → 80 ta qilgandan kam oladi) | **Unit test** qatorlari — `KpiCalculatorTests` |
| **Kunlik norma** | Reja 90 → kuniga 3.5 sh., 4.3 sinov, 13.5 lid, 40 teginish. 10 sifat normativi: 3 gudok, javobsizni 15 daq. ichida, DM 15 daq., ehtiyoj maydoni 95%, raqamli natija 100%, 2 soat+ muloqot, kechikkan vazifa 0, vazifasiz sdelka 0 — audit mezonlari 1, 2, 3, 5, 6, 8, 13 | 6 tasi avtomatik o'lchanadi, 4 tasi faqat tinglab (tiket manbai) |
| **Kelajakda_bolinish** | Admin va call-operator ajralganda: operator — lid→sinovga KELISH (25 000/kelgan), admin — kelgan→shartnoma (35 000). 8 etapli voronka, 4-etapda mas'ul almashadi. **Ikki maydon hozirdan:** «lidni ishlagan operator», «shartnomani yopgan admin» | Eng muhim arxitektura talabi. `Lead` da mas'ul xodim yo'q — 0-bosqich |
| **Geymifikatsiya** | Kunlik 4-sh. 30 000, 5-sh. 50 000, 50+ qo'ng'iroq & 2 soat+ 25 000, haftalik sifat 10/10 → 100 000. Fond ≈ 620 000/oy | Alohida byudjet, oylikka kirmaydi. Badge + Telegram xabari yetarli |

### 2.2 `KPI Chiquvchi Admin.xlsx` — 6 varaq

| Varaq | Nima bor | Tizim uchun |
|---|---|---|
| **Boshlash** | Bonus uchta manbadan — A ushlab qolish, B uzaytirish, C qarz yig'ish 1%. A eng katta qism: uzaytirish oy oxiri ishi, ushlab qolish har kungi. «Shkala — gipoteza, 2 oy o'lchab sozlanadi» | 6 000/45 000 vs 1 000/35 000 ziddiyati |
| **KPI qoidalari** | Oklad 3 000 000 · A 1 000 · B 35 000 · C 1% (yig'ish <80% → 0.5%) · sababsiz ketish 50 000 · tiket 50 000 · kafolat 4 500 000 · yuqori chegara 7 500 000. Ushlab qolish (ketish % bo'yicha 6 pog'ona 1.15→0.40), uzaytirish (5 pog'ona 0.5→1.25), samaradorlik (4 pog'ona 0.75→1.05). Tuzatuvchi: uzaytirish ta'rifi (oylik to'lov EMAS), ketish sababi majburiy (7 variant), nazoratdan tashqari sabablar (ko'chish, sog'liq, oila) ketish foizidan chiqariladi, «faol» = 30 kundan ortiq kelmagan / 45 kundan ortiq qarz bo'lmasa | «Faol o'quvchi» ta'rifi va «nazoratdan tashqari» belgisi aniq kodlanishi shart |
| **KALKULYATOR** | 10 kirish: oy boshi faol, ketgan (nazoratdagi), oy oxiri faol, kursi tugagan, uzaytirgan, oy boshi qarz, yig'ilgan qarz, samaradorlik, sababsiz ketish, tiket | 7 tasi tizimda, 3 tasi yangi yozuv |
| **Ssenariylar** | 6 oy. Muhimi — «Uzaytirdi, USHLAB QOLMADI»: uzaytirish 86%, ketish 13.9% → koef 0.40 | Unit test |
| **Birli iqtisodiyoti** | Kurs 500 000, o'qituvchi 55% → margin 225 000/o'quvchi/oy. Tekshiruv: A ≤ margin 3% (6 750), B ≤ 4 oylik margin 5% (45 000), admin oyligi markaz marginidan ≤ 10%. Ikkala admin ≤ margin 20% → kamida 270–310 faol | «Yopish» sahifasida 3 sog'lomlik indikatori — margin `Group.MonthlyFee` va `TeacherSalaryPercent` dan |
| **Vazifa va KPI** | 10 vazifa → bonus (A/B/C/Tiket/Samaradorlik) → audit mezoni (10, 11, 12) | Cheklist bandlarining `KpiTag` (U/K/Q/T) |

### 2.3 Kunlik cheklistlar — 2 fayl

**Kiruvchi admin** (09:00–18:00): 31 band, 9 vaqt bloki, har band audit mezoniga bog'langan
(1, 2, 3, 5, 6, 7, 8, 9, 13). Kun oxirida 5 raqam: yangi lid 13–15, sinovga yozildi 5–6,
sinovga **keldi** 4–5, shartnoma 3–4, muloqot 2 soat+.

**Chiquvchi admin** (08:00–17:00): 35 band, 8 blok, har bandda KPI harfi (U/K/Q/T). Kun oxirida
7 raqam: kelmaganlar, 3 dars ketma-ket kelmaganlar (0 ga tushsin), bugun ketgan, uzaytirish
suhbati 3–4, uzaytirgan 1–2, yig'ilgan qarz, nosozlik.

Ikkalasi ham qog'oz uchun (Du–Sh, «Tekshirdi»). Tizimda 66 bandning ~yarmi **o'z-o'zidan
belgilanadi** (qo'ng'iroq qaytarildi, DM lidi ochildi, kechikkan vazifa = 0, eslatma yuborildi);
qolgani (xona aylanildi, o'qituvchi so'rovi yig'ildi) xodim o'zi belgilaydi.

### 2.4 `Kiruvchi admin qabul qilish skripti.doc`

10 bosqich. KPI uchun muhim: 2-bosqich «Kanal» → `Lead.Source` (bor); 3-bosqich ehtiyoj javoblari
→ `Lead.AnswersJson`/`Note` (bor); 8-bosqich «CRM'da mas'ul admin» → **yo'q**; 9-bosqich «keyingi
aloqa sanasi avtomatik eslatma» → `WorkTask` + `WorkTaskReminderService` (bor). 6-bosqich e'tirozlar
va 13 audit mezoni → **tiket sabablari katalogi**.

---

## 3. Har bir KPI raqami qayerdan keladi

Belgilar: **BOR** — tizim hozir ham hisoblaydi · **QISMAN** — ma'lumot bor, hisob yoki 1 maydon kerak · **YO'Q** — yangi yozuv.

### 3.1 Kiruvchi admin

```
OYLIK = Oklad + ( Shartnoma × 35 000 × Konv.koef ) × Samar.koef − Tiket × 50 000
```

| Kalkulyator so'raydi | Holat | Manba | Izoh |
|---|---|---|---|
| Oyda kelgan lid | BOR | `Lead.CreatedAt`, `LeadAnalytics.BuildAsync` (Total), `IgConversation.LeadId`, `LeadFormSubmission` | Manba `Lead.Source` |
| Sinovga **KELGAN** | QISMAN | `TrialLesson.Result` — hozir faqat `pending`/`stayed` | `came`/`no_show` + `AttendedAt` kerak. Sinov kunidagi `JournalEntry.Present` bilan avtomatik tasdiqlash mumkin |
| Imzolangan shartnoma | QISMAN | `Lead.ConvertedStudentId`, `Contract.Status`, `StudentGroup.ActivatedAt`, `LeadOutcome.PayByStudent` | Hodisa bor, **kim yopgani** yo'q (`Contract.CreatedBy` yo'q). Vaqtincha `AuditLog.ActorId`; to'g'risi `Lead.ClosedByUserId` |
| Samaradorlik | BOR | `WorkTask.DueDate/DueTime/CompletedAt/AssigneeId`, `WorkTasksController.Dashboard` | Excel «amoCRM» = bizda Topshiriqlar. Muddatida yopilgan / muddati kelgan |
| Tiket | YO'Q | — | `KpiTicket` |
| 3 gudokgacha javob | BOR | `Call.StartedAt`, `Call.AnsweredAt`, `Status=no_answer` (MoiZvonki) | 3 gudok ≈ 12–15 s |
| Javobsizni 15 daq. ichida qaytarish | BOR | `Call.Direction/Status/PhoneNumber` | Kiruvchi `no_answer` → shu raqamga chiquvchi qo'ng'iroq |
| DM javob 15 daq. | QISMAN | `IgConversation.LastInboundAt/LastOutboundAt`, `NeedsOperator` | Instagram bor. Telegram: `LeadTelegramMessage` da «kim javob berdi» yo'q |
| Ehtiyoj maydonlari 95% | BOR | `Lead.AnswersJson`, `LeadEntryField` (majburiy) | |
| Muloqot 2 soat+ | BOR | `Call.DurationSeconds` sum, `OperatorUserId` bo'yicha | |
| Kechikkan vazifa / vazifasiz sdelka = 0 | BOR | `WorkTask` + `Lead` | Vazifasiz sdelka = ochiq lid, ustida ochiq topshiriq yo'q |
| Lidni ishlagan operator / yopgan admin | YO'Q | `LeadEvent.ActorUserId` faqat harakat | `Lead.AssigneeUserId` + `Lead.ClosedByUserId` |

### 3.2 Chiquvchi admin

```
OYLIK = Oklad + ( Faol × 1 000 × Ushlab qolish koef
                + Uzaytirgan × 35 000 × Uzaytirish koef
                + Yig'ilgan qarz × 1% ) × Samar.koef − Jarimalar
```

| Kalkulyator so'raydi | Holat | Manba | Izoh |
|---|---|---|---|
| Oy boshi / oxiri **faol** o'quvchi | QISMAN | `StudentGroup.Status=active`, `MembershipLifecycle.BillableInMonth`, `JournalEntry.Present`, `StudentLedger` | Uch shart bor, bitta `IsKpiActive(student, date)` yo'q. Oy boshi — **snapshot** shart (orqaga sanash — `RecordedAt` saboqi) |
| Ketgan (nazoratdagi) | QISMAN | `Student.ArchiveReason`, `ActionReason` (`archive_student`, `remove_active`, `freeze`), `StudentGroup.LeftAt` | Sababda **«nazoratdan tashqari»** belgisi yo'q → `ActionReason.OutOfControl` |
| Kursi tugagan | BOR | `Group.EndDate`, `Group.Status=completed`, `GroupCompletionRules` | Guruh yopilganda active a'zolar tugatgan |
| Uzaytirgan | YO'Q | Guruh yopib ko'chirish bor (`ClassesController`), hodisa yozilmaydi | `StudentExtension` (sana, eski/yangi guruh, kim). Oylik to'lov uzaytirish EMAS |
| Oy boshi qarz / yig'ilgan qarz | QISMAN | `MonthlyCharge`, `FinanceTransaction(tuition, refund)`, `StudentLedger`, `StudentGroupLedger` | Yig'ilgan bor. Oy boshi — snapshot. «10-sanadan keyin undirilgan»: `FinanceTransaction.Date ≥ oy-10`, `Month ≤ joriy` |
| Samaradorlik | BOR | `WorkTask` | |
| Sababi aniqlanmagan ketish | QISMAN | `Student.ArchiveReason` bo'sh / «noma'lum» | Sabab majburiy bo'ladi |
| Tiket | YO'Q | — | `KpiTicket` |
| Kelmaganga xabar 24 soat | BOR | `LessonAttendanceReminderService`, `AutoMessageRule`, `SmsLog` | Auto-band |
| Ketma-ket 2/3 dars kelmaganlar | QISMAN | `JournalEntry.Present`, `StudentAttendanceController` | «Ketma-ket N» ro'yxati yo'q — «Bugun» sahifasining asosiy bloki |
| Qarzdorga qo'ng'iroq, va'da sanasi, 15 kun eskalatsiya | BOR | `ContactRequest`/`ContactAttempt` («Bog'lanish kerak»), `ContactReport.ByStaff`, `PaymentReminderService` | Xodim kesimida urinish/yetib borish/natija bor — shu orqali yuritilsin |
| Xona / texnika / kutish zonasi / taxta | YO'Q | — | Faqat qo'lda cheklist; `Room` bo'yicha belgilash mumkin |
| O'qituvchi so'rovi 2 kun SLA | BOR | `SupportSlot`/`BotSupportMessage`, `WorkTask` | |

---

## 4. Tizimda hali yo'q narsalar

Birinchi 5 tasi — **ma'lumot yig'ishni boshlash** uchun; kechiksa, qayta tiklab bo'lmaydigan
oylar yo'qoladi (Retention bonus rejasidagi o'qituvchi tarixi saboqi).

| # | Nima yo'q | Qayerga qo'shiladi | Hajmi |
|---|---|---|---|
| 1 | Lidga mas'ul xodim va yopgan xodim | `Lead.AssigneeUserId`, `Lead.ClosedByUserId`, `Lead.ClosedAt`; bosqich almashganda avtomatik (`LeadsController`) | 3 maydon + migratsiya + kanban kartasi |
| 2 | Sinovga keldi/kelmadi | `TrialLesson.Result`: `came`, `no_show`, `stayed`, `left`; `AttendedAt` | 1 maydon + 2 tugma |
| 3 | Uzaytirish hodisasi | `StudentExtension(StudentId, FromGroupId, ToGroupId, Date, ByUserId)` — guruh yopib ko'chirishda avtomatik + qo'lda tugma | 1 entity |
| 4 | «Nazoratdan tashqari» belgisi; sabab majburiy | `ActionReason.OutOfControl` (bool); arxivlash/chiqarishda sabab majburiy (sozlama) | 1 maydon + validatsiya |
| 5 | Oy boshi snapshot | `KpiMonthSnapshot` — oyning 1-kuni 00:05 fon xizmati (`TuitionAccrualService` shablon) | 1 entity + BackgroundService |
| 6 | Xodim KPI roli va okladi | `KpiProfile(UserId, RoleCode: intake_admin/retention_admin/call_operator, StartMonth, GuaranteeUntilMonth)` + `KpiProfileSalary` (oklad tarixi, EffectiveFrom — 5.4) | 2 entity. `AppUser.Position` tegilmaydi |
| 7 | Qoidalar to'plami | `KpiRuleSet(RoleCode, EffectiveFrom, Json, CreatedBy, Note)` — konstantalar va 3 jadval | 1 entity + sahifa |
| 8 | Tiket | `KpiTicket(UserId, Date, ReasonCode, CriterionNo 1–13, CallId?, Note, IssuedBy, Status: proposed/confirmed/disputed/cancelled)` | 1 entity + sahifa |
| 9 | Kunlik cheklist | `ChecklistTemplate` + `ChecklistTemplateItem(RoleCode, TimeBlock, Text, Norm, KpiTag, CriterionNo, AutoCheckKey)` + `ChecklistEntry(UserId, Date, ItemId, State ✓✗–, Source: manual/auto)` | 3 entity + sahifa |
| 10 | Oylik yopilgan natija | `KpiMonthResult(UserId, Month, InputsJson, CoefsJson, Salary, GuaranteeApplied, RuleSetId, Status: draft/confirmed, ConfirmedBy)` | 1 entity |
| 11 | Ketma-ket N dars kelmaganlar | `KpiMetrics.AbsenceStreaks(date)` — `JournalEntry` dan, saqlanmaydi | 1 so'rov |
| 12 | Xodim ish stoli | O'z normasi, cheklisti, oy prognozi; `PermissionRules` orqali `kpi.self` | 1 sahifa |

---

## 5. Qanday bo'lim quramiz

**Tamoyil:** KPI bo'limi hech qanday operatsion ma'lumot yaratmaydi. Lid — Lidlarda, to'lov —
Kassada, davomat — Jurnalda, qo'ng'iroq — Qo'ng'iroqlarda qoladi. KPI faqat o'qiydi, koeffitsient
hisoblaydi va o'ziga tegishli 4 narsani yozadi: cheklist belgisi, tiket, oy boshi snapshot,
tasdiqlangan oylik natija.

### 5.1 Menyu va sahifalar

**KPI → Bugun**
- Xodim tanlash (yoki o'zi)
- Kunlik norma: reja / fakt (lid, sinov, shartnoma, qo'ng'iroq, muloqot vaqti)
- Cheklist — vaqt bloklari bo'yicha; auto-bandlarni tizim belgilaydi
- Chiquvchi uchun: 2/3 dars kelmaganlar, bugun to'lov muddati kelganlar, 3 hafta ichida tugaydigan guruhlar — bosilsa mavjud sahifaga o'tadi
- Signal: kechikkan vazifa, javobsiz DM, qaytarilmagan qo'ng'iroq

**KPI → Oy**
- Excel KALKULYATOR'ning tizimdagi ko'rinishi, har xodim uchun
- Kirish raqamlari avtomatik, har birining yonida «qayerdan» havolasi
- Konversiyalar, koeffitsientlar, prognoz oylik, kafolat
- «Shu tezlikda davom etsa oy oxirida…»

**KPI → Tiketlar**
- Haftalik sifat nazorati: 5 ta tasodifiy qo'ng'iroq (`Call.RecordingFile` bor)
- 13 mezon bo'yicha baholash; Gemini taklifi (`Call.AiAnalysis` bor) — rahbar tasdiqlaydi
- Tiket: sabab, mezon, qo'ng'iroq havolasi, xodim e'tirozi

**KPI → Yopish**
- Oy tugagach har xodim uchun natija, snapshot bilan solishtirish
- Rahbar tasdiqlaydi → `KpiMonthResult` muzlanadi, Excel (`ExcelExport` bor)
- Birlik iqtisodiyoti: 3 indikator (3% / 5% / 10%)
- Yuqori chegara (7.5 mln) oshsa — alohida tasdiq

**KPI → Qoidalar**
- Rollar: kiruvchi / chiquvchi / operator; xodimga biriktirish, oklad, boshlanish oyi, kafolat muddati
- Har rol uchun konstantalar va pog'ona jadvallari
- Versiyalash: o'zgarish keyingi oydan, eski oylar eski versiya bilan (5.4)
- Cheklist shablonlari va tiket sabablari katalogi

**Mavjud bo'limlar — o'zgarmaydi:** Lidlar (+3 maydon), Sinov darsi (+natija), Jurnal/Davomat,
Bog'lanish kerak, Qo'ng'iroqlar, Instagram, Moliya/Kassa, O'quvchilar (arxiv sababi),
Topshiriqlar, Sozlamalar → Sabablar (+OutOfControl).

### 5.2 Kod tuzilmasi

```
Domain/Entities.cs
  KpiProfile · KpiProfileSalary · KpiRuleSet · KpiTicket · ChecklistTemplate · ChecklistTemplateItem
  ChecklistEntry · KpiMonthSnapshot · KpiMonthResult · StudentExtension
  + Lead.AssigneeUserId, Lead.ClosedByUserId, Lead.ClosedAt
  + TrialLesson.AttendedAt (Result: came | no_show | stayed | left)
  + ActionReason.OutOfControl

Application/Services/Kpi/
  KpiRules.cs             — pog'ona jadvalidan koeffitsient (sof funksiya, Excel INDEX/MATCH ekvivalenti)
  KpiCalculator.cs        — kirish raqamlari → oylik (sof funksiya; 12 Excel ssenariysi = 12 test)
  KpiMetricsService.cs    — tizimdan kirish raqamlarini yig'ish (xodim × oy), jonli, saqlanmaydi
  KpiActiveStudentRule.cs — «faol o'quvchi» yagona ta'rifi (30 kun / 45 kun / active)
  ChecklistAutoCheck.cs   — AutoCheckKey → tekshiruv (masalan "calls.missed_returned_by_0945")
  KpiSnapshotService.cs   — BackgroundService, oyning 1-kuni snapshot
  KpiDailyDigest.cs       — 09:00 norma va 18:00 natija (TelegramService orqali)
  KpiMonthCloseService.cs — natijani muzlatish, Excel

Server/Controllers/KpiController.cs (+ KpiSelfController — xodimning o'z ko'rinishi)
Client/src/pages/admin/kpi/  TodayPage · MonthPage · TicketsPage · ClosePage · RulesPage
Tests/  KpiCalculatorTests (Excel ssenariylari) · KpiActiveStudentRuleTests · ChecklistAutoCheckTests
```

### 5.3 «Faol o'quvchi» — bitta joyda kodlanadigan ta'rif

```
IsKpiActive(student, asOf) =
     kamida bitta StudentGroup.Status == "active" && IsActive          (sinov va muzlatilgan — faol EMAS)
  && oxirgi Present==true jurnal yozuvi asOf − 30 kundan keyin         (30 kundan ortiq kelmagan — EMAS)
  && eng eski to'lanmagan MonthlyCharge muddati asOf − 45 kundan keyin (45 kundan ortiq qarz — EMAS)
  && !student.IsArchived
```

Ikki joyda ishlatiladi (oy boshi snapshot va oy oxiri hisob) — shuning uchun bitta joyda turadi
(`SalaryLedger.BillableInMonth` → `MembershipLifecycle` ga chiqarishdagi kabi).

### 5.4 Summalar bir marta kiritiladi, keyingi oylar o'zi oladi ⭐

Oklad, bonus birligi, jarima, kafolat, pog'ona jadvallari — hammasi **oy bilan bog'langan versiya**
sifatida saqlanadi. Qoida: *M oy uchun hisob — «EffectiveFrom ≤ M» bo'lgan eng so'nggi versiyani
oladi*. Bir marta kiritilgan summa qo'shimcha harakatsiz keyingi hamma oylarga o'tadi; o'zgartirilsa —
o'zgargan oydan yangisi, oldingi oylar eskisi bilan qoladi.

```
KpiRuleSet          — rol uchun umumiy qoidalar (bonus birligi, jarima, kafolat, jadvallar)
  RoleCode · EffectiveFrom "2026-10" · Json · CreatedBy · CreatedAt · Note

KpiProfileSalary    — xodimning shaxsiy okladi (har xodimda o'z tarixi)
  UserId · EffectiveFrom "2026-10" · BaseSalary · Note

RuleFor(role, month)   = KpiRuleSets.Where(r.EffectiveFrom <= month).OrderByDesc(EffectiveFrom).First()
SalaryFor(user, month) = KpiProfileSalaries.Where(s.EffectiveFrom <= month).OrderByDesc(EffectiveFrom).First()

Misol:  okt 2026  oklad 2 500 000  (kiritildi 1 marta)
        noy, dek  2 500 000        (hech narsa kiritilmadi — o'zi oldi)
        15-dek:   3 000 000 ga o'zgartirildi, «kuchga kirish: yanvar»
        dek       2 500 000        (dekabr eski qiymat bilan tugaydi)
        yan, fev  3 000 000        (yangisi o'zi qo'llanadi)
```

Himoya qoidalari:
1. O'zgartirish formasi «kuchga kirish oyi»ni so'raydi — standart **keyingi oy**, joriy oy mumkin, o'tgan oy taqiqlanadi.
2. **Yopilgan oy** (`KpiMonthResult.Status = confirmed`) o'z versiyasi bilan muzlatilgan (`RuleSetId` saqlanadi) — keyin qoida o'zgarsa ham summasi o'zgarmaydi.
3. Hech narsa o'chirilmaydi — eski versiyalar «Qoidalar» sahifasida tarix (kim, qachon, nimadan nimaga). O'zgartirish faqat `kpi.rules` ruxsati bilan.

---

## 6. Doimiy nazorat qanday ishlaydi

Excel'da nazorat qo'lda («09:00 raqamlar aytiladi», «kun oxirida Telegramga», «haftada 5 qo'ng'iroq»,
«oy oxirida sariq kataklar»). Tizimda bu ritmlar avtomatlashadi, odam faqat **qaror** qiladigan joyda qoladi.

| Ritm | Nima bo'ladi | Kim ko'radi | Mexanizm |
|---|---|---|---|
| **Har kuni 09:00** | Kunlik norma: «bugun 4 sinov, 3–4 shartnoma, 40 qo'ng'iroq; tunda 6 DM javobsiz, 2 qaytarilmagan qo'ng'iroq». Chiquvchiga: «3 dars kelmagan 4 o'quvchi, bugun to'lov muddati 11 kishi, 3 hafta ichida tugaydigan 2 guruh» | Xodim (Telegram + «Bugun») | `KpiDailyDigest` — `TelegramService`/`BotUser`; `WorkTaskReminderService` naqshi |
| **Kun davomida** | Auto-bandlar belgilanadi. Signal: DM 15 daq. oshdi, kiruvchi qo'ng'iroq javobsiz, kechikkan vazifa | Xodim; rahbarga faqat qizil | `ChecklistAutoCheck` (10 daq.), `UserNotification`/`FcmService` |
| **Har kuni 18:00** | Kunlik raqamlar avtomatik (Excel'dagi «Kunlik raqamlar» bloki). Xodim qo'lda yubormaydi, faqat izoh. Cheklist to'ldirilmagan kun — «bajarilmagan» | Xodim + rahbar (Telegram guruh) | `KpiDailyDigest`, `TelegramGroup` |
| **Har hafta** | Sifat nazorati: har xodimdan 5 tasodifiy qo'ng'iroq (yozuvi bor, >60 s). Gemini 13 mezonga dastlabki baho (`Call.Transcript` + `AiAnalysis` bor). Rahbar tasdiqlaydi → tiket. 10/10 — geymifikatsiya | Rahbar (Tiketlar) | `CallsController /{id}/analyze` + 13-mezon prompti; `KpiTicket` |
| **Oyning 1-kuni** | Snapshot: faol soni, qarz summasi, ochiq lidlar — har xodim uchun. O'tgan oy «Yopish»da qoralama | Rahbar | `KpiSnapshotService` |
| **Oyning 1–3 kuni** | Rahbar har xodim natijasini ko'radi (har kirish raqami manbaga havola bilan), koeffitsientlar, jarimalar, kafolat, chegara. Xodim ko'radi, e'tiroz bildiradi. Tasdiq → muzlanadi → Excel → maosh to'lovi (`FinanceTransaction expense/salary`; KPI pul chiqarmaydi, summani aytadi) | Rahbar + xodim | `KpiMonthResult`, `ExcelExport` |
| **Har 2 oy (kalibrovka)** | «Qoidalar»da haqiqiy taqsimot (ketish % qaysi pog'onaga tushdi, konversiya qayerda) — rahbar pog'onalarni yangi versiya qilib saqlaydi, keyingi oydan. Kafolat `KpiProfile.GuaranteeUntilMonth` da tugaydi | Rahbar | `KpiRuleSet.EffectiveFrom` |
| **Har kuni ertalab (AI)** | `CenterAiAnalysisService` ga «xodimlar» bo'limi: kim normani bajarmadi, qaysi ko'rsatkich 3 kundan beri pasayyapti, qaysi o'quvchilar ketish xavfida. Raqamlar deterministik, AI narrativ | Rahbar | `CenterAiAnalysisService` |

**Rahbar dashboardidagi 6 signal (ostonalar Excel'dan):** ketish % > 8 (koef 0.80) · lid→sinovga
kelish < 30% (koef 0.8) · kelgan→shartnoma < 80% (−0.20) · lid oqimi < 250 (reja qayta) ·
bir xodimda 5-tiket (koef bir pog'ona pastga) · «noma'lum» sababli ketish (50 000 jarima).
Har signal «Oy» sahifasidagi qatorga olib boradi.

---

## 7. Bosqichma-bosqich reja

Tartib muhim: 0-bosqich ma'lumot yig'ishni boshlaydi.

### 0 — Ma'lumot yig'ishni hozirdan boshlash (1–2 kun)
- `Lead.AssigneeUserId`, `ClosedByUserId`, `ClosedAt` — bosqich almashganda avtomatik
- `TrialLesson` — «keldi / kelmadi» tugmalari, `AttendedAt`
- `StudentExtension` — guruh yopib ko'chirishda avtomatik, qo'lda tugma
- `ActionReason.OutOfControl` + arxivlashda sabab majburiy
- `KpiMonthSnapshot` + fon xizmati (1-oktabrdan birinchi snapshot)

### 1 — Qoidalar va kalkulyator (3–4 kun)
- `KpiRuleSet`, `KpiProfile`, `KpiProfileSalary`, «Qoidalar» sahifasi — Excel konstantalari va 3 jadval seed
- `KpiRules` + `KpiCalculator` sof funksiyalar; 12 Excel ssenariysi test (so'mgacha mos)
- `KpiActiveStudentRule` — ta'rif tasdiqlangach

### 2 — Metrikalar va «Oy» (4–5 kun)
- `KpiMetricsService` — 15 kirish raqami tizimdan; har raqam yonida manba havolasi
- «Oy» sahifasi — jonli kalkulyator, prognoz; xodimning o'z ko'rinishi
- Shu bosqichdan Excel kerak emas

### 3 — Cheklist va «Bugun» (4–5 kun)
- Shablonlar — ikkala Excel cheklisti seed (66 band, vaqt bloklari, KPI teglari, mezon raqamlari)
- `ChecklistAutoCheck` — ~30 auto-band (qo'ng'iroq, DM, vazifa, eslatma, jurnal)
- Kunlik norma, erta ogohlantirish ro'yxatlari (kelmaganlar, muddati kelganlar, tugayotgan guruhlar)
- 09:00 / 18:00 Telegram digest

### 4 — Tiketlar va sifat nazorati (3 kun)
- `KpiTicket`, sabablar katalogi (13 mezon + Excel tiket sabablari)
- Haftalik 5 qo'ng'iroq tanlash, 13-mezon Gemini prompti, rahbar tasdiqi, xodim e'tirozi

### 5 — Yopish, signal va AI (2–3 kun)
- «Yopish», `KpiMonthResult`, Excel eksport, birlik iqtisodiyoti indikatorlari
- Rahbar dashboardiga 6 signal; `CenterAiAnalysisService` ga xodimlar bo'limi
- Kalibrovka ko'rinishi

**Jami ~3–4 hafta.** 0- va 1-bosqich tugagach tizim oktabrni to'liq o'lchaydi — Excel'dagi
«kafolatlangan minimum» 2 oyi (oktabr–noyabr) tizimda o'lchangan raqamlar bilan o'tadi,
dekabrdan shkala tizimda sozlanadi.

---

## 8. Qarorlar (rahbar hal qiladi)

| # | Savol | Tavsiya | Qaror |
|---|---|---|---|
| 1 | Bonus A: 1 000 yoki 6 000? B: 35 000 yoki 45 000? | Birlik iqtisodiyoti ikkalasiga yo'l beradi (A ≤ 6 750, B ≤ 45 000). 1 000 bilan normal oy = kafolat; 6 000 bilan kuchli oy 8,75 mln (chegara 7,5). Oraliq (4 000 / 40 000) maqsadga yaqinroq bo'lishi mumkin. Qoidalar sahifasida sozlanadi | ☐ |
| 2 | «Faol o'quvchi» 5.3 dagidek? Muzlatilgan faolmi? | Muzlatilgan — faol emas (Retention bonus bilan mos) | ☐ |
| 3 | Bitta odam ham kiruvchi, ham chiquvchi? | `KpiProfile` da ikki rol; oklad bitta, bonuslar ikkala qoidadan | ☐ |
| 4 | Telegram DM javob vaqti? | Birinchi versiyada faqat Instagram. Telegram — `LeadTelegramMessage.RepliedByUserId` keyin | ☐ |
| 5 | Tiketni Gemini o'zi qo'yadimi? | Yo'q — faqat `proposed`, rahbar tasdiqlaydi | ☐ |
| 6 | Xodim o'z KPI'sini real vaqtda ko'radimi? | Ha, e'tiroz tugmasi bilan | ☐ |
| 7 | `SalaryLedger` ga ulanadimi? | Yo'q — Retention bonus qaroridagidek, KPI summani aytadi, to'lov mavjud maosh yo'li bilan | ☐ |
| 8 | Summalar EffectiveFrom bilan versiyalanadi (5.4) | **Qabul qilindi (2026-09-09)** | ☑ |

---

*Manba fayllar: `kpi/KPI Kiruvchi Admin.xlsx` (7 varaq), `kpi/KPI Chiquvchi Admin.xlsx` (6 varaq),
`kpi/Kunlik cheklist Kiruvchi admin.xlsx`, `kpi/Kunlik cheklist ChiquvchiAdmin.xlsx`,
`kpi/Kiruvchi admin qabul qilish skripti.doc`. Kod: `Domain/Entities.cs`, `Application/Services`
(LeadAnalytics, LeadOutcome, ContactReport, RetentionBonusService, CenterAiAnalysisService,
MembershipLifecycle, SalaryLedger), `Server/Controllers` (Leads, Calls, WorkTasks, ActionReasons,
Students, Classes), `RETENTION-BONUS-PLAN.md`.*
