# Case Intake and Triage

Blazor Web App (.NET 10, Interactive Server) with PostgreSQL, built per [docs/Epic_Plan.md](docs/Epic_Plan.md).

> **Status:** Epics 1 to 4 (platform, case domain with 72 seeded synthetic cases, identity and roles, veteran lookup
> and attachments) and Epic 5 (the Blazor UI: dashboard, case list, intake with lookup, detail and edit, status
> workflow, attachments, a Power Apps-style desktop layout and a mobile app shell, the in-app Test results module and
> the Playwright + axe suite) are in place. See [docs/verification.md](docs/verification.md) for what has been
> verified and what is still open (manual accessibility review).

## Run

Requires Docker with Compose v2. Services run as `linux/amd64` (Apple Silicon runs them under emulation, which is slower).

```bash
docker compose up --build        # http://localhost:8080
docker compose run --rm tests    # complete automated suite; non-zero exit on any failure
scripts/run-all-tests.sh         # the same suites, plus git/build metadata and the server log in the results (bash)
scripts/smoke-docker.sh          # against a running stack: framework JS and static asset delivery
```

`docker compose up --build` builds the images, starts `db`, runs the one-shot `migrator` (migrate + seed), then starts
`veteran-lookup` and `web`. Health: `/health/live`, `/health/ready` (database and veteran lookup). Credentials in
`docker-compose.yml` are synthetic and local-only; no `.env` file is needed.
`scripts/verify-clean-start.sh` brings the stack up from empty volumes and probes the health endpoints.

**Demo accounts** (synthetic, seeded by the migrator; sign in at `http://localhost:8080/account/login`):

| Email | Password | Role |
|---|---|---|
| `caseworker@example.test` | `Demo-Passw0rd!` | CaseWorker: view, create and edit cases, lookup, attachments; start and resolve cases |
| `supervisor@example.test` | `Demo-Passw0rd!` | Supervisor: everything a case worker can do, plus triage, close and reopen |
| `qa@example.test` | `Demo-Passw0rd!` | QualityAnalyst: read cases; view test results and evidence, import runs (Administration > Quality assurance) |

The password is `Seed:DemoPassword` (`Seed__DemoPassword` in `docker-compose.yml`). It is applied when an account is
first created; re-seeding never resets a changed password. Five wrong passwords lock an account for five minutes.
There is no self-registration. Roles and permissions: [ADR 0007](docs/adr/0007-roles-and-permissions.md).

**Veteran lookup** (synthetic mock, `veteran-lookup` service, reachable only on the Compose network at
`http://veteran-lookup:8081`). Contract: [docs/contracts/veteran-lookup.openapi.yaml](docs/contracts/veteran-lookup.openapi.yaml),
generated from the mock's code ([ADR 0008](docs/adr/0008-veteran-lookup-mock-and-client.md)). The app accepts
`NNN-NN-NNNN` or nine digits. All 50 records are synthetic (SSN area numbers 900-999 are never issued); see
`src/CaseIntake.VeteranLookup.Mock/Data/veterans.json`.

| SSN | Result |
|---|---|
| `900-12-3456` | Found: Rowan Calloway, 6243 Placeholder Street, Fernwick, CA 00023 |
| `902-17-3062` | Found: Harper Kestrel, 7979 Placeholder Drive, Foxhollow, MD 00028 |
| `962-91-6025` | Found: Casey Juniper, 381 Demo Court, Duskmoor, PA 00067 |
| `999-99-9999` | Not found (404) |
| `12345` | Rejected by the app as an invalid format; no request is sent |

**Attachments** are stored on the `attachments` volume (metadata in PostgreSQL) and downloaded from
`/cases/{caseId}/attachments/{id}/content` by users with `attachments.download`. Defaults: 10 MiB; `.pdf`, `.png`,
`.jpg`/`.jpeg`, `.txt`, `.docx` (`Attachments__MaxBytes`, `Attachments__AllowedTypes__N__ContentType` /
`__Extensions__M`). See [ADR 0009](docs/adr/0009-attachment-storage.md).

## Local development

Requires .NET SDK 10.0.402 (see `global.json`) and Docker (tests start a disposable PostgreSQL through Testcontainers, or use
`TESTKIT_POSTGRES_ADMIN` to point at an existing server).

```bash
dotnet tool restore
dotnet build
dotnet test
dotnet ef migrations add <Epic>_<Change> -p src/CaseIntake.Persistence -s src/CaseIntake.Persistence -o Migrations
scripts/update-contract-snapshots.sh   # after changing the lookup mock: regenerates the OpenAPI YAML and dataset
```

Locally the web app expects the lookup mock at `http://localhost:8081`
(`dotnet run --project src/CaseIntake.VeteranLookup.Mock --urls http://localhost:8081`). Outside Compose, attachments are stored in
`<system temp>/caseintake-attachments` unless `Attachments__RootPath` is set (one folder per machine, so a locally run
web app and migrator always agree); Compose uses the `attachments` volume.

