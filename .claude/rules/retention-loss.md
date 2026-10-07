---
description: «Ketish sabablari» (Retention / Loss) hisoboti — "ketdi" ta'rifi, sabab manbalari (MembershipReasonLog), o'qituvchiga yozish, foiz maxraji; «Davomat olinmagan guruhlar» (AttendanceWatch).
paths:
  - "WunderkindLC.Application/Services/RetentionLossReport.cs"
  - "WunderkindLC.Application/Services/MembershipReasons.cs"
  - "WunderkindLC.Application/Services/AttendanceWatch.cs"
  - "WunderkindLC.Server/Controllers/RetentionLossController.cs"
  - "WunderkindLC.Server/Controllers/DashboardController.cs"
  - "WunderkindLC.Client/src/pages/admin/reports/RetentionLossPage.tsx"
  - "WunderkindLC.Client/src/components/attendance/**"
---

# «Ketish sabablari» (Retention / Loss) qoidalari

"Hisobotlar → O'qituvchilar → Ketish sabablari" (`/admin/hisobotlar/ketish-sabablari`, ruxsat
**`teachers.retention`**). Savol: tanlangan davrda **qaysi o'qituvchidan** nechta o'quvchi KETDI,
**NEGA** ketdi va bu uning AKTIV o'quvchilariga nisbatan qancha. O'qituvchi sahifasining
«Samaradorlik» tabida ixcham ko'rinishi bor (`TeacherRetentionCard`, oxirgi 6 oy, o'sha endpoint).
Migratsiya: yangi jadval **`MembershipReasonLogs`** (§3).

## 1. "Ketdi" — bosh sahifa kartochkasi bilan AYNAN bir xil

Ta'rif QAYTA YOZILMAGAN: `DashboardSummary.LeftEvents` — «Ketgan o'quvchilar» kartochkasi
(`LeftStudentsInMonth`) ham, bu hisobot ham AYNAN shu funksiyani chaqiradi
(`.claude/rules/course-analytics.md` §1):

- (o'quvchi, kurs) juftligi bo'yicha oraliqlar birlashtiriladi — **kurs ichida guruh almashtirish
  (7 kungacha tanaffus) KETISH EMAS**;
- oraliqni `completed` a'zolik yopgan bo'lsa (sertifikat bilan tugatish) — **KETISH EMAS**;
- "aktivdan ketgan" = oraliqdagi birorta a'zolik aktivlashgan (yopilgan davrlar ham —
  `FirstActivatedAt`), aks holda "sinovdan ketgan".

⚠️ BOSHQA KURSGA o'tish kurs kesimida ketish bo'lib sanaladi (kartochka bilan bir xil). Jadvalda
shunday qatorga **«Boshqa guruhda o'qiyapti»** belgisi chiqadi (`StillStudying`) — o'quvchi
markazdan emas, faqat shu kursdan ketgan.

⚠️ MUZLATISH ketish EMAS (oraliq ochiq qoladi). O'quvchini ARXIVLASH ham a'zolikni yopmaydi —
guruhdan chiqarilmagan arxivdagi o'quvchi bu yerga tushmaydi (kartochka bilan bir xil). Arxiv
sababi faqat SABAB manbai sifatida ishlatiladi (§3, 4-qadam).

⚠️ `YearFreeze` bu yerda TEKSHIRILMAYDI (`year-freeze.md` §1).

