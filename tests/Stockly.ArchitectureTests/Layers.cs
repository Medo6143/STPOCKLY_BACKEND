using System.Reflection;

namespace Stockly.ArchitectureTests;

/// <summary>Single source of the layer names and assemblies the rules talk about (AGENTS.md §4).</summary>
internal static class Layers
{
    public const string Domain = "Stockly.Domain";
    public const string Contracts = "Stockly.Contracts";
    public const string Application = "Stockly.Application";
    public const string Infrastructure = "Stockly.Infrastructure";
    public const string Api = "Stockly.Api";

    public static readonly IReadOnlyList<string> All = [Domain, Contracts, Application, Infrastructure, Api];

    public static Assembly Load(string layer) => Assembly.Load(layer);
}
