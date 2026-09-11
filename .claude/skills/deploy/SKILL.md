---
name: deploy
description: WunderkindLC'ni prod serverga deploy qilish (docker compose, .env, Cloudflare Tunnel, PostgreSQL backup/restore). Deploy, serverni ko'chirish, backup tiklash yoki tunnel sozlash kerak bo'lganda ishlating.
---

# Deploy (prod)

```bash
docker compose up -d --build    # app + postgres + cloudflared + backup + mediamtx
```

- **`.env`** (git'ga tushmaydi): `ROOT_DOMAIN=wunderkindedu.uz`, `APP_HOST=lc.wunderkindedu.uz`,
  `POSTGRES_PASSWORD` (kuchli!), `POSTGRES_USER`/`POSTGRES_DB` (default wunderkindlc), `JWT_KEY`,
  `TUNNEL_TOKEN` (tunnel `80531fd7`).
- **KALITLAR faqat `.env` da** (bazada saqlanmaydi, UI'dan kiritilmaydi — `AppSecrets`):
  `TELEGRAM_BOT_TOKEN`, `FCM_SERVICE_ACCOUNT_JSON`, `GEMINI_API_KEY`, `AZURE_SPEECH_KEY/REGION`,
  `ESKIZ_EMAIL/PASSWORD`, `TURNSTILE_USERNAME/PASSWORD`, `MOIZVONKI_*`,
  `INSTAGRAM_APP_SECRET/VERIFY_TOKEN`. O'zgartirgach `docker compose up -d`.
  ⚠️ **Yangi kalit qo'shsangiz `docker-compose.yml` dagi `app` → `environment:` ga HAM qo'shing** —
  `app` servisida `env_file` YO'Q, ya'ni faqat `.env` ga yozilgan qiymat konteynerga UMUMAN
  yetib bormaydi va modul jimgina "sozlanmagan" bo'lib qoladi (`EnvKeysWiringTests` shuni qulflaydi). Eski (kalitlar bazada bo'lgan) o'rnatishdan yangilashda — DEPLOY.md §2.1
  (migratsiya ustunlarni o'chiradi; qiymatlar startup logida `.env` qatorlari bo'lib chiqadi).
- **DB:** PostgreSQL 16 (alpine). Server'da **>=1GB RAM** (+swap tavsiya). Baza `wunderkindlc`.
  Volume `postgres-data`.
- **Cloudflare panel:** Public Hostname `lc.wunderkindedu.uz` → HTTP → `app:8080`. Ko'chirishda
  eski serverdagi cloudflared'ni to'xtating (bir tokenni 2 joyda ishlatmang).
- App porti internetga ochilmaydi (faqat cloudflared).
- **Backup:** kunlik 02:00 Toshkent, `pg_dump` → `.sql.gz`, 7 kun saqlanadi (`postgres-backups`
  volume, backup konteynerda).
  Restore: `docker exec wunderkindlc-backup sh -c "gunzip -c ...|psql"`.
- **Ubuntu/Docker auditi + noldan o'rnatish** — `DEPLOY.md` "0-bo'lim" (Docker o'rnatish, swap,
  klon, run, tekshirish).

## ⚠️ "Lokalda ishlaydi, prodda yo'q" — landing va lid formasi

Sabab deyarli har doim BITTA: **CSP faqat proddagina qo'llanadi**
(`Program.cs` → `if (!app.Environment.IsDevelopment())`). Dev'da (`dotnet run`) CSP UMUMAN
yuborilmaydi, konteynerda esa `ASPNETCORE_ENVIRONMENT=Production` — ya'ni brauzer prodda
qo'shimcha qoidalarni qo'llaydi va farq shu yerdan chiqadi.

Statik sahifa (`landing.html`, `sertifikatlar.html`) yozganda:

- **Inline `<script>` ISHLAMAYDI** — `script-src 'self' …` da `'unsafe-inline'` YO'Q (ataylab).
  Butun mantiq inline blokda bo'lsa sahifa prodda O'LIK bo'ladi: tugmalar bosilmaydi, forma
  yuborilmaydi, hech qanday xato ko'rinmaydi. Skriptni ALOHIDA `.js` fayliga chiqaring.
- **Inline `on*=` atributlari** (`onclick=`, `onmouseover=`) ham shu sababdan ishlamaydi —
  `addEventListener` ishlating.
