using System.Text;
using AwesomeAssertions;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Util;
using PhoenixmlDb.XQuery.FullText;
using Xunit;

#pragma warning disable CA5394, CA1308 // a seeded generator for reproducible text; lowercase as the reference did

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The built-in analyzer replaced the Lucene.Net 4.8 chain that XQuery 3.1 and earlier used.
/// A host's full-text index built with the earlier release stays valid only if every token,
/// position and offset is the same, so these compare the two on the same text. The reference
/// is the chain as it was (StandardTokenizer, StandardFilter, EnglishPossessiveFilter,
/// LowerCaseFilter, StopFilter, PorterStemFilter), here in the tests only.
/// </summary>
/// <remarks>
/// Set PHOENIXML_FT_CORPUS to a file listing one path per line to compare a large corpus;
/// <see cref="Corpus_from_the_environment_has_no_differences"/> reports the count.
/// </remarks>
public sealed class LuceneParityTests
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public static readonly FullTextAnalysisOptions[] OptionSets =
    [
        FullTextAnalysisOptions.Default,
        new() { Stemming = false },
        new() { CaseSensitive = true },
        new() { NoStopWords = true },
        new() { StopWords = ["Walrus", "carpenter", "ÉTÉ"] },
        new() { Language = "de" },
        new() { Language = "en-GB", CaseSensitive = true, NoStopWords = true },
    ];

    internal static List<(string Term, int Position, int Start, int End)> Reference(string text, FullTextAnalysisOptions options)
    {
        using var analyzer = new ReferenceAnalyzer(options);
        using var reader = new StringReader(text);
        using var stream = analyzer.GetTokenStream("content", reader);
        var term = stream.AddAttribute<ICharTermAttribute>();
        var increment = stream.AddAttribute<IPositionIncrementAttribute>();
        var offset = stream.AddAttribute<IOffsetAttribute>();
        stream.Reset();
        var position = -1;
        var result = new List<(string, int, int, int)>();
        while (stream.IncrementToken())
        {
            position += increment.PositionIncrement;
            result.Add((term.ToString(), position, offset.StartOffset, offset.EndOffset));
        }
        stream.End();
        return result;
    }

    internal static List<(string Term, int Position, int Start, int End)> Ours(string text, FullTextAnalysisOptions options)
        => FullTextEngine.Analyze(text, options).Select(t => (t.Text, t.Position, t.StartOffset, t.EndOffset)).ToList();

    private sealed class ReferenceAnalyzer(FullTextAnalysisOptions options) : Analyzer
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership passes to TokenStreamComponents.")]
        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            var language = options.Language?.ToLowerInvariant();
            var english = language is null or "en" or "english" || language.StartsWith("en-", StringComparison.Ordinal);
            var stem = options.Stemming != false && english;
            var source = new StandardTokenizer(Version, reader);
            TokenStream stream = new StandardFilter(Version, source);
            if (stem)
                stream = new EnglishPossessiveFilter(Version, stream);
            if (options.CaseSensitive != true)
                stream = new LowerCaseFilter(Version, stream);
            var stopSet = options.NoStopWords
                ? null
                : options.StopWords is { } custom
                    ? new CharArraySet(Version, custom.ToList(), ignoreCase: true)
                    : EnglishAnalyzer.DefaultStopSet;
            if (stopSet != null)
                stream = new StopFilter(Version, stream, stopSet);
            if (stem)
                stream = new PorterStemFilter(stream);
            return new TokenStreamComponents(source, stream);
        }
    }

    /// <summary>The first difference, or null.</summary>
    internal static string? Difference(string text, FullTextAnalysisOptions options)
    {
        var expected = Reference(text, options);
        var actual = Ours(text, options);
        for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            var e = i < expected.Count ? expected[i].ToString() : "(none)";
            var a = i < actual.Count ? actual[i].ToString() : "(none)";
            if (e != a)
            {
                var at = i < expected.Count ? expected[i].Start : actual[i].Start;
                var context = text.Substring(Math.Max(0, at - 20), Math.Min(60, text.Length - Math.Max(0, at - 20)));
                return $"token {i}: Lucene {e}, ours {a}; near \"{context}\"";
            }
        }
        return null;
    }

    public static TheoryData<string> Samples => new()
    {
        "The walrus and the carpenter were walking close at hand; they wept like anything to see such quantities of sand.",
        "John's dog's bone, the boss’s office, JAMES'S car, O'Neil, rock'n'roll, can't, won't, I'd've.",
        "Prices: 3.14, 1,000,000, 12:30, 2026-11-17, v1.2.3, $5, 50%, A4, B-52, x86_64, foo_bar, _lead, trail_, __init__.",
        "e-mail user@example.com http://www.w3.org/TR/xpath-full-text-30/ #hashtag @mention C++ C# .NET",
        "Generalizations, relational, conditional, rational, valenci, hesitanci, digitizer, conformabli, radicalli, differentli, vileli, analogousli, vietnamization, predication, operator, feudalism, decisiveness, hopefulness, callousness, formaliti, sensitiviti, sensibiliti, triplicate, formative, formalize, electriciti, electrical, hopeful, goodness, revival, allowance, inference, airliner, gyroscopic, adjustable, defensible, irritant, replacement, adjustment, dependent, adoption, homologou, communism, activate, angulariti, homologous, effective, bowdlerize, probate, rate, cease, controll, roll, caresses, ponies, ties, caress, cats, feed, agreed, plastered, bled, motoring, sing, conflated, troubled, sized, hopping, tanned, falling, hissing, fizzed, failing, filing, happy, sky, archaeology, theology, generously, sensibly.",
        "Größe Straße naïve café résumé façade Ærøskøbing İstanbul ΣΊΣΥΦΟΣ Σίσυφος ﬁnance ǅemal",
        "Москва — столица России. Київ. Ελληνικά κείμενα. עברית: צה\"ל, ג'ירפה, ת״א. العربية ١٢٣. हिन्दी पाठ।",
        "中文文本测试，日本語のテキスト、カタカナ・コーヒー。ひらがな。한국어 텍스트입니다. ０１２３ ＡＢＣ",
        "ภาษาไทยไม่มีช่องว่างระหว่างคำ ພາສາລາວ ភាសាខ្មែរ မြန်မာဘာသာ",
        "Emoji 😀 test 👍🏽 flags 🇺🇸🇬🇧 math 𝐀𝐁𝐂 𝟏𝟐 gothic 𐌰𐌱 deseret 𐐀𐐨",
        "soft­hyphen zero​width joiner‍here combining é ä word⁠joiner tab\tnew\r\nline",
        "a.b a:b a'b 1.2 1,2 1'2 a.1 1.a a..b 1..2 'quoted' \"double\" ...dots... a_1 1_a カ_a a_カ カタ1",
        "",
        "   ",
        "the a an of",
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Matches_Lucene_on_samples(string text)
    {
        foreach (var options in OptionSets)
            Difference(text, options).Should().BeNull();
    }

    [Fact]
    public void Matches_Lucene_on_long_words()
    {
        foreach (var length in new[] { 254, 255, 256, 300, 600 })
        {
            var text = "start " + new string('x', length) + " end " + new string('9', length) + " " + string.Concat(Enumerable.Repeat("ab_", length / 3)) + " last";
            foreach (var options in OptionSets)
                Difference(text, options).Should().BeNull();
        }
    }

    /// <summary>
    /// Random text from one or two characters of every class the word-break rules use, so
    /// every pair and triple of classes is tried.
    /// </summary>
    [Fact]
    public void Matches_Lucene_on_random_text()
    {
        string[] alphabet =
        [
            "a", "Z", "é", "ß", "s", "S", "e", "y",          // ALetter
            "א", "ב",                                          // Hebrew_Letter
            "1", "٣", "０",                                     // Numeric, fullwidth digit
            "カ", "ー",                                         // Katakana
            "_", "‿",                                      // ExtendNumLet
            ":", "·",                                      // MidLetter
            ",", ";",                                           // MidNum
            ".", "’",                                      // MidNumLet
            "'", "\"",                                          // Single_Quote, Double_Quote
            "́", "­", "‍",                      // Extend, Format
            "中", "ひ", "ก", "ั", "한",                    // Han, Hiragana, Thai, Thai mark, Hangul
            " ", "-", "\n", "😀", "🇺", "𝐀",
        ];
        var random = new Random(551);
        var failures = new List<string>();
        for (var n = 0; n < 200000 && failures.Count < 20; n++)
        {
            var builder = new StringBuilder();
            var length = random.Next(1, 12);
            for (var i = 0; i < length; i++)
                builder.Append(alphabet[random.Next(alphabet.Length)]);
            var text = builder.ToString();
            foreach (var options in new[] { OptionSets[0], OptionSets[2] })
            {
                if (Difference(text, options) is { } difference)
                    failures.Add($"{Escape(text)}: {difference}");
            }
        }
        string.Join("\n", failures).Should().BeEmpty();
    }

    [Fact]
    public void Corpus_from_the_environment_has_no_differences()
    {
        var list = Environment.GetEnvironmentVariable("PHOENIXML_FT_CORPUS");
        if (string.IsNullOrEmpty(list))
            return;
        var report = Environment.GetEnvironmentVariable("PHOENIXML_FT_REPORT") ?? Path.ChangeExtension(list, ".report.txt");
        long files = 0, characters = 0, tokens = 0, differences = 0;
        using var output = new StreamWriter(report);
        foreach (var path in File.ReadLines(list))
        {
            if (!File.Exists(path))
                continue;
            var text = File.ReadAllText(path);
            files++;
            characters += text.Length;
            foreach (var options in OptionSets)
            {
                tokens += Reference(text, options).Count;
                if (Difference(text, options) is { } difference)
                {
                    differences++;
                    output.WriteLine($"{path} [{Describe(options)}]: {difference}");
                }
            }
        }
        output.WriteLine($"files={files} characters={characters} option-sets={OptionSets.Length} reference-tokens={tokens} files-with-a-difference={differences}");
        output.Flush();
        differences.Should().Be(0, $"see {report}");
    }

    private static string Describe(FullTextAnalysisOptions o)
        => $"stem={o.Stemming} lang={o.Language} case={o.CaseSensitive} nostop={o.NoStopWords} stop={(o.StopWords is null ? "-" : string.Join('|', o.StopWords))}";

    private static string Escape(string text)
        => string.Concat(text.EnumerateRunes().Select(r => r.Value < 0x80 && r.Value >= 0x20 ? r.ToString() : $"\\u{r.Value:X4}"));
}
