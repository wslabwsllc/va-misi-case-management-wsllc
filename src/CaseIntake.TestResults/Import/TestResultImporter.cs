using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaseIntake.Contracts.TestResults;
using CaseIntake.Domain.TestResults;
using CaseIntake.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.TestResults.Import;

/// <summary>
/// Imports finished run folders (<c>runs/&lt;runKey&gt;/run.json</c> plus TRX files and E2E evidence) into the results
/// tables. A run is imported once (unique run key) and never changed afterwards, so history accumulates. Each run is
/// one SaveChanges (one transaction). All stored text is redacted; evidence files are referenced in place, after
/// checking that each stays inside its run folder, and are hashed. Tests of a suite that did not start or produced no
/// results are recorded as NotExecuted, so a run that failed to execute is never mistaken for a passing one.
/// </summary>
internal sealed partial class TestResultImporter(
    IDbContextFactory<AppDbContext> contextFactory,
    IOptions<TestResultsOptions> options,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<TestResultImporter> logger)
{
    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxTrxBytes = 256L * 1024 * 1024;
    private const long MaxServerLogBytes = 20L * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private TestResultsOptions Settings => options.Value;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();

    public async Task<ImportSummaryDto> ImportPendingAsync(string importedBy, string? actorId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(Settings.RunsPath))
            {
                return new ImportSummaryDto(0, 0, 0, 0, ["No test results have been written yet. Run the documented test command first."]);
            }

            int imported = 0, skipped = 0, executions = 0, artifacts = 0;
            List<string> problems = [];
            await using var db = await contextFactory.CreateDbContextAsync(ct);
            var known = (await db.TestRuns.Select(r => r.RunKey).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

            foreach (var folder in Directory.EnumerateDirectories(Settings.RunsPath).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var folderName = Path.GetFileName(folder);
                if (known.Contains(folderName))
                {
                    skipped++;
                    continue;
                }
                var manifestPath = Path.Combine(folder, "run.json");
                if (!File.Exists(manifestPath))
                {
                    continue;   // still being written, or not a run folder
                }

                try
                {
                    var outcome = await ImportRunAsync(folder, folderName, manifestPath, importedBy, actorId, ct);
                    if (outcome is null)
                    {
                        skipped++;
                        continue;
                    }
                    imported++;
                    executions += outcome.Value.Executions;
                    artifacts += outcome.Value.Artifacts;
                    problems.AddRange(outcome.Value.Problems.Select(p => $"{folderName}: {p}"));
                    known.Add(folderName);
                }
                catch (Exception ex) when (ex is IOException or JsonException or System.Xml.XmlException or InvalidDataException or UnauthorizedAccessException)
                {
                    TestResultsLog.RunImportFailed(logger, folderName, ex.GetType().Name);
                    problems.Add($"{folderName}: could not be imported ({ex.GetType().Name}).");
                }
                catch (DbUpdateException ex)
                {
                    // Another instance imported the same run first (unique run key), or the data broke a constraint.
                    TestResultsLog.RunImportFailed(logger, folderName, ex.GetType().Name);
                    problems.Add($"{folderName}: not saved ({ex.GetType().Name}).");
                }
            }

            return new ImportSummaryDto(imported, skipped, executions, artifacts, problems);
        }
        finally
        {
            Gate.Release();
        }
    }

    private readonly record struct RunImport(int Executions, int Artifacts, List<string> Problems);

    private async Task<RunImport?> ImportRunAsync(string folder, string folderName, string manifestPath, string importedBy, string? actorId, CancellationToken ct)
    {
        if (!SafeName().IsMatch(folderName))
        {
            throw new InvalidDataException("Run folder name is not a safe run key.");
        }
        var manifest = await ReadJsonAsync<RunManifest>(manifestPath, ct)
            ?? throw new InvalidDataException("run.json is empty.");
        if (!string.Equals(manifest.RunKey, folderName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("run.json runKey does not match its folder.");
        }

        List<string> problems = [];
        var secrets = KnownSecrets();
        var now = time.GetUtcNow();
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        if (await db.TestRuns.AnyAsync(r => r.RunKey == manifest.RunKey, ct))
        {
            return null;
        }

        var environment = await EnvironmentAsync(db, manifest.Environment ?? new RunEnvironment(), ct);
        var run = new TestRun
        {
            RunKey = manifest.RunKey,
            StartedAtUtc = manifest.StartedAtUtc.ToUniversalTime(),
            FinishedAtUtc = manifest.FinishedAtUtc?.ToUniversalTime(),
            Runner = Cap(manifest.Runner, TestResultFieldLimits.ShortTextMaxLength) ?? "unknown",
            GitBranch = Cap(manifest.Git?.Branch, TestResultFieldLimits.ShortTextMaxLength),
            GitCommit = Cap(manifest.Git?.Commit, 64),
            BuildId = Cap(manifest.Git?.BuildId, TestResultFieldLimits.ShortTextMaxLength),
            ImageVersion = Cap(manifest.ImageVersion, TestResultFieldLimits.ShortTextMaxLength),
            Environment = environment,
            Notes = EvidenceRedactor.RedactAndCap(manifest.Notes, TestResultFieldLimits.MessageMaxLength, secrets),
            SourcePath = Path.Combine("runs", folderName),
            ImportedAtUtc = now,
            ImportedBy = Cap(importedBy, TestResultFieldLimits.ShortTextMaxLength)!,
        };
        run.DurationMs = run.FinishedAtUtc is { } finished ? (long)Math.Max(0, (finished - run.StartedAtUtc).TotalMilliseconds) : 0;
        db.TestRuns.Add(run);

        var evidence = await ReadEvidenceAsync(folder, problems, ct);
        var catalog = await ReadCatalogAsync(folder, problems, ct);
        var suiteSummaries = new List<object>();
        var cases = new Dictionary<string, TestCase>(StringComparer.Ordinal);
        // One execution per test per run (unique index), even if two suites report the same test name.
        var seenInRun = new HashSet<string>(StringComparer.Ordinal);
        var artifactCount = 0;

        foreach (var suite in manifest.Suites)
        {
            var type = ParseType(suite.Type);
            var module = ModuleName(suite.Name);
            var trxPath = suite.Trx is { Length: > 0 } trxName ? SafeChild(folder, trxName) : null;
            List<TrxResult> results = [];
            var suiteStatus = suite.Status;
            if (trxPath is not null && File.Exists(trxPath))
            {
                if (new FileInfo(trxPath).Length > MaxTrxBytes)
                {
                    problems.Add($"{suite.Name}: result file too large to import.");
                    suiteStatus = "NoResults";
                }
                else
                {
                    await using var stream = File.OpenRead(trxPath);
                    results = [.. TrxReader.Read(stream)];
                    artifactCount += AddArtifact(db, run, null, TestArtifactKind.ResultFile, trxPath, $"{suite.Name} results (TRX)", problems) ? 1 : 0;
                }
            }
            else if (suiteStatus == "Completed")
            {
                suiteStatus = "NoResults";
            }

            if (results.Count == 0 && suiteStatus != "Completed")
            {
                // The suite did not run: every test previously seen in this module is NotExecuted in this run.
                var expected = await db.TestCases.Where(c => c.Module == module).ToListAsync(ct);
                foreach (var testCase in expected.Where(c => seenInRun.Add(c.Key)))
                {
                    cases.TryAdd(testCase.Key, testCase);
                    run.Executions.Add(new TestExecution { TestCase = testCase, TestCaseId = testCase.Id, Outcome = TestOutcome.NotExecuted,
                        ErrorMessage = EvidenceRedactor.RedactAndCap($"Not executed: suite {suite.Name} {(suiteStatus == "FailedToStart" ? "failed to start" : "produced no results")}. {suite.Message}", TestResultFieldLimits.MessageMaxLength, secrets) });
                }
            }

            var keys = results.Select(r => Key(r.FullName)).Distinct().ToList();
            var existing = await db.TestCases.Where(c => keys.Contains(c.Key)).ToDictionaryAsync(c => c.Key, ct);
            foreach (var trxResult in results)
            {
                // TRX carries no xUnit traits; the assembly's catalog (TestKit.TestCatalog) supplies Story/Req/Priority.
                var result = catalog.TryGetValue($"{trxResult.ClassName}.{trxResult.MethodName}", out var extra)
                    ? trxResult with { Categories = [.. trxResult.Categories.Concat(extra).Distinct(StringComparer.Ordinal)] }
                    : trxResult;
                var key = Key(result.FullName);
                if (!seenInRun.Add(key))
                {
                    problems.Add($"{suite.Name}: a second result for the same test in this run was ignored.");
                    continue;
                }
                evidence.TryGetValue(result.FullName, out var ev);
                if (!existing.TryGetValue(key, out var testCase) && !cases.TryGetValue(key, out testCase))
                {
                    testCase = new TestCase { Key = key, FullName = Cap(result.FullName, TestResultFieldLimits.FullNameMaxLength)!, FirstSeenAtUtc = run.StartedAtUtc };
                    db.TestCases.Add(testCase);
                }
                cases[key] = testCase;
                Describe(testCase, result, ev, module, type);

                var execution = new TestExecution
                {
                    TestCase = testCase,
                    Outcome = result.Outcome,
                    StartedAtUtc = result.StartedAtUtc,
                    FinishedAtUtc = result.FinishedAtUtc,
                    DurationMs = (long)result.Duration.TotalMilliseconds,
                    ErrorMessage = EvidenceRedactor.RedactAndCap(result.ErrorMessage, TestResultFieldLimits.MessageMaxLength, secrets),
                    StackTrace = EvidenceRedactor.RedactAndCap(result.StackTrace, TestResultFieldLimits.LongTextMaxLength, secrets),
                    StandardOutput = EvidenceRedactor.RedactAndCap(result.StandardOutput, TestResultFieldLimits.LongTextMaxLength, secrets),
                };
                if (ev is not null)
                {
                    execution.Browser = Cap(ev.Browser, 64);
                    execution.Viewport = Cap(ev.Viewport, 64);
                    execution.ActualResult = EvidenceRedactor.RedactAndCap(ev.Actual, TestResultFieldLimits.MessageMaxLength, secrets);
                    execution.ConsoleLog = EvidenceRedactor.RedactAndCap(string.Join('\n', ev.ConsoleErrors), TestResultFieldLimits.LongTextMaxLength, secrets);
                    execution.NetworkLog = EvidenceRedactor.RedactAndCap(string.Join('\n', ev.NetworkFailures), TestResultFieldLimits.LongTextMaxLength, secrets);
                    var order = 0;
                    foreach (var step in ev.Steps)
                    {
                        execution.Steps.Add(new TestStepResult
                        {
                            Order = ++order,
                            Name = Cap(EvidenceRedactor.Redact(step.Name, secrets), TestResultFieldLimits.NameMaxLength) ?? "Step",
                            Outcome = ParseOutcome(step.Outcome),
                            DurationMs = Math.Max(0, step.DurationMs),
                            Expected = EvidenceRedactor.RedactAndCap(step.Expected, TestResultFieldLimits.MessageMaxLength, secrets),
                            Actual = EvidenceRedactor.RedactAndCap(step.Actual, TestResultFieldLimits.MessageMaxLength, secrets),
                            Message = EvidenceRedactor.RedactAndCap(step.Message, TestResultFieldLimits.MessageMaxLength, secrets),
                        });
                    }
                    foreach (var file in ev.Artifacts)
                    {
                        var path = SafeChild(Path.Combine(folder, ev.FolderRelativePath), file.File);
                        if (path is null || !File.Exists(path))
                        {
                            problems.Add($"{suite.Name}: evidence file missing or outside its folder.");
                            continue;
                        }
                        artifactCount += AddArtifact(db, run, execution, ParseKind(file.Kind), path, Cap(file.Caption, TestResultFieldLimits.NameMaxLength), problems) ? 1 : 0;
                    }
                }
                run.Executions.Add(execution);
            }

            suiteSummaries.Add(new { name = suite.Name, type = type.ToString(), status = suiteStatus, tests = results.Count, message = EvidenceRedactor.RedactAndCap(suite.Message, 500, secrets) });
        }

        artifactCount += await AddServerLogsAsync(db, run, folder, manifest.ServerLogs, secrets, problems, ct);

        run.SuitesJson = JsonSerializer.Serialize(suiteSummaries);
        run.Total = run.Executions.Count;
        run.Passed = run.Executions.Count(e => e.Outcome == TestOutcome.Passed);
        run.Failed = run.Executions.Count(e => e.Outcome == TestOutcome.Failed);
        run.Skipped = run.Executions.Count(e => e.Outcome == TestOutcome.Skipped);
        run.Blocked = run.Executions.Count(e => e.Outcome == TestOutcome.Blocked);
        run.NotExecuted = run.Executions.Count(e => e.Outcome == TestOutcome.NotExecuted);
        run.Status = DeriveStatus(manifest, suiteSummaries.Count, run);

        db.TestResultAuditEntries.Add(new TestResultAuditEntry
        {
            OccurredAtUtc = now, ActorId = actorId, Actor = run.ImportedBy, Action = "RunImported",
            CorrelationId = Contracts.Common.OperationContext.CurrentId,
            Details = string.Create(CultureInfo.InvariantCulture, $"Run {run.RunKey}: {run.Total} results, {artifactCount} evidence files"),
        });
        await db.SaveChangesAsync(ct);

        await UpdateLastOutcomesAsync(db, run, cases.Values, ct);
        TestResultsLog.RunImported(logger, run.RunKey, run.Total, run.Failed, run.NotExecuted, artifactCount);
        return new RunImport(run.Executions.Count, artifactCount, problems);
    }

    private static TestRunStatus DeriveStatus(RunManifest manifest, int suites, TestRun run)
    {
        if (string.Equals(manifest.Status, nameof(TestRunStatus.FailedToStart), StringComparison.OrdinalIgnoreCase) || (suites > 0 && run.Total == run.NotExecuted && manifest.Suites.All(s => s.Status != "Completed")))
        {
            return TestRunStatus.FailedToStart;
        }
        return manifest.Suites.Any(s => s.Status != "Completed" || s.Trx is null) || run.NotExecuted > 0 || string.Equals(manifest.Status, nameof(TestRunStatus.Partial), StringComparison.OrdinalIgnoreCase)
            ? TestRunStatus.Partial
            : TestRunStatus.Completed;
    }

    /// <summary>A test's "last outcome" follows the newest run by start time, even when older runs are imported later.</summary>
    private static async Task UpdateLastOutcomesAsync(AppDbContext db, TestRun run, IEnumerable<TestCase> cases, CancellationToken ct)
    {
        var list = cases.ToList();
        var lastRunIds = list.Where(c => c.LastRunId is not null).Select(c => c.LastRunId!.Value).Distinct().ToList();
        var starts = await db.TestRuns.Where(r => lastRunIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.StartedAtUtc, ct);
        var outcomes = run.Executions.GroupBy(e => e.TestCase.Key).ToDictionary(g => g.Key, g => g.First().Outcome);
        foreach (var testCase in list)
        {
            if (!outcomes.TryGetValue(testCase.Key, out var outcome))
            {
                continue;
            }
            if (testCase.LastRunId is null || !starts.TryGetValue(testCase.LastRunId.Value, out var lastStart) || lastStart <= run.StartedAtUtc)
            {
                testCase.LastRunId = run.Id;
                testCase.LastOutcome = outcome;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static void Describe(TestCase testCase, TrxResult result, EvidenceManifest? ev, string module, TestType type)
    {
        testCase.Module = module;
        testCase.Type = type;
        testCase.Title = Cap(ev?.Title, TestResultFieldLimits.NameMaxLength) ?? Humanize(result);
        string? Category(string prefix) => result.Categories.FirstOrDefault(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..].Trim();
        testCase.Story = Cap(ev?.Story ?? Category("Story:"), TestResultFieldLimits.ShortTextMaxLength) ?? testCase.Story;
        var requirements = ev?.Requirements is { Count: > 0 } r
            ? string.Join(", ", r)
            : string.Join(", ", result.Categories.Where(c => c.StartsWith("Req:", StringComparison.OrdinalIgnoreCase)).Select(c => c[4..].Trim()));
        testCase.Requirements = Cap(requirements.Length > 0 ? requirements : null, TestResultFieldLimits.NameMaxLength) ?? testCase.Requirements;
        testCase.Priority = Cap(ev?.Priority ?? Category("Priority:"), 32) ?? testCase.Priority;
        testCase.Description = Cap(ev?.Description, TestResultFieldLimits.MessageMaxLength) ?? testCase.Description ?? $"{result.ClassName}.{result.MethodName}";
        testCase.AcceptanceCriteria = Cap(ev?.AcceptanceCriteria, TestResultFieldLimits.MessageMaxLength) ?? testCase.AcceptanceCriteria;
        testCase.Preconditions = Cap(ev?.Preconditions, TestResultFieldLimits.MessageMaxLength) ?? testCase.Preconditions;
        testCase.ExpectedResult = Cap(ev?.Expected, TestResultFieldLimits.MessageMaxLength) ?? testCase.ExpectedResult;
    }

    /// <summary>"Ns.Class.Some_behaviour_works(x: 1)" becomes "Some behaviour works (x: 1)".</summary>
    internal static string Humanize(TrxResult result)
    {
        var name = result.FullName;
        var argsAt = name.IndexOf('(', StringComparison.Ordinal);
        var args = argsAt >= 0 ? " " + name[argsAt..] : string.Empty;
        var head = argsAt >= 0 ? name[..argsAt] : name;
        var method = head.Contains('.', StringComparison.Ordinal) ? head[(head.LastIndexOf('.') + 1)..] : head;
        var text = method.Replace('_', ' ').Trim();
        var title = text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] + args : name;
        return title.Length <= TestResultFieldLimits.NameMaxLength ? title : title[..TestResultFieldLimits.NameMaxLength];
    }

    private async Task<int> AddServerLogsAsync(AppDbContext db, TestRun run, string folder, List<string> logs, IReadOnlyCollection<string> secrets, List<string> problems, CancellationToken ct)
    {
        var added = 0;
        foreach (var name in logs)
        {
            var path = SafeChild(folder, name);
            if (path is null || !File.Exists(path))
            {
                problems.Add("A listed server log is missing.");
                continue;
            }
            if (new FileInfo(path).Length > MaxServerLogBytes)
            {
                problems.Add("A server log is too large to import.");
                continue;
            }
            try
            {
                // Only a redacted copy is kept and served; the raw file is removed.
                var redactedPath = Path.ChangeExtension(path, ".redacted.log");
                var text = await File.ReadAllTextAsync(path, ct);
                await File.WriteAllTextAsync(redactedPath, EvidenceRedactor.Redact(text, secrets), ct);
                File.Delete(path);
                added += AddArtifact(db, run, null, TestArtifactKind.ServerLog, redactedPath, "Server log (redacted)", problems) ? 1 : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"A server log could not be redacted ({ex.GetType().Name}); it is not shown.");
            }
        }
        return added;
    }

    private bool AddArtifact(AppDbContext db, TestRun run, TestExecution? execution, TestArtifactKind kind, string fullPath, string? caption, List<string> problems)
    {
        var info = new FileInfo(fullPath);
        if (info.Length > Settings.MaxArtifactBytes)
        {
            problems.Add($"Evidence file {info.Name} is larger than the import limit and was skipped.");
            return false;
        }
        string sha;
        using (var stream = info.OpenRead())
        {
            sha = Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        var artifact = new TestArtifact
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            Kind = kind,
            FileName = Cap(info.Name, TestResultFieldLimits.NameMaxLength)!,
            ContentType = ContentTypeFor(info.Extension),
            SizeBytes = info.Length,
            Sha256 = sha,
            RelativePath = Path.GetRelativePath(Settings.RootPath, info.FullName).Replace('\\', '/'),
            Caption = caption,
            CreatedAtUtc = time.GetUtcNow(),
        };
        artifact.Run = run;
        if (execution is null)
        {
            db.TestArtifacts.Add(artifact);   // run-level evidence (result files, server logs)
        }
        else
        {
            execution.Artifacts.Add(artifact);
        }
        return true;
    }

    private async Task<Dictionary<string, EvidenceManifest>> ReadEvidenceAsync(string folder, List<string> problems, CancellationToken ct)
    {
        var result = new Dictionary<string, EvidenceManifest>(StringComparer.Ordinal);
        var root = Path.Combine(folder, "evidence");
        if (!Directory.Exists(root))
        {
            return result;
        }
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var file = Path.Combine(dir, "evidence.json");
            if (!File.Exists(file))
            {
                continue;
            }
            try
            {
                var manifest = await ReadJsonAsync<EvidenceManifest>(file, ct);
                if (manifest is { FullName.Length: > 0 })
                {
                    manifest.FolderRelativePath = Path.GetRelativePath(folder, dir);
                    result[manifest.FullName] = manifest;
                }
            }
            catch (JsonException)
            {
                problems.Add("An evidence manifest could not be read.");
            }
        }
        return result;
    }

    private sealed record CatalogEntry(string Method, string[] Categories);

    /// <summary><c>catalog/*.json</c>: test method to xUnit Category traits, written by each test assembly.</summary>
    private static async Task<Dictionary<string, string[]>> ReadCatalogAsync(string folder, List<string> problems, CancellationToken ct)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var root = Path.Combine(folder, "catalog");
        if (!Directory.Exists(root))
        {
            return result;
        }
        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
        {
            try
            {
                foreach (var entry in await ReadJsonAsync<List<CatalogEntry>>(file, ct) ?? [])
                {
                    result[entry.Method] = entry.Categories;
                }
            }
            catch (JsonException)
            {
                problems.Add("A test catalog could not be read.");
            }
        }
        return result;
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct)
    {
        if (new FileInfo(path).Length > MaxManifestBytes)
        {
            throw new InvalidDataException("Manifest is too large.");
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, RunManifest.Json, ct);
    }

    private static async Task<TestEnvironment> EnvironmentAsync(AppDbContext db, RunEnvironment env, CancellationToken ct)
    {
        var name = Cap(env.Name, TestResultFieldLimits.ShortTextMaxLength) ?? "unknown";
        var os = Cap(env.Os, TestResultFieldLimits.ShortTextMaxLength) ?? "unknown";
        var runtime = Cap(env.Runtime, TestResultFieldLimits.ShortTextMaxLength) ?? "unknown";
        var baseUrl = Cap(env.BaseUrl, TestResultFieldLimits.ShortTextMaxLength);
        var key = Key($"{name}\n{os}\n{runtime}\n{baseUrl}");
        return await db.TestEnvironments.FirstOrDefaultAsync(e => e.Key == key, ct)
            ?? db.TestEnvironments.Add(new TestEnvironment { Key = key, Name = name, OperatingSystem = os, Runtime = runtime, BaseUrl = baseUrl }).Entity;
    }

    private IReadOnlyCollection<string> KnownSecrets() =>
        new[] { configuration["Seed:DemoPassword"] }.OfType<string>().Where(s => s.Length > 0).ToArray();

    /// <summary>Joins a file name to a folder and returns null unless the result stays inside the folder.</summary>
    internal static string? SafeChild(string folder, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            return null;
        }
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? full : null;
    }

    internal static string Key(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static string ModuleName(string suite) =>
        Cap(suite.StartsWith("CaseIntake.", StringComparison.Ordinal) ? suite["CaseIntake.".Length..] : suite, TestResultFieldLimits.ShortTextMaxLength) ?? "unknown";

    internal static TestType ParseType(string? type) => Enum.TryParse<TestType>(type, ignoreCase: true, out var t) && Enum.IsDefined(t) ? t : TestType.Unit;

    internal static TestOutcome ParseOutcome(string? outcome) => Enum.TryParse<TestOutcome>(outcome, ignoreCase: true, out var o) && Enum.IsDefined(o) ? o : TestOutcome.Failed;

    internal static TestArtifactKind ParseKind(string? kind) => Enum.TryParse<TestArtifactKind>(kind, ignoreCase: true, out var k) && Enum.IsDefined(k) ? k : TestArtifactKind.Screenshot;

    internal static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webm" => "video/webm",
        ".mp4" => "video/mp4",
        ".zip" => "application/zip",
        ".json" => "application/json",
        ".trx" or ".xml" => "application/xml",
        ".html" or ".htm" => "text/html",
        ".log" or ".txt" => "text/plain",
        _ => "application/octet-stream",
    };

    private static string? Cap(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
