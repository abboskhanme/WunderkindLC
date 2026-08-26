# Cloudflare Tunnel'dan PUBLIC IP'ga o'tish (169.58.207.222)

Maqsad: trafik `Cloudflare Tunnel (cloudflared)` o'rniga `Cloudflare (proxied DNS) → server
80/8443 (nginx) → app` yo'lidan yursin. Tunnel o'tish tugaguncha PARALLEL ishlab turadi —
har qadamda orqaga qaytish oson.

⚠️ **PORTLAR:** serverdagi **443 xray (shaxsiy VPN)niki** — unga TEGILMAYMIZ. Nginx hostda
**8443** da turadi (konteyner ichida baribir 443). Tashrifchi buni sezmaydi: Cloudflare
dashboard'dagi **Origin Rule** (4-qadam) Cloudflare→server ulanishini 443 o'rniga 8443 ga
yo'naltiradi. 80-port bo'sh (xray faqat 443 da) — u nginx'niki bo'ladi.

**TARTIB MUHIM.** Har qadamda "Rollback" bo'limi bor — muammo chiqsa o'sha yerdan qaytiladi.

Tayyorlangan narsalar (repoda):

| Nima | Qayerda |
|---|---|
| nginx konfiguratsiyasi (TLS, WebSocket, 1GB yuklash) | `infra/nginx/nginx.conf` |
| `nginx` servisi (80:80, 8443:443 portlar bilan) | `docker-compose.yml` |
| Firewall skripti (80/8443 faqat Cloudflare'ga; 443'ga tegmaydi) | `infra/cloudflare-firewall.sh` |

⚠️ **IKKITA QATTIQ QOIDA (butun jarayon davomida):**

1. **DNS'da Proxy hech qachon o'chirilmaydi** (kulrang bulut / "DNS only" QILINMAYDI):
   - server IP'si ommaga oshkor bo'ladi (DDoS/skanerlash nishoni);
   - Origin Certificate brauzerlar uchun ishonchsiz — sayt "sertifikat xatosi" bilan ochilmay qoladi;
   - firewall faqat Cloudflare'ga ochiq — oddiy foydalanuvchi umuman ulana olmaydi.
2. **Firewall skriptisiz 80/8443 ochilmaydi.** `docker compose up -d nginx` FAQAT
   `infra/cloudflare-firewall.sh` muvaffaqiyatli o'tgandan keyin. Docker `ports:` ufw'ni
   chetlab o'tadi — skript buni `DOCKER-USER` zanjiri bilan yopadi, ya'ni "keyin sozlayman"
   degani "hozircha butun internetga ochiq" degani bo'lardi. (Skript 443'ga — xray'ga —
   umuman tegmaydi: qoida qo'shmaydi ham, o'chirmaydi ham.)

---

## 1-qadam. Cloudflare Origin Certificate yaratish va serverga qo'yish

Nega Origin Cert (Let's Encrypt emas): firewall 80/443'ni faqat Cloudflare IP'lariga ochadi,
ACME (Let's Encrypt) tekshiruvi esa boshqa IP'lardan keladi — murakkablashadi. Origin Cert
15 yilga beriladi, yangilash avtomatikasi kerak emas; Cloudflare unga to'liq ishonadi.

1. Cloudflare dashboard → domen `intellectschool.uz` → **SSL/TLS → Origin Server →
   Create Certificate**:
   - "Generate private key and CSR with Cloudflare" (RSA 2048) — default qoladi;
   - Hostnames: `intellectschool.uz` va `*.intellectschool.uz` (ikkalasi — crm subdomeni
     wildcard'ga kiradi);
   - Validity: 15 years.
2. Ochilgan sahifadagi ikkala blokni serverga saqlang (**sahifani yopmasdan** — private key
   keyin qayta ko'rsatilmaydi):

   ```bash
   # serverda (SSH):
   mkdir -p /root/certs && chmod 700 /root/certs
   nano /root/certs/origin.pem    # "Origin Certificate" blokini qo'ying (-----BEGIN CERTIFICATE----- ...)
   nano /root/certs/origin.key    # "Private Key" blokini qo'ying (-----BEGIN PRIVATE KEY----- ...)
   chmod 600 /root/certs/origin.pem /root/certs/origin.key

   # tekshiruv — ikkala buyruq xatosiz o'tsin va modulus/pubkey mos bo'lsin:
   openssl x509 -in /root/certs/origin.pem -noout -subject -dates
   openssl pkey -in /root/certs/origin.key -noout -check 2>/dev/null || openssl rsa -in /root/certs/origin.key -noout -check
   ```

3. **SSL/TLS → Overview** da rejimni **Full (strict)** qiling. (Origin Cert bilan "strict"
   ishlaydi — Cloudflare bu sertifikatga ishonadi. "Flexible" QO'YILMASIN: u serverga http
   bilan keladi, nginx esa 80'da faqat redirect beradi — cheksiz redirect bo'lardi.)

**Rollback:** bu qadam hech narsani o'zgartirmaydi (trafik hali tunnelda) — shunchaki davom
etmaslik mumkin.

---

## 2-qadam. Serverda: git pull + firewall + nginx

```bash
# serverda:
cd /root/IntellectCRM
git pull

