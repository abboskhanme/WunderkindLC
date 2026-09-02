---
description: Lid kiritish formasi — qo'lda lid kiritishda qaysi maydon so'raladi, qaysisi MAJBURIY, va markazning o'z qo'shimcha savollari.
paths:
  - "IntellectCRM.Application/Services/LeadEntryRules.cs"
  - "IntellectCRM.Application/Services/LeadEntryFormService.cs"
  - "IntellectCRM.Server/Controllers/LeadEntryFormController.cs"
  - "IntellectCRM.Client/src/pages/admin/forms/LeadEntryFormPage.tsx"
  - "IntellectCRM.Client/src/pages/admin/leads/LeadFormModal.tsx"
  - "IntellectCRM.Client/src/api/services/leadEntryForm.ts"
---

# «Lid kiritish formasi» qoidalari

Migratsiya: `AddLeadEntryForm` (20260902165910) — `LeadEntryFields` jadvali + `Leads.AnswersJson`.
Sozlanadi: **"O'quv bo'limi → Formalar → Lid kiritish formasi"** (`/admin/forms/lid-kiritish`,
ruxsat `leads.forms`). Amal qiladi: **"Lidlar" → «Yangi lid» oynasi** (`/admin/leads`).

## 1. ENG MUHIM — bu OMMAVIY forma EMAS

Bo'limda ikkita butunlay boshqa narsa yonma-yon turadi va ularni ADASHTIRMANG:

| | **Lid formalari** (`.claude/rules/lead-forms.md`) | **Lid kiritish formasi** (shu hujjat) |
|---|---|---|
| Kim to'ldiradi | tashqi MIJOZ (Instagram havolasidan) | markaz XODIMI (menejer) |
| Qayerda | ommaviy sahifa `/forma/{slug}` | admin oynasi `/admin/leads` |
| Nechta | har kanalga bittadan (`LeadForm.Source`) | **BITTA** (markazda bitta CRM formasi) |
| Entity | `LeadForm` + `LeadFormField` | `LeadEntryField` |
| Javoblar | `LeadFormSubmission.AnswersJson` | `Lead.AnswersJson` |

⚠️ Shuning uchun `LeadEntryField` da **`FormId` YO'Q** — jadvalda butun markazning yagona
formasi turadi.

## 2. TEKSHIRUV FAQAT QO'LDA KIRITISHDA

`LeadEntryFormService.ValidateAsync` AYNAN ikki joydan chaqiriladi: `POST /api/admin/leads`
va `PUT /api/admin/leads/{id}`.

⚠️ **Lid yaratadigan qolgan BESH joyda bu qoidalar QO'LLANMAYDI** — ommaviy lid formasi
(`LeadFormService`), daraja testi (`LevelTestService`), landing (`PublicLandingController`),
Instagram suhbati (`InstagramLeadBridge`) va Meta reklama leadgen (`MetaLeadBridge`).

**Sabab:** "Tug'ilgan kun majburiy" degan sozlama Instagram'dan kelayotgan lidni jimgina rad
etardi — markaz mijozini YO'QOTARDI, xato esa hech qayerda ko'rinmasdi. Sozlamaning maqsadi
XODIMNI intizomga solish, tashqi kanalni to'sish emas.

⚠️ **Yangi lid yaratadigan joy qo'shsangiz** — savol: bu XODIM qo'lda kiritayaptimi (tekshiruv
kerak) yoki tashqi kanaldan kelyaptimi (tekshiruv KERAK EMAS)?

## 3. Uch holat va NORMALIZATSIYA

