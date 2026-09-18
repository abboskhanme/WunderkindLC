# edutizim → WunderkindLC data migration

**Decision (client, 2026-09-18):** move the centre's data out of edutizim into our system.

| Question | Answer |
|---|---|
| Source | **Official export from edutizim** (Excel/CSV, or a DB dump). We do not scrape. |
| Scope | **Master data in full** (students, parents, groups, teachers, courses, rooms, leads) + **money and attendance from 2026-09-01**. |
| Balances before 1 September | **One opening-balance row per student**, so balances match edutizim exactly. |

Everything below assumes those three answers.

---

## 1. Safety first — `IMPORT_MODE`

Our system does things on its own: it accrues monthly tuition on startup and every 12 hours
(`TuitionService.AccrueDue` — it rescans the WHOLE history, see `.claude/rules/membership-periods.md` §6),
sends an auto-message to the parent for every new charge, and sends debt reminders at 09:00.
On half-imported data that means **hundreds of false debt messages to parents**.

So the import runs with `IMPORT_MODE=1` (env, passed through `docker-compose.yml` as `Import__Mode`):

- no tuition accrual, no payment/lesson/birthday/trial reminders (their hosted services are not registered);
- `EskizService.SendSmsAsync` refuses to send with "Import rejimi yoqilgan";
- a loud warning is written at startup.

⚠️ **Turn it off after the import** (empty `IMPORT_MODE`, restart) — otherwise monthly charges are
never written and nobody sees why. Locked by `ImportModeTests`.

Order of operations:

1. `IMPORT_MODE=1` → restart → confirm the startup warning is in the log.
2. Back up the database (`docs/DEPLOY.md` backup section) — a restore point.
3. Import into the **dev** database first, verify (§5), only then production.
4. `IMPORT_MODE=` → restart → check that accrual runs and the numbers stay the same.

## 2. What to request from edutizim

Ask for **CSV or XLSX, UTF-8, one file per entity, with the internal IDs included** (the IDs are
what let us link rows and re-load safely). Request text to forward is in §6.

`✅` = we need it · `—` = we have no such feature, please skip.

| # | File | Rows | Fields we need |
|---|---|---|---|
| 1 | `students` | all, including archived | id · ism · familiya · otasining ismi · tug'ilgan sana · jinsi · telefon(lar) · manzil · qabul sanasi · holati (aktiv/sinov/muzlatilgan/arxiv) · arxiv sanasi va sababi · moderator · manba (lid manbasi) · izoh |
| 2 | `parents` | all | o'quvchi id · otasining ismi + telefoni · onasining ismi + telefoni · asosiy vakil · ish joyi (bo'lsa) |
| 3 | `groups` | all, including archived | id · nomi · kurs · daraja · o'qituvchi id · xona · dars kunlari · dars vaqti (boshlanish–tugash) · oylik narxi · boshlanish/tugash sanasi · holati |
| 4 | `group_students` (memberships) | all, including past | o'quvchi id · guruh id · qo'shilgan sana · holati (sinov/aktiv/muzlatilgan/chiqib ketgan/tugatgan) · aktivlashtirilgan sana · muzlatilgan sana · chiqqan sana · chiqish sababi |
| 5 | `teachers` / `employees` | all | id · F.I.Sh · telefon · lavozim/rol · fanlari · maosh turi (qat'iy/foiz) va qiymati · ishga kirgan sana · holati |
| 6 | `courses` | all | id · nomi · darajalari · standart oylik narxi |
| 7 | `rooms` | all | id · nomi · sig'imi · bino/qavat |
| 8 | `leads` (buyurtmalar) | all | id · ism · telefon(lar) · bosqichi · manba · kurs · moderator · yaratilgan sana · sinov darsi sanasi · izoh · o'quvchiga aylantirilganmi (o'quvchi id) |
| 9 | **`balances_2026_09_01`** | all students | o'quvchi id · **2026-09-01 holatidagi balans** (manfiy = qarz) |
| 10 | `payments` | **2026-09-01 dan** | id · sana · vaqt · o'quvchi id · guruh id · summa · to'lov usuli (naqd/karta/bank) · qaysi oy uchun · kvitansiya raqami · kim qabul qilgan · izoh · bekor qilingan/vozvratmi |
| 11 | `charges` (oylik hisoblar) | **2026-09-01 dan** | o'quvchi id · guruh id · oy ("yyyy-MM") · summa · chegirma · izoh |
| 12 | `expenses` (chiqimlar) | **2026-09-01 dan** | sana · summa · turi (ijara, maosh, kommunal …) · kimga · izoh |
| 13 | `salaries` | **2026-09-01 dan** | o'qituvchi id · qaysi oy · hisoblangan · berilgan · bonus · jarima · sana |
| 14 | `attendance` | **2026-09-01 dan** | guruh id · o'quvchi id · dars sanasi · holati (keldi/kelmadi/sababli) |
| 15 | `grades` (baholar) | **2026-09-01 dan** | guruh id · o'quvchi id · sana · ball/baho · mezon (bo'lsa) |
| 16 | `discounts` | amaldagilari | o'quvchi id · guruh/kurs · foiz yoki summa · boshlanish/tugash oyi · sababi |
| — | coins / gamifikatsiya | — | bizda yo'q, kerak emas |
| — | onlayn kurs, mavsumiy baholash | — | bizda yo'q, kerak emas |

