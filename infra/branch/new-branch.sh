#!/usr/bin/env bash
# YANGI FILIAL — alohida ilova + alohida (BO'SH) baza, o'z xodimlari va login/parollari (2026-10-27).
# Asosiy ilova bilan BIR image (wunderkindlc-app) — qayta build yo'q. Superadmin tepadagi almashtirgichdan o'tadi.
#
#   cd /root/WunderkindLC && bash infra/branch/new-branch.sh <slug> <host> "<Nomi>" [asosiy filial nomi]
#   masalan: bash infra/branch/new-branch.sh furqat furqat.wunderkindedu.uz "Furqat" "Yaypan"
#
# Oldin Cloudflare'da: DNS A <slug> → server IP (PROXIED) va Origin Rule «nginx 8443» ga shu host qo'shilgan bo'lsin.
# Kalitlar SHU YERDA yaratiladi va ekranga chiqmaydi. Superadmin login/paroli — asosiy .env dagi OWNER_LOGIN/OWNER_PASSWORD.
# Qayta ishga tushirsa bo'ladi (idempotent): mavjud filialning .env/bazasi tegilmaydi.
set -euo pipefail

SLUG="${1:-}"; HOST="${2:-}"; NAME="${3:-}"; MAIN_NAME="${4:-Asosiy}"
MAIN_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
ROOT="${WLC_BRANCH_ROOT:-/root/wlc-branches}"   # sinov uchun almashtirsa bo'ladi
[[ "$SLUG" =~ ^[a-z0-9]{2,20}$ ]] || { echo "slug: 2-20 ta kichik lotin harf/raqam"; exit 1; }
[[ "$HOST" =~ ^[a-z0-9.-]+\.[a-z]{2,}$ ]] || { echo "host noto'g'ri: $HOST"; exit 1; }
[[ -n "$NAME" && "$NAME" != *"|"* && "$NAME" != *";"* ]] || { echo "nom kerak (| va ; belgisiz)"; exit 1; }
MAIN_ENV="$MAIN_DIR/.env"
[[ -f "$MAIN_ENV" ]] || { echo "$MAIN_ENV topilmadi"; exit 1; }

val() { grep -E "^$1=" "$2" 2>/dev/null | tail -1 | cut -d= -f2- || true; }
setval() { # setval KEY VALUE FILE — mavjud qatorni almashtiradi yoki oxiriga qo'shadi
  local k="$1" v="$2" f="$3" tmp; tmp="$(mktemp)"
  grep -vE "^$k=" "$f" > "$tmp" || true; printf '%s=%s\n' "$k" "$v" >> "$tmp"
  cat "$tmp" > "$f"; rm -f "$tmp"
}

DIR="$ROOT/$SLUG"
mkdir -p "$DIR" "$ROOT/nginx"; chmod 700 "$ROOT" "$DIR"

# ---- 1. Filial .env (faqat birinchi marta) ----
if [[ ! -f "$DIR/.env" ]]; then
  umask 077
  cat > "$DIR/.env" <<EOF
SLUG=$SLUG
APP_HOST=$HOST
ROOT_DOMAIN=$(val ROOT_DOMAIN "$MAIN_ENV")
POSTGRES_USER=wlc_$SLUG
POSTGRES_DB=wlc_$SLUG
POSTGRES_PASSWORD=$(openssl rand -hex 24)
JWT_KEY=$(openssl rand -hex 48)
CREDENTIAL_VAULT_KEY=$(openssl rand -base64 32)
OWNER_LOGIN=$(val OWNER_LOGIN "$MAIN_ENV")
OWNER_PASSWORD=$(val OWNER_PASSWORD "$MAIN_ENV")
GEMINI_API_KEY=$(val GEMINI_API_KEY "$MAIN_ENV")
GEMINI_MODEL=$(val GEMINI_MODEL "$MAIN_ENV")
BRANCH_LINKS=
EOF
  echo "• $DIR/.env yaratildi (kalitlar yangi)"
else
  echo "• $DIR/.env bor — tegilmadi"
fi

