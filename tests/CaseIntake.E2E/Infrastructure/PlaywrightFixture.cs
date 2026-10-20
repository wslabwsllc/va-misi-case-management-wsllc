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

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "E2E browser";
}

/// <summary>A fact that is skipped (with the reason shown in the results) when the synthetic-run staging folder is absent.</summary>
public sealed class FixturesFactAttribute : FactAttribute
{
    public FixturesFactAttribute()
    {
        if (E2ESettings.FixturesRoot is null)
        {
            Skip = "E2E_FIXTURES_ROOT is not set: the test results module tests need the web-e2e results folder (set by docker compose).";
        }
    }
}
