---
description: Read-only MCP server (AI ulanish) — FAQAT O'QISH qoidasi, vosita yozish tartibi, faqat-o'qish baza roli (wlc_ro), OAuth.
paths:
  - "WunderkindLC.Application/Mcp/**"
  - "WunderkindLC.Server/Mcp/**"
  - "WunderkindLC.Infrastructure/Data/ReadOnlyDatabase.cs"
  - "WunderkindLC.Infrastructure/Data/McpReadOnlyRole.cs"
  - "WunderkindLC.Infrastructure/Data/McpModel.cs"
  - "WunderkindLC.Infrastructure/Mcp/**"
  - "WunderkindLC.Tests/Mcp/**"
---

# AI ulanish (read-only MCP) qoidalari

To'liq spetsifikatsiya — `docs/SPEC-mcp.md` (§5 vosita yozish tartibi MAJBURIY). Foydalanuvchi qo'llanmasi —
`docs/MCP.md`.

## 1. ENG MUHIM — QAT'IY FAQAT O'QISH (markaz egasining talabi, 2026-10-11)

"Yozishga, o'chirishga, qo'shishga hech qanday vosita bo'lmasin." Yozadigan vosita qo'shish — kod o'zgarishi
emas, EGASINING YANGI QARORI. Himoya qatlamlari (bittasi ham olib tashlanmasin):

1. Har vosita `ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false`, sinf faqat
   `McpToolContext` ga bog'liq (`McpReadOnlyTests` qulflaydi; manba kodida `SaveChanges`/`ExecuteUpdate`/
   `db.X.Add(` va h.k. bo'lsa test qizaradi).
2. `t.Db` — `ReadOnlyDatabase` konteksti: kuzatuvsiz, `SaveChanges` va SELECT bo'lmagan har buyruq RAD.
3. Baza roli `wlc_ro`: ilova har ishga tushganda (migratsiyadan keyin) uni yaratadi, `REVOKE ALL`, keyin faqat
   vositalar `Reads` ro'yxatidagi jadvallarga `SELECT` beradi; sessiya `default_transaction_read_only = on`.
   Prod'dagi kabi PostgreSQL'da tekshirilgan (2026-10-11): o'qish rejimi o'chirilsa ham yozish `permission denied`.
   ⚠️ Nomi nozik USTUN (`McpReadOnlyGrants.IsSensitiveColumn`: `…Url`, `…FileId`, `ChatId`, `Password`, `Token`,
   `Email`, `Permissions`, `Code`, `Answers`, joylashuv…) HECH QACHON berilmaydi — har jadvalda, kelajakdagi
   migratsiyada qo'shilgani ham: bunday jadvalga USTUN darajasidagi grant beriladi. O'quvchi parollari seyfi
   (`StudentCredentials.PasswordCipher`) aynan shu nom qoidasi bilan yopiq — jadvalni hech bir vosita `Reads` iga
   qo'shmang (`auth-sessions.md` §6.5). U `McpReadOnlyGrants.Forbidden` da ham, o'z-o'zini tekshirish taqiqlangan
   jadvalni `has_any_column_privilege` bilan tekshiradi: BITTA "zararsiz" ustun (`UserId`) qo'lda berilsa ham MCP o'chadi;
   MCP kodida `StudentCredential`/`CredentialVault` so'zi bo'lsa test qizaradi (`McpCredentialVaultTests`). MCP konteksti modelidan ham
   o'sha ustunlar olib tashlangan (`McpReadOnlyModel`), ya'ni to'liq entity yuklash yopiq ustunni SELECT qilmaydi.
4. O'z-o'zini tekshirish (`McpReadOnlyRole.SelfTest`) o'tmasa — MCP O'CHIQ (fail-closed). Yozadigan ulanishga
   QAYTISH YO'Q.

