using System.Net;
using Npgsql;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Products;

/// <summary>
/// A barcode taken by a concurrent transaction after the handler's duplicate check still returns the contract's
/// <c>409</c> (P3, P6), and the losing request leaves nothing behind. The competing transaction is held open with raw
/// SQL so the API request passes its check, blocks on the unique index, and loses when the competitor commits.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class BarcodeRaceEndpointTests(ApiFixture fixture)
{
    private static readonly TimeSpan BlockedFor = TimeSpan.FromMilliseconds(700);

    [Fact]
    public async Task CreateProduct_BarcodeCommittedConcurrently_Returns409ProductDuplicateBarcode()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        await using var competitor = await BeginAsync();
        await ExecuteAsync(
            competitor,
            "INSERT INTO products (id, shop_id, name, normalized_name, barcode, quantity, cost_price, low_stock_threshold, is_archived, created_at, updated_at) " +
            "VALUES (@p0, @p1, 'competitor', 'competitor', 'RACE-P', 0, 1, 0, false, now(), now())",
            Guid.CreateVersion7(),
            owner.ShopId);

        var request = ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(barcode: "RACE-P", openingQuantity: "5"), owner.AccessToken);
        await Task.Delay(BlockedFor);
        request.IsCompleted.Should().BeFalse("the request passed its check and waits on the unique index");
        await competitor.Transaction.CommitAsync();
        var response = await request;

        response.Status.Should().Be(HttpStatusCode.Conflict, response.Raw);
        response.Code.Should().Be("Product.DuplicateBarcode");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM products WHERE shop_id = @p0", owner.ShopId)).Should().Be(1, "the losing product rolled back");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM stock_movements WHERE shop_id = @p0", owner.ShopId)).Should().Be(0, "its opening stock rolled back with it");
    }

    [Fact]
    public async Task AddUnit_BarcodeCommittedConcurrently_Returns409ProductUnitDuplicateBarcode()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var first = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "first"));
        var second = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "second"));

        await using var competitor = await BeginAsync();
        await ExecuteAsync(
            competitor,
            "INSERT INTO product_units (id, shop_id, product_id, name, factor, sell_price, is_base, barcode) " +
            "VALUES (@p0, @p1, @p2, 'competitor box', 3, 40, false, 'RACE-U')",
            Guid.CreateVersion7(),
            owner.ShopId,
            first);

        var request = ProductApi.PostAsync(
            client, ProductApi.Units(second), new { name = "علبة", factor = "3", sellPrice = "40", barcode = "RACE-U" }, owner.AccessToken);
        await Task.Delay(BlockedFor);
        request.IsCompleted.Should().BeFalse("the request passed its check and waits on the unique index");
        await competitor.Transaction.CommitAsync();
        var response = await request;

        response.Status.Should().Be(HttpStatusCode.Conflict, response.Raw);
        response.Code.Should().Be("ProductUnit.DuplicateBarcode");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM product_units WHERE product_id = @p0", second)).Should().Be(1, "only the base unit");
    }

    [Fact]
    public async Task CreateProduct_TwoParallelRequestsWithTheSameNewBarcode_OneCreatedOneConflict()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var responses = await Task.WhenAll(
            ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(name: "a", barcode: "PAR-P", openingQuantity: "2"), owner.AccessToken),
            ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(name: "b", barcode: "PAR-P", openingQuantity: "2"), owner.AccessToken));

        responses.Select(response => response.Status).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        responses.Single(response => response.Status == HttpStatusCode.Conflict).Code.Should().Be("Product.DuplicateBarcode");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM products WHERE shop_id = @p0 AND barcode = 'PAR-P'", owner.ShopId)).Should().Be(1);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM products WHERE shop_id = @p0", owner.ShopId)).Should().Be(1, "no partial product");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM stock_movements WHERE shop_id = @p0", owner.ShopId)).Should().Be(1, "only the winner's opening stock");
    }

    [Fact]
    public async Task AddUnit_TwoParallelRequestsWithTheSameNewBarcode_OneCreatedOneConflict()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var first = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "first"));
        var second = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "second"));
        var unit = new { name = "علبة", factor = "3", sellPrice = "40", barcode = "PAR-U" };

        var responses = await Task.WhenAll(
            ProductApi.PostAsync(client, ProductApi.Units(first), unit, owner.AccessToken),
            ProductApi.PostAsync(client, ProductApi.Units(second), unit, owner.AccessToken));

        responses.Select(response => response.Status).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        responses.Single(response => response.Status == HttpStatusCode.Conflict).Code.Should().Be("ProductUnit.DuplicateBarcode");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM product_units WHERE shop_id = @p0 AND barcode = 'PAR-U'", owner.ShopId)).Should().Be(1);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM product_units WHERE shop_id = @p0", owner.ShopId)).Should().Be(3, "two base units and the winner's unit");
    }

    [Fact]
    public async Task OtherUniqueViolation_IsNotMappedTo409_ButSurfacesAsServerUnexpected()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);

        // ux_product_units_product_id_name compares raw names; the Domain compares normalized names and loads the
        // product's units first, so only a competing transaction can hit it. Hold one open with the exact name.
        await using var competitor = await BeginAsync();
        await ExecuteAsync(
            competitor,
            "INSERT INTO product_units (id, shop_id, product_id, name, factor, sell_price, is_base) VALUES (@p0, @p1, @p2, 'علبة', 3, 40, false)",
            Guid.CreateVersion7(),
            owner.ShopId,
            id);

        var request = ProductApi.PostAsync(client, ProductApi.Units(id), new { name = "علبة", factor = "3", sellPrice = "40" }, owner.AccessToken);
        await Task.Delay(BlockedFor);
        await competitor.Transaction.CommitAsync();
        var response = await request;

        response.Status.Should().Be(HttpStatusCode.InternalServerError, response.Raw);
        response.Code.Should().Be("Server.Unexpected");
        response.Raw.Should().NotContain("ux_product_units_product_id_name", "no exception details leak");
    }

    private async Task<OpenTransaction> BeginAsync()
    {
        var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        return new OpenTransaction(connection, await connection.BeginTransactionAsync());
    }

    private static async Task ExecuteAsync(OpenTransaction open, string sql, params object[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, open.Connection, open.Transaction);
        foreach (var (value, index) in parameters.Select((value, index) => (value, index)))
        {
            command.Parameters.AddWithValue($"p{index}", value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private sealed record OpenTransaction(NpgsqlConnection Connection, NpgsqlTransaction Transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
