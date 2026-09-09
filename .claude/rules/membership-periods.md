# A'zolikning FAOL DAVRLARI qoidalari

Migratsiya: `AddStudentGroupPastPeriods` (`StudentGroup.PastPeriods`, `text[]`).

## 1. MUAMMO — bitta a'zolikda bir NECHTA faol davr bo'ladi

`StudentGroup` da a'zolik tarixi bitta `(ActivatedAt, FrozenAt)` juftligi bilan ifodalanadi.
Muzlatib qayta aktivlashtirilganda `ActivateCoreAsync` `ActivatedAt` ni YANGI sana bilan ustidan
yozadi va `FrozenAt` ni tozalaydi — **oldingi faol davrning izi qolmaydi**.

Oqibati (hammasi `ActivatedAt` ga tayanadi):

| Qayerda | Nima buzilardi |
|---|---|
| `StudentGroupLedger` | Muzlashdan oldingi to'lanmagan oy to'lov oynasida ko'rinmasdi — qarz balansda bor, lekin to'lab bo'lmasdi |
| `MembershipLifecycle.BillableInMonth` | Eski oylar "pullik emas" → maosh taqsimoti, ushlab turish bonusi, kurs moliyasi, guruh balansi |
| `TuitionService.AccrueMonth` | Eski davrda tushib qolgan oy hisobi HECH QACHON yozilmasdi |
| `CourseAnalytics.WasActiveAt` | "Oy oxirida faol" IKKI xil xato: faol oylar tushib qolar, muzlab yotganlari faol ko'rinardi |
| Jurnal (`MemberStart`) | O'tgan oylardagi davomat/baho "a'zolikdan oldingi" deb bloklangan ko'rinardi |
| Oqim grafiklari | "Shu oyda aktivlashdi" hodisasi keyingi oyga ko'chib ketardi |

## 2. YECHIM — `PastPeriods`

`StudentGroup.PastPeriods` — YOPILGAN davrlar ro'yxati, har biri **`"boshlanish|tugash"`**
(ikkalasi ISO `YYYY-MM-DD`).

⚠️ **JORIY davr bu ro'yxatda YO'Q** — u avvalgidek `ActivatedAt`/`FrozenAt` juftligida. Ya'ni
bitta a'zolikning holati IKKI joyda emas: ro'yxat faqat TARIX.

⚠️ **Mavjud qatorlarda BO'SH — backfill ATAYIN QILINMAGAN.** Shuning uchun bu maydon eski
ma'lumot uchun hech narsani o'zgartirmaydi (barcha yangi funksiyalar bo'sh ro'yxatda AYNAN eski
natijani beradi — `FinanceLogicTests` da qulflangan). Tarixni `MonthlyCharge` va `AuditLog` dan
qisman tiklash mumkin, lekin bu **alohida ish**.

## 3. QATOR QAYERDA QO'SHILADI — faqat 4 joy

`MembershipLifecycle.ClosePeriod(sg, fallbackEnd)` — `ActivatedAt` **ustidan yozilishidan yoki
tozalanishidan OLDIN**:

