namespace CaseIntake.IntegrationTests;

internal static class RepositoryRoot
{
    /// <summary>Directory containing global.json, found by walking up from the test binaries (works locally and in the image).</summary>
    public static string Path { get; } = Find();

    public static string File(params string[] segments) => System.IO.Path.Combine([Path, .. segments]);

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root (global.json).");
    }
}
