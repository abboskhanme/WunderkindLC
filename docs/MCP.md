# AI ulanish (MCP) — rahbariyat uchun qo'llanma

Wunderkind o'quv markazi tizimini **Claude**, **ChatGPT** yoki boshqa AI yordamchiga ulab, markaz
haqida oddiy tilda savol berish mumkin: *"Bugun qaysi guruhlarda davomat olinmagan?"*, *"Ingliz tili
kursida qarzdorlar kim?"*, *"Sentabrda nechta lid keldi va nechtasi o'quvchiga aylandi?"*.

Ulanish **FAQAT O'QISH uchun** (markaz egasining qat'iy talabi):

- AI hech narsani **o'zgartira olmaydi**, o'chira olmaydi, qo'sha olmaydi va hech kimga xabar
  **yubora olmaydi**. Buni uch qavat himoya ta'minlaydi: vositalarning o'zi faqat o'qiydi; ular
  bazaga alohida **faqat-o'qish** roli (`wlc_ro`) bilan ulanadi; bu rolga yozish huquqi umuman
  berilmagan va ilova har ishga tushganda buni o'zi tekshiradi.
- AI faqat **sizga ruxsat berilgan** bo'limlarni ko'radi — xuddi veb-paneldagidek. Masalan, moliya
  ruxsati yo'q xodim AI orqali ham balans va to'lovlarni ko'rmaydi.
- Parol **AI ga berilmaydi**: ulashda brauzerda **bizning** kirish sahifamiz ochiladi, login va
  parol faqat o'sha yerga, markaz serveriga kiritiladi.
- Login, parol, tokenlar, Telegram chat raqamlari, hujjat skanlari hech qachon AI ga chiqmaydi.
  **O'quvchilarning login/parollari** (paneldagi «Kirish ma'lumotlari» seyfi) AI uchun butunlay yopiq — bazadagi
  jadvalning birorta ustuni ham faqat-o'qish roliga berilmaydi, kimdir qo'lda ochsa ham AI ulanishi o'zi o'chadi.
- Har bir AI so'rovi jurnalga yoziladi (Sozlamalar → Umumiy → **AI ulanishlar**).

## 1. Kimga ruxsat bor

