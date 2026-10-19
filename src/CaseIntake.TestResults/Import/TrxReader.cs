using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CaseIntake.Domain.TestResults;

namespace CaseIntake.TestResults.Import;

/// <summary>One result from a TRX file.</summary>
public sealed record TrxResult(
    string FullName,
    string ClassName,
    string MethodName,
    TestOutcome Outcome,
    string RawOutcome,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    TimeSpan Duration,
    string? ErrorMessage,
    string? StackTrace,
    string? StandardOutput,
    IReadOnlyList<string> Categories);

/// <summary>
/// Reads the VSTest TRX format (the machine-readable output of <c>dotnet test --logger trx</c>). DTD processing is
/// disabled and the document size is bounded, so a hostile file cannot expand entities or exhaust memory.
/// </summary>
public static class TrxReader
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static IReadOnlyList<TrxResult> Read(Stream trx)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 512L * 1024 * 1024,
            IgnoreComments = true,
        };
        using var reader = XmlReader.Create(trx, settings);
        var doc = XDocument.Load(reader);

        var definitions = doc.Descendants(Ns + "UnitTest")
            .Select(d => new
            {
                Id = (string?)d.Attribute("id"),
                ClassName = (string?)d.Element(Ns + "TestMethod")?.Attribute("className") ?? string.Empty,
                MethodName = (string?)d.Element(Ns + "TestMethod")?.Attribute("name") ?? string.Empty,
                Categories = d.Descendants(Ns + "TestCategoryItem").Select(c => (string?)c.Attribute("TestCategory")).OfType<string>().ToList(),
            })
            .Where(d => d.Id is not null)
            .GroupBy(d => d.Id!)
            .ToDictionary(g => g.Key, g => g.First());

        List<TrxResult> results = [];
        foreach (var r in doc.Descendants(Ns + "UnitTestResult"))
        {
            var testId = (string?)r.Attribute("testId") ?? string.Empty;
            definitions.TryGetValue(testId, out var definition);
            var name = (string?)r.Attribute("testName") ?? definition?.MethodName ?? testId;
            var rawOutcome = (string?)r.Attribute("outcome") ?? "NotExecuted";
            var output = r.Element(Ns + "Output");
            var error = output?.Element(Ns + "ErrorInfo");
            results.Add(new TrxResult(
                FullName: name,
                ClassName: definition?.ClassName ?? string.Empty,
                MethodName: definition?.MethodName ?? name,
                Outcome: MapOutcome(rawOutcome, error is not null),
                RawOutcome: rawOutcome,
                StartedAtUtc: ParseTime((string?)r.Attribute("startTime")),
                FinishedAtUtc: ParseTime((string?)r.Attribute("endTime")),
                Duration: TimeSpan.TryParse((string?)r.Attribute("duration"), CultureInfo.InvariantCulture, out var d) ? d : TimeSpan.Zero,
                ErrorMessage: (string?)error?.Element(Ns + "Message"),
                StackTrace: (string?)error?.Element(Ns + "StackTrace"),
                StandardOutput: (string?)output?.Element(Ns + "StdOut"),
                Categories: definition?.Categories ?? []));
        }
        return results;
    }

    /// <summary>
    /// TRX outcomes to the module's five outcomes. xUnit reports a skipped test as NotExecuted with a reason, which is a
    /// deliberate skip here; tests that never ran because their suite did not start are recorded as NotExecuted by the
    /// importer, not by this mapping. Aborted, Disconnected and InProgress mean the run stopped under the test (Blocked).
    /// </summary>
    public static TestOutcome MapOutcome(string trxOutcome, bool hasError) => trxOutcome switch
    {
        "Passed" or "PassedButRunAborted" => TestOutcome.Passed,
        "Failed" or "Error" or "Timeout" => TestOutcome.Failed,
        "NotExecuted" or "NotRunnable" or "Inconclusive" or "Pending" or "Warning" => hasError ? TestOutcome.Failed : TestOutcome.Skipped,
        "Aborted" or "Disconnected" or "InProgress" => TestOutcome.Blocked,
        _ => TestOutcome.Failed,
    };

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time.ToUniversalTime() : null;
}
