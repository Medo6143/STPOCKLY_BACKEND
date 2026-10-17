using NSubstitute;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.Matching;
using Stockly.Domain.Text;

namespace Stockly.Application.Tests.Features.Invoices.Matching;

/// <summary>Matching v1 (docs/design/backend.md §8): each strategy, and the matcher's ordering.</summary>
public sealed class ProductMatchingTests
{
    private static readonly Guid SupplierId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000d1");
    private static readonly Guid Panadol = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e1");
    private static readonly Guid Strip = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e2");
    private static readonly Guid Box = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e3");

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IProductAliasRepository _aliases = Substitute.For<IProductAliasRepository>();

    public ProductMatchingTests()
    {
        _products.FindByNormalizedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<NamedProduct>());
        _aliases.FindExactAsync(Arg.Any<Guid?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AliasMatch>());
    }

    [Fact]
    public async Task Alias_SupplierAliasExists_ProductAndAliasUnitWithConfidenceOne()
    {
        _aliases.FindExactAsync(SupplierId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AliasMatch> { new(N("بنادول ٢٠ قرص"), Panadol, Box) });

        var matches = await new ExactAliasMatchStrategy(_aliases).MatchAsync(
            SupplierId, [Input(1, "بنادول ٢٠ قرص"), Input(2, "شيء آخر")], CancellationToken.None);

        matches.Should().ContainSingle().Which.Value.Should().Be(new LineMatch(1, Panadol, Box, 1.00m));
    }

    [Fact]
    public async Task Alias_OneQueryForAllLinesWithDistinctNamesAndTheInvoiceSupplier()
    {
        await new ExactAliasMatchStrategy(_aliases).MatchAsync(
            null, [Input(1, "أ"), Input(2, "ب"), Input(3, "أ")], CancellationToken.None);

        await _aliases.Received(1).FindExactAsync(
            null, Arg.Is<IReadOnlyCollection<string>>(names => names.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Name_UnitTextFound_ThatUnitWithConfidencePointNine()
    {
        GivenProducts(new NamedProduct(Panadol, N("بنادول"), [new(Strip, "شريط", true), new(Box, "علبة", false)]));

        var matches = await Name([Input(1, "بنادول", "علبة")]);

        matches[1].Should().Be(new LineMatch(1, Panadol, Box, 0.90m));
    }

    [Fact]
    public async Task Name_NoUnitTextAndSingleUnit_ThatUnitWithConfidencePointNine()
    {
        GivenProducts(new NamedProduct(Panadol, N("بنادول"), [new(Strip, "شريط", true)]));

        var matches = await Name([Input(1, "بنادول")]);

        matches[1].Should().Be(new LineMatch(1, Panadol, Strip, 0.90m));
    }

    [Fact]
    public async Task Name_UnitTextNotFound_BaseUnitWithConfidencePointSeven()
    {
        GivenProducts(new NamedProduct(Panadol, N("بنادول"), [new(Box, "علبة", false), new(Strip, "شريط", true)]));

        var matches = await Name([Input(1, "بنادول", "كرتونة")]);

        matches[1].Should().Be(new LineMatch(1, Panadol, Strip, 0.70m));
    }

    [Fact]
    public async Task Name_NoUnitTextAndSeveralUnits_BaseUnitWithConfidencePointSeven()
    {
        GivenProducts(new NamedProduct(Panadol, N("بنادول"), [new(Box, "علبة", false), new(Strip, "شريط", true)]));

        var matches = await Name([Input(1, "بنادول")]);

        matches[1].Should().Be(new LineMatch(1, Panadol, Strip, 0.70m));
    }

    [Fact]
    public async Task Name_TwoProductsWithTheSameName_AmbiguousNoMatch()
    {
        GivenProducts(
            new NamedProduct(Panadol, N("بنادول"), [new(Strip, "شريط", true)]),
            new NamedProduct(Guid.CreateVersion7(), N("بنادول"), [new(Guid.CreateVersion7(), "شريط", true)]));

        var matches = await Name([Input(1, "بنادول")]);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Name_ArabicSpellingVariants_MatchAfterNormalization()
    {
        // The product was saved as "بنادول اكسترا" and the unit as "علبة"; the invoice writes hamza, tashkeel and teh marbuta variants.
        GivenProducts(new NamedProduct(Panadol, N("بنادول اكسترا"), [new(Strip, "شريط", true), new(Box, "علبة", false)]));

        var matches = await Name([Input(1, "بَنادول  إكسترا", "عُلبه")]);

        matches[1].Should().Be(new LineMatch(1, Panadol, Box, 0.90m));
    }

    [Fact]
    public async Task Name_NoProduct_NoMatch()
    {
        var matches = await Name([Input(1, "غير موجود")]);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Matcher_AliasFirst_NameStrategyOnlySeesTheRestAndUnmatchedLinesAreNulls()
    {
        _aliases.FindExactAsync(SupplierId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AliasMatch> { new(N("بنادول"), Panadol, Box) });
        GivenProducts(new NamedProduct(Panadol, N("بنادول"), [new(Strip, "شريط", true)]));
        var matcher = new ProductMatcher([new ExactNameMatchStrategy(_products), new ExactAliasMatchStrategy(_aliases)]);

        var matches = await matcher.MatchAsync(SupplierId, [Input(1, "بنادول"), Input(2, "مجهول")], CancellationToken.None);

        matches.Should().Equal(new LineMatch(1, Panadol, Box, 1.00m), LineMatch.None(2));
        await _products.Received(1).FindByNormalizedNamesAsync(
            Arg.Is<IReadOnlyCollection<string>>(names => names.SequenceEqual(new[] { N("مجهول") })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Matcher_EverythingMatchedEarly_LaterStrategiesAreNotCalled()
    {
        _aliases.FindExactAsync(Arg.Any<Guid?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AliasMatch> { new(N("بنادول"), Panadol, Box) });
        var matcher = new ProductMatcher([new ExactAliasMatchStrategy(_aliases), new ExactNameMatchStrategy(_products)]);

        await matcher.MatchAsync(null, [Input(1, "بنادول")], CancellationToken.None);

        await _products.DidNotReceive().FindByNormalizedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Strategies_Order_AliasBeforeName()
    {
        new ExactAliasMatchStrategy(_aliases).Order.Should().BeLessThan(new ExactNameMatchStrategy(_products).Order);
    }

    private static string N(string text) => ArabicTextNormalizer.Normalize(text);

    private static MatchInput Input(int lineNo, string name, string? unit = null) =>
        new(lineNo, N(name), unit is null ? null : N(unit));

    private void GivenProducts(params NamedProduct[] products) =>
        _products.FindByNormalizedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(products.ToList());

    private Task<IReadOnlyDictionary<int, LineMatch>> Name(IReadOnlyList<MatchInput> lines) =>
        new ExactNameMatchStrategy(_products).MatchAsync(null, lines, CancellationToken.None);
}
