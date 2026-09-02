# "Bog'lanish kerak" (follow-up navbati) qoidalari

Migratsiya: `AddContactRequests`. Modul O'quvchilar bo'limi ostida, lekin **ruxsati alohida**.

## 1. Oqim

1. O'quvchi profili → **"⋮" → "Bog'lanish kerak"** → SABAB tanlanadi (ixtiyoriy izoh va sana bilan)
   → o'quvchi **navbatga** tushadi.
2. **Bog'lanish kerak** bo'limi (`/admin/students/boglanish`) → operator qatordan **"Bog'lanildi"**
   bosadi va uchta narsani kiritadi:
   - **Natija** — ko'tardimi (`answered` / `no_answer` / `busy` / `wrong_number` / `other`);
   - **"Javobi nima dedi"** — erkin matn (modulning asosiy ma'lumoti);
   - **Keyingi qadam** — `done` (hal bo'ldi) | `callback` (qayta qo'ng'iroq, SANA bilan) |
     `failed` (bog'lanib bo'lmadi).
3. `callback` bo'lsa talab o'sha sanada yana navbatda chiqadi; sana o'tib ketsa — **muddati o'tgan**
   (qizil, ro'yxat tepasida, alohida chip).
4. Yakunlangan talabni **qayta ochish** mumkin (yana `new` bo'ladi).

## 2. Yagona katalog — `ContactService`

Bosqichlar, natijalar va o'tish qoidalari `Application/Services/ContactService.cs` da (sof
funksiyalar, `ContactServiceTests`). Backend, navbat sahifasi va hisobot AYNAN shu kalitlarni
ishlatadi; frontend yorliqlarni `GET /api/admin/contacts/meta` dan oladi.

| Bosqich | Yorliq | Navbatdami |
|---|---|---|
| `new` | Bog'lanish kerak | ha |
| `callback` | Qayta qo'ng'iroq | ha |
| `done` | Hal bo'ldi | yo'q |
| `failed` | Bog'lanib bo'lmadi | yo'q |

⚠️ **`new` ga QAYTARIB bo'lmaydi** (`CanTransitionTo`): bog'langandan keyin boshiga qaytish
navbatni cheksiz aylantirardi va hisobotda bosqich ko'rinmasdi. Kerak bo'lsa bugungi sana bilan
`callback` tanlanadi.

⚠️ **"Bog'lanildi" ≠ "urinish"**: kunlik hisobotdagi *"nechta odam bilan bog'lanildi"* faqat
`Results[].Reached == true` bo'lgan natijalarni sanaydi (`answered`, `other`). Ko'tarmagan
qo'ng'iroq **urinish**ga kiradi, "bog'lanildi"ga emas — aks holda hisobot haqiqiy aloqani
ko'rsatmasdi.

## 3. Entitylar

| Entity | Vazifasi |
|---|---|
| `ContactRequest` | Bitta TALAB (case): o'quvchi, sabab, holat, muddat + ro'yxat uchun denormalizatsiya (`AttemptCount`, `LastResponse`, `LastActorName`, `LastActionAt`) |
| `ContactAttempt` | Har bir HODISA: `created` \| `contact` \| `note` \| `reopen`. **Hisobotlar AYNAN shundan** hisoblanadi |

**SNAPSHOT maydonlar** (`StudentName`, `ReasonLabel`, `ActorName`) — ataylab takrorlangan:
o'quvchi arxivlansa, sabab katalogi tahrirlansa yoki xodim o'chsa ham tarix va hisobot buzilmasin.

`ContactAttempt.Date` ("yyyy-MM-dd") — kunlik hisobot AYNAN shu ustun bo'yicha guruhlanadi
(ISO vaqtdan `Substring` bilan guruhlash indeksdan foydalana olmasdi).

## 3.5. KO'PLAB QO'SHISH (o'quvchilar ro'yxatidan)

"O'quvchilar ro'yxati"da bir nechtasini belgilab **«Bog'lanish kerak»** bosiladi — sabab/izoh/
sana BIR marta tanlanadi va hammasiga birdek qo'llanadi. `POST /api/admin/contacts/bulk`
(`MaxBulk = 500`).

⚠️ Ochiq talabi bor o'quvchi **CHETLAB O'TILADI**, butun amal to'xtamaydi — javobda
`created` / `skipped` / `skippedNames` / `notFound` qaytadi. Aks holda 100 ta tanlangandan
bittasi tufayli hech kim navbatga tushmasdi.

Bitta o'quvchi uchun ham (profil "⋮") AYNAN shu endpoint ishlatiladi — `NeedContactModal`
`students: ContactTarget[]` qabul qiladi, ya'ni qoida ikki joyda ayri ketmaydi. Talab
yaratish mantig'i controllerda bitta `AddRequest` yordamchisida (`POST /contacts` ham,
`/bulk` ham shuni chaqiradi).

## 3.6. MUDDAT GURUHLARI — "bugun kimga qo'ng'iroq kerak?"

Operatorning asosiy savoli BOSQICH emas, VAQT. Qoida `ContactService.BucketOf` da (sof
funksiya, testlangan) — `today` PARAMETR sifatida uzatiladi, ichkarida `AppClock` o'qilmaydi.

| Guruh | Nima |
|---|---|
| `todo` | **BUGUN QILISH KERAK** = `overdue` + `today` + `nodate` |
| `overdue` | Qayta qo'ng'iroq sanasi bugundan OLDIN |
| `today` / `tomorrow` | Bugun / ertaga |
| `week` | Bugundan +2..+7 kun |
| `later` | +7 kundan keyin |
| `nodate` | Sana belgilanmagan (`new` holati) — hoziroq navbatda |

⚠️ `todo` ga **kechikkanlar ham, sanasizlar ham** kiradi — aks holda operator "bugun 5 ta"
deb ko'rib, kechagi 12 tasini ko'rmay qolardi.

⚠️ Sanasiz `callback` bo'lmasligi kerak (server sanani talab qiladi), lekin eski/qo'lda
tuzatilgan yozuv shunday bo'lsa **`nodate` ga tushadi** — yo'qolib ketmaydi. Buzuq sana
(`2026-13-99`) `later` ga tushadi va navbatda ko'rinadi.

**API:** `GET /contacts?due=<guruh>` yoki `?dueDate=YYYY-MM-DD` (aniq kun). `GET /contacts/meta`
javobida `due` (kesim) va `days` (yaqin 14 kun rejasi, faqat ish BOR kunlar) qaytadi.
SQL tarjimasi controllerda, QOIDA esa `ContactService` da — ikkisi bir xil bo'lishi shart.

**UI — KANBAN TAXTASI** (`ContactBoard` · `ContactColumn` · `ContactCard`): navbat tepasida
to'rtta katta raqam (Bugun qilish kerak · Muddati o'tgan · Bugun · Ertaga — ular ayni paytda
FOKUS tugmalari: bosilsa taxta o'sha guruhga toraytiriladi), ostida bitta asboblar paneli va
taxta.

Batafsil: §3.65.

Kalendar komponenti — `components/ui/MonthDayStrip.tsx` (sana funksiyalari `lib/month.ts` da:
komponent va oddiy funksiyalar bir faylda ARALASHMAYDI — eslint
`react-refresh/only-export-components`). U "Izohlarga javoblar" sahifasida ham ishlatiladi.

Kalendar: OY TO'LIQ chiqadi (bo'sh kunlar ham o'z o'rnida — ilgari faqat "ish bor" kunlar
ko'rsatilib, ro'yxat sakrab turardi), strelkalar bilan oy almashadi. Boshqa oyga o'tilganda
tanlangan kun tozalanadi — u oyda "bugun" yo'q, tasodifiy kun tanlab qo'yish chalg'itardi.

⚠️ **KALENDAR ENDI YIG'ILGAN va BUGUN AVTOMATIK TANLANMAYDI.** Ilgari sahifa ochilganda
`dueDate = bugun` qo'yilardi, ya'ni operator FAQAT bugunga rejalashtirilgan qayta
qo'ng'iroqlarni ko'rardi — muddati o'tganlar ham, yangi (sanasiz) talablar ham ro'yxatga
kirmasdi, holbuki §3.6 bo'yicha aynan ular "bugungi ish". Endi taxtaning O'ZI "qachon"
savoliga javob beradi (har muddat guruhi alohida ustun), kalendar esa ATAYIN qidiruv asbobi:
"Kalendar" tugmasi bilan ochiladi va kun tanlansagina filtrlaydi.

⚠️ **BUGUNGI katak `todo` sonini ko'rsatadi** (muddati o'tgan + bugungi + sanasiz), qolgan
kunlar esa aynan o'sha kunga rejalashtirilganini. Sabab: "bugun" operator uchun "bugungi
ish" degani — kechagi kechikkanlar ham unga kiradi. Chiziq ostida shu izoh yozib qo'yilgan.

