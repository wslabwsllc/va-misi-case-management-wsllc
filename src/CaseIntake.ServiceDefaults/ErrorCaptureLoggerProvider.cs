using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CaseIntake.ServiceDefaults;

/// <summary>
/// Copies every Error and Critical log event into the stored error log (ADR 0015) through <see cref="IErrorLogSink"/>.
/// Applies the console formatter's rules: the rendered message with SSN-shaped values redacted, and the exception's type
/// and stack trace (both redacted) but never its message. The correlation ID is the ambient operation ID. Never blocks
/// and never throws into the code that logged. Events logged while the sink itself is writing are skipped
/// (<see cref="ErrorCaptureSuppression"/>), so a database outage cannot feed back into itself.
/// </summary>
public sealed class ErrorCaptureLoggerProvider(IServiceProvider services, TimeProvider time) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider? _scopes;
    private IErrorLogSink? _sink;

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    // Resolved lazily: the sink depends on logging itself.
    private IErrorLogSink? Sink => _sink ??= services.GetService<IErrorLogSink>();

    private sealed class CaptureLogger(ErrorCaptureLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error && !ErrorCaptureSuppression.IsActive;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || provider.Sink is not { } sink)
            {
                return;
            }
            try
            {
                string? requestPath = null;
                provider._scopes?.ForEachScope((scope, _) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> values)
                    {
                        foreach (var (key, value) in values)
                        {
                            if (key == "RequestPath" && value is string path)
                            {
                                requestPath = path;
                            }
                        }
                    }
                }, (object?)null);

                var message = formatter(state, exception);
                sink.TryEnqueue(new CapturedError(
                    provider.Now,
                    logLevel == LogLevel.Critical ? ErrorLevel.Critical : ErrorLevel.Error,
                    category,
                    eventId.Id,
                    eventId.Name,
                    SsnRedaction.Redact(string.IsNullOrEmpty(message) ? "(no message)" : message),
                    exception?.GetType().FullName,
                    exception is null ? null : SsnRedaction.Redact(StackTraceOnly(exception)),
                    OperationContext.CurrentId,
                    requestPath is null ? null : SsnRedaction.Redact(requestPath)));
            }
            catch (Exception)
            {
                // Capturing must never break the code that logged.
            }
        }
    }

    private DateTimeOffset Now => time.GetUtcNow();

    /// <summary>"Type" lines and stack frames of the exception and its inner exceptions, without any message text.</summary>
    internal static string StackTraceOnly(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().FullName}{Environment.NewLine}{current.StackTrace}");
        }
        return string.Join($"{Environment.NewLine}--- inner exception ---{Environment.NewLine}", parts).Trim();
    }
}