Sanoq birligi — **ketish** (har (o'quvchi, kurs) ketishi alohida): diagramma, jadval va
"Ketganlar" soni bir xil. Takrorsiz o'quvchilar soni alohida ko'rsatiladi (`Totals.Students`).

## 2. O'qituvchiga yozish va FOIZ

- Ketish oraliqni YOPGAN a'zolikning guruhiga, **o'sha oyda guruhni o'qitgan** o'qituvchiga
  yoziladi: `GroupTeacherHistory.TeacherAtMonth`, tarix bo'lmasa `Group.TeacherId`.
  O'rinbosar hisobga olinmaydi — guruh asosiy o'qituvchiniki.
- **Maxraj** (`ActiveBase`) = davr ichida AKTIV (pullik) bo'lgan (o'quvchi, kurs) juftliklari —
  `MembershipLifecycle.BillableInMonth(sg, oy)` (yopilgan davrlar + chiqish chegarasi bilan),
  o'sha oydagi o'qituvchiga.
- **Ketish foizi** = AKTIVDAN ketganlar ÷ maxraj × 100; **ushlab qolish** = 100 − foiz.
  Sinovdan ketganlar foizga KIRMAYDI (maxrajda sinovdagilar yo'q).
- ⚠️ Aktivdan ketgan har bir juftlik maxrajga ham qo'shiladi — aks holda davrdan OLDIN
  muzlatilib, davrda chiqarilgan o'quvchi foizni 100 dan oshirardi.

## 3. SABAB — qayerdan olinadi (`MembershipReasons.Resolve`)

Ilgari sabab FAQAT audit matnida edi («… — sabab: X»). Endi tuzilgan yozuv ham bor:
**`MembershipReasonLog`** (`StudentId`, `GroupId`, `Action`, `Date`, `ReasonId`, `ReasonLabel`
surati, `CreatedAt`). Yagona yozuvchi — `MembershipReasons.Record`, chaqiriladigan joylar
(hammasi `ClassesController`):

| Amal | `Action` | `Date` |
|---|---|---|
| Guruhdan chiqarish (`RemoveMember`) | `remove` | chiqish sanasi |
| Guruh almashtirish, eski tomoni (`TransferMember`) | `transfer` | eski guruhdan chiqish sanasi |
| Muzlatish, yakka va ommaviy (`FreezeCoreAsync`) | `freeze` | muzlatish sanasi |
| Guruhni yopish (`Close` → `CloseMembersAsync`) | `close` | yopish sanasi |

- **Sababsiz amal YOZILMAYDI** (qator yo'q). Yozuv chaqiruvchining tranzaksiyasida (audit naqshi).
- Qator o'zgarmaydi/o'chirilmaydi — tarix. A'zolikka (`StudentGroup`) ustun qo'shilmagani
  ATAYIN: bitta a'zolik muzlatilib keyin chiqariladi (ikki sabab) yoki qayta qo'shilib yana
  chiqariladi — bitta ustun eski sababni yolg'on qoldirardi.
- Arxivlash (`Archive`), sertifikat bilan tugatish, sinovga qaytarish — yozilmaydi (ketish emas
  yoki sabab boshqa joyda).

**Aniqlash tartibi** (birinchi topilgani):

1. tuzilgan yozuv `remove`/`transfer`/`close`, sanasi **AYNAN** chiqish sanasi;
2. a'zolik chiqishda MUZLATILGAN bo'lsa (`FrozenAt ≤ LeftAt`) — `freeze`/`close` yozuvi, sanasi
   AYNAN `FrozenAt` (muzlatilib, keyin sababsiz chiqarilgan holat);
3. audit matni (shu jadvaldan OLDINGI tarix): chiqishdan keyingi ENG OXIRGI «Guruhdan
   chiqarildi»/«Guruh almashtirildi» yozuvi — u sababsiz bo'lsa ESKIROQLARIGA qaralmaydi; keyin
   xuddi shunday «Muzlatildi»/«Aktiv muzlatildi»;
4. o'quvchi chiqqandan KEYIN arxivlangan bo'lsa — `Student.ArchiveReason`;
5. topilmasa — «Sabab ko'rsatilmagan».

⚠️ Sana bo'yicha AYNAN moslik ATAYIN: "eng oxirgi sababli yozuv" olinsa, bugungi sababsiz
chiqishga o'tgan yilgi sabab yopishib qolardi.

**«Nazoratdan tashqari»** — `ActionReason.OutOfControl` (KPI bilan bir xil manba): avval
`ReasonId` bo'yicha, id yo'q bo'lsa (eski tarix) NOM bo'yicha (registrsiz). Bayroq JONLI o'qiladi —
sozlamada o'zgartirilsa hisobot ham o'zgaradi.

⚠️ KPI (`KpiMetricsService.LeaversAsync`) va AI tahlil quruvchilari (`GroupSnapshotBuilder`,
`TeacherSnapshotBuilder`) hamon faqat audit matnini o'qiydi — ular TEGILMAGAN. Ularni shu
manbaga o'tkazish alohida ish (bu qoidaga tayanadi).

## 4. Ruxsat

`[AdminPerm("teachers.retention", ReadRequiresPerm = true)]` — javobda o'quvchi ISMLARI va
sabablar bor, odatdagi "har qanday xodim GET o'qiy oladi" qoidasi bu yerda ishlamaydi. Yozuvchi
amal yo'q. Meros: `teachers` bo'limi kaliti sahifani ochadi, `teachers.list` OCHMAYDI.
Testlar: `RetentionLossReportTests` (manba matni + `PermissionRules`).

## 5. Ko'rinish

- Filtrlar bitta qatorda: davr (oy — oy), o'qituvchi, kurs, tur (Hammasi · Aktivdan · Sinovdan).
  Tur diagramma/jadvalga ta'sir qiladi; "Aktivdan/Sinovdan" sonlari va foiz har doim ikkalasi.
- `?teacher=<id>` — boshlang'ich o'qituvchi filtri (o'qituvchi sahifasidagi «Batafsil →»).
- Ranglar tekshirilgan (dataviz validatori): **`#e11d48` aktivdan**, **`#d97706` sinovdan**
  (CVD ΔE 9.4, oddiy ΔE 16.6). Sabablar donut'i — `leads/stats/palette.ts` kategorial palitrasi,
  5 tadan keyingisi «Boshqa» (kulrang), rang sabab NOMIGA bog'langan.
- ⚠️ Son va foiz bitta grafikda ikki y-o'q bilan KO'RSATILMAYDI — "O'qituvchilar bo'yicha
  ketganlar" va "Ketish foizi" alohida grafiklar (`course-analytics.md` §6).

## 6. «Davomat olinmagan guruhlar» — `AttendanceWatch`

Manba BITTA: `GET /api/admin/dashboard/today-lessons` («Nazorat → Davomat qilinmagan guruhlar»
sahifasi, guruhlar ro'yxatidagi sariq qator va «O'quvchilar davomati» sahifasidagi bo'lim).

- Kunning darslari — `AttendanceWatch.LessonsOn` → `TeacherHomeService.PlanLessons` (o'qituvchi
  jadvali bilan bir xil): guruh kunlari + **KO'CHIRILGAN darslar** (asl kunidan olinadi, yangi
  kuniga yangi vaqt bilan qo'shiladi), guruh umri (`StartDate`, `EndDate`/`ArchivedAt`),
  **o'rinbosar** (`SubstituteTeacherName` — o'sha kuni davomatni u oladi).
- ⚠️ Ilgari monitor faqat hafta kuniga qarardi: ko'chirilgan dars asl kunida "davomat olinmagan"
  bo'lib qizarardi, yangi kunida esa ko'rinmasdi.
- "Davomat olindi" = o'sha kuni `Conducted` LessonNote (eslatma xizmati bilan bir xil).
- **Bosqich** (`Phase`: upcoming | started | ended) va **`Overdue`** (boshlangan, davomat yo'q) —
  keshdan KEYIN, har so'rovda markaz vaqti (`AppClock.Now`) bilan: kesh 5 daqiqagacha eski
  bo'lishi mumkin, soat esa yo'q. Vaqti noma'lum bugungi dars — `started` (`not_filled`
  eslatmasi bilan bir xil).
- ⚠️ Loyihada BAYRAM kunlari modeli YO'Q — bunday kunda ham dars rejada turadi (jadval va maosh
  bilan bir xil).
- «O'quvchilar davomati» sahifasida: «Davomat olinmagan» kartochkasi (kechikkanlar soni) va
  ro'yxat (Kechikkanlar · Hammasi), to'liq monitorga havola faqat `classes.notAttended` bo'lsa.
  Filtr/tartib — `lib/attendanceWatch.ts` (qoidani QAYTA hisoblamaydi).
