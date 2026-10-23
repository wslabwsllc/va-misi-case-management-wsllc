# Case Intake and Triage

Blazor Web App (.NET 10, Interactive Server) with PostgreSQL, a synthetic veteran lookup service and an in-app test
results module, built per [docs/Epic_Plan.md](docs/Epic_Plan.md). It works the same on Windows, macOS and Linux.

<details>
<summary><b>Install prerequisites</b></summary>

To run the app with Docker Compose, you only need **Docker**. Git is for cloning; .NET is only for building and testing
from source.

| Tool | Needed for | Install |
|---|---|---|
| Docker Desktop | running the app (`docker compose`) | [docs.docker.com/get-started/get-docker](https://docs.docker.com/get-started/get-docker/) |
| Git | cloning the repo | [git-scm.com/downloads](https://git-scm.com/downloads) |
| .NET SDK **10.0.402** | local development from source only | [dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0) |

Pick SDK **10.0.402** exactly: `global.json` disables roll-forward, so other patch versions are rejected.

On Apple Silicon Macs, give Docker at least **8 GB of memory** (Docker Desktop **Settings > Resources**) and turn on
**Use Rosetta for x86_64/amd64 emulation** if offered; the images are `linux/amd64` and run emulated.

</details>

---

<details>
<summary><b>Quick start</b></summary>

Needs only Docker and free port 8080 (see **Install prerequisites** above).

**1. Start docker desktop**

Go to your finder and open the docker desktop application you installed in the installation step

**2. clone the**

```bash
git clone https://github.com/wslabwsllc/va-misi-case-management-wsllc.git
```

**3. Change directory**

```bash
cd va-misi-case-management-wsllc
```

**3. Start the application**

```bash
docker compose up --build       # runs in the background, returns once web is healthy
```

Open [http://localhost:8080](http://localhost:8080) to access the running application. The first build takes several minutes. No `.env` file or secrets are needed:

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

Synthetic, seeded by the migrator. Sign in at `/account/login` (the sign-in page lists these accounts).
The four case roles build on each other (Auditor ⊂ IntakeClerk ⊂ CaseWorker ⊂ Supervisor); QualityAnalyst is separate.

| Email | Password | Role |
|---|---|---|
| `auditor@example.test` | `Demo-Passw0rd!` | Auditor: read-only; view cases, audit trails and documents, export the case list, read and export the audit log (Administration > Audit log) |
| `intake@example.test` | `Demo-Passw0rd!` | IntakeClerk: view and open new cases, veteran lookup, upload attachments; no editing or status changes |
| `caseworker@example.test` | `Demo-Passw0rd!` | CaseWorker: everything an intake clerk can do, plus edit cases and start and resolve them |
| `supervisor@example.test` | `Demo-Passw0rd!` | Supervisor: everything a case worker can do, plus triage, close and reopen, and manage users within their own access (Administration > Users) |
| `qa@example.test` | `Demo-Passw0rd!` | QualityAnalyst: read cases; view and import test results (Administration > Quality assurance) |
| `admin@example.test` | `Demo-Passw0rd!` | Administrator: every permission; manages users, roles and per-user permission overrides (Administration > Users, Roles) |

The password (`Seed:DemoPassword`) is applied only when an account is first created; re-seeding never resets it. Five
wrong passwords lock an account for five minutes. Administrators (and supervisors, within their own access) add users
with one or more roles and an initial password, change roles, deactivate accounts and allow or deny single permissions
per user; administrators also define roles. Auditors, supervisors and administrators read the system-wide audit log
(sign-ins, access denials, user and role changes, cases, attachments, test results), open any entry and export it as
CSV ([ADR 0014](docs/adr/0014-system-audit-log.md)). Administrators also see recorded errors (Administration > Errors, with the
reference shown on the error page) and System health ([ADR 0015](docs/adr/0015-error-log-and-system-health.md)). Every user
changes their own password from the header (Change password). Times are US Eastern, labelled ET (never EST or EDT), in en-US format;
Section 508 evidence is in [docs/section-508](docs/section-508/README.md).
There is no self-registration or password reset by email. Roles and permissions: [ADR 0007](docs/adr/0007-roles-and-permissions.md)
and [ADR 0013](docs/adr/0013-database-roles-and-permissions.md). Guided tour: [docs/demo.md](docs/demo.md).

</details>

---

<details>
<summary><b>Test data: veteran lookup and attachments</b></summary>

**Veteran lookup** is a synthetic mock (`veteran-lookup`), reachable only inside the Compose network at
`http://veteran-lookup:8081`. The app accepts `NNN-NN-NNNN` or nine digits. All 50 records are synthetic (SSN area
numbers 900-999 are never issued); see `src/CaseIntake.VeteranLookup.Mock/Data/veterans.json`. Contract: the
Government's definition from specification 4.6, committed verbatim as
[docs/contracts/veteran-lookup.spec.openapi.yaml](docs/contracts/veteran-lookup.spec.openapi.yaml) and served unchanged
by the mock at `/openapi/v1.yaml`. `OpenApiConformanceTests` prove the implementation conforms to it; the 400/415
responses are documented extensions ([ADR 0008](docs/adr/0008-veteran-lookup-mock-and-client.md), assumptions D1, D11-D16).

| SSN | Result |
|---|---|
| `900-12-3456` | Found: Rowan Calloway, 6243 Placeholder Street, Fernwick, CA 00023 |
| `902-17-3062` | Found: Harper Kestrel, 7979 Placeholder Drive, Foxhollow, MD 00028 |
| `962-91-6025` | Found: Casey Juniper, 381 Demo Court, Duskmoor, PA 00067 |
| `999-99-9999` | Not found (404) |
| `12345` | Rejected by the app as an invalid format; no request is sent |

**Attachments**: up to 10 MB; `.pdf`, `.png`, `.jpg`/`.jpeg`, `.txt`, `.docx`. Files are stored on the `attachments`
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

`scripts/coverage.sh` measures line coverage on the host (unit, component and integration suites; Docker required) and
fails below 80% in total or for any assembly ([docs/testing.md](docs/testing.md#code-coverage)).

`scripts/sonar.sh` runs a local SonarQube analysis (bugs, vulnerabilities, code smells, duplication and coverage) and
publishes it to a SonarQube server in Docker at http://localhost:9000; `--skip-tests` analyses without running the
tests ([docs/testing.md](docs/testing.md#static-analysis-sonarqube)).

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
| `pgdata18-alpine` | PostgreSQL databases (`caseintake`, and `caseintake_e2e` for browser tests) |
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
dotnet tool restore                              # restores dotnet-ef and dotnet-sonarscanner
dotnet build

# 2. PostgreSQL 18 on localhost:5432 (a standalone container, separate from the Compose database)
docker run -d --name caseintake-pg -p 5432:5432 \
  -e POSTGRES_USER=caseintake -e POSTGRES_PASSWORD=caseintake_local_only -e POSTGRES_DB=caseintake \
  -e POSTGRES_INITDB_ARGS="--locale-provider=icu --icu-locale=en-US" \
  postgres:18.6-alpine3.24@sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873

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
scripts/update-contract-snapshots.sh             # regenerates the synthetic lookup dataset (never the Government contract)
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
| `Attachments:MaxBytes` | `10485760` (10 MB) | largest upload |
| `Attachments:AllowedTypes:N:ContentType` / `:Extensions:M` | PDF, PNG, JPEG, TXT, DOCX | allowed attachment types |
| `DataProtection:KeysPath` | unset, in memory (Compose: `/dataprotection`) | persist cookie encryption keys |
| `TestResults:RootPath` | `<temp>/caseintake-test-results` (Compose: `/test-results`) | where test runs are read from |
| `TestResults:ScanInterval` | `00:01:00` (`00:00:00` disables) | automatic import interval |
| `TestResults:ArtifactRetention` / `KeepLatestRuns` | 90 days / 30 runs | evidence retention (results are kept) |
| `TestResults:MaxArtifactBytes` | 200 MB | largest evidence file imported |
| `Identity:RevalidationInterval` | `00:01:00` | how often open sessions re-check roles |
| `Dashboard:NewCasesDays` / `TrendMonths` / `RecentActivityDays` | 7 / 12 / 14 | dashboard calculations (Recent cases window, trend months, recent activity window) |
| `Notifications:WindowDays` | 14 | how far back the notification feed reaches |

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
<summary><b>Runtime and versions</b></summary>

Exact pins (checked against `global.json`, `Directory.Packages.props` and the Dockerfile by `VersionConsistencyTests`;
the full list is in [docs/versions.md](docs/versions.md)):

| Component | Version |
|---|---|
| .NET SDK | 10.0.402 (`global.json`, roll-forward disabled) |
| .NET runtime and ASP.NET Core / Blazor shared framework | 10.0.13 |
| Entity Framework Core (`Microsoft.EntityFrameworkCore`, `.Design`, Identity EF stores) | 10.0.13 |
| Npgsql EF Core provider and Npgsql | 10.0.3 |
| PostgreSQL | 18.6 (`postgres:18.6-trixie`, digest pinned) |
| Runtime image | `mcr.microsoft.com/dotnet/aspnet:10.0.13-noble` (digest pinned) |
| Browser tests | Microsoft.Playwright 1.52.0 (Chromium build fixed by it), Deque.AxeCore.Playwright 4.13.0 |
| Test framework | NUnit 4.4.0 with NUnit3TestAdapter 5.2.0 and NUnit.Analyzers 4.10.0; bUnit 2.11.3 for components |

</details>

---

<details>
<summary><b>Known limitations</b></summary>

The complete, maintained list is in [docs/assumptions-and-limitations.md](docs/assumptions-and-limitations.md)
(Limitations and Known gaps). The ones an evaluator meets first:

- **Local evaluation only:** plain HTTP on `http://localhost:8080` (no HTTPS, no HSTS, the sign-in cookie is not
  `Secure`), demo passwords in `docker-compose.yml`, synthetic data only, no Government systems.
- **Identity:** local ASP.NET Core Identity with seeded demo accounts; no SSO, PIV, 2FA, self-registration or email
  password reset (an administrator resets a password; the sign-in page's "Forgot password?" explains this).
- **Attachments:** no malware scanning; files cannot be deleted; type checks are leading-byte checks, not proof of
  safety.
- **Operations:** no backups, disaster recovery, high availability, monitoring or alerting; logs go to stdout.
- **Sessions:** sign-out clears the cookie but does not revoke a copy captured earlier (valid until it expires or the
  security stamp changes); role changes reach an open session within one minute.
- **Accessibility:** automated axe, keyboard and reflow checks run with the test suite; the manual screen-reader
  (NVDA, JAWS), zoom, contrast and forced-colours reviews in
  [docs/section-508/accessibility-test-matrix.md](docs/section-508/accessibility-test-matrix.md) have not been
  performed yet. This is not a Section 508 certification or conformance claim.
- **Dashboard trend:** the resolution trend places a case by its last update, an approximation of its completion date.
- **Not yet verified:** the clean-clone cold start on a host by a non-author and the offline (no egress) run; see
  [docs/verification.md](docs/verification.md).

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
| `src/CaseIntake.VeteranLookup.Mock` | Standalone synthetic lookup service; serves the Government contract (spec 4.6) verbatim |
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
| `tests/CaseIntake.VeteranLookup.Tests` | Conformance to the Government contract, spec-driven mock and client behaviour, dataset, client retries and timeouts |
| `tests/CaseIntake.IntegrationTests` | Architecture, migrations, seeding, logging, health, services, authorization, static assets, exports (PostgreSQL) |
| `tests/CaseIntake.Web.Tests` | bUnit component tests: layout landmarks, form kit, pages per role, dashboard, test results |
| `tests/CaseIntake.E2E` | Playwright + axe browser suite against the Docker-hosted app (keyboard, roles, viewports, framework JS) |

Phones and tablets: the app adapts below 1024 px and 768 px and is installable as a PWA on HTTPS or localhost
([ADR 0011](docs/adr/0011-ui-accessibility-and-mobile.md)).

</details>

---

<details>
<summary><b>Documents</b></summary>

- [Architecture](docs/architecture.md)
- [Architecture overview](docs/architecture-overview.md)
- [ADRs](docs/adr/README.md)
- [Versions](docs/versions.md)
- [Assumptions and limitations](docs/assumptions-and-limitations.md)
- [Verification](docs/verification.md)
- [Demo walkthrough](docs/demo.md)
- [Testing](docs/testing.md)
- [Docker troubleshooting](docs/docker-troubleshooting.md)
- [Accessibility review](docs/accessibility-review.md).
- [Epic plan](docs/Epic_Plan.md)
- [Implementation prompt](docs/implementation-prompt.md)
- [Contributors](docs/contributors.md)
- [Contributor declaration](docs/contributor-declaration.md)
Traceability and the requirement-change note are produced at G3/G4.

</details>

---

<details>
<summary><b>Contributors</b></summary>

- Rikhin Bhuta
- Alhagie Bai Cham
- Kabir Shaikh
- Dr. Janarthanan Senthurpandi
- Antony Pon Jeyakumar Selvaraj

Who built what: [docs/contributors.md](docs/contributors.md). Formal declaration (Amendment 0002):
[docs/contributor-declaration.md](docs/contributor-declaration.md).

</details>