Browser tests (Playwright Chromium + axe-core) run inside the `tests` container against `web-e2e`, an isolated copy of
the production image with its own database; see [docs/testing.md](docs/testing.md) for suites, results, evidence and
local runs, and [docs/docker-troubleshooting.md](docs/docker-troubleshooting.md) for container problems (including the
fixed `blazor.web.js` delivery defect).

**Test results** from every run are kept on the `testresults` volume and imported into the app: sign in as
`qa@example.test` and open **Administration > Quality assurance > Test results** (`/test-results`).

**Phones and tablets:** the same app adapts below 1024 px (collapsible sidebar) and below 768 px (bottom navigation,
cards, sheets). It is installable as a PWA on HTTPS or localhost; the service worker caches static assets only, never
pages, case data, downloads or sign-in responses ([ADR 0011](docs/adr/0011-ui-accessibility-and-mobile.md)).

## Layout

| Path | Purpose |
|---|---|
| `src/CaseIntake.Contracts` | Shared service contracts and DTOs (additive changes only) |
| `src/CaseIntake.Domain` | Domain types: `Case`, `CaseAuditEntry`, `CaseWorkflow`, `CaseTextRules` (E2); `Attachment`, `AttachmentFileName` (E4) |
| `src/CaseIntake.Cases` | `ICaseService` implementation (intake, detail, edit, list, status workflow, audit trail) and the case seed |
| `src/CaseIntake.Attachments` | `IAttachmentService` (upload, list, open), filesystem store, startup storage reconciler (E4) |
| `src/CaseIntake.VeteranLookup.Client` | Typed, resilient `IVeteranLookupClient` (E4) |
| `src/CaseIntake.VeteranLookup.Mock` | Standalone synthetic lookup service and its Dockerfile; source of the OpenAPI contract (E4) |
| `src/CaseIntake.Identity` | Identity and cookie setup, permission policies, `ICurrentUser` adapters, security headers, demo-user seed |
| `src/CaseIntake.Persistence` | `AppDbContext`, Identity types, migrations, seeding runner, UTC conventions |
| `src/CaseIntake.ServiceDefaults` | JSON logging with SSN scrubbing, operation scopes, health checks |
| `src/CaseIntake.MigrationService` | One-shot migrate and seed worker |
| `src/CaseIntake.TestResults` | Test results module (E5): TRX/evidence import, history, redaction, retention, exports |
| `src/CaseIntake.Web` | Blazor host and composition root; `Components/Layout` shell, `Components/Kit` accessible component kit, `Components/Pages` (E5); `Components/Account` login and access-denied pages (E3, styled by E5); `Endpoints/Attachments` download (E4); `Endpoints/Exports`, `Endpoints/TestResults` (E5) |
| `tests/CaseIntake.TestKit` | Fixtures, `CaseIntakeWebFactory`, fakes of every contract |
| `tests/CaseIntake.Cases.Tests` | E2 unit tests: workflow (all 25 pairs), text rules, change detection, permissions |
| `tests/CaseIntake.Identity.Tests` | E3 unit tests: golden permission matrix, actor snapshots, `ICurrentUser` adapters, policies, CSP, return URLs |
| `tests/CaseIntake.Attachments.Tests` | E4 unit tests: file-name sanitizing, type allowlist and its validation, filesystem store |
| `tests/CaseIntake.VeteranLookup.Tests` | E4 tests: mock behaviour, OpenAPI contract and snapshot, dataset, client outcomes, retries and timeouts (in-process and against the Compose service) |
| `tests/CaseIntake.IntegrationTests` | Architecture, migration, UTC, seeding, logging, health, case-service, identity/authorization, attachment, static asset delivery, export and test results tests (PostgreSQL) |
| `tests/CaseIntake.Web.Tests` | E5 bUnit component tests: layout landmarks, form kit, pages per role, dashboard, test results pages |
| `tests/CaseIntake.E2E` | E5 Playwright + axe browser suite against the Docker-hosted app (keyboard workflow, roles, viewports, framework JS) |

## Documents

[Installation guide](docs/installation.md) · [Epic plan](docs/Epic_Plan.md) · [Implementation prompt](docs/implementation-prompt.md) · [ADRs](docs/adr/README.md) ·
[Versions](docs/versions.md) · [Assumptions and limitations](docs/assumptions-and-limitations.md) ·
[Verification](docs/verification.md) · [Demo walkthrough](docs/demo.md) · [Testing](docs/testing.md) ·
[Docker troubleshooting](docs/docker-troubleshooting.md) · [Accessibility review](docs/accessibility-review.md).
Traceability and the requirement-change note are produced at G3/G4.
