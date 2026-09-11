# Serverni ko'chirish — ketma-ketlik

Baza + **barcha fayllar** (rasm, hujjat, sertifikat, yuz selfilari, qo'ng'iroq audiolari) eski
serverdan yangisiga o'tadi. Ikkita skript bor: `migrate-export.sh` (eski server) va
`migrate-import.sh` (yangi server).

> ⚠️ **Kunlik backup arxividan foydalanmang.** U `uploads/face` (yuz bilan kirish selfilari) ni
> ATAYIN tashlab ketadi (`docker-compose.yml` → backup servisi). Ko'chirishda volume TO'LIQ olinadi.

## Nima ko'chadi

| Manba | Nima | Qayerda |
|---|---|---|
| PostgreSQL | butun baza | `postgres-data` volume → `db.dump` |
| `uploads` | rasmlar, hujjatlar, chek, sertifikat, **`uploads/face`** | volume |
| `dpkeys` | DataProtection kalitlari | volume |
| `cti-recordings` | qo'ng'iroq audiolari | volume |
| `cam-recordings` | kamera yozuvlari (ixtiyoriy, KATTA) | volume |
| `.env` | **barcha maxfiy kalitlar** — busiz Telegram/Gemini/SMS/yuz vektorlari ishlamaydi | fayl |

⚠️ **`.env` eng muhimi.** Yuz vektorlari bazada `FACE_VECTOR_KEY` bilan shifrlangan — kalit
o'zgarsa yuz bilan kirish butunlay ishlamay qoladi. `JWT_KEY` o'zgarsa hamma qaytadan login
qilishga majbur bo'ladi.

---

## 0. Tayyorgarlik (YANGI serverda, oldindan)

```bash
# Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER && newgrp docker

# Swap (1GB RAM server uchun tavsiya)
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab

# Repo
git clone <repo-url> ~/WunderkindLC && cd ~/WunderkindLC
```

Bo'sh joyni tekshiring: `df -h` — eski serverdagi `docker system df` hajmidan ko'p bo'lsin.

---

## 1. ESKI serverda — chiqarish

```bash
cd ~/WunderkindLC
bash migrate-export.sh
# kamera yozuvlari ham kerak bo'lsa:
# WITH_CAMERA=1 bash migrate-export.sh
```

Skript: `app`/`cloudflared`/`mediamtx` ni to'xtatadi (baza dump paytida o'zgarmasin) →
`pg_dump -Fc` → har bir volume ni `.tar.gz` → `.env` nusxasi → `migration-YYYYMMDD_HHMM/` papkasi.

**Shu daqiqadan CRM ishlamaydi** — ko'chirish oynasi boshlandi. Kechqurun/dam olish kunida qiling.

## 2. Ko'chirish

Lokal kompyuterdan (yoki to'g'ridan-to'g'ri server→server, kalit sozlangan bo'lsa):

```bash
rsync -avz --progress <user>@<eski>:~/WunderkindLC/migration-*/ ~/migration/
rsync -avz --progress ~/migration/ <user>@<yangi>:~/migration/
```

⚠️ `env.backup` ichida barcha parollar bor — ko'chirish tugagach lokal nusxani o'chiring.

Skript `SHA256SUMS` yozadi, `migrate-import.sh` esa uni birinchi qadamda tekshiradi — yarim
ko'chgan arxiv jimgina ochilib, ichidan yarim ma'lumot chiqib ketmasin.

**Qo'shimcha kafolat:** provayder panelidan (DigitalOcean va h.k.) **Snapshot** olib qo'ying —
butun diskning nusxasi, biror narsa esdan chiqsa qaytarib olasiz.

## 3. Cloudflare tunnelini bo'shating

```bash
# ESKI serverda:
docker compose stop cloudflared
```

⚠️ **Bitta tunnel tokenini ikki serverda ishlatib bo'lmaydi** — trafik ikkiga bo'linib,
tasodifiy serverga tushadi. Yangisini ko'tarishdan OLDIN eskisini to'xtating.

## 4. YANGI serverda — tiklash

```bash
cd ~/WunderkindLC
bash migrate-import.sh ~/migration
```

Skript: `.env` ni qo'yadi → faqat `postgres` ni ko'taradi → bazani `DROP`+`CREATE`+`pg_restore`
qiladi → volume'larni tiklaydi → `docker compose up -d --build` → log va tekshiruv ro'yxati.

> `--build` SHART: landing fayllari image ICHIDA keladi (bind-mount yo'q).

## 5. Tekshirish (10 daqiqa)

| # | Nima | Qanday |
|---|---|---|
| 1 | Tunnel ulandimi | Cloudflare panel → Tunnels → yangi konnektor ko'rinsin |
| 2 | CRM ochiladimi | `https://lc.wunderkindedu.uz` → login |
| 3 | **RASMLAR** | o'quvchi surati, logotip, kitob muqovasi ochiladimi |
| 4 | Landing | `curl -sI https://wunderkindedu.uz/sertifikatlar.js \| head -1` → `200` |
| 5 | Eski kod qolmaganmi | `curl -s https://wunderkindedu.uz/landing.js \| grep -c "kamida bitta"` → `0` |
| 6 | Telegram bot | `/start` → klaviatura chiqsin |
| 7 | Yuz bilan kirish | bitta o'quvchida sinang (`FACE_VECTOR_KEY` to'g'ri kelganini isbotlaydi) |
| 8 | Backup | `docker compose logs backup \| tail -5` → jadval qatori |

⚠️ 3-band bajarilmasa (rasm ochilmasa) — avval login qilgan holda tekshiring: `/uploads`
**login talab qiladi** (`UploadsGuard`). Mehmon uchun faqat logotip va landing rasmlari ochiq.
Batafsil: `.claude/rules/uploads-security.md`.

## 6. Eski serverni o'chirish

Yangisi **kamida 2-3 kun** muammosiz ishlagach:

```bash
# ESKI serverda
docker compose down          # volume'lar QOLADI (`-v` QO'YMANG!)
```

`migration-*` papkasini va `env.backup` ni xavfsiz joyga (parol menejeri / shifrlangan disk)
ko'chiring, serverdan o'chiring.

---

## Muammo bo'lsa — orqaga qaytish

Yangi serverda hech narsa o'zgartirilmagan bo'lsa, eski server hamon butun:

```bash
# YANGI serverda
docker compose stop cloudflared
# ESKI serverda
docker compose up -d
```

Tunnel eski serverga qaytadi. Ma'lumot yo'qolmaydi — yangi serverda ishlangan bo'lsa,
o'sha ish yo'qoladi (shuning uchun ko'chirishdan keyin darhol tekshiring).
