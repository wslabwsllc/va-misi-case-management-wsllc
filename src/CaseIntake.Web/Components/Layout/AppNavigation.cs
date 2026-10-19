using System.Security.Claims;
using CaseIntake.Contracts.Security;
using Microsoft.AspNetCore.Authorization;

namespace CaseIntake.Web.Components.Layout;

/// <summary>One site-map entry. <see cref="Policy"/> is an E3 permission policy; the item is hidden without it.</summary>
public sealed record NavItem(string Text, string Href, string Icon, string Policy, bool MatchPrefix = true, bool Child = false);

/// <summary>A labelled group of the site map (Power Apps "area").</summary>
public sealed record NavGroup(string? Label, IReadOnlyList<NavItem> Items);

/// <summary>
/// The application site map, shared by the desktop sidebar, the mobile bottom bar and the "More" sheet so they can never
/// disagree. Visibility is decided by the E3 authorization policies (policy name = permission name) through
/// <see cref="IAuthorizationService"/>; hiding an item is a convenience, every page and service still authorizes itself.
/// </summary>
public static class AppNavigation
{
    public static IReadOnlyList<NavGroup> Groups { get; } =
    [
        new(null, [new("Dashboard", "", "dashboard", Permissions.CasesView, MatchPrefix: false)]),
        new("Cases",
        [
            new("All cases", "cases", "cases", Permissions.CasesView),
            new("New case", "cases/new", "add", Permissions.CasesCreate, MatchPrefix: false),
        ]),
        new("My Work",
        [
            new("My tasks", "my-work", "tasks", Permissions.CasesView),
            new("Notifications", "notifications", "bell", Permissions.CasesView),
        ]),
        new("Reports", [new("Case reports", "reports", "reports", Permissions.CasesView)]),
        new("Administration",
        [
            new("Quality assurance", "test-results", "admin", Permissions.TestResultsView),
            new("Test results", "test-results/runs", "beaker", Permissions.TestResultsView, Child: true),
            new("Test cases", "test-results/cases", "document", Permissions.TestResultsView, Child: true),
            new("History and trends", "test-results/history", "history", Permissions.TestResultsView, Child: true),
        ]),
    ];

    /// <summary>The phone bottom bar: Home, Cases, My Tasks, Notifications (More opens the full site map).</summary>
    public static IReadOnlyList<NavItem> BottomBar { get; } =
    [
        new("Home", "", "home", Permissions.CasesView, MatchPrefix: false),
        new("Cases", "cases", "cases", Permissions.CasesView),
        new("My Tasks", "my-work", "tasks", Permissions.CasesView),
        new("Notifications", "notifications", "bell", Permissions.CasesView),
    ];

    /// <summary>The groups and items the user may see; groups with no visible item are dropped.</summary>
    public static async Task<IReadOnlyList<NavGroup>> VisibleGroupsAsync(IAuthorizationService authorization, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var granted = await GrantedPoliciesAsync(authorization, user, Groups.SelectMany(g => g.Items).Concat(BottomBar));
        return [.. Groups
            .Select(g => new NavGroup(g.Label, [.. g.Items.Where(i => granted.Contains(i.Policy))]))
            .Where(g => g.Items.Count > 0)];
    }

    public static async Task<HashSet<string>> GrantedPoliciesAsync(IAuthorizationService authorization, ClaimsPrincipal user, IEnumerable<NavItem> items)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        HashSet<string> granted = new(StringComparer.Ordinal);
        foreach (var policy in items.Select(i => i.Policy).Distinct(StringComparer.Ordinal))
        {
            if ((await authorization.AuthorizeAsync(user, policy)).Succeeded)
            {
                granted.Add(policy);
            }
        }
        return granted;
    }

    /// <summary>
    /// The href of the single item that represents <paramref name="relativePath"/> (no query, no leading slash): an
    /// exact match, otherwise the longest prefix match among items that allow prefix matching. Exactly one link per
    /// navigation landmark gets <c>aria-current="page"</c>.
    /// </summary>
    public static string? CurrentHref(string relativePath, IEnumerable<NavItem> items)
    {
        var path = relativePath.Split('?', '#')[0].Trim('/');
        var candidates = items.ToList();
        var exact = candidates.FirstOrDefault(i => string.Equals(i.Href, path, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact.Href;
        }
        return candidates
            .Where(i => i.MatchPrefix && i.Href.Length > 0 && path.StartsWith(i.Href + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Href.Length)
            .FirstOrDefault()?.Href;
    }
}
