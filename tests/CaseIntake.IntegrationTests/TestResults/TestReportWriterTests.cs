using System.Text;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.TestResults.Reports;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>E5: run exports are a formula-safe CSV and a self-contained, encoded HTML report.</summary>
[Trait("Category", "Story:E5-S6")]
public class TestReportWriterTests
{
    private static readonly DateTimeOffset Started = TestResultsTestHarness.StartTime;

    private static TestRunSummaryDto Run(string key = "run-1", TestOutcomeCounts? counts = null, DateTimeOffset? finished = null) =>
        new(1, key, TestRunStatus.Partial, Started, finished, TimeSpan.FromSeconds(3725), "docker compose tests", null, null, null, null,
            "scripts/run-tests.sh", counts ?? new TestOutcomeCounts(4, 2, 1, 0, 0, 1), Started.AddMinutes(5), null);

    private static TestExecutionRowDto Row(long id, string title, TestOutcome outcome, string module = "Web.Tests", string? message = null,
        string? browser = null, string? viewport = null) =>
        new(id, 1, id, title, "Ns." + title, module, TestType.Unit, outcome, TimeSpan.FromMilliseconds(1234.9), browser, viewport, message, 0);

    [Fact]
    public void Csv_has_a_bom_a_header_and_one_quoted_row_per_result()
    {
        var file = TestReportWriter.Csv(Run(), [Row(1, "Shows \"quoted\" title", TestOutcome.Passed, browser: "chromium", viewport: "1440x900"),
            Row(2, "Fails", TestOutcome.Failed, message: "line1\r\nline2")]);

        Assert.Equal("test-run-run-1.csv", file.FileName);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Content[..3]);
        var lines = Encoding.UTF8.GetString(file.Content[3..]).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("RunKey,Module,Type,Test,Outcome,DurationMs,Browser,Viewport,Message", lines[0]);
        Assert.Equal("\"run-1\",\"Web.Tests\",Unit,\"Shows \"\"quoted\"\" title\",Passed,1234,\"chromium\",\"1440x900\",\"\"", lines[1]);
        Assert.Equal("\"run-1\",\"Web.Tests\",Unit,\"Fails\",Failed,1234,\"\",\"\",\"line1  line2\"", lines[2]);
        Assert.Equal(3, lines.Length);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tx", "\"'\tx\"")]
    [InlineData("plain", "\"plain\"")]
    [InlineData("", "\"\"")]
    public void Csv_cells_neutralize_leading_formula_characters(string value, string expected)
    {
        Assert.Equal(expected, TestReportWriter.Cell(value));
    }

    [Fact]
    public void Html_report_for_a_first_run_encodes_text_and_lists_failures_first()
    {
        var environment = new TestEnvironment { Name = "docker <compose>", OperatingSystem = "Linux", Runtime = "10.0" };
        var suites = new List<TestSuiteDto>
        {
            new("CaseIntake.Web.Tests", TestType.Component, "Completed", 3, null),
            new("CaseIntake.E2E", TestType.EndToEnd, "FailedToStart", 0, "App <not> reachable"),
        };
        var run = Run("<script>alert(1)</script>");
        var comparison = new TestRunComparisonDto(run, null, [], [], [], [], []);
        var rows = new List<TestExecutionRowDto>
        {
            Row(1, "A passes", TestOutcome.Passed),
            Row(2, "Z fails", TestOutcome.Failed, message: "<b>boom</b>"),
            Row(3, "M not run", TestOutcome.NotExecuted),
        };

        var file = TestReportWriter.Html(run, environment, suites, comparison, rows, Started.AddHours(1));
        var html = Encoding.UTF8.GetString(file.Content);

        Assert.Equal("test-run-<script>alert(1)</script>.html", file.FileName);
        Assert.Equal("text/html; charset=utf-8", file.ContentType);
        Assert.StartsWith("<!DOCTYPE html><html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("Test run &lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Generated 2026-10-16 16:00:00 UTC", html, StringComparison.Ordinal);
        Assert.Contains("<dt>Finished</dt><dd> UTC</dd>", html, StringComparison.Ordinal);   // a run without a finish time
        Assert.Contains("<dt>Duration</dt><dd>01:02:05</dd>", html, StringComparison.Ordinal);
        Assert.Contains("docker &lt;compose&gt; (Linux, 10.0)", html, StringComparison.Ordinal);
        Assert.Contains("unknown / unknown", html, StringComparison.Ordinal);
        Assert.Contains("4 total: 2 passed, 1 failed, 0 skipped, 0 blocked, 1 not executed (66.7% pass rate)", html, StringComparison.Ordinal);
        Assert.Contains("<td>CaseIntake.E2E</td><td>EndToEnd</td><td>FailedToStart</td><td>0</td><td>App &lt;not&gt; reachable</td>", html, StringComparison.Ordinal);
        Assert.Contains("This is the first imported run.", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"NotExecuted\">Not executed</td>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;boom&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("M not run", StringComparison.Ordinal) < html.IndexOf("A passes", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Z fails", StringComparison.Ordinal) < html.IndexOf("A passes", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_report_compares_with_the_previous_run_and_omits_a_pass_rate_when_nothing_executed()
    {
        var environment = new TestEnvironment { Name = "ci", OperatingSystem = "Linux", Runtime = "10.0" };
        var run = Run("run-2", new TestOutcomeCounts(2, 0, 0, 1, 0, 1), Started.AddMinutes(2)) with { GitBranch = "main", GitCommit = "abc", BuildId = "b7", ImageVersion = "img:1" };
        var previous = Run("run-1");
        var comparison = new TestRunComparisonDto(run, previous,
            NewlyFailed: [Row(1, "Newly <broken>", TestOutcome.Failed)],
            NewlyFixed: [],
            StillFailing: [Row(2, "Still broken", TestOutcome.Failed), Row(3, "Also broken", TestOutcome.Failed)],
            NewTests: [], RemovedTests: []);

        var html = Encoding.UTF8.GetString(TestReportWriter.Html(run, environment, [], comparison, [], Started).Content);

        Assert.Contains("Previous run: run-1. Newly failed: 1. Newly fixed: 0. Still failing: 2.", html, StringComparison.Ordinal);
        Assert.Contains("<h3>Newly failed</h3><ul><li>Newly &lt;broken&gt;</li></ul>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h3>Newly fixed</h3>", html, StringComparison.Ordinal);
        Assert.Contains("main / abc", html, StringComparison.Ordinal);
        Assert.Contains("b7 · image img:1", html, StringComparison.Ordinal);
        Assert.Contains("1 not executed</dd>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("pass rate", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TestOutcome.NotExecuted, "Not executed")]
    [InlineData(TestOutcome.Blocked, "Blocked")]
    [InlineData(TestOutcome.Passed, "Passed")]
    public void Outcome_labels_are_readable(TestOutcome outcome, string label)
    {
        Assert.Equal(label, TestReportWriter.Outcome(outcome));
    }
}
