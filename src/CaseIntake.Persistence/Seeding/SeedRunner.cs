using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CaseIntake.Persistence.Seeding;

/// <summary>Executes every registered <see cref="ISeedContributor"/> in <c>ISeedContributor.Order</c>, each in a fresh DI scope.</summary>
public sealed partial class SeedRunner(IServiceProvider services, ILogger<SeedRunner> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var plan = DiscoverOrder();
        foreach (var (type, order) in plan)
        {
            ct.ThrowIfCancellationRequested();
            LogSeedStarting(type.Name, order);
            await using var scope = services.CreateAsyncScope();
            var contributor = (ISeedContributor)scope.ServiceProvider.GetRequiredService(type);
            try
            {
                await contributor.SeedAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSeedFailed(ex, type.Name);
                throw;
            }
            LogSeedCompleted(type.Name);
        }
    }

    private List<(Type Type, int Order)> DiscoverOrder() =>
    [
        .. services.GetServices<SeedContributorRegistration>()
            .DistinctBy(r => r.ImplementationType)
            .OrderBy(r => r.Order)
            .ThenBy(r => r.ImplementationType.FullName, StringComparer.Ordinal)
            .Select(r => (r.ImplementationType, r.Order)),
    ];

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Seed contributor {Contributor} starting (order {Order})")]
    private partial void LogSeedStarting(string contributor, int order);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Seed contributor {Contributor} completed")]
    private partial void LogSeedCompleted(string contributor);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Error, Message = "Seed contributor {Contributor} failed")]
    private partial void LogSeedFailed(Exception exception, string contributor);
}
