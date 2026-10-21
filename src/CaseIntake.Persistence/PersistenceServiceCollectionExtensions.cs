using CaseIntake.Contracts.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace CaseIntake.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = ConnectionStringNames.CaseIntake;

    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);

    /// <summary>
    /// Registers <see cref="IDbContextFactory{AppDbContext}"/> (singleton) and <see cref="TimeProvider.System"/>.
    /// Callers create and dispose one context per operation. Retries are enabled, so explicit transactions must run
    /// inside <c>db.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The <c>CaseIntake</c> connection string is not configured.</exception>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail at startup rather than silently connecting somewhere else.
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings__{ConnectionStringName}.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AppDbContext>(options => options
            .UseNpgsql(connectionString, ConfigureNpgsql)
            .EnableSensitiveDataLogging(false)
            .EnableDetailedErrors(false));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<Auditing.AuditLogWriter>();
        return services;
    }
}
