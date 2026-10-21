# 0004. Test strategy and the `tests` service

* Status: accepted; framework superseded by [0016](0016-nunit-and-playwright-pins.md) (NUnit 4.4, Playwright 1.52)
* Owner: E1

## Context

`docker compose run --rm tests` must run every suite, propagate failures, and need no restore, build or download at
run time.

## Decision

xUnit v2 on the VSTest runner via plain `dotnet test` (no Microsoft.Testing.Platform settings in `global.json`).
The evaluation image is built once (Release, including tests) and shared by `web`, `migrator` and `tests`. The `tests`
service (Compose profile `tests`) runs `scripts/run-tests.sh`, which runs each `IsTestProject` with `--no-build
--no-restore`, writes one TRX per project, and fails on: a test project missing from the solution, no result file,
zero discovered tests, or any failing test. Each fixture creates its own uniquely named database on the `db`
service (`TESTKIT_POSTGRES_ADMIN`) and drops it on disposal; without that variable locally, a Testcontainers
PostgreSQL 18 is started. Demo data is never touched. `CaseIntake.TestKit` provides the fixtures, `CaseIntakeWebFactory`,
header-driven `TestAuthHandler`, builders and in-memory fakes of every contract (`AddFakeModules()`, test-only).
Dependency rules are enforced by an architecture test over the csproj graph plus NetArchTest on assemblies.

## Alternatives considered

- Microsoft.Testing.Platform: different flags and discovery; mixing runners is error-prone.
- One shared database reset between classes: unsafe with concurrent classes.

## Tradeoffs

Release-only no-build execution requires the image to be rebuilt after code changes. The Playwright browsers are
added to the image when the first E2E test exists (E5-S9).

## Consequences

New test projects must be added to `CaseIntake.sln` and contain real tests, or the wrapper fails.
