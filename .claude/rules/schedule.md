# Dars jadvali qoidalari

"Future → Jadval: bo'sh oraliqlar" (`/admin/jadval/bosh-oraliqlar`, ruxsat `schedule.timetable`).

> ⚠️ 2026-09-18 dan `/admin/jadval` (menyuda **Guruh → Dars jadvali**) — edutizimdagidek bosh
> sahifaning jadvali (`AdminDashboard scheduleOnly`). ⚠️ 2026-10 dan (CRM Phase 5) u **SANALI**:
> «Kun» / «Hafta», `GET /api/admin/schedule/dated` — §8. Eski hafta-kuni shabloni (`schedule/grid`,
> kun tugmalari) sahifalardan olib tashlandi; endpoint qoldi (eski eksport `?day=`).
> Quyidagi bo'sh oraliq/tavsiya tahlili edutizimda YO'Q, shuning uchun "Future" ga ko'chdi
> (`docs/EDUTIZIM-PARITY.md`). Qoidalar o'zgarmadi.
Migratsiya KERAK EMAS — modul mavjud `Group` maydonlaridan hisoblanadi.

## 1. MODEL — qat'iy "soat/para" YO'Q

Jadval butunlay `Group` ichida: **`Days`** (0=Dushanba … 6=Yakshanba), **`StartTime`/`EndTime`**
("HH:mm", erkin matn), **`RoomId`** (eski matnli `Room` zaxira sifatida), **`TeacherId`**.

⚠️ **Bitta guruh — bitta vaqt.** Guruhning barcha dars kunlari AYNAN bir xil vaqtda bo'ladi;
kun bo'yicha alohida vaqt belgilash mumkin emas. Shuning uchun jadval "1-soat, 2-soat"
kataklariga emas, **haqiqiy vaqt o'qiga** quriladi (bazada 60, 90, 120 va hatto 210 daqiqalik
darslar bor).

⚠️ **BAZADA BUZUQ QATORLAR BOR** — tugash boshlanishdan oldin ("13:30 → 03:00"), tugash vaqti
bo'sh ("21:29 → ''"), kunlari bo'sh. Ular jadvalga KIRMAYDI, lekin **soni qaytariladi**
(`ScheduleBoardDto.SkippedGroups`) va ekranda ogohlantirish bo'lib chiqadi — guruh jimgina
yo'qolib qolmasin.

## 2. QOIDALAR — `ScheduleRules` (sof funksiyalar, `ScheduleRulesTests`)

`ParseTime` · `FormatTime` · `MakeSlot` · `FindGaps` · `MergeSlots` · `IsFree` ·
`TouchesLesson` · `BusyHistogram` · `PeakInRange` · `ClampGapMinutes`.

### "Bo'sh oraliq" nima

⚠️ FAQAT **IKKI DARS ORASIDAGI** bo'shliq. Kun boshidagi va oxiridagi bo'sh vaqt **teshik
EMAS**: u har xonada har kuni rost bo'lardi va ro'yxatni foydasiz uzun qilardi. Muammo aynan
orada qolib ketgan teshik — *"4-soatda dars bor, 5-soat bo'sh, 6-soatda yana dars"*.

