using System.Net;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity;
using CaseIntake.Identity.Seeding;
using CaseIntake.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>E3-S1 and E3-S6 through the real web host, real Identity, real cookies and the real login form.</summary>
public class AuthenticationTests(IdentityWebFixture fixture) : IClassFixture<IdentityWebFixture>
{
    private const string LoginFailureMessage =
        "Sign-in failed. Check your email and password and try again. After five failed attempts, sign-in is blocked for five minutes.";

    private static string? AuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(IdentityModule.AuthCookieName + "=", StringComparison.Ordinal))
            : null;

    [Fact]
    public async Task Unauthenticated_page_requests_redirect_to_login()
    {
        using var session = fixture.NewSession();

        using var response = await session.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost/account/login?ReturnUrl=%2F", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("caseworker@example.test", AppRoles.CaseWorker)]
    [InlineData("supervisor@example.test", AppRoles.Supervisor)]
    public async Task Valid_seeded_credentials_sign_in_redirect_to_the_case_list_and_show_name_and_role(string userName, string role)
    {
        using var session = fixture.NewSession();

        using var login = await session.LoginAsync(userName, IdentityTestSupport.DemoPassword);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("http://localhost/", login.Headers.Location!.ToString());
        var cookie = AuthCookie(login);
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        using var home = await session.Client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains($"data-testid=\"user-name\">{userName}<", html);
        Assert.Contains($"data-testid=\"user-roles\">{AppRoles.DisplayName(role)}<", html);
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 3001 && Equals(r.GetStructuredStateValue("UserId"),
            userName == "caseworker@example.test" ? DemoUsers.CaseWorker.Id : DemoUsers.Supervisor.Id));
    }

    [Theory]
    [InlineData("/?status=New", "/?status=New")]
    [InlineData("https://evil.example/phish", "/")]
    [InlineData("//evil.example/phish", "/")]
    [InlineData("/\\evil.example/phish", "/")]
    public async Task Login_redirects_only_to_local_return_urls(string returnUrl, string expectedLocation)
    {
        using var session = fixture.NewSession();

        using var login = await session.LoginAsync("caseworker@example.test", IdentityTestSupport.DemoPassword, returnUrl);

        // Blazor writes an absolute same-origin Location; anything non-local falls back to the case list.
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(new Uri("http://localhost" + expectedLocation), login.Headers.Location);
    }

    [Theory]
    [InlineData("caseworker@example.test", "Wrong-Passw0rd-1", true)]
    [InlineData("nobody-here@example.test", "Unknown-Passw0rd-2", false)]
    public async Task Invalid_credentials_show_one_generic_focused_error_and_log_LoginFailed_without_the_password(
        string userName, string password, bool accountExists)
    {
        using var session = fixture.NewSession();

        using var response = await session.LoginAsync(userName, password);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(AuthCookie(response));
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("autofocus", html);
        Assert.Contains(LoginFailureMessage, html);
        Assert.DoesNotContain(password, html);

        var logs = fixture.Logs.GetSnapshot();
        var failed = logs.Last(r => r.Id.Id == 3002);
        Assert.Equal("LoginFailed", failed.Id.Name);
        Assert.Equal(accountExists ? DemoUsers.CaseWorker.Id : null, failed.GetStructuredStateValue("UserId"));
        Assert.All(logs, r =>
        {
            Assert.DoesNotContain(password, r.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(password, string.Join("|", r.StructuredState?.Select(s => s.Value) ?? []), StringComparison.Ordinal);
        });
        if (!accountExists)
        {
            Assert.All(logs, r => Assert.DoesNotContain(userName, r.Message, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Signing_out_clears_the_cookie_and_protected_pages_redirect_to_login_again()
    {
        using var session = fixture.NewSession();
        await session.SignInAsync(IdentityWebFixture.LogoutUser);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/")).StatusCode);

        using var logout = await session.LogoutAsync();

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/account/login?signedOut=true", logout.Headers.Location!.OriginalString);
        var cleared = AuthCookie(logout);
        Assert.NotNull(cleared);
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
        using var after = await session.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.StartsWith("http://localhost/account/login", after.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 3004);
    }

    [Fact]
    public async Task A_login_post_without_an_antiforgery_token_is_rejected()
    {
        using var session = fixture.NewSession();
        await session.Client.GetAsync("/account/login");   // antiforgery cookie present, token missing from the form

        using var response = await session.Client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.Email"] = "caseworker@example.test",
            ["Input.Password"] = IdentityTestSupport.DemoPassword,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(AuthCookie(response));
    }

    [Fact]
    public async Task A_logout_post_without_an_antiforgery_token_is_rejected_and_the_user_stays_signed_in()
    {
        using var session = fixture.NewSession();
        await session.SignInAsync("supervisor@example.test");

        using var response = await session.Client.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(AuthCookie(response));
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account_for_five_minutes_and_a_correct_password_is_then_rejected()
    {
        using var session = fixture.NewSession();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failed = await session.LoginAsync(IdentityWebFixture.LockoutUser, $"Wrong-Passw0rd-{attempt}");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
            Assert.Null(AuthCookie(failed));
        }

        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == IdentityWebFixture.LockoutUser);
            var remaining = user.LockoutEnd!.Value - TimeProvider.System.GetUtcNow();
            Assert.InRange(remaining, TimeSpan.FromMinutes(4.5), TimeSpan.FromMinutes(5));
        }

        using var correct = await session.LoginAsync(IdentityWebFixture.LockoutUser, IdentityTestSupport.DemoPassword);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Null(AuthCookie(correct));
        Assert.Contains(LoginFailureMessage, await correct.Content.ReadAsStringAsync());
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 3003 && r.Id.Name == "LoginLockedOut");
    }

    [Theory]
    [InlineData("/account/login")]
    [InlineData("/")]
    [InlineData("/app.css")]
    [InlineData("/health/live")]
    public async Task Every_response_carries_the_baseline_security_headers(string path)
    {
        using var session = fixture.NewSession();

        using var response = await session.Client.GetAsync(path);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("connect-src 'self' ws://localhost wss://localhost", csp);
    }

    /// <summary>
    /// The framework script itself is served from published static files ahead of authorization; this host runs in the
    /// Testing environment without static web assets, so the anonymous script fetch is checked against the published app
    /// (docs/verification.md) rather than here.
    /// </summary>
    [Fact]
    public async Task The_login_page_is_reachable_anonymously_with_labeled_fields()
    {
        using var session = fixture.NewSession();

        using var page = await session.Client.GetAsync("/account/login");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Matches("<label class=\"field__label\" for=\"login-email\">\\s*Email", html);
        Assert.Matches("<label class=\"field__label\" for=\"login-password\">\\s*Password", html);
        Assert.Contains("autocomplete=\"current-password\"", html);
        Assert.Matches(@"_framework/blazor\.web(\.[a-z0-9]+)?\.js", html);   // fingerprinted by MapStaticAssets
        Assert.DoesNotContain("<!--Blazor:", html);   // static SSR: no interactive component markers on the account page
    }

    [Fact]
    public async Task The_interactive_hub_rejects_anonymous_connections_without_redirecting()
    {
        using var session = fixture.NewSession();

        using var negotiate = await session.Client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);
    }

    [Fact]
    public async Task The_auth_cookie_survives_a_new_host_sharing_the_data_protection_keys()
    {
        // Keys on a shared directory (the 'dataprotection' volume in Compose) let a recreated web container read cookies.
        var keys = Directory.CreateTempSubdirectory("caseintake-keys-");
        try
        {
            var settings = new Dictionary<string, string?> { ["DataProtection:KeysPath"] = keys.FullName };
            string cookie;
            await using (var first = new TestKit.CaseIntakeWebFactory(fixture.ConnectionString, "http://127.0.0.1:1", extraConfiguration: settings))
            {
                using var session = new BrowserSession(first.CreateClient(new() { AllowAutoRedirect = false }));
                using var login = await session.LoginAsync("caseworker@example.test", IdentityTestSupport.DemoPassword);
                cookie = AuthCookie(login)!.Split(';')[0];
            }

            await using var second = new TestKit.CaseIntakeWebFactory(fixture.ConnectionString, "http://127.0.0.1:1", extraConfiguration: settings);
            using var client = second.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Lockout_options_match_the_baseline()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value;

        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Lockout.DefaultLockoutTimeSpan);
        Assert.True(options.Lockout.AllowedForNewUsers);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
    }
}
