# Case Intake and Triage

Blazor Web App (.NET 10, Interactive Server) with PostgreSQL, built per [docs/Epic_Plan.md](docs/Epic_Plan.md).

> **Status:** Epic 1 (platform, persistence, delivery pipeline), Epic 2 (case domain, intake, list, workflow and
> audit services, with 72 seeded synthetic cases), Epic 3 (local sign-in, roles, permissions) and Epic 4 (Veteran
> Lookup mock service and client, attachment upload/list/download services and the download endpoint) are in place.
> The pages that use them arrive with Epic 5, so after signing in `http://localhost:8080` shows a host page only.
> See [docs/verification.md](docs/verification.md) for what has been verified.

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

**Demo accounts** (synthetic, seeded by the migrator; sign in at `http://localhost:8080/account/login`):

| Email | Password | Role |
|---|---|---|
| `caseworker@example.test` | `Demo-Passw0rd!` | CaseWorker: view, create and edit cases, lookup, attachments; start and resolve cases |
| `supervisor@example.test` | `Demo-Passw0rd!` | Supervisor: everything a case worker can do, plus triage, close and reopen |

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

Browser (E2E) prerequisites will be documented here with the first Playwright test.

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
| `src/CaseIntake.Web` | Blazor host and composition root; `Components/Account` holds the login and access-denied pages (E3); `Endpoints/Attachments` the download endpoint (E4) |
| `tests/CaseIntake.TestKit` | Fixtures, `CaseIntakeWebFactory`, fakes of every contract |
| `tests/CaseIntake.Cases.Tests` | E2 unit tests: workflow (all 25 pairs), text rules, change detection, permissions |
| `tests/CaseIntake.Identity.Tests` | E3 unit tests: golden permission matrix, actor snapshots, `ICurrentUser` adapters, policies, CSP, return URLs |
| `tests/CaseIntake.Attachments.Tests` | E4 unit tests: file-name sanitizing, type allowlist and its validation, filesystem store |
| `tests/CaseIntake.VeteranLookup.Tests` | E4 tests: mock behaviour, OpenAPI contract and snapshot, dataset, client outcomes, retries and timeouts (in-process and against the Compose service) |
| `tests/CaseIntake.IntegrationTests` | Architecture, migration, UTC, seeding, logging, health, case-service, identity/authorization and attachment service/endpoint tests (PostgreSQL) |

## Documents

[Epic plan](docs/Epic_Plan.md) · [Implementation prompt](docs/implementation-prompt.md) · [ADRs](docs/adr/README.md) ·
[Versions](docs/versions.md) · [Assumptions and limitations](docs/assumptions-and-limitations.md) ·
[Verification](docs/verification.md). Traceability, the demo walkthrough and the requirement-change note are produced at G3/G4.
