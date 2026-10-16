# Assumptions and limitations

Status: **E1 foundation and E2 case domain.** The assumptions below are the adopted decisions from the implementation plan
(sections 0.1 and 8). Each feature epic appends the limitations it discovers. Items marked *(not yet implemented)* are
decisions that take effect when the owning epic lands.

## Assumptions

| # | Assumption |
|---|---|
| A1 | .NET 10 LTS, exact versions in [versions.md](versions.md) |
| A2 | Interactive Server render mode; Identity account pages static SSR *(E5/E3)* |
| A3 | PostgreSQL 17 via EF Core 10 / Npgsql ([ADR 0002](adr/0002-data-access-and-database.md)) |
| A4 | Local ASP.NET Core Identity with seeded synthetic users; no external IdP *(E3)* |
| A5 | Roles `CaseWorker` and `Supervisor` *(E3)* |
| A6 | "Audit information" = audit fields plus append-only trail ([ADR 0006](adr/0006-audit-trail.md)) |
| A7 | Initial status `New`; unlisted transitions rejected ([ADR 0005](adr/0005-case-workflow.md)) |
| A8 | Optional veteran lookup at intake; snapshot (name, address, SSN last 4) saved on the case *(storage: E2; lookup/UI: E4/E5)* |
| A9 | Attachments on a Docker volume, 10 MiB default limit, configurable *(E4)* |
| A10 | WCAG 2.2 AA as engineering yardstick *(E5)* |
| A11 | Primary workflow: sign in, list, intake, detail, edit, transition, attachments |
| A12 | Modular monolith; only the veteran-lookup mock is a separate service |

## Specification ambiguities and adopted decisions

See plan section 8 (OQ-1 to OQ-20). E1-relevant ones: OQ-13 structured logs only, traces/metrics optional (excluded);
OQ-14 PostgreSQL; OQ-15 ADRs plus this file plus README; OQ-16 all suites run in `tests`; OQ-19 UTC stored and
displayed with a "UTC" label; OQ-20 `platform: linux/amd64` is pinned, so Apple Silicon hosts run it under emulation
(slower startup and test runs).

E2 (case domain) decisions:

- **OQ-4 list:** filter by status (multi-select) and text search (case-insensitive substring of Title or Description,
  `%`, `_` and `\` match literally, at most 200 characters); sort by Id, Title, Status (New → Closed order), Created
  or Updated, either direction, always with `Id` as the tiebreaker; default page size 25 sorted by Updated
  descending. The service accepts page sizes 1–100 (the UI offers 10/25/50); a page past the end is an empty page.
- **OQ-9 edit:** Title and Description are editable by both roles in every status except Closed (`Validation` error
  with code `CaseClosed`). ID and status are never part of an edit. A no-op edit changes nothing (no timestamp,
  version or audit change). Stale versions return `Conflict` for both edits and status changes.
- **OQ-12 text:** Title is trimmed, then 5–200 Unicode scalar values; Description is not trimmed, up to 4,000 scalar
  values, and an empty description is stored as null. Text with NUL or unpaired surrogates is rejected. Veteran name
  ≤ 200, address ≤ 500, SSN last four = four ASCII digits.
- **Audit actor:** `CreatedBy`/`UpdatedBy` and the audit `Actor` hold the user (display) name; audit entries also
  keep the Identity user ID.
- **OQ-17:** no delete operation for cases; the audit foreign key also refuses deleting a case that has history.
- **Seed (E2-S9):** 72 fixed cases (12 New, 12 Triaged, 24 In Progress including 12 reopened, 12 Resolved, 12 Closed)
  generated from a fixed `System.Random` seed and fixed UTC base time rather than Bogus, to avoid a new runtime
  dependency; fixtures are keyed by `SeedKey` so re-seeding never duplicates or overwrites user edits. Audit entries
  reference the Identity user IDs of `caseworker@example.test` / `supervisor@example.test` when the E3 seed has
  created them, otherwise stable `seed:<user name>` IDs.

## Limitations (out of scope per specification 4.7 and plan 0.7)

- No production identity provider, password reset or self-registration.
- No malware scanning of attachments.
- No production monitoring, alerting, tracing backend or metrics dashboard; logs go to stdout only.
- No Government systems or data; all data is synthetic.
- Backup, disaster recovery and high availability are not addressed.
- Audit-trail immutability protects application operations only, not a database administrator.
- The health readiness probe is a liveness-style TCP/HTTP check, not a load test.

## Known foundation gaps (tracked, not hidden)

- Case and audit tables are in the `E2_Cases` migration; attachment tables are added by E4.
- The web app registers the real case module, but `ICurrentUser` comes from E3. Until E3 lands, running the web
  project in the `Development` environment fails DI validation (`ICurrentUser` unresolved); Docker (`Production`) and
  the tests are unaffected because they do not validate on build or supply their own `ICurrentUser`.
- The Playwright browser layer is added to the evaluation image with the first E2E test (E5-S9).
- The `veteran-lookup` Compose service builds from E4's Dockerfile, which does not exist yet.
