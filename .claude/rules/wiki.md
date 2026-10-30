# Bilimlar bazasi (wiki) qoidalari

Xodimlarning SHAXSIY eslatmalari (insaytlar, ish rejasi, qaydlar) — Notion'ga o'xshash, CRM ICHIDA. Menyuda
«Bilimlar bazasi» (`/admin/bilimlar`). TEPADAGI TABLAR (egasining talabi, 2026-10-25): rahbariyat/menejerda —
**«Eslatmalarim | Xodimlar»**, oddiy xodimda — faqat **«Eslatmalarim»**.
⚠️ «Xodimlar» tabini RUXSAT KALITI hal QILMAYDI (yalang `wiki` ham `wiki.team` ni beradi): tab faqat rahbariyatga
yoki ko'radigan xodimi BOR menejerga chiqadi — `NotesPage.teamTabVisible` (`GET team/members` javobidan: `isLeader`
yoki `members` bo'sh emas). Bunday bo'lmagan xodim `/admin/bilimlar/xodimlar` ga kelsa — «Eslatmalarim» ga qaytariladi.

⚠️ **«Umumiy bo'limlar» (SOP, insaytlar — `WikiSpace`, `WikiController`, `wiki.spaces`) OLIB TASHLANDI** (egasining
talabi, 2026-10-25) — kod, jadval va ruxsat kaliti YO'Q. Modul faqat shaxsiy eslatmalardan iborat. Umumiy SOP kerak
bo'lsa — ALOHIDA ish (rahbar o'z eslatmasini qulflab, xodimlarga ko'rsatish bu o'rinni bosmaydi: eslatmani faqat egasi,
rahbariyat va menejeri ko'radi).

| Qism | Manzil | Ruxsat | API |
|---|---|---|---|
| Eslatmalarim (tab) | `/admin/bilimlar/eslatmalar/:pageId?`, o'qituvchi: `/teacher/eslatmalar` | **ruxsatsiz** — har bir xodim (staff/admin/superadmin) va o'qituvchi | `MyNotesController` — `api/me/notes` |
| Xodimlar (tab) | `/admin/bilimlar/xodimlar/:userId?/:pageId?` | `wiki.team` | `WikiTeamController` — `api/admin/wiki/team` |

