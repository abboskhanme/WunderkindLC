---
description: O'qituvchi portali (/teacher/*) — admin tizimi bilan bir xil dizayn, moslashuvchan qobiq, bosh sahifa / jadval / davomat / o'quvchilarim.
paths:
  - "WunderkindLC.Client/src/pages/teacher/**"
  - "WunderkindLC.Client/src/components/teacher/**"
  - "WunderkindLC.Client/src/components/layout/TeacherMobileLayout.tsx"
  - "WunderkindLC.Client/src/lib/teacherPortal.ts"
  - "WunderkindLC.Server/Controllers/TeacherPortalController*.cs"
  - "WunderkindLC.Application/Services/TeacherHomeService.cs"
  - "WunderkindLC.Application/Services/TelegramBotService.cs"
---

# O'qituvchi portali qoidalari

Migratsiya KERAK EMAS — hammasi mavjud jadvallardan (guruh, a'zolik, jurnal, ko'chirish, o'rinbosar).

## 1. DIZAYN — admin tizimi bilan BIR XIL

Nunito, brand `#3d68ff`, kontent foni `#f0f2f2`, oq karta + `#dbe0e6` chegara + 12px radius.
Eski sahifalar "teal" tokenlarida yozilgan (`text-ink`, `bg-tealsoft`, `bg-teal-600` ...) — ular
`index.css` dagi `.teacher-app` blokida admin qiymatlariga QAYTA ANIQLANGAN (teal ramp → brand ramp).

⚠️ Yangi sahifada teal ishlatmang va hex yozmang — `brand-*` va tokenlar (`text-ink`, `text-mute`,
`border-line`, `bg-paper2`, `bg-panel3`). Tungi rejim shu tokenlar orqali o'zi ishlaydi; qattiq
yozilgan hex (`#dbe0e6`, `#e6eaea`) tungi rejimda "yonib" qoladi.

Umumiy bloklar — `components/teacher/TeacherUi.tsx` (`TPage`, `TCard`, `StatTile`, `LessonRow`,
`EmptyState`, `Segmented`, `Avatar`). Sof funksiyalar — `lib/teacherPortal.ts` (komponent bilan
aralashtirilmaydi: `react-refresh/only-export-components`).

## 2. QOBIQ — telefon birinchi

O'qituvchilar asosan TELEFONDAN ishlatadi (Flutter WebView ham shu yerni ochadi).

| Ekran | Navigatsiya |
|---|---|
| < 1024px | yuqori panel + pastki 5 tab: Bosh · Guruhlar · Davomat · Jadval · **Menyu** |
| ≥ 1024px | yuqori panel + chap yon menyu (hamma bo'lim) |

⚠️ Bo'limlar ro'yxati YAGONA — `TEACHER_NAV`. Yon menyu, pastki tablar va "Menyu" sahifasi
shundan quriladi; bo'limni bir joyga qo'shing, uchalasida paydo bo'ladi. Ruxsati yo'q band
(`perm`) va Support bo'lmagan o'qituvchidagi "Support" ko'rinmaydi.

## 3. API — `TeacherPortalController.Home.cs` → `TeacherHomeService` (faqat o'qish)

| Endpoint | Ruxsat | Nima |
|---|---|---|
| `GET api/teacher/overview` | — | guruh kartalari + bugungi darslar + o'quvchilar soni |
| `GET api/teacher/schedule?from&to` | `schedule` (aks holda 403) | oraliqdagi darslar, ≤ 62 kun |
| `GET api/teacher/students?month` | `journal` (aks holda 403) | barcha a'zoliklar + oylik davomat |
| `GET api/teacher/debt-summary` | — | o'z guruhlaridagi qarzdorlar SONI + JAMI qarz (yig'indi) |
| `GET api/teacher/groups/{classId}/combined-rating?period&key` | `journal` + guruh EGASI (o'rinbosar — 404) | guruhning UMUMIY reytingi — admin jadvali bilan bir xil (`RatingEngine.GroupTable`, SPEC-backlog F4 A4) |

⚠️ **Guruh kartasi (F4 T1, 2026-10-17):** `overview` dagi har O'Z guruh kartasida joriy oy davomati % (`AttendancePct` —
`MembershipAttendance`, ko'rinadigan a'zolar bo'yicha; dars o'tilmagan bo'lsa null) va qarzdorlar SONI (`DebtorCount` —
`DebtByGroupAsync`, jami `DebtorCount` bilan BITTA hisob). O'rinbosarlik kartasida ikkalasi null. Summa berilmaydi.

⚠️ `overview` "Dars jadvali" ruxsatisiz ham ochiladi, lekin `todayLessons` BO'SH (`canSchedule=false`).

Qoidalar QAYTA YOZILMAGAN, chaqiriladi:
- guruh ko'rinishi — `TeacherGroupAccess` (arxiv/blok yo'q, kursi yo'q guruh yo'q);
- dars sanalari — `JournalService.EffectiveLessonDatesInMonth` (ko'chirish bilan) + guruh umri.
  ⚠️ Kunlari yo'q guruh jadvalga TUSHMAYDI (maosh hisobidagi "har kuni" qoidasi bu yerda ishlamaydi);
- o'rinbosar — `SubstituteTeacherService.EffectiveDates`/`CoversDate`. O'z guruhida boshqa odam
  o'rinbosar bo'lgan kun dars YO'QOLMAYDI, `substitutedBy` bilan belgilanadi;
- to'lov darvozasi — `JournalPolicy.PaymentGate`: jurnalda ko'rinmagan o'quvchi ro'yxatda ham,
  sanoqlarda ham yo'q (`journal.md`);
- davomat foizi — `StudentJournalBuilder.BuildCells` (o'quvchi portali bilan bir xil); kech kelgan KELGAN.

⚠️ "O'quvchilarim" da balans SUMMASI va telefon YO'Q — faqat qarzdor oylar soni (belgi uchun).
«Telegram» ustuni (2026-10-12, CRM U6b) — ulangan (bloklanmagan) chatlar SONI va qarindoshlik belgilari
(`telegramLinked`, `telegramRelations` — `TelegramLinkSummary`); telefon, ism, chat id BERILMAYDI. Guruh
«O'quvchilar» tabida (`groups/{classId}/students`) ham xuddi shunday.
⚠️ ARXIVDAGI o'quvchi ro'yxatda ham, sanoq/qarzdorlarda ham YO'Q (`VisibleMembersAsync`,
`DebtSummaryAsync` — `!Student.IsArchived`): eski arxivlashlar a'zolikni yopmagan, `IsActive`
qator qolgan bo'lishi mumkin (arxiv = o'quvchi ketgan).
⚠️ Eski `Teacher.HomeroomClass` (matnli "guruh rahbarligi") guruhga KIRISH bermaydi — u UI'da
tahrirlanmaydi va o'qituvchi almashgandan keyin ham qolardi. Kirish — `Group.TeacherId`
(`TaughtClassIdsAsync`) va o'rinbosar sanalari. Guruh chati (`ChatService`, 2026-10 da olib tashlangan) ham shu qoidaga
o'tkazildi — kanallar faqat `TeacherGroupAccess` dan.
⚠️ Guruh JURNALI (`journal/group`) da ham summa YO'Q (2026-10-07): `Balance` = 0, rang `Debtor` bayrog'i va
`DebtMonths` dan (o'rinbosarga ham). Ilgari har o'quvchining qarz summasi jurnal javobida kelardi.
⚠️ Qarzdorlar xulosasi (`TeacherHomeService.DebtSummaryAsync`, `overview` ichida ham) — faqat
YIG'INDI: nechta o'quvchi va jami qancha. Har o'quvchining summasi baribir berilmaydi. Manba —
`GroupBalanceService` (guruh balansi bilan bir xil), faqat faol, sinovda bo'lmagan a'zolar; ikki
guruhdagi o'quvchi BIR marta sanaladi. To'lov darvozasi bu yerda QO'LLANMAYDI — maqsad aynan
jurnalda yashiringan qarzdorlarni ham ko'rsatish (uchrashuv talabi: "o'qituvchi qarzdorlikni
yopishga qiziqsin"). ⚠️ 2026-10-26 dan MAOSH sahifasi bu endpointni CHAQIRMAYDI: u yerdagi qarzdorlar soni
«to'lasa yana +» bilan BITTA manbadan (`MonthSalaryDto.UnpaidStudents`, `billing.md`), summasiz.
(Telefon faqat §3.5 dagi «O'quvchilar» tabida — o'z guruhi o'quvchilari, FAQAT KO'RISH uchun.)

## 3.5. O'QUVCHINI TAHRIRLASH (guruh sahifasi → «O'quvchilar» tabi, 2026-10-05)

Foydalanuvchi talabi: o'qituvchi o'z guruhi o'quvchisining **ism-familiyasi va maktab sinfini** tuzata olsin
(asosan Telegram Mini App ichida). Qo'shish/chiqarish — TAQIQLANGAN.

| Endpoint | Nima |
|---|---|
| `GET api/teacher/groups/{classId}/students` | guruhning tirik a'zolari (ism, telefon — faqat ko'rish, joriy sinf, holat) |
| `PUT api/teacher/groups/{classId}/students/{studentId}` | `{ fullName, schoolGrade }` — FAQAT shu ikkitasi |

