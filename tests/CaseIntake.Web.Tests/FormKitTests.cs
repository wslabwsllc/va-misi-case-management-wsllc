using CaseIntake.Web.Components.Kit;
using CaseIntake.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.Web.Tests;

/// <summary>E5-S2: the accessible form and feedback kit.</summary>
[Category("Story:E5-S2")]
[Category("Req:R-4.2-12")]
public class FormKitTests : WebTestContext
{
    private sealed class Model
    {
        public string? Title { get; set; }
        public string? Notes { get; set; }
    }

    private readonly Model _model = new();
    private readonly EditContext _context;
    private readonly ValidationMessageStore _messages;

    public FormKitTests()
    {
        _context = new EditContext(_model);
        _messages = new ValidationMessageStore(_context);
    }

    private IRenderedComponent<CascadingValue<EditContext>> RenderInContext(RenderFragment content) =>
        Render<CascadingValue<EditContext>>(p => p.Add(c => c.Value, _context).Add(c => c.IsFixed, false).AddChildContent(content));

    private RenderFragment Input(string? hint = "Short summary", bool required = true, int? max = 200) => builder =>
    {
        builder.OpenComponent<LabeledInputText>(0);
        builder.AddComponentParameter(1, nameof(LabeledInputText.Id), "title");
        builder.AddComponentParameter(2, nameof(LabeledInputText.Label), "Title");
        builder.AddComponentParameter(3, nameof(LabeledInputText.Hint), hint);
        builder.AddComponentParameter(4, nameof(LabeledInputText.Required), required);
        builder.AddComponentParameter(5, nameof(LabeledInputText.MaxCharacters), max);
        builder.AddComponentParameter(6, nameof(LabeledInputText.Value), _model.Title);
        builder.AddComponentParameter(7, nameof(LabeledInputText.ValueChanged), EventCallback.Factory.Create<string?>(this, v => _model.Title = v));
        builder.AddComponentParameter(8, nameof(LabeledInputText.ValueExpression), (System.Linq.Expressions.Expression<Func<string?>>)(() => _model.Title));
        builder.CloseComponent();
    };

    [Test]
    public void Input_has_a_programmatic_label_required_text_and_describedby_for_hint_and_counter()
    {
        var cut = RenderInContext(Input());

        var label = cut.Find("label[for=title]");
        Assert.Contains("Title", label.TextContent);
        Assert.Contains("(required)", label.TextContent);
        var input = cut.Find("input#title");
        Assert.Equal("true", input.GetAttribute("aria-required"));
        Assert.Equal("title-hint title-counter", input.GetAttribute("aria-describedby"));
        Assert.Equal("Short summary", cut.Find("#title-hint").TextContent);
        Assert.Equal("0 of 200 characters", cut.Find("#title-counter").TextContent);
        Assert.Null(input.GetAttribute("aria-invalid"));
    }

    [Test]
    public void Invalid_field_gets_aria_invalid_an_inline_message_and_describedby_to_it()
    {
        var cut = RenderInContext(Input());

        cut.InvokeAsync(() =>
        {
            _messages.Add(new FieldIdentifier(_model, nameof(Model.Title)), "Title is required.");
            _context.NotifyValidationStateChanged();
        });

        var input = cut.Find("input#title");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Contains("title-error", input.GetAttribute("aria-describedby"));
        Assert.Equal("Title is required.", cut.Find("#title-error").TextContent.Trim());
    }

    [Test]
    public void Counter_counts_unicode_scalar_values_and_announces_politely_after_typing_pauses()
    {
        var cut = RenderInContext(Input(max: 5));
        cut.Find("input#title").Input("ab😀cdef");

        Assert.Equal("7 of 5 characters", cut.Find("#title-counter").TextContent);
        Assert.Contains("field__counter--over", cut.Find("#title-counter").ClassList);
        var live = cut.Find(".field [role=status]");
        Assert.Equal("polite", live.GetAttribute("aria-live"));
        cut.WaitForAssertion(() => Assert.Equal("2 characters too many", cut.Find(".field [role=status]").TextContent), TimeSpan.FromSeconds(5));
    }

