---
description: Chegirmalar — HAR FAN (guruh) uchun alohida chegirma registri (pul manbai), tarix, tahrirlash, bekor qilish va markaz bo'yicha chegirmalar hisoboti.
paths:
  - "IntellectCRM.Application/Services/StudentDiscountService.cs"
  - "IntellectCRM.Application/Services/TuitionService.cs"
  - "IntellectCRM.Server/Controllers/StudentDiscountsController.cs"
  - "IntellectCRM.Server/Controllers/DiscountReportController.cs"
  - "IntellectCRM.Client/src/api/services/discounts.ts"
  - "IntellectCRM.Client/src/pages/admin/students/DiscountSection.tsx"
  - "IntellectCRM.Client/src/pages/admin/reports/DiscountsReportPage.tsx"
---

# Chegirmalar qoidalari

Ikki yuza: o'quvchi profilidagi **«Chegirma»** tabi (`students.list`) va
**Hisobotlar → «Chegirmalar hisoboti»** (`/admin/hisobotlar/chegirmalar`, ruxsat `finance.main`).
Migratsiyalar: `AddStudentDiscounts` (jadval), `AddStudentDiscountCourse` (FAN snapshot'i).

## 0. NIMA O'ZGARDI (2026-09-08) va NEGA

Registr birinchi kuni **KO'ZGU** edi: pul avvalgidek `Student.Discount*` maydonlaridan
hisoblanardi, ya'ni o'quvchida bir vaqtda **BITTA** chegirma bo'lardi. 2–3 fanga qatnaydigan
o'quvchida bu yetmadi — foydalanuvchi **har fan uchun alohida chegirma** talab qildi (har biri
alohida ko'rinsin, alohida tahrirlansin, alohida bekor qilinsin).

Buni `Student.Discount*` da ifodalab **bo'lmaydi**: bitta "foiz + summa" juftligi ko'p qatorli
holatni saqlay olmaydi. Shuning uchun yo'nalish **teskariga burildi**:

| | Ilgari | Endi |
|---|---|---|
| Pul manbai | `Student.Discount*` | **`StudentDiscount` registri** |
| `StudentDiscount` | ko'zgu (ko'rsatish) | **manba** |
| `Student.Discount*` | manba | **ko'zgu (denormalizatsiya)** |
| Nechta amaldagi chegirma | o'quvchida bitta | **har (o'quvchi, guruh) qamrovida bitta** |
| Eski o'quvchi formasi (`PUT /students/{id}`) | chegirmani YOZARDI | **YOZMAYDI** (§6) |

⚠️ **Eski ma'lumot uchun hech narsa o'zgarmadi**: migratsiya har chegirmali o'quvchiga BITTA
qator qo'ygan (`GroupId` = eski `Student.DiscountGroupId`), yangi qoida esa bunday holatda
AYNAN eski natijani beradi. Bu TEST bilan qulflangan (§9).

## 1. ENG MUHIM — qaysi chegirma qo'llanadi (`DiscountRules.Resolve`)

`Resolve(rows, month, groupId)` → g'olib qator yoki `null`:

1. `active` + shu oyda amalda + **`GroupId == groupId`** (AYNAN shu fan) — g'olib;
2. aks holda `active` + amalda + **`GroupId == null`** («barcha guruhlar»);
3. aks holda `null`.

⚠️ **CHEGIRMALAR HECH QACHON QO'SHILMAYDI (summa emas).** Ikkita chegirma bir-birining ustiga
qo'yilsa (masalan umumiy 40% + fanga 60%) oylik kutilmaganda **0 ga tushib ketardi**.
**ENG ANIQ moslik g'olib** — bu loyihadagi `navigation.ts → activeNavTo` bilan AYNAN bir xil
printsip.

⚠️ Bir qamrovda bir nechta `active` qator bo'lib qolsa (buzuq/qo'lda tuzatilgan ma'lumot) —
**eng YANGISI** olinadi (`CreatedAt`), jimgina birinchisi emas: aks holda natija qatorlar
tartibiga bog'liq bo'lib qolardi.

⚠️ Davr tekshiruvi (`StartMonth`..`EndMonth`, INKLYUZIV) **qayta yozilmagan** —
`TuitionService.DiscountActiveForMonth(start, end, month)` chaqiriladi. Nusxa ko'chirilsa
registr "amalda" deb ko'rsatgan chegirma hisobda qo'llanmay qolardi (yoki teskarisi).

