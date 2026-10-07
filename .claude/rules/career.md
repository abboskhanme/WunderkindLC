---
description: Karyera (Wunderkind Career) — ishga qabul moduli: ALOHIDA Telegram bot + `/vakansiya` Mini App (statik HTML/Bootstrap), vakansiyalar va nomzod arizalari.
paths:
  - "WunderkindLC.Application/Services/CareerService.cs"
  - "WunderkindLC.Application/Services/CareerBotService.cs"
  - "WunderkindLC.Application/Services/CareerTelegramService.cs"
  - "WunderkindLC.Application/Services/TelegramInitData.cs"
  - "WunderkindLC.Application/Dtos/CareerDtos.cs"
  - "WunderkindLC.Server/Controllers/CareerController.cs"
  - "WunderkindLC.Server/Controllers/PublicCareerController.cs"
  - "WunderkindLC.Server/wwwroot/vakansiya.*"
  - "WunderkindLC.Client/src/pages/admin/vacancies/**"
  - "WunderkindLC.Client/src/api/services/career.ts"
---

# Karyera (ishga qabul) qoidalari

Migratsiya: `AddCareerModule`. Modul markazdagi **bo'sh ish o'rinlarini** e'lon qiladi va nomzod
arizalarini bosqichma-bosqich yuritadi. Nomzod tomoni CRM ichida EMAS — u **alohida Telegram bot**
va uning **Mini App**ida.

## 1. IKKINCHI BOT — asosiy botdan MUSTAQIL

- Token: `.env → CAREER_BOT_TOKEN` (docker: `Career__BotToken`), `AppSecrets.CareerBotToken`.
  **Asosiy bot tokeni bilan ARALASHTIRILMAYDI** — `TelegramService` (asosiy) va
  `CareerTelegramService` (karyera) ikkita mustaqil API mijozi, har biri o'z long polling'i bilan
  (`TelegramBotService` va `CareerBotService`).
- Token bo'sh bo'lsa `CareerBotService` jim kutadi — CRM va asosiy bot odatdagidek ishlaydi.
- Bot deyarli hech narsa qilmaydi: `/start` → xush kelibsiz + **inline `web_app` tugmasi**
  (Mini App shu tugmadan ochiladi) + doimiy reply-klaviatura (ilovani ochish / telefonni ulashish).
  Telefon ulashilsa `CareerBotUser.Phone` ga yoziladi va ariza formasi uni oldindan to'ldiradi.
  Startupda `setChatMenuButton` ham Mini App'ga bog'lanadi.
- Mini App manzili: `Career:MiniAppUrl` (env `CAREER_MINIAPP_URL`); bo'sh bo'lsa `App:Host` dan
  yasaladi — `https://<APP_HOST>/vakansiya`.

## 2. MINI APP — `/vakansiya` (React EMAS)

- `WunderkindLC.Server/wwwroot/vakansiya.html` + `vakansiya.css` + `vakansiya.js`, Bootstrap 5
  **o'z serverimizdan** (`wwwroot/vendor/bootstrap.min.css`, `bootstrap.bundle.min.js`).
  **CDN ISHLATILMAYDI:** prod CSP `default-src 'self'` tashqi manbani bloklaydi. Shu sababdan
  skript ham inline emas, alohida faylda (`landing.js` bilan bir xil sabab).
  `telegram-web-app.js` esa `https://telegram.org` dan — u CSP'da ATAYIN ruxsat etilgan.
- Yo'l `Program.cs`da SPA fallback'dan OLDIN: `MapGet("/vakansiya")` va `/vakansiya/{**rest}`
  → `vakansiya.html` (`no-cache`). Aks holda React `index.html` qaytardi.
- Ekranlar (pastki navigatsiya): **Biz haqimizda · Vakansiyalar · Arizalarim** + ichki ekranlar
  (vakansiya tafsiloti, ariza formasi, tasdiq). Telegram `themeParams` CSS o'zgaruvchilarga
  ko'chiriladi — ilova foydalanuvchining kunduzgi/tungi mavzusida ko'rinadi.

## 3. AUTENTIFIKATSIYA — Telegram imzosi (login YO'Q)

