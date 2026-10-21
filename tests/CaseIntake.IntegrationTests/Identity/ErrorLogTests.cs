using System.Net;
using System.Text.RegularExpressions;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Diagnostics;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Diagnostics;
using CaseIntake.Identity.Seeding;
using CaseIntake.Persistence;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>The real host plus <c>/test/boom</c>, which throws inside the application pipeline (after every endpoint).</summary>
public sealed partial class ErrorLogWebFixture : PostgresDatabaseFixture, IAsyncLifetime
{
    public const string BoomPath = "/test/boom";

    /// <summary>The exception message: it must never be stored (it stands for submitted values).</summary>
    public const string SecretMessage = "secret-value 900-45-6789";

    public CaseIntakeWebFactory Factory { get; private set; } = null!;

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await IdentityTestSupport.SeedAsync(ConnectionString);
        Factory = new CaseIntakeWebFactory(ConnectionString, "http://127.0.0.1:1",
            configureServices: services => services.AddSingleton<IStartupFilter>(new BoomFilter()));
    }

    public new async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
        await base.DisposeAsync();
    }

    public BrowserSession NewSession() =>
        new(Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }));

    private sealed class BoomFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((HttpContext context, Func<Task> nextMiddleware) => context.Request.Path == BoomPath
                ? throw new InvalidOperationException(SecretMessage)
                : nextMiddleware());
        };
    }
}

/// <summary>ADR 0015: capture, safe storage, reference matching, status handling, retention and System Health.</summary>
public partial class ErrorLogTests(ErrorLogWebFixture fixture) : IClassFixture<ErrorLogWebFixture>
{
    private IServiceProvider Services => fixture.Factory.Services;
    private ErrorLogWriter Writer => Services.GetRequiredService<ErrorLogWriter>();

    private ErrorLogService ErrorsAs(OperationActor actor) => new(
        Services.GetRequiredService<IDbContextFactory<AppDbContext>>(), new FakeCurrentUser { Actor = actor },
        Services.GetRequiredService<TimeProvider>(), Services.GetRequiredService<ILogger<ErrorLogService>>());

    private static OperationActor Admin => FakeCurrentUser.ForRole(AppRoles.Administrator, DemoUsers.Administrator.Id);

    [GeneratedRegex("Reference: <code>([0-9a-f]+)</code>")]
    private static partial Regex ReferencePattern();

