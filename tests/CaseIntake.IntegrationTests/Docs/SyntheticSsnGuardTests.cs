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

    // Test and build output written at the repository root (not the TestResults source folders under src/ or tests/).
    private static readonly HashSet<string> SkippedRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "artifacts", "TestResults", "results",
    };

    // ASCII digits only: \d would also match full-width and other Unicode digits, which tests use on purpose as invalid input.
    [GeneratedRegex(@"(?<![0-9])(?<area>[0-9]{3})-[0-9]{2}-[0-9]{4}(?![0-9])|(?<=[""'=:/])(?<area>[0-9]{3})[0-9]{6}(?![0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SsnLiteral();

    [Fact]
    public void Every_SSN_shaped_literal_in_the_repository_uses_a_never_issued_area_number()
    {
        var files = RepositoryTextFiles().ToList();
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Logging", "LoggingTests.cs"), StringComparison.Ordinal));

        var violations = files.SelectMany(file => Violations(File.ReadAllText(file))
            .Select(v => $"{Path.GetRelativePath(RepositoryRoot.Path, file)}:{v.Line} (area {v.Area})")).ToList();

        Assert.True(violations.Count == 0,
            "SSN-shaped values must use a never-issued area number (000, 666 or 900-999):\n" + string.Join("\n", violations));
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

    [Theory]
    [MemberData(nameof(Samples))]
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
