---
description: O'rinbosar o'qituvchilar — vaqtincha tayinlov, nol yig'indili pul modeli, o'rinbosarning guruhga kirish huquqi (sana bo'yicha) va audit.
paths:
  - "WunderkindLC.Application/Services/SubstituteTeacherService.cs"
  - "WunderkindLC.Application/Services/SalaryLedger.cs"
  - "WunderkindLC.Application/Services/SalaryJournalStats.cs"
  - "WunderkindLC.Server/Controllers/SubstituteTeachersController.cs"
  - "WunderkindLC.Server/Controllers/TeacherPortalController.cs"
---

# O'rinbosar o'qituvchilar qoidalari

Asosiy o'qituvchi kasal/ta'tilda bo'lganda guruhga VAQTINCHA boshqa o'qituvchi biriktiriladi.
Modul: admin → **O'qituvchilar → O'rinbosarlar** (`/api/admin/substitute-teachers`, ruxsat
`teachers`), o'qituvchi ilovasi → guruhlar ro'yxati + `GET /api/teacher/substitutions`.
Entity — `SubstituteTeacherAssignment`. **Migratsiya kerak emas** (modul mavjud jadval ustida).

## 0. YAGONA HAQIQAT MANBAI — `SelectedDates`

Tayinlov ikki yo'l bilan yaratiladi: kalendardan **sanalar tanlab** yoki `Date`..`EndDate`
**oralig'i** bilan. Yaratishda ikkinchisi ham DARHOL guruhning HAQIQIY dars kunlariga
yoyiladi va `SelectedDates` ga yoziladi.

Sabab: **to'rt narsa AYNAN bir xil sanalar to'plamiga tayanishi shart** — dars soni, pul,
kirish huquqi va kesishuv tekshiruvi.

⚠️ Ilgari ular uch xil edi: kirish huquqi ORALIQ bo'yicha berilardi (5 va 20-avgust tanlansa
oradagi 14 kun ham ochilardi), dars soni esa oraliqdan yaratilganda har doim **2** chiqardi.

`Date`/`EndDate` endi faqat **indeks uchun** saqlanadi (SQL'da qo'pol oraliq filtri) — qaror
har doim `SelectedDates` bo'yicha, xotirada (`CoversDate`). Massiv ustun ichidan qidirish
PostgreSQL'da ishlaydi-yu SQLite testlarida ishlamaydi (audit qoidasidagi `ILike` bilan bir xil hol).

## 1. PUL — **NOL YIG'INDILI**

> **Asosiy o'qituvchidan ayirilgan summa AYNAN o'rinbosarga to'lanadi. Markaz uchun neytral.**

```
perLesson(guruh, oy) = guruhning shu oydagi maosh ULUSHI / oyning HAQIQIY dars soni
o'rinbosarga        = perLesson × o'rinbosar o'tgan darslar
asosiydan ayiriladi = AYNAN o'sha summa
```

**Hisoblagich BITTA:** `SubstituteTeacherService.PerLesson` (sof funksiya) + yuklovchisi
`PerLessonBatchAsync`. Ikkala tomon ham AYNAN shu jadvaldan (`(guruh, oy) → narx`) o'qiydi —
nol yig'indililik shu bilan **strukturaviy** kafolatlanadi, "ikkita formulani sinxron ushlab
turish" bilan emas.

