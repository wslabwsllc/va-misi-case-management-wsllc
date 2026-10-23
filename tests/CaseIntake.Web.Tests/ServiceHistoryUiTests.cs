using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using CaseIntake.Web.Components.Pages.Cases;

namespace CaseIntake.Web.Tests;

/// <summary>
/// E5-C2-1: service history at intake. Every outcome is shown, kept with the veteran and never blocks submitting; the
/// call uses the lookup's SSN before it is cleared; a client that throws is Unavailable and logged without the SSN.
/// </summary>
[Category("Story:E5-C2-1")]
[Category("Req:R-CR2-1")]
[Category("Req:R-CR2-5")]
[Category("Req:R-CR2-6")]
public class ServiceHistoryIntakeTests : WebTestContext
{
    private const string Ssn = "900-12-3456";

    internal static readonly ServiceHistory Army =
        new("Army", new DateOnly(2001, 2, 15), new DateOnly(2005, 2, 14), "Honorable", new DateOnly(2026, 1, 5));

    public ServiceHistoryIntakeTests()
    {
        SignInAs(AppRoles.CaseWorker);
        Lookup.Returns(Ssn, new VeteranLookupResult(VeteranLookupOutcome.Found, "Rowan Calloway", "6243 Placeholder Street"));
    }

    private IRenderedComponent<NewCase> LookUp(string ssn = Ssn)
    {
        var cut = Render<NewCase>();
        cut.Find("#intake-title").Input("Service history intake");
        cut.Find("#lookup-ssn").Input(ssn);
        cut.Find("[data-testid=lookup-form]").Submit();
        return cut;
    }

    private async Task<VeteranSnapshot> UseAndSubmitAsync(IRenderedComponent<NewCase> cut)
    {
        cut.Find("[data-testid=use-veteran]").Click();
        cut.Find("[data-testid=intake-form]").Submit();
        cut.Find("[data-testid=confirm-intake-submit]").Click();
        Assert.EndsWith("/cases/1", Navigation.Uri, StringComparison.Ordinal);
        return (await CaseService.GetAsync(1, CancellationToken.None)).Value!.Veteran!;
    }

    [Test]
    public async Task A_found_history_is_shown_and_submitted_with_the_case()
    {
        History.Returns(Ssn, new ServiceHistoryResult(VeteranLookupOutcome.Found, Army));

        var cut = LookUp();

        Assert.Equal([Ssn], History.Requests);   // the same SSN, before it was cleared
        var section = cut.Find("[data-testid=lookup-result] [data-testid=service-history]");
        Assert.Equal("Service history", section.QuerySelector("h3")!.TextContent);
        Assert.Equal("Army", cut.Find("[data-testid=service-branch]").TextContent);
        Assert.Equal("02-15-2001", cut.Find("[data-testid=service-start]").TextContent);
        Assert.Equal("02-14-2005", cut.Find("[data-testid=service-end]").TextContent);
        Assert.Equal("Honorable", cut.Find("[data-testid=character-of-service]").TextContent);
        Assert.Equal("01-05-2026", cut.Find("[data-testid=service-verified]").TextContent);
        Assert.Contains("Veteran found: Rowan Calloway. Service history found.", cut.Find("[data-testid=lookup-status]").TextContent);

        var stored = await UseAndSubmitAsync(cut);

        Assert.Equal(new VeteranSnapshot("Rowan Calloway", "6243 Placeholder Street", "3456", ServiceHistoryStatus.Found, Army), stored);
    }

    [Test]
    public async Task No_record_shows_an_info_message_and_the_case_is_still_created()
    {
        var cut = LookUp();   // the fake answers NotFound by default

        var message = cut.Find("[data-testid=service-history] .message--info");
        Assert.Equal(ServiceHistoryDetails.NotFoundAtIntakeText, message.TextContent.Trim());
        Assert.Equal("No service-history record was found for this veteran. You can still submit the case.", ServiceHistoryDetails.NotFoundAtIntakeText);

        var stored = await UseAndSubmitAsync(cut);

        Assert.Equal(ServiceHistoryStatus.NotFound, stored.HistoryStatus);
        Assert.Null(stored.History);
    }