## 2. `DiscountBook` — qatorlarni tashiydigan idish

`TuitionService.DiscountForMonth` endi `Student` emas, **registr qatorlarini** oladi:

```csharp
decimal DiscountForMonth(IReadOnlyList<StudentDiscount> rows, decimal fee, string month, string? groupId)
```

⚠️ **ESKI IMZO ATAYIN QOLDIRILMAGAN.** Qoldirilsa "qatorlarni yuklashni unutish" xatosi
jimgina 0 chegirma berardi va o'quvchilarga **ortiqcha qarz** yozilardi. Endi buni
**kompilyator** ushlaydi.

Qatorlar `DiscountBook` orqali olinadi (`Application/Services/StudentDiscountService.cs`):

| Metod | Qachon |
|---|---|
| `LoadForStudentAsync` | bitta o'quvchi (ledger, aktivlashtirish/muzlatish, joriy oyni qayta hisoblash) |
| `LoadAsync(ids)` | bir nechta o'quvchi (guruh narxini joriy oyga qo'llash) — id'lar 500 talik bo'laklarda |
| `LoadAllAsync` | oylik accrual va shartnoma tokenlari (butun baza) |
| `Empty` | FAQAT chegirmasiz/test yo'li |

⚠️ **N+1 QILMANG.** Ommaviy yo'llarda kitob **bir marta** yuklanadi:
`TuitionService.AccrueMonth` (`LoadAllAsync`), `ApplyGroupFeeToCurrentMonthAsync` (`LoadAsync`),
`AccrueCatchUpAsync` va `PaymentReminderService.BuildMessageAsync` (halqadan oldin bir marta),
`ContractsController.LoadCtxAsync` (`TokenCtx.Discounts`).

⚠️ **ACCRUAL YO'LI ALOHIDA XAVFLI.** `AccrueDue` har startupda va har 12 soatda **butun
tarixni** qayta skanerlaydi (`.claude/rules/membership-periods.md` §6) — kitob bo'sh bo'lsa
chegirma 0 bo'lib, **ommaviy yolg'on qarz** va ota-onalarga avto-SMS to'lqini ketardi.
Qulflovchi testlar: `FinanceDbTests.AccrueMonth_chegirma_qollanadi_*`,
`AccrueMonth_HAR_FAN_uchun_OZ_chegirmasi__qoshilmaydi`.

⚠️ **Kitob EF'ning HALI SAQLANMAGAN qatorlarini ham ko'radi** (`DbSet.Local`, Local USTUN).
Chegirma berilgandan keyin `SaveChanges` dan OLDIN joriy oy qayta hisoblanadi
(`ReapplyCurrentMonthAsync`) — `AsNoTracking` so'rovi yangi qatorni qaytarmasdi va "chegirma
berdim, oylik o'zgarmadi" holati chiqardi.

### Pul QAYERDA hisoblanadi (to'liq ro'yxat)

`TuitionService.DiscountForMonth` chaqiruvchilari: `AccrueOne` (→ `AccrueMonth`,
`AccrueCatchUpAsync`, `EnsureChargeAsync`), `ApplyFeeToCharge`, `ChargeActivationProrateAsync`,
`ChargeFreezeProrateAsync`, `StudentLedger`, `StudentGroupLedger`,
`StudentDiscountService.ReapplyCurrentMonthAsync`, `ContractsController.StudentTokens` (§3),
`StudentsController.Update` (guruhsiz o'quvchining yetishmagan oylari).

Qolgan barcha joylar (`StudentProfileBuilder`, `StudentLedger.Map`, `FinanceController`,
`StudentsPage`, `StudentViewModal` …) chegirmani faqat **KO'RSATISH** uchun o'qiydi.

## 3. Shartnoma (`ContractsController.StudentTokens`)

Oylik to'lov **HAR GURUH uchun alohida**: `net = Σ (guruh narxi − o'sha guruhga tegishli
chegirma)`.

⚠️ Ilgari narxlar avval **QO'SHILAR**, keyin ustiga BITTA chegirma qo'llanardi — endi bu
matematika chegirmasini ingliz tili narxidan ham ayirib, shartnomaga **yolg'on summa**
yozardi.

`@chegirma` tokeni: bitta chegirma bo'lsa avvalgidek ("20%" / summa), bir nechta bo'lsa FAN
nomlari bilan qisqa yorliq — «Matematika 20%, Ingliz tili 15%» (`DiscountRules.ActiveLabel`).

## 4. `Student.Discount*` — endi FAQAT KO'RSATISH (denormalizatsiya)

Maydonlar **O'CHIRILMAYDI** (mobil ilovalar bu repoda EMAS va ularni buzib bo'lmaydi), lekin
pul ularni **boshqa O'QIMAYDI**. Ular registrning **ASOSIY qatorini** aks ettiradi:

```
asosiy = GroupId == null bo'lgan `active` qator,
         bo'lmasa — eng YANGI `active` qator,
         qator umuman bo'lmasa — hammasi 0/"".
