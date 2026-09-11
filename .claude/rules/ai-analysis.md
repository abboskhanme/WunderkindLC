---
description: AI tahlil (Gemini) — markaz kunlik tahlili, o'quvchi, O'QITUVCHI, GURUH, VORONKA (lid formalari · daraja testlari) va BOG'LANISH KERAK (follow-up navbati) tahlili — oqim, ketish sabablari, davomat, jurnal intizomi, imtihon, to'lov, kanallar, sotuv konversiyasi va qo'ng'iroq javoblari.
paths:
  - "WunderkindLC.Application/Services/*Ai*.cs"
  - "WunderkindLC.Application/Services/GeminiService.cs"
  - "WunderkindLC.Application/Services/TeacherSnapshotBuilder.cs"
  - "WunderkindLC.Application/Services/GroupSnapshotBuilder.cs"
  - "WunderkindLC.Application/Services/StudentProfileBuilder.cs"
  - "WunderkindLC.Server/Controllers/AiAnalysisController.cs"
  - "WunderkindLC.Client/src/pages/admin/students/AiAnalysis*.tsx"
  - "WunderkindLC.Client/src/pages/admin/teachers/TeacherAiPanel.tsx"
  - "WunderkindLC.Client/src/pages/admin/classes/GroupAiPanel.tsx"
  - "WunderkindLC.Client/src/components/ai/**"
  - "WunderkindLC.Client/src/lib/ai.ts"
  - "WunderkindLC.Client/src/components/dashboard/CenterAiAnalysisCard.tsx"
  - "WunderkindLC.Client/src/api/services/aiAnalysis.ts"
  - "WunderkindLC.Client/src/api/services/funnelAi.ts"
---

# AI tahlil qoidalari (Gemini)

