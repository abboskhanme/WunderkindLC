#!/usr/bin/env bash
# Asosiy ilova qayta build qilingandan keyin (docker compose up -d --build app) — barcha filial ilovalarini YANGI image
# bilan qayta yaratadi (har birida migratsiyalar o'zi o'tadi). Filial yo'q bo'lsa hech narsa qilmaydi.
#   cd /root/WunderkindLC && bash infra/branch/update-branches.sh
set -euo pipefail
ROOT=/root/wlc-branches
shopt -s nullglob
for comp in "$ROOT"/*/docker-compose.yml; do
  d="$(dirname "$comp")"; s="$(basename "$d")"
  echo "• $s"
  docker compose -p "wlc-$s" -f "$comp" --env-file "$d/.env" up -d
done
