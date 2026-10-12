namespace Stockly.ArchitectureTests;

/// <summary>
/// ADR-0002 (update 2026-10-12): <c>ITenantScope</c> lets a request write tenant rows without a token's shop. Only
/// registration may do that, so in Application only <c>Features/Auth/Register</c> may mention it.
/// </summary>
public sealed class TenantScopeUsageTests
{
    private const string TenantScope = "ITenantScope";

    private static readonly string Declaration = SourceFiles.PathOf(Layers.Application, "Abstractions", "Identity", "ITenantScope.cs");

    private static readonly string AllowedFolder =
        SourceFiles.PathOf(Layers.Application, "Features", "Auth", "Register") + Path.DirectorySeparatorChar;

    [Fact]
    public void ApplicationSources_ITenantScope_OnlyInRegisterAndItsDeclaration()
    {
        var applicationFiles = SourceFiles.All()
            .Where(file => file.StartsWith(Layers.Application + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();

        var offenders = applicationFiles
            .Where(file => SourceFiles.Read(file).Contains(TenantScope, StringComparison.Ordinal))
            .Where(file => file != Declaration && !file.StartsWith(AllowedFolder, StringComparison.Ordinal));

        applicationFiles.Should().Contain(Declaration, "the scan must see the declaration, or this rule passes vacuously");
        applicationFiles.Where(file => file.StartsWith(AllowedFolder, StringComparison.Ordinal)
                && SourceFiles.Read(file).Contains(TenantScope, StringComparison.Ordinal))
            .Should().NotBeEmpty("registration uses the scope; if it no longer does, remove the exception");
        offenders.Should().BeEmpty("only registration may write a shop's rows before the shop has a token (ADR-0002)");
    }
}
