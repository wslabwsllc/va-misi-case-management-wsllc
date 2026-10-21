using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace CaseIntake.E2E.Infrastructure;

/// <summary>One headless Chromium for the whole suite (tests run one at a time, each in its own browser context).</summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public string BrowserLabel => $"chromium {Browser.Version.Split('.')[0]}";

    public async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        Playwright.Selectors.SetTestIdAttribute("data-testid");
        // Assertions wait as long as actions (E2ESession sets the action timeout on each browser context).
        Assertions.SetDefaultExpectTimeout((float)E2ESettings.ActionTimeout.TotalMilliseconds);
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        Playwright.Dispose();
    }
}

/// <summary>
/// Marks a test as ignored (with the reason shown in the results) when the synthetic-run staging folder is absent.
/// Use with <c>[Test]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FixturesRequiredAttribute : NUnitAttribute, IApplyToTest
{
    public void ApplyToTest(NUnit.Framework.Internal.Test test)
    {
        if (E2ESettings.FixturesRoot is null && test.RunState == RunState.Runnable)
        {
            test.RunState = RunState.Ignored;
            test.Properties.Set(PropertyNames.SkipReason,
                "E2E_FIXTURES_ROOT is not set: the test results module tests need the web-e2e results folder (set by docker compose).");
        }
    }
}
