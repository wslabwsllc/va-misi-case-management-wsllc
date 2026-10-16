using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>A database holding exactly 60 deterministic cases, shared read-only by <see cref="CaseListTests"/>.</summary>
public sealed class CaseListFixture : IAsyncLifetime
{
    // Words without shared prefixes, so ordinal and database collation orders agree.
    private static readonly string[] Words = ["Amber", "Birch", "Cedar", "Delta", "Ember", "Falcon", "Water"];

    public PostgresDatabaseFixture Database { get; } = new();

    public IReadOnlyList<Case> Cases { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        var baseTime = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        await using (var db = Database.CreateContext())
        {
            for (var i = 0; i < 60; i++)
            {
                var title = i switch
                {
                    7 => "Discount 100% applied",
                    8 => "Upload file_name problem",
                    9 => "Path back\\slash issue",
                    _ => $"{Words[i % Words.Length]} review",   // repeated titles exercise the Id tiebreaker
                };
                var description = i % 10 == 3 ? "Basement WATER heater leak" : i % 4 == 0 ? null : "Routine synthetic note.";
                var created = baseTime.AddHours(i / 2);                     // pairs share CreatedAt
                var entity = Case.Create(title, description, null, "seed", created);
                var at = created;
                if (i % 3 == 0)
                {
                    at = created.AddHours(i % 7 + 1);   // varied UpdatedAt with ties
                    entity.Edit(title, (description ?? "") + " Updated.", "seed", at);
                }
                var target = (CaseStatus)(i % 5);
                var path = new[] { CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Closed }.Take((int)target);
                foreach (var status in path)
                {
                    entity.TransitionTo(status, CaseWorkflow.Instance, "seed", at.AddMinutes(1));
                }
                db.Cases.Add(entity);
                await db.SaveChangesAsync();
            }
        }

        await using var read = Database.CreateContext();
        Cases = await read.Cases.AsNoTracking().ToListAsync();
    }

    public Task DisposeAsync() => Database.DisposeAsync();
}

/// <summary>E2-S5: paging, filtering and allowlisted sorting run in the database.</summary>
public class CaseListTests(CaseListFixture fixture) : IClassFixture<CaseListFixture>
{
    private readonly CaseTestHarness _harness = new(fixture.Database) { Actor = TestActors.CaseWorker };

    private async Task<PagedResult<CaseSummaryDto>> ListAsync(CaseListQuery query) =>
        CaseTestHarness.Ok(await _harness.Service.ListAsync(query, CancellationToken.None));

    [Fact]
    public async Task Pages_slice_the_ordered_results_and_report_the_total()
    {
        var expected = Expected(fixture.Cases, CaseSortField.UpdatedAt, SortDirection.Descending);

        var page2 = await ListAsync(new CaseListQuery(Page: 2, PageSize: 25));
        var page3 = await ListAsync(new CaseListQuery(Page: 3, PageSize: 25));
        var page4 = await ListAsync(new CaseListQuery(Page: 4, PageSize: 25));

        Assert.Equal(60, page2.TotalCount);
        Assert.Equal((2, 25), (page2.Page, page2.PageSize));
        Assert.Equal(expected.Skip(25).Take(25), page2.Items.Select(i => i.Id));   // items 26-50
        Assert.Equal(10, page3.Items.Count);
        Assert.Equal(expected.Skip(50), page3.Items.Select(i => i.Id));
        Assert.Empty(page4.Items);
        Assert.Equal(60, page4.TotalCount);
    }

    [Fact]
    public async Task Default_query_is_25_per_page_by_UpdatedAt_descending()
    {
        var result = await ListAsync(new CaseListQuery());

        Assert.Equal(25, result.Items.Count);
        Assert.Equal(Expected(fixture.Cases, CaseSortField.UpdatedAt, SortDirection.Descending).Take(25), result.Items.Select(i => i.Id));
    }

