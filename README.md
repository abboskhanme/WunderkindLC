<div align="center">

# 🎓 WunderkindLC

### Bitta o'quv markazi uchun zamonaviy CRM platformasi

Lidlar (CRM) · O'quvchilar · Guruhlar · Moliya · Jurnal · O'quv dasturi · Call-center · 3 ta alohida portal

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![React](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black)
![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white)

</div>

---

## ✨ Umumiy ma'lumot

**WunderkindLC** — o'quv markazlari uchun mo'ljallangan, lidlardan tortib to'lovlargacha bo'lgan
butun jarayonni boshqaradigan CRM tizimi. Bitta backend uch xil foydalanuvchiga xizmat qiladi:

| Portal | Foydalanuvchi | Dizayn | Holati |
|---|---|---|---|
| 🟣 **Admin panel** | Markaz ma'muriyati | Violet · Desktop | To'liq |
| 🟢 **O'qituvchi ilovasi** | O'qituvchilar | Teal · Mobil (WebView) | To'liq |
| 🔵 **O'quvchi ilovasi** | O'quvchi / ota-ona | Blue · Mobil (WebView) | To'liq |
| 💼 **Wunderkind Career** | Ishga kirmoqchi nomzodlar | Telegram Mini App (`/vakansiya`) | To'liq |

> Bitta Flutter WebView ilovadan o'qituvchi ham, o'quvchi ham kira oladi — rolga qarab tegishli portal ochiladi.

---

## 🧩 Texnologiyalar

