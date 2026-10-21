# Change-implementation record

Required by RFQ E.3 for the Government-provided requirement change (assumption G1, plan E1-S9). Fill in one section
per change. Implement the change in separate commits on a branch merged with a merge commit (no squash or rebase).

## Change 1: Configurable roles, database permissions and user administration (part 1 of 5)

The change is being delivered in five parts, each on its own branch and verified before the next: (1) database permissions
and user and role administration, (2) system-wide audit log and viewer, (3) error log store, error viewer, System Health
and error pages, (4) en-US and Eastern Time formatting everywhere, (5) accessibility pass and Section 508 evidence. This
section records part 1; the later parts add their own rows.

| Field | Value |
|---|---|
| Received | _date and time (ET), from whom: to be completed by the tech lead_ |
| Change as received | _verbatim text: to be pasted by the tech lead_ |
| Clarifying questions and answers | Permission names: the existing names are kept and mapped (C1-1). Delivery in five parts on separate branches (proposed to the tech lead on 2026-10-21; work continued on that basis). Assumptions C1-1 to C1-6 in the Assumptions Log. |
| Principal components affected | Contracts (`PermissionCatalog`, `EffectivePermissions`, user and role administration contracts, `RolePermissionMap` defaults + Administrator), Persistence (`AppPermission`, `AppRolePermission`, `AppUserPermissionOverride`, new `AppUser`/`AppRole` columns, `PermissionQueries`, migration `C1_DatabasePermissions`), Identity (`PermissionResolver`, async permission handler, both `ICurrentUser` adapters, `AdministrationRules`, `UserAdministrationService`, `RoleAdministrationService`, `DatabaseUnit`, seed, sign-in and revalidation of inactive users), Cases (`CaseAssigneeDirectory` from stored permissions), Web (Users, user detail, Roles, role editor, navigation, `FocusOnNavigation` waiting for a late h1); [ADR 0013](adr/0013-database-roles-and-permissions.md), ADR 0007 status |
| Commits | _to be added when committed (one branch `feat/roles-permissions-admin`, merged with a merge commit)_ |
| Tests used to validate | New: `PermissionCatalogTests`, `EffectivePermissionsTests`, `PermissionResolverTests`, golden matrix with the Administrator column and `roles.manage` row (Identity.Tests); `UserDetailPageTests`, `RolesPageTests`, rewritten `UsersPageTests`, `Administrators_see_users_and_roles` (Web.Tests); `RolesAndPermissionsTests` (12 integration tests: seed and re-seed, custom role permissions over HTTP, overrides, deactivation and sign-in refusal, concurrency, escalation rules, role rules, rename, paging and filters, unlock, case owners); browser: `RolesAndPermissionsTests` (keyboard-only administration, phone width, supervisor read-only) and the updated `UserAdministrationTests` |
| Full regression run | Host run on 2026-10-21 (Windows Server 2025, .NET SDK 10.0.402, PostgreSQL 18.6 through `TESTKIT_POSTGRES_ADMIN`; Docker not available to this account): unit and component 638 passed; integration 398 of 400 (the 2 failures are also on `main`, see [verification.md](verification.md)); browser suite against the published app 21 of 22 plus the 4 new administration tests (the failing tablet overflow test fails identically on `main` on this host). `docker compose run --rm tests`: _not run; to be run on a host with Docker_ |
| Contributors | Janarthanan Senthurpandi, with Claude Code as coding assistant (see [contributor-declaration.md](contributor-declaration.md)) |
| Documents updated | README (demo accounts, administration), ADR 0013 (new), ADR 0007 status and index, Assumptions Log (A5, E3-4 amended; C1-1 to C1-6), limitations, accessibility review, verification |
| Known limitations | In-app change history for users and roles arrives with part 2 (until then, log events 3014-3022). Role membership changes apply when the member signs in again (forced within one minute). The permission cache is per process. Manual screen-reader review of the new pages is not yet performed. |

## Change 1, part 2 of 5: System-wide audit log and viewer

