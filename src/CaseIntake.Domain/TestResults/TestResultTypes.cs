namespace CaseIntake.Domain.TestResults;

/// <summary>
/// The outcome of one test in one run. Stored as an integer; never renumber. NotExecuted (the suite did not start or
/// produced no results) is kept apart from Skipped (a deliberate skip), so a run that failed to execute never reads as
/// a passing one. The UI shows NotExecuted as "Not Run": a test that was never executed is never reported as passed.
/// </summary>
public enum TestOutcome
{
    Passed = 0,
    Failed = 1,
    Skipped = 2,
    Blocked = 3,
    NotExecuted = 4,

    /// <summary>The check does not apply to this page or workflow (recorded by a tester; never inferred).</summary>
    NotApplicable = 5,
}

/// <summary>Whether every suite of a run produced results. Stored as an integer; never renumber.</summary>
public enum TestRunStatus
{
    Completed = 0,
    Partial = 1,
    FailedToStart = 2,
}

/// <summary>The kind of test suite a test belongs to (one per test project). Stored as an integer; never renumber.</summary>
public enum TestType
{
    Unit = 0,
    Component = 1,
    Integration = 2,
    EndToEnd = 3,

    /// <summary>A manual check (keyboard, screen reader, visual) recorded by a tester (QA 508 enhancement).</summary>
    Manual = 4,
}

/// <summary>What an evidence file is. Stored as an integer; never renumber.</summary>
public enum TestArtifactKind
{
    Screenshot = 0,
    Trace = 1,
    Video = 2,
    ConsoleLog = 3,
    NetworkLog = 4,
    ResultFile = 5,
    ServerLog = 6,
}

/// <summary>Column limits shared by the EF mapping and the importer (which caps imported text to them).</summary>
public static class TestResultFieldLimits
{
    /// <summary>Names, branches, build IDs, environment fields, actors and modules.</summary>
    public const int ShortTextMaxLength = 200;

    /// <summary>Test titles, step names, file names, captions and requirement lists.</summary>
    public const int NameMaxLength = 400;

    /// <summary>Fully qualified test names (with theory arguments).</summary>
    public const int FullNameMaxLength = 2000;

    /// <summary>Failure messages, expected/actual results, descriptions and notes.</summary>
    public const int MessageMaxLength = 8000;

    /// <summary>Stack traces, standard output, console and network logs.</summary>
    public const int LongTextMaxLength = 64000;

    /// <summary>SHA-256 as lowercase hex (test case keys, evidence hashes).</summary>
    public const int Sha256HexLength = 64;

    public const int RunKeyMaxLength = 128;
    public const int EnvironmentKeyMaxLength = 128;
    public const int GitCommitMaxLength = 64;
    public const int BrowserMaxLength = 64;
    public const int PriorityMaxLength = 32;
    public const int AuditActionMaxLength = 64;
    public const int PathMaxLength = 1000;
}

/// <summary>
/// How a test case is classified for accessibility reporting. Kept apart from the result and from the standards mapping:
/// the classification is set only from the test's own metadata (never inferred from its name or module), and older
/// records stay unclassified (null). Stored as an integer; never renumber.
/// </summary>
public enum AccessibilityClassification
{
    /// <summary>Part of the Revised Section 508 baseline (36 CFR 1194, which incorporates WCAG 2.0 Level A and AA).</summary>
    Section508 = 0,

    /// <summary>A WCAG 2.2 Level AA project check beyond the Section 508 baseline.</summary>
    Wcag22Project = 1,
}

/// <summary>How a test is carried out. Stored as an integer; never renumber.</summary>
public enum TestMethod
{
    Automated = 0,
    ManualKeyboard = 1,
    ScreenReader = 2,
    VisualOther = 3,
}

/// <summary>The Section 508 filter of the QA test results pages.</summary>
public enum Section508Filter
{
    /// <summary>Every test (the default).</summary>
    All = 0,

    /// <summary>Only tests classified <see cref="AccessibilityClassification.Section508"/>.</summary>
    Section508Only = 1,

    /// <summary>Every test except those classified <see cref="AccessibilityClassification.Section508"/> (unclassified included).</summary>
    ExcludeSection508 = 2,
}
