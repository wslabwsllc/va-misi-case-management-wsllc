using CaseIntake.Cases;
using CaseIntake.Cases.Seeding;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;
using CaseIntake.Persistence.Seeding;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Cases;

/// <summary>E2-C2-3: the service-history outcome captured at intake is saved with the case and returned with its detail.</summary>
public class ServiceHistoryIntakeTests : ClassFixture<ServiceHistoryIntakeTests, PostgresDatabaseFixture>
{
    private static PostgresDatabaseFixture database => Shared;

    private static readonly ServiceHistory Army =
        new("Army", new DateOnly(2001, 2, 15), new DateOnly(2005, 2, 14), "Honorable", new DateOnly(2026, 1, 5));

    public static IEnumerable<TestCaseData> Outcomes()
    {
        yield return new TestCaseData(ServiceHistoryStatus.Found, Army).SetArgDisplayNames("Found");
        yield return new TestCaseData(ServiceHistoryStatus.Found, Army with { ServiceEndDate = null, LastVerifiedOn = null }).SetArgDisplayNames("Found, open");
        yield return new TestCaseData(ServiceHistoryStatus.NotFound, null).SetArgDisplayNames("NotFound");
        yield return new TestCaseData(ServiceHistoryStatus.Unavailable, null).SetArgDisplayNames("Unavailable");
    }

    [TestCaseSource(nameof(Outcomes))]
    public async Task Each_outcome_is_saved_and_read_back(ServiceHistoryStatus status, ServiceHistory? history)
    {
        var harness = new CaseTestHarness(database);
        var veteran = Builders.Veteran() with { HistoryStatus = status, History = history };

        var created = await harness.CreateAsync(veteran: veteran);

        Assert.Equal(veteran, created.Veteran);
        Assert.Equal(veteran, CaseTestHarness.Ok(await harness.Service.GetAsync(created.Id, CancellationToken.None)).Veteran);
    }

    [Test]
    public async Task A_request_without_a_status_is_read_back_with_none()
    {
        var harness = new CaseTestHarness(database);

        var created = await harness.CreateAsync(veteran: Builders.Veteran());

        var read = CaseTestHarness.Ok(await harness.Service.GetAsync(created.Id, CancellationToken.None)).Veteran!;
        Assert.Null(read.HistoryStatus);
        Assert.Null(read.History);
    }

    [TestCase(ServiceHistoryStatus.Found, "Veteran snapshot recorded; service history: Found")]
    [TestCase(ServiceHistoryStatus.NotFound, "Veteran snapshot recorded; service history: NotFound")]
    [TestCase(ServiceHistoryStatus.Unavailable, "Veteran snapshot recorded; service history: Unavailable")]
    [TestCase(null, "Veteran snapshot recorded")]
    public async Task Created_audit_entry_names_the_status_but_no_history_values(ServiceHistoryStatus? status, string details)
    {
        var harness = new CaseTestHarness(database);
        var veteran = Builders.Veteran() with { HistoryStatus = status, History = status == ServiceHistoryStatus.Found ? Army : null };

        var created = await harness.CreateAsync(veteran: veteran);

        var audit = Assert.Single(CaseTestHarness.Ok(await harness.Service.GetAuditTrailAsync(created.Id, CancellationToken.None)));
        Assert.Equal(AuditActions.Created, audit.Action);
        Assert.Equal(details, audit.Details);
    }

    [Test]
    public async Task An_invalid_snapshot_returns_field_errors_and_saves_nothing()
    {
        var harness = new CaseTestHarness(database);
        var before = await CountCasesAsync();
        var invalid = Builders.Veteran() with
        {
            HistoryStatus = ServiceHistoryStatus.Found,
            History = Army with { Branch = new string('b', 51), ServiceEndDate = Army.ServiceStartDate.AddDays(-1) },
        };

        var error = CaseTestHarness.Fail(await harness.Service.CreateAsync(
            new CreateCaseRequest("Valid title", null, invalid), CancellationToken.None), ErrorKind.Validation);

        Assert.Equal([CaseFieldNames.ServiceBranch, CaseFieldNames.ServiceEndDate], error.FieldErrors!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(before, await CountCasesAsync());
    }

    [Test]
    public async Task A_history_without_Found_is_rejected_and_saves_nothing()
    {
        var harness = new CaseTestHarness(database);
        var before = await CountCasesAsync();

        var error = CaseTestHarness.Fail(await harness.Service.CreateAsync(new CreateCaseRequest("Valid title", null,
            Builders.Veteran() with { HistoryStatus = ServiceHistoryStatus.Unavailable, History = Army }), CancellationToken.None), ErrorKind.Validation);

        Assert.Equal([CaseTextRules.ServiceHistoryNotAllowedMessage], error.FieldErrors![CaseFieldNames.Veteran]);
        Assert.Equal(before, await CountCasesAsync());
    }

    [Test]
    public async Task Seeded_cases_have_no_service_history_status()
    {
        var seeded = new PostgresDatabaseFixture();
        await seeded.InitializeAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(seeded.CreateContextFactory());
            services.AddCasesSeed();
            await using (var provider = services.BuildServiceProvider())
            {
                await new SeedRunner(provider, new FakeLogger<SeedRunner>()).RunAsync(CancellationToken.None);
            }
            await using var db = seeded.CreateContext();
            var id = await db.Cases.Where(c => c.SeedKey != null).Select(c => c.Id).FirstAsync();

            var detail = CaseTestHarness.Ok(await new CaseTestHarness(seeded).Service.GetAsync(id, CancellationToken.None));

            Assert.Null(detail.Veteran?.HistoryStatus);
            Assert.Equal(0, await db.Cases.CountAsync(c => c.ServiceHistoryStatus != null));
        }
        finally
        {
            await seeded.DisposeAsync();
        }
    }

    private static async Task<int> CountCasesAsync()
    {
        await using var db = database.CreateContext();
        return await db.Cases.CountAsync();
    }
}
