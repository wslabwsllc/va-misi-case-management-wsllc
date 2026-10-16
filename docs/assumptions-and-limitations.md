# Assumptions and limitations

Status: **E1 foundation portion.** The assumptions below are the adopted decisions from the implementation plan
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
| A6 | "Audit information" = audit fields plus append-only trail *(E2)* |
| A7 | Initial status `New`; unlisted transitions rejected *(E2)* |
| A8 | Optional veteran lookup at intake; snapshot saved; only SSN last 4 stored *(E2/E4/E5)* |
| A9 | Attachments on a Docker volume, 10 MiB default limit, configurable *(E4)* |
| A10 | WCAG 2.2 AA as engineering yardstick *(E5)* |
| A11 | Primary workflow: sign in, list, intake, detail, edit, transition, attachments |
| A12 | Modular monolith; only the veteran-lookup mock is a separate service |

## Specification ambiguities and adopted decisions

See plan section 8 (OQ-1 to OQ-20). E1-relevant ones: OQ-13 structured logs only, traces/metrics optional (excluded);
OQ-14 PostgreSQL; OQ-15 ADRs plus this file plus README; OQ-16 all suites run in `tests`; OQ-19 UTC stored and
displayed with a "UTC" label; OQ-20 `platform: linux/amd64` is pinned, so Apple Silicon hosts run it under emulation
(slower startup and test runs).

## Limitations (out of scope per specification 4.7 and plan 0.7)

- No production identity provider, password reset or self-registration.
- No malware scanning of attachments.
- No production monitoring, alerting, tracing backend or metrics dashboard; logs go to stdout only.
- No Government systems or data; all data is synthetic.
- Backup, disaster recovery and high availability are not addressed.
- Audit-trail immutability protects application operations only, not a database administrator.
- The health readiness probe is a liveness-style TCP/HTTP check, not a load test.

## Known foundation gaps (tracked, not hidden)

- Entity configurations and migrations for cases, audit and attachments are added by E2/E4; the baseline migration
  currently contains the Identity tables only.
- The Playwright browser layer is added to the evaluation image with the first E2E test (E5-S9).
- The `veteran-lookup` Compose service builds from E4's Dockerfile, which does not exist yet.
