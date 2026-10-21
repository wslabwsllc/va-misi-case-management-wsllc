using System.Reflection;
using System.Text.Json;

namespace CaseIntake.TestKit;

/// <summary>
/// Writes <c>&lt;TEST_RUN_DIR&gt;/catalog/&lt;assembly&gt;.json</c>: every test method with its NUnit <c>[Category]</c> values
/// (for example <c>Story:E5-S8</c>, <c>Req:R-4.2-11</c>, <c>Priority:High</c>). the catalog keeps them available to the importer even when a TRX omits them, so the
/// test results importer reads this catalog to link results to stories and requirements. Does nothing unless
/// <c>TEST_RUN_DIR</c> is set (scripts/run-tests.sh sets it).
/// </summary>
public static class TestCatalog
{
    public static void WriteIfRequested(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var runDir = Environment.GetEnvironmentVariable("TEST_RUN_DIR");
        if (string.IsNullOrEmpty(runDir))
        {
            return;
        }
        try
        {
            var entries = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsPublic)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(IsTest)
                    .Select(m => new
                    {
                        method = $"{t.FullName}.{m.Name}",
                        categories = Categories(t).Concat(Categories(m)).Distinct(StringComparer.Ordinal).ToArray(),
                    }))
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
