using System.Globalization;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;

namespace CaseIntake.Web.Components.Pages.TestResults;

/// <summary>
/// The QA results filters as they appear in the URL (QA 508 enhancement), shared by the result and test case lists, their
/// exports and the export endpoints, so the list, its counts and the exported file always use the same criteria.
/// Keys: <c>q</c>, <c>outcome</c>, <c>module</c>, <c>type</c>, <c>a11y</c> (<c>508</c> = Section 508 tests only,
/// <c>not508</c> = exclude them; absent = all tests) and <c>page</c>. Parsing is lenient: an unknown value is ignored.
/// </summary>
public static class TestResultsQueryString
{
    public const int ExecutionPageSize = 50;
    public const int CasePageSize = 50;

    public static string? Section508Value(Section508Filter filter) => filter switch
    {
        Section508Filter.Section508Only => "508",
        Section508Filter.ExcludeSection508 => "not508",
        _ => null,
    };

    public static Section508Filter ParseSection508(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "508" => Section508Filter.Section508Only,
        "not508" => Section508Filter.ExcludeSection508,
        _ => Section508Filter.All,
    };

    public static TestExecutionQuery Executions(long? runId, Func<string, string?> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new TestExecutionQuery(runId, Outcome(query("outcome")) is { } o ? [o] : null, Text(query("module")),
            Type(query("type")), Text(query("q")), Page(query("page")), ExecutionPageSize, ParseSection508(query("a11y")));
    }

    public static TestCaseQuery Cases(Func<string, string?> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new TestCaseQuery(Text(query("module")), Type(query("type")), Outcome(query("outcome")), Text(query("q")),
            Page(query("page")), CasePageSize, ParseSection508(query("a11y")));
    }

    /// <summary>URL values for <c>GetUriWithQueryParameters</c>; defaults are removed (null).</summary>
    public static IReadOnlyDictionary<string, object?> Parameters(TestExecutionQuery q) => new Dictionary<string, object?>
    {
        ["q"] = q.Search,
        ["outcome"] = q.Outcomes is [var o] ? o.ToString() : null,
        ["module"] = q.Module,
        ["type"] = q.Type?.ToString(),
        ["a11y"] = Section508Value(q.Section508),
        ["page"] = q.Page <= 1 ? null : q.Page,
    };

    public static IReadOnlyDictionary<string, object?> Parameters(TestCaseQuery q) => new Dictionary<string, object?>
    {
        ["q"] = q.Search,
        ["outcome"] = q.LastOutcome?.ToString(),
        ["module"] = q.Module,
        ["type"] = q.Type?.ToString(),
        ["a11y"] = Section508Value(q.Section508),
        ["page"] = q.Page <= 1 ? null : q.Page,
    };

    /// <summary>The filtered export link of a result list (paging is ignored by the export).</summary>
    public static string ExecutionsExportHref(TestExecutionQuery q) =>
        TestResultRoutes.ExecutionsExport.TrimStart('/') + QueryString(Parameters(q with { Page = 1 }), q.RunId);

    public static string CasesExportHref(TestCaseQuery q) =>
        TestResultRoutes.TestCasesExport.TrimStart('/') + QueryString(Parameters(q with { Page = 1 }), null);

    private static string QueryString(IReadOnlyDictionary<string, object?> values, long? runId)
    {
        var parts = values.Where(v => v.Value is not null)
            .Select(v => $"{v.Key}={Uri.EscapeDataString(Convert.ToString(v.Value, CultureInfo.InvariantCulture)!)}").ToList();
        if (runId is { } id)
        {
            parts.Insert(0, $"run={id.ToString(CultureInfo.InvariantCulture)}");
        }
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: <= 200 } t ? t : value.Trim()[..200];

    private static int Page(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is >= 1 and <= 100_000 ? p : 1;

    private static TestOutcome? Outcome(string? value) => Enum.TryParse<TestOutcome>(value, out var o) && Enum.IsDefined(o) ? o : null;

    private static TestType? Type(string? value) => Enum.TryParse<TestType>(value, out var t) && Enum.IsDefined(t) ? t : null;
}
