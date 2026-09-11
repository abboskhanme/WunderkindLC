# O'quvchini ushlab turish bonusi (Student Retention Bonus) — TAHLIL VA REJA

> **HOLAT: ✅ BAJARILDI (2026-07-30)** — bosqichlar 0–6 va ikkinchi bosqich talablari (11b)
> yozildi va sinovdan o'tdi. Bosqich 7 (`SalaryLedger` ga ulash) qaror #3 bo'yicha ATAYIN qilinmadi.
>
> ⚠️ **Sikl kaliti — `(o'quvchi × FAN)`**, o'quvchi darajasida emas (5.1). Hujjatning eski
> nusxalarida "o'quvchi darajasida" deb yozilgan bo'lsa — u eskirgan.
>
> Sana: 2026-07-30 · Tahlil manbai: kod bazasining o'zi (havolalar `fayl:satr` ko'rinishida).
>
> ### Qayerda ishlaydi
> - **Ptichka:** o'quvchi formasi → «Ushlab turish bonusi» bo'limi (ptichka + sanoq boshlanadigan oy)
> - **Hisobot:** O'quvchilar → **Bonus hisoboti** (`/admin/students/bonus`) — HAR FAN uchun alohida
>   qator, oylik kataklar (✅ ⏳ 📄 ❄️ 🚪), har katak ostida O'SHA OYDAGI o'qituvchi, filtrlar,
>   Excel, sozlamalar
> - **Bonus berish:** hisobotdagi «Bonus berish» tugmasi (`finance` yozish ruxsati) — taqsimot
>   avtomatik hisoblanadi va qo'lda tahrirlanadi
> - **Moliya → «Bonus»:** faqat HISOBOT (o'qituvchilar kesimi, oylar kesimi, batafsil, Excel)
> - **O'qituvchi:** profilida **«Bonus»** tabi — «Yo'ldagilar» + «Berilgan bonuslar»;
>   o'qituvchi ilovasida Maosh sahifasi ostida shu ikki bo'lim
>   (maosh raqamlariga QO'SHILMAYDI)
>
> ### Asosiy fayllar
> `RetentionBonusService.cs` (butun mantiq) · `GroupTeacherHistory.cs` (o'qituvchi tarixi) ·
> `RetentionBonusController.cs` · `RetentionBonusPage.tsx` + `GiveRetentionBonusModal.tsx` +
> `RetentionSettingsModal.tsx` · `TeacherBonusPanel.tsx` ·
> migratsiyalar `AddGroupTeacherHistory`, `RetentionBonusSystem`

---

## 1. Maqsad

O'quvchini markazda uzoq muddat ushlab turgan o'qituvchi(lar)ni rag'batlantirish. O'quvchi belgilangan
muddat (default **6 oy**) davomida uzluksiz o'qib, to'lovlarini qilsa — uni o'qitgan o'qituvchi(lar)ga
bonus ajratiladi. O'quvchi bu muddat ichida o'qituvchi/guruh almashtirgan bo'lsa, bonus **o'qigan oylar
nisbatida** bo'linadi.

**Asosiy tamoyil:** bonus *ushlab turgani* uchun beriladi, *o'z vaqtida to'laganligi* uchun emas.
Shu sabab kechikkan to'lov siklni buzmaydi (pastda 5-bo'limga qarang).

---

## 2. Tanlangan yondashuv: QO'LDA PTICHKA (avtomatik aniqlash EMAS)

Ikki variant ko'rib chiqildi:

| | Avtomatik (rad etildi) | **Qo'lda ptichka (tanlandi)** |
|---|---|---|
| Kim ishtirok etadi | Tizim "yangi o'quvchi" ni o'zi topadi | Admin o'quvchi formasida ptichka qo'yadi |
| Bonus summasi | Qoidada oldindan belgilanadi | **Berish paytida admin kiritadi** |
| Qoida jadvali | `RetentionBonusRule` entity kerak | ❌ kerak emas — 3 ta sozlama `CenterMeta` da |
| Fon xizmati | `BackgroundService` (12 soatda bir) kerak | ❌ **kerak emas** — jadval jonli hisoblanadi |
| Retroaktiv xavf | Ishga tushgan kuni o'nlab eski o'quvchiga bonus chiqadi | ❌ yo'q — ptichkasiz o'quvchi ko'rinmaydi |

Sabab: qo'lda ptichka kamroq kod, kamroq xavf va boshqarish tushunarli. Markaz egasi kimga bonus
tizimi tegishli ekanini o'zi hal qiladi.

---

## 3. Mavjud tizimda nima bor (tayanch nuqtalar)

Kerakli ma'lumotlarning deyarli hammasi allaqachon bazada bor — yangi "kuzatuv" mexanizmi kerak emas.

### 3.1. O'quvchi a'zoligi tarixi — BOR va yetarli
`WunderkindLC.Domain/Entities.cs:503` — `StudentGroup`:
`StudentId, GroupId, JoinedAt, LeftAt?, IsActive, Status ("trial"|"active"|"frozen"), ActivatedAt, FrozenAt, RecordedAt`

Bu bilan **istalgan oy uchun** o'quvchi qaysi guruh(lar)da pullik a'zo bo'lganini aniq tiklash mumkin.
Mantiq allaqachon yozilgan — `SalaryLedger.cs:283`:

```csharp
static bool BillableInMonth(StudentGroup m, string month)
{
    if (m.Status == "trial") return false;
    var actOk = m.ActivatedAt.Length < 7 || string.CompareOrdinal(month, m.ActivatedAt[..7]) >= 0;
    var frzOk = m.FrozenAt.Length   < 7 || string.CompareOrdinal(month, m.FrozenAt[..7])   <= 0;
    return actOk && frzOk;
}
```

> ⚠️ Bonus xizmati **AYNAN shu funksiyani** ishlatishi shart (umumiy joyga chiqariladi). Aks holda
> maosh bir oyni "pullik" deb, bonus esa "pullik emas" deb hisoblab, raqamlar bir-biriga to'g'ri kelmaydi.

### 3.2. To'lov intizomi — BOR
- `MonthlyCharge` (`Entities.cs:894`) — per-guruh, per-oy: `Amount`, `Discount`, `Locked`
- `FinanceTransaction` (`Entities.cs:914`) — `StudentId`, `GroupId`, `Month`, `Category="tuition"`,
  vozvrat `Category="refund"`
- Tayyor hisoblagichlar: `StudentGroupLedger.cs:19` (oyma-oy hisob/to'lov), `GroupBalanceService.cs:29`
  (per-guruh balans)

### 3.3. Maosh — oyma-oy, JONLI hisoblanadi
`SalaryLedger.cs:33` `BuildAsync` → `MonthSalaryDto(Month, Expected, Paid, Remaining, Status, BaseExpected, Deduction, …)`.
Maosh hech qayerda saqlanmaydi — har so'rovda qayta hisoblanadi. Maosh *to'lovi* esa
`FinanceTransaction(expense, "salary", TeacherId)`.

**Uchta iste'molchi, uchalasi ham bitta funksiyani chaqiradi:**
- `TeachersController.cs:414` → admin modal (`TeacherSalaryDetailModal.tsx`)
- `FinanceController.cs:288` `salary-report` → Moliya → O'qituvchilar jadvali
- `TeacherPortalController.cs:228` → **o'qituvchi ilovasi** (`teacher/salary/SalaryPage.tsx`)

→ Bonusni `SalaryLedger` ga qo'shsak, uchala ekranda ham avtomatik paydo bo'ladi.

### 3.4. Orqaga sanashdan himoya — BOR
`StudentGroup.RecordedAt` (`Entities.cs:528`) — *"HAQIQATDA tizimga kiritilgan sana, ORQAGA SANALMAYDI"*.
Har doim `AppClock.Today` bilan yoziladi: `ClassesController.cs:401,408` (guruhga qo'shishda) va
`:522` (aktivlashtirishda). Admin `ActivatedAt` ni orqaga sanay oladi, `RecordedAt` ni esa yo'q.

### 3.5. Fon xizmati namunasi (kerak bo'lsa)
`TuitionAccrualService.cs` — `BackgroundService`, startupda + har 12 soatda. **Bu rejada ishlatilmaydi**,
lekin kelajakda "6 oy to'ldi" Telegram xabarnomasi kerak bo'lsa shu shablon olinadi.

### 3.6. Topilma: `Teacher.BonusPct` — O'LIK MAYDON
`Entities.cs:353` da "Ustama foizi (%)" bor, lekin butun `Server`/`Application`/`Client` bo'ylab
**hech qayerda o'qilmaydi**. `TeacherSalaryCalc.WithBonus()` ni ham hech kim chaqirmaydi (dars jadvali
olib tashlanganda o'lgan). Yangi maydonlar `Retention*` prefiksi bilan nomlanadi — chalkashmasin.

---

## 4. Yagona jiddiy to'siq: guruhning o'qituvchi TARIXI yo'q

**Nima bor:** `Group.TeacherId` (`Entities.cs:472`) — faqat **HOZIRGI** o'qituvchi.
**Nima yo'q:** kim qachondan qachongacha o'qitgani.

Tekshirildi — tarix hech qayerda saqlanmaydi:
- `LessonNote` (`Entities.cs:721`) — `ClassId, SubjectId, Date, Topic, Conducted` — **`TeacherId` YO'Q**
- `JournalEntry` (`Entities.cs:695`) — ham yo'q
- `ClassesController.cs:158` — `cls.TeacherId = p.TeacherId ?? ""` — eski qiymat ustiga yoziladi

**Oqibati:** guruhda A o'qituvchi 4 oy ishlab, keyin B kelsa — bonus to'liq B ga ketadi.

**Yechim (✅ BAJARILDI — migratsiya `AddGroupTeacherHistory`):** `GroupTeacherAssignment` entity va
`GroupTeacherHistory` xizmati (`Application/Services/`). Yozish YAGONA joyda —
`GroupTeacherHistory.AssignAsync`, u `ClassesController.Create` va `Update` da chaqiriladi
(eski ochiq qator yopiladi, yangisi ochiladi). O'qish — `LoadAsync` (ommaviy, N+1 yo'q) va
`TeacherAtMonth(history, "YYYY-MM")`.

**❗ Halol ogohlantirish:** migratsiyadagi backfill eski guruhlar uchun bitta "ochiq" qator yaratadi,
lekin **o'tmishdagi almashuvlarni tiklay olmaydi** — bunday ma'lumot bazada yo'q edi.

`FromDate` uchun eng oqilona taxmin olinadi: `Group.StartDate` → yo'q bo'lsa shu guruhga eng erta
qo'shilgan o'quvchining `JoinedAt` → u ham yo'q bo'lsa migratsiya kuni. `CreatedBy = "migratsiya"`
— bu qatorlar **taxmin** ekani ko'rinib turadi.

> Nega bugungi sana emas: tarix faqat bugundan boshlansa, ertaga o'qituvchi almashgan zahoti
> o'tmishdagi oylar YANGI o'qituvchiga yozilib qolardi (`TeacherAtMonth` topa olmay
> `Group.TeacherId` ga fallback qiladi). Guruh boshlanishidan yozish — noto'g'ri emas,
> shunchaki aniqligi cheklangan.

> Yumshatuvchi omil: bonus summasi va taqsimoti berish paytida **qo'lda tahrirlanadi**, ya'ni admin
> noto'g'ri taqsimotni ko'rsa tuzata oladi. Shu sabab bu to'siq bloklovchi emas.

**Shuning uchun Bosqich 0 (tarixni yozishni boshlash) BIRINCHI bajariladi** — kechikkan har kun
qayta tiklab bo'lmaydigan ma'lumot yo'qotadi.

---

## 5. Oy holatlari va sikl qoidalari ⭐

Bu bo'lim tizimning yuragi. Besh xil holat bor va ular **bir xil emas**.

### 5.1. Sikl kaliti: (O'QUVCHI × FAN) — guruh EMAS, o'quvchining o'zi ham EMAS

> **⚠️ Bu qoida 2026-07-30 da o'zgargan.** Ilgari sikl o'quvchi darajasida edi. Markaz egasi:
> *"agar u 2 ta fanga kirsa yoki undan ko'piga kirsa, hamma fani uchun alohida hisoblanishi kerak"*.

Qator kaliti — **`(StudentId, CourseId)`**. O'quvchi Ingliz va Matematikaga qatnasa, hisobotda
**ikki qator**: har birining o'z sanog'i, o'z davri, o'z bonusi. Ingliz uchun bonus berilishi
Matematika sanog'iga **umuman ta'sir qilmaydi**.

**Nega kalit GURUH emas, KURS?** O'quvchi guruh almashtirsa — `TransferMember`
(`ClassesController.cs`) eski a'zolikni **muzlatadi** va yangisini ochadi. Ya'ni "muzlatilgan"
belgisi ikki xil narsani anglatishi mumkin:

1. Haqiqatan ta'tilga chiqdi
2. Shunchaki boshqa guruhga o'tdi (markazdan **ketmadi**)

Ingliz A1 → Ingliz A2 ga o'tgan o'quvchi o'sha fan bo'yicha markazda **qoldi** — o'qituvchi uni
ushlab turdi. Shuning uchun savol *"bu a'zolik faolmi?"* emas, *"shu oyda o'quvchining SHU FAN
bo'yicha kamida bitta pullik a'zoligi bormi?"* bo'ladi.

`CourseId` = `Group.CourseId` (Subject id); kursi biriktirilmagan eski guruhda — guruh id'si.

> **Oqibat (ataylab):** o'quvchi Ingliz'ni tashlab Matematikada qolsa, Ingliz sikli
> `MaxGapMonths` dan keyin **uziladi**, Matematika esa davom etadi. Ingliz o'qituvchisi uni
> o'z fanida ushlab qololmadi.

### 5.2. Holatlar jadvali

Barchasi **shu fan** kesimida baholanadi.

| Holat | Aniqlash | Sanoqqa ta'siri |
|---|---|---|
| ✅ **To'liq** | pullik a'zolik bor + hisob yozilgan + qarz yo'q | **+1** |
| ⏳ **Qarzdor** | pullik a'zolik bor + qarz bor | **+0**, sikl **uzilmaydi**, pauza ham emas |
| 📄 **Hisob yozilmagan** | pullik a'zolik bor, lekin `MonthlyCharge` qatori UMUMAN yo'q | **+0**, sikl **uzilmaydi** |
| ❄️ **Muzlatilgan** | shu fandagi hamma a'zolik `frozen` | **PAUZA** — oyna cho'ziladi |
| 🚪 **Ketgan** | shu fan bo'yicha pullik a'zolik yo'q | **PAUZA**, `MaxGapMonths` dan oshsa → **UZILDI** |
| 🔴 **Arxivlangan** | `Student.IsArchived == true` | **DARHOL UZILDI** |

Bitta oy uchun hisob:
```
AKTIV?    → shu oyda SHU FANDA billable a'zolik bormi   ← MembershipLifecycle.BillableInMonth
HISOB BOR? → shu (o'quvchi, fan, oy) uchun MonthlyCharge qatori bormi
TO'LADI?  → Σ MonthlyCharge(Amount − Discount) − Σ to'langan ≤ 0
             to'lov: FinanceTransaction.Month bo'yicha, vozvrat (Category="refund") ayiriladi
             TEGLANMAGAN (GroupId=null) hisob/to'lov guruhlar MonthlyFee nisbatida taqsimlanadi
             (SalaryLedger / GroupBalanceService bilan BIR XIL konvensiya)
```

#### 📄 «Hisob yozilmagan» nega alohida holat

Ilgari `charged == 0 && paid == 0` → ✅ bo'lib **sanoqqa kirardi**. Markaz egasi buni ko'rdi:
hisob yozilmagani uchun tizim o'quvchini "to'lagan" deb ko'rsatdi va bonus asossiz yaqinlashdi.

Endi farq aniq: **qator YO'Q** → 📄 (sanalmaydi) · **qator BOR-u summasi nol** (100% chegirma)
→ ✅ (sanaladi). Hisob paydo bo'lgach jadval o'z-o'zidan tuzaladi — hech narsa saqlanmagani uchun.

> Bu holat asosan `TuitionService` dagi tuzatishdan **oldingi** ma'lumotda uchraydi: orqaga
> sanalgan aktivlashtirishda oraliq oylar yozilmay qolardi (`AccrueCatchUpAsync` bilan tuzatildi).

### 5.3. Nega qarz siklni uzmaydi

Jadval **jonli** hisoblanadi (hech narsa saqlanmaydi). Demak ota-ona sentabr to'lovini yanvarda
to'lasa — sentabr katagi **o'z-o'zidan ✅ ga aylanadi**. Kechikkan to'lov bonusni yo'qotmaydi, faqat
tugma chiqishini kechiktiradi. Buning uchun qo'shimcha kod kerak emas.

```
Yanvargacha:        ✅  ⏳  ✅  ✅  ✅  ✅   → 5/6, tugma yo'q
09-oy to'langach:   ✅  ✅  ✅  ✅  ✅  ✅   → 6/6, tugma paydo bo'ladi
```

### 5.4. Nega ta'til siklni uzmaydi (lekin cheklov bor)

O'qituvchi o'quvchini yo'qotmadi — vaqtincha to'xtatdi. Lekin cheklovsiz qoldirib bo'lmaydi: 8 oy
muzlab yotgan o'quvchi ham bonus keltirsa, tizimning ma'nosi qolmaydi.

**Ta'tilga chiqdi, qaytdi → bonus BERILADI (sikl cho'ziladi):**
```
Oy      08  09  10  11  12  01  02  03
Holat   ✅  ✅  ✅  ❄️  ❄️  ✅  ✅  ✅
Sanoq   1   2   3   ·   ·   4   5   6      → 6/6 ✅  bonus 2027-03 da
```

**Butunlay ketdi → sikl UZILADI:**
```
Oy      08  09  10  11  12  01
Holat   ✅  ✅  ✅  🚪  🚪  🚪
Sanoq   1   2   3   ·   ·   ·              → gap 3 > 2  →  ❌ UZILDI
```

**Arxivlash — aniq signal.** Admin o'quvchini arxivga o'tkazsa, bu "ketdi" degani; 2 oy kutib
o'tirilmaydi, sikl darhol uziladi.

### 5.5. Uzilgandan keyin

Qator ro'yxatda **qoladi**: `❌ Uzildi · 2026-11 · sabab: 3 oy a'zolik yo'q`. Yo'qolib ketmaydi —
statistika va "nega bonus bermadik" savoliga javob uchun kerak.
O'quvchi qaytsa — **«Qayta boshlash»** tugmasi yangi boshlanish oyi bilan yangi sikl ochadi
(FAQAT o'sha fan uchun); avvalgi sikl tarixda qoladi.

### 5.6. Takroriy bonus qoidasi ⭐

> Markaz egasi (2026-07-30): *"bir marta bonus olgan o'quvchi orqali olgan o'qituvchiga qayta
> shu o'quvchi orqali boshqa bonus berilmaydi"*.

**`(TeacherId, StudentId)` juftligi — umr bo'yi BITTA bonus.** Qayerda qo'llanadi:

- **Taqsimotda:** bloklangan o'qituvchi ro'yxatda **ko'rinadi** (`AlreadyAwarded = true`, oylari
  ham ko'rinadi), lekin `Amount = 0`; uning vazni qolgan o'qituvchilarga qayta taqsimlanadi va
  yig'indi baribir jami summaga aniq teng chiqadi.
- **Berishda:** bloklangan o'qituvchiga ulush yuborilsa — 400, ism bilan aniq xabar.
- **Qator holatida:** sanoq to'ldi, lekin barcha o'qituvchi bloklangan bo'lsa — `ready` emas,
  **`blocked`**. «Bonus berish» tugmasi ko'rinmaydi. `ReadyCount` ga ham kirmaydi.

DIQQAT: blok **o'quvchi darajasida**, fan darajasida emas. Karimov Ingliz orqali bonus olgan
bo'lsa, o'sha o'quvchining Matematika siklidan ham unga bonus tegmaydi.

### 5.7. Bekor qilish — qaytarib bo'lmaydi

> Markaz egasi: *"bekor qilinganda bekor qilingan deb qo'yilishi kerak va qayta bonus
> berilmasligi kerak"*.

- Yozuv **o'chirilmaydi** — `Status = "cancelled"` bo'lib tarixda qoladi (sabab bilan).
- Sanoq **QAYTARILMAYDI** (ilgari davr boshiga qaytarardi — bu xato edi).
- Bekor qilingan bonus 5.6 dagi **blokni saqlab qoladi** — aks holda "bekor qil → qayta ber"
  yo'li ochiq qolardi.
- O'qituvchining JAMI summasiga kirmaydi, lekin ro'yxatda "bekor qilingan" belgisi bilan turadi.
- Yon ta'sir: bekor qilingan bonus ham sikl raqamini band qiladi — "3-sikl" endi
  "3-urinish" ma'nosini beradi (unikal indeks `(StudentId, CourseId, CycleNo)` shuni talab qiladi).

---

## 6. Ma'lumot modeli

```csharp
// 1) Student (mavjud entity'ga 2 maydon) — Entities.cs:178
public bool   RetentionBonus          { get; set; }          // ptichka
public string RetentionBonusStartMonth { get; set; } = "";   // "2026-08" — admin QO'LDA kiritadi (qaror #1)
                                                             // bo'sh = "hali boshlanmagan"

// 2) Berilgan bonus (bitta FAN bo'yicha bitta sikl)
public class RetentionBonusAward
{
    string Id;
    string StudentId;
    string StudentName;        // SNAPSHOT
    string CourseId;           // FAN — sikl kaliti (5.1-bo'lim)
    string CourseName;         // SNAPSHOT
    int    CycleNo;            // 1, 2, 3 … (bekor qilingani ham raqam oladi — 5.7)
    string PeriodFrom;         // "2026-08"
    string PeriodTo;           // "2027-01"
    decimal TotalAmount;
    string Status;             // "given" | "cancelled"
    string CancelReason;
    DateTime CreatedAt;
    string GivenBy;            // admin F.I.Sh
    string Note;
}
// UNIQUE INDEX (StudentId, CourseId, CycleNo)  ← bir fan siklida takroriy bonus mumkin emas

// 3) O'qituvchilar ulushi (bir sikl → N qator)
public class RetentionBonusShare
{
    string Id;
    string AwardId;
    string TeacherId;
    string TeacherName;        // SNAPSHOT — o'qituvchi o'chirilsa ham tarix o'qiladi
    decimal Months;            // masalan 2.0 (nechta oy shu o'qituvchida)
    decimal Amount;
}
// INDEX (AwardId), INDEX (TeacherId)
// TeacherId+award orqali (o'qituvchi, o'quvchi) bloki hisoblanadi — 5.6-bo'lim

// 3b) Sanoq holati — HAR FAN uchun alohida (bonus tizimiga KIRISH nuqtasi ham shu)
public class RetentionBonusTrack
{
    string Id;
    string StudentId;
    string CourseId;
    string StartMonth;         // "2026-08" — shu fanning JORIY sikli qaysi oydan
    bool   Enabled;            // shu fan bo'yicha bonus hisoblanadimi (aktivlashtirish ptichkasi)
    string UpdatedBy;
    DateTime UpdatedAt;
}
// UNIQUE INDEX (StudentId, CourseId)
// KIM hisobotga kiradi: track bo'lsa — uning Enabled'i; bo'lmasa eski Student.RetentionBonus
// (orqaga moslik — o'quvchi formasidagi ptichka olib tashlanganidan keyin ham eski ma'lumot
// yo'qolmasin). Track StartMonth ham Student.RetentionBonusStartMonth dan ustun turadi.
// Bonus BERILGANDA track = NextMonth(PeriodTo). «Qayta boshlash» ham shu qatorni yozadi.
// DIQQAT: shundan keyin o'quvchi formasidagi oyni o'zgartirish O'SHA FANGA ta'sir qilmaydi —
// oyni ko'chirish uchun «Qayta boshlash» ishlatiladi.

// 4) Guruhning o'qituvchi TARIXI (4-bo'limdagi to'siq yechimi) — ✅ BAJARILDI
public class GroupTeacherAssignment
{
    string  Id;
    string  GroupId;
    string  TeacherId;
    string  FromDate;          // "YYYY-MM-DD" — ORQAGA SANALMAYDI (AppClock.Today)
    string? ToDate;            // null = hozirgi o'qituvchi
    string  CreatedBy;         // admin F.I.Sh yoki "migratsiya" (backfill)
}
// INDEX (GroupId, FromDate), INDEX (TeacherId)
// Invariant: bir guruhda bir vaqtda ko'pi bilan BITTA ochiq (ToDate == null) qator

// 5) CenterMeta (Entities.cs:1028) — 3 ta sozlama
public int     RetentionMonthsRequired { get; set; } = 6;
public int     RetentionMaxGapMonths   { get; set; } = 2;
public decimal RetentionDefaultAmount  { get; set; }   // modal'ga oldindan to'ladi
```

**Migratsiyalar:** `AddGroupTeacherHistory` → `RetentionBonusSystem` → `RetentionPerCourse`
(oxirgisi: `Award` ga `CourseId`/`CourseName`, yangi unikal indeks, `RetentionBonusTracks` jadvali).

### Nega oylik ptichkalar saqlanmaydi

Superadmin `MonthlyCharge`ni tahrirlashi, to'lov tuzatilishi yoki vozvrat qilinishi mumkin. Saqlansa —
jadval haqiqatdan uzilib qoladi. Shu sabab **faqat yakuniy bonus saqlanadi**, oylik holatlar har safar
qaytadan hisoblanadi (maosh ham aynan shunday ishlaydi — `SalaryLedger`).

---

## 7. Taqsimot algoritmi

```
Sikl oynasi: SHU FAN bo'yicha hisobga kirgan oylar [m1 … m6]

Har oy m uchun:
  1. O'quvchining m oyidagi SHU FANDAGI billable a'zoliklari  ← MembershipLifecycle.BillableInMonth
  2. Har a'zolik → guruh → O'SHA OYDAGI o'qituvchi            ← GroupTeacherAssignment
     (topilmasa fallback: Group.TeacherId)
  3. Oy vazni 1.0 — a'zoliklar orasida MonthlyFee nisbatida bo'linadi
     (teglanmagan to'lovni taqsimlash bilan BIR XIL konvensiya — SalaryLedger)
  4. weight[teacher] += ulush

BLOKLANGAN o'qituvchilar (5.6) maxrajdan CHIQARILADI:
  Har o'qituvchi summasi = TotalAmount × weight[t] / Σ weight[ochiq o'qituvchilar]
  Bloklanganga 0 (lekin ro'yxatda oylari bilan ko'rinadi — admin nega tushib qolganini bilsin)

Yaxlitlash qoldig'i eng katta ulushli o'qituvchiga qo'shiladi (yig'indi aniq teng chiqsin)
```

**Misol 1 — ketma-ket:** 2 oy A (Matematika), 4 oy B (Ingliz), bonus 300 000
→ A: `300 000 × 2/6 = 100 000` · B: `300 000 × 4/6 = 200 000`

**Misol 2 — parallel (2 kursda bir vaqtda, 6 oy):** A guruhi 500 000, B guruhi 400 000
→ oy vazni: A `500/900 = 0.556`, B `0.444`
→ A: **166 667** · B: **133 333**

Taqsimot modalda ko'rsatiladi va **admin qo'lda o'zgartira oladi** (yig'indi `TotalAmount` ga teng
bo'lishi tekshiriladi).

---

## 8. Foydalanuvchi oqimi

### ① Ptichka — o'quvchi qo'shish/tahrirlash
`StudentFormModal.tsx` (chegirma bloki yonida, ~satr 443) — bir qatorli checkbox.
`StudentDto` (`Dtos.cs:715`) ga 2 ta **default qiymatli** parametr qo'shiladi — eski chaqiruvlar buzilmaydi.

> **⚠️ Bu bo'lim 2026-07-30 da o'zgardi — ptichka o'quvchi formasidan OLIB TASHLANDI.**
> Yangi joyi: **a'zolikni AKTIVLASHTIRISH oynasi** (pastda ①b).

<details>
<summary>Eski yondashuv (o'quvchi formasida ptichka + qo'lda oy)</summary>

Ptichka `StudentFormModal` da edi, boshlanish oyi qo'lda kiritilardi (qaror #1).
Nega o'zgartirildi — ①b ga qarang.

</details>

### ①b Ptichka — A'ZOLIKNI AKTIVLASHTIRISHDA ⭐

> Markaz egasi: *"bonusni o'quvchi profilidan emas, uni guruhga qo'shib turilganida «bonus
> hisoblansin yoki hisoblanmasin» ptichkasi shu yerga qo'yilsin... Qo'shishda emas,
> AKTIVLASHTIRISHDA bo'lishi kerak — chunki o'quvchi qo'shilib, keyingi oydan aktiv bo'lishi
> mumkin."*

Ptichka `POST /admin/classes/{gid}/members/{sid}/activate` so'roviga qo'shiladi:
`{ date, retentionBonus }`. Uchala aktivlashtirish oynasida bor: guruh sahifasi
(`ClassDetailPage`), guruh a'zolari modali (`ClassMembersModal`), o'quvchi profilidagi guruh
boshqaruvi (`StudentDetailPage`). Standart holat — **belgilangan**.

**Nega aynan aktivlashtirishda, qo'shishda emas:** o'quvchi guruhga iyunda qo'shilib, iyuldan
aktivlashtirilishi mumkin (sinov oyi to'lovsiz — `SalaryLedger`/`BillableInMonth`). Sanoq esa
PULLIK oydan boshlanishi kerak. Shuning uchun **boshlanish oyi = aktivlashtirilgan oy**, qo'shilgan
oy emas — va admin oyni alohida kiritmaydi (qaror #1 shu bilan **bekor qilindi**).

Mantiq: `RetentionBonusService.ApplyOnActivateAsync` → `RetentionBonusTrack` upsert.

| Holat | Natija |
|---|---|
| Ptichka ☑, track yo'q | Track yaratiladi, `StartMonth` = aktivlashtirilgan oy |
| Ptichka ☐, track yo'q | Hech narsa yozilmaydi — fan hisobotda ko'rinmaydi |
| Ptichka ☑, track BOR va yoqilgan | `StartMonth` **TEGILMAYDI** — yig'ilgan oylar yo'qolmasin (ta'tildan keyin qayta aktivlashtirish, bir fan ichida guruh almashtirish) |
| Ptichka ☑, track o'chirilgan edi | Qayta yoqiladi, `StartMonth` = shu oy |
| Ptichka ☐, track bor | `Enabled = false` — sanoq oyi saqlanadi, qayta yoqilsa tarix yo'qolmaydi |
| `retentionBonus` umuman berilmagan | Tegilmaydi (eski chaqiruvlar buzilmaydi) |

> **Qamrab olinmagan yo'l:** Excel **importi** (`StudentsController` ommaviy import) a'zolikni
> to'g'ridan-to'g'ri `Status="active"` bilan yaratadi va `ActivateMember` dan o'tmaydi — u yerda
> `ActivatedAt` ham yozilmaydi (eskidan shunday). Import orqali kelgan o'quvchilar bonus
> hisobotiga tushmaydi; kerak bo'lsa aktivlashtirish oynasidan yoqiladi.

### ② «Bonus hisoboti» sahifasi
`navigation.ts` → **O'quvchilar** → "Bonus hisoboti" (`/admin/students/bonus`).
Sahifa `students` ruxsati bilan ko'rinadi; **«Bonus berish» tugmasi** `can('finance','edit')` bilan.

| F.I.Sh | Fan | Guruh | Dars kunlari | 08 | 09 | 10 | 11 | 12 | 01 | Holat |
|---|---|---|---|---|---|---|---|---|---|---|
| Aliyev S. | Ingliz tili | Ingliz A1 | Du·Cho·Ju | ✅ | ✅ | ✅ | ❄️ | ✅ | ✅ | 5/6 |
| Aliyev S. | Matematika | Matem 1 | Se·Pay | ✅ | ✅ | 📄 | ✅ | ⏳ | ✅ | 4/6 |

**Bir o'quvchi bir necha qatorda chiqadi — har fan uchun bittadan** (5.1). Qator kaliti
`(studentId, courseId)`.

- **Fan** ← `Group.CourseId` → `Subject.Name` (kursi yo'q guruhda — guruh nomi)
- **Guruh** ← shu fandagi faol `StudentGroup` → `Group.Name`
- **Dars kunlari** ← `Group.Days`; qisqartma jadvali — `ClassesPage.tsx` `DAY_SHORT`
- Har oy katagi ostida — **o'sha oydagi o'qituvchi** (taqsimot shaffof bo'lsin)
- Filtr: `hammasi | yo'lda | tayyor | uzilgan | bonus berilgan`

### ③ Bonus berish modali
```
O'quvchi: Aliyev Sardor · Fan: Ingliz tili · Davr: 2026-08 … 2027-01

Summa:  [ 300 000 ]  so'm          (CenterMeta.RetentionDefaultAmount dan oldindan to'ladi)

Taqsimot (avtomatik, tahrirlanadi):
  Sobirov B.   4 oy   →  [300 000]
  Karimov A.   2 oy   →  [      0]  ⚠ allaqachon bonus olgan (5.6)
                         ─────────
                          300 000 ✓
  [ Bekor ]  [ Bonusni berish ]
```
Bloklangan o'qituvchining inputi o'chirilgan; uning vazni ochiqlarga qayta taqsimlangan.
Barcha o'qituvchi bloklangan bo'lsa — qator `blocked`, tugma umuman chiqmaydi.

### ④ O'qituvchi profilida «Bonus» bo'limi
`TeacherDetailPage.tsx` tablariga **`bonus`** qo'shiladi. IKKI bo'lim:

1. **«Yo'ldagilar»** (`inProgress`) — hali bonus berilmagan, oylari to'planayotgan (o'quvchi × fan)
   sikllari: O'quvchi · Fan · Guruh · Sanoq (4/6) · **«Menda»** (`myMonths`) · Holat.
   > `myMonths` — bonusda qancha ULUSH tegishi, "necha oy dars berdim" EMAS: faqat sanoqqa
   > kirgan (to'langan) oylar hisoblanadi. UI'da shu izoh ko'rinadi.
2. **«Berilgan bonuslar»** — o'quvchi · fan · davr · oy · summa · qachon · kim bergan.

O'qituvchi ilovasida ham (`teacher/salary/SalaryPage.tsx`) shu ikki bo'lim **alohida** ko'rinadi —
maosh jadvalining `Expected`/`Remaining` raqamlariga **qo'shilmaydi** (qaror #3).

### ⑤ Moliya → «Bonus» tabi (faqat O'QISH)
`FinancePage.tsx` → `bonuses` tabi. Bonus **berish bu yerda emas** — u ② dagi sahifada qoladi.
Bu yerda hisobot: davr tanlash · jami/soni/o'qituvchilar soni/bekor qilingan · **o'qituvchilar
kesimi** · **oylar kesimi** · batafsil ro'yxat (qidiruv + sahifalash) · Excel.
`GET /api/admin/finance/retention-bonuses?from&to` va `.../export`.

> Bonus pul chiqimi EMAS (qaror #4) — shuning uchun bu tab Moliyaning kirim/chiqim raqamlariga
> aralashmaydi. UI'da shu eslatma ko'rsatiladi.

---

## 9. API — `RetentionBonusController` (`api/admin/retention-bonus`, `[AdminPerm("finance")]`)

| Metod · yo'l | Vazifasi |
|---|---|
| `GET /` | Bonus hisoboti jadvali — HAR (o'quvchi × fan) uchun bitta qator |
| `GET /ready-count` | `ready` qatorlar soni (`blocked` KIRMAYDI) |
| `POST /awards` | Bonus berish: `{studentId, **courseId**, totalAmount, shares[], note?}` |
| `POST /awards/{id}/cancel` | Bekor qilish — sanoq qaytmaydi, blok saqlanadi (5.7) |
| `POST /students/{id}/restart` | `{**courseId**, startMonth}` — FAQAT o'sha fan sanog'ini ko'chiradi |
| `GET /teacher/{id}` | O'qituvchining bonuslari **+ `inProgress`** (yo'ldagilar) |
| `GET /export` | .xlsx (ustunlarda **Fan** ham bor) |
| `GET|PUT /settings` | `CenterMeta` dagi 3 ta sozlama |

Qo'shimcha (Moliya bo'limi, `FinanceController`, faqat o'qish):

| Metod · yo'l | Vazifasi |
|---|---|
| `GET /api/admin/finance/retention-bonuses?from&to` | Bonus hisoboti: jami · o'qituvchilar kesimi · oylar kesimi · qatorlar |
| `GET /api/admin/finance/retention-bonuses/export` | .xlsx |

O'qituvchi ilovasi: `GET /api/teacher/retention-bonus` — faqat o'ziniki (`Salary` ruxsati bilan).

> `AdminPermAttribute` qoidasi: xodimga GET har doim ochiq, yozish uchun `finance` ruxsati kerak.

**Yagona mantiq joyi:** `Application/Services/RetentionBonusService.cs` — `BookSalesService` uslubida
static xizmat. Jadval hisobi, holat mantig'i, taqsimot va bonus yaratish **faqat shu yerda**;
controller ham, kelajakdagi har qanday chaqiruvchi ham shu orqali o'tadi.

---

## 10. Maoshga ulash — ❌ QILINMAYDI (qaror #3)

> **Bu bo'lim tarixiy tahlil sifatida qoldirilgan.** Qaror: bonus `SalaryLedger` ga **ulanmaydi**,
> alohida «Bonus» bo'limida ko'rsatiladi. Quyidagi variant ko'rib chiqilgan va **rad etilgan**;
> kelajakda kerak bo'lsa shu yerdan davom ettiriladi.

<details>
<summary>Rad etilgan variant: bonusni <code>Expected</code> ga qo'shish</summary>

```
Expected = BaseExpected − Deduction + Bonus
```

- `MonthSalaryDto` (`Dtos.cs:185`) → `+ decimal Bonus = 0, List<RetentionBonusLineDto>? Bonuses = null`
- `SalaryLedgerDto` (`Dtos.cs:198`) → `+ decimal TotalBonus = 0`
- `SalaryReportRowDto` (`Dtos.cs:209`) → `+ decimal Bonus = 0`
- `SalaryLedger.cs:193` → `var expected = baseExpected - deduction + bonus;`

Nega rad etildi: `SalaryLedger` ni 3 ta ekran ishlatadi (3.3-bo'lim) va u tizimning eng nozik joyi.
Bonusning qiymati — **ko'rinishi**da; uni maosh formulasiga kiritmasdan ham to'liq beradi.

</details>

### Nega darhol chiqim yozilmaydi (qaror #4 — kuchda)

«Bonus berish» = **hisoblash**, pul chiqarish emas. Haqiqiy pul mavjud maosh to'lovi orqali beriladi
(`FinanceTransaction(expense, "salary")`). Aks holda Kassa/Moliya bir xil pulni ikki marta hisoblaydi
va "berilgan" deb ko'rsatilgan pul aslida kassadan chiqmagan bo'lib qoladi.

Natijada bonus umuman pul oqimiga tegmaydi — u **qayd**: o'qituvchi profilida va o'qituvchi
ilovasida ko'rinadi, admin maosh to'lovini kiritganda summani hisobga oladi. Moliya, Kassirlar
hisoboti va Chiqimlar bo'limi **hech narsa o'zgarmaydi**.

---

## 11. Bosqichlar

| # | Ish | Fayllar | Vaqt |
|---|---|---|---|
| **0** ✅ | `GroupTeacherAssignment` + `ClassesController` ilgagi + backfill | Domain, Infrastructure, ClassesController | **BAJARILDI** |
| **1** ✅ | Entity'lar + `CenterMeta` maydonlari + migratsiya `RetentionBonusSystem` | Entities.cs, IAppDbContext, AppDbContext | **BAJARILDI** |
| **2** ✅ | `RetentionBonusService` — jadval, holat mantig'i, taqsimot | Application/Services | **BAJARILDI** |
| **3** ✅ | `RetentionBonusController` + .xlsx eksport | Server/Controllers | **BAJARILDI** |
| **4** ✅ | O'quvchi formasi ptichkasi + payload | StudentFormModal, StudentsController, Dtos | **BAJARILDI** |
| **5** ✅ | «Bonus hisoboti» sahifasi + berish modali + sozlamalar | pages/admin/students/ | **BAJARILDI** |
| **6** ✅ | O'qituvchi profili `bonus` tabi + o'qituvchi ilovasida alohida bo'lim | TeacherDetailPage, teacher/salary | **BAJARILDI** |
| ~~7~~ | ~~`SalaryLedger` ga ulash~~ | — | ❌ **chiqarildi** (qaror #3) |

### Sinovdan o'tgan ssenariylar

Toza PostgreSQL 16 bazasi + haqiqiy API (login → endpointlar) bilan tekshirildi:

| Ssenariy | Kutilgan | Natija |
|---|---|---|
| Guruh yaratish | o'qituvchi tarixi ochiladi | ✅ |
| Guruh o'qituvchisini almashtirish | eski qator yopiladi, yangisi ochiladi (bitta ochiq qator) | ✅ |
| 6 oy to'liq, 3-oyda o'qituvchi almashgan | 6/6 tayyor; taqsimot 2 oy A / 4 oy B (§7 misol 1) | ✅ |
| Bonus berish, 300 000 | A: 100 000 · B: 200 000; sanoq keyingi siklga suriladi | ✅ |
| Taqsimot yig'indisi noto'g'ri | rad etiladi (aniq xabar bilan) | ✅ |
| Takroriy bonus | rad etiladi (sikl endi tayyor emas) | ✅ |
| Bonusni bekor qilish | "cancelled", jamidan chiqadi (sanoq QAYTMAYDI — 5.7 ga qarang) | ✅ |
| To'lov o'chirildi (qarz) | ⏳, sanoqqa kirmaydi, sikl UZILMAYDI, oyna cho'ziladi | ✅ |
| Kechikkan to'lov kiritildi | katak o'z-o'zidan ✅ ga aylanadi (§5.3) | ✅ |
| 3 oy muzlash (ruxsat 2) | sikl uziladi, sabab ko'rsatiladi | ✅ |
| 2 oy muzlash, keyin qaytdi | uzilmaydi, oyna cho'ziladi (§5.4) | ✅ |
| O'quvchi arxivlandi | DARHOL uziladi (2 oy kutilmaydi) | ✅ |
| Qayta boshlash | yangi oydan yangi sikl; noto'g'ri oy rad etiladi | ✅ |
| Excel eksport | to'g'ri .xlsx qaytadi | ✅ |
| **`SalaryLedgerDto`** | bonus maydoni YO'Q — maosh o'zgarmagan (qaror #3) | ✅ |

---

## 11b. IKKINCHI BOSQICH (2026-07-30, markaz egasining qo'shimcha talablari)

| # | Talab | Holat |
|---|---|---|
| A | Orqaga sanalgan aktivlashtirishda oraliq oylar hisoblanmaydi | ✅ **BAJARILDI** |
| B | Hisob yozilmagan oy "to'langan" deb ko'rsatilyapti | ✅ 📄 `nocharge` holati (5.2) |
| C | Har fan uchun alohida hisoblansin | ✅ sikl kaliti `(o'quvchi × fan)` (5.1) |
| D | Bir o'quvchi orqali o'qituvchiga qayta bonus berilmasin | ✅ `blocked` (5.6) |
| E | Bekor qilingan qayta berilmasin | ✅ (5.7) |
| F | Moliya ichida bonus hisoboti bo'limi | ✅ (8 ⑤) |
| G | O'qituvchi profilida "yo'ldagilar" ham ko'rinsin | ✅ (8 ④) |

### A — aktivlashtirish billing xatosi (ildiz sabab)

`TuitionService.AccrueMonth` da a'zolik shoxobchasidan **oldin** turgan tekshiruv:
```csharp
if (s.EnrollmentDate.Length >= 7 && string.CompareOrdinal(s.EnrollmentDate[..7], month) > 0) continue;
```
O'quvchi bugun (iyul) qo'shilib, guruhda **fevraldan** aktivlashtirilsa — `EnrollmentDate="2026-07"`
bo'lgani uchun mart…iyun oylari `continue` bo'lardi va **hech qachon** hisoblanmasdi. Fevral esa
yozilardi, chunki qisman hisob (`ChargeActivationProrateAsync`) bu tekshiruvni chetlab o'tadi —
shuning uchun aynan **bitta oy** ko'rinardi.

**Ikki tuzatish:**
1. Tekshiruv **guruhsiz (eski `ClassName`) shoxobchasi ichiga** ko'chirildi. A'zoligi bor o'quvchida
   haqiqat manbai — `StudentGroup.ActivatedAt`, `EnrollmentDate` emas.
2. Yangi **`TuitionService.AccrueCatchUpAsync`** — aktivlashtirish paytida oraliq oylar
   (`NextMonth(aktiv oy)` … joriy oy) **darhol** to'liq oylik bilan yoziladi.
   `ClassesController.ActivateMember` shuni chaqiradi va javobda `catchUpMonths` qaytaradi.
   Idempotent (mavjud hisobga tegmaydi, `Locked` himoyalangan), kelajak oy yozilmaydi.

Natija (iyulda, fevraldan aktivlashtirish, oylik 500 000):
`2026-02: 333 333.33 (qisman) · 03…07: har biri 500 000` · `Balance = −2 833 333.33` (aniq mos).

> **Qolgan cheklov (halol):** fon xizmati `AccrueDue` oylar oralig'ini hamon `EnrollmentDate` dan
> boshlaydi. Orqaga sanalgan a'zolik aktivlashtirish endpointidan **boshqa** yo'l bilan paydo
> bo'lsa (import / bevosita bazada), oraliq oylar yozilmay qoladi. Oralig'ini
> `min(ActivatedAt)` gacha kengaytirish **ataylab qilinmadi**: u ilgari ataylab hisoblanmagan
> eski oylar uchun BARCHA o'quvchiga birdaniga qarz yozib yuborishi mumkin.

### Ikkinchi bosqich sinovlari

| Ssenariy | Natija |
|---|---|
| Orqaga sanalgan aktivlashtirish → 1 qisman + 5 to'liq oy | ✅ |
| Qayta aktivlashtirish (idempotentlik) — hisoblar ikki baravar bo'lmaydi | ✅ |
| Nazorat: tuzatilmagan kod bilan o'sha ssenariy — oylar yozilmaydi | ✅ (sabab tasdiqlandi) |
| 2 fanga qatnaydigan o'quvchi → 2 mustaqil qator | ✅ |
| Bir fan uchun bonus → boshqa fan sanog'i tegilmaydi | ✅ |
| O'sha o'qituvchiga qayta bonus → 400, ism bilan xabar | ✅ |
| Keyingi sikl to'lgach → `blocked`, tugma yo'q, `ReadyCount` ga kirmaydi | ✅ |
| Bloklangan o'qituvchi vazni ochiqlarga qayta taqsimlanadi (yig'indi aniq) | ✅ |
| Bekor qilish → sanoq qaytmaydi, blok saqlanadi, qayta berib bo'lmaydi | ✅ |
| Hisob qatori o'chirildi → 📄 `nocharge`, sanoqqa kirmaydi | ✅ |
| 100% chegirma (qator bor, summa 0) → ✅ sanaladi | ✅ |
| Bir fan ichida guruh almashtirish (A1 → A2) → sikl uzilmaydi | ✅ |
| Teglanmagan (GroupId=null) hisob/to'lov narx nisbatida fanlarga bo'linadi | ✅ |
| Moliya tabi: jami faqat `given`, kesimlar, davr filtri, Excel | ✅ |
| O'qituvchi kesimi: `inProgress` + `myMonths` (kasrli ham) to'g'ri | ✅ |

---

## 12. Xavflar va diqqat nuqtalari

1. ~~**`SalaryLedger` — tizimning eng nozik joyi.**~~ ✅ **Xavf yo'q** — qaror #3 bo'yicha
   `SalaryLedger` va uning DTO'lari **umuman o'zgartirilmaydi**.
2. **`BillableInMonth` nusxalanmasin** — `MembershipLifecycle.BillableInMonth` YAGONA ta'rif
   (`SalaryLedger` ham shuni chaqiradi). Ikki nusxa bo'lsa, vaqt o'tib bir-biridan ajralib ketadi.
2b. **⚠️ Teglanmagan (`GroupId=null`) hisob/to'lovni narx nisbatida taqsimlash — 3 ta nusxa.**
   `SalaryLedger`, `GroupBalanceService` va endi `RetentionBonusService` da alohida yozilgan
   (birinchisiga tegish taqiqlangan, ikkinchisi guruh-shaklda). Konvensiya bir xil, lekin
   **umumiy yordamchiga chiqarish kerak** — aks holda vaqt o'tib ajralib ketadi.
3. **Arxivlangan o'qituvchi** — bonus baribir hisoblanadi (o'tgan mehnati uchun), lekin ro'yxatda
   "arxivda" belgisi bilan chiqadi; admin qaror qiladi.
4. **Guruh yopilishi** (`ClassesController.cs:940` `close`) barcha a'zoliklarni muzlatadi → sikl pauzaga
   tushadi. Markaz o'zi yopib, o'quvchini boshqa guruhga o'tkazsa — `MaxGapMonths` buni ushlaydi.
5. **Kesh ishlatilmaydi** (`DataCache`) — moliya raqamlari darhol yangilanishi kerak.
6. **Unumdorlik** — ptichkali o'quvchilar soni kam bo'ladi; barcha ma'lumot ~7 ta so'rovda ommaviy
   yuklanadi (`.AsNoTracking()`), o'quvchi boshiga alohida so'rov qilinmaydi (N+1 bo'lmasin).
7. **⚠️ `ForTeacherAsync` butun hisobotni hisoblaydi** — har o'qituvchi profili (va o'qituvchi
   ilovasidagi Maosh sahifasi) ochilganda BARCHA ptichkali o'quvchilar bo'ylab yuriladi.
   Hozircha maqbul, lekin ptichkali o'quvchilar soni yuzlab bo'lsa kesh yoki `onlyTeacherId`
   filtri kerak bo'ladi.
8. **⚠️ Bonus berish TEKSHIRUV va YOZUV orasida poyga (race) bo'lgan — tuzatildi.**
   `GiveAsync` avval holatni o'qiydi (sikl tayyormi, o'qituvchi bloklanganmi), keyin yozadi.
   Ikkinchi so'rov oraliqda kirib ulgursa ikkalasi ham "tayyor" ni ko'rardi. Bir fan ichida buni
   unikal indeks ushlardi (lekin 500 bilan), **turli fanlarda esa umuman ushlanmasdi** — bir
   o'qituvchi bitta o'quvchidan IKKI bonus olardi (600 000, 300 000 o'rniga).
   Yechim: `RetentionBonusController.Give` shu O'QUVCHI kesimida PostgreSQL advisory lock bilan
   ketma-ketlashtiriladi; `23505` endi 409 + tushunarli xabar; modal ham `saving` bilan
   ikkinchi to'siq qo'yadi.
   DIQQAT: `finally` da **`pg_advisory_unlock_all()`** ishlatiladi, aniq `pg_advisory_unlock` emas —
   EF'da `EnableRetryOnFailure` yoqilgani uchun qulf buyrug'i qayta bajarilib, re-entrant qulf
   ikki marta olinishi mumkin; bir marta bo'shatish yetmay, qulf ushlab turgan ulanish poolga
   qaytardi va o'sha o'quvchi bo'yicha keyingi so'rovlar abadiy kutib qolardi.
9. **Moliya tabidagi "Bonuslar soni" ulushlarni sanardi** — ikki o'qituvchiga bo'lingan bitta
   bonus ikki marta chiqardi (summalar to'g'ri edi). Endi award id'lari bo'yicha `Distinct().Count()`.
10. **Bonus berish sanasi (`Award.CreatedAt`) `AppClock.Now` bilan yoziladi** — ya'ni allaqachon
   markaz vaqti (UTC+5). Moliya hisobotida `AppClock.ToLocal` **qo'llanmaydi**: qo'llansa sana
   5 soatga siljib, chegaradagi bonus noto'g'ri oyga tushardi. Bu `FinanceTransaction.CreatedAt`
   dan (u UTC yoziladi va `ToLocal` qilinadi) FARQ qiladi — chalkashmasin.

---

## 13. Qarorlar — ✅ TASDIQLANDI (2026-07-30)

| # | Savol | **QAROR** |
|---|---|---|
| ~~1~~ | ~~Sanoq qaysi oydan?~~ | ❌ **BEKOR QILINDI (2026-07-30).** Endi oy qo'lda kiritilmaydi — u **aktivlashtirilgan oydan** olinadi (8-bo'lim ①b). Ptichka ham o'quvchi formasidan aktivlashtirish oynasiga ko'chdi |
| 2 | Muzlatilgan oy? | **PAUZA, max 2 oy** (`RetentionMaxGapMonths=2`, sozlanadi) |
| 3 | Bonus maoshga qo'shiladimi? | **YO'Q — faqat alohida «Bonus» bo'limida.** `SalaryLedger` TEGILMAYDI |
| 4 | «Berish» = pul chiqdimi? | **Yo'q — hisoblanadi.** Pul odatdagi maosh to'lovi orqali beriladi |
| 5 | Sikl kimga tegishli? | **Har FAN uchun alohida** — `(o'quvchi × kurs)` (5.1) |
| 6 | Bir o'qituvchiga qayta bonus? | **Yo'q** — `(o'qituvchi, o'quvchi)` umr bo'yi bitta (5.6) |
| 7 | Bekor qilingandan keyin? | **Qayta berilmaydi** — sanoq qaytmaydi, blok saqlanadi (5.7) |
| 8 | Bonus hisoboti qayerda? | Berish — O'quvchilar bo'limida; **hisobot — Moliya → «Bonus»** (8 ⑤) |

### Qarorlarning kodga ta'siri

**#1 — boshlanish oyi qo'lda.** `RetentionBonusStartMonth` ptichka bilan birga majburiy maydon
(`month` input). Bo'sh bo'lsa o'quvchi "hali boshlanmagan" holatida turadi va hisobotda sanoq
ko'rsatilmaydi. Tavsiya sifatida forma yonida birinchi aktivlashgan oy **matn ko'rinishida**
ko'rsatilishi mumkin, lekin maydonga o'zi yozilmaydi. → 8-bo'lim ① shunga moslanadi.

**#3 — `SalaryLedger` tegilmaydi.** Bu eng katta soddalashtirish:
- ❌ `Bosqich 7` rejadan **chiqarildi** (~1.5 soat kamaydi → jami ~12.5 soat)
- ❌ `MonthSalaryDto` / `SalaryLedgerDto` / `SalaryReportRowDto` o'zgarmaydi
- ❌ `SalaryLedger.cs:193` (`expected = baseExpected - deduction`) o'zgarmaydi
- ✅ 12-bo'limdagi 1-xavf ("SalaryLedger — eng nozik joy") **yo'qoladi**
- ✅ `SalaryLedger.cs:283` `BillableInMonth` baribir umumiy joyga chiqariladi (2-xavf kuchda qoladi) —
  lekin bu faqat **o'qish**, hisob mantig'i o'zgarmaydi

Bonus qayerda ko'rinadi: o'qituvchi profilidagi **`bonus` tabi** (8-bo'lim ④) va o'qituvchi
ilovasidagi `SalaryPage` da **alohida bo'lim** sifatida ("Maosh" raqamlariga qo'shilmaydi).

**#3 + #4 birgalikda:** bonus hech qayerda pul oqimiga aralashmaydi — u **qayd**. Admin bonusni
ko'rib turadi va maosh to'lovini kiritganda summani o'zi hisobga oladi. Moliya, Kassa va
Chiqimlar bo'limlari **hech qanday o'zgarishsiz** qoladi.

> Kelajakda maoshga ulash kerak bo'lsa — bu qo'shimcha, ortga qaytariladigan qadam
> (10-bo'limdagi tahlil o'z kuchida qoladi).

---

## 14. Ochiq savollar (keyinroq hal qilinadi)

- 6 oy to'lganda adminga Telegram xabarnomasi kerakmi? (`BookSalesService.NotifyAdminsAsync` tayyor
  namuna, `TuitionAccrualService` shabloni bilan yengil job) — **hali qilinmadi**
- ~~Ikkinchi va keyingi sikllar avtomatik boshlanadimi?~~ → **HAL QILINDI: avtomatik.** Bonus
  berilganda o'sha fanning `RetentionBonusTrack.StartMonth` i davr oxiridan keyingi oyga suriladi
  (uzluksiz zanjir). Aks holda o'sha sikl jadvalda «tayyor» bo'lib turaverardi. Oyni ko'chirish
  uchun **«Qayta boshlash»** ishlatiladi (o'quvchi formasidagi oy — faqat trigger yo'q fanlar
  uchun fallback). Bekor qilishda oy **QAYTMAYDI** (5.7).
- Bonus o'quvchining o'ziga/ota-onasiga ko'rinadimi? (hozircha **yo'q** — faqat admin va o'qituvchi)
- **Teglanmagan hisob/to'lovni taqsimlash uchinchi marta nusxalandi** (12-bo'lim, 2b) — umumiy
  yordamchiga chiqarish kerak.
- **`AccrueDue` oralig'i** hamon `EnrollmentDate` dan boshlanadi (11b, A bo'limidagi cheklov).
  Import/bevosita bazadan kelgan orqaga sanalgan a'zolikni qamramaydi.
