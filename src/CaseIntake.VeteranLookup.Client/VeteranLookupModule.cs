using System.Net;
using CaseIntake.Contracts.VeteranLookup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace CaseIntake.VeteranLookup.Client;

public static class VeteranLookupModule
{
    public const string PipelineName = "veteran-lookup";

    private static readonly TimeSpan MinimumTimeout = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromDays(1);

    /// <summary>
    /// Registers <see cref="IVeteranLookupClient"/> as a typed HTTP client. It is resolved in the caller's scope, so it
    /// sees the circuit's or request's <c>ICurrentUser</c>. The resilience pipeline is one retry for connection errors,
    /// timeouts, 408, 429 and 5xx (the lookup POST is read-only, so a retry is safe) around a per-attempt timeout
    /// (2 s). The total budget (5 s) is owned by the client, because it must also cover reading the response body.
    /// Readiness of the service is checked by ServiceDefaults.
    /// </summary>
    public static IServiceCollection AddVeteranLookupClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        AddVeteranLookupHttpClient(services);
        return services;
    }

    /// <summary>The registration behind <see cref="AddVeteranLookupClient"/>; tests chain a primary handler onto it.</summary>
    internal static IHttpClientBuilder AddVeteranLookupHttpClient(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        // Polly's built-in telemetry logs each attempt with the raw exception, whose message can embed request
        // details. Plan 0.3 forbids logging raw exception messages, so it is silenced for this pipeline only (below) and
        // the retry is logged by OnRetry with safe fields (attempt number, status code or exception type).
        services.AddOptions<VeteranLookupClientOptions>()
            .BindConfiguration(VeteranLookupClientOptions.SectionName)
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                "VeteranLookup:BaseUrl must be an absolute http or https URL.")
            // Polly accepts timeouts of 10 ms to 1 day and retry delays of 0 to 1 day; checked here so a bad value
            // stops startup instead of failing the first lookup.
            .Validate(o => o.AttemptTimeout >= MinimumTimeout && o.AttemptTimeout <= MaximumTimeout && o.TotalTimeout >= o.AttemptTimeout,
                "VeteranLookup:AttemptTimeout must be between 10 ms and 1 day and VeteranLookup:TotalTimeout at least that long.")
            .Validate(o => o.RetryDelay >= TimeSpan.Zero && o.RetryDelay <= MaximumTimeout,
                "VeteranLookup:RetryDelay must be between zero and 1 day.")
            .ValidateOnStart();

        var http = services.AddHttpClient<IVeteranLookupClient, VeteranLookupClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<VeteranLookupClientOptions>>().Value;
            client.BaseAddress = WithTrailingSlash(new Uri(options.BaseUrl, UriKind.Absolute));
            client.Timeout = Timeout.InfiniteTimeSpan;   // the pipeline owns every timeout
        });
        http.AddResilienceHandler(PipelineName, (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<VeteranLookupClientOptions>>().Value;
                var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<VeteranLookupClient>();
                pipeline
                    .ConfigureTelemetry(NullLoggerFactory.Instance)   // scoped to this pipeline, not app-wide
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 1,
                        Delay = options.RetryDelay,
                        BackoffType = DelayBackoffType.Constant,
                        UseJitter = false,
                        ShouldRetryAfterHeader = false,   // keep the five-second budget predictable
                        ShouldHandle = args => ValueTask.FromResult(IsRetryable(args.Outcome)),
                        OnRetry = args =>
                        {
                            LookupLog.Retrying(logger, args.AttemptNumber + 1,
                                args.Outcome.Exception?.GetType().Name ?? "HttpStatus", (int?)args.Outcome.Result?.StatusCode);
                            return ValueTask.CompletedTask;
                        },
                    })
                    .AddTimeout(options.AttemptTimeout);
            });
        return http;
    }

    internal static bool IsRetryable(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException or TimeoutRejectedException } => true,
        { Result: { } response } => response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500,
        _ => false,
    };

    private static Uri WithTrailingSlash(Uri uri) => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