    /// <summary>D6: any outcome other than Found or NotFound is stored as Unavailable.</summary>
    [TestCase(VeteranLookupOutcome.Unavailable)]
    [TestCase(VeteranLookupOutcome.InvalidInput)]
    [TestCase(VeteranLookupOutcome.Forbidden)]
    public async Task An_outage_shows_a_warning_and_does_not_block_intake(VeteranLookupOutcome outcome)
    {
        History.Returns(Ssn, new ServiceHistoryResult(outcome, null));

        var cut = LookUp();

        var warning = cut.Find("[data-testid=service-history] .message--warning");
        Assert.Equal("The service-history service is unavailable right now. You can submit without it, or look up again to retry.", warning.TextContent.Trim());
        Assert.Contains("Rowan Calloway", cut.Find("[data-testid=lookup-result]").TextContent);   // the found veteran is kept

        var stored = await UseAndSubmitAsync(cut);

        Assert.Equal(ServiceHistoryStatus.Unavailable, stored.HistoryStatus);
        Assert.Null(stored.History);
    }

    [Test]
    public async Task A_client_that_throws_keeps_the_veteran_stores_Unavailable_and_logs_5014_without_the_SSN()
    {
        History.Respond = (_, _) => throw new InvalidOperationException($"history for {Ssn} failed");

        var cut = LookUp();

        Assert.Single(cut.FindAll("[data-testid=lookup-result] [data-testid=service-history] .message--warning"));
        var record = Assert.Single(Logs.GetSnapshot(), r => r.Id.Id == 5014);
        Assert.Equal("ServiceHistoryUiFailed", record.Id.Name);
        Assert.Equal("InvalidOperationException", record.GetStructuredStateValue("ExceptionType"));
        Assert.Null(record.Exception);   // the exception (and its message) is not attached
        Assert.All(Logs.GetSnapshot(), r =>
        {
            var text = r.Message + " " + string.Join(" ", r.StructuredState?.Select(kv => kv.Value) ?? []);
            Assert.False(ServiceDefaults.SsnRedaction.ContainsSsn(text), $"SSN-shaped value logged: {r.Id.Name}");
            Assert.DoesNotContain("12-3456", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Rowan", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Placeholder Street", text, StringComparison.Ordinal);
        });

        var stored = await UseAndSubmitAsync(cut);
        Assert.Equal(ServiceHistoryStatus.Unavailable, stored.HistoryStatus);
    }

    [Test]
    public async Task Closing_the_page_while_the_lookup_runs_logs_no_false_service_history_failure()
    {
        var pending = new TaskCompletionSource<VeteranLookupResult>();
        Lookup.Respond = (_, _) => pending.Task;
        LookUp();

        await DisposeComponentsAsync();   // disposes the page's CancellationTokenSource mid-lookup
        pending.SetResult(new VeteranLookupResult(VeteranLookupOutcome.Found, "Rowan Calloway", "6243 Placeholder Street"));

        // The continuation runs on the renderer: wait until it either calls the history client or logs a failure.
        for (var i = 0; i < 500 && History.CallCount == 0 && !Logs.GetSnapshot().Any(r => r.Id.Id == 5014); i++)
        {
            await Task.Delay(10);   // at most 5 s
        }
        Assert.Equal(1, History.CallCount);
        Assert.DoesNotContain(Logs.GetSnapshot(), r => r.Id.Id == 5014);
    }

    [Test]
    public void Service_history_is_not_requested_when_the_lookup_does_not_find_a_veteran()
    {
        LookUp("999-99-9999");

        Assert.Equal(0, History.CallCount);
    }

    [Test]
    public void While_both_calls_run_the_form_is_busy_the_button_keeps_its_label_and_the_state_is_announced()
    {
        var pending = new TaskCompletionSource<ServiceHistoryResult>();
        History.Respond = (_, _) => pending.Task;

        var cut = LookUp();

        Assert.Equal("true", cut.Find("[data-testid=lookup-form]").GetAttribute("aria-busy"));
        var button = cut.Find("[data-testid=lookup-button]");
        Assert.Equal("Look up veteran", button.TextContent.Trim());
        Assert.True(button.HasAttribute("disabled"));
        Assert.Contains("Looking up their service history", cut.Find("[role=status][data-testid=lookup-status]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=lookup-result]"));

        pending.SetResult(new ServiceHistoryResult(VeteranLookupOutcome.Found, Army));

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("[data-testid=lookup-form]").GetAttribute("aria-busy")));
        Assert.Contains("Service history found.", cut.Find("[data-testid=lookup-status]").TextContent);
        Assert.Equal(string.Empty, cut.Find("#lookup-ssn").GetAttribute("value") ?? string.Empty);   // cleared after both calls
    }

    [TestCase(VeteranLookupOutcome.Found, "Service history found.")]
    [TestCase(VeteranLookupOutcome.NotFound, "No service-history record was found.")]
    [TestCase(VeteranLookupOutcome.Unavailable, "Service history is unavailable; you can submit without it.")]
    public void The_selected_veteran_card_shows_the_history_status_in_one_line(VeteranLookupOutcome outcome, string line)
    {
        History.Returns(Ssn, new ServiceHistoryResult(outcome, outcome == VeteranLookupOutcome.Found ? Army : null));
        var cut = LookUp();

        cut.Find("[data-testid=use-veteran]").Click();

        Assert.Equal(line, cut.Find("[data-testid=selected-veteran] [data-testid=selected-history]").TextContent);
    }
}