# 1) AVVAL firewall (80/8443 faqat Cloudflare'ga; 22 ochiq, 443/xray'ga tegilmaydi):
bash infra/cloudflare-firewall.sh
# oxirida `ufw status verbose` va DOCKER-USER qoidalari chiqadi — Cloudflare
# oralig'lari ro'yxatda ekanini ko'zdan kechiring.

# 2) KEYIN nginx (image'ni tortadi, 80/443 ochiladi):
docker compose up -d nginx

# 3) tekshiruv:
docker compose ps                      # nginx: Up bo'lsin (restart-loop EMAS)
docker compose logs --tail=30 nginx    # xato yo'qligini ko'ring
# serverning O'ZIDAN (firewall ichkaridan bo'g'moqda yo'q; nginx hostda 8443 da!):
curl -k --resolve crm.intellectschool.uz:8443:127.0.0.1 https://crm.intellectschool.uz:8443/api/health
# javob: 200 (health OK)
curl -k --resolve intellectschool.uz:8443:127.0.0.1 https://intellectschool.uz:8443/ | head -5
# javob: landing.html boshlanishi
# 443 hamon xray'niki ekanini ham tekshirib qo'ying (nginx uni EGALLAMAGANI):
ss -tlnp | grep -E ':(80|443|8443) '
```

`cloudflared` ISHLAYVERADI — DNS hali tunnelga qaragan, foydalanuvchilar hech narsani sezmaydi.

**Rollback:** `docker compose stop nginx` — hammasi avvalgidek tunnel orqali ishlayveradi.
Firewall qoidalari zarar qilmaydi (tunnel chiquvchi ulanish, unga to'siq yo'q).

---

## 3-qadam. Lokal sinov (DNS'ni almashtirmasdan)

Firewall 8443'ni faqat Cloudflare'ga ochgan, shuning uchun avval O'Z IP'ingizga vaqtincha
ruxsat bering:

```bash
# o'z IP'ingizni bilib oling (lokal mashinada):  curl -4 -s ifconfig.me
# serverda (MENING_IP o'rniga o'sha IP):
ufw allow from MENING_IP to any port 8443 proto tcp comment 'vaqtinchalik sinov'
iptables -I DOCKER-USER 1 -s MENING_IP -p tcp -m conntrack --ctorigdstport 8443 --ctdir ORIGINAL -j ACCEPT
```

Lokal mashinada `/etc/hosts` ga qo'shing (sudo bilan):

```
169.58.207.222 intellectschool.uz www.intellectschool.uz crm.intellectschool.uz
```

⚠️ Brauzerda manzilni **`:8443` porti bilan** oching (nginx shu portda; `:8443`siz 443 ga —
xray'ga tushasiz): `https://crm.intellectschool.uz:8443`. Brauzer **"sertifikat ishonchsiz"**
deb ogohlantiradi — bu KUTILGAN holat: Origin Cert'ga faqat Cloudflare ishonadi, siz esa hozir
Cloudflare'ni chetlab to'g'ridan-to'g'ri kiryapsiz. "Advanced → Proceed" bilan davom eting
(faqat shu sinovda!). DNS almashtirilgach foydalanuvchilar odatdagi `https://...` (portsiz)
manzildan kiradi va Cloudflare'ning oddiy (ishonchli) sertifikatini ko'radi.

**Tekshirish ro'yxati:**

