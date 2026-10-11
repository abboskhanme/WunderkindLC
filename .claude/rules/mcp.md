---
description: Read-only MCP server (AI ulanish) — FAQAT O'QISH qoidasi, vosita yozish tartibi, faqat-o'qish baza roli (wlc_ro), OAuth.
paths:
  - "WunderkindLC.Application/Mcp/**"
  - "WunderkindLC.Server/Mcp/**"
  - "WunderkindLC.Infrastructure/Data/ReadOnlyDatabase.cs"
  - "WunderkindLC.Infrastructure/Data/McpReadOnlyRole.cs"
  - "WunderkindLC.Infrastructure/Data/McpModel.cs"
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
   migratsiyada qo'shilgani ham: bunday jadvalga USTUN darajasidagi grant beriladi. MCP konteksti modelidan ham
   o'sha ustunlar olib tashlangan (`McpReadOnlyModel`), ya'ni to'liq entity yuklash yopiq ustunni SELECT qilmaydi.
4. O'z-o'zini tekshirish (`McpReadOnlyRole.SelfTest`) o'tmasa — MCP O'CHIQ (fail-closed). Yozadigan ulanishga
   QAYTISH YO'Q.

MCP kodidan faqat ikki joy yozadi va faqat `Mcp*` jadvallariga: OAuth oqimi va so'rovlar jurnali (audit).

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
- Natijada login, parol, token, Telegram chat id, `/uploads/...` manzil, hujjat skani BO'LMAYDI.
- Panel bilan BIR XIL raqam: mavjud servisni chaqiring yoki controller so'rovini aynan takrorlang.
- `McpToolRegistry.ToolTypes` ga qo'shing; testlar `WunderkindLC.Tests/Mcp/` (`McpTestHost`).

## 3. Kim ulanadi

superadmin · admin · `aiAccess` ruxsatli xodim (`McpAccess.IsAllowed`). O'qituvchi/o'quvchi/ota-ona — HECH QACHON.
Ruxsat olinsa, foydalanuvchi o'chirilsa yoki paroli o'zgarsa (`RefreshTokenService.RevokeAllForUserAsync`) —
ulanish keyingi so'rovdayoq to'xtaydi. Boshqaruv — Sozlamalar → Umumiy → «AI ulanishlar» (FAQAT superadmin,
`superadminOnly` band; `McpAdminController` — `[Authorize(Roles = SuperAdmin)]`).

## 4. Olib tashlanayotgan modullar

SMS, push, guruh chati, Local Call/CTI, onlayn test tuzish — vositalarda YO'Q va qo'shilmaydi
(`docs/GAP-crm-takomillashtirish.md`).
