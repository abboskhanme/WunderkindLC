# WUNDERKINDLC — MARKETING BO'LIMINI KENGAYTIRISH PROMPTI

> **Bu hujjat nima?**
> AI koding agentiga (Claude Code / Cursor) beriladigan **master prompt**.
> WunderkindLC'da **allaqachon mavjud** Marketing moduli (Instagram AI agenti + Meta Lead Ads)
> ustiga **yetishmayotgan qismlarni** qurish uchun.
>
> **⚠️ MUHIM:** bu noldan qurish emas. Mavjud kod, konventsiyalar va qoidalar **buzilmasligi shart**.
> Yozishdan oldin `.claude/rules/marketing-instagram.md` va `instagram/TEXNIK.md` ni **to'liq o'qi**.
>
> Meta API faktlari 2026-08-21 holatiga ko'ra rasmiy hujjatdan tekshirilgan.
> **⚠️** — Meta hujjatlarida ziddiyat/noaniqlik bor, kodga konstanta qilib yozma.

---

## 0. AGENTGA TOPSHIRIQ

Sen — WunderkindLC loyihasida ishlaydigan **.NET 8 + React 19** dasturchisisan.

**Vazifang:** `/admin/marketing` bo'limini kengaytirish. Hozir u faqat Instagram izoh/DM
AI agenti va Lead Ads lidlarini qamrab oladi. Qo'shilishi kerak: **reklama statistikasi
(xarajat, lid narxi, ROI)**, **kontent rejalashtirish**, **lid sifatini Meta'ga qaytarish (CAPI)**,
**reklama izohlari atributsiyasi** va (qaror qabul qilinsa) **Facebook Page / Messenger**.

**Qat'iy qoidalar:**

1. **Mavjud kodni sindirma.** `Instagram*.cs`, `Meta*.cs`, `IgConst`, `InstagramPipeline`,
   `InstagramWorkerService`, `IgAdPage/IgAdLead` — bularning xatti-harakati o'zgarmaydi.
   Faqat qo'shasan.
