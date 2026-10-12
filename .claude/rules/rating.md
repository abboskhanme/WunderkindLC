# O'quvchilar REYTINGI qoidalari (testlar + ball)

CRM 4-bosqich (docs/SPEC-crm-upgrade.md §5, P4.3–P4.4; egasining qarorlari `docs/ASSUMPTIONS.md` 2026-10-11).
Sahifalar: **Hisobotlar → Reyting** (`/admin/hisobotlar/reyting`), o'quvchi profilida **«Reyting»** tabi,
guruh sahifasining «Reyting» tabida **«Umumiy reyting (testlar + ball)»**, o'quvchi/ota-ona kabinetida
**Progress → «Reyting»**, sozlama — **Sozlamalar → O'quv → Reyting**. Ruxsat kaliti — **`classes.rating`**.
Migratsiya (integrator, `CrmTestsRating`): `CenterMeta.RatingTestWeight` · `RatingTopBottomCount` ·
`RatingTopBottomScope` (ma'lumot SQL'i — `docs/migrations/Phase4.sql`).

## 1. FORMULA — `RatingFormula` (sof, `RatingFormulaTests`)

```
T(o'quvchi, guruh)  = davrdagi testlar foizlarining O'RTACHASI (har test teng vaznli; foiz 0..100 ga qisiladi;
                      ball qo'yilmagan test sanalmaydi; MaxScore ≤ 0 test e'tiborsiz; test turi vaznga ta'sir qilmaydi)
B(o'quvchi, guruh)  = Ball ÷ (guruhdagi ENG YAXSHI ball) × 100;  guruhda eng yaxshi ball 0 bo'lsa — B YO'Q
GuruhBali           = w·T + (1−w)·B   (w = RatingTestWeight ÷ 100, standart 60%)
                      faqat T bo'lsa → T;  faqat B → B;  ikkalasi ham yo'q → O'RINSIZ.  1 kasr.
MarkazBali          = o'quvchining guruh ballari O'RTACHASI
```

- **O'rin — musobaqa usuli** ("1, 2, 2, 4"): teng bal — bir xil o'rin. Teng bo'lsa ko'rsatish tartibi:
  yuqori T, keyin ism.
- **Ball guruhning eng yaxshisiga nisbatan** ATAYIN: o'qituvchilar har xil hajmda baho qo'yadi (har dars vs
  haftada bir) — xom ball guruhlararo solishtirilmaydi.