| Kim | AI ulana oladimi |
|---|---|
| Superadmin | Ha, har doim |
| Administrator (admin) | Faqat superadmin shu akkauntga **alohida yoqqan** bo'lsa |
| Xodim (staff) | Faqat rolida **«AI ulanish (MCP, faqat o'qish)»** ruxsati berilgan bo'lsa |
| O'qituvchi, o'quvchi, ota-ona | Yo'q |

**Administratorga ruxsat berish** (faqat superadmin): Sozlamalar → Umumiy → **AI ulanishlar** →
**«Administratorlar uchun AI ruxsati»** → kerakli administrator yonidagi kalitni yoqing. Kalit
o'chirilsa, o'sha administratorning barcha AI ulanishlari **darhol** bekor qilinadi. Yoqilganda
eski (ruxsat o'chiq paytdagi) ulanishlar qayta tirilmaydi — administrator AI ilovada qaytadan
«Connect» qiladi. Yangi
administrator qo'shilganda (yoki xodim administratorga ko'tarilganda) AI ulanish avtomatik
ochilmaydi — superadmin ongli ravishda yoqadi. Har bir yoqish/o'chirish «O'zgarishlar tarixi» ga
yoziladi.

Xodimga ruxsat berish: **Boshqaruv → Rollar** → rolni tahrirlash → **«AI ulanish (MCP, faqat
o'qish)»** → Saqlash. Ruxsat olib qo'yilsa, o'sha roldagi xodimlarning AI ulanishlari **keyingi
so'rovdayoq** ishlamay qoladi (qayta kirish shart emas).

## 2. Ulanish manzili

```
https://lc.wunderkindedu.uz/mcp
```

Aniq manzil — Sozlamalar → Umumiy → **AI ulanishlar** sahifasining yuqorisida («Nusxa» tugmasi bilan).
Ulana oladigan administrator va xodim uni o'zining **Akkaunt sozlamalari** → **«Mening AI
ulanishlarim»** blokida ko'radi.

## 3. Qanday ulanadi

### Claude (claude.ai yoki Claude Desktop)
1. **Settings → Connectors → Add custom connector**.
2. Nomi: `Wunderkind LC`, URL: `https://lc.wunderkindedu.uz/mcp`.
3. **Connect** — ochilgan Wunderkind sahifasida login va parolni kiriting, **«Ruxsat berish»** ni bosing.

### ChatGPT
1. **Settings → Connectors** (tarifga qarab *Developer mode* yoqilishi kerak bo'lishi mumkin) → **Create**.
2. Nomi: `Wunderkind LC`, URL: `https://lc.wunderkindedu.uz/mcp`, autentifikatsiya: **OAuth**.
3. Saqlang — Wunderkind kirish sahifasi ochiladi, kiring va **«Ruxsat berish»** ni bosing.
4. Suhbatda ulagichni tanlab savol bering.

### Boshqa MCP mijozlari
Kalit faqat Claude va ChatGPT'ning **aniq callback manzillariga** (`https://claude.ai/api/mcp/auth_callback`,
`https://claude.com/api/mcp/auth_callback`, `https://chatgpt.com/connector_platform_oauth_redirect`) va
kompyuterning o'zidagi dasturlarga (`http://127.0.0.1`, `http://localhost`) yuboriladi. `cursor://`, `vscode://`
kabi manzil bilan qaytadigan ilovalar ulana olmaydi. Yangi AI xizmati kerak bo'lsa — dasturchi uning callback
manzilini `MCP_ALLOWED_REDIRECT_URIS` ga qo'shadi.

Ruxsat sahifasida **kalit qayerga yuborilishi** to'liq manzili bilan yoziladi. Ilova nomi — ilovaning o'zi
aytgan, TEKSHIRILMAGAN nom. **Hozirgina o'zingiz AI ilovasida «Connect» ni bosmagan bo'lsangiz — «Rad etish»**
ni bosing va superadminga xabar bering. Har bir yangi ulanish «O'zgarishlar tarixi» ga yoziladi.

> Ulanish 1 soatlik kalit bilan ishlaydi, AI ilovasi uni o'zi yangilab turadi. 30 kun ishlatilmasa
> yoki ulanganiga **90 kun** bo'lsa, qayta kirish so'raladi. **Parolingiz o'zgartirilsa yoki
> tiklansa** — barcha AI ulanishlaringiz darhol uziladi.

### Xavfsizlik bo'yicha maslahat
AI o'qigan ma'lumot ichida (masalan, lid izohida yoki taklif matnida) AI ni aldashga urinadigan
"ko'rsatma" bo'lishi mumkin. Shuning uchun markaz ulagichini **xat yuboradigan, internetga so'rov
jo'natadigan yoki fayl yozadigan** boshqa ulagichlar (Gmail, Slack, veb-qidiruv va h.k.) bilan
**bitta suhbatda birga yoqmang**, va maxfiy xulosalarni (qarzdorlar, telefonlar) boshqa joyga
ko'chirishdan oldin o'zingiz tekshiring.

## 4. Nimani so'rash mumkin

| Mavzu | Namuna savollar | Kerakli ruxsat |
|---|---|---|
| Umumiy holat | "Markazda nechta faol, sinovdagi va muzlatilgan o'quvchi bor?" | (ulanish ruxsati yetarli; qarz — Moliya) |
| O'quvchilar | "Valiyev Ali haqida ma'lumot", "Telegramga ulanmagan faol o'quvchilar", "Alining otasi va onasi botga ulanganmi?", "Oktabrda IELTS olganlar" | O'quvchilar ro'yxati va profili |
| Guruhlar | "English A1 guruhi a'zolari", "Oktabr jurnali xulosasi", "Xonalar bandligi", "2-qavatdagi xonalar" | Guruhlar va jurnal / Xonalar / Kurslar |
| Davomat | "Bugun kim kelmadi?", "Sababsiz kelmaganlar", "Davomati olinmagan darslar", "Alining oxirgi oydagi davomati", "Ketma-ket 3 dars kelmaganlar (qizil ro'yxat)" | O'quvchilar davomati / Davomat qilinmagan guruhlar |
| Davomat intizomi | "Bu hafta qaysi o'qituvchi davomatni kech oldi?", "Kecha davomat olinmagan darslar" | Davomat qilinmagan guruhlar (jarima summasi — Moliya) |
| Davomat xabarlari | "Bugun nechta «kelmadi» xabari tasdiq kutyapti?", "Kim xabarni to'xtatdi?", "Kechagi «kelmadi» xabarlari ota-onalarga yetib bordimi?" | Davomat xabarlari nazorati |
| Moliya | "Eng katta 10 ta qarzdor", "Sentabr kirim va chiqim", "Kassalar qoldig'i", "O'qituvchilar maoshi", "Oktabr davomat jarimalari" | Moliya (kirim/chiqim, hisobotlar, maosh, jarimalar) |
| Telegram xabarlar | "Kechagi e'lonni nechta ota-ona qabul qildi?", "Kimlar botni bloklagan?", "Bu hafta nechta «kelmadi» xabari ketdi?" | Telegram xabarlar (o'quvchining o'z xabarlari — O'quvchilar ham) |
| Lidlar | "Oxirgi 30 kun voronkasi", "Yo'qotilgan lidlar sabablari" | Lidlar / CRM statistika |
| Testlar va reyting | "Oktabrdagi oylik testlar va o'rtacha foizlari", "Unit 5 testida kim nechta savol yechdi?", "Alining oraliq testlari", "Oktabr reytingi TOP-20 (testlar + ball)", "Alining 3 oylik reytingi", "Sentabr TOP/BOTTOM ro'yxati (ota-onalarga ketadigan)" | Testlar natijalari / Reyting (guruh reytingi — Guruhlar ham, o'quvchi reytingi — O'quvchilar ham; TOP/BOTTOM — Reyting) |
| O'qituvchilar va jadval | "O'qituvchilar ro'yxati", "Shu haftaning jadvali", "Jadvalda to'qnashuvlar bormi?", "Bo'sh xonalar" | O'qituvchilar / Dars jadvali |
| Boshqa | "Bog'lanish navbati", "O'zgarishlar tarixi" | Bog'lanish kerak / O'zgarishlar tarixi |

Ruxsat yo'q bo'limni so'rasangiz, AI "Ruxsat yo'q" javobini oladi — bu jurnalda ham ko'rinadi.

Cheklovlar: bitta ro'yxat ko'pi bilan **200 qator** (qarzdorlar va maosh — 50); sana oralig'i ko'pi bilan
**1 yil** (Telegram xabarlar jurnali — 92 kun, davomat intizomi — 31 kun, haftalik jadval — 7 kun); bitta so'rov ko'pi
bilan **60 soniya**; bitta ulanishdan daqiqasiga 120 tadan ortiq so'rov qabul qilinmaydi.

Raqamlar panel bilan **bir xil**: davomat — yagona ta'rif (kech kelgan KELGAN, «sababsiz» — sababning «Sababli (uzrli)»
belgisi bo'yicha, davomat «olingan» — birinchi «keldi»/«kelmadi» belgisi; faqat baho davomat emas — dars jadvalidagi
«davomat olingan» belgisi ham shu); reyting — testlar + ball (Hisobotlar → Reyting bilan bir xil hisob); test foizi —
yechilgan savollar ÷ savollar soni (panel bilan bir xil, 0–100); TOP/BOTTOM — oylik avto-xabar yuboradigan AYNAN o'sha
ro'yxat (AI faqat ko'radi, yubormaydi). Davomat xabarlari navbatini AI faqat KO'RADI — to'xtatish, tasdiqlash va bekor
qilish faqat panelda. Qizil ro'yxat va reyting **5 daqiqagacha** eskirgan bo'lishi mumkin (server keshi).

## 5. Ulanishlarni ko'rish va bekor qilish

- **Har kim o'zinikini**: profil menyusi → **Akkaunt sozlamalari** (yoki Sozlamalar → Umumiy →
  **Foydalanuvchi sozlamalari**) → **«Mening AI ulanishlarim»**. U yerda har bir ulangan ilova,
  ulangan va oxirgi foydalanilgan vaqt, qachongacha amal qilishi va so'rovlar soni ko'rinadi.
  **«Bekor qilish»** (bitta) yoki **«Hammasini bekor qilish»** — AI ilova **darhol** to'xtaydi,
  qayta ulanish uchun yana login va parol so'raladi. Tanimagan ulanishni ko'rsangiz — darhol bekor
  qiling va parolingizni almashtiring. Blok faqat AI ulana oladigan (yoki ulanishi bor) foydalanuvchiga
  ko'rinadi; boshqalarning ulanishlarini bu yerda ko'rib ham, bekor qilib ham bo'lmaydi.
