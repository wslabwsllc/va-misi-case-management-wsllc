using CaseIntake.TestResults;
using Microsoft.Extensions.Options;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>E5: the TestResults section has safe defaults, a resolved root and is validated at startup.</summary>
[Trait("Category", "Story:E5-S7")]
public class TestResultsOptionsTests
{
    [Fact]
    public void Defaults_scan_every_minute_and_keep_ninety_days_and_thirty_runs()
    {
        var options = new TestResultsOptions();

        Assert.Equal("TestResults", TestResultsOptions.SectionName);
        Assert.Equal(TimeSpan.FromMinutes(1), options.ScanInterval);
        Assert.Equal(TimeSpan.FromDays(90), options.ArtifactRetention);
        Assert.Equal(30, options.KeepLatestRuns);
        Assert.Equal(200L * 1024 * 1024, options.MaxArtifactBytes);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "caseintake-test-results"), TestResultsOptions.DefaultRootPath);
        Assert.True(new TestResultsOptionsSetup().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_root_falls_back_to_the_temporary_default(string root)
    {
        var options = new TestResultsOptions { RootPath = root };

        new TestResultsOptionsSetup().PostConfigure(null, options);

        Assert.Equal(TestResultsOptions.DefaultRootPath, options.RootPath);
        Assert.Equal(Path.Combine(TestResultsOptions.DefaultRootPath, "runs"), options.RunsPath);
    }

    [Fact]
    public void A_relative_root_is_made_absolute()
    {
        var options = new TestResultsOptions { RootPath = Path.Combine("relative", "..", "results") };

        new TestResultsOptionsSetup().PostConfigure(null, options);

        Assert.Equal(Path.GetFullPath("results"), options.RootPath);
    }

    [Fact]
    public void Every_invalid_setting_is_reported_together()
    {
        var options = new TestResultsOptions
        {
            ScanInterval = TimeSpan.FromSeconds(-1),
            ArtifactRetention = TimeSpan.FromDays(-1),
            KeepLatestRuns = 0,
            MaxArtifactBytes = 0,
        };

        var result = new TestResultsOptionsSetup().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
        [
            "TestResults:ScanInterval cannot be negative.",
            "TestResults:ArtifactRetention cannot be negative.",
            "TestResults:KeepLatestRuns must be at least 1.",
            "TestResults:MaxArtifactBytes must be positive.",
        ], result.Failures);
    }

    [Fact]
    public void Zero_scan_interval_and_retention_are_valid_and_mean_disabled()
    {
        var result = new TestResultsOptionsSetup().Validate(null,
            new TestResultsOptions { ScanInterval = TimeSpan.Zero, ArtifactRetention = TimeSpan.Zero });

        Assert.Same(ValidateOptionsResult.Success, result);
    }
}
