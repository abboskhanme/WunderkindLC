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
bo'yicha nazorat. Menyuda **Boshqaruv → Topshiriqlar** («Rollar» dan keyin). 2026-09-26 da Future'ga
olingan edi; 2026-10 da (CRM Phase 5, TASDIQLASH bosqichi — §8) asosiy menyuga QAYTDI. Marshrut o'zgarmagan,
eski manzillar redirect (`config/__tests__/navCrmPhase5.test.ts` qulflaydi).

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
  butunlay o'chirish mumkin). Birinchi ochilishda "Umumiy" doskasi standart ustunlar bilan avtomatik
  yaratiladi (`EnsureSeedAsync`, idempotent): «Rejada» · «Jarayonda» · «Bajarildi» (2026-10 dan «Tekshiruvda»
  EKILMAYDI — tekshiruv endi tasdiqlash bosqichi, §8; mavjud doskalar tegilmaydi).
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

⚠️ **Nusxa tug'ilishi IDEMPOTENT** (2026-10): zanjirda (ildiz yoki `RepeatOfId` bir xil nusxalar)
shu KEYINGI muddatli topshiriq allaqachon bo'lsa — yangisi tug'ilmaydi. Ilgari "yopish → qayta ochish →
yopish", "✅ Bajardim" ni ikki marta bosish va ikkinchi yopiluvchi ustun har safar YANA bitta nusxa
tug'dirardi. Poyga uchun panelning `Move`/`Update` i va botning "Bajardim" i topshiriqni BIR qulf
(`PaymentIdempotency.LockAsync(WorkTaskFlow.LockKey(id))`) ostida o'qiydi va saqlaydi.

⚠️ **Ustunning `IsDone` bayrog'i almashsa yoki ustun o'chirilsa** ichidagi topshiriqlarning
`CompletedAt` i `WorkTaskFlow.SyncCompletionAsync` bilan moslanadi (yopiluvchi bo'ldi → yopiladi +
takroriysi tug'iladi; yopiluvchi emas → qayta ochiladi). Ilgari `CompletedAt` o'zgarmay qolib,
statistika va bot ustun bilan ziddiyatga tushardi. Yopiluvchi ustunni o'chirishda ham "kamida bitta
yopiluvchi ustun qolsin" qoidasi tekshiriladi.

Har o'zgarish `WorkTaskEvent` ga yoziladi ("kim, qachon, nimani") — nazorat bo'limining dalili.

## 4. Telegram oqimi

| Hodisa | Nima yuboriladi |
|---|---|
| Topshiriq mas'ulga biriktirildi (yaratish/tahrir) | "🆕 Sizga yangi topshiriq" + "✅ Bajardim" tugmasi |
| Kunlik eslatma (`WorkTaskReminderService`) | bugungi + kechikkan topshiriqlar ro'yxati, har biriga tugma |
| "✅ Bajardim" bosildi | topshiriq birinchi `IsDone` ustuniga ko'chadi, tarixga yozuv, takroriysi tug'iladi |

- Matn/tugmalar — `WorkTaskTelegram` da (YAGONA joy — tugmalar ikki joyda ayrilib ketmasin).
- Callback: **`wtdone:{taskId}`**. Faqat topshiriq **MAS'ULI** o'z chatidan belgilay oladi.
  "Allaqachon bajarilgan" — topshiriq HAR QANDAY yopiluvchi ustunda tursa (ilgari faqat birinchisi
  tekshirilardi).
- Eslatma idempotent: `WorkTask.ReminderSentDate` — bir kunda bir marta. Bog'lanmagan xodim uchun
  ham belgilab qo'yiladi (aks holda u botga ulangan kuni bir yillik "qarz" yog'ilardi).
