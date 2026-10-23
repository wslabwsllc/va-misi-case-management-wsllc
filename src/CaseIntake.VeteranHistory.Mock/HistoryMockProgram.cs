using CaseIntake.ServiceDefaults;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CaseIntake.VeteranHistory.Mock;

/// <summary>
/// Local, deterministic Veteran Service History service (Change 2, D1, E4-C2-1). Its contract is the team's own document
/// <c>docs/contracts/veteran-service-history.openapi.yaml</c> (E4-C2-2): the Government's interface table, plus 400 for an
/// unusable body and the optional <c>lastVerifiedDate</c> (D7). <see cref="ServiceHistoryDataset.OutageSsn"/> always
/// answers 503, so the outage path can be shown without stopping the container.
/// An explicit class (not top-level statements) so its entry point never collides with the web app's <c>Program</c>,
/// and a different name from the Lookup mock's <c>MockProgram</c>, so one test project can host both.
/// </summary>
public sealed class HistoryMockProgram
{
    public const string LookupRoute = "/veterans/service-history/lookup";

    private HistoryMockProgram()
    {
    }

    public static async Task Main(string[] args)
    {
        await using var app = BuildApp(args);
        await app.RunAsync();
    }

    public static WebApplication BuildApp(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        builder.AddServiceDefaults(includeHealthChecks: false);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, HistoryMockJsonContext.Default));
        builder.Services.AddProblemDetails();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(_ => ServiceHistoryDataset.LoadEmbedded());

        var app = builder.Build();
        app.UseOperationContext();
        // In Production the framework rejects an unreadable body (malformed JSON, a non-string ssn) with an empty 400.
        // Give every 400 the same problem+json body the contract documents, whichever layer produced it.
        app.UseStatusCodePages(async context =>
        {
            var http = context.HttpContext;
            if (http.Response.StatusCode == StatusCodes.Status400BadRequest)
            {
                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = { Status = StatusCodes.Status400BadRequest, Title = "The request body is not a valid service-history request." },
                });
            }
        });

        app.MapPost(LookupRoute, Lookup);
        app.MapGet("/health", () => TypedResults.Ok());
        return app;
    }

    internal static Results<Ok<ServiceHistoryResponse>, NotFound, ProblemHttpResult> Lookup(
        ServiceHistoryRequest request, ServiceHistoryDataset dataset, TimeProvider time, ILoggerFactory loggers)
    {
        // The request's operation ID scope comes from OperationContextMiddleware; no second scope is opened here.
        var logger = loggers.CreateLogger(typeof(HistoryMockProgram).Namespace!);
        var started = time.GetTimestamp();
        void Served(string outcome) => HistoryMockLog.Served(logger, outcome, time.GetElapsedTime(started).TotalMilliseconds);

        // [JsonRequired] turns a missing or non-string ssn into a 400 before this runs; an explicit null or a blank
        // string reaches here. As in the Lookup mock, the SSN format is the client's rule, not the service's.
        if (string.IsNullOrWhiteSpace(request.Ssn))
        {
            Served("InvalidInput");
            return TypedResults.Problem(title: "ssn is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (ServiceHistoryDataset.Dashed(request.Ssn) == ServiceHistoryDataset.OutageSsn)
        {
            Served("Unavailable");
            return TypedResults.Problem(title: "The service-history service is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!dataset.TryFind(request.Ssn, out var record))
        {
            Served("NotFound");
            return TypedResults.NotFound();
        }

        Served("Found");
        return TypedResults.Ok(new ServiceHistoryResponse
        {
            Branch = record.Branch,
            ServiceStartDate = record.ServiceStartDate,
            ServiceEndDate = record.ServiceEndDate,
            CharacterOfService = record.CharacterOfService,
            LastVerifiedDate = record.LastVerifiedDate,
        });
    }
}
