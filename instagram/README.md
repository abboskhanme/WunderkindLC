# Instagram AI sotuv agenti — modul haqida

> Markazning Instagram **Professional** akkauntiga kelgan **izohlar** va **DM**larni webhook
> orqali qabul qilib, ularga o'zbek tilida sotuvchi sifatida javob beradigan, qiziqqan odamni
> CRM'da **lidga** aylantiradigan va operatorga xabar beradigan modul.
>
> CRM'da joyi: **Marketing** bo'limi (`/admin/marketing`), ruxsat kaliti — **`marketing`**.

Tanlangan yo'l: **Instagram API with Instagram Login**.
Facebook Page **KERAK EMAS**, Business Verification **KERAK EMAS**,
**App Review ham KERAK EMAS** (Standard Access yetadi — akkaunt bizniki).

---

## 1. Nima qiladi

| Hodisa | Modul nima qiladi |
|---|---|
| Postimiz ostiga izoh yozildi | Izohga **ochiq javob** yozadi; sozlama yoqilgan bo'lsa qo'shimcha **shaxsiy DM** (private reply) yuboradi |
| Bizga DM keldi | Bilim bazasi asosida javob beradi, kerak bo'lsa telefon so'raydi |
| Mijoz "operator", "odam bilan gaplashaman" dedi | Darhol eskalatsiya — suhbat `NeedsOperator` bo'ladi, Telegram'ga alert ketadi |
| Mijoz xarid niyatini bildirdi yoki telefon qoldirdi | Mavjud **Lidlar** moduliga lid yoziladi (`Source = "Instagram"`) |
| Operator telefonidan qo'lda javob yozdi | Bot o'sha suhbatda **jim turadi** (echo mexanizmi, §4) |

Nima QILMAYDI: o'zi birinchi bo'lib yozmaydi (Instagram ruxsat bermaydi), narx **o'ylab
topmaydi** (faqat bilim bazasidan), matnsiz xabarga (rasm/stiker/ovoz) javob yozmaydi —
faqat operatorga signal beradi.

---

## 2. Oqim (bir qarashda)

```
Instagram
   │  izoh / DM
   ▼
POST /api/public/instagram/webhook          ← InstagramWebhookController
   │  1) XOM body baytlari o'qiladi
   │  2) InstagramSignature.Verify  (mos kelmasa → 403)
   │  3) IgWebhookEvent (Status="pending") yoziladi
   │  4) ══ DARHOL 200 OK ══                (Meta 5 soniya kutadi)
   ▼
IgWebhookEvent  (durable navbat, baza jadvali)
   │  har 2 soniyada
   ▼
InstagramWorkerService (BackgroundService)  → InstagramPipeline.ProcessAsync
   │
   ├─ InstagramEventParser.Parse       → comment | dm | echo
   ├─ echo bo'lsa → operator pauzasi yoqiladi va CHIQADI
   ├─ o'zimizdan kelgan izoh → tashlanadi (cheksiz halqa himoyasi)
   ├─ IgConversation topiladi/yaratiladi, kiruvchi IgMessage yoziladi
   ├─ modul o'chiq / pauza / kunlik limit → javob berilmaydi
   ├─ IgAutoRule (kalit so'z) mos keldimi?  ha → tayyor matn, AI chaqirilmaydi
   ├─ yo'q → InstagramAgentService.AskAsync  (GeminiService)  → IgAgentOutput
   ├─ InstagramApi: ReplyToCommentAsync / SendPrivateReplyAsync / SendDmAsync
   ├─ chiquvchi IgMessage yoziladi
   ├─ ShouldCreateLead → InstagramLeadBridge.UpsertAsync → Lead
   └─ IsHot / Escalate → Telegram alert (mavjud bot)
```

---

## 3. Fayllar qayerda

### Backend — `WunderkindLC.Application/Services/`

