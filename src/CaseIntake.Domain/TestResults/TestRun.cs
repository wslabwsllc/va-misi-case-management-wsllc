namespace CaseIntake.Domain.TestResults;

/// <summary>
/// One imported execution of the test suites (one <c>runs/&lt;runKey&gt;</c> folder). Imported once and never changed;
/// the counts are derived from <see cref="Executions"/> at import. <see cref="SuitesJson"/> keeps the per-suite summary
/// (name, type, status, test count, message) as reported by the runner.
/// </summary>
public sealed class TestRun
{
    public long Id { get; set; }
    public string RunKey { get; set; } = string.Empty;
    public TestRunStatus Status { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public long DurationMs { get; set; }
    public string Runner { get; set; } = string.Empty;
    public string? GitBranch { get; set; }
    public string? GitCommit { get; set; }
    public string? BuildId { get; set; }
    public string? ImageVersion { get; set; }

    public long EnvironmentId { get; set; }
    public TestEnvironment Environment { get; set; } = null!;

    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Blocked { get; set; }
    public int NotExecuted { get; set; }

    /// <summary>Recorded Not Applicable results (manual accessibility checks).</summary>
    public int NotApplicable { get; set; }

    public string SuitesJson { get; set; } = "[]";
    public string? Notes { get; set; }

    /// <summary>The run folder relative to the results root (<c>runs/&lt;runKey&gt;</c>).</summary>
    public string SourcePath { get; set; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ImportedBy { get; set; } = string.Empty;

    public ICollection<TestExecution> Executions { get; } = new List<TestExecution>();
}

/// <summary>Where a run executed. Shared by runs with the same name, OS, runtime and base URL (<see cref="Key"/>).</summary>
public sealed class TestEnvironment
{
    public long Id { get; set; }

    /// <summary>SHA-256 (hex) of name, OS, runtime and base URL.</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Runtime { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
