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
- Har bir AI so'rovi jurnalga yoziladi (Sozlamalar → Umumiy → **AI ulanishlar**).

## 1. Kimga ruxsat bor

| Kim | AI ulana oladimi |
|---|---|
| Superadmin | Ha |
| Administrator (admin) | Ha |
| Xodim (staff) | Faqat rolida **«AI ulanish (MCP, faqat o'qish)»** ruxsati berilgan bo'lsa |
| O'qituvchi, o'quvchi, ota-ona | Yo'q |

Xodimga ruxsat berish: **Boshqaruv → Rollar** → rolni tahrirlash → **«AI ulanish (MCP, faqat
o'qish)»** → Saqlash. Ruxsat olib qo'yilsa, o'sha roldagi xodimlarning AI ulanishlari **keyingi
so'rovdayoq** ishlamay qoladi (qayta kirish shart emas).

## 2. Ulanish manzili

```
https://lc.wunderkindedu.uz/mcp
```

Aniq manzil — Sozlamalar → Umumiy → **AI ulanishlar** sahifasining yuqorisida («Nusxa» tugmasi bilan).

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
Kalit faqat **ruxsat etilgan manzillarga** yuboriladi: `claude.ai`, `claude.com`, `chatgpt.com`,
`chat.openai.com` (https) va kompyuterning o'zidagi dasturlar (`http://127.0.0.1`,
`http://localhost`). `cursor://`, `vscode://` kabi manzil bilan qaytadigan ilovalar ulana olmaydi.
Yangi AI xizmati kerak bo'lsa — dasturchi `MCP_ALLOWED_REDIRECT_HOSTS` ga uning domenini qo'shadi.

Ruxsat sahifasida **kalit qayerga yuborilishi** yoziladi («kalit claude.ai ga yuboriladi»).
Tanimagan manzilni ko'rsangiz — **«Rad etish»** ni bosing.

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
| O'quvchilar | "Valiyev Ali haqida ma'lumot", "Telegramga ulanmagan faol o'quvchilar" | O'quvchilar ro'yxati va profili |
| Guruhlar | "English A1 guruhi a'zolari", "Oktabr jurnali xulosasi", "Xonalar bandligi" | Guruhlar va jurnal / Xonalar / Kurslar |
| Davomat | "Bugun kim kelmadi?", "Davomati olinmagan darslar", "Alining oxirgi oydagi davomati" | O'quvchilar davomati / Davomat qilinmagan guruhlar |
| Moliya | "Eng katta 10 ta qarzdor", "Sentabr kirim va chiqim", "Kassalar qoldig'i", "O'qituvchilar maoshi" | Moliya (kirim/chiqim, hisobotlar, maosh) |
| Lidlar | "Oxirgi 30 kun voronkasi", "Yo'qotilgan lidlar sabablari" | Lidlar / CRM statistika |
| Testlar va reyting | "Guruh testlari natijalari", "Markaz reytingi TOP-20" | Testlar natijalari / Guruhlar |
| O'qituvchilar va jadval | "O'qituvchilar ro'yxati", "Seshanba kungi jadval", "Bo'sh xonalar" | O'qituvchilar / Dars jadvali |
| Boshqa | "Bog'lanish navbati", "Topshiriqlar", "O'zgarishlar tarixi" | Bog'lanish kerak / Topshiriqlar / O'zgarishlar tarixi |

Ruxsat yo'q bo'limni so'rasangiz, AI "Ruxsat yo'q" javobini oladi — bu jurnalda ham ko'rinadi.

Cheklovlar: bitta ro'yxat ko'pi bilan **200 qator**; sana oralig'i ko'pi bilan **1 yil**; bitta
ulanishdan daqiqasiga 120 tadan ortiq so'rov qabul qilinmaydi.

## 5. Ulanishni bekor qilish

- **Superadmin**: Sozlamalar → Umumiy → **AI ulanishlar** → kerakli qatorda **«Bekor qilish»**.
  Ulanish darhol to'xtaydi; AI qayta ulanmoqchi bo'lsa, yana login va parol so'raladi.
- **Xodimning ruxsatini olish**: Boshqaruv → Rollar → «AI ulanish» ni o'chirish.
- **Xodimni o'chirish yoki parolini tiklash** — uning ulanishlari ham darhol ishlamay qoladi.
- Shu sahifada har bir so'rov ko'rinadi: kim, qaysi ilova, qaysi vosita, nechta qator, natija.
  Jurnal **1 yil** saqlanadi.

---

## 6. Texnik: yoqish va deploy (dasturchi uchun)

Arxitektura va qoidalar: `docs/SPEC-mcp.md`.

### 6.1 Sozlamalar (`.env`)

| O'zgaruvchi | Qiymat |
|---|---|
| `MCP_DB_RO_PASSWORD` | **Majburiy.** `wlc_ro` rolining paroli: `openssl rand -hex 24` (16–128 belgi, `[A-Za-z0-9_-]`). Bo'sh = MCP o'chiq |
| `MCP_PUBLIC_BASE_URL` | Ixtiyoriy. Bo'sh bo'lsa `https://<APP_HOST>` |
| `MCP_ALLOWED_REDIRECT_HOSTS` | Ixtiyoriy, vergul bilan. Sukut: `claude.ai,claude.com,chatgpt.com,chat.openai.com` |

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
# 2) Odatdagi deploy: migratsiya AddMcpReadOnly (5 ta Mcp* jadval, DROP yo'q) o'zi qo'llanadi
docker compose up -d --build app
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
