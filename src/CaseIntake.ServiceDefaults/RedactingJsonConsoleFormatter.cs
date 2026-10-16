using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace CaseIntake.ServiceDefaults;

/// <summary>
/// One compact JSON object per line: UTC timestamp, level, category, EventId, rendered message, structured state,
/// scopes (OperationId, TraceId/SpanId when an activity exists) and the exception <em>type</em>. Exception messages are
/// deliberately omitted because provider/HTTP messages can embed submitted values. Every string passes through
/// <see cref="SsnRedaction"/> as an extra safeguard.
/// </summary>
public sealed class RedactingJsonConsoleFormatter(IOptionsMonitor<JsonConsoleFormatterOptions> options, TimeProvider? timeProvider = null)
    : ConsoleFormatter(FormatterName)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public const string FormatterName = "caseintake-json";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
        {
            return;
        }

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, options.CurrentValue.JsonWriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("Timestamp", _time.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
            json.WriteString("LogLevel", logEntry.LogLevel.ToString());
            json.WriteString("Category", logEntry.Category);
            json.WriteNumber("EventId", logEntry.EventId.Id);
            if (logEntry.EventId.Name is { } eventName)
            {
                json.WriteString("EventName", eventName);
            }
            json.WriteString("Message", SsnRedaction.Redact(message ?? string.Empty));
            if (logEntry.Exception is { } exception)
            {
                json.WriteString("ExceptionType", exception.GetType().FullName);
            }

            if (logEntry.State is IEnumerable<KeyValuePair<string, object?>> state)
            {
                json.WriteStartObject("State");
                foreach (var (key, value) in state)
                {
                    WriteValue(json, key, value);
                }
                json.WriteEndObject();
            }

            if (options.CurrentValue.IncludeScopes && scopeProvider is not null)
            {
                json.WriteStartObject("Scopes");
                scopeProvider.ForEachScope(static (scope, writer) =>
                {
                    // Also matches IEnumerable<KeyValuePair<string, object>>: nullability is erased at runtime.
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (key, value) in pairs)
                        {
                            WriteValue(writer, key, value);
                        }
                    }
                }, json);
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        textWriter.WriteLine(SsnRedaction.Redact(Encoding.UTF8.GetString(stream.ToArray())));
    }

    private static void WriteValue(Utf8JsonWriter json, string key, object? value)
    {
        if (key == "{OriginalFormat}")
        {
            return;
        }

        switch (value)
        {
            case null: json.WriteNull(key); break;
            case bool b: json.WriteBoolean(key, b); break;
            case int i: json.WriteNumber(key, i); break;
            case long l: json.WriteNumber(key, l); break;
            case uint u: json.WriteNumber(key, u); break;
            case double d: json.WriteNumber(key, d); break;
            case decimal m: json.WriteNumber(key, m); break;
            case DateTimeOffset dto: json.WriteString(key, dto.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)); break;
            case Enum e: json.WriteString(key, e.ToString()); break;
            default: json.WriteString(key, SsnRedaction.Redact(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)); break;
        }
    }
}
