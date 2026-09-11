#!/usr/bin/env sh
# ============================================================================
#  WunderkindLC — lokal rivojlanish (hot reload) uchun qisqartma.
#
#  `docker compose -f docker-compose.yml -f docker-compose.dev.yml ...` ni har safar
#  yozib o'tirmaslik uchun. Barcha argumentlar to'g'ridan-to'g'ri compose'ga ketadi.
#
#  Namunalar:
#    ./dev.sh up                 # ko'tarish (loglar ekranda; Ctrl+C bilan to'xtaydi)
#    ./dev.sh up -d              # fonda ko'tarish
#    ./dev.sh logs -f app        # faqat backend loglari
#    ./dev.sh restart app        # backendni qayta ishga tushirish
#    ./dev.sh down               # to'xtatish (baza volume'i saqlanadi)
#    ./dev.sh down -v            # to'xtatish + DEV bazasini butunlay o'chirish
#    ./dev.sh build --no-cache   # dev obrazini noldan qayta yig'ish
#
#  DIQQAT: bu FAQAT lokal mashina uchun. Serverda odatdagidek `docker compose up -d --build`.
# ============================================================================
set -e
cd "$(dirname "$0")"

if [ ! -f .env ]; then
  echo "XATO: .env fayli yo'q. Namunadan nusxa oling:  cp .env.example .env" >&2
  exit 1
fi

# Lokal override (portlar, nginx o'chirish) — bor bo'lsa qo'shiladi. Fayl .gitignore'da,
# ya'ni serverga tushmaydi va boshqa mashinada bo'lmasa hech narsa o'zgarmaydi.
if [ -f docker-compose.override.yml ]; then
  exec docker compose -f docker-compose.yml -f docker-compose.dev.yml -f docker-compose.override.yml "$@"
fi

exec docker compose -f docker-compose.yml -f docker-compose.dev.yml "$@"