- **Superadmin — hammaning ulanishlari**: Sozlamalar → Umumiy → **AI ulanishlar** → kerakli
  qatorda **«Bekor qilish»**.
- **Administratorning ruxsatini olish**: o'sha sahifada «Administratorlar uchun AI ruxsati» kalitini o'chirish.
- **Xodimning ruxsatini olish**: Boshqaruv → Rollar → «AI ulanish» ni o'chirish.
- **Xodimni o'chirish yoki parolini tiklash** — uning ulanishlari ham darhol ishlamay qoladi.
- «AI ulanishlar» sahifasida har bir so'rov ko'rinadi: kim, qaysi ilova, qaysi vosita, nechta qator,
  natija. Jurnal **1 yil** saqlanadi.

## 5.1 Superadminga Telegram xabarlari

Superadmin Telegram botga ulangan bo'lsa (Akkaunt sozlamalaridagi telefon raqamini botga
yuborgan bo'lsa), unga quyidagi xabarlar keladi:

- **Yangi AI ulanish** — kim, qaysi ilova (nomi tekshirilmagan), kalit qayerga yuborilgani va
  qachon. Tanimasangiz — «AI ulanishlar» sahifasida bekor qiling.
- **24 soatda odatdan ko'p so'rov** — bitta ulanish 24 soatda **1 000** tadan ortiq so'rov qilsa
  yoki **50 000** dan ortiq qator o'qisa. Har bir ulanish uchun kuniga ko'pi bilan bitta xabar.
  Ulanish to'xtatilmaydi — so'rovlar jurnalini ko'rib, kerak bo'lsa o'zingiz bekor qilasiz.
  «Ulangan AI ilovalar» jadvalida bunday ulanish yonida qizil **«Ko'p so'rov»** belgisi turadi.