`GET /contacts/meta?month=yyyy-MM` — kunlik reja shu oy uchun qaytadi (chiplar/sanoqlar
oyga BOG'LIQ EMAS, ular har doim joriy holat).

## 3.65. TAXTA (kanban) — ko'rinish, filtrlar va SUDRASH

Sahifa `/admin/students/boglanish` → "Navbat" tabi. Lidlar taxtasi bilan BIR XIL texnika:
`@dnd-kit/core` + `.kanban` / `.kanban-col` / `.kanban-col-body` CSS klasslari.

### Guruhlash — ikki rejim (tanlov brauzerda eslab qolinadi)

| Rejim | Ustunlar | Qachon |
|---|---|---|
| **Muddat** (standart) | Muddati o'tgan · Bugun · Sanasiz · Ertaga · Shu hafta · Keyinroq — **HISOBLANADI** (sanadan), tahrirlanmaydi | "Bugun kimga qo'ng'iroq qilaman" — §3.6 dagi asosiy savol |
| **Bosqich** | `ContactStage` jadvalidan — foydalanuvchi **qo'shadi/tahrirlaydi/o'chiradi/tartiblaydi** (§3.66) | "Talab qayerda turibdi va qanday yakunlandi" |

⚠️ Ustunlarni boshqarish FAQAT "Bosqich" rejimida. Muddat ustunlari — ma'lumot emas, sanadan
HISOBLANADIGAN natija; ularni tahrirlash mumkin bo'lgan narsa emas.

⚠️ **RANG HAR IKKI REJIMDA ham SHOSHILINCHLIKNI bildiradi**, ustunni emas — shuning uchun
"Bosqich" rejimida "Qayta qo'ng'iroq" ustunidagi KECHIKKAN karta ham qizil bo'lib ko'zga
tashlanadi. Aks holda bosqich rejimiga o'tgan operator muddati o'tganlarni ko'rmay qolardi.

⚠️ "Bosqich" rejimi tanlanganda QAMROV avtomatik "Hammasi" ga o'tadi (saqlangan tanlov bilan
sahifa ochilganda ham) — aks holda yakuniy ikki ustun DOIM bo'sh turardi.

