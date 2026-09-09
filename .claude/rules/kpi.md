---
description: Xodimlar KPI tizimi — kalkulyator, kunlik cheklist, tiketlar, oyni yopish va qoidalarni versiyalash.
paths:
  - "IntellectCRM.Application/Services/Kpi/*.cs"
  - "IntellectCRM.Server/Controllers/KpiController.cs"
  - "IntellectCRM.Client/src/pages/admin/kpi/*"
  - "IntellectCRM.Client/src/api/services/kpi.ts"
---

# KPI (xodimlar samaradorligi) qoidalari

"Boshqaruv → KPI" (`/admin/boshqaruv/kpi`), 5 sahifa: **Bugun · Oy · Tiketlar · Yopish · Qoidalar**.
Migratsiya: `AddKpiModule`. Manba — `kpi/` papkasidagi 5 fayl, tahlil va reja — `kpi.md`.

## 1. ENG MUHIM — KPI hech narsa YARATMAYDI, faqat O'QIYDI

Lid — Lidlarda, to'lov — Kassada, davomat — Jurnalda, qo'ng'iroq — Qo'ng'iroqlarda qoladi.
KPI moduli o'ziga tegishli **beshta** narsanigina yozadi:

| Yozuv | Nima uchun |
|---|---|
| `ChecklistEntry` | kunlik cheklist belgisi |
| `KpiTicket` | sifat nazorati tiketi |
| `KpiMonthSnapshot` | oy BOSHIDAGI raqamlar (orqaga tiklab bo'lmaydi) |
| `KpiMonthResult` | tasdiqlangan va MUZLATILGAN oylik natija |
| `StudentExtension` | kursni tugatib keyingi bosqichga o'tish hodisasi (Bonus B manbai) |

⚠️ **`SalaryLedger` ga ULANMAYDI.** KPI summani AYTADI, pulni to'lamaydi — to'lov avvalgidek
Moliya bo'limidagi maosh yo'li bilan (`FinanceTransaction expense/salary`). Sabab «Bonus
hisoboti» (`retentionBonus`) dagi bilan bir xil: hisob va to'lov ikki xil mas'uliyat, ularni
bog'lash "hisobotni ochdim — pul ketdi" holatiga olib borardi.

## 2. RAQAM DETERMINISTIK, KO'RINISH — YO'Q

`RetentionBonusService` naqshi: oylik hisob **jonli** hisoblanadi va **saqlanmaydi**. Kechikkan
ma'lumot kiritilsa (sinov natijasi, ketish sababi, to'lov) katak o'z-o'zidan tuzaladi.

Saqlanadigan yagona hisob — **tasdiqlangan** oy (`KpiMonthResult.Status = "confirmed"`): u
`InputsJson` + `CoefsJson` + `RuleSetId` bilan MUZLATILADI. Undan keyin qoida o'zgarsa ham
o'sha oyning summasi o'zgarmaydi.

## 3. Hisob QATLAMI — sof funksiyalar

```
Application/Services/Kpi/
  KpiRuleSetJson.cs    KpiTier + KpiRuleSetJson (konstantalar va 3 pog'ona jadvali)
  KpiRules.cs          Coef / TierOf — Excel INDEX(...,MATCH(x,froms,1)) ekvivalenti
  KpiCalculator.cs     Intake / Retention — bazaga BOG'LIQ EMAS, 14 test bilan qulflangan
  KpiActiveStudentRule "faol o'quvchi" YAGONA ta'rifi
  KpiRuleSeed.cs       Excel konstantalari (3 rol)
  KpiTicketCatalog.cs  13 audit mezoni + tiket sabablari
  KpiVersioning.cs     RuleFor(role, month) / SalaryFor(user, month)
  KpiMetricsService.cs tizimdan kirish raqamlarini yig'ish (jonli)
  KpiSeedService.cs    qoidalar + cheklist shablonlari (idempotent)
```