MCP kodidan faqat shu joylar yozadi: OAuth oqimi, so'rovlar jurnali (audit) va ulanishlarni boshqarish
(o'z/superadmin bekor qilishi, admin kaliti, hajm belgisi) — faqat `Mcp*` jadvallari, `AuditLog` va
`Users.AiAccessEnabled`. Hech biri VOSITA orqali emas.

## 2. Yangi vosita qo'shganda

- `t.Require(area)` — BIRINCHI qator; pul faqat `t.CanReadFinance` bilan; panelda hard rol bo'lsa
  `SuperAdminOnly` area. Bo'lim kalitlari `adminPermissions` katalogida bo'lishi shart (test).
- Nozik bo'lim — SAHIFA kaliti bilan (`students.list`, `finance.main`), bo'lim kaliti bilan emas
  (`permissions.md` §2: yuqoriga o'qish merosi).
- `public static readonly Type[] Reads` — vosita va u chaqiradigan servislar o'qiydigan entity'lar. Ro'yxatda
  bo'lmagan jadval prod'da `permission denied` beradi; testdagi `AssertReadsOnlyDeclared` buni oldindan ushlaydi.
- `AppUser` entity'sini YUKLAMANG — ismlar uchun `t.UserNamesAsync(ids)` (SQL'da id bo'yicha; `t.Users` ni BUTUN
  yuklamang — unda har o'quvchi/ota-ona akkaunti bor).
- Natijada MA'LUMOT MATNI KALIT bo'lmasin (`{ "Sababsiz": 3 }`): maxfiy-nom filtri "hash"/"token"/"login" li
  kalitni o'chiradi («Hashar» sababi shunday yo'qolgan edi) — `[{ name, count }]` massivi.
- `CenterMeta` YOPIQ (integratsiya sirlari) — undagi sozlama kerak bo'lsa, natijaga izoh yozing, jadvalni ochmang.
  Faqat ODDIY son/bayroq sozlamasi `RestrictedColumns[CenterMeta]` ruxsat RO'YXATIGA qo'shiladi (2026-10-12: davomat
  intizomi muddati/jarimasi, `RatingTestWeight`, «kelmadi» xabari kechikishi/tasdiq rejimi, TOP/BOTTOM N va qamrovi). ⚠️ Vosita chaqiradigan servis `CenterMeta` ustunini o'qisa va u
  ro'yxatda bo'lmasa — qisqartirilgan modelda ustun YO'Q: so'rov tarjima bo'lmaydi yoki to'liq yuklashda qiymat
  JIMGINA standartga tushadi (reyting vazni 60 bo'lib qolardi) — ustunni ro'yxatga qo'shing.
- Natijada login, parol, token, Telegram chat id, `/uploads/...` manzil, hujjat skani BO'LMAYDI.
- Panel bilan BIR XIL raqam: mavjud servisni chaqiring yoki controller so'rovini aynan takrorlang.
- `McpToolRegistry.ToolTypes` ga qo'shing; testlar `WunderkindLC.Tests/Mcp/` (`McpTestHost`).

## 3. Kim ulanadi

`McpAccess.IsAllowed`: **superadmin** — har doim · **admin** — FAQAT superadmin shu akkauntga yoqqan bo'lsa
(`Users.AiAccessEnabled`; 2026-10-11 dan, I7 — avval har admin avtomatik ulanardi) · **xodim** — `aiAccess`
ruxsati bilan. O'qituvchi/o'quvchi/ota-ona — HECH QACHON. ⚠️ Admin bayrog'i faqat `admin` rolida o'qiladi;
`AiAccessEnabled` faqat-o'qish roliga BERILMAYDI (`RestrictedColumns[AppUser]` — ruxsat RO'YXATI, test qulflaydi).

Ruxsat olinsa (rol kaliti, admin kaliti), foydalanuvchi o'chirilsa yoki paroli o'zgarsa
(`RefreshTokenService.RevokeAllForUserAsync`) — ulanish keyingi so'rovdayoq to'xtaydi; admin kaliti o'chirilganda
ulanishlari darhol BEKOR qilinadi ham.

**Boshqaruv — ikki joy, BITTA bekor qilish yo'li** (`Infrastructure/Mcp/McpConnections.RevokeAsync`):

| Joy | Kim | Server |
|---|---|---|
| Sozlamalar → Umumiy → «AI ulanishlar» (hammaning ulanishlari, admin kalitlari, 24 soatlik hajm belgisi, jurnal) | FAQAT superadmin (`superadminOnly` band) | `McpAdminController` — `[Authorize(Roles = SuperAdmin)]` |
| Akkaunt sozlamalari → «Mening AI ulanishlarim» (faqat O'Z ulanishlari) | superadmin/admin/xodim | `McpMyConnectionsController` (`api/mcp/my`) — har so'rov `NameIdentifier` bo'yicha, begona grant → 404 |

Har bekor qilish / admin kaliti o'zgarishi — audit (`McpGrant`, `McpAccess` → Sozlamalar).

**Superadminga Telegram** (`McpAlerts`, `McpAlertWorker`): yangi ulanish (kod almashilganda, fire-and-forget
navbat — OAuth oqimini hech qachon buzmaydi) va hajm ogohlantirishi (24 soatda > 1 000 so'rov yoki > 50 000 qator,
ulanish boshiga kuniga ko'pi bilan BIR marta — `McpGrant.VolumeAlertAt`, bazada). Matnda token/ID/chat id YO'Q.

**Refresh poygasi** (`McpTokens.RotateAsync`): rotatsiya shartli UPDATE (bittasi g'olib); 30 s ichida qayta
taqdim etilgan token — `invalid_grant`, ulanish SAQLANADI; keyin — ulanish bekor (o'g'irlik belgisi).

**Natija matni** (`McpText.CleanOutput`, `McpToolContext.Serialize` ichida — BITTA joy): ko'rinmas/bidi/nol
kenglikdagi belgilar olib tashlanadi, har satr 2 000 belgigacha. Vositada alohida tozalash YOZMANG.

## 3.5. CRM yangilanishi vositalari (2026-10-12, U2m–U6m) — ta'riflar PANELNIKI

To'liq ro'yxat — `docs/SPEC-mcp.md` §9. Har biri panel funksiyasini CHAQIRADI, qayta yozmaydi:

| Mavzu | Qoida |
|---|---|
| Telegram xabarlar (`MessagingTools`, `messages_history`) | sanoqlar `MessagingReports` dan (yetkazish jurnali; eski e'lon — saqlangan sanoq). Chat id, fayl manzili, Telegram file_id — HECH QACHON (ustun ham tanlanmaydi). «Telegram ulangan» = BLOKLANMAGAN chat bor (U6b ustuni) |
| Davomat (`AttendanceTools`, `AttendanceControlTools`) | «olingan» = `LessonNote.AttendanceTakenAt` (birinchi belgi, `AttendanceTaking`); «sababsiz» = `AbsenceReason.IsExcused == false` — NOMDAN taxmin qilinmaydi; belgi — `AttendanceRules`; qizil ro'yxat — `AbsenceStreakLoader`; intizom — `AttendanceDiscipline.Decide`; jarima summasi — faqat moliya ruxsati bilan |
| Reyting (`RatingTools`) | `RatingEngine` (Centre / GroupTable / StudentBlockAsync) — `RatingController` ruxsatlari bilan; `rating_group`/`rating_center` — ESKI faqat-ball ko'rinishi; `rating_top_bottom` — `TopBottomService.BuildAsync` (oylik avto-xabar yuboradigan AYNAN o'sha ro'yxat; sukut — O'TGAN oy) |
| Davomat xabarlari (`attendance_notices`) | `AbsentNoticeQueries.ListAsync` (Nazorat → «Davomat xabarlari»); kalit `students.notices` (panelda `ReadRequiresPerm` — `students.attendance` OCHMAYDI); yetkazish — faqat SANOQ (`TelegramOutbox.SummaryBySourceAsync`), chat id ustuni tanlanmaydi; to'xtatish/tasdiqlash — faqat panelda |
| Testlar (`TestTools`) | turi/savollar soni/yuborilgan vaqt; foiz — panel formulasi (`TestEntryRules.Percent`, `TestResultService.AvgPercent`: har natija foizi 0..100, o'rtacha shulardan) — `overview`/`DetailAsync` bilan bir xil |
| Jadval/topshiriqlar | `DatedSchedule` (≤ 7 kun, qavat), `RoomConflictService.AllPairsAsync`, `WorkTaskApproval.State`; «davomat olingan» (`attendanceTaken`) — `AttendanceTakenAt` (U3a), `Conducted` EMAS |

⚠️ Bir test hostida BIR NECHTA vosita chaqirilsa `AssertReadsOnlyDeclared` hammasining SQL'ini ko'radi — har vosita uchun
alohida host oching (aks holda boshqa vositaning jadvali "e'lon qilinmagan" bo'lib chiqadi).

## 4. Olib tashlanayotgan modullar

SMS, push, guruh chati, Local Call/CTI, onlayn test tuzish — vositalarda YO'Q va qo'shilmaydi
(`docs/GAP-crm-takomillashtirish.md`).
