using Microsoft.Extensions.Options;

namespace CaseIntake.TestResults;

/// <summary>
/// Bound from the <c>TestResults</c> configuration section. The results root is the persistent, access-controlled
/// artifact store: the <c>tests</c> service writes one folder per run under <c>runs/</c>, and the web app imports them and
/// serves evidence only through the authorized endpoint (never as static files).
/// </summary>
public sealed class TestResultsOptions
{
    public const string SectionName = "TestResults";

    /// <summary>Compose mounts the <c>testresults</c> volume at <c>/test-results</c>. Unset: <c>&lt;temp&gt;/caseintake-test-results</c>.</summary>
    public string RootPath { get; set; } = string.Empty;

    public static string DefaultRootPath { get; } = Path.Combine(Path.GetTempPath(), "caseintake-test-results");

    /// <summary>How often new run folders are imported automatically; zero disables the background import.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Evidence files of runs older than this are removed (results stay); zero keeps everything.</summary>
    public TimeSpan ArtifactRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>The newest runs whose evidence is always kept, regardless of age.</summary>
    public int KeepLatestRuns { get; set; } = 30;

    /// <summary>Largest single evidence file accepted at import (bigger files are recorded as skipped).</summary>
    public long MaxArtifactBytes { get; set; } = 200L * 1024 * 1024;

    public string RunsPath => Path.Combine(RootPath, "runs");
}

internal sealed class TestResultsOptionsSetup : IPostConfigureOptions<TestResultsOptions>, IValidateOptions<TestResultsOptions>
{
    public void PostConfigure(string? name, TestResultsOptions options)
    {
        options.RootPath = string.IsNullOrWhiteSpace(options.RootPath)
            ? TestResultsOptions.DefaultRootPath
            : Path.GetFullPath(options.RootPath);
    }

    public ValidateOptionsResult Validate(string? name, TestResultsOptions options)
    {
        List<string> failures = [];
        if (options.ScanInterval < TimeSpan.Zero)
        {
            failures.Add("TestResults:ScanInterval cannot be negative.");
        }
        if (options.ArtifactRetention < TimeSpan.Zero)
        {
            failures.Add("TestResults:ArtifactRetention cannot be negative.");
        }
        if (options.KeepLatestRuns < 1)
        {
            failures.Add("TestResults:KeepLatestRuns must be at least 1.");
        }
        if (options.MaxArtifactBytes <= 0)
        {
            failures.Add("TestResults:MaxArtifactBytes must be positive.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
