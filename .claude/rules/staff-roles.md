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
