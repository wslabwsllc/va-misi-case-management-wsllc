using CaseIntake.Contracts.Cases;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

/// <summary>E2-C2-1: the service-history snapshot rules, applied the same way by the request validator and the entity.</summary>
[Category("Story:E2-C2-1")]
[Category("Req:R-CR2-2")]
[Category("Req:R-CR2-4")]
public class ServiceHistoryRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2001, 2, 15);

    private static ServiceHistory History(string branch = "Army", DateOnly? start = null, DateOnly? end = null,
        string character = "Honorable", DateOnly? verified = null) =>
        new(branch, start ?? Start, end, character, verified);

    /// <summary>Field names the validator reports, or empty when valid; the entity must reject exactly when this is non-empty.</summary>
    private static string[] ErrorsFor(ServiceHistoryStatus? status, ServiceHistory? history)
    {
        var request = new CreateCaseRequest("Valid title", null, new VeteranSnapshot("Pat Example", "1 Example Way", "0000", status, history));
        var fields = CaseRequestValidator.Validate(request)?.FieldErrors?.Keys.Order(StringComparer.Ordinal).ToArray() ?? [];

        var veteran = new CaseVeteran("Pat Example", "1 Example Way", "0000", status, history);
        var entityFields = Array.Empty<string>();
        try
        {
            Case.Create("Valid title", null, veteran, "worker", Now);
        }
        catch (CaseRuleException ex)
        {
            entityFields = [.. ex.FieldErrors.Select(e => e.Field).Distinct().Order(StringComparer.Ordinal)];
        }

        Assert.Equal(fields, entityFields);
        return fields;
    }

    [Test]
    public void Found_with_a_complete_history_is_valid() =>
        Assert.Empty(ErrorsFor(ServiceHistoryStatus.Found, History(end: new DateOnly(2005, 2, 14), verified: new DateOnly(2026, 1, 5))));

    [Test]
    public void Found_requires_a_history() =>
        Assert.Equal([CaseFieldNames.Veteran], ErrorsFor(ServiceHistoryStatus.Found, null));

    [TestCase(null)]
    [TestCase(ServiceHistoryStatus.NotFound)]
    [TestCase(ServiceHistoryStatus.Unavailable)]
    public void Any_other_status_rejects_a_history_and_accepts_none(ServiceHistoryStatus? status)
    {
        Assert.Equal([CaseFieldNames.Veteran], ErrorsFor(status, History()));
        Assert.Empty(ErrorsFor(status, null));
    }

    [Test]
    public void An_undefined_status_is_rejected() =>
        Assert.Equal([CaseFieldNames.Veteran], ErrorsFor((ServiceHistoryStatus)7, null));

    [TestCase("")]
    [TestCase("   ")]
    public void Blank_branch_and_character_of_service_are_required(string blank) =>
        Assert.Equal([CaseFieldNames.ServiceBranch, CaseFieldNames.CharacterOfService],
            ErrorsFor(ServiceHistoryStatus.Found, History(branch: blank, character: blank)));

    [TestCase(50, 100, false)]
    [TestCase(51, 100, true)]
    [TestCase(50, 101, true)]
    public void Branch_and_character_of_service_are_capped_at_50_and_100(int branchLength, int characterLength, bool invalid)
    {
        var fields = ErrorsFor(ServiceHistoryStatus.Found, History(branch: new string('b', branchLength), character: new string('c', characterLength)));

        if (!invalid)
        {
            Assert.Empty(fields);
            return;
        }
        Assert.Equal([branchLength > 50 ? CaseFieldNames.ServiceBranch : CaseFieldNames.CharacterOfService], fields);
    }

    [Test]
    public void Branch_and_character_of_service_are_free_text() =>
        Assert.Empty(ErrorsFor(ServiceHistoryStatus.Found, History(branch: "Space Force", character: "Uncharacterized (entry level)")));

    [Test]
    public void Start_after_end_is_rejected_but_equal_dates_and_an_open_end_are_accepted()
    {
        Assert.Equal([CaseFieldNames.ServiceEndDate], ErrorsFor(ServiceHistoryStatus.Found, History(end: Start.AddDays(-1))));
        Assert.Empty(ErrorsFor(ServiceHistoryStatus.Found, History(end: Start)));
        Assert.Empty(ErrorsFor(ServiceHistoryStatus.Found, History(end: null)));
    }

    [Test]
    public void A_case_without_a_veteran_cannot_carry_a_history_status()
    {
        // The status lives on the veteran snapshot, so it cannot reach a case without one; the request is rejected for
        // the missing veteran and CK_Case_ServiceHistory_RequiresVeteran guards the table.
        var error = CaseRequestValidator.Validate(new CreateCaseRequest("Valid title", null, null));

        Assert.Equal([CaseFieldNames.Veteran], error!.FieldErrors!.Keys);
        Assert.Null(Case.Create("Valid title", null, null, "worker", Now).ServiceHistoryStatus);
    }

    [Test]
    public void The_entity_stores_and_returns_the_snapshot()
    {
        var history = History(end: new DateOnly(2005, 2, 14), verified: new DateOnly(2026, 1, 5));
        var veteran = new CaseVeteran("Pat Example", "1 Example Way", "0000", ServiceHistoryStatus.Found, history);

        var entity = Case.Create("Valid title", null, veteran, "worker", Now);

        Assert.Equal(veteran, entity.Veteran);
        Assert.Equal(ServiceHistoryStatus.Found, entity.ServiceHistoryStatus);
        Assert.Equal("Army", entity.ServiceBranch);
        Assert.Equal(new DateOnly(2026, 1, 5), entity.ServiceRecordVerifiedOn);
    }

    [TestCase(ServiceHistoryStatus.NotFound)]
    [TestCase(ServiceHistoryStatus.Unavailable)]
    [TestCase(null)]
    public void Other_outcomes_round_trip_without_history(ServiceHistoryStatus? status)
    {
        var veteran = new CaseVeteran("Pat Example", "1 Example Way", "0000", status);

        Assert.Equal(veteran, Case.Create("Valid title", null, veteran, "worker", Now).Veteran);
    }

    [Test]
    public void Created_audit_details_name_the_status_only()
    {
        Assert.Equal("Veteran snapshot recorded; service history: NotFound",
            AuditEvent.Created(Now, hasVeteranSnapshot: true, ServiceHistoryStatus.NotFound).Details);
        Assert.Equal("Veteran snapshot recorded", AuditEvent.Created(Now, hasVeteranSnapshot: true).Details);
        Assert.Null(AuditEvent.Created(Now, hasVeteranSnapshot: false).Details);
    }
}
