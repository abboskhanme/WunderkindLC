# B2B SOTUV MODULI + AI SALES COPILOT — Claude Code uchun to'liq topshiriq

> Bu fayl — **Claude Code'ga beriladigan prompt**. Uni to'liq nusxalab yuboring.
> Loyiha: `WunderkindLC` (ASP.NET Core 8 + EF Core + PostgreSQL 16 · React 19 + TS + Vite + Tailwind).

---

## 0. VAZIFA (bir jumlada)

WunderkindLC ichida **yangi mustaqil bo'lim** yarat: **«B2B sotuv»** — xususiy maktablar, litseylar
va korporativ mijozlarga IELTS / Multilevel / Milliy sertifikat xizmatlarini (autsorsing,
diagnostika, korporativ paketlar) sotish jarayonini lidni saralashdan shartnoma imzolashgacha
boshqaradigan kanban + har bir bitim uchun **AI Sales Copilot** (Gemini) — menejerga bosqich,
bitim salomatligi, e'tirozlar tahlili, **so'zma-so'z aytiladigan skript** va **keyingi qat'iy qadam**
beradi.

---

## 1. AVVAL O'QI (majburiy — kod yozishdan OLDIN)

Bu loyihada har modulning yozilmagan qoidalari `.claude/rules/` da. Quyidagilarni **to'liq** o'qi:

