using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

public class CaseEntityTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static Case NewCase(string? description = "Original description") =>
        Case.Create("  Original title  ", description, null, "creator", Created);

    [Fact]
    public void Create_sets_initial_status_audit_fields_and_normalized_title()
    {
        var entity = Case.Create("  Leaking roof  ", "", new CaseVeteran("Pat Example", "1 Example Way", "1234"), "worker", Created);

        Assert.Equal("Leaking roof", entity.Title);
        Assert.Null(entity.Description);
        Assert.Equal(CaseStatus.New, entity.Status);
        Assert.Equal(Created, entity.CreatedAtUtc);
        Assert.Equal(Created, entity.UpdatedAtUtc);
        Assert.Equal("worker", entity.CreatedBy);
        Assert.Equal("worker", entity.UpdatedBy);
        Assert.Equal(new CaseVeteran("Pat Example", "1 Example Way", "1234"), entity.Veteran);
        Assert.Equal(0, entity.Id);   // assigned by the database
    }

    [Fact]
    public void Create_normalizes_timestamps_to_utc()
    {
        var local = new DateTimeOffset(2026, 10, 16, 7, 0, 0, TimeSpan.FromHours(-5));

        var entity = Case.Create("Valid title", null, null, "worker", local);

        Assert.Equal(TimeSpan.Zero, entity.CreatedAtUtc.Offset);
        Assert.Equal(local.UtcDateTime, entity.CreatedAtUtc.UtcDateTime);
    }

    [Fact]
    public void Create_rejects_invalid_text_as_a_domain_guard()
    {
        var ex = Assert.Throws<CaseRuleException>(() => Case.Create("abc", null, null, "worker", Created));

        Assert.Equal(CaseRuleException.ValidationCode, ex.Code);
        Assert.Contains(ex.FieldErrors, e => e.Field == CaseFieldNames.Title);
    }

    [Theory]
    [InlineData("Original title", "Original description", new string[0])]
    [InlineData("  Original title ", "Original description", new string[0])]          // title is normalized before comparing
    [InlineData("New title", "Original description", new[] { "Title" })]
    [InlineData("Original title", "Changed description", new[] { "Description" })]
    [InlineData("Original title", "Original description ", new[] { "Description" })]   // description is not trimmed
    [InlineData("Original title", null, new[] { "Description" })]
    [InlineData("New title", "", new[] { "Title", "Description" })]
    [InlineData("original title", "Original description", new[] { "Title" })]          // ordinal comparison
    public void Change_detection_lists_changed_fields_in_order(string title, string? description, string[] expected) =>
        Assert.Equal(expected, NewCase().DetectChanges(title, description));

    [Fact]
    public void Empty_and_null_descriptions_are_the_same_value()
    {
        Assert.Empty(NewCase(description: null).DetectChanges("Original title", ""));
    }

    [Fact]
    public void Edit_applies_changes_and_refreshes_audit_fields()
    {
        var entity = NewCase();
        var later = Created.AddHours(1);

        var changed = entity.Edit("Updated title", "Original description", "editor", later);

        Assert.Equal(["Title"], changed);
        Assert.Equal("Updated title", entity.Title);
        Assert.Equal(later, entity.UpdatedAtUtc);
        Assert.Equal("editor", entity.UpdatedBy);
        Assert.Equal("creator", entity.CreatedBy);
        Assert.Equal(Created, entity.CreatedAtUtc);
    }

    [Fact]
    public void No_op_edit_changes_nothing()
    {
        var entity = NewCase();

        var changed = entity.Edit(" Original title ", "Original description", "editor", Created.AddHours(1));

        Assert.Empty(changed);
        Assert.Equal(Created, entity.UpdatedAtUtc);
        Assert.Equal("creator", entity.UpdatedBy);
    }

    [Fact]
    public void Closed_cases_cannot_be_edited()
    {
        var entity = NewCase();
        foreach (var status in new[] { CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Closed })
        {
            entity.TransitionTo(status, CaseWorkflow.Instance, "supervisor", Created.AddHours(1));
        }

        var ex = Assert.Throws<CaseRuleException>(() => entity.Edit("Another title", null, "editor", Created.AddHours(2)));
        Assert.Equal(CaseRuleException.CaseClosedCode, ex.Code);
    }

    [Fact]
    public void TransitionTo_follows_the_workflow_and_returns_the_previous_status()
    {
        var entity = NewCase();
        var at = Created.AddHours(3);

        var from = entity.TransitionTo(CaseStatus.Triaged, CaseWorkflow.Instance, "supervisor", at);

        Assert.Equal(CaseStatus.New, from);
        Assert.Equal(CaseStatus.Triaged, entity.Status);
        Assert.Equal(at, entity.UpdatedAtUtc);
        Assert.Equal("supervisor", entity.UpdatedBy);
    }

    [Theory]
    [InlineData(CaseStatus.Resolved)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.New)]
    [InlineData((CaseStatus)42)]
    public void TransitionTo_rejects_disallowed_targets_without_changing_the_case(CaseStatus target)
    {
        var entity = NewCase();

        var ex = Assert.Throws<CaseRuleException>(() => entity.TransitionTo(target, CaseWorkflow.Instance, "supervisor", Created.AddHours(1)));

        Assert.Equal(CaseRuleException.InvalidTransitionCode, ex.Code);
        Assert.Equal(CaseStatus.New, entity.Status);
        Assert.Equal(Created, entity.UpdatedAtUtc);
    }

    [Fact]
    public void Edited_audit_details_list_field_names_only()
    {
        var audit = AuditEvent.Edited(Created, ["Title", "Description"]);

        Assert.Equal(AuditActions.Edited, audit.Action);
        Assert.Equal("Changed: Title, Description", audit.Details);
    }

    [Fact]
    public void Audit_entries_are_linked_through_the_case_navigation()
    {
        var entity = NewCase();

        var entry = CaseAuditEntry.Record(entity, "user-1", "worker@example.test", AuditEvent.Created(Created, hasVeteranSnapshot: false));

        Assert.Same(entity, entry.Case);
        Assert.Equal(AuditActions.Created, entry.Action);
        Assert.Null(entry.FromStatus);
        Assert.Equal(CaseStatus.New, entry.ToStatus);
        Assert.Equal("user-1", entry.ActorId);
        Assert.Equal("worker@example.test", entry.Actor);
    }

    [Fact]
    public void Actor_names_longer_than_the_column_are_capped_on_create_and_on_update()
    {
        var longActor = new string('a', 300);
        var entity = Case.Create("Valid title", null, null, longActor, new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(CaseFieldLimits.ActorMaxLength, entity.CreatedBy.Length);
        Assert.Equal(CaseFieldLimits.ActorMaxLength, entity.UpdatedBy.Length);

        entity.Edit("Another valid title", null, new string('b', 400), new DateTimeOffset(2026, 10, 18, 13, 0, 0, TimeSpan.Zero));
        Assert.Equal(new string('b', CaseFieldLimits.ActorMaxLength), entity.UpdatedBy);
    }
}
