using System.Text;
using System.Text.RegularExpressions;

namespace Stockly.Domain.Text;

/// <summary>
/// The cross-script comparison key stored in <c>products.latin_key</c> and <c>product_aliases.latin_key</c>
/// (docs/erd.md §1, fuzzy matching in docs/features/invoice-scan.md): a deterministic consonant skeleton in Latin letters,
/// so "بنادول اكسترا" and "Panadol Extra" both become <c>bndl kstr</c>. Arabic has no short vowels in writing, so vowels
/// and the long-vowel letters are dropped in both scripts and close sounds share one letter (p→b, v→f, j→g, q→k, ث→t,
/// Egyptian ج→g). Steps, in order (the SQL function <c>stockly_latin_key</c> of migration <c>MatchingAndImageLifecycle</c>
/// repeats them exactly and a test checks both agree):
/// <list type="number">
/// <item><see cref="ArabicTextNormalizer.Normalize"/>.</item>
/// <item>Latin digraphs: ph→f, sh→s, ch→s, th→t, kh→k, gh→g, ck→k, x→ks; c before e, i or y → s.</item>
/// <item>Letter map (<see cref="LetterMap"/>); vowels, و ي ا ء ع and their forms are dropped; other letters stay.</item>
/// <item>Anything but a-z and 0-9 becomes one space.</item>
/// <item>A repeated letter is kept once ("tt" → "t"; digits are kept as written).</item>
/// <item>A word-final h is dropped (ة/ه at the end of an Arabic word, silent h in Latin).</item>
/// <item>Spaces collapsed and trimmed.</item>
/// </list>
/// Pure and culture-independent. The result can be empty (a text of vowels only).
/// </summary>
public static partial class LatinKey
{
    /// <summary>Letter map of step 3 as (from, to) pairs; an empty "to" drops the letter. Order is irrelevant.</summary>
    public static readonly IReadOnlyDictionary<char, string> LetterMap = new Dictionary<char, string>
    {
        // Latin consonants (c is k here: c before e/i/y became s in step 2; x became ks).
        ['b'] = "b", ['c'] = "k", ['d'] = "d", ['f'] = "f", ['g'] = "g", ['h'] = "h", ['j'] = "g", ['k'] = "k", ['l'] = "l",
        ['m'] = "m", ['n'] = "n", ['p'] = "b", ['q'] = "k", ['r'] = "r", ['s'] = "s", ['t'] = "t", ['v'] = "f", ['z'] = "z",

        // Latin vowels and semi-vowels.
        ['a'] = "", ['e'] = "", ['i'] = "", ['o'] = "", ['u'] = "", ['w'] = "", ['y'] = "",

        // Arabic consonants (Egyptian readings: ج g, ث t, ذ ظ z, ق k).
        ['ب'] = "b", // ب
        ['ت'] = "t", // ت
        ['ث'] = "t", // ث
        ['ج'] = "g", // ج
        ['ح'] = "h", // ح
        ['خ'] = "k", // خ
        ['د'] = "d", // د
        ['ذ'] = "z", // ذ
        ['ر'] = "r", // ر
        ['ز'] = "z", // ز
        ['س'] = "s", // س
        ['ش'] = "s", // ش
        ['ص'] = "s", // ص
        ['ض'] = "d", // ض
        ['ط'] = "t", // ط
        ['ظ'] = "z", // ظ
        ['غ'] = "g", // غ
        ['ف'] = "f", // ف
        ['ق'] = "k", // ق
        ['ك'] = "k", // ك
        ['ک'] = "k", // ک (Persian kaf)
        ['ل'] = "l", // ل
        ['م'] = "m", // م
        ['ن'] = "n", // ن
        ['ه'] = "h", // ه
        ['ة'] = "h", // ة
        ['پ'] = "b", // پ
        ['چ'] = "s", // چ
        ['ڤ'] = "f", // ڤ
        ['گ'] = "g", // گ

        // Arabic long vowels, hamza forms and ain: dropped like Latin vowels.
        ['ا'] = "", // ا
        ['أ'] = "", // أ
        ['إ'] = "", // إ
        ['آ'] = "", // آ
        ['ٱ'] = "", // ٱ
        ['ء'] = "", // ء
        ['ؤ'] = "", // ؤ
        ['ئ'] = "", // ئ
        ['و'] = "", // و
        ['ى'] = "", // ى
        ['ي'] = "", // ي
        ['ی'] = "", // ی
        ['ع'] = "", // ع
    };

    private static readonly (string From, string To)[] Digraphs =
    [
        ("ph", "f"), ("sh", "s"), ("ch", "s"), ("th", "t"), ("kh", "k"), ("gh", "g"), ("ck", "k"), ("x", "ks"),
    ];

    /// <summary>The key of <paramref name="text"/> (raw or already normalized: normalizing twice changes nothing).</summary>
    public static string From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var value = ArabicTextNormalizer.Normalize(text);
        foreach (var (from, to) in Digraphs)
        {
            value = value.Replace(from, to, StringComparison.Ordinal);
        }

        value = SoftC().Replace(value, "s$1");

        var mapped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            mapped.Append(LetterMap.TryGetValue(c, out var to) ? to : c.ToString());
        }

        value = NotLetterOrDigit().Replace(mapped.ToString(), " ");
        value = RepeatedLetter().Replace(value, "$1");
        value = WordFinalH().Replace(value, "$1");
        return Spaces().Replace(value, " ").Trim();
    }

    [GeneratedRegex("c([eiy])", RegexOptions.CultureInvariant)]
    private static partial Regex SoftC();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotLetterOrDigit();

    [GeneratedRegex("([a-z])\\1+", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedLetter();

    [GeneratedRegex("h( |$)", RegexOptions.CultureInvariant)]
    private static partial Regex WordFinalH();

    [GeneratedRegex(" +", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
