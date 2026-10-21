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
                // Varied triage (issue #22): every priority, five categories, a third unassigned, a sixth without a due date.
                var triage = new CaseTriage((CasePriority)(i % 4), (CaseCategory)(i % 5),
                    i % 3 == 0 ? null : i % 3 == 1 ? "caseworker@example.test" : "supervisor@example.test",
                    i % 6 == 0 ? null : new DateOnly(2026, 4, 1).AddDays(i % 9 * 30));
                var entity = Case.Create(title, description, null, "seed", created, triage: triage);
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
public class CaseListTests : ClassFixture<CaseListTests, CaseListFixture>
{
    private static CaseListFixture fixture => Shared;

    private readonly CaseTestHarness _harness = new(fixture.Database) { Actor = TestActors.CaseWorker };

    private async Task<PagedResult<CaseSummaryDto>> ListAsync(CaseListQuery query) =>
        CaseTestHarness.Ok(await _harness.Service.ListAsync(query, CancellationToken.None));

    [Test]
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

    [Test]
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

    [TestCaseSource(nameof(SortCombinations))]
    public async Task Every_sort_field_and_direction_orders_with_an_Id_tiebreaker(CaseSortField field, SortDirection direction)
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, SortBy: field, Direction: direction));

        Assert.Equal(Expected(fixture.Cases, field, direction), result.Items.Select(i => i.Id));
    }

    [Test]
    public async Task Status_filter_returns_only_matching_statuses()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Statuses: [CaseStatus.Triaged, CaseStatus.InProgress]));

        Assert.Equal(fixture.Cases.Count(c => c.Status is CaseStatus.Triaged or CaseStatus.InProgress), result.TotalCount);
        Assert.All(result.Items, i => Assert.Contains(i.Status, new[] { CaseStatus.Triaged, CaseStatus.InProgress }));
    }

    [Test]
    public async Task Search_matches_title_or_description_case_insensitively()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Search: "water"));

        var expected = fixture.Cases.Where(c => c.Title.Contains("water", StringComparison.OrdinalIgnoreCase)
                                                || (c.Description?.Contains("water", StringComparison.OrdinalIgnoreCase) ?? false)).Select(c => c.Id).Order();
        Assert.Equal(expected, result.Items.Select(i => i.Id).Order());
        Assert.Contains(result.Items, i => i.Title.StartsWith("Water", StringComparison.Ordinal));
        Assert.Contains(result.Items, i => !i.Title.Contains("Water", StringComparison.Ordinal));   // description-only match
    }

    [TestCase("%", "Discount 100% applied")]
    [TestCase("_", "Upload file_name problem")]
    [TestCase("\\", "Path back\\slash issue")]
    public async Task Wildcards_in_search_text_match_literally(string search, string onlyTitle)
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Search: search));

        Assert.Equal(onlyTitle, Assert.Single(result.Items).Title);
    }

    [Test]
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

    [Test]
    public async Task Blank_search_and_empty_status_list_do_not_filter()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Statuses: [], Search: "   "));

        Assert.Equal(60, result.TotalCount);
    }

    [TestCase(0, 25)]
    [TestCase(1, 0)]
    [TestCase(1, 101)]
    public async Task Invalid_paging_is_a_validation_error(int page, int pageSize)
    {
        var error = CaseTestHarness.Fail(await _harness.Service.ListAsync(new CaseListQuery(page, pageSize), CancellationToken.None), ErrorKind.Validation);

        Assert.Equal("InvalidQuery", error.Code);
    }

    [Test]
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
            CaseSortField.Priority => ascending ? cases.OrderBy(c => c.Priority) : cases.OrderByDescending(c => c.Priority),
            // Cases without a due date come last in both directions.
            CaseSortField.DueOn => ascending
                ? cases.OrderBy(c => c.DueOn is null).ThenBy(c => c.DueOn)
                : cases.OrderBy(c => c.DueOn is null).ThenByDescending(c => c.DueOn),
            CaseSortField.Category => ascending ? cases.OrderBy(c => c.Category) : cases.OrderByDescending(c => c.Category),
            CaseSortField.Owner => ascending
                ? cases.OrderBy(c => c.AssignedTo is null).ThenBy(c => c.AssignedTo, StringComparer.Ordinal)
                : cases.OrderBy(c => c.AssignedTo is null).ThenByDescending(c => c.AssignedTo, StringComparer.Ordinal),
            _ => ascending ? cases.OrderBy(c => c.UpdatedAtUtc) : cases.OrderByDescending(c => c.UpdatedAtUtc),
        };
        return [.. ordered.ThenBy(c => c.Id).Select(c => c.Id)];
    }

    // ---------- Triage filters (issues #21 and #22) ----------

    [Test]
    public async Task Priority_category_and_owner_filters_combine()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, Priorities: [CasePriority.Urgent, CasePriority.High],
            Categories: [CaseCategory.Benefits], AssignedTo: "caseworker@example.test"));

        var expected = fixture.Cases.Where(c => c.Priority is CasePriority.Urgent or CasePriority.High && c.Category == CaseCategory.Benefits
                                                && c.AssignedTo == "caseworker@example.test").Select(c => c.Id).Order().ToList();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, result.Items.Select(i => i.Id).Order());
        Assert.All(result.Items, i => Assert.Equal("caseworker@example.test", i.AssignedTo));
    }

    [Test]
    public async Task Unassigned_filter_selects_cases_without_an_owner()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, AssignedTo: CaseListQuery.UnassignedFilter));

        Assert.Equal(fixture.Cases.Count(c => c.AssignedTo is null), result.TotalCount);
        Assert.All(result.Items, i => Assert.Null(i.AssignedTo));
    }

    [Test]
    public async Task Overdue_filter_keeps_open_cases_due_before_today_utc()
    {
        // The harness clock is 2026-10-16; fixture due dates run from 2026-04-01 to 2026-12-27.
        var today = DateOnly.FromDateTime(CaseTestHarness.StartTime.UtcDateTime);
        var result = await ListAsync(new CaseListQuery(PageSize: 100, OverdueOnly: true));

        var expected = fixture.Cases.Where(c => CaseTriageRules.IsOverdue(c.Status, c.DueOn, today)).Select(c => c.Id).Order().ToList();
        Assert.NotEmpty(expected);
        Assert.True(expected.Count < fixture.Cases.Count(c => c.Status is CaseStatus.New or CaseStatus.Triaged or CaseStatus.InProgress));
        Assert.Equal(expected, result.Items.Select(i => i.Id).Order());
    }

    [Test]
    public async Task Created_and_updated_ranges_are_inclusive()
    {
        var from = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 1, 14, 0, 0, TimeSpan.Zero);
        var created = await ListAsync(new CaseListQuery(PageSize: 100, CreatedFromUtc: from, CreatedToUtc: to));
        var updated = await ListAsync(new CaseListQuery(PageSize: 100, UpdatedFromUtc: from, UpdatedToUtc: to));

        Assert.Equal(fixture.Cases.Where(c => c.CreatedAtUtc >= from && c.CreatedAtUtc <= to).Select(c => c.Id).Order(), created.Items.Select(i => i.Id).Order());
        Assert.Equal(10, created.TotalCount);   // two cases at each of 10:00, 11:00, 12:00, 13:00 and 14:00
        Assert.Equal(fixture.Cases.Where(c => c.UpdatedAtUtc >= from && c.UpdatedAtUtc <= to).Select(c => c.Id).Order(), updated.Items.Select(i => i.Id).Order());
    }

    [Test]
    public async Task Summaries_carry_the_triage_fields()
    {
        var result = await ListAsync(new CaseListQuery(PageSize: 100, SortBy: CaseSortField.Id, Direction: SortDirection.Ascending));

        foreach (var (summary, entity) in result.Items.Zip(fixture.Cases.OrderBy(c => c.Id)))
        {
            Assert.Equal((entity.Priority, entity.Category, entity.AssignedTo, entity.DueOn), (summary.Priority, summary.Category, summary.AssignedTo, summary.DueOn));
        }
    }

    [Test]
    public async Task An_inverted_date_range_or_unknown_priority_is_rejected()
    {
        var inverted = await _harness.Service.ListAsync(new CaseListQuery(CreatedFromUtc: DateTimeOffset.UnixEpoch.AddDays(2), CreatedToUtc: DateTimeOffset.UnixEpoch), CancellationToken.None);
        var unknown = await _harness.Service.ListAsync(new CaseListQuery(Priorities: [(CasePriority)42]), CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, inverted.Error!.Kind);
        Assert.Contains("DateRange", inverted.Error.FieldErrors!.Keys);
        Assert.Contains(nameof(CaseListQuery.Priorities), unknown.Error!.FieldErrors!.Keys);
    }

    [Test]
    public async Task Search_by_case_number_finds_that_case_with_or_without_a_hash()
    {
        var target = fixture.Cases.Where(c => c.Title == "Birch review" && c.Id > 20).OrderBy(c => c.Id).First();

        var plain = await ListAsync(new CaseListQuery(PageSize: 100, Search: target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var hashed = await ListAsync(new CaseListQuery(PageSize: 100, Search: $"#{target.Id}"));

        Assert.Contains(plain.Items, c => c.Id == target.Id);
        Assert.Contains(hashed.Items, c => c.Id == target.Id);
        Assert.Equal(0, (await ListAsync(new CaseListQuery(PageSize: 100, Search: "#999999"))).TotalCount);
    }

    [Test]
    public async Task Due_date_range_keeps_cases_due_inside_it_and_drops_cases_without_a_due_date()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 8, 31);
        var expected = fixture.Cases.Where(c => c.DueOn >= from && c.DueOn <= to).Select(c => c.Id).Order().ToList();
        Assert.NotEmpty(expected);

        var page = await ListAsync(new CaseListQuery(PageSize: 100, DueFrom: from, DueTo: to, SortBy: CaseSortField.Id, Direction: SortDirection.Ascending));

        Assert.Equal(expected, page.Items.Select(c => c.Id));
        Assert.Equal(expected.Count, page.TotalCount);
        var onlyFrom = await ListAsync(new CaseListQuery(PageSize: 100, DueFrom: from));
        Assert.All(onlyFrom.Items, c => Assert.True(c.DueOn >= from));
        var inverted = await _harness.Service.ListAsync(new CaseListQuery(DueFrom: to, DueTo: from), CancellationToken.None);
        Assert.Contains("DateRange", inverted.Error!.FieldErrors!.Keys);
    }

    [Test]
    public async Task Rows_carry_a_description_preview_capped_for_the_list()
    {
        var page = await ListAsync(new CaseListQuery(PageSize: 100));

        foreach (var summary in page.Items)
        {
            var entity = fixture.Cases.Single(c => c.Id == summary.Id);
            Assert.Equal(entity.Description is { Length: > CaseSummaryDto.PreviewLength } d ? d[..CaseSummaryDto.PreviewLength] : entity.Description, summary.DescriptionPreview);
        }
    }
}