Ikki tab — BITTA komponent `NotesPage` (`mode="mine" | "team"`). `/admin/bilimlar` — kirish nuqtasi (`WikiEntry`): har
doim «Eslatmalarim». Oraliq manzil `/admin/bilimlar/eslatmalar/xodim/...` → «Xodimlar»
(`LegacyTeamNotesRedirect`). RAHBARIYAT xodim eslatmalarida: «+» bilan sahifa/bo'lim qo'shadi, ⋯ → qulflaydi
(egasi ham faqat o'qiydi), «Tahrirlash» — "o'zgartirasizmi?" tasdig'idan keyin, ochiq eslatma «jonli» (4 s) yangilanadi.
Migratsiyalar: `AddWikiNotes`, `AddWikiSections`. Muharrir — BlockNote 0.51.4 (MPL-2.0) + Mantine 8 (MIT).

**Kod joylashuvi:**
- server — `Application/Services/Wiki/`: `WikiRules` (sof qoidalar), `WikiAccess` (so'rov egasi: rahbariyatmi, kimning
  eslatmalarini ko'radi), `WikiPageOps` (yaratish/saqlash/ko'chirish — ikki controller uchun BITTA joy);
  `Server/Controllers/Wiki/` — ikki controller;
- klient — `pages/admin/wiki/`: `NotesPage`, `WikiEntry`, `components/` (`WikiShell` — chap/o'ng panel va tablar,
  `WikiPageView` — sahifa, `WikiTreeRows`, `WikiSections`, `WikiEditor`, `WikiSearchResults`, `WikiSettingsModal`),
  `lib/` (`wikiModel` sof, `useWikiPage` avtosaqlash, `useWikiSearch`, `useStoredSet`, `wikiSchema`, `wiki.css`).

## 1. Model

| Entity | Vazifasi |
|---|---|
| `WikiPage` | ESLATMA: `OwnerUserId` (egasi), `ParentId` ("" = yuqori daraja), sarlavha, emoji, `IsSection` (§8.5), `ContentJson` (BlockNote bloklari), `ContentText` (faqat qidiruv uchun), `Version`, QULF |
| `WikiSettings` | BITTA qator: menejerlar bo'ysunuvchilar eslatmalarini ko'radimi + qaysi lavozimlar |

## 2. ⚠️ FAQAT MATN

Foydalanuvchi talabi: "videolar, rasmlarni yuklash shart emas, faqat yozuv va matn". Ikki qatlam:

- **Klient** — `wikiSchema.ts`: sxemada faqat `WIKI_BLOCK_TYPES` (paragraph, heading, quote, 4 xil ro'yxat,
  codeBlock, table, divider). `image`/`video`/`audio`/`file` "/" menyusida ham, paste'da ham yo'q.
- **Server** — `WikiRules.ParseContent`: boshqa blok/inline turi → 400. Havola faqat `http(s):`, `mailto:`, `tel:`
  (`javascript:` havolasi bosilganda CRM ichida kod ishga tushardi). Hajm ≤ 1 MB, ichma-ichlik ≤ 32.

⚠️ Ikki ro'yxat (`WIKI_BLOCK_TYPES` ↔ `WikiRules.AllowedBlockTypes`) AYNAN bir xil bo'lishi shart —
`wikiModel.test.ts` sxemani ro'yxat bilan, ro'yxatni BlockNote'ning haqiqiy turlari bilan qulflaydi. Natijada
`/uploads` ga hech narsa tushmaydi — `uploads-security.md` bu modulga tegmaydi. Rasm kerak bo'lsa — ALOHIDA ish.

## 3. RAHBARIYAT va ruxsatlar

**Rahbariyat** (`WikiViewer.IsLeader`) = admin, superadmin, platforma egasi YOKI HR'da «direktor» (`HrEmployee.HrRole =
director`, faol; «Topshiriqlar → Xodimlar» da belgilanadi). Rahbariyat: HAMMA xodimning eslatmalarini o'qiydi, ularga
sahifa/bo'lim qo'shadi va tahrirlaydi, QULF qo'yadi/oladi, sozlamani o'zgartiradi.

- `WikiTeamController` — `[AdminPerm("wiki.team", ReadRequiresPerm = true)]`. `MyNotesController` da `[AdminPerm]` YO'Q
  — `Allowed` (har endpointda): staff, admin, superadmin, platforma egasi, o'qituvchi. ⚠️ RUXSAT KALITI YO'Q (egasining
  talabi, 2026-10-27: "barchaga avtomatik ko'rinsin") — yangi akkaunt rolga qo'shilmasdan yozadi; menyu bandi ham ruxsatsiz.
  O'quvchi/ota-ona — 403. `WikiControllerGateTests` qulflaydi.
- `wiki.team` kaliti faqat «Xodimlar» SAHIFASINI ochadi; QAYSI eslatmalar — server hal qiladi (§9).

## 4. QULF (faqat o'qish)

- Qulf — FAQAT rahbariyat (`PUT team/pages/{id}/lock`, boshqaga 403).
- **Meros pastga** (`WikiRules.IsLocked`): eslatma qulflangan = o'zi YOKI birorta otasi (bo'lim ham) qulflangan.
- Egasiga qulflangan joyda: saqlash, ichiga sahifa qo'shish, ko'chirish (U YERDAN ham, U YERGA ham), o'chirish — 403
  `WikiRules.NoteLockedMessage`. O'chirishda ICHKI eslatmalardan birortasi qulflangan bo'lsa ham — 403.
- O'qish — qulf faqat yozishni yopadi. `WikiPageDto.EffectiveLocked` — klient muharrirni faqat o'qish rejimiga
  o'tkazadi va «Qulflangan — faqat o'qish» belgisini chiqaradi. Klientdagi `effectiveLocked` — serverdagining nusxasi,
  faqat KO'RINISH uchun; hal qiluvchi — server.

## 5. Avtosaqlash va TO'QNASHUV (`lib/useWikiPage.ts`)

- Yozish to'xtagandan `AUTOSAVE_MS` (1,2 s) keyin saqlanadi; sahifa almashganda/yopilganda kutib turgan o'zgarish
  DARHOL saqlanadi; brauzer yopilayotganda saqlanmagan bo'lsa — ogohlantirish.
- Har sahifa uchun ALOHIDA `Session` obyekti — eski sahifaning kechikkan saqlashi yangisiga aralashmaydi.
- ⚠️ Saqlash `baseVersion` talab qiladi: bazadagidan farq qilsa **409** (`conflict: true`, kim va qachon). Avtosaqlash
  TO'XTAYDI, «Sahifani yangilash» tugmasi chiqadi. Aks holda egasi va rahbar bir vaqtda yozsa, biri ikkinchisinikini
  JIMGINA o'chirardi. Birga yozish (real-time) ATAYIN yo'q — og'ir (Yjs + WebSocket).
- Sahifa ochilganda GET KESHSIZ (`X-No-Cache`) — 25 s'lik GET-kesh eski versiyani bermasin.
- Muharrir `key` = `id:version`; avtosaqlashda `page.version` ATAYIN yangilanmaydi (yozayotganda qayta yaratilmasin).
- Saqlash/ko'chirish mantig'i — `WikiPageOps` (sof, `WikiRulesTests`): o'z eslatmasi ham, rahbarning tahriri ham AYNAN shundan.

## 6. Daraxt

- Ko'chirish: boshqa ota ostiga yoki tartib. O'zining yoki ichki sahifasining ostiga — 400. Ota boshqa EGAGA tegishli
  bo'lsa — 400 (eslatma begona eslatma ostiga tushmaydi).
- O'chirish — ichki sahifalari bilan birga (klient oldin sonini aytib so'raydi).
- Klient `buildTree` otasi topilmagan/halqadagi sahifani YUQORIGA chiqaradi — jimgina yo'qolmaydi.
- Chegara: bir odamning eslatmalari ≤ 2000 (`WikiRules.MaxPagesPerOwner`).

## 7. Audit

⚠️ **Eslatmalar tarixga UMUMAN YOZILMAYDI** — tarixni `audit` ruxsatli har kim ko'radi, sarlavhalar esa shaxsiy.
Faqat `WikiSettings` (sozlama, qiymatlari bilan) — `AuditSections` da «Bilimlar bazasi» (`wiki`) bo'limi.

## 8. Zaxira, hajm, shrift

- `wikiPages`, `wikiSettings` — kunlik JSON zaxirasida.
- Muharrir `lazy()` bilan alohida chunk (`WikiEditor`, ~70 KB js + 34 KB css gzip) — boshqa sahifalarga tushmaydi.
  Mantine'ning GLOBAL uslublari olinmaydi — CRM uslubiga tegmaydi.
- ⚠️ **BlockNote 0.51.4 ga QAT'IY bog'langan** (`^` siz): 0.52–0.56 ning ixtiyoriy (Yjs) bog'liqligi `npm install` ni
  ERESOLVE bilan yiqitadi. Yangilashdan oldin `npm install --dry-run`; `--force`/`--legacy-peer-deps` ISHLATMANG.
- **Shrift** — CRM'niki (Nunito, `--font-sans`; BlockNote'ning Inter'i ATAYIN o'chirilgan), joylashuv Apple bilimlar
  bazasi uslubida (`wiki.css`: qator oralig'i 1.65, `#1d1d1f`, sarlavhalar sahifa nomidan kichik). O'lcham —
  A− / A+ (14–22 px, standart 16), har foydalanuvchining brauzerida eslab qolinadi (`wiki:fontSize`).

## 8.5. ESLATMALAR ICHIDAGI BO'LIMLAR (papkalar, 2026-10-27, migratsiya `AddWikiSections`)

Foydalanuvchi talabi: «Mening eslatmalarim» ostida o'z BO'LIMLARINI qo'shish («+ Yangi bo'lim» tugmasi).

