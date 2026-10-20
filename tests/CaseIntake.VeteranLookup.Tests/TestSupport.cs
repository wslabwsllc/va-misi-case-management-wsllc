using System.Collections.Immutable;
using CaseIntake.Contracts.Security;
using CaseIntake.VeteranLookup.Mock;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.VeteranLookup.Tests;

internal static class RepositoryRoot
{
    /// <summary>Directory containing global.json, found by walking up from the test binaries (works locally and in the image).</summary>
    public static string Path { get; } = Find();

    public static string File(params string[] segments) => System.IO.Path.Combine([Path, .. segments]);

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root (global.json).");
    }
}

/// <summary>Committed files that are generated from code. <c>UPDATE_SNAPSHOTS=1</c> rewrites them instead of comparing.</summary>
internal static class Snapshots
{
    public const string Category = "Snapshot";

    public static bool Updating => Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";

    public static void Verify(string expectedContent, params string[] repositoryPath)
    {
        var path = RepositoryRoot.File(repositoryPath);
        var expected = Normalize(expectedContent);
        if (Updating)
        {
            System.IO.File.WriteAllText(path, expected);
            return;
        }

        Assert.True(System.IO.File.Exists(path), $"{string.Join('/', repositoryPath)} is missing; run scripts/update-contract-snapshots.sh");
        var committed = Normalize(System.IO.File.ReadAllText(path));
        Assert.True(expected == committed,
            $"{string.Join('/', repositoryPath)} is out of date with the code; run scripts/update-contract-snapshots.sh and review the diff.");
    }

    private static string Normalize(string text)
    {
        var lf = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        return lf + "\n";
    }
}

/// <summary>The real mock hosted in memory, with captured logs.</summary>
public sealed class MockServiceFactory : WebApplicationFactory<MockProgram>
{
    // Production, like the container: Development rethrows binding failures and would hide production-only behaviour.
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseEnvironment("Production")
        .ConfigureTestServices(services => services.AddFakeLogging());

    public FakeLogCollector Logs => Services.GetFakeLogCollector();
}

/// <summary>Where a lookup service runs: the in-process mock always, plus the Compose container when configured.</summary>
public static class LookupTarget
{
    public const string InProcess = "in-process";
    public const string Compose = "compose";

    public static string? ComposeBaseUrl => Environment.GetEnvironmentVariable("VeteranLookup__BaseUrl") is { Length: > 0 } url ? url : null;

    public static IEnumerable<string> Names() => ComposeBaseUrl is null ? [InProcess] : [InProcess, Compose];

    public static TheoryData<string> All() => [.. Names()];

    public static HttpClient CreateClient(string target, MockServiceFactory factory) => target switch
    {
        InProcess => factory.CreateClient(),
        Compose => new HttpClient { BaseAddress = new Uri(ComposeBaseUrl!), Timeout = TimeSpan.FromSeconds(10) },
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}

internal sealed class TestCurrentUser(params string[] permissions) : ICurrentUser
{
    public OperationActor Actor { get; set; } = new("test-user", "test@example.test",
        ImmutableHashSet.Create(AppRoles.CaseWorker), [.. permissions]);

    public Task<OperationActor> GetAsync(CancellationToken ct) => Task.FromResult(Actor);
}

/// <summary>One request as sent: content type, body, URI and method.</summary>
internal sealed record RecordedRequest(string? ContentType, string Body, Uri? Uri, HttpMethod Method)
{
    /// <summary>Reads the request (buffering its body) and appends it to <paramref name="requests"/> under a lock.</summary>
    public static async Task CaptureAsync(HttpRequestMessage request, List<RecordedRequest> requests, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (requests)
        {
            requests.Add(new(request.Content?.Headers.ContentType?.MediaType, body, request.RequestUri, request.Method));
        }
    }
}

/// <summary>Scripted HTTP responses; records every request body so the wire format can be asserted.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _calls);
        await RecordedRequest.CaptureAsync(request, Requests, cancellationToken);
        return await respond(request, attempt, cancellationToken);
    }
}

/// <summary>
/// Records each attempt (method, URI, content type, body) and passes the request on, so the payload sent to a real
/// service can be asserted. Reading the body buffers it, so the framing (Content-Length rather than chunked) can differ
/// from the production request; the tests assert the payload and headers, not the framing. A new instance is created per
/// handler chain (IHttpClientFactory may build several); they share one list.
/// </summary>
internal sealed class RecordingHandler(List<RecordedRequest> requests) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await RecordedRequest.CaptureAsync(request, requests, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}

internal static class LogAssertions
{
    /// <summary>No captured record's message or structured state contains an SSN or any of the given values.</summary>
    public static void ContainsNoSensitiveValues(FakeLogCollector logs, params string[] values)
    {
        foreach (var record in logs.GetSnapshot())
        {
            var text = record.Message + " " + string.Join(" ", record.StructuredState?.Select(kv => kv.Value) ?? []);
            Assert.False(ServiceDefaults.SsnRedaction.ContainsSsn(text), $"SSN-shaped value logged: {record.Id.Name}");
            foreach (var value in values)
            {
                Assert.DoesNotContain(value, text, StringComparison.Ordinal);
            }
        }
    }

    public static IEnumerable<FakeLogRecord> WithEventId(this FakeLogCollector logs, int id) =>
        logs.GetSnapshot().Where(r => r.Id.Id == id);
}
