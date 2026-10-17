using CaseIntake.Identity.Account;
using CaseIntake.Identity.Security;
using Microsoft.AspNetCore.Http;

namespace CaseIntake.Identity.Tests;

public class RequestSecurityTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/cases/12", "/cases/12")]
    [InlineData("/cases?status=New&page=2", "/cases?status=New&page=2")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("cases", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/ok\\..\\x", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("/x\r\nLocation: https://evil.example", "/")]
    public void Return_urls_must_be_local(string? returnUrl, string expected)
    {
        Assert.Equal(expected, LocalReturnUrl.Sanitize(returnUrl));
    }

    [Fact]
    public void Csp_allows_own_scripts_and_the_same_host_SignalR_connection_only()
    {
        var csp = SecurityHeadersMiddleware.ContentSecurityPolicy(new HostString("localhost:8080"));
        var directives = csp.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToDictionary(d => d.Split(' ')[0], d => d.Split(' ').Skip(1).ToArray());

        Assert.Equal(["'self'"], directives["script-src"]);
        Assert.Equal(["'self'", "ws://localhost:8080", "wss://localhost:8080"], directives["connect-src"]);
        Assert.Equal(["'self'"], directives["default-src"]);
        Assert.Equal(["'none'"], directives["object-src"]);
        Assert.Equal(["'none'"], directives["frame-ancestors"]);
        Assert.Equal(["'self'"], directives["form-action"]);
        Assert.DoesNotContain("'unsafe-eval'", csp);
        Assert.DoesNotContain("'unsafe-inline'", string.Join(' ', directives["script-src"]));
        Assert.DoesNotContain("upgrade-insecure-requests", csp);
    }

    [Theory]
    [InlineData("127.0.0.1:8080")]
    [InlineData("[::1]:8080")]
    [InlineData("web")]
    public void Well_formed_hosts_are_added_to_connect_src(string host)
    {
        Assert.Contains($"ws://{host} ", SecurityHeadersMiddleware.ContentSecurityPolicy(new HostString(host)) + " ");
    }

    [Theory]
    [InlineData("evil.example; script-src *")]
    [InlineData("a b")]
    [InlineData("")]
    public void Malformed_hosts_never_reach_the_policy(string host)
    {
        var csp = SecurityHeadersMiddleware.ContentSecurityPolicy(new HostString(host));

        Assert.EndsWith("connect-src 'self'", csp);
        Assert.DoesNotContain("script-src *", csp);
    }
}
