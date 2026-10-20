namespace CaseIntake.Web.Services;

/// <summary>
/// Circuit-scoped "work in progress" counter behind the top progress bar in the layout (issue #21). A page wraps each
/// load or refresh in <c>using var busy = Activity.Begin();</c>; the bar shows while any work is running, so every page
/// shows the same progress feedback without its own markup.
/// </summary>
public sealed class PageActivity
{
    private int _running;

    public bool IsBusy => _running > 0;

    public event Action? Changed;

    public IDisposable Begin()
    {
        if (Interlocked.Increment(ref _running) == 1)
        {
            Changed?.Invoke();
        }
        return new Scope(this);
    }

    private void End()
    {
        if (Interlocked.Decrement(ref _running) == 0)
        {
            Changed?.Invoke();
        }
    }

    private sealed class Scope(PageActivity owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.End();
            }
        }
    }
}

/// <summary>
/// Process-wide signal that case activity happened (a case was created, edited, moved or received a file), so open
/// circuits refresh their notification badge at once instead of waiting for the next poll (issue #21). Carries no
/// data: each circuit re-reads its own unread count through the authorized notification service.
/// </summary>
public sealed class CaseActivitySignal
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