⚠️ **Ustma-ust tushgan darslar avval BIRLASHTIRILADI** (`MergeSlots`). Xona/o'qituvchi ikki
marta band qilingan bo'lishi mumkin — 2026-10 gacha to'qnashuv faqat OGOHLANTIRILARDI (eski
ma'lumotda qolgan), endi esa QAT'IY taqiq (§8), lekin superadmin sabab bilan o'tkaza oladi.
Birlashtirmasak "10:30 → 10:00" kabi manfiy teshik chiqardi.

⚠️ Standart chegara **60 daqiqa** (`DefaultMinGapMinutes`): bazadagi eng qisqa dars 60 daqiqa,
ya'ni undan kalta oraliqqa baribir hech narsa sig'maydi.

## 3. TAVSIYA — kim/nima to'ldiradi

`ScheduleService.GetGapsAsync(scope, ownerId, minMinutes)`.

| `scope` | Savol | Tavsiya |
|---|---|---|
| `room` (standart) | Xona bekor turibdi | Shu paytda BO'SH **o'qituvchilar** |
| `teacher` | O'qituvchi kutib o'tiribdi | Shu paytda BO'SH **xonalar** |

**O'qituvchi tavsiyasining tartibi** (`SuggestTeachers`):

1. **Yonma-yon darsi bor** (`TouchesLesson`) — u markazda BARIBIR turibdi, teshikni to'ldirsa
   kuni uzluksiz bo'ladi: qo'shimcha qatnov ham, kutish ham yo'q. **Eng kuchli belgi.**
2. Shu kuni markazda darsi bor.
3. Umuman bo'sh (qatnov kerak, lekin quvvati bor).

Teng bo'lsa — **haftalik yuki kam** bo'lgani tepada. Ko'pi bilan `MaxSuggestions` = 5 ta.

⚠️ **Jadvalda umuman darsi yo'q o'qituvchilar ham tavsiyaga kiradi** — ular eng bo'sh resurs.
Ular guruhlardan emas, `db.Teachers` dan (arxivlanmaganlar) olinadi.

⚠️ O'qituvchining bandligi **BUTUN markaz bo'yicha** tekshiriladi, xona kesimida emas: u boshqa
xonada dars o'tayotgan bo'lsa ham band.

## 4. TIG'IZLIK — ro'yxat nima bo'yicha saralanadi

`BusyHistogram` — (kun, yarim soat) → o'sha payt davom etayotgan darslar soni, **butun markaz**
bo'yicha. Bo'sh oraliqlar ro'yxati **`PeakScore` bo'yicha kamayish tartibida** keladi.

⚠️ Sabab: jadval tuzishda savol "qachon bo'sh xona bor" emas, **"odam qachon KELADI"**. Eng
tig'iz paytdagi bo'sh xonaning "narxi" eng baland — aynan o'shanda o'quvchi kela oladi.
Shuning uchun yangi guruh (va o'qituvchining jadvali) avval shu kataklarga qo'yiladi.

## 5. JADVALNI O'ZGARTIRMAYDI

⚠️ `ScheduleController` da **POST/PUT YO'Q** — modul faqat KO'RSATADI va TAVSIYA beradi.
Guruhning vaqti/xonasi/o'qituvchisi avvalgidek guruh formasidan tahrirlanadi (u yerda
`RoomConflictService` to'qnashuvni tekshiradi).

Sabab: jadvalni avtomatik ko'chirish maosh hisobiga, jurnalga va o'quvchi xabarlariga tegib
ketardi — qaror odamniki bo'lib qolsin. Avtomatik joylashtirish kerak bo'lsa, u ALOHIDA ish
sifatida ko'rib chiqilsin (bu qoidaga tayanadi, uni almashtirmaydi).

## 6. Ruxsat va joylashuv

- `[AdminPerm("schedule.timetable")]` — sinf darajasida, GET'lar odatdagidek xodimga ochiq
  (javobda faqat guruh/o'qituvchi/xona nomlari va vaqtlar — nozik ma'lumot yo'q).
- Menyuda **O'quv bo'limi → Dars jadvali**. "Hisobotlar" bo'limiga ATAYIN qo'shilmadi: bu
  ko'rish emas, **ish** sahifasi (jadval tuziladi) — `.claude/rules/reports.md` §2 qoidasi.

## 7. Yangi ko'rsatkich qo'shsangiz

- Vaqtni QO'LDA parse qilmang — `ScheduleRules.ParseTime`/`MakeSlot` (buzuq qatorlar shu yerda
  filtrlanadi).
- Bandlikni `IsFree` bilan tekshiring (yarim-ochiq oraliq: `[start, end)` — tegib turgan darslar
  to'qnashmaydi).
- Tashlab yuborilgan guruhlar SONINI qaytaring — jimgina yo'qotmang.

## 8. SANALI JADVAL, QAVATLAR va TO'QNASHUV TAQIG'I (CRM Phase 5, 2026-10)

Migratsiya `CrmWorkspace` — `Rooms.Floor` (`int?`). Backfill YO'Q (eski xonalar «Qavat ko'rsatilmagan»).

### 8.1. «Kun» / «Hafta» — haqiqiy sanadagi darslar

`GET /api/admin/schedule/dated?from=&to=` (`schedule.timetable`; bo'sh — bugun; ko'pi bilan **7 kun**,
teskari/buzuq oraliq — 400). Manba — **`AttendanceWatch.LessonsOn`** (o'qituvchi jadvali, darslar monitoringi va
davomat eslatmasi bilan BITTA manba): guruh kunlari + bir martalik KO'CHIRISHLAR (dars YANGI sana/vaqtida,
`moved`), guruh umri (`StartDate`..`EndDate`/arxiv), O'RINBOSAR (`substituteTeacherName`). Qoida va yuklash —
`DatedSchedule` (`Build` sof, `DatedScheduleTests`). Kesh — `DataCache` `schedule:dated:{from}:{to}` (bog'liq
turlar: Group, Teacher, Subject, Room, StudentGroup, Student, LessonNote, LessonReschedule,
SubstituteTeacherAssignment).

- Javob: `rooms` (BARCHA faol xona + darsi bor nofaol/eski matnli xona — dars jimgina yo'qolmasin), `teachers`
  (arxivlanmaganlar + dars o'tadiganlar), `lessons` (`members` = `SeatHolders`, `attendanceTaken` = jurnalda
  «o'tildi»), `skippedGroups` (kunsiz/vaqti buzuq guruhlar SONI).
- Klient (`lib/scheduleDated.ts`, testlari bilan): manzilda `?date=&view=day|week&groupBy=room|teacher&layout=list`
  + filtrlar (`teacher`, `group`, `course`, `room`, `floor`, `status`), `replace` bilan. ⚠️ O'QITUVCHI filtri
  EGASI ham, O'RINBOSARI ham bo'yicha; «o'qituvchi bo'yicha» ustunlarda dars — shu kuni uni O'TADIGAN odamda.
- «Kun» — mavjud to'r/ro'yxat komponentlari (`ScheduleGridView`/`ListView`), xona ustunlari **QAVAT sarlavhalari**
  ostida («1-qavat», …, «Qavat ko'rsatilmagan»), qavat o'sish tartibida, qavat ichida tabiiy nom. «Hafta»
  (`WeekBoard`) — qatorlar xona (qavat bo'yicha) yoki o'qituvchi, ustunlar Dushanba–Yakshanba, katakda
  «14:00 Guruh» chiplari, «ko'chirilgan» / «o'rinbosar: …» belgilari.
- Eksport: `GET export?date=yyyy-MM-dd` — SANALI kun (`DatedSchedule.ToGrid` → o'sha `ScheduleGridExport`);
  `?day=` (shablon) eski havolalar uchun qoldi.

### 8.2. Qavat — `Room.Floor`

−2..50 (`RoomFloors`, sof). Tahrirda `floor` yuborilmasa — TEGILMAYDI (`IsActive` dagi qoida), tozalash —
`clearFloor: true`. Audit `Room` — qavat o'zgarishi matnda («qavat: 1-qavat → 2-qavat»).

### 8.3. ⚠️ TO'QNASHUV — QAT'IY TAQIQ (409)

Qoida — **`ScheduleConflicts`** (sof, `ScheduleConflictsTests`); `RoomConflictService` faqat IO o'rami. Ikki guruh
to'qnashadi: ikkalasi arxivlanmagan, kunlari va vaqti to'g'ri, umumiy hafta kuni, yarim-ochiq vaqtlari kesishadi,
**sana oraliqlari** `[StartDate|−∞, EndDate|+∞]` kesishadi va **xona** (`RoomId`) yoki **o'qituvchi** (`TeacherId`)
bir xil.

| Joy | Nima |
|---|---|
| `ClassesController.Create` · `Update` · `Unarchive` | 409 `{ roomConflict, message: "Jadvalda to'qnashuv bor — saqlanmadi", conflicts, canForce }` |
| `Update` | ⚠️ FAQAT jadval maydoni (xona, o'qituvchi, kunlar, vaqt, boshlanish/tugash sanasi) O'ZGARGANDA (`ScheduleChanged`) — bazada allaqachon to'qnashib turgan guruhning nomini tuzatish bloklanmaydi |
| `JournalService.RescheduleLessonAsync` | yangi sana/vaqt o'sha kunning HAQIQIY darslari bilan (xona yoki shu kuni darsni o'tadigan o'qituvchi) — `RescheduleConflicts`; rad matni «Bu vaqtda xona/o'qituvchi band: {guruh} {HH:mm–HH:mm}» |
| O'rinbosar tayinlash | o'zgarmadi — faqat ogohlantirish |
| `CompleteAndTransfer` (superadmin) | tekshirilmaydi: yangi guruh yopilayotgan guruhning o'rnini oladi |

- ⚠️ **Baribir saqlash — FAQAT SUPERADMIN va SABAB bilan** (`ScheduleConflicts.Decide`, hard rol
  `User.IsInRole(SuperAdmin)` — `[AdminPerm]` admin'ni ham o'tkazardi). `force=true&forceReason=...`; sababsiz —
  409 «Baribir saqlash uchun sababni yozing». Audit `Group`: «Jadval to'qnashuvi bilan saqlandi: {ro'yxat} — sabab: …».
  Boshqalar uchun `force` E'TIBORSIZ (jimgina o'tkazilmaydi). Dars ko'chirishda ham shu: faqat admin jurnali
  (`JournalController`, `RescheduleLessonRequest.Force/ForceReason`), o'qituvchi yo'li hech qachon majburlamaydi
  (`ScheduleConflictException` → 400 matn bilan).
- `GET /api/admin/schedule/conflicts` — bazada QOLGAN to'qnashuv juftliklari (`ScheduleConflicts.AllPairs`); jadval
  sahifasida «Jadvalda {n} ta to'qnashuv bor» banneri.
- Klient: `lib/scheduleConflicts.ts` (`scheduleConflictOf` — 409 ni o'qish) + `ScheduleConflictModal` — forma
  YOPILMAYDI, superadmin «Sabab» + «Baribir saqlash» ni ko'radi, xato oynaning o'zida.