    [Fact]
    public async Task An_unhandled_request_error_is_stored_under_the_reference_the_error_page_shows_without_its_message()
    {
        using var session = fixture.NewSession();
        await session.SignInAsync(DemoUsers.CaseWorker.UserName);

        using var response = await session.Client.GetAsync(ErrorLogWebFixture.BoomPath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var reference = ReferencePattern().Match(html).Groups[1].Value;
        Assert.Equal(response.Headers.GetValues("X-Operation-Id").Single(), reference);
        Assert.DoesNotContain("secret-value", html);

        await Writer.FlushAsync(CancellationToken.None);
        var found = (await ErrorsAs(Admin).FindByReferenceAsync(reference, CancellationToken.None)).Value!;
        var detail = (await ErrorsAs(Admin).GetAsync(Assert.Single(found).Id, CancellationToken.None)).Value!;
        Assert.Equal(ErrorLevel.Error, detail.Level);
        Assert.Equal(ErrorLogWebFixture.BoomPath, detail.RequestPath);
        Assert.Equal(typeof(InvalidOperationException).FullName, detail.ExceptionType);
        Assert.Contains("   at ", detail.StackTrace);
        Assert.Equal(ErrorStatus.Open, detail.Status);
        await using var db = fixture.CreateContext();
        Assert.False(await db.ErrorLogEntries.AnyAsync(e => e.Message.Contains("secret-value") || (e.StackTrace ?? "").Contains("secret-value")
            || e.Message.Contains("900-45-6789") || (e.StackTrace ?? "").Contains("900-45-6789")));
    }

    [Fact]
    public async Task Only_errors_are_stored_redacted_with_the_operation_id_and_never_while_suppressed()
    {
        var logger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("CaseIntake.Probe");
        var tag = Guid.NewGuid().ToString("N")[..8];
        using (OperationContext.Begin($"op-{tag}"))
        {
            logger.LogWarning("warning {Tag}", tag);
            logger.LogError("error {Tag} for 900-12-3456", tag);
            logger.LogCritical(new TimeoutException("hidden message"), "critical {Tag}", tag);
            using (ErrorCaptureSuppression.Begin())
            {
                logger.LogError("suppressed {Tag}", tag);
            }
        }
        await Writer.FlushAsync(CancellationToken.None);

        var stored = (await ErrorsAs(Admin).ListAsync(new ErrorQuery(Search: tag), CancellationToken.None)).Value!.Items;

        Assert.Equal(2, stored.Count);
        var error = Assert.Single(stored, e => e.Level == ErrorLevel.Error);
        Assert.Equal($"error {tag} for [REDACTED-SSN]", error.Message);
        Assert.Equal($"op-{tag}", error.CorrelationId);
        var critical = (await ErrorsAs(Admin).GetAsync(Assert.Single(stored, e => e.Level == ErrorLevel.Critical).Id, CancellationToken.None)).Value!;
        Assert.Equal(typeof(TimeoutException).FullName, critical.ExceptionType);
        Assert.DoesNotContain("hidden message", critical.StackTrace ?? "");
    }

    [Fact]
    public async Task Same_errors_share_a_fingerprint_and_status_changes_are_audited_and_version_checked()
    {
        var logger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("CaseIntake.Repeat");
        var tag = Guid.NewGuid().ToString("N")[..8];
        logger.LogError(new EventId(2999, "Repeat"), "repeat {Tag} first", tag);
        logger.LogError(new EventId(2999, "Repeat"), "repeat {Tag} second", tag);
        await Writer.FlushAsync(CancellationToken.None);
        var service = ErrorsAs(Admin);
        var rows = (await service.ListAsync(new ErrorQuery(Search: $"repeat {tag}"), CancellationToken.None)).Value!.Items;
        Assert.Equal(2, rows.Count);
        Assert.Single(rows.Select(r => r.Fingerprint).Distinct());
        var detail = (await service.GetAsync(rows[0].Id, CancellationToken.None)).Value!;
        Assert.Equal(2, detail.Occurrences);

        var resolved = await service.SetStatusAsync(detail.Id, new SetErrorStatusRequest(ErrorStatus.Resolved, "Known issue", detail.Version), CancellationToken.None);
        var stale = await service.SetStatusAsync(detail.Id, new SetErrorStatusRequest(ErrorStatus.Open, null, detail.Version), CancellationToken.None);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal(ErrorStatus.Resolved, resolved.Value!.Status);
        Assert.Equal(Admin.DisplayName, resolved.Value.StatusChangedBy);
        Assert.Equal(ErrorKind.Conflict, stale.Error!.Kind);
        await using var db = fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(a => a.EntityType == AuditEntityTypes.Error && a.EntityId == detail.Id.ToString());
        Assert.Equal(AuditLogActions.ErrorStatusChanged, audit.Action);
        Assert.Contains("Known issue", audit.After);
        Assert.Single((await service.ListAsync(new ErrorQuery(Search: $"repeat {tag}", Status: ErrorStatus.Resolved), CancellationToken.None)).Value!.Items);
    }

    [Fact]
    public async Task Reading_needs_errors_view_and_changing_status_needs_errors_manage()
    {
        var viewer = FakeCurrentUser.Authenticated("viewer", "viewer", "Custom", [Permissions.ErrorsView]);
        var nobody = FakeCurrentUser.ForRole(AppRoles.Supervisor);

        Assert.Equal(ErrorKind.Forbidden, (await ErrorsAs(nobody).ListAsync(new ErrorQuery(), CancellationToken.None)).Error!.Kind);
        Assert.True((await ErrorsAs(viewer).ListAsync(new ErrorQuery(), CancellationToken.None)).IsSuccess);
        Assert.Equal(ErrorKind.Forbidden, (await ErrorsAs(viewer).SetStatusAsync(1, new SetErrorStatusRequest(ErrorStatus.Resolved, null, 0), CancellationToken.None)).Error!.Kind);
    }

    [Fact]
    public async Task Retention_deletes_only_entries_older_than_the_configured_days()
    {
        var logger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("CaseIntake.Retention");
        var tag = Guid.NewGuid().ToString("N")[..8];
        logger.LogError("retention {Tag} old", tag);
        logger.LogError("retention {Tag} new", tag);
        await Writer.FlushAsync(CancellationToken.None);
        await using (var db = fixture.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"ErrorLogEntries\" SET \"OccurredAtUtc\" = now() - interval '91 days' WHERE \"Message\" = {0}", $"retention {tag} old");
        }

        Assert.True(await Writer.PurgeAsync(CancellationToken.None) >= 1);

        var left = (await ErrorsAs(Admin).ListAsync(new ErrorQuery(Search: $"retention {tag}"), CancellationToken.None)).Value!.Items;
        Assert.Equal([$"retention {tag} new"], left.Select(e => e.Message));
    }

    [Fact]
    public async Task System_health_reports_the_database_migrations_checks_and_counts_to_permitted_users_only()
    {
        SystemHealthService Health(OperationActor actor) => new(
            Services.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>(),
            Services.GetRequiredService<IDbContextFactory<AppDbContext>>(), Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
            new FakeCurrentUser { Actor = actor }, Services.GetRequiredService<TimeProvider>(), Services.GetRequiredService<ILogger<SystemHealthService>>());

        var health = (await Health(Admin).GetAsync(CancellationToken.None)).Value!;
        var denied = await Health(FakeCurrentUser.ForRole(AppRoles.Supervisor)).GetAsync(CancellationToken.None);

        Assert.True(health.DatabaseReachable);
        await using var db = fixture.CreateContext();
        Assert.Equal(db.Database.GetMigrations().Count(), health.AppliedMigrations);
        Assert.Empty(health.PendingMigrations);
        Assert.NotEmpty(health.Checks);
        // The lookup mock is not running in this host, so its check (and the overall state) is not healthy.
        Assert.Contains(health.Checks, c => c.State != HealthState.Healthy);
        Assert.NotEqual(HealthState.Healthy, health.Overall);
        Assert.Equal(["Error", "Critical"], health.ErrorsLast24Hours.Select(c => c.Label));
        Assert.Equal(ErrorKind.Forbidden, denied.Error!.Kind);
    }
}