- Vaqt sozlamasi: `CenterMeta.WorkTaskReminderEnabled/Hour/Minute` (default 09:30) — UI'da
  "Doska sozlamalari" oynasida. Saqlash **auditga yoziladi** (`CenterMeta` → "Sozlamalar" bo'limi,
  `SettingsController.LogSettings` naqshi — qaysi maydon o'zgargani bilan).
- ⚠️ **ARXIVLANGAN DOSKA** topshiriqlari eslatmaga TUSHMAYDI (tanlov — `WorkTaskFlow.DueForReminderAsync`)
  va nazorat paneli / mas'ullar ro'yxatidagi "ochiq/kechikkan" sanoqlarga kirmaydi (doska tanlanmagan
  ko'rinishda). Ilgari doska arxivlansa ham har kuni eslatma ketaverardi.

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

## 8. TASDIQLASH bosqichi (CRM Phase 5, 2026-10)

Migratsiya `CrmWorkspace`: `WorkTask.ApprovalRequired` (bool), `ApprovedAt`, `ApprovedById`, `ReturnCount`,
`LastReturnedAt`. Backfill YO'Q — mavjud topshiriqlarda `ApprovalRequired = false`, ya'ni ular o'zgarishsiz.

Savol: "bajarildimi va BERGAN odam qabul qildimi". Qoidalar — **`WorkTaskApproval`** (sof, `WorkTaskApprovalTests`),
bazaviy qism — **`WorkTaskFlow.Approval.cs`** (panel ham, bot ham AYNAN shu metodlar — `SpawnRepeatAsync` dagi
"yagona joy" qoidasi).

| Holat (`state`) | Yorliq | Qachon |
|---|---|---|
| `open` / `overdue` | Ochiq / Kechikkan | yopiluvchi ustunda emas |
| `awaiting_approval` | Tasdiq kutmoqda | bajarilgan, `ApprovalRequired`, `ApprovedAt` bo'sh |
| `approved` | Tasdiqlangan | tasdiqlangan |
| `done` | Bajarildi | bajarilgan, tasdiq talab qilinmaydi |

- **Standart**: yaratishda `approvalRequired` yuborilmasa — mas'ul BOSHQA odam bo'lsa yoqiq (o'ziga berilgan
  yoki mas'ulsiz topshiriqda tasdiqlovchi yo'q). Oynada «Bajarilgach tasdiqlash talab qilinsin» ptichkasi;
  tahrirda yuborilmasa — tegilmaydi.
- **Bajarildi** (panelda yopiluvchi ustunga ko'chirish, ustun bayrog'i, botda «✅ Bajardim») — avvalgidek
  (`CompletedAt`, takroriy nusxa) + `WorkTaskFlow.OnCompletedAsync`: tarixga `submitted` «Bajarildi — tasdiqlash
  kutilmoqda» va topshiriq BERGANGA qo'ng'iroqcha + Telegram «✅ {mas'ul} topshiriqni bajardi: «…». Tasdiqlang.»
  tugmasi bilan **«✅ Tasdiqlash»** (`wtok:{taskId}`). Bergan o'zi yopsa — xabar yo'q.
- **Tasdiqlash** `PUT work-tasks/{id}/approve` · **Qaytarish** `PUT {id}/return {reason}` (1..500) — `tasks.board`
  + QAT'IY darvoza: topshiriq BERGAN xodim yoki admin/superadmin (`WorkTaskApproval.IsApprover`), aks holda
  403 «Faqat topshiriq bergan xodim tasdiqlaydi». Ikkalasi ham `WorkTaskFlow.LockKey` qulfi ostida.
  Qaytarish: doskaning birinchi OCHIQ ustuniga (tepaga), `CompletedAt`/tasdiq tozalanadi, `ReturnCount++`,
  tarixga «Qaytarildi: {sabab}», mas'ulga Telegram «↩️ …» (tasdiqda «👍 …») + qo'ng'iroqcha.
- ⚠️ Yopiluvchi ustundan **har qanday** chiqish tasdiqni bekor qiladi (`WorkTaskFlow.OnReopened`, panel va
  `SyncCompletionAsync`).
- **Bot `wtok:`** — chat topshiriq BERGANNING yoki admin/superadmin akkauntiga bog'langan bo'lishi shart
  (`ChatApproverIdAsync`), aks holda «Bu amal sizga ruxsat etilmagan». ⚠️ Botdagi tugma javobi (`answerCallbackQuery`)
  umumiy tarmoqda allaqachon matnsiz yuboriladi, shuning uchun rad matni ODDIY xabar bo'lib keladi.
- Takroriy topshiriq qaytarilib qayta yopilsa ham keyingi nusxa BIR marta tug'iladi (idempotentlik o'zgarmadi).
- Ro'yxat filtri `status=awaiting|approved`; `assigneeId=me` / `createdById=me` — so'rovchi. Nazorat paneli
  qatorlarida «Tasdiq kutmoqda» · «Tasdiqlangan» · «Qaytarilgan».
- Telegram matnlari — `WorkTaskTelegram` (`ApprovalRequestText`, `ApproveKeyboard`, `ApprovedText`,
  `ReturnedText`). Xodimga operatsion xabar — to'g'ridan-to'g'ri `TelegramService` (ota-ona navbati emas).

## 9. LIDGA AVTOMATIK TOPSHIRIQ (SPEC-backlog-2026-10-17 F4 L1, TZ 3.3)

Migratsiya `LeadAutoTasks` (`WorkTask.LeadId`, `CenterMeta.LeadAutoTaskEnabled/AssigneeIds/Cursor/BoardId`). Qoida —
`LeadAutoTask` (Application, `LeadAutoTaskTests`). Sozlama — «Doska sozlamalari» oynasi → «Lidga avtomatik topshiriq»
(`GET/PUT work-tasks/lead-auto`, yozish `tasks.settings`, audit `CenterMeta`). Standart — O'CHIQ.

- **Qachon:** lid TASHQI kanaldan ma'lumot qoldirdi (lid formasi, landing, daraja testi, Instagram, Meta reklama
  formasi) — chaqiruv `LeadNotifier.NotifyNewLeadAsync` ICHIDA, Telegram tekshiruvidan OLDIN (bot sozlanmagan markazda
  ham; yangi tashqi kanal qo'shilsa ham unutilmaydi). Takroriy murojaat / yangi test natijasida — lidda ochiq topshiriq
  qolmagan bo'lsa «Takroriy murojaat — qo'ng'iroq: …».
- ⚠️ **QO'LDA kiritilgan lidga YO'Q** (`LeadsController.Create` → `autoTask: false`): kiritgan xodim lid bilan allaqachon
  ishlayapti.
- **Kimga:** lidning mas'uli (panel akkaunti bo'lsa) — aks holda sozlamadagi xodimlar NAVBAT bilan (`LeadAutoTask.Pick`).
  ⚠️ Navbat SAQLANMAYDI: keyingi xodim = OXIRGI avto-topshiriq olgan xodimdan keyingisi (qulf ichida o'qiladi — bir vaqtda
  kelgan ikki lid bir xodimga tushmaydi). `CenterMeta.LeadAutoTaskCursor` ustuni ISHLATILMAYDI (revyu S1: CenterMeta'ga har
  lidda yozish reyting/brend keshlarini bekor qilardi). O'chirilgan xodim navbatdan tushadi; hech
  kim qolmasa topshiriq mas'ulsiz (doskada ko'rinadi). ⚠️ Lidning O'Z mas'uli (`AssigneeUserId`) o'zgartirilMAYDI — KPI
  "biriktirilmagan lid" qoidasi tegilmasin.
