---
description: O'quvchini BUTUNLAY o'chirish (dublikat / xato kiritilgan) — nima o'chadi, nima saqlanadi, kim qila oladi.
paths:
  - "WunderkindLC.Application/Services/StudentPurge.cs"
  - "WunderkindLC.Server/Controllers/StudentPurgeController.cs"
  - "WunderkindLC.Client/src/pages/admin/students/StudentPurgeModal.tsx"
---

# O'quvchini butunlay o'chirish qoidalari

O'quvchi profili → **«⋮» → «Butunlay o'chirish»** → tasdiqlash oynasi (nimalar o'chishi ko'rsatiladi
+ "Tushundim" ptichkasi). Maqsad — xato kiritilgan yoki DUBLIKAT o'quvchini izsiz olib tashlash.
Migratsiya KERAK EMAS.

Endpointlar: `GET api/admin/students/{id}/purge-preview` · `DELETE api/admin/students/{id}/purge`.
Mantiq — `StudentPurge` (Application, testlar: `StudentPurgeTests`).

## 1. Oddiy "o'chirish" bilan ADASHTIRMANG

| | Arxiv tabidagi «Butunlay o'chirish» (`DELETE {id}`) | Profildagi «Butunlay o'chirish» (`DELETE {id}/purge`) |
|---|---|---|
| O'quvchi yozuvi | «O'chirilganlar arxivi»ga (`ArchivedRecords`) nusxa qoladi | **Hech qayerda qolmaydi** |
| Bog'liq qatorlar | faqat 4 jadval (hisob, a'zolik, izoh, jurnal) — qolgani yetim | **o'quvchiga tegishli HAR BIR jadval** |
| Fayllar (rasm, passport, selfi, sertifikat) | diskda qoladi | **diskdan ham o'chadi** |
| Kim | `students.list:delete` | `students.list:delete` (AYNAN bir xil kalit) |
| Tarix | «O'quvchi o'chirildi …» qatori (2026-10-07 dan, `studentId` bilan) | bitta qator (§6) |

**Arxivdan TIKLASH** («Arxiv (barcha turdagi)» → «Tiklash», `ArchiveController`): o'chirishda oylik
hisoblar va a'zoliklar ham o'chgan, surat esa eski BALANSNI saqlaydi. Shuning uchun tiklashda:
balans = `min(max(surat, 0), qolgan qatorlar)` (`StudentArchiving.RestoredBalance`: qarz faqat qolgan
qatorlar uni tushuntirsa qoladi, avans suratdagidan oshmaydi — to'lovlar qolib hisoblar o'chgani uchun
"tekin avans" chiqmasin), `ClassName` bo'shatiladi (guruhsiz billing yorliqqa qarab qayta ishlamasin),
o'chgan login akkauntiga bog'lanish uziladi. Javobda — "guruhsiz tiklandi, guruhga qo'shing".

## 2. RUXSAT — «O'quvchilar ro'yxati va profili» → «O'chirish» (`students.list:delete`)

