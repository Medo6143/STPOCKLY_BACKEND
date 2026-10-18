using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Time;
using Stockly.Infrastructure.Jobs;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Jobs;

/// <summary>
/// The daily image retention job through the production wiring (MediatR pipeline, transaction, admin persistence) on a
/// real PostgreSQL, without a tenant (docs/operations/image-lifecycle.md).
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ExpireInvoiceImagesJobTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2027, 6, 1, 1, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_MarksTheImagesOlderThan90Days()
    {
        var shop = await fixture.CreateShopAsync();
        var old = await InsertInvoiceAsync(shop.Id, Now.AddDays(-91));
        var recent = await InsertInvoiceAsync(shop.Id, Now.AddDays(-89));
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ExpireInvoiceImagesJob>().RunAsync(CancellationToken.None);

        (await fixture.ScalarAsync<bool>("SELECT image_deleted_at IS NOT NULL FROM invoices WHERE id = @p0", old)).Should().BeTrue();
        (await fixture.ScalarAsync<bool>("SELECT image_deleted_at IS NULL FROM invoices WHERE id = @p0", recent)).Should().BeTrue();
    }

    [Fact]
    public void Schedule_DailyOnTheMaintenanceQueue()
    {
        ExpireInvoiceImagesJob.Cron.Should().Be("30 1 * * *");
        ExpireInvoiceImagesJob.RecurringJobId.Should().Be("invoice-image-expiry");
        HangfireOptions.MaintenanceQueue.Should().Be("maintenance");
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(fixture.ConnectionString);
        services.AddApplication();
        services.AddStocklyJobs(new ConfigurationBuilder().Build(), fixture.ConnectionString);
        services.AddSingleton<ICurrentTenant>(new NoTenant());
        services.AddSingleton<IClock>(new FakeClock(Now));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private async Task<Guid> InsertInvoiceAsync(Guid shopId, DateTimeOffset createdAt)
    {
        var id = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO invoices (id, shop_id, client_request_id, created_by_user_id, status, image_object_key, image_sha256, created_at) " +
            "VALUES (@p0, @p1, @p2, @p3, 2, 'invoices/test.jpg', repeat('a', 64), @p4)",
            id,
            shopId,
            Guid.CreateVersion7(),
            Catalog.UserId,
            createdAt);
        return id;
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? ShopId => null;
    }
}