Xabarlarda kalit (token), ilova ID raqami yoki boshqa maxfiy ma'lumot bo'lmaydi. Bot sozlanmagan
bo'lsa xabar server jurnaliga (log) yoziladi.

## 5.2 AI o'qiydigan matnlar

Lid izohi, ota-ona taklifi, ishga ariza kabi tashqi matnlar AI ga berilishidan oldin
tozalanadi: ko'rinmas belgilar (masalan matn yo'nalishini o'zgartiruvchi va "nol kenglikdagi"
belgilar) olib tashlanadi va har bir matn **2 000 belgigacha** qisqartiriladi (oxirida
«… [matn qisqartirildi]»). Oddiy matn — o'zbekcha apostroflar (ʻ ’ ') va emoji bilan — o'zgarmaydi.

---

## 6. Texnik: yoqish va deploy (dasturchi uchun)

Arxitektura va qoidalar: `docs/SPEC-mcp.md`.

### 6.1 Sozlamalar (`.env`)

| O'zgaruvchi | Qiymat |
|---|---|
| `MCP_DB_RO_PASSWORD` | **Majburiy.** `wlc_ro` rolining paroli: `openssl rand -hex 24` (16–128 belgi, `[A-Za-z0-9_-]`). Bo'sh = MCP o'chiq |
| `MCP_PUBLIC_BASE_URL` | Ixtiyoriy. Bo'sh bo'lsa `https://<APP_HOST>` |
| `MCP_ALLOWED_REDIRECT_URIS` | Ixtiyoriy, vergul bilan, AYNAN manzillar. Sukut: Claude va ChatGPT callback'lari (yuqorida) |

