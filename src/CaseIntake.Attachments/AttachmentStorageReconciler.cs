using CaseIntake.Attachments.Storage;
using CaseIntake.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.Attachments;

/// <summary>
/// Startup cleanup of attachment storage (E4-S5, decision D9). Runs in the migrator after migrations and seeding,
/// before the web app accepts uploads. Removes temporary files and content with no committed metadata once they are
/// older than the grace period, and counts attachments whose content is missing. Content with metadata is never
/// deleted. Failures are logged and retried on the next start; they never stop the application from starting.
/// The migrator calls it explicitly (<see cref="AttachmentsModule.ReconcileAttachmentStorageAsync"/>) after seeding.
/// </summary>
internal sealed class AttachmentStorageReconciler(
    IDbContextFactory<AppDbContext> contextFactory,
    IAttachmentStore store,
    IOptions<AttachmentOptions> options,
    TimeProvider timeProvider,
    ILogger<AttachmentStorageReconciler> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var cutoff = timeProvider.GetUtcNow() - options.Value.OrphanGracePeriod;
            int tempDeleted = 0, orphansDeleted = 0, failures = 0;

            foreach (var file in store.EnumerateTemporaryFiles().Where(f => f.LastWriteUtc < cutoff).ToList())
            {
                if (TryDelete(file, ref failures))
                {
                    tempDeleted++;
                }
            }

            await using var db = await contextFactory.CreateDbContextAsync(ct);
            var referenced = await db.Attachments.AsNoTracking().Select(a => a.StorageKey).ToHashSetAsync(StringComparer.Ordinal, ct);
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in store.EnumerateContent().ToList())
            {
                if (file.Key is { } key && referenced.Contains(key))
                {
                    present.Add(key);
                }
                else if (file.LastWriteUtc < cutoff && TryDelete(file, ref failures))
                {
                    orphansDeleted++;
                }
            }

            AttachmentLog.Reconciled(logger, tempDeleted, orphansDeleted, referenced.Count - present.Count, failures);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AttachmentLog.ReconcileFailed(logger, ex.GetType().Name);
        }
    }

    private bool TryDelete(StoredFile file, ref int failures)
    {
        try
        {
            store.DeleteFile(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures++;
            return false;
        }
    }
}
