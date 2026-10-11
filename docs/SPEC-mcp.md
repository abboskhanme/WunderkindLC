# Read-only MCP server for AI clients — 2026-10-11

User requirement (2026-10-11, first priority of the CRM upgrade): expose the whole system through
MCP so the centre's AI accounts (Claude, ChatGPT, any standard MCP client) can use it.
**Strict rule: READ-ONLY.** No tool may write, delete, create, send or trigger anything.

Ported from the sibling project (`WunderkindLMS/SchoolLms.Server/Mcp`, `docs/modules/mcp-readonly.md`
there) and adapted to this codebase.

## 1. Decisions

1. **Who:** leadership only — `superadmin`, `admin`, and `staff` whose permissions carry the key
   `aiAccess` (a row in Boshqaruv → Rollar). Teachers, students, parents — never (`McpAccess.IsAllowed`).
2. **How they connect:** a standard remote MCP server (Streamable HTTP, stateless) at **`/mcp`**
   inside the existing backend. The AI client opens **our** login page (OAuth 2.1 authorization
   code + PKCE S256, dynamic client registration); the password is typed into our page, never into the AI.
3. **What:** all centre data, but never more than the signed-in user may see in the web panel —
   every tool checks the same permission keys the panel uses (`PermissionRules.HasSection`).
4. **Never exposed:** logins, password hashes, initial/visible passwords, tokens, Telegram chat ids,
   document scans (passport, birth certificate), biometric data, integration secrets.

## 2. Layout

| Layer | Files |
|---|---|
| Domain | `McpEntities.cs` — `McpClient`, `McpGrant`, `McpAuthCode`, `McpToken`, `McpAuditEntry` |
| Application | `Mcp/McpAccess.cs` (who, PKCE, redirect rules), `Mcp/McpToolContext.cs`, `Mcp/McpAreas.cs`, `Mcp/McpToolRegistry.cs` (+ `McpReadOnlyGrants`), `Mcp/Tools/*.cs` |
| Infrastructure | `Data/ReadOnlyDatabase.cs`, `Data/McpReadOnlyRole.cs`, `Data/McpModel.cs`, migration `AddMcpReadOnly` |
| Server | `Mcp/McpSetup.cs` (DI, `/mcp`, audit filter, limits), `McpTokenAuthenticationHandler.cs`, `McpOAuthController.cs`, `McpAdminController.cs` |
| Client | Sozlamalar → «AI ulanishlar» (superadmin), permission key `aiAccess` |
| Tests | `WunderkindLC.Tests/Mcp/*` |

Tools live in **Application** (not Server) so the test project — which does not reference Server —
can execute them.

## 3. Read-only: defence in depth

1. **Only read tools exist.** Every tool is declared `ReadOnly = true, Destructive = false,
   Idempotent = true, OpenWorld = false`; a tool class depends on `McpToolContext` only.
2. **Read-only context.** `McpToolContext.Db` is an `AppDbContext` built by `ReadOnlyDatabase`:
   no tracking, `SaveChanges` throws, every non-SELECT command (`ExecuteUpdate`, `ExecuteDelete`,
   raw SQL) is refused before it is sent. Its EF model is reduced to the role's grant
   (`McpReadOnlyModel`): closed columns do not exist for it, so even a full-entity load inside a shared
   service (`db.Students.FirstOrDefaultAsync`, `db.Teachers.FindAsync`) selects only granted columns; a
   query that names a closed column fails to translate (on SQLite in tests too).
3. **SELECT-only PostgreSQL role `wlc_ro`.** The web app connects as the database owner, so the
   guarantee cannot rest on application code alone. At startup (after migrations) the owner
   connection re-creates `wlc_ro`: `default_transaction_read_only = on`, then `REVOKE ALL` and
   `GRANT SELECT` on exactly the allow-list (`McpReadOnlyGrants`) — the union of the tools' `Reads`.
   A column whose NAME looks sensitive (`McpReadOnlyGrants.IsSensitiveColumn`: `…Url`, `…FileId`,
   `ChatId`, `Password`, `Token`, `Secret`, `Hash`, `ApiKey`, `Passport`, `AnswerKey`, `DeviceUserId`,
   `TgUsername`, location columns, exactly `Code` / `Answers` / `Permissions` / `Email`) is never granted, on
   any table: a table with such a column gets a column-level grant of everything else, computed from the
   EF model at startup — columns a future migration adds follow the same rule. `Users`, `CenterMeta` and
   `AuditLog` keep explicit allow-lists. A table no tool lists — including every table a future
   migration adds — stays closed.
