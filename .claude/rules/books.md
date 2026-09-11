---
description: Kitoblar sotuvi — ombor (qoldiq/kirim tarixi), Telegram bot orqali buyurtma, admin tasdiqlash va analitika.
paths:
  - "WunderkindLC.Application/Services/BookSalesService.cs"
  - "WunderkindLC.Application/Services/BookShopBotService.cs"
  - "WunderkindLC.Server/Controllers/BooksController.cs"
  - "WunderkindLC.Client/src/pages/admin/books/**"
  - "WunderkindLC.Client/src/api/services/books.ts"
---

# Kitoblar sotuvi qoidalari

Migratsiya: `AddBookSales` (20260730050021). Modul markazda sotiladigan kitoblarni omborda yuritadi va
Telegram bot orqali buyurtma qabul qiladi. **Click/Payme YO'Q** — faqat naqd yoki karta raqamiga o'tkazma
+ chek rasmi.

## 1. Entitylar (Domain/Entities.cs, "KITOBLAR SOTUVI" bo'limi)

| Entity | Vazifasi |
|---|---|
| `Book` | Tovar: `Title/Author/Description/CoverUrl/CoverFileId/Price/Stock/IsActive/CreatedAt/CreatedBy` |
| `BookStockMove` | Qoldiqning HAR bir o'zgarishi: `Qty` (±), `Reason`, `OrderId?`, `Note`, `StockAfter`, `CreatedBy` |
| `BookOrder` | Buyurtma (nom/narx SNAPSHOT), `Number` (#1,#2…), `Status`, `ReceiptUrl`, `DecidedAt/By`, `Source`, `CardLast4`, `PaidTime` + NASIYA: `DueDate`, `PaidAt`, `PaidBy`, `SettledMethod` |
| `BookBotSession` | Chatning vaqtinchalik savdo holati — `ChatId` UNIKAL (bir chatda bitta faol sessiya) |

- `Book.CoverFileId` — Telegram keshlagan `file_id`. Muqova bir marta yuklangach botga qayta
  yuklanmaydi (APK/test PDF bilan bir xil usul). **Muqova o'zgarsa `CoverFileId` bo'shatilishi SHART.**
- `BookOrder.StudentId` — bot foydalanuvchisining telefoni markazdagi o'quvchi (`Phone`/`ParentPhone`)
  bilan mos kelsa teglanadi. Mos kelmasa `null` — mehmon ham kitob sotib olishi mumkin.

## 2. YAGONA QOIDA: qoldiq faqat TASDIQLAGANDA ayiriladi

`BookSalesService` (static, Application/Services) — ombor/buyurtma mantig'ining **yagona joyi**.
Controller ham, bot ham faqat shu orqali ishlaydi, aks holda "qoldiq qanday o'zgaradi" ikki xil bo'lib ketadi.

```
Bot: buyurtma yaratildi  →  Stock TEGILMAYDI  (Status=pending)
Admin: Tasdiqlash        →  Move(book, -Qty, "sale", …)  →  Stock ayiriladi
Admin: Rad etish         →  Stock TEGILMAYDI, mijozga sabab yuboriladi
Qo'lda sotuv             →  pending yaratiladi + DARHOL ApproveAsync (bitta SaveChanges)
Admin: Qaytarish         →  Move(book, +qty, "return", …) →  Stock QAYTADI (§2.5)
```

- `Move(book, qty, reason, note, createdBy, orderId?)` — `Stock`ni o'zgartiradi va `BookStockMove`
  qaytaradi. **`SaveChanges` chaqirmaydi** — chaqiruvchi o'z tranzaksiyasida saqlaydi (tasdiqlash
  bitta SaveChanges'da ketsin).
- `ApproveAsync` / `RejectAsync` — `null` qaytarsa muvaffaqiyat, aks holda foydalanuvchiga
  ko'rsatiladigan xato matni (chaqiruvchi 400 qiladi va mijozga xabar YUBORMAYDI).
- Qoldiq yetmasa tasdiqlash rad etiladi (`"Omborda yetarli emas: qoldiq N, buyurtma M"`).
- Konstantalar: `StatusPending|Approved|Rejected`, `ReasonInitial|Restock|Sale|Return|Correction`,
  `PayCash|PayCard`. **Xom satr yozmang** — shu konstantalardan foydalaning.
- Mijozga/adminga ketadigan matnlar ham shu yerda (`CustomerApprovedText`, `CustomerRejectedText`,
  `AdminNewOrderText`) — controller va bot bir xil matn yuborsin.
- `NotifyAdminsAsync` — yangi buyurtma haqida `TelegramRegistration`dagi admin/superadminlarga xabar.
  Xato **jim yutiladi** (`LeadNotifier` bilan bir xil siyosat) — xabarnoma buyurtmani buzmasin.

## 2.5 QAYTARISH (vozvrat) — kitob qaytarib olindi (migratsiya `AddBookReturns`)

Naqd, karta va nasiya sotuvlarining HAMMASIDA ishlaydi. Mantiq — `BookSalesService.ReturnAsync`
(yagona joy), endpoint — `POST /orders/{id}/return`.

```
Qaytarish  →  Move(book, +qty, "return", …)   →  qoldiq OSHADI
           →  ReturnedQty += qty              →  sotuv summasidan o'sha qismi AYIRILADI
           →  pul: OLINGAN bo'lsa qaytariladi, to'lanmagan nasiyada esa QARZ kamayadi
```

⚠️ **HOLAT O'ZGARMAYDI** — qaytarilgan sotuv `approved` bo'lib qolaveradi. Sabab: qaytarish
QISMAN ham bo'ladi (3 dona sotilib, 1 tasi qaytariladi), holat esa buni ifodalay olmaydi.
Shuning uchun "qancha sotildi / qancha pul qoldi" savoliga **`Status` emas**, sof yordamchilar
javob beradi:

| Funksiya | Nima |
|---|---|
| `NetQty(o)` | `Qty − ReturnedQty` — mijozda qolgan dona |
| `NetTotal(o)` | `Total − UnitPrice × ReturnedQty` — **barcha hisobotlarning manbai** |
| `ReturnedAmount(o)` | qaytarilganlarning sotuv qiymati |
| `IsFullyReturned(o)` | butunlay qaytarilganmi (qarz/tushum qolmagan) |
| `ReturnError(o, qty)` | darvoza (sof funksiya): faqat `approved`, `1..qolgan` oralig'ida |

⚠️ **PUL faqat OLINGAN bo'lsa qaytariladi** (`IsPaid` sotuv paytida tekshiriladi):
to'lanmagan nasiyada kassadan hech narsa chiqmaydi — `RefundedAmount` 0 bo'lib qoladi va
shunchaki qarz kamayadi. To'liq qaytarilgan nasiya qarzdorlar ro'yxatidan **butunlay chiqadi**
va `PayCreditAsync` unga to'lovni qabul qilmaydi.

⚠️ **QAYTARISH SOTILGAN KUNGA yoziladi** (kunlik grafik, kun × kitob kesimi, kitob kesimi —
hammasi sof). Aks holda "shu kuni nima sotildi" savoliga qaytarilgan kitob bilan javob berilardi.
Kassadan pul QACHON chiqqani esa **alohida** raqam: `RefundedInPeriod` (qaytarish sanasi bo'yicha).
Ikkalasi analitikada ayri ko'rsatiladi.

⚠️ **QAYTARISH — KIRIM EMAS** (`Reason="return"`, `"restock"` emas): "davr ichida kirim" va
"faqat kirim" ro'yxati nashriyotdan olingan kitoblarni ko'rsatadi, qaytarilgan sotuv u yerga
qo'shilsa raqam shishardi. To'liq ombor tarixida u baribir ko'rinadi.

`Book.Stock` konkurentlik tokeni bo'lgani uchun qaytarishda ham poyga himoyasi bor (§2.2) —
`DbUpdateConcurrencyException` ushlanadi, xotiradagi o'zgarishlar QAYTARILADI (jumladan oldingi
qaytarish izlari — `ReturnedAt/By/Reason` bo'shatilmaydi, aynan eski qiymatga tiklanadi) va
odatiy "qaytadan urinib ko'ring" xatosi qaytadi.

Mijozga (bot buyurtmasi bo'lsa) `CustomerReturnedText` yuboriladi; qo'lda sotuvda `ChatId=0` —
xabar yo'q. Audit: `BookOrder` / `update` — "Kitob qaytarildi: … pul qaytarildi | qarz kamaydi".

## 2.4 NASIYA — kitob berildi, pul keyin (migratsiya `AddBookCreditSales`)

Uchinchi to'lov turi: `BookSalesService.PayCredit` = `"credit"`. **FAQAT markazda qo'lda sotuvda**
— botda yo'q (noma'lum Telegram mijoziga qarz berilmaydi).

```
Nasiyaga sotuv  →  Status=approved, qoldiq AYIRILADI (kitob mijozning qo'lida), PaidAt=NULL
                   ⇒ summa TUSHUMGA emas, QARZGA sanaladi
«To'landi»      →  PayCreditAsync: PaidAt/PaidBy/SettledMethod  ⇒ summa to'lovlarga qo'shiladi
                   OMBOR TEGILMAYDI (kitob allaqachon berilgan)
```

⚠️ **"To'landimi" savoli `Status` bilan EMAS, `BookSalesService.IsPaid` bilan javob beriladi.**
Naqd/kartada `approved` = to'langan; nasiyada esa qo'shimcha `PaidAt != null` sharti bor.
`IsPaid` ATAYIN `PaidAt != null` deb yozilmagan: migratsiyadagi to'ldirish bajarilmagan bazada
eski naqd/karta qatorlarida `PaidAt` bo'sh bo'lib, ular "to'lanmagan" bo'lib ko'rinardi.

⚠️ **XARIDOR NASIYADA MAJBURIY** (`CreditCustomerError`): o'quvchini F.I.Sh. bo'yicha qidirib
tanlash YOKI ismini yozish. Qarz kimda ekani yozilmasa nasiyaning ma'nosi qolmaydi. Naqd/kartada
xaridor ilgarigidek IXTIYORIY (§2.1). O'quvchi tanlanmasa telefon ham kiritish mumkin
(`CustomerPhone`) — qarzdorni topish uchun.

⚠️ **MUDDAT IXTIYORIY**, lekin `IsOverdue` faqat muddat QO'YILGAN va to'lanmagan nasiyani
kechikkan deb sanaydi (`today` — parametr, funksiya sof). Muddatsiz qarz hech qachon
"muddati o'tgan" bo'lmaydi — kassir muddat qo'ymagani uchun mijozni ayblash noto'g'ri bo'lardi.

**Qarzdorlar kesimi** (`BooksController.DebtorKey`): o'quvchi bo'lsa `s:{id}` (ismi o'zgarsa ham
qarz bitta odamda qoladi), aks holda `n:{ism}|{mahalliy telefon}`.

**Analitikada:**
- sotuv taqsimoti — SOTUV paytidagi to'lov turi bo'yicha (`naqd + karta + nasiya = jami`).
  Nasiya keyin to'lansa ham o'sha kunda "nasiya" bo'lib qoladi — aks holda o'tgan kunlar grafigi
  orqaga qarab o'zgarib turardi;
- `CreditOutstanding`/`CreditOverdue` — **davrga BOG'LIQ EMAS** (ombor qoldig'i kabi joriy holat);
- `CreditCollected` — davr ichida yig'ilgan pul, **`PaidAt` bo'yicha** (nasiya o'tgan oyda
  sotilib, pul shu oyda kelishi mumkin).

Kitob sotuvi baribir `FinanceTransaction`ga yozilmaydi (§7) — nasiya ham o'quvchi balansiga
tegmaydi, u faqat shu bo'limning qarzi.

## 2.2 QOLDIQ POYGASI (race) — `Book.Stock` konkurentlik tokeni

`ApproveAsync` "qoldiqni o'qi → yetadimi deb tekshir → yangisini yoz" ketma-ketligi bilan ishlaydi va
orada `await` bor. Qoldiq 1 bo'lganda **ikki kassir bir vaqtda** "Kitob sotish" bossa, ikkalasi ham
`Stock=1` ni o'qib, ikkalasi ham tekshiruvdan o'tib ketardi: **2 dona sotilib, qoldiqdan 1 tasi
ayirilardi**, `BookStockMove` ning ikkala qatorida ham `StockAfter=0` turgani uchun buni tarixdan ham
bilib bo'lmasdi.

- `AppDbContext`: `b.Entity<Book>().Property(x => x.Stock).IsConcurrencyToken()`.
  EF endi `UPDATE Books SET Stock=@yangi WHERE Id=@id AND Stock=@asl` yozadi — oraga boshqa amal
  tushgan bo'lsa **0 qator** yangilanadi va `DbUpdateConcurrencyException` chiqadi.
  Bu **faqat model metadatasi**: ustun/indeks o'zgarmaydi, ya'ni **MIGRATSIYA KERAK EMAS**
  (keyingi migratsiya yaratilganda `ModelSnapshot` ga annotatsiya o'zi qo'shiladi, DDL chiqmaydi).
- `ApproveAsync` istisnoni ushlaydi, xotiradagi o'zgarishlarni **qaytaradi** (qoldiq, holat, ombor
  harakati) va odatiy uslubda xato matnini beradi:
  `"Qoldiq shu payt boshqa amalda o'zgardi — qaytadan urinib ko'ring."`
