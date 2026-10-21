using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit.Fakes;

namespace CaseIntake.Cases.Tests;

/// <summary>Who may change a case's owner: supervisors assign anyone; case workers take or hand back; nobody on Closed.</summary>
[Category("Story:Issue-22")]
public class CaseAssignmentRulesTests
{
    private const string Me = "caseworker@example.test";
    private const string Other = "supervisor@example.test";

    private static OperationActor Role(string role) => FakeCurrentUser.ForRole(role);

    [TestCase(AppRoles.Supervisor, null, AssignmentRight.AssignAnyone)]
    [TestCase(AppRoles.Supervisor, Other, AssignmentRight.AssignAnyone)]
    [TestCase(AppRoles.CaseWorker, null, AssignmentRight.Take)]
    [TestCase(AppRoles.CaseWorker, Me, AssignmentRight.HandBack)]
    [TestCase(AppRoles.CaseWorker, Other, AssignmentRight.None)]
    [TestCase(AppRoles.IntakeClerk, null, AssignmentRight.None)]
    [TestCase(AppRoles.Auditor, null, AssignmentRight.None)]
    [TestCase(AppRoles.QualityAnalyst, null, AssignmentRight.None)]
    public void Right_follows_the_role_and_the_current_owner(string role, string? owner, AssignmentRight expected)
    {
        Assert.Equal(expected, CaseAssignmentRules.RightFor(Role(role), Me, CaseStatus.InProgress, owner));
    }

    [Test]
    public void Nobody_reassigns_a_closed_case_and_anonymous_users_have_no_right()
    {
        Assert.Equal(AssignmentRight.None, CaseAssignmentRules.RightFor(Role(AppRoles.Supervisor), Other, CaseStatus.Closed, null));
        Assert.Equal(AssignmentRight.None, CaseAssignmentRules.RightFor(FakeCurrentUser.Anonymous, null, CaseStatus.New, null));
    }

    [TestCase(AssignmentRight.AssignAnyone, Other, "anyone@example.test", true)]
    [TestCase(AssignmentRight.AssignAnyone, Other, null, true)]
    [TestCase(AssignmentRight.Take, null, Me, true)]
    [TestCase(AssignmentRight.Take, null, Other, false)]        // taking is for yourself only
    [TestCase(AssignmentRight.HandBack, Me, null, true)]
    [TestCase(AssignmentRight.HandBack, Me, Other, false)]      // handing back cannot pass the case to someone else
    [TestCase(AssignmentRight.None, null, Me, false)]
    public void Allows_only_the_moves_each_right_covers(AssignmentRight right, string? current, string? target, bool allowed)
    {
        Assert.Equal(allowed, CaseAssignmentRules.Allows(right, Me, current, target));
    }

    [Test]
    public void Assign_changes_the_owner_once_and_reports_the_previous_one()
    {
        var created = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var entity = Case.Create("Assign me please", null, null, "creator", created);

        Assert.Equal((true, (string?)null), entity.Assign("  " + Me + " ", "supervisor", created.AddHours(1)));
        Assert.Equal(Me, entity.AssignedTo);
        Assert.Equal(created.AddHours(1), entity.UpdatedAtUtc);
        Assert.Equal((false, (string?)Me), entity.Assign(Me, "supervisor", created.AddHours(2)));   // no change: untouched
        Assert.Equal(created.AddHours(1), entity.UpdatedAtUtc);
        Assert.Equal((true, (string?)Me), entity.Assign("", "supervisor", created.AddHours(3)));    // empty = unassigned
        Assert.Null(entity.AssignedTo);
    }

    [TestCase(null, "caseworker@example.test", "Assigned to caseworker@example.test")]
    [TestCase("a@example.test", "b@example.test", "Assigned to b@example.test (was a@example.test)")]
    [TestCase("a@example.test", null, "Unassigned (was a@example.test)")]
    public void Assigned_audit_details_name_the_new_and_previous_owner(string? from, string? to, string details)
    {
        var e = AuditEvent.Assigned(DateTimeOffset.UnixEpoch, from, to);
        Assert.Equal((AuditActions.Assigned, details), (e.Action, e.Details));
    }
}
