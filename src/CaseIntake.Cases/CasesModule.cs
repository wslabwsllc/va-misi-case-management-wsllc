using CaseIntake.Cases.Seeding;
using CaseIntake.Contracts.Cases;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CaseIntake.Cases;

public static class CasesModule
{
    /// <summary>
    /// Registers <see cref="ICaseService"/> (scoped, so it sees the circuit's or request's <c>ICurrentUser</c>) and the
    /// workflow. Requires <c>AddPersistence</c> and an <c>ICurrentUser</c> registration (E3).
    /// </summary>
    public static IServiceCollection AddCasesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICaseWorkflow>(CaseWorkflow.Instance);
        services.TryAddScoped<ICaseService, CaseService>();
        return services;
    }

    /// <summary>Registers the synthetic case seed (order 20, after Identity) with the migrator.</summary>
    public static IServiceCollection AddCasesSeed(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICaseWorkflow>(CaseWorkflow.Instance);
        return services.AddSeedContributor<CasesSeedContributor>();
    }
}