- `POST /{id}/stock` (`AddStock`) ham shu istisnoni ushlaydi va 500 o'rniga o'sha matn bilan 400 beradi.
- **Kitob yangilanadigan barcha joylar kitobni kuzatilgan holda (`AsNoTracking` SIZ) yuklashi shart** —
  aks holda EF asl qiymatni bilmay, istisno chiqara boshlaydi. Tekshirilgan: `BooksController`
  (`Update`, `Delete`, `AddStock`, `ManualSale`) va `BookSalesService.ApproveAsync` — hammasi kuzatilgan.
  `BookShopBotService` kitobni faqat O'QIYDI (qoldiqni o'zgartirmaydi), shuning uchun UPDATE ham
  generatsiya qilinmaydi.

## 2.3 BUYURTMA RAQAMI — jarayon ichidagi navbat

`NextOrderNumberAsync` = `MAX(Number)+1`, unikal indeks yo'q va raqam olingandan keyin `Add`/
`SaveChanges` gacha bir necha `await` bor → ikki kassir bir vaqtda sotsa **ikkala buyurtma ham #57**
bo'lardi. Ilova bitta nusxada ishlagani uchun jarayon ichidagi navbat yetarli
(`TestCertificateService.NumberGate` bilan bir xil yondashuv):

- `SemaphoreSlim NumberGate` — "o'qish + belgilash" oralig'ini bo'linmas qiladi;
- `_lastIssuedNumber` — **berilgan, lekin hali saqlanmagan** raqamni eslab qoladi (faqat qulf
  yetmaydi: qulf ostida berilgan raqam bazada darhol ko'rinmaydi);
- raqam olingach buyurtma yozilmasa (masalan qoldiq yetmadi) raqam "kuyadi" va ro'yxatda bo'shliq
  qoladi — takrorlanishdan ko'ra zararsizroq;
- `ResetOrderNumberSequence()` — **faqat testlar uchun** (har test o'z bazasi bilan ishlaydi).

Tuzatish `NextOrderNumberAsync` ning O'ZIDA bo'lgani uchun **bot oqimi ham** (`BookShopBotService`)
avtomatik himoyalangan — chaqiruvchilarni o'zgartirish shart emas.

## 2.1 QO'LDA SOTUV — markazda, joyida (migratsiya `AddBookManualSale`)

"Buyurtmalar" tabidagi **«Kitob sotish»** tugmasi (`BookSellModal`, perm `books:create`):
kitob → soni → **(ixtiyoriy) o'quvchi** → naqd/karta. Karta bo'lsa **to'lov vaqti**
(`PaidTime`, "HH:mm") va **kartaning oxirgi 4 raqami** (`CardLast4`) kiritiladi — chek rasmi
YO'Q, chunki pul kassirning oldida to'langan. Normalizatsiya moliya bo'limi bilan bir xil
(`PaymentFields.TryNormalizeCardLast4/TryNormalizeTime`) — **to'liq karta raqami saqlanmaydi**.

- `POST /orders/manual` (`BookManualSalePayload`) buyurtmani `pending` qilib qo'shadi va
  **darhol `ApproveAsync`** chaqiradi. Qoldiq yetmasa `ApproveAsync` `SaveChanges` qilmaydi →
  400 qaytadi va **buyurtma umuman yozilmaydi** (yarim holatdagi `pending` qolib ketmasin).
  Ombor mantig'i shu bilan botdagi oqim bilan **bitta joyda** qoladi.
- `BookOrder.Source` = `"bot"` | `"manual"` (`BookSalesService.SourceBot/SourceManual`).
  Qo'lda sotuvda `ChatId = 0` → **`Approve`/`Reject` Telegram xabarini `ChatId != 0` bilan
  darvozalaydi** (yuboriladigan chat yo'q). Migratsiya eski qatorlarni `defaultValue: "bot"`
  bilan to'ldirgan.
- **Sotuvdan olingan kitob sotilmaydi**: `ManualSale` `BookSalesService.ManualSaleBookError(book)`
  darvozasidan o'tadi (`null` — sotsa bo'ladi). Ilgari tekshiruv faqat frontend'da (`sellable`
  filtri) edi, ya'ni API to'g'ridan-to'g'ri chaqirilsa `IsActive=false` kitob ham sotilardi.
- **O'QUVCHI IXTIYORIY** (2026-08-05): markazda o'qimaydigan xaridorga (ota-ona, o'tkinchi,
  qo'shni maktab o'quvchisi) ham kitob sotiladi. Ilgari `StudentId` MAJBURIY edi va kassir
  bunday sotuv uchun soxta o'quvchi yaratishga majbur bo'lardi. `BookOrder.StudentId`
  allaqachon nullable — bot oqimida mehmon buyurtmasi shunday yozilardi, ya'ni ma'lumot
  modeliga tegilmadi (**migratsiya kerak emas**).
  - `StudentId` berilsa VA topilmasa — baribir 400 (jim o'tkazilsa sotuv noto'g'ri odamga
    teglanmay qolardi va kassir sezmasdi).
  - Ism: o'quvchi tanlansa uning ismi (asl manba), aks holda `CustomerName` erkin matn —
    u ham ixtiyoriy. Bo'sh qolsa `CustomerName=""` saqlanadi va UI uni `"Noma'lum"` deb
    ko'rsatadi (`BookOrdersTab`/`BookCardPaymentsTab` da allaqachon shunday fallback bor,
    `AdminNewOrderText` ham). **Sun'iy "Noma'lum xaridor" matni BAZAGA yozilmaydi.**
  - O'quvchi tanlanmasa `Phone` ham bo'sh qoladi (raqam faqat o'quvchidan olinadi).
- O'quvchi qidiruvi — `GET /students?q=` (`BookStudentDto`), `books` ruxsati ostida va balanssiz
  (kitob sotuvi balansga tegmaydi). Telefon bo'yicha moslik — `BookSalesService.PhoneMatches`:
  **ikkala tomon ham mahalliy qismga keltiriladi** (`PhoneUtil.Key` = mamlakat kodisiz oxirgi 9
  raqam), keyin `Contains`. Sabab: bazada hamma raqam `+998...` bo'lgani uchun xom raqamlar ustidagi
  qidiruvda **"9989" deyarli BARCHA o'quvchiga mos kelib**, kassirga 80 ta begona odam chiqarardi.
  Nomzodlar `PhoneScanLimit` (40) ga yetganda o'qish to'xtaydi — butun jadval ro'yxatga yig'ilmaydi.
  > ⚠️ `KassaController.SearchStudents` da hali ESKI mantiq (xom raqamlar + `Take(80)`) turibdi —
  > boshqa bo'lim bo'lgani uchun ataylab tegilmagan. O'sha yerda ham xuddi shu kamchilik bor.
- Qo'lda sotuv `PaymentMethod=card` bo'lsa **"Karta to'lovlari" tabida ham** ko'rinadi
  (`CardOrdersQuery` faqat to'lov turiga qaraydi) — chek o'rnida `••1234` + vaqt turadi.

## 3. Bot oqimi (`BookShopBotService`, singleton)

`OnlineTestBotService` bilan bir xil tuzilma. Barcha callback'lar `Handles(data)` orqali
`TelegramBotService`dan yo'naltiriladi.

```
«📚 Kitob sotib olish» (yoki /kitoblar)
   → ShowCatalogAsync    — IsActive kitoblar (max 30), narx + qoldiq; tugma faqat Stock>0 bo'lsa
   → kb:{id}  OpenBookAsync   — muqova + tavsif, soni so'raladi (Step="qty")
   → kq:{n} / kqm             — soni tanlandi yoki qo'lda yoziladi (max 50)
   → kpc  ChooseCashAsync     — Step="confirm" → kconf ConfirmCashAsync
   → kpk  ChooseCardAsync     — karta rekvizitlari, Step="receipt" → rasm/PDF kutiladi
   → krcp PromptReceiptAsync  — «🧾 Chekni yuborish»: chekni qanday yuborish yo'riqnomasi
   → kcan CancelAsync         — sessiya o'chiriladi
```

- **«🧾 Chekni yuborish» tugmasi** (`CbSendReceipt`) sessiya bosqichini O'ZGARTIRMAYDI — karta
  tanlangan zahoti `Step="receipt"` va chek allaqachon qabul qilinadi. Tugma faqat yo'riqnoma
  (mijoz uzun matnni o'qimay, nima qilishni bilmay qolmasin). Shu sabab tugmani bosmasdan
  yuborilgan chek ham ishlaydi — **bu ataylab**, aks holda rasm jimgina yo'qolardi.

- **Majburiy obuna** kitob buyurtmasiga ham tegishli (`RequireSubscriptionAsync`) — katalog ochishda
  va eski xabardagi `kb:` tugmasi bosilganda ikkalasida ham tekshiriladi.
- Chek (rasm/hujjat) `TelegramBotService.HandleFileAsync`da qabul qilinadi — avval
  `AwaitingReceiptAsync` tekshiriladi, keyin `HandleReceiptAsync` faylni `/uploads/…` ga ko'chiradi.
- `CreateOrderAsync` qoldiqni **yana bir bor** tekshiradi (boshqa mijoz oldin olgan bo'lishi mumkin).
- Karta rekvizitlari kiritilmagan bo'lsa `ChooseCardAsync` mijozga "sozlanmagan, naqdni tanlang"
  deydi — sessiya bosqichi o'zgarmaydi.
- **Kitob sotuvi HAMMAGA ochiq** — sotib olish uchun markazda o'qish shart emas. Markaz ro'yxatida
  topilmagan mijoz `GuestKeyboard` (kitob + adminga murojaat) oladi.

## 4. Botda tugma ko'rinmasa — `CenterMeta.BookSalesEnabled`

Tugma **kitoblar soniga bog'liq emas**, faqat shu bayroqqa:
`TelegramBotService.RegisteredKeyboard(books)` ← `BookSalesEnabledAsync(db)`.

> ⚠️ Migratsiya bu ustunni mavjud bazaga `defaultValue: false` bilan qo'shgan (entity'da `= true`,
> lekin u faqat YANGI qatorga tegishli). Ya'ni **eski o'rnatishda modul O'CHIQ** holda keladi —
> Sozlamalar tabidan yoqish kerak. Yoqilgandan keyin botda `/start` yuborilishi ham shart:
> Telegram reply-klaviaturani mijoz tomonida keshlaydi.

Sozlamalar `CenterMeta`da (maxfiy EMAS — mijozga baribir ko'rsatiladi, `.env` emas):
`BookSalesEnabled`, `BookCardNumber`, `BookCardHolder`, `BookPaymentNote`.

## 5. API — `BooksController` (`api/admin/books`, `[AdminPerm("books")]`)

| Metod · yo'l | Vazifasi |
|---|---|
| `GET /` | Kitoblar + sotuv/kutilayotgan statistika (N+1 emas — bitta so'rovda biriktiriladi) |
| `POST /` | Yangi kitob; `InitialStock` berilsa `"initial"` kirim yoziladi |
| `PUT /{id}` | Tahrirlash — **qoldiq bu yerda o'zgarmaydi** |
| `DELETE /{id}` | Buyurtma tarixi bor kitob O'CHIRILMAYDI (hisobot buzilmasin) — `IsActive=false` qiling |
| `POST /{id}/stock` | Qoldiq kirim/korreksiya (`qty` ±, `note`) |
| `GET /stock-moves` | Ombor tarixi; `onlyIn=true` → faqat kirim (Qty>0, qaytarish KIRMAYDI) |
| `GET /orders`, `GET /orders/pending-count` | Buyurtmalar + tab belgilari (`count` kutilmoqda, `credits` to'lanmagan nasiya, `overdue`). `status=returned` — QAYTARILGANLAR kesimi (holat emas) |
| `POST /orders/{id}/return` | QAYTARISH: `qty` dona omborga qaytadi, summa sof bo'ladi (§2.5) |
| `GET /credits`, `GET /credits/export` | NASIYA: to'lanmaganlar (yoki davrda to'langanlari) + qarzdorlar kesimi + jamlanma |
| `POST /orders/{id}/pay` | NASIYA to'lovini qabul qilish (`method` naqd/karta) — ombor tegilmaydi |
| `POST /orders/manual` | QO'LDA SOTUV — yaratadi va darhol tasdiqlaydi (§2.1) |
| `GET /students?q=` | Qo'lda sotuv uchun o'quvchi qidiruvi (min 2 belgi, max 20) |
| `GET /card-payments` | KARTA to'lovlari + jamlanma (tasdiqlangan/kutilayotgan summa) va karta rekvizitlari |
| `POST /orders/{id}/approve`, `/reject` | `BookSalesService` orqali; muvaffaqiyatda mijozga xabar |
| `GET /analytics` | Tushum (naqd/karta), sotilgan soni, qoldiq, kunlik va kitob kesimi — **hammasi SOF** (qaytarilgani ayirilgan) + qaytarish raqamlari |
| `GET /orders/export`, `/stock-moves/export`, `/analytics/export` | .xlsx (analitika — 3 varaq) |
| `GET|PUT /settings` | Bot sozlamalari (yuqoridagi 4 maydon) |
| `POST /cover` | Muqova yuklash |

## 6. Frontend — `/admin/books` (nav: O'quv bo'limi → Kitoblar sotuvi)

`pages/admin/books/BookSalesPage.tsx` — 6 tab: **Buyurtmalar** (default) · **Karta to'lovlari** ·
**Nasiya** · **Ombor** · **Analitika** · **Sozlamalar**.
"Nasiya" (`BookCreditsTab`) — qarzlar ro'yxati + qarzdorlar kesimi; tab belgisi qarz soni bilan
(muddati o'tgani bo'lsa QIZIL). Analitikada "Har kuni sotilgan kitoblar" — kun bosilganda o'sha
kunning kitob kesimi va sotuvlari (soati bilan) ochiladi; sotuvlar lentasi eng oxirgi **400** ta
bilan cheklangan va sig'magani ro'yxatda ochiq yozib qo'yiladi (jim qirqilmaydi).
"Karta to'lovlari" (`BookCardPaymentsTab`) — kartaga o'tkazma qilganlar: chek rasmi jadvalda
kichik ko'rinishda turadi (bosilsa kattalashadi), tepada bo'lim bog'langan karta rekvizitlari va
shu kartaga hisoblangan jami summa. **Jamlanma SERVERDA butun topilma bo'yicha hisoblanadi** —
`GET /orders` 1000 ta bilan cheklangani uchun ro'yxatdan qo'shib chiqarish noto'g'ri bo'lardi.

**QAYTARISH** (`BookReturnModal` — bitta oyna, uch joydan ochiladi): "Nasiya" tabida «To'landi»
yonida, "Buyurtmalar"/"Karta to'lovlari"da tasdiqlangan qatorda, "Analitika → sotuvlar
lentasi"da har sotuv qatorida. Oyna soni (qisman qaytarish), sababi va **pul qaytadimi yoki qarz
kamayadimi** ni ochiq yozadi. Ro'yxatlarda soni/summa SOF ko'rsatiladi, xomi tagi chizilgan holda —
ya'ni "nega raqam kamaydi" savoli qolmaydi. Analitika lentasi esa XOM qiymatni saqlaydi (u hodisa
tarixi) va yonida "N qaytarildi" belgisi chiqadi.

Yozish amallari `can('books','edit')` bilan darvozalangan. API qatlami
`api/services/books.ts`, yorliqlar `bookLabels.ts` (status/to'lov/sabab matnlari — komponentda xom
satr yozilmasin).

## 7. Moliya bilan bog'liqlik — YO'Q

Kitob sotuvi **`FinanceTransaction`ga yozilmaydi** va o'quvchi balansiga tegmaydi. Tushum faqat shu
bo'limning analitikasida ko'rinadi. (Sabab: o'quv to'lovi hisobotlarini kitob savdosi buzmasin.)
Agar kelajakda kassaga ulash kerak bo'lsa — `ApproveAsync` ichida, bitta joyda qilinadi.
