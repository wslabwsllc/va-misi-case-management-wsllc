using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Pages.Cases;

namespace CaseIntake.Web.Tests;

/// <summary>E5-S4: intake validation (the server's rules), focus, all five lookup outcomes and SSN handling.</summary>
[Trait("Category", "Story:E5-S4")]
[Trait("Category", "Req:R-4.2-4")]
[Trait("Category", "Req:R-4.2-12")]
[Trait("Category", "Req:R-4.2-16")]
public class NewCaseTests : WebTestContext
{
    public NewCaseTests()
    {
        SignInAs(AppRoles.CaseWorker);
    }

    private IRenderedComponent<NewCase> RenderPage() => Render<NewCase>();

    private static void LookUp(IRenderedComponent<NewCase> cut, string ssn)
    {
        cut.Find("#lookup-ssn").Input(ssn);
        cut.Find("[data-testid=lookup-form]").Submit();
    }

    [Fact]
    public void Title_and_description_have_labels_hints_and_counters_from_the_domain_limits()
    {
        var cut = RenderPage();

        Assert.Contains("(required)", cut.Find("label[for=intake-title]").TextContent);
        Assert.Contains("(optional)", cut.Find("label[for=intake-description]").TextContent);
        Assert.Equal($"0 of {CaseFieldLimits.TitleMaxLength} characters", cut.Find("#intake-title-counter").TextContent);
        Assert.Equal("0 of 4,000 characters", cut.Find("#intake-description-counter").TextContent);
        Assert.Contains("5 to 200 characters", cut.Find("#intake-title-hint").TextContent);
    }

    [Theory]
    [InlineData("", CaseTextRules.TitleRequiredMessage)]
    [InlineData("    ", CaseTextRules.TitleRequiredMessage)]
    [InlineData("abcd", "Title must be between 5 and 200 characters.")]
    public void Invalid_title_is_rejected_with_the_server_message_and_the_summary_takes_focus(string title, string message)
    {
        var cut = RenderPage();
        cut.Find("#intake-title").Input(title);

        cut.Find("[data-testid=intake-form]").Submit();

        Assert.Equal(message, cut.Find("#error-summary a").TextContent);
        Assert.Equal(message, cut.Find("#intake-title-error").TextContent.Trim());
        Assert.Equal("true", cut.Find("#intake-title").GetAttribute("aria-invalid"));
        JSInterop.VerifyFocusAsyncInvoke();
        Assert.Empty(CaseService.ListQueries);
        Assert.False(Navigation.Uri.Contains("/cases/", StringComparison.Ordinal));
    }

    [Fact]
    public void Description_over_the_limit_uses_the_server_message()
    {
        var cut = RenderPage();
        cut.Find("#intake-title").Input("Valid title");
        cut.Find("#intake-description").Input(new string('x', CaseFieldLimits.DescriptionMaxLength + 1));

        cut.Find("[data-testid=intake-form]").Submit();

        Assert.Equal(CaseTextRules.DescriptionLengthMessage, cut.Find("#intake-description-error").TextContent.Trim());
    }

    [Fact]
    public void Lookup_found_shows_name_and_address_and_Use_this_veteran_attaches_it_with_the_last_four_only()
    {
        Lookup.Returns("900-12-3456", new VeteranLookupResult(VeteranLookupOutcome.Found, "Rowan Calloway", "6243 Placeholder Street"));
        var cut = RenderPage();

        LookUp(cut, "900-12-3456");

        Assert.Contains("Veteran found: Rowan Calloway", cut.Find("[data-testid=lookup-status]").TextContent);
        Assert.Equal("status", cut.Find("[data-testid=lookup-status]").GetAttribute("role"));
        Assert.Contains("6243 Placeholder Street", cut.Find("[data-testid=lookup-result]").TextContent);
        Assert.Equal(string.Empty, cut.Find("#lookup-ssn").GetAttribute("value") ?? string.Empty);
        Assert.DoesNotContain("900-12", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid=use-veteran]").Click();

        Assert.Contains("Rowan Calloway, SSN ending 3456", cut.Find("[data-testid=selected-veteran]").TextContent);
    }

    [Fact]
    public void Lookup_not_found_is_announced_politely()
    {
        var cut = RenderPage();
        LookUp(cut, "999-99-9999");
        Assert.Contains("Not found", cut.Find("[data-testid=lookup-status]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=lookup-result]"));
    }

