# edutizim → WunderkindLC: what was actually migrated (2026-09-18)

Source: ten Excel exports in `docs/excels/` (the client's own export from edutizim).
Loaded into the **dev** database first, verified, then into **production**
(`lc.wunderkindedu.uz`, server `2.28.36.240`) — both hold identical numbers.

## Loaded

| Entity | Rows | Note |
|---|---|---|
| Students | **2 211** | 1 028 archived, 1 183 not archived |
| Memberships (StudentGroups) | **759** | 728 students with an `active` membership, the rest `trial` |
| Groups (Classes) | **45** | 44 from the export + 1 created from a student reference |
| Teachers | 17 | names only — the export has no phone/salary data |
| Courses (Subjects) | 10 | built from the group's course + level pairs |
| Rooms | 13 | with seat counts |
| Leads | 45 | all put in the first stage ("Yangi") |
| Parents | — | folded into the student record (father's name + phone): 1 210 students got a parent phone, 880 a father's name |

**Balances match the export exactly:** total −6 966 642 · debt 141 779 872 · credit 134 813 230.
They are carried as each student's opening balance (`Students.Balance`), because the export
contains no payment history.

## Decisions taken while loading (all visible in the UI)

1. **3 duplicate students merged** — "Zuhra" appeared three times with identical data, and
   "Ravshanbek Asqaraliyev" twice (same name, same phone typed two ways).
2. **1 phone collision kept apart** — "Dilafruz Muhammadazimova" has the phone written as
   `+900598803`, which normalises to another student's number (`+998900598803`,
   "Muhammadazimova Ziyoda"). Different names → kept as two students. **Please check which
   number is right.**
3. **Two groups share one name** — "Avazxon Teacher | Elementary" exists twice in edutizim
   (Juft kunlar 15:30 and Toq kunlar 15:30). Both were created (the second is suffixed
   "(Toq kunlar)"), but the export does not say which student belongs to which, so **all 32
   members sit in the first one**. edutizim shows 20 + 23. **Needs splitting by hand.**
4. **One group was missing from the export** — "Dilnoza teacher | Cefr Intensive" is referenced
   by a student but absent from `guruhlar.xlsx`; it was created without a schedule so the
   student keeps a group. The home page flags it ("1 ta guruhning dars vaqti … noto'g'ri").
5. **Membership activation date = 2026-09-01**, while the join date keeps the original value.
   Reason: the agreement was "money from September". Without this, the moment a group price is
   entered, the accrual job would write monthly charges back to 2025 and every parent would
   become a false debtor (`.claude/rules/membership-periods.md` §6).

## Not migrated — the export does not contain it

- payments and monthly charges (history), expenses, salaries, bonuses/penalties
- attendance and grades
- discounts
- **group monthly fees** (`guruhlar.xlsx` has no price column) — every group is 0 so'm now
- teacher phones, salary mode/percent; most students' birth date, gender, address
- mothers' names and phones (the export only carries the father's)

Consequences to expect in the UI: Moliya reports are empty (no money history), a group's
per-student balance shows 0 (that number is computed from charges and payments), and the
student's own balance is correct.

## Still to do (morning)

1. Enter the **monthly fee** for each group (Guruh → group → Tahrirlash).
2. Split the two "Avazxon Teacher | Elementary" groups.
3. Check the phone collision in decision #2.
4. Then switch **`IMPORT_MODE` off** in `/root/WunderkindLC/.env` and restart:
   `docker compose up -d app`. Until then no monthly charges are written, no reminders and
   no SMS go out — deliberately.

Backup of the database as it was before the wipe: `/root/pre-import-2026-09-18-1518.sql.gz`.

## Second batch (2026-09-19): employees and branches

Source: `Xodimlar.xlsx` (38 rows) plus the branch and role screens the client sent.

| Loaded | Result |
|---|---|
| Teachers | 27 — the 17 created from the groups file were **updated** (phone, gender, subject), 10 **added** |
| Staff accounts | 11 moderators got a login + generated password (role `staff`, "Administrator" permission set) |
| Branches | 4 — Yaypan, Furqat, WET Baza, Yakkatut |

The passwords are visible in the panel: **Boshqaruv → Rollar → "Login/parollar"** (they disappear
from there once the person logs in for the first time).

⚠️ **The employee export is partial.** edutizim's roles screen shows 59 people (41 O'qituvchi,
6 Bosh Menejer, 6 Administrator, 5 CEO, 1 Akademik Direktor), but `Xodimlar.xlsx` contains 38
(27 teacher + 11 moderator) and every row says branch "Yaypan" — so the export looks filtered to
one branch. The remaining ~21 people are missing.

⚠️ Four teacher rows are duplicates of a moderator account (same person, placeholder phones
`+998999999999`, `+998995555555`, `+998996666666`) — they were loaded as teachers too; archive
them if they are not really teachers.

⚠️ Our data model has no employee↔branch link, so the branch column was not stored anywhere.

## Third batch (2026-09-19): the course catalogue

The client teaches four subjects, so the level-based course list built from the groups export
(`Ingliz tili — Elementary`, `… — Pre-Intermediate`, …, 10 entries) was replaced by five entries:

