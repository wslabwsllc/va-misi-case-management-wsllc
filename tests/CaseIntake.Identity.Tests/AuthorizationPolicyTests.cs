using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace CaseIntake.Identity.Tests;

public class AuthorizationPolicyTests
{
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityModule(new ConfigurationBuilder().Build());
        // The default matrix in memory instead of the database (no user rows: role claims only).
        services.AddSingleton<IPermissionStore>(new InMemoryPermissionStore());
        return services.BuildServiceProvider();
    }

    public static TheoryData<string, string> RoleTimesPermission()
    {
        var data = new TheoryData<string, string>();
        foreach (var role in new[] { AppRoles.CaseWorker, AppRoles.Supervisor, "NoSuchRole" })
        {
            foreach (var permission in RolePermissionMap.AllPermissions)
            {
                data.Add(role, permission);
            }
        }
        return data;
    }

    [TestCaseSource(nameof(RoleTimesPermission))]
    public async Task Every_permission_has_a_policy_that_follows_the_matrix(string role, string permission)
    {
        await using var provider = Services();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(Principals.User("u-1", "user", role), resource: null, policyName: permission);

        Assert.Equal(RolePermissionMap.Grants([role], permission), result.Succeeded);
    }

    [Test]
    public async Task Permission_policies_reject_anonymous_users()
    {
        await using var provider = Services();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        foreach (var permission in RolePermissionMap.AllPermissions)
        {
            Assert.False((await authorization.AuthorizeAsync(Principals.Anonymous, null, permission)).Succeeded);
        }
    }

    [Test]
    public async Task The_fallback_policy_requires_an_authenticated_user()
    {
        await using var provider = Services();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        Assert.NotNull(options.FallbackPolicy);
        Assert.False((await authorization.AuthorizeAsync(Principals.Anonymous, null, options.FallbackPolicy)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principals.User("u", "u", "NoSuchRole"), null, options.FallbackPolicy)).Succeeded);
    }

    [Test]
    public async Task A_forbidden_request_logs_AuthorizationDenied_with_user_permission_and_resource()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Cookies").AddCookie("Cookies");
        await using var provider = services.BuildServiceProvider();
        var logger = new FakeLogger<DenialLoggingAuthorizationResultHandler>();
        var handler = new DenialLoggingAuthorizationResultHandler(logger);
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            User = Principals.User("cw-1", "caseworker@example.test", AppRoles.CaseWorker),
        };
        context.Request.Path = "/cases/7/attachments/abc/content";
        var failure = AuthorizationFailure.Failed([new PermissionRequirement(Permissions.AttachmentsDownload)]);
        var nextCalled = false;

        await handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context,
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(), PolicyAuthorizationResult.Forbid(failure));

        Assert.False(nextCalled);
        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(SecurityEvents.AuthorizationDeniedId, record.Id.Id);
        Assert.Equal(SecurityEvents.AuthorizationDeniedName, record.Id.Name);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("cw-1", record.GetStructuredStateValue("UserId"));
        Assert.Equal(Permissions.AttachmentsDownload, record.GetStructuredStateValue("Permission"));
        Assert.Equal("/cases/7/attachments/abc/content", record.GetStructuredStateValue("ResourceId"));
    }

    [Test]
    public async Task Successful_and_challenged_requests_are_not_logged_as_denials()
    {
        var logger = new FakeLogger<DenialLoggingAuthorizationResultHandler>();
        var handler = new DenialLoggingAuthorizationResultHandler(logger);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Cookies").AddCookie("Cookies");
        await using var provider = services.BuildServiceProvider();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        await handler.HandleAsync(_ => Task.CompletedTask, new DefaultHttpContext { RequestServices = provider }, policy, PolicyAuthorizationResult.Success());
        await handler.HandleAsync(_ => Task.CompletedTask, new DefaultHttpContext { RequestServices = provider }, policy, PolicyAuthorizationResult.Challenge());

        Assert.Empty(logger.Collector.GetSnapshot());
    }
}
