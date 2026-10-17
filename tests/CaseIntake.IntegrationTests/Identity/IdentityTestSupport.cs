using System.Net;
using System.Text.RegularExpressions;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Seeding;
using CaseIntake.TestKit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Identity;

internal static partial class IdentityTestSupport
{
    public const string DemoPassword = "Test-Demo-Passw0rd!";

    /// <summary>Services as the migrator composes them for the Identity seed, against <paramref name="connectionString"/>.</summary>
    public static ServiceProvider SeedServices(string connectionString, string? demoPassword = DemoPassword, Action<IServiceCollection>? extra = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CaseIntake"] = connectionString,
            ["Seed:DemoPassword"] = demoPassword,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPersistence(configuration);
        services.AddIdentitySeed();
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public static async Task SeedAsync(string connectionString, string? demoPassword = DemoPassword)
    {
        await using var provider = SeedServices(connectionString, demoPassword);
        await new SeedRunner(provider, new FakeLogger<SeedRunner>()).RunAsync(CancellationToken.None);
    }

    /// <summary>Creates an extra synthetic user through <see cref="UserManager{TUser}"/> (lockout enabled, like the demo users).</summary>
    public static async Task<string> CreateUserAsync(string connectionString, string userName, string password, params string[] roles)
    {
        await using var provider = SeedServices(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = userName, Email = userName, EmailConfirmed = true, LockoutEnabled = true };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        foreach (var role in roles)
        {
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        return user.Id;
    }

    public static string? AntiforgeryToken(string html) =>
        AntiforgeryPattern().Match(html) is { Success: true } m ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();
}

/// <summary>
/// The real web host over a migrated test database holding the seeded demo accounts plus synthetic test accounts:
/// one with no role (no permissions), one for lockout and one for revalidation, so those tests never disturb the others.
/// Circuits and cookies re-check security stamps every second here.
/// </summary>
public sealed class IdentityWebFixture : PostgresDatabaseFixture, IAsyncLifetime
{
    public const string NoPermissionsUser = "nobody@example.test";
    public const string LockoutUser = "lockout@example.test";
    public const string RevalidationUser = "revalidate@example.test";
    public const string LogoutUser = "logout@example.test";

    public CaseIntakeWebFactory Factory { get; private set; } = null!;
    public FakeLogCollector Logs => Factory.Services.GetFakeLogCollector();

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await IdentityTestSupport.SeedAsync(ConnectionString);
        await IdentityTestSupport.CreateUserAsync(ConnectionString, NoPermissionsUser, IdentityTestSupport.DemoPassword);
        await IdentityTestSupport.CreateUserAsync(ConnectionString, LockoutUser, IdentityTestSupport.DemoPassword, AppRoles.CaseWorker);
        await IdentityTestSupport.CreateUserAsync(ConnectionString, RevalidationUser, IdentityTestSupport.DemoPassword, AppRoles.CaseWorker);
        await IdentityTestSupport.CreateUserAsync(ConnectionString, LogoutUser, IdentityTestSupport.DemoPassword, AppRoles.Supervisor);
        Factory = new CaseIntakeWebFactory(ConnectionString, "http://127.0.0.1:1",
            configureServices: services => services.AddFakeLogging(),
            extraConfiguration: new Dictionary<string, string?> { ["Identity:RevalidationInterval"] = "00:00:01" });
    }

    public new async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
        await base.DisposeAsync();
    }

    /// <summary>A cookie-keeping client that does not follow redirects, like a browser whose redirects the test inspects.</summary>
    public BrowserSession NewSession() =>
        new(Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }));
}

/// <summary>Drives the real static SSR login form: fetches it (antiforgery cookie and token), then posts it.</summary>
public sealed class BrowserSession(HttpClient client) : IDisposable
{
    public HttpClient Client => client;

    public async Task<HttpResponseMessage> LoginAsync(string userName, string password, string? returnUrl = null)
    {
        var url = returnUrl is null ? "/account/login" : $"/account/login?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
        var token = IdentityTestSupport.AntiforgeryToken(await client.GetStringAsync(url));
        Assert.NotNull(token);
        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token!,
            ["Input.Email"] = userName,
            ["Input.Password"] = password,
        }));
    }

    public async Task SignInAsync(string userName, string password = IdentityTestSupport.DemoPassword)
    {
        using var response = await LoginAsync(userName, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>Signs out the way the header's form does: a post with the antiforgery token from an app page.</summary>
    public async Task<HttpResponseMessage> LogoutAsync()
    {
        var token = IdentityTestSupport.AntiforgeryToken(await client.GetStringAsync("/"));
        Assert.NotNull(token);
        return await client.PostAsync("/account/logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token! }));
    }

    public void Dispose() => client.Dispose();
}
