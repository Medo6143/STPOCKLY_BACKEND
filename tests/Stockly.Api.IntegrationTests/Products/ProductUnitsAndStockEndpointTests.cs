using System.Net;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Products;

/// <summary>P6–P11 on the real Api.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class ProductUnitsAndStockEndpointTests(ApiFixture fixture)
{
    private static readonly object Box = new { name = "علبة", factor = "3.000000", sellPrice = "40.00", barcode = (string?)null };

    [Fact]
    public async Task AddUnit_Valid_Returns201WithLocation()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);

        var response = await ProductApi.PostAsync(client, ProductApi.Units(id), Box, owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        var unitId = response.Body!["id"]!.GetValue<Guid>();
        response.Location!.ToString().Should().Be($"/api/products/{id}/units/{unitId}");
        response.Body!["factor"]!.GetValue<string>().Should().Be("3.000000");
        response.Body!["sellPrice"]!.GetValue<string>().Should().Be("40.00");
        response.Body!["isBase"]!.GetValue<bool>().Should().BeFalse();
        var detail = await ProductApi.GetAsync(client, ProductApi.Product(id), owner.AccessToken);
        detail.Body!["units"]!.AsArray().Select(unit => unit!["name"]!.GetValue<string>()).Should().Equal("شريط", "علبة");
    }

    [Fact]
    public async Task AddUnit_ErrorCases()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);
        var other = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(name: "other"));
        await ProductApi.PostAsync(client, ProductApi.Units(other), new { name = "علبة", factor = "2", sellPrice = "1", barcode = "B-1" }, owner.AccessToken);

        var duplicateName = await ProductApi.PostAsync(client, ProductApi.Units(id), new { name = "شَريـط", factor = "2", sellPrice = "1" }, owner.AccessToken);
        var duplicateBarcode = await ProductApi.PostAsync(client, ProductApi.Units(id), new { name = "كرتونة", factor = "30", sellPrice = "1", barcode = "B-1" }, owner.AccessToken);
        var zeroFactor = await ProductApi.PostAsync(client, ProductApi.Units(id), new { name = "x", factor = "0", sellPrice = "1" }, owner.AccessToken);
        var blankName = await ProductApi.PostAsync(client, ProductApi.Units(id), new { name = " ", factor = "2", sellPrice = "1" }, owner.AccessToken);
        var wrongTenant = await ProductApi.PostAsync(client, ProductApi.Units(id), Box, stranger.AccessToken);
        await ProductApi.PostAsync(client, ProductApi.Archive(id), new { }, owner.AccessToken);
        var archived = await ProductApi.PostAsync(client, ProductApi.Units(id), Box, owner.AccessToken);

        Expect(duplicateName, HttpStatusCode.Conflict, "ProductUnit.DuplicateName");
        Expect(duplicateBarcode, HttpStatusCode.Conflict, "ProductUnit.DuplicateBarcode");
        Expect(zeroFactor, HttpStatusCode.BadRequest, "Precision.NotPositive");
        Expect(blankName, HttpStatusCode.BadRequest, "ProductUnit.NameRequired");
        Expect(wrongTenant, HttpStatusCode.NotFound, "Product.NotFound");
        Expect(archived, HttpStatusCode.Conflict, "Product.Archived");
    }

    [Fact]
    public async Task UpdateUnit_ValidAndErrorCases()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);
        var detail = await ProductApi.GetAsync(client, ProductApi.Product(id), owner.AccessToken);
        var baseUnitId = detail.Body!["units"]![0]!["id"]!.GetValue<Guid>();
        var boxId = (await ProductApi.PostAsync(client, ProductApi.Units(id), Box, owner.AccessToken)).Body!["id"]!.GetValue<Guid>();

        var updated = await ProductApi.PutAsync(client, ProductApi.Unit(id, boxId), new { name = "علبة كبيرة", factor = "6", sellPrice = "75.5", barcode = "BX" }, owner.AccessToken);
        var renamedBase = await ProductApi.PutAsync(client, ProductApi.Unit(id, baseUnitId), new { name = "قرص", factor = "1.000000", sellPrice = "16" }, owner.AccessToken);
        var baseFactor = await ProductApi.PutAsync(client, ProductApi.Unit(id, baseUnitId), new { name = "قرص", factor = "2", sellPrice = "16" }, owner.AccessToken);
        var unknownUnit = await ProductApi.PutAsync(client, ProductApi.Unit(id, Guid.CreateVersion7()), Box, owner.AccessToken);

        updated.Status.Should().Be(HttpStatusCode.OK, updated.Raw);
        updated.Body!["factor"]!.GetValue<string>().Should().Be("6.000000");
        updated.Body!["sellPrice"]!.GetValue<string>().Should().Be("75.50");
        updated.Body!["barcode"]!.GetValue<string>().Should().Be("BX");
        renamedBase.Status.Should().Be(HttpStatusCode.OK);
        Expect(baseFactor, HttpStatusCode.BadRequest, "ProductUnit.BaseUnitFactorFixed");
        Expect(unknownUnit, HttpStatusCode.NotFound, "ProductUnit.NotFound");
    }

    [Fact]
    public async Task RemoveUnit_ValidAndErrorCases()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);
        var baseUnitId = (await ProductApi.GetAsync(client, ProductApi.Product(id), owner.AccessToken)).Body!["units"]![0]!["id"]!.GetValue<Guid>();
        var boxId = (await ProductApi.PostAsync(client, ProductApi.Units(id), Box, owner.AccessToken)).Body!["id"]!.GetValue<Guid>();
        var cartonId = (await ProductApi.PostAsync(client, ProductApi.Units(id), new { name = "كرتونة", factor = "30", sellPrice = "1" }, owner.AccessToken))
            .Body!["id"]!.GetValue<Guid>();

        var wrongTenant = await ProductApi.DeleteAsync(client, ProductApi.Unit(id, boxId), stranger.AccessToken);
        var removed = await ProductApi.DeleteAsync(client, ProductApi.Unit(id, boxId), owner.AccessToken);
        var again = await ProductApi.DeleteAsync(client, ProductApi.Unit(id, boxId), owner.AccessToken);
        var baseUnit = await ProductApi.DeleteAsync(client, ProductApi.Unit(id, baseUnitId), owner.AccessToken);
        await ProductApi.PostAsync(client, ProductApi.Archive(id), new { }, owner.AccessToken);
        var archived = await ProductApi.DeleteAsync(client, ProductApi.Unit(id, cartonId), owner.AccessToken);

        Expect(wrongTenant, HttpStatusCode.NotFound, "Product.NotFound");
        removed.Status.Should().Be(HttpStatusCode.NoContent);
        Expect(again, HttpStatusCode.NotFound, "ProductUnit.NotFound");
        Expect(baseUnit, HttpStatusCode.BadRequest, "ProductUnit.BaseUnitCannotBeRemoved");
        Expect(archived, HttpStatusCode.Conflict, "Product.Archived");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM product_units WHERE product_id = @p0", id)).Should().Be(2);
    }

    [Fact]
    public async Task Adjust_InAnotherUnit_Returns201MovementInBaseUnits()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);
        var boxId = (await ProductApi.PostAsync(client, ProductApi.Units(id), Box, owner.AccessToken)).Body!["id"]!.GetValue<Guid>();

        var response = await ProductApi.PostAsync(
            client, ProductApi.Adjustments(id), new { quantityChange = "-2.000", unitId = boxId, note = "Expired, thrown away" }, owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Created, response.Raw);
        var movement = response.Body!;
        movement["quantityChange"]!.GetValue<string>().Should().Be("-6.000");
        movement["balanceAfter"]!.GetValue<string>().Should().Be("-6.000");
        movement["reason"]!.GetValue<string>().Should().Be("ManualAdjustment");
        movement["note"]!.GetValue<string>().Should().Be("Expired, thrown away");
        movement["productId"]!.GetValue<Guid>().Should().Be(id);
        movement["invoiceId"].Should().BeNull();
        movement["saleId"].Should().BeNull();
        movement["createdByUserId"]!.GetValue<Guid>().Should().Be(owner.UserId);
        movement["createdAt"]!.GetValue<string>().Should().EndWith("Z");
        (await ProductApi.GetAsync(client, ProductApi.Product(id), owner.AccessToken)).Body!["quantity"]!.GetValue<string>().Should().Be("-6.000");
    }

    [Fact]
    public async Task Adjust_ErrorCases()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var staff = TestJwt.Mint(owner.UserId, owner.ShopId, "Staff");
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);

        var noNote = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "1" }, owner.AccessToken);
        var zero = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "0.000", note = "n" }, owner.AccessToken);
        var decimals = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "1.0001", note = "n" }, owner.AccessToken);
        var range = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "-100000000000", note = "n" }, owner.AccessToken);
        var longNote = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "1", note = new string('n', 501) }, owner.AccessToken);
        var unknownUnit = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "1", note = "n", unitId = Guid.CreateVersion7() }, owner.AccessToken);
        var asStaff = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "1", note = "n" }, staff);
        var wrongTenant = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "1", note = "n" }, stranger.AccessToken);

        Expect(noNote, HttpStatusCode.BadRequest, "Stock.NoteRequired");
        Expect(zero, HttpStatusCode.BadRequest, "Stock.ZeroChange");
        Expect(decimals, HttpStatusCode.BadRequest, "Precision.TooManyDecimals");
        Expect(range, HttpStatusCode.BadRequest, "Precision.OutOfRange");
        Expect(longNote, HttpStatusCode.BadRequest, "Validation.Failed");
        Expect(unknownUnit, HttpStatusCode.NotFound, "ProductUnit.NotFound");
        Expect(asStaff, HttpStatusCode.Forbidden, "Auth.Forbidden");
        Expect(wrongTenant, HttpStatusCode.NotFound, "Product.NotFound");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM stock_movements WHERE product_id = @p0", id)).Should().Be(0);
    }

    [Fact]
    public async Task Adjust_ArchivedProduct_IsAllowed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);
        await ProductApi.PostAsync(client, ProductApi.Archive(id), new { }, owner.AccessToken);

        var response = await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "-1", note = "late" }, owner.AccessToken);

        response.Status.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Count_ChangedThenUnchanged()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(openingQuantity: "20"));
        var boxId = (await ProductApi.PostAsync(client, ProductApi.Units(id), Box, owner.AccessToken)).Body!["id"]!.GetValue<Guid>();

        var changed = await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "26.000" }, owner.AccessToken);
        var unchanged = await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "26" }, owner.AccessToken);
        var inBoxes = await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "3", unitId = boxId }, owner.AccessToken);

        changed.Status.Should().Be(HttpStatusCode.OK, changed.Raw);
        changed.Body!["changed"]!.GetValue<bool>().Should().BeTrue();
        changed.Body!["quantity"]!.GetValue<string>().Should().Be("26.000");
        changed.Body!["movement"]!["quantityChange"]!.GetValue<string>().Should().Be("6.000");
        changed.Body!["movement"]!["reason"]!.GetValue<string>().Should().Be("StockCount");
        unchanged.Body!["changed"]!.GetValue<bool>().Should().BeFalse();
        unchanged.Body!["quantity"]!.GetValue<string>().Should().Be("26.000");
        unchanged.Body!.AsObject().ContainsKey("movement").Should().BeTrue();
        unchanged.Body!["movement"].Should().BeNull();
        inBoxes.Body!["quantity"]!.GetValue<string>().Should().Be("9.000");
    }

    [Fact]
    public async Task Count_ErrorCases()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var staff = TestJwt.Mint(owner.UserId, owner.ShopId, "Staff");
        var id = await ProductApi.CreateAsync(client, owner.AccessToken);

        Expect(await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "-1" }, owner.AccessToken), HttpStatusCode.BadRequest, "Precision.Negative");
        Expect(await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "1.0001" }, owner.AccessToken), HttpStatusCode.BadRequest, "Precision.TooManyDecimals");
        Expect(await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "100000000000" }, owner.AccessToken), HttpStatusCode.BadRequest, "Precision.OutOfRange");
        Expect(await ProductApi.PostRawAsync(client, ProductApi.StockCounts(id), """{ "countedQuantity": 5 }""", owner.AccessToken), HttpStatusCode.BadRequest, "Validation.Failed");
        Expect(await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "1", unitId = Guid.CreateVersion7() }, owner.AccessToken), HttpStatusCode.NotFound, "ProductUnit.NotFound");
        Expect(await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "1" }, staff), HttpStatusCode.Forbidden, "Auth.Forbidden");
        Expect(await ProductApi.PostAsync(client, ProductApi.StockCounts(id), new { countedQuantity = "1" }, stranger.AccessToken), HttpStatusCode.NotFound, "Product.NotFound");
    }

    [Fact]
    public async Task Movements_NewestFirstPagedAndScopedToTheShop()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var id = await ProductApi.CreateAsync(client, owner.AccessToken, ProductApi.CreateBody(openingQuantity: "10"));
        await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "-1", note = "first" }, owner.AccessToken);
        await ProductApi.PostAsync(client, ProductApi.Adjustments(id), new { quantityChange = "-2", note = "second" }, owner.AccessToken);

        var page = await ProductApi.GetAsync(client, $"{ProductApi.Movements(id)}?pageSize=2", owner.AccessToken);
        var lastPage = await ProductApi.GetAsync(client, $"{ProductApi.Movements(id)}?pageSize=2&page=2", owner.AccessToken);
        var tooBig = await ProductApi.GetAsync(client, $"{ProductApi.Movements(id)}?pageSize=101", owner.AccessToken);
        var wrongTenant = await ProductApi.GetAsync(client, ProductApi.Movements(id), stranger.AccessToken);

        page.Status.Should().Be(HttpStatusCode.OK);
        page.Body!["totalCount"]!.GetValue<int>().Should().Be(3);
        Notes(page).Should().Equal("second", "first");
        page.Body!["items"]![0]!["balanceAfter"]!.GetValue<string>().Should().Be("7.000");
        Notes(lastPage).Should().Equal("Opening stock");
        Expect(tooBig, HttpStatusCode.BadRequest, "Validation.Failed");
        Expect(wrongTenant, HttpStatusCode.NotFound, "Product.NotFound");
    }

    private static List<string?> Notes(ApiResponse response) =>
        response.Body!["items"]!.AsArray().Select(item => item!["note"]?.GetValue<string>()).ToList();

    private static void Expect(ApiResponse response, HttpStatusCode status, string code)
    {
        response.Status.Should().Be(status, response.Raw);
        response.Code.Should().Be(code, response.Raw);
        response.ContentType.Should().Be("application/problem+json");
    }
}
