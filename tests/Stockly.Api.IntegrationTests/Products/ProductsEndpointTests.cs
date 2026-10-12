using System.Net;
using StackExchange.Redis;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Products;

/// <summary>P1–P5 on the real Api (PostgreSQL and Redis in containers), plus the catalog cache.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class ProductsEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndDecimalStrings()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var response = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(barcode: " 6221234567890 "), owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        var body = response.Body!;
        var id = body["id"]!.GetValue<Guid>();
        response.Location!.ToString().Should().Be($"/api/products/{id}");
        body["name"]!.GetValue<string>().Should().Be("بنادول اكسترا");
        body["barcode"]!.GetValue<string>().Should().Be("6221234567890");
        body["category"]!.GetValue<string>().Should().Be("مسكنات");
        body["quantity"]!.GetValue<string>().Should().Be("0.000");
        body["costPrice"]!.GetValue<string>().Should().Be("12.50");
        body["lowStockThreshold"]!.GetValue<string>().Should().Be("5.000");
        body["isLowStock"]!.GetValue<bool>().Should().BeTrue();
        body["isArchived"]!.GetValue<bool>().Should().BeFalse();
        body["units"]!.AsArray().Should().ContainSingle();
        body["units"]![0]!["factor"]!.GetValue<string>().Should().Be("1.000000");
        body["units"]![0]!["sellPrice"]!.GetValue<string>().Should().Be("15.00");
        body["units"]![0]!["isBase"]!.GetValue<bool>().Should().BeTrue();
        body["createdAt"]!.GetValue<string>().Should().EndWith("Z");
        body["updatedAt"]!.GetValue<string>().Should().EndWith("Z");
    }

    [Fact]
    public async Task Create_OwnerWithOpeningQuantity_RecordsOpeningStockCount()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var id = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(openingQuantity: "24.000"));

        var detail = await ProductApi.GetAsync(client, ProductApi.Product(id), owner.AccessToken);
        detail.Body!["quantity"]!.GetValue<string>().Should().Be("24.000");
        var movements = await ProductApi.GetAsync(client, ProductApi.Movements(id), owner.AccessToken);
        var movement = movements.Body!["items"]!.AsArray().Single()!;
        movement["reason"]!.GetValue<string>().Should().Be("StockCount");
        movement["note"]!.GetValue<string>().Should().Be("Opening stock");
        movement["quantityChange"]!.GetValue<string>().Should().Be("24.000");
        movement["createdByUserId"]!.GetValue<Guid>().Should().Be(owner.UserId);
    }

    [Fact]
    public async Task Create_ZeroOpeningQuantity_RecordsNoMovement()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var id = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(openingQuantity: "0.000"));

        var movements = await ProductApi.GetAsync(client, ProductApi.Movements(id), owner.AccessToken);
        movements.Body!["totalCount"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task Create_StaffWithOpeningQuantity_Returns403AndCreatesNothing()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var staff = TestJwt.Mint(owner.UserId, owner.ShopId, "Staff");

        var response = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(openingQuantity: "24.000"), staff);
        var withoutOpening = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(name: "staff product"), staff);

        response.Status.Should().Be(HttpStatusCode.Forbidden);
        response.Code.Should().Be("Product.OpeningStockOwnerOnly");
        withoutOpening.Status.Should().Be(HttpStatusCode.Created, "Staff may create products");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM products WHERE shop_id = @p0", owner.ShopId)).Should().Be(1);
    }

    [Theory]
    [InlineData("12.505", "Precision.TooManyDecimals")]
    [InlineData("-1.00", "Precision.Negative")]
    [InlineData("10000000000000000", "Precision.OutOfRange")]
    public async Task Create_InvalidMoney_Returns400WithPrecisionCode(string costPrice, string code)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var response = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(costPrice: costPrice), owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be(code);
    }

    [Fact]
    public async Task Create_JsonNumberForMoney_Returns400ValidationFailed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var response = await ProductApi.PostRawAsync(
            client,
            ProductApi.Products,
            """{ "name": "x", "baseUnitName": "u", "costPrice": 12.5, "baseUnitSellPrice": "15.00" }""",
            owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!["costPrice"].Should().NotBeNull();
    }

    [Fact]
    public async Task Create_MalformedDecimalAndMissingFields_Returns400ValidationFailedPerField()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var response = await ProductApi.PostAsync(client, ProductApi.Products, new { name = "x", costPrice = "12,50" }, owner.AccessToken, language: "en");

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!.AsObject().Select(pair => pair.Key)
            .Should().BeEquivalentTo("baseUnitName", "costPrice", "baseUnitSellPrice");
        response.Body!["errors"]!["costPrice"]![0]!.GetValue<string>().Should().Contain("decimal");
    }

    [Fact]
    public async Task Create_BlankName_Returns400ProductNameRequired()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var response = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(name: " ـ "), owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Product.NameRequired");
    }

    [Fact]
    public async Task Create_BarcodeOfAnotherActiveProduct_Returns409_ArchivedOneFreesIt()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var first = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(barcode: "111"));

        var duplicate = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(name: "other", barcode: "111"), owner.AccessToken);
        await ProductApi.PostAsync(client, ProductApi.Archive(first), new { }, owner.AccessToken);
        var afterArchive = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(name: "other", barcode: "111"), owner.AccessToken);

        duplicate.Status.Should().Be(HttpStatusCode.Conflict);
        duplicate.Code.Should().Be("Product.DuplicateBarcode");
        afterArchive.Status.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_SameBarcodeInAnotherShop_IsAllowed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shopA = await AuthApi.RegisterAsync(client);
        var shopB = await AuthApi.RegisterAsync(client);

        await ProductApi.CreateAsync(client, shopA.AccessToken, ProductApi.CreateBody(barcode: "555"));
        var response = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(barcode: "555"), shopB.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Get_OtherShopsProduct_Returns404ProductNotFound()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);

        var response = await ProductApi.GetAsync(client, ProductApi.Product(id), stranger.AccessToken, language: "en");

        response.Status.Should().Be(HttpStatusCode.NotFound, "wrong tenant is 404, never 403 (AGENTS.md rule 10)");
        response.Code.Should().Be("Product.NotFound");
        response.Body!["detail"]!.GetValue<string>().Should().Be("The product was not found in this shop.");
        response.Raw.Should().NotContain("بنادول");
    }

    [Fact]
    public async Task Get_NotAGuid_Returns404HttpNotFound()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var response = await ProductApi.GetAsync(client, $"{ProductApi.Products}/not-a-guid", owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.NotFound);
        response.Code.Should().Be("Http.NotFound");
    }

    [Fact]
    public async Task Endpoints_WithoutToken_Return401_SuspendedShop_Returns403()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        await fixture.ExecuteAsync("UPDATE shops SET status = 2 WHERE id = @p0", owner.ShopId);

        var anonymous = await ProductApi.GetAsync(client, ProductApi.Products, token: null);
        var suspended = await ProductApi.GetAsync(client, ProductApi.Products, owner.AccessToken);

        anonymous.Status.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.Code.Should().Be("Auth.Unauthenticated");
        suspended.Status.Should().Be(HttpStatusCode.Forbidden);
        suspended.Code.Should().Be("Shop.Suspended");
    }

    [Fact]
    public async Task Update_ValidRequest_Returns200WithNewDetails()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(barcode: "111"));

        var response = await ProductApi.PutAsync(
            client, ProductApi.Product(id), new { name = "Panadol", costPrice = "9.75", lowStockThreshold = "0", barcode = "111" }, owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.OK, response.Raw);
        response.Body!["name"]!.GetValue<string>().Should().Be("Panadol");
        response.Body!["costPrice"]!.GetValue<string>().Should().Be("9.75");
        response.Body!["lowStockThreshold"]!.GetValue<string>().Should().Be("0.000");
        response.Body!["category"].Should().BeNull("an absent category clears it");
        response.Body!["barcode"]!.GetValue<string>().Should().Be("111", "keeping its own barcode is not a duplicate");
    }

    [Fact]
    public async Task Update_ErrorCases()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);
        await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "other", barcode: "222"));
        var body = new { name = "x", costPrice = "1.00", lowStockThreshold = "0", barcode = "222" };

        var duplicate = await ProductApi.PutAsync(client, ProductApi.Product(id), body, owner.AccessToken);
        var wrongTenant = await ProductApi.PutAsync(client, ProductApi.Product(id), body with { barcode = "999" }, stranger.AccessToken);
        var missing = await ProductApi.PutAsync(client, ProductApi.Product(id), new { name = "x" }, owner.AccessToken);
        await ProductApi.PostAsync(client, ProductApi.Archive(id), new { }, owner.AccessToken);
        var archived = await ProductApi.PutAsync(client, ProductApi.Product(id), body with { barcode = "999" }, owner.AccessToken);

        duplicate.Code.Should().Be("Product.DuplicateBarcode");
        duplicate.Status.Should().Be(HttpStatusCode.Conflict);
        wrongTenant.Status.Should().Be(HttpStatusCode.NotFound);
        wrongTenant.Code.Should().Be("Product.NotFound");
        missing.Status.Should().Be(HttpStatusCode.BadRequest);
        missing.Code.Should().Be("Validation.Failed");
        archived.Status.Should().Be(HttpStatusCode.Conflict);
        archived.Code.Should().Be("Product.Archived");
    }

    [Fact]
    public async Task Archive_Owner_Returns204Idempotently_Staff403_OtherShop404()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var staff = TestJwt.Mint(owner.UserId, owner.ShopId, "Staff");
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);

        var asStaff = await ProductApi.PostAsync(client, ProductApi.Archive(id), new { }, staff);
        var wrongTenant = await ProductApi.PostAsync(client, ProductApi.Archive(id), new { }, stranger.AccessToken);
        var first = await ProductApi.PostAsync(client, ProductApi.Archive(id), new { }, owner.AccessToken);
        var again = await ProductApi.PostAsync(client, ProductApi.Archive(id), new { }, owner.AccessToken);

        asStaff.Status.Should().Be(HttpStatusCode.Forbidden);
        asStaff.Code.Should().Be("Auth.Forbidden");
        wrongTenant.Status.Should().Be(HttpStatusCode.NotFound);
        first.Status.Should().Be(HttpStatusCode.NoContent);
        again.Status.Should().Be(HttpStatusCode.NoContent);
        (await ProductApi.GetAsync(client, ProductApi.Product(id), owner.AccessToken)).Body!["isArchived"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task List_SortedSearchableFilterableAndScopedToTheShop()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "كونجستال", openingQuantity: "50"));
        await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "بنادول إكسترا"));
        var archived = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "أسبرين"));
        await ProductApi.PostAsync(client, ProductApi.Archive(archived), new { }, owner.AccessToken);
        await ProductApi.CreateAsync(client, stranger.AccessToken, ProductApi.CreateBody(name: "بنادول الغريب"));

        var all = await ProductApi.GetAsync(client, ProductApi.Products, owner.AccessToken);
        var search = await ProductApi.GetAsync(client, $"{ProductApi.Products}?search={Uri.EscapeDataString("اكسترا")}", owner.AccessToken);
        var lowStock = await ProductApi.GetAsync(client, $"{ProductApi.Products}?lowStock=true", owner.AccessToken);
        var withArchived = await ProductApi.GetAsync(client, $"{ProductApi.Products}?includeArchived=true&pageSize=1&page=2", owner.AccessToken);

        Names(all).Should().Equal("بنادول إكسترا", "كونجستال");
        all.Body!["page"]!.GetValue<int>().Should().Be(1);
        all.Body!["pageSize"]!.GetValue<int>().Should().Be(20);
        all.Body!["totalCount"]!.GetValue<int>().Should().Be(2);
        all.Body!["items"]![1]!["quantity"]!.GetValue<string>().Should().Be("50.000");
        all.Body!["items"]![1]!["baseUnit"]!["sellPrice"]!.GetValue<string>().Should().Be("15.00");
        Names(search).Should().Equal("بنادول إكسترا");
        Names(lowStock).Should().Equal("بنادول إكسترا");
        Names(withArchived).Should().ContainSingle()
            .Which.Should().Be("بنادول إكسترا", "page 2 of 1 sorted by normalized name: اسبرين, بنادول, كونجستال");
        withArchived.Body!["totalCount"]!.GetValue<int>().Should().Be(3);
    }

    [Theory]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=0")]
    [InlineData("page=0")]
    [InlineData("lowStock=maybe")]
    [InlineData("page=abc")]
    public async Task List_InvalidQuery_Returns400ValidationFailed(string query)
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var response = await ProductApi.GetAsync(client, $"{ProductApi.Products}?{query}", owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Code.Should().Be("Validation.Failed");
    }

    [Fact]
    public async Task List_IsCachedPerCatalogVersion_AndEveryWriteBumpsTheVersionAfterCommit()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
        var versionKey = $"stockly:{owner.ShopId}:ver:catalog";

        var empty = await ProductApi.GetAsync(client, ProductApi.Products, owner.AccessToken);
        var listKeys = await KeysAsync(redis, $"stockly:{owner.ShopId}:products:list:v0:*");
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);
        var afterCreate = await ProductApi.GetAsync(client, ProductApi.Products, owner.AccessToken);
        var invalid = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "0", note = "x" }, owner.AccessToken);
        var versionAfterFailedWrite = (long)await redis.GetDatabase().StringGetAsync(versionKey);
        await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "7", note = "found" }, owner.AccessToken);
        var lowStock = await ProductApi.GetAsync(client, $"{ProductApi.Products}?lowStock=true", owner.AccessToken);
        var afterAdjust = await ProductApi.GetAsync(client, ProductApi.Products, owner.AccessToken);

        Names(empty).Should().BeEmpty();
        listKeys.Should().ContainSingle("the first page was cached under version 0");
        (await redis.GetDatabase().KeyTimeToLiveAsync(listKeys[0])).Should().NotBeNull();
        Names(afterCreate).Should().ContainSingle("the create bumped the version, so the cached empty page is unreachable");
        invalid.Code.Should().Be("Stock.ZeroChange");
        versionAfterFailedWrite.Should().Be(1, "a failed write rolls back and bumps nothing");
        afterAdjust.Body!["items"]![0]!["quantity"]!.GetValue<string>().Should().Be("7.000");
        Names(lowStock).Should().BeEmpty();
        (await KeysAsync(redis, $"stockly:{owner.ShopId}:products:lowstock:v2:*")).Should().ContainSingle();
        ((long)await redis.GetDatabase().StringGetAsync(versionKey)).Should().Be(2);
    }

    [Fact]
    public async Task RedisDown_ReadsAndWritesStillSucceed()
    {
        await using var factory = fixture.CreateFactory(redis: ApiFixture.UnreachableRedis);
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);

        var created = await ProductApi.PostAsync(client, ProductApi.Products, ProductApi.CreateBody(openingQuantity: "3"), owner.AccessToken);
        var id = created.Body!["id"]!.GetValue<Guid>();
        var adjusted = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "1", note = "n" }, owner.AccessToken);
        var list = await ProductApi.GetAsync(client, ProductApi.Products, owner.AccessToken);
        var lowStock = await ProductApi.GetAsync(client, $"{ProductApi.Products}?lowStock=true", owner.AccessToken);

        created.Status.Should().Be(HttpStatusCode.Created);
        adjusted.Status.Should().Be(HttpStatusCode.Created);
        list.Status.Should().Be(HttpStatusCode.OK);
        list.Body!["items"]![0]!["quantity"]!.GetValue<string>().Should().Be("4.000");
        Names(lowStock).Should().ContainSingle();
    }

    private static List<string> Names(ApiResponse response) =>
        response.Body!["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()).ToList();

    private static async Task<List<RedisKey>> KeysAsync(ConnectionMultiplexer redis, string pattern)
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var keys = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            keys.Add(key);
        }

        return keys;
    }
}