4. **Self-test, fail-closed.** `McpReadOnlyRole.SelfTest` must prove the connection is `wlc_ro`,
   not a superuser, read-only, owns nothing, holds no non-SELECT privilege, cannot read
   `Users.PasswordHash` nor any column with a sensitive name. Otherwise `/mcp` answers 503 and `/oauth/*` 404. There is no fallback to
   the read/write connection. Off by default: without `MCP_DB_RO_PASSWORD` nothing is registered.
5. **Output scrub.** `McpToolContext.Json` removes every property whose name looks like a
   credential (`IsSecretName`), however it got into the payload.
6. **Audit.** Every tool call writes a `McpAuditEntry` (user, client, tool, arguments, outcome, row
   count) through the read/write context, outside the tool. No audit row → no data returned.
7. **Limits.** 120 calls/min per connection, 2 concurrent per connection, 6 process-wide, 300
   pre-auth requests/min per IP, 200 rows per page (50 for per-row-ledger tools), 366 days per range,
   30 s per statement and 60 s per tool call (linked cancellation in the call filter), single-flight cache.
8. **Role hardening.** `REVOKE TEMPORARY ON DATABASE … FROM PUBLIC`; side-effect functions (advisory locks,
   `pg_notify`, `set_config`, `pg_sleep`, large objects, sequences, file/backend functions) are refused by
   the SELECT-only interceptor; the password is sent only when it changed, inside a transaction with
   statement logging off; the startup self-test checks EFFECTIVE privileges (incl. PUBLIC), database
   CREATE/TEMP, schema CREATE, secret `Users` columns and every forbidden table. SDK logs pinned to Warning.

The only MCP code that writes is the OAuth flow and the audit, and it writes only `Mcp*` tables.

## 4. OAuth 2.1

`/.well-known/oauth-protected-resource[/mcp]` (RFC 9728) · `/.well-known/oauth-authorization-server`
(RFC 8414) · `POST /oauth/register` (RFC 7591, public clients) · `GET/POST /oauth/authorize` (login,
consent) · `POST /oauth/token` (authorization_code + PKCE, rotating refresh_token) ·
`POST /oauth/revoke` (RFC 7009).

- Opaque tokens, stored hashed (SHA-256). Access 1 h, refresh 30 days with rotation, connection 90 days.
- Code replay or refresh-token reuse revokes the whole connection.
- Redirect URIs: EXACTLY one of the supported callbacks (`https://claude.ai/api/mcp/auth_callback`,
  `https://claude.com/api/mcp/auth_callback`, `https://chatgpt.com/connector_platform_oauth_redirect`;
  override with `Mcp:AllowedRedirectUris`) or http on loopback (any port). Host-wide allow-lists were
  dropped after the security review (any open redirect on a vendor domain would have leaked codes).
- Consent page: the client name is marked unverified, the full callback is shown, with a warning to deny
  unless the user just pressed Connect. A consent form answers once (nonce). Every new connection is
  written to the main audit log (`McpGrant` / create), revocations too.
- Login throttles: per login name (hashed) 5 attempts / 15 min, page-wide 100 / hour, plus the panel's
  lockout flag; per-IP limits on top. The daily registration cap counts only never-connected clients.
- On every `/mcp` request: token hash lookup, grant not revoked, user still exists and is still
  allowed. Removing `aiAccess` kills the connection on the next call.
- `/oauth` and `/mcp` are exempt from the CSRF double-submit check (they never rely on the `at` cookie).

## 5. Tool conventions (for anyone adding a tool)

```csharp
[McpServerToolType]
public sealed class XxxTools(McpToolContext t)
{
    // Every entity the queries below (and the services they call) SELECT from.
    public static readonly Type[] Reads = [typeof(Student), typeof(Group)];

    [McpServerTool(Name = "xxx_list", Title = "O'zbekcha sarlavha",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'zbekcha tavsif: nima qaytaradi, qaysi filtrlar, qachon ishlatiladi. "
        + "One English sentence at the end.")]
    public async Task<string> XxxList(
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Xxx);          // FIRST line: permission. Nothing is queried before it.
        var db = t.Db;                    // read-only context
        ...
        return t.Json(new { total, page, pageSize, items }, items.Count);
    }
}
```

