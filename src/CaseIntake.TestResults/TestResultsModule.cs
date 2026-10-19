using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.TestResults.Import;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CaseIntake.TestResults;

/// <summary>Builds a results service bound to an explicitly chosen actor (the HTTP adapter for the evidence endpoints).</summary>
public interface ITestResultsServiceFactory
{
    ITestResultsService Create(ICurrentUser currentUser);
}

internal sealed class TestResultsServiceFactory(IServiceProvider services) : ITestResultsServiceFactory
{
    private static readonly ObjectFactory<TestResultsService> Factory =
        ActivatorUtilities.CreateFactory<TestResultsService>([typeof(ICurrentUser)]);

    public ITestResultsService Create(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        return Factory(services, [currentUser]);
    }
}

public static class TestResultsModule
{
    /// <summary>
    /// Registers <see cref="ITestResultsService"/> (scoped, circuit actor), <see cref="ITestResultsServiceFactory"/> for
    /// HTTP endpoints, the importer, retention and the background import. Requires <c>AddPersistence</c> and an
    /// <c>ICurrentUser</c> registration (E3). Options are validated at startup.
    /// </summary>
    public static IServiceCollection AddTestResultsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<TestResultsOptions>().BindConfiguration(TestResultsOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<TestResultsOptions>, TestResultsOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TestResultsOptions>, TestResultsOptionsSetup>());
        services.TryAddScoped<TestResultImporter>();
        services.TryAddScoped<TestArtifactRetention>();
        services.TryAddScoped<ITestResultsService, TestResultsService>();
        services.TryAddScoped<ITestResultsServiceFactory, TestResultsServiceFactory>();
        services.AddHostedService<TestResultsBackgroundService>();
        return services;
    }
}
