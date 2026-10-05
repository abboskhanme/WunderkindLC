# SPEC — Kursda sinflar oralig'i va sinfga qarab oylik (2026-10-05)

## Talab

Kurs qo'shish/tahrirlashda sinflar qamrovi kiritiladi ("5–6-sinflar", "4–8-sinflar") va o'quvchining
oylik to'lovi uning **joriy maktab sinfiga qarab avtomatik** tanlanadi (foydalanuvchi tanlovi).

## Model

`SubjectPriceBand` (yangi jadval, migratsiya `AddSubjectPriceBands`):

| Maydon | Ma'no |
|---|---|
| `SubjectId` | kurs (o'chirilsa — oraliqlar ham, cascade) |
| `GradeFrom` / `GradeTo` | sinf oralig'i, 1..12 (12 = «Maktabni bitirgan»), chegaralar KIRADI |
| `Price` | shu oraliq uchun oylik narx (> 0) |
| `LessonPrice` | bir dars narxi (qisman oy uchun); 0 = pro-rata |

Kursning sinflar QAMROVI = oraliqlarining birlashmasi. Oraliq yo'q kurs — hozirgidek bitta narx.

## Narx qoidasi — YAGONA joy (`TuitionPricing` / `PricingBook`)

```
o'quvchining SHU OYDAGI sinfi (SchoolGrades.Current, oyning 1-kuniga)
  → guruh kursining oralig'iga tushsa  : oraliq Price (+ oraliq LessonPrice)
  → aks holda (sinf yo'q / oraliqdan tashqarida / kurssiz guruh): guruh MonthlyFee (+ kurs LessonPrice)
```

- 1-sentabrda sinf o'zi oshadi → sentabr hisobi yangi oraliq narxida yoziladi (alohida ish yo'q).
- Chegirma o'zgarmaydi — tanlangan narxdan hisoblanadi (`DiscountBook`).

## Qo'llanadigan joylar (o'quvchi QARZI)

`TuitionService`: `AccrueMonth` · `AccrueCatchUpAsync` · `EnsureChargeAsync` · `ChargeActivationProrateAsync`
· `ChargeFreezeProrateAsync` · `ApplyGroupFeeToCurrentMonthAsync`; `StudentGroupLedger` (to'lov oynasidagi
oylar va qisman oylar ko'rinishi); `StudentLedger` (oylik ko'rinishi); `SubscriptionRisk` + `IncomePlan`
(hali yozilmagan oy); `ContractsController` (shartnoma summasi); `StudentPortalController` (ilovadagi
oylik); `ClassesController` guruh a'zolari narxi; `StudentDiscountService` (chegirma oynasidagi narx).

## ATAYIN o'zgarmaydigan joylar (guruh darajasi)

Maosh ulushi og'irliklari (`SalaryLedger`, `GroupBalanceService`, `CourseFinanceReport`), KPI o'rtacha narxi,
kurs analitikasi tushumi, ushlab turish bonusi og'irligi, guruh snapshot/audit, eski guruhsiz (ClassName)
billing. ⚠️ Maosh foizi 2026-09 dan HISOBLANGAN oylikdan (`MonthlyCharge.Amount`) — demak u avtomatik
to'g'ri bo'ladi; faqat teglanmagan to'lovni guruhlarga bo'lish nisbati guruh narxida qoladi.

## Mavjud hisoblar

Yozilgan hisoblar (oktabr ham) TEGILMAYDI. Oraliqlar tahrirlanganda «joriy oydan qo'llash» tanlansa —
kurs narxi o'zgarganidagi kabi joriy oy hisoblari (qo'lda tahrirlanmagan) har o'quvchi uchun qayta
moslanadi; aks holda keyingi hisobdan.

## Tekshiruv (server)

1 ≤ from ≤ to ≤ 12, narx > 0, bir dars narxi ≥ 0, oraliqlar ustma-ust tushmaydi, ko'pi bilan 12 ta.
Asosiy narxi 0 (bepul) kursga oraliq qo'shilmaydi; bepul guruhda oraliq qo'llanmaydi (orqaga qarz xavfi).

## Ko'rib chiqishdan keyin (qa-review)

- Bepul guruh + oraliq → `AccrueDue` o'tgan oylarga orqaga qarz yozardi → qoida va forma bilan yopildi.
- «Joriy oydan qo'llash» qisman oy hisobini to'liq oylik bilan ustidan yozardi (eski xato, oraliqlar
  bilan kengayardi) → `oldFee`: narxi o'zgarmagan o'quvchi va qisman hisob tegilmaydi.
- Guruh kursi va narxi birga o'zgarsa eski kurs oraliqlari qo'llanardi → kurs id'lari aniq uzatiladi.
- Ommaviy amallarda a'zolik boshiga so'rov → oraliqlar bir marta yuklanadi.
- O'quvchi guruhlari kartasi / to'lov oynasi va aktivlashtirish tarixi matnida o'quvchi narxi.

## Testlar

`TuitionPricingTests` (sof qoida + validatsiya), `SubjectPriceBandBillingTests` (SQLite: oylik hisob,
qisman oy, catch-up, to'lov oynasi, joriy oyga qo'llash).
