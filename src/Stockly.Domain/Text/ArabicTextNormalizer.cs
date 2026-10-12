using System.Text;

namespace Stockly.Domain.Text;

/// <summary>
/// Builds the search and matching key stored in <c>normalized_*</c> columns (docs/erd.md §1):
/// tashkeel, superscript alef and tatweel removed; أ إ آ ٱ → ا, ة → ه, ى → ي;
/// Arabic-Indic and Persian digits → 0-9; Latin lower-case (invariant); trimmed, whitespace collapsed.
/// Mixed scripts stay mixed.
/// </summary>
public static class ArabicTextNormalizer
{
    private const char FathatanFirstTashkeel = 'ً';
    private const char SukunLastTashkeel = 'ْ';
    private const char SuperscriptAlef = 'ٰ';
    private const char Tatweel = 'ـ';

    private const char Alef = 'ا';
    private const char AlefWithMaddaAbove = 'آ';
    private const char AlefWithHamzaAbove = 'أ';
    private const char AlefWithHamzaBelow = 'إ';
    private const char AlefWasla = 'ٱ';
    private const char TehMarbuta = 'ة';
    private const char Heh = 'ه';
    private const char AlefMaksura = 'ى';
    private const char Yeh = 'ي';

    private const char ArabicIndicZero = '٠';
    private const char ArabicIndicNine = '٩';
    private const char PersianZero = '۰';
    private const char PersianNine = '۹';

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var c in text)
        {
            if (IsRemoved(c))
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(Map(c));
        }

        return builder.ToString();
    }

    private static bool IsRemoved(char c) =>
        c is >= FathatanFirstTashkeel and <= SukunLastTashkeel or SuperscriptAlef or Tatweel;

    private static char Map(char c) => c switch
    {
        AlefWithHamzaAbove or AlefWithHamzaBelow or AlefWithMaddaAbove or AlefWasla => Alef,
        TehMarbuta => Heh,
        AlefMaksura => Yeh,
        >= ArabicIndicZero and <= ArabicIndicNine => (char)('0' + (c - ArabicIndicZero)),
        >= PersianZero and <= PersianNine => (char)('0' + (c - PersianZero)),
        _ => char.ToLowerInvariant(c),
    };
}