- Har so'rovda `X-Telegram-Init-Data` sarlavhasi; `TelegramInitData.Validate` uni
  **karyera boti tokeni** bilan tekshiradi (HMAC: `secret = HMAC("WebAppData", token)`),
  `auth_date` 24 soatdan eski bo'lsa rad etadi.
- Shundan `ChatId` olinadi — "Arizalarim" va ariza yuborish FAQAT shu asosda ishlaydi
  (foydalanuvchi boshqa birovning arizasini ko'ra olmaydi).
- Imzo bo'lmasa (oddiy brauzer) — **faqat ko'rish**: biz haqimizda + vakansiyalar ko'rinadi,
  ariza yuborish 401.
- ⚠️ Dekodlash `Uri.UnescapeDataString` bilan (`+` ni bo'sh joyga aylantirmaydi) — query-parser
  ishlatilsa imzo mos kelmay qoladi.

## 4. Entitylar (Domain/Entities.cs, "KARYERA" bo'limi)

| Entity | Vazifasi |
|---|---|
| `CareerAbout` | "Biz haqimizda" — BITTA qator (CenterMeta kabi): matn, manzil, aloqa, ijtimoiy tarmoqlar |
| `Vacancy` | Vakansiya: `Status` = `active` \| `paused` \| `archived` (§5.5), maosh, talab/vazifa/shart matnlari, `Deadline`, `Order` |
| `JobApplication` | Ariza: `Number` (#1,#2…), `ChatId`, F.I.Sh./telefon/tajriba/motivatsiya, `CvUrl`, `Status`, `StatusNote`, `AdminNote` |
| `JobApplicationEvent` | Bosqich TARIXI — nomzod "Arizalarim"da shuni ko'radi |
| `CareerBotUser` | Botga /start bosgan foydalanuvchi (`ChatId` unikal) — telefon keshi + statistika |

⚠️ `CareerAbout.Youtube`/`Tiktok` ATAYIN shunday yozilgan (`YouTube`/`TikTok` EMAS): camelCase
JSON siyosati `YouTube` ni `youTube` qilib yuborardi va klient maydonni topa olmasdi.

## 5. BOSQICHLAR — yagona katalog (ishga qabul VORONKASI)

2026-10 yig'ilishi bo'yicha voronka: **CV qabul qilindi → Test → Demo dars → HR suhbati →
Rahbar (direktor) suhbati → Ishga qabul qilindi**, yakuniy natija — **Rad etildi**.

| Kalit | Yorliq | Tartib | Izoh |
|---|---|---|---|
| `new` | CV qabul qilindi | 1 | eski "Yangi ariza" |
| `review` | Ko'rib chiqilmoqda (eski) | 1 | **ESKI (`IsLegacy`)** — pastga qarang |
| `test` | Test | 2 | YANGI |
| `trial` | Demo dars | 3 | eski "Sinov bosqichi" — ma'nosi bir xil |
| `interview` | HR suhbati | 4 | eski "Suhbatga taklif" |
| `director` | Rahbar (direktor) suhbati | 5 | YANGI |
| `hired` | Ishga qabul qilindi | 6 | yakuniy · **moslashuvni boshlaydi** (`onboarding.md`) |
| `rejected` | Rad etildi | 99 | yakuniy, yo'l-xaritaga kirmaydi |

⚠️ **ESKI KALITLAR O'ZGARMAGAN** — bazadagi arizalar ham, `JobApplicationEvent` tarixi ham shu
kalitlarda. Ma'nosi mos kelgani qayta ishlatildi, yangilari (`test`, `director`) qo'shildi.
Migratsiya/backfill YO'Q (kalit katalogda, tartib bazada saqlanmaydi). Eski tartibda `interview`
`trial` dan OLDIN edi — endi voronka bo'yicha keyin; eski arizalar yangi tartibda o'qiladi.

⚠️ **`review` — ESKI (legacy) bosqich.** Voronkada YO'Q, lekin o'chirilmagan: shu bosqichdagi
mavjud arizalar ko'rsatiladi va undan chiqib ketish mumkin. Yangi o'tish YOPIQ —
`CareerService.CanMoveTo` (server 400, klient `stageOptions` uni tanlagichga qo'ymaydi; ariza
hozir o'sha bosqichda bo'lsa — izohini yangilash uchun qoladi). Tartibi `new` bilan BIR XIL (1):
yo'l-xaritada va voronkada u "CV qabul qilindi" o'rnida turadi (ma'nosi "CV ko'rib chiqilmoqda").
Admin chiplarida faqat unda ariza qolgan bo'lsa chiqadi.

Backend, Mini App va admin paneli AYNAN shu kalitlarni ishlatadi (`GET /api/admin/career/stages`,
Mini App esa `bootstrap` javobidan oladi — `vakansiya.js` kalitlarni QATTIQ YOZMAYDI, faqat
`rejected` va `isLegacy` ni yo'l-xaritadan chiqaradi). Frontend'dagi `careerLabels.ts` — server
javob bermasa ishlatiladigan ZAXIRA yorliqlar, yagona haqiqat manbai emas.
⚠️ `vakansiya.js` o'zgarsa `vakansiya.html` dagi `?v=` ni oshiring — Telegram WebView eski
skriptni keshda ushlab qolmasin (HTML `no-cache`, skript esa oddiy statik fayl).

**Nomzodga matnlar** (`CandidateText`) neytral yozilgan ("… bosqichiga o'tdingiz, vaqti izohda"):
admin bosqichni o'tkazib yuborishi mumkin (CV → HR), shuning uchun "testdan o'tdingiz!" kabi
oldingi bosqichni nazarda tutadigan matn YOZILMAYDI.

### 5.1. VORONKA KONVERSIYASI — `CareerService.ComputeFunnel` (sof, testlangan)

`GET stats` → `funnel[]`: har asosiy bosqich uchun **Reached** (shu bosqichga yoki undan
keyingisiga YETIB BORGANLAR), **Current** (hozir shu yerda), **RejectedHere** (shu bosqichda
to'xtab rad etilganlar), **FromPrevPct** / **FromStartPct** (pastga yaxlitlangan %).

- Ariza qayerga yetgani — joriy bosqich VA tarix (`JobApplicationEvent`) dagi ENG UZOQ bosqich.
  Rad etilgan nomzod qayerda to'xtagani bilan sanaladi; o'tkazib yuborilgan oraliq bosqichlar
  "o'tilgan" hisoblanadi; har ariza kamida CV bosqichida.
- ⚠️ Eski tartib bilan yozilgan tarix (`interview` → `trial`) yangi tartibda o'qiladi — eski
  "suhbat"dan o'tgan ariza HR suhbatigacha yetgan sanaladi. Qabul qilingan taxmin.

## 5.5. VAKANSIYANI YOQISH/O'CHIRISH (ON/OFF) — bitta tugma

`Vacancy.Status`: `active` (yoqilgan) · **`paused`** (O'CHIRILGAN — vaqtincha yopiq) · `archived`.
`PUT vacancies/{id}/active { active: bool }` (tahrir amali, idempotent, auditga yoziladi).

- O'chirilgan vakansiya Mini App'da (Telegram bot ichida ham, oddiy brauzerda `/vakansiya` sahifasida
  ham) **DARHOL** ko'rinmaydi va ariza qabul qilmaydi: `bootstrap` va `apply` faqat
  `CareerService.VacancyActive` ni o'tkazadi, javob keshlanmaydi (har ochilishda bazadan).
  Ochiq turgan ilovadan yuborilgan ariza "yopilgan" xabari bilan rad etiladi.
- Admin ro'yxatida o'chirilgan vakansiya JOYIDA turadi ("Joriy" filtri = `active,paused`) —
  arxivdan farqi shu. Arxivdagini yoqib bo'lmaydi (400) — avval "Tiklash".
- Landing (`/`) da vakansiyalar ro'yxati YO'Q — "veb-sayt" = `/vakansiya` sahifasi.
- `GET vacancies?status=` vergul bilan bir nechta holat qabul qiladi; noma'lum qiymat tashlanadi.

**Bosqich o'zgarganda** (`CareerService.SetStatusAsync`): yozuv yangilanadi + `JobApplicationEvent`
qo'shiladi + nomzodga **karyera botida** avtomatik xabar ketadi. Admin kiritgan izoh
(`StatusNote`) NOMZODGA KO'RINADI (suhbat vaqti, rad sababi); faqat ichki eslatma uchun
alohida `AdminNote` maydoni bor va u hech qachon yuborilmaydi.

**Yangi ariza tushganda** adminlarga xabar MARKAZNING ASOSIY boti orqali ketadi
(`CareerService.NotifyAdminsAsync` — `LeadNotifier` bilan bir xil oluvchi mantig'i: superadminlar
+ bot qo'shilgan faol guruhlar). Sabab: adminlar asosiy botda ro'yxatdan o'tgan, karyera botida emas.

## 6. API

- **Admin:** `api/admin/career` — `AdminPerm("vacancies.list", ReadRequiresPerm = true)`
  (2026-10 dan SAHIFA kaliti: bo'limda ikkinchi sahifa — «Moslashuv», `vacancies.onboarding`;
  bo'lim kaliti bo'lsa faqat moslashuv berilgan xodim yuqoriga o'qish merosi bilan CV'larni
  o'qir edi. Yalang `vacancies` ruxsatli eski xodimlar o'zgarishsiz — pastga meros).
  ⚠️ O'QISH ham darvozalangan (odatdagi `AdminPerm` da xodim uchun GET ochiq): arizalar javobida
  nomzodning `CvUrl` — `/uploads/*.pdf` rezyume manzili qaytadi, `/uploads` esa autentifikatsiyasiz
  beriladi (manzilni olgan xodim faylni abadiy ola oladi). Bo'limni FAQAT `vacancies` ruxsati
  (biror amali) bor xodim o'qiy oladi; admin/superadmin — cheklovsiz. Bu Mini App'ga TEGMAYDI —
  nomzod tomoni alohida `api/career` (`[AllowAnonymous]`) da.
  `about` (GET/PUT) · `vacancies` (GET/POST/PUT + `/{id}/active` (ON/OFF), `/{id}/archive`,
  `/{id}/restore`, DELETE) ·
  `applications` (GET ro'yxat/filtr, `/{id}` tarixi bilan, `/{id}/status`, `/{id}/note`, DELETE) ·
  `stages` · `stats`.
  Ariza tushgan vakansiyani **o'chirib bo'lmaydi** — arxivlanadi (tarix buzilmasin).
- **Mini App:** `api/career` — `[AllowAnonymous]`, initData bilan.
  `bootstrap` (BIR so'rovda: about + faol vakansiyalar + o'z arizalari + bosqichlar) ·
  `cv` (FAQAT `.pdf`, 10 MB, `public-lead` rate-limit) · `apply` (`public-lead` rate-limit).
  `apply` da `CvUrl` faqat o'zimiz yuklagan `/uploads/*.pdf` bo'lishi tekshiriladi (tashqi/soxta
  havola adminga tushmasin), bitta vakansiyaga bitta ariza.

## 7. Admin UI

"Boshqaruv → Vakansiyalar" (`/admin/boshqaruv/vacancies`, perm `vacancies.list`) — bitta sahifa, 3 tab:
**Vakansiyalar** (yaratish/tahrir, **ON/OFF tugmasi** kartaning o'ng yuqorisida, arxivlash) ·
**Arizalar** (voronka kartasi + bosqich chiplari sonlari bilan + qidiruv, qator bosilganda tafsilot
modali) · **Biz haqimizda** (Mini App'ning birinchi ekrani).
Vakansiya kartasidagi "N ta ariza" — filtrni qo'yib "Arizalar" tabiga o'tkazadi.
Sarlavhadagi «Moslashuv (30 kun)» havolasi — `vacancies.onboarding` bo'lsa.

## 8. Ishga qabul → MOSLASHUV

Ariza `hired` ga **O'TGANDA** (`SetStatusAsync` `changed == true`) 30 kunlik moslashuv cheklisti
AVTOMATIK boshlanadi — `OnboardingService.StartForApplicationAsync`, xuddi shu tranzaksiyada,
auditi bilan. Faqat izohni yangilash (bosqich o'zgarmasa) qayta boshlamaydi. Batafsil:
`.claude/rules/onboarding.md`.