⚠️ "Muddat" rejimida yakuniy talabning ustuni YO'Q (`bucketOf` bo'sh qaytaradi). "Hammasi"
qamrovida ular ro'yxatga tushadi, lekin taxtada chizilmaydi — shuning uchun pastdagi sanoq
`boardItems` dan hisoblanadi, aks holda "talab yo'qolgan"dek tuyulardi.

### ⚠️ SUDRASH HOLATNI JIMGINA O'ZGARTIRMAYDI

Serverda kartani ustundan ustunga ko'chiradigan endpoint **YO'Q va bo'lmaydi**: har o'tish —
HODISA (`ContactAttempt`), ya'ni "kim, qachon, nima dedi" yozilishi SHART (§2, §7). Karta
tashlanganda **"Bog'lanildi" oynasi keyingi qadam OLDINDAN TANLANGAN holda ochiladi**
(`presetNextStatus` / `presetDueDate`), operator faqat natijani va javobni yozadi.

- Qabul QILMAYDIGAN ustunlar: **"Bog'lanish kerak"** va **"Sanasiz"** — `new` ga qaytarish
  serverda ham taqiqlangan (`ContactService.CanTransitionTo`). Ular sudrash paytida
  xiralashadi, ya'ni foydalanuvchi server rad etadigan amalni BOSHLAY olmaydi.
- Yakuniy (done/failed) karta **sudralmaydi** — `attempt` faqat ochiq talabga yoziladi.
- Muddat ustunlari `callback` + o'sha guruhning boshlang'ich sanasini taklif qiladi
  (`BoardColumn.drop.offsetDays`), sana oynada o'zgartirilishi mumkin.

### `lib/contactDue.ts` — ustunlar katalogi va BUCKET nusxasi

⚠️ `bucketOf` — serverdagi `ContactService.BucketOf` ning **AYNAN nusxasi**. Nusxa ATAYIN:
taxta butun navbatni BITTA so'rovda oladi (`limit=500`) va ustunlarga klientda bo'ladi;
server bir so'rovda faqat bitta guruhni qaytara oladi (`?due=`), ya'ni olti ustun = olti
so'rov bo'lardi. **Serverdagi qoida o'zgarsa shu faylni ham o'zgartiring** —
`lib/__tests__/contactDue.test.ts` ikkisining bir xilligini qulflaydi.

### Filtrlar — bittadan boshqasi KLIENTDA

Serverga faqat **QAMROV** (`status` = ochiqlar / hammasi) va kalendar OYI uzatiladi. Qidiruv,
sabab, "kim yuborgan" va fokus — kelgan ro'yxat ustida, ya'ni bir zumda ishlaydi va so'rov
yubormaydi.

- **Qidiruv JONLI** (Enter bosish shart emas) va serverdagidan KENG: server faqat ism va
  sabab bo'yicha qidiradi, klient esa izoh, oxirgi javob, yuborgan xodim va TELEFON
  raqamlari bo'yicha ham (raqamlar bo'yicha — "901234567" ham "+998 90 123 45 67" ni topadi).
- **Sabab** (`ActionReasons`, kategoriya `contact`) va **"Kim yuborgan"** — YANGI filtrlar;
  ikkinchisi kelgan ma'lumotdagi `createdBy` dan quriladi (alohida so'rov yo'q).
- ⚠️ Muddat va Holat chiplari **OLIB TASHLANDI**: ular bir-birini tozalardi va nima
  tanlangani ko'rinmasdi. Muddat endi USTUN, holat esa qamrov tugmasi.

### `lib/contactDue.ts` — MUDDAT ustunlari