Foydalanuvchi qarori (2026-09-30): "faqat superadminda bo'lmasin, role berilgan istalgan odamda
bo'lsin". Ya'ni admin/superadmin va **Rollar** matritsasida shu katak belgilangan har qanday xodim
(meros bilan `students`, `students:delete`, `students.list` ham o'tadi — `permissions.md` §2).

- Server: `StudentPurgeController.MayPurge` → `PermissionRules.CanWrite(..., "students.list", "DELETE")`.
  ⚠️ U **GET (preview) da ham** tekshiriladi: `[AdminPerm]` GET'ni har qanday xodimga ochiq qoldirardi.
- Klient: `can('students.list', 'delete')` — arxiv tabidagi eski o'chirish tugmasi bilan bir xil.
- Ruxsatsiz so'rov — 403, sabab matni bilan. Yangi ruxsat kaliti QO'SHILMAGAN.

⚠️ Ilk versiyada (o'sha kuni) faqat superadmin edi — foydalanuvchi talabi bilan kengaytirildi.
Qulflovchi testlar: `StudentPurgeTests` → `Har_endpoint_BIRINCHI_qadamda_ochirish_ruxsatini_tekshiradi`,
`Ruxsat_qoidasi__faqat_OCHIRISH_amali_yetadi`.

## 3. PUL YOZUVI O'CHIRILMAYDI — tirik to'lov bo'lsa RAD etiladi

Tirik (bekor qilinmagan) `FinanceTransaction` bor o'quvchi o'chirilmaydi: preview `canPurge = false`
+ sabab, server esa 400 (`StudentPurge.BlockReason`). Sabab: moliya yozuvlari hech qachon
o'chirilmaydi (`billing.md` → «Tranzaksiyani BEKOR QILISH»), to'lovni o'chirish kassa hisobotini
orqaga qarab o'zgartirardi.

Dublikatga adashib to'lov yozilgan bo'lsa: to'lovni **bekor qiling** (sabab bilan) → to'g'ri
o'quvchiga qayta kiriting → keyin o'chiring.

⚠️ Bu qoida qo'shimcha himoya ham: haqiqiy (to'lov qilgan) o'quvchini tasodifan o'chirib bo'lmaydi.

## 4. Nima O'CHADI, nima SAQLANADI

**O'chadi** (o'quvchining o'zi): a'zoliklar, oylik hisoblar, jurnal/baho/ball, chegirmalar, izohlar,
bog'lanish talablari va hodisalari, test natijalari/sertifikatlari, onlayn kurs natijalari, AI tahlil
va tekshiruvlar, fikrlar, yuz bilan kirish, bonus/uzaytirish yozuvlari, Telegram bog'lanishi,
YUTUQLAR (`StudentAchievement`) va Telegram xabarlari jurnali (`MessageDelivery`, 2026-10-12), ketma-ket kelmaslik
belgilari (`AbsenceStreakAlert`, 2026-10-12),
qabul cheklistining qo'lda belgilari (`StudentIntakeMark`),
o'quvchi bo'yicha **audit yozuvlari** (a'zolik `"{groupId}:{studentId}"` ham), login akkaunti va
uning tokenlari/qurilmalari/xabarlari.

**Saqlanadi, faqat ajratiladi** (`StudentId = null`) — boshqa bo'limning yozuvi:
bekor qilingan pul amallari · qo'ng'iroqlar (`Call`, `CtiCallRecord`) · kitob sotuvi · lid
(`ConvertedStudentId`) · support darslari (bron qilingan — qayta **bo'sh** bo'ladi, o'tilgani qoladi).

⚠️ **LID YOPIQ QOLADI** (2026-10-07): ajratilgan lid YO'QOTILGAN deb belgilanadi (`LeadLoss` maydonlari,
sabab «O'quvchi o'chirildi» — katalogdan tashqari snapshot, lid tarixiga `lost` hodisasi, bosqich
tegilmaydi). Sabab: "ochiq lid" = aylantirilmagan VA yo'qotilmagan — faqat `ConvertedStudentId = null`
qilinsa lid jimgina QAYTA OCHILIB bosh sahifa, KPI ("eski ochiq lidlar"), sinov eslatmasi va cheklistga
"ishlanmagan lid" bo'lib tushardi.

**Tegilmaydi:** shartnomalar — ular ota-ona TELEFONIGA bog'langan (`Contract.RecipientKey`) va
aka-ukalar bilan umumiy.

⚠️ **Login akkaunti** faqat u `student` roli bo'lsa va boshqa hech bir o'quvchi unga bog'lanmagan
bo'lsa o'chadi — xodim akkauntini tasodifan o'chirmaslik uchun.

⚠️ **Rasm/passport fayli** boshqa o'quvchida ham turgan bo'lsa (bir xil manzil) — o'chirilmaydi.
⚠️ Ular admin formasidan keladi, shuning uchun (2026-10-08) faqat **server yuklash nomi**
(`/uploads/<32 hex>.<ruxsat etilgan kengaytma>`, `UploadUrlRules.IsServerUpload`) VA boshqa hech bir
yozuv (shartnoma, test, dars materiali, o'qituvchi surati ... — `UploadReferences.InUseAsync`)
ishlatmayotgan fayl o'chiriladi. Aks holda qo'lda yozilgan BEGONA manzil (masalan shartnoma PDF'i)
o'quvchi bilan birga diskdan o'chib ketardi (`uploads-security.md`).

## 5. Tartib — hammasi yoki hech narsa

`StudentPurge.PurgeAsync` o'zgarishlarni faqat TAYYORLAYDI; controller audit qatorini qo'shib
**bitta** `SaveChangesAsync` qiladi. Fayllar saqlash muvaffaqiyatli bo'lgandan **keyin**
o'chiriladi (saqlash yiqilsa fayl yo'qolib qolmasin). Fayl manzili `ResolveUploadPath` dan o'tadi:
faqat `uploads/`, `uploads/face/`, `uploads/certificates/` — boshqa yo'l (`..`, begona papka) rad etiladi.

## 6. Tarixda BITTA iz qoladi

«O'zgarishlar tarixi»da bitta qator: *"O'quvchi BUTUNLAY o'chirildi (xato kiritilgan / dublikat):
<ism>"* — kim va qachon. `studentId` berilmaydi (mavjud bo'lmagan o'quvchiga bog'lanmasin).
Sabab: qaytarib bo'lmaydigan amalning "kim qildi" savoliga javob qolishi shart.

## 7. Yangi jadval qo'shsangiz

O'quvchiga bog'liq yangi entity (`StudentId` ustuni) qo'shsangiz — uni `StudentPurge` ga ham
qo'shing (o'chirish yoki ajratish) va `PreviewAsync` sanog'iga kiriting. Aks holda o'chirilgan
o'quvchidan yetim qatorlar qoladi.
