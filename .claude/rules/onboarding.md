---
description: Moslashuv (onboarding) — yangi xodimning 30 kunlik adaptatsiya cheklisti: qadamlar katalogi (11 standart qadam), ariza "hired" bo'lganda avtomatik boshlash, monitoring (foiz, kechikish).
paths:
  - "WunderkindLC.Domain/OnboardingEntities.cs"
  - "WunderkindLC.Application/Services/OnboardingService.cs"
  - "WunderkindLC.Application/Dtos/OnboardingDtos.cs"
  - "WunderkindLC.Server/Controllers/OnboardingController.cs"
  - "WunderkindLC.Client/src/pages/admin/onboarding/**"
  - "WunderkindLC.Client/src/api/services/onboarding.ts"
---

# Moslashuv (onboarding — 30 kunlik adaptatsiya) qoidalari

"Boshqaruv → Moslashuv" (`/admin/boshqaruv/moslashuv`, ruxsat **`vacancies.onboarding`**).
Yig'ilish talabi: *"11 ta aniq qadamli cheklist (hujjat topshirish, shartnoma imzolash, ichki
tartib-qoidalar, Telegram kanallarga ulanish va h.k.). Har bir qadam bajarilgach belgilanishi va
monitoring qilinishi lozim."* Ishga qabul voronkasining DAVOMI (`career.md` §8).

Migratsiya: (lead yaratadi) — uchta yangi jadval, mavjud jadvallarga TEGILMAYDI.

## 1. Model — uch qatlam

| Entity | Vazifasi |
|---|---|
| `OnboardingStep` | KATALOG (andoza): `Title`, `Description`, `DayOffset` (0..30), `Order`, `IsActive` |
| `EmployeeOnboarding` | Bitta xodimning moslashuvi: ism/telefon/lavozim (SNAPSHOT), `JobApplicationId`, `TeacherId`/`StaffUserId`, `StartDate`, mentor, `Status`, `Source` |
| `EmployeeOnboardingTask` | O'sha xodimning har qadami: katalogdan NUSXA + `DoneAt`/`DoneBy`/`Note` |

⚠️ **Boshlanganda katalog NUSXA qilinadi.** Katalogni keyin tahrirlash, qadamni o'chirish yoki
`IsActive = false` qilish DAVOM ETAYOTGAN moslashuvlarga TEGMAYDI — faqat yangilariga. Sabab:
xodim 10-kunda turganda katalog o'zgarsa uning cheklisti "o'zgarib" qolardi va monitoring yolg'on
chiqardi. UI'da (katalog oynasi tepasida) shu ochiq yozilgan.

⚠️ Ism/telefon/lavozim — SNAPSHOT: ariza o'chirilsa yoki vakansiya nomi o'zgarsa ham moslashuv o'qiladi.

## 2. Katalog seed'i — 11 standart qadam

`OnboardingService.DefaultSteps`: hujjatlar (1-kun) · mehnat shartnomasi (2) · ichki tartib-qoidalar (2)
· Telegram kanal/guruhlar (1) · CRM akkaunt (2) · ish joyi va jihozlar (3) · mentor biriktirish (3)
· metodika/o'quv dasturi (7) · darslarni kuzatish (10) · birinchi mustaqil dars (14) · 30-kunlik
yakuniy suhbat (30).

- `Program.cs` — **FAQAT katalog BO'SH bo'lganda** (tranzaksiya turlaridagi qoida): admin o'chirgan
  qadam restartda qayta tug'ilmaydi.
- ⚠️ Qabul qilingan chekka holat: admin katalogni BUTUNLAY bo'shatsa, keyingi moslashuv boshlanganda
  (yoki restartda) standart 11 qadam qaytadi — "bo'sh katalog = cheklistsiz moslashuv" holatidan
  ko'ra shu afzal. Hammasini o'chirmasdan `IsActive = false` qilish mumkin (nusxa qilinmaydi).