| Joy | Nega |
|---|---|
| `ActivateCoreAsync` | `ActivatedAt = date` (asosiy holat: muzlatib qayta aktivlashtirish) |
| `ReturnToTrial` | `ActivatedAt = ""` |
| `AddMember` (mavjudni qayta qo'shish) | `ActivatedAt = ""`. ⚠️ `LeftAt = null` dan OLDIN — aks holda tugash sanasi yo'qolardi |
| `TransferMember` — MAQSAD tomoni | `ActivatedAt = activateDate` (o'quvchi bu guruhda ilgari ham o'qigan bo'lishi mumkin). ⚠️ `LeftAt = null` dan OLDIN — bu yerda ham |

⚠️ **Muzlatish, chiqarish, guruh yopish va sertifikat bilan tugatishda QO'SHILMAYDI** — u yerda
joriy davr `ActivatedAt` + `FrozenAt`/`LeftAt` bilan hamon o'qiladi. Qator faqat ma'lumot
YO'QOLADIGAN joyda yoziladi.

⚠️ **TARTIB — eng ko'p yo'l qo'yiladigan xato.** `ClosePeriod` `LeftAt`/`FrozenAt`/`ActivatedAt`
tozalanishidan OLDIN turishi SHART. Kech qolsa tugash sanasi yo'qoladi va davr `fallbackEnd`
gacha (ya'ni bugungacha) cho'zilib ketadi — o'quvchi guruhda **umuman bo'lmagan** oylarga qarz
yoziladi va maosh/bonus/analitika ham shu yolg'on davrga tayanadi. Aynan shu xato
`TransferMember` da bo'lgan va revyuda tutilgan.

Tugash sanasi: **`FrozenAt` → `LeftAt` → `fallbackEnd`**. `ClosePeriod` idempotent (bir xil qator
ikki marta qo'shilmaydi) va teskari oraliq yasamaydi (orqaga sanalgan amalda davr bir kunlik
bo'lib yopiladi — aks holda hech bir oy unga tushmasdi). Chegara `MaxPastPeriods = 50`.

⚠️ **FAQAT HAQIQIY SANALAR yoziladi** (`DateOnly.TryParse`). Aktivlashtirish endpointi `req.Date`
ni validatsiya qilmaydi, ya'ni `ActivatedAt` ga `"2026-13-99"` tushishi mumkin; bunday qator
`TuitionService.MonthRange` da CHEKSIZ siklga olib borardi (`NextMonth` oyni normallashtirmaydi:
13 → 14 → … → 100, ordinal solishtiruvda hech qachon tugamaydi). Ikkinchi qatlam — `MonthRange`
dagi `MaxRangeMonths = 1200` chegarasi.

## 4. IKKI XIL "davr ichida" — ARALASHTIRMANG

| Funksiya | Chegaralar | Kim ishlatadi |
|---|---|---|
| `MonthInPeriod` | **KIRADI** (aktivlashtirish oyi ham, muzlatish oyi ham) | `BillableInMonth` — "shu oyda pullik edimi" |
| `AccruableInPeriod` | **KIRMAYDI** | `TuitionService.AccruableMonth` — "hisob YOZILADIMI" |

⚠️ Farq ATAYIN: aktivlashtirish va muzlatish oylari **QISMAN** hisob bilan o'sha paytning o'zida
yozilgan (`ChargeActivationProrateAsync` / `ChargeFreezeProrateAsync`). Accrual ularni qayta
yozsa, oy IKKI marta hisoblangan bo'lardi.

## 5. `BillableInMonth` — yopilgan davrlar "trial" dan OLDIN tekshiriladi

```csharp
if (pastPeriods is not null) foreach (...) if (MonthInPeriod(p, month)) return true;
if (status == "trial") return false;   // ← keyin
```

⚠️ Tartib muhim: `ReturnToTrial` a'zolikni bugun `trial` ga qaytarishi mumkin, lekin bu
o'tmishda pul to'lanmagan degani EMAS.

⚠️ **Nusxa ko'chirmang.** Ilgari `GroupBalanceService` va `CourseFinanceReport` da mantiq qayta
yozilgan edi; ular endi `MembershipLifecycle` ga DELEGAT qiladi. Nusxa qaytarilsa, u jimgina
eski xatti-harakatda qolib, hisobot maosh va bonusdan ajralib ketadi.

## 6. RETROAKTIV HISOB — nega XAVFSIZ

`AccrueDue` har startupda va har 12 soatda **butun tarixni** qayta skanerlaydi, ya'ni accrual
shartini kengaytirish o'z-o'zicha ommaviy kutilmagan qarz (va ota-onalarga avto-SMS to'lqini)
berishi mumkin edi. Uchta narsa buni ushlab turadi:

1. **Backfill YO'Q** — mavjud qatorlarda `PastPeriods` bo'sh, ya'ni deploy paytida bironta ham
   yangi hisob yozilmaydi (test: `AccrueMonth_PastPeriods_BOSH_bolsa_bironta_ham_yangi_hisob_YOZILMAYDI`).
2. **Yopilgan davrning O'ZI tombstone** — davr muzlatish sanasida yopilgani uchun orqaga sanalgan
   muzlatishda `PurgeChargesAfterMonthAsync` o'chirgan oylar davrdan TASHQARIDA qoladi va qayta
   tirilmaydi (test: `AccruableMonth_orqaga_sanalgan_MUZLATISHDA_ochirilgan_oylar_TIRILMAYDI`).

   ⚠️ Bu **bitta** davr uchun o'z-o'zidan chiqadi, IKKI davr uchun esa YO'Q: purge
   `(o'quvchi, guruh)` ning muzlatish oyidan keyingi BARCHA hisoblarini kesadi — ESKI yopilgan
   davr ichidagilarni ham. Shuning uchun `MembershipBilling.SettleFreezeAsync` a'zolik berilganda
   `MembershipLifecycle.TruncatePastPeriodsAfter` bilan davrlarni ham o'sha sanaga QIRQADI: undan
   keyin cho'zilgani qisqartiriladi, butunlay keyin boshlangani o'chiriladi. Tarix hisob bilan
   izchil qoladi — "hisobi bekor qilingan oy" hech qachon "pullik davr ichida" bo'lib qolmaydi.
3. **`Status == "active" && IsActive` sharti TEGILMAGAN** — guruhdan chiqib ketgan o'quvchiga
   orqaga qarab qarz yozilmaydi.

`AccrueCatchUpAsync` ga `membership` berilsa (aktivlashtirish yo'li) o'sha bo'shliqlar 12 soat
kutmasdan darhol to'ldiriladi; `null` bo'lsa eski xatti-harakat.

## 7. Yangi ko'rsatkich qo'shsangiz

- **"Shu oyda aktivlashdi"** — `ActivatedAt` ga QARAMANG, `MembershipLifecycle.ActivatedInMonth`
  yoki `FirstActivatedAt` dan foydalaning.
- **"Shu oyda pullik edimi"** — `BillableInMonth` (entity overload'ini chaqiring, u tarixni O'ZI
  uzatadi).
- **PROYEKSIYA** (`.Select(...)`) qilsangiz — `PastPeriods` ni ham qo'shing, aks holda tarix
  jimgina yo'qoladi. Hozircha bunday joy bitta: `CourseAnalyticsController`.
- Davr qatorini QO'LDA parse qilmang — `TryParsePeriod` (buzuq yozuv shu yerda filtrlanadi).

## 7.5. QABUL QILINGAN xatti-harakat (xato emas, lekin bilib qo'ying)

- **Retroaktiv oy BUGUNGI narx va BUGUNGI chegirma bilan yoziladi** (`AccrueOne` →
  `Group.MonthlyFee` + `DiscountForMonth`). Loyihada tarixiy narxlar jadvali YO'Q, ya'ni bu
  catch-up ning boshidanoq shunday edi; `PastPeriods` uni chuqurroq oylarga yoygan xolos. Guruh
  narxi ko'tarilgandan keyin qayta aktivlashtirish o'tgan yilgi bo'shliqni YANGI narxda yozadi.
- **Xato aktivlashtirishni «sinovga qaytarish» bilan bekor qilib bo'lmaydi.** Aktivlashtirib,
  o'sha kuni sinovga qaytarilsa bir kunlik davr yoziladi va `MonthInPeriod` chegaralarni
  kiritgani uchun o'sha oy "pullik" bo'lib qoladi. Bu **izchil**: `ChargeActivationProrateAsync`
  yozgan qisman hisobni `ReturnToTrial` avval ham o'chirmasdi. Davrni UI'dan olib tashlash yo'li
  yo'q — kerak bo'lsa hisob qatori qo'lda tahrirlanadi (`PUT charges/{month}`).

## 8. Hali TUZATILMAGAN (ma'lum bo'shliq)

Oqim grafiklaridagi **"muzlatildi"** va **"ketdi"** sanoqlari (`GroupSnapshotBuilder`,
`TeacherSnapshotBuilder`, `TeacherActivityReport`) hamon joriy `FrozenAt`/`LeftAt` ga qaraydi.
Yopilgan davrning TUGASHI muzlatishmi, chiqishmi yoki sinovga qaytarishmi — qatordan bilib
bo'lmaydi, shuning uchun ularni sanoqqa qo'shish taxminga aylanardi. Kerak bo'lsa davr qatoriga
sabab qo'shilsin (bu qoidaga tayanadi, uni almashtirmaydi).

## 9. `AddStudent` a'zoligi endi SANA bilan yoziladi (2026-09-09)

⚠️ `StudentsController.AddStudent` (o'quvchi yaratish · CSV import · `Update` orqali guruh
biriktirish) ilgari `active`/`frozen` a'zolikni yaratar, lekin `ActivatedAt`/`FrozenAt` ni
**bo'sh** qoldirardi. Oqibati ikki tomonlama edi:

- `BillableInMonth` bo'sh `ActivatedAt` ni "boshidan beri pullik" deb o'qir edi → maosh va
  guruh balansi shu a'zolikka ULUSH ajratardi;
- `AccruableMonth` esa haqiqiy sana talab qilgani uchun **hisob umuman yozilmasdi** — ya'ni
  o'quvchi "aktiv" bo'lib turib hech qachon oylik olmasdi.

Endi ikkalasi ham to'g'rilandi: `ActivatedAt = enrollment` (yoki `FrozenAt = enrollment`), va
`BillableInMonth` haqiqiy sanani TALAB qiladi.

⚠️ **ORQAGA SANALGAN QABUL SANASI QIRQILADI** — `MembershipLifecycle.ActivationStartForCreate`:
`ActivatedAt` = qabul sanasi, lekin **joriy oy boshidan orqaga o'tmaydi** (buzuq/bo'sh sana ham
oy boshiga tushadi).

Sabab: bu yo'l qisman oylik (prorate) YOZMAYDI va bo'shliqlarni ongli to'ldirmaydi — u shunchaki
"shu o'quvchi shu guruhda" deb qayd qiladi. Orqaga sanalgan qiymat qolsa, `AccrueDue` ning 12
soatlik skaneri o'sha oylarni pullik deb topib qarz yozar va to'lov eslatmasi SMS'i ota-onalarga
ketardi — **ommaviy importda bu bir zumda yuzlab yolg'on qarz** degani. §6 aynan shu xavfdan
qo'riqlaydi.

⚠️ Haqiqatan o'tmishdagi sanadan aktivlashtirish kerak bo'lsa — **«Aktivlashtirish» oynasi**:
u qisman oylikni to'g'ri yozadi va catch-up ni ONGLI chaqiradi (`AccrueCatchUpAsync`).

Mavjud qatorlar TEGILMAGAN (backfill YO'Q) — deploy paytida bironta ham yangi hisob paydo
bo'lmaydi. Testlar: `FinanceLogicTests` → `ActivationStart_*` (4 ta).