# ---- 2. Almashtirgich ro'yxati (asosiy .env da yagona manba) ----
LINKS="$(val BRANCH_LINKS "$MAIN_ENV")"
if [[ -z "$LINKS" ]]; then LINKS="$MAIN_NAME|https://$(val APP_HOST "$MAIN_ENV")"; fi
if [[ "$LINKS" != *"https://$HOST"* ]]; then LINKS="$LINKS;$NAME|https://$HOST"; fi
cp "$MAIN_ENV" "$MAIN_ENV.bak-branch-$(date -u +%Y%m%d-%H%M%S)"; chmod 600 "$MAIN_ENV".bak-branch-*
setval BRANCH_LINKS "$LINKS" "$MAIN_ENV"
for envf in "$ROOT"/*/.env; do setval BRANCH_LINKS "$LINKS" "$envf"; done
echo "• BRANCH_LINKS: $LINKS"

# ---- 3. Compose (servis nomlari filialga xos — umumiy tarmoqda `app`/`postgres` nomlari bilan to'qnashmasin) ----
cat > "$DIR/docker-compose.yml" <<EOF
name: wlc-$SLUG
services:
  db_$SLUG:
    image: postgres:16-alpine
    container_name: wlc-$SLUG-postgres
    command: [postgres, -c, shared_buffers=256MB, -c, max_connections=40, -c, random_page_cost=1.1]
    shm_size: 256mb
    environment:
      POSTGRES_USER: "\${POSTGRES_USER}"
      POSTGRES_PASSWORD: "\${POSTGRES_PASSWORD}"
      POSTGRES_DB: "\${POSTGRES_DB}"
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U \${POSTGRES_USER} -d \${POSTGRES_DB}"]
      interval: 5s
      timeout: 10s
      retries: 30
    networks: [internal]
    restart: unless-stopped
    logging: { driver: json-file, options: { max-size: "10m", max-file: "3" } }

  app_$SLUG:
    image: wunderkindlc-app:latest
    pull_policy: never
    container_name: wlc-$SLUG-app
    environment:
      ASPNETCORE_ENVIRONMENT: "Production"
      ASPNETCORE_URLS: "http://+:8080"
      ConnectionStrings__Default: "Host=db_$SLUG;Port=5432;Database=\${POSTGRES_DB};Username=\${POSTGRES_USER};Password=\${POSTGRES_PASSWORD};Maximum Pool Size=20;Minimum Pool Size=1;SSL Mode=Disable"
      Tenancy__RootDomain: "\${ROOT_DOMAIN}"
      App__Host: "\${APP_HOST}"
      Jwt__Key: "\${JWT_KEY}"
      Credentials__VaultKey: "\${CREDENTIAL_VAULT_KEY}"
      Gemini__ApiKey: "\${GEMINI_API_KEY:-}"
      GEMINI_MODEL: "\${GEMINI_MODEL:-gemini-3.1-flash-lite}"
      Branches__Links: "\${BRANCH_LINKS:-}"
      Seed__OwnerLogin: "\${OWNER_LOGIN:-admin}"
      Seed__OwnerPassword: "\${OWNER_PASSWORD}"
      DOTNET_SYSTEM_NET_SOCKETS_INLINE_OPERATIONS: "true"
    volumes:
      - uploads:/app/uploads
      - dpkeys:/app/keys
    depends_on:
      db_$SLUG: { condition: service_healthy }
    healthcheck:
      test: ["CMD-SHELL", "bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf \"GET /api/health HTTP/1.1\\\\r\\\\nHost: localhost\\\\r\\\\nConnection: close\\\\r\\\\n\\\\r\\\\n\" >&3 && head -c 200 <&3 | grep -q \" 200 \"'"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 90s
    networks: [internal, edge]
    restart: unless-stopped
    logging: { driver: json-file, options: { max-size: "10m", max-file: "3" } }

  # Kunlik zaxira: 02:00 Toshkent (21:00 UTC), oxirgi 7 tasi saqlanadi (backups volume).
  backup_$SLUG:
    image: postgres:16-alpine
    container_name: wlc-$SLUG-backup
    environment:
      PGHOST: db_$SLUG
      PGUSER: "\${POSTGRES_USER}"
      PGPASSWORD: "\${POSTGRES_PASSWORD}"
      PGDATABASE: "\${POSTGRES_DB}"
    entrypoint: ["sh", "-c", "while true; do now=\$\$(date -u +%s); next=\$\$(( (now / 86400) * 86400 + 75600 )); [ \$\$next -le \$\$now ] && next=\$\$(( next + 86400 )); sleep \$\$(( next - now )); pg_dump | gzip > /backups/$SLUG-\$\$(date -u +%Y%m%d-%H%M).sql.gz && ls -1t /backups/*.sql.gz | tail -n +8 | xargs -r rm -f; done"]
    volumes: [backups:/backups]
    depends_on:
      db_$SLUG: { condition: service_healthy }
    networks: [internal]
    restart: unless-stopped

networks:
  internal: {}
  edge:
    external: true
    name: wunderkindlc_default

volumes:
  pgdata: {}
  uploads: {}
  dpkeys: {}
  backups: {}
EOF
echo "• $DIR/docker-compose.yml yozildi"

# ---- 4. Nginx: host → filial konteyneri ----
printf '%s app_%s;\n' "$HOST" "$SLUG" > "$ROOT/nginx/$SLUG.map"

[[ "${DRY:-}" == 1 ]] && { echo "DRY: fayllar yozildi, konteynerlarga tegilmadi"; exit 0; }

# ---- 5. Ishga tushirish ----
docker compose -p "wlc-$SLUG" -f "$DIR/docker-compose.yml" --env-file "$DIR/.env" up -d
echo "• filial konteynerlari ishga tushdi, sog'lomlik kutilmoqda (migratsiyalar ~1-2 daq)..."
for i in $(seq 1 60); do
  st="$(docker inspect -f '{{.State.Health.Status}}' "wlc-$SLUG-app" 2>/dev/null || echo none)"
  [[ "$st" == healthy ]] && break; sleep 5
done
echo "• wlc-$SLUG-app: $st"

# ---- 6. Asosiy ilova (yangi BRANCH_LINKS) + nginx (filial xaritasi) + boshqa filiallar ----
(cd "$MAIN_DIR" && docker compose up -d app nginx)
docker exec wunderkindlc-nginx nginx -t && docker exec wunderkindlc-nginx nginx -s reload
for comp in "$ROOT"/*/docker-compose.yml; do
  d="$(dirname "$comp")"; s="$(basename "$d")"
  [[ "$s" == "$SLUG" ]] && continue
  docker compose -p "wlc-$s" -f "$comp" --env-file "$d/.env" up -d
done

echo
echo "TAYYOR: https://$HOST  — superadmin login: $(val OWNER_LOGIN "$MAIN_ENV") (paroli asosiy .env dagi OWNER_PASSWORD)."
echo "Yangilash (har deploydan keyin): bash infra/branch/update-branches.sh"