- `AddWithTasksAsync` katalog bo'sh bo'lsa standart qadamlarni **saqlamasdan** qo'shadi — chaqiruvchi
  arizaning bosqich o'zgarishi va audit yozuvi bilan BITTA `SaveChanges` da saqlaydi.

## 3. Boshlash — ikki yo'l

1. **AVTOMATIK** — ariza `hired` ga O'TGANDA (`CareerController.SetStatus`, `changed == true`).
   `StartForApplicationAsync` **IDEMPOTENT**: shu `JobApplicationId` ga moslashuv BOR bo'lsa (har
   qanday holatda — hatto to'xtatilgan/yakunlangan) yangisi ochilmaydi. Boshlanish sanasi — o'sha
   kun (`AppClock.Today`). Qidiruv avval `Local` (saqlanmagan), keyin bazadan.
   - ⚠️ Izohni yangilash (`hired` → `hired`) qayta boshlamaydi: aks holda ataylab o'chirilgan
     moslashuv izoh tahririda "tirilib" qolardi.
   - `hired` → boshqa bosqich moslashuvni BEKOR QILMAYDI (yashirin yon ta'sir yo'q) — kerak bo'lsa
     «To'xtatish» qo'lda.
   - Mavjud `hired` arizalar uchun **backfill YO'Q** (ular allaqachon ishlab yurgan xodimlar).
2. **QO'LDA** — «Yangi xodim moslashuvi» (`POST`): vakansiyasiz qabul qilingan xodim uchun.
   `Source = "manual"`.

## 4. Holat — `active` → `completed` | `cancelled`

| O'tish | Qanday |
|---|---|
| `active` → `completed` | **AVTOMATIK** — oxirgi qadam belgilanganda (`StatusAfterTasks`), `CompletedAt` |
| `completed` → `active` | biror qadam belgisi olinganda |
| → `cancelled` | qo'lda «To'xtatish» (xodim ketdi / xato) — monitoringdan chiqadi, tarix qoladi |
| `cancelled` → | «Qayta ochish» — holat qadamlarga qarab tiklanadi |

⚠️ To'xtatilgan moslashuvda qadam belgilanmaydi (400 — sabab matni bilan). Qadamsiz moslashuv
o'z-o'zidan "yakunlandi" bo'lmaydi.
O'chirish (`DELETE`, qadamlari bilan) — faqat xato yaratilgan yozuv uchun; odatda «To'xtatish».

## 5. Monitoring — sof qoidalar (`OnboardingService`, `OnboardingServiceTests`)

- **Muddat** = `StartDate + DayOffset` kun (`DueDate`).
- **KECHIKKAN** = bajarilmagan VA muddat **BUGUNDAN OLDIN** (`IsOverdue`). Bugungi muddat hali
  kechikmagan. Faqat **faol** moslashuvda sanaladi (to'xtatilgan/yakunlanganda qizil yo'q).
  Buzuq sana — kechikmagan (yolg'on qizil bo'lmasin).
- **Foiz** = bajarilgan / jami, **PASTGA** yaxlitlanadi — 100% faqat hammasi bajarilganda (10/11 = 90%).
- **Kun** = boshlanish kuni 1-kun (`DayNumber`); 30 dan oshishi mumkin (cho'zilgan moslashuv ko'rinsin).
- **Navbatdagi qadam** — eng yaqin muddatli bajarilmagan.
- Ro'yxat filtri `overdue` = faol + kechikkan qadami bor. Saralash: faollar tepada, eng ko'p
  kechikkani birinchi.
- Jamlanma (`GET summary`) ro'yxat DTO'laridan `Summarize` bilan — bitta hisob, ikki joyda ayri ketmaydi.
- Kechikish SERVERDA hisoblanadi (`AppClock.Today`); klientdagi `dueText` faqat matn ("3 kun kechikdi").

## 6. API — `api/admin/onboarding` (`OnboardingController`)

