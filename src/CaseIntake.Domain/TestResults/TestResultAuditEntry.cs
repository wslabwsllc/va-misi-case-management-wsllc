namespace CaseIntake.Domain.TestResults;

/// <summary>
/// Who imported runs, viewed or downloaded evidence, exported reports, and what retention removed. Details hold IDs,
/// kinds and counts only, never test output.
/// </summary>
public sealed class TestResultAuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Identity user ID; null for system actions (automatic import, retention).</summary>
    public string? ActorId { get; set; }
    public string Actor { get; set; } = string.Empty;

    /// <summary>RunImported, EvidenceViewed, EvidenceDownloaded, ReportExported or ArtifactsPurged.</summary>
    public string Action { get; set; } = string.Empty;
    public long? RunId { get; set; }
    public Guid? ArtifactId { get; set; }
    public string? Details { get; set; }
}

/// <summary>
/// A row of the read-only <c>TestExecutionHistory</c> view (created by the E5_TestResults migration): every execution
/// with its run and test case, for history and flaky-test queries.
/// </summary>
public sealed class TestExecutionHistoryRow
{
    public long ExecutionId { get; set; }
    public long TestCaseId { get; set; }
    public string TestTitle { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public TestType Type { get; set; }
    public long RunId { get; set; }
    public string RunKey { get; set; } = string.Empty;
    public DateTimeOffset RunStartedAtUtc { get; set; }
    public string? GitCommit { get; set; }
    public string? GitBranch { get; set; }
    public TestOutcome Outcome { get; set; }
    public long DurationMs { get; set; }
}