- Qoida — `TeacherStudentEdit` (Application, sof, `TeacherStudentEditTests`). Sinf: `0` = tozalash,
  joriy o'quv yiliga bog'lanadi (admin bilan bir xil, `school-grade.md`).
- ⚠️ **TELEFON RAQAMLARI — O'QITUVCHIGA YOPIQ (2026-10-12, review H1).** O'quvchi, ota-ona, ota va ona raqami —
  to'rttalasi ham Telegram botida ro'yxatdan o'tish uchun MOSLASH maydoni (`TelegramRegistrar`), ya'ni KIRISH OMILI:
  raqamni o'z telefoniga almashtirgan o'qituvchi botda o'quvchi akkaunti uchun bir martalik kirish kodini olardi.
  Kiruvchi DTO'da telefon maydoni UMUMAN yo'q (yuborilsa JSON bog'lovchisi tashlab yuboradi), `Apply` raqamlarga
  tegmaydi, Telegram ulanishlari ham o'zgarmaydi. Raqamni faqat xodim/admin o'zgartiradi (`StudentsController.Update`,
  auditlanadi). Klientda maydon o'chirilgan (faqat ko'rish) + izoh «Telefon raqamini faqat administrator o'zgartiradi
  — u Telegram botga kirish uchun ishlatiladi».
