using System.Text.Json;
using CaseIntake.ServiceDefaults;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Logging;

public class LoggingTests
{
    private static async Task<List<JsonDocument>> CaptureAsync(Action<ILogger> act)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.AddServiceDefaults(includeHealthChecks: false);
            using var host = builder.Build();
            act(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test.Category"));
            await host.StopAsync();
            await Task.Delay(200);   // console provider writes on a background thread
        }
        finally
        {
            Console.SetOut(original);
        }

        return [.. writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith('{')).Select(l => JsonDocument.Parse(l))];
    }

    [Fact]
    public async Task Logs_are_json_with_utc_timestamp_level_category_eventid_and_operation_id()
    {
        var lines = await CaptureAsync(logger =>
        {
            using var scope = logger.BeginOperation("unit-test");
            logger.LogInformation(new EventId(1234, "Sample"), "Case {CaseId} processed", 42);
        });

        var root = Assert.Single(lines, l => l.RootElement.GetProperty("EventId").GetInt32() == 1234).RootElement;
        Assert.EndsWith("Z", root.GetProperty("Timestamp").GetString());
        Assert.Equal("Information", root.GetProperty("LogLevel").GetString());
        Assert.Equal("Test.Category", root.GetProperty("Category").GetString());
        Assert.Equal(42, root.GetProperty("State").GetProperty("CaseId").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("Scopes").GetProperty("OperationId").GetString()));
        Assert.Equal("unit-test", root.GetProperty("Scopes").GetProperty("Operation").GetString());
    }

    [Fact]
    public async Task Ssn_shaped_values_are_redacted_in_message_state_and_scopes()
    {
        var lines = await CaptureAsync(logger =>
        {
            using var scope = logger.BeginScope(new Dictionary<string, object> { ["Leak"] = "scope 900-45-6789" });
            logger.LogWarning(new EventId(1235), "value {Ssn} and {Plain}", "900-45-6789", "987654321");
        });

        var text = lines.Single(l => l.RootElement.GetProperty("EventId").GetInt32() == 1235).RootElement.GetRawText();
        Assert.DoesNotContain("900-45-6789", text);
        Assert.DoesNotContain("987654321", text);
        Assert.Contains(SsnRedaction.Replacement, text);
    }

    [Fact]
    public async Task Exception_messages_are_never_written_only_the_type()
    {
        var lines = await CaptureAsync(logger =>
            logger.LogError(new EventId(1236), new InvalidOperationException("secret value 666-44-3333 in provider text"), "Operation failed"));

        var root = lines.Single(l => l.RootElement.GetProperty("EventId").GetInt32() == 1236).RootElement;
        Assert.Equal("System.InvalidOperationException", root.GetProperty("ExceptionType").GetString());
        Assert.DoesNotContain("provider text", root.GetRawText());
    }

    [Theory]
    [InlineData("900-45-6789", true)]
    [InlineData("900456789", true)]
    [InlineData("ssn=900456789.", true)]
    [InlineData("1234567890", false)]
    [InlineData("12-345-6789", false)]
    [InlineData("case 42", false)]
    public void Ssn_detector_matches_nine_digit_forms_only(string text, bool expected) =>
        Assert.Equal(expected, SsnRedaction.ContainsSsn(text));

    [Fact]
    public void FakeLogger_capture_can_assert_no_ssn_in_captured_records()
    {
        var logger = new FakeLogger();
        logger.LogInformation(new EventId(1237), "Lookup completed with outcome {Outcome}", "Found");

        Assert.DoesNotContain(logger.Collector.GetSnapshot(), r => SsnRedaction.ContainsSsn(r.Message));
    }
}
