# "Hisobotlar" bo'limi qoidalari

Barcha analitika/hisobot yuzalari BIR joyda: yon menyudagi **Hisobotlar** guruhi va
`/admin/hisobotlar` hub sahifasi. Migratsiya KERAK EMAS — bu faqat NAVIGATSIYA qatlami.

## 1. YAGONA MANBA — `config/reports.ts`

Ro'yxat FAQAT shu faylda. Undan IKKALASI quriladi:

| Nima | Qayerdan |
|---|---|
| Yon menyudagi "Hisobotlar" guruhi (`navigation.ts`) | `navReports` (`inNav: true` bo'lganlar) |
| `/admin/hisobotlar` hub sahifasi (`ReportsPage`) | `visibleReportGroups(...)` — HAMMASI |

⚠️ Menyuga bandni QO'LDA yozmang. Katalogga qo'shilgan hisobot menyuda paydo bo'lmasa —
foydalanuvchi uchun u "yo'qolgan" bo'lib ko'rinadi, va teskarisi. `reports.test.ts` menyu
katalog bilan bir xilligini qulflaydi.

## 2. `inNav` — nima MENYUGA tushadi, nima faqat hub'da qoladi

- **`inNav: true`** — SOF hisobot: sahifada hech qanday yozuvchi amal yo'q. U eski
  bo'limidan **KO'CHIRILDI** (masalan "Kurslar analitikasi" O'quv bo'limidan chiqdi).
- **`inNav` yo'q** — ichida amal bor sahifa (Moliya — tranzaksiya kiritish, O'quvchilar
  davomati — SMS, Kitoblar — qaytarish). U **o'z operativ bo'limida QOLADI**, hub'da esa
  havola bo'lib turadi.

⚠️ Sabab: kundalik ish yo'li uzilmasligi kerak. Kassir "Moliya"ni "Hisobotlar" ichidan
qidirib yurmasin.

