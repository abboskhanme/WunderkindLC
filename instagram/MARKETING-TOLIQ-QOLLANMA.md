# WunderkindLC — **Marketing bo'limi**: to'liq qo'llanma

> **Kimga:** markaz rahbari, marketing/SMM mas'uli va texnik mas'ul uchun.
> **Nima uchun:** Marketing bo'limidagi **hamma funksiya** Meta (Instagram + Facebook) API'lari
> ustiga qurilgan. Bu hujjat bitta joyda javob beradi: *qaysi sahifa nima qiladi, qanday
> ishlatiladi, Facebookni CRM'ga qanday ulayman, Business (Business Manager) qanday ulanadi,
> qaysi token qayerdan olinadi va nima ishlamasa nima qilaman.*
>
> **Holat:** 2026-yil avgust. Kodda Graph API **v23.0** ishlatiladi (`IgConst.GraphVersion`).
> Meta tomonidagi eng so'nggi versiya — **v26.0** (2026-07-29). Batafsil: §12.

---

## Mundarija

1. [Bir qarashda — Marketing bo'limi nimalardan iborat](#1-bir-qarashda)
2. [Meta ekotizimi — 15 daqiqada tushuniladigan asos](#2-meta-ekotizimi)
3. [Tayyorgarlik ro'yxati (nima kerak)](#3-tayyorgarlik)
4. [1-BLOK — Instagram'ni ulash (izoh + DM agenti)](#4-blok-1-instagram)
5. [2-BLOK — **Facebook va Business'ni ulash**](#5-blok-2-facebook-business)
6. [3-BLOK — Reklama modullarini sozlash (Lead Ads · Ads Insights · CAPI · Kontent)](#6-blok-3-reklama-modullari)
7. [Sahifalar bo'yicha qo'llanma — nima qiladi, qanday ishlatiladi](#7-sahifalar)
8. [Kundalik ish tartibi](#8-ish-tartibi)
9. [Diagnostika — birlashgan xatolar jadvali](#9-diagnostika)
10. [Muddatlar, limitlar va kvotalar](#10-muddatlar)
11. [Xavfsizlik, maxfiylik va audit](#11-xavfsizlik)
12. [2026-08 holati — Meta tomonidagi o'zgarishlar va manbalar](#12-meta-holati)
13. [FAQ — tez-tez so'raladigan savollar](#13-faq)

---

<a id="1-bir-qarashda"></a>

## 1. Bir qarashda — Marketing bo'limi nimalardan iborat

CRM'da bo'lim: **Marketing** (`/admin/marketing`), umumiy ruxsat kaliti — **`marketing`**.
Ichida **10 sahifa** bor va ular **5 ta mustaqil modul**ga tegishli. Har modulning **o'z
bayrog'i** bor va **hammasi default O'CHIQ** — yoqilmaguncha tashqariga bitta ham so'rov
ketmaydi.

| # | Sahifa (menyu) | Manzil | Ruxsat | Qaysi modul | Meta tomoni |
|---|---|---|---|---|---|
| 1 | Boshqaruv paneli | `/admin/marketing` | `marketing.dashboard` | Izoh · DM agenti | Instagram Login |
| 2 | Inbox | `/admin/marketing/inbox` | `marketing.inbox` | Izoh · DM agenti | Instagram Login |
| 3 | Javob qoidalari | `/admin/marketing/rules` | `marketing.rules` | Izoh · DM agenti | — (ichki) |
| 4 | Bilim bazasi | `/admin/marketing/knowledge` | `marketing.knowledge` | Izoh · DM agenti + AI caption | — (ichki) |
| 5 | Analitika | `/admin/marketing/analytics` | `marketing.analytics` | Izoh · DM agenti | — (ichki) |
| 6 | **Reklama lidlari** | `/admin/marketing/reklama-lidlari` | `marketing.leadads` | Lead Ads | **Facebook Page** |
| 7 | **Reklama statistikasi** | `/admin/marketing/reklama-statistikasi` | `marketing.adsstats` | Ads Insights + ROI | **Ad Account** |
| 8 | **Kontent** | `/admin/marketing/kontent` | `marketing.content` | Content Publishing | Instagram Login (+scope) |
| 9 | Javob sifati | `/admin/marketing/javob-sifati` | `marketing.quality` | Sifat jurnali (E6.6) | — (ichki) |
| 10 | Sozlamalar | `/admin/marketing/settings` | `marketing.settings` | **hammasi** | hammasi |

**CAPI** moduli alohida sahifaga ega emas — u **Sozlamalar** ichidagi kartochkada boshqariladi
(`Lid sifatini Meta'ga qaytarish (CAPI)`).

### Modullar bir gapda

| Modul | Bir gapda | Bayroq |
|---|---|---|
| **Izoh · DM agenti** | Instagram'ga kelgan izoh va DM'ga AI o'zbekcha javob beradi, qiziqqanni **lidga** aylantiradi, operatorni chaqiradi | `InstagramEnabled` + `AutoReplyComments` / `AutoReplyDm` |
| **Reklama lidlari (Lead Ads)** | Target reklamadagi **Instant Form** to'ldirilsa F.I.Sh. + telefon avtomatik CRM lidiga tushadi | «Reklama lidlari yoqilgan» |
| **Reklama statistikasi (Ads Insights)** | Xarajat · ko'rsatish · **CPL** · **CAC** · **ROI** — qaysi reklama haqiqatan pul keltirdi | «Reklama statistikasi yoqilgan» |
| **Kontent joylash** | Rasm / Reels / Story / karuselni CRM'dan rejalashtirib joylash + **AI matn yozdirish** | «Kontent joylash yoqilgan» |
| **CAPI** | «Bu lid o'quvchi bo'ldi va pul to'ladi» ni Meta'ga qaytarish → reklama shunga optimallashadi | «CAPI yoqilgan» |

---

<a id="2-meta-ekotizimi"></a>

## 2. Meta ekotizimi — 15 daqiqada tushuniladigan asos

Bu bo'limni **o'tkazib yubormang**. Marketing bo'limini sozlashdagi vaqtning ~80% i aynan shu
tushunchalar chalkashganidan yo'qoladi.

### 2.1. Ishtirok etadigan obyektlar

| Obyekt | Bu nima | Qayerda turadi |
|---|---|---|
| **Instagram Professional akkaunt** | Bizning Instagram akkauntimiz, «Business» yoki «Creator» rejimida | Instagram ilovasi |
| **Facebook Page (sahifa)** | Markazning Facebook sahifasi. **Reklama lidlari** aynan shundan keladi | facebook.com |
| **Meta App (ilova)** | `developers.facebook.com` da ochiladigan «dastur». Token va webhook aynan unga tegishli | developers.facebook.com |
| **Business portfolio / Business Manager** | Kompaniya «seyfi»: sahifa, reklama kabineti, dataset, xodimlar **shu yerda** bir joyda turadi | business.facebook.com |
| **Ad Account (reklama kabineti)** | Reklamaga pul sarflanadigan kabinet (`act_...`) | Ads Manager |
| **Dataset (Events Manager)** | Konversiya hodisalari yig'iladigan idish (eski nomi «piksel») | business.facebook.com/events_manager |
| **System User (sistema foydalanuvchisi)** | «Robot xodim» — **muddatsiz token** aynan shundan olinadi | Business settings → Users |

### 2.2. Ikki xil yo'l — eng ko'p chalkashtiradigan joy

Meta'da Instagram bilan ishlashning **ikki** yo'li bor. Loyiha **birinchisini** tanlagan:

| | **Instagram API with Instagram Login** ← biz shu yo'ldamiz | Instagram API with Facebook Login |
|---|---|---|
| Kirish | Instagram parol oynasi | Facebook parol oynasi |
| Facebook Page | **kerak emas** | shart |
| Business Verification | **kerak emas** | odatda shart |
| App Review | **kerak emas** (Standard Access, akkaunt o'zimizniki) | shart |
| Baza manzil | `graph.instagram.com` | `graph.facebook.com` |
| Scope nomlari | `instagram_business_basic`, `instagram_business_manage_messages`, `instagram_business_manage_comments`, `instagram_business_content_publish` | `pages_*`, `instagram_manage_*` |

🔴 **LEKIN:** bu yengillik faqat **izoh · DM · kontent** uchun. **Reklama lidlari**,
**reklama statistikasi** va **CAPI** — butunlay boshqa Meta mahsulotlari va ular **Facebook
Page**, **Business Manager**, ba'zilari esa **App Review** talab qiladi. Aynan shuning uchun
«Facebookni ulash» degan alohida blok bor (§5).

### 2.3. To'rt token xaritasi — 🔴 ENG MUHIM JADVAL

Modullar **to'rt xil token** bilan ishlaydi. Ular bir-birining o'rnini **BOSMAYDI**, chunki
har biri **boshqa obyektga** tegishli:

| Modul | Token turi | Qaysi obyektga tegishli | Kerakli ruxsat | Muddati | Qayerdan olinadi |
|---|---|---|---|---|---|
| Izoh · DM · Kontent | **Instagram Login tokeni** | Instagram akkaunt | `instagram_business_*` | 60 kun (45-kunda o'zi yangilanadi) | CRM'dagi **«Ulash»** tugmasi (OAuth) |
| Reklama lidlari | **Page Access Token** | **Facebook Page** | `leads_retrieval` + `pages_show_list` + `pages_manage_ads` + `pages_read_engagement` | muddatsiz (System User) | Business settings → System users |
| Reklama statistikasi | **System User tokeni** | **Ad Account** | **`ads_read`** (+ `business_management`) | muddatsiz | Business settings → System users |
| CAPI | **Dataset tokeni** | **Dataset** | **`ads_management`** | muddatsiz | Events Manager yoki System user |

**Almashtirib yuborilsa nima bo'ladi:**

| Xato belgisi | Odatiy sabab |
|---|---|
| `OAuthException 190` — «Token yaroqsiz yoki muddati tugagan» | Boshqa modulning tokeni qo'yilgan yoki oddiy foydalanuvchi tokeni o'lgan |
| `#200` / `#10` — «Ruxsat yetishmaydi» | Tokenda kerakli scope yo'q **yoki** System User'ga tegishli **asset biriktirilmagan** |
| `803` — «obyekt topilmadi» | Dataset ID xato |
| `100` — «Noto'g'ri so'rov» | ID formati xato (masalan `act_` prefikssiz) yoki payload xatosi |

### 2.4. Qaysi modul nimani talab qiladi — talablar matritsasi

| Talab | Izoh · DM | Kontent | Reklama lidlari | Reklama statistikasi | CAPI |
|---|---|---|---|---|---|
| Instagram Professional akkaunt | ✅ | ✅ | ✅ (Page'ga bog'langan) | — | — |
| **Facebook Page** | ❌ | ❌ | ✅ **SHART** | ❌ | ❌ |
| **Business Manager** | ❌ | ❌ | ✅ | ✅ **SHART** | ✅ **SHART** |
| **Business Verification** | ❌ | ❌ | ✅ **SHART** | ❌ (odatda) | ❌ (odatda) |
| **App Review** | ❌ | ❌ | ✅ **SHART** (`leads_retrieval`) | ❌ (Standard Access yetadi) | ❌ (odatda) |
| Reklama kabineti (Ad Account) | ❌ | ❌ | ✅ (reklama uchun) | ✅ | ✅ (bog'liq dataset) |
| Server HTTPS + tashqaridan ochiq | ✅ (webhook) | ✅ **SHART** (media) | ✅ (webhook) | ❌ | ❌ |
| Sozlash vaqti | ~30–40 daq | ~10 daq | ~10 daq + **3–10 kun Meta tasdig'i** | ~15 daq | ~15 daq |

---

<a id="3-tayyorgarlik"></a>

## 3. Tayyorgarlik ro'yxati (nima kerak)

Boshlashdan oldin shular tayyor bo'lsin:

- ☐ Instagram akkaunt **Professional** (Business yoki Creator) — Instagram ilova → Sozlamalar → Akkaunt turi va vositalari;
- ☐ Server **HTTPS** da, tashqaridan ochiq domen bilan (webhook va media uchun);
- ☐ Serverdagi `.env` faylga yozish va `docker compose up -d` qilish imkoni;
- ☐ CRM'da `marketing` va `marketing.settings` ruxsati bor foydalanuvchi;
- ☐ `GEMINI_API_KEY` sozlangan (AI javoblar va AI caption uchun);
- ☐ **Reklama modullari uchun qo'shimcha:** Facebook Page, Business Manager, reklama kabineti;
- ☐ Facebook akkaunti — Page'ning **admini** va Business portfolio'ning **admini**.

---

<a id="4-blok-1-instagram"></a>

## 4. 1-BLOK — Instagram'ni ulash (izoh + DM agenti)

> Natija: Instagram izohlari va DM'lariga AI javob beradi, lidlar CRM'ga tushadi.
> **Vaqt: ~30–40 daqiqa. App Review kerak emas.**

### 4.1. Meta App yaratish (~10 daq)

1. `developers.facebook.com` → **My Apps** → **Create App**;
2. Use case: **Other** → App type: **Business** → nom bering;
3. Dashboard → **Add product** → **Instagram**;
4. Chap menyu → **Instagram → API setup with Instagram login**
   ⚠️ *«API setup with **Facebook** login»* **EMAS** — noto'g'ri yo'lda scope nomlari boshqacha bo'ladi va OAuth ishlamaydi;
5. 3-bo'lim *Set up Instagram business login* dan:
   - **Instagram App ID** — ko'chirib oling (maxfiy emas),
   - **Instagram App Secret** — **Show** bosib ko'chiring (**MAXFIY**).

🔴 **`Invalid platform app` xatosining sababi shu yerda:** ilova sahifasining tepasidagi
**Meta App ID** bilan **Instagram App ID** — bu **ikki xil raqam**. Bizga faqat ikkinchisi kerak.

### 4.2. Redirect URI (~2 daq)

*Instagram → API setup with Instagram login* → **Business login settings**:

```
OAuth redirect URI:  https://<domen>/api/public/instagram/callback
```

⚠️ Oxirida `/` **yo'q**, `https` majburiy, harfma-harf mos bo'lsin.
⚠️ **Joyi ham muhim:** *Facebook Login → Settings* yoki *App settings → Basic → App Domains*
ichiga yozilgani **ishlamaydi** (saqlangandek ko'rinadi, lekin `Invalid redirect_uri` chiqaveradi).

💡 Manzilni CRM → Marketing → Sozlamalar sahifasidagi **«OAuth callback URL»** maydonidan
**nusxa oling** — qo'lda termang.

### 4.3. `.env` (~3 daq)

```env
# ---- Instagram AI agenti (Marketing bo'limi) ----
INSTAGRAM_APP_SECRET=<App Secret>
INSTAGRAM_VERIFY_TOKEN=<o'zingiz o'ylab topgan satr, masalan: ic_ig_2026_a7f3>
```

So'ng `docker compose up -d`.

⚠️ Bu ikkisi **bazaga saqlanmaydi va CRM sahifasidan kiritilmaydi** (baza dump'i Telegram'ga
yuboriladi — kalit sizib chiqmasin). Sahifada faqat «sozlangan / sozlanmagan» ko'rinadi.
⚠️ `INSTAGRAM_VERIFY_TOKEN` ni **Meta bermaydi** — o'zingiz o'ylab topasiz; muhimi
Meta'dagi va `.env` dagi qiymat **bir xil** bo'lsin.

### 4.4. CRM Sozlamalar (~3 daq)

**Marketing → Sozlamalar → «Meta ilovasi»** kartochkasi:

| Maydon | Qiymat |
|---|---|
| **Instagram App ID** | 4.1-qadamdagi App ID |
| Lid manbasi | default `Instagram` — Lidlar bo'limida shu nom ko'rinadi |
| Gemini modeli | bo'sh qoldirilsa loyihaning standart modeli |
| Javob kechikishi | default `5` soniya (javob bir zumda kelmasin) |
| Kunlik javob chegarasi | default `200` |
| **Salomlashuv matni** | **Meta talabi:** birinchi xabarda botligimiz oshkor bo'lishi SHART |

Namuna: *«🤖 Men <markaz nomi> ning AI yordamchisiman. Operator kerak bo'lsa yozing — ulaymiz.»*

⚠️ Hozircha bayroqlarni **YOQMANG** — ular 4.8 da, hammasi sinovdan o'tgach.

### 4.5. Webhook ulash (~5 daq)

Meta → **Instagram → API setup with Instagram login** → **Configure webhooks**:

| Maydon | Qiymat |
|---|---|
| Callback URL | `https://<domen>/api/public/instagram/webhook` |
| Verify token | `.env` dagi `INSTAGRAM_VERIFY_TOKEN` bilan **aynan** bir xil |

**Verify and save** → so'ng maydonlarni belgilang:

| Maydon | Nega kerak |
|---|---|
| `comments` | izohlar |
| `messages` | DM |
🔴 **`message_echoes` endi YO'Q** — Meta uni qabul qilinadigan maydonlar ro'yxatidan olib
tashlagan. Yuborilsa `IGApiException 100` qaytadi va **butun obuna so'rovi rad etiladi**.
Operator qo'lda yozgan xabar `messages` obunasi ostida `is_echo: true` bilan keladi.

⚠️ `messages` belgilanmasa mijoz bir vaqtda **ikki odam bilan** gaplashadi.

### 4.6. Akkauntni ulash — bitta tugma (~1 daq)

**Marketing → Sozlamalar → «Instagram'ni ulash»** → Instagram'ning o'z login oynasi ochiladi
(parol bizga ko'rinmaydi) → ruxsatlarni tasdiqlaysiz.

Tizim o'zi bajaradi: kod → qisqa token → **60 kunlik token** → akkaunt ID/username →
webhook obunasi → bazaga saqlash.

✅ Tekshiruv: Sozlamalarda username, rasm va **«Token: ~60 kun qoldi»** ko'rinadi.

### 4.7. Bilim bazasini to'ldirish (~10 daq) — **ENG MUHIM QADAM**

**Marketing → Bilim bazasi.** Agent **faqat** shu yerdagi ma'lumot asosida gapiradi.

| Bo'lak | Ichida nima bo'lsin |
|---|---|
| Markaz haqida | nomi, manzil, ish vaqti, telefon |
| **Kurslar va narxlar** | yo'nalish, davomiyligi, oylik to'lov |
| To'lov va chegirmalar | to'lov usullari, aka-uka chegirmasi, shartnoma |
| FAQ | eng ko'p so'raladigan savol-javob |
| Muloqot qoidalari | joriy aksiyalar, nima va'da qilinmaydi |

⚠️ **Bilim bazasi bo'sh bo'lsa agent narx AYTA OLMAYDI** — har narx savolini operatorga
o'tkazadi. Bu **ataylab**: bot narxni o'ylab topmaydi.

### 4.8. Sinov va yoqish

**Xavfsiz sinov:** Sozlamalardagi **«Agentni sinash»** — javob ekranda ko'rsatiladi,
Instagram'ga **yuborilmaydi**. Tekshiring: narx aniq raqam bilan keladimi, javob mijoz
yozgan **tilda va yozuvda**mi (lotin/kirill/rus/ingliz), matn o'rtada kesilmayaptimi.

**Yoqish tartibi:** `Instagram moduli yoqilgan` → `Izohlarga avtojavob` → (bir kun kuzatib)
→ `DM'ga avtojavob` → `Izohga shaxsiy javob (private reply)` → `Telegram bildirishnomasi`.

⚠️ **O'z akkauntingizdan izoh yozib sinamang** — modul o'z izohini ataylab tashlaydi
(cheksiz halqadan himoya). Boshqa akkaunt kerak.

### 4.9. Huquqiy sahifalar (Meta talabi)

| Maydon | Manzil |
|---|---|
| Privacy Policy URL | `https://<domen>/privacy` |
| Data Deletion URL | `https://<domen>/data-deletion` |

Ular SPA'da ochiq marshrut sifatida turadi va hech qanday CRM ma'lumotini ko'rsatmaydi.

---

<a id="5-blok-2-facebook-business"></a>

## 5. 2-BLOK — **Facebook va Business'ni ulash**

> Bu blok savolga javob beradi: *«Facebookni CRM'ga qanday ulayman, businessni qanday
> ulayman?»* Uchala reklama moduli (**Lead Ads**, **Ads Insights**, **CAPI**) aynan shu
> blokdan keyin ishlaydi.

### 5.0. Umumiy manzara — nima nimaga ulanadi

```
              ┌──────────────────────────────────────────────┐
              │   BUSINESS PORTFOLIO  (business.facebook.com) │   ← «Business» shu
              │                                              │
              │   ┌─────────┐  ┌───────────┐  ┌───────────┐  │
              │   │Facebook │  │Ad Account │  │ Dataset   │  │   ← ASSETLAR
              │   │  Page   │  │  act_...  │  │(Events M.)│  │
              │   └────┬────┘  └─────┬─────┘  └─────┬─────┘  │
              │        │             │              │        │
              │   ┌────┴─────────────┴──────────────┴─────┐  │
              │   │        SYSTEM USER  («robot xodim»)   │  │   ← TOKEN shundan
              │   └───────────────────┬───────────────────┘  │
              └───────────────────────┼──────────────────────┘
                                      │  token(lar)
                        ┌─────────────┴──────────────┐
                        │        META APP            │           ← webhook shunda
                        └─────────────┬──────────────┘
                                      │
                       ┌──────────────┴───────────────┐
                       │   WunderkindLC → Marketing   │
                       │   → Sozlamalar → kartochkalar│
                       └──────────────────────────────┘

   Instagram akkaunt ──(Page'ga bog'lanadi)──> Facebook Page
   Instagram akkaunt ──(OAuth «Ulash»)───────> CRM  (izoh · DM · kontent)
```

**Uch bosqichli mantiq:**
1. **Assetlar** (Page, Ad Account, Dataset) Business portfolio **ichida** bo'lishi kerak;
2. **System User**ga o'sha assetlar **biriktiriladi** (Add assets);
3. System User **token** beradi → token CRM'ning tegishli kartochkasiga kiritiladi.

🔴 **Eng ko'p unutiladigan qadam — 2-si (Add assets).** Token qaysi obyektga huquq
berilmagan bo'lsa, uni **umuman ko'rmaydi** va xato «Ruxsat yetishmaydi» (`#200`/`#10`)
bo'lib chiqadi — go'yo token noto'g'ri kabi.

---

### ☐ 5.1. Facebook Page yaratish (agar yo'q bo'lsa)

1. `facebook.com` → chap menyu → **Pages** → **Create new Page**;
2. Nom (markaz nomi), kategoriya (masalan *Education* / *Tutoring service*), tavsif;
3. Profil rasmi va muqova qo'ying, kontakt ma'lumotlarini to'ldiring.

⚠️ Sahifa **haqiqiy va to'ldirilgan** bo'lsin: App Review paytida Meta uni ko'radi, bo'sh
sahifa rad javobining odatiy sabablaridan biri.

---

### ☐ 5.2. Instagram akkauntini Facebook Page'ga bog'lash

Reklama lidlari uchun Instagram akkaunt Page'ga **bog'langan** bo'lishi kerak. 2026-yilda
buning to'rt yo'li bor — **bittasi yetadi**:

| Yo'l | Qayerdan |
|---|---|
| Instagram ilovasidan | Profil → **Edit profile** → **Page** → sahifani tanlash |
| Facebook Page sozlamasidan | Page → **Settings** → **Linked accounts** → Instagram |
| **Business portfolio orqali (tavsiya)** | business.facebook.com → **Business settings → Accounts → Instagram accounts** → **Add** |
| Accounts Centre | Meta Accounts Centre → akkauntlarni bog'lash |

🔴 **Cheklov:** bitta Instagram akkaunt **bir vaqtda faqat bitta** Facebook Page'ga
bog'lanadi. Boshqasiga o'tkazish uchun avval uzish kerak.
⚠️ Instagram akkaunt **Professional** bo'lishi shart (shaxsiy akkaunt ro'yxatda chiqmaydi).

---

### ☐ 5.3. Business portfolio (Business Manager) yaratish va assetlarni ichiga olish

1. `business.facebook.com` → **Create account / Business portfolio**;
2. Biznes nomi (**haqiqiy nom** — verifikatsiyada hujjat bilan solishtiriladi), sizning
   ismingiz va ish email'ingiz;
3. **Business settings** ga kiring va assetlarni ichiga oling:

| Asset | Qayerda | Nima qilinadi |
|---|---|---|
| **Page** | Accounts → **Pages** | **Add → Add a Page** (allaqachon sizniki bo'lsa) yoki **Claim** |
| **Ad account** | Accounts → **Ad accounts** | **Add** (mavjudini olish) yoki **Create a new ad account** |
| **Instagram account** | Accounts → **Instagram accounts** | **Add** → Instagram login bilan tasdiqlash |
| **Dataset** | Data sources → **Datasets** (Events Manager) | **Add** yoki yangi dataset yaratish |
| **App** | Accounts → **Apps** | Meta App'ni portfolio ichiga **qo'shing** |

⚠️ **Ad account'ni «Request access» emas, «Add» qilib olish** — u sizniki bo'lsa to'liq
egalik kerak, aks holda System User'ga to'liq huquq bera olmaysiz.
⚠️ **App'ni ham portfolio'ga qo'shing:** App Review va Business Verification aynan
«ilova ortidagi biznes» bo'yicha tekshiriladi.

---

### ☐ 5.4. Business Verification (faqat Reklama lidlari uchun)

`business.facebook.com` → **Security Center** (yoki Business settings → Security Center) →
**Start verification**:

1. Biznes ma'lumotlari: yuridik nom, manzil, telefon, veb-sayt;
2. Hujjat yuklash: ro'yxatdan o'tish guvohnomasi / STIR / bank ma'lumotnomasi
   (nomi va manzili **kiritilgan ma'lumot bilan aynan** mos bo'lsin);
3. Telefon yoki email orqali tasdiqlash.

⏱ Odatda **3–10 kun**. Hujjatdagi nom/manzil farq qilsa rad etiladi — bu eng ko'p uchraydigan sabab.

⚠️ Verifikatsiya **`leads_retrieval` uchun majburiy**: usiz ilova qanchalik yaxshi
tayyorlangan bo'lsa ham **Standard Access**da qolib ketadi.
ℹ️ Reklama statistikasi va CAPI uchun odatda **kerak emas** (o'z kabinetimiz, Standard Access).

---

### ☐ 5.5. System User yaratish va **muddatsiz token** olish

> **Nega System User?** Oddiy foydalanuvchi tokeni ~60 kunda o'ladi va bir kuni statistika
> «sababsiz» to'xtaydi. System User tokeni — **muddatsiz**.

**A) Yaratish (bir marta):**
`business.facebook.com` → **Business settings → Users → System users** → **Add** →
nom bering (masalan `WunderkindLC`) → rol: **Admin**.

ℹ️ Bitta System User **uchala modul uchun** ham yetadi.

**B) Assetlarni biriktirish — 🔴 unutilmasin:**
System User → **Add assets** → har bir kerakli obyektga **to'liq huquq** (Full control):

| Modul | Qaysi asset biriktiriladi |
|---|---|
| Reklama lidlari | **Page** |
| Reklama statistikasi | **Ad account** |
| CAPI | **Dataset** |

**C) Token generatsiya qilish:** **Generate new token** → **ilovangizni tanlang** →
ruxsatlarni belgilang:

| Modul | Ruxsatlar |
|---|---|
| Reklama lidlari | `leads_retrieval` · `pages_show_list` · `pages_manage_ads` · `pages_read_engagement` |
| Reklama statistikasi | **`ads_read`** (+ `business_management`) |
| CAPI | **`ads_management`** (Dataset ustidan) |

⚠️ **Token faqat BIR MARTA ko'rsatiladi** — darhol nusxa oling va xavfsiz joyda saqlang.
⚠️ **`ads_management` ni statistika uchun so'ramang:** CRM reklamani boshqarmaydi, faqat
o'qiydi; ortiqcha ruxsat App Review talabini oshiradi.
⚠️ Texnik jihatdan bitta tokenga hamma ruxsatni berish mumkin, lekin **tavsiya etilmaydi**:
u bekor qilinsa **uchala modul birdan** to'xtaydi va sababini topish qiyinlashadi.

---

### ☐ 5.6. `leadgen` webhook'i (Facebook **Page** obyekti uchun)

Bu izoh/DM webhook'idan **alohida** obuna:

1. `developers.facebook.com` → ilovangiz → **Products → Webhooks**;
2. Obyekt sifatida **Page** ni tanlang (`Instagram` emas!);
3. **Callback URL** — CRM → Marketing → Sozlamalar → **«Reklama lidlari»** kartochkasidan
   nusxa oling. U `…/api/public/instagram/**leadgen**` bilan tugaydi;
4. **Verify token** — `.env` dagi `META_VERIFY_TOKEN` (bo'sh bo'lsa `INSTAGRAM_VERIFY_TOKEN`);
5. **Verify and Save** → maydonlardan **`leadgen`** ni belgilang (Subscribe).

| Obyekt | Callback URL |
|---|---|
| `instagram` (izoh · DM) | `https://<domen>/api/public/instagram/webhook` |
| **`page` (reklama lidlari)** | `https://<domen>/api/public/instagram/leadgen` |

⚠️ **Ayri Meta App ishlatsangiz** `.env` ga `META_APP_SECRET` va `META_VERIFY_TOKEN` ni ham
qo'shing. Bitta ilova bo'lsa ularni **bo'sh qoldiring** — `INSTAGRAM_*` qiymatlari ishlatiladi.

---

### ☐ 5.7. App Review — `leads_retrieval` (faqat reklama lidlari uchun)

Ilova → **App Review → Permissions and Features** → **Advanced Access** so'rang:
`leads_retrieval` (asosiysi), `pages_show_list`, `pages_manage_ads`, `pages_read_engagement`.

**2026-yilda Meta nimani ko'rmoqchi (rad javobining oldini olish):**

| Talab | Izoh |
|---|---|
| Business Verification tugagan | Usiz baribir Standard Access'da qolasiz |
| **Ishlaydigan** lid oqimi | Bo'sh dashboard emas — real (test) lid tizimga tushayotgani ko'rinsin |
| **Webhook** ishlatilgani ko'rsatilsin | Faqat polling emas, aynan `leadgen` webhook'i |
| Screencast | Page'ni ulash → ruxsat berish → lid interfeysda paydo bo'lishi — **uchchalasi** bir videoda |
| Use-case matni | Qaysi maydonlar olinadi, qayerda saqlanadi, kim ko'radi |
| Privacy Policy | Lid ma'lumotlari va **saqlash muddati** ochiq yozilgan bo'lsin |

**Namuna tushuntirish:** *«CRM tizimimiz o'z markazimizning Facebook/Instagram reklama
formalaridan kelgan lidlarni avtomatik qabul qiladi va sotuv bo'limi uchun lid kartochkasiga
aylantiradi. Boshqa bizneslarning ma'lumotlariga kirmaydi.»*

⏱ Odatda **3–10 kun** (Meta muddat kafolatlamaydi).

💡 **Tasdiqni kutmasdan sinash:** ilova **Development** rejimida bo'lsa va siz sahifaning
admini hamda ilovaning admin/developer/tester'i bo'lsangiz — webhook o'sha sahifa uchun
ishlaydi. Bu **sinov** yo'li, prod yechim emas: rejim yoki xodim o'zgarsa lidlar **jimgina**
kelmay qo'yadi.

---

### 5.8. Facebook tomonini ulash — yakuniy tekshiruv ro'yxati

- ☐ Facebook Page bor va to'ldirilgan;
- ☐ Instagram akkaunt shu Page'ga bog'langan;
- ☐ Business portfolio yaratilgan, ichida: Page · Ad account · Dataset · Instagram · App;
- ☐ Business Verification o'tgan (Lead Ads uchun);
- ☐ System User yaratilgan, **Add assets** qilingan, tokenlar olingan;
- ☐ `leadgen` webhook'i Page obyekti uchun ulangan va Subscribe qilingan;
- ☐ `leads_retrieval` uchun App Review yuborilgan.

---

<a id="6-blok-3-reklama-modullari"></a>

## 6. 3-BLOK — Reklama modullarini CRM'da sozlash

### ☐ 6.1. Reklama lidlari (Meta Lead Ads)

**Marketing → Sozlamalar → «Reklama lidlari (Lead Ads)»:**

1. **«Reklama lidlari yoqilgan»** — yoqing;
2. **Facebook Page ID** (Page → About yoki Business settings → Pages) va **Page Access Token**
   (§5.5) ni kiriting;
3. **«Sahifani ulash va tekshirish»** bosing — CRM tokenni tekshiradi **va** sahifani
   `leadgen` maydoniga **obuna qiladi**;
4. **«Saqlash»** (bayroq va lid manbasi shu bilan saqlanadi).

| Holat kartochkasi | Nima bo'lishi kerak |
|---|---|
| Modul | Yoqilgan |
| Facebook sahifa | sahifa **nomi** ko'rinadi |
| Page Access Token | Sozlangan |
| **Leadgen obunasi** | **Faol** |

⚠️ Obuna «Yo'q» bo'lsa lid **umuman kelmaydi** — odatiy sabab: tokenda `pages_manage_ads` yo'q.

**Sinash:** `developers.facebook.com/tools/lead-ads-testing` → sahifa va formani tanlang →
**Create lead** → 5–10 soniyada **Marketing → Reklama lidlari** da qator, **Lidlar** bo'limida
esa yangi kartochka paydo bo'ladi.

---

### ☐ 6.2. Reklama statistikasi (Ads Insights + ROI)

**Marketing → Sozlamalar → «Reklama statistikasi (Ads Insights)»:**

1. Bayroqni yoqing;
2. **Reklama akkaunti ID** — `act_1234567890` yoki faqat raqamlar (prefiksni CRM o'zi qo'shadi);
3. **System User tokeni** (`ads_read`) ni kiriting;
4. **«Ulash va tekshirish»** — CRM **saqlashdan oldin** Meta'ga so'rov yuboradi va kabinet
   **nomi**, **valyutasi**, **vaqt zonasi**ni oladi.

🔴 Tekshiruv o'tmasa **hech narsa saqlanmaydi** — bu ataylab, aks holda nosozlik
«ulandi, lekin statistika yo'q» bo'lib bir haftadan keyin sezilardi.

5. **«Hoziroq sinxronlash»** bosing.

| Sinxronizatsiya | Oraliq |
|---|---|
| Birinchi ulanish | oxirgi **90 kun**, **10 kunlik** bo'laklarda |
| Har kuni **5:00** da | oxirgi **7 kun** qayta yuklanadi (Meta atributsiyani 48 soatgacha tuzatadi) |
| Qo'lda | tanlangan oraliq (ko'pi bilan 365 kun) |

⚠️ Bir vaqtda **faqat bitta faol** reklama akkaunti bo'ladi; yangisi ulanganda eskisi uziladi,
lekin **yig'ilgan statistikasi o'chirilmaydi**.

---

### ☐ 6.3. CAPI — lid sifatini Meta'ga qaytarish

**1) Events Manager'da bosqichlar:** `business.facebook.com/events_manager` → Dataset →
**Settings → Lead stages** → ikkita bosqich qo'shing, masalan `Sifatli lid` va `To'lov qildi`.

🔴 **Nom harfma-harf mos bo'lishi SHART.** `Sifatli lid` va `sifatli lid` — Meta uchun ikki
xil bosqich. Mos kelmasa so'rov **200 OK** qaytadi, hodisa qabul qilinadi, lekin hech qaysi
bosqichga tushmaydi — nosozlik **jimgina** yuz beradi.

**2) Dataset ID va token:** Events Manager → Dataset → Settings → **Dataset ID**; token —
`ads_management` ruxsatli System User tokeni (Dataset ustidan huquqi bilan).

**3) CRM:** Sozlamalar → **«Lid sifatini Meta'ga qaytarish (CAPI)»** → bayroq · Dataset ID ·
token · ikkala bosqich nomi → **«CAPI sozlamalarini saqlash»** → **«Hoziroq yuborish»**.

**Hodisa xaritasi:**

| CRM'da nima bo'ldi | CAPI hodisasi | Hodisa vaqti |
|---|---|---|
| Reklama lidi yaratildi | ❌ yuborilmaydi (Meta buni allaqachon biladi) | — |
| Lid **o'quvchiga aylantirildi** | «Sifatli lid» | skan vaqti |
| Lid kanbanda «sifatli» ma'noli bosqichga o'tdi | «Sifatli lid» | skan vaqti |
| Lid bo'yicha **birinchi `tuition` to'lovi** | «To'lov qildi» + summa (`value`, UZS) | **birinchi to'lov sanasi** |

⚠️ CAPI **faqat reklama formasidan (Instant Form) kelgan lidlar** uchun ishlaydi — DM/izoh
lidlari bu navbatga umuman tushmaydi.
⚠️ Modul birinchi yoqilganda 7 kundan eski to'lovlar `skipped` bo'ladi — bu **normal**.
⚠️ Meta'ning «Conversion Leads» optimizatsiyasi uchun rasmiy talab: oyiga **200+ lid**,
konversiya **1–40%**, maqsadli bosqich **28 kun ichida**. Talab bajarilmasa ham modulni
yoqib qo'ying — hodisalar atributsiya hisobotlarida baribir ko'rinadi.

---

### ☐ 6.4. Kontent joylash — **akkauntni qayta ulash**

Bu modul uchun Instagram Login tokeniga **yangi scope** kerak:
`instagram_business_content_publish`.

🔴 **Yangi scope mavjud tokenga avtomatik qo'llanmaydi** — OAuth ruxsatlari token olingan
paytda muzlatiladi.

1. **Marketing → Sozlamalar → Instagram kartochkasi → «Qayta ulash»**;
2. Ruxsatlar ro'yxatida **kontent joylash** ham ko'rinadi — tasdiqlang;
3. Sozlamalar → **«Kontent joylash yoqilgan»** bayrog'ini yoqing.

⚠️ Qayta ulash **hech narsani buzmaydi**: suhbatlar, lidlar, qoidalar, bilim bazasi joyida
qoladi. Yangilanadigan narsa — token, akkaunt ID lari va webhook obunasi.
⚠️ **CRM sizda bu ruxsat bor-yo'qligini aniq ayta olmaydi** (berilgan scope'lar ro'yxati
saqlanmaydi) — diagnostikada «noma'lum» deb turadi. Ruxsat yo'qligi birinchi postda ma'lum bo'ladi.

**Server talabi:** 🔴 Meta media faylni **o'zi yuklab oladi** — manzil **ochiq HTTPS**
bo'lishi shart (autentifikatsiya, IP cheklov, redirect ishlamaydi). Shuning uchun alohida
ochiq papka bor: `/uploads/marketing-public/{32 hex}.jpg|.jpeg|.mp4|.mov`. **Lokal muhitda
(http) post joylab bo'lmaydi** — validatsiya buni ataylab rad etadi.

---

<a id="7-sahifalar"></a>

## 7. Sahifalar bo'yicha qo'llanma — nima qiladi, qanday ishlatiladi

### 7.1. Boshqaruv paneli — `/admin/marketing`

**Nima:** modulning «sog'lig'i» bir ekranda.

| Blok | Nima ko'rsatadi | Qachon qarash kerak |
|---|---|---|
| **Navbat holati** | `pending` / `done` / `error` hodisalar soni | `pending` to'planib qolsa — modul o'chiq yoki xato bor |
| **Operator kerak** | `NeedsOperator` holatidagi suhbatlar | **Har kuni** — bu javobsiz qolgan mijozlar |
| **Oxirgi qaynoq suhbatlar** | AI «qaynoq lid» deb belgilagan suhbatlar | Sotuv bo'limi shulardan boshlaydi |

**Kundalik odat:** ertalab shu sahifani ochish → «Operator kerak» ro'yxatini yopish.

### 7.2. Inbox — `/admin/marketing/inbox`

**Nima:** Instagram suhbatlari (DM va izohlar) CRM ichida.

**Qanday ishlatiladi:**
- chapda suhbatlar ro'yxati, o'ngda yozishmalar tarixi;
- **«Yuborish»** — operator o'zi javob yozadi;
- operator qo'lda javob yozgan zahoti **bot o'sha suhbatda jim turadi** (pauza);
- botni qaytarish uchun — **«Botni qaytarish»**.

⚠️ **DM oynasi 24 soat:** mijoz oxirgi marta 24 soatdan oldin yozgan bo'lsa Instagram DM
yuborishga ruxsat bermaydi. Bu Meta cheklovi, CRM kamchiligi emas.
⚠️ Izohga **private reply** — izohdan **7 kun** ichida va **bir marta**.
⚠️ Mijoz xabarni o'chirsa mazmun **haqiqatan o'chadi** (Meta Platform Terms talabi).

### 7.3. Javob qoidalari — `/admin/marketing/rules`

**Nima:** kalit so'zga qarab **AI'ni chaqirmasdan** tayyor javob berish.

| Ustun | Ma'nosi |
|---|---|
| **Qachon ishlaydi** | kalit so'z(lar), kanal (izoh / DM), holat |
| **Nima qiladi** | qaytariladigan tayyor matn |

**Qachon kerak:** tez-tez takrorlanadigan, aniq javobli savollar («manzil», «ish vaqti»,
«telefon raqamingiz»). Qoida mos kelsa **AI umuman chaqirilmaydi** — arzon va bir xil javob.

💡 Analitikadagi **«Eng ko'p ishlagan qoidalar»** ga qarab qoidalarni to'ldirib boring.

### 7.4. Bilim bazasi — `/admin/marketing/knowledge`

**Nima:** AI **faqat shu yerdan** gapiradi. Post matni (AI caption) ham shundan quriladi.

**Qanday ishlatiladi:** bo'lak-bo'lak (sarlavha + matn) qo'shiladi, tartibini o'zgartirish
mumkin. Narx o'zgarsa — **birinchi navbatda shu yerni** yangilang.

**RAG (E6.5):** baza kattalashganda hammasi promptga tiqilmaydi — savolga **eng yaqin 6
bo'lak** tanlanadi. Vektorlarni fon xizmati **har 60 soniyada** hisoblab boradi, qo'lda hech
narsa qilish kerak emas. Vektor bo'lmasa tizim **eski yo'lga qaytadi** — RAG modulni hech
qachon to'xtatmaydi.

### 7.5. Analitika — `/admin/marketing/analytics`

| Grafik | Nima |
|---|---|
| Kunlik oqim | xabar/izoh soni |
| Kunlik lidlar | shu oqimdan nechta lid chiqdi |
| Niyat bo'yicha | narx so'radi / ro'yxatga yozildi / shikoyat … |
| Til bo'yicha | lotin · kirill · rus · ingliz |
| Kanal bo'yicha | izoh vs DM |
| Eng ko'p ishlagan qoidalar | qaysi kalit so'zlar tez-tez ishlayapti |

**Nima uchun kerak:** kontent rejasini shu yerdan quring — qaysi savol ko'p bo'lsa, o'sha
mavzuda post kerak.

### 7.6. Reklama lidlari — `/admin/marketing/reklama-lidlari`

**Nima:** Instant Form'dan kelgan lidlar ro'yxati (F.I.Sh., telefon, kampaniya, vaqt).

**Qanday ishlatiladi:**
- har qator CRM'dagi **Lead** kartochkasiga bog'langan;
- ism/telefon kelmagan bo'lsa — qatordagi **«Qayta olish»** (token tuzatilgandan keyin);
- dublikat lid **ochilmaydi**: o'sha telefon bo'lsa mavjud lidning `RepeatCount` i oshadi.

⚠️ Meta lidni **~90 kun** saqlaydi — undan eskisini «Qayta olish» ham ololmaydi.
⚠️ Lidlar bo'limida kamida bitta **bosqich** (`LeadStage`) bo'lmasa lid CRM'ga yozilmaydi.

### 7.7. Reklama statistikasi — `/admin/marketing/reklama-statistikasi`

| Blok | Nima |
|---|---|
| Filtr | sana oralig'i · platforma (Hammasi / Instagram / Facebook) · kampaniya |
| **KPI (7 ta)** | Xarajat · Ko'rsatish · Qamrov · CRM lidlari · **CPL** · O'quvchi bo'ldi · **ROI** |
| Grafiklar | kunlik xarajat · kunlik lidlar · platforma ulushi |
| Jadval | kampaniya → adset → e'lon daraxti, ROI ustunlari, «Lidlarni ko'rish →» |
| Holat | oxirgi yangilash · oxirgi xato · «Qayta urinish» |

**ROI ustunlari qayerdan:**

| Ustun | Formulasi |
|---|---|
| **CPL** | Xarajat ÷ CRM lidlari |
| **CAC** | Xarajat ÷ to'lov qilganlar |
| **ROI** | (Daromad − Xarajat) ÷ Xarajat → `1.5` = **+150%** |

🔴 **Hisobotni noto'g'ri o'qimaslik uchun 5 ta ogohlantirish:**

1. **Qamrov ≈ taxminiy.** Statistika platforma va kun kesimida yuklanadi, Meta bunday
   qatorlarni dedup qilmaydi. Shuning uchun «kamida» (MAX) va «ko'pi bilan» (SUM) chegara
   beriladi va raqam **«≈»** bilan turadi.
2. **«Meta lidlari» ≠ «CRM lidlari» — bu normal** (telefon dublikati, 90 kunlik devor, piksel
   lidlari). Farq **doimiy va katta** bo'lsa (Meta 100 / CRM 30) — bu lid oqimidagi nosozlik.
3. **Xarajat — faqat tanlangan oraliqda, daromad — butun umr bo'yi.** ROI ni «sof foyda» deb
   o'qib bo'lmaydi; yangi kampaniyada ROI past ko'rinadi, chunki lidlar hali to'lay boshlamagan.
4. **Valyuta:** kabinet `USD` da bo'lsa xarajat dollarda, to'lovlar so'mda — CRM kurs
   qo'llamaydi, ekranda ochiq ogohlantirish chiqadi.
5. **Vaqt zonasi:** xarajat sanasi kabinet zonasida, lidlar Toshkent vaqtida — chegaradagi
   kunlarda **bir kunlik siljish** bo'lishi mumkin.

### 7.8. Kontent — `/admin/marketing/kontent`

**Nima:** Instagram postlarini CRM'dan rejalashtirib joylash.

**Post yaratish:** **«Yangi post»** → tur (rasm / Reels / Story / karusel) → media yuklash →
matn → vaqt (bo'sh qoldirilsa **hozir**) → saqlash.

⚠️ **Tekshiruv saqlashda bo'ladi**, joylash paytida emas — xato darhol aytiladi.

**Media talablari:**

| Tur | Format | Hajm | Davomiylik | Nisbat |
|---|---|---|---|---|
| Rasm (feed) | **faqat JPEG** | ≤ 8 MB | — | 4:5–1.91:1, kenglik 320–1440 px |
| Reels / video | MP4 / MOV | ≤ 300 MB | 3–900 s | **9:16** |
| Story — rasm | JPEG | ≤ 8 MB | — | 9:16 |
| Story — video | MP4 / MOV | ≤ 100 MB | 3–60 s | 9:16 |
| Karusel | 2–10 element | har biri o'z turi bo'yicha | | nisbat **birinchi element** bo'yicha |

**Matn:** ≤ 2200 belgi · ≤ 30 hashtag · ≤ 20 mention.
⚠️ **PNG, WebP, HEIC qabul qilinmaydi.** Karusel elementiga alohida matn yozib bo'lmaydi.
Story'da caption ko'rinmaydi.

**Holatlar:** `scheduled` → `processing` → `published` / `failed` / `cancelled`.

🔴 **Instagram'da native rejalashtirish YO'Q:** vaqt bizning navbatda turadi, konteyner
**faqat chop etish vaqti kelganda** yaratiladi (konteyner 24 soatda o'ladi). Server o'chib
qolsa reja **bazada** turadi — post kechikadi, lekin yo'qolmaydi.

🔴 **Joylangan postni orqaga qaytarib bo'lmaydi:** API orqali tahrirlash ham, o'chirish ham
mumkin emas. CRM'dan o'chirsangiz **faqat CRM yozuvi** o'chadi, Instagram'dagi post qoladi.

**AI matn yozdirish:** matn maydoni tepasidagi **«Matn yozdirish»** → mavzu + post turi + til
(lotin/kirill/rus/ingliz) + uslub (Samimiy · Ishonchli · Jonli · Sotuvga yo'naltirilgan).
AI **bilim bazasidan** yozadi va **narxni o'ylab topmaydi**. Matn bor bo'lsa ustiga jimgina
yozilmaydi — **«Almashtirish» / «Oxiriga qo'shish» / «Boshqattan yozdirish»** tanlanadi.

### 7.9. Javob sifati — `/admin/marketing/javob-sifati`

**Nima:** «AI shunday dedi → operator shunday yozdi» juftliklari. Promptni va bilim bazasini
yaxshilash uchun eng qimmatli ma'lumot.

**Qanday ishlatiladi:** operator AI matnini muntazam tuzatayotgan mavzuni toping → **bilim
bazasiga** yoki **javob qoidasiga** qo'shing.

🔴 Bu hisobotda **mijozning hech qanday belgisi yo'q** — na ism, na telefon, na mijoz yozgan
matn. Bu **ichki sifat** ma'lumoti; «kim bilan yozishilgani» savolining joyi — **Inbox**.

### 7.10. Sozlamalar — `/admin/marketing/settings`

Bo'limlari (kartochkalar):

| Kartochka | Ichida |
|---|---|
| **Instagram akkaunt** | ulash / qayta ulash · username · **token muddati** · webhook obunasi |
| **Meta ilovasi** | Instagram App ID · `.env` kalitlari holati · **Webhook URL** va **OAuth callback URL** (nusxa olish uchun) |
| **Avtojavob** | modul bayrog'i · izoh / DM / private reply · Telegram bildirishnomasi |
| **AI va chegaralar** | Gemini modeli · javob kechikishi · kunlik chegara · salomlashuv matni · lid manbasi |
| **Reklama lidlari (Lead Ads)** | Page ID · Page Access Token · leadgen obunasi · kelgan lidlar soni |
| **Reklama statistikasi** | Ad Account ID · token · valyuta va vaqt zonasi · oxirgi sinxronizatsiya |
| **CAPI** | Dataset ID · token · bosqich nomlari · navbat holati |
| **Kontent joylash** | modul bayrog'i · kunlik limit |
| **Diagnostika** | `webhookSubscribed`, navbat, bugungi javoblar, bilim bazasi holati |

⚠️ **Tokenlar hech qachon ekranda ko'rsatilmaydi** — faqat «Sozlangan / Sozlanmagan».
Forma har safar bo'sh ochiladi va **bo'sh yuborilgan maydon mavjud qiymatni O'CHIRMAYDI**
(ya'ni faqat ID ni tuzatish uchun tokenni qayta yozish shart emas).
⚠️ Ba'zi kartochkalar **o'z «Saqlash» tugmasi** bilan saqlanadi — umumiy saqlash ularga tegmaydi.

---

<a id="8-ish-tartibi"></a>

## 8. Kundalik ish tartibi

| Qachon | Kim | Nima qiladi |
|---|---|---|
| **Har kuni ertalab** | Operator | Boshqaruv paneli → **«Operator kerak»** ro'yxatini yopish; Inbox'dagi javobsiz suhbatlar |
| **Har kuni** | Sotuv | **Reklama lidlari** va **Lidlar** bo'limidagi yangi lidlar bilan qo'ng'iroq |
| **Haftada 1** | Marketing | **Reklama statistikasi** → CPL va ROI; yomon ishlayotgan adset'ni o'chirish |
| **Haftada 1** | SMM | **Kontent** → kelgusi hafta postlarini rejalashtirish (Analitikadagi savollarga qarab) |
| **Haftada 1** | Rahbar/SMM | **Javob sifati** → AI ko'p adashayotgan mavzuni bilim bazasiga qo'shish |
| **Oyda 1** | Texnik | Sozlamalar → token muddati · diagnostika · CAPI navbatidagi `failed` qatorlar |
| **Narx o'zgarganda** | Administrator | **Bilim bazasi** ni darhol yangilash (AI eski narxni aytmasin) |

---

<a id="9-diagnostika"></a>

## 9. Diagnostika — birlashgan xatolar jadvali

### 9.1. Ulanish va Instagram agenti

| Alomat | Sabab | Yechim |
|---|---|---|
| Webhook «Verify and save» **qizil** | Verify token mos emas · manzil tashqaridan ochiq emas · HTTP ishlatilgan | §4.5 |
| **«Ulash» tugmasi o'chiq** | App ID / App Secret / Verify token yo'q yoki saqlanmagan | §4.3–4.4 |
| `Invalid redirect_uri` | Manzil aynan mos emas (oxirgi `/`, http/https, **noto'g'ri joyga yozilgan**) | §4.2 |
| `Invalid platform app` | **Meta App ID** kiritilgan, **Instagram App ID** o'rniga | §4.1 |
| «Kod muddati o'tgan» | Authorize kodi **1 soat** va **bir marta** ishlaydi | Tugmani qayta bosing |
| Akkaunt ro'yxatda yo'q | Akkaunt hali **Professional** emas | Instagram sozlamalari |
| Bot umuman javob bermayapti | Modul o'chiq · avtojavob bayrog'i o'chiq · Gemini kaliti yo'q · webhook obunasi yo'q · token o'lgan | Sozlamalar → diagnostika |
| Navbatda `pending` to'planib qolgan | `InstagramEnabled=false` yoki xato | `GET /events` xatolar matni bilan |
| Bot **o'ziga** javob berayapti 🚨 | Saqlangan akkaunt ID lari bo'sh | **Darhol modulni o'chiring** va qayta ulang |
| Narx o'rniga «operator bog'lanadi» | Bilim bazasida narx yo'q | §4.7 |
| Bot bitta suhbatda jim | Operator qo'lda javob bergan → **pauza** | Ataylab. Inbox → «Botni qaytarish» |
| «DM oynasi yopiq» | Mijoz **24 soatdan** oldin yozgan | Instagram cheklovi |
| Token muddati tugagan | 45-kunda avtomatik yangilanadi; xato bo'lsa Telegram alert | «Tokenni yangilash» yoki qayta ulash |

### 9.2. Reklama lidlari

| Alomat | Sabab | Yechim |
|---|---|---|
| Meta «Verify and Save» ni qabul qilmayapti | `…/webhook` manzili qo'yilgan | Manzil `…/**leadgen**` bo'lsin |
| Ro'yxat bo'sh | `leadgen` **obunasi yo'q** | Sozlamalar → «Leadgen obunasi» kartochkasi |
| Lid keldi, **ism/telefon yo'q** | Page tokeni yo'q / muddati tugagan / `leads_retrieval` yo'q | Tokenni yangilang → qatordagi «Qayta olish» |
| «Ruxsat yetishmaydi» | App Review o'tmagan yoki tokenda ruxsat yo'q | §5.7 |
| «Token muddati tugagan» | Oddiy foydalanuvchi tokeni (~60 kun) | **System User** tokeniga o'ting |
| Lid CRM'da yangi emas | O'sha telefonli lid bor edi | Ataylab: `RepeatCount` oshadi |
| Lid keldi, CRM'da yo'q | **Lid bosqichi yo'q** (`LeadStage` bo'sh) | Lidlar bo'limida bosqich yarating |

### 9.3. Reklama statistikasi

| Alomat | Sabab | Yechim |
|---|---|---|
| `#200` / `#10` «Ruxsat yetishmaydi» | Tokenda `ads_read` yo'q **yoki** System User'ga **Ad account biriktirilmagan** | §5.5-B |
| `190` «Token yaroqsiz» | Page tokeni qo'yilgan | System User tokeni oling |
| «Reklama akkaunti ID noto'g'ri» | ID da raqamdan boshqa belgi | `act_1234567890` yoki faqat raqam |
| Ulandi, statistika **bo'sh** | Kabinet faol emas (`account_status ≠ 1`) yoki oraliqda sarf yo'q | Ads Manager'da kabinetni tekshiring |
| `80000` «So'rovlar chegarasi» | `ads_insights` kvotasi tugagan | **Qo'lda qayta bosmang** — CRM o'zi kutib qayta uradi |
| `100/1487534` «juda ko'p ma'lumot» | Oraliq katta | CRM o'zi bo'lib qayta so'raydi; qo'lda bo'lsa oraliqni qisqartiring |
| ROI ustunlari bo'sh | Reklama lidlari ulanmagan yoki lidlar `CampaignId` siz | §6.1 |
| Ads Manager bilan **1 kunlik farq** | Vaqt zonasi | Normal (§7.7) |

### 9.4. Kontent

| Alomat | Sabab | Yechim |
|---|---|---|
| «Ruxsat yetishmaydi … `instagram_business_content_publish`» | Eski token | **«Qayta ulash»** (§6.4) |
| **`2207052`** «Media yuklab bo'lmadi» | 🔴 **Eng ko'p uchraydigan.** Server tashqaridan ochiq emas / HTTPS yo'q | Fayl manzilini **boshqa tarmoqdagi** qurilmada brauzerda oching |
| `2207005` | PNG/WebP/HEIC yuborilgan | JPEG qiling |
| `2207009` | Nisbat noto'g'ri | feed 4:5–1.91:1, story/reels 9:16 |
| `2207010` | Matn > 2200 belgi | Qisqartiring |
| `2207026` | Video kodeki | MP4 (H.264) + AAC qilib qayta saqlang |
| `2207020` | Konteyner 24 soatda o'ldi | Qayta urinish |
| `2207042` | Kunlik limit to'ldi | Post `scheduled` bo'lib qoladi va o'zi joylanadi |
| `2207001` | Spam deb belgilandi | Matn va hashtaglarni o'zgartiring |
| Post `processing` da uzoq | Video qayta kodlanmoqda | 10 daqiqagacha normal |
| Post ikki marta chiqdi | Kamdan-kam; endi qulf bilan yopilgan | Ikkinchisini **Instagram ilovasidan** o'chiring |

### 9.5. CAPI

| Alomat | Sabab | Yechim |
|---|---|---|
| Hodisalar `sent`, Events Manager'da **ko'rinmaydi** | 🔴 Bosqich nomi mos emas | §6.3 — harfma-harf solishtiring (Meta xato bermaydi!) |
| `190` | Boshqa modulning tokeni | Dataset tokenini oling |
| `10`/`200`/`299` | `ads_management` yo'q yoki Dataset asset emas | System User'ga Dataset biriktiring |
| `803` | Dataset ID xato | Events Manager → Settings dan qayta nusxa oling |
| Navbatda hammasi `skipped` | To'lovlar 7 kundan eski | Normal — yangi to'lovlar odatdagidek ketadi |
| «Conversion Leads» yoqilmayapti | Oyiga 200 lid / 1–40% konversiya talabi | Hodisalarni yuborishda davom eting |

---

<a id="10-muddatlar"></a>

## 10. Muddatlar, limitlar va kvotalar

| Narsa | Qiymat |
|---|---|
| OAuth kodi | **1 soat**, **bir marta** |
| Instagram Login tokeni | **~60 kun** (45-kunda avtomatik yangilanadi) |
| System User tokeni | **muddatsiz** |
| DM yuborish oynasi | mijoz oxirgi yozganidan **24 soat** |
| Izohga private reply | **7 kun** ichida, **bir marta** |
| Meta webhook javobini kutishi | **5 soniya** (CRM darhol 200 qaytaradi) |
| Kunlik javob chegarasi | **200** (sozlanadi) |
| Halqadan himoya | post bo'yicha **8/10 daq** · global **30/10 daq** |
| Meta lidni saqlash muddati | **~90 kun** |
| Business Verification + App Review | odatda **3–10 kun** |
| Statistika: birinchi yuklash | **90 kun**, **10 kunlik** bo'laklarda |
| Statistika: har kuni qayta yuklanadi | oxirgi **7 kun**, soat **5:00** da |
| Statistika: hisobotda eng uzun oraliq | **400 kun** |
| Statistika: kvotada to'xtash | **95%** |
| Media konteyneri umri | **24 soat** |
| Post navbati | har **30 soniya**, tsiklda **3 ta** post, **3** urinish |
| Kontent kunlik limiti | Meta beradi (hujjatlar zid: 50 yoki 100) |
| CAPI: `event_time` eng eskisi | **7 kun** |
| CAPI: bir so'rovda | **1000** hodisa · ishga tushishda 5000 qator |
| CAPI: Meta dedup oynasi | **48 soat** |
| Meta atributsiyani tuzatishi | **48 soatgacha** |

---

<a id="11-xavfsizlik"></a>

## 11. Xavfsizlik, maxfiylik va audit

| Qoida | Nima uchun |
|---|---|
| `INSTAGRAM_APP_SECRET` va `INSTAGRAM_VERIFY_TOKEN` **faqat `.env` da** | Baza dump'i Telegram'ga yuboriladi — kalit sizib chiqmasin |
| **Tokenlar auditga yozilmaydi** | Tarixni ko'rgan xodim tokenni olib qololmasin (Page ID / Dataset ID esa yoziladi — ular sir emas) |
| Webhook imzosi **fail-closed** | App Secret bo'sh bo'lsa so'rov **rad etiladi** — aks holda istalgan odam bizning nomimizdan hodisa yubora olardi |
| CAPI navbatida **xom telefon/email saqlanmaydi** | Faqat SHA-256 hash. DPA aynan shuni tekshiradi |
| Ism/familiya CAPI'ga **yuborilmaydi** | «Ism Familiya» va «Familiya Ism» chalkashligi moslikni oshirmaydi |
| `uploads/marketing-public/` — **faqat post medialari** | Hujjat, shartnoma, sertifikat, o'quvchi surati **hech qachon**; fayl nomi tasodifiy, uch bosqichli tekshiruv |
| Audit bo'limi: **Marketing** (`EntityType = "Instagram"`) | Akkaunt ulash/uzish, sozlama o'zgarishi, qo'lda joylash/yuborish — hammasi «O'zgarishlar tarixi» da |

**Ruxsatlar (rollarni sozlashda):** sahifalarni ko'rish uchun `marketing.*` kalitlari,
**ulash/uzish, token kiritish va modul bayroqlari** uchun esa **`marketing.settings`** kerak.
Ya'ni SMM xodimiga `marketing.content` bering, tokenlarga tegish huquqini bermang.

---

<a id="12-meta-holati"></a>

## 12. 2026-08 holati — Meta tomonidagi o'zgarishlar

Quyidagilar hujjat yozilgan paytdagi (2026-yil avgust) rasmiy holat asosida tekshirildi:

| Mavzu | Holat |
|---|---|
| **Graph API versiyasi** | Kodda **v23.0** (`IgConst.GraphVersion` / `FbGraphVersion`). Meta'dagi eng so'nggi — **v26.0** (2026-07-29 chiqdi), v25.0 ham qo'llab-quvvatlanadi. v20.0 → **2026-09-24**, v21.0 → **2027-01-21** da o'chadi. **v23.0 hozircha xavfsiz**, lekin versiyani ko'tarish rejaga qo'yilsin |
| **Instagram Login scope nomlari** | `instagram_business_basic` · `instagram_business_manage_messages` · `instagram_business_manage_comments` · `instagram_business_content_publish`. Eski (2025-01-27 gacha bo'lgan) nomlar **o'chirilgan** |
| **Facebook Page kerakmi (izoh/DM)** | **Yo'q** — Meta rasman yozadi: bu setup Page bog'lanishini talab qilmaydi |
| **`leads_retrieval`** | **Advanced Access + Business Verification majburiy.** Screencast'da webhook orqali real lid oqimi ko'rinishi shart; bo'sh dashboard bilan rad etiladi |
| **Bir Instagram → bir Page** | Instagram akkaunt bir vaqtda faqat **bitta** Facebook Page'ga bog'lanadi |
| **v26.0 dagi o'chirishlar** | 47 ta Commerce Order Management endpoint'i, Instagram Explore placement, Messenger Stories targeting va h.k. — **bizning modullarga tegmaydi** (biz ularni ishlatmaymiz) |

### Manbalar

- [Instagram API with Instagram Login — Meta for Developers](https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/)
- [Permissions Reference — Meta for Developers](https://developers.facebook.com/docs/permissions/)
- [Lead Ads — Meta for Developers](https://developers.facebook.com/documentation/ads-commerce/marketing-api/guides/lead-ads)
- [Retrieving Leads — Meta for Developers](https://developers.facebook.com/documentation/ads-commerce/marketing-api/guides/lead-ads/retrieving)
- [Enable Leads Access in Meta Business Suite](https://www.facebook.com/business/help/618808448980683)
- [leads_retrieval Approval Guide (2026)](https://singhamandeep.com/leads-retrieval-permission-approval-facebook-lead-ads-api/)
- [Meta Advanced Access: Which Permissions Need App Review](https://singhamandeep.com/what-is-meta-advanced-access/)
- [Graph API v26.0 relizi (2026-07-29)](https://ppc.land/meta-blocks-47-commerce-endpoints-as-graph-api-v26-0-lands-today/)
- [How to Link Instagram to a Facebook Page (2026)](https://www.leadsie.com/blog/link-instagram-facebook-page)

---

<a id="13-faq"></a>

## 13. FAQ — tez-tez so'raladigan savollar

**S: Facebook Page'im yo'q. Marketing bo'limini ishlata olamanmi?**
J: Ha — **izoh · DM agenti**, **kontent joylash**, **analitika**, **javob sifati** Page'siz
ishlaydi. **Reklama lidlari** esa Page'siz **umuman ishlamaydi**.

**S: Instagram'ni ulash uchun Business Manager kerakmi?**
J: Yo'q. Izoh/DM/kontent uchun faqat Instagram Professional akkaunt va Meta App yetadi.
Business Manager **faqat reklama modullari** (lidlar, statistika, CAPI) uchun kerak.

**S: Bitta token bilan hammasini qilsam bo'ladimi?**
J: Texnik jihatdan bitta System User'ga hamma ruxsatni berish mumkin, lekin **tavsiya
etilmaydi**: token bekor qilinsa uchala modul birdan to'xtaydi. Instagram Login tokeni esa
baribir alohida — u OAuth bilan olinadi.

**S: App Review'ni kutmasdan reklama lidlarini sinasam bo'ladimi?**
J: Ha, ilova **Development** rejimida va siz Page admini + ilova admin/developer/tester
bo'lsangiz. Bu **faqat sinov** uchun — prodda ishonchli emas.

**S: Bot noto'g'ri narx aytdi. Nima qilaman?**
J: Narx **faqat bilim bazasidan** olinadi. Bilim bazasini yangilang. Bo'lmasa AI narx
umuman aytmaydi — «operator bog'lanadi» deydi.

**S: Postni joyladim, o'chirmoqchiman.**
J: API orqali **mumkin emas** — Instagram ilovasidan o'chiring. CRM'dan o'chirish faqat
CRM yozuvini o'chiradi.

**S: Statistikadagi ROI Ads Manager'dagiga to'g'ri kelmayapti.**
J: Bu normal — ROI CRM'dagi **haqiqiy to'lovlar** bo'yicha hisoblanadi (Ads Manager buni
bilmaydi), xarajat sanasi esa kabinet vaqt zonasida kesiladi. §7.7 dagi 5 ta ogohlantirishni
o'qing.

**S: Tokenim o'ldi — suhbatlar va lidlar yo'qoladimi?**
J: Yo'q. **«Qayta ulash»** faqat tokenni, akkaunt ID larini va webhook obunasini yangilaydi.
Suhbatlar, lidlar, qoidalar, bilim bazasi joyida qoladi.

---

## Batafsil hujjatlar (loyiha ichida)

| Savol | Fayl |
|---|---|
| Izoh/DM agentini qadamma-qadam sozlash | `instagram/SOZLASH.md` |
| Meta bilan qaysi so'rov almashinadi, xato kodlari | `instagram/TEXNIK.md` |
| Reklama lidlari | `instagram/REKLAMA-LIDLARI.md` |
| Reklama statistikasi va ROI | `instagram/REKLAMA-STATISTIKASI.md` |
| Kontent joylash va AI caption | `instagram/KONTENT.md` |
| CAPI | `instagram/CAPI.md` |
| Meta API to'liq ma'lumotnomasi | `instagram/META-API-MALUMOTNOMA.md` |
| Kod yozayotganda nimaga tegmaslik kerak | `.claude/rules/marketing-instagram.md` |
