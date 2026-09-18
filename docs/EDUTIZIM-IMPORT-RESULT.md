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