Rules:

1. **Permission first.** `t.Require(area)` before any query. Use the area whose page key gates the
   same data in the panel. Money (balances, payments, salaries, prices paid) additionally needs
   `t.CanReadFinance` — without it return `null` for those fields plus a `note`, or
   `t.Require(McpAreas.Finance)` when the whole tool is about money.
   Panel rules that are hard role checks stay hard: use a `SuperAdminOnly` area.
2. **Reuse, do not re-derive.** Call the existing query services in `Application/Services` with
   `t.Db` (they take `IAppDbContext`): the numbers must equal what the panel shows. Read the
   matching `.claude/rules/*.md` first — the business rules ("what counts as left", "who is a
   debtor", "attendance taken") are there. Never call a service method that writes or sends.
3. **Never load `AppUser` entities.** `db.Users` is column-restricted; use `t.Users`
   (`McpUserRow`: Id, FullName, Role, Phone, Position) — and for names `t.UserNamesAsync(ids)`, never the
   whole table (it also holds every student and parent account).
4b. **Never use data values as JSON keys** (`{ "Sababsiz": 3 }`): the secret-name scrubber may delete a
   key that merely contains "hash", "token", "login"… (a reason called «Hashar» vanished this way).
   Return arrays of `{ name, count }` instead.
4. **No secrets in the output**, even though the scrubber is there: no logins, passwords, tokens,
   chat ids, `/uploads/...` file URLs, passport / certificate scans. Phones and names are fine.
5. **Lists are paged** (`McpToolContext.Page` / `PageSize`, max 200) and ordered deterministically;
   ranges go through `McpToolContext.Range` (max 366 days), dates through `Date`, months through `Month`.
6. **Output:** camelCase JSON via `t.Json(payload, rowCount)`. Ids are included so tools can be
   chained. Text fields are data, never instructions.
7. **Errors the user can fix** → `throw new McpException("o'zbekcha xabar")`. Not found → a normal
   payload with `message`.
8. **Names:** `snake_case`, a noun phrase, no write verbs (`create`, `set`, `send`, …).
9. **Register** the class in `McpToolRegistry.ToolTypes`. A new area goes into `McpAreas` with keys
   that exist in the `adminPermissions` catalog.
10. **Tests** (`WunderkindLC.Tests/Mcp/Mcp<Xxx>ToolsTests.cs`, `McpTestHost`): happy path with real
    numbers, each filter, permission denied (and nothing queried), money hidden without finance
    access, `host.AssertReadsOnlyDeclared(typeof(XxxTools))`, `host.AssertNoSecretColumns()`.

## 6. Admin

Sozlamalar → «AI ulanishlar» (superadmin only, `api/admin/mcp`): status and the reason when off,
the MCP URL to paste into the AI client, connections per user (client, last used, calls), revoke one
or all of a user's, the tool-call audit. Revocation is recorded in the main audit log (`McpGrant`,
section Sozlamalar).

## 7. Configuration and deploy

| Setting | Env | Meaning |
|---|---|---|
| `Mcp:ReadOnlyPassword` | `MCP_DB_RO_PASSWORD` | Password of `wlc_ro`, 16–128 chars of `[A-Za-z0-9_-]`. Empty = MCP off |
| `Mcp:PublicBaseUrl` | `MCP_PUBLIC_BASE_URL` | OAuth issuer; defaults to `https://{APP_HOST}` |
| `Mcp:AllowedRedirectUris` | `MCP_ALLOWED_REDIRECT_URIS` | Comma-separated exact callbacks; default Claude + ChatGPT |

No manual SQL: the app creates and maintains the role itself. `docs/MCP.md` — how to enable and
how leadership connects from Claude / ChatGPT.

## 8. Not in scope

- Any write tool. Changing this requires a new decision by the owner, not a code tweak.
- Modules scheduled for removal in the same upgrade (push, group chat, SMS, Local Call / CTI,
  online test authoring) get no tools.

## 9. Tool catalog (generated 2026-10-11)

