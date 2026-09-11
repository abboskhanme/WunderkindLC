#!/usr/bin/env bash
# ============================================================================
# WunderkindLC — firewall: 22 hammaga, 80/8443 FAQAT Cloudflare IP oralig'lariga.
#
# ⚠️ 443-PORTGA UMUMAN TEGILMAYDI — u serverdagi xray (shaxsiy VPN)niki: skript unga
#    qoida QO'SHMAYDI ham, O'CHIRMAYDI ham. Nginx hostda 8443 da (konteyner ichida 443),
#    Cloudflare esa Origin Rule bilan 443→8443 ga keladi (PUBLIC-IP-OTISH.md).
#
# ⚠️ O'ZINI QULFLAB QO'YMASLIK: SSH (22) ruxsati BIRINCHI qo'shiladi va faqat
#    shundan keyin "default deny" + enable qilinadi. Skript SSH sessiya ichida
#    xavfsiz ishga tushiriladi — 22-port hech qachon yopilmaydi.
#
# ⚠️ NEGA FAQAT ufw YETMAYDI: docker compose `ports:` bilan ochilgan portlar
#    ufw'ni CHETLAB O'TADI (Docker o'z DNAT/FORWARD qoidalarini ufw'dan oldin
#    qo'yadi). Shuning uchun skript ikki qatlamda ishlaydi:
#      1) ufw  — hostning o'zi uchun (SSH, default deny);
#      2) DOCKER-USER iptables zanjiri — konteyner portlari (nginx 80/8443) uchun.
#    Faqat ufw qilinsa, 80/8443 amalda BUTUN internetga ochiq qolardi.
#
# Idempotent: qayta ishga tushirish xavfsiz (ufw takror qoidani o'zi tashlaydi,
# DOCKER-USER zanjiri esa har safar tozalab qayta quriladi). Cloudflare IP
# ro'yxati o'zgarganda ham shunchaki QAYTA ISHGA TUSHIRING.
#
# ⚠️ REBOOT: DOCKER-USER qoidalari qayta yuklashda YO'QOLADI. Skript oxirida
#    o'zini @reboot cron'ga yozib qo'yadi (allaqachon bo'lsa — tegmaydi).
#
# ⚠️ PORTLAR SOZLANADI — `CF_PORTS`. Standart "80 8443" (eski server: 443 ni xray
#    band qilgani uchun nginx 8443 da edi). Nginx TO'G'RIDAN-TO'G'RI 443 da turgan
#    serverda 443 ni ham yozing, aks holda u BUTUN INTERNETGA OCHIQ qoladi va
#    Cloudflare'ni chetlab o'tib to'g'ridan-to'g'ri origin'ga kirish mumkin bo'ladi:
#        CF_PORTS="80 443" bash infra/cloudflare-firewall.sh
#    @reboot cron ham AYNAN shu qiymat bilan yoziladi.
#
# Ishlatish (serverda, root):  bash /root/WunderkindLC/infra/cloudflare-firewall.sh
# ============================================================================
set -euo pipefail

# Cloudflare'ga ochiladigan portlar (bo'sh joy bilan ajratilgan).
CF_PORTS="${CF_PORTS:-80 8443}"
CF_PORTS_CSV="$(echo "$CF_PORTS" | tr ' ' ',')"
echo "==> Himoyalanadigan portlar: $CF_PORTS (faqat Cloudflare IP'lariga ochiladi)"

if [ "$(id -u)" -ne 0 ]; then
  echo "XATO: root sifatida ishga tushiring (sudo bash $0)" >&2
  exit 1
fi

command -v ufw      >/dev/null || { echo "XATO: ufw o'rnatilmagan (apt install ufw)" >&2; exit 1; }
command -v curl     >/dev/null || { echo "XATO: curl o'rnatilmagan" >&2; exit 1; }
command -v iptables >/dev/null || { echo "XATO: iptables topilmadi" >&2; exit 1; }

echo "==> Cloudflare IP ro'yxatlarini olish..."
CF_V4="$(curl -fsS --max-time 15 https://www.cloudflare.com/ips-v4)"
CF_V6="$(curl -fsS --max-time 15 https://www.cloudflare.com/ips-v6)"

# HIMOYA: ro'yxat bo'sh yoki buzuq kelsa (tarmoq xatosi, HTML sahifa qaytishi) —
# HECH NARSANI o'zgartirmasdan chiqamiz. Aks holda 80/8443 hech kimga ochilmay
# qolishi (yoki aksincha, cheklovsiz qolishi) mumkin edi.
check_cidrs() {
  local list="$1" re="$2" name="$3" n=0 line
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    if ! printf '%s' "$line" | grep -Eq "$re"; then
      echo "XATO: $name ro'yxatida CIDR emas qator: '$line' — hech narsa o'zgartirilmadi" >&2
      exit 1
    fi
    n=$((n + 1))
  done <<< "$list"
  if [ "$n" -lt 3 ]; then
    echo "XATO: $name ro'yxati shubhali qisqa ($n qator) — hech narsa o'zgartirilmadi" >&2
    exit 1
  fi
}
check_cidrs "$CF_V4" '^[0-9]{1,3}(\.[0-9]{1,3}){3}/[0-9]{1,2}$' "ips-v4"
check_cidrs "$CF_V6" '^[0-9A-Fa-f:]+/[0-9]{1,3}$'               "ips-v6"