- Markazdagi T (komponentlar, jadval) — o'quvchining BARCHA testlari o'rtachasi (har test teng vaznli).
- **Arxivlangan o'quvchi HECH QAYERDA yo'q** (o'rin ham olmaydi, guruhning eng yaxshi balini ham belgilamaydi).
- Davrdagi guruh to'plami = shu davrda testi YOKI bali bor arxivlanmagan o'quvchilar (a'zolik shart emas — o'tgan
  davrdagi eski guruh o'sha davr uchun sanaladi).
- ⚠️ **QARZDORLIK REYTINGNI YOPMAYDI** (egasining qarori) — balans hech qayerda o'qilmaydi.

## 2. DAVRLAR — `RatingPeriods.Resolve(kind, key, today)` (sof, `RatingPeriodsTests`)

| Tur | Kalit | Qamrov | Yorliq |
|---|---|---|---|
| `month` («Oy») | `yyyy-MM` (standart — joriy oy) | butun oy | «Oktabr 2026» |
| `quarter` («3 oy») | OXIRGI oy `yyyy-MM` | oxirgi−2 … oxirgi (SIRPANUVCHI 3 oy) | «Avgust – Oktabr 2026» |
| `year` («O'quv yili») | boshlanish yili `yyyy` | 1-sentabr … 31-avgust | «2026/27 o'quv yili» |

- «3 oy» va «chorak» egasining qarori bilan BITTA ("juda maydalashib ketmasin").
- Oraliq BUGUNGACHA qirqiladi; kelajakdagi kalit — 400 «Davr noto'g'ri».
- **Oldingi davr** (▲/▼): oy → o'tgan oy; «3 oy» → oynadan OLDINGI 3 oy (ustma-ust emas); yil → o'tgan o'quv yili.
- Klientdagi nusxa — `lib/ratingPeriods.ts` (faqat tanlov va yorliq; chegaralarni server hal qiladi).

## 3. DVIGATEL — `RatingEngine` (Application/Services/Rating)

⚠️ **JURNAL HECH QACHON qator-qator YUKLANMAYDI.** Ball — `StudentBallService.ComputeByGroupAsync(db, groupIds,
from, to)` (sana oralig'i varianti, SQL'da GROUP BY); testlar — davr testlariga join qilingan (o'quvchi, guruh,
ball, maks) proyeksiyasi (kichik; decimal agregati SQLite'da ishlamaydi). So'rovlar soni MA'LUMOT HAJMIGA bog'liq
emas — `RatingEngineTests` qulflaydi (10 barobar ko'p jurnal → so'rovlar soni o'zgarmaydi, jurnalga tegadigan har
so'rov GROUP BY).

- Bitta davr = bitta hisob (`RatingResult`), **kesh** — `DataCache` kaliti `rating:v2:{kind}:{key}:{to}`,
  bog'liq turlar `RatingEngine.CacheDeps` (TestScore, TestResult, JournalEntry, CriterionGrade,
  StudentBallAdjustment, StudentGroup, Student, Group, CenterMeta, Subject, Teacher), TTL 15 daqiqa.
  Markaz jadvali, guruh jadvali, o'quvchi bloki va kabinet SHU obyektni kesadi — raqamlar hech qachon ayrilmaydi.
- **Test foizi = Score ÷ MaxScore.** U4a dan keyin `MaxScore = QuestionCount`, `Score` = yechgan soni — formula
  O'ZGARMAYDI. Test turi (`kind`) blok javobida — `TestResult.Kind` (U4a; eski, to'ldirilmagan qatorda `""`).
- **Davomat** (blokdagi qator) — `StudentJournalBuilder.PeriodAsync` (o'quvchi ilovasining o'z raqami, faqat bitta
  o'quvchining darslari). U3a uni `AttendanceRules` ga o'tkazganda blok avtomatik ergashadi — NUSXA YOZMANG.
- O'quvchi bloki: markazdagi o'rin (+ oldingi davr o'rni), har guruhdagi o'rin (hozirgi faol/sinov a'zoliklari ma'lumotsiz ham
  «—» bilan ko'rinadi), komponentlar, 12 oylik tarix (har oy o'z keshidan), davrdagi testlar (testdagi o'rin —
  ball qo'yilgan HAMMA orasida, `TestResultService` qoidasi), davomat.

## 4. ESKI `RatingService.SchoolAsync` — kontrakt va raqamlar O'ZGARMADI

`GET /api/admin/students/rating` va `GET /api/student/rating` (eski ilova) avvalgidek ishlaydi: ball, o'rtacha baho
(BARCHA baholar), davomat = (o'tilgan − kech bo'lmagan sababli qoldirilgan) ÷ o'tilgan. Ilgari butun `JournalEntries`
xotiraga yuklanardi — endi SQL agregatlari (MCP `TestTools.ComputeCenterAsync` naqshi). `RatingEngineTests`
yangi kodni ESKI algoritm (test ichida saqlangan) va MCP bilan solishtiradi. Yangi UI bu ikki endpointni CHAQIRMAYDI.

## 5. API va RUXSAT

`RatingController` (`api/admin/rating`), hammasi `ReadRequiresPerm = true` (javobda o'quvchilar va ballari):

| Endpoint | Ruxsat |
|---|---|
| `GET centre?period=&key=&q=&page=&pageSize=` | `classes.rating` |
| `GET groups/{groupId}?period=&key=` | `classes.rating` yoki `classes.list` (guruh sahifasi) |
| `GET students/{studentId}?period=&key=` | `students.list` yoki `classes.rating` (profil) |
| `GET settings` / `PUT settings` | `classes.rating` (PUT — tahrir) |

Kabinet: `GET api/student/rating/summary?studentId=&period=&key=` (`StudentPortalController.Rating.cs`) — blok +
guruh va markaz TOP 10 (+ o'z qatori TOP dan tashqarida bo'lsa). Begona o'quvchilar FAQAT ism, o'rin, bal bilan
(id YO'Q). Ota-ona faqat O'Z farzandini (`TargetAsync`), admin — `?studentId=` bilan.

`GET top-bottom` (oylik TOP/BOTTOM ko'rinishi) — U4d (`TopBottomRules`, `MonthlyRatingNotifier`), hali YO'Q.

## 6. SOZLAMALAR — `CenterMeta`

`RatingTestWeight` (0..100, standart 60; **0 ham to'g'ri qiymat** — "faqat ball", shuning uchun migratsiyada mavjud
qator 60 bilan to'ldiriladi, modelda `HasDefaultValue` ATAYIN yo'q: EF 0 ni "berilmagan" deb o'tkazib yuborardi),
`RatingTopBottomCount` (1..10, standart 5), `RatingTopBottomScope` (`group` | `centre`). Saqlash tarixga QIYMATLARI
bilan yoziladi (`SettingsAudit`, «Reyting sozlamalari», «Testlar ulushi, %: 60 → 70»).

## 7. UI — `components/rating/*`

- `StudentRatingBlock` — YAGONA ko'rinish (admin profili ham, kabinet ham, `variant`): davr tanlovi, 3 plitka
  («Markazda: 12-o'rin / 430 ▲3», «Guruhda», «Reyting bali» + formula izohi), 12 oylik IKKI kichik grafik
  (o'rin — teskari o'q; bal 0..100) — ⚠️ hech qachon ikki y-o'qli bitta grafik emas (`course-analytics.md` §6),
  yakka rang `#6366f1`; o'rin o'zgarishi sky/rose (yashil/qizil EMAS); «Testlarim»; davomat qatori.
- Ranglar umumiy CSS o'zgaruvchilaridan (`--surface`, `--border`, `--muted`, `--text`/`--fg`) — kabinetning tungi
  rejimi buzilmaydi.
- Kabinetdagi eski «Guruh» va «Markaz» tablari olib tashlandi — o'rniga bitta «Reyting» (blok + TOP ro'yxatlar).
- Guruh sahifasida eski ball jadvali qoldi — endi sarlavhasi «Ball tafsiloti».
- Hisobot katalogida `inNav: true` (sof hisobot), menyuda asosiy «Hisobotlar» guruhida (`mainReportRoutes`).
