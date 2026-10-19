namespace CaseIntake.Web.Services;

/// <summary>One breadcrumb step. <see cref="Href"/> is null for the current page.</summary>
public sealed record Crumb(string Text, string? Href = null);

/// <summary>
/// What the shell (header breadcrumb, mobile title and back button) shows for the current page. Each page sets it
/// through <c>PageShell</c>; the layout re-renders when it changes. Circuit-scoped.
/// </summary>
public sealed class ShellState
{
    public string Title { get; private set; } = "Case Intake";

    public IReadOnlyList<Crumb> Breadcrumbs { get; private set; } = [];

    /// <summary>The logical parent page for the mobile back button; null on top-level pages.</summary>
    public string? BackHref { get; private set; }

    public event Action? Changed;

    private string? _lastNavigationKey;

    /// <summary>
    /// Records the page now shown (<paramref name="key"/> identifies page and route values, not the query string) and
    /// returns true when focus should move to its h1: there was an earlier page in this circuit and it was different.
    /// The first page of a circuit is a full page load, where focus stays at the start of the document (ADR 0011).
    /// </summary>
    public bool ShouldFocusAfterNavigation(string key)
    {
        var focus = _lastNavigationKey is not null && _lastNavigationKey != key;
        _lastNavigationKey = key;
        return focus;
    }

    public void Set(string title, IReadOnlyList<Crumb> breadcrumbs, string? backHref)
    {
        if (Title == title && BackHref == backHref && Breadcrumbs.SequenceEqual(breadcrumbs))
        {
            return;
        }
        Title = title;
        Breadcrumbs = breadcrumbs;
        BackHref = backHref;
        Changed?.Invoke();
    }
}
