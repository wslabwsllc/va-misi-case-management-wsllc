using System.Runtime.CompilerServices;
using CaseIntake.Testing;

namespace CaseIntake.IntegrationTests;

/// <summary>Writes this assembly's trait catalog for the test results importer when the run asks for it.</summary>
internal static class TestCatalogInitializer
{
#pragma warning disable CA2255 // A test assembly, not a library: runs once when the test host loads it.
    [ModuleInitializer]
    internal static void Initialize() => TestCatalog.WriteIfRequested(typeof(TestCatalogInitializer).Assembly);
#pragma warning restore CA2255
}
