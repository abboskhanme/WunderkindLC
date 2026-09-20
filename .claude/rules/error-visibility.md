# Xato KO'RINISHI qoidalari (klient)

## 1. ENG MUHIM — global xato ko'rsatkichi YO'Q

`api/client.ts` dagi interceptor **faqat 401** ni ushlaydi (token yangilash → `forceLogout`).
Qolgan barcha status (`400`, `403`, `409`, `500`) va tarmoq xatosi `Promise.reject` bo'lib
komponentga qaytadi. Ya'ni:

> **Komponent xatoni yutsa — u HAQIQATAN ko'rinmaydi.** Foydalanuvchi uchun bu "tugma
> ishlamayapti" yoki (eng yomoni) "saqlandi" bo'lib ko'rinadi.

Shuning uchun **har YOZUV so'rovi** (`post`/`put`/`patch`/`delete`) xatosi ko'rsatilishi SHART.

## 2. Qanday ko'rsatiladi

Xato matni **har doim** `apiErrorMessage(err, '<o'zbekcha fallback>')` (`src/lib/utils.ts`)
orqali olinadi — u avval backendning `{ message }` maydonini, keyin `Error.message` ni oladi.
Server sababni aynan shu maydonda qaytaradi ("Bu tur 3 ta amalda ishlatilgan", "Sana guruh
yaratilishidan oldin"), ya'ni foydalanuvchi ANIQ sababni ko'radi.

| Qayerda | Qanday |
|---|---|
| Modal / panel / forma ichida | `setError(...)` → qizil satr (forma yopilmaydi, kiritilgan matn qoladi) |
| Ro'yxat qatoridagi amal (o'chirish, arxivlash, tiklash) | `alert(...)` |
| Sozlamalar sahifasi | sahifadagi "Saqlandi" holati yonida qizil satr |

⚠️ **Xato bo'lganda forma YOPILMAYDI** va kiritilgan ma'lumot tozalanmaydi — aks holda
foydalanuvchi hammasini qaytadan yozadi (va dublikat yaratadi).

## 3. OPTIMISTIK yangilash — eng xavfli holat

Ekranni so'rovdan OLDIN yangilash (`setState` → keyin `await`) tez ko'rinadi, lekin xato
yutilsa **yolg'on "saqlandi"** beradi: ro'yxatda yangi qiymat, bazada eski.

Qoida: optimistik yangilashda `catch` da **orqaga qaytarish SHART** (eski holatni saqlab
qo'ying), va ustiga xabar chiqaring. Muqobil — lokal yamoqni faqat muvaffaqiyatdan keyin
qo'llash (bu oddiyroq va xavfsizroq).

⚠️ `setState` updater'i (`setX(prev => ...)`) ICHIDA so'rov chaqirmang: React StrictMode
updater'ni ikki marta bajaradi va so'rov ham ikki marta ketadi.

## 4. Xatoni yutish MUMKIN bo'lgan joylar

Faqat quyidagilar, va har birida SABABI izohda yozilishi kerak:

- **fon O'QISH** (`get*`) — masalan polling, ikkilamchi ma'lumot. Lekin sahifaning ASOSIY
  ma'lumoti bo'lsa xato ko'rsatilsin (aks holda "sahifa bo'sh, sababi noma'lum").
- brauzer API'lari: `navigator.clipboard`, `localStorage`, push/notification ruxsatlari.
- "o'qildi" belgilari kabi ikkilamchi amallar (`markNotificationsRead`).

## 5. Server tomoni — sabab MATN bilan qaytsin

Xato `500` bo'lib chiqsa, klient ko'rsatsa ham foydasi yo'q ("server xatosi"). Shuning uchun
kutilgan xatolar `BadRequest(new { message = ... })` bilan qaytariladi.

⚠️ Servis qatlamidagi `InvalidOperationException` controllerda **ushlanishi** kerak — aks
holda u 500 bo'lib chiqadi va sabab yo'qoladi (jurnalda aynan shunday bo'lgan:
`JournalService` "Sana guruh yaratilishidan oldin" deb tashlar, foydalanuvchi esa "server
xatosi" ko'rardi).

## 6. Namuna (ko'chirish uchun)

Loyihadagi to'g'ri yozilgan joylar: `BoardSettingsModal.run()` · `InstagramInbox.runAction()` ·
`components/media/PhotoDialog.tsx` · `ContactQueuePage` (tartib o'zgartirish) ·
`CurriculumItemEditorPage` (PDF yuklash) · `PaymentHistoryPanel` (xato ATAYIN modalga qayta
otiladi).

Takrorlanuvchi joyda eng arzon yechim — bitta `run(fn, fallback)` yordamchisi
(`BoardSettingsModal.tsx` dagidek).

## 7. Tarix

2026-09-20 da butun klient bo'yicha audit qilindi: **55 ta yozuv amali** xatoni jimgina
yutardi (6 tasi optimistik — yolg'on "saqlandi" bilan). Hammasi tuzatildi. Yangi kod
yozganda shu qoidaga amal qiling — audit qayta talab qilinmasin.
