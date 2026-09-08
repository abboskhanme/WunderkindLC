---
description: Chegirmalar — o'quvchi chegirmasi registri (tarix, tahrirlash, bekor qilish) va markaz bo'yicha chegirmalar hisoboti.
paths:
  - "IntellectCRM.Application/Services/StudentDiscountService.cs"
  - "IntellectCRM.Server/Controllers/StudentDiscountsController.cs"
  - "IntellectCRM.Server/Controllers/DiscountReportController.cs"
  - "IntellectCRM.Client/src/api/services/discounts.ts"
  - "IntellectCRM.Client/src/pages/admin/students/DiscountSection.tsx"
  - "IntellectCRM.Client/src/pages/admin/reports/DiscountsReportPage.tsx"
---

# Chegirmalar qoidalari

Ikki yuza: o'quvchi profilidagi **«Chegirma»** tabi (`students.list`) va
**Hisobotlar → «Chegirmalar hisoboti»** (`/admin/hisobotlar/chegirmalar`, ruxsat `finance.main`).
Migratsiya: `AddStudentDiscounts` (`StudentDiscount` jadvali).

## 1. ENG MUHIM — PUL MANTIG'I O'ZGARMADI

Hisob-kitob avvalgidek `Student.DiscountPct` / `DiscountAmount` / `DiscountNote` /
`DiscountStartMonth` / `DiscountEndMonth` / `DiscountGroupId` maydonlariga tayanadi
(`TuitionService.DiscountForMonth`). Yangi jadval — **REGISTR**: "kim, qachon, qancha, nega
berdi va kim bekor qildi".

