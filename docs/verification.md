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
