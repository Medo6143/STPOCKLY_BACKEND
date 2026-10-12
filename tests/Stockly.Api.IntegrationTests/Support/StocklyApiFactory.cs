using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>
/// Hosts the real Api in memory. Environment "Testing": the developer's .env is not loaded, so tests only see
/// the settings given here. Requests may choose their client IP with <see cref="TestClientIp.Header"/>.
/// <c>authPermitLimit</c> (<c>RateLimiting:Auth:PermitLimit</c>) is raised by default so suites that call login and
/// register many times are not throttled; <c>null</c> keeps the Api's default (10 per minute), as the rate-limit tests
/// need. <c>clock</c>, when given, replaces the system clock (and with it Identity's lockout time).
/// </summary>
public sealed class StocklyApiFactory(
    string postgres,
    string redis,
    bool includeTestEndpoints,
    string jwtSigningKey = TestJwt.SigningKey,
    string? authPermitLimit = StocklyApiFactory.RaisedAuthPermitLimit,
    IClock? clock = null)
    : WebApplicationFactory<Program>
{
    public const string TestEnvironment = "Testing";
    public const string RaisedAuthPermitLimit = "100000";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestEnvironment);
        builder.UseSetting("ConnectionStrings:Postgres", postgres);
        builder.UseSetting("ConnectionStrings:Redis", redis);
        builder.UseSetting("Jwt:SigningKey", jwtSigningKey);
        if (authPermitLimit is not null)
        {
            builder.UseSetting("RateLimiting:Auth:PermitLimit", authPermitLimit);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, TestClientIp>();
            if (clock is not null)
            {
                services.AddSingleton(clock);
            }

            if (includeTestEndpoints)
            {
                services.AddControllers().AddApplicationPart(typeof(TestOnlyController).Assembly);
            }
        });
    }
}
