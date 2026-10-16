using CaseIntake.Cases.Seeding;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

public class CaseSeedDataTests
{
    [Fact]
    public void Fixtures_are_deterministic()
    {
        var first = CaseSeedData.Build();
        var second = CaseSeedData.Build();

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i] with { Steps = [] }, second[i] with { Steps = [] });
            Assert.Equal(first[i].Steps, second[i].Steps);
        }
    }

    [Fact]
    public void Fixtures_cover_every_status_with_more_than_two_default_pages()
    {
        var finalStatuses = CaseSeedData.Build().Select(FinalStatus).ToList();

        Assert.True(finalStatuses.Count >= 60);
        Assert.Equal(Enum.GetValues<CaseStatus>().Order(), finalStatuses.Distinct().Order());
    }

    [Fact]
    public void Histories_follow_the_workflow_role_matrix_and_move_forward_in_time()
    {
        foreach (var fixture in CaseSeedData.Build())
        {
            Assert.True(CaseTextRules.ValidateCase(CaseTextRules.NormalizeTitle(fixture.Title), fixture.Description).Count == 0, fixture.Key);
            Assert.Equal(TimeSpan.Zero, fixture.CreatedAtUtc.Offset);

            var status = CaseWorkflow.InitialStatus;
            var at = fixture.CreatedAtUtc;
            foreach (var step in fixture.Steps)
            {
                Assert.True(step.AtUtc > at, fixture.Key);
                at = step.AtUtc;
                if (step.TargetStatus is { } target)
                {
                    Assert.True(CaseWorkflow.Instance.IsAllowed(status, target), $"{fixture.Key}: {status}->{target}");
                    var supervisorOnly = (status, target) is (CaseStatus.New, CaseStatus.Triaged) or (CaseStatus.Resolved, _);
                    Assert.Equal(supervisorOnly ? CaseSeedData.SupervisorUserName : CaseSeedData.CaseWorkerUserName, step.ActorUserName);
                    status = target;
                }
            }
        }
    }

    [Fact]
    public void Some_fixtures_are_reopened_and_some_match_the_water_search()
    {
        var fixtures = CaseSeedData.Build();

        Assert.Contains(fixtures, f => f.Steps.Count(s => s.TargetStatus == CaseStatus.InProgress) == 2);
        Assert.Contains(fixtures, f => f.Title.Contains("water", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fixture_text_is_clearly_synthetic()
    {
        foreach (var fixture in CaseSeedData.Build())
        {
            Assert.DoesNotMatch(@"\d{3}-?\d{2}-?\d{4}", fixture.Title + fixture.Description);
            Assert.EndsWith("@example.test", fixture.CreatorUserName, StringComparison.Ordinal);
        }
    }

    private static CaseStatus FinalStatus(SeedCase fixture) =>
        fixture.Steps.LastOrDefault(s => s.TargetStatus is not null)?.TargetStatus ?? CaseWorkflow.InitialStatus;
}
