using System.Net;
using System.Security.Claims;
using CaseIntake.Cases;
using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Identity;
using CaseIntake.Identity.CurrentUser;
using CaseIntake.Identity.Seeding;
using CaseIntake.IntegrationTests.Cases;
using CaseIntake.Persistence;
using CaseIntake.TestKit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>E3-S5: permissions are enforced on pages, endpoints and services, and every denial is logged.</summary>
public class AuthorizationEnforcementTests : ClassFixture<AuthorizationEnforcementTests, IdentityWebFixture>
{
    private static IdentityWebFixture fixture => Shared;

    [Test]
    public async Task A_page_needing_a_permission_the_user_lacks_shows_an_accessible_access_denied_page_with_403()
    {
        using var session = fixture.NewSession();
        await session.SignInAsync(IdentityWebFixture.NoPermissionsUser);

        using var redirect = await session.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("http://localhost/account/access-denied?ReturnUrl=%2F", redirect.Headers.Location!.ToString());

        using var page = await session.Client.GetAsync(redirect.Headers.Location);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.Contains("<h1 tabindex=\"-1\">Access denied</h1>", html);
        Assert.Contains("Back to the case list", html);

        var denial = fixture.Logs.GetSnapshot().Last(r => r.Id.Id == SecurityEvents.AuthorizationDeniedId);
        Assert.Equal(SecurityEvents.AuthorizationDeniedName, denial.Id.Name);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, denial.Level);
        Assert.Equal(Permissions.CasesView, denial.GetStructuredStateValue("Permission"));
        Assert.Equal("/", denial.GetStructuredStateValue("ResourceId"));
        Assert.NotNull(denial.GetStructuredStateValue("UserId"));
    }

    [Test]
    public async Task Users_with_the_permission_reach_the_page()
    {
        using var session = fixture.NewSession();
        await session.SignInAsync("caseworker@example.test");

        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/")).StatusCode);
    }

    [Test]
    public async Task The_composition_root_registers_the_circuit_adapter_by_default_and_the_HTTP_adapter_by_key()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();

        Assert.IsType<CircuitCurrentUser>(scope.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.IsType<HttpCurrentUser>(scope.ServiceProvider.GetRequiredKeyedService<ICurrentUser>(CurrentUserSources.Http));
    }

    /// <summary>
    /// The attachment download endpoint belongs to E4, so this hosts an endpoint configured the way E4 must configure it
    /// (<c>RequireAuthorization(Permissions.AttachmentsDownload).DisableCookieRedirect()</c>, actor from the keyed HTTP
    /// adapter) on the real Identity module, real cookies and real users.
    /// </summary>
    [Test]
    public async Task A_download_style_endpoint_returns_401_anonymous_403_without_the_permission_and_200_with_it()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["ConnectionStrings:CaseIntake"] = fixture.ConnectionString;
        builder.Services.AddFakeLogging();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/test/sign-in", async (string userName, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
        {
            await signIn.SignInAsync((await users.FindByNameAsync(userName))!, isPersistent: false);
            return Results.Ok();
        }).AllowAnonymous();
        app.MapGet("/cases/{caseId:long}/attachments/{id:guid}/content", async (
                long caseId, Guid id, [FromKeyedServices(CurrentUserSources.Http)] ICurrentUser currentUser, CancellationToken ct) =>
            {
                var actor = await currentUser.GetAsync(ct);
                return actor.HasPermission(Permissions.AttachmentsDownload) ? Results.Text(actor.UserId) : Results.Forbid();
            })
            .RequireAuthorization(Permissions.AttachmentsDownload)
            .DisableCookieRedirect();
        app.MapGet("/page-like", () => "page");
        await app.StartAsync();

        var url = AttachmentRoutes.Content(7, Guid.NewGuid());
        async Task<HttpResponseMessage> GetAs(string? userName, string path)
        {
            var client = app.GetTestClient();
            if (userName is not null)
            {
                using var signIn = await client.GetAsync($"/test/sign-in?userName={Uri.EscapeDataString(userName)}");
                var cookie = signIn.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(IdentityModule.AuthCookieName, StringComparison.Ordinal));
                client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
            }
            return await client.GetAsync(path);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAs(null, url)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await GetAs(null, "/page-like")).StatusCode);   // pages still redirect to login
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAs(IdentityWebFixture.NoPermissionsUser, url)).StatusCode);

        var caseWorker = await GetAs("caseworker@example.test", url);
        Assert.Equal(HttpStatusCode.OK, caseWorker.StatusCode);
        Assert.Equal(DemoUsers.CaseWorker.Id, await caseWorker.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await GetAs("supervisor@example.test", url)).StatusCode);

        var denial = Assert.Single(app.Services.GetFakeLogCollector().GetSnapshot(), r => r.Id.Id == SecurityEvents.AuthorizationDeniedId);
        Assert.Equal(Permissions.AttachmentsDownload, denial.GetStructuredStateValue("Permission"));
        Assert.Equal(url, denial.GetStructuredStateValue("ResourceId"));
    }
}

