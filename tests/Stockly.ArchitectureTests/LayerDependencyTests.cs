using NetArchTest.Rules;

namespace Stockly.ArchitectureTests;

/// <summary>Type-level dependencies of each compiled layer (AGENTS.md §4, ADR-0010).</summary>
public sealed class LayerDependencyTests
{
    // Namespaces the compiler may emit into any assembly (nullable and embedded attributes).
    private const string CompilerGenerated = "Microsoft.CodeAnalysis";
    private const string Bcl = "System";

    private static readonly string[] ForbiddenForApplication =
    [
        Layers.Infrastructure,
        Layers.Api,
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "Microsoft.Extensions.Caching.StackExchangeRedis",
        "Hangfire",
        "Amazon",
        "Microsoft.AspNetCore",
        "System.Net.Http.HttpClient",
    ];

    [Fact]
    public void Domain_DependsOnlyOnBcl()
    {
        var result = Types.InAssembly(Layers.Load(Layers.Domain))
            .Should()
            .OnlyHaveDependenciesOn(Layers.Domain, Bcl, CompilerGenerated)
            .GetResult();

        result.ShouldBeSuccessful("Domain references nothing but the BCL (ADR-0010)");
    }

    [Fact]
    public void Contracts_DependsOnlyOnBcl()
    {
        var result = Types.InAssembly(Layers.Load(Layers.Contracts))
            .Should()
            .OnlyHaveDependenciesOn(Layers.Contracts, Bcl, CompilerGenerated)
            .GetResult();

        result.ShouldBeSuccessful("Contracts references nothing but the BCL");
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureConcerns()
    {
        var result = Types.InAssembly(Layers.Load(Layers.Application))
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenForApplication)
            .GetResult();

        result.ShouldBeSuccessful("Application only defines interfaces; Infrastructure implements them");
    }

    [Fact]
    public void Admin_DoesNotDependOnServerLayers()
    {
        var result = Types.InAssembly(Layers.Load(Layers.Admin))
            .ShouldNot()
            .HaveDependencyOnAny(Layers.Domain, Layers.Application, Layers.Infrastructure, Layers.Api)
            .GetResult();

        result.ShouldBeSuccessful("Stockly.Admin talks to the API over HTTP with Contracts only (ADR-0005)");
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnApi()
    {
        var result = Types.InAssembly(Layers.Load(Layers.Infrastructure))
            .ShouldNot()
            .HaveDependencyOn(Layers.Api)
            .GetResult();

        result.ShouldBeSuccessful("Infrastructure must never reference Api");
    }
}
