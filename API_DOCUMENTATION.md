# IntellectCRM — API hujjati

> Bu hujjat **IntellectCRM** (bitta o'quv markazi CRM) backend REST API'sining to'liq ma'lumotnomasi.
> Har bir controller, uning bazaviy yo'li, ruxsat talabi va endpointlari (metod · yo'l · vazifasi) keltirilgan.
> Manba: `IntellectCRM.Server/Controllers/*`. Real route'lar kodidan olingan.

- **Backend:** ASP.NET Core 8 (C#), Clean Architecture
- **Auth:** JWT (Bearer token) — `POST /api/auth/login` orqali olinadi
- **Real-time:** SignalR (`/hubs/chat`, `/hubs/live`) + CTI uchun raw WebSocket (`/ws?token=`)
- **Format:** so'rov/javob — JSON (fayl yuklash — `multipart/form-data`)

---

## 1. Autentifikatsiya va ruxsatlar tizimi

### 1.1. Kirish (JWT)
Barcha `[Authorize]` endpointlar `Authorization: Bearer <token>` sarlavhasini talab qiladi.
Token `POST /api/auth/login` (email + parol) orqali olinadi. Ruxsat claim'lari tokenga
YOZILMAYDI — ular har so'rovda DB'dan yuklanadi (superadmin xodim ruxsatini o'zgartirsa,
xodim qayta login qilmasdan darrov yangi ruxsat bilan ishlaydi).

### 1.2. Rollar (`AppUser.Role`)
| Rol | Tavsif |
|---|---|
| `superadmin` | Tizim egasi — admin huquqlari + muzlatilgan amallarni o'zgartirish, ruxsat berish, eksport. |
| `admin` | Oddiy administrator — barcha admin endpointlar (muzlatilgan ma'lumotlardan tashqari). |
| `staff` | O'qituvchi bo'lmagan xodim (kassir/administrator) — faqat `Permissions`dagi bo'limlar. |
| `teacher` | O'qituvchi — o'qituvchi ilovasi (`/api/teacher`). Support o'qituvchi = `teacher` + `IsSupport`. |
| `student` | O'quvchi/ota-ona — o'quvchi ilovasi (`/api/student`). |
| `platformowner` | Platforma egasi (yagona) — barcha modullar, tizim sozlamalari. |
| `ctiagent` | CTI (Local Call) Android agent — FAQAT mobil API (`/api/mobile`) va WebSocket. |

### 1.3. `[AdminPerm("...")]` darvozasi (staff uchun)
Admin controllerlar `[AdminPerm("<kalit>")]` bilan himoyalangan:
- **admin / superadmin** — to'liq kirish (ruxsat tekshirilmaydi).
- **staff** — **O'QISH** (GET/HEAD/OPTIONS) HAR DOIM ochiq (bo'limlararo bog'liqliklar buzilmasin);
  **YOZISH** (POST/PUT/DELETE/PATCH) faqat shu bo'lim ruxsati bo'lsa.
- **boshqa rollar** (teacher/student) — taqiqlanadi.

**Ruxsat kalitlari:** `app`, `settings`, `messages`, `students`, `teachers`, `teacherReports`,
`classes`, `calls`, `contracts`, `staff`, `schedule`, `leads`, `kassa`, `finance`, `cameras`, `feedback`.

### 1.4. Ochiq (autentifikatsiyasiz) endpointlar
`[AllowAnonymous]`: `POST /api/auth/login`, ommaviy test/brending (`/api/public/*`), landing lid
(`/api/public/landing-lead`), SMS callback (`/api/sms/callback`), telefoniya webhook
(`/api/telephony/moizvonki/{secret}` — maxfiy segment bilan), sertifikat tekshiruvi.

---

## 2. Autentifikatsiya va markaz

### AuthController
`api/auth` · Ruxsat: aralash (login `[AllowAnonymous]`, qolganlari `[Authorize]`). Foydalanuvchi autentifikatsiyasi va shaxsiy akkaunt.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| POST | /api/auth/login | Email + parol bilan kirish, JWT qaytaradi (rate-limit; arxiv/bloklangan rad; kirish vaqtlari yoziladi). |
| GET | /api/auth/me | Joriy foydalanuvchi ma'lumoti va ruxsatlari. |
| PUT | /api/auth/account | Joriy foydalanuvchi login (email), parol, telefonini o'zgartiradi (joriy parol bilan tasdiq). |

### CenterController
`api/school` · Ruxsat: `[Authorize]`. Markaz brendingi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/school | Markaz nomi va logotip URL manzili. |

### SettingsController
`api/admin/settings` · Ruxsat: `[Authorize]` + `[AdminPerm("settings")]`. Markaz sozlamalari va integratsiyalar.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/settings | Umumiy sozlamalar (davomat sabablari, sintetik davrlar). |
| GET | /api/admin/settings/school | Markaz ma'lumotlari (nom, direktor, telefon, email, manzil, hudud, logotip). |
| PUT | /api/admin/settings/school | Markaz ma'lumotlarini saqlaydi. |
| POST | /api/admin/settings/logo | Markaz logotipini yuklaydi (maks 8 MB). |
| DELETE | /api/admin/settings/logo | Logotipni o'chiradi. |
| GET | /api/admin/settings/telegram | Telegram bot sozlamalari (username, nom, kanal + token HOLATI; token qiymati qaytmaydi — u .env da). |
| PUT | /api/admin/settings/telegram | Bot nomi/username/kanalni saqlaydi. Token yuborilsa 400 (token faqat .env: TELEGRAM_BOT_TOKEN). |
| GET | /api/admin/settings/telegram-backup | Telegram backup sozlamalari (admin chat ID, jadval, yoqilgan). |
| POST | /api/admin/settings/telegram-backup | Telegram backup sozlamalarini saqlaydi (validatsiya bilan). |
| POST | /api/admin/settings/telegram-backup/test | Admin chat ID ga test xabari yuboradi. |
| POST | /api/admin/settings/telegram-backup/run | Baza backupini HOZIR Telegram orqali adminga yuboradi. |
| GET | /api/admin/settings/firebase | Firebase/FCM: web config + VAPID (ommaviy) va service account HOLATI (.env: FCM_SERVICE_ACCOUNT_JSON). |
| PUT | /api/admin/settings/firebase | Web config + VAPID saqlaydi. Service account yuborilsa 400 (u .env da). |
| GET | /api/admin/settings/azure-speech | Azure Speech holati (region + kalit holati; ikkalasi .env dan). |
| PUT | /api/admin/settings/azure-speech | Saqlamaydi — kalit/region yuborilsa 400 (.env: AZURE_SPEECH_KEY / AZURE_SPEECH_REGION). |
| GET | /api/admin/settings/gemini | Gemini holati (model + kalit holati; kalit .env: GEMINI_API_KEY). |
| PUT | /api/admin/settings/gemini | Saqlamaydi — kalit yuborilsa 400 (kalit faqat .env da). |
| GET | /api/admin/settings/check | To'lov cheki (termal kvitansiya) sozlamalari. |
| PUT | /api/admin/settings/check | To'lov cheki sozlamalarini saqlaydi. |
| GET | /api/admin/settings/eskiz | Eskiz SMS holati (login .env dan, sender, holat, balans). |
| PUT | /api/admin/settings/eskiz | Faqat sender (From) saqlanadi. Login/parol yuborilsa 400 (.env: ESKIZ_EMAIL / ESKIZ_PASSWORD). |
| GET | /api/admin/settings/app-apk | Yuklangan o'quvchi/o'qituvchi APK ma'lumotlari. |
| POST | /api/admin/settings/app-apk/{role} | Rol (`student`/`teacher`) uchun APK yuklaydi (maks 50 MB). |
| DELETE | /api/admin/settings/app-apk/{role} | Rol uchun APK ni o'chiradi. |
| GET | /api/admin/settings/turnstile | Turniket/FaceID sozlamalari. |
| PUT | /api/admin/settings/turnstile | Turniket sozlamalari + qurilma moslamasi. Login/parol yuborilsa 400 (.env: TURNSTILE_USERNAME / TURNSTILE_PASSWORD). |
| GET | /api/admin/settings/cameras | Kamera sozlamalari (yoqilgan, soni). |
| PUT | /api/admin/settings/cameras | Kamera yoqilgan/o'chirilgan holatini saqlaydi. |
| PUT | /api/admin/settings/absence-reasons | Davomat (kelmaganlik) sabablarini saqlaydi. |

### BranchesController
`api/admin/branches` · Ruxsat: `[Authorize(Roles = "superadmin")]`. Filiallar CRUD (faqat superadmin).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/branches | Barcha filiallar (nom bo'yicha). |
| POST | /api/admin/branches | Yangi filial (nom, manzil, GPS, radius). |
| PUT | /api/admin/branches/{id} | Filialni tahrirlaydi. |
| DELETE | /api/admin/branches/{id} | Filialni o'chiradi. |

### DistrictsController
`api/admin` · Ruxsat: `[Authorize]` + `[AdminPerm("settings")]`. Tashqi tuman/maktablar ma'lumotnomasi (o'quvchi o'qiydigan maktab).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/districts | Barcha tumanlar + ichidagi maktablari. |
| POST | /api/admin/districts | Yangi tuman. |
| PUT | /api/admin/districts/{id} | Tuman nomini tahrirlaydi. |
| DELETE | /api/admin/districts/{id} | Tuman + barcha maktablarini o'chiradi. |
| POST | /api/admin/districts/{districtId}/schools | Tuman ichida yangi maktab. |
| PUT | /api/admin/schools/{id} | Maktab nomini tahrirlaydi. |
| DELETE | /api/admin/schools/{id} | Maktabni o'chiradi. |

### ActionReasonsController
`api/admin/action-reasons` · Ruxsat: `[Authorize]` + `[AdminPerm("settings")]`. Amal sabablari (muzlatish/o'chirish/lid/guruh va h.k.) CRUD.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/action-reasons | Barcha sabablar (kategoriya + tartib). |
| POST | /api/admin/action-reasons | Yangi sabab (kategoriya validatsiya). |
| PUT | /api/admin/action-reasons/{id} | Sabab nomini tahrirlaydi. |
| DELETE | /api/admin/action-reasons/{id} | Sababni o'chiradi. |

### AuditController
`api/admin/audit` · Ruxsat: `[Authorize(Roles = "admin,superadmin")]`. O'zgarishlar tarixi (asosan moliya).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/audit | Audit tarixi (filtr: entityType/entityId/studentId/teacherId/action/from/to/limit). |

---

## 3. O'quvchilar, o'qituvchilar, xodimlar

### StudentsController
`api/admin/students` · Ruxsat: `[Authorize]` + `[AdminPerm("students")]` (ba'zi amal — superadmin). O'quvchilar CRUD, profil, AI tahlil, to'lov/hisob, arxiv, import/eksport.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/students | Faol o'quvchilar (`includeArchived=true` bilan arxiv ham). |
| GET | /api/admin/students/archived | Faqat arxivlangan o'quvchilar. |
| GET | /api/admin/students/{id} | Bitta o'quvchi (tahrirlash formasi uchun). |
| GET | /api/admin/students/{id}/profile | Shaxsiy daftar — to'liq (o'zlashtirish, davomat). |
| GET | /api/admin/students/{id}/ai-analyses | Saqlangan AI tahlillar tarixi. |
| POST | /api/admin/students/{id}/ai-analysis | Gemini AI tahlil (kuniga bir marta). |
| GET | /api/admin/students/{id}/support-feedback | Support darslari feedbacki. |
| POST | /api/admin/students/check-phones | Telefon raqamlari band emasligini tekshiradi. |
| POST | /api/admin/students | Yangi o'quvchi (akkaunt + guruh a'zoligi + avto-xabar). |
| PUT | /api/admin/students/{id} | Tahrirlaydi (`applyDiscount=true` chegirmani joriy oyga). |
| DELETE | /api/admin/students/{id} | O'chiradi (bog'liq + akkaunt; `reasonId`). |
| POST | /api/admin/students/{id}/archive | Arxivga ko'chiradi (login bloklanadi). |
| POST | /api/admin/students/{id}/restore | Arxivdan qaytaradi (ixtiyoriy yangi parol). |
| PUT | /api/admin/students/{id}/login-block | Login'ini cheklaydi/ochadi. |
| PUT | /api/admin/students/login-block-bulk | Bir nechta login'ini birdaniga cheklaydi/ochadi. |
| GET | /api/admin/students/{id}/credentials | Akkaunt login/parol (yo'q bo'lsa yaratadi). |
| POST | /api/admin/students/{id}/reset-password | Yangi tasodifiy parol (bir marta qaytadi). |
| GET | /api/admin/students/export | Barcha faolni login/parol bilan Excel (superadmin). |
| POST | /api/admin/students/export-selected | Tanlanganlarni Excel'ga eksport. |
| GET | /api/admin/students/import-template | Import uchun bo'sh Excel shabloni. |
| POST | /api/admin/students/import | Excel'dan ommaviy yaratish (qisman import). |
| GET | /api/admin/students/{id}/group-ledger | Bitta guruh oylik hisobi (`groupId`). |
| POST | /api/admin/students/{id}/payments | To'lov kiritadi (balans + kirim + avto-xabar). |
| GET | /api/admin/students/{id}/ledger | To'lov tarixi (oylar bo'yicha). |
| PUT | /api/admin/students/{id}/charges/{month} | Oy hisoblangan summasini qo'lda (superadmin; `groupId`). |
| GET | /api/admin/students/{id}/calls | Local Call (CTI) qo'ng'iroqlari (max 100). |
| GET | /api/admin/students/{id}/notes | Profil izohlari (yangisi tepada; canEdit/canDelete bilan). |
| POST | /api/admin/students/{id}/notes | Yangi izoh (muallif + vaqt bilan). |
| PUT | /api/admin/students/notes/{noteId} | Izoh matnini tahrirlaydi (muallifi/superadmin; `editedAt`). |
| DELETE | /api/admin/students/notes/{noteId} | Izohni o'chiradi (muallifi yoki superadmin). |

### TeachersController
`api/admin/teachers` · Ruxsat: `[Authorize]` + `[AdminPerm("teachers")]` (eksport — superadmin). O'qituvchilar CRUD, arxiv, akkaunt, maosh, samaradorlik.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/teachers | Faol o'qituvchilar (`includeArchived=true`). |
| GET | /api/admin/teachers/archived | Faqat arxivlangan. |
| POST | /api/admin/teachers | Yangi o'qituvchi (akkaunt + maosh sozlamalari). |
| PUT | /api/admin/teachers/{id} | Tahrirlaydi (maosh rejimi, toifa, ruxsatlar, ixtiyoriy parol). |
| DELETE | /api/admin/teachers/{id} | O'chiradi (faol guruhda bo'lsa rad). |
| POST | /api/admin/teachers/{id}/archive | Arxivga ko'chiradi. |
| POST | /api/admin/teachers/{id}/restore | Arxivdan qaytaradi. |
| GET | /api/admin/teachers/{id}/credentials | Akkaunt login/parol (yo'q bo'lsa yaratadi). |
| POST | /api/admin/teachers/{id}/reset-password | Yangi tasodifiy parol. |
| GET | /api/admin/teachers/export | Barchani login/parol bilan Excel (superadmin). |
| GET | /api/admin/teachers/{id}/performance | Bitta o'qituvchi retention/loss/effectiveness. |
| GET | /api/admin/teachers/performance | Barcha o'qituvchilar samaradorligi. |
| GET | /api/admin/teachers/{id}/salary-ledger | Maosh batafsil hisobi (`from`/`to`). |
| PUT | /api/admin/teachers/{id}/group-salaries | Per-guruh maosh sozlamasini yangilaydi. |

