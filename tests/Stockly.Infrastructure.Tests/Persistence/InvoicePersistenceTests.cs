using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Contracts.Invoices;
using InvoiceWarning = Stockly.Domain.Invoices.InvoiceWarning;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Domain.Shops;
using Stockly.Domain.Suppliers;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>Invoice repository, queries and constraints on a real PostgreSQL (docs/erd.md §3.3, §3.6).</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class InvoicePersistenceTests(PostgresFixture fixture)
{
    private static readonly string Sha = new('b', InvoiceLimits.ImageSha256Length);

    [Fact]
    public async Task FindListItemByClientRequestId_ExtractedInvoice_ProjectsTheContractRow()
    {
        var shop = await fixture.CreateShopAsync();
        var invoice = NewInvoice(shop.Id);
        invoice.CompleteExtraction(
            new InvoiceHeader("شركة", "INV-7", new DateOnly(2026, 10, 10), 30.00m),
            [Line("أ", 1m, 10m), Line("ب", 2m, 10m)],
            [InvoiceWarning.TotalMismatch, InvoiceWarning.UnreadableLines],
            "{\"ok\":true}",
            null,
            TestTime.T1);
        await SaveAsync(shop.Id, invoice);

        await using var scope = fixture.CreateScope(shop.Id);
        var item = await scope.ServiceProvider.GetRequiredService<IInvoiceQueries>()
            .FindListItemByClientRequestIdAsync(invoice.ClientRequestId, CancellationToken.None);

        item.Should().BeEquivalentTo(new InvoiceListItem(
            invoice.Id,
            Contracts.Invoices.InvoiceStatus.NeedsReview,
            "شركة",
            "INV-7",
            new DateOnly(2026, 10, 10),
            "30.00",
            2,
            [Contracts.Invoices.InvoiceWarning.TotalMismatch, Contracts.Invoices.InvoiceWarning.UnreadableLines],
            TestTime.T0.UtcDateTime));
        (await fixture.ScalarAsync<string>("SELECT warnings::text FROM invoices WHERE id = @p0", invoice.Id))
            .Should().Be("[\"TotalMismatch\", \"UnreadableLines\"]", "warnings are a jsonb array of names (contract §6)");
    }

    [Fact]
    public async Task FindListItemByClientRequestId_OtherShop_Null()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var invoice = NewInvoice(shop.Id);
        await SaveAsync(shop.Id, invoice);

        await using var scope = fixture.CreateScope(other.Id);
        var item = await scope.ServiceProvider.GetRequiredService<IInvoiceQueries>()
            .FindListItemByClientRequestIdAsync(invoice.ClientRequestId, CancellationToken.None);

        item.Should().BeNull("another shop's invoice is invisible (ADR-0002)");
    }

    [Fact]
    public async Task TrySave_SameClientRequestIdInShop_DuplicateClientRequest()
    {
        var shop = await fixture.CreateShopAsync();
        var first = NewInvoice(shop.Id);
        await SaveAsync(shop.Id, first);

        await using var scope = fixture.CreateScope(shop.Id);
        scope.ServiceProvider.GetRequiredService<IInvoiceRepository>().Add(NewInvoice(shop.Id, first.ClientRequestId));
        var saved = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().TrySaveChangesAsync(CancellationToken.None);

        saved.Error.Should().Be(InvoiceErrors.DuplicateClientRequest);
    }

    [Fact]
    public async Task TrySave_SameClientRequestIdInAnotherShop_Allowed()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var first = NewInvoice(shop.Id);
        await SaveAsync(shop.Id, first);

        var act = () => SaveAsync(other.Id, NewInvoice(other.Id, first.ClientRequestId));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FindForUpdate_InsideTransaction_ReturnsTrackedInvoiceWithItems()
    {
        var shop = await fixture.CreateShopAsync();
        var invoice = NewInvoice(shop.Id);
        invoice.CompleteExtraction(InvoiceHeader.Empty, [Line("أ", 1m, 1m)], [], null, null, TestTime.T1);
        await SaveAsync(shop.Id, invoice);

        await using var scope = fixture.CreateScope(shop.Id);
        var repository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var loaded = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            token => repository.FindForUpdateAsync(invoice.Id, token), _ => true, CancellationToken.None);
        var outside = () => repository.FindForUpdateAsync(invoice.Id, CancellationToken.None);

        loaded!.Items.Should().ContainSingle().Which.RawName.Should().Be("أ");
        await outside.Should().ThrowAsync<InvalidOperationException>("a row lock outside a transaction is released at once");
    }

    [Fact]
    public async Task FindForUpdate_OtherShop_Null()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var invoice = NewInvoice(shop.Id);
        await SaveAsync(shop.Id, invoice);

        await using var scope = fixture.CreateScope(other.Id);
        var repository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var loaded = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            token => repository.FindForUpdateAsync(invoice.Id, token), _ => true, CancellationToken.None);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task FindReusableExtraction_NewestExtractedSamePhoto_WithItsModel()
    {
        var shop = await fixture.CreateShopAsync();
        var sha = UniqueSha();
        var failed = NewInvoice(shop.Id, sha: sha);
        failed.MarkFailed(InvoiceFailureReason.AiOutputInvalid, "\"bad\"", TestTime.T1);
        var extracted = NewInvoice(shop.Id, sha: sha);
        extracted.CompleteExtraction(InvoiceHeader.Empty, [Line("أ", 1m, 1m)], [], "{\"v\":1}", null, TestTime.T1);
        var current = NewInvoice(shop.Id, sha: sha);
        await SaveAsync(shop.Id, failed, extracted, current);
        await SaveUsageAsync(shop.Id, extracted.Id, "openai/gpt-4.1-mini", AiCallOutcome.Succeeded);
        await SaveUsageAsync(shop.Id, extracted.Id, "google/gemini-2.5-flash", AiCallOutcome.InvalidOutput);

        await using var scope = fixture.CreateScope(shop.Id);
        var reusable = await scope.ServiceProvider.GetRequiredService<IInvoiceRepository>()
            .FindReusableExtractionAsync(sha, current.Id, CancellationToken.None);

        reusable.Should().Be(new ReusableExtraction("{\"v\": 1}", "openai/gpt-4.1-mini"));
    }

    [Fact]
    public async Task FindReusableExtraction_OnlyItselfOrOtherShops_Null()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        var sha = UniqueSha();
        var otherShops = NewInvoice(other.Id, sha: sha);
        otherShops.CompleteExtraction(InvoiceHeader.Empty, [Line("أ", 1m, 1m)], [], "{}", null, TestTime.T1);
        await SaveAsync(other.Id, otherShops);
        var current = NewInvoice(shop.Id, sha: sha);
        await SaveAsync(shop.Id, current);

        await using var scope = fixture.CreateScope(shop.Id);
        var reusable = await scope.ServiceProvider.GetRequiredService<IInvoiceRepository>()
            .FindReusableExtractionAsync(sha, current.Id, CancellationToken.None);

        reusable.Should().BeNull();
    }

    [Fact]
    public async Task CountCreatedSince_CountsOnlyThisShopFromTheInstant()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        await SaveAsync(shop.Id, NewInvoice(shop.Id, now: TestTime.T0), NewInvoice(shop.Id, now: TestTime.T1), NewInvoice(shop.Id, now: TestTime.T2));
        await SaveAsync(other.Id, NewInvoice(other.Id, now: TestTime.T2));

        await using var scope = fixture.CreateScope(shop.Id);
        var count = await scope.ServiceProvider.GetRequiredService<IInvoiceRepository>().CountCreatedSinceAsync(TestTime.T1, CancellationToken.None);

        count.Should().Be(2);
    }

    [Fact]
    public async Task ExistsOtherWithNumber_MatchesSameNumberAndSupplierButNotFailedOrItself()
    {
        var shop = await fixture.CreateShopAsync();
        var supplier = Supplier.Create(shop.Id, "شركة الأدوية", null, TestTime.T0).Value;
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Add(supplier);
            await context.SaveChangesAsync();
        }

        var withSupplier = NewInvoice(shop.Id);
        withSupplier.CompleteExtraction(new InvoiceHeader("شركة الأدوية", "INV-1", null, null), [Line("أ", 1m, 1m)], [], null, supplier.Id, TestTime.T1);
        var unknownSupplier = NewInvoice(shop.Id);
        unknownSupplier.CompleteExtraction(new InvoiceHeader(null, "INV-2", null, null), [Line("أ", 1m, 1m)], [], null, null, TestTime.T1);
        var failed = NewInvoice(shop.Id);
        failed.MarkFailed(InvoiceFailureReason.AiNoItems, null, TestTime.T1);
        await SaveAsync(shop.Id, withSupplier, unknownSupplier, failed);

        await using var scope = fixture.CreateScope(shop.Id);
        var repository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var current = Guid.CreateVersion7();

        (await repository.ExistsOtherWithNumberAsync(current, supplier.Id, "INV-1", CancellationToken.None)).Should().BeTrue();
        (await repository.ExistsOtherWithNumberAsync(current, null, "INV-1", CancellationToken.None)).Should().BeFalse("another supplier");
        (await repository.ExistsOtherWithNumberAsync(current, null, "INV-2", CancellationToken.None)).Should().BeTrue("unknown matches unknown");
        (await repository.ExistsOtherWithNumberAsync(withSupplier.Id, supplier.Id, "INV-1", CancellationToken.None)).Should().BeFalse("itself");
    }

    [Fact]
    public async Task SupplierRepository_FindsNonArchivedByNormalizedName()
    {
        var shop = await fixture.CreateShopAsync();
        var supplier = Supplier.Create(shop.Id, "شركة الأدوية المتحدة", null, TestTime.T0).Value;
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Add(supplier);
            await context.SaveChangesAsync();
        }

        await using var scope = fixture.CreateScope(shop.Id);
        var suppliers = scope.ServiceProvider.GetRequiredService<ISupplierRepository>();

        (await suppliers.FindIdByNormalizedNameAsync("شركه الادويه المتحده", CancellationToken.None)).Should().Be(supplier.Id);
        await fixture.ExecuteAsync("UPDATE suppliers SET is_archived = true WHERE id = @p0", supplier.Id);
        (await suppliers.FindIdByNormalizedNameAsync("شركه الادويه المتحده", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ShopPlanQueries_ActiveSubscription_ReturnsThePlansDailyQuota()
    {
        var shop = await fixture.CreateShopAsync();
        var noSubscription = await fixture.CreateShopAsync();
        await fixture.ExecuteAsync(
            "INSERT INTO shop_subscriptions (id, shop_id, plan_id, status, starts_on, created_at) VALUES (@p0, @p1, @p2, 1, '2026-10-01', now())",
            Guid.CreateVersion7(),
            shop.Id,
            DefaultPlan.Id);

        await using var scope = fixture.CreateScope(shop.Id);
        await using var otherScope = fixture.CreateScope(noSubscription.Id);

        (await scope.ServiceProvider.GetRequiredService<IShopPlanQueries>().GetDailyScanQuotaAsync(CancellationToken.None))
            .Should().Be(DefaultPlan.DailyScanQuota);
        (await otherScope.ServiceProvider.GetRequiredService<IShopPlanQueries>().GetDailyScanQuotaAsync(CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task AiUsageRecord_RoundTripsCostWithEightDecimals()
    {
        var shop = await fixture.CreateShopAsync();
        var record = AiUsageRecord.Create(shop.Id, null, "m", 10, 5, 0.12345678m, 900, AiCallOutcome.RateLimited, false, TestTime.T0);
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Add(record);
            await context.SaveChangesAsync();
        }

        await using var reader = fixture.CreateContext(shop.Id);
        var loaded = await reader.Set<AiUsageRecord>().AsNoTracking().SingleAsync(r => r.Id == record.Id);

        loaded.Should().BeEquivalentTo(record);
    }

    [Theory]
    [InlineData("UPDATE invoice_items SET product_id = @p1 WHERE id = @p0", "ck_invoice_items_unit_requires_product")]
    [InlineData("UPDATE invoice_items SET line_no = 101 WHERE id = @p0", "ck_invoice_items_line_no")]
    [InlineData("UPDATE invoice_items SET confirmed_at = now() WHERE id = @p0", "ck_invoice_items_confirmed_resolved")]
    [InlineData("UPDATE invoice_items SET new_product_name = 'x' WHERE id = @p0", "ck_invoice_items_new_product_complete")]
    [InlineData("UPDATE invoice_items SET match_confidence = 1.5 WHERE id = @p0", "ck_invoice_items_match_confidence")]
    [InlineData("UPDATE invoice_items SET quantity = 0 WHERE id = @p0", "ck_invoice_items_amounts")]
    [InlineData("UPDATE invoice_items SET created_product_id = @p1 WHERE id = @p0", "ck_invoice_items_created_product")]
    public async Task InvoiceItems_RowBreakingAnInvariant_Rejected(string sql, string constraint)
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        var (_, itemId) = await fixture.CreateInvoiceLineAsync(shop.Id);

        var act = () => fixture.ExecuteAsync(sql, itemId, product.Id);

        var error = (await act.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task Invoices_ConfirmedAtWhileNotConfirmed_Rejected()
    {
        var shop = await fixture.CreateShopAsync();
        var (invoiceId, _) = await fixture.CreateInvoiceLineAsync(shop.Id);

        var act = () => fixture.ExecuteAsync("UPDATE invoices SET confirmed_at = now() WHERE id = @p0", invoiceId);

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_invoices_confirmed_at");
    }

    [Fact]
    public async Task StockMovements_ReceiptForAnUnknownInvoiceLine_RejectedByForeignKey()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        var act = () => fixture.ExecuteAsync(
            "INSERT INTO stock_movements (id, shop_id, product_id, quantity_change, balance_after, reason, invoice_id, invoice_item_id, created_by_user_id, created_at) " +
            "VALUES (@p0, @p1, @p2, 1, 1, 1, @p3, @p4, @p5, now())",
            Guid.CreateVersion7(),
            shop.Id,
            product.Id,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Catalog.UserId);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    private static Invoice NewInvoice(Guid shopId, Guid? clientRequestId = null, string? sha = null, DateTimeOffset? now = null) =>
        Invoice.CreateForScan(
            shopId, clientRequestId ?? Guid.CreateVersion7(), Catalog.UserId, sha ?? Sha, now ?? TestTime.T0, id => $"invoices/{shopId}/{id}.jpg").Value;

    private static InvoiceLineDraft Line(string name, decimal quantity, decimal unitPrice) => new(name, quantity, unitPrice, null, null);

    private static string UniqueSha() => Convert.ToHexStringLower(Guid.CreateVersion7().ToByteArray()) + new string('c', 32);

    private async Task SaveAsync(Guid shopId, params Invoice[] invoices)
    {
        await using var context = fixture.CreateContext(shopId);
        context.AddRange(invoices);
        await context.SaveChangesAsync();
    }

    private async Task SaveUsageAsync(Guid shopId, Guid invoiceId, string model, AiCallOutcome outcome)
    {
        await using var context = fixture.CreateContext(shopId);
        context.Add(AiUsageRecord.Create(shopId, invoiceId, model, 1, 1, 0m, 1, outcome, false, TestTime.T2));
        await context.SaveChangesAsync();
    }
}
