using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CaseIntake.IntegrationTests.Docs;

/// <summary>
/// Synthetic data guard (spec 4.1, workbook R24/R35): every SSN-shaped literal in the repository must use an area
/// number that is never issued (000, 666 or 900-999), so no real person's number can appear in code, tests or docs.
/// Dashed values (NNN-NN-NNNN) are checked everywhere; undashed nine-digit values only where a string, JSON value,
/// query or key=value starts (after a quote, '=', ':' or '/'), so byte sizes and other constants do not trip it.
/// Failures report the file, line and area number only, never the full value.
/// </summary>
public partial class SyntheticSsnGuardTests
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "", ".cs", ".razor", ".cshtml", ".json", ".md", ".yml", ".yaml", ".sh", ".js", ".css", ".html", ".txt",
        ".props", ".targets", ".csproj", ".sln", ".xml", ".http", ".sql", ".webmanifest", ".gitignore",
        ".dockerignore", ".gitattributes", ".editorconfig",
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs", ".idea",
    };

    // Test and build output written at the repository root (not the TestResults source folders under src/ or tests/), and
    // untracked local tool state: .claude/worktrees holds whole extra checkouts, which would be scanned as duplicates.
    private static readonly HashSet<string> SkippedRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "artifacts", "TestResults", "results", ".claude",
    };

    // The Government's Veteran Lookup definition (TBS 4.6) must stay byte for byte, and it contains the Government's own
    // example SSN. The file is skipped only while its bytes match this SHA-256 exactly (the same pin as GovernmentSpec in
    // CaseIntake.VeteranLookup.Tests); any edit, however small, puts the whole file back under the guard (assumption D16).
    private static readonly string GovernmentContractPath = Path.Combine("docs", "contracts", "veteran-lookup.spec.openapi.yaml");
    private const string GovernmentContractSha256 = "bbd95fc2a187220e9bfc1860c84fbf148bfc8b10282ae82f13c05bbb84730f26";

    // ASCII digits only: \d would also match full-width and other Unicode digits, which tests use on purpose as invalid input.
    [GeneratedRegex(@"(?<![0-9])(?<area>[0-9]{3})-[0-9]{2}-[0-9]{4}(?![0-9])|(?<=[""'=:/])(?<area>[0-9]{3})[0-9]{6}(?![0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SsnLiteral();

    [Test]
    public void Every_SSN_shaped_literal_in_the_repository_uses_a_never_issued_area_number()
    {
        var files = RepositoryTextFiles().ToList();
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Logging", "LoggingTests.cs"), StringComparison.Ordinal));

        var violations = files
            .Select(file => (File: file, RelativePath: Path.GetRelativePath(RepositoryRoot.Path, file)))
            .Where(f => f.RelativePath != GovernmentContractPath || !IsPinnedGovernmentContract(f.RelativePath, File.ReadAllBytes(f.File)))
            .SelectMany(f => Violations(File.ReadAllText(f.File)).Select(v => $"{f.RelativePath}:{v.Line} (area {v.Area})"))
            .ToList();

        Assert.True(violations.Count == 0,
            "SSN-shaped values must use a never-issued area number (000, 666 or 900-999):\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// E3-C2-2: the Service History dataset and contract (Change 2) are scanned like every other file, and every SSN in
    /// them is in the never-issued 900-999 area. Neither has an exemption.
    /// </summary>
    [TestCase("src", "CaseIntake.VeteranHistory.Mock", "Data", "service-history.json")]
    [TestCase("docs", "contracts", "veteran-service-history.openapi.yaml")]
    public void The_service_history_data_and_contract_are_scanned_and_use_area_900_to_999_only(params string[] path)
    {
        var file = RepositoryRoot.File(path);
        Assert.Contains(file, RepositoryTextFiles().ToList());

        var text = File.ReadAllText(file);
        var ssns = SsnLiteral().Matches(text).Select(m => m.Groups["area"].Value).ToList();

        Assert.NotEmpty(ssns);
        Assert.All(ssns, area => Assert.Equal('9', area[0]));
        Assert.Empty(Violations(text));
    }

    // Issuable examples are assembled from fragments so this file never contains one as a literal.
    public static TheoryData<string, string?> Samples() => new()
    {
        { "Veteran " + "12" + "3-45-6789" + " file.txt", "123" },
        { "{\"ssn\":" + "55" + "5443333}", "555" },
        { "lookup?ssn=" + "11" + "1223333", "111" },
        { "Veteran 900-45-6789 file.txt", null },
        { "{\"ssn\":\"666443333\"}", null },
        { "{\"ssn\":\"000000000\"}", null },
        { "MaxBytes = 209715200;", null },
        { "\"1234567890\"", null },
        { "build 12-345-6789", null },
        { "\"\uFF19\uFF10\uFF10\uFF14\uFF15\uFF16\uFF17\uFF18\uFF19\"", null },   // full-width digits are not an SSN literal
    };

    [TestCaseSource(nameof(Samples))]
    public void Guard_flags_issuable_areas_only(string text, string? expectedArea)
    {
        var violations = Violations(text).ToList();

        if (expectedArea is null)
        {
            Assert.Empty(violations);
        }
        else
        {
            Assert.Equal(expectedArea, Assert.Single(violations).Area);
        }
    }

    [Test]
    public void The_Government_contract_exemption_matches_the_committed_file_and_is_still_needed()
    {
        var content = File.ReadAllBytes(RepositoryRoot.File(GovernmentContractPath));

        Assert.True(IsPinnedGovernmentContract(GovernmentContractPath, content),
            $"{GovernmentContractPath} no longer matches the pinned SHA-256; restore it from TBS 4.6 or update the pin deliberately.");
        Assert.NotEmpty(Violations(System.Text.Encoding.UTF8.GetString(content)));   // otherwise the exemption is obsolete: remove it
    }

    [Test]
    public void The_Government_contract_is_exempt_only_with_its_exact_bytes_at_its_own_path()
    {
        var content = File.ReadAllBytes(RepositoryRoot.File(GovernmentContractPath));

        Assert.True(IsPinnedGovernmentContract(GovernmentContractPath, content));
        Assert.False(IsPinnedGovernmentContract(GovernmentContractPath, [.. content, (byte)'\n']));   // any edit: scanned again
        Assert.False(IsPinnedGovernmentContract(Path.Combine("docs", "demo.md"), content));          // same bytes elsewhere: scanned
    }

    private static bool IsPinnedGovernmentContract(string relativePath, byte[] content) =>
        string.Equals(relativePath, GovernmentContractPath, StringComparison.Ordinal)
        && Convert.ToHexStringLower(SHA256.HashData(content)) == GovernmentContractSha256;

    private static IEnumerable<(int Line, string Area)> Violations(string text)
    {
        if (text.Contains('\0', StringComparison.Ordinal))
        {
            yield break;   // binary content
        }

        foreach (Match match in SsnLiteral().Matches(text))
        {
            var area = match.Groups["area"].Value;
            if (area is not ("000" or "666") && area[0] != '9')
            {
                yield return (text.AsSpan(0, match.Index).Count('\n') + 1, area);
            }
        }
    }

    private static IEnumerable<string> RepositoryTextFiles()
    {
        var pending = new Stack<string>([RepositoryRoot.Path]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(child);
                var atRoot = string.Equals(directory, RepositoryRoot.Path, StringComparison.Ordinal);
                if (!SkippedDirectories.Contains(name) && !(atRoot && SkippedRootDirectories.Contains(name)))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory).Where(f => TextExtensions.Contains(Path.GetExtension(f))))
            {
                yield return file;
            }
        }
    }
}
