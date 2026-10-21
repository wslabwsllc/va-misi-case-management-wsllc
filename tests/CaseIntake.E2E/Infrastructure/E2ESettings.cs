namespace CaseIntake.E2E.Infrastructure;

/// <summary>Where the suite points and where it writes evidence. Every value has a default for a local Compose stack.</summary>
public static class E2ESettings
{
    /// <summary>The application under test. Compose sets http://web-e2e:8080 (the production image, isolated database).</summary>
    public static string BaseUrl { get; } = (Environment.GetEnvironmentVariable("E2E_BASE_URL") is { Length: > 0 } url ? url : "http://localhost:8080").TrimEnd('/');

    /// <summary>The run folder (scripts/run-tests.sh sets TEST_RUN_DIR); evidence goes to its evidence/ subfolder.</summary>
    public static string RunDirectory { get; } = Environment.GetEnvironmentVariable("TEST_RUN_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(AppContext.BaseDirectory, "e2e-results");

    public static string EvidenceDirectory => Path.Combine(RunDirectory, "evidence");

    /// <summary>The results root the web-e2e instance imports from (used to stage a synthetic run for the module tests).</summary>
    public static string? FixturesRoot { get; } = Environment.GetEnvironmentVariable("E2E_FIXTURES_ROOT") is { Length: > 0 } root ? root : null;

    public static string DemoPassword { get; } = Environment.GetEnvironmentVariable("E2E_DEMO_PASSWORD") is { Length: > 0 } p ? p : "Demo-Passw0rd!";

    /// <summary>"always" keeps traces of passing tests too (default: only failures).</summary>
    public static bool TraceAlways { get; } = string.Equals(Environment.GetEnvironmentVariable("E2E_TRACE"), "always", StringComparison.OrdinalIgnoreCase);

    /// <summary>Records a video of every test when set to 1.</summary>
    public static bool RecordVideo { get; } = Environment.GetEnvironmentVariable("E2E_VIDEO") == "1";

    /// <summary>
    /// Limit for every Playwright action and every <c>Expect(...)</c> assertion (Playwright's own assertion default is 5 s).
    /// Under linux/amd64 emulation on Apple Silicon a sign-in redirect and first render can take longer than 5 s.
    /// </summary>
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(15);
}

/// <summary>The synthetic demo accounts seeded by the migrator (README).</summary>
public static class DemoAccounts
{
    public const string CaseWorker = "caseworker@example.test";
    public const string Supervisor = "supervisor@example.test";
    public const string QualityAnalyst = "qa@example.test";
    public const string IntakeClerk = "intake@example.test";
    public const string Auditor = "auditor@example.test";
    public const string Administrator = "admin@example.test";
}

/// <summary>Viewports required by the responsive tests.</summary>
public static class Viewports
{
    public static readonly ViewportSize Phone390 = new() { Width = 390, Height = 844 };
    public static readonly ViewportSize Phone375 = new() { Width = 375, Height = 812 };
    public static readonly ViewportSize Tablet768 = new() { Width = 768, Height = 1024 };
    public static readonly ViewportSize Desktop1440 = new() { Width = 1440, Height = 900 };

    /// <summary>WCAG 1.4.10 reflow width: 320 CSS px, which is 1280 px at 400% zoom.</summary>
    public static readonly ViewportSize Reflow320 = new() { Width = 320, Height = 640 };

    public static string Label(ViewportSize v) => $"{v.Width}x{v.Height}";
}
