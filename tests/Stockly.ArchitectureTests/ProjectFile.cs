using System.Xml.Linq;

namespace Stockly.ArchitectureTests;

/// <summary>
/// Reads the references declared in a src .csproj. This catches a forbidden reference the moment it is
/// added, even before any type from it is used (the compiler drops unused references from the assembly).
/// </summary>
internal sealed class ProjectFile
{
    private const string SolutionFileName = "Stockly.sln";

    private ProjectFile(string name, IReadOnlySet<string> projectReferences, IReadOnlySet<string> packageReferences)
    {
        Name = name;
        ProjectReferences = projectReferences;
        PackageReferences = packageReferences;
    }

    public string Name { get; }

    public IReadOnlySet<string> ProjectReferences { get; }

    public IReadOnlySet<string> PackageReferences { get; }

    public static string SourceDirectory { get; } = Path.Combine(FindBackendRoot(), "src");

    public static ProjectFile Load(string projectName)
    {
        var path = Path.Combine(SourceDirectory, projectName, $"{projectName}.csproj");
        var document = XDocument.Load(path);

        var projects = document.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(Include(e)))
            .ToHashSet(StringComparer.Ordinal);

        var packages = document.Descendants("PackageReference")
            .Select(Include)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ProjectFile(projectName, projects, packages);
    }

    private static string Include(XElement element) =>
        element.Attribute("Include")?.Value
        ?? throw new InvalidOperationException($"Reference without Include attribute: {element}");

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"{SolutionFileName} not found above {AppContext.BaseDirectory}");
    }
}
