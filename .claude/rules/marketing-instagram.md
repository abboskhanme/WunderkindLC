---
description: Marketing — Instagram AI sotuv agenti (webhook, OAuth, avtojavob, lidga aylantirish, inbox) va REKLAMA LIDLARI (Meta Lead Ads).
paths:
  - "IntellectCRM.Application/Services/Instagram*.cs"
  - "IntellectCRM.Application/Services/Meta*.cs"
  - "IntellectCRM.Server/Controllers/InstagramController.cs"
  - "IntellectCRM.Server/Controllers/InstagramWebhookController.cs"
  - "IntellectCRM.Client/src/pages/admin/marketing/**"
  - "IntellectCRM.Client/src/api/services/instagram.ts"
  - "instagram/*.md"
---

# Instagram AI agenti qoidalari

Migratsiya: `AddInstagramAgent`. Modul markazning Instagram **Professional** akkauntiga kelgan
**izohlar** va **DM**larga AI bilan javob beradi va qiziqqan odamni **lidga** aylantiradi.
Bo'lim: **Marketing** (`/admin/marketing`), ruxsat kaliti — **`marketing`**.

Yo'l: **Instagram API with Instagram Login**. Facebook Page, Business Verification va
**App Review — KERAK EMAS** (Standard Access; akkaunt bizniki). Protokol tafsilotlari:
`instagram/TEXNIK.md`, sozlash: `instagram/SOZLASH.md`.

## 1. YAGONA QOIDA: webhook HECH QACHON og'ir ish qilmaydi

**Meta 5 soniya kutadi.** LLM chaqiruvi undan uzoq — kechiksa Meta yetkazishni
muvaffaqiyatsiz deb hisoblaydi va takroriy kechikishda webhookni **o'chirib qo'yadi**.
Shuning uchun **javob va ish AJRATILGAN**:

```
POST /api/public/instagram/webhook
   1) xom body BAYT sifatida o'qiladi (EnableBuffering)
   2) InstagramSignature.Verify  → mos kelmasa 403, body ishlanmaydi
   3) IgWebhookEvent (Status="pending") yoziladi     ← DURABLE navbat, baza jadvali
   4) ══ DARHOL 200 OK ══
                    ↓ har 2 soniyada
InstagramWorkerService → InstagramPipeline.ProcessAsync   ← BUTUN og'ir ish shu yerda
```

⚠️ Controllerga AI chaqiruvi, Graph API so'rovi yoki uzun DB ishi **QO'SHILMAYDI**.
⚠️ Fire-and-forget (`_ = Task.Run(...)`) ham **YARAMAYDI** — nusxa qayta ishga tushsa hodisa
yo'qolardi. Navbat ATAYIN bazada (kesh emas): Inbox suhbat tarixini ko'rsatadi va
restartdan keyin dedup/navbat saqlanib qolishi kerak.

## 2. IMZO TEKSHIRUVI — FAIL-CLOSED

`InstagramSignature.Verify(byte[] rawBody, string? header, string appSecret)`:

| Qoida | Qiymat |
|---|---|
| Nimadan hisoblanadi | **XOM BODY BAYTLARIDAN** — qayta seriyalash/formatlash/trim YO'Q |
| Kalit | `AppSecrets.InstagramAppSecret` (`.env`) |
| Algoritm | HMAC-SHA256, kichik harfli hex, header `sha256=…` |
| Solishtirish | **doimiy vaqtli** (`CryptographicOperations.FixedTimeEquals`), `==` EMAS |
| **App Secret bo'sh** | **`false`** — so'rov RAD ETILADI |

⚠️ **FAIL-OPEN TAQIQLANADI.** Manba loyihada secret bo'sh bo'lsa tekshiruv o'tkazib
yuborilardi ("lokal test qulay bo'lsin") — prodda bu istalgan odam bizning nomimizdan
hodisa yubora oladigan **himoyasiz endpoint** degani. `InstagramSignatureTests` shu xulqni
test bilan qulflaydi — testni "qulaylik uchun" yumshatmang.

### 2.1. «IMZO MOS KELMADI» — SABAB LOGDA NOMLANADI

Rad etish sababi `InstagramSignature.DescribeFailure` (sof funksiya, testlangan) orqali logga
yoziladi. Sabab: bu xatoning kamida olti xil manbai bor va ular **butunlay boshqa** ishni
talab qiladi.

| Logdagi sabab | Nima qilish kerak |
|---|---|
| `sarlavhasi YO'Q — so'rov Meta'dan kelmagan` | **Hech narsa.** Manzil ochiq, skanerlar POST qiladi — 403 to'g'ri javob |
| `App Secret sozlanmagan — fail-closed` | `.env` **va** `docker-compose.yml` `environment:` (§7 dagi tuzoq) |
| `imzo uzunligi noto'g'ri` / `hex formatda emas` | Buzuq/soxta so'rov — Meta bunday yubormaydi |
| `imzo BOSHQA App Secret bilan qilingan` | **Avval kalitni tekshiring:** «Instagram → API setup with Instagram login» dagi *Instagram app secret* kerak — «App settings → Basic» dagi *Facebook app secret* BOSHQA qiymat va webhook u bilan imzolanmaydi. Kalit to'g'ri bo'lsa: manzilga **boshqa ilova** obuna (token qaysi ilovadan olingan bo'lsa, `SubscribeWebhookAsync` obunani o'shanga qo'yadi) |
| `imzo META (Page) kaliti bilan MOS KELDI` | Page hodisasi `/webhook` ga kelyapti — konsolda uni `/leadgen` ga yo'naltiring |

⚠️ **Sabab aniqlanishi QABUL QILISH degani EMAS.** `DescribeFailure` ikkinchi kalitni faqat
*tashxis* uchun solishtiradi; `Verify` baribir `false` qaytaradi va so'rov 403 bo'ladi.
`InstagramSignatureTests` shuni alohida qulflaydi — "sabab topildi, demak o'tkazamiz" degan
"tuzatish" kelajakda kiritilmasin.

⚠️ **Ikkala kalit BIR XIL bo'lishi mumkin:** `META_APP_SECRET` bo'sh bo'lsa `MetaAppSecret`
Instagram kalitiga qaytadi. O'shanda "Page hodisasi" degan xulosa yolg'on bo'lardi — shuning
uchun tekshiruv kalitlar farq qilgandagina bajariladi.

⚠️ **Logda payloadning `object` turi ham bor** (`obyekt: instagram (izoh/DM)` /
`page (…)`, `InstagramEventParser.ObjectType`) — rad etilgan so'rov qayta ishlanmaydi, ya'ni
"qaysi mahsulot yuboryapti" savoli boshqa hech qayerdan javob topmasdi. Qiymat **oq
ro'yxatdan** olinadi: uni tashqaridagi yuboruvchi belgilaydi va xom holicha yozilsa soxta log
qatorlarini "yozib" qo'yardi (User-Agent bilan bir xil mulohaza).

⚠️ Logga **kalit ham, imzo qiymati ham, body ham** yozilmaydi (§7). User-Agent yoziladi, lekin
80 belgiga qisqartirilib, yangi qatorlar olib tashlanadi (log injection).

⚠️ Body **buferlanmasa** imzo HECH QACHON mos kelmaydi: framework deserializatsiya qilib
bo'lgan obyektni qayta seriyalasak bo'sh joylar va kalit tartibi o'zgaradi. Bu — eng ko'p
uchraydigan xato.

## 3. MODUL O'CHIQ BO'LSA — TASHQARIGA HECH NARSA KETMAYDI

Barcha bayroqlar **default `false`** (entity default'i ham, migratsiya default'i ham —
`books.md` §4 dagi saboq): `InstagramEnabled`, `InstagramAutoReplyComments`,
`InstagramAutoReplyDm`, `InstagramPrivateReplyEnabled`.

- `CenterMeta.InstagramEnabled == false` → `InstagramWorkerService` navbatni umuman
  qayta ishlamaydi, ya'ni **Graph API'ga ham, Gemini'ga ham** so'rov ketmaydi.
