using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CaseIntake.E2E.Infrastructure;

/// <summary>
/// Writes <c>&lt;TEST_RUN_DIR&gt;/catalog/CaseIntake.E2E.json</c> with every test's NUnit <c>[Category]</c> values, like
/// TestKit's TestCatalog (this project does not reference TestKit). The importer reads it for stories, requirements and
/// the accessibility classification and standards mapping. Does nothing unless <c>TEST_RUN_DIR</c> is set.
/// </summary>
internal static class TestCatalogInitializer
{
#pragma warning disable CA2255 // A test assembly, not a library: runs once when the test host loads it.
    [ModuleInitializer]
    internal static void Initialize()
#pragma warning restore CA2255
    {
        var runDir = Environment.GetEnvironmentVariable("TEST_RUN_DIR");
        if (string.IsNullOrEmpty(runDir))
        {
            return;
        }
        try
        {
            var assembly = typeof(TestCatalogInitializer).Assembly;
            var entries = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsPublic)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(IsTest)
                    .Select(m => new { method = $"{t.FullName}.{m.Name}", categories = Categories(t).Concat(Categories(m)).Distinct(StringComparer.Ordinal).ToArray() }))
                .Where(e => e.categories.Length > 0)
                .ToList();
            var folder = Path.Combine(runDir, "catalog");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"{assembly.GetName().Name}.json"), JsonSerializer.Serialize(entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ReflectionTypeLoadException)
        {
            // The catalog is optional metadata; never fail a test run because of it.
        }
    }

    private static bool IsTest(MethodInfo method) =>
        method.GetCustomAttributes<TestAttribute>(inherit: true).Any() || method.GetCustomAttributes<TestCaseAttribute>(inherit: true).Any()
        || method.GetCustomAttributes<TestCaseSourceAttribute>(inherit: true).Any();

    private static IEnumerable<string> Categories(MemberInfo member) =>
        member.GetCustomAttributes<CategoryAttribute>(inherit: true).Select(c => c.Name);
}
