using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>
/// Test host only: the in-memory server has no network peer, so a request names its client IP in <see cref="Header"/>.
/// Runs before the Api's middleware, so the rate limiter partitions by it.
/// </summary>
internal sealed class TestClientIp : IStartupFilter
{
    public const string Header = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(Header, out var value) && IPAddress.TryParse(value, out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}
