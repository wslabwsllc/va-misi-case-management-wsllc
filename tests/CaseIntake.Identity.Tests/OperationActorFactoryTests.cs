using System.Collections.Immutable;
using System.Security.Claims;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Identity.CurrentUser;

namespace CaseIntake.Identity.Tests;

public class OperationActorFactoryTests
{
    private static OperationActor FromPrincipal(ClaimsPrincipal? principal) =>
        OperationActorFactory.FromPrincipal(principal, InMemoryPermissionStore.DefaultMatrix, null);

    [Test]
    public void Null_or_unauthenticated_principals_are_anonymous_with_no_permissions()
    {
        foreach (var principal in new ClaimsPrincipal?[] { null, Principals.Anonymous })
        {
            var actor = FromPrincipal(principal);

            Assert.False(actor.IsAuthenticated);
            Assert.Null(actor.UserId);
            Assert.Empty(actor.Permissions);
            Assert.All(RolePermissionMap.AllPermissions, p => Assert.False(actor.HasPermission(p)));
        }
    }

    [Test]
    public void An_authenticated_identity_without_a_user_id_is_treated_as_anonymous()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "x"), new Claim(ClaimTypes.Role, AppRoles.Supervisor)], "Test"));

        Assert.False(FromPrincipal(principal).HasPermission(Permissions.CasesView));
        Assert.Null(OperationActorFactory.UserId(principal));
    }

    [Test]
    public void UserId_is_the_Identity_user_id_and_DisplayName_the_user_name()
    {
        var actor = FromPrincipal(Principals.User("id-42", "caseworker@example.test", AppRoles.CaseWorker));

        Assert.Equal("id-42", actor.UserId);
        Assert.Equal("caseworker@example.test", actor.DisplayName);
        Assert.Equal([AppRoles.CaseWorker], actor.Roles);
    }

    [Test]
    public void Permissions_come_from_role_claims_through_the_stored_role_permissions()
    {
        var caseWorker = FromPrincipal(Principals.User("1", "cw", AppRoles.CaseWorker));
        var supervisor = FromPrincipal(Principals.User("2", "sv", AppRoles.Supervisor));

        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.CaseWorker]), caseWorker.Permissions);
        Assert.False(caseWorker.HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
        Assert.True(supervisor.HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
    }

    [Test]
    public void A_changed_role_in_the_store_changes_the_permissions_and_a_custom_role_counts()
    {
        var roles = new Dictionary<string, ImmutableHashSet<string>>(InMemoryPermissionStore.DefaultMatrix)
        {
            [AppRoles.CaseWorker] = [Permissions.CasesView],
            ["Records reviewer"] = [Permissions.AttachmentsDownload],
        };

        var actor = OperationActorFactory.FromPrincipal(Principals.User("1", "cw", AppRoles.CaseWorker, "Records reviewer"), roles, null);

        Assert.Equal([Permissions.AttachmentsDownload, Permissions.CasesView], actor.Permissions.Order());
    }

    [Test]
    public void Stored_overrides_and_the_active_flag_apply()
    {
        var principal = Principals.User("1", "cw", AppRoles.CaseWorker);
        var overrides = new Dictionary<string, PermissionEffect>
        {
            [Permissions.CasesAssign] = PermissionEffect.Allow,
            [Permissions.CasesEdit] = PermissionEffect.Deny,
        };

        var active = OperationActorFactory.FromPrincipal(principal, InMemoryPermissionStore.DefaultMatrix, InMemoryPermissionStore.Access("1", overrides: overrides));
        var inactive = OperationActorFactory.FromPrincipal(principal, InMemoryPermissionStore.DefaultMatrix, InMemoryPermissionStore.Access("1", isActive: false));

        Assert.True(active.HasPermission(Permissions.CasesAssign));
        Assert.False(active.HasPermission(Permissions.CasesEdit));
        Assert.True(active.HasPermission(Permissions.CasesView));
        Assert.True(inactive.IsAuthenticated);
        Assert.Empty(inactive.Permissions);
    }

    [Test]
    public void Permission_claims_on_the_principal_are_ignored()
    {
        var principal = Principals.User("1", "cw", AppRoles.CaseWorker);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("permission", Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));

        Assert.False(FromPrincipal(principal).HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
    }

    [Test]
    public void Several_roles_give_the_union_and_unknown_roles_give_nothing()
    {
        var both = FromPrincipal(Principals.User("1", "x", AppRoles.CaseWorker, AppRoles.Supervisor));
        var unknown = FromPrincipal(Principals.User("2", "y", "NoSuchRole"));

        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.Supervisor]), both.Permissions);
        Assert.True(unknown.IsAuthenticated);
        Assert.Empty(unknown.Permissions);
    }

    [Test]
    public void A_custom_role_claim_type_is_honored()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim("name", "n"), new Claim("role", AppRoles.Supervisor)],
            "Test", "name", "role");

        var actor = FromPrincipal(new ClaimsPrincipal(identity));

        Assert.Equal("n", actor.DisplayName);
        Assert.True(actor.HasPermission(Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed)));
        Assert.Equal([AppRoles.Supervisor], OperationActorFactory.Roles(new ClaimsPrincipal(identity)));
    }

    [Test]
    public void The_snapshot_is_immutable_and_detached_from_the_principal()
    {
        var principal = Principals.User("1", "cw", AppRoles.CaseWorker);
        var actor = FromPrincipal(principal);

        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, AppRoles.Supervisor));

        Assert.IsType<ImmutableHashSet<string>>(actor.Roles);
        Assert.IsType<ImmutableHashSet<string>>(actor.Permissions);
        Assert.DoesNotContain(AppRoles.Supervisor, actor.Roles);
        Assert.False(actor.HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
    }
}

