# Verification record (Epic 1 foundation)

Base revision: `d8fe6a5` plus **uncommitted** Epic 1 changes (nothing committed). Host: macOS arm64, Docker 29.8.2 / Compose v5.6.0, containers `linux/amd64` under emulation.

## Commands and results

| Command | Result |
|---|---|
| `dotnet build CaseIntake.sln -c Release` | 0 warnings, 0 errors |
| `dotnet test CaseIntake.sln -c Release` (host) | 56 passed, 0 failed |
| `docker compose build web` | image `caseintake-eval:local` built |
| `docker compose run --rm --no-deps tests` | 56 passed, 0 failed, `RESULT: PASS` (`--no-deps`: veteran-lookup is E4) |
| `scripts/run-tests.sh` with a deliberately failing test | exit 1, `RESULT: FAIL` (test then removed) |
| `docker compose run --rm migrator` x2 on empty DB | exit 0 both; second run "No migrations were applied" |
| `docker stop` on `migrator` while waiting for a stopped `db` | stops in <1 s, logs EventId 1007 `MigrationServiceCancelled` (JSON), exit 1 |
| `docker compose up -d --no-deps web` | `/health/live` 200, `/` 200, `/health/ready` 503 (no veteran-lookup service) |

Runtime inventory from the image: Microsoft.NETCore.App 10.0.13, Microsoft.AspNetCore.App 10.0.13, SDK 10.0.402; runs as uid 10001.

## Not verified / blocked

- `docker compose up --build` and `docker compose run --rm tests` without `--no-deps`: need E4's veteran-lookup Dockerfile.
- Zero-test detection and missing-project detection in `run-tests.sh` are implemented but were not exercised.
- Offline (no-egress) run, repeated-start persistence, cookie validity after recreation: G3, need E3/E5.
- Seeding content, case/audit/attachment migrations, Playwright image layer: E2/E3/E4/E5.
- Traceability table and ADRs owned by other epics: later gates.

# Verification record (Epic 2 case domain)

Base revision: `8485c71` plus Epic 2 changes on branch `epic-2-case-domain` (uncommitted). Same host as above.
Docker checks used an isolated Compose project (`-p caseintake-e2v`, removed afterwards with `down -v`) so no demo
volumes were touched. The Docker rows were recorded before the working tree was accidentally deleted and the
files were recreated; host build and test results were re-run after recreation.

| Command | Result |
|---|---|
| `dotnet build CaseIntake.sln` | 0 warnings, 0 errors |
| `dotnet test CaseIntake.sln` (host) | 261 passed (Cases.Tests 104, IntegrationTests 157), 0 failed |
| `docker compose -p caseintake-e2v build web` | image built (exit 0) |
| `docker compose -p caseintake-e2v run --rm migrator` x2 on empty DB | exit 0 both; run 1 applied 2 migrations and seeded 72 of 72 cases; run 2 applied none and added 0 |
| `psql` row counts after seeding | New 12, Triaged 12, InProgress 24, Resolved 12, Closed 12; 252 audit entries |
| `docker compose -p caseintake-e2v run --rm --no-deps tests` | Cases.Tests 104/0, IntegrationTests 157/0, `RESULT: PASS` (`--no-deps`: veteran-lookup is E4) |

## Not verified / blocked (E2)

- End-to-end use through the UI and with real Identity users: needs E3 (`ICurrentUser`, demo users) and E5 (pages).
- Running the web project in `Development` fails DI validation until E3 registers `ICurrentUser` (see assumptions doc).
- Optional 10k-row list performance check was not run.