| Fayl | Vazifasi |
|---|---|
| `InstagramContract.cs` | `IgConst` konstantalari + **sof funksiyalar**: `ClampScore`, `NormalizeIntent/Language`, `IsHot`, `ShouldCreateLead`, `DmWindowOpen`, `OperatorPaused`, `ExtractPhone` |
| `InstagramSignature.cs` | `X-Hub-Signature-256` tekshiruvi (xom baytlardan, **fail-closed**) + GET verify challenge |
| `InstagramEventParser.cs` | Meta xom JSON → `IgIncomingEvent[]`; **deterministik** dedup kaliti |
| `InstagramApi.cs` | Graph API klienti: OAuth almashuv, token yangilash, izohga javob, private reply, DM, media caption |
| `InstagramAgentService.cs` | Prompt qurish + Gemini chaqiruvi + `IgAgentOutput` parse |
| `InstagramPipeline.cs` | Asosiy oqim — bitta `IgWebhookEvent`ni boshidan oxirigacha |
| `InstagramLeadBridge.cs` | Suhbatdan **Lead** yaratish/yangilash (first-touch qoidasi) |
| `InstagramWorkerService.cs` | `BackgroundService`: navbat · token yangilash · eski hodisalarni tozalash |

### Backend — controllerlar (`WunderkindLC.Server/Controllers/`)

| Fayl | Yo'l | Kirish |
|---|---|---|
| `InstagramWebhookController.cs` | `api/public/instagram` | **`[AllowAnonymous]`** — Meta murojaat qiladi (GET verify · POST webhook · GET callback) |
| `InstagramController.cs` | `api/admin/instagram` | `[AdminPerm("marketing", ReadRequiresPerm = true)]` |

### Domen va baza

`WunderkindLC.Domain/Entities.cs` → `// ═══ MARKETING — INSTAGRAM AI AGENTI ═══` bo'limi:
`IgAccount` · `IgWebhookEvent` · `IgConversation` · `IgMessage` · `IgAutoRule` ·
`IgKnowledge` · `IgOAuthState`, hamda `CenterMeta` ga qo'shilgan `Instagram*` sozlamalari.
Migratsiya: **`AddInstagramAgent`**.

### Frontend — `WunderkindLC.Client/src/pages/admin/marketing/`

| Sahifa | Yo'l |
|---|---|
| Boshqaruv paneli | `/admin/marketing` |
| Inbox (suhbatlar) | `/admin/marketing/inbox` |
| Javob qoidalari | `/admin/marketing/rules` |
| Bilim bazasi | `/admin/marketing/knowledge` |
| Analitika | `/admin/marketing/analytics` |
| Sozlamalar (ulash) | `/admin/marketing/settings` |

API klienti — `src/api/services/instagram.ts`.

---

## 4. Qanday yoqiladi (qisqacha)

To'liq bosqichma-bosqich qo'llanma: **[`SOZLASH.md`](SOZLASH.md)** (~30–40 daqiqa).

1. Instagram akkaunt **Professional** (Business yoki Creator) qilinadi;
2. `developers.facebook.com` da Meta App ochiladi → **Instagram → API setup with
   Instagram login** → App ID va App Secret olinadi;
3. `.env` ga `INSTAGRAM_APP_SECRET` va `INSTAGRAM_VERIFY_TOKEN` yoziladi (`docker compose up -d`);
4. CRM → Marketing → Sozlamalar: **App ID** kiritiladi;
5. Meta'da webhook ulanadi: `https://<domen>/api/public/instagram/webhook`,
   maydonlar `comments` va `messages` (⚠️ `message_echoes` Meta'da endi YO'Q — echo
   `messages` ichida `is_echo` bilan keladi);
6. **«Instagram'ni ulash»** bosiladi (OAuth) — token, ID lar va webhook obunasi avtomatik;
7. **Bilim bazasi** to'ldiriladi (kurslar va narxlar);
8. `POST /test-agent` yoki Sozlamalardagi sinov maydoni bilan tekshiriladi;
9. **Oxirida** avtojavob yoqiladi: `InstagramEnabled` + `InstagramAutoReplyComments` /
   `InstagramAutoReplyDm`.

⚠️ Barcha bayroqlar **default `false`** — sozlanmaguncha mijozga jonli javob **ketmaydi**.

---

## 5. Qayerdan boshlash

