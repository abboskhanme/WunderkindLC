---
description: Raqam, summa va telefon maydonlari — kiritish va ko'rsatish formati (O'zbekiston odatlari), `type="number"` taqiqi.
paths:
  - "WunderkindLC.Client/src/**/*.tsx"
  - "WunderkindLC.Client/src/lib/numberFormat.ts"
  - "WunderkindLC.Client/src/components/ui/NumberInput.tsx"
  - "WunderkindLC.Client/src/components/ui/PhoneInput.tsx"
---

# Raqam / summa / telefon maydonlari

## 1. ENG MUHIM — `type="number"` ISHLATILMAYDI

Foydalanuvchi shikoyati (2026-09-27): "summa maydonida 0 turadi, uni o'chirib bo'lmaydi".
Sabab — `<input type="number" value={x} onChange={e => setX(Number(e.target.value))}>`:
maydon bo'shatilsa `Number('') === 0` bo'ladi va "0" qaytib yoziladi. Ustiga `type="number"`
guruh ajratgichini ko'rsata olmaydi.

O'rniga — `components/ui/NumberInput.tsx`:

| Maydon | Komponent |
|---|---|
| Summa (so'm): to'lov, narx, maosh, chegirma, bonus, xarajat | `MoneyInput` — `1 250 000` |
| Boshqa son: kun, daqiqa, foiz, sig'im, ball, yil, port | `NumberInput` (guruhsiz) |

⚠️ **FAQAT BUTUN SON** (foydalanuvchi qarori, 2026-09-27: "kasr son kiritilmaydi umuman").
Vergul/nuqta yozib bo'lmaydi. Serverdan kasrli qiymat kelsa (qisman oy qarzi) — maydon uni
yaxlitlab ko'rsatadi VA formaga yaxlit sonni qaytaradi (ekrandagi = yuboriladigan).
**Yagona istisno — KPI formulasi** (`KpiRulesPage`: jarima −0,2, pog'ona 1,2, chegara 35,99%):
`fractional` belgisi. Boshqa joyda `fractional` ISHLATMANG — KPI'ni butun son qilsak formula buzilardi.

- Qiymat turi `number | null`: bo'sh maydon → `null`. Holat ham `null` ni saqlashi SHART.
- ⚠️ `onChange={(v) => setX(v ?? 0)}` YOZMANG — xato qaytadi ("0" yana paydo bo'ladi).
  `?? 0` faqat hisob-kitobda va so'rov yuborishda (0 haqiqatan ruxsat etilgan joyda).
- YANGI yozuvda summa maydoni BO'SH boshlanadi (`null`), tahrirlashda — haqiqiy qiymat.
- Ko'rinish: `as={DrawerInput}` / `as="input"` — mavjud maydon uslubi saqlanadi.
- Telefonda `inputMode` raqamli klaviaturani ochadi; kursor formatlashda sakramaydi.

## 2. FORMAT — `lib/numberFormat.ts` (yagona manba, testlangan)

- Summa: 3 xonadan BO'SH JOY bilan (kasr faqat KPI koeffitsiyentida — VERGUL bilan: `1,2`). Ro'yxatda: `formatNumber(n)` yoki
  `formatMoney(n)` ("… so'm"). ⚠️ `toLocaleString()` ishlatmang — brauzer tiliga qarab `1,250,000`
  yoki `1.250.000` chiqadi.
- Telefon: `+998 90 123 45 67`.
  - Kiritish — `PhoneInput` (qiymat `"998901234567"`, bazaga shu shaklda).
  - Ko'rsatish — `formatPhone(raw)`: o'zbek raqami formatlanadi, XORIJIY/buzuq raqam TEGILMAYDI
    (unga "998" qo'shib soxta raqam yasalmaydi).
  - Jadvaldagi qisqa ko'rinish (`PhoneChip` → `formatPhoneShort`, "93 854 70 83") edutizim
    bilan bir xil bo'lishi uchun ATAYIN qoldirilgan.

## 3. Testlar

`lib/__tests__/numberFormat.test.ts` (format qoidalari), `components/ui/__tests__/NumberInput.test.tsx`
(jsdom: "0 ni o'chirib bo'sh qoldirish mumkin", guruhlash, kasr verguli yo'qolmasligi).