`[AdminPerm("vacancies.onboarding", ReadRequiresPerm = true)]` — O'QISH ham darvozalangan: javobda
yangi xodimlarning telefoni va ichki izohlar bor.

`GET` (ro'yxat, `?status=&q=`) · `GET summary` · `GET {id}` (cheklist bilan) · `POST` (qo'lda boshlash) ·
`PUT {id}` (sarlavha; `StartDate` o'zgarsa barcha muddatlar siljiydi) · `PUT {id}/tasks/{taskId}`
(`{done, note}`) · `PUT {id}/cancel` · `PUT {id}/reopen` · `DELETE {id}` · `GET people` (mentor/akkaunt
tanlash uchun — faqat ism) · `GET|POST steps` · `PUT|DELETE steps/{id}` · `PUT steps/reorder`.

- Amallar: belgilash/tahrir/to'xtatish — `edit` (PUT); boshlash va katalogga qadam qo'shish —
  `create` (POST); o'chirish — `delete`.
- Mentor — o'qituvchi yoki panel xodimi (id + ism SNAPSHOT). `TeacherId` va `StaffUserId` dan
  faqat BITTASI bo'ladi (400).

## 7. Ruxsat — `vacancies.onboarding` (sahifa kaliti)

`vacancies` bo'limi 2026-10 dan IKKI sahifali: `vacancies.list` (vakansiyalar va arizalar —
`CareerController`) va `vacancies.onboarding`. Yalang `vacancies` (eski xodimlar) ikkalasini ham
ochadi (pastga meros).

⚠️ **Faqat moslashuv berilgan xodim nomzod CV'larini KO'RMAYDI** — aynan shu sabab `CareerController`
darvozasi bo'lim emas, `vacancies.list` sahifa kaliti bilan (`permissions.md` §2). Teskarisi ham:
faqat `vacancies.list` berilgan xodim moslashuvni ochmaydi. Qulflovchi testlar: `OnboardingRbacTests`.

## 8. Audit

`EntityType = "Onboarding"` (moslashuv: boshlash — avtomatik/qo'lda, tahrir, qadam belgilash/izoh,
yakunlanish, to'xtatish, qayta ochish, o'chirish) va `"OnboardingStep"` (katalog) — ikkalasi
`AuditSections` da **`vacancies`** bo'limiga. Avtomatik boshlash yozuvi arizaning bosqich yozuvi
bilan bitta tranzaksiyada.

## 9. Klient

```
pages/admin/onboarding/
  onboardingModel.ts        sof: yorliqlar, filtrlar, dueText, todayIso (MAHALLIY sana), akkaunt kodlash
  OnboardingPage.tsx        monitoring: jamlanma kartalari, filtr chiplari, qidiruv, progress
  OnboardingDetailModal.tsx cheklist: belgilash (optimistik EMAS), izoh, to'xtatish/qayta ochish
  OnboardingFormModal.tsx   qo'lda boshlash / sarlavha tahriri
  OnboardingStepsModal.tsx  katalog: qo'shish, tahrir, tartib, o'chirish (`run` yordamchisi)
```

Modallar `key` bilan qayta yaratiladi — boshlang'ich qiymatlar `useState` initializer'ida, effekt
ichida to'g'ridan-to'g'ri setState yo'q; ro'yxatni qayta yuklash `tick` orqali.
Kesh: `/admin/career` mutatsiyasi `/admin/onboarding` ni ham tozalaydi (avtomatik boshlash).

## 10. Nima ATAYIN qilinmagan

- Xodimga Telegram eslatmasi (kunlik "bugungi qadamlar") — hozircha yo'q; kerak bo'lsa alohida ish (kunlik
  fon ishi — `JobRunMarker` naqshi, `messaging.md` «KUNLIK FON ISHLARI»).
- Moslashuv tugaganda o'qituvchi/xodim akkauntini avtomatik yaratish — yo'q: akkaunt odatdagi
  yo'l bilan (O'qituvchilar / Xodimlar) yaratiladi va shu yerda faqat BOG'LANADI.
