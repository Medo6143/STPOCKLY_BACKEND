namespace Stockly.ArchitectureTests;

/// <summary>
/// AGENTS.md rule 9, ADR-0005 (update 2026-10-18): <c>IgnoreQueryFilters()</c> (cross-tenant access) only in
/// <c>Infrastructure/Persistence/Admin</c>, behind the admin ports that only <c>Application/Features/Admin</c> uses
/// (<see cref="AdminPortUsageTests"/>).
/// </summary>
public sealed class IgnoreQueryFiltersTests
{
    private const string ForbiddenCall = "IgnoreQueryFilters";

    private static readonly string AllowedFolder =
        Path.Combine(Layers.Infrastructure, "Persistence", "Admin") + Path.DirectorySeparatorChar;

    [Fact]
    public void SourceFiles_IgnoreQueryFilters_OnlyUnderInfrastructurePersistenceAdmin()
    {
        var sourceFiles = SourceFiles.All();

        var users = sourceFiles
            .Where(file => SourceFiles.Read(file).Contains(ForbiddenCall, StringComparison.Ordinal))
            .ToList();
        var offenders = users.Where(relative => !relative.StartsWith(AllowedFolder, StringComparison.Ordinal));

        sourceFiles.Should().NotBeEmpty("the scan must see the source tree, or this rule passes vacuously");
        users.Should().Contain(
            file => file.StartsWith(AllowedFolder, StringComparison.Ordinal),
            "the admin queries bypass the filter there; if none does any more, remove the exception");
        offenders.Should().BeEmpty("only the platform-admin persistence may bypass the tenant filter (ADR-0005)");
    }
}
