using System.Security.Claims;
using CaseIntake.Contracts.Security;
using CaseIntake.Web.Components.Account.Shared;
using CaseIntake.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>
/// E3-S5: the access-denied content the router shows during interactive navigation (no HTTP authorization middleware
/// runs there) explains the denial and logs <c>AuthorizationDenied</c> itself; the HTTP page variant does not log twice.
/// </summary>
public class AccessDeniedMessageTests
{
    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string uri) => Initialize("http://localhost/", uri);
    }

    private static async Task<(string Html, FakeLogCollector Logs)> RenderAsync(ClaimsPrincipal user, Type? pageType)
    {
        var services = new ServiceCollection();
        services.AddFakeLogging();
        services.AddSingleton<NavigationManager>(new TestNavigationManager("http://localhost/cases/5?tab=audit"));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment content = builder =>
            {
                builder.OpenComponent<AccessDeniedMessage>(0);
                builder.AddComponentParameter(1, nameof(AccessDeniedMessage.PageType), pageType);
                builder.CloseComponent();
            };
            var output = await renderer.RenderComponentAsync<CascadingValue<Task<AuthenticationState>>>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(CascadingValue<object>.Value)] = Task.FromResult(new AuthenticationState(user)),
                    [nameof(CascadingValue<object>.ChildContent)] = content,
                }));
            return output.ToHtmlString();
        });
        return (html, provider.GetFakeLogCollector());
    }

    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "user-7"), new Claim(ClaimTypes.Name, "u@example.test"),
         .. roles.Select(r => new Claim(ClaimTypes.Role, r))], "Identity.Application"));

    [Fact]
    public async Task Interactive_denial_renders_an_accessible_message_and_logs_user_permission_and_resource()
    {
        var (html, logs) = await RenderAsync(User(), typeof(Home));

        Assert.Contains("<h1 tabindex=\"-1\">Access denied</h1>", html);
        Assert.Contains("Back to the case list", html);
        var denial = Assert.Single(logs.GetSnapshot());
        Assert.Equal(SecurityEvents.AuthorizationDeniedId, denial.Id.Id);
        Assert.Equal(SecurityEvents.AuthorizationDeniedName, denial.Id.Name);
        Assert.Equal("user-7", denial.GetStructuredStateValue("UserId"));
        Assert.Equal(Permissions.CasesView, denial.GetStructuredStateValue("Permission"));
        Assert.Equal("/cases/5?tab=audit", denial.GetStructuredStateValue("ResourceId"));
    }

    [Fact]
    public async Task The_HTTP_access_denied_page_variant_does_not_log_again()
    {
        var (html, logs) = await RenderAsync(User(), pageType: null);

        Assert.Contains("Access denied", html);
        Assert.Empty(logs.GetSnapshot());
    }
}
