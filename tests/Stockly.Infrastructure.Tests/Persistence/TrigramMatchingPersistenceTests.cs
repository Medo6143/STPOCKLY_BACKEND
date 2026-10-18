using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.Matching;
using Stockly.Domain.Suppliers;
using Stockly.Domain.Text;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// Fuzzy matching on a real PostgreSQL with pg_trgm (docs/features/invoice-scan.md): cross-script names, aliases of other
/// suppliers, archived products and other shops, one query for all lines, and the SQL copy of <see cref="LatinKey"/>.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class TrigramMatchingPersistenceTests(PostgresFixture fixture)
{
    public static TheoryData<string> LatinKeySamples =>
    [
        "بنادول اكسترا",
        "Panadol Extra 500mg",
        "كونجستال شراب",
        "Congestal",
        "اوجمنتين ١ جم",
        "Augmentin 1 gm",
        "شيبسي بالجبنة",
        "Chipsy cheese",
        "قهوة تركي محوج",
        "Vitamin C 1000",
        "ڤيتامين سي ١٠٠٠",
        "پيبسي كولا",
        "گاز",
        "phyto-check/xanax",
        "cycle cocoa",
        "50% off!!",
        "aeiou",
        "مُحَمَّدٌ",
        "  أَقْرَاصٌ ـ ٢٠   قرص  ",
        "Café crème",
        "sarah h",
        "Thyme khaki ghee",
    ];

    [Theory]
    [MemberData(nameof(LatinKeySamples))]
    public async Task LatinKeySqlFunction_SameKeyAsTheDomain(string text)
    {
        var normalized = ArabicTextNormalizer.Normalize(text);

        var sql = await fixture.ScalarAsync<string>("SELECT stockly_latin_key(@p0)", normalized);

        sql.Should().Be(LatinKey.From(text));
    }

    [Fact]
    public async Task Match_CrossScriptAndFuzzyNames_ByTheTrigramStrategyInOneQuery()
    {
        var shop = await fixture.CreateShopAsync();
        var panadol = Catalog.NewProduct(shop.Id, "Panadol Extra");
        var box = panadol.AddUnit("علبة", 3m, 40m, null).Value;
        var brufen = Catalog.NewProduct(shop.Id, "بروفين ٤٠٠");
        var archived = Catalog.NewProduct(shop.Id, "Congestal");
        archived.Archive();
        await using (var seed = fixture.CreateContext(shop.Id))
        {
            seed.Products.AddRange(panadol, brufen, archived);
            await seed.SaveChangesAsync();
        }

        await using var scope = fixture.CreateScope(shop.Id);
        var strategy = Strategy(scope);
        using var counter = new CommandCounter(scope.ServiceProvider.GetRequiredService<StocklyDbContext>());

        var matches = await strategy.MatchAsync(
            null,
            [Input(1, "بنادول اكسترا", "علبة"), Input(2, "Brufen 400"), Input(3, "كونجستال"), Input(4, "شيء لا علاقة له")],
            CancellationToken.None);

        counter.Commands.Should().ContainSingle("one query for all lines (rule 33)");
        matches.Should().HaveCount(2);
        matches[1].Should().Be(new LineMatch(1, panadol.Id, box.Id, 0.90m), "same latin key: similarity 1.00, capped at the exact-name confidence");
        matches[2].Should().Be(new LineMatch(2, brufen.Id, brufen.BaseUnit.Id, 0.90m), "a single unit and no unit text is a certain unit");
        matches.Should().NotContainKey(3, "archived products never match");
    }

    [Fact]
    public async Task Match_FuzzyArabicSpelling_SimilarityAsConfidence()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id, "شامبو هيد اند شولدرز");
        await using (var seed = fixture.CreateContext(shop.Id))
        {
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }

        var expected = await fixture.ScalarAsync<float>(
            "SELECT GREATEST(similarity(@p0, @p1), similarity(@p2, @p3))",
            product.NormalizedName,
            ArabicTextNormalizer.Normalize("شامبو هيد شولدرز"),
            product.LatinKey,
            LatinKey.From("شامبو هيد شولدرز"));
        await using var scope = fixture.CreateScope(shop.Id);

        var matches = await Strategy(scope).MatchAsync(null, [Input(1, "شامبو هيد شولدرز")], CancellationToken.None);

        expected.Should().BeInRange(0.45f, 0.89f, "the example must match without reaching the cap");
        var confidence = Math.Min(Math.Round((decimal)(double)expected, 2, MidpointRounding.AwayFromZero), MatchConfidence.NameAndUnit);
        matches[1].Should().Be(new LineMatch(1, product.Id, product.BaseUnit.Id, confidence));
    }

    [Fact]
    public async Task Match_AliasLearnedFromAnotherSupplier_ConfidenceTimesPointNine()
    {
        var shop = await fixture.CreateShopAsync();
        var learnedFrom = Supplier.Create(shop.Id, "مورد قديم", null, TestTime.T0).Value;
        var invoiceSupplier = Supplier.Create(shop.Id, "مورد جديد", null, TestTime.T0).Value;
        var product = Catalog.NewProduct(shop.Id, "صنف داخلي");
        var box = product.AddUnit("علبة", 10m, 100m, null).Value;
        await using (var seed = fixture.CreateContext(shop.Id))
        {
            seed.AddRange(learnedFrom, invoiceSupplier);
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }

        await using (var upsert = fixture.CreateScope(shop.Id))
        {
            await upsert.ServiceProvider.GetRequiredService<IProductAliasRepository>().UpsertAsync(
                [ProductAlias.Learn(shop.Id, product.Id, learnedFrom.Id, box.Id, "Panadol Extra 20 tab", TestTime.T0).Value],
                CancellationToken.None);
        }

        var stored = await fixture.ScalarAsync<string>("SELECT latin_key FROM product_aliases WHERE product_id = @p0", product.Id);
        stored.Should().Be("bndl kstr 20 tb", "the alias upsert writes the latin key");
        await using var scope = fixture.CreateScope(shop.Id);

        var matches = await Strategy(scope).MatchAsync(
            invoiceSupplier.Id, [Input(1, "بنادول اكسترا ٢٠ تاب")], CancellationToken.None);

        matches[1].Should().Be(new LineMatch(1, product.Id, box.Id, 0.90m), "same latin key (1.00) × 0.9 for another supplier");
    }

    [Fact]
    public async Task Match_ProductOfAnotherShop_NoMatch()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        await using (var seed = fixture.CreateContext(other.Id))
        {
            seed.Products.Add(Catalog.NewProduct(other.Id, "Panadol Extra"));
            await seed.SaveChangesAsync();
        }

        await using var scope = fixture.CreateScope(shop.Id);

        var matches = await Strategy(scope).MatchAsync(null, [Input(1, "بنادول اكسترا")], CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task FindSimilar_NoTenant_NothingAndNoQuery()
    {
        await using var scope = fixture.CreateScope(shopId: null);
        var queries = scope.ServiceProvider.GetRequiredService<IProductSimilarityQueries>();
        using var counter = new CommandCounter(scope.ServiceProvider.GetRequiredService<StocklyDbContext>());

        var found = await queries.FindSimilarAsync(null, [new SimilarityProbe(1, "x", "x")], 0.45, CancellationToken.None);

        found.Should().BeEmpty();
        counter.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task FullMatcher_ThreeStrategies_AtMostOneQueryEach()
    {
        var shop = await fixture.CreateShopAsync();
        var exact = Catalog.NewProduct(shop.Id, "اسم مطابق");
        var fuzzy = Catalog.NewProduct(shop.Id, "Augmentin 1 gm");
        await using (var seed = fixture.CreateContext(shop.Id))
        {
            seed.Products.AddRange(exact, fuzzy);
            await seed.SaveChangesAsync();
        }

        await using var scope = fixture.CreateScope(shop.Id);
        var matcher = new ProductMatcher(
        [
            new ExactAliasMatchStrategy(scope.ServiceProvider.GetRequiredService<IProductAliasRepository>()),
            new ExactNameMatchStrategy(scope.ServiceProvider.GetRequiredService<IProductRepository>()),
            Strategy(scope),
        ]);
        using var counter = new CommandCounter(scope.ServiceProvider.GetRequiredService<StocklyDbContext>());

        var matches = await matcher.MatchAsync(null, [Input(1, "اسم مطابق"), Input(2, "اوجمنتين ١ جم")], CancellationToken.None);

        counter.Commands.Should().HaveCount(3);
        matches.Should().Equal(
            new LineMatch(1, exact.Id, exact.BaseUnit.Id, 0.90m),
            new LineMatch(2, fuzzy.Id, fuzzy.BaseUnit.Id, 0.90m));
    }

    private static TrigramMatchStrategy Strategy(AsyncServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<IProductSimilarityQueries>(), new MatchingOptions());

    private static MatchInput Input(int lineNo, string name, string? unit = null) =>
        new(lineNo, ArabicTextNormalizer.Normalize(name), unit is null ? null : ArabicTextNormalizer.Normalize(unit));
}
