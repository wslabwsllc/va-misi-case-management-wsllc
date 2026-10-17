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
