using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Stockly.Api.Configuration;

/// <summary>
/// Forwarded headers for a deployment behind a reverse proxy (contract note 19: rate limits use the client IP), off by
/// default. <c>ForwardedHeaders__Enabled=true</c> trusts <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from one
/// proxy hop, and only from <c>ForwardedHeaders__KnownProxies</c> (comma-separated IPs) and
/// <c>ForwardedHeaders__KnownNetworks</c> (comma-separated CIDR); with neither, only from loopback (ASP.NET Core's
/// default). Invalid values fail start-up naming the variable.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    private const string EnabledKey = "Enabled";
    private const string ProxiesKey = "KnownProxies";
    private const string NetworksKey = "KnownNetworks";

    /// <summary>Registers the options when enabled; returns whether the middleware must run.</summary>
    public static bool AddStocklyForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var enabledText = section[EnabledKey];
        if (string.IsNullOrWhiteSpace(enabledText))
        {
            return false;
        }

        if (!bool.TryParse(enabledText, out var enabled))
        {
            throw new InvalidOperationException($"{SectionName}__{EnabledKey} must be true or false.");
        }

        if (!enabled)
        {
            return false;
        }

        var proxies = Split(section[ProxiesKey])
            .Select(text => IPAddress.TryParse(text, out var address)
                ? address
                : throw new InvalidOperationException($"{SectionName}__{ProxiesKey} must be a comma-separated list of IP addresses."))
            .ToList();
        var networks = Split(section[NetworksKey])
            .Select(text => System.Net.IPNetwork.TryParse(text, out var network)
                ? network
                : throw new InvalidOperationException($"{SectionName}__{NetworksKey} must be a comma-separated list of CIDR networks such as 10.0.0.0/8."))
            .ToList();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            if (proxies.Count > 0 || networks.Count > 0)
            {
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                proxies.ForEach(options.KnownProxies.Add);
                networks.ForEach(options.KnownIPNetworks.Add);
            }
        });
        return true;
    }

    private static string[] Split(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