- Darvoza: `journal` ruxsati + guruh EGASI (`TeacherGroupAccess.OwnedBy`) + o'quvchi shu guruhning
  `IsActive` a'zosi va arxivda emas. ⚠️ **O'rinbosar tahrirlay OLMAYDI** (u vaqtincha keladi).
- Login akkauntidagi ism ham yangilanadi; audit — `Student/update`, ActorName = o'qituvchi.
- ⚠️ Ism QISMLARI (`LastName/FirstName/MiddleName`) ham yangi FullName'dan qayta bo'linadi
  (`TeacherStudentEdit.SetName`: 1-so'z familiya, 2-so'z ism, qolgani sharif — klientdagi
  `splitFullName` bilan bir xil). Sabab: admin formasi saqlangan qismlarni qayta yuboradi va
  `StudentsController.Update` FullName'ni ulardan YIG'ADI — ilgari o'qituvchining tuzatishi
  adminning keyingi (istalgan) tahririda jimgina bekor bo'lardi.
- ⚠️ **O'qituvchi portalida a'zo qo'shish/chiqarish/muzlatish YO'Q va bo'lmaydi** — test
  (`Oqituvchi_portalida_azo_QOSHISH_CHIQARISH_yoli_YOQ`) bunday endpoint paydo bo'lsa qizaradi.
- Klient: `pages/teacher/groups/TeacherGroupStudentsTab.tsx` — qator butunlay bosiladi, maydonlar
  mobil'da 16px (iOS Telegram'da fokusda zoom bo'lmasin), xato oyna ichida, oyna yopilmaydi.

## 4. TEZKOR DAVOMAT (`/teacher/attendance?group=&date=`)

Yozish mavjud `POST journal/bulk-attendance` orqali — server siyosat/o'rinbosar sanasi/to'lov
darvozasini O'ZI tekshiradi, baho/uy vazifa TEGILMAYDI. Faol bo'lmagan / begona / arxivlangan id'lar jimgina
chetlab o'tiladi; "Kech" — "kelmadi" xabari EMAS; xabarlar FONDA va faqat yangi kelmaganlarga (journal.md §C8).

