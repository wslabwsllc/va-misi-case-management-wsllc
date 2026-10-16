using CaseIntake.Persistence;
using CaseIntake.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// One-shot worker: apply migrations (retrying while the database starts), run seed contributors, exit 0/1.
// Each epic registers its contributor with one line: builder.Services.AddSeedContributor<XxxSeedContributor>();
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults(includeHealthChecks: false);
builder.Services.AddPersistence(builder.Configuration);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CaseIntake.MigrationService");
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

// Starting the host installs the console lifetime's SIGTERM/SIGINT handlers, so `docker compose down` or Ctrl+C
// trips ApplicationStopping and cancels the database backoff instead of waiting for the container kill.
await host.StartAsync();
var exitCode = 1;
try
{
    using var operation = logger.BeginOperation("migrate-and-seed");
    var initializer = host.Services.GetRequiredService<DatabaseInitializer>();
    await initializer.MigrateAsync(options: null, lifetime.ApplicationStopping);
    await initializer.SeedAsync(lifetime.ApplicationStopping);
    logger.LogInformation(new EventId(1005, "MigrationServiceCompleted"), "Migration and seeding completed");
    exitCode = 0;
}
catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
{
    logger.LogWarning(new EventId(1007, "MigrationServiceCancelled"), "Migration or seeding was cancelled by a shutdown signal");
}
catch (Exception ex)
{
    logger.LogCritical(new EventId(1006, "MigrationServiceFailed"), "Migration or seeding failed ({ExceptionType})", ex.GetType().Name);
}

await host.StopAsync();   // flushes the console logger before the process exits
return exitCode;