| Fayl | Nima uchun |
|---|---|
| `.claude/rules/ai-analysis.md` | **ENG MUHIM** — AI tahlil arxitekturasi (raqam kodda, narrativ AI'da), maxfiylik chegarasi, `{ai, metrics}` saqlash |
| `.claude/rules/crm-leads.md` | mavjud CRM (lidlar, bosqichlar, `LeadEvent`, menejerlar kesimi) — B2B undan NIMASI bilan farq qilishini tushunish uchun |
| `.claude/rules/permissions.md` | yangi bo'lim/sahifa ruxsati qo'shishning 4 qadami va `PermissionCatalogTests` |
| `.claude/rules/audit.md` | `AuditSections` xaritasi va `audit.Record` qoidalari |
| `.claude/rules/contacts.md` §7.55 | **davrga/obyektga bog'langan AI tahlil**ning eng yaqin namunasi |
| `.claude/rules/uploads-security.md` | KP/shartnoma fayli manzili javobga chiqsa nima qilish kerak |
| `.claude/rules/tests.md` | test yozish uslubi |

Kod namunalari (uslub va tuzilmani AYNAN shulardan ol, nusxa ko'chirma — qayta ishlat):

- `WunderkindLC.Application/Services/ContactAiAnalysisService.cs` — AI servis skeleti
  (kesh tekshiruvi → bo'sh ma'lumot tekshiruvi → kalit tekshiruvi → prompt → `ParseNarrative` →
  `Sanitize` → saqlash);
- `WunderkindLC.Application/Services/ContactService.cs` — **sof statik katalog** (bosqich/natija
  kalitlari yagona manbada);
- `WunderkindLC.Application/Services/ContactReport.cs` — deterministik hisob-kitob;
- `WunderkindLC.Application/Services/GeminiService.cs` — `GenerateAsync(..., jsonMode: true)`;
- `WunderkindLC.Server/Controllers/ContactsController.cs` — `ai-analyses` / `ai-analysis` endpointlari;
- `WunderkindLC.Server/Controllers/CallsController.cs` (`{id}/transcribe`, `{id}/analyze`) —
  transkript va suhbat tahlili allaqachon bor, **qayta yozilmaydi**;
- `WunderkindLC.Client/src/components/ai/ContactAiPanel.tsx` + `components/ai/AiParts.tsx` +
  `lib/ai.ts` — AI panelining UI qismlari;
- `WunderkindLC.Client/src/pages/admin/leads/` (`LeadsPage`, `LeadColumn`, `LeadCard`,
  `LeadDetailModal`) — kanban naqshi.

---

## 2. ARXITEKTURA QARORI — nega ALOHIDA modul (mavjud `Lead` QAYTA ISHLATILMAYDI)

Mavjud `Lead` — **JISMONIY SHAXS** (o'quvchi): `FullName`, `Gender`, `BirthDate`, `FatherPhone`,
`MotherPhone`, `InterestSubject`, va `POST /leads/{id}/convert` uni **`Student`ga aylantiradi**.
Butun CRM statistikasi (`LeadCrmOverview`, `LeadAnalytics`, `LeadOutcome`, voronka AI tahlili)
shu ma'noga qurilgan: «lid → o'quvchi → pul».

**Maktab yoki kompaniya** o'quvchiga aylanmaydi — u shartnoma imzolaydi va ortidan **o'nlab**
o'quvchi keladi. Agar B2B bitimlar `Leads` jadvaliga qo'yilsa:

- CRM konversiya foizi buziladi (100 o'quvchi keltirgan maktab «1 ta aylanmagan lid» bo'lib qoladi);
- `LeadCrmOverview` dagi «jami lid» raqami ikki xil narsani qo'shib yuboradi;
- voronka AI tahlili noto'g'ri raqam ustiga xulosa yozadi.

⚠️ Shu sababli: **yangi entitylar, yangi controller, yangi ruxsat bo'limi.** Mavjud lidlar
moduliga (`LeadsController`, `LeadStage`, `LeadEvent`, `CrmStatsPage`) **TEGILMAYDI**.

Bog'lanish nuqtasi bitta va u ixtiyoriy: B2B bitim yopilgach undan kelgan o'quvchilar odatdagi
`Student` sifatida kiritiladi va `B2BDeal.Id` ga `Student` orqali emas, `B2BDealStudent`
bog'lash jadvali orqali biriktiriladi (§3.6) — ya'ni «bu maktab bizga nechta o'quvchi va qancha
pul keltirdi» savoliga javob bor, lekin CRM raqamlari aralashmaydi.

---

## 3. DOMAIN — entitylar (`WunderkindLC.Domain/Entities.cs`, oxiriga yangi bo'lim izohi bilan)

Loyiha konvensiyasi: `Id` — `Guid.NewGuid().ToString()`, sanalar **satr** (`"yyyy-MM-dd"` yoki
ISO `"yyyy-MM-ddTHH:mm:ss"`, `AppClock` orqali, Toshkent vaqti), `decimal` — pul.
Har maydonga **o'zbekcha XML izoh** — nima ekani emas, **NEGA shunday** ekani yoziladi.

### 3.1 `B2BAccount` — TASHKILOT (mijoz)
`Name`, `Type` (`school` | `lyceum` | `company` | `gov` | `other`), `DistrictId?`, `Address`,
`StudentCount` (o'quvchilar/xodimlar soni), `Website`, `Note`, `IsActive`,
`CreatedAt`, `CreatedBy`.

> `DistrictId` mavjud `District` ma'lumotnomasidan (o'quvchilarniki bilan bir xil) — yangi
> ma'lumotnoma yaratilmaydi.

### 3.2 `B2BContact` — TASHKILOTDAGI ODAM (LPR va boshqalar)
`AccountId`, `FullName`, `Position` (lavozim), `Phone`, `PhoneKey` (mahalliy 9 raqam —
`PhoneUtil.Key`, qidiruv uchun **indekslangan**), `Email`, `IsDecisionMaker` (LPRmi),
`Note`, `CreatedAt`.

> ⚠️ `PhoneKey` ni **qo'lda to'ldirma** — `AppDbContext.SaveChanges` da `Lead.PhoneKey` uchun
> yozilgan `SyncLeadPhoneKeys` naqshi bilan bir xil avtomatik to'ldirish qo'sh (yangi joy
> qo'shilganda unutilmasin).

### 3.3 `B2BDeal` — BITIM (voronkaning o'zi)
`AccountId`, `Title`, `Stage` (§4 kalitlari), `OwnerUserId` + `OwnerName` (mas'ul menejer,
`AppUser`), `Services` (vergul bilan: `ielts,multilevel,milliy,outsourcing,diagnostics`),
`StudentsPlanned` (qamrab olinishi kutilayotgan o'quvchi soni), `Amount` (bitim summasi, so'm),
`Probability` (menejer qo'ygan %), `ExpectedCloseDate` (`"yyyy-MM-dd"`),
`Status` (`open` | `won` | `lost`), `LostReason`, `Source` (mavjud `LeadSource` NOMI —
lidlardagi konvensiya bilan bir xil), `StageChangedAt`, `LastActivityAt`,
`CreatedAt`, `CreatedBy`, `ClosedAt`.

⚠️ `Stage` va `Status` **ikkalasi ham** kerak: bosqich — voronkadagi o'rni, holat — bitim tirikmi.
Yutqazilgan bitim oxirgi bosqichida qoladi (**qaysi bosqichda yutqazganimiz** — eng qimmatli
ma'lumot; buni `Stage`ni "lost" ga o'zgartirib yo'qotib qo'yma).

### 3.4 `B2BEvent` — BITIM TARIXI (hamma narsaning manbai)
`DealId`, `Type` (`created` | `stage` | `note` | `call` | `meeting` | `proposal` | `objection` |
`file` | `ai`), `Text`, `FromStage`, `ToStage`, `ActorUserId?`, `ActorName`,
`CallId?` (mavjud `Call` yozuvi bilan bog'lash), `TranscriptText` (qo'lda joylashtirilgan
transkript/yozishma), `CreatedAt`, `Date` (`"yyyy-MM-dd"` — kunlik hisobot **shu ustun bo'yicha**
guruhlanadi, ISO'dan `Substring` bilan emas: indeksdan foydalana olmasdi).

⚠️ `LeadEvent`dagi dars: `ActorUserId` **BOSHIDAN** yozilsin — mavjud CRM'da u kech qo'shilgani
uchun menejerlar kesimi faqat 2026-08 dan keyingi tarixni ko'rsatadi. Bu xato takrorlanmasin.

### 3.5 `B2BObjection` — E'TIROZ (alohida entity, `B2BEvent` ichida ko'milmagan)
`DealId`, `Kind` (§4.3 katalogi), `Text` (mijoz aynan nima dedi), `AnswerText` (menejer nima dedi),
`Resolved`, `CreatedAt`, `ActorName`.

> ⚠️ NEGA alohida: «qaysi e'tiroz bizni eng ko'p to'xtatyapti» — butun bo'limning asosiy
> hisoboti. Erkin matn ichida qolsa uni `LIKE` bilan qidirishga majbur bo'lardik.

### 3.6 `B2BDealStudent` — bitimdan kelgan o'quvchilar (natijani o'lchash)
`DealId`, `StudentId`, `LinkedAt`, `LinkedBy`. Unikal `(DealId, StudentId)`.

> Bu jadval orqali «shu maktab qancha o'quvchi va qancha PUL keltirdi» hisoblanadi
> (`FinanceTransaction` orqali — `LeadOutcome` dagi ta'rif bilan **bir xil**: kirim/tuition
> minus chiqim/refund). Yangi pul hisoblash mantig'i **yaratilmaydi**.

### 3.7 `B2BAiAnalysis` — copilot javobi (saqlanadi)
`DealId`, `Date` (`"yyyy-MM-dd"`), `CreatedAt`, `Model`, `Stage` (tahlil paytidagi bosqich),
`HealthScore` (0..100), `Summary` (qisqa xulosa — tarix ro'yxatida ko'rinadi),
`ResultJson` (`{ ai, metrics }` — `.claude/rules/ai-analysis.md` bilan bir xil),
`InputHash` (§7.5), `EventCountAtRun` (tahlil paytidagi hodisalar soni).

### 3.8 `B2BPlaybook` — SOTUV BILIM BAZASI (bitta qator, `CenterMeta` naqshida)
`ServicesText` (markaz nima sotadi va qanday paketlar bor), `PricingText` (narx/paket shartlari),
`CasesText` (keyslar, natijalar, raqamlar), `GuaranteesText` (kafolatlar), `ObjectionsText`
(tayyor javoblar), `ToneText` (murojaat uslubi), `UpdatedAt`, `UpdatedBy`.

> ⚠️ **NEGA SHART:** loyihaning AI qoidasi — *«Hech narsani TO'QIB CHIQARMA»*. Playbook'siz
> copilot narx, kafolat va keyslarni **o'ylab topadi** va menejer uni mijozga aytadi. Playbook
> bo'sh bo'lsa promptga «narx/keys ma'lumoti berilmagan — ularni O'YLAB TOPMA, menejerga
> aniqlashtirishni tavsiya qil» ko'rsatmasi qo'shiladi.

### Migratsiya
`dotnet ef migrations add AddB2BSales -p WunderkindLC.Infrastructure -s WunderkindLC.Server`
Indekslar: `B2BDeal(Stage)`, `B2BDeal(Status, StageChangedAt)`, `B2BEvent(DealId, Date)`,
`B2BContact(PhoneKey)`, `B2BAiAnalysis(DealId, Date)`, `B2BDealStudent(DealId, StudentId)` unikal.
`IAppDbContext` ga ham, `AppDbContext` ga ham `DbSet`lar qo'shiladi (Application qatlami
`IAppDbContext` orqali ishlaydi).

---

## 4. BOSQICHLAR VA KATALOGLAR — `B2BSalesService` (sof statik, YAGONA manba)

`WunderkindLC.Application/Services/B2BSalesService.cs` — `ContactService.cs` naqshida:
sof funksiyalar, bazaga tegmaydi, to'liq testlangan.

### 4.1 Bosqichlar (kalitlar **O'ZGARMAS** — AI kontrakti ham shularni qaytaradi)

| Kalit | Yorliq (UI, o'zbekcha) | Maqsad |
|---|---|---|
| `LEAD_QUALIFICATION` | Lidni saralash | LPR aniqlash: ta'sischi/direktor/HRD. Kerakli ma'lumot: o'quvchi/xodim soni, hozirgi imtihon ko'rsatkichlari, maqsadli sertifikat |
| `FIRST_CONTACT` | Birinchi aloqa | Sotuv EMAS — 20 daqiqalik uchrashuv yoki diagnostika kelishuvi |
| `AUDIT_DIAGNOSTICS` | Diagnostika | Bazada test o'tkazish, zaif nuqtani RAQAMDA ko'rsatish (SPIN — muammoni kattalashtirish) |
| `PROPOSAL_KP` | Tijoriy taklif | Raqam, kafolat, ROI bilan KP taqdimoti |
| `NEGOTIATION_OBJECTIONS` | E'tirozlar | «Qimmat», «o'z o'qituvchilarimiz bor», «keyinroq» — yopish va pilotga o'tish |
| `CLOSING_CONTRACT` | Bitimni yopish | Pilot guruh, to'lov shartlari, yillik autsorsing shartnomasi |

⚠️ **Bosqichlar `LeadStage` kabi bazadan boshqarilmaydi (jadval EMAS).** Sabab: AI kontrakti
(`current_stage`) aynan shu kalitlarni qaytaradi — admin bosqich nomini o'zgartira olsa, prompt
bilan ma'lumot **ayri ketardi** va copilot mavjud bo'lmagan bosqichni nomlab qo'yardi.
Yorliqni o'zgartirish mumkin (`Label`), **kalitni yo'q**.

Funksiyalar: `Stages` (tartibli ro'yxat), `IsValidStage(key)`, `IndexOf(key)`,
`CanTransitionTo(from, to)` (**orqaga qaytish RUXSAT** — sotuvda bu normal, oldinga esa
**bittadan ko'p sakrash mumkin emas** — sakralgan bosqich «bo'ldi» bo'lib ko'rinib, voronkani
yolg'onlashtirardi), `RequiredFields(stage)` (bosqichga o'tish uchun majburiy ma'lumot, §4.2).

### 4.2 Bosqich darvozalari (`RequiredFields`)
- `FIRST_CONTACT` ga o'tish → hech bo'lmasa bitta `B2BContact` bo'lishi shart;
- `PROPOSAL_KP` ga o'tish → `Amount > 0` **va** `IsDecisionMaker` bo'lgan kontakt bo'lishi shart;
- `CLOSING_CONTRACT` ga o'tish → `ExpectedCloseDate` to'ldirilgan bo'lishi shart.

Darvoza buzilsa server **400** va o'zbekcha tushunarli matn qaytaradi (`ContactService`dagi
xato matni uslubida). Frontend darvozani oldindan ko'rsatadi, lekin **haqiqiy tekshiruv serverda**.

### 4.3 E'tiroz turlari (`ObjectionKinds`)
`price` (qimmat) · `own_teachers` (o'z o'qituvchilarimiz bor) · `later` (keyinroq o'ylaymiz) ·
`no_budget` (byudjet yo'q) · `no_need` (ehtiyoj ko'rmayapmiz) · `competitor` (boshqa bilan
ishlaymiz) · `trust` (ishonch/natijaga shubha) · `bureaucracy` (yuqoridan ruxsat kerak) · `other`.

### 4.4 Yutqazish sabablari va tashkilot turlari
`LostReasons`, `AccountTypes`, `ServiceKinds` — shu yerda, yorliqlari bilan.

### 4.5 Yorliqlar frontendga qayerdan boradi
`GET /api/admin/b2b/meta` → bosqichlar, e'tiroz turlari, yutqazish sabablari, xizmatlar,
menejerlar ro'yxati. Frontenddagi `b2bLabels.ts` — **faqat zaxira** (`careerLabels.ts` /
`bookLabels.ts` konvensiyasi), yagona haqiqat manbai server.

---

## 5. DETERMINISTIK RAQAMLAR — `B2BAnalytics` (sof funksiyalar, testlangan)

⚠️ **`.claude/rules/ai-analysis.md` ning bosh qoidasi: raqamni KOD hisoblaydi, AI faqat yozadi
va baho qo'yadi.** AI hech qachon o'zi sanamaydi.

### 5.1 Bitta bitim uchun (copilot promptiga va bitim sahifasiga)
`DaysInStage` (`StageChangedAt` dan bugungacha), `DaysSinceLastActivity`, `TotalEvents`,
`CallCount`, `MeetingCount`, `ProposalSent` (bool), `ObjectionCount` / `ObjectionsOpen`,
`ContactCount`, `HasDecisionMaker`, `StageHistory[]` (har bosqichda necha kun turgani),
`AgeDays`, `Amount`, `StudentsPlanned`.

### 5.2 Bo'lim hisoboti (`/analytics?from&to`)
- **Voronka**: har bosqichda nechta ochiq bitim, jami summa, o'rtacha bosqichda turish kunlari;
- **Konversiya**: bosqichdan bosqichga o'tish foizi (`B2BEvent` `stage` hodisalaridan);
- **Menejerlar kesimi**: bitimlar, yutuq, yutqazish, summa, o'rtacha sikl uzunligi;
  ⚠️ **PUL BIR MARTA SANALADI** — `LeadAnalytics` dagi qoida bilan **bir xil**: summa faqat
  bitimni **YOPGAN** (`won`) menejerga yoziladi, aks holda jadvaldagi jami markaz tushumidan
  oshib ketardi;
- **E'tirozlar kesimi**: qaysi e'tiroz necha marta, qaysisi hal bo'lgan, qaysi bosqichda paydo bo'ladi;
- **Yutqazish sabablari kesimi** + **qaysi bosqichda yutqazilgan**;
- **Natija**: `B2BDealStudent` orqali kelgan o'quvchilar soni va **sof tushum** (`LeadOutcome`
  dagi AYNAN o'sha ta'rif — kirim/tuition minus chiqim/refund).

⚠️ **«Yopildi» hali PUL emas** — `won` bitim va undan kelgan real tushum **alohida** ikki raqam
bo'lib ko'rsatiladi (`.claude/rules/crm-leads.md` §4 bilan bir xil mantiq).

⚠️ **BO'SH DAVRDA jimgina 0 chizma** — «bu davrda bitim harakati bo'lmagan» deb ochiq yoz.

---

## 6. MAXFIYLIK CHEGARASI (bu yerda ATAYIN boshqacha — sababi bilan)

`.claude/rules/ai-analysis.md` ikki xil chegarani ko'rsatadi: voronka tahlilida ism/telefon
**umuman** promptga tushmaydi, guruh tahlilida esa o'quvchi ismi tushadi (tavsiya aynan shu
odamlar haqida).

**B2B copilot — ikkinchi holat**, lekin qisman:

| Maydon | Promptga | Nega |
|---|---|---|
| Tashkilot nomi | ✅ | Bitim aynan shu tashkilot bilan; skript unga murojaat qiladi |
| LPR ismi va **lavozimi** | ✅ | «LPR aniqlandimi» — vazifaning O'ZI; skriptda unga murojaat qilinadi |
| Kontakt **telefoni / email** | ❌ **HECH QACHON** | Xulosa uchun kerak emas — copilot «qo'ng'iroq qil» deydi, raqamni terish menejerning ishi |
| Qo'ng'iroq transkripti / yozishma | ✅ (kesilgan) | Tahlilning asosiy xom ashyosi |
| Menejer (xodim) ismi | ✅ | Ichki ma'lumot, «kim qanday sotmoqda» maqsadli savol |
| Bitimdan kelgan **o'quvchilar ismi** | ❌ | B2B bitim tahliliga aloqasi yo'q — faqat SON kerak |

Transkript kesish: bitta hodisada **4000 belgigacha**, promptda **eng yangi 10** hodisa
(`B2BAiAnalysisService.MaxTranscriptChars` / `MaxEvents` konstantalari bilan) — prompt shishmasin
va token narxi ushlab turilsin. Nechtasi kesilgani `metrics` da ochiq qaytadi (**jim qirqilmaydi**).

---

## 7. AI SALES COPILOT — `B2BAiAnalysisService`

`ContactAiAnalysisService.cs` ni **skelet** sifatida ol (tartib, `Sanitize`, `ParseNarrative`,
kod-fence tozalash, `{ai, metrics}` saqlash — hammasi bir xil).

### 7.1 Tekshiruvlar tartibi (ATAYIN shu ketma-ketlik)
1. Bitim bormi → yo'q bo'lsa 404;
2. **Kesh** (§7.5) → bor bo'lsa Gemini **CHAQIRILMAYDI**, mavjudi qaytadi (`AlreadyFresh = true`);
3. **Ma'lumot yetarlimi** — bitimda `created` dan boshqa hech qanday hodisa yo'q **va** kontakt
   ham yo'q bo'lsa: *«Bitimda hali ma'lumot yo'q — hech bo'lmasa bitta aloqa yoki kontakt
   qo'shing»* (foydalanuvchining TANLOVI haqidagi xato);
4. **API kaliti** — `AppSecrets.GeminiApiKey` (bu tekshiruv 2 va 3 dan **KEYIN**: keshlangan
   natija kalitsiz ham ko'rinishi kerak, bo'sh bitim esa kalitdan qat'i nazar tahlil qilinmaydi).

### 7.2 Promptga nima ketadi
1. **ROL** (quyida, o'zgarmas matn);
2. **Playbook** (§3.8) — markazning xizmatlari, narxi, keyslari, kafolatlari, e'tirozlarga
   tayyor javoblari. Bo'sh bo'lsa — «bu ma'lumot berilmagan, O'YLAB TOPMA» ko'rsatmasi;
3. **Bitim holati JSON'da** — tashkilot (nomi, turi, o'quvchi soni, tumani), kontaktlar
   (ism + lavozim + LPRmi; **telefon YO'Q**), bitim (bosqich, summa, xizmatlar, kutilayotgan
   sana, mas'ul menejer), `B2BAnalytics` deterministik raqamlari (§5.1), e'tirozlar ro'yxati;
4. **Hodisalar lentasi** — eng yangi 10 tasi, turi + sana + matn (+ transkript, kesilgan);
5. **Oldingi tahlil konteksti** — `ContactAiAnalysisService` dagi `prevContext` naqshi bilan
   AYNAN bir xil (oldingi xulosa + ball, «nima o'zgardi» ni yozish buyrug'i).

### 7.3 ROL matni (promptga o'zgarishsiz kiradi)

```
Sen O'zbekistondagi yetakchi o'quv markazining B2B yo'nalishi bo'yicha eng yuqori darajadagi
Strategik Sotuv Maslahatchisi va Sales Copilotisan.

Vazifang: xususiy maktablar, litseylar va korporativ mijozlarga IELTS, Multilevel va Milliy
sertifikatga tayyorlash xizmatlarini (autsorsing, diagnostika, korporativ paketlar) sotish
jarayonini lidni saralashdan shartnoma imzolashgacha boshqarish, tahlil qilish va menejerga
ANIQ harakatlar rejasini berish.

VORONKA BOSQICHLARI VA HAR BIRIDAN NIMA TALAB QILINADI:
1. LEAD_QUALIFICATION — LPR (ta'sischi / direktor / HRD) aniqlanadi. Kerakli ma'lumot:
   o'quvchi yoki xodimlar soni, hozirgi imtihon ko'rsatkichlari, maqsadli sertifikatlar.
2. FIRST_CONTACT — maqsad SOTUV EMAS: LPR bilan 20 daqiqalik yuzma-yuz uchrashuv yoki
   diagnostika kelishuvi. Ilgak: "bitiruvchilarning grantga kirish ko'rsatkichini oshirish"
   yoki "bepul IELTS Mock / diagnostika auditi".
3. AUDIT_DIAGNOSTICS — tashkilot bazasida test o'tkaziladi, zaif nuqta RAQAMDA ko'rsatiladi
   (SPIN metodikasi: muammoni kattalashtirish).
4. PROPOSAL_KP — raqam, kafolat va ROI ko'rsatilgan tijoriy taklif LPRga taqdim etiladi.
5. NEGOTIATION_OBJECTIONS — "qimmat", "o'zimizning o'qituvchilarimiz bor", "keyinroq o'ylab
   ko'ramiz" kabi e'tirozlar yopiladi va pilot loyihaga o'tiladi.
6. CLOSING_CONTRACT — pilot guruh ishga tushiriladi, to'lov shartlari kelishiladi, yillik
   autsorsing shartnomasi imzolanadi.
```

### 7.4 Javob kontrakti (`jsonMode: true`, `B2BAiNarrativeDto`)

Foydalanuvchi bergan kontrakt **saqlanadi**, lekin loyiha uslubiga moslashtiriladi (izohlar
o'zbekcha, `deal_health_score` — **butun son 0..100**, satr emas):

```json
{
  "current_stage": "LEAD_QUALIFICATION | FIRST_CONTACT | AUDIT_DIAGNOSTICS | PROPOSAL_KP | NEGOTIATION_OBJECTIONS | CLOSING_CONTRACT",
  "deal_health_score": 0,
  "client_analysis": {
    "lpr_status": "Aniqlangan (Ism, Lavozim) yoki Aniqlanmagan",
    "identified_pain_points": ["mijozning og'riqli nuqtalari"],
    "current_objections": ["aytilgan yoki YASHIRIN e'tirozlar"]
  },
  "sales_script_for_manager": "menejer keyingi qo'ng'iroq/uchrashuvda SO'ZMA-SO'Z aytadigan, psixologik asoslangan gaplar va savollar",
  "next_action": "keyingi QAT'IY qadam (masalan: ertaga 14:00 da diagnostika natijalarini taqdim qilish uchun uchrashuv belgilash)",
  "recommended_materials": "taqdim etilishi kerak bo'lgan hujjat (Mock test hisoboti, KP PDF, keyslar taqdimoti)",
  "risks": ["bitimni yo'qotish xavflari"],
  "baholar": { "malumot": 0, "aloqa": 0, "ehtiyoj": 0, "qaror": 0, "umumiy": 0 },
  "trend": "yaxshilanmoqda | barqaror | yomonlashmoqda"
}
```

Qoidalar promptda ochiq yoziladi:
- **`current_stage` — bu AI ning HUKMI**, menejer qo'ygan bosqich emas. Farq bo'lsa
  `sales_script_for_manager` boshida sababi yoziladi («siz KP bosqichida deb belgilagansiz,
  lekin LPR hali aniqlanmagan — bu qaytish kerakligini bildiradi»). ⚠️ **AI bosqichni O'ZI
  KO'CHIRMAYDI** — bazadagi `Stage` faqat odam tomonidan o'zgartiriladi;
- har da'vo **berilgan raqam** bilan asoslanadi («7 kundan beri harakat yo'q», «3 ta e'tirozdan
  2 tasi ochiq»);
- **TO'QIB CHIQARMA**: playbook'da yo'q narx/kafolat/keys aytilmaydi;
- ma'lumot kam bo'lsa buni **OCHIQ ayt** va xulosani shartli qil;
- hammasi **O'ZBEK tilida, lotin alifbosida**;
- `baholar` — 0..100 butun sonlar: `malumot` (bitim haqida ma'lumot to'liqmi), `aloqa`
  (aloqa faolligi va sifati), `ehtiyoj` (ehtiyoj aniqlanganmi, SPIN ishlaganmi), `qaror`
  (LPR va qaror qabul qilish jarayoni tushunarlimi), `umumiy`.

`Sanitize` — barcha `null`larni bo'sh satr/ro'yxatga aylantiradi, `baholar` va
`deal_health_score` ni `Math.Clamp(0, 100)` qiladi, `current_stage` **`B2BSalesService.IsValidStage`
dan o'tkaziladi** (noto'g'ri qiymat kelsa bitimning joriy bosqichiga tushiriladi — panel
mavjud bo'lmagan bosqichni chizmasin). Format buzilsa — *«AI javobini o'qib bo'lmadi»*, yozuv
**SAQLANMAYDI**.

### 7.5 Chastota — «kuniga bir marta» EMAS (ATAYIN farq)

Boshqa AI tahlillarda cheklov `Date` bo'yicha. Bu yerda **shunday qilinmaydi**: menejer bir kunda
ikki marta qo'ng'iroq qilishi va ikkinchisidan keyin yangi maslahat olishi kerak.

Cheklov o'rniga: `InputHash` = bitimning **holati + hodisalar soni + oxirgi hodisa vaqti** dan
hisoblangan SHA-256. Yangi tahlil so'ralganda hash bir xil bo'lsa Gemini **chaqirilmaydi** va
mavjud yozuv `AlreadyFresh = true` bilan qaytadi.

> ⚠️ Ya'ni: **hech narsa o'zgarmagan bo'lsa pul sarflanmaydi**, bitimda yangi hodisa paydo
> bo'lishi bilan tahlil darhol qayta ishlaydi. Bu qoida `.claude/rules/ai-analysis.md` dagi
> «kuniga bir marta» dan farq qiladi — **yangi qoidalar faylida sababi bilan yoziladi**.

Qo'shimcha himoya: bitta bitimda kuniga **eng ko'pi 20** tahlil (`MaxRunsPerDay`) — cheksiz
bosishdan himoya; oshib ketsa tushunarli o'zbekcha xato.

### 7.6 Qo'ng'iroq transkripti bilan bog'liqlik
Mavjud `CallsController.Transcribe` (Azure) va `Analyze` (Gemini) **qayta yozilmaydi**.
B2B tomonda ikki yo'l:
1. hodisa yaratishda transkriptni **qo'lda joylashtirish** (`TranscriptText`) — asosiy yo'l;
2. `B2BEvent.CallId` orqali mavjud `Call` ga bog'lash — bog'langan bo'lsa
   `B2BAiAnalysisService` `Call.Transcript` ni o'zi o'qiydi (qayta transkript qilmaydi).

⚠️ `Call` yozuvini B2B ga **avtomatik** biriktirish (telefon bo'yicha topib) — bu topshiriqqa
KIRMAYDI (2-bosqich). Buni qilma, lekin `CallId` maydonini hozirdan qo'y.

---

## 8. API — `B2BController` (`api/admin/b2b`)

```csharp
[ApiController]
[Authorize]
[AdminPerm("b2b", ReadRequiresPerm = true)]
[Route("api/admin/b2b")]
```

⚠️ **`ReadRequiresPerm = true` MAJBURIY**: javobda bitim summasi, mijoz kontaktlari va sotuv
skriptlari bor — `AdminPermAttribute` odatda GET'ni har qanday xodimga ochadi
(`.claude/rules/uploads-security.md` dagi «Xodim uchun O'QISH darvozasi»).

| Metod · yo'l | Vazifasi |
|---|---|
| `GET /meta` | Bosqichlar, e'tiroz/yutqazish/xizmat kataloglari, menejerlar |
| `GET /accounts` · `POST` · `PUT /{id}` · `DELETE /{id}` | Tashkilotlar |
| `GET /accounts/{id}` | Tashkilot + kontaktlari + bitimlari |
| `POST /accounts/{id}/contacts` · `PUT` · `DELETE` | Kontaktlar (LPR) |
| `GET /deals?stage&owner&status&q&from&to` | Kanban va ro'yxat |
| `POST /deals` · `PUT /{id}` · `DELETE /{id}` | Bitim |
| `POST /deals/{id}/stage` | Bosqich ko'chirish — **darvoza tekshiriladi** (§4.2), `B2BEvent` yoziladi |
| `POST /deals/{id}/close` | `won` / `lost` (+ `LostReason` majburiy `lost` da) |
| `GET /deals/{id}` | To'liq: tashkilot, kontaktlar, hodisalar, e'tirozlar, metrikalar |
| `POST /deals/{id}/events` | Hodisa (izoh/qo'ng'iroq/uchrashuv/transkript) |
| `POST /deals/{id}/objections` · `PUT /{oid}` | E'tiroz qo'shish / javob berish va yopish |
| `POST /deals/{id}/students` · `DELETE /{id}/students/{sid}` | Bitimga o'quvchi biriktirish |
| `GET /deals/{id}/ai-analyses` · `POST /deals/{id}/ai-analysis` | Copilot (o'qish / yaratish) |
| `GET /analytics?from&to` | Bo'lim hisoboti (§5.2) |
| `GET /analytics/export` | .xlsx (`ExcelExport` mavjud yordamchisi bilan) |
| `GET \| PUT /playbook` | Sotuv bilim bazasi |

**Amal ruxsatlari** (`PermissionRules.CanWrite`, `.claude/rules/permissions.md`):
`b2b:create` — bitim/tashkilot yaratish va **AI tahlil boshlash**; `b2b:edit` — tahrir, bosqich,
hodisa, e'tiroz; `b2b:delete` — o'chirish.

⚠️ **AI tahlilni YARATISH — `create` amali** (voronka tahlilidagi bilan bir xil): faqat ko'rish
ruxsati bor xodim tahlilni **o'qiydi**, lekin yangisini boshlay olmaydi — aks holda u tugmani
bosib 403 olardi va Gemini chaqiruviga (pulga) urinilardi.

### 8.1 Ruxsat katalogi — 4 qadam (`.claude/rules/permissions.md` §3.1)
1. `Client/src/config/constants.ts` → `adminPermissions` ga yangi bo'lim:
   `{ key: 'b2b', label: 'B2B sotuv', pages: [{ key: 'b2b.deals', label: 'Bitimlar (Kanban)' },
   { key: 'b2b.accounts', label: 'Tashkilotlar' }, { key: 'b2b.stats', label: 'B2B analitika' },
   { key: 'b2b.playbook', label: 'Sotuv bilim bazasi' }] }`;
2. `config/navigation.ts` → **«Lidlar» guruhidan alohida**, o'z guruhi «B2B sotuv»
   (guruhning O'ZIDA `perm` YO'Q — bolalarga ko'chiriladi, `contacts`/`settings` naqshi);
3. `App.tsx` → har marshrut `<RequirePerm perm="b2b.deals">` va h.k.;
4. Controller: sinf darajasida `[AdminPerm("b2b", ReadRequiresPerm = true)]`, sahifaga xos
   metodlarda torroq kalit (`b2b.playbook` — playbook endpointlarida, `b2b.stats` — analitikada).

⚠️ Qadamlardan biri unutilsa **`PermissionCatalogTests` qizaradi** — bu test yangi kalitlarni
o'zi tekshiradi, qo'lda hech narsa qo'shish shart emas.

---

## 9. AUDIT (`.claude/rules/audit.md`)

- `AuditSections.ByEntityType` ga qo'sh: `B2BDeal` · `B2BAccount` · `B2BContact` ·
  `B2BObjection` → bo'lim **`b2b`** («B2B sotuv»). Qo'shilmasa yozuv «Boshqa»da qolib, bo'lim
  filtrida topilmaydi (`AuditSectionsTests` buni tekshiradi);
- `audit.Record(...)` **SaveChanges qilmaydi** — chaqiruvchining tranzaksiyasiga qo'shiladi va
  saqlashdan **OLDIN** chaqiriladi;
- **QAMROV** — quyidagi har bir amaldan keyin tarixda **BITTA qator** paydo bo'lishi shart
  (`.claude/rules/audit.md` §3.5 — «shartli yozuv» xatosini takrorlama):
  bitim yaratildi · tahrirlandi · **bosqich ko'chirildi** · yutildi/yutqazildi (sababi bilan) ·
  o'chirildi · tashkilot yaratildi/tahrirlandi/o'chirildi · kontakt qo'shildi/o'chirildi ·
  e'tiroz qo'shildi/yopildi · o'quvchi biriktirildi · **playbook o'zgartirildi**;
- `summary` — o'zbekcha, TO'LIQ jumla (foydalanuvchi tarixda FAQAT shuni o'qiydi);
- **AI tahlil auditga YOZILMAYDI** (ma'lumotni o'zgartirmaydi — mavjud qoida bilan bir xil);
- snapshot maydonlari ekranda ko'rinishi uchun `AuditHistoryList.fieldLabels` ga yorliq qo'shish
  **SHART** — yorlig'i yo'q maydon chizilmaydi.

---

## 10. FRONTEND (`WunderkindLC.Client/src`)

### 10.1 Fayllar
```
pages/admin/b2b/
  B2BDealsPage.tsx        // Kanban — 6 ustun, drag&drop (LeadsPage naqshi)
  B2BDealColumn.tsx       // ustun (LeadColumn)
  B2BDealCard.tsx         // karta: tashkilot, summa, mas'ul, kunlar, "sovumoqda" belgisi
  B2BDealModal.tsx        // bitim tafsiloti — tablar: Umumiy · Tarix · E'tirozlar · Copilot · O'quvchilar
  B2BDealFormModal.tsx    // yaratish/tahrir
  B2BAccountsPage.tsx     // tashkilotlar ro'yxati + profili
  B2BStatsPage.tsx        // analitika (voronka, menejerlar, e'tirozlar, yutqazish sabablari)
  B2BPlaybookPage.tsx     // sotuv bilim bazasi
components/ai/B2BAiPanel.tsx   // COPILOT paneli
components/b2b/ (StageChip, ObjectionList, FunnelBars, DealHealthBadge)
api/services/b2b.ts
config/b2bLabels.ts       // faqat ZAXIRA yorliqlar
```

### 10.2 Copilot paneli — qat'iy qoidalar
- **`components/ai/AiParts.tsx` va `lib/ai.ts` QAYTA ISHLATILADI** (`ScoreRing`, `AiRadar`,
  `ScoreGrid`, `CardList`, `TextBlock`, `RankedBars`, `AiErrorBox`, `scoreColor`, `trendInfo`,
  `escapeHtml`, `openPrintWindow`, `printCss`). Nusxa ko'chirish **TAQIQLANADI** —
  `ContactAiPanel.tsx` ni namuna sifatida ol;
- ⚠️ Komponent va oddiy funksiyalar **bir faylda aralashmaydi** (eslint
  `react-refresh/only-export-components`) — funksiyalar `lib/` ga;
- Panel bitim modalining **birinchi tabi** emas, alohida «Copilot» tabi bo'ladi, lekin bitim
  ochilganda **oxirgi tahlil xulosasi va salomatlik bali «Umumiy» tabining tepasida** ham
  ko'rinadi (menejer tabni ochmasdan ham holatni ko'rsin);
- **`sales_script_for_manager` — panelning ENG KATTA bloki**, «Nusxalash» tugmasi bilan
  (menejer uni o'qib turib qo'ng'iroq qiladi — kichkina matnda foydasi qolmaydi);
- `next_action` — alohida ajratilgan, ko'zga tashlanadigan blok;
- **Tahlillar TARIXI** — sana + salomatlik bali bilan, eng yangisi tepada, qator bosilsa
  o'shanisi ochiladi (`TeacherAiPanel` / `ContactAiPanel` dagi bilan bir xil);
- **PDF chop etish** (`openPrintWindow` + `printCss`);
- AI bosqichi menejer qo'yganidan farq qilsa — panelda **ochiq ogohlantirish chipi**;
- Tugma `can('b2b','create')` bilan darvozalanadi, yozish amallari `can('b2b','edit')` bilan.

### 10.3 Kanban
- Ustunlar §4.1 tartibida, har ustun sarlavhasida **bitimlar soni + jami summa**;
- Karta: `LastActivityAt` dan 7 kundan ko'p o'tgan bitim **«sovumoqda»** chipi bilan (rang
  ogohlantiruvchi, **qizil/yashil juftligiga tayanmaydi** — `.claude/rules/course-analytics.md` §6
  dagi ranglar qoidasi: deuteranopiyada ajralmaydi);
- Drag&drop bosqich darvozasi buzilsa — karta **joyiga qaytadi** va server xatosi toast'da
  o'zbekcha ko'rsatiladi (jimgina muvaffaqiyatsiz bo'lmaydi).

---

## 11. TESTLAR (`WunderkindLC.Tests`)

⚠️ Testlarda `AppSecrets.Init` chaqirilmaydi → Gemini kaliti bo'sh → **tashqi tarmoq so'rovi
hech qanday holatda ketmaydi** (`ContactReportTests` dagi izoh bilan bir xil).

`B2BSalesServiceTests.cs`
- bosqich kalitlari va tartibi; `IsValidStage` noto'g'ri qiymatni rad etadi;
- `CanTransitionTo`: orqaga — mumkin, oldinga bittadan ko'p sakrash — mumkin emas;
- `RequiredFields` darvozalari (LPRsiz `PROPOSAL_KP` ga o'tib bo'lmaydi).

`B2BAnalyticsTests.cs`
- bosqichda turish kunlari; `DaysSinceLastActivity`;
- voronka va bosqichlararo konversiya;
- **menejer kesimida pul BIR MARTA** (bitimni ikki menejer surgan bo'lsa ham summa faqat
  yopganida);
- yutqazilgan bitim **oxirgi bosqichida qoladi** va «qaysi bosqichda yutqazdik» to'g'ri chiqadi;
- bo'sh davr — 0 emas, «ma'lumot yo'q» holati.

`B2BAiTests.cs`
- `InputHash` o'zgarmaganda Gemini **chaqirilmaydi** (`AlreadyFresh`), yangi hodisadan keyin
  hash o'zgaradi;
- kalitsiz holatda tushunarli xato **va yozuv SAQLANMAYDI**;
- bo'sh bitimda tahlil umuman boshlanmaydi (kalit tekshiruvidan oldin);
- `Sanitize`: `null` maydonlar, 0..100 dan tashqaridagi ballar, **noto'g'ri `current_stage`**
  bitimning joriy bosqichiga tushadi;
- **promptda telefon/email YO'Q** — metrics DTO'sida bunday maydon umuman yo'qligi bilan
  tasdiqlanadi;
- transkript kesilishi va nechtasi kesilgani `metrics` da qaytishi;
- `MaxRunsPerDay` chegarasi.

`AuditSectionsTests` va `PermissionCatalogTests` — mavjud, yangi kalitlarni **o'zi** tekshiradi
(qizarsa katalogda bo'shliq bor degani).

Ishga tushirish:
```bash
dotnet test WunderkindLC.Tests/WunderkindLC.Tests.csproj
```
Server build (SPA'siz — bu mashinada `npm` yo'q):
```bash
dotnet build WunderkindLC.Server/WunderkindLC.Server.csproj -p:BuildSpa=false
```

---

## 12. YANGI QOIDALAR FAYLI — `.claude/rules/b2b-sales.md` (MAJBURIY)

Modul tugagach qoidalar faylini yoz — mavjud fayllar uslubida (frontmatter `description` + `paths`,
o'zbekcha, **⚠️ belgisi bilan «nega shunday» izohlari**). Ichida albatta bo'lishi kerak:

1. **Nega mavjud `Lead` qayta ishlatilmadi** (§2) — bu keyingi dasturchi beradigan birinchi savol;
2. Bosqichlar kodda, bazada emas — **sababi** (AI kontrakti bilan drift bo'lmasin);
3. `Stage` va `Status` nega ikkalasi ham bor (yutqazilgan bitim bosqichida qoladi);
4. **`InputHash` — «kuniga bir marta» o'rniga** va nega (§7.5);
5. Maxfiylik jadvali (§6) — nima promptga ketadi, nima **hech qachon** ketmaydi va NEGA;
6. Playbook nega majburiy (to'qib chiqarishga qarshi);
7. Pul bir marta sanalishi (menejerlar kesimi);
8. `AuditSections` xaritasiga qo'shilgan turlar;
9. Ruxsat kalitlari jadvali;
10. Testlar ro'yxati va qaysi qoidani qopalashi.

---

## 13. ISH TARTIBI (bosqichma-bosqich, har biridan keyin build + test)

1. Qoidalar fayllarini o'qish (§1) → menga **qisqa reja** ber (nima qilaman, qanday farazlar);
2. Domain + `IAppDbContext`/`AppDbContext` + migratsiya `AddB2BSales`;
3. `B2BSalesService` (sof katalog) + testlari;
4. `B2BAnalytics` + testlari;
5. `B2BAiAnalysisService` + testlari;
6. `B2BController` + ruxsat katalogi (4 qadam) + audit;
7. Frontend: API qatlami → kanban → bitim modali → Copilot paneli → analitika → playbook;
8. `.claude/rules/b2b-sales.md`;
9. `dotnet test` (hammasi yashil) + `dotnet build ... -p:BuildSpa=false`;
10. Commit (o'zbekcha, `feat(b2b):` prefiksi, tanasida NEGA) va `main` ga push.
    ⚠️ Frontend TypeScript build'ini bu mashinada tekshirib bo'lmasa — buni **ochiq ayt**.

---

## 14. NIMA QILINMAYDI (chegara)

- Mavjud `LeadsController` / `LeadStage` / `CrmStatsPage` / voronka AI tahliliga **tegilmaydi**;
- `Contract` modeliga `b2b` target qo'shilmaydi (2-bosqich) — hozircha bitim fayli
  `B2BEvent` (type=`file`) sifatida saqlanadi va `/uploads` qoidalari qo'llanadi
  (manzil javobga chiqsa — kim ko'rishini tekshir);
- `Call` yozuvini telefon bo'yicha **avtomatik** bitimga biriktirish — 2-bosqich
  (`B2BEvent.CallId` maydoni hozirdan qo'yiladi);
- Telegram/SMS xabarnomalari — 2-bosqich;
- `FinanceTransaction` ga B2B tushumi **yozilmaydi** (kitob sotuvidagi bilan bir xil mantiq:
  o'quv to'lovi hisobotlarini buzmasin) — tushum `B2BDealStudent` orqali **o'qib** ko'rsatiladi.

---

## 15. QABUL MEZONLARI

- [ ] `dotnet test` — hammasi yashil, jumladan `PermissionCatalogTests` va `AuditSectionsTests`;
- [ ] Ruxsati yo'q xodim `GET /api/admin/b2b/deals` dan **403** oladi (`ReadRequiresPerm`);
- [ ] Faqat `b2b:edit` bo'lgan xodim AI tahlilni **o'qiydi**, lekin boshlay olmaydi;
- [ ] LPRsiz bitim `PROPOSAL_KP` ga ko'chmaydi (server 400, o'zbekcha matn);
- [ ] Bitimda hech narsa o'zgarmaganda «Tahlil» tugmasi **Gemini'ni chaqirmaydi**;
- [ ] Gemini kaliti yo'qligida tushunarli o'zbekcha xato va **bo'sh yozuv saqlanmaydi**;
- [ ] Copilot javobidagi `current_stage` har doim haqiqiy bosqich kalitlaridan biri;
- [ ] Promptda mijoz telefoni/emaili **yo'q** (test bilan tasdiqlangan);
- [ ] Bitimning har bir muhim amali «O'zgarishlar tarixi»da **bitta qator** bo'lib ko'rinadi;
- [ ] `.claude/rules/b2b-sales.md` yozilgan.