⚠️ **Menyudan ko'chirganda eski joyidan O'CHIRING.** Ikki joyda turgan band "olib kirish"ni
chala qilib ko'rsatadi; test (`ko'chirilgan hisobotlar ESKI menyu guruhlarida qolmagan`) buni
ushlaydi.

## 3. MARSHRUTLAR O'ZGARMAYDI

Katalog mavjud sahifalarga YO'L ko'rsatadi, ularni ko'chirmaydi. Shuning uchun eski havolalar,
xatcho'plar, `CardTabs` (`sectionTabs.ts`) va sahifalar orasidagi ichki linklar ishlayveradi.
`reports.test.ts` har bir `to` uchun `App.tsx` da marshrut BORLIGINI tekshiradi.

## 4. Sahifa ICHIDAGI hisobotga havola — `?tab=`

Ba'zi hisobot alohida sahifa emas, tab (Moliya → "Bonus", Kitoblar → "Analitika",
"Bog'lanish kerak" → "Hisobot"). Ular uchun boshlang'ich tab manzildan o'qiladi —
`lib/tabParam.ts` (`tabFromUrl`), `useState` initializer'ida.

- Parametrsiz havola foydalanuvchini sahifaning birinchi (OPERATIV) tabiga tashlab ketardi.
- ⚠️ FAQAT boshlang'ich qiymat: keyin tab almashtirilsa manzil O'ZGARMAYDI — har bosishda
  tarixga yozuv qo'shilsa "orqaga" tugmasi sahifadan chiqmay, tablar orasida aylanardi.
- Noma'lum qiymat JIM e'tiborsiz (`fallback`) — eskirgan havola sahifani buzmasin.

Hozir qo'llab-quvvatlaydiganlar: `FinancePage` · `BookSalesPage` · `ContactQueuePage`.
Yangi tab-hisobotga havola bersangiz — o'sha sahifaga ham `tabFromUrl` qo'shing.

## 5. RUXSAT

Hisobotlarning ruxsatlari ARALASH (`leads.stats`, `finance.main`, `audit`, `contacts` ...),
shuning uchun bo'limning O'Z kaliti YO'Q:

- **Menyuda** — guruh `permAny: reportPerms` (katalogdagi barcha kalitlar): birorta hisoboti
  bo'lmagan xodimga bo'lim umuman ko'rinmaydi.
- ⚠️ **`superadminOnly: true`** — ruxsat kaliti YETMAYDIGAN holat. `can()` admin uchun ham
  `true` qaytargani sababli "faqat superadmin" ni kalit bilan ifodalab bo'lmaydi; shuning uchun
  bandda alohida bayroq bor va `visibleReportGroups(canSee, isSuperAdmin)` uni rol bo'yicha
  filtrlaydi. Bunday bandning kaliti **`reportPerms` ga kirmaydi** (menyu bo'sh bo'lim
  ko'rsatmasin) va u **`inNav` bilan ishlatilmaydi** (Sidebar rolni bilmaydi) — ikkalasi ham
  `reports.test.ts` da qulflangan. Hozir shunday band bitta: "Bog'lanish hisoboti".
- **Marshrutda `RequirePerm` YO'Q** — hub har havolani O'ZI `can(perm, 'view')` bilan
  filtrlaydi va bittasi ham ochiq bo'lmasa buni ochiq yozadi. Bitta kalit qo'ysak, aralash
  ruxsatlar bilan mos kelmasdi.
- Hisobot SAHIFASINING o'zi avvalgidek o'z `RequirePerm`i bilan darvozalangan — hub qo'shimcha
  eshik ochmaydi.

## 5.5. MENYUDA BITTA MANZIL = BITTA BAND (`activeNavTo`)

Yon menyu ilgari har guruhni MUSTAQIL tekshirardi (`pathname.startsWith(...)`), shuning uchun
bitta manzil bir NECHTA guruhni ochib yuborardi. "Hisobotlar" paydo bo'lgach bu ko'rindi:
`/admin/subjects/analitika` ochilganda ESKI "O'quv bo'limi" ham ochilardi (u yerda
`/admin/subjects` bor va yangi marshrut uning prefiksi ostiga tushadi). Xuddi shu
`/admin/rooms/utilization`, `/admin/forms/statistika`, `/admin/marketing/analytics` da ham.

Endi qaror `navigation.ts` dagi **`activeNavTo`** da: **ENG ANIQ (eng uzun) moslik g'olib**,
qolganlari ochilmaydi. `Sidebar` uni bir marta hisoblab, har guruhga `active` propi bilan
uzatadi (guruh o'zini o'zi tekshirmaydi — qaror faqat qo'shnilar bilan solishtirib chiqadi).

⚠️ `?tab=` solishtirishga KIRMAYDI: u sahifani emas, sahifa ichidagi bo'limni tanlaydi.

Testlar: `reports.test.ts` → `activeNavTo — qaysi menyu bandi faol` (har bir ko'chirilgan
marshrut "Hisobotlar"ga, eski bo'limlar esa O'Z sahifalarida faol qolishi qulflangan).

## 6. KONTEKSTGA BOG'LIQ hisobotlar KIRMAYDI

Guruh/o'quvchi/o'qituvchi ICHIDAGI tablar (guruh davomati, o'quvchi to'lov tarixi, o'qituvchi
maoshi, AI tahlil panellari) katalogga **tushmaydi**: ular `:id` siz ma'nosiz. Ularning
umumiy variantlari esa katalogda bor (masalan guruh to'lovlari → "Guruhlar bo'yicha to'lov",
guruh tarixi → "O'zgarishlar tarixi").

## 7. Yangi hisobot qo'shganda

1. `config/reports.ts` ga band qo'shing (`label`, `to`, `perm`, `description`, kerak bo'lsa `inNav`).
2. `description` — "bu hisobot QAYSI SAVOLGA javob beradi" (nom o'zi yetarli emas: "Analitika" —
   nimaning analitikasi?).
3. `perm` — `adminPermissions` katalogidagi HAQIQIY kalit (test tekshiradi).
4. `inNav: true` qo'ysangiz — bandni eski menyu guruhidan O'CHIRING.
5. Sahifa ichidagi tab bo'lsa — `?tab=` va `tabFromUrl`.
