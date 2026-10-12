# O'zgarishlar tarixi (audit) qoidalari

"Kim, qachon, nimani o'zgartirdi". Yozuv — `AuditLog` entity, yozuvchi — `AuditService.Record`,
o'quvchi — `AuditController` (`/api/admin/audit`).

## 1. Yozish — `audit.Record(...)`

```csharp
audit.Record(entityType, entityId, action, summary,
             before: null, after: null, studentId: null, teacherId: null);
```

- **SaveChanges QILMAYDI** — yozuv chaqiruvchining tranzaksiyasiga qo'shiladi va uning
  `SaveChangesAsync`i bilan birga saqlanadi. Demak `Record` **saqlashdan OLDIN** chaqiriladi.
  ⚠️ Servis o'zi saqlab yuboradigan joylarda (masalan `BookSalesService.ApproveAsync`) audit
  yozuvi uchun **alohida `SaveChangesAsync`** kerak — aks holda u bazaga umuman tushmaydi.
- `action` faqat to'rt qiymatdan: `create` | `update` | `delete` | `complete-and-transfer`.
- `summary` — o'zbekcha, TO'LIQ jumla: tarix ro'yxatida foydalanuvchi FAQAT shuni o'qiydi.
- `before`/`after` — snapshot obyektlari (JSON'ga seriyalanadi). UI ularni "eski → yangi"
  ko'rinishida faqat `AuditHistoryList.fieldLabels` da yorlig'i BOR maydonlar uchun chizadi.
- **SOZLAMA o'zgarishi** — `SettingsAudit.Record<TEntity>(audit, db.ChangeTracker, "nima", ...)`
  (yagona yordamchi; o'zgargan maydonlarni ChangeTracker'dan oladi, shuning uchun SaveChanges'dan
  OLDIN). `SettingsController` — faqat maydon NOMLARI (eskicha); pul/limit sozlamalari (ushlab turish
  bonusi, AI tekshiruv limitlari, landing aloqa ma'lumotlari) — `withValues: true` + o'zbekcha
  `labels`: tarixda "Standart summa: 100 000 → 150 000". Maxfiy nomli maydon bu rejimda ham faqat
  NOMI bilan. Ilgari bu sozlamalar `SettingsController` dan tashqarida bo'lgani uchun UMUMAN yozilmasdi.
- **Maxfiy qiymat yozilmaydi:** parol, token, API kalit — hech qachon. `/uploads/...` MANZILI ham
  yozilmasin (tarixni ko'rgan har kim faylni abadiy olib qolardi — `uploads-security.md`).

## 2. BO'LIMLARGA AJRATISH — `AuditSections` (yagona manba)

Foydalanuvchi texnik turni (`EntityType`) emas, **bo'limni** ko'radi. Xarita —
`Application/Services/AuditSections.cs` (sof funksiyalar, `AuditSectionsTests` bilan qoplangan).
Bo'lim kalitlari `adminPermissions` kalitlari bilan bir xil (`students`, `classes`, `finance`, ...);
xaritada yo'q tur `other` ("Boshqa") bo'limiga tushadi — ya'ni **hech qachon yo'qolmaydi**.

⚠️ Audit bo'limlari ATAYIN **BO'LIM darajasida** qoladi — ruxsatlarda sahifa kalitlari paydo
bo'lgan bo'lsa ham (`students.turnstile` va h.k., `.claude/rules/permissions.md`). Sabab: tarixni
o'qiyotgan odam "qaysi bo'limda nima o'zgardi" deb qaraydi, `audit` ruxsati esa baribir BUTUN
tarixni ochadi — chiplarni sahifalarga bo'lish ro'yxatni maydalab, foyda bermasdi.

⚠️ **TUR NOMLARI ALDAMCHI** — xarita nom bo'yicha emas, "qaysi bo'lim sahifasida ko'rinishi kerak"
bo'yicha tuzilgan:

| EntityType | Aslida nima yoziladi | Bo'lim |
|---|---|---|
| `StudentDiscount` | chegirma **+** arxivlash/tiklash **+** login bloklash **+** qo'lda oylik tahriri | `students` |
| `ClassFee` | guruh oyligi **+** guruh yaratish/tahrir/arxiv | `classes` |
| `TeacherSalary` | maosh to'lovi **+** o'qituvchi yozuvining o'zi (yaratish/tahrir/arxiv/rasm) | `teachers` |
| `Membership` | a'zolik hodisalari, `EntityId = "{groupId}:{studentId}"` | `classes` |
| `substitute_teacher` | o'rinbosar biriktirish/bekor qilish, `EntityId = "{groupId}:{assignmentId}"` | `teachers` |

**Yangi `audit.Record` qo'shsangiz** — `EntityType`ni `AuditSections.ByEntityType` ga ham qo'shing,
aks holda yozuv "Boshqa"da qolib, bo'lim filtrida topilmaydi. `AuditSectionsTests` xaritadagi har bir
tur `All` ro'yxatidagi bo'limga tushishini tekshiradi.

⚠️ `substitute_teacher` bo'limi **`teachers`** ("Guruhlar" EMAS): amal guruh tarkibiga emas, KIM
dars o'tishiga va kimning MAOSHIGA tegishli (o'rinbosarga haq, asosiy o'qituvchidan ushlanma).
`EntityId` esa `Membership` naqshida prefiksli — aks holda yozuv guruh sahifasining "Tarix"
tabida hech qachon ko'rinmasdi (`groupId` filtri `EntityId.StartsWith(groupId + ":")` deb
qidiradi). Batafsil: `.claude/rules/substitute-teachers.md` §6.

Tarixiy izoh: kurs narxi ilgari `ClassFee` deb yozilar va "Guruhlar"ga tushardi; endi `Course`
(bo'lim: Kurslar). **Eski qatorlar qayta yozilmaydi** — ular o'sha joyda qoladi.

**«Xabarlar» bo'limi (`messages`, 2026-10-12, CRM upgrade P2.9)** — `Sozlamalar` dan OLDIN:

| EntityType | Nima |
|---|---|
| `Broadcast` | Telegram xabar (kampaniya) yuborildi: auditoriya + xabarlar soni + matn boshi (120 belgi); bekor qilindi (nechta) |
| `AutoMessageRule` | avto-xabar qoidasi yaratildi (standart to'plam ham) / tahrirlandi («yoqildi»/«o'chirildi») / o'chirildi |
| `MessageTemplate` | qo'lda yoziladigan shablon qo'shildi / tahrirlandi / o'chirildi |
| `StudentAchievement` | **O'quvchilar** bo'limida (`studentId` bilan): yutuq qo'shildi / tahrirlandi / o'chirildi / tabrik yuborildi |

Telegram ulanishining qarindoshligi o'zgarishi — `Student` turi («Telegram ulanishi turi o'zgardi: … — Otasi → Onasi»).
⚠️ Kampaniya qatorida chat id, telefon va fayl manzili YO'Q.

## 3. RUXSAT — `audit`

`AuditController` da `[AdminPerm("audit", ReadRequiresPerm = true)]`.

- **admin / superadmin** — har doim ko'radi (odatdagi bypass).
- **xodim (staff)** — faqat "Xodimlar va rollar" da `audit` ruxsati berilgan bo'lsa.
- **o'qituvchi/o'quvchi/ota-ona** — hech qachon.

⚠️ `ReadRequiresPerm = true` ATAYIN: `AdminPermAttribute` da GET odatda har qanday xodimga ochiq
(bo'limlararo o'qish uchun), bu yerda esa javobda to'lov summalari, maosh, chegirma va ruxsat
o'zgarishlari bor.

⚠️ `audit` = **BUTUN tarix**. Bo'limlarga bo'lib berish (masalan "faqat o'z bo'limlaringni ko'r")
ATAYIN qilinmagan — bitta tushunarli kalit. Kimga berilayotgani shu sababdan o'ylab tanlanadi.

Ilgari bu yerda yalang `[Authorize(Roles = "admin,superadmin")]` turardi — xodim o'quvchi/guruh
sahifasidagi "Tarix" bo'limini umuman ocha olmasdi.

## 3.5. QAMROV — nima yoziladi

⚠️ Bo'lim chipi bo'lgani yozuv BOR degani EMAS. Bo'shliq ikki xil bo'ladi va ikkalasi ham
foydalanuvchiga BIR XIL ko'rinadi ("tarixda yo'q"):

1. `audit.Record` umuman chaqirilmagan (endpoint jim);
2. chaqirilgan, lekin **shartli** — shart bajarilmasa hech narsa yozilmaydi.

(2) ni topish qiyin, shuning uchun yangi endpoint yozganda savol: *"bu amaldan keyin tarixda
BITTA qator paydo bo'ladimi — har doim?"*

Ilgari shu tarzda tushib qolgan va 2026-08-05 da to'ldirilgan joylar:

| Bo'lim | Amal | Nima bo'lgan edi |
|---|---|---|
| Guruhlar | **A'zo qo'shish** (`AddMember`) | umuman yozilmasdi — chiqarish/muzlatish/ko'chirish yozilar, qo'shish yo'q edi (o'quvchi tarixda "birdan paydo bo'lardi") |
| Guruhlar | **Aktivlashtirish** (`ActivateMember`) | faqat NODIR yon ta'sir (avans ko'chishi) yozilardi; pul hisobi boshlanadigan asosiy amalning o'zi yo'q edi |
| Guruhlar | O'qituvchi / kurs almashuvi | faqat `GroupSnapshot` ichida GUID sifatida — ro'yxatda "Guruh tahrirlandi" dan boshqa hech narsa ko'rinmasdi |
| O'qituvchilar | **Qo'shish** | faqat toifa berilgan bo'lsa "Toifa belgilandi" — toifasiz qo'shilgan o'qituvchi tarixda YO'Q edi |
| O'qituvchilar | **Tahrirlash** | faqat toifa va maosh boshlanish oyi; ism, telefon, **MAOSH summasi/foizi**, fanlar, ruxsatlar o'zgarishi ko'rinmasdi |
| O'qituvchilar | **O'chirish** | umuman yozilmasdi |
| O'qituvchilar | Parolni tiklash | umuman yozilmasdi |

