using CaseIntake.Cases.Seeding;
using CaseIntake.Contracts.Cases;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

/// <summary>Issue #22: triage defaults, the overdue rule, validation and change detection on the entity.</summary>
[Category("Story:Issue-22")]
public class CaseTriageRulesTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 16, 23, 30, 0, TimeSpan.Zero);

    [TestCase(CasePriority.Urgent, 2)]
    [TestCase(CasePriority.High, 5)]
    [TestCase(CasePriority.Medium, 10)]
    [TestCase(CasePriority.Low, 30)]
    public void Default_due_date_counts_target_days_from_the_utc_creation_date(CasePriority priority, int days)
    {
        Assert.Equal(new DateOnly(2026, 10, 16).AddDays(days), CaseTriageRules.DefaultDueOn(priority, Created));
        // A non-UTC offset is converted first: 2026-10-17 01:30 +02:00 is still 2026-10-16 in UTC.
        Assert.Equal(new DateOnly(2026, 10, 16).AddDays(days), CaseTriageRules.DefaultDueOn(priority, Created.ToOffset(TimeSpan.FromHours(2))));
    }

    [TestCase(CaseStatus.New, "2026-10-19", true)]
    [TestCase(CaseStatus.Triaged, "2026-10-19", true)]
    [TestCase(CaseStatus.InProgress, "2026-10-19", true)]
    [TestCase(CaseStatus.InProgress, "2026-10-20", false)]   // due today is not overdue yet
    [TestCase(CaseStatus.Resolved, "2026-10-01", false)]
    [TestCase(CaseStatus.Closed, "2026-10-01", false)]
    [TestCase(CaseStatus.New, null, false)]                  // no due date, never overdue
    public void Overdue_means_open_and_due_before_today(CaseStatus status, string? due, bool overdue)
    {
        DateOnly? dueOn = due is null ? null : DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(overdue, CaseTriageRules.IsOverdue(status, dueOn, new DateOnly(2026, 10, 20)));
    }

    [Test]
    public void Validation_names_each_invalid_field()
    {
        var errors = CaseTriageRules.Validate(new CaseTriage((CasePriority)7, (CaseCategory)(-1), new string('x', 300), new DateOnly(2101, 1, 1)));

        Assert.Equal([CaseFieldNames.Priority, CaseFieldNames.Category, CaseFieldNames.AssignedTo, CaseFieldNames.DueOn], errors.Select(e => e.Field));
        Assert.Empty(CaseTriageRules.Validate(CaseTriage.Default));
        Assert.Empty(CaseTriageRules.Validate(new CaseTriage(CasePriority.Low, CaseCategory.ContactDetails, "owner@example.test", new DateOnly(2100, 12, 31))));
    }

    [Test]
    public void The_request_validator_includes_triage_errors_for_intake_and_edit()
    {
        var bad = new CaseTriage(CasePriority.Low, (CaseCategory)50, null, null);

        Assert.Contains(CaseFieldNames.Category, CaseRequestValidator.Validate(new CreateCaseRequest("Valid title", null, null, bad))!.FieldErrors!.Keys);
        Assert.Contains(CaseFieldNames.Category, CaseRequestValidator.Validate(new UpdateCaseRequest("Valid title", null, 1, bad))!.FieldErrors!.Keys);
        Assert.Null(CaseRequestValidator.Validate(new UpdateCaseRequest("Valid title", null, 1)));
    }

    [Test]
    public void Create_stores_the_triage_with_a_trimmed_owner_and_defaults_without_one()
    {
        var withTriage = Case.Create("Triage on create", null, null, "creator", Created, triage: new CaseTriage(CasePriority.High, CaseCategory.Travel, "  owner@example.test ", new DateOnly(2026, 11, 1)));
        var plain = Case.Create("No triage given", null, null, "creator", Created);

        Assert.Equal(new CaseTriage(CasePriority.High, CaseCategory.Travel, "owner@example.test", new DateOnly(2026, 11, 1)), withTriage.Triage);
        Assert.Equal(CaseTriage.Default, plain.Triage);
        Assert.Throws<CaseRuleException>(() => Case.Create("Bad triage case", null, null, "creator", Created, triage: CaseTriage.Default with { Priority = (CasePriority)9 }));
    }

    [Test]
    public void Edit_reports_changed_triage_fields_in_order_and_ignores_triage_when_not_given()
    {
        var entity = Case.Create("Edit triage", null, null, "creator", Created, triage: new CaseTriage(CasePriority.Medium, CaseCategory.General, "a@example.test", new DateOnly(2026, 10, 26)));
        var later = Created.AddHours(1);

        var changed = entity.Edit("Edit triage", null, "editor", later, new CaseTriage(CasePriority.Urgent, CaseCategory.Claims, "", new DateOnly(2026, 10, 18)));

        Assert.Equal([CaseFieldNames.Priority, CaseFieldNames.Category, CaseFieldNames.DueOn], changed);
        Assert.Equal("a@example.test", entity.AssignedTo);   // an edit never changes the owner; only Assign does
        Assert.Equal(later, entity.UpdatedAtUtc);
        Assert.Empty(entity.Edit("Edit triage", null, "editor", later.AddHours(1)));                 // text unchanged, triage not given
        Assert.Empty(entity.Edit("Edit triage", null, "editor", later.AddHours(1), entity.Triage));   // same triage
        Assert.Equal(later, entity.UpdatedAtUtc);
    }

    [Test]
    public void Seeded_triage_is_derived_from_the_index_and_covers_every_priority_and_a_mix_of_owners()
    {
        var fixtures = CaseSeedData.Build();

        Assert.Equal(fixtures.Select((_, i) => CaseSeedData.TriageFor(i)), fixtures.Select(f => f.Triage));
        Assert.All(Enum.GetValues<CasePriority>(), p => Assert.Contains(fixtures, f => f.Triage.Priority == p));
        Assert.Contains(fixtures, f => f.Triage.AssignedTo is null);
        Assert.Contains(fixtures, f => f.Triage.AssignedTo == CaseSeedData.CaseWorkerUserName);
        Assert.Contains(fixtures, f => f.Triage.AssignedTo == CaseSeedData.SupervisorUserName);
        Assert.All(fixtures, f => Assert.InRange(f.Triage.DueOn!.Value, CaseSeedData.FirstDueOn, CaseSeedData.FirstDueOn.AddDays(CaseSeedData.DueSpreadDays - 1)));
        Assert.Equal(CaseCategory.Facilities, fixtures[0].Triage.Category);   // "Water damage"
        Assert.Equal(CaseCategory.Eligibility, fixtures[11].Triage.Category);  // "Eligibility review"
    }
}