### StaffController
`api/admin/staff` · Ruxsat: `[Authorize]` + `[AdminPerm("staff")]` (ruxsatlar — superadmin). Xodimlar (role=staff) CRUD, akkaunt, ruxsatlar.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/staff | Barcha xodimlar. |
| GET | /api/admin/staff/role-templates | Xodim roli shablonlari. |
| POST | /api/admin/staff | Yangi xodim (ruxsatlar faqat superadmin bersa). |
| PUT | /api/admin/staff/{id} | Tahrirlaydi (F.I.SH, lavozim, telefon, ixtiyoriy parol). |
| DELETE | /api/admin/staff/{id} | O'chiradi (`reasonId`). |
| GET | /api/admin/staff/{id}/credentials | Akkaunt logini. |
| POST | /api/admin/staff/{id}/reset-password | Yangi tasodifiy parol. |
| PUT | /api/admin/staff/{id}/permissions | Bo'lim ruxsatlarini belgilaydi (superadmin). |

### TeacherAttendanceController
`api/admin/teacher-attendance` · Ruxsat: `[Authorize]` + `[AdminPerm("teachers")]`. O'qituvchilar kunlik ish davomati (turniket sinxronizatsiyasi bilan).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/teacher-attendance/dashboard | Kunlik dashboard (holat, kelgan/ketgan, kechikish; `date`). |
| POST | /api/admin/teacher-attendance/sync | Turniketdan hodisalarni tortib davomatni yangilaydi. |
| GET | /api/admin/teacher-attendance | Oylik board (`month`). |
| PUT | /api/admin/teacher-attendance | Bitta kun-katakni belgilaydi. |
| PUT | /api/admin/teacher-attendance/day | Bir kunda barcha o'qituvchini belgilaydi (toggle). |

