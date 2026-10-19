# 0012. Test results, execution evidence and history inside the application

* Status: accepted
* Owner: E5

## Context

QA users need real test results (unit, bUnit component, integration and Playwright browser runs) with failures,
expected/actual results, screenshots, traces, console and network problems, server logs, history and trends, in the
same Power Apps-style UI and the same repository and Docker deployment. Evidence must be access-controlled, sensitive
values redacted, history kept across container rebuilds, and tests must not start on application startup.

## Decision

**Producing results.** `scripts/run-tests.sh` (the `tests` service) writes every run to its own folder on the
`testresults` volume and never deletes earlier runs: `runs/<runKey>/<Project>.trx`, `catalog/<assembly>.json` (xUnit
`Category` traits, written by `TestKit.TestCatalog`, because TRX does not carry traits), `evidence/<test>/` (Playwright:
`evidence.json` with steps, browser, viewport, expected/actual, console errors and network failures, plus screenshots
with password and SSN inputs masked, a trace on failure and an optional video), and `run.json` last. A suite that cannot
start (for example the app under test is unreachable) is recorded as FailedToStart. `scripts/run-all-tests.sh` is the one
documented command: it records branch, commit and build ID, rebuilds, runs the suites against the Docker-hosted
`web-e2e` copy of the production app (own database, attachments and results folder), and attaches the `web-e2e` server log.

**Storing results.** A feature module `CaseIntake.TestResults` (references Persistence, Domain, Contracts only) imports
complete run folders into PostgreSQL (migration `E5_TestResults`): `TestEnvironments`, `TestRuns`, `TestCases`,
`TestExecutions`, `TestStepResults`, `TestArtifacts` (metadata, SHA-256 and a path relative to the results root),
`TestResultAuditEntries`, and the `TestExecutionHistory` view. Runs are unique by run key, imported once in one
transaction and never overwritten. Tests of a suite that did not run are stored as **Not executed** (distinct from
Skipped and Blocked) for every test previously seen in that module. All stored text is redacted (SSN-shaped numbers,
passwords and secret-like key/value pairs, bearer tokens, cookies, the configured demo password); server logs are kept
only as a redacted copy. Evidence files stay in the run folder; every path is resolved and checked to stay inside the
root before it is read.

**Serving results.** Pages under Administration > Quality assurance (`/test-results`: dashboard, runs, run detail,
execution detail, test cases, history, evidence gallery) require `testresults.view`. Evidence and exports are served only
by `GET /test-results/artifacts/{id}` and `GET /test-results/runs/{id}/export/{csv|html}`: authorization policy
plus a service check for the HTTP actor, 401/403 without redirects, `no-store`, inline only for images, video and plain
text, every access audited. Importing requires `testresults.manage`; a background service imports new folders every
minute (`TestResults:ScanInterval`) and applies retention: evidence files of runs older than
`TestResults:ArtifactRetention` (90 days) and outside the newest `TestResults:KeepLatestRuns` (30) are deleted and marked
purged, while structured results remain. A new role `QualityAnalyst` (`cases.view`, `testresults.view`,
`testresults.manage`) and demo account `qa@example.test` are added to the E3 matrix.

## Alternatives considered

- An external test-reporting product: a separate service and login, against the single-deployment requirement.
- Starting test runs from the web app: needs the Docker socket or a privileged runner inside the app container, which
  would let a web compromise run arbitrary containers. Runs are started by the documented command instead; the app
  imports them (`testresults.manage`).
- Copying evidence into the database: large binary rows and backups; the results volume plus hashed metadata is simpler.
- Serving the results folder as static files: bypasses authorization and auditing.

## Tradeoffs

The web container mounts the results volume read-write (redacted log copies, retention). Screenshots cannot be redacted
after capture, so the harness masks sensitive inputs and all test data is synthetic. "Initiate a test run" in the UI
means importing a finished run, not executing tests.

## Consequences

New test projects are picked up automatically by `run-tests.sh` and the importer; to link results to stories, give
tests `[Trait("Category", "Story:...")]` / `Req:` / `Priority:` traits and add the two-line catalog initializer.
History survives `docker compose down` and rebuilds; `docker compose down -v` deletes it (and the demo data).
