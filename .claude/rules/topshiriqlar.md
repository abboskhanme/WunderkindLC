---
description: "Topshiriqlar" bo'limi (Kanban) — doska/ustun/topshiriq modeli, holat va takroriylik qoidalari, Telegram oqimi, ruxsatlar va "Adminga topshiriq" (StaffTask) bilan farqi.
paths:
  - "IntellectCRM.Domain/Entities.cs"
  - "IntellectCRM.Server/Controllers/WorkTasksController.cs"
  - "IntellectCRM.Server/Controllers/WorkTasksController.Helpers.cs"
  - "IntellectCRM.Server/Controllers/WorkTasksController.Dashboard.cs"
  - "IntellectCRM.Application/Services/WorkTaskFlow.cs"
  - "IntellectCRM.Application/Services/WorkTaskTelegram.cs"
  - "IntellectCRM.Application/Services/WorkTaskReminderService.cs"
  - "IntellectCRM.Client/src/pages/admin/tasks/*"
  - "IntellectCRM.Client/src/api/services/workTasks.ts"
---

# Topshiriqlar (Kanban) qoidalari

`/admin/topshiriqlar` — adminlarga/xodimlarga beriladigan **loyihaviy topshiriqlar** va ular
bo'yicha nazorat. Menyuda "Boshqaruv" dan TEPADA (kundalik ish oqimi).

## 1. IKKI MODUL — chalkashtirmang

| | "Adminga topshiriq" (eski) | "Topshiriqlar" (bu modul) |
|---|---|---|
| Entity | `StaffTask` · `StaffTaskLog` | `WorkTaskBoard` · `WorkTaskColumn` · `WorkTask` · `WorkTaskItem` · `WorkTaskComment` · `WorkTaskEvent` |
| Mohiyati | HAR KUNI takrorlanadigan checklist | Muddat/mas'ul/muhimlik bilan bir martalik (yoki takroriy) topshiriq |
| Controller | `StaffTasksController` (`[AdminPerm("staff")]`) | `WorkTasksController` (`[AdminPerm("tasks")]`) |
| Sahifa | `/admin/topshiriqlar/kunlik` — **faqat superadmin** (rol bilan) | Doska · Ro'yxat · Kalendar · Nazorat paneli |
| Telegram | ertalab checklist, `stask:` callback | topshiriq berilganda + kunlik eslatma, `wtdone:` callback |

⚠️ Kunlik checklist Boshqaruvdan shu bo'limga **KO'CHDI** (`/admin/boshqaruv/staff-tasks` →
redirect). **Ruxsat lineyasi ATAYIN o'zgarmadi**: u avvalgidek superadmin roli bilan
darvozalangan va serverda `staff` kalitida qolgan — aks holda mavjud xodimlarning ruxsati
sezdirmasdan kengayib ketardi.

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

- Matn/tugmalar — `WorkTaskTelegram` da (YAGONA joy, `StaffTaskChecklist` bilan bir xil naqsh).
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
