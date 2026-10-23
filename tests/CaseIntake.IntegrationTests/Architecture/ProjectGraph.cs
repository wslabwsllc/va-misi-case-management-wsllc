using System.Xml.Linq;

namespace CaseIntake.IntegrationTests.Architecture;

/// <summary>Project reference graph read from the csproj files, plus the dependency rules from plan section 0.2.</summary>
public sealed class ProjectGraph(IReadOnlyDictionary<string, ProjectNode> projects)
{
    private static readonly string[] FeatureModules = ["Cases", "Identity", "Attachments", "VeteranLookup.Client", "TestResults"];
    private static readonly string[] Mocks = ["VeteranLookup.Mock", "VeteranHistory.Mock"];
    private static readonly string[] ForbiddenInCore = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.Identity", "Npgsql"];

    public IReadOnlyDictionary<string, ProjectNode> Projects => projects;

    public static ProjectGraph Load(string repositoryRoot)
    {
        var nodes = new Dictionary<string, ProjectNode>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var node = Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
            nodes[node.Name] = node;
        }

        return new ProjectGraph(nodes);
    }

    public static ProjectNode Parse(string csprojXml, string name)
    {
        var doc = XDocument.Parse(csprojXml);
        var refs = doc.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? "").Replace('\\', '/')))
            .ToList();
        var packages = doc.Descendants("PackageReference").Select(e => (string?)e.Attribute("Include") ?? "").ToList();
        return new ProjectNode(name, refs, packages);
    }

    /// <summary>Returns a human-readable description of every violated rule; empty when the graph is valid.</summary>
    public IReadOnlyList<string> Violations()
    {
        var violations = new List<string>();
        foreach (var node in projects.Values)
        {
            var short_ = node.Name.Replace("CaseIntake.", "", StringComparison.Ordinal);
            var refs = node.References.Select(r => r.Replace("CaseIntake.", "", StringComparison.Ordinal)).ToList();

            if (short_ is "Domain")
            {
                Expect(refs.Count == 0, $"{node.Name} must not reference any project (found: {string.Join(", ", refs)}).");
                Expect(!HasForbiddenPackage(node), $"{node.Name} must not use EF Core, Identity or Npgsql packages.");
            }
            else if (short_ is "Contracts")
            {
                Expect(refs.All(r => r == "Domain"), $"{node.Name} may reference only Domain (found: {string.Join(", ", refs)}).");
                Expect(!HasForbiddenPackage(node), $"{node.Name} must not use EF Core, Identity or Npgsql packages.");
            }
            else if (short_ is "Persistence")
            {
                Expect(refs.All(r => r is "Domain" or "Contracts"),
                    $"{node.Name} may reference only Domain and Contracts (found: {string.Join(", ", refs)}).");
            }
            else if (short_ is "VeteranLookup.Client")
            {
                Expect(refs.All(r => r == "Contracts"), $"{node.Name} may reference only Contracts (found: {string.Join(", ", refs)}).");
            }
            else if (Mocks.Contains(short_))
            {
                Expect(!refs.Contains("Persistence") && !refs.Contains("Domain"),
                    $"{node.Name} must not reference application persistence or the domain.");
            }
            else if (FeatureModules.Contains(short_))
            {
                var otherFeatures = refs.Where(r => FeatureModules.Contains(r) || Mocks.Contains(r)).ToList();
                Expect(otherFeatures.Count == 0,
                    $"{node.Name} must not reference another feature module (found: {string.Join(", ", otherFeatures)}).");
                Expect(refs.All(r => r is "Persistence" or "Domain" or "Contracts"),
                    $"{node.Name} may reference only Persistence, Domain and Contracts (found: {string.Join(", ", refs)}).");
            }

            void Expect(bool ok, string message)
            {
                if (!ok) violations.Add(message);
            }
        }

        foreach (var cycle in FindCycles())
        {
            violations.Add($"Project reference cycle: {cycle}");
        }

        return violations;
    }

    private static bool HasForbiddenPackage(ProjectNode node) =>
        node.Packages.Any(p => ForbiddenInCore.Any(f => p.StartsWith(f, StringComparison.OrdinalIgnoreCase)));

    private IEnumerable<string> FindCycles()
    {
        var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
        var found = new List<string>();

        void Visit(string name, List<string> path)
        {
            if (!projects.TryGetValue(name, out var node)) return;
            if (state.GetValueOrDefault(name) == 2) return;
            if (state.GetValueOrDefault(name) == 1)
            {
                found.Add(string.Join(" -> ", path.SkipWhile(p => p != name).Append(name)));
                return;
            }

            state[name] = 1;
            path.Add(name);
            foreach (var r in node.References) Visit(r, path);
            path.RemoveAt(path.Count - 1);
            state[name] = 2;
        }

        foreach (var name in projects.Keys) Visit(name, []);
        return found;
    }
}

public sealed record ProjectNode(string Name, IReadOnlyList<string> References, IReadOnlyList<string> Packages);
