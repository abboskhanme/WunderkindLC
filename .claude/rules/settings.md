---
description: Sozlamalar bo'limi — edutizim tuzilishi (guruhlar, chap bo'limlar kartasi, Funksionallik qatorlari), yagona katalog.
paths:
  - "WunderkindLC.Client/src/config/settingsGroups.ts"
  - "WunderkindLC.Client/src/pages/admin/settings/**"
  - "WunderkindLC.Client/src/config/navigation.ts"
---

# Sozlamalar bo'limi qoidalari

Foydalanuvchi talabi (2026-09-27): Sozlamalar edutizim bilan BIR XIL bo'lsin. Tanlangan yo'l —
"ko'rinish + borlarini joylash": edutizim tuzilishi, lekin FAQAT bizda BOR sozlamalar.

## 1. YAGONA MANBA — `config/settingsGroups.ts`

Undan IKKALASI quriladi: yon menyudagi "Sozlamalar" guruhi (`navigation.ts`) va guruh sahifasi
(`SettingsGroupPage`, `/admin/sozlamalar/:group`). Menyuga band QO'LDA yozilmaydi —
`settingsGroups.test.ts` menyu katalog bilan bir xilligini qulflaydi.

Guruhlar (edutizim tartibida): Umumiy sozlamalar · Moliya · O'quv · Sotuv va marketing · Ilova
sozlamalari. **Gamifikatsiya YO'Q** (modul yo'q). edutizimdagi Obuna, Bayram kunlari, Ommaviy
oferta, Yon panel modullari, Foydalanuvchi nazorati, Tizim va jadval — bizda ekvivalenti yo'q,
ATAYIN ko'rsatilmaydi (soxta bo'sh sahifa qilinmaydi). Kerak bo'lsa — alohida ish.

## 2. Ko'rinish (edutizim skrinshoti: `docs/edutizim/screens/settings-general.jpg`)

- Chapda oq karta: bo'limlar (ikonka + nom, tanlangani ko'k), 386px, ekran bo'yi.
- O'ngda: `rows` → kulrang `#f0f2f2` qatorlar (44px rangli ikonka + sarlavha + izoh + strelka);
  `panel` → tayyor sozlama komponenti o'z kartasi bilan; `link` → alohida sahifa (↗ belgisi).
- Tanlangan bo'lim — `?bolim=`. ⚠️ `?tab=` EMAS: "Xabar kanallari" paneli `tab` ni o'z ichki
  tablari (sms/telegram/firebase) uchun ishlatadi.

## 3. Ruxsat

Marshrutda `RequirePerm` YO'Q — sahifa har band/qatorni o'zi filtrlaydi (`visibleSettingsGroups`),
hisobotlar hub'idagi naqsh. Jurnal siyosati qatorlari (`JournalPolicyModal`, server ruxsati
`classes`) faqat TAHRIRLASH huquqi bilan ko'rinadi — oyna faqat saqlash uchun.
Menyuda guruh — undagi birorta kalit bo'lsa (`groupPermAny`); ruxsatsiz band (Foydalanuvchi
sozlamalari) bor guruh hammaga ko'rinadi.

## 4. Eski manzillar

`/admin/settings/{school,check,reasons,channels,apk,azure-speech,gemini}` →
`LEGACY_SETTINGS_REDIRECTS` bo'yicha yangi guruh sahifasiga (xatcho'plar buzilmasin).
Integratsiyalar (Zaxira, Turniket, Kamera, PostHog) edutizimda yo'q — `/admin/settings/*` da va
menyuda Future → Sozlamalar ostida QOLADI.

## 5. Yangi sozlama qo'shganda

1. `settingsGroups.ts` ga band yoki qator qo'shing (edutizimdagi mos guruhga), `perm` — haqiqiy kalit.
2. `panel` bo'lsa — `SettingsGroupPage.Panel` ga kalit → komponent qo'shing.
3. Eski joyida menyu bandi bo'lsa — O'CHIRING (bir band ikki joyda turmasin; test ushlaydi).
