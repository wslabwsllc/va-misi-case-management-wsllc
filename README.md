# Case Intake and Triage

Blazor Web App (.NET 10, Interactive Server) with PostgreSQL, a synthetic veteran lookup service and an in-app test
results module, built per [docs/Epic_Plan.md](docs/Epic_Plan.md). It works the same on Windows, macOS and Linux.

<details>
<summary><b>Install prerequisites</b></summary>

| Tool | Needed for | Check |
|---|---|---|
| Docker Desktop or Docker Engine with **Compose v2** | everything (the old `docker-compose` v1 is not supported) | `docker compose version` |
| Git | cloning | `git --version` |
| bash | the helper scripts in `scripts/` | `bash --version` |
| .NET SDK **10.0.402** exactly | building, running and testing from source (`global.json` sets `rollForward: disable`) | `dotnet --version` in the repo folder |

Also needed: free host port **8080**, several GB of disk, and network access at **build** time only (nothing is
downloaded when containers run). Images are `linux/amd64`; the .NET build runs natively, so on Apple Silicon only the
running containers are emulated. All pinned versions: [docs/versions.md](docs/versions.md).

<details>
<summary>macOS</summary>

0. [Homebrew](https://brew.sh/), if `brew --version` fails:

   ```bash
   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
   echo 'eval "$(/opt/homebrew/bin/brew shellenv)"' >> ~/.zprofile   # Apple Silicon; Intel: /usr/local/bin/brew
   eval "$(/opt/homebrew/bin/brew shellenv)"
   brew --version
   ```

1. Install [Docker Desktop for Mac](https://docs.docker.com/desktop/setup/install/mac-install/) (Apple Silicon or Intel)
   and start it. Compose v2 is included.
2. Git: `xcode-select --install` (or `brew install git`). bash is preinstalled.
3. .NET SDK 10.0.402 with [mise](https://mise.jdx.dev/):

   ```bash
   brew install mise                                  # or: curl https://mise.run | sh
   echo 'eval "$(mise activate zsh)"' >> ~/.zshrc     # bash: mise activate bash >> ~/.bashrc
   exec $SHELL                                        # reload the shell
   mise use -g dotnet@10.0.402
   dotnet --version                                   # 10.0.402
   ```

</details>

<details>
<summary>Windows</summary>

1. Install [Docker Desktop for Windows](https://docs.docker.com/desktop/setup/install/windows-install/) with the
   **WSL 2 backend** and **Linux containers**, and start it.
2. Install [Git for Windows](https://git-scm.com/download/win); it includes **Git Bash**. Run the `scripts/` from Git Bash.
3. .NET SDK 10.0.402: download the **SDK 10.0.402** x64 installer from the
   [.NET 10.0 downloads page](https://dotnet.microsoft.com/download/dotnet/10.0), or in PowerShell:

   ```powershell
   Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
   ./dotnet-install.ps1 -Version 10.0.402             # installs to %LOCALAPPDATA%\Microsoft\dotnet; add it to PATH
   dotnet --version                                   # 10.0.402
   ```

   `winget install Microsoft.DotNet.SDK.10` installs the latest 10.0 patch, which `global.json` rejects if it isn't 10.0.402.

Notes:
- `.gitattributes` keeps shell scripts in LF. If you see `bash\r: No such file or directory`, the scripts were copied
  with CRLF; delete `scripts/*.sh` and run `git checkout -- scripts`.
- The scripts handle Git Bash path conversion. For your own `docker compose exec` commands with container paths such as
  `/src/...`, prefix them with `MSYS_NO_PATHCONV=1`.

</details>

<details>
<summary>Linux</summary>

1. Install [Docker Engine](https://docs.docker.com/engine/install/) and the
   [Compose plugin](https://docs.docker.com/compose/install/linux/) (`docker-compose-plugin`). Optionally
   [run Docker without sudo](https://docs.docker.com/engine/install/linux-postinstall/).
2. Git and bash from your package manager (for example `sudo apt install git`).
3. .NET SDK 10.0.402 with the official install script (or with mise, as on macOS):

   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 10.0.402
   echo 'export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH' >> ~/.bashrc && exec $SHELL
   dotnet --version                                   # 10.0.402
   ```

   Distribution packages (`apt install dotnet-sdk-10.0`) usually ship a different patch version.

</details>

</details>

---

<details>
<summary><b>Quick start</b></summary>

Needs Docker with Compose v2, Git and free port 8080 (see **Install prerequisites** above).

```bash
git clone https://github.com/wslabwsllc/va-misi-case-management-wsllc.git
cd va-misi-case-management-wsllc
docker compose up --build --wait       # runs in the background, returns once web is healthy
```

Open <http://localhost:8080> and sign in. The first build takes several minutes. No `.env` file or secrets are needed:
every setting, including the synthetic local database credentials, is in `docker-compose.yml`.

Startup order: `db` (PostgreSQL 18) → `migrator` (migrates, seeds, exits 0) → `veteran-lookup` → `web`. `web` waits for
the migrator to succeed and the lookup to be healthy.

</details>

---

<details>
<summary><b>Verify the running app</b></summary>

```bash
docker compose ps                                # web: running (healthy); migrator: exited (0)
curl -fsS http://localhost:8080/health/live      # {"status":"Healthy",...} (process up; runs no checks)
curl -fsS http://localhost:8080/health/ready     # JSON per component: database, veteran-lookup, attachment-storage
curl -sS  http://localhost:8080/health           # every registered check, same JSON shape
scripts/smoke-docker.sh                          # expects "SMOKE: PASS" (framework JS and static assets)
```

The health endpoints answer JSON. The top-level `status` is the worst component status. Any `Unhealthy` component
makes the response 503, so the app never reports healthy while one of its dependencies is down:

```json
{
  "status": "Unhealthy",
  "totalDurationMs": 1012.4,
  "components": {
    "database": { "status": "Unhealthy", "durationMs": 1004.9, "description": "Database is unreachable.", "data": { "errorType": "NpgsqlException" } },
    "veteran-lookup": { "status": "Healthy", "durationMs": 8.2 },
    "attachment-storage": { "status": "Healthy", "durationMs": 0.6 }
  }
}
```

Exception messages are never included, because they can contain connection strings. Each check times out after 5
seconds.

`scripts/verify-clean-start.sh` checks a completely fresh install from empty volumes. **It runs `docker compose down -v`
first, which deletes all existing data.**

</details>

---

<details>
<summary><b>Demo accounts</b></summary>

Synthetic, seeded by the migrator. Sign in at `/account/login`.

| Email | Password | Role |
|---|---|---|
| `caseworker@example.test` | `Demo-Passw0rd!` | CaseWorker: view, create and edit cases, lookup, attachments; start and resolve cases |
| `supervisor@example.test` | `Demo-Passw0rd!` | Supervisor: everything a case worker can do, plus triage, close and reopen |
| `qa@example.test` | `Demo-Passw0rd!` | QualityAnalyst: read cases; view and import test results (Administration > Quality assurance) |

The password (`Seed:DemoPassword`) is applied only when an account is first created; re-seeding never resets it. Five
wrong passwords lock an account for five minutes. There is no self-registration or password reset. Roles and
permissions: [ADR 0007](docs/adr/0007-roles-and-permissions.md). Guided tour: [docs/demo.md](docs/demo.md).

</details>

---

<details>
<summary><b>Test data: veteran lookup and attachments</b></summary>

**Veteran lookup** is a synthetic mock (`veteran-lookup`), reachable only inside the Compose network at
`http://veteran-lookup:8081`. The app accepts `NNN-NN-NNNN` or nine digits. All 50 records are synthetic (SSN area
numbers 900-999 are never issued); see `src/CaseIntake.VeteranLookup.Mock/Data/veterans.json`. Contract:
[docs/contracts/veteran-lookup.openapi.yaml](docs/contracts/veteran-lookup.openapi.yaml)
([ADR 0008](docs/adr/0008-veteran-lookup-mock-and-client.md)).

| SSN | Result |
|---|---|
| `900-12-3456` | Found: Rowan Calloway, 6243 Placeholder Street, Fernwick, CA 00023 |
| `902-17-3062` | Found: Harper Kestrel, 7979 Placeholder Drive, Foxhollow, MD 00028 |
| `962-91-6025` | Found: Casey Juniper, 381 Demo Court, Duskmoor, PA 00067 |
| `999-99-9999` | Not found (404) |
| `12345` | Rejected by the app as an invalid format; no request is sent |

**Attachments**: up to 10 MiB; `.pdf`, `.png`, `.jpg`/`.jpeg`, `.txt`, `.docx`. Files are stored on the `attachments`
volume (metadata in PostgreSQL) and downloaded from `/cases/{caseId}/attachments/{id}/content` by users with
`attachments.download` ([ADR 0009](docs/adr/0009-attachment-storage.md)). Limits are set in **Configuration** below.

</details>

---

<details>
<summary><b>Run the tests</b></summary>

The suites run inside Docker against `web-e2e`, an isolated copy of the production image with its own database, so the
demo data is never changed. The test image (.NET SDK + Playwright Chromium) is large and built only for this.

```bash
docker compose run --rm tests    # all suites; non-zero exit on any failure
scripts/run-all-tests.sh         # the same, plus git/build metadata and the server log in the results
scripts/run-all-tests.sh unit    # or: integration | ui
```

Results go to the `testresults` volume and are imported into the app within about a minute: sign in as
`qa@example.test` and open **Administration > Quality assurance > Test results** (`/test-results`). Suites, evidence
and local browser runs: [docs/testing.md](docs/testing.md).

</details>

---

<details>
<summary><b>Stop, update, uninstall</b></summary>

```bash
docker compose stop                              # stop, keep containers and data
docker compose start                             # start again
docker compose down                              # remove containers, keep data volumes
git pull && docker compose up --build -d         # update; the migrator applies new migrations
docker compose --profile tests down -v --rmi local   # uninstall: containers, ALL data volumes, local images
```

`-v` permanently deletes the database, attachments and test results. Data lives in named volumes:

| Volume | Contents |
|---|---|
| `pgdata18` | PostgreSQL databases (`caseintake`, and `caseintake_e2e` for browser tests) |
| `attachments` | uploaded case attachments |
| `dataprotection` | Data Protection keys, so sign-in cookies survive restarts |
| `testresults` | test runs (TRX files, evidence) shown in the Test results module |
| `attachments-e2e`, `testfixtures` | data for the isolated browser-test copy of the app |

</details>

---

<details>
<summary><b>Local development (from source)</b></summary>

Needs the .NET SDK 10.0.402 and Docker (for PostgreSQL). Each `dotnet run` below is a separate terminal.

```bash
# 1. Tools and build
dotnet tool restore                              # restores dotnet-ef
dotnet build

# 2. PostgreSQL 18 on localhost:5432 (a standalone container, separate from the Compose database)
docker run -d --name caseintake-pg -p 5432:5432 \
  -e POSTGRES_USER=caseintake -e POSTGRES_PASSWORD=caseintake_local_only -e POSTGRES_DB=caseintake \
  postgres:18.6-trixie@sha256:74935e72241653ca55e0414067e6d8763aceb8a810eb51b452253ec3dcfc4336

# 3. Schema and seed data (safe to rerun; run again after pulling new migrations)
dotnet run --project src/CaseIntake.MigrationService

# 4. Veteran lookup mock
dotnet run --project src/CaseIntake.VeteranLookup.Mock --urls http://localhost:8081

# 5. Web app: http://localhost:8080, Development environment
dotnet run --project src/CaseIntake.Web
```

To use your own PostgreSQL server, change `ConnectionStrings:CaseIntake`. Outside Docker, attachments are stored in
`<system temp>/caseintake-attachments` and test results read from `<system temp>/caseintake-test-results` unless
`Attachments__RootPath` / `TestResults__RootPath` are set; the web app and migrator use the same defaults, so they agree.

**Tests and maintenance:**

```bash
dotnet test                                      # starts a disposable PostgreSQL via Testcontainers (Docker must run)
dotnet ef migrations add <Epic>_<Change> -p src/CaseIntake.Persistence -s src/CaseIntake.Persistence -o Migrations
scripts/update-contract-snapshots.sh             # after changing the lookup mock: regenerates the OpenAPI YAML and dataset
```

Set `TESTKIT_POSTGRES_ADMIN` to an admin connection string to run tests against an existing server. The browser suite
(`CaseIntake.E2E`) needs the Docker-hosted `web-e2e`; see [docs/testing.md](docs/testing.md).

</details>

---

<details>
<summary><b>Configuration</b></summary>

Settings come from `appsettings.json` and can be overridden with environment variables, using `__` as the section
separator (for example `Attachments__MaxBytes=5242880`). In Docker, set them under the service's `environment:` in
`docker-compose.yml`. All settings have working defaults.

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionStrings:CaseIntake` | `Host=localhost;Port=5432;Database=caseintake;Username=caseintake;Password=caseintake_local_only;Timezone=UTC` | PostgreSQL connection (web and migrator) |
| `VeteranLookup:BaseUrl` | `http://localhost:8081` (Compose: `http://veteran-lookup:8081`) | lookup service address |
| `VeteranLookup:TotalTimeout` / `AttemptTimeout` / `RetryDelay` | `00:00:05` / `00:00:02` / `00:00:00.200` | lookup timeout budget (one retry at most) |
| `Seed:DemoPassword` | `Demo-Passw0rd!` | password given to demo accounts **when first created** |
| `Attachments:RootPath` | `<temp>/caseintake-attachments` (Compose: `/attachments`) | storage folder; must match for web and migrator |
| `Attachments:MaxBytes` | `10485760` (10 MiB) | largest upload |
| `Attachments:AllowedTypes:N:ContentType` / `:Extensions:M` | PDF, PNG, JPEG, TXT, DOCX | allowed attachment types |
| `DataProtection:KeysPath` | unset, in memory (Compose: `/dataprotection`) | persist cookie encryption keys |
| `TestResults:RootPath` | `<temp>/caseintake-test-results` (Compose: `/test-results`) | where test runs are read from |
| `TestResults:ScanInterval` | `00:01:00` (`00:00:00` disables) | automatic import interval |
| `TestResults:ArtifactRetention` / `KeepLatestRuns` | 90 days / 30 runs | evidence retention (results are kept) |
| `TestResults:MaxArtifactBytes` | 200 MiB | largest evidence file imported |
| `Identity:RevalidationInterval` | `00:01:00` | how often open sessions re-check roles |
| `Dashboard:OverdueAfterDays` / `TrendMonths` / `RecentActivityDays` | 14 / 12 / 14 | dashboard calculations |

</details>

---

<details>
<summary><b>Troubleshooting</b></summary>

| Symptom | What to do |
|---|---|
| `port is already allocated` for 8080 | stop whatever uses it (`docker compose ls` shows other stacks), or change `"8080:8080"` in `docker-compose.yml` to e.g. `"8090:8080"` |
| `web` never becomes healthy | `docker compose logs migrator web`. A failed migrator blocks `web`; check that `db` is healthy |
| Pages render but buttons do nothing, or styles are missing | run `scripts/smoke-docker.sh`; see [docs/docker-troubleshooting.md](docs/docker-troubleshooting.md) |
| `bash\r: No such file or directory` | delete `scripts/*.sh` and run `git checkout -- scripts` |
| Reconnect dialog stays open | the WebSocket to `/_blazor` is blocked (proxy) or the server restarted; press Retry or Reload |
| Demo sign-in fails | account locked (wait five minutes) or password changed; `Seed:DemoPassword` does not reset existing accounts |
| `A compatible .NET SDK was not found` | install exactly SDK 10.0.402 |
| Test results page is empty | wait a minute for the background import, or press **Import new runs** |

More: [docs/docker-troubleshooting.md](docs/docker-troubleshooting.md).

</details>

---

<details>
<summary><b>Before a shared or production deployment</b></summary>

Out of scope here (see [docs/assumptions-and-limitations.md](docs/assumptions-and-limitations.md)), but required:

- **HTTPS** with a trusted certificate, usually via a reverse proxy that allows WebSocket upgrades to `/_blazor`.
  Without it the sign-in cookie is not `Secure`, there is no HSTS, and the PWA is unavailable.
- **A real identity provider**; remove the seeded demo accounts and `Seed:DemoPassword`.
- **Secrets** from a secret store, not `docker-compose.yml`.
- A **real veteran lookup service** instead of the mock.
- **Backups** for the database and `attachments` volume, plus monitoring and alerting (logs go to stdout only).
- **Malware scanning** for uploaded attachments.

</details>

---

<details>
<summary><b>Project layout</b></summary>

| Path | Purpose |
|---|---|
| `src/CaseIntake.Contracts` | Shared service contracts and DTOs (additive changes only) |
| `src/CaseIntake.Domain` | Domain types: `Case`, `CaseAuditEntry`, `CaseWorkflow`, `CaseTextRules`, `Attachment`, `AttachmentFileName` |
| `src/CaseIntake.Cases` | `ICaseService` (intake, detail, edit, list, status workflow, audit trail) and the case seed |
| `src/CaseIntake.Attachments` | `IAttachmentService` (upload, list, open), filesystem store, startup storage reconciler |
| `src/CaseIntake.VeteranLookup.Client` | Typed, resilient `IVeteranLookupClient` |
| `src/CaseIntake.VeteranLookup.Mock` | Standalone synthetic lookup service; source of the OpenAPI contract |
| `src/CaseIntake.Identity` | Identity and cookies, permission policies, `ICurrentUser`, security headers, demo-user seed |
| `src/CaseIntake.Persistence` | `AppDbContext`, Identity types, migrations, seeding runner, UTC conventions |
| `src/CaseIntake.ServiceDefaults` | JSON logging with SSN scrubbing, operation scopes, health checks |
| `src/CaseIntake.MigrationService` | One-shot migrate and seed worker |
| `src/CaseIntake.TestResults` | Test results module: TRX/evidence import, history, redaction, retention, exports |
| `src/CaseIntake.Web` | Blazor host and composition root: `Components/Layout`, `Components/Kit`, `Components/Pages`, `Components/Account`, `Endpoints/*` |
| `tests/CaseIntake.TestKit` | Fixtures, `CaseIntakeWebFactory`, fakes of every contract |
| `tests/CaseIntake.Cases.Tests` | Workflow (all 25 pairs), text rules, change detection, permissions |
| `tests/CaseIntake.Identity.Tests` | Permission matrix, actor snapshots, `ICurrentUser`, policies, CSP, return URLs |
| `tests/CaseIntake.Attachments.Tests` | File-name sanitizing, type allowlist, filesystem store |
| `tests/CaseIntake.VeteranLookup.Tests` | Mock behaviour, OpenAPI contract and snapshot, dataset, client retries and timeouts |
| `tests/CaseIntake.IntegrationTests` | Architecture, migrations, seeding, logging, health, services, authorization, static assets, exports (PostgreSQL) |
| `tests/CaseIntake.Web.Tests` | bUnit component tests: layout landmarks, form kit, pages per role, dashboard, test results |
| `tests/CaseIntake.E2E` | Playwright + axe browser suite against the Docker-hosted app (keyboard, roles, viewports, framework JS) |

Phones and tablets: the app adapts below 1024 px and 768 px and is installable as a PWA on HTTPS or localhost
([ADR 0011](docs/adr/0011-ui-accessibility-and-mobile.md)).

</details>

## Documents

- [Epic plan](docs/Epic_Plan.md)
- [Implementation prompt](docs/implementation-prompt.md)
- [ADRs](docs/adr/README.md)
- [Versions](docs/versions.md)
- [Assumptions and limitations](docs/assumptions-and-limitations.md)
- [Verification](docs/verification.md)
- [Demo walkthrough](docs/demo.md)
- [Testing](docs/testing.md)
- [Docker troubleshooting](docs/docker-troubleshooting.md)
- [Accessibility review](docs/accessibility-review.md).
Traceability and the requirement-change note are produced at G3/G4.
