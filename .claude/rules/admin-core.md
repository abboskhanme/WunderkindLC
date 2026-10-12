---
description: Admin ma'lumotnomalari — xonalar, kurslar katalogi, davomat sabablari va kunlik Telegram JSON zaxirasi (o'chirish darvozalari, noyoblik, maxfiy maydonlar).
paths:
  - "WunderkindLC.Server/Controllers/RoomsController.cs"
  - "WunderkindLC.Server/Controllers/SubjectsController.cs"
  - "WunderkindLC.Server/Controllers/SettingsController.cs"
  - "WunderkindLC.Application/Services/CatalogGuards.cs"
  - "WunderkindLC.Application/Services/BackupService.cs"
  - "WunderkindLC.Application/Services/SecretScrub.cs"
---

# Admin ma'lumotnomalari qoidalari

Audit (2026-10-07) natijasida yozilgan. Qoidalarning sof qismi — `CatalogGuards.cs`
(`CourseCatalog` · `RoomRules` · `AbsenceReasonRules`) va `SecretScrub.cs`, testlari
`AuditFixT10Tests`. Migratsiya KERAK EMAS.

## 1. Xonalar (`RoomsController`)

- ⚠️ **Tahrir faollikni O'ZGARTIRMAYDI**, agar `isActive` yuborilmasa (`UpdateRoomRequest.IsActive` —
  `bool?`, `null` = tegilmaydi). Ilgari maydon majburiy `bool` edi, klient uni yubormasdi va sig'imni
  tuzatish xonani jimgina nofaol qilib, ro'yxatdan, guruh formasidan va bandlik hisobotidan
  yo'qotardi. Klient ham endi joriy qiymatni yuboradi — ikki tomonlama himoya.
- **O'chirish = nofaol qilish** (yozuv qoladi — o'tgan jadval unga tayanadi). ⚠️ ARXIVLANMAGAN
  guruhi bor xona o'chirilmaydi (400, soni bilan): aks holda guruh "ko'rinmas" xonada qolardi —
  guruh formasida xona bo'sh ko'rinib, keyingi saqlashda tushib qolardi. Tahrirda `isActive: false`
  ham AYNAN shu darvozadan o'tadi.
- Nom noyobligi faqat FAOL xonalar orasida.
- Har yaratish/tahrir/o'chirish — audit `Room` ("Guruhlar" bo'limi).

## 2. Kurslar katalogi (`SubjectsController`)

- ⚠️ **O'chirish darvozasi** (`CourseCatalog.CountReferencesAsync`): guruh (ARXIVDAGISI ham),
  berilgan sertifikat, sertifikat andozasi, ushlab turish bonusi (track/award), daraja testi yoki
  uzaytirish yozuvi bog'langan kurs — 400, sanoqlar bilan. Sabab: ilgari kurs jimgina o'char, sinf
  narxlari kaskad bilan yo'qolib o'quvchilarga keyingi oydan ASOSIY narx yozilar, guruhlar kurs
  analitikasidan tushib qolardi (qoida: pul/tarix bor joyda — rad etish, kaskad emas).
- Bog'liqsiz kursni o'chirish **BITTA SaveChanges** ichida: dastur bog'lanishlari (dasturning O'ZI
  qoladi), sinf narxlari, o'qituvchilarning `SubjectIds` dagi id. Ilgari dasturlar alohida
  `ExecuteDeleteAsync` bilan oldinroq o'chirilar va keyingi xatoda qaytmasdi.
- ⚠️ **Kurs nomi NOYOB** — kalit `CourseCatalog.NameKey`: chetdagi/ichki bo'shliq, registr va
  apostrof turlari (`'` `ʻ` `’` ...) hisobga olinmaydi. Saqlashda nom `CleanName` qilinadi.
  Tahrirda nom (kalit bo'yicha) o'zgarmagan bo'lsa tekshirilmaydi — qoidadan OLDIN paydo bo'lgan
  takror nomli kursning narxini tahrirlash bloklanmaydi. Bazada unikal indeks YO'Q (eski takrorlar
  migratsiyani yiqitardi) — qoida ilova darajasida.
- Qayta nomlashda lidlarning `InterestSubject` i (NOM saqlaydi) **o'sha tranzaksiyada** ko'chiriladi
  (`RelabelLeadsAsync`, kuzatiladigan o'zgarish — `Lead` keshi o'zi yangilanadi). ⚠️ Eski nom boshqa
  kursda ham bo'lsa (eski takror) — ko'chirilmaydi: o'sha kursning lidlari "o'g'irlanmasin".

## 3. Davomat sabablari (`PUT settings/absence-reasons`)

