using System.Text.Json;
using CaseIntake.TestResults.Import;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>E5: run.json and evidence.json are read with the documented schema, defaults and tolerance.</summary>
[Category("Story:E5-S2")]
public class ImportModelsTests
{
    [Test]
    public void A_run_manifest_written_by_run_tests_sh_round_trips_with_comments_and_trailing_commas()
    {
        const string json = """
            {
              // written by scripts/run-tests.sh
              "schemaVersion": 1,
              "runKey": "20261016-150000-abc1234",
              "startedAtUtc": "2026-10-16T15:00:00Z",
              "finishedAtUtc": "2026-10-16T15:03:00Z",
              "runner": "scripts/run-tests.sh (all)",
              "status": "Partial",
              "git": { "branch": "main", "commit": "abc1234", "buildId": "local" },
              "imageVersion": "caseintake-eval:local",
              "environment": { "name": "docker compose tests", "os": "Linux", "runtime": "10.0.0", "baseUrl": "" },
              "suites": [ {"name":"CaseIntake.E2E","type":"EndToEnd","trx":null,"exitCode":null,"status":"FailedToStart","message":"not reachable"}, ],
              "serverLogs": ["server-web-e2e.log"],
              "notes": "synthetic",
            }
            """;

        var manifest = JsonSerializer.Deserialize<RunManifest>(json, RunManifest.Json)!;

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("20261016-150000-abc1234", manifest.RunKey);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 15, 3, 0, TimeSpan.Zero), manifest.FinishedAtUtc);
        Assert.Equal("Partial", manifest.Status);
        Assert.Equal(("main", "abc1234", "local"), (manifest.Git!.Branch, manifest.Git.Commit, manifest.Git.BuildId));
        Assert.Equal("caseintake-eval:local", manifest.ImageVersion);
        Assert.Equal(("docker compose tests", "Linux", "10.0.0", ""),
            (manifest.Environment!.Name, manifest.Environment.Os, manifest.Environment.Runtime, manifest.Environment.BaseUrl));
        var suite = Assert.Single(manifest.Suites);
        Assert.Equal(("CaseIntake.E2E", "EndToEnd", null, null, "FailedToStart", "not reachable"),
            (suite.Name, suite.Type, suite.Trx, suite.ExitCode, suite.Status, suite.Message));
        Assert.Equal(["server-web-e2e.log"], manifest.ServerLogs);
        Assert.Equal("synthetic", manifest.Notes);
    }

    [Test]
    public void Missing_manifest_fields_take_the_documented_defaults()
    {
        var manifest = JsonSerializer.Deserialize<RunManifest>("""{"runKey":"r1","suites":[{"name":"S"}]}""", RunManifest.Json)!;

        Assert.Equal((1, "scripts/run-tests.sh", "Completed"), (manifest.SchemaVersion, manifest.Runner, manifest.Status));
        Assert.Null(manifest.Git);
        Assert.Null(manifest.Environment);
        Assert.Null(manifest.FinishedAtUtc);
        Assert.Empty(manifest.ServerLogs);
        var suite = Assert.Single(manifest.Suites);
        Assert.Equal(("Unit", "Completed", (string?)null), (suite.Type, suite.Status, suite.Trx));
        var env = new RunEnvironment();
        Assert.Equal(("unknown", "unknown", "unknown", (string?)null), (env.Name, env.Os, env.Runtime, env.BaseUrl));
    }

    [Test]
    public void Deeply_nested_manifests_are_refused()
    {
        var json = """{"runKey":"r","notes":""" + new string('[', 40) + new string(']', 40) + "}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RunManifest>(json, RunManifest.Json));
    }

    [Test]
    public void An_evidence_manifest_carries_steps_files_and_browser_problems_but_not_its_folder()
    {
        const string json = """
            {
              "fullName": "CaseIntake.E2E.Login.Signs_in", "title": "Signs in", "story": "E5-S8", "requirements": ["R-1", "R-2"],
              "acceptanceCriteria": "AC", "priority": "High", "description": "D", "preconditions": "P", "expected": "E", "actual": "A",
              "browser": "chromium", "viewport": "1440x900", "startedAtUtc": "2026-10-16T15:00:00Z", "finishedAtUtc": "2026-10-16T15:00:05Z",
              "outcome": "Passed",
              "steps": [ { "name": "Open", "outcome": "Passed", "durationMs": 12, "expected": "x", "actual": "y", "message": "m" }, { "name": "Defaults" } ],
              "artifacts": [ { "kind": "Trace", "file": "trace.zip", "caption": "Trace" }, { "file": "shot.png" } ],
              "consoleErrors": ["boom"], "networkFailures": ["GET /x 500"],
              "folderRelativePath": "should be ignored"
            }
            """;

        var manifest = JsonSerializer.Deserialize<EvidenceManifest>(json, RunManifest.Json)!;

        Assert.Equal(("Signs in", "E5-S8", "AC", "High", "D", "P", "E", "A"),
            (manifest.Title, manifest.Story, manifest.AcceptanceCriteria, manifest.Priority, manifest.Description, manifest.Preconditions, manifest.Expected, manifest.Actual));
        Assert.Equal(["R-1", "R-2"], manifest.Requirements);
        Assert.Equal(("chromium", "1440x900", "Passed"), (manifest.Browser, manifest.Viewport, manifest.Outcome));
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 15, 0, 5, TimeSpan.Zero), manifest.FinishedAtUtc);
        Assert.NotNull(manifest.StartedAtUtc);
        Assert.Equal(("Open", "Passed", 12L, "x", "y", "m"),
            (manifest.Steps[0].Name, manifest.Steps[0].Outcome, manifest.Steps[0].DurationMs, manifest.Steps[0].Expected, manifest.Steps[0].Actual, manifest.Steps[0].Message));
        Assert.Equal(("Passed", 0L), (manifest.Steps[1].Outcome, manifest.Steps[1].DurationMs));
        Assert.Equal(("Trace", "trace.zip", "Trace"), (manifest.Artifacts[0].Kind, manifest.Artifacts[0].File, manifest.Artifacts[0].Caption));
        Assert.Equal(("Screenshot", (string?)null), (manifest.Artifacts[1].Kind, manifest.Artifacts[1].Caption));
        Assert.Equal(["boom"], manifest.ConsoleErrors);
        Assert.Equal(["GET /x 500"], manifest.NetworkFailures);
        Assert.Equal(string.Empty, manifest.FolderRelativePath);
    }
}