| Savol | Qayerga qarash |
|---|---|
| "Qanday sozlanadi, qadamma-qadam?" | [`SOZLASH.md`](SOZLASH.md) |
| "Meta bilan qaysi so'rov almashinadi, xato kodi nima degani?" | [`TEXNIK.md`](TEXNIK.md) |
| "Kod yozayotganda nimaga tegmaslik kerak?" | [`../.claude/rules/marketing-instagram.md`](../.claude/rules/marketing-instagram.md) |
| "Lid qanday yoziladi?" | `.claude/rules/crm-leads.md` + `InstagramLeadBridge` |
| "Audit qayerda ko'rinadi?" | `.claude/rules/audit.md`, `EntityType = "Instagram"` → bo'lim `marketing` |

---

## 6. Qo'shimcha modullar (2026-08 kengaytirishi)

Yuqoridagi hamma narsa **izoh va DM agenti** haqida. Marketing bo'limida undan tashqari yana
to'rtta **mustaqil** modul bor: har birining o'z bayrog'i (default **o'chiq**), o'z tokeni,
o'z sahifasi va o'z qo'llanmasi.

| Modul | Nima beradi | Sahifa | Ruxsat | Qo'llanma |
|---|---|---|---|---|
| **Reklama lidlari** | Reklamadagi forma (Instant Form) to'ldirilsa F.I.Sh. va telefon CRM lidiga tushadi | `/admin/marketing/reklama-lidlari` | `marketing.leadads` | [`REKLAMA-LIDLARI.md`](REKLAMA-LIDLARI.md) |
| **Reklama statistikasi** | Xarajat · ko'rsatish · lid narxi (**CPL**) · **ROI** — "qaysi reklama pul keltirdi" | `/admin/marketing/reklama-statistikasi` | `marketing.adsstats` | [`REKLAMA-STATISTIKASI.md`](REKLAMA-STATISTIKASI.md) |
| **Kontent joylash** | Rasm/Reels/Story/karuselni CRM'dan rejalashtirib joylash | `/admin/marketing/kontent` | `marketing.content` | [`KONTENT.md`](KONTENT.md) |
| **CAPI** | "Bu lid mijoz bo'ldi va pul to'ladi" ni Meta'ga qaytarish — reklama shunga optimallashadi | Sozlamalar kartochkasi | `marketing.settings` | [`CAPI.md`](CAPI.md) |

