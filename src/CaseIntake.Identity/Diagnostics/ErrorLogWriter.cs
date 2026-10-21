using System.Threading.Channels;
using CaseIntake.Contracts.Diagnostics;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Diagnostics;

/// <summary>
/// Stores captured errors (ADR 0015). <see cref="TryEnqueue"/> never blocks: the queue holds at most
/// <see cref="Capacity"/> errors and drops new ones when full. A background loop saves them in batches with capture
/// suppressed, so a failing database does not produce more errors to store; a batch that cannot be saved is dropped and
/// counted in a warning (console log only). Once a day, entries older than <c>ErrorLog:RetentionDays</c> (default 90)
/// are deleted. On shutdown the queue is drained.
/// </summary>
public sealed partial class ErrorLogWriter(
    IDbContextFactory<AppDbContext> contextFactory,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<ErrorLogWriter> logger) : BackgroundService, IErrorLogSink
{
    public const int Capacity = 1000;
    private const int BatchSize = 100;
    public const string RetentionDaysKey = "ErrorLog:RetentionDays";

    private readonly Channel<CapturedError> _queue = Channel.CreateBounded<CapturedError>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite });
    private DateTimeOffset _nextPurge = DateTimeOffset.MinValue;

    public bool TryEnqueue(CapturedError error) => _queue.Writer.TryWrite(error);

    /// <summary>Saves everything queued now (tests and shutdown).</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        var batch = new List<CapturedError>();
        while (_queue.Reader.TryRead(out var error))
        {
            batch.Add(error);
            if (batch.Count == BatchSize)
            {
                await SaveAsync(batch, ct);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            await SaveAsync(batch, ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var suppressed = ErrorCaptureSuppression.Begin();
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
                await PurgeIfDueAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        using var suppressed = ErrorCaptureSuppression.Begin();
        await FlushAsync(cancellationToken);
    }

    private async Task SaveAsync(List<CapturedError> batch, CancellationToken ct)
    {
        using var suppressed = ErrorCaptureSuppression.Begin();
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            db.ErrorLogEntries.AddRange(batch.Select(ErrorLogEntry.From));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorsDropped(logger, batch.Count, ex.GetType().Name);
        }
    }

    private async Task PurgeIfDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (now < _nextPurge)
        {
            return;
        }
        _nextPurge = now.AddDays(1);
        await PurgeAsync(ct);
    }

    /// <summary>Deletes entries older than the retention period now; returns how many.</summary>
    internal async Task<int> PurgeAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var days = int.TryParse(configuration[RetentionDaysKey], out var configured) && configured > 0 ? configured : 90;
        var cutoff = now.AddDays(-days);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            var deleted = await db.ErrorLogEntries.Where(e => e.OccurredAtUtc < cutoff).ExecuteDeleteAsync(ct);
            if (deleted > 0)
            {
                ErrorsPurged(logger, deleted, days);
            }
            return deleted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorsDropped(logger, 0, ex.GetType().Name);
            return 0;
        }
    }

    [LoggerMessage(EventId = 3040, EventName = "ErrorLogWriteFailed", Level = LogLevel.Warning,
        Message = "{Count} captured errors could not be stored and were dropped ({ExceptionType})")]
    private static partial void ErrorsDropped(ILogger logger, int count, string exceptionType);

    [LoggerMessage(EventId = 3041, EventName = "ErrorLogPurged", Level = LogLevel.Information,
        Message = "{Count} stored errors older than {Days} days were deleted")]
    private static partial void ErrorsPurged(ILogger logger, int count, int days);
}
