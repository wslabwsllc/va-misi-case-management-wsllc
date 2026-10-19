using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;

namespace CaseIntake.Web.Components.Layout;

/// <summary>
/// Base for the site-map renderers: evaluates the E3 policies for the signed-in user and tracks the current location
/// so exactly one link carries <c>aria-current="page"</c>.
/// </summary>
public abstract class NavigationAwareBase : ComponentBase, IDisposable
{
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected IAuthorizationService Authorization { get; set; } = default!;
    [CascadingParameter] protected Task<AuthenticationState>? AuthenticationState { get; set; }

    protected ClaimsPrincipal User { get; private set; } = new(new ClaimsIdentity());
    protected IReadOnlyList<NavGroup> VisibleGroups { get; private set; } = [];
    protected HashSet<string> GrantedPolicies { get; private set; } = new(StringComparer.Ordinal);
    protected bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    protected string RelativePath => Navigation.ToBaseRelativePath(Navigation.Uri);

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    protected override async Task OnParametersSetAsync()
    {
        User = AuthenticationState is null ? new ClaimsPrincipal(new ClaimsIdentity()) : (await AuthenticationState).User;
        VisibleGroups = IsAuthenticated ? await AppNavigation.VisibleGroupsAsync(Authorization, User) : [];
        GrantedPolicies = IsAuthenticated
            ? await AppNavigation.GrantedPoliciesAsync(Authorization, User, AppNavigation.Groups.SelectMany(g => g.Items).Concat(AppNavigation.BottomBar))
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    protected string? CurrentAttr(NavItem item, IEnumerable<NavItem> landmarkItems) =>
        AppNavigation.CurrentHref(RelativePath, landmarkItems) == item.Href ? "page" : null;

    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
        GC.SuppressFinalize(this);
    }
}
