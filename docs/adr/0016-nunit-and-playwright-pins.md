# 0016. NUnit 4.4 and Playwright 1.52 for every test suite

* Status: accepted
* Owner: E1
* Supersedes: the framework choice in [0004](0004-test-strategy.md) (xUnit v2); everything else in 0004 stands.

## Context

Review finding F01 (rows R09): the required test framework is NUnit 4.4.x and the browser tests must pin Playwright
1.52.x with a compatible axe integration. The suites used xUnit 2.9.3 and Microsoft.Playwright 1.63.0.

## Decision

- **NUnit 4.4.0** on the VSTest runner through plain `dotnet test` (**NUnit3TestAdapter 5.2.0**), with
  **NUnit.Analyzers 4.10.0** in every test project (warnings are errors). `scripts/run-tests.sh`, the TRX output, the
  catalog and the import into the test results module are unchanged.
- **Microsoft.Playwright 1.52.0**; the Chromium build in the test image follows it. **Deque.AxeCore.Playwright 4.13.0**
  is kept: it requires Microsoft.Playwright 1.20.2 or later, so it is compatible with 1.52.
- xUnit's execution model is kept explicitly, so no test changes meaning: `FixtureLifeCycle(InstancePerTestCase)` (a new
  test-class instance per test) and `Parallelizable(ParallelScope.Fixtures)` (classes in parallel, tests of a class one
  at a time); the browser suite runs one test at a time.
- `tests/CaseIntake.Testing` (not a test project) holds the shared NUnit support:
  - `ClassFixture<TSelf, TFixture>`: one shared fixture per test class, created in a static `[OneTimeSetUp]` and disposed
    in `[OneTimeTearDown]` (the scope of the former class fixtures). The browser is one `[SetUpFixture]` per assembly.
  - `IAsyncLifetime` for fixtures; a test class's own per-test set-up uses `[SetUp]` and `[TearDown]`.
  - `TheoryData<...>` rows for `[TestCaseSource]`.
  - `Assert`: the assertion call shapes the tests were written with (expected first; `Single`, `IsType` and `Throws`
    return what they found), each evaluated with NUnit constraints (`Is`, `Does`, `Has`). A failure throws NUnit's
    `AssertionException`, which NUnit reports as a failed test, but it is not recorded on the test before it is thrown,
    so bUnit's `WaitForAssertion` and other retrying callers can try again without an earlier attempt failing the test.
    `AssertionTests` checks that every assertion fails when it should and that a retried failure is not recorded.
- Test categories are NUnit `[Category]` values (`Story:`, `Req:`, `Priority:`, `A11y:`, `Section508:`, `Wcag:`,
  `Method:`, `Workflow:`); the catalog writers read them from `CategoryAttribute`.

## Alternatives considered

- Rewriting every assertion as `Assert.That(...)`: about 2,900 hand edits with a real risk of changing what a test
  checks (collection versus sequence equality, substring versus element checks), and bUnit retries would record
  failures. The helper keeps each check's meaning and still uses NUnit's constraint engine and failure reporting.
- Keeping xUnit and recording a deviation: rejected by the reviewer's requirement.

## Consequences

Test counts are unchanged by the move (each `[Fact]` is a `[Test]`, each `[InlineData]` a `[TestCase]`). New tests use
`[Test]`/`[TestCase]` and the shared `Assert`. Playwright APIs newer than 1.52 must not be used in the browser suite.
