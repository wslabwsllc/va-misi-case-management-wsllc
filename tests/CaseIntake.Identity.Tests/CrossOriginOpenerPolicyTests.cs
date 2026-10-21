using CaseIntake.Identity.Security;
using Microsoft.AspNetCore.Http;

namespace CaseIntake.Identity.Tests;

/// <summary>COOP is sent only where browsers apply it (HTTPS or loopback); elsewhere Chrome logs a console error for it.</summary>
public class CrossOriginOpenerPolicyTests
{
    [TestCase("http", "localhost:8080", true)]
    [TestCase("http", "127.0.0.1:8080", true)]
    [TestCase("http", "[::1]:8080", true)]
    [TestCase("https", "cases.example.test", true)]
    [TestCase("http", "web-e2e:8080", false)]
    [TestCase("http", "192.168.1.20:8080", false)]
    public void Only_trustworthy_origins_get_the_header(string scheme, string host, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);

        Assert.Equal(expected, SecurityHeadersMiddleware.IsTrustworthy(context.Request));
    }
}
