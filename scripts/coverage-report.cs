#:property RestorePackagesWithLockFile=false
// Summarises the Cobertura files that scripts/coverage.sh collects: line coverage per application assembly and in
// total, counting a line as covered when any suite covered it. Writes the summary to stdout and to <dir>/summary.md and
// exits 1 when the total or any assembly is below the threshold.
//   dotnet run scripts/coverage-report.cs -- <results-dir> [threshold-percent]
using System.Globalization;
using System.Text;
using System.Xml.Linq;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/coverage-report.cs -- <results-dir> [threshold-percent]");
    return 2;
}
var dir = args[0];
var threshold = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 80.0;
var reports = Directory.GetFiles(dir, "*.cobertura.xml", SearchOption.AllDirectories);
if (reports.Length == 0)
{
    Console.Error.WriteLine($"FAIL: no *.cobertura.xml files under {dir}");
    return 2;
}

// (assembly, file, line) -> covered by at least one suite
var lines = new Dictionary<(string Assembly, string File, int Line), bool>();
foreach (var report in reports)
{
    foreach (var package in XDocument.Load(report).Descendants("package"))
    {
        var assembly = (string)package.Attribute("name")!;
        foreach (var cls in package.Descendants("class"))
        {
            var file = (string)cls.Attribute("filename")!;
            foreach (var line in cls.Descendants("line"))
            {
                var key = (assembly, file, (int)line.Attribute("number")!);
                var hit = (long)line.Attribute("hits")! > 0;
                lines[key] = lines.TryGetValue(key, out var seen) ? seen || hit : hit;
            }
        }
    }
}

var rows = lines.GroupBy(l => l.Key.Assembly, StringComparer.Ordinal)
    .Select(g => (Name: g.Key, Covered: g.Count(l => l.Value), Total: g.Count()))
    .OrderBy(r => r.Name, StringComparer.Ordinal)
    .ToList();
var covered = rows.Sum(r => r.Covered);
var total = rows.Sum(r => r.Total);
static double Percent(int covered, int total) => total == 0 ? 100 : 100.0 * covered / total;

var failed = false;
var md = new StringBuilder();
md.AppendLine("| Assembly | Lines covered | Line coverage |").AppendLine("|---|---:|---:|");
foreach (var (name, c, t) in rows)
{
    var below = Percent(c, t) < threshold;
    failed |= below;
    md.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {c} / {t} | {Percent(c, t):0.0}%{(below ? " (below threshold)" : "")} |");
}
var totalBelow = Percent(covered, total) < threshold;
failed |= totalBelow;
md.AppendLine(CultureInfo.InvariantCulture, $"| **Total** | **{covered} / {total}** | **{Percent(covered, total):0.0}%** |");

Console.Write(md);
File.WriteAllText(Path.Combine(dir, "summary.md"), md.ToString());
Console.WriteLine(failed
    ? $"FAIL: line coverage is below {threshold:0.#}% (total or at least one assembly)"
    : $"PASS: total and every assembly at or above {threshold:0.#}% line coverage");
return failed ? 1 : 0;