| Course | Groups |
|---|---|
| Ingliz tili 1 | 38 |
| Ingliz tili 2 | 0 — a price tier, waiting for the client to say which groups belong here |
| Koreys tili | 2 |
| Matematika | 2 |
| SAT | 3 |

Every group now has a course (the one group missing from the export, "Dilnoza teacher | Cefr
Intensive", was attached to Ingliz tili 1), and each teacher's subject list was recomputed from
the groups they actually teach. Applied to dev and to production.

⚠️ **The course IS the abonement.** `Subjects.Price` is the monthly price, and changing it writes
that price into `Classes.MonthlyFee` for every group of the course — optionally into the current
month's charges as well (`SubjectsController.Update` → `TuitionService.ApplyGroupFeeToCurrentMonthAsync`).
So the missing group fees (open issue #1) are filled by entering five prices, not 45.

## Closed by the client (2026-09-19)

- **The employee export is not short after all.** edutizim's roles screen counts people who have
  left; the 38 rows in `Xodimlar.xlsx` are the current staff. Nothing is missing.
- **Three "teacher" rows were duplicates of a moderator account.** The export lists Zuhra(xon)
  Islomova, Muhabbatxon Yusupova and Munojat Shermatova twice — once as `moderator` with a real
  phone, once as `teacher` with a placeholder (`+998999999999`, `+998995555555`, `+998996666666`)
  and no groups. The teacher rows are archived (reason recorded on the row); the staff accounts
  stay. Active teachers: 27 → **24**.
  Muzaffar Abdubannoyev and Nurhayotbegim Abduvaqqosova are also in both lists, but with two
  different real phones — they really are both, so both rows were kept.
- **One phone on two students is normal**, not a collision: siblings without their own phone share
  a parent's number. Nothing to fix, and more such pairs are expected.
- Still parked, to be decided later: splitting the two "Avazxon Teacher | Elementary" groups, and
  what to do with the 443 non-archived students who have no active membership.

## Fourth batch (2026-09-19): the leads table's empty columns

«Buyurtmalar ro'yxati» showed KURS DARAJASI and MODERATOR empty, with both values dumped into
the comment ("daraja: Beginner · moderator: …"). Fixed for all 45 leads:

| Column | Where it comes from now |
|---|---|
| **Moderator** | `Lead.AssigneeUserId` — the export's moderator name matched to the staff account (42 of 45; 3 rows say "undefined undefined" in the export) |
| **Kurs darajasi** | new `Lead.Level` field (migration `AddLeadLevel`, column only) — 29 leads are "Beginner", the rest have no level in the export |
| **Izoh** | now only what the export really had: "Kurs kuni: Toq kunlar" etc., 11 rows; no comments exist in the export |

⚠️ `Lead.Level` does not replace the level test: it only wins when filled. A lead that takes the
test still gets its level from `LevelTestSubmission`, exactly as before.

⚠️ **O'QITUVCHI stays empty, and that is correct** — in our model that column is the teacher of
the trial lesson's group, and the leads export carries no teacher at all.

On production the column and the data were applied ahead of the deploy (the migration is recorded
in `__EFMigrationsHistory`, so the next deploy skips it). The moderator shows immediately; the
level appears once the new build is deployed.

## Fifth batch (2026-09-20): the opening balance became real ledger rows

**The bug the client hit:** a student showed two different balances — one in the list and the
profile header, another (always 0) inside the group, in the journal and at the cash desk; and a
payment looked as if it changed nothing.

**Cause, not a code defect:** the import wrote the balance only into `Students.Balance`. That
field is the stored running balance, but everything else — "To'lash kerak", the per-group balance
(`GroupBalanceService`), the month list in the payment modal, the cash desk — is **computed** from
`MonthlyCharges` + `FinanceTransactions`, and those tables were empty (2 rows in the whole
database). So the computed side was 0 for all 2 211 students.

**Fix — the difference was materialised, per student:**

```
delta = Balance − (payments_net − charges_effective)
delta < 0  →  MonthlyCharge  (debt),  Locked = true
delta > 0  →  FinanceTransaction income/tuition (advance)
```

| | Rows | Total |
|---|---|---|
| Debt (`MonthlyCharges`) | 833 | 141 779 872 |
| Advance (`FinanceTransactions`) | 612 | 134 813 230 |

- **`Students.Balance` was not touched** — it was already right; `delta` subtracts what is already
  recorded, so the two test payments made through the UI are not counted twice.
- Month **2026-08**, date **2026-08-31** — the period *before* September, so September reports stay
  clean and `AccrueDue` never touches it (memberships are activated 2026-09-01). `Locked = true`
  keeps the fee-recalculation and the freeze purge away from these rows as well.
- Each row is **tagged with the student's group** (split by fee when there are several), because a
  tagged row lands 100 % on that group in the per-group balance. The 704 students with no active
  group get `GroupId = NULL`.

**Verified:** stored balance = computed balance for **2 211 of 2 211** students, on dev and on
production. Total unchanged.

To undo: `DELETE FROM "MonthlyCharges" WHERE "Month"='2026-08';` and
`DELETE FROM "FinanceTransactions" WHERE "Month"='2026-08' AND "CreatedBy"='Import';`
