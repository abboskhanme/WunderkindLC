---
description: Bosh sahifa VIDJETLARI (CRM Phase 5) — kartochka va bloklarni yoqish/o'chirish/tartiblash, sozlama serverda (foydalanuvchiga), standart va yangi blok qo'shish tartibi.
paths:
  - "WunderkindLC.Application/Services/HomeLayoutRules.cs"
  - "WunderkindLC.Server/Controllers/HomeLayoutController.cs"
  - "WunderkindLC.Client/src/lib/homeWidgets.ts"
  - "WunderkindLC.Client/src/components/dashboard/home/*"
  - "WunderkindLC.Client/src/pages/admin/AdminDashboard.tsx"
---

# Bosh sahifa vidjetlari qoidalari

Bosh sahifa (`/admin`, `AdminDashboard`) — 12 ta kartochka (`HOME_CARDS`) va BLOKLAR. «Vidjetlar» tugmasi
(`HomeWidgetsDrawer`) har birini yoqadi/o'chiradi va ▲/▼ bilan tartiblaydi; «Statistika» — kartochkalarni
birdan yashiradi. Migratsiya `CrmWorkspace` — `UserSettings.HomeLayout` (text, standart `""`).

## 1. Sozlama SERVERDA va FOYDALANUVCHINIKI

`GET / PUT / DELETE /api/admin/home/layout` (`HomeLayoutController`, `[Authorize(Roles = "superadmin,admin,staff")]`).
Marshrutda foydalanuvchi id'si YO'Q — faqat o'z qatori (`NameIdentifier` claim). Boshqa brauzerda ham o'sha
ko'rinish. Auditga YOZILMAYDI (shaxsiy ko'rinish tanlovi, markaz ma'lumoti emas — til/tema kabi).

```
{ version: 1, cardsVisible: bool, cards: [{ key, visible }], blocks: [{ key, visible }] }
```

`HomeLayoutRules.Normalize` (sof, `HomeLayoutRulesTests`): versiya 1 bo'lmasa — 400; yuborilgan JSON
4 000 belgidan oshsa — 400; noma'lum va takror kalitlar TASHLANADI, yetishmagan ma'lum kalitlar standart
ko'rinishi bilan OXIRIGA qo'shiladi. `""`/buzuq qiymat — "sozlanmagan" (`layout: null`).

⚠️ Eski brauzer sozlamasi `localStorage['home.showStats']` faqat server sozlamasi YO'Q bo'lganda bir marta
boshlang'ich qiymat bo'ladi (`initialHomeLayout`), keyin «Statistika» serverga saqlanadi (optimistik — xatoda
qaytariladi va sabab ko'rsatiladi).

## 2. STANDART = vidjetlardan oldingi sahifa (edutizim parity)

Kartochkalar hammasi (`HOME_CARDS` tartibida), `cardsVisible = true`; bloklardan faqat `cashboxes` va `schedule`.

| Blok | Ruxsat | Komponent |
|---|---|---|
| `cashboxes` | FAQAT superadmin (rol, kalit emas) | `CashboxBalanceStrip` |
| `schedule` | `schedule.timetable` | sanali jadval (`DatedScheduleBoard`, `schedule.md` §8) |
| `lessonsMonitor` | `classes.notAttended` | `TodayLessonsMonitor compact` |
| `myTasks` | `tasks.board` | `MyTasksBlock` — ochiq (`assigneeId=me`) + tasdig'imni kutayotgan (`createdById=me&status=awaiting`), top 5 |
| `absentNotices` | `students.notices` | ⏳ U3b quradi — hozircha `available: false` |
| `redList` | `students.attendance` | ⏳ U3d quradi — hozircha `available: false` |
| `contactsToday` | `contacts` | `ContactsTodayBlock` — `contacts/meta` `due.todo` |
| `aiAnalysis` | `ai` | `CenterAiAnalysisCard` |

⚠️ Vidjet sozlamasi MA'LUMOT OCHMAYDI: har blok o'z endpointidan o'qiydi va server darvozasi o'z joyida.
Ruxsatsiz karta/blok oynada ko'rinmaydi, lekin sozlamada o'z joyida qoladi (ruxsat berilsa o'sha tartibda chiqadi).

## 3. Yangi blok qo'shish

1. Server: `HomeLayoutRules.BlockKeys` ga `(kalit, standartKo'rinish)`.
2. Klient: `lib/homeWidgets.ts` → `HOME_BLOCKS` ga band (`label`, `description`, `perm`, `available: true`).
   ⚠️ Bu faylda KOMPONENT YO'Q (react-refresh qoidasi).
3. `AdminDashboard.renderBlock` ga komponent.

`homeWidgets.test.ts` server va klient katalogining kalitlari (va standart ko'rinishi) AYNAN bir xilligini
`HomeLayoutRules.cs` manba matnidan o'qib qulflaydi.
