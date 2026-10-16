using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CaseIntake.Persistence;

/// <summary>Used only by <c>dotnet ef</c>. The connection string is a local placeholder; migrations never connect.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>Synthetic local-only credentials, identical to docker-compose.yml. Never a real secret.</summary>
    private const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=caseintake;Username=caseintake;Password=caseintake_local_only;Timezone=UTC";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{PersistenceServiceCollectionExtensions.ConnectionStringName}")
            ?? PlaceholderConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, PersistenceServiceCollectionExtensions.ConfigureNpgsql)
            .Options;
        return new AppDbContext(options);
    }
}
