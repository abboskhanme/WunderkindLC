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

## 4. TEZKOR DAVOMAT (`/teacher/attendance?group=&date=`)

Yozish mavjud `POST journal/bulk-attendance` orqali — server siyosat/o'rinbosar sanasi/to'lov
darvozasini O'ZI tekshiradi, baho/uy vazifa TEGILMAYDI.

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

## 5. TELEGRAM BOT — «🎓 O'qituvchi paneli» (Mini App)

Portal asosiy botda `web_app` tugmasi bilan Telegram ICHIDA ochiladi (`TelegramBotService`):
- reply-klaviaturada ENG TEPADA — faqat chat FAOL o'qituvchiga bog'langan bo'lsa
  (`IsTeacherChatAsync`; o'quvchi/ota-onada tugma yo'q);
- o'qituvchining login xabari ostida inline tugma (`TeacherPanelInline`).

⚠️ Kirish ODDIY (login/parol yoki botdagi «🔑 Yangi kod olish») — Telegram orqali AVTOMATIK kirish
ATAYIN qilinmagan: u yangi kirish yo'li bo'lardi (foydalanuvchi tanlovi, 2026-09-27). Kerak bo'lsa
`TelegramInitData` (karyera botidagi) naqshida alohida ish sifatida ko'rilsin.

SPA Mini App uchun tayyor: SDK `index.html` da, `ready()/expand()` — `main.tsx`, CSP
`frame-ancestors` Telegram Web'ga ochiq, cookie'lar https'da `SameSite=None`.
⚠️ `web_app` faqat HTTPS manzilni qabul qiladi — `TeacherPanelUrl` ni http qilmang
(`TeacherBotButtonTests` qulflaydi).

## 6. Testlar

Backend — `TeacherHomeServiceTests` (reja, ko'rinish doirasi, to'lov darvozasi, RBAC manba matnidan).
Klient — `lib/__tests__/teacherPortal.test.ts` (sana, dars holati, menyu katalogi).
Bot — `TeacherBotButtonTests` (tugma faqat o'qituvchida, eng tepada, https).