Qo'lda SQL **kerak emas**: ilova har ishga tushganda (migratsiyalardan keyin) egasi ulanishi orqali
`wlc_ro` rolini yaratadi/yangilaydi — avval barcha huquqlarni oladi, keyin faqat vositalar o'qiydigan
jadvallarga `SELECT` beradi (`Users` — faqat ism/rol/telefon/lavozim ustunlari). So'ng faqat-o'qish
ulanishi o'zini tekshiradi; o'tmasa MCP **o'chiq** qoladi (`/mcp` → 503, `/oauth/*` → 404, sababi
logda `MCP disabled: …` va «AI ulanishlar» sahifasida). Ilovaning qolgan qismi o'zgarishsiz ishlaydi.

### 6.2 Deploy

```bash
# 0) BACKUP (deploy skill / DEPLOY.md)
# 1) .env ga parol (bir marta)
echo "MCP_DB_RO_PASSWORD=$(openssl rand -hex 24)" >> .env
# 2) Odatdagi deploy: migratsiyalar AddMcpReadOnly (5 ta Mcp* jadval) va McpAccessAndVolumeAlerts
#    (Users.AiAccessEnabled, McpGrants.VolumeAlertAt — faqat ustun qo'shadi, DROP yo'q) o'zi qo'llanadi
docker compose up -d --build app
# 2.1) Deploydan keyin: administratorlar AI ulanishi O'CHIQ bo'ladi — superadmin kerakli
#      administratorlarga Sozlamalar → Umumiy → AI ulanishlar → «Administratorlar uchun AI ruxsati» da yoqadi
# 3) Logda: "MCP (AI ulanish) yoqildi — wlc_ro o'z-o'zini tekshirishdan o'tdi"
docker logs wunderkindlc-app 2>&1 | grep -i mcp
```

### 6.3 Tekshirish

```bash
curl -s https://lc.wunderkindedu.uz/.well-known/oauth-authorization-server   # JSON, issuer = https://lc…
curl -si -X POST https://lc.wunderkindedu.uz/mcp | grep -i www-authenticate  # 401 + resource_metadata
docker exec wunderkindlc-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  "select rolname, rolsuper, rolconfig from pg_roles where rolname='wlc_ro'"
#   rolconfig: {default_transaction_read_only=on,statement_timeout=30s}
```

### 6.4 Cloudflare

- `/mcp`, `/oauth/token`, `/oauth/register`, `/oauth/revoke`, `/.well-known/*` ni AI ilovasining
  **serveri** chaqiradi — Bot Fight Mode / JS challenge ularni bloklasa, ulanish ishlamaydi
  (shu yo'llar uchun Skip qoidasi).
- `/oauth/authorize` — odam brauzerda ochadigan login sahifasi: Cloudflare himoyasi unda **qolsin**.
- Javoblar `Cache-Control: no-store` — keshlanmaydi.

### 6.5 O'chirish

`.env` dan `MCP_DB_RO_PASSWORD` ni olib tashlab ilovani qayta ishga tushiring → MCP o'chadi
(rol bazada qoladi, lekin ilova unga ulanmaydi). Rolni butunlay olib tashlash:
`REVOKE ALL ON ALL TABLES IN SCHEMA public FROM wlc_ro; DROP OWNED BY wlc_ro; DROP ROLE wlc_ro;`