⚠️ FAQAT O'ZGARGAN qatorlar yuboriladi: har "kelmadi" yozuvi ota-onaga avto-xabar jo'natadi —
hammasini qayta yuborsak, har saqlashda xabar takrorlanardi.

⚠️ O'quvchi default BELGILANMAGAN turadi (foydalanuvchi talabi, 2026-09-27) va HAMMASI
belgilanmaguncha «Saqlash» YOPIQ. Sabab: jurnal qoidasi bo'yicha davomat olingan darsda yozuvi
yo'q o'quvchi «keldi» hisoblanadi (`StudentJournalBuilder` → `PresentDefaultFrom`) — qisman saqlash
qolganlarni JIMGINA «keldi» qilib qo'yardi (e2e'da aynan shu ko'rindi). Tezlik uchun
«Qolganlari keldi» tugmasi — faqat belgilanmaganlarga tegadi.

⚠️ «Saqlash» → TASDIQLASH oynasi (guruh, sana, keldi/kech/kelmadi soni, kelmaganlar sababi bilan).
Xato bo'lsa oyna YOPILMAYDI, sabab ichida ko'rinadi. Saqlangandan keyin dars manzilga qotiriladi
(`?group=`) — aks holda "birinchi belgilanmagan dars" qoidasi sahifani keyingi guruhga o'tkazardi.

Jurnalda shu kunga ustun bo'lmasa (dars ko'chirilgan) saqlash YOPIQ — aks holda yozuv ko'rinmas
sanaga tushardi. Muzlatilganlar ro'yxatda yo'q; a'zoligi keyin boshlanganlar xira, bosilmaydi.

⚠️ **«DAVOMAT OLINDI» — YAGONA TA'RIF (2026-10-12, review BLOCKER).** Tezkor davomat «Keldi» ni OLDINDAN FAQAT davomati
TO'LIQ olingan darsda qo'yadi (`GroupJournal.attendanceTakenDates`, server `AttendanceRules`); dars tanlash
(«davomati kutilayotgan birinchi dars»), dars holati (`lessonPhase` → «Davomat olingan») va bosh sahifadagi
«Davomat kutilmoqda» — `TeacherLessonDto.AttendanceTaken` (`LessonNote.AttendanceTakenAt`, eslatma/jarima/monitor
bilan bir xil). `Conducted` («o'tildi») EMAS: bitta baho darsni "o'tildi" qiladi — ilgari shunday darsda hamma
o'quvchi «Keldi» bo'lib to'lar va «Saqlash» ularni haqiqatan «keldi» qilib yozardi (`journal.md`).

## 5. TELEGRAM BOT — «📱 Panel» (Mini App)

⚠️ **BITTA tugma — «📱 Panel» (2026-10-07, foydalanuvchi talabi):** o'qituvchi ham, o'quvchi/ota-ona
ham bitta tugmani bosadi. Manzil — sayt **ILDIZI** (`TelegramBotService.PanelUrl`): `RootRedirect`
kirgan foydalanuvchini rol bo'yicha o'zi yo'naltiradi (o'qituvchi → `/teacher`, o'quvchi/ota-ona →
`/student`), kirmagan bo'lsa login → keyin o'z portaliga. Rol bo'yicha ALOHIDA tugma/manzil QO'SHMANG
(ilgari «🎓 O'qituvchi paneli» va «🎒 O'quvchi kabineti» ikki tugma edi).

Portal asosiy botda `web_app` tugmasi bilan Telegram ICHIDA ochiladi (`TelegramBotService`):
- ⚠️ 2026-10-12 dan «📱 Panel» HAR chatga (ulanmagan chatga ham — `StartKeyboard`), telefon so'ralmaydi; chat Panel
  ichida login qilinganda bog'lanadi (`messaging.md` §0.5 «ULANISH — PANEL ORQALI»). Quyidagi ikki band — tarix.
- reply-klaviaturada ENG TEPADA — chat FAOL o'qituvchiga (`IsTeacherChatAsync`) YOKI arxivlanmagan
  o'quvchiga (`IsStudentChatAsync`) bog'langan bo'lsa; salomdagi izoh satri rolga qarab
  (`PanelGreetingLine`), tugma esa bitta;
