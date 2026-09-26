---
description: "Topshiriqlar" bo'limi (Kanban) — doska/ustun/topshiriq modeli, holat va takroriylik qoidalari, Telegram oqimi va ruxsatlar. Eski "Adminga topshiriq" (kunlik cheklist) moduli olib tashlangan.
paths:
  - "WunderkindLC.Domain/Entities.cs"
  - "WunderkindLC.Server/Controllers/WorkTasksController.cs"
  - "WunderkindLC.Server/Controllers/WorkTasksController.Helpers.cs"
  - "WunderkindLC.Server/Controllers/WorkTasksController.Dashboard.cs"
  - "WunderkindLC.Application/Services/WorkTaskFlow.cs"
  - "WunderkindLC.Application/Services/WorkTaskTelegram.cs"
  - "WunderkindLC.Application/Services/WorkTaskReminderService.cs"
  - "WunderkindLC.Client/src/pages/admin/tasks/*"
  - "WunderkindLC.Client/src/api/services/workTasks.ts"
---

# Topshiriqlar (Kanban) qoidalari

`/admin/topshiriqlar` — adminlarga/xodimlarga beriladigan **loyihaviy topshiriqlar** va ular
bo'yicha nazorat. Menyuda **Future → Boshqaruv → Topshiriqlar** (2026-09-26 dan: hozircha
ishlatilmaydi, asosiy menyudan olindi; marshrut o'zgarmagan).

## 1. BITTA MODUL — eski "Adminga topshiriq" OLIB TASHLANDI

Ilgari yonma-yon IKKI topshiriq tizimi bor edi. Eskisi — "Adminga topshiriq" (HAR KUNI
takrorlanadigan **kunlik cheklist**) — 2026-09-04 da **butunlay o'chirildi**: yangi modul
o'sha ehtiyojni takroriy topshiriq (`Repeat = daily`) bilan qoplaydi, ikkita o'xshash tizimni
parallel yuritishning ma'nosi qolmadi.

Nima o'chdi: `StaffTask` · `StaffTaskLog` entitylari, `StaffTasksController`,
`StaffTaskChecklist`, `StaffTaskDispatchService`, botdagi **`stask:`** callback'i,
`CenterMeta.StaffTaskEnabled/Hour/Minute`, klientdagi `StaffTasksPage` va `staffTasks.ts`.
Migratsiya — **`RemoveStaffTasks`** (jadvallarni ham tushiradi; `Down` faqat SXEMANI tiklaydi).

⚠️ **Eski manzillar redirect bo'lib qoldi** — `/admin/boshqaruv/staff-tasks` va
`/admin/topshiriqlar/kunlik` ikkalasi ham `/admin/topshiriqlar` ga tushiradi (xatcho'p va eski
havolalar 404 bermasin).

⚠️ **Yangi kod `staff` ruxsat kalitiga topshiriq mantig'ini OSMASIN** — endi bo'limning yagona
kaliti `tasks*` (§5). Eski modul `staff` kalitida turgani faqat tarixiy sabab edi.

## 2. Model: doska → ustun → topshiriq

- **`WorkTaskBoard`** — loyiha/bo'lim doskasi. O'chirilmaydi, **arxivlanadi** (`?hard=true` bilan
  butunlay o'chirish mumkin). Birinchi ochilishda "Umumiy" doskasi 4 ta ustun bilan avtomatik
  yaratiladi (`EnsureSeedAsync`, idempotent).
- **`WorkTaskColumn`** — Kanban bosqichi. Ikki bayroq muhim:
  - `IsDone` — bu ustundagi topshiriq **YOPILGAN** hisoblanadi. Statistika, muddat eslatmasi va
    botdagi "Bajardim" tugmasi AYNAN shunga qaraydi. Doskada kamida bitta `IsDone` ustun qolishi
    shart (server tekshiradi).
  - `IsSystem` — o'chirilmaydi (birinchi va yopiluvchi ustun: topshiriqning "uyi" va yakuni).
  - Ustun o'chirilganda topshiriqlari YO'QOLMAYDI — doskaning birinchi ustuniga ko'chadi.
- **`WorkTask`** — `Order` faqat **ustun ichidagi** tartib; sudrab ko'chirishda ustun 0..N qilib
  qayta raqamlanadi (`ApplyMoveAsync`). Muddat — `DueDate` ("yyyy-MM-dd") + ixtiyoriy `DueTime`.

## 3. Holat o'zgarishi — YAGONA yo'l

Topshiriq holati faqat **ustun** orqali o'zgaradi (alohida `status` ustuni YO'Q). `ApplyMoveAsync`:

```
yopiluvchi ustunga tushdi  →  CompletedAt = hozir, CompletedById = amal qilgan odam
                              + takroriy bo'lsa keyingi nusxa TUG'ILADI
yopiluvchi ustundan chiqdi →  CompletedAt = null  ("qayta ochildi")
```

