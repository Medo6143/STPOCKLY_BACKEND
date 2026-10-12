using Stockly.Domain.Text;

namespace Stockly.Domain.Tests.Text;

public sealed class ArabicTextNormalizerTests
{
    [Theory]
    // Alef forms → bare alef
    [InlineData("أحمد", "احمد")]
    [InlineData("إسعاف", "اسعاف")]
    [InlineData("آمال", "امال")]
    [InlineData("ٱلصيدلية", "الصيدليه")] // alef wasla
    // Taa marbuta → haa, alef maqsura → yaa
    [InlineData("صيدلية", "صيدليه")]
    [InlineData("مصطفى كامل", "مصطفي كامل")]
    // Tashkeel (U+064B–U+0652) and superscript alef (U+0670) removed
    [InlineData("مُحَمَّدٌ", "محمد")]
    [InlineData("ًٌٍَُِّْ", "")]
    [InlineData("هٰذا", "هذا")]
    // Tatweel (U+0640) removed
    [InlineData("بـــنادول", "بنادول")]
    // Arabic-Indic and Persian digits → ASCII
    [InlineData("٠١٢٣٤٥٦٧٨٩", "0123456789")]
    [InlineData("۰۱۲۳۴۵۶۷۸۹", "0123456789")]
    [InlineData("بنادول ٥٠٠", "بنادول 500")]
    // Latin lower-case invariant
    [InlineData("PANADOL Extra", "panadol extra")]
    // Trim and collapse any whitespace, including tabs, new lines and no-break space
    [InlineData("  بنادول    اكسترا  ", "بنادول اكسترا")]
    [InlineData("بنادول\t\r\nاكسترا", "بنادول اكسترا")]
    [InlineData("a  b", "a b")]
    // Mixed scripts stay mixed
    [InlineData("بنادول Extra 500", "بنادول extra 500")]
    [InlineData("Panadol بنادول ٥٠٠mg", "panadol بنادول 500mg")]
    // Everything at once; a lone tatweel between spaces disappears and the spaces collapse
    [InlineData("  أَقْرَاصٌ ـ ٢٠   قرص  ", "اقراص 20 قرص")]
    [InlineData("بَنادول إكسترا", "بنادول اكسترا")]
    // Edge cases
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("panadol 500", "panadol 500")]
    public void Normalize_Input_ReturnsExpected(string input, string expected)
    {
        ArabicTextNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_SpellingVariantsOfSameProduct_ProduceSameKey()
    {
        var variants = new[] { "بنادول اكسترا", "بَنادول إكسترا", "  بنـادول   أكسترا ", "بنادول اكستراّ" };

        variants.Select(ArabicTextNormalizer.Normalize).Distinct().Should().ContainSingle()
            .Which.Should().Be("بنادول اكسترا");
    }

    [Fact]
    public void Normalize_AlreadyNormalized_IsIdempotent()
    {
        var once = ArabicTextNormalizer.Normalize("  أَقْرَاصٌ PANADOL ٥٠٠ ");

        ArabicTextNormalizer.Normalize(once).Should().Be(once);
    }

    [Fact]
    public void Normalize_Null_Throws()
    {
        var act = () => ArabicTextNormalizer.Normalize(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