- **UMUMIY ARXITEKTURA (oltala tahlilda ham bir xil):** RAQAMLAR DETERMINISTIK hisoblanadi (kod),
  AI faqat NARRATIV yozadi (o'zbekcha) va 0..100 sohaviy baho qo'yadi. Natija
  `ResultJson` (`{ ai, metrics }`) sifatida saqlanadi — shu sabab eski tahlil ochilganda ham
  diagrammalar ishlaydi. Gemini javobi ```json fence'dan tozalanadi va `Sanitize` bilan null'lardan
  himoyalanadi (format buzilsa — "AI javobini o'qib bo'lmadi", yozuv SAQLANMAYDI).
  **KUNIGA BIR MARTA**: shu kun yozuvi bo'lsa Gemini CHAQIRILMAYDI, mavjudi qaytadi
  (`AlreadyToday=true`) — bu tekshiruv API kaliti tekshiruvidan OLDIN (keshlangan natija kalitsiz
  ham ko'rinadi). Kalit: `AppSecrets.GeminiApiKey` (.env), model: `GEMINI_MODEL`.

- **UMUMIY UI QISMLARI:** `components/ai/AiParts.tsx` (ScoreRing, AiRadar, ScoreGrid, PctRow,
  RankedBars, CardList, TextBlock, MiniStat, AiErrorBox) + `lib/ai.ts` (scoreColor, trendInfo,
  escapeHtml, openPrintWindow, printCss). Yangi AI paneli yozilganda SHULAR ishlatiladi (nusxa
  ko'chirilmaydi) — eng yangi misol **`components/ai/FunnelAiPanel.tsx`** (voronka tahlili):
  ScoreRing/AiRadar/ScoreGrid/CardList/TextBlock/RankedBars/AiErrorBox + `lib/ai` ning
  `scoreColor`/`trendInfo`/`escapeHtml`/`openPrintWindow`/`printCss` (PDF chop etish).
  DIQQAT: komponent va oddiy funksiyalar ARALASH bo'lmasin (eslint
  `react-refresh/only-export-components`) — shuning uchun funksiyalar `lib/ai.ts` da.

- **Oltita tahlil:**
  1. **Markaz** — `CenterAiAnalysisService` + `CenterAiSchedulerService` (har kuni ertalab avtomatik),
     `AiAnalysisController` (`api/admin/ai-analysis/center`). KIRISH: superadmin yoki "ai" ruxsatli
     xodim (oddiy admin KO'RMAYDI). Bosh sahifadagi `CenterAiAnalysisCard`.
  2. **O'quvchi** — `StudentsController` (`{id}/ai-analysis`, `{id}/ai-analyses`), ma'lumot manbai
     `StudentProfileBuilder`. Ruxsat: `AdminPerm("students.list")`. UI: `AiAnalysisModal`/`AiAnalysisView`.
  3. **O'QITUVCHI** — `TeacherAiAnalysisService` + `TeacherSnapshotBuilder`, `TeachersController`
     (`{id}/ai-snapshot`, `{id}/ai-analyses`, `{id}/ai-analysis`). Ruxsat: `AdminPerm("teachers.list")`.
     UI: o'qituvchi profilidagi **"AI tahlil"** tabi (`TeacherAiPanel`).
  4. **GURUH** — `GroupAiAnalysisService` + `GroupSnapshotBuilder`, `ClassesController`
     (`{id}/ai-snapshot`, `{id}/ai-analyses`, `{id}/ai-analysis`). Ruxsat: `AdminPerm("classes.list")`.
     UI: guruh sahifasidagi **"AI tahlil"** tabi (`GroupAiPanel`).
  5. **VORONKA** (lid formalari · daraja testlari) — `FunnelAiAnalysisService`, entity
     `FunnelAiAnalysis` (migratsiya `AddFunnelAiAnalysis`, indeks `(Kind, Date)`).
     Endpointlar: `GET/POST api/admin/lead-forms/ai-analyses|ai-analysis` va
     `GET/POST api/admin/level-tests/ai-analyses|ai-analysis`. Ruxsat: `leads` / `schedule`.
     UI: `components/ai/FunnelAiPanel.tsx` — "Formalar" bo'limining IKKALA statistika sahifasida.
     Batafsil quyida.

  6. **BOG'LANISH KERAK** (follow-up navbati hisoboti) — `ContactAiAnalysisService`, entity
     `ContactAiAnalysis` (migratsiya `AddContactAiAnalysis`, indeks `(FromDate, ToDate, Date)`).
     Endpointlar: `GET/POST api/admin/contacts/ai-analyses|ai-analysis`. Ruxsat: `contacts`
     (yaratish — `contacts:create`). UI: `components/ai/ContactAiPanel.tsx` — hisobot tabida.
     ⚠️ **DAVRGA BOG'LANGAN**: kalit `Date` emas, `(FromDate, ToDate)` — sahifada tanlangan
     kun/oy/oraliq tahlil qilinadi va "kuniga bir marta" cheklovi SHU davr bo'yicha ishlaydi.
     Raqamlar `ContactReport.BuildAsync` dan — hisobot sahifasidagi sonlar bilan AYNAN bir xil.
     ⚠️ Promptga o'quvchi ISMI/TELEFONI tushmaydi (voronka tahlilidagi maxfiylik chegarasi bilan
     bir xil sabab); xodim ismi qoladi. Bo'sh davrda Gemini umuman chaqirilmaydi.
     Batafsil: `.claude/rules/contacts.md` §7.55.

- **VORONKA TAHLILI — bitta servis, IKKI tur** (`FunnelAiAnalysisService`, `Kind` =
  `lead-forms` | `level-tests`; `IsValidKind` — klientdan kelgan qiymat shu yerda tekshiriladi,
  noto'g'ri tur Gemini'ga umuman bormaydi):
  - **NEGA BITTA:** ikkala voronkaning SAVOLI ham, ma'lumot SHAKLI ham bir xil —
    keldi → ariza/topshiriq → lid → o'quvchi → **PUL**. Shu sabab ikkita ayri servis/jadval/panel
    YASALMADI: entity ham bitta (`FunnelAiAnalysis`), DTO'lar ham (`FunnelAiMetricsDto`,
    `FunnelAiNarrativeDto`, `FunnelAiScoresDto`, `FunnelAiRecordDto`, `FunnelAiResponseDto`).
  - **RAQAMLAR YANGI HISOBLANMAYDI** — `BuildMetricsAsync` MAVJUD yagona manbalardan o'qiydi
    (`LeadFormService.BuildStatsAsync` / `LevelTestService.BuildOverallStatsAsync`), ya'ni AI
    ko'rsatgan son statistika SAHIFASIDAGI son bilan AYNAN bir xil. Aks holda "AI boshqa raqam
    yozyapti" holati kelib chiqardi.
  - `ResultJson` = `{ ai, metrics }`, **kuniga bir marta** — bugungi yozuv bo'lsa Gemini
    chaqirilmaydi (`AlreadyToday=true`) va bu tekshiruv **API kaliti tekshiruvidan OLDIN**
    (yuqoridagi umumiy qoida bilan bir xil).
  - **Baholar:** `hajm · konversiya · sotuv · barqarorlik · umumiy` (`FunnelAiScoresDto`).
    **Narrativ:** `umumiy, kanallar, voronka, sifat, pul, ozgarishlar, kuchli[], zaif[],
    xavflar[], tavsiyalar[], trend`.
  - **PROMPT `kind` ga qarab ikkiga bo'linadi** (`LeadFormPrompt` / `LevelTestPrompt`): lid
    formalarida gap KANALLAR va reklama byudjeti haqida ("byudjetni qayerga ko'chirish kerak"),
    daraja testlarida esa TESTLAR va ularga yuborilgan bir martalik havolalar haqida.
    ⚠️ **`Views` ning MA'NOSI ham har xil:** formada — havola OCHILISHLARI, testda — YUBORILGAN
    invite'lar (`LevelTestInvite`). Testni ommaviy havola orqali ham topshirish mumkin, ya'ni
    topshiriq invite'dan KO'P bo'lishi mumkin (foiz 100 dan oshadi) — bu **xato emas**, promptda
    ham shunday izohlangan.
  - **`MaxChannels = 15`** — promptga eng ko'p arizali 15 ta forma/test kesimi kiradi (prompt
    shishmasin, token narxi oshmasin); **JAMLANMA sonlar esa BUTUN to'plam bo'yicha** — cheklov
    faqat kesim ro'yxatiga tegishli.
  - **RUXSAT — bo'lim ruxsatida, `ai` da EMAS:** markaz tahlili `ai` ruxsatida (faqat egasi),
    qolgan tahlillar (o'quvchi/o'qituvchi/guruh) o'z BO'LIM ruxsatida — voronka tahlili ham shu
    ikkinchi qoidada (`leads` / `schedule`), chunki u ko'rsatadigan raqamlar o'sha sahifada
    allaqachon ochiq. O'qish darvozalangan: `LeadFormsController` sinf darajasida
    `[AdminPerm("leads.forms", ReadRequiresPerm = true)]`, `LevelTestsController` da esa GET **metod
    darajasida** `[AdminPerm("schedule.levelTests", ReadRequiresPerm = true)]` (saqlangan tahlil ichida
    o'sha voronka raqamlari va tushum turadi).
    ⚠️ **YARATISH — bo'limning "create" amali** (server `PermissionRules.CanWrite`), UI'da ham
    tugma shu bilan darvozalangan: faqat KO'RISH ruxsati bor xodim tahlilni O'QIYDI, lekin
    yangisini boshlay olmaydi — aks holda u tugmani bosib 403 olardi va Gemini chaqiruviga
    (pulga) urinilardi.
  - **Auditga YOZILMAYDI** — tahlil hech qanday ma'lumotni o'zgartirmaydi
    (`.claude/rules/audit.md` — AI tahlil qamrovda ATAYIN yo'q).
  - **UI:** yagona `FunnelAiPanel` (`kind` propi bilan; ikki nusxa YO'Q — turga bog'liq barcha
    matnlar bitta `texts` xaritasida: `Ochilgan` ↔ `Yuborilgan havolalar`, `Ariza` ↔ `Topshirdi`,
    `Formalar` ↔ `Testlar`). Panel ikkala sahifada **KPI kartochkalaridan keyin, birinchi
    grafikdan oldin** turadi — u sahifaning "boshqaruvchi xulosasi": jadvallarni o'qishdan oldin
    nima muhimligini aytadi. Ichida: ScoreRing + radar/baholar, trend chipi, narrativ bloklari,
    kuchli/zaif/xavflar/tavsiyalar, **eng samarali kanallar** (to'lov bo'yicha; to'lov bo'lmasa
    hajm bo'yicha), tahlillar **TARIXI** (qator bosilsa o'shanisi ochiladi) va PDF chop etish.
  - **Testlar:** `WunderkindLC.Tests/FunnelAiTests.cs` (9 ta) — bugungi yozuvda kalitsiz ham
    `AlreadyToday`, turlarning ajratilishi, noto'g'ri `kind`, kalitsizlikda tushunarli xato va
    yozuvning SAQLANMASLIGI, takrorsiz lid, ikki formadagi bir odam, `Views=0` da foiz 0, testda
    `Views` = yuborilgan havolalar, kanallar chegarasi, tarix tartibi.

- ⚠️ **VORONKA TAHLILIDA MAXFIYLIK CHEGARASI — GURUH TAHLILIDAN FARQ QILADI.** Bu keyingi tahlil
  turini yozadigan odam uchun eng muhim qoida:
  - **Promptga FAQAT jamlanma raqamlar ketadi.** Ariza qoldirganlarning ISMI, TELEFONI va
    savolnomaga bergan JAVOBLARI Gemini'ga **HECH QACHON** yuborilmaydi (`FunnelAiMetricsDto`
    ichida ular umuman yo'q).
  - **Nega guruh tahlilida boshqacha:** u yerda o'quvchilar ismi promptga KIRADI, chunki bu ICHKI
    ro'yxat (markazning o'z o'quvchilari) va tavsiya AYNAN shu odamlar haqida bo'ladi ("falonchi
    3 oydan beri kelmayapti"). Voronkada esa murojaatchilar **hali markazga tegishli emas** —
    ular begona odamlarning kontaktlari, va tahlil savoli ham ular haqida emas: "qaysi kanal
    ishlayapti", "kim yozildi" emas. Ya'ni shaxsiy ma'lumot tashqi xizmatga chiqarilishi uchun
    **hech qanday sabab yo'q**.
  - Yangi AI tahlil turi qo'shilayotganda birinchi savol: *promptdagi HAR bir maydon xulosa uchun
    haqiqatan kerakmi?* Kerak bo'lmasa — u yerda umuman turmasin.

- **O'QITUVCHI TAHLILI — ma'lumot manbalari** (`TeacherSnapshotBuilder`, oxirgi 12 oy; hammasi
  MAVJUD yagona manbalardan olinadi, yangi hisoblash mantig'i YARATILMAYDI):
  - **o'quvchi oqimi** (kim kelyapti/ketyapti) — `StudentGroup` sanalari (JoinedAt/ActivatedAt/
    FrozenAt/LeftAt) + `MembershipLifecycle.Tally` → performance va `TeacherActivityReport` bilan
    AYNAN bir xil ta'rif (faqat ARXIVLANMAGAN guruhlar; guruh tugaganda ommaviy yopilgan a'zoliklar
    "ketgan" emas);
  - **ketish sabablari** — sabab alohida ustunda saqlanmaydi: `AuditLog` (`EntityType="Membership"`,
    summary ichida `— sabab: X`; `EntityId="{groupId}:{studentId}"`) + markazdan butunlay
    arxivlanganlar uchun `ArchivedRecord.Reason`;
  - **jurnalni O'Z VAQTIDA to'ldirish** — `SalaryJournalStats` (reja/o'tilgan/`MissedDates` =
    muhlati o'tgan, lekin belgilanmagan darslar; muhlat = `CenterMeta.SalaryGraceDays`) +
    `LessonNote` (mavzu/uy vazifa/`AttendanceTaken` foizi) + qo'yilgan baholar soni;
  - **rivojlanish** — oyma-oy o'rtacha baho (`JournalEntry.Grade`), o'quvchilar davomati va bali
    (`StudentBallService.TeacherAsync`) va testlar (`TestResult`/`TestScore`);
  - qo'shimcha: o'qituvchining O'Z davomati (`TeacherAttendance`) va guruh ota-onalaridan kelgan
    shikoyat/takliflar (`Feedback`).
  DIQQAT: `GET {id}/ai-snapshot` — AI'SIZ ham ishlaydi (Gemini chaqirilmaydi). Tab ochilganda barcha
  diagramma/jadval shu endpointdan to'ladi; AI xulosasi esa alohida, tugma bosilganda yaratiladi.

- **O'QUVCHILARNING O'QITUVCHI HAQIDAGI FIKRI** (migratsiya `AddTeacherReviews`, entity
  `TeacherReview`): o'qituvchini rivojlantirish uchun yig'iladigan **MATNLI** manba — AI aynan
  shundan raqamlar ko'rsatmaydigan narsani (tushuntirish uslubi, munosabat, adolatlilik) biladi.
  • **KIM YOZADI:** FAQAT `admin`/`superadmin` (+platforma egasi) — o'quvchi profili →
    «Fikr-mulohaza» tabi → "O'qituvchilar haqida fikr". O'quvchi/ota-ona O'ZI yozmaydi, xodim
    (staff) ham ko'rmaydi: `TeacherReviewsController` da ATAYIN `[AdminPerm]` EMAS, balki
    `[Authorize(Roles = admin,superadmin,platformowner)]` (AdminPerm xodimga GET'ni ochib qo'yardi).
  • **HAR GURUH UCHUN ALOHIDA:** kalit (o'quvchi, o'qituvchi, guruh). O'quvchi 2+ guruhda o'qisa —
    2+ blok chiqadi. Chiqarilgan/tugatgan a'zolik ham ko'rinadi (tarix qimmatli), faollar tepada.
    Server klientdan kelgan `teacherId`ga ISHONMAYDI — guruhning amaldagi o'qituvchisi olinadi.
  • **KO'RISH — ADMIN uchun IKKI joyda:** (1) o'quvchi profilida (guruh bo'yicha bloklar);
    (2) **o'qituvchi profilida «Fikrlar» tabi** (`GET /api/admin/teachers/{id}/reviews` →
    `TeacherReviewService.ForTeacherAsync`) — shu o'qituvchi haqidagi BARCHA fikrlar bir joyda
    yig'iladi, eng yangisi tepada, o'quvchi (profiliga havola bilan) va guruh nomi ko'rinadi,
    guruh bo'yicha filtr va o'chirish bor. Yozish u yerda YO'Q — fikr faqat o'quvchi profilida
    yoziladi (u yerda guruh/o'qituvchi konteksti aniq).
  • **MAXFIYLIK — CHEGARA QAYERDA:** xom matn **O'QITUVCHINING O'ZIGA** hech qachon berilmaydi —
    o'qituvchi portalida (`api/teacher/*`) va Flutter ilovasida bunday endpoint ATAYIN yo'q;
    auditga ham matn yozilmaydi (faqat fakt). ADMIN esa yuqoridagi ikki joyda ko'radi.
    AI xulosasi o'qituvchiga ko'rsatilishi mumkin, shuning uchun
    `TeacherReviewService.TextsForTeacherAsync` matnga faqat
    **sana + guruh nomi** qo'shadi, O'QUVCHI ISMINI QO'SHMAYDI; promptda ham "so'zma-so'z ko'chirma,
    ism yozma" ko'rsatmasi bor — chunki xulosa o'qituvchining o'ziga ham ko'rsatiladi.
  • **AI'ga ULANISHI:** `TeacherSnapshotBuilder` 7b-bo'lim → snapshotdagi `oquvchilarFikri`
    (`{soni, matnlar}`, oxirgi 12 oy, eng yangi 25 ta, har biri 400 belgigacha);
    `TeacherAiMetricsDto.StudentReviewCount` (faqat SON — UI'ga chiqadi, matn emas);
    `TeacherAiNarrativeDto.OquvchilarFikri` — AI ajratgan TAKRORLANUVCHI naqshlar.
    Prompt AI'ga bu fikrlarni `kuchli`/`zaif`/`tavsiyalar` ro'yxatlarida ham hisobga olishni buyuradi.
  • **KO'RINISHI:** o'qituvchi profili → «AI tahlil» tabida (1) yangi **«Tahlillar»** kartochkasi —
    tahlillar TARIXI sana + umumiy ball bilan, **eng yangisi tepada**, qatorni bosib o'shanisi
    ochiladi (ilgari oddiy `<select>` edi, tarix ko'rinmasdi); (2) «O'quvchilar fikri asosida»
    binafsha bloki + nechta fikr asosida ekani. PDF eksportga ham qo'shilgan.
  • Flutter O'QITUVCHI ilovasida AI tahlil ekrani UMUMAN yo'q — u yerga hech narsa qo'shilmaydi
    (va qo'shilmasligi ham kerak: xom fikrlar o'qituvchiga ko'rinmaydi).

- **GURUH TAHLILI — ma'lumot manbalari** (`GroupSnapshotBuilder`, oxirgi 12 oy; hammasi MAVJUD
  yagona manbalardan, yangi hisoblash mantig'i yaratilmaydi):
  - **a'zolik oqimi** — `StudentGroup` sanalari + `MembershipLifecycle.Tally`;
  - **ketish/muzlatish sabablari** — `AuditLog` (`EntityType="Membership"`, `EntityId` shu guruh bilan
    boshlanadi, summary ichida `— sabab: X`) + `ArchivedRecord.Reason`;
  - **davomat** — jurnal "Davomat" tabi bilan AYNAN bir xil qoida: o'tilgan darslar a'zolik oynasi
    ichida (`JournalService.MemberStart` .. muzlatilgan/chiqqan sana), qoldirgan = sababli belgi
    (`AbsenceReason.IsLate` MUSTASNO);
  - **jurnal intizomi** — `SalaryJournalStats` (reja/o'tilgan/`MissedDates`) + `LessonNote`
    (mavzu/uy vazifa/`AttendanceTaken`);
  - **o'zlashtirish** — `JournalEntry` baholari/uy vazifa/xulq, `StudentBallService.ComputeAsync`,
    `CurriculumForecast.BuildGroupAsync` (dastur qamrovi va tugash prognozi);
  - **imtihonlar** — `TestResult`/`TestScore` (onlayn/oflayn, o'rtacha foiz, topshirmaganlar);
  - **to'lovlar** — `CourseFinanceReport.BuildGroupPaymentsAsync` (hisoblangan/yig'ilgan/qarz/
    to'lamaganlar) + oyma-oy `MonthlyCharge`/`FinanceTransaction`.
  MOLIYA RUXSATI: `ClassesController.CanSeeFinance()` (admin/superadmin yoki "finance" ruxsatli
  xodim) — false bo'lsa to'lov raqamlari UMUMAN yig'ilmaydi (`FinanceIncluded=false`), AI promptida
  ham bo'lmaydi va panelda to'lov bloki ko'rinmaydi. Guruh sahifasidagi "To'lovlar" tabi bilan bir xil.
  PROMPT: guruh tahlili ATAYIN **TANQIDIY** — kamchilikni yumshatmasdan, har da'voni raqam bilan
  asoslab, muammoli o'quvchilarni ism bilan ko'rsatib yozadi.
