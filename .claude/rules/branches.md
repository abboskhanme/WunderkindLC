# Filiallar — har filial ALOHIDA ilova va baza (2026-10-27)

Egasining talabi: yangi filial — noldan bo'sh baza, o'z xodimlari va login/parollari; tepadagi tanlovdan FAQAT superadmin
boshqa filialga o'tadi. Kod bitta (multi-tenant YO'Q) — har filial shu image'ning alohida nusxasi.

| Qism | Qayerda |
|---|---|
| Filial yaratish (serverda) | `infra/branch/new-branch.sh <slug> <host> "<Nomi>" [asosiy nomi]` → `/root/wlc-branches/<slug>/` (.env, compose) |
| Konteynerlar | `wlc-<slug>-app` · `wlc-<slug>-postgres` · `wlc-<slug>-backup` (kunlik dump, 7 ta), servis nomlari `app_<slug>`/`db_<slug>` |
| Yo'naltirish | `infra/nginx/nginx.conf` → `map $host $app_upstream` + `/root/wlc-branches/nginx/<slug>.map` |
| Almashtirgich | `BRANCH_LINKS` (env, barcha nusxada bir xil) → `GET api/admin/branch-links` (superadmin) → `Topbar` → `BranchSwitcher` |
| Har deploydan keyin | `bash infra/branch/update-branches.sh` — filiallar yangi image bilan qayta yaratiladi |

- ⚠️ **Servis nomi `app`/`postgres` BO'LMASIN**: filial app'i asosiy tarmoqqa (`wunderkindlc_default`, nginx uchun) ulanadi —
  bir xil nom DNS aliasi bo'lib, asosiy app'ning `Host=postgres` ulanishi yoki nginx'ning `app` i filialga ketib qolardi.
  Baza faqat filialning ichki tarmog'ida.
- Har filialning O'Z `JWT_KEY`, `CREDENTIAL_VAULT_KEY`, Postgres paroli (skript serverda yaratadi, chiqarmaydi). Sessiya
  filiallar orasida O'TMAYDI — superadmin har filialda alohida kiradi (seed: asosiy `.env` dagi OWNER_LOGIN/OWNER_PASSWORD).
- Filialda Telegram bot, MCP, kamera, turniket O'CHIQ (tokenlar berilmagan): BITTA bot tokenini ikki nusxaga BERMANG (409).
- Cloudflare: DNS `A <slug>` → server IP (PROXIED) va Origin Rule «nginx 8443» ifodasiga `http.host eq "<host>"` qo'shiladi —
  aks holda trafik 443 dagi xray'ga tushadi.
- `Branch` jadvali («Boshqaruv → Filiallar») — faqat ma'lumotnoma (nom, manzil, xarita), almashtirgichga ta'sir qilmaydi.
- Umumiy (filiallararo) hisobot YO'Q — kerak bo'lsa alohida ish.
