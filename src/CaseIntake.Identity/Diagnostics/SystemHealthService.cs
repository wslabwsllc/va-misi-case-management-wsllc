using System.Diagnostics;
using System.Reflection;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Diagnostics;
using CaseIntake.Contracts.Security;
using CaseIntake.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Identity.Diagnostics;

/// <summary>
/// System Health (ADR 0015, <c>system.health</c>): the registered health checks run now, the app version, environment and
/// start time, whether the database answers and which migrations are applied or pending, and counts from the error log
/// and the audit log for the last 24 hours. A database that does not answer is shown, not thrown.
/// </summary>
internal sealed class SystemHealthService(
    HealthCheckService healthChecks,
    IDbContextFactory<AppDbContext> contextFactory,
    IHostEnvironment environment,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<SystemHealthService> logger) : ISystemHealthService
{
    private static readonly DateTimeOffset StartedAt = new(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);

    public async Task<Result<SystemHealthDto>> GetAsync(CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.SystemHealth))
        {
            IdentityLog.AuthorizationDenied(logger, actor.UserId, Permissions.SystemHealth, "SystemHealth");
            return Result<SystemHealthDto>.Failure(new(ErrorKind.Forbidden, "Forbidden", "You do not have permission to perform this action."));
        }

        var now = time.GetUtcNow();
        var report = await healthChecks.CheckHealthAsync(ct);
        var checks = report.Entries
            .Select(e => new HealthCheckDto(e.Key, Map(e.Value.Status), e.Value.Description, e.Value.Duration))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        var reachable = false;
        var applied = 0;
        IReadOnlyList<string> pending = [];
        var openErrors = 0;
        IReadOnlyList<CountDto> errors = [];
        IReadOnlyList<CountDto> security = [];
        IReadOnlyList<ErrorSummaryDto> latest = [];
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            reachable = await db.Database.CanConnectAsync(ct);
            if (reachable)
            {
                applied = (await db.Database.GetAppliedMigrationsAsync(ct)).Count();
                pending = [.. await db.Database.GetPendingMigrationsAsync(ct)];
                var since = now.AddHours(-24);
                openErrors = await db.ErrorLogEntries.CountAsync(e => e.Status == ErrorStatus.Open, ct);
                var byLevel = await db.ErrorLogEntries.Where(e => e.OccurredAtUtc >= since)
                    .GroupBy(e => e.Level).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
                errors = [.. Enum.GetValues<ErrorLevel>().Select(l => new CountDto(l.ToString(), byLevel.FirstOrDefault(b => b.Key == l)?.Count ?? 0))];
                string[] watched = [AuditLogActions.SignInFailed, AuditLogActions.SignInLockedOut, AuditLogActions.AccessDenied];
                var byAction = await db.AuditLogEntries.Where(a => a.OccurredAtUtc >= since && watched.Contains(a.Action))
                    .GroupBy(a => a.Action).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
                security = [.. watched.Select(a => new CountDto(a, byAction.FirstOrDefault(b => b.Key == a)?.Count ?? 0))];
                latest = [.. (await db.ErrorLogEntries.AsNoTracking().OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
                    .Take(5).ToListAsync(ct)).Select(ErrorLogService.Summary)];
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Shown on the page as "not reachable"; the exception itself is logged (and captured) by EF.
            reachable = false;
        }

        var overall = checks.Count == 0 ? HealthState.Healthy : checks.Max(c => c.State);
        if (!reachable || pending.Count > 0)
        {
            overall = HealthState.Unhealthy;
        }
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return Result<SystemHealthDto>.Success(new SystemHealthDto(now, overall, checks, version, environment.EnvironmentName, StartedAt,
            reachable, applied, pending, openErrors, errors, security, latest));
    }

    private static HealthState Map(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => HealthState.Healthy,
        HealthStatus.Degraded => HealthState.Degraded,
        _ => HealthState.Unhealthy,
    };
}
