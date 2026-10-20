using CaseIntake.Attachments;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CaseIntake.Web.Health;

/// <summary>
/// Attachment storage is usable: the store's <c>tmp/</c> folder under <c>Attachments:RootPath</c> accepts a write and
/// a delete. Uploads fail without it, so the app is not ready. The probe file uses a <c>.probe</c> extension, which the
/// orphan reconciler (it removes <c>*.part</c> files) never touches.
/// </summary>
internal sealed class AttachmentStorageHealthCheck(IOptions<AttachmentOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var tempDirectory = Path.Combine(Path.GetFullPath(options.Value.RootPath), "tmp");
            Directory.CreateDirectory(tempDirectory);
            var probe = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".probe");
            await File.WriteAllBytesAsync(probe, [0], cancellationToken);
            File.Delete(probe);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The path is configuration, not user data, but the message is kept generic like the other checks.
            return HealthCheckResult.Unhealthy("Attachment storage is not writable.", exception: null,
                data: new Dictionary<string, object> { ["errorType"] = ex.GetType().Name });
        }
    }
}
