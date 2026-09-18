# edutizim parity — the admin panel looks and works like edutizim

**Decision (client, 2026-09-18):** staff already work in edutizim (`wunderkindyaypan.edutizim.uz`)
and are used to it, so our admin panel must be a faithful copy of it — layout, menu, labels,
page structure. Rules:

1. Everything edutizim has **and we also have** → looks and is placed exactly like edutizim.
2. Things **we have and edutizim doesn't** → collected in the **"Future"** menu at the bottom.
3. Things **edutizim has and we don't** (Gamifikatsiya, Onlayn kurs, Mavsumiy baholash, Savdo
   plani, Yangiliklar, Hikoya, Filiallar holati …) → **not built, not shown** ("umuman kerak emas").
   ⚠️ **Exception — Moliya:** the client later asked for the Moliya section to be complete, so its
   pages (Jarima, Tushum rejasi, Moliya analitikasi, Moliya hisobotlari, P&L, Pul oqimi,
   Tranzaksiya turi, Rejalashtirilgan xarajatlar) were built.
4. edutizim is **read-only** for us — never change anything there while cataloguing.
5. **Scope (client, same day):** only the CORE sections are copied page-by-page — **Lidlar,
   Guruh, O'quvchilar, Moliya** and what belongs to them (group page, student profile, payments,
   lesson schedule, home). Nazorat, Boshqaruv, Sotuv va marketing, Hisobotlar and Sozlamalar keep
   their menu place (edutizim tree) but open our existing pages as they are — no redesign effort.

Reference: `docs/edutizim/INVENTORY.md` (structure, tokens, every page) and
`docs/edutizim/screens/` (screenshots — contain real PII, gitignored, never commit).

Business rules in `.claude/rules/*` stay authoritative — this is a presentation change.
A page may be rebuilt, but the numbers it shows must come from the same services as before.

## Design system (from the inventory §1)

| Token | Value |
|---|---|
| Font | Nunito |
| Primary | `#3D68FF` (`brand-600`) |
| Content background | `#F0F2F2` (`--bg`) |
| Card | white, 1px `#DBE0E6`, radius 12, shadow `0 1px 2px rgba(0,0,0,.05)` |
| Hover / active nav | `#E6EAEA` bg, primary text, 3px primary bar on the left |
| Page title | 18px / 600 / `#000` |
| Button | 31px high, radius 8, 12px / 500, 20px leading icon |
| Table (MUI DataGrid look) | header 56px, 12px/600 UPPERCASE; rows 52px, 13px/500 |
| Filter input | 34px, radius 8 |
| Icons | Tabler outline (`@tabler/icons-react`) |

## Menu mapping (edutizim → ours)

`✅` done · `🟡` mapped to an existing page, page not yet restyled · `⬜` to build (we have the
data, only the page is missing) · `—` out of scope (rule 3).

