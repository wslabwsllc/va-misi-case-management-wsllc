using System.Data.Common;
using System.Security.Cryptography;
using CaseIntake.Attachments;
using CaseIntake.Attachments.Storage;
using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Attachments;
using CaseIntake.Domain.Cases;
using CaseIntake.IntegrationTests.Cases;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CaseIntake.IntegrationTests.Attachments;

/// <summary>
/// A real <see cref="AttachmentService"/> and filesystem store over a test database and a private temporary root,
/// with a settable actor, fake clock, captured logs and fault injection for the store and the database.
/// </summary>
internal sealed class AttachmentTestHarness : IDisposable
{
    public AttachmentTestHarness(PostgresDatabaseFixture database, string? root = null)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), "caseintake-attachments-" + Guid.NewGuid().ToString("N"));
        OwnsRoot = root is null;
        RetrySimulator = new RetryAfterCommitSimulator(database, QueryFaults);
        Cases = new CaseTestHarness(database, Faults, CommitFaults, QueryFaults, RetrySimulator);
        Options = Microsoft.Extensions.Options.Options.Create(new AttachmentOptions { RootPath = Root });
        new AttachmentOptionsSetup().PostConfigure(null, Options.Value);
        Store = new FaultyStore(new FileSystemAttachmentStore(Options));
        Service = new AttachmentService(Cases.Factory, CurrentUser, Store, Options, Time, Logs);
    }

    public string Root { get; }
    private bool OwnsRoot { get; }
    public CaseTestHarness Cases { get; }
    public SaveFaults Faults { get; } = new();
    public CommitFaults CommitFaults { get; } = new();
    public RetryAfterCommitSimulator RetrySimulator { get; }
    public QueryFaults QueryFaults { get; } = new();
    public IOptions<AttachmentOptions> Options { get; }
    public FaultyStore Store { get; }
    public FakeCurrentUser CurrentUser { get; } = new() { Actor = TestActors.CaseWorker };
    public FakeTimeProvider Time { get; } = new(CaseTestHarness.StartTime);
    public FakeLogger<AttachmentService> Logs { get; } = new();
    public AttachmentService Service { get; }

    public OperationActor Actor
    {
        get => CurrentUser.Actor;
        set => CurrentUser.Actor = value;
    }

    public Task<CaseDetailDto> CreateCaseAsync() => Cases.CreateAsync();

    public async Task<AttachmentDto> UploadAsync(long caseId, byte[] bytes, string fileName = "notes.txt", string contentType = "text/plain")
    {
        Time.Advance(TimeSpan.FromSeconds(1));
        return CaseTestHarness.Ok(await Service.UploadAsync(caseId, fileName, contentType, bytes.Length, new MemoryStream(bytes), CancellationToken.None));
    }

    public async Task<int> CountAttachmentsAsync()
    {
        await using var db = Cases.Database.CreateContext();
        return await db.Attachments.CountAsync();
    }

    public string[] FilesUnderRoot() => Directory.Exists(Root) ? Directory.GetFiles(Root, "*", SearchOption.AllDirectories) : [];

    /// <summary>Random bytes that start like a PDF, so they pass the signature check when uploaded as one.</summary>
    public static byte[] RandomBytes(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        "%PDF-1.7\n"u8.CopyTo(bytes);
        return bytes;
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose()
    {
        if (OwnsRoot && Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

/// <summary>Wraps the real store so a test can make promotion or deletion fail.</summary>
internal sealed class FaultyStore(IAttachmentStore inner) : IAttachmentStore
{
    public bool FailPromote { get; set; }
    public bool FailDelete { get; set; }
    public bool FailDeleteFile { get; set; }

    public Task<StagedFile> StageAsync(Stream content, long maxBytes, CancellationToken ct) => inner.StageAsync(content, maxBytes, ct);

    public void Promote(StagedFile staged, string key)
    {
        if (FailPromote)
        {
            throw new IOException("Simulated storage failure.");
        }
        inner.Promote(staged, key);
    }

    public Stream? OpenRead(string key) => inner.OpenRead(key);

    public void Delete(string key)
    {
        if (FailDelete)
        {
            throw new IOException("Simulated delete failure.");
        }
        inner.Delete(key);
    }

    public void DeleteStaged(StagedFile staged) => inner.DeleteStaged(staged);
    public void DeleteFile(StoredFile file)
    {
        if (FailDeleteFile)
        {
            throw new IOException("Simulated delete failure.");
        }
        inner.DeleteFile(file);
    }
    public IEnumerable<StoredFile> EnumerateTemporaryFiles() => inner.EnumerateTemporaryFiles();
    public IEnumerable<StoredFile> EnumerateContent() => inner.EnumerateContent();
}

/// <summary>Makes SaveChanges fail before it reaches the database.</summary>
internal sealed class SaveFaults : SaveChangesInterceptor
{
    public bool FailBeforeCommit { get; set; }

    /// <summary>When set, a failed save also makes every later database command fail (the commit check cannot run).</summary>
    public QueryFaults? BreakQueriesOnFailure { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailBeforeCommit)
        {
            if (BreakQueriesOnFailure is { } queries)
            {
                queries.Fail = true;
            }
            throw new DbUpdateException("Simulated database failure before commit.");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>Throws after a transaction has committed, like a lost acknowledgement from the database.</summary>
internal sealed class CommitFaults : DbTransactionInterceptor
{
    public bool FailAfterCommit { get; set; }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        FailAfterCommit
            ? throw new DbUpdateException("Simulated failure after the commit (a lost acknowledgement).")
            : base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
}

/// <summary>Fails every database command while <see cref="Fail"/> is set.</summary>
internal sealed class QueryFaults : DbCommandInterceptor
{
    public bool Fail { get; set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
        Fail ? throw new InvalidOperationException("Simulated database outage.") : base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
}

public enum RetryAfterCommitMode { Off, Committed, Uncertain }

/// <summary>
/// Reproduces what the production retrying execution strategy can do, without needing a transient fault: just before
/// the commit transaction reads the case <c>FOR SHARE</c>, it writes the pending attachment row from a separate
/// connection (as if an earlier attempt's COMMIT had landed) and closes the case (as if a supervisor closed it during
/// the retry delay). The service then sees Closed and must confirm the commit before deleting anything. In
/// <see cref="RetryAfterCommitMode.Uncertain"/> every later command fails, so the confirmation itself cannot run.
/// </summary>
internal sealed class RetryAfterCommitSimulator(PostgresDatabaseFixture database, QueryFaults queries) : DbCommandInterceptor
{
    public RetryAfterCommitMode Mode { get; set; }

    private static bool IsCaseLock(DbCommand command) => command.CommandText.Contains("FOR SHARE", StringComparison.Ordinal);

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Mode != RetryAfterCommitMode.Off && IsCaseLock(command))
        {
            var pending = eventData.Context!.ChangeTracker.Entries<Attachment>().Single(e => e.State == EntityState.Added).Entity;
            await using var other = database.CreateContext();
            await other.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Attachments" ("Id", "CaseId", "FileName", "ContentType", "SizeBytes", "Sha256", "StorageKey",
                                           "UploadedAtUtc", "UploadedById", "UploadedBy")
                VALUES ({pending.Id}, {pending.CaseId}, {pending.FileName}, {pending.ContentType}, {pending.SizeBytes},
                        {pending.Sha256}, {pending.StorageKey}, {pending.UploadedAtUtc}, {pending.UploadedById}, {pending.UploadedBy})
                """, cancellationToken);
            await other.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Cases" SET "Status" = {(int)CaseStatus.Closed} WHERE "Id" = {pending.CaseId}""", cancellationToken);
        }
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (Mode == RetryAfterCommitMode.Uncertain && IsCaseLock(command))
        {
            queries.Fail = true;   // the lock read itself succeeded; the commit confirmation that follows cannot run
        }
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }
}