2026-10-07 auditida to'ldirilganlar (operatsion bo'limlar):

| Bo'lim | Amal | Nima bo'lgan edi |
|---|---|---|
| Shartnomalar | **Tuzish (yuklab olish)** va **Telegram orqali yuborish** | umuman yozilmasdi — har shartnoma uchun bitta qator (fayl MANZILISIZ), yuborilmagani ham |
| O'quvchilar | **Taklif/shikoyat** hal qilindi / qayta ochildi (`Feedback`) | yozilmasdi; qayta ochish yo'li umuman yo'q edi |
| Sozlamalar | **Topshiriqlar kunlik eslatmasi** (`CenterMeta`) | `SettingsController` dan tashqarida — yozilmasdi |
| Kitoblar sotuvi | Olib ketilmagan buyurtmani **bekor qilish** | yangi amal (books.md §2.6) |

2026-10-07 auditida to'ldirilganlar (hisobotni o'zgartiradigan KATALOGLAR va sozlamalar):

| Tur | Amal | Bo'lim |
|---|---|---|
| `ActionReason` | sabab yaratish/tahrir/o'chirish — ⚠️ `OutOfControl` KPI jarimasi va ketish hisobotida JONLI o'qiladi | settings |
| `AbsenceReason` | davomat sabablari ro'yxati (nima qo'shildi/o'chdi/qayta nomlandi) | settings |
| `Room` | xona yaratish/tahrir/o'chirish | classes |
| `Branch` | filial yaratish/tahrir/o'chirish | settings |
| `CenterMeta` | ushlab turish bonusi, AI tekshiruv, landing sozlamalari (`SettingsAudit`) | settings |
| `StudentAiAccess` | o'quvchiga AI limit/premium/blok (`studentId` bilan — profil tarixida ham) | students |
| `CertificateTemplate` | sertifikat andozasi qo'shish/o'chirish (HTML matn yozilmaydi) | settings |
| `Staff` (delete) | o'chirishda bo'shatilgan havolalar (lid, kassa, topshiriq, KPI, Telegram; CTI 2026-10 gacha) shu qatorda | staff |

