using CaseIntake.ServiceDefaults;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;

namespace CaseIntake.VeteranLookup.Mock;

/// <summary>
/// Local, deterministic Veteran Lookup service (E4-S1). Its contract is the Government's definition (specification 4.6),
/// served verbatim at <c>/openapi/v1.yaml</c> (<see cref="GovernmentContract"/>). The document generated from these
/// endpoint declarations (<c>/openapi/implementation.json</c>) is conformance evidence only: a test compares it with the
/// Government definition and allows only the documented extensions (ADR 0008, D11-D15).
/// An explicit class (not top-level statements) so its entry point never collides with the web app's <c>Program</c>.
/// </summary>
public sealed class MockProgram
{
    public const string LookupRoute = "/veterans/lookup";
    public const string ImplementationDocumentName = "implementation";

    private MockProgram()
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
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, MockJsonContext.Default));
        builder.Services.AddProblemDetails();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(_ => VeteranDataset.LoadEmbedded());
        builder.Services.AddOpenApi(ImplementationDocumentName, options =>
        {
            // OpenAPI 3.0, like the Government definition. Microsoft.OpenApi writes its own 3.0 patch version (D15).
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.AddDocumentTransformer((document, _, _) =>
            {
                // The Government definition's info, and no "servers" entry, so the document does not depend on the host.
                document.Info = new OpenApiInfo { Title = "Veteran Lookup Service", Version = "1.0.0" };
                document.Servers?.Clear();
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, _, _) =>
            {
                // The Government definition's response descriptions instead of the framework's reason phrases.
                SetDescription(operation, "200", "Veteran found");
                SetDescription(operation, "404", "Not found");
                return Task.CompletedTask;
            });
        });

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
                    ProblemDetails = { Status = StatusCodes.Status400BadRequest, Title = "The request body is not a valid lookup request." },
                });
            }
        });

        app.MapPost(LookupRoute, Lookup)
            .WithName("LookupVeteran")
            .WithTags("Veterans")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .WithSummary("Look up a veteran by SSN")
            .WithDescription("Returns the veteran's name and address. 404 when the SSN is not in the dataset; " +
                "400 when the body is not valid JSON or ssn is missing, null, not a string or empty; 415 when the body is not JSON.");
        app.MapGet("/health", () => TypedResults.Ok()).ExcludeFromDescription();
        app.MapGet(GovernmentContract.Route, () => TypedResults.Bytes(GovernmentContract.Bytes, GovernmentContract.ContentType))
            .ExcludeFromDescription();
        app.MapOpenApi("/openapi/{documentName}.json");
        return app;
    }

    private static void SetDescription(OpenApiOperation operation, string status, string description)
    {
        if (operation.Responses?.TryGetValue(status, out var response) == true && response is OpenApiResponse concrete)
        {
            concrete.Description = description;
        }
    }

    internal static Results<Ok<VeteranLookupResponse>, NotFound, ProblemHttpResult> Lookup(
        VeteranLookupRequest request, VeteranDataset dataset, TimeProvider time, ILoggerFactory loggers)
    {
        // The request's operation ID scope comes from OperationContextMiddleware; no second scope is opened here.
        var logger = loggers.CreateLogger(typeof(MockProgram).Namespace!);
        var started = time.GetTimestamp();
        void Served(string outcome) => MockLog.LookupServed(logger, outcome, time.GetElapsedTime(started).TotalMilliseconds);

        // [JsonRequired] turns a missing or non-string ssn into a 400 before this runs; an explicit null or a blank
        // string reaches here. The SSN format is deliberately not checked: that rule belongs to the client (OQ-5c).
        if (string.IsNullOrWhiteSpace(request.Ssn))
        {
            Served("InvalidInput");
            return TypedResults.Problem(title: "ssn is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!dataset.TryFind(request.Ssn, out var record))
        {
            Served("NotFound");
            return TypedResults.NotFound();
        }

        Served("Found");
        return TypedResults.Ok(new VeteranLookupResponse { Name = record.Name, Address = record.Address });
    }
}
