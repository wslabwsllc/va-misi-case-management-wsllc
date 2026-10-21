// xUnit's execution model, kept after the move to NUnit: a new instance of the test class for every test (so fields
// never leak between tests), and test classes running in parallel with each other while the tests of one class run
// one at a time.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[assembly: Parallelizable(ParallelScope.Fixtures)]
