---
description: O'quvchi izohlari (profildagi erkin eslatmalar) va ular yig'iladigan "Izohlarga javoblar" sahifasi.
paths:
  - "WunderkindLC.Application/Services/StudentNoteService.cs"
  - "WunderkindLC.Client/src/components/students/StudentNotesThread.tsx"
  - "WunderkindLC.Client/src/components/ui/MonthDayStrip.tsx"
  - "WunderkindLC.Client/src/lib/month.ts"
  - "WunderkindLC.Client/src/pages/admin/students/notes/**"
---

# O'quvchi izohlari va "Izohlarga javoblar"

Izoh — xodim o'quvchi profiliga yozadigan ERKIN eslatma (ota-ona bilan suhbat, to'lov kelishuvi,
sog'lig'i). Entity `StudentNote` (migratsiya `AddStudentNotes`). **Migratsiya KERAK EMAS** —
"Izohlarga javoblar" sahifasi mavjud jadvaldan hisoblanadi.

## 1. Izohning O'ZI — TARIX, ustiga yozilmaydi

Har yozuv o'z muallifi (`AuthorId`/`AuthorName`) va vaqti bilan qoladi. Tahrirlash MUALLIF va
VAQTNI o'zgartirmaydi, faqat `EditedAt` yoziladi va ro'yxatda "(tahrirlangan)" bo'lib ko'rinadi.

⚠️ Tahrir/o'chirish — faqat **muallifi yoki superadmin**. Qoida SERVERDA
(`StudentsController.EditNote`/`DeleteNote`), klientga `canEdit`/`canDelete` bayroqlari sifatida
keladi — ya'ni shart frontendda TAKRORLANMAYDI.

O'quvchi o'chirilsa izohlari ham o'chadi (`StudentsController.Delete` → `RemoveRange`).

## 2. "IZOHLARGA JAVOBLAR" — kimda izoh bor?

Izoh profil ichida yoziladi, ya'ni "kimga izoh yozilgan" degan savolga javob berish uchun har bir
profilni ochib chiqish kerak edi. **O'quvchilar → Izohlarga javoblar**
(`/admin/students/izohlar`, ruxsat `students`) aynan shu savolga javob beradi:

| Ustun | Nima |
|---|---|
| F.I.Sh | o'quvchi (arxivdagilar ham — chip bilan belgilanadi) |
| Guruhi | FAOL a'zoliklar, **muzlatilganlarsiz** (o'quvchilar ro'yxatidagi qoida bilan bir xil) |
| Izohlar | soni (davr tanlangan bo'lsa — o'sha davrdagilar) |
| Oxirgi izoh | sanasi va matni + kim yozgani |

Qator bosilsa — o'quvchining butun izoh tarixi va **o'sha yerdan qo'shimcha izoh yozish**.

### Sana — OYLIK KALENDAR, aniq KUN bilan

Filtr **`MonthDayStrip`** (bog'lanish navbatidagi bilan AYNAN bir xil komponent,
`components/ui/MonthDayStrip.tsx`): oy strelkalar bilan almashadi, har katakda **o'sha kuni
yozilgan izohlar soni** turadi, katak bosilsa — faqat o'sha kunning izohlari. Yonida ikki chip:
**«Hamma vaqt»** va **«Butun oy»**.

- ⚠️ **"7 kun / 30 kun / 90 kun" kabi tez oraliqlar ATAYIN YO'Q** — bu yerda savol "oxirgi N kun"
  emas, **"falon kuni nima yozilgan"**.
- ⚠️ **Standart holat — «Hamma vaqt»** (bog'lanish navbatidagidan FARQ QILADI, u yerda bugun
  tanlangan): sahifaning asosiy savoli "kimda umuman izoh bor", shuning uchun ochilganda ro'yxat
  to'liq turadi — bugun izoh yozilmagan bo'lsa bo'sh ekran chiqmasin.
- Davr holati **bitta union** (`{kind:'all'|'month'|'day'}`) — "kun + oraliq + tez tugma" kabi
  bir-biriga qarama-qarshi kombinatsiyalar bo'lishi MUMKIN EMAS.
- Oy almashtirilsa tanlangan KUN bekor qilinadi (u boshqa oyga tegishli edi va ko'rinmagan holda
  ro'yxatni filtrlab turardi) — «Butun oy» ga o'tadi.
- Kalendar sonlari **alohida yengil so'rovdan**: `GET /admin/students/notes/days?month=yyyy-MM`
  (`StudentNoteService.DaysAsync`) — aks holda bitta kun tanlanganda kalendar bo'shab qolardi.

**YAGONA MANBA:** ro'yxat `StudentNoteService.OverviewAsync` da (Application, testlangan:
`StudentNoteServiceTests`), yozish/tahrir/o'chirish esa avvalgidek `StudentsController` dagi
`{id}/notes` endpointlarida — sahifa ham AYNAN o'sha endpointlarga boradi.

**KO'RINISH ham bitta komponentda:** `components/students/StudentNotesThread.tsx` — profildagi
"Izohlar" tabi ham, bu sahifadagi oyna ham o'shani ishlatadi (nusxa YO'Q).

## 3. Nozik joylar

- ⚠️ **`to` filtri KUN sifatida beriladi**, server uni `T23:59:59` gacha cho'zadi
  (`StudentNoteService.DayEnd`) — aks holda o'sha kunning o'zi tushib qolardi (audit modulidagi
  bilan bir xil muammo).
- ⚠️ **"Oxirgi izoh" — DAVR ichidagisi**: davr tanlanganda son (nechta) va matn (qaysi biri)
  bir-biriga mos bo'lishi shart, aks holda "iyulda 2 ta izoh" deb turgan qatorda avgustdagi matn
  ko'rinardi.
- Qidiruv **ikki tomonlama**: o'quvchi ISMI (Students jadvalidan) yoki izoh MATNI ichidan.
  `ToLower().Contains` — provayderga bog'liq emas (Npgsql `ILike` SQLite testlarida ishlamasdi).
- Chegara: bir so'rovda ko'pi bilan **500** o'quvchi (`MaxLimit`), sahifa 200 talab boradi va
  qirqilgani ro'yxat ostida ochiq yoziladi.

## 4. RUXSAT — `students`, lekin o'qish DARVOZALANGAN

`GET /api/admin/students/notes/overview` da **metod darajasida**
`[AdminPerm("students.list", "students.notes", ReadRequiresPerm = true)]` (sinf darajasidagi `[AdminPerm("students.list")]`
ustiga).

⚠️ Sabab: bitta o'quvchining izohlari uning profilida ko'rinadi, bu yerda esa BUTUN markazning
izohlari BIR ro'yxatda — ichida to'lov kelishuvi, sog'liq, oilaviy sharoit kabi shaxsiy
eslatmalar bo'ladi. Bunday jamlanma "bo'limlararo o'qish" uchun kerak emas
(`uploads-security.md` dagi bir xil mantiq).

Izoh QO'SHISH — `students:create` (server `PermissionRules.CanWrite`); UI'da ham forma shu bilan
darvozalangan, aks holda faqat ko'rish ruxsati bor xodim tugmani bosib 403 olardi.
