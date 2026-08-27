# Dars jadvali qoidalari

"O'quv bo'limi → Dars jadvali" (`/admin/jadval`, ruxsat `schedule.timetable`).
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
marta band qilingan bo'lishi mumkin — `RoomConflictService` to'qnashuvni faqat OGOHLANTIRADI,
rad etmaydi. Birlashtirmasak "10:30 → 10:00" kabi manfiy teshik chiqardi.

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
