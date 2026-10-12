namespace Stockly.ArchitectureTests;

/// <summary>The C# files under <c>src/</c>, excluding build output. Paths are relative to <c>src/</c>.</summary>
internal static class SourceFiles
{
    public static IReadOnlyList<string> All() =>
        Directory.EnumerateFiles(ProjectFile.SourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(ProjectFile.SourceDirectory, file))
            .Where(relative => !IsBuildOutput(relative))
            .ToList();

    public static string Read(string relativePath) => File.ReadAllText(Path.Combine(ProjectFile.SourceDirectory, relativePath));

    public static string PathOf(params string[] segments) => Path.Combine(segments);

    private static bool IsBuildOutput(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }
}
