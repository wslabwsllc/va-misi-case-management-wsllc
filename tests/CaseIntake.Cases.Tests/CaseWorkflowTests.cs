using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

public class CaseWorkflowTests
{
    private static readonly HashSet<(CaseStatus, CaseStatus)> Allowed =
    [
        (CaseStatus.New, CaseStatus.Triaged),
        (CaseStatus.Triaged, CaseStatus.InProgress),
        (CaseStatus.InProgress, CaseStatus.Resolved),
        (CaseStatus.Resolved, CaseStatus.Closed),
        (CaseStatus.Resolved, CaseStatus.InProgress),
    ];

    public static TheoryData<CaseStatus, CaseStatus> AllPairs()
    {
        var data = new TheoryData<CaseStatus, CaseStatus>();
        foreach (var from in Enum.GetValues<CaseStatus>())
        {
            foreach (var to in Enum.GetValues<CaseStatus>())
            {
                data.Add(from, to);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Exactly_the_five_specified_edges_are_allowed(CaseStatus from, CaseStatus to)
    {
        var expected = Allowed.Contains((from, to));

        Assert.Equal(expected, CaseWorkflow.Instance.IsAllowed(from, to));
        Assert.Equal(expected, CaseWorkflow.Instance.NextStatuses(from).Contains(to));
    }

    [Fact]
    public void All_25_pairs_are_covered_and_5_are_allowed()
    {
        Assert.Equal(25, AllPairs().Count());
        Assert.Equal(5, AllPairs().Count(row => CaseWorkflow.Instance.IsAllowed((CaseStatus)row[0], (CaseStatus)row[1])));
    }

    [Fact]
    public void Closed_is_terminal_and_new_cases_start_as_New()
    {
        Assert.Empty(CaseWorkflow.Instance.NextStatuses(CaseStatus.Closed));
        Assert.Equal(CaseStatus.New, CaseWorkflow.InitialStatus);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void Undefined_enum_values_are_never_allowed(int value)
    {
        var undefined = (CaseStatus)value;

        Assert.Empty(CaseWorkflow.Instance.NextStatuses(undefined));
        foreach (var status in Enum.GetValues<CaseStatus>())
        {
            Assert.False(CaseWorkflow.Instance.IsAllowed(undefined, status));
            Assert.False(CaseWorkflow.Instance.IsAllowed(status, undefined));
        }
    }

    [Fact]
    public void Only_Resolved_to_InProgress_is_a_reopen()
    {
        foreach (var (from, to) in Allowed)
        {
            var audit = AuditEvent.StatusChange(DateTimeOffset.UnixEpoch, from, to);
            var expected = (from, to) == (CaseStatus.Resolved, CaseStatus.InProgress) ? AuditActions.Reopened : AuditActions.StatusChanged;
            Assert.Equal(expected, audit.Action);
            Assert.Equal(from, audit.FromStatus);
            Assert.Equal(to, audit.ToStatus);
        }
    }
}
