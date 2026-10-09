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

⚠️ **Sinf PULGA ta'sir qiladi** (kursda sinf narxlari bo'lsa): sinfni yozadigan HAR yo'l yozgandan keyin
`GradeRepricing.ApplyAsync` ni chaqiradi — joriy (va oldindan to'langan) oylik darhol sinf narxiga
moslanadi (`billing.md` «SINFGA QARAB OYLIK»). Yangi sinf yozish yo'li qo'shsangiz — uni ham chaqiring.

## 4. Qayerda ko'rinadi

- Qo'shish/tahrirlash: `StudentFormModal` («Boshqa ma'lumotlar» → Markazga kelgan sana · Sinf), profil
  «Tahrirlash» tabi. Tuman/maktab tizimdan butunlay olib tashlangan (`crm-leads.md`).
- Profil: kartada ism ostida "7-sinf" + «Shaxsiy ma'lumotlar» → «Sinf». ⚠️ Manba — daftar
  (`StudentNotebookDto.SchoolGradeNow`), to'liq `Student` EMAS: u faqat "Tahrirlash" bosilganda
  yuklanadi va sinf profil ochilganda ko'rinmay qolardi (2026-10-02 da shunday xato bo'lgan).
- O'quvchilar ro'yxati: «Sinf» filtri (sonlari joriy tab bo'yicha, Holat filtridagi kabi).
- «Hisobotlar → Sinflar kesimi» (`/admin/hisobotlar/sinflar`, `students.list`) — manba
  `GET /admin/students` (yangi endpoint YO'Q), standart qamrov — AKTIV o'quvchilar.

- **Excel importi** — shablonda «Sinf (1-11 yoki bitirgan)» ustuni. Ustunlar SARLAVHA bo'yicha topiladi
  (`StudentImportPlan.MapColumns`, 2026-10-09): «Jinsi» ustuni shablondan olib tashlangan (jins
  ixtiyoriy), lekin eski shablon bilan to'ldirilgan fayllar (Jinsi + Sinf) ham to'g'ri o'qiladi; F.I.SH
  sarlavhasi topilmasa — eski o'rinli tartib. Qabul qilinadi: `7`, `7-sinf`, `7 sinf`, `7 класс`, `7.0` (Excel raqami),
  `bitirgan`/`tugatgan` → 12. ⚠️ Harfli sinf (`7a`, `8-B`) markazda YO'Q — xato qator bo'lib qaytadi
  (foydalanuvchi qarori). Bo'sh — kiritilmagan. Mavjud o'quvchida sinf YO'Q bo'lsa importdagisi
  yoziladi, BOR bo'lsa tegilmaydi. Qoida: `StudentImportPlan.TryParseGrade` (`StudentImportPlanTests`).

### Importda DUBLIKATDAN HIMOYA (`StudentImportPlan`, 2026-10-05)

Ilgari har qator yangi o'quvchi yaratardi — ikki guruh ro'yxatidagi o'quvchi ikki marta yaratilgan
(2026-09-30, 22 ta dublikat qo'lda birlashtirildi). Endi: ISM (registr/apostrof/bo'shliqdan qat'i
nazar) VA kamida bitta TELEFON (o'quvchi/ota/ona) mos → yangisi YARATILMAYDI, faqat ko'rsatilgan
guruhga qo'shiladi; faylning o'zidagi takror ham shunday. Faqat ismi mos → yaratiladi, lekin
ogohlantirish bilan (ismdoshlar jimgina birlashmasin). Arxivdagi o'quvchi bilan mos — xato
(avval tiklang). Guruhdan avval chiqqan — jimgina qaytarilmaydi (ogohlantirish). Natija oynasida
«Mavjud o'quvchi» va ogohlantirishlar jadvali.

Klient yordamchilari — `lib/schoolGrades.ts` (faqat yorliq/filtr/sanoq; sinfni QAYTA hisoblamaydi).
