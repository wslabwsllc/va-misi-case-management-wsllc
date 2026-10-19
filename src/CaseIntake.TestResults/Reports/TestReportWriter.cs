using System.Globalization;
using System.Net;
using System.Text;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;

namespace CaseIntake.TestResults.Reports;

/// <summary>Run exports: a CSV of every result and a self-contained, accessible HTML report (summary, comparison, results).</summary>
internal static class TestReportWriter
{
    public static TestReportFile Csv(TestRunSummaryDto run, IReadOnlyList<TestExecutionRowDto> rows)
    {
        var csv = new StringBuilder();
        csv.AppendLine("RunKey,Module,Type,Test,Outcome,DurationMs,Browser,Viewport,Message");
        foreach (var r in rows)
        {
            csv.Append(Cell(run.RunKey)).Append(',').Append(Cell(r.Module)).Append(',').Append(r.Type).Append(',')
               .Append(Cell(r.TestTitle)).Append(',').Append(r.Outcome).Append(',')
               .Append(((long)r.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(Cell(r.Browser ?? string.Empty)).Append(',').Append(Cell(r.Viewport ?? string.Empty)).Append(',')
               .Append(Cell(r.FailureMessage ?? string.Empty)).AppendLine();
        }
        return new TestReportFile(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            $"test-run-{run.RunKey}.csv", "text/csv; charset=utf-8");
    }

    public static TestReportFile Html(TestRunSummaryDto run, TestEnvironment environment, IReadOnlyList<TestSuiteDto> suites,
        TestRunComparisonDto comparison, IReadOnlyList<TestExecutionRowDto> rows, DateTimeOffset generatedAtUtc)
    {
        static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        static string T(DateTimeOffset? value) => value?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
        var c = run.Counts;
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>Test run ").Append(E(run.RunKey)).Append(" - Case Intake and Triage</title><style>")
            .Append("body{font-family:'Segoe UI',system-ui,sans-serif;color:#242424;margin:24px;line-height:1.45}h1{font-size:22px}h2{font-size:17px;margin-top:28px}")
            .Append("table{border-collapse:collapse;width:100%;font-size:14px}th,td{border-bottom:1px solid #d1d1d1;padding:6px 8px;text-align:left;vertical-align:top}")
            .Append("th{background:#f5f5f5}.Passed{color:#0b5a0b}.Failed{color:#b10e1c;font-weight:600}.NotExecuted,.Blocked{color:#6b3a00;font-weight:600}")
            .Append("dl{display:grid;grid-template-columns:max-content 1fr;gap:4px 16px}dt{font-weight:600;color:#5c5c5c}dd{margin:0}")
            .Append("</style></head><body><main>")
            .Append("<h1>Test run ").Append(E(run.RunKey)).Append("</h1>")
            .Append("<p>Generated ").Append(E(T(generatedAtUtc))).Append(" from the Case Intake and Triage test results store. Synthetic data only.</p>")
            .Append("<h2>Summary</h2><dl>")
            .Append("<dt>Status</dt><dd>").Append(E(run.Status.ToString())).Append("</dd>")
            .Append("<dt>Started</dt><dd>").Append(E(T(run.StartedAtUtc))).Append("</dd>")
            .Append("<dt>Finished</dt><dd>").Append(E(T(run.FinishedAtUtc))).Append("</dd>")
            .Append("<dt>Duration</dt><dd>").Append(E(run.Duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture))).Append("</dd>")
            .Append("<dt>Environment</dt><dd>").Append(E($"{environment.Name} ({environment.OperatingSystem}, {environment.Runtime})")).Append("</dd>")
            .Append("<dt>Branch / commit</dt><dd>").Append(E($"{run.GitBranch ?? "unknown"} / {run.GitCommit ?? "unknown"}")).Append("</dd>")
            .Append("<dt>Build</dt><dd>").Append(E(run.BuildId ?? "unknown")).Append(" · image ").Append(E(run.ImageVersion ?? "unknown")).Append("</dd>")
            .Append("<dt>Results</dt><dd>").Append(c.Total).Append(" total: ").Append(c.Passed).Append(" passed, ").Append(c.Failed).Append(" failed, ")
            .Append(c.Skipped).Append(" skipped, ").Append(c.Blocked).Append(" blocked, ").Append(c.NotExecuted).Append(" not executed")
            .Append(c.PassRate is { } rate ? string.Create(CultureInfo.InvariantCulture, $" ({rate * 100:0.#}% pass rate)") : string.Empty).Append("</dd></dl>");

        html.Append("<h2>Suites</h2><table><caption>Suites in this run</caption><thead><tr><th scope=\"col\">Suite</th><th scope=\"col\">Type</th><th scope=\"col\">Status</th><th scope=\"col\">Tests</th><th scope=\"col\">Message</th></tr></thead><tbody>");
        foreach (var s in suites)
        {
            html.Append("<tr><td>").Append(E(s.Name)).Append("</td><td>").Append(E(s.Type.ToString())).Append("</td><td>").Append(E(s.Status))
                .Append("</td><td>").Append(s.Tests).Append("</td><td>").Append(E(s.Message)).Append("</td></tr>");
        }
        html.Append("</tbody></table>");

        html.Append("<h2>Compared with the previous run</h2>");
        if (comparison.Previous is null)
        {
            html.Append("<p>This is the first imported run.</p>");
        }
        else
        {
            html.Append("<p>Previous run: ").Append(E(comparison.Previous.RunKey)).Append(". Newly failed: ").Append(comparison.NewlyFailed.Count)
                .Append(". Newly fixed: ").Append(comparison.NewlyFixed.Count).Append(". Still failing: ").Append(comparison.StillFailing.Count).Append(".</p>");
            List(html, "Newly failed", comparison.NewlyFailed.Select(r => r.TestTitle));
            List(html, "Newly fixed", comparison.NewlyFixed.Select(r => r.TestTitle));
        }

        html.Append("<h2>All results</h2><table><caption>Every test result in this run</caption><thead><tr><th scope=\"col\">Module</th><th scope=\"col\">Test</th><th scope=\"col\">Outcome</th><th scope=\"col\">Duration (ms)</th><th scope=\"col\">Message</th></tr></thead><tbody>");
        foreach (var r in rows.OrderBy(r => r.Outcome == TestOutcome.Passed ? 1 : 0).ThenBy(r => r.Module).ThenBy(r => r.TestTitle))
        {
            html.Append("<tr><td>").Append(E(r.Module)).Append("</td><td>").Append(E(r.TestTitle)).Append("</td><td class=\"").Append(r.Outcome).Append("\">")
                .Append(E(Outcome(r.Outcome))).Append("</td><td>").Append(((long)r.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(E(r.FailureMessage)).Append("</td></tr>");
        }
        html.Append("</tbody></table></main></body></html>");
        return new TestReportFile(Encoding.UTF8.GetBytes(html.ToString()), $"test-run-{run.RunKey}.html", "text/html; charset=utf-8");

        static void List(StringBuilder html, string title, IEnumerable<string> items)
        {
            var list = items.ToList();
            if (list.Count == 0)
            {
                return;
            }
            html.Append("<h3>").Append(E(title)).Append("</h3><ul>");
            foreach (var item in list)
            {
                html.Append("<li>").Append(E(item)).Append("</li>");
            }
            html.Append("</ul>");
        }
    }

    public static string Outcome(TestOutcome outcome) => outcome == TestOutcome.NotExecuted ? "Not executed" : outcome.ToString();

    /// <summary>Quotes a CSV cell and neutralizes leading formula characters.</summary>
    internal static string Cell(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal) + "\"";
    }
}
