# O'quvchining MAKTAB SINFI qoidalari

Maqsad: "markazda maktabning nechanchi sinflari o'qiyapti" statistikasi.
Migratsiya: `AddStudentSchoolGrade` (`Students.SchoolGrade`, `Students.SchoolGradeYear`, ikkalasi `int?`).

## 1. `ClassName` BILAN ADASHTIRMANG

`Student.ClassName` — o'quvchining ASOSIY GURUHI yorlig'i (eski maydon, guruh nomi). Maktab sinfi —
alohida `SchoolGrade`. "Sinf" so'zi UI'da faqat maktab sinfini bildiradi.

## 2. Sinf HAR YILI O'ZI OSHADI — `SchoolGrades` (Domain, sof, `SchoolGradesTests`)

Bazada juftlik: `SchoolGrade` (kiritilgan sinf) + `SchoolGradeYear` (o'sha O'QUV YILI, sentabrdan
boshlanadi). Joriy qiymat — `Student.SchoolGradeNow` (NotMapped, har javobda hisoblanadi).

⚠️ **Xom `SchoolGrade` ni KO'RSATMANG va klientga QAYTARIB YUBORMANG** — u eski yildagi qiymat.
Klient tipida (`types/index.ts`) ATAYIN faqat `schoolGradeNow` bor; tahrir payloadi
(`studentFormModel.payloadFromStudent`) JORIY sinfni yuboradi, server uni joriy yilga qayta bog'laydi
(natija o'zgarmaydi). Xom qiymat yuborilsa yillik oshish jimgina yo'qolardi.

- 11-sinfdan keyin — `Graduated = 12` («Maktabni bitirgan»), undan oshmaydi.
- «Maktabgacha» varianti ATAYIN yo'q (`docs/ASSUMPTIONS.md`).

## 3. API — `StudentPayload.SchoolGrade`

`null` = tegilmaydi (eski mijozlar/import buzilmaydi) · `0` = tozalash · 1..12 = o'rnatish.
Boshqa qiymat — 400 (sabab matni bilan). Audit: `StudentProfileSnapshot.SchoolGrade` (joriy sinf).

## 4. Qayerda ko'rinadi

- Qo'shish/tahrirlash: `StudentFormModal` («Boshqa ma'lumotlar» → Markazga kelgan sana · Sinf), profil
  «Tahrirlash» tabi. Tuman/maktab tizimdan butunlay olib tashlangan (`crm-leads.md`).
- Profil kartasi: ism ostida "7-sinf".
- O'quvchilar ro'yxati: «Sinf» filtri (sonlari joriy tab bo'yicha, Holat filtridagi kabi).
- «Hisobotlar → Sinflar kesimi» (`/admin/hisobotlar/sinflar`, `students.list`) — manba
  `GET /admin/students` (yangi endpoint YO'Q), standart qamrov — AKTIV o'quvchilar.

Klient yordamchilari — `lib/schoolGrades.ts` (faqat yorliq/filtr/sanoq; sinfni QAYTA hisoblamaydi).
