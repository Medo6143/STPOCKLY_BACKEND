using NetArchTest.Rules;

namespace Stockly.ArchitectureTests;

/// <summary>
/// ADR-0005 (update 2026-10-18): the admin ports (<c>Application.Abstractions.Admin</c>) give cross-tenant access, so in
/// Application only <c>Features.Admin</c> may use them. Infrastructure implements them (in <c>Persistence.Admin</c>, the
/// identity adapter and the dashboard code store); the Api only wires them.
/// </summary>
public sealed class AdminPortUsageTests
{
    private const string AdminPorts = Layers.Application + ".Abstractions.Admin";
    private const string AdminFeatures = Layers.Application + ".Features.Admin";

    [Fact]
    public void Application_AdminPorts_UsedOnlyByAdminFeatures()
    {
        var result = Types.InAssembly(Layers.Load(Layers.Application))
            .That()
            .DoNotResideInNamespace(AdminFeatures)
            .And()
            .DoNotResideInNamespace(AdminPorts)
            .ShouldNot()
            .HaveDependencyOn(AdminPorts)
            .GetResult();

        result.ShouldBeSuccessful("only platform-admin handlers may reach across tenants (ADR-0005)");
    }

    [Fact]
    public void Application_AdminFeatures_UseTheAdminPorts()
    {
        var users = Types.InAssembly(Layers.Load(Layers.Application))
            .That()
            .ResideInNamespace(AdminFeatures)
            .And()
            .HaveDependencyOn(AdminPorts)
            .GetTypes();

        users.Should().NotBeEmpty("the rule above passes vacuously if the admin handlers no longer use the ports");
    }

    [Fact]
    public void Infrastructure_CrossTenantAdminQueries_LiveInPersistenceAdmin()
    {
        // The IAdmin* ports read or write across tenants (IPlatformAdminAccounts and IHangfireSessionCodes do not).
        var implementations = Layers.Load(Layers.Infrastructure).GetTypes()
            .Where(type => type.IsClass && type.GetInterfaces().Any(port =>
                port.Namespace == AdminPorts && port.Name.StartsWith("IAdmin", StringComparison.Ordinal)))
            .ToList();

        implementations.Should().NotBeEmpty();
        implementations.Should().OnlyContain(
            type => type.Namespace == Layers.Infrastructure + ".Persistence.Admin",
            "cross-tenant persistence stays in one folder, the only one allowed IgnoreQueryFilters()");
    }
}
