using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaseIntake.TestResults.Import;

/// <summary>
/// <c>runs/&lt;runKey&gt;/run.json</c>, written by <c>scripts/run-tests.sh</c> for every run, including one whose suites
/// failed to start. Schema version 1.
/// </summary>
public sealed class RunManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string RunKey { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string Runner { get; set; } = "scripts/run-tests.sh";

    /// <summary>Completed, Partial or FailedToStart as reported by the runner.</summary>
    public string Status { get; set; } = "Completed";
    public RunGit? Git { get; set; }
    public string? ImageVersion { get; set; }
    public RunEnvironment? Environment { get; set; }
    public List<RunSuite> Suites { get; set; } = [];

    /// <summary>Server log files (relative to the run folder) captured by the host script, if any.</summary>
    public List<string> ServerLogs { get; set; } = [];
    public string? Notes { get; set; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };
}

public sealed class RunGit
{
    public string? Branch { get; set; }
    public string? Commit { get; set; }
    public string? BuildId { get; set; }
}

public sealed class RunEnvironment
{
    public string Name { get; set; } = "unknown";
    public string Os { get; set; } = "unknown";
    public string Runtime { get; set; } = "unknown";
    public string? BaseUrl { get; set; }
}

public sealed class RunSuite
{
    /// <summary>The test project name, for example CaseIntake.Web.Tests.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unit, Component, Integration or EndToEnd.</summary>
    public string Type { get; set; } = "Unit";

    /// <summary>The TRX file name relative to the run folder, when one was produced.</summary>
    public string? Trx { get; set; }
    public int? ExitCode { get; set; }

    /// <summary>Completed, NoResults or FailedToStart.</summary>
    public string Status { get; set; } = "Completed";
    public string? Message { get; set; }
}

/// <summary>
/// <c>evidence/&lt;slug&gt;/evidence.json</c>, written by the Playwright harness for every end-to-end test: metadata,
/// recorded steps, browser details, console and network problems, and the evidence files next to it.
/// </summary>
public sealed class EvidenceManifest
{
    public string FullName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Story { get; set; }
    public List<string> Requirements { get; set; } = [];
    public string? AcceptanceCriteria { get; set; }
    public string? Priority { get; set; }
    public string? Description { get; set; }
    public string? Preconditions { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string? Browser { get; set; }
    public string? Viewport { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string? Outcome { get; set; }
    public List<EvidenceStep> Steps { get; set; } = [];
    public List<EvidenceFile> Artifacts { get; set; } = [];
    public List<string> ConsoleErrors { get; set; } = [];
    public List<string> NetworkFailures { get; set; } = [];

    [JsonIgnore]
    public string FolderRelativePath { get; set; } = string.Empty;
}

public sealed class EvidenceStep
{
    public string Name { get; set; } = string.Empty;
    public string Outcome { get; set; } = "Passed";
    public long DurationMs { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string? Message { get; set; }
}

public sealed class EvidenceFile
{
    /// <summary>Screenshot, Trace, Video, ConsoleLog or NetworkLog.</summary>
    public string Kind { get; set; } = "Screenshot";

    /// <summary>File name inside the evidence folder (no directories).</summary>
    public string File { get; set; } = string.Empty;
    public string? Caption { get; set; }
}

/// <summary>
/// One manual test (keyboard, screen reader, visual) from a run's <c>manual/*.json</c> (copied from
/// <c>tests/accessibility/</c> by scripts/run-tests.sh). Its metadata classifies it explicitly; <see cref="Result"/>
/// records an execution. Without a complete result the test is imported as Not Run: a verdict needs who ran it, when,
/// and what they observed, so no passing result is ever created without execution evidence.
/// </summary>
public sealed class ManualTestEntry
{
    /// <summary>Stable ID, for example <c>A11Y-KB-001</c>; the test's identity across runs.</summary>
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Page { get; set; }
    public string? Workflow { get; set; }

    /// <summary>Section508 or Wcag22 (anything else leaves the test unclassified).</summary>
    public string? Classification { get; set; }
    public string? Section508Provision { get; set; }
    public string? WcagVersion { get; set; }
    public string? WcagCriterion { get; set; }
    public string? WcagLevel { get; set; }

    /// <summary>ManualKeyboard, ScreenReader or VisualOther.</summary>
    public string? Method { get; set; }
    public string? Preconditions { get; set; }
    public string? Steps { get; set; }
    public string? Expected { get; set; }
    public string? Priority { get; set; }
    public List<string> Requirements { get; set; } = [];
    public ManualTestResult? Result { get; set; }
}

/// <summary>A recorded manual execution. Passed, Failed, Blocked and NotApplicable need Tester, ExecutedAtUtc and Actual.</summary>
public sealed class ManualTestResult
{
    public string Outcome { get; set; } = "NotExecuted";
    public DateTimeOffset? ExecutedAtUtc { get; set; }
    public string? Tester { get; set; }
    public string? Browser { get; set; }
    public string? AssistiveTechnology { get; set; }
    public string? Actual { get; set; }
    public string? DefectReference { get; set; }
    public string? RemediationNotes { get; set; }

    /// <summary>Evidence files next to the JSON file (no directories).</summary>
    public List<EvidenceFile> Evidence { get; set; } = [];
}
