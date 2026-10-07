---
description: Yangi o'quvchini QABUL QILISH cheklisti (retseptsiya) — 5 ta band, avtomatik aniqlash, qo'lda belgi, ro'yxat qamrovi va ruxsat.
paths:
  - "WunderkindLC.Application/Services/StudentIntake.cs"
  - "WunderkindLC.Application/Dtos/StudentIntakeDtos.cs"
  - "WunderkindLC.Domain/IntakeEntities.cs"
  - "WunderkindLC.Server/Controllers/StudentIntakeController.cs"
  - "WunderkindLC.Client/src/lib/intake.ts"
  - "WunderkindLC.Client/src/api/services/studentIntake.ts"
  - "WunderkindLC.Client/src/components/students/Intake*.tsx"
  - "WunderkindLC.Client/src/pages/admin/students/lists/IntakeChecklistPage.tsx"
---

# Qabul cheklisti (yangi o'quvchi) qoidalari

Majlis talabi: *"Retseptsiya/Administrator uchun yangi o'quvchini qabul qilish cheklisti
(ma'lumotlarni to'ldirish, test olish, shartnoma, Telegramga ulash, to'lovni qabul qilish)."*

Uch joyda ko'rinadi, uchalasi ham BITTA manbadan (`StudentIntake`, Application/Services):

| Joy | Nima |
|---|---|
| O'quvchi profili → chap ustun, profil kartasi ostida — **«Qabul cheklisti»** kartasi (`IntakeChecklistCard`) | 5 band, progress, har band uchun "qayerda tuzatiladi" tugmasi; to'liq bo'lsa BITTA qatorga yig'iladi |
| **Future → O'quvchilar → «Qabul cheklisti»** (`/admin/students/qabul`) | yangi o'quvchilar jadvali, "To'liq emas" (standart) / band bo'yicha filtr, katakni bosib qo'lda belgilash |
| O'quvchilar → «Yangi o'quvchilar» | «Qabul» ustuni (`3/5`) + sahifaga havola |

Migratsiya: **bitta yangi jadval** — `StudentIntakeMarks` (faqat QO'LDA belgilar). Backfill YO'Q.

## 1. ENG MUHIM — holat HISOBLANADI, faqat qo'lda belgi SAQLANADI

Har band avval MAVJUD yozuvlardan aniqlanadi. Bazaga faqat xodim qo'ygan **«bajarildi»** belgisi
yoziladi (`StudentIntakeMark`). **Bajarilgan = avtomatik YOKI qo'lda.**

| Band (`key`) | Avtomatik qachon |
|---|---|
| `profile` — Ma'lumotlar to'ldirilgan | telefon (o'quvchi/asosiy ota-ona/ota/ona, kamida 9 raqam) · tug'ilgan sana (haqiqiy sana) · maktab sinfi (`SchoolGrades.Current`, «bitirgan» ham sanaladi) · kamida bitta TIRIK a'zolik ARXIVLANMAGAN guruhda (sinov ham) |
| `levelTest` — Daraja testi olingan | o'quvchiga aylantirilgan LID (`Lead.ConvertedStudentId`) orqali `LevelTestSubmission`, yoki lid kartasidagi `Lead.Level` |
| `contract` — Shartnoma tuzilgan | `Contract` `Target="parent"`, `RecipientKey = StudentId` (`StudentListView` bilan bir xil) |
| `telegram` — Telegramga ulangan | `TelegramRegistration.StudentId` shu o'quvchi |
| `payment` — To'lov qabul qilingan | kamida bitta O'QUV to'lovi: `income` + `tuition` (`DashboardSummary` konvensiyasi). Bekor qilingan to'lov global filtr bilan chiqib ketadi |

⚠️ Shu sabab avtomatik holat **o'z-o'zidan yangilanadi**: to'lov kiritilsa, shartnoma tuzilsa,
ota-ona botga ulansa — cheklist keyingi so'rovda o'zgaradi, hech kim hech narsa "belgilamaydi".

⚠️ **Avtomatik holatni qo'lda O'CHIRIB bo'lmaydi.** Qo'lda belgi faqat "bajarildi" qo'shadi;
uni olib tashlash avtomatik aniqlangan bandni "bajarilmagan" qilmaydi (u yozuvlardan keladi).

### Nima ATAYIN avtomatik SANALMAYDI

- **Guruh testlari (`TestScore`)** — ular oylik/bo'lim imtihonlari. Sinovdagi o'quvchi guruh
  imtihonini yozsa "daraja testi olingan" yolg'on chiqardi. Darsda og'zaki/qog'ozda olingan
  daraja testi — **qo'lda belgi** bilan.
- **Telefon bo'yicha moslash** (lid ↔ o'quvchi, eski shartnoma kaliti `p:<telefon>`) — aka-uka
  bir xil ota-ona raqamida: begona natija/shartnoma "bajarildi" bo'lib qolardi. Faqat ANIQ
  bog'lanish (`ConvertedStudentId`, `RecipientKey = StudentId`).
- **Kitob va boshqa kirimlar** — faqat `tuition`.

## 2. QO'LDA belgi — `PUT /api/admin/student-intake/{studentId}/{item}`

`{ done: true, note }` — qo'yish (izoh bilan) · `{ done: false }` — olib tashlash.
Mantiq — `StudentIntake.ApplyMarkAsync` (SaveChanges QILMAYDI; controller audit qo'shib bitta
saqlash qiladi).

- Har `(StudentId, Item)` uchun **BITTA qator** — unikal indeks. Parallel ikkinchi bosish
  `409` (sabab matni bilan), jim yutilmaydi.
- **Idempotent:** yo'q belgini olib tashlash yoki bir xil izohni qayta yozish — hech narsa
  o'zgarmaydi, tarixga qator TUSHMAYDI.
- Izoh ≤ 500 belgi (`MaxNoteLength`), kim/qachon — SNAPSHOT (`MarkedByName`, `MarkedAt`).
- Noma'lum band kaliti — `400`.

**Audit:** tur `StudentIntake` → `AuditSections` da **`students`** bo'limi, `studentId` bilan
(profilning to'lov/tarix panelida ham ko'rinadi). Amallar: `create` (belgilandi) · `update`
(izoh o'zgardi) · `delete` (belgi olib tashlandi).

## 3. RO'YXAT QAMROVI — kim "yangi o'quvchi"

`GET /api/admin/student-intake?days=60` (`StudentIntake.ListAsync`, `InCohort`):

```
arxivlanmagan  &&  ( TIRIK SINOV a'zoligi bor   ||   qabul sanasi >= bugun − days )
```

- Sinovdagilar oynadan QAT'I NAZAR kiradi — «Yangi o'quvchilar» ro'yxatidagi har qatorga
  «Qabul» ustuni to'lsin (o'sha `trial` ta'rifi).
- ⚠️ **Qabul sanasi = `EnrollmentDate`, bo'sh bo'lsa `CreatedAt`** — shu tartibda. Ommaviy
  import (edutizim) hammani BUGUNGI `CreatedAt` bilan yaratadi; u birinchi tursa butun markaz
  "yangi o'quvchi" bo'lib ko'rinardi.
- `days` 7..365 ga qirqiladi (standart 60). Ko'pi bilan **1000** qator (eng yangilari) —
  qirqilgani `truncated` bilan ochiq aytiladi.
- Saralash: to'liq EMASLAR tepada, keyin eng yangi qabul.

## 4. N+1 YO'Q

`StudentIntake.BuildAsync` har jadvalga BITTA so'rov yuboradi (a'zolik, guruh, lid, test
topshiruvi, shartnoma, Telegram, to'lov, belgi) — o'quvchilar soni qancha bo'lsa ham so'rovlar
soni o'zgarmaydi (`StudentIntakeTests.Sorovlar_soni_oquvchilar_soniga_BOGLIQ_EMAS` qulflaydi).
Yangi band qo'shsangiz — u ham ommaviy so'rov bilan yuklansin, sikl ichida emas.

## 5. RUXSAT — yangi kalit YO'Q

`StudentIntakeController`: `[AdminPerm("students.list", ReadRequiresPerm = true)]`.

| Amal | Kim |
|---|---|
| Ko'rish (karta, ro'yxat) | admin/superadmin · `students.list` sahifasi yoki `students` bo'limi (istalgan amali) |
| Qo'lda belgi (`PUT`) | + **tahrir** amali: `students.list:edit` / `students:edit` / yalang kalit |

- `ReadRequiresPerm` ATAYIN: ro'yxatda butun markazning yangi o'quvchilari, telefonlari va
  "to'lov qildimi / shartnoma bormi" holati — bo'limlararo o'qish uchun kerak emas. Turniket
  operatori (`students.turnstile`) ko'rmaydi.
- Yagona yozish fe'li — `PUT` (= `edit`). POST/DELETE qo'shilmasin: aks holda faqat "qo'shish"
  yoki "o'chirish" ruxsati bor xodim ham belgilay olardi (test qulflaydi).
- Kartadagi tugmalar O'Z ruxsati bilan: «Shartnoma tuzish» — `contracts:create`, «Lid kartasi» —
  `leads.list`, «To'ldirish» — `students.list:edit`. Ruxsat bo'lmasa tugma chizilmaydi (server
  baribir rad etadi).

## 6. MAXFIYLIK

Javobda **fayl manzili YO'Q** — shartnomadan faqat raqam va sana (`uploads-security.md`); SELECT'ga
ham `PdfUrl`/`DocxUrl` tushmaydi. Telegram bot NOMI qaytadi (ommaviy; token EMAS). Test qulflaydi:
`Javobda_fayl_MANZILI_yoq`.

## 7. KLIENT

- Holat klientda QAYTA hisoblanmaydi — `lib/intake.ts` faqat filtr, rang va ustun sarlavhalari.
- Karta: belgi ranglari — **yashil** avtomatik, **indigo** qo'lda, **kulrang doira** bajarilmagan
  (ikki "bajarildi" farqi ko'rinsin: qo'lda belgi — ishonch, yozuv emas).
- «Shartnoma tuzish» kartadan — `IntakeContractModal`: ota-ona andozasi → `POST /contracts/build`
  (Shartnomalar sahifasidagi bilan AYNAN bitta endpoint). Blob javobdagi xato matni alohida
  o'qiladi (`apiErrorMessage` Blob'ni o'qiy olmaydi).
- Xato: modal ichida qizil satr, oyna yopilmaydi, izoh saqlanadi (`error-visibility.md`).
  Ro'yxatda qo'lda belgidan keyin qator SERVER javobi bilan almashtiriladi (optimistik emas).
- Bosh sahifaga hisoblagich QO'SHILMADI: u edutizimning 12 kartochkasi (`HOME_CARDS`) bilan
  aynan bir xil turishi kerak (`docs/EDUTIZIM-PARITY.md`).

## 8. O'quvchi o'chirilsa

`StudentPurge` belgilarni ham o'chiradi (preview'da «Qabul cheklisti belgilari»). Arxivga
ko'chirish belgilarni saqlaydi (o'quvchi qaytsa tarix joyida).
