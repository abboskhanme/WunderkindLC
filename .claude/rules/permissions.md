---
description: Xodim (staff) ruxsatlari — BO'LIM va SAHIFA (page) kalitlari, meros qoidasi, matritsa UI va yangi sahifa qo'shish tartibi.
paths:
  - "IntellectCRM.Application/Services/PermissionRules.cs"
  - "IntellectCRM.Server/Controllers/AdminPermAttribute.cs"
  - "IntellectCRM.Client/src/lib/permissions.ts"
  - "IntellectCRM.Client/src/config/constants.ts"
  - "IntellectCRM.Client/src/config/navigation.ts"
  - "IntellectCRM.Client/src/components/staff/PermMatrix.tsx"
---

# Ruxsatlar (rollar) qoidalari

"Xodimlar va rollar" (`/admin/boshqaruv/staff`) da superadmin har bir xodimga **bo'lim** yoki
**alohida SAHIFA** beradi. Migratsiya KERAK EMAS — ruxsatlar `AppUser.Permissions` (text[]) da
oddiy satrlar, format kengaytirildi xolos.

## 1. Uchta token turi

| Token | Ma'nosi |
|---|---|
| `students` | BUTUN bo'lim: barcha sahifalari, barcha amallari (eski yozuvlar shunday) |
| `students:edit` | Butun bo'lim, faqat "Tahrir" amali |
| `students.turnstile` | FAQAT "Turniket" sahifasi, barcha amallari |
| `students.turnstile:edit` | Faqat o'sha sahifa, faqat tahrir |

Ajratkichlar: `.` — bo'lim/sahifa, `:` — amal (`create` \| `edit` \| `delete`; `view` alohida
yozilmaydi — biror amal bo'lsa ko'rish ochiq).

⚠️ **Chuqurlik BITTA daraja**: `bolim.sahifa`. `bolim.sahifa.tab` qo'llanmaydi — `ParentOf` doim
BIRINCHI nuqtagacha qismni oladi.

## 2. MEROS — eng muhim qoida

```
BO'LIM  →  SAHIFA   :  o'qish HA, yozish HA      (pastga — to'liq)
SAHIFA  →  BO'LIM   :  o'qish HA, yozish YO'Q    (yuqoriga — faqat ko'rish)
```

- **Pastga to'liq meros** — shu sababdan **mavjud xodimlar ruxsati o'zgarmadi**: bazadagi
  `"students"` tokeni yangi `students.turnstile` darvozasidan ham o'tadi.
- **Yuqoriga faqat o'qish** — sahifa o'z ma'lumotini ko'pincha BO'LIM controlleridan oladi
  (`GET`), shuning uchun `HasSection` uni "shu bo'limda ishlaydi" deb hisoblaydi. Aks holda
  `ReadRequiresPerm` qo'yilgan controllerlar sahifa ruxsatli xodimga 403 berardi.
- **Yuqoriga YOZISH ATAYIN BERILMAYDI**: aks holda "Turniket" operatori
  `POST /api/admin/students` bilan o'quvchi yaratib yuborardi — ya'ni tor ruxsat berishning
  ma'nosi qolmasdi.

⚠️ Shu sababli **NOZIK o'qish darvozalari sahifa kaliti bilan qo'yiladi**. Masalan
`StudentsController.RedactDocs` passport skanini `HasSectionAccess(User, "students.list")` bo'yicha
tekshiradi — `"students"` bo'lsa turniket operatoriga hujjatlar ochilib ketardi.

Qoidaning O'ZI ikki joyda, **AYNAN bir xil**:

| Qatlam | Fayl | Funksiyalar |
|---|---|---|
| Server | `Application/Services/PermissionRules.cs` | `HasSection` · `CanWrite` · `HasFullSection` · `ParentOf` |
| Klient | `Client/src/lib/permissions.ts` | `can` · `canAny` · `parentOf` |

Ikkalasi ham testlangan: `PermissionRulesTests` / `PermissionRulesPageTests`, `permissions.test.ts`.

## 3. YAGONA KATALOG — `adminPermissions`

`Client/src/config/constants.ts`. Bo'lim → `pages[]`. Matritsa (`PermMatrix`), menyu
(`navigation.ts`), marshrut darvozasi (`App.tsx`) va serverdagi `[AdminPerm]` AYNAN shu
kalitlardan foydalanadi.

Yordamchilar: `permPagesOf(section)` · `permLabel(key)` · `settingsPagePerm(urlSegment)`.

### 3.1. YANGI SAHIFA QO'SHSANGIZ — 4 qadam

1. `constants.ts` → tegishli bo'limning `pages` ro'yxatiga `{ key: 'bolim.sahifa', label }`;
2. `navigation.ts` → menyu bandiga `perm: 'bolim.sahifa'`;
3. `App.tsx` → `<RequirePerm perm="bolim.sahifa">`;
4. **server**: sahifaning O'Z controlleri bo'lsa `[AdminPerm("bolim.sahifa")]`; controller
   bo'limga umumiy bo'lsa — faqat o'sha sahifaga tegishli METODLARGA sahifa kalitini qo'ying.

