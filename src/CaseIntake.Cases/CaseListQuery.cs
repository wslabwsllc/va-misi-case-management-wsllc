using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.Cases;

/// <summary>Validates a <see cref="CaseListQuery"/> before it reaches the database (E2-S5).</summary>
internal static class CaseListQueryValidator
{
    public static Error? Validate(CaseListQuery query)
    {
        Dictionary<string, string[]> errors = new(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors[nameof(CaseListQuery.Page)] = ["Page must be 1 or greater."];
        }
        if (query.PageSize is < 1 or > CaseService.MaxPageSize)
        {
            errors[nameof(CaseListQuery.PageSize)] = [$"Page size must be between 1 and {CaseService.MaxPageSize}."];
        }
        else if (query.Page >= 1 && ((long)query.Page - 1) * query.PageSize > int.MaxValue)
        {
            errors[nameof(CaseListQuery.Page)] = ["Page is too large."];
        }
        if (query.Statuses is not null && query.Statuses.Any(s => !Enum.IsDefined(s)))
        {
            errors[nameof(CaseListQuery.Statuses)] = ["Choose valid statuses."];
        }
        if (!Enum.IsDefined(query.SortBy))
        {
            errors[nameof(CaseListQuery.SortBy)] = ["Choose a valid sort field."];
        }
        if (!Enum.IsDefined(query.Direction))
        {
            errors[nameof(CaseListQuery.Direction)] = ["Choose a valid sort direction."];
        }
        if (query.Search is { } search &&
            (CaseTextRules.CountScalarValues(search) is not { } length || length > CaseFieldLimits.SearchMaxLength))
        {
            errors[nameof(CaseListQuery.Search)] = [$"Search text must be {CaseFieldLimits.SearchMaxLength} characters or fewer, without control characters."];
        }

        return errors.Count == 0 ? null : CaseErrors.InvalidQuery(errors);
    }
}

/// <summary>Server-side filtering and allowlisted sorting; never builds expressions from caller strings.</summary>
internal static class CaseListQueryBuilder
{
    private const string LikeEscape = "\\";

    public static IQueryable<Case> Filter(IQueryable<Case> cases, CaseListQuery query)
    {
        if (query.Statuses is { Count: > 0 } statuses)
        {
            var wanted = statuses.Distinct().ToArray();
            cases = cases.Where(c => wanted.Contains(c.Status));
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{EscapeLike(search)}%";
            cases = cases.Where(c => EF.Functions.ILike(c.Title, pattern, LikeEscape)
                                     || (c.Description != null && EF.Functions.ILike(c.Description, pattern, LikeEscape)));
        }

        return cases;
    }

    /// <summary>Orders by the chosen field with <c>Id</c> as the stable tiebreaker. Status order is New → Closed.</summary>
    public static IQueryable<Case> Sort(IQueryable<Case> cases, CaseSortField field, SortDirection direction)
    {
        var ascending = direction == SortDirection.Ascending;
        return field switch
        {
            CaseSortField.Id => ascending ? cases.OrderBy(c => c.Id) : cases.OrderByDescending(c => c.Id),
            CaseSortField.Title => (ascending ? cases.OrderBy(c => c.Title) : cases.OrderByDescending(c => c.Title)).ThenBy(c => c.Id),
            CaseSortField.Status => (ascending ? cases.OrderBy(c => c.Status) : cases.OrderByDescending(c => c.Status)).ThenBy(c => c.Id),
            CaseSortField.CreatedAt => (ascending ? cases.OrderBy(c => c.CreatedAtUtc) : cases.OrderByDescending(c => c.CreatedAtUtc)).ThenBy(c => c.Id),
            CaseSortField.UpdatedAt => (ascending ? cases.OrderBy(c => c.UpdatedAtUtc) : cases.OrderByDescending(c => c.UpdatedAtUtc)).ThenBy(c => c.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported sort field."),
        };
    }

    /// <summary>Escapes LIKE wildcards so <c>%</c>, <c>_</c> and the escape character match literally.</summary>
    public static string EscapeLike(string text) => text
        .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
        .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
        .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
