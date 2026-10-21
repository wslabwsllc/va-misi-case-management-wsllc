# 0014. System-wide audit log, correlation IDs and the audit viewer

* Status: accepted (extends [0006](0006-audit-trail.md); case and attachment entries stay in the case trail)
* Owner: E3

## Context

Change 1, part 2 asks for a system-wide audit log: who did what and when across the application, with before and after
values, a correlation ID, a viewer and a CSV export. Until now only cases and attachments had an audit trail
([ADR 0006](0006-audit-trail.md)) and test results had their own (ADR 0012); sign-ins, access denials and the user and
role administration of [ADR 0013](0013-database-roles-and-permissions.md) were only log events. The operation ID that
correlates log lines existed only inside log scopes, and interactive (circuit) actions had none.

## Decision

**Store.** A new append-only table `AuditLogEntries` (migration `C2_AuditLog`): UTC time, actor ID and name (both null
for an unknown or anonymous actor), category, action, outcome (`Success`, `Failure`, `Denied`), subject (entity type,
ID and display name), a short summary, `Before` and `After` as `jsonb` objects of field → value, and the correlation
ID. No foreign keys, so an entry outlives the user or role it describes. Like `CaseAuditEntries`, every column rejects
updates and the SaveChanges guard rejects modifying or deleting either table's entries
(`AuditTrailModificationException`). `CaseAuditEntries` and `TestResultAuditEntries` gain a nullable `CorrelationId`.

**What is recorded.**

| Category | Actions | Written |
|---|---|---|
| Authentication | `SignedIn`, `SignInFailed` (wrong password, unknown account, deactivated), `SignInLockedOut`, `SignedOut`, `PasswordChanged`, `PasswordChangeFailed` | on their own context (`AuditLogWriter`) |
| Authorization | `AccessDenied` (page or endpoint, missing permissions) for HTTP requests and interactive navigation | on their own context |
| Users | `UserCreated`, `UserUpdated`, `UserDeactivated`, `UserReactivated`, `UserPermissionsChanged`, `UserUnlocked`, with the changed fields before and after | in the change's transaction (`SystemAudit.Append`) |
| Roles | `RoleCreated`, `RoleUpdated`, `RoleDeleted`, with name, description and permissions before and after | in the change's transaction |
| Audit log | `AuditExported`, with the filters (text filters recorded only as "(text filter)") | on its own context |
| Cases, Attachments, Test results | the existing trails (ADR 0006, ADR 0012), read by the viewer | unchanged, now with the correlation ID |

Values are names, IDs, flags, role and permission names only: never passwords, hashes, tokens, SSNs, veteran details,
case text, or what someone typed into the sign-in form for an account that does not exist. A refused change
(validation, conflict, escalation) writes nothing. An entry written on its own context that fails to save is logged as
`AuditWriteFailed` (1020) and does not fail the sign-in or page: an audit outage must not lock everyone out.

**Correlation.** `OperationContext` (Contracts) holds the ambient operation ID (`AsyncLocal`). The HTTP middleware sets
it for each request (the same ID as the `OperationId` log scope and the `X-Operation-Id` response header);
`AuditOperation.BeginIfNone` starts one, with its log scope, for each data-changing service call made from a circuit
(user and role administration, case create, edit, assign and status change, attachment upload). Every audit row
stores it, so an entry leads to its log lines and to the other entries of the same operation.

**Viewer** (`audit.view`, Administration > Audit log, `IAuditLogService` in Identity). Filters: Eastern date range,
category, outcome, actor name, subject (type and ID, set by the History links) and a text search over action, subject,
summary and correlation ID; newest or oldest first; 50 per page. Each source is filtered and ordered in SQL and read up
to the rows the page needs, then the sources are merged by time; a page deeper than the first 10,000 matches is
refused with a request to narrow the filters. An entry page shows every field, the before/after table and a link to
the entries of the same operation. The user detail page and the role editor show the latest ten entries about that
user or role with a link to all of them.

**Export.** `GET /exports/audit.csv` with the viewer's filters, `audit.view` required (403, no redirect, otherwise),
newest first, at most 10,000 rows (`X-Export-Truncated: true` when cut short), UTC ISO times like every export,
formula-safe cells (`CsvText.Cell`, now shared with the case export), UTF-8 with BOM, `no-store`. The export is audited.

**Permission.** `audit.view` is granted by default to Auditor (oversight), Supervisor (history of the users they
manage) and Administrator. The seed now records, per built-in role, the defaults it has already applied
(`AspNetRoles.DefaultPermissionsApplied`, replacing the earlier yes/no flag), so a default added later (like this one)
reaches already-seeded databases once, while a default an administrator removed is never granted again.

## Alternatives considered

- **One table for everything (move case and test-result entries into `AuditLogEntries`):** a single query, but it would
  rewrite two established trails, their tests and ADR 0006's same-transaction guarantee for cases. Reading three
  sources keeps each module's trail where it is.
- **A SQL `UNION ALL` view:** EF Core refuses set operations over columns of different store types, and the three tables
  differ (`varchar` lengths, enum mappings). A merge over per-source queries is simple and indexed per source.
- **Database triggers:** capture every row change but not the application actor or the correlation ID, and would record
  password hashes unless carefully excluded.
- **Auditing every authorization check:** `AuthorizeView` evaluates policies just to decide what to show; only real
  denials (a page or endpoint refused) are recorded.

## Tradeoffs

- Deep pages cost more: page N reads up to N × page size rows from each source; capped at 10,000.
- Writes outside a transaction (sign-in, denials) can be lost if the database fails at that moment; the failure is logged.
- Case and test-result entries record status changes and safe details only, not full before/after values (ADR 0006).
- Immutability is enforced by the application, not against a database administrator (as ADR 0006).

## Consequences

- New administration or security operations add a `AuditLogActions` constant and call `SystemAudit.Append` inside their
  transaction, or `AuditLogWriter.WriteAsync` when there is no business change.
- Data-changing service methods called from circuits begin an operation with `AuditOperation.BeginIfNone`.
- Part 3 (error log) reuses `OperationContext` so an error can be found from an audit entry and the other way round.
