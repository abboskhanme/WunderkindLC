# CTI (Local Call) moduli — qisqa yo'riqnoma

Call Center ostidagi **"Local Call"** bo'limi — markazning YAGONA telefoniyasi: xodim telefonlaridagi Android agent-ilova
qo'ng'iroqlar metadata'si va audio yozuvlarini serverga yuboradi; operator React'dan tarixni
ko'radi, audioni eshitadi va **click-to-call** qiladi (server WebSocket orqali telefonga
`dial` buyrug'i yuboradi, agent oflayn bo'lsa FCM push bilan ilova uyg'otiladi).

## Ishga tushirish

1. **Migratsiya** — alohida buyruq SHART EMAS: server startup'da avto-migratsiya
   (`20260704160829_AddCtiModule` — CtiAgents, CtiCallRecords, CtiCallEvents, CtiCommandLogs).
2. **FCM** (agent oflayn bo'lsa uyg'otish uchun) — allaqachon mavjud sozlama ishlatiladi:
   Admin → Sozlamalar → **Push (Firebase)** dagi service account JSON. Alohida secret kerak emas.
3. **JWT** — mavjud `Jwt__Key` ishlatiladi (agent tokenlari ham shu kalit bilan, rol `ctiagent`).
4. **Audio yozuvlar** — `recordings/cti/` papkada (docker'da `cti-recordings` volume,
   `/app/recordings`). Yo'lni o'zgartirish: `Cti__RecordingsPath` env. DIQQAT: `/uploads`
   ostida EMAS — faqat autentifikatsiyalangan endpoint orqali beriladi.

## Android ilovani ulash

Ilovadagi `BaseUrl` ni serverga moslang: `https://lc.wunderkindedu.uz` — mobil API prefiksi
`/api/mobile` (login: `POST /api/mobile/auth/login` → `{ token, agentId, wsUrl }`;
`wsUrl` = `wss://host/ws`, ulanish `?token=<JWT>` bilan).

Agent hisobini yaratish: Admin → Call Center → **Local Call** → "Agentlar" tab → "Agent qo'shish"
(login/parol/ism). Shu login-parol Android ilovaga kiritiladi.

## API xaritasi