Har standart maydonning holati: **`hidden`** (so'ralmaydi) · **`optional`** (ixtiyoriy) ·
**`required`** (majburiy). Qoida `LeadEntryRules.NormalizeState` / `NormalizeStandard` da
(sof funksiyalar, `LeadEntryFormTests`).

⚠️ **Chiqib bo'lmaydigan tuzoq YARATILMAYDI** — saqlashda uchta normalizatsiya majburiy:

1. **`hidden` + `required` MUMKIN EMAS** → `hidden` g'olib. Aks holda menejer formada
   ko'rmaydigan maydon tufayli lidni umuman saqlay olmasdi.
2. **F.I.SH (`fullName`) QULFLANGAN** — doim ko'rinadi va doim majburiy (`Locked: true`).
   Ommaviy formadagi "ism va telefon HAR DOIM so'raladi" bilan bir xil sabab: ismsiz lid
   kanbanda tanib bo'lmaydigan kartaga aylanardi.
3. **Maktab tumansiz ko'rsatilmaydi** — maktab ro'yxati TUMANDAN quriladi (kaskad), tumansiz
   select doim bo'sh turardi. `districtId = hidden` → `schoolId` ham `hidden`.

⚠️ Noma'lum holat **`optional`** ga tushadi — sozlama buzilib qolgandan ko'ra ishlagani yaxshi.

## 4. QATOR YO'QLIGI — ODDIY HOLAT (backfill KERAK EMAS)

Standart maydonning `LeadEntryFields` da qatori bo'lmasa u **standart holatda** ishlaydi:
ko'rinadi, majburiy emas. Ya'ni **bo'sh jadval = hozirgacha bo'lgan xatti-harakat AYNAN o'zi**.

- eski o'rnatishlarni to'ldirish (backfill) kerak emas;
- `Program.cs` da **seed YO'Q** (`ContactStage` dan farqli — u yerda ustun kartaning "uyi",
  bu yerda esa faqat sozlama);
- kelajakda `LeadEntryRules.Standard` katalogiga yangi maydon qo'shilsa forma o'z-o'zidan
  ishlayveradi.

Katalog — **YAGONA manba** (`LeadEntryRules.Standard`): kalitlar `Lead` maydonlarining
camelCase nomlari. `LeadEntryRules.ValueOf` da har kalit uchun qiymat o'quvchisi bo'lishi
SHART — unutilgan kalit "hech qachon to'ldirilmagan" bo'lib, uni majburiy qilish lidni
butunlay saqlab bo'lmaydigan holga keltirardi (test buni qulflaydi).

## 5. STANDART maydonlarning TARTIBI sozlanmaydi

Sozlanadigani faqat HOLAT. Sabab: standart maydonlar formada juftlik va bog'liqlik bilan
chiziladi (tuman → maktab kaskadi, ikki ustunli setka) — erkin tartib bu tuzilishni buzardi.

**Erkin tartib — QO'SHIMCHA savollarda** (`Order`), ular standart maydonlardan KEYIN chiziladi.

## 6. QO'SHIMCHA savollar — lid formasi bilan BIR XIL qoidalar

Turlari `LeadFormService.Kinds` dan (`text | textarea | number | select | radio | checkbox`),
chegaralari ham o'sha yerdan (`MaxFields` 25, `MaxOptions` 30, `MaxAnswerLength` 500) —
ikkinchi katalog YARATILMAGAN.

- variantsiz qolgan `select/radio/checkbox` **oddiy matnga tushiriladi** (menejer hech narsa
  tanlay olmaydigan bo'sh select ko'rmasin);
- variantli savolda faqat MAVJUD variant qabul qilinadi;
- bitta tanlovli savolga bir nechta javob kelsa — birinchisi;
- yorlig'i bo'sh savol umuman saqlanmaydi.

**Saqlash — bandlar TO'LIQ almashtiriladi** (lid formasidagi `WriteFields` bilan bir xil,
sodda va ishonchli usul). Qatorlarning `Id` si o'zgarishi tarixni buzmaydi, chunki:

⚠️ **JAVOB SAVOL MATNI bilan saqlanadi** (`Lead.AnswersJson` → `[{"question","answers"}]`,
`LeadFormSubmission.AnswersJson` bilan AYNAN bir format). Sozlama keyin tahrirlansa yoki
savol o'chirilsa ham lidning javobi "noma'lum savolga javob" bo'lib qolmaydi.

## 7. TAHRIRLASHDA ma'lumot JIMGINA o'chirilmaydi

Ikkita alohida himoya:

1. **Yashirilgan maydonning qiymati tegilmaydi** — klient `form` state'ini `initial` dan
   to'liq to'ldiradi va yashirin maydonni chizmasa ham o'sha qiymati bilan yuboradi;
   server esa faqat KO'RINADIGAN+MAJBURIY maydonlarni tekshiradi.
2. **`answers` uzatilmasa (null) qo'shimcha javoblar TEGILMAYDI** — `ValidateAsync` `Json`
   sifatida `null` qaytaradi, controller esa `AnswersJson` ga umuman tegmaydi. Bu eskirgan
   yoki qo'shimcha savollarni bilmaydigan chaqiruvchi lidning javoblarini o'chirib
   yubormasligi uchun.