### Formulalar

```
KIRUVCHI:  OYLIK = Oklad + (Shartnoma × 35 000 × Konv.koef) × Samar.koef − Tiket × 50 000
CHIQUVCHI: OYLIK = Oklad + (Faol × 1 000 × Ushlab qolish koef
                          + Uzaytirgan × 35 000 × Uzaytirish koef
                          + Yig'ilgan qarz × 1%) × Samar.koef − Jarimalar
```

⚠️ **Pog'ona tanlash — `From <= x` bo'lgan ENG OXIRGI qator** (Excel `MATCH(...,1)`). Qiymat
birinchi pog'onadan ham past bo'lsa Excel `#N/A` beradi, biz esa **eng past pog'onani** olamiz:
manfiy/nol konversiya butun hisobni xatoga tushirmasin.

⚠️ **Pul `decimal`, koeffitsient `double`.** Har pul qatori alohida so'mgacha yaxlitlanadi
(`AwayFromZero`) — aks holda ekrandagi qatorlar yig'indisi "jami" bilan tiyin farq qilardi.

⚠️ **Yuqori chegara (7,5 mln) KESMAYDI** — faqat `CapExceeded` bayrog'ini qo'yadi. Kesib
qo'ysak, xodim sababini bilmasdan pul yo'qotardi; endi «Yopish»da rahbar alohida tasdiqlaydi.

## 4. «FAOL O'QUVCHI» — bitta joyda kodlangan ta'rif

```
IsKpiActive(asOf) =
     kamida bitta StudentGroup: Status == "active" && IsActive    (sinov va MUZLATILGAN — faol EMAS)
  && oxirgi Present==true jurnal yozuvi > asOf − 30 kun           (hech qachon dars bo'lmagan — faol)
  && eng eski to'lanmagan MonthlyCharge muddati > asOf − 45 kun
  && !Student.IsArchived
```

⚠️ Ikki joyda ishlatiladi — oy BOSHI snapshoti va oy OXIRI hisobi. Nusxa ko'chirilsa, biri
o'zgarganda ikkinchisi jimgina eskirib qolardi va Bonus A ikki xil raqamdan hisoblanardi.
⚠️ Funksiya SOF: entity emas, tayyor qiymatlar qabul qiladi (`KpiActiveStudentRuleTests`).

## 5. SUMMALAR BIR MARTA KIRITILADI — `EffectiveFrom` versiyalash

Oklad, bonus birligi, jarima, kafolat, pog'ona jadvallari — hammasi **oy bilan bog'langan
versiya**:

```
RuleFor(role, M)   = KpiRuleSets      .Where(EffectiveFrom <= M).OrderByDesc(EffectiveFrom).First()
SalaryFor(user, M) = KpiProfileSalaries.Where(EffectiveFrom <= M).OrderByDesc(EffectiveFrom).First()
```

Bir marta kiritilgan summa keyingi hamma oylarga **o'zi** o'tadi; o'zgartirilsa — o'zgargan
oydan yangisi, oldingi oylar eskisi bilan qoladi.

Himoya qoidalari:
1. O'zgartirish formasi «kuchga kirish oyi»ni so'raydi — standart **keyingi oy**, joriy oy
   mumkin, **o'tgan oy TAQIQLANGAN** (yopilgan oyni orqadan qayta yozib bo'lmasin).
2. **Yopilgan oy o'z versiyasi bilan muzlatilgan** (`KpiMonthResult.RuleSetId`).
3. Hech narsa o'chirilmaydi — eski versiyalar «Qoidalar» sahifasida tarix.

## 6. RUXSAT

| Kalit | Nima ochadi |
|---|---|
| `kpi` | bo'lim (o'qish) |
| `kpi.today` | Bugun — kunlik norma va cheklist belgilash |
| `kpi.month` | Oy — kalkulyator |
| `kpi.tickets` | Tiketlar — sifat nazorati |
| `kpi.close` | Yopish — natijani tasdiqlash va muzlatish |
| `kpi.rules` | Qoidalar, oklad, rol biriktirish, seed |

