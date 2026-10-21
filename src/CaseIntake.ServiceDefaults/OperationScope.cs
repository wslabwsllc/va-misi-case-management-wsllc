using CaseIntake.Contracts.Common;
using Microsoft.Extensions.Logging;

namespace CaseIntake.ServiceDefaults;

/// <summary>
/// Correlation scope for logs. HTTP requests get one from <see cref="OperationContextMiddleware"/>; interactive
/// (Blazor circuit) actions begin their own so correlation never depends on an HTTP request. The ID is also the ambient
/// <see cref="OperationContext"/>, which audit entries store.
/// </summary>
public static class OperationScope
{
    public static IDisposable BeginOperation(this ILogger logger, string operationName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var operationId = NewOperationId();
        var context = OperationContext.Begin(operationId);
        var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OperationId"] = operationId,
            ["Operation"] = operationName,
        });
        return new Both(scope, context);
    }

    /// <summary>
    /// Always a fresh server-generated ID. It is deliberately not derived from <c>Activity.Current</c>: inside a Blazor
    /// circuit that is the long-lived connection activity (every action would share one ID), and for HTTP it can come
    /// from a client-supplied <c>traceparent</c>. TraceId/SpanId are still logged separately via activity tracking.
    /// </summary>
    public static string NewOperationId() => OperationContext.NewId();

    private sealed class Both(IDisposable? scope, IDisposable context) : IDisposable
    {
        public void Dispose()
        {
            scope?.Dispose();
            context.Dispose();
        }
    }
}