- **Yangi tashqi host** (iframe, shrift, skript, so'rov) qo'shsangiz — CSP'dagi tegishli
  direktivaga ham qo'shing: `frame-src` (xarita), `style-src`+`font-src` (Google Fonts),
  `connect-src` (fetch), `script-src`. Aks holda faqat prodda jim bloklanadi.

Tekshirish (deploydan keyin, 1 daqiqa): sahifani prodda oching → DevTools → Console.
`Refused to … because it violates the Content Security Policy` qatorlari aynan shu muammoni
ko'rsatadi.

## Deploy tekshiruvi (landing/lid)

1. `docker compose up -d --build` — **`--build` SHART**: landing fayllari image ICHIDA keladi
   (bind-mount YO'Q), ya'ni `up -d` bilan eski nusxa qolib ketadi.
2. Cloudflare panelda **ikkala** Public Hostname bo'lsin: `lc.wunderkindedu.uz` VA apex
   `wunderkindedu.uz` (+ `www`) → HTTP → `app:8080`. Apex marshrutsiz landing umuman ochilmaydi.
3. **Deploy o'tganini ISBOTLANG** (pastdagi "Deploy o'tdimi?" bo'limi) — `docker compose up -d
   --build` qildim degan gap deploy o'tganini isbotlamaydi.
4. Cloudflare keshi: landing HTML/JS origin'dan `Cache-Control: no-cache, no-store,
   must-revalidate` bilan keladi, lekin CF sozlamalarida "Cache Everything" qoidasi bo'lsa eski
   nusxa qolishi mumkin — shubha bo'lsa **Purge Everything**.
5. Lidni oxirigacha sinang: apex sahifasidagi forma → CRM → "Lidlar" bo'limida yangi qator.
   `429` chiqsa — bu rate-limit (`public-lead`, IP bo'yicha daqiqada 5 ta); javob endi
   o'zbekcha matn bilan keladi ("Juda ko'p urinish…"), ya'ni chalkashmaydi.

## Deploy o'tdimi? — ISBOTLASH (landing statik fayllari)

> ⚠️ **"`docker compose up -d --build` qildim" degan gap deploy o'tganini ISBOTLAMAYDI.**
> Aynan shu sababdan 2026-08-05 da tuzatilgan xato prodda **13 kun** turib qoldi: image yangilangan,
> lekin foydalanuvchi eski `landing.js` ni olayotgan edi va lid formasi validatsiyada to'xtardi.
> Quyidagi uchta `curl` — deploy o'tganining YAGONA isboti. Domenni o'zingiznikiga almashtiring.

```bash
D=wunderkindedu.uz

# 1) ESKI KOD QOLMAGANMI? 2026-08-05 da (commit 0ad95e1) o'chirilgan xato matni.
#    Natija 0 bo'lishi SHART. 1 chiqsa — eski nusxa berilyapti (image yoki kesh).
curl -s "https://$D/landing.js" | grep -c "kamida bitta"

# 2) YANGI FAYL BORMI? sertifikatlar.js 2026-08-18 da yaratilgan.
#    200 bo'lishi SHART. 404 — deploy UMUMAN o'tmagan (eski image ishlayapti).
curl -sI "https://$D/sertifikatlar.js" | head -1

# 3) MUAMMO QAYERDA — image'dami yoki Cloudflare keshidami?
curl -sI "https://$D/landing.js" | grep -i "cf-cache-status\|^age\|cache-control"
```

3-buyruq natijasini qanday o'qish kerak:

| Ko'rinish | Ma'nosi | Nima qilish kerak |
|---|---|---|
| `cf-cache-status: DYNAMIC` yoki `MISS`, `age` yo'q | Javob **origin'dan** keladi | Muammo image'da — `docker compose up -d --build` qaytadan, `--build` SIZ emas |
| `cf-cache-status: HIT` + katta `age` (masalan `age: 900000`) | Javobni **Cloudflare** o'z keshidan berayapti | **Purge Everything** (pastda) |
| `cache-control: no-cache, no-store, must-revalidate` yo'q | Eski image (yangi kesh siyosati yetib bormagan) | `--build` bilan qayta deploy |

**Cloudflare → Purge Everything qachon kerak:** faqat 3-holatda — `cf-cache-status: HIT` bo'lsa,
yoki 1/2-tekshiruv o'tmay turib origin'da fayl TO'G'RI ekaniga ishonch hosil qilinganda
(`docker exec wunderkindlc-app grep -c "kamida bitta" /app/wwwroot/landing.js` → 0, lekin
tashqaridan 1 chiqsa — bu aniq kesh). Panel: **Caching → Configuration → Purge Everything**.
Purge'dan keyin 1-3 tekshiruvni QAYTA ishga tushiring.

### ⚠️ Versiya belgisini QO'LDA yangilash

`landing.html` va `sertifikatlar.html` da skriptlar `?v=YYYYMMDD` kesh-buster bilan ulangan:

```html
<script src="/landing.js?v=20260818" defer></script>
<script src="/sertifikatlar.js?v=20260818" defer></script>
```

SPA assetlari (`/assets/index-XXXX.js`) kontent-hash bilan chiqadi va o'zi eskiradi, landing fayllari
esa **doimiy nomli** — manzil o'zgarmasa kesh yangilanmaydi.

> **`landing.js` yoki `sertifikatlar.js` ni o'zgartirdingizmi — HTML'dagi `?v=` sanasini ham
> bugungiga almashtiring.** Bu AVTOMATIK emas. Unutilsa, yangi kodni faqat keshi bo'sh brauzer
> ko'radi va yuqoridagi 1-tekshiruv "o'tgan"dek chiqib, foydalanuvchida eski nusxa qolib ketadi.

