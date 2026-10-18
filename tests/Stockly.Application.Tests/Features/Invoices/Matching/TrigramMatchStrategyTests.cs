using NSubstitute;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.Matching;
using Stockly.Domain.Text;

namespace Stockly.Application.Tests.Features.Invoices.Matching;

/// <summary>Fuzzy matching (Order 30): confidence, caps, other-supplier factor, units, ambiguity and the single query.</summary>
public sealed class TrigramMatchStrategyTests
{
    private static readonly Guid SupplierId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000d1");
    private static readonly Guid Panadol = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e1");
    private static readonly Guid Strip = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e2");
    private static readonly Guid Box = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e3");
    private static readonly Guid Brufen = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e4");
    private static readonly Guid BrufenBox = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e5");

    private static readonly IReadOnlyList<NamedProductUnit> PanadolUnits = [new(Strip, "شريط", true), new(Box, "علبة", false)];

    private readonly IProductSimilarityQueries _similarity = Substitute.For<IProductSimilarityQueries>();
    private readonly MatchingOptions _options = new();

    [Fact]
    public async Task Match_OneQueryForAllLinesWithLatinKeysAndTheConfiguredThreshold()
    {
        GivenCandidates();

        await Strategy().MatchAsync(SupplierId, [Input(1, "بنادول اكسترا"), Input(2, "Brufen 400")], CancellationToken.None);

        await _similarity.Received(1).FindSimilarAsync(
            SupplierId,
            Arg.Is<IReadOnlyList<SimilarityProbe>>(probes =>
                probes.Count == 2
                && probes[0] == new SimilarityProbe(1, "بنادول اكسترا", "bndl kstr")
                && probes[1] == new SimilarityProbe(2, "brufen 400", "brfn 400")),
            0.45,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Match_BlankLinesOnly_NoQuery()
    {
        var matches = await Strategy().MatchAsync(SupplierId, [new MatchInput(1, string.Empty, null)], CancellationToken.None);

        matches.Should().BeEmpty();
        await _similarity.DidNotReceiveWithAnyArgs().FindSimilarAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Match_NameWithCertainUnit_SimilarityRoundedToTwoDecimals()
    {
        GivenCandidates(Name(1, Panadol, 0.734, PanadolUnits));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "Panadol Extra", "علبة")], CancellationToken.None);

        matches[1].Should().Be(new LineMatch(1, Panadol, Box, 0.73m));
    }