    [Theory]
    [InlineData(VeteranLookupOutcome.InvalidInput, "Invalid SSN format")]
    [InlineData(VeteranLookupOutcome.Unavailable, "unavailable right now. You can continue the intake")]
    [InlineData(VeteranLookupOutcome.Forbidden, "Access denied")]
    public void Lookup_problems_are_reported_in_an_alert_and_the_SSN_is_cleared(VeteranLookupOutcome outcome, string text)
    {
        Lookup.Returns("12345", new VeteranLookupResult(outcome, null, null));
        var cut = RenderPage();

        LookUp(cut, "12345");

        var alert = cut.Find("[data-testid=lookup-alert]");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains(text, alert.TextContent);
        Assert.Equal(string.Empty, cut.Find("#lookup-ssn").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void Ssn_field_is_a_labelled_numeric_text_input_without_autocomplete()
    {
        var cut = RenderPage();
        var ssn = cut.Find("#lookup-ssn");
        Assert.Equal("text", ssn.GetAttribute("type"));
        Assert.Equal("numeric", ssn.GetAttribute("inputmode"));
        Assert.Equal("off", ssn.GetAttribute("autocomplete"));
        Assert.Contains("Social Security number", cut.Find("label[for=lookup-ssn]").TextContent);
    }

    [Fact]
    public void A_new_or_failed_lookup_clears_a_previously_selected_veteran()
    {
        Lookup.Returns("900-12-3456", new VeteranLookupResult(VeteranLookupOutcome.Found, "Rowan Calloway", "Somewhere"));
        Lookup.Returns("111", new VeteranLookupResult(VeteranLookupOutcome.Unavailable, null, null));
        var cut = RenderPage();
        LookUp(cut, "900-12-3456");
        cut.Find("[data-testid=use-veteran]").Click();
        Assert.Single(cut.FindAll("[data-testid=selected-veteran]"));

        LookUp(cut, "111");

        Assert.Empty(cut.FindAll("[data-testid=selected-veteran]"));
        Assert.Empty(cut.FindAll("[data-testid=lookup-result]"));
    }

    [Fact]
    public async Task Valid_submit_creates_the_case_navigates_to_it_and_announces_creation()
    {
        Lookup.Returns("900-12-3456", new VeteranLookupResult(VeteranLookupOutcome.Found, "Rowan Calloway", "Somewhere"));
        var cut = RenderPage();
        cut.Find("#intake-title").Input("Valid intake title");
        cut.Find("#intake-description").Input("Details");
        LookUp(cut, "900-12-3456");
        cut.Find("[data-testid=use-veteran]").Click();

        cut.Find("[data-testid=intake-form]").Submit();

        Assert.EndsWith("/cases/1", Navigation.Uri, StringComparison.Ordinal);
        Assert.Equal("Case 1 created", Announcer.Message);
        var created = (await CaseService.GetAsync(1, CancellationToken.None)).Value!;
        Assert.Equal("Valid intake title", created.Title);
        Assert.Equal("3456", created.Veteran!.SsnLast4);
    }

    [Fact]
    public void Server_failure_shows_an_alert_and_keeps_the_input()
    {
        CaseService.CreateError = new Error(ErrorKind.ExternalUnavailable, "Down", "The case service is unavailable.");
        var cut = RenderPage();
        cut.Find("#intake-title").Input("Keep this title");

        cut.Find("[data-testid=intake-form]").Submit();

        Assert.Contains("The case service is unavailable.", cut.Find("[data-testid=intake-error]").TextContent);
        Assert.Equal("Keep this title", cut.Find("#intake-title").GetAttribute("value"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void Server_field_errors_are_mapped_to_the_fields()
    {
        CaseService.CreateError = new Error(ErrorKind.Validation, "Validation", "Some fields need attention.",
            new Dictionary<string, string[]> { [CaseFieldNames.Title] = ["Title is taken by server rule."] });
        var cut = RenderPage();
        cut.Find("#intake-title").Input("A valid title");

        cut.Find("[data-testid=intake-form]").Submit();

        Assert.Equal("Title is taken by server rule.", cut.Find("#intake-title-error").TextContent.Trim());
    }

    [Fact]
    public void The_submit_button_reports_busy_state_semantics()
    {
        var cut = RenderPage();
        var button = cut.Find("[data-testid=create-case]");
        Assert.Equal("false", button.GetAttribute("aria-busy"));
        Assert.Equal("intake-form", button.GetAttribute("form"));
    }
}