`DUE_COLUMNS` — muddat rejimidagi olti ustun. `STATUS_COLUMNS` esa faqat **ZAXIRA**: server
ustunlarni bermasa (eski backend yoki so'rov xatosi) taxta baribir ishlashi kerak. Odatda
bosqich ustunlari serverdan keladi va `stageColumns()` bilan quriladi.

### ⚠️ FAQAT KANBAN — ro'yxat ko'rinishi YO'Q

Ilgari "Taxta / Ro'yxat" almashtirgichi bor edi; u **OLIB TASHLANDI** — navbat sahifasi
bitta ko'rinishga ega. Sabab: ikkita ko'rinish bir xil ma'lumotni ikki xil chizib, ikkalasini
ham qo'llab-quvvatlashni talab qilardi, holbuki taxta tor ekranda ham ishlaydi (ustunlar
gorizontal aylanadi, sahifa emas). Yangi ko'rinish rejimi QO'SHMANG.

## 3.66. USTUNLARNI BOSHQARISH — `ContactStage` (migratsiya `AddContactStages`)

Foydalanuvchi taxtaga o'z ustunini qo'shadi ("Bog'lanildi", "SMS yuborildi", "Ota-onasi bilan
gaplashildi"), nomini/rangini tahrirlaydi, tartibini o'zgartiradi va o'chiradi — lidlar
taxtasidagi `LeadStage` bilan bir xil ish yo'li.

### ⚠️ ENG MUHIM QOIDA — ustun HOLATNI ALMASHTIRMAYDI

Lidda bosqich hech narsaga ta'sir qilmaydi, bu yerda esa `ContactRequest.Status` **butun
mantiqni** boshqaradi: navbat (ochiq/yopiq), muddat guruhlari (`BucketOf`), o'tish qoidalari
(`CanTransitionTo`), "bitta ochiq talab" qoidasi va **BARCHA hisobotlar** (`ContactReport`,
jurnal, AI tahlil — §7). Erkin ustunlar bularning hammasini buzardi.

Shuning uchun ustun holatga **BOG'LANADI**: har `ContactStage` da `BaseStatus` bor va u
to'rtta bazaviy holatdan biri (`new`/`callback`/`done`/`failed`).

⚠️ **Natijada hisobotlar O'ZGARMAYDI**: nechta ustun qo'shilsa ham `/stats`, jurnal va AI
avvalgidek `Status` bo'yicha sanaydi. Yangi ko'rsatkich qo'shsangiz — `StageId` ga
**QARAMANG** (bu «Aktiv muzlatish» §1 dagi bilan bir xil printsip: ko'rinish qatlami
mantiqqa aralashmaydi).

### «Ustun qo'shish» QAYERDA

Ikki joyda, ikkalasi ham BITTA `openStageForm` ni chaqiradi:

1. **Sahifa sarlavhasida, «Yangilash» yonida** — asosiy yo'l. ⚠️ U **har doim** ko'rinadi
   (standart "Muddat" rejimida ham) va kerak bo'lsa O'ZI "Bosqich" rejimiga o'tkazadi.
   Sabab: taxta oxiridagi "+" ustuni gorizontal aylantirishning narigi chetida qolib
   ketardi va foydalanuvchi tugmani **umuman topa olmasdi** (aynan shu xato bo'lgan).
2. Taxta oxiridagi punktir **"+" ustuni** — lidlar taxtasidagi odat saqlanadi.

### TIZIM ustunlari

Har bazaviy holat uchun **AYNAN bitta** tizim ustuni bor va uning **Id'si holat kalitining
o'zi** (`"new"`, `"callback"`, `"done"`, `"failed"`).

- Seed — `Program.cs` da, **idempotent** (yo'q bo'lgani qo'shiladi), lidlardagi bilan bir xil naqsh.
- ⚠️ `StageId` **BO'SH** bo'lishi ODDIY holat: talab o'z holatining tizim ustunida turibdi.
  Shuning uchun eski qatorlarni to'ldirish (backfill) **KERAK EMAS** va ustun o'chirilsa ham
  karta taxtadan **yo'qolmaydi** (klientda `stageKeyOf`, serverda `Stages` sanog'i).
- Tizim ustuni **o'chirilmaydi** va `BaseStatus` i **o'zgartirilmaydi** (nomi va rangi — bemalol):
  u o'z holatidagi kartalar uchun doimiy "uy".

### SUDRASH — ikki xil, farqi PRINSIPIAL

| Holat | Nima bo'ladi |
|---|---|
| Maqsad ustunning `BaseStatus` i **BIR XIL** | Oddiy siljish — `POST {id}/stage`, DARHOL saqlanadi, oyna so'ralmaydi |
| `BaseStatus` **BOSHQA** | "Bog'lanildi" oynasi keyingi qadam va ustun OLDINDAN tanlangan holda ochiladi |

⚠️ Ikkinchisi shart, chunki har HOLAT o'zgarishi hodisa (`ContactAttempt`) bo'lishi kerak —
"kim, qachon, nima dedi" yozilmasa hisobot yolg'on chiqardi (§2).

⚠️ Ustun ko'chirish ham tarixga yoziladi, lekin **`note` turi bilan** — hisobotlardagi
"urinish"/"bog'lanildi" sonlari FAQAT `contact` turini sanaydi, ya'ni ustun ko'chirish
raqamlarni **buzmaydi**.

⚠️ Ustun QABUL QILISHI sudralayotgan KARTAGA bog'liq (`ContactBoard.acceptsActive`): `new` ga
bog'langan ustun boshqa bosqichdagi kartani qabul qilmaydi (serverda `new` ga qaytish
taqiqlangan), lekin **o'sha bosqichdagi** kartani qabul qiladi — u holat o'zgarishi emas.
Qabul qilmaydigan ustun sudrash paytida xiralashadi.

### Holat o'zgarsa — ustun TOZALANADI

`StageId` `""` ga qaytadi (talab yangi holatining tizim ustuniga tushadi): `attempt` da
(sudralgan ustun mos kelsa — o'sha ustun), `reopen` da, va ustunning `BaseStatus` i
tahrirlanganda (ichidagi talablar unga ZID bo'lib qolmasin).

### ⚠️ SABAB ≠ USTUN (ikkisi MUTLAQO bog'liq emas)

"Sabablar" katalogiga (`ActionReason`, kategoriya `contact`) yangi sabab qo'shilsa **ustun
PAYDO BO'LMAYDI** va ustunlar soni o'zgarmaydi. Ular boshqa-boshqa narsalar:

| | Sabab (`ActionReason`) | Ustun (`ContactStage`) |
|---|---|---|
| Savol | **NEGA** bog'lanamiz | Talab **QAYERDA** turibdi |
| Qayerdan boshqariladi | O'quv bo'limi → Sabablar | Taxtadagi «Ustun qo'shish» |
| Taxtada nima bo'ladi | "Sabab" FILTRIDA variant + kartada kulrang matn | ALOHIDA ustun |

`ContactStage` faqat IKKI joyda yaratiladi: `Program.cs` dagi tizim seed'i va
`POST contacts/stages` (qo'lda qo'shish). Sabab esa navbat mantiqiga faqat `ResolveReasonAsync`
orqali tegadi — u sababning MATNINI talabga snapshot qilib yozadi, xolos.

Sabab bo'yicha guruhlash (uchinchi rejim) hozircha YO'Q — kerak bo'lsa alohida ish sifatida
ko'rib chiqilsin.

### Endpointlar (hammasi `ContactsController` da — ruxsat `contacts` avtomatik meros)

`GET stages` · `POST stages` · `PUT stages/{id}` · `DELETE stages/{id}` ·
`PATCH stages/reorder` · `POST {id}/stage`.

⚠️ **O'chirish**: tizim ustuni — 400; ichida talab bor ustun — 400 (soni bilan). Lidlardagi
"jimgina yetim qoldirish va keyingi restartda tuzatish" bu yerda **ATAYIN qilinmadi**.

Qoidalarning sof qismi — `ContactService` (`SystemStages`, `CanAnchorTo`, `StageMatches`,
`SafeColor`), testlari `ContactServiceTests` da; klient tomoni `lib/contactDue.ts`
(`stageColumns`, `stageKeyOf`) va `contactDue.test.ts`.

## 3.7. GURUH JURNALIDAGI "ALOQA" TABI (o'qituvchi ham, admin ham)

Guruh jurnalida **"Aloqa"** tabi: guruh o'quvchilari ro'yxati, tanlab (yoki qatordagi tugma
bilan bittasini) **"Bog'lanish kerak"** navbatiga yuboriladi. Sabab va izoh tanlanganlarning
HAMMASIGA bir xil qo'yiladi.

⚠️ **SANA SO'RALMAYDI** — talab darhol navbatga tushadi (`new`, bugungi ish). Rejalashtirish
(qayta qo'ng'iroq sanasi) — operatorning ishi, o'qituvchining emas.

| Tomon | Sahifa | API |
|---|---|---|
| Admin/xodim | `ClassDetailPage` → "Aloqa" tabi (`contacts:create`) | `POST /admin/contacts/bulk` |
| O'qituvchi | `TeacherGroupDetailPage` → "Aloqa" tabi | `POST /api/teacher/groups/{classId}/contacts` |

Ko'rinish BITTA komponentda — `components/contacts/GroupContactTab.tsx` (API farqi
`loadReasons`/`onSend` orqali tashqaridan beriladi).

**XAVFSIZLIK (o'qituvchi):** guruh o'qituvchiniki ekani tekshiriladi (`Teaches`) VA faqat
SHU guruhning faol a'zolari qabul qilinadi (`allowedStudentIds`) — aks holda o'qituvchi
id yozib begona o'quvchini navbatga qo'sha olardi. Sabablar katalogi o'qituvchiga alohida
endpointdan (`GET /api/teacher/contact-reasons`) — admin endpointi unga yopiq.

**YAGONA MANBA:** talab yaratish mantig'i `ContactQueueService` da (Application/Services) —
admin va o'qituvchi controllerlari AYNAN shuni chaqiradi, ya'ni "bitta ochiq talab" qoidasi,
hodisa yozuvi va audit izi ikki joyda ayri ketmaydi.

Navbat ro'yxatida **"Yuborgan: <ism>"** ko'rinadi (`CreatedBy`) — jurnaldan yuborilgan bo'lsa
bu o'qituvchining ismi, ya'ni operator kimning iltimosi bilan qo'ng'iroq qilayotganini biladi.

## 4. BITTA OCHIQ TALAB qoidasi

Bir o'quvchida bir vaqtda faqat **bitta** ochiq talab (`new`/`callback`) bo'ladi — aks holda navbat
bir xil odam bilan to'lib ketardi. `POST /api/admin/contacts` ikkinchisini ochmaydi: 400 qaytaradi
va javobda `existingId` beradi, UI esa navbatga havola ko'rsatadi. `reopen` da ham shu tekshiruv bor.

Yangi sabab paydo bo'lsa — yangi talab emas, mavjud talabga **izoh** (`POST {id}/note`) qo'shiladi.

## 5. RUXSAT — `contacts`

`[AdminPerm("contacts", ReadRequiresPerm = true)]`.

- O'quvchilar bo'limidan **ATAYIN alohida**: navbat bilan ishlaydigan operatorga o'quvchilar
  bo'limini to'liq ochish shart emas ("Kassa" "Moliya"dan alohida bo'lgani bilan bir xil mantiq).
- `ReadRequiresPerm = true` — javobda o'quvchi ismi va **telefonlari** qaytadi, GET'ni odatdagidek
  har qanday xodimga ochib bo'lmaydi.
- Amallar: `contacts:create` — talab ochish ("⋮" tugmasi), `contacts:edit` — bog'lanildi/izoh/qayta
  ochish, `contacts:delete` — talabni o'chirish.

### ⚠️ HISOBOT — FAQAT SUPERADMIN (navbat esa hammaga)

Sahifa ikki tabli va ular RUXSAT jihatidan AJRATILGAN:

| Tab | Kim ko'radi |
|---|---|
| **Navbat** (taxta, ustunlar, "Bog'lanildi") | superadmin · admin · `contacts` ruxsati berilgan xodim |
| **Hisobot** (stats · jurnal · javoblar · AI tahlil) | **FAQAT superadmin** |

⚠️ **Buni ruxsat KALITI bilan ifodalab BO'LMAYDI.** `AdminPermAttribute` da to'liq huquqli
rollar cheklovsiz o'tadi ("to'liq huquqli rollar — cheklovsiz"), klientda esa `can()`
`permissions == null` bo'lganda har doim `true` qaytaradi — ya'ni istalgan yangi kalit oddiy
`admin` ni ham o'tkazib yuborardi. Shuning uchun HARD rol tekshiruvi
(`.claude/rules/year-freeze.md` §3 dagi bilan bir xil sabab):

- **Server:** `ContactsController.MaySeeReports => User.IsInRole(Roles.SuperAdmin)`, beshta
  hisobot endpointining boshida `403` (jim bo'sh ro'yxat EMAS — sabab yoziladi):
  `GET stats` · `GET journal` · `GET responses` · `GET ai-analyses` · `POST ai-analysis`.
- **Klient:** `user?.role === 'superadmin'` — "Hisobot" tabi umuman chizilmaydi va
  `?tab=hisobot` bilan kelingan havola ham navbatga tushiradi (xatcho'p sahifani buzmasin).

⚠️ **Yangi ruxsat kaliti QO'SHILMAGAN** — `adminPermissions` katalogi va `PermissionCatalogTests`
tegilmaydi. Amal delegatsiya qilinmaydi: foydalanuvchi aniq "faqat superadmin" dedi. Kerak
bo'lsa keyinchalik `superOrGranted` ("superadmin yoki ruxsat berilgan xodim") ga o'tkazish mumkin.

⚠️ **Hisobotlar hub'i ham filtrlanadi** — `config/reports.ts` dagi band `superadminOnly: true`
bilan belgilangan, `visibleReportGroups(canSee, isSuperAdmin)` uni rolsizlarga ko'rsatmaydi va
uning kaliti `reportPerms` ga KIRMAYDI (aks holda faqat `contacts` ruxsati bor operator menyuda
"Hisobotlar"ni ko'rib, ichidan hech narsa topmasdi). Qulflovchi testlar: `reports.test.ts` →
`superadminOnly — faqat superadmin ko'radigan hisobotlar`.
- Nav: "O'quvchilar" guruhining O'ZIDA `perm` YO'Q (bolalarga ko'chirilgan) — aks holda faqat
  `contacts` berilgan operator guruhni umuman ko'rmasdi.

## 6. Sabablar

**O'quv bo'limi → Sabablar** sahifasida "Bog'lanish kerak" kartochkasi, kategoriya **`contact`** (`ContactService.ReasonCategory`,
`ActionReasonsController.Categories` ro'yxatida). Sabab **ixtiyoriy** — bo'sh bo'lsa hisobotda
"— sababsiz —" guruhiga tushadi.

⚠️ **KATEGORIYA IKKI JOYDA EDI va DRIFT bo'ldi:** backendda `contact` bor edi, `ReasonsPage.tsx`
dagi `CATEGORIES` da esa YO'Q — natijada admin sabab qo'sha olmas, tanlash ro'yxati doim bo'sh
chiqardi (xuddi shu sabab `archive_student` ham ko'rinmasdi). Endi kartochkalar
**`GET /api/admin/action-reasons/categories`** dan quriladi: frontenddagi ro'yxat faqat
SARLAVHA/IKONKA beradi, yorlig'i topilmagan kategoriya baribir (kalit nomi bilan) ko'rinadi —
bo'shliq jimgina yo'qolmaydi.

## 7. Hisobotlar (`GET /api/admin/contacts/stats`)

Barcha sonlar **hodisalardan** (`ContactAttempt`), ya'ni "kim nima qildi" bo'yicha:

- **Kunlik** — har kuni: yangi talab, urinish, bog'lanildi, hal bo'ldi, qayta qo'ng'iroq, bo'lmadi;
- **Xodimlar kesimi** — "kim qaysi bosqichga oldi, natijasi qanday bo'ldi";
- **Sabablar kesimi** — talab OCHILGAN sana bo'yicha (urinish emas);
- **Natijalar kesimi** — ko'tarmagan/band ulushi (aloqa sifati).

`OpenNow`/`OverdueNow` — davrga bog'liq EMAS, joriy holat (navbat sanoqlari bilan bir xil bo'lsin).

**Davr tanlash** — navbatdagi bilan BIR XIL oylik kalendar: kun bosilsa o'sha kun hisoboti,
«Butun oy» — butun oy, tez oraliqlar (7/30/90 kun) va qo'lda `from`/`to` ham bor.
Ochilganda BUGUN tanlangan. Kalendar kataklaridagi sonlar (o'sha kundagi urinishlar) BUTUN
OY uchun ALOHIDA yengil so'rovdan keladi — aks holda bitta kun tanlanganda kalendar
bo'shab qolardi.

### 7.1. KUNLIK JURNAL — "bugun kimga qo'ng'iroq qilindi"

Jadvallar "NECHTA" ga javob beradi, jurnal esa KUNNING O'ZINI ko'rsatadi: rahbar bir kunni ochib,
o'sha kuni nima bo'lganini boshdan-oxir o'qiy oladi — **kimga**, **qachon** (soati bilan),
**nima dedi**, **qaysi sabab** bilan va **kim** qildi.

**`GET /api/admin/contacts/journal?from&to&type&limit`** → kunlar ro'yxati (`ContactJournalDayDto`),
har kunda jamlanma + hodisalar (`ContactJournalItemDto`).

- **Kunlar YANGISIDAN eskisiga, kun ICHIDA esa ertalabdan kechgacha** — jurnal xronologik o'qilsin
  (qolgan ro'yxatlar "eng yangisi tepada" bo'lgani bilan bu yerda kun ichida teskarisi mantiqiy).
- `type` — `contact` | `created` | `note` | `reopen` (vergul bilan bir nechtasi). **Noma'lum tur
  JIM tashlanadi** — klientdagi xato kalit tufayli jurnal butunlay bo'shab qolmasin.
  UI'da ikki rejim: «Qo'ng'iroqlar» (default, `type=contact`) va «Barcha amallar».
- Chegara **500** (max `MaxJournalItems` = 2000) va u **ENG YANGI** hodisalardan olinadi: bitta kun
  tanlanganda uning hammasi kiradi, uzun davrda esa qirqilgani ro'yxat ostida ochiq yoziladi.
- Qatorda telefon raqamlari ham bor (`tel:` havolasi) — operator jurnaldan darhol qayta qo'ng'iroq
  qila oladi. Javob yozilmagan urinish "javob yozilmagan" deb KO'RSATILADI (jimgina bo'sh qolmaydi).

⚠️ Hisob-kitob `ContactReport` da (Application/Services) — **`GET /stats` ham AYNAN shu funksiyadan
foydalanadi**. Sabab: bir xil raqam uch joyda (hisobot, jurnal, AI) kerak; ayri hisoblansa
"AI boshqa son ko'rsatyapti" holati kelib chiqardi.

## 7.5. JAVOBLAR TAHLILI ("nima deb yozilgan")

Sonlar "nechta" ga javob beradi, bu bo'lim esa **"NIMA deyilgan"** ga:

- **`GET /api/admin/contacts/responses`** — javoblar lentasi. Faqat `Type=contact` VA
  `Response != ""` qatorlar (bo'sh javoblar lentani suyultirib, o'qishni qiyinlashtirardi).
  Filtrlar: davr, natija, xodim, matn ichidan qidiruv. Chegara 500 (default 200).
- **`ContactStatsDto.TopWords`** — javoblarda eng ko'p uchragan so'zlar
  (`ContactService.TopWords`, sof funksiya, testlangan).
  - ⚠️ Bir matnda takrorlangan so'z **BIR marta** sanaladi: savol "necha marta yozildi" emas,
    "NECHTA JAVOBDA uchradi" — aks holda bitta uzun izoh butun hisobotni egallab olardi.
  - ⚠️ Apostroflar (`'` `ʻ` `’` `` ` ``) bir ko'rinishga keltiriladi — aks holda "to'lov" va
    "toʻlov" ikki xil so'z bo'lib sanalardi (matn turli klaviaturalardan kiritiladi).
  - `StopWords` ATAYIN qisqa: faqat bog'lovchi/olmosh/yordamchi so'zlar. "to'lov", "dars",
    "kasal", "kerak" QOLADI — aynan ular hisobotning ma'nosi.
- UI'da so'z bosilsa javoblar lentasi o'sha so'z bo'yicha filtrlanadi ("nega bu so'z ko'p?").

## 7.55. AI TAHLIL (Gemini) — "nima deyilyapti va nimani tuzatish kerak"

Hisobot tabida, KPI kartochkalaridan keyin va jadvallardan oldin (voronka tahlilidagi bilan bir xil
joylashuv). Servis — `ContactAiAnalysisService`, entity `ContactAiAnalysis`
(migratsiya `AddContactAiAnalysis`), panel — `components/ai/ContactAiPanel.tsx`.
Umumiy arxitektura: `.claude/rules/ai-analysis.md`.

⚠️ **DAVRGA BOG'LANGAN — boshqa AI tahlillardan asosiy FARQI.** Boshqalarida kalit `Date`, bu yerda
`(FromDate, ToDate)`: hisobotda tanlangan kun/oy/oraliq uchun tahlil qilinadi. "Kuniga bir marta"
cheklovi ham SHU davr bo'yicha — bir kunda har xil davrlarni tahlil qilish mumkin, bitta davrni
ikki marta emas. Tarix ham davr bo'yicha filtrlanadi (`GET /ai-analyses?from&to`): aks holda
ekranda boshqa davrning raqamlari ko'rinib qolardi.

⚠️ **MAXFIYLIK — promptga o'quvchi ISMI ham, TELEFONI ham TUSHMAYDI** (`ContactAiSampleDto` da
bunday maydon umuman yo'q). Savol "kim" emas, "NIMA deyilyapti va natija qanday". Xodim ismi esa
qoladi — "kim qanday ishlayapti" tahlilning maqsadli qismi va bu ichki ma'lumot.

- Promptga: jamlanma sonlar + kunlik oqim + xodimlar/sabablar/natijalar kesimi + eng ko'p uchragan
  so'zlar + **javob matnlaridan namunalar** (eng yangi 120 ta, har biri 300 belgigacha).
- Baholar: `qamrov · aloqa · natija · sifat · umumiy`.
  Narrativ: `umumiy, sabablar, javoblar, sifat, xodimlar, ozgarishlar, kuchli[], zaif[],
  xavflar[], tavsiyalar[], trend`.
- ⚠️ **BO'SH DAVRDA Gemini UMUMAN chaqirilmaydi** (`attempts == 0 && created == 0`) — bo'sh
  davrdan xulosa chiqmaydi, so'rov puli esa baribir ketardi. Bu tekshiruv **API kaliti
  tekshiruvidan ham oldin**: u foydalanuvchining TANLOVI haqidagi xato ("boshqa davrni tanlang"),
  kalit esa sozlama muammosi.
- Yaratish — `contacts:create` (POST), o'qish — bo'lim ruxsati (sinf darajasida
  `ReadRequiresPerm = true`). Auditga YOZILMAYDI (tahlil ma'lumotni o'zgartirmaydi).
- Testlar: `IntellectCRM.Tests/ContactReportTests.cs`.

## 7.6. O'QUVCHI PROFILIDA

`GET /contacts/student/{id}` **tarixni ham** qaytaradi (ro'yxat endpointi qaytarmaydi — bu bitta
o'quvchi uchun ikkita yengil so'rov). O'quvchi profilining **"Aloqa"** tabida:

- **"Bog'lanish tarixi"** — talab darajasi: sabab, bosqich, muddat ("NIMA UCHUN va qaysi
  bosqichda");
- **"Qo'ng'iroqlar tarixi"** — ARALASH lenta: haqiqiy qo'ng'iroqlar (Local Call) va bog'lanish
  javoblari BITTA vaqt o'qida, eng yangisi tepada.

⚠️ Javob matni **faqat lentada** chiqadi, yuqoridagi bo'limda TAKRORLANMAYDI — aks holda bir
xil matn bitta tabda ikki marta ko'rinardi. Sabab: operator qo'ng'iroq qiladi, keyin "javobi
nima dedi" ni yozadi — bular bitta hodisaning ikki tomoni, ayri ro'yxatlarda bir-biridan
uzoqda tushib qolardi.

## 8. Audit

`ContactRequest` turi `AuditSections` da **`contacts`** ("Bog'lanish kerak") bo'limiga xaritalangan
— talab ochish, bog'lanish, izoh, qayta ochish va o'chirish "O'zgarishlar tarixi"da ko'rinadi.
Batafsil: `.claude/rules/audit.md`.