| Kim | Prefiks | Endpointlar |
|---|---|---|
| Android agent | `/api/mobile` | `auth/login`, `calls`, `calls/{id}/audio` (multipart, 50MB), `calls/{id}/events`, `agents/heartbeat`, `agents/fcm-token` |
| Android agent | `/ws?token=` | raw WebSocket: server→ilova `{action:"dial"|"ping"|"logout"}`, ilova→server `{type:"ack"|"presence"}` |
| Operator (web) | `/api/cti` | `agents` (ro'yxat/CRUD), `agents/{id}/dial`, `calls` (filtr+sahifalash), `calls/{id}`, `calls/{id}/audio` (Range stream), `calls/{id}/note` |

Operator API ruxsati — `calls.local` sahifa kaliti (`AdminPerm("calls.local")`; `calls` bo'lim
ruxsati ham ochadi). Ruxsatdan tashqari EGALIK: har xodim FAQAT o'ziga biriktirilgan agent(lar)ning
(`CtiAgent.StaffUserId`) qo'ng'iroqlari va audiosini ko'radi, hammasini — faqat superadmin.

## Arxitektura eslatmalari (ESLAB QOLISH)

- **WebSocket SignalR EMAS** — raw `/ws?token=`; ulanishlarni `CtiConnectionManager` singleton'i
  boshqaradi. U **Application qatlamida** (Server/Cti/ dan ko'chirilgan) — sababi: Local SMS uchun
  `AutoMessageService` kabi Application xizmatlari ham shu gateway orqali yuboradi.
- **Click-to-call:** WS ulangan bo'lsa `dial` buyrug'i; agent oflayn bo'lsa mavjud
  `FcmService.SendDataAsync` (data-only push, `CenterMeta.FcmServiceAccountJson`) bilan uyg'otib
  qayta urinish. Dial raqami `CtiController.NormalizePhone` orqali `+998` formatga keltiriladi
  (9 xonali lokal → `+998XXXXXXXXX`; ilova qo'ng'iroq qilishi uchun oldida `+` SHART).
- **Agent autentifikatsiyasi** — mavjud JWT kaliti, rol `Roles.CtiAgent`, parol mavjud
  `PasswordHasher` bilan. Alohida secret/kalit yaratilmaydi.
- ⚠️ **Agentni O'CHIRISH (`IsActive=false`) DARHOL kuchga kiradi.** Token 12 soat yashaydi, shuning
  uchun faqat loginni to'sish yetmaydi (ishdan ketgan xodimning telefoni qo'ng'iroq qilish va
  ota-onalarga SMS yuborishda davom etardi). Endi: o'chirilganda ilovaga `{action:"logout"}` yuborilib
  WS ulanishi uziladi va FCM tokeni tozalanadi (`CtiConnectionManager.DisconnectAsync`); WS ulanishda,
  har bir `/api/mobile/*` so'rovida (401) va `Dial`/SMS'da bazadagi `IsActive` tekshiriladi;
  `CtiSmsService` o'chirilgan agentni (standart ham, aniq tanlangan ham) ishlatmaydi va sababni
  yozadi; o'chirilgan agentni standart Local SMS agenti qilib saqlab bo'lmaydi. Xodim akkaunti
  o'chirilganda ham (`StaffRelease`) agent xuddi shunday uziladi (WS + FCM tokeni).
- ⚠️ **STANDART Local SMS agentini o'chirish** barcha avtomatik Local SMS'ni to'xtatadi — amal
  bloklanmaydi, lekin javobda `warning` qaytadi (UI `alert` qiladi) va Sozlamalar → Xabar kanallari →
  SMS da banner turadi (`LocalSmsDefaultAgent`, `.claude/rules/messaging.md`).
- ⚠️ **Buyruq id'si = `CtiCommandLogs.Id`.** `dial`/`send_sms` da ilovaga aynan jurnal yozuvining id'si
  (`commandId`) yuboriladi, `ack` shu id bilan (va faqat o'z agentining buyrug'ini) "acked" qiladi,
  `SmsLog.RequestId` ham shu id — ilgari alohida GUID yuborilib, hech bir ack jurnalga mos kelmasdi.
- **`POST /api/mobile/calls` IDEMPOTENT** — kalit (agent + raqam + yo'nalish + startedAt); retry
  dublikat yozuv yaratmaydi. Javobda id **3 xil nom bilan** qaytadi (`{serverCallId, id, callId}`) —
  ilovaning qaysi versiyasi qaysi maydonni kutsa ham o'qiy oladi (sinxronlanmagan-retry muammosi).
- **Tarix RAQAM bo'yicha guruhlangan:** `GET /api/cti/calls/grouped` — har raqam bitta qator
  (qo'ng'iroqlar soni + oxirgisi); raqam bosilganda `GET /api/cti/calls?number=` bilan o'sha
  raqamning barcha qo'ng'iroqlari ochiladi.
- **Frontend:** `WunderkindLC.Client/src/pages/admin/calls/local/`; menyuda "Call Center — Local
  Call". Istalgan joydan qo'ng'iroq — `components/CallPickerModal.tsx` (raqam → agent-telefon →
  `dial`); provayder tanlash bosqichi YO'Q.

## Bulutli telefoniya OLIB TASHLANDI (2026-10-08)

Ilgari yonma-yon ikkinchi telefoniya — bulutli provayder (operator telefonidagi ilova, webhook +
tarix sinxroni, `/admin/calls` sahifasi, `calls.cloud` ruxsati) bor edi. Foydalanuvchi qarori bilan
u **butunlay olib tashlandi**: servislar, `api/admin/calls` va `api/telephony` endpointlari, CSRF
istisnosi, `.env`/compose kalitlari, sahifa, menyu bandi va ruxsat kaliti. Sabab — ikki parallel
telefoniya bir xil ishni ikki joyda yuritardi (tarix, yozuv, transkript ikki xil ekranda), amalda
esa Local Call ishlatiladi.

Nima QOLDI va nega:

- ⚠️ **`Calls` jadvali va qatorlari O'CHIRILMADI** (DROP — ma'lumot yo'qotish, egasining tasdig'i
  kerak). Unga endi HECH NARSA yozilmaydi; uni faqat o'quvchini tozalash (`StudentPurge` —
  `StudentId` uziladi) va zaxira nusxa o'qiydi. Yangi ko'rsatkichni bu jadvalga QURMANG.
- Eski `/admin/calls` manzili (xatcho'p) `/admin/calls/local` ga REDIRECT bo'ladi.
- Xodimlarning saqlangan ruxsatlaridagi eski `calls.cloud` kaliti hech narsani ochmaydi (zararsiz).

Bulutga TAYANGAN funksiyalar endi Local Call ma'lumotida ishlaydi (`KpiCallSource`,
`.claude/rules/kpi.md` §11.1): KPI kunlik «teginish» va «muloqot vaqti» normasi, cheklistning
`calls.missed_returned` va `calls.answer_speed` avto-bandlari, haftalik sifat nazorati namunasi
(yozuv `GET /api/cti/calls/{id}/audio` orqali tinglanadi). Xodim ↔ qo'ng'iroq bog'lanishi —
agentning `StaffUserId` si: **KPI'da hisobga tushishi uchun har xodimning telefoni (agenti) unga
BIRIKTIRILGAN bo'lishi shart.**