- [ ] `https://crm.intellectschool.uz:8443` — login qilish (cookie/CSRF ishlashi = `X-Forwarded-Proto` to'g'ri);
- [ ] rasmlar ochiladi (`/uploads` — o'quvchi surati, logotip);
- [ ] chat ishlaydi (SignalR `/hubs/chat` — xabar real vaqtda kelsin; brauzer DevTools →
      Network → WS da `101 Switching Protocols` ko'rinsin);
- [ ] jonli yangilanishlar (`/hubs/live`);
- [ ] kamera ko'rinishi (Kameralar sahifasi — HLS app orqali proksilanadi);
- [ ] katta fayl yuklash (masalan Marketing → kontent video) — 413 xatosi chiqmasin;
- [ ] `https://intellectschool.uz:8443` — landing ochiladi (xarita iframe'i bilan).
- Telegram Mini App'ni bu usulda tekshirib bo'lmaydi (Telegram real DNS ishlatadi) — u
  5-qadamdan keyin tekshiriladi.

Sinov tugagach vaqtinchalik ruxsatlarni OLIB TASHLANG va `/etc/hosts` qatorini o'chiring:

```bash
# serverda:
ufw status numbered            # 'vaqtinchalik sinov' qoidasining raqamini toping
ufw delete <RAQAM>
bash infra/cloudflare-firewall.sh   # DOCKER-USER zanjirini toza holatga qayta quradi
```

**Rollback:** hech narsa o'zgarmagan — `/etc/hosts` qatorini o'chirish kifoya.

---

## 4-qadam. Cloudflare Origin Rule: 443 → 8443 (DNS'DAN OLDIN!)

⚠️ **BU QADAM DNS ALMASHTIRISHDAN (5-qadam) ALBATTA OLDIN.** Aks holda Cloudflare serverga
odatdagi 443-portga ulanadi va **xray'ga tushadi** — sayt "yotib qoladi" (xatolik yoki
tushunarsiz javob), foydalanuvchilar buni darhol sezadi. Origin Rule oldindan qo'yilsa,
DNS almashganda trafik to'g'ridan-to'g'ri 8443 ga (nginx'ga) boradi. Qoida tunnelga
qaragan DNS'ga TA'SIR QILMAYDI (tunnel trafigi origin portidan yurmaydi) — shuning uchun
uni xotirjam oldindan yoqib qo'yish mumkin.

Cloudflare dashboard → domen `intellectschool.uz` → **Rules → Origin Rules → Create rule**:

- **Rule name:** `nginx 8443`
- **If** (Custom filter expression):
  `(http.host eq "intellectschool.uz") or (http.host eq "www.intellectschool.uz") or (http.host eq "crm.intellectschool.uz")`
- **Then → Destination Port → Rewrite to:** `8443`
- **Deploy**.

Tashrifchi uchun HECH NARSA o'zgarmaydi: u odatdagidek `https://crm.intellectschool.uz`
(443) ga kiradi, portni Cloudflare o'zi server tomonda 8443 ga almashtiradi.

**Rollback:** qoidani o'chirish/pauza qilish — hozircha trafik baribir tunnelda, hech narsa
buzilmaydi.

---

## 5-qadam. Cloudflare DNS: tunnel CNAME → A yozuv (Proxy YOQIQ)

Cloudflare dashboard → **DNS → Records**. Hozir uchta yozuv tunnelga qaraydi
(`<tunnel-id>.cfargotunnel.com` ga CNAME). Ularni BIRMA-BIR almashtiring:

| Nomi | Eski (CNAME) | Yangi |
|---|---|---|
| `crm` | `80531fd7-....cfargotunnel.com` | **A** `169.58.207.222`, Proxy: **Proxied (to'q sariq)** |
| `intellectschool.uz` (@) | o'sha tunnel | **A** `169.58.207.222`, Proxied |
| `www` | o'sha tunnel (bo'lsa) | **A** `169.58.207.222` (yoki CNAME → `intellectschool.uz`), Proxied |

Tavsiya etilgan tartib: avval **`crm`** (o'zingiz darhol tekshira olasiz), 10-15 daqiqa
kuzatib keyin apex + www.

⚠️ Har yozuvda **Proxy status = Proxied** qolishiga qarang (yuqoridagi 1-qoida).

Almashtirilgach tekshiring (lokal mashinada, `/etc/hosts` TOZA holda):

```bash
dig +short crm.intellectschool.uz        # Cloudflare IP'lari chiqadi (104.x/172.x...), 169.58.207.222 EMAS — bu TO'G'RI
curl -s https://crm.intellectschool.uz/api/health
```

Brauzerda (endi ODDIY, portsiz manzilda) login, chat, rasm, kamera — 3-qadamdagi ro'yxat +
**Telegram Mini App** (web.telegram.org ichida ochilishi — iframe cookie'lari) va **mobil
ilovalar** (Flutter — login, rasmlar). Shaxsiy **xray VPN ham ishlayotganini** tekshiring —
u 443 da qoldi, unga hech narsa tegmagan bo'lishi kerak.

**Rollback (eng muhimi):** DNS yozuvini qaytadan CNAME `80531fd7-f356-4bc6-ab30-9e3fd2c134ee.cfargotunnel.com`
(Proxied) ga qaytaring — cloudflared ishlab turgani uchun 1-2 daqiqada hammasi tunnel orqali
qaytadi. Shuning uchun bu bosqichda cloudflared O'CHIRILMAYDI. (Origin Rule'ni o'chirish shart
emas — tunnel trafigiga ta'siri yo'q.)

---

## 6-qadam. 1-2 kun kuzatish

Tunnel va nginx parallel turadi. Kuzatiladiganlar:

```bash
# serverda:
docker compose logs -f --tail=100 nginx     # 4xx/5xx to'lqini yo'qmi
docker compose logs --tail=200 app | grep -i "uploads rad etildi"   # mobil ilova rasm ololmayaptimi
docker stats --no-stream                    # nginx resurs yemayaptimi
```

- foydalanuvchi shikoyatlari: chat uzilishi, rasm ochilmasligi, video yuklash;
- Telegram Mini App va Career bot mini-app;
- kunlik backup odatdagidek o'tganini tekshiring (unga bu o'tish ta'sir qilmasligi kerak).

**Rollback:** 5-qadamdagi DNS qaytarish — hali ham bir daqiqalik ish.

---

## 7-qadam. cloudflared'ni o'chirish (hammasi barqaror bo'lgach)

1. Repoda: `docker-compose.yml` dan `cloudflared` servisini olib tashlash (ustidagi
   "VAQTINCHALIK" izohli blok), `docker-compose.override.example.yml` dagi `cloudflared`
   bo'limini ham. Commit + push.
2. Serverda:

   ```bash
   cd /root/IntellectCRM
   git pull
   docker compose up -d --remove-orphans     # cloudflared konteyneri olib tashlanadi
   ```

3. `.env` dan `TUNNEL_TOKEN` qatorini o'chirish mumkin (ixtiyoriy).
4. Cloudflare dashboard → **Zero Trust → Networks → Tunnels** → `80531fd7-...` tunnelni
   **Delete** qiling (public hostname'lari bilan).

**Rollback (bu qadamdan keyin qiyinroq):** tunnel o'chirilgach eski CNAME ishlamaydi — orqaga
yo'l endi "yangi tunnel yaratish". Shuning uchun bu qadam faqat kamida 1-2 kun muammosiz
ishlagandan keyin bajariladi.

---

## Texnik ma'lumotnoma (nima qayerda)

- **Portlar:** app ichkarida `8080` (`ASPNETCORE_URLS`, `ports:` yo'q — tashqariga faqat
  nginx/tunnel orqali); nginx konteyner ichida `80`+`443`, hostda `80`+`8443` (**host 443 =
  xray VPN, unga tegilmagan**); Cloudflare→server ulanishini Origin Rule 8443 ga buradi.
- **Ikkala domen bitta app'ga boradi** — hostni app o'zi ajratadi (`Program.cs`: apex/www →
  `landing.html`, `App__Host` → CRM SPA). Nginx'da alohida routing YO'Q, `Host` sarlavhasi
  o'zgartirilmasdan uzatiladi.
- **WebSocket yo'llari:** `/ws` (CTI Android agent), `/hubs/chat`, `/hubs/live` (SignalR) —
  nginx'da upgrade + 1 soatlik timeout. `mediamtx` tashqariga chiqmaydi (kamera HLS app'ning
  `/api/admin/cameras/...` endpointlari orqali proksilanadi).
- **Yuklash chegarasi:** nginx `client_max_body_size 1100m` — eng katta endpoint Instagram
  Reels video (1 GiB, `IgPublishConst.MaxReelsBytes`); aniq chegaralarni app o'zi tekshiradi.
- **Real IP:** Cloudflare → `CF-Connecting-IP` → nginx uni `X-Forwarded-For` ga qo'yadi →
  app `UseForwardedHeaders` bilan o'qiydi. Gzip nginx'da ATAYIN o'chiq — app o'zi siqadi.
- **Firewall reboot'dan keyin:** skript o'zini `@reboot` cron'ga yozadi (DOCKER-USER
  qoidalari xotirada turadi, ufw esa o'zi doimiy).
