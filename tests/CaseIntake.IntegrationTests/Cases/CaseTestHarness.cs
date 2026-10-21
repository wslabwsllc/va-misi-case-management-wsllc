using System.Collections.Immutable;
using CaseIntake.Cases;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>Role actors matching the adopted permission matrix (plan E3-S2). E3 owns the production map.</summary>
internal static class TestActors
{
    private static readonly string[] Common =
    [
        Permissions.CasesView, Permissions.CasesCreate, Permissions.CasesEdit, Permissions.VeteranLookup,
        Permissions.AttachmentsUpload, Permissions.AttachmentsDownload,
    ];

    public static OperationActor CaseWorker { get; } = FakeCurrentUser.Authenticated("cw-id", "caseworker@example.test", AppRoles.CaseWorker,
    [
        .. Common,
        Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),
        Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),
    ]);

    public static OperationActor Supervisor { get; } = FakeCurrentUser.Authenticated("sv-id", "supervisor@example.test", AppRoles.Supervisor,
    [
        .. Common,
        Permissions.CasesAssign,
        Permissions.Transition(CaseStatus.New, CaseStatus.Triaged),
        Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress),
        Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved),
        Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed),
        Permissions.Transition(CaseStatus.Resolved, CaseStatus.InProgress),
    ]);

    public static OperationActor ViewOnly { get; } = FakeCurrentUser.Authenticated("viewer-id", "viewer@example.test", "Viewer", [Permissions.CasesView]);

    public static OperationActor NoPermissions { get; } =
        new("nobody-id", "nobody@example.test", ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty);
}

/// <summary>A real <see cref="CaseService"/> over a test database with a settable actor, fake clock and captured logs.</summary>
internal sealed class CaseTestHarness
{
    public static readonly DateTimeOffset StartTime = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    public CaseTestHarness(PostgresDatabaseFixture database, params IInterceptor[] interceptors)
    {
        Database = database;
        Factory = new InterceptingFactory(database.ConnectionString, interceptors);
        Service = new CaseService(Factory, CurrentUser, CaseWorkflow.Instance, Time, Logs);
    }

    public PostgresDatabaseFixture Database { get; }
    public IDbContextFactory<AppDbContext> Factory { get; }
    public FakeCurrentUser CurrentUser { get; } = new() { Actor = TestActors.Supervisor };
    public FakeTimeProvider Time { get; } = new(StartTime);
    public FakeLogger<CaseService> Logs { get; } = new();
    public CaseService Service { get; }

    public OperationActor Actor
    {
        get => CurrentUser.Actor;
        set => CurrentUser.Actor = value;
    }

    public async Task<CaseDetailDto> CreateAsync(string title = "Synthetic intake case", string? description = "Synthetic description.",
        VeteranSnapshot? veteran = null)
    {
        var previous = Actor;
        Actor = TestActors.Supervisor;
        try
        {
            return Ok(await Service.CreateAsync(new CreateCaseRequest(title, description, veteran ?? Builders.Veteran()), CancellationToken.None));
        }
        finally
        {
            Actor = previous;
        }
    }

    /// <summary>Moves a case along the workflow as the Supervisor, returning the final detail.</summary>
    public async Task<CaseDetailDto> MoveAsync(CaseDetailDto detail, params CaseStatus[] path)
    {
        var previous = Actor;
        Actor = TestActors.Supervisor;
        try
        {
            foreach (var target in path)
            {
                Time.Advance(TimeSpan.FromMinutes(1));
                detail = Ok(await Service.ChangeStatusAsync(detail.Id, new ChangeStatusRequest(target, detail.Version), CancellationToken.None));
            }
            return detail;
        }
        finally
        {
            Actor = previous;
        }
    }

    public async Task<int> CountAuditAsync(long caseId)
    {
        await using var db = Database.CreateContext();
        return await db.CaseAuditEntries.CountAsync(a => a.CaseId == caseId);
    }

    public async Task<Case> LoadAsync(long caseId)
    {
        await using var db = Database.CreateContext();
        return await db.Cases.AsNoTracking().SingleAsync(c => c.Id == caseId);
    }

    public static T Ok<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error is null ? "" : $"{result.Error.Kind} {result.Error.Code}: {result.Error.Message}");
        return result.Value!;
    }

    public static Error Fail<T>(Result<T> result, ErrorKind kind)
    {
        Assert.False(result.IsSuccess, "Expected a failure result.");
        Assert.Null(result.Value);
        Assert.Equal(kind, result.Error!.Kind);
        return result.Error;
    }

    private sealed class InterceptingFactory(string connectionString, IInterceptor[] interceptors) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptors)
            .Options);
    }
}
