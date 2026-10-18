using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Admin;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>The image retention write (docs/operations/image-lifecycle.md) on a real PostgreSQL.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ImageRetentionPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 1, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = Now.AddDays(-90);

    [Fact]
    public async Task MarkImagesDeleted_OldImagesOfEveryShop_OnceInOneStatement()
    {
        var shopA = await fixture.CreateShopAsync();
        var shopB = await fixture.CreateShopAsync();
        var oldA = await InsertInvoiceAsync(shopA.Id, Cutoff.AddSeconds(-1));
        var oldB = await InsertInvoiceAsync(shopB.Id, Cutoff.AddDays(-30));
        var atCutoff = await InsertInvoiceAsync(shopA.Id, Cutoff);
        var recent = await InsertInvoiceAsync(shopB.Id, Now.AddDays(-1));
        var alreadyMarked = await InsertInvoiceAsync(shopA.Id, Cutoff.AddDays(-200), imageDeletedAt: Cutoff.AddDays(-100));

        await using (var scope = fixture.CreateScope(shopId: null))
        {
            using var counter = new CommandCounter(scope.ServiceProvider.GetRequiredService<StocklyDbContext>());

            await Retention(scope).MarkImagesDeletedAsync(Cutoff, Now, CancellationToken.None);

            counter.Commands.Should().ContainSingle().Which.Should().StartWith("UPDATE");
        }

        (await DeletedAtAsync(oldA)).Should().Be(Now);
        (await DeletedAtAsync(oldB)).Should().Be(Now);
        (await DeletedAtAsync(atCutoff)).Should().BeNull("exactly 90 days old is still kept");
        (await DeletedAtAsync(recent)).Should().BeNull();
        (await DeletedAtAsync(alreadyMarked)).Should().Be(Cutoff.AddDays(-100), "a marked image keeps its first date");

        await using var again = fixture.CreateScope(shopId: null);
        var secondRun = await Retention(again).MarkImagesDeletedAsync(Cutoff, Now.AddDays(1), CancellationToken.None);
        secondRun.Should().Be(0, "the job is idempotent");
    }

    private static IAdminInvoiceImageRetention Retention(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IAdminInvoiceImageRetention>();

    private async Task<Guid> InsertInvoiceAsync(Guid shopId, DateTimeOffset createdAt, DateTimeOffset? imageDeletedAt = null)
    {
        var id = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO invoices (id, shop_id, client_request_id, created_by_user_id, status, image_object_key, image_sha256, created_at, image_deleted_at) " +
            "VALUES (@p0, @p1, @p2, @p3, 2, 'invoices/test.jpg', repeat('a', 64), @p4, @p5)",
            id,
            shopId,
            Guid.CreateVersion7(),
            Catalog.UserId,
            createdAt,
            imageDeletedAt is { } deletedAt ? deletedAt : (object)DBNull.Value);
        return id;
    }

    private async Task<DateTimeOffset?> DeletedAtAsync(Guid invoiceId)
    {
        var seconds = await fixture.ScalarAsync<string>(
            "SELECT coalesce(extract(epoch FROM image_deleted_at)::bigint::text, '') FROM invoices WHERE id = @p0", invoiceId);
        return string.IsNullOrEmpty(seconds)
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(long.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture));
    }
}
