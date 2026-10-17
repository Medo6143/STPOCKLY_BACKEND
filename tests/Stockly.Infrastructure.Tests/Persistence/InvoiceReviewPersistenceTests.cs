using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.UpdateInvoiceItems;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;
using ContractInvoiceStatus = Stockly.Contracts.Invoices.InvoiceStatus;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// I2/I3 projections and the I4 save on a real PostgreSQL (docs/design/backend.md §9): one statement per read, line order,
/// product and unit names, confirmed new-product lines, paging, status filter, item count, tenant isolation, and the
/// delete-then-insert of lines that reuse their line numbers.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class InvoiceReviewPersistenceTests(PostgresFixture fixture) : IAsyncDisposable
{
    private static readonly string Sha = new('e', InvoiceLimits.ImageSha256Length);
    private readonly List<ServiceProvider> _providers = [];

    [Fact]
    public async Task FindDetail_MatchedAndUnmatchedLines_OneStatementWithNamesInLineOrder()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id, "بنادول اكسترا");
        var box = product.AddUnit("علبة", 3m, 40m, null).Value;
        await SaveProductsAsync(shop.Id, product);
        var invoice = NewInvoice(shop.Id);
        invoice.CompleteExtraction(
            new InvoiceHeader("شركة", "INV-3", new DateOnly(2026, 10, 10), 100.00m),
            [
                new InvoiceLineDraft("بنادول", 2m, 40m, new DateOnly(2027, 3, 31), "B1", new InvoiceLineMatch(product.Id, box.Id, 0.90m)),
                new InvoiceLineDraft("مجهول", 1.5m, 10m, null, null),
            ],
            [Domain.Invoices.InvoiceWarning.TotalMismatch],
            "{}",
            null,
            TestTime.T1);
        await SaveAsync(shop.Id, invoice);

        await using var scope = fixture.CreateScope(shop.Id);
        using var counter = new CommandCounter(scope.ServiceProvider.GetRequiredService<StocklyDbContext>());
        var detail = await scope.ServiceProvider.GetRequiredService<IInvoiceQueries>().FindDetailAsync(invoice.Id, CancellationToken.None);

        counter.Commands.Should().HaveCount(1, "the invoice, its lines and their names come in one statement (rule 33)");
        detail.Should().BeEquivalentTo(new InvoiceDetail(
            invoice.Id,
            invoice.ClientRequestId,
            ContractInvoiceStatus.NeedsReview,
            null,
            "شركة",
            "INV-3",
            new DateOnly(2026, 10, 10),
            "100.00",
            "95.00",
            [Contracts.Invoices.InvoiceWarning.TotalMismatch],
            null,
            [
                new InvoiceItemDto(
                    invoice.Items.Single(i => i.LineNo == 1).Id, 1, "بنادول", product.Id, "بنادول اكسترا", box.Id, "علبة",
                    "2.000", "40.00", "80.00", new DateOnly(2027, 3, 31), "B1", "0.90", null),
                new InvoiceItemDto(
                    invoice.Items.Single(i => i.LineNo == 2).Id, 2, "مجهول", null, null, null, null,
                    "1.500", "10.00", "15.00", null, null, null, null),
            ],
            TestTime.T0.UtcDateTime,
            TestTime.T1.UtcDateTime,
            null), options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task FindDetail_ConfirmedNewProductLine_NamesTheCreatedProductAndKeepsNewProduct()
    {
        var shop = await fixture.CreateShopAsync();
        var created = Catalog.NewProduct(shop.Id, "منتج جديد");
        await SaveProductsAsync(shop.Id, created);
        var invoiceId = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO invoices (id, shop_id, client_request_id, created_by_user_id, status, image_object_key, image_sha256, created_at, confirmed_at) " +
            "VALUES (@p0, @p1, @p2, @p3, 3, 'invoices/x.jpg', repeat('a', 64), @p4, @p4)",
            invoiceId, shop.Id, Guid.CreateVersion7(), Catalog.UserId, TestTime.T1);
        await fixture.ExecuteAsync(
            "INSERT INTO invoice_items (id, shop_id, invoice_id, line_no, raw_name, quantity, unit_price, line_total, " +
            "new_product_name, new_product_base_unit_name, new_product_sell_price, created_product_id, confirmed_at) " +
            "VALUES (@p0, @p1, @p2, 1, 'سطر', 2, 5, 10, 'منتج جديد', 'علبة', 7.5, @p3, @p4)",
            Guid.CreateVersion7(), shop.Id, invoiceId, created.Id, TestTime.T1);

        await using var scope = fixture.CreateScope(shop.Id);
        var detail = await scope.ServiceProvider.GetRequiredService<IInvoiceQueries>().FindDetailAsync(invoiceId, CancellationToken.None);

        detail!.Status.Should().Be(ContractInvoiceStatus.Confirmed);
        detail.ConfirmedAt.Should().Be(TestTime.T1.UtcDateTime);
        detail.Items.Single().Should().BeEquivalentTo(new
        {
            ProductId = (Guid?)created.Id,
            ProductName = "منتج جديد",
            ProductUnitId = (Guid?)null,
            NewProduct = new InvoiceNewProductDto("منتج جديد", "علبة", "7.50"),
        });
    }

    [Fact]
    public async Task FindDetail_OtherShop_Null()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var invoice = NewInvoice(shop.Id);
        await SaveAsync(shop.Id, invoice);

        await using var scope = fixture.CreateScope(other.Id);
        var detail = await scope.ServiceProvider.GetRequiredService<IInvoiceQueries>().FindDetailAsync(invoice.Id, CancellationToken.None);

        detail.Should().BeNull("another shop's invoice is invisible (rule 10)");
    }

    [Fact]
    public async Task List_PagesNewestFirstWithItemCountsInTwoStatementsAndOnlyThisShop()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var oldest = NewInvoice(shop.Id, TestTime.T0);
        var middle = NewInvoice(shop.Id, TestTime.T1);
        middle.CompleteExtraction(InvoiceHeader.Empty, [Line("أ"), Line("ب")], [], null, null, TestTime.T1);
        var newest = NewInvoice(shop.Id, TestTime.T2);
        await SaveAsync(shop.Id, oldest, middle, newest);
        await SaveAsync(other.Id, NewInvoice(other.Id, TestTime.T2));

        await using var scope = fixture.CreateScope(shop.Id);
        var queries = scope.ServiceProvider.GetRequiredService<IInvoiceQueries>();
        using var counter = new CommandCounter(scope.ServiceProvider.GetRequiredService<StocklyDbContext>());
        var first = await queries.ListAsync(null, 1, 2, CancellationToken.None);
        var commands = counter.Commands.Count;
        var second = await queries.ListAsync(null, 2, 2, CancellationToken.None);

        commands.Should().Be(2, "a count and one page query with the item count inside it (no N+1)");
        first.TotalCount.Should().Be(3);
        first.Items.Select(item => (item.Id, item.ItemCount)).Should().Equal((newest.Id, 0), (middle.Id, 2));
        second.Items.Select(item => item.Id).Should().Equal(oldest.Id);
        (first.Page, first.PageSize, second.Page).Should().Be((1, 2, 2));
    }

    [Fact]
    public async Task List_StatusFilter_OnlyThatStatus()
    {
        var shop = await fixture.CreateShopAsync();
        var processing = NewInvoice(shop.Id, TestTime.T0);
        var review = NewInvoice(shop.Id, TestTime.T1);
        review.CompleteExtraction(InvoiceHeader.Empty, [Line("أ")], [], null, null, TestTime.T1);
        await SaveAsync(shop.Id, processing, review);

        await using var scope = fixture.CreateScope(shop.Id);
        var page = await scope.ServiceProvider.GetRequiredService<IInvoiceQueries>()
            .ListAsync(DomainInvoiceStatus.NeedsReview, 1, 20, CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { review.Id, Status = ContractInvoiceStatus.NeedsReview, ItemCount = 1 });
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateItems_TwiceWithTheSameLineNumbers_ReplacesRowsInPlaceWithoutStockChanges()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id, "بنادول اكسترا");
        await SaveProductsAsync(shop.Id, product);
        var invoice = NewInvoice(shop.Id);
        invoice.CompleteExtraction(
            InvoiceHeader.Empty,
            [new InvoiceLineDraft("بنادول", 1m, 1m, null, null, new InvoiceLineMatch(product.Id, product.BaseUnit.Id, 0.90m)), Line("مجهول")],
            [],
            null,
            null,
            TestTime.T1);
        await SaveAsync(shop.Id, invoice);
        InvoiceLineRequest[] lines =
        [
            new(1, "بنادول", "3", "12.50", product.Id, product.BaseUnit.Id),
            new(2, "مجهول", "1", "5.00", NewProduct: new InvoiceNewProductDto("صنف جديد", "علبة", "8.00")),
        ];

        var first = await SendAsync(shop.Id, new UpdateInvoiceItemsCommand(invoice.Id, lines, "مورد", "INV-9", null, "42.50"));
        var second = await SendAsync(shop.Id, new UpdateInvoiceItemsCommand(invoice.Id, lines[..1], null, null, null, null));

        first.Value.Items.Select(item => (item.LineNo, item.ProductName, item.MatchConfidence, item.NewProduct?.Name))
            .Should().Equal((1, "بنادول اكسترا", "0.90", null), (2, null, null, "صنف جديد"));
        first.Value.ComputedTotal.Should().Be("42.50");
        first.Value.Warnings.Should().BeEmpty();
        second.Value.Items.Should().ContainSingle();
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM invoice_items WHERE invoice_id = @p0", invoice.Id)).Should().Be(1);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM stock_movements WHERE shop_id = @p0", shop.Id)).Should().Be(0, "rule 19");
        (await fixture.ScalarAsync<short>("SELECT status FROM invoices WHERE id = @p0", invoice.Id)).Should().Be((short)DomainInvoiceStatus.NeedsReview);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }

    private async Task<Domain.Common.Result<InvoiceDetail>> SendAsync(Guid shopId, UpdateInvoiceItemsCommand command)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(fixture.ConnectionString);
        services.AddApplication();
        services.AddSingleton<ICurrentTenant>(new Tenant(shopId));
        var provider = services.BuildServiceProvider(validateScopes: true);
        _providers.Add(provider);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private static Invoice NewInvoice(Guid shopId, DateTimeOffset? now = null) =>
        Invoice.CreateForScan(shopId, Guid.CreateVersion7(), Catalog.UserId, Sha, now ?? TestTime.T0, id => $"invoices/{shopId}/{id}.jpg").Value;

    private static InvoiceLineDraft Line(string name) => new(name, 1m, 1m, null, null);

    private async Task SaveAsync(Guid shopId, params Invoice[] invoices)
    {
        await using var context = fixture.CreateContext(shopId);
        context.AddRange(invoices);
        await context.SaveChangesAsync();
    }

    private async Task SaveProductsAsync(Guid shopId, params Domain.Products.Product[] products)
    {
        await using var context = fixture.CreateContext(shopId);
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    private sealed class Tenant(Guid shopId) : ICurrentTenant
    {
        public Guid? ShopId => shopId;
    }
}
