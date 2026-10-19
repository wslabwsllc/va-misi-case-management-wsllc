# Installation guide

How to install and run Case Intake and Triage: a .NET 10 Blazor Web App with PostgreSQL, a synthetic veteran lookup
service and an in-app test results module.

There are two ways to run it:

| Option | Use it for | What you install |
|---|---|---|
| [A. Docker Compose](#a-install-with-docker-compose-recommended) (recommended) | evaluation, demos, running the full test suite | Docker only |
| [B. Local development](#b-local-development-install) | changing and debugging the code | .NET SDK 10.0.402 and Docker (for PostgreSQL) |

> **Local evaluation only.** The app serves plain HTTP on port 8080, uses seeded demo accounts with a published
> password, and holds synthetic data only. It is not configured for production; see [Before any shared or production
> deployment](#before-any-shared-or-production-deployment).

---

## Prerequisites

| Requirement | Version / notes |
|---|---|
| Docker Engine or Docker Desktop | with **Compose v2** (`docker compose version` must work; the old `docker-compose` v1 is not supported) |
| CPU architecture | images are `linux/amd64`; the .NET compile runs natively (`--platform=$BUILDPLATFORM`), so ARM hosts such as Apple Silicon emulate only the running containers |
| Free host port | **8080** (the web app). PostgreSQL and the lookup service are not published to the host |
| Disk space | several GB for the images and the volumes; the test image (`--profile tests`, .NET SDK + Playwright Chromium) is the large one and is built only for the tests |
| Network | needed at **build** time only (base images, NuGet packages, Chromium). Nothing is downloaded when containers run |
| Git | to clone the repository |
| bash | for the helper scripts in `scripts/`. On Windows use **Git Bash** |
| .NET SDK | **10.0.402** exactly, and only for option B (`global.json` pins it with `rollForward: disable`) |

All pinned versions (SDK, base images, NuGet packages) are listed in [versions.md](versions.md).

### Windows notes

- Use Docker Desktop with the WSL 2 backend and Linux containers.
- `.gitattributes` keeps shell scripts in LF line endings even when `core.autocrlf=true`. If you copied the files in
  some other way and see `bash\r: No such file or directory`, see [docker-troubleshooting.md](docker-troubleshooting.md).
- The scripts handle Git Bash path conversion themselves. If you run your own `docker compose exec` commands with container
  paths such as `/src/...` from Git Bash, prefix them with `MSYS_NO_PATHCONV=1`.

---

## A. Install with Docker Compose (recommended)

### 1. Get the code

```bash
git clone https://github.com/wslabwsllc/va-misi-case-management-wsllc.git
cd va-misi-case-management-wsllc
```

No `.env` file and no secrets are needed: every setting, including the synthetic local database credentials, is in
`docker-compose.yml`.

### 2. Build and start

```bash
docker compose up --build          # foreground; Ctrl+C stops it
# or
docker compose up --build -d       # in the background
```

The first build takes several minutes (it restores, builds and publishes the solution and installs Chromium for the
browser tests). Later builds reuse the Docker layer cache.

The services start in this order:

| Service | What it does |
|---|---|
| `db` | PostgreSQL 18, database `caseintake`, data on the `pgdata18` volume |
| `migrator` | one-shot job: applies EF Core migrations, seeds 72 synthetic cases and the demo accounts, cleans up attachment storage, then exits with code 0 |
| `veteran-lookup` | synthetic veteran lookup mock, reachable only inside the Compose network (`http://veteran-lookup:8081`) |
| `web` | the Blazor app on **<http://localhost:8080>** |

`web` starts only after `migrator` has finished successfully and `veteran-lookup` is healthy. It counts as healthy only
when both `/health/ready` and `/_framework/blazor.web.js` return 200.

### 3. Verify the installation

```bash
docker compose ps                                   # web: running (healthy); migrator: exited (0)
curl -fsS http://localhost:8080/health/live         # Healthy
curl -fsS http://localhost:8080/health/ready        # Healthy (database and veteran lookup reachable)
scripts/smoke-docker.sh                             # expects "SMOKE: PASS" (framework JS and static assets)
```

To check a completely fresh install, `scripts/verify-clean-start.sh` brings the stack up from empty volumes and probes
the health endpoints. **It runs `docker compose down -v` first, which deletes all existing data.**

### 4. Sign in

Open <http://localhost:8080>. You are redirected to the sign-in page (`/account/login`). The demo accounts are created by
the migrator:

| Email | Password | Role |
|---|---|---|
| `caseworker@example.test` | `Demo-Passw0rd!` | CaseWorker: view, create and edit cases, veteran lookup, attachments, start and resolve cases |
| `supervisor@example.test` | `Demo-Passw0rd!` | Supervisor: everything a case worker can do, plus triage, close and reopen |
| `qa@example.test` | `Demo-Passw0rd!` | QualityAnalyst: read cases, view and import test results (Administration > Quality assurance) |

Five wrong passwords lock an account for five minutes. There is no self-registration or password reset.

Test SSNs for the veteran lookup (all synthetic): `900-12-3456` (found), `999-99-9999` (not found), `12345` (rejected as
invalid format). More are listed in the [README](../README.md). For a guided tour of the features, see [demo.md](demo.md).

### 5. Stop, restart and update

```bash
docker compose stop                 # stop the containers, keep them and all data
docker compose start                # start them again
docker compose down                 # remove the containers, keep the data volumes
git pull && docker compose up --build -d    # update: rebuild the images; the migrator applies new migrations
```

Data is kept in named volumes, so it survives `down` and rebuilds:

| Volume | Contents |
|---|---|
| `pgdata18` | PostgreSQL databases (`caseintake`, and `caseintake_e2e` for browser tests) |
| `attachments` | uploaded case attachments (metadata is in the database) |
| `dataprotection` | Data Protection keys, so sign-in cookies survive container restarts |
| `testresults` | test runs (TRX files, evidence) shown in the Test results module |
| `attachments-e2e`, `testfixtures` | data for the isolated browser-test copy of the app |

### 6. Uninstall

```bash
docker compose --profile tests down -v --rmi local    # removes containers, ALL volumes (data) and the locally built images
```

`-v` permanently deletes the database, attachments and test results. Leave it out to keep the data.

---

## Run the automated tests (optional)

The test suites run inside Docker against `web-e2e`, an isolated copy of the production image with its own database. They never change the demo data.

```bash
docker compose run --rm tests       # all suites; non-zero exit code if anything fails
scripts/run-all-tests.sh            # the same, plus git/build metadata and the server log in the results
scripts/run-all-tests.sh unit       # or: integration | ui
```

Results are written to the `testresults` volume and imported into the app automatically, usually within a minute. Sign
in as `qa@example.test` and open **Administration > Quality assurance > Test results** (`/test-results`). Details:
[testing.md](testing.md).

---

## B. Local development install

Use this to run the app from source with the .NET SDK, for example to debug it in an IDE. Docker is still used for
PostgreSQL.

### 1. Install the tools

1. Install the **.NET SDK 10.0.402** (<https://dotnet.microsoft.com/download/dotnet/10.0>). Check with `dotnet --version`
   in the repository folder; `global.json` rejects any other SDK version.
2. Restore the local tools (`dotnet-ef`) and build:

   ```bash
   dotnet tool restore
   dotnet build
   ```

### 2. Start PostgreSQL

Either reuse the Compose database by publishing its port:

```bash
docker run -d --name caseintake-pg -p 5432:5432 \
  -e POSTGRES_USER=caseintake -e POSTGRES_PASSWORD=caseintake_local_only -e POSTGRES_DB=caseintake \
  postgres:18
```

or point the app at your own PostgreSQL 18 server by changing `ConnectionStrings:CaseIntake` (see
[Configuration](#configuration)). The default in `appsettings.json` is
`Host=localhost;Port=5432;Database=caseintake;Username=caseintake;Password=caseintake_local_only;Timezone=UTC`.

### 3. Create the schema and seed data

```bash
dotnet run --project src/CaseIntake.MigrationService
```

Run it again after pulling new migrations. It is safe to rerun: seeding never duplicates data or resets a changed
password.

### 4. Start the veteran lookup mock

In a separate terminal:

```bash
dotnet run --project src/CaseIntake.VeteranLookup.Mock --urls http://localhost:8081
```

### 5. Start the web app

```bash
dotnet run --project src/CaseIntake.Web          # http://localhost:8080, Development environment
```

Sign in with the demo accounts from [step 4 of option A](#4-sign-in).

Outside Docker, attachments are stored in `<system temp>/caseintake-attachments` and test results are read from
`<system temp>/caseintake-test-results`, unless you set `Attachments__RootPath` or `TestResults__RootPath`. The web app
and the migrator use the same default folders, so they always agree.

### 6. Run the tests locally

```bash
dotnet test
```

The integration tests start a disposable PostgreSQL container through Testcontainers, so Docker must be running. To use an
existing server instead, set `TESTKIT_POSTGRES_ADMIN` to an admin connection string. The browser suite (`CaseIntake.E2E`)
needs the Docker-hosted `web-e2e`; see [testing.md](testing.md) for running it locally.

---

## Configuration

Settings come from `appsettings.json` and can be overridden with environment variables. Use `__` as the section separator
in environment variable names, for example `Attachments__MaxBytes=5242880`. In Docker, set them under the service's `environment:` in
`docker-compose.yml`. All settings have working defaults.

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionStrings:CaseIntake` | local `caseintake` database | PostgreSQL connection (web and migrator) |
| `VeteranLookup:BaseUrl` | `http://localhost:8081` (Compose: `http://veteran-lookup:8081`) | lookup service address |
| `VeteranLookup:TotalTimeout` / `AttemptTimeout` / `RetryDelay` | `00:00:05` / `00:00:02` / `00:00:00.200` | lookup timeout budget (one retry at most) |
| `Seed:DemoPassword` | `Demo-Passw0rd!` | password given to demo accounts **when they are first created** |
| `Attachments:RootPath` | `<temp>/caseintake-attachments` (Compose: `/attachments`) | attachment storage folder; must be the same for web and migrator |
| `Attachments:MaxBytes` | `10485760` (10 MiB) | largest upload |
| `Attachments:AllowedTypes:N:ContentType` / `:Extensions:M` | PDF, PNG, JPEG, TXT, DOCX | allowed attachment types |
| `DataProtection:KeysPath` | unset (keys kept in memory) (Compose: `/dataprotection`) | persist cookie encryption keys |
| `TestResults:RootPath` | `<temp>/caseintake-test-results` (Compose: `/test-results`) | where test runs are read from |
| `TestResults:ScanInterval` | `00:01:00` (`00:00:00` disables) | automatic import interval |
| `TestResults:ArtifactRetention` / `KeepLatestRuns` | 90 days / 30 runs | evidence retention (results are kept) |
| `TestResults:MaxArtifactBytes` | 200 MiB | largest evidence file imported |
| `Identity:RevalidationInterval` | `00:01:00` | how often open sessions re-check the user's roles |
| `Dashboard:OverdueAfterDays` / `TrendMonths` / `RecentActivityDays` | 14 / 12 / 14 | dashboard calculations |

Health endpoints: `/health/live` (process up) and `/health/ready` (database and veteran lookup reachable).

---

## Troubleshooting

| Symptom | What to do |
|---|---|
| `port is already allocated` for 8080 | stop whatever uses port 8080 (`docker compose ls` shows other Compose stacks), or change `"8080:8080"` in `docker-compose.yml` to, for example, `"8090:8080"` |
| `web` never becomes healthy | `docker compose logs migrator web`. A failed migrator (exit code not 0) blocks `web`. Check that `db` is healthy |
| Pages render but buttons do nothing, or styles are missing | run `scripts/smoke-docker.sh`; the content root must be `/app/web` (see [docker-troubleshooting.md](docker-troubleshooting.md)) |
| `bash\r: No such file or directory` | scripts have CRLF line endings; delete `scripts/*.sh` and run `git checkout -- scripts` |
| Reconnect dialog stays open | the WebSocket to `/_blazor` is blocked (proxy) or the server restarted; press Retry or Reload |
| Sign-in fails for a demo account | the account may be locked (wait five minutes), or its password was changed. `Seed:DemoPassword` does not reset existing accounts |
| Local run: `A compatible .NET SDK was not found` | install exactly SDK 10.0.402 |
| Test results page is empty | wait a minute for the background import, or press **Import new runs** |

More causes and fixes: [docker-troubleshooting.md](docker-troubleshooting.md).

---

## Before any shared or production deployment

This repository is set up for local evaluation. Before you host it anywhere else, you need at least the following, which
are out of scope here (see [assumptions-and-limitations.md](assumptions-and-limitations.md)):

- **HTTPS** with a trusted certificate, usually from a reverse proxy that allows WebSocket upgrades to `/_blazor`.
  Without HTTPS the sign-in cookie is not `Secure`, there is no HSTS, and the PWA and service worker are unavailable.
- **A real identity provider.** Remove the seeded demo accounts and the published `Seed:DemoPassword`.
- **Secrets** (connection strings) supplied from a secret store, not from `docker-compose.yml`.
- A **real veteran lookup service** in place of the synthetic mock.
- **Backups** for the database and the `attachments` volume, plus monitoring and alerting. Logs currently go to stdout
  only.
- **Malware scanning** for uploaded attachments.
