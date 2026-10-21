namespace CaseIntake.Domain.TestResults;

/// <summary>
/// A test known to the results store, identified across runs by <see cref="Key"/> (SHA-256 of its fully qualified
/// name). The descriptive fields come from the E2E evidence manifest or the test's Story/Req/Priority traits and are
/// refreshed on every import; <see cref="LastOutcome"/> follows the newest run by start time.
/// </summary>
public sealed class TestCase
{
    public long Id { get; set; }

    /// <summary>SHA-256 of <see cref="FullName"/> as 64 lowercase hex digits.</summary>
    public string Key { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>The test project without the <c>CaseIntake.</c> prefix, for example <c>Web.Tests</c> or <c>E2E</c>.</summary>
    public string Module { get; set; } = string.Empty;
    public TestType Type { get; set; }
    public string? Description { get; set; }
    public string? Story { get; set; }

    /// <summary>Comma-separated requirement IDs.</summary>
    public string? Requirements { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? Priority { get; set; }
    public string? Preconditions { get; set; }
    public string? ExpectedResult { get; set; }
    public TestOutcome? LastOutcome { get; set; }
    public long? LastRunId { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; set; }

    // Accessibility metadata (QA 508 enhancement). Classification, standards mapping and method are separate fields,
    // filled only from the test's own catalog entry or traits; older records stay null (unclassified).
    public AccessibilityClassification? Accessibility { get; set; }

    /// <summary>Revised Section 508 provision, for example <c>E205.4</c> or <c>502.3.1</c>.</summary>
    public string? Section508Provision { get; set; }

    /// <summary>WCAG version, for example <c>2.0</c> or <c>2.2</c>.</summary>
    public string? WcagVersion { get; set; }

    /// <summary>WCAG success criterion number, for example <c>2.4.7</c>.</summary>
    public string? WcagCriterion { get; set; }

    /// <summary>WCAG conformance level: <c>A</c>, <c>AA</c> or <c>AAA</c>.</summary>
    public string? WcagLevel { get; set; }

    public TestMethod? Method { get; set; }

    /// <summary>The workflow the test covers, for example <c>Case intake</c>.</summary>
    public string? Workflow { get; set; }
}
