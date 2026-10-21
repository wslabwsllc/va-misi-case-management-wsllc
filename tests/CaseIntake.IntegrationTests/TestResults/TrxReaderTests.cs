using System.Text;
using System.Xml;
using CaseIntake.Domain.TestResults;
using CaseIntake.TestResults.Import;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>E5: the TRX reader maps VSTest results to the module's outcomes and refuses hostile XML.</summary>
[Trait("Category", "Story:E5-S2")]
public class TrxReaderTests
{
    private static IReadOnlyList<TrxResult> Read(string xml) => TrxReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Results_carry_names_outcomes_timings_errors_output_and_categories_from_their_definitions()
    {
        var results = Read(Trx.Document(
            new TrxTest("Ns.Suite", "Passes", Categories: ["Story:E5-S2", "Req:R-1"], StdOut: "hello"),
            new TrxTest("Ns.Suite", "Fails", "Failed", Message: "Assert.Equal() Failure", StackTrace: "at Ns.Suite.Fails()", Duration: "00:00:01.5000000"),
            new TrxTest("Ns.Suite", "Theory", Arguments: "(x: 1)")));

        Assert.Equal(3, results.Count);
        var passed = results[0];
        Assert.Equal(("Ns.Suite.Passes", "Ns.Suite", "Passes", TestOutcome.Passed, "Passed"),
            (passed.FullName, passed.ClassName, passed.MethodName, passed.Outcome, passed.RawOutcome));
        Assert.Equal(["Story:E5-S2", "Req:R-1"], passed.Categories);
        Assert.Equal("hello", passed.StandardOutput);
        Assert.Equal(TestResultsTestHarness.StartTime, passed.StartedAtUtc);
        Assert.Equal(TestResultsTestHarness.StartTime.AddMilliseconds(125), passed.FinishedAtUtc);
        Assert.Equal(TimeSpan.FromMilliseconds(125), passed.Duration);
        Assert.Null(passed.ErrorMessage);

        var failed = results[1];
        Assert.Equal(TestOutcome.Failed, failed.Outcome);
        Assert.Equal("Assert.Equal() Failure", failed.ErrorMessage);
        Assert.Equal("at Ns.Suite.Fails()", failed.StackTrace);
        Assert.Equal(TimeSpan.FromSeconds(1.5), failed.Duration);
        Assert.Empty(failed.Categories);

        Assert.Equal("Ns.Suite.Theory(x: 1)", results[2].FullName);
        Assert.Equal("Theory", results[2].MethodName);
    }

    [Fact]
    public void A_result_without_a_definition_or_attributes_falls_back_to_safe_defaults()
    {
        const string xml = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="orphan" duration="not a duration" startTime="garbage" />
                <UnitTestResult testId="def" testName="Named.Test" outcome="Passed" />
              </Results>
              <TestDefinitions>
                <UnitTest id="def"><TestMethod className="Named" name="Test" /></UnitTest>
                <UnitTest id="def"><TestMethod className="Duplicate" name="Ignored" /></UnitTest>
                <UnitTest><TestMethod className="NoId" name="Ignored" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """;

        var results = Read(xml);

        var orphan = results[0];
        Assert.Equal(("orphan", string.Empty, "orphan", "NotExecuted", TestOutcome.Skipped),
            (orphan.FullName, orphan.ClassName, orphan.MethodName, orphan.RawOutcome, orphan.Outcome));
        Assert.Equal(TimeSpan.Zero, orphan.Duration);
        Assert.Null(orphan.StartedAtUtc);
        Assert.Null(orphan.FinishedAtUtc);
        Assert.Equal("Named", results[1].ClassName);   // the first definition of a duplicated id wins
    }

    [Fact]
    public void A_document_with_a_DTD_is_refused_so_entities_cannot_expand()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;">]>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results /></TestRun>
            """;

        Assert.Throws<XmlException>(() => Read(xml));
    }

    [Fact]
    public void Malformed_xml_is_reported_as_an_xml_error()
    {
        Assert.Throws<XmlException>(() => Read("<TestRun><Results>"));
    }

    [Theory]
    [InlineData("Passed", false, TestOutcome.Passed)]
    [InlineData("PassedButRunAborted", false, TestOutcome.Passed)]
    [InlineData("Failed", false, TestOutcome.Failed)]
    [InlineData("Error", false, TestOutcome.Failed)]
    [InlineData("Timeout", false, TestOutcome.Failed)]
    [InlineData("NotExecuted", false, TestOutcome.Skipped)]
    [InlineData("NotExecuted", true, TestOutcome.Failed)]
    [InlineData("NotRunnable", false, TestOutcome.Skipped)]
    [InlineData("Inconclusive", false, TestOutcome.Skipped)]
    [InlineData("Pending", false, TestOutcome.Skipped)]
    [InlineData("Warning", true, TestOutcome.Failed)]
    [InlineData("Aborted", false, TestOutcome.Blocked)]
    [InlineData("Disconnected", false, TestOutcome.Blocked)]
    [InlineData("InProgress", false, TestOutcome.Blocked)]
    [InlineData("SomethingNew", false, TestOutcome.Failed)]
    public void Trx_outcomes_map_to_the_five_module_outcomes(string trxOutcome, bool hasError, TestOutcome expected)
    {
        Assert.Equal(expected, TrxReader.MapOutcome(trxOutcome, hasError));
    }
}
