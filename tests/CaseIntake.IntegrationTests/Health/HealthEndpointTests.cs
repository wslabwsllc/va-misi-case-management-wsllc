using System.Net;
using System.Text.Json;
using CaseIntake.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace CaseIntake.IntegrationTests.Health;

public class HealthEndpointTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
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

    [Fact]
    public async Task Live_returns_200_even_when_dependencies_are_down()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, veteranLookupBaseUrl: "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_200_only_when_database_and_veteran_lookup_are_reachable()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true);
        await using var _ = stub;
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, url);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Operation-Id"));
    }

    [Fact]
    public async Task Ready_probes_health_under_the_base_url_path_prefix()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true, healthPath: "/lookup/health");
        await using var _ = stub;
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, $"{url}/lookup");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Ready_returns_503_when_veteran_lookup_is_unreachable()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Ready_returns_503_when_veteran_lookup_reports_unhealthy()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: false);
        await using var _ = stub;
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, url);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
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

    [Fact]
    public async Task Ready_reports_each_component_as_json_when_all_are_healthy()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true);
        await using var _ = stub;
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, url);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal(["attachment-storage", "database", "veteran-lookup"],
            body.GetProperty("components").EnumerateObject().Select(p => p.Name).Order());
        Assert.All(body.GetProperty("components").EnumerateObject(), p => Assert.Equal("Healthy", p.Value.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Ready_names_the_down_database_while_other_components_stay_healthy()
    {
        var (stub, url) = await StartStubLookupAsync(healthy: true);
        await using var _ = stub;
        var badDb = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=secret-value;Timeout=1";
        await using var factory = new CaseIntakeWebFactory(badDb, url);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var raw = await response.Content.ReadAsStringAsync();
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", ComponentStatus(body, "database"));
        Assert.Equal("Healthy", ComponentStatus(body, "veteran-lookup"));
        Assert.Equal("Healthy", ComponentStatus(body, "attachment-storage"));
        Assert.DoesNotContain("secret-value", raw);
    }

    [Fact]
    public async Task Ready_names_the_down_veteran_lookup_service()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", ComponentStatus(body, "veteran-lookup"));
        Assert.Equal("Healthy", ComponentStatus(body, "database"));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task Home_page_renders_from_the_real_host()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Case Intake and Triage", html);
    }
}
