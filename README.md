# Case Intake and Triage

Blazor Web App (.NET 10, Interactive Server) with PostgreSQL, built per [docs/Epic_Plan.md](docs/Epic_Plan.md).

> **Status:** Epic 1 (platform, persistence, delivery pipeline) foundation is in place. Case, identity, lookup,
> attachment and UI features arrive with Epics 2 to 5, so `http://localhost:8080` currently shows a host page only and
> there are no demo accounts yet. See [docs/verification.md](docs/verification.md) for what has been verified.

## Run

Requires Docker with Compose v2. Services run as `linux/amd64` (Apple Silicon runs them under emulation, which is slower).

```bash
docker compose up --build        # http://localhost:8080
docker compose run --rm tests    # complete automated suite; non-zero exit on any failure
```

`docker compose up --build` builds the images, starts `db`, runs the one-shot `migrator` (migrate + seed), then starts
`veteran-lookup` and `web`. Health: `/health/live`, `/health/ready` (database and veteran lookup). Credentials in
`docker-compose.yml` are synthetic and local-only; no `.env` file is needed.
`scripts/verify-clean-start.sh` brings the stack up from empty volumes and probes the health endpoints.

**Demo accounts:** added by E3 (seeded `CaseWorker` and `Supervisor` users); this section is updated then.

## Local development

Requires .NET SDK 10.0.402 (see `global.json`) and Docker (tests start a disposable PostgreSQL through Testcontainers, or use
`TESTKIT_POSTGRES_ADMIN` to point at an existing server).

```bash
dotnet tool restore
dotnet build
dotnet test
dotnet ef migrations add <Epic>_<Change> -p src/CaseIntake.Persistence -s src/CaseIntake.Persistence -o Migrations
```

Browser (E2E) prerequisites will be documented here with the first Playwright test.

## Layout

| Path | Purpose |
|---|---|
| `src/CaseIntake.Contracts` | Shared service contracts and DTOs (additive changes only) |
| `src/CaseIntake.Domain` | Domain types (`CaseStatus`; case/attachment entities and workflow from E2/E4) |
| `src/CaseIntake.Persistence` | `AppDbContext`, Identity types, migrations, seeding runner, UTC conventions |
| `src/CaseIntake.ServiceDefaults` | JSON logging with SSN scrubbing, operation scopes, health checks |
| `src/CaseIntake.MigrationService` | One-shot migrate and seed worker |
| `src/CaseIntake.Web` | Blazor host and composition root |
| `tests/CaseIntake.TestKit` | Fixtures, `CaseIntakeWebFactory`, fakes of every contract |
| `tests/CaseIntake.IntegrationTests` | Architecture, migration, UTC, seeding, logging and health tests |

## Documents

[Epic plan](docs/Epic_Plan.md) · [Implementation prompt](docs/implementation-prompt.md) · [ADRs](docs/adr/README.md) ·
[Versions](docs/versions.md) · [Assumptions and limitations](docs/assumptions-and-limitations.md) ·
[Verification](docs/verification.md). Traceability, the demo walkthrough and the requirement-change note are produced at G3/G4.