- Webhook baribir hodisani **qabul qilib navbatga yozadi** (Meta obunani o'chirib
  qo'ymasin) — u yerda turadi va 30 kunda tozalanadi.

⚠️ Yangi tashqi chaqiruv qo'shsangiz — u ham shu darvozadan o'tsin. "Kichkina bitta so'rov"
sozlanmagan markazda kutilmagan xabar yuborilishiga olib keladi.

## 4. CHEKSIZ HALQADAN HIMOYA — 4 QAVAT, BIRI HAM OLIB TASHLANMAYDI

**Real hodisa:** bot izohga javob yozadi → o'z javobi webhook bo'lib qaytadi → begona izoh
deb hisoblaydi → yana javob yozadi → **cheksiz halqa** → akkaunt spam sifatida bloklanadi.

| Qavat | Mexanizm | Kod |
|---|---|---|
| 1. Identifikatsiya | `from.id` **uchala** qiymat bilan solishtiriladi: `IgAccount.IgUserId`, app-scoped `IgAccount.AppScopedUserId`, `entry.id` + zaxira `username` (registr e'tiborsiz) | `InstagramEventParser.IsOurs` |
| 2. Dedup | bir xil `EventKey` ikkinchi marta ishlanmaydi + `IgMessages` dagi `mid`/`comment_id` | `AlreadyHandledAsync` |
| 3. Avtomat o'chirgich | post bo'yicha **8/10daq** · global **30/10daq** | `InstagramContract.BurstBlockReason` |
| 4. Kunlik chegara | `InstagramDailyReplyLimit` (default 200) | pipeline §4 |

⚠️ **3-qavat 4-qavatning o'rnini BOSMAYDI va aksincha.** Kunlik chegara uzoq muddatli to'siq:
halqa daqiqalar ichida yuzlab javob yozadi va Instagram akkauntni 200 ga yetmasdan spam deb
belgilaydi. Qisqa oynali chegaralar esa odam tezligidan yuqori, halqa tezligidan past qilib
tanlangan. (2026-08-19 gacha 3-qavat hujjatda VA'DA QILINGAN, lekin kodda YO'Q edi.)

⚠️ **Uchala identifikator ham saqlanadi**: `IgAccount.IgUserId` (`me.user_id`),
`IgAccount.AppScopedUserId` (`me.id`, migratsiya `AddIgAccountAppScopedId`) va `Username`.
Webhook'da `from.id` **ba'zan** biri, **ba'zan** ikkinchisi bo'lib keladi. Bittasiga tayanish —
yuqoridagi halqaning aynan sababi. Eski ulangan akkauntda `AppScopedUserId` bo'sh bo'ladi —
himoya qolgan qiymatlar bilan ishlaydi, akkauntni **qayta ulash** uni to'ldiradi.

DM tomonida ekvivalenti: `message.is_echo == true` bo'lsa **hech qachon** javob yozilmaydi.

## 5. DEDUP KALITI DETERMINISTIK BO'LISHI SHART

| Hodisa | `EventKey` |
|---|---|
| Izoh | `comment:{comment_id}` |
| DM | `dm:{message.mid}` |
| Echo | `echo:{message.mid}` |
| Ikkalasi yo'q | `sender + timestamp + matn` ning **barqaror kriptografik hash**i |

⚠️ **`GetHashCode()`, `Random`, `Guid` yoki jarayonga bog'liq har qanday qiymat
TAQIQLANADI.** Manba loyihada DM kaliti runtime hash'dan qurilar — restartdan keyin kalit
o'zgarardi va dedup **umuman ishlamasdi**. `InstagramEventParserTests` bir xil payload
ikki marta parse qilinganda bir xil kalit chiqishini tekshiradi.

⚠️ **Dedup IKKI QATLAM** (`InstagramWebhookController.EnqueueAsync` — ikkala marshrut ham
shundan o'tadi): avval "kalit bazada bormi" deb qaraladi, so'ng INSERT qilinadi. Tez yo'l
KAFOLAT EMAS — bir vaqtda kelgan ikki bir xil webhook undan ikkalasi ham o'tishi mumkin va
takrorni FAQAT unikal indeks to'xtatadi.

🔴 Tez yo'l **LOG uchun** qo'shilgan (2026-08-23, prodda o'lchangan): rad etilgan INSERT uchun
EF Core `fail:` darajasida **~60 qatorlik stack trace** yozadi va Meta'ning bir nechta takroriy
hodisasi logni butunlay to'ldirib, haqiqiy ogohlantirishlarni ko'rinmas qilgan edi. EF log
darajasini tushirish bilan yechib BO'LMAYDI — haqiqiy baza xatolari ham yashirinardi.

`IgWebhookEvent.EventKey` — **UNIKAL indeks**: bir vaqtda kelgan ikki bir xil webhook ham
to'g'ri filtrlanadi (ikkinchisi `skipped`).

**Nega majburiy:** Meta muvaffaqiyatsiz yetkazishni **36 soat** qayta yuboradi va kafolat
"at-least-once" — dedupsiz mijoz bir savolga bir necha xil javob olardi.

## 6. LID — `LeadIntake` orqali, FIRST-TOUCH qoidasi bilan

Instagram lidi **mavjud `Lead` moduliga** tushadi (`InstagramLeadBridge.UpsertAsync`),
`Source = CenterMeta.InstagramLeadSource` (default `"Instagram"`).

⚠️ **`ContactRequest` EMAS:** "Bog'lanish kerak" — mavjud O'QUVCHI bilan bog'lanish navbati;
Instagram'dan **yangi mijoz** keladi, u lidlar voronkasiga tushadi (kanban, bosqichlar,
konversiya allaqachon bor). Parallel `InstagramLead` jadvali ham **yaratilmaydi**.

| Qoida | Tafsilot |
|---|---|
| Dublikat | telefon bo'lsa `LeadIntake.FindByPhoneAsync` orqali qidiriladi |
| `conv.LeadId` bor | **yangi lid yaratilmaydi** — mavjudi yangilanadi, `RepeatCount++`, `LeadEvent` yoziladi |
| **`Lead.Source` va `Lead.Stage`** | mavjud lidda **O'ZGARMAYDI** (first-touch: birinchi murojaat manbasi saqlanadi) |
| `Lead.PhoneKey` | **qo'lda yozilmaydi** — `AppDbContext.SaveChanges` o'zi to'ldiradi (`crm-leads.md`) |
| Telefonsiz qaynoq lid | baribir yoziladi: `FullName = "@username (Instagram)"` |
| Har suhbat lid bo'lmaydi | `InstagramContract.ShouldCreateLead` = `IsHot || kontakt bor`. Salom-alik va spam CRM'ni ifloslantirmaydi |

### 6.1. 🔴 TELEGRAM GURUHIGA KARTA — Instagram YAGONA ISTISNO edi

Lid yaratilgach `LeadNotifier.NotifyNewLeadAsync` chaqiriladi (guruh + superadmin shaxsiy chati).
Ilgari bu yerda FAQAT `SyncCardAsync` turardi, u esa o'z qoidasi bo'yicha **kartasi YO'Q lidga
hech narsa yubormaydi** — ya'ni Instagram lidi CRM'da paydo bo'lar, Telegram guruhiga esa
**umuman tushmasdi**. Qolgan barcha kanallar (lid formasi, daraja testi, reklama lidi §16, qo'lda
kiritish) `NotifyNewLeadAsync` ni chaqiradi; nosozlik jimgina edi.

| Holat | Nima yuboriladi |
|---|---|
| Suhbat lidga **BIRINCHI** marta bog'landi + lid YANGI | to'liq karta (guruh + superadmin) |
| Suhbat lidga **BIRINCHI** marta bog'landi, lekin lid MAVJUD (masalan formadan kelgan odam endi Instagram'da yozdi) | mavjud karta tahrirlanadi + bitta qatorli signal |
| O'sha suhbatning **keyingi** xabarlari | karta JIMGINA yangilanadi (`SyncCardAsync`) |

⚠️ **Karta suhbat lidga BIRINCHI bog'langanda yuboriladi, har xabarda EMAS.** Sabab:
`ShouldCreateLead` qaynoq suhbatda HAR xabarda rost bo'ladi (telefon allaqachon berilgan), ya'ni
har «rahmat» ga ham guruhga signal ketardi va karta shovqinga aylanardi. Bayroq `UpsertAsync` dan
**OLDIN** o'qiladi (`conv.LeadId` bo'shmi) — u chaqiruv ichida to'ldirib qo'yiladi.

⚠️ Darvoza — `CenterMeta.InstagramNotifyTelegram` (reklama lidlaridagi bilan bir xil). Toggle
izohi shu sababdan yangilangan: u endi faqat "admin ogohlantirishi" emas, guruhdagi lid kartasini
ham boshqaradi.

⚠️ Inbox'dagi **«Lidga aylantirish»** tugmasi (`POST conversations/{id}/create-lead`) AYNAN shu
qoidadan o'tadi — operator qo'lda bosgani ham lidning tug'ilishi.

⚠️ Izoh va DM'da ID formatlari farq qilishi mumkin — lid dedup **faqat** `IgUserId` ga
tayanmasin (telefon + `conv.LeadId` birga ishlaydi), aks holda bir mijoz uchun **ikki lid**
paydo bo'ladi.

## 7. TOKEN VA MAXFIY QIYMATLAR

| Qiymat | Qayerda | Nega |
|---|---|---|
| `INSTAGRAM_APP_SECRET` | **`.env`** (`AppSecrets`) | maxfiy — baza dump'i Telegram'ga yuboriladi |
| `INSTAGRAM_VERIFY_TOKEN` | **`.env`** (`AppSecrets`) | xuddi shunday |
| `IgAccount.AccessToken` | **BAZADA** | ATAYIN chekinish: token OAuth orqali ISH VAQTIDA olinadi, `.env` ga yozib bo'lmaydi |
| `CenterMeta.InstagramAppId` va qolgan 10 sozlama | **BAZADA** (UI'dan) | maxfiy emas |

⚠️ **Token/secret HECH QACHON javobga, DTO'ga, auditga yoki LOGGA tushmaydi.**

⚠️ **LOG orqali sizib chiqish — 2026-08-19 da topilgan REAL hodisa.** `AddHttpClient` .NET'ning
standart HTTP loggerini yoqadi va u so'rovning **to'liq manzilini** `Information` darajasida
yozadi. Bizda esa token MANZIL ICHIDA keladi: Telegram (`api.telegram.org/bot<TOKEN>/…`) va
Instagram Graph (`?access_token=…`). Natijada konteyner loglarida bot tokeni **ochiq** turardi
(102 marta). Tuzatish — `appsettings.json` da `"System.Net.Http.HttpClient": "Warning"`;
`SecretLeakAndPublicPageTests` buni qulflaydi.

⚠️ Izohni `Logging:LogLevel` ICHIGA yozib bo'lmaydi — u yerdagi har bir qiymat `LogLevel` enum
sifatida o'qiladi va ilova startupda yiqiladi.

`GET /status`
faqat holat qaytaradi: `appIdSet`, `appSecretSet`, `verifyTokenSet`, `tokenDaysLeft`,
`connected`. Qiymatning o'zi emas.

⚠️ **`.env` ga yozish YETARLI EMAS** (2026-08-14 da aynan shu tufayli modul ulanmagan edi):
prod `app` servisida `env_file` YO'Q, shuning uchun kalit `docker-compose.yml` dagi
`environment:` ro'yxatiga ham qo'yilishi SHART (`Instagram__AppSecret` / `Instagram__VerifyToken`).
Bo'lmasa `AppSecrets` bo'sh qiymat o'qiydi va webhook fail-closed bo'lib **403** qaytaradi —
tashqaridan bu "Meta tasdiqlamayapti" bo'lib ko'rinadi. `EnvKeysWiringTests` shuni qulflaydi.

⚠️ **Meta konsolida webhook maydoni ham «Callback URL» deb ataladi** — u yerga
`…/api/public/instagram/**webhook**` qo'yiladi. `…/callback` — bu OAuth qaytish manzili, faqat
"Valid OAuth Redirect URIs" uchun; webhook maydoniga qo'yilsa Meta 302 oladi va tasdiqlamaydi.

### 7.1. AKKAUNTNI ULASHNING IKKI YO'LI

| Yo'l | Endpoint | Qachon |
|---|---|---|
| **OAuth (asosiy)** | `GET /connect-url` → `GET /api/public/instagram/callback` | odatda har doim |
| **Qo'lda token (zaxira)** | `POST /connect-token` (`marketing.settings`) | OAuth yurmasa: `Invalid redirect_uri`, domen hali ochilmagan, yoki token Meta konsolidagi «Generate token» dan olingan |

⚠️ **Ikkalasi ham AYNAN bir xil ketma-ketlikni bajaradi** — `me` → uzoq muddatliga aylantirish →
webhook obunasi → eski akkauntni arxivlash (tokenini tozalash bilan) → yangi `IgAccount`.
Qo'lda kiritilgan token **ishonch bilan saqlanmaydi**: `MeAsync` yiqilsa hech narsa yozilmaydi.

⚠️ **NEGA token yolg'iz yoziladigan maydon EMAS.** Halqa himoyasi (§4) **uchala**
identifikatorga tayanadi (`IgUserId`, `AppScopedUserId`, `Username`), ularni esa faqat `me`
qaytaradi. Admin token yozadigan "oddiy sozlama maydoni" qo'shilsa, bu qiymatlar bo'sh qolib
bot o'z xabariga javob yoza boshlardi — shuning uchun token har doim `me` orqali o'tadi.

⚠️ **Muddat noma'lum qolishi MUMKIN:** `ig_refresh_token` faqat uzoq muddatli tokenda ishlaydi.
Yiqilsa token baribir saqlanadi (u ishlashi `me` bilan tasdiqlangan), `TokenExpiresAt` esa bo'sh
qoladi — `RefreshTokensAsync` "muddat noma'lum" holatini ham yangilaydi, ya'ni o'zi to'g'rilaydi.

⚠️ **Yangi ulash yo'li qo'shsangiz** — u ham shu ketma-ketlikdan o'tsin va eski faol akkauntning
tokenini TOZALASIN (uzilgan akkaunt tokeni bazada qolib ketmasin).

**Token hayoti:** uzoq token ~60 kun; `InstagramWorkerService` kuniga bir marta tekshiradi
va muddatiga **< 15 kun** qolganda (`IgConst.TokenRefreshDays = 45`) yangilaydi.
Muvaffaqiyatsiz bo'lsa — **Telegram alert**, jim yiqilmaydi.

🔴 **Token yangilash darvozasi — `InstagramContract.NeedsLoginToken`, `InstagramEnabled` YOLG'IZ
EMAS.** Instagram Login tokenini **ikkita** modul ishlatadi: izoh/DM agenti (`InstagramEnabled`)
va **kontent joylash** (`InstagramPublishEnabled`). Ilgari worker'da yalang
`if (!meta.InstagramEnabled) return;` turardi — faqat kontent modulini yoqqan markazda token
60 kunda **jimgina o'lardi** va post birdan `OAuthException 190` bilan yiqila boshlardi, sabab
esa butunlay boshqa joydagi bayroq edi.

⚠️ **Yangi modul qo'shsangiz** — u qaysi tokenni ishlatishini aniqlang. Instagram Login tokeni
bo'lsa `NeedsLoginToken` ga qo'shing; Page / System User / Dataset tokeni bo'lsa qo'shmang
(ular muddatsiz va bu yerda yangilanmaydi).

🔧 **TASHQI MANZIL — `App:Host` (env `APP_HOST`) dan, so'rov hostidan EMAS.**
`InstagramWebhookController.PublicBase` → `InstagramContract.PublicBase` (sof funksiya,
testlangan). Webhook manzili, OAuth `redirect_uri` va Sozlamalar sahifasidagi "nusxa olish"
tugmalari **bitta** manbadan quriladi. Sabab: `redirect_uri` Meta'dagi bilan **harfma-harf**
bir xil bo'lishi shart (§11 tuzoq 10), admin esa CRM'ni boshqa nom (IP, `www.`, vaqtinchalik
domen) bilan ochgan bo'lishi mumkin edi. `APP_HOST` bo'sh bo'lsa — eskicha, so'rov hostidan.

## 8. RUXSAT — `marketing`

`InstagramController`: sinf darajasida `[AdminPerm("marketing", ReadRequiresPerm = true)]` (O'QISH),
yozish esa SAHIFA kaliti bilan metod darajasida: `marketing.settings` · `marketing.inbox` ·
`marketing.rules` · `marketing.knowledge` (qarang: `.claude/rules/permissions.md` §4.1).

⚠️ `ReadRequiresPerm = true` ATAYIN: javobda mijoz suhbatlari, ismlari va **telefonlari**
bor. `AdminPermAttribute` da GET odatda har qanday xodimga ochiq (bo'limlararo o'qish
uchun), bu yerda bo'lmaydi (`uploads-security.md` dagi `ContractsController`/`CareerController`
bilan bir xil mantiq).

`InstagramWebhookController` — **`[AllowAnonymous]`**, `api/public/instagram` yo'lida.
⚠️ `[AllowAnonymous]` **hech qachon** `api/admin/...` yo'lida turmasin. Uchta marshrut
(`GET /webhook`, `POST /webhook`, `GET /callback`) himoyalanadi: webhook — HMAC imzo,
callback — bir martalik `IgOAuthState` (15 daqiqa).

⚠️ **`POST /simulate` va `POST /test-agent` — ADMIN tomonda, `marketing` ruxsati ostida.**
Manba loyihada diagnostika endpointlari **autentifikatsiyasiz** edi: tashqi odam bizning
nomimizdan xabar yubortirishi va LLM tokenimizni yeyishi mumkin edi.

Yozish amallari `can('marketing','edit')` bilan darvozalangan. `constants.ts` O'ZGARMAYDI —
`marketing` kaliti allaqachon bor, yangi kalit qo'shilmaydi.

## 9. AUDIT — `EntityType = "Instagram"`

`AuditSections.ByEntityType` da `"Instagram" → "marketing"` ("Marketing" bo'limi).
Yoziladi: akkauntni **ulash/uzish**, sozlamalarni o'zgartirish, qoida yaratish/tahrir/
o'chirish, bilim bazasini saqlash, **operator javobi**, qo'lda lidga aylantirish.

⚠️ Token va App Secret **auditga yozilmaydi** (`audit.md` §1).
⚠️ Yangi `audit.Record` qo'shsangiz — savol: *"bu amaldan keyin tarixda BITTA qator paydo
bo'ladimi — har doim?"* (`audit.md` §3.5).

**ATAYIN yozilmaydi:** botning avtomatik javoblari — ular `IgMessage` sifatida suhbat
lentasida allaqachon ko'rinadi va har javobni auditga yozish tarixni ko'mib tashlardi.

## 10. AI — narx O'YLAB TOPILMAYDI

`InstagramAgentService` mavjud **`GeminiService`** dan foydalanadi (yangi provayder = yangi
kalit = yangi billing). Model — `CenterMeta.InstagramAiModel`, bo'sh bo'lsa loyiha default'i.

| Qoida | Tafsilot |
|---|---|
| **Faqat bilim bazasidan** | narx/shart bilim bazasida yo'q bo'lsa taxmin qilinmaydi: `EscalateToHuman = true` |
| **Til va yozuv** | mijoz qaysi tilda va **yozuvda** yozsa AYNAN o'shanda javob (kirill/lotin/rus/ingliz) |
| **Spamga qarshi xilma-xillik** | bir xil shablon takrorlansa Instagram uni **spam** deb belgilaydi |
| **Operatorga o'tish** | "operator"/"odam" so'ralsa darhol eskalatsiya; majburlash **taqiqlangan** (platforma talabi) |
| **Bot oshkorligi** | birinchi xabarga `CenterMeta.InstagramGreeting` qo'shiladi (Meta talabi) |
| Uzunlik | ochiq izohga 1–2 gap; DM'da batafsilroq + telefon so'rash |

⚠️ Enum qiymatlari **bir joyda** — `IgConst.Intents` / `IgConst.Languages`. Manba loyihada
mock `price_inquiry`, sxemada `price_question` edi — mos kelmagani sezilmay qolgan.

⚠️ Strukturali chiqish sxemasida `minLength`/`minimum`/`maximum` **ISHLATILMAYDI** (sxema
rad etiladi). Diapazon kod tomonda: `ClampScore` (0..100), `NormalizeIntent` (→ `other`),
`NormalizeLanguage` (→ `uz-Latn`).

⚠️ AI javob bermasa (`ParseOutput` → `null`) pipeline **jonli javob YUBORMAYDI**. "Xato
bo'lsa umumiy matn yozib qo'yaylik" — **qilinmaydi**: mijozga mazmunsiz javob yuborishdan
ko'ra operatorga signal berish yaxshiroq.

## 11. TUZOQLAR (kod yozayotganda)

| # | Tuzoq | Qoida |
|---|---|---|
| 1 | `hub.mode` da **nuqta** bor — model binding ololmaydi | `Request.Query["hub.mode"]` bilan **qo'lda** o'qiladi; javob **`text/plain`** xom challenge (JSON emas) |
| 2 | DM hodisasi `changes[]` da EMAS | `entry[].messaging[]`; ikkala massiv bitta `entry` ichida bo'lishi mumkin — parser **ikkalasini** ko'radi |
| 3 | **24 soat oynasi** | `DmWindowOpen` yuborishdan OLDIN. Yopiq bo'lsa so'rov ketmaydi, `NeedsOperator = true` + sabab. ⚠️ **Operatorda oyna UZUNROQ:** `HumanAgentWindowOpen` — 7 kun, Meta'ning `messaging_type=MESSAGE_TAG` · `tag=HUMAN_AGENT` tegi bilan (`SendDmAsync(..., humanAgent: true)`). 🔴 Teg **BOTGA berilmaydi** — avtomatik javobni "odam yozdi" deb belgilash Meta siyosatini buzadi va akkauntga cheklov keltiradi |
| 4 | **Private reply — 7 kun, BIR MARTA** | takroriy yuborish xato beradi; yuborilgani `IgMessage` (`Channel="private_reply"`) sifatida yoziladi |
| 5 | **Echo = operator pauzasi** | `is_echo` — javob berish uchun emas, botni **jim qildirish** uchun. Iz topilmasa → odam yozgan → `OperatorPausedUntil` (`IgConst.OperatorPauseMinutes` = **720 daqiqa = 12 soat**, yagona manba). Muddat tugasa bot O'ZI qaytadi; darhol qaytarish — «Botga qaytarish» tugmasi |
| 6 | Webhook'da DM'da **username YO'Q** | faqat `sender.id`. Username `InstagramApi.GetUserProfileAsync` (`GET /{igsid}?fields=name,username`) bilan olinadi — pipeline'da **suhbatga bir marta**, natija `IgConversation.Username` da saqlanadi. Darvoza `InstagramEnabled` (§3), xato **jim yutiladi** (username — qulaylik, xabar emas). Usiz Inbox'da DM suhbati `@1784140…` degan RAQAM bo'lib turardi |
| 7 | Matnsiz xabar (rasm/stiker/ovoz) | jimgina tashlanmaydi — `NeedsOperator = true` |
| 8 | `mentions`, `live_comments` | ishlanmaydi, lekin **jimgina yo'qolmaydi**: `InstagramEventParser.UnsupportedFields` maydon NOMINI qaytaradi, u navbat yozuvining `Error` ustuniga va `Warning` logiga tushadi. Umumiy "hodisa topilmadi" matni "kelyapti, lekin hech narsa bo'lmayapti" savoliga javob bermasdi |
| 9 | `graph.facebook.com` | **YO'Q** — `IgConst.GraphBase` (`graph.instagram.com/v23.0`). Xom satr yozmang |
| 10 | `redirect_uri` | Meta'dagi bilan **harfma-harf** bir xil, oxirida `/` yo'q; `[2]` va `[4]` da ham bir xil |
| 11 | Kod javobi `data[]` massivida | `ExchangeCodeAsync` parseri — obyekt emas, massiv |
| 12 | `DateTime.Now` | **TAQIQLANGAN** — `AppClock.Now` / `AppClock.Iso()` |
| 14 | **24 soatlik oyna MIJOZ yozgan vaqtdan** | `IgIncomingEvent.SentAtIso` (Meta bergan vaqt), qayta ishlangan vaqtdan EMAS — navbat kechiksa oyna "ochiq" ko'rinib, Instagram javobni rad etardi. Vaqt mantiqsiz bo'lsa (kelajak / 30 kundan eski) joriy vaqtga qaytiladi |
| 15 | **Javob kechikishi navbatda kutgan vaqtni HISOBGA oladi** | aks holda ketma-ket siklda 10 hodisa × 5 soniya = bitta tsiklga 50+ soniya qo'shilardi |
| 16 | Chiquvchi xabarga **`MediaId` yoziladi** | halqa avtomat o'chirgichi "shu post ostida nechta javob" ni AYNAN shu ustundan sanaydi |
| 13 | Telegram bildirishnomasi | xatosi **jim yutiladi** (`LeadNotifier`/`BookSalesService` bilan bir xil siyosat) — bildirishnoma javobni buzmasin |

### 11.1. 🔴 AI PROMPTI — «oxirgi xabar» va IZOH TARIXI

**Muammo A — mavzuga yopishib qolish.** Model suhbatning BOSHIDAGI qiziqishni sotishda davom
etardi: odam avval IELTS so'rab, keyin bolalar kursini so'rasa ham javob yana IELTS haqida
chiqardi. Sabab tuzilmada: tarix promptning katta qismi, oxirgi xabar esa bitta qator, ustiga
tizim ko'rsatmasi «qiziqqan odamni bog'lanishga olib kel» deydi. Yechim — **1-qoida**
(«JAVOB — MIJOZNING OXIRGI XABARIGA, tarix FAQAT kontekst, mavzu o'zgargan bo'lsa eskisiga
QAYTARMA») va `BuildContext` da oxirgi xabarning ochiq nomlanishi
(`[JAVOB YOZILADIGAN XABAR …]`).

**Muammo B — izohga DM tarixi berilardi.** `InstagramPipeline.LoadHistoryAsync` endi kanalga
qarab ikki xil ishlaydi:

| Kanal | Tarix |
|---|---|
| DM | butun suhbatning oxirgi `DmHistoryLimit` (20) xabari — avvalgidek |
| **Izoh** | **FAQAT o'sha post ostidagi** `comment` + `private_reply` qatorlari, `CommentHistoryLimit` (6) ta |

🔴 Ikki zarar bor edi: (1) boshqa post ostidagi eski savol yoki DM suhbati modelni ostidagi
izohdan chalg'itardi; (2) **shaxsiy yozishma OMMAGA chiqardi** — javob ochiq izoh sifatida chop
etiladi, promptdagi DM tarixi (telefon, to'lov haqidagi gap) javobga kirib qolsa uni post ostida
hamma o'qirdi. Chegara **tuzilma darajasida**: DM qatorlari so'rovga umuman olinmaydi.

⚠️ `MediaId` bo'sh bo'lsa (eski yozuv / Meta post id bermagan) post bo'yicha ajratib bo'lmaydi —
u holda hech bo'lmaganda **KANAL** bo'yicha filtrlanadi. Bo'sh tarix zarar qilmaydi (post matni
va bilim bazasi joyida), aralashgan tarix esa ikkala zararni ham qaytarardi.

⚠️ Yopiq javob (`private_reply`) izoh tarixida **QOLADI** — u o'sha izohning davomi.

⚠️ **Kalit so'z qoidalari (`IgAutoRules`) AI'dan OLDIN ishlaydi** va aniq faktlar (narx, manzil,
jadval) uchun aynan shu yo'l tavsiya etiladi: `StopAi` bilan javob qat'iy bo'ladi. Lekin ular
AI'ning O'RNINI bosa olmaydi — bir xil shablon takrorlansa Instagram uni spam deb belgilaydi
(§10) va ro'yxatda yo'q savol javobsiz qoladi.

**Xatolarga chidamlilik:** har bosqich alohida `try/catch`. Yordamchi tizim yiqilsa
(dedup, tarix, lid, Telegram) — **asosiy vazifa, mijozga javob berish, baribir bajariladi**.
Yagona istisno: **AI javobi** yiqilsa oqim to'xtaydi (yuboradigan narsa yo'q).

## 12. NIMA ATAYIN QILINMAGAN

| Narsa | Nega |
|---|---|
| **Alohida mikroservis / konteyner / Redis** | Instagram — bitta ASP.NET Core ilova ichidagi **modul**. Yangi konteyner deploy va zaxirani murakkablashtirardi; holat (navbat, dedup, tarix, pauza) mavjud bazada |
| **Alohida Telegram bot** | mavjud `TelegramService` ishlatiladi (kitob buyurtmasi bildirishnomasi bilan bir xil naqsh) |
| **Yangi ruxsat kaliti** (`instagram`) | `marketing` allaqachon bor |
| **Parallel lead/lead_events jadvallari** | mavjud `Lead` + `LeadEvent` (§6) |
| **Yangi lid bosqichi lug'ati** | mavjud bosqichlar ishlatiladi (`crm-leads.md`) |
| **Moliya bilan bog'liqlik** | modul `FinanceTransaction`ga yozmaydi va balansga tegmaydi (`books.md` §7 bilan bir xil sabab) |
| **Bir necha akkaunt UI'si** | hozircha bitta akkaunt; jadval ko'p qatorga tayyor (kelajakda filial) |
| **Instagram'dan birinchi bo'lib yozish** | platforma ruxsat bermaydi (24 soat oynasi) |
| **Xotiradagi kunlik statistika** | restartda nolga tushardi — analitika **bazadan** (`GET /analytics`) |

## 13. Frontend

**Marketing bo'limi FAQAT Instagram bo'ladi** — eski mock sahifalar (`MarketingDashboard`,
`MarketingInbox`, `MarketingRules`, `MarketingChannels`, `MarketingAi`, `MarketingAnalytics`)
o'chirilgan, `mk.tsx` qoladi (mock ma'lumotsiz, `ChannelId` faqat `'instagram'`).

| Sahifa | Yo'l |
|---|---|
| Boshqaruv paneli | `/admin/marketing` |
| Inbox | `/admin/marketing/inbox` |
| Javob qoidalari | `/admin/marketing/rules` |
| Bilim bazasi | `/admin/marketing/knowledge` |
| Analitika | `/admin/marketing/analytics` |
| Reklama lidlari | `/admin/marketing/reklama-lidlari` |
| Reklama statistikasi | `/admin/marketing/reklama-statistikasi` |
| **Kontent** (Navbat · Joylanganlar · Holat · Composer) | `/admin/marketing/kontent[/joylangan\|/holat\|/yangi\|/post/:id]` — tuzilishi **§18.11** |
| Javob sifati | `/admin/marketing/javob-sifati` |
| Sozlamalar | `/admin/marketing/settings` |

Holat boshqaruvi — `useState`/`useEffect` + axios (loyiha uslubi, TanStack Query YO'Q).

⚠️ **Sahifalar TO'LIQ KENGLIKDA:** `MarketingPage` dan `full` propi olib tashlangan,
`content-narrow` o'chirilgan. Sub-sahifalar (Kontent) va tablar (Sozlamalar, Reklama
statistikasi — `?bolim=…`) **sidebar nav'da EMAS, sahifa ICHIDA**: chap menyu bo'limlar uchun,
sub-sahifa esa bo'lim ICHIDAGI qadam. Batafsil — §18.11.

⚠️ **Inbox HAR 15 SONIYADA o'zi yangilanadi** (`REFRESH_MS`): Instagram xabari webhook orqali
fonda keladi va sahifada hech qanday "hodisa" bo'lmaydi — yangilanishsiz operator yangi
murojaatni qo'lda F5 qilmaguncha ko'rmasdi. Ikki cheklov ATAYIN: tab ko'rinmayotganda so'rov
YUBORILMAYDI (`visibilitychange`), ochiq suhbat esa operator **matn yozayotganda**
yangilanmaydi (lenta pastga sakrab yozuvni chalg'itardi).
API klienti — `src/api/services/instagram.ts` (`books.ts` uslubida).

⚠️ **Sozlamalar sahifasi maxfiy qiymatni KO'RSATMAYDI** — faqat "sozlangan / sozlanmagan".
Webhook URL va Callback URL **nusxa olish** tugmasi bilan turadi (Meta'ga qo'lda kiritiladi,
harfma-harf mos bo'lishi shart).

⚠️ **Analitika grafiklari** `course-analytics.md` qoidasiga bo'ysunadi: `#0284c7` / `#e11d48`,
**yashil/qizil juftlik ISHLATILMAYDI** (deuteranopiyada ajralmaydi), ikki o'lchov bitta
grafikda **ikki y-o'q bilan ko'rsatilmaydi**.

## 14. Meta platforma talablari

| Talab | Bajarilishi |
|---|---|
| Bot ekanini oshkor qilish | birinchi xabarga `InstagramGreeting` |
| Operatorga o'tish yo'li | "operator"/"odam" → darhol eskalatsiya |
| Maxfiylik siyosati | **ochiq** (login talab qilmaydigan) marshrut `/privacy` |
| Ma'lumotni o'chirish | **ochiq** marshrut `/data-deletion` |

⚠️ Bu ikki sahifa SPA'da ochiq marshrut, lekin **hech qanday CRM ma'lumotini
KO'RSATMAYDI** — faqat: qaysi ma'lumot yig'iladi (username, ID, xabar matni), nima
yig'ilmaydi, kim bilan bo'lishiladi, qanday o'chiriladi.

| Sahifa | Komponent | Marshrut |
|---|---|---|
| Maxfiylik siyosati | `pages/public/PrivacyPolicyPage.tsx` (§10 «Instagram orqali murojaat qilganlar») | `/privacy` |
| Ma'lumotni o'chirish | `pages/public/DataDeletionPage.tsx` | `/data-deletion` |

⚠️ **`/data-deletion` 2026-08-19 gacha UMUMAN YO'Q edi** (hujjatda va'da qilingan, kodda yo'q) —
Meta App sozlamasidagi majburiy maydonni to'ldirib bo'lmasdi. `SecretLeakAndPublicPageTests`
ikkala marshrut ham mavjudligini VA `ProtectedRoute` dan OLDIN turishini (ya'ni login ortida
qolmaganini) tekshiradi.

⚠️ Sahifaga **forma, qidiruv yoki hisob ma'lumoti QO'SHILMAYDI**: manzil ochiq, begona odam
boshqaning ma'lumotini so'rab olishi mumkin bo'lardi.

## 15. Testlar

`IntellectCRM.Tests/Instagram*Tests.cs`:

| Test | Nimani qulflaydi |
|---|---|
| `InstagramSignatureTests` | to'g'ri/noto'g'ri imzo, **bo'sh secret → `false`**, verify challenge |
| `InstagramEventParserTests` | izoh/DM/echo payloadlari, buzuq JSON → bo'sh ro'yxat, **dedup kalitining deterministikligi**, o'z izohini tashlash |
| `InstagramContractTests` | `ClampScore`, `Normalize*`, `DmWindowOpen`, `OperatorPaused`, `ExtractPhone` |
| `InstagramAgentServiceTests` | `ParseOutput` — markdown fence, buzuq JSON, noma'lum enum |
| `InstagramLeadBridgeTests` | yangi lid, mavjud lid (**first-touch**: `Source`/`Stage` o'zgarmaydi), telefonsiz qaynoq lid, `conv.LeadId` bo'lsa takror yaratmaslik |
| `InstagramPipelineTests` | soxta webhook uchdan-uchgacha |

⚠️ Sof funksiyalar (`InstagramContract`, `InstagramSignature`, `InstagramEventParser`,
`BuildSystemPrompt`, `ParseOutput`) ATAYIN HTTP va DB'dan ajratilgan — aynan shular
testlanadi (`tests.md` uslubi).

## 16. REKLAMA LIDLARI (Meta Lead Ads) — target reklamadagi forma

Migratsiya: `AddInstagramLeadAds`. Instagram/Facebook **reklamasidagi forma** (Instant Form)
to'ldirilganda F.I.Sh. va telefon CRM lidiga avtomatik tushadi.
Sahifa: **Marketing → Reklama lidlari** (`/admin/marketing/reklama-lidlari`), sozlash:
**Marketing → Sozlamalar → «Reklama lidlari (Lead Ads)»**.

### 16.1. ⚠️ BU IZOH/DM'DAN BOSHQA YO'L — eng muhim farq

| | Izoh · DM | **Reklama lidi** |
|---|---|---|
| Meta mahsuloti | Instagram API with Instagram Login | **Facebook Page** webhook'i |
| Webhook obyekti | `instagram` | **`page`**, maydon **`leadgen`** |
| Graph hosti | `graph.instagram.com` (`IgConst.GraphBase`) | **`graph.facebook.com`** (`IgConst.FbGraphBase`) |
| Token | Instagram Login tokeni (60 kun, avto-yangilanadi) | **Page Access Token** (System User — muddatsiz) |
| Meta talabi | App Review KERAK EMAS | **`leads_retrieval` + App Review + Business Verification** |
| Manzil | `…/api/public/instagram/webhook` | `…/api/public/instagram/**leadgen**` |

⚠️ **Qoida §11 №9 («`graph.facebook.com` — YO'Q») shu bo'limga TEGISHLI EMAS.** Reklama lidi
Page obyektiga tegishli va `graph.instagram.com` da bunday endpoint YO'Q. Aynan shuning uchun
mijozlar ayri sinflarda: `InstagramApi` (Instagram) va **`MetaAdsApi`** (reklama). Tokenni
almashtirib yuborish `OAuthException 190` bo'lib chiqadi va sababini topish qiyin.

### 16.2. Oqim

```
Meta → POST /api/public/instagram/leadgen
   1) xom bayt + HMAC imzo (AppSecrets.MetaAppSecret)   ← FAIL-CLOSED, §2 bilan bir xil
   2) IgWebhookEvent(EventKey="leadgen:{leadgen_id}")   ← MAVJUD durable navbat
   3) ══ DARHOL 200 OK ══
                    ↓ InstagramWorkerService (har 2 soniya)
InstagramPipeline → MetaLeadgenService.HandleAsync
   4) GET graph.facebook.com/{leadgen_id}  → field_data: full_name, phone_number
   5) MetaLeadBridge.UpsertAsync → LeadIntake dedup → Lead + LeadEvent
   6) LeadNotifier (Telegram)
```

⚠️ **Webhook payloadida ism ham, telefon ham YO'Q** — faqat `leadgen_id`. Meta shaxsiy
ma'lumotni faqat (4) so'rovi orqali beradi. Ya'ni **token bo'lmasa lid mazmunsiz qoladi** —
shuning uchun yozuv baribir saqlanadi (`IgAdLead.Error` bilan) va admin «Qayta olish» tugmasi
bilan tuzatadi. Meta lidni **~90 kun** saqlaydi.

### 16.3. Bayroqlar MUSTAQIL

`CenterMeta.InstagramLeadAdsEnabled` (default **false**) — `InstagramEnabled` dan **AYRI**:
markaz AI agentini ishlatmasdan ham reklama lidlarini olishi mumkin (va aksincha).

⚠️ `InstagramWorkerService` navbatni **ikkalasidan BIRORTASI** yoqilganda qayta ishlaydi.
Faqat `InstagramEnabled` ga qaralsa, AI agentini ishlatmaydigan markazda reklama lidlari
navbatda **turib qolardi** va sababi hech qayerda ko'rinmasdi. Token yangilash esa faqat
`InstagramEnabled` da (u Instagram Login tokeniga tegishli).

### 16.4. Entitylar va dedup

| Entity | Vazifasi |
|---|---|
| `IgAdPage` | Lid olinadigan Facebook Page: `PageId`, **`AccessToken`**, `LeadgenSubscribed`, `LastLeadAt`, `LastError` |
| `IgAdLead` | Kelgan BITTA lid: `LeadgenId` (**UNIKAL**), forma/e'lon/kampaniya id va nomlari, F.I.Sh., telefon, `RawFieldsJson`, `LeadId`, `Error` |

⚠️ **Dedup IKKI qavat:** (1) `IgWebhookEvent.EventKey = leadgen:{id}` — navbat darajasida;
(2) **`IgAdLead.LeadgenId` unikal indeksi** — uzoq muddatli qavat. Navbat yozuvlari 30 kunda
tozalanadi, ya'ni birinchi qavat abadiy emas: usiz Meta eski hodisani qayta yuborsa CRM'da
**ikkinchi lid** ochilardi.

⚠️ Kalit `MetaLeadgenParser.EventKey` da — sof funksiya, `MetaLeadgenParserTests` bilan
qulflangan (deterministiklik §5 qoidasi).

⚠️ **`IgConst.LeadgenFields` ga mavjud bo'lmagan maydon qo'shmang** — Graph butun so'rovni rad
etadi (`code 100`) va lidlar UMUMAN kelmay qo'yadi. Forma NOMI lid tugunida yo'q, u alohida
olinadi (`FetchFormNameAsync`) va **keshlanadi** (o'sha formaning oldingi lidida saqlangan nom).

### 16.5. Lid — `MetaLeadBridge`, qoidalar `InstagramLeadBridge` bilan AYNAN bir xil

Telefon bo'yicha dedup (`LeadIntake.FindByPhoneAsync`), **first-touch** (`Source`/`Stage`
o'zgarmaydi, `RepeatCount++`), faqat bo'sh maydonlar to'ldiriladi.
`Lead.Source` = `CenterMeta.InstagramAdsLeadSource` (default `"Instagram reklama"`),
`Lead.InterestSubject` = **forma nomi**.

⚠️ Bir odam avval reklama formasini to'ldirib keyin DM yozishi (yoki aksincha) juda odatiy —
ikki joyda ikki xil qoida bo'lsa CRM'da bitta odam **ikkita kartochka** bo'lib qolardi.

⚠️ **Telefonsiz lid ham YOZILADI** (`"Reklama lidi (ismsiz)"`): Meta formasida telefon majburiy
bo'lmasligi mumkin va jimgina tashlansa markaz **pul to'lagan** murojaatdan xabar topmasdi.

⚠️ **Kanal tasnifi:** `LeadOrigins.Ads` (`"ads"`, «Instagram reklamasi») — DM/izoh lididan
ATAYIN ajratilgan, aks holda "Instagram" degan bitta qator reklama byudjeti qancha lid
berganini ko'rsatmasdi. `Classify` da **reklama Instagram'dan OLDIN** tekshiriladi (birinchi
teginish).

### 16.6. Sozlash — HAMMASI Marketing bo'limidan

`.env` da faqat **ixtiyoriy** `META_APP_SECRET` / `META_VERIFY_TOKEN`: bo'sh bo'lsa
`INSTAGRAM_*` kalitlariga qaytadi (bitta Meta ilovasi ishlatilganda hech narsa qo'shilmaydi).
Ikkalasi ham `docker-compose.yml` da (`EnvKeysWiringTests` qulfi).

**Page ID va Page Access Token — UI'dan** (`PUT /ads/page`), OAuth YO'Q. Sabab: System User
tokeni **muddatsiz**, ya'ni bir marta kiritiladi; OAuth oqimi esa Facebook Login mahsulotini,
yana bir redirect URI'ni va yangilash mexanizmini talab qilardi.

⚠️ Saqlashdan **OLDIN** token tekshiriladi (`GET /{page-id}`) va sahifa `leadgen` maydoniga
**obuna** qilinadi (`POST /{page-id}/subscribed_apps`). **Obunasiz Meta hodisani UMUMAN
yubormaydi** — Meta konsolida manzil to'g'ri turgan bo'lsa ham. Shuning uchun "obuna faol/yo'q"
holati ekranda alohida ko'rsatiladi: aks holda nosozlik "reklama ishlayapti, lid kelmayapti"
bo'lib ko'rinardi.

⚠️ Token **hech qachon** javobga tushmaydi — faqat `tokenSet` bayrog'i. Forma bo'sh yuborilsa
mavjud token saqlanadi (Page ID'ni tahrirlash uchun tokenni qayta yozish shart emas).

### 16.7. API va ruxsat

| Metod · yo'l | Vazifasi | Ruxsat |
|---|---|---|
| `GET \| POST /api/public/instagram/leadgen` | Meta webhook'i | `[AllowAnonymous]` + HMAC |
| `GET /ads/status` | Diagnostika (modul/sahifa/token/obuna/sanoq) | `marketing` (RRP) |
| `PUT \| DELETE /ads/page` | Sahifani ulash / uzish | `marketing.settings` |
| `GET /ads/leads` | Ro'yxat + jamlanma + forma/kampaniya kesimi | `marketing` (RRP) |
| `POST /ads/leads/{id}/retry` | Xato bilan qolgan lidni qayta olish | `marketing.settings` |

Sahifa ruxsati — **`marketing.leadads`** (bo'lim kaliti `marketing` o'zgarmagan, faqat sahifa
kaliti qo'shilgan — §8 dagi naqsh).

⚠️ Jamlanma va kesimlar **SERVERDA, butun topilma bo'yicha** — ro'yxat sahifalangani uchun uni
qatorlardan qo'shib chiqarish noto'g'ri son berardi (`books.md` dagi bir xil saboq).

### 16.8. Audit

`EntityType = "Instagram"` (bo'lim `marketing`). Yoziladi: **sahifani ulash/uzish**, sozlama
o'zgarishi (bayroq holati bilan), **lidni qayta olish**. Token va App Secret YOZILMAYDI.
Har kelgan lid auditga yozilmaydi — u `IgAdLead` ro'yxatida va `LeadEvent` da allaqachon bor.

### 16.9. Testlar

`IntellectCRM.Tests/MetaLeadgenTests.cs`:

| Test sinfi | Nimani qulflaydi |
|---|---|
| `MetaLeadgenParserTests` | payload o'qilishi, **dedup kalitining deterministikligi**, raqam/satr id, `page_id` yo'q bo'lsa `entry.id`, izoh payloadi olinmasligi, buzuq JSON → bo'sh |
| `MetaAdLeadReadTests` | `field_data` → F.I.Sh.+telefon, `first_name`+`last_name`, notanish maydon xom JSON'da qolishi |
| `MetaLeadBridgeTests` | yangi lid, **first-touch**, boshqa formatdagi telefon bilan dedup, telefonsiz lid, bo'sh manba |
| `LeadOriginsAdsTests` | reklama DM'dan ustun, qo'lda kiritilgan reklamadan ustun |


## 17. REKLAMA STATISTIKASI (Meta Ads Insights + ROI)

Migratsiya: `AddMarketingExpansion` (§17–§20 ning HAMMASI shu bitta migratsiyada).
Sahifa: **Marketing → Reklama statistikasi** (`/admin/marketing/reklama-statistikasi`),
ruxsat `marketing.adsstats`. Sozlash qo'llanmasi: `instagram/REKLAMA-STATISTIKASI.md`.

Modul Meta'dagi **xarajat**ni CRM'dagi **lid → o'quvchi → to'lov** zanjiri bilan birlashtiradi.
Bu Ads Manager'da **YO'Q** narsa: Meta lidning SONINI biladi, CRM esa o'sha lid PUL to'laganini.

### 17.1. ⚠️ UCHINCHI TOKEN — eng ko'p vaqt yo'qotadigan chalkashlik

| Modul | Token | Ruxsat | Entity |
|---|---|---|---|
| Izoh · DM | Instagram Login (OAuth) | `instagram_business_*` | `IgAccount` |
| Reklama lidlari (§16) | Page Access Token | `leads_retrieval` | `IgAdPage` |
| **Reklama statistikasi** | **System User tokeni** | **`ads_read`** | **`IgAdAccount`** |

🔴 **`IgAdPage.AccessToken` bu yerda YARAMAYDI** — Page tokenida `ads_read` yo'q, chunki
Insights **Ad Account** obyektiga tegishli. Aynan shu sababdan token AYRI entity'da va mijoz
AYRI sinfda: **`MetaInsightsApi`** (`MetaAdsApi` ga TEGILMAYDI — u lid uchun). Host esa
ikkalasida bir xil — `IgConst.FbGraphBase`.

### 17.2. Entitylar

| Entity | Vazifasi | Unikal kalit |
|---|---|---|
| `IgAdAccount` | Ulangan reklama kabineti: `AdAccountId` (**`act_` prefiksi bilan**), `Currency`, `CurrencyOffset`, `TimezoneName`, `AccessToken`, `LastSyncAt`, `LastError` | `AdAccountId` |
| `IgAdEntity` | campaign / adset / ad iyerarxiyasi + `CreativeStoryId` (§20 uchun) | `ExternalId` |
| `IgAdInsight` | Kunlik faktlar (bitta obyekt × kun × platforma) | `(Level, ExternalId, StatDate, Platform)` |

`CenterMeta`: `InstagramAdsStatsEnabled` (**default false**), `InstagramAdsSyncHour` (5),
`InstagramAdsBackfillDays` (90).

⚠️ **Migratsiyadagi `defaultValue` lar QO'LDA tuzatilgan** (`books.md` §4 sabog'i): EF entity
initsializatorini MAVJUD qatorlarga qo'llamaydi, ya'ni ishlab turgan markazda `SyncHour` va
`BackfillDays` **0** bo'lib qolib, birinchi backfill UMUMAN bajarilmasdi.

⚠️ Akkaunt uzilganda **qator O'CHIRILMAYDI** (`IsActive=false` + token tozalanadi) — o'tgan
oylarning hisoboti buzilmasin. Reklama Meta'da o'chirilsa ham `IgAdEntity` qatori qoladi.

**YIG'ILADIGAN, LEKIN ODDIY BO'LMAGAN maydonlar:**

| Maydon | Nima | Holati |
|---|---|---|
| `IgAdInsight.MsgStarted` | `messaging_conversation_started_7d` | ✅ **KO'RSATILADI** (2026-08-21): «Yozishma boshlandi» KPI + kampaniya jadvalidagi ustun. Click-to-Direct reklamasining ASOSIY natijasi — bunday kampaniyada forma yo'q, ya'ni `MetaLeads` nol turadi. ⚠️ Lidlarga **QO'SHILMAYDI** (bitta odam ikkalasini ham qilishi mumkin) va 7 kunlik oyna tufayli son **orqaga qarab o'zgaradi** |
| `IgAdInsight.AttributionSetting` | Ad set'ning atributsiya oynasi | ✅ **KO'RSATILADI** (2026-08-21): `IgRoiReportDto.AttributionSetting` → kampaniya jadvali ostidagi kichik izoh. ⚠️ Meta bergan **HOLICHA**, tarjima QILINMAYDI (Ads Manager'dagi texnik nom). Davrda bir necha qiymat uchrasa **hammasi** `" · "` bilan sanaladi — jimgina bittasi tanlansa hisobot "bitta oyna bo'yicha" bo'lib ko'rinardi |
| `IgAdInsight.ActionsJson` | XOM `actions` massivi | ⏸️ Yig'iladi, ko'rsatilmaydi — yangi `action_type` kerak bo'lsa **qayta sinxronizatsiyasiz** hisoblash uchun (`ActionValueFromJson`) |
| `cost_per_action_type` | So'raladi, lekin **parse qilinmaydi** | ⏸️ ATAYIN: CPL biz tomonda **CRM lidlari** bo'yicha hisoblanadi (Meta'niki o'z lid ta'rifi bo'yicha va boshqa son berardi). So'rovdan olib tashlanmaydi ham — maydonlar ro'yxati Meta bilan shartnoma, uni qisqartirish hech narsa tejamaydi, kerak bo'lganda esa qayta sinxronizatsiya talab qilardi |

### 17.3. 🔴 PUL BIRLIGI ASSIMETRIYASI — №1 xato manbai

| Nima | Format | Misol |
|---|---|---|
| Byudjet (`daily_budget`, `lifetime_budget`) | **butun son, MINOR unit** | `5000` = 50.00 |
| Insights `spend` | **MATN, MAJOR unit** | `"312.45"` = 312.45 |

Bazada hamma narsa **minor** (`long`): kasrli `decimal` ustunlar yig'indida yaxlitlash xatosi
to'plardi. O'girish YAGONA joyda — `MetaCurrency` (sof, testlangan).

🔴 **`currency_offset` — TAXMIN QILINMAYDI, ISH VAQTIDA aniqlanadi** (2026-08-21). Hujjatlar
zid edi: `META-API-MALUMOTNOMA.md` §11.1 "maydon bor", `KENGAYTIRISH-PROMPT.md` §4.2 "maydon
yo'q, so'ralsa `code 100`". Noto'g'ri offset esa pul hisobini **100 barobar** buzadi, ya'ni
"xavfsiz taxmin" ham baribir taxmin bo'lardi. Shuning uchun `MetaInsightsApi.FetchAccountAsync`:

1. avval maydonni **SO'RAYDI** (`fields=…,currency_offset`);
2. Meta rad etsa — **BIR MARTA** maydonsiz qayta so'raydi va offsetni `MetaCurrency.OffsetOf`
   dan oladi (zero-decimal ro'yxati → 0, qolgani va noma'lum kod → **2**; UZS ham 2);
3. Meta qiymat bersa — **o'sha haqiqat manbai**, lekin jadval bilan solishtiriladi va farq
   bo'lsa `LogWarning` (jadvalimiz eskirgan degani).

⚠️ **Qayta so'rov FAQAT `code 100` da** (`MetaInsightsApi.IsUnknownFieldError`, `100+1487534`
CHIQARIB tashlangan): token (190), ruxsat (200) yoki kvota xatosida takror so'rov kvotani
bekorga yeydi (§17.5). Qaror **KOD** bo'yicha — Meta xato MATNI shartnoma emas.

⚠️ **Meta qiymati DIAPAZONGA solishtiriladi** (`0..MetaCurrency.MaxOffset`): eskirgan `Currency`
tugunida `offset` KO'PAYTUVCHI edi (`100`), bizga esa kasr xonalari soni kerak — mantiqsiz qiymat
jadvalga qaytadi. `0` esa HAQIQIY offset (JPY).

⚠️ **Manba KO'RINADI:** `MetaAdAccountInfo.OffsetSource` (`"meta"` | `"jadval"`,
`MetaOffsetSource`), `GET adsstats/status` javobida `currencyOffsetSource` sifatida va akkaunt
ulanganda auditda. **Bazada ustun YO'Q** (migratsiya kerak emas) — qiymat
`MetaInsightsService.OffsetSourceOf` bilan hisoblanadi: jadvaldan farq qiladigan offsetni faqat
Meta bergan bo'lishi mumkin (teng bo'lsa farq amaliy ahamiyatga ega emas, "jadval" deyiladi).

⚠️ `Math.Pow` o'rniga JADVAL (`Factors`) — `double` aylanishi katta summalarda bir tiyinlik
farq berardi. Parse `InvariantCulture` bilan: server `ru-RU` da `"312.45"` nuqtasini guruh
ajratgichi deb o'qib, natijani **100 barobar** buzardi.

### 17.4. Sinxronizatsiya siyosati (`MetaInsightsService`)

| Qachon | Oraliq | Nega |
|---|---|---|
| Birinchi ulanish (`LastSyncAt` bo'sh) | `InstagramAdsBackfillDays` (90), **`ChunkDays` = 10** kunlik bo'laklarda | `level=ad` + `time_increment=1` + `publisher_platform` bilan 90 kun ≈ 9000 qator — `MaxPages` to'sig'idan ham, `100/1487534` xatosidan ham o'tolmasdi |
| Har kuni `InstagramAdsSyncHour` da | oxirgi **`ReloadDays` = 7** kun QAYTA | Meta atributsiyani **48 soatgacha** tuzatadi — bir marta yozilgan kun keyin ham o'zgaradi |
| Qo'lda | tanlangan oraliq, ≤ `MaxRangeDays` (365) | |

Upsert `(Level, ExternalId, StatDate, Platform)` — bazadagi unikal indeks bilan AYNAN bir xil,
shuning uchun qayta yuklash dublikat yaratmaydi.

⚠️ **Kalitga `AdAccountId` KIRMAYDI** (indeksda ham yo'q) — mavjud qatorlar akkauntga
qaramasdan qidiriladi, aks holda takroriy yuklash unikal indeksni buzardi.

⚠️ **`LastSyncAt` faqat TO'LIQ muvaffaqiyatda yangilanadi.** Backfill yarmida yiqilsa ertaga u
BOSHIDAN takrorlanadi — upsert buni zararsiz qiladi, "yarim yuklangan tarix" esa hisobotda
**jimgina teshik** qoldirardi.

⚠️ **Har bo'lak O'Z tranzaksiyasida saqlanadi** — 90 kunni bitta ulkan `SaveChanges` bilan
yozib, oxirida yiqilsa hammasi yo'qolardi.

⚠️ **Sana AKKAUNT vaqt zonasida** (`TodayInAccountZone`), `AppClock.Today` (Toshkent) EMAS.
Aralashsa "kechagi sarf 0" holati chiqadi. Zona serverda tanilmasa (tzdata yo'q konteyner)
Toshkent kuniga qaytiladi — bir kunlik siljish ehtimoli bor, lekin chegaraviy kunlar baribir
qayta yuklanadi. `IgAdInsight.StatDate` Meta bergan holicha yoziladi, **surilmaydi**.

### 17.5. Rate limit siyosati — MAJBURIY

Sarlavhalar har javobdan o'qiladi (`MetaInsightsParser.ParseThrottle`):
`X-FB-Ads-Insights-Throttle` va `X-Business-Use-Case-Usage` (kalit — **business id**, ya'ni
oldindan noma'lum: barcha kalitlar ko'riladi, faqat `type == "ads_insights"` olinadi).

Kvota formulasi: `600 + 400 × aktiv reklama − 0.001 × xatolar`.
🔴 **Bizning 4xx xatolarimiz kvotani KAMAYTIRADI** — shuning uchun:

| Kod | Qayta urinish |
|---|---|
| `190` (token), `200`/`10`/`299` (ruxsat), `100` (parametr), `803` | ❌ **hech qachon** |
| `80000`/`80004` (BUC limiti) | ❌ Meta ochiq aytadi: to'xtatish kerak, davom etilsa blok **UZAYADI** |
| `4`, `17`, `32`, `613`, `2` va 5xx/429 | ✅ 1s → 2s → 4s (3 urinish) |
| `100` subcode `1487534` | ❌ backoff yordam bermaydi → **oraliq ikkiga bo'linadi** (`MaxSplits` = 24) |

⚠️ Kvota **≥95%** (`QuotaStopPct`) bo'lsa sinxronizatsiya **o'z ixtiyorimiz bilan** to'xtaydi;
≥80% da muvaffaqiyatdan keyin `LastError` ga **ogohlantirish** yoziladi (xato emas).

⚠️ **`Classify` qarori (`Stop` / `Shrink` / `Fatal`) — avval Meta KODI bo'yicha, kod
bo'lmasa MATN bo'yicha.** Kod `MetaInsightsApi.LastErrorCode` / `LastErrorSubcode` dan olinadi.

🔴 **Nega kod birinchi:** `MapError` dagi matn foydalanuvchiga ko'rsatiladigan jumla, ya'ni uni
tahrirlash NORMAL ish. Qaror faqat matnga tayansa (`Contains("qisqartiring")`), **bitta so'z
o'zgarishi bilan backfill hech qachon BO'LINMAY qolardi** va sabab hech qayerda ko'rinmasdi.
Kod esa Meta bilan **shartnoma**.

| Kod | Qaror |
|---|---|
| `100` + subcode `1487534` | `Shrink` — oraliq ikkiga bo'linadi |
| `190`, `200`, `10`, `299` | `Fatal` — odam aralashuvi + Telegram signali |
| `80000`, `80004`, `4`, `17`, `32`, `613` | `Stop` — keyingi ishga qoldiriladi |

⚠️ **Matn baribir ZAXIRA:** tarmoq uzilishi/timeout'da Meta kodi umuman bo'lmaydi (0) va qaror
matndan olinadi. Ikkalasi ham tanimasa — `Stop`, ya'ni **XAVFSIZ tomon** (kutamiz, davom etib
blokni uzaytirmaymiz).

⚠️ **Sahifalash to'sig'i `MaxPages` = 20** va oshgani **JIM KESILMAYDI**: `LogWarning` + metod
`Ok=false` qaytaradi. Yarim yuklangan kunni "to'liq" deb yozib qo'yish hisobotni sekin-asta
buzardi va buni hech kim sezmasdi.

⚠️ `paging.next` ga faqat **`https://`** bo'lsa ergashiladi — manzil ichida `access_token` bor,
begona xostga ergashish tokenni sizdirardi.

### 17.6. ROI qoidalari (`MetaAdsRoi`)

🔴 **DARAJALAR ARALASHMAYDI.** `IgAdInsight` da uch daraja bitta jadvalda yotadi va kampaniya
qatori o'z e'lonlari yig'indisi bilan bir xil — birga sanalsa **sarf ikki-uch barobar**
ko'rinardi. Hisobot HAR DOIM **bitta darajadan** yig'iladi (`PickLevel`: ad → adset → campaign,
eng maydasi ustun), qolganlari `ParentId` orqali yuqoriga ko'tariladi.

🔴 **QAMROV QO'SHILMAYDI.** `publisher_platform` qatorlari Meta tomonidan **dedup
QILINMAGAN** (bir odam ikki kunda ham, ikki platformada ham sanaladi), ya'ni `SUM(Reach)` —
qamrov emas. Ikki **halol chegara** beriladi: `Reach` = **MAX** (quyi), `ReachUpper` = **SUM**
(yuqori), `ReachApprox = true`. UI'da «≈» bilan chiziladi.

| Qoida | Nega |
|---|---|
| **CPL/CAC/ROI `null` bo'lishi mumkin** | Bo'luvchi 0 bo'lsa `0` YOZILMAYDI — "lid tekinga tushdi" bilan "hisoblab bo'lmadi" bir xil ko'rinardi. UI «—» chizadi |
| ROI daromad 0 bo'lganda **`-1`** | Bu HAQIQIY qiymat (butun pul kuydi), `null` EMAS |
| **Konversiya LID bo'yicha, daromad O'QUVCHI bo'yicha** dedup | Ikki lid bitta o'quvchiga ulansa: 2 konversiya, lekin pul **BIR marta** — aks holda "daromad ikki barobar" |
| To'liq qaytarilgan to'lov | `Math.Max(0, …)` — daromadga **manfiy qo'shilmaydi** (`LeadFormService.Funnel` bilan bir xil qoida) |
| "To'ladi" = faqat `tuition` | Kitob savdosi `FinanceTransaction` ga yozilmaydi (`books.md` §7) |
| Manba — **`LeadOutcome`** | "To'ladi" so'zi lid formalari, daraja testi va shu hisobotda BIR XIL ma'no anglatishi shart |
| Nomi topilmagan tugun | id'ning O'ZI chiziladi — sun'iy "Noma'lum" bazadagi haqiqiy tugundan ajratib bo'lmasdi, id esa Ads Manager'da qidirsa bo'ladigan qiymat |
| `Platform == "all"` qatorlar platforma tanlanganda **KIRMAYDI** | "Instagram sarfi" deb Facebook pulini qo'shish hisobotni yolg'on qilardi; natijasi `Notes` da aytiladi |
| `DayEnd(day)` = `"…T23:59:59.999"` | `IgAdLead.CreatedTime` zona qo'shimchasi bilan kelishi mumkin (`…+0000`) va oddiy `T23:59:59` chegarasida oxirgi soniya tushib qolardi |

⚠️ **`Notes[]` — foydalanuvchiga OCHIQ aytiladigan ogohlantirishlar** (taqqoslanmaydigan
o'lchov, taxminiy qamrov, Meta≠CRM farqi, o'chirilgan lidlar, valyuta, vaqt zonasi). Ular
hisobotni **noto'g'ri o'qishdan** saqlaydi va UI'da chizilishi SHART.

⚠️ Chegaralar: `MaxCampaigns` = 200, `MaxChildren` = 100 — oshgani **JIM tashlanmaydi**,
`Notes` ga qator qo'shiladi va **jamlanma BARCHASI bo'yicha** qoladi.

### 17.7. Kesh va endpointlar

**Kesh:** `DataCache`, kalit `marketing:ads-roi:{from}:{to}:{platform}:{campaign}`, TTL 10 daq,
bog'liq turlar: `IgAdAccount`, `IgAdInsight`, `IgAdEntity`, `IgAdLead`, `Lead`, `LeadStage`,
`StudentGroup`, `FinanceTransaction`.

⚠️ **Kampaniya ham KALITGA kiradi** (spetsifikatsiyadagi kalitga qo'shimcha): jamlanma filtrga
bog'liq hisoblanadi, aks holda "jadval bitta kampaniya, tepadagi KPI esa hammasi" bo'lardi.
⚠️ **`IgAdAccount` ham bog'liqlikda**: akkaunt ulangan zahoti hisobot 10 daqiqa "ulanmagan" deb
turib qolmasin.

| Verb + route | Ruxsat | Nima |
|---|---|---|
| `GET adsstats/status` | klass | Diagnostika: bayroq, akkaunt, `tokenSet`, oxirgi sinxronizatsiya/xato, qatorlar soni |
| `PUT adsstats/account` | `marketing.settings` | Ulash — **saqlashdan OLDIN `FetchAccountAsync` bilan tekshiriladi**, o'tmasa hech narsa saqlanmaydi |
| `DELETE adsstats/account` | `marketing.settings` | Uzish (`IsActive=false`, token tozalanadi) |
| `POST adsstats/sync` | `marketing.settings` | Qo'lda; ⚠️ **muvaffaqiyatsizlikda ham HTTP 200** — sinxronizatsiya QISMAN bajarilishi mumkin va buni 400 ifodalay olmaydi |
| `GET adsstats/overview` · `campaigns` · `roi` | klass | Uchalasi ham **AYNAN bitta** hisobotdan (`MetaAdsRoi.BuildAsync`) — bir ekranning uch bo'lagi uch xil raqam ko'rsatmasin |

⚠️ Akkaunt ulanmagan bo'lsa hisobot **200 + `connected:false`** qaytaradi (400/500 EMAS):
"xato" bilan "hali sozlanmagan" ni aralashtirish foydalanuvchini bekorga qo'rqitardi.

⚠️ `POST adsstats/sync` da **audit yozuvi sinxronizatsiyadan KEYIN** qo'shiladi: servis o'z
`SaveChangesAsync` ini chaqiradi va tranzaksiya bir xil `DbContext` da — oldin yozilgan audit
qatori yarim yo'lda saqlanib, "qilindi" deb yozilgan bo'lardi.

⚠️ Telegram signali `InstagramPipeline.NotifyAdminsAsync` dan **qayta ishlatilmadi**: u
`InstagramEnabled` bilan darvozalangan, bu modul esa undan MUSTAQIL — AI agentini ishlatmaydigan
markazda "token o'lgan" signali **jimgina yo'qolardi**. Signal faqat `Fatal` (token/ruxsat)
xatosida va faqat **xato matni O'ZGARGANDA** yuboriladi (kvota xatosi o'zi tiklanadi, bir xil
matnni har kuni yuborish signalni shovqinga aylantirardi).

### 17.8. Testlar

| Test sinfi | Nimani qulflaydi |
|---|---|
| `MetaInsightsParserTests` (33) | Valyuta offseti (UZS 2 / JPY 0 / noma'lum 2), **Meta bergan `currency_offset` jadvaldan ustunligi** va mantiqsiz qiymat (`100`, `"abc"`) jadvalga qaytishi, `spend` matndan minorga va yaxlitlash, `actions` da yo'q tur → 0, `action_breakdowns` da takrorlangan tur **yig'iladi**, `lead` turi hisobga olinmasligi, buzuq JSON → bo'sh, `paging.next` faqat `https`, `end_time`/`stop_time`, `creative.effective_object_story_id`, akkaunt id normalizatsiyasi, throttle sarlavhalari |
| `MetaInsightsServiceTests` (13) | **Modul o'chiq bo'lsa tashqariga so'rov ketmasligi**, **`currency_offset` rad etilsa AYNAN bir marta maydonsiz qayta so'ralishi** (190 da esa — YO'Q), Meta bergan offsetning bazaga yozilib sarfga qo'llanishi, qayta sinxronda dublikat yo'qligi, birinchi ulanishda oraliq bo'laklarga bo'linishi, token xatosida qayta urinilmasligi, **xato KODI matndan ustunligi**, kod yo'q bo'lsa matnga tushilishi, noma'lum xato to'xtatishi |
| `MetaAdsRoiTests` (22) | Qaytarilgan to'lov, kitob savdosi daromadga qo'shilmasligi, Meta≠CRM ikkalasi qaytishi, **kampaniya va e'lon qatorlari QO'SHILMASLIGI**, qamrovning ikki chegarasi, platforma/kampaniya filtri, akkauntsiz bo'sh javob, CPL/ROI `null` qoidalari |

---

## 18. KONTENT JOYLASH (Instagram Content Publishing)

Migratsiya: `AddMarketingExpansion`. Sahifa: **Marketing → Kontent**
(`/admin/marketing/kontent`), ruxsat `marketing.content`.
Sozlash qo'llanmasi: `instagram/KONTENT.md`. **UI tuzilishi — §18.11** (modul bitta modaldan
alohida sahifalarga ko'chgan).

Entity — `IgScheduledPost`. `CenterMeta.InstagramPublishEnabled` (**default false**).
Sof qoidalar — `InstagramPublishContract` + `IgPublishConst`, HTTP — `InstagramPublishApi`,
oqim — `InstagramPublishService`.

### 18.1. 🔴 NATIVE REJALASHTIRISH YO'Q

`POST /{ig-user-id}/media` da **`scheduled_publish_time` parametri MAVJUD EMAS** (u faqat
Facebook Page `/feed` da), konteyner esa **24 soatda o'ladi**. Demak:

- vaqt **bizning** navbatda (`IgScheduledPost.ScheduledAt`), navbat **DB jadvalida**;
- konteyner **oldindan YARATILMAYDI** — faqat chop etish payti. Aks holda "ertaga ertalabga"
  yaratilgan konteyner vaqti kelganda `EXPIRED` bo'lib, **post jimgina yo'qolardi**.

⚠️ `/me` ga **TAYANILMAYDI**: Overview'da `/me` aliasi tasdiqlangan, endpoint reference'da esa
`/{ig-user-id}/media` — ikkisi zid. Har chaqiruvda `IgAccount.IgUserId` dan aniq yo'l
ishlatiladi; u bo'sh bo'lsa metod tarmoqqa umuman chiqmaydi.

### 18.2. Oqim va poll

```
worker (30 s) → validatsiya → limit → POST /media → GET /{container} → POST /media_publish
```

| Qoida | Tafsilot |
|---|---|
| Poll jadvali | **30 → 60 → 120 → 300 s**, keyin 300 da to'xtaydi (Meta: daqiqada bir marta, 5 daqiqadan ko'p emas) |
| Poll muddati | **10 daqiqa** (`PollTimeoutSeconds`), keyin `failed` |
| Bir tsiklda | **3 ta** post, ULARNING ICHIDA `processing` BIRINCHI — aks holda boshlangan ish oxiriga yetmasdi |
| Urinishlar | **3**, keyin `failed` + Telegram signali |

🔴 **POLL WORKER'NI BLOKLAMAYDI.** `ContinueAsync` da `Task.Delay` **YO'Q**: konteyner
`IN_PROGRESS` bo'lsa post `processing` da QOLADI va keyingi tsiklda davom etadi. Aks holda
bitta video butun navbatni 10 daqiqaga to'xtatib qo'yardi.

⚠️ Konteyner yaratilgach **darhol birinchi poll** qilinadi — rasm konteyneri odatda darhol
`FINISHED` bo'ladi va oddiy post AYNI tsiklda joylanadi (30 soniya kutilmaydi).

⚠️ SQL faqat QO'POL saralash qiladi (satr taqqoslash), yakuniy qaror sof funksiyada
(`IsDue`) — buzuq sana yozilgan qator SQL filtridan o'tib ketishi mumkin, qoida esa BITTA
joyda turishi kerak (`contacts.md` §3.6 yondashuvi).

### 18.3. 🔴 `PublishAsync` da QAYTA URINISH YO'Q + POYGA QULFI

Chop etilgan IG media'ni API orqali **tahrirlab ham, o'chirib ham bo'lmaydi**. Shuning uchun:

| Himoya | Nima bo'lardi busiz |
|---|---|
| `PublishAsync(retry: false)` | Meta postni joylab, javobni yetkaza olmasa (5xx/timeout) takror **IKKINCHI POST** yaratardi — profilda abadiy qoladigan dublikat |
| `SemaphoreSlim Gate` (jarayon ichida) | Worker tsikli va «Hoziroq joylash» bir vaqtda bir postni ko'rsa **ikkita konteyner** yaratilib, post ikki marta chiqardi |
| Konteyner `PUBLISHED` bo'lsa **qayta chop etilmaydi** | Avvalgi urinishda javob yo'qolgan bo'lishi mumkin; ikkinchi marta chop etish dublikat berardi. Holat `published` deb yopiladi va sabab ochiq yoziladi |

Noaniq holatda post `failed` bo'ladi va xato matnida **"Instagram'da tekshiring"** deyiladi —
qayta urinish qarorini **ODAM** qabul qiladi.

⚠️ Poll holati (`Polls`) **XOTIRADA**: `IgScheduledPost` da "oxirgi so'rov vaqti" ustuni yo'q
va migratsiya bu ish doirasidan tashqarida. Bu faqat TEZLIK maslahati — yo'qolsa post keyingi
tsiklda darhol so'raladi (eng yomon oqibat: bitta ortiqcha so'rov). Haqiqiy holat
(`Status`, `ContainerId`, `ContainerStatus`) BAZADA.

### 18.4. Darvozalar va "yumshoq" xatolar

| Holat | Post nima bo'ladi | Nega |
|---|---|---|
| Modul o'chiq | tegilmaydi | Tashqariga hech qanday so'rov ketmaydi |
| **Token o'lgan / akkaunt ulanmagan** | `failed` EMAS — sabab `Error` ga yoziladi, navbat to'xtaydi | Sabab ULANISHDA, postda emas. Admin qayta ulaganda navbat o'zi davom etadi. ⚠️ Jimgina "hech narsa bo'lmayapti" holati eng yomoni — shuning uchun sabab ro'yxatda ko'rinadi |
| **Kunlik limit to'lgan** | `scheduled` bo'lib qoladi, `Attempts` **oshmaydi** | Limit sutkalik va o'zi bo'shaydi; aks holda post uch tsiklda "xato" bo'lib yonardi |
| Limit so'rovi **javob bermadi** | ish TO'XTAMAYDI, urinish "kuymaydi" | Limit tekshiruvi MASLAHAT xarakterida — haqiqiy limitni Meta `media_publish` da o'zi tekshiradi (`2207042`) |
| Validatsiya / buzuq `MediaJson` | DARHOL `failed` (`hard`) | Qayta urinishdan o'zi tuzalmaydi. ⚠️ Tarmoqqa **umuman chiqilmaydi** |
| Konteyner 24 soatdan oshgan | `scheduled` ga qaytadi, yangi konteyner | Kutishning ma'nosi yo'q |

⚠️ `quota_total` **KODGA YOZILMAYDI** — Meta hujjatlari zid (qo'llanmada **100**, reference
namunasida **50**). Qiymat ish vaqtida `config.quota_total` dan o'qiladi; **0 = noma'lum** va
`QuotaExceeded` postni to'xtatmaydi. UI'da ham "noma'lum" yoziladi, taxminiy son ko'rsatilmaydi.

⚠️ Noma'lum `status_code` **`IN_PROGRESS`** ga tushadi, `ERROR` ga emas: yangi/kutilmagan
qiymat tufayli tayyor bo'layotgan post o'chirilib ketmasin (poll baribir 10 daqiqada to'xtaydi).

⚠️ `IsContainerExpired` / `IsPollExpired` buzuq sanada **`true`** qaytaradi ("bilmasak, cheksiz
kutmaymiz"), `IsDue` esa **`false`** (buzuq yozuv navbatni band qilmasin).

### 18.5. 🔴 `record` DESERIALIZATSIYA TUZOG'I (.NET 8 STJ)

`MediaJson`/`OptionsJson` `IgMediaItem`/`IgPublishOptions` **record**'lariga to'g'ridan-to'g'ri
deserializatsiya **QILINMAYDI**. Sabab: .NET 8 dagi `System.Text.Json` **konstruktor
parametrlarining STANDART QIYMATINI e'tiborsiz qoldiradi** va yo'q maydonga `default(T)` beradi:

- `thumbOffsetMs` JSON'da bo'lmasa record'dagi **`-1` ("berilmagan")** o'rniga **`0`
  ("videoning birinchi kadri")** tushardi va Meta'ga ortiqcha `thumb_offset=0` ketardi;
- `kind` **`null`** bo'lib, `ValidateMedia` ichidagi `item.AltText.Length` **NullReference**
  bilan yiqilardi.

Shuning uchun oraliq **o'zgaruvchan sinflar** — `IgMediaJson` / `IgOptionsJson` (xossa
initializatorlari HAR DOIM ishlaydi), o'qish-yozish esa `IgPublishPayload` da (sof, testlangan).

⚠️ `thumb_offset` da **0 ham HAQIQIY qiymat** — "berilmagan" belgisi `-1`.
⚠️ `share_to_feed` faqat **Reels**da yuboriladi, `alt_text` faqat **yakka rasm**da, story'da
**caption yo'q** — ortiqcha parametr Graph'da `code 100` berishi mumkin.
⚠️ `video` turi ATAYIN **`REELS`** ga aylanadi (Meta feed videosini baribir Reels qilib
joylaydi), `image` uchun esa `media_type` **umuman yuborilmaydi**.
⚠️ Karusel BOLASIDA caption Meta tomonidan **jimgina e'tiborsiz qoldiriladi** — shuning uchun
CRM buni **XATO** deb qaytaradi; nisbat esa faqat **BIRINCHI** element bo'yicha tekshiriladi
(qolganlarini Instagram shunga qirqadi, ularni rad etish foydalanuvchini bekorga to'sardi).

### 18.6. Ochiq media marshruti (§5.6, Variant A)

🔴 **Meta faylni O'ZI yuklab oladi** — manzil ochiq HTTPS bo'lishi SHART; `/uploads`
`UploadsGuard` ortida va har post `2207052` bilan yiqilardi. Yechim: **alohida papka +
alohida marshrut** — `uploads/marketing-public/`.

**To'liq qoida `uploads-security.md` da** («OCHIQ MEDIA» bo'limi) — bu yerda TAKRORLANMAYDI.
Kod yozayotganda bilish shart bo'lgan uch narsa:

1. **`Program.cs` da darvozasiz `UseStaticFiles` bloki AYNAN BITTA** bo'lishi kerak va u shu
   papkaga ildizlangan — `MarketingPublicMediaTests` buni qulflaydi (ikkinchisi qo'shilsa test
   qizaradi);
2. MIME xaritasi **YOPIQ** (`.jpg/.jpeg/.mp4/.mov`) + `ServeUnknownFileTypes=false` — bu papka
   bizning domenimizda, u yerdan `.html`/`.svg` chiqishi **saqlangan XSS** bo'lardi;
3. **auditga fayl manzili/nomi YOZILMAYDI**, `EntityId` — o'zgarmas `"content-media"`.

⚠️ **`Uri.TryCreate(s, UriKind.Absolute, …)` TUZOG'I:** UNIX'da u `/uploads/...` kabi ODDIY
YO'LNI ham qabul qiladi va uni **`file:` sxemasi** deb biladi. Shartsiz ishlatilsa bizning O'Z
manzilimiz "begona sxema" deb rad etilardi va **o'chirish umuman ishlamasdi** — Windows'da esa
bu sezilmasdi (u yerda `/…` absolut URI emas). Shuning uchun `SafeStoredName` da avval
**`"://"` borligi** tekshiriladi.

⚠️ Dev muhitida (http) manzil HTTPS bo'lmaydi va `ValidateMediaUrl` uni **ATAYIN rad etadi** —
"lokalda ishladi, serverda ishlamadi" holatini jimgina yaratmaslik uchun.

### 18.7. API

| Verb + route | Ruxsat | Izoh |
|---|---|---|
| `GET content/posts`, `content/posts/{id}` | klass | Jamlanma **BUTUN topilma** bo'yicha (ko'rinadigan sahifadan emas); noma'lum `status` filtri **qo'llanmaydi** (ro'yxat bo'shab qolmasin). ⚠️ `totals` filtr QO'LLANGANDAN KEYIN hisoblanadi — shu sababdan klient `status` ni serverga umuman yubormaydi (§18.11). ⚠️ Ikkilamchi tartiblovchi YO'Q (`.ThenByDescending(p => p.Id)` — ochiq ish) |
| `POST content/posts` | `marketing.content` | ⚠️ Validatsiya **SAQLASHDA** — aks holda xato 10 daqiqalik poll'dan keyin ko'rinardi |
| `PUT content/posts/{id}` | `marketing.content` | **FAQAT `scheduled`** holatida |
| `DELETE content/posts/{id}` | `marketing.content` | `scheduled` → **bekor**; `published` → faqat CRM yozuvi (⚠️ javobda OCHIQ yoziladi); `processing` → rad |
| `POST content/posts/{id}/publish` | `marketing.content` | Kutmaydi; audit **har doim** yoziladi (xatoda ham) |
| `GET content/limit`, `content/status` | klass | `total=0` → `unknown=true`; `ScopeGranted` ATAYIN **`null`** |
| `POST\|DELETE content/media` | `marketing.content` | Ochiq papkaga yuklash/o'chirish (§18.9) |
| `POST content/caption` | `marketing.content` | AI bilan matn yozdirish (§18.8) — ⚠️ **har doim HTTP 200**, natija `ok`/`error` da |
| `GET content/caption/meta` | klass | Uslub/til ro'yxati + `geminiConfigured` — kalitlar frontendda **takrorlanmaydi** |

⚠️ `content/status` dagi **`ScopeGranted` har doim `null` ("noma'lum")**: berilgan OAuth
ruxsatlari ro'yxati saqlanmaydi, ya'ni `instagram_business_content_publish` olinganini ishonch
bilan ayta olmaymiz. **Yolg'on "ha" dan ko'ra ochiq "noma'lum" yaxshi** — UI shu holatda
«Qayta ulash» maslahatini ko'rsatadi.

⚠️ Scope `IgConst.Scopes` ga qo'shilgan, lekin u **mavjud tokenga qo'llanmaydi** — akkauntni
**QAYTA ULASH** shart.

### 18.8. AI CAPTION (§5.10)

`InstagramCaptionService` (sof funksiyalar) + `InstagramController.ContentAi.cs`.
Foydalanuvchi **MAVZU** yozadi, servis markazning **bilim bazasi** asosida post matni va
hashtaglarni qaytaradi.

| Qoida | Nega |
|---|---|
| Model chaqiruvi **faqat `GeminiService`** orqali | Yangi provayder = yangi kalit = yangi billing — TAQIQ |
| Bilim bazasi **`InstagramAgentService.LoadKnowledgeAsync`** dan | AI agenti va caption generatori AYNAN bir xil ma'lumotni ko'rsin |
| Gemini kaliti yo'q → **tarmoqqa umuman chiqilmaydi** | Tekshiruv `GenerateAsync` ICHIDA, ya'ni kelajakdagi boshqa chaqiruvchi ham o'tkazib yubora olmaydi |
| **Auditga YOZILMAYDI** | Matn yaratish hech narsani o'zgartirmaydi (`audit.md` §3.5, "AI tahlili" istisnosi). Matn ishlatilsa post SAQLANGANDA auditga tushadi |
| Promptga faqat markaz nomi + bilim bazasi + mavzu | O'quvchi, telefon, to'lov — **hech qachon** (`InstagramAgentService` bilan bir xil chegara) |

🔴 **NATIJA CHEGARALARGA SOLISHTIRILADI** (`Finalize`) — AI matni to'g'ridan-to'g'ri
foydalanuvchiga berilmaydi. Aks holda u matnni maydonga qo'yib, **saqlashda** «Matn juda uzun»
(`2207010`) xatosini olardi — ya'ni **yordamchi tugma muammo yasab bergan** bo'lardi.

Tartib ATAYIN shunday:

| # | Qadam | Nega shu tartibda |
|---|---|---|
| 1 | Mention > 20 → **RAD** | Matndan `@` ni olib tashlash ma'noni buzardi; promptda mention allaqachon TAQIQLANGAN, ya'ni bu holat deyarli bo'lmaydi |
| 2 | Hashtaglar tozalanadi, **takrorlari va matnda ALLAQACHON borlari** tashlanadi | Aks holda bitta teg ikki marta chiqardi |
| 3 | Uzunlik oshsa **AVVAL hashtaglar** oxiridan qirqiladi | Ular yordamchi; matn esa asosiy mazmun |
| 4 | Keyin matn **SO'Z chegarasida** kesiladi va oxiriga `…` qo'yiladi | O'rtasidan kesish o'qib bo'lmaydigan natija berardi; `…` qirqilgani **KO'RINIB tursin** (jimgina kesish foydalanuvchini aldardi) |
| 5 | Yakunda **yana `ValidateCaption`** | Saqlashda ishlatiladigan AYNAN o'sha darvoza — kutilmagan kamchilik foydalanuvchiga chiqmasin |

⚠️ **AI'dan chegaraning O'ZI emas, ZAXIRALI qiymat so'raladi:** `TargetCaptionLength` = **1400**
(chegara 2200), `WantedHashtags` = **12** (chegara 30). Model uzunlikni aniq hisoblay olmaydi va
biroz oshirib yuborishi odatiy hol — zaxirasiz har uchinchi natija qirqilardi. 30 ta hashtag esa
Instagram'da "spam" ko'rinadi.

⚠️ **Qaytadigan `Caption` — TAYYOR matn** (hashtaglar allaqachon oxiriga qo'shilgan), `Hashtags`
ro'yxati esa faqat **KO'RSATISH** uchun (chiplar). Ularni matnga qayta qo'shish **takror**
bo'lardi.

⚠️ **`NormalizeHashtag` da kamida bitta HARF/RAQAM bo'lishi SHART:** `#___` Instagram'da teg
emas va bizning `CountHashtags` sanog'iga ham kirmasdi — ya'ni chegara hisobi buzilardi.

⚠️ **Javob HAR DOIM HTTP 200**, muvaffaqiyat `ok` bayrog'ida. Sabab: xatolarning aksariyati
TASHQI va vaqtinchalik (kalit sozlanmagan, timeout, format buzuq) — ularni 4xx/5xx qilib
yuborish klientda "so'rov xato ketdi" degan **umumiy** matn chiqarardi, foydalanuvchiga esa
AYNAN sabab kerak. ⚠️ Klient `ok` ni tekshirmasa maydonga **BO'SH matn** qo'yib qo'yardi.

⚠️ **UI matn ustiga JIMGINA yozmaydi:** maydon bo'sh bo'lsa natija darhol qo'yiladi, matn BOR
bo'lsa avval ko'rsatiladi va foydalanuvchi «Almashtirish» yoki «Oxiriga qo'shish» ni O'ZI
tanlaydi — bir soatlik ishni bitta tugma o'chirib yuborishi mumkin edi.

⚠️ Uslub/til kalitlari (`friendly`, `uz-Latn` …) frontendda **qo'lda yozilmaydi** — ular
`GET content/caption/meta` dan keladi (`contacts.md` §6 dagi DRIFT sabog'i: kalit ikki joyda
yozilsa "tanlash ro'yxati bo'sh" degan jimgina nosozlik chiqadi).

### 18.9. Media o'lchovi — SERVER va BRAUZER birgalikda

Yuklashda server fayl **sarlavhasidan** o'lchaydi: JPEG kengligi/balandligi (SOFn markeri),
MP4/MOV davomiyligi (`mvhd` box'i — u ko'p enkoderlarda faylning OXIRIDA turadi, shuning uchun
bosh va oxirgi 256 KB ko'riladi).

🔴 **Server VIDEO KENGLIGI/BALANDLIGINI o'qimaydi va `0` qaytaradi.** `0` bu yerda
**«noma'lum»** degani va tegishli tekshiruv o'tkazib yuboriladi (`IgMediaItem` kelishuvi).

⚠️ Shu sababdan frontend server qiymatini **so'zsiz yozib qo'ymaydi**: `0` bo'lgan maydonlar
brauzer o'lchovi bilan (`<video>`/`<img>` metadata) to'ldiriladi. Aks holda to'g'ri o'lcham
yo'qolib, **Reels'ning 9:16 tekshiruvi umuman o'tkazib yuborilardi** va post `2207009` bilan
faqat joylash paytida yiqilardi.

Qoida: **server qiymati USTUN** (u faylning o'zidan o'qilgan, brauzerdan ishonchliroq), brauzer
esa faqat **bo'sh** joylarni to'ldiradi. Brauzer o'lchovi yiqilsa yuklash **BEKOR QILINMAYDI** —
fayl allaqachon serverda va manzil ishlaydi, o'lcham esa "noma'lum" bo'lib qoladi.

### 18.10. Testlar

| Test sinfi | Nimani qulflaydi |
|---|---|
| `InstagramPublishContractTests` (57) | Caption chegaralari (2200/30/20), JPEG-only, hajm/davomiylik/nisbat har tur uchun, o'lcham noma'lum bo'lsa nisbat tekshirilmasligi, karusel 2–10 va **bolasidagi caption XATO**, nisbat faqat birinchi element bo'yicha, konteyner so'rovi (image'da `media_type` yo'q, story'da caption yo'q), poll jadvali, 24 soat/10 daqiqa muddatlari, `QuotaExceeded` noma'lum limitda to'xtatmasligi, xato kodlari va **noma'lum kod jim yutilmasligi** |
| `InstagramPublishServiceTests` (26) | **Modul o'chiq → so'rov yo'q**, limit to'lganda `scheduled`, noma'lum limit ishni to'xtatmasligi, `IN_PROGRESS` **workerni bloklamasligi**, chop etilgan post qayta joylanmasligi, 3 urinishdan keyin `failed`, buzuq JSON, **validatsiyadan o'tmagan post tarmoqqa chiqmasligi**, o'lik token, karusel oqimi, **chop etish xatosida qayta urinilmasligi**, bir tsiklda 3 ta post |
| `IgPublishPayloadTests` | Buzuq JSON istisno otmasligi, **yo'q maydonlar standart qiymatga tushishi**, **nol `thumbOffset` saqlanishi**, yozib-o'qish davri |
| `InstagramCaptionTests` (24) | Gemini JSON'ini o'qish (fence, ortiqcha matn, buzuq javob), hashtag normalizatsiyasi va yaroqsizini tashlash, **matnda allaqachon bor teg qayta qo'shilmasligi**, `#ingliz` ≠ `#inglizcha`, **uzunlik oshsa AVVAL hashtaglar qirqilishi**, so'z chegarasida qisqartirish, mention chegarasida rad etish, **natija HAR DOIM `ValidateCaption` dan o'tishi**, bo'sh mavzuda **AI umuman chaqirilmasligi** |
| `MarketingPublicMediaTests` (33) | **Darvozasiz statik blok AYNAN BITTA**, sertifikat/selfi papkalari yopiqligi, MIME xaritasi yopiqligi, `AllowAnonymous` yo'qligi, **yo'ldan chiqish himoyasi**, auditga manzil yozilmasligi, `.jpg` niqobidagi HTML rad etilishi, JPEG o'lchami va MP4 davomiyligi parseri |

### 18.11. KONTENT UI — BITTA MODAL EMAS, ALOHIDA SAHIFALAR

Modul ilgari **bitta sahifa + katta modal** edi (900px'lik oynada media maydonlari, AI paneli va
Instagram ko'rinishi bir-birini itarardi). Endi u marshrutlarga bo'lingan:

```
/admin/marketing/kontent            Navbat (index)      ┐
              .../joylangan         Joylanganlar        ├─ ContentLayout, sub-nav = sahifa
              .../holat             Holat va limit      ┘   ICHIDAGI tugmalar (nav'da EMAS)
              .../yangi             Composer (to'liq ekran, 4 bosqich tugmasi)
              .../post/:id          O'sha composer, tahrirlash
```

Marshrutlar `App.tsx` da; hammasi `RequirePerm perm="marketing.content"` ostida.
⚠️ Composer layoutdan **TASHQARIDA** (`kontent` ning bolasi emas, aka-uka marshrut) — uzun forma
yonida sub-nav va sahifa sarlavhasi faqat joyni egallardi.

**Fayl tuzilishi** — `pages/admin/marketing/content/`:

| Fayl | Vazifasi |
|---|---|
| `ContentLayout` | o'rovchi: sub-nav, «Yangilash», «Yangi post», diagnostika bannerlari, `Outlet` konteksti |
| `ContentQueue` | Navbat (index) — kelajak: kalendar + kun bo'yicha guruhlangan ro'yxat |
| `ContentPublished` | Joylanganlar — o'tmish: **galereya** (Instagram profilining o'zi galereya) |
| `ContentStatus` | Holat va limit — «nega chiqmayapti», kunlik limit, media talablari |
| `ContentComposer` | Post muharriri (to'liq ekran, 4 bosqich) |
| `MediaEditor` · `CaptionAi` · `IgPreview` | composer bo'laklari: media, AI matn, «telefon» ko'rinishi |
| `helpers.ts` | UMUMIY SOF FUNKSIYALAR — holat ranglari, sana formati, `loadAllPosts`. ⚠️ JSX YO'Q (eslint `react-refresh/only-export-components`, `lib/month.ts` bilan bir xil konvensiya) |

Sub-nav sanoqlari `GET content/status` dan (`scheduled + processing`, `failed`) — u FAQAT bazadan
o'qiydi, ya'ni har sub-sahifada chaqirish xavfsiz. **Kunlik limit** endpointi esa har chaqirilganda
Meta'ga so'rov yuboradi va ATAYIN faqat «Holat va limit» sahifasida.

#### 🔴 `status` filtri SERVERGA YUBORILMAYDI — kesim KLIENTDA

`ContentQueue` ham, `ContentPublished` ham `loadAllPosts({ …, status: 'all' })` bilan so'raydi va
holat bo'yicha filtrni **xotirada** qo'llaydi.

NEGA: backend jamlanmani (`IgPostTotals`) **status filtri QO'LLANGANDAN KEYIN** hisoblaydi
(`InstagramController.Content.cs` → `ContentPosts`), ya'ni `status=published` bilan so'ralsa
`failed` maydoni har doim **0** chiqadi — KPI raqamlari yolg'on nol ko'rsatardi. Loyihadagi qattiq
qoida: ekrandagi ikki son bir-biriga mos kelishi SHART. Yon ta'siri ham foydali — kesim
almashganda qayta so'rov ketmaydi.

⚠️ Kun bo'yicha filtr ham KLIENTDA: kalendar katagidagi son va ro'yxat AYNAN bitta manbadan
chiqsin (aks holda katakda «3» turib, ro'yxatda 2 ta post ko'rinardi).

#### ⚠️ `loadAllPosts` qatorlarni `id` bo'yicha DEDUP qiladi

Oylik ro'yxat `MAX_PAGES` (4 × 50 = 200 post) gacha ketma-ket sahifalanadi va `Set<id>` bilan
dublikatlar tashlanadi.

NEGA: backend `OrderByDescending(p => p.ScheduledAt)` + `Skip/Take` bilan sahifalaydi va
**IKKILAMCHI tartiblovchisi YO'Q** — bir xil `ScheduledAt` li postlarning tartibi so'rovdan
so'rovga o'zgarishi mumkin. Sahifa chegarasida (50 ga karrali) bitta post IKKI MARTA kelishi yoki
umuman TUSHIB QOLISHI mumkin edi; birinchisi React `key` dublikatini va kalendarda yolg'on sonni
berardi.

🔧 **OCHIQ ISH — asl yechim BACKENDDA:** `ContentPosts` so'roviga `.ThenByDescending(p => p.Id)`
qo'shilishi kerak (hali qo'shilmagan). Klientdagi dedup — arzon himoya, sabab emas.

⚠️ `truncated` DEDUPDAN KEYINGI songa qarab hisoblanadi va ro'yxat ostida OCHIQ yoziladi —
sahifalash beqarorligi tufayli tushib qolgan post jimgina yo'qolmasin.

#### 🔴 DIAGNOSTIKA OGOHLANTIRISHLARI — `ContentLayout` da, HAMMA sub-sahifada

«Instagram akkaunti ulanmagan» va «Chop etish moduli o'chiq» bannerlari `<Outlet />` USTIDA
chiziladi (`ContentAlerts`).

NEGA: ilgari ular faqat «Holat va limit» sahifasida, xotirjam kartochka ko'rinishida edi —
natijada modul o'chiq bo'lganda operator **Navbatda post yaratar, yashil «Post navbatga qo'shildi»
xabarini olar va postlar HECH QACHON chiqmasdi**. Sub-nav'dagi yagona sanoq `failed`, modul o'chiq
bo'lganda esa u ham 0 (post umuman urinilmaydi) — ya'ni ekranda birorta belgi qolmasdi.

⚠️ SCOPE ogohlantirishi (sariq) faqat akkaunt ULANGAN **va** modul YOQILGAN bo'lganda chiziladi:
modul o'chiq bo'lsa «scope noma'lum» ikkinchi darajali gap bo'lib, haqiqiy sababni shovqin bilan
ko'mib qo'yardi.

⚠️ Diagnostikani o'qish XATOSI esa layoutda CHIZILMAYDI (har sahifa tepasida takrorlanadigan
qizil chiziq bo'lardi) — matn kontekst orqali «Holat va limit» sahifasiga uzatiladi, u yerda bu
ma'lumot sahifaning MAZMUNI. Jim yutilmaydi.

#### ⚠️ Composer → Navbat KONTRAKTI: `location.state`

Composer saqlagandan keyin `navigate(QUEUE, { state: { mkNotice, month? } })` bilan qaytadi:

| Maydon | Nima |
|---|---|
| `mkNotice` | Navbat tepasida chiqadigan yashil xabar («Reja yangilandi», «Post navbatga qo'shildi») |
| `month` | `"yyyy-MM"` — post rejalashtirilgan oy |

⚠️ `month` NEGA kerak: Navbat har ochilganda JORIY oyni ko'rsatadi. Sentabrga rejalashtirilgan
postni tahrirlab qaytgan odam **o'z postini ko'rmasdi** («saqlandi» deydi, ro'yxatda yo'q).
Buzuq/bo'sh `month` umuman yuborilmaydi va Navbat uni baribir jim tashlaydi.

#### 🔴 `/yangi` va `/post/:id` — SIBLING marshrutlar, komponent QAYTA MOUNT BO'LMAYDI

Ikkala marshrut ham AYNAN bitta elementni chizadi, ya'ni React Router uni qayta ishlatadi
(unmount/mount YO'Q). Shuning uchun yuklash effektida **`else` tarmog'i MAJBURIY**: `id` yo'q
bo'lsa forma **majburan bo'sh holatga tiklanadi** (post, tur, matn, media, sozlamalar, vaqt,
`baseline`, fayl va AI jarayonlari).

Busiz: A postini tahrirlab, brauzerning «Orqaga» tugmasi bilan `/yangi` ga o'tgan odam A ning
to'ldirilgan formasini ko'rardi va `post` hamon A bo'lgani uchun «Saqlash» YANGI post yaratmay,
**A ni USTIGA YOZARDI**.

⚠️ `?kun=` (kalendardan kelgan kun) AYNAN shu tiklashda qo'llanadi. Ilgari alohida «faqat mount»
effekti bor edi va ikkisi TO'QNASHARDI (tiklash vaqtni bo'shatar, mount effekti qayta yozardi).

⚠️ Faol bosqich URL'da (`?bosqich=matn`, `replace: true`) — sahifa yangilansa yoki havola
ulashilsa o'sha joy ochiladi, brauzer tarixi esa bosqichlar bilan to'lmaydi.

⚠️ Bosqichlar **SEHRGAR (wizard) EMAS** va qulflanmaydi: tahrirlashda odam ko'pincha BITTA
narsani (masalan vaqtni) o'zgartirgani keladi. Shuning uchun "uzoq" holat (fayl yuklash, AI
so'rovi) bosqich komponentlarida EMAS, `ContentComposer` ning O'ZIDA saqlanadi — bosqich almashuvi
komponentni unmount qiladi va 40 MB video yuklanayotgan jarayon jimgina yo'qolardi.

#### `--mk-head-h` — yopishqoq sarlavha balandligi YAGONA MANBADAN

`styles/marketing.css` dagi CSS o'zgaruvchi. Undan hisoblanadi: `.inbox` balandligi
(`100vh − 113px − var(--mk-head-h) − var(--mk-head-gap)`), `.mk-day-head` ning sticky `top` i va
`[id]` elementlarining `scroll-margin-top` i.

| Holat | Qiymat |
|---|---|
| sub-nav'siz sahifa | `79px` |
| sub-nav bor sahifa (`.mk-shell:has(.mk-subnav)`) | `131px` |
| mobil (`@media`) | `72px` / `124px` |

⚠️ **Sarlavha tuzilishini o'zgartirsangiz shu o'zgaruvchini ham yangilang** — ilgari har joyda o'z
konstantasi bor edi va ular DRIFT qilgan: natijada sahifada IKKINCHI skroll paydo bo'lar yoki kun
sarlavhasi sahifa sarlavhasi ostiga kirib ketardi.

#### Marketing sahifalari — TO'LIQ KENGLIKDA va TABLARGA bo'lingan

- `MarketingPage` dan **`full` propi OLIB TASHLANDI**, `content-narrow` o'chirilgan: sahifa DOIM
  to'liq kenglikda. Sabab — navbat, galereya, jadval va composer tor ustunga sig'masdi.
- **Sozlamalar** (`InstagramSettings`) va **Reklama statistikasi** (`InstagramAdsStats`) sahifa
  ichidagi tab tugmalariga bo'lingan; faol tab manzilda saqlanadi — **`?bolim=…`** (standart tabda
  parametr umuman yozilmaydi). Havola nusxa qilinsa yoki sahifa yangilansa o'sha joy ochiladi.

---

## 19. CAPI — LID SIFATINI META'GA QAYTARISH

Migratsiya: `AddMarketingExpansion`. Entity `IgCapiEvent`, servis `MetaCapiService`,
mijoz `MetaCapiApi`, sof funksiyalar `MetaCapiHash` + `MetaCapiPayload`.
Sozlash qo'llanmasi: `instagram/CAPI.md`.

`CenterMeta`: `InstagramCapiEnabled` (**default false**), `InstagramCapiDatasetId`,
`InstagramCapiToken`, `InstagramCapiStageQualified` (`"Sifatli lid"`),
`InstagramCapiStageWon` (`"To'lov qildi"`).

### 19.1. 🔴 YANGI HOOK YOZILMAYDI — KUNLIK SKAN

`Lead.Stage` o'zgarishini ushlash uchun hodisa tinglovchisi qo'shish vasvasasi bor, lekin lid
holati bir necha joydan o'zgaradi (kanban, konvertatsiya, kassa) va **bittasi tushib qolsa
hodisa JIMGINA yo'qolardi**. Kunlik skan "hozirgi holat"ni qayta hisoblaydi — o'tkazib
yuborilgan o'zgarish keyingi kuni o'z-o'zidan tuziladi.

Skan oynasi `ScanWindowDays` = **90 kun** (Meta talabi 28 kun — uch barobar zaxira).

⚠️ FAQAT **reklama formasidan** kelgan lidlar (`IgAdLead.LeadgenId` bor): `lead_id` siz Meta
hodisani hech qanday e'longa bog'lay olmaydi. DM/izoh lidi bu navbatga **umuman tushmaydi**.
⚠️ `CreatedTime` bo'sh/buzuq bo'lsa `ReceivedAt` bo'yicha ham tekshiriladi — vaqti yozilmagan
lid jimgina tushib qolmasin.
⚠️ Bitta CRM lidiga bir necha reklama lidi to'g'ri kelsa **FIRST-TOUCH**: eng birinchisi
olinadi (`LeadIntake` bilan bir xil qoida), ya'ni konversiya BIRINCHI e'longa yoziladi.

### 19.2. 🔴 DEDUP KALITI — `(LeadId, EventName)`, `EventId` EMAS

`EventId` = `"{leadgenId}_{unix}"`, ya'ni **ichida VAQT bor** va u har kuni boshqacha chiqadi.
Agar dedup unga tayansa **bir xil holat HAR KUNI qayta yuborilardi** va unikal indeks ham
saqlamasdi. Shuning uchun:

| Qavat | Kalit | Vazifasi |
|---|---|---|
| 1 (skan) | `(LeadId, EventName)` | Bir xil holat ikkinchi marta navbatga tushmaydi |
| 2 (baza) | `IgCapiEvent.EventId` **unikal indeksi** | Poyga holati (`DbUpdateException` **ushlanadi va XATO EMAS**) |
| 3 (Meta) | `event_name` + `event_id`, **48 soat** | Qayta yuborish xavfsiz — Meta takrorni o'zi tashlaydi |

⚠️ Unikal indeks buzilganda qator **`Remove` bilan kuzatuvdan chiqariladi**: muvaffaqiyatsiz
`Added` yozuv qolib ketsa keyingi HAR BIR `SaveChanges` yiqilardi (`Entry()` `IAppDbContext`
da yo'q).

### 19.3. 🔴 VAQT TUZOQLARI

**`AppClock.Now` `Kind=Unspecified`** — "devor soati"ni qaytaradi. Uni to'g'ridan-to'g'ri
`DateTimeOffset` ga bersak .NET **SERVER mintaqasini** qo'llaydi; Docker'da bu **UTC**, ya'ni
natija **5 soatga kelajakka** siljib, Meta hodisani rad etardi. Shuning uchun Toshkent ofseti
(`+05:00`) `MetaCapiPayload.ToUnix` da **QO'LDA** biriktiriladi.

**`event_time` 7 kundan eski bo'lsa BUTUN so'rov rad etiladi:**

| Chegara | Qiymat | Nega |
|---|---|---|
| Eng eski | 7 kun **− 1 soat zaxira** | Meta chegarani O'Z soati bo'yicha tekshiradi; "roppa-rosa 7 kun" dagi hodisa yo'lda (navbat + qayta urinish) chegaradan chiqib ketardi |
| Kelajak | +5 daqiqa | Server soatining siljishi |

Eskirgan qator **`skipped`** bo'ladi (`failed` EMAS — admin muammo izlab yurmasin) va yuborish
paytida ham paketdan **chiqarib tashlanadi**: aks holda u o'zi bilan qolgan 999 tasini
yiqitardi.

⚠️ **"Sifatli lid" vaqti — SKAN vaqti** (bosqichga o'tishning aniq soati saqlanmaydi, eng ko'p
24 soat kechikish), **"To'lov qildi" vaqti — BIRINCHI TO'LOV SANASI** (kun boshi, Toshkent):
Meta hodisani atributsiya oynasiga aynan shu vaqt bo'yicha joylashtiradi. Demak modul birinchi
marta yoqilganda **eski to'lovlar ATAYIN yuborilmaydi** — "bugun to'ladi" deb yuborish yolg'on
ma'lumot bo'lardi. Kun OXIRI olinmaydi: bugungi to'lov kelajakda turib qolardi.

### 19.4. 🔴 HASHLASH VA U+02BB APOSTROF TUZOG'I

SHA-256 → hex → **kichik harf**. Meta hashni bayt-ma-bayt solishtiradi: bir belgi farq qilsa
moslik **0** bo'ladi va nosozlik **jimgina** yuz beradi (200 OK keladi, hodisa hech kimga
bog'lanmaydi).

| Maydon | Normallashtirish |
|---|---|
| `ph` | faqat raqamlar, boshidagi nollar olib tashlanadi, **9 xonali raqamga `998` qo'shiladi**; uzunlik 10–15 bo'lmasa **bo'sh** qaytadi |
| `em` | trim + lowercase + eng oddiy shakl tekshiruvi |
| **`lead_id`** | 🔴 **HASHLANMAYDI**, `long` ga aylanmasa maydon **umuman qo'shilmaydi** (satr ko'rinishidagi `lead_id` butun so'rovni yiqitardi) |

⚠️ **`PhoneUtil.Key` bu yerda ISHLATILMAYDI** — u ataylab oxirgi 9 raqamni (mamlakat kodisiz)
beradi, CAPI uchun esa talab AYNAN TESKARI.

🔴 **`char.IsLetterOrDigit('ʻ')` → TRUE.** U+02BB (o'zbek klaviaturasining asosiy apostrofi) va
U+02BC Unicode'da **`ModifierLetter`**, ya'ni HARF hisoblanadi va oddiy filtrdan **o'tib
ketadi**. Natijada `To'lqin` (ASCII) va `To’lqin` (U+2019) tashlanardi-yu, `Toʻlqin`
tashlanmasdi — bitta odam **uchta xil hash** berardi. Shuning uchun `IsNameChar` modifikator
harflarni ATAYIN chiqarib tashlaydi.

⚠️ **`fn`/`ln` YUBORILMAYDI:** O'zbekistonda formaga "Familiya Ism" ham, "Ism Familiya" ham
yoziladi va tartibni aniqlashning ishonchli yo'li yo'q. Noto'g'ri joylashgani moslikni
**oshirmaydi**, lekin Meta hisobotida "sifatsiz integratsiya" bo'lib ko'rinardi.

🔴 **MAXFIYLIK — tuzilma darajasidagi kafolat:** `IgCapiEvent.PayloadJson` faqat
`MetaCapiPayload.BuildEvent` orqali quriladi, u esa **`MetaCapiUserData`** (hashlangan record)
dan boshqasini qabul qilmaydi — ya'ni xom PII yozishning **texnik imkoni yo'q**. Xom telefon/
email faqat `Lead` jadvalida qoladi (DPA aynan shuni tekshiradi).
⚠️ `access_token` **tanaga qo'shilmaydi** — u manzilda ketadi (payload log/bazaga tushishi
mumkin, token esa hech qachon).
⚠️ `test_event_code` produksiyada **berilmaydi**: u bilan kelgan hodisalar faqat sinov oynasida
ko'rinadi va optimizatsiyaga umuman qo'shilmaydi — modul "ishlayotgandek" ko'rinib, aslida
hech narsa qilmasdi.

### 19.5. Hodisa xaritasi va nomlar

| CRM'da nima bo'ldi | Hodisa | Vaqt |
|---|---|---|
| Reklama lidi yaratildi | ❌ **yuborilmaydi** (Meta buni allaqachon biladi) | — |
| `ConvertedStudentId` to'ldi **YOKI** kanban bosqichi "sifatli" | `InstagramCapiStageQualified` | skan vaqti |
| Birinchi `tuition` to'lovi | `InstagramCapiStageWon` + `value` + `currency: UZS` | **birinchi to'lov sanasi** |

🔴 **`event_name` — ERKIN MATN** (qat'iy enum faqat Business Messaging CAPI'da). Yagona shart:
Events Manager'dagi bosqich nomi bilan **AYNAN** bir xil. Shuning uchun nomlar `CenterMeta`
sozlamasida — markaz nomni o'zgartirsa kod qayta yig'ilmaydi. Bo'sh sozlamada default nom
ishlatiladi (bo'sh `event_name` bilan ketgan so'rovni Meta rad etardi).

⚠️ Kanban bosqichi **NOM bo'yicha** taniladi (`IsQualifiedStage`: `sifatli`, `sinov`, `trial`,
`qualified`, `aylantir`, `convert`): `LeadStage` da "tur" ustuni YO'Q, ya'ni id bo'yicha
bog'lab bo'lmaydi. Markaz boshqacha nomlagan bo'lsa hodisa baribir `ConvertedStudentId` orqali
yuboriladi — ro'yxat **qo'shimcha signal**, yagona shart emas.

⚠️ Ikki manba (konvertatsiya va bosqich) **BITTA** hodisa beradi — Events Manager'da ham
bosqich bitta.

### 19.6. Navbat va API

Holatlar: `pending` · `sent` · `failed` · `skipped` (`MaxAttempts` = 3,
`MaxPerRun` = 5 × 1000, bir so'rovda ≤ **1000** hodisa).

⚠️ **Xato bo'lgan paket YO'QOLMAYDI** — qatorlar `pending` bo'lib qoladi va keyingi ishga
tushishda qayta yuboriladi (deterministik `event_id` buni xavfsiz qiladi).
⚠️ Yuboriladigan hodisa **`PayloadJson` dan TIKLANADI**, xom ma'lumotdan emas: lidning
telefoni/summasi orada o'zgargan bo'lsa ham hodisa o'zgarmaydi — aks holda `event_id` bir xil,
mazmuni boshqa hodisa ketardi.
⚠️ `fbtrace_id` **muvaffaqiyatda ham** logga yoziladi: Meta qo'llab-quvvatlash xizmati usiz
gaplashmaydi ("hodisa yuborilgan, lekin ko'rinmayapti").
⚠️ Meta **200 qaytarib**, hodisalarning bir qismini jimgina tashlashi mumkin —
`events_received < yuborilgan` bo'lsa `LogWarning`.

| Verb + route | Ruxsat |
|---|---|
| `GET capi/status`, `GET capi/events` | klass (⚠️ Dataset ID va token **QIYMATI qaytmaydi**, `PayloadJson` ham) |
| `PUT capi/settings` | `marketing.settings` |
| `POST capi/send` | `marketing.settings` (xatoda ham **HTTP 200**) |

⚠️ **`PUT capi/settings` da Dataset ID token bilan BIR XIL qoidada:** bo'sh kelsa mavjudi
saqlanadi. Ilgari u SHARTSIZ yozilardi, qiymat esa javobga tushmaydi (forma har safar bo'sh
ochiladi) — faqat toggle'ni o'zgartirgan admin Dataset ID'ni **bilmasdan o'chirib qo'yardi** va
CAPI jimgina ishlamay qolardi.
⚠️ Auditga **token yozilmaydi**, **Dataset ID yoziladi** (sir emas, Page ID bilan bir xil
maqom): "qaysi datasetga ulandik" savoli tarixdan javobsiz qolmasin.

### 19.7. Testlar

| Test sinfi | Nimani qulflaydi |
|---|---|
| `MetaCapiHashTests` | Telefon ma'lum SHA-256 qiymati, turli formatdagi raqam **bir xil hash**, email trim+lowercase, **apostrofli ism** (uchala apostrof), yaroqsiz qiymat → bo'sh satr |
| `MetaCapiPayloadTests` | **`lead_id` hashlanmasligi va xom raqam bo'lishi**, raqam bo'lmasa payloadga tushmasligi, **payloadda xom telefon/email yo'qligi**, bo'sh maydon qo'shilmasligi, `event_id` deterministikligi, **tanada token yo'qligi**, 7 kun va kelajak chegarasi, **`ToUnix` Toshkent ofseti**, 1000 talik bo'laklar |
| `MetaCapiServiceTests` (9) | **Modul o'chiq → so'rov yo'q**, to'lov hodisasi yaratilishi, **lid yaratilgani uchun hodisa YO'Q**, sinov darsi bosqichi, **bir xil holat ikki marta yuborilmasligi**, eski hodisa `skipped` bo'lib **paketni yiqitmasligi**, `SendPending` yangi qator yaratmasligi |

---

## 20. REKLAMA IZOHI ATRIBUTSIYASI (E3) VA E6 YAXSHILANISHLARI

### 20.1. 🔴 ATRIBUTSIYA — TAXMINIY, hech qayerda "aniq" deb ko'rsatilmaydi

**Muammo:** Instagram Login yo'lidagi `comments` webhook'ida **`ad_id` UMUMAN YO'Q** — u faqat
Facebook Login yo'lidagi payloadda bor (`value.media.ad_id`). Bizda faqat `value.media.id`.

**Yechim (bilvosita):** §17 dagi iyerarxiya sinxronizatsiyasi har e'lon uchun
`IgAdEntity.CreativeStoryId` (= `effective_object_story_id`) ni saqlaydi — bu reklama ostidagi
HAQIQIY post identifikatori. Izoh kelganda `media.id` shu ustun bilan solishtiriladi va
topilsa `IgConversation`/`IgMessage` ga `AdId` + `AdCampaignId` yoziladi.

| Holat | Ishlaydimi | Nega |
|---|---|---|
| **Boostlangan organik post** | ✅ | Post bizning media ro'yxatimizda bor, creative aynan unga ishora qiladi |
| **Dark post** (chop etilmagan reklama) | ❌ | Bunday post akkaunt lentasida yo'q |
| **Dinamik (katalog) reklama** | ❌ | Meta hujjati ochiq aytadi: dinamik reklamada `ad_id` **umuman qaytarilmaydi** |

🔴 **Bo'sh natija "reklamadan kelmagan" degani EMAS, "aniqlanmadi" degani.** Shuning uchun bu
qiymat hech qayerda "aniq atributsiya" sifatida ko'rsatilmasligi kerak — chizilganda yoniga
**"taxminiy"** deb yozilishi SHART.

⚠️ **EKRANGA CHIQARADIGAN ODAM UCHUN QOIDA:** `AdId`/`AdCampaignId` — bu **taxmin**, ya'ni
uni DTO'ga qo'shganda ham, Inbox'da chizganda ham **"taxminiy" belgisisiz ko'rsatmang** va bo'sh
qiymatni "organik" deb yozma ("aniqlanmadi" de). Hisobotda esa bu qiymatga tayanib
"reklama N ta izoh keltirdi" degan **aniq** son chiqarish mumkin emas.

### 20.1.1. KO'RINADIGAN QISM (2026-08-21)

| Qayerda | Nima |
|---|---|
| `IgConversationDto` · `IgConversationDetailDto` | `adId`, `adCampaignId`, **`adCampaignName`** |
| Inbox ro'yxati | qatorda `📢 <kampaniya> · taxminiy` chipi (tooltip bilan) |
| Inbox detali | `📢 Reklama izohidan kelgan — taxminiy` bloki: kampaniya, e'lon id va **cheklovlar ochiq yozilgan** |
| Filtr | `GET /conversations?source=ads` + «📢 Reklamadan (taxminiy)» chipi |

⚠️ **"taxminiy" so'zi TOOLTIPDA EMAS, MATNDA** — tooltip sichqonchasiz qurilmada umuman
ko'rinmaydi va yagona kanal bo'lib qola olmaydi.

⚠️ **KAMPANIYA NOMI — BITTA so'rovda** (`AttachCampaignNamesAsync`): sahifadagi takrorsiz
kampaniya id'lari yig'ilib, bitta `IN (...)` bilan olinadi. Qator boshiga so'rov (N+1)
TAQIQLANADI — inbox sahifasida 100 tagacha suhbat bo'ladi. Atributsiya umuman bo'lmasa
qo'shimcha so'rov **ketmaydi ham**.

⚠️ Nom topilmasa **id'ning O'ZI** ko'rsatiladi (`InstagramContract.AdCampaignLabel`,
`MetaAdsRoi.BuildNode` bilan bir xil qoida) — sun'iy "Noma'lum kampaniya" yozilmaydi.

⚠️ `?source=` da **noma'lum qiymat filtrni UMUMAN qo'llamaydi** (`WantsAdsOnly`) — klientdagi
xato kalit tufayli inbox bo'shab qolib, operator "suhbat yo'q" deb o'ylab qolmasin.

⚠️ `effective_object_story_id` odatda **`"{page_id}_{post_id}"`** ko'rinishida, webhook'dagi
`media.id` esa **yalang id** — to'g'ridan-to'g'ri solishtirish HECH QACHON mos kelmasdi.
`MediaPart` oxirgi `_` dan keyingi qismni oladi; `Matches` uch xil moslikni qabul qiladi
(aynan teng · prefiksli creative · prefiksli media).

⚠️ **Tanlov DETERMINISTIK:** bitta post bir necha e'londa bo'lishi mumkin (A/B test, qayta
boost). Avval `ad` darajasi, keyin `ExternalId` bo'yicha **ordinal** tartibda birinchisi
olinadi — aks holda bir xil izoh har safar boshqa e'longa biriktirilib, hisobot **beqaror**
bo'lardi.

⚠️ **Bu QO'SHIMCHA baza so'rovi, ya'ni YORDAMCHI vazifa** — yiqilsa asosiy ish (mijozga javob
berish va xabarni yozib qo'yish) BARIBIR bajariladi (§11 "xatolarga chidamlilik").
⚠️ Reklama statistikasi ulanmagan markazda `IgAdEntities` bo'sh — har izohda bekorga so'rov
ketmasin, **mavjudlik tekshiruvi 5 daqiqaga keshlanadi**; nomzodlar `MaxCandidates` = 50 bilan
cheklangan.

### 20.2. E6 — kiruvchi hodisalarning yangi turlari

| Nima | Qayerdan | Nima qilinadi |
|---|---|---|
| **Story javobi** | `message.reply_to.story.{id,url}` | Kontekst xabar matniga qo'shiladi. ⚠️ `reply_to` ODATIY xabarga javobda ham keladi (u yerda faqat `mid`) — story konteksti FAQAT ichki `story` obyekti bo'lganda |
| **Story mention** | `attachments[].type == "story_mention"` | Alohida belgilanadi: bu "javob" emas, **eslatish** — mijoz o'z story'sida bizni belgilagan |
| **Ulashilgan post** | `attachments[].type == "ig_post"` | ⚠️ Eski **`share`** turi **2026-02-01 da OLIB TASHLANGAN** |
| **Xabar o'chirildi** | `message.is_deleted` | Alohida hodisa turi; matn **HAQIQATAN o'chiriladi** (`IgMessage` va suhbatdagi denormalizatsiya ham) — Platform Terms talabi |
| **Siyosat ogohlantirishi** | `messaging_policy_enforcement` | §20.3 |

⚠️ Story rasmining CDN manzili **TEZ O'LADI** (story 24 soatda yo'qoladi, imzolangan havola
undan ham tez) — manzil saqlanadi, lekin unga tayanib bo'lmaydi; operator hech bo'lmaganda
"qaysi story haqida gap ketyapti" ni ko'radi. Story id/url uchun **alohida ustun YO'Q**
(migratsiya bu ish doirasidan tashqarida) — kontekst matnga qo'shiladi, aks holda "Salom!"
degan story javobi butunlay tushunarsiz bo'lardi.

### 20.3. `messaging_policy_enforcement` — 🔴 JORIY YO'LDA KELMAYDI (kod zaxira sifatida qoladi)

🔴 **2026-08-22 da aniqlandi:** bu hodisa **"Instagram Messaging API (Messenger Platform)"**
yo'liga tegishli. Loyiha esa **"Instagram API with Instagram Login"** yo'lida
(`graph.instagram.com`, `instagram_business_*` scope'lari) va Meta ruxsat bergan webhook
maydonlari ro'yxatida `messaging_policy_enforcement` **YO'Q** — ya'ni unga **obuna bo'lib ham
bo'lmaydi** va hodisa **hech qachon kelmaydi**.

⚠️ Shuning uchun bu **"cheklovdan oldingi yagona signal" EMAS** — ilgari shu yerda va kod
izohlarida shunday yozilgan edi va bu **xato tasavvur** berardi ("bizda ogohlantirish bor").
Meta cheklovini bilishning haqiqiy yo'llari: **Graph xato kodlari** (`10`, `200`, `368`,
`551`) va **Meta konsolidagi ogohlantirishlar**.

Kod **O'CHIRILMADI**: hodisa kelib qolsa to'g'ri ishlaydi va zarar qilmaydi. Lekin unga
**tayanib bo'lmaydi** — yangi funksiya bu signalga bog'lanmasin.

Kelgan taqdirda **ikki narsa DARHOL** bajariladi:

1. **Avtomatika pauza qilinadi** — `InstagramAutoReplyComments` va `InstagramAutoReplyDm`
   o'chiriladi;
2. **Telegram alert** — admin sababni ko'rib, **QO'LDA** qayta yoqadi.

⚠️ **`InstagramEnabled` ATAYIN O'CHIRILMAYDI.** Ikki sabab: (1) u MASTER darvoza —
o'chirilsa `NotifyAdminsAsync` ham jim bo'lardi va **ogohlantirish hech kimga yetmasdi**;
(2) u bilan birga navbat qayta ishlash to'xtardi, ya'ni kelayotgan xabarlar **tarixga
yozilmay** qolardi. Pauza faqat AVTOMATIK JAVOBGA tegadi — operator qo'lda javob bera oladi.

⚠️ **Qayta yoqish QO'LDA:** "N soatdan keyin o'zi yonsin" varianti sababni tekshirmasdan o'sha
xatoni takrorlashga olib kelardi.

⚠️ **Maydon nomi bir xil emas:** Meta hujjatida `messaging_policy_enforcement`, hodisa obyekti
esa `policy-enforcement` (**DEFIS** bilan) kaliti ostida keladi. Parser ATAYIN kechirimli —
**uchala yozilishni** ham qabul qiladi (kelib qolgan taqdirda shakl tufayli boy berilmasin).
⚠️ Meta bu hodisada **`mid` bermaydi**, shuning uchun `EventKey` `action|reason` ning barqaror
hash'idan quriladi (§5 deterministiklik qoidasi). Shakl kutilmagan bo'lsa ham signal
qolishi uchun `action` bo'sh bo'lganda `"warning"` yoziladi.
⚠️ `ContainsPolicyEnforcement` — **webhook controlleri** uchun arzon tekshiruv: so'rov kelgan
zahoti logga yozish imkonini beradi (navbat fon xizmatida qayta ishlanadi, modul o'chiq bo'lsa
esa umuman ishlanmaydi).

### 20.4. Testlar

| Test sinfi | Nimani qulflaydi |
|---|---|
| `InstagramContractTests` (E3 qismi) | `WantsAdsOnly` faqat `ads` kalitida ishlashi va **noma'lum qiymat filtrni qo'llamasligi**, `FromAd`, `AdCampaignLabel` nom → id → e'lon id zanjiri |
| `MetaAdsRoiTests` (E3/E5 qismi) | `MsgStarted` daraxt bo'ylab ko'tarilishi va **lidlarga qo'shilmasligi**, atributsiya oynasi Meta bergan holicha qaytishi, bir necha qiymat **hammasi** sanalishi, bo'sh davr/akkauntsiz holat |
| `IgAdAttributionTests` | `MediaPart` ajratishi (`{page}_{post}` va yalang id), buzuq `"abc_"` da to'liq qiymat qolishi, mos kelmagan/bo'sh kirishda **moslik BERILMASLIGI**, bir post bir necha e'londa bo'lganda **tanlovning DETERMINISTIKLIGI**, `ad → adset → campaign` zanjiri |
| `InstagramEventParserTests` (E6 qismi) | Story javobi id+url bilan o'qilishi, **oddiy xabarga javob story deb hisoblanmasligi**, story mention ajratilishi, `ig_post` attachment, **eski `share` turi qabul qilinmasligi**, `is_deleted` alohida hodisa turi berishi |

---

## 21. BILIM BAZASI RAG (E6.5) VA JAVOB SIFATI JURNALI (E6.6)

Migratsiya: **`AddMarketingRagAndQuality`** (§17–§20 dan AYRI — ular `AddMarketingExpansion` da).
Ikkala modul ham **mavjud AI agentini yaxshilaydi**, yangi ekran yoki yangi bayroq qo'shmaydi.

### 21.1. 🔴 RAG — muammo va yechim

**Ilgari:** `LoadKnowledgeAsync` barcha faol bo'laklarni ketma-ket qo'shib, natijani
`IgConst.KnowledgeLimit` (**12000** belgi) da **KESARDI**. Bilim bazasi o'sganda oxirgi
bo'laklar promptga **umuman tushmasdi** va AI "bunday ma'lumot yo'q" deb operatorga o'tkazardi.
Nosozlik **jimgina** edi: u faqat "AI bilmayapti" shikoyati orqali ko'rinardi.

**Endi:** har bo'lakning Gemini embedding vektori saqlanadi (`IgKnowledge.EmbeddingJson`),
savol ham vektorga aylantiriladi va **kosinus** bo'yicha eng yaqin `TopN` = **6** bo'lak
tanlanadi.

🔴 **YANGI KUTUBXONA YO'Q** — `pgvector` ham. Vektor **JSON matn** sifatida saqlanadi, kosinus
oddiy C# tsiklida hisoblanadi: bilim bazasi o'nlab bo'lakdan iborat, ya'ni bitta so'rovda bir
necha ming ko'paytirish — **o'lchanadigan yuk emas**.

### 21.2. 🔴 ZAXIRA YO'L — RAG modulni HECH QACHON to'xtatmaydi

`LoadKnowledgeAsync(db, ct, query)` da tanlov `null` qaytsa **ESKI xatti-harakat** ishlaydi
(butun bilim bazasi + `KnowledgeLimit`). `null` qaytadigan holatlar:

| Holat | Nega zaxiraga o'tiladi |
|---|---|
| `query` bo'sh | Savolsiz "yaqin bo'lak" tushunchasi yo'q |
| `CanUseRag == false` | §21.3 |
| Gemini kaliti sozlanmagan | Tarmoqqa umuman chiqilmaydi |
| Embedding so'rovi yiqildi (timeout, kvota, format) | Vektorsiz taqqoslash mumkin emas |
| Hech bir bo'lak `MinScore` dan o'tmadi | Savol bilim bazasidagi hech qaysi mavzuga tegmagan |

⚠️ **Yechim faqat "yaxshilash", majburiyat EMAS.** Vektorlar butunlay yo'q bo'lsa ham modul
avvalgidek ishlaydi — bu `IgKnowledge` entity izohida ham qat'iy yozilgan.

### 21.3. 🔴 `CanUseRag` ATAYIN QAT'IY

Ikki shart: (1) bo'laklar soni `TopN` dan **KO'P** (kamroq bo'lsa hammasini yuborish ham arzon,
ham xatosiz); (2) **HAR BIR** faol bo'lakning vektori bor.

⚠️ (2) — eng muhim qaror. Yangi qo'shilgan, hali embedding qilinmagan bo'lak **aynan savolga
javob** bo'lishi mumkin. Yarim tayyor bazada RAG ishlatilsa u **JIMGINA tashlab ketilardi** va
sabab hech qayerda ko'rinmasdi. Fon xizmati bir necha soniyada yetib olgach RAG **o'zi**
yoqiladi.

### 21.4. Sozlamalar va nega aynan shunday

| Konstanta | Qiymat | Nega |
|---|---|---|
| `TopN` | **6** | Kamroq bo'lsa yonma-yon mavzular ("narx" va "chegirma") tushib qolardi; ko'proq bo'lsa RAG'ning ma'nosi (promptni qisqartirish) yo'qolardi |
| `MinScore` | **0.20** | ⚠️ ATAYIN past: RAG'da eng qimmat xato — **kerakli bo'lakni tashlab yuborish**. Ortiqcha bo'lak promptni biroz uzaytiradi, xolos |
| `MaxDims` | 4096 | Buzuq/ulkan JSON xotirani yeb qo'ymasin (`text-embedding-004` — 768) |
| `BatchPerTick` | 5 | Fon xizmatining bitta aylanishi cho'zilmasin — navbat undan keyin turadi |
| `TextLimit` | 8000 belgi | Uzun bo'lak baribir bitta mavzuni ifodalaydi, dumi ma'noga ta'sir qilmaydi |
| Worker oralig'i | **60 soniya** | Bilim bazasi kamdan-kam o'zgaradi; har tsiklda so'rash Gemini kvotasini bekorga yeyardi |

⚠️ **`RETRIEVAL_DOCUMENT` va `RETRIEVAL_QUERY` HAR XIL bo'lishi SHART.** Gemini savol va
hujjatni bir-biriga yaqinroq joylashtirish uchun aynan shu belgidan foydalanadi — ikkovini ham
"document" qilib yuborish o'xshashlikni sezilarli **pasaytiradi**.

⚠️ Embedding modeli uchun **yangi `.env` kaliti kiritilmadi** (`DefaultModel` konstantasi).
Sabab: kalit `AppSecrets.EnvKeys` ga, `docker-compose.yml` ga va `.env.example` ga ham
qo'shilishi kerak bo'lardi (`EnvKeysWiringTests`), model esa amalda o'zgarmaydi. Model almashsa
konstanta yangilanadi va vektorlar **o'z-o'zidan** qayta hisoblanadi.

⚠️ `Compose` **eski formatni AYNAN saqlaydi** (`## Sarlavha\nMatn\n\n`) va tartib **`Order`
bo'yicha**, ball bo'yicha EMAS: operator bilim bazasini o'sha tartibda ko'radi va promptdagi
tartib unga mos tursin. Aks holda RAG yoqilgan markazda promptning ko'rinishi **sababsiz**
o'zgarardi.

### 21.5. 🔴 `EmbeddedHash` — nega `UpdatedAt` YETARLI EMAS

Bilim bazasi **bulk** saqlanadi: faqat **TARTIB** o'zgarganda ham har bo'lakning `UpdatedAt` i
yangilanadi. Agar qayta hisoblash qaroriga `UpdatedAt` asos qilinsa, har saqlashda **BUTUN
baza qaytadan embedding** qilinardi — o'nlab bekorga ketgan Gemini so'rovi.

`NeedsEmbedding` to'rt sababni ko'radi:

| Sabab | Izoh |
|---|---|
| Vektor **yo'q** | Yangi bo'lak |
| Vektor **buzuq** | `ParseVector` bo'sh massiv qaytardi |
| **Matn o'zgargan** | `ContentHash(title, content)` mos kelmadi — ⚠️ **sarlavha ham hashga kiradi** (u ham promptga tushadi va ma'noga ta'sir qiladi) |
| **Model almashgan** | ⚠️ Har xil modelning vektorlari **boshqa fazoda** yotadi; ularni taqqoslash ma'nosiz natija berardi va **o'lcham mos kelib qolsa xato ham chiqmasdi** — eng yomon holat |

⚠️ **Matni umuman yo'q bo'lak HECH QACHON navbatga tushmaydi:** Gemini bo'sh matnni rad etadi,
navbatda qolsa esa har tsiklda qayta urinilib, **boshqa bo'laklarni surib qo'yardi**.

⚠️ `EmbedPendingAsync` **birinchi XATODA to'xtaydi** (`break`): kalit noto'g'ri yoki kvota
tugagan bo'lsa qolgan bo'laklarni urinib ko'rish faqat bekorga so'rov sarflardi. Keyingi tsiklda
qaytadan uriniladi — bo'lak "hisoblanmagan" bo'lib qolaveradi.

⚠️ **Darvoza:** `CenterMeta.InstagramEnabled == false` bo'lsa tashqariga **hech qanday so'rov
ketmaydi** (vektor faqat AI agenti uchun kerak). Tekshiruv **ikki qavat**: worker ham,
`EmbedPendingAsync` ning o'zi ham.

⚠️ Vektor JSON'i **`InvariantCulture`** bilan yoziladi: server mintaqasi vergulli o'nlik
ishlatsa `"0,12"` massivda **ikkita son** bo'lib o'qilardi.

⚠️ `Cosine` bo'sh, `null` yoki **TURLI O'LCHAMDAGI** vektorda `0` qaytaradi (istisno emas) —
turli o'lcham model almashganining belgisi; bunday bo'lak jimgina chetlab o'tiladi, butun javob
buzilmaydi.

⚠️ Izohda kelgan savolga **post matni (caption)** ham qo'shiladi (`QueryText`) — u kontekst
beradi ("bu qaysi kurs haqidagi post"), lekin **200 belgigacha qisqartiriladi va xabardan KEYIN
turadi**: uzun caption savolni "bo'g'ib" qo'yardi.

### 21.6. 🔴 JAVOB SIFATI JURNALI — "taklif" nima

`IgQualityLog.AttachSuggestionAsync` operator yozgan chiquvchi xabarga AI taklifini biriktiradi.
**Taklif** = suhbatdagi **ENG OXIRGI CHIQUVCHI** xabar AI yozgan bo'lsa (`IsAi`) va u
**3 soat** (`SuggestionWindowMinutes` = 180) ichida yozilgan bo'lsa.

| Qoida | Nega |
|---|---|
| Oxirgi chiquvchi xabar **OPERATORNIKI** bo'lsa taklif YO'Q | Bu suhbatning davomi, tahrir emas. Aks holda operatorning ketma-ket ikki xabari "AI javobini tahrirladi" bo'lib sanalib, hisobot **yolg'on** chiqardi |
| AI javobi **YUBORILMAGAN** bo'lsa ham (`Error` to'la) u taklif hisoblanadi | ⚠️ Aynan shu holat **eng foydalisi**: AI matn yozdi, mijozga ketmadi, odam qaytadan yozdi |
| 3 soatdan eski taklif hisobga olinmaydi | Mijoz ertasi kuni qayta yozganda operator kechagi bot javobini tahrirlamaydi — bu yangi qadam |
| Buzuq `CreatedAt` → **taklif emas** | Noaniq qiymat asosida hisobotga qator yozishdan ko'ra yozmagan yaxshi |

⚠️ **`AttachSuggestionAsync` `Add` dan OLDIN chaqiriladi** — so'rov bazaga ketadi va hali
yozilmagan qatorni "taklif" deb olib qo'ymaydi. **SaveChanges QILMAYDI**: chaqiruvchining
tranzaksiyasi bilan birga ketadi (`AuditService.Record` bilan bir xil siyosat).

⚠️ **Xato JIM YUTILADI:** sifat jurnali — ichki tahlil ma'lumoti, uning tufayli operatorning
javobi yuborilmay qolishi mumkin emas.

### 21.7. 🔴 `AiSuggestedIntent` NEGA ALOHIDA USTUN

Niyatni mavjud `IgMessage.AiIntent` ga yozish vasvasasi bor, lekin **`GET /analytics`
niyatlarni AYNAN o'sha ustun bo'yicha guruhlaydi**. Operator xabariga ham niyat yozilsa bitta
suhbat **ikki marta** sanalib, mavjud hisobot **ikki barobar shishardi** — va buni hech kim
darhol sezmasdi.

Uch maydon (`AiSuggestedText` · `AiSuggestedIntent` · `WasEdited`) **FAQAT operator yozgan
chiquvchi xabarda** to'ldiriladi, AI'ning o'z javobida bo'sh qoladi.

### 21.8. Matn solishtirish va hisobot

O'xshashlik — **normallashtirilgan Levenshtein**: `1 − masofa / uzunroq matn`, natija 0..1
(hisobotda 0..100%).

⚠️ **Normallashtirish:** kichik harf + **apostroflar bir ko'rinishga** + ketma-ket bo'shliqlar
bitta bo'shliqqa. Apostrof birxillashtirish — `ContactService.TopWords` bilan AYNAN bir sabab:
matn turli klaviaturalardan kiritiladi va aks holda operator **faqat apostrofni** almashtirsa
ham "tahrirladi" deb sanalardi.

⚠️ **O'xshashlik SAQLANMAYDI** (ustun yo'q) — ikkala matn joyida turgani uchun **o'qishda**
hisoblanadi. Yagona manba `IgQualityLog.Similarity`: jadval, ro'yxat va kelajakdagi eksport
bir xil raqamni ko'rsatsin.

`GET /api/admin/instagram/quality?from&to&limit` (sinf darajasidagi `marketing` ruxsati):

| Qoida | Nega |
|---|---|
| **O'rtacha farq FAQAT tahrirlanganlar bo'yicha** | O'zgartirilmagan javoblar 100% o'xshashlik bilan o'rtachani sun'iy ko'tarib, "AI matni deyarli aynan qoldirilgan" degan **yolg'on** taassurot berardi |
| Taklif AYNAN qabul qilingan holat ham **kiradi** (`WasEdited=false`) | "AI to'g'ri yozdi" ham o'lchov |
| Niyat kesimi **eng ko'p TAHRIRLANADIGAN** niyat bo'yicha tartiblanadi | Savol — "AI qayerda ko'proq yanglishadi", "qaysi niyat ko'p uchraydi" emas (bunisi analitikada bor) |
| Jamlanma **ro'yxatdan emas**, `QualityScanLimit` (2000) to'plamdan | `books.md` sabog'i: sahifalangan ro'yxatdan qo'shib chiqarilgan son noto'g'ri bo'ladi |
| Chegaradan oshgani **JIM QIRQILMAYDI** | Javobda `Truncated` bayrog'i — ekranda ochiq yozilishi SHART |
| Buzuq sana → **standart davr** (oxirgi 30 kun) | 500 bermasin |

🔴 **MAXFIYLIK — javobda MIJOZNING HECH QANDAY BELGISI YO'Q:** na username, na Instagram ID,
na telefon, na **mijoz yozgan matn**. Faqat BIZNING ikki chiquvchi matnimiz (AI taklifi va
operator yuborgani), niyat, kanal va **XODIM** ismi.

⚠️ **`ConversationId` ham qaytmaydi** — u orqali suhbatni ochib mijozni topish mumkin bo'lardi,
ya'ni "faqat matnlar" qoidasi **bilvosita** buzilardi. Bu ICHKI SIFAT ma'lumoti; "kim bilan
yozishilgani" savolining joyi — Inbox.

### 21.9. Testlar

| Test sinfi | Nimani qulflaydi |
|---|---|
| `IgKnowledgeRagTests` (44) | Vektor JSON'iga yozib-o'qish va **o'nlik ajratgich HAR DOIM nuqta**, buzuq/ulkan JSON istisno otmasligi; kosinus (ayni vektor → 1, perpendikulyar → 0, **turli o'lcham va nol vektorda YIQILMASLIK**); tanlov tartibi, chegaradan o'tmagan bo'lak, **teng ballda BARQAROR tartib**; **kichik bazada va bitta bo'lak embedding qilinmaganda RAG ISHLATILMASLIGI**; `Compose` eski formatni saqlashi va chegaradan oshmasligi; `NeedsEmbedding` to'rt sababi va **sarlavhaning hashga kirishi**; `QueryText` da xabar oldinda turishi |
| `IgQualityLogTests` | Bosh harf/bo'shliq va **turli apostroflar** farq emasligi, Levenshtein masofasi, ayni matn → 100%, bitta tomon bo'sh → 0; **operatorning o'z xabari va kiruvchi xabar taklif EMASLIGI**, eski taklif va buzuq sanali xabar olinmasligi |
| `InstagramCaptionTests` (24) | §18.10 jadvalida |
