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

# Verification record (Epic 3 identity, roles and authorization)

Base revision: `bec2b8e` plus Epic 3 changes on branch `epic-3-identity-roles` (uncommitted). Host: Windows Server 2019,
.NET SDK 10.0.402. **No Docker on this host**: PostgreSQL-backed tests ran against a local PostgreSQL **18.6** server
through `TESTKIT_POSTGRES_ADMIN` (Compose uses PostgreSQL 17); every test database was created with a random name and
dropped afterwards.

| Command | Result |
|---|---|
| `dotnet build CaseIntake.sln` | 0 warnings, 0 errors |
| `dotnet ef migrations has-pending-model-changes` | no changes (E3 needs no migration; Identity tables are in `E1_Baseline`) |
| `dotnet test CaseIntake.sln` (host, `TESTKIT_POSTGRES_ADMIN` set), final code | 423 passed (Identity.Tests 90, Cases.Tests 104, IntegrationTests 229), 0 failed, in **5 of 5** consecutive full runs; no test database left behind after any run |
| Intermittent failures before the TestKit fix | 2 of 8 full runs failed (146 and 3 integration failures). Captured cause: `PostgresDatabaseFixture.DisposeAsync` timed out on `DROP DATABASE ... WITH (FORCE)` (Npgsql's default 30 s command timeout) while many fixtures created and dropped databases in parallel, leaving test databases behind. Fix: the fixture now runs CREATE/DROP DATABASE one at a time per test process with a 300 s timeout |
| Published migrator x2 on an empty database | exit 0 both; run 1: Identity seed 2 roles, 2 users, 2 memberships (EventId 3010) before 72 cases; run 2: 0 added |
| Published web app + `curl` (database above) | anonymous `/` → 302 login; anonymous `/_framework/blazor.web.js` → 200; anonymous `POST /_blazor/negotiate` → 401; CSP/nosniff/Referrer-Policy/X-Frame-Options on the login page; wrong password → 200 with `role="alert"`; correct password → 302 `/`, `CaseIntake.Auth` HttpOnly; `/` shows `caseworker@example.test (CaseWorker)` with interactive markers; logout without token → 400, with token → 302 login; `/` afterwards → 302 login |
| Web process restarted with the same `DataProtection:KeysPath` | a cookie issued before the restart is accepted (200) |
| Web and migrator logs | EventIds 3001/3002/3004/3010 present; no demo password, attempted password or connection password in either log |
| Headless Chrome 154 over the DevTools protocol against the published app (`Identity:RevalidationInterval` 3 s) | 15 of 15 checks: anonymous `/` → login; Tab moves email → password; a failed keyboard sign-in puts focus on the `role="alert"` error and empties the password; keyboard sign-in lands on `/` showing `caseworker@example.test (CaseWorker)`; the circuit connects over WebSocket (`/_blazor`) and exchanges frames; changing the user's security stamp in the database makes the open page return to `/account/login?ReturnUrl=%2F` on its own (EventId 3006 logged); the Supervisor header shows `Supervisor`; the interactive header's sign-out form carries an antiforgery token and keyboard sign-out returns to login; `/` then requires sign-in; no CSP violations, script errors or 404s in the session |

Acceptance-criteria evidence: E3-S1 `AuthenticationTests`; E3-S2 `RolePermissionMapTests` (golden matrix) and
`AuthorizationPolicyTests`; E3-S3 `CurrentUserAdapterTests` and `CurrentUserIntegrationTests` (simultaneous circuits,
HTTP principal, revalidation after a security-stamp change); E3-S4 `IdentitySeedTests`; E3-S5
`AuthorizationEnforcementTests`, `ServiceAuthorizationMatrixTests` (3 principals × 10 `ICaseService` operations,
`AuthorizationDenied` logged), `AccessDeniedMessageTests` and `EndpointAuthorizationGuardTests`; E3-S6 `AuthenticationTests` (antiforgery on login and logout, headers, lockout) and
`RequestSecurityTests` (CSP, local return URLs).

## Not verified / blocked (E3)

- `docker compose up --build` and `docker compose run --rm tests`: not run (no Docker here; `veteran-lookup` is E4).
- The browser check above is a scratch script, not part of the suite; the permanent Playwright and axe coverage of the
  login form is E5-S9. Screen-reader announcement of the error was not checked.
- The attachment download endpoint is E4's; the 401/403/200 behavior was verified on an endpoint configured the way
  ADR 0007 requires E4 to configure it, not on E4's endpoint.
- Interactive access-denied rendering is covered at component level (`AccessDeniedMessageTests`) and the router's
  signed-out branch in the browser (revocation check); the router's signed-in-but-forbidden branch cannot occur yet
  because the only app page is the case list, which every role may open. It becomes reachable with E5's pages.
- E4's endpoint is guarded in advance: `EndpointAuthorizationGuardTests` fails if any minimal API endpoint that needs
  authorization lacks `DisableCookieRedirect()`, or if the attachment content route lacks `attachments.download`.
  Until E4 maps that route the attachment check passes with nothing to check.

# Verification record (Epic 4 integrations: veteran lookup and attachments)

**Current totals (latest run, 2026-10-19, final code after review round 4):** host `dotnet test` 649 passed,
0 warnings. Docker from empty `caseintake-e4v` volumes: `db`, `veteran-lookup`, `web` healthy, `migrator` exit 0,
`/health/live` 200, `/health/ready` 200; `docker compose run --rm tests` `RESULT: PASS` with **666** tests
(Attachments 86, Cases 106, Identity 90, Integration 284, VeteranLookup 100 including the Compose-target cases);
container log SSN/demo-name scan 0; project removed with `down -v`. Rows below are the history of earlier runs.

Base revision: `7a3ba1b` plus Epic 4 changes (uncommitted; branch and commit by the engineer). Host: macOS arm64,
Docker 29.1.3, containers `linux/amd64` under emulation, .NET SDK 10.0.402. Docker checks used an isolated Compose
project (`-p caseintake-e4v`), removed afterwards with `down -v`; demo volumes were not touched.

| Command | Result |
|---|---|
| `dotnet build CaseIntake.sln -c Release` | 0 warnings, 0 errors |
| `dotnet test CaseIntake.sln -c Release` (host, Testcontainers PostgreSQL 17) | 611 passed, 0 failed (Cases 104, Identity 90, Attachments 67, VeteranLookup 79, Integration 271) |
| `scripts/update-contract-snapshots.sh` run twice | snapshot output identical (deterministic) |
| `docker compose -p caseintake-e4v down -v` then `up --build -d --wait` | exit 0; `db`, `veteran-lookup`, `web` healthy; `migrator` exit 0; `/health/live` 200, **`/health/ready` 200** (first full-stack readiness pass), `/` 302 to login |
| Migrator log (fresh volumes) | 3010 Identity seed 2 roles/2 users → 2010 72 of 72 cases → 4110 attachment storage reconciled (0/0/0/0) → 1005 completed |
| POST `/veterans/lookup` from the `web` container to `veteran-lookup:8081` | `900-12-3456` → 200 `{"name":"Rowan Calloway",...}`; `999-99-9999` → 404 |
| `docker compose -p caseintake-e4v run --rm tests` (**no `--no-deps`**) | exit 0, `RESULT: PASS`: Attachments 67, Cases 104, Identity 90, Integration 271, VeteranLookup 96 (79 + 17 cases run against the Compose `veteran-lookup` container) = 628 |
| `down` (keep volumes) then `up -d --wait` | exit 0; `/health/ready` 200; migrator: no migrations, 0 roles/users, 0 of 72 cases added, reconciled 0 |
| SSN / demo-name scan of all container logs | 0 matches |

An earlier container run (before the review fixes) failed 2 tests: in Production the framework answered a
non-string `ssn` with an empty 400 instead of problem+json, which the Development-mode host had hidden. Fixed in
the mock (`UseStatusCodePages` for 400) and the in-process tests now run in Production mode.

Review evidence: `/code-review xhigh` (15 findings, 14 fixed with regression tests where behavioural, 1 documented:
SHA-256 not re-verified on download), `/security-review` (no HIGH/MEDIUM findings), `/simplify` (reuse,
simplification, efficiency and altitude passes applied; skipped items listed in the hand-off).

Second review round on the final code (after the first round's fixes and `/simplify`): `/security-review` no
HIGH/MEDIUM findings (raw `FOR SHARE` SQL confirmed parameterized). `/code-review xhigh` 15 findings: 10 fixed
(trailing-separator root broke reconciler deletes; a strategy retry after a committed upload could delete committed
content on the closed-case path; local web and migrator used different relative roots; version-7 keys all landed in
one shard; actor truncation could split a surrogate pair; Polly timeout limits not validated at startup; a duplicate
operation-ID scope in the mock; `.dockerignore` missed local attachment folders; 415 missing from the contract;
fsync of an over-limit upload), 5 skipped with reasons (directory fsync documented in ADR 0009; a wrong base-URL
path already fails readiness; the second actor resolution per download would need a shared-contract change; the
defensive committed-without-entity branch and the separate storage-key column are intentional; small cross-module
duplications are imposed by the module boundaries). Host after fixes: 0 warnings, 618 passed. Docker after fixes (fresh `caseintake-e4v` volumes): all services healthy, `/health/ready` 200,
`docker compose run --rm tests` `RESULT: PASS` with 635 tests (Attachments 69, Cases 105, Identity 90, Integration
271, VeteranLookup 100), container log SSN/demo-name scan 0.

Third review round (`/code-review high` on the round-2 fixes, 2026-10-18): 10 findings, 6 fixed with regression
tests (a relative root still depended on each process's working directory, now a fixed default outside the tree
when unset; file type trusted from the client's claim, now a leading-byte signature check for PDF/PNG/JPEG/DOCX;
`Case.CreatedBy/UpdatedBy` not capped like audit actors; an over-limit temporary-file delete could surface as a
storage error; truncated stored content was served, now refused on a size mismatch (4112); a dataset test checked
10 of 50 records), 4 skipped (directory fsync, documented; list gated on `cases.view` per the plan; the UTF-16 cap
cannot exceed the column; the unused configuration parameter is the module convention). Coverage of Epic 4 code
(VSTest collector, report in the workspace notes): 96.7% lines, 92.1% branches. Host: 0 warnings, 631 passed. Docker (fresh
`caseintake-e4v` volumes, removed afterwards): `/health/ready` 200, `docker compose run --rm tests` `RESULT: PASS`
with 648 tests (Attachments 72, Cases 106, Identity 90, Integration 280, VeteranLookup 100), log SSN scan 0.

Round 4 (2026-10-18, audit follow-up): tests added for the migrator entry point
(`ReconcileAttachmentStorageAsync`, run twice) and for both outcomes of the closed-case retry-after-commit branch
(simulated with `RetryAfterCommitSimulator`), plus hardening tests (domain record guards, negative grace period,
long-name truncation, reconciler delete failure). A flaky PII assertion that matched digits inside logged durations
was corrected. The Assumptions Log was rebuilt with the six required columns and the E4 story text in the plan was
brought in line with the delivered design. No production code changed. Host: 649 passed in three consecutive runs.

## Not verified / blocked (E4)

- Upload and download through the UI, and attachment durability across web-container recreation **via the UI**:
  needs E5-S7. Durability is covered by `Uploads_survive_a_restart_of_the_service_and_store` (new service and store
  instances over the same root and database) and by the named volume mounted on `migrator` and `web`.
- Offline (no-egress) runtime check: G3.
- The intake lookup UI and its five announced outcomes: E5-S4.

# Verification record (Epic 5: UI, accessibility, mobile, test results)

Base revision: `748e2c0` (EPIC-5 branch = `main`) plus the E5 changes on branch
`feature/e5-enterprise-ui-mobile-test-results`. Host: Windows Server 2025, Docker 29.8.2 (linux/amd64 engine),
.NET SDK 10.0.402. Docker runs used the repository's own Compose project (`caseintake`); the `pgdata`, `attachments`
and `dataprotection` volumes were kept (no `down -v`). Browser tests ran against `web-e2e` (own database
`caseintake_e2e`).

## Docker JavaScript defect: diagnosis

| Check | Finding |
|---|---|
| .NET / hosting model | .NET 10.0.13 Blazor Web App (`AddRazorComponents().AddInteractiveServerComponents()`), global Interactive Server routing, account pages static SSR |
| Published output | `/app/web/wwwroot/_framework/blazor.web.js` present (from `Microsoft.AspNetCore.App.Internal.Assets` 10.0.13) |
| `curl -I http://localhost:8080/_framework/blazor.web.js` (before) | **302** to `/account/login?ReturnUrl=...`; same for `/app.css` |
| Web log (before) | `"Content root path: /src"`: the image's `WORKDIR`; `/src/wwwroot` does not exist |
| Cause | static files not found from `/src`, request fell through to the authenticated fallback policy and was redirected |
| Second defect | `web` **unhealthy**: `healthcheck.sh` had CRLF endings (`/usr/bin/env: 'bash\r'`) from a `core.autocrlf=true` checkout |
| After fix | content root `/app/web`; `scripts/smoke-docker.sh`: **SMOKE: PASS** (framework script, CSS, JS modules, manifest 200 with JS/CSS types; one framework script reference; asset present in image); `web` healthy (health check now also probes `blazor.web.js`) |

Further defects found and fixed by the Docker browser runs: kit inputs lost characters typed during a server round
trip (now `@bind:event="oninput"`), the audit trail rendered the literal text `_auditError`, empty alert boxes rendered
when an alert had only child content, and `Cross-Origin-Opener-Policy` caused Chrome console errors on non-loopback HTTP
(now sent only on HTTPS or loopback).

## Commands and results (final code)

| Command | Result |
|---|---|
| `dotnet build CaseIntake.sln -c Release` | 0 warnings, 0 errors |
| `scripts/run-all-tests.sh all` (rebuild + `tests` service against `web-e2e`), run `20261019T180910Z-748e2c0d` | **RESULT: PASS**, exit 0, 847 tests |
| same, final run `20261019T182341Z-748e2c0d` after the last fixes | **RESULT: PASS**, exit 0, 847 tests |
| per suite (final run) | Attachments 86/0, Cases 106/0, Identity 104/0, VeteranLookup 100/0, Web.Tests (bUnit) 112/0, IntegrationTests 323/0, E2E (Playwright + axe) 16/0; 0 skipped; E2E 39 s, whole run about 2 min |
| `docker compose up -d --wait` then `scripts/smoke-docker.sh` | all services healthy; SMOKE: PASS |
| Demo `web` background import | all 7 completed runs imported automatically (event 5201) and kept, including the 5 earlier failing ones (78, then 1 failure each); the first attempt never started and wrote no run |
| `scripts/capture-screenshots.sh` | 18 screenshots in [screenshots/](screenshots/) (desktop, tablet 768, phone 390; cases and test results) |

Earlier runs on the way (kept in the Test results history): 78 failures (`UseStaticWebAssets` package paths differed
between build and run images; fixed by one NuGet cache path), then single E2E failures (COOP console error, lost
keystrokes, test timing, mid-animation contrast scan, a test typing before focus moved), each fixed at the cause.

E2E coverage: framework script delivery and interactivity (WebSocket circuit, button, navigation after reload, no
console errors), PWA manifest and worker policy, the primary keyboard workflow (37 recorded steps incl. all five
transitions, lookup outcomes, validation focus, upload/download byte check, sign-out), edit conflict, CaseWorker /
Supervisor / QualityAnalyst differences, access denied (HTTP and in-app with h1 focus), Not Found, anonymous 401s,
390x844, 375x812, 768x1024 and 1440x900 layouts (no horizontal overflow, 44 px bottom-bar targets), test results import,
failure evidence, screenshot preview and zoom, exports, phone dashboard. axe (WCAG 2.0/2.1/2.2 A and AA tags) found no
violations on the 16 scanned views.

## Not verified / open (E5)

- Manual accessibility review (native file picker, download prompt, screen reader, zoom 200%/400%, high contrast,
  reconnect, real iOS/Android devices): not performed; see [accessibility-review.md](accessibility-review.md).
- iOS Safari / Android Chrome: only Chromium device emulation in automation.
- The clean-clone and offline (no egress) checks of G3 were not rerun for this change.
- Runs imported before the `run-tests.sh` status fix are labelled Partial although every suite ran.
