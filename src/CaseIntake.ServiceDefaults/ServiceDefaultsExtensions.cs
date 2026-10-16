using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace CaseIntake.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// JSON logs on stdout (UTC timestamp, level, category, EventId, scopes with OperationId and TraceId/SpanId),
    /// SSN scrubbing, quiet framework categories, and the readiness health checks. Telemetry exporters are
    /// intentionally absent: nothing leaves the host (plan 0.7).
    /// </summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder, bool includeHealthChecks = true)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.FormatterName = RedactingJsonConsoleFormatter.FormatterName);
        // Binds Logging:Console:FormatterOptions like the built-in formatters. The timestamp is always UTC ISO-8601,
        // so only IncludeScopes and JsonWriterOptions are honoured.
        builder.Logging.AddConsoleFormatter<RedactingJsonConsoleFormatter, JsonConsoleFormatterOptions>(options =>
            options.IncludeScopes = true);
        builder.Logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

        // EF would otherwise log statements at Information; HTTP client logging is limited to warnings (no bodies ever).
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        if (includeHealthChecks)
        {
            builder.Services.AddHttpClient(VeteranLookupHealthCheck.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(3));
            builder.Services.AddHealthChecks()
                .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag])
                .AddCheck<VeteranLookupHealthCheck>("veteran-lookup", tags: [ReadyTag]);
        }

        return builder;
    }

    /// <summary>Adds the per-request operation ID scope. Call early in the pipeline.</summary>
    public static IApplicationBuilder UseOperationContext(this IApplicationBuilder app) =>
        app.UseMiddleware<OperationContextMiddleware>();

    /// <summary>
    /// <c>/health/live</c> runs no checks (process is up); <c>/health/ready</c> requires the database and the veteran
    /// lookup service. Both are anonymous so probes never redirect to login.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains(ReadyTag) }).AllowAnonymous();
        return app;
    }
}
