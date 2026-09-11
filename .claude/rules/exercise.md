---
description: O'quv dasturidagi INTERAKTIV MASHQ (topshiriq konstruktori) — 8 kategoriya, 25 tur, tahrirlovchi + jonli foydalanuvchi ko'rinishi, o'quvchi portalida ishlashi.
paths:
  - "WunderkindLC.Client/src/components/exercise/**"
  - "WunderkindLC.Client/src/pages/admin/curricula/**"
  - "WunderkindLC.Server/Controllers/CurriculumController.cs"
  - "WunderkindLC.Client/src/pages/student/Lesson.tsx"
---

# Topshiriq konstruktori (interaktiv mashqlar) qoidalari

- **QAYERDA:** o'quv dasturining OXIRGI bosqichida — Dastur → Modul → Mavzu → Dars → **Topshiriq**.
  Topshiriq turi (`LESSON_TYPES`) ga **"Mashq"** (`exercise`) qo'shilgan. Shu turdagi topshiriq
  ochilganda `CurriculumItemEditorPage` oddiy tahrirlovchi o'rniga **`ExerciseWorkspace`** ni
  ko'rsatadi.

- **YARATISH OQIMI (muhim):** darsdagi **"+ Topshiriq"** tugmasi endi eski "tur chiplari" modalini
  EMAS, **tur tanlash ekranini** (`ExercisePicker` — zip maketidagi "Topshiriq yaratish") ochadi.
  Tablar: 8 mashq kategoriyasi + **oxirgi "Boshqa"** tabi (`OTHER_CATEGORY`) — u yerda ESKI turlar
  (video / matn / audio / PDF / lug'at / oddiy test) o'z mini previewlari bilan.
  · Mashq turi tanlansa → bitta nom so'raladi (`NameModal`, tur nomi bilan oldindan to'ldirilgan) →
    `createItem(..., type="exercise", exerciseKind)` → darhol konstruktorga o'tiladi.
  · "Boshqa" turi tanlansa → eski `BulkAddModal` (tur qulflangan, faqat nomlar) → `createItemsBulk`.
  Konstruktor ichida TUR ALMASHTIRISH va TIL tanlash YO'Q (mashq bitta tilda yoziladi) — tur
  topshiriq yaratishda tanlanadi; keyin o'zgartirish kerak bo'lsa jadvaldagi qalam tugmasi
  (`ItemEditModal`) ishlatiladi.

- **TOPSHIRIQLAR RO'YXATI** (`CurriculumItemsPage`): sarlavhada "N ta topshiriq yaratilgan · M tasi
  tayyor"; jadval ustunlari — **Nomi · Topshiriqlar soni · Topshiriq turi · Yaratilgan sana**.
  "Topshiriqlar soni" = topshiriq ICHIDAGI elementlar soni (`CurriculumItemDto.Count`): mashqda
  gap/savol/juftlik, testda savol, lug'atda so'z soni. Server tomonda `CurriculumController.CountFor`
  + `ExerciseItemCount` — mashq JSON'i turga bog'lanmasdan umumiy tarzda sanaladi
  (`<oila>.items` / `matching.rows` / writing-speaking `topic`), yangi tur qo'shilsa tegish shart emas.

