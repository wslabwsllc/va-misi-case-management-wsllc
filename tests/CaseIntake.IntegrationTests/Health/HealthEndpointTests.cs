using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CaseIntake.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace CaseIntake.IntegrationTests.Health;

public class HealthEndpointTests : ClassFixture<HealthEndpointTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    private static async Task<(WebApplication App, string Url)> StartStubLookupAsync(bool healthy, string healthPath = "/health")
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapGet(healthPath, () => healthy ? Results.Ok() : Results.StatusCode(503));
        await app.StartAsync();
        return (app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    [Test]
    public async Task Live_returns_200_even_when_dependencies_are_down()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, veteranLookupBaseUrl: "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Test]
    public async Task Ready_returns_200_only_when_database_and_veteran_lookup_are_reachable()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true);
        await using var _ = stub;
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, url);
        using var client = factory.CreateClient();

        var ready = await ProbeReadyAsync(client, r => r.Status == HttpStatusCode.OK);

        Assert.True(ready.Status == HttpStatusCode.OK, ready.Raw);
        Assert.True(ready.HasOperationId);
    }

    [Test]
    public async Task Ready_probes_health_under_the_base_url_path_prefix()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true, healthPath: "/lookup/health");
        await using var _ = stub;
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, $"{url}/lookup");
        using var client = factory.CreateClient();

        var ready = await ProbeReadyAsync(client, r => r.Status == HttpStatusCode.OK);

        Assert.True(ready.Status == HttpStatusCode.OK, ready.Raw);
    }

    [Test]
    public async Task Ready_returns_503_when_veteran_lookup_is_unreachable()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Test]
    public async Task Ready_returns_503_when_veteran_lookup_reports_unhealthy()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: false);
        await using var _ = stub;
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, url);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Test]
    public async Task Ready_returns_503_when_database_is_unreachable()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true);
        await using var _ = stub;
        var badDb = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";
        await using var factory = new CaseIntakeWebFactory(badDb, url);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string ComponentStatus(JsonElement body, string name) =>
        body.GetProperty("components").GetProperty(name).GetProperty("status").GetString()!;

    private sealed record ReadyProbe(HttpStatusCode Status, string Raw, JsonElement Body, bool HasOperationId)
    {
        public bool ComponentIs(string name, string status) =>
            Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("components", out var components)
            && components.TryGetProperty(name, out var component) && component.GetProperty("status").GetString() == status;
    }

    /// <summary>
    /// Probes /health/ready until <paramref name="settled"/> holds or 30 s pass, and returns the last probe. The
    /// production checks allow 3 s for the lookup probe and 5 s per check; under linux/amd64 emulation a cold first probe
    /// can exceed that although every dependency is up, which is why the Compose healthcheck retries too. Use it for
    /// components expected to be healthy; a component that is really down stays down however often it is probed.
    /// </summary>
    private static async Task<ReadyProbe> ProbeReadyAsync(HttpClient client, Func<ReadyProbe, bool> settled)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            using var response = await client.GetAsync("/health/ready");
            var raw = await response.Content.ReadAsStringAsync();
            var body = response.Content.Headers.ContentType?.MediaType == "application/json"
                ? JsonDocument.Parse(raw).RootElement.Clone()
                : default;
            var probe = new ReadyProbe(response.StatusCode, raw, body, response.Headers.Contains("X-Operation-Id"));
            if (settled(probe) || stopwatch.Elapsed > TimeSpan.FromSeconds(30))
            {
                return probe;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    [Test]
    public async Task Ready_reports_each_component_as_json_when_all_are_healthy()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true);
        await using var _ = stub;
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, url);
        using var client = factory.CreateClient();

        var ready = await ProbeReadyAsync(client, r => r.Status == HttpStatusCode.OK);
        var body = ready.Body;

        Assert.True(ready.Status == HttpStatusCode.OK, ready.Raw);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal(["attachment-storage", "database", "veteran-lookup"],
            body.GetProperty("components").EnumerateObject().Select(p => p.Name).Order());
        Assert.All(body.GetProperty("components").EnumerateObject(), p => Assert.Equal("Healthy", p.Value.GetProperty("status").GetString()));
    }

    [Test]
    public async Task Ready_names_the_down_database_while_other_components_stay_healthy()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true);
        await using var _ = stub;
        var badDb = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=secret-value;Timeout=1";
        await using var factory = new CaseIntakeWebFactory(badDb, url);
        using var client = factory.CreateClient();

        var ready = await ProbeReadyAsync(client, r => r.ComponentIs("veteran-lookup", "Healthy") && r.ComponentIs("attachment-storage", "Healthy"));
        var body = ready.Body;

        Assert.True(ready.Status == HttpStatusCode.ServiceUnavailable, ready.Raw);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", ComponentStatus(body, "database"));
        Assert.Equal("Healthy", ComponentStatus(body, "veteran-lookup"));
        Assert.Equal("Healthy", ComponentStatus(body, "attachment-storage"));
        Assert.DoesNotContain("secret-value", ready.Raw);
    }

    [Test]
    public async Task Ready_names_the_down_veteran_lookup_service()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        var ready = await ProbeReadyAsync(client, r => r.ComponentIs("database", "Healthy"));
        var body = ready.Body;

        Assert.True(ready.Status == HttpStatusCode.ServiceUnavailable, ready.Raw);
        Assert.Equal("Unhealthy", ComponentStatus(body, "veteran-lookup"));
        Assert.Equal("Healthy", ComponentStatus(body, "database"));
    }

    [Test]
    public async Task Ready_returns_503_when_attachment_storage_is_not_writable()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true);
        await using var _ = stub;
        // A regular file where the storage folder should be: the tmp/ folder cannot be created under it.
        var notADirectory = Path.GetTempFileName();
        try
        {
            await using var factory = new CaseIntakeWebFactory(database.ConnectionString, url,
                extraConfiguration: new Dictionary<string, string?> { ["Attachments:RootPath"] = notADirectory });
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/health/ready");
            var body = await ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("Unhealthy", ComponentStatus(body, "attachment-storage"));
            Assert.Equal("Healthy", ComponentStatus(body, "database"));
        }
        finally
        {
            File.Delete(notADirectory);
        }
    }

    [Test]
    public async Task Health_runs_every_check_and_live_runs_none()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        using var all = await client.GetAsync("/health");
        var allBody = await ReadJsonAsync(all);
        using var live = await client.GetAsync("/health/live");
        var liveBody = await ReadJsonAsync(live);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, all.StatusCode);
        Assert.Equal("Unhealthy", ComponentStatus(allBody, "veteran-lookup"));
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", liveBody.GetProperty("status").GetString());
        Assert.Empty(liveBody.GetProperty("components").EnumerateObject());
    }

    [Test]
    public async Task Home_page_renders_from_the_real_host()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Case Intake and Triage", html);
    }
}
