using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.Persistence.Seeding;

internal sealed record SeedContributorRegistration(Type ImplementationType, int Order);

public static class SeedServiceCollectionExtensions
{
    /// <summary>Registers a seed contributor with the migrator.</summary>
    public static IServiceCollection AddSeedContributor<T>(this IServiceCollection services)
        where T : class, ISeedContributor
    {
        services.AddTransient<T>();
        services.AddSingleton(new SeedContributorRegistration(typeof(T), T.Order));
        return services;
    }
}