⚠️ Qadamlardan biri unutilsa **test qizaradi** — `PermissionCatalogTests`:
serverdagi har bir `[AdminPerm]` kaliti, `App.tsx` dagi har bir `RequirePerm` va
`navigation.ts` dagi har bir `perm`/`permAny` katalogda bo'lishi SHART.

## 4. `[AdminPerm]` — server darvozasi

```csharp
[AdminPerm("students.list")]                              // bitta kalit
[AdminPerm("students.list", "students.notes")]            // BIRORTASI yetadi (permAny)
[AdminPerm("app.aiCheck", ReadRequiresPerm = true)]       // GET ham darvozalanadi
```

- **METOD darajasidagi atribut SINF darajasidagisini BEKOR QILADI** (`IsOverriddenAtMethod`).
  ASP.NET Core ikkala filtrni ham ishga tushiradi, ya'ni bu tekshiruvsiz metodga TORROQ kalit
  qo'yib bo'lmasdi — sinfdagi keng kalit baribir talab qilinardi.
- **Bir nechta kalit** kerak bo'ladigan joy: bitta endpoint IKKI sahifadan ishlatiladi.
  Misol: o'quvchi izohlari — profil sahifasida ham, "Izohlarga javoblar" ro'yxatida ham
  yoziladi → `[AdminPerm("students.list", "students.notes")]`.

### 4.1. Bo'limga UMUMIY controllerlar

Ikkitasi bir nechta mustaqil sahifaga xizmat qiladi, shuning uchun **sinf darajasi = o'qish**,
**metod darajasi = sahifa bo'yicha yozish**:

