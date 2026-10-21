// A new test-class instance per test; the browser suite runs one test at a time (it shares one browser and the
// Docker-hosted application), as it did before the move to NUnit.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]