    [Fact]
    public async Task Match_NameWithCertainUnit_CappedAtTheExactNameConfidence()
    {
        GivenCandidates(Name(1, Panadol, 1.0, PanadolUnits));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "Panadol Extra", "علبة")], CancellationToken.None);

        matches[1].Should().Be(new LineMatch(1, Panadol, Box, MatchConfidence.NameAndUnit));
    }

    [Fact]
    public async Task Match_NameWithUnknownUnit_BaseUnitCappedAtPointSeven()
    {
        GivenCandidates(Name(1, Panadol, 0.95, PanadolUnits));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "Panadol Extra", "كرتونة")], CancellationToken.None);

        matches[1].Should().Be(new LineMatch(1, Panadol, Strip, MatchConfidence.NameWithBaseUnit));
    }

    [Fact]
    public async Task Match_NameWithoutUnits_NoMatch()
    {
        GivenCandidates(Name(1, Panadol, 0.8, []));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "Panadol")], CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Match_AliasOfTheInvoiceSupplier_AliasUnitAtTheSimilarity()
    {
        GivenCandidates(Alias(1, Panadol, Box, otherSupplier: false, 0.81));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "بنادول اكسترا ٢٠")], CancellationToken.None);

        matches[1].Should().Be(new LineMatch(1, Panadol, Box, 0.81m));
    }

    [Fact]
    public async Task Match_AliasOfAnotherSupplier_ConfidenceTimesPointNine()
    {
        GivenCandidates(Alias(1, Panadol, Box, otherSupplier: true, 1.0), Alias(2, Brufen, BrufenBox, otherSupplier: true, 0.75));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "بنادول علبة"), Input(2, "بروفين")], CancellationToken.None);

        matches[1].Should().Be(new LineMatch(1, Panadol, Box, 0.90m));
        matches[2].Should().Be(new LineMatch(2, Brufen, BrufenBox, 0.68m), "0.75 × 0.9 = 0.675, rounded half away from zero");
    }

    [Fact]
    public async Task Match_BestConfidenceWins_AfterTheOtherSupplierFactor()
    {
        GivenCandidates(Alias(1, Brufen, BrufenBox, otherSupplier: true, 0.80), Name(1, Panadol, 0.75, PanadolUnits));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "بنادول", "علبة")], CancellationToken.None);

        matches[1].Should().Be(new LineMatch(1, Panadol, Box, 0.75m), "0.80 × 0.9 = 0.72 is below 0.75");
    }

    [Fact]
    public async Task Match_TwoProductsShareTheBestConfidence_Ambiguous()
    {
        GivenCandidates(Name(1, Panadol, 0.6, PanadolUnits), Name(1, Brufen, 0.6, [new(BrufenBox, "علبة", true)]));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "صنف", "علبة")], CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Match_SameProductByAliasAndName_AliasUnitPreferred()
    {
        GivenCandidates(Name(1, Panadol, 0.6, PanadolUnits), Alias(1, Panadol, Box, otherSupplier: false, 0.6));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "بنادول")], CancellationToken.None);

        matches[1].Should().Be(new LineMatch(1, Panadol, Box, 0.60m));
    }

    [Fact]
    public async Task Match_NoCandidate_LineAbsent()
    {
        GivenCandidates(Name(2, Panadol, 0.9, PanadolUnits));

        var matches = await Strategy().MatchAsync(SupplierId, [Input(1, "شيء"), Input(2, "بنادول", "علبة")], CancellationToken.None);

        matches.Keys.Should().Equal(2);
    }

    [Fact]
    public async Task Matcher_TrigramRunsLastOnlyOnTheLinesTheExactStrategiesLeft()
    {
        var products = Substitute.For<IProductRepository>();
        products.FindByNormalizedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<NamedProduct> { new(Panadol, N("بنادول"), PanadolUnits) });
        var aliases = Substitute.For<IProductAliasRepository>();
        aliases.FindExactAsync(Arg.Any<Guid?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AliasMatch>());
        GivenCandidates(Name(2, Brufen, 0.7, [new(BrufenBox, "علبة", true)]));
        var matcher = new ProductMatcher(
            [Strategy(), new ExactNameMatchStrategy(products), new ExactAliasMatchStrategy(aliases)]);

        var matches = await matcher.MatchAsync(SupplierId, [Input(1, "بنادول", "علبة"), Input(2, "Brufen")], CancellationToken.None);

        matches.Should().Equal(new LineMatch(1, Panadol, Box, 0.90m), new LineMatch(2, Brufen, BrufenBox, 0.70m));
        await _similarity.Received(1).FindSimilarAsync(
            SupplierId, Arg.Is<IReadOnlyList<SimilarityProbe>>(probes => probes.Count == 1 && probes[0].LineNo == 2), 0.45, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0.29, false)]
    [InlineData(0.30, true)]
    [InlineData(0.45, true)]
    [InlineData(1.00, true)]
    [InlineData(1.01, false)]
    public void Options_ThresholdRange(double threshold, bool valid)
    {
        var options = new MatchingOptions { TrigramThreshold = threshold };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, new System.ComponentModel.DataAnnotations.ValidationContext(options), results, validateAllProperties: true);

        isValid.Should().Be(valid);
    }

    private TrigramMatchStrategy Strategy() => new(_similarity, _options);

    private void GivenCandidates(params SimilarCandidate[] candidates) =>
        _similarity.FindSimilarAsync(Arg.Any<Guid?>(), Arg.Any<IReadOnlyList<SimilarityProbe>>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(candidates);

    private static SimilarCandidate Name(int lineNo, Guid productId, double similarity, IReadOnlyList<NamedProductUnit> units) =>
        new(lineNo, productId, null, false, similarity, units);

    private static SimilarCandidate Alias(int lineNo, Guid productId, Guid unitId, bool otherSupplier, double similarity) =>
        new(lineNo, productId, unitId, otherSupplier, similarity, []);

    private static MatchInput Input(int lineNo, string name, string? unit = null) =>
        new(lineNo, N(name), unit is null ? null : N(unit));

    private static string N(string text) => ArabicTextNormalizer.Normalize(text);
}