| Field | Value |
|---|---|
| Received | As part 1 |
| Change as received | As part 1 |
| Clarifying questions and answers | Scope of "system-wide" and what is recorded: assumptions C2-1 to C2-4. Part 1 and part 2 are kept in one uncommitted working tree at the tech lead's request (2026-10-21), to be split into commits afterwards if needed. |
| Principal components affected | Contracts (`Auditing` contracts, `OperationContext`, `CsvText`, `audit.view`), Persistence (`AuditLogEntry`, `SystemAudit`, `AuditLogWriter`, `AuditOperation`, append-only guard for the new table, `CorrelationId` on the case and test-result trails, `DefaultPermissionsApplied` replacing the part 1 seed flag, migrations `C1_DatabasePermissions` (regenerated) and `C2_AuditLog`), ServiceDefaults (operation ID made ambient), Identity (`AuditLogService`, auditing in sign-in, sign-out, password change, access denials, user and role administration; seed grants new defaults to seeded roles once), Cases and Attachments (operation per change), TestResults (correlation ID), Web (Audit log, audit entry, History on user and role pages, `/exports/audit.csv`, navigation); [ADR 0014](adr/0014-system-audit-log.md), ADR 0013 and index |
| Commits | _to be added when committed_ |
| Tests used to validate | New: `AuditLogTests` (10 integration tests: user and role administration with before/after and operation IDs, refused changes write nothing, sign-in outcomes and the request operation ID, access denials, case trail in the viewer, merge order and Eastern date filter and paging limits, authorization and append-only, CSV export, seed grants a later default once); `AuditLogPageTests` (8 bUnit tests); browser `AuditLogTests` (keyboard-only read, filter, entry, history link, CSV download) and the audit log in the phone-width test; golden matrix with the `audit.view` row |
| Full regression run | Host run on 2026-10-21 (see [verification.md](verification.md)); `docker compose run --rm tests`: _not run (Docker not available to this account)_ |
| Contributors | Janarthanan Senthurpandi, with Claude Code as coding assistant |
| Documents updated | ADR 0014 (new), ADR 0013 (seed bookkeeping), README, Assumptions Log (C1-6 resolved; C2-1 to C2-4), accessibility review, verification |
| Known limitations | Case and test-result entries keep their existing detail (status changes and safe details, no full field values). Audit rows written outside a business transaction (sign-in, denials) can be lost if the database fails at that moment; the failure is logged. Pages beyond the first 10,000 matching entries are refused. |

## Change 1, parts 3 to 5 of 5: error log and System Health, en-US and Eastern Time, accessibility and Section 508

| Field | Value |
|---|---|
| Received | As part 1 |
| Change as received | As part 1 |
| Clarifying questions and answers | Assumptions C3-1 to C3-3 (error capture, reference, permissions), C4-1 (en-US culture, date inputs, exports), C5-1 (how Section 508 conformance is reported); E1-4 amended. All parts kept in one uncommitted working tree at the tech lead's request. |
| Principal components affected | **Part 3:** Contracts (`Diagnostics` contracts, `ErrorCaptureSuppression`, three permissions), ServiceDefaults (`ErrorCaptureLoggerProvider`), Persistence (`ErrorLogEntry`, migration `C3_ErrorLog`), Identity (`ErrorLogWriter`, `ErrorLogService`, `SystemHealthService`), Web (Errors, error detail, System health, error page reference and the operation middleware moved outermost, circuit error banner); [ADR 0015](adr/0015-error-log-and-system-health.md). **Part 4:** Domain (`EasternTime.Abbreviation`), Web (`UiText` en-US time and date helpers, every page's timestamps and labels, culture pinned to en-US, `lang="en-US"`). **Part 5:** CSS (scroll padding for WCAG 2.4.11), browser accessibility sweep, [docs/section-508](section-508/README.md) |
| Commits | _to be added when committed_ |
| Tests used to validate | New: `ErrorLogTests` (6 integration: request error stored under the error page's reference without its message, capture rules and suppression, fingerprint and audited status changes with conflicts, permissions, retention, System Health); `ErrorAndHealthPageTests` (6 bUnit); `DateTimeFormatTests` (10: EDT/EST including both DST changes, culture independence, a source guard against hand-written time formats); browser `AccessibilitySweepTests` (5: axe and structure on every page and the public pages, skip link and visible, unobscured focus on every page, reflow at 320 px on every page, Eastern timestamps); format expectations updated in 6 existing bUnit test classes; golden matrix rows for the three new permissions |
| Full regression run | Host run on 2026-10-21, see [verification.md](verification.md); `docker compose run --rm tests`: _not run (Docker not available to this account)_ |
| Contributors | Janarthanan Senthurpandi, with Claude Code as coding assistant |
| Documents updated | ADR 0015 (new), ADR index, README, Assumptions Log (E1-4 amended; C3-1 to C5-1), accessibility review, Section 508 report, manual test procedures and index (new), verification |
| Known limitations | Manual accessibility procedures M1 to M9 not performed, so the Section 508 report claims no full conformance. Errors dropped when the capture queue is full or a batch cannot be saved stay on stdout only. Native date inputs show the browser's own date format. Session timeout without warning (WCAG 2.2.1, reported as partial). |