`StudentBall` xaritada qoldi, lekin hozir uni YOZADIGAN endpoint yo'q (eski qatorlar uchun).

2026-10-07 auditida to'ldirilganlar (o'quvchilar, arxiv, guruh a'zoligi):

| Bo'lim | Amal | Nima bo'lgan edi |
|---|---|---|
| Sozlamalar → Arxiv | **Tiklash** va **butunlay o'chirish** (`ArchiveController`) | umuman yozilmasdi. Endi tur — tiklanayotgan entity'ning O'Z turi (`Lead`, `Student`, `TeacherSalary`, `Staff`, `Group`, `FinanceTransaction`), ya'ni yozuv o'sha bo'limda ko'rinadi |
| O'quvchilar | Arxiv tabidan **o'chirish** (`DELETE students/{id}`) | iz qoldirmasdi |
| O'quvchilar | **Telegram ulanishi uzildi** (raqam o'zgarganda avtomatik yoki profildan qo'lda) | yangi amal — `Student` turi |
| Guruhlar | O'quvchini **arxivlash** → har guruhdan chiqish (`Membership`, «Guruhdan chiqarildi … — o'quvchi arxivlandi») | a'zolik umuman yopilmasdi |
| Guruhlar | O'quvchi YARATILGANDA/IMPORTDA guruhga qo'shish (`Membership`) | yaratish yo'li izsiz qo'shardi |