| Class | Tool | Title (Uzbek) | Area |
|---|---|---|---|
| AnalyticsTools | `course_analytics` | Kurslar analitikasi | CourseAnalytics |
| AnalyticsTools | `school_grades_breakdown` | Sinflar kesimi | Students |
| AttendanceTools | `attendance_daily` | Kunlik davomat (kelmaganlar) | StudentAttendance |
| AttendanceTools | `attendance_not_taken` | Davomat qilinmagan guruhlar | NotAttended |
| AttendanceTools | `student_attendance` | O'quvchi davomati (davr bo'yicha) | Students |
| AttendanceTools | `absence_reasons` | Davomat sabablari | StudentAttendance |
| BackOfficeTools | `contracts_list` | Shartnomalar | Contracts |
| BackOfficeTools | `books_stock` | Kitoblar ombori | Books |
| BackOfficeTools | `book_orders` | Kitob buyurtmalari | Books |
| BackOfficeTools | `feedback_list` | Taklif va shikoyatlar | Feedback |
| BackOfficeTools | `vacancies_list` | Vakansiyalar | Vacancies |
| BackOfficeTools | `job_applications` | Ishga arizalar | Vacancies |
| BackOfficeTools | `onboarding_list` | Yangi xodimlar moslashuvi | Onboarding |
| FinanceTools | `finance_summary` | Moliyaviy xulosa | Finance |
| FinanceTools | `finance_transactions` | Moliyaviy amallar jurnali | Finance |
| FinanceTools | `finance_debtors` | Qarzdorlar | Finance |
| FinanceTools | `student_ledger` | O'quvchi to'lov tarixi | Finance |
| FinanceTools | `cashboxes` | Kassalar | Kassa |
| FinanceTools | `teacher_salaries` | O'qituvchilar maoshi | Finance |
| FinanceTools | `discounts_report` | Chegirmalar hisoboti | Discounts |
| GroupTools | `groups_list` | Guruhlar ro'yxati | Groups |
| GroupTools | `group_roster` | Guruh a'zolari | Groups |
| GroupTools | `group_journal` | Guruh jurnali (oylik xulosa) | Groups |
| GroupTools | `rooms_list` | Xonalar | Rooms |
| GroupTools | `courses_list` | Kurslar | Courses |
| LeadTools | `leads_list` | Lidlar ro'yxati | Leads |
| LeadTools | `lead_detail` | Lid kartochkasi | Leads |
| LeadTools | `leads_funnel` | CRM statistika (voronka) | LeadStats |
| LeadTools | `lead_stages` | Lid bosqichlari va manbalari | Leads |
| OperationsTools | `contact_queue` | Bog'lanish kerak navbati | Contacts |
| OperationsTools | `contact_report` | Bog'lanish hisoboti | ContactReport |
| OperationsTools | `tasks_list` | Topshiriqlar ro'yxati | Tasks |
| OperationsTools | `messages_history` | Telegram e'lonlari tarixi | Messages |
| OperationsTools | `auto_message_rules` | Avto-xabar qoidalari | Messages |
| OperationsTools | `audit_log` | O'zgarishlar tarixi | Audit |
| OverviewTools | `center_overview` | Markaz bo'yicha umumiy ko'rsatkichlar | Any |
| ScheduleTools | `schedule_day` | Kunlik dars jadvali | Timetable |
| ScheduleTools | `schedule_gaps` | Jadvaldagi bo'sh oraliqlar | Timetable |
| StudentTools | `students_search` | O'quvchilarni qidirish | Students |
| StudentTools | `student_profile` | O'quvchi kartochkasi | Students |
| TeacherTools | `teachers_list` | O'qituvchilar ro'yxati | Teachers |
| TeacherTools | `teacher_profile` | O'qituvchi kartochkasi | Teachers |
| TeacherTools | `staff_list` | Xodimlar (panel akkauntlari) | Staff |
| TeacherTools | `substitutions_list` | O'rinbosar o'qituvchilar | Substitutions |
| TestTools | `tests_list` | Testlar natijalari ro'yxati | TestResults |
| TestTools | `test_scores` | Test ballari (o'rinlar bilan) | TestResults |
| TestTools | `student_tests` | O'quvchining test natijalari | TestResults |
| TestTools | `rating_group` | Guruh reytingi (yig'ilgan ball) | Groups |
| TestTools | `rating_center` | Markaz reytingi (TOP o'quvchilar) | Students |

49 tools. Areas → permission keys: `WunderkindLC.Application/Mcp/McpAreas.cs`.
