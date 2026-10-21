# Topshiriqlar (HR) qoidalari

Boshliq o'z xodimlariga topshiriq beradi, xodim o'z panelida («Mening topshiriqlarim») bajaradi.
**WunderkindLMS'dagi «HR topshiriqlari» modulining 1:1 ko'chirmasi** (u esa eski WunderkindHR'ning
ko'chirmasi). SPEC: `docs/SPEC-hr-tasks.md`. LMS manbasi: `WunderkindLMS/docs/modules/hr-tasks.md`.
Migratsiya: `HrTasks` (11 ta `hr_*` jadval; Kanban jadvallarini ham shu migratsiya tushiradi).

> ⚠️ **Port, qayta loyihalash EMAS.** Qoida, matn, xato kodi, limit va JSON kontrakti
> (`WunderkindLC.Client/src/types/hrTasks.ts`) LMS'dagidek. LC'ga moslash joylari kodda `LC:` izohi
> bilan belgilangan va pastdagi §6 jadvalida. Yangi narsa qo'shishdan oldin LMS'da qanday ekanini qarang.

## 1. Kod qayerda

| Qism | Fayl |
|---|---|
| Domen (holatlar mashinasi, takroriylik, eslatma qoidalari, limitlar, xato kodlari) | `WunderkindLC.Domain/HrTasks.cs` |
| EF modeli (`hr_*`, snake_case, CHECK, unikal kalitlar) | `Infrastructure/Data/AppDbContext.HrTasks.cs` |
| Servis | `Application/HrTasks/HrTasksService*.cs` (`.People` · `.Tasks` · `.Jobs` · `.Sync`) |
| Telegram/fayl portlari | `HrTasksPorts.cs` (`IHrTelegramSender`, `IHrFileStore`) |
| Telegram matnlari (eski `texts.py`/`digest.py`, AYNAN) | `HrTasksTexts.cs` |
| Rejalashtiruvchi (har 5 daq) | `HrTasksScheduler.cs` (IMPORT_MODE'ga BOG'LANMAGAN — u faqat oylik hisob va ota-ona eslatmalarini to'xtatadi) |
| Panel API | `Server/Controllers/HrTasksController.cs` — `api/admin/hr/...` |
| Xodim API («Mening topshiriqlarim») | `Server/Controllers/HrMeController.cs` — `api/hr/me/...` |
| Testlar | `WunderkindLC.Tests/HrTasks/*` |

## 2. Kim nimani ko'radi — SCOPE

| Kim | Panel (`api/admin/hr`) | Doira |
|---|---|---|
| `admin`, `superadmin` | ha | butun tashkilot (direktor) |
| xodim + `hrTasks` kaliti + HR'da `hr_role = director` | ha | butun tashkilot |
| xodim + `hrTasks` kaliti (`manager`/`employee`) | ha | O'ZI + butun bo'ysunuvchilar daraxti (`hr_employee_managers`, rekursiv, halqaga chidamli) |
| xodim + kalit, lekin HR qatori YO'Q | ha | HECH KIM (fail-closed) |
| HR qatori bor har qanday akkaunt (o'qituvchi ham) | yo'q — faqat `api/hr/me` | o'zi |

- ⚠️ **Doiradan tashqari yozuv — 404, 403 EMAS** (mavjudligi oshkor bo'lmasin).
- Kalit (`hrTasks`) faqat PANELNI ochadi; QAYSI qatorlar — servis hal qiladi (`HrTasksService.PanelAsync`).
- Lavozimlar yozish va Sozlamalar — faqat direktor (403 `forbidden`).
- Menejer yaratgan xodim avtomatik uning ostiga tushadi; menejer hech kimga `director`/`manager` bera olmaydi
  (403 `hr_panel_access_forbidden`).
- Server darvozasi: `[AdminPerm("hrTasks", ReadRequiresPerm = true)]` — LMS'dagi `GatedRead`. Kalitning har qanday
  tokeni (`hrTasks:view` ham) o'qishni ochadi (GET, eksport ham). ⚠️ **HAR YOZISH — FAQAT YALANG `hrTasks`** (yoki
  admin/superadmin) — `HrTasksController.EnsureCanWrite` (LMS bilan bir xil, 2026-10-21 revyu): `[AdminPerm]` POST'ni
  `create` ga tenglagani uchun «Ko'rish + Qo'shish» roli xodimni bo'shatish, topshiriqni bekor qilish/qaytarish va
  shablonni o'chirib-yoqishni ham ochib qo'yardi. Rad — 403 `forbidden` matn bilan.
- Yangi AKKAUNT yaratish (`POST employees` ism + telefon bilan) — qo'shimcha `HasFullAccess(User, "staff")`
  (`staff-roles.md` §2), aks holda 403 `hr_panel_access_forbidden`. LC UI bu yo'lni chaqirmaydi (odamlar
  Xodimlar'dan); mavjud foydalanuvchini tanlash (`userId`) qo'shimcha ruxsat talab qilmaydi.
- `api/hr/me` — `[Authorize]` (rol muhim emas), xodim id'si TOKENDAN; faol HR qatori bo'lmasa 403
  `hr_not_employee`; begona topshiriq — 403 `task_not_owned`. Bu yerda xato kodlari `hr_` prefiksisiz.
- Har javob `no-store` (`NoStoreAttribute`) — umumiy kompyuterda keyingi odam oldingining topshiriqlarini ko'rmasin.

## 3. Hayot sikli (eski §5 — o'zgartirmang)

`open → done / returned / cancelled` (`missed` faqat eski `close` siyosatli qatorlarda). Bajarish uchun ISBOT —
fayl YOKI bo'sh bo'lmagan izoh (`hr_proof_required`). KPI (raqamli) topshiriqda qadamlar (`hr_task_progress`,
manfiy — tuzatish), maqsadga yetganda O'ZI `done`. Qayta ochish — faqat o'sha mahalliy kun va kechki yakun
yuborilishidan OLDIN. Muddati o'tgan topshiriq ochiq qoladi (`keep_open`), kech bajarilgani `is_late`.
Isbot: 5 ta × 10 MB, html/svg/js/xml rad etiladi. Ovozli izoh ≤ 300 s.

Takroriy shablonlar: kunlik/haftalik/oylik (oyning 31-i qisqa oyda oxirgi kunga), o'tgan muddat yaratilmaydi,
lavozim nishoni FAOL xodimlarga yoyiladi, nofaol xodimga hech narsa. Idempotentlik — unikal kalit
`(template_id, employee_id, period_start, due_at)`. Shablon o'chsa topshiriqlar qoladi (`template_id = null`).

## 4. Xodimlar ro'yxati — Boshqaruv → Xodimlar'dan

Har LC xodimi HR xodimi (`SyncLmsPeopleAsync`, rejalashtiruvchining har siklida): arxivlanmagan o'qituvchi kartasi
(akkaunti bor, lavozim «O'qituvchi») va har `staff`/`admin` akkaunt; superadmin va platforma egasi — yo'q. Faqat
QO'SHADI: hech kimni nofaol qilmaydi, rahbar/rolga tegmaydi; ism, lavozim (`AppUser.Position`) va yo'q telefonni
moslaydi. Yangi qatorlar «Biriktirilmagan» — direktor daraxtga joylaydi. `branch = center`.

- Odam qo'shish/tahrirlash — **Boshqaruv → Xodimlar**da (HR panelida «+ Xodim» yo'q; API qoldi).
- HR'da ishdan bo'shatish = HR qatori nofaol + ochiq topshiriqlar bekor; AKKAUNTGA TEGMAYDI.
- ⚠️ **Akkauntni O'CHIRISH — HECH QACHON BLOKLANMAYDI** (LMS'dan FARQ, 2026-10-21 revyu): LC'da akkauntni o'chirish =
  ishdan bo'shatish (`staff-roles.md` §6, boshqa yo'l yo'q), LMS'dagi 409 esa birorta topshirig'i bo'lgan xodimni
  abadiy o'chirib bo'lmas qilardi. `StaffController.Delete` va `TeachersController.Delete` →
  `HrTasksService.ReleaseEmployeeAsync`: tarixsiz HR qatori olib tashlanadi; tarixi bor qator **SOBIQ XODIM** bo'lib
  qoladi — nofaol, ochiq topshiriqlari bekor, `user_id = "deleted:<eski id>"` (unikal; sinxron uni tiriltirmaydi).
  Shuning uchun `hr_employees.user_id` da **Users'ga FK YO'Q** (LMS'da RESTRICT) — butunlikni shu chaqiruv ushlaydi.
  Audit qatorida «Topshiriqlar tarixi saqlandi (sobiq xodim), N ta ochiq topshiriq bekor qilindi».
  Yangi joyda `Users.Remove` yozsangiz — shu chaqiruvni ham qo'shing.
- ⚠️ **O'qituvchi ARXIVLANSA** (= ishdan ketdi, `billing.md`) HR qatori nofaol + ochiq topshiriqlari bekor
  (`HrTasksService.SetAccountActiveAsync(…, false)`); arxivdan QAYTARILSA — yana faol (bekor qilinganlar
  tiklanmaydi). Arxivdagi o'qituvchi kartasi Telegram chatlari ro'yxatiga ham kirmaydi.

## 5. Telegram

Yangi topshiriq (panel) va qaytarilgan topshiriq — xodimga DARHOL; ertalabki ro'yxat (07:30), muddat eslatmalari
(120/60/30 daq, sozlamada), kechki yakun (19:00, Du–Sh) — direktorga butun tashkilot, menejerga o'z daraxti; bo'sh
yakun yuborilmaydi, 4096 belgida xodim chegarasida bo'linadi. Ommaviy berishda xodimga BITTA xabar (+ ovozli izoh).
Bot sozlanmagan bo'lsa hech narsa yuborilmaydi; Telegram xatosi so'rovni buzmaydi. Xodim operativ xabari —
outbox'siz, to'g'ridan-to'g'ri (`messaging.md` §0.5 ruxsat bergan toifa).

## 6. LC moslashlari (LMS'dan FARQI)

| Mavzu | LC'da |
|---|---|
| Rollar | kassir roli yo'q (kassir = `staff`) |
| Telegram chatlari | `TelegramRegistrations`: staff/admin — `UserId`, o'qituvchi — o'qituvchi KARTASI `TeacherId`; `BlockedAt` bo'lgani o'tkaziladi (`HrTasksService.LinksAsync`) |
| Xabar tugmasi | **LC qo'shimchasi:** har xabarda «📋 Topshiriqlarim» `web_app` tugmasi — Mini App'da xodimga `admin/hr/mening-topshiriqlarim`, o'qituvchiga `teacher/topshiriqlar`, yakunda `admin/hr/topshiriqlar` (`HrTasksService.MyTasksUrl`) |
| «Taklif» | LC'da admin beradigan bir martalik kod YO'Q: chat Panel ichida login qilganda O'ZI ulanadi. `invite` javobida `inviteCode = ""` va bot havolasi; ulanganga 409 |
| Ruxsat tokeni | LC rol matritsasi `hrTasks:view`/`:create` kabi qisman tokenlar yozadi: har qanday token o'qiydi (`PermissionRules.HasSection`), YOZISH va «boshqaradi» (`canManage`) — faqat yalang kalit (`HasFullSection`, `EnsureCanWrite`) |
| Akkaunt o'chirish | LMS — 409; LC — sobiq xodim (yuqorida §4), `user_id` FK'siz |
| Fayl turlari | `/uploads` `ServeUnknownFileTypes = false` — shuning uchun isbot/biriktirma FAQAT u bera oladigan kengaytma bilan qabul qilinadi (`HrFileTypes`: freymvork xaritasi + `.heic/.heif/.opus/.7z`; `Program.cs` `/uploads` ham AYNAN shu xarita). Aks holda fayl "isbot" bo'lib qabul qilinar, rahbar ochganda 404 |
| Ustun nomlari | LC'da global snake_case YO'Q — HR jadvallari uchun nomlar `AppDbContext.HrTasks.cs` da qo'lda (LMS bilan bir xil: `pk_`/`fk_`/`ix_`) |
| SQLite testlari | PG-only qismlar (smallint[] standartlari, massiv CHECK'lari, jsonb) SQLite'da qo'yilmaydi, `DateTimeOffset` — ticks konverteri (faqat testlar) |
| Audit | LMS yozmaydi; LC'da faqat HR paneli YANGI AKKAUNT yaratganda bitta `Staff` qatori («… (Topshiriqlar → Xodimlar) — ruxsatsiz») |
| Zaxira | JSON zaxirada `hr*` biznes jadvallari bor; `hrTaskReminders`, `hrJobRuns` — `ExcludedTables` (texnik) |
| Xodim faolligi | `StaffActivity` «yopilgan topshiriqlar» = xodimning O'ZI bajargan HR topshiriqlari |
| KPI / so'rovnomalar | LMS'ning HR KPI'si va so'rovnomalari ko'chirilmagan. LC KPI cheklistining `tasks.overdue_zero` / `tasks.due_done` kalitlari HR topshiriqlaridan hisoblanadi (`ChecklistAutoCheck`, `kpi.md` §11) |

## 7. Yangi narsa qo'shsangiz

- Qoida/matn/limitni o'zgartirishdan oldin LMS'dagisini qarang — ikkala tizim bir xil ishlashi kerak.
- Yangi endpoint — `HrTasksController`/`HrMeController` ichida, `Run(...)` orqali (scope + xato formati shu yerda).
  Doiradan tashqari — 404.
- Vaqt — `DateTimeOffset` (UTC, offset 0), "bugun" — sozlamadagi mintaqa (`HrValues.LocalDate`), `DateTime.Now` EMAS.
- Yangi `hr_*` jadval — `AppDbContext.HrTasks.cs` dagi `Entities` ro'yxatiga (snake nomlar), `IAppDbContext.HrTasks.cs`
  ga va `BackupService` ga (yoki `ExcludedTables` ga sababi bilan).
- Telegram matni — `HrTasksTexts` da, foydalanuvchi matni `Escape()` dan o'tadi (HTML parse_mode).
- Yangi xodim manbasi (yangi rol) — `SyncLmsPeopleAsync` va `EmployeeUserRoles`.
