---
name: test
description: WunderkindLC testlari — unit testlarni ishga tushirish, yangi test yozish va butun loyihani tester sifatida audit qilish (xato/kamchilik izlash). Test yozish, "testlarni ishga tushir", "xatolarni top", regressiya tekshiruvi kerak bo'lganda ishlating.
---

# Testlar (WunderkindLC)

## 1. Ishga tushirish

```bash
dotnet test WunderkindLC.Tests/WunderkindLC.Tests.csproj          # hammasi
dotnet test WunderkindLC.Tests/WunderkindLC.Tests.csproj \
  --filter "FullyQualifiedName~PhoneUtil"                          # bitta guruh
dotnet test WunderkindLC.Tests/WunderkindLC.Tests.csproj \
  --collect:"XPlat Code Coverage"                                  # qamrov (coverlet)
```

> Backendni alohida qurish kerak bo'lsa DOIM `-p:BuildSpa=false` bilan:
> `dotnet build WunderkindLC.Server/WunderkindLC.Server.csproj -p:BuildSpa=false`
> (aks holda `npm`/esproj talab qilinadi). Test loyihasi Server'ga havola QILMAYDI —
> shuning uchun `dotnet test` uchun bu bayroq kerak emas.

## 2. Test loyihasi

`WunderkindLC.Tests/` — xUnit **v2**, `net8.0`. Havolalar: Domain · Application · Infrastructure
(**Server ATAYIN yo'q** — u esproj/SPA ga bog'lanib test qurishni buzadi; controller darajasidagi
test kerak bo'lsa `WebApplicationFactory` bilan alohida loyiha ochiladi).

**Konvensiyalar:**
- Sof `Assert.*` — **FluentAssertions ISHLATILMAYDI** (yangi versiyalari tijorat litsenziyasi talab qiladi).
- Kod izohlari va test nomlari mazmuni — **o'zbek tilida** (repo uslubi).
- Baza kerak bo'lsa `TestDb` (`WunderkindLC.Tests/TestDb.cs`):
  ```csharp
  using var db = TestDb.Sqlite();      // ASOSIY: haqiqiy relyatsion baza (unique indeks/FK ishlaydi)
  db.Context.Students.Add(...);
  ```
  `TestDb.InMemory()` — faqat zaxira (SQLite ishlamay qolsa). Har chaqiriq izolyatsiyalangan baza beradi;
  `using` shart — SQLite in-memory bazasi ulanish yopilishi bilan yo'qoladi.
- Vaqt: kodda `DateTime.Now` emas, `AppClock.Now` (Asia/Tashkent, UTC+5). Testda ham shundan boshlang —
  UTC bilan solishtirish 5 soatlik xatoga olib keladi.

## 2b. Ma'lum xatolar — `Skip` konvensiyasi

Auditda topilgan, lekin HALI TUZATILMAGAN xatolar test bilan **hujjatlashtirilgan**. Naqsh — juft test:

- `[Fact(Skip = "XATO (fayl:qator): ...")]` — **kutilgan (to'g'ri)** xulq. Tuzatilgach `Skip` olib tashlanadi.
- yonida yashil test — **hozirgi (noto'g'ri)** xulqni muzlatadi, ya'ni xatoni isbotlaydi va
  regressiyani ushlaydi. Tuzatilganda bu test **o'chiriladi** (izohda shunday yozilgan).

Frontendda xuddi shu narsa `it.skip(...)` bilan.

⚠️ Shuning uchun `Skip` sonining kamayishi — yaxshi belgi (xato tuzatildi), oshishi — yangi
xato topildi degani. Test to'plami DOIM yashil bo'lishi kerak.

## 3. Yangi test yozganda

1. Avval **`.claude/rules/*.md`** ni o'qing — kutilayotgan xulqning RASMIY manbai
   (`billing.md`, `journal.md`, `tests.md`, `exercise.md`, `messaging.md`, `books.md`,
   `crm-leads.md`, `career.md`). Kod bilan qoida orasidagi farq — bu **xato**, testni qoidaga qarab yozing.
2. Sof (static/DB'siz) mantiqni birinchi qamrang — eng arzon va eng foydali:
   `PhoneUtil`, `UploadGuard`, `TelegramInitData`, `MessageTokenizer`, `JournalPolicy`,
   `TeacherSalaryCalc`, `CareerService` bosqichlari, `AuditService.Money`.
3. Chegara holatlarini yozing: bo'sh/null, 0 va manfiy, oyning oxiri, bir xil ball (rank),
   uzunlik mos kelmasligi, mintaqa chegarasi (23:00–01:00), juda uzun matn.

## 4. Tester rejimi (to'liq audit)

Katta audit so'ralganda ish **PM uslubida agentlarga bo'linadi** (qarang: `pm-delegate` skill).
Sinalgan bo'linish — hududlar bir-birining fayllariga tegmaydi:

| Agent | Hudud |
|---|---|
| Moliya | `TuitionService`, `MembershipLifecycle`, `StudentLedger`, `GroupBalanceService`, `TeacherSalaryCalc`, `SalaryLedger`, `RetentionBonusService`, `CourseFinanceReport`, `PaymentIntake`, `CashierReport` |
| O'quv jarayoni | `JournalService/Policy`, `GradingService`, `TestResultService`, `OnlineTest*`, `LevelTestService`, `CurriculumForecast`, `RatingService` |
| Bot / xabar / xavfsizlik | `Telegram*`, `Career*`, `TelegramInitData`, `AutoMessage*`, `MessageTokenizer`, `PhoneUtil`, `UploadGuard`, `AppSecrets`, `AdminPermAttribute`, ochiq (`Public*`) controllerlar |
| Frontend | `src/lib/utils.ts`, `src/lib/permissions.ts`, `src/api/services/**`, `careerLabels.ts` |

**Qoidalar:** audit agentlari FAQAT o'qiydi — production kodini o'zgartirmaydi, topilmani
`fayl:qator — muammo — qanday sindiradi — tuzatish` ko'rinishida qaytaradi. Tuzatish qarori odamda.
Test yozuvchi agentlarga har biriga ALOHIDA test fayllari beriladi (parallel konflikt bo'lmasin).

## 5. Frontend testlari (Vitest)

Bu mashinada `node`/`npm` PATH da **yo'q** — hammasi Docker orqali (`WunderkindLC.Client` ichidan):
```bash
docker run --rm -v "$PWD":/w -w /w node:24-slim npx vitest run     # testlar
docker run --rm -v "$PWD":/w -w /w node:24-slim npm run build      # tsc -b + vite build
docker run --rm -v "$PWD":/w -w /w node:24-slim npx eslint .       # lint
```

- Konfiguratsiya **`vitest.config.ts`** da — `vite.config.ts` ni QAYTA ISHLATIB BO'LMAYDI:
  u `command === 'serve'` da `dotnet dev-certs` chaqiradi va konteynerda yiqiladi.
- Testlar `src/**/__tests__/*.test.ts` da; faqat **sof funksiyalar** qamralgan
  (komponent render testlari ataylab yo'q).
- DOM kerak bo'lsa fayl boshiga `// @vitest-environment jsdom`.

**Tuzoqlar (tekshirilgan):** `Blob.text()` BOM'ni yeb qo'yadi — BOM'ni `arrayBuffer()` +
`TextDecoder(..., {ignoreBOM:true})` bilan tekshiring. jsdom `URL.createObjectURL` ni
implementatsiya qilmaydi — `vi.spyOn` yiqiladi, stub'ni qo'lda o'rnatib keyin tozalang.

## 6. Flutter ilovalari

O'qituvchi/o'quvchi mobil ilovalari **alohida repolarda** (bu repoda yo'q) — bu skill
ularni qamrab olmaydi. U yerda `flutter test`.
