using Microsoft.AspNetCore.Diagnostics;

namespace CaseIntake.Web;

/// <summary>Startup check that the static web root exists (see Program.cs).</summary>
public static class WebRootGuard
{
    public static void EnsureWebRootExists(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.IsDevelopment())
        {
            return;   // Development serves static web assets from the project and package folders through the manifest.
        }

        var webRoot = environment.WebRootPath;
        if (string.IsNullOrEmpty(webRoot) || !Directory.Exists(webRoot) || !File.Exists(Path.Combine(webRoot, "app.css")))
        {
            throw new InvalidOperationException(
                $"The web root '{webRoot}' (content root '{environment.ContentRootPath}') does not contain the published " +
                "static assets. Start the app from its publish folder (Compose sets working_dir: /app/web) or set " +
                "ASPNETCORE_CONTENTROOT to it.");
        }
    }
}

/// <summary>The Not Found page route and the rule that limits status-code re-execution to unmatched URLs.</summary>
public static class NotFoundPages
{
    public const string Route = "/not-found";

    public static Task OnlyForUnmatchedRequests(HttpContext context, Func<Task> next)
    {
        // Routing has already selected an endpoint (WebApplication adds UseRouting first). A matched endpoint owns its
        // status code; only requests that matched nothing are re-executed to the Not Found page.
        if (context.GetEndpoint() is not null && context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
        {
            statusCodePages.Enabled = false;
        }
        return next();
    }
}