    [Test]
    public void Text_area_labels_optional_fields_in_text()
    {
        var cut = RenderInContext(builder =>
        {
            builder.OpenComponent<LabeledTextArea>(0);
            builder.AddComponentParameter(1, nameof(LabeledTextArea.Id), "notes");
            builder.AddComponentParameter(2, nameof(LabeledTextArea.Label), "Notes");
            builder.AddComponentParameter(3, nameof(LabeledTextArea.MaxCharacters), 4000);
            builder.AddComponentParameter(4, nameof(LabeledTextArea.ValueExpression), (System.Linq.Expressions.Expression<Func<string?>>)(() => _model.Notes));
            builder.CloseComponent();
        });

        Assert.Contains("(optional)", cut.Find("label[for=notes]").TextContent);
        Assert.Equal("notes-counter", cut.Find("textarea#notes").GetAttribute("aria-describedby"));
        Assert.Equal("0 of 4,000 characters", cut.Find("#notes-counter").TextContent);
    }

    [Test]
    public void Error_summary_lists_linked_messages_and_takes_focus()
    {
        _messages.Add(new FieldIdentifier(_model, nameof(Model.Title)), "Title is required.");
        IRenderedComponent<FormErrorSummary>? summary = null;
        var cut = RenderInContext(builder =>
        {
            builder.OpenComponent<FormErrorSummary>(0);
            builder.AddComponentParameter(1, nameof(FormErrorSummary.Visible), true);
            builder.AddComponentParameter(2, nameof(FormErrorSummary.Fields), (IReadOnlyList<KeyValuePair<string, string>>)[new("Title", "title")]);
            builder.CloseComponent();
        });
        summary = cut.FindComponent<FormErrorSummary>();

        var region = cut.Find("#error-summary");
        Assert.Equal("alert", region.GetAttribute("role"));
        Assert.Equal("-1", region.GetAttribute("tabindex"));
        Assert.Equal("There is a problem", cut.Find("#error-summary-title").TextContent);
        var link = cut.Find("#error-summary a");
        Assert.Equal("#title", link.GetAttribute("href"));
        Assert.Equal("Title is required.", link.TextContent);

        cut.InvokeAsync(() => summary.Instance.FocusAsync().AsTask());
        JSInterop.VerifyFocusAsyncInvoke();

        link.Click();
        Assert.Equal("#title", InteropModule.VerifyInvoke("focusSelector").Arguments[0]);
    }

    [Test]
    public void Error_alert_uses_role_alert_and_renders_nothing_visible_without_a_message()
    {
        var empty = Render<ErrorAlert>();
        Assert.Equal("alert", empty.Find("[role=alert]").GetAttribute("role"));
        Assert.Empty(empty.Find("[role=alert]").TextContent.Trim());

        var cut = Render<ErrorAlert>(p => p.Add(a => a.Title, "The status was not changed").Add(a => a.Message, "This case was changed by someone else."));
        var alert = cut.Find("[role=alert]");
        Assert.Contains("The status was not changed", alert.TextContent);
        Assert.Contains("message--error", alert.ClassList);
    }

    [Test]
    public void Status_announcer_speaks_through_a_polite_status_region()
    {
        var cut = Render<StatusAnnouncerOutlet>();
        var region = cut.Find("[data-testid=status-announcer]");
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));

        cut.InvokeAsync(() => Services.GetRequiredService<StatusAnnouncer>().Announce("Case 123 created"));

        cut.WaitForAssertion(() => Assert.Equal("Case 123 created", cut.Find("[data-testid=status-announcer]").TextContent), TimeSpan.FromSeconds(3));
    }

    [Test]
    public void Status_pill_shows_the_status_as_text()
    {
        var cut = Render<StatusPill>(p => p.Add(s => s.Status, Domain.Cases.CaseStatus.InProgress));
        Assert.Equal("In Progress", cut.Find(".status-pill").TextContent);
    }

    [Test]
    public void Page_shell_sets_a_unique_document_title_and_a_focusable_h1()
    {
        var cut = Render<PageShell>(p => p.Add(s => s.Title, "Cases").Add(s => s.Breadcrumbs, [new Crumb("Dashboard", "")]));
        Assert.Equal("-1", cut.Find("h1").GetAttribute("tabindex"));
        var shell = Services.GetRequiredService<ShellState>();
        Assert.Equal("Cases", shell.Title);
        Assert.Equal("", shell.BackHref);
        Assert.Equal(["Dashboard", "Cases"], shell.Breadcrumbs.Select(c => c.Text));
    }
}