Naqsh `.claude/rules/permissions.md` §4.1 bilan bir xil: **sinf darajasi = o'qish, metod
darajasi = sahifa bo'yicha yozish**.

⚠️ **`ReadRequiresPerm = true`** — javobda xodimning OKLADI va oylik summasi bor. `[AdminPerm]`
da GET odatda har qanday xodimga ochiq (bo'limlararo o'qish uchun); bu yerda esa har kim
hammaning maoshini ko'rib qolardi.

⚠️ **Xodim O'Z KPI'sini ko'radi** — `userId` bo'sh bo'lsa joriy foydalanuvchi. Boshqa xodimning
raqamini ko'rish uchun bo'lim ruxsati kerak (`KpiController.ResolveUser`), va rad etish **403 +
SABAB** bilan qaytadi, jim bo'sh ro'yxat bilan emas.

⚠️ **«Bugun» sahifasidagi OYLIK PROGNOZI alohida darvozalangan.** `kpi.today` cheklist to'ldirish
uchun beriladi — u orqali begona xodimning maoshi ko'rinmasligi kerak. Shuning uchun `Forecast`
faqat O'ZIGA yoki `kpi.month` ruxsati borga qaytariladi; norma, cheklist va signallar hammaga
qoladi (rahbar cheklistni baribir tekshira oladi).

⚠️ **Metod darajasidagi GET'larda ham `ReadRequiresPerm = true`** — metod atributi sinf
atributini BEKOR QILADI (`AdminPermAttribute.IsOverriddenAtMethod`), ya'ni usiz sinfdagi
darvoza tushib qolar va maosh qaytaradigan GET har qanday xodimga ochilib ketardi.

## 7. ZIDDIYAT — Bonus A/B qiymati (hal qilinmagan, lekin BLOKLAMAYDI)

Excel «Boshlash» varag'i Bonus A = **6 000**, B = **45 000** deydi; «KPI qoidalari» varag'i va
**barcha formulalar** A = **1 000**, B = **35 000** bilan hisoblaydi.

**Seed FORMULA qiymati bilan (1 000 / 35 000).** Tekshiruv: shu qiymatlar bilan «NORMA»
ssenariysi roppa-rosa **4 500 000** — ya'ni kafolatga teng, normal oyda bonus yo'q. 6 000/45 000
bilan «Kuchli oy» 8,75 mln bo'lib 7,5 mln chegaradan oshib ketardi.

⚠️ Qiymat «Qoidalar» sahifasida tahrirlanadi va keyingi oydan kuchga kiradi — ya'ni rahbar
qarorini kutib turish SHART EMAS va kod o'zgartirilmaydi.

## 8. NIMA ATAYIN QILINMAGAN

- **Geymifikatsiya** (kunlik 4-shartnoma 30 000 va h.k.) — alohida byudjet, oylikka kirmaydi.
- **Tiketni AI o'zi QO'YMAYDI** — Gemini faqat `proposed` taklif qiladi, rahbar tasdiqlaydi.
  Sabab: tiket — pul jarimasi, uni mashina yakuniy qilib qo'ymasligi kerak.
- **Sabab katalogi `ActionReason` ga qo'shilmagan** — tiket sabablari KODDA
  (`KpiTicketCatalog`): ular 13 ta audit mezoniga qattiq bog'langan, operatsion sabablar esa
  foydalanuvchi tomonidan tahrirlanadi.
- **Telegram DM javob vaqti** — birinchi versiyada faqat Instagram (`IgConversation`).
  Telegram uchun `LeadTelegramMessage` da "kim javob berdi" maydoni YO'Q.

## 9. Yangi ko'rsatkich qo'shsangiz