| edutizim | Ours | Status |
|---|---|---|
| **Topshiriqlar** | `/admin/topshiriqlar` | 🟡 |
| **Lidlar** → Buyurtmalar ro'yxati | `/admin/leads` (table; `?view=kanban`; lead page `/admin/leads/:id`) | ✅ |
| Lidlar → Birinchi darsga yozilganlar | `/admin/leads/birinchi-dars` | ✅ |
| **Guruh** → Guruh | `/admin/classes` | ✅ |
| Guruh → Barcha vazifalar | — | — |
| Guruh → Dars jadvali | `/admin/jadval` (home grid, no cards); our gap analysis → Future | ✅ |
| Guruh → Xonalar | `/admin/rooms` (edutizim list + Analitika tab) | ✅ |
| Guruh → Guruh o'quvchilari | `/admin/classes/oquvchilar` | ✅ |
| **O'quvchilar** → Yangi o'quvchilar | `/admin/students/yangi` | ✅ |
| O'quvchilar → Aktiv o'quvchilar | `/admin/students/aktiv` | ✅ |
| O'quvchilar → Arxiv o'quvchilar | `/admin/students/arxiv` (our multi-archive page → Future) | ✅ |
| O'quvchilar → O'quvchilar ro'yxati | `/admin/students` (+ To'lov sanasi · Yaratilgan · Manba · Moderator) | ✅ |
| O'quvchilar → Ota-ona | `/admin/students/ota-ona` (app-parent accounts → Future) | ✅ |
| O'quvchilar → Joriy oyda obunasi tugaydiganlar | `/admin/students/obuna-tugaydi` | ✅ |
| O'quvchilar → O'quvchilar manzillari | `/admin/locations` | 🟡 |
| **O'quv bo'limi** → Oflayn kurslar | `/admin/subjects` | 🟡 |
| O'quv bo'limi → Shartnoma | `/admin/contracts` | 🟡 |
| **Moliya** → Kassalar | `/admin/finance?tab=cashiers` (our payment desk → Future) | ✅ |
| Moliya → Bonus | `/admin/finance?tab=bonuses` | 🟡 |
| Moliya → Oylik chiqarish | `/admin/finance?tab=teachers` | 🟡 |
| Moliya → Kirim chiqim | `/admin/moliya/kirim-chiqim` (donut + categories) | ✅ |
| Moliya → Tranzakisyalar | `/admin/finance?tab=payments` | 🟡 |
| Moliya → Jarima | `/admin/moliya/jarima` (expense category `penalty`) | ✅ |
| Moliya → Tushum rejasi | `/admin/moliya/tushum-rejasi` | ✅ |
| Moliya → Moliya analitikasi | `/admin/moliya/analitika` | ✅ |
| Moliya → Moliya hisobotlari | `/admin/moliya/hisobotlar` | ✅ |
| Moliya → Moliya hisobotlari (P&L) | `/admin/moliya/pnl` | ✅ |
| Moliya → Pul oqimi | `/admin/moliya/pul-oqimi` (operating section only) | ✅ |
| Moliya → Tranzaksiya turi | `/admin/moliya/tranzaksiya-turi` (read-only catalog) | ✅ |
| Moliya → Rejalashtirilgan xarajatlar | `/admin/moliya/rejalashtirilgan-xarajatlar` (new table `PlannedExpenses`) | ✅ |
| Moliya → Shartnoma | `/admin/contracts` | ✅ |
| **Nazorat** → Davomat | `/admin/students/davomat` | 🟡 |
| Nazorat → Davomat analitikasi | — (rule 5) | — |
| Nazorat → Fikr-mulohaza | `/admin/boshqaruv/feedback` | 🟡 |
| Nazorat → Xodimlar reytingi | `/admin/teacher-reports` | 🟡 |
| Nazorat → Davomat qilinmagan guruhlar | `/admin/nazorat/davomat-qilinmagan` | ✅ |
| **Boshqaruv** → Xodimlar | `/admin/teachers` (teachers; staff accounts under Rollar) | 🟡 |
| Boshqaruv → Rollar | `/admin/boshqaruv/staff` | 🟡 |
| Boshqaruv → Filiallar | `/admin/boshqaruv/branches` | 🟡 |
| **Sotuv va marketing** → Xabarlar ro'yhati | `/admin/messages` | 🟡 |
| Sotuv va marketing → SMS shablonlari | — (rule 5) | — |
| **Hisobotlar** → Sotuv voronkasi | `/admin/crm-stats` | 🟡 |
| Hisobotlar → Kirim chiqim | `/admin/moliya/kirim-chiqim` | ✅ |
| Hisobotlar → Xonalar analitikasi | `/admin/rooms/utilization` | 🟡 |
| Hisobotlar → Xodimlar reytingi | `/admin/teacher-reports` | 🟡 |
| **Sozlamalar** → Umumiy sozlamalar | `/admin/settings/school` | 🟡 |
| Sozlamalar → Moliya | `/admin/settings/check` | 🟡 |
| Sozlamalar → Ilova sozlamalari | `/admin/settings/apk` | 🟡 |
| **Home** (Dars jadvali + 12 cards) | `/admin` | ✅ |
| Group detail (left card + tabs, O'quvchilar first) | `/admin/classes/:id` | ✅ (our extra tabs kept after) |
| Student profile (edutizim left card) | `/admin/students/:id` | ✅ (tab order still ours) |

edutizim itself lists "Kirim chiqim" and "Xodimlar reytingi" in two groups; we mirror that
(`edutizimDuplicateRoutes` in `config/navigation.ts`, locked by `reports.test.ts`).

**Future** (ours only): Bog'lanish kerak, Izohlarga javoblar, Ushlab turish bonusi, Yuz bilan
kirish · O'quv dasturi, Baholash mezonlari, Testlar natijalari, Formalar, Kitoblar sotuvi,
Sabablar · Guruh chati, Support Telegram, Call Center · AI check, ilova Support/O'qituvchilar ·
Instagram marketing · Hisobotlar hub + our extra reports · KPI, Vakansiyalar, Kameralar ·
extra Sozlamalar (Landing, Tumanlar, Kanallar, Zaxira, Azure, Gemini, Turniket, Kamera, PostHog).

## Phases

1. **Shell** — tokens, Nunito, Tabler icons, full-width top bar, 173/72px sidebar with hover
   flyouts, menu tree + Future. ✅
2. **Home** — Dars jadvali grid + 12 cards (`GET /api/admin/dashboard/summary`, `GET /api/admin/schedule/grid`). ✅
3. **Shared primitives** — `components/ui/list/*` (DataGrid-style table, filter grid, toolbar
   with tinted icon buttons, "Umumiy soni" pill, phone chip), "50 qator" pagination. ✅
4. **Core pages** (rule 5) — Lidlar (table/kanban, lead page, Birinchi darsga yozilganlar),
   Guruh (list, group page, Guruh o'quvchilari), O'quvchilar (lists + ⬜ lists, student
   profile), Moliya (Kassalar, Kirim chiqim, Tranzaksiyalar, Oylik chiqarish, Bonus, payment
   modals).
5. Nothing else is planned (rule 5).
