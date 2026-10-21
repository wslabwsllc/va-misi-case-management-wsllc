using CaseIntake.Contracts.Security;
using CaseIntake.Web.Components.Layout;
using CaseIntake.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.Web.Tests;

/// <summary>E5-S1 landmarks and skip link; E5-S8 signed-in header and permission-based navigation, per role.</summary>
[Trait("Category", "Story:E5-S1")]
[Trait("Category", "Story:E5-S8")]
[Trait("Category", "Req:R-4.2-11")]
[Trait("Category", "Req:R-4.2-12")]
public class LayoutTests : WebTestContext
{
    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<h1>Body</h1>"))));

    [Fact]
    public void Layout_has_a_skip_link_first_then_banner_labelled_navigation_and_main()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = RenderLayout();

        var first = cut.Find(".app-shell > *");
        Assert.Equal("a", first.TagName, ignoreCase: true);
        Assert.Equal("Skip to main content", first.TextContent);
        Assert.Equal("#main", first.GetAttribute("href"));
        Assert.Single(cut.FindAll("header"));
        var main = cut.Find("main#main");
        Assert.Equal("-1", main.GetAttribute("tabindex"));
        var nav = cut.Find("nav#app-nav");
        Assert.Equal("Main navigation", nav.GetAttribute("aria-label"));
        Assert.NotEmpty(nav.QuerySelectorAll("ul > li > a"));
        Assert.Equal("Mobile navigation", cut.Find("nav.bottom-nav").GetAttribute("aria-label"));
        Assert.Equal("Breadcrumb", cut.Find("nav.breadcrumb").GetAttribute("aria-label"));
    }

    [Fact]
    public void Skip_link_moves_focus_to_main()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = RenderLayout();

        cut.Find("a.skip-link").Click();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Theory]
    [InlineData(AppRoles.Auditor)]
    [InlineData(AppRoles.IntakeClerk)]
    [InlineData(AppRoles.CaseWorker)]
    [InlineData(AppRoles.Supervisor)]
    [InlineData(AppRoles.QualityAnalyst)]
    public void Header_shows_display_name_actual_role_and_an_accessible_sign_out_button(string role)
    {
        SignInAs(role);
        var cut = RenderLayout();

        Assert.Equal($"{role.ToLowerInvariant()}@example.test", cut.Find("[data-testid=user-name]").TextContent);
        Assert.Equal(AppRoles.DisplayName(role), cut.Find("[data-testid=user-roles]").TextContent);
        var signOut = cut.Find("[data-testid=sign-out]");
        Assert.Equal("submit", signOut.GetAttribute("type"));
        Assert.Contains("Sign out", signOut.TextContent);
        var form = signOut.Closest("form")!;
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Equal("account/logout", form.GetAttribute("action"));
        Assert.NotNull(form.QuerySelector("input[name=__RequestVerificationToken]"));
    }

    [Fact]
    public void New_case_link_is_hidden_without_create_permission()
    {
        SignInAs(AppRoles.QualityAnalyst);
        var cut = RenderLayout();

        Assert.DoesNotContain(cut.FindAll("nav#app-nav a"), a => a.TextContent.Contains("New case"));
        Assert.Empty(cut.FindAll("[data-testid=fab-new-case]"));
    }

    [Theory]
    [InlineData(AppRoles.CaseWorker)]
    [InlineData(AppRoles.Supervisor)]
    public void New_case_link_is_visible_with_create_permission(string role)
    {
        SignInAs(role);
        var cut = RenderLayout();

        Assert.Contains(cut.FindAll("nav#app-nav a"), a => a.TextContent.Contains("New case") && a.GetAttribute("href") == "cases/new");
        Assert.Single(cut.FindAll("[data-testid=fab-new-case]"));
    }

    [Fact]
    public void Administration_is_hidden_without_test_results_or_user_permission()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = RenderLayout();

        Assert.DoesNotContain(cut.FindAll("nav#app-nav .app-nav__group-label"), l => l.TextContent == "Administration");
        Assert.DoesNotContain(cut.FindAll("nav#app-nav a"), a => a.GetAttribute("href")?.StartsWith("test-results", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(cut.FindAll("nav#app-nav a"), a => a.GetAttribute("href") == "admin/users");
    }

    [Fact]
    public void Supervisors_see_users_under_administration_but_not_test_results()
    {
        SignInAs(AppRoles.Supervisor);
        var cut = RenderLayout();

        Assert.Contains(cut.FindAll("nav#app-nav .app-nav__group-label"), l => l.TextContent == "Administration");
        Assert.Contains(cut.FindAll("nav#app-nav a"), a => a.GetAttribute("href") == "admin/users");
        Assert.DoesNotContain(cut.FindAll("nav#app-nav a"), a => a.GetAttribute("href") == "admin/roles");
        Assert.DoesNotContain(cut.FindAll("nav#app-nav a"), a => a.GetAttribute("href")?.StartsWith("test-results", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Administrators_see_users_and_roles()
    {
        SignInAs(AppRoles.Administrator);
        var cut = RenderLayout();

        Assert.Contains(cut.FindAll("nav#app-nav a"), a => a.GetAttribute("href") == "admin/users");
        Assert.Contains(cut.FindAll("nav#app-nav a"), a => a.GetAttribute("href") == "admin/roles");
    }

    [Fact]
    public void Administration_and_test_results_are_shown_to_the_quality_analyst()
    {
        SignInAs(AppRoles.QualityAnalyst);
        var cut = RenderLayout();

        Assert.Contains(cut.FindAll("nav#app-nav .app-nav__group-label"), l => l.TextContent == "Administration");
        Assert.Contains(cut.FindAll("nav#app-nav a"), a => a.TextContent.Contains("Test results"));
        Assert.DoesNotContain(cut.FindAll("nav#app-nav a"), a => a.GetAttribute("href") == "admin/users");
    }

    [Fact]
    public void Current_page_is_marked_with_aria_current_on_exactly_one_link_per_navigation()
    {
        SignInAs(AppRoles.Supervisor);
        Navigation.NavigateTo("cases/42");
        var cut = RenderLayout();

        var current = cut.FindAll("nav#app-nav [aria-current=page]");
        Assert.Single(current);
        Assert.Contains("All cases", current[0].TextContent);
        var bottom = cut.FindAll("nav.bottom-nav [aria-current=page]");
        Assert.Single(bottom);
        Assert.Contains("Cases", bottom[0].TextContent);
    }

    [Fact]
    public void Navigation_toggle_collapses_the_sidebar_and_reports_its_state()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = RenderLayout();
        var toggle = cut.Find("[data-testid=nav-toggle]");
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));

        toggle.Click();

        Assert.Equal("false", cut.Find("[data-testid=nav-toggle]").GetAttribute("aria-expanded"));
        Assert.Contains("app-nav--collapsed", cut.Find("nav#app-nav").ClassList);
    }

    [Fact]
    public void Anonymous_user_sees_a_sign_in_link_and_no_navigation()
    {
        Auth.SetNotAuthorized();
        var cut = RenderLayout();

        Assert.Empty(cut.FindAll("nav#app-nav"));
        Assert.Contains(cut.FindAll("a"), a => a.TextContent == "Sign in" && a.GetAttribute("href") == "account/login");
    }

    [Fact]
    public void Breadcrumb_and_mobile_back_target_follow_the_page_shell()
    {
        SignInAs(AppRoles.CaseWorker);
        var cut = RenderLayout();
        Services.GetRequiredService<ShellState>().Set("Case 7", [new Crumb("Dashboard", ""), new Crumb("Cases", "cases"), new Crumb("Case 7")], "cases");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Case 7", cut.Find(".breadcrumb [aria-current=page]").TextContent);
            Assert.Equal("Back to Cases", cut.Find("[data-testid=mobile-back]").GetAttribute("aria-label"));
        });
    }
}