| Controller | Sinf (o'qish) | Metodlar (yozish) |
|---|---|---|
| `SettingsController` | `settings` | `settings.school` · `settings.channels` · `settings.backup` · `settings.apk` · `settings.azure-speech` · `settings.gemini` · `settings.check` · `settings.turnstile` · `settings.cameras` · `settings.reasons` |
| `InstagramController` | `marketing` (RRP) | `marketing.settings` · `marketing.inbox` · `marketing.rules` · `marketing.knowledge` |

Ya'ni "Zaxira nusxa" berilgan xodim "Markaz ma'lumotlari"ni o'zgartira olmaydi.

## 5. Matritsa UI (`PermMatrix`)

Bo'lim qatori — **master**, ostida sahifa qatorlari (ochiladi/yopiladi).

| Amal | Nima bo'ladi |
|---|---|
| Bo'lim katagi YOQILDI | `"bolim[:amal]"` yoziladi, o'sha amal bo'yicha sahifa tokenlari TOZALANADI (keraksiz) |
| Bo'lim "Ko'rish" O'CHIRILDI | bo'lim ham, uning BARCHA sahifa tokenlari ham o'chadi |
| Sahifa katagi bosildi (bo'lim ochiq turganda) | bo'lim tokeni avval sahifalarga **YOYILADI** (`expandSection`), keyin bosilgani almashadi |
| Barcha sahifalar bir xil to'plamga keldi | bitta bo'lim tokeniga **IXCHAMLANADI** (`collapsePages`) |

⚠️ **Yoyish ATAYIN**: aks holda "bo'lim ochiq, faqat bitta sahifasi yopiq" holatini yasash uchun
avval butun bo'limni o'chirib, keyin sahifalarni birma-bir yoqish kerak bo'lardi.

Token ro'yxati doim eng IXCHAM ko'rinishda saqlanadi — bir vaqtda bo'lim va uning sahifa tokeni
turmaydi.

## 6. Menyu va marshrutlar

- **Guruh bandida `perm` bo'lsa** — u yuqoriga meros bilan ishlaydi: `students.turnstile` bor
  xodim "O'quvchilar" guruhini ko'radi. Bolalari qolmagan guruhni `Sidebar.filterNav` o'zi
  yashiradi.
- **`permAny`** — band bir nechta sahifaga olib boradigan joy ("Formalar":
  `leads.forms` \| `schedule.levelTests`; "O'qituvchilar": to'rtta sahifa).
- **KIRISH NUQTALARI** — band bitta bo'lib, ichida CardTabs bilan bir nechta sahifa bo'lsa,
  `/` manzili foydalanuvchiga OCHIQ sahifaga yo'naltirishi kerak. Aks holda faqat "Davomati"
  ruxsati bor xodim menyudan kelib "ruxsatingiz yo'q" kartasiga tushib qolardi:
  - `FormsEntry` (`/admin/forms`), `TeachersEntry` (`/admin/teachers`),
  - `SettingsEntry` (`/admin/settings/:section` — ruxsat SEGMENTGA qarab tanlanadi).

## 7. Kalitlar xaritasi (bo'lim → sahifalar)

| Bo'lim | Sahifalar | Controller(lar) |
|---|---|---|
| `marketing` | `.dashboard` `.inbox` `.rules` `.knowledge` `.analytics` `.settings` | `InstagramController` (metod darajasida) |
| `leads` | `.list` `.stats` `.forms` | `LeadsController` · `LeadStagesController` · `LeadFormsController` |
| `students` | `.list` `.notes` `.attendance` `.turnstile` `.face` | `StudentsController` · `StudentAttendanceController` · `StudentTurnstileController` · `AdminFaceController` · `CertificatesController` |
| `teachers` | `.list` `.attendance` `.substitutions` | `TeachersController` · `TeacherAttendanceController` · `SubstituteTeachersController` |
| `schedule` | `.courses` `.analytics` `.curricula` `.grading` `.levelTests` | `SubjectsController` · `CourseAnalyticsController` · `CurriculumController` · `GradingController` · `LevelTestsController` |
| `classes` | `.list` `.rooms` `.testResults` | `ClassesController` · `JournalController` · `RoomsController` · `TestResultsController` |
| `messages` | `.broadcast` `.chat` `.support` | `MessagesController` (+ `chat` metodi) · `AutoMessagesController` · `BotSupportController` |
| `app` | `.aiCheck` `.support` `.locations` `.parents` `.teachers` | `AiCheckController` · `SupportController` · `LocationsController` · `ParentsController` · `AppTeachersController` |
| `finance` | `.main` `.bonus` | `FinanceController` · `RetentionBonusController` |
| `calls` | `.cloud` `.local` | `CallsController` · `Cti/CtiController` |
| `tasks` | `.board` `.dashboard` `.settings` | `WorkTasksController` (metod darajasida) |
| `settings` | `.school` `.landing` `.districts` `.reasons` `.channels` `.backup` `.apk` `.azure-speech` `.gemini` `.check` `.turnstile` `.cameras` `.posthog` `.archive` | `SettingsController` (metod darajasida) · `LandingCmsController` · `DistrictsController` · `ActionReasonsController` · `LeadSourcesController` · `ArchiveController` |

Sahifasiz bo'limlar (bittasi = bitta sahifa): `contacts` · `teacherReports` · `contracts` ·
`books` · `kassa` · `audit` · `staff` · `feedback` · `cameras` · `vacancies` · `ai` ·
`retentionBonus`.

⚠️ `settings.posthog` — sozlamasi klientda, serverda alohida endpoint YO'Q (shuning uchun
jadvalda controller ko'rsatilmagan).

## 8. Nozik kalitlar — `can()` YARAMAYDIGAN joylar

- **`retentionBonus`** (aktivlashtirishdagi «Bonus hisoblansin» ptichkasi) —
  `useSuperOrGranted` / `AdminPermAttribute.IsSuperAdminOrGranted`: oddiy `admin` roli ham
  KO'RMAYDI, faqat superadmin va ANIQ ruxsat berilgan xodim. Sahifa kalitlari bu yerda
  ishlatilmaydi (bo'lim ham, sahifa ham emas — bitta yalang kalit).
- **`HasFullAccess` / `HasFullSection`** — faqat YALANG kalit (barcha amallar): parol eksporti,
  xodim ruxsatini o'zgartirish. Bo'limdan sahifaga meros bu yerda ham ishlaydi
  (`students` → `students.list`), lekin sahifadan bo'limga YO'Q.

## 9. Nima O'ZGARDI (2026-08-19)

Ilgari ruxsat faqat BO'LIM darajasida edi va yangi sahifa qo'shilganda uni alohida berish uchun
yangi top-level kalit yasash kerak bo'lardi (shu sababdan `contacts`, `kassa`, `audit`,
`teacherReports` alohida kalit bo'lib qolgan). Endi sahifa kaliti bo'lim ichida yashaydi.

**Ruxsat lineyasi o'zgargan yagona joy** — «Bonus hisoboti» (`/admin/students/bonus`). Ilgari uni
KO'RISH `students`, YOZISH esa `finance` talab qilardi — ya'ni sahifani ochgan odam undan
foydalana olmasdi. Endi ikkalasi ham **`finance.bonus`** (menyudagi joyi o'zgarmadi — o'quvchilar
guruhida).

- moliya xodimi (`finance`) — merosi bilan HAMMASINI oladi (ilgari faqat yozardi, endi ko'radi ham);
- faqat `students` bo'lgan xodim bu sahifani endi KO'RMAYDI — lekin u ilgari ham hech narsa
  qila olmasdi (yozish `finance` da edi), ya'ni ishlaydigan funksiya yo'qolmadi.

⚠️ ATAYIN `students.bonus` deb NOMLANMADI: bo'limdan sahifaga meros tufayli u holda BARCHA
`students` ruxsatli xodimlar bonus bera oladigan bo'lib qolardi (ruxsat KENGAYIB ketardi).

Qolgan hamma joyda eski tokenlar AYNAN eskicha ishlaydi.
