namespace Stockly.ArchitectureTests;

/// <summary>AGENTS.md rule 9, ADR-0005: <c>IgnoreQueryFilters()</c> only under Application/Features/Admin.</summary>
public sealed class IgnoreQueryFiltersTests
{
    private const string ForbiddenCall = "IgnoreQueryFilters";

    private static readonly string AllowedFolder =
        Path.Combine(Layers.Application, "Features", "Admin") + Path.DirectorySeparatorChar;

    [Fact]
    public void SourceFiles_IgnoreQueryFilters_OnlyUnderApplicationFeaturesAdmin()
    {
        var sourceFiles = SourceFiles().ToList();

        var offenders = sourceFiles
            .Where(file => File.ReadAllText(file).Contains(ForbiddenCall, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(ProjectFile.SourceDirectory, file))
            .Where(relative => !relative.StartsWith(AllowedFolder, StringComparison.Ordinal));

        sourceFiles.Should().NotBeEmpty("the scan must see the source tree, or this rule passes vacuously");
        offenders.Should().BeEmpty("only platform-admin handlers may bypass the tenant filter (ADR-0005)");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(ProjectFile.SourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string file)
    {
        var parts = Path.GetRelativePath(ProjectFile.SourceDirectory, file).Split(Path.DirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }
}
