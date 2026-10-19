using CaseIntake.Domain.Attachments;
using CaseIntake.Domain.Cases;
using CaseIntake.Domain.TestResults;
using CaseIntake.Persistence.Auditing;
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
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseAuditEntry> CaseAuditEntries => Set<CaseAuditEntry>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    // Test results and evidence (E5 test management).
    public DbSet<TestEnvironment> TestEnvironments => Set<TestEnvironment>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<TestExecution> TestExecutions => Set<TestExecution>();
    public DbSet<TestStepResult> TestStepResults => Set<TestStepResult>();
    public DbSet<TestArtifact> TestArtifacts => Set<TestArtifact>();
    public DbSet<TestResultAuditEntry> TestResultAuditEntries => Set<TestResultAuditEntry>();
    public DbSet<TestExecutionHistoryRow> TestExecutionHistory => Set<TestExecutionHistoryRow>();

    // Every SaveChanges overload funnels into these two; the audit trail is append-only (E2-S4).
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AuditTrailGuard.Validate(ChangeTracker);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AuditTrailGuard.Validate(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

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