public class PermissionResolverTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Test]
    public async Task Loads_are_cached_until_the_revalidation_interval_passes()
    {
        var store = new InMemoryPermissionStore();
        var time = new ManualTime(Start);
        var resolver = store.Resolver(time);
        var principal = Principals.User("u-1", "u", AppRoles.CaseWorker);

        await resolver.ResolveAsync(principal, CancellationToken.None);
        await resolver.ResolveAsync(principal, CancellationToken.None);
        Assert.Equal((1, 1), (store.RoleLoads, store.UserLoads));

        time.Now = Start + new IdentityModuleOptions().RevalidationInterval;
        await resolver.ResolveAsync(principal, CancellationToken.None);
        Assert.Equal((2, 2), (store.RoleLoads, store.UserLoads));
    }

    [Test]
    public async Task Invalidate_applies_a_stored_change_at_the_next_check()
    {
        var store = new InMemoryPermissionStore();
        var resolver = store.Resolver();
        var principal = Principals.User("u-1", "u", AppRoles.CaseWorker);
        Assert.True((await resolver.ResolveAsync(principal, CancellationToken.None)).HasPermission(Permissions.CasesEdit));

        store.Users["u-1"] = InMemoryPermissionStore.Access("u-1", overrides: new Dictionary<string, PermissionEffect> { [Permissions.CasesEdit] = PermissionEffect.Deny });
        Assert.True((await resolver.ResolveAsync(principal, CancellationToken.None)).HasPermission(Permissions.CasesEdit));   // still cached

        resolver.Invalidate();
        Assert.False((await resolver.ResolveAsync(principal, CancellationToken.None)).HasPermission(Permissions.CasesEdit));
    }

    [Test]
    public async Task A_deactivated_user_loses_every_permission_and_anonymous_needs_no_load()
    {
        var store = new InMemoryPermissionStore();
        store.Users["u-1"] = InMemoryPermissionStore.Access("u-1", isActive: false);
        var resolver = store.Resolver();

        var actor = await resolver.ResolveAsync(Principals.User("u-1", "u", AppRoles.Administrator), CancellationToken.None);
        var anonymous = await resolver.ResolveAsync(Principals.Anonymous, CancellationToken.None);

        Assert.Empty(actor.Permissions);
        Assert.False(anonymous.IsAuthenticated);
        Assert.Equal(1, store.UserLoads);
    }
}