**Two things that matter more than the rest:** file **#9** (opening balances) and the IDs in every
file. Without #9 every student's debt will be wrong; without IDs we cannot re-run the import
without creating duplicates.

## 3. How it maps to our tables

| edutizim | ours |
|---|---|
| students | `Students` (+ `Students.Balance` from #9) |
| parents | fields on `Students` (father/mother name + phone) |
| groups | `Classes` (Days, StartTime/EndTime, RoomId, TeacherId, MonthlyFee) |
| group_students | `StudentGroups` (Status, ActivatedAt, FrozenAt, LeftAt; closed spells → `PastPeriods`, see `.claude/rules/membership-periods.md`) |
| teachers | `Teachers` (+ salary mode/percent) |
| courses | `Subjects` |
| rooms | `Rooms` |
| leads | `Leads` (+ `LeadStages`, `LeadSources`) |
| balances_2026_09_01 | one `FinanceTransaction` per student: income/`other`, date `2026-09-01`, note "edutizimdan ko'chirilgan boshlang'ich balans" |
| payments | `FinanceTransactions` (income/`tuition`, `Month` = "qaysi oy uchun", `Method`, `ReceiptNo`) |
| charges | `MonthlyCharges` (Amount, Discount, Month, GroupId) |
| expenses | `FinanceTransactions` (expense + category) |
| salaries | `FinanceTransactions` (expense/`salary`, `Month` = the salary's month, not the pay date — `.claude/rules/billing.md`) |
| attendance | `JournalEntries` |
| grades | `CriterionGrades` / journal grades |
| discounts | `StudentDiscounts` (per course — `.claude/rules/discounts.md`) |

Rules that stay authoritative during the import: `billing.md` (Month vs Date), `membership-periods.md`
(spells), `discounts.md`, `year-freeze.md`, `audit.md` — the loaded rows must satisfy them, otherwise
our own reports will disagree with edutizim's afterwards.

## 4. How the loading is done

**No import module is built** (client's decision, 2026-09-18). The client hands over the exported
files and they are adapted and loaded by hand — one-off scripts against the database, entity by
entity, in this order:

courses → rooms → teachers → groups → students → parents → memberships → discounts →
opening balances → charges → payments → expenses → salaries → attendance → grades → leads.

Each entity is loaded into the **dev** database first and checked (§5) before the same load runs
against production. Rows that cannot be mapped are written to a reject list and reported — never
dropped silently.

## 5. Verification before we call it done

- Row counts per entity: ours = edutizim's export (±0).
- Σ balances matches edutizim's balance report; spot-check 10 students by hand, including one
  debtor, one with credit, one frozen, one archived.
- September income total matches edutizim's "Kirim chiqim"; expense total too.
- A group page shows the same members, the same attendance days and the same monthly fee.
- Then `IMPORT_MODE=` off → restart → the first accrual run must NOT create surprise charges for
  September (they are already imported); check the log line and the charge count before/after.

## 6. Text to send to edutizim (copy as is)

> Assalomu alaykum. Markazimiz ma'lumotlarini o'z tizimimizga ko'chirmoqchimiz. Iltimos, quyidagi
> ma'lumotlarni **Excel yoki CSV (UTF-8)** ko'rinishida, har bir bo'lim uchun alohida fayl qilib
> bering. Har bir faylda tizimdagi **ID ustuni** ham bo'lishi muhim.
>
> 1. O'quvchilar (arxivdagilari bilan): ID, F.I.Sh, tug'ilgan sana, jinsi, telefonlar, manzil, qabul
>    sanasi, holati, arxiv sanasi va sababi, moderator, manba, izoh.
> 2. Ota-onalar: o'quvchi ID, ota ismi va telefoni, ona ismi va telefoni.
> 3. Guruhlar (arxivdagilari bilan): ID, nomi, kurs, daraja, o'qituvchi, xona, dars kunlari, dars
>    vaqti, oylik narxi, boshlanish/tugash sanasi, holati.
> 4. Guruh a'zoliklari (o'tganlari bilan): o'quvchi ID, guruh ID, qo'shilgan sana, holati,
>    aktivlashtirilgan/muzlatilgan/chiqqan sanalari va chiqish sababi.
> 5. Xodimlar va o'qituvchilar: ID, F.I.Sh, telefon, lavozim, fanlari, maosh turi va qiymati.
> 6. Kurslar, 7. Xonalar, 8. Buyurtmalar (lidlar) — yuqoridagi maydonlar bilan.
> 9. **2026-yil 1-sentabr holatidagi har bir o'quvchining balansi** (qarz manfiy ko'rinishda).
> 10. 1-sentabrdan buyongi to'lovlar, 11. oylik hisoblar, 12. chiqimlar, 13. xodim maoshlari,
>     14. davomat, 15. baholar, 16. amaldagi chegirmalar.
>
> Gamifikatsiya (coinlar) va onlayn kurs ma'lumotlari kerak emas.
>
> Agar bazaning to'liq nusxasini (dump) berish qulayroq bo'lsa, biz uni ham qabul qilamiz.