Ro'yxat to'liq yuboriladi, server JOYIDA yangilaydi (upsert — id'lar saqlanadi). ⚠️ Jurnalda
(`JournalEntry.ReasonId`) ISHLATILGAN sababni olib tashlash ham, uning «kech keldi» (`IsLate`)
belgisini almashtirish ham **rad etiladi** (400, nechta yozuvda ishlatilgani bilan). Nomini va
qisqa nomini o'zgartirish — erkin.

**Nega yumshoq o'chirish emas:** u model o'zgarishi va sababni o'qiydigan ~10 joyni (jurnal, davomat,
reyting, portal, AI) qayta ko'rib chiqishni talab qilardi; rad etish esa o'sha natijani (o'tgan
davomat o'zgarmaydi) migratsiyasiz beradi. Keraksiz sabab — nomini o'zgartirib qo'yiladi.

Audit: `AbsenceReason` — nima qo'shildi/o'chdi/qayta nomlandi ("Sozlamalar" bo'limi).

## 4. Kunlik Telegram JSON zaxirasi (`BackupService`)

- **Bu TO'LIQ tiklash nusxasi EMAS** (u pg_dump, DEPLOY.md §6.3) — "ma'lumot nusxasi". Fayl
  Telegram chatiga ketadi, shuning uchun:
- ⚠️ **MAXFIY maydonlar HAR jadvaldan nom bo'yicha olib tashlanadi** (`SecretScrub` —
  `JsonSerializerOptions` modifikatori): `…Token`, `…Password…`, `Secret`, `ApiKey`, `PasswordHash`,
  `TokenHash`, `CodeHash`, `FcmVapidKey`, kamera `RtspUrl` (ichida login/parol), va `byte[]` ustunlar
  (biometrik vektor). Nom bo'yicha ATAYIN — yangi `…Token` maydon qo'shilsa o'z-o'zidan tushmaydi.
  Ilgari `CenterMeta` TO'LIQ (`InstagramCapiToken` bilan) va arxiv suratlari xom (o'chirilgan
  xodimning `PasswordHash` / ochiq `InitialPassword` i) ketardi.
- ⚠️ **Arxiv suratlarining ICHI ham tozalanadi** (`ArchivedRecord.Json` — matn ichidagi JSON).
- Biznes jadvallari to'liq (kitob, shartnoma, qo'ng'iroq, bog'lanish, topshiriq, vakansiya, KPI,
  chegirma, bonus ...). Texnik jurnallar/keshlar va biometrik/sessiya jadvallari ATAYIN kirmaydi va
  ro'yxati faylning o'zida — `excluded` (ilgari "BARCHA ma'lumotlar" deb yozilib, ko'p jadval yo'q edi).
  2026-10-12: `messageDeliveries` (Telegram outbox jurnali — katta, 180 kun) KIRMAYDI; `messageAttachments`
  (faqat metama'lumot) va `studentAchievements` KIRADI.
- ⚠️ **Fayl GZIP — `wunderkindlc_backup_<vaqt>.json.gz` (2026-10-08).** JSON to'g'ridan-to'g'ri siqilgan
  oqimga yoziladi (`BuildGzipAsync`) — xom JSON bufer xotirada umuman turmaydi, hajm ~10 marta kichik.
  Ochish: 7-Zip / WinRAR yoki `gunzip` (ichida o'sha-o'sha JSON). Ilgari ~90 jadval bitta xom `byte[]` ga
  yig'ilar, 49 MB dan oshsa ikkinchi nusxa birinchisi hali xotirada turganda qurilardi (1 GB serverda
  cho'qqi xotira ikki baravar). `BuildJsonAsync` (siqilmagan) testlar/tekshiruv uchun qoladi.
- Siqilgan fayl ham 49 MB dan oshsa — ikkinchi urinishda `HeavyTables` chiqariladi (caption'da aytiladi;
  birinchi bufer OLDIN qo'yib yuboriladi): butun zaxira yuborilmay qolgandan ko'ra asosiylari ketgani yaxshi.
  ⚠️ `financeTransactions`, `monthlyCharges`, `journalEntries` ATAYIN og'ir ro'yxatda YO'Q — ular asosiy (pul,
  davomat); qo'ng'iroqlar (`calls`, `ctiCallRecords`) va lid tarixi (`leadEvents`) esa ikkilamchi.
- Jadval (`BackupSchedulerService`): restartdan keyin TAKRORLANMAYDI — xotiradagi belgidan tashqari
  `CenterMeta.TelegramBackupLastSentAt` ham tekshiriladi (bugun belgilangan vaqtdan KEYIN
  yuborilgan bo'lsa — o'tkaziladi; ertalabki qo'lda yuborish kechki jadvalni bekor qilmaydi).

⚠️ Yangi jadval qo'shsangiz — `BuildJsonAsync` ga ham qo'shing (yoki `ExcludedTables` ga sababini
o'ylab). Maxfiy maydonni nomi bilan aniq qiling (`…Token`, `…Password`) — shunda u avtomatik tushadi.
