using Stockly.Domain.Invoices;

namespace Stockly.Domain.Tests.Invoices;

/// <summary>
/// The two pure rules of I5 (docs/design/backend.md §10.5, §11 step 8): which products a confirm creates, and which
/// aliases it learns.
/// </summary>
public sealed class InvoiceConfirmationRulesTests
{
    private static readonly Guid SupplierId = Guid.CreateVersion7();

    [Fact]
    public void NewProducts_SameNameAndBaseUnitOnTwoLines_OneProductPricedByTheLowestLine()
    {
        // Lines given out of order: the lowest line number sets the prices whatever the caller's order.
        var items = Items(
            New(3, "فيتامين سي", "علبة", unitPrice: 30m, sellPrice: 40m),
            New(1, "فيتامين  سى", "علبه", unitPrice: 25m, sellPrice: 35m));

        var created = NewProductsFromInvoice.Create(Enumerable.Reverse(items), TestData.ShopId, TestData.Now);

        var product = created.Value.Values.Should().ContainSingle().Subject;
        product.CostPrice.Should().Be(25m);
        product.BaseUnit.SellPrice.Should().Be(35m);
        product.ShopId.Should().Be(TestData.ShopId);
        created.Value.Should().ContainKey(NewProductsFromInvoice.KeyOf(items[0]));
        created.Value.Should().ContainKey(NewProductsFromInvoice.KeyOf(items[1]));
    }

    [Fact]
    public void NewProducts_SameNameOtherBaseUnit_TwoProducts_ExistingLinesIgnored()
    {
        var items = Items(
            New(1, "بنادول", "علبة", unitPrice: 30m, sellPrice: 40m),
            New(2, "بنادول", "شريط", unitPrice: 10m, sellPrice: 15m),
            Existing(3, "كونجستال"));

        var created = NewProductsFromInvoice.Create(items, TestData.ShopId, TestData.Now);

        created.Value.Should().HaveCount(2);
        created.Value.Values.Select(product => product.BaseUnit.Name).Should().BeEquivalentTo(["علبة", "شريط"]);
    }

    [Fact]
    public void Aliases_SameRawNameOnTwoLines_OneAliasForTheHighestLineCountedTwice()
    {
        var items = Items(Existing(1, "بنادول علبة"), Existing(2, "كونجستال"), Existing(4, "بنادول  علبه"));
        var first = (Line: items[0], ProductId: Guid.CreateVersion7(), UnitId: Guid.CreateVersion7());
        var last = (Line: items[2], ProductId: Guid.CreateVersion7(), UnitId: Guid.CreateVersion7());
        var other = (Line: items[1], ProductId: Guid.CreateVersion7(), UnitId: Guid.CreateVersion7());

        var learned = InvoiceAliasLearning.Learn([last, other, first], TestData.ShopId, SupplierId, TestData.Now);

        learned.Value.Should().HaveCount(2);
        var panadol = learned.Value.Single(alias => alias.ProductId == last.ProductId);
        panadol.ProductUnitId.Should().Be(last.UnitId, "the highest line number decides the product and unit");
        panadol.TimesConfirmed.Should().Be(2, "every line with the name counts");
        panadol.SupplierId.Should().Be(SupplierId);
        learned.Value.Should().NotContain(alias => alias.ProductId == first.ProductId);
    }

    private static InvoiceReviewLine New(int lineNo, string name, string baseUnit, decimal unitPrice, decimal sellPrice) =>
        new(lineNo, name, 1m, unitPrice, null, null, null, new InvoiceNewProduct(name, baseUnit, sellPrice));

    private static InvoiceReviewLine Existing(int lineNo, string rawName) =>
        new(lineNo, rawName, 1m, 2m, null, null, new InvoiceLineProduct(Guid.CreateVersion7(), Guid.CreateVersion7()), null);

    /// <summary>The items of a reviewed invoice with these lines, in line order.</summary>
    private static List<InvoiceItem> Items(params InvoiceReviewLine[] lines)
    {
        var invoice = Invoice.CreateForScan(
            TestData.ShopId, Guid.CreateVersion7(), TestData.UserId, new string('a', 64), TestData.Now, id => $"k/{id}").Value;
        invoice.CompleteExtraction(
            InvoiceHeader.Empty, [new InvoiceLineDraft("x", 1m, 1m, null, null)], [], null, null, TestData.Now).IsSuccess
            .Should().BeTrue();
        invoice.ReplaceLines(InvoiceHeader.Empty, lines, null, []).IsSuccess.Should().BeTrue();
        return [.. invoice.Items.OrderBy(item => item.LineNo)];
    }
}
