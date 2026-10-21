using CaseIntake.E2E.Infrastructure;

namespace CaseIntake.E2E;

/// <summary>
/// Starts the browser once before the first E2E test of the assembly and closes it after the last. NUnit applies a
/// set-up fixture to its own namespace and below, so it sits in the root namespace of the tests.
/// </summary>
[SetUpFixture]
public sealed class E2EAssemblySetup
{
    private static PlaywrightFixture? s_fixture;

    public static PlaywrightFixture Fixture => s_fixture ?? throw new InvalidOperationException("The browser is not started.");

    [OneTimeSetUp]
    public async Task StartBrowserAsync()
    {
        var fixture = new PlaywrightFixture();
        await fixture.InitializeAsync();
        s_fixture = fixture;
    }

    [OneTimeTearDown]
    public async Task StopBrowserAsync()
    {
        if (s_fixture is { } fixture)
        {
            s_fixture = null;
            await fixture.DisposeAsync();
        }
    }
}
