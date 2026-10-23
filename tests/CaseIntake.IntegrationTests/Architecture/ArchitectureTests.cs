using NetArchTest.Rules;

namespace CaseIntake.IntegrationTests.Architecture;

public class ArchitectureTests
{
    private static ProjectNode Node(string name, string refs = "", string packages = "") => ProjectGraph.Parse(
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>{string.Concat(refs.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => $"<ProjectReference Include=\"..\\CaseIntake.{r}\\CaseIntake.{r}.csproj\" />"))}" +
        $"{string.Concat(packages.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => $"<PackageReference Include=\"{p}\" />"))}</ItemGroup></Project>", $"CaseIntake.{name}");

    private static ProjectGraph Graph(params ProjectNode[] nodes) => new(nodes.ToDictionary(n => n.Name));

    [Test]
    public void Repository_project_graph_satisfies_the_dependency_rule()
    {
        var graph = ProjectGraph.Load(RepositoryRoot.Path);

        Assert.NotEmpty(graph.Projects);
        Assert.Empty(graph.Violations());
    }

    [Test]
    public void Module_referencing_another_feature_module_fails()
    {
        var graph = Graph(Node("Cases", "Persistence,Domain,Contracts,Attachments"), Node("Attachments", "Persistence,Domain,Contracts"));

        Assert.Contains(graph.Violations(), v => v.Contains("CaseIntake.Cases") && v.Contains("another feature module"));
    }

    [Test]
    public void Domain_referencing_Contracts_fails()
    {
        var graph = Graph(Node("Domain", "Contracts"), Node("Contracts", "Domain"));

        var violations = graph.Violations();
        Assert.Contains(violations, v => v.Contains("CaseIntake.Domain must not reference any project"));
        Assert.Contains(violations, v => v.StartsWith("Project reference cycle", StringComparison.Ordinal));
    }

    [Test]
    public void Persistence_referencing_Identity_feature_fails()
    {
        var graph = Graph(Node("Persistence", "Domain,Contracts,Identity"), Node("Identity", "Persistence,Domain,Contracts"));

        Assert.NotEmpty(graph.Violations());
    }

    [Test]
    public void Contracts_using_EF_Core_fails()
    {
        var graph = Graph(Node("Contracts", "Domain", "Microsoft.EntityFrameworkCore"), Node("Domain"));

        Assert.Contains(graph.Violations(), v => v.Contains("must not use EF Core"));
    }

    [Test]
    public void VeteranLookup_client_referencing_Persistence_fails()
    {
        var graph = Graph(Node("VeteranLookup.Client", "Contracts,Persistence"));

        Assert.NotEmpty(graph.Violations());
    }

    [Test]
    public void VeteranHistory_mock_referencing_Domain_or_a_feature_module_referencing_it_fails()
    {
        var graph = Graph(Node("VeteranHistory.Mock", "Domain"), Node("Cases", "Persistence,Domain,Contracts,VeteranHistory.Mock"));

        var violations = graph.Violations();
        Assert.Contains(violations, v => v.Contains("CaseIntake.VeteranHistory.Mock must not reference application persistence"));
        Assert.Contains(violations, v => v.Contains("CaseIntake.Cases") && v.Contains("another feature module"));
    }

    [Test]
    public void Domain_and_Contracts_assemblies_have_no_EF_or_Identity_dependency()
    {
        foreach (var assembly in new[] { typeof(CaseIntake.Domain.Cases.CaseStatus).Assembly, typeof(CaseIntake.Contracts.Common.Error).Assembly })
        {
            var result = Types.InAssembly(assembly).ShouldNot()
                .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.Identity", "Npgsql")
                .GetResult();
            Assert.True(result.IsSuccessful, $"{assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Test]
    public void Domain_assembly_does_not_depend_on_Contracts()
    {
        var result = Types.InAssembly(typeof(CaseIntake.Domain.Cases.CaseStatus).Assembly).ShouldNot()
            .HaveDependencyOn("CaseIntake.Contracts").GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Test]
    public void Shared_contract_types_are_defined_exactly_once()
    {
        var assemblies = new[]
        {
            typeof(CaseIntake.Domain.Cases.CaseStatus).Assembly,
            typeof(CaseIntake.Contracts.Common.Error).Assembly,
            typeof(CaseIntake.Persistence.AppDbContext).Assembly,
            typeof(CaseIntake.ServiceDefaults.OperationScope).Assembly,
            typeof(CaseIntake.Attachments.AttachmentsModule).Assembly,
            typeof(CaseIntake.VeteranLookup.Client.VeteranLookupModule).Assembly,
        };
        var duplicates = assemblies.SelectMany(a => a.GetExportedTypes()).GroupBy(t => t.FullName).Where(g => g.Count() > 1).Select(g => g.Key);

        Assert.Empty(duplicates);
    }
}
