using CaseIntake.Contracts.Cases;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Web.Components.Pages.Cases;

/// <summary>
/// The case list view state as it appears in the URL (E5-S3: reload or share the URL to restore the view). Parsing is
/// lenient: unknown or out-of-range values fall back to the defaults (25 per page, Updated descending, OQ-4) instead of
/// failing the page; the service validates the final query again.
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

    public static CaseListState Default { get; } = new(1, DefaultPageSize, [], null, CaseSortField.UpdatedAt, SortDirection.Descending);

    public static CaseListState Parse(int? page, int? size, string[]? statuses, string? search, string? sort, string? dir)
    {
        var parsedStatuses = (statuses ?? [])
            .Select(s => Enum.TryParse<CaseStatus>(s, ignoreCase: true, out var status) && Enum.IsDefined(status) ? status : (CaseStatus?)null)
            .OfType<CaseStatus>()
            .Distinct()
            .Order()
            .ToList();
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
            Statuses: parsedStatuses,
            Search: trimmed,
            SortBy: sortBy,
            Direction: direction);
    }

    /// <summary>Text columns sort A to Z first; ID and dates newest first.</summary>
    public static SortDirection DefaultDirectionFor(CaseSortField field) =>
        field is CaseSortField.Title or CaseSortField.Status ? SortDirection.Ascending : SortDirection.Descending;

    public CaseListQuery ToQuery() => new(Page, PageSize, Statuses.Count == 0 ? null : Statuses, Search, SortBy, Direction);

    /// <summary>Query-string values for <c>NavigationManager.GetUriWithQueryParameters</c>; defaults are omitted (null).</summary>
    public IReadOnlyDictionary<string, object?> ToQueryParameters() => new Dictionary<string, object?>
    {
        ["page"] = Page == 1 ? null : Page,
        ["size"] = PageSize == DefaultPageSize ? null : PageSize,
        ["status"] = Statuses.Count == 0 ? null : Statuses.Select(s => s.ToString()).ToArray(),
        ["q"] = Search,
        ["sort"] = SortBy == Default.SortBy && Direction == Default.Direction ? null : SortBy.ToString(),
        ["dir"] = SortBy == Default.SortBy && Direction == Default.Direction ? null : (Direction == SortDirection.Ascending ? "asc" : "desc"),
    };

    public CaseListState SortedBy(CaseSortField field) => SortBy == field
        ? this with { Direction = Direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending, Page = 1 }
        : this with { SortBy = field, Direction = DefaultDirectionFor(field), Page = 1 };
}
