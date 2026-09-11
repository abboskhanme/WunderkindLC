#!/usr/bin/env bash
# ============================================================================
#  ESKI SERVERDA ishga tushiring — bazani va BARCHA fayllarni bitta papkaga yig'adi.
#
#      cd ~/WunderkindLC && bash migrate-export.sh
#      # kameralar yozuvi ham kerak bo'lsa (juda katta bo'lishi mumkin):
#      cd ~/WunderkindLC && WITH_CAMERA=1 bash migrate-export.sh
#
#  Natija: ./migration-YYYYMMDD_HHMM/ papkasi (uni scp bilan yangi serverga ko'chirasiz).
#
#  ⚠️ Kunlik backup arxividan FOYDALANMANG: u `uploads/face` (yuz bilan kirish selfilari) ni
#     ATAYIN tashlab ketadi. Ko'chirishda esa volume TO'LIQ olinadi.
# ============================================================================
set -euo pipefail
cd "$(dirname "$0")"

OUT="migration-$(date +%Y%m%d_%H%M)"
mkdir -p "$OUT"
echo "==> Natija papkasi: $OUT"

# --- volume nomlarini konteynerlardan aniqlaymiz (papka nomiga bog'liq bo'lmasin) ---
vol() { # vol <container> <mount-destination>
  docker inspect "$1" -f '{{range .Mounts}}{{if eq .Destination "'"$2"'"}}{{.Name}}{{end}}{{end}}' 2>/dev/null
}
V_UPLOADS=$(vol wunderkindlc-app /app/uploads)
V_KEYS=$(vol wunderkindlc-app /app/keys)
V_CTI=$(vol wunderkindlc-app /app/recordings)
V_CAM=$(vol wunderkindlc-mediamtx /recordings)

[ -n "$V_UPLOADS" ] || { echo "XATO: uploads volume topilmadi (app konteyneri ishlayaptimi?)"; exit 1; }
echo "    uploads=$V_UPLOADS  keys=$V_KEYS  cti=$V_CTI  cam=${V_CAM:-YOQ}"

# --- 1) Yozuvlar to'xtasin: app o'chiriladi, postgres ISHLAB TURADI (dump uchun) ---
echo "==> 1/5 app va tunnel to'xtatilmoqda (ma'lumot dump paytida o'zgarmasin)..."
docker compose stop app cloudflared mediamtx || true

# --- 2) Baza dump (custom format — tiklashda tezroq va ishonchli) ---
echo "==> 2/5 PostgreSQL dump..."
PGUSER_V=$(docker inspect wunderkindlc-postgres -f '{{range .Config.Env}}{{println .}}{{end}}' | sed -n 's/^POSTGRES_USER=//p')
PGDB_V=$(docker inspect wunderkindlc-postgres -f '{{range .Config.Env}}{{println .}}{{end}}' | sed -n 's/^POSTGRES_DB=//p')
PGUSER_V=${PGUSER_V:-wunderkindlc}; PGDB_V=${PGDB_V:-wunderkindlc}
docker exec wunderkindlc-postgres pg_dump -U "$PGUSER_V" -d "$PGDB_V" -Fc > "$OUT/db.dump"
echo "    baza: $(du -h "$OUT/db.dump" | cut -f1)  (user=$PGUSER_V db=$PGDB_V)"

# --- 3) Fayl volume'lari (TO'LIQ, hech narsa istisno qilinmaydi) ---
tarvol() { # tarvol <volume> <fayl-nomi>
  [ -n "$2" ] || return 0
  [ -n "$1" ] || { echo "    ! $2 — volume yo'q, tashlab ketildi"; return 0; }
  docker run --rm -v "$1":/src:ro -v "$PWD/$OUT":/out alpine \
    tar czf "/out/$2" -C /src . 2>/dev/null
  echo "    $2: $(du -h "$OUT/$2" | cut -f1)"
}
echo "==> 3/5 fayllar arxivlanmoqda (rasm, hujjat, selfi, audio)..."
tarvol "$V_UPLOADS" uploads.tar.gz          # rasmlar, hujjatlar, sertifikat, uploads/face
tarvol "$V_KEYS"    dpkeys.tar.gz           # DataProtection kalitlari
tarvol "$V_CTI"     cti-recordings.tar.gz   # qo'ng'iroq audiolari
if [ "${WITH_CAMERA:-0}" = "1" ]; then
  tarvol "$V_CAM" cam-recordings.tar.gz     # kamera yozuvlari (KATTA)
else
  echo "    kamera yozuvlari TASHLAB KETILDI (kerak bo'lsa: WITH_CAMERA=1)"
fi

# --- 4) Sozlamalar ---
echo "==> 4/5 .env va sozlamalar..."
cp .env "$OUT/env.backup"                                  # ⚠️ ICHIDA BARCHA MAXFIY KALITLAR BOR
[ -d backup-config ] && cp -r backup-config "$OUT/" || true
docker compose config > "$OUT/compose-resolved.yml" 2>/dev/null || true

# --- 5) Yakun ---
echo "==> 5/5 tekshiruv ro'yxati..."
# Nazorat summalari — ko'chirishdan keyin fayl BUTUN kelganini isbotlaydi (yarim ko'chgan
# arxiv jimgina ochilib, ichidan yarim ma'lumot chiqishi mumkin edi).
( cd "$OUT" && sha256sum ./* > SHA256SUMS 2>/dev/null || shasum -a 256 ./* > SHA256SUMS ) || true
{ echo "Sana: $(date)"; echo "Commit: $(git rev-parse --short HEAD 2>/dev/null || echo '-')"; \
  echo; echo "Fayllar:"; ls -lh "$OUT"; } > "$OUT/MANIFEST.txt"
cat "$OUT/MANIFEST.txt"

echo
echo "TAYYOR. Jami: $(du -sh "$OUT" | cut -f1)"
echo "Endi lokal kompyuteringizdan yuklab oling:"
echo "    rsync -avz --progress <user>@<eski-server>:$(pwd)/$OUT ./"
echo
echo "⚠️ app hozir TO'XTATILGAN. Ko'chirishni bekor qilsangiz: docker compose up -d"
echo "⚠️ $OUT/env.backup ichida barcha maxfiy kalitlar bor — ochiq joyda saqlamang."
