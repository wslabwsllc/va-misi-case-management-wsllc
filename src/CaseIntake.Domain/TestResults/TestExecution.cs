namespace CaseIntake.Domain.TestResults;

/// <summary>
/// The result of one test case in one run (at most one per run and test). Stored text is redacted and capped by the
/// importer; browser fields, the actual result, steps and logs come from the E2E evidence manifest when there is one.
/// </summary>
public sealed class TestExecution
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public TestRun Run { get; set; } = null!;
    public long TestCaseId { get; set; }
    public TestCase TestCase { get; set; } = null!;
    public TestOutcome Outcome { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public long DurationMs { get; set; }
    public string? Browser { get; set; }
    public string? Viewport { get; set; }
    public string? ActualResult { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? StandardOutput { get; set; }
    public string? ConsoleLog { get; set; }
    public string? NetworkLog { get; set; }

    // Execution evidence for manual and accessibility checks (QA 508 enhancement).
    /// <summary>Who executed the test (manual results), or the runner for automated ones.</summary>
    public string? Tester { get; set; }

    /// <summary>Assistive technology and version, for example <c>NVDA 2024.4</c>.</summary>
    public string? AssistiveTechnology { get; set; }

    /// <summary>Defect tracker reference for a failure.</summary>
    public string? DefectReference { get; set; }

    public string? RemediationNotes { get; set; }

    public ICollection<TestStepResult> Steps { get; } = new List<TestStepResult>();
    public ICollection<TestArtifact> Artifacts { get; } = new List<TestArtifact>();
}

/// <summary>One recorded step of an E2E test, in <see cref="Order"/> (1-based, unique per execution).</summary>
public sealed class TestStepResult
{
    public long Id { get; set; }
    public long ExecutionId { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public TestOutcome Outcome { get; set; }
    public long DurationMs { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// An evidence file (screenshot, trace, video, log or result file) referenced in place under the results root at
/// <see cref="RelativePath"/>. Run-level files have no <see cref="ExecutionId"/>. Retention deletes the file and sets
/// <see cref="Purged"/>; the row (and the structured results) stay.
/// </summary>
public sealed class TestArtifact
{
    public Guid Id { get; set; }
    public long RunId { get; set; }
    public TestRun Run { get; set; } = null!;
    public long? ExecutionId { get; set; }
    public TestArtifactKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the file as lowercase hex, computed at import.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Path relative to the results root, with forward slashes.</summary>
    public string RelativePath { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool Purged { get; set; }
    public DateTimeOffset? PurgedAtUtc { get; set; }
}
