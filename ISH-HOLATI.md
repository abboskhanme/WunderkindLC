# Ish holati — qayerda to'xtadik

> **Oxirgi yangilanish: 2026-07-30.** Bu fayl "hozir nima tayyor, nima qolgan" savoliga javob
> beradi. Modul dizayni va qoidalari — `RETENTION-BONUS-PLAN.md`; billing konvensiyalari —
> `.claude/rules/billing.md`.

---

## 1. Bugun nima qilindi

Kun davomida ikkita katta yo'nalish bo'yicha ishlandi: **o'quvchini ushlab turish bonusi**
(noldan) va **billing xatolari** (aktivlashtirish/muzlatish).

### 1.1. Ushlab turish bonusi — to'liq modul

| Bo'lak | Holat |
|---|---|
| Guruhning o'qituvchi TARIXI (`GroupTeacherAssignment`) | ✅ |
| Bonus entity'lari, sozlamalar, migratsiyalar | ✅ |
| `RetentionBonusService` — butun mantiq (yagona joy) | ✅ |
| `RetentionBonusController` + Excel eksport | ✅ |
| O'quvchi formasidagi ptichka + sanoq boshlanish oyi | ✅ |
| «Bonus hisoboti» sahifasi + berish/sozlamalar modallari | ✅ |
| O'qituvchi profilidagi «Bonus» tabi + o'qituvchi ilovasi | ✅ |
| Moliya → «Bonus» tabi (faqat hisobot) | ✅ |
| O'quvchi profilidagi «Bonus» bo'limi (admin/superadmin) | ✅ |

**Asosiy qoidalar** (batafsil — `RETENTION-BONUS-PLAN.md`):