- **SAQLASH:** `CourseItem.ExerciseKind` (tur, masalan `sentence-order`) + `CourseItem.ExerciseJson`
  (turga mos JSON mazmun) — migratsiya `AddCourseItemExercise`. `SaveItemContentRequest`da bu ikki
  maydon **null bo'lsa TEGILMAYDI** (boshqa turdagi topshiriqni saqlash mashqni o'chirmasin).
  `IsReady` (daraxtdagi yashil belgi) va `MetaFor` (tur yorlig'i) mashqni hisobga oladi —
  `CurriculumController.ExerciseLabel` nomlari front-end katalogi bilan bir xil.

- **TAXONOMIYA — 8 kategoriya, 25 tur** (`components/exercise/catalog.tsx`, `CATEGORIES`):
  Make sentence (so'z tartibi / audio / rasm / variant tanlash) · Bo'sh joyni to'ldirish (variant /
  yozish / audio / rasm / **audio + rasm**) · So'z tanlash (oddiy / rasm / audio) · So'z topish
  (oddiy / rasm / audio) · Reading (variant / bo'sh joy / qisqa javob) · Test (rasmli / rasmli
  variantlar / audio) · Writing & Speaking · Moslashtirish (oddiy / reading / audio).
  Tur `ExerciseKind` (`model.ts`) — `kindFamily()` tahrirlovchini, `kindMedia()` media rejimini
  (`none | audio | image | **both**`), `kindTheme()` rang sxemasini beradi. Nom oxiri qoida:
  `-audio` → audio, `-image` → rasm, `-media` → IKKALASI.
  DIQQAT: Reading'da "To'g'ri / Xato" turi ATAYIN yo'q (foydalanuvchi talabi bilan olib tashlangan).

- **KOMPONENTLAR** (`src/components/exercise/`, CRM ichida — admin ham, o'quvchi portali ham
  ishlatadi): `model.ts` (ma'lumot modeli + parsing yordamchilari: `___` bo'sh joylar,
  `(bir/*ikki)` variantlar, `/` bilan ajratilgan javoblar), `catalog.tsx` (turlar + mini previewlar
  + ranglar), `kit.tsx` (maketdagi chrome: qorong'i sarlavha, banner, ikki panel, telefon ramkasi,
  toast, audio/rasm yuklagich), `ExercisePicker.tsx` (tur tanlash ekrani), `editors/*` (chap panel
  tahrirlovchilari), **`players.tsx`** (o'ng paneldagi jonli ekran).

- **BITTA PLEYER — IKKI JOYDA:** `ExercisePlayer`/`ExerciseRunner` konstruktordagi "Foydalanuvchi
  ko'rinishi" previewi (`mode="preview"`, element chapdagi ro'yxatdan tanlanadi) VA o'quvchi
  portalidagi haqiqiy ishlash (`mode="solve"`, elementlar ketma-ket, oxirida natija) uchun AYNAN bir
  xil komponent. Yangi tur qo'shilsa faqat shu ikki joyga (editor + player) qo'shiladi.

- **O'QUVCHIDA:** `student/Lesson.tsx` bo'limlariga `exercise` qo'shilgan — dars kontenti ichida
  (video/matn/audio/pdf/lug'at/test dan keyin) alohida qadam sifatida chiqadi. Dars ochilishi eski
  qoidada: o'qituvchi "o'tildi" qilmaguncha yopiq (`GroupCurriculumLog`).

- **NATIJA SAQLANADI — `CourseItemAttempt`** (migratsiya `AddCourseItemAttempt`): har ishlash
  ALOHIDA URINISH (`AttemptNo` 1,2,3…), ustiga yozilmaydi — o'sish dinamikasi ko'rinadi.
  Maydonlar: `Section` (**exercise** | **test** | **view**), `ExerciseKind`, `Correct/Total/ScorePct`,
  `DurationSec`, `AnswersJson` (`[{index,prompt,answer,expected,ok,sec}]`), `GroupId` (qaysi guruh
  orqali ishlangani), `StartedAt/FinishedAt`.
  · **Pleyer**: `useRunner.finish(ok, {prompt,answer,expected})` va `onFinish(correct,total,answers)` —
    javoblar `RunState.answers` **Map**'ida INDEKS bo'yicha (↺ bosib qayta tekshirilsa hisob
    ikkilanmaydi; ilgari `scoreRef` har tekshiruvda oshib ketardi). Bir elementli mashqlar
    (writing/speaking/matching) `singleLog(...)` bilan bitta yozuv qaytaradi.
  · **Yozish yo'li**: `POST /api/student/curriculum/attempt` (faqat `student` roli) →
    urinish + `CourseProgress.Done=true` avtomatik. Xato YUTILADI — internet uzilsa ham o'quvchi
    ekranida natija ko'rinaveradi.
  · **Ko'rish bo'limlari** (video/matn/audio/PDF/lug'at): "Yakunlash" bosilganda BITTA `view`
    urinishi — har bo'limga sarflangan vaqt bilan (ballsiz, `Total=0`).
  · **Admin**: o'quvchi profili → "O'quv dasturi" tabi → **"Topshiriq natijalari"**
    (`AttemptsSection`): statistika, tur bo'yicha filtr, urinish tafsiloti modali.
    `GET /api/admin/curriculum/student/{id}/attempts` + `attempt/{id}`.
  · DIQQAT: progress/urinish kaliti HAR DOIM **`Student.Id`** (AppUser.Id EMAS) — ilgari student
    portali claim'dagi user id'ni yozardi, admin esa Student.Id bo'yicha o'qirdi va hech qachon
    mos kelmasdi (`MeAsync()`/`TargetAsync()` ishlatiladi).

- **DIZAYN — CRM tizimiga moslangan:** maketning TUZILISHI va o'zaro ta'siri saqlangan, ranglar esa
  CRM dizayn tokenlariga keltirilgan (`catalog.tsx` → `UI` va `THEMES`): yagona aksent
  **brand-600 `#5d53cb`** (+ `#eef0ff` yumshoq fon), matn `#181a22 / #4a4d56 / #777a82`, chegara
  `#e3e4e8`, yuzalar `#fff / #fbfaf7 / #f7f5f1`, holat ranglari `#169f65` va `#de3b3d`.
  DIQQAT: ilgari har kategoriyaning o'z rangi bor edi (binafsha/ko'k/yashil/sariq/pushti) va juda
  rang-barang ko'rinardi — endi `THEMES` ning barcha yozuvlari bitta `CRM_THEME` ga ishora qiladi.
  Sarlavha paneli ham QORA emas, oq (CRM modallari kabi). Shrift — CRM global shrifti
  (`var(--font-sans)`, Pliant); alohida Google shriftlari ishlatilmaydi.
  Hover/animatsiyalar `src/styles/exercise.css` da `.dc-root` ichida (qolgan CRM'ga ta'sir qilmaydi).