⚠️ **YARATISHDA esa bo'sh lug'at uzatiladi (null EMAS)** — ya'ni majburiy qo'shimcha savol
chindan tekshiriladi. Ikki oqimning farqi ATAYIN.

⚠️ Tahrirlashda majburiy maydon qoidasi ham AMAL QILADI: aks holda maydon bir marta
to'ldirilib, keyingi tahrirda bo'shatib yuborilardi. Demak boshqa kanaldan kelgan chala lid
BIRINCHI tahrirda to'ldirilishi kerak — bu ataylab (sozlamaning butun maqsadi shu).

## 8. XATO KO'RSATILADI — jim rad etish YO'Q

Server `400` + `{ message }` qaytaradi. Klientda «Yangi lid» oynasi:

- xato bo'lsa **OCHIQ qoladi** va tepasida qizil matn chiqadi (ilgari `LeadsPage` xatoni
  umuman ushlamas, modal yopilib ketar va menejer lid saqlanmaganini SEZMASDI);
- `setLeads` optimistik emas — server javobidan KEYIN yoziladi.

⚠️ Sozlamani yuklab bo'lmasa (tarmoq xatosi) oyna **zaxira** holatga tushadi: barcha standart
maydonlar ko'rinadi, faqat F.I.SH majburiy. Sozlama tufayli lid kiritish ISHLAMAY qolmasin.

## 9. RUXSAT

| Amal | Kalit |
|---|---|
| Sozlamani O'QISH (`GET /api/admin/lead-entry-form`) | **har qanday xodim** (odatdagi GET istisnosi) |
| Sozlamani SAQLASH (`PUT`) | `leads.forms:edit` |
| Lid kiritish/tahrirlash | avvalgidek `leads.list:create` / `:edit` |

⚠️ **`ReadRequiresPerm` ATAYIN QO'YILMAGAN** (`LeadFormsController` dan farqli): sozlamani
AYNAN lidlar sahifasi o'qiydi va u yerdagi xodimda ko'pincha faqat `leads.list` bo'ladi.
GET'ni `leads.forms` bilan yopsak, lid kiritish oynasi o'sha xodimda umuman ochilmasdi.
Javobda shaxsiy ma'lumot yo'q (faqat maydon nomlari va holatlari) — yopishning ma'nosi ham yo'q.

⚠️ **Yangi ruxsat kaliti QO'SHILMAGAN** — `adminPermissions` katalogi va
`PermissionCatalogTests` tegilmaydi.

## 10. Audit

Sozlama o'zgarishi `LeadForm` turida, `EntityId = "entry-form"` bilan yoziladi — ya'ni
"O'zgarishlar tarixi"da **"Lidlar"** bo'limida ko'rinadi (`AuditSections`, yangi tur
qo'shilmadi). Matnda qo'shimcha savollar va majburiy maydonlar SONI bor.

Lidning O'ZI avvalgidek: yaratish `LeadEvent("created")`, tahrir esa tarixsiz (bu modul
o'zgartirmadi).

## 11. Yangi ko'rsatkich/maydon qo'shsangiz

1. Standart maydon — `LeadEntryRules.Standard` katalogiga **va** `ValueOf` ga qo'shing
   (test ikkovini qulflaydi), keyin klientda `LeadFormModal` da `show()/req()` bilan o'rang.
2. `Lead.AnswersJson` ni hisob-kitobga (statistika, AI, hisobot) **QO'SHMANG** — u erkin matn,
   ko'rinish qatlamining ma'lumoti (`year-freeze.md` §1 va `contacts.md` §3.66 dagi bilan bir
   xil printsip).
3. Yangi lid oqimi qo'shsangiz — §2 dagi savolga javob bering.

## 12. Testlar

`IntellectCRM.Tests/LeadEntryFormTests.cs` — 21 test: bo'sh sozlamada standart xatti-harakat,
majburiy maydon rad etishi, `hidden`+`required` normalizatsiyasi, F.I.SH qulfi, tuman→maktab
kaskadi, majburiy qo'shimcha savol, javobning savol MATNI bilan saqlanishi, begona variantning
rad etilishi, bitta/ko'p tanlov, variantsiz `select` ning matnga tushishi, to'liq almashtirish,
`MaxFields` chegarasi, tartib, `answers = null` da javoblarning tegilmasligi va katalog
yaxlitligi.