/// <summary>
/// E3-S5 service matrix: every role (and a principal with no permissions) against every protected <see cref="ICaseService"/>
/// operation, with actors built by the production principal → actor path. Services deny independently of the UI.
/// </summary>
public class ServiceAuthorizationMatrixTests : ClassFixture<ServiceAuthorizationMatrixTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    public const string NoPermissions = "NoPermissions";

    public enum Operation { Create, Get, List, AuditTrail, Edit, Triage, Start, Resolve, Close, Reopen }

    private static readonly Dictionary<Operation, (string Permission, CaseStatus Setup)> Operations = new()
    {
        [Operation.Create] = (Permissions.CasesCreate, CaseStatus.New),
        [Operation.Get] = (Permissions.CasesView, CaseStatus.New),
        [Operation.List] = (Permissions.CasesView, CaseStatus.New),
        [Operation.AuditTrail] = (Permissions.CasesView, CaseStatus.New),
        [Operation.Edit] = (Permissions.CasesEdit, CaseStatus.New),
        [Operation.Triage] = (Permissions.Transition(CaseStatus.New, CaseStatus.Triaged), CaseStatus.New),
        [Operation.Start] = (Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress), CaseStatus.Triaged),
        [Operation.Resolve] = (Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved), CaseStatus.InProgress),
        [Operation.Close] = (Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed), CaseStatus.Resolved),
        [Operation.Reopen] = (Permissions.Transition(CaseStatus.Resolved, CaseStatus.InProgress), CaseStatus.Resolved),
    };

    public static TheoryData<string, Operation> Matrix()
    {
        var data = new TheoryData<string, Operation>();
        foreach (var role in new[] { AppRoles.CaseWorker, AppRoles.Supervisor, NoPermissions })
        {
            foreach (var operation in Enum.GetValues<Operation>())
            {
                data.Add(role, operation);
            }
        }
        return data;
    }

    private static readonly IReadOnlyDictionary<string, System.Collections.Immutable.ImmutableHashSet<string>> DefaultMatrix =
        RolePermissionMap.Roles.ToDictionary(r => r, r => RolePermissionMap.PermissionsFor([r]));

    private static OperationActor ActorFor(string role)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, $"id-{role}"), new(ClaimTypes.Name, $"{role}@example.test") };
        if (role != NoPermissions)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        return OperationActorFactory.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, "Identity.Application")), DefaultMatrix, null);
    }

    private static readonly Dictionary<CaseStatus, CaseStatus[]> PathTo = new()
    {
        [CaseStatus.New] = [],
        [CaseStatus.Triaged] = [CaseStatus.Triaged],
        [CaseStatus.InProgress] = [CaseStatus.Triaged, CaseStatus.InProgress],
        [CaseStatus.Resolved] = [CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved],
    };

    [TestCaseSource(nameof(Matrix))]
    public async Task Each_role_is_allowed_exactly_the_operations_its_permissions_cover(string role, Operation operation)
    {
        var harness = new CaseTestHarness(database);
        var (permission, setup) = Operations[operation];
        var detail = await harness.MoveAsync(await harness.CreateAsync(), PathTo[setup]);
        var auditBefore = await harness.CountAuditAsync(detail.Id);
        var actor = ActorFor(role);
        harness.Actor = actor;
        var ct = CancellationToken.None;

        Error? error = operation switch
        {
            Operation.Create => (await harness.Service.CreateAsync(Builders.CreateCase(), ct)).Error,
            Operation.Get => (await harness.Service.GetAsync(detail.Id, ct)).Error,
            Operation.List => (await harness.Service.ListAsync(new CaseListQuery(), ct)).Error,
            Operation.AuditTrail => (await harness.Service.GetAuditTrailAsync(detail.Id, ct)).Error,
            Operation.Edit => (await harness.Service.UpdateAsync(detail.Id, new UpdateCaseRequest("Edited synthetic title", null, detail.Version), ct)).Error,
            Operation.Triage => (await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(CaseStatus.Triaged, detail.Version), ct)).Error,
            Operation.Start or Operation.Reopen => (await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(CaseStatus.InProgress, detail.Version), ct)).Error,
            Operation.Resolve => (await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(CaseStatus.Resolved, detail.Version), ct)).Error,
            Operation.Close => (await harness.Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(CaseStatus.Closed, detail.Version), ct)).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        if (RolePermissionMap.Grants(actor.Roles, permission))
        {
            Assert.Null(error);
            return;
        }

        Assert.Equal(ErrorKind.Forbidden, error?.Kind);
        Assert.Equal(auditBefore, await harness.CountAuditAsync(detail.Id));
        var unchanged = await harness.LoadAsync(detail.Id);
        Assert.Equal(detail.Version, unchanged.Version);
        var denial = Assert.Single(harness.Logs.Collector.GetSnapshot(), r => r.Id.Id == SecurityEvents.AuthorizationDeniedId);
        Assert.Equal(SecurityEvents.AuthorizationDeniedName, denial.Id.Name);
        Assert.Equal(actor.UserId, denial.GetStructuredStateValue("UserId"));
        // Status changes check cases.view before the transition permission, so a principal without it is denied on that.
        var isStatusChange = operation is Operation.Triage or Operation.Start or Operation.Resolve or Operation.Close or Operation.Reopen;
        var expectedPermission = isStatusChange && !actor.HasPermission(Permissions.CasesView) ? Permissions.CasesView : permission;
        Assert.Equal(expectedPermission, denial.GetStructuredStateValue("Permission"));
        if (operation is not (Operation.Create or Operation.List))
        {
            Assert.Equal(detail.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), denial.GetStructuredStateValue("ResourceId"));
        }
    }

    [Test]
    public async Task A_case_worker_cannot_triage_a_new_case_through_the_service()
    {
        var harness = new CaseTestHarness(database);
        var created = await harness.CreateAsync();
        harness.Actor = ActorFor(AppRoles.CaseWorker);

        var result = await harness.Service.ChangeStatusAsync(created.Id, new ChangeStatusRequest(CaseStatus.Triaged, created.Version), CancellationToken.None);

        CaseTestHarness.Fail(result, ErrorKind.Forbidden);
        Assert.Equal(CaseStatus.New, (await harness.LoadAsync(created.Id)).Status);
    }
}