- Bo'lim — alohida jadval EMAS: `WikiPage.IsSection = true`. Shuning uchun qulf merosi, o'chirish (ichidagilari bilan),
  egalik va 404 qoidalari o'z-o'zidan ishlaydi. Rahbar bo'limni qulflasa — ichidagi hamma sahifa egasiga faqat o'qish.
- ⚠️ Bo'lim FAQAT yuqori darajada (`WikiRules.SectionPlacementError`): yaratishda ham, ko'chirishda ham — 400.
- Yuqori darajada bo'limlar va «Mening eslatmalarim» sahifalari ALOHIDA tartiblanadi (`ApplyMove` va klient `moveTarget`
  ikkalasini alohida ro'yxat qiladi) — aks holda `index` ikki tomonda boshqa narsani bildirardi.
- Bo'limda matn yo'q (`ApplySave` kelganini tashlaydi), qidiruvda yo'q, xodimlar ro'yxatidagi «N ta eslatma» ga kirmaydi,
  bo'sh nomi — «Yangi bo'lim». Nomi `renameNote` bilan (oxirgi versiyani olib saqlaydi — 409 bo'lmaydi).
- Yaratish mantig'i — `WikiPageOps.Create` (o'zi ham, rahbariyat ham AYNAN shu).
- Klient: `components/WikiSections.tsx` (`SectionBlock`, `NewSectionButton`); yig'ilgan bo'limlar — `wiki:sectionsCollapsed`
  (standart ochiq). ⚠️ Nom maydonida takroriy yuborish ref bilan to'silgan: Enter → `disabled` → brauzer `blur` beradi
  va ikkinchi so'rov bitta bo'limni IKKI marta yaratardi (shunday bo'lgan).
- «Xodimlar» tabida rahbar xodimga ham bo'lim ochadi, nomini o'zgartiradi, qulflaydi; menejer — faqat o'qiydi.

## 9. SHAXSIY ESLATMALAR — kim o'qiydi

Foydalanuvchi talabi: administrator, menejer, ustozlarning eslatmalari direktorga ko'rinsin; hamkasbga — yo'q.

- Egasi o'z eslatmalarini `api/me/notes` orqali yozadi (egasi id'si TOKENDAN; begona eslatma — 404). `[NoStore]`.
- O'qituvchilar ham yozadi (portal → «Eslatmalarim», ruxsatsiz) — ular ham HR xodimi.
- **Kim o'qiydi** (`WikiViewer.CanSeeNotesOf`): egasi · rahbariyat (hammaniki) · HR menejeri — `WikiRules.TeamNoteOwners`:
  «Topshiriqlar → Xodimlar» daraxtidagi BARCHA bo'ysunuvchilari (ichma-ich, o'zisiz), faqat FAOL va haqiqiy akkauntli
  (`deleted:` — yo'q), sozlama yoqilgan bo'lsa va lavozimlar ro'yxati bo'sh bo'lmasa — faqat o'sha lavozimdagilar.
  Oddiy xodim — hech kimnikini. ⚠️ Bo'ysunish daraxti HR bilan BITTA manba (`HrTasksService.Subtree`) — kim kimga
  biriktirilgani bir joyda sozlanadi, bu yerda nusxa YO'Q.
- `wiki.team` faqat SAHIFANI ochadi; QAYSI xodimlar — server hal qiladi (HR `hrTasks` bilan bir xil printsip).

## 10. Xodimlar eslatmalari sahifasi va SOZLAMA

- `GET team/members` — ko'rinadigan xodimlar (o'zisiz): ism, lavozim, HR roli, eslatmalar soni, oxirgi yozgan vaqti
  (yozmaganlar ham — "kim umuman yozmayapti"). `GET team/members/{userId}` — daraxt, `GET team/pages/{id}` — eslatma.
  Ko'rinmaydigan xodim/eslatma — 404.
- YOZISH (`POST team/members/{userId}/pages`, `PUT team/pages/{id}`, `PUT team/pages/{id}/lock`, `DELETE team/pages/{id}`)
  — FAQAT rahbariyat (403); menejer faqat o'qiydi.
- Sozlama (`GET|PUT team/settings`) — faqat rahbariyat (403): «Menejerlar o'ziga biriktirilganlarning eslatmalarini
  ko'radi» + lavozimlar (HR lavozimlari; hech biri belgilanmasa — barchasi). Tarixga qiymatlari bilan yoziladi.

## 11. Testlar

`WikiRulesTests` (faqat matn, xavfli havola, daraxt, qulf merosi, `TeamNoteOwners`, `WikiViewer`, `WikiPageOps`),
`WikiControllerGateTests` (controller darvozalari — manba matni), `wikiModel.test.ts` (sxema ↔ ro'yxat, daraxt,
ko'chirish, qulf, shrift, tablar). `PermissionCatalogTests` — `wiki.*` kalitlari kodda ishlatilganini qulflaydi.
