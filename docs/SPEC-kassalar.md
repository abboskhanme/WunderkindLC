# SPEC — Kassalar (cashboxes)

Date: 2026-10-05. Request: "kassa yaratish mumkin bo'lsin kassa bo'limida faqat super admin uchun".
User decisions (2026-10-05):
1. Money goes INTO a kassa — every income/expense records its kassa, each kassa shows a balance (edutizim parity).
2. A kassa has a responsible staff member; a payment entered by that person lands in their kassa
   automatically, and can be switched in the payment window.
3. Existing transactions go to one «Asosiy kassa».

## Data model

`Cashbox` — Id, Name (≤100), ResponsibleUserId (AppUser.Id, nullable), ResponsibleName (snapshot),
IsArchived, CreatedAt, CreatedByName. The main kassa has the fixed Id `main` (`CashboxService.MainId`);
it cannot be archived (it is the fallback).

`FinanceTransaction.CashboxId` (nullable, indexed). Balance = Σ income − Σ expense of non-voided rows.

Migration `AddCashboxes`: create table, add column + index, insert «Asosiy kassa» (`main`), backfill
`CashboxId = 'main'` for every existing transaction. `Program.cs` also ensures `main` exists (fresh DB).

## Which kassa a new transaction lands in — `CashboxService.ResolveAsync`

1. Explicitly chosen → must exist and be active, otherwise 400.
2. Otherwise the actor's own active kassa (ResponsibleUserId == actor; first by CreatedAt).
3. Otherwise `main` (if active) → otherwise the first active kassa → otherwise null.

Paths: PaymentIntake (admin payment + Kassa), FinanceController.Create (kirim/chiqim/maosh),
Refund (= the ORIGINAL payment's kassa — the box that took the money gives it back), advance split
(copies the original's kassa), archive restore (null/unknown → main). Edit (payment / transaction):
optional `cashboxId`, null = unchanged; a change is audited.

## API — `CashboxesController` (`/api/admin/cashboxes`, class `[AdminPerm("finance.main")]`)

| Endpoint | Who |
|---|---|
| `GET options` — active kassas + the caller's default (payment-window picker) | any admin-panel user (GET open to staff) |
| `GET ?from&to` — all kassas with balance + period in/out | `finance.main` (ReadRequiresPerm). NB: raw `GET finance/transactions` is still open to any staff (pre-existing), so this does not hide balances from a determined cashier. |
| `POST` · `PUT {id}` · `POST {id}/archive` · `POST {id}/restore` | **superadmin only** — hard role check, 403 with reason |

No new permission key (same reasoning as `year-freeze.md` §3: "only superadmin" cannot be expressed
with a key). Audit type `Cashbox` → section `finance`.

## Client

- Moliya → «Kassalar» (`/admin/finance?tab=cashiers`): kassa cards (name, balance, responsible,
  period +/−) above the existing «Kassirlar kesimi». Superadmin: «Yangi kassa qo'shish», edit, archive,
  restore; archived list behind a toggle.
- Payment window (admin + Kassa), Kirim/Chiqim form, payment edit: «Kassa» select, shown only when
  there are 2+ active kassas; preselected with the caller's default.
- «To'lovlar» tab: «Kassa» filter (2+ kassas).

## Out of scope (next steps)

«Ko'chirish» (transfer between kassas), per-kassa transaction page, kassa filter in reports.
