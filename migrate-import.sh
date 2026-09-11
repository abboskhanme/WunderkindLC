#!/usr/bin/env bash
# ============================================================================
#  YANGI SERVERDA ishga tushiring — bazani va barcha fayllarni tiklaydi.
#
#      cd ~/WunderkindLC && bash migrate-import.sh ~/migration-YYYYMMDD_HHMM
#
#  Oldindan: Docker o'rnatilgan, repo klon qilingan, ESKI serverdagi cloudflared TO'XTATILGAN.
# ============================================================================
set -euo pipefail
cd "$(dirname "$0")"

SRC="${1:-}"
[ -n "$SRC" ] && [ -d "$SRC" ] || { echo "Foydalanish: bash migrate-import.sh <migration-papkasi>"; exit 1; }
SRC=$(cd "$SRC" && pwd)
[ -f "$SRC/db.dump" ] || { echo "XATO: $SRC/db.dump topilmadi"; exit 1; }

# --- 0) Fayllar BUTUN ko'chganmi? (yarim ko'chgan arxiv jimgina ochilib qolmasin) ---
if [ -f "$SRC/SHA256SUMS" ]; then
  echo "==> 0/6 nazorat summalari tekshirilmoqda..."
  ( cd "$SRC" && grep -v 'SHA256SUMS' SHA256SUMS | sha256sum -c - ) \
    || { echo "XATO: fayllar buzilgan yoki to'liq ko'chmagan — rsync'ni QAYTA ishga tushiring"; exit 1; }
else
  echo "==> 0/6 SHA256SUMS yo'q — tekshiruv o'tkazib yuborildi"
fi

# --- 1) .env ---
if [ ! -f .env ]; then
  cp "$SRC/env.backup" .env
  echo "==> 1/6 .env eski serverdan ko'chirildi"
else
  echo "==> 1/6 .env allaqachon bor — TEGILMADI (eskisi: $SRC/env.backup)"
fi
[ -d "$SRC/backup-config" ] && cp -rn "$SRC/backup-config/." backup-config/ 2>/dev/null || true

# --- 2) Faqat postgres ko'tariladi (app hali YO'Q — migratsiya bo'sh bazaga tushmasin) ---
echo "==> 2/6 PostgreSQL ishga tushirilmoqda..."
docker compose up -d postgres
for i in $(seq 1 60); do
  docker compose exec -T postgres pg_isready >/dev/null 2>&1 && break
  sleep 2
done
docker compose exec -T postgres pg_isready >/dev/null 2>&1 || { echo "XATO: postgres tayyor bo'lmadi"; exit 1; }

# --- 3) Baza tiklash ---
echo "==> 3/6 baza tiklanmoqda..."
PGUSER_V=$(grep -E '^POSTGRES_USER=' .env | cut -d= -f2- | tr -d '"' ); PGUSER_V=${PGUSER_V:-wunderkindlc}
PGDB_V=$(grep -E '^POSTGRES_DB=' .env | cut -d= -f2- | tr -d '"' );   PGDB_V=${PGDB_V:-wunderkindlc}
# Toza baza: eski (bo'sh) sxema qolib ketmasin.
docker compose exec -T postgres psql -U "$PGUSER_V" -d postgres \
  -c "DROP DATABASE IF EXISTS \"$PGDB_V\";" -c "CREATE DATABASE \"$PGDB_V\" OWNER \"$PGUSER_V\";"
docker compose exec -T postgres pg_restore -U "$PGUSER_V" -d "$PGDB_V" --no-owner --no-acl < "$SRC/db.dump"
echo "    jadvallar: $(docker compose exec -T postgres psql -U "$PGUSER_V" -d "$PGDB_V" -tAc \
  "select count(*) from information_schema.tables where table_schema='public'")"

# --- 4) Fayl volume'lari ---
echo "==> 4/6 fayllar tiklanmoqda..."
# Volume'lar hali yaratilmagan — nomi compose LOYIHA nomidan yasaladi. Uni taxmin qilmaymiz:
# ishlab turgan postgres konteynerining compose yorlig'idan ANIQ olamiz.
PROJ=$(docker inspect wunderkindlc-postgres -f '{{index .Config.Labels "com.docker.compose.project"}}')
[ -n "$PROJ" ] || { echo "XATO: compose loyiha nomi aniqlanmadi"; exit 1; }
echo "    compose loyihasi: $PROJ"
untarvol() { # untarvol <volume-suffix> <arxiv>
  [ -f "$SRC/$2" ] || { echo "    ! $2 yo'q — tashlab ketildi"; return 0; }
  local v="${PROJ}_$1"
  docker volume create "$v" >/dev/null
  docker run --rm -v "$v":/dst -v "$SRC":/in:ro alpine \
    sh -c "rm -rf /dst/* /dst/.[!.]* 2>/dev/null; tar xzf /in/$2 -C /dst"
  echo "    $1 <- $2 ($(docker run --rm -v "$v":/d:ro alpine du -sh /d | cut -f1))"
}
untarvol uploads         uploads.tar.gz
untarvol dpkeys          dpkeys.tar.gz
untarvol cti-recordings  cti-recordings.tar.gz
untarvol cam-recordings  cam-recordings.tar.gz

# --- 5) To'liq ishga tushirish ---
echo "==> 5/6 ilova qurilmoqda va ishga tushirilmoqda (--build SHART)..."
docker compose up -d --build

# --- 6) Tekshiruv ---
echo "==> 6/6 tekshiruv..."
sleep 15
docker compose ps
echo
echo "Log (app, oxirgi 30 qator):"
docker compose logs --tail=30 app || true
echo
APP_HOST_V=$(grep -E '^APP_HOST=' .env | cut -d= -f2- | tr -d '"')
cat <<CHECK

TAYYOR. Endi QO'LDA tekshiring:
  1) Cloudflare panelda tunnel yangi serverga ulanganini ko'ring (eski cloudflared TO'XTATILGAN bo'lsin!)
  2) https://$APP_HOST_V — login qiling
  3) RASMLAR: o'quvchi profili / logotip / landing rasmlari ochiladimi?
     (ochilmasa: docker compose exec app ls /app/uploads | head)
  4) landing: curl -sI "https://\$(grep -E '^ROOT_DOMAIN=' .env | cut -d= -f2-)/sertifikatlar.js" | head -1  -> 200
  5) Telegram bot: /start yuboring (bir bot tokeni IKKI serverda ishlamasin — eski server o'chirilgan bo'lsin)
CHECK
