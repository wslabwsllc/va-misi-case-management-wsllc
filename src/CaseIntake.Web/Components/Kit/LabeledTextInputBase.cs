using System.Diagnostics.CodeAnalysis;
using CaseIntake.Domain.Cases;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace CaseIntake.Web.Components.Kit;

/// <summary>
/// Shared behaviour of <see cref="LabeledInputText"/> and <see cref="LabeledTextArea"/> (E5-S2): a programmatic
/// <c>&lt;label for&gt;</c>, the required state in text, hint, error and counter text linked with
/// <c>aria-describedby</c>, <c>aria-invalid</c> from the <see cref="EditContext"/> (InputBase re-renders when the
/// validation state changes), and a character counter whose screen-reader announcement is debounced so it does not
/// interrupt typing.
/// </summary>
public abstract class LabeledTextInputBase : InputBase<string?>
{
    private static readonly TimeSpan AnnounceDelay = TimeSpan.FromMilliseconds(1200);
    private CancellationTokenSource? _announceDelay;

    [Parameter, EditorRequired] public string Id { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;
    [Parameter] public string? Hint { get; set; }
    [Parameter] public bool Required { get; set; }

    /// <summary>Shows "n of max characters"; lengths count Unicode scalar values like the server rules (OQ-12).</summary>
    [Parameter] public int? MaxCharacters { get; set; }

    protected ElementReference? InputElement { get; set; }

    /// <summary>The text the polite counter live region currently holds (updated after typing pauses).</summary>
    protected string? AnnouncedCount { get; private set; }

    public string ErrorId => $"{Id}-error";
    public string HintId => $"{Id}-hint";
    public string CounterId => $"{Id}-counter";

    protected IReadOnlyList<string> Errors => EditContext.GetValidationMessages(FieldIdentifier).ToList();

    protected bool IsInvalid => Errors.Count > 0;

    protected int CharacterCount => CurrentValue is { Length: > 0 } value
        ? CaseTextRules.CountScalarValues(value) ?? value.Length
        : 0;

    protected bool IsOverLimit => MaxCharacters is { } max && CharacterCount > max;

    protected string CounterText => MaxCharacters is { } max
        ? $"{CharacterCount:N0} of {max:N0} characters"
        : string.Empty;

    protected string? DescribedBy
    {
        get
        {
            List<string> ids = [];
            if (!string.IsNullOrEmpty(Hint)) ids.Add(HintId);
            if (IsInvalid) ids.Add(ErrorId);
            if (MaxCharacters is not null) ids.Add(CounterId);
            return ids.Count == 0 ? null : string.Join(' ', ids);
        }
    }

    public async ValueTask FocusAsync()
    {
        if (InputElement is { } element)
        {
            await element.FocusAsync();
        }
    }

    /// <summary>
    /// Runs after each keystroke's value is bound. The inputs use <c>@bind:event="oninput"</c> rather than a manual
    /// <c>value</c> + <c>@oninput</c> pair: only <c>@bind</c> lets the renderer reconcile with edits still in flight from the
    /// browser, so fast typing over a slow connection never loses characters.
    /// </summary>
    protected void AfterInput()
    {
        if (MaxCharacters is not null)
        {
            ScheduleCounterAnnouncement();
        }
    }

    private void ScheduleCounterAnnouncement()
    {
        _announceDelay?.Cancel();
        _announceDelay?.Dispose();
        var cts = _announceDelay = new CancellationTokenSource();
        _ = AnnounceLaterAsync(cts.Token);
    }

    private async Task AnnounceLaterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(AnnounceDelay, ct);
            var remaining = MaxCharacters!.Value - CharacterCount;
            AnnouncedCount = remaining >= 0
                ? $"{remaining:N0} characters remaining"
                : $"{-remaining:N0} characters too many";
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException)
        {
            // Typing continued; a later announcement replaces this one.
        }
    }

    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _announceDelay?.Cancel();
            _announceDelay?.Dispose();
        }
        base.Dispose(disposing);
    }
}
