using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CaseIntake.Identity.Security;

/// <summary>
/// Baseline response headers for every response (E3-S6): <c>X-Content-Type-Options: nosniff</c>, a <c>Referrer-Policy</c>,
/// frame denial and a Content-Security-Policy that Blazor Server works under. Scripts load only from the app's own origin
/// (<c>_framework/blazor.web.js</c>); the SignalR circuit connects to the same host over WebSockets (named explicitly,
/// because older browsers do not treat <c>ws:</c> as matching <c>'self'</c>). Inline styles are allowed because the Blazor
/// reconnect and error UI set them at run time; inline scripts, plugins, framing and foreign form targets are not.
/// No HSTS or <c>upgrade-insecure-requests</c>: the app is served over plain HTTP on localhost:8080 (spec 4.5).
/// </summary>
public sealed partial class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicyBase =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; " +
        "img-src 'self' data:; font-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            var headers = ctx.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "same-origin";
            headers.XFrameOptions = "DENY";
            // Browsers honour COOP only on potentially trustworthy origins (HTTPS or loopback) and log a console error
            // for it elsewhere (for example http://web-e2e:8080 on the Compose network), so it is sent only where it applies.
            if (IsTrustworthy(ctx.Request))
            {
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
            }
            headers.ContentSecurityPolicy = ContentSecurityPolicy(ctx.Request.Host);
            return Task.CompletedTask;
        }, context);

        return next(context);
    }

    /// <summary>HTTPS, or a loopback host (localhost, 127.0.0.0/8, ::1): where browsers apply Cross-Origin-Opener-Policy.</summary>
    public static bool IsTrustworthy(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IsHttps)
        {
            return true;
        }
        var host = request.Host.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var address) && System.Net.IPAddress.IsLoopback(address));
    }

    /// <summary>The policy for a request to <paramref name="host"/>; <c>connect-src</c> adds that host's ws/wss origin when it is well formed.</summary>
    public static string ContentSecurityPolicy(HostString host)
    {
        var connect = "connect-src 'self'";
        if (host.HasValue && HostPattern().IsMatch(host.Value))
        {
            connect += $" ws://{host.Value} wss://{host.Value}";
        }
        return $"{ContentSecurityPolicyBase}; {connect}";
    }

    // Host names, IPv4 and bracketed IPv6 literals with an optional port; anything else is left out of the policy.
    [GeneratedRegex(@"^(\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9.-]+)(:[0-9]{1,5})?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();
}

public static class SecurityHeadersExtensions
{
    /// <summary>Adds the baseline security headers to every response. Register before static files and endpoints.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