1. **Formulani `KpiCalculator` ga qo'ying** — controllerda yoki klientda emas. Test yozing.
2. **Konstantani hardcode QILMANG** — `KpiRuleSetJson` ga maydon qo'shing va seed'ni yangilang;
   aks holda uni o'zgartirish uchun deploy kerak bo'lardi.
3. **"Faol o'quvchi" kerak bo'lsa** — `KpiActiveStudentRule`, o'z shartingizni yozmang.
4. **Oy uchun qoida/oklad** — `KpiVersioning.RuleFor` / `SalaryFor`, `OrderByDescending` ni
   qayta yozmang.
5. **Orqaga tiklab bo'lmaydigan raqam** (oy boshi holati) — `KpiMonthSnapshot` ga qo'shing,
   aks holda o'tgan oy hech qachon to'g'ri hisoblanmaydi.
6. **Yangi cheklist bandi** — shablonga (`ChecklistTemplateItem`), avtomatlashtirilsa
   `AutoCheckKey` va `ChecklistAutoCheck` xaritasi. Qo'llanmagan kalit JIM e'tiborsiz
   qoldiriladi (band qo'lda belgilanadi) — seed to'liq yozilib, avtomatlashtirish bosqichma-bosqich
   qo'shilishi uchun.

## 10. AMALDAGI TA'RIFLAR — hisob nimaga tayanadi

Reja darajasida ochiq qolgan savollar kod yozilganda hal qilindi. Ular **bu yerda** yozilgan,
chunki raqamning ma'nosi shu ta'riflarga bog'liq.

| Savol | Qaror | Nega |
|---|---|---|
| **"To'lanmagan hisob"** (45 kunlik qarz sharti) | O'QUVCHI darajasida **FIFO**: barcha o'quv to'lovlari (vozvrat manfiy) bitta havzaga yig'ilib, eng eski oydan boshlab yopiladi; birinchi to'liq yopilmagan oy — "eng eski to'lanmagan". `StudentLedger` bilan bir xil | To'lovlarning KO'PIDA `GroupId` YO'Q (u faqat bitta guruhli o'quvchiga avto-teglanadi). Qat'iy `(o'quvchi, guruh, oy)` mosligi deyarli hamma hisobni "to'lanmagan" deb belgilab, faol o'quvchi sonini nolga tushirar va Bonus A ni yo'q qilardi |
| **A'zolik holati** (faol o'quvchi 1-sharti) | `CourseAnalytics.WasActiveAt` — SANALARDAN tiklanadi, `Status` dan EMAS | `Status` JORIY holat: bugun muzlatilgan a'zolik o'tgan oyda faol bo'lgan bo'lishi mumkin (`.claude/rules/course-analytics.md` §2). `PastPeriods` ham shu yo'l bilan hisobga olinadi |
| **Samaradorlik** manbai | Avval **cheklist** (`na` maxrajga KIRMAYDI), bo'lmasa **`WorkTask`**, ikkalasi ham bo'lmasa **1.0 + ochiq `Warning`** | Nol qaytarilsa har bir xodim o'lchanmagan narsa uchun eng past pog'onaga (0.75) tushib qolardi. Ikkala xom raqam alohida `KpiInputDto` bo'lib qaytadi — hech narsa yashirilmaydi |
| **Shartnoma** hodisasi | `Lead.ClosedByUserId` + `ClosedAt` (faqat `LeadsController.Convert` yozadi) | `LeadStage` da "yutuq" bayrog'i YO'Q; o'quvchiga aylantirish — yagona ishonchli hodisa |
| **Ketish sababi** | `AuditLog` matnidan `"— sabab: X"` ajratib olinadi va `ActionReason.OutOfControl` bilan solishtiriladi | `StudentGroup` da sabab USTUNI yo'q; `GroupSnapshotBuilder` ham shu usulni ishlatadi. Sabab topilmasa — HAM `leftControlled`, HAM `unknownReasonLeft` ga kiradi (jarima aynan shuning uchun) |
| **Guruh almashtirish ketish EMAS** | Oy oxirida boshqa guruhda hali FAOL bo'lgan o'quvchi ketganlar soniga KIRMAYDI | `.claude/rules/course-analytics.md` §1 bilan bir xil printsip — aks holda guruh almashtirish "ketdi" bo'lib, qo'rqinchli, ammo YOLG'ON churn ko'rsatardi |
| **Biriktirilmagan lidlar** | Har intake xodimning HAM `leads`, HAM `trialCame` soniga qo'shiladi; `leadsUnassigned` alohida qator bo'lib ogohlantiradi | Konversiya kasrining ikkala tomonida bir xil populyatsiya bo'lishi shart, aks holda nisbat ma'nosini yo'qotadi. Ikki intake admin bo'lsa ikkalasiga sanaladi — shu sabab ogohlantirish |

