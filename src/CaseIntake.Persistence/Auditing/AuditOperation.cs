using CaseIntake.Contracts.Common;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Persistence.Auditing;

/// <summary>
/// Starts an operation for a service call that changes data when none is running: an interactive (circuit) action has
/// no HTTP request, so it would otherwise have no operation ID. Sets the ambient <see cref="OperationContext"/> (the
/// audit correlation ID) and an <c>OperationId</c> log scope. Inside an HTTP request or an outer operation it does
/// nothing, so the request's ID is kept.
/// </summary>
public static class AuditOperation
{
    public static IDisposable BeginIfNone(ILogger logger, string operationName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (OperationContext.CurrentId is not null)
        {
            return NoOp.Instance;
        }

        var operationId = OperationContext.NewId();
        var context = OperationContext.Begin(operationId);
        var scope = logger.BeginScope(new Dictionary<string, object> { ["OperationId"] = operationId, ["Operation"] = operationName });
        return new Both(scope, context);
    }

    private sealed class NoOp : IDisposable
    {
        public static NoOp Instance { get; } = new();
        public void Dispose()
        {
        }
    }

    private sealed class Both(IDisposable? scope, IDisposable context) : IDisposable
    {
        public void Dispose()
        {
            scope?.Dispose();
            context.Dispose();
        }
    }
}
