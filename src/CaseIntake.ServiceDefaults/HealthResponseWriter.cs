using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CaseIntake.ServiceDefaults;

/// <summary>
/// Writes a health report as JSON: the overall status (the worst component status) plus one entry per check with its
/// status, duration, description and data. Exceptions are never written: their messages can carry connection strings
/// or values, so checks put a safe description and an <c>errorType</c> in data instead.
/// </summary>
public static class HealthResponseWriter
{
    public const string ContentType = "application/json; charset=utf-8";

    public static async Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = ContentType;
        await using var json = new Utf8JsonWriter(context.Response.Body, new JsonWriterOptions { Indented = true });

        json.WriteStartObject();
        json.WriteString("status", report.Status.ToString());
        json.WriteNumber("totalDurationMs", Math.Round(report.TotalDuration.TotalMilliseconds, 1));
        json.WriteStartObject("components");
        foreach (var (name, entry) in report.Entries)
        {
            json.WriteStartObject(name);
            json.WriteString("status", entry.Status.ToString());
            json.WriteNumber("durationMs", Math.Round(entry.Duration.TotalMilliseconds, 1));
            if (!string.IsNullOrEmpty(entry.Description))
            {
                json.WriteString("description", entry.Description);
            }
            if (entry.Data.Count > 0)
            {
                json.WriteStartObject("data");
                foreach (var (key, value) in entry.Data)
                {
                    json.WriteString(key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                }
                json.WriteEndObject();
            }
            json.WriteEndObject();
        }
        json.WriteEndObject();
        json.WriteEndObject();

        await json.FlushAsync(context.RequestAborted);
    }
}