2026-10-07 auditida to'ldirilganlar (guruhlar):

| Bo'lim | Amal | Nima bo'lgan edi |
|---|---|---|
| Guruhlar | **«Tugatish (sertifikat bilan)»** | yig'ma qator oxirgi `SaveChanges`dan KEYIN yozilib bazaga TUSHMASDI, a'zo qatorlari umuman yo'q edi — endi har a'zoga `Membership` qatori (tugatdi / kursni tugatmay chiqdi / yangi guruhga qo'shildi) + yig'ma qator, hammasi bitta tranzaksiyada |
| Guruhlar | Guruhni o'chirish | endi nima o'chgani (jurnal, baholar, testlar, o'rinbosarlar, bekor qilingan chegirmalar) sanoq bilan yoziladi |
| Guruhlar | Qayta nomlash | «Guruh nomi o'zgardi: A → B» + nechta chat xabari/yorliq ko'chgani |
| Guruhlar | **Uzaytirishni bekor qilish** (yangi) | `StudentExtension`, `delete`, `"{groupId}:{studentId}"` |
| O'qituvchilar | O'rinbosar tayinlovi guruh yopilganda/o'qituvchi almashganda qisqardi/bo'lindi | `substitute_teacher` (`substitute-teachers.md` §9) |
| O'quvchilar | Chegirma guruh almashganda ko'chdi / ko'chmadi (boshqa fan) | `StudentDiscount` |

**QAMROVDA ATAYIN YO'Q:** `POST {id}/ai-analysis` (guruh/o'qituvchi AI tahlili) — ma'lumot
o'zgartirmaydi, faqat tahlil yaratadi.

Snapshot yordamchilari: `AuditService.GroupSnapshot` · `TeacherSnapshot` · `StudentProfileSnapshot`
· `Snapshot(FinanceTransaction)`. Snapshot maydonining ekranda o'qiladigan bo'lishi uchun
`AuditHistoryList.fieldLabels` ga yorliq qo'shish SHART — yorlig'i yo'q maydon (masalan `TeacherId`)
faqat o'zgarish ANIQLASH uchun ishlaydi, chizilmaydi.

## 4. Qayerda ko'rinadi

| Joy | Filtr | Fayl |
|---|---|---|
| **Sozlamalar → O'zgarishlar tarixi** (umumiy, bo'limlarga ajratilgan) | `section`, `from/to`, `action`, `actor`, `q` | `pages/admin/settings/AuditLogPage.tsx`, marshrut `/admin/settings/history` |
| Guruh sahifasi → "Tarix" tabi | `groupId` (guruh + a'zolik hodisalari) | `ClassDetailPage.tsx` |
| O'qituvchi sahifasi / maosh modali | `teacherId` | `TeacherDetailPage.tsx`, `TeacherSalaryDetailModal.tsx` |
| O'quvchi to'lov tarixi paneli | `studentId` | `PaymentHistoryPanel.tsx` |
| Moliya → "Tarix" tugmasi va qator ikonkalari | `{}` / `entityType+entityId` / `teacherId` | `FinancePage.tsx` |

Hammasi bitta endpointdan (`GET /api/admin/audit`) — per-entity marshrut YO'Q. UI'da hammasi
`can('audit','view')` bilan darvozalangan (admin uchun `can` har doim true).

Nav: "Sozlamalar" guruhining O'ZIDA `perm` YO'Q — u bolalarga ko'chirilgan, chunki "O'zgarishlar
tarixi" boshqa ruxsat (`audit`) bilan ishlaydi. Sidebar bolalari qolmagan guruhni o'zi yashiradi.

## 5. Chegaralar

- `GET /api/admin/audit` bir so'rovda ko'pi bilan **500** yozuv (`AuditController.MaxLimit`);
  sahifa 100 talab boradi va "Ko'proq" bilan 500 gacha oshiradi.
- `GET /api/admin/audit/sections` — bo'limlar + sanoq + xodimlar ro'yxati. Sanoq **ayni o'sha
  filtrlar** bo'yicha, ya'ni chipdagi son ochilganda chiqadigan son bilan bir xil.
- Qidiruv `Summary` ichidan, `ToLower().Contains` (provayderga bog'liq emas — Npgsql `ILike`
  SQLite testlarida ishlamasdi).
- `to` filtri KUN sifatida beriladi, klient uni `T23:59:59` gacha cho'zadi (aks holda o'sha kunning
  o'zi tushib qolardi).
