using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Abstractions.Time;
using Stockly.Infrastructure.Jobs;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>
/// Hosts the real Api in memory. Environment "Testing": the developer's .env is not loaded, so tests only see
/// the settings given here. Requests may choose their client IP with <see cref="TestClientIp.Header"/>.
/// <c>authPermitLimit</c> (<c>RateLimiting:Auth:PermitLimit</c>) is raised by default so suites that call login and
/// register many times are not throttled; <c>null</c> keeps the Api's default (10 per minute), as the rate-limit tests
/// need. <c>clock</c>, when given, replaces the system clock (and with it Identity's lockout time).
/// OpenRouter is never called: its base URL points nowhere, and the extractor is replaced by <see cref="FakeExtractor"/>.
/// The in-process Hangfire server runs only with <see cref="JobMode.Run"/>.
/// </summary>
public sealed class StocklyApiFactory(
    string postgres,
    string redis,
    bool includeTestEndpoints,
    string jwtSigningKey = TestJwt.SigningKey,
    string? authPermitLimit = StocklyApiFactory.RaisedAuthPermitLimit,
    IClock? clock = null,
    StorageSettings? storage = null,
    StocklyApiFactory.JobMode jobs = StocklyApiFactory.JobMode.Off)
    : WebApplicationFactory<Program>
{
    public const string TestEnvironment = "Testing";
    public const string RaisedAuthPermitLimit = "100000";

    /// <summary>Whether this host runs background jobs.</summary>
    public enum JobMode
    {
        Off,
        Run,
    }

    /// <summary>The AI the jobs of this host use. Tests arrange its answer.</summary>
    public FakeExtractor Extractor { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var storageSettings = storage ?? StorageSettings.Unused;

        builder.UseEnvironment(TestEnvironment);
        builder.UseSetting("ConnectionStrings:Postgres", postgres);
        builder.UseSetting("ConnectionStrings:Redis", redis);
        builder.UseSetting("Jwt:SigningKey", jwtSigningKey);
        builder.UseSetting("Storage:Endpoint", storageSettings.Endpoint);
        builder.UseSetting("Storage:AccessKey", storageSettings.AccessKey);
        builder.UseSetting("Storage:SecretKey", storageSettings.SecretKey);
        builder.UseSetting("Storage:Bucket", storageSettings.Bucket);
        builder.UseSetting("OpenRouter:ApiKey", "test-only-openrouter-key");
        builder.UseSetting("OpenRouter:BaseUrl", "http://127.0.0.1:1/");
        if (authPermitLimit is not null)
        {
            builder.UseSetting("RateLimiting:Auth:PermitLimit", authPermitLimit);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, TestClientIp>();
            services.RemoveAll<IAiInvoiceExtractor>();
            services.AddSingleton<IAiInvoiceExtractor>(Extractor);
            if (jobs == JobMode.Off)
            {
                foreach (var server in services.Where(IsHangfireServer).ToList())
                {
                    services.Remove(server);
                }
            }

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

    private static bool IsHangfireServer(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(HangfireServerHost);
}

/// <summary>Where the Api stores invoice images in a test.</summary>
public sealed record StorageSettings(string Endpoint, string AccessKey, string SecretKey, string Bucket)
{
    public const string TestBucket = "stockly-invoices-test";

    /// <summary>Valid settings for hosts that never store an image (nothing listens on port 1).</summary>
    public static StorageSettings Unused { get; } = new("http://127.0.0.1:1", "unused-access-key", "unused-secret-key", TestBucket);
}
