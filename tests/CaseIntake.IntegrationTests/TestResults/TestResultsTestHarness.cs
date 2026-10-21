using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Persistence;
using CaseIntake.TestKit;
using CaseIntake.TestKit.Fakes;
using CaseIntake.TestResults;
using CaseIntake.TestResults.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>Actors for the test results module (plan E5): the quality analyst role holds view and manage.</summary>
internal static class TestResultsActors
{
    public static OperationActor QualityAnalyst { get; } = FakeCurrentUser.ForRole(AppRoles.QualityAnalyst, "qa-id");

    public static OperationActor ViewOnly { get; } =
        FakeCurrentUser.Authenticated("viewer-id", "viewer@example.test", "Viewer", [Permissions.TestResultsView]);

    public static OperationActor NoPermissions { get; } =
        FakeCurrentUser.Authenticated("nobody-id", "nobody@example.test", "Nobody", []);
}

/// <summary>
/// A real <see cref="TestResultImporter"/>, <see cref="TestResultsService"/> and <see cref="TestArtifactRetention"/> over a
/// test database and a private temporary results root, with a settable actor, fake clock, captured logs and optional
/// EF interceptors for fault injection. Run folders are laid out like <c>scripts/run-tests.sh</c> writes them.
/// </summary>
internal sealed class TestResultsTestHarness : IDisposable
{
    public static readonly DateTimeOffset StartTime = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    /// <summary>The deployment's demo password (a known literal secret the importer redacts).</summary>
    public const string DemoPassword = "Synthetic-Demo-Passw0rd";

    public TestResultsTestHarness(PostgresDatabaseFixture database, Action<TestResultsOptions>? configure = null, params IInterceptor[] interceptors)
    {
        Database = database;
        Root = Path.Combine(Path.GetTempPath(), "caseintake-testresults-" + Guid.NewGuid().ToString("N"));
        var settings = new TestResultsOptions { RootPath = Root };
        configure?.Invoke(settings);
        new TestResultsOptionsSetup().PostConfigure(null, settings);
        Options = Microsoft.Extensions.Options.Options.Create(settings);
        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:DemoPassword"] = DemoPassword })
            .Build();
        Factory = new InterceptingFactory(database.ConnectionString, interceptors);
        Importer = new TestResultImporter(Factory, Options, Configuration, Time, ImporterLogs);
        Service = new TestResultsService(Factory, CurrentUser, Importer, Options, Time, ServiceLogs);
        Retention = new TestArtifactRetention(Factory, Options, Time, RetentionLogs);
    }

    public PostgresDatabaseFixture Database { get; }
    public string Root { get; }
    public string RunsPath => Options.Value.RunsPath;
    public IOptions<TestResultsOptions> Options { get; }
    public IConfiguration Configuration { get; }
    public IDbContextFactory<AppDbContext> Factory { get; }
    public FakeTimeProvider Time { get; } = new(StartTime);
    public FakeCurrentUser CurrentUser { get; } = new() { Actor = TestResultsActors.QualityAnalyst };
    public FakeLogger<TestResultImporter> ImporterLogs { get; } = new();
    public FakeLogger<TestResultsService> ServiceLogs { get; } = new();
    public FakeLogger<TestArtifactRetention> RetentionLogs { get; } = new();
    public TestResultImporter Importer { get; }
    public TestResultsService Service { get; }
    public TestArtifactRetention Retention { get; }

    public OperationActor Actor
    {
        get => CurrentUser.Actor;
        set => CurrentUser.Actor = value;
    }

    /// <summary>Starts a run folder under <c>runs/</c>; nothing is importable until <see cref="RunFolder.Complete"/> writes run.json.</summary>
    public RunFolder NewRun(string runKey, DateTimeOffset? startedAtUtc = null) =>
        new(Path.Combine(RunsPath, runKey), runKey, startedAtUtc ?? StartTime);

    public Task<ImportSummaryDto> ImportAsync() =>
        Importer.ImportPendingAsync("qa@example.test", "qa-id", CancellationToken.None);

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var db = Database.CreateContext();
        return await query(db);
    }

    public static T Ok<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error is null ? "" : $"{result.Error.Kind} {result.Error.Code}: {result.Error.Message}");
        return result.Value!;
    }

    public static Error Fail<T>(Result<T> result, ErrorKind kind)
    {
        Assert.False(result.IsSuccess, "Expected a failure result.");
        Assert.Equal(kind, result.Error!.Kind);
        return result.Error;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class InterceptingFactory(string connectionString, IInterceptor[] interceptors) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptors)
            .Options);
    }
}

/// <summary>One <c>runs/&lt;runKey&gt;</c> folder: TRX files per suite, catalog, evidence, server logs and run.json (written last).</summary>
internal sealed class RunFolder
{
    public RunFolder(string path, string runKey, DateTimeOffset startedAtUtc)
    {
        Path = path;
        Directory.CreateDirectory(path);
        Manifest = new RunManifest
        {
            RunKey = runKey,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = startedAtUtc.AddMinutes(3),
            Runner = "scripts/run-tests.sh (all) via docker compose tests service",
            Git = new RunGit { Branch = "main", Commit = "abc1234def", BuildId = "build-42" },
            ImageVersion = "caseintake-eval:local (build-42, abc1234def)",
            Environment = new RunEnvironment { Name = "docker compose tests", Os = "Debian GNU/Linux 13", Runtime = "Microsoft.AspNetCore.App 10.0.0", BaseUrl = "http://web:8080" },
        };
    }

    public string Path { get; }
    public RunManifest Manifest { get; }

