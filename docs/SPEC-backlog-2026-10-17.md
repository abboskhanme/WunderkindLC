# SPEC — owner backlog of 2026-10-17 (finance, attendance, rating, Future → main)

Source: the owner's TZ «CRM Tizimini Rivojlantirish: Texnik Vazifalar Ro'yxati» (audio transcript, 2026-10-17).
Gap analysis was run against the code first; this SPEC covers only what is MISSING or PARTIAL. Items that
already exist are listed in §0 so nobody rebuilds them.

## 0. Already exists — do NOT rebuild

| TZ | What exists | Where |
|---|---|---|
| 1.3 cash date ≠ invoice month | `FinanceTransaction.Date` vs `Month`, PaymentModal asks both | billing.md |
| 1.5 teacher % salary, status, history | `SalaryLedger`, «Oylik chiqarish», teacher profile «Maosh» | billing.md |
| 1.6 discount does not cut teacher salary | since 2026-09 the base is gross `MonthlyCharge.Amount` (`SalaryChargedBaseFrom`) | discounts.md |
| 2.1 red list 3+/5+ (consecutive) | `AbsenceStreaks`, «Qizil ro'yxat» tab, auto contact request | contacts.md §9 |
| 2.2 TOP/BOTTOM, parent sees rank | `TopBottomService`, `/student/rating` | rating.md §7–8 |
| 3.2 kassa book sale → cash + books | `KassaController.Books`, `BookManualSale` | books.md §7–8 |
| 3.3 lead sources, lost-lead reasons | `LeadNotifier` callers, «Yo'qotilgan lidlar» | crm-leads.md |

## 1. Owner decisions (2026-10-17)

1. **Expense approval** — only expenses ABOVE a threshold set in settings wait for approval; smaller ones post at once.
2. **Staff payroll** — fixed monthly salary + KPI bonus; payments marked advance / final (teachers too).
3. **Rating** — homework is added with its own weight (settings); unmarked homework is skipped.
4. **Menu** — the TZ modules leave «Future»: Kassa, Kitoblar sotuvi, O'qituvchilar kabineti, Instagram marketing and
   the Future reports; a top-bar «Analitika» button opens the executive dashboard. Everything else stays in Future.

My calls (logged in `docs/ASSUMPTIONS.md`):

- Debt age uses the STRICT per-(group, month) ledger (`StudentGroupLedger`, what the cashier sees); the due date is day
  `CenterMeta.JournalUnpaidCutoffDay` (default 10) of the charge month; age = today − due date (≤ 0 → «muddati kelmagan»).
- «Arxivdagi qarzdorlar» = archived students; a checkbox adds them (default OFF), totals show the split.
- Tariff = gross price `MonthlyCharge.Amount`; discount is its own column.
- Poor payment discipline = collection < 60 % (same red as the existing groups view).
- Invoices = existing `MonthlyCharge` rows (no new table); «toifa» = the group's course.
- Attendance «dropped» = −10 percentage points or more against the previous period of equal length.
- Red list keeps «consecutive» and gains «total in period ≥ 3 / ≥ 5» mode.
- Lead auto-task goes round-robin to staff picked in settings; bell + Telegram (existing task assignment message).
- Executive dashboard — superadmin only (cashbox balances are superadmin-only today).
- Approver = superadmin always, plus staff holding the new `finance.approve` key; the maker can never approve their
  own request; a superadmin's own expense posts at once (there is nobody above to approve it).

## 2. Phases

Each phase ends with: backend tests, client tests + build, a review pass, deploy (backup first when a migration ships).

### F1 — Finance reports (no migration)

New report pages, all pure reports → `config/reports.ts` with `inNav: true` and in `mainReportRoutes` (main
«Hisobotlar»). Each has its own permission key under `finance` (permissions.md §10) and `ReadRequiresPerm = true`.

| Page | Route | Key | Endpoint |
|---|---|---|---|
| Qarzdorlik tahlili | `/admin/hisobotlar/qarzdorlik` | `finance.debts` | `GET api/admin/reports/debts` |
| Guruhlar va tariflar | `/admin/hisobotlar/tariflar` | `finance.tariffs` | `GET api/admin/reports/tariffs` |
| Hisob-fakturalar | `/admin/hisobotlar/hisob-fakturalar` | `finance.invoices` | `GET api/admin/reports/invoices` |

**R1 Debts** (`from`,`to` months, `includeArchived`, `groupId`, `teacherId`, `age` = all|7|30|60):
- KPIs: charged (gross), discount, net, paid, debt, collection % (= paid ÷ net), debtors count, split active/archived.
- Aging buckets by the OLDEST unpaid (group, month): not due, 0–6, 7–29, 30–59, 60+ days — count + sum.
- Matrix: rows = student × group (grouped by group, group subtotal), columns = months, cell = remaining debt of that
  (group, month); row total, oldest month, age days. Advances (overpay of a later month) never close an earlier month.
