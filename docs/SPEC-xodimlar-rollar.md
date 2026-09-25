# SPEC — Xodimlar va Rollar (2026-09-25)

## Talab (foydalanuvchidan)

1. **Boshqaruv → Rollar** — faqat ROLLAR ro'yxati; har rolga beriladigan ruxsatlar (dostup)
   shu yerda belgilanadi.
2. **Boshqaruv → Xodimlar** — barcha xodim shu yerdan, ROL tanlab qo'shiladi. Rol
   "O'qituvchi" bo'lsa — oylik maosh FOIZI so'raladi.
3. Xodim formasida keraksiz maydon: **Manzil** olib tashlanadi.
4. Foiz — o'qituvchining UMUMIY foizi, guruhda alohida o'zgartirish mumkin.
5. Foiz bazasi — **HISOBLANGAN oylik, CHEGIRMASIZ (to'liq)**, **joriy oydan (2026-09)**.
   O'tgan oylar o'zgarmaydi.

## Model

| Nima | Qaror |
|---|---|
| Rol | Mavjud `StaffRoleTemplate` (Code, Name, Description, DefaultPermissions). Endi CRUD bor |
| Xodim ↔ rol | YANGI `AppUser.RoleTemplateId` (nullable). **Jonli bog'lanish**: rol ruxsatlari o'zgarsa, shu roldagi hamma xodimning `Permissions` i qayta yoziladi |
| Eski xodimlar | `RoleTemplateId = null` — "Individual ruxsatlar", avvalgidek ishlaydi (backfill YO'Q) |
| O'qituvchi | Rol ro'yxatida TIZIM qatori (`teacher`), jadvalda emas. Ruxsatlari o'qituvchi portalining o'z kalitlari (`TeacherPermissions`) — Rollar'da o'zgartirilmaydi |
| Umumiy foiz | Mavjud `Teacher.SalaryMode = "percent"` + `Teacher.SalaryPercent`. Guruh `TeacherSalaryMode = ""` bo'lsa unga ergashadi (mavjud `EffMode`) |

Migratsiya: `AddStaffRoleLink` — faqat `Users.RoleTemplateId` (nullable text).

## Maosh bazasi — `SalaryLedger`

`SalaryBaseChargedFrom = "2026-09"`:

- oy < 2026-09 → **yig'ilgan** pul (avvalgidek — berilgan maoshlar o'zgarmaydi);
- oy >= 2026-09 → **hisoblangan** `MonthlyCharge.Amount` (chegirma AYRILMAYDI).

O'rinbosarlik hovuzi (`SubstituteTeacherService`) AYNAN shu bazadan — nol yig'indili model
buzilmasin.

## API

- `GET/POST/PUT/DELETE api/admin/staff/role-templates[/{id}]` — yozish `HasFullAccess("staff")`.
  O'chirish: rolda xodim bo'lsa 400 (soni bilan).
- `POST/PUT api/admin/staff` — `roleTemplateId` qabul qiladi; ruxsatlar roldan olinadi.
- `PUT api/admin/staff/{id}/permissions` — xodimni roldan ajratadi (individual).

## UI

- **Rollar** (`/admin/boshqaruv/rollar`): rollar ro'yxati (nechta xodim), qo'shish,
  tahrirlash (nom, tavsif, ruxsatlar matritsasi), o'chirish.
- **Xodimlar** (`/admin/boshqaruv/xodimlar`): o'qituvchilar + panel xodimlari bitta
  ro'yxatda, rol filtri. "Xodim qo'shish" → rol tanlanadi → o'qituvchi bo'lsa foiz va fanlar.
  Xodim qatorida: tahrirlash, login/parol, o'chirish, superadmin qilish (faqat superadmin).
- O'qituvchi formasi: manzil olib tashlanadi, umumiy foiz maydoni qo'shiladi.
- Guruh maosh muharriri: "Umumiy (o'qituvchi foizi)" varianti.
