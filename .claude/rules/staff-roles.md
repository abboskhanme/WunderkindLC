# Xodimlar va Rollar qoidalari

"Boshqaruv → Xodimlar" (`/admin/boshqaruv/xodimlar`) + "Boshqaruv → Rollar" (`/admin/boshqaruv/rollar`).
Migratsiya: `AddStaffRoleLink` (`Users.RoleTemplateId`, `CenterMeta.SalaryChargedBaseFrom`).
SPEC: `docs/SPEC-xodimlar-rollar.md`.

## 1. Rol = `StaffRoleTemplate`, xodim unga JONLI bog'langan

- Xodim (`AppUser`, role=`staff`) `RoleTemplateId` orqali rolga bog'lanadi va ruxsatlarini roldan oladi.
- Rolning ruxsatlari o'zgarsa — shu roldagi HAMMA xodimning `Permissions` i qayta yoziladi
  (`StaffRoles.SyncMembersAsync`). ⚠️ Avtorizatsiya qatlami O'ZGARMADI: token va `PermissionRules`
  avvalgidek faqat `AppUser.Permissions` ga qaraydi.
- `RoleTemplateId = null` — "rolsiz (individual)": eski xodimlar shunday qoldi (backfill YO'Q).
  Individual ruxsat tahriri (`PUT {id}/permissions`) xodimni ROLDAN UZADI — aks holda rolning
  keyingi tahriri qo'lda berilgan ruxsatni jimgina bosib ketardi.
- Superadmin/admin qilingan akkauntga rol ruxsati yozilmaydi (ular hamma narsani ko'radi), lekin
  bog'lanish saqlanadi — orqaga tushirilsa roli qaytadi.

## 2. RUXSAT — rol = huquq

Rol yaratish/tahrirlash/o'chirish VA xodimga rol berish — `HasFullAccess(User, "staff")`
(admin/superadmin yoki yalang `staff` kaliti). Klientda — `lib/staffRoles.ts` → `canManageStaffRoles`
(ikkalasi bir xil bo'lishi shart). Sabab: qisman ruxsatli xodim "Administrator" rolini kengaytirib
yoki shu roldagi akkaunt yaratib o'z huquqini oshira olmasin.

## 3. O'chirish va seed

- Rolda xodim bo'lsa o'chirilmaydi (400, soni bilan) — jimgina "individual"ga tushirilmaydi.
- ⚠️ Seed (`Program.cs`) FAQAT jadval bo'sh bo'lganda. Ilgari har restartda mavjud rolga
  "yetishmagan" ruxsatlar QAYTA qo'shilardi — endi bu admin olib tashlagan ruxsatni qaytarib,
  rolning hamma xodimiga jonli tarqatardi (jimgina huquq kengayishi).
  Qabul qilingan chekka holat: admin rollarning HAMMASINI o'chirsa, keyingi restartda standart
  uchtasi qaytadi (xodimsiz — hech kimning huquqi o'zgarmaydi).
- Superadmin'dan oddiy xodimga QAYTARILGANDA (`SetRole`) ruxsatlar roldan qayta yoziladi — u
  superadmin bo'lib turgan paytda rol o'zgargan bo'lishi mumkin.
- Rol sanog'i va "tarqaldi" faqat `role = staff` larni sanaydi. Rol o'chirilganda superadmin/admin
  qilinganlarning bog'lanishi tozalanadi.
- Parol tiklash/almashtirish va individual ruxsat berish ham `HasFullAccess("staff")` — akkauntga
  kirish = uning huquqlari (ilgari `staff:create`/`staff:edit` yetardi — huquq oshirish yo'li).
  Yangi parol xodimning BARCHA ochiq sessiyalarini tugatadi (`auth-sessions.md` §2).
- **O'chirilgan xodim ARXIVDAN tiklansa** (`ArchiveController`, tur `staff`) — ruxsatlarsiz, ROLSIZ
  (`RoleTemplateId = null`) va PAROLSIZ qaytadi. ⚠️ Rol bog'lanishi qolsa rolning KEYINGI tahriri
  (`SyncMembersAsync`) eski ruxsatlarni jimgina qaytarib yozardi. Parol suratga umuman tushmaydi
  (`ArchiveService.StripSecrets` — parol xeshi, ochiq dastlabki parol, har qanday token/secret; ilgari
  ular tungi JSON zaxirasi bilan Telegram'ga ham ketardi), eski suratlar startupda tozalanadi
  (`ScrubStoredSecretsAsync`). Javob admin'ga parol va rolni qayta berishni aytadi.

## 4. O'qituvchi — TIZIM roli

Jadvalda qatori yo'q (`StaffRoles.TeacherRoleId = "teacher"`, klientda `TEACHER_ROLE_ID`).
"Xodim qo'shish" da tanlansa — `TeacherFormModal` (oylik maosh FOIZI bilan, `TeachersController.Create`).
Portal ruxsatlari o'qituvchi kartasida (`TeacherPermissions`), Rollar'da emas.

Foiz bazasi va "Umumiy" rejim — `billing.md`.

## 5. Xodimlar sahifasi

- Bitta ro'yxat: o'qituvchilar (`teachers.list`) + panel akkauntlari (`staff`) — har biri o'z
  ruxsati bilan yuklanadi. Kirish: `XodimlarEntry` (ikkalasi ham yo'q bo'lsa o'qituvchilar
  bo'limining ochiq sahifasiga).
- Per-xodim ruxsat MATRITSASI ATAYIN yo'q — huquq roldan.
- Menyuda «Xodimlar» `/admin/teachers` sahifalarida ham faol (`NavChild.alsoMatch`).
- Eski manzillar: `boshqaruv/staff` → xodimlar, `boshqaruv/roles` → rollar.

## 6. Xodimni O'CHIRISH — jonli havolalar bo'shatiladi (`StaffRelease`)

Akkaunt o'chirilganda (`DELETE api/admin/staff/{id}`) o'sha tranzaksiyada: KPI profili nofaol,
OCHIQ lidlar (aylantirilmagan/yopilmagan/yo'qotilmagan) biriktirilmagan holatga, kassa mas'ulsiz
(ism snapshot'i ham tozalanadi), bajarilmagan topshiriqlar mas'ulsiz (topshiriq tarixiga yozuv),
CTI agent o'chiriladi, Telegram xodim bog'lanishi uziladi. Nima bo'shatilgani — o'sha BITTA audit
qatorida. Sabab: ilgari KPI'da "arvoh" qator qolar, o'chirilgan odamga topshiriq eslatmasi ketaverardi.

⚠️ CTI agent `CtiController.UpdateAgent` dagi o'chirish bilan BIR XIL bo'shatiladi (2026-10-08): nofaol,
`IsOnline=false`, FCM tokeni tozalanadi va saqlangandan KEYIN jonli WebSocket uziladi (`logout` buyrug'i,
`CtiConnectionManager.DisconnectAsync`). Ilgari faqat `IsActive=false` edi — telefon ochiq ulanish bilan
qo'ng'iroq/SMS'da davom etardi. Agent STANDART Local SMS agenti bo'lsa — audit matnida "⚠️ … standart Local
SMS agenti edi — avtomatik Local SMS'lar to'xtadi" va javobda `warning` (messaging.md, `LocalSmsDefaultAgent`).

⚠️ TARIX tegilmaydi: yopilgan/yo'qotilgan lidlarning `AssigneeUserId` i (yo'qotish hisoboti, KPI),
bajarilgan topshiriqlar, `ClosedByUserId`.

⚠️ Arxiv suratiga `PasswordHash` va ochiq `InitialPassword` TUSHMAYDI (`SecretScrub`) — surat kunlik
Telegram zaxirasiga ham kiradi. Arxivdan tiklangan akkaunt PAROLSIZ bo'ladi: admin yangisini yaratadi.