/// <summary>
/// E5-C2-2 and E3-C2-1: the case detail shows the stored service history, or why there is none, to every role that can
/// view the case. The history is case data governed by cases.view, not by veteran.lookup.
/// </summary>
[Category("Story:E5-C2-2")]
[Category("Story:E3-C2-1")]
[Category("Req:R-CR2-3")]
[Category("Req:R-CR2-6")]
public class ServiceHistoryDetailTests : WebTestContext
{
    private IRenderedComponent<CaseDetail> RenderCase(long id) => Render<CaseDetail>(p => p.Add(c => c.Id, id));

    private Task<CaseDetailDto> SeedAsync(ServiceHistoryStatus? status, ServiceHistory? history) =>
        SeedCaseAsync("Case with service history", veteran: new VeteranSnapshot("Rowan Calloway", "6243 Placeholder Street", "3456", status, history));

    public static IEnumerable<string> RolesThatViewCases() =>
        RolePermissionMap.Roles.Where(role => RolePermissionMap.Grants([role], Permissions.CasesView));

    [TestCaseSource(nameof(RolesThatViewCases))]
    public async Task Every_role_that_can_view_the_case_sees_the_stored_history(string role)
    {
        var created = await SeedAsync(ServiceHistoryStatus.Found, ServiceHistoryIntakeTests.Army);
        SignInAs(role);

        var cut = RenderCase(created.Id);

        var section = cut.Find("[data-testid=veteran-section] [data-testid=service-history]");
        Assert.Equal("Service history", section.QuerySelector("h3")!.TextContent);
        Assert.Equal("Army", cut.Find("[data-testid=service-branch]").TextContent);
        Assert.Equal("Honorable", cut.Find("[data-testid=character-of-service]").TextContent);
        Assert.Equal("02-15-2001", cut.Find("[data-testid=service-start]").TextContent);
        Assert.Equal("02-14-2005", cut.Find("[data-testid=service-end]").TextContent);
        Assert.Equal("01-05-2026", cut.Find("[data-testid=service-verified]").TextContent);
    }

    [Test]
    public void The_six_built_in_roles_all_view_cases_including_two_that_cannot_look_up_veterans()
    {
        Assert.Equal(RolePermissionMap.Roles.Length, RolesThatViewCases().Count());
        Assert.Equal(new[] { AppRoles.Auditor, AppRoles.QualityAnalyst }.Order(StringComparer.Ordinal),
            RolesThatViewCases().Where(role => !RolePermissionMap.Grants([role], Permissions.VeteranLookup)).Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task A_missing_end_date_reads_Present_and_a_missing_verified_date_reads_Not_provided()
    {
        var created = await SeedAsync(ServiceHistoryStatus.Found, ServiceHistoryIntakeTests.Army with { ServiceEndDate = null, LastVerifiedOn = null });
        SignInAs(AppRoles.CaseWorker);

        var cut = RenderCase(created.Id);

        Assert.Equal("Present", cut.Find("[data-testid=service-end]").TextContent);
        Assert.Equal("Not provided", cut.Find("[data-testid=service-verified]").TextContent);
    }

    [TestCase(ServiceHistoryStatus.NotFound, "No service-history record was found at intake.")]
    [TestCase(ServiceHistoryStatus.Unavailable, "The service-history service was unavailable when this case was opened.")]
    [TestCase(null, "Not requested for this case.")]
    public async Task Without_a_record_one_sentence_says_why(ServiceHistoryStatus? status, string sentence)
    {
        var created = await SeedAsync(status, null);
        SignInAs(AppRoles.Auditor);

        var cut = RenderCase(created.Id);

        var section = cut.Find("[data-testid=service-history]");
        Assert.Equal(sentence, section.QuerySelector("p")!.TextContent);
        Assert.Empty(section.QuerySelectorAll("dl"));
        Assert.Empty(section.QuerySelectorAll(".message"));   // a stored fact, not a live warning
    }

    [Test]
    public async Task A_case_without_a_veteran_shows_no_service_history()
    {
        var created = await SeedCaseAsync("No veteran");
        SignInAs(AppRoles.CaseWorker);

        var cut = RenderCase(created.Id);

        Assert.Empty(cut.FindAll("[data-testid=service-history]"));
    }
}