    /// <summary>Adds a suite whose TRX (when given) is written as <c>&lt;name&gt;.trx</c>.</summary>
    public RunFolder Suite(string name, string type, string? trx, string status = "Completed", string? message = null)
    {
        string? trxName = null;
        if (trx is not null)
        {
            trxName = name + ".trx";
            File.WriteAllText(System.IO.Path.Combine(Path, trxName), trx);
        }
        Manifest.Suites.Add(new RunSuite { Name = name, Type = type, Trx = trxName, ExitCode = trx is null ? null : 0, Status = status, Message = message });
        return this;
    }

    /// <summary>Adds a suite entry that references a TRX file by name without writing it.</summary>
    public RunFolder SuiteReferencing(string name, string type, string trxName, string status = "Completed")
    {
        Manifest.Suites.Add(new RunSuite { Name = name, Type = type, Trx = trxName, ExitCode = 0, Status = status });
        return this;
    }

    public RunFolder Catalog(string assembly, params (string Method, string[] Categories)[] entries)
    {
        var folder = System.IO.Path.Combine(Path, "catalog");
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, assembly + ".json"),
            JsonSerializer.Serialize(entries.Select(e => new { method = e.Method, categories = e.Categories })));
        return this;
    }

    /// <summary>Writes <c>evidence/&lt;slug&gt;/evidence.json</c> and the given files next to it.</summary>
    public RunFolder Evidence(string slug, EvidenceManifest manifest, params (string File, byte[] Content)[] files)
    {
        var folder = System.IO.Path.Combine(Path, "evidence", slug);
        Directory.CreateDirectory(folder);
        foreach (var (file, content) in files)
        {
            File.WriteAllBytes(System.IO.Path.Combine(folder, file), content);
        }
        File.WriteAllText(System.IO.Path.Combine(folder, "evidence.json"), JsonSerializer.Serialize(manifest, RunManifest.Json));
        return this;
    }

    public RunFolder ServerLog(string name, string content)
    {
        File.WriteAllText(System.IO.Path.Combine(Path, name), content);
        Manifest.ServerLogs.Add(name);
        return this;
    }

    /// <summary>Writes run.json, which makes the folder importable.</summary>
    public RunFolder Complete()
    {
        File.WriteAllText(System.IO.Path.Combine(Path, "run.json"), JsonSerializer.Serialize(Manifest, RunManifest.Json));
        return this;
    }

    public string PathOf(params string[] segments) => System.IO.Path.Combine([Path, .. segments]);
}

/// <summary>One test in a generated TRX document.</summary>
internal sealed record TrxTest(
    string ClassName,
    string Method,
    string Outcome = "Passed",
    string? Message = null,
    string? StackTrace = null,
    string? StdOut = null,
    string Duration = "00:00:00.1250000",
    string[]? Categories = null,
    string? Arguments = null)
{
    public string FullName => $"{ClassName}.{Method}{Arguments}";
}

/// <summary>Builds VSTest TRX documents like <c>dotnet test --logger trx</c> writes them.</summary>
internal static class Trx
{
    public static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static string Document(params TrxTest[] tests) => Document(TestResultsTestHarness.StartTime, tests);

    public static string Document(DateTimeOffset startedAtUtc, params TrxTest[] tests)
    {
        var results = new XElement(Ns + "Results");
        var definitions = new XElement(Ns + "TestDefinitions");
        for (var i = 0; i < tests.Length; i++)
        {
            var t = tests[i];
            var id = $"00000000-0000-0000-0000-{i + 1:D12}";
            var start = startedAtUtc.AddSeconds(i);
            var result = new XElement(Ns + "UnitTestResult",
                new XAttribute("executionId", Guid.NewGuid()),
                new XAttribute("testId", id),
                new XAttribute("testName", t.FullName),
                new XAttribute("computerName", "tests"),
                new XAttribute("duration", t.Duration),
                new XAttribute("startTime", start.ToString("o", CultureInfo.InvariantCulture)),
                new XAttribute("endTime", start.AddMilliseconds(125).ToString("o", CultureInfo.InvariantCulture)),
                new XAttribute("outcome", t.Outcome));
            if (t.Message is not null || t.StackTrace is not null || t.StdOut is not null)
            {
                var output = new XElement(Ns + "Output");
                if (t.StdOut is not null)
                {
                    output.Add(new XElement(Ns + "StdOut", t.StdOut));
                }
                if (t.Message is not null || t.StackTrace is not null)
                {
                    output.Add(new XElement(Ns + "ErrorInfo",
                        t.Message is null ? null : new XElement(Ns + "Message", t.Message),
                        t.StackTrace is null ? null : new XElement(Ns + "StackTrace", t.StackTrace)));
                }
                result.Add(output);
            }
            results.Add(result);
            definitions.Add(new XElement(Ns + "UnitTest",
                new XAttribute("name", t.FullName),
                new XAttribute("storage", "/src/tests/bin/Tests.dll"),
                new XAttribute("id", id),
                t.Categories is { Length: > 0 } categories
                    ? new XElement(Ns + "TestCategory", categories.Select(c => new XElement(Ns + "TestCategoryItem", new XAttribute("TestCategory", c))))
                    : null,
                new XElement(Ns + "Execution", new XAttribute("id", Guid.NewGuid())),
                new XElement(Ns + "TestMethod",
                    new XAttribute("codeBase", "/src/tests/bin/Tests.dll"),
                    new XAttribute("adapterTypeName", "executor://xunit/VsTestRunner2/netcoreapp"),
                    new XAttribute("className", t.ClassName),
                    new XAttribute("name", t.Method))));
        }
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "TestRun", new XAttribute("id", Guid.NewGuid()), new XAttribute("name", "tests"), results, definitions));
        return doc.Declaration + Environment.NewLine + doc.ToString();
    }
}
