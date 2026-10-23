using CaseIntake.ServiceDefaults;

namespace CaseIntake.VeteranHistory.Mock;

/// <summary>
/// Local, deterministic Veteran Service History service (Change 2, D1). Shell from the H0 PR: only <c>/health</c>, so
/// Compose can start it; E4-C2-1 adds <c>POST /veterans/service-history/lookup</c> and the synthetic data.
/// An explicit class (not top-level statements) so its entry point never collides with the web app's <c>Program</c>.
/// </summary>
public sealed class HistoryMockProgram
{
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

        var app = builder.Build();
        app.UseOperationContext();
        app.MapGet("/health", () => TypedResults.Ok());
        return app;
    }
}
