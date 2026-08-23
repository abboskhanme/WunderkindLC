# Instagram AI agentini sozlash — bosqichma-bosqich

> Kimga: markaz administratori / texnik mas'ul uchun. Kod bilishi shart emas, lekin serverga
> `.env` yozish va `docker compose up -d` qilish imkoni bo'lishi kerak.
>
> **Real vaqt: ~30–40 daqiqa.**

---

## 🟢 ENG MUHIM XABAR — App Review KERAK EMAS

Meta hujjati aniq yozadi:

> *"Standard Access is the default access level for all apps… **If your app only serves your
> Instagram professional account or an account you manage, Standard Access is all your app
> needs.**"*

| Holat | App Review |
|---|---|
| Ilova **faqat O'Z akkauntimizga** xizmat qiladi (bizning holat) | **KERAK EMAS** |
| Ilovani boshqa markazlarga sotsak (ular o'z akkauntini ulasa) | KERAK |

Ilovani **Live rejimga** o'tkazish ham shart emas — Standard Access development rejimda
o'z akkauntimiz uchun ishlayveradi.

Shuningdek **KERAK EMAS**: Facebook Page, Business Verification, biznes hujjatlari.
Sabab: biz **Instagram Login** yo'lidan boramiz (Facebook Login emas).

> ⚠️ Internetdagi va eski hujjatlardagi *"App Review 1–3 hafta kutiladi"* degan ma'lumot
> **Facebook Login** yo'liga tegishli. Bizga taalluqli emas.

> 🔴 **LEKIN — REKLAMA LIDLARI (Lead Ads) uchun bu ISTISNO ISHLAMAYDI.** Target reklamadagi
> forma orqali kelgan lid Facebook **Page** obyektidan keladi va u uchun Facebook Page,
> Business Verification **va App Review SHART**. Alohida qo'llanma:
> [`REKLAMA-LIDLARI.md`](REKLAMA-LIDLARI.md).

---

## ☐ 0-qadam. Shartlar (2 daqiqa)

| Talab | Qanday tekshiriladi |
|---|---|
| Instagram akkaunt **Professional** (Business yoki Creator) | Instagram ilova → Sozlamalar → Akkaunt turi va vositalari → "Professional akkauntga o'tish" |
| Server **HTTPS** da, tashqaridan ochiq domen bor | Brauzerda `https://<domen>/` ochiladimi |
| CRM'da `marketing` ruxsati bor foydalanuvchi | Sozlamalar → Xodimlar va rollar |

❌ **Xato bo'lsa:** akkaunt shaxsiy (personal) qolsa Meta App uni umuman ko'rmaydi —
"Ulash" oynasida akkaunt ro'yxatda chiqmaydi. Avval Professional qiling.

---

## ☐ 1-qadam. Meta App yaratish (~10 daqiqa)

1. `developers.facebook.com` → **My Apps** → **Create App**;
2. Use case: **Other** → App type: **Business** → ilovaga nom bering;
3. Dashboard → **Add product** → **Instagram**;
4. Chap menyu → **Instagram → API setup with Instagram login**
   ⚠️ *"API setup with **Facebook** login"* EMAS — noto'g'ri yo'l tanlansa scope nomlari
   boshqacha bo'ladi va OAuth ishlamaydi;
5. 3-bo'lim *Set up Instagram business login* dan:
   - **Instagram App ID** — ko'chirib oling (maxfiy emas);
   - **Instagram App Secret** — **Show** bosib ko'chirib oling (**MAXFIY**).

✅ **Qanday tekshiriladi:** App ID — uzun raqam, App Secret — 32 belgilik hex satr.

❌ **Xato bo'lsa:** "Instagram" mahsuloti ro'yxatda yo'q bo'lsa, App type **Business**
emasligidan — ilovani qaytadan, to'g'ri turda yarating.

---

## ☐ 2-qadam. Redirect URI ro'yxatga olish (~2 daqiqa)

Meta Dashboard → *Set up Instagram business login* → **Business login settings**:

| Maydon | Qiymat |
|---|---|
| **OAuth redirect URI** | `https://<domen>/api/public/instagram/callback` |

**Save** bosing.

⚠️ Manzil **AYNAN** shu ko'rinishda bo'lsin — oxirida `/` **YO'Q**. Meta uni harfma-harf
solishtiradi.

✅ **Qanday tekshiriladi:** manzil ro'yxatda saqlangan holda turibdi.

⚠️ **JOYI ham muhim.** Manzil AYNAN *Instagram → API setup with Instagram login* →
**Business login settings** ichiga yoziladi. Meta konsolida yana ikkita o'xshash joy bor va
ular bu oqimga **umuman ta'sir qilmaydi**:
*Facebook Login → Settings → Valid OAuth Redirect URIs* va *App settings → Basic → App Domains*.
Manzil o'sha yerlarga yozilgan bo'lsa, saqlangandek ko'rinadi, lekin `Invalid redirect_uri`
baribir chiqaveradi.

❌ **Xato bo'lsa:** ulash paytida `Invalid redirect_uri` chiqadi → sabablari kamayish
tartibida: (1) noto'g'ri joyga yozilgan (yuqoridagi ⚠️), (2) oxirgi `/`, (3) `http`/`https`
farqi, (4) domen xato. Meta'dagi qatorni CRM Sozlamalar sahifasidagi "OAuth callback URL"
maydonidan **nusxa olib** qo'ying — qo'lda termang.

🔍 **Qanday tekshiriladi:** xato chiqqan sahifaning manzil satridagi `redirect_uri=` qiymatini
ko'ring (`%3A%2F%2F` → `://`) va Meta'dagi qator bilan harfma-harf solishtiring.

🔧 **Manzil qayerdan olinadi (2026-08-22 dan):** CRM `.env` dagi **`APP_HOST`** ni kanonik
manzil deb oladi — ya'ni webhook manzili ham, OAuth callback ham HAR DOIM
`https://<APP_HOST>/…` bo'ladi, admin CRM'ni qaysi nom bilan ochganidan qat'i nazar.
`APP_HOST` bo'sh bo'lsa (dev) eskicha — joriy so'rov hostidan quriladi.

Ilgari manzil faqat so'rov hostidan qurilardi va admin CRM'ni boshqa nom bilan ochsa
(IP orqali, `www.` prefiksi bilan, vaqtinchalik domen) "nusxa olish" tugmasi **boshqa**
manzil berardi — Meta esa `Invalid redirect_uri` bilan rad etardi.

---

## ☐ 3-qadam. `.env` ga maxfiy kalitlar (~3 daqiqa)

Serverdagi `.env` fayliga (loyihaning `.env.example` da namunasi bor):

```
# ---- Instagram AI agenti (Marketing bo'limi) ----
INSTAGRAM_APP_SECRET=<1-qadamdagi App Secret>
INSTAGRAM_VERIFY_TOKEN=<o'zingiz o'ylab topgan satr, masalan: ic_ig_2026_a7f3>
```

So'ng: `docker compose up -d` (konteyner yangi qiymat bilan qayta yaratiladi).

| Kalit | Nima uchun | Qayerda ishlatiladi |
|---|---|---|
| `INSTAGRAM_APP_SECRET` | Webhook imzosi (HMAC) + OAuth token almashuvi | server, hech qachon UI'ga chiqmaydi |
| `INSTAGRAM_VERIFY_TOKEN` | Meta webhook manzilini tasdiqlaganda parol o'rnida | 5-qadamda Meta'ga ham yoziladi |

⚠️ Bu ikkisi **BAZAGA saqlanmaydi va CRM sahifasidan kiritilmaydi** — loyihaning umumiy
qoidasi (baza dump'i Telegram'ga yuboriladi, kalit sizib chiqmasin). Sahifada faqat
"sozlangan / sozlanmagan" holati ko'rinadi.

⚠️ `INSTAGRAM_VERIFY_TOKEN` ni **o'zingiz o'ylab topasiz** — uni Meta bermaydi. Faqat
Meta'dagi qiymat bilan CRM'dagi qiymat **bir xil** bo'lishi muhim.

✅ **Qanday tekshiriladi:** CRM → Marketing → Sozlamalar → holat kartochkalarida
"App Secret: sozlangan" va "Verify token: sozlangan" (yashil).

❌ **Xato bo'lsa:** qizil qolsa — `docker compose up -d` qilinmagan (konteyner eski
o'zgaruvchilar bilan turibdi) yoki qiymat `.env` da tirnoq ichida yozilgan.

---

## ☐ 4-qadam. CRM Sozlamalar sahifasi (~3 daqiqa)

CRM → **Marketing → Sozlamalar**:

| Maydon | Qiymat |
|---|---|
| **Instagram App ID** | 1-qadamdagi App ID |
| Lid manbasi nomi | default `Instagram` (Lidlar bo'limida shu nom ko'rinadi) |
| AI modeli | bo'sh qoldirilsa loyihaning default Gemini modeli |
| Salomlashuv matni (bot oshkorligi) | quyida ☟ |
| Javob kechikishi (soniya) | default `5` — javob bir zumda kelmasin (tabiiylik) |
| Kunlik javob chegarasi | default `200` — himoya to'sig'i |

**Salomlashuv matni Meta talabi:** suhbatning BIRINCHI xabariga botligimiz oshkor qilinishi
SHART. Masalan:

> 🤖 Men *<markaz nomi>* ning AI yordamchisiman. Operator kerak bo'lsa yozing — ulaymiz.

**Saqlash**ni bosing.

⚠️ Hozircha **`InstagramEnabled` va avtojavob bayroqlarini YOQMANG** — ular 9-qadamda,
hammasi sinovdan o'tgach yoqiladi.

✅ **Qanday tekshiriladi:** "Ulash" tugmasi **faollashadi** (avval o'chiq turadi).

❌ **Xato bo'lsa:** tugma o'chiq qolsa — App ID kiritilmagan/saqlanmagan yoki `.env` dagi
ikki kalitdan biri bo'sh.

---

## ☐ 5-qadam. Webhook ulash (~5 daqiqa)

Meta Dashboard → **Instagram → API setup with Instagram login** → **Configure webhooks**:

| Maydon | Qiymat |
|---|---|
| **Callback URL** | `https://<domen>/api/public/instagram/webhook` |
| **Verify token** | 3-qadamdagi `INSTAGRAM_VERIFY_TOKEN` bilan **AYNAN** bir xil |

**Verify and save** → yashil bo'lishi kerak.

So'ng **Subscribe** maydonlarini belgilang:

| Maydon | Nega kerak |
|---|---|
| `comments` | izohlar |
| `messages` | DM |
🔴 **`message_echoes` ni QIDIRMANG — u endi YO'Q.** Meta uni qabul qilinadigan maydonlar
ro'yxatidan olib tashlagan (2026-08-22 da prodda tekshirildi: yuborilsa
`IGApiException 100 "Param subscribed_fields[N] must be one of {…}"` qaytadi va **butun
so'rov rad etiladi**, ya'ni `comments` ham obuna bo'lmay qoladi). Operator qo'lda yozgan
xabar endi `messages` obunasi ostida `is_echo: true` bilan keladi va bot o'sha suhbatda
baribir jim turadi.

⚠️ `messages` belgilanmasa: operator telefonidan javob yozganda bot buni bilmaydi va
mijoz bir vaqtda **ikki odam bilan** gaplashadi.

✅ **Qanday tekshiriladi:** "Verify and save" yashil; CRM → Marketing → Sozlamalar →
diagnostika kartochkasida `webhookSubscribed` ✓ (ulashdan keyin).

❌ **Xato bo'lsa — "Verify and save" QIZIL:**

| Sabab | Yechim |
|---|---|
| Verify token mos emas | `.env` dagi va Meta'dagi qiymatni yonma-yon solishtiring (bo'sh joy/ko'chirish xatosi) |
| Manzil tashqaridan ochilmaydi | `https://<domen>/api/public/instagram/webhook` ni brauzerda oching — 403 kelsa **yaxshi** (endpoint bor, faqat parametrsiz); 404 yoki "site can't be reached" kelsa tunnel/proxy sozlamasi |
| HTTP ishlatilgan | Meta faqat **HTTPS** qabul qiladi |

---

## ☐ 6-qadam. Akkauntni ULASH — bitta tugma (~1 daqiqa)

1. CRM → **Marketing → Sozlamalar** → **«Instagram'ni ulash»**;
2. Instagram'ning **o'z login oynasi** ochiladi — parol bizga ko'rinmaydi;
3. Ruxsat so'raladi: **xabarlar** va **izohlar**;
4. Tizim avtomatik bajaradi: kod → qisqa token → **60 kunlik token** → akkaunt ID/username
   aniqlash → webhook obunasi → hammasini bazaga saqlash;
5. *"Instagram ulandi ✅"* chiqsa — tayyor.

✅ **Qanday tekshiriladi:** Sozlamalar sahifasida username, profil rasmi va
**"Token: N kun qoldi"** (≈60) ko'rinadi.

❌ **Xato bo'lsa:**

| Alomat | Sabab |
|---|---|
| `Invalid redirect_uri` | 2-qadamdagi manzil aynan mos emas |
| Ro'yxatda akkaunt yo'q | Akkaunt hali Professional emas (0-qadam) |
| "Kod muddati o'tgan" | Authorize kodi **1 soat** va **bir marta** ishlaydi — tugmani qayta bosing |
| Ulanish tugadi, lekin `webhookSubscribed` ✗ | 5-qadamdagi Subscribe maydonlari belgilanmagan |

### 6b. ZAXIRA yo'l — «Token bilan qo'lda ulash»

OAuth hech qanday tuzatishdan keyin ham yurmasa (ko'pincha `Invalid redirect_uri` yoki domen
hali tashqaridan ochilmagan bo'lsa), akkauntni **tokenning o'zi bilan** ulash mumkin:

1. Meta konsoli → **Instagram → API setup with Instagram login** → 2-bo'lim
   **Generate token** → akkauntni tanlang → tokenni ko'chiring;
2. CRM → **Marketing → Sozlamalar** → «Instagram akkaunt» kartasi →
   **«Token bilan qo'lda ulash»** → tokenni qo'ying → **«Tekshirib ulash»**.

Server tokenni **ishonch bilan saqlamaydi** — OAuth bilan AYNAN bir xil ketma-ketlikni bajaradi:
`me` bilan qaysi akkauntniki ekanini aniqlaydi (uchala identifikator: `user_id`, app-scoped `id`,
`username` — halqa himoyasi shularga tayanadi), tokenni 60 kunlikka aylantiradi va webhook
obunasini qiladi. Token yaroqsiz bo'lsa **hech narsa saqlanmaydi**.

⚠️ **Bu ATAYIN ikkinchi darajali yo'l.** OAuth'da token o'zi olinadi va admin hech narsa
ko'chirmaydi; qo'lda ulashda esa jonli kalit ekrandan o'tadi. Iloji bo'lsa 6-qadamdagi tugmadan
foydalaning. Ikkala yo'lda ham token keyinchalik 45-kunda avtomatik yangilanadi.

⚠️ Muddat "noma'lum" chiqsa — token uzaytirilmadi (masalan endigina yaratilgan yoki qisqa
muddatli). Akkaunt baribir ulanadi va fon xizmati keyingi yurishida muddatni o'zi to'g'rilaydi.

---

## ☐ 7-qadam. Bilim bazasini to'ldirish (~10 daqiqa) — ENG MUHIM QADAM

CRM → **Marketing → Bilim bazasi**. Agent **FAQAT** shu yerdagi ma'lumot asosida gapiradi.

Tavsiya etilgan bo'laklar:

| Sarlavha | Ichida nima bo'lishi kerak |
|---|---|
| Markaz haqida | nomi, manzili, ish vaqti, telefon |
| **Kurslar va narxlar** | har yo'nalish, davomiyligi, oylik to'lov |
| To'lov va chegirmalar | to'lov usullari, aka-uka chegirmasi, shartnoma |
| FAQ | eng ko'p so'raladigan savol-javoblar |
| Muloqot qoidalari | joriy aksiyalar, nima va'da qilinmaydi |

⚠️ **Bilim bazasi bo'sh bo'lsa agent narx AYTA OLMAYDI** — har narx savolini operatorga
o'tkazadi (`escalate_to_human`). Bu **ataylab**: bot narxni **o'ylab topmaydi**.

✅ **Qanday tekshiriladi:** 8-qadamdagi sinovda narx savoliga aniq raqam bilan javob keladi.

---

## ☐ 8-qadam. Sinov (~5 daqiqa)

### 8.1. Jonli yubormasdan (xavfsiz)

Sozlamalar sahifasidagi **«Agentni sinash»** maydoni (`POST /test-agent`): kanal va matn
kiritiladi → AI javobi **ekranda ko'rsatiladi**, Instagram'ga **yuborilmaydi**.

Shu bosqichda tekshiring:
- narx savoliga bilim bazasidagi aniq raqam keladimi;
- javob mijoz yozgan **tilda va yozuvda** (kirill/lotin/rus/ingliz) keladimi;
- javob o'rtasida kesilib qolmayaptimi.

### 8.2. Jonli sinov

Avtojavobni 9-qadamda yoqqach:

- **Izoh:** o'z postingizga **boshqa akkauntdan** "Qancha turadi?" deb yozing →
  ochiq javob keladi va DM'ga taklif qilinadi;
- **DM:** "Ingliz tili kursi narxi?" → javob keladi, telefon so'raladi;
- CRM → **Lidlar** bo'limida yangi lid paydo bo'ladi;
- Qaynoq lid bo'lsa — Telegram'da operatorga bildirishnoma keladi.

⚠️ **O'Z akkauntingizdan izoh yozib sinamang** — modul o'z izohini ataylab tashlaydi
(cheksiz halqadan himoya). Boshqa akkaunt kerak.

---

## ☐ 9-qadam. Avtojavobni yoqish

Hammasi ishlaganiga ishonch hosil qilgach, CRM → Marketing → Sozlamalar:

| Bayroq | Ma'nosi |
|---|---|
| **Instagram moduli yoqilgan** | asosiy kalit — o'chiq bo'lsa navbat ham ishlamaydi, tashqariga **hech qanday so'rov ketmaydi** |
| Izohlarga avtojavob | ochiq izohga javob yoziladi |
| DM'ga avtojavob | shaxsiy xabarga javob yoziladi |
| Izohga shaxsiy javob (private reply) | izoh yozgan odamga qo'shimcha DM |
| Telegram bildirishnomasi | qaynoq lid/eskalatsiyada operatorga xabar |

Tavsiya: avval **faqat izohlarni** yoqib bir kun kuzating, so'ng DM'ni.

---

## 📎 Qo'shimcha: huquqiy sahifalar (Meta talabi)

Meta App sozlamalarida ikki ochiq (login talab qilmaydigan) manzil so'raladi:

| Maydon | Manzil |
|---|---|
| Privacy Policy URL | `https://<domen>/privacy` |
| Data Deletion URL | `https://<domen>/data-deletion` |

Ular SPA'da ochiq marshrut sifatida turadi va **hech qanday CRM ma'lumotini
ko'rsatmaydi** — faqat: qaysi ma'lumot yig'iladi (username, ID, xabar matni), nima
yig'ilmaydi, kim bilan bo'lishiladi va qanday o'chiriladi.

---

## 🔧 Diagnostika belgilari

| Alomat | Ehtimoliy sabab | Nima qilinadi |
|---|---|---|
| Webhook "Verify and save" qizil | Verify token mos emas · manzil tashqaridan ochiq emas · HTTP | 5-qadam |
| «Ulash» tugmasi o'chiq | App ID / App Secret / Verify token to'ldirilmagan yoki saqlanmagan | 3–4-qadam |
| `Invalid redirect_uri` | Meta'dagi manzil aynan mos emas (oxirgi `/`) | 2-qadam |
| `Invalid platform app` | **App ID noto'g'ri turdagi.** Ilova sahifasining tepasidagi / *App settings → Basic* dagi **Meta App ID** kiritilgan, holbuki bu oqim **Instagram App ID** ni talab qiladi — u boshqa raqam va faqat *Instagram → API setup with Instagram login* → 3-bo'limda turadi. Yoki "API setup with **Facebook** login" yo'li tanlangan | 1-qadam |
| Bot umuman javob bermayapti | Modul o'chiq · avtojavob bayrog'i o'chiq · Gemini kaliti yo'q · webhook obunasi yo'q · token muddati o'tgan | Sozlamalar → diagnostika kartochkasi |
| Navbatda `pending` to'planib qolgan | `InstagramEnabled=false` (worker ishlamayapti) yoki xato | `GET /events` — xatolar matni bilan ko'rinadi |
| Bot **o'ziga** javob berayapti 🚨 | Saqlangan akkaunt ID lari bo'sh | **Darhol modulni o'chiring** va qaytadan «Ulash» qiling |
| Javob keladi, lekin lid yo'q | AI `is_hot_lead=false` bergan va kontakt yo'q | Normal — har suhbat lid bo'lmaydi |
| Narx o'rniga "operator bog'lanadi" | Bilim bazasida narx yo'q | 7-qadam |
| Bot bitta suhbatda jim | Operator qo'lda javob bergan → **pauza** | Ataylab. Inbox'da "Botni qaytarish" |
| DM yuborilmadi, "oyna yopiq" | Mijoz oxirgi marta **24 soatdan** oldin yozgan | Instagram cheklovi — faqat operator boshqa yo'l bilan bog'lanadi |
| Javoblar kesilib qolyapti | AI model chegarasi past | AI modeli sozlamasi |
| Token muddati tugab qolgan | 45-kunda avtomatik yangilanadi; xato bo'lsa Telegram alert keladi | «Tokenni yangilash» yoki qaytadan «Ulash» |

---

## ⏱ Muddatlar eslatmasi

| Narsa | Muddat |
|---|---|
| OAuth kodi | **1 soat**, **bir marta** |
| Uzoq muddatli token | **~60 kun** (45-kunda avtomatik yangilanadi) |
| DM yuborish oynasi | mijoz oxirgi yozganidan **24 soat** |
| Izohga private reply | izohdan **7 kun** ichida, **bir marta** |
| Meta webhook javobini kutishi | **5 soniya** |

Protokol tafsilotlari: [`TEXNIK.md`](TEXNIK.md).

---
---

# QO'SHIMCHA MODULLAR (2026-08)

> Yuqoridagi 9 qadam **izoh va DM agenti**ni ishga tushiradi. Marketing bo'limida undan tashqari
> yana uchta mustaqil modul bor va ularning **har biri o'z bayrog'i, o'z tokeni va o'z
> qo'llanmasi** bilan keladi:

| Modul | Nima beradi | To'liq qo'llanma |
|---|---|---|
| **Reklama lidlari** | Reklamadagi forma to'ldirilsa lid CRM'ga tushadi | [`REKLAMA-LIDLARI.md`](REKLAMA-LIDLARI.md) |
| **Reklama statistikasi** | Xarajat · lid narxi · **ROI** (kim pul to'lagani bilan) | [`REKLAMA-STATISTIKASI.md`](REKLAMA-STATISTIKASI.md) |
| **Kontent joylash** | Post/Reels/Story'ni CRM'dan rejalashtirib joylash | [`KONTENT.md`](KONTENT.md) |
| **CAPI** | "Bu lid mijoz bo'ldi" ni Meta'ga qaytarish (reklama optimallashadi) | [`CAPI.md`](CAPI.md) |

⚠️ **Barcha yangi bayroqlar default O'CHIQ** — yoqilmaguncha tashqariga bitta ham so'rov
ketmaydi. Modullar bir-biridan mustaqil: birini yoqmasdan ikkinchisini ishlatish mumkin.

---

## ☐ A-qadam. System User tokeni (muddatsiz) — reklama modullari uchun

Reklama lidlari, reklama statistikasi va CAPI **OAuth'dan foydalanmaydi**: token
Business Manager'dan **qo'lda** olinadi va CRM sozlamalariga kiritiladi. Sabab —
**System User tokeni MUDDATSIZ**, ya'ni bir marta olinadi va 60 kunda o'lmaydi.

### A.1. Sistema foydalanuvchisini yaratish (bir marta)

1. `business.facebook.com` → **Business settings → Users → System users** → **Add**;
2. Nom bering (masalan `IntellectCRM`), rolni **Admin** qiling.

⚠️ Bitta sistema foydalanuvchisi **uchala modul uchun** ham yetadi — har biriga alohida
yaratish shart emas.

### A.2. Kerakli obyektlarni biriktirish (**Add assets**)

🔴 **Eng ko'p unutiladigan qadam.** Token qaysi obyektga huquq berilmagan bo'lsa, o'sha
obyektni **umuman ko'rmaydi** va xato "Ruxsat yetishmaydi" (`#200`/`#10`) bo'lib chiqadi.

| Modul | Qaysi asset biriktiriladi |
|---|---|
| Reklama lidlari | **Page** (Instagram akkaunt bog'langan sahifa) |
| Reklama statistikasi | **Ad account** (reklama kabineti) |
| CAPI | **Dataset** (Events Manager) |

Har biriga **to'liq huquq** bering.

### A.3. Token generatsiya qilish

**Generate new token** → ilovangizni tanlang → kerakli ruxsatlarni belgilang:

| Modul | Ruxsatlar |
|---|---|
| Reklama lidlari | `leads_retrieval` · `pages_show_list` · `pages_manage_ads` · `pages_read_engagement` |
| **Reklama statistikasi** | **`ads_read`** (+ `business_management`) |
| **CAPI** | **`ads_management`** (Dataset ustidan) |

⚠️ **Token faqat BIR MARTA ko'rsatiladi** — darhol nusxa oling.

⚠️ **`ads_management` ni statistika uchun so'ramang:** CRM reklamani boshqarmaydi, faqat
o'qiydi. Ortiqcha ruxsat App Review talabini oshiradi.

🔴 **HAR MODUL O'Z TOKENINI TALAB QILADI.** Ular bir-birining o'rnini **BOSMAYDI**, chunki
har xil obyektga tegishli:

| Token | Qaysi obyekt | Almashtirilsa nima bo'ladi |
|---|---|---|
| Instagram Login tokeni | Instagram akkaunt | — (u OAuth bilan o'zi olinadi) |
| Page Access Token | **Page** | Statistika so'ralsa `OAuthException 190`/`200` |
| System User (`ads_read`) | **Ad account** | Lid olinmaydi (`leads_retrieval` yo'q) |
| Dataset tokeni (`ads_management`) | **Dataset** | CAPI `190`/`803` |

⚠️ Bitta System User tokeniga **hamma ruxsatni birga** berish texnik jihatdan mumkin, lekin
**tavsiya etilmaydi**: bitta token bekor qilinsa uchala modul birdan to'xtaydi va sababini
topish qiyinlashadi.

### A.4. Qayerga kiritiladi

CRM → **Marketing → Sozlamalar**, har modulning **o'z kartochkasi**ga.

⚠️ **Token hech qachon ekranda ko'rsatilmaydi** — faqat «Sozlangan / Sozlanmagan». Forma har
safar bo'sh ochiladi va **bo'sh yuborilgan maydon mavjud qiymatni O'CHIRMAYDI** (ya'ni faqat
id'ni tuzatish uchun tokenni qayta yozish shart emas).

---

## ☐ B-qadam. `content_publish` ruxsati va AKKAUNTNI QAYTA ULASH

Kontent joylash moduli uchun Instagram Login tokeniga **yangi scope** kerak:

```
instagram_business_content_publish
```

🔴 **Yangi scope mavjud tokenga AVTOMATIK qo'llanmaydi.** OAuth ruxsatlari token olingan paytda
muzlatiladi — ilgari ulangan akkauntda bu ruxsat **yo'q** va har post
«Ruxsat yetishmaydi» bilan yiqiladi.

**Nima qilish kerak:**

1. CRM → **Marketing → Sozlamalar** → Instagram kartochkasi → **«Qayta ulash»**;
2. Instagram login oynasida ruxsatlar ro'yxatida **kontent joylash** ham ko'rinadi — tasdiqlang;
3. Ulanish tugagach yangi token saqlanadi va modul ishlay boshlaydi.

⚠️ **Qayta ulash boshqa hech narsani buzmaydi:** suhbatlar tarixi, lidlar, qoidalar va bilim
bazasi joyida qoladi. Yangilanadigan narsa — token, akkaunt id'lari va webhook obunasi.

⚠️ **CRM sizda bu ruxsat bor-yo'qligini ANIQ ayta olmaydi.** Berilgan scope'lar ro'yxati
saqlanmaydi, shuning uchun kontent bo'limi diagnostikasida bu maydon **«noma'lum»** deb turadi
— yolg'on "ha" dan ko'ra ochiq "bilmayman" yaxshiroq. Ruxsat yo'qligi birinchi postda ma'lum
bo'ladi.

✅ **Qanday tekshiriladi:** Marketing → Kontent sahifasida bitta rasm posti yarating va
«Hoziroq joylash» bosing. Xato chiqmasa — ruxsat berilgan.

❌ **Xato bo'lsa:**

| Alomat | Sabab |
|---|---|
| «Ruxsat yetishmaydi … instagram_business_content_publish» | Akkaunt hali eski token bilan ulangan — B-qadamni takrorlang |
| «Instagram tokeni muddati tugagan» | Token o'lgan — «Qayta ulash» |
| `2207052` («Media yuklab bo'lmadi») | Bu **ruxsat masalasi EMAS** — server tashqaridan HTTPS bilan ochiq emas ([`KONTENT.md`](KONTENT.md) 3-qadam) |

---

## 🔧 Qo'shimcha modullar — tez diagnostika

| Alomat | Qaysi qo'llanma |
|---|---|
| Reklama lidi kelmayapti | [`REKLAMA-LIDLARI.md`](REKLAMA-LIDLARI.md) → «Nosozliklar» |
| Statistika bo'sh / `#200` / `190` | [`REKLAMA-STATISTIKASI.md`](REKLAMA-STATISTIKASI.md) → «Nosozliklar» |
| Post joylanmayapti / `2207052` | [`KONTENT.md`](KONTENT.md) → «Nosozliklar» |
| CAPI hodisalari Events Manager'da ko'rinmayapti | [`CAPI.md`](CAPI.md) → «Nosozliklar» |
