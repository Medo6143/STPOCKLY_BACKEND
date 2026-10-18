using Stockly.Domain.Products;
using Stockly.Domain.Suppliers;
using Stockly.Domain.Text;

namespace Stockly.Domain.Tests.Text;

public sealed class LatinKeyTests
{
    [Theory]
    // Cross-script pairs from Egyptian pharmacies and groceries: both spellings give the same key.
    [InlineData("بنادول اكسترا", "Panadol Extra", "bndl kstr")]
    [InlineData("بَنادول إكسترا", "PANADOL extra", "bndl kstr")]
    [InlineData("كونجستال", "Congestal", "kngstl")]
    [InlineData("اوجمنتين ١ جم", "Augmentin 1 gm", "gmntn 1 gm")]
    [InlineData("بروفين ٤٠٠", "Brufen 400", "brfn 400")]
    [InlineData("فولتارين", "Voltaren", "fltrn")]
    [InlineData("سيتال", "Cetal", "stl")]
    [InlineData("كريمة", "Cream", "krm")]
    [InlineData("شيبسي", "Chipsy", "sbs")]
    [InlineData("ميثيل", "Methyl", "mtl")]
    [InlineData("قهوة", "Qahwa", "k")]
    public void From_ArabicAndLatinSpellings_GiveTheSameKey(string arabic, string latin, string expected)
    {
        LatinKey.From(arabic).Should().Be(expected);
        LatinKey.From(latin).Should().Be(expected);
    }

    [Theory]
    // Digraphs and soft c
    [InlineData("pharma", "frm")]
    [InlineData("shampoo", "smb")]
    [InlineData("check", "sk")]
    [InlineData("thyme", "tm")]
    [InlineData("khaki", "k")]
    [InlineData("ghee", "g")]
    [InlineData("xanax", "ksnks")]
    [InlineData("cycle", "skl")]
    [InlineData("cocoa", "k")]
    // Repeated letters once; digits as written
    [InlineData("vitamin c 1000", "ftmn k 1000")]
    [InlineData("ammonia", "mn")]
    // Word-final h dropped in both scripts, inner h kept
    [InlineData("صيدلية", "sdl")]
    [InlineData("محمد", "mhmd")]
    [InlineData("sarah h", "sr")]
    // Separators: punctuation and symbols become one space
    [InlineData("panadol-extra/500", "bndl kstr 500")]
    [InlineData("  50% off!  ", "50 f")]
    // Persian letters
    [InlineData("پيبسي", "bs")]
    [InlineData("ڤيتامين", "ftmn")]
    [InlineData("گاز", "gz")]
    // Vowels only, empty, whitespace
    [InlineData("aeiou", "")]
    [InlineData("اوي", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void From_Text_FollowsTheSteps(string text, string expected) =>
        LatinKey.From(text).Should().Be(expected);

    [Theory]
    [InlineData("بنادول اكسترا")]
    [InlineData("  Panadol   EXTRA ٥٠٠ ")]
    [InlineData("مُحَمَّد")]
    public void From_NormalizedOrRawText_SameKey(string text) =>
        LatinKey.From(ArabicTextNormalizer.Normalize(text)).Should().Be(LatinKey.From(text));

    [Fact]
    public void From_Null_Throws()
    {
        var act = () => LatinKey.From(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LetterMap_MapsOnlyToLatinLettersOrDrops() =>
        string.Concat(LatinKey.LetterMap.Values).Should().MatchRegex("^[a-z]*$");

    [Fact]
    public void ProductCreateAndRename_KeepTheLatinKeyInStep()
    {
        var product = Product.Create(TestData.ShopId, "بنادول اكسترا", "شريط", 1m, 1m, 0m, TestData.Now).Value;
        product.LatinKey.Should().Be("bndl kstr");

        product.Rename("Brufen 400").IsSuccess.Should().BeTrue();
        product.LatinKey.Should().Be("brfn 400");

        product.Update("فولتارين", null, null, 1m, 0m).IsSuccess.Should().BeTrue();
        product.LatinKey.Should().Be("fltrn");
    }

    [Fact]
    public void ProductAliasLearn_SetsTheLatinKey()
    {
        var alias = ProductAlias.Learn(TestData.ShopId, Guid.NewGuid(), null, Guid.NewGuid(), "Panadol Extra", TestData.Now).Value;

        alias.LatinKey.Should().Be("bndl kstr");
    }
}