- Legacy charges without a group (`GroupId == null`) get their own «Guruhsiz» block (never silently dropped).

**R2 Tariffs** (`month`, `teacherId`):
- Per group: teacher (current), course, price distribution `[{price, count}]` from gross amounts, planned (gross sum),
  discount, net, collected (tuition paid FOR that month in that group), debt, collection %, red when < 60 %.
- Teacher summary: groups, students, planned, collected, debt, % — sorted worst first.

**R3 Invoices** (`from`,`to`, `status` = all|paid|partial|unpaid, `overdue` bool, `q`, `groupId`, `page`,`pageSize`):
- Row = one `MonthlyCharge`: charge date, month, student, group, course, amount, discount, paid, remaining, status,
  due date, overdue days. Totals over the whole filter (not just the page).

**R4 Discount coverage (1.6)** — «Chegirmalar hisoboti» gains «O'quvchidan tushgan» next to «Markaz qoplagan»
per month and per teacher. Teacher portal «Qarzdorlar to'lasa yana +X» must not include the discount share.

### F2 — Attendance and rating

**A1 Period attendance** — Davomat page new tab «Davr tahlili» (`?tab=davr`), `students.attendance`:
`GET api/admin/student-attendance/period?from&to&groupId&teacherId` (≤ 92 days, else 400):
totals (lessons, came, late, excused, unexcused, rate), per group (+ previous equal period, delta, «pasaydi» flag),
per reason (`AbsenceReason`, excused flag), per school grade, students with ≥ 3 / ≥ 5 absences in the period.
Uses the single attendance definition (`AttendanceRules`, journal.md) — never a private copy.

**A2 Group / grade averages** — Reyting report new tab «Guruhlar kesimi»: per group avg test %, avg ball %, attendance %,
rating avg; per school grade the same. From `RatingEngine` results (cached) + A1 attendance.

**A3 Homework in rating** (migration `RatingHomework`: `CenterMeta.RatingHomeworkWeight` int, default 0 → existing
ranks unchanged until the owner sets a weight). H = (done + 0.5 × partly) ÷ marked lessons × 100; unmarked skipped.
Weights tests / ball / homework; a missing component's weight is redistributed (existing rule for T/B extended).
Settings modal gets the third slider. rating.md §1 updated.

**A4 Teacher** sees the combined rating (`StudentRatingBlock` data) for own groups only.

### F3 — Cashboxes, expense approval, payroll (migrations)

**K1 Cashbox** — `Cashbox.Kind` (`cash`|`bank`, default cash), `OpeningBalance` decimal, `OpeningDate` date;
balance = opening + flows after it. Per-cashbox statement: opening / in / out / transfers / closing for a period.
Expense `Method` allowed (cash/card/transfer), optional.

**K2 Expense approval** — `CenterMeta.ExpenseApprovalThreshold` (0 = off). Manual expense with amount > threshold
(incl. salary payments) by a non-superadmin → `ExpenseRequest` row (pending), NOT a `FinanceTransaction`; nothing
reads it as money. Approve (superadmin or `finance.approve`, never the maker) → creates the transaction through the
same code path; reject needs a reason. List «Tasdiq kutayotgan chiqimlar» in Moliya; bell to approvers. Audited.

**K3 Payroll** — `StaffSalary` (UserId, MonthlyAmount, FromMonth); `FinanceTransaction.StaffUserId`,
`SalaryPayKind` (`advance`|`final`|null). Register page per month: staff rows (fixed + KPI bonus from the existing KPI
salary calc) and teacher rows (SalaryLedger) — accrued, advances, final, remaining; «To'lash» → expense (K2 applies).

### F4 — Leads, teacher portal, staff

- **L1** `WorkTask.LeadId` (migration), `CenterMeta.LeadAutoTask*` settings; on a new lead → task «Lidga qo'ng'iroq»
  due today, round-robin assignee, one open task per lead.
- **T1** Teacher portal group cards: month attendance % + debtors per group.
- **S1** Staff list: last login column + 30-day activity (audit actions, tasks closed, leads handled).
- **B1** Kassa: walk-in (non-student) book sale.

### F5 — Menu and executive dashboard

- **N1** Kassa → Moliya; Kitoblar sotuvi → O'quv bo'limi; O'qituvchilar kabineti → Xodimlar; Instagram marketing → its
  own main group; Future reports → main «Hisobotlar». Tests in `config/__tests__` updated.
- **D1** `/admin/analitika` (superadmin): month revenue (tuition / total income), cashbox balances, active debt sum +
  debtors, student flow (new / left, 6-month trend), lead conversion (lead → student) — each tile links to its report.
  Top-bar «Analitika» button.
