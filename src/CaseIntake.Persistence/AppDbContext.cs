using CaseIntake.Persistence.Conventions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CaseIntake.Persistence;

/// <summary>
/// The application database. Never retain an instance for a circuit's lifetime: create one per operation through
/// <see cref="IDbContextFactory{TContext}"/> and dispose it before returning. Entity configurations are owned by
/// the epic named in their folder under Configurations/ and applied from this assembly.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, string>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Every stored timestamp is timestamptz and UTC (E1-S5).
        const string timestamptz = "timestamp with time zone";
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveColumnType(timestamptz).HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>()
            .HaveColumnType(timestamptz).HaveConversion<UtcNullableDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType(timestamptz).HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveColumnType(timestamptz).HaveConversion<UtcNullableDateTimeConverter>();
    }
}