- o'qituvchi va o'quvchi login xabari ostida inline tugma (`PanelInline`).
- Eski klaviaturadagi eski tugmalar ham ishlayveradi (`web_app` — bot matn olmaydi); yangi tugma
  /start bosilganda chiqadi.

⚠️ Kirish ODDIY (login/parol yoki botdagi «🔑 Yangi kod olish») — Telegram orqali AVTOMATIK kirish
ATAYIN qilinmagan: u yangi kirish yo'li bo'lardi (foydalanuvchi tanlovi, 2026-09-27). Kerak bo'lsa
`TelegramInitData` (karyera botidagi) naqshida alohida ish sifatida ko'rilsin.

SPA Mini App uchun tayyor: SDK `index.html` da, `ready()/expand()` — `main.tsx`, CSP
`frame-ancestors` Telegram Web'ga ochiq, cookie'lar https'da `SameSite=None`.
⚠️ `web_app` faqat HTTPS manzilni qabul qiladi — `PanelUrl` ni http qilmang
(`TeacherBotButtonTests` qulflaydi).

**O'quvchi kabineti (2026-10-07):** «Dars jadvali» va «Test natijalari» ekranlari qo'shildi
(`GET /student/groups`, `GET /student/test-results` — mavjud endpointlar). Kirish o'sha-o'sha ODDIY.

## 5.1. O'QITUVCHI BOTI — alohida bot (2026-09-30)

Foydalanuvchi talabi: o'qituvchilar panelni **alohida bot** orqali ochsin (`@Wundora_LC_bot`,
`.env: TEACHER_BOT_TOKEN` → compose `TeacherBot__BotToken`). Karyera boti naqshi:
`TeacherTelegramService` (mijoz) + `TeacherBotService` (long polling).

- **Pastda BITTA tugma** (reply-klaviatura, `web_app`, `is_persistent`) — «📱 Panel»,
  `TelegramBotService.PanelUrl` ga (sayt ildizi, rol bo'yicha yo'naltirish) olib boradi (manzil va
  matn asosiy bot bilan UMUMIY).
- ⚠️ **Chapdagi ko'k menyu tugmasi YO'Q** (foydalanuvchi talabi) — startupda `setChatMenuButton`
  `type=default` + `deleteMyCommands`. BotFather'da qo'lda qo'yilgan tugma ham keyingi restartda
  tozalanadi. Menyu tugmasini `web_app` qilib QO'YMANG.
- Bot o'qituvchini TANIMAYDI va telefon so'ramaydi: tugma har kimga ko'rinadi, kirish esa panel
  ichida login/parol bilan (§5 dagi qaror — yangi kirish yo'li ochilmaydi).
- Asosiy botdagi o'qituvchi tugmasi va o'qituvchiga boradigan xabarlar (davomat eslatmasi,
  e'lonlar, shartnomalar) **o'zgarmadi** — ular hamon asosiy botdan ketadi.
- ⚠️ **PROD'DA O'CHIQ (2026-10-12, egasining qarori «bitta botda ishlaydi hamma»):** @Wundora_LC_bot tokeni
  `TELEGRAM_BOT_TOKEN` ga (ASOSIY bot) ko'chirildi, `TEACHER_BOT_TOKEN` bo'sh. Asosiy bot tokeni prod'da umuman
  kiritilmagan edi — xabarlar ketmasdi, ota-onalar ulana olmasdi. ⚠️ BITTA tokenni ikki xizmatga BERMANG: ikkala
  long polling bir-birini `getUpdates` 409 bilan uzib, yangilanishlarni bo'lib olardi.
- ⚠️ Dev'da `DEV_TEACHER_BOT_TOKEN` (bo'sh = bot o'chiq): prod tokeni bilan lokal `getUpdates`
  jonli botning xabarlarini o'g'irlardi. Testlar: `TeacherBotServiceTests`.

## 6. Testlar

Backend — `TeacherHomeServiceTests` (reja, ko'rinish doirasi, to'lov darvozasi, RBAC manba matnidan).
Klient — `lib/__tests__/teacherPortal.test.ts` (sana, dars holati, menyu katalogi).
Bot — `TeacherBotButtonTests` (tugma faqat o'qituvchida, eng tepada, https).