**Backend**
- ASP.NET Core 8 (C#) — **Clean Architecture**
- Entity Framework Core 8 + **PostgreSQL 16** (`Npgsql`, inkremental migratsiyalar)
- **SignalR** (turniket jonli oqimi) · **JWT** auth
- **Telegram bot** · **OpenXML** (shartnoma/hisobot/Excel eksport)

**Frontend**
- React 19 + TypeScript + **Vite**
- **Tailwind CSS** (admin) + custom CSS dizayn-tizimlari (teacher/student portallari)
- Recharts (admin grafiklar) · lucide ikonkalar · React Router

**Infratuzilma**
- **Docker Compose** — `app` · `postgres` · `cloudflared` · `backup` · `mediamtx` (kamera)
- **Cloudflare Tunnel** (port internetga ochilmaydi)

---

## 🏛 Solution tuzilishi

```
WunderkindLC.slnx
├── WunderkindLC.Domain          # Entitylar, Rollar (sof biznes modeli)
├── WunderkindLC.Application      # Servislar, DTO'lar, Abstraksiyalar, SignalR Hub'lar
├── WunderkindLC.Infrastructure   # AppDbContext, EF migratsiyalar
├── WunderkindLC.Server           # Controllers, Program.cs, appsettings
└── WunderkindLC.Client           # React SPA (admin + teacher + student portallari)
```

---

## 🚀 Asosiy imkoniyatlar

### 🟣 Admin panel
- **CRM / Lidlar** — kanban, sinov darslari, konversiya, statistika
- **O'quvchilar** — profil, ko'p-guruh a'zoligi, daftar (oyma-oy), to'lov tarixi, turniket, feedback
- **Guruhlar** — kurs + o'qituvchi + dars kunlari/vaqti; oylik jurnal
- **Kurslar + O'quv dasturi** — daraja → mavzu → band ierarxiyasi (Excel import)
- **Moliya / Kassa** — kirim/chiqim, oylik hisoblash, **kurs/guruh bo'yicha daromad hisoboti**
- **Maosh** — qat'iy yoki guruhdan yig'ilgan to'lovning foizi
- **Xabarlar** — Telegram (yagona tashqi kanal) + kabinet bildirishnomasi, avto-xabar qoidalari (10 trigger).
  2026-10 da SMS (Eskiz / Local SMS), push, guruh chati, ilova (APK) va Local Call olib tashlandi.
- **Marketing — Instagram AI agenti** — izoh/DM ga avtojavob, inbox, javob qoidalari,
  bilim bazasi, avtomatik lid yaratish (`instagram/`)
- **Daraja testi** (lidlar) · guruh testlari natijalari (oflayn — ball qo'lda; onlayn rejim 2026-10 da olib tashlandi)
- **Kitoblar sotuvi** — ombor + botdan buyurtma + admin tasdiqlash + analitika
- **Shartnomalar** — Word/matnli andoza (`@`-o'rinbosarlar) → har bir oluvchi uchun .docx hosil bo'ladi
  va saqlanadi; superadmin uni yakunlab **tayyor PDF nusxasini yuklaydi** — shundan keyin shartnoma
  oluvchining (o'qituvchi/o'quvchi) ilovasidagi "Shartnoma" bo'limida ko'rinadi va yuklab olinadi
- **Vakansiyalar (Wunderkind Career)** — bo'sh ish o'rinlari e'loni + nomzod arizalari (bosqichma-bosqich);
  nomzod tomoni **alohida Telegram bot** va uning Mini App'ida (`crm.<domen>/vakansiya`)
- **AI check · Kameralar · Arxiv**

### 🟢 O'qituvchi ilovasi (mobil)
- Guruhga kirib **oylik jurnal** (baho/davomat) yuritish
- **O'quv dasturi o'tilishi** + tugash prognozi (sana bilan)
- O'z maoshi · Baholash
- **Shartnoma** — o'zi bilan tuzilgan shartnomaning elektron (PDF) nusxasi (Profil ichida)

### 🔵 O'quvchi ilovasi (mobil)
- **Duolingo uslubidagi o'quv dasturi yo'l-xaritasi** (o'tilgan/qolgan + prognoz)
- **Umumiy statistika** — diagrammalarda (baholar trendi, davomat, uy vazifa va xulq, feedback)
- Baholar · Davomat · To'lovlar · Reyting
- **Shartnoma** — o'quvchi va ota-ona bilan tuzilgan shartnomaning elektron (PDF) nusxasi
- Telegram kanalga o'tish · kabinet bildirishnomalari

---

## 💳 Billing mantig'i (qisqacha)

- **A'zolik holati:** `trial` (sinov, to'lovsiz) · `active` · `frozen`
- **Aktivlashtirish** — birinchi oy qisman (qolgan darslar nisbatida)
- **Muzlatish** — o'tilgan darslar uchun qisman hisob
- **Oylik hisob** — faqat faol a'zoliklarning `MonthlyFee` yig'indisi (**per-guruh**, `MonthlyCharge`)

Batafsil: [`.claude/rules/billing.md`](.claude/rules/billing.md)

---

## 🔐 Maxfiy kalitlar — faqat `.env`

Barcha maxfiy qiymatlar (Telegram bot tokeni, Google service account JSON — Play Integrity zaxirasi,
Gemini/Azure kaliti, turniket login/paroli) **`.env` dan** o'qiladi — yagona joy
`AppSecrets` (Application/Services). Ular **bazada saqlanmaydi** va Sozlamalar sahifasidan
kiritilmaydi: sahifada faqat "sozlangan / sozlanmagan" holati va qo'shiladigan `.env` qatori
ko'rinadi. Sabab — baza dump'i yoki Telegram'ga yuboriladigan zaxira nusxa ichida kalit sizib
chiqmasin. Namuna: [`.env.example`](.env.example).

Maxfiy **bo'lmagan** sozlamalar (bot username, kanal, turniket IP, yoqish-o'chirish bayroqlari)
env-wins: `.env` da berilsa har deploy'da qo'llanadi, bo'sh qoldirilsa UI'dan boshqariladi.

---

## 🛠 Ishga tushirish

### Talablar
- .NET 8 SDK · Node.js 20+ · PostgreSQL 16 (yoki Docker)

### Lokal (development) — Docker + **hot reload** ⭐ tavsiya etiladi

Hech narsani qo'lda qayta qurish kerak emas: `.cs` fayl o'zgarsa backend **o'zi** qayta
yig'iladi va ko'tariladi (`dotnet watch`), `.tsx`/`.css` o'zgarsa brauzer **o'zi** yangilanadi
(Vite HMR). `docker compose build` faqat birinchi marta kerak.

```bash
cp .env.example .env      # (bir marta) kalitlarni to'ldiring
./dev.sh up               # ko'tarish — loglar ekranda, Ctrl+C to'xtatadi
```

| Manzil | Nima |
|---|---|
| **http://localhost:5173** | 👈 **kundalik ish shu yerda** — React (HMR), `/api`·`/hubs`·`/uploads`·`/vakansiya` backendga uzatiladi |
| http://localhost:8080 | backendning o'zi (Mini App, statik sahifalar, API'ni to'g'ridan-to'g'ri sinash) |
| localhost:5432 | PostgreSQL (DBeaver/psql) |

```bash
./dev.sh up -d            # fonda
./dev.sh logs -f app      # backend loglari
./dev.sh restart app      # backendni qayta ishga tushirish
./dev.sh down             # to'xtatish (baza saqlanadi)
./dev.sh down -v          # to'xtatish + dev bazasini o'chirish
```

> ⚠️ **Dev'da botlar va tashqi integratsiyalar ATAYIN O'CHIQ** (`docker-compose.dev.yml`):
> Telegram har yangilanishni faqat **bir marta** yetkazadi — lokal bot serverdagi bot bilan bir xil
> token bilan ishlasa, jonli bot foydalanuvchilarga javob bermay qoladi. Shuningdek turniket va
> kamera ham o'chirilgan. Botni lokal sinash uchun BotFather'da
> **alohida test boti** oching va `.env` ga qo'shing: `DEV_TELEGRAM_BOT_TOKEN` / `DEV_CAREER_BOT_TOKEN`.

Dev muhiti prod konfiguratsiyasidan **meros** oladi (kalitlar ikki joyda takrorlanmaydi) va
alohida compose loyihasida (`wunderkindlc-dev`) ishlaydi — prod konteynerlarini bosib ketmaydi.

### Lokal (Docker'siz)
```bash
# PostgreSQL (Docker bilan eng oson)
docker run -d -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=wunderkindlc \
  -p 5432:5432 postgres:16-alpine

# Backend (API + avtomatik migratsiya, baza: wunderkindlc)
dotnet run --project WunderkindLC.Server

# Frontend (ixtiyoriy dev server)
cd WunderkindLC.Client
npm install
npm run dev
```

### Build
```bash
# Backend (SPA'siz — tez)
dotnet build WunderkindLC.Server/WunderkindLC.Server.csproj -p:BuildSpa=false

# Frontend
cd WunderkindLC.Client && npx tsc -b && npm run build
```

### Docker (production)
```bash
docker compose up -d --build
```
`.env` fayli (git'ga tushmaydi): `ROOT_DOMAIN`, `APP_HOST`, `POSTGRES_PASSWORD`, `JWT_KEY`,
`TUNNEL_TOKEN`, `OWNER_LOGIN`/`OWNER_PASSWORD` (super-admin bootstrap) va integratsiya kalitlari.
To'liq ro'yxat — [`.env.example`](.env.example), qadamlar — [`DEPLOY.md`](DEPLOY.md).

---

## 🗄 Migratsiyalar

Sxema o'zgarganda **inkremental** migratsiya qo'shiladi (baza buzilmaydi, ma'lumot saqlanadi):

```bash
dotnet build WunderkindLC.Server/WunderkindLC.Server.csproj -p:BuildSpa=false
dotnet ef migrations add <Nom> --project WunderkindLC.Infrastructure \
  --startup-project WunderkindLC.Server --no-build
```
App ishga tushganda `Migrate()` mavjud bazaga `ALTER` qo'llaydi.

> ⚠️ `migrations add` dan KEYIN `database update` qilishdan oldin **yana build qiling** — aks holda
> yangi migratsiya assembly'ga kirmaydi va EF "already up to date" deb o'tkazib yuboradi.

---

## 🔔 Bildirishnomalar

Push (FCM / Web Push) 2026-10 da olib tashlandi — o'rnatiladigan ilova ishlatilmaydi. Avto-xabarlar
kabinet qo'ng'iroqchasiga va Telegram'ga ketadi (`.claude/rules/messaging.md` §0).

---

## 📁 Diqqatga sazovor jihatlar

- **Multi-tenant YO'Q** — bitta markazga moslangan (sodda va tez)
- **Choraklar olib tashlangan** — barcha hisob **oyma-oy** (monthly)
- **Dars jadvali yo'q** — jurnal ustunlari qo'lda qo'shilgan darslardan (`LessonNote`) keladi
- **3 ta mustaqil dizayn-tizimi** — admin (violet), teacher (teal), student (blue) — bir-biriga ta'sir qilmaydi
- **1 GB RAM serverga sig'adi** — shu sabab SQL Server o'rniga PostgreSQL
- **Kunlik avtomatik backup** — `pg_dump | gzip` + uploads (va eski CTI audio) `tar.gz`, 7 kun saqlanadi;
  ixtiyoriy GPG shifrlash va rclone off-site nusxa

---

## 📚 Hujjatlar

| Fayl | Nima haqida |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Arxitektura qarorlari, entity rename'lari, olib tashlangan modullar, build buyruqlari |
| [`API_DOCUMENTATION.md`](API_DOCUMENTATION.md) | To'liq REST API ma'lumotnomasi (controller · yo'l · ruxsat) |
| [`DEPLOY.md`](DEPLOY.md) | Prod deploy, Cloudflare Tunnel, backup/restore, serverni ko'chirish |
| [`WORKLOG.md`](WORKLOG.md) | To'liq o'zgarishlar tarixi |
| [`instagram/README.md`](instagram/README.md) | Instagram AI agenti — modul, sozlash qo'llanmasi (`SOZLASH.md`), protokol (`TEXNIK.md`) |
| `.claude/rules/*.md` | Modulga xos qoidalar: billing · messaging · journal · tests · exercise · books · crm-leads · career · marketing-instagram |

---

<div align="center">

**WunderkindLC** · ASP.NET Core 8 + React · Private repository

</div>
