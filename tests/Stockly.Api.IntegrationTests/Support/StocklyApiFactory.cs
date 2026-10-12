using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>
/// Hosts the real Api in memory. Environment "Testing": the developer's .env is not loaded, so tests only see
/// the settings given here.
/// </summary>
public sealed class StocklyApiFactory(string postgres, string redis, bool includeTestEndpoints)
    : WebApplicationFactory<Program>
{
    public const string TestEnvironment = "Testing";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestEnvironment);
        builder.UseSetting("ConnectionStrings:Postgres", postgres);
        builder.UseSetting("ConnectionStrings:Redis", redis);

        if (includeTestEndpoints)
        {
            builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(TestOnlyController).Assembly));
        }
    }
}
