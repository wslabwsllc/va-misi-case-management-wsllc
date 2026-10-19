using CaseIntake.Contracts.Common;

namespace CaseIntake.TestResults;

/// <summary>Errors returned by the test results service. Messages are safe to show.</summary>
public static class TestResultErrors
{
    public static Error Forbidden { get; } = new(ErrorKind.Forbidden, "Forbidden", "You do not have permission to view or manage test results.");
    public static Error RunNotFound { get; } = new(ErrorKind.NotFound, "TestRunNotFound", "The test run was not found.");
    public static Error ExecutionNotFound { get; } = new(ErrorKind.NotFound, "TestExecutionNotFound", "The test result was not found.");
    public static Error TestCaseNotFound { get; } = new(ErrorKind.NotFound, "TestCaseNotFound", "The test case was not found.");
    public static Error ArtifactNotFound { get; } = new(ErrorKind.NotFound, "TestArtifactNotFound", "The evidence file was not found.");
    public static Error ArtifactPurged { get; } = new(ErrorKind.NotFound, "TestArtifactPurged",
        "This evidence file was removed by the retention policy. The structured results are still available.");
    public static Error UnknownFormat { get; } = new(ErrorKind.Validation, "UnknownFormat", "Choose CSV or HTML.");

    public static Error InvalidQuery(string field, string message) => new(ErrorKind.Validation, "InvalidQuery",
        "The query is not valid.", new Dictionary<string, string[]> { [field] = [message] });
}
