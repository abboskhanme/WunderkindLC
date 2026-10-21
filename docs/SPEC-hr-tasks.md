# SPEC — HR topshiriqlari (LMS'dan 1:1 ko'chirish) + Kanban «Topshiriqlar»ni olib tashlash (2026-10-21)

## Talab (foydalanuvchidan, 2026-10-21)

1. Xodimlarga topshiriq berib boshqarish bo'limi — **WunderkindLMS'dagi «HR topshiriqlari» moduli menyusi,
   funksiyasi va mantig'i bilan BIRGA-BIR** ko'chiriladi. Boshliq o'z xodimlariga topshiriq beradi.
2. Xodimning o'z panelida ham bo'lishi kerak (LMS'da — «Mening topshiriqlarim»).
3. LC'dagi mavjud Kanban «Topshiriqlar» (WorkTask) **"umuman kerak emas — tag-tugi bilan olib tashlansin"**
   (lidga avtomatik topshiriq, KPI bog'lanishi, bot tugmalari, jadvallar ham).
4. Eski WunderkindHR'dagi **Markaz** xodimlari (Muzaffar Abdubannoyev, Fayzullo) va ularning topshiriqlari
   LC'ga **import** qilinsin (LMS'ga faqat maktab xodimlari ko'chirilgan edi).

## Manba (haqiqat manbai)

LMS repo: `/Users/abboskhan/Documents/Projects/WunderkindLMS` (branch `master`).

| Qism | LMS fayli |
|---|---|
| Qoidalar (SPEC) | `docs/modules/hr-tasks.md` (+ §8.1 «Men bergan») |
| Domain | `SchoolLms.Domain/HrTasks.cs` |
| EF model | `SchoolLms.Infrastructure/Data/HrTasksModel.cs`, migratsiyalar `*_HrTasks.cs`, `*_HrEmployeePhoneOptional.cs` |
| Servis | `SchoolLms.Application/HrTasks/*.cs` (Service + .People/.Tasks/.Jobs/.Sync, Dtos, Ports, Scheduler, Seeder, Texts) |
| API | `SchoolLms.Server/Controllers/HrTasksController.cs` (panel), `HrMeController.cs` (xodim) |
| Integratsiya | `StaffController`/`TeachersController` → `HrTasksService.ReleaseUnusedEmployeeAsync` (o'chirishda; LC'da `ReleaseEmployeeAsync` — sobiq xodim, 409 emas) |
| Testlar | `SchoolLms.Tests/HrTasks/*.cs` |
| Klient | `schoollms.client/src/{types/hrTasks.ts, api/services/hrTasks.ts, components/hr-tasks/*, pages/admin/hr-tasks/*}` + `App.tsx`/`config/navigation.ts`/`config/constants.ts` qatorlari |
| Mini App (o'qituvchi) | `schoollms.client/src/pages/miniapp/ui-tg/src/screens/hr/*` |

**Bu port, qayta loyihalash emas.** Qoidalar, matnlar, raqamlar, ekranlar, JSON kontrakt (camelCase,
`types/hrTasks.ts`) — LMS'dagidek. Faqat quyidagi "moslash" jadvalidagi farqlar.

## Moslash (LMS → LC)

| Mavzu | LMS | LC'da |
|---|---|---|
| Namespace | `SchoolLms.*` | `WunderkindLC.*` (Domain / Application / Infrastructure / Server) |
| .NET | net10 / C# 14 | **net8 / C# 12** — `field`, extension bloklar, `?.=` va boshqa yangi sintaksis qayta yoziladi |
| Rollar | staff, teacher, admin, superadmin, **cashier** | `Roles.Staff/Teacher/Admin/SuperAdmin` (kassir roli YO'Q). `platformowner` — superadmin kabi o'tkazib yuboriladi |
| Xodim ↔ foydalanuvchi | `users` | `AppUser` (`Users`), lavozim = `AppUser.Position`; o'qituvchi = `Teacher` (arxivlanmagan, `UserId` bor), lavozim «O'qituvchi» |
| Telegram chatlari | `telegram_accounts` (user_id) | `TelegramRegistrations`: `UserId == user.Id` **yoki** `TeacherId == teacher.Id` (o'sha user'ning o'qituvchi kartasi). `HrEmployee.telegramUserId` = birinchi chat id |
| Telegram yuborish | `TelegramService.SendHtmlAsync/SendVoiceAsync` | `SendMessageAsync(chatId, html, null, ct, "HTML")`; **`SendVoiceAsync` LC `TelegramService` ga QO'SHILADI** (additiv) |
| Mini App havolalari | LMS Mini App | LC Mini App = shu veb-ilova (`TelegramBotService.PanelUrl`) — havola `…/admin/hr/mening-topshiriqlarim` (o'qituvchiga `…/teacher/topshiriqlar`) |
| «Taklif» (invite) | bir martalik link kodi | LC'da chat Panel ichida login qilganda O'ZI ulanadi (`linkTelegramChat`). Endpoint shakli qoladi: LC'da bir martalik kod mexanizmi bo'lsa — o'sha, bo'lmasa `inviteCode` bo'sh + bot havolasi va yo'riqnoma |
| Vaqt | `DateTimeOffset` (timestamptz) | O'sha (`Npgsql.EnableLegacyTimestampBehavior` bilan ham timestamptz). **SQLite testlari** uchun model konfiguratsiyasida provayder SQLite bo'lsa `DateTimeOffset` → binary konverter (EF tavsiyasi) |
| "Bugun" | `AppClock` (Asia/Tashkent) | LC `AppClock` |
| Ruxsat | kalit `hrTasks` | xuddi shu kalit — server katalogi + klient `adminPermissions` (`constants.ts`) + `PermissionCatalogTests` |
| Fayllar | `uploads/hr/{folder}/yyyy/MM/<guid>.ext` | o'sha (`/uploads` LC'da login talab qiladi — `UploadsGuard`) |
| Rejalashtiruvchi | `HrTasksScheduler` (5 daq), `hr_job_runs` | o'sha; IMPORT_MODE'ga bog'lanmagan (u faqat oylik hisob va ota-ona eslatmalarini to'xtatadi; prod oylab IMPORT_MODE=1 da) |
| Testlar | Testcontainers PG + HTTP (WebApplicationFactory) | LC: **SQLite `TestDb`** + servis/controller darajasida. Qoidalar testlari 1:1; API testlari xatti-harakat bo'yicha (scope 404, ruxsat, oqimlar) servis orqali |
| Panel UI | LMS UI kit | LC UI kit (`components/ui/*`: Button, Card, Modal, Input, PhoneInput, CredentialsBox, RightDrawer, DateInput, Toaster) |
| Menyu | «HR» bo'limi → TOPSHIRIQLAR guruhi + yuqori «Mening topshiriqlarim» | LC'da «HR» bo'limi yo'q → **yuqori darajadagi «Topshiriqlar» bo'limi** (Boshqaruv'dan keyin) shu 6 band bilan + yuqori «Mening topshiriqlarim» (staff/admin/superadmin) + o'qituvchi portalida «Mening topshiriqlarim» |
| O'qituvchi Mini App tabi | alohida `ui-tg` ilova | LC'da alohida Mini App yo'q → o'qituvchi portali sahifasi (`/teacher/topshiriqlar`) — `HrMyTasksPage` mobilga mos |
| Marshrutlar | `/admin/hr/topshiriqlar[/royxat|/royxat/yangi|/takroriy|/xodimlar|/hisobotlar|/sozlamalar]`, `/admin/hr/mening-topshiriqlarim` | o'sha. Eski Kanban manzillari (`/admin/topshiriqlar*`, `/admin/boshqaruv/staff-tasks`) → `/admin/hr/topshiriqlar` ga redirect |
| Jadval nomlari | `hr_*` snake_case | o'sha (LMS model konfiguratsiyasi aynan ko'chiriladi) |
| KPI / so'rovnomalar | LMS KPI HR topshiriqlarini o'qiydi | LC'da YO'Q (LC KPI boshqa modul) — ko'chirilmaydi |
| Xodim faolligi | — | `StaffActivity` dagi "yopilgan topshiriqlar" endi HR `done` topshiriqlaridan (Kanban o'rniga) |
| Audit | LMS HR auditga yozmaydi | o'sha (1:1) |
| Akkaunt o'chirish (revyu) | tarixi bor HR xodimi — 409 | LC'da o'chirish = ishdan bo'shatish → HR qatori SOBIQ XODIM bo'lib qoladi (`user_id = "deleted:…"`, Users'ga FK yo'q); o'qituvchi arxivlansa — HR qatori nofaol |
| Yozish huquqi (revyu) | har yozish — yalang `hrTasks` | o'sha; LC qisman tokenlari (`hrTasks:create`) faqat o'qiydi |

## Kanban'ni olib tashlash — nima ketadi

Entity'lar (`WorkTaskBoard/Column/Task/Item/Comment/Event/...`), `WorkTasksController*`, `WorkTaskFlow*`,
`WorkTaskApproval`, `WorkTaskTelegram`, `WorkTaskReminderService`, `LeadAutoTask` (+ `Crm/LeadAutoTasks.cs`),
`CenterMeta.WorkTaskReminder*` / `LeadAutoTask*`, bot callback'lari `wtdone:`/`wtok:`, `StaffRelease.OpenTasks`,
KPI `ChecklistAutoCheck` dagi `WorkTask.LeadId` qismi, MCP work-task vositalari va `McpAreas` kaliti,
`BackupService`/MCP read-only ro'yxatlaridagi jadvallar, ruxsat kalitlari `tasks*`, klient `pages/admin/tasks/*`,
`api/services/workTasks.ts`, bosh sahifa vidjeti, lid sahifasidagi topshiriq chipi/havolasi, testlar,
`.claude/rules/topshiriqlar.md`. **Migratsiya jadvallarni ham tushiradi** (foydalanuvchi: "tag-tugi bilan").

## Migratsiya

Bitta EF migratsiya `HrTasks` (oxirida, ikkala ish tugagach): `hr_*` jadvallar (LMS'dagi ustunlar, CHECK'lar,
unique kalitlar) + WorkTask jadvallari va `CenterMeta` ustunlarini DROP. Avtomatik generatsiyadan keyin qo'lda
o'qiladi (spurious DROP yo'qligi). Deploydan oldin `pg_dump`.

## Import (Markaz, bir martalik)

Manba: `WunderkindLMS/backups/old-hr/hr_final_20261011_2226.dump` (+ `hr_storage_final_20261011_2226.tar`).
Markaz: 2 xodim (Muzaffar Abdubannoyev — rahbari Ulug'bek; Fayzullo — rahbari Muzaffar), 137 topshiriq, 10 isbot.
Eski UUID'lar saqlanadi; xodim LC foydalanuvchisiga telefon, keyin ism bo'yicha moslanadi; Telegram id'lar
`TelegramRegistrations` ga yoziladi. Prod'da: dry-run → tasdiq → commit (foydalanuvchidan so'raladi).

## Qabul mezonlari

- Admin/superadmin va `hrTasks` ruxsatli xodim panelni ko'radi; scope: direktor — hammasi, menejer — o'zi +
  bo'ysunuvchilar daraxti; doiradan tashqari yozuv — 404.
- Har LC xodimi (o'qituvchi + staff/admin) avtomatik HR xodimi; «Mening topshiriqlarim» har birida.
- Topshiriq berish (yakka/ommaviy), takroriy shablonlar, isbot (fayl/izoh), KPI (raqamli) progress, qaytarish,
  bekor qilish, hisobotlar + eksport, sozlamalar, Telegram: yangi/qaytarilgan topshiriq darhol, ertalabki ro'yxat,
  muddat eslatmalari, kechki dayjest.
- Kanban izsiz: menyu, marshrut (redirectdan tashqari), API, bot, jadval yo'q.
- `dotnet test` va `npm run build` / `tsc` / `vitest` / `eslint` yashil; migratsiya `upgrade head` ishlaydi.
