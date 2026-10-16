using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CaseIntake.ServiceDefaults;

/// <summary>Opens a logging scope carrying an operation ID for each HTTP request and echoes it in <c>X-Operation-Id</c>.</summary>
public sealed class OperationContextMiddleware(RequestDelegate next, ILogger<OperationContextMiddleware> logger)
{
    public const string HeaderName = "X-Operation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var operationId = OperationScope.NewOperationId();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = operationId;
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { ["OperationId"] = operationId }))
        {
            await next(context);
        }
    }
}
