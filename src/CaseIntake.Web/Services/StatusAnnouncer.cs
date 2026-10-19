namespace CaseIntake.Web.Services;

/// <summary>
/// Circuit-scoped polite announcements (E5-S2). Pages call <see cref="Announce"/> after an operation succeeds; the
/// <c>StatusAnnouncerOutlet</c> in the layout renders the text into a persistent <c>role="status"</c> live region, so
/// the message survives in-app navigation (for example "Case 123 created" after intake moves to the detail page).
/// Errors are not announced here: they go into an inline <c>ErrorAlert</c> (<c>role="alert"</c>) next to the action.
/// </summary>
public sealed class StatusAnnouncer
{
    public string? Message { get; private set; }

    /// <summary>Incremented on every announcement so that an identical message is announced again.</summary>
    public int Sequence { get; private set; }

    public event Action? Changed;

    public void Announce(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Message = message;
        Sequence++;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Message is null)
        {
            return;
        }
        Message = null;
        Changed?.Invoke();
    }
}