```

⚠️ Eski ma'lumotda har o'quvchida bitta qator bor, ya'ni bu qoida mavjud qiymatlarni **AYNAN**
qaytaradi (test: `RefreshMirror_ESKI_MALUMOT__bitta_qator_bolsa_AYNAN_eski_qiymatlarni_beradi`).

⚠️ Ko'zgu **BITTA joyda** yangilanadi — `StudentDiscountService.RefreshMirrorAsync`, har
yozuvdan keyin (`ApplyAsync` / `UpdateAsync` / `CancelAsync` uni o'zi chaqiradi).
`Student.DiscountPct = ...` deb qo'lda yozmang.

⚠️ `TuitionService.DiscountActiveForMonth(Student, month)` va
`TuitionService.ChargeFor(Student, IDictionary)` **OLIB TASHLANDI** (ikkinchisi o'lik kod edi) —
`Student` dan chegirma o'qiydigan yo'l qolmasin.

**`Student.DiscountCount`** — `[NotMapped]`, HOZIR amaldagi chegirmalar soni. `MemberState`
naqshi bilan AYNAN bir xil: faqat `StudentsController.GetAll` va `GetOne` da bitta ommaviy
so'rov bilan to'ldiriladi. Boshqa joyda `0` bo'lib qolishi **normal** — bu ko'rsatkich, mantiq
emas.

## 5. INVARIANT — «bitta QAMROV, bitta amaldagi qator»

Har `(StudentId, GroupId)` juftligida ko'pi bilan bitta `active` qator; `GroupId == null`
(«barcha guruhlar») — **alohida qamrov**.

| Status | Nima |
|---|---|
| `active` | Shu qamrovda hozir amaldagi yozuv |
| `replaced` | Shu qamrovda yangisi bilan almashtirilgan (tarix) |
| `cancelled` | Bekor qilingan (tarix), `CancelReason` bilan |

- `ApplyAsync` faqat **O'SHA qamrovdagi** eski qatorni `replaced` qiladi — boshqa fanlarnikiga
  TEGMAYDI.
- `CancelAsync` faqat **o'sha** qatorni yopadi.
- ⚠️ Allaqachon amaldagi chegirmasi bor qamrovga YANGI chegirma berishga urinish — **409**
  («Bu fanda allaqachon chegirma bor — uni tahrirlang»). **Jimgina almashtirmang:** admin
  "yangi berdim" deb o'ylab, eskisini bilmasdan o'chirib yuborardi. Javobda `existingId`
  qaytadi. `UpdateAsync` da qamrov BOSHQASIGA ko'chirilsa ham shu tekshiruv.
- ⚠️ **Tarixiy qator TAHRIRLANMAYDI va BEKOR QILINMAYDI** (server 400). O'tgan oylarning hisobi
  allaqachon `MonthlyCharge` da yozilgan.

### Standart tanlov — FAN, «barcha guruhlar» EMAS

Chegirma berish oynasida qamrov shunday oldindan tanlanadi:

| O'quvchining bo'sh fanlari | Standart tanlov |
|---|---|
| bitta | **o'sha fan** |
| bir nechta | **hech narsa** — admin o'zi tanlaydi, tanlamaguncha saqlash o'chiq |
| umuman yo'q (guruhsiz) | «Barcha guruhlar» |

⚠️ Sabab — ikki xatoning zarari **TENG EMAS**. «Barcha guruhlar» tanlab qo'yilsa, o'quvchi
KEYIN yangi guruhga qo'shilganda chegirma o'sha fanga ham **jimgina** tushadi va buni hech kim
sezmaydi (pul yo'qoladi). Aniq fan tanlangan bo'lsa esa yangi fanga chegirma tushmaydi — admin
buni KO'RADI va kerak bo'lsa qo'shadi.

⚠️ Klientda «tanlanmagan» holat `SCOPE_NONE` bilan ifodalanadi va `''` DAN FARQ QILADI: `''` —
«Barcha guruhlar» degan HAQIQIY tanlov. Ikkalasi bir xil bo'lsa, hech narsa tanlamagan admin
bilmasdan barcha fanlarga chegirma berib yuborardi.

⚠️ **MA'LUM CHEKLOV:** «barcha guruhlar» chegirmasidan BITTA fanni **chiqarib tashlash**
(istisno) hozircha YO'Q. Buning o'rniga har fanga alohida qator beriladi. Kerak bo'lsa bu
ALOHIDA ish sifatida ko'rib chiqilsin (bu qoidaga tayanadi, uni almashtirmaydi).

## 6. Eski o'quvchi formasi chegirmani ENDI YOZMAYDI

Foydalanuvchi aniq aytdi: chegirma o'quvchini tahrirlash formasidan emas, profildagi
«Chegirma» bo'limidan boshqariladi.

- **`PUT /api/admin/students/{id}`** — `p.Discount*` maydonlari **e'tiborga olinmaydi**;
  `discountChanged` mantig'i, u bilan bog'liq joriy-oy qayta hisobi, `?applyDiscount=` ta'siri
  va chegirma auditi olib tashlangan. Ko'zgu faqat registrdan yangilanadi.
- **`POST /api/admin/students`** — chegirma bilan yaratilsa avvalgidek `ApplyAsync` orqali
  («barcha guruhlar» qamrovi): import/API mijozlari buzilmasin.
- `StudentPayload` DTO'sidan maydonlar **OLIB TASHLANMAGAN** (API shakli buzilmasin) —
  `Update` da ishlatilmasligi kodda izoh bilan yozilgan.

⚠️ Sabab: bitta "foiz + summa" juftligi butun holatni ifodalay olmaydi, ya'ni eski forma
saqlanganda o'quvchining BOSHQA fanlaridagi chegirmalarini **jimgina o'chirib yuborardi**.

## 7. SNAPSHOT maydonlar

`StudentName`, `GroupName`, **`CourseId`/`CourseName`**, `TeacherId`, `TeacherName` — chegirma
BERILGAN paytdagi holat, ATAYIN takrorlangan (`ContactRequest` va `RetentionBonusAward` dagi
bilan bir xil sabab): o'quvchi arxivlansa, guruh o'chsa yoki o'qituvchi almashsa ham tarix
o'qilaveradi.

⚠️ **FAN nomi birinchi.** Foydalanuvchi chegirmani "guruh" emas, **FAN** deb o'ylaydi
("Matematikaga 20%"), shuning uchun UI va audit matnlari `DiscountRules.ScopeLabel` dan
foydalanadi: fan nomi → bo'lmasa guruh nomi → bo'lmasa «Barcha guruhlar».

⚠️ Registrdagi o'qituvchi — **o'sha paytdagi** o'qituvchi. Hisobotdagi o'qituvchi kesimi esa
`MonthlyCharge` → guruh → **joriy** o'qituvchi bo'yicha quriladi. Ikkisi farq qilishi mumkin va
bu XATO emas.

## 8. `applyCurrentMonth` — joriy oyga DARHOL qo'llanadimi

`StudentDiscountService.ReapplyCurrentMonthAsync`:

- `true` — joriy oy `MonthlyCharge` larida `Discount` **har qator uchun alohida** qayta
  hisoblanadi (`Resolve` bilan), balans farqqa to'g'rilanadi;
- `false` — chegirma keyingi oydan amal qiladi.

⚠️ `Locked` (superadmin qo'lda tahrirlagan) qatorlar TEGILMAYDI va `Amount` (narx) hech qachon
o'zgarmaydi — faqat `Discount`. O'TGAN oylar ham tegilmaydi: ular tarix.

⚠️ Chegirma o'zgarishi **balansni** o'zgartiradi — klient amaldan keyin o'quvchi ma'lumotini
qayta yuklaydi.

## 9. HISOBOT — davr summalari `MonthlyCharge` dan

`GET /api/admin/reports/discounts?from=yyyy-MM&to=yyyy-MM`.

⚠️ Davr summalari registrdan EMAS, **`MonthlyCharge.Discount`** dan yig'iladi: registr faqat
"hozir nima amalda" ni biladi, o'tgan oyda esa boshqacha bo'lgan bo'lishi mumkin.

- `byStudent` da `pct/amount/reason` o'rniga **`activeCount`** (nechta fanda chegirma bor) va
  **`activeLabel`** («Matematika 20%, Ingliz tili 50 000 so'm»). ⚠️ Yorliq **SERVERDA**
  quriladi (`DiscountRules.ActiveLabel`) — klientda qayta yig'ilsa, qoida o'zgarganda
  ikkinchisi jimgina eskirardi.
- `summary.activeCount` — **qatorlar** soni, `summary.studentCount` — **odamlar** soni. Endi
  ular teng emas (bir odamda bir nechta chegirma bo'lishi mumkin).
- `byReason` — `Count` qatorlar soni; summa esa o'quvchi bo'yicha va `Distinct` bilan (bir
  sabab bilan ikki fanga chegirma olgan o'quvchi ikki marta sanalmasin).
- `periodCharged` — davrdagi BARCHA hisoblar (chegirmalilar emas): usiz "chegirma ulushi"
  ma'nosiz bo'lardi.
- `months[]` — davrdagi HAR oy, chegirmasiz oy ham 0 bilan (grafik uzilmasin).
- Guruhsiz hisoblar va o'qituvchisi biriktirilmagan guruhlar ALOHIDA qatorda chiqadi.

## 10. Ruxsat

| Yuza | Kalit |
|---|---|
| Profil → «Chegirma» tabi (ko'rish) | `students.list` |
| Chegirma berish/tahrirlash/bekor qilish | `students.list:edit` |
| «Chegirmalar hisoboti» | `finance.main` (`ReadRequiresPerm = true`) |

⚠️ **Yangi ruxsat kaliti QO'SHILMAGAN.** Chegirma allaqachon `students.list` ostida edi.
Hisobot esa pul kesimi bo'lgani uchun Moliya kalitida va `ReadRequiresPerm` bilan.

## 11. Audit

Turi — mavjud **`StudentDiscount`** (`AuditService.EntityStudentDiscount`), bo'limi `students`
(`AuditSections`). Yangi tur QO'SHILMAGAN.

⚠️ Bu tur ALDAMCHI: unda chegirmadan tashqari arxivlash/tiklash, login bloklash va qo'lda oylik
tahriri ham yoziladi (`.claude/rules/audit.md` §2). Shuning uchun `summary` matni to'liq
bo'lishi SHART va u endi **QAMROV (fan) bilan boshlanadi** — «Matematika: 20% / 0 so'm …»:
o'quvchida bir nechta chegirma bo'lishi mumkin, ya'ni tarixda birinchi savol "qaysi fanniki".

⚠️ Audit `before`/`after` snapshot'i **KO'ZGU** maydonlaridan olinadi — u endi pul haqiqati
emas; "qaysi fan o'zgardi" faqat `summary` matnida ko'rinadi.

## 12. Yangi ish qo'shsangiz

- Chegirmani KOD bilan o'zgartirmoqchi bo'lsangiz — faqat `StudentDiscountService` orqali (§5).
- Yangi moliyaviy ko'rsatkichda chegirmani `DiscountBook` dan oling (§2) — `Student.Discount*`
  ni **O'QIMANG** (§4). O'tgan oylarning HAQIQIY chegirmasi esa `MonthlyCharge.Discount` da.
- Ommaviy yo'l yozsangiz — kitobni **halqadan tashqarida** yuklang (§2).
- `Resolve` ga qo'shimcha qamrov (masalan "kurs bo'yicha chegirma") qo'shsangiz — **QO'SHISH
  emas, USTUNLIK** darajasi sifatida qo'shing va §1 dagi jadvalni yangilang.
- Javob shakli o'zgarsa — `IntellectCRM.Client/src/api/services/discounts.ts` bilan
  MAYDONMA-MAYDON mos bo'lishi SHART (ikkala tomon BIRGA o'zgaradi).