## 11. Cheklistni AVTOMATIK belgilash — `ChecklistAutoCheck`

Hozir **6** kalit qo'llangan (`Supported`): `tasks.overdue_zero` · `tasks.due_done` ·
`calls.missed_returned` · `calls.answer_speed` · `journal.yesterday_filled` ·
`leads.without_task_zero`. Qolgan **16** tasi ATAYIN QO'LDA.

⚠️ **`Supported` da yo'q kalit JIM e'tiborsiz qoldiriladi** va band oddiy qo'lda belgilanadi.
Shuning uchun seed to'liq yozilgan (66 band) — avtomatlashtirish bosqichma-bosqich qo'shiladi
va shablonni qayta yozish kerak bo'lmaydi.

⚠️ **Qo'lda qolganlarning sababi bir xil**: ular "ro'yxat ochildi", "xabar yuborildi",
"qo'ng'iroq qilindi" kabi bandlar bo'lib, tizimda ularni ISBOTLAYDIGAN yozuv yo'q. Taxmin
bilan avtomatlashtirish xodimni **noto'g'ri jarimalab** qo'yardi — bu tekshiruvlar
samaradorlik koeffitsienti orqali pulga bevosita ta'sir qiladi.

⚠️ **`leads.without_task_zero` — ATAYIN "fail-open"**: sxemada `Lead` ↔ `WorkTask` bog'lovchi
ustun YO'Q (yagona imkoniyat — lid Id'si `WorkTask.Tags` ichida). Agar xodimning ochiq
topshiriqlaridan birortasi ham uning ochiq lidlariga ishora qilmasa, bog'lanish
"ishlatilmayapti" deb hisoblanadi va band O'TADI. Aks holda tekshiruv HAR KUNI HAMMAGA
"bajarilmadi" berib, bonusni jimgina kesib turardi.

## 12. Oy boshi snapshoti — `KpiSnapshotService`

- 30 daqiqalik sikl, ishni AVVAL bajarib keyin kutadi (ya'ni startupda ham ishlaydi — server
  1-kuni o'chiq turgan bo'lsa ham oy boshi qo'lga kiritiladi).
- Faqat oyning **1–5-kunlari** yozadi; keyinroq hech narsa yozilmaydi va metrikalar jonli
  hisobga tushib `estimated: true` beradi. ⚠️ Yolg'on "oy boshi" dan ko'ra HALOL taxmin yaxshi.
- ⚠️ **O'TGAN oy hech qachon ustidan yozilmaydi** — snapshotning butun ma'nosi o'sha
  paytdagi holatni muzlatishda. Joriy oy esa faqat BUGUN olingan bo'lsa yangilanadi.
- ⚠️ **Upsert** (`(Month, RoleCode, UserId)` unikal) — 1-kuni restart bo'lsa `23505` bilan
  yiqilmasin.
- Markaz qatori: `UserId = ""` (§17 — Postgres'da NULL unikal indeksni ishlatmaydi).