### TeacherReportsController
`api/admin/teacher-reports` · Ruxsat: `[Authorize]` + `[AdminPerm("teacherReports")]`. O'qituvchilar faollik hisoboti (dars, baho, mavzu/uy-vazifa).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/teacher-reports | Barcha o'qituvchilar umumiy (`month` bo'sh = barcha oylar). |
| GET | /api/admin/teacher-reports/{id} | Bitta o'qituvchi batafsil (guruh/fan yoyilmasi; `month`). |

### AppTeachersController
`api/admin/app/teachers` · Ruxsat: `[Authorize]` + `[AdminPerm("app")]`. "Ilova → O'qituvchilar" — ilova faolligi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/app/teachers | O'qituvchilar ilova faolligi (login vaqtlari + oxirgi qurilma). |

### ParentsController
`api/admin/parents` · Ruxsat: `[Authorize]` + `[AdminPerm("app")]`. Ota-onalar — o'quvchi akkauntlari telefon bo'yicha guruhlangan.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/parents | Ota-onalar (telefon bo'yicha farzandlar, ilova aktivligi, oxirgi kirish). |

---

## 4. Guruhlar, kurslar, xonalar

### ClassesController
`api/admin/classes` · Ruxsat: `[Authorize]` + `[AdminPerm("classes")]`. Guruhlar CRUD, arxiv, xona konflikti, M2M a'zolik, yakunlash-ko'chirish.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/classes | Faol guruhlar (`includeArchived=true`). |
| GET | /api/admin/classes/archived | Arxivlangan guruhlar. |
| POST | /api/admin/classes | Yangi guruh (o'qituvchi majburiy; xona konflikti `force` bilan). |
| PUT | /api/admin/classes/{id} | Tahrirlaydi (`applyFee=true` narxni joriy oyga; `force`). |
| DELETE | /api/admin/classes/{id} | O'chiradi (faol o'quvchi bo'lsa rad). |
| POST | /api/admin/classes/{id}/archive | Arxivlaydi (faol o'quvchilar ham). |
| POST | /api/admin/classes/{id}/unarchive | Arxivdan chiqaradi. |
| GET | /api/admin/classes/{id}/members | Guruh a'zolari (faol + o'tgan). |
| POST | /api/admin/classes/{id}/members | O'quvchini qo'shadi (M2M "trial"; sig'im to'lsa rad). |
| DELETE | /api/admin/classes/{id}/members/{studentId} | Guruhdan chiqaradi (`reasonId`). |
| POST | /api/admin/classes/{id}/members/{studentId}/return-trial | A'zolikni sinovga qaytaradi. |
| POST | /api/admin/classes/{id}/members/{studentId}/activate | Aktivlashtiradi (qisman birinchi oy to'lovi). |
| POST | /api/admin/classes/{id}/members/{studentId}/freeze | Muzlatadi (qisman to'lov). |
| POST | /api/admin/classes/{id}/members/bulk-freeze | OMMAVIY muzlatish — shu guruhda tanlanganlar (`studentIds`, `date`, `reasonId`). |
| POST | /api/admin/classes/{id}/members/bulk-activate | OMMAVIY aktivlashtirish — shu guruhda tanlanganlar (`studentIds`, `date`). |
| POST | /api/admin/classes/members/bulk-freeze | OMMAVIY muzlatish — tanlanganlarning BARCHA guruhlardagi a'zoliklari. |
| POST | /api/admin/classes/members/bulk-activate | OMMAVIY aktivlashtirish — tanlanganlarning BARCHA guruhlardagi a'zoliklari. |
| GET | /api/admin/schedule | Dars jadvali: darslar, xonalar/o'qituvchilar va tig'izlik (`schedule.timetable`). |
| GET | /api/admin/schedule/gaps | Bo'sh oraliqlar + tavsiyalar (`scope=room\|teacher`, `ownerId`, `minMinutes`). |
| GET | /api/admin/classes/student/{studentId}/groups | O'quvchining barcha a'zoliklari. |
| GET | /api/admin/classes/fill | Guruh to'ldirish hisoboti (bo'sh o'rinlar). |
| POST | /api/admin/classes/{id}/complete-and-transfer | Yakunlab arxivlaydi, sertifikat, yangi guruh. |
| POST | /api/admin/classes/{id}/close | Guruhni YOPADI: barcha a'zolar `date` sanasidan muzlatiladi (qarz shu sanagacha), guruh arxivga. |

### SubjectsController
`api/admin/subjects` · Ruxsat: `[Authorize]` + `[AdminPerm("schedule")]`. Kurslar (Subject) CRUD — narx o'zgarsa guruhlar oyligi yangilanadi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/subjects | Barcha kurslar. |
| POST | /api/admin/subjects | Yangi kurs (narx + dars narxi). |
| PUT | /api/admin/subjects/{id} | Tahrirlaydi (`applyFee=true` joriy oyga). |
| DELETE | /api/admin/subjects/{id} | O'chiradi. |

### RoomsController
`api/admin/rooms` · Ruxsat: `[Authorize]` + `[AdminPerm("classes")]`. O'quv xonalari CRUD (soft delete) + bandlik metrikalari.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/rooms | Barcha xonalar. |
| POST | /api/admin/rooms | Yangi xona (nom takrorlanmasin, sig'im 1–1000). |
| PUT | /api/admin/rooms/{id} | Tahrirlaydi. |
| DELETE | /api/admin/rooms/{id} | Soft-delete (guruhlarda RoomId SET NULL). |
| GET | /api/admin/rooms/utilization-dashboard | Barcha xonalar bandlik + samaradorlik. |
| GET | /api/admin/rooms/{id}/utilization | Bitta xona samaradorlik metrikasi. |
| GET | /api/admin/rooms/{id}/capacity | Bitta xona sig'im samaradorligi. |
| GET | /api/admin/rooms/{id}/detail | Bitta xona birlashtirilgan metrika. |

### LocationsController
`api/admin/locations` · Ruxsat: `[Authorize]` + `[AdminPerm("app")]`. O'quvchilar joylashuvi (xarita).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/locations | Joylashuvi bor o'quvchilar (`className` filtri). |

---

## 5. Jurnal, o'quv dasturi, baholash

### JournalController
`api/admin/journal` · Ruxsat: `[Authorize]` + `[AdminPerm("classes")]`. Guruh jurnali: ustunlar, baho/davomat, ommaviy davomat, tahrirlash siyosati, Excel import.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/journal/columns | Guruh+fan+chorak dars ustunlari (sana + raqam). |
| GET | /api/admin/journal/conducted | Sanada o'tilgan darslar. |
| GET | /api/admin/journal | Jurnal yozuvlari (baho/davomat). |
| GET | /api/admin/journal/group | Guruh oylik jurnali (ustunlar dars kunlaridan). |
| PUT | /api/admin/journal | Bitta katakni belgilash (siyosat tekshiruvi). |
| POST | /api/admin/journal/bulk-attendance | Bir darsga barcha o'quvchiga davomat (null=hammasi keldi). |
| DELETE | /api/admin/journal | Bitta katakni tozalash. |
| GET | /api/admin/journal/policy | Joriy jurnal tahrirlash siyosati. |
| PUT | /api/admin/journal/policy | Jurnal siyosatini saqlaydi. |
| GET | /api/admin/journal/notes | Mavzu va uy-vazifalar. |
| PUT | /api/admin/journal/notes | Mavzu/uy-vazifani saqlaydi (dars qo'shish uchun ham). |
| GET | /api/admin/journal/topics-template | Mavzular Excel shabloni (.xlsx). |
| POST | /api/admin/journal/topics-import | Excel'dan mavzu+uy-vazifa import. |

### CurriculumController
`api/admin/curriculum` · Ruxsat: `[Authorize]` + `[AdminPerm("schedule")]`. Kurs sillabusi (Daraja→Mavzu→Band) CRUD, dars kontenti, guruh o'tilishi/prognozi, o'quvchi progressi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/curriculum/{subjectId} | Kurs to'liq sillabusi (daraxt). |
| POST | /api/admin/curriculum/{subjectId}/levels | Daraja yaratadi. |
| PUT | /api/admin/curriculum/levels/{id} | Darajani tahrirlaydi. |
| DELETE | /api/admin/curriculum/levels/{id} | Darajani o'chiradi. |
| POST | /api/admin/curriculum/levels/{levelId}/topics | Mavzu yaratadi. |
| PUT | /api/admin/curriculum/topics/{id} | Mavzuni tahrirlaydi. |
| DELETE | /api/admin/curriculum/topics/{id} | Mavzuni o'chiradi. |
| POST | /api/admin/curriculum/topics/{topicId}/items | Band (dars) yaratadi. |
| PUT | /api/admin/curriculum/items/{id} | Band nomi/izohini tahrirlaydi. |
| DELETE | /api/admin/curriculum/items/{id} | Bandni o'chiradi. |
| GET | /api/admin/curriculum/item/{id} | Dars kontenti (video/matn/audio/lug'at/test). |
| PUT | /api/admin/curriculum/items/{id}/content | Dars kontentini saqlaydi. |
| GET | /api/admin/curriculum/import-template | O'quv dasturi Excel shabloni. |
| POST | /api/admin/curriculum/{subjectId}/import-excel | Excel'dan dastur (replace/append). |
| POST | /api/admin/curriculum/levels/{levelId}/copy-to/{targetSubjectId} | Darajani boshqa kursga nusxalaydi. |
| POST | /api/admin/curriculum/{subjectId}/import | Sillabusni JSON bilan almashtiradi. |
| GET | /api/admin/curriculum/{subjectId}/progress/{studentId} | O'quvchi tugatgan bandlar. |
| POST | /api/admin/curriculum/progress | O'quvchi band progressini belgilaydi. |
| GET | /api/admin/curriculum/group/{groupId} | Guruh o'tilishi + tugash prognozi. |
| POST | /api/admin/curriculum/group/{groupId}/cover | Bandni guruhda "o'tilgan" qiladi. |
| POST | /api/admin/curriculum/group/{groupId}/revision | Takrorlash darsini qo'shadi/oladi. |
| GET | /api/admin/curriculum/student/{studentId}/coverage-log | O'tilgan sillabus vaqt jadvali. |

### GradingController
`api/admin/grading` · Ruxsat: `[Authorize]` + `[AdminPerm("schedule")]` (student summary — `[AllowAnonymous]`). Baholash mezonlari CRUD, guruhga biriktirish, baholash.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/grading/criteria | Barcha mezonlar. |
| POST | /api/admin/grading/criteria | Yangi mezon. |
| PUT | /api/admin/grading/criteria/{id} | Mezonni tahrirlaydi. |
| DELETE | /api/admin/grading/criteria/{id} | Mezonni o'chiradi. |
| GET | /api/admin/grading/group/{groupId}/criteria | Guruh mezon id'lari. |
| PUT | /api/admin/grading/group/{groupId}/criteria | Guruh mezonlarini almashtiradi. |
| GET | /api/admin/grading/group/{groupId}/board | Guruh baholash grid'i. |
| POST | /api/admin/grading/grade | Bitta katakni belgilaydi. |
| POST | /api/admin/grading/grade/bulk | Sanada bir mezon bo'yicha barchani. |
| GET | /api/admin/grading/student/{studentId}/summary | O'quvchi baholash xulosasi (anonim). |
| GET | /api/admin/grading/group/{groupId}/summary | Guruh baholash statistikasi. |
| GET | /api/admin/grading/groups/summary | Barcha guruhlar statistikasi (keshlanadi). |

### ClassAnalyticsController
Bazaviy route yo'q · Ruxsat: `[Authorize(Roles = "admin,superadmin,staff")]`. Guruh analitikasi, statistika, o'quvchilar reytingi (keshlanadi).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/classes/{classId}/performance | Guruh fan bo'yicha o'zlashtirish/qatnashish. |
| GET | /api/admin/classes/stats | Barcha guruhlar statistikasi (keshlanadi). |
| GET | /api/admin/students/rating | Markaz o'quvchilar reytingi (keshlanadi). |

### CertificatesController
Bazaviy route yo'q · Ruxsat: har action o'zi (student portal / admin / public). Sertifikatlar.

| Metod | Yo'l | Vazifasi | Ruxsat |
|---|---|---|---|
| GET | /api/student/certificates | O'quvchi sertifikatlari | student,parent |
| GET | /api/student/certificates/{id}/download | Sertifikat faylini yuklaydi | student,parent |
| GET | /api/admin/students/{studentId}/certificates | Admin: o'quvchi kurslari + sertifikatlari | Authorize |
| GET | /api/admin/students/{studentId}/certificates/{id}/download | Admin: fayl yuklaydi | Authorize |
| GET | /api/admin/certificate-templates | Andozalar ro'yxati | Authorize |
| POST | /api/admin/certificate-templates | Kurs uchun andoza | Authorize |
| GET | /api/admin/certificate-templates/{id} | Bitta andoza | Authorize |
| POST | /api/admin/students/{studentId}/certificates/generate | Qo'lda sertifikat yaratadi | Authorize |
| DELETE | /api/admin/certificate-templates/{id} | Andozani o'chiradi | Authorize |
| GET | /api/public/certificates/{id}/verify | Anonim: sertifikat tekshiruvi | AllowAnonymous |

---

## 6. CRM (lidlar), daraja testi, feedback

### LeadsController
`api/admin/leads` · Ruxsat: `[Authorize]` + `[AdminPerm("leads")]`. CRM lidlari — CRUD, hodisa tarixi, sinov darslari, test havolasi, o'quvchiga aylantirish, statistika.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/leads | Barcha lidlar + birinchi dars davomati + takroriy murojaat (`repeatCount`/`lastRepeatAt`). |
| POST | /api/admin/leads | Yangi lid (+ Telegram + avto-SMS). |
| POST | /api/admin/leads/{id}/send-test | Lidga bir martalik daraja-testi havolasi (SMS). |
| PUT | /api/admin/leads/{id} | Lid ma'lumotlarini tahrirlaydi. |
| PATCH | /api/admin/leads/{id} | Lidni boshqa bosqichga ko'chiradi. |
| DELETE | /api/admin/leads/{id} | O'chiradi (sabab; arxiv + audit). |
| GET | /api/admin/leads/{id}/events | Lid hodisalari tarixi. |
| POST | /api/admin/leads/{id}/events | Qo'lda hodisa (izoh) qo'shadi. |
| GET | /api/admin/leads/{id}/trials | Sinov darslari ro'yxati. |
| POST | /api/admin/leads/{id}/trials | Sinov darsi belgilaydi. |
| GET | /api/admin/leads/trials/{trialId}/receipt | Sinov darsiga yozilish cheki. |
| PATCH | /api/admin/leads/trials/{trialId} | Sinov natijasi (stayed/left). |
| POST | /api/admin/leads/{id}/convert | Lidni o'quvchiga aylantiradi. |
| GET | /api/admin/leads/courses | Kurslar nomlari — lid formasi "Qiziqqan fani" ro'yxati uchun. |
| GET | /api/admin/leads/stats | CRM statistikasi (konversiya, oylik, qiziqish fanlari). |

### LeadStagesController
`api/admin/lead-stages` · Ruxsat: `[Authorize]` + `[AdminPerm("leads")]`. CRM voronka bosqichlari (kanban ustunlari).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/lead-stages | Barcha bosqichlar (Order). |
| POST | /api/admin/lead-stages | Yangi bosqich. |
| PUT | /api/admin/lead-stages/{id} | Nom/rangini tahrirlaydi. |
| DELETE | /api/admin/lead-stages/{id} | O'chiradi. |
| PATCH | /api/admin/lead-stages/reorder | Ustunlar tartibini saqlaydi. |

### LevelTestsController
`api/admin/level-tests` · Ruxsat: `[Authorize]` + `[AdminPerm("schedule")]`. Daraja testi — CRUD, havolalar, natijalar (topshiruvchilar lid bo'ladi).

⚠️ **O'qish qisman darvozalangan:** `overall-stats`, `{id}/stats`, `{id}/submissions` va
`{id}/invites` METOD darajasida `[AdminPerm("schedule", ReadRequiresPerm = true)]` — javobda
abituriyentlarning telefonlari va to'lov summalari bor (odatda `AdminPerm` GET'ni har qanday
xodimga ochadi). Sinf darajasida qo'yilmadi ATAYIN: `GET /api/admin/level-tests` (testlar
ro'yxati) lidlar bo'limidagi "test yuborish" oynasiga kerak (`LeadDetailModal`), uni yopish
`leads` ruxsatli xodimning ishini buzardi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/level-tests | Testlar ro'yxati (savol + topshiruv soni). Lidlar bo'limi ham chaqiradi — o'qish darvozalanMAGAN. |
| GET | /api/admin/level-tests/{id} | Bitta test (savollar + diapazonlar). |
| POST | /api/admin/level-tests | Yangi test. |
| PUT | /api/admin/level-tests/{id} | Tahrirlaydi. |
| DELETE | /api/admin/level-tests/{id} | O'chiradi. |
| GET | /api/admin/level-tests/{id}/invites | Yuborilgan havolalar (lid + SMS holati). `ReadRequiresPerm`. |
| GET | /api/admin/level-tests/overall-stats | «Test statistikasi» sahifasi: voronka (topshirdi → lid → o'quvchi → to'ladi) test/bosqich/daraja kesimida + 30 kunlik oqim. `ReadRequiresPerm`. `DataCache` da (`level-tests:overall-stats`, TTL 10 daq; bog'liq turlar: LevelTest, LevelTestSubmission, LevelTestInvite, Lead, LeadStage, StudentGroup, FinanceTransaction, **Group, Teacher** — javobda guruh nomi va o'qituvchi F.I.Sh ham bor). `Rows` — eng yangi **500** ta, jami son alohida `RowsTotal` da (sanoqlar cheklovdan mustaqil). |
| GET | /api/admin/level-tests/{id}/submissions | Topshirganlar ro'yxati. `ReadRequiresPerm`. |
| GET | /api/admin/level-tests/{id}/stats | Bitta test statistikasi — sonlar **TAKRORSIZ LIDLAR** bo'yicha (`LevelTestService.DistinctByLead`, umumiy sahifa bilan bir xil); `Leads` maydoni foizlar uchun maxraj. `ReadRequiresPerm`. |
| GET | /api/admin/level-tests/ai-analyses | Daraja testlari voronkasining saqlangan AI tahlillari (eng yangisi birinchi). `ReadRequiresPerm` — javobda o'sha statistika (voronka + tushum) turadi. |
| POST | /api/admin/level-tests/ai-analysis | Voronkani Gemini orqali TANQIDIY tahlil qiladi (`FunnelAiAnalysisService`, `kind=level-tests`). |

⚠️ **AI tahlil (`ai-analyses` / `ai-analysis`) — ikkala bo'limda bir xil qoida:** tahlil **kuniga
bir marta** yaratiladi (bugungi yozuv bo'lsa Gemini CHAQIRILMAYDI, `alreadyToday=true` bilan
mavjudi qaytadi); **auditga YOZILMAYDI** (hech narsa o'zgarmaydi); **promptga shaxsiy ma'lumot
ketmaydi** — faqat jamlanma raqamlar, murojaatchilarning ismi/telefoni/javoblari HECH QACHON
yuborilmaydi. Yaratish o'sha bo'limning **`create`** amalini talab qiladi (faqat ko'rish ruxsatli
xodim tahlilni o'qiydi, lekin yangisini boshlay olmaydi). Batafsil: `.claude/rules/ai-analysis.md`.

### LeadFormsController
`api/admin/lead-forms` · Ruxsat: `[Authorize]` + `[AdminPerm("leads", ReadRequiresPerm = true)]`.
Lid formalari — har bir ijtimoiy tarmoq uchun alohida ommaviy forma (`/forma/{slug}`) o'z MANBASI
bilan; ariza CRM'da lid bo'lib tushadi. O'qish ham darvozalangan (javobda telefonlar bor).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/lead-forms | Formalar ro'yxati (maydon + ariza soni, ochilishlar). |
| GET | /api/admin/lead-forms/{id} | Bitta forma (qo'shimcha savollari bilan). |
| POST | /api/admin/lead-forms | Yangi forma. |
| PUT | /api/admin/lead-forms/{id} | Tahrirlaydi (maydonlar to'liq almashtiriladi). |
| POST | /api/admin/lead-forms/{id}/duplicate | Nusxa — yangi havola, manbasiz va o'chiq holda. |
| DELETE | /api/admin/lead-forms/{id} | Formani + arizalar tarixini o'chiradi (lidlar qoladi). |
| GET | /api/admin/lead-forms/submissions | Arizalar (`?formId=`, max 1000) + lidning holati. |
| GET | /api/admin/lead-forms/stats | Voronka: ochildi → ariza → lid → o'quvchi (forma/manba/ref). `DataCache` da (bog'liq jadval o'zgarsa avto-yangilanadi). |
| GET | /api/admin/lead-forms/sources | Manba ma'lumotnomasi (forma uchun). |
| GET | /api/admin/lead-forms/field-kinds | Qo'llab-quvvatlanadigan maydon turlari. |
| GET | /api/admin/lead-forms/ai-analyses | Lid formalari voronkasining saqlangan AI tahlillari (eng yangisi birinchi). |
| POST | /api/admin/lead-forms/ai-analysis | Voronkani Gemini orqali TANQIDIY tahlil qiladi (`FunnelAiAnalysisService`, `kind=lead-forms`) — kuniga bir marta, auditga yozilmaydi, promptga shaxsiy ma'lumot ketmaydi (yuqoridagi `LevelTestsController` izohi bilan bir xil qoida). |

⚠️ Kurslar ma'lumotnomasi endpointi YO'Q: forma kursni markazdagi kurslar (`Subject`) katalogidan
olmaydi — variantlar formaning O'ZIDA yoziladi (`courseOptions`), mijoz shulardan tanlaydi.

### PublicLeadFormController
`api/public/form` · Ruxsat: `[AllowAnonymous]` (yuborish — `public-lead` rate-limit).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/public/form/{slug} | Faol formani oladi (+ ochilishlar sanog'ini oshiradi). |
| POST | /api/public/form/{slug}/submit | Ariza — lid yaratiladi yoki telefon bo'yicha mavjudiga biriktiriladi. |

### FeedbackController
`api/admin/feedback` · Ruxsat: `[Authorize]` + `[AdminPerm("feedback")]`. Taklif/shikoyatlar (ota-onalardan).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/feedback | Taklif/shikoyatlar (type/status filtri, oxirgi 300). |
| POST | /api/admin/feedback/{id}/resolve | "Hal qilindi" deb belgilaydi. |

---

## 7. Moliya, shartnomalar, dashboard

### FinanceController
`api/admin/finance` · Ruxsat: `[Authorize]` + `[AdminPerm("finance")]`. Moliya — tranzaksiyalar, cheklar, oylik hisoblash, hisobotlar.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/finance/transactions | Tranzaksiyalar (sana/yo'nalish/toifa filtri). |
| GET | /api/admin/finance/receipt/{id} | Bitta to'lov cheki. |
| POST | /api/admin/finance/transactions | Tranzaksiya yaratadi (idempotent 5s; balans + avto-xabar). |
| PUT | /api/admin/finance/transactions/{id} | Tahrirlaydi (balans delta). |
| DELETE | /api/admin/finance/transactions/{id} | O'chiradi (sabab; balans qaytadi; arxiv). |
| GET | /api/admin/finance/salary-report | O'qituvchi maoshlari hisoboti. |
| GET | /api/admin/finance/student-report | O'quvchilar to'lov hisoboti (hisoblangan/to'langan/qarz/avans). |
| GET | /api/admin/finance/summary | Davr umumiy moliyaviy xulosa. |
| GET | /api/admin/finance/course-report | Kurs/guruh kesimida hisobot. |
| GET | /api/admin/finance/group-payments/{groupId} | Guruh ichida to'lov holati. |
| POST | /api/admin/finance/accrue | Oylik to'lovni hisoblaydi (month bo'sh = barcha). |
| GET | /api/admin/finance/cashiers?from=&to= | Kassirlar kesimi: kim qancha qabul qilgan (soni, jami, naqd/karta/bank). Faqat admin/superadmin yoki `finance` ruxsatli xodim. |
| GET | /api/admin/finance/cashier-payments?from=&to=&cashierId=&cashierName= | Bitta kassir kiritgan to'lovlar ro'yxati + jami. |
| GET | /api/admin/finance/monthly | Bir yil oylik kirim/chiqim (12 oy). |

### KassaController
`api/admin/kassa` · Ruxsat: `[Authorize]` + `[AdminPerm("kassa")]`. Kassa — pul qabul qilish ish o'rni:
kassir o'quvchini topib to'lov kiritadi. To'lov mantiqi `StudentsController.AddPayment` bilan BIR XIL
(`PaymentIntake` xizmati) — farqi faqat ruxsatda: kassirga o'quvchilarni tahrirlash huquqi kerak emas.
Ro'yxatlar (o'qituvchi/guruh/a'zolar/o'quvchi profili/oylik hisob) uchun alohida endpoint yo'q —
staff'ga GET har doim ochiq bo'lgani uchun mavjud endpointlar ishlatiladi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/kassa/students?q= | O'quvchini F.I.Sh yoki telefon bo'yicha qidiradi (min 2 belgi, max 30 ta). |
| POST | /api/admin/kassa/students/{id}/payments | To'lov kiritadi (kvitansiya nazorati, idempotent 6s, avans hisobi, audit, avto-xabar). 409 = kvitansiya band. |
| GET | /api/admin/kassa/my-payments?from=&to= | Kassir O'ZI kiritgan to'lovlar + jami (kim ekani TOKENDAN; standart — bugun). |

### ContractsController
`api/admin/contracts` · Ruxsat: `[Authorize]` + `[AdminPerm("contracts")]`. Shartnomalar — andozalar, `@`-tokenli .docx yuklab olish/Telegram.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/contracts/templates | Andozalar (target filtri). |
| POST | /api/admin/contracts/templates | Yangi andoza (Word yoki matn). |
| PUT | /api/admin/contracts/templates/{id} | Matnli andozani tahrirlaydi. |
| DELETE | /api/admin/contracts/templates/{id} | O'chiradi. |
| GET | /api/admin/contracts/recipients/students | O'quvchi oluvchilar. |
| GET | /api/admin/contracts/recipients/staff | Xodim oluvchilar. |
| POST | /api/admin/contracts/build | Bitta oluvchiga to'ldirib .docx yuklaydi. |
| POST | /api/admin/contracts/send | Telegram orqali yuboradi (ko'p oluvchi). |

### DashboardController
`api/admin/dashboard` · Ruxsat: `[Authorize(Roles = "admin,superadmin,staff")]`. Bosh sahifa ko'rsatkichlari + darslar monitoringi (keshlanadi).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/dashboard | Umumiy dashboard (statistika, top guruhlar, taqsimot, baholar). |
| GET | /api/admin/dashboard/today-lessons | Sanadagi darslar — davomat/baho holati. |

---

## 8. Xabarlar, avto-xabar, bot, support

### MessagesController
`api/admin/messages` · Ruxsat: `[Authorize]` + `[AdminPerm("messages")]`. "Xabarlar" — guruh chati, Telegram e'lon, Push (FCM), SMS (Eskiz) yuborish + tarixi, andozalar.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/messages/classes | Guruhlar (o'quvchi, chat soni, oxirgi xabar). |
| GET | /api/admin/messages/last-messages | Har kanal oxirgi xabar vaqti. |
| GET | /api/admin/messages/chat/{className} | Guruh chati xabarlari (`since`). |
| POST | /api/admin/messages/chat/{className} | Guruh chatiga xabar. |
| GET | /api/admin/messages/broadcasts | Telegram e'lonlari tarixi (`className`, oxirgi 100). |
| POST | /api/admin/messages/broadcast | Telegram e'lon (scope class/all/selected; qarzdorlar; mail-merge). |
| GET | /api/admin/messages/telegram/registrations | Ro'yxatdan o'tgan ota-onalar (`className`). |
| GET | /api/admin/messages/telegram/registrations/teachers | Ro'yxatdan o'tgan o'qituvchilar. |
| GET | /api/admin/messages/telegram/status | Telegram bot holati. |
| GET | /api/admin/messages/push/status | Firebase sozlanganmi. |
| GET | /api/admin/messages/push/devices | Qurilma tokenlari soni + so'nggilari. |
| GET | /api/admin/messages/push/recipients | "Tanlab" push oluvchilar. |
| GET | /api/admin/messages/push | Push e'lonlari tarixi (oxirgi 100). |
| GET | /api/admin/messages/push/{id}/confirmations | Kim tasdiqlagani. |
| POST | /api/admin/messages/push/send | Push yuborish (parents/teachers/selected; mail-merge). |
| GET | /api/admin/messages/sms/status | SMS (Eskiz) holati + sender. |
| GET | /api/admin/messages/sms | SMS partiyalari tarixi (oxirgi 100). |
| GET | /api/admin/messages/sms/{id}/logs | Partiyadagi raqamlar + yetkazish holati. |
| GET | /api/admin/messages/sms/recipients | "Tanlab" SMS oluvchilar. |
| GET | /api/admin/messages/sms/recipients/teachers | O'qituvchi SMS oluvchilar. |
| POST | /api/admin/messages/sms/send | SMS yuboradi (parents/students/teachers/selected; qarzdorlar; mail-merge). |
| GET | /api/admin/messages/templates/all | Barcha tayyor matnlar (SMS + avto-qoida shablonlari). |
| GET | /api/admin/messages/sms/templates | SMS andozalari. |
| POST | /api/admin/messages/sms/templates | Yangi andoza. |
| PUT | /api/admin/messages/sms/templates/{id} | Tahrirlaydi. |
| DELETE | /api/admin/messages/sms/templates/{id} | O'chiradi. |
| POST | /api/admin/messages/sms/lead | Bitta lidga SMS (sinov tokenlari; lid tarixiga). |

### AutoMessagesController
`api/admin/auto-messages` · Ruxsat: `[Authorize]` + `[AdminPerm("messages")]`. Yagona avto-xabar qoidalari CRUD (hodisa → SMS/Push/Telegram).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/auto-messages/triggers | Hodisa (trigger) katalogi. |
| GET | /api/admin/auto-messages | Barcha qoidalar. |
| POST | /api/admin/auto-messages | Yangi qoida (validatsiya). |
| PUT | /api/admin/auto-messages/{id} | Tahrirlaydi. |
| DELETE | /api/admin/auto-messages/{id} | O'chiradi. |

### RemindersController
`api/admin/reminders` · Ruxsat: `[Authorize]` + `[AdminPerm("settings")]` · **`[Obsolete]`** (frontend `auto-messages`ga ko'chgan, lekin API ishlaydi). Eski avto push-eslatma qoidalari.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/reminders/types | Eslatma turlari katalogi. |
| GET | /api/admin/reminders | Barcha eslatma qoidalari. |
| POST | /api/admin/reminders | Yangi qoida. |
| PUT | /api/admin/reminders/{id} | Tahrirlaydi. |
| DELETE | /api/admin/reminders/{id} | O'chiradi. |

### BotSupportController
`api/admin/messages/support` · Ruxsat: `[Authorize]` + `[AdminPerm("messages")]`. Telegram bot support suhbatlari.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/messages/support/threads | Suhbatlar (oxirgi xabar bo'yicha). |
| GET | /api/admin/messages/support/threads/{chatId}/messages | Suhbat xabarlari (o'qilgan deb belgilaydi). |
| POST | /api/admin/messages/support/threads/{chatId}/reply | Admin javobi (Telegramga). |
| GET | /api/admin/messages/support/unread | O'qilmagan xabarlar umumiy soni. |

### SupportController
`api/admin/support` · Ruxsat: `[Authorize]` + `[AdminPerm("app")]`. "Ilova → Support" — support o'qituvchilar va slotlari.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/support/teachers | Support o'qituvchilar + slot statistikasi. |
| GET | /api/admin/support/teachers/{id} | Bitta support o'qituvchi + barcha slot/darslari. |

---

## 9. Qo'ng'iroqlar (Call Center va CTI)

### CallsController
`api/admin/calls` · Ruxsat: `[Authorize]` + `[AdminPerm("calls")]` (`telephony/subscribe` — superadmin). Call Center — MoiZvonki (bulut) chiquvchi qo'ng'iroq + jurnal (transkript + AI).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/calls/config | Modul holati + provayder. |
| POST | /api/admin/calls/telephony/subscribe | (superadmin) MoiZvonki webhook obunasi. |
| POST | /api/admin/calls/originate | Chiquvchi qo'ng'iroq (studentId yoki phoneNumber). |
| POST | /api/admin/calls/telephony/sync | Tarixni provayderdan sinxronlaydi. |
| GET | /api/admin/calls | Barcha qo'ng'iroqlar (filtr, sahifalash). |
| GET | /api/admin/calls/by-number | Raqam bo'yicha guruhlangan tarix. |
| GET | /api/admin/calls/{id}/detail | Bitta qo'ng'iroq (transkript + AI). |
| POST | /api/admin/calls/{id}/transcribe | Yozuvni Azure bilan matnga. |
| POST | /api/admin/calls/{id}/analyze | Transkriptni Gemini AI tahlili. |
| GET | /api/admin/calls/student/{studentId} | O'quvchi qo'ng'iroqlari. |
| GET | /api/admin/calls/{id}/recording | Suhbat yozuvi (provayderdan proxy). |

### CtiController
`api/cti` · Ruxsat: `[Authorize]` + `[AdminPerm("calls")]`. CTI (Local Call) OPERATOR API — agentlar, tarix, click-to-call.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/cti/agents | Barcha agentlar (isOnline jonli). |
| POST | /api/cti/agents | Yangi agent (login band emasligi). |
| PUT | /api/cti/agents/{id} | Tahrirlaydi (parol bo'sh = o'zgarmaydi). |
| POST | /api/cti/agents/{id}/dial | Click-to-call: `dial` buyrug'i (WS yoki FCM+poll; raqam +998 formatga keltiriladi). |
| GET | /api/cti/calls | CTI tarixi (filtr, sahifalash). |
| GET | /api/cti/calls/grouped | Raqam bo'yicha guruhlangan. |
| GET | /api/cti/calls/{id} | Bitta qo'ng'iroq (hodisalar bilan). |
| GET | /api/cti/calls/{id}/audio | Audio yozuvi (range, path-traversal himoyasi). |
| PUT | /api/cti/calls/{id}/note | Operator izohini yangilaydi. |

### CtiMobileController — MOBIL (Android) API
`api/mobile` · Ruxsat: `[Authorize(Roles = ctiagent)]` (`auth/login` — `[AllowAnonymous]`). Agent-ilovasi shartnomasi (o'zgartirilmaydi); har agent faqat o'z yozuviga.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| POST | /api/mobile/auth/login | Login+parol → JWT, agentId, WebSocket manzili. |
| POST | /api/mobile/calls | Qo'ng'iroq metadatasi → yozuv (o'quvchiga moslash, IDEMPOTENT). |
| POST | /api/mobile/calls/{serverCallId}/audio | Audio yozuvni yuklaydi (multipart, 50MB). |
| POST | /api/mobile/calls/{serverCallId}/events | Hodisa (ringing/answered/ended) + yozuvni yangilaydi. |
| POST | /api/mobile/agents/heartbeat | Agent tirikligi (onlayn + faollik vaqti). |
| POST | /api/mobile/agents/fcm-token | FCM qurilma tokenini yangilaydi. |

### TelephonyWebhookController
`api/telephony` · Ruxsat: `[AllowAnonymous]` — auth URL'dagi maxfiy segment orqali (JWT emas). MoiZvonki webhook qabul qiluvchi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| POST | /api/telephony/moizvonki/{secret} | MoiZvonki hodisalarini qabul qiladi (start/answer/finish) + SignalR broadcast. |

---

## 10. Kamera, turniket

### CamerasController
`api/admin/cameras` · Ruxsat: `[Authorize]` + `[AdminPerm("cameras")]`. Videokuzatuv — CRUD + jonli HLS + playback (MediaMTX proxy).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/cameras | Kameralar ro'yxati. |
| POST | /api/admin/cameras | Yangi kamera (nom + RTSP majburiy). |
| PUT | /api/admin/cameras/{id} | Tahrirlaydi. |
| DELETE | /api/admin/cameras/{id} | O'chiradi (shlyuzdan ham). |
| GET | /api/admin/cameras/{id}/index.m3u8 | Jonli HLS pleylisti (proxy). |
| GET | /api/admin/cameras/{id}/{file} | HLS segmenti (.ts/.m3u8/.mp4) proxy. |
| GET | /api/admin/cameras/{id}/clip | Playback/qirqib olish — MP4. |

### StudentTurnstileController
`api/admin/students/turnstile` · Ruxsat: `[Authorize]` + `[AdminPerm("students")]`. O'quvchilar turniketi (kirgan/chiqqan vaqti).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/students/turnstile/dashboard | Kun uchun o'quvchilar turniketi. |
| POST | /api/admin/students/turnstile/sync | Qurilmadan hodisalarni tortadi + SignalR. |
| PUT | /api/admin/students/turnstile/device | O'quvchiga qurilma ID biriktiradi/uzadi. |

---

## 11. Portallar (o'quvchi va o'qituvchi ilovasi)

### StudentPortalController
`api/student` · Ruxsat: `[Authorize(Roles = "student,parent,admin")]` (mutatsiya asosan `student`). O'quvchi/ota-ona ilovasi API'si. Admin `?studentId=` bilan istalganini ko'radi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/student/me | Profil (asosiy). |
| GET | /api/student/notebook | To'liq shaxsiy daftar (profil+baholar+davomat). |
| GET | /api/student/dashboard | Bosh sahifa (profil + bugungi darslar/baholar + balans). |
| GET | /api/student/meta | Markaz meta'si (chorak/dars vaqti/sabablar). |
| GET | /api/student/school | Markaz nomi/logo/telegram kanal. |
| GET | /api/student/telegram | Telegram bot holati + ro'yxatdan o'tganmi. |
| POST | /api/student/feedback | Taklif/shikoyat (matn + rasm). |
| GET | /api/student/notifications | Bildirishnomalar (o'qilmagan soni). |
| POST | /api/student/notifications/read | Barchasini o'qilgan deb belgilaydi. |
| POST | /api/student/notifications/{id}/confirm | Bittasini tasdiqlaydi. |
| POST | /api/student/notifications/register | Push qurilma tokenini ro'yxatga. |
| DELETE | /api/student/notifications/register | Qurilma tokenini o'chiradi. |
| GET | /api/student/grades | Baholar hisoboti. |
| GET | /api/student/rating | Reyting (o'z guruhi + markaz TOP 15). |
| GET | /api/student/attendance | Davomat (chorak + kunlik). |
| GET | /api/student/finance | To'lovlar (ledger). |
| GET | /api/student/settings | Foydalanuvchi sozlamasi (til/tema/bildirishnoma). |
| PUT | /api/student/settings | Sozlamani yangilaydi. |
| PUT | /api/student/password | Parolni almashtiradi. |
| GET | /api/student/location | Saqlangan joylashuvni o'qiydi. |
| PUT | /api/student/location | Uy joylashuvini yangilaydi (GPS). |
| GET | /api/student/chat | Guruh chati xabarlari. |
| POST | /api/student/chat | Chatga xabar. |
| GET | /api/student/ai-check/status | AI tekshiruv holati (limit/premium/blok). |
| GET | /api/student/ai-check/history | AI tekshiruv tarixi. |
| GET | /api/student/ai-check/history/{id} | Bitta yozuv (to'liq). |
| POST | /api/student/ai-check/writing | Writing — matn → Gemini tahlili. |
| POST | /api/student/ai-check/speaking | Speaking — ovoz → Azure+Gemini. |
| GET | /api/student/subjects-progress | Fanlar bo'yicha progress. |
| GET | /api/student/subjects-progress/{subjectId} | Bitta fan darslari. |
| GET | /api/student/curriculum | O'quv dasturi yo'l-xaritasi. |
| GET | /api/student/curriculum/item/{id} | Dars kontenti (o'tilgach ochiladi). |
| GET | /api/student/curriculum/{courseId}/progress | Kursda o'tilgan bandlar. |
| POST | /api/student/curriculum/progress | Dars progresini yangilaydi. |
| GET | /api/student/grading | Baholash statistikasi (mezonlar × oy). |
| GET | /api/student/support | Support: bo'sh slotli o'qituvchilar + bronlar. |
| POST | /api/student/support/slots/{id}/book | Bo'sh slotni bron qiladi (atomik). |
| POST | /api/student/support/slots/{id}/cancel | Bronni bekor qiladi. |

### TeacherPortalController
`api/teacher` · Ruxsat: `[Authorize(Roles = "teacher")]` (amal ichida `Teacher.Permissions` + guruh egaligi). O'qituvchi ilovasi API'si.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/teacher/me | O'qituvchi profili. |
| GET | /api/teacher/meta | Markaz meta'si. |
| GET | /api/teacher/school | Markaz nomi/logo. |
| GET | /api/teacher/notifications | Bildirishnomalar. |
| POST | /api/teacher/notifications/read | Barchasini o'qilgan. |
| POST | /api/teacher/notifications/{id}/confirm | Bittasini tasdiqlaydi. |
| POST | /api/teacher/notifications/register | Push token ro'yxatga. |
| DELETE | /api/teacher/notifications/register | Tokenni o'chiradi. |
| GET | /api/teacher/classes | Dars beradigan guruhlar (kurslari bilan). |
| GET | /api/teacher/salary | Maosh ledgeri (faqat o'ziniki). |
| GET | /api/teacher/journal/group | Guruh oylik jurnali. |
| PUT | /api/teacher/journal | Jurnal katagi (baho/davomat, siyosat). |
| DELETE | /api/teacher/journal | Katakni tozalaydi. |
| POST | /api/teacher/journal/bulk-attendance | Bir darsga ommaviy davomat. |
| GET | /api/teacher/grading/group/{groupId}/board | Guruh baholash grid'i. |
| POST | /api/teacher/grading/grade | Mezon bahosini saqlaydi. |
| POST | /api/teacher/grading/grade/bulk | Sanada bir mezon bo'yicha barchani. |
| GET | /api/teacher/curriculum/group/{groupId} | Guruh sillabus o'tilishi + prognoz. |
| POST | /api/teacher/curriculum/group/{groupId}/cover | Bandni o'tilgan/o'tilmagan. |
| POST | /api/teacher/curriculum/group/{groupId}/revision | Takrorlash darsi. |
| GET | /api/teacher/chat/last-messages | Har kanal oxirgi xabar vaqti. |
| GET | /api/teacher/chat/classes | Chat kanallari. |
| GET | /api/teacher/chat/{className} | Kanal xabarlari. |
| POST | /api/teacher/chat/{className} | Kanalga xabar. |
| POST | /api/teacher/feedback | Taklif/shikoyat (matn + rasm). |
| GET | /api/teacher/support/slots | O'z bo'sh slotlari (oy bo'yicha). |
| POST | /api/teacher/support/slots | Bo'sh vaqt bloki (bo'lish + takror). |
| DELETE | /api/teacher/support/slots/{id} | Slotni o'chiradi. |
| POST | /api/teacher/support/slots/{id}/complete | Bron darsini yopadi (mavzu+izoh). |

---

## 12. Ommaviy (autentifikatsiyasiz) endpointlar

### PublicTestController
`api/public/test` (+ ba'zi absolut yo'llar) · Ruxsat: `[AllowAnonymous]`. Ommaviy daraja testi + brending/PWA (topshiruv → yangi lid).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/public/brand | Ommaviy brending (nom/logo/telefon). |
| GET | /api/public/push-config | Web/PWA push konfiguratsiyasi (Firebase + VAPID). |
| GET | /api/public/manifest.webmanifest | Dinamik PWA manifest. |
| GET | /api/public/test/{slug} | Slug bo'yicha faol test (javobsiz). |
| GET | /api/public/test/invite/{token} | Havola bo'yicha test (lid oldindan to'ldirilgan). |
| POST | /api/public/test/invite/{token}/submit | Havola orqali topshirish (lidga bog'lanadi). |
| POST | /api/public/test/{slug}/submit | Testni topshirish (yangi lid yaratiladi). |

### PublicLandingController
`api/public/landing-lead` · Ruxsat: `[AllowAnonymous]` + `[EnableRateLimiting("public-lead")]`. Apex domen landing formasi (Source="sayt").

| Metod | Yo'l | Vazifasi |
|---|---|---|
| POST | /api/public/landing-lead | Saytdan lid (ism+telefon+yo'nalish) — Telegram + avto-xabar. |

### PublicCareerController
`api/career` · Ruxsat: `[AllowAnonymous]` + Telegram imzosi. Karyera **Mini App**i (`/vakansiya`) uchun.
Autentifikatsiya `X-Telegram-Init-Data` sarlavhasi orqali (KARYERA boti tokeni bilan tekshiriladi —
`TelegramInitData`); imzo bo'lmasa faqat ko'rish rejimi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/career/bootstrap | Boshlang'ich holat: biz haqimizda + faol vakansiyalar + o'z arizalari + bosqichlar. |
| POST | /api/career/cv | CV yuklash — FAQAT PDF, 10MB (`public-lead` limiti). |
| POST | /api/career/apply | Vakansiyaga ariza (`public-lead` limiti; bitta vakansiyaga bitta ariza). |

### SmsCallbackController
`api/sms` · Ruxsat: `[AllowAnonymous]` (Eskiz serveri chaqiradi). SMS yetkazish holati webhook'i.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| POST | /api/sms/callback | Eskiz yetkazish holati (`request_id` → SmsLog; doim 200). |

---

## 13. Umumiy fayl yuklash va arxiv

### UploadsController
`api/admin/uploads` · Ruxsat: `[Authorize(Roles = "admin,superadmin,staff")]`. Umumiy fayl yuklash (rasm/PDF → URL).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| POST | /api/admin/uploads | Fayl yuklaydi (`/uploads/...` URL; maks 20MB). |

### CareerController
`api/admin/career` · Ruxsat: `[Authorize]` + `[AdminPerm("vacancies")]`. Karyera (ishga qabul) —
"Boshqaruv → Vakansiyalar": vakansiyalar, nomzod arizalari va Mini App'ning "Biz haqimizda" bloki.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/career/stages | Ariza bosqichlari katalogi. |
| GET/PUT | /api/admin/career/about | "Biz haqimizda" (Mini App birinchi ekrani). |
| GET | /api/admin/career/vacancies | Vakansiyalar (`status=active\|archived`) + ariza soni. |
| POST/PUT | /api/admin/career/vacancies[/{id}] | Yaratish / tahrirlash. |
| POST | /api/admin/career/vacancies/{id}/archive | Arxivlash (ilovada ko'rinmaydi). |
| POST | /api/admin/career/vacancies/{id}/restore | Arxivdan tiklash. |
| DELETE | /api/admin/career/vacancies/{id} | O'chirish — faqat ariza tushmagan bo'lsa. |
| GET | /api/admin/career/applications | Arizalar (`status`/`vacancyId`/`q` filtri). |
| GET | /api/admin/career/applications/{id} | Bitta ariza + bosqichlar tarixi. |
| POST | /api/admin/career/applications/{id}/status | Bosqichni o'zgartiradi (nomzodga botda xabar). |
| PUT | /api/admin/career/applications/{id}/note | Ichki izoh (nomzodga ko'rinmaydi). |
| DELETE | /api/admin/career/applications/{id} | Arizani tarixi bilan o'chiradi. |
| GET | /api/admin/career/stats | Bosqich bo'yicha jamlanma. |

### ArchiveController
`api/admin/archive` · Ruxsat: `[Authorize]` + `[AdminPerm("settings")]`. Arxiv — o'chirilgan yozuvlar JSON suratlari (ro'yxat, tiklash, butunlay o'chirish).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/archive | Arxiv ro'yxati (`type` filtri). |
| GET | /api/admin/archive/counts | Tur bo'yicha soni (lead/student/teacher/staff/group/finance). |
| POST | /api/admin/archive/{id}/restore | Tiklaydi (moliyada balans qaytadi). |
| DELETE | /api/admin/archive/{id} | Butunlay o'chiradi. |

---

## 14. AI modullari (admin)

### AiCheckController
`api/admin/ai-check` · Ruxsat: `[Authorize]` + `[AdminPerm("app")]`. AI tekshiruv (Speaking/Writing) admin boshqaruvi.

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/ai-check/overview | O'quvchilar: speaking/writing soni, bugungi limit/premium/blok. |
| GET | /api/admin/ai-check/settings | Global standart kunlik limit. |
| PUT | /api/admin/ai-check/settings | Standart limitni saqlaydi. |
| PUT | /api/admin/ai-check/access/{studentId} | O'quvchiga limit/premium/blok (upsert). |
| GET | /api/admin/ai-check/history/{studentId} | O'quvchi AI tekshiruv tarixi. |
| GET | /api/admin/ai-check/item/{id} | Bitta yozuv (matn/ovoz/tahlil). |

### AiAnalysisController
`api/admin/ai-analysis` · Ruxsat: `[Authorize(Roles = "admin,superadmin,staff")]`. Markazning kunlik AI tahlili (bosh sahifa).

| Metod | Yo'l | Vazifasi |
|---|---|---|
| GET | /api/admin/ai-analysis/center | Bugungi (yoki so'nggi) markaz AI tahlili. |
| GET | /api/admin/ai-analysis/center/history | Tahlillar tarixi. |
| POST | /api/admin/ai-analysis/center/run | Qo'lda tahlil (`force=true` — superadmin). |

---

## 15. Real-time (SignalR / WebSocket)

REST emas, lekin API sirtining bir qismi:

| Yo'l | Turi | Vazifasi |
|---|---|---|
| /hubs/chat | SignalR | Guruh chati real-time xabarlari. |
| /hubs/live | SignalR | Jonli hodisalar (turniket/qo'ng'iroq holati broadcast). |
| /ws?token=... | Raw WebSocket | CTI agent-telefon ulanishi (SignalR EMAS; `dial` buyrug'i). |
| /api/health | GET | Health-check (`{status:"healthy"}`). |