2. **Loyiha konventsiyalari — §2 da.** Ulardan chetga chiqma. Yangi kutubxona qo'shma
   (Hangfire, MediatR, Redis, TanStack Query, pgvector — **hech biri yo'q va kerak emas**).
3. Kod identifikatorlari **inglizcha**, izohlar/UI matnlari/xato xabarlari — **o'zbekcha (lotin)**.
4. Har modul oxirida: `dotnet build` · `dotnet test` · `npm run build` — uchalasi ham o'tsin.
5. Meta API fakti bu hujjatda yo'q bo'lsa — **taxmin qilma**, to'xta va so'ra.
6. Har yangi tashqi chaqiruv **`CenterMeta` bayrog'i ostida** bo'lsin, bayroq **default `false`**.

---

## 1. HOZIRGI HOLAT (audit) — nima allaqachon bor

### 1.1 Ikki mustaqil integratsiya yo'li

| | **Izoh + DM agenti** | **Reklama lidlari** |
|---|---|---|
| Meta mahsuloti | Instagram API with **Instagram Login** | **Facebook Page** webhook |
| Webhook obyekti | `instagram` | `page`, field `leadgen` |
| Host | `graph.instagram.com/v23.0` (`IgConst.GraphBase`) | `graph.facebook.com/v23.0` (`IgConst.FbGraphBase`) |
| Token | IG Login (60 kun, avto-yangilanadi) | Page Access Token (UI'dan kiritiladi) |
| App Review | **kerak emas** (Standard Access) | `leads_retrieval` — kerak |
| Endpoint | `/api/public/instagram/webhook` | `/api/public/instagram/leadgen` |
| Klient | `InstagramApi` | `MetaAdsApi` |

### 1.2 Mavjud entity'lar (`WunderkindLC.Domain/Entities.cs`)

```
// ═══ MARKETING — INSTAGRAM AI AGENTI ═══  (3720+)
IgAccount · IgWebhookEvent · IgConversation · IgMessage · IgAutoRule · IgKnowledge · IgOAuthState
// ==== REKLAMA LIDLARI (Meta Lead Ads) ==== (3985+)
IgAdPage · IgAdLead
```

### 1.3 Mavjud servislar (`WunderkindLC.Application/Services/`)

```
InstagramContract.cs      (361)  IgConst + sof funksiyalar
InstagramSignature.cs      (73)  HMAC, fail-closed
InstagramEventParser.cs   (268)  webhook JSON → IgIncomingEvent[], deterministik EventKey
InstagramApi.cs           (367)  graph.instagram.com klienti
InstagramAgentService.cs  (239)  Gemini prompt + IgAgentOutput
InstagramPipeline.cs      (619)  bitta hodisani boshidan oxirigacha
InstagramLeadBridge.cs    (114)  suhbat → Lead
InstagramWorkerService.cs (175)  BackgroundService: navbat · token · tozalash
MetaAdsApi.cs             (328)  graph.facebook.com klienti (YAGONA joy)
MetaLeadgenParser.cs      (121)
MetaLeadgenService.cs     (173)
MetaLeadBridge.cs         (123)
```

### 1.4 Mavjud frontend (`WunderkindLC.Client/src/pages/admin/marketing/`)

```
InstagramDashboard.tsx  /admin/marketing                      marketing.dashboard
InstagramInbox.tsx      /admin/marketing/inbox                marketing.inbox
InstagramRules.tsx      /admin/marketing/rules                marketing.rules
InstagramKnowledge.tsx  /admin/marketing/knowledge            marketing.knowledge
InstagramAnalytics.tsx  /admin/marketing/analytics            marketing.analytics
InstagramAdLeads.tsx    /admin/marketing/reklama-lidlari      marketing.leadads
InstagramSettings.tsx   /admin/marketing/settings             marketing.settings
mk.tsx                  umumiy UI kit (Ic ikonka xaritasi, sahifa o'ramlari)
```
API klient: `src/api/services/instagram.ts` (29 funksiya, `Ig`-prefiksli tiplar).

### 1.5 🔴 NIMA YO'Q (bu hujjatning mavzusi)

| # | Yetishmayapti | Nima uchun muhim |
|---|---|---|
| **E1** | **Reklama statistikasi** (`/insights`) — xarajat, ko'rsatish, CPL, ROI | Hozir lid keladi, lekin **necha pulga tushgani noma'lum**. Marketing byudjetini boshqarib bo'lmaydi |
| **E2** | **Kontent rejalashtirish** (IG post/reels/story joylash) | SMM qo'lda ishlaydi, CRM'dan uzilgan |
| **E3** | **Reklama izohlari atributsiyasi** (`ad_id`) | Reklama ostidagi izohni organikdan ajratib bo'lmaydi |
| **E4** | **CAPI** — lid sifatini Meta'ga qaytarish | Meta faqat "lid keldi"ni biladi, "mijoz bo'ldi"ni bilmaydi → reklama yomon optimallashadi |
| **E5** | **Facebook Page / Messenger** | FB'dagi izoh va DM'lar umuman ko'rinmaydi |
| **E6** | Mavjud modulni yaxshilash (RAG, story javoblari, media) | Sifat |

---

## 2. LOYIHA KONVENTSIYALARI — QAT'IY

Bu bo'lim **majburiy**. Har qatorini bajarasan.

### 2.1 Domen va baza

```csharp
// ID — string GUID
public string Id { get; set; } = Guid.NewGuid().ToString();

// Vaqt — HAR DOIM ISO string. DateTime ustuni YO'Q.
public string CreatedAt { get; set; } = "";     // AppClock.Iso()
// ⚠️ DateTime.Now / DateTime.UtcNow — TAQIQLANADI. Faqat AppClock.Now / AppClock.Iso().

// "Yo'q" qiymat — bo'sh satr, null EMAS.
public string Error { get; set; } = "";
// Nullable faqat "yo'qlik" ma'noli bo'lganda (masalan IgConversation.LeadId).

// Pul — Lead Ads/Insights'da: minor unit bo'lgani uchun long, aks holda ehtiyot bo'l (§4.3)
```

- `AppDbContext` — 129 ta `DbSet<T> X => Set<T>();` uslubida. Yangi DbSet **`IAppDbContext`
  interfeysiga ham** qo'shiladi.
- Indeks/`HasMaxLength` — `OnModelCreating` ichida, mavjud Ig bloki yonida (348–375 qatorlar).
- Migratsiya nomi: `yyyyMMddHHmmss_PascalCaseName`. Yangi bayroqlar migratsiyada ham
  **`false` default** bilan.

### 2.2 Servis uslubi

```csharp
// Istisno OTILMAYDI — tuple qaytariladi, xato matni O'ZBEKCHA.
public async Task<(bool Ok, TData? Data, string Error)> FetchAsync(...)

// HTTP klient — typed HttpClient:
builder.Services.AddHttpClient<MetaInsightsApi>();
// Servis — Scoped; pipeline kabi uzun umrli narsa — Singleton (o'z scope'ini ochadi).
```

- Konstantalar **bitta joyda** — `IgConst` (`InstagramContract.cs`). Yangi konstantalar ham
  shu yerga (yoki yangi `Meta*Contract.cs` ga, agar mantiqan alohida bo'lsa).
- **Sof funksiyalar** (parser, hisob-kitob, validatsiya) HTTP va DB'dan **ajratilgan** bo'lsin —
  faqat ular test bilan qoplanadi (`tests.md` uslubi).

### 2.3 Fon ishlari — YANGI BackgroundService QO'SHMA

Loyihada 16 ta hosted service bor, Instagram uchun **bittasi** — `InstagramWorkerService`
(har 2 soniyada). Yangi davriy ish kerak bo'lsa:

- **Tez-tez (soniyalar)** → mavjud `InstagramWorkerService` ichiga **yangi vazifa** sifatida qo'sh.
- **Kamdan-kam (soatlar/kunlar)** → xuddi shu worker ichida "oxirgi ishlash vaqti"ni
  `CenterMeta` yoki alohida ustunda saqlab, shartli bajar.
- Navbat **har doim DB jadvali**, kesh emas. `_ = Task.Run(...)` **taqiqlanadi**
  (restartda hodisa yo'qoladi).

### 2.4 Webhook qoidalari (o'zgarmaydi)

```
xom baytlar (EnableBuffering) → HMAC-SHA256 fail-closed → IgWebhookEvent(Status="pending")
→ DARHOL 200 OK → InstagramWorkerService → InstagramPipeline
```
- `CryptographicOperations.FixedTimeEquals`, `==` **hech qachon**.
- App Secret bo'sh → `false` (fail-open **taqiqlanadi**).
- `EventKey` **deterministik**: `GetHashCode()`, `Random`, `Guid` — **taqiqlanadi**.
- Meta 5 soniya kutadi, 36 soat retry qiladi.

### 2.5 Ruxsatlar

```csharp
[Authorize]
[AdminPerm("marketing", ReadRequiresPerm = true)]   // klass darajasi — javobda telefon bor
[Route("api/admin/instagram")]
public class InstagramController : ControllerBase
{
    [HttpPut("...")]
    [AdminPerm("marketing.settings")]                // yozish — sahifa kaliti
}
```
Yangi sahifa kaliti qo'shilsa **uch joyda**:
1. `WunderkindLC.Client/src/config/constants.ts` → `adminPermissions` → `marketing.pages`
2. `WunderkindLC.Client/src/config/navigation.ts` → Marketing guruhi
3. Route: `src/App.tsx` → `<RequirePerm perm="...">`
`PermissionCatalogTests` buni tekshiradi — mos kelmasa test yiqiladi.

### 2.6 Audit

```csharp
await audit.Record(..., entityType: "Instagram", ...);   // AuditSections: "Instagram" → "marketing"
```
- Yoziladi: ulash/uzish, sozlama o'zgarishi, qoida CRUD, bilim bazasi, **operator javobi**,
  qo'lda lid yaratish, sahifa ulash, lid qayta olish.
- **Yozilmaydi:** botning avtomatik javoblari, har kiruvchi lid.
- **Token/secret hech qachon auditga tushmaydi.**
- Savol: *"bu amaldan keyin tarixda AYNAN BITTA qator paydo bo'ladimi — har doim?"*

### 2.7 Sirlar

- `.env` → `AppSecrets` + `EnvKeys`. **Va `docker-compose.yml` `environment:` ga ham**
  (prod `app` servisida `env_file` yo'q) — `EnvKeysWiringTests` tekshiradi.
- Runtime tokenlar (OAuth, Page Token) — DB'da, lekin **DTO/javob/log/auditga tushmaydi**.
  Frontendga faqat `tokenSet: true/false`.
- `appsettings.json` da `"System.Net.Http.HttpClient": "Warning"` — **o'zgartirma**
  (token URL'da ketadi, `Information` da loglanadi). `SecretLeakAndPublicPageTests` qulflagan.
- ⚠️ `Logging:LogLevel` ichiga **izoh yozib bo'lmaydi** — startup yiqiladi.

### 2.8 Lid yaratish

```csharp
// HAR DOIM mavjud LeadIntake orqali. Parallel jadval YARATILMAYDI.
var lead = await LeadIntake.FindByPhoneAsync(db, phone);   // oxirgi 9 raqam, PhoneKey ustuni
// ⚠️ Lead.PhoneKey QO'LDA yozilmaydi — AppDbContext.SaveChanges o'zi hisoblaydi.
// ⚠️ FIRST-TOUCH: mavjud lidning Source va Stage O'ZGARMAYDI.
//    Takroriy murojaat → RepeatCount++ , LastRepeatAt, LeadEvent.
```
Kanal tasnifi — `LeadOrigins` (`ads` → `instagram` tartibida tekshiriladi).

### 2.9 Frontend

- React 19 + Vite 8 + **Tailwind v4** (`tailwind.config` **yo'q**, tokenlar `src/index.css`
  `@theme` ichida) + `src/styles/marketing.css` (`.marketing-app` scoped CSS o'zgaruvchilari:
  `--primary`, `--c-instagram`, `--c-instagram-grad`, `--ai-grad`).
- **State: `useState`/`useEffect` + axios.** ⚠️ **TanStack Query YO'Q.**
- Named export: `export function InstagramAdsStats() {...}`, import `@/` alias bilan.
- Grafik — `recharts`. Ranglar: `#0284c7` / `#e11d48`. ⚠️ **Yashil-qizil juftlik ishlatma**
  (deuteranopiya), **bitta grafikda ikki o'q ishlatma**.
- Ikonka — `mk.tsx` dagi `Ic` xaritasi (marketing sahifalari), qolgan joyda `lucide-react`.
- **i18n yo'q** — matnlar to'g'ridan-to'g'ri o'zbekcha yoziladi.
- Avto-yangilanish: `REFRESH_MS = 15000`, `visibilitychange` da to'xtaydi.
- Sozlamalar sahifasi **hech qachon sir qiymatini ko'rsatmaydi**.

### 2.10 Testlar

`WunderkindLC.Tests/<Subject>Tests.cs`, xUnit, `TestDb.cs` yordamchisi (SQLite/InMemory).
Sof funksiyalar test qilinadi, HTTP/DB emas.

---

## 3. STRATEGIK QARORLAR — birinchi shular hal qilinsin

### 3.1 🔴 QAROR №1: Ads Insights uchun qaysi token?

Reklama statistikasi `graph.facebook.com/v{ver}/act_{id}/insights` dan olinadi va
**`ads_read` ruxsatli token** talab qiladi. Hozirgi `IgAdPage.AccessToken` — **Page token**,
u **yaramaydi** (Page token'da `ads_read` yo'q).

| Variant | Qanday | Afzallik | Kamchilik |
|---|---|---|---|
| **A. System User token (TAVSIYA)** | Business Manager → Sistema foydalanuvchisi yarat → ad account'ni biriktir → `ads_read` bilan token generatsiya qil → CRM Sozlamalaridan kirit | **Muddatsiz**, OAuth kerak emas, `IgAdPage` bilan bir xil naqsh | Qo'lda sozlash |
| B. Facebook Login OAuth | Yangi OAuth oqimi, `ads_read` scope | Foydalanuvchi uchun qulay | Yangi OAuth oqimi + 60 kunlik token + refresh mantiq |

→ **Variant A tanlanadi.** Yangi entity `IgAdAccount` (Page bilan bir xil naqsh:
`AccessToken` UI'dan kiritiladi, javobda faqat `tokenSet`).

**App Review kerakmi?** O'z reklama akkauntingiz uchun (ilova adminlari egalik qiladi) —
Standard Access yetadi. ⚠️ Amalda tekshiring: `ads_read` bilan `act_{id}/insights` chaqirib
ko'ring; `#200`/`#10` xatosi kelsa — App Review kerak.

### 3.2 🔴 QAROR №2: Reklama izohlari `ad_id` — yo'lni o'zgartirish kerakmi?

**Muammo:** Instagram Login yo'lidagi `comments` webhook'ida `ad_id` **umuman yo'q**.
U faqat **Facebook Login** yo'lidagi payload'da bo'ladi:
```json
"media": { "id": "...", "ad_id": "...", "ad_title": "...",
           "original_media_id": "...", "media_product_type": "AD" }
```

| Variant | Nima qilinadi | Baho |
|---|---|---|
| **A. Hech nima (TAVSIYA — birinchi bosqichda)** | `ad_id` yo'q, izohlar organik deb qaraladi | 0 mehnat. Reklama izohi ajratilmaydi |
| **B. `media_id` orqali bilvosita** | Izoh kelganda `media.id` ni `IgAdEntity`/creative'lar bilan solishtirish (`effective_object_story_id`) | O'rtacha mehnat, **boostlangan organik postlar** uchun ishlaydi, dark post uchun yo'q |
| C. Facebook Login yo'liga ko'chish | Butun agentni qayta yozish, Page + App Review + Business Verification | **Juda katta** — hozir tavsiya etilmaydi |

→ **A dan boshla, E1 tugagach B ni qo'sh** (E1 da `IgAdEntity` va creative'lar allaqachon
sinxronlanadi — `media_id → ad_id` xaritasi tekin chiqadi).

### 3.3 🔴 QAROR №3: Facebook Page / Messenger kerakmi?

**Xarajat:** `pages_messaging` + `pages_manage_engagement` + `pages_read_user_content`
→ **App Review majburiy** (Standard Access'da faqat ilovada roli bor odamlar bilan
yozishish mumkin — ya'ni prodda ishlamaydi). Bu Instagram Login yo'lidan **tubdan farq qiladi**.

→ **Savol markazga:** Facebook Page'da haqiqiy trafik bormi? Yo'q bo'lsa — **E5 ni qilma**.
Bor bo'lsa — E5 eng oxirida, App Review bilan parallel.

### 3.4 API versiyasi

Hozir `v23.0`. Joriy — **v26.0** (2026-07-29). v23.0 hali ishlaydi, lekin:
- Yangi kod uchun `IgConst` ga **`FbGraphVersion = "v23.0"`** kabi alohida konstanta chiqar
  (hozir versiya URL ichiga yopishtirilgan).
- Insights va Publishing uchun **v23.0 dan boshla** (mavjud kod bilan bir xil), keyin
  bitta joydan ko'tarasan.
- ⚠️ Meta'ning ba'zi o'zgarishlari **versiyaga bog'liq emas** — versiyani muzlatish
  sizni himoya qilmaydi (metrika o'chirilishi, attribution oynalari).

---

## 4. E1 — REKLAMA STATISTIKASI (Ads Insights) 🔴 ENG MUHIM

**Maqsad:** "Bu oyda Instagram reklamasiga N so'm sarfladik, M ta lid keldi, bittasi K so'mga
tushdi, ulardan P tasi o'quvchi bo'ldi, R so'm daromad keltirdi."

Bu — CRM'ning **eng katta ustunligi**: Ads Manager lid *sonini* biladi, lekin
**qaysi lid pul to'laganini bilmaydi**. WunderkindLC biladi.

### 4.1 Entity'lar → `Entities.cs`, `REKLAMA LIDLARI` bloki ostiga

```csharp
// ==== REKLAMA STATISTIKASI (Meta Ads Insights) ====

public class IgAdAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AdAccountId { get; set; } = "";    // "act_1234567890" — PREFIKS BILAN
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";       // "USD" | "UZS" — GET /act_{id}?fields=currency
    public int    CurrencyOffset { get; set; } = 2;  // ⚠️ Meta'dan KELMAYDI — §4.2 dagi jadvaldan
    public string TimezoneName { get; set; } = "";   // hisobot sanasi SHU zonada
    public string AccessToken { get; set; } = "";    // System User token — javobga TUSHMAYDI
    public bool   IsActive { get; set; } = true;
    public string ConnectedAt { get; set; } = "";
    public string ConnectedBy { get; set; } = "";
    public string LastSyncAt { get; set; } = "";
    public string LastError { get; set; } = "";
}

public class IgAdEntity            // campaign / adset / ad iyerarxiyasi
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AdAccountId { get; set; } = "";    // "act_..."
    public string Level { get; set; } = "";          // campaign | adset | ad
    public string ExternalId { get; set; } = "";     // UNIKAL indeks
    public string ParentId { get; set; } = "";       // adset→campaign, ad→adset
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string EffectiveStatus { get; set; } = "";
    public string Objective { get; set; } = "";      // OUTCOME_LEADS ...
    public long   DailyBudgetMinor { get; set; }     // ⚠️ MINOR UNIT (tiyin/sent)
    public long   LifetimeBudgetMinor { get; set; }
    public string StartTime { get; set; } = "";
    public string StopTime { get; set; } = "";
    public string CreativeStoryId { get; set; } = "";// effective_object_story_id — E3 uchun
    public string SyncedAt { get; set; } = "";
}

public class IgAdInsight           // kunlik faktlar
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AdAccountId { get; set; } = "";
    public string Level { get; set; } = "";          // campaign | adset | ad
    public string ExternalId { get; set; } = "";
    public string StatDate { get; set; } = "";       // "yyyy-MM-dd" — akkaunt zonasida
    public string Platform { get; set; } = "all";    // instagram | facebook | all
    public long   Impressions { get; set; }
    public long   Reach { get; set; }
    public long   Clicks { get; set; }
    public long   LinkClicks { get; set; }           // inline_link_clicks
    public long   SpendMinor { get; set; }           // ⚠️ §4.3 ni O'QI
    public int    LeadsOnsite { get; set; }          // onsite_conversion.lead_grouped
    public int    LeadsPixel { get; set; }           // offsite_conversion.fb_pixel_lead
    public int    MsgStarted { get; set; }           // messaging_conversation_started_7d
    public string ActionsJson { get; set; } = "";    // XOM `actions` massivi
    public string AttributionSetting { get; set; } = "";
    public string FetchedAt { get; set; } = "";
}
```

**`AppDbContext` (`OnModelCreating`, Ig bloki yonida):**
```csharp
b.Entity<IgAdAccount>().Property(x => x.AdAccountId).HasMaxLength(200);
b.Entity<IgAdAccount>().HasIndex(x => x.AdAccountId).IsUnique();
b.Entity<IgAdEntity>().Property(x => x.ExternalId).HasMaxLength(200);
b.Entity<IgAdEntity>().HasIndex(x => x.ExternalId).IsUnique();
b.Entity<IgAdEntity>().HasIndex(x => new { x.AdAccountId, x.Level });
b.Entity<IgAdInsight>().Property(x => x.ExternalId).HasMaxLength(200);
b.Entity<IgAdInsight>()
 .HasIndex(x => new { x.Level, x.ExternalId, x.StatDate, x.Platform }).IsUnique();
b.Entity<IgAdInsight>().HasIndex(x => x.StatDate);
```

**`CenterMeta` ga (Instagram bloki oxiriga):**
```csharp
public bool   InstagramAdsStatsEnabled { get; set; }          // default FALSE
public int    InstagramAdsSyncHour { get; set; } = 5;         // har kuni soat 5 da
public int    InstagramAdsBackfillDays { get; set; } = 90;    // birinchi yuklash
```

**Migratsiya:** `AddMetaAdsInsights`.

### 4.2 🔴 PUL BIRLIGI — ENG KO'P XATO SHU YERDA

Meta'da **assimetriya** bor:

| Nima | Format | Misol |
|---|---|---|
| Byudjet (`daily_budget`, `lifetime_budget`) | **integer, MINOR unit** | `5000` = 50.00 USD |
| Insights `spend` | **STRING, MAJOR unit** | `"312.45"` = 312.45 USD |

Kodda:
```csharp
// spend "312.45" → minor:
static long ParseSpendToMinor(string? spend, int offset)
{
    if (string.IsNullOrWhiteSpace(spend)) return 0;
    if (!decimal.TryParse(spend, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return 0;
    return (long)Math.Round(d * (decimal)Math.Pow(10, offset), MidpointRounding.AwayFromZero);
}
```
> ✅ **HAL QILINDI (2026-08-21).** Bu bo'limdagi da'vo `META-API-MALUMOTNOMA.md` §11.1 bilan ZID
> edi (u yerda maydon so'raladigan qilib yozilgan). Ikkalasiga ham tayanmasdan, kod endi buni
> **ish vaqtida aniqlaydi**: `currency_offset` bilan so'raladi, Meta uni rad etsa (`#100`,
> qaror MATN emas KOD bo'yicha) bir marta maydonsiz qayta so'raladi va offset
> `MetaCurrency.OffsetOf` dan olinadi. Manba (`meta` | `jadval`) `GET adsstats/status` da
> ko'rinadi. Qoida: `.claude/rules/marketing-instagram.md` §17.3.
> **Quyidagi matn tarixiy topshiriq sifatida o'z holida qoldirilgan.**

🔴 **`currency_offset` — Ad Account node'da BUNDAY MAYDON YO'Q.**
(U `Currency` node'ida, u ham eskirgan.) `GET /act_{id}?fields=currency` faqat ISO kodini beradi.

Shuning uchun offset **bizning tomonda**, sof funksiya sifatida (`MetaCurrency.cs` + test):
```csharp
public static class MetaCurrency
{
    // Meta "zero-decimal" valyutalari — minor unit = major unit
    private static readonly HashSet<string> Zero = new(StringComparer.OrdinalIgnoreCase)
    { "JPY","KRW","VND","CLP","ISK","PYG","UGX","RWF","VUV","XAF","XOF","XPF","KMF","DJF","GNF","BIF","MGA" };

    public static int OffsetOf(string? code) =>
        string.IsNullOrWhiteSpace(code) ? 2 : (Zero.Contains(code) ? 0 : 2);
}
```
UZS → offset **2**. Yangi valyuta chiqsa jadvalga qo'shiladi; noma'lum kod → **2** (xavfsiz default).

**Barcha raqamli metrikalar JSON'da MATN** — `decimal.Parse(..., InvariantCulture)`.

### 4.3 `MetaInsightsApi.cs` — yangi typed HttpClient

⚠️ `MetaAdsApi.cs` ga **tegma** — u lid uchun. Yangi fayl, xuddi shu uslub.

```csharp
public record MetaAdAccountInfo(string Id, string Name, string Currency, int CurrencyOffset, string TimezoneName);
public record MetaAdEntityRow(string Level, string ExternalId, string ParentId, string Name,
                              string Status, string EffectiveStatus, string Objective,
                              long DailyBudgetMinor, long LifetimeBudgetMinor,
                              string StartTime, string StopTime, string CreativeStoryId);
public record MetaInsightRow(string Level, string ExternalId, string StatDate, string Platform,
                             long Impressions, long Reach, long Clicks, long LinkClicks,
                             long SpendMinor, int LeadsOnsite, int LeadsPixel, int MsgStarted,
                             string ActionsJson, string AttributionSetting);

public sealed class MetaInsightsApi(HttpClient http, ILogger<MetaInsightsApi> logger)
{
    Task<(bool Ok, MetaAdAccountInfo? Info, string Error)> FetchAccountAsync(string actId, string token, CancellationToken ct);
    Task<(bool Ok, List<MetaAdEntityRow> Rows, string Error)> FetchEntitiesAsync(string actId, string token, CancellationToken ct);
    Task<(bool Ok, List<MetaInsightRow> Rows, string Error)> FetchInsightsAsync(
        string actId, string token, string since, string until, CancellationToken ct);
}
```

**So'rovlar:**

```
# 1) Akkaunt ma'lumoti
GET {FbGraphBase}/act_{ID}?fields=name,currency,timezone_name,account_status&access_token={TOKEN}
# ⚠️ currency_offset SO'RAMA — bunday maydon yo'q, so'rov #100 xato beradi.
#    Offset MetaCurrency.OffsetOf(currency) dan olinadi.

# 2) Iyerarxiya (uchta alohida so'rov)
GET {FbGraphBase}/act_{ID}/campaigns
    ?fields=id,name,status,effective_status,objective,daily_budget,lifetime_budget,start_time,stop_time
    &limit=200&access_token={TOKEN}
GET {FbGraphBase}/act_{ID}/adsets
    ?fields=id,name,campaign_id,status,effective_status,daily_budget,lifetime_budget,start_time,end_time
    &limit=200&access_token={TOKEN}
GET {FbGraphBase}/act_{ID}/ads
    ?fields=id,name,adset_id,campaign_id,status,effective_status,creative{id,effective_object_story_id}
    &limit=200&access_token={TOKEN}

# 3) Kunlik statistika
GET {FbGraphBase}/act_{ID}/insights
    ?level=ad
    &fields=campaign_id,campaign_name,adset_id,adset_name,ad_id,ad_name,
            impressions,reach,clicks,inline_link_clicks,spend,actions,
            cost_per_action_type,attribution_setting,date_start,date_stop
    &time_range={"since":"2026-08-01","until":"2026-08-20"}
    &time_increment=1
    &breakdowns=publisher_platform
    &action_breakdowns=action_type
    &limit=500
    &access_token={TOKEN}
```

⚠️ `breakdowns=publisher_platform` — **Instagram va Facebook xarajatini ajratishning
yagona yo'li**. Alohida "Instagram insights" endpoint'i yo'q.

**Sahifalash:** `paging.next` bo'lsa ergash, lekin **maksimum 20 sahifa** (himoya to'sig'i),
oshsa logga `warning` va to'xta — jimgina kesib tashlama.

### 4.4 `actions` massividan lidlarni ajratish — sof funksiya

`MetaInsightsParser.cs` (sof, test bilan qoplanadi):

```csharp
public static class MetaInsightsParser
{
    public const string ActLeadGrouped = "onsite_conversion.lead_grouped";
    public const string ActPixelLead   = "offsite_conversion.fb_pixel_lead";
    public const string ActMsgStarted  = "onsite_conversion.messaging_conversation_started_7d";
    public const string ActLinkClick   = "link_click";

    // ⚠️ Qiymati 0 bo'lgan action_type massivda UMUMAN BO'LMAYDI → indeks bilan o'qima
    public static int ActionValue(JsonElement row, string actionType) { ... }
}
```

🔴 **Ikki marta hisoblash xavfi:**
`lead ≈ onsite_conversion.lead_grouped + offsite_conversion.fb_pixel_lead`.
**Uchtasini qo'shma.** Biz `LeadsOnsite` va `LeadsPixel` ni alohida saqlaymiz,
UI'da **yig'indisini** ko'rsatamiz. `lead` turini umuman ishlatmaymiz.

⚠️ `action_breakdowns` ishlatilganda bitta `action_type` **bir necha qator** bo'lib keladi —
qo'shishdan oldin guruhla.

### 4.5 `MetaInsightsService.cs` — sinxronizatsiya

```csharp
public sealed class MetaInsightsService(IAppDbContext db, MetaInsightsApi api,
                                        ILogger<MetaInsightsService> logger)
{
    // Kunlik to'liq sinxronizatsiya (worker chaqiradi)
    public Task<(bool Ok, int Rows, string Error)> SyncAsync(CancellationToken ct);
    // Qo'lda "Yangilash" tugmasi
    public Task<(bool Ok, int Rows, string Error)> SyncRangeAsync(string since, string until, CancellationToken ct);
}
```

**Sinxronizatsiya siyosati:**

| Qachon | Nima |
|---|---|
| Birinchi ulanish | `InstagramAdsBackfillDays` (default 90) kun orqaga, **10 kunlik bo'laklarda** |
| Har kuni `InstagramAdsSyncHour` da | **oxirgi 7 kun** qayta yuklanadi (Meta ma'lumoti 48 soatgacha o'zgaradi) |
| Qo'lda | tanlangan oraliq |

**Upsert:** `(Level, ExternalId, StatDate, Platform)` bo'yicha — mavjud bo'lsa yangilanadi.
Bu qayta yuklashda dublikat yaratmaslikni kafolatlaydi.

⚠️ **Sana akkaunt vaqt zonasida.** `IgAdAccount.TimezoneName` ni saqlab, `since`/`until`
ni shunga qarab hisobla. CRM foydalanuvchisi "bugun" desa — Toshkent kuni, Meta esa
akkaunt zonasida beradi. Farqni UI'da tushuntir.

### 4.6 Rate limit — MAJBURIY

Har javobdan o'qi va `IgAdAccount.LastError` ga yoz:
```
X-FB-Ads-Insights-Throttle: {"app_id_util_pct":100,"acc_id_util_pct":10,"ads_api_access_tier":"standard_access"}
X-Business-Use-Case-Usage:  {"<biz>":[{"type":"ads_insights","call_count":42,...,
                             "estimated_time_to_regain_access":0}]}
```
Kvota (soatiga, akkaunt uchun): quyi tier `600 + 400 × aktiv reklama − 0.001 × xatolar`.
⚠️ **Sizning 4xx xatolaringiz kvotani kamaytiradi** — xatoni qayta urinib takrorlama.

**Xatolar:**

| Kod | Ma'no | Nima qilinadi |
|---|---|---|
| `80000` (subcode 2446079) | ads_insights BUC limiti | `estimated_time_to_regain_access` (daqiqa) qancha bo'lsa — shuncha kut |
| `4`, `17`, `613` | app/user/custom limit | backoff, keyin navbatga qaytar |
| `100` subcode `1487534` | bir so'rovda juda ko'p ma'lumot | **oraliqni qisqartir** — backoff yordam bermaydi |
| `190` | token | retry **yo'q**, `LastError` + Telegram alert |
| `200`, `10` | ruxsat yo'q | retry **yo'q** — `ads_read` masalasi (§3.1) |

Meta ochiq aytadi: limitga yetganda **chaqiruvni to'xtat**, davom etsang blok uzayadi.

### 4.7 Controller — `InstagramController.cs` ga qo'shiladi

⚠️ Yangi controller **yaratma** — mavjudiga qo'sh (route `api/admin/instagram`).

| Verb + route | Ruxsat | Nima qaytaradi |
|---|---|---|
| `GET adsstats/status` | klass | `{ connected, adAccountId, name, currency, tokenSet, lastSyncAt, lastError, enabled }` |
| `PUT adsstats/account` | `marketing.settings` | akkaunt + token saqlash (token bo'sh bo'lsa eskisi qoladi) |
| `DELETE adsstats/account` | `marketing.settings` | uzish (`IsActive=false`, qator o'chirilmaydi) |
| `POST adsstats/sync` | `marketing.settings` | qo'lda sinxronizatsiya (`since`,`until` ixtiyoriy) |
| `GET adsstats/overview` | klass | KPI + kunlik qator + platforma bo'yicha bo'linish |
| `GET adsstats/campaigns` | klass | kampaniya→adset→ad daraxti + metrikalar |
| `GET adsstats/roi` | klass | **§4.8 — CRM bilan birlashtirilgan hisobot** |

**Saqlashdan oldin token validatsiyasi** (`IgAdPage` naqshi): `FetchAccountAsync` chaqir,
xato bo'lsa **saqlama** va o'zbekcha sabab qaytar.

### 4.8 🏆 ROI HISOBOTI — loyihaning asosiy qiymati

Bu — Ads Manager'da **yo'q** narsa. Zanjir:

```
IgAdInsight (xarajat, kampaniya bo'yicha)
   └─ IgAdLead.CampaignId / AdId  (qaysi reklama qaysi lidni keltirdi)
        └─ IgAdLead.LeadId → Lead
             └─ LeadOutcome (mavjud servis!)  → Stage · Student · FinanceTransaction
```

**`LeadOutcome` — mavjud servis, uni ishlat**, "lid → bosqich → o'quvchi → to'lov → aktiv"
zanjiri allaqachon shu yerda (lead-forms va level-tests statistikasi shundan quriladi).

Hisobot qatori (kampaniya bo'yicha):

| Ustun | Manba |
|---|---|
| Kampaniya | `IgAdEntity.Name` |
| Xarajat | `SUM(IgAdInsight.SpendMinor)` |
| Ko'rsatish / Qamrov | `IgAdInsight` |
| Meta lidlari | `LeadsOnsite + LeadsPixel` |
| **CRM lidlari** | `COUNT(DISTINCT IgAdLead.LeadId)` shu `CampaignId` bo'yicha |
| **CPL (lid narxi)** | Xarajat / CRM lidlari |
| **O'quvchi bo'ldi** | `LeadOutcome` — `ConvertedStudentId` bor lidlar |
| **To'lov qildi** | `LeadOutcome` — `FinanceTransaction` `tuition` net > 0 |
| **Daromad** | shu lidlarning net `tuition` yig'indisi |
| **CAC (mijoz narxi)** | Xarajat / to'lov qilganlar |
| **ROI** | (Daromad − Xarajat) / Xarajat |

⚠️ **Muhim nuanslar:**
- **Meta lidlari ≠ CRM lidlari.** Farq normal: telefon dublikati (bitta odam ikki marta),
  90 kunlik oyna, token xatosi. Farqni UI'da **ochiq ko'rsat**: "Meta: 42 · CRM: 38 (4 ta dublikat)".
- **Kitob savdosi hisobga olinmaydi** (`FinanceTransaction` ga yozilmaydi — `books.md`).
  "To'ladi" = faqat `tuition`.
- **To'liq qaytarilgan lid to'lamagan hisoblanadi** (net ≤ 0), `Revenue` ga manfiy qo'shilmaydi.
- **Konversiya foizi TAKRORSIZ LIDLAR bo'yicha** (`DISTINCT LeadId`), ariza soni bo'yicha emas.
- Daromad **butun umr bo'yi**, xarajat **tanlangan oraliqda** — bu **taqqoslanmaydigan
  o'lchov**. UI'da ochiq yoz: "Daromad — lid kelganidan buyon jami".

**Kesh:** `DataCache`, kalit `marketing:ads-roi`, TTL 10 daqiqa, bog'liq tiplar:
`IgAdInsight`, `IgAdEntity`, `IgAdLead`, `Lead`, `LeadStage`, `StudentGroup`, `FinanceTransaction`.

**Agregatlar server tomonda, butun natija bo'yicha hisoblanadi** — sahifalangan qatorlardan emas
(`books.md` darsi).

### 4.9 Frontend — `InstagramAdsStats.tsx`

Route `/admin/marketing/reklama-statistikasi`, ruxsat `marketing.adsstats`.
(⚠️ `constants.ts` + `navigation.ts` + `App.tsx` — uch joyga qo'sh.)

**Tuzilma:**
1. **Filtr paneli:** sana oralig'i (tayyor tugmalar: Bugun · 7 kun · 30 kun · Bu oy · O'tgan oy),
   platforma (Hammasi / Instagram / Facebook), kampaniya.
2. **KPI kartochkalari (7 ta):** Xarajat · Ko'rsatish · Qamrov · CRM lidlari · **CPL** ·
   O'quvchi bo'ldi · **ROI**.
3. **Grafik 1:** kunlik xarajat (chiziq, `#0284c7`). ⚠️ Lid soni **alohida grafikda**
   (`#e11d48`) — bitta grafikda ikki o'q **taqiqlanadi**.
4. **Grafik 2:** platforma bo'yicha ulush (Instagram vs Facebook) — donut.
5. **Jadval:** kampaniya → adset → ad (ochiladigan), §4.8 ustunlari bilan.
   Har qatorda **«Lidlarni ko'rish →»** → `/admin/marketing/reklama-lidlari?campaign={id}`.
6. **Holat bloki:** oxirgi sinxronizatsiya vaqti, xato bo'lsa qizil chip + «Qayta urinish».

Bo'sh holat: "Reklama akkaunti ulanmagan" + Sozlamalarga havola.

### 4.10 Testlar

`MetaInsightsParserTests.cs`:
- `spend "312.45"` + offset 2 → `31245`
- `actions` da yo'q tur → `0` (istisno emas)
- `action_breakdowns` bilan takrorlangan `action_type` → to'g'ri yig'iladi
- `lead` turi hisobga olinmaydi (ikki marta hisoblash yo'q)
- bo'sh/buzuq JSON → `0`, yiqilmaydi

`MetaInsightsRoiTests.cs` (TestDb bilan):
- 2 lid, 1 tasi to'lagan, 1 tasi qaytargan → `Paid = 1`, `Revenue` to'g'ri
- kitob savdosi daromadga qo'shilmaydi
- Meta lidi 5, CRM lidi 3 → farq ko'rsatiladi

---

## 5. E2 — KONTENT REJALASHTIRISH

**Yaxshi xabar:** Instagram Login yo'lida bu uchun **App Review kerak emas** — faqat
bitta yangi scope: **`instagram_business_content_publish`**.

⚠️ Scope qo'shilishi **qayta OAuth** talab qiladi. `IgConst.Scopes` ga qo'shiladi va
foydalanuvchi Sozlamalarda «Qayta ulash» bosishi kerak. UI'da buni tushuntir.

### 5.1 Entity

```csharp
// ==== KONTENT REJALASHTIRISH ====
public class IgScheduledPost
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PostType { get; set; } = "image";  // image | video | reels | story | carousel
    public string Caption { get; set; } = "";
    public string MediaJson { get; set; } = "";      // [{url,type,coverUrl,thumbOffset,altText}]
    public string OptionsJson { get; set; } = "";    // {shareToFeed,locationId,collaborators,audioName}
    public string ScheduledAt { get; set; } = "";    // ISO — BIZNING navbat, Meta emas
    public string Status { get; set; } = "scheduled";// scheduled|processing|published|failed|cancelled
    public string ContainerId { get; set; } = "";
    public string ContainerStatus { get; set; } = "";
    public string MediaId { get; set; } = "";        // chop etilgandan keyin
    public string Permalink { get; set; } = "";
    public int    Attempts { get; set; }
    public string Error { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string PublishedAt { get; set; } = "";
}
```
`CenterMeta`: `InstagramPublishEnabled` (default **false**).
Migratsiya: `AddInstagramContentScheduler`.

### 5.2 🔴 INSTAGRAM'DA NATIVE REJALASHTIRISH YO'Q

`POST /{ig-user-id}/media` da `scheduled_publish_time` **parametri mavjud emas**, va
konteyner **24 soatdan keyin o'ladi**. Demak:

- Rejalashtirilgan vaqt **bizning `IgScheduledPost.ScheduledAt`** da.
- Konteynerni **oldindan yaratma** — faqat chop etish vaqti kelganda.
- Oqim: worker vaqtni ko'radi → konteyner yaratadi → status so'raydi → `FINISHED` → publish.

### 5.3 Ikki bosqichli oqim

```
1) POST {GraphBase}/me/media
   image:    image_url, caption
   reels:    media_type=REELS, video_url, caption, cover_url, share_to_feed
   story:    media_type=STORIES, image_url yoki video_url
   carousel: bolalar (is_carousel_item=true) → ota-ona (media_type=CAROUSEL, children=id1,id2)
   → { "id": "<CONTAINER_ID>" }

2) GET {GraphBase}/{CONTAINER_ID}?fields=status_code,status
   status_code: IN_PROGRESS | FINISHED | ERROR | EXPIRED | PUBLISHED
   Meta tavsiyasi: daqiqada bir marta, 5 daqiqadan ko'p emas.
   Bizda: 30s → 60s → 120s → 300s (eksponensial), 10 daqiqadan keyin `failed`.

3) POST {GraphBase}/me/media_publish?creation_id={CONTAINER_ID}
   → { "id": "<MEDIA_ID>" }
```

### 5.4 Chop etish limiti

```
GET {GraphBase}/me/content_publishing_limit?fields=config,quota_usage
→ { "data": [{ "quota_usage": 2, "config": { "quota_total": 100, "quota_duration": 86400 } }] }
```
⚠️ Meta hujjatlari zid: qo'llanmada **100**, reference namunasida **50**.
**Ikkalasini ham kodga yozma** — `config.quota_total` ni ish vaqtida o'qi.
Karusel = 1 post. Limit `media_publish` bosqichida tekshiriladi.

### 5.5 Media talablari (validatsiya — sof funksiya, test bilan)

| Tur | Format | Hajm | Davomiyligi | Nisbat |
|---|---|---|---|---|
| Rasm | **faqat JPEG** | ≤8 MB | — | 4:5 – 1.91:1, kenglik 320–1440 px, sRGB |
| Reels | MOV/MP4 | ≤300 MB | 3–900 s | 9:16 |
| Story rasm | JPEG | ≤8 MB | — | 9:16 |
| Story video | MOV/MP4 | ≤100 MB | 3–60 s | 9:16 |
| Karusel | 2–10 element | — | — | birinchi elementning nisbatiga qirqiladi |

Caption: ≤2200 belgi, ≤30 hashtag, ≤20 mention.
⚠️ Karusel **bolalarida caption ishlamaydi** — faqat ota-onada.

### 5.6 🔴 MEDIA URL — ochiq HTTPS bo'lishi SHART

Meta faylni **o'zi yuklab oladi**. Auth, IP cheklov, redirect — ishlamaydi.

Loyihada `uploads/` va `UploadsGuard` bor, lekin ular **login ortida**. Ikki variant:

| Variant | Qanday |
|---|---|
| **A (TAVSIYA)** | `uploads/marketing-public/` uchun **ochiq statik marshrut** ochish + fayl nomiga tasodifiy uzun suffiks (`{guid}.jpg`). Faqat rejalashtirilgan post media'si. |
| B | Meta yuklab olgunicha amal qiladigan **imzolangan vaqtinchalik URL** (≥30 daqiqa) |

⚠️ Variant A tanlansa: `UploadsGuard` va `uploads-security.md` qoidalarini buzmaslik uchun
**alohida papka** va **alohida marshrut**, boshqa hech qanday fayl u yerdan chiqmasin.
`SecretLeakAndPublicPageTests` uslubida test yoz: `uploads/marketing-public/` dan tashqari
yo'l ochiq bo'lmasligini qulfla.

### 5.7 Worker vazifasi

`InstagramWorkerService` ga **4-vazifa** qo'shiladi (har 30 soniyada):
```
InstagramPublishEnabled == false → chiq
ScheduledAt <= now VA Status == "scheduled" bo'lgan postlarni ol (max 3 ta)
  → Status = "processing"
  → content_publishing_limit tekshir → limit to'lgan bo'lsa keyingi tsiklga qoldir
  → konteyner yarat → status poll → publish
  → Status = "published", MediaId, Permalink, PublishedAt
  → xato: Attempts++, Attempts >= 3 → "failed" + Telegram alert
```

### 5.8 Xato kodlari

⚠️ Rasmiy Instagram xato kodlari sahifasi mavjud emas — quyidagilar amaliyotdan
(uchinchi tomon manbasi), o'zbekcha xabarga aylantiriladi:

| Kod | Ma'no |
|---|---|
| `2207052` | Media yuklab bo'lmadi (**eng ko'p uchraydi** — URL yopiq/sekin) |
| `2207020` | Konteyner muddati o'tdi (24 soat) |
| `2207003` | Yuklab olish timeout |
| `2207005` | JPEG emas |
| `2207009` | Nisbat noto'g'ri |
| `2207010` | Caption juda uzun |
| `2207026` | Video kodek qo'llab-quvvatlanmaydi |
| `2207042` | Kunlik limit |
| `2207001` | Spam deb belgilandi |

### 5.9 Muhim ogohlantirishlar

1. **Chop etilgan IG media'ni API orqali tahrirlash ham, o'chirish ham mumkin emas.**
   UI'da ochiq yoz: "Joylangandan keyin faqat Instagram ilovasidan o'chiriladi".
2. `audio_name` — **bir marta** o'zgartiriladi.
3. `collaborators` (≤3) — ular qabul qilishi kerak.
4. sRGB bo'lmagan rasm konvertatsiya qilinadi, rang o'zgarishi mumkin.
5. Token muddati — soat 3:00 da ishga tushgan job'ning tokeni o'lik bo'lishi eng ko'p
   uchraydigan nosozlik. Job boshida token holatini tekshir.

### 5.10 Frontend — `InstagramContent.tsx`

Route `/admin/marketing/kontent`, ruxsat `marketing.content`.
- **Kalendar** (oy/hafta) + navbat ro'yxati.
- Yangi post modali: tur, media yuklash, caption + **AI bilan caption generatsiya**
  (mavjud `GeminiService` + `IgKnowledge` konteksti), vaqt tanlash, IG preview.
- Kunlik limit indikatori (`quota_usage / quota_total` real vaqtda).
- Xato holatida o'zbekcha sabab + «Qayta urinish».

---

## 6. E3 — REKLAMA IZOHLARI ATRIBUTSIYASI

**Muammo:** Instagram Login `comments` webhook'ida `ad_id` **yo'q** (§3.2).

**Yechim (E1 tugagach amalga oshiriladi):**

`IgAdEntity.CreativeStoryId` (= `effective_object_story_id`) — reklama ostidagi
**haqiqiy media ID**. Izoh kelganda:

```
webhook value.media.id  →  IgAdEntity da CreativeStoryId yoki uning media qismi bilan solishtir
   topildi  → IgMessage/IgConversation ga AdId yoziladi, izoh "reklama izohi" deb belgilanadi
   topilmadi → organik
```

Buning uchun `IgConversation` va `IgMessage` ga qo'shiladi:
```csharp
public string AdId { get; set; } = "";        // topilgan bo'lsa
public string AdCampaignId { get; set; } = "";
```
Migratsiya: `AddIgAdAttribution`.

**Nima beradi:**
- Inbox va Comment ro'yxatida 📢 belgisi + kampaniya nomi.
- «Reklama izohidan kelgan lidlar» — `LeadOrigins.Ads` ga qo'shimcha signal.
- ROI hisobotida: reklama faqat forma lidini emas, **izoh orqali kelgan lidni** ham keltirgani ko'rinadi.

⚠️ **Cheklovlar (ochiq yoz):**
- **Boostlangan organik post** — ishlaydi (`original_media_id` mavjud).
- **Dark post (chop etilmagan reklama)** — bizning `/media` ro'yxatimizda yo'q, ishlamaydi.
- **Dinamik (katalog) reklama** — Meta `ad_id` ni umuman bermaydi, hech qanday yo'l bilan
  aniqlab bo'lmaydi.
→ Ya'ni bu **taxminiy atributsiya**. UI'da "taxminiy" deb belgilash shart.

---

## 7. E4 — CAPI: LID SIFATINI META'GA QAYTARISH

**Nima uchun:** hozir Meta faqat "lid keldi"ni biladi. "Bu lid o'quvchi bo'ldi va pul to'ladi"ni
bilmaydi. CAPI bilan qaytarilsa — Meta **haqiqiy mijoz keltiradigan** auditoriyaga
optimallashadi. Bu odatda **CPL ni 20–40% tushiradi**.

### 7.1 Meta talablari (rasmiy, tekshirilgan)

| Talab | Qiymat | Markazda bormi? |
|---|---|---|
| Lead Ads (Instant Form) ishlatish | ✅ bor | ✅ |
| Meta Lead ID (15–17 raqam) CRM'da saqlangan | `IgAdLead.LeadgenId` | ✅ **allaqachon saqlanadi** |
| Oyiga kamida **200 lid** | — | ⚠️ tekshiring |
| Kuniga kamida bir marta yuklash | worker | ✅ |
| Maqsadli bosqich **28 kun ichida** sodir bo'lishi | "to'lov qildi" — odatda 1–2 hafta | ✅ |
| Konversiya darajasi **1%–40%** | — | ⚠️ tekshiring |

⚠️ Agar oyiga 200 lid yo'q bo'lsa — Meta "Conversion Leads" optimizatsiyasini yoqmaydi,
lekin **hodisalarni yuborish baribir foydali** (atributsiya hisobotlari uchun).

### 7.2 Entity

```csharp
public class IgCapiEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string LeadId { get; set; } = "";        // CRM Lead.Id
    public string LeadgenId { get; set; } = "";     // Meta lead id
    public string EventName { get; set; } = "";     // erkin matn — §7.6
    public string EventId { get; set; } = "";       // dedup: "{leadgenId}_{unix}"
    public string EventTime { get; set; } = "";     // ISO
    public string Status { get; set; } = "pending"; // pending | sent | failed | skipped
    public int    Attempts { get; set; }
    public string Error { get; set; } = "";
    public string PayloadJson { get; set; } = "";   // ⚠️ HASHLANGAN holatda, xom PII EMAS
    public string CreatedAt { get; set; } = "";
    public string SentAt { get; set; } = "";
}
```
`CenterMeta`:
```csharp
public bool   InstagramCapiEnabled { get; set; }             // default FALSE
public string InstagramCapiDatasetId { get; set; } = "";     // Events Manager dataset/pixel ID
public string InstagramCapiToken { get; set; } = "";         // javobga TUSHMAYDI
```
Migratsiya: `AddMetaCapi`.

### 7.3 So'rov

```
POST {FbGraphBase}/{DATASET_ID}/events?access_token={TOKEN}
```
```json
{ "data": [{
    "event_name": "Sifatli lid",          // ⚠️ §7.6 — ERKIN MATN, Events Manager'dagi bilan bir xil
    "event_time": 1755600000,
    "action_source": "system_generated",
    "event_id": "1234567890123456_1755600000",
    "user_data": {
      "lead_id": 1234567890123456,
      "ph": ["<sha256(998901234567)>"]
    },
    "custom_data": { "lead_event_source": "WunderkindLC", "event_source": "crm" }
  }] }
```

### 7.4 🔴 HASHLASH QOIDALARI

**SHA-256, hex, kichik harf.** Normallashtirish:

| Maydon | Normallashtirish |
|---|---|
| `ph` (telefon) | **faqat raqamlar**, boshidagi nollar olib tashlanadi, **mamlakat kodi bilan** → `998901234567` |
| `em` (email) | trim + lowercase |
| `fn`/`ln` | lowercase, tinish belgisiz |

🔴 **`lead_id` HASHLANMAYDI.** Bu Meta'ning lid ID'si, xom holda yuboriladi (raqam sifatida).
Xuddi shunday hashlanmaydiganlar: `client_ip_address`, `client_user_agent`, `fbc`, `fbp`,
`page_id`, `page_scoped_user_id`, `ig_sid`.

Sof funksiya `MetaCapiHash.cs` + testlar:
```csharp
public static string Phone(string raw);   // "+998 90 123-45-67" → sha256("998901234567")
public static string Email(string raw);
```

### 7.5 Cheklovlar

- `event_time` **7 kundan eski bo'lmasin** — aks holda **butun so'rov rad etiladi**.
- Bir so'rovda **1000 tagacha** hodisa.
- Dedup: `event_name` + `event_id`, **48 soatlik** oyna.
- `test_event_code` — faqat sinovda, produksiyada **olib tashlanadi**.

### 7.6 Qaysi hodisa qachon

🔴 **`event_name` — Conversion Leads oqimida ERKIN MATN (advertiser o'zi belgilaydi).**
Meta hech qanday qat'iy `"Qualified Lead"` satrini talab qilmaydi — hujjatdagi misollar:
*"Initial Lead from Facebook"*, *"Marketing Qualified Lead"*, *"Sales Opportunity"*, *"Converted"*.
Shart: **Events Manager'da sozlangan bosqich nomi bilan AYNAN bir xil bo'lishi**.
(Faqat **Business Messaging CAPI** da qat'iy enum bor — u yerda `QualifiedLead` camelCase.)

→ **Nomlar `CenterMeta` sozlamasiga chiqariladi**, kodga yozib qo'yilmaydi:
```csharp
public string InstagramCapiStageQualified { get; set; } = "Sifatli lid";
public string InstagramCapiStageWon       { get; set; } = "To'lov qildi";
```

| CRM'da nima bo'ldi | CAPI hodisasi (`event_name`) |
|---|---|
| Lid yaratildi (`IgAdLead` → `Lead`) | ⚠️ yuborilmaydi — Meta buni allaqachon biladi |
| `Lead.Stage` "sifatli"/"sinov darsi" bosqichiga o'tdi | `InstagramCapiStageQualified` |
| `Lead.ConvertedStudentId` to'ldi | shu (agar hali yuborilmagan bo'lsa) |
| Birinchi `tuition` to'lovi (`FinanceTransaction`) | `InstagramCapiStageWon` + `custom_data.value` + `currency: "UZS"` |

**Trigger:** `Lead.Stage` o'zgarishini ushlash uchun **yangi hook yozma** — mavjud
`LeadEvent` yozuvidan yoki worker'ning kunlik skanidan foydalan:
```
Har kuni: IgAdLead bilan bog'langan Lead'larni ol
  → LeadOutcome bo'yicha hozirgi holatni hisobla
  → IgCapiEvent da hali yuborilmagan holat bo'lsa — qator yarat
  → navbatdan yuborish
```
Bu **soddaroq va ishonchliroq** (o'tkazib yuborilgan o'zgarish keyingi kuni tuziladi).

### 7.7 Maxfiylik

🔴 `IgCapiEvent.PayloadJson` ga **xom telefon/email yozma** — faqat hashlangan holat.
Xom PII faqat `Lead` jadvalida qoladi.
DPA (Data Protection Assessment) aynan shuni tekshiradi.

---

## 8. E5 — FACEBOOK PAGE / MESSENGER (shartli)

> **⚠️ Bu modul faqat §3.3 qarori "ha" bo'lsa quriladi.**
> `pages_messaging` **App Review talab qiladi** — Standard Access'da faqat ilovada
> roli bor odamlar bilan yozishish mumkin, ya'ni prodda ishlamaydi.

Agar quriladigan bo'lsa, quyidagilar Instagram'dan **farq qiladi**:

| | Instagram | Messenger |
|---|---|---|
| Matn limiti | **1000 bayt** | 2000 belgi |
| Handover Protocol | ❌ (2025-10-23 da bekor qilindi, Conversation Routing) | ✅ ishlaydi |
| Message Tags | faqat `HUMAN_AGENT` | faqat `HUMAN_AGENT` (qolganlari 2026-02-10 da o'chirildi) |
| Standby kanali | `entry[].standby[]` | `entry[].standby[]` |
| `messaging_type` | — | `RESPONSE` / `UPDATE` / `MESSAGE_TAG` |

**Webhook:** obyekt `page`, maydonlar `messages, messaging_postbacks, message_echoes,
messaging_referrals, messaging_handovers, standby, messaging_policy_enforcement`.

🔴 **`entry[].standby[]` — ALOHIDA MASSIV.** `messaging` deb o'qisang hodisalarni
butunlay yo'qotasan. Standby hodisalariga **hech qachon avtomatik javob berma**.

🔴 **`HUMAN_AGENT` tegini AI bilan ishlatma.** Meta hujjati: "a business representative to
**manually** respond". AI bilan ishlatish akkaunt cheklanishiga olib keladi.
Faqat operator qo'lda yozgan xabar bu tegni oladi.

**Click-to-Messenger atributsiyasi (qimmatli):** reklamadan kelgan suhbat `ad_id` bilan keladi.
⚠️ **Ikki yo'ldan ham ushla:**
- **Yangi suhbat** → `messaging_postbacks` **ichida** `postback.referral.{ref, ad_id, source}`
- **Mavjud suhbat** → alohida `messaging_referrals` hodisasi (+ 24 soatlik oyna qayta ochiladi)

Faqat `messaging_referrals` ni tinglasang — **birinchi marta yozgan har bir mijozning
atributsiyasini yo'qotasan.**

---

## 9. E6 — MAVJUD MODULNI YAXSHILASH (kichik ishlar)

| # | Nima | Qiyinlik |
|---|---|---|
| 9.1 | **Story javoblari** — `message.reply_to.story.{id,url}` ni parse qilish, story rasmini darhol o'zimizga ko'chirish (CDN linki tez o'ladi) | kichik |
| 9.2 | **Story mention** — `attachments[].type == "story_mention"` ni ajratib belgilash | kichik |
| 9.3 | **`ig_post` attachment** — 2026-02-01 dan eski `share` turi olib tashlandi, yangisini parse qil | kichik |
| 9.4 | **`is_deleted`** — mijoz xabarni o'chirsa mazmunni **haqiqatan o'chir** (Platform Terms talabi) | kichik |
| 9.5 | **Bilim bazasi RAG** — hozir butun `IgKnowledge` promptga tiqiladi (`KnowledgeLimit=12000`). Bilim o'sganda kesiladi. Yechim: Gemini embedding + PostgreSQL'da kosinus (yangi kutubxonasiz — `float[]` + oddiy hisob, yoki `pgvector` bo'lsa u) | o'rta |
| 9.6 | **Javob sifati jurnali** — operator AI javobini tahrirlaganda farqni saqlash (prompt yaxshilash uchun eng qimmatli ma'lumot) | kichik |
| 9.7 | **`messaging_policy_enforcement`** webhook maydoniga obuna — Meta'ning ogohlantirishi, cheklovdan oldingi signal. Kelganda darhol Telegram alert + avtomatikani pauza | kichik, **yuqori qiymat** |
| 9.8 | **`v23.0` → versiya konstantasi** — hozir URL ichiga yopishtirilgan, `IgConst` ga chiqar | kichik |

---

## 10. BOSQICHMA-BOSQICH REJA

| Bosqich | Nima | Natija | Baho |
|---|---|---|---|
| **B0** | §3 qarorlari: System User token olish, FB Page kerakmi — hal qilish | Qarorlar yozilgan | 1 kun |
| **B1** | **E1a** — `IgAdAccount`/`IgAdEntity`/`IgAdInsight` + migratsiya + `MetaInsightsApi` + `MetaInsightsParser` + testlar | Ma'lumot bazaga tushadi | 3 kun |
| **B2** | **E1b** — `MetaInsightsService` sync + worker vazifasi + controller endpointlari | Kunlik sinxronizatsiya ishlaydi | 2 kun |
| **B3** | **E1c** — ROI hisoboti (`LeadOutcome` bilan) + kesh + testlar | Haqiqiy qiymat | 2 kun |
| **B4** | **E1d** — `InstagramAdsStats.tsx` + ruxsat kaliti (3 joy) + navigatsiya | Ekran tayyor | 3 kun |
| **B5** | **E6.7 + E6.8** — policy enforcement webhook, versiya konstantasi | Xavfsizlik | 0.5 kun |
| **B6** | **E4** — CAPI: entity, hash funksiyalari, worker skani, sozlamalar UI | Reklama optimallashadi | 3 kun |
| **B7** | **E3** — reklama izohlari atributsiyasi (`CreativeStoryId` xaritasi) | Izoh manbasi ko'rinadi | 2 kun |
| **B8** | **E2a** — `instagram_business_content_publish` scope + qayta OAuth + `IgScheduledPost` + ochiq media marshruti | Asos | 2 kun |
| **B9** | **E2b** — konteyner oqimi + worker + limit + xato xaritasi + testlar | Joylash ishlaydi | 3 kun |
| **B10** | **E2c** — `InstagramContent.tsx` kalendar + AI caption | Ekran tayyor | 3 kun |
| **B11** | **E6.1–E6.6** — story, media, RAG, sifat jurnali | Sifat | 3 kun |
| **B12** | (shartli) **E5** — Facebook/Messenger + App Review | FB kanali | 2 hafta+ |

**Tavsiya:** B1–B4 (Ads Insights) — birinchi va eng muhim. U tugagach marketing byudjeti
o'lchanadigan bo'ladi, qolgani ustiga quriladi.

---

## 11. 🔴 ENG MUHIM OGOHLANTIRISHLAR

**Loyiha konventsiyalari:**
1. `DateTime.Now` — **taqiqlanadi**, faqat `AppClock`.
2. `Lead.PhoneKey` — **qo'lda yozilmaydi**, `SaveChanges` o'zi hisoblaydi.
3. Yangi bayroq **default `false`** — entity'da ham, migratsiyada ham.
4. Ruxsat kaliti **uch joyda** (`constants.ts` + `navigation.ts` + `App.tsx`) —
   `PermissionCatalogTests` tekshiradi.
5. `.env` kaliti **`docker-compose.yml` ga ham** — `EnvKeysWiringTests` tekshiradi.
6. Token/secret **javobga, logga, auditga tushmaydi**. Frontendga faqat `tokenSet`.
7. `_ = Task.Run(...)` — **taqiqlanadi**, navbat DB jadvali.
8. **TanStack Query yo'q**, `useState`/`useEffect` + axios.
9. Grafikda **yashil-qizil juftlik yo'q**, **ikki o'q yo'q**.
10. Cheklov (top-N, limit) — **hech qachon jimgina qo'llanilmaydi**, foydalanuvchiga yoziladi.

**Meta API:**
11. `spend` — **string, major unit**; byudjet — **integer, minor unit**. №1 xato.
12. `actions` da qiymati 0 bo'lgan tur **umuman yo'q** — indeks bilan o'qima.
13. `lead` = `onsite` + `pixel` — **uchtasini qo'shma**.
14. Insights sanasi **reklama akkaunti vaqt zonasida**, Toshkentda emas.
15. Instagram'da **native rejalashtirish yo'q**, konteyner **24 soatda o'ladi**.
16. Chop etilgan IG media'ni **tahrirlab ham, o'chirib ham bo'lmaydi**.
17. Media URL **ochiq HTTPS** bo'lishi shart — Meta o'zi yuklab oladi.
18. CAPI'da **`lead_id` hashlanmaydi**, telefon/email hashlanadi.
19. CAPI `event_time` **7 kundan eski bo'lsa butun so'rov rad etiladi**.
20. `entry[].standby[]` — **alohida massiv** (E5 uchun).
21. **`HUMAN_AGENT` tegini AI bilan ishlatma** — akkaunt cheklanadi.
22. Ads Insights kvotasi: **sizning 4xx xatolaringiz kvotani kamaytiradi**.

**Meta hujjatlarida noaniq (kodga konstanta qilma):**
- `content_publishing_limit` — qo'llanmada 100, reference'da 50 → ish vaqtida o'qi.
- Instagram publishing xato kodlari — rasmiy sahifa yo'q.
- `Lead.platform` enum qiymatlari (`"ig"`/`"fb"`) — matn sifatida saqla.
- Instagram Login'da `/me/media` — Overview'da `/me` alias tasdiqlangan, lekin endpoint
  reference'da `/{ig-id}/media` yozilgan. **`IgAccount.IgUserId` bilan aniq yo'lni ishlat**,
  `/me` ga tayanma.
- 2026-06-15 da Facebook Page Insights metrikalari o'chirilgan — aniq ro'yxat login ortida.

---

## 12. HUJJATLASHTIRISH (ish tugagach majburiy)

Loyiha uslubiga ko'ra har modul o'z hujjatiga ega:

| Fayl | Nima yoziladi |
|---|---|
| `instagram/REKLAMA-STATISTIKASI.md` | E1: protokol, so'rovlar, ROI formulalari, sozlash |
| `instagram/KONTENT.md` | E2: joylash oqimi, media talablari, xato kodlari |
| `instagram/CAPI.md` | E4: hodisalar, hashlash, Meta talablari |
| `.claude/rules/marketing-instagram.md` | Yangi §17, §18… bo'limlari — **mavjud matnni o'zgartirmasdan qo'shiladi** |
| `instagram/SOZLASH.md` | Yangi bo'limlar: System User token olish, `content_publish` scope qo'shish |
| `.env.example` | Yangi kalitlar (agar bo'lsa) — izoh bilan |

---

## 13. AGENTGA YAKUNIY KO'RSATMA

**Boshlash tartibi:**
1. `.claude/rules/marketing-instagram.md` — **to'liq o'qi**.
2. `instagram/TEXNIK.md` va `instagram/REKLAMA-LIDLARI.md` — o'qi.
3. `WunderkindLC.Application/Services/MetaAdsApi.cs` va `MetaLeadgenService.cs` — **naqsh sifatida** o'qi
   (yangi kod xuddi shu uslubda bo'ladi).
4. `WunderkindLC.Server/Controllers/InstagramController.cs` — `ads/*` endpointlari qanday
   yozilganini ko'r.
5. `WunderkindLC.Client/src/pages/admin/marketing/InstagramAdLeads.tsx` — yangi sahifa
   shu naqshda bo'ladi.
6. **B1 bosqichidan** boshla.

**Har bosqich oxirida:**
- `dotnet build && dotnet test` — o'tsin.
- `cd WunderkindLC.Client && npm run build` — o'tsin.
- Yangi migratsiya bo'lsa: `dotnet ef migrations add <Nom> -p WunderkindLC.Infrastructure -s WunderkindLC.Server`
- Qisqacha hisobot: nima qilindi · qanday qaror qabul qilindi · nima qoldi.
- Meta API fakti bu hujjatdagi bilan mos kelmasa — **to'xta va ayt**, taxmin qilma.