⚠️ **Takroriylikning nusxa tug'dirish mantig'i `WorkTaskFlow.SpawnRepeatAsync` da — YAGONA joy.**
Panel ham, Telegram bot ham shundan foydalanadi; nusxa ko'chirilsa botda yopilgan takroriy
topshiriq keyingi nusxasini tug'dirmasdan qolib ketardi.

Har o'zgarish `WorkTaskEvent` ga yoziladi ("kim, qachon, nimani") — nazorat bo'limining dalili.

## 4. Telegram oqimi

| Hodisa | Nima yuboriladi |
|---|---|
| Topshiriq mas'ulga biriktirildi (yaratish/tahrir) | "🆕 Sizga yangi topshiriq" + "✅ Bajardim" tugmasi |
| Kunlik eslatma (`WorkTaskReminderService`) | bugungi + kechikkan topshiriqlar ro'yxati, har biriga tugma |
| "✅ Bajardim" bosildi | topshiriq birinchi `IsDone` ustuniga ko'chadi, tarixga yozuv, takroriysi tug'iladi |

- Matn/tugmalar — `WorkTaskTelegram` da (YAGONA joy — tugmalar ikki joyda ayrilib ketmasin).
- Callback: **`wtdone:{taskId}`**. Faqat topshiriq **MAS'ULI** o'z chatidan belgilay oladi.
- Eslatma idempotent: `WorkTask.ReminderSentDate` — bir kunda bir marta. Bog'lanmagan xodim uchun
  ham belgilab qo'yiladi (aks holda u botga ulangan kuni bir yillik "qarz" yog'ilardi).
- Vaqt sozlamasi: `CenterMeta.WorkTaskReminderEnabled/Hour/Minute` (default 09:30) — UI'da
  "Doska sozlamalari" oynasida.

## 5. Ruxsatlar

| Kalit | Nima ochadi | Server |
|---|---|---|
| `tasks` | bo'lim (o'qish — odatdagi qoida bo'yicha ochiq) | sinf darajasi `[AdminPerm("tasks")]` |
| `tasks.board` | Doska · Ro'yxat · Kalendar + topshiriq YOZISH | topshiriq metodlari |
| `tasks.dashboard` | Nazorat paneli (xodimlar kesimi) | — (marshrut darvozasi) |
| `tasks.settings` | doska/ustun va kunlik eslatma sozlamalari | doska/ustun/settings metodlari |

Naqsh `.claude/rules/permissions.md` §4.1 bilan bir xil: **sinf darajasi = o'qish, metod darajasi =
sahifa bo'yicha yozish**. Ya'ni faqat `tasks.dashboard` berilgan xodim statistikani ko'radi, lekin
topshiriq yarata olmaydi.

## 6. Klient tuzilishi

```
pages/admin/tasks/
  model.ts               sof mantiq: muhimlik, sana, filtrlar, useTasksMeta/useBoardParam
  shared.tsx             umumiy KO'RINISH: TasksShell, TaskFilterBar, DueChip, AssigneeChip
  TaskCard.tsx           kartochka (+ DragOverlay uchun "o'lik" ko'rinish)
  TaskColumn.tsx         doska ustuni (.kanban-col* CSS — lidlar taxtasi bilan bir xil)
  TaskModal.tsx          yaratish/tahrirlash + qadamlar + izohlar + tarix (BITTA oyna)
  BoardSettingsModal.tsx doskalar · ustunlar · kunlik eslatma
  Tasks{Board,List,Calendar,Dashboard}Page.tsx
```

⚠️ `model.ts` va `shared.tsx` ATAYIN ajratilgan: bitta fayl ham komponent, ham konstanta eksport
qilsa Vite'ning fast-refresh qoidasi buziladi (`react-refresh/only-export-components`).

⚠️ Effekt ichida **to'g'ridan-to'g'ri setState chaqirilmaydi** (`react-hooks/set-state-in-effect`):
so'rov effektda bajariladi va setState javob kelgach chaqiriladi; qayta yuklash esa `tick`
hisoblagichi orqali so'raladi (`useTasksMeta`, nazorat paneli). `TaskModal` esa oyna ochilganda
`key` bilan qaytadan yaratiladi — boshlang'ich qiymatlar `useState` initializer'ida.

Tanlangan doska manzilda saqlanadi (`?board=...`) — ko'rinishlar orasida o'tganda yo'qolmaydi.

## 7. Nima ATAYIN qilinmagan

- **Sudrashda tasdiqlash oynasi yo'q** (lidlar taxtasidan farqli): holat o'zgarishi qo'shimcha
  ma'lumot talab qilmaydi — kim va qachon ko'chirgani tarixga o'zi yoziladi.
- **Ustun ichida sudrab qayta tartiblash yo'q** — faqat ustundan ustunga. Ustun ichidagi tartib
  serverda saqlanadi (`Order`), lekin kundalik ishda saralash muddat/muhimlik bo'yicha qilinadi.
- **Topshiriq o'chirish o'rniga ARXIV** tavsiya etiladi (`PUT /{id}/archive`) — tarix saqlanadi.
