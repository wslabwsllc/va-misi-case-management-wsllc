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
        if (query.Priorities is not null && query.Priorities.Any(p => !Enum.IsDefined(p)))
        {
            errors[nameof(CaseListQuery.Priorities)] = ["Choose valid priorities."];
        }
        if (query.Categories is not null && query.Categories.Any(c => !Enum.IsDefined(c)))
        {
            errors[nameof(CaseListQuery.Categories)] = ["Choose valid categories."];
        }
        if (query.AssignedTo is { } owner &&
            (CaseTextRules.CountScalarValues(owner) is not { } ownerLength || ownerLength > CaseFieldLimits.ActorMaxLength))
        {
            errors[nameof(CaseListQuery.AssignedTo)] = ["Choose a valid owner."];
        }
        if (query.CreatedFromUtc > query.CreatedToUtc || query.UpdatedFromUtc > query.UpdatedToUtc || query.DueFrom > query.DueTo)
        {
            errors["DateRange"] = ["The start of a date range must not be after its end."];
        }

        return errors.Count == 0 ? null : CaseErrors.InvalidQuery(errors);
    }
}

/// <summary>Server-side filtering and allowlisted sorting; never builds expressions from caller strings.</summary>
internal static class CaseListQueryBuilder
{
    private const string LikeEscape = "\\";

    /// <param name="today">Today's Eastern calendar date, for <see cref="CaseListQuery.OverdueOnly"/>.</param>
    public static IQueryable<Case> Filter(IQueryable<Case> cases, CaseListQuery query, DateOnly today)
    {
        if (query.Statuses is { Count: > 0 } statuses)
        {
            var wanted = statuses.Distinct().ToArray();
            cases = cases.Where(c => wanted.Contains(c.Status));
        }
        if (query.Priorities is { Count: > 0 } priorities)
        {
            var wanted = priorities.Distinct().ToArray();
            cases = cases.Where(c => wanted.Contains(c.Priority));
        }
        if (query.Categories is { Count: > 0 } categories)
        {
            var wanted = categories.Distinct().ToArray();
            cases = cases.Where(c => wanted.Contains(c.Category));
        }
        if (query.AssignedTo is { Length: > 0 } owner)
        {
            cases = owner == CaseListQuery.UnassignedFilter
                ? cases.Where(c => c.AssignedTo == null)
                : cases.Where(c => c.AssignedTo == owner);
        }
        if (query.OverdueOnly)
        {
            // Same rule as CaseTriageRules.IsOverdue: open and due before today.
            cases = cases.Where(c => c.DueOn != null && c.DueOn < today
                                     && (c.Status == CaseStatus.New || c.Status == CaseStatus.Triaged || c.Status == CaseStatus.InProgress));
        }
        if (query.CreatedFromUtc is { } createdFrom)
        {
            cases = cases.Where(c => c.CreatedAtUtc >= createdFrom);
        }
        if (query.CreatedToUtc is { } createdTo)
        {
            cases = cases.Where(c => c.CreatedAtUtc <= createdTo);
        }
        if (query.UpdatedFromUtc is { } updatedFrom)
        {
            cases = cases.Where(c => c.UpdatedAtUtc >= updatedFrom);
        }
        if (query.UpdatedToUtc is { } updatedTo)
        {
            cases = cases.Where(c => c.UpdatedAtUtc <= updatedTo);
        }

        if (query.DueFrom is { } dueFrom)
        {
            cases = cases.Where(c => c.DueOn != null && c.DueOn >= dueFrom);
        }
        if (query.DueTo is { } dueTo)
        {
            cases = cases.Where(c => c.DueOn != null && c.DueOn <= dueTo);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{EscapeLike(search)}%";
            var id = CaseSearch.IdOf(search);
            cases = cases.Where(c => (id != null && c.Id == id)
                                     || EF.Functions.ILike(c.Title, pattern, LikeEscape)
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
            CaseSortField.Priority => (ascending ? cases.OrderBy(c => c.Priority) : cases.OrderByDescending(c => c.Priority)).ThenBy(c => c.Id),
            // Cases without a due date sort last in both directions.
            CaseSortField.DueOn => (ascending
                ? cases.OrderBy(c => c.DueOn == null).ThenBy(c => c.DueOn)
                : cases.OrderBy(c => c.DueOn == null).ThenByDescending(c => c.DueOn)).ThenBy(c => c.Id),
            CaseSortField.Category => (ascending ? cases.OrderBy(c => c.Category) : cases.OrderByDescending(c => c.Category)).ThenBy(c => c.Id),
            // Unassigned cases sort last in both directions.
            CaseSortField.Owner => (ascending
                ? cases.OrderBy(c => c.AssignedTo == null).ThenBy(c => c.AssignedTo)
                : cases.OrderBy(c => c.AssignedTo == null).ThenByDescending(c => c.AssignedTo)).ThenBy(c => c.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported sort field."),
        };
    }

    /// <summary>Escapes LIKE wildcards so <c>%</c>, <c>_</c> and the escape character match literally.</summary>
    public static string EscapeLike(string text) => text
        .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
        .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
        .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
