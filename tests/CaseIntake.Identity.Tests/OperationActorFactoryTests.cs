using System.Collections.Immutable;
using System.Security.Claims;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Identity.CurrentUser;

namespace CaseIntake.Identity.Tests;

public class OperationActorFactoryTests
{
    [Fact]
    public void Null_or_unauthenticated_principals_are_anonymous_with_no_permissions()
    {
        foreach (var principal in new ClaimsPrincipal?[] { null, Principals.Anonymous })
        {
            var actor = OperationActorFactory.FromPrincipal(principal);

            Assert.False(actor.IsAuthenticated);
            Assert.Null(actor.UserId);
            Assert.Empty(actor.Permissions);
            Assert.All(RolePermissionMap.AllPermissions, p => Assert.False(actor.HasPermission(p)));
        }
    }

    [Fact]
    public void An_authenticated_identity_without_a_user_id_is_treated_as_anonymous()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "x"), new Claim(ClaimTypes.Role, AppRoles.Supervisor)], "Test"));

        Assert.False(OperationActorFactory.FromPrincipal(principal).HasPermission(Permissions.CasesView));
    }

    [Fact]
    public void UserId_is_the_Identity_user_id_and_DisplayName_the_user_name()
    {
        var actor = OperationActorFactory.FromPrincipal(Principals.User("id-42", "caseworker@example.test", AppRoles.CaseWorker));

        Assert.Equal("id-42", actor.UserId);
        Assert.Equal("caseworker@example.test", actor.DisplayName);
        Assert.Equal([AppRoles.CaseWorker], actor.Roles);
    }

    [Fact]
    public void Permissions_come_from_role_claims_through_the_matrix()
    {
        var caseWorker = OperationActorFactory.FromPrincipal(Principals.User("1", "cw", AppRoles.CaseWorker));
        var supervisor = OperationActorFactory.FromPrincipal(Principals.User("2", "sv", AppRoles.Supervisor));

        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.CaseWorker]), caseWorker.Permissions);
        Assert.False(caseWorker.HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
        Assert.True(supervisor.HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
    }

    [Fact]
    public void Permission_claims_on_the_principal_are_ignored()
    {
        var principal = Principals.User("1", "cw", AppRoles.CaseWorker);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("permission", Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));

        Assert.False(OperationActorFactory.FromPrincipal(principal).HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
    }

    [Fact]
    public void Several_roles_give_the_union_and_unknown_roles_give_nothing()
    {
        var both = OperationActorFactory.FromPrincipal(Principals.User("1", "x", AppRoles.CaseWorker, AppRoles.Supervisor));
        var unknown = OperationActorFactory.FromPrincipal(Principals.User("2", "y", "Administrator"));

        Assert.Equal(RolePermissionMap.PermissionsFor([AppRoles.Supervisor]), both.Permissions);
        Assert.True(unknown.IsAuthenticated);
        Assert.Empty(unknown.Permissions);
    }

    [Fact]
    public void A_custom_role_claim_type_is_honored()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim("name", "n"), new Claim("role", AppRoles.Supervisor)],
            "Test", "name", "role");

        var actor = OperationActorFactory.FromPrincipal(new ClaimsPrincipal(identity));

        Assert.Equal("n", actor.DisplayName);
        Assert.True(actor.HasPermission(Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed)));
    }

    [Fact]
    public void The_snapshot_is_immutable_and_detached_from_the_principal()
    {
        var principal = Principals.User("1", "cw", AppRoles.CaseWorker);
        var actor = OperationActorFactory.FromPrincipal(principal);

        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, AppRoles.Supervisor));

        Assert.IsType<ImmutableHashSet<string>>(actor.Roles);
        Assert.IsType<ImmutableHashSet<string>>(actor.Permissions);
        Assert.DoesNotContain(AppRoles.Supervisor, actor.Roles);
        Assert.False(actor.HasPermission(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged)));
    }
}
