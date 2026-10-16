using System.Net;
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

    [Fact]
    public async Task Home_page_renders_from_the_real_host()
    {
        await using var factory = new CaseIntakeWebFactory(database.ConnectionString, "http://127.0.0.1:1");
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Case Intake and Triage", html);
    }
}
