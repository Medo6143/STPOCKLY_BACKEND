using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Jobs;
using Stockly.Application.Abstractions.Storage;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Infrastructure.Jobs;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Jobs;

/// <summary>
/// <see cref="ProcessInvoiceJob"/> end to end on a real PostgreSQL with the production persistence and Application wiring;
/// only storage, the AI and the caches are fakes. Also Hangfire's storage in schema <c>hangfire</c>.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ProcessInvoiceJobTests(PostgresFixture fixture) : IAsyncDisposable
{
    private const string Model = "google/gemini-2.5-flash";

    private readonly FakeExtractor _extractor = new();
    private readonly InMemoryStorage _storage = new();
    private readonly List<ServiceProvider> _providers = [];

    [Fact]
    public async Task RunAsync_ProcessingInvoice_SavesLinesAndUsageRowsInOneTransaction()
    {
        var shop = await fixture.CreateShopAsync();
        var invoice = await SeedProcessingInvoiceAsync(shop.Id);
        _extractor.Result = Success(Line("بنادول", "10", "12.50"), Line("كونجستال", "1", "20.00"));

        await RunJobAsync(shop.Id, invoice.Id);

        await using var context = fixture.CreateContext(shop.Id);
        var saved = await context.Set<Invoice>().Include(i => i.Items).AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        saved.Status.Should().Be(InvoiceStatus.NeedsReview);
        saved.Items.OrderBy(item => item.LineNo).Select(item => (item.RawName, item.LineTotal))
            .Should().Equal(("بنادول", 125.00m), ("كونجستال", 20.00m));
        saved.ComputedTotal.Should().Be(145.00m);
        saved.RawAiJson.Should().NotBeNull();
        (await context.Set<AiUsageRecord>().CountAsync(record => record.InvoiceId == invoice.Id)).Should().Be(2);
        (await context.Products.CountAsync()).Should().Be(0, "the AI never changes stock (rule 19)");
    }

    [Fact]
    public async Task RunAsync_LineNamesAnExistingProduct_SavedMatchedWithoutChangingStock()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id, "بنادول اكسترا");
        await using (var seed = fixture.CreateContext(shop.Id))
        {
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }

        var invoice = await SeedProcessingInvoiceAsync(shop.Id);
        _extractor.Result = Success(Line("بنادول إكسترا", "2", "15.00"), Line("غير معروف", "1", "1.00"));

        await RunJobAsync(shop.Id, invoice.Id);

        await using var context = fixture.CreateContext(shop.Id);
        var items = await context.Set<InvoiceItem>().AsNoTracking()
            .Where(item => item.InvoiceId == invoice.Id).OrderBy(item => item.LineNo).ToListAsync();
        items.Select(item => (item.ProductId, item.ProductUnitId, item.MatchConfidence))
            .Should().Equal((product.Id, product.BaseUnit.Id, 0.90m), (null, null, null));
        (await context.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id)).Quantity.Should().Be(0m, "rule 19");
    }

    [Fact]
    public async Task RunAsync_Twice_SecondRunWritesNothing()
    {
        var shop = await fixture.CreateShopAsync();
        var invoice = await SeedProcessingInvoiceAsync(shop.Id);
        _extractor.Result = Success(Line("بنادول", "1", "1.00"));

        await RunJobAsync(shop.Id, invoice.Id);
        await RunJobAsync(shop.Id, invoice.Id);

        _extractor.Calls.Should().Be(1, "a finished invoice is skipped (idempotent job)");
        await using var context = fixture.CreateContext(shop.Id);
        (await context.Set<InvoiceItem>().CountAsync(item => item.InvoiceId == invoice.Id)).Should().Be(1);
        (await context.Set<AiUsageRecord>().CountAsync(record => record.InvoiceId == invoice.Id)).Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_SamePhotoAsAnExtractedInvoice_ReusesItWithACacheHitRow()
    {
        var shop = await fixture.CreateShopAsync();
        var first = await SeedProcessingInvoiceAsync(shop.Id);
        _extractor.Result = Success(Line("بنادول", "1", "1.00"));
        await RunJobAsync(shop.Id, first.Id);
        var second = await SeedProcessingInvoiceAsync(shop.Id);

        await RunJobAsync(shop.Id, second.Id);

        _extractor.Calls.Should().Be(1);
        await using var context = fixture.CreateContext(shop.Id);
        var usage = await context.Set<AiUsageRecord>().AsNoTracking().SingleAsync(record => record.InvoiceId == second.Id);
        usage.Should().BeEquivalentTo(new { CacheHit = true, Model, CostUsd = 0m, Outcome = AiCallOutcome.Succeeded });
        (await context.Set<Invoice>().SingleAsync(i => i.Id == second.Id)).Status.Should().Be(InvoiceStatus.NeedsReview);
    }

    [Fact]
    public async Task RunAsync_AiFails_FailedWithReasonAndAttemptRows()
    {
        var shop = await fixture.CreateShopAsync();
        var invoice = await SeedProcessingInvoiceAsync(shop.Id);
        _extractor.Result = new AiExtractionResult(
            null,
            InvoiceFailureReason.AiOutputInvalid,
            null,
            "garbage",
            [new AiAttempt(Model, AiCallOutcome.InvalidOutput, 200, 10, 5, 0.001m, 800)]);

        await RunJobAsync(shop.Id, invoice.Id);

        await using var context = fixture.CreateContext(shop.Id);
        var saved = await context.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        saved.Status.Should().Be(InvoiceStatus.Failed);
        saved.FailureReason.Should().Be(InvoiceFailureReason.AiOutputInvalid);
        (await fixture.ScalarAsync<string>("SELECT failure_reason FROM invoices WHERE id = @p0", invoice.Id)).Should().Be("AiOutputInvalid");
        saved.RawAiJson.Should().Be("\"garbage\"");
        (await context.Set<AiUsageRecord>().CountAsync(record => record.InvoiceId == invoice.Id)).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ImageMissing_FailedWithoutUsageRows()
    {
        var shop = await fixture.CreateShopAsync();
        var invoice = await SeedProcessingInvoiceAsync(shop.Id, storeImage: false);

        await RunJobAsync(shop.Id, invoice.Id);

        await using var context = fixture.CreateContext(shop.Id);
        (await context.Set<Invoice>().SingleAsync(i => i.Id == invoice.Id)).FailureReason.Should().Be(InvoiceFailureReason.ImageMissing);
        (await context.Set<AiUsageRecord>().CountAsync(record => record.InvoiceId == invoice.Id)).Should().Be(0);
        _extractor.Calls.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_OtherShopsInvoice_TouchesNothing()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var invoice = await SeedProcessingInvoiceAsync(shop.Id);
        _extractor.Result = Success(Line("بنادول", "1", "1.00"));

        await RunJobAsync(other.Id, invoice.Id);

        _extractor.Calls.Should().Be(0);
        await using var context = fixture.CreateContext(shop.Id);
        (await context.Set<Invoice>().SingleAsync(i => i.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Processing);
    }

    [Fact]
    public async Task FailInvoiceProcessingJob_ProcessingInvoice_FailedUnexpected()
    {
        var shop = await fixture.CreateShopAsync();
        var invoice = await SeedProcessingInvoiceAsync(shop.Id);

        await using (var scope = CreateProvider().CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FailInvoiceProcessingJob>().RunAsync(shop.Id, invoice.Id, CancellationToken.None);
        }

        await using var context = fixture.CreateContext(shop.Id);
        (await context.Set<Invoice>().SingleAsync(i => i.Id == invoice.Id)).FailureReason.Should().Be(InvoiceFailureReason.Unexpected);
    }

    [Fact]
    public async Task Scheduler_Enqueue_StoresAProcessInvoiceJobOnTheInvoicesQueueInSchemaHangfire()
    {
        var shopId = Guid.CreateVersion7();
        var invoiceId = Guid.CreateVersion7();

        CreateProvider().GetRequiredService<IBackgroundJobScheduler>().EnqueueProcessInvoice(shopId, invoiceId);

        var job = await fixture.ScalarAsync<string>(
            "SELECT j.invocationdata::text || ' ' || j.arguments::text FROM hangfire.job j JOIN hangfire.jobqueue q ON q.jobid = j.id " +
            "WHERE q.queue = 'invoices' AND j.arguments::text LIKE @p0",
            $"%{invoiceId}%");
        job.Should().Contain(nameof(ProcessInvoiceJob)).And.Contain(shopId.ToString(), "arguments are ids only (rule 12)");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }

    private async Task RunJobAsync(Guid shopId, Guid invoiceId)
    {
        // One DI scope per run, as Hangfire's activator does.
        await using var scope = CreateProvider().CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProcessInvoiceJob>().RunAsync(shopId, invoiceId, CancellationToken.None);
    }

    private async Task<Invoice> SeedProcessingInvoiceAsync(Guid shopId, bool storeImage = true)
    {
        var invoice = Invoice.CreateForScan(
            shopId, Guid.CreateVersion7(), Catalog.UserId, Sha(shopId), TestTime.T0, id => $"invoices/{shopId}/{id}.jpg").Value;
        await using var context = fixture.CreateContext(shopId);
        context.Add(invoice);
        await context.SaveChangesAsync();
        if (storeImage)
        {
            await _storage.PutAsync(invoice.ImageObjectKey, new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg", CancellationToken.None);
        }

        return invoice;
    }

    // One photo per shop in these tests, so a second invoice of the same shop is the same photo.
    private static string Sha(Guid shopId) => Convert.ToHexStringLower(shopId.ToByteArray()) + new string('d', 32);

    private ServiceProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(fixture.ConnectionString);
        services.AddApplication();
        services.AddStocklyJobs(configuration, fixture.ConnectionString);
        services.AddScoped<ICurrentTenant, JobTenant>();
        services.AddSingleton<IFileStorage>(_storage);
        services.AddSingleton<IAiInvoiceExtractor>(_extractor);
        services.AddSingleton<IAiResultCache, NoAiCache>();
        services.AddSingleton<IInvoiceStatusCache, NoStatusCache>();
        var provider = services.BuildServiceProvider(validateScopes: true);
        _providers.Add(provider);
        return provider;
    }

    private static AiExtractionResult Success(params ExtractedInvoiceLine[] lines)
    {
        var extraction = new ExtractedInvoice(null, null, null, null, false, lines);
        return new AiExtractionResult(
            extraction,
            null,
            Model,
            JsonSerializer.Serialize(extraction, AiJson.Options),
            [new AiAttempt(Model, AiCallOutcome.ServerError, 503, 0, 0, 0m, 100), new AiAttempt(Model, AiCallOutcome.Succeeded, 200, 900, 300, 0.002m, 2000)]);
    }

    private static ExtractedInvoiceLine Line(string name, string quantity, string unitPrice) => new(name, null, quantity, unitPrice, null, null, null);

    private sealed class JobTenant(JobTenantContext job) : ICurrentTenant
    {
        public Guid? ShopId => job.ShopId;
    }

    private sealed class FakeExtractor : IAiInvoiceExtractor
    {
        public AiExtractionResult? Result { get; set; }

        public int Calls { get; private set; }

        public Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result ?? throw new InvalidOperationException("No AI result arranged."));
        }
    }

    private sealed class InMemoryStorage : IFileStorage
    {
        private readonly Dictionary<string, StoredFile> _files = [];

        public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
        {
            _files[key] = new StoredFile(content.ToArray(), contentType);
            return Task.CompletedTask;
        }

        public Task<StoredFile?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_files.GetValueOrDefault(key));

        public Task<string?> GetDownloadUrlAsync(string key, TimeSpan validFor, CancellationToken cancellationToken) =>
            Task.FromResult(_files.ContainsKey(key) ? $"memory://{key}" : null);

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            _files.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class NoAiCache : IAiResultCache
    {
        public Task<CachedExtraction?> GetAsync(Guid shopId, string imageSha256, CancellationToken cancellationToken) =>
            Task.FromResult<CachedExtraction?>(null);

        public Task SetAsync(Guid shopId, string imageSha256, string model, ExtractedInvoice extraction, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoStatusCache : IInvoiceStatusCache
    {
        public Task<InvoiceStatusSnapshot?> GetAsync(Guid shopId, Guid invoiceId, CancellationToken cancellationToken) =>
            Task.FromResult<InvoiceStatusSnapshot?>(null);

        public Task SetAsync(Guid shopId, InvoiceStatusSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetIfAbsentAsync(Guid shopId, InvoiceStatusSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
