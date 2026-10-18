using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Stockly.LoadTests;

// Load test of the hot endpoints (docs/operations/load-test.md). Start the Api in the LoadTest environment with the fake
// AI first; this program only sends HTTP requests to it.
var options = LoadTestOptions.Parse(args);

var services = new ServiceCollection();
services.AddHttpClient<StocklyClient>(client =>
{
    client.BaseAddress = options.BaseUrl;
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { MaxConnectionsPerServer = options.Concurrency * 2 });
await using var provider = services.BuildServiceProvider();
var api = provider.GetRequiredService<StocklyClient>();

Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"Stockly load test against {options.BaseUrl}: {options.Concurrency} users, {options.Duration.TotalSeconds:F0} s per timed scenario, {options.Scans} scans."));

// Set-up (not measured): one shop with products for P1 and S1.
var owner = await api.RegisterShopAsync(0);
var products = new List<SellableProduct>();
for (var index = 0; index < options.Products; index++)
{
    products.Add(await api.CreateProductAsync(owner, string.Create(CultureInfo.InvariantCulture, $"Load test product {index:D4}")));
}

// Warm-up (not measured): one request of each kind, so JIT, connection pools and first-use initialisation stay out of
// the numbers. The cold start is reported separately.
var coldStart = System.Diagnostics.Stopwatch.StartNew();
var warmUpShop = await api.RegisterShopAsync(-1);
var warmUpScan = await api.ScanAsync(warmUpShop, options.ImageKilobytes);
coldStart.Stop();
await api.ListProductsAsync(owner);
await api.SellAsync(owner, products);
if (warmUpScan is { } warmUpInvoice)
{
    await api.GetInvoiceAsync(new ScannedInvoice(warmUpInvoice, warmUpShop));
}

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Warm-up register + first scan: {coldStart.Elapsed.TotalMilliseconds:F0} ms."));

var results = new List<ScenarioResult>();
if (options.Runs("p1"))
{
    results.Add(await ScenarioRunner.ForDurationAsync("P1 product list", options.Concurrency, options.Duration, _ => api.ListProductsAsync(owner)));
}

if (options.Runs("s1"))
{
    results.Add(await ScenarioRunner.ForDurationAsync("S1 create sale", options.Concurrency, options.Duration, _ => api.SellAsync(owner, products)));
}

// I1: scans spread over shops so neither the per-minute limit nor the daily quota (30 on the default plan) is reached;
// raise Scan__RateLimitPerMinute on the Api for the run. Shops are registered before the measurement.
const int ScansPerShop = 25;
var shopTokens = new List<string>();
var scanCount = options.Runs("i1") || options.Runs("i3") ? options.Scans : 0;
for (var index = 0; index < (scanCount + ScansPerShop - 1) / ScansPerShop; index++)
{
    shopTokens.Add(await api.RegisterShopAsync(index + 1));
}

var invoices = new System.Collections.Concurrent.ConcurrentBag<ScannedInvoice>();
var scanResult = await ScenarioRunner.ForCountAsync("I1 scan", options.Concurrency, scanCount, async number =>
{
    var token = shopTokens[number / ScansPerShop];
    var id = await api.ScanAsync(token, options.ImageKilobytes);
    if (id is { } invoiceId)
    {
        invoices.Add(new ScannedInvoice(invoiceId, token));
    }

    return id is not null;
});
if (options.Runs("i1"))
{
    results.Add(scanResult);
}

// I3: the app polls a processing invoice every 2 s; here every user polls back to back, on the scanned invoices.
var scanned = invoices.ToList();
if (options.Runs("i3") && scanned.Count > 0)
{
    var processingBefore = await api.CountProcessingAsync(scanned);
    results.Add(await ScenarioRunner.ForDurationAsync(
        "I3 poll invoice", options.Concurrency, options.Duration, _ => api.GetInvoiceAsync(scanned[RandomNumberGenerator.GetInt32(scanned.Count)])));
    var processingAfter = await api.CountProcessingAsync(scanned);
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"I3: {processingBefore} of {scanned.Count} invoices were Processing (status cache path) before polling, {processingAfter} after."));
}

Console.WriteLine();
Console.WriteLine(ScenarioRunner.MarkdownHeader);
foreach (var result in results)
{
    Console.WriteLine(result.ToMarkdownRow());
}

return results.Exists(result => result.ErrorRate > 0.01) ? 1 : 0;