Sabab `.claude/rules/year-freeze.md` §1 dagi bilan AYNAN bir xil: chegirmani o'nlab joy o'qiydi
(oylik hisobi, guruh balansi, maosh, kurs moliyasi, ushlab turish bonusi, o'quvchi kabineti).
Chegirmani "ko'p qatorli" qilib pul mantig'iga ulash bularning HAMMASINI jimgina buzardi.

⚠️ Yangi ko'rsatkich qo'shsangiz — chegirma summasini `StudentDiscount` dan HISOBLAMANG.
Pul haqiqati ikki joyda: joriy holat `Student.Discount*` da, TARIX esa `MonthlyCharge.Discount`
da. Registr — ko'rinish va sabab qatlami.

## 2. INVARIANT — bitta `active` qator = `Student.Discount*`

Har o'quvchida ko'pi bilan BITTA `Status == "active"` qator bo'ladi va u AYNAN
`Student.Discount*` maydonlarini aks ettiradi.

| Status | Nima |
|---|---|
| `active` | Hozir amaldagi yagona yozuv |
| `replaced` | Yangisi bilan almashtirilgan (tarix) |
| `cancelled` | Bekor qilingan (tarix), `CancelReason` bilan |

⚠️ **Yozish YAGONA joydan — `StudentDiscountService`**. To'g'ridan-to'g'ri `Student.DiscountPct`
ga qiymat berib qo'ymang: registr eskirib, profil "chegirma yo'q" deb ko'rsatib turardi.
Eski forma (`PUT /api/admin/students/{id}`) ham `SyncFromStudentAsync` orqali registrni
moslaydi — shuning uchun `StudentFormModal` dagi chegirma maydonlari avvalgidek ishlaydi.

⚠️ **Tarixiy qator TAHRIRLANMAYDI va BEKOR QILINMAYDI** (server 400). O'tgan oylarning
hisobi allaqachon `MonthlyCharge` da yozilgan — tarixiy qatorni o'zgartirish hisobot bilan
pulni bir-biridan ayirib yuborardi.

## 3. Nolga tushirish — «bekor qilish», tahrirlash EMAS

`Pct == 0 && Amount == 0` bilan chegirma YARATIB bo'lmaydi (400). Chegirmani olib tashlash
uchun **«Bekor qilish»** bor: qator `cancelled` bo'ladi, sabab yoziladi va o'quvchidan
chegirma olinadi. Aks holda "chegirma 0 ga tushirildi" tarixda sababsiz qolib ketardi.

## 4. `applyCurrentMonth` — joriy oyga DARHOL qo'llanadimi

Mavjud `PUT /students/{id}?applyDiscount=true` bilan AYNAN bir xil mantiq va AYNAN bir xil kod
(`StudentDiscountService.ReapplyCurrentMonthAsync`):

- `true` — joriy oy `MonthlyCharge` larida `Discount` qayta hisoblanadi, balans farqqa
  to'g'rilanadi;
- `false` — chegirma keyingi oydan amal qiladi.

⚠️ `Locked` (superadmin qo'lda tahrirlagan) qatorlar TEGILMAYDI va `Amount` (narx) hech qachon
o'zgarmaydi — faqat `Discount`. O'TGAN oylar ham tegilmaydi: ular tarix.

⚠️ Chegirma o'zgarishi **balansni** o'zgartiradi — klient amaldan keyin o'quvchi ma'lumotini
qayta yuklaydi, aks holda ekranda eski balans qolardi.

## 5. SNAPSHOT maydonlar

`StudentName`, `GroupName`, `TeacherId`, `TeacherName` — chegirma BERILGAN paytdagi holat,
ATAYIN takrorlangan (`ContactRequest` va `RetentionBonusAward` dagi bilan bir xil sabab):
o'quvchi arxivlansa, guruh o'chsa yoki o'qituvchi almashsa ham tarix o'qilaveradi.

⚠️ Demak registrdagi o'qituvchi — **o'sha paytdagi** o'qituvchi. Hisobotdagi o'qituvchi kesimi
esa `MonthlyCharge` → guruh → **joriy** o'qituvchi bo'yicha quriladi (pul o'sha guruhda
hisoblangan). Ikkisi farq qilishi mumkin va bu XATO emas.

## 6. HISOBOT — pul haqiqati `MonthlyCharge` dan

`GET /api/admin/reports/discounts?from=yyyy-MM&to=yyyy-MM`.

⚠️ Summalar registrdan EMAS, **`MonthlyCharge.Discount`** dan yig'iladi. Sabab: registr yangi
jadval, chegirmalarning HAQIQIY tarixi esa oylik hisoblarda allaqachon to'liq turibdi — ya'ni
hisobot birinchi kundanoq rost raqam ko'rsatadi. Registr faqat "hozir kimda chegirma bor,
qanday sabab bilan" qismini beradi.

- `periodCharged` — davrdagi BARCHA hisoblar yig'indisi (chegirmalilar emas): usiz "chegirma
  ulushi" ko'rsatkichi ma'nosiz bo'lardi.
- `months[]` — davrdagi HAR oy, chegirmasiz oy ham 0 bilan (grafik uzilmasin).
- Guruhsiz hisoblar (`MonthlyCharge.GroupId == null`) va o'qituvchisi biriktirilmagan guruhlar
  ALOHIDA qatorda chiqadi — jimgina yo'qolmaydi.

Migratsiyada `Pct > 0 || Amount > 0` bo'lgan o'quvchilar uchun bittadan `active` qator
**BACKFILL QILINGAN**. Bu `PastPeriods` dagi qaroridan farq qiladi (`.claude/rules/membership-periods.md` §2)
va sabab aniq: u yerda backfill pul hisobini o'zgartirardi, bu yerda jadval pulga umuman
tegmaydi — backfill bo'lmasa esa bo'lim birinchi kundanoq bo'sh ko'rinardi.

## 7. Ruxsat

| Yuza | Kalit |
|---|---|
| Profil → «Chegirma» tabi (ko'rish) | `students.list` |
| Chegirma berish/tahrirlash/bekor qilish | `students.list:edit` |
| «Chegirmalar hisoboti» | `finance.main` (`ReadRequiresPerm = true`) |

⚠️ **Yangi ruxsat kaliti QO'SHILMAGAN.** Chegirma allaqachon o'quvchi formasidan
(`students.list`) tahrirlanardi — yangi kalit kiritish o'sha lineyani ikkiga bo'lardi.
Hisobot esa pul kesimi bo'lgani uchun Moliya kalitida va `ReadRequiresPerm` bilan: javobda
summalar bor, GET'ni odatdagidek har qanday xodimga ochib bo'lmaydi
(`.claude/rules/uploads-security.md` dagi "xodim uchun o'qish darvozasi" bilan bir xil sabab).

## 8. Audit

Turi — mavjud **`StudentDiscount`** (`AuditService.EntityStudentDiscount`), bo'limi `students`
(`AuditSections`). Yangi tur QO'SHILMAGAN.

⚠️ Bu tur ALDAMCHI: unda chegirmadan tashqari arxivlash/tiklash, login bloklash va qo'lda
oylik tahriri ham yoziladi (`.claude/rules/audit.md` §2). Shuning uchun chegirma yozuvlarining
`summary` matni to'liq va o'ziga xos bo'lishi SHART — tarixda ular faqat matn bo'yicha
ajratiladi.

## 9. Yangi ish qo'shsangiz

- Chegirmani KOD bilan o'zgartirmoqchi bo'lsangiz — `StudentDiscountService` orqali (§2).
- Yangi moliyaviy ko'rsatkichda chegirmani `MonthlyCharge.Discount` dan oling, registrdan emas (§1).
- Bir o'quvchida bir vaqtda BIR NECHTA (masalan har guruhga alohida) chegirma kerak bo'lsa —
  bu ALOHIDA ish: u `Student.Discount*` ni pul mantig'idan chiqarishni talab qiladi va shu
  qoidaga tayanadi, uni almashtirmaydi.
