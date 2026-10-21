namespace CaseIntake.Contracts.Common;

/// <summary>
/// The ambient operation ID of the current logical operation (an HTTP request or one interactive action). Logs carry it
/// as the <c>OperationId</c> scope and audit entries store it as their correlation ID, so an audit row leads to its log
/// lines. Flows with the async call chain (AsyncLocal); <see cref="Begin"/> restores the previous value on dispose.
/// </summary>
public static class OperationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>The current operation ID, or <c>null</c> outside any operation.</summary>
    public static string? CurrentId => Current.Value;

    /// <summary>A fresh server-generated ID (never taken from the client).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Makes <paramref name="operationId"/> (or a new ID) current until the result is disposed.</summary>
    public static IDisposable Begin(string? operationId = null)
    {
        var previous = Current.Value;
        Current.Value = operationId ?? NewId();
        return new Restore(previous);
    }

    /// <summary>The current ID, or a new one when there is no operation (the caller should then begin one).</summary>
    public static string CurrentOrNew() => Current.Value ?? NewId();

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
