using CaseIntake.Domain.TestResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseIntake.Persistence.Configurations.TestResults;

// Test results and evidence (E5 test management), created by the E5_TestResults migration. Results are imported once
// and kept: every foreign key restricts deletes, and retention only marks evidence rows as purged.

internal sealed class TestEnvironmentConfiguration : IEntityTypeConfiguration<TestEnvironment>
{
    public void Configure(EntityTypeBuilder<TestEnvironment> builder)
    {
        builder.ToTable("TestEnvironments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Key).HasMaxLength(TestResultFieldLimits.EnvironmentKeyMaxLength).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength).IsRequired();
        builder.Property(e => e.OperatingSystem).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength).IsRequired();
        builder.Property(e => e.Runtime).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength).IsRequired();
        builder.Property(e => e.BaseUrl).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength);
        builder.HasIndex(e => e.Key).IsUnique();
    }
}

internal sealed class TestRunConfiguration : IEntityTypeConfiguration<TestRun>
{
    public void Configure(EntityTypeBuilder<TestRun> builder)
    {
        builder.ToTable("TestRuns", t =>
        {
            t.HasCheckConstraint("CK_TestRun_Counts_NonNegative",
                "\"Total\" >= 0 AND \"Passed\" >= 0 AND \"Failed\" >= 0 AND \"Skipped\" >= 0 AND \"Blocked\" >= 0 AND \"NotExecuted\" >= 0");
            t.HasCheckConstraint("CK_TestRun_Status", "\"Status\" BETWEEN 0 AND 2");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RunKey).HasMaxLength(TestResultFieldLimits.RunKeyMaxLength).IsRequired();
        builder.Property(r => r.Runner).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength).IsRequired();
        builder.Property(r => r.GitBranch).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength);
        builder.Property(r => r.GitCommit).HasMaxLength(TestResultFieldLimits.GitCommitMaxLength);
        builder.Property(r => r.BuildId).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength);
        builder.Property(r => r.ImageVersion).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength);
        builder.Property(r => r.SuitesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.Property(r => r.SourcePath).HasMaxLength(TestResultFieldLimits.PathMaxLength).IsRequired();
        builder.Property(r => r.ImportedBy).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength).IsRequired();

        builder.HasOne(r => r.Environment).WithMany().HasForeignKey(r => r.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.RunKey).IsUnique();
        builder.HasIndex(r => r.StartedAtUtc);
    }
}

internal sealed class TestCaseConfiguration : IEntityTypeConfiguration<TestCase>
{
    public void Configure(EntityTypeBuilder<TestCase> builder)
    {
        builder.ToTable("TestCases", t => t.HasCheckConstraint("CK_TestCase_Key_Hex", "\"Key\" ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Key).HasMaxLength(TestResultFieldLimits.Sha256HexLength).IsFixedLength().IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(TestResultFieldLimits.FullNameMaxLength).IsRequired();
        builder.Property(c => c.Title).HasMaxLength(TestResultFieldLimits.NameMaxLength).IsRequired();
        builder.Property(c => c.Module).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.Property(c => c.Story).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength);
        builder.Property(c => c.Requirements).HasMaxLength(TestResultFieldLimits.NameMaxLength);
        builder.Property(c => c.AcceptanceCriteria).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.Property(c => c.Priority).HasMaxLength(TestResultFieldLimits.PriorityMaxLength);
        builder.Property(c => c.Preconditions).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.Property(c => c.ExpectedResult).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.HasIndex(c => c.Key).IsUnique();
        builder.HasIndex(c => new { c.Module, c.Type });
    }
}

internal sealed class TestExecutionConfiguration : IEntityTypeConfiguration<TestExecution>
{
    public void Configure(EntityTypeBuilder<TestExecution> builder)
    {
        builder.ToTable("TestExecutions", t => t.HasCheckConstraint("CK_TestExecution_Outcome", "\"Outcome\" BETWEEN 0 AND 4"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Browser).HasMaxLength(TestResultFieldLimits.BrowserMaxLength);
        builder.Property(e => e.Viewport).HasMaxLength(TestResultFieldLimits.BrowserMaxLength);
        builder.Property(e => e.ActualResult).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.Property(e => e.ErrorMessage).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.Property(e => e.StackTrace).HasMaxLength(TestResultFieldLimits.LongTextMaxLength);
        builder.Property(e => e.StandardOutput).HasMaxLength(TestResultFieldLimits.LongTextMaxLength);
        builder.Property(e => e.ConsoleLog).HasMaxLength(TestResultFieldLimits.LongTextMaxLength);
        builder.Property(e => e.NetworkLog).HasMaxLength(TestResultFieldLimits.LongTextMaxLength);

        builder.HasOne(e => e.Run).WithMany(r => r.Executions).HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.TestCase).WithMany().HasForeignKey(e => e.TestCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Steps).WithOne().HasForeignKey(s => s.ExecutionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Artifacts).WithOne().HasForeignKey(a => a.ExecutionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.RunId, e.TestCaseId }).IsUnique();
        builder.HasIndex(e => new { e.RunId, e.Outcome });
        builder.HasIndex(e => new { e.TestCaseId, e.RunId });
    }
}

internal sealed class TestStepResultConfiguration : IEntityTypeConfiguration<TestStepResult>
{
    public void Configure(EntityTypeBuilder<TestStepResult> builder)
    {
        builder.ToTable("TestStepResults");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(TestResultFieldLimits.NameMaxLength).IsRequired();
        builder.Property(s => s.Expected).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.Property(s => s.Actual).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.Property(s => s.Message).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.HasIndex(s => new { s.ExecutionId, s.Order }).IsUnique();
    }
}

internal sealed class TestArtifactConfiguration : IEntityTypeConfiguration<TestArtifact>
{
    public void Configure(EntityTypeBuilder<TestArtifact> builder)
    {
        builder.ToTable("TestArtifacts", t => t.HasCheckConstraint("CK_TestArtifact_Size", "\"SizeBytes\" >= 0"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.FileName).HasMaxLength(TestResultFieldLimits.NameMaxLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(TestResultFieldLimits.Sha256HexLength).IsRequired();
        builder.Property(a => a.RelativePath).HasMaxLength(TestResultFieldLimits.PathMaxLength).IsRequired();
        builder.Property(a => a.Caption).HasMaxLength(TestResultFieldLimits.NameMaxLength);

        builder.HasOne(a => a.Run).WithMany().HasForeignKey(a => a.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.RunId, a.Kind });
    }
}

internal sealed class TestResultAuditEntryConfiguration : IEntityTypeConfiguration<TestResultAuditEntry>
{
    public void Configure(EntityTypeBuilder<TestResultAuditEntry> builder)
    {
        builder.ToTable("TestResultAuditEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ActorId).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength);
        builder.Property(a => a.Actor).HasMaxLength(TestResultFieldLimits.ShortTextMaxLength).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(TestResultFieldLimits.AuditActionMaxLength).IsRequired();
        builder.Property(a => a.Details).HasMaxLength(TestResultFieldLimits.MessageMaxLength);
        builder.HasIndex(a => a.OccurredAtUtc);
    }
}

/// <summary>The read-only <c>TestExecutionHistory</c> view; its SQL is in the E5_TestResults migration.</summary>
internal sealed class TestExecutionHistoryRowConfiguration : IEntityTypeConfiguration<TestExecutionHistoryRow>
{
    public void Configure(EntityTypeBuilder<TestExecutionHistoryRow> builder)
    {
        builder.HasNoKey();
        builder.ToView("TestExecutionHistory");
    }
}
