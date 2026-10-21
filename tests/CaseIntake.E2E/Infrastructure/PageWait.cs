using System.Diagnostics;
using Microsoft.Playwright;

namespace CaseIntake.E2E.Infrastructure;

/// <summary>
/// Waits for a page condition without <c>IPage.WaitForFunctionAsync</c>: from Playwright 1.52 its polling predicate calls
/// eval in the page, which the app's Content-Security-Policy (script-src 'self', no 'unsafe-eval') refuses.
/// <c>EvaluateAsync</c> goes through the DevTools protocol, which CSP does not govern.
/// </summary>
internal static class PageWait
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task UntilAsync(IPage page, string predicate)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await TryEvaluateAsync(page, predicate))
        {
            if (elapsed.Elapsed > Timeout)
            {
                throw new TimeoutException($"Timed out after {Timeout.TotalSeconds:0} s waiting for {predicate}");
            }
            await Task.Delay(100);
        }
    }

    // A poll that lands during a navigation sees the old document go away; that is "not yet", not a failure.
    private static async Task<bool> TryEvaluateAsync(IPage page, string predicate)
    {
        try
        {
            return await page.EvaluateAsync<bool>(predicate);
        }
        catch (PlaywrightException e) when (e.Message.Contains("context was destroyed", StringComparison.Ordinal)
                                            || e.Message.Contains("navigat", StringComparison.Ordinal))
        {
            return false;
        }
    }
}
