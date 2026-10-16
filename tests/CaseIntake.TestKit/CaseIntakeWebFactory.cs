using CaseIntake.TestKit.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CaseIntake.TestKit;

/// <summary>
/// Hosts the real web app against a per-fixture test database. Call <see cref="WebApplicationFactory{TEntryPoint}.UseKestrel(int)"/>
/// (inherited) before first use for real-socket browser tests.
/// </summary>
public sealed class CaseIntakeWebFactory(
    string connectionString,
    string? veteranLookupBaseUrl = null,
    Action<IServiceCollection>? configureServices = null,
    bool useTestAuthentication = false,
    IReadOnlyDictionary<string, string?>? extraConfiguration = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?> { ["ConnectionStrings:CaseIntake"] = connectionString };
            if (veteranLookupBaseUrl is not null)
            {
                settings["VeteranLookup:BaseUrl"] = veteranLookupBaseUrl;
            }
            if (extraConfiguration is not null)
            {
                foreach (var (key, value) in extraConfiguration)
                {
                    settings[key] = value;
                }
            }
            config.AddInMemoryCollection(settings);
        });

        // Program.cs reads the connection string while registering services, before ConfigureAppConfiguration
        // callbacks are applied under minimal hosting; host settings are visible from the start.
        builder.UseSetting($"ConnectionStrings:{Persistence.PersistenceServiceCollectionExtensions.ConnectionStringName}", connectionString);

        builder.ConfigureTestServices(services =>
        {
            if (useTestAuthentication)
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            }
            configureServices?.Invoke(services);
        });
    }
}