- Sikl kaliti — **(o'quvchi × FAN)**, guruh emas. Bir fan ichida guruh almashtirish siklni buzmaydi.
- **Bir o'qituvchi — bir o'quvchi — BITTA bonus** (umr bo'yi). Bekor qilingan bonus ham bloklaydi.
- Bekor qilish sanoqni **qaytarmaydi** va qayta bonus berishga yo'l ochmaydi.
- Bonus **`SalaryLedger` ga ULANMAGAN** va **pul chiqarmaydi** — u qayd; pul odatdagi maosh
  to'lovi orqali beriladi. Moliya/Kassa/Chiqimlar raqamlari o'zgarmaydi.
- Oylik holatlar hech qayerda **saqlanmaydi** — har so'rovda qayta hisoblanadi (kechikkan to'lov
  kiritilsa katak o'z-o'zidan ✅ ga aylanadi).

### 1.2. Billing xatolari (mustaqil, bonusdan alohida)

**A. Orqaga sanalgan AKTIVLASHTIRISHDA oraliq oylar yozilmasdi.**
Iyulda qo'shilgan o'quvchi fevraldan aktivlashtirilsa faqat fevral yozilardi.
Ildiz sabab: `TuitionService.AccrueMonth` da a'zolik shoxobchasidan oldin turgan
`EnrollmentDate` filtri mart–iyunni tashlab yuborardi (fon xizmati ham tuzata olmasdi).
Tuzatildi: filtr guruhsiz shoxobchaga ko'chirildi + yangi `AccrueCatchUpAsync` oraliq oylarni
aktivlashtirish paytida **darhol** yozadi.

**B. Orqaga sanalgan MUZLATISHDA keyingi oylar bekor qilinmasdi** (A ning teskarisi).
`PurgeChargesAfterMonthAsync` bor edi, lekin faqat "guruhni yopish" yo'lida chaqirilardi.
Endi **muzlatish**, **guruh almashtirish** va **guruh yopish** — uchalasi ham bir xil.

**C. Guruh almashtirishda avans to'liq ko'chmasdi.** (B ochib berdi.) Hisoblar o'chirishga
belgilangan, lekin hali bazaga yozilmagan; avans ko'chirish esa bazadan qayta o'qiydi va EF
o'chirilgan qatorlarni baribir qaytaradi. Endi o'chirilgan oylar ro'yxati `zeroOwedMonths`
sifatida uzatiladi.

**D. Bonus IKKI MARTA berilishi (poyga).** Tekshiruv va yozuv orasida ikkinchi so'rov kirsa,
bir o'qituvchi bitta o'quvchidan ikki bonus olardi (turli fanlar orqali — unikal indeks
ushlamasdi). Endi o'quvchi kesimida advisory lock bilan ketma-ketlashtirilgan.

### 1.3. Boshqa

- Chap yon menyuni yopib-ochadigan **hamburger** endi har qanday ekranda ko'rinadi
  (desktopda menyuni yig'adi, holat `localStorage` da eslab qolinadi).

---

## 2. Qayerda ko'rinadi

| Joy | Nima |
|---|---|
| O'quvchi formasi → «Ushlab turish bonusi» | Ptichka + sanoq boshlanadigan oy (admin qo'lda kiritadi) |
| **O'quvchilar → Bonus hisoboti** (`/admin/students/bonus`) | Jadval: № · F.I.Sh · Guruh · O'qituvchi · Oylar · Sanoq · Holat. F.I.Sh/Guruh/O'qituvchi — bosiladigan havola. Filtrlar, qidiruv, Excel, sozlamalar |
| Hisobotdagi «Bonus berish» | Taqsimot avtomatik, qo'lda tahrirlanadi (`finance` yozish ruxsati) |
| **Moliya → «Bonus»** | Faqat HISOBOT: o'qituvchilar kesimi, oylar kesimi, batafsil ro'yxat, Excel |
| **O'qituvchi profili → «Bonus»** | «Yo'ldagilar» (kim necha oy to'plagan) + «Berilgan bonuslar» |
| **O'quvchi profili → «Bonus»** | Bonus olganmi/yo'qmi, qaysi oydan sanaladi, fan bo'yicha sanoq, tarix. **Faqat admin/superadmin** |
| O'qituvchi ilovasi → Maosh | Shu ikki bo'lim mobil ko'rinishda (maosh raqamlariga qo'shilmaydi) |

---

## 3. Nima tekshirilgan

Hammasi **toza PostgreSQL 16 + haqiqiy ishlayotgan API** bilan (login → endpointlar), unit test
emas. Qamrab olingan ssenariylar:

- Orqaga sanalgan aktivlashtirish (qisman oy + to'liq oylar), idempotentlik, nazorat sinovi
  (tuzatilmagan kod bilan xato qaytadi)
- Muzlatish: orqaga sanalgan, aktivlashtirishdan oldin, bugungi sana (regressiya yo'q),
  `Locked` himoyasi, guruh almashtirish + avans balansi
- Bonus: 2 fanli o'quvchi → 2 mustaqil sikl, bonus berish, takroriy bonus rad etilishi,
  bekor qilish, `nocharge` oy, 100% chegirma, guruh almashtirish siklni buzmasligi,
  teglanmagan to'lovni fanlarga taqsimlash
- Poyga: 2 parallel so'rov → 1 bonus, advisory qulf oqmasligi
- Moliya hisoboti: jami faqat `given`, kesimlar, davr filtri, Excel
- `SalaryLedgerDto` da bonus maydoni YO'Qligi (maosh tegilmagani)

---

## 4. Ochiq masalalar (keyingi ish uchun)

1. **Prodda allaqachon dublikat bonus bo'lsa, o'z-o'zidan yo'qolmaydi.** Tuzatish faqat
   yangilarining oldini oladi. Topish:
   ```sql
   SELECT sh."TeacherName", a."StudentName", COUNT(*) AS bonuslar, SUM(sh."Amount") AS jami
   FROM "RetentionBonusShares" sh
   JOIN "RetentionBonusAwards" a ON a."Id" = sh."AwardId"
   GROUP BY sh."TeacherId", sh."TeacherName", a."StudentId", a."StudentName"
   HAVING COUNT(*) > 1;
   ```
   Topilganlarini interfeysdan **bekor qilish** kerak (o'chirmang — tarix qoladi).
2. **«Qayta boshlash» allaqachon bonus berilgan davrga ham qaytara oladi.** O'sha o'qituvchiga
   qayta bonus tegmaydi (blok ishlaydi), lekin BOSHQA o'qituvchiga o'sha davr uchun ikkinchi
   bonus berish yo'li ochiq. Ataylab taqiqlanmagan — admin qaroriga qoldirilgan.
3. **`AccrueDue` oralig'i hamon `EnrollmentDate` dan boshlanadi.** Orqaga sanalgan a'zolik
   aktivlashtirish tugmasidan BOSHQA yo'l bilan (import / bevosita bazada) paydo bo'lsa,
   oraliq oylar yozilmay qoladi. Oralig'ini kengaytirish **ataylab qilinmadi**: u ilgari
   ataylab hisoblanmagan eski oylar uchun barcha o'quvchiga birdaniga qarz yozib yuborardi.
4. **Teglanmagan (`GroupId=null`) hisob/to'lovni narx nisbatida taqsimlash — 3 ta nusxa**
   (`SalaryLedger`, `GroupBalanceService`, `RetentionBonusService`). Konvensiya bir xil,
   lekin umumiy yordamchiga chiqarish kerak.
5. **`ForTeacherAsync` butun hisobotni hisoblaydi** — har o'qituvchi profili ochilganda barcha
   ptichkali o'quvchilar bo'ylab yuriladi. Hozircha maqbul; ptichkali o'quvchilar yuzlab bo'lsa
   kesh yoki `onlyTeacherId` filtri kerak bo'ladi.
6. **`GiveAsync` o'qituvchi shu siklda haqiqatan o'qitganini tekshirmaydi** (faqat mavjudligi va
   bloklanmaganini). API orqali ulushi 0 bo'lgan o'qituvchi yuborilsa — u shu o'quvchi bo'yicha
   umrbod bloklanadi. UI bunday qilmaydi, lekin xavfli nuqta.
7. **6 oy to'lganda adminga Telegram xabarnomasi** — hali qilinmagan
   (`BookSalesService.NotifyAdminsAsync` + `TuitionAccrualService` shabloni tayyor namuna).

---

## 5. DEPLOY

⚠️ **Bugungi ishning hech biri prodda YO'Q.** Migratsiyalar server ishga tushganda avtomatik
qo'llanadi (`Program.cs` → `db.Database.Migrate()`), ya'ni **deploy qilish kerak**.

Yangi migratsiyalar (shu tartibda qo'llanadi):
1. `AddGroupTeacherHistory` — guruhning o'qituvchi tarixi + eski guruhlar uchun backfill
2. `RetentionBonusSystem` — bonus jadvallari, `Student`/`CenterMeta` maydonlari
3. `RetentionPerCourse` — `Award` ga fan, yangi unikal indeks, `RetentionBonusTracks`

Deploy bo'yicha: `/deploy` skill (docker compose, .env, Cloudflare Tunnel, backup/restore).

**Deploydan keyin tekshirish:**
```sql
-- Backfill ishladimi (har o'qituvchisi bor guruh uchun bitta ochiq qator)
SELECT COUNT(*) FROM "GroupTeacherAssignments" WHERE "ToDate" IS NULL;
-- Sozlamalar 0 bo'lib qolmaganini tekshirish (6 va 2 bo'lishi kerak)
SELECT "RetentionMonthsRequired", "RetentionMaxGapMonths" FROM "CenterMeta";
```

---

## 6. Ishlab chiqish muhiti (MUHIM)

Bu mashinada **`dotnet` ham, `node` ham o'rnatilmagan** — hamma narsa Docker orqali:

```bash
cd /Users/me/Documents/git/WunderkindLC

# Backend build (BuildSpa=false SHART — aks holda klient esproj build'i xato beradi)
docker run --rm -v "$PWD":/src -w /src -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e HOME=/tmp \
  -e BuildSpa=false mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -c "dotnet build WunderkindLC.Server -v q --nologo"

# Migratsiya yaratish
docker run --rm -v "$PWD":/src -w /src -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e HOME=/tmp \
  -e BuildSpa=false mcr.microsoft.com/dotnet/sdk:8.0 bash -c \
  "dotnet tool install --global dotnet-ef --version 8.* >/dev/null 2>&1; \
   export PATH=\$PATH:/tmp/.dotnet/tools; dotnet ef migrations add <Nom> \
   --project WunderkindLC.Infrastructure --startup-project WunderkindLC.Server"

# Frontend (node_modules o'rnatilgan)
docker run --rm -v "$PWD/WunderkindLC.Client":/app -w /app node:20-alpine \
  sh -c "./node_modules/.bin/tsc -b --noEmit --force && ./node_modules/.bin/vite build"
```

**Lokal sinov muhiti** (toza baza + haqiqiy API):
```bash
docker run -d --name t-pg -e POSTGRES_PASSWORD=test -e POSTGRES_DB=testdb \
  -e POSTGRES_USER=test -p 55440:5432 postgres:16-alpine
docker run -d --name t-api -p 5090:5090 -v "$PWD":/src -w /src \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e HOME=/tmp -e BuildSpa=false \
  -e ConnectionStrings__Default="Host=host.docker.internal;Port=55440;Database=testdb;Username=test;Password=test" \
  -e Jwt__Key="test-jwt-key-32-belgidan-kam-bolmasin" \
  -e Seed__OwnerLogin="owner" -e Seed__OwnerPassword="Owner12345" \
  -e ASPNETCORE_URLS="http://0.0.0.0:5090" \
  mcr.microsoft.com/dotnet/sdk:8.0 bash -c "dotnet run --project WunderkindLC.Server --no-launch-profile"
```
Login: `POST /api/auth/login` `{"email":"owner","password":"Owner12345"}` → `token`.
Migratsiyalar startupda avtomatik qo'llanadi. Oxirida: `docker rm -f t-api t-pg`.

> DIQQAT: Docker Desktop'da `--network host` macOS'da ishlamaydi — port `-p` bilan chiqariladi,
> konteynerdan hostga esa `host.docker.internal` orqali murojaat qilinadi.