Migratsiya: **`AddMarketingExpansion`** (to'rttasi ham bitta migratsiyada).

### Nima o'zgardi (bir qarashda)

```
Marketing bo'limi endi 8 sahifa:
  Boshqaruv paneli · Inbox · Javob qoidalari · Bilim bazasi · Analitika
  · Reklama lidlari · Reklama statistikasi · Kontent · Sozlamalar
```

- **Reklama izohlari** endi kampaniyaga bog'lanadi (🔴 **taxminiy** — boostlangan postda
  ishlaydi, dark post va dinamik reklamada yo'q);
- **Story javoblari**, story mention va ulashilgan post ajratib belgilanadi;
- mijoz xabarni o'chirsa mazmun **haqiqatan o'chadi** (Meta Platform Terms talabi);
- **`messaging_policy_enforcement`** — Meta cheklov qo'yishidan oldingi ogohlantirish:
  avtomatik javoblar **pauza qilinadi** va Telegram'ga alert ketadi.

### 🔴 Eng ko'p adashtiradigan narsa — TOKENLAR

Modullar **to'rt xil token** bilan ishlaydi va ular bir-birining o'rnini **bosmaydi**:

| Modul | Token | Ruxsat | Muddati |
|---|---|---|---|
| Izoh · DM | Instagram Login (OAuth bilan o'zi olinadi) | `instagram_business_*` | 60 kun, avtomatik yangilanadi |
| Reklama lidlari | Page Access Token | `leads_retrieval` | muddatsiz (System User) |
| Reklama statistikasi | System User tokeni | **`ads_read`** | muddatsiz |
| CAPI | Dataset (Events Manager) tokeni | `ads_management` | muddatsiz |

Almashtirib yuborilsa `OAuthException 190` yoki `#200` chiqadi va sababini topish qiyin.
Umumiy qadamlar: [`SOZLASH.md`](SOZLASH.md) → «QO'SHIMCHA MODULLAR → A-qadam».

⚠️ **Kontent joylash uchun akkauntni QAYTA ULASH shart** — yangi OAuth ruxsati
(`instagram_business_content_publish`) mavjud tokenga avtomatik qo'llanmaydi
([`SOZLASH.md`](SOZLASH.md) → B-qadam).

### Qayerdan boshlash

| Savol | Qayerga qarash |
|---|---|
| "Reklamaga qancha sarfladik, qaysi lid pul to'ladi?" | [`REKLAMA-STATISTIKASI.md`](REKLAMA-STATISTIKASI.md) |
| "Postni CRM'dan qanday joylayman?" | [`KONTENT.md`](KONTENT.md) |
| "Meta reklamani qanday yaxshiroq optimallashtiradi?" | [`CAPI.md`](CAPI.md) |
| "Nega post `2207052` bilan yiqilyapti?" | [`KONTENT.md`](KONTENT.md) → «Xato kodlari» |
| "Ochiq media papkasi xavfsizmi?" | [`../.claude/rules/uploads-security.md`](../.claude/rules/uploads-security.md) → «OCHIQ MEDIA» |
| "Kod yozayotganda nimaga tegmaslik kerak?" | [`../.claude/rules/marketing-instagram.md`](../.claude/rules/marketing-instagram.md) §17–§20 |

---

## 7. AI agentining yaxshilanishlari (E6.5 · E6.6 · AI caption)

Bular **yangi ekran ham, yangi bayroq ham qo'shmaydi** — mavjud modullarni kuchaytiradi.
Migratsiya: **`AddMarketingRagAndQuality`**.

| Nima | Muammo qanday edi | Endi |
|---|---|---|
| **Bilim bazasi RAG** (E6.5) | Butun bilim baza promptga tiqilar va **12000 belgida KESILARDI** — baza o'sganda oxirgi bo'laklar umuman tushmasdi va AI "bilmayman" derdi (nosozlik **jimgina** edi) | Har bo'lakning ma'no vektori saqlanadi, savolga **eng yaqin 6 bo'lak** tanlanadi. ⚠️ Vektor yo'q/xato bo'lsa **eski yo'lga qaytadi** — RAG modulni hech qachon to'xtatmaydi |
| **Javob sifati jurnali** (E6.6) | Operator AI javobini tuzatsa, bu **hech qayerda qolmasdi** — promptni yaxshilash uchun eng qimmatli ma'lumot yo'qolardi | «AI shunday dedi → operator shunday yozdi» juftligi saqlanadi va `GET /api/admin/instagram/quality` hisobotida ko'rinadi |
| **AI caption** (§5.10) | Post matnini har safar noldan yozish — SMM ishining eng ko'p vaqt oladigan qismi | Post modalidagi **«Matn yozdirish»**: mavzu → bilim bazasi asosida matn + hashtaglar ([`KONTENT.md`](KONTENT.md)) |

⚠️ **Yangi kutubxona QO'SHILMADI** (`pgvector` ham): vektor JSON matn sifatida saqlanadi,
kosinus oddiy C# hisobida — bilim bazasi o'nlab bo'lakdan iborat, bu o'lchanadigan yuk emas.

⚠️ **Yangi `.env` kaliti YO'Q:** embedding uchun ham o'sha `GEMINI_API_KEY` ishlatiladi.
Vektorlarni fon xizmati **har 60 soniyada** hisoblab boradi — qo'lda hech narsa qilish kerak emas.

🔴 **Javob sifati hisobotida mijozning hech qanday belgisi yo'q** — na ism, na telefon, na
mijoz yozgan matn. Faqat bizning ikki chiquvchi matnimiz va xodim ismi: bu **ichki sifat**
ma'lumoti, "kim bilan yozishilgani" savolining joyi — Inbox.

Texnik qoidalar: [`../.claude/rules/marketing-instagram.md`](../.claude/rules/marketing-instagram.md) §21
(RAG va sifat jurnali) hamda §18.8 (AI caption).
