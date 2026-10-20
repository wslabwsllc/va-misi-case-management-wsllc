# Running the tests and reading the results

## One command

```bash
scripts/run-all-tests.sh            # everything: unit, bUnit component, integration, browser (Playwright + axe)
scripts/run-all-tests.sh unit       # unit and bUnit component suites
scripts/run-all-tests.sh integration
scripts/run-all-tests.sh ui         # browser suite against the Docker-hosted web-e2e copy of the app
```

The script records the git branch, commit and a build ID, rebuilds the images, runs the `tests` service, attaches the
redacted `web-e2e` server log to the run, and exits with the suite's exit code (0 only when every selected suite ran
and passed). `docker compose run --rm tests` (the specification's command) runs the same suites without the git metadata
and server log.

Requires Docker with Compose v2 and bash (Git Bash on Windows). The `tests` service depends on `db`,
`veteran-lookup` and `web-e2e` (the production image, own database `caseintake_e2e`, own attachments and results
folder), so browser tests never change the demo data. Nothing is downloaded at run time: Chromium is in the image.

## Suites

| Project | Type | What it covers |
|---|---|---|
| `CaseIntake.Cases.Tests`, `Identity.Tests`, `Attachments.Tests`, `VeteranLookup.Tests` | Unit | domain rules, permission matrix, adapters, storage, lookup client and mock, conformance to the Government lookup contract (spec 4.6) |
| `CaseIntake.Web.Tests` | Component (bUnit) | layout landmarks, form kit attributes and focus, pages per role (bUnit authorization context), dashboard data/empty/error, test results pages |
| `CaseIntake.IntegrationTests` | Integration (PostgreSQL) | services, migrations, identity and authorization, endpoints, static asset delivery, test results import/redaction/retention |
| `CaseIntake.E2E` | End-to-end (Playwright Chromium headless + axe-core) | framework JS in Docker, primary keyboard workflow, roles, access denied, Not Found, 390x844 / 375x812 / 768x1024 / 1440x900 layouts, test results module |

bUnit 2 names the authorization helper `BunitAuthorizationContext` (bUnit 1's `TestAuthorizationContext`).

The browser workflow uses keyboard navigation and Playwright's programmatic file selection (`SetInputFiles`), which
bypasses the native file picker; the native picker and the browser download prompt are checked manually
([accessibility-review.md](accessibility-review.md)).

## Where results go

Each run gets its own folder on the `testresults` volume, and earlier runs are never deleted:

```
runs/<runKey>/run.json                 manifest (written last; status, git, build, image, environment, suites)
runs/<runKey>/<Project>.trx            VSTest results per suite
runs/<runKey>/catalog/<Assembly>.json  xUnit Category traits (Story:, Req:, Priority:) for linking
runs/<runKey>/evidence/<test>/         evidence.json, screenshots, trace.zip (on failure), video.webm (E2E_VIDEO=1)
runs/<runKey>/server-web-e2e.redacted.log
latest-run                             key of the newest run
```

The web app imports new run folders within a minute (or **Import new runs** for users with `testresults.manage`) and
shows them under **Administration > Quality assurance > Test results** (`qa@example.test`). Results persist in PostgreSQL
and evidence on the volume, so they survive rebuilding and recreating containers.

Outcomes: Passed, Failed, Skipped (skipped on purpose), Blocked (the run stopped under the test) and **Not executed**
(its suite failed to start or produced no results). A suite that cannot start is reported as `FAILED TO START` in the
console summary and as `FailedToStart` in the run.

Options: `E2E_TRACE=always` keeps traces of passing browser tests too; `E2E_VIDEO=1` records videos;
`TestResults__ArtifactRetention` / `TestResults__KeepLatestRuns` change evidence retention (default 90 days, newest 30 runs).

## Running outside Docker

```bash
dotnet test tests/CaseIntake.Web.Tests          # no database needed
dotnet test tests/CaseIntake.IntegrationTests   # starts a disposable PostgreSQL through Testcontainers (Docker required)
# Browser tests against a running stack:
docker compose up -d && E2E_BASE_URL=http://localhost:8080 dotnet test tests/CaseIntake.E2E
```

Locally, install the browser once: `pwsh tests/CaseIntake.E2E/bin/Debug/net10.0/playwright.ps1 install chromium`.
Against the demo stack the browser tests create cases in the demo database; prefer the Docker command above.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `RESULT: FAIL` with `FAILED TO START (application not reachable)` | `web-e2e` unhealthy: `docker compose --profile tests logs web-e2e` |
| `NO RESULTS (exit=...)` for a suite | the test host crashed before writing TRX; the run is recorded as Partial with that suite's tests Not executed |
| A failed browser test | open it in Test results: steps, failure screenshot, console and network problems; download `trace.zip` and open it with `npx playwright show-trace trace.zip` |
| axe violation | the failure lists rule, impact and elements; fix the markup (see ADR 0011) |
| The run is missing from the dashboard | `run.json` not yet written (run still going) or import problems (web log event 5202) |
