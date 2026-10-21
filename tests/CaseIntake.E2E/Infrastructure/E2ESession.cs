using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

namespace CaseIntake.E2E.Infrastructure;

/// <summary>What a test is, for the evidence manifest and the Test Results module.</summary>
public sealed record TestMeta(
    string FullName,
    string Title,
    string Story,
    IReadOnlyList<string> Requirements,
    string Priority = "High",
    string? Description = null,
    string? Preconditions = null,
    string? Expected = null,
    string? AcceptanceCriteria = null);

/// <summary>
/// One test's browser context and its evidence. Every step runs through <see cref="StepAsync"/>, which records its
/// name, outcome, duration, expected and actual result; a failing step takes a screenshot (sensitive inputs masked)
/// and the trace is kept. Browser console errors, page errors and failed or erroring network requests are collected.
/// On disposal the evidence (steps, screenshots, trace, optional video, logs) is written to
/// <c>&lt;run&gt;/evidence/&lt;test&gt;/evidence.json</c>, which the importer attaches to the test's TRX result.
/// </summary>
public sealed partial class E2ESession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly PlaywrightFixture _fixture;
    private readonly TestMeta _meta;
    private readonly string _folder;
    private readonly List<object> _steps = [];
    private readonly List<object> _artifacts = [];
    private readonly List<string> _consoleErrors = [];
    private readonly List<string> _networkFailures = [];
    private readonly HashSet<string> _expectedErrorPaths = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _startedAt = TimeProvider.System.GetUtcNow();
    private readonly ViewportSize _viewport;
    private bool _completed;
    private bool _failed;
    private int _screenshotIndex;
    private string? _actual;

    public IBrowserContext Context { get; }
    public IPage Page { get; }

    /// <summary>Console errors seen so far (the framework tests assert there are none).</summary>
    public IReadOnlyList<string> ConsoleErrors => _consoleErrors;
    public IReadOnlyList<string> NetworkFailures => _networkFailures;

    private E2ESession(PlaywrightFixture fixture, TestMeta meta, IBrowserContext context, IPage page, string folder, ViewportSize viewport)
    {
        _fixture = fixture;
        _meta = meta;
        Context = context;
        Page = page;
        _folder = folder;
        _viewport = viewport;
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                _consoleErrors.Add($"console.error: {message.Text}");
            }
        };
        page.PageError += (_, error) => _consoleErrors.Add($"page error: {error}");
        page.RequestFailed += (_, request) => _networkFailures.Add($"{request.Method} {Path(request.Url)} failed: {request.Failure}");
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && !_expectedErrorPaths.Contains(Path(response.Url)))
            {
                _networkFailures.Add($"{response.Request.Method} {Path(response.Url)} -> HTTP {response.Status}");
            }
        };
    }

    public static async Task<E2ESession> StartAsync(PlaywrightFixture fixture, TestMeta meta, ViewportSize? viewport = null, bool mobile = false)
    {
        var size = viewport ?? Viewports.Desktop1440;
        var folder = System.IO.Path.Combine(E2ESettings.EvidenceDirectory, Slug(meta.FullName));
        Directory.CreateDirectory(folder);
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = E2ESettings.BaseUrl,
            ViewportSize = size,
            IsMobile = mobile,
            HasTouch = mobile,
            DeviceScaleFactor = mobile ? 2 : 1,
            AcceptDownloads = true,
            Locale = "en-US",
            TimezoneId = "UTC",
            // The app's prefers-reduced-motion rule cuts animations to ~0 ms, so an element that appears late (the status
            // toast fades in after a 60 ms gap) is never scanned by axe mid-fade with blended, mis-measured colours.
            ReducedMotion = ReducedMotion.Reduce,
            // The service worker is exercised by its own test; elsewhere it would hide network events from the recorder.
            ServiceWorkers = ServiceWorkerPolicy.Block,
            RecordVideoDir = E2ESettings.RecordVideo ? System.IO.Path.Combine(folder, "video") : null,
            RecordVideoSize = E2ESettings.RecordVideo ? new RecordVideoSize { Width = size.Width, Height = size.Height } : null,
        });
        context.SetDefaultTimeout((float)E2ESettings.ActionTimeout.TotalMilliseconds);
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Title = meta.Title });
        var page = await context.NewPageAsync();
        return new E2ESession(fixture, meta, context, page, folder, size);
    }

    /// <summary>A response with this path and an error status is part of the scenario (for example a deliberate 404).</summary>
    public void ExpectErrorResponse(string path) => _expectedErrorPaths.Add(path);

    /// <summary>Runs one recorded step. A failure is recorded (with a screenshot) and rethrown, failing the test.</summary>
    public async Task StepAsync(string name, Func<Task> action, string? expected = null, string? actual = null)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await action();
            _steps.Add(new { name, outcome = "Passed", durationMs = watch.ElapsedMilliseconds, expected, actual = actual ?? expected });
        }
        catch (Exception ex)
        {
            _failed = true;
            var message = FirstLine(ex.Message);
            _steps.Add(new { name, outcome = "Failed", durationMs = watch.ElapsedMilliseconds, expected, actual = message, message });
            _actual = $"Step '{name}' failed: {message}";
            await TryScreenshotAsync($"Failure: {name}");
            throw;
        }
    }

    /// <summary>A screenshot kept as evidence (password and SSN inputs are masked).</summary>
    public async Task ScreenshotAsync(string caption) => await TryScreenshotAsync(caption);

    /// <summary>
    /// axe-core scan of the current page with the WCAG 2.0/2.1/2.2 A and AA rule tags. Fails on any violation and lists
    /// each rule, its impact and the first affected elements.
    /// </summary>
    public Task AxeAsync(string pageName) => StepAsync($"Accessibility scan (axe, WCAG A/AA): {pageName}", async () =>
    {
        // Scan the settled page: mid-animation (dialog fade-in) colours are blended and would mis-measure contrast.
        await PageWait.UntilAsync(Page, "() => document.getAnimations().every(a => a.playState !== 'running')");
        var results = await Page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
        });
        var violations = results.Violations ?? [];
        Assert.True(violations.Length == 0, $"axe found {violations.Length} WCAG A/AA violation(s) on {pageName}:\n" + string.Join("\n",
            violations.Select(v => $"- {v.Id} ({v.Impact}): {v.Help} at {string.Join(", ", v.Nodes.Take(3).Select(n => n.Target?.ToString()))}")));
    }, expected: "No WCAG 2.x A or AA violations", actual: "No violations");

    /// <summary>Asserts that keyboard focus is on the element (the E5 focus-management criteria).</summary>
    public static Task ExpectFocusedAsync(ILocator locator) => Assertions.Expect(locator).ToBeFocusedAsync();

    /// <summary>Marks the scenario as finished; a session disposed without this counts as failed.</summary>
    public void Complete(string? actual = null)
    {
        _completed = true;
        _actual ??= actual;
    }

    public async ValueTask DisposeAsync()
    {
        var failed = _failed || !_completed;
        if (failed && _actual is null)
        {
            _actual = "The test stopped before completing its steps (see the failure message).";
        }
        if (failed || E2ESettings.TraceAlways)
        {
            var trace = System.IO.Path.Combine(_folder, "trace.zip");
            try
            {
                await Context.Tracing.StopAsync(new TracingStopOptions { Path = trace });
                _artifacts.Add(new { kind = "Trace", file = "trace.zip", caption = "Playwright trace" });
            }
            catch (PlaywrightException)
            {
            }
        }
        else
        {
            await Context.Tracing.StopAsync();
        }

        var video = Page.Video;
        await Context.CloseAsync();
        if (video is not null)
        {
            try
            {
                var path = await video.PathAsync();
                var target = System.IO.Path.Combine(_folder, "video.webm");
                File.Move(path, target, overwrite: true);
                _artifacts.Add(new { kind = "Video", file = "video.webm", caption = "Recording" });
            }
            catch (Exception ex) when (ex is IOException or PlaywrightException)
            {
            }
        }

        var manifest = new
        {
            fullName = _meta.FullName,
            title = _meta.Title,
            story = _meta.Story,
            requirements = _meta.Requirements,
            priority = _meta.Priority,
            description = _meta.Description,
            preconditions = _meta.Preconditions,
            expected = _meta.Expected,
            acceptanceCriteria = _meta.AcceptanceCriteria,
            actual = _actual ?? _meta.Expected,
            browser = _fixture.BrowserLabel,
            viewport = Viewports.Label(_viewport),
            startedAtUtc = _startedAt,
            finishedAtUtc = TimeProvider.System.GetUtcNow(),
            outcome = failed ? "Failed" : "Passed",
            steps = _steps,
            artifacts = _artifacts,
            consoleErrors = _consoleErrors,
            networkFailures = _networkFailures,
        };
        await File.WriteAllTextAsync(System.IO.Path.Combine(_folder, "evidence.json"), JsonSerializer.Serialize(manifest, Json));
    }

    private async Task TryScreenshotAsync(string caption)
    {
        try
        {
            var file = $"{++_screenshotIndex:00}-{Slug(caption)}.png";
            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = System.IO.Path.Combine(_folder, file),
                FullPage = true,
                Mask = [Page.Locator("input[type=password]"), Page.Locator("#lookup-ssn")],
                Animations = ScreenshotAnimations.Disabled,
            });
            _artifacts.Add(new { kind = "Screenshot", file, caption });
        }
        catch (PlaywrightException)
        {
            // The page may already be gone; the failure itself is still recorded.
        }
    }

    private static string Path(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.Length > 500 ? line[..500] : line;
    }

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    public static string Slug(string text)
    {
        var slug = NonAlphanumeric().Replace(text, "-").Trim('-');
        return slug.Length > 90 ? slug[^90..].Trim('-') : slug;
    }
}
