using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Pages.Cases;

namespace CaseIntake.Web.Tests;

/// <summary>
/// What each case role sees on the case list and detail pages, using the real <see cref="RolePermissionMap"/> rather
/// than hand-picked permissions: Auditor is read-only, IntakeClerk opens cases and attaches documents, CaseWorker edits
/// and moves them.
/// </summary>
[Trait("Category", "Req:R-4.2-6")]
public class RoleCapabilityTests : WebTestContext
{
    [Theory]
    [InlineData(AppRoles.Auditor, false)]
    [InlineData(AppRoles.IntakeClerk, true)]
    [InlineData(AppRoles.CaseWorker, true)]
    public void New_case_command_follows_the_role(string role, bool shown)
    {
        SignInAs(role);

        var cut = Render<CaseList>();

        Assert.Equal(shown ? 1 : 0, cut.FindAll("[data-testid=new-case]").Count);
    }

    [Theory]
    [InlineData(AppRoles.Auditor, false, false, false)]
    [InlineData(AppRoles.IntakeClerk, false, false, true)]
    [InlineData(AppRoles.CaseWorker, true, true, true)]
    public async Task Case_detail_controls_follow_the_role(string role, bool canEdit, bool canMove, bool canUpload)
    {
        var seeded = await SeedCaseAsync("Role capability case", CaseStatus.Triaged);
        SignInAs(role);

        var cut = Render<CaseDetail>(p => p.Add(c => c.Id, seeded.Id));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=case-title]")));
        Assert.Equal(canEdit, cut.FindAll("[data-testid=edit-case]").Count == 1);
        Assert.Equal(canMove, cut.FindAll("[data-testid=transition-InProgress]").Count == 1);
        Assert.Equal(!canMove, cut.FindAll("[data-testid=no-transitions]").Count == 1);
        Assert.Equal(canUpload, cut.FindAll("[data-testid=upload-form]").Count == 1);
        Assert.NotEmpty(cut.FindAll("[data-testid=audit-list] li"));   // every role reads the audit trail
    }
}
