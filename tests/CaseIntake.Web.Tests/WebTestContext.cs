using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit.Fakes;
using CaseIntake.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CaseIntake.Web.Tests;

/// <summary>
/// bUnit context with the E5 UI services and TestKit fakes. <see cref="SignInAs"/> configures the bUnit authorization
/// context (identity, role claim and the E3 policies the role's permissions grant, from the production
/// <see cref="RolePermissionMap"/>) and the matching <see cref="ICurrentUser"/>, so AuthorizeView and the services agree.
/// </summary>
public abstract class WebTestContext : BunitContext
{
    /// <summary>Configuration seen by components; the sign-in page lists demo accounts only when Seed:DemoPassword is set.</summary>
    protected Microsoft.Extensions.Configuration.ConfigurationManager Configuration { get; } = new();

    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    protected FakeCurrentUser CurrentUser { get; } = new();
    protected FakeVeteranLookupClient Lookup { get; } = new();
    protected BunitAuthorizationContext Auth { get; }

    /// <summary>The application JS module (wwwroot/js/interop.js); calls such as focusSelector are verified here.</summary>
    protected BunitJSModuleInterop InteropModule { get; }

    protected WebTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        InteropModule = JSInterop.SetupModule(UiInterop.ModulePath);
        InteropModule.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddSingleton<ICurrentUser>(CurrentUser);
        Services.AddSingleton<InMemoryCaseService>();
        Services.AddSingleton<ScriptedCaseService>();
        Services.AddSingleton<ICaseService>(sp => sp.GetRequiredService<ScriptedCaseService>());
        Services.AddSingleton<ICaseAssignmentService>(sp => sp.GetRequiredService<InMemoryCaseService>());
        Services.AddSingleton<InMemoryAssigneeDirectory>();
        Services.AddSingleton<ICaseAssigneeDirectory>(sp => sp.GetRequiredService<InMemoryAssigneeDirectory>());
        Services.AddSingleton<FakeCaseNotificationService>();
        Services.AddSingleton<ICaseNotificationService>(sp => sp.GetRequiredService<FakeCaseNotificationService>());
        Services.AddSingleton<CaseActivitySignal>();
        Services.AddScoped<PageActivity>();
        Services.AddSingleton<FakeTestResultsService>();
        Services.AddSingleton<FakeUserAdministrationService>();
        Services.AddSingleton<IUserAdministrationService>(sp => sp.GetRequiredService<FakeUserAdministrationService>());
        Services.AddSingleton<Contracts.TestResults.ITestResultsService>(sp => sp.GetRequiredService<FakeTestResultsService>());
        // The sign-in page injects AccountSignIn; presentation tests never submit, so an uninitialized instance suffices.
        Services.AddScoped(_ => (Identity.Account.AccountSignIn)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Identity.Account.AccountSignIn)));
        Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(Configuration);
        Services.AddSingleton<IAttachmentService, InMemoryAttachmentService>();
        Services.AddSingleton<IVeteranLookupClient>(Lookup);
        Services.AddSingleton<ICaseWorkflow>(CaseWorkflow.Instance);
        Services.AddSingleton<AntiforgeryStateProvider, TestAntiforgeryStateProvider>();
        Services.AddSingleton(Options.Create(new DashboardOptions()));
        Services.AddSingleton(Options.Create(new Attachments.AttachmentOptions
        {
            MaxBytes = 1024,
            AllowedTypes = [new() { ContentType = "text/plain", Extensions = [".txt"] }, new() { ContentType = "application/pdf", Extensions = [".pdf"] }],
        }));
        Services.AddScoped<StatusAnnouncer>();
        Services.AddScoped<ShellState>();
        Services.AddScoped<UiInterop>();
        Services.AddScoped<CaseDashboardService>();
        Auth = AddAuthorization();
        // Last: setting the renderer info builds the service provider, after which nothing can be registered.
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    protected ScriptedCaseService CaseService => Services.GetRequiredService<ScriptedCaseService>();
    protected FakeCaseNotificationService NotificationFeed => Services.GetRequiredService<FakeCaseNotificationService>();
    protected FakeTestResultsService TestResults => Services.GetRequiredService<FakeTestResultsService>();
    protected FakeUserAdministrationService UserAdmin => Services.GetRequiredService<FakeUserAdministrationService>();
    protected StatusAnnouncer Announcer => Services.GetRequiredService<StatusAnnouncer>();
    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    /// <summary>Signs in a user with <paramref name="role"/>: bUnit identity, role claim, E3 policies and ICurrentUser.</summary>
    protected void SignInAs(string role, string? userName = null)
    {
        var name = userName ?? $"{role.ToLowerInvariant()}@example.test";
        Auth.SetAuthorized(name);
        Auth.SetRoles(role);
        Auth.SetClaims(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, $"id-{role}"));
        Auth.SetPolicies([.. RolePermissionMap.PermissionsFor([role])]);
        CurrentUser.Actor = FakeCurrentUser.ForRole(role, $"id-{role}");
    }

    /// <summary>Adds cases through the fake service as a temporary all-permissions actor, leaving the signed-in actor unchanged.</summary>
    protected async Task<CaseDetailDto> SeedCaseAsync(string title, CaseStatus status = CaseStatus.New, string? description = null, CaseTriage? triage = null)
    {
        var actor = CurrentUser.Actor;
        CurrentUser.Actor = FakeCurrentUser.Authenticated("seed", "seed", AppRoles.Supervisor, FakeCurrentUser.AllPermissions);
        try
        {
            var created = (await CaseService.CreateAsync(new CreateCaseRequest(title, description, null, triage), CancellationToken.None)).Value!;
            CaseStatus[] path = status switch
            {
                CaseStatus.Triaged => [CaseStatus.Triaged],
                CaseStatus.InProgress => [CaseStatus.Triaged, CaseStatus.InProgress],
                CaseStatus.Resolved => [CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved],
                CaseStatus.Closed => [CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Closed],
                _ => [],
            };
            foreach (var target in path)
            {
                created = (await CaseService.ChangeStatusAsync(created.Id, new ChangeStatusRequest(target, created.Version), CancellationToken.None)).Value!;
            }
            return created;
        }
        finally
        {
            CurrentUser.Actor = actor;
        }
    }

    private sealed class TestAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