⚠️ **NEGA MUHIM:** ilgari TO'RT joyda TO'RT formula bor edi — o'rinbosarga
`MonthlyFee × o'quvchi × foiz` ("HISOBLANGAN") dan to'lanar, asosiydan esa `yig'ilgan × foiz`
dan ushlanardi. Yig'ilmagan qarz bor oyda o'rinbosarga KO'PROQ to'lanib, asosiydan KAMROQ
ushlanardi — farqni markaz to'lardi va buni hech bir hisobot ko'rsatmasdi.

### Maxraj — oyning HAQIQIY dars soni

`JournalService.EffectiveLessonDatesInMonth` — ya'ni **ko'chirilgan darslar bilan**
(`LessonReschedules`) va guruh chegaralari (`StartDate`, `EndDate`/`ArchivedAt` dan
ERTAROG'I) bilan.

⚠️ Ilgari bu fayl hafta kuni mantig'ini QO'LDA takrorlar (`((int)DayOfWeek + 6) % 7`) va
ko'chirishlarni bilmasdi, `SalaryJournalStats` esa bilardi → bitta oyning dars soni ikki xil
chiqardi. Maxraj ba'zi joyda `"-28"` bilan kesilar va bitta dars narxi sun'iy ravishda KATTA
chiqib, asosiy o'qituvchidan ortiqcha ayirilardi.

⚠️ Guruh **arxivlangandan keyingi** kunlar dars emas: `SalaryLedger.LessonEnd(EndDate, ArchivedAt)`.
Ilgari hisoblagichga `new Group { … }` bilan QISMAN to'ldirilgan nusxa uzatilar va bu maydonlar
tashlab ketilardi — yopilgan guruhda ham dars sanalib, pul to'lanaverardi.

## 2. UCH MAOSH REJIMI — hovuz qayerdan olinadi

| Rejim | Qachon | Hovuz (`GroupPool`) |
|---|---|---|
| `group-percent` | `Group.TeacherSalaryMode == "percent"` | shu guruhga shu oyda **YIG'ILGAN** pul × `Group.TeacherSalaryPercent` |
| `group-fixed` | `Group.TeacherSalaryMode == "fixed"` | `Group.TeacherSalaryFixed` (o'quvchi/tushum qatnashmaydi) |
| `legacy-percent` | guruh sozlanmagan, `Teacher.SalaryMode == "percent"` | yig'ilgan pul × `Teacher.SalaryPercent` |
| `legacy-fixed` | guruh sozlanmagan, o'qituvchi qat'iy oyliqda | `Teacher.Salary × (shu guruh darslari ÷ o'qituvchining BARCHA guruhlaridagi darslar)` |

⚠️ **`legacy-fixed` NEGA shunday:** qat'iy oylik bitta guruhga tegishli emas, u hamma guruhlar
uchun to'lanadi. Shuning uchun bitta dars narxi = `oylik ÷ hamma darslar` — bu `SalaryLedger`
dagi legacy-qat'iy **jurnal jarimasi** formulasi bilan AYNAN bir xil (ikki joyda ikki xil
bo'lmasin).

⚠️ **YIG'ILGAN pul, HISOBLANGAN emas.** Manba — `SalaryLedger.CollectedForGroupsAsync`
(taqsimot: teglangan to'lov 100% guruhga, teglanmagani `MonthlyFee` nisbatida — butun
tizimdagi bilan bir xil konvensiya). Bu metod AYNAN shu sabab ommaviy: o'rinbosarning maosh
varaqasini hisoblashda u **O'ZI O'QITMAYDIGAN (begona) guruhning** yig'ilgan puli va maosh
rejimi kerak bo'ladi.

⚠️ **PUL KELMAGAN OYDA HAQ 0** — bu xato emas, `billing.md` dagi asosiy qoidaning davomi
("pul kelmaguncha maosh hisoblanmaydi"). Pul kelgach maosh varaqasida o'zi paydo bo'ladi.
Modal bu holatni `Warning` bilan ochiq yozadi.

### `SalaryLedger` da qanday qo'llanadi

```
o'rinbosarga:  MonthSalaryDto.SubstituteFee       → baseExpected ga QO'SHILADI
asosiydan:     MonthSalaryDto.SubstituteDeduction → guruh ulushidan (yoki oy bazasidan) AYIRILADI
```

⚠️ **USHLANMA UCH REJIMDA HAM QO'LLANADI.** Per-guruh va legacy-foizda u guruh ulushidan
(`contribution`) ayriladi; legacy-QAT'IYda guruh ulushi umuman yo'q (`contribution == 0`),
shuning uchun u **yakuniy `baseExpected` dan** ayriladi. Natija hech qachon manfiy bo'lmaydi
(`Math.Max(0, …)`).

> Ilgari `if (contribution > 0)` sharti bor edi: legacy-qat'iyda u HECH QACHON bajarilmasdi,
> legacy-foizda esa baza guruhlar ulushidan emas, oyning JAMI yig'ilganidan chiqardi. Ya'ni
> ushlanma hisoblanar, ekranga "ushlandi" deb yuborilar, lekin maoshdan **AYRILMASDI** —
> markaz bekorga to'lardi va hisobot kamaytirilgan deb ko'rsatardi.

### Jurnal jarimasi bilan chegara

- **Maxraj:** legacy-qat'iy jurnal jarimasi `ownWorkBase / plannedTotal` dan hisoblanadi, bunda
  `ownWorkBase` — o'rinbosarlik haqi QO'SHILMAGAN baza. O'rinbosarlik haqi BOSHQA guruhlarda
  o'tilgan darslar uchun; uni o'z guruhidagi "belgilanmagan dars" jarimasi maxrajiga qo'shish
  jarimani sun'iy kattalashtirardi.
- **Sanoq:** o'rinbosar qamragan sanalar asosiy o'qituvchining `Planned`/`Missed` sanog'idan
  CHIQARILADI (`SalaryJournalStats.BuildAsync(excludeDates:)`).
  ⚠️ Aks holda **bitta dars uchun IKKI marta jarima**: dars jurnalda belgilanmagani uchun
  `contribution × Missed/Planned` ushlanar, USTIGA o'rinbosarlik ushlanmasi ham ayrilardi.

## 3. KIRISH HUQUQI — o'rinbosar NIMANI, QAYSI SANADA ocha oladi

Darvoza BITTA: `TeacherPortalController.ResolveOwnedGroup` → `(Me, Group, Owns, IsSubstitute)`.
Har bir endpoint o'rinbosarga nima ochilishini **o'zi** hal qiladi.

⚠️ Ilgari `ResolveOwnedGroup` o'rinbosarlikni UMUMAN bilmasdi: o'rinbosar guruhni ro'yxatda
(`/classes`) ko'rar, bosardi va **403** olardi — modulning asosiy stsenariysi ishlamasdi.

### Ikki oyna (nomlangan konstantalar, `SubstituteTeacherService`)

| Konstanta | Qiymat | Nima |
|---|---|---|
| `EditWindowDays` | 3 | dars kunidan keyin **YOZISH** (tuzatish) mumkin bo'lgan kunlar |
| `UpcomingDays` | 7 | guruh ro'yxatda oldindan **KO'RINADIGAN** kunlar |

- **YOZISH** (`CanWriteOn` / `CanSubstituteWriteAsync`): tayinlov AYNAN shu kunni qamraydi
  **VA** `0 ≤ bugun − sana ≤ 3`.
  ⚠️ NEGA 0 emas: dars o'tib kechqurun jurnalni to'ldirish odatiy hol; tayinlov tugagan zahoti
  yozuv yopilsa, o'rinbosar o'z xatosini tuzata olmasdi va tuzatish **dars o'tmagan** asosiy
  o'qituvchi zimmasiga tushardi.
  ⚠️ NEGA cheksiz emas: ilgari tekshiruvga SANA umuman uzatilmas, ichkarida `AppClock.Today`
  o'qilardi — tayinlangan kuni o'rinbosar guruhning **O'TGAN ISTALGAN** kunidagi baho/davomatini
  o'zgartira olardi.
- **O'QISH** (`CanSubstituteReadAsync` / `SubstituteGroupIdsAsync`): tayinlov `[bugun−3, bugun+7]`
  oynasidagi biror kunni qamrasa. Guruhlar RO'YXATI ham AYNAN shu qoidada — ro'yxat va darvoza
  ikki xil ishlamasin.

### Endpointlar bo'yicha QAROR

| Endpoint | O'rinbosarga | NEGA |
|---|---|---|
| `GET /classes` (guruhlar ro'yxati) | ✅ ko'rish oynasida | ertangi darsiga tayyorlansin |
| `GET journal/group` | ✅ **o'qish** | jurnalni ko'rmasdan davomat qo'yib bo'lmaydi |
| `PUT/DELETE journal` (katak) | ✅ **yozish**, `req.Date` bo'yicha | baho/davomat aynan uning ishi |
| `POST journal/bulk-attendance` | ✅ **yozish**, `req.Date` bo'yicha | o'sha darsning davomati |
| `POST/DELETE journal/reschedule` | ❌ | dars ko'chirish guruh JADVALINI va oyning dars sonini (maosh maxraji) o'zgartiradi — guruh egasining qarori |
| `GET grading/group/{id}/board` | ✅ **o'qish** | baho qo'yish uchun mezonlar taxtasi kerak |
| `POST grading/grade`, `grade/bulk` | ✅ **yozish**, `req.Date` bo'yicha | o'zi o'tgan darsning bahosi |
| `GET curriculum/group/{id}` | ✅ **o'qish** | "guruh qayerga yetgan, bugun qaysi mavzu" — dars o'tish uchun ZARUR |
| `POST curriculum/.../cover`, `/revision` | ❌ | o'quv dasturi o'tilishini belgilash kurs prognozini o'zgartiradi — bir kunlik o'rinbosarning ishi emas |
| `test-results/*` (`OwnsGroup`) | ❌ | test yaratish/o'chirish, ball, SERTIFIKAT — kurs davomidagi ish |
| `POST groups/{id}/contacts` ("Aloqa") | ✅ bugun yozish huquqi bo'lsa | `Teaches` orqali, sana = bugun |

Arxivlangan/bloklangan guruh **hech kimga** ochilmaydi (`TeacherGroupAccess.Visible`) — o'rinbosarga ham.

## 4. SERVER TEKSHIRUVI — `ValidateAsync`

⚠️ Ilgari server so'rovda kelgan **hamma narsani** yozardi. Bu naqsh `books.md` §2.1 da xato deb
belgilangan ("tekshiruv faqat frontendda edi, API to'g'ridan-to'g'ri chaqirilsa …").

| Tekshiruv | Konstanta | NEGA |
|---|---|---|
| Sana formati AYNAN `yyyy-MM-dd` | — | `"2026-13-99"` bazaga yozilardi (`DateOnly.TryParse` madaniyatga qarab qabul qilib yuborishi mumkin — ISO shakli qaytib chiqishi tekshiriladi) |
| Har sana guruhning HAQIQIY dars kuni | — | dars yo'q kunga "dars o'tildi" deb pul yozilardi |
| Sanalar soni | `MaxDates = 60` | chegara umuman yo'q edi — 1000 ta sana yuborib shuncha darsga haq yozdirish mumkin edi |
| O'tmish oynasi | `MaxBackdateDays = 14` | ⚠️ butunlay taqiqlanMAYDI ("kecha kasal bo'ldi, bugun rasmiylashtiramiz" — odatiy hol), lekin yopilgan oy maoshini orqaga qarab jimgina o'zgartirish mumkin bo'lmasin |
| Guruh ko'rinadimi | `TeacherGroupAccess.Visible` | arxivlangan/bloklangan guruhda dars ham, pul ham yo'q |
| O'rinbosar ishda-mi | `Teacher.IsArchived` / `IsBlocked` | ishdan ketgan o'qituvchiga maosh yozilardi |
| O'ziga o'zi o'rinbosar emasmi | — | ma'nosiz + qo'sh to'lov |
| Kesishuv (`BusyDatesAsync`) | — | bir kunga ikki tayinlov = ikki marta haq (tugma ikki marta bosilsa) |

**AYNAN shu tekshiruv `POST /` da ham, `GET /preview` da ham ishlatiladi** — "modal ruxsat
berdi, server rad etdi" holati bo'lmaydi.

`today` — **PARAMETR** (`null` = `AppClock.Today`): o'tmish oynasi testda vaqtni surmasdan
tekshirilsin.

## 5. API

| Metod · yo'l | Vazifasi |
|---|---|
| `GET /` | Ro'yxat (`groupId`, `teacherId`, `date`, `isActive`, `limit`) |
| `GET /preview` | **JONLI HISOB** — `groupId`, `substituteTeacherId`, `dates[]` (yoki `date`/`endDate`) → `SubstituteFeePreviewDto` |
| `GET /group-lesson-dates` | Guruhning oydagi dars kunlari (modal kalendari) |
| `GET /{id}` · `POST /` · `DELETE /{id}` | Bitta tayinlov · yaratish · bekor qilish |

⚠️ **O'QISH DARVOZALANGAN:** `[AdminPerm("teachers.substitutions", ReadRequiresPerm = true)]` — javobda
`EstimatedSalary`/`PerLessonFee`/`EstimatedDeduction`, ya'ni MAOSH raqamlari. `AdminPerm` odatda
GET'ni har qanday xodimga ochadi (bo'limlararo o'qish uchun); bu yerda u kassir/qabulchi ham
o'qituvchilar maoshini ko'rishini anglatardi (`uploads-security.md`).

**CHEGARA:** `MaxRows = 500` (audit `MaxLimit` bilan bir xil g'oya). **Yashirilmaydi:** javob
sarlavhalarida `X-Total-Count` (jami topilgan) va `X-Returned-Count` (shu javobda nechta) —
UI "jami N, bu yerda M" deb yoza oladi. Javob TANASI ilgarigidek MASSIV (klientlar buzilmasin).

**Frontend pulni O'ZI hisoblaMAYDI** — `/preview` dan oladi. `SubstituteFeePreviewDto.Warning`
foydalanuvchiga ko'rsatiladigan izoh (guruhda faol o'quvchi yo'q, bu oyda pul yig'ilmagan,
oyda dars kuni yo'q) yoki `null`; u tayinlashga TO'SIQ emas.

### O'qituvchi ilovasi

`GET /api/teacher/substitutions` — faqat FAOL tayinlovlar. Filtr
`SubstituteTeacherId == me || OriginalTeacherId == me`, ya'ni javobda **asosiy o'qituvchi ham**
qatnashadi va u yerda o'rinbosarning haqi turadi.

⚠️ Pul maydonlari (`EstimatedSalary`, `EstimatedDeduction`, `PerLessonFee`, `StudentCount`)
`TeacherPermissions.Salary` bilan darvozalangan. **403 qaytarilMAYDI, javob TOZALANADI**
(`uploads-security.md` dagi "javobni tozalash" siyosati): ro'yxatning o'zi ("qaysi kuni qaysi
guruhda dars o'taman") pul ma'lumoti emas va modul ishlashi uchun kerak. Ilgari bu endpointda
`Salary` tekshiruvi umuman yo'q edi.

## 6. AUDIT

| Nima | Qiymat |
|---|---|
| `EntityType` | `substitute_teacher` |
| Bo'lim (`AuditSections`) | **`teachers`** ("O'qituvchilar") — amal guruh TARKIBIGA emas, kim dars o'tishiga va kimning MAOSHIGA tegishli |
| `EntityId` | **`"{groupId}:{assignmentId}"`** — `Membership` naqshi |
| `action` | `create` (biriktirish) · `delete` (bekor qilish) |
| `teacherId` | O'RINBOSAR (haq uning maoshiga qo'shiladi → uning kartochkasidagi "Tarix"da ko'rinadi) |

⚠️ **`EntityId` NEGA prefiksli:** `AuditController` ning `groupId` filtri
`EntityId == groupId || EntityId.StartsWith(groupId + ":")` deb qidiradi. Ilgari u yerda faqat
tayinlov GUID'i turardi va yozuv **guruh sahifasining "Tarix" tabida hech qachon ko'rinmasdi**.

⚠️ `action = "delete"` ("cancel" ruxsat etilgan to'rt qiymat ichida YO'Q — `audit.md` §1).

⚠️ Audit yozuvi **servis ichida**, `SaveChangesAsync` dan OLDIN. Ilgari u controllerda, servis
allaqachon saqlab bo'lgandan KEYIN chaqirilar va bazaga umuman tushmasdi.

Jumla — o'zbekcha, TO'LIQ: guruh NOMI, o'qituvchilar F.I.SH, SANALAR (o'zbekcha: "17-avgust")
va sabab. **GUID yozilmaydi.** Sanalar 5 tadan ko'p bo'lsa "boshi — oxiri (jami N kun)".

## 7. UNUMDORLIK

`SalaryLedger.BuildAsync` — Moliya → "O'qituvchilar" hisoboti uni **har bir o'qituvchi uchun
sikl ichida** chaqiradi. Shuning uchun o'rinbosarlik bloki bitta yengil `AnyAsync` bilan
darvozalangan: markazda o'rinbosarlik ishlatilmasa (odatiy hol) qolgan so'rovlarning **hech
biri** ketmaydi. Ilgari bu yerda 4 ta SHARTSIZ so'rov turardi (N×4 aylanish, jadvalda indeks
ham yo'q).

Ro'yxat va preview'da narx **ommaviy** hisoblanadi (`PerLessonBatchAsync`) — tayinlov boshiga
so'rov yo'q; o'quvchilar soni, ko'chirishlar va yig'ilgan pul bittadan guruhlangan so'rovda.

## 8. NIMA ATAYIN QILINMAGAN

- **Markaz ustidan qo'shmaydi.** Model nol yig'indili: o'rinbosarga bonus, asosiyga "yarim
  ushlanma" kabi variantlar YO'Q. Sabab: har qanday koeffitsient ikkinchi formulani tug'diradi
  va modul aynan shundan kasal edi. Kerak bo'lsa — `PerLesson` ning ICHIDA, bitta joyda.
- **`FinanceTransaction` ga yozilmaydi.** O'rinbosarlik pul HARAKATI emas, maosh HISOBI:
  u `SalaryLedger` da ikki tomonning raqamini o'zgartiradi, kassaga tegmaydi.
- **O'rinbosarning boshqa guruhdagi darsi bilan VAQT kesishuvi tekshirilmaydi** — guruhlarning
  dars vaqtlari (`StartTime`) turlicha bo'lishi mumkin, "bir kunda ikki guruh" haqiqiy konflikt
  emas. Bir GURUHda bir KUNGA ikki tayinlov esa taqiqlangan (qo'sh to'lov).
- **O'rinbosar guruh chatiga qo'shilmaydi** va o'quvchilar ro'yxatining pul/hujjat qismini
  ko'rmaydi — tayinlov faqat DARS o'tish uchun.
- **Uzoq muddatga mo'ljallanmagan** (`MaxDates = 60`): butun kurs davomida boshqa odam
  o'qitadigan bo'lsa — guruhning ASOSIY o'qituvchisini almashtirish kerak, aks holda maosh,
  reyting va jurnal tarixi noto'g'ri odamga bog'lanib qoladi.
