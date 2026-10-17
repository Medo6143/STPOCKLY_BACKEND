using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.Matching;
using Stockly.Domain.Suppliers;
using Stockly.Domain.Text;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// Matching v1 on a real PostgreSQL (docs/design/backend.md §8): the alias and name strategies over the production
/// repositories, one query per strategy for all lines (rule 33), archived products and other shops never match.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ProductMatchingPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Match_ManyLines_OneQueryPerStrategyAndTheExpectedMatches()
    {
        var shop = await fixture.CreateShopAsync();
        var supplier = Supplier.Create(shop.Id, "شركة الأدوية", null, TestTime.T0).Value;
        var panadol = Catalog.NewProduct(shop.Id, "بنادول اكسترا");
        var box = panadol.AddUnit("علبة", 3m, 40m, null).Value;
        var congestal = Catalog.NewProduct(shop.Id, "كونجستال");
        var archived = Catalog.NewProduct(shop.Id, "منتج قديم");
        archived.Archive();
        var twinA = Catalog.NewProduct(shop.Id, "مكرر");
        var twinB = Catalog.NewProduct(shop.Id, "مكرر");
        var archivedAliasTarget = Catalog.NewProduct(shop.Id, "صنف مؤرشف");
        archivedAliasTarget.Archive();
        await using (var seed = fixture.CreateContext(shop.Id))
        {
            seed.Add(supplier);
            seed.Products.AddRange(panadol, congestal, archived, twinA, twinB, archivedAliasTarget);
            await seed.SaveChangesAsync();
            seed.Add(ProductAlias.Learn(shop.Id, panadol.Id, supplier.Id, box.Id, "بنادول ٢٠ قرص", TestTime.T0).Value);
            seed.Add(ProductAlias.Learn(shop.Id, archivedAliasTarget.Id, supplier.Id, archivedAliasTarget.BaseUnit.Id, "مؤرشف", TestTime.T0).Value);
            seed.Add(ProductAlias.Learn(shop.Id, congestal.Id, supplierId: null, congestal.BaseUnit.Id, "كونجستال شراب", TestTime.T0).Value);
            await seed.SaveChangesAsync();
        }

        await using var scope = fixture.CreateScope(shop.Id);
        var matcher = Matcher(scope);
        using var counter = new CommandCounter(scope.ServiceProvider.GetRequiredService<StocklyDbContext>());

        var matches = await matcher.MatchAsync(
            supplier.Id,
            [
                Input(1, "بنادول ٢٠ قرص"),
                Input(2, "بنادول إكسترا", "عُلبة"),
                Input(3, "كونجستال", "زجاجة"),
                Input(4, "منتج قديم"),
                Input(5, "مكرر"),
                Input(6, "مؤرشف"),
                Input(7, "كونجستال شراب"),
            ],
            CancellationToken.None);

        counter.Commands.Should().HaveCount(2, "one query per strategy for all lines, never one per line (rule 33)");
        matches.Should().Equal(
            new LineMatch(1, panadol.Id, box.Id, 1.00m),
            new LineMatch(2, panadol.Id, box.Id, 0.90m),
            new LineMatch(3, congestal.Id, congestal.BaseUnit.Id, 0.70m),
            LineMatch.None(4),
            LineMatch.None(5),
            LineMatch.None(6),
            LineMatch.None(7));
    }

    [Fact]
    public async Task Match_UnknownSupplier_UsesOnlyAliasesWithoutSupplier()
    {
        var shop = await fixture.CreateShopAsync();
        var supplier = Supplier.Create(shop.Id, "مورد", null, TestTime.T0).Value;
        var product = Catalog.NewProduct(shop.Id, "صنف");
        await using (var seed = fixture.CreateContext(shop.Id))
        {
            seed.Add(supplier);
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
            seed.Add(ProductAlias.Learn(shop.Id, product.Id, supplierId: null, product.BaseUnit.Id, "اسم بلا مورد", TestTime.T0).Value);
            seed.Add(ProductAlias.Learn(shop.Id, product.Id, supplier.Id, product.BaseUnit.Id, "اسم المورد", TestTime.T0).Value);
            await seed.SaveChangesAsync();
        }

        await using var scope = fixture.CreateScope(shop.Id);

        var matches = await Matcher(scope).MatchAsync(
            null, [Input(1, "اسم بلا مورد"), Input(2, "اسم المورد")], CancellationToken.None);

        matches.Should().Equal(new LineMatch(1, product.Id, product.BaseUnit.Id, 1.00m), LineMatch.None(2));
    }

    [Fact]
    public async Task Match_ProductOfAnotherShop_NoMatch()
    {
        var shop = await fixture.CreateShopAsync();
        var other = await fixture.CreateShopAsync();
        await using (var seed = fixture.CreateContext(other.Id))
        {
            seed.Products.Add(Catalog.NewProduct(other.Id, "صنف المحل الآخر"));
            await seed.SaveChangesAsync();
        }

        await using var scope = fixture.CreateScope(shop.Id);

        var matches = await Matcher(scope).MatchAsync(null, [Input(1, "صنف المحل الآخر")], CancellationToken.None);

        matches.Should().Equal(LineMatch.None(1));
    }

    private static ProductMatcher Matcher(AsyncServiceScope scope) => new(
    [
        new ExactAliasMatchStrategy(scope.ServiceProvider.GetRequiredService<IProductAliasRepository>()),
        new ExactNameMatchStrategy(scope.ServiceProvider.GetRequiredService<IProductRepository>()),
    ]);

    private static MatchInput Input(int lineNo, string name, string? unit = null) =>
        new(lineNo, ArabicTextNormalizer.Normalize(name), unit is null ? null : ArabicTextNormalizer.Normalize(unit));
}
