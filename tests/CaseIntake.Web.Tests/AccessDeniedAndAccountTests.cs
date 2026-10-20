using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Account;
using CaseIntake.Web.Components.Account.Pages;
using CaseIntake.Web.Components.Account.Shared;
using CaseIntake.Web.Components.Pages.Cases;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.Web.Tests;

/// <summary>E5-S8 access-denied experience and the E3 sign-in page styled with the E5 kit.</summary>
[Trait("Category", "Story:E5-S8")]
[Trait("Category", "Req:R-4.2-11")]
[Trait("Category", "Req:R-4.2-12")]
public class AccessDeniedAndAccountTests : WebTestContext
{
    [Fact]
    public void Access_denied_has_a_focusable_h1_a_clear_reason_and_a_back_to_case_list_action()
    {
        SignInAs(AppRoles.QualityAnalyst);
        var cut = Render<AccessDeniedMessage>(p => p.Add(m => m.PageType, typeof(NewCase)));

        var h1 = cut.Find("h1");
        Assert.Equal("Access denied", h1.TextContent);
        Assert.Equal("-1", h1.GetAttribute("tabindex"));
        Assert.Contains("You don't have permission", cut.Markup);
        Assert.Contains("Quality analyst", cut.Find("[data-testid=access-denied]").TextContent);
        var back = cut.FindAll("a").Single(a => a.TextContent == "Back to the case list");
        Assert.Equal("cases", back.GetAttribute("href"));
    }

    [Fact]
    public void Access_denied_does_not_reveal_permission_or_policy_names()
    {
        SignInAs(AppRoles.QualityAnalyst);
        var cut = Render<AccessDeniedMessage>(p => p.Add(m => m.PageType, typeof(NewCase)));

        Assert.DoesNotContain(Permissions.CasesCreate, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("policy", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sign_in_page_labels_every_field_and_marks_them_required_in_text()
    {
        var cut = Render<Login>();

        Assert.Equal("Sign in", cut.Find("h1").TextContent);
        foreach (var (id, label) in new[] { ("login-email", "Email"), ("login-password", "Password") })
        {
            var labelElement = cut.Find($"label[for={id}]");
            Assert.StartsWith(label, labelElement.TextContent.Trim(), StringComparison.Ordinal);
            Assert.Contains("(required)", labelElement.TextContent);
            Assert.Equal("true", cut.Find($"#{id}").GetAttribute("aria-required"));
        }
        Assert.Equal("username", cut.Find("#login-email").GetAttribute("autocomplete"));
        Assert.Equal("current-password", cut.Find("#login-password").GetAttribute("autocomplete"));
        Assert.Equal("Input.Email", cut.Find("#login-email").GetAttribute("name"));
    }

    [Fact]
    public void Sign_in_page_lists_the_demo_accounts_only_while_demo_seeding_is_configured()
    {
        Assert.Empty(Render<Login>().FindAll("[data-testid=demo-accounts]"));

        Configuration["Seed:DemoPassword"] = "Demo-Passw0rd!";
        var accounts = Render<Login>().Find("[data-testid=demo-accounts]");

        Assert.Equal(5, accounts.QuerySelectorAll("li").Length);
        Assert.Contains("auditor@example.test", accounts.TextContent);
        Assert.Contains("Intake clerk", accounts.TextContent);
    }

    [Fact]
    public void Sign_in_page_confirms_a_sign_out()
    {
        Navigation.NavigateTo(AccountRoutes.SignedOut);
        var cut = Render<Login>();

        var message = cut.Find("[data-testid=signed-out]");
        Assert.Equal("status", message.GetAttribute("role"));
        Assert.Contains("You have signed out.", message.TextContent);
    }

    [Fact]
    public void Sign_in_page_explains_a_redirect_from_a_protected_page_or_an_ended_session()
    {
        Navigation.NavigateTo("account/login?ReturnUrl=%2Fcases%2F5");
        var cut = Render<Login>();

        Assert.Contains("Sign in to continue", cut.Find("[data-testid=sign-in-required]").TextContent);
    }

    [Fact]
    public void Sign_out_endpoint_redirects_to_the_signed_out_confirmation()
    {
        Assert.Equal("/account/login?signedOut=true", AccountRoutes.SignedOut);
    }

    [Fact]
    public void Anonymous_navigation_to_a_protected_page_redirects_to_sign_in_with_the_return_url()
    {
        Auth.SetNotAuthorized();
        Navigation.NavigateTo("cases/9");
        Render<RedirectToLogin>();

        Assert.EndsWith("/account/login?ReturnUrl=%2Fcases%2F9", Navigation.Uri, StringComparison.Ordinal);
    }
}
