using System.Globalization;
using CaseIntake.Contracts.Cases;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Web.Components.Pages.Cases;

/// <summary>
/// The case list view state as it appears in the URL (E5-S3: reload or share the URL to restore the view). Parsing is
/// lenient: unknown or out-of-range values fall back to the defaults (25 per page, Updated descending, OQ-4) instead of
/// failing the page; the service validates the final query again. Issue #21/#22 added priority, category, owner,
/// overdue and created/updated date-range filters (dates are inclusive Eastern days, yyyy-MM-dd).
/// </summary>
public sealed record CaseListState(
    int Page,
    int PageSize,
    IReadOnlyList<CaseStatus> Statuses,
    string? Search,
    CaseSortField SortBy,
    SortDirection Direction)
{
    public static readonly int[] PageSizes = [10, 25, 50];
    public const int DefaultPageSize = 25;

    public IReadOnlyList<CasePriority> Priorities { get; init; } = [];
    public IReadOnlyList<CaseCategory> Categories { get; init; } = [];

    /// <summary>Owner user name, or <see cref="CaseListQuery.UnassignedFilter"/>.</summary>
    public string? Owner { get; init; }
    public bool Overdue { get; init; }
    public DateOnly? CreatedFrom { get; init; }
    public DateOnly? CreatedTo { get; init; }
    public DateOnly? UpdatedFrom { get; init; }
    public DateOnly? UpdatedTo { get; init; }

    public static CaseListState Default { get; } = new(1, DefaultPageSize, [], null, CaseSortField.UpdatedAt, SortDirection.Descending);

    /// <summary>True when any filter beyond status and search is set (the "more filters" are shown as chips).</summary>
    public bool HasExtraFilters => Priorities.Count > 0 || Categories.Count > 0 || Owner is not null || Overdue
        || CreatedFrom is not null || CreatedTo is not null || UpdatedFrom is not null || UpdatedTo is not null;

    public static CaseListState Parse(int? page, int? size, string[]? statuses, string? search, string? sort, string? dir)
    {
        var sortBy = Enum.TryParse<CaseSortField>(sort, ignoreCase: true, out var field) && Enum.IsDefined(field) ? field : Default.SortBy;
        var direction = dir?.ToLowerInvariant() switch
        {
            "asc" => SortDirection.Ascending,
            "desc" => SortDirection.Descending,
            _ => sort is null ? Default.Direction : DefaultDirectionFor(sortBy),
        };
        var trimmed = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (trimmed is { Length: > CaseFieldLimits.SearchMaxLength })
        {
            trimmed = trimmed[..CaseFieldLimits.SearchMaxLength];
        }
        return new CaseListState(
            Page: page is >= 1 and <= 100_000 ? page.Value : 1,
            PageSize: size is { } s && PageSizes.Contains(s) ? s : DefaultPageSize,
            Statuses: ParseEnums<CaseStatus>(statuses),
            Search: trimmed,
            SortBy: sortBy,
            Direction: direction);
    }

    /// <summary>Parses every list parameter from a query-string accessor (values of a repeated key in order).</summary>
    public static CaseListState Parse(Func<string, string[]> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string? First(string key) => query(key).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        int? Int(string key) => int.TryParse(First(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        var owner = First("owner")?.Trim();
        return Parse(Int("page"), Int("size"), query("status"), First("q"), First("sort"), First("dir")) with
        {
            Priorities = ParseEnums<CasePriority>(query("priority")),
            Categories = ParseEnums<CaseCategory>(query("category")),
            Owner = owner is { Length: > 0 and <= CaseFieldLimits.ActorMaxLength } ? owner : null,
            Overdue = string.Equals(First("overdue"), "true", StringComparison.OrdinalIgnoreCase),
            CreatedFrom = Date(First("createdFrom")),
            CreatedTo = Date(First("createdTo")),
            UpdatedFrom = Date(First("updatedFrom")),
            UpdatedTo = Date(First("updatedTo")),
        };
    }

    private static List<T> ParseEnums<T>(string[]? values) where T : struct, Enum =>
        [.. (values ?? [])
            .Select(s => Enum.TryParse<T>(s, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : (T?)null)
            .OfType<T>()
            .Distinct()
            .Order()];

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string? Format(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Text columns sort A to Z first; ID, priority and dates highest or newest first; due date soonest first.</summary>
    public static SortDirection DefaultDirectionFor(CaseSortField field) =>
        field is CaseSortField.Title or CaseSortField.Status or CaseSortField.DueOn or CaseSortField.Category or CaseSortField.Owner ? SortDirection.Ascending : SortDirection.Descending;

    public CaseListQuery ToQuery() => new(Page, PageSize, Statuses.Count == 0 ? null : Statuses, Search, SortBy, Direction,
        Priorities: Priorities.Count == 0 ? null : Priorities,
        Categories: Categories.Count == 0 ? null : Categories,
        AssignedTo: Owner,
        OverdueOnly: Overdue,
        CreatedFromUtc: StartOfDay(CreatedFrom),
        CreatedToUtc: EndOfDay(CreatedTo),
        UpdatedFromUtc: StartOfDay(UpdatedFrom),
        UpdatedToUtc: EndOfDay(UpdatedTo));

    private static DateTimeOffset? StartOfDay(DateOnly? day) =>
        day is { } d ? Domain.EasternTime.StartOfDay(d) : null;

    private static DateTimeOffset? EndOfDay(DateOnly? day) =>
        day is { } d ? Domain.EasternTime.EndOfDay(d) : null;

    /// <summary>Query-string values for <c>NavigationManager.GetUriWithQueryParameters</c>; defaults are omitted (null).</summary>
    public IReadOnlyDictionary<string, object?> ToQueryParameters() => new Dictionary<string, object?>
    {
        ["page"] = Page == 1 ? null : Page,
        ["size"] = PageSize == DefaultPageSize ? null : PageSize,
        ["status"] = Statuses.Count == 0 ? null : Statuses.Select(s => s.ToString()).ToArray(),
        ["q"] = Search,
        ["sort"] = SortBy == Default.SortBy && Direction == Default.Direction ? null : SortBy.ToString(),
        ["dir"] = SortBy == Default.SortBy && Direction == Default.Direction ? null : (Direction == SortDirection.Ascending ? "asc" : "desc"),
        ["priority"] = Priorities.Count == 0 ? null : Priorities.Select(p => p.ToString()).ToArray(),
        ["category"] = Categories.Count == 0 ? null : Categories.Select(c => c.ToString()).ToArray(),
        ["owner"] = Owner,
        ["overdue"] = Overdue ? "true" : null,
        ["createdFrom"] = Format(CreatedFrom),
        ["createdTo"] = Format(CreatedTo),
        ["updatedFrom"] = Format(UpdatedFrom),
        ["updatedTo"] = Format(UpdatedTo),
    };

    /// <summary>Removes every filter (status, search and the extra filters); keeps sort and page size.</summary>
    public CaseListState WithoutFilters() => this with
    {
        Page = 1, Statuses = [], Search = null, Priorities = [], Categories = [], Owner = null, Overdue = false,
        CreatedFrom = null, CreatedTo = null, UpdatedFrom = null, UpdatedTo = null,
    };

    public CaseListState SortedBy(CaseSortField field) => SortBy == field
        ? this with { Direction = Direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending, Page = 1 }
        : this with { SortBy = field, Direction = DefaultDirectionFor(field), Page = 1 };
}