# ---------------------------------------------------------------------------
# 1-QATLAM: ufw (host portlari)
# ---------------------------------------------------------------------------
echo "==> 1) SSH (22) HAMMAGA ochiladi (birinchi — o'zimizni qulflamaslik uchun)"
ufw allow 22/tcp comment 'SSH' >/dev/null

echo "==> 2) ufw: $CF_PORTS faqat Cloudflare IP'lariga (hostning o'zi uchun)"
while IFS= read -r cidr; do
  [ -n "$cidr" ] && ufw allow from "$cidr" to any port "$CF_PORTS_CSV" proto tcp comment 'Cloudflare' >/dev/null
done <<< "$CF_V4"
while IFS= read -r cidr; do
  [ -n "$cidr" ] && ufw allow from "$cidr" to any port "$CF_PORTS_CSV" proto tcp comment 'Cloudflare' >/dev/null
done <<< "$CF_V6"

echo "==> 3) ufw default: kiruvchi DENY, chiquvchi ALLOW; yoqish"
ufw default deny incoming  >/dev/null
ufw default allow outgoing >/dev/null
ufw --force enable >/dev/null

# ---------------------------------------------------------------------------
# 2-QATLAM: DOCKER-USER (konteyner portlari — nginx 80/8443)
# Docker DNAT'dan keyin FORWARD trafigi shu zanjirdan o'tadi; asl (tashqi)
# portni conntrack'dan olamiz (8443 — DNAT'dan OLDINGI host porti). Zanjir har
# safar tozalab qayta quriladi. 443 bu yerda ham YO'Q: xray host protsessi,
# docker FORWARD'idan o'tmaydi — unga bu zanjir baribir ta'sir qilmaydi.
# ---------------------------------------------------------------------------
EXT_IF="$(ip route show default | awk '/default/ {print $5; exit}')"
[ -n "$EXT_IF" ] || { echo "XATO: tashqi interfeys topilmadi (ip route show default)" >&2; exit 1; }
echo "==> 4) DOCKER-USER: $CF_PORTS konteyner trafigi faqat Cloudflare'dan (interfeys: $EXT_IF)"

build_docker_user() { # $1 = iptables|ip6tables, $2 = CIDR ro'yxati
  local ipt="$1" list="$2" cidr port
  "$ipt" -N DOCKER-USER 2>/dev/null || true   # docker odatda o'zi yaratadi
  "$ipt" -F DOCKER-USER
  while IFS= read -r cidr; do
    [ -z "$cidr" ] && continue
    for port in $CF_PORTS; do
      "$ipt" -A DOCKER-USER -i "$EXT_IF" -s "$cidr" -p tcp \
             -m conntrack --ctorigdstport "$port" --ctdir ORIGINAL -j ACCEPT
    done
  done <<< "$list"
  # Cloudflare bo'lmagan hamma $CF_PORTS -> DROP
  for port in $CF_PORTS; do
    "$ipt" -A DOCKER-USER -i "$EXT_IF" -p tcp \
           -m conntrack --ctorigdstport "$port" --ctdir ORIGINAL -j DROP
  done
  # Qolgan trafik — Docker'ning odatiy oqimiga qaytadi (bu qator MAJBURIY:
  # zanjirni flush qilganda Docker qo'ygan default RETURN ham o'chadi).
  "$ipt" -A DOCKER-USER -j RETURN
}
build_docker_user iptables  "$CF_V4"
if command -v ip6tables >/dev/null; then
  build_docker_user ip6tables "$CF_V6"
fi

# ---------------------------------------------------------------------------
# REBOOT'da tiklanish: DOCKER-USER qoidalari xotirada — @reboot cron qo'yamiz.
# (sleep 60 — docker va tarmoq ko'tarilishini kutish uchun.)
# ---------------------------------------------------------------------------
SCRIPT_PATH="$(readlink -f "$0")"
CRON_LINE="@reboot sleep 60 && CF_PORTS=\"$CF_PORTS\" /usr/bin/env bash $SCRIPT_PATH >> /var/log/cloudflare-firewall.log 2>&1"
if ! crontab -l 2>/dev/null | grep -Fq "$SCRIPT_PATH"; then
  ( crontab -l 2>/dev/null; echo "$CRON_LINE" ) | crontab -
  echo "==> 5) @reboot cron qo'shildi ($SCRIPT_PATH)"
else
  echo "==> 5) @reboot cron allaqachon bor — tegilmadi"
fi

echo
echo "==> Tayyor. ufw holati:"
ufw status verbose
echo
echo "==> DOCKER-USER (IPv4) birinchi qoidalar:"
iptables -L DOCKER-USER -n --line-numbers | head -12
echo "    (to'liq: iptables -L DOCKER-USER -n)"