    public static TheoryData<CaseSortField, SortDirection> SortCombinations()
    {
        var data = new TheoryData<CaseSortField, SortDirection>();
        foreach (var field in Enum.GetValues<CaseSortField>())
        {
            foreach (var direction in Enum.GetValues<SortDirection>())
            {
                data.Add(field, direction);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SortCombinations))]
    public async Task Every_sort_field_and_direction_orders_with_an_Id_tiebreaker(CaseSortField field, SortDirection direction)
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, SortBy: field, Direction: direction));

        Assert.Equal(Expected(fixture.Cases, field, direction), result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Status_filter_returns_only_matching_statuses()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Statuses: [CaseStatus.Triaged, CaseStatus.InProgress]));

        Assert.Equal(fixture.Cases.Count(c => c.Status is CaseStatus.Triaged or CaseStatus.InProgress), result.TotalCount);
        Assert.All(result.Items, i => Assert.Contains(i.Status, new[] { CaseStatus.Triaged, CaseStatus.InProgress }));
    }

    [Fact]
    public async Task Search_matches_title_or_description_case_insensitively()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Search: "water"));

        var expected = fixture.Cases.Where(c => c.Title.Contains("water", StringComparison.OrdinalIgnoreCase)
                                                || (c.Description?.Contains("water", StringComparison.OrdinalIgnoreCase) ?? false)).Select(c => c.Id).Order();
        Assert.Equal(expected, result.Items.Select(i => i.Id).Order());
        Assert.Contains(result.Items, i => i.Title.StartsWith("Water", StringComparison.Ordinal));
        Assert.Contains(result.Items, i => !i.Title.Contains("Water", StringComparison.Ordinal));   // description-only match
    }

    [Theory]
    [InlineData("%", "Discount 100% applied")]
    [InlineData("_", "Upload file_name problem")]
    [InlineData("\\", "Path back\\slash issue")]
    public async Task Wildcards_in_search_text_match_literally(string search, string onlyTitle)
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Search: search));

        Assert.Equal(onlyTitle, Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task Filters_and_sort_combine_before_paging()
    {
        var query = new CaseListQuery(Page: 2, PageSize: 3, Statuses: [CaseStatus.New, CaseStatus.Closed], Search: "review",
            SortBy: CaseSortField.Title, Direction: SortDirection.Ascending);
        var matching = fixture.Cases.Where(c => c.Status is CaseStatus.New or CaseStatus.Closed
                                                && (c.Title.Contains("review", StringComparison.OrdinalIgnoreCase)
                                                    || (c.Description?.Contains("review", StringComparison.OrdinalIgnoreCase) ?? false))).ToList();

        var result = await ListAsync(query);

        Assert.Equal(matching.Count, result.TotalCount);
        Assert.Equal(Expected(matching, CaseSortField.Title, SortDirection.Ascending).Skip(3).Take(3), result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Blank_search_and_empty_status_list_do_not_filter()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Statuses: [], Search: "   "));

        Assert.Equal(60, result.TotalCount);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_paging_is_a_validation_error(int page, int pageSize)
    {
        var error = CaseTestHarness.Fail(await _harness.Service.ListAsync(new CaseListQuery(page, pageSize), CancellationToken.None), ErrorKind.Validation);

        Assert.Equal("InvalidQuery", error.Code);
    }

    [Fact]
    public async Task Listing_requires_view_permission()
    {
        _harness.Actor = TestActors.NoPermissions;

        CaseTestHarness.Fail(await _harness.Service.ListAsync(new CaseListQuery(), CancellationToken.None), ErrorKind.Forbidden);
    }

    private static List<long> Expected(IEnumerable<Case> cases, CaseSortField field, SortDirection direction)
    {
        var ascending = direction == SortDirection.Ascending;
        IOrderedEnumerable<Case> ordered = field switch
        {
            CaseSortField.Id => ascending ? cases.OrderBy(c => c.Id) : cases.OrderByDescending(c => c.Id),
            CaseSortField.Title => ascending ? cases.OrderBy(c => c.Title, StringComparer.Ordinal) : cases.OrderByDescending(c => c.Title, StringComparer.Ordinal),
            CaseSortField.Status => ascending ? cases.OrderBy(c => c.Status) : cases.OrderByDescending(c => c.Status),
            CaseSortField.CreatedAt => ascending ? cases.OrderBy(c => c.CreatedAtUtc) : cases.OrderByDescending(c => c.CreatedAtUtc),
            _ => ascending ? cases.OrderBy(c => c.UpdatedAtUtc) : cases.OrderByDescending(c => c.UpdatedAtUtc),
        };
        return [.. ordered.ThenBy(c => c.Id).Select(c => c.Id)];
    }
}