- **Bir lid — bitta OCHIQ topshiriq** (`LeadId` + yopilmagan + arxivlanmagan); aylantirilgan/yo'qotilgan lidga yo'q.
- Topshiriq: «Lidga qo'ng'iroq: <ism>», tavsifda telefon/manba/qiziqish, muddat — BUGUN, muhimlik «Yuqori», tasdiq talab
  qilinmaydi (`CreatedById = ""` — tizim; kartada muallif «Tizim (avtomatik)»). Doska — sozlamadagisi (faol bo'lsa) →
  birinchi faol doska → yangi «Umumiy» (`WorkTaskFlow.AddDefaultBoardIfNoneAsync`, panel seed'i bilan bitta nusxa).
- Xabar: mas'ulga qo'ng'iroqcha (`UserNotification`, tur `tasks`) + Telegram (`WorkTaskTelegram.NewTaskText` + «✅ Bajardim»).
- ⚠️ Hech qachon istisno chiqarmaydi (lid qabulini buzmasin); saqlash yiqilsa O'Z qatorlari kontekstdan chiqariladi.
- Kartada «📞 Lid» chipi, topshiriq oynasida lid sahifasiga havola (lid o'chirilgan bo'lsa — «Lid o'chirilgan»).
- KPI «vazifasiz sdelka = 0» (`ChecklistAutoCheck.LeadsHaveTasksAsync`) endi `WorkTask.LeadId` ni ham bog'lanish deb sanaydi.
- Instagram/Meta lidlarida «Instagram Telegram signali» O'CHIQ bo'lsa karta ketmaydi, lekin topshiriq BARIBIR yaratiladi
  (`LeadAutoTask.EnsureByIdAsync`, revyu S4). Inbox'dagi «Lidga aylantirish» (operator o'zi) — `autoTask: false`.
